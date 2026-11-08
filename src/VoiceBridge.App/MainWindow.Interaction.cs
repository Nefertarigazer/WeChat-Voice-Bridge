using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using VoiceBridge.Core;

namespace VoiceBridge.App;

public partial class MainWindow
{
    private readonly Dictionary<FrameworkElement, string> restrictions = new();
    private readonly ToolTip restrictionTip = new() { Placement = PlacementMode.Bottom };
    private readonly DispatcherTimer restrictionTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool scrolling;
    private double scrollFrom;
    private double scrollTo;
    private long scrollStart;

    private bool ListeningActive => controller.Status.State is BridgeState.Starting or BridgeState.Running or BridgeState.Stopping;

    private void InitializeInteraction()
    {
        ContentScroller.PreviewMouseWheel += PageMouseWheel;
        ContentScroller.ScrollChanged += (_, _) => TopFade.Opacity = ContentScroller.VerticalOffset > 0.5 ? 1 : 0;
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(RestrictedMouseDown), true);
        AddHandler(Mouse.QueryCursorEvent, new QueryCursorEventHandler(RestrictedQueryCursor), true);
        restrictionTimer.Tick += (_, _) => DismissRestrictionTip();
        Closed += (_, _) =>
        {
            StopScrolling();
            DismissRestrictionTip();
        };
    }

    private void UpdateInteractionRestrictions(bool active)
    {
        const string listen = "正在监听，请先结束监听";
        SetRestriction(ChangeButton, active ? listen + "再更换联系人。" : null);
        SetRestriction(SearchPanel, active ? listen + "再搜索联系人。" : null);
        SetRestriction(ResultsPanel, active ? listen + "再选择联系人。" : null);
        SetRestriction(AddContactButton, active ? listen + "再添加联系人。" : null);
        SetRestriction(PreviewButton, active ? listen + "再试听。" : null);
        SetRestriction(WeChatSettingsGroup, active ? listen + "再修改微信目录或账号。" : null);
        SetRestriction(ReadingSettingsGroup, null);
        SetRestriction(CacheSettingsGroup, active ? listen + "再调整缓存上限。" : null);
        SetRestriction(CleanButton, active ? listen + "再清理缓存。" : null);
        SetRestriction(TestSettingsGroup, active ? listen + "再试听。"
            : manualCancel != null ? "正在试听，请稍候。" : null);
        SetRestriction(DeleteKeyButton, active && userKeys.Count == 1
            ? listen + "再删除最后一个 API Key。" : null);
        SetRestriction(StartButton, controller.Status.State == BridgeState.Stopping ? "正在结束监听，请稍候。" : null);
        foreach (var button in Descendants(this).OfType<Button>())
        {
            if (button.Name is "RemovePeerButton" or "UseHistoryButton" or "UseSearchButton")
                SetRestriction(button, active ? listen + "再修改监听名单。" : null);
            else if (button.Name == "PreviewPeerButton")
            {
                var ownPreview = manualCancel != null && button.Tag is ContactCard card && manualPreviewContact == card.Profile.ConversationId;
                button.Content = ownPreview ? "取消试听" : "试听";
                SetRestriction(button, active ? listen + "再试听。" : manualCancel != null && !ownPreview ? "另一段试听正在播放，请稍候。" : null);
            }
        }
    }

    private void SetRestriction(FrameworkElement element, string? reason)
    {
        if (reason is null)
        {
            if (!restrictions.Remove(element))
                return;
            if (ReferenceEquals(restrictionTip.PlacementTarget, element))
                DismissRestrictionTip();
            element.ClearValue(IsEnabledProperty);
            element.ClearValue(OpacityProperty);
            element.ClearValue(CursorProperty);
            element.ClearValue(ToolTipService.ToolTipProperty);
            element.ClearValue(ToolTipService.ShowOnDisabledProperty);
            element.ClearValue(AutomationProperties.HelpTextProperty);
            return;
        }
        if (restrictions.TryGetValue(element, out var previous) && previous == reason)
            return;
        restrictions[element] = reason;
        element.IsEnabled = false;
        element.Opacity = 0.76;
        element.Cursor = Cursors.No;
        ToolTipService.SetShowOnDisabled(element, true);
        ToolTipService.SetToolTip(element, reason);
        AutomationProperties.SetHelpText(element, reason);
    }

    private FrameworkElement? RestrictedAt(Point position)
    {
        if (restrictions.Count == 0)
            return null;
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(this,
            candidate => candidate is UIElement { IsHitTestVisible: false }
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
            result =>
            {
                hit = result.VisualHit;
                return HitTestResultBehavior.Stop;
            },
            new PointHitTestParameters(position));
        for (var current = hit; current != null; current = ParentOf(current))
            if (current is FrameworkElement element && restrictions.ContainsKey(element))
                return element;
        return null;
    }

    private void RestrictedMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || RestrictedAt(e.GetPosition(this)) is not { } element)
            return;
        DismissRestrictionTip();
        restrictionTip.PlacementTarget = element;
        restrictionTip.Content = restrictions[element];
        restrictionTip.IsOpen = true;
        restrictionTimer.Start();
        e.Handled = true;
    }

    private void RestrictedQueryCursor(object sender, QueryCursorEventArgs e)
    {
        if (RestrictedAt(e.GetPosition(this)) is null)
            return;
        e.Cursor = Cursors.No;
        e.Handled = true;
    }

    private void DismissRestrictionTip()
    {
        restrictionTimer.Stop();
        restrictionTip.IsOpen = false;
        restrictionTip.PlacementTarget = null;
    }

    private void NavigationChanged(object sender, RoutedEventArgs e)
    {
        if (ContentScroller == null)
            return;
        DismissRestrictionTip();
        StopScrolling();
        ContentScroller.ScrollToTop();
        if (ReferenceEquals(sender, NavAdvanced))
            UpdateCacheInfo();
        if (ready) Dispatcher.BeginInvoke(new Action(Render), DispatcherPriority.Loaded);
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var child = VisualTreeHelper.GetChild(node, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private void PageMouseWheel(object sender, MouseWheelEventArgs e)
    {
        for (var current = e.OriginalSource as DependencyObject; current != null && current != ContentScroller;
            current = ParentOf(current))
        {
            if (current is ScrollViewer nested && nested.ScrollableHeight > 0 &&
                (e.Delta > 0 ? nested.VerticalOffset > 0 : nested.VerticalOffset < nested.ScrollableHeight))
                return;
        }
        var lines = SystemParameters.WheelScrollLines;
        if (ContentScroller.ScrollableHeight <= 0 || lines == 0)
            return;
        e.Handled = true;
        var delta = -e.Delta / 120.0 * (lines < 0 ? ContentScroller.ViewportHeight : lines * 16.0);
        scrollFrom = ContentScroller.VerticalOffset;
        var origin = scrolling && Math.Sign(scrollTo - scrollFrom) == Math.Sign(delta) ? scrollTo : scrollFrom;
        scrollTo = Math.Clamp(origin + delta, 0, ContentScroller.ScrollableHeight);
        if (!SystemParameters.ClientAreaAnimation || Math.Abs(scrollTo - scrollFrom) < 0.5)
        {
            StopScrolling();
            ContentScroller.ScrollToVerticalOffset(scrollTo);
            return;
        }
        scrollStart = Stopwatch.GetTimestamp();
        if (!scrolling)
        {
            scrolling = true;
            CompositionTarget.Rendering += ScrollFrame;
        }
    }

    private void ScrollFrame(object? sender, EventArgs e)
    {
        var target = Math.Min(scrollTo, ContentScroller.ScrollableHeight);
        var distance = Math.Abs(target - scrollFrom);
        var duration = Math.Clamp(75 + distance * 0.10, 85, 180);
        var ratio = Math.Min(Stopwatch.GetElapsedTime(scrollStart).TotalMilliseconds / duration, 1);
        var offset = scrollFrom + (target - scrollFrom) * (1 - Math.Pow(1 - ratio, 3));
        if (Math.Abs(ContentScroller.VerticalOffset - offset) > 0.25 || ratio >= 1)
            ContentScroller.ScrollToVerticalOffset(offset);
        if (ratio >= 1)
            StopScrolling();
    }

    private void StopScrolling()
    {
        if (!scrolling)
            return;
        scrolling = false;
        CompositionTarget.Rendering -= ScrollFrame;
    }

    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is Visual or Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                return match;
            if (FindVisualChild<T>(child) is { } descendant)
                return descendant;
        }
        return null;
    }
}
