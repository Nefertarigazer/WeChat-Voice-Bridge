using System.Diagnostics;
using System.Text;
using System.Text.Json;
using VoiceBridge.Core;

namespace VoiceBridge.WeChat;

/// <summary>
/// Runs worker/edge_speak.py, the bundled edge-tts helper, as one long-lived child
/// of the app. Loading edge-tts takes seconds, so the process is started once
/// (or warmed early) and then answers one request at a time over JSON lines.
/// It never touches WeChat data; it is bound to the app through a Job Object so
/// it ends with the app, and a timed-out request kills it to keep the protocol
/// in step — the next request starts a fresh copy.
/// </summary>
public sealed class EdgeSpeechHost : IDisposable
{
    private const int MaxReplyChars = 24 * 1024 * 1024;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string scriptPath;
    private Process? process;
    private WorkerJob? job;
    private bool disposed;

    /// <param name="scriptPath">Only tests point this elsewhere; the app uses its own worker folder.</param>
    public EdgeSpeechHost(string? scriptPath = null) =>
        this.scriptPath = scriptPath ?? Path.Combine(AppContext.BaseDirectory, "worker", "edge_speak.py");

    /// <summary>Whether this build carries the helper at all (edge-tts sits next to the script).</summary>
    public bool Available =>
        File.Exists(PythonFixtureSource.ResolvePython()) && File.Exists(scriptPath) &&
        File.Exists(Path.Combine(Path.GetDirectoryName(scriptPath)!, "tts-deps", "edge_tts", "__init__.py"));

    /// <summary>Load edge-tts in the background so the first message does not wait for it.</summary>
    public void Warm()
    {
        if (!Available) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await gate.WaitAsync();
                try { if (!disposed) await EnsureStartedAsync(CancellationToken.None); }
                finally { gate.Release(); }
            }
            catch (Exception) { /* The first real request reports any problem. */ }
        });
    }

    public async Task<byte[]> SpeakAsync(string text, string voice, double speed, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Available)
            throw new BridgeException("这个版本缺少 Edge 朗读组件，请重新安装软件。");
        await gate.WaitAsync(token);
        try
        {
            var child = await EnsureStartedAsync(token);
            var request = JsonSerializer.Serialize(new { text, voice, speed });
            try
            {
                await child.StandardInput.WriteLineAsync(request.AsMemory(), token);
                await child.StandardInput.FlushAsync(token);
                var line = await child.StandardOutput.ReadLineAsync(token);
                if (line is null)
                {
                    Stop();
                    throw new BridgeException("Edge 朗读组件意外退出，本条已跳过。");
                }
                if (line.Length > MaxReplyChars)
                {
                    Stop();
                    throw new BridgeException("Edge 朗读返回的音频过大。");
                }
                return Decode(line);
            }
            catch (OperationCanceledException)
            {
                // The helper is still busy with this request: drop it so the next
                // reply cannot be mistaken for this one.
                Stop();
                throw;
            }
            catch (IOException)
            {
                Stop();
                throw new BridgeException("Edge 朗读组件意外退出，本条已跳过。");
            }
        }
        finally { gate.Release(); }
    }

    private static byte[] Decode(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True &&
            root.TryGetProperty("audio", out var audio) && audio.GetString() is { Length: > 0 } encoded)
            return Convert.FromBase64String(encoded);
        var code = root.TryGetProperty("code", out var value) ? value.GetString() : null;
        throw new BridgeException(code switch
        {
            "request" => "Edge 朗读拒绝了这条请求，请检查音色名称。",
            "no-audio" => "Edge 朗读没有返回声音，可能是音色名称不对或服务暂时不可用。",
            "too-large" => "Edge 朗读返回的音频过大。",
            "dependency" => "这个版本缺少 Edge 朗读组件，请重新安装软件。",
            _ => "连不上 Edge 朗读服务（非官方接口，可能被限流或已变更），本条已跳过。"
        });
    }

    private async Task<Process> EnsureStartedAsync(CancellationToken token)
    {
        if (process is { HasExited: false } running)
            return running;
        Stop();
        var info = new ProcessStartInfo(PythonFixtureSource.ResolvePython())
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(scriptPath)!
        };
        info.ArgumentList.Add("-I");
        info.ArgumentList.Add("-B");
        info.ArgumentList.Add(scriptPath);
        job = new WorkerJob();
        var child = Process.Start(info) ?? throw new BridgeException("无法启动 Edge 朗读组件。");
        try { job.Attach(child); }
        catch (Exception) { /* Lifetime binding is best effort; Dispose still stops it. */ }
        process = child;
        // Stderr only carries short failure codes; drain it so the pipe never fills.
        _ = Task.Run(async () =>
        {
            try { while (await child.StandardError.ReadLineAsync() is not null) { } }
            catch (Exception) { }
        });
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
        startup.CancelAfter(TimeSpan.FromSeconds(30));
        string? first;
        try { first = await child.StandardOutput.ReadLineAsync(startup.Token); }
        catch (OperationCanceledException)
        {
            Stop();
            if (token.IsCancellationRequested) throw;
            throw new BridgeException("Edge 朗读组件启动超时，本条已跳过。");
        }
        if (first is null || !first.Contains("\"ready\"", StringComparison.Ordinal))
        {
            Stop();
            throw new BridgeException(first is not null && first.Contains("dependency", StringComparison.Ordinal)
                ? "这个版本缺少 Edge 朗读组件，请重新安装软件。" : "Edge 朗读组件启动失败。");
        }
        return child;
    }

    private void Stop()
    {
        var child = process;
        process = null;
        if (child is not null)
        {
            try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
            catch (Exception) { }
            child.Dispose();
        }
        job?.Dispose();
        job = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
        gate.Dispose();
    }
}
