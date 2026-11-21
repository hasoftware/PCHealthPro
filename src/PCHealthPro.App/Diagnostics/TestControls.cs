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

    public void Card(Panel host, ManualTest test, out StackPanel body)
    {
        var card = new ManualTestCard { Tag = test };
        body = card.Bind(test, session, strings);
        host.Children.Add(card);
    }

    public TestActions Actions(Panel host)
    {
        var actions = new TestActions();
        host.Children.Add(actions);
        return actions;
    }

    public Button Button(Panel host, string key, RoutedEventHandler click, bool primary = false)
    {
        var button = new Button { Content = T(key) };
        if (primary)
        {
            var action = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            action.Children.Add(new FontIcon { Glyph = "\uE768", FontSize = 14, Style = (Style)Application.Current.Resources["TestGlyph"] });
            action.Children.Add(Text(T(key)));
            button.Content = action;
            AutomationProperties.SetName(button, T(key));
        }
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
