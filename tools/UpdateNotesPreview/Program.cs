using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml;

namespace UpdateNotesPreview;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var appRoot = args.Length > 0 ? args[0] : AppDomain.CurrentDomain.BaseDirectory;
        var appXaml = System.IO.Path.Combine(appRoot, "src", "VoiceBridge.App", "App.xaml");
        var outDir = System.IO.Path.Combine(appRoot, "artifacts", "screenshots");
        Directory.CreateDirectory(outDir);

        // Load the app's resource dictionary by extracting only the
        // <Application.Resources> inner block and re-wrapping it, dropping the
        // app-assembly references (local: converters) the standalone preview can't resolve.
        var dict = LoadResources(appXaml);

        Brush B(string key) => (Brush)dict[key];
        Style S(string key) => (Style)dict[key];

        // ---------------- Left card: the "软件更新" settings group ----------------
        var left = new StackPanel { Width = 460, Margin = new Thickness(0, 0, 0, 0) };
        left.Children.Add(new TextBlock
        {
            Text = "软件更新",
            Style = S("Caption"),
            Margin = new Thickness(2, 0, 0, 8)
        });

        var group = new Border
        {
            Style = S("Group"),
            Child = new StackPanel { }
        };
        var stack = (StackPanel)group.Child;

        var rows = new (string Dot, Brush DotFill, Style Btn, string BtnLabel, string Ver, string Sub)[]
        {
            ("gray", B("Secondary"), S("SecondaryButton"), "检查更新", "已是最新 v1.4.9", "已是最新版本"),
            ("mint", B("Positive"), S("UpdateReadyButton"), "更新到 v1.4.9", "发现新版本", "发现新版本"),
            ("green", B("Positive"), S("UpdateInstallButton"), "重启安装更新", "下载完成", "更新包已就绪"),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            var row = new Border { Style = S("Row") };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new Ellipse
            {
                Width = 9,
                Height = 9,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = r.DotFill
            });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = r.Ver,
                Style = S("RowLabel"),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            });
            text.Children.Add(new TextBlock
            {
                Text = r.Sub,
                Style = S("Footnote"),
                Margin = new Thickness(0, 3, 0, 0)
            });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var btn = new Button
            {
                Style = r.Btn,
                Height = 36,
                MinWidth = 132,
                Padding = new Thickness(20, 0, 20, 0),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            if (r.BtnLabel == "重启安装更新")
                btn.Content = new TextBlock { Text = r.BtnLabel, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            else
                btn.Content = r.BtnLabel;
            Grid.SetColumn(btn, 2);
            grid.Children.Add(btn);

            row.Child = grid;
            stack.Children.Add(row);
        }
        left.Children.Add(group);

        // ---------------- Right card: the "版本介绍" dialog ----------------
        var dialog = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFE, 0xFB)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xE3, 0xDF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Width = 544,
            Margin = new Thickness(16),
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Direction = 270, Opacity = 0.24, Color = Color.FromRgb(0x20, 0x26, 0x2B) }
        };
        var dlgGrid = new Grid();
        dlgGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        dlgGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // caption bar with a close glyph
        var caption = new Grid { Background = Brushes.Transparent };
        var closeBtn = new Border
        {
            Width = 42, Height = 30, Margin = new Thickness(4, 3, 4, 3),
            HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, CornerRadius = new CornerRadius(6)
        };
        closeBtn.Child = new System.Windows.Shapes.Path
        {
            Width = 10, Height = 10, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Data = Geometry.Parse("M 1,1 L 9,9 M 9,1 L 1,9"),
            Stroke = B("Secondary"), StrokeThickness = 1, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        caption.Children.Add(closeBtn);
        dlgGrid.Children.Add(caption);

        var body = new Grid { Margin = new Thickness(28, 8, 28, 24) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        head.Children.Add(new TextBlock { Text = "已更新到 v1.4.9", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = B("Label") });
        head.Children.Add(new TextBlock { Text = "看看这次有哪些变化", Style = S("Subtitle"), Margin = new Thickness(0, 6, 0, 0) });
        body.Children.Add(head);

        var notesBox = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE4, 0xE9, 0xE5)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };
        var scroller = new ScrollViewer
        {
            MaxHeight = 340,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(16, 14, 22, 18),
            Content = BuildNotes(S, B)
        };
        notesBox.Child = scroller;
        Grid.SetRow(notesBox, 1);
        body.Children.Add(notesBox);

        var ok = new Button
        {
            Style = S("PrimaryButton"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 20, 0, 0),
            Content = new TextBlock { Text = "知道了", Foreground = Brushes.White, TextWrapping = TextWrapping.NoWrap }
        };
        Grid.SetRow(ok, 2);
        body.Children.Add(ok);

        Grid.SetRow(body, 1);
        dlgGrid.Children.Add(body);
        dialog.Child = dlgGrid;

        // ---------------- Compose ----------------
        var combo = new StackPanel { Orientation = Orientation.Horizontal };
        combo.Children.Add(left);
        combo.Children.Add(new Border { Width = 44, Background = Brushes.Transparent });
        combo.Children.Add(dialog);
        combo.Width = 460 + 44 + 576; // left + spacer + dialog(544 + 16*2 margin)

        var label = new TextBlock
        {
            Text = "左：主窗口「软件更新」分组（三态按钮）    右：更新后弹出的「版本介绍」对话框",
            Foreground = B("Secondary"), FontSize = 12, Margin = new Thickness(28, 14, 28, 0)
        };

        var root = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(0xE4, 0xE9, 0xE6)),
            Margin = new Thickness(28)
        };
        root.Children.Add(label);
        root.Children.Add(combo);

        const double scale = 2.0;
        var window = new Window
        {
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -4000,
            Top = -4000
        };
        window.Show();
        window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));
        window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => { }));
        root.UpdateLayout();
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        var w = (int)Math.Ceiling(root.ActualWidth);
        var h = (int)Math.Ceiling(root.ActualHeight);
        Console.WriteLine($"DEBUG root={root.ActualWidth}x{root.ActualHeight} combo={combo.ActualWidth}x{combo.ActualHeight} dialog={dialog.ActualWidth}x{dialog.ActualHeight} left={left.ActualWidth}");
        var bmp = new RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(root);

        var outPath = System.IO.Path.Combine(outDir, "update-combo.png");
        using (var fs = File.Create(outPath))
            PngBitmapEncoderWith(bmp, fs);
        window.Close();
        Console.WriteLine($"WROTE {outPath} {w}x{h} (x{scale})");
        return 0;
    }

    private static UIElement BuildNotes(Func<string, Style> S, Func<string, Brush> B)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "本次更新", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12), Foreground = B("Label") });

        var changes = new[]
        {
            "真实微信模式支持范围限定为微信 Windows 4.1.11 至 4.2.0.13；范围外版本不再静默失败或乱读数据，而是给出明确提示。",
            "密钥掩码保持与原有实现逐字节一致，58 项等价性自检全部通过；不回退 1.4.7 / 1.4.8 的改动。",
            "启动识别到范围外微信版本时弹出提示窗口，显示检测到的版本与受支持区间，并提供「更新微信」与「退出应用」。",
            "更新页精简：删去重复解释文案；下载完成的绿色安装按钮与版本文字改为统一固定高度、文字垂直居中，整体更整洁。",
        };
        foreach (var c in changes)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = "•", Foreground = B("Accent") });
            row.Children.Add(new TextBlock
            {
                Text = c,
                LineHeight = 23,
                TextWrapping = TextWrapping.Wrap,
                Foreground = B("Label")
            });
            Grid.SetColumn(row.Children[1], 1);
            panel.Children.Add(row);
        }
        panel.Children.Add(new TextBlock
        {
            Text = "完整版本记录可在「使用说明 → 更新日志」查看。",
            Style = S("Hint"),
            Margin = new Thickness(0, 2, 0, 0)
        });
        return panel;
    }

    private static void PngBitmapEncoderWith(BitmapSource bmp, Stream outStream)
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
