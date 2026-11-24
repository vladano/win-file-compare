using System.Diagnostics;

namespace FileCompare.Core.Folders;

/// <summary>
/// Two folders being compared. Holds the result tree and can update just the parts that changed
/// (after a copy or delete) instead of scanning everything again.
/// </summary>
public sealed class FolderCompareSession
{
    private readonly Stopwatch _clock;

    private FolderCompareSession(string leftRoot, string rightRoot, FolderCompareOptions options, ContentCache cache,
        FolderNode root, ContentComparison? contents, int parallelism, Stopwatch clock)
    {
        LeftRoot = leftRoot;
        RightRoot = rightRoot;
        Options = options;
        Cache = cache;
        Root = root;
        Contents = contents;
        Parallelism = parallelism;
        _clock = clock;
        FirstResultsTime = clock.Elapsed;
        Elapsed = contents is null ? clock.Elapsed : TimeSpan.Zero;
    }

    public string LeftRoot { get; }
    public string RightRoot { get; }
    public FolderCompareOptions Options { get; }
    public FolderNode Root { get; private set; }

    /// <summary>Content results of this session, reused by refreshes.</summary>
    public ContentCache Cache { get; }

    /// <summary>
    /// The files of the first scan whose content still has to be compared (null when there are none, for example in
    /// "size and time" mode). Run it with <see cref="CompareContentsAsync"/>.
    /// </summary>
    public ContentComparison? Contents { get; }

    /// <summary>How long it took until the tree could be shown (the listing, without reading files).</summary>
    public TimeSpan FirstResultsTime { get; }

    /// <summary>How long the whole first scan took, once the contents are compared. Zero before that.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>How many files are read at the same time.</summary>
    public int Parallelism { get; }

    public FolderSummary Summary => FolderSummary.Of(Root);

    /// <summary>
    /// The first, fast phase on a background thread: lists both folders. In content mode, files of the same size are
    /// <see cref="FolderStatus.Pending"/> until <see cref="CompareContentsAsync"/> has read them.
    /// </summary>
    /// <param name="progress">Receives the number of items listed so far.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static Task<FolderCompareSession> ScanAsync(
        string leftRoot, string rightRoot, FolderCompareOptions options,
        CancellationToken ct = default, IProgress<int>? progress = null) =>
        Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            var cache = new ContentCache();
            var comparer = new FolderComparer(leftRoot, rightRoot, options, cache);
            var (root, contents) = comparer.ScanStructure(ct, progress);
            return new FolderCompareSession(leftRoot, rightRoot, options, cache, root, contents, comparer.Parallelism, clock);
        }, ct);

    /// <summary>Both phases: the session is complete, nothing is pending.</summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static async Task<FolderCompareSession> OpenAsync(
        string leftRoot, string rightRoot, FolderCompareOptions options,
        CancellationToken ct = default, IProgress<int>? progress = null)
    {
        var session = await ScanAsync(leftRoot, rightRoot, options, ct, progress).ConfigureAwait(false);
        await session.CompareContentsAsync(ct).ConfigureAwait(false);
        session.ApplyContentResults();
        return session;
    }

    /// <summary>
    /// The second phase: reads the pending files on worker threads. The tree changes only when
    /// <see cref="ApplyContentResults"/> is called, so call that from the thread that shows the tree.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled; then call <see cref="MarkContentsStopped"/>.</exception>
    public async Task CompareContentsAsync(CancellationToken ct = default)
    {
        if (Contents is null) return;
        await Contents.RunAsync(ct).ConfigureAwait(false);
        Elapsed = _clock.Elapsed;
    }

    /// <summary>Puts the content results found so far into the tree. Returns the files that changed.</summary>
    public IReadOnlyList<FolderNode> ApplyContentResults() => Contents?.ApplyResults() ?? Array.Empty<FolderNode>();

    /// <summary>After a stop: applies what was found and marks the rest as not compared.</summary>
    public IReadOnlyList<FolderNode> MarkContentsStopped()
    {
        Elapsed = _clock.Elapsed;
        return Contents?.MarkStopped() ?? Array.Empty<FolderNode>();
    }

    /// <summary>
    /// Runs an Update, Mirror or Sync plan, then scans both folders again. After a two-way sync that finished without
    /// errors, the new state is saved as the baseline for the next sync. After a stop or a failure the old baseline
    /// stays, so the next sync sees the same changes again (and nothing done half way is mistaken for a delete).
    /// </summary>
    /// <exception cref="OperationCanceledException">Stopped; the tree was scanned again first.</exception>
    public async Task<OperationResult> RunPlanAsync(SyncPlan plan, SyncBaselineStore? baselines, CancellationToken ct = default,
        IProgress<int>? progress = null, bool recycle = true)
    {
        OperationResult result;
        try
        {
            result = await Task.Run(() => FolderOperations.Execute(plan, LeftRoot, RightRoot, ct, progress, recycle), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await RefreshAsync(new[] { "" }).ConfigureAwait(false);
            throw;
        }

        await RefreshAsync(new[] { "" }).ConfigureAwait(false);
        if (plan.Kind == SyncKind.TwoWay && baselines is not null && !result.HasErrors)
        {
            var root = Root;
            await Task.Run(() => baselines.Save(SyncBaseline.FromTree(root, LeftRoot, RightRoot))).ConfigureAwait(false);
        }
        return result;
    }

    /// <summary>The entry at a relative path, or null.</summary>
    public FolderNode? Find(string relativePath)
    {
        var node = Root;
        if (relativePath.Length == 0) return node;

        foreach (var part in relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            node = node.Children.FirstOrDefault(c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
            if (node is null) return null;
        }
        return node;
    }

    /// <summary>Scans the given entries again and fixes the tree and the folders above them.</summary>
    public Task RefreshAsync(IEnumerable<string> relativePaths, CancellationToken ct = default)
    {
        var paths = relativePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return Task.Run(() => Refresh(paths, ct), ct);
    }

    private void Refresh(List<string> paths, CancellationToken ct)
    {
        // The cache means only files that changed since they were compared are read again.
        var comparer = new FolderComparer(LeftRoot, RightRoot, Options, Cache);

        foreach (var path in paths.OrderBy(p => p.Length))
        {
            if (path.Length == 0)
            {
                Root = comparer.CompareRoot(ct);
                return;
            }

            string parentPath = Path.GetDirectoryName(path) ?? "";
            var parent = Find(parentPath);
            if (parent is null)
            {
                // The folder above is not in the tree (it changed too much): scan everything again.
                Root = comparer.CompareRoot(ct);
                return;
            }

            parent.Children.RemoveAll(c => string.Equals(c.RelativePath, path, StringComparison.OrdinalIgnoreCase));
            var fresh = comparer.CompareAt(path, ct);
            if (fresh is not null)
            {
                fresh.Parent = parent;
                parent.Children.Add(fresh);
                parent.Children.Sort(CompareSiblings);
            }

            // A folder may now exist on the other side too (or no longer), so refresh the folders above.
            for (var folder = parent; folder is not null; folder = folder.Parent)
            {
                string left = Path.Combine(LeftRoot, folder.RelativePath), right = Path.Combine(RightRoot, folder.RelativePath);
                folder.Left = Directory.Exists(left) ? FolderComparer.SideOf(new DirectoryInfo(left)) : null;
                folder.Right = Directory.Exists(right) ? FolderComparer.SideOf(new DirectoryInfo(right)) : null;
                folder.Recalculate();
            }
        }
    }

    /// <summary>Folders first, then by name, like the first scan.</summary>
    private static int CompareSiblings(FolderNode a, FolderNode b)
    {
        int byKind = (a.IsFolder ? 0 : 1).CompareTo(b.IsFolder ? 0 : 1);
        return byKind != 0 ? byKind : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
    }
}
