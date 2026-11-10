using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ultragear.Core;
using static Ultragear.Native.NativeMethods;

namespace Ultragear.UI
{
    /// <summary>Full-screen test patterns rendered pixel-exact (bitmap at native resolution, no scaling).</summary>
    public sealed class PatternWindow : Window
    {
        readonly MonitorTarget _t;
        int _index;
        readonly Image _img = new() { Stretch = Stretch.None, SnapsToDevicePixels = true };
        readonly TextBlock _caption = new() { Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)), FontSize = 14, Margin = new Thickness(16), VerticalAlignment = VerticalAlignment.Bottom };
        readonly DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromMinutes(3) };
        static readonly string[] Names = { "Rampa de cinza (0–255)", "Nível de preto: quadrados 1 a 24 sobre 0", "Saturação de branco: quadrados 231 a 254 sobre 255",
                                           "Gama 2.2: as faixas devem se confundir com o fundo listrado quando vistas de longe", "Barras de cor (degradês R, G, B, C, M, Y)", "Cor sólida" };
        static readonly Color[] Solids = { Colors.White, Color.FromRgb(128, 128, 128), Colors.Red, Colors.Lime, Colors.Blue, Colors.Black };
        int _solid;

        public PatternWindow(MonitorTarget t, int index)
        {
            _t = t; _index = index;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Background = Brushes.Black; Topmost = true; ShowInTaskbar = false;
            Cursor = Cursors.None;
            var g = new Grid();
            g.Children.Add(_img); g.Children.Add(_caption);
            Content = g;
            KeyDown += OnKey;
            MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Right) Close(); else Next(1); };
            _autoClose.Tick += (s, e) => Close();
            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                SetWindowPos(h, new IntPtr(-1), t.Bounds.Left, t.Bounds.Top, t.Bounds.Width, t.Bounds.Height, SWP_NOACTIVATE);
            };
            Loaded += (s, e) => { Render(); _autoClose.Start(); Activate(); };
        }

        void OnKey(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key is Key.Right or Key.Space or Key.Down) Next(1);
            else if (e.Key is Key.Left or Key.Up) Next(-1);
        }

        void Next(int d)
        {
            if (_index == 5 && d > 0 && _solid < Solids.Length - 1) { _solid++; Render(); return; }
            if (_index == 5 && d < 0 && _solid > 0) { _solid--; Render(); return; }
            _index = (_index + d + 6) % 6; _solid = 0; Render();
        }

        void Render()
        {
            int w = _t.Bounds.Width, h = _t.Bounds.Height;
            var px = new byte[w * h * 4];
            void Put(int x, int y, byte r, byte g, byte b) { int i = (y * w + x) * 4; px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255; }
            void Fill(int x0, int y0, int x1, int y1, byte r, byte g, byte b)
            { for (int y = Math.Max(0, y0); y < Math.Min(h, y1); y++) for (int x = Math.Max(0, x0); x < Math.Min(w, x1); x++) Put(x, y, r, g, b); }

            switch (_index)
            {
                case 0: // grey ramp: smooth on top half, 32 steps below
                    for (int x = 0; x < w; x++) { byte v = (byte)(x * 255 / (w - 1)); Fill(x, 0, x + 1, h / 2, v, v, v); }
                    for (int i = 0; i < 32; i++) { byte v = (byte)(i * 255 / 31); Fill(i * w / 32, h / 2, (i + 1) * w / 32, h, v, v, v); }
                    break;
                case 1:
                    Fill(0, 0, w, h, 0, 0, 0);
                    for (int i = 0; i < 24; i++) { byte v = (byte)(i + 1); int cw = w / 8, ch = h / 3; Fill((i % 8) * cw + cw / 6, (i / 8) * ch + ch / 6, (i % 8) * cw + cw * 5 / 6, (i / 8) * ch + ch * 5 / 6, v, v, v); }
                    break;
                case 2:
                    Fill(0, 0, w, h, 255, 255, 255);
                    for (int i = 0; i < 24; i++) { byte v = (byte)(231 + i); int cw = w / 8, ch = h / 3; Fill((i % 8) * cw + cw / 6, (i / 8) * ch + ch / 6, (i % 8) * cw + cw * 5 / 6, (i / 8) * ch + ch * 5 / 6, v, v, v); }
                    break;
                case 3: // 1-px black/white lines average to 50% light = 186 grey at gamma 2.2
                    for (int y = 0; y < h; y++) { byte v = (byte)(y % 2 == 0 ? 255 : 0); Fill(0, y, w, y + 1, v, v, v); }
                    foreach (var (fx, gv) in new[] { (0.15, (byte)170), (0.40, (byte)186), (0.65, (byte)200) })
                        Fill((int)(fx * w), h / 3, (int)(fx * w) + w / 6, h * 2 / 3, gv, gv, gv);
                    break;
                case 4:
                    var cols = new (int r, int g, int b)[] { (1, 0, 0), (0, 1, 0), (0, 0, 1), (0, 1, 1), (1, 0, 1), (1, 1, 0) };
                    for (int c = 0; c < 6; c++)
                        for (int x = 0; x < w; x++)
                        {
                            byte v = (byte)(x * 255 / (w - 1));
                            Fill(x, c * h / 6, x + 1, (c + 1) * h / 6, (byte)(v * cols[c].r), (byte)(v * cols[c].g), (byte)(v * cols[c].b));
                        }
                    break;
                case 5:
                    var sc = Solids[_solid];
                    Fill(0, 0, w, h, sc.R, sc.G, sc.B);
                    break;
            }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            _img.Source = bmp; _img.Width = w / scale; _img.Height = h / scale;
            _caption.Text = Names[_index] + "   •   ← → trocar   •   Esc fechar";
            _caption.Foreground = new SolidColorBrush(_index == 2 || (_index == 5 && Solids[_solid] == Colors.White) ? Color.FromRgb(140, 140, 140) : Color.FromRgb(110, 110, 110));
        }
    }
}
