using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ultragear.Features;

namespace Ultragear.UI
{
    public partial class SplitPage : UserControl
    {
        public SplitPage()
        {
            InitializeComponent();
            BuildTiles();
            Loaded += (s, e) => Refresh();
            Host.TargetsChanged += () => Dispatcher.BeginInvoke(new Action(Refresh));
        }

        static readonly System.Collections.Generic.Dictionary<string, string> Short = new()
        {
            ["Full"] = "Tela cheia", ["Main"] = "Principal + lateral", ["Halves"] = "Lado a lado", ["TopBottom"] = "Cima e baixo",
            ["Left2"] = "2 esq. + 1 dir.", ["Right2"] = "1 esq. + 2 dir.", ["Top2"] = "2 cima + 1 baixo", ["Bottom2"] = "1 cima + 2 baixo",
            ["Thirds"] = "3 colunas", ["Grid4"] = "4 em grade", ["Custom"] = "Personalizado"
        };

        public static Geometry LayoutGeometry(Rect[] zones)
        {
            var g = new GeometryGroup();
            double x0 = 2, y0 = 5, w = 20, h = 14;
            g.Children.Add(new RectangleGeometry(new Rect(x0, y0, w, h), 1.5, 1.5));
            foreach (var z in zones)
                g.Children.Add(new RectangleGeometry(new Rect(x0 + z.X * w + 1.2, y0 + z.Y * h + 1.2, Math.Max(0.5, z.Width * w - 2.4), Math.Max(0.5, z.Height * h - 2.4))));
            return g;
        }

        void BuildTiles()
        {
            LayoutPanel.Children.Clear();
            foreach (var l in SplitLayouts.All)
            {
                var zones = l.Id == "Custom" ? SplitLayouts.Grid(Host.Settings.CustomColumns, Host.Settings.CustomRows) : l.Zones;
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("ModeTile"), GroupName = "split", Content = Short.TryGetValue(l.Id, out var sn) ? sn : l.Name,
                    Tag = l.Id == "Custom" ? FindResource("IconSettings") : LayoutGeometry(zones), ToolTip = l.Name, DataContext = l,
                    IsChecked = l.Id == Host.Settings.SplitLayout
                };
                System.Windows.Automation.AutomationProperties.SetName(rb, "Divisão " + l.Name);
                rb.Click += (s, e) =>
                {
                    Host.Settings.SplitLayout = l.Id; Host.Settings.Save();
                    Refresh();
                    Host.Snap?.Preview(1500);
                };
                LayoutPanel.Children.Add(rb);
            }
        }

        void Refresh()
        {
            HotkeyToggle.IsChecked = Host.Settings.SplitHotkeysEnabled;
            SnapToggle.IsChecked = Host.Settings.SplitDragSnap;
            HotkeyStatus.Text = Host.Hotkeys?.FailedText ?? "";
            MonitorName.Text = Host.Monitor.Target?.Display ?? "Nenhum monitor selecionado (veja Ajustes da tela)";
            CustomPanel.Visibility = Host.Settings.SplitLayout == "Custom" ? Visibility.Visible : Visibility.Collapsed;
            ColText.Text = Host.Settings.CustomColumns.ToString();
            RowText.Text = Host.Settings.CustomRows.ToString();
        }

        void HotkeyToggle_Click(object s, RoutedEventArgs e)
        {
            Host.Settings.SplitHotkeysEnabled = HotkeyToggle.IsChecked == true; Host.Settings.Save();
            Host.Hotkeys.Apply();
            Refresh();
        }

        void SnapToggle_Click(object s, RoutedEventArgs e)
        {
            Host.Settings.SplitDragSnap = SnapToggle.IsChecked == true; Host.Settings.Save();
            Host.Snap.Enabled = Host.Settings.SplitDragSnap;
        }

        void Arrange_Click(object s, RoutedEventArgs e)
        {
            var t = Host.Monitor.Target;
            if (t == null) { Host.Toast("Nenhum monitor selecionado.", true); return; }
            var layout = SplitLayouts.Get(Host.Settings);
            Window.GetWindow(this)?.Hide();
            int n = WindowPlacer.Arrange(t, layout);
            Host.Toast(n == 0 ? "Nenhuma janela para organizar neste monitor." : $"{n} janela(s) organizada(s) em “{layout.Name}”.");
        }

        void Preview_Click(object s, RoutedEventArgs e) => Host.Snap?.Preview(3000);

        void Custom(int dc, int dr)
        {
            Host.Settings.CustomColumns = Math.Clamp(Host.Settings.CustomColumns + dc, 1, 6);
            Host.Settings.CustomRows = Math.Clamp(Host.Settings.CustomRows + dr, 1, 4);
            Host.Settings.Save();
            Refresh();
            Host.Snap?.Preview(1500);
        }

        void ColMinus_Click(object s, RoutedEventArgs e) => Custom(-1, 0);
        void ColPlus_Click(object s, RoutedEventArgs e) => Custom(1, 0);
        void RowMinus_Click(object s, RoutedEventArgs e) => Custom(0, -1);
        void RowPlus_Click(object s, RoutedEventArgs e) => Custom(0, 1);
    }
}
