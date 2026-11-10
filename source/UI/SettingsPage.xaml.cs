using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Ultragear.Core;
using Ultragear.Features;

namespace Ultragear.UI
{
    public partial class SettingsPage : UserControl
    {
        public SettingsPage()
        {
            InitializeComponent();
            Loaded += async (s, e) =>
            {
                StartupToggle.IsChecked = WindowsDisplay.IsStartupEnabled();
                MinToggle.IsChecked = Host.Settings.StartMinimized;
                TrayToggle.IsChecked = Host.Settings.CloseToTray;
                AboutText.Text = $"Ultragear {typeof(SettingsPage).Assembly.GetName().Version?.ToString(3)} — aplicativo independente para o LG 27GX704A. " +
                                 "Comunicação pelo padrão DDC/CI (MCCS) via API de Configuração de Monitor do Windows. Não é afiliado à LG Electronics.\n" +
                                 "Configurações: " + Log.Folder;
                await FillInfo();
            };
        }

        async System.Threading.Tasks.Task FillInfo()
        {
            InfoGrid.Children.Clear(); InfoGrid.RowDefinitions.Clear();
            var t = Host.Monitor.Target;
            var st = Host.Monitor.State;
            if (t != null && st.Mode == null) { try { st = await Host.Monitor.ReadAllAsync(); } catch { } }
            void Row(string k, string v)
            {
                int r = InfoGrid.RowDefinitions.Count;
                InfoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var a = new TextBlock { Text = k, Style = (Style)FindResource("Hint"), FontSize = 12.5, Margin = new Thickness(0, 0, 0, 6) };
                var b = new TextBlock { Text = v ?? "—", Style = (Style)FindResource("Label"), FontSize = 12.5, Margin = new Thickness(0, 0, 0, 6) };
                Grid.SetRow(a, r); Grid.SetRow(b, r); Grid.SetColumn(b, 1);
                InfoGrid.Children.Add(a); InfoGrid.Children.Add(b);
            }
            if (t == null) { Row("Monitor", "Nenhum monitor selecionado"); return; }
            Row("Monitor", t.FriendlyName + (t.Is27GX704A ? " — LG 27GX704A" : ""));
            Row("ID EDID", t.PnpId);
            Row("Nº de série", t.Serial);
            Row("Fabricação", t.ManufactureYear > 0 ? t.ManufactureYear.ToString() : null);
            Row("Conexão", t.ConnectionName + "  (" + t.GdiDeviceName + ")");
            Row("Modo de vídeo", WindowsDisplay.ModeText(t));
            Row("HDR", WindowsDisplay.HdrText(t));
            Row("Firmware", st.FirmwareText);
            Row("Versão MCCS", st.VcpVersionText);
            Row("Tempo de uso", st.UsageTime is uint u ? $"{u} h (contador do monitor, VCP C0)" : null);
            Row("Modo de imagem", st.Mode is uint m ? (Lg27GX704A.ModeFor(m)?.Name ?? "código " + m) : null);
        }

        async void Info_Click(object s, RoutedEventArgs e) { try { await Host.Monitor.ReadAllAsync(); } catch { } await FillInfo(); }

        void Startup_Click(object s, RoutedEventArgs e)
        {
            try { WindowsDisplay.SetStartup(StartupToggle.IsChecked == true); Host.Settings.StartWithWindows = StartupToggle.IsChecked == true; Host.Settings.Save(); }
            catch (Exception ex) { Host.Toast("Não foi possível alterar a inicialização: " + ex.Message, true); }
            StartupToggle.IsChecked = WindowsDisplay.IsStartupEnabled();
        }

        void Min_Click(object s, RoutedEventArgs e) { Host.Settings.StartMinimized = MinToggle.IsChecked == true; Host.Settings.Save(); }
        void Tray_Click(object s, RoutedEventArgs e) { Host.Settings.CloseToTray = TrayToggle.IsChecked == true; Host.Settings.Save(); }

        async void Caps_Click(object s, RoutedEventArgs e)
        {
            if (!Host.Monitor.IsConnected) return;
            string text;
            try { text = await Host.Monitor.GetCapabilitiesAsync(); }
            catch (Exception ex)
            {
                text = "O monitor não devolveu a string de capacidades: " + ex.Message +
                       "\n\nNo 27GX704A isso é esperado: a resposta de capacidades chega com checksum inválido. Os comandos individuais funcionam normalmente; o Ultragear usa um mapa de comandos medido no próprio monitor.";
            }
            Dialogs.Info(Window.GetWindow(this), "Capacidades DDC/CI", text);
        }

        void Logs_Click(object s, RoutedEventArgs e)
        {
            System.IO.Directory.CreateDirectory(Log.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Log.Folder + "\"") { UseShellExecute = true });
        }

        void Export_Click(object s, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.SaveFileDialog { Filter = "Perfis Ultragear (*.json)|*.json", FileName = "ultragear-perfis.json" };
            if (d.ShowDialog() != true) return;
            try { Host.Settings.ExportProfiles(d.FileName); Host.Toast("Perfis exportados."); }
            catch (Exception ex) { Host.Toast(ex.Message, true); }
        }

        void Import_Click(object s, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Perfis Ultragear (*.json)|*.json" };
            if (d.ShowDialog() != true) return;
            try { int n = Host.Settings.ImportProfiles(d.FileName); Host.Settings.Save(); Host.Toast($"{n} perfil(is) importado(s)."); }
            catch (Exception ex) { Host.Toast("Arquivo de perfis inválido: " + ex.Message, true); }
        }

        void Exit_Click(object s, RoutedEventArgs e) => Host.Main.ExitApp();
    }
}
