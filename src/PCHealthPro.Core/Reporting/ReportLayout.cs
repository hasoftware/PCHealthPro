using System.Reflection;

namespace PCHealthPro.Core.Reporting;

public sealed record ReportLine(string Label, string Value);
public sealed record ReportSection(string Title, List<ReportLine> Lines);
public sealed record ReportLayout(string Brand, string Title, string Machine, string Date, List<ReportSection> Sections)
{
    public static ReportLayout Create(InspectionReport r, Func<string, string> t)
    {
        string Show(string? value) => string.IsNullOrWhiteSpace(value) ? t("Unknown") : value;
        var sections = new List<ReportSection>();
        var notes = new List<ReportLine>();
        if (r.Notes.Seller.Length > 0) notes.Add(new(t("ReportSeller"), r.Notes.Seller));
        if (r.Notes.Price.Length > 0) notes.Add(new(t("ReportPrice"), r.Notes.Price));
        if (r.Notes.Condition.Length > 0) notes.Add(new(t("ReportCondition"), r.Notes.Condition));
        if (notes.Count > 0) sections.Add(new(t("ReportNotes"), notes));
        sections.Add(new(t("Overview"), r.Hardware.Select(h => new ReportLine(t(h.Category),
            $"{Show(h.Value)} · {t(h.Status.ToString())}")).ToList()));
        sections.Add(new(t("ManualResults"), r.Tests.Select(test => new ReportLine(t("Test" + test.Test), t("Verdict" + test.Verdict))).ToList()));
        if (r.Benchmark is { } b)
        {
            var lines = new List<ReportLine> { new(t("BenchVolume"), $"{b.Volume} · {b.SizeMiB} MiB · {t(b.Status)}") };
            lines.AddRange(b.Results.Select(s => new ReportLine(t(s.Phase.ToString()), $"{s.MegabytesPerSecond:N2} MB/s · {s.Iops:N0} IOPS")));
            sections.Add(new(t("BenchTitle"), lines));
        }
        foreach (var hardware in r.Hardware)
        foreach (var g in hardware.Groups)
        {
            var title = g.DeviceName ?? t(g.Label);
            if (title != t(hardware.Category)) title = $"{t(hardware.Category)} / {title}";
            sections.Add(new(title, g.Fields.Select(f => new ReportLine(t(f.Label),
                f.Localize && f.Value != null ? t(f.Value) : Show(f.Value))).ToList()));
        }
        var brand = typeof(InspectionReport).Assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product;
        return new(brand, t("ReportTitle"), r.Machine, r.CheckedAt.ToString("yyyy-MM-dd HH:mm zzz"), sections);
    }
}
