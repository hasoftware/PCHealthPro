using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace PCHealthPro.Core.Inventory;

public sealed class DriveHourHistory(string directory)
{
    internal string Compare(string? model, string? serial, ulong? hours)
    {
        var id = serial?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(model) || string.IsNullOrEmpty(id) || hours == null ||
            id is "UNKNOWN" or "NONE" or "N/A" || id.All(c => c is '0' or '-' or ' '))
            return "HoursUnavailable";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(model.Trim().ToUpperInvariant() + "\n" + id));
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Convert.ToHexString(hash) + ".hours");
            using var gate = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            ulong previous = 0;
            bool known = File.Exists(path) && ulong.TryParse(File.ReadAllText(path), CultureInfo.InvariantCulture, out previous);
            if (known && hours < previous) return "HoursDecreased";
            if (!known || hours > previous)
            {
                var temp = path + ".tmp";
                File.WriteAllText(temp, hours.Value.ToString(CultureInfo.InvariantCulture));
                File.Move(temp, path, true);
            }
            return known ? "HoursConsistent" : "HoursFirst";
        }
        catch (IOException) { return "HoursUnavailable"; }
        catch (UnauthorizedAccessException) { return "HoursUnavailable"; }
    }
}
