using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCompare.Core.Settings;

namespace FileCompare.Core.Folders;

/// <summary>
/// The state of a folder pair after its last successful two-way sync. Without it, a file on one side only could be
/// new there or deleted on the other side; with it, the sync can tell which.
/// </summary>
public sealed class SyncBaseline
{
    /// <summary>Files up to this size also get a content hash, so a file that was only touched does not count as changed.</summary>
    public const long HashLimit = 256 * 1024;

    /// <summary>One file or folder as it was on both sides (it was the same on both).</summary>
    public sealed record Entry(string Path, bool IsFolder, long Size, DateTime LeftTimeUtc, DateTime RightTimeUtc, string? Hash);

    private readonly Dictionary<string, Entry> _entries;

    public SyncBaseline(string leftRoot, string rightRoot, DateTime writtenUtc, IEnumerable<Entry> entries)
    {
        LeftRoot = leftRoot;
        RightRoot = rightRoot;
        WrittenUtc = writtenUtc;
        _entries = entries.ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);
    }

    public string LeftRoot { get; }
    public string RightRoot { get; }
    public DateTime WrittenUtc { get; }
    public IReadOnlyCollection<Entry> Entries => _entries.Values;

    public Entry? Find(string relativePath) => _entries.GetValueOrDefault(relativePath);

    /// <summary>Whether a side changed since the baseline. A missing side or another size is a change.</summary>
    /// <param name="fullPath">The file on that side, read to compare the hash when only the time moved.</param>
    internal bool Changed(Entry entry, SideInfo? now, bool left, string fullPath)
    {
        if (now is null) return true;
        if (entry.IsFolder) return false; // a folder's own time changes with its content; its files tell
        if (now.Value.Size != entry.Size) return true;

        var then = left ? entry.LeftTimeUtc : entry.RightTimeUtc;
        if ((now.Value.LastWriteTime.ToUniversalTime() - then).Duration() <= FolderComparer.TimeTolerance) return false;

        // Only the time moved: for small files the hash tells whether the content changed too.
        if (entry.Hash is null) return true;
        try { return HashOf(fullPath) != entry.Hash; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    /// <summary>The baseline from a tree right after a sync: every entry that is the same on both sides.</summary>
    public static SyncBaseline FromTree(FolderNode root, string leftRoot, string rightRoot)
    {
        var entries = new List<Entry>();
        void Walk(FolderNode node)
        {
            foreach (var child in node.Children)
            {
                if (child.Left is not { } l || child.Right is not { } r) continue;
                if (child.IsFolder)
                {
                    if (child.Message is not null) continue;
                    entries.Add(new Entry(child.RelativePath, true, 0, default, default, null));
                    Walk(child);
                }
                else if (child.Status == FolderStatus.Same)
                {
                    string? hash = null;
                    if (l.Size <= HashLimit)
                    {
                        try { hash = HashOf(Path.Combine(leftRoot, child.RelativePath)); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* no hash: a time change then counts */ }
                    }
                    entries.Add(new Entry(child.RelativePath, false, l.Size, l.LastWriteTime.ToUniversalTime(), r.LastWriteTime.ToUniversalTime(), hash));
                }
            }
        }
        Walk(root);
        return new SyncBaseline(leftRoot, rightRoot, DateTime.UtcNow, entries);
    }

    internal static string HashOf(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>The same baseline seen from the other order of the folders.</summary>
    internal SyncBaseline Swapped() =>
        new(RightRoot, LeftRoot, WrittenUtc, _entries.Values.Select(e => e with { LeftTimeUtc = e.RightTimeUtc, RightTimeUtc = e.LeftTimeUtc }));
}

/// <summary>
/// Keeps baselines in files, one per folder pair: %AppData%\FileCompare\sync\&lt;hash of both folders&gt;.json.
/// The pair is the same in either order.
/// </summary>
public sealed class SyncBaselineStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public SyncBaselineStore(string? folder = null) => Folder = folder ?? Path.Combine(AppDataPaths.Root, "sync");

    public string Folder { get; }

    public string FileFor(string leftRoot, string rightRoot)
    {
        var roots = new[] { Normalize(leftRoot), Normalize(rightRoot) }.Order(StringComparer.Ordinal);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", roots)));
        return Path.Combine(Folder, Convert.ToHexString(hash, 0, 12).ToLowerInvariant() + ".json");
    }

    /// <summary>The baseline for the pair, with Left and Right as given. Null when the pair was never synced (or the file is unreadable).</summary>
    public SyncBaseline? Load(string leftRoot, string rightRoot)
    {
        string file = FileFor(leftRoot, rightRoot);
        if (!File.Exists(file)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(file), Json);
            if (data?.Entries is null || data.LeftRoot is null || data.RightRoot is null) return null;
            var baseline = new SyncBaseline(data.LeftRoot, data.RightRoot, data.WrittenUtc, data.Entries);
            return Normalize(data.LeftRoot) == Normalize(leftRoot) ? baseline : baseline.Swapped();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // treated like a first sync, which never deletes
        }
    }

    /// <summary>Writes the baseline (through a temporary file, so a crash leaves the old one).</summary>
    public void Save(SyncBaseline baseline)
    {
        Directory.CreateDirectory(Folder);
        string file = FileFor(baseline.LeftRoot, baseline.RightRoot);
        string temp = file + ".tmp";
        var data = new Data(baseline.LeftRoot, baseline.RightRoot, baseline.WrittenUtc, baseline.Entries.ToList());
        File.WriteAllText(temp, JsonSerializer.Serialize(data, Json));
        File.Move(temp, file, overwrite: true);
    }

    public void Delete(string leftRoot, string rightRoot) => File.Delete(FileFor(leftRoot, rightRoot));

    private static string Normalize(string root) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant();

    private sealed record Data(string? LeftRoot, string? RightRoot, DateTime WrittenUtc, List<SyncBaseline.Entry>? Entries);
}
