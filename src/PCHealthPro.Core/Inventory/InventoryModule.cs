namespace PCHealthPro.Core.Inventory;

public sealed record InventoryField(string Label, string? Value)
{
    public bool Localize { get; init; }
}
public sealed record DeviceGroup(string Label, IReadOnlyList<InventoryField> Fields)
{
    public string? DeviceName { get; init; }
}
public sealed record InventoryQuery(string Label, string Class, string Columns,
    string Scope = @"root\cimv2", string? Filter = null)
{
    public uint DriveIndex { get; init; }
    public uint SectorSize { get; init; }
}
public sealed class DeviceRow : Dictionary<string, object?>
{
    public DeviceRow() : base(StringComparer.OrdinalIgnoreCase) { }
    public string? Text(string field) => this.GetValueOrDefault(field)?.ToString()?.Trim() is { Length: > 0 } value ? value : null;
    public ulong? Number(string field) => ulong.TryParse(Text(field), out var value) ? value : null;
}

public interface IInventorySource
{
    Task<IReadOnlyList<DeviceRow>> QueryAsync(InventoryQuery query, CancellationToken token);
}

public sealed class InventoryModule(string category, IInventorySource source, params InventoryQuery[] queries) : IHardwareModule
{
    public string Category => category;

    // VGHH family: https://consumer.huawei.com/cn/support/content/zh-cn15993309/
    private static bool IsMateBookXPro(DeviceRow? system) =>
        string.Equals(system?.Text("Manufacturer")?.Split(' ')[0], "Huawei", StringComparison.OrdinalIgnoreCase)
        && system?.Text("Model")?.ToUpperInvariant() is "VGHH" or "VGHH-X" or "VGHH-XX";

    public async Task<HardwareReading> ReadAsync(CancellationToken token)
    {
        var results = await Task.WhenAll(queries.Select(async query =>
            (Query: query, Rows: await source.QueryAsync(query, token).ConfigureAwait(false)))).ConfigureAwait(false);
        var groups = new List<DeviceGroup>();
        string? summary = null;
        foreach (var (query, rows) in results)
        {
            if (query.Label == "RamPlatform") continue;
            if (rows.Count == 0)
                groups.Add(new(query.Label, [new("Details", null)]));
            foreach (var row in rows)
            {
                var fields = InventoryFormat.Fields(query, row);
                if (query.Label == "Machine" && IsMateBookXPro(row))
                    fields = [new("ProductLine", "HUAWEI MateBook X Pro Core Ultra Premium Edition"), .. fields];
                summary ??= fields.Select(f => f.Value).FirstOrDefault(v => v != null);
                groups.Add(new(query.Label, fields));
            }
        }
        if (Category == "CPU")
        {
            var chips = results.First(r => r.Query.Class == "Win32_Processor").Rows;
            var registry = results.First(r => r.Query.Class == "ProcessorRegistry").Rows.FirstOrDefault();
            // Some hypervisors hide CPUID leaf 16. The nominal registry frequency and
            // SMBIOS-rated clock can corroborate a nominal base, independent of current load.
            if (chips.Count == 1 && registry?.Number("BaseClock") is not > 0 &&
                registry?.Number("NominalMHz") is > 0 and var nominal && chips[0].Number("MaxClockSpeed") is > 0 and var rated &&
                Math.Abs((double)nominal - rated) / rated < 0.02)
                groups.Add(new("CPU", [new("RatedBaseClock", $"{rated / 1000d:0.###} GHz")]));
        }
        if (Category == "RAM")
        {
            var modules = results.First(r => r.Query.Class == "Win32_PhysicalMemory").Rows;
            var sizes = modules.Select(r => r.Number("Capacity")).ToArray();
            summary = sizes.Length > 0 && sizes.All(n => n > 0)
                ? InventoryFormat.Bytes((ulong)sizes.Sum(n => (decimal)n!.Value))
                : null;
            var arrays = results.First(r => r.Query.Class == "Win32_PhysicalMemoryArray").Rows;
            var slots = arrays.Select(r => r.Number("MemoryDevices")).ToArray();
            long? total = slots.Length > 0 && slots.All(n => n is > 0 and <= ushort.MaxValue)
                ? slots.Sum(n => (long)n!.Value) : null;
            if (total < modules.Count) total = null;
            long? used = modules.Count > 0 ? modules.Count : null;
            var platform = results.Where(r => r.Query.Label == "RamPlatform").SelectMany(r => r.Rows).FirstOrDefault();
            static bool LowPower(DeviceRow row) => row.Number("SMBIOSMemoryType") is 27 or 28 or 29 or 30 or 35;
            bool mateBookOnboard = IsMateBookXPro(platform) && modules.Count > 0 && modules.All(LowPower);
            // SMBIOS counts memory devices, which may include soldered packages.
            // LPDDR can also be on a replaceable module, so its type alone cannot prove soldering.
            bool uncertainSlots = modules.Any(m => m.Number("FormFactor") == 21 ||
                LowPower(m) && m.Number("FormFactor") is not (8 or 12));
            if (mateBookOnboard || uncertainSlots)
                groups.Add(new("RAM", [new("MemoryLayout", mateBookOnboard ? "RamOnboard" : "RamSocketsUnconfirmed") { Localize = true }]));
            else
                groups.Add(new("RAM", [new("MemoryDevices", total?.ToString()),
                    new("UsedSlots", used?.ToString()), new("FreeSlots", (total - used)?.ToString())]));
        }
        if (Category == "Display")
        {
            var panels = results.First(r => r.Query.Class == "WmiMonitorID").Rows;
            var modes = results.First(r => r.Query.Class == "DisplayModes").Rows;
            int count = modes.Select(r => r.Text("MonitorPath")).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (count == 0) count = panels.Count;
            if (count > 0) groups.Insert(0, new("Display", [new("MonitorCount", count.ToString())]));
            var present = results.First(r => r.Query.Class == "PresentPanels").Rows;
            // Match by monitor instance so identical models on different outputs stay separate.
            var summaries = new Dictionary<string, DeviceRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in present.Concat(panels).Concat(modes))
            {
                string? id = row.Text("PanelInstance") ?? row.Text("InstanceName");
                if (id?.EndsWith("_0", StringComparison.Ordinal) == true) id = id[..^2];
                id ??= row.Text("MonitorPath") ?? row.Text("DisplayName") ?? $"panel-{summaries.Count}";
                if (!summaries.TryGetValue(id, out var merged)) summaries[id] = merged = new();
                foreach (var field in row) if (field.Value != null) merged[field.Key] = field.Value;
            }
            var layoutQuery = new InventoryQuery("PanelSummary", "", "PanelName,UserFriendlyName,Name,PanelVendor,ManufacturerName,Resolution,RefreshHz,PreferredMode,MaxHorizontalImageSize,MaxVerticalImageSize,DisplayName");
            foreach (var panel in summaries.Values)
                groups.Add(new("PanelSummary", InventoryFormat.Fields(layoutQuery, panel)));
            groups.Insert(0, new("PresentPanels", [new("PresentCount", present.Count > 0 ? present.Count.ToString() : null)]));
            summary = results.Where(r => r.Query.Class == "WmiMonitorID").SelectMany(r => r.Rows.Select(row =>
                    InventoryFormat.Fields(r.Query, row).First(f => f.Label == "UserFriendlyName").Value))
                .Concat(modes.Concat(present).Select(r => r.Text("PanelName")))
                .FirstOrDefault(n => n != null) ?? summary;
        }
        return new(Category, summary, summary == null ? HealthStatus.Unknown : HealthStatus.Detected)
            { Groups = groups };
    }
}
