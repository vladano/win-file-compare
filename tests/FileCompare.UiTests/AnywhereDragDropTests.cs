using System.Security.Cryptography;
using FileCompare.UiTests.Infrastructure;
using FlaUI.Core.AutomationElements;

namespace FileCompare.UiTests;

/// <summary>docs/TESTING.md, section 3, anywhere (DD-19, DD-20), on a text compare.</summary>
[Collection(UiCollection.Name)]
public sealed class AnywhereDragDropTests : DragDropTest
{
    private const string View = "File Compare";

    public AnywhereDragDropTests(UiTestEnvironment env)
        : base(env, new[] { env.Files.DragLeft, env.Files.DragRight }, TitleOf(env.Files.DragLeft, env.Files.DragRight, View))
    {
    }

    /// <summary>
    /// A file inside a .zip has no path on disk until it is extracted. Either Windows hands over a temporary copy
    /// (the left side becomes it) or the drop is ignored. The app must keep running and show no error.
    /// </summary>
    [UiFact]
    public void DD19_FileFromAZipFolder_IsUsedOrIgnored()
    {
        OpenExplorer(Files.DragZip);
        string before = AppWindow.Title;

        Drop("LeftPath", Path.Combine(Files.DragZip, TestFiles.ZippedFileName));
        Thread.Sleep(3000);

        Assert.Single(App.Running());
        var windows = App.Windows().Where(w => w.Title.Length > 0).ToList(); // tooltips have no title, messages do
        string Describe(Window w) => $"\"{w.Title}\": " + string.Join(" | ",
            w.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text)).Select(t => t.Name).Take(5));
        Assert.True(windows.Count == 1, "Expected no message, found: " + string.Join("; ", windows.Where(w => w.Title != AppWindow.Title).Select(Describe)));
        string title = AppWindow.Title;
        string left = PathBox("LeftPath");
        bool ignored = title == before && left == Files.DragLeft;
        bool used = title == TitleOf(TestFiles.ZippedFileName, Files.DragRight, View)
                    && left.EndsWith(TestFiles.ZippedFileName, StringComparison.OrdinalIgnoreCase);
        Assert.True(ignored || used, $"Neither used nor ignored: the title is \"{title}\" and the left path \"{left}\".");
    }

    /// <summary>Several kinds of drops, then every file of the drag folder is compared with how it was before.</summary>
    [UiFact]
    public void DD20_Drops_DoNotMoveCopyOrChangeFiles()
    {
        var before = Snapshot(Files.DragFolder);

        Drop("LeftPath", Files.DragNotes1);
        WaitForTitle(TitleOf(Files.DragNotes1, Files.DragRight, View));

        Drop("RightEditor", Files.DragNotes2);
        WaitForTitle(TitleOf(Files.DragNotes1, Files.DragNotes2, View));

        Drop("DiffCountText", Files.DragLeft, Files.DragRight);
        WaitForTitle(EitherOrder(Files.DragLeft, Files.DragRight, View), "both sides to be replaced");

        Drop("DiffCountText", Files.DragFolderA, Files.DragFolderB);
        WaitForTitle(EitherOrder(Files.DragFolderA, Files.DragFolderB, "Folder Compare"), "a folder compare tab");

        Thread.Sleep(1000);
        Assert.Equal(before, Snapshot(Files.DragFolder));
    }

    /// <summary>Every file and folder below <paramref name="root"/> with its size, time and content hash.</summary>
    private static List<string> Snapshot(string root)
    {
        var lines = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            string relative = Path.GetRelativePath(root, path);
            if (Directory.Exists(path))
            {
                lines.Add($"{relative}\\");
                continue;
            }
            var info = new FileInfo(path);
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            lines.Add($"{relative} {info.Length} {info.LastWriteTimeUtc:O} {hash}");
        }
        return lines;
    }
}
