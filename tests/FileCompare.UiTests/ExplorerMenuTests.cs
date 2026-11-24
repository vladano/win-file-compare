using FileCompare.Core.Shell;
using FileCompare.UiTests.Infrastructure;
using FlaUI.Core.AutomationElements;

namespace FileCompare.UiTests;

/// <summary>
/// docs/TESTING.md, section 2 (EM): the Explorer menu, tested at the command level. The app registers its entries
/// (in a throwaway key), and each test runs the command line stored for an entry, exactly as Explorer would after a click.
/// That covers everything the app does; whether Explorer draws the entries is left to the manual checklist.
/// </summary>
[Collection(UiCollection.Name)]
public sealed class ExplorerMenuTests
{
    private const string TextView = "File Compare", FolderView = "Folder Compare";

    private readonly UiTestEnvironment _env;

    public ExplorerMenuTests(UiTestEnvironment env)
    {
        _env = env;
        if (!UiFactAttribute.IsEnabled) return;

        // Every test starts with no app running, nothing remembered, and the entries freshly installed.
        App.KillAll();
        File.Delete(env.LeftSideFile);
        App.RegisterMenu();
    }

    private AppDriver App => _env.App;
    private TestFiles Files => _env.Files;

    private static string Title(string left, string right, string view) =>
        $"{Path.GetFileName(left)} <--> {Path.GetFileName(right)} - {view}";

    /// <summary>Clicks "Select left side" and waits for that short-lived copy of the app to finish.</summary>
    private void SelectLeft(MenuTarget target, string item)
    {
        using var process = App.RunMenuCommand(target, ShellRegistration.SelectLeftVerb, item);
        Assert.True(process.WaitForExit(15000), "\"Select left side\" did not finish");
    }

    [UiFact]
    public void EM01_EntriesForFilesFoldersAndBackground()
    {
        foreach (var target in new[] { MenuTarget.Files, MenuTarget.Folders })
        {
            Assert.Equal("Select left side to compare", App.Label(target, ShellRegistration.SelectLeftVerb));
            Assert.Equal("Compare selected items", App.Label(target, ShellRegistration.CompareVerb));
            Assert.Equal("Single", App.Value(target, ShellRegistration.SelectLeftVerb, "MultiSelectModel"));
            Assert.Equal("Player", App.Value(target, ShellRegistration.CompareVerb, "MultiSelectModel"));
            Assert.False(App.HasEntry(target, ShellRegistration.CompareToVerb), "\"Compare to\" before anything was selected");
        }

        Assert.Equal("Select left side to compare", App.Label(MenuTarget.FolderBackground, ShellRegistration.SelectLeftVerb));
        Assert.False(App.HasEntry(MenuTarget.FolderBackground, ShellRegistration.CompareVerb));
        Assert.Equal($"\"{_env.ExePath}\",0", App.Value(MenuTarget.Files, ShellRegistration.SelectLeftVerb, "Icon"));
    }

    [UiFact]
    public void EM02_SelectLeft_RemembersTheItem_AndAddsCompareTo()
    {
        SelectLeft(MenuTarget.Files, Files.TextLeft);

        Assert.Equal(Files.TextLeft, File.ReadAllText(_env.LeftSideFile).Trim());
        foreach (var target in new[] { MenuTarget.Files, MenuTarget.Folders, MenuTarget.FolderBackground })
            Assert.Equal("Compare to 'fcui-left.txt'", App.Label(target, ShellRegistration.CompareToVerb));
        Assert.Empty(App.Windows());
    }

    [UiFact]
    public void EM03_CompareTo_OpensTheComparison_AndRemovesCompareTo()
    {
        SelectLeft(MenuTarget.Files, Files.TextLeft);
        App.RunMenuCommand(MenuTarget.Files, ShellRegistration.CompareToVerb, Files.TextRight).Dispose();

        App.WaitForWindow(t => t == Title(Files.TextLeft, Files.TextRight, TextView), "the text compare window");
        Wait.Until(() => !App.HasEntry(MenuTarget.Files, ShellRegistration.CompareToVerb), "\"Compare to\" to be removed");
        Assert.False(File.Exists(_env.LeftSideFile), "the left side is still remembered");
    }

    [UiFact]
    public void EM04_CompareTo_WithFolders_OpensFolderCompare()
    {
        SelectLeft(MenuTarget.Folders, Files.FolderLeft);
        App.RunMenuCommand(MenuTarget.Folders, ShellRegistration.CompareToVerb, Files.FolderRight).Dispose();

        App.WaitForWindow(t => t == Title(Files.FolderLeft, Files.FolderRight, FolderView), "the folder compare window");
    }

    [UiFact]
    public void EM05_FolderBackground_SelectLeftThenCompareTo()
    {
        SelectLeft(MenuTarget.FolderBackground, Files.FolderLeft);
        Assert.Equal("Compare to 'fcui-left'", App.Label(MenuTarget.FolderBackground, ShellRegistration.CompareToVerb));
        App.RunMenuCommand(MenuTarget.FolderBackground, ShellRegistration.CompareToVerb, Files.FolderRight).Dispose();

        App.WaitForWindow(t => t == Title(Files.FolderLeft, Files.FolderRight, FolderView), "the folder compare window");
    }

    [UiFact]
    public void EM06_CompareSelected_TwoFiles_OpenOneComparison()
    {
        // Explorer starts the command once per selected item, at the same time.
        App.RunMenuCommand(MenuTarget.Files, ShellRegistration.CompareVerb, Files.TextLeft).Dispose();
        App.RunMenuCommand(MenuTarget.Files, ShellRegistration.CompareVerb, Files.TextRight).Dispose();

        App.WaitForWindow(t => t == Title(Files.TextLeft, Files.TextRight, TextView) || t == Title(Files.TextRight, Files.TextLeft, TextView),
            "one comparison of the two files");
        Wait.Until(() => App.Running().Count == 1, "a single running copy");
        Assert.Single(App.Windows());
    }

    [UiFact]
    public void EM07_CompareSelected_TwoFolders_OpenFolderCompare()
    {
        App.RunMenuCommand(MenuTarget.Folders, ShellRegistration.CompareVerb, Files.FolderLeft).Dispose();
        App.RunMenuCommand(MenuTarget.Folders, ShellRegistration.CompareVerb, Files.FolderRight).Dispose();

        App.WaitForWindow(t => t.EndsWith(" - " + FolderView, StringComparison.Ordinal) && t.Contains("fcui-left") && t.Contains("fcui-right"),
            "the folder compare window");
    }

    [UiFact]
    public void EM08_CompareSelected_OneItem_BecomesTheLeftSide()
    {
        using (var process = App.RunMenuCommand(MenuTarget.Files, ShellRegistration.CompareVerb, Files.TextLeft))
            Assert.True(process.WaitForExit(15000), "the app kept running after one item");

        Assert.Equal(Files.TextLeft, File.ReadAllText(_env.LeftSideFile).Trim());
        Assert.Equal("Compare to 'fcui-left.txt'", App.Label(MenuTarget.Files, ShellRegistration.CompareToVerb));
        Assert.Empty(App.Windows());
    }

    [UiFact]
    public void EM09_CompareSelected_ThreeItems_ShowsAMessage_AndComparesTwo()
    {
        foreach (var item in new[] { Files.TextLeft, Files.TextRight, Files.ThirdText })
            App.RunMenuCommand(MenuTarget.Files, ShellRegistration.CompareVerb, item).Dispose();

        var message = Wait.For(() => App.FindMessageBox("3 items were selected"), "the \"3 items were selected\" message");
        message.FindFirstDescendant(cf => cf.ByName("OK"))!.AsButton().Invoke();

        var names = new[] { "fcui-left.txt", "fcui-right.txt", "fcui-third.txt" };
        App.WaitForWindow(t => t.EndsWith(" - " + TextView, StringComparison.Ordinal) && names.Count(t.Contains) == 2,
            "a comparison of two of the three files");
    }

    [UiFact]
    public void EM10_NameWithAmpersand_IsEscapedInTheLabel()
    {
        SelectLeft(MenuTarget.Files, Files.Ampersand1);

        // Explorer turns "&&" into a plain "&" (a single "&" would underline the next letter instead).
        Assert.Equal("Compare to 'fcui-notes && ideas (v1).txt'", App.Label(MenuTarget.Files, ShellRegistration.CompareToVerb));
        App.RunMenuCommand(MenuTarget.Files, ShellRegistration.CompareToVerb, Files.Ampersand2).Dispose();
        App.WaitForWindow(t => t == Title(Files.Ampersand1, Files.Ampersand2, TextView), "the comparison of the & files");
    }

    [UiFact]
    public void EM11_NonAsciiNames_Work()
    {
        SelectLeft(MenuTarget.Files, Files.NonAscii1);

        Assert.Equal($"Compare to '{Path.GetFileName(Files.NonAscii1)}'", App.Label(MenuTarget.Files, ShellRegistration.CompareToVerb));
        App.RunMenuCommand(MenuTarget.Files, ShellRegistration.CompareToVerb, Files.NonAscii2).Dispose();
        App.WaitForWindow(t => t == Title(Files.NonAscii1, Files.NonAscii2, TextView), "the comparison of the non-ASCII names");
    }

    [UiFact]
    public void EM12_PicturesAndBinaryFiles_OpenTheirViews()
    {
        SelectLeft(MenuTarget.Files, Files.Binary1);
        App.RunMenuCommand(MenuTarget.Files, ShellRegistration.CompareToVerb, Files.Binary2).Dispose();
        App.WaitForWindow(t => t == Title(Files.Binary1, Files.Binary2, "Hex Compare"), "the hex compare");

        SelectLeft(MenuTarget.Files, Files.Picture1);
        App.RunMenuCommand(MenuTarget.Files, ShellRegistration.CompareToVerb, Files.Picture2).Dispose();
        App.WaitForWindow(t => t == Title(Files.Picture1, Files.Picture2, "Image Compare"), "the image compare");
    }

    [UiFact]
    public void EM13_AppAlreadyOpen_OpensANewTabInTheSameWindow()
    {
        App.Start().Dispose();
        var window = App.WaitForWindow(t => t == "File Compare", "the app with its Home tab");

        SelectLeft(MenuTarget.Files, Files.TextLeft);
        App.RunMenuCommand(MenuTarget.Files, ShellRegistration.CompareToVerb, Files.TextRight).Dispose();

        Wait.Until(() => AppDriver.Tabs(window).Contains("fcui-left.txt ↔ fcui-right.txt"), "a new tab in the open window");
        Assert.Equal(2, AppDriver.Tabs(window).Count);
        Assert.Single(App.Windows());
        Wait.Until(() => App.Running().Count == 1, "a single running copy");
    }

    [UiFact]
    public void EM14_SelectLeft_WithTheAppClosed_LeavesNoProcess()
    {
        SelectLeft(MenuTarget.Files, Files.TextLeft);

        Thread.Sleep(1500);
        Assert.Empty(App.Running());
    }

    [UiFact]
    public void EM17_EM18_UnregisterShell_RemovesEverything()
    {
        SelectLeft(MenuTarget.Files, Files.TextLeft); // so that a "Compare to" entry exists too
        App.UnregisterMenu();

        foreach (var target in new[] { MenuTarget.Files, MenuTarget.Folders, MenuTarget.FolderBackground })
        {
            foreach (var verb in new[] { ShellRegistration.SelectLeftVerb, ShellRegistration.CompareToVerb, ShellRegistration.CompareVerb })
                Assert.False(App.HasEntry(target, verb), $"{verb} is still there for {target}");
        }
        Assert.False(File.Exists(_env.LeftSideFile), "the left side is still remembered");
    }
}
