using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using VoiceBridge.Core;

namespace VoiceBridge.App;

internal static class UpdateProcess
{
    internal static readonly string CurrentVersion = typeof(UpdateProcess).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    internal static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeChatVoiceBridge", "updates");

    private sealed record HelperStatus(bool Ready, string? Error);

    internal static async Task StartAsync(string stage, CancellationToken cancellationToken)
    {
        UpdateInstaller.ValidateStage(stage, DirectoryPath);
        var exe = Path.Combine(stage, UpdateInstaller.ExecutableName);
        using var parent = Process.GetCurrentProcess();
        if (!string.Equals(Path.GetFileName(parent.MainModule?.FileName), UpdateInstaller.ExecutableName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请从发布目录运行微信语音桥，再安装更新。");
        var ticket = Guid.NewGuid().ToString("N");
        var statusFile = HelperStatusPath(ticket);
        using var helper = Launch(exe, DirectoryPath, "--apply-update", parent.Id.ToString(CultureInfo.InvariantCulture),
            ProcessStamp(parent), CurrentVersion, Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), ticket);
        try
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(15))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(statusFile))
                {
                    var status = JsonSerializer.Deserialize<HelperStatus>(await File.ReadAllTextAsync(statusFile, cancellationToken));
                    if (status is { Ready: true }) return;
                    throw new IOException(status?.Error ?? "更新助手未能准备就绪。");
                }
                if (helper.HasExited) throw new IOException($"更新助手提前退出（代码 {helper.ExitCode}），当前版本和下载包已保留。");
                await Task.Delay(100, cancellationToken);
            }
            throw new TimeoutException("更新助手启动超时。当前版本和下载包已保留，请重试。");
        }
        catch
        {
            if (!helper.HasExited) helper.Kill(entireProcessTree: true);
            throw;
        }
        finally { try { File.Delete(statusFile); } catch (IOException) { } }
    }

    internal static void ValidatePackageVersion(string stage, string expectedVersion)
    {
        UpdateInstaller.ValidateStage(stage, DirectoryPath);
        var exe = Path.Combine(stage, UpdateInstaller.ExecutableName);
        var productVersion = FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "";
        if (UpdateManifest.CompareVersions(productVersion, expectedVersion) != 0)
            throw new InvalidDataException("更新包内的程序版本与清单不符。");
    }

    internal static int Apply(string[] args, UpdateHandoff handoff,
        IProgress<InstallationProgress>? progress = null, Action<string>? reportFailure = null)
    {
        // Five arguments remain compatible with already released clients.
        if ((args.Length != 5 && args.Length != 6) || !int.TryParse(args[1], out var parentId) ||
            (args.Length == 6 && !Guid.TryParseExact(args[5], "N", out _)))
            return 1;

        var stage = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var installation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[4]));
        var installedExe = Path.Combine(installation, UpdateInstaller.ExecutableName);
        var oldVersion = args[3];
        var canRestart = false;
        try
        {
            UpdateInstaller.ValidateStage(stage, DirectoryPath);
            if (UpdateManifest.CompareVersions(CurrentVersion, oldVersion) <= 0)
                throw new InvalidDataException("更新程序的版本无效。");
            using var parent = FindProcess(parentId, args[2]);
            if (parent is not null && !IsImage(parent, installedExe))
                throw new IOException("无法确认待更新的程序进程。");

            UpdateInstaller.EnsurePlainPath(Path.Combine(DirectoryPath, "apply.lock"));
            using var updateLock = new FileStream(Path.Combine(DirectoryPath, "apply.lock"), FileMode.OpenOrCreate,
                FileAccess.Write, FileShare.None);
            UpdateInstaller.EnsurePlainPath(installedExe);
            var installedVersion = FileVersionInfo.GetVersionInfo(installedExe).ProductVersion ?? "";
            if (UpdateManifest.CompareVersions(installedVersion, oldVersion) != 0)
                throw new IOException("当前程序版本已改变，请重新检查更新。");
            // Surface missing directory permissions before the UI agrees to exit.
            var probe = Path.Combine(installation, $".wvb-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            File.Delete(probe);
            progress?.Report(new("正在等待主程序安全退出", 0));
            ReportHelper(args, new(true, null));
            if (parent is not null && !parent.WaitForExit(45_000))
                throw new IOException("主程序尚未退出，未替换任何文件。请结束监听后重试。");
            canRestart = true;
            var others = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(installedExe));
            try
            {
                if (others.Any(process => IsImage(process, installedExe)))
                    throw new IOException("另一个微信语音桥实例仍在运行，请关闭后重试。");
            }
            finally
            {
                foreach (var process in others)
                    process.Dispose();
            }

            var confirmed = false;
            UpdateInstaller.Apply(stage, installation, DirectoryPath, oldVersion,
                exe => confirmed = Restart(exe, stage, "installed", handoff), progress);
            if (!confirmed)
            {
                // Do not release the restarted build here. Even on this degraded
                // path the new window must wait until the user dismisses the
                // installation window, which writes the "go" signal on close.
                reportFailure?.Invoke("新版本文件已安装，但暂未确认启动成功。点「关闭」后会打开新版本；若仍未出现，请检查任务栏或手动启动微信语音桥。更新包和备份已保留。");
                return 4;
            }
            progress?.Report(new("更新完成，等待确认后打开新版本", 100));
            return 0;
        }
        catch (AggregateException error)
        {
            ReportHelper(args, new(false, error.Message));
            reportFailure?.Invoke(error.Message);
            // Preserve the stage and backup for recovery if rollback itself failed.
            return 3;
        }
        catch (Exception error)
        {
            ReportHelper(args, new(false, $"更新未完成：{error.Message}"));
            reportFailure?.Invoke($"更新未完成：{error.Message}" + (error.InnerException == null ? "" : $"\n原因：{error.InnerException.Message}"));
            if (canRestart)
            {
                try { Restart(installedExe, stage, "failed"); }
                catch (Exception) { return 3; }
            }
            return 2;
        }
    }

    private static string HelperStatusPath(string ticket)
    {
        var path = Path.Combine(DirectoryPath, $"launch-{ticket}.json");
        UpdateInstaller.EnsurePlainPath(path);
        return path;
    }

    private static void ReportHelper(string[] args, HelperStatus status)
    {
        if (args.Length != 6) return;
        try
        {
            var path = HelperStatusPath(args[5]);
            UpdateInstaller.EnsurePlainPath(path + ".tmp");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(status));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception) { /* The parent also detects early exit and startup timeout. */ }
    }

    /// <summary>True while the installation helper process that launched this build is still alive.</summary>
    internal static bool HelperStillRunning(string[] args)
    {
        if (args.Length < 4 || args[0] != "--update-result" || !int.TryParse(args[2], out var helperId))
            return false;
        try
        {
            using var helper = FindProcess(helperId, args[3]);
            return helper is not null && !helper.HasExited;
        }
        catch (Exception) { return false; }
    }

    internal static string? ReadRestartNotice(string[] args)
    {
        // Five arguments come from an older helper; seven carry the extra
        // --await-handoff <ticket> pair used by this build.
        if ((args.Length != 5 && args.Length != 7) || args[0] != "--update-result" ||
            !int.TryParse(args[2], out var helperId))
            return null;

        var stage = Path.Combine(DirectoryPath, args[1]);
        try { UpdateInstaller.ValidateStage(stage, DirectoryPath); }
        catch (Exception) { return null; }

        if (args[4] == "installed") _ = Task.Run(() =>
        {
            try
            {
                using var helper = FindProcess(helperId, args[3]);
                if (helper is null || (IsImage(helper, Path.Combine(stage, UpdateInstaller.ExecutableName)) &&
                    helper.WaitForExit(25_000)))
                    Directory.Delete(stage, recursive: true);
            }
            catch (Exception) { /* Staging is disposable and can be removed later. */ }
        });

        return args[4] == "installed" ? $"已更新到 v{CurrentVersion}。"
            : "更新未完成，已重新打开原版本。请检查程序目录权限后重试。";
    }

    private static bool Restart(string exe, string stage, string result, UpdateHandoff? handoff = null)
    {
        using var helper = Process.GetCurrentProcess();
        var args = new List<string> { "--update-result", Path.GetFileName(stage),
            helper.Id.ToString(CultureInfo.InvariantCulture), ProcessStamp(helper), result };
        // Only a successful install is asked to wait. A rolled-back original build
        // knows nothing about the handshake and must start normally.
        if (result == "installed" && handoff is not null)
            args.AddRange(handoff.LaunchArguments);
        using var process = Launch(exe, Path.GetDirectoryName(exe)!, args.ToArray());
        if (result != "installed")
            return true;

        // Detect a failed load before committing the update. The restarted build
        // signals readiness once its main window has been constructed, but does NOT
        // show it until the user dismisses the installation window. If it never
        // becomes ready we leave its files in place instead of killing it.
        var started = Stopwatch.StartNew();
        while (started.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (process.WaitForExit(100))
                throw new IOException("新版程序未能启动。");
            if (handoff is not null && handoff.IsReady())
                return true;
            if (handoff is null)
            {
                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero)
                    return true;
            }
        }
        return false;
    }

    private static Process Launch(string executable, string workingDirectory, params string[] args)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new IOException("无法启动更新程序。");
    }

    private static Process? FindProcess(int id, string stamp)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(id);
            if (ProcessStamp(process) == stamp)
                return process;
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        process?.Dispose();
        return null;
    }

    private static string ProcessStamp(Process process) =>
        process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);

    private static bool IsImage(Process process, string executable)
    {
        try { return string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase); }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}
