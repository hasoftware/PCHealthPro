using System.Runtime.InteropServices;

namespace PCHealthPro.Core.Inventory;

internal static class WindowsLicense
{
    private static readonly Guid Application = new("55c92734-d682-4d71-983e-d6ec3f16059f");

    internal static DeviceRow Read()
    {
        var row = new DeviceRow();
        if (SLIsGenuineLocal(Application, out uint genuine, IntPtr.Zero) == 0) row["GenuineState"] = genuine;
        if (SLOpen(out var context) != 0) return row;
        try
        {
            if (SLGetLicensingStatusInformation(context, Application, IntPtr.Zero, IntPtr.Zero, out uint count, out var pointer) != 0) return row;
            try
            {
                var statuses = new HashSet<int>();
                for (int i = 0; i < count; i++)
                    statuses.Add(Marshal.PtrToStructure<LicenseStatus>(pointer + i * Marshal.SizeOf<LicenseStatus>()).Status);
                if (statuses.Count == 1) row["LicenseStatus"] = statuses.Single() switch { 0 => 0, 1 => 1, 2 => 2, 3 => 5, _ => (int?)null };
            }
            finally { LocalFree(pointer); }
        }
        finally { SLClose(context); }
        return row;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LicenseStatus
    {
        public Guid Sku;
        public int Status;
        public uint GraceMinutes, GraceDays;
        public int Reason;
        public ulong Expiration;
    }
    [DllImport("slwga.dll")] private static extern int SLIsGenuineLocal(in Guid application, out uint state, IntPtr ui);
    [DllImport("slc.dll")] private static extern int SLOpen(out IntPtr context);
    [DllImport("slc.dll")] private static extern int SLClose(IntPtr context);
    [DllImport("slc.dll")] private static extern int SLGetLicensingStatusInformation(IntPtr context, in Guid application, IntPtr sku, IntPtr right, out uint count, out IntPtr statuses);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
