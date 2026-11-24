using System.Windows.Media;
using FileCompare.App.Theming;

namespace FileCompare.App.Editor;

/// <summary>Single place for the colors and fonts used by the diff panes. Call <see cref="Apply"/> when the theme changes.</summary>
public static class DiffTheme
{
    public static Brush ChangedBrush { get; private set; } = Frozen(Colors.MistyRose);
    public static Brush FillerBrush { get; private set; } = Frozen(Colors.Gainsboro);
    public static Brush LineNumberBrush { get; private set; } = Frozen(Colors.Gray);
    public static Brush EditorBackground { get; private set; } = Frozen(Colors.White);
    public static Brush EditorForeground { get; private set; } = Frozen(Colors.Black);

    /// <summary>The characters that changed inside a changed line (stronger than <see cref="ChangedBrush"/>).</summary>
    public static Brush InlineChangedBrush { get; private set; } = Frozen(Colors.LightCoral);

    /// <summary>The whitespace or case that differs inside a minor line.</summary>
    public static Brush InlineMinorBrush { get; private set; } = Frozen(Colors.CornflowerBlue);

    /// <summary>Lines that differ only in unimportant ways (whitespace or case).</summary>
    public static Brush MinorBrush { get; private set; } = Frozen(Colors.LightSteelBlue);

    /// <summary>Folder compare: entries that exist on one side only.</summary>
    public static Brush FolderOnlyBrush { get; private set; } = Frozen(Colors.Moccasin);

    /// <summary>The "N filtered lines" separator row.</summary>
    public static Brush FilteredBrush { get; private set; } = Frozen(Colors.LightSteelBlue);

    public static readonly Typeface MonoTypeface = new("Consolas");
    public const double LineNumberFontSize = 11;
    public const double LineNumberMarginWidth = 56;
    public const double LineNumberRightPadding = 4;

    public static void Apply(DiffPalette palette)
    {
        ChangedBrush = Frozen(palette.Changed);
        FillerBrush = Frozen(palette.Filler);
        LineNumberBrush = Frozen(palette.LineNumber);
        EditorBackground = Frozen(palette.EditorBackground);
        EditorForeground = Frozen(palette.EditorText);

        // Derived from the theme so every theme gets matching colors without extra palette entries.
        var blue = Color.FromRgb(74, 144, 226);
        var minor = Mix(palette.EditorBackground, blue, 0.22);
        MinorBrush = Frozen(minor);
        InlineMinorBrush = Frozen(Mix(minor, blue, 0.5));
        InlineChangedBrush = Frozen(Mix(palette.Changed, Color.FromRgb(230, 60, 60), 0.45));
        FolderOnlyBrush = Frozen(Mix(palette.EditorBackground, Color.FromRgb(240, 160, 40), 0.2));
        FilteredBrush = Frozen(Mix(palette.EditorBackground, palette.LineNumber, 0.28));
    }

    /// <summary>Mixes <paramref name="amount"/> (0..1) of <paramref name="other"/> into <paramref name="color"/>.</summary>
    internal static Color Mix(Color color, Color other, double amount) => Color.FromRgb(
        (byte)(color.R + (other.R - color.R) * amount),
        (byte)(color.G + (other.G - color.G) * amount),
        (byte)(color.B + (other.B - color.B) * amount));

    internal static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
