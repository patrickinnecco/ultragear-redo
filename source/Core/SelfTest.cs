using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using M = Ultragear.Core.Lg27GX704A;

namespace Ultragear.Core
{
    /// <summary>
    /// Exercises every DDC/CI function on the real monitor, verifies each write by reading it back
    /// and restores the original values. Usage: Ultragear.exe --selftest [--vrr]
    /// </summary>
    public static class SelfTest
    {
        public static string ReportPath => Path.Combine(Log.Folder, "autoteste.txt");

        public static async Task<string> RunAsync(bool includeVrr)
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            void Line(string s) { sb.AppendLine(s); Log.Write("selftest: " + s); }
            void Check(string name, bool ok, string detail) { if (ok) pass++; else fail++; Line($"[{(ok ? "OK " : "FALHA")}] {name}: {detail}"); }

            Line($"Ultragear — autoteste DDC/CI  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            var targets = MonitorLocator.FindAll();
            foreach (var t in targets) Line($"Tela ativa: {t.FriendlyName} [{t.PnpId}] {t.GdiDeviceName} via {t.ConnectionName}");
            var lg = targets.FirstOrDefault(t => t.Is27GX704A);
            if (lg == null) { Line("LG 27GX704A (GSM5CEA) não está ativo. Autoteste cancelado — nenhum comando foi enviado."); return Save(sb); }

            using var svc = new MonitorService();
            svc.Connect(lg);
            MonitorState s0;
            try { s0 = (await svc.ReadAllAsync()).Clone(); }
            catch (Exception ex) { Line("Sem resposta DDC/CI: " + ex.Message); return Save(sb); }
            var original = svc.CaptureProfile("original");
            Line($"Estado inicial: modo {s0.Mode} ({M.ModeFor(s0.Mode ?? 0)?.Name}), brilho {s0.Brightness}, contraste {s0.Contrast}, nitidez {s0.Sharpness}, " +
                 $"estab. preto {s0.BlackStabilizer}, temp {s0.ColorPreset}/{s0.Kelvin}K, RGB {s0.Red}/{s0.Green}/{s0.Blue}, VRR {s0.AdaptiveSync}, volume {s0.Volume}, mudo {s0.Mute}, " +
                 $"firmware {s0.FirmwareText}, MCCS {s0.VcpVersionText}, uso {s0.UsageTime}");
            Check("Leitura do estado", s0.Mode != null && s0.Brightness != null, "valores lidos do monitor");

            async Task Value(string name, byte code, uint? cur, uint test)
            {
                if (cur == null) { Check(name, false, "não lido"); return; }
                var r = await svc.SetAsync(code, test);
                Check(name, r.Outcome == WriteOutcome.Applied, $"gravado {test}, lido {r.Actual}");
                var back = await svc.SetAsync(code, cur.Value);
                Check(name + " (restaurar)", back.Outcome == WriteOutcome.Applied, $"restaurado {cur}, lido {back.Actual}");
            }

            uint Other(uint? v, uint delta, uint max) => v is uint x ? (x + delta <= max ? x + delta : x - delta) : delta;

            await Value("Brilho", M.Brightness, s0.Brightness, Other(s0.Brightness, 10, 100));
            await Value("Contraste", M.Contrast, s0.Contrast, Other(s0.Contrast, 5, 100));
            await Value("Nitidez", M.Sharpness, s0.Sharpness, Other(s0.Sharpness, 5, 100));
            await Value("Estabilizador de preto", M.BlackStabilizer, s0.BlackStabilizer, Other(s0.BlackStabilizer, 5, 100));
            await Value("Volume", M.Volume, s0.Volume, Other(s0.Volume, 3, 100));
            if (s0.Mute != null)
            {
                var r = await svc.SetAsync(M.AudioMute, s0.Mute == 1 ? 2u : 1u);
                Check("Mudo", r.Outcome == WriteOutcome.Applied, $"lido {r.Actual}");
                var b = await svc.SetAsync(M.AudioMute, s0.Mute.Value);
                Check("Mudo (restaurar)", b.Outcome == WriteOutcome.Applied, $"lido {b.Actual}");
            }

            if (s0.EnergySaving is uint es && es <= 2)
            {
                foreach (uint v in new uint[] { 1, 2 })
                {
                    var r = await svc.SetAsync(M.SmartEnergySaving, v);
                    Check("SMART ENERGY SAVING = " + (v == 1 ? "Baixo" : "Elevado"), r.Outcome == WriteOutcome.Applied, $"lido {r.Actual}");
                }
                var b = await svc.SetAsync(M.SmartEnergySaving, es);
                Check("SMART ENERGY SAVING (restaurar)", b.Outcome == WriteOutcome.Applied, $"lido {b.Actual}");
            }

            // colour temperature presets, manual Kelvin and user RGB
            foreach (var ct in M.ColorTemps)
            {
                var r = await svc.SetColorPresetAsync(ct.Value);
                Check("Temperatura de cor: " + ct.Name, r.Outcome == WriteOutcome.Applied, $"lido {r.Actual}");
            }
            await svc.SetColorPresetAsync(M.ManualPresetValue);
            var k0 = (await svc.GetRawAsync(M.ColorTempKelvin)).Current;
            await Value("Temperatura manual (K)", M.ColorTempKelvin, k0, k0 == 5000 ? 5500u : 5000u);
            await svc.SetColorPresetAsync(11);
            foreach (var (n, c) in new[] { ("Ganho vermelho", M.RedGain), ("Ganho verde", M.GreenGain), ("Ganho azul", M.BlueGain) })
            {
                var v = (await svc.GetRawAsync(c)).Current;
                await Value(n, c, v, Other(v, 4, 100));
            }
            if (s0.ColorPreset != null)
            {
                var r = await svc.SetColorPresetAsync(s0.ColorPreset.Value);
                Check("Temperatura de cor (restaurar)", r.Ok, $"lido {r.Actual}");
            }

            // every picture mode
            foreach (var m in M.AllModes)
            {
                var r = await svc.SetPictureModeAsync(m.Code);
                var st = svc.State;
                Check("Modo " + m.Name, r.Outcome == WriteOutcome.Applied,
                      $"código {m.Code}, lido {r.Actual}; brilho {st.Brightness}, contraste {st.Contrast}, temp {st.ColorPreset}");
            }
            if (s0.Mode != null)
            {
                var r = await svc.SetPictureModeAsync(s0.Mode.Value);
                Check("Modo de imagem (restaurar)", r.Ok, $"lido {r.Actual}");
            }

            if (includeVrr && s0.AdaptiveSync != null)
            {
                var r = await svc.SetAdaptiveSyncAsync(s0.AdaptiveSync != 1);
                Check("Adaptive-Sync/VRR", r.Outcome == WriteOutcome.Applied, $"lido {r.Actual}");
                var b = await svc.SetAdaptiveSyncAsync(s0.AdaptiveSync == 1);
                Check("Adaptive-Sync/VRR (restaurar)", b.Outcome == WriteOutcome.Applied, $"lido {b.Actual}");
            }

            // final consistency check against the initial snapshot
            var end = await svc.ReadAllAsync();
            bool same = end.Mode == s0.Mode && end.Brightness == s0.Brightness && end.Contrast == s0.Contrast && end.Sharpness == s0.Sharpness &&
                        end.BlackStabilizer == s0.BlackStabilizer && end.ColorPreset == s0.ColorPreset && end.Volume == s0.Volume && end.AdaptiveSync == s0.AdaptiveSync && end.EnergySaving == s0.EnergySaving;
            Check("Estado final igual ao inicial", same,
                  $"modo {end.Mode}, brilho {end.Brightness}, contraste {end.Contrast}, nitidez {end.Sharpness}, estab. {end.BlackStabilizer}, temp {end.ColorPreset}, volume {end.Volume}, VRR {end.AdaptiveSync}");

            Line($"Resultado: {pass} OK, {fail} falha(s).");
            return Save(sb);
        }

        /// <summary>Reports which raw picture-mode codes the monitor accepts (restores the original mode).</summary>
        public static async Task<string> ProbeModesAsync(uint[] codes)
        {
            var sb = new StringBuilder();
            var lg = MonitorLocator.FindAll().FirstOrDefault(t => t.Is27GX704A);
            if (lg == null) { sb.AppendLine("LG 27GX704A não está ativo."); return Save(sb, "sondagem-modos.txt"); }
            using var svc = new MonitorService();
            svc.Connect(lg);
            var s0 = (await svc.ReadAllAsync()).Clone();
            foreach (var c in codes)
            {
                var r = await svc.SetAsync(M.PictureMode, c);
                await Task.Delay(800);
                var st = await svc.ReadAllAsync();
                sb.AppendLine($"{c}: {(r.Actual == c ? "aceito" : "recusado (lido " + r.Actual + ")")} | brilho {st.Brightness} contraste {st.Contrast} nitidez {st.Sharpness} estab {st.BlackStabilizer} temp {st.ColorPreset} K {st.Kelvin} RGB {st.Red}/{st.Green}/{st.Blue}");
            }
            await svc.SetPictureModeAsync(s0.Mode ?? 45);
            sb.AppendLine("restaurado: " + svc.State.Mode);
            return Save(sb, "sondagem-modos.txt");
        }

        static string Save(StringBuilder sb, string file = "autoteste.txt")
        {
            Directory.CreateDirectory(Log.Folder);
            var p = Path.Combine(Log.Folder, file);
            File.WriteAllText(p, sb.ToString(), Encoding.UTF8);
            return p;
        }
    }
}
