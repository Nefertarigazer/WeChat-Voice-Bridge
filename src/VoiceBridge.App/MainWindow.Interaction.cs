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

    private bool ListeningActive => controller.Status.State is BridgeState.Starting or BridgeState.Running or BridgeState.Stopping;

    private void InitializeInteraction()
    {
        ContentScroller.ScrollChanged += (_, _) => TopFade.Opacity = ContentScroller.VerticalOffset > 0.5 ? 1 : 0;
        // A row lights up when clicked; clicking anywhere outside the table drops that
        // selection again, so nothing stays highlighted while you work elsewhere.
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(ClearReadingSelectionOutside), true);
        AddHandler(Mouse.QueryCursorEvent, new QueryCursorEventHandler(RestrictedQueryCursor), true);
        Closed += (_, _) => SmoothScroll.Cancel(ContentScroller);
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
            if (button.Name is "UseHistoryButton" or "UseSearchButton")
                SetRestriction(button, active ? listen + "再添加联系人。" : null);
            else if (button.Name == "RemovePeerButton")
                // Removing a peer only narrows the routing snapshot, so it can be applied while listening.
                SetRestriction(button, null);
            else if (button.Name == "PreviewPeerButton")
            {
                var ownPreview = manualCancel != null && button.Tag is ContactCard card && manualPreviewContact == card.Profile.ConversationId;
                button.Content = ownPreview ? "取消试听" : "试听";
                SetRestriction(button, active ? listen + "再试听。" : manualCancel != null && !ownPreview ? "另一段试听正在播放，请稍候。" : null);
            }
        }
    }

    /// <summary>
    /// Clicking away from a row that is being edited saves it, exactly as the 完成
    /// button would: another row, another field, or anywhere outside the table. Clicking
    /// outside the table also drops the row highlight.
    /// </summary>
    private void ClearReadingSelectionOutside(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var editing = readingItems.FirstOrDefault(row => row.IsEditing);
        // A dropdown's list lives in its own popup: picking an entry is not "clicking
        // away", so it must not end the edit before the selection lands.
        if (editing is not null && !IsInsideDropDown(source))
        {
            var insideTable = IsWithin(source, ReadingList);
            var clickedRow = insideTable ? ReadingItemAt(source) : null;
            // Same row (its own boxes, 完成, 删除) keeps its own handling.
            if (!insideTable || (clickedRow is not null && !ReferenceEquals(clickedRow, editing)))
                CommitReading(editing);
        }

        if (ReadingList.SelectedItem is not null && !IsWithin(source, ReadingList))
            ReadingList.SelectedItem = null;
    }

    /// <summary>True when the click belongs to a combo box dropdown (its popup).</summary>
    private static bool IsInsideDropDown(DependencyObject node)
    {
        for (var current = node; current is not null; current = ParentOf(current))
            if (current is ComboBoxItem or System.Windows.Controls.Primitives.Popup)
                return true;
        return false;
    }

    private ReadingItem? ReadingItemAt(DependencyObject node)
    {
        for (var current = node; current is not null; current = ParentOf(current))
            if (current is FrameworkElement { DataContext: ReadingItem item })
                return item;
        return null;
    }

    private static bool IsWithin(DependencyObject node, DependencyObject ancestor)
    {
        for (var current = node; current is not null; current = ParentOf(current))
            if (ReferenceEquals(current, ancestor))
                return true;
        return false;
    }

    private void SetRestriction(FrameworkElement element, string? reason)
    {
        if (reason is null)
        {
            if (!restrictions.Remove(element))
                return;
            element.ClearValue(IsEnabledProperty);
            element.ClearValue(OpacityProperty);
            element.ClearValue(CursorProperty);
            element.ClearValue(AutomationProperties.HelpTextProperty);
            return;
        }
        if (restrictions.TryGetValue(element, out var previous) && previous == reason)
            return;
        restrictions[element] = reason;
        element.IsEnabled = false;
        element.Opacity = 0.76;
        element.Cursor = Cursors.No;
        // No hover tooltip: the disabled look and the NotAllowed cursor carry it, and
        // the app avoids floating text boxes over the interface.
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

    private void RestrictedQueryCursor(object sender, QueryCursorEventArgs e)
    {
        if (RestrictedAt(e.GetPosition(this)) is null)
            return;
        e.Cursor = Cursors.No;
        e.Handled = true;
    }

    private void NavigationChanged(object sender, RoutedEventArgs e)
    {
        if (ContentScroller == null)
            return;
        // A hint is about the page you were just on, so it does not travel with you.
        notice = null;
        SmoothScroll.Cancel(ContentScroller);
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
