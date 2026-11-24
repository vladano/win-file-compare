using System.Text.Json.Serialization;

namespace FileCompare.Core.Folders;

public enum FolderStatus
{
    /// <summary>On both sides and equal.</summary>
    Same,
    /// <summary>On both sides but different (or a folder that contains differences).</summary>
    Different,
    LeftOnly,
    RightOnly,
    /// <summary>Could not be compared (no access, a file on one side and a folder on the other, ...). See <see cref="FolderNode.Message"/>.</summary>
    Error,
    /// <summary>
    /// The sizes match and the content is still being compared. A folder is pending while it holds pending files and
    /// nothing that already differs. If the comparison was stopped, the file keeps this status and gets a
    /// <see cref="FolderNode.Message"/>.
    /// </summary>
    Pending
}

/// <summary>How two files with the same name are compared.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FolderCompareMode
{
    /// <summary>Same size, then the bytes are compared. Exact, but reads the files.</summary>
    Content,
    /// <summary>Same size and last write time (within 2 seconds). Fast, but a touched file looks different.</summary>
    SizeAndTime
}

/// <summary>What an entry looks like on one side. Folders have size 0.</summary>
public readonly record struct SideInfo(long Size, DateTime LastWriteTime);

public sealed class FolderCompareOptions
{
    /// <summary>Names of files and folders to skip. "*" and "?" work, case does not matter.</summary>
    public IReadOnlyList<string> IgnorePatterns { get; init; } = Array.Empty<string>();

    public FolderCompareMode Mode { get; init; } = FolderCompareMode.Content;

    /// <summary>How many files are read at the same time in a content compare. 0 picks a number for the disks.</summary>
    public int Parallelism { get; init; }
}

/// <summary>One file or folder in the comparison. Folders hold their children.</summary>
public sealed class FolderNode
{
    internal FolderNode(string name, string relativePath, bool isFolder, FolderNode? parent)
    {
        Name = name;
        RelativePath = relativePath;
        IsFolder = isFolder;
        Parent = parent;
    }

    public string Name { get; }

    /// <summary>Path below the compared root folders, with the system separator. Empty for the root.</summary>
    public string RelativePath { get; }

    public bool IsFolder { get; }
    public FolderNode? Parent { get; internal set; }

    /// <summary>Null when the entry does not exist on that side.</summary>
    public SideInfo? Left { get; internal set; }
    public SideInfo? Right { get; internal set; }

    public FolderStatus Status { get; internal set; }

    /// <summary>The reason when <see cref="Status"/> is <see cref="FolderStatus.Error"/>.</summary>
    public string? Message { get; internal set; }

    public List<FolderNode> Children { get; } = new();

    /// <summary>Works out a folder's status from its sides and children. Files keep the status they were given.</summary>
    internal void Recalculate()
    {
        if (!IsFolder) return;
        if (Message is not null) { Status = FolderStatus.Error; return; }

        if (Left is null && Right is not null) Status = FolderStatus.RightOnly;
        else if (Right is null && Left is not null) Status = FolderStatus.LeftOnly;
        else
        {
            bool pending = false;
            foreach (var child in Children)
            {
                if (child.Status == FolderStatus.Pending) pending = true;
                else if (child.Status != FolderStatus.Same) { Status = FolderStatus.Different; return; }
            }
            Status = pending ? FolderStatus.Pending : FolderStatus.Same;
        }
    }

    /// <summary>How deep the entry is: 0 for the root.</summary>
    internal int Depth
    {
        get
        {
            int depth = 0;
            for (var node = Parent; node is not null; node = node.Parent) depth++;
            return depth;
        }
    }

    public override string ToString() => $"{(IsFolder ? "[D]" : "[F]")} {RelativePath} ({Status})";
}

/// <summary>How many files are in each state.</summary>
public readonly record struct FolderSummary(int Same, int Different, int LeftOnly, int RightOnly, int Errors, int Pending = 0)
{
    public int Total => Same + Different + LeftOnly + RightOnly + Errors + Pending;

    public static FolderSummary Of(FolderNode root)
    {
        int same = 0, different = 0, left = 0, right = 0, errors = 0, pending = 0;

        void Walk(FolderNode node)
        {
            if (node.IsFolder && node.Message is null)
            {
                foreach (var child in node.Children) Walk(child);
                return;
            }
            switch (node.Status)
            {
                case FolderStatus.Same: same++; break;
                case FolderStatus.Different: different++; break;
                case FolderStatus.LeftOnly: left++; break;
                case FolderStatus.RightOnly: right++; break;
                case FolderStatus.Pending: pending++; break;
                default: errors++; break;
            }
        }

        Walk(root);
        return new FolderSummary(same, different, left, right, errors, pending);
    }
}
