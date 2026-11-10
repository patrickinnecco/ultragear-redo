using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Ultragear.Core;
using Ultragear.Features;
using Ultragear.UI;

namespace Ultragear
{
    /// <summary>Shared application state (one instance).</summary>
    public static class Host
    {
        public static AppSettings Settings;
        public static MonitorService Monitor = new();
        public static List<MonitorTarget> Targets = new();
        public static AppPresetWatcher Presets;
        public static MainWindow Main;
        public static TrayIcon Tray;
        public static HotkeyManager Hotkeys;
        public static SnapManager Snap;

        public static event Action TargetsChanged;
        public static event Action<string, bool> Notify;   // message, isError

        public static void Toast(string msg, bool error = false) =>
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => Notify?.Invoke(msg, error)));

        /// <summary>Scans displays; prefers the saved monitor, then the 27GX704A, then any LG, then any DDC/CI display.</summary>
        public static MonitorTarget RefreshTargets()
        {
            Targets = MonitorLocator.FindAll()
                .OrderByDescending(t => t.Is27GX704A).ThenByDescending(t => t.IsLg).ToList();
            var pick = Targets.FirstOrDefault(t => t.DevicePath != null && t.DevicePath == Settings.SelectedMonitorPath)
                       ?? Targets.FirstOrDefault(t => t.Is27GX704A)
                       ?? Targets.FirstOrDefault(t => t.IsLg);
            bool changed = Monitor.Target?.DevicePath != pick?.DevicePath;
            if (changed) Monitor.Connect(pick);
            TargetsChanged?.Invoke();
            return pick;
        }

        public static void Select(MonitorTarget t)
        {
            Settings.SelectedMonitorPath = t?.DevicePath;
            Settings.Save();
            Monitor.Connect(t);
            TargetsChanged?.Invoke();
        }
    }

    public partial class App : Application
    {
        Mutex _single;
        EventWaitHandle _wake;

        protected override async void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length >= 2 && e.Args[0] == "--vcp")
            {
                // Diagnostic: Ultragear.exe --vcp get 10,12   |   --vcp set 15 45   (LG 27GX704A only, located by EDID)
                var sb = new System.Text.StringBuilder();
                var lg = MonitorLocator.FindAll().FirstOrDefault(t => t.Is27GX704A);
                if (lg == null) sb.AppendLine("LG 27GX704A não está ativo.");
                else
                {
                    using var ch = new DdcChannel(lg);
                    sb.AppendLine($"alvo: {lg.FriendlyName} {lg.GdiDeviceName} {lg.DevicePath}");
                    try
                    {
                        if (e.Args[1] == "set" && e.Args.Length >= 4)
                        {
                            byte c = Convert.ToByte(e.Args[2], 16); uint v = uint.Parse(e.Args[3]);
                            await ch.SetAsync(c, v); await Task.Delay(700);
                            sb.AppendLine($"set {c:X2}={v} -> {await ch.GetAsync(c)}");
                        }
                        else foreach (var c in e.Args[2].Split(','))
                            {
                                try { sb.AppendLine($"{c}: {await ch.GetAsync(Convert.ToByte(c, 16))}"); }
                                catch (Exception ex) { sb.AppendLine($"{c}: erro {ex.Message}"); }
                            }
                    }
                    catch (Exception ex) { sb.AppendLine("erro: " + ex.Message); }
                }
                System.IO.Directory.CreateDirectory(Log.Folder);
                System.IO.File.WriteAllText(System.IO.Path.Combine(Log.Folder, "vcp.txt"), sb.ToString());
                Shutdown();
                return;
            }

            if (e.Args.Contains("--selftest") || e.Args.Contains("--probe-modes") || e.Args.Contains("--selftest-windows"))
            {
                string path;
                try
                {
                    path = e.Args.Contains("--selftest-windows") ? await WindowsSelfTest.RunAsync()
                         : e.Args.Contains("--selftest")
                        ? await SelfTest.RunAsync(e.Args.Contains("--vrr"))
                        : await SelfTest.ProbeModesAsync(e.Args.SkipWhile(a => a != "--probe-modes").Skip(1).FirstOrDefault()?.Split(',').Select(uint.Parse).ToArray()
                                                         ?? new uint[] { 1, 15, 22, 23, 24, 26, 31, 39, 48, 49, 50 });
                }
                catch (Exception ex) { Log.Write("selftest crashed: " + ex); path = Log.FilePath; }
                if (!e.Args.Contains("--quiet")) MessageBox.Show("Relatório salvo em:\n" + path, "Ultragear — autoteste");
                Shutdown();
                return;
            }

            _single = new Mutex(true, "Ultragear.SingleInstance", out bool first);
            _wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Ultragear.Wake");
            if (!first) { _wake.Set(); Shutdown(); return; }

            DispatcherUnhandledException += (s, a) =>
            {
                Log.Write("UI exception: " + a.Exception);
                Host.Toast("Erro inesperado: " + a.Exception.Message, true);
                a.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log.Write("Fatal: " + a.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, a) => { Log.Write("Task: " + a.Exception); a.SetObserved(); };

            base.OnStartup(e);
            Log.Write("Ultragear starting " + string.Join(" ", e.Args));
            Host.Settings = AppSettings.Load();
            Host.Monitor.Error += m => Host.Toast(m, true);
            Host.RefreshTargets();

            Host.Main = new MainWindow();
            Host.Tray = new TrayIcon();
            Host.Hotkeys = new HotkeyManager(Host.Main);
            Host.Snap = new SnapManager();
            Host.Presets = new AppPresetWatcher(Host.Settings, Host.Monitor);
            Host.Presets.Applied += m => { Host.Toast("Predefinição: " + m); Host.Main.RefreshFromState(); };
            if (Host.Settings.PresetsEnabled) Host.Presets.Start();
            Host.Snap.Enabled = Host.Settings.SplitDragSnap;

            bool tray = e.Args.Contains("--tray") && Host.Settings.StartMinimized;
            if (!tray) Host.Main.Show();

            SystemEvents.DisplaySettingsChanged += (s, a) => Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(1500);
                Host.RefreshTargets();
                Host.Main.OnDisplaysChanged();
            }));

            var t = new Thread(() =>
            {
                while (_wake.WaitOne())
                    Dispatcher.BeginInvoke(new Action(() => Host.Main.ShowFromTray()));
            }) { IsBackground = true };
            t.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Host.Presets?.Dispose();
            Host.Hotkeys?.Dispose();
            Host.Snap?.Dispose();
            Host.Tray?.Dispose();
            Host.Monitor?.Dispose();
            Host.Settings?.Save();
            base.OnExit(e);
        }
    }
}
