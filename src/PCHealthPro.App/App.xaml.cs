using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using PCHealthPro.Core;
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
        foreach (var sample in SampleModule.Overview)
            services.AddSingleton<IHardwareModule>(new SampleModule(sample));
        services.AddSingleton<HardwareScan>();
        services.AddSingleton<LanguageService>();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<MainWindow>();
        window = services.BuildServiceProvider().GetRequiredService<MainWindow>();
        window.Activate();
    }
}
