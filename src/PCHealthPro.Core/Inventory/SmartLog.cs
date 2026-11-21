using System.Buffers.Binary;
using System.Numerics;

namespace PCHealthPro.Core.Inventory;

internal static class SmartLog
{
    // NVMe SMART / Health log 02h. Lifetime counters are unsigned 128-bit
    // little-endian. One Data Unit Written represents 1,000 units of 512 bytes.
    internal static void Nvme(ReadOnlySpan<byte> page, DeviceRow row)
    {
        if (page.Length < 512 || page.IndexOfAnyExcept((byte)0) < 0 ||
            page.IndexOfAnyExcept((byte)255) < 0) return;
        row["CriticalWarning"] = page[0];
        var kelvin = BinaryPrimitives.ReadUInt16LittleEndian(page[1..]);
        if (kelvin is > 0 and < 65535) row["Temperature"] = kelvin - 273;
        row["Wear"] = page[5];
        row["BytesWritten"] = new BigInteger(page.Slice(48, 16), true) * 512000;
        row["PowerCycles"] = new BigInteger(page.Slice(112, 16), true);
        row["PowerOnHours"] = new BigInteger(page.Slice(128, 16), true);
        row["MediaErrors"] = new BigInteger(page.Slice(160, 16), true);
    }

    // ATA Device Statistics (GPL 04h). Bits 63/62 indicate supported/valid.
    internal static void AtaStatistics(ReadOnlySpan<byte> data, int page, uint sectorSize, DeviceRow row)
    {
        if (data.Length < 512 || data[2] != page || BinaryPrimitives.ReadUInt16LittleEndian(data) != 1) return;
        if (page == 1)
        {
            SetStatistic(row, "PowerCycles", data[8..], uint.MaxValue);
            SetStatistic(row, "PowerOnHours", data[16..], uint.MaxValue);
            if (sectorSize > 0 && Statistic(data[24..], 0xffffffffffff) is ulong sectors)
                row["BytesWritten"] = (BigInteger)sectors * sectorSize;
        }
        if (page == 5) SetStatistic(row, "Temperature", data[8..], 0xff);
        if (page == 7) SetStatistic(row, "Wear", data[8..], 0xff);
    }

    private static ulong? Statistic(ReadOnlySpan<byte> slot, ulong mask)
    {
        ulong entry = BinaryPrimitives.ReadUInt64LittleEndian(slot);
        return entry >> 62 == 3 ? entry & mask : null;
    }

    private static void SetStatistic(DeviceRow row, string key, ReadOnlySpan<byte> slot, ulong mask)
    {
        if (Statistic(slot, mask) is ulong value) row[key] = value;
    }

    internal static void AtaAttributes(ReadOnlySpan<byte> page, DeviceRow row, string? model)
    {
        if (page.Length < 512 || page[0] == 0 || page[0] == 255) return;
        for (int offset = 2; offset < 362; offset += 12)
        {
            // Seagate ST-series HDDs may pack a sub-hour counter above the
            // low DWORD. Other vendors can use different time units for ID 9.
            if (page[offset] == 9 && model?.StartsWith("ST", StringComparison.OrdinalIgnoreCase) == true &&
                model.Length > 2 && char.IsAsciiDigit(model[2]))
                row["PowerOnHours"] = BinaryPrimitives.ReadUInt32LittleEndian(page[(offset + 5)..]);
            if (page[offset] == 194 && page[offset + 5] is > 0 and < 128)
                row["Temperature"] = page[offset + 5];
            var key = page[offset] switch { 5 => "Reallocated", 12 => "PowerCycles", 197 => "PendingSectors", _ => null };
            if (key != null)
            {
                ulong raw = 0;
                for (int i = 0; i < 6; i++) raw |= (ulong)page[offset + 5 + i] << (8 * i);
                row[key] = raw;
            }
        }
    }
}
