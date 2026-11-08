using System.Collections.Concurrent;
using System.Diagnostics;
using VoiceBridge.Core;
using VoiceBridge.WeChat;
using static VoiceBridge.Tests.TestSupport;

namespace VoiceBridge.Tests;

public sealed class MultiWorkerTests
{
    [Fact]
    public async Task OneWorkerConfirmsTwoContactsAndReleasesAllSessionResources()
    {
        await using var source = new PythonFixtureSource(WatchOptions.Default,
            PythonFixtureSource.ResolvePython(), Path.Combine(AppContext.BaseDirectory, "worker/main.py"),
            conversations: ["demo-peer", "demo-peer-2"]);
        using var cancellation = new CancellationTokenSource();
        await source.PrepareAsync(new(Guid.NewGuid(), 1), DemoTarget, cancellation.Token);
        Assert.True(source.Watching);
        using var worker = Process.GetProcessById(source.WorkerId!.Value);
        var directory = source.FixtureDirectory!;
        var received = new ConcurrentQueue<ChatMessage>();
        var running = source.RunAsync(message => { received.Enqueue(message); return Admission.Accepted; },
            cancellation.Token);
        try
        {
            Assert.Empty(received);
            for (var i = 0; i < 5; i++)
                source.RequestDemo();
            await Until(() => received.Count == 5);
            var messages = received.ToArray();
            Assert.Equal(["demo-peer", "demo-peer-2", "demo-peer", "demo-peer-2", "demo-peer"],
                messages.Select(message => message.ConversationId));
            Assert.Equal(5, messages.Select(message => message.Key).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(["你在干嘛呀？", "哈哈哈", "哈哈哈", "我听得到。", "等一下，我回来啦。"],
                messages.Select(message => message.Text));
        }
        finally
        {
            cancellation.Cancel();
            try { await running.WaitAsync(Limit); }
            catch (OperationCanceledException) { }
            await source.DisposeAsync();
        }
        Assert.True(worker.HasExited);
        Assert.False(source.Watching);
        Assert.False(Directory.Exists(directory));
        source.RequestDemo();
        Assert.Equal(5, received.Count);
    }

    [Theory]
    [InlineData("demo-peer")]
    [InlineData("filehelper")]
    [InlineData("room@chatroom")]
    public async Task InvalidSecondContactIsRejectedBeforeStartingAWorker(string second)
    {
        await using var source = new PythonFixtureSource(WatchOptions.Default,
            PythonFixtureSource.ResolvePython(), Path.Combine(AppContext.BaseDirectory, "worker/main.py"),
            conversations: ["demo-peer", second]);
        await Assert.ThrowsAsync<BridgeException>(() =>
            source.PrepareAsync(new(Guid.NewGuid(), 1), DemoTarget, CancellationToken.None));
        Assert.Null(source.WorkerId);
        Assert.False(source.Watching);
    }

    [Fact]
    public async Task LegacyWorkerCannotClaimReadinessForMoreThanOneContact()
    {
        await using var source = new PythonFixtureSource(WatchOptions.Default,
            PythonFixtureSource.ResolvePython(), Path.Combine(AppContext.BaseDirectory, "readings_worker.py"),
            rules: [new("demo", "legacy", true)],
            conversations: ["demo-peer", "demo-peer-2"]);
        var error = await Assert.ThrowsAsync<BridgeException>(() =>
            source.PrepareAsync(new(Guid.NewGuid(), 1), DemoTarget, CancellationToken.None));
        Assert.Contains("不支持多人", error.Message);
        using var child = Process.GetProcessById(source.WorkerId!.Value);
        var directory = source.FixtureDirectory!;
        await source.DisposeAsync();
        Assert.True(child.HasExited);
        Assert.False(Directory.Exists(directory));
    }
}
