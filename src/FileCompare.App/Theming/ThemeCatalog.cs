using System.Windows.Media;

namespace FileCompare.App.Theming;

/// <summary>
/// All themes the user can pick. To add a theme, add an entry here and to <see cref="All"/>;
/// the menus and the saved setting pick it up automatically.
/// </summary>
public static class ThemeCatalog
{
    public const string SystemId = "system";

    private static readonly DiffPalette LightDiff = new(C("#FFFFFF"), C("#000000"), C("#FFE0E0"), C("#ECECEC"), C("#808080"));
    private static readonly DiffPalette DarkDiff = new(C("#1E1E1E"), C("#E6E6E6"), C("#5C292E"), C("#303034"), C("#8C8C8C"));

    /// <summary>Follows the Windows light/dark setting. Its colors are resolved to <see cref="Light"/> or <see cref="Dark"/> when applied.</summary>
    public static AppTheme System { get; } = new(SystemId, "System default", false, LightDiff);

    public static AppTheme Light { get; } = new("light", "Light", false, LightDiff,
        new ChromePalette(C("#F3F3F3"), C("#F9F9F9"), C("#FFFFFF"), C("#D0D0D0"), C("#1A1A1A"), C("#6B6B6B"), C("#0067C0")));

    public static AppTheme Dark { get; } = new("dark", "Dark", true, DarkDiff,
        new ChromePalette(C("#202020"), C("#2B2B2B"), C("#2D2D2D"), C("#454545"), C("#FFFFFF"), C("#A0A0A0"), C("#60CDFF")));

    public static AppTheme SolarizedLight { get; } = new("solarized-light", "Solarized Light", false,
        new DiffPalette(C("#FDF6E3"), C("#586E75"), C("#F8DCCB"), C("#EEE8D5"), C("#93A1A1")),
        new ChromePalette(C("#FDF6E3"), C("#EEE8D5"), C("#FDF6E3"), C("#93A1A1"), C("#586E75"), C("#839496"), C("#268BD2")));

    public static AppTheme SolarizedDark { get; } = new("solarized-dark", "Solarized Dark", true,
        new DiffPalette(C("#002B36"), C("#93A1A1"), C("#4F3238"), C("#073642"), C("#657B83")),
        new ChromePalette(C("#002B36"), C("#073642"), C("#073642"), C("#586E75"), C("#93A1A1"), C("#657B83"), C("#2AA198")));

    public static AppTheme Dracula { get; } = new("dracula", "Dracula", true,
        new DiffPalette(C("#282A36"), C("#F8F8F2"), C("#55303B"), C("#21222C"), C("#6272A4")),
        new ChromePalette(C("#282A36"), C("#21222C"), C("#343746"), C("#44475A"), C("#F8F8F2"), C("#9AA0C3"), C("#BD93F9")));

    public static AppTheme Nord { get; } = new("nord", "Nord", true,
        new DiffPalette(C("#2E3440"), C("#D8DEE9"), C("#5B3A42"), C("#3B4252"), C("#616E88")),
        new ChromePalette(C("#2E3440"), C("#3B4252"), C("#434C5E"), C("#4C566A"), C("#ECEFF4"), C("#A7B0C3"), C("#88C0D0")));

    /// <summary>In menu order.</summary>
    public static IReadOnlyList<AppTheme> All { get; } = new[]
    {
        System, Light, Dark, SolarizedLight, SolarizedDark, Dracula, Nord
    };

    /// <summary>Finds a theme by id (case-insensitive). Unknown or empty ids give the system theme.</summary>
    public static AppTheme Find(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? System;

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
