using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace VoiceBridge.App;

public sealed class DragSlider : Slider
{
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (GetTemplateChild("PART_Track") is not Track track || track.Thumb.IsMouseOver)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        Focus();
        MoveToPointer(track, e.GetPosition(track));
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed &&
            GetTemplateChild("PART_Track") is Track track)
        {
            MoveToPointer(track, e.GetPosition(track));
            e.Handled = true;
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
            e.Handled = true;
        }
        base.OnPreviewMouseLeftButtonUp(e);
    }

    private void MoveToPointer(Track track, Point point)
    {
        var halfThumb = track.Thumb.ActualWidth / 2;
        var travel = track.ActualWidth - halfThumb * 2;
        if (travel <= 0)
            return;
        var ratio = Math.Clamp((point.X - halfThumb) / travel, 0, 1);
        SetCurrentValue(ValueProperty, Minimum + (Maximum - Minimum) * (IsDirectionReversed ? 1 - ratio : ratio));
    }
}
