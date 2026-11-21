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
                display.Children.Add(ui.Text(ui.T("DisplayHint")));
                ui.Button(display, "DisplayStart", (_, _) => test.Open());
                break;
            case "Input":
                ui.Card(host, ManualTest.Keyboard, out var keyboard);
                InputTests.Keyboard(keyboard, ui);
                ui.Card(host, ManualTest.Pointer, out var pointer);
                InputTests.Pointer(pointer, ui);
                break;
            case "Reports":
                var heading = ui.Text(ui.T("ManualResults"));
                heading.Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"];
                host.Children.Add(heading);
                foreach (var r in session.Snapshot())
                    host.Children.Add(ui.Text($"{ui.T("Test" + r.Test)} · {ui.T("Verdict" + r.Verdict)}"));
                break;
            default:
                host.Visibility = Visibility.Collapsed;
                break;
        }
    }
    public void Dispose() { active?.Dispose(); active = null; }
}
