using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using PCHealthPro.App.Services;
using PCHealthPro.App.ViewModels;
using PCHealthPro.Core.Benchmark;
using Windows.Foundation;
using Windows.Storage.Pickers;

namespace PCHealthPro.App;

public sealed partial class BenchmarkPane : UserControl
{
    private BenchmarkViewModel? model;
    private LanguageService language = null!;
    private Window owner = null!;
    private string? directory;
    private string? lastTarget;
    private bool choosing;
    private bool closed;
    private bool initializedDrives;
    private static readonly string[] Sizes = ["256 MiB", "1 GiB", "4 GiB"];

    public BenchmarkPane() => InitializeComponent();

    public void Initialize(BenchmarkViewModel benchmark, LanguageService strings, Window window)
    {
        model = benchmark;
        language = strings;
        owner = window;
        SizeBox.ItemsSource = Sizes;
        SizeBox.SelectedIndex = 0;
        model.PropertyChanged += (_, _) => { if (!closed) Render(); };
        owner.Closed += (_, _) => { closed = true; model.Cancel(); };
    }

    public async Task LoadVolumesAsync()
    {
        if (initializedDrives) return;
        initializedDrives = true;
        string[] roots;
        try
        {
            roots = await Task.Run(() => DriveInfo.GetDrives()
                .Where(d => (d.DriveType is DriveType.Fixed or DriveType.Removable) && d.IsReady)
                .Select(d => d.Name).ToArray());
        }
        catch (IOException) { initializedDrives = false; roots = []; }
        catch (UnauthorizedAccessException) { initializedDrives = false; roots = []; }
        if (closed) return;
        Volume.ItemsSource = roots;
        var preferred = Path.GetPathRoot(Path.GetTempPath());
        Volume.SelectedItem = roots.FirstOrDefault(s => string.Equals(s, preferred, StringComparison.OrdinalIgnoreCase)) ?? roots.FirstOrDefault();
        Render();
    }

    public void Localize()
    {
        Title.Text = language.Get("BenchTitle");
        Volume.Header = language.Get("BenchVolume");
        SizeBox.Header = language.Get("BenchSize");
        StartButton.Content = language.Get("BenchStart");
        CancelButton.Content = language.Get("BenchCancel");
        FolderButton.Content = language.Get("BenchFolder");
        AutomationProperties.SetName(Plot, language.Get("BenchChart"));
        Render();
    }

    private void VolumeChanged(object sender, SelectionChangedEventArgs e)
    {
        string? root = Volume.SelectedItem as string;
        var temp = Path.GetTempPath();
        directory = string.Equals(root, Path.GetPathRoot(temp), StringComparison.OrdinalIgnoreCase) ? temp : root;
        if (model != null) Render();
    }

    private async void ChooseFolder(object sender, RoutedEventArgs e)
    {
        if (model == null || model.IsRunning || choosing) return;
        choosing = true;
        Render();
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
            var folder = await picker.PickSingleFolderAsync();
            if (closed || folder == null) return;
            string? root = Volume.Items.OfType<string>().FirstOrDefault(v =>
                string.Equals(v, Path.GetPathRoot(folder.Path), StringComparison.OrdinalIgnoreCase));
            if (root == null) { await NotifyAsync(language.Get("BenchLocalOnly")); return; }
            Volume.SelectedItem = root;
            directory = folder.Path;
        }
        catch (Exception ex) { if (!closed) await NotifyAsync(ex.Message); }
        finally { choosing = false; if (!closed) Render(); }
    }

    private Task<ContentDialogResult> NotifyAsync(string message) => new ContentDialog
    {
        XamlRoot = XamlRoot, Title = language.Get("BenchTitle"), Content = message,
        CloseButtonText = language.Get("BenchClose")
    }.ShowAsync().AsTask();

    private async void Start(object sender, RoutedEventArgs e)
    {
        if (model == null || model.IsRunning || choosing || directory == null || SizeBox.SelectedIndex < 0) return;
        var target = directory;
        var index = SizeBox.SelectedIndex;
        choosing = true;
        Render();
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = language.Get("BenchTitle"),
                Content = string.Format(language.Get("BenchConsent"), target, Sizes[index]),
                PrimaryButtonText = language.Get("BenchStart"), CloseButtonText = language.Get("BenchCancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed) return;
            lastTarget = $"{target} · {Sizes[index]}";
            await model.RunAsync(target, DiskBenchmark.SizesMiB[index]);
        }
        finally { choosing = false; if (!closed) Render(); }
    }

    private void Cancel(object sender, RoutedEventArgs e) => model?.Cancel();

    private void Render()
    {
        if (model == null) return;
        bool idle = !model.IsRunning && !choosing;
        DirectoryText.Text = directory ?? language.Get("BenchNoVolume");
        Volume.IsEnabled = SizeBox.IsEnabled = FolderButton.IsEnabled = idle;
        StartButton.IsEnabled = idle && directory != null;
        CancelButton.IsEnabled = model.IsRunning;
        StatusText.Text = model.Status.Length == 0 ? "" : $"{language.Get(model.Status)} · {lastTarget}";
        if (model.ErrorDetail != null) StatusText.Text += Environment.NewLine + model.ErrorDetail;
        StatusText.Visibility = model.Status.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var latest = model.Latest;
        Meter.Visibility = model.IsRunning || latest != null ? Visibility.Visible : Visibility.Collapsed;
        Meter.Value = 100 * (latest?.Progress ?? 0);
        Live.Visibility = Plot.Visibility = ChartScale.Visibility = latest == null ? Visibility.Collapsed : Visibility.Visible;
        Live.Text = latest == null ? "" : $"{language.Get(latest.Phase.ToString())} · {Rate(latest)}";
        ResultsText.Visibility = model.Results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ResultsText.Text = string.Join(Environment.NewLine, model.Results.Select(r => $"{language.Get(r.Phase.ToString())}   {Rate(r)}"));
        DrawChart();
    }

    private static string Rate(BenchmarkSample s) => $"{s.MegabytesPerSecond:N2} MB/s   ·   {s.Iops:N0} IOPS";

    private void PlotResized(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        if (model == null || model.Series.Count == 0 || Plot.ActualWidth < 4) return;
        var values = model.Series;
        var peak = Math.Max(1, values.Max());
        ChartScale.Text = $"{language.Get("BenchChart")} · 0 – {peak:N2} MB/s";
        var points = new PointCollection();
        for (int i = 0; i < values.Count; i++)
            points.Add(new Point(2 + i * (Plot.ActualWidth - 4) / Math.Max(1, values.Count - 1),
                Plot.Height - 2 - values[i] / peak * (Plot.Height - 4)));
        Trace.Points = points;
    }
}
