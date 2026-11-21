using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PCHealthPro.Core.Benchmark;

internal static class BenchmarkFile
{
    internal const string Prefix = "PCHealthPro-bench-";
    private const uint NoBuffering = 0x20000000, WriteThrough = 0x80000000, Overlapped = 0x40000000,
        DeleteOnClose = 0x04000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security,
        uint disposition, uint attributes, nint template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong total, out ulong free);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, StringBuilder volume, uint length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass,
        out StorageInfo info, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct StorageInfo
    {
        public uint Logical, PhysicalAtomic, PhysicalPerformance, EffectiveAtomic, Flags, SectorOffset, PartitionOffset;
    }

    internal static void CheckSpace(ulong available, long length)
    {
        if (available < (ulong)length + 16 * 1024 * 1024)
            throw new BenchmarkException("BenchNoSpace");
    }

    internal static SafeFileHandle Create(string directory, long length, out int alignment)
    {
        var path = Path.GetFullPath(directory);
        var root = Path.GetPathRoot(path);
        if (root == null || new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Removable))
            throw new BenchmarkException("BenchLocalOnly");
        if (!GetDiskFreeSpaceEx(path, out var available, out _, out _)) throw Failure();
        CheckSpace(available, length);
        // CREATE_NEW and zero sharing: a collision fails. The kernel deletes this exact
        // file on the last handle close, including when the process terminates.
        var handle = CreateFile(Path.Combine(path, $"{Prefix}{Guid.NewGuid():N}.tmp"),
            0xC0000000, 0, 0, 1, NoBuffering | WriteThrough | Overlapped | DeleteOnClose, 0);
        if (handle.IsInvalid) { var error = Failure(); handle.Dispose(); throw error; }
        try
        {
            // A folder under a drive letter can be a junction to another partition.
            // Validate the actual open handle before extending or writing the file.
            var volume = new StringBuilder(64);
            var finalPath = new StringBuilder(32768);
            if (!GetVolumeNameForVolumeMountPoint(root, volume, (uint)volume.Capacity)) throw Failure();
            uint chars = GetFinalPathNameByHandle(handle, finalPath, (uint)finalPath.Capacity, 1);
            if (chars == 0 || chars >= finalPath.Capacity ||
                !finalPath.ToString().StartsWith(volume.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new BenchmarkException("BenchLocalOnly");
            if (!GetFileInformationByHandleEx(handle, 16, out var storage, (uint)Marshal.SizeOf<StorageInfo>()))
                throw Failure();
            if (storage.Logical == 0 || 4096 % storage.Logical != 0)
                throw new BenchmarkException("BenchAlignment");
            uint boundary = Math.Max(storage.Logical, Math.Max(storage.PhysicalPerformance, storage.PhysicalAtomic));
            if (boundary > 1024 * 1024 || (boundary & (boundary - 1)) != 0)
                throw new BenchmarkException("BenchAlignment");
            alignment = (int)Math.Max(4096u, boundary);
            RandomAccess.SetLength(handle, length);
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    internal static Memory<byte> Buffer(int size, int alignment)
    {
        var array = GC.AllocateUninitializedArray<byte>(size + alignment - 1, pinned: true);
        long address = Marshal.UnsafeAddrOfPinnedArrayElement(array, 0).ToInt64();
        int skip = (int)((-address) & (alignment - 1));
        return array.AsMemory(skip, size);
    }

    private static Win32Exception Failure() => new(Marshal.GetLastWin32Error());
}
