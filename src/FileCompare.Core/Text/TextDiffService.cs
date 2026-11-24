namespace FileCompare.Core.Text;

public static class TextDiffService
{
    public static DiffResult Compare(string left, string right, DiffOptions? options = null)
    {
        options ??= new DiffOptions();
        var leftLines = LineTools.Split(left);
        var rightLines = LineTools.Split(right);

        // The algorithm compares the normalized lines; we keep the originals to show and to copy.
        var leftKeys = leftLines.Select(l => LineTools.Normalize(l, options)).ToArray();
        var rightKeys = rightLines.Select(l => LineTools.Normalize(l, options)).ToArray();
        var blocks = LineDiff.Compute(leftKeys, rightKeys, options.Algorithm);

        var rows = new List<DiffRow>(Math.Max(leftLines.Length, rightLines.Length));
        int a = 0, b = 0;

        void AddUnchanged()
        {
            // Equal after normalizing; if the real text differs the difference is only minor.
            var kind = string.Equals(leftLines[a], rightLines[b], StringComparison.Ordinal) ? RowKind.Same : RowKind.Minor;
            rows.Add(new DiffRow(a + 1, leftLines[a], b + 1, rightLines[b], kind));
            a++;
            b++;
        }

        foreach (var block in blocks)
        {
            while (a < block.StartA) AddUnchanged();

            int deleted = block.CountA, inserted = block.CountB;
            int paired = Math.Min(deleted, inserted);
            for (int i = 0; i < paired; i++)
                rows.Add(new DiffRow(a + i + 1, leftLines[a + i], b + i + 1, rightLines[b + i], RowKind.Modified));
            for (int i = paired; i < deleted; i++)
                rows.Add(new DiffRow(a + i + 1, leftLines[a + i], null, null, RowKind.LeftOnly));
            for (int i = paired; i < inserted; i++)
                rows.Add(new DiffRow(null, null, b + i + 1, rightLines[b + i], RowKind.RightOnly));

            a += deleted;
            b += inserted;
        }
        while (a < leftLines.Length) AddUnchanged();

        return new DiffResult(rows);
    }
}
