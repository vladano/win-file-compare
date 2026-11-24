using System.Globalization;
using System.Text;

namespace FileCompare.Core.Binary;

/// <summary>A file is too large for the hex compare, which keeps both files in memory.</summary>
public sealed class FileTooLargeException : IOException
{
    public FileTooLargeException(string path, long size)
        : base($"{Path.GetFileName(path)} is {size / (1024 * 1024):N0} MB. The hex compare handles files up to " +
               $"{HexComparison.MaxFileSize / (1024 * 1024):N0} MB.")
    {
    }
}

/// <summary>
/// Two files compared byte by byte at the same offsets (no insertions are detected, like most hex compares).
/// Bytes that exist in one file only count as different.
/// </summary>
public sealed class HexComparison
{
    public const int BytesPerRow = 16;
    public const long MaxFileSize = 256L * 1024 * 1024;

    private readonly bool[] _rowDiffers;

    private HexComparison(string leftPath, byte[] left, string rightPath, byte[] right)
    {
        LeftPath = leftPath;
        RightPath = rightPath;
        Left = left;
        Right = right;

        int length = Math.Max(left.Length, right.Length);
        RowCount = (length + BytesPerRow - 1) / BytesPerRow;
        _rowDiffers = new bool[RowCount];

        long differentBytes = 0;
        var starts = new List<int>();
        for (int row = 0; row < RowCount; row++)
        {
            int start = row * BytesPerRow;
            int size = Math.Min(BytesPerRow, length - start);
            int inLeft = Math.Clamp(left.Length - start, 0, size);
            int inRight = Math.Clamp(right.Length - start, 0, size);
            int common = Math.Min(inLeft, inRight);

            int different = size - common; // bytes on one side only
            // Past the end of the shorter file there is nothing in common (and AsSpan would throw for a start beyond its end).
            var a = common > 0 ? left.AsSpan(start, common) : ReadOnlySpan<byte>.Empty;
            var b = common > 0 ? right.AsSpan(start, common) : ReadOnlySpan<byte>.Empty;
            if (!a.SequenceEqual(b))
            {
                for (int i = 0; i < common; i++)
                    if (a[i] != b[i]) different++;
            }

            if (different > 0)
            {
                _rowDiffers[row] = true;
                differentBytes += different;
                if (row == 0 || !_rowDiffers[row - 1]) starts.Add(row);
            }
        }

        DifferentBytes = differentBytes;
        SectionStarts = starts;
    }

    public string LeftPath { get; }
    public string RightPath { get; }
    public byte[] Left { get; }
    public byte[] Right { get; }

    public int RowCount { get; }
    public long DifferentBytes { get; }

    /// <summary>First row of each run of rows that contain differences.</summary>
    public IReadOnlyList<int> SectionStarts { get; }

    public int DifferenceCount => SectionStarts.Count;
    public bool AreSame => DifferentBytes == 0;

    public bool RowDiffers(int row) => row >= 0 && row < RowCount && _rowDiffers[row];

    /// <summary>True when the byte at <paramref name="offset"/> differs, or exists in one file only.</summary>
    public bool ByteDiffers(long offset)
    {
        bool inLeft = offset < Left.Length, inRight = offset < Right.Length;
        if (inLeft != inRight) return true;
        return inLeft && Left[offset] != Right[offset];
    }

    /// <exception cref="FileTooLargeException">A file is larger than <see cref="MaxFileSize"/>.</exception>
    /// <exception cref="IOException">A file could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">A file is not accessible.</exception>
    public static HexComparison Load(string leftPath, string rightPath) =>
        new(leftPath, Read(leftPath), rightPath, Read(rightPath));

    public static HexComparison FromBytes(string leftPath, byte[] left, string rightPath, byte[] right) =>
        new(leftPath, left, rightPath, right);

    private static byte[] Read(string path)
    {
        if (path.Length == 0) return Array.Empty<byte>(); // that side is still to be chosen

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxFileSize) throw new FileTooLargeException(path, stream.Length);

        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}

/// <summary>Text for one row of the hex view.</summary>
public static class HexFormat
{
    /// <summary>Characters in the hex part of a row: 16 bytes of "XX " plus one extra space after the 8th byte.</summary>
    public const int HexWidth = HexComparison.BytesPerRow * 3;

    public static string Offset(long offset) => offset.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>Where the hex digits of a byte start, counted in characters.</summary>
    public static int HexColumn(int byteIndex) => byteIndex * 3 + (byteIndex >= HexComparison.BytesPerRow / 2 ? 1 : 0);

    /// <summary>"4D 5A 90 00 ...". Bytes past the end of the file are blank, so both sides line up.</summary>
    public static string HexText(byte[] data, int rowStart)
    {
        var text = new StringBuilder(new string(' ', HexWidth));
        for (int i = 0; i < HexComparison.BytesPerRow; i++)
        {
            int offset = rowStart + i;
            if (offset >= data.Length) break;
            string hex = data[offset].ToString("X2", CultureInfo.InvariantCulture);
            int column = HexColumn(i);
            text[column] = hex[0];
            text[column + 1] = hex[1];
        }
        return text.ToString();
    }

    /// <summary>Printable ASCII as is, everything else as '.', blanks past the end of the file.</summary>
    public static string AsciiText(byte[] data, int rowStart)
    {
        var text = new char[HexComparison.BytesPerRow];
        for (int i = 0; i < text.Length; i++)
        {
            int offset = rowStart + i;
            text[i] = offset >= data.Length ? ' ' : data[offset] is >= 0x20 and < 0x7F ? (char)data[offset] : '.';
        }
        return new string(text);
    }
}
