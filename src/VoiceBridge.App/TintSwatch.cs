using System.Windows;
using System.Windows.Media;

namespace VoiceBridge.App;

/// <summary>
/// The two colors a 主题色 swatch previews: the tinted surface (upper left) and the accent
/// (lower right). They describe a choice, so they stay the same in light and dark mode.
/// Brushes rather than colors: the template paints them with TemplateBinding on shapes, since
/// a binding on a gradient stop inside a template finds no templated parent.
/// </summary>
public static class TintSwatch
{
    public static readonly DependencyProperty SurfaceProperty = DependencyProperty.RegisterAttached("Surface",
        typeof(Brush), typeof(TintSwatch), new PropertyMetadata(Brushes.Transparent));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.RegisterAttached("Accent",
        typeof(Brush), typeof(TintSwatch), new PropertyMetadata(Brushes.Transparent));

    public static Brush GetSurface(DependencyObject item) => (Brush)item.GetValue(SurfaceProperty);
    public static void SetSurface(DependencyObject item, Brush value) => item.SetValue(SurfaceProperty, value);
    public static Brush GetAccent(DependencyObject item) => (Brush)item.GetValue(AccentProperty);
    public static void SetAccent(DependencyObject item, Brush value) => item.SetValue(AccentProperty, value);
}
