using System;
using System.Linq;
using System.Windows;
using Ultragear.Core;
using Ultragear.Features;
using WF = System.Windows.Forms;

namespace Ultragear.UI
{
    /// <summary>Notification-area icon with quick controls (opens with a click, menu with right click).</summary>
    public sealed class TrayIcon : IDisposable
    {
        readonly WF.NotifyIcon _icon;

        public TrayIcon()
        {
            System.Drawing.Icon ico;
            try
            {
                using var s = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ultragear.ico")).Stream;
                ico = new System.Drawing.Icon(s);
            }
            catch { ico = System.Drawing.SystemIcons.Application; }
            _icon = new WF.NotifyIcon { Icon = ico, Text = "Ultragear", Visible = true, ContextMenuStrip = new WF.ContextMenuStrip() };
            _icon.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) Host.Main.ShowFromTray(); };
            _icon.ContextMenuStrip.Opening += (s, e) => BuildMenu();
        }

        void BuildMenu()
        {
            var m = _icon.ContextMenuStrip;
            m.Items.Clear();
            m.Items.Add("Abrir Ultragear", null, (s, e) => Host.Main.ShowFromTray());
            m.Items.Add(new WF.ToolStripSeparator());

            var modes = new WF.ToolStripMenuItem("Modo de imagem") { Enabled = Host.Monitor.IsConnected };
            foreach (var mode in Lg27GX704A.AllModes)
            {
                var item = new WF.ToolStripMenuItem(mode.Name) { Checked = Host.Monitor.State.Mode == mode.Code };
                var code = mode.Code;
                item.Click += async (s, e) => { var r = await Host.Monitor.SetPictureModeAsync(code); if (r.Ok) Host.Main.RefreshFromState(); };
                modes.DropDownItems.Add(item);
            }
            m.Items.Add(modes);

            var bright = new WF.ToolStripMenuItem("Brilho") { Enabled = Host.Monitor.IsConnected };
            foreach (var v in new[] { 0, 25, 50, 75, 100 })
            {
                var item = new WF.ToolStripMenuItem(v + "%") { Checked = Host.Monitor.State.Brightness == v };
                item.Click += async (s, e) => await Host.Monitor.SetAsync(Lg27GX704A.Brightness, (uint)v);
                bright.DropDownItems.Add(item);
            }
            m.Items.Add(bright);

            var profiles = new WF.ToolStripMenuItem("Perfis") { Enabled = Host.Monitor.IsConnected && Host.Settings.Profiles.Count > 0 };
            foreach (var p in Host.Settings.Profiles.ToList())
                profiles.DropDownItems.Add(p.Name, null, async (s, e) => { await Host.Monitor.ApplyProfileAsync(p); Host.Toast("Perfil aplicado: " + p.Name); });
            m.Items.Add(profiles);

            m.Items.Add("Organizar janelas", null, (s, e) =>
            {
                if (Host.Monitor.Target != null) WindowPlacer.Arrange(Host.Monitor.Target, SplitLayouts.Get(Host.Settings));
            });
            var preset = new WF.ToolStripMenuItem("Predefinição por aplicativo") { Checked = Host.Settings.PresetsEnabled };
            preset.Click += (s, e) =>
            {
                Host.Settings.PresetsEnabled = !Host.Settings.PresetsEnabled; Host.Settings.Save();
                if (Host.Settings.PresetsEnabled) Host.Presets.Start(); else Host.Presets.Stop();
            };
            m.Items.Add(preset);
            m.Items.Add(new WF.ToolStripSeparator());
            m.Items.Add("Sair", null, (s, e) => Host.Main.ExitApp());
        }

        public void Balloon(string text)
        {
            try { _icon.ShowBalloonTip(3000, "Ultragear", text, WF.ToolTipIcon.Info); } catch { }
        }

        public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
    }
}
