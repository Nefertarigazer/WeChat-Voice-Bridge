using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace VoiceBridge.App;

public partial class ReleaseNotesDialog : Window
{
    public ReleaseNotesDialog(string version)
    {
        InitializeComponent();
        VersionTitle.Text = $"已更新到 v{version}";
        var release = ReleaseHistory.Find(version);
        ReleaseHeading.Text = release?.Title ?? "本次更新";
        NotesItems.ItemsSource = release?.Changes ?? new[] { "本次版本已安装完成。可在「使用说明 → 更新日志」查看版本记录。" };
        MaxWidth = Math.Max(320, SystemParameters.WorkArea.Width - 48);
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 48);
        Loaded += (_, _) => UpdateScrollEdges();
        NotesScroller.ScrollChanged += (_, _) => UpdateScrollEdges();
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
    }

    private void DismissClick(object sender, RoutedEventArgs e) => Close();

    private void UpdateScrollEdges()
    {
        NotesTopFade.Visibility = NotesScroller.VerticalOffset > 0.5 ? Visibility.Visible : Visibility.Collapsed;
        var hasMore = NotesScroller.ScrollableHeight - NotesScroller.VerticalOffset > 0.5;
        NotesBottomFade.Visibility = hasMore ? Visibility.Visible : Visibility.Collapsed;
    }
}
