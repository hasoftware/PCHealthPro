using System.Reflection;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PCHealthPro.App.Services;
using PCHealthPro.App.ViewModels;
using PCHealthPro.Core;
using PCHealthPro.Core.Diagnostics;
using PCHealthPro.App.Diagnostics;
using Windows.ApplicationModel.DataTransfer;

namespace PCHealthPro.App;

public sealed partial class MainWindow : Window
{
    private static readonly (string Key, string Icon, int Phase)[] Sections =
    [
        ("Overview", "\uE80F", 1), ("Compute", "\uE950", 2), ("Storage", "\uEDA2", 3),
        ("Battery", "\uE83F", 3), ("Network", "\uE701", 2), ("Display", "\uE7F4", 5),
        ("Audio", "\uE767", 5), ("Camera", "\uE722", 5), ("Input", "\uE765", 5),
        ("Windows", "\uE770", 2), ("Reports", "\uE9F9", 6), ("Settings", "\uE713", 1)
    ];

    public OverviewViewModel Model { get; }
    private readonly LanguageService language;
    private string section = "Overview";
    private bool updating;
    private int columns;
    private readonly bool elevated;
    private readonly CancellationTokenSource lifetime = new();
    private UpdatePackage? update;
    private int checkRevision;
    private string? updateStateKey;

    private readonly InteractivePane manual;
    private readonly ManualTestSession tests;
    public MainWindow(OverviewViewModel model, LanguageService language, BenchmarkViewModel benchmark, ManualTestSession session)
    {
        Model = model;
        tests = session;
        this.language = language;
        using var identity = WindowsIdentity.GetCurrent();
        elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        InitializeComponent();
        manual = new InteractivePane(ManualPane, language, session, this);
        Benchmark.Initialize(benchmark, language, this);
        Report.Initialize(this, language, model, benchmark, session);
        Title = typeof(App).Assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product;
        AppTitle.Text = Title;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Localize();
        Model.PropertyChanged += (_, _) => DrawCards();
        Model.RefreshCommand.PropertyChanged += (_, _) => UpdateStatus();
        Nav.Loaded += StartOverview;
        Closed += (_, _) => { lifetime.Cancel(); Model.RefreshCommand.Cancel(); manual.Dispose(); };
    }

    private async void StartOverview(object sender, RoutedEventArgs args)
    {
        Nav.Loaded -= StartOverview;
        if (!elevated && language.CheckUpdates) _ = CheckUpdatesAsync();
        try { await Model.RefreshCommand.ExecuteAsync(null); }
        catch (OperationCanceledException) { return; }
        Tip.IsOpen = language.ShowHint && section == "Overview";
    }

    private void Localize()
    {
        updating = true;
        Nav.MenuItems.Clear();
        Nav.FooterMenuItems.Clear();
        foreach (var item in Sections)
        {
            var entry = new NavigationViewItem
            {
                Tag = item.Key, Content = language.Get(item.Key),
                Icon = new FontIcon { Glyph = item.Icon }
            };
            (item.Key is "Settings" or "Reports" ? Nav.FooterMenuItems : Nav.MenuItems).Add(entry);
            if (item.Key == section) Nav.SelectedItem = entry;
        }
        LanguageBox.ItemsSource = new[] { language.Get("SystemLanguage"), "Tiếng Việt", "English" };
        LanguageBox.SelectedIndex = language.Selection switch { "vi-VN" => 1, "en-US" => 2, _ => 0 };
        LanguageTitle.Text = language.Get("Language");
        AutomationProperties.SetName(LanguageBox, LanguageTitle.Text);
        UpdateTitle.Text = language.Get("UpdatesTitle");
        PrivacyTitle.Text = language.Get("PrivacyTitle");
        AppVersion.Text = $"{Title} · v{typeof(App).Assembly.GetName().Version!.ToString(3)}";
        Privacy.Text = language.Get("Privacy");
        UpdateLabel.Text = language.Get("UpdateOptIn");
        AutomationProperties.SetName(Updates, UpdateLabel.Text);
        Updates.IsChecked = language.CheckUpdates;
        Updates.IsEnabled = !elevated;
        ApplyUpdate.Content = language.Get("UpdateAction");
        if (elevated) UpdateState.Text = language.Get("UpdateStandard");
        else if (updateStateKey != null) UpdateState.Text = language.Get(updateStateKey);
        if (update != null) UpdateNotice.Title = $"{language.Get("UpdateAvailable")} · {update.Tag}";
        Benchmark.Localize();
        DiskHealthTab.Text = language.Get("DiskHealthTab");
        DiskBenchTab.Text = language.Get("BenchTitle");
        HardwareTitle.Text = language.Get("HardwareSummary");
        QuickCheckLabel.Text = language.Get("QuickCheck");
        AutomationProperties.SetName(QuickCheck, language.Get("QuickCheck"));
        Tip.Title = language.Get("QuickCheck");
        Elevate.Content = language.Get("RunAdmin");
        SmartNotice.Message = language.Get("SmartNotice");
        Tip.Subtitle = language.Get("Hint");
        updating = false;
        UpdateSection();
    }

    private void ResizeScanPanel(object sender, SizeChangedEventArgs args)
    {
        bool narrow = args.NewSize.Width < 500;
        Grid.SetColumn(QuickCheck, narrow ? 1 : 2);
        Grid.SetRow(QuickCheck, narrow ? 2 : 0);
        Grid.SetRowSpan(QuickCheck, narrow ? 1 : 2);
        QuickCheck.Margin = narrow ? new Thickness(0, 8, 0, 0) : new Thickness(0);
    }

    private void ChangeLanguage(object sender, SelectionChangedEventArgs args)
    {
        if (updating || LanguageBox.SelectedIndex < 0) return;
        language.Change(LanguageBox.SelectedIndex switch { 1 => "vi-VN", 2 => "en-US", _ => "system" });
        Localize();
    }

    private Visibility TextVisibility(string? value) => string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    private void ResizeLanguage(object sender, SizeChangedEventArgs args)
    {
        bool narrow = args.NewSize.Width < 360;
        Grid.SetColumn(LanguageBox, narrow ? 1 : 2);
        Grid.SetRow(LanguageBox, narrow ? 1 : 0);
        LanguageRow.RowSpacing = narrow ? 8 : 0;
    }

    private async void ToggleUpdates(object sender, RoutedEventArgs args)
    {
        try
        {
            language.SetUpdates(Updates.IsChecked == true);
            checkRevision++;
            update = null; updateStateKey = null; UpdateNotice.IsOpen = false; UpdateState.Text = "";
            if (Updates.IsChecked == true) await CheckUpdatesAsync();
        }
        catch (Exception e) { UpdateState.Text = e.Message; }
    }

    private async Task CheckUpdatesAsync()
    {
        int revision = ++checkRevision;
        try
        {
            var found = await ReleaseUpdates.CheckAsync(lifetime.Token);
            if (checkRevision != revision || lifetime.IsCancellationRequested) return;
            update = found;
            updateStateKey = found == null ? "UpdateCurrent" : "UpdateAvailable";
            UpdateState.Text = language.Get(updateStateKey);
            if (found == null) return;
            UpdateNotice.Title = $"{language.Get("UpdateAvailable")} · {found.Tag}";
            UpdateNotice.IsOpen = true;
        }
        catch (Exception e) { if (revision == checkRevision && !lifetime.IsCancellationRequested) UpdateState.Text = e.Message; }
    }

    private async void InstallUpdate(object sender, RoutedEventArgs args)
    {
        if (update is not { } package || elevated) return;
        var dialog = new ContentDialog { XamlRoot = Body.XamlRoot, Title = package.Tag,
            Content = language.Get(package.Installed ? "UpdateMsi" : "UpdateZip"),
            PrimaryButtonText = language.Get("UpdateAction"), CloseButtonText = language.Get("BenchCancel"),
            DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        ApplyUpdate.IsEnabled = Updates.IsEnabled = false;
        try { await ReleaseUpdates.ApplyAsync(package, lifetime.Token); Close(); }
        catch (Exception e) { if (!lifetime.IsCancellationRequested) UpdateNotice.Message = e.Message; }
        finally { if (!lifetime.IsCancellationRequested) ApplyUpdate.IsEnabled = Updates.IsEnabled = true; }
    }

    private void Navigate(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (updating || args.SelectedItem is not NavigationViewItem { Tag: string key }) return;
        section = key;
        Tip.IsOpen = false;
        UpdateSection();
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(140) };
        Storyboard.SetTarget(fade, Body);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var transition = new Storyboard();
        transition.Children.Add(fade);
        transition.Begin();
    }

    private void UpdateSection()
    {
        Heading.Text = language.Get(section);
        ScanPanel.Visibility = HardwareTitle.Visibility = section == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        manual.Show(section);
        SystemInventory.Visibility = section is "Compute" or "Network" or "Windows" ? Visibility.Visible : Visibility.Collapsed;
        OverviewPanel.Visibility = SystemInventory.Visibility == Visibility.Visible || section is "Settings" or "Reports" ? Visibility.Collapsed : Visibility.Visible;
        SettingsPanel.Visibility = section == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        DiskTabs.Visibility = section == "Storage" ? Visibility.Visible : Visibility.Collapsed;
        if (section == "Storage") _ = Benchmark.LoadVolumesAsync();
        Report.Show(section == "Reports");
        UpdateStatus();
        DrawCards();
        Scroller.ChangeView(null, 0, null, true);
    }

    private void DismissTip(TeachingTip sender, TeachingTipClosedEventArgs args) => language.DismissHint();

    private void DiskTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (Cards != null && section == "Storage") DrawCards();
    }

    private void RestartElevated(object sender, RoutedEventArgs args)
    {
        if (Environment.ProcessPath is not string exe) return;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            if (process != null) Close();
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223) { }
        catch (Win32Exception) { SmartNotice.Message = language.Get("ElevationFailed"); }
    }

    private void UpdateStatus()
    {
        bool busy = Model.RefreshCommand.IsRunning;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        var machine = Model.Readings.FirstOrDefault(r => r.Category == "Machine");
        var model = machine?.Value;
        MachineName.Text = string.IsNullOrWhiteSpace(model) || model.Equals("System Product Name", StringComparison.OrdinalIgnoreCase)
            ? Environment.MachineName
            : model;
        CheckStatus.Text = busy || Model.CheckedAt == null ? language.Get("Busy")
            : string.Format(language.Get("ScannedAt"), Model.CheckedAt.Value.ToLocalTime().ToString("HH:mm"));
        var results = tests.Snapshot();
        var next = results.FirstOrDefault(t => t.Verdict == TestVerdict.Untested);
        ManualProgress.Visibility = section == "Overview" && !busy && Model.CheckedAt != null
            ? Visibility.Visible : Visibility.Collapsed;
        var recorded = results.Count(r => r.Verdict != TestVerdict.Untested);
        ManualCount.Text = string.Format(language.Get("ManualCount"), recorded, results.Length);
        ManualMeter.Maximum = results.Length;
        ManualMeter.Value = recorded;
        AutomationProperties.SetName(ManualMeter, ManualCount.Text);
        if (next == null)
        {
            NextManual.Content = language.Get("Reports");
            NextManual.Tag = "Reports";
        }
        else
        {
            NextManual.Content = $"{language.Get("NextManual")} · {language.Get("Test" + next.Test)}";
            NextManual.Tag = next.Test switch { ManualTest.Microphone or ManualTest.Speakers => "Audio",
                ManualTest.Keyboard or ManualTest.Pointer => "Input", _ => next.Test.ToString() };
        }
    }

    private void OpenNextManual(object sender, RoutedEventArgs args)
    {
        var next = tests.Snapshot().FirstOrDefault(t => t.Verdict == TestVerdict.Untested);
        Nav.SelectedItem = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => Equals(i.Tag, NextManual.Tag));
        if (next != null && ManualPane.Children.OfType<ManualTestCard>().FirstOrDefault(c => Equals(c.Tag, next.Test)) is { } card)
            card.DispatcherQueue.TryEnqueue(() => card.StartBringIntoView());
    }

    private void ResizeCards(object sender, SizeChangedEventArgs args)
    {
        int count = args.NewSize.Width >= 640 ? 3 : args.NewSize.Width >= 400 ? 2 : 1;
        if (columns == count) return;
        columns = count;
        if (section == "Overview") DrawCards();
    }

    private void OpenHardware(string category)
    {
        var target = category switch { "CPU" or "RAM" => "Compute", "GPU" => "Display", _ => category };
        if (target == "Storage") DiskTabs.SelectedItem = DiskHealthTab;
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => Equals(i.Tag, target));
        if (target is "Compute" or "Windows") { SystemInventory.OpenHardware(category); return; }
        if (Cards.Children.OfType<Expander>().FirstOrDefault(e => Equals(e.Tag, category)) is { } details)
        {
            details.IsExpanded = true;
            details.Focus(FocusState.Programmatic);
            details.StartBringIntoView();
        }
    }

    private void DrawCards()
    {
        if (section is "Compute" or "Network" or "Windows")
        {
            SystemInventory.Show(section, language, Model.Readings);
            return;
        }
        Cards.Children.Clear();
        Cards.ColumnDefinitions.Clear();
        Cards.RowDefinitions.Clear();
        bool showBenchmark = section == "Storage" && DiskTabs.SelectedItem == DiskBenchTab;
        SmartNotice.IsOpen = !elevated && !showBenchmark && section == "Storage" && Model.Readings.Any(r => r.LimitedSmart);
        Benchmark.Visibility = showBenchmark ? Visibility.Visible : Visibility.Collapsed;
        Cards.Visibility = showBenchmark ? Visibility.Collapsed : Visibility.Visible;
        if (showBenchmark) return;
        if (section is "Storage" or "Battery")
        {
            var reading = Model.Readings.FirstOrDefault(r => r.Category == section);
            var devices = reading?.Groups;
            for (int i = 0; i < Math.Max(1, devices?.Count ?? 0); i++)
            {
                var device = devices?.ElementAtOrDefault(i);
                var fallback = device != null ? $"{language.Get(section)} {i + 1}" : language.Get(reading == null ? "Busy" :
                    reading.Status == HealthStatus.Absent ? "Absent" : "Unknown");
                var card = new DeviceCard();
                card.Show(section, device, fallback, language);
                Cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(card, i);
                Cards.Children.Add(card);
            }
            return;
        }
        var count = section == "Overview" ? Math.Max(1, columns) : 1;
        for (int col = 0; col < count; col++) Cards.ColumnDefinitions.Add(new ColumnDefinition());
        var readings = Model.Readings.Where(reading => section switch
        {
            "Overview" => reading.Category is "CPU" or "RAM" or "GPU" or "Windows" or "Storage" or "Battery",
            "Display" => reading.Category is "GPU" or "Display",
            _ => reading.Category == section
        }).ToArray();
        for (int index = 0; index < readings.Length; index++)
        {
            if (index % count == 0) Cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var reading = readings[index];
            var name = language.Get(reading.Category);
            var value = reading.Value ?? language.Get(reading.Status == HealthStatus.Absent ? "Absent" : "Unknown");
            if (section == "Overview")
            {
                var glyph = reading.Category switch { "CPU" => "\uE950", "RAM" => "\uE964", "GPU" => "\uE7F4",
                    "Storage" => "\uEDA2", "Battery" => "\uE83F", _ => "\uE770" };
                string? Field(string key) => reading.Groups.SelectMany(g => g.Fields).FirstOrDefault(f => f.Label == key && f.Value != null)?.Value;
                var caption = reading.Category switch
                {
                    "CPU" when Field("NumberOfCores") is { } cores && Field("NumberOfLogicalProcessors") is { } threads =>
                        string.Format(language.Get("CpuTopology"), cores, threads),
                    "RAM" when Field("ConfiguredClockSpeed") is { } speed => speed,
                    "GPU" when Field("CurrentHorizontalResolution") is { } width && Field("CurrentVerticalResolution") is { } height => $"{width} × {height}",
                    "Windows" when Field("BuildNumber") is { } build => $"{language.Get("BuildNumber")} {build}",
                    _ => null
                };
                var tile = new HardwareTile();
                tile.Bind(name, glyph, value, reading.Status, caption ?? (value == language.Get(reading.Status.ToString()) ? "" : language.Get(reading.Status.ToString())),
                    () => OpenHardware(reading.Category));
                Grid.SetColumn(tile, index % count);
                Grid.SetRow(tile, index / count);
                Cards.Children.Add(tile);
                continue;
            }
            bool interactive = section is "Audio" or "Input" or "Display" or "Camera";
            var details = new Expander { Header = interactive ? $"{language.Get("Details")} · {name}" : name, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8),
                Tag = reading.Category, VerticalAlignment = VerticalAlignment.Top, IsExpanded = !interactive };
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            var badge = new InfoBar { IsOpen = true, IsClosable = false, Message = language.Get(reading.Status.ToString()),
                Severity = reading.Status switch { HealthStatus.Good => InfoBarSeverity.Success,
                    HealthStatus.Attention => InfoBarSeverity.Warning, HealthStatus.Problem => InfoBarSeverity.Error,
                    _ => InfoBarSeverity.Informational } };
            if (reading.Status is HealthStatus.Good or HealthStatus.Attention or HealthStatus.Problem) content.Children.Add(badge);
            foreach (var group in reading.Groups)
            {
                var fields = new StackPanel { Spacing = 6 };
                foreach (var field in group.Fields)
                {
                    var formatted = field.Value == null ? language.Get("Unknown") :
                        field.Localize ? language.Get(field.Value) : field.Value;
                    fields.Children.Add(new ReadingRow(language.Get(field.Label), formatted));
                    if (field.Label == "LicenseStatus" && field.Value is "Unlicensed" or "NonGenuineGrace" or "Notification")
                        fields.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning,
                            Message = language.Get("LicenseWarning") });
                }
                content.Children.Add(new Expander { Header = group.DeviceName ?? language.Get(group.Label), Content = fields,
                    IsExpanded = reading.Groups.Count == 1,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            }
            details.Content = content;
            var copy = new MenuFlyoutItem { Text = language.Get("Copy") };
            copy.Click += (_, _) => { var data = new DataPackage(); data.SetText(value); Clipboard.SetContent(data); };
            var menu = new MenuFlyout();
            menu.Items.Add(copy);
            details.ContextFlyout = menu;
            Grid.SetColumn(details, index % count);
            Grid.SetRow(details, index / count);
            Cards.Children.Add(details);
        }
    }
}
