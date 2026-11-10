using System;
using System.Collections.Generic;
using System.Linq;
using Ultragear.Native;
using static Ultragear.Native.NativeMethods;

namespace Ultragear.Core
{
    /// <summary>An active display output, identified by its EDID PnP id (e.g. GSM5CEA) and device path.</summary>
    public sealed class MonitorTarget
    {
        public IntPtr HMonitor { get; set; }
        public string GdiDeviceName { get; set; }      // \\.\DISPLAY2
        public string DevicePath { get; set; }         // \\?\DISPLAY#GSM5CEA#...
        public string PnpId { get; set; }              // GSM5CEA
        public string FriendlyName { get; set; }       // LG ULTRAGEAR+
        public string Serial { get; set; }
        public int ManufactureYear { get; set; }
        public int PhysicalIndex { get; set; }         // index inside the HMONITOR physical list
        public RECT Bounds { get; set; }
        public RECT WorkArea { get; set; }
        public LUID AdapterId { get; set; }
        public uint SourceId { get; set; }
        public uint OutputTechnology { get; set; }
        public bool IsLg => PnpId != null && PnpId.StartsWith("GSM", StringComparison.OrdinalIgnoreCase);
        /// <summary>LG 27GX704A reports EDID product 0x5CEA ("LG ULTRAGEAR+").</summary>
        public bool Is27GX704A => string.Equals(PnpId, "GSM5CEA", StringComparison.OrdinalIgnoreCase);

        public string ConnectionName => OutputTechnology switch
        {
            0 => "VGA", 4 => "DVI", 5 => "HDMI", 10 => "DisplayPort", 11 => "DisplayPort (embutido)", 6 => "LVDS", 16 => "USB-C/DP", _ => "Outro (" + OutputTechnology + ")"
        };

        public string Display => (FriendlyName ?? "Monitor") + (Is27GX704A ? " (27GX704A)" : "") + "  •  " + GdiDeviceName?.Replace(@"\\.\", "");
        public override string ToString() => Display;
    }

    public static class MonitorLocator
    {
        public static List<MonitorTarget> FindAll()
        {
            var result = new List<MonitorTarget>();
            var hmons = new List<IntPtr>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) => { hmons.Add(h); return true; }, IntPtr.Zero);

            var ccd = QueryCcd();
            var wmi = QueryWmiIds();

            foreach (var hm in hmons)
            {
                var mi = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
                if (!GetMonitorInfo(hm, ref mi)) continue;

                // Active monitor device interfaces attached to this GDI source, in the same order as the physical monitor list.
                var paths = new List<string>();
                for (uint j = 0; ; j++)
                {
                    var dd = new DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<DISPLAY_DEVICE>() };
                    if (!EnumDisplayDevices(mi.szDevice, j, ref dd, EDD_GET_DEVICE_INTERFACE_NAME)) break;
                    if ((dd.StateFlags & DISPLAY_DEVICE_ACTIVE) != 0) paths.Add(dd.DeviceID);
                }
                if (paths.Count == 0) paths.Add(null);

                for (int i = 0; i < paths.Count; i++)
                {
                    var t = new MonitorTarget
                    {
                        HMonitor = hm, GdiDeviceName = mi.szDevice, DevicePath = paths[i], PhysicalIndex = i,
                        Bounds = mi.rcMonitor, WorkArea = mi.rcWork
                    };
                    t.PnpId = ExtractPnpId(t.DevicePath);
                    var c = ccd.FirstOrDefault(x => string.Equals(x.path, t.DevicePath, StringComparison.OrdinalIgnoreCase))
                            ?? ccd.FirstOrDefault(x => string.Equals(x.gdi, t.GdiDeviceName, StringComparison.OrdinalIgnoreCase));
                    if (c != null)
                    {
                        t.FriendlyName = c.friendly; t.AdapterId = c.adapter; t.SourceId = c.sourceId; t.OutputTechnology = c.tech;
                    }
                    var w = wmi.FirstOrDefault(x => t.DevicePath != null && t.DevicePath.Replace('#', '\\').IndexOf(x.instance, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (w != null)
                    {
                        t.Serial = w.serial; t.ManufactureYear = w.year;
                        if (string.IsNullOrEmpty(t.FriendlyName)) t.FriendlyName = w.name;
                    }
                    result.Add(t);
                }
            }
            return result;
        }

        static string ExtractPnpId(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return null;
            var parts = devicePath.Split('#');
            return parts.Length > 1 ? parts[1] : null;
        }

        sealed class CcdInfo { public string gdi, path, friendly; public LUID adapter; public uint sourceId, tech; }

        static List<CcdInfo> QueryCcd()
        {
            var list = new List<CcdInfo>();
            try
            {
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm) != 0) return list;
                var paths = new DISPLAYCONFIG_PATH_INFO[np];
                var modes = new DISPLAYCONFIG_MODE_INFO[nm];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return list;
                for (int i = 0; i < np; i++)
                {
                    var p = paths[i];
                    var src = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    src.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                    src.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                    src.header.adapterId = p.sourceInfo.adapterId; src.header.id = p.sourceInfo.id;
                    DisplayConfigGetDeviceInfo(ref src);
                    var tgt = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    tgt.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    tgt.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                    tgt.header.adapterId = p.targetInfo.adapterId; tgt.header.id = p.targetInfo.id;
                    DisplayConfigGetDeviceInfo(ref tgt);
                    list.Add(new CcdInfo
                    {
                        gdi = src.viewGdiDeviceName, path = tgt.monitorDevicePath, friendly = tgt.monitorFriendlyDeviceName,
                        adapter = p.sourceInfo.adapterId, sourceId = p.sourceInfo.id, tech = p.targetInfo.outputTechnology
                    });
                }
            }
            catch (Exception ex) { Log.Write("CCD query failed: " + ex.Message); }
            return list;
        }

        /// <summary>Reads HDR / advanced color state of the target from Windows.</summary>
        public static (bool supported, bool enabled, uint bits) GetAdvancedColor(MonitorTarget t)
        {
            try
            {
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm) != 0) return (false, false, 0);
                var paths = new DISPLAYCONFIG_PATH_INFO[np];
                var modes = new DISPLAYCONFIG_MODE_INFO[nm];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return (false, false, 0);
                foreach (var p in paths.Take((int)np))
                {
                    if (p.sourceInfo.id != t.SourceId || p.sourceInfo.adapterId.LowPart != t.AdapterId.LowPart) continue;
                    var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
                    info.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
                    info.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>();
                    info.header.adapterId = p.targetInfo.adapterId; info.header.id = p.targetInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref info) == 0)
                        return ((info.value & 1) != 0, (info.value & 2) != 0, info.bitsPerColorChannel);
                }
            }
            catch { }
            return (false, false, 0);
        }

        sealed class WmiId { public string instance, name, serial; public int year; }

        /// <summary>Reads EDID blocks that Windows stores in the registry (read-only, no admin needed).</summary>
        static List<WmiId> QueryWmiIds()
        {
            var list = new List<WmiId>();
            try
            {
                using var display = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY");
                if (display == null) return list;
                foreach (var model in display.GetSubKeyNames())
                {
                    using var mk = display.OpenSubKey(model);
                    foreach (var inst in mk.GetSubKeyNames())
                    {
                        using var pk = mk.OpenSubKey(inst + @"\Device Parameters");
                        if (pk?.GetValue("EDID") is not byte[] edid || edid.Length < 128) continue;
                        var id = new WmiId { instance = @"DISPLAY\" + model + @"\" + inst, year = edid[17] + 1990 };
                        for (int d = 54; d <= 108; d += 18)
                        {
                            if (edid[d] != 0 || edid[d + 1] != 0) continue;
                            string text = System.Text.Encoding.ASCII.GetString(edid, d + 5, 13).Split('\n')[0].Trim();
                            if (edid[d + 3] == 0xFC) id.name = text;
                            else if (edid[d + 3] == 0xFF) id.serial = text;
                        }
                        list.Add(id);
                    }
                }
            }
            catch (Exception ex) { Log.Write("EDID registry read failed: " + ex.Message); }
            return list;
        }
    }
}
