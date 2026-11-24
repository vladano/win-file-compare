using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FileCompare.Core.Text;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace FileCompare.App.Editor;

/// <summary>Line number gutter that shows the real file line number (blank for filler lines).</summary>
public sealed class RowNumberMargin : AbstractMargin
{
    private readonly bool _isLeft;

    public RowNumberMargin(bool isLeft) => _isLeft = isLeft;

    public IReadOnlyList<DiffRow> Rows { get; set; } = Array.Empty<DiffRow>();

    protected override Size MeasureOverride(Size availableSize) => new(DiffTheme.LineNumberMarginWidth, 0);

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView != null) oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView != null) newTextView.VisualLinesChanged += OnVisualLinesChanged;
        InvalidateVisual();
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var textView = TextView;
        if (textView is null || !textView.VisualLinesValid) return;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double right = DiffTheme.LineNumberMarginWidth - DiffTheme.LineNumberRightPadding;
        foreach (var line in textView.VisualLines)
        {
            int index = line.FirstDocumentLine.LineNumber - 1;
            if (index < 0 || index >= Rows.Count) continue;

            int? number = _isLeft ? Rows[index].LeftNumber : Rows[index].RightNumber;
            if (number is null) continue;

            var text = new FormattedText(number.Value.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                DiffTheme.MonoTypeface, DiffTheme.LineNumberFontSize, DiffTheme.LineNumberBrush, dpi);
            double y = line.VisualTop - textView.ScrollOffset.Y;
            dc.DrawText(text, new Point(right - text.Width, y + (line.Height - text.Height) / 2));
        }
    }
}
