using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace VoiceBridge.App;

public enum ModeKind { Privacy, Reminder }

/// <summary>
/// One mode of one contact on the home page: icon, name, a small switch and a ⋮ button for its
/// settings. Privacy is indigo and reminders orange when on; both are a quiet grey when off.
/// </summary>
public sealed class ModeCapsule : Border
{
    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(nameof(IsOn), typeof(bool),
        typeof(ModeCapsule), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (item, _) => ((ModeCapsule)item).Paint(animate: true)));
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(ModeKind),
        typeof(ModeCapsule), new PropertyMetadata(ModeKind.Privacy, (item, _) => ((ModeCapsule)item).Build()));
    public static readonly RoutedEvent SettingsRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(SettingsRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ModeCapsule));

    private static readonly Palette Off = new("#F3F6F5", "#E3E9E6", "#8A938F", "#5E6B65", "#D5DBD8");
    private static readonly Palette PrivacyOn = new("#EFEFFC", "#D9D8F5", "#5653CF", "#4A47BD", "#5E5CE0");
    private static readonly Palette ReminderOn = new("#FFF3E4", "#F5D9B0", "#D0730A", "#A95A00", "#EE8B12");
    private static readonly TimeSpan Motion = TimeSpan.FromMilliseconds(160);
    private const double Travel = 14;

    private readonly SolidColorBrush fill = new(), edge = new(), ink = new(), label = new(), track = new();
    private readonly TranslateTransform thumbShift = new();
    private readonly ToggleButton toggle = new();
    private readonly TextBlock name = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 0, 0) };
    private readonly Grid icon = new() { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button more = new();

    public ModeCapsule()
    {
        Height = 32;
        CornerRadius = new(16);
        BorderThickness = new(1);
        Padding = new(10, 0, 3, 0);
        Background = fill;
        BorderBrush = edge;
        SnapsToDevicePixels = true;

        var trackBorder = new Border
        {
            Width = 34, Height = 20, CornerRadius = new(10), Background = track,
            VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 2, 0)
        };
        trackBorder.Child = new Ellipse
        {
            Width = 16, Height = 16, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new(2, 0, 0, 0), RenderTransform = thumbShift,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 3, ShadowDepth = 0.6, Direction = 270, Opacity = 0.22, Color = Color.FromRgb(0x20, 0x26, 0x2B) }
        };
        var layout = new Grid { Background = Brushes.Transparent };
        layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        layout.Children.Add(icon);
        Grid.SetColumn(name, 1); layout.Children.Add(name);
        Grid.SetColumn(trackBorder, 3); layout.Children.Add(trackBorder);

        toggle.Template = PlainTemplate(typeof(ToggleButton));
        toggle.Content = layout;
        toggle.Background = Brushes.Transparent;
        toggle.Cursor = Cursors.Hand;
        toggle.FocusVisualStyle = null;
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(IsOn)) { Source = this, Mode = BindingMode.TwoWay });

        more.Template = MoreTemplate();
        more.Foreground = ink;
        more.Width = 24; more.Height = 24;
        more.Cursor = Cursors.Hand;
        more.FocusVisualStyle = null;
        more.VerticalAlignment = VerticalAlignment.Center;
        more.Click += (_, e) => { e.Handled = true; RaiseEvent(new RoutedEventArgs(SettingsRequestedEvent, this)); };

        var row = new Grid();
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(toggle);
        Grid.SetColumn(more, 1); row.Children.Add(more);
        Child = row;

        toggle.IsKeyboardFocusedChanged += (_, _) => Paint(animate: false);
        Build();
    }

    public bool IsOn { get => (bool)GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }
    public ModeKind Kind { get => (ModeKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public event RoutedEventHandler SettingsRequested
    {
        add => AddHandler(SettingsRequestedEvent, value);
        remove => RemoveHandler(SettingsRequestedEvent, value);
    }

    private void Build()
    {
        var privacy = Kind == ModeKind.Privacy;
        name.Text = privacy ? "隐私" : "提醒";
        AutomationProperties.SetName(toggle, privacy ? "隐私模式" : "提醒模式");
        AutomationProperties.SetName(more, privacy ? "隐私模式设置" : "提醒模式设置");
        icon.Children.Clear();
        if (privacy)
        {
            icon.Children.Add(new Path
            {
                Data = Geometry.Parse("M 4.6,6.4 V 4.6 A 2.4,2.4 0 0 1 9.4,4.6 V 6.4"), Stroke = ink,
                StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            });
            icon.Children.Add(new Path
            {
                Data = Geometry.Parse("M 3.4,6.2 H 10.6 A 1.4,1.4 0 0 1 12,7.6 V 11.4 A 1.4,1.4 0 0 1 10.6,12.8 H 3.4 A 1.4,1.4 0 0 1 2,11.4 V 7.6 A 1.4,1.4 0 0 1 3.4,6.2 Z"),
                Fill = ink
            });
        }
        else
        {
            icon.Children.Add(new Path
            {
                Data = Geometry.Parse("M 2.2,10.4 H 11.8 L 10.6,8.9 V 6.3 A 3.6,3.6 0 0 0 3.4,6.3 V 8.9 Z " +
                    "M 5.6,11.4 A 1.4,1.4 0 0 0 8.4,11.4 Z M 6.1,2.6 A 0.9,0.9 0 1 1 7.9,2.6 A 0.9,0.9 0 1 1 6.1,2.6 Z"),
                Fill = ink
            });
        }
        Paint(animate: false);
    }

    private void Paint(bool animate)
    {
        var on = IsOn;
        var colors = !on ? Off : Kind == ModeKind.Privacy ? PrivacyOn : ReminderOn;
        var focused = toggle.IsKeyboardFocused;
        var duration = animate && SystemParameters.ClientAreaAnimation && IsLoaded ? Motion : TimeSpan.Zero;
        To(fill, colors.Fill, duration);
        To(edge, focused ? (Color)ColorConverter.ConvertFromString("#0B8F66") : colors.Edge, duration);
        To(ink, colors.Ink, duration);
        To(label, colors.Label, duration);
        To(track, colors.Track, duration);
        name.Foreground = label;
        name.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        if (duration == TimeSpan.Zero)
        {
            thumbShift.BeginAnimation(TranslateTransform.XProperty, null);
            thumbShift.X = on ? Travel : 0;
            return;
        }
        thumbShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? Travel : 0, new Duration(duration))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private static void To(SolidColorBrush brush, Color color, TimeSpan duration)
    {
        if (duration == TimeSpan.Zero)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = color;
            return;
        }
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(color, new Duration(duration)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private static ControlTemplate PlainTemplate(Type target)
    {
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.SetValue(BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        surface.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        return new ControlTemplate(target) { VisualTree = surface };
    }

    /// <summary>A round hit area with three vertical dots; it darkens a little under the pointer.</summary>
    private ControlTemplate MoreTemplate()
    {
        var surface = new FrameworkElementFactory(typeof(Border), "Surface");
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        surface.SetValue(BackgroundProperty, Brushes.Transparent);
        var dots = new FrameworkElementFactory(typeof(Path));
        var group = new GeometryGroup();
        foreach (var y in new[] { 2.0, 7.0, 12.0 }) group.Children.Add(new EllipseGeometry(new Point(2, y), 1.45, 1.45));
        group.Freeze();
        dots.SetValue(Path.DataProperty, group);
        // The animated ink brush lives on the button itself: a template would freeze it.
        dots.SetValue(Shape.FillProperty, new TemplateBindingExtension(Control.ForegroundProperty));
        dots.SetValue(WidthProperty, 4.0);
        dots.SetValue(HeightProperty, 14.0);
        dots.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        dots.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        surface.AppendChild(dots);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = surface };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0)), "Surface"));
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x24, 0, 0, 0)), "Surface"));
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x1C, 0x0B, 0x8F, 0x66)), "Surface"));
        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);
        template.Triggers.Add(focus);
        return template;
    }

    private sealed record Palette(Color Fill, Color Edge, Color Ink, Color Label, Color Track)
    {
        public Palette(string fill, string edge, string ink, string label, string track)
            : this(Parse(fill), Parse(edge), Parse(ink), Parse(label), Parse(track)) { }
        private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
    }
}
