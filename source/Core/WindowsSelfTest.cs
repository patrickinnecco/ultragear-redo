using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Ultragear.Features;
using static Ultragear.Native.NativeMethods;

namespace Ultragear.Core
{
    /// <summary>
    /// Tests of the Windows-side features: window placement into zones (own test window only),
    /// per-application picture mode (Notepad) and ICC association (reverted). Usage: Ultragear.exe --selftest-windows
    /// </summary>
    public static class WindowsSelfTest
    {
        public static async Task<string> RunAsync()
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            void Check(string n, bool ok, string d) { if (ok) pass++; else fail++; sb.AppendLine($"[{(ok ? "OK " : "FALHA")}] {n}: {d}"); Log.Write("wintest: " + n + " " + ok + " " + d); }
            sb.AppendLine($"Ultragear — autoteste de recursos do Windows  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            var lg = MonitorLocator.FindAll().FirstOrDefault(t => t.Is27GX704A);
            if (lg == null) { sb.AppendLine("LG 27GX704A não está ativo."); return Save(sb); }

            // ---- 1. zones: place an Ultragear-owned test window into every zone of every layout ----
            var w = new Window { Title = "Ultragear — janela de teste", Width = 400, Height = 300, ShowActivated = false, Topmost = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
            w.Show();
            var h = new WindowInteropHelper(w).Handle;
            int zonesOk = 0, zonesTotal = 0; string firstBad = null;
            foreach (var layout in SplitLayouts.All)
            {
                var zones = layout.Id == "Custom" ? SplitLayouts.Grid(3, 2) : layout.Zones;
                foreach (var z in zones)
                {
                    var target = WindowPlacer.ZoneToPixels(lg.WorkArea, z);
                    WindowPlacer.Place(h, target);
                    await Task.Delay(60);
                    DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT fr, System.Runtime.InteropServices.Marshal.SizeOf<RECT>());
                    bool ok = Math.Abs(fr.Left - target.Left) <= 2 && Math.Abs(fr.Top - target.Top) <= 2 && Math.Abs(fr.Right - target.Right) <= 2 && Math.Abs(fr.Bottom - target.Bottom) <= 2;
                    zonesTotal++; if (ok) zonesOk++; else firstBad ??= $"{layout.Name}: alvo {target.Left},{target.Top}-{target.Right},{target.Bottom} obtido {fr.Left},{fr.Top}-{fr.Right},{fr.Bottom}";
                }
            }
            Check("Divisão de tela: encaixe nas zonas", zonesOk == zonesTotal, $"{zonesOk}/{zonesTotal} zonas exatas (±2 px)" + (firstBad != null ? "; " + firstBad : ""));
            int idx = WindowPlacer.ZoneIndexOf(lg, SplitLayouts.All.First(l => l.Id == "Grid4"), h);
            Check("Divisão de tela: detecção da zona atual", idx >= 0, "zona " + idx);
            w.Close();

            // ---- 2. per-application preset with Notepad ----
            using (var svc = new MonitorService())
            {
                svc.Connect(lg);
                var s0 = (await svc.ReadAllAsync()).Clone();
                var settings = new AppSettings { PresetsEnabled = true, PresetDefaultMode = (int?)s0.Mode };
                string testExe = Path.Combine(Environment.SystemDirectory, "charmap.exe");
                Process np = null;
                try
                {
                    settings.Presets.Add(new AppPreset { ExePath = testExe, DisplayName = "Mapa de Caracteres", ModeCode = 30 });
                    var watcher = new AppPresetWatcher(settings, svc);
                    watcher.Start();
                    np = Process.Start(testExe);
                    for (int i = 0; i < 30 && np.MainWindowHandle == IntPtr.Zero; i++) { await Task.Delay(100); np.Refresh(); }
                    keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);   // lets SetForegroundWindow succeed
                    SetForegroundWindow(np.MainWindowHandle);
                    await Task.Delay(2500);
                    var fg = WindowsDisplay.ProcessPathOf(GetForegroundWindow());
                    var m1 = (await svc.GetRawAsync(Lg27GX704A.PictureMode)).Current;
                    Check("Predefinição: Mapa de Caracteres em primeiro plano → FPS", m1 == 30, $"modo lido {m1} (em foco: {Path.GetFileName(fg ?? "?")})");
                    try { np.Kill(); } catch { }
                    await Task.Delay(3000);
                    var m2 = (await svc.GetRawAsync(Lg27GX704A.PictureMode)).Current;
                    Check("Predefinição: demais aplicativos → modo padrão", m2 == s0.Mode, $"modo lido {m2}, esperado {s0.Mode}");
                    watcher.Dispose();
                }
                finally
                {
                    try { if (np != null && !np.HasExited) np.Kill(); } catch { }
                    if (s0.Mode is uint mm) await svc.SetPictureModeAsync(mm);
                }
            }

            // ---- 3. ICC association (reverted) ----
            try
            {
                string before = ColorProfiles.GetDefault(lg);
                string probe = ColorProfiles.Installed().FirstOrDefault(p => p.StartsWith("sRGB", StringComparison.OrdinalIgnoreCase)) ?? ColorProfiles.Installed().FirstOrDefault();
                if (probe == null) Check("Perfil ICC", false, "nenhum perfil instalado para testar");
                else
                {
                    var err = ColorProfiles.SetDefault(lg, probe);
                    var now = ColorProfiles.GetDefault(lg);
                    Check("Perfil ICC: definir padrão do monitor", err == null && now != null && Path.GetFileName(now).Equals(probe, StringComparison.OrdinalIgnoreCase), $"definido {probe}, lido {now ?? "nenhum"} {err}");
                    // revert
                    if (before != null) ColorProfiles.SetDefault(lg, Path.GetFileName(before));
                    else ColorProfileRemoveDisplayAssociation(WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER, probe, lg.AdapterId, lg.SourceId, false);
                    var after = ColorProfiles.GetDefault(lg);
                    Check("Perfil ICC: reverter", string.Equals(Path.GetFileName(after ?? ""), Path.GetFileName(before ?? ""), StringComparison.OrdinalIgnoreCase), $"antes {before ?? "nenhum"}, agora {after ?? "nenhum"}");
                }
            }
            catch (Exception ex) { Check("Perfil ICC", false, ex.Message); }

            // ---- 4. read-only Windows information ----
            Check("Orientação (leitura)", WindowsDisplay.Current(lg) != null, WindowsDisplay.OrientationNames[WindowsDisplay.GetOrientation(lg)]);
            Check("Modo de vídeo / HDR (leitura)", true, WindowsDisplay.ModeText(lg) + " | HDR: " + WindowsDisplay.HdrText(lg));
            Check("Detecção de sensor", true, SensorDetector.Detect() is var s && s.Count == 0 ? "nenhum sensor conectado" : string.Join("; ", s));

            sb.AppendLine($"Resultado: {pass} OK, {fail} falha(s).");
            return Save(sb);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        static string Save(StringBuilder sb)
        {
            Directory.CreateDirectory(Log.Folder);
            var p = Path.Combine(Log.Folder, "autoteste-windows.txt");
            File.WriteAllText(p, sb.ToString(), Encoding.UTF8);
            return p;
        }
    }
}
