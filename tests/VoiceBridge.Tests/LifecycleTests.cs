using System.Collections.Concurrent;
using VoiceBridge.Core;
using static VoiceBridge.Tests.TestSupport;

namespace VoiceBridge.Tests;

public sealed class LifecycleTests
{
    [Fact]
    public async Task StartsIdleAndAllowsOnlyOneStartingSession()
    {
        await using var controller = new SessionController();
        var source = new ProbeSource();
        var calls = 0;
        Func<IMessageSource> factory = () => { Interlocked.Increment(ref calls); return source; };
        Assert.Equal(BridgeState.Idle, controller.Status.State);
        Assert.Null(controller.ActiveStamp);
        Assert.Equal(0, calls);
        Assert.Throws<BridgeException>(() => controller.Start(DemoTarget, false, 30, false, factory, new ProbeSpeech(), new ProbePlayer()));
        Assert.Equal(0, calls);
        Assert.True(controller.Start(DemoTarget, true, 30, false, factory, new ProbeSpeech(), new ProbePlayer()));
        await source.Entered.Task.WaitAsync(Limit);
        Assert.Equal(BridgeState.Starting, controller.Status.State);
        Assert.False(controller.Start(DemoTarget, true, 30, false, factory, new ProbeSpeech(), new ProbePlayer()));
        Assert.Equal(1, calls);
        source.Ready.SetResult();
        await Until(() => controller.Status.State == BridgeState.Running);
    }

    [Fact]
    public async Task StartupFailureRollsBackAndCanReset()
    {
        await using var controller = new SessionController();
        var source = new ProbeSource();
        controller.Start(DemoTarget, true, 30, false, () => source, new ProbeSpeech(), new ProbePlayer());
        await source.Entered.Task.WaitAsync(Limit);
        source.Ready.SetException(new BridgeException("fixture failure"));
        await Until(() => controller.Status.State == BridgeState.Error);
        Assert.True(source.Stopped.Task.IsCompletedSuccessfully);
        Assert.Equal("fixture failure", controller.Status.Error);
        Assert.True(controller.Reset());
        Assert.Equal(BridgeState.Idle, controller.Status.State);
    }

    [Fact]
    public async Task StopInvalidatesOldResultsBeforeWaitingForCleanup()
    {
        await using var controller = new SessionController();
        var source = new ProbeSource();
        var response = new TaskCompletionSource<AudioData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = ProbeSource.NewSignal();
        var speech = new ProbeSpeech(async (_, token) =>
        {
            using var registration = token.Register(() => cancellation.TrySetResult());
            return await response.Task;
        });
        var player = new ProbePlayer();
        controller.Start(DemoTarget, true, 30, false, () => source, speech, player);
        source.Ready.SetResult();
        await Until(() => source.LateDelivery != null);
        source.Send(Message("1"));
        await Until(() => speech.Calls.Count == 1);
        source.Send(Message("2"));
        await Until(() => controller.Status.Pending == 1);
        var old = controller.ActiveStamp;
        var ending = controller.EndAsync();
        Assert.Same(ending, controller.EndAsync());
        Assert.Equal(BridgeState.Stopping, controller.Status.State);
        Assert.Equal(0, controller.Status.Pending);
        Assert.Equal(Admission.Closed, source.LateDelivery!(Message("3")));
        await cancellation.Task.WaitAsync(Limit);
        response.SetResult(new([]));
        await ending.WaitAsync(Limit);
        Assert.Equal(0, player.Played);
        Assert.True(source.Stopped.Task.IsCompletedSuccessfully);

        var restarted = new ProbeSource();
        controller.Start(DemoTarget, true, 30, false, () => restarted, speech, player);
        restarted.Ready.SetResult();
        await Until(() => restarted.LateDelivery != null);
        Assert.NotEqual(old!.Value.Id, controller.ActiveStamp!.Value.Id);
        Assert.True(controller.ActiveStamp.Value.Generation > old.Value.Generation);
        Assert.Equal(Admission.Closed, source.LateDelivery(Message("4")));
        restarted.Send(Message("1", "新会话"));
        await Until(() => player.Played == 1);
        Assert.Equal(new[] { "哈哈哈", "新会话" }, speech.Calls.ToArray());
    }

    [Fact]
    public async Task DisposeDuringPlaybackStopsAudioAndDisallowsRestart()
    {
        var controller = new SessionController();
        var source = new ProbeSource();
        var player = new ProbePlayer(block: true);
        controller.Start(DemoTarget, true, 30, false, () => source, new ProbeSpeech(), player);
        source.Ready.SetResult();
        source.Send(Message("1"));
        await player.Playing.Task.WaitAsync(Limit);
        await controller.DisposeAsync().AsTask().WaitAsync(Limit);
        Assert.True(player.Stopped > 0);
        Assert.True(source.Stopped.Task.IsCompletedSuccessfully);
        Assert.False(controller.Start(DemoTarget, true, 30, false, () => source, new ProbeSpeech(), player));
    }

    [Fact]
    public async Task StopDuringPreparationCancelsAndReleasesResources()
    {
        await using var controller = new SessionController();
        var source = new ProbeSource();
        controller.Start(DemoTarget, true, 30, false, () => source, new ProbeSpeech(), new ProbePlayer());
        await source.Entered.Task.WaitAsync(Limit);
        await controller.EndAsync().WaitAsync(Limit);
        Assert.Equal(BridgeState.Idle, controller.Status.State);
        Assert.True(source.Stopped.Task.IsCompletedSuccessfully);
        Assert.Null(source.LateDelivery);
    }

    [Fact]
    public async Task QueueOverflowAndClearAreVisibleAndSkipCancelsCurrent()
    {
        await using var controller = new SessionController();
        var source = new ProbeSource();
        var player = new ProbePlayer(block: true);
        var trace = new ConcurrentQueue<ProcessingEvent>();
        controller.Processed += trace.Enqueue;
        // The look-ahead takes the next message off the queue while the current one plays.
        // Wait for it, so the single queue slot is decided by this test and not by the thread pool.
        var lookAhead = ProbeSource.NewSignal();
        var speech = new ProbeSpeech((text, _) =>
        {
            if (text == "second") lookAhead.TrySetResult();
            return Task.FromResult(new AudioData([]));
        });
        controller.Start(DemoTarget, true, 1, false, () => source, speech, player);
        source.Ready.SetResult();
        source.Send(Message("1", "first"));
        await player.Playing.Task.WaitAsync(Limit);
        Assert.Equal(Admission.Accepted, source.LateDelivery!(Message("2", "second")));
        await lookAhead.Task.WaitAsync(Limit);
        Assert.Equal(Admission.Accepted, source.LateDelivery(Message("3", "third")));
        Assert.Equal(Admission.Dropped, source.LateDelivery(Message("4", "fourth")));
        Assert.Equal(Admission.Duplicate, source.LateDelivery(Message("4", "fourth")));
        controller.ClearQueue();
        controller.SkipCurrent();
        Assert.Equal(0, controller.Status.Pending);
        Assert.Contains(trace, item => item.Stage == "队列已满，跳过新消息");
        await Until(() => controller.Status.Activity == "等待新消息");
        Assert.Equal(1, player.Played);
    }
}
