using FileCompare.UiTests.Infrastructure;

namespace FileCompare.UiTests;

/// <summary>docs/TESTING.md, section 3, folder compare (DD-12..DD-14).</summary>
[Collection(UiCollection.Name)]
public sealed class FolderDragDropTests : DragDropTest
{
    private const string View = "Folder Compare";

    public FolderDragDropTests(UiTestEnvironment env)
        : base(env, new[] { env.Files.DragFolderA, env.Files.DragFolderB }, TitleOf(env.Files.DragFolderA, env.Files.DragFolderB, View))
    {
    }

    [UiFact]
    public void DD12_FolderOnTheLeftPathBox_ReplacesTheLeftSide()
    {
        Drop("LeftPath", Files.DragFolderC);

        WaitForTitle(TitleOf(Files.DragFolderC, Files.DragFolderB, View));
        Assert.Equal(Files.DragFolderC, PathBox("LeftPath"));
        WaitForText("StatusText", t => t == "Done", "the scan to finish");
    }

    [UiFact]
    public void DD13_TwoFoldersOnTheList_ReplaceBothSides()
    {
        Drop("FolderGrid", Files.DragFolderC, Files.DragFolderD);

        WaitForTitle(EitherOrder(Files.DragFolderC, Files.DragFolderD, View), "both sides to be replaced");
        Assert.Single(Tabs());
    }

    [UiFact]
    public void DD14_TwoFilesOnTheList_OpenATextCompareInANewTab()
    {
        Drop("FolderGrid", Files.DragLeft, Files.DragRight);

        WaitForTitle(EitherOrder(Files.DragLeft, Files.DragRight, "File Compare"), "a text compare tab");
        var tabs = Tabs();
        Assert.Equal(2, tabs.Count);
        Assert.Contains(TabOf(Files.DragFolderA, Files.DragFolderB), tabs); // the folder tab is unchanged
    }
}
