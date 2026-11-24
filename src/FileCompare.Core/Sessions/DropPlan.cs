namespace FileCompare.Core.Sessions;

/// <summary>Where files were dropped.</summary>
public enum DropSide
{
    Left,
    Right,
    /// <summary>Somewhere else on the page.</summary>
    Any
}

/// <summary>Works out the two paths after files or folders were dropped.</summary>
public static class DropPlan
{
    /// <summary>
    /// Two or more items replace both sides (the first two are used). One item replaces the side it was
    /// dropped on; dropped elsewhere it fills the empty side, or replaces the right side when both are set.
    /// </summary>
    public static (string Left, string Right) Apply(string left, string right, IReadOnlyList<string> dropped, DropSide side)
    {
        if (dropped.Count == 0) return (left, right);
        if (dropped.Count >= 2) return (dropped[0], dropped[1]);

        string item = dropped[0];
        return side switch
        {
            DropSide.Left => (item, right),
            DropSide.Right => (left, item),
            _ when left.Length == 0 => (item, right),
            _ => (left, item)
        };
    }
}
