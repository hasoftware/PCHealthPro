using PCHealthPro.Core.Inventory;
using Xunit;

namespace PCHealthPro.Tests;

public sealed class InventoryTests
{
    private sealed class Source(Dictionary<string, DeviceRow[]> rows) : IInventorySource
    {
        public Task<IReadOnlyList<DeviceRow>> QueryAsync(InventoryQuery query, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<DeviceRow>>(rows.GetValueOrDefault(query.Class) ?? []);
    }

    [Theory]
    [InlineData("HUAWEI", "VGHH-XX", true)]
    [InlineData("Huawei Technologies Co., Ltd.", "vghh-x", true)]
    [InlineData("Other", "VGHH-XX", false)]
    [InlineData("Huawei", "VGHH-XX-PCB", false)]
    [InlineData("Huawei", "Unlisted", false)]
    public async Task MachineSummaryResolvesKnownVendorModelAndRetainsFirmwareValues(string maker, string model, bool known)
    {
        var source = new Source(new()
        {
            ["Win32_ComputerSystem"] = [new() { ["Manufacturer"] = maker, ["Model"] = model, ["SystemFamily"] = "OEM family" }]
        });
        var machine = InventoryCatalog.Create(source).Single(m => m.Category == "Machine");
        var result = await machine.ReadAsync(default);
        Assert.Equal(known ? "HUAWEI MateBook X Pro Core Ultra Premium Edition" : model, result.Value);
        var fields = result.Groups.Single(g => g.Label == "Machine").Fields;
        Assert.Contains(fields, f => f.Label == "Model" && f.Value == model);
        Assert.Contains(fields, f => f.Label == "SystemFamily" && f.Value == "OEM family");
    }

    [Theory]
    [InlineData("8589934592", "16 GiB")]
    [InlineData(null, null)]
    public async Task MemoryKeepsIndividualModulesAndCountsFreeSlots(string? secondCapacity, string? expected)
    {
        var source = new Source(new()
        {
            ["Win32_PhysicalMemory"] = [new() { ["Capacity"] = 8L << 30 }, new() { ["Capacity"] = secondCapacity }],
            ["Win32_PhysicalMemoryArray"] = [new() { ["MemoryDevices"] = 4 }]
        });
        var module = InventoryCatalog.Create(source).Single(m => m.Category == "RAM");
        var result = await module.ReadAsync(default);
        Assert.Equal(expected, result.Value);
        Assert.Equal(2, result.Groups.Count(g => g.Label == "MemoryModule"));
        Assert.Contains(result.Groups.SelectMany(g => g.Fields), f => f.Label == "FreeSlots" && f.Value == "2");
    }

    [Fact]
    public async Task MateBookReportsSolderedCapacityAndKeepsFirmwareCountInDetails()
    {
        var source = new Source(new()
        {
            ["Win32_ComputerSystem"] = [new() { ["Manufacturer"] = "HUAWEI", ["Model"] = "VGHH-XX" }],
            ["Win32_PhysicalMemory"] = Enumerable.Range(0, 8).Select(i => new DeviceRow
                { ["Capacity"] = 4L << 30, ["SMBIOSMemoryType"] = 35, ["DeviceLocator"] = $"Controller{i}", ["ConfiguredClockSpeed"] = 6400 }).ToArray(),
            ["Win32_PhysicalMemoryArray"] = [new() { ["MemoryDevices"] = 8 }]
        });
        var reading = await InventoryCatalog.Create(source).Single(m => m.Category == "RAM").ReadAsync(default);
        Assert.Equal("32 GiB", reading.Value);
        var fields = reading.Groups.SelectMany(g => g.Fields).ToArray();
        Assert.Contains(fields, f => f.Label == "MemoryLayout" && f.Value == "RamOnboard" && f.Localize);
        Assert.Contains(fields, f => f.Label == "FirmwareDeviceCount" && f.Value == "8");
        Assert.DoesNotContain(fields, f => f.Label is "MemoryDevices" or "UsedSlots" or "FreeSlots");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(21, false)]
    [InlineData(12, true)]
    public async Task OtherLpddrSystemsNeedModuleEvidenceForSocketCounts(int form, bool removable)
    {
        var source = new Source(new()
        {
            ["Win32_PhysicalMemory"] = [new() { ["SMBIOSMemoryType"] = 35, ["FormFactor"] = form }],
            ["Win32_PhysicalMemoryArray"] = [new() { ["MemoryDevices"] = 2 }]
        });
        var result = await InventoryCatalog.Create(source).Single(m => m.Category == "RAM").ReadAsync(default);
        var topology = result.Groups.Single(g => g.Label == "RAM").Fields;
        if (removable) Assert.Contains(topology, f => f.Label == "FreeSlots" && f.Value == "1");
        else Assert.Equal("RamSocketsUnconfirmed", Assert.Single(topology).Value);
    }

    [Theory]
    [InlineData(@"pci\ven_8086&dev_7d55&subsys_1234\0", "GpuIntegratedPci")]
    [InlineData(@"PCI\VEN_8086&DEV_7DD5", "GpuIntegratedPci")]
    [InlineData(@"PCI\VEN_8086&DEV_56A0", "GpuDiscretePci")]
    [InlineData(@"PCI\VEN_8086&DEV_7D550", null)]
    [InlineData(@"PCI\VEN_1234&DEV_7D55", null)]
    [InlineData(null, null)]
    public void IntelArcAdaptersAreClassifiedByExactPciIdentity(string? instance, string? expected)
    {
        Assert.Equal(expected, GraphicsAdapterType.FromProduct("Intel", "Intel(R) Arc(TM) Graphics", instance));
    }

    [Fact]
    public async Task SlotTotalsCombineArraysAndIgnoreContradictoryCounts()
    {
        async Task<IReadOnlyList<InventoryField>> Topology(params int[] counts)
        {
            var source = new Source(new()
            {
                ["Win32_PhysicalMemory"] = [new(), new()],
                ["Win32_PhysicalMemoryArray"] = counts.Select(n => new DeviceRow { ["MemoryDevices"] = n }).ToArray()
            });
            var reading = await InventoryCatalog.Create(source).Single(m => m.Category == "RAM").ReadAsync(default);
            return reading.Groups.Single(g => g.Label == "RAM").Fields;
        }
        Assert.Equal(["4", "2", "2"], (await Topology(2, 2)).Select(f => f.Value));
        Assert.Equal([null, "2", null], (await Topology(1)).Select(f => f.Value));
        Assert.Equal([null, "2", null], (await Topology(0, 2)).Select(f => f.Value));
    }

    [Fact]
    public void ModernOfficeLicenseKeepsOnlyProductAndStatus()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{"ProductReleaseId":"O365BusinessRetail","Metadata":{"NotAfter":"2026-10-20T00:00:00Z","UserId":"private"},"ExpiresOn":"2026-10-01T00:00:00Z"}""");
        foreach (var (month, day, status) in new[] { (9, 1, "1"), (10, 5, "2"), (11, 1, "5") })
        {
            var row = OfficeLicenses.Describe(doc.RootElement, new DateTimeOffset(2026, month, day, 0, 0, 0, TimeSpan.Zero))!;
            Assert.Equal(status, row.Text("LicenseStatus"));
            Assert.Equal(3, row.Count);
            Assert.DoesNotContain("private", row.Values);
        }
    }

    [Fact]
    public async Task MissingActivationAndInvalidKeyDoNotImplyCounterfeit()
    {
        var source = new Source(new() { ["SoftwareLicensingProduct"] = [new() { ["Name"] = "Example", ["PartialProductKey"] = "long-private-value" }] });
        var result = await InventoryCatalog.Create(source).Single(m => m.Category == "Windows").ReadAsync(default);
        var fields = result.Groups.Single(g => g.Label == "WindowsActivation").Fields;
        Assert.Null(fields.Single(f => f.Label == "PartialProductKey").Value);
        Assert.Null(fields.Single(f => f.Label == "LicenseStatus").Value);
    }

    [Fact]
    public async Task BlockedAndDeniedProviderReturnNoRowsWhileCallerCancellationPropagates()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var release = new ManualResetEventSlim();
        IInventorySource provider = new WindowsInventorySource((q, token) =>
        {
            if (q.Class == "Denied") throw new UnauthorizedAccessException();
            release.Wait(token);
            return [];
        }, TimeSpan.FromMilliseconds(150));
        try
        {
            var query = new InventoryQuery("CPU", "Slow", "Name");
            Assert.Empty(await provider.QueryAsync(query, default).WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Empty(await provider.QueryAsync(query with { Class = "Denied" }, default));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.QueryAsync(query, new CancellationToken(true)));
        }
        finally { release.Set(); }
    }
}
