namespace FileCompare.Core.Text;

public enum RowKind
{
    /// <summary>Identical on both sides.</summary>
    Same,
    /// <summary>Different text on both sides.</summary>
    Modified,
    /// <summary>Only on the left; the right side has a filler line.</summary>
    LeftOnly,
    /// <summary>Only on the right; the left side has a filler line.</summary>
    RightOnly,
    /// <summary>Text differs, but only in ways the Rules say are unimportant (whitespace or case).</summary>
    Minor,
    /// <summary>Not a file line: stands for a run of lines hidden by a view filter.</summary>
    Filtered
}

/// <summary>One aligned row of a side-by-side comparison. A null number/text means a filler line on that side.</summary>
public sealed record DiffRow(int? LeftNumber, string? LeftText, int? RightNumber, string? RightText, RowKind Kind)
{
    /// <summary>True for important differences (Modified, LeftOnly, RightOnly). Minor differences do not count.</summary>
    public bool IsDifference => Kind is RowKind.Modified or RowKind.LeftOnly or RowKind.RightOnly;
}

/// <summary>How lines are matched up between the two files.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum DiffAlgorithm
{
    /// <summary>Shortest edit script (Myers). Fast and the usual choice.</summary>
    Myers,
    /// <summary>Anchors on lines that occur once in both files. Often gives cleaner results for source code.</summary>
    Patience
}

/// <summary>What the comparison treats as unimportant. By default every difference is important.</summary>
public sealed class DiffOptions
{
    public DiffAlgorithm Algorithm { get; init; } = DiffAlgorithm.Myers;

    /// <summary>Ignore all whitespace, wherever it is.</summary>
    public bool IgnoreWhitespace { get; init; }
    public bool IgnoreLeadingWhitespace { get; init; }
    public bool IgnoreTrailingWhitespace { get; init; }

    /// <summary>Ignore the amount of whitespace between words.</summary>
    public bool IgnoreEmbeddedWhitespace { get; init; }

    public bool IgnoreCase { get; init; }

    /// <summary>True when any of the whitespace rules is on.</summary>
    public bool IgnoresAnyWhitespace =>
        IgnoreWhitespace || IgnoreLeadingWhitespace || IgnoreTrailingWhitespace || IgnoreEmbeddedWhitespace;
}

public sealed class DiffResult
{
    public DiffResult(IReadOnlyList<DiffRow> rows)
    {
        Rows = rows;
        var starts = new List<int>();
        var ends = new List<int>();
        for (int i = 0; i < rows.Count; i++)
        {
            bool isDiff = rows[i].IsDifference;
            bool previousIsDiff = i > 0 && rows[i - 1].IsDifference;
            if (isDiff && !previousIsDiff) starts.Add(i);
            if (!isDiff && previousIsDiff) ends.Add(i);
        }
        if (rows.Count > 0 && rows[^1].IsDifference) ends.Add(rows.Count);

        SectionStarts = starts;
        SectionEnds = ends;
    }

    public IReadOnlyList<DiffRow> Rows { get; }

    /// <summary>Row index where each contiguous block of differences begins.</summary>
    public IReadOnlyList<int> SectionStarts { get; }

    /// <summary>Row index just after each block of differences (exclusive end).</summary>
    public IReadOnlyList<int> SectionEnds { get; }

    public int DifferenceCount => SectionStarts.Count;
    public bool AreSame => SectionStarts.Count == 0;

    /// <summary>Index of the section that contains <paramref name="rowIndex"/>, or -1.</summary>
    public int SectionAt(int rowIndex)
    {
        for (int i = 0; i < SectionStarts.Count; i++)
            if (rowIndex >= SectionStarts[i] && rowIndex < SectionEnds[i]) return i;
        return -1;
    }
}
