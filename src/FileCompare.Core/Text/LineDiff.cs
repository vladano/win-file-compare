using DiffPlex;

namespace FileCompare.Core.Text;

/// <summary>A run of changed lines: left lines [StartA, StartA+CountA) were replaced by right lines [StartB, StartB+CountB).</summary>
public readonly record struct LineBlock(int StartA, int CountA, int StartB, int CountB);

/// <summary>Finds which lines changed between two lists of (already normalized) lines.</summary>
internal static class LineDiff
{
    public static List<LineBlock> Compute(string[] a, string[] b, DiffAlgorithm algorithm)
    {
        var blocks = new List<LineBlock>();
        if (algorithm == DiffAlgorithm.Patience) PatienceRange(a, 0, a.Length, b, 0, b.Length, blocks);
        else blocks.AddRange(MyersRange(a, 0, a.Length, b, 0, b.Length));
        return blocks;
    }

    // ----- Myers (through DiffPlex) -----

    private static IEnumerable<LineBlock> MyersRange(string[] a, int aLo, int aHi, string[] b, int bLo, int bHi)
    {
        var subA = a[aLo..aHi];
        var subB = b[bLo..bHi];

        // DiffPlex wants text and a function that splits it. The two names stand for the two line lists.
        var result = new Differ().CreateCustomDiffs("A", "B", false, name => name == "A" ? subA : subB);
        return result.DiffBlocks.Select(x =>
            new LineBlock(aLo + x.DeleteStartA, x.DeleteCountA, bLo + x.InsertStartB, x.InsertCountB));
    }

    // ----- Patience -----

    /// <summary>
    /// Cuts the range at lines that appear exactly once on each side (in the same order),
    /// then repeats inside the pieces. Pieces without such lines are handed to Myers.
    /// </summary>
    private static void PatienceRange(string[] a, int aLo, int aHi, string[] b, int bLo, int bHi, List<LineBlock> output)
    {
        while (aLo < aHi && bLo < bHi && a[aLo] == b[bLo]) { aLo++; bLo++; }
        while (aLo < aHi && bLo < bHi && a[aHi - 1] == b[bHi - 1]) { aHi--; bHi--; }

        if (aLo == aHi && bLo == bHi) return;
        if (aLo == aHi || bLo == bHi)
        {
            output.Add(new LineBlock(aLo, aHi - aLo, bLo, bHi - bLo));
            return;
        }

        var anchors = UniqueCommonLines(a, aLo, aHi, b, bLo, bHi);
        if (anchors.Count == 0)
        {
            output.AddRange(MyersRange(a, aLo, aHi, b, bLo, bHi));
            return;
        }

        int previousA = aLo, previousB = bLo;
        foreach (var (anchorA, anchorB) in anchors)
        {
            PatienceRange(a, previousA, anchorA, b, previousB, anchorB, output);
            previousA = anchorA + 1;
            previousB = anchorB + 1;
        }
        PatienceRange(a, previousA, aHi, b, previousB, bHi, output);
    }

    /// <summary>
    /// Lines that occur once on each side, reduced to the longest run whose positions increase on both sides
    /// (so the anchors never cross).
    /// </summary>
    private static List<(int A, int B)> UniqueCommonLines(string[] a, int aLo, int aHi, string[] b, int bLo, int bHi)
    {
        var seen = new Dictionary<string, (int CountA, int IndexA, int CountB, int IndexB)>();
        for (int i = aLo; i < aHi; i++)
        {
            seen[a[i]] = seen.TryGetValue(a[i], out var v) ? (v.CountA + 1, v.IndexA, 0, -1) : (1, i, 0, -1);
        }
        for (int j = bLo; j < bHi; j++)
        {
            if (!seen.TryGetValue(b[j], out var v)) continue;
            seen[b[j]] = (v.CountA, v.IndexA, v.CountB + 1, v.CountB == 0 ? j : v.IndexB);
        }

        var pairs = seen.Values
            .Where(v => v.CountA == 1 && v.CountB == 1)
            .Select(v => (A: v.IndexA, B: v.IndexB))
            .OrderBy(p => p.A)
            .ToList();

        // Longest increasing subsequence of B (patience sorting with back pointers).
        var tails = new List<int>();
        var previous = new int[pairs.Count];
        for (int i = 0; i < pairs.Count; i++)
        {
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (pairs[tails[mid]].B < pairs[i].B) lo = mid + 1; else hi = mid;
            }
            previous[i] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count) tails.Add(i); else tails[lo] = i;
        }

        var result = new List<(int, int)>(tails.Count);
        for (int k = tails.Count > 0 ? tails[^1] : -1; k >= 0; k = previous[k]) result.Add(pairs[k]);
        result.Reverse();
        return result;
    }
}
