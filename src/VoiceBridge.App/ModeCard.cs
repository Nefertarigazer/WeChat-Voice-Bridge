using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace VoiceBridge.App;

/// <summary>A control that switches one mode of one contact and asks for that mode's settings.</summary>
public interface IModeSwitch
{
    ModeKind Kind { get; }
}

/// <summary>
/// The wide form of a mode, shown above the card when only one contact is listened to:
/// a colored icon tile, the mode's name, what it currently does, a switch and ⋮.
/// With two or more contacts the same settings move into each card's capsules.
/// </summary>
public sealed class ModeCard : Border, IModeSwitch
{
    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(nameof(IsOn), typeof(bool),
        typeof(ModeCard), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (item, _) => ((ModeCard)item).Paint(animate: true)));
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(ModeKind),
        typeof(ModeCard), new PropertyMetadata(ModeKind.Privacy, (item, _) => ((ModeCard)item).Build()));
    public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(nameof(Summary), typeof(string),
        typeof(ModeCard), new PropertyMetadata("", (item, value) => ((ModeCard)item).summary.Text = value.NewValue as string ?? ""));
    /// <summary>Someone waits for a reply: the summary turns amber, like the 已读 pill.</summary>
    public static readonly DependencyProperty WaitingProperty = DependencyProperty.Register(nameof(Waiting), typeof(bool),
        typeof(ModeCard), new PropertyMetadata(false, (item, _) => ((ModeCard)item).Paint(animate: true)));
    public static readonly RoutedEvent SettingsRequestedEvent = ModeCapsule.SettingsRequestedEvent.AddOwner(typeof(ModeCard));

    private static readonly Palette PrivacyOff = new("#FFFFFF", "#9C9AEC", "#1D1D1F", "#D5DBD8", "#8E95A3");
    private static readonly Palette PrivacyOn = new("#F4F4FE", "#5E5CE0", "#4A47BD", "#5E5CE0", "#5653CF");
    private static readonly Palette ReminderOff = new("#FFFFFF", "#F3B96A", "#1D1D1F", "#D5DBD8", "#A39A8D");
    private static readonly Palette ReminderOn = new("#FFF7EC", "#EE8B12", "#A95A00", "#EE8B12", "#D0730A");
    private static readonly Color Quiet = (Color)ColorConverter.ConvertFromString("#6E7673");
    private static readonly Color Amber = (Color)ColorConverter.ConvertFromString("#B2760A");
    private static readonly TimeSpan Motion = TimeSpan.FromMilliseconds(180);
    private const double Travel = 18;

    private readonly SolidColorBrush fill = new(), tileFill = new(), titleInk = new(), summaryInk = new(), track = new(), dots = new();
    private readonly TranslateTransform thumbShift = new();
    private readonly ToggleButton toggle = new();
    private readonly Grid glyph = new() { Width = 12, Height = 12 };
    private readonly TextBlock title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock summary = new()
    {
        FontSize = 12, Margin = new(0, 3, 0, 0), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly Button more = new();

    public ModeCard()
    {
        Height = 64;
        CornerRadius = new(16);
        Padding = new(14, 0, 8, 0);
        Background = fill;
        Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 2, Direction = 270, Opacity = 0.06, Color = Color.FromRgb(0x2B, 0x3A, 0x35) };
        title.Foreground = titleInk;
        summary.Foreground = summaryInk;

        var tile = new Border
        {
            Width = 30, Height = 30, CornerRadius = new(9), Background = tileFill, VerticalAlignment = VerticalAlignment.Center,
            Child = new Viewbox { Width = 17, Height = 17, Child = glyph }
        };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 10, 0) };
        words.Children.Add(title);
        words.Children.Add(summary);
        var trackBorder = new Border
        {
            Width = 46, Height = 28, CornerRadius = new(14), Background = track,
            VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 4, 0),
            Child = new Ellipse
            {
                Width = 24, Height = 24, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new(2, 0, 0, 0), RenderTransform = thumbShift,
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Direction = 270, Opacity = 0.22, Color = Color.FromRgb(0x20, 0x26, 0x2B) }
            }
        };
        var layout = new Grid { Background = Brushes.Transparent };
        layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        layout.Children.Add(tile);
        Grid.SetColumn(words, 1); layout.Children.Add(words);
        Grid.SetColumn(trackBorder, 2); layout.Children.Add(trackBorder);

        toggle.Template = PlainTemplate();
        toggle.Content = layout;
        toggle.Background = Brushes.Transparent;
        toggle.Cursor = Cursors.Hand;
        toggle.FocusVisualStyle = null;
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(IsOn)) { Source = this, Mode = BindingMode.TwoWay });

        more.Template = MoreTemplate();
        more.Foreground = dots;
        more.Width = 28; more.Height = 28;
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
        Build();
    }

    public bool IsOn { get => (bool)GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }
    public ModeKind Kind { get => (ModeKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string Summary { get => (string)GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }
    public bool Waiting { get => (bool)GetValue(WaitingProperty); set => SetValue(WaitingProperty, value); }
    public event RoutedEventHandler SettingsRequested
    {
        add => AddHandler(SettingsRequestedEvent, value);
        remove => RemoveHandler(SettingsRequestedEvent, value);
    }

    private void Build()
    {
        var privacy = Kind == ModeKind.Privacy;
        title.Text = privacy ? "隐私模式" : "提醒模式";
        AutomationProperties.SetName(toggle, title.Text);
        AutomationProperties.SetName(more, title.Text + "设置");
        glyph.Children.Clear();
        if (privacy)
        {
            glyph.Children.Add(new Path
            {
                Data = Geometry.Parse("M 4,5.5 V 4.1 A 2,2 0 0 1 8,4.1 V 5.5"), Stroke = Brushes.White,
                StrokeThickness = 1.3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            });
            glyph.Children.Add(new Path
            {
                Data = Geometry.Parse("M 2.9,5.3 H 9.1 A 1.1,1.1 0 0 1 10.2,6.4 V 9.6 A 1.1,1.1 0 0 1 9.1,10.7 H 2.9 A 1.1,1.1 0 0 1 1.8,9.6 V 6.4 A 1.1,1.1 0 0 1 2.9,5.3 Z"),
                Fill = Brushes.White
            });
        }
        else
            glyph.Children.Add(new Path
            {
                Data = Geometry.Parse("M 1.9,8.8 H 10.1 L 9.1,7.5 V 5.3 A 3.1,3.1 0 0 0 2.9,5.3 V 7.5 Z " +
                    "M 4.8,9.7 A 1.2,1.2 0 0 0 7.2,9.7 Z M 5.25,2.1 A 0.75,0.75 0 1 1 6.75,2.1 A 0.75,0.75 0 1 1 5.25,2.1 Z"),
                Fill = Brushes.White
            });
        Paint(animate: false);
    }

    private void Paint(bool animate)
    {
        var on = IsOn;
        var colors = Kind == ModeKind.Privacy ? on ? PrivacyOn : PrivacyOff : on ? ReminderOn : ReminderOff;
        var duration = animate && SystemParameters.ClientAreaAnimation && IsLoaded ? Motion : TimeSpan.Zero;
        To(fill, colors.Fill, duration);
        To(tileFill, colors.Tile, duration);
        To(titleInk, colors.Title, duration);
        To(summaryInk, Waiting && on ? Amber : Quiet, duration);
        To(track, colors.Track, duration);
        To(dots, colors.Dots, duration);
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

    private static ControlTemplate PlainTemplate()
    {
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.SetValue(BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        surface.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        return new ControlTemplate(typeof(ToggleButton)) { VisualTree = surface };
    }

    private static ControlTemplate MoreTemplate()
    {
        var surface = new FrameworkElementFactory(typeof(Border), "Surface");
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        surface.SetValue(BackgroundProperty, Brushes.Transparent);
        var dotsPath = new FrameworkElementFactory(typeof(Path));
        var group = new GeometryGroup();
        foreach (var y in new[] { 2.0, 7.5, 13.0 }) group.Children.Add(new EllipseGeometry(new Point(2, y), 1.6, 1.6));
        group.Freeze();
        dotsPath.SetValue(Path.DataProperty, group);
        dotsPath.SetValue(Shape.FillProperty, new TemplateBindingExtension(Control.ForegroundProperty));
        dotsPath.SetValue(WidthProperty, 4.0);
        dotsPath.SetValue(HeightProperty, 15.0);
        dotsPath.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        dotsPath.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        surface.AppendChild(dotsPath);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = surface };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x12, 0, 0, 0)), "Surface"));
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0)), "Surface"));
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x1C, 0x0B, 0x8F, 0x66)), "Surface"));
        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);
        template.Triggers.Add(focus);
        return template;
    }

    private sealed record Palette(Color Fill, Color Tile, Color Title, Color Track, Color Dots)
    {
        public Palette(string fill, string tile, string title, string track, string dots)
            : this(Parse(fill), Parse(tile), Parse(title), Parse(track), Parse(dots)) { }
        private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
    }
}
