using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace PCHealthPro.App.Diagnostics;

internal sealed class DisplayTest(Window owner) : IDisposable
{
    private Window? screen;
    internal static Brush[] Patterns() => [
        new SolidColorBrush(Colors.Black), new SolidColorBrush(Colors.White), new SolidColorBrush(Colors.Red),
        new SolidColorBrush(Colors.Lime), new SolidColorBrush(Colors.Blue),
        new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0),
            GradientStops = { new GradientStop { Color = Colors.Black }, new GradientStop { Color = Colors.White, Offset = 1 } } }
    ];
    public void Open()
    {
        if (screen != null) { screen.Activate(); return; }
        var window = new Window { Title = owner.Title };
        screen = window;
        var surface = new Grid();
        var focus = new ContentControl { IsTabStop = true, UseSystemFocusVisuals = false };
        surface.Children.Add(focus);
        var fills = Patterns();
        int index = 0;
        surface.Background = fills[0];
        void Next() => surface.Background = fills[index = (index + 1) % fills.Length];
        surface.PointerPressed += (_, e) => { Next(); e.Handled = true; };
        surface.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key == VirtualKey.Escape) window.Close();
            else if (!e.KeyStatus.WasKeyDown) Next();
            e.Handled = true;
        }), true);
        window.Content = surface;
        window.Closed += (_, _) => screen = null;
        window.AppWindow.Move(owner.AppWindow.Position);
        window.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        window.Activate();
        focus.Focus(FocusState.Programmatic);
    }

    public void Dispose() => screen?.Close();
}
