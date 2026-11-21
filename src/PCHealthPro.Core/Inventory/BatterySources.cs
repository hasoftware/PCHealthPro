using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;

namespace PCHealthPro.Core.Inventory;

internal static class BatterySources
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPower
    {
        public byte AC, Flags, Percent, Saver;
        public int Seconds, FullSeconds;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPower status);

    internal static IReadOnlyList<DeviceRow> Presence()
    {
        if (!GetSystemPowerStatus(out var power)) return [];
        return [new() { ["Absent"] = power.Flags != 255 && (power.Flags & 128) != 0,
            ["Percent"] = power.Percent <= 100 ? power.Percent : null,
            ["Minutes"] = power.Seconds >= 0 ? power.Seconds / 60 : null }];
    }

    internal static IReadOnlyList<DeviceRow> Report(CancellationToken token)
    {
        // powercfg writes XML only to a private temporary directory. Capture
        // installed battery entries and discard the machine's usage history.
        var dir = Directory.CreateTempSubdirectory("PCHealthPro-");
        string path = Path.Combine(dir.FullName, "battery.xml");
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in new[] { "/batteryreport", "/xml", "/duration", "1", "/output", path }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process == null) return [];
            try { process.WaitForExitAsync(token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill();
                process.WaitForExit();
                throw;
            }
            if (process.ExitCode != 0 || !File.Exists(path)) return [];
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 16_000_000 });
            return Parse(XDocument.Load(reader));
        }
        finally
        {
            File.Delete(path);
            dir.Delete();
        }
    }

    internal static IReadOnlyList<DeviceRow> Parse(XDocument doc)
    {
        var batteries = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Batteries");
        return batteries?.Elements().Where(e => e.Name.LocalName == "Battery").Select(battery =>
        {
            var row = new DeviceRow();
            foreach (var element in battery.Elements())
                if (element.Name.LocalName is "Id" or "Manufacturer" or "SerialNumber" or "DesignCapacity" or "FullChargeCapacity" or "CycleCount")
                    row[element.Name.LocalName] = element.Value;
            return row;
        }).ToArray() ?? [];
    }
}
