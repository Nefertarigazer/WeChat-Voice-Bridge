using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;

namespace MenuPreview;

/// <summary>
/// Renders the service row's "⋮" overflow menu with the app's own styles, so the
/// panel can be judged next to the cards: resting state plus one item hovered.
/// Resources come from App.xaml — the menu is not re-invented here.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var root = args.Length > 0 ? args[0] : AppDomain.CurrentDomain.BaseDirectory;
        var appXaml = Path.Combine(root, "src", "VoiceBridge.App", "App.xaml");
        var outDir = Path.Combine(root, "artifacts", "screenshots");
        Directory.CreateDirectory(outDir);

        var dict = LoadResources(appXaml);
        Style MenuStyle() => (Style)dict["RowMenu"];
        Style ItemStyle() => (Style)dict["RowMenuItem"];
        Style DangerStyle() => (Style)dict["RowMenuItemDanger"];

        // The page behind the menu, so the shadow and the rounded corners are judged
        // against the real background instead of a flat white sheet.
        var page = new Border { Background = (Brush)dict["PageBg"], Padding = new Thickness(34) };
        var stack = new StackPanel();
        page.Child = stack;

        stack.Children.Add(Label("静态"));
        stack.Children.Add(Panel(MenuStyle(), ItemStyle(), DangerStyle(), -1));

        stack.Children.Add(Label("悬停「编辑」"));
        stack.Children.Add(Panel(MenuStyle(), ItemStyle(), DangerStyle(), 0));

        stack.Children.Add(Label("悬停「删除」"));
        stack.Children.Add(Panel(MenuStyle(), ItemStyle(), DangerStyle(), 1));

        var width = 300.0;
        var height = 566.0;
        page.Measure(new Size(width, height));
        page.Arrange(new Rect(0, 0, width, height));
        page.UpdateLayout();

        var dpi = 192.0;
        var bmp = new RenderTargetBitmap(
            (int)(width * dpi / 96), (int)(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(page);

        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        var target = Path.Combine(outDir, "menu-preview.png");
        using (var fs = File.Create(target))
            png.Save(fs);
        Console.WriteLine($"saved: {target}");
        return 0;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Margin = new Thickness(2, 0, 0, 8),
        Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x73)),
    };

    /// <summary>
    /// Builds the menu panel exactly as RowMenu does — transparent shadow ring,
    /// white rounded frame, item stack — and optionally highlights one item.
    /// </summary>
    private static FrameworkElement Panel(Style menu, Style item, Style danger, int hovered)
    {
        var items = new StackPanel();
        items.Children.Add(Row("编辑", item, hovered == 0));
        items.Children.Add(Row("删除", danger, hovered == 1));

        var panel = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE9, 0xEE, 0xEC)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = 0.16,
                Color = Color.FromRgb(0x2B, 0x3A, 0x35),
            },
            Child = items,
        };
        var ring = new Border
        {
            Padding = new Thickness(10),
            Background = Brushes.Transparent,
            Child = panel,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 22),
        };
        _ = menu;   // template of ContextMenu itself is exercised by the app, not here
        return ring;
    }

    private static MenuItem Row(string header, Style style, bool highlighted)
    {
        var item = new MenuItem { Header = header, Style = style, Width = 124 };
        if (!highlighted)
            return item;
        // IsHighlighted has an internal setter (it is normally driven by the mouse).
        var prop = typeof(MenuItem).GetProperty("IsHighlighted", BindingFlags.Instance | BindingFlags.Public);
        var setter = prop?.GetSetMethod(true);
        setter?.Invoke(item, new object[] { true });
        return item;
    }

    private static ResourceDictionary LoadResources(string appXaml)
    {
        var xaml = File.ReadAllText(appXaml);
        var start = xaml.IndexOf("<Application.Resources>", StringComparison.Ordinal);
        var end = xaml.IndexOf("</Application.Resources>", StringComparison.Ordinal);
        if (start < 0 || end < start)
            throw new InvalidOperationException("Application.Resources not found");
        var body = xaml.Substring(
            start + "<Application.Resources>".Length,
            end - start - "<Application.Resources>".Length);
        // Any reference to app-local types (attached behaviours) is dropped: the
        // preview only needs the visual styles, and those markers are single lines.
        var inner = string.Join("\n", body.Split('\n')
            .Where(line => !line.TrimStart().StartsWith("<local:", StringComparison.Ordinal))
            .Where(line => !line.Contains("local:", StringComparison.Ordinal)));
        return (ResourceDictionary)XamlReader.Parse(
            "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" + inner + "</ResourceDictionary>");
    }
}
