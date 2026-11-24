namespace FileCompare.Core.Folders;

/// <summary>Which kinds of entries are shown.</summary>
[Flags]
public enum FolderFilter
{
    None = 0,
    Same = 1,
    Different = 2,
    LeftOnly = 4,
    RightOnly = 8,
    All = Same | Different | LeftOnly | RightOnly
}

/// <summary>One row of the folder list.</summary>
/// <param name="HasChildren">The entry is a folder with something to show inside it, so it can be expanded.</param>
public sealed record VisibleNode(FolderNode Node, int Depth, bool HasChildren, bool IsExpanded);

/// <summary>Turns the tree into the flat list a grid shows: filtered, and with collapsed folders hiding their content.</summary>
public static class FolderListBuilder
{
    public static FolderFilter FilterFor(FolderStatus status) => status switch
    {
        FolderStatus.Same => FolderFilter.Same,
        FolderStatus.LeftOnly => FolderFilter.LeftOnly,
        FolderStatus.RightOnly => FolderFilter.RightOnly,
        // Not known yet: shown when either of the two possible outcomes is.
        FolderStatus.Pending => FolderFilter.Same | FolderFilter.Different,
        _ => FolderFilter.Different // Different and Error: both need attention
    };

    /// <summary>The rows below the root folder.</summary>
    /// <param name="expanded">Relative paths of the folders that are open.</param>
    public static List<VisibleNode> Build(FolderNode root, FolderFilter filter, ISet<string> expanded) =>
        BuildChildren(root, 0, filter, expanded);

    /// <summary>The rows for the content of one folder, as they would appear right below it.</summary>
    public static List<VisibleNode> BuildChildren(FolderNode folder, int depth, FolderFilter filter, ISet<string> expanded)
    {
        var rows = new List<VisibleNode>();
        foreach (var child in folder.Children) Collect(child, depth, filter, expanded, rows);
        return rows;
    }

    /// <summary>
    /// The folders to open at first: every folder that is known to differ. Folders that are the same, or still being
    /// compared, stay closed.
    /// </summary>
    public static HashSet<string> DefaultExpanded(FolderNode root)
    {
        var open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Walk(FolderNode node)
        {
            if (!node.IsFolder) return;
            if (node.Status is not (FolderStatus.Same or FolderStatus.Pending) && node.RelativePath.Length > 0) open.Add(node.RelativePath);
            foreach (var child in node.Children) Walk(child);
        }

        Walk(root);
        return open;
    }

    private static bool Collect(FolderNode node, int depth, FolderFilter filter, ISet<string> expanded, List<VisibleNode> output)
    {
        if (!node.IsFolder)
        {
            if ((filter & FilterFor(node.Status)) == 0) return false;
            output.Add(new VisibleNode(node, depth, false, false));
            return true;
        }

        var inside = new List<VisibleNode>();
        foreach (var child in node.Children) Collect(child, depth + 1, filter, expanded, inside);

        // A folder shows when something inside shows. An empty folder shows when its own status is wanted.
        bool visible = inside.Count > 0 || (node.Children.Count == 0 && (filter & FilterFor(node.Status)) != 0);
        if (!visible) return false;

        bool hasChildren = inside.Count > 0;
        bool isExpanded = hasChildren && expanded.Contains(node.RelativePath);
        output.Add(new VisibleNode(node, depth, hasChildren, isExpanded));
        if (isExpanded) output.AddRange(inside);
        return true;
    }
}
