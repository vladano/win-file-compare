using FileCompare.Core.Folders;

namespace FileCompare.Tests;

/// <summary>Update, Mirror and two-way Sync (PLAN.md, phase 11), on temporary folders.</summary>
public sealed class SyncTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileCompare.Tests", Guid.NewGuid().ToString("N"));
    private string Left => Path.Combine(_root, "left");
    private string Right => Path.Combine(_root, "right");
    private readonly SyncBaselineStore _baselines;

    public SyncTests()
    {
        Directory.CreateDirectory(Left);
        Directory.CreateDirectory(Right);
        _baselines = new SyncBaselineStore(Path.Combine(_root, "sync"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
            string parent = Path.GetDirectoryName(_root)!;
            if (!Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
        }
        catch (IOException) { /* in use for a moment, or another test class uses the folder; it is in %TEMP% */ }
    }

    // ----- Helpers -----

    private string L(string relative) => Path.Combine(Left, relative);
    private string R(string relative) => Path.Combine(Right, relative);

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>The same file on both sides, with the same time (as after a copy).</summary>
    private void Both(string relative, string text)
    {
        Write(L(relative), text);
        Directory.CreateDirectory(Path.GetDirectoryName(R(relative))!);
        File.Copy(L(relative), R(relative), overwrite: true);
    }

    /// <summary>A real edit: other content, another size, and a time clearly later (beyond the 2 s tolerance).</summary>
    private static void Change(string path, string text = "changed content")
    {
        File.WriteAllText(path, text + Guid.NewGuid());
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(1));
    }

    /// <summary>Only the time moves; the content stays.</summary>
    private static void Touch(string path) => File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(2));

    private Task<FolderCompareSession> Scan() =>
        FolderCompareSession.OpenAsync(Left, Right, new FolderCompareOptions { Mode = FolderCompareMode.Content, Parallelism = 2 });

    private Task<OperationResult> Run(FolderCompareSession session, SyncPlan plan, CancellationToken ct = default) =>
        session.RunPlanAsync(plan, _baselines, ct, recycle: false);

    /// <summary>Syncs once with no baseline, so the pair has one; returns nothing to check.</summary>
    private async Task FirstSync()
    {
        var session = await Scan();
        var plan = SyncPlanner.PlanTwoWay(session.Root, Left, Right, null);
        Assert.True(plan.IsFirstSync);
        var result = await Run(session, plan);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors));
        Assert.NotNull(_baselines.Load(Left, Right));
    }

    private async Task<SyncPlan> PlanTwoWay()
    {
        var session = await Scan();
        return SyncPlanner.PlanTwoWay(session.Root, Left, Right, _baselines.Load(Left, Right));
    }

    private static string Describe(SyncPlan plan) =>
        string.Join(", ", plan.Items.OrderBy(i => i.RelativePath, StringComparer.OrdinalIgnoreCase).Select(i => $"{i.Action} {i.RelativePath}"));

    private static string P(string relative) => relative.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>Every file and folder below a root, with the file contents.</summary>
    private static List<string> Snapshot(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p) + (File.Exists(p) ? "=" + File.ReadAllText(p) : "\\"))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // ----- Update -----

    [Fact]
    public async Task Update_CopiesNewAndNewer_NeverDeletes_AndOffersOlderAsAConflict()
    {
        Both("same.txt", "same");
        Write(L("new.txt"), "new");
        Write(L(@"newdir\a.txt"), "a");
        Both("newer.txt", "x");
        Change(L("newer.txt"));
        Both("older.txt", "y");
        Change(R("older.txt"));
        Write(R("rightonly.txt"), "keep me");

        var session = await Scan();
        var plan = SyncPlanner.PlanUpdate(session.Root, toRight: true);

        Assert.Equal("Conflict older.txt, CopyToRight new.txt, CopyToRight newdir, CopyToRight newer.txt",
            string.Join(", ", plan.Items.OrderBy(i => i.Action != SyncAction.Conflict).ThenBy(i => i.RelativePath).Select(i => $"{i.Action} {i.RelativePath}")));
        Assert.Equal(new[] { ConflictChoice.Skip, ConflictChoice.KeepLeft }, plan.Conflicts.Single().Choices);
        Assert.Equal(0, plan.FilesToDeleteRight);

        await Run(session, plan);
        Assert.Equal("new", File.ReadAllText(R("new.txt")));
        Assert.Equal("a", File.ReadAllText(R(@"newdir\a.txt")));
        Assert.Equal(File.ReadAllText(L("newer.txt")), File.ReadAllText(R("newer.txt")));
        Assert.NotEqual(File.ReadAllText(L("older.txt")), File.ReadAllText(R("older.txt"))); // skipped
        Assert.True(File.Exists(R("rightonly.txt")));
    }

    // ----- Mirror -----

    private void MirrorTree()
    {
        Both("same.txt", "same");
        Both(@"sub\same.txt", "same");
        Write(L("leftonly.txt"), "l");
        Write(L(@"leftdir\deep\a.txt"), "a");
        Write(R("rightonly.txt"), "r");
        Write(R(@"rightdir\deep\b.txt"), "b");
        Write(R(@"sub\extra.txt"), "extra");
        Both(@"sub\changed.txt", "one");
        Change(R(@"sub\changed.txt")); // newer on the target: mirror still copies the source
        Directory.CreateDirectory(L("emptydir"));
    }

    [Fact]
    public async Task Mirror_MakesTheTargetAnExactCopy()
    {
        MirrorTree();
        var session = await Scan();
        var plan = SyncPlanner.PlanMirror(session.Root, Left, Right, toRight: true);

        Assert.Equal(P("CopyToRight emptydir, CopyToRight leftdir, CopyToRight leftonly.txt, DeleteRight rightdir, DeleteRight rightonly.txt, CopyToRight sub/changed.txt, DeleteRight sub/extra.txt"),
            Describe(plan));
        Assert.Equal(3, plan.FilesToDeleteRight); // rightonly.txt, rightdir\deep\b.txt, sub\extra.txt
        Assert.Equal(0, plan.FilesToDeleteLeft);

        var before = Snapshot(Left);
        var result = await Run(session, plan);

        Assert.False(result.HasErrors, string.Join("; ", result.Errors));
        Assert.Equal(before, Snapshot(Left));   // the source is not touched
        Assert.Equal(before, Snapshot(Right));  // and the target is the same
        Assert.Equal(FolderStatus.Same, session.Root.Status);
    }

    [Fact]
    public async Task Mirror_ToTheLeft_Works_TheOtherWay()
    {
        MirrorTree();
        var session = await Scan();
        var plan = SyncPlanner.PlanMirror(session.Root, Left, Right, toRight: false);
        await Run(session, plan);
        Assert.Equal(Snapshot(Right), Snapshot(Left));
    }

    [Fact]
    public async Task Mirror_DeletesOnlyWhatThePlanListed()
    {
        MirrorTree();
        var session = await Scan();
        var plan = SyncPlanner.PlanMirror(session.Root, Left, Right, toRight: true);
        plan.Items.Single(i => i.RelativePath == "rightonly.txt").Included = false;
        plan.Items.Single(i => i.RelativePath == "leftonly.txt").Included = false;

        await Run(session, plan);

        Assert.True(File.Exists(R("rightonly.txt")));    // left out
        Assert.False(File.Exists(R("leftonly.txt")));    // left out
        Assert.False(Directory.Exists(R("rightdir")));   // listed, so deleted
        Assert.False(File.Exists(R(@"sub\extra.txt")));
        Assert.True(File.Exists(R(@"sub\same.txt")));     // never listed, never touched
        Assert.True(File.Exists(R("same.txt")));
    }

    [Theory]
    [InlineData(false)] // empty
    [InlineData(true)]  // missing
    public async Task Mirror_FromAMissingOrEmptySource_IsRefused(bool missing)
    {
        Write(R("precious.txt"), "do not delete");
        var session = await Scan();
        if (missing) Directory.Delete(Left);

        var plan = SyncPlanner.PlanMirror(session.Root, Left, Right, toRight: true);

        Assert.NotNull(plan.Refusal);
        Assert.Empty(plan.Items);
        Assert.Throws<InvalidOperationException>(() => FolderOperations.Execute(plan, Left, Right, recycle: false));
        Assert.True(File.Exists(R("precious.txt")));
    }

    [Fact]
    public async Task FileOnOneSide_FolderOnTheOther_IsAConflictThatIsSkipped()
    {
        Both("keep.txt", "k");
        Write(L("thing"), "a file");
        Write(R(@"thing\inside.txt"), "a folder");

        var session = await Scan();
        foreach (var plan in new[]
                 {
                     SyncPlanner.PlanMirror(session.Root, Left, Right, toRight: true),
                     SyncPlanner.PlanUpdate(session.Root, toRight: true),
                     SyncPlanner.PlanTwoWay(session.Root, Left, Right, null)
                 })
        {
            var item = Assert.Single(plan.Items);
            Assert.Equal(SyncAction.Conflict, item.Action);
            Assert.Equal(new[] { ConflictChoice.Skip }, item.Choices);
            Assert.True(plan.IsEmpty);
        }
    }

    // ----- Two-way: the rules table -----

    public static TheoryData<string, Action<SyncTests>, string> Rules => new()
    {
        { "nothing changed", _ => { }, "" },
        { "changed on the left", t => Change(t.L("a.txt")), "CopyToRight a.txt" },
        { "changed on the right", t => Change(t.R("a.txt")), "CopyToLeft a.txt" },
        { "new on the left", t => Write(t.L("new.txt"), "n"), "CopyToRight new.txt" },
        { "new on the right", t => Write(t.R("new.txt"), "n"), "CopyToLeft new.txt" },
        { "new folder on the right", t => Write(t.R(@"newdir\x.txt"), "x"), "CopyToLeft newdir" },
        { "deleted on the left, unchanged on the right", t => File.Delete(t.L("a.txt")), "DeleteRight a.txt" },
        { "deleted on the right, unchanged on the left", t => File.Delete(t.R("a.txt")), "DeleteLeft a.txt" },
        { "changed on both sides", t => { Change(t.L("a.txt"), "left"); Change(t.R("a.txt"), "right"); }, "Conflict a.txt" },
        { "deleted on the left, changed on the right", t => { File.Delete(t.L("a.txt")); Change(t.R("a.txt")); }, "Conflict a.txt" },
        { "deleted on the right, changed on the left", t => { File.Delete(t.R("a.txt")); Change(t.L("a.txt")); }, "Conflict a.txt" },
        { "new on both sides and different", t => { Write(t.L("new.txt"), "one"); Write(t.R("new.txt"), "other"); }, "Conflict new.txt" },
        { "new on both sides and the same", t => { Write(t.L("new.txt"), "same"); Write(t.R("new.txt"), "same"); }, "" },
        { "touched on the left, changed on the right", t => { Touch(t.L("a.txt")); Change(t.R("a.txt")); }, "CopyToLeft a.txt" },
        { "folder deleted on the left, unchanged on the right", t => Directory.Delete(t.L("dir"), true), "DeleteRight dir" },
        { "folder deleted on the left, file added in it on the right", t => { Directory.Delete(t.L("dir"), true); Write(t.R(@"dir\added.txt"), "+"); }, "Conflict dir" },
        { "folder deleted on the left, file changed in it on the right", t => { Directory.Delete(t.L("dir"), true); Change(t.R(@"dir\b.txt")); }, "Conflict dir" },
        { "file in a folder changed on the left", t => Change(t.L(@"dir\b.txt")), P("CopyToRight dir/b.txt") },
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public async Task TwoWay_FollowsTheRule(string rule, Action<SyncTests> change, string expected)
    {
        Both("a.txt", "a");
        Both(@"dir\b.txt", "b");
        Both("other.txt", "o");
        await FirstSync();

        change(this);
        var plan = await PlanTwoWay();

        Assert.False(plan.IsFirstSync);
        Assert.True(expected == Describe(plan), $"{rule}: expected \"{expected}\", got \"{Describe(plan)}\"");

        // Running it (conflicts are skipped) leaves the two sides the same, except for the conflicts.
        var session = await Scan();
        plan = SyncPlanner.PlanTwoWay(session.Root, Left, Right, _baselines.Load(Left, Right));
        await Run(session, plan);
        bool conflict = plan.Conflicts.Any();
        Assert.True(conflict || session.Root.Status == FolderStatus.Same, $"{rule}: the sides differ after the sync");
    }

    [Fact]
    public async Task FirstSync_NeverDeletes_AndTheNewerSideWins()
    {
        Write(L("leftonly.txt"), "l");
        Write(R("rightonly.txt"), "r");
        Both("newer-left.txt", "x");
        Change(L("newer-left.txt"));
        Both("newer-right.txt", "y");
        Change(R("newer-right.txt"));
        Both("tie.txt", "z");
        File.WriteAllText(R("tie.txt"), "Z!"); // different, same time (within 2 s)
        File.SetLastWriteTimeUtc(R("tie.txt"), File.GetLastWriteTimeUtc(L("tie.txt")));

        var plan = await PlanTwoWay();

        Assert.True(plan.IsFirstSync);
        Assert.Equal("CopyToRight leftonly.txt, CopyToRight newer-left.txt, CopyToLeft newer-right.txt, CopyToLeft rightonly.txt, Conflict tie.txt",
            Describe(plan));
        Assert.Equal(0, plan.FilesToDeleteLeft + plan.FilesToDeleteRight);
    }

    [Fact]
    public async Task TwoWay_WithAnEmptySide_AndABaseline_IsRefused()
    {
        Both("a.txt", "a");
        await FirstSync();
        File.Delete(R("a.txt")); // the right folder is now empty (for example a drive that was wiped)

        var plan = await PlanTwoWay();

        Assert.NotNull(plan.Refusal);
        Assert.True(File.Exists(L("a.txt")));
    }

    // ----- Conflict choices -----

    [Fact]
    public async Task Conflict_KeepLeft_CopiesTheLeftOver_KeepRight_RestoresADeletedFile()
    {
        Both("both.txt", "start");
        Both("deleted.txt", "start");
        await FirstSync();
        Change(L("both.txt"), "left wins");
        Change(R("both.txt"), "right loses");
        File.Delete(L("deleted.txt"));
        Change(R("deleted.txt"), "right edited");

        var session = await Scan();
        var plan = SyncPlanner.PlanTwoWay(session.Root, Left, Right, _baselines.Load(Left, Right));
        Assert.Equal(2, plan.Conflicts.Count());
        Assert.True(plan.IsEmpty); // conflicts are skipped until the user picks

        plan.Items.Single(i => i.RelativePath == "both.txt").Choice = ConflictChoice.KeepLeft;
        plan.Items.Single(i => i.RelativePath == "deleted.txt").Choice = ConflictChoice.KeepRight;
        Assert.Equal(SyncAction.CopyToRight, plan.Items.Single(i => i.RelativePath == "both.txt").Effective);
        Assert.Equal(SyncAction.CopyToLeft, plan.Items.Single(i => i.RelativePath == "deleted.txt").Effective);
        await Run(session, plan);

        Assert.StartsWith("left wins", File.ReadAllText(R("both.txt")));
        Assert.StartsWith("right edited", File.ReadAllText(L("deleted.txt")));
        Assert.Equal(FolderStatus.Same, session.Root.Status);
    }

    [Fact]
    public async Task Conflict_KeepLeft_WhenTheLeftWasDeleted_DeletesTheRight()
    {
        Both("deleted.txt", "start");
        Both("other.txt", "o");
        await FirstSync();
        File.Delete(L("deleted.txt"));
        Change(R("deleted.txt"));

        var session = await Scan();
        var plan = SyncPlanner.PlanTwoWay(session.Root, Left, Right, _baselines.Load(Left, Right));
        plan.Items.Single().Choice = ConflictChoice.KeepLeft;

        Assert.Equal(SyncAction.DeleteRight, plan.Items.Single().Effective);
        await Run(session, plan);
        Assert.False(File.Exists(R("deleted.txt")));
    }

    // ----- Baseline -----

    [Fact]
    public async Task Baseline_IsWrittenOnlyAfterASuccessfulSync()
    {
        Both("a.txt", "a");
        Write(L("b.txt"), "b");
        Both("locked.txt", "old");
        Change(R("locked.txt"), "new"); // newer on the right: copied to the left

        // A copy that fails: the target file is open in "another program" that allows no sharing.
        var session = await Scan();
        var plan = SyncPlanner.PlanTwoWay(session.Root, Left, Right, null);
        Assert.Contains(plan.Items, i => i.RelativePath == "locked.txt" && i.Action == SyncAction.CopyToLeft);
        OperationResult result;
        using (new FileStream(L("locked.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            result = await Run(session, plan);

        Assert.True(result.HasErrors);
        Assert.Null(_baselines.Load(Left, Right));

        // Stopped before the end: no baseline either.
        session = await Scan();
        plan = SyncPlanner.PlanTwoWay(session.Root, Left, Right, null);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(session, plan, cancel.Token));
        Assert.Null(_baselines.Load(Left, Right));

        // A sync that succeeds writes it, with every file that is now the same on both sides.
        session = await Scan();
        plan = SyncPlanner.PlanTwoWay(session.Root, Left, Right, null);
        result = await Run(session, plan);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors));
        var baseline = _baselines.Load(Left, Right);
        Assert.NotNull(baseline);
        Assert.Equal(new[] { "a.txt", "b.txt", "locked.txt" }, baseline!.Entries.Select(e => e.Path).Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(baseline.Entries, e => Assert.NotNull(e.Hash)); // small files get a hash
    }

    [Fact]
    public void BaselineStore_IsTheSameForBothOrders_AndSwapsTheSides()
    {
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddMinutes(5);
        _baselines.Save(new SyncBaseline(Left, Right, DateTime.UtcNow, new[] { new SyncBaseline.Entry("a.txt", false, 3, t1, t2, "AB") }));

        Assert.Equal(_baselines.FileFor(Left, Right), _baselines.FileFor(Right + Path.DirectorySeparatorChar, Left.ToUpperInvariant()));
        var swapped = _baselines.Load(Right, Left)!;
        Assert.Equal(t2, swapped.Find("A.TXT")!.LeftTimeUtc);
        Assert.Equal(t1, swapped.Find("a.txt")!.RightTimeUtc);

        File.WriteAllText(_baselines.FileFor(Left, Right), "{ not json");
        Assert.Null(_baselines.Load(Left, Right)); // unreadable: like a first sync, which never deletes
    }

    // ----- Delete warning -----

    [Fact]
    public async Task ManyDeletes_OrMoreThanHalf_AskAgain()
    {
        for (int i = 0; i < 4; i++) Both($"keep{i}.txt", "k");
        Write(R("extra.txt"), "e");
        var session = await Scan();
        Assert.Null(SyncPlanner.PlanMirror(session.Root, Left, Right, toRight: true).DeleteWarning); // 1 of 5

        for (int i = 0; i < 5; i++) Write(R($"more{i}.txt"), "m");
        session = await Scan();
        Assert.Contains("6 of the 10 files on the right side", SyncPlanner.PlanMirror(session.Root, Left, Right, toRight: true).DeleteWarning);

        for (int i = 0; i < 60; i++) Both($"many{i}.txt", "x");
        for (int i = 0; i < 51; i++) Write(R($@"gone\f{i}.txt"), "g");
        session = await Scan();
        Assert.NotNull(SyncPlanner.PlanMirror(session.Root, Left, Right, toRight: true).DeleteWarning); // 57 of 121
    }
}
