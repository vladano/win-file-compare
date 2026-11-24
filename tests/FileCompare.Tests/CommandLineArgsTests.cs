using FileCompare.App;

namespace FileCompare.Tests;

public class CommandLineArgsTests
{
    [Fact]
    public void NoArgs_GivesNoPaths()
    {
        var a = CommandLineArgs.Parse(Array.Empty<string>());
        Assert.Null(a.Left);
        Assert.Null(a.Right);
    }

    [Fact]
    public void OneArg_IsLeftOnly()
    {
        var a = CommandLineArgs.Parse(new[] { @"C:\a.txt" });
        Assert.Equal(@"C:\a.txt", a.Left);
        Assert.Null(a.Right);
    }

    [Fact]
    public void TwoArgs_AreLeftAndRight()
    {
        var a = CommandLineArgs.Parse(new[] { @"C:\a.txt", @"D:\b.txt" });
        Assert.Equal(@"C:\a.txt", a.Left);
        Assert.Equal(@"D:\b.txt", a.Right);
    }

    [Fact]
    public void ExtraArgs_AreIgnored()
    {
        var a = CommandLineArgs.Parse(new[] { "1", "2", "3" });
        Assert.Equal("1", a.Left);
        Assert.Equal("2", a.Right);
    }

    [Fact]
    public void BlankArgsAndSwitches_AreSkipped()
    {
        var a = CommandLineArgs.Parse(new[] { "", "--flag", "  ", "x", "y" });
        Assert.Equal("x", a.Left);
        Assert.Equal("y", a.Right);
    }

    [Fact]
    public void PathsWithSpaces_ArePreserved()
    {
        var a = CommandLineArgs.Parse(new[] { @"C:\My Files\a b.txt" });
        Assert.Equal(@"C:\My Files\a b.txt", a.Left);
    }
}
