using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FileCompare.App.Editor;
using FileCompare.Core.Binary;

namespace FileCompare.App.Binary;

/// <summary>
/// Draws a <see cref="HexComparison"/>: offset, then hex and ASCII for the left file, then for the right file.
/// Only the rows on screen are drawn, so very large files scroll as fast as small ones.
/// The page scrolls it by setting <see cref="FirstRow"/>.
/// </summary>
public sealed class HexView : FrameworkElement
{
    private const double FontSize = 13;
    private const double LeftPadding = 8;

    /// <summary>Character columns: offset, gap, hex, gap, ASCII, separator gap, hex, gap, ASCII.</summary>
    private const int OffsetChars = 8, GapChars = 2, SideGapChars = 4;

    private HexComparison? _comparison;
    private int _firstRow;
    private double _charWidth = 7.2, _rowHeight = 17;
    private double _measuredDpi;

    public HexView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public HexComparison? Comparison
    {
        get => _comparison;
        set
        {
            _comparison = value;
            _firstRow = 0;
            InvalidateVisual();
        }
    }

    public int FirstRow
    {
        get => _firstRow;
        set
        {
            int clamped = Math.Clamp(value, 0, Math.Max(0, (_comparison?.RowCount ?? 0) - 1));
            if (clamped == _firstRow) return;
            _firstRow = clamped;
            InvalidateVisual();
        }
    }

    public double RowHeight => _rowHeight;

    /// <summary>Rows that fit below the header line.</summary>
    public int VisibleRows => Math.Max(1, (int)((ActualHeight - _rowHeight) / _rowHeight));

    private double HexX(bool left) => LeftPadding + (OffsetChars + GapChars + (left ? 0 : SideWidthChars + SideGapChars)) * _charWidth;

    private double AsciiX(bool left) => HexX(left) + (HexFormat.HexWidth + GapChars) * _charWidth;

    private static int SideWidthChars => HexFormat.HexWidth + GapChars + HexComparison.BytesPerRow;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(DiffTheme.EditorBackground, null, new Rect(RenderSize));
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        MeasureFont(dpi);

        DrawHeader(dc, dpi);
        var c = _comparison;
        if (c is null) return;

        // A line between the two files.
        double separatorX = AsciiX(left: true) + (HexComparison.BytesPerRow + SideGapChars / 2.0) * _charWidth;
        dc.DrawRectangle(DiffTheme.FillerBrush, null, new Rect(separatorX, 0, 1, ActualHeight));

        for (int i = 0; i < VisibleRows + 1; i++)
        {
            int row = _firstRow + i;
            if (row >= c.RowCount) break;
            double y = (i + 1) * _rowHeight;
            int start = row * HexComparison.BytesPerRow;

            if (c.RowDiffers(row))
                dc.DrawRectangle(DiffTheme.ChangedBrush, null, new Rect(0, y, ActualWidth, _rowHeight));

            for (int b = 0; b < HexComparison.BytesPerRow; b++)
            {
                int offset = start + b;
                bool inLeft = offset < c.Left.Length, inRight = offset < c.Right.Length;
                if (!inLeft && !inRight) break;

                bool differs = c.ByteDiffers(offset);
                MarkByte(dc, y, b, left: true, inLeft ? differs ? DiffTheme.InlineChangedBrush : null : DiffTheme.FillerBrush);
                MarkByte(dc, y, b, left: false, inRight ? differs ? DiffTheme.InlineChangedBrush : null : DiffTheme.FillerBrush);
            }

            DrawText(dc, HexFormat.Offset(start), LeftPadding, y, DiffTheme.LineNumberBrush, dpi);
            DrawText(dc, HexFormat.HexText(c.Left, start), HexX(true), y, DiffTheme.EditorForeground, dpi);
            DrawText(dc, HexFormat.AsciiText(c.Left, start), AsciiX(true), y, DiffTheme.EditorForeground, dpi);
            DrawText(dc, HexFormat.HexText(c.Right, start), HexX(false), y, DiffTheme.EditorForeground, dpi);
            DrawText(dc, HexFormat.AsciiText(c.Right, start), AsciiX(false), y, DiffTheme.EditorForeground, dpi);
        }
    }

    private void DrawHeader(DrawingContext dc, double dpi)
    {
        var columns = string.Concat(Enumerable.Range(0, HexComparison.BytesPerRow)
            .Select(i => i.ToString("X2", CultureInfo.InvariantCulture) + (i == 7 ? "  " : " "))).TrimEnd();
        DrawText(dc, "Offset", LeftPadding, 0, DiffTheme.LineNumberBrush, dpi);
        DrawText(dc, columns, HexX(true), 0, DiffTheme.LineNumberBrush, dpi);
        DrawText(dc, "Left", AsciiX(true), 0, DiffTheme.LineNumberBrush, dpi);
        DrawText(dc, columns, HexX(false), 0, DiffTheme.LineNumberBrush, dpi);
        DrawText(dc, "Right", AsciiX(false), 0, DiffTheme.LineNumberBrush, dpi);
        dc.DrawRectangle(DiffTheme.FillerBrush, null, new Rect(0, _rowHeight - 1, ActualWidth, 1));
    }

    private void MarkByte(DrawingContext dc, double y, int byteIndex, bool left, Brush? brush)
    {
        if (brush is null) return;
        dc.DrawRectangle(brush, null, new Rect(HexX(left) + HexFormat.HexColumn(byteIndex) * _charWidth - 1, y, 2 * _charWidth + 2, _rowHeight));
        dc.DrawRectangle(brush, null, new Rect(AsciiX(left) + byteIndex * _charWidth, y, _charWidth, _rowHeight));
    }

    private static void DrawText(DrawingContext dc, string text, double x, double y, Brush brush, double dpi)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            DiffTheme.MonoTypeface, FontSize, brush, dpi);
        dc.DrawText(formatted, new Point(x, y + 1));
    }

    /// <summary>The monospace font decides the column width and row height (measured again when the DPI changes).</summary>
    private void MeasureFont(double dpi)
    {
        if (dpi == _measuredDpi) return;
        var sample = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            DiffTheme.MonoTypeface, FontSize, Brushes.Black, dpi);
        _charWidth = sample.WidthIncludingTrailingWhitespace;
        _rowHeight = Math.Ceiling(sample.Height) + 2;
        _measuredDpi = dpi;
    }
}
