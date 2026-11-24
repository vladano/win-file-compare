using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;

namespace FileCompare.App.Editor;

/// <summary>
/// AvalonEdit's built-in highlighting colors are made for a white background and are hard to read on dark themes.
/// This lightens the text colors (and darkens any background) when the theme is dark. The shared definitions are
/// never changed: colors are adapted on the fly.
/// </summary>
public static class SyntaxTheme
{
    private static readonly Dictionary<HighlightingColor, HighlightingColor> Cache = new();
    private static bool _dark;

    /// <summary>Call when the theme changes. The editors must be redrawn afterwards.</summary>
    public static void Apply(bool dark)
    {
        _dark = dark;
        Cache.Clear();
    }

    /// <summary>The color to really use for a highlighting color from a definition.</summary>
    public static HighlightingColor Adapt(HighlightingColor color)
    {
        if (!_dark) return color;
        if (Cache.TryGetValue(color, out var adapted)) return adapted;

        adapted = color.Clone();
        if (ColorOf(adapted.Foreground) is { } foreground)
            adapted.Foreground = new SimpleHighlightingBrush(WithLightness(foreground, min: 0.68, max: 1));
        if (ColorOf(adapted.Background) is { } background)
            adapted.Background = new SimpleHighlightingBrush(WithLightness(background, min: 0, max: 0.28));
        adapted.Freeze();

        Cache[color] = adapted;
        return adapted;
    }

    private static Color? ColorOf(HighlightingBrush? brush) =>
        (brush as SimpleHighlightingBrush)?.GetBrush(null!) is SolidColorBrush solid ? solid.Color : null;

    /// <summary>Keeps the hue and saturation and moves the lightness into [min, max].</summary>
    private static Color WithLightness(Color color, double min, double max)
    {
        RgbToHsl(color, out double h, out double s, out double l);
        return HslToRgb(h, s, Math.Clamp(l, min, max), color.A);
    }

    private static void RgbToHsl(Color c, out double h, out double s, out double l)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2;
        double d = max - min;
        if (d == 0) { h = s = 0; return; }

        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        h = max == r ? (g - b) / d + (g < b ? 6 : 0)
          : max == g ? (b - r) / d + 2
          : (r - g) / d + 4;
        h /= 6;
    }

    private static Color HslToRgb(double h, double s, double l, byte alpha)
    {
        if (s == 0) { byte v = (byte)Math.Round(l * 255); return Color.FromArgb(alpha, v, v, v); }

        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return Color.FromArgb(alpha,
            (byte)Math.Round(Channel(p, q, h + 1.0 / 3) * 255),
            (byte)Math.Round(Channel(p, q, h) * 255),
            (byte)Math.Round(Channel(p, q, h - 1.0 / 3) * 255));
    }

    private static double Channel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}

/// <summary>The normal AvalonEdit highlighter, except that colors pass through <see cref="SyntaxTheme"/> first.</summary>
public sealed class ThemedHighlightingColorizer : HighlightingColorizer
{
    public ThemedHighlightingColorizer(IHighlightingDefinition definition) : base(definition) { }

    protected override void ApplyColorToElement(VisualLineElement element, HighlightingColor color) =>
        base.ApplyColorToElement(element, SyntaxTheme.Adapt(color));
}
