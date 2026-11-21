using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PCHealthPro.Core;

namespace PCHealthPro.App;

public sealed partial class HardwareTile : UserControl
{
    public HardwareTile() => InitializeComponent();

    internal void Bind(string category, string glyph, OverviewTileContent content, HealthStatus status, string label, string? brand, Action open)
    {
        Category.Text = category;
        Icon.Glyph = glyph;
        Reading.Text = content.Title;
        Facts.Text = content.Facts;
        Facts.Visibility = content.Facts.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Detail.Text = content.Detail;
        Detail.Visibility = content.Detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        BrandMark.Apply(Brand, brand);
        if (status is HealthStatus.Detected or HealthStatus.Absent || content.Title == label) label = "";
        State.Text = label;
        StatusLine.Visibility = label.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        StatusIcon.Visibility = status == HealthStatus.Detected ? Visibility.Collapsed : Visibility.Visible;
        StatusIcon.Glyph = status switch
        {
            HealthStatus.Good => "\uE73E", HealthStatus.Attention => "\uE7BA", HealthStatus.Problem => "\uEA39",
            HealthStatus.Detected => "\uE946", _ => "\uE738"
        };
        if (Resources.TryGetValue(status.ToString(), out var style)) StatusIcon.Style = (Style)style;
        AutomationProperties.SetName(Open, string.Join(", ", new[] { category, content.Title, content.Facts, content.Detail, label }.Where(s => s.Length > 0)));
        ToolTipService.SetToolTip(Open, content.Title);
        Open.Click += (_, _) => open();
    }
}
