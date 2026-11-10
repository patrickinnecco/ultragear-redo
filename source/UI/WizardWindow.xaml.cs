using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Ultragear.Core;
using M = Ultragear.Core.Lg27GX704A;

namespace Ultragear.UI
{
    /// <summary>
    /// Personalized Picture wizard: each step changes ONE real monitor setting of the "Imagem Personalizada"
    /// mode live, while the user compares a reference image drawn by this app.
    /// </summary>
    public partial class WizardWindow : Window
    {
        sealed class Step
        {
            public string Title, Question, Guide; public byte Code; public (string label, int value)[] Options; public Action<Canvas> Draw;
        }

        readonly List<Step> _steps;
        readonly Dictionary<byte, int> _chosen = new();
        Profile _original;          // state before the wizard (to restore on cancel)
        Profile _personalBefore;    // Personalized mode values before changes
        int _index = -1;
        bool _busy, _done;

        public WizardWindow()
        {
            InitializeComponent();
            _steps = new List<Step>
            {
                new() { Title = "Brilho", Code = M.Brightness, Question = "Qual nível de brilho é mais confortável?",
                        Guide = "Olhe a imagem ao lado. Escolha o nível em que o branco não incomoda e os detalhes continuam visíveis.",
                        Options = new[] { ("Muito baixo", 20), ("Baixo", 40), ("Médio", 60), ("Alto", 80), ("Máximo", 100) }, Draw = DrawScene },
                new() { Title = "Contraste", Code = M.Contrast, Question = "Em qual contraste as faixas claras continuam separadas?",
                        Guide = "Observe os quadrados quase brancos no alto. Prefira o maior contraste em que todos ainda se distinguem.",
                        Options = new[] { ("50", 50), ("60", 60), ("70 (padrão)", 70), ("80", 80), ("90", 90) }, Draw = DrawWhiteSteps },
                new() { Title = "Estabilizador de preto", Code = M.BlackStabilizer, Question = "Em qual nível você vê os quadrados escuros?",
                        Guide = "Valores maiores clareiam áreas escuras (útil em jogos). Escolha o menor nível em que você enxerga a maioria dos quadrados.",
                        Options = new[] { ("20", 20), ("35", 35), ("50 (padrão)", 50), ("65", 65), ("80", 80) }, Draw = DrawBlackSteps },
                new() { Title = "Temperatura de cor", Code = M.ColorTempKelvin, Question = "Qual tom de branco você prefere?",
                        Guide = "Valores menores deixam o branco mais quente (amarelado); maiores, mais frio (azulado).",
                        Options = new[] { ("5000 K", 5000), ("5500 K", 5500), ("6500 K", 6500), ("7500 K", 7500), ("9300 K", 9300) }, Draw = DrawScene },
                new() { Title = "Nitidez", Code = M.Sharpness, Question = "Em qual nitidez o texto fica mais natural?",
                        Guide = "Observe as linhas finas e o texto pequeno. Nitidez demais cria contornos; de menos deixa tudo suave.",
                        Options = new[] { ("30", 30), ("40", 40), ("50 (padrão)", 50), ("60", 60), ("70", 70) }, Draw = DrawText },
            };
            Loaded += (s, e) => { PlaceOnMonitor(); ShowIntro(); };
        }

        void PlaceOnMonitor()
        {
            var t = Host.Monitor.Target;
            if (t == null) return;
            var src = PresentationSource.FromVisual(this);
            double sx = src?.CompositionTarget.TransformFromDevice.M11 ?? 1, sy = src?.CompositionTarget.TransformFromDevice.M22 ?? 1;
            Left = t.WorkArea.Left * sx + (t.WorkArea.Width * sx - Width) / 2;
            Top = t.WorkArea.Top * sy + (t.WorkArea.Height * sy - Height) / 2;
        }

        void ShowIntro()
        {
            StepText.Text = "Ajuste guiado do modo “Imagem Personalizada”";
            Question.Text = "Como funciona";
            Guide.Text = "O assistente muda o monitor para o modo Imagem Personalizada e, em 5 passos, aplica ao vivo cada opção que você escolher " +
                         "(brilho, contraste, estabilizador de preto, temperatura de cor e nitidez). Ao final você pode salvar ou desfazer tudo.";
            Options.Children.Clear();
            BackBtn.IsEnabled = false;
            NextBtn.Content = "Começar";
            DrawScene(Reference);
            RenderDots();
        }

        async Task BeginAsync()
        {
            _busy = true; Status.Text = "Mudando para o modo Imagem Personalizada…";
            try
            {
                if (Host.Monitor.State.Mode == null) await Host.Monitor.ReadAllAsync();
                _original = Host.Monitor.CaptureProfile("antes do assistente");
                var r = await Host.Monitor.SetPictureModeAsync(65);
                if (!r.Ok) { Status.Text = "O monitor não aceitou o modo Imagem Personalizada."; _busy = false; return; }
                _personalBefore = Host.Monitor.CaptureProfile("personalizada antes");
                // Kelvin control requires the Manual colour preset
                var ct = await Host.Monitor.SetColorPresetAsync(M.ManualPresetValue);
                if (!ct.Ok) Status.Text = "Aviso: temperatura manual indisponível neste modo; o passo de cor pode não ter efeito.";
                else Status.Text = "";
                _busy = false;
                Go(0);
            }
            catch (Exception ex) { Status.Text = ex.Message; _busy = false; }
        }

        void Go(int i)
        {
            _index = i;
            if (i >= _steps.Count) { ShowSummary(); return; }
            var st = _steps[i];
            StepText.Text = $"Passo {i + 1} de {_steps.Count}: {st.Title}";
            Question.Text = st.Question; Guide.Text = st.Guide;
            BackBtn.IsEnabled = i > 0;
            NextBtn.Content = i == _steps.Count - 1 ? "Concluir" : "Próximo";
            st.Draw(Reference);
            Options.Children.Clear();
            uint? current = CurrentValue(st.Code);
            foreach (var (label, value) in st.Options)
            {
                var rb = new RadioButton { Content = label, GroupName = "opt" + i, Tag = value, Margin = new Thickness(0, 0, 0, 8), Style = OptionStyle() };
                if (_chosen.TryGetValue(st.Code, out var ch) ? ch == value : current == value) rb.IsChecked = true;
                rb.Checked += async (s, e) => await Choose(st, value);
                Options.Children.Add(rb);
            }
            RenderDots();
        }

        uint? CurrentValue(byte code)
        {
            var s = Host.Monitor.State;
            return code switch { M.Brightness => s.Brightness, M.Contrast => s.Contrast, M.BlackStabilizer => s.BlackStabilizer, M.ColorTempKelvin => s.Kelvin, M.Sharpness => s.Sharpness, _ => null };
        }

        async Task Choose(Step st, int value)
        {
            _chosen[st.Code] = value;
            Status.Text = "Aplicando no monitor…";
            var r = await Host.Monitor.SetAsync(st.Code, (uint)value);
            Status.Text = r.Ok ? $"{st.Title}: {value} aplicado e confirmado pelo monitor." : $"{st.Title}: o monitor não aceitou ({r.Error ?? "valor atual " + r.Actual}).";
        }

        void ShowSummary()
        {
            StepText.Text = "Análise concluída";
            Question.Text = "Resumo";
            Guide.Text = "Os valores abaixo já estão aplicados no modo Imagem Personalizada. “Salvar” mantém; fechar a janela desfaz.";
            Options.Children.Clear();
            foreach (var st in _steps)
            {
                var v = CurrentValue(st.Code);
                Options.Children.Add(new TextBlock { Text = $"{st.Title}: {(v?.ToString() ?? "—")}{(st.Code == M.ColorTempKelvin ? " K" : "")}", Style = (Style)FindResource("Label"), Margin = new Thickness(0, 0, 0, 6) });
            }
            NextBtn.Content = "Salvar";
            DrawScene(Reference);
            RenderDots();
        }

        async void Next_Click(object s, RoutedEventArgs e)
        {
            if (_busy) return;
            if (_index < 0) { await BeginAsync(); return; }
            if (_index >= _steps.Count) { _done = true; Host.Toast("Imagem Personalizada salva no monitor."); Close(); return; }
            Go(_index + 1);
        }

        void Back_Click(object s, RoutedEventArgs e) { if (!_busy && _index > 0) Go(_index - 1); }

        void Cancel_Click(object s, RoutedEventArgs e) => Close();

        protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            if (_done || _original == null) return;
            // undo: restore Personalized values, then the mode the user had before
            try
            {
                await Host.Monitor.ApplyProfileAsync(_personalBefore);
                await Host.Monitor.ApplyProfileAsync(new Profile { Mode = _original.Mode });
                Host.Toast("Assistente cancelado: ajustes anteriores restaurados.");
            }
            catch (Exception ex) { Log.Write("wizard restore: " + ex.Message); }
        }

        void Drag(object s, MouseButtonEventArgs e) { try { DragMove(); } catch { } }

        void RenderDots()
        {
            Dots.Children.Clear();
            for (int i = 0; i < _steps.Count; i++)
                Dots.Children.Add(new Border
                {
                    Width = i == _index ? 26 : 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 6, 0),
                    Background = i <= _index ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255))
                });
        }

        Style OptionStyle()
        {
            var st = new Style(typeof(RadioButton));
            var tpl = new ControlTemplate(typeof(RadioButton));
            var b = new FrameworkElementFactory(typeof(Border), "b");
            b.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            b.SetValue(Border.BackgroundProperty, FindResource("ControlBg"));
            b.SetValue(Border.PaddingProperty, new Thickness(14, 10, 14, 10));
            b.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            b.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            b.AppendChild(cp);
            tpl.VisualTree = b;
            var trig = new Trigger { Property = RadioButton.IsCheckedProperty, Value = true };
            trig.Setters.Add(new Setter(Border.BorderBrushProperty, FindResource("Accent"), "b"));
            trig.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x30, 0x5E, 0xE3, 0xC3)), "b"));
            tpl.Triggers.Add(trig);
            st.Setters.Add(new Setter(TemplateProperty, tpl));
            st.Setters.Add(new Setter(ForegroundProperty, FindResource("TextPrimary")));
            st.Setters.Add(new Setter(FontSizeProperty, 13.5));
            st.Setters.Add(new Setter(CursorProperty, Cursors.Hand));
            return st;
        }

        // ---------- reference images (drawn by Ultragear) ----------
        static double W(Canvas c) => c.ActualWidth > 0 ? c.ActualWidth : 560;
        static double H(Canvas c) => c.ActualHeight > 0 ? c.ActualHeight : 470;

        static void Rect(Canvas c, double x, double y, double w, double h, Color col)
        {
            var r = new Rectangle { Width = Math.Max(0, w), Height = Math.Max(0, h), Fill = new SolidColorBrush(col) };
            Canvas.SetLeft(r, x); Canvas.SetTop(r, y); c.Children.Add(r);
        }

        static void Label(Canvas c, string t, double x, double y, Color col, double size = 12)
        {
            var tb = new TextBlock { Text = t, Foreground = new SolidColorBrush(col), FontSize = size };
            Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y); c.Children.Add(tb);
        }

        void DrawScene(Canvas c)
        {
            c.Children.Clear();
            double w = W(c), h = H(c);
            // sky gradient, sun, hills, a dark cave and a grey ramp: covers highlights, mid-tones and shadows
            var sky = new Rectangle { Width = w, Height = h * 0.62, Fill = new LinearGradientBrush(Color.FromRgb(70, 120, 200), Color.FromRgb(235, 200, 160), 90) };
            c.Children.Add(sky);
            var sun = new Ellipse { Width = 70, Height = 70, Fill = new RadialGradientBrush(Color.FromRgb(255, 250, 230), Color.FromRgb(255, 210, 120)) };
            Canvas.SetLeft(sun, w * 0.72); Canvas.SetTop(sun, h * 0.12); c.Children.Add(sun);
            var hill = new Path { Fill = new SolidColorBrush(Color.FromRgb(46, 110, 60)), Data = Geometry.Parse($"M0,{h * 0.55} Q{w * 0.25},{h * 0.38} {w * 0.5},{h * 0.56} T{w},{h * 0.5} V{h * 0.78} H0 Z") };
            c.Children.Add(hill);
            Rect(c, 0, h * 0.78, w, h * 0.22, Color.FromRgb(12, 12, 14));
            var cave = new Ellipse { Width = w * 0.3, Height = h * 0.16, Fill = new SolidColorBrush(Color.FromRgb(6, 6, 8)) };
            Canvas.SetLeft(cave, w * 0.08); Canvas.SetTop(cave, h * 0.6); c.Children.Add(cave);
            for (int i = 0; i < 6; i++)
            {
                var stone = new Ellipse { Width = 22, Height = 14, Fill = new SolidColorBrush(Color.FromRgb((byte)(10 + i * 4), (byte)(10 + i * 4), (byte)(12 + i * 4))) };
                Canvas.SetLeft(stone, w * 0.12 + i * 26); Canvas.SetTop(stone, h * 0.66); c.Children.Add(stone);
            }
            int steps = 16; double sw = w / steps;
            for (int i = 0; i < steps; i++) { byte v = (byte)(i * 255 / (steps - 1)); Rect(c, i * sw, h * 0.86, sw + 1, h * 0.14, Color.FromRgb(v, v, v)); }
            Label(c, "Ultragear • cena de referência", 12, 10, Colors.White, 12);
        }

        void DrawBlackSteps(Canvas c)
        {
            c.Children.Clear();
            double w = W(c), h = H(c);
            Rect(c, 0, 0, w, h, Colors.Black);
            int n = 12; double size = Math.Min(w / 6.5, h / 3.2);
            for (int i = 0; i < n; i++)
            {
                byte v = (byte)(2 + i * 3);
                double x = 20 + (i % 6) * (size + 6), y = 40 + (i / 6) * (size + 34);
                Rect(c, x, y, size, size, Color.FromRgb(v, v, v));
                Label(c, v.ToString(), x + 4, y + size + 4, Color.FromRgb(90, 90, 90), 11);
            }
            Label(c, "Níveis de preto 2 a 35 (de 255)", 20, 10, Color.FromRgb(120, 120, 120));
        }

        void DrawWhiteSteps(Canvas c)
        {
            c.Children.Clear();
            double w = W(c), h = H(c);
            Rect(c, 0, 0, w, h, Color.FromRgb(255, 255, 255));
            int n = 12; double size = Math.Min(w / 6.5, h / 3.2);
            for (int i = 0; i < n; i++)
            {
                byte v = (byte)(232 + i * 2);
                double x = 20 + (i % 6) * (size + 6), y = 40 + (i / 6) * (size + 34);
                Rect(c, x, y, size, size, Color.FromRgb(v, v, v));
                Label(c, v.ToString(), x + 4, y + size + 4, Color.FromRgb(150, 150, 150), 11);
            }
            Label(c, "Níveis de branco 232 a 254 sobre 255", 20, 10, Color.FromRgb(130, 130, 130));
        }

        void DrawText(Canvas c)
        {
            c.Children.Clear();
            double w = W(c), h = H(c);
            Rect(c, 0, 0, w, h, Color.FromRgb(245, 245, 245));
            for (int i = 0; i < 40; i++)
            {
                var l = new Line { X1 = 20 + i * 4, Y1 = 30, X2 = 20 + i * 4, Y2 = 150, Stroke = Brushes.Black, StrokeThickness = 1 };
                c.Children.Add(l);
            }
            for (int i = 0; i < 30; i++)
            {
                var l = new Line { X1 = 220, Y1 = 30 + i * 4, X2 = w - 20, Y2 = 30 + i * 4, Stroke = Brushes.Black, StrokeThickness = 1 };
                c.Children.Add(l);
            }
            double y = 170;
            foreach (var size in new[] { 9.0, 11, 13, 16, 20 })
            {
                Label(c, "Ultragear — Aa Bb Cc 0123456789 ãçé {}[]", 20, y, Color.FromRgb(20, 20, 20), size);
                y += size * 1.9;
            }
            var circle = new Ellipse { Width = 120, Height = 120, Stroke = Brushes.Black, StrokeThickness = 1 };
            Canvas.SetLeft(circle, w - 150); Canvas.SetTop(circle, h - 150); c.Children.Add(circle);
        }
    }
}
