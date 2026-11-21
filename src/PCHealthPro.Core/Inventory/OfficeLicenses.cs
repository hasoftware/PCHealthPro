using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PCHealthPro.Core.Inventory;

internal static class OfficeLicenses
{
    // Match the local user/device license-state rules used by Microsoft's vNextDiag.
    // Retain only the product and its status for the diagnostic report.
    internal static IReadOnlyList<DeviceRow> Read(CancellationToken token)
    {
        var rows = new List<DeviceRow>();
        foreach (var folder in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.CommonApplicationData })
        {
            var path = Path.Combine(Environment.GetFolderPath(folder), "Microsoft", "Office", "Licenses");
            if (!Directory.Exists(path)) continue;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 4 };
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (file.Length > 1024 * 1024) continue;
                    using var wrapper = JsonDocument.Parse(File.ReadAllText(file.FullName, Encoding.Unicode));
                    if (!wrapper.RootElement.TryGetProperty("License", out var envelope) || envelope.ValueKind != JsonValueKind.String) continue;
                    using var payload = JsonDocument.Parse(envelope.GetBytesFromBase64());
                    if (Describe(payload.RootElement, DateTimeOffset.UtcNow) is { } row) rows.Add(row);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException) { }
            }
        }
        return rows;
    }

    internal static DeviceRow? Describe(JsonElement data, DateTimeOffset now)
    {
        if (!data.TryGetProperty("ProductReleaseId", out var product) || product.ValueKind != JsonValueKind.String ||
            !data.EnumerateObject().Any(p => p.Name.Equals("Metadata", StringComparison.OrdinalIgnoreCase))) return null;
        var meta = data.EnumerateObject().First(p => p.Name.Equals("Metadata", StringComparison.OrdinalIgnoreCase)).Value;
        static DateTimeOffset? Date(JsonElement element, string key) =>
            element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
        var end = Date(meta, "NotAfter");
        var start = Date(meta, "NotBefore");
        var expires = Date(data, "ExpiresOn");
        int? state = end == null || start > now ? null : now > end ? 5 : now >= expires ? 2 : 1;
        return new() { ["Name"] = product.GetString(), ["LicenseStatus"] = state, ["ProductKeyChannel"] = "Microsoft 365 (vNext)" };
    }
}
