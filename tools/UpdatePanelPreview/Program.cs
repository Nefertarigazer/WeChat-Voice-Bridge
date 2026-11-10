using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace UpdatePanelPreview;

/// <summary>
/// Renders the real "软件更新" card the way the app draws it and then MEASURES it:
/// the status dot must sit on the version text's line in all three states, and the
/// button must centre on the header region (card top down to the notes box top).
/// Uses the app's own App.xaml resources, so styles are not re-invented here.
/// </summary>
internal static class Program
{
    private static readonly (byte R, byte G, byte B)[] DotColours =
    {
        (0xAE, 0xAE, 0xB2), // IdleDot  · up to date
        (0xB2, 0x76, 0x0A), // WarnDot  · update available
        (0x1D, 0x9A, 0x5B), // LiveDot  · ready to install
    };

    private sealed record Probe(Border Card, Button Action, FrameworkElement Line, Ellipse Dot, TextBlock Title, Border? NotesBox, string Caption, Grid Header, Border Row);

    [STAThread]
    private static int Main(string[] args)
    {
        var appRoot = args.Length > 0 ? args[0] : AppDomain.CurrentDomain.BaseDirectory;
        var appXaml = Path.Combine(appRoot, "src", "VoiceBridge.App", "App.xaml");
        var outDir = Path.Combine(appRoot, "artifacts", "screenshots");
        Directory.CreateDirectory(outDir);

        var dict = LoadResources(appXaml);
        Brush B(string key) => (Brush)dict[key];
        Style S(string key) => (Style)dict[key];

        var notes = string.Join("\n", new[]
        {
            "更新说明上方新增版本标题：版本号以标题字号呈现，安装包体积作为次要信息跟在右侧。",
            "「软件更新」一行只保留状态点和版本号，第二行说明删除。",
            "按钮按「卡片上沿到说明框上沿」整块居中：说明与状态行之间没有横线，整体是通的。",
        });

        var probes = new List<Probe>();
        var log = new List<string>();
        var body = new StackPanel();

        void BuildGroup(
            string caption, string version, string buttonLabel, Style buttonStyle,
            Brush dot, Brush ink, string statusText, string? noteVersion, string? noteSize, bool hasNotes)
        {
            var host = new StackPanel { Width = 660, Margin = new Thickness(0, 0, 0, 26) };
            body.Children.Add(host);
            host.Children.Add(new TextBlock
            {
                Text = caption, Foreground = B("Secondary"), FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var card = new Border { Style = S("Group") };
            var cardStack = new StackPanel();
            card.Child = cardStack;

            // Header grid mirrors MainWindow.xaml: row 0 is the status line, row 1 the
            // notes title, and the button spans both rows so it centres over the whole
            // header region rather than over the status row alone.
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var leftColumn = new StackPanel();
            Grid.SetColumn(leftColumn, 0);
            header.Children.Add(leftColumn);

            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dotShape = new Ellipse
            {
                Width = 9, Height = 9, Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center, Fill = dot
            };
            line.Children.Add(dotShape);
            var title = new TextBlock
            {
                Text = version, Style = S("RowLabel"), FontSize = 15, FontWeight = FontWeights.SemiBold
            };
            line.Children.Add(title);

            var row = new Border { Style = S("Row"), Child = line };
            leftColumn.Children.Add(row);

            if (hasNotes)
            {
                var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 12) };
                heading.Children.Add(new TextBlock { Text = noteVersion!, Style = S("SectionTitle") });
                heading.Children.Add(new TextBlock
                {
                    Text = noteSize!, Style = S("Footnote"),
                    Margin = new Thickness(8, 0, 0, 2), VerticalAlignment = VerticalAlignment.Bottom
                });
                leftColumn.Children.Add(heading);
            }

            var btn = new Button
            {
                Style = buttonStyle, Height = 36, MinWidth = 132,
                Padding = new Thickness(20, 0, 20, 0), FontSize = 14,
                Margin = new Thickness(0, 0, 16, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = ink, Content = buttonLabel,
            };
            Grid.SetColumn(btn, 1);
            header.Children.Add(btn);
            cardStack.Children.Add(header);

            Border? notesBox = null;
            if (hasNotes)
            {
                notesBox = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF9, 0xF7)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xE7, 0xEE, 0xE9)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12, 10, 12, 10),
                };
                var notesPanel = new StackPanel();
                var items = notes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < items.Length; i++)
                {
                    var noteLine = new Grid { Margin = new Thickness(0, 0, 0, i == items.Length - 1 ? 0 : 8) };
                    noteLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                    noteLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    noteLine.Children.Add(new Ellipse
                    {
                        Width = 5, Height = 5, Fill = B("Accent"),
                        VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 7, 0, 0)
                    });
                    var t = new TextBlock
                    {
                        Text = items[i], FontSize = 12, Foreground = B("Label"),
                        TextWrapping = TextWrapping.Wrap, LineHeight = 19
                    };
                    Grid.SetColumn(t, 1);
                    noteLine.Children.Add(t);
                    notesPanel.Children.Add(noteLine);
                }
                notesBox.Child = notesPanel;
                cardStack.Children.Add(new Border { Padding = new Thickness(16, 0, 16, 10), Child = notesBox });
            }

            host.Children.Add(card);
            host.Children.Add(new TextBlock
            {
                Text = statusText, Style = S("Footnote"), Margin = new Thickness(17, 10, 17, 0)
            });
            probes.Add(new Probe(card, btn, line, dotShape, title, notesBox, caption, header, row));
        }

        // ① up to date: grey dot, grey button, no notes
        BuildGroup("① 已是最新 · 灰点 + 灰按钮", "当前版本 v1.5.1", "检查更新",
            S("SecondaryButton"), Freeze(DotColours[0]), B("Label"), "已是最新 v1.5.1。", null, null, false);

        // ② update available: amber dot, mint outline button, notes with title
        BuildGroup("② 发现新版本 · 琥珀点 + 薄荷描边按钮", "当前版本 v1.5.1", "更新到 v1.5.2",
            S("UpdateReadyButton"), Freeze(DotColours[1]), B("UpdateReadyText"), "正在下载 v1.5.2…",
            "v1.5.2", "90.8 MB", true);

        // ③ ready to install: green dot, solid green button
        BuildGroup("③ 下载完成 · 绿点 + 实心绿安装按钮", "当前版本 v1.5.1", "重启安装更新",
            S("UpdateInstallButton"), Freeze(DotColours[2]), B("WhiteText"), "更新包已下载并通过校验，可直接重启安装。",
            "v1.5.2", "90.8 MB", true);

        const double scale = 2.0;
        const double logicalWidth = 660 + 28 * 2;
        var frame = new Border
        {
            Width = logicalWidth,
            Padding = new Thickness(28),
            Background = new SolidColorBrush(Color.FromRgb(0xE4, 0xE9, 0xE6)),
            Child = body,
        };
        var window = new Window
        {
            Content = frame,
            Width = logicalWidth,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -4000,
            Top = -4000,
        };
        window.Show();
        window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));
        window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => { }));
        frame.UpdateLayout();

        var w = (int)Math.Ceiling(logicalWidth);
        var h = (int)Math.Ceiling(frame.ActualHeight);
        var bmp = new RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(frame);

        var stride = bmp.PixelWidth * 4;
        var pixels = new byte[stride * bmp.PixelHeight];
        bmp.CopyPixels(pixels, stride, 0);

        foreach (var probe in probes)
        {
            var cardBox = Box(probe.Card, frame);
            var buttonBox = Box(probe.Action, frame);
            var dotBox = Box(probe.Dot, frame);
            var notesTop = probe.NotesBox is null ? cardBox.Bottom : Box(probe.NotesBox, frame).Top;
            var expected = (cardBox.Top + notesTop) / 2;

            var lineBox = Box(probe.Line, frame);
            var dotCentre = DotBand(pixels, bmp.PixelWidth, bmp.PixelHeight, scale, dotBox);
            // Ink is read inside the status line only: the notes below it are dark too.
            var inkCentre = InkBand(pixels, bmp.PixelWidth, bmp.PixelHeight, scale,
                                    dotBox.Right + 3, lineBox.Right, lineBox.Top, lineBox.Bottom);
            var buttonCentre = buttonBox.Top + buttonBox.Height / 2;

            log.Add(probe.Caption);
            log.Add($"    card {cardBox.Top:0.0}..{cardBox.Bottom:0.0}"
                    + (probe.NotesBox is null ? " (no notes)" : $" / notes box top {notesTop:0.0}"));
            log.Add($"    dot centre {dotCentre:0.0} / title ink centre {inkCentre:0.0}"
                    + $"  delta {dotCentre - inkCentre:+0.0;-0.0;0.0}"
                    + $"  {(Math.Abs(dotCentre - inkCentre) <= 1.0 ? "OK" : "OFF")}");
            log.Add($"    button centre {buttonCentre:0.0} / region centre {expected:0.0}"
                    + $"  delta {buttonCentre - expected:+0.0;-0.0;0.0}"
                    + $"  {(Math.Abs(buttonCentre - expected) <= 1.5 ? "OK" : "OFF")}");
            log.Add($"    line box {lineBox.Top:0.0}..{lineBox.Bottom:0.0} centre {lineBox.Top + lineBox.Height / 2:0.0}"
                    + $" / card h {cardBox.Height:0.0}");
            log.Add($"    heights: header {probe.Header.ActualHeight:0.0}, row {probe.Row.ActualHeight:0.0},"
                    + $" button {probe.Action.ActualHeight:0.0}");
        }

        var outPath = Path.Combine(outDir, "update-panel-with-notes.png");
        using (var fs = File.Create(outPath)) Png(bmp, fs);
        window.Close();
        log.Add($"WROTE {outPath} {w}x{h} (x{scale})");
        File.WriteAllText(Path.Combine(outDir, "update-panel-check.txt"), string.Join("\n", log));
        return 0;
    }

    /// <summary>Screen-space box of an element in the rendered frame.</summary>
    private static Rect Box(FrameworkElement element, Visual root)
    {
        var topLeft = element.TransformToAncestor(root).Transform(new Point(0, 0));
        return new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
    }

    /// <summary>Vertical centre of the solid dot, found by its own fill colour.</summary>
    private static double DotBand(byte[] pixels, int width, int height, double scale, Rect dotRect)
    {
        var x0 = (int)Math.Max(0, dotRect.Left * scale) - 2;
        var x1 = (int)Math.Min(width, dotRect.Right * scale) + 2;
        var y0 = (int)Math.Max(0, dotRect.Top * scale) - 2;
        var y1 = (int)Math.Min(height, dotRect.Bottom * scale) + 2;

        int min = int.MaxValue, max = int.MinValue;
        for (var y = y0; y < y1; y++)
        {
            var run = 0;
            for (var x = x0; x < x1; x++)
            {
                var i = y * width * 4 + x * 4;
                var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
                if (!DotColours.Any(c => Math.Abs(c.R - r) <= 18 && Math.Abs(c.G - g) <= 18 && Math.Abs(c.B - b) <= 18))
                    continue;
                run++;
            }
            // A solid disk gives a long horizontal run; antialiased glyph edges do not.
            if (run < 4) continue;
            min = Math.Min(min, y);
            max = Math.Max(max, y);
        }
        return min == int.MaxValue ? double.NaN : (min + max + 1) / 2.0 / scale;
    }

    /// <summary>Vertical centre of the dark ink inside a horizontal band.</summary>
    private static double InkBand(byte[] pixels, int width, int height, double scale,
                                  double left, double right, double top, double bottom)
    {
        var x0 = (int)Math.Max(0, left * scale);
        var x1 = (int)Math.Min(width, right * scale);
        var y0 = (int)Math.Max(0, top * scale);
        var y1 = (int)Math.Min(height, bottom * scale);

        int min = int.MaxValue, max = int.MinValue;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var i = y * width * 4 + x * 4;
                var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
                if (r > 110 || g > 110 || b > 110) continue;
                min = Math.Min(min, y);
                max = Math.Max(max, y);
            }
        }
        return min == int.MaxValue ? double.NaN : (min + max + 1) / 2.0 / scale;
    }

    private static Brush Freeze((byte R, byte G, byte B) colour)
    {
        var brush = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
        brush.Freeze();
        return brush;
    }

    private static void Png(BitmapSource bmp, Stream outStream)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        enc.Save(outStream);
    }

    private static ResourceDictionary LoadResources(string appXaml)
    {
        const string open = "<Application.Resources>";
        const string close = "</Application.Resources>";
        var text = File.ReadAllText(appXaml);
        var start = text.IndexOf(open, StringComparison.Ordinal);
        var end = text.IndexOf(close, StringComparison.Ordinal);
        if (start < 0 || end < 0) throw new InvalidDataException("App.xaml has no Application.Resources block.");
        var inner = text.Substring(start + open.Length, end - start - open.Length);
        inner = string.Join("\n", inner.Split('\n').Where(line => !line.Contains("local:")));
        var xaml = "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" "
                   + "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" + inner + "</ResourceDictionary>";
        return (ResourceDictionary)XamlReader.Parse(xaml);
    }
}
