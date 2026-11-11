using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using VoiceBridge.Core;

namespace VoiceBridge.App;

/// <summary>
/// Privacy mode and reminder mode, per contact. The rules live in Core
/// (<see cref="AttentionText"/>, <see cref="ReplyTracker"/>); this part feeds them from the
/// session and queues the reminders it asks for.
/// </summary>
public partial class MainWindow
{
    private readonly ReplyTracker replies = new();
    private readonly Dictionary<string, AudioData?> cues = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? reminderTimer;

    /// <summary>Raised on the UI thread when a contact's number of unanswered messages changes.</summary>
    private event Action<string>? AttentionChanged;

    private void InitializeAttention()
    {
        // Session events fire under the controller's lock: post, never wait.
        controller.Received += message =>
        {
            var stamp = controller.ActiveStamp;
            var now = DateTimeOffset.Now;
            Dispatcher.InvokeAsync(() =>
            {
                if (stamp == controller.ActiveStamp)
                    replies.Received(message.ConversationId, message.Sequence, message.Text, now);
            });
        };
        controller.Replied += reply =>
        {
            var stamp = controller.ActiveStamp;
            Dispatcher.InvokeAsync(() =>
            {
                if (stamp == controller.ActiveStamp)
                    replies.Replied(reply.ConversationId, reply.Sequence);
            });
        };
        controller.Finished += message =>
        {
            if (message.Kind != MessageKind.Reminder) return;
            Dispatcher.InvokeAsync(() => replies.Finished(message.ConversationId, DateTimeOffset.Now));
        };
        // Read on the worker thread: only the immutable routing snapshot is safe there.
        controller.Merge = message => RoutedContact(Volatile.Read(ref routing), message.ConversationId)
            ?.Privacy is { Enabled: true };
        replies.Changed += conversation => AttentionChanged?.Invoke(conversation);
        reminderTimer = new(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RemindDue(), Dispatcher);
        reminderTimer.Start();
    }

    /// <summary>Unanswered messages of a contact, while reminder mode is on for them.</summary>
    private int Unanswered(ContactProfile profile) =>
        profile.Reminder is { Enabled: true } ? replies.Count(profile.ConversationId) : 0;

    private void MarkRead(string conversation) => replies.MarkRead(conversation);

    private static ContactProfile? RoutedContact(RoutingSnapshot snapshot, string conversation) =>
        snapshot.Contacts.FirstOrDefault(contact => contact.ConversationId == conversation);

    private void RemindDue()
    {
        if (controller.Status.State != BridgeState.Running) return;
        var snapshot = Volatile.Read(ref routing);
        var now = DateTimeOffset.Now;
        foreach (var due in replies.Due(now, conversation => RoutedContact(snapshot, conversation)?.Reminder))
        {
            if (RoutedContact(snapshot, due.ConversationId) is not { } contact) continue;
            var (spoken, shown) = ReminderText(contact, due, snapshot);
            var reminder = new ChatMessage($"reminder:{due.ConversationId}:{now.ToUnixTimeMilliseconds()}",
                contact.AccountId, due.ConversationId, Direction.Incoming, true, spoken, 0,
                Kind: MessageKind.Reminder, Cue: Cue(contact.Reminder.Sound));
            if (controller.Announce(reminder))
                AddResult("提醒：" + shown);
            else if (due.Continuous)
                replies.Finished(due.ConversationId, now);
        }
    }

    /// <summary>What a reminder says, and what the records list shows (never the content).</summary>
    private (string Spoken, string Shown) ReminderText(ContactProfile contact, DueReminder due, RoutingSnapshot snapshot)
    {
        var rules = Volatile.Read(ref activeReadings);
        string Render(string phrase) => rules.Length == 0 ? phrase : ReadingText.Apply(phrase, rules, ignoreScope: true);
        var wording = AttentionText.WordingFor(contact.Privacy, contact.Reminder);
        var shown = AttentionText.Reminder(wording == ReminderWording.Content ? ReminderWording.Name : wording,
            contact.Name, due.Since, due.Count, due.Texts);
        if (wording != ReminderWording.Content)
            return (Render(shown), shown);
        var spoken = AttentionText.Reminder(wording, contact.Name, due.Since, due.Count, due.Texts,
            body => contact.Prepare(body, snapshot.Contacts.Length, rules.Length == 0 ? null : Render));
        return (spoken, shown);
    }

    /// <summary>What privacy mode says for this contact, with the word book applied.</summary>
    private string PrivacyText(ContactProfile contact, int count)
    {
        var rules = Volatile.Read(ref activeReadings);
        var phrase = AttentionText.Privacy(contact.Privacy, contact.Name, count);
        return rules.Length == 0 ? phrase : ReadingText.Apply(phrase, rules, ignoreScope: true);
    }

    private AudioData? Cue(string sound)
    {
        if (string.IsNullOrWhiteSpace(sound)) return null;
        if (cues.TryGetValue(sound, out var cached)) return cached;
        AudioData? audio = null;
        try
        {
            if (ReminderSounds.PathOf(sound) is { } path &&
                new FileInfo(path) is { Exists: true, Length: > 44 and < 20_000_000 })
                audio = new(File.ReadAllBytes(path), IsWave: path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        cues[sound] = audio;
        return audio;
    }
}

/// <summary>Reminder sounds: the alarms and rings every Windows 10/11 installs, or the user's own file.</summary>
internal static class ReminderSounds
{
    public sealed record Choice(string Id, string Label);

    public static IReadOnlyList<Choice> BuiltIn { get; } =
        Enumerable.Range(1, 10).Select(index => new Choice($"Alarm{index:00}", $"闹钟 {index}"))
            .Concat(Enumerable.Range(1, 10).Select(index => new Choice($"Ring{index:00}", $"铃声 {index}")))
            .Prepend(new Choice("", "不响提醒音")).ToArray();

    /// <summary>The file a sound plays, or null when there is none.</summary>
    public static string? PathOf(string sound)
    {
        if (string.IsNullOrWhiteSpace(sound)) return null;
        if (BuiltIn.Any(choice => choice.Id.Length > 0 && choice.Id.Equals(sound, StringComparison.OrdinalIgnoreCase)))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", sound + ".wav");
        return Path.IsPathFullyQualified(sound) &&
            (sound.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || sound.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            ? sound : null;
    }

    public static string LabelOf(string sound) =>
        BuiltIn.FirstOrDefault(choice => choice.Id.Equals(sound, StringComparison.OrdinalIgnoreCase))?.Label
        ?? (PathOf(sound) is { } path ? Path.GetFileNameWithoutExtension(path) : "闹钟 1");
}
