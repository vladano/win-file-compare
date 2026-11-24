using System.Buffers;
using Microsoft.Win32.SafeHandles;

namespace FileCompare.Core.Folders;

internal static class FileContent
{
    /// <summary>How much is read at once.</summary>
    internal const int ChunkSize = 1024 * 1024;

    /// <summary>The start and the end of a file are checked first; most different files differ there.</summary>
    internal const int EdgeSize = 64 * 1024;

    /// <summary>The outcome of comparing two listed files.</summary>
    public enum Outcome { Same, Different, Changed }

    /// <summary>
    /// Compares two files byte by byte. "Same" is only given when both files still have the size and time they were
    /// listed with once they are read; otherwise a file was written to while it was compared ("Changed").
    /// </summary>
    /// <param name="bytesRead">Bytes read from both files are added here as the work goes on (for progress).</param>
    public static Outcome Compare(FileSnapshot left, FileSnapshot right, CancellationToken ct = default, ByteCounter? bytesRead = null)
    {
        using var a = Open(left.FullPath);
        using var b = Open(right.FullPath);
        if (!AreEqual(a, b, ct, bytesRead)) return Outcome.Different;

        // Read from the open handles: no second look-up of the files by name.
        bool unchanged = IsAsListed(a, left) && IsAsListed(b, right);
        return unchanged ? Outcome.Same : Outcome.Changed;
    }

    /// <summary>Compares two files byte by byte. Files that other programs have open can still be read.</summary>
    public static bool AreEqual(string pathA, string pathB, CancellationToken ct = default, ByteCounter? bytesRead = null)
    {
        using var a = Open(pathA);
        using var b = Open(pathB);
        return AreEqual(a, b, ct, bytesRead);
    }

    private static SafeFileHandle Open(string path) =>
        File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);

    private static bool IsAsListed(SafeFileHandle handle, FileSnapshot listed) =>
        RandomAccess.GetLength(handle) == listed.Length && File.GetLastWriteTimeUtc(handle) == listed.LastWriteTimeUtc;

    private static bool AreEqual(SafeFileHandle a, SafeFileHandle b, CancellationToken ct, ByteCounter? bytesRead)
    {
        long length = RandomAccess.GetLength(a);
        if (length != RandomAccess.GetLength(b)) return false;
        if (length == 0) return true;

        var bufferA = ArrayPool<byte>.Shared.Rent(ChunkSize);
        var bufferB = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            // Small files: one pass. Larger ones: the start, then the end, then the middle.
            if (length <= 4L * EdgeSize) return Same(a, b, 0, length, bufferA, bufferB, ct, bytesRead);

            return Same(a, b, 0, EdgeSize, bufferA, bufferB, ct, bytesRead)
                   && Same(a, b, length - EdgeSize, EdgeSize, bufferA, bufferB, ct, bytesRead)
                   && Same(a, b, EdgeSize, length - 2L * EdgeSize, bufferA, bufferB, ct, bytesRead);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bufferA);
            ArrayPool<byte>.Shared.Return(bufferB);
        }
    }

    /// <summary>Compares <paramref name="count"/> bytes of both files from <paramref name="offset"/>.</summary>
    private static bool Same(SafeFileHandle a, SafeFileHandle b, long offset, long count, byte[] bufferA, byte[] bufferB,
        CancellationToken ct, ByteCounter? bytesRead)
    {
        long end = offset + count;
        while (offset < end)
        {
            ct.ThrowIfCancellationRequested();
            int want = (int)Math.Min(ChunkSize, end - offset);
            int readA = ReadFully(a, bufferA.AsSpan(0, want), offset);
            int readB = ReadFully(b, bufferB.AsSpan(0, want), offset);
            bytesRead?.Add(readA + readB);

            // A short read means the file got shorter after it was opened: it changed, so it is not the same.
            if (readA != want || readB != want) return false;
            if (!bufferA.AsSpan(0, want).SequenceEqual(bufferB.AsSpan(0, want))) return false;
            offset += want;
        }
        return true;
    }

    private static int ReadFully(SafeFileHandle handle, Span<byte> buffer, long offset)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = RandomAccess.Read(handle, buffer[total..], offset + total);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}

/// <summary>A byte count that several threads add to.</summary>
public sealed class ByteCounter
{
    private long _value;

    public long Value => Interlocked.Read(ref _value);

    public void Add(long bytes) => Interlocked.Add(ref _value, bytes);
}
