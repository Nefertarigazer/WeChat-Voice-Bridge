using System.Collections.Concurrent;
using System.Text;
using VoiceBridge.Core;
using static VoiceBridge.Tests.TestSupport;

namespace VoiceBridge.Tests;

public sealed class MultiSessionTests
{
    private const string Account = "same-account";
    private static Target[] Targets => [new(Account, "friend-a"), new(Account, "friend-b")];
    private static ChatMessage Incoming(string peer, string key, string text = "相同的文字") =>
        new(key, Account, peer, Direction.Incoming, true, text, 10);

    [Fact]
    public async Task SameKeyAndTextFromTwoContactsRemainDistinctAndRouteToTheSender()
    {
        await using var controller = new SessionController();
        var source = new ProbeSource();
        var speech = new RoutedProbe();
        var player = new SerialPlayer();
        var accepted = new ConcurrentQueue<ProcessingEvent>();
        controller.Processed += accepted.Enqueue;
        Assert.True(controller.Start(Targets, true, 30, true, () => source, speech, player,
            (message, _) => Task.FromResult(message.ConversationId + "说，" + message.Text)));
        source.Ready.SetResult();
        await Until(() => source.LateDelivery != null);
        Assert.Equal(Admission.Accepted, source.LateDelivery!(Incoming("friend-a", "same-local-id")));
        Assert.Equal(Admission.Accepted, source.LateDelivery(Incoming("friend-b", "same-local-id")));
        Assert.Equal(Admission.Duplicate, source.LateDelivery(Incoming("friend-a", "same-local-id")));
        Assert.Equal(Admission.Duplicate, source.LateDelivery(Incoming("friend-b", "same-local-id")));
        await Until(() => Volatile.Read(ref player.Completed) == 2);
        Assert.Equal(0, speech.UnroutedCalls);
        Assert.Equal(["friend-a", "friend-b"], speech.Calls.Select(call => call.Message.ConversationId));
        Assert.Equal(["friend-a说，相同的文字", "friend-b说，相同的文字"], speech.Calls.Select(call => call.Text));
        Assert.All(speech.Calls, call => Assert.Equal("相同的文字", call.Message.Text));
        Assert.Equal(["friend-a", "friend-b"], accepted.Where(item => item.Stage == "已接收")
            .Select(item => item.ConversationId));
        Assert.Equal(1, player.MaxActive);
    }

    [Fact]
    public async Task UnselectedContactsAndNonIncomingTextAreIgnoredWithoutUsingAProvider()
    {
        await using var controller = new SessionController();
        var source = new ProbeSource();
        var speech = new RoutedProbe();
        var player = new SerialPlayer();
        controller.Start(Targets, true, 30, false, () => source, speech, player);
        source.Ready.SetResult();
        await Until(() => source.LateDelivery != null);
        var message = Incoming("friend-a", "shared-id");
        Assert.Equal(Admission.Ignored, source.LateDelivery!(message with { ConversationId = "unselected" }));
        Assert.Equal(Admission.Ignored, source.LateDelivery(message with { Direction = Direction.Outgoing }));
        Assert.Equal(Admission.Ignored, source.LateDelivery(message with { Direction = Direction.System }));
        Assert.Equal(Admission.Ignored, source.LateDelivery(message with { Direction = Direction.Unknown }));
        Assert.Equal(Admission.Ignored, source.LateDelivery(message with { IsText = false }));
        Assert.Equal(Admission.Accepted, source.LateDelivery(message));
        await Until(() => Volatile.Read(ref player.Completed) == 1);
        Assert.Single(speech.Calls);
    }

    [Fact]
    public async Task SourceAccountChangeStopsEveryContactAndDisposesTheSharedSource()
    {
        await using var controller = new SessionController();
        var source = new ProbeSource();
        var speech = new RoutedProbe();
        var player = new SerialPlayer();
        controller.Start(Targets, true, 30, false, () => source, speech, player);
        source.Ready.SetResult();
        await Until(() => source.LateDelivery != null);
        source.Send(Incoming("friend-b", "from-other-account") with { AccountId = "other-account" });
        await Until(() => controller.Status.State == BridgeState.Error);
        Assert.Contains("账号变化", controller.Status.Error);
        Assert.True(source.Stopped.Task.IsCompletedSuccessfully);
        Assert.Empty(speech.Calls);
        Assert.True(Volatile.Read(ref player.Stopped) > 0);
        Assert.Equal(Admission.Closed, source.LateDelivery!(Incoming("friend-a", "late")));
    }

    [Fact]
    public async Task AllVoicesShareOneSerialQueueAndCannotOverlapPlayback()
    {
        await using var controller = new SessionController();
        var source = new ProbeSource();
        var speech = new RoutedProbe();
        var player = new SerialPlayer(holdFirst: true);
        controller.Start(Targets, true, 30, false, () => source, speech, player);
        source.Ready.SetResult();
        await Until(() => source.LateDelivery != null);
        source.LateDelivery!(Incoming("friend-a", "1", "A-one"));
        await player.FirstStarted.Task.WaitAsync(Limit);
        source.LateDelivery(Incoming("friend-b", "1", "B-one"));
        source.LateDelivery(Incoming("friend-a", "2", "A-two"));
        Assert.Equal(2, controller.Status.Pending);
        Assert.Single(speech.Calls);
        Assert.Single(player.Playback);
        player.ReleaseFirst.TrySetResult();
        await Until(() => Volatile.Read(ref player.Completed) == 3);
        Assert.Equal(["friend-a|A-one", "friend-b|B-one", "friend-a|A-two"], player.Playback);
        Assert.Equal(1, player.MaxActive);
    }

    [Fact]
    public async Task StopRejectsLateMessagesFromBothContactsAndLateAudioAcrossRestart()
    {
        await using var controller = new SessionController();
        var source = new ProbeSource();
        var response = new TaskCompletionSource<AudioData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = ProbeSource.NewSignal();
        var first = 0;
        var speech = new RoutedProbe(async (message, text, token) =>
        {
            if (Interlocked.Increment(ref first) == 1)
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                return await response.Task;
            }
            return new(Encoding.UTF8.GetBytes(message.ConversationId + "|" + text));
        });
        var player = new SerialPlayer();
        var trace = new ConcurrentQueue<ProcessingEvent>();
        controller.Processed += trace.Enqueue;
        controller.Start(Targets, true, 30, false, () => source, speech, player);
        source.Ready.SetResult();
        await Until(() => source.LateDelivery != null);
        source.LateDelivery!(Incoming("friend-a", "shared", "old-A"));
        await Until(() => speech.Calls.Count == 1);
        source.LateDelivery(Incoming("friend-b", "shared", "old-B"));
        var oldStamp = controller.ActiveStamp!.Value;
        var ending = controller.EndAsync();
        Assert.Equal(BridgeState.Stopping, controller.Status.State);
        Assert.Equal(0, controller.Status.Pending);
        Assert.Equal(Admission.Closed, source.LateDelivery(Incoming("friend-a", "late-a")));
        Assert.Equal(Admission.Closed, source.LateDelivery(Incoming("friend-b", "late-b")));
        await cancelled.Task.WaitAsync(Limit);
        response.SetResult(new(Encoding.UTF8.GetBytes("late-network-response")));
        await ending.WaitAsync(Limit);
        Assert.Empty(player.Playback);
        Assert.DoesNotContain(trace, item => item.Stage == "收到首个有效音频帧");
        Assert.Single(speech.Calls);

        var next = new ProbeSource();
        controller.Start(Targets, true, 30, false, () => next, speech, player);
        next.Ready.SetResult();
        await Until(() => next.LateDelivery != null);
        Assert.NotEqual(oldStamp.Id, controller.ActiveStamp!.Value.Id);
        Assert.True(controller.ActiveStamp.Value.Generation > oldStamp.Generation);
        Assert.Equal(Admission.Closed, source.LateDelivery(Incoming("friend-a", "still-old-a")));
        Assert.Equal(Admission.Closed, source.LateDelivery(Incoming("friend-b", "still-old-b")));
        Assert.Equal(Admission.Accepted, next.LateDelivery!(Incoming("friend-b", "shared", "new-B")));
        await Until(() => Volatile.Read(ref player.Completed) == 1);
        Assert.Equal(["friend-b|new-B"], player.Playback);
        Assert.Equal(["old-A", "new-B"], speech.Calls.Select(call => call.Text));
    }

    [Fact]
    public async Task MutatingCallerSelectionCannotExpandTheRunningWhitelist()
    {
        await using var controller = new SessionController();
        var source = new ProbeSource();
        var targets = Targets;
        var speech = new RoutedProbe();
        var player = new SerialPlayer();
        controller.Start(targets, true, 30, false, () => source, speech, player);
        targets[0] = new(Account, "unapproved-change");
        source.Ready.SetResult();
        await Until(() => source.LateDelivery != null);
        Assert.Equal(Admission.Ignored, source.LateDelivery!(Incoming("unapproved-change", "1")));
        Assert.Equal(Admission.Accepted, source.LateDelivery(Incoming("friend-a", "1")));
        await Until(() => Volatile.Read(ref player.Completed) == 1);
        Assert.Equal("friend-a", Assert.Single(speech.Calls).Message.ConversationId);
    }

    [Fact]
    public async Task InvalidMixedDuplicateAndMoreThanEightTargetsCannotCreateAWorker()
    {
        await using var controller = new SessionController();
        var created = 0;
        IMessageSource Create() { Interlocked.Increment(ref created); return new ProbeSource(); }
        IReadOnlyList<Target>[] invalid =
        [
            [],
            [new(Account, "friend-a"), new("different-account", "friend-b")],
            [new(Account, "friend-a"), new(Account, "friend-a")],
            [new(Account, "room@chatroom")],
            Enumerable.Range(1, 9).Select(index => new Target(Account, $"friend-{index}")).ToArray()
        ];
        foreach (var selection in invalid)
            Assert.Throws<BridgeException>(() => controller.Start(selection, true, 30, false,
                Create, new RoutedProbe(), new SerialPlayer()));
        Assert.Equal(0, created);
        Assert.Equal(BridgeState.Idle, controller.Status.State);
        var source = new ProbeSource();
        var eight = Enumerable.Range(1, 8).Select(index => new Target(Account, $"friend-{index}")).ToArray();
        Assert.True(controller.Start(eight, true, 30, false, () => source, new RoutedProbe(), new SerialPlayer()));
        await source.Entered.Task.WaitAsync(Limit);
        Assert.Equal(BridgeState.Starting, controller.Status.State);
        await controller.EndAsync().WaitAsync(Limit);
        Assert.True(source.Stopped.Task.IsCompletedSuccessfully);
    }

    private sealed class RoutedProbe(
        Func<ChatMessage, string, CancellationToken, Task<AudioData>>? synthesize = null) : IMessageSpeech
    {
        public ConcurrentQueue<(ChatMessage Message, string Text)> Calls { get; } = new();
        public int UnroutedCalls;
        public Task<AudioData> SynthesizeAsync(string text, Action firstAudio, CancellationToken token)
        {
            Interlocked.Increment(ref UnroutedCalls);
            throw new InvalidOperationException("A multi-contact voice must receive the verified sender.");
        }
        public async Task<AudioData> SynthesizeAsync(ChatMessage message, string text, Action firstAudio,
            CancellationToken token)
        {
            Calls.Enqueue((message, text));
            var audio = synthesize is null ? new AudioData(Encoding.UTF8.GetBytes(message.ConversationId + "|" + text))
                : await synthesize(message, text, token);
            firstAudio();
            return audio;
        }
    }

    private sealed class SerialPlayer(bool holdFirst = false) : IPlayer
    {
        public ConcurrentQueue<string> Playback { get; } = new();
        public TaskCompletionSource FirstStarted { get; } = ProbeSource.NewSignal();
        public TaskCompletionSource ReleaseFirst { get; } = ProbeSource.NewSignal();
        private int active;
        private int maximum;
        public int Completed;
        public int Stopped;
        public int MaxActive => Volatile.Read(ref maximum);
        public async Task PlayAsync(AudioData audio, Func<Action, bool> startIfCurrent, CancellationToken token)
        {
            var started = false;
            var first = false;
            if (!startIfCurrent(() =>
            {
                started = true;
                var count = Interlocked.Increment(ref active);
                var previous = Volatile.Read(ref maximum);
                while (count > previous)
                {
                    var observed = Interlocked.CompareExchange(ref maximum, count, previous);
                    if (observed == previous) break;
                    previous = observed;
                }
                first = Playback.IsEmpty;
                Playback.Enqueue(Encoding.UTF8.GetString(audio.Bytes));
                FirstStarted.TrySetResult();
            })) return;
            try
            {
                if (holdFirst && first)
                    await ReleaseFirst.Task.WaitAsync(token);
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                Interlocked.Increment(ref Completed);
            }
            finally
            {
                if (started) Interlocked.Decrement(ref active);
            }
        }
        public void Stop() => Interlocked.Increment(ref Stopped);
    }
}
