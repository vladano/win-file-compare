using FileCompare.Core.Folders;

namespace FileCompare.Tests;

/// <summary>The two-phase folder scan (PLAN.md, phase 10): listing, parallel content compare, cache, cancellation.</summary>
public sealed class FolderScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileCompare.Tests", Guid.NewGuid().ToString("N"));
    private string Left => Path.Combine(_root, "left");
    private string Right => Path.Combine(_root, "right");

    public FolderScanTests()
    {
        Directory.CreateDirectory(Left);
        Directory.CreateDirectory(Right);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
            string parent = Path.GetDirectoryName(_root)!;
            if (!Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
        }
        catch (IOException) { /* still in use for a moment, or another test class is using the folder; it is in %TEMP% */ }
    }

    // ----- Helpers -----

    private static void Write(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private void Both(string relative, byte[] content)
    {
        Write(Path.Combine(Left, relative), content);
        Write(Path.Combine(Right, relative), content);
    }

    private static byte[] Bytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static byte[] With(byte[] content, int index)
    {
        var copy = (byte[])content.Clone();
        copy[index] ^= 0xFF;
        return copy;
    }

    private static FolderCompareOptions Options(int parallelism = 0) => new() { Mode = FolderCompareMode.Content, Parallelism = parallelism };

    /// <summary>The tree as text: one line per entry with its status and message, in tree order.</summary>
    private static List<string> Describe(FolderNode root)
    {
        var lines = new List<string>();
        void Walk(FolderNode node)
        {
            lines.Add($"{node.RelativePath}|{node.IsFolder}|{node.Status}|{node.Message}");
            foreach (var child in node.Children) Walk(child);
        }
        Walk(root);
        return lines;
    }

    private static IEnumerable<FolderNode> All(FolderNode root) => new[] { root }.Concat(root.Children.SelectMany(All));

    private FolderNode Node(FolderCompareSession session, string relative) =>
        session.Find(relative.Replace('/', Path.DirectorySeparatorChar)) ?? throw new InvalidOperationException(relative + " not found");

    /// <summary>Every folder has the status its children give it.</summary>
    private static void AssertFoldersConsistent(FolderNode root)
    {
        foreach (var folder in All(root).Where(n => n.IsFolder && n.Message is null && n.Left is not null && n.Right is not null))
        {
            var statuses = folder.Children.Select(c => c.Status).ToList();
            var expected = statuses.Any(s => s is not (FolderStatus.Same or FolderStatus.Pending)) ? FolderStatus.Different
                : statuses.Contains(FolderStatus.Pending) ? FolderStatus.Pending
                : FolderStatus.Same;
            Assert.True(expected == folder.Status, $"{folder.RelativePath}: {folder.Status}, expected {expected}");
        }
    }

    // ----- File content -----

    [Theory]
    [InlineData(0, -1)]          // empty
    [InlineData(10, -1)]         // small, equal
    [InlineData(10, 3)]          // small, different
    [InlineData(300_000, -1)]    // start, end and middle are read separately
    [InlineData(300_000, 5)]     // differs in the first 64 KB
    [InlineData(300_000, 299_990)] // differs in the last 64 KB
    [InlineData(300_000, 150_000)] // differs only in the middle
    [InlineData(3_000_000, 1_500_000)] // several 1 MB chunks
    [InlineData(3_000_000, -1)]
    public void FileContent_FindsEveryDifference(int length, int changedAt)
    {
        var content = Bytes(length, 1);
        Write(Path.Combine(Left, "a.bin"), content);
        Write(Path.Combine(Right, "a.bin"), changedAt < 0 ? content : With(content, changedAt));

        var counter = new ByteCounter();
        bool same = FileContent.AreEqual(Path.Combine(Left, "a.bin"), Path.Combine(Right, "a.bin"), default, counter);

        Assert.Equal(changedAt < 0, same);
        if (same) Assert.Equal(2L * length, counter.Value); // a "same" needs every byte of both files
    }

    [Fact]
    public void FileContent_DifferenceAtTheEnd_IsFoundWithoutReadingTheMiddle()
    {
        var content = Bytes(3_000_000, 2);
        Write(Path.Combine(Left, "a.bin"), content);
        Write(Path.Combine(Right, "a.bin"), With(content, content.Length - 1));

        var counter = new ByteCounter();
        Assert.False(FileContent.AreEqual(Path.Combine(Left, "a.bin"), Path.Combine(Right, "a.bin"), default, counter));
        Assert.Equal(4L * FileContent.EdgeSize, counter.Value); // the start and the end of both, nothing else
    }

    // ----- Two phases -----

    [Fact]
    public async Task Listing_KnowsSizesAtOnce_AndLeavesSameSizeFilesPending()
    {
        var content = Bytes(1000, 3);
        Both(@"sub\same.txt", content);
        Write(Path.Combine(Left, @"sub\changed.txt"), content);
        Write(Path.Combine(Right, @"sub\changed.txt"), With(content, 500));
        Write(Path.Combine(Left, @"other\longer.txt"), content);
        Write(Path.Combine(Right, @"other\longer.txt"), content.Concat(new byte[] { 1 }).ToArray());

        var session = await FolderCompareSession.ScanAsync(Left, Right, Options());

        Assert.Equal(FolderStatus.Pending, Node(session, "sub/same.txt").Status);
        Assert.Equal(FolderStatus.Pending, Node(session, "sub/changed.txt").Status);
        Assert.Equal(FolderStatus.Different, Node(session, "other/longer.txt").Status);
        Assert.Equal(FolderStatus.Pending, Node(session, "sub").Status);       // only pending files inside
        Assert.Equal(FolderStatus.Different, Node(session, "other").Status);   // already known to differ
        Assert.Equal(FolderStatus.Different, session.Root.Status);
        Assert.Equal(2, session.Summary.Pending);
        Assert.Equal(2, session.Contents!.Total);
        Assert.Equal(0, session.Contents.Done); // nothing read yet

        await session.CompareContentsAsync();
        Assert.Equal(FolderStatus.Pending, Node(session, "sub/same.txt").Status); // not applied until asked
        var changed = session.ApplyContentResults();

        Assert.Equal(2, changed.Count);
        Assert.Equal(FolderStatus.Same, Node(session, "sub/same.txt").Status);
        Assert.Equal(FolderStatus.Different, Node(session, "sub/changed.txt").Status);
        Assert.Equal(FolderStatus.Different, Node(session, "sub").Status);
        Assert.Equal(0, session.Summary.Pending);
        AssertFoldersConsistent(session.Root);
    }

    [Fact]
    public async Task SizeAndTimeMode_ReadsNothing()
    {
        Both("a.txt", Bytes(100, 4));
        var session = await FolderCompareSession.ScanAsync(Left, Right, new FolderCompareOptions { Mode = FolderCompareMode.SizeAndTime });

        Assert.Null(session.Contents);
        Assert.Equal(FolderStatus.Same, session.Root.Status);
    }

    // ----- Parallel gives the same result as sequential -----

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    public async Task ParallelScan_GivesExactlyTheSequentialTree(int seed)
    {
        var expectedSame = RandomTrees(seed);

        var sequential = await FolderCompareSession.OpenAsync(Left, Right, Options(parallelism: 1));
        var parallel = await FolderCompareSession.OpenAsync(Left, Right, Options(parallelism: 8));

        Assert.Equal(1, sequential.Parallelism);
        Assert.Equal(8, parallel.Parallelism);
        Assert.Equal(Describe(sequential.Root), Describe(parallel.Root));
        Assert.Equal(0, parallel.Summary.Pending);
        AssertFoldersConsistent(parallel.Root);

        // And both agree with reading the files directly.
        foreach (var (relative, same) in expectedSame)
            Assert.True((Node(parallel, relative).Status == FolderStatus.Same) == same, $"{relative}: {Node(parallel, relative).Status}");
    }

    /// <summary>
    /// Random trees: nested folders, files of random sizes (some larger than the 64 KB edges), and on the right
    /// copies, changed copies (same size and other size), missing files and extra files.
    /// Returns, for each file on both sides, whether the bytes are the same.
    /// </summary>
    private Dictionary<string, bool> RandomTrees(int seed)
    {
        var random = new Random(seed);
        var sameOnBothSides = new Dictionary<string, bool>();
        int files = random.Next(20, 60);
        for (int i = 0; i < files; i++)
        {
            string folder = string.Join('/', Enumerable.Range(0, random.Next(0, 4)).Select(_ => $"d{random.Next(0, 3)}"));
            string relative = (folder.Length > 0 ? folder + "/" : "") + $"f{i}.bin";
            int size = random.Next(0, 10) switch { 0 => 0, < 7 => random.Next(1, 5000), _ => random.Next(200_000, 400_000) };
            var content = Bytes(size, seed * 1000 + i);
            Write(Path.Combine(Left, relative), content);

            switch (random.Next(0, 6))
            {
                case 0 when size > 0: // same size, other bytes, anywhere in the file
                    Write(Path.Combine(Right, relative), With(content, random.Next(0, size)));
                    sameOnBothSides[relative] = false;
                    break;
                case 1: // other size
                    Write(Path.Combine(Right, relative), content.Concat(new byte[] { 7 }).ToArray());
                    sameOnBothSides[relative] = false;
                    break;
                case 2: // left only
                    break;
                default:
                    Write(Path.Combine(Right, relative), content);
                    sameOnBothSides[relative] = true;
                    break;
            }
        }
        for (int i = 0; i < 5; i++) Write(Path.Combine(Right, $"d{i % 3}", $"extra{i}.txt"), Bytes(10, i));
        return sameOnBothSides;
    }

    // ----- Cancellation -----

    [Fact]
    public async Task StoppedBeforeTheStart_EveryFileIsMarkedNotCompared()
    {
        for (int i = 0; i < 20; i++) Both($@"d{i % 3}\f{i}.bin", Bytes(1000, i));
        var session = await FolderCompareSession.ScanAsync(Left, Right, Options(4));

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.CompareContentsAsync(cancel.Token));
        session.MarkContentsStopped();

        var files = All(session.Root).Where(n => !n.IsFolder).ToList();
        Assert.All(files, f => Assert.Equal(FolderStatus.Pending, f.Status));
        Assert.All(files, f => Assert.Equal(ContentComparison.StoppedMessage, f.Message));
        Assert.Equal(20, session.Summary.Pending);
        AssertFoldersConsistent(session.Root);
    }

    [Fact]
    public async Task StoppedHalfWay_LeavesAConsistentTree()
    {
        var content = Bytes(2_000_000, 5);
        for (int i = 0; i < 40; i++) Both($@"d{i % 4}\f{i}.bin", i % 5 == 0 ? Bytes(2_000_000, 100 + i) : content);
        Write(Path.Combine(Right, @"d2\f6.bin"), With(content, 1_000_000)); // f6 is in d{6 % 4}

        var session = await FolderCompareSession.ScanAsync(Left, Right, Options(2));
        using var cancel = new CancellationTokenSource();
        var run = session.CompareContentsAsync(cancel.Token);
        while (session.Contents!.Done < 3 && !run.IsCompleted) await Task.Delay(1);
        cancel.Cancel();
        try { await run; } catch (OperationCanceledException) { }
        session.MarkContentsStopped();

        var files = All(session.Root).Where(n => !n.IsFolder).ToList();
        Assert.Equal(40, session.Summary.Total);
        Assert.All(files, f => Assert.True(
            f.Status is FolderStatus.Same or FolderStatus.Different || (f.Status == FolderStatus.Pending && f.Message == ContentComparison.StoppedMessage),
            $"{f.RelativePath}: {f.Status} {f.Message}"));
        Assert.Contains(files, f => f.Status != FolderStatus.Pending); // some results made it in
        AssertFoldersConsistent(session.Root);

        // Whatever was found is right.
        foreach (var file in files.Where(f => f.Status is FolderStatus.Same or FolderStatus.Different))
        {
            bool same = File.ReadAllBytes(Path.Combine(Left, file.RelativePath)).AsSpan()
                .SequenceEqual(File.ReadAllBytes(Path.Combine(Right, file.RelativePath)));
            Assert.Equal(same, file.Status == FolderStatus.Same);
        }
    }

    // ----- Files that change during the scan -----

    [Fact]
    public async Task FileChangedAfterTheListing_IsNeverSame()
    {
        var content = Bytes(5000, 6);
        Both("rewritten.bin", content);
        Both("touched.bin", content);
        Both("deleted.bin", content);
        Both("untouched.bin", content);

        var session = await FolderCompareSession.ScanAsync(Left, Right, Options(2));

        // Between the listing and the content compare:
        File.WriteAllBytes(Path.Combine(Right, "rewritten.bin"), With(content, 10));              // same size, new bytes
        File.SetLastWriteTimeUtc(Path.Combine(Left, "touched.bin"), DateTime.UtcNow.AddHours(1)); // same bytes, new time
        File.Delete(Path.Combine(Right, "deleted.bin"));

        await session.CompareContentsAsync();
        session.ApplyContentResults();

        Assert.Equal(FolderStatus.Different, Node(session, "rewritten.bin").Status);
        Assert.Equal(FolderStatus.Error, Node(session, "touched.bin").Status);
        Assert.Equal(ContentComparison.ChangedMessage, Node(session, "touched.bin").Message);
        Assert.Equal(FolderStatus.Error, Node(session, "deleted.bin").Status);
        Assert.Equal(FolderStatus.Same, Node(session, "untouched.bin").Status);
    }

    [Fact]
    public void FileContent_Compare_SaysChangedWhenTheTimeMoved()
    {
        var content = Bytes(1000, 7);
        Both("a.bin", content);
        var left = FileSnapshot.Of(new FileInfo(Path.Combine(Left, "a.bin")));
        var right = FileSnapshot.Of(new FileInfo(Path.Combine(Right, "a.bin")));

        Assert.Equal(FileContent.Outcome.Same, FileContent.Compare(left, right));
        File.SetLastWriteTimeUtc(right.FullPath, right.LastWriteTimeUtc.AddMinutes(5));
        Assert.Equal(FileContent.Outcome.Changed, FileContent.Compare(left, right));
    }

    // ----- Cache -----

    [Fact]
    public async Task Refresh_ReadsOnlyWhatChanged()
    {
        var content = Bytes(3000, 8);
        for (int i = 0; i < 10; i++) Both($@"d{i % 2}\f{i}.bin", content);

        var session = await FolderCompareSession.OpenAsync(Left, Right, Options(2));
        Assert.Equal(10, session.Cache.Count);
        Assert.Equal(0, session.Cache.Hits);

        await session.RefreshAsync(new[] { "" });
        Assert.Equal(10, session.Cache.Hits); // nothing was read again
        Assert.Equal(FolderStatus.Same, session.Root.Status);

        // One file changes (new bytes and a new time): only that one is read.
        string changed = Path.Combine("d1", "f3.bin");
        File.WriteAllBytes(Path.Combine(Right, changed), With(content, 1));
        File.SetLastWriteTimeUtc(Path.Combine(Right, changed), DateTime.UtcNow.AddMinutes(1));
        await session.RefreshAsync(new[] { "" });

        Assert.Equal(19, session.Cache.Hits);
        Assert.Equal(FolderStatus.Different, Node(session, "d1/f3.bin").Status);
        Assert.Equal(FolderStatus.Different, Node(session, "d1").Status);
        Assert.Equal(FolderStatus.Same, Node(session, "d0").Status);
    }

    // ----- Status rules -----

    [Fact]
    public void Folder_WithPendingAndDifferentChildren_IsDifferent_InAnyOrder()
    {
        foreach (var order in new[] { new[] { FolderStatus.Pending, FolderStatus.Different }, new[] { FolderStatus.Different, FolderStatus.Pending } })
        {
            var folder = new FolderNode("f", "f", isFolder: true, parent: null) { Left = new SideInfo(0, default), Right = new SideInfo(0, default) };
            foreach (var status in order) folder.Children.Add(new FolderNode("x", "f\\x", false, folder) { Status = status });
            folder.Recalculate();
            Assert.Equal(FolderStatus.Different, folder.Status);
        }
    }

    [Fact]
    public async Task PendingRows_ShowWithEitherFilter_AndPendingFoldersStayClosed()
    {
        Both(@"sub\a.txt", Bytes(100, 9));
        var session = await FolderCompareSession.ScanAsync(Left, Right, Options());
        var none = new HashSet<string>();
        var open = new HashSet<string> { "sub" };

        Assert.Single(FolderListBuilder.Build(session.Root, FolderFilter.Same, none));
        Assert.Single(FolderListBuilder.Build(session.Root, FolderFilter.Different, none));
        Assert.Equal(2, FolderListBuilder.Build(session.Root, FolderFilter.Different, open).Count);
        Assert.Empty(FolderListBuilder.Build(session.Root, FolderFilter.LeftOnly, none));
        Assert.Empty(FolderListBuilder.DefaultExpanded(session.Root));
    }

    [Fact]
    public async Task Update_CopiesNewerFilesThatWereNotCompared()
    {
        var content = Bytes(100, 10);
        Both("newer.txt", content);
        Both("older.txt", content);
        File.SetLastWriteTimeUtc(Path.Combine(Left, "newer.txt"), DateTime.UtcNow.AddHours(1));
        File.SetLastWriteTimeUtc(Path.Combine(Left, "older.txt"), DateTime.UtcNow.AddHours(-1));

        var session = await FolderCompareSession.ScanAsync(Left, Right, Options());
        session.MarkContentsStopped();
        var plan = SyncPlanner.PlanUpdate(session.Root, toRight: true);

        Assert.Equal(new[] { "newer.txt" }, plan.Items.Select(n => n.RelativePath));
        Assert.Empty(plan.Conflicts);
    }

    // ----- Disks -----

    [Fact]
    public void ParallelReads_FollowsTheUserOrTheDisk()
    {
        Assert.InRange(DiskInfo.ParallelReads(Left, Right), 1, DiskInfo.MaxParallelism);
        Assert.Equal(3, DiskInfo.ParallelReads(Left, Right, requested: 3));
        Assert.Equal(DiskInfo.MaxParallelism, DiskInfo.ParallelReads(Left, Right, requested: 100));
        Assert.Equal(DiskInfo.SsdParallelism, DiskInfo.ParallelReads(@"\\server\share\a", @"\\server\share\b"));
    }
}
