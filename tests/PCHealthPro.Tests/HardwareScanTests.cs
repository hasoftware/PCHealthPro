using PCHealthPro.Core;
using Xunit;

namespace PCHealthPro.Tests;

public class HardwareScanTests
{
    private sealed record Module(string Category, bool Fail = false) : IHardwareModule
    {
        public Task<HardwareReading> ReadAsync(CancellationToken token) =>
            Fail ? throw new IOException() : Task.FromResult(new HardwareReading(Category, "16 GB", HealthStatus.Good));
    }

    [Fact]
    public async Task ScanKeepsOtherReadingsWhenOneModuleFails()
    {
        var result = await new HardwareScan([new Module("Disk", true), new Module("RAM")]).RunAsync();
        Assert.Equal(new HardwareReading("Disk", null, HealthStatus.Unknown), result[0]);
        Assert.Equal(new HardwareReading("RAM", "16 GB", HealthStatus.Good), result[1]);
    }

    [Fact]
    public async Task CancelledScanPropagatesCancellation()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new HardwareScan([new Module("RAM")]).RunAsync(new CancellationToken(true)));
    }
}
