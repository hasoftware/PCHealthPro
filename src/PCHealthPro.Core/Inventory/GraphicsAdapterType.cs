using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PCHealthPro.Core.Inventory;

internal static class GraphicsAdapterType
{
    // Resolve the interface for this exact PnP instance, including render-only GPUs.
    private static readonly Guid DisplayInterface = new("5B45201D-F2F2-4F3B-85BB-30FF1F953599");

    internal static string? FromProduct(string? vendor, string? model, string? instance)
    {
        // Match the adapter, including on hybrid systems. Intel uses the Arc name
        // on both Meteor Lake integrated graphics and discrete graphics products.
        // PCI IDs: linux/include/drm/intel/pciids.h (MTL and DG2).
        var path = instance?.ToUpperInvariant().Split('\\');
        if (path is ["PCI", var hardware, ..])
        {
            var parts = hardware.Split('&');
            if (parts.Contains("VEN_8086"))
            {
                var device = Array.Find(parts, p => p.StartsWith("DEV_", StringComparison.Ordinal));
                if (device is "DEV_7D40" or "DEV_7D45" or "DEV_7D55" or "DEV_7D60" or "DEV_7DD5")
                    return "GpuIntegratedPci";
                if (device is "DEV_56A0" or "DEV_56A1" or "DEV_56A5") return "GpuDiscretePci";
            }
        }
        if (model == null) return null;
        var name = model.ToUpperInvariant();
        if (vendor == "NVIDIA" && (name.Contains("GEFORCE RTX") || name.Contains("GEFORCE GTX"))) return "GpuDiscreteModel";
        if (vendor == "Intel" && (name.Contains("UHD GRAPHICS") || name.Contains("IRIS XE GRAPHICS") ||
            name.Contains("IRIS PLUS") || name.Contains("HD GRAPHICS"))) return "GpuIntegratedModel";
        return null;
    }

    [SupportedOSPlatform("windows")]
    internal static string? Read(string? instance)
    {
        if (string.IsNullOrWhiteSpace(instance)) return null;
        if (instance.StartsWith(@"SWD\REMOTEDISPLAYENUM\", StringComparison.OrdinalIgnoreCase)) return "GpuVirtual";
        if (CM_Get_Device_Interface_List_SizeW(out var length, DisplayInterface, instance, 0) != 0 || length < 2) return null;
        var names = new char[length];
        if (CM_Get_Device_Interface_ListW(DisplayInterface, instance, names, length, 0) != 0) return null;
        foreach (var name in new string(names).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var open = new OpenAdapter { DeviceName = name };
            if (D3DKMTOpenAdapterFromDeviceName(ref open) != 0) continue;
            try
            {
                if (DXCoreCreateAdapterFactory(typeof(IFactory).GUID, out var factory) < 0) return null;
                try
                {
                    if (factory.GetAdapterByLuid(open.Luid, typeof(IAdapter).GUID, out var adapter) < 0) continue;
                    try
                    {
                        if (adapter.GetProperty(11, 1, out byte hardware) < 0) return null;
                        if (hardware == 0) return "GpuVirtual";
                        if (adapter.GetProperty(12, 1, out byte integrated) >= 0)
                            return integrated != 0 ? "GpuIntegrated" : "GpuDiscrete";
                    }
                    finally { Marshal.ReleaseComObject(adapter); }
                }
                finally { Marshal.ReleaseComObject(factory); }
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
            finally { D3DKMTCloseAdapter(ref open.Handle); }
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAdapter
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DeviceName;
        public uint Handle;
        // LUID consists of two 32-bit fields with 4-byte alignment.
        public Luid Luid;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public int High; }

    [ComImport, Guid("78EE5945-C36E-4B13-A669-005DD11C0F06"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFactory
    {
        [PreserveSig] int CreateAdapterList(uint count, in Guid attributes, in Guid iid, out IntPtr list);
        [PreserveSig] int GetAdapterByLuid(in Luid luid, in Guid iid, out IAdapter adapter);
    }
    [ComImport, Guid("F0DB4C7F-FE5A-42A2-BD62-F2A6CF6FC83E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAdapter
    {
        [PreserveSig] byte IsValid();
        [PreserveSig] byte IsAttributeSupported(in Guid attribute);
        [PreserveSig] byte IsPropertySupported(uint property);
        [PreserveSig] int GetProperty(uint property, nuint size, out byte value);
    }
    [DllImport("cfgmgr32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_List_SizeW(out uint size, in Guid category, string instance, uint flags);
    [DllImport("cfgmgr32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_ListW(in Guid category, string instance, [Out] char[] buffer, uint length, uint flags);
    [DllImport("gdi32")] private static extern int D3DKMTOpenAdapterFromDeviceName(ref OpenAdapter adapter);
    [DllImport("gdi32")] private static extern int D3DKMTCloseAdapter(ref uint handle);
    [DllImport("dxcore")] private static extern int DXCoreCreateAdapterFactory(in Guid iid, out IFactory factory);
}
