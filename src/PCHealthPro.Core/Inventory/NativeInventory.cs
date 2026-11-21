using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace PCHealthPro.Core.Inventory;

internal static class NativeInventory
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }

    // Fields consumed from the Windows SDK DEVMODEW display variant.
    [StructLayout(LayoutKind.Explicit, Size = 220)]
    private struct DisplayMode
    {
        [FieldOffset(68)] public ushort Size;
        [FieldOffset(172)] public uint Width;
        [FieldOffset(176)] public uint Height;
        [FieldOffset(184)] public uint Frequency;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice info, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string device, int modeIndex, ref DisplayMode mode);

    [SupportedOSPlatform("windows")]
    public static List<DeviceRow> Displays()
    {
        var rows = new List<DeviceRow>();
        for (uint index = 0; ; index++)
        {
            var display = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref display, 0)) break;
            if ((display.Flags & 1) == 0) continue;
            var mode = new DisplayMode { Size = (ushort)Marshal.SizeOf<DisplayMode>() };
            if (!EnumDisplaySettings(display.Name, -1, ref mode)) continue;
            DeviceRow Output() => new() { ["DisplayName"] = display.Name, ["Resolution"] = $"{mode.Width} × {mode.Height}",
                ["RefreshHz"] = mode.Frequency > 1 ? mode.Frequency : null };
            bool found = false;
            for (uint child = 0; ; child++)
            {
                var panel = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                if (!EnumDisplayDevices(display.Name, child, ref panel, 1)) break;
                if ((panel.Flags & 1) == 0) continue;
                found = true;
                var output = Output();
                output["MonitorPath"] = panel.Id;
                rows.Add(output);
                // EDD_GET_DEVICE_INTERFACE_NAME yields DISPLAY#vendor#instance#{guid}.
                // Match the active interface before reading its cached EDID.
                var parts = panel.Id.Split('#');
                if (parts.Length != 4 || !parts[0].EndsWith(@"\DISPLAY", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var path = $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters";
                    if (Registry.GetValue(path, "EDID", null) is byte[] edid) DecodeEdid(edid, output);
                }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
                catch (IOException) { }
            }
            if (!found) rows.Add(Output());
        }
        return rows;
    }

    internal static void DecodeEdid(byte[] data, DeviceRow row)
    {
        ReadOnlySpan<byte> header = [0, 255, 255, 255, 255, 255, 255, 0];
        if (data.Length < 128 || !data.AsSpan(0, 8).SequenceEqual(header) ||
            data.Take(128).Sum(b => b) % 256 != 0) return;
        if (data[21] > 0 && data[22] > 0)
        {
            row["MaxHorizontalImageSize"] = data[21];
            row["MaxVerticalImageSize"] = data[22];
        }
        for (int offset = 54; offset <= 108; offset += 18)
        {
            if (data[offset] == 0 && data[offset + 1] == 0 && data[offset + 2] == 0 && data[offset + 3] == 0xfc)
                row["PanelName"] = Encoding.ASCII.GetString(data, offset + 5, 13).Trim('\0', '\r', '\n', ' ');
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterface
    {
        public Guid Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public int State;
    }

    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, nint reserved, out uint negotiated, out nint client);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(nint client, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] private static extern uint WlanGetInterfaceCapability(nint client, ref Guid id, nint reserved, out nint capability);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(nint memory);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(nint client, nint reserved);

    public static List<DeviceRow> Wifi()
    {
        var rows = new List<DeviceRow>();
        var error = WlanOpenHandle(2, 0, out _, out var client);
        if (error != 0) throw new Win32Exception((int)error);
        try
        {
            error = WlanEnumInterfaces(client, 0, out var list);
            if (error != 0) throw new Win32Exception((int)error);
            try
            {
                // WLAN_INTERFACE_INFO_LIST starts with two DWORDs followed by its array.
                for (var i = 0; i < Marshal.ReadInt32(list); i++)
                {
                    var wlan = Marshal.PtrToStructure<WlanInterface>(list + 8 + i * Marshal.SizeOf<WlanInterface>());
                    var row = new DeviceRow { ["InterfaceDescription"] = wlan.Description };
                    rows.Add(row);
                    if (WlanGetInterfaceCapability(client, ref wlan.Id, 0, out var cap) != 0) continue;
                    try
                    {
                        // dwNumberOfSupportedPhys precedes DOT11_PHY_TYPE[64].
                        var count = Math.Clamp(Marshal.ReadInt32(cap, 16), 0, 64);
                        string[] names = ["", "FHSS", "DSSS", "IR", "802.11a", "802.11b", "802.11g",
                            "802.11n", "802.11ac", "802.11ad", "802.11ax", "802.11be"];
                        var phys = Enumerable.Range(0, count).Select(n => Marshal.ReadInt32(cap, 20 + n * 4));
                        row["RadioTypes"] = string.Join(", ", phys.Where(p => p > 0 && p < names.Length).Distinct().Select(p => names[p]));
                    }
                    finally { WlanFreeMemory(cap); }
                }
            }
            finally { WlanFreeMemory(list); }
        }
        finally { WlanCloseHandle(client, 0); }
        return rows;
    }
}
