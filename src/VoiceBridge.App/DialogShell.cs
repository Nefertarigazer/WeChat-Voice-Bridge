using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Threading;

namespace VoiceBridge.App;

/// <summary>The same surface and caption treatment as the release notes, for app-owned dialogs.</summary>
internal static class DialogShell
{
    public static void Apply(Window window, UIElement body)
    {
        window.Content = null;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.UseLayoutRounding = true;
        window.Background = Brush("#EEF2EF");
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 50, CornerRadius = new(14), GlassFrameThickness = new(1),
            ResizeBorderThickness = new(0), UseAeroCaptionButtons = false
        });
        var root = new Grid();
        root.Children.Add(new Border
        {
            Margin = new(12), Background = Brush("#FFFEFB"), CornerRadius = new(14), IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270,
                Opacity = .18, Color = (Color)ColorConverter.ConvertFromString("#263B2E") }
        });
        var surface = new Border { Margin = new(12), Background = Brush("#FFFEFB"),
            BorderBrush = Brush("#CAD4CD"), BorderThickness = new(1), CornerRadius = new(14) };
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = new(36) });
        layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var caption = new Grid { Background = Brushes.Transparent };
        var close = new Button { Style = (Style)window.FindResource("DialogCloseButton"),
            HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "关闭" };
        AutomationProperties.SetName(close, "关闭" + window.Title);
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => window.Close();
        caption.Children.Add(close); layout.Children.Add(caption);
        Grid.SetRow(body, 1); layout.Children.Add(body);
        surface.Child = layout; root.Children.Add(surface); window.Content = root;
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var screen = System.Windows.Forms.Screen.FromHandle(window.Owner is null ? handle : new WindowInteropHelper(window.Owner).Handle);
            var transform = HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var pixels = screen.WorkingArea;
            var area = Rect.Transform(new Rect(pixels.X, pixels.Y, pixels.Width, pixels.Height), transform);
            window.MaxWidth = Math.Max(280, area.Width - 32);
            window.MaxHeight = Math.Max(240, area.Height - 32);
            window.Width = Math.Min(window.Width, window.MaxWidth);
        };
        // SizeToContent only centers on first show. Recenter after expanded sections or wizard pages resize it.
        var positioning = false;
        void QueuePosition()
        {
            if (positioning || !window.IsLoaded) return;
            positioning = true;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                positioning = false;
                if (!window.IsVisible) return;
                var handle = new WindowInteropHelper(window).Handle;
                var screen = System.Windows.Forms.Screen.FromHandle(handle);
                var transform = HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                var pixels = screen.WorkingArea;
                var area = Rect.Transform(new Rect(pixels.X, pixels.Y, pixels.Width, pixels.Height), transform);
                var center = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
                if (window.Owner is { IsVisible: true } owner)
                    center = transform.Transform(owner.PointToScreen(new Point(owner.ActualWidth / 2, owner.ActualHeight / 2)));
                window.Left = Math.Clamp(center.X - window.ActualWidth / 2, area.Left + 16,
                    Math.Max(area.Left + 16, area.Right - window.ActualWidth - 16));
                window.Top = Math.Clamp(center.Y - window.ActualHeight / 2, area.Top + 16,
                    Math.Max(area.Top + 16, area.Bottom - window.ActualHeight - 16));
            }), DispatcherPriority.Loaded);
        }
        window.Loaded += (_, _) => QueuePosition();
        window.SizeChanged += (_, _) => QueuePosition();
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
