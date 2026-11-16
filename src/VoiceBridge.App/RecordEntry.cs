using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using VoiceBridge.Core;

namespace VoiceBridge.App;

internal sealed class RecordEntry : INotifyPropertyChanged
{
    private string senderName;
    private string initial;
    private ImageSource? avatar;
    private string text;
    private bool isPrivacy;
    private bool isAlert;
    private double fadeOpacity = 1.0;

    private RecordEntry(
        RecordEntryKind kind,
        string? conversationId,
        string senderName,
        string initial,
        ImageSource? avatar,
        string text,
        bool isPrivacy,
        bool isAlert,
        DateTimeOffset createdAt)
    {
        Kind = kind;
        ConversationId = conversationId;
        this.senderName = senderName;
        this.initial = initial;
        this.avatar = avatar;
        this.text = text;
        this.isPrivacy = isPrivacy;
        this.isAlert = isAlert;
        CreatedAt = createdAt;
    }

    public RecordEntryKind Kind { get; }
    public string? ConversationId { get; }
    public DateTimeOffset CreatedAt { get; }

    public string SenderName
    {
        get => senderName;
        set { if (senderName != value) { senderName = value; Notify(); } }
    }

    public string Initial
    {
        get => initial;
        set { if (initial != value) { initial = value; Notify(); } }
    }

    public ImageSource? Avatar
    {
        get => avatar;
        set { if (!ReferenceEquals(avatar, value)) { avatar = value; Notify(); Notify(nameof(HasAvatar)); } }
    }

    public bool HasAvatar => avatar is not null;

    public string Text
    {
        get => text;
        set { if (text != value) { text = value; Notify(); } }
    }

    public bool IsPrivacy
    {
        get => isPrivacy;
        set { if (isPrivacy != value) { isPrivacy = value; Notify(); } }
    }

    public bool IsAlert
    {
        get => isAlert;
        set { if (isAlert != value) { isAlert = value; Notify(); } }
    }

    public double FadeOpacity
    {
        get => fadeOpacity;
        set { if (Math.Abs(fadeOpacity - value) > 0.001) { fadeOpacity = value; Notify(); } }
    }

    public bool IsTimestamp => Kind == RecordEntryKind.Timestamp;
    public bool IsMessage => Kind == RecordEntryKind.Message;
    public bool IsSystemNotice => Kind == RecordEntryKind.SystemNotice;
    public bool IsTransitional => Kind == RecordEntryKind.TransitionalHint;

    public static RecordEntry Timestamp(DateTimeOffset at) =>
        new(RecordEntryKind.Timestamp, null, "", "", null, MessageFeedPolicy.FormatTimestamp(at), false, false, at);

    public static RecordEntry Message(string? conversationId, string senderName, string initial,
        ImageSource? avatar, string text, bool isPrivacy, DateTimeOffset at) =>
        new(RecordEntryKind.Message, conversationId, senderName, initial, avatar, text, isPrivacy, false, at);

    public static RecordEntry Notice(string text, bool isAlert, DateTimeOffset at) =>
        new(RecordEntryKind.SystemNotice, null, "", "", null, text, false, isAlert, at);

    public static RecordEntry Transitional(string text, DateTimeOffset at) =>
        new(RecordEntryKind.TransitionalHint, null, "", "", null, text, false, false, at);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
