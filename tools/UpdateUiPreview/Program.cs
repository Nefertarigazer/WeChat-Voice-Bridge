using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace UpdateUiPreview;

/// <summary>
/// Design-time helper: renders the real "软件更新" row from App.xaml resources in
/// its three states and writes PNGs for review. Not part of the solution or any
/// shipped payload. Run: dotnet run --project tools/UpdateUiPreview -- &lt;repo-root&gt;
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var root = args.Length > 0 ? args[0] : FindRepoRoot();
        var appXaml = Path.Combine(root, "src", "VoiceBridge.App", "App.xaml");
        var output = Path.Combine(root, "artifacts", "screenshots");
        Directory.CreateDirectory(output);

        var resources = LoadResources(appXaml);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(resources);

        Style S(string key) => (Style)app.Resources[key];
        Brush B(string key) => (Brush)app.Resources[key];

        var states = new (string Caption, string Label, string Sub, Style Button, Brush Dot, Brush Ink)[]
        {
            ("① 已是最新 · 灰色", "检查更新", "已是最新版本", S("SecondaryButton"), B("Tertiary"), B("Label")),
            ("② 发现新版本 · 薄荷描边", "更新到 v1.4.9", "发现新版本", S("UpdateReadyButton"), B("Caution"), B("UpdateReadyText")),
            ("③ 下载完成 · 绿色安装按钮", "重启安装更新", "更新包已就绪", S("UpdateInstallButton"), B("Positive"), B("WhiteText")),
        };

        const double width = 720;
        var all = BuildSurface(S, B, states, width, includeHeader: true);
        var green = BuildSurface(S, B, states.Skip(2).ToArray(), 560, includeHeader: false);

        Save(Show(all, width), Path.Combine(output, "update-ui-states.png"), 96);
        Save(Show(green, 560), Path.Combine(output, "update-install-green.png"), 192);

        app.Shutdown();
        Console.WriteLine("SCREENSHOTS:");
        Console.WriteLine(Path.Combine(output, "update-ui-states.png"));
        Console.WriteLine(Path.Combine(output, "update-install-green.png"));
        return 0;
    }

    private static Grid BuildSurface(
        Func<string, Style> S, Func<string, Brush> B,
        (string Caption, string Label, string Sub, Style Button, Brush Dot, Brush Ink)[] states,
        double width, bool includeHeader)
    {
        var stack = new StackPanel { Margin = new Thickness(28) };
        if (includeHeader)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "软件更新", FontSize = 20, FontWeight = FontWeights.SemiBold,
                Foreground = B("Label"), Margin = new Thickness(0, 0, 0, 4)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "微信语音桥 · 高级设置 · 使用真实 App.xaml 样式渲染",
                FontSize = 12, Foreground = B("Secondary"), Margin = new Thickness(0, 0, 0, 20)
            });
        }
        foreach (var state in states)
        {
            if (includeHeader)
                stack.Children.Add(new TextBlock
                {
                    Text = state.Caption, FontSize = 13, Foreground = B("Secondary"),
                    Margin = new Thickness(4, 0, 0, 8)
                });

            var group = new Border { Style = S("Group"), Margin = new Thickness(0, 0, 0, 18) };
            var row = new Border { Style = S("Row") };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Ellipse
            {
                Width = 9, Height = 9, Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center, Fill = state.Dot
            };
            Grid.SetColumn(dot, 0);
            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(copy, 1);
            copy.Children.Add(new TextBlock
            {
                Text = "当前版本 v1.4.8", Style = S("RowLabel"),
                FontSize = 15, FontWeight = FontWeights.SemiBold
            });
            copy.Children.Add(new TextBlock
            {
                Text = state.Sub, Style = S("Footnote"), Margin = new Thickness(0, 3, 0, 0)
            });
            // Local Height/Padding match MainWindow.xaml exactly: the label is
            // centred by VerticalContentAlignment with zero vertical padding.
            var button = new Button
            {
                Content = state.Label, Style = state.Button, Foreground = state.Ink,
                Height = 36, MinWidth = 132, Padding = new Thickness(20, 0, 20, 0), FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(button, 2);
            grid.Children.Add(dot); grid.Children.Add(copy); grid.Children.Add(button);
            row.Child = grid; group.Child = row;
            stack.Children.Add(group);
        }

        return new Grid { Background = B("PageBg"), Width = width, Children = { stack } };
    }

    private static Grid Show(Grid surface, double width)
    {
        var window = new Window
        {
            Content = surface, Width = width, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, AllowsTransparency = true, Background = Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -3000, Top = -3000
        };
        window.Show();
        window.Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
        window.Dispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
        surface.UpdateLayout();
        window.Close();
        return surface;
    }

    private static void Save(Grid surface, string path, double dpi)
    {
        var w = Math.Max(1, (int)Math.Ceiling(surface.ActualWidth * dpi / 96));
        var h = Math.Max(1, (int)Math.Ceiling(surface.ActualHeight * dpi / 96));
        var bitmap = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine($"WROTE {path} ({w}x{h} @ {dpi}dpi)");
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
        // Two app-assembly types are referenced (InverseBoolToVis converter and the
        // SwitchMotion attached property). Drop those lines for this standalone
        // preview; every other resource is a pure framework type and neither affects
        // the update row's appearance.
        inner = string.Join("\n", inner.Split('\n').Where(line => !line.Contains("local:")));
        var xaml = "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" "
                   + "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" + inner + "</ResourceDictionary>";
        return (ResourceDictionary)XamlReader.Parse(xaml);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WeChatVoiceBridge.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Pass the repository root as the first argument.");
    }
}
