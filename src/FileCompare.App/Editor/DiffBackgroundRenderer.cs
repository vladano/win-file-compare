using System.Windows;
using System.Windows.Media;
using FileCompare.Core.Text;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace FileCompare.App.Editor;

/// <summary>Paints each editor line according to the diff row it represents, plus the changed characters inside it.</summary>
public sealed class DiffBackgroundRenderer : IBackgroundRenderer
{
    private readonly bool _isLeft;
    private readonly Dictionary<int, InlineDiffResult> _inlineCache = new();
    private IReadOnlyList<DiffRow> _rows = Array.Empty<DiffRow>();

    public DiffBackgroundRenderer(bool isLeft) => _isLeft = isLeft;

    public IReadOnlyList<DiffRow> Rows
    {
        get => _rows;
        set
        {
            _rows = value;
            _inlineCache.Clear();
        }
    }

    /// <summary>The Rules the rows were compared with. Needed to find the changed characters the same way.</summary>
    public DiffOptions InlineOptions { get; set; } = new();

    /// <summary>Show the changed characters inside a changed line.</summary>
    public bool ShowInline { get; set; } = true;

    /// <summary>
    /// True while the user has typed since the rows were made. The rows no longer match the text,
    /// so the character highlights would land in the wrong place and are left out until the next compare.
    /// </summary>
    public bool Stale { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (!textView.VisualLinesValid) return;
        foreach (var line in textView.VisualLines)
        {
            int index = line.FirstDocumentLine.LineNumber - 1;
            if (index < 0 || index >= Rows.Count) continue;

            var brush = BrushFor(Rows[index]);
            if (brush is null) continue;

            double y = line.VisualTop - textView.ScrollOffset.Y;
            dc.DrawRectangle(brush, null, new Rect(0, y, Math.Max(textView.ActualWidth, textView.DocumentHeight), line.Height));

            if (ShowInline && !Stale) DrawInline(textView, dc, line.FirstDocumentLine, Rows[index], index);
        }
    }

    private void DrawInline(TextView textView, DrawingContext dc, DocumentLine documentLine, DiffRow row, int index)
    {
        if (row.Kind is not (RowKind.Modified or RowKind.Minor)) return;

        if (!_inlineCache.TryGetValue(index, out var inline))
            _inlineCache[index] = inline = InlineDiff.ForRow(row, InlineOptions);

        var ranges = _isLeft ? inline.Left : inline.Right;
        if (ranges.Count == 0) return;

        var geometryBuilder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 2 };
        foreach (var range in ranges)
        {
            int length = Math.Min(range.Length, documentLine.Length - range.Start);
            if (length <= 0) continue;
            geometryBuilder.AddSegment(textView, new TextSegment { StartOffset = documentLine.Offset + range.Start, Length = length });
        }

        var geometry = geometryBuilder.CreateGeometry();
        if (geometry is null) return;
        dc.DrawGeometry(row.Kind == RowKind.Minor ? DiffTheme.InlineMinorBrush : DiffTheme.InlineChangedBrush, null, geometry);
    }

    private Brush? BrushFor(DiffRow row) => row.Kind switch
    {
        RowKind.Modified => DiffTheme.ChangedBrush,
        RowKind.Minor => DiffTheme.MinorBrush,
        RowKind.Filtered => DiffTheme.FilteredBrush,
        RowKind.LeftOnly => _isLeft ? DiffTheme.ChangedBrush : DiffTheme.FillerBrush,
        RowKind.RightOnly => _isLeft ? DiffTheme.FillerBrush : DiffTheme.ChangedBrush,
        _ => null
    };
}
