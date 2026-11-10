using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Ultragear.Core;
using Ultragear.Features;
using static Ultragear.Native.NativeMethods;

namespace Ultragear.UI
{
    /// <summary>Click-through, non-activating overlay that draws the zones on the target monitor.</summary>
    public sealed class ZoneOverlay : Window
    {
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int idx, int v);
        const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
        readonly Canvas _canvas = new();
        Rectangle[] _rects = Array.Empty<Rectangle>();

        public ZoneOverlay()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
            Content = _canvas;
            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            };
        }

        public void ShowZones(MonitorTarget t, SplitLayout layout)
        {
            if (!IsVisible) { Left = 0; Top = 0; Width = 10; Height = 10; Show(); }
            var h = new WindowInteropHelper(this).Handle;
            var wa = t.WorkArea;
            SetWindowPos(h, new IntPtr(-1), wa.Left, wa.Top, wa.Width, wa.Height, SWP_NOACTIVATE);
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            _canvas.Children.Clear();
            _rects = new Rectangle[layout.Zones.Length];
            for (int i = 0; i < layout.Zones.Length; i++)
            {
                var z = WindowPlacer.ZoneToPixels(wa, layout.Zones[i]);
                var r = new Rectangle
                {
                    Width = Math.Max(0, (z.Width - 16) / scale), Height = Math.Max(0, (z.Height - 16) / scale), RadiusX = 14, RadiusY = 14,
                    Stroke = new SolidColorBrush(Color.FromArgb(0xE0, 0x5E, 0xE3, 0xC3)), StrokeThickness = 3,
                    Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x2B, 0x32, 0x63))
                };
                Canvas.SetLeft(r, (z.Left - wa.Left + 8) / scale); Canvas.SetTop(r, (z.Top - wa.Top + 8) / scale);
                _canvas.Children.Add(r);
                var num = new TextBlock { Text = (i + 1).ToString(), FontSize = 48, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)) };
                Canvas.SetLeft(num, (z.Left - wa.Left + z.Width / 2) / scale - 14); Canvas.SetTop(num, (z.Top - wa.Top + z.Height / 2) / scale - 32);
                _canvas.Children.Add(num);
                _rects[i] = r;
            }
        }

        public void Highlight(int index)
        {
            for (int i = 0; i < _rects.Length; i++)
                _rects[i].Fill = new SolidColorBrush(i == index ? Color.FromArgb(0x70, 0x5E, 0xE3, 0xC3) : Color.FromArgb(0x30, 0x2B, 0x32, 0x63));
        }
    }

    /// <summary>Drag-to-zone snapping using the public WinEvent move/size notifications.</summary>
    public sealed class SnapManager : IDisposable
    {
        readonly WinEventDelegate _proc;
        IntPtr _hook;
        ZoneOverlay _overlay;
        readonly DispatcherTimer _track = new() { Interval = TimeSpan.FromMilliseconds(40) };
        readonly DispatcherTimer _hide = new();
        IntPtr _dragging;
        RECT _startRect;
        bool _enabled;

        public SnapManager()
        {
            _proc = OnEvent;
            _track.Tick += (s, e) => Track();
            _hide.Tick += (s, e) => { _hide.Stop(); if (_dragging == IntPtr.Zero) _overlay?.Hide(); };
        }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (value && _hook == IntPtr.Zero)
                    _hook = SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
                else if (!value && _hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
            }
        }

        ZoneOverlay Overlay => _overlay ??= new ZoneOverlay();

        public void Preview(int ms)
        {
            var t = Host.Monitor.Target;
            if (t == null) return;
            Overlay.ShowZones(t, SplitLayouts.Get(Host.Settings));
            _hide.Stop(); _hide.Interval = TimeSpan.FromMilliseconds(ms); _hide.Start();
        }

        void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0) return;
            var t = Host.Monitor.Target;
            var layout = SplitLayouts.Get(Host.Settings);
            if (t == null || layout.Zones.Length < 2 || !WindowPlacer.IsArrangeable(hwnd)) return;
            if (evt == EVENT_SYSTEM_MOVESIZESTART)
            {
                _dragging = hwnd;
                GetWindowRect(hwnd, out _startRect);
                Overlay.ShowZones(t, layout);
                Overlay.Highlight(-1);
                _track.Start();
            }
            else if (evt == EVENT_SYSTEM_MOVESIZEEND && hwnd == _dragging)
            {
                _track.Stop();
                _dragging = IntPtr.Zero;
                Overlay.Hide();
                GetWindowRect(hwnd, out var end);
                bool resized = Math.Abs(end.Width - _startRect.Width) > 2 || Math.Abs(end.Height - _startRect.Height) > 2;
                bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                if (resized || ctrl) return;
                GetCursorPos(out var p);
                int zone = WindowPlacer.ZoneAt(t, layout, p.X, p.Y);
                if (zone >= 0) WindowPlacer.Place(hwnd, WindowPlacer.ZoneToPixels(t.WorkArea, layout.Zones[zone]));
            }
        }

        void Track()
        {
            var t = Host.Monitor.Target;
            if (t == null || _overlay == null) return;
            GetCursorPos(out var p);
            _overlay.Highlight(WindowPlacer.ZoneAt(t, SplitLayouts.Get(Host.Settings), p.X, p.Y));
        }

        public void Dispose() { Enabled = false; _overlay?.Close(); }
    }

    /// <summary>Global hotkeys (RegisterHotKey) for quick split and window placement.</summary>
    public sealed class HotkeyManager : IDisposable
    {
        readonly Window _owner;
        HwndSource _src;
        bool _registered;
        public string FailedText { get; private set; }
        const int IdArrange = 0x5501, IdLeft = 0x5502, IdRight = 0x5503, IdUp = 0x5504, IdDown = 0x5505;

        public HotkeyManager(Window owner)
        {
            _owner = owner;
            var helper = new WindowInteropHelper(owner);
            helper.EnsureHandle();
            _src = HwndSource.FromHwnd(helper.Handle);
            _src.AddHook(WndProc);
            Apply();
        }

        public void Apply()
        {
            var h = _src.Handle;
            if (_registered) { foreach (var id in new[] { IdArrange, IdLeft, IdRight, IdUp, IdDown }) UnregisterHotKey(h, id); _registered = false; }
            FailedText = null;
            if (!Host.Settings.SplitHotkeysEnabled) return;
            uint mods = MOD_ALT | MOD_SHIFT | MOD_NOREPEAT;
            var failed = new System.Collections.Generic.List<string>();
            if (!RegisterHotKey(h, IdArrange, mods, 0x4C)) failed.Add("Alt+Shift+L");
            if (!RegisterHotKey(h, IdLeft, mods, 0x25)) failed.Add("Alt+Shift+←");
            if (!RegisterHotKey(h, IdUp, mods, 0x26)) failed.Add("Alt+Shift+↑");
            if (!RegisterHotKey(h, IdRight, mods, 0x27)) failed.Add("Alt+Shift+→");
            if (!RegisterHotKey(h, IdDown, mods, 0x28)) failed.Add("Alt+Shift+↓");
            _registered = true;
            if (failed.Count > 0) FailedText = "Já em uso por outro programa (ex.: LG Switch): " + string.Join(", ", failed);
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg != WM_HOTKEY) return IntPtr.Zero;
            var t = Host.Monitor.Target;
            if (t == null) return IntPtr.Zero;
            var layout = SplitLayouts.Get(Host.Settings);
            switch (w.ToInt32())
            {
                case IdArrange: WindowPlacer.Arrange(t, layout); break;
                case IdLeft: WindowPlacer.MoveForeground(t, layout, -1, 0); break;
                case IdRight: WindowPlacer.MoveForeground(t, layout, 1, 0); break;
                case IdUp: WindowPlacer.MoveForeground(t, layout, 0, -1); break;
                case IdDown: WindowPlacer.MoveForeground(t, layout, 0, 1); break;
            }
            handled = true;
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_src == null) return;
            foreach (var id in new[] { IdArrange, IdLeft, IdRight, IdUp, IdDown }) UnregisterHotKey(_src.Handle, id);
            _src.RemoveHook(WndProc);
            _src = null;
        }
    }
}
