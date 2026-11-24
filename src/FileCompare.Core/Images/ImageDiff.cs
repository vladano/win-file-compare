namespace FileCompare.Core.Images;

/// <summary>Pixels as 32-bit BGRA (blue, green, red, alpha), row by row with no padding.</summary>
public sealed record PixelImage
{
    public PixelImage(int width, int height, byte[] bgra)
    {
        if (width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width), "The size cannot be negative.");
        if (bgra.Length != (long)width * height * 4)
            throw new ArgumentException($"Expected {(long)width * height * 4} bytes for {width} x {height} pixels, got {bgra.Length}.", nameof(bgra));

        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }

    public static PixelImage Empty { get; } = new(0, 0, Array.Empty<byte>());
}

/// <param name="Mask">An image the size of the larger picture: changed pixels red, pixels on one side only orange,
/// equal pixels faint (mostly transparent) so the picture can still be recognized.</param>
public sealed record ImageDiffResult(bool SameSize, long DifferentPixels, long TotalPixels, PixelImage Mask)
{
    public bool AreSame => SameSize && DifferentPixels == 0;

    public double DifferentPercent => TotalPixels == 0 ? 0 : DifferentPixels * 100.0 / TotalPixels;
}

/// <summary>Compares two images pixel by pixel, aligned at the top left corner.</summary>
public static class ImageDiff
{
    /// <param name="tolerance">How far a color channel may be off (0 to 255) and still count as equal.</param>
    public static ImageDiffResult Compare(PixelImage a, PixelImage b, int tolerance = 0)
    {
        int width = Math.Max(a.Width, b.Width), height = Math.Max(a.Height, b.Height);
        var mask = new byte[(long)width * height * 4];
        long different = 0, total = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool inA = x < a.Width && y < a.Height, inB = x < b.Width && y < b.Height;
                if (!inA && !inB) continue; // outside both pictures (one is wider, the other taller)
                total++;

                long m = ((long)y * width + x) * 4;
                if (inA && inB)
                {
                    long ia = ((long)y * a.Width + x) * 4, ib = ((long)y * b.Width + x) * 4;
                    if (Differs(a.Bgra, ia, b.Bgra, ib, tolerance))
                    {
                        different++;
                        Set(mask, m, 40, 40, 230, 255); // red
                    }
                    else
                    {
                        Set(mask, m, a.Bgra[ia], a.Bgra[ia + 1], a.Bgra[ia + 2], 50); // faint original
                    }
                }
                else
                {
                    different++;
                    Set(mask, m, 0, 150, 255, 255); // orange: on one side only
                }
            }
        }

        bool sameSize = a.Width == b.Width && a.Height == b.Height;
        return new ImageDiffResult(sameSize, different, total, new PixelImage(width, height, mask));
    }

    private static bool Differs(byte[] a, long ia, byte[] b, long ib, int tolerance)
    {
        for (int c = 0; c < 4; c++)
            if (Math.Abs(a[ia + c] - b[ib + c]) > tolerance) return true;
        return false;
    }

    private static void Set(byte[] pixels, long index, byte blue, byte green, byte red, byte alpha)
    {
        pixels[index] = blue;
        pixels[index + 1] = green;
        pixels[index + 2] = red;
        pixels[index + 3] = alpha;
    }
}
