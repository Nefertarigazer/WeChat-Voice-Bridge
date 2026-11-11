using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VoiceBridge.App;

// CapsulePreview <png>: the privacy and reminder capsules, off and on, at the width a
// two-column contact card gives them (142 DIP), rendered at 2x on the card's white.
var output = args.Length > 0 ? args[0] : "capsules.png";
var thread = new Thread(() =>
{
    var board = new Grid { Background = Brushes.White, Width = 340, Margin = new Thickness(0) };
    board.ColumnDefinitions.Add(new() { Width = new GridLength(142) });
    board.ColumnDefinitions.Add(new() { Width = new GridLength(8) });
    board.ColumnDefinitions.Add(new() { Width = new GridLength(142) });
    var states = new[] { (false, false), (true, false), (false, true), (true, true) };
    for (var row = 0; row < states.Length; row++)
    {
        board.RowDefinitions.Add(new() { Height = new GridLength(48) });
        var (privacy, reminder) = states[row];
        var left = new ModeCapsule { Kind = ModeKind.Privacy, IsOn = privacy, VerticalAlignment = VerticalAlignment.Center };
        var right = new ModeCapsule { Kind = ModeKind.Reminder, IsOn = reminder, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(left, row);
        Grid.SetRow(right, row); Grid.SetColumn(right, 2);
        board.Children.Add(left);
        board.Children.Add(right);
    }
    Save(new Border { Background = Brushes.White, Padding = new Thickness(16), Child = board }, output);

    // The wide cards a single contact gets, on the page background, 660 DIP wide.
    var page = new StackPanel { Width = 660 };
    var rows = new[]
    {
        (false, false, false, "未开启，照常朗读内容", "未开启，没回也不再提醒"),
        (true, true, false, "播报“微信来消息了”", "每 3 分钟，最多 20 分钟"),
        (true, true, true, "暗语“天王盖地虎”", "2 条没回"),
    };
    foreach (var (privacy, reminder, waiting, privacyText, reminderText) in rows)
    {
        var line = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        line.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new() { Width = new GridLength(12) });
        line.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var left = new ModeCard { Kind = ModeKind.Privacy, IsOn = privacy, Summary = privacyText };
        var right = new ModeCard { Kind = ModeKind.Reminder, IsOn = reminder, Summary = reminderText, Waiting = waiting };
        Grid.SetColumn(right, 2);
        line.Children.Add(left);
        line.Children.Add(right);
        page.Children.Add(line);
    }
    var wide = args.Length > 1 ? args[1] : Path.ChangeExtension(output, null) + "-wide.png";
    Save(new Border { Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF8, 0xF7)), Padding = new Thickness(24), Child = page }, wide);
});

static void Save(FrameworkElement frame, string path)
{
    TextOptions.SetTextFormattingMode(frame, TextFormattingMode.Display);
    frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    frame.Arrange(new Rect(frame.DesiredSize));
    frame.UpdateLayout();
    var bitmap = new RenderTargetBitmap((int)(frame.ActualWidth * 2), (int)(frame.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
    bitmap.Render(frame);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(path);
    encoder.Save(stream);
    Console.WriteLine($"saved: {path} ({bitmap.PixelWidth}x{bitmap.PixelHeight})");
}
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
