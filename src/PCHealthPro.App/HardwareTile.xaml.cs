using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PCHealthPro.Core;

namespace PCHealthPro.App;

public sealed partial class HardwareTile : UserControl
{
    public HardwareTile() => InitializeComponent();

    internal void Bind(string category, string glyph, string value, HealthStatus status, string label, Action open)
    {
        Category.Text = category;
        Icon.Glyph = glyph;
        Reading.Text = value;
        State.Text = label;
        StatusLine.Visibility = label.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        StatusIcon.Visibility = status == HealthStatus.Detected ? Visibility.Collapsed : Visibility.Visible;
        StatusIcon.Glyph = status switch
        {
            HealthStatus.Good => "\uE73E", HealthStatus.Attention => "\uE7BA", HealthStatus.Problem => "\uEA39",
            HealthStatus.Detected => "\uE946", _ => "\uE738"
        };
        if (Resources.TryGetValue(status.ToString(), out var style)) StatusIcon.Style = (Style)style;
        AutomationProperties.SetName(Open, $"{category}, {value}, {label}");
        ToolTipService.SetToolTip(Open, value);
        Open.Click += (_, _) => open();
    }
}
