using System.Collections.Concurrent;
using System.Text.Json;
using VoiceBridge.Core;
using static VoiceBridge.Tests.TestSupport;

namespace VoiceBridge.Tests;

public sealed class AttentionTextTests
{
    private static readonly PrivacySettings Off = new();
    private static readonly PrivacySettings WeChat = new() { Enabled = true, Style = PrivacyStyle.WeChat };
    private static readonly PrivacySettings Named = new() { Enabled = true, Style = PrivacyStyle.Name };
    private static readonly PrivacySettings Secret = new() { Enabled = true, Style = PrivacyStyle.Secret, Secret = " 天王盖地虎 " };

    [Fact]
    public void PrivacyNeverReadsTheMessage()
    {
        Assert.Equal("微信来消息了", AttentionText.Privacy(WeChat, "争渡"));
        Assert.Equal("微信来了3条消息", AttentionText.Privacy(WeChat, "争渡", 3));
        Assert.Equal("争渡给你发消息了", AttentionText.Privacy(Named, "争渡"));
        Assert.Equal("争渡给你发了2条消息", AttentionText.Privacy(Named, "争渡", 2));
        Assert.Equal("天王盖地虎", AttentionText.Privacy(Secret, "争渡", 4));
        Assert.Equal("微信来消息了", AttentionText.Privacy(Secret with { Secret = "  " }, "争渡"));
        Assert.Equal("联系人给你发消息了", AttentionText.Privacy(Named, " "));
    }

    [Fact]
    public void ReminderWordingFollowsPrivacyAndPrivacyHidesTheContentChoice()
    {
        var reminder = new ReminderSettings();
        Assert.Equal(ReminderWording.Content, AttentionText.WordingFor(Off, reminder));
        Assert.Equal(ReminderWording.WeChat, AttentionText.WordingFor(Secret, reminder));
        Assert.Equal(ReminderWording.WeChat, AttentionText.WordingFor(WeChat, reminder));
        Assert.Equal(ReminderWording.Name, AttentionText.WordingFor(Named, reminder));
        Assert.Equal(ReminderWording.Name, AttentionText.WordingFor(Off, reminder with { Wording = ReminderWording.Name }));
        // A stale "read the content" choice can never leak a message in privacy mode.
        Assert.Equal(ReminderWording.WeChat, AttentionText.WordingFor(WeChat, reminder with { Wording = ReminderWording.Content }));
        Assert.Equal(new[] { ReminderWording.WeChat, ReminderWording.Name }, AttentionText.WordingChoices(Named));
        Assert.Equal(3, AttentionText.WordingChoices(Off).Count);
    }

    [Fact]
    public void RemindersSayHowLongAndHowMany()
    {
        string[] texts = ["明天几点", "到了吗？", "回我一下"];
        Assert.Equal("微信在3分钟前来了一条消息",
            AttentionText.Reminder(ReminderWording.WeChat, "争渡", TimeSpan.FromMinutes(3), 1, texts));
        Assert.Equal("争渡在6分钟前给您发了3条消息",
            AttentionText.Reminder(ReminderWording.Name, "争渡", TimeSpan.FromMinutes(6), 3, texts));
        Assert.Equal("来自3分钟前的消息，明天几点；到了吗？回我一下",
            AttentionText.Reminder(ReminderWording.Content, "争渡", TimeSpan.FromMinutes(3), 3, texts));
        Assert.Equal("来自1分钟前的消息，争渡说，明天几点",
            AttentionText.Reminder(ReminderWording.Content, "争渡", TimeSpan.FromSeconds(40), 1, ["明天几点"],
                body => "争渡说，" + body));
        // Nothing left to re-read: say who wrote instead.
        Assert.Equal("争渡在3分钟前给您发了一条消息",
            AttentionText.Reminder(ReminderWording.Content, "争渡", TimeSpan.FromMinutes(3), 1, []));
    }

    [Theory]
    [InlineData(0.2, "1分钟")]
    [InlineData(3, "3分钟")]
    [InlineData(59.9, "59分钟")]
    [InlineData(60, "1小时")]
    [InlineData(80, "1小时20分钟")]
    [InlineData(600, "10小时")]
    public void ElapsedTimeIsSpokenInWholeMinutesAndHours(double minutes, string expected) =>
        Assert.Equal(expected, AttentionText.Elapsed(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void ScheduleIsClampedToTheAllowedRange()
    {
        Assert.Equal((3, 20), (new ReminderSettings().Interval, new ReminderSettings().Duration));
        Assert.Equal((1, 180), (new ReminderSettings { IntervalMinutes = 0, DurationMinutes = 999 }.Interval,
            new ReminderSettings { IntervalMinutes = 0, DurationMinutes = 999 }.Duration));
        // An interval longer than the duration reminds once, at the end.
        Assert.Equal(20, new ReminderSettings { IntervalMinutes = 30, DurationMinutes = 20 }.Interval);
    }

    [Fact]
    public void BothModesStartOffAndSurviveTheSettingsFile()
    {
        var fresh = new ContactProfile();
        Assert.False(fresh.Privacy.Enabled);
        Assert.False(fresh.Reminder.Enabled);
        var profile = fresh with
        {
            Privacy = Secret,
            Reminder = new() { Enabled = true, IntervalMinutes = 5, DurationMinutes = 60, Continuous = true,
                Sound = "Ring02", Wording = ReminderWording.Name }
        };
        var copy = JsonSerializer.Deserialize<ContactProfile>(JsonSerializer.Serialize(profile))!;
        Assert.Equal(profile.Privacy, copy.Privacy);
        Assert.Equal(profile.Reminder, copy.Reminder);
        // Profiles saved before this feature have neither object.
        var old = JsonSerializer.Deserialize<ContactProfile>("""{"ConversationId":"peer","Selected":true}""")!;
        Assert.Equal(new PrivacySettings(), old.Privacy);
        Assert.Equal(new ReminderSettings(), old.Reminder);
    }
}

public sealed class ReplyTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 10, 0, 0, TimeSpan.FromHours(8));
    private static readonly ReminderSettings Default = new() { Enabled = true };

    private static int[] StepsUntil(ReplyTracker tracker, string chat, double minutes, ReminderSettings settings,
        DateTimeOffset? from = null)
    {
        var steps = new List<int>();
        var start = from ?? T0;
        for (var second = 0; second <= minutes * 60; second += 10)
            foreach (var due in tracker.Due(start.AddSeconds(second), _ => settings))
                if (due.ConversationId == chat) steps.Add((int)due.Since.TotalMinutes);
        return steps.ToArray();
    }

    [Fact]
    public void DefaultRoundRemindsEveryThreeMinutesForTwentyMinutes()
    {
        var tracker = new ReplyTracker();
        tracker.Received("a", 1000, "在吗", T0);
        Assert.Equal(new[] { 3, 6, 9, 12, 15, 18 }, StepsUntil(tracker, "a", 40, Default));
        Assert.Equal(1, tracker.Count("a"));
    }

    [Fact]
    public void AnHourWithFiveMinuteStepsRemindsTwelveTimesIncludingTheLastMinute()
    {
        var tracker = new ReplyTracker();
        tracker.Received("a", 1000, "在吗", T0);
        var steps = StepsUntil(tracker, "a", 70, Default with { IntervalMinutes = 5, DurationMinutes = 60 });
        Assert.Equal(12, steps.Length);
        Assert.Equal(60, steps[^1]);
    }

    [Fact]
    public void ANewMessageStartsANewRoundFromItselfAndTheCountKeepsGrowing()
    {
        var tracker = new ReplyTracker();
        tracker.Received("a", 1000, "一", T0);
        Assert.Equal(new[] { 3, 6 }, StepsUntil(tracker, "a", 8, Default));
        tracker.Received("a", 2000, "二", T0.AddMinutes(8));
        var due = tracker.Due(T0.AddMinutes(11), _ => Default).Single();
        Assert.Equal((2, TimeSpan.FromMinutes(3)), (due.Count, due.Since));
        Assert.Equal(new[] { "一", "二" }, due.Texts);
    }

    [Fact]
    public void AFinishedRoundStopsRemindingButKeepsCountingUntilTheNextMessage()
    {
        var tracker = new ReplyTracker();
        tracker.Received("a", 1000, "一", T0);
        StepsUntil(tracker, "a", 25, Default);
        Assert.Empty(StepsUntil(tracker, "a", 600, Default, T0.AddMinutes(25)));
        Assert.Equal(1, tracker.Count("a"));
        // Ten hours later he writes again: a new round, timed from the new message.
        var later = T0.AddHours(10);
        tracker.Received("a", 9000, "二", later);
        var due = tracker.Due(later.AddMinutes(3), _ => Default).Single();
        Assert.Equal((2, TimeSpan.FromMinutes(3)), (due.Count, due.Since));
    }

    [Fact]
    public void AReplyAnswersOnlyWhatCameBeforeIt()
    {
        var tracker = new ReplyTracker();
        var changes = new List<string>();
        tracker.Changed += changes.Add;
        tracker.Received("a", 1000, "一", T0);
        tracker.Received("a", 3000, "三", T0.AddMinutes(1));
        tracker.Replied("a", 2000);
        Assert.Equal(1, tracker.Count("a"));
        // A message older than the reply that is read late (a later page) is already answered.
        tracker.Received("a", 1500, "一点五", T0.AddMinutes(2));
        Assert.Equal(1, tracker.Count("a"));
        tracker.Replied("a", 4000);
        Assert.Equal(0, tracker.Count("a"));
        Assert.Empty(StepsUntil(tracker, "a", 30, Default));
        Assert.Equal(new[] { "a", "a", "a", "a" }, changes);
    }

    [Fact]
    public void MarkingReadClearsOneContactOnly()
    {
        var tracker = new ReplyTracker();
        tracker.Received("a", 1000, "一", T0);
        tracker.Received("b", 1000, "一", T0);
        tracker.MarkRead("a");
        Assert.Equal((0, 1), (tracker.Count("a"), tracker.Count("b")));
        tracker.Received("a", 900, "旧", T0.AddMinutes(1));
        Assert.Equal(0, tracker.Count("a"));
        Assert.Equal(new[] { 3 }, StepsUntil(tracker, "b", 3.5, Default).Distinct().ToArray());
    }

    [Fact]
    public void ContinuousRemindersWaitAMinuteThenFollowFiveSecondsAfterEachOther()
    {
        var tracker = new ReplyTracker();
        var continuous = Default with { Continuous = true };
        tracker.Received("a", 1000, "一", T0);
        Assert.Empty(tracker.Due(T0.AddSeconds(59), _ => continuous));
        var first = tracker.Due(T0.AddMinutes(1), _ => continuous).Single();
        Assert.True(first.Continuous);
        // Nothing new while the reminder is still being spoken.
        Assert.Empty(tracker.Due(T0.AddMinutes(1.5), _ => continuous));
        tracker.Finished("a", T0.AddMinutes(1.5));
        Assert.Empty(tracker.Due(T0.AddMinutes(1.5).AddSeconds(4), _ => continuous));
        Assert.Single(tracker.Due(T0.AddMinutes(1.5).AddSeconds(5), _ => continuous));
        // A reminder that never reports back does not block the contact for ever.
        Assert.Single(tracker.Due(T0.AddMinutes(4), _ => continuous));
        tracker.MarkRead("a");
        Assert.Empty(tracker.Due(T0.AddMinutes(10), _ => continuous));
    }

    [Fact]
    public void ContactsWithRemindersOffAreTrackedButNeverReminded()
    {
        var tracker = new ReplyTracker();
        tracker.Received("a", 1000, "一", T0);
        Assert.Empty(StepsUntil(tracker, "a", 30, Default with { Enabled = false }));
        Assert.Equal(1, tracker.Count("a"));
        tracker.Clear();
        Assert.Equal(0, tracker.Count("a"));
    }
}

public sealed class AttentionPipelineTests
{
    private sealed class CountingSpeech : ISpeech, IMessageSpeechResolver
    {
        public ConcurrentQueue<string> Calls { get; } = new();
        public ResolvedSpeech Resolve(ChatMessage message, string text) =>
            new(this, message.Kind == MessageKind.Reminder ? text : $"{text}×{message.Merged}");
        public Task<AudioData> SynthesizeAsync(string text, Action firstAudio, CancellationToken token)
        {
            Calls.Enqueue(text);
            firstAudio();
            return Task.FromResult(new AudioData([]));
        }
    }

    [Fact]
    public async Task MessagesWaitingInTheQueueAreMergedIntoOneAnnouncement()
    {
        var source = new ProbeSource(); source.Ready.TrySetResult();
        var gate = ProbeSource.NewSignal();
        var speech = new CountingSpeech();
        var received = new ConcurrentQueue<ChatMessage>();
        await using var controller = new SessionController { Merge = _ => true };
        controller.Received += received.Enqueue;
        // Hold the first message in synthesis, so the next ones stay in the queue.
        controller.Start(DemoTarget, true, 30, false, () => source, speech, new ProbePlayer(),
            async (message, token) =>
            {
                if (message.Key == "1") await gate.Task.WaitAsync(token);
                return message.Text;
            });
        await Until(() => controller.Status.State == BridgeState.Running);
        source.Send(Message("1", "a"));
        await Until(() => received.Count == 1);
        source.Send(Message("2", "b"));
        source.Send(Message("3", "c"));
        source.Send(Message("4", "d"));
        await Until(() => received.Count == 4);
        Assert.Equal(1, controller.Status.Pending);
        gate.SetResult();
        await Until(() => speech.Calls.Count == 2);
        Assert.Equal(new[] { "a×1", "b×3" }, speech.Calls.ToArray());
        await controller.EndAsync().WaitAsync(Limit);
    }

    [Fact]
    public async Task AReminderPlaysItsSoundOnceThenItsFinalTextAndReportsWhenDone()
    {
        var source = new ProbeSource(); source.Ready.TrySetResult();
        var speech = new CountingSpeech();
        var player = new ProbePlayer();
        var finished = new ConcurrentQueue<ChatMessage>();
        await using var controller = new SessionController();
        controller.Finished += finished.Enqueue;
        controller.Start(DemoTarget, true, 30, false, () => source, speech, player,
            (message, _) => Task.FromResult("re-rendered"));
        await Until(() => controller.Status.State == BridgeState.Running);
        var cue = new AudioData([1, 2, 3], IsWave: true);
        var reminder = new ChatMessage("reminder:1", DemoTarget.AccountId, DemoTarget.ConversationId,
            Direction.Incoming, true, "微信在3分钟前来了一条消息", 0, Kind: MessageKind.Reminder, Cue: cue);
        Assert.False(controller.Announce(reminder with { Kind = MessageKind.Chat }));
        Assert.False(controller.Announce(reminder with { ConversationId = "someone-else" }));
        Assert.True(controller.Announce(reminder));
        await Until(() => finished.Count == 1);
        Assert.Equal(new[] { "微信在3分钟前来了一条消息" }, speech.Calls.ToArray());
        Assert.Equal(2, Volatile.Read(ref player.Played));
        Assert.Equal(MessageKind.Reminder, finished.Single().Kind);
        await controller.EndAsync().WaitAsync(Limit);
        Assert.False(controller.Announce(reminder));
    }
}
