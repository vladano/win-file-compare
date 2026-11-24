using System.Text.Json.Serialization;
using FileCompare.Core.Text;

namespace FileCompare.Core.Sessions;

/// <summary>Which view shows a comparison.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CompareKind
{
    Text,
    Folder,
    Hex,
    Image
}

/// <summary>What a page compares: the view and the two paths.</summary>
public sealed record CompareTarget(CompareKind Kind, string Left, string Right);

/// <summary>Picks the view for two paths.</summary>
public static class CompareKindDetector
{
    /// <summary>Formats Windows can decode without extra codecs.</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico", ".wdp", ".jxr", ".hdp"
    };

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Folders (or one folder with the other side still empty) give <see cref="CompareKind.Folder"/>.
    /// Image files give <see cref="CompareKind.Image"/>, other binary files <see cref="CompareKind.Hex"/>,
    /// and everything else, including a folder paired with a file, <see cref="CompareKind.Text"/>
    /// (the text view explains what is wrong). Empty paths mean "not chosen yet".
    /// </summary>
    public static CompareKind Detect(string left, string right)
    {
        bool leftFolder = left.Length > 0 && Directory.Exists(left);
        bool rightFolder = right.Length > 0 && Directory.Exists(right);
        if (leftFolder || rightFolder)
        {
            bool otherSideFits = (leftFolder || left.Length == 0) && (rightFolder || right.Length == 0);
            return otherSideFits ? CompareKind.Folder : CompareKind.Text;
        }

        var files = new[] { left, right }.Where(p => p.Length > 0).ToArray();
        if (files.Length == 0) return CompareKind.Text;
        if (files.All(IsImage)) return CompareKind.Image;
        if (files.Any(p => File.Exists(p) && TextFileLoader.LooksBinary(p))) return CompareKind.Hex;
        return CompareKind.Text;
    }
}
