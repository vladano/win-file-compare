using FileCompare.UiTests.Infrastructure;

namespace FileCompare.UiTests;

/// <summary>docs/TESTING.md, section 3, image compare (DD-17, DD-18).</summary>
[Collection(UiCollection.Name)]
public sealed class ImageDragDropTests : DragDropTest
{
    private const string View = "Image Compare";

    public ImageDragDropTests(UiTestEnvironment env)
        : base(env, new[] { env.Files.DragPicture1, env.Files.DragPicture2 }, TitleOf(env.Files.DragPicture1, env.Files.DragPicture2, View))
    {
    }

    [UiFact]
    public void DD17_PictureOnTheRightPicture_ReplacesTheRightSide()
    {
        Drop("RightImage", Files.DragPictureOtherSize);

        WaitForTitle(TitleOf(Files.DragPicture1, Files.DragPictureOtherSize, View));
        WaitForText("DiffCountText", t => t.StartsWith("The sizes differ", StringComparison.Ordinal), "the status to say the sizes differ");
    }

    [UiFact]
    public void DD18_TwoTextFiles_OpenATextCompareInANewTab()
    {
        Drop("DiffCountText", Files.DragNotes1, Files.DragNotes2);

        WaitForTitle(EitherOrder(Files.DragNotes1, Files.DragNotes2, "File Compare"), "a text compare tab");
        var tabs = Tabs();
        Assert.Equal(2, tabs.Count);
        Assert.Contains(TabOf(Files.DragPicture1, Files.DragPicture2), tabs); // the image tab is unchanged
    }
}
