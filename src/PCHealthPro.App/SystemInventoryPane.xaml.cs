using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PCHealthPro.App.Services;
using PCHealthPro.Core;
using PCHealthPro.Core.Inventory;
using Windows.ApplicationModel.DataTransfer;

namespace PCHealthPro.App;

public sealed partial class SystemInventoryPane : UserControl
{
    private LanguageService strings = null!;
    private IReadOnlyList<HardwareReading> readings = [];
    private string page = "";
    private bool buildingTabs;
    private bool hasScanned;

    public SystemInventoryPane() => InitializeComponent();

    internal void Show(string section, LanguageService language, IReadOnlyList<HardwareReading> data, bool scanComplete)
    {
        strings = language;
        readings = data;
        hasScanned = scanComplete;
        if (page != section)
        {
            page = section;
            buildingTabs = true;
            Categories.Items.Clear();
            string[] tabs = page switch { "Compute" => ["Compute", "PlatformTab"], "Network" => ["AdaptersTab", "WirelessTab", "Bluetooth"],
                "GPU" or "Display" => [], _ => ["Windows", "OfficeTab", "LicenseTab"] };
            foreach (var key in tabs)
            {
                var item = new SelectorBarItem { Tag = key };
                AutomationProperties.SetAutomationId(item, "Tab" + key);
                Categories.Items.Add(item);
            }
            Categories.Visibility = tabs.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (tabs.Length > 0) Categories.SelectedItem = Categories.Items[0];
            buildingTabs = false;
        }
        foreach (var tab in Categories.Items) tab.Text = strings.Get((string)tab.Tag switch
            { "Compute" => "ComputeSummaryTab", "Windows" => "OperatingSystemTab", var key => key });
        Draw();
    }

    internal void OpenHardware(string category)
    {
        Categories.SelectedItem = Categories.Items[0];
        var card = List.Children.OfType<DeviceCard>().FirstOrDefault(c => Equals(c.Tag, category));
        if (card != null) card.DispatcherQueue.TryEnqueue(card.FocusSummary);
    }

    private void ChangeTab(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!buildingTabs && strings != null) Draw();
    }

    private void Draw()
    {
        List.Children.Clear();
        var tab = Categories.SelectedItem?.Tag as string ?? page;
        string scope = tab switch { "Compute" => "CPU", "PlatformTab" => "Machine", "GPU" or "Display" => page,
            "AdaptersTab" or "WirelessTab" or "Bluetooth" => "Network", _ => "Windows" };
        if (hasScanned && Reading(scope) == null && !(tab == "Compute" && Reading("RAM") != null))
        {
            Empty.Visibility = Visibility.Visible;
            Empty.Text = strings.Get("ScopeDisabled");
            return;
        }
        switch (tab)
        {
            case "Compute": Compute(); break;
            case "PlatformTab": Platform(); break;
            case "AdaptersTab": Adapters(); break;
            case "WirelessTab": Wireless(); break;
            case "Bluetooth": Bluetooth(); break;
            case "GPU": Graphics(); break;
            case "Display": Displays(); break;
            case "LicenseTab": Licenses(); break;
            case "Windows": WindowsPage(); break;
            case "OfficeTab": Office(); break;
        }
        Empty.Visibility = List.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Empty.Text = strings.Get(readings.Count == 0 ? "Busy" : tab switch
        {
            "WirelessTab" => "WifiUnavailable", "Bluetooth" => "BluetoothUnavailable",
            "OfficeTab" => "OfficeUnavailable", "AdaptersTab" => "AdaptersUnavailable", _ => "Unknown"
        });
    }

    private HardwareReading? Reading(string category) => readings.FirstOrDefault(r => r.Category == category);
    private DeviceGroup[] Groups(string category) => Reading(category)?.Groups.Where(g => g.Fields.Any(f => f.Value != null)).ToArray() ?? [];
    private static InventoryField? Field(DeviceGroup g, string key) => g.Fields.FirstOrDefault(f => f.Label == key);
    private static string? Value(DeviceGroup g, string key) => Field(g, key)?.Value;
    private static InventoryField[] Metrics(DeviceGroup g, params string[] keys) => keys.Select(k => Field(g, k) ?? new(k, null)).ToArray();

    private DeviceCard Add(string category, string glyph, string title, string? subtitle, IEnumerable<InventoryField> metrics,
        IReadOnlyList<DeviceGroup> groups)
    {
        var card = new DeviceCard();
        card.Bind(strings, glyph, title, subtitle, metrics, groups, strings.Get("Details") + " · " + strings.Get(category));
        card.Identify(category);
        var mark = category switch
        {
            "CPU" or "GPU" => BrandMark.Chip(groups.Select(g => Value(g, "Manufacturer")).FirstOrDefault(v => v != null) ?? title),
            "Windows" or "WindowsActivation" => BrandMark.Windows(Reading("Windows")?.Value),
            "Office" or "OfficeActivation" or "OfficeLegacy" or "OfficeSubscription" => BrandMark.Office(title),
            _ => null
        };
        card.SetBrand(mark);
        var copy = new MenuFlyoutItem { Text = strings.Get("Copy") };
        copy.Click += (_, _) => { var data = new DataPackage(); data.SetText(title); Clipboard.SetContent(data); };
        card.ContextFlyout = new MenuFlyout { Items = { copy } };
        List.Children.Add(card);
        return card;
    }

    private void Compute()
    {
        if (readings.Count == 0) return;
        var cpu = Groups("CPU");
        var processors = cpu.Where(g => Field(g, "NumberOfCores") != null).ToArray();
        foreach (var g in processors)
            Add("CPU", "\uE950", Value(g, "Name") ?? strings.Get("CPU"), strings.Get("CPU"),
                Metrics(g, "NumberOfCores", "NumberOfLogicalProcessors").Append(processors.Length == 1 ? cpu.SelectMany(x => x.Fields).FirstOrDefault(x => x.Label is "BaseClock" or "RatedBaseClock" && x.Value != null) ?? new("BaseClock", null) : new("BaseClock", null)), processors.Length == 1 ? cpu : [g]);
        if (processors.Length == 0 || processors.Length > 1 && cpu.Any(g => Field(g, "NominalMHz") != null))
            Add("CPU", "\uE950", strings.Get("CPU"), cpu.Length == 0 ? strings.Get("Unknown") : null, cpu.SelectMany(g => g.Fields).Where(f => f.Label == "NominalMHz"),
                cpu.Except(processors).ToArray());
        var ram = Groups("RAM");
        InventoryField DistinctValues(string key)
        {
            var values = ram.Select(g => Value(g, key)).OfType<string>().Distinct().ToArray();
            return new(key, values.Length == 0 ? null : string.Join(" · ", values));
        }
        Add("RAM", "\uE964", Reading("RAM")?.Value ?? strings.Get("Unknown"), strings.Get("RAM"),
            [DistinctValues("SMBIOSMemoryType"), DistinctValues("ConfiguredClockSpeed"),
                ram.Select(g => Field(g, "MemoryLayout")).FirstOrDefault(f => f != null) ?? DistinctValues("FreeSlots")], ram);
    }

    private void Platform()
    {
        foreach (var group in Groups("Machine").GroupBy(g => g.Label))
        {
            var g = group.First();
            var title = Value(g, "ProductLine") ?? Value(g, "Product") ?? Value(g, "SMBIOSBIOSVersion") ?? strings.Get(g.Label);
            var metrics = g.Fields.Where(f => f.Label is "Model" or "Manufacturer" or "PCSystemType" or "ReleaseDate" or
                "SpecVersion" or "SecureBootState" or "IsEnabled_InitialValue").ToArray();
            Add(g.Label, "\uE950", title, title == strings.Get(g.Label) ? null : strings.Get(g.Label), metrics, group.ToArray());
        }
    }

    private void Adapters()
    {
        var data = Groups("Network").Where(g => g.Label == "Network").ToArray();
        var main = data.Where(g => Field(g, "InterfaceDescription") != null).ToArray();
        var legacy = data.Except(main).ToArray();
        foreach (var g in main.Length > 0 ? main : legacy)
        {
            var kind = Value(g, "InterfaceType");
            Add("AdaptersTab", kind == "Wi-Fi" ? "\uE701" : "\uE839", Value(g, "InterfaceDescription") ?? Value(g, "Name") ?? strings.Get("AdaptersTab"),
                string.Join(" · ", new[] { kind, main.Length > 0 ? Value(g, "Name") : null }.OfType<string>().Distinct()),
                main.Length > 0 ? Metrics(g, "ReceiveLinkSpeed", "TransmitLinkSpeed", "DriverVersionString") : Metrics(g, "Manufacturer"), [g]);
        }
        if (main.Length > 0 && legacy.Length > 0)
            Add("AdapterInventory", "\uE839", strings.Get("AdapterInventory"), null, [], legacy);
    }

    private void Wireless()
    {
        foreach (var g in Groups("Network").Where(g => g.Label == "Wifi"))
            Add("Wifi", "\uE701", Value(g, "InterfaceDescription") ?? strings.Get("WirelessTab"), strings.Get("Wifi"),
                Metrics(g, "RadioTypes"), [g]);
    }

    private void Bluetooth()
    {
        foreach (var g in Groups("Network").Where(g => g.Label == "Bluetooth"))
            Add("Bluetooth", "\uE702", Value(g, "Name") ?? strings.Get("Bluetooth"), null, Metrics(g, "Manufacturer", "Status"), [g]);
    }

    private void WindowsPage()
    {
        foreach (var g in Groups("Windows").Where(g => g.Label == "Windows"))
            Add("Windows", "\uE770", Value(g, "Caption") ?? "Windows", Value(g, "Version"),
                Metrics(g, "BuildNumber", "OSArchitecture", "InstallDate"), [g]);

    }

    private void Office()
    {
        foreach (var g in Groups("Windows").Where(g => g.Label == "Office"))
            foreach (var product in (Value(g, "ProductReleaseIds") ?? strings.Get("Office")).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                Add("Office", "\uE8A5", product, strings.Get("Office"), Metrics(g, "VersionToReport"), [g]);
        if (List.Children.Count == 0)
            foreach (var g in Groups("Windows").Where(g => g.Label is "OfficeActivation" or "OfficeLegacy" or "OfficeSubscription"))
                Add("Office", "\uE8A5", Value(g, "Name") ?? strings.Get("Office"), null, [], [g]);
    }

    private void Licenses()
    {
        if (readings.Count == 0) return;
        List.Children.Add(new TextBlock { Text = strings.Get("LicenseEvidence"), TextWrapping = TextWrapping.Wrap });
        Activation("WindowsActivation");
        Activation("OfficeSubscription");
        Activation("OfficeActivation");
        Activation("OfficeLegacy");
        if (!Groups("Windows").Any(g => g.Label is "OfficeActivation" or "OfficeLegacy" or "OfficeSubscription"))
        {
            var installed = Groups("Windows").Where(g => g.Label == "Office").Select(g => Value(g, "ProductReleaseIds")).FirstOrDefault();
            Add("Office", "\uE8A5", installed ?? strings.Get("Office"), strings.Get("OfficeLicenseMissing"), [], []);
        }
    }

    private void Graphics()
    {
        foreach (var g in Groups("GPU"))
            Add("GPU", "\uE7F4", Value(g, "Name") ?? strings.Get("GPU"), strings.Get(Value(g, "GpuType") ?? "GpuUnknown"),
                Metrics(g, "Manufacturer", "AdapterRAM", "DriverVersion"), [g]);
    }

    private void Displays()
    {
        var groups = Groups("Display");
        var panels = groups.Where(g => g.Label == "PanelSummary");
        foreach (var g in panels)
        {
            var name = Value(g, "PanelName") ?? Value(g, "UserFriendlyName");
            var id = Value(g, "PanelVendor") ?? Value(g, "ManufacturerName");
            var maker = id switch { "DEL" => "Dell", "SAM" => "Samsung", "GSM" => "LG", "ACI" or "AUS" => "ASUS", "AOC" => "AOC", "ACR" => "Acer", "BNQ" => "BenQ", "HWP" or "HPN" => "HP", "LEN" => "Lenovo", "MSI" => "MSI", "XMI" => "Xiaomi", "BOE" => "BOE", "AUO" => "AU Optronics", _ => id };
            Add("Display", "\uE7F4", name ?? Value(g, "Name") ?? strings.Get("Display"), Value(g, "DisplayName"),
                new[] { new InventoryField("Manufacturer", maker) }.Concat(Value(g, "Resolution") == null ? Metrics(g, "PreferredMode", "MaxHorizontalImageSize") : Metrics(g, "Resolution", "RefreshHz")),
                [g]);
        }
    }

    private void Activation(string key)
    {
        var products = Groups("Windows");
        var licenses = products.Where(g => g.Label == key).ToArray();
        if (key == "OfficeActivation" && products.Any(g => g.Label == "OfficeSubscription"))
            licenses = licenses.Where(g => Value(g, "Name")?.Contains("Subscription", StringComparison.OrdinalIgnoreCase) != true).ToArray();
        var validation = Groups("Windows").FirstOrDefault(g => g.Label == "WindowsValidation");
        var genuine = validation == null ? new InventoryField("GenuineState", null) : Field(validation, "GenuineState")!;
        if (key == "WindowsActivation" && licenses.Length == 0)
            Add(key, "\uE72E", Reading("Windows")?.Value ?? "Windows", null, [validation == null ? new("LicenseStatus", null) : Field(validation, "LicenseStatus") ?? new("LicenseStatus", null), genuine], validation == null ? [] : [validation]);
        foreach (var g in licenses)
        {
            var metrics = Metrics(g, "LicenseStatus").ToList();
            var desc = Value(g, "Description") ?? "";
            var channel = Value(g, "ProductKeyChannel") ?? new[] { "VOLUME_KMSCLIENT", "VOLUME_MAK", "OEM_DM", "OEM", "RETAIL" }
                .FirstOrDefault(c => desc.Contains(c, StringComparison.OrdinalIgnoreCase));
            metrics.Add(new("ProductKeyChannel", channel));
            if (key == "WindowsActivation") metrics.Add(genuine);
            var card = Add(key, "\uE72E", Value(g, "Name") ?? strings.Get(key), strings.Get(key), metrics, [g]);
            if (Value(g, "LicenseStatus") is "Unlicensed" or "NonGenuineGrace" or "Notification" || key == "WindowsActivation" && genuine.Value is ("GenuineInvalid" or "GenuineTampered")) card.ShowLicenseWarning();
        }
    }
}
