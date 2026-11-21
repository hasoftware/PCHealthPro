using Microsoft.Windows.ApplicationModel.Resources;

namespace PCHealthPro.App.Services;

public sealed class LanguageService
{
    private readonly ResourceManager manager = new();
    private ResourceContext context;
    private readonly string preferences = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HASOFTWARE", "PCHealthPro");
    public string Selection { get; private set; } = "system";
    public bool ShowHint => !File.Exists(Path.Combine(preferences, "intro-seen"));
    public bool CheckUpdates => File.Exists(Path.Combine(preferences, "check-updates"));
    public void SetUpdates(bool enabled)
    {
        if (enabled) Save("check-updates", "1");
        else File.Delete(Path.Combine(preferences, "check-updates"));
    }

    public LanguageService()
    {
        context = manager.CreateResourceContext();
        try { Apply(File.ReadAllText(Path.Combine(preferences, "language"))); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public string Get(string key) => manager.MainResourceMap.TryGetValue("Resources/" + key, context)?.ValueAsString ?? key;

    public void Change(string language)
    {
        Apply(language);
        Save("language", Selection);
    }

    public void DismissHint() => Save("intro-seen", "1");

    private void Apply(string language)
    {
        Selection = language is "en-US" or "vi-VN" ? language : "system";
        context = manager.CreateResourceContext();
        if (Selection != "system") context.QualifierValues["Language"] = Selection;
    }

    private void Save(string name, string value)
    {
        try
        {
            Directory.CreateDirectory(preferences);
            File.WriteAllText(Path.Combine(preferences, name), value);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
