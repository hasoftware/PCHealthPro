using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PCHealthPro.App.Services;
using PCHealthPro.Core.Diagnostics;

namespace PCHealthPro.App.Diagnostics;

public sealed partial class ManualTestCard : UserControl
{
    public ManualTestCard() => InitializeComponent();

    internal StackPanel Bind(ManualTest test, ManualTestSession session, LanguageService strings)
    {
        Heading.Text = strings.Get("Test" + test);
        Icon.Glyph = test switch { ManualTest.Camera => "\uE722", ManualTest.Microphone => "\uE720",
            ManualTest.Speakers => "\uE767", ManualTest.Display => "\uE7F4", ManualTest.Keyboard => "\uE765", _ => "\uE962" };
        Verdict.Header = strings.Get("TestVerdict");
        Verdict.ItemsSource = Enum.GetValues<TestVerdict>().Select(v => strings.Get("Verdict" + v)).ToArray();
        Verdict.SelectedIndex = (int)session.Get(test);
        AutomationProperties.SetAutomationId(Verdict, test + "Verdict");
        AutomationProperties.SetName(Verdict, Heading.Text + " · " + Verdict.Header);
        Verdict.SelectionChanged += (_, _) => { if (Verdict.SelectedIndex >= 0) session.Mark(test, (TestVerdict)Verdict.SelectedIndex); };
        return TestBody;
    }

    private void ResizeHeader(object sender, SizeChangedEventArgs args)
    {
        bool narrow = args.NewSize.Width < 360;
        Grid.SetColumn(Verdict, narrow ? 0 : 1);
        Grid.SetRow(Verdict, narrow ? 1 : 0);
        HeaderGrid.RowSpacing = narrow ? 8 : 0;
    }
}
