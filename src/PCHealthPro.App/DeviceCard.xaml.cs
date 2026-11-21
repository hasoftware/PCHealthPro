using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PCHealthPro.App.Services;
using PCHealthPro.Core;
using PCHealthPro.Core.Inventory;

namespace PCHealthPro.App;

public sealed partial class DeviceCard : UserControl
{
    private IReadOnlyList<DeviceGroup> groups = [];
    private LanguageService language = null!;

    public DeviceCard() => InitializeComponent();
    internal void SetBenchSummary(string message) { BenchSummary.Text = message; BenchSummary.Visibility = Visibility.Visible; }
    internal void SetBrand(string? brand) => BrandMark.Apply(BrandHost, brand);

    internal void Identify(string category)
    {
        Tag = category;
        AutomationProperties.SetAutomationId(Details, "Details" + category);
    }

    internal void FocusSummary()
    {
        Title.Focus(FocusState.Programmatic);
        StartBringIntoView();
    }

    internal void Show(string category, DeviceGroup? device, string fallback, LanguageService strings)
    {
        bool disk = category == "Storage";
        InventoryField? Field(string key) => device?.Fields.FirstOrDefault(f => f.Label == key);
        string? ShowValue(InventoryField? f) => f?.Localize == true && f.Value != null ? strings.Get(f.Value) : f?.Value;
        var description = disk ? new[] { "DriveMedia", "DriveBus" } : ["Manufacturer", "ChargeState"];
        var subtitle = string.Join(" · ", description.Select(k => ShowValue(Field(k))).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
        string[] keys = disk
            ? Field("DriveMedia")?.Value == "HDD" && Field("LifeRemaining")?.Value == null
                ? ["Size", "Temperature", "PowerOnHours", "HoursTrend"]
                : ["Size", "Temperature", "PowerOnHours", "LifeRemaining", "HoursTrend"]
            : [Field("RelativeDesign") != null ? "RelativeDesign" : "DesignCapacity",
                Field("RelativeFull") != null ? "RelativeFull" : "FullCapacity", "BatteryWear",
                "EstimatedChargeRemaining", "Temperature", "CycleCount", "ManufactureDate", "BatteryAgeDays", "TimeOnBattery"];
        Bind(strings, disk ? "\uEDA2" : "\uE83F", string.IsNullOrWhiteSpace(device?.DeviceName) ? fallback : device.DeviceName,
            subtitle, device == null ? [] : keys.Select(k => Field(k) ?? new(k, null)), device == null ? [] : [device],
            strings.Get(disk ? "DiskDetails" : "BatteryDetails"));
        if (disk && Field("DriveHealth")?.Value is { } state)
        {
            Health.Severity = state switch { nameof(HealthStatus.Good) => InfoBarSeverity.Success, nameof(HealthStatus.Attention) => InfoBarSeverity.Warning,
                nameof(HealthStatus.Problem) => InfoBarSeverity.Error, _ => InfoBarSeverity.Informational };
            Health.Title = strings.Get(state);
            Health.Message = ShowValue(Field("HealthReason")) ?? "";
            Health.IsOpen = true;
            Health.Visibility = Visibility.Visible;
        }
    }

    internal void Bind(LanguageService strings, string glyph, string title, string? subtitle, IEnumerable<InventoryField> metrics,
        IReadOnlyList<DeviceGroup> details, string detailsLabel)
    {
        language = strings;
        groups = details;
        Icon.Glyph = glyph;
        Title.Text = title;
        Subtitle.Text = subtitle ?? "";
        Subtitle.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;
        foreach (var field in metrics)
        {
            var metric = new StackPanel { Spacing = 4 };
            metric.Children.Add(new TextBlock { Text = strings.Get(field.Label), Style = (Style)Application.Current.Resources["MetricLabel"], TextWrapping = TextWrapping.Wrap });
            metric.Children.Add(new TextBlock { Text = Format(field), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            Grid.SetColumn(metric, Metrics.Children.Count);
            Metrics.Children.Add(metric);
        }
        Metrics.Visibility = Metrics.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Details.Header = detailsLabel;
        Details.Visibility = groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void ShowLicenseWarning()
    {
        Health.Title = language.Get("Attention");
        Health.Severity = InfoBarSeverity.Warning;
        Health.Message = language.Get("LicenseWarning");
        Health.IsOpen = true;
        Health.Visibility = Visibility.Visible;
    }

    private string Format(InventoryField field) => field.Value == null ? language.Get("Unknown")
        : field.Localize ? language.Get(field.Value) : field.Value;

    private void ResizeMetrics(object sender, SizeChangedEventArgs args)
    {
        int count = Math.Clamp(Metrics.Children.Count, 1, args.NewSize.Width >= 360 ? 3 : args.NewSize.Width >= 230 ? 2 : 1);
        int rows = (Metrics.Children.Count + count - 1) / count;
        if (Metrics.ColumnDefinitions.Count == count && Metrics.RowDefinitions.Count == rows) return;
        Metrics.ColumnDefinitions.Clear();
        Metrics.RowDefinitions.Clear();
        for (int i = 0; i < count; i++) Metrics.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < Metrics.Children.Count; i++)
        {
            if (i % count == 0) Metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var metric = (FrameworkElement)Metrics.Children[i];
            Grid.SetColumn(metric, i % count);
            Grid.SetRow(metric, i / count);
        }
    }

    private void OpenFields(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (sender.Content != null) return;
        var rows = new StackPanel { Spacing = 4 };
        foreach (var group in groups)
        {
            if (groups.Count > 1)
            {
                var identifier = group.DeviceName ?? group.Fields.FirstOrDefault(f => f.Label is "DeviceLocator" or "InterfaceDescription" or "Name" && f.Value != null)?.Value;
                var label = language.Get(group.Label);
                rows.Children.Add(new TextBlock { Text = identifier == null ? label : $"{label} · {identifier}", TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, rows.Children.Count == 0 ? 0 : 12, 0, 4), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
            }
            foreach (var field in group.Fields) rows.Children.Add(new ReadingRow(language.Get(field.Label), Format(field)));
        }
        sender.Content = rows;
    }
}
