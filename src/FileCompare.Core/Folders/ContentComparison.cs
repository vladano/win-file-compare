using System.Collections.Concurrent;

namespace FileCompare.Core.Folders;

/// <summary>A file as it was when the folders were listed.</summary>
internal readonly record struct FileSnapshot(string FullPath, long Length, DateTime LastWriteTimeUtc)
{
    public static FileSnapshot Of(FileInfo file) => new(file.FullName, file.Length, file.LastWriteTimeUtc);
}

/// <summary>Two files of the same size whose content still has to be compared.</summary>
internal sealed record ContentJob(FolderNode Node, FileSnapshot Left, FileSnapshot Right)
{
    public ContentCache.Key CacheKey => new(Node.RelativePath.ToUpperInvariant(), Left.Length, Left.LastWriteTimeUtc.Ticks, Right.LastWriteTimeUtc.Ticks);
}

/// <summary>
/// Results of content compares, kept for the life of a folder compare session. A file pair whose sizes and times
/// have not changed since it was compared is not read again (for example after a copy or a refresh).
/// </summary>
public sealed class ContentCache
{
    internal readonly record struct Key(string RelativePath, long Length, long LeftTicks, long RightTicks);

    private readonly ConcurrentDictionary<Key, bool> _results = new();
    private int _hits;

    /// <summary>How many compares were answered from the cache.</summary>
    public int Hits => Volatile.Read(ref _hits);

    public int Count => _results.Count;

    internal bool TryGet(Key key, out bool same)
    {
        if (!_results.TryGetValue(key, out same)) return false;
        Interlocked.Increment(ref _hits);
        return true;
    }

    internal void Set(Key key, bool same) => _results[key] = same;
}

/// <summary>
/// The second phase of a content scan: reads the files of the same size, several at a time, and works out whether
/// they are the same. The tree is only changed by <see cref="ApplyResults"/>, on the caller's thread, so a UI can show
/// the tree while the files are read and apply the results in batches.
/// </summary>
public sealed class ContentComparison
{
    internal const string ChangedMessage = "The file changed while it was compared. Press F5 to compare again.";
    internal const string StoppedMessage = "Not compared: the comparison was stopped.";

    private readonly IReadOnlyList<ContentJob> _jobs;
    private readonly ContentCache _cache;
    private readonly ConcurrentQueue<(ContentJob Job, FolderStatus Status, string? Message)> _results = new();
    private readonly ByteCounter _bytesRead = new();
    private int _done;

    internal ContentComparison(IReadOnlyList<ContentJob> jobs, int parallelism, ContentCache cache)
    {
        _jobs = jobs;
        _cache = cache;
        Parallelism = Math.Max(1, parallelism);
        BytesTotal = jobs.Sum(j => j.Left.Length * 2);
    }

    /// <summary>How many files are read at the same time.</summary>
    public int Parallelism { get; }

    /// <summary>Number of file pairs to compare.</summary>
    public int Total => _jobs.Count;

    /// <summary>File pairs compared so far (their results may not be applied yet).</summary>
    public int Done => Volatile.Read(ref _done);

    /// <summary>Bytes to read from both sides together.</summary>
    public long BytesTotal { get; }

    /// <summary>Bytes read so far. Less than <see cref="BytesTotal"/> at the end when files differ early.</summary>
    public long BytesRead => _bytesRead.Value;

    /// <summary>Compares the files on worker threads. Results wait in a queue for <see cref="ApplyResults"/>.</summary>
    /// <exception cref="OperationCanceledException">The token was cancelled. Results found so far can still be applied.</exception>
    public Task RunAsync(CancellationToken ct = default) =>
        Parallel.ForEachAsync(_jobs, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
            (job, token) =>
            {
                var (status, message) = Compare(job, token);
                _results.Enqueue((job, status, message));
                Interlocked.Increment(ref _done);
                return ValueTask.CompletedTask;
            });

    /// <summary>Compares on the calling thread's behalf and applies everything (for refreshes and tests).</summary>
    internal void RunToEnd(CancellationToken ct)
    {
        RunAsync(ct).GetAwaiter().GetResult();
        ApplyResults();
    }

    /// <summary>
    /// Puts the results that arrived since the last call into the tree, and updates the folders above them.
    /// Call it from the thread that owns the tree. Returns the files whose status changed.
    /// </summary>
    public IReadOnlyList<FolderNode> ApplyResults()
    {
        var changed = new List<FolderNode>();
        while (_results.TryDequeue(out var result))
        {
            result.Job.Node.Status = result.Status;
            result.Job.Node.Message = result.Message;
            changed.Add(result.Job.Node);
        }
        RecalculateFolders(changed);
        return changed;
    }

    /// <summary>
    /// After a stop: applies what was found, and marks the files that were not compared, so the tree says what is
    /// known. They stay <see cref="FolderStatus.Pending"/> with a message.
    /// </summary>
    public IReadOnlyList<FolderNode> MarkStopped()
    {
        var changed = new List<FolderNode>(ApplyResults());
        foreach (var job in _jobs)
        {
            if (job.Node.Status != FolderStatus.Pending || job.Node.Message is not null) continue;
            job.Node.Message = StoppedMessage;
            changed.Add(job.Node);
        }
        return changed;
    }

    /// <summary>Every folder above the changed files, deepest first, so each one sees its children's new status.</summary>
    internal static void RecalculateFolders(IEnumerable<FolderNode> changed)
    {
        var folders = new HashSet<FolderNode>();
        foreach (var node in changed)
            for (var folder = node.Parent; folder is not null && folders.Add(folder); folder = folder.Parent) { }

        foreach (var folder in folders.OrderByDescending(f => f.Depth)) folder.Recalculate();
    }

    private (FolderStatus, string?) Compare(ContentJob job, CancellationToken ct)
    {
        var key = job.CacheKey;
        if (_cache.TryGet(key, out bool cached)) return (cached ? FolderStatus.Same : FolderStatus.Different, null);

        try
        {
            // A file written to during the read can look the same by chance, so "same" is only trusted for files that
            // still look exactly as they did when they were listed.
            var outcome = FileContent.Compare(job.Left, job.Right, ct, _bytesRead);
            if (outcome == FileContent.Outcome.Changed) return (FolderStatus.Error, ChangedMessage);

            bool same = outcome == FileContent.Outcome.Same;
            _cache.Set(key, same);
            return (same ? FolderStatus.Same : FolderStatus.Different, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (FolderStatus.Error, ex.Message);
        }
    }
}
