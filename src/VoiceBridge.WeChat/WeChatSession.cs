using System.Diagnostics;
using VoiceBridge.Core;

namespace VoiceBridge.WeChat;

/// <summary>
/// One logged-in Windows WeChat account. The session only holds read paths and the
/// process handles used to locate the in-memory database key. It never writes to
/// the original WeChat files.
/// </summary>
public sealed record WeChatSession(string Root, string Account, string DirectoryPath,
                                   int[] Pids, DateTime ProcessStarted, string Version)
{
    /// <summary>
    /// Lower bound (inclusive) of passively-supported WeChat file versions.
    /// This is the start of the single Config.Cipher layout band; below it the
    /// plaintext key cache is gone and no audited upstream publishes passive
    /// extraction data (4.1.10.x zero residency). 4.1.11/4.1.15.99 are range-covered
    /// but not individually field-verified by us yet.
    /// </summary>
    public static readonly System.Version MinSupportedVersion = new Version(4, 1, 11);

    /// <summary>
    /// Upper bound (inclusive). 4.2.0.13 is the current latest client and shares the
    /// same Config.Cipher layout; beyond it no layout is published, so nothing is
    /// force-fitted.
    /// </summary>
    public static readonly System.Version MaxSupportedVersion = new Version(4, 2, 0, 13);

    /// <summary>
    /// Whether a parsed file version lies inside the supported passive range
    /// (4.1.11&nbsp;&lt;=&nbsp;v&nbsp;&lt;=&nbsp;4.2.0.13). Parsing must succeed; an
    /// empty or malformed version is not supported.
    /// </summary>
    public static bool IsSupportedVersion(System.Version? version)
        => version is not null
           && version >= MinSupportedVersion
           && version <= MaxSupportedVersion;

    /// <summary>
    /// Read the running Weixin client's file version without touching any database,
    /// process memory or account directory. Used by the host's version gate so an
    /// unsupported client is reported to the user instead of failing silently.
    /// </summary>
    public static (string? Raw, System.Version? Parsed) DetectClientVersion()
    {
        var processes = Process.GetProcessesByName("Weixin");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited) continue;
                    var raw = process.MainModule?.FileVersionInfo.FileVersion;
                    System.Version.TryParse(raw, out var parsed);
                    return (raw, parsed);
                }
                catch (Exception) { /* A process may exit between the enumeration and the read. */ }
            }
            return (null, null);
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static readonly string[] RootCandidates =
    [
        @"D:\xwechat_files",
        @"C:\xwechat_files",
        @"E:\xwechat_files",
        @"F:\xwechat_files",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "xwechat_files"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "WeChat Files"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "xwechat_files")
    ];

    /// <summary>Directory name that anchors an out-of-list data root.</summary>
    private const string RootAnchorName = "xwechat_files";

    /// <summary>Maximum directory depth below a drive root for the fallback scan.</summary>
    private const int ScanMaxDepth = 6;

    /// <summary>Total wall-clock budget for the fallback scan; stop and return null on expiry.</summary>
    private static readonly TimeSpan ScanBudget = TimeSpan.FromSeconds(20);

    /// <summary>Bind to a running WeChat client and resolve the account's message folder.</summary>
    public static WeChatSession Bind(string root, string account)
    {
        if (!Path.IsPathFullyQualified(root) || string.IsNullOrWhiteSpace(account) ||
            Path.GetFileName(account) != account || account is "." or "..")
            throw new BridgeException("请填写微信数据目录和账号目录名。");
        var directory = Path.Combine(root, account, "db_storage", "message");
        if (!Directory.Exists(directory))
            throw new BridgeException("找不到所选微信账号的消息目录。");
        var processes = Process.GetProcessesByName("Weixin");
        try
        {
            var running = processes.Where(p => !p.HasExited).OrderBy(p => p.StartTime).ToArray();
            if (running.Length == 0)
                throw new BridgeException("请先打开并登录 Windows 微信。");
            var module = running[0].MainModule;
            var fileVersion = module?.FileVersionInfo.FileVersion;
            System.Version.TryParse(fileVersion, out var parsed);
            // An unreadable module version is a hard refusal too: never fall back to
            // the old single-version check and never let a null Version reach ToString().
            if (module is null || !IsSupportedVersion(parsed))
                throw new BridgeException(
                    $"不支持此版本。真实微信模式当前支持微信 Windows {MinSupportedVersion} 至 " +
                    $"{MaxSupportedVersion}（含两端及其间全部小版本；4.1.11/4.1.15.99 为区间覆盖·待实测）。" +
                    $"检测到版本：{fileVersion ?? "未知"}。");
            var pids = running.Where(p => p.MainModule?.FileName == module.FileName).Select(p => p.Id).ToArray();
            return new(root, account, directory, pids, running[0].StartTime, parsed!.ToString());
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    /// <summary>Locate the WeChat data root without asking the user for a path.</summary>
    public static string? DiscoverRoot()
    {
        foreach (var candidate in RootCandidates)
            if (Directory.Exists(candidate) && DiscoverAccount(candidate) != null)
                return candidate;
        return DiscoverRootByScan();
    }

    /// <summary>
    /// Fallback: bounded recursive scan of every ready fixed drive for a directory named
    /// exactly <c>xwechat_files</c>. Depth and total time are capped; the first directory
    /// whose account structure verifies through <see cref="DiscoverAccount"/> wins.
    /// </summary>
    private static string? DiscoverRootByScan()
    {
        var deadline = Stopwatch.StartNew();
        foreach (var drive in EnumerateFixedDrives())
        {
            var found = ScanDirectory(drive, 0, deadline);
            if (found != null)
                return found;
            if (deadline.Elapsed >= ScanBudget)
                return null;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateFixedDrives()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch (Exception) { yield break; }

        foreach (var drive in drives)
        {
            string? root = null;
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                    root = drive.RootDirectory.FullName;
            }
            catch (Exception) { root = null; }
            if (root != null)
                yield return root;
        }
    }

    private static string? ScanDirectory(string path, int depth, Stopwatch deadline)
    {
        if (deadline.Elapsed >= ScanBudget)
            return null;

        if (Directory.Exists(path) &&
            string.Equals(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), RootAnchorName, StringComparison.OrdinalIgnoreCase) &&
            DiscoverAccount(path) != null)
            return path;

        if (depth >= ScanMaxDepth)
            return null;

        string[] children;
        try { children = Directory.GetDirectories(path); }
        catch (Exception) { return null; }

        foreach (var child in children)
        {
            if (deadline.Elapsed >= ScanBudget)
                return null;
            var found = ScanDirectory(child, depth + 1, deadline);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>Return the single account folder that holds a message database.</summary>
    public static string? DiscoverAccount(string root)
    {
        if (!Directory.Exists(root))
            return null;
        string? found = null;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (!Directory.Exists(Path.Combine(directory, "db_storage")))
                continue;
            found ??= Path.GetFileName(directory);
        }
        return found;
    }

    internal void CheckRunning()
    {
        try
        {
            using var process = Process.GetProcessById(Pids[0]);
            if (!process.HasExited && process.StartTime == ProcessStarted)
                return;
        }
        catch (ArgumentException) { }
        throw new BridgeException("微信进程已退出或重启，请重新开始监听。");
    }
}
