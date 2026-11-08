using System.IO.Compression;
using System.Security.Cryptography;

namespace VoiceBridge.Core;

public sealed record InstallationProgress(string Stage, int Percent);

public static class UpdateInstaller
{
    public const string ExecutableName = "WeChatVoiceBridge.exe";
    private const long MaxExpandedBytes = 2L * 1024 * 1024 * 1024;
    private const int MaxEntries = 20_000;
    private static readonly StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;

    public static string Prepare(string archive, UpdateManifest manifest, string installation, string updates,
        CancellationToken cancellationToken, IProgress<InstallationProgress>? progress = null)
    {
        ValidateInstallation(installation, updates);
        if (!IsDirectChild(archive, updates))
            throw new InvalidDataException("下载文件不在更新目录。");

        var stage = Path.Combine(updates, $"staging-{Guid.NewGuid():N}");
        try
        {
            progress?.Report(new("正在校验更新包", 0));
            EnsurePlainPath(archive);
            using var input = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length != manifest.Size ||
                !Convert.ToHexString(SHA256.HashData(input)).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装前复核失败，请重新下载更新包。");
            input.Position = 0;
            using var zip = new ZipArchive(input, ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxEntries || zip.Entries.Sum(entry => entry.Length) > MaxExpandedBytes)
                throw new InvalidDataException("更新包解压后超过大小上限。");

            Directory.CreateDirectory(stage);
            var processed = 0;
            var lastProgress = -1;
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = entry.FullName.Replace('\\', '/');
                var relative = name.TrimEnd('/');
                var target = PayloadPath(stage, relative);
                var attributes = entry.ExternalAttributes;
                if (((attributes >> 16) & 0xF000) == 0xA000 ||
                    (attributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("更新包包含链接。");
                if (name.EndsWith('/'))
                    Directory.CreateDirectory(target);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target);
                }
                var percent = ++processed * 100 / Math.Max(1, zip.Entries.Count);
                if (percent != lastProgress) { progress?.Report(new("正在解压安装文件", percent)); lastProgress = percent; }
            }
            if (!File.Exists(Path.Combine(stage, ExecutableName)))
                throw new InvalidDataException("更新包缺少主程序，无法安装。");
            return stage;
        }
        catch
        {
            if (Directory.Exists(stage))
                Directory.Delete(stage, recursive: true);
            throw;
        }
    }

    public static void Apply(string stage, string installation, string updates, string oldVersion, Action<string> restart,
        IProgress<InstallationProgress>? progress = null)
    {
        ValidateInstallation(installation, updates);
        ValidateStage(stage, updates);
        UpdateManifest.CompareVersions(oldVersion, oldVersion);

        var backup = Path.Combine(updates, $"backup-{oldVersion}", Guid.NewGuid().ToString("N"));
        EnsurePlainPath(backup);
        var exe = Path.Combine(installation, ExecutableName);
        var files = EnumeratePayload(stage, stage).Select(source =>
        {
            var relative = Path.GetRelativePath(stage, source);
            var target = PayloadPath(installation, relative);
            EnsurePlainPath(target);
            if (Directory.Exists(target))
                throw new IOException("程序目录中存在与更新文件同名的文件夹。");
            return (Source: source, Target: target, Backup: PayloadPath(backup, relative), Existed: File.Exists(target));
        }).OrderBy(file => file.Target.Equals(exe, PathComparison)).ToArray();

        if (!files.Any(file => file.Target.Equals(exe, PathComparison)))
            throw new InvalidDataException("暂存目录缺少主程序。");

        Directory.CreateDirectory(backup);
        progress?.Report(new("正在备份当前版本", 5));
        var oldFiles = files.Where(file => file.Existed).ToArray();
        var backedUp = 0;
        var lastProgress = -1;
        foreach (var file in oldFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file.Backup)!);
            File.Copy(file.Target, file.Backup);
            var percent = 5 + ++backedUp * 30 / Math.Max(1, oldFiles.Length);
            if (percent != lastProgress) { progress?.Report(new("正在备份当前版本", percent)); lastProgress = percent; }
        }

        var applied = new List<int>();
        var addedDirectories = new Stack<string>();
        try
        {
            for (var i = 0; i < files.Length; i++)
            {
                var file = files[i];
                CreateParents(Path.GetDirectoryName(file.Target)!, addedDirectories);
                ReplaceFile(file.Source, file.Target);
                applied.Add(i);
                var percent = 35 + (i + 1) * 55 / files.Length;
                if (percent != lastProgress) { progress?.Report(new("正在安装新版本", percent)); lastProgress = percent; }
            }
            progress?.Report(new("正在启动并确认新版本", 95));
            restart(exe);
        }
        catch (Exception installError)
        {
            progress?.Report(new("安装未完成，正在恢复原版本", 0));
            var rollbackErrors = new List<Exception>();
            foreach (var i in Enumerable.Reverse(applied))
            {
                var file = files[i];
                try
                {
                    if (file.Existed)
                        ReplaceFile(file.Backup, file.Target);
                    else
                        File.Delete(file.Target);
                }
                catch (Exception error) { rollbackErrors.Add(error); }
            }
            while (addedDirectories.TryPop(out var directory))
            {
                try { Directory.Delete(directory); }
                catch (Exception error) { rollbackErrors.Add(error); }
            }
            if (rollbackErrors.Count > 0)
                throw new AggregateException("安装失败，部分文件无法恢复，请从 updates 下的备份恢复。",
                    rollbackErrors.Prepend(installError));
            throw new IOException("安装失败，已恢复原程序文件。", installError);
        }
    }

    public static void ValidateStage(string stage, string updates)
    {
        var name = Path.GetFileName(stage);
        if (!IsDirectChild(stage, updates) || !name.StartsWith("staging-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[8..], "N", out _))
            throw new InvalidDataException("暂存目录无效。");
        EnsurePlainPath(stage);
    }

    public static void EnsurePlainPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("更新路径包含 Windows 链接或重解析点。");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void ValidateInstallation(string installation, string updates)
    {
        var data = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(updates)))!;
        var install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation));
        if (ContainsPath(data, install) || ContainsPath(install, data) ||
            !File.Exists(Path.Combine(install, ExecutableName)))
            throw new IOException("请在独立的程序目录中运行微信语音桥，再尝试更新。");
        EnsurePlainPath(Path.Combine(install, ExecutableName));
        EnsurePlainPath(updates);
    }

    private static bool ContainsPath(string directory, string path) =>
        path.Equals(directory, PathComparison) ||
        path.StartsWith(Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar, PathComparison);

    private static bool IsDirectChild(string path, string parent) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)), PathComparison);

    private static string PayloadPath(string root, string relative)
    {
        var parts = relative.Split('/', '\\');
        if (parts.Length > 32 || parts.Any(UnsafeName))
            throw new InvalidDataException("更新包包含无效或受保护的文件路径。");
        return Path.Combine(root, Path.Combine(parts));
    }

    private static bool UnsafeName(string name)
    {
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains(':') || name.Contains('~') || name.EndsWith('.') || name.EndsWith(' '))
            return true;
        var stem = name.Split('.')[0].ToLowerInvariant();
        return stem is "con" or "prn" or "aux" or "nul" or "conin$" or "conout$" or "settings" or "credentials" or "keys"
            || (stem.Length == 8 && stem.StartsWith("unins", StringComparison.Ordinal) && stem[5..].All(char.IsAsciiDigit))
            || (stem.Length == 4 && (stem.StartsWith("com", StringComparison.Ordinal) || stem.StartsWith("lpt", StringComparison.Ordinal))
                && "0123456789¹²³".Contains(stem[3]))
            || name.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumeratePayload(string root, string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            PayloadPath(root, Path.GetRelativePath(root, path));
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("暂存目录包含链接。");
            if ((attributes & FileAttributes.Directory) == 0)
                yield return path;
            else
                foreach (var child in EnumeratePayload(root, path))
                    yield return child;
        }
    }

    private static void CreateParents(string directory, Stack<string> added)
    {
        if (Directory.Exists(directory))
            return;
        CreateParents(Path.GetDirectoryName(directory)!, added);
        Directory.CreateDirectory(directory);
        added.Push(directory);
    }

    private static void ReplaceFile(string source, string target)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".wvb-{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(source, temporary);
            File.Move(temporary, target, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
