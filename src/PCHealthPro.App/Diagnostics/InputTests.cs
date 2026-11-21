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
    // ANSI 104-key layout, measured in key units. Each cluster has its own origin.
    private const double Unit = 44;
    public static void Keyboard(StackPanel body, TestControls ui)
    {
        var actions = ui.Actions(body);
        var listen = ui.Button(actions, "KeyboardListen", (sender, _) => ((Button)sender).Focus(FocusState.Programmatic), primary: true);
        var seen = new HashSet<int>();
        var labels = new Dictionary<int, Border>();
        var layout = new Canvas { Width = 23 * Unit, Height = 6.5 * Unit };
        var extra = ui.Actions(body);
        var accent = (Style)Application.Current.Resources["TestKeyPressed"];
        var neutral = (Style)Application.Current.Resources["TestKey"];
        Border Key(int code, string name, double width, double height)
        {
            var label = new Border { Style = neutral, Width = width * Unit - 3, Height = height * Unit - 3,
                Padding = new Thickness(2), BorderThickness = new Thickness(1, 1, 1, 3),
                Child = new TextBlock { Text = name, FontSize = 12, TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            labels.Add(code, label);
            return label;
        }
        void Row(double x, double y, params (int Code, string Name, double Width, double Height)[] keys)
        {
            foreach (var k in keys)
            {
                var key = Key(k.Code, k.Name, k.Width, k.Height);
                Canvas.SetLeft(key, x * Unit);
                Canvas.SetTop(key, y * Unit);
                layout.Children.Add(key);
                x += k.Width;
            }
        }
        static (int, string, double, double) K(int code, string name, double width = 1, double height = 1) => (code, name, width, height);
        Row(0, 0, K(27,"Esc"));
        for (int group = 0; group < 3; group++)
            Row(2 + 4.5 * group, 0, Enumerable.Range(1 + 4 * group, 4).Select(i => K(111 + i, $"F{i}")).ToArray());
        Row(0,1.5,K(192,"`"),K(49,"1"),K(50,"2"),K(51,"3"),K(52,"4"),K(53,"5"),K(54,"6"),K(55,"7"),K(56,"8"),K(57,"9"),K(48,"0"),K(189,"-"),K(187,"="),K(8,"Backspace",2));
        Row(0,2.5,K(9,"Tab",1.5),K(81,"Q"),K(87,"W"),K(69,"E"),K(82,"R"),K(84,"T"),K(89,"Y"),K(85,"U"),K(73,"I"),K(79,"O"),K(80,"P"),K(219,"["),K(221,"]"),K(220,"\\",1.5));
        Row(0,3.5,K(20,"Caps Lock",1.75),K(65,"A"),K(83,"S"),K(68,"D"),K(70,"F"),K(71,"G"),K(72,"H"),K(74,"J"),K(75,"K"),K(76,"L"),K(186,";"),K(222,"'"),K(13,"Enter",2.25));
        Row(0,4.5,K(160,"Shift",2.25),K(90,"Z"),K(88,"X"),K(67,"C"),K(86,"V"),K(66,"B"),K(78,"N"),K(77,"M"),K(188,","),K(190,"."),K(191,"/"),K(161,"Shift",2.75));
        Row(0,5.5,K(162,"Ctrl",1.25),K(91,"Win",1.25),K(164,"Alt",1.25),K(32,"Space",6.25),K(165,"Alt",1.25),K(92,"Win",1.25),K(93,"Menu",1.25),K(163,"Ctrl",1.25));
        Row(15.5,0,K(44,"PrtSc"),K(145,"ScrLk"),K(19,"Pause"));
        Row(15.5,1.5,K(45,"Ins"),K(36,"Home"),K(33,"PgUp"));
        Row(15.5,2.5,K(46,"Del"),K(35,"End"),K(34,"PgDn"));
        Row(16.5,4.5,K(38,"↑"));
        Row(15.5,5.5,K(37,"←"),K(40,"↓"),K(39,"→"));
        Row(19,1.5,K(144,"Num"),K(111,"/"),K(106,"*"),K(109,"−"));
        Row(19,2.5,K(103,"7"),K(104,"8"),K(105,"9"),K(107,"+",1,2));
        Row(19,3.5,K(100,"4"),K(101,"5"),K(102,"6"));
        Row(19,4.5,K(97,"1"),K(98,"2"),K(99,"3"),K(269,"Enter",1,2));
        Row(19,5.5,K(96,"0",2),K(110,"."));
        var frame = new Border { Child = layout, Padding = new Thickness(10),
            Style = (Style)Application.Current.Resources["InspectionSurface"] };
        var view = new Viewbox { Child = frame, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        var scroll = new ScrollViewer { Content = view, HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled };
        scroll.SizeChanged += (_, e) => view.Width = Math.Clamp(e.NewSize.Width, 700, layout.Width + 22);
        body.Children.Add(scroll);
        var status = ui.Text(ui.T("KeyboardHint"));
        body.Children.Add(status);
        listen.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            int key = (int)e.OriginalKey;
            key = key switch { 16 => e.KeyStatus.ScanCode == 54 ? 161 : 160,
                17 => e.KeyStatus.IsExtendedKey ? 163 : 162, 18 => e.KeyStatus.IsExtendedKey ? 165 : 164,
                13 when e.KeyStatus.IsExtendedKey => 269, _ => key };
            if (!e.KeyStatus.IsExtendedKey && key is >= 33 and <= 46 or 12)
                key = e.KeyStatus.ScanCode switch { 71 => 103, 72 => 104, 73 => 105, 75 => 100, 76 => 101, 77 => 102,
                    79 => 97, 80 => 98, 81 => 99, 82 => 96, 83 => 110, _ => key };
            if (!labels.ContainsKey(key)) extra.Children.Add(Key(key, e.OriginalKey.ToString(), 2, 1));
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
        var area = new Canvas { Height = 340, Style = (Style)Application.Current.Resources["PointerSurface"],
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
