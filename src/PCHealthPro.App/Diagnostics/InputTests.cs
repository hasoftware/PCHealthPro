using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace PCHealthPro.App.Diagnostics;

internal static class InputTests
{
    private static readonly string[] Rows =
    [
        "27:Esc 112:F1 113:F2 114:F3 115:F4 116:F5 117:F6 118:F7 119:F8 120:F9 121:F10 122:F11 123:F12",
        "192:` 49:1 50:2 51:3 52:4 53:5 54:6 55:7 56:8 57:9 48:0 189:- 187:= 8:Backspace",
        "9:Tab 81:Q 87:W 69:E 82:R 84:T 89:Y 85:U 73:I 79:O 80:P 219:[ 221:] 220:\\",
        "20:Caps 65:A 83:S 68:D 70:F 71:G 72:H 74:J 75:K 76:L 186:; 222:' 13:Enter",
        "160:Shift-L 90:Z 88:X 67:C 86:V 66:B 78:N 77:M 188:, 190:. 191:/ 161:Shift-R",
        "162:Ctrl-L 91:Win-L 164:Alt-L 32:Space 165:Alt-R 92:Win-R 93:Menu 163:Ctrl-R",
        "45:Ins 46:Del 36:Home 35:End 33:PgUp 34:PgDn 37:← 38:↑ 40:↓ 39:→",
        "144:Num 111:Num/ 106:Num* 109:Num- 103:Num7 104:Num8 105:Num9 107:Num+",
        "100:Num4 101:Num5 102:Num6 97:Num1 98:Num2 99:Num3 96:Num0 110:Num. 269:NumEnter"
    ];

    public static void Keyboard(StackPanel body, TestControls ui)
    {
        var actions = ui.Actions(body);
        var listen = ui.Button(actions, "KeyboardListen", (sender, _) => ((Button)sender).Focus(FocusState.Programmatic), primary: true);
        var seen = new HashSet<int>();
        var labels = new Dictionary<int, Border>();
        var layout = new StackPanel { Spacing = 4 };
        var extra = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var accent = (Style)Application.Current.Resources["TestKeyPressed"];
        var neutral = (Style)Application.Current.Resources["TestKey"];
        void Key(Panel panel, int code, string name)
        {
            double width = code switch { 32 => 160, 8 or 13 or 160 or 161 => 64, 9 or 20 => 50, _ => 32 };
            var label = new Border { Style = neutral, MinWidth = width,
                Child = new TextBlock { Text = name, HorizontalAlignment = HorizontalAlignment.Center } };
            labels.Add(code, label);
            panel.Children.Add(label);
        }
        foreach (var row in Rows)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var item in row.Split(' '))
            {
                int colon = item.IndexOf(':');
                Key(line, int.Parse(item[..colon]), item[(colon + 1)..]);
            }
            layout.Children.Add(line);
        }
        layout.Children.Add(extra);
        body.Children.Add(new ScrollViewer { Content = layout, HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled });
        var status = ui.Text(ui.T("KeyboardHint"));
        body.Children.Add(status);
        listen.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            int key = (int)e.OriginalKey;
            key = key switch { 16 => e.KeyStatus.ScanCode == 54 ? 161 : 160,
                17 => e.KeyStatus.IsExtendedKey ? 163 : 162, 18 => e.KeyStatus.IsExtendedKey ? 165 : 164,
                13 when e.KeyStatus.IsExtendedKey => 269, _ => key };
            if (!labels.ContainsKey(key)) Key(extra, key, e.OriginalKey.ToString());
            seen.Add(key);
            var tile = labels[key];
            tile.Style = accent;
            var label = (TextBlock)tile.Child;
            label.Style = (Style)Application.Current.Resources["PressedKeyLabel"];
            status.Text = $"{ui.T("KeyboardSeen")} {seen.Count} · {label.Text}";
            if (key != 9) e.Handled = true;
        }), true);
        ui.Button(actions, "TestReset", (_, _) =>
        {
            foreach (var tile in labels.Values)
            {
                tile.Style = neutral;
                ((TextBlock)tile.Child).ClearValue(FrameworkElement.StyleProperty);
            }
            seen.Clear();
            status.Text = ui.T("KeyboardHint");
            listen.Focus(FocusState.Programmatic);
        });
    }

    public static void Pointer(StackPanel body, TestControls ui)
    {
        body.Children.Add(ui.Text(ui.T("PointerHint")));
        var area = new Canvas { Height = 184, Style = (Style)Application.Current.Resources["PointerSurface"],
            ManipulationMode = ManipulationModes.None };
        AutomationProperties.SetName(area, ui.T("TestPointer"));
        AutomationProperties.SetAutomationId(area, "PointerCanvas");
        area.SizeChanged += (_, e) => area.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        body.Children.Add(new Border { Style = (Style)Application.Current.Resources["TestKey"], Padding = new Thickness(0), Child = area });
        var status = ui.Text();
        body.Children.Add(status);
        var strokes = new Dictionary<uint, Polyline>();
        var touches = new HashSet<uint>();
        int left = 0, right = 0, vertical = 0, horizontal = 0, peak = 0;
        void Update() => status.Text = string.Format(ui.T("PointerCounts"), left, right, vertical, horizontal, touches.Count, peak);
        void End(uint id) { strokes.Remove(id); touches.Remove(id); Update(); }
        area.PointerPressed += (_, e) =>
        {
            var p = e.GetCurrentPoint(area);
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
            {
                if (p.Properties.IsLeftButtonPressed) left++;
                if (p.Properties.IsRightButtonPressed) right++;
            }
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch) touches.Add(p.PointerId);
            peak = Math.Max(peak, touches.Count);
            var line = new Polyline { Style = (Style)Application.Current.Resources["PointerStroke"] };
            line.Points.Add(p.Position);
            line.Points.Add(new Point(p.Position.X + 0.5, p.Position.Y));
            if (area.Children.Count >= 160) area.Children.RemoveAt(0);
            area.Children.Add(line);
            strokes[p.PointerId] = line;
            area.CapturePointer(e.Pointer);
            Update();
            e.Handled = true;
        };
        area.PointerMoved += (_, e) =>
        {
            if (!strokes.TryGetValue(e.Pointer.PointerId, out var line)) return;
            if (line.Points.Count > 2048) line.Points.RemoveAt(0);
            line.Points.Add(e.GetCurrentPoint(area).Position);
            e.Handled = true;
        };
        area.PointerReleased += (_, e) => { End(e.Pointer.PointerId); area.ReleasePointerCapture(e.Pointer); e.Handled = true; };
        area.PointerCanceled += (_, e) => End(e.Pointer.PointerId);
        area.PointerCaptureLost += (_, e) => End(e.Pointer.PointerId);
        area.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, e) =>
        {
            var p = e.GetCurrentPoint(area).Properties;
            if (p.IsHorizontalMouseWheel) horizontal += p.MouseWheelDelta; else vertical += p.MouseWheelDelta;
            Update();
            e.Handled = true;
        }), true);
        ui.Button(body, "TestReset", (_, _) =>
        {
            area.ReleasePointerCaptures();
            strokes.Clear(); touches.Clear(); area.Children.Clear();
            left = right = vertical = horizontal = peak = 0;
            Update();
        });
        Update();
    }
}
