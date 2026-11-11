using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace VoiceBridge.App;

public partial class ReleaseNotesDialog : Window
{
    /// <summary>How long the scrollbar stays after the notes stop moving.</summary>
    private readonly DispatcherTimer barIdle = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private ScrollBar? bar;
    private bool barShown;

    public ReleaseNotesDialog(string version)
    {
        InitializeComponent();
        VersionTitle.Text = $"已更新到 v{version}";
        var release = ReleaseHistory.Find(version);
        ReleaseHeading.Text = release?.Title ?? "本次更新";
        NotesItems.ItemsSource = release?.Changes ?? new[] { "本次版本已安装完成。可在「使用说明 → 更新日志」查看版本记录。" };
        MaxWidth = Math.Max(320, SystemParameters.WorkArea.Width - 48);
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 48);
        Loaded += (_, _) =>
        {
            bar = NotesScroller.Template?.FindName("PART_VerticalScrollBar", NotesScroller) as ScrollBar;
            if (bar is not null)
            {
                // Pointing at the strip shows the thumb, so it can still be dragged.
                bar.MouseEnter += (_, _) => RevealBar();
                bar.MouseLeave += (_, _) => RestartBarIdle();
            }
            UpdateScrollEdges();
        };
        // Runs within the first layout pass: WPF centres the window on its owner afterwards,
        // with the fitted height (checked by tools/DialogPreview).
        NotesContent.SizeChanged += (_, _) => FitHalfLine();
        NotesScroller.ScrollChanged += (_, e) =>
        {
            UpdateScrollEdges();
            if (e.VerticalChange != 0) RevealBar();
        };
        barIdle.Tick += (_, _) => FadeBar();
        Closed += (_, _) => barIdle.Stop();
        // No native caption on a transparent window: keep Alt+F4 working.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.System && e.SystemKey == Key.F4 &&
                (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) Close();
        };
    }

    private void CaptionDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        if (e.OriginalSource is DependencyObject source && CloseNotesButton.IsAncestorOf(source)) return;
        DragMove();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Work in the owner's monitor's logical units, including monitors with different DPI.
        var handle = new WindowInteropHelper(this).Handle;
        var ownerHandle = Owner is null ? handle : new WindowInteropHelper(Owner).Handle;
        var screen = System.Windows.Forms.Screen.FromHandle(ownerHandle);
        var source = HwndSource.FromHwnd(handle);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var pixels = screen.WorkingArea;
        var area = Rect.Transform(new Rect(pixels.X, pixels.Y, pixels.Width, pixels.Height), transform);
        MaxWidth = Math.Max(280, area.Width - 32);
        MaxHeight = Math.Max(240, area.Height - 32);
        Width = Math.Min(544, MaxWidth);
        NotesScroller.MaxHeight = Math.Max(60, Math.Min(340, MaxHeight - 230));
        FitHalfLine();
    }

    private void DismissClick(object sender, RoutedEventArgs e) => Close();

    private void UpdateScrollEdges() =>
        NotesTopFade.Visibility = NotesScroller.VerticalOffset > 0.5 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// When the notes need scrolling, end the box in the middle of a line: exactly half a
    /// line peeks out, showing that more follows. Notes that fit keep their natural height.
    /// </summary>
    private void FitHalfLine()
    {
        var limit = NotesScroller.MaxHeight;
        var top = NotesContent.Margin.Top;
        if (NotesContent.ActualHeight <= 0 || top + NotesContent.ActualHeight + NotesContent.Margin.Bottom <= limit + 0.5)
        {
            NotesScroller.ClearValue(HeightProperty);
            return;
        }
        var cut = 0.0;
        foreach (var block in Descendants<TextBlock>(NotesContent))
        {
            // The bullet sits on its item's first line, so only the item text counts.
            if (block.Text == "•" || block.ActualHeight <= 0) continue;
            var pitch = double.IsNaN(block.LineHeight) ? block.FontFamily.LineSpacing * block.FontSize : block.LineHeight;
            var lines = Math.Max(1, (int)Math.Round(block.ActualHeight / pitch));
            var start = top + block.TranslatePoint(new Point(), NotesContent).Y;
            for (var line = 0; line < lines; line++)
            {
                var middle = start + (line + 0.5) * block.ActualHeight / lines;
                if (middle <= limit && middle > cut) cut = middle;
            }
        }
        if (cut > 0) NotesScroller.Height = cut;
        else NotesScroller.ClearValue(HeightProperty);
    }

    /// <summary>Like an overlay scroller: the thumb shows while the notes move, then fades.</summary>
    private void RevealBar()
    {
        if (bar is null) return;
        // Smooth wheel scrolling moves the notes every frame: start the fade-in only once.
        if (!barShown)
        {
            barShown = true;
            bar.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        }
        RestartBarIdle();
    }

    private void RestartBarIdle()
    {
        barIdle.Stop();
        barIdle.Start();
    }

    private void FadeBar()
    {
        // Keep it while the pointer is on it or the thumb is being dragged; check again later.
        if (bar is not null && (bar.IsMouseOver || bar.IsMouseCaptureWithin)) return;
        barIdle.Stop();
        if (bar is null) return;
        barShown = false;
        bar.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)));
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }
}
