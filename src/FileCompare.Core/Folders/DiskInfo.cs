using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCompare.Core.Folders;

/// <summary>
/// Picks how many files a content compare reads at the same time. SSDs and network shares keep up with several reads
/// at once; a spinning disk gets slower when its head has to jump between files, so it gets one reader per disk.
/// </summary>
public static class DiskInfo
{
    public const int SsdParallelism = 4;
    public const int MaxParallelism = 16;

    /// <param name="requested">The user's choice: 0 for automatic, otherwise used as given (1 to 16).</param>
    public static int ParallelReads(string leftRoot, string rightRoot, int requested = 0)
    {
        if (requested > 0) return Math.Min(requested, MaxParallelism);
        if (!OperatingSystem.IsWindows()) return SsdParallelism;

        var left = Describe(leftRoot);
        var right = Describe(rightRoot);
        bool spinning = left.Spinning == true || right.Spinning == true;
        if (!spinning) return SsdParallelism;

        // Both folders on the same spinning disk: one file at a time. On two different disks, one reader each.
        bool sameDisk = left.DiskNumber is { } l && right.DiskNumber is { } r ? l == r : true;
        return sameDisk ? 1 : 2;
    }

    /// <summary>What is known about the disk a folder is on. Nulls mean it could not be found out (a network share, no access...).</summary>
    public readonly record struct Disk(bool? Spinning, int? DiskNumber);

    public static Disk Describe(string path)
    {
        if (!OperatingSystem.IsWindows()) return new Disk(null, null);

        string? root;
        try { root = Path.GetPathRoot(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return new Disk(null, null); }

        // Only local drives with a letter ("C:\"); network shares (\\server\share) are treated as fast.
        if (root is not { Length: 3 } || root[1] != ':') return new Disk(null, null);

        using var volume = CreateFile($@"\\.\{root[0]}:", 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (volume.IsInvalid) return new Disk(null, null);
        return new Disk(SeekPenalty(volume), DeviceNumber(volume));
    }

    private static bool? SeekPenalty(SafeFileHandle volume)
    {
        var query = new StoragePropertyQuery { PropertyId = StorageDeviceSeekPenaltyProperty, QueryType = PropertyStandardQuery };
        var result = new DeviceSeekPenaltyDescriptor();
        bool ok = DeviceIoControl(volume, IoctlStorageQueryProperty, ref query, Marshal.SizeOf<StoragePropertyQuery>(),
            out result, Marshal.SizeOf<DeviceSeekPenaltyDescriptor>(), out _, IntPtr.Zero);
        return ok ? result.IncursSeekPenalty != 0 : null;
    }

    private static int? DeviceNumber(SafeFileHandle volume)
    {
        bool ok = DeviceIoControl(volume, IoctlStorageGetDeviceNumber, IntPtr.Zero, 0,
            out StorageDeviceNumber number, Marshal.SizeOf<StorageDeviceNumber>(), out _, IntPtr.Zero);
        return ok ? (int)number.DeviceNumber : null;
    }

    // ----- Windows API -----

    private const uint FileShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;
    private const uint IoctlStorageQueryProperty = 0x2D1400;
    private const uint IoctlStorageGetDeviceNumber = 0x2D1080;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int PropertyStandardQuery = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceSeekPenaltyDescriptor
    {
        public uint Version;
        public uint Size;
        public byte IncursSeekPenalty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StorageDeviceNumber
    {
        public uint DeviceType;
        public uint DeviceNumber;
        public uint PartitionNumber;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, ref StoragePropertyQuery input, int inputSize,
        out DeviceSeekPenaltyDescriptor output, int outputSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, int inputSize,
        out StorageDeviceNumber output, int outputSize, out int returned, IntPtr overlapped);
}
