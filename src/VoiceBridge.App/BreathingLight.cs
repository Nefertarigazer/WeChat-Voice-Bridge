using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace VoiceBridge.App;

/// <summary>
/// The breathing light shared by the software-update dot and the connection pill.
/// </summary>
/// <remarks>
/// One breath takes 3.2 s and is deliberately uneven: the dot dims slowly, sits a
/// beat at the bottom, then rises slowly again. A symmetric fade (the obvious
/// AutoReverse version) reads as a blink; the held bottom is what makes it read as
/// breathing. A faint blurred halo of the same colour blooms at the top of each
/// breath, so the dot looks like light rather than a shape that changes opacity.
/// Only opacity and a small scale are animated, never the Fill, so whatever colour
/// the caller picked (green / red / orange / grey) keeps breathing unchanged, and
/// the RenderTransform keeps the layout footprint identical.
/// </remarks>
internal static class BreathingLight
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(3.2);
    // Bottom of the breath and how long it lingers there.
    private static readonly TimeSpan Dimming = TimeSpan.FromSeconds(1.25);
    private static readonly TimeSpan Held = TimeSpan.FromSeconds(1.7);

    /// <summary>Builds the storyboard; the caller decides when (and whether) to run it.</summary>
    public static Storyboard Create(Ellipse dot, ScaleTransform dotScale, Ellipse halo, ScaleTransform haloScale)
    {
        var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        storyboard.Children.Add(Breathe(dot, UIElement.OpacityProperty, 1.0, 0.42));
        storyboard.Children.Add(Breathe(dotScale, ScaleTransform.ScaleXProperty, 1.0, 0.85));
        storyboard.Children.Add(Breathe(dotScale, ScaleTransform.ScaleYProperty, 1.0, 0.85));
        // The halo is brightest at the top of the breath and gone before the dot
        // reaches the bottom, so it never competes with the dip for attention.
        storyboard.Children.Add(Bloom(halo, UIElement.OpacityProperty, 0.0, 0.30));
        storyboard.Children.Add(Bloom(haloScale, ScaleTransform.ScaleXProperty, 1.0, 2.05));
        storyboard.Children.Add(Bloom(haloScale, ScaleTransform.ScaleYProperty, 1.0, 2.05));
        return storyboard;
    }

    /// <summary>Fully lit → dimmed → held at the bottom → fully lit, eased both ends.</summary>
    private static DoubleAnimationUsingKeyFrames Breathe(
        DependencyObject target, DependencyProperty property, double lit, double dim)
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var animation = new DoubleAnimationUsingKeyFrames { Duration = Period };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(lit, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(dim, KeyTime.FromTimeSpan(Dimming), ease));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(dim, KeyTime.FromTimeSpan(Held)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(lit, KeyTime.FromTimeSpan(Period), ease));
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        return animation;
    }

    /// <summary>Starts at the peak, fades out by the middle of the breath, stays dark.</summary>
    private static DoubleAnimationUsingKeyFrames Bloom(
        DependencyObject target, DependencyProperty property, double rest, double peak)
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseOut };
        var animation = new DoubleAnimationUsingKeyFrames { Duration = Period };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(rest, KeyTime.FromTimeSpan(Held), ease));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(rest, KeyTime.FromTimeSpan(Period)));
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        return animation;
    }
}
