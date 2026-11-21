using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PCHealthPro.App.Services;
using PCHealthPro.Core.Diagnostics;

namespace PCHealthPro.App.Diagnostics;

internal sealed class TestControls(LanguageService strings, ManualTestSession session)
{
    public string T(string key) => strings.Get(key);
    public TextBlock Text(string value = "") => new() { Text = value, TextWrapping = TextWrapping.Wrap };

    public StackPanel Card(Panel host, ManualTest test, out StackPanel body)
    {
        var card = new StackPanel { Spacing = 10, Padding = new Thickness(16), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"] };
        var heading = Text(T("Test" + test));
        heading.Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"];
        card.Children.Add(heading);
        body = new StackPanel { Spacing = 8 };
        card.Children.Add(body);
        var verdict = new ComboBox { Header = T("TestVerdict"), MinWidth = 180,
            ItemsSource = Enum.GetValues<TestVerdict>().Select(v => T("Verdict" + v)).ToArray(),
            SelectedIndex = (int)session.Get(test) };
        AutomationProperties.SetAutomationId(verdict, test + "Verdict");
        verdict.SelectionChanged += (_, _) =>
        {
            if (verdict.SelectedIndex >= 0) session.Mark(test, (TestVerdict)verdict.SelectedIndex);
        };
        card.Children.Add(verdict);
        host.Children.Add(card);
        return card;
    }

    public Button Button(Panel host, string key, RoutedEventHandler click)
    {
        var button = new Button { Content = T(key) };
        AutomationProperties.SetAutomationId(button, key);
        button.Click += click;
        host.Children.Add(button);
        return button;
    }
    public ComboBox Picker(Panel host, string key)
    {
        var box = new ComboBox { Header = T(key), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(box, key);
        host.Children.Add(box);
        return box;
    }

    public string Error(Exception e) => e is UnauthorizedAccessException || e.HResult == unchecked((int)0x80070005)
        ? T("TestPermission") : $"{T("TestUnavailable")} {e.Message}";
}
