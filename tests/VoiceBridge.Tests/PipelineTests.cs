using VoiceBridge.Core;

namespace VoiceBridge.Tests;

public sealed class PipelineTests
{
    [Fact]
    public void SpaceIsUsedBeforeAHardCutAndTheOriginalTextIsPreserved()
    {
        var text = new string('土', 170) + " " + new string('豆', 180);
        var parts = SpeechText.Split(text).ToArray();
        Assert.Equal(2, parts.Length);
        Assert.EndsWith(" ", parts[0]);
        Assert.Equal(text, string.Concat(parts));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongTextIsPreservedWithOrWithoutPunctuation(bool punctuation)
    {
        var text = string.Concat(Enumerable.Repeat(punctuation ? "一个土豆的故事。\n" : "土豆精灵😀", 300));
        var parts = SpeechText.Split(text).ToArray();
        Assert.Equal(text, string.Concat(parts));
        Assert.All(parts, part =>
        {
            Assert.InRange(part.Length, 1, 240);
            Assert.False(char.IsHighSurrogate(part[^1]));
            Assert.False(char.IsLowSurrogate(part[0]));
        });
    }

    [Fact]
    public async Task NextMessageIsSynthesizedWhileFirstIsStillPlayingAndEndCancelsPrefetch()
    {
        var source = new ProbeSource(); source.Ready.TrySetResult();
        var secondEntered = ProbeSource.NewSignal();
        var canceled = ProbeSource.NewSignal();
        var speech = new ProbeSpeech(async (text, token) =>
        {
            if (text == "second")
            {
                secondEntered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { canceled.TrySetResult(); throw; }
            }
            return new AudioData([]);
        });
        var player = new ProbePlayer(block: true);
        await using var controller = new SessionController();
        Assert.True(controller.Start(TestSupport.DemoTarget, true, 30, false, () => source, speech, player));
        await TestSupport.Until(() => controller.Status.State == BridgeState.Running);
        source.Send(TestSupport.Message("1", "first"));
        await player.Playing.Task.WaitAsync(TestSupport.Limit);
        source.Send(TestSupport.Message("2", "second"));
        await secondEntered.Task.WaitAsync(TestSupport.Limit);
        Assert.Equal(1, player.Played);
        await controller.EndAsync().WaitAsync(TestSupport.Limit);
        Assert.True(canceled.Task.IsCompleted);
        Assert.Equal(1, player.Played);
    }

    [Fact]
    public async Task UnpunctuatedLongMessageIsReadInOrderWithoutLosingText()
    {
        var source = new ProbeSource(); source.Ready.TrySetResult();
        var speech = new ProbeSpeech(); var player = new ProbePlayer();
        await using var controller = new SessionController();
        controller.Start(TestSupport.DemoTarget, true, 30, false, () => source, speech, player);
        await TestSupport.Until(() => controller.Status.State == BridgeState.Running);
        var text = new string('薯', 1800);
        source.Send(TestSupport.Message("long", text));
        await TestSupport.Until(() => player.Played == 8);
        Assert.Equal(text, string.Concat(speech.Calls));
        await controller.EndAsync().WaitAsync(TestSupport.Limit);
    }

    [Fact]
    public async Task ClearQueueDiscardsAlreadySynthesizedLookAhead()
    {
        var source = new ProbeSource(); source.Ready.TrySetResult();
        var speech = new ProbeSpeech(); var player = new GatePlayer();
        await using var controller = new SessionController();
        controller.Start(TestSupport.DemoTarget, true, 30, false, () => source, speech, player);
        await TestSupport.Until(() => controller.Status.State == BridgeState.Running);
        source.Send(TestSupport.Message("1", "first"));
        await TestSupport.Until(() => player.Played == 1);
        source.Send(TestSupport.Message("2", "second"));
        await TestSupport.Until(() => speech.Calls.Count == 2);
        controller.ClearQueue();
        player.Release.TrySetResult();
        await TestSupport.Until(() => controller.Status.Activity == "等待新消息");
        await controller.EndAsync().WaitAsync(TestSupport.Limit);
        Assert.Equal(1, player.Played);
    }

    private sealed class GatePlayer : IPlayer
    {
        public int Played;
        public TaskCompletionSource Release { get; } = ProbeSource.NewSignal();
        public async Task PlayAsync(AudioData audio, Func<Action, bool> startIfCurrent, CancellationToken token)
        {
            if (startIfCurrent(() => Interlocked.Increment(ref Played))) await Release.Task.WaitAsync(token);
        }
        public void Stop() { }
    }
}
