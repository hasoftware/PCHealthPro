using System.Buffers.Binary;
using System.Numerics;
using System.Xml.Linq;
using PCHealthPro.Core;
using PCHealthPro.Core.Inventory;
using Xunit;

namespace PCHealthPro.Tests;

public sealed class DriveBatteryTests
{
    private sealed class Source(Dictionary<string, DeviceRow[]> data) : IInventorySource
    {
        public Task<IReadOnlyList<DeviceRow>> QueryAsync(InventoryQuery q, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<DeviceRow>>(data.GetValueOrDefault(q.Class) ?? []);
        }
    }

    [Fact]
    public void DriveHourBaselinePersistsAndNeverDropsAfterARegression()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var first = new DriveHourHistory(dir.FullName);
            Assert.Equal("HoursFirst", first.Compare("Model", "serial-A", 300));
            var later = new DriveHourHistory(dir.FullName);
            Assert.Equal("HoursDecreased", later.Compare("Model", "serial-A", 10));
            Assert.Equal("HoursDecreased", later.Compare("Model", "serial-A", 299));
            Assert.Equal("HoursConsistent", later.Compare("Model", "serial-A", 301));
            Assert.Equal("HoursFirst", later.Compare("Model", "serial-B", 100));
            Assert.Equal("HoursUnavailable", later.Compare("Model", null, 20));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task PackTemperatureCapacityAndAgeUseMatchingBatteryData()
    {
        var source = new Source(new()
        {
            ["BatteryStaticData"] = [new() { ["InstanceName"] = "Pack_0", ["DesignedCapacity"] = 60000, ["ManufactureDate"] = "20230101000000.000000+000" }],
            ["BatteryFullChargedCapacity"] = [new() { ["InstanceName"] = "Pack_0", ["FullChargedCapacity"] = 45000 }],
            ["BatteryTemperature"] = [new() { ["InstanceName"] = "Pack_0", ["Temperature"] = 3032 }],
            ["Win32_Battery"] = [new() { ["PNPDeviceID"] = "Pack", ["DesignVoltage"] = 12000 }]
        });
        var pack = Assert.Single((await new BatteryModule(source).ReadAsync(default)).Groups);
        string? V(string k) => pack.Fields.Single(f => f.Label == k).Value;
        Assert.Equal("60 Wh (5000 mAh)", V("DesignCapacity"));
        Assert.Equal("45 Wh (3750 mAh)", V("FullCapacity"));
        Assert.Equal("25 %", V("BatteryWear"));
        Assert.Equal("2023-01-01", V("ManufactureDate"));
        Assert.StartsWith("30", V("Temperature"));
    }

    [Fact]
    public void NvmeKeeps128BitCountersAndUsesDecimalDataUnits()
    {
        byte[] log = new byte[512];
        log[2] = 1;
        log[1] = 60; // 316 K
        log[5] = 12;
        log[48 + 8] = 1;
        log[112] = 27;
        log[128] = 84;
        var row = new DeviceRow();
        SmartLog.Nvme(log, row);
        Assert.Equal("43", row.Text("Temperature"));
        Assert.Equal(((BigInteger.One << 64) * 512000).ToString(), row.Text("BytesWritten"));
        Assert.Equal("27", row.Text("PowerCycles"));
        Assert.Equal("84", row.Text("PowerOnHours"));
        Assert.Equal("12", row.Text("Wear"));
        var blank = new DeviceRow();
        SmartLog.Nvme(new byte[512], blank);
        SmartLog.Nvme(log[..10], blank);
        Assert.Empty(blank);
    }

    [Fact]
    public void AtaUsesOnlyValidStatisticsAndActualSectorSize()
    {
        byte[] page = new byte[512];
        page[0] = page[2] = 1;
        BinaryPrimitives.WriteUInt64LittleEndian(page.AsSpan(8), (3UL << 62) | 34);
        BinaryPrimitives.WriteUInt64LittleEndian(page.AsSpan(16), (1UL << 63) | 99);
        BinaryPrimitives.WriteUInt64LittleEndian(page.AsSpan(24), (3UL << 62) | 25);
        var row = new DeviceRow();
        SmartLog.AtaStatistics(page, 1, 4096, row);
        Assert.Equal("34", row.Text("PowerCycles"));
        Assert.Null(row.Text("PowerOnHours"));
        Assert.Equal("102400", row.Text("BytesWritten"));
    }

    [Theory]
    [InlineData("CriticalWarning", 1, HealthStatus.Problem)]
    [InlineData("PendingSectors", 1, HealthStatus.Problem)]
    [InlineData("Reallocated", 3, HealthStatus.Attention)]
    [InlineData("Wear", 100, HealthStatus.Problem)]
    [InlineData("Wear", 90, HealthStatus.Attention)]
    [InlineData("Temperature", 60, HealthStatus.Attention)]
    [InlineData("CriticalWarning", 0, HealthStatus.Good)]
    [InlineData("PowerOnHours", 12, HealthStatus.Unknown)]
    public void HealthRequiresEvidence(string field, int value, HealthStatus expected) =>
        Assert.Equal(expected, StorageModule.Assess(new() { [field] = value }, null).Item1);

    [Fact]
    public void AtaVendorHoursExcludeSubHourBytesAndUnknownEncodings()
    {
        var page = new byte[512];
        page[0] = 1;
        page[2] = 9;
        page[7] = 24;
        page[11] = 202;
        page[14] = 194;
        page[19] = 36;
        page[26] = 12;
        page[31] = 11;
        var seagate = new DeviceRow();
        SmartLog.AtaAttributes(page, seagate, "ST500LM021");
        Assert.Equal("24", seagate.Text("PowerOnHours"));
        Assert.Equal("36", seagate.Text("Temperature"));
        Assert.Equal("11", seagate.Text("PowerCycles"));
        var other = new DeviceRow();
        SmartLog.AtaAttributes(page, other, "Other");
        Assert.Null(other.Text("PowerOnHours"));
    }

    [Fact]
    public async Task DrivesWithDuplicateSerialNeverBorrowOtherDisksHealth()
    {
        var source = new Source(new() {
            ["Win32_DiskDrive"] = [new() { ["Model"] = "A", ["SerialNumber"] = "0" }, new() { ["Model"] = "B", ["SerialNumber"] = "0" }],
            ["MSFT_PhysicalDisk"] = [new() { ["SerialNumber"] = "0", ["HealthStatus"] = 0 }]
        });
        var result = await new StorageModule(source).ReadAsync(default);
        Assert.Equal(2, result.Groups.Count);
        Assert.Equal(HealthStatus.Unknown, result.Status);
    }

    [Theory]
    [InlineData(50000, 35000, 30)]
    [InlineData(50000, 51000, 0)]
    [InlineData(0, 35000, null)]
    [InlineData(50000, 0, null)]
    public void BatteryWearHandlesMissingAndOverDesignCapacity(int design, int full, int? expected)
    {
        var actual = BatteryModule.Wear((ulong)design, (ulong)full);
        if (expected.HasValue) Assert.Equal(expected.Value, actual!.Value, 6);
        else Assert.Null(actual);
    }

    [Fact]
    public async Task BatteryPacksJoinByInstanceAndReportFallbackUsesSerial()
    {
        var source = new Source(new() {
            ["BatteryStaticData"] = [new() { ["InstanceName"] = "B", ["SerialNumber"] = "2", ["DesignedCapacity"] = 100 },
                new() { ["InstanceName"] = "A", ["SerialNumber"] = "1", ["DesignedCapacity"] = 200 }],
            ["BatteryFullChargedCapacity"] = [new() { ["InstanceName"] = "A", ["FullChargedCapacity"] = 100 },
                new() { ["InstanceName"] = "B", ["FullChargedCapacity"] = 80 }],
            ["BatteryReport"] = [new() { ["SerialNumber"] = "1", ["CycleCount"] = 123 }]
        });
        var r = await new BatteryModule(source).ReadAsync(default);
        string? Field(int pack, string key) => r.Groups[pack].Fields.Single(f => f.Label == key).Value;
        Assert.Equal("20 %", Field(0, "BatteryWear"));
        Assert.Equal("50 %", Field(1, "BatteryWear"));
        Assert.Null(Field(0, "CycleCount"));
        Assert.Equal("123", Field(1, "CycleCount"));
    }

    [Fact]
    public async Task ExplicitBatteryAbsenceIsDistinctFromProviderFailure()
    {
        Assert.Equal(HealthStatus.Absent, (await new BatteryModule(new Source(new()
            { ["SystemPower"] = [new() { ["Absent"] = true }] })).ReadAsync(default)).Status);
        Assert.Equal(HealthStatus.Unknown, (await new BatteryModule(new Source([])).ReadAsync(default)).Status);
    }

    [Fact]
    public void ReportReadsInstalledPacksFromNamespacedXml()
    {
        var xml = XDocument.Parse("<BatteryReport xmlns='urn:test'><Batteries><Battery><Id>Pack 1</Id><DesignCapacity>50000</DesignCapacity></Battery></Batteries><History><Battery/></History></BatteryReport>");
        var row = Assert.Single(BatterySources.Parse(xml));
        Assert.Equal("50000", row.Text("DesignCapacity"));
        Assert.Equal("Pack 1", row.Text("Id"));
    }
}
