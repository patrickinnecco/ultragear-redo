using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Ultragear.Core;
using static Ultragear.Native.NativeMethods;

namespace Ultragear.Features
{
    /// <summary>Windows-side display features: orientation, current mode, start-up registration.</summary>
    public static class WindowsDisplay
    {
        public static readonly string[] OrientationNames = { "Paisagem", "Retrato", "Paisagem (invertida)", "Retrato (invertido)" };

        public static DEVMODE? Current(MonitorTarget t)
        {
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            return EnumDisplaySettings(t.GdiDeviceName, ENUM_CURRENT_SETTINGS, ref dm) ? dm : null;
        }

        /// <summary>DMDO_DEFAULT=0, 90=1, 180=2, 270=3 — mapped to LG Switch's Landscape/Portrait/flipped list.</summary>
        public static int GetOrientation(MonitorTarget t) => Current(t)?.dmDisplayOrientation switch
        {
            0 => 0, 1 => 1, 2 => 2, 3 => 3, _ => 0
        };

        public static string SetOrientation(MonitorTarget t, int orientation)
        {
            var cur = Current(t);
            if (cur == null) return "Não foi possível ler o modo atual da tela.";
            var dm = cur.Value;
            bool wasPortrait = dm.dmDisplayOrientation % 2 == 1;
            bool toPortrait = orientation % 2 == 1;
            if (wasPortrait != toPortrait) (dm.dmPelsWidth, dm.dmPelsHeight) = (dm.dmPelsHeight, dm.dmPelsWidth);
            dm.dmDisplayOrientation = orientation;
            dm.dmFields = DM_DISPLAYORIENTATION | DM_PELSWIDTH | DM_PELSHEIGHT;
            int r = ChangeDisplaySettingsEx(t.GdiDeviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            Log.Write($"orientation -> {orientation}: {r}");
            return r == DISP_CHANGE_SUCCESSFUL ? null : $"O Windows recusou a rotação (código {r}).";
        }

        public static string ModeText(MonitorTarget t)
        {
            var c = Current(t);
            if (c == null) return "—";
            var hdr = MonitorLocator.GetAdvancedColor(t);
            return $"{c.Value.dmPelsWidth} × {c.Value.dmPelsHeight} @ {c.Value.dmDisplayFrequency} Hz" +
                   (hdr.bits > 0 ? $", {hdr.bits} bits/canal" : "");
        }

        public static string HdrText(MonitorTarget t)
        {
            var hdr = MonitorLocator.GetAdvancedColor(t);
            if (!hdr.supported) return "Não suportado / não informado";
            return hdr.enabled ? "Ativado (Windows)" : "Desativado (Windows)";
        }

        // ---------- start with Windows (current user, no admin) ----------
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void SetStartup(bool enable)
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable) k.SetValue("Ultragear", $"\"{Environment.ProcessPath}\" --tray");
            else k.DeleteValue("Ultragear", false);
        }

        public static bool IsStartupEnabled()
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("Ultragear") is string s && s.Contains(Environment.ProcessPath ?? "Ultragear.exe", StringComparison.OrdinalIgnoreCase);
        }

        public static string ProcessPathOf(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new System.Text.StringBuilder(1024); int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }
    }

    /// <summary>ICC profile association through the public WCS API (mscms.dll, Windows 10 2004+).</summary>
    public static class ColorProfiles
    {
        public static string ColorDirectory
        {
            get
            {
                uint size = 520; var sb = new System.Text.StringBuilder((int)size);
                return GetColorDirectory(null, sb, ref size) ? sb.ToString() : Path.Combine(Environment.SystemDirectory, @"spool\drivers\color");
            }
        }

        public static List<string> Installed()
        {
            var l = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(ColorDirectory))
                    if (f.EndsWith(".icc", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".icm", StringComparison.OrdinalIgnoreCase))
                        l.Add(Path.GetFileName(f));
            }
            catch (Exception ex) { Log.Write("list profiles: " + ex.Message); }
            l.Sort(StringComparer.OrdinalIgnoreCase);
            return l;
        }

        public static string GetDefault(MonitorTarget t)
        {
            try
            {
                int hr = ColorProfileGetDisplayDefault(WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER, t.AdapterId, t.SourceId, CPT_ICC, CPST_NONE, out IntPtr p);
                if (hr != 0 || p == IntPtr.Zero) return null;
                var s = Marshal.PtrToStringUni(p); LocalFree(p); return s;
            }
            catch (EntryPointNotFoundException) { return null; }
        }

        public static string SetDefault(MonitorTarget t, string profileFile)
        {
            try
            {
                int hr = ColorProfileAddDisplayAssociation(WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER, profileFile, t.AdapterId, t.SourceId, true, false);
                return hr == 0 ? null : $"O Windows recusou a associação (HRESULT 0x{hr:X8}).";
            }
            catch (EntryPointNotFoundException) { return "Esta versão do Windows não oferece a API de associação de perfil por tela."; }
        }

        public static string Install(string fullPath)
        {
            return InstallColorProfile(null, fullPath) ? null : $"Falha ao instalar o perfil (erro {Marshal.GetLastWin32Error()}). Pode ser necessário executar como administrador.";
        }
    }
}
