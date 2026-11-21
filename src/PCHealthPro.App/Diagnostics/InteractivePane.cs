using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PCHealthPro.App.Services;
using PCHealthPro.Core.Diagnostics;

namespace PCHealthPro.App.Diagnostics;

internal sealed class InteractivePane(StackPanel host, LanguageService strings, ManualTestSession session, Window owner) : IDisposable
{
    private IDisposable? active;
    private readonly TestControls ui = new(strings, session);

    public void Show(string section)
    {
        Dispose();
        host.Children.Clear();
        host.Visibility = Visibility.Visible;
        switch (section)
        {
            case "Camera":
                ui.Card(host, ManualTest.Camera, out var camera);
                active = new CameraTest(camera, ui);
                break;
            case "Audio":
                active = new AudioTests(host, ui);
                break;
            case "Display":
                ui.Card(host, ManualTest.Display, out var display);
                var test = new DisplayTest(owner);
                active = test;
                var strip = new Grid { ColumnSpacing = 4, Height = 24 };
                foreach (var fill in DisplayTest.Patterns())
                {
                    var swatch = new Border { Background = fill, Style = (Style)Application.Current.Resources["TestKey"] };
                    Grid.SetColumn(swatch, strip.Children.Count);
                    strip.ColumnDefinitions.Add(new ColumnDefinition());
                    strip.Children.Add(swatch);
                }
                display.Children.Add(strip);
                display.Children.Add(ui.Text(ui.T("DisplayHint")));
                ui.Button(display, "DisplayStart", (_, _) => test.Open(), primary: true);
                break;
            case "Input":
                ui.Card(host, ManualTest.Keyboard, out var keyboard);
                InputTests.Keyboard(keyboard, ui);
                ui.Card(host, ManualTest.Pointer, out var pointer);
                InputTests.Pointer(pointer, ui);
                break;
            default:
                host.Visibility = Visibility.Collapsed;
                break;
        }
    }
    public void Dispose() { active?.Dispose(); active = null; }
}
