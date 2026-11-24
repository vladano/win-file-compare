namespace FileCompare.Core.Text;

/// <summary>Which rows of a comparison are shown.</summary>
public enum ViewMode
{
    /// <summary>Every row.</summary>
    All,
    /// <summary>Only the differences.</summary>
    Diffs,
    /// <summary>Only the lines that are the same.</summary>
    Same,
    /// <summary>The differences plus a few lines around each.</summary>
    Context
}

/// <summary>Turns the full list of rows into what a view mode shows, collapsing hidden runs into one "filtered lines" row.</summary>
public static class DiffViewBuilder
{
    /// <param name="contextLines">Lines kept around each difference in <see cref="ViewMode.Context"/>.</param>
    /// <param name="showMinor">When false, minor differences look like plain same lines. When true they are shown as differences.</param>
    public static IReadOnlyList<DiffRow> Build(IReadOnlyList<DiffRow> rows, ViewMode mode, int contextLines, bool showMinor)
    {
        var source = showMinor ? rows : rows.Select(r => r.Kind == RowKind.Minor ? r with { Kind = RowKind.Same } : r).ToList();
        if (mode == ViewMode.All) return source;

        var keep = new bool[source.Count];
        if (mode == ViewMode.Same)
        {
            for (int i = 0; i < source.Count; i++) keep[i] = source[i].Kind == RowKind.Same;
        }
        else
        {
            int radius = mode == ViewMode.Context ? Math.Max(0, contextLines) : 0;
            for (int i = 0; i < source.Count; i++)
            {
                if (!source[i].IsDifference && source[i].Kind != RowKind.Minor) continue;
                int from = Math.Max(0, i - radius), to = Math.Min(source.Count - 1, i + radius);
                for (int j = from; j <= to; j++) keep[j] = true;
            }
        }

        var result = new List<DiffRow>();
        int hidden = 0;
        for (int i = 0; i < source.Count; i++)
        {
            if (keep[i])
            {
                if (hidden > 0) { result.Add(FilteredRow(hidden)); hidden = 0; }
                result.Add(source[i]);
            }
            else hidden++;
        }
        if (hidden > 0) result.Add(FilteredRow(hidden));
        return result;
    }

    private static DiffRow FilteredRow(int count)
    {
        string label = $"··· {count} filtered line{(count == 1 ? "" : "s")} ···";
        return new DiffRow(null, label, null, label, RowKind.Filtered);
    }
}
