using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace PCHealthPro.Core.Inventory;

internal static class StorageDeviceReader
{
    private const uint QueryProperty = 0x002D1400;
    private const uint PredictFailure = 0x002D1100;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize,
        [Out] byte[] output, int outputSize, out int returned, nint overlapped);

    internal static DeviceRow Read(uint index, uint sectorSize, CancellationToken token)
    {
        var row = new DeviceRow();
        // Desired access 0 is sufficient for IOCTL_STORAGE_QUERY_PROPERTY.
        using var handle = CreateFile($@"\\.\PhysicalDrive{index}", 0, 3, 0, 3, 0, 0);
        if (handle.IsInvalid) return row;
        byte[] descriptor = new byte[1024];
        if (!DeviceIoControl(handle, QueryProperty, new byte[12], 12, descriptor, descriptor.Length, out int size, 0) || size < 36)
            return row;
        var descriptorLength = size;
        uint bus = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(28));
        row["BusType"] = bus;
        if (bus == 17)
        {
            var page = ReadLog(handle, 3, 2, 0);
            SmartLog.Nvme(page, row);
        }
        if (bus is 3 or 11)
        {
            byte[] smart = new byte[516];
            if (DeviceIoControl(handle, PredictFailure, [], 0, smart, smart.Length, out size, 0) && size >= 516)
            {
                row["PredictFailure"] = BitConverter.ToUInt32(smart) != 0;
                uint offset = BitConverter.ToUInt32(descriptor, 16); // ProductIdOffset
                string? model = null;
                if (offset > 0 && offset < descriptorLength)
                {
                    int end = Array.IndexOf(descriptor, (byte)0, (int)offset, descriptorLength - (int)offset);
                    if (end >= 0) model = Encoding.ASCII.GetString(descriptor, (int)offset, end - (int)offset).Trim();
                }
                SmartLog.AtaAttributes(smart.AsSpan(4), row, model);
            }
            foreach (int page in new[] { 1, 5, 7 })
            {
                token.ThrowIfCancellationRequested();
                SmartLog.AtaStatistics(ReadLog(handle, 2, 4, page), page, sectorSize, row);
            }
        }
        return row;
    }

    private static byte[] ReadLog(SafeFileHandle handle, int protocol, int log, int page)
    {
        // STORAGE_PROPERTY_QUERY (8 bytes) + STORAGE_PROTOCOL_SPECIFIC_DATA
        // (40 bytes) + log payload. The returned offset is relative to the
        // protocol structure, not to the beginning of the descriptor.
        var input = new byte[560];
        void Set(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(offset), value);
        Set(0, 50); // StorageDeviceProtocolSpecificProperty
        Set(8, protocol);
        Set(12, 2); // Log page
        Set(16, log);
        Set(20, page);
        Set(24, 40);
        Set(28, 512);
        var output = new byte[input.Length];
        if (!DeviceIoControl(handle, QueryProperty, input, input.Length, output, output.Length, out var count, 0) ||
            count < 48 || BitConverter.ToUInt32(output) != 48 || BitConverter.ToUInt32(output, 4) != 48) return [];
        uint offset = BitConverter.ToUInt32(output, 24);
        if (offset < 40 || BitConverter.ToUInt32(output, 28) < 512 || offset + 520L > count) return [];
        return output.AsSpan((int)offset + 8, 512).ToArray();
    }
}
