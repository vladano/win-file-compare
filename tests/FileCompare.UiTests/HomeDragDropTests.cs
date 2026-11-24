using FileCompare.UiTests.Infrastructure;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace FileCompare.UiTests;

/// <summary>docs/TESTING.md, section 3, Home tab (DD-01..DD-04). The app starts without paths, so it shows Home.</summary>
[Collection(UiCollection.Name)]
public sealed class HomeDragDropTests : DragDropTest
{
    public HomeDragDropTests(UiTestEnvironment env) : base(env, Array.Empty<string>(), "File Compare")
    {
    }

    /// <summary>A place on the page that is not a path box: the "Saved sessions" heading.</summary>
    private System.Drawing.Point Page() =>
        NearStart(Wait.For(() => AppWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text).And(cf.ByName("Saved sessions"))),
            "the Saved sessions heading"));

    [UiFact]
    public void DD01_FileOnTheLeftBox_FillsIt()
    {
        Drop("LeftPath", Files.DragLeft);

        WaitForText("StatusText", t => t == "Now pick or drop the other side", "the note to ask for the other side");
        Assert.Equal(Files.DragLeft, PathBox("LeftPath"));
        Assert.Equal("File Compare", AppWindow.Title); // still Home
    }

    [UiFact]
    public void DD02_SecondFileOnTheRightBox_ThenCompare()
    {
        Drop("LeftPath", Files.DragLeft);
        WaitForText("StatusText", t => t == "Now pick or drop the other side", "the first drop");

        Drop("RightPath", Files.DragRight);
        WaitForText("StatusText", t => t == "Press Compare to start", "the note to say Compare");
        Assert.Equal(Files.DragRight, PathBox("RightPath"));
        Assert.Equal("File Compare", AppWindow.Title);

        Wait.For(() => AppWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Compare"))), "the Compare button")
            .AsButton().Invoke();

        WaitForTitle(TitleOf(Files.DragLeft, Files.DragRight, "File Compare"));
        Assert.Single(Tabs()); // in the same tab
    }

    [UiFact]
    public void DD03_TwoFilesAnywhere_OpenTheComparisonAtOnce()
    {
        Drop(Page(), Files.DragLeft, Files.DragRight);

        WaitForTitle(EitherOrder(Files.DragLeft, Files.DragRight, "File Compare"), "the text compare");
        Assert.Single(Tabs()); // in place of Home
    }

    [UiFact]
    public void DD04_TwoFolders_OpenAFolderCompare()
    {
        Drop(Page(), Files.DragFolderA, Files.DragFolderB);

        WaitForTitle(EitherOrder(Files.DragFolderA, Files.DragFolderB, "Folder Compare"), "the folder compare");
        Assert.Single(Tabs());
    }
}
