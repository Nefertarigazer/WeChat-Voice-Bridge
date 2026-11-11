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

    // Like a row in iOS Settings, each mode keeps its own colored icon tile even when off,
    // so the pair never reads as two identical grey pills. "On" deepens the tile and tints
    // the whole capsule; the switch is what says on or off.
    private static readonly Palette PrivacyOff = new("#F8F9FB", "#E3E6EE", "#9C9AEC", "#3F4744", "#D5DBD8", "#8E95A3");
    private static readonly Palette PrivacyOn = new("#EFEFFC", "#D9D8F5", "#5E5CE0", "#4A47BD", "#5E5CE0", "#5653CF");
    private static readonly Palette ReminderOff = new("#FBF9F6", "#ECE5DB", "#F3B96A", "#3F4744", "#D5DBD8", "#A39A8D");
    private static readonly Palette ReminderOn = new("#FFF3E4", "#F5D9B0", "#EE8B12", "#A95A00", "#EE8B12", "#D0730A");
    private static readonly TimeSpan Motion = TimeSpan.FromMilliseconds(160);
    private const double Travel = 14;

    private readonly SolidColorBrush fill = new(), edge = new(), tileFill = new(), label = new(), track = new(), dots = new();
    private readonly TranslateTransform thumbShift = new();
    private readonly ToggleButton toggle = new();
    private readonly TextBlock name = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new(7, 0, 0, 0) };
    private readonly Grid icon = new() { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border tile = new() { Width = 18, Height = 18, CornerRadius = new(5.5), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button more = new();

    public ModeCapsule()
    {
        Height = 32;
        CornerRadius = new(16);
        BorderThickness = new(1);
        Padding = new(7, 0, 3, 0);
        Background = fill;
        BorderBrush = edge;
        SnapsToDevicePixels = true;
        tile.Background = tileFill;
        tile.Child = icon;

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
        layout.Children.Add(tile);
        Grid.SetColumn(name, 1); layout.Children.Add(name);
        Grid.SetColumn(trackBorder, 3); layout.Children.Add(trackBorder);

        toggle.Template = PlainTemplate(typeof(ToggleButton));
        toggle.Content = layout;
        toggle.Background = Brushes.Transparent;
        toggle.Cursor = Cursors.Hand;
        toggle.FocusVisualStyle = null;
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(IsOn)) { Source = this, Mode = BindingMode.TwoWay });

        more.Template = MoreTemplate();
        more.Foreground = dots;
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
        // White glyphs on the colored tile, drawn in a 12×12 box.
        if (privacy)
        {
            icon.Children.Add(new Path
            {
                Data = Geometry.Parse("M 4,5.5 V 4.1 A 2,2 0 0 1 8,4.1 V 5.5"), Stroke = Brushes.White,
                StrokeThickness = 1.3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            });
            icon.Children.Add(new Path
            {
                Data = Geometry.Parse("M 2.9,5.3 H 9.1 A 1.1,1.1 0 0 1 10.2,6.4 V 9.6 A 1.1,1.1 0 0 1 9.1,10.7 H 2.9 A 1.1,1.1 0 0 1 1.8,9.6 V 6.4 A 1.1,1.1 0 0 1 2.9,5.3 Z"),
                Fill = Brushes.White
            });
        }
        else
        {
            icon.Children.Add(new Path
            {
                Data = Geometry.Parse("M 1.9,8.8 H 10.1 L 9.1,7.5 V 5.3 A 3.1,3.1 0 0 0 2.9,5.3 V 7.5 Z " +
                    "M 4.8,9.7 A 1.2,1.2 0 0 0 7.2,9.7 Z M 5.25,2.1 A 0.75,0.75 0 1 1 6.75,2.1 A 0.75,0.75 0 1 1 5.25,2.1 Z"),
                Fill = Brushes.White
            });
        }
        Paint(animate: false);
    }

    private void Paint(bool animate)
    {
        var on = IsOn;
        var privacy = Kind == ModeKind.Privacy;
        var colors = privacy ? on ? PrivacyOn : PrivacyOff : on ? ReminderOn : ReminderOff;
        var focused = toggle.IsKeyboardFocused;
        var duration = animate && SystemParameters.ClientAreaAnimation && IsLoaded ? Motion : TimeSpan.Zero;
        To(fill, colors.Fill, duration);
        To(edge, focused ? (Color)ColorConverter.ConvertFromString("#0B8F66") : colors.Edge, duration);
        To(tileFill, colors.Tile, duration);
        To(label, colors.Label, duration);
        To(track, colors.Track, duration);
        To(dots, colors.Dots, duration);
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

    private sealed record Palette(Color Fill, Color Edge, Color Tile, Color Label, Color Track, Color Dots)
    {
        public Palette(string fill, string edge, string tile, string label, string track, string dots)
            : this(Parse(fill), Parse(edge), Parse(tile), Parse(label), Parse(track), Parse(dots)) { }
        private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
    }
}
