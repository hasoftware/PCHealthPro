// Developed by Hoang Anh Dev at HASOFTWARE (hasoftware.vn).
namespace PCHealthPro.Core;

public enum HealthStatus { Good, Attention, Problem, Unknown, Detected, Absent }

public sealed record HardwareReading(string Category, string? Value, HealthStatus Status)
{
    public IReadOnlyList<Inventory.DeviceGroup> Groups { get; init; } = [];
    public bool LimitedSmart { get; init; }
}

public interface IHardwareModule
{
    string Category { get; }
    Task<HardwareReading> ReadAsync(CancellationToken cancellationToken);
}

public sealed class HardwareScan(IEnumerable<IHardwareModule> modules)
{
    public static IReadOnlyList<string> Categories { get; } = ["Machine", "CPU", "RAM", "GPU", "Display", "Network", "Windows", "Camera", "Audio", "Input", "Storage", "Battery"];
    private readonly IHardwareModule[] modules = modules.ToArray();

    public async Task<IReadOnlyList<HardwareReading>> RunAsync(CancellationToken token = default, Func<string, bool>? include = null)
    {
        token.ThrowIfCancellationRequested();
        return await Task.WhenAll(modules.Where(m => include?.Invoke(m.Category) != false).Select(ReadModuleAsync)).ConfigureAwait(false);

        async Task<HardwareReading> ReadModuleAsync(IHardwareModule module)
        {
            try
            {
                return await module.ReadAsync(token).ConfigureAwait(false);
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                return new HardwareReading(module.Category, null, HealthStatus.Unknown);
            }
        }
    }
}
