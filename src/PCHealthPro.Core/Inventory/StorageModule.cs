using System.Numerics;

namespace PCHealthPro.Core.Inventory;

internal sealed class StorageModule(IInventorySource source, DriveHourHistory? history = null) : IHardwareModule
{
    public string Category => "Storage";
    private const string StorageScope = @"root\Microsoft\Windows\Storage";

    public async Task<HardwareReading> ReadAsync(CancellationToken token)
    {
        var drives = source.QueryAsync(new("Storage", "Win32_DiskDrive",
            "Model,Index,PNPDeviceID,SerialNumber,FirmwareRevision,Size,BytesPerSector,InterfaceType"), token);
        var physical = source.QueryAsync(new("Storage", "MSFT_PhysicalDisk",
            "DeviceId,SerialNumber,FriendlyName,Model,FirmwareVersion,Size,MediaType,BusType,HealthStatus", StorageScope), token);
        var counters = source.QueryAsync(new("Storage", "MSFT_StorageReliabilityCounter",
            "DeviceId,Temperature,PowerOnHours,Wear,ReadErrorsUncorrected,WriteErrorsUncorrected", StorageScope), token);
        var partitions = source.QueryAsync(new("Storage", "MSFT_Partition", "DiskNumber,DriveLetter", StorageScope), token);
        await Task.WhenAll(drives, physical, counters, partitions).ConfigureAwait(false);
        var disks = await drives;
        var providers = await physical;
        var metrics = await counters;
        var tasks = new List<Task<(DeviceGroup Group, HealthStatus Status, bool Limited)>>();
        foreach (var disk in disks)
        {
            // PhysicalDisk.DeviceId is provider-specific. Never treat it as a PhysicalDrive index.
            var serial = disk.Text("SerialNumber");
            var match = Unique(providers, "SerialNumber", serial);
            if (Unique(disks, "SerialNumber", serial) == null) match = null;
            tasks.Add(DescribeAsync(disk, match));
        }
        if (disks.Count == 0)
            foreach (var provider in providers) tasks.Add(DescribeAsync(new(), provider));
        var items = await Task.WhenAll(tasks).ConfigureAwait(false);
        var states = items.Select(i => i.Status).ToArray();
        var status = states.Contains(HealthStatus.Problem) ? HealthStatus.Problem :
            states.Contains(HealthStatus.Attention) ? HealthStatus.Attention :
            states.Length == 0 || states.Contains(HealthStatus.Unknown) ? HealthStatus.Unknown : HealthStatus.Good;
        return new(Category, items.Length == 0 ? null : string.Join(" · ", items.Select(i => i.Group.DeviceName).OfType<string>()), status)
        {
            Groups = items.Select(i => i.Group).ToArray(),
            LimitedSmart = items.Length == 0 || items.Any(i => i.Limited)
        };

        async Task<(DeviceGroup, HealthStatus, bool)> DescribeAsync(DeviceRow disk, DeviceRow? provider)
        {
            var smart = new DeviceRow();
            var counter = Unique(metrics, "DeviceId", provider?.Text("DeviceId"));
            if (counter != null)
                foreach (var field in counter) smart[field.Key] = field.Value;
            if (smart.Number("Temperature") == 0) smart.Remove("Temperature");
            if (disk.Number("Index") is ulong index && index <= uint.MaxValue)
            {
                var query = new InventoryQuery("Storage", "DriveSmart", "")
                    { DriveIndex = (uint)index, SectorSize = (uint)(disk.Number("BytesPerSector") ?? 0) };
                var native = await source.QueryAsync(query, token).ConfigureAwait(false);
                if (native.FirstOrDefault() is { } row)
                    foreach (var field in row) smart[field.Key] = field.Value;
            }
            var bus = smart.Number("BusType") ?? provider?.Number("BusType");
            string? busName = bus switch { 1 => "SCSI", 3 => "ATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 11 => "SATA",
                12 => "SD", 13 => "MMC", 14 => "Virtual", 16 => "Storage Spaces", 17 => "NVMe", _ => disk.Text("InterfaceType") };
            string? media = provider?.Number("MediaType") switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => bus == 17 ? "NVMe" : null };
            var (state, reason) = Assess(smart, provider?.Number("HealthStatus"));
            var model = disk.Text("Model") ?? provider?.Text("Model") ?? provider?.Text("FriendlyName");
            var size = disk.Number("Size") ?? provider?.Number("Size");
            string? Unit(string key, string unit) => smart.Text(key) is string value ? $"{value} {unit}" : null;
            string? tbw = BigInteger.TryParse(smart.Text("BytesWritten"), out var bytes)
                ? $"{(double)bytes / 1e12:0.######} TB" : null;
            var indexNumber = disk.Number("Index");
            var volumes = indexNumber == null ? [] : (await partitions).Where(p => p.Number("DiskNumber") == indexNumber)
                .Select(p => p.Text("DriveLetter")).Where(letter => letter?.Length == 1 && char.IsAsciiLetter(letter[0]))
                .Select(letter => letter!.ToUpperInvariant() + @":\").Distinct().ToArray();
            var serialNumber = disk.Text("SerialNumber") ?? provider?.Text("SerialNumber");
            var identityRows = disks.Count > 0 ? disks : providers;
            var trend = history?.Compare(model, Unique(identityRows, "SerialNumber", serialNumber) == null ? null : serialNumber, smart.Number("PowerOnHours")) ?? "HoursUnavailable";
            var wear = smart.Number("Wear");
            var life = media is "SSD" or "NVMe" || bus == 17 ? wear : null;
            InventoryField[] fields =
            [
                new("Model", model), new("SerialNumber", disk.Text("SerialNumber") ?? provider?.Text("SerialNumber")),
                new("Firmware", disk.Text("FirmwareRevision") ?? provider?.Text("FirmwareVersion")),
                new("Size", size > 0 ? InventoryFormat.Bytes(size.Value) : null),
                new("Volumes", string.Join(", ", volumes)), new("HoursTrend", trend) { Localize = true },
                new("LifeRemaining", life == null ? null : $"{100 - Math.Min(life.Value, 100)} %"), new("DriveMedia", media), new("DriveBus", busName),
                new("DriveHealth", state.ToString()) { Localize = true }, new("HealthReason", reason) { Localize = true },
                new("Temperature", Unit("Temperature", "°C")), new("PowerOnHours", Unit("PowerOnHours", "h")),
                new("PowerCycles", smart.Text("PowerCycles")), new("Reallocated", smart.Text("Reallocated")),
                new("PendingSectors", smart.Text("PendingSectors")), new("MediaErrors", smart.Text("MediaErrors")),
                new("Wear", Unit("Wear", "%")), new("BytesWritten", tbw)
            ];
            if (trend == "HoursDecreased" && state != HealthStatus.Problem)
            {
                state = HealthStatus.Attention;
                int pos = Array.FindIndex(fields, f => f.Label == "DriveHealth");
                fields[pos] = fields[pos] with { Value = state.ToString() };
                fields[pos + 1] = fields[pos + 1] with { Value = "HoursDecreased" };
            }
            return (new DeviceGroup(Category, fields) { DeviceName = model }, state,
                smart.Text("PowerOnHours") == null || smart.Text("PowerCycles") == null || smart.Text("Temperature") == null);
        }
    }

    private static DeviceRow? Unique(IReadOnlyList<DeviceRow> rows, string field, string? value)
    {
        if (value == null) return null;
        var matches = rows.Where(r => string.Equals(r.Text(field), value, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    internal static (HealthStatus, string) Assess(DeviceRow smart, ulong? health)
    {
        bool Positive(string field) => BigInteger.TryParse(smart.Text(field), out var count) && count > 0;
        if (smart.GetValueOrDefault("PredictFailure") is true || Positive("CriticalWarning")) return (HealthStatus.Problem, "SmartFailed");
        if (health == 2) return (HealthStatus.Problem, "StorageUnhealthy");
        if (Positive("PendingSectors") || Positive("MediaErrors") || Positive("ReadErrorsUncorrected") || Positive("WriteErrorsUncorrected"))
            return (HealthStatus.Problem, "UncorrectableErrors");
        if (smart.Number("Wear") >= 100) return (HealthStatus.Problem, "EnduranceSpent");
        if (Positive("Reallocated")) return (HealthStatus.Attention, "RemappedSectors");
        if (smart.Number("Wear") >= 90) return (HealthStatus.Attention, "EnduranceLow");
        if (smart.Number("Temperature") >= 60) return (HealthStatus.Attention, "HighTemperature");
        if (health == 1) return (HealthStatus.Attention, "StorageWarning");
        if (smart.Number("CriticalWarning") == 0 || smart.GetValueOrDefault("PredictFailure") is false)
            return (HealthStatus.Good, "SmartClear");
        if (health == 0) return (HealthStatus.Good, "StorageHealthy");
        return (HealthStatus.Unknown, "NoHealthSignal");
    }
}
