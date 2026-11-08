using VoiceBridge.Core;
using static VoiceBridge.Tests.TestSupport;

namespace VoiceBridge.Tests;

public sealed class MessageTests
{
    [Fact]
    public async Task FiltersDirectionsAndConversationsAndPlaysDistinctKeysWithIdenticalText()
    {
        await using var controller = new SessionController();
        var source = new ProbeSource();
        var player = new ProbePlayer();
        var speech = new ProbeSpeech();
        controller.Start(DemoTarget, true, 30, false, () => source, speech, player);
        source.Ready.SetResult();
        await Until(() => source.LateDelivery != null);
        var admit = source.LateDelivery!;
        var original = Message("shard0:2");
        Assert.Equal(Admission.Ignored, admit(original with { Direction = Direction.Outgoing }));
        Assert.Equal(Admission.Ignored, admit(original with { Direction = Direction.System }));
        Assert.Equal(Admission.Ignored, admit(original with { Direction = Direction.Unknown }));
        Assert.Equal(Admission.Ignored, admit(original with { IsText = false }));
        Assert.Equal(Admission.Ignored, admit(original with { ConversationId = "other" }));
        Assert.Equal(Admission.Accepted, admit(original));
        Assert.Equal(Admission.Duplicate, admit(original));
        Assert.Equal(Admission.Accepted, admit(original with { Key = "shard1:2" }));
        for (var i = 0; i < 5; i++)
            Assert.Equal(Admission.Accepted, admit(Message($"shard0:{i + 3}", $"第 {i + 1} 条")));
        await Until(() => player.Played == 7);
        Assert.Equal(new[] { "哈哈哈", "哈哈哈", "第 1 条", "第 2 条", "第 3 条", "第 4 条", "第 5 条" }, speech.Calls.ToArray());
    }

    [Fact]
    public async Task AccountChangeStopsSession()
    {
        await using var controller = new SessionController();
        var source = new ProbeSource();
        var speech = new ProbeSpeech();
        controller.Start(DemoTarget, true, 30, false, () => source, speech, new ProbePlayer());
        source.Ready.SetResult();
        source.Send(Message("1") with { AccountId = "changed" });
        await Until(() => controller.Status.State == BridgeState.Error);
        Assert.Contains("账号", controller.Status.Error);
        Assert.Empty(speech.Calls);
    }
}
