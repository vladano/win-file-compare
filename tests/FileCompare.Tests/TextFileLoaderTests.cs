using System.Text;
using FileCompare.Core.Text;

namespace FileCompare.Tests;

public sealed class TextFileLoaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("filecompare-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(byte[] bytes)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string Write(string text, Encoding encoding) => Write(encoding.GetBytes(text));

    [Fact]
    public void Utf8Bom_IsDetectedAndStripped()
    {
        var path = Write(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a', (byte)'\r', (byte)'\n', (byte)'b' });
        var t = TextFileLoader.Load(path);
        Assert.Equal("UTF-8 BOM", t.EncodingName);
        Assert.Equal(LineEnding.Windows, t.LineEnding);
        Assert.Equal("a\r\nb", t.Text);
    }

    [Fact]
    public void Utf8WithoutBom_IsDetected()
    {
        var path = Write("héllo wörld", new UTF8Encoding(false));
        var t = TextFileLoader.Load(path);
        Assert.Equal("UTF-8", t.EncodingName);
        Assert.Equal("héllo wörld", t.Text);
    }

    [Fact]
    public void InvalidUtf8_FallsBackToAnsi()
    {
        var path = Write(new byte[] { (byte)'c', (byte)'a', (byte)'f', 0xE9 }); // 0xE9 alone is invalid UTF-8
        var t = TextFileLoader.Load(path);
        Assert.Equal("ANSI", t.EncodingName);
        Assert.Equal("café", t.Text);
    }

    [Fact]
    public void Utf16LittleEndian_IsDetected()
    {
        var path = Write(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("hi")).ToArray());
        var t = TextFileLoader.Load(path);
        Assert.Equal("UTF-16 LE", t.EncodingName);
        Assert.Equal("hi", t.Text);
    }

    [Fact]
    public void Utf16BigEndian_IsDetected()
    {
        var path = Write(Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes("hi")).ToArray());
        var t = TextFileLoader.Load(path);
        Assert.Equal("UTF-16 BE", t.EncodingName);
        Assert.Equal("hi", t.Text);
    }

    [Fact]
    public void EmptyFile_LoadsAsEmptyUtf8WithNoLineEnding()
    {
        var t = TextFileLoader.Load(Write(Array.Empty<byte>()));
        Assert.Equal("", t.Text);
        Assert.Equal("UTF-8", t.EncodingName);
        Assert.Equal(LineEnding.None, t.LineEnding);
        Assert.Equal(0, t.SizeBytes);
    }

    [Theory]
    [InlineData("a\nb\n", LineEnding.Unix)]
    [InlineData("a\r\nb\r\n", LineEnding.Windows)]
    [InlineData("a\rb\r", LineEnding.Mac)]
    [InlineData("a\r\nb\n", LineEnding.Mixed)]
    [InlineData("a\nb\rc", LineEnding.Mixed)]
    [InlineData("single line", LineEnding.None)]
    public void LineEndings_AreClassified(string text, LineEnding expected)
    {
        var t = TextFileLoader.Load(Write(text, new UTF8Encoding(false)));
        Assert.Equal(expected, t.LineEnding);
    }

    [Fact]
    public void Metadata_ReflectsFile()
    {
        var path = Write("abc", new UTF8Encoding(false));
        var t = TextFileLoader.Load(path);
        Assert.Equal(path, t.Path);
        Assert.Equal(3, t.SizeBytes);
        Assert.Equal(File.GetLastWriteTime(path), t.LastWriteTime);
    }

    [Fact]
    public void FileOpenForWriting_CanStillBeRead()
    {
        var path = Write("locked", new UTF8Encoding(false));
        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Equal("locked", TextFileLoader.Load(path).Text);
    }

    [Fact]
    public void MissingFile_Throws()
    {
        Assert.ThrowsAny<IOException>(() => TextFileLoader.Load(Path.Combine(_dir, "nope.txt")));
    }
}
