using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace PCHealthPro.Core.Benchmark;

public enum BenchmarkPhase { SeqWrite, SeqRead, RandomWrite1, RandomRead1, RandomWrite32, RandomRead32 }

public sealed record BenchmarkSample(BenchmarkPhase Phase, double Progress, double MegabytesPerSecond,
    double Iops, bool PhaseComplete);

public sealed class BenchmarkException(string resourceKey) : IOException(resourceKey)
{
    public string ResourceKey { get; } = resourceKey;
}

public sealed class DiskBenchmark
{
    private const int MiB = 1024 * 1024;
    private static readonly BenchmarkPhase[] Phases = Enum.GetValues<BenchmarkPhase>();
    public static IReadOnlyList<int> SizesMiB { get; } = Array.AsReadOnly(new[] { 256, 1024, 4096 });

    public Task RunAsync(string directory, int sizeMiB, IProgress<BenchmarkSample> progress, CancellationToken token)
    {
        if (!SizesMiB.Contains(sizeMiB)) throw new ArgumentOutOfRangeException(nameof(sizeMiB));
        return RunFileAsync(directory, sizeMiB * (long)MiB, progress, token);
    }

    internal static Task RunFileAsync(string directory, long length, IProgress<BenchmarkSample> progress, CancellationToken token) =>
        Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            using var handle = BenchmarkFile.Create(directory, length, out int alignment);
            foreach (var phase in Phases)
            {
                token.ThrowIfCancellationRequested();
                await MeasureAsync(phase, handle, length, alignment, progress, token).ConfigureAwait(false);
            }
        }, token);

    private static async Task MeasureAsync(BenchmarkPhase phase, SafeFileHandle handle, long length, int alignment,
        IProgress<BenchmarkSample> progress, CancellationToken token)
    {
        bool sequential = phase is BenchmarkPhase.SeqWrite or BenchmarkPhase.SeqRead;
        bool write = phase is BenchmarkPhase.SeqWrite or BenchmarkPhase.RandomWrite1 or BenchmarkPhase.RandomWrite32;
        int depth = phase is BenchmarkPhase.RandomWrite32 or BenchmarkPhase.RandomRead32 ? 32 : 1;
        int block = sequential ? MiB : 4096;
        long blocks = length / block;
        var buffers = Enumerable.Range(0, depth).Select(_ => BenchmarkFile.Buffer(block, alignment)).ToArray();
        if (write) foreach (var buffer in buffers) RandomNumberGenerator.Fill(buffer.Span);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        long completed = 0;
        var clock = Stopwatch.StartNew();
        // Random stages sample for up to three seconds or one file's worth of I/O.
        // Each lane owns disjoint offsets, even when 32 writes are in flight.
        async Task RunLane(int lane)
        {
            try
            {
                long laneBlocks = (blocks - 1 - lane) / depth + 1;
                var buffer = buffers[lane];
                for (long i = 0; i < laneBlocks; i++)
                {
                    stop.Token.ThrowIfCancellationRequested();
                    if (!sequential && i > 0 && clock.Elapsed.TotalSeconds >= 3) break;
                    long index = sequential ? i : lane + depth * Random.Shared.NextInt64(laneBlocks);
                    if (write) await RandomAccess.WriteAsync(handle, buffer, index * block, stop.Token).ConfigureAwait(false);
                    else if (await RandomAccess.ReadAsync(handle, buffer, index * block, stop.Token).ConfigureAwait(false) != block)
                        throw new EndOfStreamException();
                    Interlocked.Increment(ref completed);
                }
            }
            catch { stop.Cancel(); throw; }
        }

        progress.Report(new(phase, (double)(int)phase / Phases.Length, 0, 0, false));
        var work = Task.WhenAll(Enumerable.Range(0, depth).Select(lane => Task.Run(() => RunLane(lane))));
        double lastTime = 0;
        long lastCount = 0;
        try
        {
            while (await Task.WhenAny(work, Task.Delay(250)).ConfigureAwait(false) != work)
            {
                var count = Interlocked.Read(ref completed);
                double seconds = clock.Elapsed.TotalSeconds;
                double fraction = (double)count / blocks;
                if (!sequential) fraction = Math.Max(fraction, Math.Min(seconds / 3, 0.99));
                double iops = (count - lastCount) / (seconds - lastTime);
                progress.Report(new(phase, ((int)phase + fraction) / Phases.Length, iops * block / 1e6, iops, false));
                (lastTime, lastCount) = (seconds, count);
            }
        }
        finally
        {
            if (!work.IsCompleted) stop.Cancel();
            // Drain outstanding I/O before any buffer or file handle is released.
            await work.ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        double average = completed / clock.Elapsed.TotalSeconds;
        progress.Report(new(phase, ((int)phase + 1d) / Phases.Length, average * block / 1e6, average, true));
    }
}
