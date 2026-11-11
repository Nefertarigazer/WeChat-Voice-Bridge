using System.Collections.Concurrent;
using VoiceBridge.Core;
using VoiceBridge.WeChat;
using static VoiceBridge.Tests.TestSupport;

namespace VoiceBridge.Tests;

public sealed class ReplyTests
{
    [Fact]
    public async Task RepliesAreReportedWithoutAcknowledgmentAndAreNeverSpoken()
    {
        await using var controller = new SessionController();
        var replies = new ConcurrentQueue<ReplySignal>();
        controller.Replied += replies.Enqueue;
        var source = new PythonFixtureSource(new(WatchMode.PollingOnly, 100, 30, []),
            PythonFixtureSource.ResolvePython(), Path.Combine(AppContext.BaseDirectory, "replies_worker.py"));
        var speech = new ProbeSpeech();
        controller.Start(DemoTarget, true, 30, false, () => source, speech, new ProbePlayer());
        await Until(() => controller.Status.State is BridgeState.Running or BridgeState.Error);
        Assert.True(controller.Status.State == BridgeState.Running, controller.Status.Error);
        await Until(() => replies.Count == 2 && speech.Calls.Count == 1);
        Assert.Equal(new[] { new ReplySignal(DemoTarget.ConversationId, 2000), new ReplySignal(DemoTarget.ConversationId, 2500) },
            replies.ToArray());
        Assert.Equal(new[] { "hello" }, speech.Calls.ToArray());
        await Task.Delay(200);
        Assert.Equal(BridgeState.Running, controller.Status.State);
        await controller.EndAsync().WaitAsync(Limit);
        Assert.Equal(BridgeState.Idle, controller.Status.State);
    }

    [Fact]
    public async Task RepliesFromAnotherChatOrAfterStopAreIgnored()
    {
        var source = new ProbeSource(); source.Ready.TrySetResult();
        await using var controller = new SessionController();
        var replies = new ConcurrentQueue<ReplySignal>();
        controller.Replied += replies.Enqueue;
        var speech = new ProbeSpeech();
        controller.Start(DemoTarget, true, 30, false, () => source, speech, new ProbePlayer());
        await Until(() => controller.Status.State == BridgeState.Running);
        source.Send(new("r1", DemoTarget.AccountId, "someone-else", Direction.Outgoing, false, "", 10));
        source.Send(new("r2", DemoTarget.AccountId, DemoTarget.ConversationId, Direction.Outgoing, false, "", 20));
        source.Send(Message("m1", "after"));
        await Until(() => speech.Calls.Count == 1);
        Assert.Equal(new[] { new ReplySignal(DemoTarget.ConversationId, 20) }, replies.ToArray());
        await controller.EndAsync().WaitAsync(Limit);
        Assert.Equal(Admission.Closed, source.LateDelivery!(new("r3", DemoTarget.AccountId,
            DemoTarget.ConversationId, Direction.Outgoing, false, "", 30)));
        Assert.Single(replies);
    }
}
