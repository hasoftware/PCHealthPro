using System.Text;
using PCHealthPro.Core;
using PCHealthPro.Core.Diagnostics;
using PCHealthPro.Core.Inventory;
using PCHealthPro.Core.Reporting;
using Xunit;

namespace PCHealthPro.Tests;

public class ReportTests
{
    private static readonly InspectionReport Report = new(DateTimeOffset.UtcNow, "LAB-PC",
        new("Nguyễn An", "10 triệu", "<script>alert(1)</script> SN-12345"),
        [new("Machine", "Portable SN-12345", HealthStatus.Detected)
        {
            Groups = [new("BIOS", [new("SerialNumber", "SN-12345"), new("PermanentAddress", "00-11-22-33-44-55"),
                new("Description", "MAC 00:11:22:33:44:55")]) { DeviceName = "SN-12345" }]
        }], new ManualTestSession().Snapshot(), null);

    [Fact]
    public void RedactionCoversDerivedFieldsAndLeavesOriginalAvailable()
    {
        var masked = Report.HideIdentifiers();
        Assert.Same(masked, masked.HideIdentifiers());
        var html = ReportFiles.Html(ReportLayout.Create(masked, k => k));
        Assert.DoesNotContain("SN-12345", html);
        Assert.DoesNotContain("00:11:22:33:44:55", html);
        Assert.DoesNotContain("00-11-22-33-44-55", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Equal("SN-12345", Report.Hardware[0].Groups[0].DeviceName);
    }

    [Fact]
    public void ShortFirmwareSerialPreservesUnrelatedNumbers()
    {
        var shortSerial = Report with { Hardware = [new("Machine", "SSD 1000 GB", HealthStatus.Detected)
        {
            Groups = [new("BIOS", [new("SerialNumber", "0"), new("MACAddress", null)])]
        }] };
        var masked = shortSerial.HideIdentifiers().Hardware[0];
        Assert.Equal("SSD 1000 GB", masked.Value);
        Assert.Equal("•••", masked.Groups[0].Fields[0].Value);
        Assert.Null(masked.Groups[0].Fields[1].Value);
    }

    [Fact]
    public void HistoryPreservesSnapshotsAndSkipsBrokenEntries()
    {
        var directory = Directory.CreateTempSubdirectory("PCHealth-history-test-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "broken.json"), "{}");
            var paths = Enumerable.Range(0, 4).AsParallel()
                .Select(_ => ReportFiles.SaveHistory(directory.FullName, Report.HideIdentifiers())).ToArray();
            Assert.Equal(4, paths.Distinct().Count());
            var entries = ReportFiles.History(directory.FullName);
            Assert.Equal(4, entries.Count);
            Assert.All(entries, e => { Assert.True(e.IdentifiersHidden); Assert.Equal(6, e.Tests.Length); });
            Assert.DoesNotContain("SN-12345", File.ReadAllText(paths[0]));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void ExportWritesThreeFormatsAndProtectsExistingFileOnFailure()
    {
        var dir = Directory.CreateTempSubdirectory("PCHealth-export-test-");
        try
        {
            var safe = Report.HideIdentifiers();
            var layout = ReportLayout.Create(safe, k => k);
            foreach (var ext in new[] { "json", "html", "pdf" })
            {
                var path = Path.Combine(dir.FullName, "report." + ext);
                ReportFiles.Write(path, safe, layout);
                var bytes = File.ReadAllBytes(path);
                Assert.True(bytes.Length > 300);
                if (ext == "pdf") Assert.StartsWith("%PDF", Encoding.ASCII.GetString(bytes, 0, 5));
                if (ext == "json") Assert.Equal(Report.Machine, ReportFiles.Read(path).Machine);
            }
            var previous = Path.Combine(dir.FullName, "last.json");
            File.WriteAllText(previous, "original");
            Assert.Throws<IOException>(() => ReportFiles.AtomicWrite(previous, stream => { stream.WriteByte(1); throw new IOException(); }));
            Assert.Equal("original", File.ReadAllText(previous));
            Assert.Empty(Directory.GetFiles(dir.FullName, "*.tmp"));
        }
        finally { dir.Delete(true); }
    }
}
