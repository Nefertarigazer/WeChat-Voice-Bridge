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
    var frame = new Border { Background = Brushes.White, Padding = new Thickness(16), Child = board };
    TextOptions.SetTextFormattingMode(frame, TextFormattingMode.Display);
    frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    frame.Arrange(new Rect(frame.DesiredSize));
    frame.UpdateLayout();
    var bitmap = new RenderTargetBitmap((int)(frame.ActualWidth * 2), (int)(frame.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
    bitmap.Render(frame);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(output);
    encoder.Save(stream);
    Console.WriteLine($"saved: {output} ({bitmap.PixelWidth}x{bitmap.PixelHeight})");
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
