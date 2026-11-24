using System.Diagnostics;

namespace FileCompare.Core.Text;

public enum CopyDirection
{
    LeftToRight,
    RightToLeft
}

/// <summary>One side of a comparison: the file as loaded plus the text as it is now (possibly edited).</summary>
public sealed class DocumentState
{
    internal DocumentState(LoadedText info)
    {
        Info = info;
        Text = SavedText = LineTools.NormalizeLineEndings(info.Text);
    }

    /// <summary>File details (path, size, date, encoding, line ending). Updated after saving.</summary>
    public LoadedText Info { get; internal set; }

    public string Path => Info.Path;

    /// <summary>Current text with "\n" line breaks.</summary>
    public string Text { get; internal set; }

    /// <summary>Text as it is on disk.</summary>
    public string SavedText { get; internal set; }

    public bool IsModified => !string.Equals(Text, SavedText, StringComparison.Ordinal);
}

/// <summary>
/// Two files being compared, kept in memory so they can be edited, copied between and saved.
/// All UI-independent logic of a text compare lives here.
/// </summary>
public sealed class TextCompareSession
{
    private TextCompareSession(DocumentState left, DocumentState right, DiffOptions options)
    {
        Left = left;
        Right = right;
        Options = options;
        Diff = TextDiffService.Compare(left.Text, right.Text, options);
    }

    public DocumentState Left { get; private set; }
    public DocumentState Right { get; private set; }
    public DiffOptions Options { get; private set; }
    public DiffResult Diff { get; private set; }

    /// <summary>How long loading and the first compare took.</summary>
    public TimeSpan LoadTime { get; private set; }

    public bool IsModified => Left.IsModified || Right.IsModified;

    /// <exception cref="IOException">A file could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">A file is not accessible.</exception>
    public static TextCompareSession Open(
        string leftPath, string rightPath, DiffOptions options,
        EncodingChoice leftEncoding = EncodingChoice.Detect, EncodingChoice rightEncoding = EncodingChoice.Detect)
    {
        var sw = Stopwatch.StartNew();
        var left = new DocumentState(TextFileLoader.Load(leftPath, leftEncoding));
        var right = new DocumentState(TextFileLoader.Load(rightPath, rightEncoding));
        var session = new TextCompareSession(left, right, options);
        sw.Stop();
        session.LoadTime = sw.Elapsed;
        return session;
    }

    /// <summary>Compares again, for example after the Rules changed.</summary>
    public void Recompare(DiffOptions? options = null)
    {
        if (options is not null) Options = options;
        Diff = TextDiffService.Compare(Left.Text, Right.Text, Options);
    }

    // ----- Undo and redo -----

    /// <summary>The text of both sides at one moment. Documents are not part of it, only their text.</summary>
    private sealed record Snapshot(string Left, string Right);

    private const int MaxHistory = 100;
    private readonly List<Snapshot> _undo = new();
    private readonly List<Snapshot> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    private Snapshot Capture() => new(Left.Text, Right.Text);

    private static void Push(List<Snapshot> stack, Snapshot snapshot)
    {
        stack.Add(snapshot);
        if (stack.Count > MaxHistory) stack.RemoveAt(0);
    }

    private static Snapshot Pop(List<Snapshot> stack)
    {
        var snapshot = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return snapshot;
    }

    /// <summary>Call before every user change to the text: remembers the current state and drops the redo steps.</summary>
    private void Remember()
    {
        Push(_undo, Capture());
        _redo.Clear();
    }

    private void Restore(Snapshot snapshot)
    {
        Left.Text = snapshot.Left;
        Right.Text = snapshot.Right;
        Recompare();
    }

    /// <summary>Goes back one change (typing, copy). Returns false when there is nothing to undo.</summary>
    public bool Undo()
    {
        if (!CanUndo) return false;
        Push(_redo, Capture());
        Restore(Pop(_undo));
        return true;
    }

    /// <summary>Repeats a change that was undone. Returns false when there is nothing to redo.</summary>
    public bool Redo()
    {
        if (!CanRedo) return false;
        Push(_undo, Capture());
        Restore(Pop(_redo));
        return true;
    }

    // ----- Changing the text -----

    /// <summary>
    /// Takes in text the user typed in one or both sides, compares again and remembers one undo step.
    /// Pass null for a side that was not edited.
    /// </summary>
    /// <returns>False when the text is the same as before (nothing changed, no undo step).</returns>
    public bool ApplyEdit(string? leftText, string? rightText)
    {
        string? left = leftText is null ? null : LineTools.NormalizeLineEndings(leftText);
        string? right = rightText is null ? null : LineTools.NormalizeLineEndings(rightText);
        bool leftChanged = left is not null && !string.Equals(left, Left.Text, StringComparison.Ordinal);
        bool rightChanged = right is not null && !string.Equals(right, Right.Text, StringComparison.Ordinal);
        if (!leftChanged && !rightChanged) return false;

        Remember();
        if (leftChanged) Left.Text = left!;
        if (rightChanged) Right.Text = right!;
        Recompare();
        return true;
    }

    /// <summary>Exchanges the two sides, including unsaved edits. The undo history is cleared because it belongs to the old sides.</summary>
    public void Swap()
    {
        (Left, Right) = (Right, Left);
        _undo.Clear();
        _redo.Clear();
        Recompare();
    }

    /// <summary>
    /// Makes the target side match the source side inside one difference section, then compares again.
    /// </summary>
    /// <returns>False when <paramref name="sectionIndex"/> is not a valid section.</returns>
    public bool CopySection(int sectionIndex, CopyDirection direction)
    {
        if (sectionIndex < 0 || sectionIndex >= Diff.DifferenceCount) return false;

        int start = Diff.SectionStarts[sectionIndex], end = Diff.SectionEnds[sectionIndex];
        bool toRight = direction == CopyDirection.LeftToRight;
        var lines = new List<string>(Diff.Rows.Count);
        for (int i = 0; i < Diff.Rows.Count; i++)
        {
            var row = Diff.Rows[i];
            bool inSection = i >= start && i < end;
            // Inside the section take the source side (a filler there means "delete this line");
            // outside keep the target side exactly as it is.
            string? text = (toRight == inSection) ? row.LeftText : row.RightText;
            if (text is not null) lines.Add(text);
        }

        Remember();
        (toRight ? Right : Left).Text = string.Join("\n", lines);
        Recompare();
        return true;
    }

    /// <summary>Writes one side back to its file with its original encoding and line ending.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file is read-only or not accessible.</exception>
    /// <exception cref="System.Text.EncoderFallbackException">The text cannot be stored in the file's encoding.</exception>
    public void Save(bool left)
    {
        var doc = left ? Left : Right;
        TextFileSaver.Save(doc.Path, doc.Text, doc.Info.FileEncoding, doc.Info.LineEnding);

        var file = new FileInfo(doc.Path);
        doc.SavedText = doc.Text;
        doc.Info = doc.Info with { SizeBytes = file.Length, LastWriteTime = file.LastWriteTime };
    }
}
