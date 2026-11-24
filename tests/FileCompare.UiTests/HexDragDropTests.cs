using FileCompare.UiTests.Infrastructure;

namespace FileCompare.UiTests;

/// <summary>docs/TESTING.md, section 3, hex compare (DD-15, DD-16).</summary>
[Collection(UiCollection.Name)]
public sealed class HexDragDropTests : DragDropTest
{
    private const string View = "Hex Compare";

    public HexDragDropTests(UiTestEnvironment env)
        : base(env, new[] { env.Files.DragBinary1, env.Files.DragBinary2 }, TitleOf(env.Files.DragBinary1, env.Files.DragBinary2, View))
    {
    }

    [UiFact]
    public void DD15_SameFileOnTheRightPathBox_IsIdentical()
    {
        Drop("RightPath", Files.DragBinary1);

        WaitForTitle(TitleOf(Files.DragBinary1, Files.DragBinary1, View));
        WaitForText("DiffCountText", t => t == "The files are identical", "the status to say identical");
    }

    [UiFact]
    public void DD16_TwoPictures_OpenAnImageCompareInANewTab()
    {
        Drop("DiffCountText", Files.DragPicture1, Files.DragPicture2);

        WaitForTitle(EitherOrder(Files.DragPicture1, Files.DragPicture2, "Image Compare"), "an image compare tab");
        var tabs = Tabs();
        Assert.Equal(2, tabs.Count);
        Assert.Contains(TabOf(Files.DragBinary1, Files.DragBinary2), tabs); // the hex tab is unchanged
    }
}
