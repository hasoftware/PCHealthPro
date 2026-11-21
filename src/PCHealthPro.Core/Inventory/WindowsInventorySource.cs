using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PCHealthPro.Core.Inventory;

[SupportedOSPlatform("windows")]
public sealed class WindowsInventorySource : IInventorySource
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    private readonly Func<InventoryQuery, CancellationToken, IReadOnlyList<DeviceRow>> read;
    private readonly TimeSpan budget;
    public WindowsInventorySource() : this(Read, Timeout) { }

    internal WindowsInventorySource(Func<InventoryQuery, CancellationToken, IReadOnlyList<DeviceRow>> read, TimeSpan budget)
    {
        this.read = read;
        this.budget = budget;
    }
    private static readonly object LogLock = new();
    // A provider can remain in COM after a caller times out. Bound outstanding workers
    // and retain each slot until its worker actually exits.
    private readonly SemaphoreSlim workers = new(12);

    public async Task<IReadOnlyList<DeviceRow>> QueryAsync(InventoryQuery query, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(budget);
        var queryToken = deadline.Token;
        try
        {
            await workers.WaitAsync(deadline.Token).ConfigureAwait(false);
            var work = Task.Run(() =>
            {
                try { return read(query, queryToken); }
                catch (Exception error) when (!queryToken.IsCancellationRequested)
                {
                    Log(query.Class, error);
                    return Array.Empty<DeviceRow>();
                }
                finally { workers.Release(); }
            });
            return await work.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            Log(query.Class, error);
            return [];
        }
    }

    private static IReadOnlyList<DeviceRow> Read(InventoryQuery query, CancellationToken token)
    {
        switch (query.Class)
        {
            case "SecureBootRegistry":
                return [new() { ["SecureBootState"] = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled", null) }];
            case "ProcessorRegistry":
                return [new() { ["NominalMHz"] = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz", null) }];
            case "OfficeRegistry":
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration");
                    if (key != null) return [new() { ["ProductReleaseIds"] = key.GetValue("ProductReleaseIds"), ["VersionToReport"] = key.GetValue("VersionToReport") }];
                }
                return [];
            case "WifiCapabilities": return NativeInventory.Wifi();
            case "DisplayModes": return NativeInventory.Displays();
        }
        string wql = $"SELECT {query.Columns} FROM {query.Class}";
        if (query.Filter != null) wql += " WHERE " + query.Filter;
        if (query.Class == "PresentPanels")
            wql = "SELECT Name,PNPDeviceID FROM Win32_PnPEntity WHERE PNPClass = 'Monitor' AND Present = TRUE";
        using var search = new ManagementObjectSearcher(new ManagementScope(query.Scope), new ObjectQuery(wql),
            new System.Management.EnumerationOptions { Timeout = Timeout, ReturnImmediately = true, Rewindable = false });
        // Some monitor providers allow instance enumeration to standard users
        // but reject ExecQuery, even for SELECT * on the same class.
        using var monitor = query.Class is "WmiMonitorID" or "WmiMonitorBasicDisplayParams"
            ? new ManagementClass(query.Scope, query.Class, null) : null;
        using var result = monitor == null ? search.Get() : monitor.GetInstances(search.Options);
        var rows = new List<DeviceRow>();
        foreach (ManagementObject item in result)
        {
            using (item)
            {
                token.ThrowIfCancellationRequested();
                if (monitor != null && item["Active"] is not true) continue;
                var row = new DeviceRow();
                if (query.Class == "PresentPanels")
                {
                    row["Name"] = item["Name"];
                    try
                    {
                        var path = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\" + item["PNPDeviceID"] + @"\Device Parameters";
                        if (Registry.GetValue(path, "EDID", null) is byte[] edid) NativeInventory.DecodeEdid(edid, row);
                    }
                    catch (Exception error) { Log("PanelEdid", error); }
                }
                else foreach (var name in query.Columns.Split(',')) row[name] = item[name];
                if (query.Class == "Win32_VideoController") ReadVideoMemory(row);
                rows.Add(row);
            }
        }
        return rows;
    }

    private static void ReadVideoMemory(DeviceRow row)
    {
        // WMI AdapterRAM is a DWORD. Use the matching display driver's 64-bit
        // memory value when available, never guess the VRAM of a larger card.
        try
        {
            var id = row.Text("PNPDeviceID");
            var driver = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\" + id, "Driver", null) as string;
            if (driver != null && Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\" + driver, "HardwareInformation.qwMemorySize", null) is long bytes && bytes > 0)
                row["AdapterRAM"] = bytes;
            else if (row.Number("AdapterRAM") == uint.MaxValue) row["AdapterRAM"] = null;
        }
        catch (Exception error) { Log("VideoMemory", error); }
    }

    private static void Log(string source, Exception error)
    {
        try
        {
            lock (LogLock)
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "inventory.log"),
                    $"{DateTimeOffset.Now:u} {source} {error.GetType().Name}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
