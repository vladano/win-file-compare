using System.Windows.Media;

namespace FileCompare.App.Theming;

/// <summary>Colors of the diff panes (editor text, highlighted and filler lines, line numbers).</summary>
public sealed record DiffPalette(Color EditorBackground, Color EditorText, Color Changed, Color Filler, Color LineNumber);

/// <summary>Colors that replace the Fluent theme colors for the window, menus, toolbar and inputs.</summary>
public sealed record ChromePalette(
    Color Background, Color Surface, Color Control, Color Border, Color Text, Color TextSecondary, Color Accent);

/// <summary>
/// One selectable theme. <see cref="IsDark"/> picks the Fluent base (light or dark) for the built-in
/// controls; <see cref="Chrome"/> then recolors the window. Every theme in the catalog should set it.
/// </summary>
public sealed record AppTheme(string Id, string Name, bool IsDark, DiffPalette Diff, ChromePalette? Chrome = null);
