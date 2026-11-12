using System;
using System.Windows;
using System.Windows.Controls;

namespace VoiceBridge.App;

/// <summary>设置 → 外观: 浅色、深色或跟随系统, and the color the theme is tinted with.</summary>
public partial class MainWindow
{
    private bool showingAppearance;

    private void InitializeAppearance()
    {
        ShowAppearance();
        Theme.Changed += ThemeChanged;
        Closed += (_, _) => Theme.Changed -= ThemeChanged;
    }

    /// <summary>Selects the controls that match the theme in use, without applying anything.</summary>
    private void ShowAppearance()
    {
        showingAppearance = true;
        var choice = Theme.Choice;
        (choice.Mode switch { ThemeMode.Dark => ThemeDark, ThemeMode.System => ThemeSystem, _ => ThemeLight }).IsChecked = true;
        (choice.Tint switch { ThemeTint.Green => TintGreen, ThemeTint.Pink => TintPink, ThemeTint.Blue => TintBlue, _ => TintDefault })
            .IsChecked = true;
        showingAppearance = false;
    }

    private void ThemeModeChecked(object sender, RoutedEventArgs e)
    {
        if (!showingAppearance && sender is RadioButton { Tag: string tag } && Enum.TryParse<ThemeMode>(tag, out var mode))
            ChangeAppearance(Theme.Choice with { Mode = mode });
    }

    private void ThemeTintChecked(object sender, RoutedEventArgs e)
    {
        if (!showingAppearance && sender is RadioButton { Tag: string tag } && Enum.TryParse<ThemeTint>(tag, out var tint))
            ChangeAppearance(Theme.Choice with { Tint = tint });
    }

    /// <summary>Applies at once, like iOS settings, and is saved with everything else.</summary>
    private void ChangeAppearance(AppearanceSettings choice)
    {
        if (choice == Theme.Choice) return;
        Theme.Apply(choice);
        SaveNow();
    }

    /// <summary>XAML repaints by itself; these parts pick their brushes in code.</summary>
    private void ThemeChanged(object? sender, EventArgs e)
    {
        Render();
        RenderUpdate();
    }
}
