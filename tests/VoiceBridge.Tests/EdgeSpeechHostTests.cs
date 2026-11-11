using VoiceBridge.Core;
using VoiceBridge.WeChat;

namespace VoiceBridge.Tests;

public sealed class EdgeSpeechHostTests
{
    // Speaks the helper's line protocol without any network: a canned frame for one
    // voice, a refused request for another, and a hang for a third.
    private const string FakeHelper = """
        import base64, json, sys, time
        sys.stdout.buffer.write(b'{"ready": true}\n'); sys.stdout.buffer.flush()
        frame = bytes([255, 251, 144]) + bytes(414)
        while True:
            line = sys.stdin.buffer.readline()
            if not line:
                break
            request = json.loads(line)
            if request["voice"] == "hang-Voice":
                time.sleep(60)
            reply = {"ok": True, "audio": base64.b64encode(frame).decode()} if request["voice"] == "zh-CN-XiaoxiaoNeural" \
                else {"ok": False, "code": "network"}
            sys.stdout.buffer.write(json.dumps(reply).encode() + b"\n"); sys.stdout.buffer.flush()
        """;

    private static string Helper(string? source = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "wvb-edge-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "tts-deps", "edge_tts"));
        File.WriteAllText(Path.Combine(folder, "tts-deps", "edge_tts", "__init__.py"), "");
        var script = Path.Combine(folder, "edge_speak.py");
        File.WriteAllText(script, source ?? FakeHelper);
        return script;
    }

    [Fact]
    public async Task OneHelperAnswersRequestsInTurnAndExplainsFailures()
    {
        var script = Helper();
        using var host = new EdgeSpeechHost(script);
        Assert.True(host.Available);
        var first = await host.SpeakAsync("你好", "zh-CN-XiaoxiaoNeural", 1, CancellationToken.None);
        var second = await host.SpeakAsync("再见", "zh-CN-XiaoxiaoNeural", 1.2, CancellationToken.None);
        Assert.Equal(417, first.Length);
        Assert.Equal(first, second);
        var failure = await Assert.ThrowsAsync<BridgeException>(() =>
            host.SpeakAsync("你好", "zh-CN-YunxiNeural", 1, CancellationToken.None));
        Assert.Contains("Edge", failure.Message);
        Assert.DoesNotContain("你好", failure.Message);
        // The helper is still in step after a failure.
        Assert.Equal(417, (await host.SpeakAsync("你好", "zh-CN-XiaoxiaoNeural", 1, CancellationToken.None)).Length);
    }

    [Fact]
    public async Task ACancelledRequestRestartsTheHelperInsteadOfReadingAStaleReply()
    {
        var script = Helper();
        using var host = new EdgeSpeechHost(script);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.SpeakAsync("你好", "hang-Voice", 1, stop.Token));
        var audio = await host.SpeakAsync("你好", "zh-CN-XiaoxiaoNeural", 1, CancellationToken.None);
        Assert.Equal(417, audio.Length);
    }

    [Fact]
    public void ABuildWithoutEdgeTtsSaysSo()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wvb-edge-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "edge_speak.py"), "");
        using var host = new EdgeSpeechHost(Path.Combine(folder, "edge_speak.py"));
        Assert.False(host.Available);
    }

    /// <summary>
    /// Real network call through the shipped helper and edge-tts. Run on demand:
    /// set WVB_TTS_DEPS to an extracted tts-deps folder and filter Category=Network.
    /// </summary>
    [Trait("Category", "Network")]
    [Fact]
    public async Task TheShippedHelperSpeaksChineseThroughEdge()
    {
        var deps = Environment.GetEnvironmentVariable("WVB_TTS_DEPS");
        Assert.False(string.IsNullOrWhiteSpace(deps), "Set WVB_TTS_DEPS to an extracted tts-deps folder.");
        var folder = Path.Combine(Path.GetTempPath(), "wvb-edge-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "worker", "edge_speak.py"), Path.Combine(folder, "edge_speak.py"));
        CopyDirectory(deps!, Path.Combine(folder, "tts-deps"));
        using var host = new EdgeSpeechHost(Path.Combine(folder, "edge_speak.py"));
        var audio = await host.SpeakAsync("你好，这是一条测试消息。", "zh-CN-XiaoxiaoNeural", 1, CancellationToken.None);
        Assert.True(audio.Length > 1000);
        Assert.Equal(255, audio[0]);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await host.SpeakAsync("第二条消息复用同一个进程。", "zh-CN-YunxiNeural", 1.2, CancellationToken.None);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), timer.Elapsed.ToString());
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(from, to, StringComparison.OrdinalIgnoreCase));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(from, to, StringComparison.OrdinalIgnoreCase), true);
    }
}
