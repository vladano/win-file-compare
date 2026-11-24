using System.Diagnostics;
using FileCompare.Core.Folders;

// Times folder compare scans (PLAN.md, phase 10).
//
//   dotnet run -c Release --project tools/FileCompare.Benchmark -- [options]
//
//   --root <folder>     where the generated test tree goes (default %TEMP%\FileCompare.Benchmark)
//   --left <folder>     compare two real folders instead of the generated tree
//   --right <folder>
//   --profile <name>    generated tree: "full" (about 5,000 files, 1.3 GB per side, like the measurement in PLAN.md)
//                       or "small" (500 files, 60 MB per side). Default: full
//   --regenerate        build the generated tree again
//   --runs <n>          measured runs after one warm-up run (default 3)
//   --parallel <n>      parallel content reads (0 = automatic, the default)
//   --delete            remove the generated tree at the end

var options = BenchmarkOptions.Parse(args);
if (options is null) return 1;

string left, right;
if (options.Left is not null && options.Right is not null)
{
    (left, right) = (options.Left, options.Right);
}
else
{
    (left, right) = TestTree.Ensure(options.Root, options.Profile, options.Regenerate);
}

Console.WriteLine($"Left:  {left}");
Console.WriteLine($"Right: {right}");
Console.WriteLine($"CPUs: {Environment.ProcessorCount}, memory: {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024 * 1024.0):0.0} GB");
Console.WriteLine();

foreach (var mode in new[] { FolderCompareMode.SizeAndTime, FolderCompareMode.Content })
{
    var compare = new FolderCompareOptions { Mode = mode, Parallelism = options.Parallel };
    Scan.Run(left, right, compare); // warm-up: fills the disk cache, so the runs below are "warm"

    var times = new List<Scan.Result>();
    for (int i = 0; i < options.Runs; i++) times.Add(Scan.Run(left, right, compare));

    var median = times.OrderBy(t => t.Total).ElementAt(times.Count / 2);
    Console.WriteLine($"{mode,-12} median of {options.Runs}: total {median.Total.TotalSeconds,6:0.00} s, " +
                      $"first results {median.FirstResults.TotalSeconds,6:0.00} s, " +
                      $"parallel reads {median.Parallelism}   {median.Summary}");
}

if (options.Delete && options.Left is null)
{
    Directory.Delete(options.Root, recursive: true);
    string parent = Path.GetDirectoryName(options.Root)!;
    if (!Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
}
return 0;

internal sealed record BenchmarkOptions(string Root, string? Left, string? Right, string Profile, bool Regenerate, int Runs, int Parallel, bool Delete)
{
    public static BenchmarkOptions? Parse(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "FileCompare.Benchmark");
        string? left = null, right = null;
        string profile = "full";
        bool regenerate = false, delete = false;
        int runs = 3, parallel = 0;

        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--root": root = Path.GetFullPath(Next()); break;
                case "--left": left = Path.GetFullPath(Next()); break;
                case "--right": right = Path.GetFullPath(Next()); break;
                case "--profile": profile = Next(); break;
                case "--regenerate": regenerate = true; break;
                case "--runs": runs = int.Parse(Next()); break;
                case "--parallel": parallel = int.Parse(Next()); break;
                case "--delete": delete = true; break;
                default:
                    Console.Error.WriteLine($"Unknown option {args[i]}. See the comment at the top of Program.cs.");
                    return null;
            }
        }
        if ((left is null) != (right is null)) { Console.Error.WriteLine("Give both --left and --right."); return null; }
        if (profile is not ("full" or "small")) { Console.Error.WriteLine("--profile is full or small."); return null; }
        return new BenchmarkOptions(Path.Combine(root, profile), left, right, profile, regenerate, Math.Max(1, runs), parallel, delete);
    }
}

internal static class Scan
{
    public sealed record Result(TimeSpan Total, TimeSpan FirstResults, int Parallelism, string Summary);

    public static Result Run(string left, string right, FolderCompareOptions options)
    {
        var sw = Stopwatch.StartNew();
        var session = FolderCompareSession.OpenAsync(left, right, options).GetAwaiter().GetResult();
        var total = sw.Elapsed;
        var s = session.Summary;
        return new Result(total, session.FirstResultsTime, session.Parallelism,
            $"{s.Total:N0} files: {s.Same:N0} same, {s.Different:N0} different, {s.LeftOnly:N0} left only, {s.RightOnly:N0} right only, {s.Errors:N0} errors");
    }
}

/// <summary>
/// A reproducible pair of folder trees: many small files, some medium and a few large ones, nested a few levels.
/// The right side is a copy with some files changed (same size and different size), and some only on one side.
/// </summary>
internal static class TestTree
{
    public static (string Left, string Right) Ensure(string root, string profile, bool regenerate)
    {
        string left = Path.Combine(root, "left"), right = Path.Combine(root, "right"), done = Path.Combine(root, "complete");
        if (!regenerate && File.Exists(done)) return (left, right);

        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Console.WriteLine($"Generating the {profile} test tree in {root} ...");
        var sw = Stopwatch.StartNew();

        bool full = profile == "full";
        int small = full ? 4_900 : 480, medium = full ? 90 : 16, large = full ? 10 : 4;
        var random = new Random(20261124);
        long bytes = 0;
        var files = new List<(string Relative, int Size)>();

        string FolderFor(int i) => Path.Combine($"area{i % 8}", $"part{i % 23}", $"leaf{i % 5}");
        for (int i = 0; i < small; i++) files.Add((Path.Combine(FolderFor(i), $"small{i:D5}.dat"), random.Next(1, 64 * 1024)));
        for (int i = 0; i < medium; i++) files.Add((Path.Combine(FolderFor(i), $"medium{i:D3}.bin"), random.Next(1, 8) * 1024 * 1024 + random.Next(1, 4096)));
        for (int i = 0; i < large; i++) files.Add((Path.Combine($"big{i % 3}", $"large{i:D2}.iso"), (full ? 60 + random.Next(0, 25) : 8) * 1024 * 1024));

        var buffer = new byte[85 * 1024 * 1024];
        random.NextBytes(buffer);

        for (int i = 0; i < files.Count; i++)
        {
            var (relative, size) = files[i];
            int offset = random.Next(0, buffer.Length - size);
            var content = buffer.AsSpan(offset, size);
            Write(Path.Combine(left, relative), content);
            bytes += size;

            switch (i % 50)
            {
                case 7: // same size, one byte different in the middle (needs a full read)
                    var changed = content.ToArray();
                    changed[size / 2] ^= 0x5A;
                    Write(Path.Combine(right, relative), changed);
                    break;
                case 13: // different size
                    Write(Path.Combine(right, relative), content[..Math.Max(0, size - 1)]);
                    break;
                case 21: // left only
                    break;
                default:
                    Write(Path.Combine(right, relative), content);
                    break;
            }
        }
        for (int i = 0; i < files.Count / 100; i++) Write(Path.Combine(right, FolderFor(i), $"new{i:D4}.txt"), buffer.AsSpan(i, 1000));

        File.WriteAllText(done, DateTime.Now.ToString("O"));
        Console.WriteLine($"  {files.Count:N0} files, {bytes / (1024 * 1024.0):N0} MB per side, in {sw.Elapsed.TotalSeconds:0} s");
        return (left, right);
    }

    private static void Write(string path, ReadOnlySpan<byte> content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        stream.Write(content);
    }
}
