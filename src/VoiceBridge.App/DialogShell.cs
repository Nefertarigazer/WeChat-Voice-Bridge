using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace VoiceBridge.App;

/// <summary>The same surface and caption treatment as the release notes, for app-owned dialogs.</summary>
/// <remarks>Only the rounded card is opaque; the window around it is transparent, so no tinted frame shows.</remarks>
internal static class DialogShell
{
    private static readonly System.Collections.Generic.HashSet<Window> Held = new();

    /// <summary>
    /// While a section animates open or shut, the dialog keeps its top edge and only
    /// grows or shrinks at the bottom (still kept on screen). Re-centring on every
    /// frame of a resize is what makes a growing window wobble.
    /// </summary>
    public static void Hold(Window? window, bool hold)
    {
        if (window is null) return;
        if (hold) { Held.Add(window); return; }
        // Release only after the last size change of the motion has been laid out,
        // so it is not answered with a re-centring jump.
        window.Dispatcher.BeginInvoke(new Action(() => Held.Remove(window)), DispatcherPriority.Background);
    }

    public static void Apply(Window window, UIElement body)
    {
        window.Content = null;
        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = true;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.UseLayoutRounding = true;
        window.Background = Brushes.Transparent;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        var root = new Grid();
        var shadow = new Border
        {
            Margin = new(16), CornerRadius = new(14), IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Direction = 270,
                Opacity = .24, Color = (Color)ColorConverter.ConvertFromString("#20262B") }
        };
        shadow.SetResourceReference(Border.BackgroundProperty, "DialogBg");
        root.Children.Add(shadow);
        var surface = new Border { Margin = new(16), BorderThickness = new(1), CornerRadius = new(14) };
        surface.SetResourceReference(Border.BackgroundProperty, "DialogBg");
        surface.SetResourceReference(Border.BorderBrushProperty, "DialogBorder");
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = new(36) });
        layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var caption = new Grid { Background = Brushes.Transparent };
        var close = new Button { Style = (Style)window.FindResource("DialogCloseButton"),
            HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(close, "关闭" + window.Title);
        close.Click += (_, _) => window.Close();
        // A transparent window has no native caption; dragging and Alt+F4 are provided here instead.
        caption.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            if (e.OriginalSource is DependencyObject source && close.IsAncestorOf(source)) return;
            window.DragMove();
        };
        caption.Children.Add(close); layout.Children.Add(caption);
        Grid.SetRow(body, 1); layout.Children.Add(body);
        surface.Child = layout; root.Children.Add(surface); window.Content = root;
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.System && e.SystemKey == Key.F4 &&
                (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) window.Close();
        };
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
                if (Held.Contains(window))
                {
                    KeepOnScreen(window);
                    return;
                }
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
        window.SizeChanged += (_, _) =>
        {
            // While a section animates, clamp in the same frame: a queued (Loaded
            // priority) move waits behind the animation's render frames and then
            // lands as one jump at the end.
            if (Held.Contains(window)) KeepOnScreen(window);
            else QueuePosition();
        };
        window.Closed += (_, _) => Held.Remove(window);
    }

    private static void KeepOnScreen(Window window)
    {
        if (!window.IsLoaded) return;
        var handle = new WindowInteropHelper(window).Handle;
        var screen = System.Windows.Forms.Screen.FromHandle(handle);
        var transform = HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var pixels = screen.WorkingArea;
        var area = Rect.Transform(new Rect(pixels.X, pixels.Y, pixels.Width, pixels.Height), transform);
        var top = Math.Clamp(window.Top, area.Top + 16, Math.Max(area.Top + 16, area.Bottom - window.ActualHeight - 16));
        if (Math.Abs(top - window.Top) > 0.1) window.Top = top;
    }
}
