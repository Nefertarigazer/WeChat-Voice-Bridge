using System.Text.Json;
using VoiceBridge.Core;

namespace VoiceBridge.Tests;

public sealed class ContactPreferencesTests
{
    [Fact]
    public void Suffix_and_saved_custom_positions_survive_roundtrip()
    {
        var contact = new ContactProfile { Name = "备注名", Announcement = AnnouncementMode.NicknameSuffix };
        Assert.Equal("你好，备注名留言", contact.Prepare("你好", 2));
        contact = contact with { Announcement = AnnouncementMode.Custom, CustomAnnouncement = "来自她",
            CustomPosition = AnnouncementPosition.End, AnnouncementPresetId = "saved",
            AnnouncementPresets = [new("saved", "来自她", AnnouncementPosition.End)] };
        var restored = JsonSerializer.Deserialize<ContactProfile>(JsonSerializer.Serialize(contact))!;
        Assert.Equal("你好，来自她", restored.Prepare("你好", 1));
        Assert.Equal(AnnouncementPosition.End, Assert.Single(restored.AnnouncementPresets).Position);
        Assert.Equal("saved", restored.AnnouncementPresetId);
    }
    [Fact]
    public void Automatic_prefix_follows_people_count_without_overriding_explicit_off()
    {
        var profile = new ContactProfile { AccountId = "a", ConversationId = "peer", Name = "小熊" };
        Assert.Equal("你好", profile.Prepare("你好", 1));
        Assert.Equal("小熊说，你好", profile.Prepare("你好", 2));
        Assert.Equal("你好", (profile with { Announcement = AnnouncementMode.Off }).Prepare("你好", 8));
    }

    [Fact]
    public void Custom_prefix_and_explicit_nickname_survive_roundtrip_and_mode_changes()
    {
        var profile = new ContactProfile { AccountId = "a", ConversationId = "peer", Name = "她",
            Announcement = AnnouncementMode.Custom, CustomAnnouncement = "来自小熊：", VoiceProfileId = "voice-b" };
        var restored = JsonSerializer.Deserialize<ContactProfile>(JsonSerializer.Serialize(profile))!;
        Assert.Equal("来自小熊，你好", restored.Prepare("你好", 1));
        var disabled = restored with { Announcement = AnnouncementMode.Off, Selected = false };
        var enabled = disabled with { Announcement = AnnouncementMode.Custom, Selected = true };
        Assert.Equal("来自小熊，你好", enabled.Prepare("你好", 3));
        Assert.Equal("voice-b", enabled.VoiceProfileId);
        Assert.Equal("她说，你好", (enabled with { Announcement = AnnouncementMode.Nickname }).Prepare("你好", 1));
    }

    [Fact]
    public void History_tracks_identity_separately_from_nickname_and_limits_size()
    {
        ContactHistory[] history = [];
        for (var index = 0; index < 20; index++)
            history = RecentContacts.Add(history, new("a", "peer-" + index, "同名", "", null, DateTimeOffset.UtcNow));
        Assert.Equal(12, history.Length);
        Assert.Equal("peer-19", history[0].ConversationId);
        var refreshed = RecentContacts.Add(history, history[3] with { Name = "新昵称" });
        Assert.Equal(12, refreshed.Length);
        Assert.Equal("新昵称", refreshed[0].Name);
        Assert.Single(refreshed, item => item.ConversationId == refreshed[0].ConversationId);
        var otherAccount = RecentContacts.Add(refreshed, refreshed[0] with { AccountId = "b" });
        Assert.Equal(2, otherAccount.Count(item => item.ConversationId == refreshed[0].ConversationId));
    }

    [Fact]
    public void Search_history_uses_latest_query_but_preserves_account_scope()
    {
        SearchHistory[] history = [];
        for (var index = 0; index < 12; index++)
            history = RecentContacts.Add(history, new("a", "query-" + index, DateTimeOffset.UtcNow));
        Assert.Equal(8, history.Length);
        var item = history[1];
        history = RecentContacts.Add(history, item with { Query = item.Query.ToUpperInvariant() });
        Assert.Equal(8, history.Length);
        Assert.Equal(item.Query.ToUpperInvariant(), history[0].Query);
        history = RecentContacts.Add(history, item with { AccountId = "b" });
        Assert.Equal(2, history.Count(entry => entry.Query.Equals(item.Query, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Preview_can_be_disabled_while_session_is_running()
    {
        var source = new ProbeSource();
        source.Ready.SetResult();
        var speech = new ProbeSpeech();
        await using var controller = new SessionController();
        var received = new List<ProcessingEvent>();
        controller.Processed += item => { if (item.Stage == "已接收") lock (received) received.Add(item); };
        controller.Start(TestSupport.DemoTarget, true, 30, true, () => source, speech, new ProbePlayer());
        await TestSupport.Until(() => controller.Status.State == BridgeState.Running);
        source.Send(TestSupport.Message("before", "可显示"));
        await TestSupport.Until(() => { lock (received) return received.Count == 1; });
        controller.SetPreview(false);
        source.Send(TestSupport.Message("after", "隐藏正文"));
        await TestSupport.Until(() => { lock (received) return received.Count == 2; });
        lock (received)
        {
            Assert.Equal("可显示", received[0].Preview);
            Assert.Null(received[1].Preview);
        }
        await controller.EndAsync();
    }
}
