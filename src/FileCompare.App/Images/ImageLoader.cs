using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileCompare.Core.Images;

namespace FileCompare.App.Images;

/// <summary>A picture read from disk: its pixels for comparing, and a bitmap for showing.</summary>
public sealed record LoadedImage(
    string Path, PixelImage Pixels, BitmapSource Bitmap, string Format, long SizeBytes, DateTime LastWriteTime, int FrameCount);

/// <summary>Reads pictures with the decoders built into Windows (PNG, JPEG, BMP, GIF, TIFF, ICO, JPEG XR).</summary>
public static class ImageLoader
{
    /// <summary>Larger pictures are refused: both are kept in memory as 32-bit pixels, plus the difference mask.</summary>
    public const long MaxPixels = 100_000_000;

    /// <summary>Reads the first frame of a picture. Safe to call on a background thread; the bitmap is frozen.</summary>
    /// <exception cref="IOException">The file could not be read, or the picture is too large.</exception>
    /// <exception cref="UnauthorizedAccessException">The file is not accessible.</exception>
    /// <exception cref="NotSupportedException">Windows has no decoder for this format.</exception>
    /// <exception cref="FileFormatException">The file is damaged or not a picture.</exception>
    public static LoadedImage Load(string path)
    {
        var info = new FileInfo(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        int width = frame.PixelWidth, height = frame.PixelHeight;
        if ((long)width * height > MaxPixels)
            throw new IOException($"{System.IO.Path.GetFileName(path)} is {width:N0} x {height:N0} pixels. " +
                                  $"The image compare handles up to {MaxPixels / 1_000_000:N0} megapixels.");

        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int stride = width * 4;
        var pixels = new byte[(long)stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        var bitmap = ToBitmap(new PixelImage(width, height, pixels));
        string format = decoder.CodecInfo?.FriendlyName?.Replace(" Decoder", "", StringComparison.OrdinalIgnoreCase)
                        ?? System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();

        return new LoadedImage(path, new PixelImage(width, height, pixels), bitmap, format, info.Length, info.LastWriteTime,
            decoder.Frames.Count);
    }

    /// <summary>A frozen 96 DPI bitmap, so one image pixel is one device-independent pixel on screen.</summary>
    public static BitmapSource ToBitmap(PixelImage image)
    {
        if (image.Width == 0 || image.Height == 0)
        {
            var empty = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            empty.Freeze();
            return empty;
        }

        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Bgra, image.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
