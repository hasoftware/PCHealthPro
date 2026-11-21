using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    public void Show(bool show)
    {
        active = show;
        Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        revision++;
        if (!show) return;
        SourceBox.Header = T("ReportSource"); CompareBox.Header = T("ReportCompare");
        Seller.Header = T("ReportSeller"); Price.Header = T("ReportPrice"); Notes.Header = T("ReportCondition");
        HideIds.Content = T("ReportHide"); ExportButton.Content = T("ReportExport"); HistoryButton.Content = T("ReportSave");
        DetailBox.Header = T("Details");
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
    private InspectionReport? Current()
    {
        if (overview.CheckedAt == null || overview.Readings.Count == 0 || overview.RefreshCommand.IsRunning) return null;
        return new(overview.CheckedAt.Value, Environment.MachineName, selected == null ? ReadNotes() : draft,
            overview.Readings.ToArray(), manual.Snapshot(), benchmark.Status == "" ? null :
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
        if (report == null) { Summary.Text = T("ReportWait"); Details.Text = Comparison.Text = ""; return; }
        var layout = ReportLayout.Create(report, T);
        Summary.Text = $"{layout.Machine} · {layout.Date}\n" + string.Join("\n", report.Tests.Select(test => $"{T("Test" + test.Test)} · {T("Verdict" + test.Verdict)}"));
        Details.Text = string.Join("\n\n", layout.Sections.Select(s => s.Title + "\n" + string.Join("\n", s.Lines.Select(l => $"{l.Label} · {l.Value}"))));
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
