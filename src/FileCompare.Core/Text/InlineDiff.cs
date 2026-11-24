using DiffPlex;

namespace FileCompare.Core.Text;

/// <summary>A span of characters inside one line.</summary>
public readonly record struct TextRange(int Start, int Length);

/// <summary>The changed characters inside a pair of lines, one list per side.</summary>
public sealed record InlineDiffResult(IReadOnlyList<TextRange> Left, IReadOnlyList<TextRange> Right);

/// <summary>Finds the characters that changed inside a line that differs on both sides.</summary>
public static class InlineDiff
{
    /// <summary>Longer lines are skipped: a character diff of a minified file would be slow and unreadable.</summary>
    public const int MaxLineLength = 2000;

    /// <summary>Changes separated by this many unchanged characters or fewer are joined into one highlight.</summary>
    private const int MergeGap = 2;

    public static readonly InlineDiffResult None = new(Array.Empty<TextRange>(), Array.Empty<TextRange>());

    /// <summary>Inline changes for a row. Only rows with text on both sides that differ (Modified or Minor) have any.</summary>
    public static InlineDiffResult ForRow(DiffRow row, DiffOptions options)
    {
        if (row.LeftText is null || row.RightText is null) return None;

        return row.Kind switch
        {
            RowKind.Modified => Compute(row.LeftText, row.RightText, options.IgnoreCase, options.IgnoresAnyWhitespace),
            // A minor row is "equal" under the Rules, so show exactly what the Rules chose to ignore.
            RowKind.Minor => Compute(row.LeftText, row.RightText, ignoreCase: false, ignoreWhitespace: false),
            _ => None
        };
    }

    public static InlineDiffResult Compute(string left, string right, bool ignoreCase, bool ignoreWhitespace)
    {
        if (left.Length > MaxLineLength || right.Length > MaxLineLength) return None;

        var diff = new Differ().CreateCharacterDiffs(left, right, ignoreWhitespace, ignoreCase);
        var leftRanges = new List<TextRange>();
        var rightRanges = new List<TextRange>();
        foreach (var block in diff.DiffBlocks)
        {
            if (block.DeleteCountA > 0) leftRanges.Add(new TextRange(block.DeleteStartA, block.DeleteCountA));
            if (block.InsertCountB > 0) rightRanges.Add(new TextRange(block.InsertStartB, block.InsertCountB));
        }
        return new InlineDiffResult(Merge(leftRanges), Merge(rightRanges));
    }

    private static List<TextRange> Merge(List<TextRange> ranges)
    {
        var merged = new List<TextRange>(ranges.Count);
        foreach (var range in ranges)
        {
            if (merged.Count > 0)
            {
                var last = merged[^1];
                if (range.Start - (last.Start + last.Length) <= MergeGap)
                {
                    merged[^1] = new TextRange(last.Start, range.Start + range.Length - last.Start);
                    continue;
                }
            }
            merged.Add(range);
        }
        return merged;
    }
}
