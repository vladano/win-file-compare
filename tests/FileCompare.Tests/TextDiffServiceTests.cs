using FileCompare.Core.Text;

namespace FileCompare.Tests;

public class TextDiffServiceTests
{
    [Fact]
    public void IdenticalText_HasNoDifferences()
    {
        var r = TextDiffService.Compare("a\nb\nc", "a\nb\nc");
        Assert.True(r.AreSame);
        Assert.Equal(0, r.DifferenceCount);
        Assert.Equal(3, r.Rows.Count);
        Assert.All(r.Rows, row => Assert.Equal(RowKind.Same, row.Kind));
    }

    [Fact]
    public void BothEmpty_IsSame()
    {
        var r = TextDiffService.Compare("", "");
        Assert.True(r.AreSame);
    }

    [Fact]
    public void InsertedLine_IsRightOnly_AndAligned()
    {
        var r = TextDiffService.Compare("a\nc", "a\nb\nc");
        Assert.Equal(1, r.DifferenceCount);
        Assert.Equal(3, r.Rows.Count);
        Assert.Equal(RowKind.RightOnly, r.Rows[1].Kind);
        Assert.Null(r.Rows[1].LeftNumber);
        Assert.Null(r.Rows[1].LeftText);
        Assert.Equal(2, r.Rows[1].RightNumber);
        Assert.Equal("b", r.Rows[1].RightText);
    }

    [Fact]
    public void DeletedLine_IsLeftOnly_AndAligned()
    {
        var r = TextDiffService.Compare("a\nb\nc", "a\nc");
        Assert.Equal(1, r.DifferenceCount);
        Assert.Equal(RowKind.LeftOnly, r.Rows[1].Kind);
        Assert.Equal("b", r.Rows[1].LeftText);
        Assert.Null(r.Rows[1].RightNumber);
    }

    [Fact]
    public void ChangedLine_IsModified()
    {
        var r = TextDiffService.Compare("a\nb", "a\nx");
        Assert.Equal(1, r.DifferenceCount);
        Assert.Equal(RowKind.Modified, r.Rows[1].Kind);
        Assert.Equal("b", r.Rows[1].LeftText);
        Assert.Equal("x", r.Rows[1].RightText);
    }

    [Fact]
    public void LineNumbers_AreOneBasedAndSkipFillerLines()
    {
        var r = TextDiffService.Compare("a\nb", "a\nx\nb");
        Assert.Equal(new int?[] { 1, null, 2 }, r.Rows.Select(x => x.LeftNumber).ToArray());
        Assert.Equal(new int?[] { 1, 2, 3 }, r.Rows.Select(x => x.RightNumber).ToArray());
    }

    [Fact]
    public void EmptyLeft_AllRowsAreRightOnly()
    {
        var r = TextDiffService.Compare("", "a\nb");
        Assert.True(r.Rows.Count >= 2);
        Assert.False(r.AreSame);
        Assert.Contains(r.Rows, x => x.Kind == RowKind.RightOnly && x.RightText == "a");
        Assert.Contains(r.Rows, x => x.Kind == RowKind.RightOnly && x.RightText == "b");
    }

    [Fact]
    public void SeparatedChanges_AreCountedAsSeparateSections()
    {
        var r = TextDiffService.Compare("a\nb\nc\nd\ne", "a\nB\nc\nD\ne");
        Assert.Equal(2, r.DifferenceCount);
        Assert.Equal(new[] { 1, 3 }, r.SectionStarts.ToArray());
    }

    [Fact]
    public void AdjacentChanges_AreOneSection()
    {
        var r = TextDiffService.Compare("a\nb\nc\nd", "a\nB\nC\nd");
        Assert.Equal(1, r.DifferenceCount);
        Assert.Equal(1, r.SectionStarts[0]);
    }

    [Fact]
    public void ChangeOnFirstLine_StartsSectionAtRowZero()
    {
        var r = TextDiffService.Compare("a\nb", "x\nb");
        Assert.Equal(0, r.SectionStarts[0]);
    }

    [Fact]
    public void IgnoreCase_TreatsCaseChangesAsSame()
    {
        var r = TextDiffService.Compare("Hello", "hello", new DiffOptions { IgnoreCase = true });
        Assert.True(r.AreSame);
    }

    [Fact]
    public void CaseChange_IsDifferentByDefault()
    {
        var r = TextDiffService.Compare("Hello", "hello");
        Assert.False(r.AreSame);
    }

    [Fact]
    public void IgnoreWhitespace_TreatsIndentChangesAsSame()
    {
        var r = TextDiffService.Compare("if (x) {\n  y();\n}", "if (x) {\n\t\ty();\n}",
            new DiffOptions { IgnoreWhitespace = true });
        Assert.True(r.AreSame);
    }

    [Fact]
    public void WhitespaceChange_IsDifferentByDefault()
    {
        var r = TextDiffService.Compare("y();", "  y();");
        Assert.False(r.AreSame);
    }

    [Fact]
    public void MixedLineEndings_AreSplitIntoSameLines()
    {
        var r = TextDiffService.Compare("a\r\nb\r\nc", "a\nb\nc");
        Assert.True(r.AreSame);
    }

    [Fact]
    public void RowTextOnBothSides_RebuildsOriginalLines()
    {
        var r = TextDiffService.Compare("one\ntwo\nthree", "one\n2\nthree\nfour");
        var left = r.Rows.Where(x => x.LeftText != null).Select(x => x.LeftText).ToArray();
        var right = r.Rows.Where(x => x.RightText != null).Select(x => x.RightText).ToArray();
        Assert.Equal(new[] { "one", "two", "three" }, left);
        Assert.Equal(new[] { "one", "2", "three", "four" }, right);
    }
}

public class DiffResultTests
{
    private static DiffRow Row(RowKind kind) => new(1, "x", 1, "x", kind);

    [Fact]
    public void NoRows_IsSame()
    {
        var r = new DiffResult(Array.Empty<DiffRow>());
        Assert.True(r.AreSame);
        Assert.Empty(r.SectionStarts);
    }

    [Fact]
    public void SectionStarts_AreFirstRowOfEachNonSameRun()
    {
        var r = new DiffResult(new[]
        {
            Row(RowKind.Modified), Row(RowKind.LeftOnly), Row(RowKind.Same),
            Row(RowKind.Same), Row(RowKind.RightOnly), Row(RowKind.Same)
        });
        Assert.Equal(new[] { 0, 4 }, r.SectionStarts.ToArray());
        Assert.Equal(2, r.DifferenceCount);
        Assert.False(r.AreSame);
    }
}
