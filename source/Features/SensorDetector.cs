using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Ultragear.Features
{
    /// <summary>
    /// Detects colorimeters / spectrophotometers currently plugged in (USB vendor ids published by the
    /// manufacturers). Detection only: driving a sensor requires the vendor's own SDK.
    /// </summary>
    public static class SensorDetector
    {
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        static extern int CM_Get_Device_ID_List_Size(out uint len, string filter, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        static extern int CM_Get_Device_ID_List(string filter, [Out] char[] buffer, uint len, uint flags);
        const uint CM_GETIDLIST_FILTER_ENUMERATOR = 0x1, CM_GETIDLIST_FILTER_PRESENT = 0x100;

        static readonly (string vid, string pid, string name)[] Known =
        {
            ("0765", "5020", "X-Rite/Calibrite i1Display Pro / ColorChecker Display (família i1Display 3)"),
            ("0765", "5021", "Calibrite ColorChecker Display Plus"),
            ("0765", "5022", "Calibrite ColorChecker Display Pro"),
            ("0765", "D094", "X-Rite i1Pro / ColorMunki Photo-Design / i1Studio"),
            ("0765", "D020", "X-Rite i1Display 2"),
            ("0765", "6008", "X-Rite i1Pro 3"),
            ("0765", null,   "Dispositivo X-Rite / Calibrite"),
            ("085C", "0300", "Datacolor Spyder3"),
            ("085C", "0400", "Datacolor Spyder4"),
            ("085C", "0500", "Datacolor Spyder5"),
            ("085C", "0A00", "Datacolor SpyderX"),
            ("085C", null,   "Dispositivo Datacolor Spyder"),
            ("132B", null,   "Konica Minolta (CA-410 ou similar)"),
            ("10C4", null,   "Ponte USB Silicon Labs (possível Gamma Scientific GS1160B)"),
            ("0403", "6001", "Ponte USB FTDI (possível Klein K10-A)"),
        };

        public static List<string> Detect()
        {
            var found = new List<string>();
            try
            {
                uint flags = CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT;
                if (CM_Get_Device_ID_List_Size(out uint len, "USB", flags) != 0 || len == 0) return found;
                var buf = new char[len];
                if (CM_Get_Device_ID_List("USB", buf, len, flags) != 0) return found;
                var ids = new string(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                var seen = new HashSet<string>();
                foreach (var id in ids)
                {
                    var up = id.ToUpperInvariant();
                    int v = up.IndexOf("VID_"), p = up.IndexOf("PID_");
                    if (v < 0 || p < 0) continue;
                    string vid = up.Substring(v + 4, 4), pid = up.Substring(p + 4, 4);
                    foreach (var k in Known)
                    {
                        if (k.vid != vid || (k.pid != null && k.pid != pid)) continue;
                        if (seen.Add(vid + pid)) found.Add($"{k.name}  (VID {vid} / PID {pid})");
                        break;
                    }
                }
            }
            catch (Exception ex) { Core.Log.Write("sensor detection: " + ex.Message); }
            return found;
        }
    }
}
