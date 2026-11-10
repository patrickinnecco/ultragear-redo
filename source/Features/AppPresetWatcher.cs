using System;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Ultragear.Core;
using static Ultragear.Native.NativeMethods;

namespace Ultragear.Features
{
    /// <summary>
    /// "My Display Preset": switches the monitor picture mode according to the application in the foreground.
    /// </summary>
    public sealed class AppPresetWatcher : IDisposable
    {
        readonly AppSettings _settings;
        readonly MonitorService _monitor;
        readonly WinEventDelegate _proc;
        readonly DispatcherTimer _debounce;
        IntPtr _hook;
        uint? _lastApplied;
        public event Action<string> Applied;

        public AppPresetWatcher(AppSettings settings, MonitorService monitor)
        {
            _settings = settings; _monitor = monitor;
            _proc = OnForeground;
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _debounce.Tick += async (s, e) =>
            {
                _debounce.Stop();
                try { await Evaluate(); } catch (Exception ex) { Log.Write("preset evaluate: " + ex.Message); }
            };
        }

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
            _lastApplied = null;
            _debounce.Start();
        }

        public void Stop()
        {
            if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
            _debounce.Stop();
        }

        void OnForeground(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            _debounce.Stop(); _debounce.Start();
        }

        async System.Threading.Tasks.Task Evaluate()
        {
            if (!_settings.PresetsEnabled || !_monitor.IsConnected) return;
            var hwnd = GetForegroundWindow();
            var path = WindowsDisplay.ProcessPathOf(hwnd);
            if (path == null || string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return;

            var match = _settings.Presets.FirstOrDefault(p => string.Equals(p.ExePath, path, StringComparison.OrdinalIgnoreCase))
                        ?? _settings.Presets.FirstOrDefault(p => string.Equals(Path.GetFileName(p.ExePath), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
            uint? target = match != null ? (uint)match.ModeCode : (uint?)_settings.PresetDefaultMode;
            if (target == null || target == _lastApplied) return;
            if (_monitor.State.Mode == target) { _lastApplied = target; return; }

            var r = await _monitor.SetPictureModeAsync(target.Value);
            if (r.Ok)
            {
                _lastApplied = target;
                var name = Lg27GX704A.ModeFor(target.Value)?.Name ?? target.ToString();
                Applied?.Invoke($"{(match?.DisplayName ?? "Demais aplicativos")} → {name}");
            }
        }

        public void Dispose() => Stop();
    }
}
