using PCHealthPro.Core.Benchmark;
using Xunit;

namespace PCHealthPro.Tests;

public sealed class DiskBenchmarkTests
{
    private sealed class Sink(Action<BenchmarkSample> report) : IProgress<BenchmarkSample>
    {
        public void Report(BenchmarkSample value) => report(value);
    }

    [Fact]
    public void CapacityCheckIncludesReserveAndSupportsFourGiB()
    {
        long length = 4L << 30;
        ulong required = (ulong)length + (16UL << 20);
        Assert.Throws<BenchmarkException>(() => BenchmarkFile.CheckSpace(required - 1, length));
        BenchmarkFile.CheckSpace(required, length);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = new DiskBenchmark().RunAsync("", 3, new Sink(_ => { }), default);
        });
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    [InlineData("error")]
    public async Task DirectIoRunPreservesNeighborsAndCleansUp(string outcome)
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("PCHealthPro-test-");
        var neighbor = Path.Combine(dir.FullName, BenchmarkFile.Prefix + "existing.tmp");
        await File.WriteAllTextAsync(neighbor, "keep");
        using var stop = new CancellationTokenSource();
        var samples = new List<BenchmarkSample>();
        try
        {
            var sink = new Sink(s =>
            {
                samples.Add(s);
                if (s.Phase != BenchmarkPhase.RandomWrite32) return;
                if (outcome == "cancel") stop.Cancel();
                if (outcome == "error") throw new IOException("Injected failure");
            });
            var run = DiskBenchmark.RunFileAsync(dir.FullName, 4L << 20, sink, stop.Token);
            if (outcome == "complete")
            {
                await run;
                var results = samples.Where(s => s.PhaseComplete).ToArray();
                Assert.Equal(Enum.GetValues<BenchmarkPhase>(), results.Select(s => s.Phase));
                Assert.All(results, s =>
                {
                    Assert.True(s.MegabytesPerSecond > 0 && s.Iops > 0);
                    var bytes = s.Phase is BenchmarkPhase.SeqRead or BenchmarkPhase.SeqWrite ? 1 << 20 : 4096;
                    Assert.Equal(bytes, s.MegabytesPerSecond * 1e6 / s.Iops, 4);
                });
                Assert.Equal(1d, samples.Last().Progress);
            }
            else if (outcome == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            else await Assert.ThrowsAsync<IOException>(() => run);
            Assert.Equal([neighbor], Directory.GetFiles(dir.FullName));
            Assert.Equal("keep", await File.ReadAllTextAsync(neighbor));
        }
        finally
        {
            File.Delete(neighbor);
            dir.Delete();
        }
    }
}
