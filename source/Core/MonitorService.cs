using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using M = Ultragear.Core.Lg27GX704A;

namespace Ultragear.Core
{
    /// <summary>Snapshot of what the monitor reported. A null value means the monitor did not answer that code.</summary>
    public sealed class MonitorState
    {
        public uint? Mode, Brightness, Contrast, Sharpness, BlackStabilizer, ColorPreset, Kelvin, Red, Green, Blue;
        public uint? AdaptiveSync, Volume, Mute, Firmware, UsageTime, VcpVersion, ResponseTime, EnergySaving;
        public DateTime ReadAt = DateTime.Now;

        public MonitorState Clone() => (MonitorState)MemberwiseClone();

        public string FirmwareText =>Firmware is uint f ? $"{f >> 8}.{(f & 0xFF):00}" : "—";
        public string VcpVersionText => VcpVersion is uint v ? $"{v >> 8}.{v & 0xFF}" : "—";
    }

    public enum WriteOutcome { Applied, Adjusted, Rejected, Failed }

    public sealed class WriteResult
    {
        public WriteOutcome Outcome; public uint Requested; public uint? Actual; public string Error;
        public bool Ok => Outcome == WriteOutcome.Applied || Outcome == WriteOutcome.Adjusted;
    }

    /// <summary>High-level, verified control of the LG 27GX704A. Every write is read back from the monitor.</summary>
    public sealed class MonitorService : IDisposable
    {
        DdcChannel _ddc;
        readonly SemaphoreSlim _modeGate = new(1, 1);
        public MonitorTarget Target => _ddc?.Target;
        public bool IsConnected => _ddc != null;
        public MonitorState State { get; private set; } = new();

        public event Action<MonitorState> StateChanged;
        public event Action<string> Error;
        public event Action<string> Busy;

        public void Connect(MonitorTarget target)
        {
            _ddc?.Dispose();
            _ddc = target == null ? null : new DdcChannel(target);
            Log.Write("Connected to " + target?.FriendlyName + " " + target?.DevicePath);
        }

        async Task<uint?> TryGet(byte code)
        {
            try { return (await _ddc.GetAsync(code)).Current; }
            catch (Exception ex) { Log.Write($"read 0x{code:X2} failed: {ex.Message}"); return null; }
        }

        /// <summary>Reads every supported value. Throws if the monitor does not answer at all.</summary>
        public async Task<MonitorState> ReadAllAsync()
        {
            if (_ddc == null) throw new InvalidOperationException("Nenhum monitor selecionado.");
            var s = new MonitorState();
            try { s.Mode = (await _ddc.GetAsync(M.PictureMode)).Current; }
            catch (DdcException)
            {
                // a second attempt after relocating (handles change after VRR / input re-sync)
                await _ddc.ReconnectAsync();
                s.Mode = (await _ddc.GetAsync(M.PictureMode)).Current;
            }
            s.Brightness = await TryGet(M.Brightness);
            s.Contrast = await TryGet(M.Contrast);
            s.Sharpness = await TryGet(M.Sharpness);
            s.BlackStabilizer = await TryGet(M.BlackStabilizer);
            s.ColorPreset = await TryGet(M.ColorPreset);
            s.Kelvin = await TryGet(M.ColorTempKelvin);
            s.Red = await TryGet(M.RedGain);
            s.Green = await TryGet(M.GreenGain);
            s.Blue = await TryGet(M.BlueGain);
            s.AdaptiveSync = await TryGet(M.AdaptiveSync);
            s.Volume = await TryGet(M.Volume);
            s.Mute = await TryGet(M.AudioMute);
            s.ResponseTime = await TryGet(M.ResponseTime);
            s.EnergySaving = await TryGet(M.SmartEnergySaving);
            s.Firmware ??= await TryGet(M.FirmwareLevel);
            s.UsageTime = await TryGet(M.UsageTime);
            s.VcpVersion = await TryGet(M.VcpVersion);
            State = s;
            StateChanged?.Invoke(s);
            return s;
        }

        /// <summary>Writes a plain value (brightness, contrast, gains...) and verifies it by reading back.</summary>
        public async Task<WriteResult> SetAsync(byte code, uint value, bool coalesce = false)
        {
            var r = new WriteResult { Requested = value };
            if (_ddc == null) { r.Outcome = WriteOutcome.Failed; r.Error = "Nenhum monitor conectado."; return r; }
            try
            {
                uint wire = code == M.BlackStabilizer ? (uint)Math.Round(value / 5.0) : value;
                if (coalesce) await _ddc.SetLatestAsync(code, wire); else await _ddc.SetAsync(code, wire);
                await Task.Delay(120);
                r.Actual = (await _ddc.GetAsync(code)).Current;
                uint expected = code == M.BlackStabilizer ? wire * 5 : value;
                r.Outcome = r.Actual == expected ? WriteOutcome.Applied
                          : (coalesce ? WriteOutcome.Adjusted : WriteOutcome.Rejected);
                if (r.Outcome == WriteOutcome.Rejected)
                    Error?.Invoke($"O monitor não aceitou {value} (valor atual: {r.Actual}). Este ajuste pode estar bloqueado no modo de imagem atual.");
                UpdateState(code, r.Actual);
            }
            catch (Exception ex)
            {
                r.Outcome = WriteOutcome.Failed; r.Error = ex.Message;
                Error?.Invoke(ex.Message);
            }
            return r;
        }

        public async Task<WriteResult> SetPictureModeAsync(uint code)
        {
            await _modeGate.WaitAsync();
            try
            {
                Busy?.Invoke("Aplicando modo de imagem…");
                var r = await SetAsync(M.PictureMode, code);
                await Task.Delay(500);   // values are stored per mode; let the scaler settle before reading them
                try { await ReadAllAsync(); } catch (Exception ex) { Error?.Invoke(ex.Message); }
                return r;
            }
            finally { _modeGate.Release(); Busy?.Invoke(null); }
        }

        public async Task<WriteResult> SetColorPresetAsync(uint preset)
        {
            var r = await SetAsync(M.ColorPreset, preset);
            if (r.Ok)
            {
                State.Kelvin = await TryGet(M.ColorTempKelvin);
                State.Red = await TryGet(M.RedGain);
                State.Green = await TryGet(M.GreenGain);
                State.Blue = await TryGet(M.BlueGain);
                StateChanged?.Invoke(State);
            }
            return r;
        }

        /// <summary>Adaptive-Sync makes the monitor re-train the link: DDC/CI is unavailable for a few seconds.</summary>
        public async Task<WriteResult> SetAdaptiveSyncAsync(bool on)
        {
            var r = new WriteResult { Requested = on ? 1u : 0u };
            Busy?.Invoke("O monitor está ressincronizando o sinal…");
            try
            {
                await _ddc.SetAsync(M.AdaptiveSync, r.Requested);
                await Task.Delay(2500);
                for (int i = 0; i < 15; i++)
                {
                    try
                    {
                        await _ddc.ReconnectAsync();
                        r.Actual = (await _ddc.GetAsync(M.AdaptiveSync)).Current;
                        break;
                    }
                    catch { await Task.Delay(1000); }
                }
                r.Outcome = r.Actual == r.Requested ? WriteOutcome.Applied : (r.Actual == null ? WriteOutcome.Failed : WriteOutcome.Rejected);
                if (r.Actual == null) { r.Error = "O monitor não respondeu após a ressincronização."; Error?.Invoke(r.Error); }
                UpdateState(M.AdaptiveSync, r.Actual);
            }
            catch (Exception ex) { r.Outcome = WriteOutcome.Failed; r.Error = ex.Message; Error?.Invoke(ex.Message); }
            finally { Busy?.Invoke(null); }
            return r;
        }

        void UpdateState(byte code, uint? v)
        {
            var s = State;
            switch (code)
            {
                case M.PictureMode: s.Mode = v; break;
                case M.Brightness: s.Brightness = v; break;
                case M.Contrast: s.Contrast = v; break;
                case M.Sharpness: s.Sharpness = v; break;
                case M.BlackStabilizer: s.BlackStabilizer = v; break;
                case M.ColorPreset: s.ColorPreset = v; break;
                case M.ColorTempKelvin: s.Kelvin = v; break;
                case M.RedGain: s.Red = v; break;
                case M.GreenGain: s.Green = v; break;
                case M.BlueGain: s.Blue = v; break;
                case M.AdaptiveSync: s.AdaptiveSync = v; break;
                case M.Volume: s.Volume = v; break;
                case M.AudioMute: s.Mute = v; break;
                case M.SmartEnergySaving: s.EnergySaving = v; break;
            }
            StateChanged?.Invoke(s);
        }

        public Task<string> GetCapabilitiesAsync() => _ddc.GetCapabilitiesAsync();
        public Task<VcpReply> GetRawAsync(byte code) => _ddc.GetAsync(code);

        /// <summary>Applies a saved profile in a safe order: mode first, then the values stored for it.</summary>
        public async Task<List<string>> ApplyProfileAsync(Profile p)
        {
            var problems = new List<string>();
            async Task Put(byte code, int? v, string name)
            {
                if (v == null) return;
                var r = await SetAsync(code, (uint)v.Value);
                if (!r.Ok) problems.Add($"{name}: {r.Error ?? "não aceito (atual " + r.Actual + ")"}");
            }
            if (p.Mode != null)
            {
                var r = await SetPictureModeAsync((uint)p.Mode.Value);
                if (!r.Ok) problems.Add("Modo de imagem: " + (r.Error ?? "não aceito"));
            }
            await Put(M.Brightness, p.Brightness, "Brilho");
            await Put(M.Contrast, p.Contrast, "Contraste");
            await Put(M.Sharpness, p.Sharpness, "Nitidez");
            await Put(M.BlackStabilizer, p.BlackStabilizer, "Estabilizador de preto");
            if (p.ColorPreset != null) await SetColorPresetAsync((uint)p.ColorPreset.Value);
            if (p.ColorPreset == (int)M.ManualPresetValue) await Put(M.ColorTempKelvin, p.Kelvin, "Temperatura (K)");
            if (p.ColorPreset == 11)
            {
                await Put(M.RedGain, p.Red, "Vermelho");
                await Put(M.GreenGain, p.Green, "Verde");
                await Put(M.BlueGain, p.Blue, "Azul");
            }
            if (p.AdaptiveSync != null && State.AdaptiveSync != (uint)p.AdaptiveSync)
                await SetAdaptiveSyncAsync(p.AdaptiveSync == 1);
            try { await ReadAllAsync(); } catch { }
            return problems;
        }

        public Profile CaptureProfile(string name)
        {
            var s = State;
            return new Profile
            {
                Name = name, Mode = (int?)s.Mode, Brightness = (int?)s.Brightness, Contrast = (int?)s.Contrast,
                Sharpness = (int?)s.Sharpness, BlackStabilizer = (int?)s.BlackStabilizer, ColorPreset = (int?)s.ColorPreset,
                Kelvin = (int?)s.Kelvin, Red = (int?)s.Red, Green = (int?)s.Green, Blue = (int?)s.Blue,
                AdaptiveSync = (int?)s.AdaptiveSync, Created = DateTime.Now
            };
        }

        public void Dispose() => _ddc?.Dispose();
    }
}
