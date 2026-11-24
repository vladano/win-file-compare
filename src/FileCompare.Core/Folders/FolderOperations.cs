using Microsoft.VisualBasic.FileIO;

namespace FileCompare.Core.Folders;

/// <summary>What an operation did. Errors hold one readable line per item that failed.</summary>
public sealed record OperationResult(int Done, IReadOnlyList<string> Errors)
{
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>Copying and deleting files and folders found by a comparison, and running sync plans.</summary>
public static class FolderOperations
{
    // ----- Copy -----

    /// <summary>
    /// Copies entries to the other side. Folders are copied with their content; files that are already
    /// the same are skipped. Anything that exists only on the target side is left alone.
    /// </summary>
    public static OperationResult Copy(
        IEnumerable<FolderNode> nodes, string leftRoot, string rightRoot, bool toRight,
        CancellationToken ct = default, IProgress<int>? progress = null)
    {
        string source = toRight ? leftRoot : rightRoot, target = toRight ? rightRoot : leftRoot;
        var errors = new List<string>();
        int done = 0;

        void CopyNode(FolderNode node)
        {
            ct.ThrowIfCancellationRequested();
            if ((toRight ? node.Left : node.Right) is null) return; // nothing on the source side

            try
            {
                if (node.IsFolder)
                {
                    Directory.CreateDirectory(Path.Combine(target, node.RelativePath));
                    foreach (var child in node.Children) CopyNode(child);
                }
                else if (node.Status != FolderStatus.Same)
                {
                    CopyFile(Path.Combine(source, node.RelativePath), Path.Combine(target, node.RelativePath));
                    done++;
                    progress?.Report(done);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{node.RelativePath}: {ex.Message}");
            }
        }

        foreach (var node in WithoutNestedNodes(nodes)) CopyNode(node);
        return new OperationResult(done, errors);
    }

    /// <summary>How many existing files on the target side a copy would replace. Used to ask before overwriting.</summary>
    public static int CountOverwrites(IEnumerable<FolderNode> nodes, bool toRight)
    {
        int count = 0;

        void Walk(FolderNode node)
        {
            if (node.IsFolder)
            {
                foreach (var child in node.Children) Walk(child);
                return;
            }
            bool bothExist = node.Left is not null && node.Right is not null;
            if (bothExist && node.Status != FolderStatus.Same) count++;
        }

        foreach (var node in WithoutNestedNodes(nodes))
            if ((toRight ? node.Left : node.Right) is not null) Walk(node);
        return count;
    }

    private static void CopyFile(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (File.Exists(to)) File.SetAttributes(to, File.GetAttributes(to) & ~FileAttributes.ReadOnly);
        File.Copy(from, to, overwrite: true);
    }

    // ----- Delete -----

    /// <summary>Deletes entries from one side. They go to the Recycle Bin unless <paramref name="recycle"/> is false.</summary>
    public static OperationResult Delete(
        IEnumerable<FolderNode> nodes, string leftRoot, string rightRoot, bool fromLeft, bool recycle = true)
    {
        string root = fromLeft ? leftRoot : rightRoot;
        var errors = new List<string>();
        int done = 0;

        foreach (var node in WithoutNestedNodes(nodes))
        {
            if ((fromLeft ? node.Left : node.Right) is null) continue;

            string path = Path.Combine(root, node.RelativePath);
            try
            {
                if (Directory.Exists(path))
                {
                    if (recycle) FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    else Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    if (recycle) FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    else
                    {
                        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                        File.Delete(path);
                    }
                }
                done++;
            }
            catch (OperationCanceledException)
            {
                // The user cancelled the Windows delete dialog: leave this item alone.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{node.RelativePath}: {ex.Message}");
            }
        }
        return new OperationResult(done, errors);
    }

    // ----- Sync plans (Update, Mirror, two-way Sync) -----

    /// <summary>
    /// Runs what a plan says, with the user's choices: first every copy, then every delete (to the Recycle Bin), so
    /// that stopping or a failure half way never loses the only copy of a file. Cancellation stops between items.
    /// </summary>
    /// <param name="progress">Receives the number of files copied or deleted so far.</param>
    /// <exception cref="InvalidOperationException">The plan was refused (see <see cref="SyncPlan.Refusal"/>).</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled; what was done so far stays done.</exception>
    public static OperationResult Execute(
        SyncPlan plan, string leftRoot, string rightRoot, CancellationToken ct = default, IProgress<int>? progress = null,
        bool recycle = true)
    {
        if (plan.Refusal is not null) throw new InvalidOperationException(plan.Refusal);

        var actions = plan.Actions.ToList();
        var errors = new List<string>();
        int done = 0;

        foreach (var (item, action) in actions.Where(a => a.Action is SyncAction.CopyToRight or SyncAction.CopyToLeft))
        {
            ct.ThrowIfCancellationRequested();
            int before = done;
            var result = Copy(new[] { item.Node }, leftRoot, rightRoot, action == SyncAction.CopyToRight, ct,
                progress is null ? null : new Progress(count => progress.Report(before + count)));
            done += result.Done;
            errors.AddRange(result.Errors);
            progress?.Report(done);
        }

        foreach (var (item, action) in actions.Where(a => a.Action is SyncAction.DeleteLeft or SyncAction.DeleteRight))
        {
            ct.ThrowIfCancellationRequested();
            var result = Delete(new[] { item.Node }, leftRoot, rightRoot, action == SyncAction.DeleteLeft, recycle);
            if (result.Done > 0) done += item.Files;
            errors.AddRange(result.Errors);
            progress?.Report(done);
        }

        return new OperationResult(done, errors);
    }

    /// <summary>Reports at once on the calling thread (System.Progress would post to a sync context).</summary>
    private sealed class Progress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    // ----- Helpers -----

    /// <summary>Drops entries that sit inside another selected folder, because that folder already covers them.</summary>
    private static List<FolderNode> WithoutNestedNodes(IEnumerable<FolderNode> nodes)
    {
        var all = nodes.ToList();
        var set = new HashSet<FolderNode>(all);
        return all.Where(n =>
        {
            for (var parent = n.Parent; parent is not null; parent = parent.Parent)
                if (set.Contains(parent)) return false;
            return true;
        }).ToList();
    }
}
