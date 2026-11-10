using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static Ultragear.Native.NativeMethods;

namespace Ultragear.Core
{
    public readonly struct VcpReply
    {
        public VcpReply(uint current, uint maximum) { Current = current; Maximum = maximum; }
        public uint Current { get; }
        public uint Maximum { get; }
        public override string ToString() => $"{Current}/{Maximum}";
    }

    public sealed class DdcException : Exception
    {
        public DdcException(string message, int win32) : base(message) { Win32Error = win32; }
        public int Win32Error { get; }
    }

    /// <summary>
    /// Serialises all DDC/CI traffic for one physical monitor on a dedicated thread.
    /// DDC/CI over I2C is slow and not re-entrant; commands are spaced, retried and the physical
    /// handle is re-opened (located again by device path) whenever the display configuration changes.
    /// </summary>
    public sealed class DdcChannel : IDisposable
    {
        readonly BlockingCollection<Action> _queue = new();
        readonly Thread _thread;
        readonly Dictionary<byte, uint> _pendingWrites = new();
        readonly object _pendingGate = new();
        IntPtr _handle;
        DateTime _lastCommand = DateTime.MinValue;
        public MonitorTarget Target { get; private set; }

        const int MinGapMs = 50;   // MCCS recommends >= 40-50 ms between commands
        const int Retries = 3;

        public DdcChannel(MonitorTarget target)
        {
            Target = target;
            _thread = new Thread(Run) { IsBackground = true, Name = "DDC/CI " + target.GdiDeviceName };
            _thread.Start();
        }

        void Run()
        {
            foreach (var job in _queue.GetConsumingEnumerable())
            {
                try { job(); } catch (Exception ex) { Log.Write("DDC job crashed: " + ex); }
            }
            CloseHandle();
        }

        Task<T> Enqueue<T>(Func<T> f)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_queue.IsAddingCompleted) { tcs.SetException(new ObjectDisposedException("DdcChannel")); return tcs.Task; }
            _queue.Add(() => { try { tcs.SetResult(f()); } catch (Exception ex) { tcs.SetException(ex); } });
            return tcs.Task;
        }

        public Task<VcpReply> GetAsync(byte code) => Enqueue(() => WithRetry(() => GetRaw(code), "ler 0x" + code.ToString("X2")));

        public Task<bool> SetAsync(byte code, uint value) => Enqueue(() => { WithRetry(() => { SetRaw(code, value); return true; }, "gravar 0x" + code.ToString("X2")); return true; });

        /// <summary>Coalescing write used by sliders: only the latest value per code is sent.</summary>
        public Task<bool> SetLatestAsync(byte code, uint value)
        {
            lock (_pendingGate)
            {
                bool queued = _pendingWrites.ContainsKey(code);
                _pendingWrites[code] = value;
                if (queued) return Task.FromResult(true);
            }
            return Enqueue(() =>
            {
                uint v;
                lock (_pendingGate) { v = _pendingWrites[code]; _pendingWrites.Remove(code); }
                WithRetry(() => { SetRaw(code, v); return true; }, "gravar 0x" + code.ToString("X2"));
                return true;
            });
        }

        public Task<string> GetCapabilitiesAsync() => Enqueue(() => WithRetry(() =>
        {
            EnsureHandle();
            Pace();
            if (!GetCapabilitiesStringLength(_handle, out uint len)) throw Fail("ler capacidades");
            var buf = new byte[len];
            Pace();
            if (!CapabilitiesRequestAndCapabilitiesReply(_handle, buf, len)) throw Fail("ler capacidades");
            return System.Text.Encoding.ASCII.GetString(buf).TrimEnd('\0');
        }, "ler capacidades"));

        /// <summary>Re-locates the monitor (after VRR/mode changes the HMONITOR can change) and reopens the handle.</summary>
        public Task<bool> ReconnectAsync() => Enqueue(() => { CloseHandle(); return TryRelocate(); });

        VcpReply GetRaw(byte code)
        {
            EnsureHandle();
            Pace();
            if (!GetVCPFeatureAndVCPFeatureReply(_handle, code, out _, out uint cur, out uint max))
                throw Fail("ler 0x" + code.ToString("X2"));
            return new VcpReply(cur, max);
        }

        void SetRaw(byte code, uint value)
        {
            EnsureHandle();
            Pace();
            if (!SetVCPFeature(_handle, code, value))
                throw Fail("gravar 0x" + code.ToString("X2"));
            Log.Write($"SET 0x{code:X2} = {value}");
        }

        T WithRetry<T>(Func<T> f, string what)
        {
            DdcException last = null;
            for (int i = 0; i < Retries; i++)
            {
                try { return f(); }
                catch (DdcException ex)
                {
                    last = ex;
                    Thread.Sleep(80 * (i + 1));
                    if (i == Retries - 2) { CloseHandle(); TryRelocate(); }
                }
            }
            Log.Write("DDC failure (" + what + "): " + last?.Message);
            throw last ?? new DdcException("Falha DDC/CI", 0);
        }

        void Pace()
        {
            var wait = MinGapMs - (int)(DateTime.UtcNow - _lastCommand).TotalMilliseconds;
            if (wait > 0) Thread.Sleep(wait);
            _lastCommand = DateTime.UtcNow;
        }

        void EnsureHandle()
        {
            if (_handle != IntPtr.Zero) return;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(Target.HMonitor, out uint n) || n == 0)
            {
                if (!TryRelocate() || !GetNumberOfPhysicalMonitorsFromHMONITOR(Target.HMonitor, out n) || n == 0)
                    throw new DdcException("Monitor não encontrado (desligado, desconectado ou em outra entrada).", Marshal.GetLastWin32Error());
            }
            var arr = new PHYSICAL_MONITOR[n];
            if (!GetPhysicalMonitorsFromHMONITOR(Target.HMonitor, n, arr))
                throw new DdcException("Não foi possível abrir o canal DDC/CI do monitor.", Marshal.GetLastWin32Error());
            int idx = Math.Min(Target.PhysicalIndex, (int)n - 1);
            _handle = arr[idx].hPhysicalMonitor;
            for (int i = 0; i < n; i++) if (i != idx) DestroyPhysicalMonitor(arr[i].hPhysicalMonitor);
        }

        bool TryRelocate()
        {
            foreach (var t in MonitorLocator.FindAll())
            {
                if (Target.DevicePath != null && string.Equals(t.DevicePath, Target.DevicePath, StringComparison.OrdinalIgnoreCase))
                {
                    Target = t;
                    return true;
                }
            }
            return false;
        }

        void CloseHandle()
        {
            if (_handle != IntPtr.Zero) { DestroyPhysicalMonitor(_handle); _handle = IntPtr.Zero; }
        }

        static DdcException Fail(string what)
        {
            int err = Marshal.GetLastWin32Error();
            return new DdcException($"Falha DDC/CI ao {what}: {Describe(err)}", err);
        }

        public static string Describe(int err) => unchecked((uint)err) switch
        {
            0xC0262582 => "erro de transmissão I2C",
            0xC0262583 => "erro de recepção I2C",
            0xC0262584 => "código VCP não suportado pelo monitor",
            0xC0262585 => "dados DDC/CI inválidos",
            0xC026258A => "tamanho de mensagem inválido",
            0xC026258B => "checksum inválido (barramento ocupado)",
            0xC0262589 => "comando inválido",
            0xC026258D => "monitor não suporta DDC/CI ou DDC/CI desativado no menu",
            0 => "sem resposta",
            _ => "código 0x" + unchecked((uint)err).ToString("X8")
        };

        public void Dispose()
        {
            _queue.CompleteAdding();
        }
    }
}
