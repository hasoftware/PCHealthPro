using PCHealthPro.App.Services;
using PCHealthPro.Core;
using PCHealthPro.Core.Inventory;

namespace PCHealthPro.App;

internal sealed record OverviewTileContent(string Title, string Facts, string Detail)
{
    internal static OverviewTileContent From(HardwareReading reading, LanguageService strings)
    {
        var groups = reading.Groups;
        string T(string key) => strings.Get(key);
        string Format(string key, params object[] values) => string.Format(T(key), values);
        static string? Field(DeviceGroup? group, string key) => group?.Fields.FirstOrDefault(f => f.Label == key)?.Value;
        static string Join(params string?[] parts) => string.Join(" · ", parts.Where(s => !string.IsNullOrWhiteSpace(s)));
        var title = string.IsNullOrWhiteSpace(reading.Value)
            ? T(reading.Status == HealthStatus.Absent ? "Absent" : "Unknown") : reading.Value;
        var facts = "";
        var detail = "";
        switch (reading.Category)
        {
            case "RAM":
                var topology = groups.FirstOrDefault(g => g.Label == "RAM");
                var total = Field(topology, "MemoryDevices");
                var used = Field(topology, "UsedSlots");
                var free = Field(topology, "FreeSlots");
                var layout = Field(topology, "MemoryLayout");
                facts = layout != null ? T(layout) : (total, used, free) switch
                {
                    (not null, not null, not null) => Format("RamOccupancy", used, total, free),
                    (_, not null, _) => Format("RamModulesOnly", used),
                    (not null, _, _) => Format("RamSlotsOnly", total),
                    _ => T("RamSlotsUnknown")
                };
                var modules = groups.Where(g => g.Label == "MemoryModule")
                    .Select(g => Join(layout == null ? Field(g, "Capacity") : null, Field(g, "SMBIOSMemoryType"), Field(g, "ConfiguredClockSpeed")))
                    .Where(s => s.Length > 0).GroupBy(s => s);
                detail = string.Join("\n", modules.Select(m => layout == null ? $"{m.Count()} × {m.Key}" : m.Key));
                break;
            case "CPU":
                var processors = groups.Where(g => Field(g, "Name") != null).ToArray();
                var cpu = processors.FirstOrDefault();
                if (Field(cpu, "NumberOfCores") is { } cores && Field(cpu, "NumberOfLogicalProcessors") is { } threads)
                    facts = Format("CpuTopology", cores, threads);
                var clock = groups.SelectMany(g => g.Fields).FirstOrDefault(f => f.Label is "BaseClock" or "RatedBaseClock" && f.Value != null);
                detail = Join(Field(cpu, "Architecture"), clock == null ? null : $"{T(clock.Label)} {clock.Value}");
                if (processors.Length > 1)
                {
                    title = Format("CpuPackages", processors.Length);
                    facts = string.Join("\n", processors.Select(p => Join(Field(p, "Name"),
                        Field(p, "NumberOfCores") is { } c && Field(p, "NumberOfLogicalProcessors") is { } t ? Format("CpuTopology", c, t) : null)));
                    detail = "";
                }
                break;
            case "GPU":
                var gpu = groups.FirstOrDefault();
                facts = T(Field(gpu, "GpuType") ?? "GpuUnknown");
                var integrated = Field(gpu, "GpuType") is "GpuIntegrated" or "GpuIntegratedModel" or "GpuIntegratedPci";
                detail = Join(Field(gpu, "Manufacturer"), integrated ? T("GpuSharedRam") : Field(gpu, "AdapterRAM"));
                break;
            case "Storage" when groups.Count > 0:
                title = Format("DiskCount", groups.Count);
                facts = string.Join("\n", groups.Select(g => Join(Field(g, "Size"),
                    string.Join(" ", new[] { Field(g, "DriveMedia"), Field(g, "DriveBus") }.Where(s => s != null).Distinct()),
                    g.DeviceName)).Select(s => s.Length > 0 ? s : T("Unknown")));
                break;
            case "Windows":
                var os = groups.FirstOrDefault(g => g.Label == "Windows");
                facts = Join(Field(os, "OSArchitecture"), Field(os, "BuildNumber") is { } build ? $"{T("BuildNumber")} {build}" : null);
                if (Field(groups.FirstOrDefault(g => g.Label == "WindowsActivation"), "LicenseStatus") is { } license)
                    detail = T(license);
                break;
            case "Battery":
                facts = string.Join("\n", groups.Select(g => Join(Field(g, "EstimatedChargeRemaining"),
                    Field(g, "ChargeState") is { } charge ? T(charge) : null,
                    Field(g, "BatteryWear") is { } wear ? $"{T("BatteryWear")} {wear}" : null)).Where(s => s.Length > 0));
                break;
        }
        return new(title, facts, detail);
    }
}
