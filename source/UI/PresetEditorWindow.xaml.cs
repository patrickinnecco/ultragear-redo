using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ultragear.Core;
using Ultragear.Features;

namespace Ultragear.UI
{
    public partial class PresetEditorWindow : Window
    {
        readonly List<AppPreset> _work;
        const string NoChange = "Não alterar";

        public PresetEditorWindow()
        {
            InitializeComponent();
            _work = Host.Settings.Presets.Select(p => new AppPreset { ExePath = p.ExePath, DisplayName = p.DisplayName, ModeCode = p.ModeCode }).ToList();
            DefaultCombo.Items.Add(NoChange);
            foreach (var m in Lg27GX704A.AllModes) DefaultCombo.Items.Add(m);
            DefaultCombo.SelectedItem = Host.Settings.PresetDefaultMode is int d ? Lg27GX704A.ModeFor((uint)d) : NoChange;
            if (DefaultCombo.SelectedItem == null) DefaultCombo.SelectedIndex = 0;
            Render();
        }

        void Render()
        {
            Rows.Children.Clear();
            if (_work.Count == 0)
                Rows.Children.Add(new TextBlock { Text = "Nenhum aplicativo adicionado.", Style = (Style)FindResource("Hint"), Margin = new Thickness(2, 6, 0, 6) });
            foreach (var p in _work)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                var name = new TextBlock { Text = p.DisplayName, Style = (Style)FindResource("Label"), ToolTip = p.ExePath, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
                g.Children.Add(name);
                var cb = new ComboBox { MinWidth = 0 };
                foreach (var m in Lg27GX704A.AllModes) cb.Items.Add(m);
                cb.SelectedItem = Lg27GX704A.ModeFor((uint)p.ModeCode);
                cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is PictureModeInfo mi) p.ModeCode = (int)mi.Code; };
                Grid.SetColumn(cb, 1); g.Children.Add(cb);
                var del = new Button { Style = (Style)FindResource("IconButton"), ToolTip = "Remover", Content = new System.Windows.Shapes.Path { Data = (System.Windows.Media.Geometry)FindResource("IconTrash"), Style = (Style)FindResource("Icon"), Width = 16, Height = 16 } };
                del.Click += (s, e) => { _work.Remove(p); Render(); };
                Grid.SetColumn(del, 2); g.Children.Add(del);
                Rows.Children.Add(g);
            }
        }

        void Add(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (_work.Count >= 20) { Host.Toast("Limite de 20 aplicativos.", true); return; }
            if (_work.Any(x => string.Equals(x.ExePath, path, StringComparison.OrdinalIgnoreCase))) return;
            string display;
            try { display = FileVersionInfo.GetVersionInfo(path).FileDescription; } catch { display = null; }
            if (string.IsNullOrWhiteSpace(display)) display = Path.GetFileNameWithoutExtension(path);
            _work.Add(new AppPreset { ExePath = path, DisplayName = display, ModeCode = (int)(Host.Monitor.State.Mode ?? Lg27GX704A.Modes[1].Code) });
            Render();
        }

        void AddRunning_Click(object s, RoutedEventArgs e)
        {
            RunningCombo.Items.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Native.NativeMethods.EnumWindows((h, l) =>
            {
                if (WindowPlacer.IsArrangeable(h))
                {
                    var p = WindowsDisplay.ProcessPathOf(h);
                    if (p != null && seen.Add(p) && !string.Equals(p, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                        RunningCombo.Items.Add(new ComboBoxItem { Content = Path.GetFileNameWithoutExtension(p) + "  —  " + p, Tag = p });
                }
                return true;
            }, IntPtr.Zero);
            RunningCombo.Visibility = Visibility.Visible;
            RunningCombo.IsDropDownOpen = RunningCombo.Items.Count > 0;
        }

        void Running_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (RunningCombo.SelectedItem is ComboBoxItem i) { Add((string)i.Tag); RunningCombo.Visibility = Visibility.Collapsed; }
        }

        void AddExe_Click(object s, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Aplicativos (*.exe)|*.exe", Title = "Escolha o aplicativo" };
            if (d.ShowDialog(this) == true) Add(d.FileName);
        }

        void Apply_Click(object s, RoutedEventArgs e)
        {
            Host.Settings.Presets = _work;
            Host.Settings.PresetDefaultMode = DefaultCombo.SelectedItem is PictureModeInfo m ? (int)m.Code : null;
            DialogResult = true;
        }

        void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;
        void Drag(object s, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    }
}
