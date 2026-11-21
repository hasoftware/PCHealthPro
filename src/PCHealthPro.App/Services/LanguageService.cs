using Microsoft.Windows.ApplicationModel.Resources;
using PCHealthPro.Core;

namespace PCHealthPro.App.Services;

public sealed class LanguageService
{
    private readonly ResourceManager manager = new();
    private ResourceContext context;
    private readonly string preferences = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HASOFTWARE", "PCHealthPro");
    public string Selection { get; private set; } = "system";
    private readonly Dictionary<string, string> settings = new(StringComparer.OrdinalIgnoreCase);
    public string SettingsPath => Path.Combine(preferences, "settings.ini");
    public bool ShowHint => !Flag("intro-seen");
    public bool CheckUpdates => Flag("check-updates");
    public bool IncludeBenchmark => Flag("benchmark");
    public bool ManualEnabled(string key) => Flag("manual." + key, true);
    public void SetManual(string key, bool enabled) => Save("manual." + key, enabled.ToString());
    public bool ScanEnabled(string key) => Flag("scan." + key, true);
    private bool Flag(string key, bool fallback = false) =>
        settings.TryGetValue(key, out var v) && bool.TryParse(v, out var value) ? value : fallback;
    public void SetUpdates(bool enabled) => Save("check-updates", enabled.ToString());
    public void SetScan(string key, bool enabled) => Save("scan." + key, enabled.ToString());
    public void SetBenchmark(bool enabled) => Save("benchmark", enabled.ToString());

    public LanguageService()
    {
        context = manager.CreateResourceContext();
        try
        {
            if (File.Exists(SettingsPath))
            {
                foreach (string line in File.ReadLines(SettingsPath))
                {
                    int split = line.IndexOf('=');
                    if (split > 0) settings[line[..split].Trim()] = line[(split + 1)..].Trim();
                }
            }
            else
            {
                settings["check-updates"] = File.Exists(Path.Combine(preferences, "check-updates")).ToString();
                settings["intro-seen"] = File.Exists(Path.Combine(preferences, "intro-seen")).ToString();
                settings["benchmark"] = "False";
                foreach (var key in HardwareScan.Categories) settings["scan." + key] = "True";
                var old = Path.Combine(preferences, "language");
                Save("language", File.Exists(old) ? File.ReadAllText(old).Trim() : "system");
            }
            Apply(settings.GetValueOrDefault("language") ?? "system");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public string Get(string key) => manager.MainResourceMap.TryGetValue("Resources/" + key, context)?.ValueAsString ?? key;

    public void Change(string language)
    {
        Save("language", language);
        Apply(language);
    }

    public void DismissHint()
    {
        try { Save("intro-seen", "True"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void Apply(string language)
    {
        Selection = language is "en-US" or "vi-VN" ? language : "system";
        context = manager.CreateResourceContext();
        if (Selection != "system") context.QualifierValues["Language"] = Selection;
    }

    private void Save(string name, string value)
    {
        Directory.CreateDirectory(preferences);
        using var fileLock = new FileStream(SettingsPath + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        var next = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);
        if (File.Exists(SettingsPath))
            foreach (string line in File.ReadLines(SettingsPath))
            {
                int split = line.IndexOf('=');
                if (split > 0) next[line[..split].Trim()] = line[(split + 1)..].Trim();
            }
        next[name] = value;
        var temp = Path.Combine(preferences, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllLines(temp, new[] { "[PCHealthPro]" }.Concat(next.Select(kv => $"{kv.Key}={kv.Value}")));
            File.Move(temp, SettingsPath, true);
            settings[name] = value;
        }
        finally { File.Delete(temp); }
    }
}
