using FileCompare.Core.Settings;
using FileCompare.Core.Text;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;

namespace FileCompare.App.Editor;

/// <summary>
/// One side of the comparison: wraps an AvalonEdit control together with its
/// background renderer and line number margin so callers deal with a single object.
/// Filler lines and "filtered lines" rows are locked, so only real file lines can be edited.
/// </summary>
public sealed class DiffPane
{
    private readonly DiffBackgroundRenderer _renderer;
    private readonly RowNumberMargin _margin;
    private readonly Func<DiffRow, string?> _textSelector;
    private readonly LockedLines _locked = new();
    private bool _loading;

    public DiffPane(TextEditor editor, bool isLeft)
    {
        Editor = editor;
        IsLeft = isLeft;
        _renderer = new DiffBackgroundRenderer(isLeft);
        _margin = new RowNumberMargin(isLeft);
        _textSelector = isLeft ? r => r.LeftText : r => r.RightText;

        editor.ShowLineNumbers = false;
        editor.IsReadOnly = true;
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.TextArea.TextView.BackgroundRenderers.Add(_renderer);
        editor.TextArea.LeftMargins.Insert(0, _margin);
        editor.TextChanged += (_, _) =>
        {
            if (_loading) return;
            IsDirty = true;
            _renderer.Stale = true;
            Edited?.Invoke(this, EventArgs.Empty);
        };
        Refresh();
    }

    public TextEditor Editor { get; }
    public bool IsLeft { get; }

    /// <summary>A caret place described by the file's own line number, which survives the filler lines moving.</summary>
    public readonly record struct CaretPlace(int FileLine, int Column);

    private IReadOnlyList<DiffRow> _rows = Array.Empty<DiffRow>();

    private int? NumberOf(DiffRow row) => IsLeft ? row.LeftNumber : row.RightNumber;

    /// <summary>
    /// Where the caret is, as a file line and column. While the user has typed, the document no longer matches
    /// the rows, so the line is counted from the document (only real file lines count, filler lines do not).
    /// </summary>
    public CaretPlace GetCaret()
    {
        int docLine = Editor.TextArea.Caret.Line;
        int column = Editor.TextArea.Caret.Column;

        if (IsDirty)
        {
            int fileLine = 0;
            foreach (var line in Editor.Document.Lines)
            {
                if (line.LineNumber > docLine) break;
                if (!_locked.Lines.Contains(line)) fileLine++;
            }
            return new CaretPlace(Math.Max(1, fileLine), column);
        }

        // Not typed in: the rows describe the document exactly. A filler line has no number, so use the next real one.
        for (int i = Math.Min(docLine, _rows.Count) - 1; i >= 0 && i < _rows.Count; i++)
        {
            if (NumberOf(_rows[i]) is int number) return new CaretPlace(number, column);
        }
        for (int i = Math.Min(docLine, _rows.Count) - 1; i >= 0; i--)
        {
            if (NumberOf(_rows[i]) is int number) return new CaretPlace(number, column);
        }
        return new CaretPlace(1, 1);
    }

    /// <summary>Puts the caret back on a file line after the panes were rebuilt. Does nothing if that line is not shown.</summary>
    public void RestoreCaret(CaretPlace place)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (NumberOf(_rows[i]) != place.FileLine) continue;

            int docLine = i + 1;
            int column = Math.Clamp(place.Column, 1, Editor.Document.GetLineByNumber(docLine).Length + 1);
            Editor.TextArea.Caret.Position = new TextViewPosition(docLine, column);
            return;
        }
    }

    /// <summary>True when the user typed in the pane since the last <see cref="Show"/>.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Raised when the user edits the text.</summary>
    public event EventHandler? Edited;

    /// <summary>Fills the editor with this side's text (filler rows become blank, locked lines) and repaints.</summary>
    public void Show(IReadOnlyList<DiffRow> rows)
    {
        _loading = true;
        try
        {
            _rows = rows;
            _renderer.Rows = rows;
            _margin.Rows = rows;
            Editor.Text = string.Join("\n", rows.Select(r => _textSelector(r) ?? ""));
            Editor.Document.UndoStack.ClearAll();

            _locked.Lines.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Kind == RowKind.Filtered || _textSelector(rows[i]) is null)
                    _locked.Lines.Add(Editor.Document.GetLineByNumber(i + 1));
            }
            IsDirty = false;
            _renderer.Stale = false;
        }
        finally { _loading = false; }
        Editor.TextArea.TextView.Redraw();
    }

    /// <summary>The Rules the rows were made with, so the changed characters inside a line are found the same way.</summary>
    public DiffOptions InlineOptions
    {
        set => _renderer.InlineOptions = value;
    }

    private HighlightingColorizer? _colorizer;
    private string? _filePath;
    private bool _syntaxEnabled = true;

    /// <summary>Tells the pane which file it shows, so the syntax can be chosen from the extension.</summary>
    public void SetFile(string? path)
    {
        if (string.Equals(path, _filePath, StringComparison.OrdinalIgnoreCase)) return;
        _filePath = path;
        UpdateSyntax();
    }

    private void UpdateSyntax()
    {
        var transformers = Editor.TextArea.TextView.LineTransformers;
        if (_colorizer is not null)
        {
            transformers.Remove(_colorizer);
            _colorizer = null;
        }

        string extension = string.IsNullOrEmpty(_filePath) ? "" : System.IO.Path.GetExtension(_filePath);
        var definition = _syntaxEnabled && extension.Length > 0
            ? HighlightingManager.Instance.GetDefinitionByExtension(extension)
            : null;

        if (definition is not null)
        {
            _colorizer = new ThemedHighlightingColorizer(definition);
            transformers.Insert(0, _colorizer);
        }
        Editor.TextArea.TextView.Redraw();
    }

    /// <summary>The file text as it is now: every line except the locked filler lines, joined with "\n".</summary>
    public string GetEditedText()
    {
        var lines = new List<string>();
        foreach (var line in Editor.Document.Lines)
        {
            if (!_locked.Lines.Contains(line)) lines.Add(Editor.Document.GetText(line));
        }
        return string.Join("\n", lines);
    }

    public void SetEditable(bool editable)
    {
        Editor.IsReadOnly = !editable;
        // AvalonEdit swaps the provider only when IsReadOnly changes, so it is set here every time: the whole document
        // is read-only when not editing, and only the filler lines are locked when editing.
        Editor.TextArea.ReadOnlySectionProvider = editable ? _locked : WholeDocument.Instance;
    }

    public void ApplyFormat(FormatSettings format)
    {
        Editor.Options.IndentationSize = Math.Clamp(format.TabSize, 1, 16);
        Editor.Options.ShowSpaces = format.ShowWhitespace;
        Editor.Options.ShowTabs = format.ShowWhitespace;
        Editor.Options.ShowEndOfLine = format.ShowEndOfLine;

        _renderer.ShowInline = format.InlineDifferences;
        if (_syntaxEnabled != format.SyntaxHighlighting)
        {
            _syntaxEnabled = format.SyntaxHighlighting;
            UpdateSyntax();
        }
        Editor.TextArea.TextView.Redraw();
    }

    public void ScrollToRow(int rowIndex) => Editor.ScrollTo(rowIndex + 1, 0);

    /// <summary>Repaints the highlights and the line numbers, e.g. after a theme change.</summary>
    public void Refresh()
    {
        Editor.Background = DiffTheme.EditorBackground;
        Editor.Foreground = DiffTheme.EditorForeground;
        Editor.TextArea.TextView.Redraw();
        _margin.InvalidateVisual();
    }

    /// <summary>Row index of the caret, in the rows last passed to <see cref="Show"/>.</summary>
    public int CaretRow => Editor.TextArea.Caret.Line - 1;

    /// <summary>Nothing can be inserted or deleted (AvalonEdit's own provider for this is internal).</summary>
    private sealed class WholeDocument : IReadOnlySectionProvider
    {
        public static readonly WholeDocument Instance = new();

        public bool CanInsert(int offset) => false;

        public IEnumerable<ISegment> GetDeletableSegments(ISegment segment) => Enumerable.Empty<ISegment>();
    }

    /// <summary>Stops typing, pasting and deleting from touching the locked lines.</summary>
    private sealed class LockedLines : IReadOnlySectionProvider
    {
        public HashSet<DocumentLine> Lines { get; } = new();

        /// <summary>Inserting is allowed anywhere except on (or at the end of the text of) a locked line.</summary>
        public bool CanInsert(int offset)
        {
            foreach (var line in Lines)
            {
                if (!line.IsDeleted && offset >= line.Offset && offset <= line.Offset + line.Length)
                    return false;
            }
            return true;
        }

        public IEnumerable<ISegment> GetDeletableSegments(ISegment segment)
        {
            if (segment.Length == 0) yield break;

            int position = segment.Offset, end = segment.EndOffset;
            foreach (var span in Lines
                         .Where(l => !l.IsDeleted && l.Offset + l.TotalLength > position && l.Offset < end)
                         .OrderBy(l => l.Offset))
            {
                if (span.Offset > position) yield return new TextSegment { StartOffset = position, Length = span.Offset - position };
                position = Math.Max(position, span.Offset + span.TotalLength);
            }
            if (position < end) yield return new TextSegment { StartOffset = position, Length = end - position };
        }
    }
}
