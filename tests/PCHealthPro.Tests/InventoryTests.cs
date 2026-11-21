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
