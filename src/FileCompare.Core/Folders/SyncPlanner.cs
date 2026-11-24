namespace FileCompare.Core.Folders;

/// <summary>
/// Works out what Update, Mirror and two-way Sync would do on a compared tree. Nothing is touched here; the plan is
/// shown to the user and then run by <see cref="FolderOperations.Execute"/>.
/// A folder that exists on one side only is one item (copied or deleted with everything in it).
/// </summary>
public static class SyncPlanner
{
    private static readonly ConflictChoice[] BothWays = { ConflictChoice.Skip, ConflictChoice.KeepLeft, ConflictChoice.KeepRight };
    private static readonly ConflictChoice[] SkipOnly = { ConflictChoice.Skip };

    // ----- Update: one way, never deletes -----

    /// <summary>
    /// Copies what is missing on the target and what is newer on the source. Files that differ but are not newer on
    /// the source are conflicts; the user may copy them anyway.
    /// </summary>
    public static SyncPlan PlanUpdate(FolderNode root, bool toRight)
    {
        var items = new List<SyncItem>();
        var copy = toRight ? SyncAction.CopyToRight : SyncAction.CopyToLeft;
        var keepSource = new[] { ConflictChoice.Skip, toRight ? ConflictChoice.KeepLeft : ConflictChoice.KeepRight };
        string source = toRight ? "left" : "right";

        void Walk(FolderNode node)
        {
            foreach (var child in node.Children)
            {
                var src = toRight ? child.Left : child.Right;
                var target = toRight ? child.Right : child.Left;
                if (IsUnusable(child, out string? problem))
                {
                    items.Add(new SyncItem(child, SyncAction.Conflict, problem!, SkipOnly));
                    continue;
                }
                if (src is null) continue;                               // only on the target: left alone
                if (target is null) { items.Add(new SyncItem(child, copy, $"only on the {source}")); continue; }
                if (child.IsFolder) { Walk(child); continue; }

                switch (child.Status)
                {
                    case FolderStatus.Same:
                        break;
                    case FolderStatus.Different when IsNewer(src.Value, target.Value):
                    case FolderStatus.Pending when IsNewer(src.Value, target.Value):
                        items.Add(new SyncItem(child, copy, $"newer on the {source}"));
                        break;
                    case FolderStatus.Different:
                        items.Add(new SyncItem(child, SyncAction.Conflict, $"different, but not newer on the {source}", keepSource));
                        break;
                    // Pending and not newer: most likely the same file (the comparison was stopped).
                }
            }
        }

        Walk(root);
        return new SyncPlan(toRight ? SyncKind.UpdateRight : SyncKind.UpdateLeft, items,
            leftFiles: CountFiles(root, left: true), rightFiles: CountFiles(root, left: false));
    }

    // ----- Mirror: one way, with deletes -----

    /// <summary>
    /// Makes the target an exact copy of the source: copies what is missing or different on the target, and deletes
    /// what exists only on the target. Refused when the source folder is missing or empty, because mirroring an empty
    /// folder deletes everything (usually a mistake, such as an unplugged drive).
    /// </summary>
    public static SyncPlan PlanMirror(FolderNode root, string leftRoot, string rightRoot, bool toRight)
    {
        var kind = toRight ? SyncKind.MirrorRight : SyncKind.MirrorLeft;
        string sourceRoot = toRight ? leftRoot : rightRoot;
        string source = toRight ? "left" : "right", target = toRight ? "right" : "left";
        if (IsMissingOrEmpty(sourceRoot))
            return new SyncPlan(kind, Array.Empty<SyncItem>(),
                $"The {source} folder is missing or empty, so mirroring it would delete everything on the {target} side. Nothing was done.");

        var items = new List<SyncItem>();
        var copy = toRight ? SyncAction.CopyToRight : SyncAction.CopyToLeft;
        var delete = toRight ? SyncAction.DeleteRight : SyncAction.DeleteLeft;

        void Walk(FolderNode node)
        {
            foreach (var child in node.Children)
            {
                var src = toRight ? child.Left : child.Right;
                var dst = toRight ? child.Right : child.Left;
                if (IsUnusable(child, out string? problem))
                {
                    items.Add(new SyncItem(child, SyncAction.Conflict, problem!, SkipOnly));
                    continue;
                }
                if (src is null) { items.Add(new SyncItem(child, delete, $"only on the {target}")); continue; }
                if (dst is null) { items.Add(new SyncItem(child, copy, $"only on the {source}")); continue; }
                if (child.IsFolder) { Walk(child); continue; }

                if (child.Status == FolderStatus.Different) items.Add(new SyncItem(child, copy, "different"));
                else if (child.Status == FolderStatus.Pending) items.Add(new SyncItem(child, copy, "not compared"));
            }
        }

        Walk(root);
        return new SyncPlan(kind, items, leftFiles: CountFiles(root, left: true), rightFiles: CountFiles(root, left: false));
    }

    // ----- Two-way sync -----

    /// <summary>
    /// Brings the changes made on each side since the last sync to the other side.
    /// <list type="bullet">
    /// <item>Changed on one side only: copied to the other side.</item>
    /// <item>New on one side: copied to the other side.</item>
    /// <item>Deleted on one side and unchanged on the other: deleted there too.</item>
    /// <item>Changed on both sides, new on both sides, or deleted on one and changed on the other: a conflict.</item>
    /// </list>
    /// Without a baseline (the first sync of the pair) nothing is deleted: entries on one side are copied, and a
    /// file that differs goes from the newer side (a conflict when neither is newer).
    /// </summary>
    public static SyncPlan PlanTwoWay(FolderNode root, string leftRoot, string rightRoot, SyncBaseline? baseline)
    {
        if (baseline is not null && (IsMissingOrEmpty(leftRoot) || IsMissingOrEmpty(rightRoot)))
        {
            string side = IsMissingOrEmpty(leftRoot) ? "left" : "right";
            return new SyncPlan(SyncKind.TwoWay, Array.Empty<SyncItem>(),
                $"The {side} folder is missing or empty. A sync would delete everything on the other side. " +
                "If you emptied it on purpose, use Mirror instead. Nothing was done.");
        }

        var items = new List<SyncItem>();
        var planner = new TwoWay(leftRoot, rightRoot, baseline, items);
        planner.Walk(root);
        return new SyncPlan(SyncKind.TwoWay, items, firstSync: baseline is null,
            leftFiles: CountFiles(root, left: true), rightFiles: CountFiles(root, left: false));
    }

    private sealed class TwoWay(string leftRoot, string rightRoot, SyncBaseline? baseline, List<SyncItem> items)
    {
        public void Walk(FolderNode node)
        {
            foreach (var child in node.Children) Plan(child);
        }

        private void Plan(FolderNode node)
        {
            if (IsUnusable(node, out string? problem))
            {
                items.Add(new SyncItem(node, SyncAction.Conflict, problem!, SkipOnly));
                return;
            }

            var entry = baseline?.Find(node.RelativePath);
            if (node.Left is not null && node.Right is not null)
            {
                if (node.IsFolder) Walk(node);
                else PlanBoth(node, entry);
                return;
            }

            bool onLeft = node.Left is not null;
            string here = onLeft ? "left" : "right", there = onLeft ? "right" : "left";
            var copy = onLeft ? SyncAction.CopyToRight : SyncAction.CopyToLeft;
            var delete = onLeft ? SyncAction.DeleteLeft : SyncAction.DeleteRight;

            if (entry is null)
            {
                items.Add(new SyncItem(node, copy, $"new on the {here}"));
                return;
            }

            // It was on both sides after the last sync, so it was deleted on the other side.
            if (ChangedInside(node, onLeft))
                items.Add(new SyncItem(node, SyncAction.Conflict, $"deleted on the {there}, changed on the {here}", BothWays));
            else
                items.Add(new SyncItem(node, delete, $"deleted on the {there}"));
        }

        private void PlanBoth(FolderNode node, SyncBaseline.Entry? entry)
        {
            if (node.Status == FolderStatus.Same) return;
            var l = node.Left!.Value;
            var r = node.Right!.Value;

            if (baseline is null)
            {
                // First sync: no history, so the newer side wins; equal times are a conflict.
                if (IsNewer(l, r)) items.Add(new SyncItem(node, SyncAction.CopyToRight, "newer on the left (first sync)"));
                else if (IsNewer(r, l)) items.Add(new SyncItem(node, SyncAction.CopyToLeft, "newer on the right (first sync)"));
                else if (node.Status == FolderStatus.Different)
                    items.Add(new SyncItem(node, SyncAction.Conflict, "different, and neither side is newer (first sync)", BothWays));
                return;
            }

            if (entry is null || entry.IsFolder)
            {
                items.Add(new SyncItem(node, SyncAction.Conflict, "new on both sides, and different", BothWays));
                return;
            }

            bool leftChanged = baseline.Changed(entry, l, left: true, Path.Combine(leftRoot, node.RelativePath));
            bool rightChanged = baseline.Changed(entry, r, left: false, Path.Combine(rightRoot, node.RelativePath));
            if (leftChanged && !rightChanged) items.Add(new SyncItem(node, SyncAction.CopyToRight, "changed on the left"));
            else if (rightChanged && !leftChanged) items.Add(new SyncItem(node, SyncAction.CopyToLeft, "changed on the right"));
            else if (leftChanged && rightChanged) items.Add(new SyncItem(node, SyncAction.Conflict, "changed on both sides", BothWays));
            else if (node.Status == FolderStatus.Different)
                items.Add(new SyncItem(node, SyncAction.Conflict, "different, though neither side changed since the last sync", BothWays));
            // Pending and neither changed: they were the same at the last sync, and still are as far as can be told.
        }

        /// <summary>For an entry on one side only: was it (or anything in it) changed or added since the baseline?</summary>
        private bool ChangedInside(FolderNode node, bool onLeft)
        {
            string root = onLeft ? leftRoot : rightRoot;
            if (!node.IsFolder)
            {
                var entry = baseline!.Find(node.RelativePath);
                return entry is null || entry.IsFolder || baseline.Changed(entry, onLeft ? node.Left : node.Right, onLeft, Path.Combine(root, node.RelativePath));
            }
            if (baseline!.Find(node.RelativePath) is not { IsFolder: true }) return true;
            return node.Children.Any(child => ChangedInside(child, onLeft));
        }
    }

    // ----- Helpers -----

    /// <summary>Entries that cannot be copied or deleted safely: a file on one side and a folder on the other, unreadable folders, read errors.</summary>
    private static bool IsUnusable(FolderNode node, out string? problem)
    {
        problem = null;
        if (node.Status == FolderStatus.Error || node.Message is not null && node.Status != FolderStatus.Pending)
            problem = node.Message ?? "could not be compared";
        return problem is not null;
    }

    private static bool IsNewer(SideInfo a, SideInfo b) => a.LastWriteTime - b.LastWriteTime > FolderComparer.TimeTolerance;

    internal static bool IsMissingOrEmpty(string root)
    {
        try { return !Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    private static int CountFiles(FolderNode root, bool left)
    {
        int count = 0;
        void Walk(FolderNode n)
        {
            if (!n.IsFolder) { if ((left ? n.Left : n.Right) is not null) count++; return; }
            foreach (var child in n.Children) Walk(child);
        }
        Walk(root);
        return count;
    }
}
