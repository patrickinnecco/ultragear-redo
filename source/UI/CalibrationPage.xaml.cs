using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Ultragear.Features;

namespace Ultragear.UI
{
    public partial class CalibrationPage : UserControl
    {
        public CalibrationPage()
        {
            InitializeComponent();
            Loaded += (s, e) => { DetectSensor(); LoadIcc(); };
        }

        void DetectSensor()
        {
            var found = SensorDetector.Detect();
            SensorText.Text = found.Count == 0 ? "Nenhum sensor de cor conectado." : "Detectado: " + string.Join("\n", found);
        }

        void Sensor_Click(object s, RoutedEventArgs e) => DetectSensor();

        async void CalMode_Click(object s, RoutedEventArgs e)
        {
            if (!Host.Monitor.IsConnected) { Host.Toast("Monitor não conectado.", true); return; }
            uint code = uint.Parse((string)((Button)s).Tag);
            var r = await Host.Monitor.SetPictureModeAsync(code);
            CalModeText.Text = r.Ok ? $"Modo {(code == 18 ? "Calibração 1" : "Calibração 2")} ativo (confirmado pelo monitor)."
                                    : "O monitor não aceitou o modo de calibração (indisponível em HDR).";
            Host.Main.RefreshFromState();
        }

        void LoadIcc()
        {
            var t = Host.Monitor.Target;
            IccCombo.Items.Clear();
            foreach (var p in ColorProfiles.Installed()) IccCombo.Items.Add(p);
            var cur = t == null ? null : ColorProfiles.GetDefault(t);
            IccCurrent.Text = t == null ? "Nenhum monitor selecionado." : "Perfil padrão atual: " + (cur ?? "nenhum (sRGB do sistema)");
            if (cur != null) IccCombo.SelectedItem = IccCombo.Items.Cast<string>().FirstOrDefault(x => string.Equals(x, Path.GetFileName(cur), StringComparison.OrdinalIgnoreCase));
        }

        void IccSet_Click(object s, RoutedEventArgs e)
        {
            var t = Host.Monitor.Target;
            if (t == null || IccCombo.SelectedItem is not string p) { Host.Toast("Escolha um perfil.", true); return; }
            var err = ColorProfiles.SetDefault(t, p);
            var now = ColorProfiles.GetDefault(t);
            if (err == null && now != null && Path.GetFileName(now).Equals(p, StringComparison.OrdinalIgnoreCase)) Host.Toast("Perfil definido e confirmado: " + p);
            else Host.Toast(err ?? "O Windows não confirmou a associação do perfil.", true);
            LoadIcc();
        }

        void IccImport_Click(object s, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Perfis ICC (*.icc;*.icm)|*.icc;*.icm", Title = "Importar perfil de cor" };
            if (d.ShowDialog() != true) return;
            var err = ColorProfiles.Install(d.FileName);
            Host.Toast(err ?? "Perfil instalado: " + Path.GetFileName(d.FileName), err != null);
            LoadIcc();
            IccCombo.SelectedItem = Path.GetFileName(d.FileName);
        }

        void Pattern_Click(object s, RoutedEventArgs e)
        {
            var t = Host.Monitor.Target;
            if (t == null) { Host.Toast("Nenhum monitor selecionado.", true); return; }
            new PatternWindow(t, int.Parse((string)((Button)s).Tag)).Show();
        }
    }
}
