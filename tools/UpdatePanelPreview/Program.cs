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
/// Renders the real "软件更新" group the way the app draws it and then MEASURES the
/// result: the status dot must centre on the version text in all three states, and
/// the notes block now carries a version title with the size beside it.
/// The three states are stacked so the button, the dot and the copy are judged together.
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
            "更新说明上方新增版本标题：版本号以标题字号呈现，安装包体积作为次要信息跟在右侧，不再是挤在正文前的一行小字。",
            "修正「软件更新」一行里状态圆点与文字的纵向对齐：圆点改为与版本文字同一行居中，不再偏低；未开始（灰）、发现新版（琥珀）、可安装（绿）三种状态一致对齐。",
            "API 配置界面精简、音色不再无故消失、凭据指针与钥匙库清理等 1.5.0 的改动继续保留。",
        });

        var rows = new List<(Grid Left, Ellipse Dot, TextBlock Title, string Caption)>();
        var log = new List<string>();
        var body = new StackPanel();

        FrameworkElement BuildGroup(
            string caption, string version, string buttonLabel,
            Style buttonStyle, Brush dot, Brush ink, string statusText,
            string? noteVersion, string? noteSize, bool hasNotes)
        {
            var host = new StackPanel { Width = 660, Margin = new Thickness(0, 0, 0, 26) };
            body.Children.Add(host);

            host.Children.Add(new TextBlock
            {
                Text = caption, Foreground = B("Secondary"), FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var group = new Border { Style = S("Group") };
            var groupStack = new StackPanel();
            group.Child = groupStack;

            // --- status row: exactly the live structure (dot + title share row 0) ---
            var row = new Border { Style = S("Row") };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            left.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dotShape = new Ellipse
            {
                Width = 9, Height = 9, Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center, Fill = dot
            };
            left.Children.Add(dotShape);

            var title = new TextBlock
            {
                Text = version, Style = S("RowLabel"), FontSize = 15, FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(title, 1);
            left.Children.Add(title);

            grid.Children.Add(left);

            var btn = new Button
            {
                Style = buttonStyle, Height = 36, MinWidth = 132,
                Padding = new Thickness(20, 0, 20, 0), FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = ink, Content = buttonLabel,
            };
            Grid.SetColumn(btn, 1);
            grid.Children.Add(btn);

            row.Child = grid;
            groupStack.Children.Add(row);
            rows.Add((left, dotShape, title, caption));

            // --- notes block: version title + size, then the dotted lines ---
            if (hasNotes)
            {
                var package = new Border { Style = S("Row") };
                var stack = new StackPanel();

                var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 10) };
                heading.Children.Add(new TextBlock { Text = noteVersion!, Style = S("SectionTitle") });
                heading.Children.Add(new TextBlock
                {
                    Text = noteSize!, Style = S("Footnote"),
                    Margin = new Thickness(8, 0, 0, 2), VerticalAlignment = VerticalAlignment.Bottom
                });
                stack.Children.Add(heading);

                var inset = new Border
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
                    var line = new Grid { Margin = new Thickness(0, 0, 0, i == items.Length - 1 ? 0 : 8) };
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    line.Children.Add(new Ellipse
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
                    line.Children.Add(t);
                    notesPanel.Children.Add(line);
                }
                inset.Child = notesPanel;
                stack.Children.Add(inset);
                package.Child = stack;
                groupStack.Children.Add(package);
            }

            host.Children.Add(group);
            host.Children.Add(new TextBlock
            {
                Text = statusText, Style = S("Footnote"), Margin = new Thickness(17, 10, 17, 0)
            });
            return host;
        }

        // ① up to date: grey dot, grey button, no notes
        BuildGroup("① 已是最新 · 灰点 + 灰按钮", "当前版本 v1.5.1", "检查更新",
            S("SecondaryButton"), Freeze(DotColours[0]), B("Label"), "已是最新版本。", null, null, false);

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

        // ---- measure the rendered pixels: dot centre vs title ink centre ----
        var stride = bmp.PixelWidth * 4;
        var pixels = new byte[stride * bmp.PixelHeight];
        bmp.CopyPixels(pixels, stride, 0);

        foreach (var (left, dotShape, title, caption) in rows)
        {
            var cellRect = Box(left, frame);
            var dotRect = Box(dotShape, frame);
            var geo = Geometry(left, dotShape, title, frame);
            var report = Measure(pixels, bmp.PixelWidth, bmp.PixelHeight, scale, dotRect, cellRect);
            var delta = report.DotCentreY - report.InkCentreY;
            log.Add($"{caption}");
            log.Add($"    layout : dot centre {geo.DotCentre:0.0} / title box centre {geo.TitleCentre:0.0} / title h {geo.TitleHeight:0.0} / block h {geo.BlockHeight:0.0}");
            log.Add($"    pixels : dot centre {report.DotCentreY:0.0} / title ink {report.InkTop:0.0}..{report.InkBottom:0.0} centre {report.InkCentreY:0.0}");
            log.Add($"    delta  : {delta:+0.0;-0.0;0.0} DIP   {(Math.Abs(delta) <= 1.0 ? "OK" : "OFF")}");
        }

        var outPath = Path.Combine(outDir, "update-panel-with-notes.png");
        using (var fs = File.Create(outPath)) Png(bmp, fs);
        window.Close();
        log.Add($"WROTE {outPath} {w}x{h} (x{scale})");
        File.WriteAllText(Path.Combine(outDir, "update-panel-check.txt"), string.Join("\n", log));
        return 0;
    }

    private readonly record struct MeasureResult(double DotCentreY, double InkTop, double InkBottom)
    {
        public double InkCentreY => (InkTop + InkBottom) / 2;
    }

    private readonly record struct Geo(double DotCentre, double TitleCentre, double TitleHeight, double BlockHeight);

    /// <summary>Layout arithmetic straight from WPF, independent of the pixel scan.</summary>
    private static Geo Geometry(Grid cell, Ellipse dot, TextBlock title, Visual root)
    {
        var cellTop = cell.TransformToAncestor(root).Transform(new Point(0, 0)).Y;
        var dotTop = dot.TransformToAncestor(root).Transform(new Point(0, 0)).Y;
        var titleTop = title.TransformToAncestor(root).Transform(new Point(0, 0)).Y;
        return new Geo(dotTop + dot.ActualHeight / 2, titleTop + title.ActualHeight / 2,
            title.ActualHeight, cell.ActualHeight);
    }

    /// <summary>Screen-space box of an element in the rendered frame.</summary>
    private static Rect Box(FrameworkElement element, Visual root)
    {
        var topLeft = element.TransformToAncestor(root).Transform(new Point(0, 0));
        return new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
    }

    /// <summary>
    /// Reads the rendered pixels: the dot's own rows by fill colour, then the title's
    /// ink by darkness to the right of the dot. Comparing the two centres is the same
    /// judgement the eye makes, so it cannot be talked around by layout arithmetic.
    /// </summary>
    private static MeasureResult Measure(byte[] pixels, int width, int height, double scale, Rect dotRect, Rect cellRect)
    {
        var dx0 = (int)Math.Max(0, dotRect.Left * scale) - 2;
        var dx1 = (int)Math.Min(width, dotRect.Right * scale) + 2;
        var dy0 = (int)Math.Max(0, dotRect.Top * scale) - 2;
        var dy1 = (int)Math.Min(height, dotRect.Bottom * scale) + 2;

        int dotMin = int.MaxValue, dotMax = int.MinValue, found = 0;
        for (var y = dy0; y < dy1; y++)
        {
            var row = 0;
            for (var x = dx0; x < dx1; x++)
            {
                var i = y * width * 4 + x * 4;
                var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
                if (!DotColours.Any(c => Math.Abs(c.R - r) <= 18 && Math.Abs(c.G - g) <= 18 && Math.Abs(c.B - b) <= 18))
                    continue;
                row++;
            }
            // A solid disk gives a long run; scattered antialiased glyph pixels do not.
            if (row < 4) continue;
            dotMin = Math.Min(dotMin, y);
            dotMax = Math.Max(dotMax, y);
            found += row;
        }

        var x0 = (int)Math.Min(width, (dotRect.Right + 3) * scale);
        var x1 = (int)Math.Min(width, cellRect.Right * scale) - 2;
        var y0 = (int)Math.Max(0, cellRect.Top * scale);
        var y1 = (int)Math.Min(height, cellRect.Bottom * scale);

        int inkMin = int.MaxValue, inkMax = int.MinValue;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var i = y * width * 4 + x * 4;
                var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
                if (r > 110 || g > 110 || b > 110) continue;
                inkMin = Math.Min(inkMin, y);
                inkMax = Math.Max(inkMax, y);
            }
        }

        if (found == 0 || inkMin == int.MaxValue)
            return new MeasureResult(double.NaN, double.NaN, double.NaN);
        return new MeasureResult((dotMin + dotMax + 1) / 2.0 / scale, inkMin / scale, (inkMax + 1) / scale);
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
