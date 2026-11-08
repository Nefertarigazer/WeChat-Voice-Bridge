using System.Diagnostics;
using VoiceBridge.Core;

namespace VoiceBridge.WeChat;

/// <summary>
/// One logged-in Windows WeChat account. The session only holds read paths and the
/// process handles used to locate the in-memory database key. It never writes to
/// the original WeChat files.
/// </summary>
public sealed record WeChatSession(string Root, string Account, string DirectoryPath, int[] Pids, DateTime ProcessStarted)
{
    public const string SupportedVersion = "4.2.0.13";

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
            if (module?.FileVersionInfo.FileVersion != SupportedVersion)
                throw new BridgeException($"真实微信模式目前支持 {SupportedVersion}。");
            var pids = running.Where(p => p.MainModule?.FileName == module.FileName).Select(p => p.Id).ToArray();
            return new(root, account, directory, pids, running[0].StartTime);
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    /// <summary>Locate the WeChat data root without asking the user for a path.</summary>
    public static string? DiscoverRoot()
    {
        foreach (var candidate in RootCandidates)
            if (Directory.Exists(candidate) && DiscoverAccount(candidate) != null)
                return candidate;
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
