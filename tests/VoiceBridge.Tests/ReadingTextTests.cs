using VoiceBridge.Core;

namespace VoiceBridge.Tests;

public sealed class ReadingTextTests
{
    private static ReadingRule Rule(string match, string say, string scope = "both") =>
        new(match, say, scope != "standalone") { Scope = scope };

    [Fact]
    public void Announcement_is_rendered_with_the_same_word_book_as_the_message()
    {
        const string face = "✧₍^˶- ˕ -˵^₎✧";
        var contact = new ContactProfile
        {
            Name = "小林", Announcement = AnnouncementMode.Custom,
            CustomAnnouncement = face + "说", AnnouncementPresetId = "saved",
            AnnouncementPresets = [new("saved", face + "说", AnnouncementPosition.Beginning)]
        };
        var rules = new[] { Rule(face, "我是小猫哦") };
        // Without the prefix renderer the symbol would reach the voice service unreadable.
        Assert.Equal(face + "说，你好", contact.Prepare("你好", 1));
        Assert.Equal("我是小猫哦说，你好", contact.Prepare("你好", 1,
            prefix => ReadingText.Apply(prefix, rules)));
    }

    [Fact]
    public void Announcement_renders_even_when_the_rule_is_scoped_to_standalone_lines()
    {
        const string face = "✧₍^˶- ˕ -˵^₎✧";
        var contact = new ContactProfile
        {
            Announcement = AnnouncementMode.Custom, CustomAnnouncement = face + "说",
            AnnouncementPresetId = "saved",
            AnnouncementPresets = [new("saved", face + "说", AnnouncementPosition.Beginning)]
        };
        // Chat lines keep the scope, but a composed phrase must not be blocked by it.
        Assert.Equal(face + "说，你好", contact.Prepare("你好", 1,
            prefix => ReadingText.Apply(prefix, [Rule(face, "我是小猫哦", "standalone")])));
        Assert.Equal("我是小猫哦说，你好", contact.Prepare("你好", 1,
            prefix => ReadingText.Apply(prefix, [Rule(face, "我是小猫哦", "standalone")], ignoreScope: true)));
        var plainSymbol = contact with { CustomAnnouncement = face,
            AnnouncementPresets = [new("saved", face, AnnouncementPosition.Beginning)] };
        Assert.Equal("我是小猫哦，你好", plainSymbol.Prepare("你好", 1,
            prefix => ReadingText.Apply(prefix, [Rule(face, "我是小猫哦", "inside")], ignoreScope: true)));
    }

    [Fact]
    public void Word_book_renders_a_suffix_announcement_too()
    {
        var contact = new ContactProfile
        {
            Announcement = AnnouncementMode.Custom, CustomAnnouncement = "yyds留言",
            CustomPosition = AnnouncementPosition.End, AnnouncementPresetId = "saved",
            AnnouncementPresets = [new("saved", "yyds留言", AnnouncementPosition.End)]
        };
        Assert.Equal("你好，永远的神留言", contact.Prepare("你好", 1,
            prefix => ReadingText.Apply(prefix, [Rule("yyds", "永远的神")])));
    }

    [Fact]
    public void Longest_match_wins_and_empty_rules_change_nothing()
    {
        const string text = "ABCDE";
        Assert.Equal(text, ReadingText.Apply(text, []));
        Assert.Equal("长词", ReadingText.Apply(text,
            [Rule("AB", "短"), Rule("ABCDE", "长词"), Rule("ABCD", "中等")]));
    }

    [Fact]
    public void Scopes_follow_the_worker_semantics()
    {
        const string symbol = "※";
        Assert.Equal("换掉", ReadingText.Apply(symbol, [Rule(symbol, "换掉", "standalone")]));
        Assert.Equal("你好" + symbol, ReadingText.Apply("你好" + symbol, [Rule(symbol, "换掉", "standalone")]));
        Assert.Equal("你好换掉", ReadingText.Apply("你好" + symbol, [Rule(symbol, "换掉", "inside")]));
        Assert.Equal("你好换掉", ReadingText.Apply("你好" + symbol, [Rule(symbol, "换掉", "both")]));
    }

    [Fact]
    public void Digit_rules_keep_longer_numbers_intact()
    {
        Assert.Equal("我买了123个", ReadingText.Apply("我买了123个", [Rule("1", "一")]));
        Assert.Equal("我一有个", ReadingText.Apply("我1有个", [Rule("1", "一")]));
    }

    [Fact]
    public void An_old_rule_without_scope_falls_back_to_anywhere()
    {
        Assert.Equal("天哪", ReadingText.Apply("yyds", [new ReadingRule("yyds", "天哪", true)]));
        Assert.Equal("yyds!", ReadingText.Apply("yyds!", [new ReadingRule("yyds", "天哪", false)]));
    }

    [Fact]
    public async Task A_peer_removed_while_running_is_ignored_without_restarting_the_baseline()
    {
        var source = new ProbeSource();
        source.Ready.SetResult();
        var removed = false;
        await using var controller = new SessionController();
        controller.Start(TestSupport.DemoTarget, true, 30, false, () => source,
            new ProbeSpeech(), new ProbePlayer(), admitFilter: _ => !removed);
        await TestSupport.Until(() => controller.Status.State == BridgeState.Running);
        await TestSupport.Until(() => source.LateDelivery is not null);
        Assert.Equal(Admission.Accepted, source.LateDelivery!(TestSupport.Message("before")));
        removed = true;
        Assert.Equal(Admission.Ignored, source.LateDelivery!(TestSupport.Message("after")));
        await controller.EndAsync();
    }
}
