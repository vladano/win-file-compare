namespace FileCompare.Core.Folders;

/// <summary>Which operation a plan is for.</summary>
public enum SyncKind
{
    /// <summary>Copy new and newer files from the left to the right. Never deletes.</summary>
    UpdateRight,
    /// <summary>Copy new and newer files from the right to the left. Never deletes.</summary>
    UpdateLeft,
    /// <summary>Make the right an exact copy of the left: copies and deletes on the right.</summary>
    MirrorRight,
    /// <summary>Make the left an exact copy of the right: copies and deletes on the left.</summary>
    MirrorLeft,
    /// <summary>Bring changes made on either side since the last sync to the other side, deletes included.</summary>
    TwoWay
}

/// <summary>What happens to one entry.</summary>
public enum SyncAction
{
    CopyToRight,
    CopyToLeft,
    DeleteLeft,
    DeleteRight,
    /// <summary>The planner cannot decide; the user picks one of <see cref="SyncItem.Choices"/>.</summary>
    Conflict
}

/// <summary>What the user picked for a conflict.</summary>
public enum ConflictChoice
{
    /// <summary>Leave both sides as they are.</summary>
    Skip,
    /// <summary>The left side wins: it is copied over the right, or the right is deleted if the left has nothing.</summary>
    KeepLeft,
    /// <summary>The right side wins.</summary>
    KeepRight
}

/// <summary>One entry of a plan: a file or a whole folder that exists on one side only.</summary>
public sealed class SyncItem
{
    internal SyncItem(FolderNode node, SyncAction action, string reason, IReadOnlyList<ConflictChoice>? choices = null)
    {
        Node = node;
        Action = action;
        Reason = reason;
        Choices = action == SyncAction.Conflict ? choices ?? new[] { ConflictChoice.Skip } : Array.Empty<ConflictChoice>();
        Files = CountFiles(node, action);
        Bytes = SumBytes(node, action);
    }

    public FolderNode Node { get; }
    public string RelativePath => Node.RelativePath;
    public SyncAction Action { get; }

    /// <summary>Why this was planned, in a few words ("newer on the left", "deleted on the right since the last sync").</summary>
    public string Reason { get; }

    /// <summary>For a conflict: what the user can pick. Only <see cref="ConflictChoice.Skip"/> when it cannot be resolved here.</summary>
    public IReadOnlyList<ConflictChoice> Choices { get; }

    /// <summary>For a conflict: what the user picked. Skip until they pick something else.</summary>
    public ConflictChoice Choice { get; set; } = ConflictChoice.Skip;

    /// <summary>False when the user left the item out in the preview.</summary>
    public bool Included { get; set; } = true;

    /// <summary>Files this item copies or deletes (a folder counts its files).</summary>
    public int Files { get; }

    /// <summary>Bytes copied (0 for deletes and conflicts).</summary>
    public long Bytes { get; }

    /// <summary>What will really be done, after the user's choice; null when nothing is done.</summary>
    public SyncAction? Effective
    {
        get
        {
            if (!Included) return null;
            if (Action != SyncAction.Conflict) return Action;
            return Choice switch
            {
                ConflictChoice.KeepLeft => Node.Left is not null ? SyncAction.CopyToRight : SyncAction.DeleteRight,
                ConflictChoice.KeepRight => Node.Right is not null ? SyncAction.CopyToLeft : SyncAction.DeleteLeft,
                _ => null
            };
        }
    }

    private static int CountFiles(FolderNode node, SyncAction action)
    {
        bool fromLeft = action is SyncAction.CopyToRight or SyncAction.DeleteLeft;
        if (!node.IsFolder) return 1;
        int count = 0;
        void Walk(FolderNode n)
        {
            if (!n.IsFolder) { if ((fromLeft ? n.Left : n.Right) is not null || action == SyncAction.Conflict) count++; return; }
            foreach (var child in n.Children) Walk(child);
        }
        Walk(node);
        return count;
    }

    private static long SumBytes(FolderNode node, SyncAction action)
    {
        bool fromLeft = action == SyncAction.CopyToRight;
        if (action is not (SyncAction.CopyToRight or SyncAction.CopyToLeft)) return 0;
        long bytes = 0;
        void Walk(FolderNode n)
        {
            if (!n.IsFolder) { bytes += (fromLeft ? n.Left : n.Right)?.Size ?? 0; return; }
            foreach (var child in n.Children) Walk(child);
        }
        Walk(node);
        return bytes;
    }

    public override string ToString() => $"{Action} {RelativePath} ({Reason})";
}

/// <summary>
/// What an Update, Mirror or Sync would do, worked out before anything is touched so the user can check it, leave
/// items out and resolve conflicts.
/// </summary>
public sealed class SyncPlan
{
    internal SyncPlan(SyncKind kind, IReadOnlyList<SyncItem> items, string? refusal = null, bool firstSync = false,
        int leftFiles = 0, int rightFiles = 0)
    {
        Kind = kind;
        Items = items;
        Refusal = refusal;
        IsFirstSync = firstSync;
        LeftFiles = leftFiles;
        RightFiles = rightFiles;
    }

    public SyncKind Kind { get; }
    public IReadOnlyList<SyncItem> Items { get; }

    /// <summary>Set when the plan must not be run at all (for example a mirror from an empty folder). Items is then empty.</summary>
    public string? Refusal { get; }

    /// <summary>Two-way sync only: there was no baseline yet, so nothing is deleted and differences go to the newer side.</summary>
    public bool IsFirstSync { get; }

    /// <summary>Files on each side when the plan was made (to judge how big a delete is).</summary>
    public int LeftFiles { get; }
    public int RightFiles { get; }

    public IEnumerable<SyncItem> Conflicts => Items.Where(i => i.Action == SyncAction.Conflict);

    /// <summary>The items that will be done, with what will be done to each.</summary>
    public IEnumerable<(SyncItem Item, SyncAction Action)> Actions =>
        Items.Where(i => i.Effective is not null).Select(i => (i, i.Effective!.Value));

    public int FilesToCopy => Actions.Where(a => a.Action is SyncAction.CopyToLeft or SyncAction.CopyToRight).Sum(a => a.Item.Files);
    public long BytesToCopy => Actions.Sum(a => a.Action switch
    {
        SyncAction.CopyToRight => a.Item.Node.IsFolder ? a.Item.Bytes : a.Item.Node.Left?.Size ?? 0,
        SyncAction.CopyToLeft => a.Item.Node.IsFolder ? a.Item.Bytes : a.Item.Node.Right?.Size ?? 0,
        _ => 0
    });
    public int FilesToDeleteLeft => Actions.Where(a => a.Action == SyncAction.DeleteLeft).Sum(a => DeletedFiles(a.Item, left: true));
    public int FilesToDeleteRight => Actions.Where(a => a.Action == SyncAction.DeleteRight).Sum(a => DeletedFiles(a.Item, left: false));

    /// <summary>Nothing will be done (everything is up to date, left out, or skipped).</summary>
    public bool IsEmpty => !Actions.Any();

    /// <summary>
    /// A second warning, with the numbers, when the plan deletes more than <see cref="ManyDeletes"/> files or more
    /// than half of the files on a side. Null when no warning is needed.
    /// </summary>
    public string? DeleteWarning
    {
        get
        {
            var parts = new List<string>();
            void Check(int deletes, int files, string side)
            {
                if (deletes == 0) return;
                if (deletes > ManyDeletes || deletes * 2 > files)
                    parts.Add($"{deletes:N0} of the {files:N0} files on the {side} side");
            }
            Check(FilesToDeleteLeft, LeftFiles, "left");
            Check(FilesToDeleteRight, RightFiles, "right");
            return parts.Count == 0 ? null : $"This deletes {string.Join(" and ", parts)}. They go to the Recycle Bin.";
        }
    }

    public const int ManyDeletes = 50;

    private static int DeletedFiles(SyncItem item, bool left)
    {
        if (!item.Node.IsFolder) return 1;
        int count = 0;
        void Walk(FolderNode n)
        {
            if (!n.IsFolder) { if ((left ? n.Left : n.Right) is not null) count++; return; }
            foreach (var child in n.Children) Walk(child);
        }
        Walk(item.Node);
        return count;
    }
}
