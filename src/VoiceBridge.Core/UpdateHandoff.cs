using System;
using System.IO;

namespace VoiceBridge.Core;

/// <summary>
/// Cross-process handshake between the update helper window and the restarted build.
/// <para>
/// The restarted build is launched during installation so a failed load can still
/// roll the files back, but it must not show its main window on its own. It writes a
/// "ready" signal once it has loaded, waits for the user to dismiss the helper window
/// (the "完成" button or the window close cross), and only then receives the "go"
/// signal and shows its main window.
/// </para>
/// </summary>
public sealed class UpdateHandoff
{
    public const string ArgumentName = "--await-handoff";

    private readonly string updates;

    private UpdateHandoff(string updates, string ticket)
    {
        this.updates = updates;
        Ticket = ticket;
    }

    public string Ticket { get; }

    private string ReadyPath => Path.Combine(updates, $"handoff-{Ticket}.ready");
    private string GoPath => Path.Combine(updates, $"handoff-{Ticket}.go");

    /// <summary>Only a 32-character hex Guid is accepted, so a ticket can never escape the updates directory.</summary>
    public static bool IsValidTicket(string? ticket) =>
        ticket is not null && Guid.TryParseExact(ticket, "N", out _);

    /// <summary>Creates a fresh handshake owned by the helper process.</summary>
    public static UpdateHandoff Create(string updates)
    {
        if (string.IsNullOrWhiteSpace(updates)) throw new ArgumentException("更新目录不能为空。", nameof(updates));
        return new(updates, Guid.NewGuid().ToString("N"));
    }

    /// <summary>Rebuilds the handshake in the restarted process from its command line.</summary>
    public static UpdateHandoff? FromArguments(string updates, string[] args)
    {
        if (string.IsNullOrWhiteSpace(updates) || args is null) return null;
        for (var i = 0; i + 1 < args.Length; i++)
            if (string.Equals(args[i], ArgumentName, StringComparison.Ordinal) && IsValidTicket(args[i + 1]))
                return new(updates, args[i + 1]);
        return null;
    }

    /// <summary>Extra arguments appended when the helper launches the restarted build.</summary>
    public string[] LaunchArguments => new[] { ArgumentName, Ticket };

    /// <summary>Called by the restarted build once its main window has been constructed.</summary>
    public void SignalReady() => Write(ReadyPath);

    /// <summary>Called by the helper when the user dismisses its window.</summary>
    public void SignalGo() => Write(GoPath);

    public bool IsReady() => File.Exists(ReadyPath);
    public bool IsGo() => File.Exists(GoPath);

    /// <summary>
    /// The contract the restarted build polls: its main window stays hidden until
    /// the user dismisses the installation window. Readiness alone never releases it.
    /// </summary>
    public bool ShouldShowMainWindow() => IsGo();

    /// <summary>Called by the restarted build after it shows its window. Never called by the helper,
    /// which exits before the restarted build has necessarily read the go signal.</summary>
    public void Cleanup()
    {
        TryDelete(ReadyPath);
        TryDelete(GoPath);
    }

    private void Write(string path)
    {
        Directory.CreateDirectory(updates);
        UpdateInstaller.EnsurePlainPath(path);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, "1");
        File.Move(temporary, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
