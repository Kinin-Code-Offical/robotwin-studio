using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace RobotTwin.CoreSim.Firmware.Esp32
{
    /// <summary>A loaded sketch: in the app's process (<see cref="Esp32SketchModule"/>) or in one of its own (<see cref="Esp32SketchProcess"/>).</summary>
    internal interface IEsp32SketchModule : IDisposable
    {
        int Init(ref Esp32Abi.HostApi api, ref Esp32Abi.Config config);
        int Start(ulong startNs);
        int RunUntil(ulong timeNs);
        void Stop();
        void PinChanged(int gpio, int level, ulong timeNs);
        int SerialRx(int port, byte[] data, int length);
        ulong Now();
        string StatusMessage();
        (long Heap, long Stacks)? Memory();
    }

    /// <summary>[isolation] How a running sketch is kept apart from the app.</summary>
    public enum Esp32Isolation
    {
        /// <summary>In the app's own process (no sketch host, or it could not start).</summary>
        InProcess,

        /// <summary>In a process of its own, in a job: a memory limit, no child processes, no desktop; it ends with the app.</summary>
        Process,

        /// <summary>As <see cref="Process"/>, and in an AppContainer: no network, none of the user's files, no other program's objects.</summary>
        Sandbox
    }

    /// <summary>[isolation] Where the sketch host of a compiled sketch is.</summary>
    public static class Esp32SketchHost
    {
        public const string FileName = "rw_sketch_host.exe";

        /// <summary>Beside each sketch DLL: a text file naming the host it was built with.</summary>
        public const string SidecarExtension = ".host";

        /// <summary>The sketch host a DLL was built with, or null (an older build: it runs in the app's process).</summary>
        public static string? Find(string dllPath)
        {
            try
            {
                string sidecar = dllPath + SidecarExtension;
                if (!File.Exists(sidecar)) return null;
                string host = File.ReadAllText(sidecar).Trim();
                return host.Length > 0 && File.Exists(host) ? host : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return null;
            }
        }
    }

    /// <summary>[isolation] The sketch host could not be started or stopped answering; the sketch can still run in the app's process.</summary>
    public sealed class Esp32IsolationException : InvalidOperationException
    {
        public Esp32IsolationException(string message) : base(message)
        {
        }
    }

    /// <summary>The app's side of rw_host_api: what the sketch's calls reach.</summary>
    internal sealed class Esp32Callbacks
    {
        public Esp32Abi.LogFn Log = null!;
        public Esp32Abi.PinModeFn PinMode = null!;
        public Esp32Abi.DigitalWriteFn DigitalWrite = null!;
        public Esp32Abi.DigitalReadFn DigitalRead = null!;
        public Esp32Abi.AnalogReadFn AnalogRead = null!;
        public Esp32Abi.PwmWriteFn PwmWrite = null!;
        public Esp32Abi.DacWriteFn DacWrite = null!;
        public Esp32Abi.TouchReadFn TouchRead = null!;
        public Esp32Abi.SerialBeginFn SerialBegin = null!;
        public Esp32Abi.SerialWriteFn SerialWrite = null!;
        public Esp32Abi.I2cStartFn I2cStart = null!;
        public Esp32Abi.I2cWriteFn I2cWrite = null!;
        public Esp32Abi.I2cReadFn I2cRead = null!;
        public Esp32Abi.I2cStopFn I2cStop = null!;
        public Esp32Abi.SystemEventFn SystemEvent = null!;
        public Esp32Abi.RadioRequestFn RadioRequest = null!;
    }

    /// <summary>
    /// [isolation] A sketch in a process of its own (native/esp32hal/host/rw_sketch_host.cpp): a sketch that crashes the
    /// process, hangs it or leaks memory takes down only its host, and in the sandbox it cannot reach the network or the
    /// user's files. The host and the app share one section: the sketch's writes are queued with its time and taken here,
    /// in order, at each exchange; its pin reads come from a snapshot written before each command (the circuit does not
    /// move while the sketch runs a slice); I²C bytes and radio / SPI / LED requests are answered one by one. Everything
    /// the host writes is checked before it is used: the sketch is not trusted.
    /// </summary>
    internal sealed class Esp32SketchProcess : IEsp32SketchModule
    {
        // ---- the section's layout (rw_sketch_host.cpp)
        private const uint Magic = 0x31585752;   // "RWX1"
        private const uint LayoutVersion = 1;
        private const int Gpios = 48;
        private const int OffDown = 64, OffUp = 128, OffLevel = 192, OffTouch = 256, OffVolts = 448, OffQueueUsed = 832, OffDownData = 1024;
        private const int DataSize = 65536;
        private const int OffUpData = OffDownData + DataSize;
        private const int OffQueue = OffUpData + DataSize;
        private const int QueueSize = 262144;
        private const int SectionSize = OffQueue + QueueSize;
        private const int RecordSize = 48;

        // packet fields
        private const int POp = 0, PI0 = 4, PI1 = 8, PI2 = 12, PL0 = 16, PL1 = 24, PU0 = 32, PLen = 56, PCap = 60;

        private const int CmdInit = 1, CmdStart = 2, CmdRunUntil = 3, CmdStop = 4, CmdPinChanged = 5, CmdSerialRx = 6, CmdStatus = 8, CmdMemory = 9, CmdExit = 10;
        private const int Ack = 200;
        private const int UpDone = 100, UpDrain = 101, UpI2cStart = 102, UpI2cWrite = 103, UpI2cRead = 104, UpRadio = 105, UpReady = 106;
        private const int RecLog = 1, RecPinMode = 2, RecDigitalWrite = 3, RecPwm = 4, RecDac = 5, RecSerialBegin = 6, RecSerialWrite = 7, RecSystemEvent = 8, RecI2cStop = 9;

        /// <summary>A command that has not finished in this long (wall clock) means the host hangs beyond the sketch's own watchdog.</summary>
        private const int CommandTimeoutMs = 60000;

        /// <summary>The most memory the sketch's process may commit (a real ESP32 has 520 KB; this is only a guard).</summary>
        private const long MemoryLimit = 1L << 30;

        private const string AppContainerName = "RobotWin.Esp32Sketch";

        private readonly Esp32Callbacks _cb;
        private IntPtr _section, _view, _hostEvent, _job;
        private NativeWaitHandle? _upEvent, _process;
        private WaitHandle[] _waits = Array.Empty<WaitHandle>();
        private ulong _now;
        private bool _dead;
        private string _deathMessage = string.Empty;

        public Esp32Isolation Isolation { get; }

        /// <summary>Why the sketch is not in the sandbox although it was asked for (null when it is, or was not asked).</summary>
        public string? SandboxNote { get; }

        public Esp32SketchProcess(string dllPath, string hostPath, Esp32Callbacks callbacks, bool sandbox)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8)
                throw new Esp32IsolationException("çizim süreci yalnızca Windows x64'te çalışır");
            _cb = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
            string dll = Path.GetFullPath(dllPath);
            string host = Path.GetFullPath(hostPath);
            try
            {
                var inheritable = new Native.SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<Native.SECURITY_ATTRIBUTES>(), bInheritHandle = 1 };
                _section = Native.CreateFileMappingW(new IntPtr(-1), ref inheritable, Native.PAGE_READWRITE, 0, SectionSize, null);
                if (_section == IntPtr.Zero) throw Fail("paylaşılan bellek ayrılamadı");
                _view = Native.MapViewOfFile(_section, Native.FILE_MAP_ALL_ACCESS, 0, 0, new UIntPtr(SectionSize));
                if (_view == IntPtr.Zero) throw Fail("paylaşılan bellek eşlenemedi");
                Marshal.WriteInt32(_view, 0, unchecked((int)Magic));
                Marshal.WriteInt32(_view, 4, (int)LayoutVersion);
                IntPtr up = Native.CreateEventW(ref inheritable, false, false, null);
                if (up == IntPtr.Zero) throw Fail("olay nesnesi oluşturulamadı");
                _upEvent = new NativeWaitHandle(up);
                _hostEvent = Native.CreateEventW(ref inheritable, false, false, null);
                if (_hostEvent == IntPtr.Zero) throw Fail("olay nesnesi oluşturulamadı");

                _job = CreateJob();
                string cmd = "\"" + host + "\" \"" + dll + "\" " + (ulong)_section.ToInt64() + " " + (ulong)up.ToInt64() + " " + (ulong)_hostEvent.ToInt64();
                var handles = new[] { _section, up, _hostEvent };
                IntPtr process = IntPtr.Zero;
                if (sandbox)
                {
                    try
                    {
                        IntPtr sid = AppContainerSid();
                        Grant(host, sid);
                        Grant(dll, sid);
                        process = Launch(host, cmd, handles, sid);
                        Isolation = Esp32Isolation.Sandbox;
                    }
                    catch (Esp32IsolationException ex)
                    {
                        SandboxNote = ex.Message;
                    }
                }

                if (process == IntPtr.Zero)
                {
                    process = Launch(host, cmd, handles, IntPtr.Zero);
                    Isolation = Esp32Isolation.Process;
                }

                _process = new NativeWaitHandle(process);
                _waits = new WaitHandle[] { _upEvent, _process };

                // The host loads the sketch and says whether it could.
                if (!WaitUp(10000)) throw new Esp32IsolationException("çizim süreci başlamadı: " + _deathMessage);
                int op = Marshal.ReadInt32(_view, OffUp + POp);
                long status = Marshal.ReadInt64(_view, OffUp + PL0);
                long detail = Marshal.ReadInt64(_view, OffUp + PL1);
                if (op != UpReady) throw new Esp32IsolationException("çizim süreci beklenmeyen bir yanıt verdi");
                if (status == -1) throw new Esp32IsolationException("çizim süreci derlenmiş kodu yükleyemedi (Win32 hata kodu " + detail + ")");
                if (status != 0) throw new InvalidOperationException("Derlenmiş ESP32 kodunda gerekli işlevler yok; kod RobotWin'in ESP32 katmanıyla derlenmemiş olabilir, yeniden derleyin.");
                if (detail != Esp32Abi.Version)
                    throw new InvalidOperationException($"Derlenmiş ESP32 kodu bu sürümle uyumsuz (ABI {detail}, beklenen {Esp32Abi.Version}); kodu yeniden derleyin.");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        // ------------------------------------------------------------------ commands

        public int Init(ref Esp32Abi.HostApi api, ref Esp32Abi.Config config)
        {
            if (_dead) return -100;
            int size = Marshal.SizeOf<Esp32Abi.Config>();
            Marshal.StructureToPtr(config, _view + OffDownData, false);
            return Command(CmdInit, len: size) ? (int)Marshal.ReadInt64(_view, OffUp + PL0) : -100;
        }

        public int Start(ulong startNs) => Command(CmdStart, u0: startNs) ? (int)Marshal.ReadInt64(_view, OffUp + PL0) : -100;

        public int RunUntil(ulong timeNs) => Command(CmdRunUntil, u0: timeNs) ? (int)Marshal.ReadInt64(_view, OffUp + PL0) : Esp32Abi.StatusCrashed;

        public void Stop() => Command(CmdStop);

        public void PinChanged(int gpio, int level, ulong timeNs) => Command(CmdPinChanged, gpio, level, timeNs);

        public int SerialRx(int port, byte[] data, int length)
        {
            if (_dead || data == null) return 0;
            int n = Math.Max(0, Math.Min(Math.Min(length, data.Length), DataSize));
            Marshal.Copy(data, 0, _view + OffDownData, n);
            return Command(CmdSerialRx, port, len: n) ? (int)Marshal.ReadInt64(_view, OffUp + PL0) : 0;
        }

        /// <summary>The sketch's time: of the write or request being handled, otherwise where the last command left it.</summary>
        public ulong Now() => _now;

        public string StatusMessage()
        {
            if (_dead) return _deathMessage;
            const int cap = 4096;
            if (!Command(CmdStatus, cap: cap, snapshot: false)) return _deathMessage;
            int n = (int)Math.Max(0, Math.Min(Marshal.ReadInt64(_view, OffUp + PL0), cap));
            if (n == 0) return string.Empty;
            var bytes = new byte[n];
            Marshal.Copy(_view + OffUpData, bytes, 0, n);
            int end = Array.IndexOf(bytes, (byte)0);
            return Encoding.UTF8.GetString(bytes, 0, end >= 0 ? end : n);
        }

        public (long Heap, long Stacks)? Memory()
        {
            if (!Command(CmdMemory, snapshot: false) || Marshal.ReadInt32(_view, OffUp + PI0) != 1) return null;
            return (Math.Max(0, Marshal.ReadInt64(_view, OffUp + PL0)), Math.Max(0, Marshal.ReadInt64(_view, OffUp + PL1)));
        }

        private bool Command(int op, int i0 = 0, int i1 = 0, ulong u0 = 0, int len = 0, int cap = 0, bool snapshot = true)
        {
            if (_dead) return false;
            if (snapshot) Snapshot();
            IntPtr p = _view + OffDown;
            Marshal.WriteInt32(p, POp, op);
            Marshal.WriteInt32(p, PI0, i0);
            Marshal.WriteInt32(p, PI1, i1);
            Marshal.WriteInt64(p, PU0, unchecked((long)u0));
            Marshal.WriteInt32(p, PLen, len);
            Marshal.WriteInt32(p, PCap, cap);
            Thread.MemoryBarrier();
            Native.SetEvent(_hostEvent);
            return Serve();
        }

        /// <summary>The pins as the sketch reads them during the command: the circuit stands still meanwhile.</summary>
        private void Snapshot()
        {
            int n = Math.Min(Gpios, Esp32Gpio.Count);
            for (int g = 0; g < n; g++)
            {
                Marshal.WriteByte(_view, OffLevel + g, (byte)(_cb.DigitalRead(IntPtr.Zero, g) != 0 ? 1 : 0));
                Marshal.WriteInt32(_view, OffTouch + 4 * g, _cb.TouchRead(IntPtr.Zero, g));
                Marshal.WriteInt64(_view, OffVolts + 8 * g, BitConverter.DoubleToInt64Bits(_cb.AnalogRead(IntPtr.Zero, g)));
            }
        }

        /// <summary>Answers the host until it reports the command done (false: it died or hung).</summary>
        private bool Serve()
        {
            while (true)
            {
                if (!WaitUp(CommandTimeoutMs)) return false;
                TakeQueue();
                IntPtr p = _view + OffUp;
                int op = Marshal.ReadInt32(p, POp);
                _now = unchecked((ulong)Marshal.ReadInt64(p, PU0));
                int i0 = Marshal.ReadInt32(p, PI0), i1 = Marshal.ReadInt32(p, PI1), i2 = Marshal.ReadInt32(p, PI2);
                switch (op)
                {
                    case UpDone:
                        return true;
                    case UpDrain:
                        Answer(0);
                        break;
                    case UpI2cStart:
                        Answer(_cb.I2cStart(IntPtr.Zero, i0, i1, i2));
                        break;
                    case UpI2cWrite:
                        Answer(_cb.I2cWrite(IntPtr.Zero, i0, i1));
                        break;
                    case UpI2cRead:
                        Answer(_cb.I2cRead(IntPtr.Zero, i0, i1));
                        break;
                    case UpRadio:
                    {
                        int len = Clamp(Marshal.ReadInt32(p, PLen), DataSize);
                        int cap = Clamp(Marshal.ReadInt32(p, PCap), DataSize);
                        Answer(_cb.RadioRequest(IntPtr.Zero, i0, _view + OffUpData, len, cap > 0 ? _view + OffDownData : IntPtr.Zero, cap));
                        break;
                    }
                    default:
                        Kill("çizim süreci anlaşılmayan bir istek gönderdi (" + op + ")");
                        return false;
                }
            }
        }

        private static int Clamp(int value, int max) => value < 0 ? 0 : value > max ? max : value;

        private void Answer(long value)
        {
            IntPtr p = _view + OffDown;
            Marshal.WriteInt32(p, POp, Ack);
            Marshal.WriteInt64(p, PL0, value);
            Thread.MemoryBarrier();
            Native.SetEvent(_hostEvent);
        }

        /// <summary>The sketch's writes since the last exchange, in the order it made them, each at its own time.</summary>
        private void TakeQueue()
        {
            int used = Clamp(Marshal.ReadInt32(_view, OffQueueUsed), QueueSize);
            int at = 0;
            while (at + RecordSize <= used)
            {
                IntPtr r = _view + OffQueue + at;
                int op = Marshal.ReadInt32(r, 0), i0 = Marshal.ReadInt32(r, 4), i1 = Marshal.ReadInt32(r, 8);
                int len = Marshal.ReadInt32(r, 12);
                if (len < 0 || len > used - at - RecordSize) break;   // damaged: the rest is dropped
                long l0 = Marshal.ReadInt64(r, 16);
                _now = unchecked((ulong)Marshal.ReadInt64(r, 24));
                double d0 = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(r, 32));
                double d1 = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(r, 40));
                IntPtr data = r + RecordSize;
                switch (op)
                {
                    case RecLog:
                        if (len > 0)
                        {
                            Marshal.WriteByte(data, len - 1, 0);   // ends where its length says
                            _cb.Log(IntPtr.Zero, i0, data);
                        }

                        break;
                    case RecPinMode:
                        _cb.PinMode(IntPtr.Zero, i0, i1);
                        break;
                    case RecDigitalWrite:
                        _cb.DigitalWrite(IntPtr.Zero, i0, i1);
                        break;
                    case RecPwm:
                        _cb.PwmWrite(IntPtr.Zero, i0, d0, d1);
                        break;
                    case RecDac:
                        _cb.DacWrite(IntPtr.Zero, i0, d0);
                        break;
                    case RecSerialBegin:
                        _cb.SerialBegin(IntPtr.Zero, i0, unchecked((uint)l0));
                        break;
                    case RecSerialWrite:
                        if (len > 0) _cb.SerialWrite(IntPtr.Zero, i0, data, len);
                        break;
                    case RecSystemEvent:
                        _cb.SystemEvent(IntPtr.Zero, i0, l0);
                        break;
                    case RecI2cStop:
                        _cb.I2cStop(IntPtr.Zero, i0);
                        break;
                }

                at += RecordSize + ((len + 7) & ~7);
            }

            Marshal.WriteInt32(_view, OffQueueUsed, 0);
        }

        /// <summary>
        /// [perf] How long the app polls the host's answer before sleeping on it: an exchange (a slice, an I²C byte, a radio
        /// or LED request) is usually answered within microseconds, and a sleep and a wake-up cost a thread switch each, which
        /// thousands of exchanges a simulated second turned into most of an ESP32 sketch's cost in its own process.
        /// </summary>
        private static readonly long SpinTicks = System.Diagnostics.Stopwatch.Frequency / 20_000;   // 50 µs

        private bool WaitUp(int timeoutMs)
        {
            if (_dead || _process == null) return false;
            if (Environment.ProcessorCount > 1 && _upEvent != null)
            {
                long until = System.Diagnostics.Stopwatch.GetTimestamp() + SpinTicks;
                int spins = 0;
                do
                {
                    if (_upEvent.WaitOne(0))
                    {
                        Thread.MemoryBarrier();
                        return true;
                    }

                    // never sleeps (SpinWait's own Sleep(1) would cost a millisecond): spins, and now and then gives the core away
                    if ((++spins & 15) == 0) Thread.Yield();
                    else Thread.SpinWait(20);
                }
                while (System.Diagnostics.Stopwatch.GetTimestamp() < until);
            }

            int which = WaitHandle.WaitAny(_waits, timeoutMs);
            if (which == 0)
            {
                Thread.MemoryBarrier();
                return true;
            }

            if (which == WaitHandle.WaitTimeout)
            {
                Kill("çizim süreci " + timeoutMs / 1000 + " s yanıt vermedi ve durduruldu");
                return false;
            }

            uint code = Native.GetExitCodeProcess(_process.Raw, out uint c) ? c : 0;
            _dead = true;
            _deathMessage = "(simülatör: çizimin süreci beklenmedik biçimde kapandı: " + ExitReason(code) + ")";
            return false;
        }

        private static string ExitReason(uint code)
        {
            switch (code)
            {
                case 0xC0000005: return "bellek erişim hatası (0xC0000005)";
                case 0xC00000FD: return "yığın taştı (0xC00000FD)";
                case 0xC0000409: return "yığın arabelleği taştı (0xC0000409)";
                case 0xC0000017: case 0xC000012D: return "bellek sınırı aşıldı (0x" + code.ToString("X8") + ")";
                case 0xC0000374: return "yığın (heap) bozuldu (0xC0000374)";
                default: return "çıkış kodu " + (code >= 0x80000000 ? "0x" + code.ToString("X8") : code.ToString());
            }
        }

        private void Kill(string why)
        {
            _dead = true;
            _deathMessage = "(simülatör: " + why + ")";
            if (_process != null) Native.TerminateProcess(_process.Raw, 1);
        }

        public void Dispose()
        {
            if (_process != null && !_dead && _view != IntPtr.Zero)
            {
                Marshal.WriteInt32(_view, OffDown + POp, CmdExit);
                Thread.MemoryBarrier();
                Native.SetEvent(_hostEvent);
                if (!_process.WaitOne(2000)) Native.TerminateProcess(_process.Raw, 1);
            }

            _dead = true;
            if (_job != IntPtr.Zero)
            {
                Native.CloseHandle(_job);   // ends whatever is left of the host
                _job = IntPtr.Zero;
            }

            _process?.Dispose();
            _process = null;
            _upEvent?.Dispose();
            _upEvent = null;
            if (_hostEvent != IntPtr.Zero) Native.CloseHandle(_hostEvent);
            _hostEvent = IntPtr.Zero;
            if (_view != IntPtr.Zero) Native.UnmapViewOfFile(_view);
            _view = IntPtr.Zero;
            if (_section != IntPtr.Zero) Native.CloseHandle(_section);
            _section = IntPtr.Zero;
        }

        // ------------------------------------------------------------------ starting the host

        private static Esp32IsolationException Fail(string what) => new Esp32IsolationException(what + " (Win32 hata kodu " + Marshal.GetLastWin32Error() + ")");

        /// <summary>The job the host runs in: killed with the app, one process only, a memory limit, no desktop or clipboard.</summary>
        private static IntPtr CreateJob()
        {
            IntPtr job = Native.CreateJobObjectW(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw Fail("iş nesnesi oluşturulamadı");
            var info = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | Native.JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION |
                                                    Native.JOB_OBJECT_LIMIT_ACTIVE_PROCESS | Native.JOB_OBJECT_LIMIT_PROCESS_MEMORY;
            info.BasicLimitInformation.ActiveProcessLimit = 1;
            info.ProcessMemoryLimit = new UIntPtr((ulong)MemoryLimit);
            if (!Native.SetInformationJobObject(job, Native.JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf<Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
            {
                Native.CloseHandle(job);
                throw Fail("iş nesnesinin sınırları kurulamadı");
            }

            int ui = Native.JOB_OBJECT_UILIMIT_ALL;
            Native.SetInformationJobObject(job, Native.JobObjectBasicUIRestrictions, ref ui, 4);
            return job;
        }

        private IntPtr Launch(string host, string commandLine, IntPtr[] handles, IntPtr appContainerSid)
        {
            IntPtr size = IntPtr.Zero;
            Native.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            IntPtr list = Marshal.AllocHGlobal(size);
            IntPtr handleArray = Marshal.AllocHGlobal(IntPtr.Size * handles.Length);
            IntPtr token = IntPtr.Zero;
            bool listReady = false;
            try
            {
                if (!Native.InitializeProcThreadAttributeList(list, 1, 0, ref size)) throw Fail("süreç öznitelikleri hazırlanamadı");
                listReady = true;
                for (int i = 0; i < handles.Length; i++) Marshal.WriteIntPtr(handleArray, i * IntPtr.Size, handles[i]);
                // Only these three handles reach the host.
                if (!Native.UpdateProcThreadAttribute(list, 0, new IntPtr(Native.PROC_THREAD_ATTRIBUTE_HANDLE_LIST), handleArray,
                        new IntPtr(IntPtr.Size * handles.Length), IntPtr.Zero, IntPtr.Zero))
                    throw Fail("süreç öznitelikleri hazırlanamadı");
                if (appContainerSid != IntPtr.Zero) token = LowBoxToken(appContainerSid);

                var si = new Native.STARTUPINFOEX();
                si.StartupInfo.cb = Marshal.SizeOf<Native.STARTUPINFOEX>();
                si.lpAttributeList = list;
                var cmd = new StringBuilder(commandLine);
                string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                const uint flags = Native.EXTENDED_STARTUPINFO_PRESENT | Native.CREATE_SUSPENDED | Native.CREATE_NO_WINDOW;
                Native.PROCESS_INFORMATION pi;
                bool started = token != IntPtr.Zero
                    ? Native.CreateProcessAsUserW(token, host, cmd, IntPtr.Zero, IntPtr.Zero, true, flags, IntPtr.Zero, system, ref si, out pi)
                    : Native.CreateProcessW(host, cmd, IntPtr.Zero, IntPtr.Zero, true, flags, IntPtr.Zero, system, ref si, out pi);
                if (!started) throw Fail(token != IntPtr.Zero ? "korumalı alanda (AppContainer) süreç başlatılamadı" : "çizim süreci başlatılamadı");
                if (!Native.AssignProcessToJobObject(_job, pi.hProcess))
                {
                    var error = Fail("çizim süreci iş nesnesine alınamadı");
                    Native.TerminateProcess(pi.hProcess, 1);
                    Native.CloseHandle(pi.hThread);
                    Native.CloseHandle(pi.hProcess);
                    throw error;
                }

                Native.ResumeThread(pi.hThread);
                Native.CloseHandle(pi.hThread);
                return pi.hProcess;
            }
            finally
            {
                if (listReady) Native.DeleteProcThreadAttributeList(list);
                Marshal.FreeHGlobal(list);
                Marshal.FreeHGlobal(handleArray);
                if (token != IntPtr.Zero) Native.CloseHandle(token);
            }
        }

        private static readonly object DirectoryGate = new object();
        private static IntPtr _directory;

        /// <summary>
        /// An AppContainer ("lowbox") token from the app's own, with no capabilities: what CreateProcess makes for a registered
        /// AppContainer profile, made here so nothing is registered on the PC (as Chromium's sandbox does). The container's
        /// named-object directory is the app's, kept open for its lifetime.
        /// </summary>
        private static IntPtr LowBoxToken(IntPtr sid)
        {
            IntPtr directory = LowBoxDirectory(sid);
            if (!Native.OpenProcessToken(Native.GetCurrentProcess(), Native.TOKEN_DUPLICATE | Native.TOKEN_QUERY | Native.TOKEN_ASSIGN_PRIMARY | Native.TOKEN_ADJUST_DEFAULT,
                    out IntPtr own))
                throw Fail("uygulamanın erişim belirteci açılamadı");
            try
            {
                var attributes = new Native.OBJECT_ATTRIBUTES { Length = Marshal.SizeOf<Native.OBJECT_ATTRIBUTES>() };
                var keep = directory != IntPtr.Zero ? new[] { directory } : Array.Empty<IntPtr>();
                int status = Native.NtCreateLowBoxToken(out IntPtr token, own, Native.TOKEN_ALL_ACCESS, ref attributes, sid, 0, IntPtr.Zero, (uint)keep.Length,
                    keep.Length > 0 ? keep : null);
                if (status != 0) throw new Esp32IsolationException("korumalı alan belirteci oluşturulamadı (NTSTATUS 0x" + status.ToString("X8") + ")");
                return token;
            }
            finally
            {
                Native.CloseHandle(own);
            }
        }

        /// <summary>\Sessions\&lt;n&gt;\AppContainerNamedObjects\&lt;sid&gt;: where the container's named objects would live (zero when it cannot be made).</summary>
        private static IntPtr LowBoxDirectory(IntPtr sid)
        {
            lock (DirectoryGate)
            {
                if (_directory != IntPtr.Zero) return _directory;
                if (!Native.ConvertSidToStringSidW(sid, out IntPtr text)) return IntPtr.Zero;
                string sidText = Marshal.PtrToStringUni(text) ?? string.Empty;
                Native.LocalFree(text);
                Native.ProcessIdToSessionId((uint)System.Diagnostics.Process.GetCurrentProcess().Id, out uint session);
                string path = "\\Sessions\\" + session + "\\AppContainerNamedObjects\\" + sidText;
                IntPtr buffer = Marshal.StringToHGlobalUni(path);
                IntPtr name = Marshal.AllocHGlobal(Marshal.SizeOf<Native.UNICODE_STRING>());
                try
                {
                    Marshal.StructureToPtr(new Native.UNICODE_STRING { Length = (ushort)(path.Length * 2), MaximumLength = (ushort)(path.Length * 2 + 2), Buffer = buffer }, name, false);
                    var attributes = new Native.OBJECT_ATTRIBUTES
                    {
                        Length = Marshal.SizeOf<Native.OBJECT_ATTRIBUTES>(), ObjectName = name, Attributes = Native.OBJ_CASE_INSENSITIVE | Native.OBJ_OPENIF
                    };
                    int status = Native.NtCreateDirectoryObject(out IntPtr directory, Native.DIRECTORY_ALL_ACCESS, ref attributes);
                    return _directory = status >= 0 ? directory : IntPtr.Zero;   // kept for the app's lifetime
                }
                finally
                {
                    Marshal.FreeHGlobal(name);
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        private static readonly object SidGate = new object();
        private static IntPtr _sid;

        /// <summary>The AppContainer's SID, derived from its name (no profile is created: nothing is registered on the PC).</summary>
        private static IntPtr AppContainerSid()
        {
            lock (SidGate)
            {
                if (_sid != IntPtr.Zero) return _sid;
                int hr = Native.DeriveAppContainerSidFromAppContainerName(AppContainerName, out IntPtr sid);
                if (hr != 0 || sid == IntPtr.Zero) throw new Esp32IsolationException("korumalı alan (AppContainer) kimliği alınamadı (0x" + hr.ToString("X8") + ")");
                return _sid = sid;   // kept for the process' lifetime
            }
        }

        /// <summary>Lets the AppContainer read and run one file (the host or the sketch): nothing else of the user's is open to it.</summary>
        private static void Grant(string path, IntPtr sid)
        {
            int rc = Native.GetNamedSecurityInfoW(path, Native.SE_FILE_OBJECT, Native.DACL_SECURITY_INFORMATION, IntPtr.Zero, IntPtr.Zero, out IntPtr dacl, IntPtr.Zero, out IntPtr sd);
            if (rc != 0) throw new Esp32IsolationException("dosya izinleri okunamadı: " + path + " (Win32 hata kodu " + rc + ")");
            try
            {
                var access = new Native.EXPLICIT_ACCESS_W
                {
                    grfAccessPermissions = Native.GENERIC_READ | Native.GENERIC_EXECUTE,
                    grfAccessMode = Native.GRANT_ACCESS,
                    grfInheritance = 0,
                    Trustee = new Native.TRUSTEE_W { TrusteeForm = Native.TRUSTEE_IS_SID, TrusteeType = Native.TRUSTEE_IS_UNKNOWN, ptstrName = sid }
                };
                rc = Native.SetEntriesInAclW(1, ref access, dacl, out IntPtr newDacl);
                if (rc != 0) throw new Esp32IsolationException("dosya izni hazırlanamadı (Win32 hata kodu " + rc + ")");
                try
                {
                    rc = Native.SetNamedSecurityInfoW(path, Native.SE_FILE_OBJECT, Native.DACL_SECURITY_INFORMATION, IntPtr.Zero, IntPtr.Zero, newDacl, IntPtr.Zero);
                    if (rc != 0) throw new Esp32IsolationException("dosyaya korumalı alan izni verilemedi: " + path + " (Win32 hata kodu " + rc + ")");
                }
                finally
                {
                    Native.LocalFree(newDacl);
                }
            }
            finally
            {
                Native.LocalFree(sd);
            }
        }

        private sealed class NativeWaitHandle : WaitHandle
        {
            public NativeWaitHandle(IntPtr handle)
            {
                SafeWaitHandle = new SafeWaitHandle(handle, true);
            }

            public IntPtr Raw => SafeWaitHandle.DangerousGetHandle();
        }

        private static class Native
        {
            public const uint PAGE_READWRITE = 0x04;
            public const uint FILE_MAP_ALL_ACCESS = 0xF001F;
            public const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
            public const uint CREATE_SUSPENDED = 0x00000004;
            public const uint CREATE_NO_WINDOW = 0x08000000;
            public const int PROC_THREAD_ATTRIBUTE_HANDLE_LIST = 0x00020002;
            public const uint TOKEN_ASSIGN_PRIMARY = 0x0001, TOKEN_DUPLICATE = 0x0002, TOKEN_QUERY = 0x0008, TOKEN_ADJUST_DEFAULT = 0x0080, TOKEN_ALL_ACCESS = 0xF01FF;
            public const uint OBJ_CASE_INSENSITIVE = 0x40, OBJ_OPENIF = 0x80;
            public const uint DIRECTORY_ALL_ACCESS = 0xF000F;
            public const int JobObjectBasicUIRestrictions = 4;
            public const int JobObjectExtendedLimitInformation = 9;
            public const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
            public const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100;
            public const uint JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION = 0x00000400;
            public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
            public const int JOB_OBJECT_UILIMIT_ALL = 0xFF;
            public const int SE_FILE_OBJECT = 1;
            public const int DACL_SECURITY_INFORMATION = 4;
            public const uint GENERIC_READ = 0x80000000;
            public const uint GENERIC_EXECUTE = 0x20000000;
            public const int GRANT_ACCESS = 1;
            public const int TRUSTEE_IS_SID = 0;
            public const int TRUSTEE_IS_UNKNOWN = 0;

            [StructLayout(LayoutKind.Sequential)]
            public struct SECURITY_ATTRIBUTES
            {
                public int nLength;
                public IntPtr lpSecurityDescriptor;
                public int bInheritHandle;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct STARTUPINFO
            {
                public int cb;
                public IntPtr lpReserved, lpDesktop, lpTitle;
                public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
                public short wShowWindow, cbReserved2;
                public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct STARTUPINFOEX
            {
                public STARTUPINFO StartupInfo;
                public IntPtr lpAttributeList;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct PROCESS_INFORMATION
            {
                public IntPtr hProcess, hThread;
                public int dwProcessId, dwThreadId;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct UNICODE_STRING
            {
                public ushort Length, MaximumLength;
                public IntPtr Buffer;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct OBJECT_ATTRIBUTES
            {
                public int Length;
                public IntPtr RootDirectory, ObjectName;
                public uint Attributes;
                public IntPtr SecurityDescriptor, SecurityQualityOfService;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint PriorityClass, SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct IO_COUNTERS
            {
                public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                public IO_COUNTERS IoInfo;
                public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct TRUSTEE_W
            {
                public IntPtr pMultipleTrustee;
                public int MultipleTrusteeOperation;
                public int TrusteeForm;
                public int TrusteeType;
                public IntPtr ptstrName;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct EXPLICIT_ACCESS_W
            {
                public uint grfAccessPermissions;
                public int grfAccessMode;
                public uint grfInheritance;
                public TRUSTEE_W Trustee;
            }

            [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern IntPtr CreateFileMappingW(IntPtr file, ref SECURITY_ATTRIBUTES attributes, uint protect, uint sizeHigh, uint sizeLow, string? name);

            [DllImport("kernel32", SetLastError = true)]
            public static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnmapViewOfFile(IntPtr view);

            [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern IntPtr CreateEventW(ref SECURITY_ATTRIBUTES attributes, bool manualReset, bool initialState, string? name);

            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetEvent(IntPtr handle);

            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr handle);

            [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int length);

            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref int info, int length);

            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);

            [DllImport("kernel32", SetLastError = true)]
            public static extern void DeleteProcThreadAttributeList(IntPtr list);

            [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CreateProcessW(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
                bool inheritHandles, uint flags, IntPtr environment, string currentDirectory, ref STARTUPINFOEX startupInfo, out PROCESS_INFORMATION processInformation);

            [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CreateProcessAsUserW(IntPtr token, string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
                bool inheritHandles, uint flags, IntPtr environment, string currentDirectory, ref STARTUPINFOEX startupInfo, out PROCESS_INFORMATION processInformation);

            [DllImport("kernel32")]
            public static extern IntPtr GetCurrentProcess();

            [DllImport("advapi32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

            [DllImport("ntdll")]
            public static extern int NtCreateLowBoxToken(out IntPtr token, IntPtr existingToken, uint access, ref OBJECT_ATTRIBUTES attributes, IntPtr packageSid,
                uint capabilityCount, IntPtr capabilities, uint handleCount, IntPtr[]? handles);

            [DllImport("ntdll")]
            public static extern int NtCreateDirectoryObject(out IntPtr handle, uint access, ref OBJECT_ATTRIBUTES attributes);

            [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ConvertSidToStringSidW(IntPtr sid, out IntPtr text);

            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

            [DllImport("kernel32", SetLastError = true)]
            public static extern uint ResumeThread(IntPtr thread);

            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool TerminateProcess(IntPtr process, uint exitCode);

            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

            [DllImport("userenv", CharSet = CharSet.Unicode)]
            public static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);

            [DllImport("advapi32", CharSet = CharSet.Unicode)]
            public static extern int GetNamedSecurityInfoW(string name, int objectType, int securityInfo, IntPtr owner, IntPtr group, out IntPtr dacl, IntPtr sacl, out IntPtr securityDescriptor);

            [DllImport("advapi32", CharSet = CharSet.Unicode)]
            public static extern int SetNamedSecurityInfoW(string name, int objectType, int securityInfo, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);

            [DllImport("advapi32", CharSet = CharSet.Unicode)]
            public static extern int SetEntriesInAclW(int count, ref EXPLICIT_ACCESS_W entries, IntPtr oldAcl, out IntPtr newAcl);

            [DllImport("kernel32")]
            public static extern IntPtr LocalFree(IntPtr memory);
        }
    }
}
