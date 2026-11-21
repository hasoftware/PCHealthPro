using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace PCHealthPro.App;

public sealed partial class MachineIllustration : UserControl
{
    public MachineIllustration() => InitializeComponent();

    internal void Show(string? kind, string? manufacturer, string description)
    {
        bool portable = kind == "Mobile";
        bool desktop = kind is "Desktop" or "Workstation" or "Server";
        Desktop.Visibility = desktop ? Visibility.Visible : Visibility.Collapsed;
        Laptop.Visibility = portable ? Visibility.Visible : Visibility.Collapsed;
        Other.Visibility = !portable && !desktop ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(this, description);
        if (!portable) return;
        var maker = manufacturer?.ToLowerInvariant() ?? "";
        if (maker.Contains("hewlett")) maker = "hp";
        if (maker.Contains("micro-star")) maker = "msi";
        var brand = new[] { "dell", "hp", "lenovo", "asus", "acer", "msi", "apple", "samsung", "microsoft", "lg", "huawei", "razer" }
            .FirstOrDefault(key => maker.Contains(key, StringComparison.Ordinal));
        OemLogo.Source = brand == null ? null : new BitmapImage(new Uri($"ms-appx:///Assets/Brands/{brand}.png"));
        OemName.Text = brand == null ? manufacturer ?? "" : "";
    }
}
