using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCHealthPro.Core;

namespace PCHealthPro.App.ViewModels;

public sealed partial class OverviewViewModel(HardwareScan scan) : ObservableObject
{
    public DateTimeOffset? CheckedAt { get; private set; }
    [ObservableProperty]
    public partial IReadOnlyList<HardwareReading> Readings { get; private set; } = [];

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken token)
    {
        var readings = await scan.RunAsync(token);
        CheckedAt = DateTimeOffset.Now;
        Readings = readings;
    }
}
