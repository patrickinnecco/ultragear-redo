using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using Ultragear.Core;
using static Ultragear.Native.NativeMethods;

namespace Ultragear.Features
{
    public sealed class SplitLayout
    {
        public SplitLayout(string id, string name, params Rect[] zones) { Id = id; Name = name; Zones = zones; }
        public string Id { get; }
        public string Name { get; }
        public Rect[] Zones { get; set; }   // fractions of the work area
    }

    public static class SplitLayouts
    {
        public static readonly List<SplitLayout> All = new()
        {
            new("Full", "Tela cheia", new Rect(0, 0, 1, 1)),
            new("Main", "2 telas (principal + lateral)", new Rect(0, 0, 0.75, 1), new Rect(0.75, 0, 0.25, 1)),
            new("Halves", "2 telas (lado a lado)", new Rect(0, 0, 0.5, 1), new Rect(0.5, 0, 0.5, 1)),
            new("TopBottom", "2 telas (em cima e embaixo)", new Rect(0, 0, 1, 0.5), new Rect(0, 0.5, 1, 0.5)),
            new("Left2", "3 telas (2 à esquerda, 1 à direita)", new Rect(0, 0, 0.5, 0.5), new Rect(0, 0.5, 0.5, 0.5), new Rect(0.5, 0, 0.5, 1)),
            new("Right2", "3 telas (1 à esquerda, 2 à direita)", new Rect(0, 0, 0.5, 1), new Rect(0.5, 0, 0.5, 0.5), new Rect(0.5, 0.5, 0.5, 0.5)),
            new("Top2", "3 telas (2 em cima, 1 embaixo)", new Rect(0, 0, 0.5, 0.5), new Rect(0.5, 0, 0.5, 0.5), new Rect(0, 0.5, 1, 0.5)),
            new("Bottom2", "3 telas (1 em cima, 2 embaixo)", new Rect(0, 0, 1, 0.5), new Rect(0, 0.5, 0.5, 0.5), new Rect(0.5, 0.5, 0.5, 0.5)),
            new("Thirds", "3 telas (colunas iguais)", new Rect(0, 0, 1 / 3.0, 1), new Rect(1 / 3.0, 0, 1 / 3.0, 1), new Rect(2 / 3.0, 0, 1 / 3.0, 1)),
            new("Grid4", "4 telas (grade)", new Rect(0, 0, 0.5, 0.5), new Rect(0.5, 0, 0.5, 0.5), new Rect(0, 0.5, 0.5, 0.5), new Rect(0.5, 0.5, 0.5, 0.5)),
            new("Custom", "Personalizado"),
        };

        public static SplitLayout Get(AppSettings s)
        {
            var l = All.FirstOrDefault(x => x.Id == s.SplitLayout) ?? All[0];
            if (l.Id == "Custom") l.Zones = Grid(Math.Clamp(s.CustomColumns, 1, 6), Math.Clamp(s.CustomRows, 1, 4));
            return l;
        }

        public static Rect[] Grid(int cols, int rows)
        {
            var z = new List<Rect>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    z.Add(new Rect(c / (double)cols, r / (double)rows, 1.0 / cols, 1.0 / rows));
            return z.ToArray();
        }
    }

    /// <summary>Places top-level windows into layout zones on the selected monitor's work area.</summary>
    public static class WindowPlacer
    {
        public static RECT ZoneToPixels(RECT work, Rect z) => new RECT
        {
            Left = work.Left + (int)Math.Round(z.X * work.Width),
            Top = work.Top + (int)Math.Round(z.Y * work.Height),
            Right = work.Left + (int)Math.Round((z.X + z.Width) * work.Width),
            Bottom = work.Top + (int)Math.Round((z.Y + z.Height) * work.Height)
        };

        public static bool IsArrangeable(IntPtr h)
        {
            if (!IsWindowVisible(h) || IsIconic(h)) return false;
            if (GetWindow(h, GW_OWNER) != IntPtr.Zero) return false;
            int ex = GetWindowLong(h, GWL_EXSTYLE), st = GetWindowLong(h, GWL_STYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0 || (st & WS_CAPTION) != WS_CAPTION) return false;
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return false;
            if (h == GetShellWindow()) return false;
            var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
            return sb.Length > 0;
        }

        public static void Place(IntPtr h, RECT target)
        {
            if (IsZoomed(h) || IsIconic(h)) ShowWindow(h, SW_RESTORE);
            // compensate the invisible resize borders of Windows 10/11
            GetWindowRect(h, out RECT wr);
            int l = 0, t = 0, r = 0, b = 0;
            if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT fr, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0)
            { l = fr.Left - wr.Left; t = fr.Top - wr.Top; r = wr.Right - fr.Right; b = wr.Bottom - fr.Bottom; }
            SetWindowPos(h, IntPtr.Zero, target.Left - l, target.Top - t, target.Width + l + r, target.Height + t + b, SWP_NOZORDER | SWP_NOACTIVATE);
        }

        public static List<IntPtr> WindowsOn(MonitorTarget m)
        {
            var list = new List<IntPtr>();
            EnumWindows((h, p) =>
            {
                if (IsArrangeable(h) && MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST) == m.HMonitor) list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list; // EnumWindows returns Z-order: most recently used first
        }

        /// <summary>Quick split: arranges the most recently used windows of the monitor into the zones.</summary>
        public static int Arrange(MonitorTarget m, SplitLayout layout)
        {
            var wins = WindowsOn(m);
            int n = Math.Min(wins.Count, layout.Zones.Length);
            for (int i = 0; i < n; i++) Place(wins[i], ZoneToPixels(m.WorkArea, layout.Zones[i]));
            return n;
        }

        public static int ZoneIndexOf(MonitorTarget m, SplitLayout layout, IntPtr h)
        {
            if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT fr, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) != 0)
                GetWindowRect(h, out fr);
            int cx = (fr.Left + fr.Right) / 2, cy = (fr.Top + fr.Bottom) / 2;
            return ZoneAt(m, layout, cx, cy);
        }

        public static int ZoneAt(MonitorTarget m, SplitLayout layout, int x, int y)
        {
            for (int i = 0; i < layout.Zones.Length; i++)
            {
                var z = ZoneToPixels(m.WorkArea, layout.Zones[i]);
                if (x >= z.Left && x < z.Right && y >= z.Top && y < z.Bottom) return i;
            }
            return -1;
        }

        /// <summary>Window placement hotkey: moves the foreground window to the neighbouring zone.</summary>
        public static void MoveForeground(MonitorTarget m, SplitLayout layout, int dx, int dy)
        {
            var h = GetForegroundWindow();
            if (!IsArrangeable(h) || layout.Zones.Length == 0) return;
            int cur = ZoneIndexOf(m, layout, h);
            int next;
            if (cur < 0) next = 0;
            else
            {
                var c = layout.Zones[cur];
                var center = new Point(c.X + c.Width / 2 + dx * 0.5, c.Y + c.Height / 2 + dy * 0.5);
                next = cur;
                double best = double.MaxValue;
                for (int i = 0; i < layout.Zones.Length; i++)
                {
                    if (i == cur) continue;
                    var z = layout.Zones[i];
                    var zc = new Point(z.X + z.Width / 2, z.Y + z.Height / 2);
                    double ddx = zc.X - (c.X + c.Width / 2), ddy = zc.Y - (c.Y + c.Height / 2);
                    if (dx != 0 && Math.Sign(ddx) != dx) continue;
                    if (dy != 0 && Math.Sign(ddy) != dy) continue;
                    double d = (zc - center).Length;
                    if (d < best) { best = d; next = i; }
                }
            }
            Place(h, ZoneToPixels(m.WorkArea, layout.Zones[next]));
        }
    }
}
