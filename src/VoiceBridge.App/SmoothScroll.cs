using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VoiceBridge.App;

/// <summary>
/// Frame-synced smooth wheel scrolling for every ScrollViewer in the app.
/// </summary>
/// <remarks>
/// WPF's stock wheel handler jumps the offset in one step, which is what makes long
/// pages feel like they stutter. This follows the approach used by the widely copied
/// WPF smooth-scroll samples: accumulate the wheel distance into a target offset, then
/// ease towards it on <see cref="CompositionTarget.Rendering"/> so the movement is
/// rendered once per frame instead of in uneven jumps.
///
/// Details that matter for feel:
/// <list type="bullet">
/// <item>the target accumulates while the wheel keeps turning, so a flick keeps its
/// momentum instead of restarting from the current pixel;</item>
/// <item>easing is ease-out-quint, which starts quickly and settles softly - a linear
/// or short cubic ramp reads as a stop-and-go;</item>
/// <item>duration grows with distance, so one notch is a gentle nudge and a page-sized
/// throw is not a lurch;</item>
/// <item>the system "animate controls" accessibility switch is respected, and a nested
/// scrollable (a list inside a page) keeps priority while it still has room.</item>
/// </list>
/// </remarks>
internal static class SmoothScroll
{
    private sealed class Motion
    {
        public double From;
        public double To;
        public long Started;
        public bool Running;
    }

    private static readonly Dictionary<ScrollViewer, Motion> Motions = new();
    private static readonly MouseWheelEventHandler Handler = OnWheel;

    public static void Install() =>
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(Handler), true);

    /// <summary>Stops any running animation and leaves the viewer where it is.</summary>
    public static void Cancel(ScrollViewer viewer)
    {
        if (!Motions.TryGetValue(viewer, out var motion) || !motion.Running)
            return;
        motion.Running = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || e.Handled)
            return;
        if (viewer.ScrollableHeight <= 0)
            return;
        // A nested viewer that can still move in this direction keeps the event.
        if (e.OriginalSource is DependencyObject source && HasRoomBelow(source, viewer, e.Delta))
            return;

        var lines = SystemParameters.WheelScrollLines;
        // WheelScrollLines == -1 means "a page per notch" (Windows accessibility setting).
        var step = lines < 0 ? viewer.ViewportHeight : lines * 16.0;
        if (step <= 0)
            return;

        var delta = -e.Delta / 120.0 * step;
        if (!Motions.TryGetValue(viewer, out var motion))
            Motions[viewer] = motion = new Motion();

        // Keep the momentum: retarget from the pending destination, not from the pixel
        // we happen to be showing, so consecutive notches add up.
        var origin = motion.Running ? motion.To : viewer.VerticalOffset;
        var target = Math.Clamp(origin + delta, 0, viewer.ScrollableHeight);
        if (Math.Abs(target - viewer.VerticalOffset) < 0.5)
            return;

        e.Handled = true;
        motion.From = viewer.VerticalOffset;
        motion.To = target;
        motion.Started = Stopwatch.GetTimestamp();

        if (!SystemParameters.ClientAreaAnimation)
        {
            motion.Running = false;
            viewer.ScrollToVerticalOffset(target);
            return;
        }
        if (!motion.Running)
        {
            motion.Running = true;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    private static bool HasRoomBelow(DependencyObject source, ScrollViewer root, int delta)
    {
        for (var current = source; current is not null && current != root; current = Parent(current))
        {
            if (current is not ScrollViewer nested || nested.ScrollableHeight <= 0)
                continue;
            var canMove = delta > 0 ? nested.VerticalOffset > 0.5
                : nested.VerticalOffset < nested.ScrollableHeight - 0.5;
            if (canMove)
                return true;
        }
        return false;
    }

    private static DependencyObject? Parent(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    private static void OnFrame(object? sender, EventArgs e)
    {
        var finished = new List<ScrollViewer>();
        foreach (var (viewer, motion) in Motions)
        {
            if (!motion.Running)
                continue;
            var distance = Math.Abs(motion.To - motion.From);
            var duration = Math.Clamp(140 + distance * 0.22, 160, 380);
            var ratio = Math.Min(Stopwatch.GetElapsedTime(motion.Started).TotalMilliseconds / duration, 1);
            // ease-out-quint: quick start, long soft settle.
            var eased = 1 - Math.Pow(1 - ratio, 5);
            var offset = motion.From + (motion.To - motion.From) * eased;
            viewer.ScrollToVerticalOffset(Math.Clamp(offset, 0, viewer.ScrollableHeight));
            if (ratio >= 1)
            {
                viewer.ScrollToVerticalOffset(Math.Clamp(motion.To, 0, viewer.ScrollableHeight));
                motion.Running = false;
                finished.Add(viewer);
            }
        }
        if (finished.Count > 0)
            CompositionTarget.Rendering -= OnFrame;
    }
}
