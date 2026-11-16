using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VoiceBridge.Core;

namespace VoiceBridge.App;

internal sealed class RecordTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TimestampTemplate { get; set; }
    public DataTemplate? MessageTemplate { get; set; }
    public DataTemplate? NoticeTemplate { get; set; }
    public DataTemplate? TransitionalTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        RecordEntry entry => entry.Kind switch
        {
            RecordEntryKind.Timestamp => TimestampTemplate,
            RecordEntryKind.Message => MessageTemplate,
            RecordEntryKind.TransitionalHint => TransitionalTemplate,
            _ => NoticeTemplate
        },
        _ => base.SelectTemplate(item, container)
    };
}

public partial class MainWindow
{
    private readonly ObservableCollection<RecordEntry> recordItems = new();
    internal IReadOnlyList<RecordEntry> RecordItems => recordItems;
    private DispatcherTimer? transitionalTimer;
    private DispatcherTimer? transitionalFadeTimer;
    private Stopwatch? transitionalFadeClock;
    private RecordEntry? activeTransitional;
    private DateTimeOffset? lastTimestampAt;
    private ScrollViewer? recordsScroller;
    private (bool Top, double Height) recordsFadeState;

    private void InitializeRecords()
    {
        Results.ItemsSource = recordItems;
        Results.Loaded += (_, _) => HookRecordsScroller();
        Results.SizeChanged += (_, _) => UpdateRecordsTopFade();
        transitionalTimer = new DispatcherTimer(DispatcherPriority.Normal);
        transitionalTimer.Tick += (_, _) =>
        {
            transitionalTimer.Stop();
            FadeOutTransitionalHint();
        };
        transitionalFadeTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(24) };
        transitionalFadeTimer.Tick += TransitionalFadeTick;
    }

    private void HookRecordsScroller()
    {
        var scroller = FindVisualChild<ScrollViewer>(Results);
        if (scroller is null || ReferenceEquals(scroller, recordsScroller)) return;
        recordsScroller = scroller;
        recordsScroller.ScrollChanged += (_, _) => UpdateRecordsTopFade();
        UpdateRecordsTopFade();
    }

    private void UpdateRecordsTopFade()
    {
        if (recordsScroller is null) return;
        var viewport = FindVisualChild<ScrollContentPresenter>(recordsScroller) as FrameworkElement ?? recordsScroller;
        var showTop = recordsScroller.VerticalOffset > 0.5;
        var height = Math.Round(viewport.ActualHeight);
        var state = (showTop, height);
        if (state == recordsFadeState) return;
        recordsFadeState = state;
        if (!showTop || height < 40)
        {
            viewport.OpacityMask = null;
            return;
        }
        var fade = Math.Min(18.0, height * 0.15);
        var stops = new GradientStopCollection
        {
            new(Color.FromArgb(0, 0, 0, 0), 0),
            new(Color.FromArgb(140, 0, 0, 0), fade * 0.45 / height),
            new(Color.FromArgb(255, 0, 0, 0), fade / height),
            new(Color.FromArgb(255, 0, 0, 0), 1)
        };
        var mask = new LinearGradientBrush(stops)
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, height)
        };
        mask.Freeze();
        viewport.OpacityMask = mask;
    }

    private void HandleProcessingEvent(ProcessingEvent item)
    {
        var decision = MessageFeedPolicy.Classify(item.Stage);
        switch (decision.Kind)
        {
            case FeedStageKind.Ignored:
                return;
            case FeedStageKind.Received:
                var card = contactCards.FirstOrDefault(c => c.Profile.ConversationId == item.ConversationId);
                var isPrivacy = item.Preview is null || PreviewBox.IsChecked != true || card?.Profile.Privacy is { Enabled: true };
                var body = isPrivacy ? "发来一条新消息" : item.Preview!;
                AddChatMessage(
                    item.ConversationId,
                    card?.Name ?? "对方",
                    card?.Initial ?? "微",
                    card?.Avatar,
                    body,
                    isPrivacy,
                    DateTimeOffset.Now);
                break;
            case FeedStageKind.Transitional:
                ShowTransitionalHint(decision.Text, decision.AutoHideAfter);
                break;
            case FeedStageKind.SystemNotice:
                AddSystemNotice(decision.Text, decision.IsAlert, DateTimeOffset.Now);
                break;
        }
    }

    internal void AddChatMessage(
        string? conversationId,
        string senderName,
        string initial,
        System.Windows.Media.ImageSource? avatar,
        string text,
        bool isPrivacy,
        DateTimeOffset? at = null)
    {
        var now = at ?? DateTimeOffset.Now;
        // Older messages must stay completely clean: discard any previous transitional hint as soon as a new message arrives.
        ClearTransitionalHint();
        AppendTimestampIfDue(now);
        recordItems.Add(RecordEntry.Message(conversationId, senderName, initial, avatar, text, isPrivacy, now));
        TrimRecordItems();
        RevealRecords(scrollToEnd: true);
    }

    internal void AddSystemNotice(string text, bool isAlert = false, DateTimeOffset? at = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var now = at ?? DateTimeOffset.Now;
        ClearTransitionalHint();
        recordItems.Add(RecordEntry.Notice(text.TrimEnd('。'), isAlert, now));
        TrimRecordItems();
        RevealRecords(scrollToEnd: true);
    }

    internal void ShowTransitionalHint(string text, TimeSpan autoHideAfter)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        transitionalFadeTimer?.Stop();
        transitionalTimer?.Stop();

        if (activeTransitional is not null && recordItems.Count > 0 && ReferenceEquals(recordItems[^1], activeTransitional))
        {
            activeTransitional.Text = text;
            activeTransitional.FadeOpacity = 1.0;
        }
        else
        {
            ClearTransitionalHint();
            var entry = RecordEntry.Transitional(text, DateTimeOffset.Now);
            activeTransitional = entry;
            recordItems.Add(entry);
            TrimRecordItems();
        }

        if (transitionalTimer is not null && autoHideAfter > TimeSpan.Zero)
        {
            transitionalTimer.Interval = autoHideAfter;
            transitionalTimer.Start();
        }
        RevealRecords(scrollToEnd: false);
    }

    private void ClearTransitionalHint()
    {
        transitionalTimer?.Stop();
        transitionalFadeTimer?.Stop();
        if (activeTransitional is null) return;
        recordItems.Remove(activeTransitional);
        activeTransitional = null;
    }

    private void FadeOutTransitionalHint()
    {
        if (activeTransitional is null) return;
        if (!SystemParameters.ClientAreaAnimation || !IsVisible)
        {
            ClearTransitionalHint();
            SyncRecordsVisibility();
            return;
        }
        transitionalFadeClock = Stopwatch.StartNew();
        transitionalFadeTimer?.Start();
    }

    private void TransitionalFadeTick(object? sender, EventArgs e)
    {
        if (activeTransitional is null || transitionalFadeClock is null)
        {
            transitionalFadeTimer?.Stop();
            return;
        }
        var progress = transitionalFadeClock.Elapsed.TotalMilliseconds / 260.0;
        if (progress >= 1.0)
        {
            ClearTransitionalHint();
            SyncRecordsVisibility();
            return;
        }
        activeTransitional.FadeOpacity = Math.Max(0, 1.0 - progress);
    }

    private void AppendTimestampIfDue(DateTimeOffset now)
    {
        if (!MessageFeedPolicy.ShouldInsertTimestamp(lastTimestampAt, now)) return;
        lastTimestampAt = now;
        recordItems.Add(RecordEntry.Timestamp(now));
    }

    private void TrimRecordItems()
    {
        while (recordItems.Count > MessageFeedPolicy.MaxVisibleItems)
        {
            if (ReferenceEquals(recordItems[0], activeTransitional)) break;
            recordItems.RemoveAt(0);
        }
        while (recordItems.Count > 0 && recordItems[0].IsTimestamp &&
               (recordItems.Count == 1 || recordItems[1].IsTimestamp))
            recordItems.RemoveAt(0);
    }

    internal void ClearRecords()
    {
        ClearTransitionalHint();
        recordItems.Clear();
        lastTimestampAt = null;
        SyncRecordsVisibility();
    }

    internal void SetPreviewAvatar(int contactIndex, System.Windows.Media.ImageSource avatar)
    {
        if (contactIndex >= 0 && contactIndex < contactCards.Count)
        {
            contactCards[contactIndex].Avatar = avatar;
            RefreshRecordAvatars(contactCards[contactIndex]);
        }
    }

    private void RefreshRecordAvatars(ContactCard card)
    {
        foreach (var item in recordItems)
        {
            if (item.IsMessage && item.ConversationId == card.Profile.ConversationId)
            {
                item.Avatar = card.Avatar;
                item.SenderName = card.Name;
                item.Initial = card.Initial;
            }
        }
    }

    private void SyncRecordsVisibility()
    {
        var empty = recordItems.Count == 0;
        ClearButton.IsEnabled = !empty;
        ResultsBox.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ResultsEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ScheduleRecordsHeightUpdate();
    }

    private void RevealRecords(bool scrollToEnd)
    {
        SyncRecordsVisibility();
        if (recordItems.Count == 0) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (recordItems.Count == 0) return;
            var scroller = FindVisualChild<ScrollViewer>(Results);
            if (scroller is null)
            {
                Results.ScrollIntoView(recordItems[^1]);
                return;
            }
            if (scrollToEnd || scroller.ScrollableHeight - scroller.VerticalOffset <= 48)
                scroller.ScrollToEnd();
        }), DispatcherPriority.Loaded);
    }
}
