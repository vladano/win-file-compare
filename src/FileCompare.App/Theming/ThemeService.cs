using System.Windows;
using System.Windows.Media;
using FileCompare.App.Editor;
using Microsoft.Win32;

namespace FileCompare.App.Theming;

/// <summary>
/// Applies the chosen theme: the WPF Fluent base (light or dark), then the theme's own colors on top,
/// and keeps the diff colors in sync. The choice is remembered between runs.
/// </summary>
public static class ThemeService
{
    private static readonly List<string> OverriddenKeys = new();

    public static event EventHandler? Changed;

    /// <summary>The theme the user picked (may be "System default").</summary>
    public static AppTheme Current { get; private set; } = ThemeCatalog.System;

    /// <summary>True when the theme that is currently shown is dark (also when following Windows).</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Loads the saved choice and applies it. Call once at startup, before creating windows.</summary>
    public static void Initialize()
    {
        Current = ThemeCatalog.Find(UserSettings.Current.Theme);
        Apply();

        // Re-apply when Windows switches between light and dark while we follow the system.
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Current.Id == ThemeCatalog.SystemId)
                Application.Current.Dispatcher.BeginInvoke(Apply);
        };
    }

    /// <summary>Keeps a window's title bar in step with the theme for as long as the window is open.</summary>
    public static void Track(Window window)
    {
        void Update(object? s, EventArgs e) => TitleBarTheme.Apply(window, IsDark);
        window.SourceInitialized += Update;
        Changed += Update;
        window.Closed += (_, _) => Changed -= Update;
    }

    public static void SetTheme(AppTheme theme)
    {
        if (theme.Id == Current.Id) return;
        Current = theme;
        UserSettings.Current.Theme = theme.Id;
        UserSettings.Save();
        Apply();
    }

    private static void Apply()
    {
        bool followSystem = Current.Id == ThemeCatalog.SystemId;
        var effective = followSystem ? (WindowsUsesDarkApps() ? ThemeCatalog.Dark : ThemeCatalog.Light) : Current;
        IsDark = effective.IsDark;

        // ThemeMode switches WPF's built-in Fluent theme (WPF0001 is silenced in Directory.Build.props).
        Application.Current.ThemeMode = followSystem ? ThemeMode.System : IsDark ? ThemeMode.Dark : ThemeMode.Light;

        ApplyChrome(effective.Chrome);
        DiffTheme.Apply(effective.Diff);
        SyntaxTheme.Apply(IsDark);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Replaces the Fluent colors with the theme's colors, or restores Fluent when the theme has none.</summary>
    private static void ApplyChrome(ChromePalette? p)
    {
        var resources = Application.Current.Resources;
        foreach (var key in OverriddenKeys) resources.Remove(key);
        OverriddenKeys.Clear();
        if (p is null) return;

        // Keys the app's own XAML binds to (Fluent's built-in styles do not read overrides of their keys).
        Override("App.Background", p.Background);
        Override("App.Surface", p.Surface);
        Override("App.Control", p.Control);
        Override("App.Border", p.Border);
        Override("App.Text", p.Text);
        Override("App.TextSecondary", p.TextSecondary);
        Override("App.Accent", p.Accent);

        var hover = Blend(p.Control, p.Text, 0.10);
        var pressed = Blend(p.Control, p.Text, 0.18);
        var disabledText = Blend(p.Text, p.Background, 0.55);

        Override("ApplicationBackgroundBrush", p.Background);
        Override("SolidBackgroundFillColorBaseBrush", p.Background);
        Override("SolidBackgroundFillColorBaseAltBrush", p.Surface);
        Override("SolidBackgroundFillColorSecondaryBrush", p.Surface);
        Override("SolidBackgroundFillColorTertiaryBrush", p.Surface);
        Override("LayerFillColorDefaultBrush", p.Surface);
        Override("LayerFillColorAltBrush", p.Surface);
        Override("MenuPopupBrush", p.Surface);

        Override("ControlFillColorDefaultBrush", p.Control);
        Override("ControlFillColorSecondaryBrush", hover);
        Override("ControlFillColorTertiaryBrush", pressed);

        Override("ControlStrokeColorDefaultBrush", p.Border);
        Override("ControlStrokeColorSecondaryBrush", p.Border);
        Override("SurfaceStrokeColorDefaultBrush", p.Border);
        Override("MenuBorderColorDefaultBrush", p.Border);

        Override("TextFillColorPrimaryBrush", p.Text);
        Override("TextFillColorSecondaryBrush", p.TextSecondary);
        Override("TextFillColorTertiaryBrush", p.TextSecondary);
        Override("TextFillColorDisabledBrush", disabledText);

        Override("AccentFillColorDefaultBrush", p.Accent);
        Override("AccentFillColorSecondaryBrush", Blend(p.Accent, p.Background, 0.15));
        Override("AccentFillColorTertiaryBrush", Blend(p.Accent, p.Background, 0.30));
    }

    private static void Override(string key, Color color)
    {
        Application.Current.Resources[key] = DiffTheme.Frozen(color);
        OverriddenKeys.Add(key);
    }

    /// <summary>Mixes <paramref name="amount"/> (0..1) of <paramref name="other"/> into <paramref name="color"/>.</summary>
    private static Color Blend(Color color, Color other, double amount) => Color.FromRgb(
        (byte)(color.R + (other.R - color.R) * amount),
        (byte)(color.G + (other.G - color.G) * amount),
        (byte)(color.B + (other.B - color.B) * amount));

    private static bool WindowsUsesDarkApps()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
