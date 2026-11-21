using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using PCHealthPro.Core;
using PCHealthPro.Core.Diagnostics;
using PCHealthPro.Core.Benchmark;
using PCHealthPro.Core.Inventory;
using PCHealthPro.App.Services;
using PCHealthPro.App.ViewModels;

namespace PCHealthPro.App;

public partial class App : Application
{
    private Window? window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var services = new ServiceCollection();
        var source = new WindowsInventorySource();
        foreach (var module in InventoryCatalog.Create(source, new DriveHourHistory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HASOFTWARE", "PCHealthPro", "drive-hours"))))
            services.AddSingleton(module);
        services.AddSingleton<HardwareScan>();
        services.AddSingleton<LanguageService>();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<ManualTestSession>();
        services.AddSingleton<DiskBenchmark>();
        services.AddSingleton<BenchmarkViewModel>();
        services.AddSingleton<MainWindow>();
        window = services.BuildServiceProvider().GetRequiredService<MainWindow>();
        window.Activate();
    }
}
