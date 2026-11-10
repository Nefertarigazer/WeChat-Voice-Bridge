using System;
using System.IO;
using System.Text;

namespace VoiceBridge.App;

/// <summary>
/// A tiny diagnostics file for housekeeping events such as the credential
/// sweep. Only counts and event names are written — never credential names or
/// values — and a failed write must never stop the bridge.
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 128 * 1024;
    private const int MaxLines = 200;

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WeChatVoiceBridge", "app.log");

    public static void Write(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var encoding = new UTF8Encoding(false);
            File.AppendAllText(path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}", encoding);
            var info = new FileInfo(path);
            if (info.Length <= MaxBytes)
                return;
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            File.WriteAllLines(path, lines[Math.Max(0, lines.Length - MaxLines)..], encoding);
        }
        catch (IOException)
        {
            // A missing diagnostics file must never stop the app.
        }
        catch (UnauthorizedAccessException)
        {
            // The same applies to a read-only profile.
        }
    }
}
