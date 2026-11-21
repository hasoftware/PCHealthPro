using PCHealthPro.Core;

namespace PCHealthPro.App.Services;

internal sealed class SampleModule(HardwareReading reading) : IHardwareModule
{
    internal static readonly HardwareReading[] Overview =
    [
        new("CPU", "Intel Core i5-1240P", HealthStatus.Good),
        new("RAM", "16 GB · DDR4", HealthStatus.Good),
        new("GPU", "Intel Iris Xe", HealthStatus.Good),
        new("Storage", "512 GB · NVMe SSD", HealthStatus.Attention),
        new("Battery", "54 Wh / 80 Wh", HealthStatus.Problem),
        new("Windows", "Windows 11 Pro", HealthStatus.Good)
    ];

    public string Category => reading.Category;

    public async Task<HardwareReading> ReadAsync(CancellationToken token)
    {
        await Task.Delay(300, token);
        return reading;
    }
}
