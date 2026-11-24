using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileCompare.UiTests.Infrastructure;

/// <summary>
/// Sample files for the UI tests. Every name starts with "fcui-" so the items are easy to find in Explorer, also when
/// Explorer hides file extensions.
/// </summary>
public sealed class TestFiles
{
    private TestFiles(string root) => Root = root;

    public string Root { get; }

    public string TextLeft => P(@"text\fcui-left.txt");
    public string TextRight => P(@"text\fcui-right.txt");
    public string Ampersand1 => P(@"text\fcui-notes & ideas (v1).txt");
    public string Ampersand2 => P(@"text\fcui-notes & ideas (v2).txt");
    public string NonAscii1 => P($@"text\fcui-Z{(char)0xFC}rich 1.txt");
    public string NonAscii2 => P($@"text\fcui-Z{(char)0xFC}rich 2.txt");
    public string ThirdText => P(@"text\fcui-third.txt");
    public string FolderLeft => P(@"folders\fcui-left");
    public string FolderRight => P(@"folders\fcui-right");
    public string Binary1 => P(@"binary\fcui-app-v1.bin");
    public string Binary2 => P(@"binary\fcui-app-v2.bin");
    public string Picture1 => P(@"images\fcui-before.png");
    public string Picture2 => P(@"images\fcui-after.png");

    /// <summary>The folder that Explorer shows for the drag and drop tests.</summary>
    public string DragFolder => P("fcui-drag");
    public string DragLeft => Path.Combine(DragFolder, "fcui-left.txt");
    public string DragRight => Path.Combine(DragFolder, "fcui-right.txt");
    public string DragNotes1 => Path.Combine(DragFolder, "fcui-notes1.txt");
    public string DragNotes2 => Path.Combine(DragFolder, "fcui-notes2.txt");
    public string DragFolderA => Path.Combine(DragFolder, "fcui-dirA");
    public string DragFolderB => Path.Combine(DragFolder, "fcui-dirB");
    public string DragFolderC => Path.Combine(DragFolder, "fcui-dirC");
    public string DragFolderD => Path.Combine(DragFolder, "fcui-dirD");
    public string DragBinary1 => Path.Combine(DragFolder, "fcui-app-v1.bin");
    public string DragBinary2 => Path.Combine(DragFolder, "fcui-app-v2.bin");
    public string DragPicture1 => Path.Combine(DragFolder, "fcui-before.png");
    public string DragPicture2 => Path.Combine(DragFolder, "fcui-after.png");
    public string DragPictureOtherSize => Path.Combine(DragFolder, "fcui-other-size.png");

    /// <summary>A .zip archive in the drag folder. Explorer shows it as a folder holding <see cref="ZippedFileName"/>.</summary>
    public string DragZip => Path.Combine(DragFolder, "fcui-archive.zip");
    public const string ZippedFileName = "fcui-zipped.txt";

    private string P(string relative) => Path.Combine(Root, relative);

    public static TestFiles Create(string root)
    {
        var files = new TestFiles(root);
        string[] lines = Enumerable.Range(1, 20).Select(i => $"Line {i}").ToArray();
        string[] changed = (string[])lines.Clone();
        changed[4] = "Line 5, changed";

        Text(files.TextLeft, lines);
        Text(files.TextRight, changed);
        Text(files.ThirdText, "third");
        Text(files.Ampersand1, "one", "two");
        Text(files.Ampersand2, "one", "TWO");
        Text(files.NonAscii1, "Gr\u00FCezi", "one");
        Text(files.NonAscii2, "Gr\u00FCezi", "two");

        Text(Path.Combine(files.FolderLeft, "same.txt"), "same");
        Text(Path.Combine(files.FolderRight, "same.txt"), "same");
        Text(Path.Combine(files.FolderLeft, "changed.txt"), "left");
        Text(Path.Combine(files.FolderRight, "changed.txt"), "right");

        var bytes = new byte[512];
        new Random(7).NextBytes(bytes);
        Write(files.Binary1, bytes);
        var other = (byte[])bytes.Clone();
        other[10] ^= 0xFF;
        Write(files.Binary2, other);

        Picture(files.Picture1, Colors.SteelBlue);
        Picture(files.Picture2, Colors.IndianRed);

        Text(files.DragLeft, lines);
        Text(files.DragRight, changed);
        Text(files.DragNotes1, "notes", "one");
        Text(files.DragNotes2, "notes", "two");
        Text(Path.Combine(files.DragFolderA, "a.txt"), "a");
        Text(Path.Combine(files.DragFolderB, "a.txt"), "b");
        Text(Path.Combine(files.DragFolderC, "c.txt"), "c");
        Text(Path.Combine(files.DragFolderD, "c.txt"), "d");
        Write(files.DragBinary1, bytes);
        Write(files.DragBinary2, other);
        Picture(files.DragPicture1, Colors.SteelBlue);
        Picture(files.DragPicture2, Colors.IndianRed);
        Picture(files.DragPictureOtherSize, Colors.SeaGreen, width: 60, height: 30);

        using (var zip = System.IO.Compression.ZipFile.Open(files.DragZip, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry(ZippedFileName).Open()))
            writer.Write("zipped\r\n");
        return files;
    }

    private static void Text(string path, params string[] lines) =>
        Write(path, new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines) + "\r\n"));

    private static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>A small PNG, half of it in <paramref name="color"/>, so two pictures differ in part of their pixels.</summary>
    private static void Picture(string path, Color color, int width = 40, int height = 30)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                var c = x < width / 2 ? Colors.White : color;
                pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R; pixels[i + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
