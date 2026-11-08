using System.Windows;
using System.Windows.Media;

namespace VoiceBridge.App;

public static class ContactActionIcon
{
    public static readonly DependencyProperty GeometryProperty = DependencyProperty.RegisterAttached(
        "Geometry", typeof(Geometry), typeof(ContactActionIcon));
    public static Geometry? GetGeometry(DependencyObject element) => (Geometry?)element.GetValue(GeometryProperty);
    public static void SetGeometry(DependencyObject element, Geometry value) => element.SetValue(GeometryProperty, value);
}
