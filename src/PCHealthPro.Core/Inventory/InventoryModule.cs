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

    public async Task<HardwareReading> ReadAsync(CancellationToken token)
    {
        var results = await Task.WhenAll(queries.Select(async query =>
            (Query: query, Rows: await source.QueryAsync(query, token).ConfigureAwait(false)))).ConfigureAwait(false);
        var groups = new List<DeviceGroup>();
        string? summary = null;
        foreach (var (query, rows) in results)
        {
            if (rows.Count == 0)
                groups.Add(new(query.Label, [new("Details", null)]));
            foreach (var row in rows)
            {
                var fields = InventoryFormat.Fields(query, row);
                summary ??= fields.Select(f => f.Value).FirstOrDefault(v => v != null);
                groups.Add(new(query.Label, fields));
            }
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
            if (modules.Count > 0 && slots.Length > 0 && slots.All(n => n > 0))
            {
                var free = slots.Sum(n => (long)n!.Value) - modules.Count;
                groups.Add(new("RAM", [new("FreeSlots", free >= 0 ? free.ToString() : null)]));
            }
        }
        if (Category == "Display")
        {
            var panels = results.First(r => r.Query.Class == "WmiMonitorID").Rows;
            var modes = results.First(r => r.Query.Class == "DisplayModes").Rows;
            int count = modes.Select(r => r.Text("MonitorPath")).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (count == 0) count = panels.Count;
            if (count > 0) groups.Insert(0, new("Display", [new("MonitorCount", count.ToString())]));
            var present = results.First(r => r.Query.Class == "PresentPanels").Rows;
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
