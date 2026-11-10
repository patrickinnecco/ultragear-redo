using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ultragear.Core
{
    public sealed class Profile
    {
        public string Name { get; set; }
        public int? Mode { get; set; }
        public int? Brightness { get; set; }
        public int? Contrast { get; set; }
        public int? Sharpness { get; set; }
        public int? BlackStabilizer { get; set; }
        public int? ColorPreset { get; set; }
        public int? Kelvin { get; set; }
        public int? Red { get; set; }
        public int? Green { get; set; }
        public int? Blue { get; set; }
        public int? AdaptiveSync { get; set; }
        public DateTime Created { get; set; }
        public override string ToString() => Name;
    }

    public sealed class AppPreset
    {
        public string ExePath { get; set; }
        public string DisplayName { get; set; }
        public int ModeCode { get; set; }
    }

    public sealed class AppSettings
    {
        public string SelectedMonitorPath { get; set; }
        public bool StartWithWindows { get; set; }
        public bool StartMinimized { get; set; } = true;
        public bool CloseToTray { get; set; } = true;

        // My Display Preset
        public bool PresetsEnabled { get; set; }
        public int? PresetDefaultMode { get; set; }
        public List<AppPreset> Presets { get; set; } = new();

        // Screen split
        public string SplitLayout { get; set; } = "Full";
        public bool SplitHotkeysEnabled { get; set; } = true;
        public bool SplitDragSnap { get; set; } = true;
        public int CustomColumns { get; set; } = 3;
        public int CustomRows { get; set; } = 1;

        public List<Profile> Profiles { get; set; } = new();
        public bool PictureExpanded { get; set; } = true;
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }

        static string FilePath => Path.Combine(Log.Folder, "settings.json");
        static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Log.Write("settings load failed, keeping a backup: " + ex.Message);
                try { File.Copy(FilePath, FilePath + ".bad", true); } catch { }
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Log.Folder);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
                File.Move(tmp, FilePath, true);
            }
            catch (Exception ex) { Log.Write("settings save failed: " + ex.Message); }
        }

        public void ExportProfiles(string path) => File.WriteAllText(path, JsonSerializer.Serialize(Profiles, Json));

        public int ImportProfiles(string path)
        {
            var list = JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(path)) ?? new List<Profile>();
            foreach (var p in list) { Profiles.RemoveAll(x => x.Name == p.Name); Profiles.Add(p); }
            return list.Count;
        }
    }
}
