using FileCompare.UiTests.Infrastructure;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Wait = FileCompare.UiTests.Infrastructure.Wait;

namespace FileCompare.UiTests;

/// <summary>docs/TESTING.md, section 3, text compare (DD-05..DD-11).</summary>
[Collection(UiCollection.Name)]
public sealed class TextDragDropTests : DragDropTest
{
    private const string View = "File Compare";

    public TextDragDropTests(UiTestEnvironment env)
        : base(env, new[] { env.Files.DragLeft, env.Files.DragRight }, TitleOf(env.Files.DragLeft, env.Files.DragRight, View))
    {
    }

    [UiFact]
    public void DD05_FileOnTheLeftPathBox_ReplacesTheLeftSide()
    {
        Drop("LeftPath", Files.DragNotes1);

        WaitForTitle(TitleOf(Files.DragNotes1, Files.DragRight, View));
        Assert.Equal(Files.DragNotes1, PathBox("LeftPath"));
    }

    [UiFact]
    public void DD06_FileOnTheRightEditor_ReplacesTheRightSide()
    {
        Drop("RightEditor", Files.DragNotes2);

        WaitForTitle(TitleOf(Files.DragLeft, Files.DragNotes2, View));
        Assert.Equal(Files.DragNotes2, PathBox("RightPath"));
    }

    [UiFact]
    public void DD07_TwoFilesOnTheStatusBar_ReplaceBothSides()
    {
        Drop("DiffCountText", Files.DragNotes1, Files.DragNotes2);

        WaitForTitle(EitherOrder(Files.DragNotes1, Files.DragNotes2, View), "both sides to be replaced");
    }

    [UiFact]
    public void DD08_TwoFolders_OpenAFolderCompareInANewTab()
    {
        Drop("DiffCountText", Files.DragFolderA, Files.DragFolderB);

        WaitForTitle(EitherOrder(Files.DragFolderA, Files.DragFolderB, "Folder Compare"), "a folder compare tab");
        Assert.Equal(2, Tabs().Count);

        AppDriver.SelectTab(AppWindow, text => text == TabOf(Files.DragLeft, Files.DragRight));
        WaitForText("DiffCountText", t => t == "Opened in a new folder compare tab", "the text tab to say where the folders went");
    }

    [UiFact]
    public void DD09_DropWithUnsavedChanges_Cancel_KeepsEverything()
    {
        TypeInLeftEditor("abc");

        Drop("LeftPath", Files.DragNotes1);
        Answer("There are unsaved changes", "Cancel");

        Thread.Sleep(500);
        Assert.Equal(Files.DragLeft, PathBox("LeftPath"));
        Assert.Equal(Files.DragRight, PathBox("RightPath"));
        Assert.Equal(ModifiedTitle, AppWindow.Title); // the typing is still there, unsaved
    }

    [UiFact]
    public void DD10_DropWithUnsavedChanges_No_LoadsTheNewFile()
    {
        TypeInLeftEditor("abc");

        Drop("LeftPath", Files.DragNotes1);
        Answer("There are unsaved changes", "No");

        WaitForTitle(TitleOf(Files.DragNotes1, Files.DragRight, View)); // no " *": the typing was discarded
        Assert.Equal("Line 1", File.ReadLines(Files.DragLeft).First()); // and not saved to the old file
    }

    /// <summary>
    /// DD-11 drags text from another program. Here the text comes from the app's own editor: that is the same kind of
    /// drag (plain text, not files), without starting Notepad, which would add the file to the user's Notepad tabs.
    /// </summary>
    [UiFact]
    public void DD11_PlainText_IsInsertedIntoThePathBox_AndNothingIsCompared()
    {
        var editor = Target("LeftEditor");
        var r = editor.BoundingRectangle;
        var from = new System.Drawing.Point(r.Left + 70, r.Top + 12); // on the text of the first line
        Mouse.Click(from);
        Thread.Sleep(200);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Thread.Sleep(300);

        var to = DragAndDrop.Center(Target("RightPath"));
        EnsureAppAt(to);
        DragAndDrop.Drag(from, to);

        Wait.Until(() => PathBox("RightPath").Contains("Line", StringComparison.Ordinal), $"the text in the right path box (it is \"{PathBox("RightPath")}\")");
        Thread.Sleep(1000);
        Assert.Equal(TitleOf(Files.DragLeft, Files.DragRight, View), AppWindow.Title);
        Assert.Single(Tabs());
        Assert.Equal(Files.DragLeft, PathBox("LeftPath"));
    }

    private string ModifiedTitle => $"{Path.GetFileName(Files.DragLeft)} <--> {Path.GetFileName(Files.DragRight)} * - {View}";

    /// <summary>Turns on Edit and types at the end of the first line of the left side.</summary>
    private void TypeInLeftEditor(string text)
    {
        var edit = Target("EditToggle").AsToggleButton();
        edit.Toggle();
        Wait.Until(() => edit.ToggleState == ToggleState.On, "Edit to be on");

        var r = Target("LeftEditor").BoundingRectangle;
        Mouse.Click(new System.Drawing.Point(r.Left + r.Width / 2, r.Top + 12));
        Thread.Sleep(200);
        Keyboard.Type(VirtualKeyShort.END);
        Keyboard.Type(text);
        WaitForTitle(t => t == ModifiedTitle, "the title to show unsaved changes");
    }
}
