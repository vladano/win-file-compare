namespace FileCompare.Core.Folders;

/// <summary>
/// Scans two folder trees and builds a <see cref="FolderNode"/> tree describing the differences.
/// An instance is for one scan at a time (it counts items for progress reports).
/// </summary>
public sealed class FolderComparer
{
    /// <summary>File systems store times with different precision (FAT keeps 2 seconds), so close times count as equal.</summary>
    internal static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(2);

    private readonly string _leftRoot;
    private readonly string _rightRoot;
    private readonly FolderCompareOptions _options;
    private readonly IgnoreMatcher _ignore;
    private readonly ContentCache _cache;
    private readonly List<ContentJob> _jobs = new();
    private IProgress<int>? _progress;
    private int _count;

    /// <param name="cache">Earlier content results to reuse; a new one if null.</param>
    public FolderComparer(string leftRoot, string rightRoot, FolderCompareOptions options, ContentCache? cache = null)
    {
        _leftRoot = leftRoot;
        _rightRoot = rightRoot;
        _options = options;
        _ignore = new IgnoreMatcher(options.IgnorePatterns);
        _cache = cache ?? new ContentCache();
        _listParallelism = DiskInfo.ParallelReads(leftRoot, rightRoot, options.Parallelism);
        Parallelism = options.Mode == FolderCompareMode.Content ? _listParallelism : 1;
    }

    /// <summary>How many folders are listed at the same time (the same rule as for reading files).</summary>
    private readonly int _listParallelism;

    /// <summary>How many files a content compare reads at the same time (1 in "size and time" mode, which reads none).</summary>
    public int Parallelism { get; }

    /// <summary>
    /// The first, fast phase: lists both trees and compares names, sizes (and in "size and time" mode, times).
    /// In content mode, files of the same size are <see cref="FolderStatus.Pending"/> (unless the cache knows them);
    /// the returned comparison reads them. It is null when there is nothing to read.
    /// </summary>
    /// <param name="progress">Receives the number of items listed so far, every so often.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public (FolderNode Root, ContentComparison? Contents) ScanStructure(CancellationToken ct = default, IProgress<int>? progress = null)
    {
        _count = 0;
        _progress = progress;
        _jobs.Clear();

        var root = new FolderNode("", "", isFolder: true, parent: null)
        {
            Left = SideOf(new DirectoryInfo(_leftRoot)),
            Right = SideOf(new DirectoryInfo(_rightRoot))
        };
        FillChildren(root, _leftRoot, _rightRoot, ct);
        root.Recalculate();
        return (root, TakeContents());
    }

    /// <summary>Both phases, finished: no entry is left pending.</summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public FolderNode CompareRoot(CancellationToken ct = default, IProgress<int>? progress = null)
    {
        var (root, contents) = ScanStructure(ct, progress);
        contents?.RunToEnd(ct);
        return root;
    }

    /// <summary>Scans one entry again (for example after a copy), both phases. Returns null when it exists on neither side any more.</summary>
    public FolderNode? CompareAt(string relativePath, CancellationToken ct = default)
    {
        if (relativePath.Length == 0) return CompareRoot(ct);

        string name = Path.GetFileName(relativePath);
        if (_ignore.IsIgnored(name)) return null;

        var left = Describe(Path.Combine(_leftRoot, relativePath));
        var right = Describe(Path.Combine(_rightRoot, relativePath));
        if (left is null && right is null) return null;

        _count = 0;
        _progress = null;
        _jobs.Clear();
        var node = Build(null, name, relativePath, left, right, ct);
        TakeContents()?.RunToEnd(ct);
        node.Recalculate(); // the folders inside were updated by RunToEnd; this one has no parent to be reached through
        return node;
    }

    private ContentComparison? TakeContents()
    {
        if (_jobs.Count == 0) return null;
        var contents = new ContentComparison(_jobs.ToList(), Parallelism, _cache);
        _jobs.Clear();
        return contents;
    }

    // ----- Building the tree -----

    private FolderNode Build(FolderNode? parent, string name, string relativePath, FileSystemInfo? left, FileSystemInfo? right, CancellationToken ct)
    {
        Tick();

        bool leftIsFolder = left is DirectoryInfo, rightIsFolder = right is DirectoryInfo;
        if (left is not null && right is not null && leftIsFolder != rightIsFolder)
        {
            return new FolderNode(name, relativePath, isFolder: false, parent)
            {
                Left = SideOf(left),
                Right = SideOf(right),
                Status = FolderStatus.Error,
                Message = "A folder on one side and a file on the other."
            };
        }

        bool isFolder = (left ?? right) is DirectoryInfo;
        var node = new FolderNode(name, relativePath, isFolder, parent) { Left = SideOf(left), Right = SideOf(right) };

        if (!isFolder)
        {
            (node.Status, node.Message) = CompareFile(node, left as FileInfo, right as FileInfo);
            return node;
        }

        // Do not follow links to other folders: they can point back up and loop forever.
        bool isLink = IsLink(left) || IsLink(right);
        if (!isLink) FillChildren(node, left?.FullName, right?.FullName, ct);
        node.Recalculate();
        return node;
    }

    private void FillChildren(FolderNode node, string? leftDirectory, string? rightDirectory, CancellationToken ct)
    {
        var (left, leftError) = List(leftDirectory);
        var (right, rightError) = List(rightDirectory);

        string? error = leftError is not null ? $"Left side: {leftError}" : rightError is not null ? $"Right side: {rightError}" : null;
        if (error is not null)
        {
            node.Message = error;
            node.Status = FolderStatus.Error;
            return;
        }

        var names = left.Keys.Union(right.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => (left.GetValueOrDefault(n) is DirectoryInfo || right.GetValueOrDefault(n) is DirectoryInfo) ? 0 : 1)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var children = new FolderNode[names.Count];
        FolderNode BuildAt(int i)
        {
            left.TryGetValue(names[i], out var l);
            right.TryGetValue(names[i], out var r);
            string shownName = (l ?? r)!.Name;
            return Build(node, shownName, Path.Combine(node.RelativePath, shownName), l, r, ct);
        }

        // Sub-folders are listed in parallel (as many at once as files are read), files on this thread.
        // The children keep their sorted order either way.
        int folders = names.TakeWhile(n => left.GetValueOrDefault(n) is DirectoryInfo || right.GetValueOrDefault(n) is DirectoryInfo).Count();
        if (_listParallelism > 1 && folders > 1)
        {
            try
            {
                Parallel.For(0, folders, new ParallelOptions { MaxDegreeOfParallelism = _listParallelism, CancellationToken = ct },
                    i => children[i] = BuildAt(i));
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
            {
                throw new OperationCanceledException(ct);
            }
        }
        else
        {
            for (int i = 0; i < folders; i++)
            {
                ct.ThrowIfCancellationRequested();
                children[i] = BuildAt(i);
            }
        }

        for (int i = folders; i < names.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            children[i] = BuildAt(i);
        }
        node.Children.AddRange(children);
    }

    /// <summary>
    /// Lists a folder without the ignored names, by name. A null folder (the entry is missing on that side) lists nothing.
    /// A folder that cannot be read gives the reason as the error.
    /// </summary>
    private (Dictionary<string, FileSystemInfo> Entries, string? Error) List(string? directory)
    {
        var map = new Dictionary<string, FileSystemInfo>(StringComparer.OrdinalIgnoreCase);
        if (directory is null) return (map, null);

        try
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                if (!_ignore.IsIgnored(entry.Name)) map[entry.Name] = entry;
            return (map, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (map, ex.Message);
        }
    }

    // ----- Files -----

    /// <summary>Everything that can be told without reading the files. Same-size files in content mode become a job.</summary>
    private (FolderStatus Status, string? Message) CompareFile(FolderNode node, FileInfo? left, FileInfo? right)
    {
        if (left is null) return (FolderStatus.RightOnly, null);
        if (right is null) return (FolderStatus.LeftOnly, null);

        try
        {
            if (left.Length != right.Length) return (FolderStatus.Different, null);

            if (_options.Mode == FolderCompareMode.SizeAndTime)
            {
                bool closeInTime = (left.LastWriteTimeUtc - right.LastWriteTimeUtc).Duration() <= TimeTolerance;
                return (closeInTime ? FolderStatus.Same : FolderStatus.Different, null);
            }

            var job = new ContentJob(node, FileSnapshot.Of(left), FileSnapshot.Of(right));
            if (_cache.TryGet(job.CacheKey, out bool same)) return (same ? FolderStatus.Same : FolderStatus.Different, null);

            lock (_jobs) _jobs.Add(job);
            return (FolderStatus.Pending, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (FolderStatus.Error, ex.Message);
        }
    }

    // ----- Small helpers -----

    private void Tick()
    {
        int count = Interlocked.Increment(ref _count);
        if (count % 50 == 0) _progress?.Report(count);
    }

    private static bool IsLink(FileSystemInfo? info) =>
        info is not null && (info.Attributes & FileAttributes.ReparsePoint) != 0;

    private static FileSystemInfo? Describe(string path) =>
        Directory.Exists(path) ? new DirectoryInfo(path) : File.Exists(path) ? new FileInfo(path) : null;

    internal static SideInfo? SideOf(FileSystemInfo? info)
    {
        if (info is null) return null;
        try
        {
            return info is FileInfo file
                ? new SideInfo(file.Length, file.LastWriteTime)
                : new SideInfo(0, info.LastWriteTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SideInfo(0, DateTime.MinValue); // vanished while scanning
        }
    }
}
