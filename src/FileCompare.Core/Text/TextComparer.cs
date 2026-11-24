namespace FileCompare.Core.Text;

/// <summary>What the two path inputs allow the app to do.</summary>
public enum CompareInputState
{
    Ready,
    NeedBothPaths,
    NeedOtherPath,
    FolderPaths
}

/// <summary>Validates user-entered paths before a <see cref="TextCompareSession"/> is opened.</summary>
public static class TextComparer
{
    /// <summary>Trims whitespace and the quotes Explorer adds around pasted paths.</summary>
    public static string NormalizePath(string? path) => (path ?? "").Trim().Trim('"');

    public static CompareInputState Validate(string left, string right)
    {
        if (left.Length == 0 && right.Length == 0) return CompareInputState.NeedBothPaths;
        if (left.Length == 0 || right.Length == 0) return CompareInputState.NeedOtherPath;
        if (Directory.Exists(left) || Directory.Exists(right)) return CompareInputState.FolderPaths;
        return CompareInputState.Ready;
    }
}
