using CommunityToolkit.Mvvm.ComponentModel;
using PCHealthPro.Core.Benchmark;

namespace PCHealthPro.App.ViewModels;

public sealed class BenchmarkViewModel(DiskBenchmark benchmark) : ObservableObject
{
    private CancellationTokenSource? active;
    public bool IsRunning => active != null;
    public BenchmarkSample? Latest { get; private set; }
    public List<BenchmarkSample> Results { get; } = [];
    public List<double> Series { get; } = [];
    public string Status { get; private set; } = "";
    public string? ErrorDetail { get; private set; }

    public async Task RunAsync(string directory, int sizeMiB)
    {
        if (IsRunning) return;
        using var run = new CancellationTokenSource();
        active = run;
        Results.Clear();
        Series.Clear();
        Latest = null;
        ErrorDetail = null;
        Status = "BenchRunning";
        OnPropertyChanged(string.Empty);
        var progress = new Progress<BenchmarkSample>(s =>
        {
            if (active != run) return;
            if (Latest?.Phase != s.Phase) Series.Clear();
            if (s.PhaseComplete) Results.Add(s);
            // Final samples are phase averages. Keep the trace as interval rates.
            if (!s.PhaseComplete || Series.Count <= 1) Series.Add(s.MegabytesPerSecond);
            if (Series.Count > 80) Series.RemoveAt(0);
            Latest = s;
            OnPropertyChanged(string.Empty);
        });
        try
        {
            await benchmark.RunAsync(directory, sizeMiB, progress, run.Token);
            Status = "BenchDone";
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested) { Status = "BenchCanceled"; }
        catch (BenchmarkException e) { Status = e.ResourceKey; }
        catch (Exception e)
        {
            Status = "BenchFailed";
            ErrorDetail = e.Message;
        }
        finally
        {
            active = null;
            OnPropertyChanged(string.Empty);
        }
    }

    public void Cancel() => active?.Cancel();
}
