namespace PCHealthPro.Core.Inventory;

internal sealed class BatteryModule(IInventorySource source) : IHardwareModule
{
    public string Category => "Battery";
    public async Task<HardwareReading> ReadAsync(CancellationToken token)
    {
        Task<IReadOnlyList<DeviceRow>> Query(string name, string columns = "", string scope = @"root\WMI") =>
            source.QueryAsync(new("Battery", name, columns, scope), token);

        var power = (await Query("SystemPower").ConfigureAwait(false)).FirstOrDefault();
        if (power?.GetValueOrDefault("Absent") is true)
            return new(Category, null, HealthStatus.Absent);
        var queries = new[]
        {
            Query("BatteryStaticData", "InstanceName,DeviceName,ManufactureName,SerialNumber,DesignedCapacity,Capabilities"),
            Query("BatteryFullChargedCapacity", "InstanceName,FullChargedCapacity"),
            Query("BatteryCycleCount", "InstanceName,CycleCount"),
            Query("BatteryStatus", "InstanceName,RemainingCapacity,Charging,Discharging,ChargeRate,DischargeRate"),
            Query("Win32_Battery", "Name,PNPDeviceID,EstimatedChargeRemaining,EstimatedRunTime,BatteryStatus,DesignCapacity,FullChargeCapacity", @"root\cimv2")
        };
        var results = await Task.WhenAll(queries).ConfigureAwait(false);
        var packs = new Dictionary<string, DeviceRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var rows in results.Take(4))
        foreach (var row in rows)
        {
            if (row.Text("InstanceName") is not string id) continue;
            if (!packs.TryGetValue(id, out var pack)) packs[id] = pack = new();
            foreach (var field in row) if (field.Value != null) pack[field.Key] = field.Value;
        }

        var devices = packs.Values.ToList();
        foreach (var basic in results[4])
        {
            var pnp = basic.Text("PNPDeviceID");
            var target = devices.FirstOrDefault(p => pnp != null && string.Equals(
                p.Text("InstanceName"), pnp + "_0", StringComparison.OrdinalIgnoreCase));
            if (target == null && results[4].Count == 1 && devices.Count == 1) target = devices[0];
            if (target == null) { target = new(); devices.Add(target); }
            foreach (var field in basic) if (field.Value != null) target[field.Key] = field.Value;
        }

        if (devices.Count == 0 || devices.Any(p => Capacity(p, "DesignedCapacity", "DesignCapacity") == null ||
            Capacity(p, "FullChargedCapacity", "FullChargeCapacity") == null || Counter(p, "CycleCount") == null))
        {
            var report = await Query("BatteryReport").ConfigureAwait(false);
            if (devices.Count == 0) devices.AddRange(report);
            else foreach (var entry in report)
            {
                var serial = entry.Text("SerialNumber");
                var matches = devices.Where(p => serial != null && p.Text("SerialNumber") == serial).ToArray();
                var pack = matches.Length == 1 ? matches[0] : report.Count == 1 && devices.Count == 1 ? devices[0] : null;
                if (pack != null)
                    foreach (var field in entry)
                    {
                        bool missing = field.Key switch
                        {
                            "DesignCapacity" or "FullChargeCapacity" => Counter(pack, field.Key) is null or 0,
                            "CycleCount" => Counter(pack, field.Key) == null,
                            _ => pack.Text(field.Key) == null
                        };
                        if (missing && field.Value != null) pack[field.Key] = field.Value;
                    }
            }
        }
        if (devices.Count == 0 && power?.Number("Percent") != null) devices.Add(new());
        var groups = devices.Select(p => Describe(p, devices.Count == 1 ? power : null)).ToArray();
        return new(Category, groups.FirstOrDefault()?.DeviceName, groups.Length == 0 ? HealthStatus.Unknown : HealthStatus.Detected) { Groups = groups };
    }

    private static ulong? Counter(DeviceRow pack, string key) => pack.Number(key) is ulong n && n < uint.MaxValue ? n : null;
    private static ulong? Capacity(DeviceRow pack, string key, string fallback) =>
        Counter(pack, key) is > 0 and var n ? n : Counter(pack, fallback) is > 0 and var f ? f : null;

    internal static double? Wear(ulong? design, ulong? full) => design is > 0 && full is > 0
        ? Math.Clamp(100d * (1 - (double)full.Value / design.Value), 0, 100) : null;

    private static DeviceGroup Describe(DeviceRow pack, DeviceRow? system)
    {
        ulong? design = Capacity(pack, "DesignedCapacity", "DesignCapacity");
        ulong? full = Capacity(pack, "FullChargedCapacity", "FullChargeCapacity");
        bool relative = (pack.Number("Capabilities").GetValueOrDefault() & 0x40000000) != 0;
        string? Energy(ulong? n) => n == null ? null : relative ? n.ToString() : $"{n / 1000d:0.###} Wh";
        string? Rate(string key) => !relative && Counter(pack, key) is ulong n && n < int.MaxValue ? $"{n / 1000d:0.###} W" : null;
        var wear = Wear(design, full);
        var percent = Counter(pack, "EstimatedChargeRemaining");
        if (percent is not <= 100)
            percent = full > 0 && Counter(pack, "RemainingCapacity") is ulong remaining
                ? (ulong)Math.Clamp(Math.Round(100d * remaining / full.Value), 0, 100) : system?.Number("Percent");
        var state = pack.GetValueOrDefault("Charging") is true ? "Charging" :
            pack.GetValueOrDefault("Discharging") is true ? "Discharging" :
            pack.GetValueOrDefault("Charging") is false && pack.GetValueOrDefault("Discharging") is false ? "BatteryIdle" :
            pack.Number("BatteryStatus") switch { 3 => "BatteryFull", 6 or 7 or 8 or 9 => "Charging", _ => null };
        var minutes = Counter(pack, "EstimatedRunTime");
        if (minutes is null or >= 71582788) minutes = system?.Number("Minutes");
        string? model = pack.Text("DeviceName") ?? pack.Text("Id") ?? pack.Text("Name");
        return new("Battery", [
            new("Model", model), new("Manufacturer", pack.Text("ManufactureName") ?? pack.Text("Manufacturer")),
            new("SerialNumber", pack.Text("SerialNumber")), new(relative ? "RelativeDesign" : "DesignCapacity", Energy(design)),
            new(relative ? "RelativeFull" : "FullCapacity", Energy(full)), new("BatteryWear", wear == null ? null : $"{wear:0.#} %"),
            new("CycleCount", Counter(pack, "CycleCount")?.ToString()), new("ChargeState", state) { Localize = true },
            new("EstimatedChargeRemaining", percent == null ? null : $"{percent} %"),
            new("Runtime", minutes == null ? null : $"{minutes} min"), new("ChargeRate", Rate("ChargeRate")),
            new("DischargeRate", Rate("DischargeRate"))
        ]) { DeviceName = model };
    }
}
