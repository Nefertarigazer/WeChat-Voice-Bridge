using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace VoiceBridge.App;

/// <summary>浅色、深色，或跟随 Windows. Stored as a number: append only.</summary>
public enum ThemeMode { Light = 0, Dark = 1, System = 2 }

/// <summary>The color the theme is tinted with; 默认 is the neutral one. Stored as a number: append only.</summary>
public enum ThemeTint { Default = 0, Green = 1, Pink = 2, Blue = 3 }

public sealed record AppearanceSettings
{
    public ThemeMode Mode { get; init; } = ThemeMode.Light;
    public ThemeTint Tint { get; init; } = ThemeTint.Default;

    /// <summary>Unknown numbers from a newer or edited settings file fall back to the default.</summary>
    public AppearanceSettings Valid() => new()
    {
        Mode = Enum.IsDefined(Mode) ? Mode : ThemeMode.Light,
        Tint = Enum.IsDefined(Tint) ? Tint : ThemeTint.Default
    };
}

/// <summary>
/// The app's colors. Every color the UI shows is an application resource that XAML reads with
/// DynamicResource, so applying a theme swaps the resources and every window repaints at
/// once. Controls that paint in code listen to <see cref="Changed"/>.
/// </summary>
internal static class Theme
{
    public static AppearanceSettings Choice { get; private set; } = new();
    public static bool IsDark { get; private set; }
    /// <summary>Raised on the UI thread after the resources changed.</summary>
    public static event EventHandler? Changed;

    public static void Apply(AppearanceSettings choice)
    {
        Choice = choice.Valid();
        IsDark = Choice.Mode == ThemeMode.Dark || Choice.Mode == ThemeMode.System && WindowsPrefersDark();
        var resources = Application.Current.Resources;
        foreach (var (key, color) in ThemePalette.Build(IsDark, Choice.Tint))
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
            // Gradients need the bare color, under the same name plus "Color".
            resources[key + "Color"] = color;
        }
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public static Color ColorOf(string key) => (Color)Application.Current.Resources[key + "Color"];

    /// <summary>
    /// Follows Windows' light/dark app setting while 跟随系统 is chosen. Windows announces the
    /// change as a general preference change; only a real flip of light and dark repaints.
    /// </summary>
    public static void FollowWindows()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General || Application.Current is not { } app) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Choice.Mode == ThemeMode.System && WindowsPrefersDark() != IsDark) Apply(Choice);
            }));
        };
    }

    public static bool WindowsPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// The saved choice, read straight from settings.json so the first window already opens in
    /// it (the full settings load and migrate later, inside the main window).
    /// </summary>
    public static AppearanceSettings ReadSaved()
    {
        try
        {
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WeChatVoiceBridge", "settings.json");
            if (!File.Exists(file)) return new();
            using var document = JsonDocument.Parse(File.ReadAllBytes(file));
            return document.RootElement.TryGetProperty(nameof(LocalSettings.Appearance), out var value)
                ? (value.Deserialize<AppearanceSettings>() ?? new()).Valid() : new();
        }
        catch (Exception) { return new(); }
    }
}

/// <summary>
/// Builds every color token for one mode and tint. Each tint sets a handful of base colors
/// (surfaces, text, accent); the rest is mixed from them, so all tints stay consistent.
/// </summary>
internal static class ThemePalette
{
    private sealed record Base(string Page, string Sidebar, string Card, string Popup, string Dialog, string DialogBorder,
        string Fill, string FillStrong, string Separator, string GroupBorder, string Label, string Secondary,
        string Tertiary, string Glyph, string Accent);

    // Light: the default is Apple-like neutral grey with system blue; the tints keep the same
    // structure with a faint hue in the surfaces. 浅绿 is the app's look before 1.8.
    private static readonly Base LightDefault = new("#F5F5F7", "#EDEDF0", "#FFFFFF", "#FFFFFF", "#F7F7F9", "#DCDCE1",
        "#F1F1F4", "#E5E5EA", "#E8E8ED", "#E9E9EE", "#1D1D1F", "#6E6E73", "#AEAEB2", "#5C5C61", "#007AFF");
    private static readonly Base LightGreen = new("#F6F8F7", "#EDF2F0", "#FFFFFF", "#FFFFFF", "#FFFEFB", "#DCE3DF",
        "#F0F4F2", "#E3E9E6", "#E6EBE9", "#E9EEEC", "#1D1D1F", "#6E7673", "#A8B0AC", "#5C5C61", "#0B8F66");
    private static readonly Base LightPink = new("#FBF6F8", "#F6ECF0", "#FFFFFF", "#FFFFFF", "#FFFCFD", "#EBDCE2",
        "#F8F0F3", "#EFE3E8", "#F0E6EA", "#F2E9ED", "#201C1E", "#7A6F74", "#B8ADB2", "#66595F", "#D9457C");
    private static readonly Base LightBlue = new("#F4F8FC", "#EAF1F8", "#FFFFFF", "#FFFFFF", "#FBFDFF", "#D8E2ED",
        "#EEF3F9", "#E0E8F1", "#E3EAF2", "#E6ECF3", "#1B1E22", "#69727D", "#A5AEB9", "#56606B", "#1F88D8");

    // Dark: near-black page, raised cards, the same faint hue per tint.
    private static readonly Base DarkDefault = new("#1C1C1E", "#161618", "#2A2A2D", "#333337", "#232326", "#3A3A3F",
        "#333337", "#404045", "#3A3A3E", "#37373B", "#F2F2F7", "#A1A1A8", "#6C6C73", "#C7C7CC", "#0A84FF");
    private static readonly Base DarkGreen = new("#171C1A", "#121614", "#232A27", "#2B3330", "#1E2422", "#33403B",
        "#2C3431", "#38423E", "#323B37", "#2F3834", "#EEF4F1", "#9CA9A3", "#65716C", "#C3CEC9", "#1FA874");
    private static readonly Base DarkPink = new("#1D191B", "#171415", "#2B2428", "#342C30", "#241F22", "#42373C",
        "#352D31", "#41383D", "#3B3236", "#383034", "#F7F0F3", "#ADA1A7", "#74696E", "#D0C4CA", "#E8558A");
    private static readonly Base DarkBlue = new("#181B20", "#13161A", "#242930", "#2C323A", "#1F2329", "#364049",
        "#2D333B", "#39404A", "#333A43", "#30373F", "#EEF3F8", "#9BA5B1", "#646E7A", "#C1C9D3", "#2F93E0");

    public static Dictionary<string, Color> Build(bool dark, ThemeTint tint)
    {
        var b = (dark, tint) switch
        {
            (false, ThemeTint.Green) => LightGreen,
            (false, ThemeTint.Pink) => LightPink,
            (false, ThemeTint.Blue) => LightBlue,
            (false, _) => LightDefault,
            (true, ThemeTint.Green) => DarkGreen,
            (true, ThemeTint.Pink) => DarkPink,
            (true, ThemeTint.Blue) => DarkBlue,
            _ => DarkDefault
        };
        var t = new Dictionary<string, Color>(StringComparer.Ordinal);
        Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        var white = Colors.White;
        var black = Colors.Black;
        var card = C(b.Card);
        var page = C(b.Page);
        var label = C(b.Label);
        var secondary = C(b.Secondary);
        var fill = C(b.Fill);
        var fillStrong = C(b.FillStrong);
        var accent = C(b.Accent);

        // ---- surfaces
        t["PageBg"] = page;
        t["PageBgClear"] = WithAlpha(page, 0);
        t["PageFade1"] = WithAlpha(page, 0x22);
        t["PageFade2"] = WithAlpha(page, 0x66);
        t["PageFade3"] = WithAlpha(page, 0xB8);
        t["SidebarBg"] = C(b.Sidebar);
        t["CardBg"] = card;
        t["PopupBg"] = C(b.Popup);
        t["DialogBg"] = C(b.Dialog);
        t["DialogBorder"] = C(b.DialogBorder);
        t["Fill"] = fill;
        t["FillStrong"] = fillStrong;
        t["FillSubtle"] = Mix(card, fill, 0.55);
        t["FillPressed"] = Mix(fillStrong, label, dark ? 0.08 : 0.06);
        t["Separator"] = C(b.Separator);
        t["GroupBorder"] = C(b.GroupBorder);
        t["ControlBorder"] = Mix(card, secondary, dark ? 0.26 : 0.2);
        t["ControlBorderStrong"] = Mix(card, secondary, dark ? 0.4 : 0.34);
        t["PanelBg"] = dark ? Mix(page, card, 0.45) : Mix(page, C(b.Sidebar), 0.6);
        t["PanelBorder"] = Mix(t["PanelBg"], secondary, dark ? 0.16 : 0.1);
        t["InputFocusBg"] = dark ? Mix(page, card, 0.3) : card;
        t["NavOn"] = dark ? Mix(card, label, 0.08) : card;
        t["SegmentOn"] = dark ? Mix(fillStrong, label, 0.14) : card;
        t["SwitchOff"] = Mix(fill, label, 0.12);
        t["Knob"] = white;
        t["TrackBg"] = Mix(fill, label, dark ? 0.1 : 0.08);
        t["ScrollThumbBg"] = Mix(fill, label, dark ? 0.24 : 0.18);
        t["CheckBg"] = card;
        t["CheckBorder"] = Mix(card, secondary, dark ? 0.5 : 0.42);
        t["Veil"] = dark ? Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0, 0, 0);
        t["VeilStrong"] = dark ? Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x26, 0, 0, 0);
        t["NavHover"] = dark ? Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0, 0, 0);
        t["HintBg"] = dark ? WithAlpha(Mix(card, label, 0.06), 0xE6) : WithAlpha(Mix(fillStrong, label, 0.03), 0x9E);
        t["HintBorder"] = dark ? WithAlpha(Mix(card, label, 0.16), 0xC0) : WithAlpha(Mix(fillStrong, label, 0.12), 0x8C);
        t["HintText"] = Mix(label, secondary, 0.6);

        // ---- text
        t["Label"] = label;
        t["Secondary"] = secondary;
        t["Tertiary"] = C(b.Tertiary);
        t["Ink"] = Mix(label, secondary, 0.45);
        t["Glyph"] = C(b.Glyph);
        t["WhiteText"] = white;

        // ---- accent
        t["Accent"] = accent;
        t["AccentHover"] = dark ? Mix(accent, white, 0.12) : Mix(accent, black, 0.12);
        t["AccentPressed"] = dark ? Mix(accent, black, 0.14) : Mix(accent, black, 0.22);
        t["AccentSubtle"] = Mix(card, accent, dark ? 0.1 : 0.07);
        t["AccentSoft"] = Mix(card, accent, dark ? 0.16 : 0.11);
        t["AccentSoftHover"] = Mix(card, accent, dark ? 0.22 : 0.17);
        t["AccentSoftPressed"] = Mix(card, accent, dark ? 0.28 : 0.24);
        t["AccentBorderLight"] = Mix(card, accent, dark ? 0.22 : 0.16);
        t["AccentBorder"] = Mix(card, accent, dark ? 0.36 : 0.3);
        t["AccentBorderStrong"] = Mix(card, accent, dark ? 0.5 : 0.45);
        t["AccentText"] = dark ? Mix(accent, white, 0.28) : Mix(accent, black, 0.18);
        t["AccentTextMuted"] = Mix(t["AccentText"], secondary, 0.42);
        t["UpdateReadyText"] = t["AccentText"];
        t["UpdateReadyFill"] = t["AccentSubtle"];
        t["UpdateReadyBorder"] = t["AccentBorder"];
        t["UpdateInstallFill"] = accent;
        t["UpdateInstallBorder"] = t["AccentHover"];

        // ---- states: fixed hues so a warning reads the same in every tint
        Family("Positive", dark ? C("#34C46E") : C("#1D9A5B"));
        Family("Caution", dark ? C("#E5A33A") : C("#B2760A"));
        Family("Danger", dark ? C("#FF5C5C") : C("#D33B3B"));
        // Destructive buttons (移除、删除、清空) are a softer terracotta than error red.
        Family("Remove", dark ? C("#F07A61") : C("#C8553D"));
        // The 已读 pill shares the reminder capsule's orange.
        Family("Amber", dark ? C("#FFA53A") : C("#E8890E"));
        t["DangerIcon"] = dark ? C("#FF6A55") : C("#D81E06");
        t["CloseHover"] = C("#C42B1C");
        t["ClosePressed"] = C("#A81E13");
        t["Badge"] = dark ? C("#FF5A5F") : C("#E5484D");
        t["IdleDot"] = dark ? C("#6E6E73") : C("#AEAEB2");
        return t;

        void Family(string name, Color color)
        {
            t[name] = color;
            t[name + "Soft"] = Mix(card, color, dark ? 0.16 : 0.09);
            t[name + "SoftHover"] = Mix(card, color, dark ? 0.24 : 0.15);
            t[name + "SoftPressed"] = Mix(card, color, dark ? 0.3 : 0.22);
            t[name + "Border"] = Mix(card, color, dark ? 0.32 : 0.22);
            t[name + "BorderHover"] = Mix(card, color, dark ? 0.46 : 0.38);
            t[name + "BorderPressed"] = Mix(card, color, dark ? 0.55 : 0.5);
            t[name + "Text"] = dark ? Mix(color, white, 0.12) : Mix(color, black, 0.16);
            t[name + "TextStrong"] = dark ? Mix(color, white, 0.28) : Mix(color, black, 0.3);
        }
    }

    /// <summary>The fixed colors of the two contact modes: privacy indigo, reminder orange.</summary>
    public static Color ModeColor(ModeKind kind, bool dark) => kind == ModeKind.Privacy
        ? (Color)ColorConverter.ConvertFromString(dark ? "#8583FF" : "#5E5CE0")
        : (Color)ColorConverter.ConvertFromString(dark ? "#FFA032" : "#EE8B12");

    public static Color Mix(Color from, Color to, double amount) => Color.FromArgb(
        (byte)Math.Round(from.A + (to.A - from.A) * amount),
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}
