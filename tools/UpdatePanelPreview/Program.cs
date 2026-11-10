using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace UpdatePanelPreview;

/// <summary>
/// Renders the real "软件更新" settings group the way the app draws it:
/// the status row (dot + 当前版本 + subtext + button) with the version-notes
/// block (v1.4.9 · size + release text) directly below it. Two states are
/// rendered stacked so the button and the copy under it can be judged together.
/// Uses the app's own App.xaml resources, so styles are not re-invented.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var appRoot = args.Length > 0 ? args[0] : AppDomain.CurrentDomain.BaseDirectory;
        var appXaml = System.IO.Path.Combine(appRoot, "src", "VoiceBridge.App", "App.xaml");
        var outDir = System.IO.Path.Combine(appRoot, "artifacts", "screenshots");
        Directory.CreateDirectory(outDir);

        var dict = LoadResources(appXaml);
        Brush B(string key) => (Brush)dict[key];
        Style S(string key) => (Style)dict[key];

        // Real 1.4.9 release notes, as they would arrive in latest.json "notes".
        var notes = string.Join("\n", new[]
        {
            "真实微信模式支持范围限定为微信 Windows 4.1.11 至 4.2.0.13（含两端及其间全部小版本）。范围外的版本不再静默失败或乱读数据，而是走 unsupported_version 通道并给出明确提示。",
            "密钥掩码保持与原有实现逐字节一致，58 项等价性自检全部通过；不回退 1.4.7 / 1.4.8 的改动。",
            "启动识别到范围外微信版本时弹出提示窗口，显示检测到的版本与受支持区间，并给出「更新微信」与「退出应用」两个操作。",
            "更新页精简：删去「点确定才会进入更新应用」等重复解释文案；绿色安装按钮与版本文字改为统一固定高度、文字垂直居中，三种状态尺寸一致。",
        });

        // -------- one full group, faithful to MainWindow.xaml (content width 660) --------
        FrameworkElement BuildGroup(
            string stateLabel, string subtext, string buttonLabel, Style buttonStyle, Brush dot,
            string statusText, string caption)
        {
            var host = new StackPanel { Width = 660, Margin = new Thickness(0, 0, 0, 26) };

            host.Children.Add(new TextBlock
            {
                Text = caption,
                Foreground = B("Secondary"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var group = new Border { Style = S("Group") };
            var groupStack = new StackPanel();
            group.Child = groupStack;

            // --- status row ---
            var row = new Border { Style = S("Row") };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new Ellipse
            {
                Width = 9, Height = 9, Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center, Fill = dot
            });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = "当前版本 v1.4.9", Style = S("RowLabel"), FontSize = 15, FontWeight = FontWeights.SemiBold
            });
            text.Children.Add(new TextBlock { Text = subtext, Style = S("Footnote"), Margin = new Thickness(0, 3, 0, 0) });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var btn = new Button
            {
                Style = buttonStyle,
                Height = 36,
                MinWidth = 132,
                Padding = new Thickness(20, 0, 20, 0),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            // Mirror MainWindow.Updates.cs: label colour is set explicitly, so the
            // solid-green state keeps white ink instead of inheriting dark text.
            btn.Foreground = B(buttonStyle == S("UpdateInstallButton") ? "WhiteText" : "UpdateReadyText");
            btn.Content = buttonLabel;
            Grid.SetColumn(btn, 2);
            grid.Children.Add(btn);

            row.Child = grid;
            groupStack.Children.Add(row);

            // --- version-notes block directly under the button row (new layout) ---
            var package = new Border { Style = S("Row") };
            var packageStack = new StackPanel();

            var sizeRow = new Grid();
            sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sizeRow.Children.Add(new TextBlock { Text = "v1.4.9 · 89.5 MB", Style = S("RowLabel") });
            var notesTag = new TextBlock { Text = "更新内容", Style = S("Footnote"), Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(notesTag, 1);
            sizeRow.Children.Add(notesTag);
            packageStack.Children.Add(sizeRow);

            var inset = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF9, 0xF7)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE7, 0xEE, 0xE9)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 8, 0, 0),
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
            packageStack.Children.Add(inset);

            package.Child = packageStack;
            groupStack.Children.Add(package);

            host.Children.Add(group);

            // --- status line under the group (UpdateStatus, Footnote) ---
            host.Children.Add(new TextBlock
            {
                Text = statusText, Style = S("Footnote"), Margin = new Thickness(17, 10, 17, 0)
            });

            return host;
        }

        var body = new StackPanel();

        body.Children.Add(BuildGroup(
            stateLabel: "发现新版本", subtext: "发现新版本", buttonLabel: "更新到 v1.4.9",
            buttonStyle: S("UpdateReadyButton"), dot: B("Positive"),
            statusText: "正在下载 v1.4.9…", caption: "① 发现新版本 · 薄荷描边按钮"));

        body.Children.Add(BuildGroup(
            stateLabel: "更新包已就绪", subtext: "更新包已就绪", buttonLabel: "重启安装更新",
            buttonStyle: S("UpdateInstallButton"), dot: B("Positive"),
            statusText: "更新包已下载并通过校验，可直接重启安装。", caption: "② 下载完成 · 实心绿安装按钮"));

        const double scale = 2.0;
        // Wrap in a fixed-width frame with real padding instead of setting Width on a
        // margined element (that combination shifts the layout and crops the right edge).
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
        frame.Measure(new Size(logicalWidth, double.PositiveInfinity));
        frame.Arrange(new Rect(0, 0, logicalWidth, frame.DesiredSize.Height));
        var w = (int)Math.Ceiling(logicalWidth);
        var h = (int)Math.Ceiling(frame.DesiredSize.Height);

        var bmp = new RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(frame);

        var outPath = System.IO.Path.Combine(outDir, "update-panel-with-notes.png");
        using (var fs = File.Create(outPath)) Png(bmp, fs);
        window.Close();
        Console.WriteLine($"WROTE {outPath} {w}x{h} (x{scale})");
        return 0;
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
