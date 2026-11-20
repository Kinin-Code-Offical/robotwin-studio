using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Audio
{
    public enum Timbre
    {
        /// <summary>Piezo buzzer: square wave.</summary>
        Square,

        /// <summary>Brushed DC motor: commutator whine with harmonics and brush noise.</summary>
        Motor,

        /// <summary>Hobby servo: gear-train buzz.</summary>
        Gear,

        /// <summary>Overheating part: sizzling crackle.</summary>
        Sizzle,

        /// <summary>Pure tone.</summary>
        Sine
    }

    public enum OneShot
    {
        Click,
        Pop,
        Bang,
        Crackle,
        Spark,
        Hiss
    }

    /// <summary>
    /// Small real-time synthesizer on the Windows waveOut API (no extra packages):
    /// continuous voices keyed by name (frequency/gain glide smoothly) plus one-shot
    /// effects, mixed at 44.1 kHz stereo with a soft limiter. Every voice and one-shot can be
    /// placed in 3D (<see cref="SetSpatial"/>): per-ear gain, per-ear low-pass (head shadow,
    /// sounds from behind, air absorption) and Doppler pitch. Thread-safe setters.
    /// </summary>
    public sealed class SoundEngine : IDisposable
    {
        private const int Rate = 44100;
        private const int BufferSamples = 1536;
        private const int BufferCount = 4;

        private sealed class Voice
        {
            public Timbre Timbre;
            public double Freq, TargetFreq;
            public double Gain, TargetGain;
            public double Phase, Phase2;
            public double Filter;
            public double Load;
            public DateTime Touched;

            // [objs] stereo placement: ear gains, low-pass coefficients, Doppler (targets glide like the gain).
            public double L = 1, R = 1, TargetL = 1, TargetR = 1;
            public double CutL = 1, CutR = 1, TargetCutL = 1, TargetCutR = 1;
            public double Pitch = 1, TargetPitch = 1;
            public double ZL, ZR;
        }

        private sealed class Shot
        {
            public OneShot Kind;
            public double Gain;
            public int Pos;
            public int Length;
            public double Seed;

            // [objs] fixed stereo placement of the shot.
            public double L = 1, R = 1, CutL = 1, CutR = 1;
            public double ZL, ZR;
        }

        private readonly Dictionary<string, Voice> _voices = new Dictionary<string, Voice>(StringComparer.Ordinal);
        private readonly List<Shot> _shots = new List<Shot>();
        private readonly object _gate = new object();
        private readonly Random _rnd = new Random(7);
        private Thread? _thread;
        private volatile bool _running;
        private IntPtr _device;

        public static SoundEngine Shared { get; } = new SoundEngine();

        /// <summary>Master volume 0..1.</summary>
        public double Volume { get; set; } = 0.6;

        public bool Enabled { get; set; } = true;

        public bool IsAvailable => _device != IntPtr.Zero;

        // ================================================================ control

        /// <summary>Sets (or creates) a continuous voice. <paramref name="load"/> (0..1) roughens motor/gear timbres.</summary>
        public void SetVoice(string key, Timbre timbre, double frequency, double gain, double load = 0)
        {
            if (!Enabled) return;
            EnsureStarted();
            lock (_gate)
            {
                if (!_voices.TryGetValue(key, out var v))
                {
                    v = new Voice { Timbre = timbre, Freq = frequency };
                    _voices[key] = v;
                }

                v.Timbre = timbre;
                v.TargetFreq = Math.Max(5, Math.Min(12000, frequency));
                v.TargetGain = Math.Max(0, Math.Min(1, gain));
                v.Load = Math.Max(0, Math.Min(1, load));
                v.Touched = DateTime.UtcNow;
            }
        }

        /// <summary>Fades out every voice whose key starts with the prefix (or all).</summary>
        public void Silence(string prefix = "")
        {
            lock (_gate)
            {
                foreach (var kv in _voices)
                {
                    if (kv.Key.StartsWith(prefix, StringComparison.Ordinal)) kv.Value.TargetGain = 0;
                }
            }
        }

        /// <summary>Fades out the voices under a prefix except the listed keys.</summary>
        public void Silence(string prefix, ICollection<string> keep)
        {
            lock (_gate)
            {
                foreach (var kv in _voices)
                {
                    if (kv.Key.StartsWith(prefix, StringComparison.Ordinal) && !keep.Contains(kv.Key)) kv.Value.TargetGain = 0;
                }
            }
        }

        /// <summary>[objs] Places a continuous voice in 3D (call after <see cref="SetVoice"/>); unplaced voices play centred.</summary>
        public void SetSpatial(string key, SpatialMix mix)
        {
            lock (_gate)
            {
                if (!_voices.TryGetValue(key, out var v)) return;
                v.TargetL = mix.Left;
                v.TargetR = mix.Right;
                v.TargetCutL = Coefficient(mix.CutoffLeft);
                v.TargetCutR = Coefficient(mix.CutoffRight);
                v.TargetPitch = Math.Max(0.5, Math.Min(2, mix.Pitch));
            }
        }

        /// <summary>One-pole low-pass coefficient for a cut-off frequency.</summary>
        private static double Coefficient(double cutoff) => 1 - Math.Exp(-2 * Math.PI * Math.Max(20, Math.Min(Rate * 0.45, cutoff)) / Rate);

        public void Play(OneShot kind, double gain = 1.0) => Play(kind, gain, SpatialMix.Centre);

        /// <summary>[objs] One-shot heard from a place.</summary>
        public void Play(OneShot kind, double gain, SpatialMix mix)
        {
            if (!Enabled) return;
            EnsureStarted();
            int ms = kind switch
            {
                OneShot.Click => 40,
                OneShot.Pop => 260,
                OneShot.Bang => 900,
                OneShot.Crackle => 900,
                OneShot.Spark => 220,
                _ => 700
            };
            lock (_gate)
            {
                if (_shots.Count > 24) _shots.RemoveAt(0);
                _shots.Add(new Shot
                {
                    Kind = kind, Gain = Math.Max(0, Math.Min(1.5, gain)), Length = Rate * ms / 1000, Seed = _rnd.NextDouble(),
                    L = mix.Left, R = mix.Right, CutL = Coefficient(mix.CutoffLeft), CutR = Coefficient(mix.CutoffRight)
                });
            }
        }

        // ================================================================ synthesis

        private double[] _mixL = Array.Empty<double>(), _mixR = Array.Empty<double>();

        /// <summary>Fills an interleaved stereo buffer (L, R, L, R ...).</summary>
        private void Mix(short[] output)
        {
            int frames = output.Length / 2;
            if (_mixL.Length != frames)
            {
                _mixL = new double[frames];
                _mixR = new double[frames];
            }

            var mixL = _mixL;
            var mixR = _mixR;
            Array.Clear(mixL, 0, frames);
            Array.Clear(mixR, 0, frames);
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                var dead = new List<string>();
                foreach (var kv in _voices)
                {
                    var v = kv.Value;
                    // A voice nobody updates for half a second fades away (sim paused, object deleted …).
                    if ((now - v.Touched).TotalSeconds > 0.5) v.TargetGain = 0;
                    if (v.Gain < 1e-4 && v.TargetGain < 1e-4)
                    {
                        dead.Add(kv.Key);
                        continue;
                    }

                    Render(v, mixL, mixR);
                }

                foreach (var k in dead) _voices.Remove(k);
                for (int i = _shots.Count - 1; i >= 0; i--)
                {
                    if (RenderShot(_shots[i], mixL, mixR)) _shots.RemoveAt(i);
                }
            }

            double master = Volume;
            for (int i = 0; i < frames; i++)
            {
                output[2 * i] = (short)(Math.Tanh(mixL[i] * master * 1.2) * 30000);   // soft limiter
                output[2 * i + 1] = (short)(Math.Tanh(mixR[i] * master * 1.2) * 30000);
            }
        }

        private double Noise() => _rnd.NextDouble() * 2 - 1;

        private void Render(Voice v, double[] mixL, double[] mixR)
        {
            double dt = 1.0 / Rate;
            for (int i = 0; i < mixL.Length; i++)
            {
                v.Freq += (v.TargetFreq - v.Freq) * 0.0015;
                v.Gain += (v.TargetGain - v.Gain) * 0.002;
                v.L += (v.TargetL - v.L) * 0.002;
                v.R += (v.TargetR - v.R) * 0.002;
                v.CutL += (v.TargetCutL - v.CutL) * 0.002;
                v.CutR += (v.TargetCutR - v.CutR) * 0.002;
                v.Pitch += (v.TargetPitch - v.Pitch) * 0.0015;
                v.Phase += v.Freq * v.Pitch * dt;
                if (v.Phase >= 1) v.Phase -= Math.Floor(v.Phase);
                double s;
                switch (v.Timbre)
                {
                    case Timbre.Square:
                        s = (v.Phase < 0.5 ? 1 : -1) * 0.35;
                        break;
                    case Timbre.Sine:
                        s = Math.Sin(2 * Math.PI * v.Phase) * 0.5;
                        break;
                    case Timbre.Motor:
                    {
                        // Commutator whine (saw) + second harmonic + brush noise; load makes it harsher and adds a low growl.
                        double saw = 2 * v.Phase - 1;
                        v.Phase2 += v.Freq * 0.5 * dt;
                        if (v.Phase2 >= 1) v.Phase2 -= 1;
                        double growl = Math.Sin(2 * Math.PI * v.Phase2) * (0.25 + 0.6 * v.Load);
                        v.Filter += (Noise() - v.Filter) * 0.15;
                        s = (saw * 0.22 + Math.Sin(4 * Math.PI * v.Phase) * 0.12 + growl * 0.25 + v.Filter * (0.08 + 0.25 * v.Load)) * 0.9;
                        break;
                    }

                    case Timbre.Gear:
                    {
                        double tri = 4 * Math.Abs(v.Phase - 0.5) - 1;
                        v.Phase2 += 31 * dt;
                        if (v.Phase2 >= 1) v.Phase2 -= 1;
                        v.Filter += (Noise() - v.Filter) * 0.3;
                        s = (tri * 0.25 + v.Filter * 0.18) * (0.7 + 0.3 * Math.Sin(2 * Math.PI * v.Phase2));
                        break;
                    }

                    default:
                    {
                        // Sizzle: high-passed noise with random crackles.
                        double n = Noise();
                        double hp = n - v.Filter;
                        v.Filter += (n - v.Filter) * 0.1;
                        s = hp * 0.12 + (_rnd.NextDouble() < 0.0006 * (1 + 4 * v.Load) ? Noise() * 0.8 : 0);
                        break;
                    }
                }

                s *= v.Gain;
                v.ZL += (s * v.L - v.ZL) * v.CutL;
                v.ZR += (s * v.R - v.ZR) * v.CutR;
                mixL[i] += v.ZL;
                mixR[i] += v.ZR;
            }
        }

        /// <summary>Adds one buffer of a one-shot; true when it has finished.</summary>
        private bool RenderShot(Shot sh, double[] mixL, double[] mixR)
        {
            for (int i = 0; i < mixL.Length && sh.Pos < sh.Length; i++, sh.Pos++)
            {
                double t = sh.Pos / (double)Rate, k = sh.Pos / (double)sh.Length;
                double s;
                switch (sh.Kind)
                {
                    case OneShot.Click:
                        // Relay armature hitting its stop: sharp tick + 2.8 kHz ring.
                        s = Math.Exp(-t * 400) * (Noise() * 0.6 + Math.Sin(2 * Math.PI * 2800 * t) * 0.5);
                        break;
                    case OneShot.Pop:
                        // Component bursting: low thump + noise burst.
                        s = Math.Exp(-t * 18) * Math.Sin(2 * Math.PI * (140 - 80 * k) * t) * 0.9 + Math.Exp(-t * 40) * Noise() * 0.6;
                        break;
                    case OneShot.Bang:
                        // Battery venting / explosion: loud low-passed noise with a boom.
                        s = Math.Exp(-t * 5) * Noise() * 0.8 * (1 - k) + Math.Exp(-t * 7) * Math.Sin(2 * Math.PI * 55 * t) * 1.0;
                        break;
                    case OneShot.Spark:
                        s = Math.Exp(-t * 25) * Noise() * (Math.Sin(2 * Math.PI * 6000 * t) > 0 ? 0.7 : 0.3);
                        break;
                    case OneShot.Crackle:
                        s = _rnd.NextDouble() < 0.004 * (1 - k) ? Noise() * 0.9 : Noise() * 0.03 * (1 - k);
                        break;
                    default:
                        s = Noise() * 0.25 * (1 - k) * Math.Min(1, t * 20);
                        break;
                }

                s *= sh.Gain;
                sh.ZL += (s * sh.L - sh.ZL) * sh.CutL;
                sh.ZR += (s * sh.R - sh.ZR) * sh.CutR;
                mixL[i] += sh.ZL;
                mixR[i] += sh.ZR;
            }

            return sh.Pos >= sh.Length;
        }

        // ================================================================ waveOut

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID, ref WaveFormatEx lpFormat, IntPtr dwCallback, IntPtr dwInstance, int dwFlags);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hWaveOut);

        // WAVEHDR (x64): lpData 8, dwBufferLength 4, dwBytesRecorded 4, dwUser 8, dwFlags 4, dwLoops 4, lpNext 8, reserved 8.
        private static readonly int HeaderSize = IntPtr.Size == 8 ? 48 : 32;
        private static readonly int FlagsOffset = IntPtr.Size == 8 ? 24 : 16;
        private const int WhdrDone = 1;

        private void EnsureStarted()
        {
            if (_thread != null) return;
            lock (_gate)
            {
                if (_thread != null) return;
                var fmt = new WaveFormatEx { wFormatTag = 1, nChannels = 2, nSamplesPerSec = Rate, wBitsPerSample = 16, nBlockAlign = 4, nAvgBytesPerSec = Rate * 4 };   // [objs] stereo
                try
                {
                    if (waveOutOpen(out _device, -1, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) _device = IntPtr.Zero;
                }
                catch (DllNotFoundException)
                {
                    _device = IntPtr.Zero;
                }

                _running = _device != IntPtr.Zero;
                _thread = new Thread(Loop) { IsBackground = true, Name = "RobotWin audio", Priority = ThreadPriority.AboveNormal };
                if (_running) _thread.Start();
            }
        }

        private void Loop()
        {
            var headers = new IntPtr[BufferCount];
            var data = new IntPtr[BufferCount];
            var samples = new short[BufferSamples * 2];   // [objs] interleaved stereo frames
            for (int b = 0; b < BufferCount; b++)
            {
                data[b] = Marshal.AllocHGlobal(samples.Length * 2);
                headers[b] = Marshal.AllocHGlobal(HeaderSize);
                for (int i = 0; i < HeaderSize; i++) Marshal.WriteByte(headers[b], i, 0);
                Marshal.WriteIntPtr(headers[b], 0, data[b]);
                Marshal.WriteInt32(headers[b], IntPtr.Size, samples.Length * 2);
                Marshal.WriteInt32(headers[b], FlagsOffset, WhdrDone);   // free to fill
            }

            bool[] prepared = new bool[BufferCount];
            try
            {
                while (_running)
                {
                    bool wrote = false;
                    for (int b = 0; b < BufferCount; b++)
                    {
                        int flags = Marshal.ReadInt32(headers[b], FlagsOffset);
                        if ((flags & WhdrDone) == 0) continue;
                        if (prepared[b]) waveOutUnprepareHeader(_device, headers[b], HeaderSize);
                        if (Enabled) Mix(samples);
                        else Array.Clear(samples, 0, samples.Length);
                        Marshal.Copy(samples, 0, data[b], samples.Length);
                        Marshal.WriteInt32(headers[b], FlagsOffset, 0);
                        waveOutPrepareHeader(_device, headers[b], HeaderSize);
                        prepared[b] = true;
                        waveOutWrite(_device, headers[b], HeaderSize);
                        wrote = true;
                    }

                    if (!wrote) Thread.Sleep(4);
                }
            }
            finally
            {
                waveOutReset(_device);
                for (int b = 0; b < BufferCount; b++)
                {
                    if (prepared[b]) waveOutUnprepareHeader(_device, headers[b], HeaderSize);
                    Marshal.FreeHGlobal(headers[b]);
                    Marshal.FreeHGlobal(data[b]);
                }

                waveOutClose(_device);
                _device = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            _running = false;
            _thread?.Join(500);
        }
    }
}
