using System.Text.RegularExpressions;
using PCHealthPro.Core.Benchmark;
using PCHealthPro.Core.Diagnostics;

namespace PCHealthPro.Core.Reporting;

public sealed record ReportNotes(string Seller, string Price, string Condition);
public sealed record BenchmarkReport(string? Volume, int SizeMiB, string Status, BenchmarkSample[] Results);
public sealed record InspectionReport(DateTimeOffset CheckedAt, string Machine, ReportNotes Notes,
    HardwareReading[] Hardware, ManualTestResult[] Tests, BenchmarkReport? Benchmark, bool IdentifiersHidden = false)
{
    private static bool Sensitive(string key) => key is "MACAddress" or "PermanentAddress" or "InstanceName" or "PNPDeviceID"
        || key.Contains("Serial", StringComparison.OrdinalIgnoreCase);

    public InspectionReport HideIdentifiers()
    {
        if (IdentifiersHidden) return this;
        var secrets = Hardware.SelectMany(h => h.Groups).SelectMany(g => g.Fields)
            .Where(f => Sensitive(f.Label) && !string.IsNullOrWhiteSpace(f.Value))
            .Select(f => f.Value!).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(s => s.Length).ToArray();
        string Clean(string value)
        {
            foreach (var secret in secrets)
            {
                // Some firmware reports a single digit as the serial. Do not
                // replace that digit inside unrelated capacities or model names.
                value = secret.Length <= 3
                    ? Regex.Replace(value, @"(?<![\p{L}\p{N}])" + Regex.Escape(secret) + @"(?![\p{L}\p{N}])",
                        "•••", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    : value.Replace(secret, "•••", StringComparison.OrdinalIgnoreCase);
            }
            return Regex.Replace(value, @"(?i)(?<![\da-f])(?:[\da-f]{2}[:-]){5}[\da-f]{2}(?![\da-f])", "•••");
        }
        return this with
        {
            IdentifiersHidden = true, Machine = Clean(Machine),
            Notes = new(Clean(Notes.Seller), Clean(Notes.Price), Clean(Notes.Condition)),
            Hardware = Hardware.Select(h => h with
            {
                Value = h.Value == null ? null : Clean(h.Value),
                Groups = h.Groups.Select(g => g with
                {
                    DeviceName = g.DeviceName == null ? null : Clean(g.DeviceName),
                    Fields = g.Fields.Select(f => Sensitive(f.Label) && !string.IsNullOrEmpty(f.Value) ? f with { Value = "•••", Localize = false } :
                        f.Value == null || f.Localize ? f : f with { Value = Clean(f.Value) }).ToArray()
                }).ToArray()
            }).ToArray()
        };
    }
}
