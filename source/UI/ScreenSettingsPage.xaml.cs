using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ultragear.Core;
using Ultragear.Features;
using M = Ultragear.Core.Lg27GX704A;

namespace Ultragear.UI
{
    public partial class ScreenSettingsPage : UserControl
    {
        readonly SliderRow _brightness = new("Brilho", M.Brightness, 0, 100, 1);
        readonly SliderRow _contrast = new("Contraste", M.Contrast, 0, 100, 1);
        readonly SliderRow _sharpness = new("Nitidez", M.Sharpness, 0, 100, 1);
        readonly SliderRow _blackStab = new("Estabilizador de preto", M.BlackStabilizer, 0, 100, 5, 10);
        readonly SliderRow _kelvin = new("Temperatura (Kelvin)", M.ColorTempKelvin, M.KelvinMin, M.KelvinMax, M.KelvinStep, 500) { Unit = " K" };
        readonly SliderRow _red = new("Vermelho", M.RedGain, 0, 100, 1);
        readonly SliderRow _green = new("Verde", M.GreenGain, 0, 100, 1);
        readonly SliderRow _blue = new("Azul", M.BlueGain, 0, 100, 1);
        readonly SliderRow _volume = new("Volume", M.Volume, 0, 100, 1);
        readonly Dictionary<uint, RadioButton> _modeTiles = new();
        bool _updating;

        public ScreenSettingsPage()
        {
            InitializeComponent();
            SliderHost.Children.Add(_brightness);
            SliderHost.Children.Add(_contrast);
            SliderHost.Children.Add(_sharpness);
            SliderHost.Children.Add(_blackStab);
            KelvinHost.Children.Add(_kelvin);
            RgbHost.Children.Add(_red); RgbHost.Children.Add(_green); RgbHost.Children.Add(_blue);
            SoundHost.Children.Add(_volume);
            _kelvin.Unit = " K";
            foreach (var r in new[] { _brightness, _contrast, _sharpness, _blackStab, _kelvin, _red, _green, _blue, _volume })
                r.Changed += Slider_Changed;

            foreach (var c in M.ColorTemps) ColorTempCombo.Items.Add(c);
            foreach (var o in WindowsDisplay.OrientationNames) OrientationCombo.Items.Add(o);
            BuildModeTiles();
            SetPictureExpanded(Host.Settings.PictureExpanded);

            Host.TargetsChanged += () => Dispatcher.BeginInvoke(new Action(FillMonitors));
            Host.Monitor.StateChanged += s => Dispatcher.BeginInvoke(new Action(() => ApplyState(s)));
            Loaded += async (s, e) => { FillMonitors(); UpdatePresetSummary(); FillProfiles(); if (!_loadedOnce) { _loadedOnce = true; await ReloadAsync(); } };

            // Re-read the monitor every 10 s while the window is visible, so changes made with the
            // joystick/OSD or by another program show up here.
            _poll.Tick += async (s, e) =>
            {
                var w = Window.GetWindow(this);
                if (_polling || w == null || !w.IsVisible || !Host.Monitor.IsConnected || IsUserInteracting()) return;
                _polling = true;
                try
                {
                    var mode = (await Host.Monitor.GetRawAsync(M.PictureMode)).Current;
                    var bri = (await Host.Monitor.GetRawAsync(M.Brightness)).Current;
                    if (mode != Host.Monitor.State.Mode || bri != Host.Monitor.State.Brightness) await Host.Monitor.ReadAllAsync();
                }
                catch { /* monitor off / busy: the banner is handled by ReloadAsync on demand */ }
                finally { _polling = false; }
            };
            _poll.Start();
        }
        bool _loadedOnce, _polling;
        readonly System.Windows.Threading.DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(10) };

        bool IsUserInteracting() =>
            Mouse.LeftButton == MouseButtonState.Pressed || ColorTempCombo.IsDropDownOpen || VrrCombo.IsDropDownOpen;

        public void BuildModeTiles()
        {
            ModesPanel.Children.Clear(); _modeTiles.Clear();
            foreach (var m in M.AllModes)
            {
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("ModeTile"), Content = m.Name, GroupName = "mode",
                    Tag = FindResource(m.Icon), ToolTip = m.Description, DataContext = m
                };
                rb.Click += Mode_Click;
                System.Windows.Automation.AutomationProperties.SetName(rb, "Modo de imagem " + m.Name);
                ModesPanel.Children.Add(rb);
                _modeTiles[m.Code] = rb;
            }
        }

        void FillMonitors()
        {
            _updating = true;
            MonitorCombo.Items.Clear();
            foreach (var t in Host.Targets) MonitorCombo.Items.Add(t);
            MonitorCombo.SelectedItem = Host.Targets.FirstOrDefault(t => t.DevicePath == Host.Monitor.Target?.DevicePath);
            _updating = false;
            var lg = Host.Monitor.Target;
            if (lg == null)
                ShowBanner("O monitor LG 27GX704A não foi encontrado entre as telas ativas. Verifique se ele está ligado, conectado e definido como tela ativa no Windows.");
            else if (!lg.Is27GX704A)
                ShowBanner("A tela selecionada não é um LG 27GX704A. Os comandos foram medidos para esse modelo; em outras telas podem não ter efeito.", warn: true);
            else HideBanner();
        }

        void ShowBanner(string text, bool warn = false)
        {
            BannerText.Text = text;
            Banner.Background = new SolidColorBrush(warn ? Color.FromArgb(0x33, 0xFF, 0xC8, 0x6B) : Color.FromArgb(0x33, 0xFF, 0x7A, 0x8A));
            Banner.Visibility = Visibility.Visible;
        }
        void HideBanner() => Banner.Visibility = Visibility.Collapsed;

        public async Task ReloadAsync()
        {
            FillMonitors();
            if (!Host.Monitor.IsConnected) { SetControlsEnabled(false); StatusText.Text = "Sem conexão com o monitor."; return; }
            StatusText.Text = "Lendo o estado atual do monitor…";
            try
            {
                var s = await Host.Monitor.ReadAllAsync();
                SetControlsEnabled(true);
                ApplyState(s);
                HideBannerIfOk();
            }
            catch (Exception ex)
            {
                SetControlsEnabled(false);
                StatusText.Text = "Sem resposta DDC/CI.";
                ShowBanner("O monitor não respondeu via DDC/CI: " + ex.Message + "\nConfira se ele está ligado nesta entrada e se o DDC/CI está ativo.");
            }
            UpdateOrientation();
        }

        void HideBannerIfOk() { if (Host.Monitor.Target?.Is27GX704A == true) HideBanner(); }

        void SetControlsEnabled(bool on)
        {
            PicturePanel.IsEnabled = on; SoundHost.IsEnabled = on; MuteToggle.IsEnabled = on; ProfileCombo.IsEnabled = on; EnergyCombo.IsEnabled = on;
        }

        public void ApplyState(MonitorState s)
        {
            _updating = true;
            try
            {
                foreach (var mt in _modeTiles.Values) mt.IsChecked = false;
                var mode = s.Mode is uint mc ? M.ModeFor(mc) : null;
                if (s.Mode is uint code && _modeTiles.TryGetValue(code, out var tile)) tile.IsChecked = true;
                ModeHint.Text = mode != null ? mode.Description
                    : s.Mode is uint u ? $"O monitor está num modo que o Ultragear não reconhece (código {u}). Os ajustes abaixo continuam sendo lidos do monitor." : "";

                _brightness.SetFromMonitor(s.Brightness);
                _contrast.SetFromMonitor(s.Contrast);
                _sharpness.SetFromMonitor(s.Sharpness);
                _blackStab.SetFromMonitor(s.BlackStabilizer);
                _volume.SetFromMonitor(s.Volume);
                MuteToggle.IsChecked = s.Mute == 1;
                MuteToggle.IsEnabled = s.Mute != null;

                EnergyCombo.SelectedIndex = s.EnergySaving is uint es && es <= 2 ? (int)es : -1;
                EnergyCombo.IsEnabled = s.EnergySaving is uint es2 && es2 <= 2;
                VrrCombo.SelectedIndex = s.AdaptiveSync == 1 ? 1 : s.AdaptiveSync == 0 ? 0 : -1;
                VrrCombo.IsEnabled = s.AdaptiveSync != null;

                var ct = s.ColorPreset is uint cp ? M.ColorTempFor(cp) : null;
                ColorTempCombo.SelectedItem = ct;
                ColorTempCombo.IsEnabled = s.ColorPreset != null && ct != null;
                bool manual = ct?.Value == M.ManualPresetValue, user = ct?.Value == 11;
                KelvinHost.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
                RgbHost.Visibility = user ? Visibility.Visible : Visibility.Collapsed;
                _kelvin.SetFromMonitor(s.Kelvin);
                _red.SetFromMonitor(s.Red); _green.SetFromMonitor(s.Green); _blue.SetFromMonitor(s.Blue);

                var t = Host.Monitor.Target;
                StatusText.Text = t == null ? "" :
                    $"Conectado via {t.ConnectionName} • DDC/CI ok • firmware {s.FirmwareText} • lido às {s.ReadAt:HH:mm:ss}";
            }
            finally { _updating = false; }
        }

        void UpdateOrientation()
        {
            _updating = true;
            if (Host.Monitor.Target != null) OrientationCombo.SelectedIndex = WindowsDisplay.GetOrientation(Host.Monitor.Target);
            _updating = false;
        }

        // ---------- events ----------
        async void Mode_Click(object sender, RoutedEventArgs e)
        {
            var m = (PictureModeInfo)((FrameworkElement)sender).DataContext;
            var r = await Host.Monitor.SetPictureModeAsync(m.Code);
            if (r.Ok) Host.Toast("Modo de imagem: " + m.Name);
            else if (r.Outcome == WriteOutcome.Rejected) Host.Toast($"O monitor recusou o modo {m.Name}. Alguns modos só existem em SDR ou dependem do sinal de entrada.", true);
            ApplyState(Host.Monitor.State);
        }

        async void Slider_Changed(SliderRow row, int value)
        {
            if (_updating) return;
            var r = await Host.Monitor.SetAsync(row.Code, (uint)value, coalesce: true);
            if (!row.IsDragging && r.Actual is uint a && a != value && r.Outcome != WriteOutcome.Adjusted) row.SetFromMonitor(a);
        }

        async void Vrr_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_updating || VrrCombo.SelectedIndex < 0) return;
            bool on = VrrCombo.SelectedIndex == 1;
            if (Host.Monitor.State.AdaptiveSync == (on ? 1u : 0u)) return;
            var r = await Host.Monitor.SetAdaptiveSyncAsync(on);
            if (r.Ok) Host.Toast("Adaptive-Sync/VRR " + (on ? "ligado" : "desligado") + ". A tela pode piscar durante a ressincronização.");
            ApplyState(Host.Monitor.State);
        }

        async void ColorTemp_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_updating || ColorTempCombo.SelectedItem is not ColorTempOption o) return;
            var r = await Host.Monitor.SetColorPresetAsync(o.Value);
            if (!r.Ok && r.Outcome == WriteOutcome.Rejected) Host.Toast("A temperatura de cor não pode ser alterada no modo de imagem atual.", true);
            ApplyState(Host.Monitor.State);
        }

        async void Energy_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_updating || EnergyCombo.SelectedIndex < 0) return;
            var r = await Host.Monitor.SetAsync(M.SmartEnergySaving, (uint)EnergyCombo.SelectedIndex);
            if (r.Ok) Host.Toast("SMART ENERGY SAVING: " + ((ComboBoxItem)EnergyCombo.SelectedItem).Content);
            ApplyState(Host.Monitor.State);
        }

        async void Mute_Click(object s, RoutedEventArgs e)
        {
            var r = await Host.Monitor.SetAsync(M.AudioMute, MuteToggle.IsChecked == true ? 1u : 2u);
            ApplyState(Host.Monitor.State);
        }

        async void Orientation_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_updating || Host.Monitor.Target == null || OrientationCombo.SelectedIndex < 0) return;
            int prev = WindowsDisplay.GetOrientation(Host.Monitor.Target);
            int next = OrientationCombo.SelectedIndex;
            if (prev == next) return;
            var err = WindowsDisplay.SetOrientation(Host.Monitor.Target, next);
            if (err != null) { Host.Toast(err, true); UpdateOrientation(); return; }
            // same safety net as Windows: revert automatically unless confirmed
            bool keep = await Dialogs.ConfirmWithTimeout(Window.GetWindow(this), "Manter esta orientação?",
                "A orientação da tela foi alterada. Ela será revertida em 15 segundos se você não confirmar.", 15);
            if (!keep) { Host.RefreshTargets(); WindowsDisplay.SetOrientation(Host.Monitor.Target, prev); }
            await Task.Delay(800);
            Host.RefreshTargets();
            UpdateOrientation();
        }

        async void Refresh_Click(object s, RoutedEventArgs e) => await ReloadAsync();

        async void Retry_Click(object s, RoutedEventArgs e) { Host.RefreshTargets(); await ReloadAsync(); }

        async void MonitorCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_updating || MonitorCombo.SelectedItem is not MonitorTarget t) return;
            Host.Select(t);
            await ReloadAsync();
        }

        void PictureHeader_Click(object s, MouseButtonEventArgs e)
        {
            Host.Settings.PictureExpanded = PicturePanel.Visibility != Visibility.Visible;
            Host.Settings.Save();
            SetPictureExpanded(Host.Settings.PictureExpanded);
        }

        void SetPictureExpanded(bool on)
        {
            PicturePanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            ChevronRotate.Angle = on ? 180 : 0;
        }

        async void Wizard_Click(object s, RoutedEventArgs e)
        {
            if (!Host.Monitor.IsConnected) return;
            var w = new WizardWindow { Owner = Window.GetWindow(this) };
            w.ShowDialog();
            await ReloadAsync();
        }

        // ---------- My Display Preset ----------
        void PresetToggle_Click(object s, RoutedEventArgs e)
        {
            Host.Settings.PresetsEnabled = PresetToggle.IsChecked == true;
            Host.Settings.Save();
            if (Host.Settings.PresetsEnabled) Host.Presets.Start(); else Host.Presets.Stop();
            UpdatePresetSummary();
        }

        void PresetEdit_Click(object s, RoutedEventArgs e)
        {
            var w = new PresetEditorWindow { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() == true) { Host.Settings.Save(); if (Host.Settings.PresetsEnabled) { Host.Presets.Stop(); Host.Presets.Start(); } }
            UpdatePresetSummary();
        }

        void UpdatePresetSummary()
        {
            PresetToggle.IsChecked = Host.Settings.PresetsEnabled;
            int n = Host.Settings.Presets.Count;
            PresetSummary.Text = n == 0 ? "Nenhum aplicativo configurado." : n == 1 ? "1 aplicativo configurado." : $"{n} aplicativos configurados.";
        }

        // ---------- profiles ----------
        void FillProfiles()
        {
            ProfileCombo.Items.Clear();
            foreach (var p in Host.Settings.Profiles) ProfileCombo.Items.Add(p);
            if (ProfileCombo.Items.Count > 0) ProfileCombo.SelectedIndex = 0;
        }

        async void ProfileApply_Click(object s, RoutedEventArgs e)
        {
            if (ProfileCombo.SelectedItem is not Profile p) { Host.Toast("Selecione um perfil salvo."); return; }
            var problems = await Host.Monitor.ApplyProfileAsync(p);
            Host.Toast(problems.Count == 0 ? $"Perfil “{p.Name}” aplicado e conferido no monitor." : "Perfil aplicado com avisos: " + string.Join("; ", problems), problems.Count > 0);
        }

        void ProfileSave_Click(object s, RoutedEventArgs e)
        {
            if (Host.Monitor.State.Mode == null) { Host.Toast("Leia o estado do monitor antes de salvar um perfil.", true); return; }
            var mode = M.ModeFor(Host.Monitor.State.Mode.Value)?.Name ?? "Perfil";
            var name = Dialogs.Prompt(Window.GetWindow(this), "Salvar perfil", "Nome do perfil:", $"{mode} {DateTime.Now:dd/MM HH:mm}");
            if (string.IsNullOrWhiteSpace(name)) return;
            Host.Settings.Profiles.RemoveAll(x => x.Name == name);
            Host.Settings.Profiles.Add(Host.Monitor.CaptureProfile(name.Trim()));
            Host.Settings.Save();
            FillProfiles();
            ProfileCombo.SelectedItem = Host.Settings.Profiles.Last();
            Host.Toast("Perfil salvo.");
        }

        void ProfileDelete_Click(object s, RoutedEventArgs e)
        {
            if (ProfileCombo.SelectedItem is not Profile p) return;
            if (!Dialogs.Confirm(Window.GetWindow(this), "Excluir perfil", $"Excluir o perfil “{p.Name}”?")) return;
            Host.Settings.Profiles.Remove(p);
            Host.Settings.Save();
            FillProfiles();
        }
    }
}
