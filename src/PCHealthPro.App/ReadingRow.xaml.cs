using Microsoft.UI.Xaml.Controls;

namespace PCHealthPro.App;

public sealed partial class ReadingRow : UserControl
{
    public ReadingRow() => InitializeComponent();

    internal ReadingRow(string label, string value) : this()
    {
        Label.Text = label;
        Value.Text = value;
    }
}
