using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PCHealthPro.App.Services;
using PCHealthPro.App.ViewModels;
using PCHealthPro.Core;
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

    public MainWindow(OverviewViewModel model, LanguageService language)
    {
        Model = model;
        this.language = language;
        InitializeComponent();
        Title = typeof(App).Assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product;
        Nav.PaneTitle = Title;
        Localize();
        Model.PropertyChanged += (_, _) => DrawCards();
        Model.RefreshCommand.PropertyChanged += (_, _) => UpdateStatus();
        Nav.Loaded += StartOverview;
        Closed += (_, _) => Model.RefreshCommand.Cancel();
    }

    private async void StartOverview(object sender, RoutedEventArgs args)
    {
        Nav.Loaded -= StartOverview;
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
        QuickCheck.Content = language.Get("QuickCheck");
        AutomationProperties.SetName(QuickCheck, language.Get("QuickCheck"));
        SampleNotice.Message = language.Get("Sample");
        Tip.Title = language.Get("QuickCheck");
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
        OverviewPanel.Visibility = section == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = section == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        var phase = Sections.First(item => item.Key == section).Phase;
        Placeholder.Visibility = phase > 1 ? Visibility.Visible : Visibility.Collapsed;
        Placeholder.Message = string.Format(language.Get("Pending"), phase);
        Scroller.ChangeView(null, 0, null, true);
    }

    private void DismissTip(TeachingTip sender, TeachingTipClosedEventArgs args) => language.DismissHint();

    private void UpdateStatus()
    {
        bool busy = Model.RefreshCommand.IsRunning;
        Progress.IsActive = busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CheckStatus.Text = language.Get(busy ? "Busy" : "Completed");
    }

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
        var count = Math.Max(1, columns);
        for (int col = 0; col < count; col++) Cards.ColumnDefinitions.Add(new ColumnDefinition());
        for (int index = 0; index < Model.Readings.Count; index++)
        {
            if (index % count == 0) Cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var reading = Model.Readings[index];
            var name = language.Get(reading.Category);
            var value = reading.Value ?? language.Get("Unknown");
            var details = new Expander { Header = name, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8),
                IsExpanded = true };
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            var badge = new InfoBar { IsOpen = true, IsClosable = false, Message = language.Get(reading.Status.ToString()),
                Severity = reading.Status switch { HealthStatus.Good => InfoBarSeverity.Success,
                    HealthStatus.Attention => InfoBarSeverity.Warning, HealthStatus.Problem => InfoBarSeverity.Error,
                    _ => InfoBarSeverity.Informational } };
            content.Children.Add(badge);
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
