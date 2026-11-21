using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PCHealthPro.Core;
using Windows.UI;

namespace PCHealthPro.App;

internal static class BrandMark
{
    internal static string? For(HardwareReading reading) => reading.Category switch
    {
        "CPU" or "GPU" => Chip(reading.Groups.SelectMany(g => g.Fields).FirstOrDefault(f => f.Label == "Manufacturer")?.Value ?? reading.Value),
        "Windows" => Windows(reading.Value), _ => null
    };

    internal static string? Chip(string? name)
    {
        var text = name?.ToUpperInvariant() ?? "";
        if (text.Contains("INTEL")) return "intel";
        if (text.Contains("AMD") || text.Contains("ADVANCED MICRO DEVICES")) return "amd";
        if (text.Contains("NVIDIA")) return "nvidia";
        return null;
    }

    internal static string? Windows(string? caption) => caption?.Contains("Windows 10") == true ? "windows10" : "windows";
    internal static string? Office(string? product)
    {
        if (product?.Contains("365", StringComparison.OrdinalIgnoreCase) == true) return "m365";
        if (product?.Contains("2016") == true || product?.Contains("2013") == true || product?.Contains("Office 15") == true) return "officeClassic";
        return "officeModern";
    }

    internal static void Apply(Border host, string? brand)
    {
        if (brand == null) return;
        bool wide = brand is "intel" or "amd" or "nvidia";
        host.Visibility = Visibility.Visible;
        host.Width = wide ? 72 : 36;
        host.Height = 32;
        host.Padding = new Thickness(wide ? 6 : 4, 4, wide ? 6 : 4, 4);
        host.CornerRadius = new CornerRadius(5);
        host.Background = new SolidColorBrush(Color.FromArgb(255, 246, 247, 248));
        host.Child = new Image { Stretch = Stretch.Uniform, Source = new BitmapImage(new Uri($"ms-appx:///Assets/Brands/{brand}.png")) };
    }
}
