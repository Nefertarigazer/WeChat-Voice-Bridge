using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace VoiceBridge.WeChat;

/// <summary>
/// Keeps the worker's stderr in a local diagnostics file. A Python traceback
/// only carries file names and error messages, never the API key or the chat
/// text, so the file stays free of anything the user does not want stored.
/// </summary>
internal static class WorkerLog
{
    private const long MaxBytes = 128 * 1024;
    private const int MaxLines = 200;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WeChatVoiceBridge", "worker.log");

    public static async Task DrainAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                await AppendAsync(line).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The pipe closed with the worker.
        }
        catch (ObjectDisposedException)
        {
            // The reader was disposed while the worker was stopping.
        }
    }

    private static async Task AppendAsync(string line)
    {
        if (line.Length == 0)
            return;
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var encoding = new UTF8Encoding(false);
            await File.AppendAllTextAsync(path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}", encoding).ConfigureAwait(false);
            var info = new FileInfo(path);
            if (info.Length <= MaxBytes)
                return;
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            File.WriteAllLines(path, lines[Math.Max(0, lines.Length - MaxLines)..], encoding);
        }
        catch (IOException)
        {
            // A missing diagnostics file must never stop the bridge.
        }
        catch (UnauthorizedAccessException)
        {
            // The same applies to a read-only profile.
        }
        finally
        {
            Gate.Release();
        }
    }
}
