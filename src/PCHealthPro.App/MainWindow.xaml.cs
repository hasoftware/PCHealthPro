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
        ("Reports", "\uE9F9", 6), ("Settings", "\uE713", 1)
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
        Nav.PaneTitle = Title;
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
            (item.Key == "Settings" ? Nav.FooterMenuItems : Nav.MenuItems).Add(entry);
            if (item.Key == section) Nav.SelectedItem = entry;
        }
        LanguageBox.ItemsSource = new[] { language.Get("SystemLanguage"), "Tiếng Việt", "English" };
        LanguageBox.SelectedIndex = language.Selection switch { "vi-VN" => 1, "en-US" => 2, _ => 0 };
        LanguageBox.Header = language.Get("Language");
        Privacy.Text = language.Get("Privacy");
        Updates.Content = language.Get("UpdateOptIn");
        Updates.IsChecked = language.CheckUpdates;
        Updates.IsEnabled = !elevated;
        ApplyUpdate.Content = language.Get("UpdateAction");
        if (elevated) UpdateState.Text = language.Get("UpdateStandard");
        Benchmark.Localize();
        QuickCheck.Content = language.Get("QuickCheck");
        AutomationProperties.SetName(QuickCheck, language.Get("QuickCheck"));
        Tip.Title = language.Get("QuickCheck");
        Elevate.Content = language.Get("RunAdmin");
        SmartNotice.Message = language.Get("SmartNotice");
        Tip.Subtitle = language.Get("Hint");
        updating = false;
        UpdateSection();
        UpdateStatus();
        DrawCards();
    }

    private void ChangeLanguage(object sender, SelectionChangedEventArgs args)
    {
        if (updating || LanguageBox.SelectedIndex < 0) return;
        language.Change(LanguageBox.SelectedIndex switch { 1 => "vi-VN", 2 => "en-US", _ => "system" });
        Localize();
    }

    private async void ToggleUpdates(object sender, RoutedEventArgs args)
    {
        try
        {
            language.SetUpdates(Updates.IsChecked == true);
            checkRevision++;
            update = null; UpdateNotice.IsOpen = false; UpdateState.Text = "";
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
            UpdateState.Text = language.Get(found == null ? "UpdateCurrent" : "UpdateAvailable");
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
        manual.Show(section);
        OverviewPanel.Visibility = section is "Settings" or "Reports" ? Visibility.Collapsed : Visibility.Visible;
        SettingsPanel.Visibility = section == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        Benchmark.Visibility = section == "Storage" ? Visibility.Visible : Visibility.Collapsed;
        if (section == "Storage") _ = Benchmark.LoadVolumesAsync();
        Report.Show(section == "Reports");
        UpdateStatus();
        DrawCards();
        Scroller.ChangeView(null, 0, null, true);
    }

    private void DismissTip(TeachingTip sender, TeachingTipClosedEventArgs args) => language.DismissHint();

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
        Progress.IsActive = busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CheckStatus.Text = language.Get(busy ? "Busy" : "Completed");
        var next = tests.Snapshot().FirstOrDefault(t => t.Verdict == TestVerdict.Untested);
        NextManual.Visibility = section == "Overview" && !busy && Model.CheckedAt != null && next != null
            ? Visibility.Visible : Visibility.Collapsed;
        if (next != null)
        {
            NextManual.Content = $"{language.Get("NextManual")} · {language.Get("Test" + next.Test)}";
            NextManual.Tag = next.Test switch { ManualTest.Microphone or ManualTest.Speakers => "Audio",
                ManualTest.Keyboard or ManualTest.Pointer => "Input", _ => next.Test.ToString() };
        }
    }

    private void OpenNextManual(object sender, RoutedEventArgs args) =>
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => Equals(i.Tag, NextManual.Tag));

    private void ResizeCards(object sender, SizeChangedEventArgs args)
    {
        int count = args.NewSize.Width >= 840 ? 3 : args.NewSize.Width >= 500 ? 2 : 1;
        if (columns == count) return;
        columns = count;
        DrawCards();
    }

    private void DrawCards()
    {
        Cards.Children.Clear();
        Cards.ColumnDefinitions.Clear();
        Cards.RowDefinitions.Clear();
        SmartNotice.IsOpen = !elevated && section == "Storage" && Model.Readings.Any(r => r.LimitedSmart);
        var count = section is "Storage" or "Battery" ? 1 : Math.Max(1, columns);
        for (int col = 0; col < count; col++) Cards.ColumnDefinitions.Add(new ColumnDefinition());
        var readings = Model.Readings.Where(reading => section switch
        {
            "Overview" => reading.Category is "Machine" or "CPU" or "RAM" or "GPU" or "Windows" or "Storage" or "Battery",
            "Compute" => reading.Category is "CPU" or "RAM" or "Machine",
            "Display" => reading.Category is "GPU" or "Display",
            _ => reading.Category == section
        }).ToArray();
        for (int index = 0; index < readings.Length; index++)
        {
            if (index % count == 0) Cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var reading = readings[index];
            var name = language.Get(reading.Category);
            var value = reading.Value ?? language.Get(reading.Status == HealthStatus.Absent ? "Absent" : "Unknown");
            var details = new Expander { Header = name, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8),
                IsExpanded = true };
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            var badge = new InfoBar { IsOpen = true, IsClosable = false, Message = language.Get(reading.Status.ToString()),
                Severity = reading.Status switch { HealthStatus.Good => InfoBarSeverity.Success,
                    HealthStatus.Attention => InfoBarSeverity.Warning, HealthStatus.Problem => InfoBarSeverity.Error,
                    _ => InfoBarSeverity.Informational } };
            if (reading.Status != HealthStatus.Absent) content.Children.Add(badge);
            foreach (var group in reading.Groups)
            {
                var fields = new StackPanel { Spacing = 6 };
                foreach (var field in group.Fields)
                {
                    var formatted = field.Value == null ? language.Get("Unknown") :
                        field.Localize ? language.Get(field.Value) : field.Value;
                    fields.Children.Add(new TextBlock { Text = $"{language.Get(field.Label)} · {formatted}",
                        TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
                    if (field.Label == "LicenseStatus" && field.Value is "Unlicensed" or "NonGenuineGrace" or "Notification")
                        fields.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning,
                            Message = language.Get("LicenseWarning") });
                }
                content.Children.Add(new Expander { Header = group.DeviceName ?? language.Get(group.Label), Content = fields,
                    IsExpanded = section is "Storage" or "Battery",
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
