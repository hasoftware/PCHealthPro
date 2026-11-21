using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCHealthPro.Core.Reporting;

public static class ReportFiles
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static void Write(string path, InspectionReport report, ReportLayout layout)
    {
        AtomicWrite(path, stream =>
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".pdf": ReportPdf.Write(stream, layout); break;
                case ".html": using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)) writer.Write(Html(layout)); break;
                case ".json": JsonSerializer.Serialize(stream, report, Json); break;
                default: throw new ArgumentException("Unsupported report extension.");
            }
        });
    }

    internal static void AtomicWrite(string path, Action<Stream> write)
    {
        var temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".report-{Guid.NewGuid():N}.tmp");
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            try { write(stream); stream.Flush(true); }
            catch { stream.Dispose(); File.Delete(temp); throw; }
        }
        try { File.Move(temp, path, true); }
        finally { File.Delete(temp); }
    }

    public static string Html(ReportLayout layout)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var html = new StringBuilder("<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'><title>")
            .Append(E(layout.Title)).Append("</title><style>*{box-sizing:border-box}body{font:15px/1.5 'Segoe UI',sans-serif;color:#172d38;max-width:960px;margin:auto;padding:32px}header{border-bottom:3px solid #087565;padding-bottom:16px}.mark{background:#095c52;color:white;padding:5px 8px;font-weight:bold}h1{font-size:2rem;margin:.6em 0 .2em}h2{font-size:1.1rem;color:#095c52;margin-top:1.7em}table{border-collapse:collapse;width:100%;table-layout:fixed}th,td{text-align:left;vertical-align:top;padding:6px 10px;border-bottom:1px solid #d8e4e1;overflow-wrap:anywhere;white-space:pre-wrap}th{width:36%;font-weight:600}tr:nth-child(odd){background:#f2f7f6}@media print{h2{break-after:avoid}tr{break-inside:avoid}body{padding:0}}</style></head><body><header><span class='mark'>PC</span> ")
            .Append(E(layout.Brand)).Append("<h1>").Append(E(layout.Title)).Append("</h1>").Append(E(layout.Machine))
            .Append(" · ").Append(E(layout.Date)).Append("</header>");
        foreach (var group in layout.Sections)
        {
            html.Append("<section><h2>").Append(E(group.Title)).Append("</h2><table>");
            foreach (var line in group.Lines) html.Append("<tr><th scope='row'>").Append(E(line.Label)).Append("</th><td>").Append(E(line.Value)).Append("</td></tr>");
            html.Append("</table></section>");
        }
        return html.Append("</body></html>").ToString();
    }

    public static InspectionReport Read(string path)
    {
        using var input = File.OpenRead(path);
        var r = JsonSerializer.Deserialize<InspectionReport>(input, Json);
        if (r?.Hardware == null || r.Tests == null || r.Notes == null || r.Machine == null ||
            r.Notes.Seller == null || r.Notes.Price == null || r.Notes.Condition == null ||
            r.Hardware.Any(h => h?.Category == null || h.Groups == null || h.Groups.Any(g =>
                g?.Label == null || g.Fields == null || g.Fields.Any(f => f?.Label == null))) ||
            r.Tests.Any(t => t == null) || r.Benchmark is { } b &&
            (b.Status == null || b.Results == null || b.Results.Any(sample => sample == null))) throw new InvalidDataException();
        return r;
    }

    public static string SaveHistory(string directory, InspectionReport report)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{report.CheckedAt.UtcDateTime:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        AtomicWrite(path, stream => JsonSerializer.Serialize(stream, report, Json));
        return path;
    }

    public static IReadOnlyList<InspectionReport> History(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var reports = new List<InspectionReport>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try { reports.Add(Read(path)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { }
        }
        return reports.OrderByDescending(r => r.CheckedAt).ToArray();
    }
}
