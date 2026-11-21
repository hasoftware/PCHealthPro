using System.Globalization;

namespace PCHealthPro.Core.Inventory;

internal static class InventoryFormat
{
    internal static string Bytes(ulong bytes) => $"{bytes / 1073741824d:0.##} GiB";

    internal static InventoryField[] Fields(InventoryQuery query, DeviceRow row) =>
        query.Columns.Split(',').Where(name => name != "PNPDeviceID")
            .Select(name => query.Label == "MemoryArray" && name == "MemoryDevices"
                ? new InventoryField("FirmwareDeviceCount", row.Text(name)) : Format(name, row)).ToArray();

    private static string? Chassis(ushort[] codes)
    {
        if (codes.Any(n => n is 8 or 9 or 10 or 14 or 30 or 31 or 32)) return "Mobile";
        return codes.Any(n => n is 3 or 4 or 5 or 6 or 7 or 13 or 15 or 16 or 35 or 36) ? "Desktop" : null;
    }

    private static InventoryField Format(string name, DeviceRow row)
    {
        var text = row.Text(name);
        var n = row.Number(name);
        string? WithUnit(string unit) => n is > 0 ? $"{n} {unit}" : null;
        string? value = name switch
        {
            "Capacity" or "AdapterRAM" or "Size" => n is > 0 ? Bytes(n.Value) : null,
            "FormFactor" => n switch { 8 => "DIMM", 12 => "SO-DIMM", 21 => "BGA", > 0 => text, _ => null },
            "CurrentClockSpeed" or "MaxClockSpeed" or "NominalMHz" or "BaseClock" => n > 0 ? $"{n / 1000d:0.###} GHz" : null,
            "RefreshHz" or "CurrentRefreshRate" => WithUnit("Hz"),
            "MaxHorizontalImageSize" or "MaxVerticalImageSize" => WithUnit("cm"),
            "L2CacheSize" or "L3CacheSize" => WithUnit("KiB"),
            "ConfiguredClockSpeed" => WithUnit("MT/s"),
            "EstimatedChargeRemaining" => n <= 100 ? $"{n} %" : null,
            "ReceiveLinkSpeed" or "TransmitLinkSpeed" => n is > 0 and < ulong.MaxValue ? $"{n / 1000000d:0.##} Mb/s" : null,
            "Architecture" => n switch { 0 => "x86", 5 => "ARM", 6 => "Itanium", 9 => "x64", 12 => "ARM64", _ => null },
            "InterfaceType" => n switch { 6 => "Ethernet", 71 => "Wi-Fi", _ => text },
            "SMBIOSMemoryType" => n switch { 18 => "DDR", 19 => "DDR2", 24 => "DDR3", 26 => "DDR4",
                27 => "LPDDR", 28 => "LPDDR2", 29 => "LPDDR3", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => null },
            "InstallDate" or "ReleaseDate" => text?.Length >= 8 && DateOnly.TryParseExact(text[..8], "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.ToString("yyyy-MM-dd") : null,
            "UserFriendlyName" or "ProductCodeID" or "ManufacturerName" => row.GetValueOrDefault(name) is ushort[] chars
                ? new string(chars.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim() : null,
            "PartialProductKey" => text?.Length == 5 ? text : null,
            "GenuineState" => n switch { 0 => "GenuineLocal", 1 => "GenuineInvalid", 2 => "GenuineTampered", 3 => "GenuineOffline", _ => null },
            "LicenseStatus" => n switch { 0 => "Unlicensed", 1 => "Licensed", 2 or 3 or 6 => "Grace", 4 => "NonGenuineGrace", 5 => "Notification", _ => null },
            "ChassisTypes" => row.GetValueOrDefault(name) is ushort[] codes ? Chassis(codes) : null,
            "PCSystemType" => n switch { 1 => "Desktop", 2 => "Mobile", 3 => "Workstation", 4 or 5 or 7 => "Server", _ => null },
            "SecureBootState" => n switch { 0 => "Disabled", 1 => "Enabled", _ => null },
            "IsEnabled_InitialValue" => row.GetValueOrDefault(name) switch { true => "Enabled", false => "Disabled", _ => null },
            "CurrentHorizontalResolution" or "CurrentVerticalResolution" => WithUnit("px"),
            _ => text
        };
        return new(name, string.IsNullOrWhiteSpace(value) ? null : value)
            { Localize = name is "ChassisTypes" or "GpuType" or "GenuineState" or "LicenseStatus" or "PCSystemType" or "SecureBootState" or "IsEnabled_InitialValue" };
    }
}
