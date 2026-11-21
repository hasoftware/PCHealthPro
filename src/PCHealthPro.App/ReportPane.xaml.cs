using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using PCHealthPro.App.Services;
using PCHealthPro.App.ViewModels;
using PCHealthPro.Core.Diagnostics;
using PCHealthPro.Core.Reporting;
using Microsoft.Windows.Storage.Pickers;

namespace PCHealthPro.App;

public sealed partial class ReportPane : UserControl
{
    private sealed record Entry(string Name, InspectionReport? Report)
    {
        public override string ToString() => Name;
    }

    private readonly string historyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HASOFTWARE", "PCHealthPro", "history");
    private Window owner = null!;
    private LanguageService strings = null!;
    private OverviewViewModel overview = null!;
    private BenchmarkViewModel benchmark = null!;
    private ManualTestSession manual = null!;
    private InspectionReport? selected;
    private ReportNotes draft = new("", "", "");
    private bool busy, binding, active;
    private int revision;

    public ReportPane() => InitializeComponent();

    public void Initialize(Window window, LanguageService language, OverviewViewModel model, BenchmarkViewModel disk, ManualTestSession session)
    {
        (owner, strings, overview, benchmark, manual) = (window, language, model, disk, session);
        overview.PropertyChanged += (_, _) => { if (active) Render(); };
        overview.RefreshCommand.PropertyChanged += (_, _) => { if (active) Render(); };
        benchmark.PropertyChanged += (_, _) => { if (active && !benchmark.IsRunning) Render(); };
        owner.Closed += (_, _) => { active = false; revision++; };
    }

    private string T(string key) => strings.Get(key);
    private Visibility HasText(string text) => string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;

    private void OpenDetails(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (ForExport(selected ?? Current()) is { } report) DrawDetails(ReportLayout.Create(report, T));
    }

    private void DrawDetails(ReportLayout layout)
    {
        Details.Children.Clear();
        foreach (var group in layout.Sections)
        {
            var block = new Expander { Header = new TextBlock { Text = group.Title, TextWrapping = TextWrapping.Wrap },
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            block.Expanding += (sender, _) =>
            {
                if (sender.Content != null) return;
                var fields = new StackPanel();
                foreach (var line in group.Lines) fields.Children.Add(new ReadingRow(line.Label, line.Value));
                sender.Content = fields;
            };
            Details.Children.Add(block);
        }
    }
    public void Show(bool show)
    {
        active = show;
        Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        revision++;
        if (!show) return;
        SourceBox.Header = T("ReportSource"); CompareBox.Header = T("ReportCompare");
        Seller.Header = T("ReportSeller"); Price.Header = T("ReportPrice"); Notes.Header = T("ReportCondition");
        HideIds.Content = T("ReportHide"); ExportLabel.Text = T("ReportExport"); HistoryButton.Content = T("ReportSave");
        AutomationProperties.SetName(ExportButton, ExportLabel.Text);
        DetailBox.Header = T("Details");
        NotesBox.Header = T("ReportNotes");
        OutputTitle.Text = T("ReportOutput");
        AutomationProperties.SetName(Format, T("ReportFormat"));
        Render();
        _ = LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        int id = ++revision;
        try
        {
            var history = await Task.Run(() => ReportFiles.History(historyPath));
            if (!active || id != revision) return;
            var entries = history.Select(r => new Entry($"{r.CheckedAt:yyyy-MM-dd HH:mm:ss} · {r.Machine}", r)).ToArray();
            binding = true;
            SourceBox.ItemsSource = new[] { new Entry(T("ReportCurrent"), null) }.Concat(entries).ToArray();
            SourceBox.SelectedIndex = 0;
            CompareBox.ItemsSource = new[] { new Entry(T("ReportNoComparison"), null), new Entry(T("ReportCurrent"), null) }.Concat(entries).ToArray();
            CompareBox.SelectedIndex = 0;
            binding = false;
            SelectReport(null);
        }
        catch (Exception e) { if (active && id == revision) Status.Text = e.Message; }
    }

    private void SourceChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!binding && SourceBox.SelectedItem is Entry e) SelectReport(e.Report);
    }

    private void SelectReport(InspectionReport? report)
    {
        if (selected == null) draft = ReadNotes();
        selected = report;
        var notes = report?.Notes ?? draft;
        Seller.Text = notes.Seller; Price.Text = notes.Price; Notes.Text = notes.Condition;
        Seller.IsReadOnly = Price.IsReadOnly = Notes.IsReadOnly = report != null;
        if (report?.IdentifiersHidden == true) HideIds.IsChecked = true;
        Render();
    }

    private ReportNotes ReadNotes() => new(Seller.Text.Trim(), Price.Text.Trim(), Notes.Text.Trim());

    private void ResizeActions(object sender, SizeChangedEventArgs args)
    {
        bool narrow = args.NewSize.Width < 340;
        Grid.SetColumn(HistoryButton, narrow ? 0 : 2);
        Grid.SetColumnSpan(HistoryButton, narrow ? 3 : 1);
        Grid.SetRow(HistoryButton, narrow ? 1 : 0);
        HistoryButton.Margin = narrow ? new Thickness(0, 8, 0, 0) : new Thickness(0);
    }

    private void FinishEditingNotes(object sender, RoutedEventArgs args)
    {
        if (active && selected == null && (CompareBox.SelectedIndex > 0 || DetailBox.IsExpanded)) Render();
    }
    private InspectionReport? Current()
    {
        if (overview.CheckedAt == null || overview.RefreshCommand.IsRunning) return null;
        return new(overview.CheckedAt.Value, Environment.MachineName, selected == null ? ReadNotes() : draft,
            overview.Readings.ToArray(), manual.Snapshot().Where(t => strings.ManualEnabled(t.Test.ToString()) || t.Verdict != TestVerdict.Untested).ToArray(), benchmark.Status == "" ? null :
                new(benchmark.Volume, benchmark.SizeMiB, benchmark.Status, benchmark.Results.ToArray()));
    }

    private InspectionReport? ForExport(InspectionReport? r) => HideIds.IsChecked == true ? r?.HideIdentifiers() : r;
    private void PrivacyChanged(object sender, RoutedEventArgs args) => Render();
    private void ComparisonChanged(object sender, SelectionChangedEventArgs args) { if (!binding && active) Render(); }

    private void Render()
    {
        var report = ForExport(selected ?? Current());
        SourceBox.IsEnabled = Format.IsEnabled = CompareBox.IsEnabled = !busy;
        HideIds.IsEnabled = !busy && selected?.IdentifiersHidden != true;
        ExportButton.IsEnabled = !busy && report != null;
        HistoryButton.IsEnabled = ExportButton.IsEnabled && selected == null;
        if (report == null) { Summary.Text = T("ReportWait"); Details.Children.Clear(); Comparison.Text = ""; return; }
        var layout = ReportLayout.Create(report, T);
        Summary.Text = $"{layout.Machine} · {layout.Date}\n" +
            string.Format(T("ReportCoverage"), report.Hardware.Length,
                report.Tests.Count(test => test.Verdict != TestVerdict.Untested), report.Tests.Length);
        if (DetailBox.IsExpanded) DrawDetails(layout);
        Comparison.Text = "";
        if (CompareBox.SelectedIndex <= 0 || CompareBox.SelectedItem is not Entry entry) return;
        var other = ForExport(entry.Report ?? Current());
        if (other == null) return;
        // Compare the two localized snapshots by category, device and field, including
        // repeated modules (e.g. DIMMs). Preserve additions and removals.
        static Dictionary<string, string> Fields(ReportLayout source)
        {
            var result = new Dictionary<string, string>();
            foreach (var section in source.Sections)
            foreach (var line in section.Lines)
            {
                var key = $"{section.Title} / {line.Label}";
                var unique = key;
                for (int i = 2; result.ContainsKey(unique); i++) unique = $"{key} ({i})";
                result.Add(unique, line.Value);
            }
            return result;
        }
        var before = Fields(ReportLayout.Create(other, T));
        var after = Fields(layout);
        var changes = before.Keys.Union(after.Keys).Where(k => before.GetValueOrDefault(k) != after.GetValueOrDefault(k))
            .Select(k => $"{k}\n{before.GetValueOrDefault(k, "—")} → {after.GetValueOrDefault(k, "—")}").ToArray();
        Comparison.Text = changes.Length == 0 ? T("ReportEqual") : string.Join("\n\n", changes);
    }

    private async void Export(object sender, RoutedEventArgs args)
    {
        var report = ForExport(selected ?? Current());
        if (report == null || busy) return;
        busy = true; Render();
        try
        {
            var layout = ReportLayout.Create(report, T);
            string format = (string)Format.SelectedItem;
            string extension = "." + format.ToLowerInvariant();
            var picker = new FileSavePicker(owner.AppWindow.Id)
            {
                SuggestedFileName = $"PCHealthPro-{report.CheckedAt:yyyyMMdd-HHmmss}",
                DefaultFileExtension = extension
            };
            picker.FileTypeChoices.Add(format, new[] { extension });
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            await Task.Run(() => ReportFiles.Write(file.Path, report, layout));
            Status.Text = $"{T("ReportWritten")} · {file.Path}";
        }
        catch (Exception e) { Status.Text = e.Message; }
        finally { busy = false; if (active) Render(); }
    }

    private async void Save(object sender, RoutedEventArgs args)
    {
        var report = ForExport(Current());
        if (selected != null || report == null || busy) return;
        busy = true; Render();
        try
        {
            await Task.Run(() => ReportFiles.SaveHistory(historyPath, report));
            if (active) { await LoadHistoryAsync(); Status.Text = T("ReportSaved"); }
        }
        catch (Exception e) { Status.Text = e.Message; }
        finally { busy = false; if (active) Render(); }
    }
}
