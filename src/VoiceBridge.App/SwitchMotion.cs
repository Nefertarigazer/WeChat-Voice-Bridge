using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VoiceBridge.App;

/// <summary>Animate deliberate clicks, never template creation or page visibility changes.</summary>
public static class SwitchMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SwitchMotion), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void Changed(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not CheckBox box) return;
        if ((bool)args.NewValue) box.Click += Click;
        else box.Click -= Click;
    }

    private static void Click(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox box || !box.IsVisible || !SystemParameters.ClientAreaAnimation ||
            box.Template.FindName("Thumb", box) is not FrameworkElement thumb ||
            thumb.RenderTransform is not TranslateTransform current) return;
        // Template values may be frozen/shared; animate a private copy, leaving the checked-state base intact.
        var transform = current.Clone();
        var destination = box.IsChecked == true ? 18d : 0d;
        transform.X = destination;
        thumb.SetCurrentValue(UIElement.RenderTransformProperty, transform);
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            From = destination == 18 ? 0 : 18, To = destination,
            Duration = TimeSpan.FromMilliseconds(160), FillBehavior = FillBehavior.Stop
        });
    }
}
