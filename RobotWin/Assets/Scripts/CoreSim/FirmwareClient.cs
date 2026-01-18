using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

namespace RobotTwin.CoreSim
{
    [Serializable]
    public class FirmwareStepRequest
    {
        public float RailVoltage = 5.0f;
        public uint DeltaMicros = 100000;
        public int[] PinStates = Array.Empty<int>();
        public float[] AnalogVoltages = Array.Empty<float>();
    }

    [Serializable]
    public class FirmwarePerfCounters
    {
        public ulong Cycles;
        public ulong AdcSamples;
        public ulong[] UartTxBytes = new ulong[4];
        public ulong[] UartRxBytes = new ulong[4];
        public ulong SpiTransfers;
        public ulong TwiTransfers;
        public ulong WdtResets;
        public ulong DroppedOutputs;

        public void CopyFrom(FirmwarePerfCounters other)
        {
            if (other == null) return;
            Cycles = other.Cycles;
            AdcSamples = other.AdcSamples;
            if (other.UartTxBytes != null)
            {
                for (int i = 0; i < UartTxBytes.Length && i < other.UartTxBytes.Length; i++)
                {
                    UartTxBytes[i] = other.UartTxBytes[i];
                }
            }
            if (other.UartRxBytes != null)
            {
                for (int i = 0; i < UartRxBytes.Length && i < other.UartRxBytes.Length; i++)
                {
                    UartRxBytes[i] = other.UartRxBytes[i];
                }
            }
            SpiTransfers = other.SpiTransfers;
            TwiTransfers = other.TwiTransfers;
            WdtResets = other.WdtResets;
            DroppedOutputs = other.DroppedOutputs;
        }

        public FirmwarePerfCounters Clone()
        {
            var copy = new FirmwarePerfCounters();
            copy.CopyFrom(this);
            return copy;
        }
    }

    public enum FirmwareLogLevel : byte
    {
        Unknown = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    public enum FirmwareMemoryType : byte
    {
        Flash = 1,
        Sram = 2,
        Io = 3,
        Eeprom = 4
    }

    public enum FirmwareErrorKind
    {
        None = 0,
        PipeUnavailable = 1,
        AccessDenied = 2,
        BrokenPipe = 3,
        Io = 4,
        Protocol = 5,
        Unknown = 6
    }

    [Serializable]
    public class FirmwareDebugCounters
    {
        public uint FlashBytes;
        public uint SramBytes;
        public uint EepromBytes;
        public uint IoBytes;
        public uint CpuHz;
        public ushort ProgramCounter;
        public ushort StackPointer;
        public byte StatusRegister;
        public ushort StackHighWater;
        public ushort HeapTopAddress;
        public ushort StackMinAddress;
        public ushort DataSegmentEnd;
        public ulong StackOverflows;
        public ulong InvalidMemoryAccesses;
        public ulong InterruptCount;
        public ulong InterruptLatencyMax;
        public ulong TimingViolations;
        public ulong CriticalSectionCycles;
        public ulong SleepCycles;
        public ulong FlashAccessCycles;
        public ulong UartOverflows;
        public ulong TimerOverflows;
        public ulong BrownOutResets;
        public ulong GpioStateChanges;
        public ulong PwmCycles;
        public ulong I2cTransactions;
        public ulong SpiTransactions;

        public void CopyFrom(FirmwareDebugCounters other)
        {
            if (other == null) return;
            FlashBytes = other.FlashBytes;
            SramBytes = other.SramBytes;
            EepromBytes = other.EepromBytes;
            IoBytes = other.IoBytes;
            CpuHz = other.CpuHz;
            ProgramCounter = other.ProgramCounter;
            StackPointer = other.StackPointer;
            StatusRegister = other.StatusRegister;
            StackHighWater = other.StackHighWater;
            HeapTopAddress = other.HeapTopAddress;
            StackMinAddress = other.StackMinAddress;
            DataSegmentEnd = other.DataSegmentEnd;
            StackOverflows = other.StackOverflows;
            InvalidMemoryAccesses = other.InvalidMemoryAccesses;
            InterruptCount = other.InterruptCount;
            InterruptLatencyMax = other.InterruptLatencyMax;
            TimingViolations = other.TimingViolations;
            CriticalSectionCycles = other.CriticalSectionCycles;
            SleepCycles = other.SleepCycles;
            FlashAccessCycles = other.FlashAccessCycles;
            UartOverflows = other.UartOverflows;
            TimerOverflows = other.TimerOverflows;
            BrownOutResets = other.BrownOutResets;
            GpioStateChanges = other.GpioStateChanges;
            PwmCycles = other.PwmCycles;
            I2cTransactions = other.I2cTransactions;
            SpiTransactions = other.SpiTransactions;
        }
    }

    [Serializable]
    public class FirmwareDebugBitField
    {
        public FirmwareDebugBitField(string name, ushort offset, byte width, ulong value)
        {
            Name = name;
            Offset = offset;
            Width = width;
            Value = value;
        }

        public string Name { get; }
        public ushort Offset { get; }
        public byte Width { get; }
        public ulong Value { get; }

        public string Bits
        {
            get
            {
                if (Width == 0) return string.Empty;
                var text = Convert.ToString((long)Value, 2);
                return text.PadLeft(Width, '0');
            }
        }
    }

    [Serializable]
    public class FirmwareDebugBitset
    {
        public ushort BitCount;
        public byte[] Raw = Array.Empty<byte>();
        public List<FirmwareDebugBitField> Fields { get; } = new List<FirmwareDebugBitField>();

        public void CopyFrom(FirmwareDebugBitset other)
        {
            if (other == null) return;
            BitCount = other.BitCount;
            Raw = other.Raw ?? Array.Empty<byte>();
            Fields.Clear();
            Fields.AddRange(other.Fields);
        }
    }

    [Serializable]
    public class FirmwareStepResult
    {
        public const int PinUnknown = -1;
        public ulong StepSequence;
        public ulong TickCount;
        public int[] PinStates = new int[70];
        public string SerialOutput = string.Empty;
        public FirmwarePerfCounters PerfCounters = new FirmwarePerfCounters();
        public FirmwareDebugCounters DebugCounters = new FirmwareDebugCounters();
        public FirmwareDebugBitset DebugBits = new FirmwareDebugBitset();
        public ulong OutputTimestampMicros;

        public FirmwareStepResult()
        {
            Array.Fill(PinStates, PinUnknown);
        }
    }

    public class FirmwareClient : MonoBehaviour
    {
        private const string DefaultPipeName = "RoboTwin.FirmwareEngine";
        private const string AlternatePipeName = "RoboTwin.Firmware";
        private const float ConnectRetrySeconds = 1.0f;
        private const int PipeConnectTimeoutMs = 250;
        private static float StepWaitSeconds = 0.0f;
        private const float StaleOutputGraceSeconds = 1.0f;
        private const int MaxWriteQueue = 8;
        private const uint ProtocolMagic = 0x57465452; // "RTFW"
        private const ushort ProtocolMajor = 1;
        private const ushort ProtocolMinor = 3;
        private const int PinCount = 70;
        private const int AnalogCount = 16;
        private const int BoardIdSize = 64;
        private const int BoardProfileSize = 64;
        private const uint ClientFlags = 1u << 8; // Lockstep mode hint
        private const uint FeatureTimestampMicros = 1u << 0;
        private const uint FeaturePerfCounters = 1u << 1;
        private const uint MaxPayloadBytes = 8 * 1024 * 1024;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private const int PipeNotFoundError = 2;
        private const int PipeBusyError = 231;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool WaitNamedPipe(string name, int timeout);
#endif

        private enum MessageType : ushort
        {
            Hello = 1,
            HelloAck = 2,
            LoadBvm = 3,
            Step = 4,
            OutputState = 5,
            Serial = 6,
            Status = 7,
            Log = 8,
            Error = 9,
            MemoryPatch = 10
        }

        private readonly struct DebugBitFieldSpec
        {
            public readonly string Name;
            public readonly ushort Offset;
            public readonly byte Width;

            public DebugBitFieldSpec(string name, ushort offset, byte width)
            {
                Name = name;
                Offset = offset;
                Width = width;
            }
        }

        private static readonly DebugBitFieldSpec[] DebugBitFields =
        {
            new DebugBitFieldSpec("pc", 0, 16),
            new DebugBitFieldSpec("sp", 16, 16),
            new DebugBitFieldSpec("sreg", 32, 8),
            new DebugBitFieldSpec("flash_bytes", 40, 32),
            new DebugBitFieldSpec("sram_bytes", 72, 32),
            new DebugBitFieldSpec("eeprom_bytes", 104, 32),
            new DebugBitFieldSpec("io_bytes", 136, 32),
            new DebugBitFieldSpec("cpu_hz", 168, 32),
            new DebugBitFieldSpec("stack_high_water", 200, 16),
            new DebugBitFieldSpec("heap_top", 216, 16),
            new DebugBitFieldSpec("stack_min", 232, 16),
            new DebugBitFieldSpec("data_segment_end", 248, 16),
            new DebugBitFieldSpec("stack_overflows", 264, 32),
            new DebugBitFieldSpec("invalid_mem_accesses", 296, 32),
            new DebugBitFieldSpec("interrupt_count", 328, 32),
            new DebugBitFieldSpec("interrupt_latency_max", 360, 32),
            new DebugBitFieldSpec("timing_violations", 392, 32),
            new DebugBitFieldSpec("critical_section_cycles", 424, 32),
            new DebugBitFieldSpec("sleep_cycles", 456, 32),
            new DebugBitFieldSpec("flash_access_cycles", 488, 32),
            new DebugBitFieldSpec("uart_overflows", 520, 32),
            new DebugBitFieldSpec("timer_overflows", 552, 32),
            new DebugBitFieldSpec("brown_out_resets", 584, 32),
            new DebugBitFieldSpec("gpio_state_changes", 616, 32),
            new DebugBitFieldSpec("pwm_cycles", 648, 32),
            new DebugBitFieldSpec("i2c_transactions", 680, 32),
            new DebugBitFieldSpec("spi_transactions", 712, 32)
        };

        private Process _firmwareProcess;
        private NamedPipeClientStream _pipeClient;
        private Thread _readerThread;
        private Thread _writerThread;
        private AutoResetEvent _writeSignal;
        private volatile bool _readerRunning;
        private volatile bool _readerHealthy;
        private volatile bool _writerRunning;
        private float _nextConnectAttemptTime;
        private readonly object _writeLock = new object();
        private readonly object _writeQueueLock = new object();
        private readonly object _stateLock = new object();
        private readonly int[] _lastInputs = new int[PinCount];
        private readonly Queue<PendingWrite> _writeQueue = new Queue<PendingWrite>();
        private uint _sequence = 1;
        private ulong _stepSequence = 1;
        private string _pipeName = DefaultPipeName;
        private readonly Dictionary<string, BoardState> _boardStates = new Dictionary<string, BoardState>(StringComparer.OrdinalIgnoreCase);
        private uint _serverFlags;
        private bool _versionWarned;
        private bool _timestampSyncValid;
        private long _timestampOffsetMicros;
        private DateTime _nextPipeWarningUtc = DateTime.MinValue;
        private const long TimestampResyncThresholdMicros = 5_000_000;

        public bool IsConnected => _pipeClient != null && _pipeClient.IsConnected;
        public bool ReaderHealthy => _readerHealthy;
        public DateTime LastPacketUtc { get; private set; } = DateTime.MinValue;
        public DateTime LastWriteUtc { get; private set; } = DateTime.MinValue;
        public string LastError { get; private set; } = string.Empty;
        public FirmwareErrorKind LastErrorKind { get; private set; } = FirmwareErrorKind.None;

        private sealed class BoardState
        {
            public ulong LastSequence;
            public readonly object SequenceLock = new object();
            public readonly int[] PinOutputs = new int[PinCount];
            public readonly StringBuilder SerialBuffer = new StringBuilder();
            public readonly FirmwarePerfCounters Perf = new FirmwarePerfCounters();
            public readonly FirmwareDebugCounters Debug = new FirmwareDebugCounters();
            public readonly FirmwareDebugBitset DebugBits = new FirmwareDebugBitset();
            public ulong LastTimestampMicros;
            public ulong LastTick;

            public BoardState()
            {
                Array.Fill(PinOutputs, FirmwareStepResult.PinUnknown);
            }
        }

        private sealed class PendingWrite
        {
            public MessageType Type;
            public byte[] Header;
            public byte[] Payload;
        }

        public string PipeName => _pipeName;
        public bool LaunchLockstep { get; set; } = true;
        public string LastLaunchArguments { get; private set; } = string.Empty;
        public bool DropStaleOutputs { get; set; } = true;
        public double MaxOutputAgeMs { get; set; } = 250.0;
        public string ExtraLaunchArguments { get; set; } = string.Empty;

        public void Configure(string pipeName)
        {
            _pipeName = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName;
        }

        private void SetError(FirmwareErrorKind kind, string message)
        {
            LastErrorKind = kind;
            LastError = message ?? string.Empty;
        }

        private void ClearError()
        {
            LastErrorKind = FirmwareErrorKind.None;
            LastError = string.Empty;
        }

        public void LaunchFirmware(string executablePath)
        {
            if (_firmwareProcess != null && !_firmwareProcess.HasExited)
            {
                UnityEngine.Debug.LogWarning("[FirmwareClient] Process already running.");
                return;
            }

            if (!File.Exists(executablePath))
            {
                UnityEngine.Debug.LogError($"[FirmwareClient] Firmware executable not found at: {executablePath}");
                return;
            }

            try
            {
                var modeArg = LaunchLockstep ? "--lockstep" : "--realtime";
                string extra = string.IsNullOrWhiteSpace(ExtraLaunchArguments) ? string.Empty : $" {ExtraLaunchArguments}";
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = $"--pipe {_pipeName} {modeArg}{extra}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                LastLaunchArguments = startInfo.Arguments;
                _firmwareProcess = Process.Start(startInfo);
                UnityEngine.Debug.Log($"[FirmwareClient] Launched Firmware PID: {_firmwareProcess.Id}");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FirmwareClient] Failed to launch: {ex.Message}");
            }
        }

        private bool EnsureConnected()
        {
            if (_pipeClient != null && _pipeClient.IsConnected) return true;
            if (Time.unscaledTime < _nextConnectAttemptTime) return false;
            _nextConnectAttemptTime = Time.unscaledTime + ConnectRetrySeconds;

            try
            {
                if (TryConnectToPipe(_pipeName)) return true;
                if (!string.Equals(_pipeName, DefaultPipeName, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryConnectToPipe(DefaultPipeName))
                    {
                        _pipeName = DefaultPipeName;
                        return true;
                    }
                }
                if (!string.Equals(_pipeName, AlternatePipeName, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryConnectToPipe(AlternatePipeName))
                    {
                        _pipeName = AlternatePipeName;
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                SetError(FirmwareErrorKind.Unknown, ex.Message);
                UnityEngine.Debug.LogWarning($"[FirmwareClient] Pipe connection failed: {ex.Message}");
                return false;
            }
        }

        private bool TryConnectToPipe(string pipeName)
        {
            if (string.IsNullOrWhiteSpace(pipeName)) return false;
            if (!IsPipeAvailable(pipeName))
            {
                SetError(FirmwareErrorKind.PipeUnavailable, "pipe not available");
                return false;
            }
            _pipeClient?.Dispose();
            _pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            UnityEngine.Debug.Log($"[FirmwareClient] Connecting to firmware pipe '{pipeName}'...");
            try
            {
                _pipeClient.Connect(PipeConnectTimeoutMs);
            }
            catch (TimeoutException ex)
            {
                SetError(FirmwareErrorKind.PipeUnavailable, ex.Message);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                SetError(FirmwareErrorKind.AccessDenied, ex.Message);
                UnityEngine.Debug.LogWarning($"[FirmwareClient] Pipe access denied: {ex.Message}");
                return false;
            }
            catch (IOException ex)
            {
                SetError(FirmwareErrorKind.PipeUnavailable, ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                SetError(FirmwareErrorKind.Unknown, ex.Message);
                return false;
            }
            try
            {
                _pipeClient.ReadTimeout = Timeout.Infinite;
                _pipeClient.WriteTimeout = 200;
            }
            catch { }
            ClearError();
            UnityEngine.Debug.Log($"[FirmwareClient] Connected to firmware pipe '{pipeName}'.");
            StartWriterThread();
            if (!SendHello())
            {
                Disconnect();
                return false;
            }
            StartReaderThread();
            return true;
        }

        private static bool IsPipeAvailable(string pipeName)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (string.IsNullOrWhiteSpace(pipeName)) return false;
            string fullName = pipeName.StartsWith(@"\\.\pipe\", StringComparison.Ordinal)
                ? pipeName
                : $@"\\.\pipe\{pipeName}";
            if (WaitNamedPipe(fullName, 0)) return true;
            int err = Marshal.GetLastWin32Error();
            if (err == PipeNotFoundError || err == PipeBusyError) return false;
            return false;
#else
            _ = pipeName;
            return true;
#endif
        }

        public bool TryStep(string boardId, FirmwareStepRequest request, out FirmwareStepResult result)
        {
            result = new FirmwareStepResult();
            if (request == null) return false;
            if (string.IsNullOrWhiteSpace(boardId)) return false;
            if (!EnsureConnected()) return false;

            ulong expectedSeq;
            lock (_stateLock)
            {
                expectedSeq = _stepSequence++;
            }

            if (!SendStep(boardId, expectedSeq, request)) return false;

            BoardState state;
            lock (_stateLock)
            {
                state = GetBoardState(boardId);
            }

            if (StepWaitSeconds > 0f)
            {
                // Wait briefly for a fresh OutputState matching this step.
                lock (state.SequenceLock)
                {
                    var deadline = Time.realtimeSinceStartup + StepWaitSeconds;
                    while (state.LastSequence < expectedSeq)
                    {
                        float remaining = deadline - Time.realtimeSinceStartup;
                        if (remaining <= 0f)
                        {
                            break;
                        }
                        Monitor.Wait(state.SequenceLock, Mathf.CeilToInt(remaining * 1000f));
                    }
                }
            }

            result.PinStates = new int[PinCount];
            lock (_stateLock)
            {
                // If the expected sequence hasn't arrived yet, return the latest known outputs (if any).
                if (state.LastSequence < expectedSeq && LastPacketUtc != DateTime.MinValue)
                {
                    var silenceSeconds = (DateTime.UtcNow - LastPacketUtc).TotalSeconds;
                    if (silenceSeconds > StaleOutputGraceSeconds)
                    {
                        SetError(FirmwareErrorKind.PipeUnavailable, "no output state received");
                        return false;
                    }
                }
                Array.Copy(state.PinOutputs, result.PinStates, result.PinStates.Length);
                if (state.SerialBuffer.Length > 0)
                {
                    result.SerialOutput = state.SerialBuffer.ToString();
                    state.SerialBuffer.Clear();
                }
                result.PerfCounters.CopyFrom(state.Perf);
                result.DebugCounters.CopyFrom(state.Debug);
                result.DebugBits.CopyFrom(state.DebugBits);
                result.OutputTimestampMicros = state.LastTimestampMicros;
                result.StepSequence = state.LastSequence;
                result.TickCount = state.LastTick;
            }

            return true;
        }

        public bool LoadBvmFile(string boardId, string boardProfile, string path)
        {
            if (string.IsNullOrWhiteSpace(boardId)) return false;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            var data = File.ReadAllBytes(path);
            return LoadBvmBytes(boardId, boardProfile, data);
        }

        public bool LoadBvmBytes(string boardId, string boardProfile, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(boardId)) return false;
            if (data == null || data.Length == 0) return false;
            if (!EnsureConnected()) return false;
            var header = new byte[BoardIdSize + BoardProfileSize];
            WriteFixedString(header, 0, BoardIdSize, boardId);
            WriteFixedString(header, BoardIdSize, BoardProfileSize, boardProfile ?? string.Empty);
            var payload = new byte[header.Length + data.Length];
            Buffer.BlockCopy(header, 0, payload, 0, header.Length);
            Buffer.BlockCopy(data, 0, payload, header.Length, data.Length);
            if (!WritePacket(MessageType.LoadBvm, payload))
            {
                UnityEngine.Debug.LogWarning("[FirmwareClient] Load failed: pipe write error.");
                Disconnect();
                return false;
            }
            return true;
        }

        public bool InjectMemory(string boardId, FirmwareMemoryType memoryType, uint address, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(boardId)) return false;
            if (data == null || data.Length == 0) return false;
            if (!EnsureConnected()) return false;

            var header = new byte[BoardIdSize + 12];
            WriteFixedString(header, 0, BoardIdSize, boardId);
            header[BoardIdSize] = (byte)memoryType;
            WriteUInt32(header, BoardIdSize + 4, address);
            WriteUInt32(header, BoardIdSize + 8, (uint)data.Length);
            var payload = new byte[header.Length + data.Length];
            Buffer.BlockCopy(header, 0, payload, 0, header.Length);
            Buffer.BlockCopy(data, 0, payload, header.Length, data.Length);
            if (!WritePacket(MessageType.MemoryPatch, payload))
            {
                UnityEngine.Debug.LogWarning("[FirmwareClient] Memory patch failed: pipe write error.");
                Disconnect();
                return false;
            }
            return true;
        }

        public bool InjectFlashBytes(string boardId, uint address, byte[] data)
        {
            return InjectMemory(boardId, FirmwareMemoryType.Flash, address, data);
        }

        public void StopFirmware()
        {
            Disconnect();

            if (_firmwareProcess != null && !_firmwareProcess.HasExited)
            {
                _firmwareProcess.Kill();
                _firmwareProcess = null;
                UnityEngine.Debug.Log("[FirmwareClient] Firmware stopped.");
            }
        }

        private void OnDestroy()
        {
            StopFirmware();
        }

        private void Disconnect()
        {
            StopReaderThread();
            StopWriterThread();
            _pipeClient?.Dispose();
            _pipeClient = null;
            _timestampSyncValid = false;
            _timestampOffsetMicros = 0;
        }

        private void StartReaderThread()
        {
            if (_readerThread != null && _readerThread.IsAlive) return;
            _readerRunning = true;
            _readerHealthy = true;
            _readerThread = new Thread(ReadLoop) { IsBackground = true, Name = "FirmwarePipeReader" };
            _readerThread.Start();
        }

        private void StopReaderThread()
        {
            _readerRunning = false;
            _readerHealthy = false;
            if (_readerThread != null && _readerThread.IsAlive)
            {
                if (Thread.CurrentThread != _readerThread)
                {
                    _readerThread.Join(500);
                }
            }
            _readerThread = null;
        }

        private void StartWriterThread()
        {
            if (_writerThread != null && _writerThread.IsAlive) return;
            _writerRunning = true;
            _writeSignal ??= new AutoResetEvent(false);
            _writerThread = new Thread(WriteLoop) { IsBackground = true, Name = "FirmwarePipeWriter" };
            _writerThread.Start();
        }

        private void StopWriterThread()
        {
            _writerRunning = false;
            _writeSignal?.Set();
            if (_writerThread != null && _writerThread.IsAlive)
            {
                if (Thread.CurrentThread != _writerThread)
                {
                    _writerThread.Join(500);
                }
            }
            _writerThread = null;
            lock (_writeQueueLock)
            {
                _writeQueue.Clear();
            }
        }

        private void WriteLoop()
        {
            while (_writerRunning)
            {
                PendingWrite write = null;
                lock (_writeQueueLock)
                {
                    if (_writeQueue.Count > 0)
                    {
                        write = _writeQueue.Dequeue();
                    }
                }

                if (write == null)
                {
                    _writeSignal?.WaitOne(50);
                    continue;
                }

                if (_pipeClient == null || !_pipeClient.IsConnected)
                {
                    continue;
                }

                lock (_writeLock)
                {
                    try
                    {
                        LastWriteUtc = DateTime.UtcNow;
                        _pipeClient.Write(write.Header, 0, write.Header.Length);
                        if (write.Payload.Length > 0)
                        {
                            _pipeClient.Write(write.Payload, 0, write.Payload.Length);
                        }
                        _pipeClient.Flush();
                    }
                    catch (Exception ex)
                    {
                        var kind = IsPipeBroken(ex) ? FirmwareErrorKind.BrokenPipe : FirmwareErrorKind.Io;
                        SetError(kind, ex.Message);
                        if (DateTime.UtcNow >= _nextPipeWarningUtc)
                        {
                            _nextPipeWarningUtc = DateTime.UtcNow.AddSeconds(2);
                            UnityEngine.Debug.LogWarning($"[FirmwareClient] Pipe write failed: {ex.Message}");
                        }
                        Disconnect();
                    }
                }
            }
        }

        private void ReadLoop()
        {
            try
            {
                while (_readerRunning && _pipeClient != null && _pipeClient.IsConnected)
                {
                    if (!ReadPacket(out var type, out var payload))
                    {
                        break;
                    }
                    HandleMessage(type, payload);
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                UnityEngine.Debug.LogWarning($"[FirmwareClient] Pipe reader exception: {ex.Message}");
            }
            finally
            {
                _readerHealthy = false;
                // Ensure we don't keep returning stale state.
                Disconnect();
            }
        }

        private void HandleMessage(MessageType type, byte[] payload)
        {
            if (type == MessageType.HelloAck)
            {
                if (payload != null && payload.Length >= 16)
                {
                    _serverFlags = ReadUInt32(payload, 0);
                    if ((_serverFlags & FeatureTimestampMicros) == 0)
                    {
                        DropStaleOutputs = false;
                    }
                }
                LastPacketUtc = DateTime.UtcNow;
                UnityEngine.Debug.Log("[FirmwareClient] Firmware handshake complete.");
                return;
            }
            if (type == MessageType.OutputState)
            {
                // OutputStatePayload: board_id (64) + step_sequence (8) + tick_count (8) + pins (70) + perf + debug + bits
                if (payload == null || payload.Length < BoardIdSize + 16 + PinCount) return;
                lock (_stateLock)
                {
                    LastPacketUtc = DateTime.UtcNow;
                    string boardId = ReadFixedString(payload, 0, BoardIdSize);
                    var state = GetBoardState(boardId);
                    ulong seq = ReadUInt64(payload, BoardIdSize);
                    ulong tick = ReadUInt64(payload, BoardIdSize + 8);
                    ulong previous = state.LastSequence;

                    int cursor = BoardIdSize + 16 + PinCount;
                    int perfSize = 13 * 8;
                    if (payload.Length >= cursor + perfSize)
                    {
                        state.Perf.Cycles = ReadUInt64(payload, cursor); cursor += 8;
                        state.Perf.AdcSamples = ReadUInt64(payload, cursor); cursor += 8;
                        for (int i = 0; i < state.Perf.UartTxBytes.Length; i++)
                        {
                            state.Perf.UartTxBytes[i] = ReadUInt64(payload, cursor); cursor += 8;
                        }
                        for (int i = 0; i < state.Perf.UartRxBytes.Length; i++)
                        {
                            state.Perf.UartRxBytes[i] = ReadUInt64(payload, cursor); cursor += 8;
                        }
                        state.Perf.SpiTransfers = ReadUInt64(payload, cursor); cursor += 8;
                        state.Perf.TwiTransfers = ReadUInt64(payload, cursor); cursor += 8;
                        state.Perf.WdtResets = ReadUInt64(payload, cursor); cursor += 8;
                    }

                    if (payload.Length >= cursor + 8)
                    {
                        state.LastTimestampMicros = ReadUInt64(payload, cursor);
                        cursor += 8;
                        if (DropStaleOutputs && state.LastTimestampMicros > 0)
                        {
                            long nowMicros = NowMicros();
                            long serverMicros = (long)state.LastTimestampMicros;
                            long expected = serverMicros + _timestampOffsetMicros;
                            long delta = Math.Abs(nowMicros - expected);
                            if (!_timestampSyncValid || delta > TimestampResyncThresholdMicros)
                            {
                                _timestampOffsetMicros = nowMicros - serverMicros;
                                _timestampSyncValid = true;
                            }
                            long ageMicros = nowMicros - (serverMicros + _timestampOffsetMicros);
                            if (ageMicros < 0)
                            {
                                ageMicros = 0;
                            }
                            if (ageMicros > (long)(MaxOutputAgeMs * 1000.0))
                            {
                                state.Perf.DroppedOutputs += 1;
                                return;
                            }
                        }
                    }

                    if (payload.Length >= cursor + 20)
                    {
                        state.Debug.FlashBytes = ReadUInt32(payload, cursor); cursor += 4;
                        state.Debug.SramBytes = ReadUInt32(payload, cursor); cursor += 4;
                        state.Debug.EepromBytes = ReadUInt32(payload, cursor); cursor += 4;
                        state.Debug.IoBytes = ReadUInt32(payload, cursor); cursor += 4;
                        state.Debug.CpuHz = ReadUInt32(payload, cursor); cursor += 4;
                    }
                    if (payload.Length >= cursor + 14)
                    {
                        state.Debug.ProgramCounter = ReadUInt16(payload, cursor); cursor += 2;
                        state.Debug.StackPointer = ReadUInt16(payload, cursor); cursor += 2;
                        state.Debug.StatusRegister = payload[cursor]; cursor += 1;
                        cursor += 1;
                        state.Debug.StackHighWater = ReadUInt16(payload, cursor); cursor += 2;
                        state.Debug.HeapTopAddress = ReadUInt16(payload, cursor); cursor += 2;
                        state.Debug.StackMinAddress = ReadUInt16(payload, cursor); cursor += 2;
                        state.Debug.DataSegmentEnd = ReadUInt16(payload, cursor); cursor += 2;
                    }
                    if (payload.Length >= cursor + 88)
                    {
                        state.Debug.StackOverflows = ReadUInt64(payload, cursor); cursor += 8;
                        state.Debug.InvalidMemoryAccesses = ReadUInt64(payload, cursor); cursor += 8;
                        state.Debug.InterruptCount = ReadUInt64(payload, cursor); cursor += 8;
                        state.Debug.InterruptLatencyMax = ReadUInt64(payload, cursor); cursor += 8;
                        state.Debug.TimingViolations = ReadUInt64(payload, cursor); cursor += 8;
                        state.Debug.CriticalSectionCycles = ReadUInt64(payload, cursor); cursor += 8;
                        state.Debug.SleepCycles = ReadUInt64(payload, cursor); cursor += 8;
                        state.Debug.FlashAccessCycles = ReadUInt64(payload, cursor); cursor += 8;
                        state.Debug.UartOverflows = ReadUInt64(payload, cursor); cursor += 8;
                        state.Debug.TimerOverflows = ReadUInt64(payload, cursor); cursor += 8;
                        state.Debug.BrownOutResets = ReadUInt64(payload, cursor); cursor += 8;
                    }
                    if (payload.Length >= cursor + 32)
                    {
                        state.Debug.GpioStateChanges = ReadUInt64(payload, cursor); cursor += 8;
                        state.Debug.PwmCycles = ReadUInt64(payload, cursor); cursor += 8;
                        state.Debug.I2cTransactions = ReadUInt64(payload, cursor); cursor += 8;
                        state.Debug.SpiTransactions = ReadUInt64(payload, cursor); cursor += 8;
                    }

                    if (payload.Length >= cursor + 4)
                    {
                        ushort bitCount = ReadUInt16(payload, cursor); cursor += 2;
                        cursor += 2;
                        int byteCount = (bitCount + 7) / 8;
                        if (payload.Length >= cursor + byteCount && byteCount > 0)
                        {
                            var raw = new byte[byteCount];
                            Buffer.BlockCopy(payload, cursor, raw, 0, byteCount);
                            state.DebugBits.BitCount = bitCount;
                            state.DebugBits.Raw = raw;
                            state.DebugBits.Fields.Clear();
                            foreach (var spec in DebugBitFields)
                            {
                                ulong value = ReadBits(raw, spec.Offset, spec.Width);
                                state.DebugBits.Fields.Add(new FirmwareDebugBitField(spec.Name, spec.Offset, spec.Width, value));
                            }
                        }
                    }

                    if (previous > 0 && seq > previous + 1)
                    {
                        state.Perf.DroppedOutputs += seq - previous - 1;
                    }

                    lock (state.SequenceLock)
                    {
                        if (seq >= state.LastSequence)
                        {
                            state.LastSequence = seq;
                            state.LastTick = tick;
                        }
                        Monitor.PulseAll(state.SequenceLock);
                    }

                    for (int i = 0; i < PinCount; i++)
                    {
                        byte raw = payload[BoardIdSize + 16 + i];
                        state.PinOutputs[i] = raw == 0xFF ? FirmwareStepResult.PinUnknown : raw;
                    }
                }
                return;
            }
            if (type == MessageType.Serial)
            {
                if (payload == null || payload.Length <= BoardIdSize) return;
                lock (_stateLock)
                {
                    LastPacketUtc = DateTime.UtcNow;
                    string boardId = ReadFixedString(payload, 0, BoardIdSize);
                    var state = GetBoardState(boardId);
                    state.SerialBuffer.Append(Encoding.UTF8.GetString(payload, BoardIdSize, payload.Length - BoardIdSize));
                }
                return;
            }
            if (type == MessageType.Log)
            {
                if (payload == null || payload.Length < BoardIdSize + 1) return;
                LastPacketUtc = DateTime.UtcNow;
                string boardId = ReadFixedString(payload, 0, BoardIdSize);
                var level = (FirmwareLogLevel)payload[BoardIdSize];
                string text = payload.Length > BoardIdSize + 1
                    ? Encoding.UTF8.GetString(payload, BoardIdSize + 1, payload.Length - (BoardIdSize + 1))
                    : string.Empty;
                string message = $"[Firmware:{boardId}:{level}] {text}";
                if (level == FirmwareLogLevel.Warning)
                {
                    UnityEngine.Debug.LogWarning(message);
                }
                else if (level == FirmwareLogLevel.Error)
                {
                    UnityEngine.Debug.LogError(message);
                }
                else
                {
                    UnityEngine.Debug.Log(message);
                }
                return;
            }
            if (type == MessageType.Error)
            {
                if (payload == null || payload.Length < BoardIdSize + 4) return;
                LastPacketUtc = DateTime.UtcNow;
                string boardId = ReadFixedString(payload, 0, BoardIdSize);
                uint code = ReadUInt32(payload, BoardIdSize);
                string text = payload.Length > BoardIdSize + 4
                    ? Encoding.UTF8.GetString(payload, BoardIdSize + 4, payload.Length - (BoardIdSize + 4))
                    : $"Firmware error code {code}";
                LastError = $"Firmware error from {boardId}: {text}";
                UnityEngine.Debug.LogError($"[Firmware:{boardId}] {text}");
                return;
            }
        }

        private bool SendStep(string boardId, ulong stepSequence, FirmwareStepRequest request)
        {
            if (request == null) return false;
            var payload = new byte[BoardIdSize + 8 + 4 + PinCount + (AnalogCount * 2) + 8];
            WriteFixedString(payload, 0, BoardIdSize, boardId);
            WriteUInt64(payload, BoardIdSize, stepSequence);
            WriteUInt32(payload, BoardIdSize + 8, request.DeltaMicros);
            for (int i = 0; i < PinCount; i++)
            {
                int value = (request.PinStates != null && i < request.PinStates.Length && request.PinStates[i] > 0) ? 1 : 0;
                payload[BoardIdSize + 8 + 4 + i] = (byte)value;
                _lastInputs[i] = value;
            }
            int analogOffset = BoardIdSize + 8 + 4 + PinCount;
            for (int i = 0; i < AnalogCount; i++)
            {
                float voltage = (request.AnalogVoltages != null && i < request.AnalogVoltages.Length)
                    ? request.AnalogVoltages[i]
                    : 0f;
                if (voltage < 0f) voltage = 0f;
                if (voltage > 5f) voltage = 5f;
                ushort raw = (ushort)Mathf.RoundToInt((voltage / 5f) * 1023f);
                WriteUInt16(payload, analogOffset + (i * 2), raw);
            }
            WriteUInt64(payload, analogOffset + (AnalogCount * 2), (ulong)NowMicros());
            return WritePacket(MessageType.Step, payload);
        }

        private bool SendHello()
        {
            var payload = new byte[16];
            WriteUInt32(payload, 0, ClientFlags | FeatureTimestampMicros | FeaturePerfCounters);
            WriteUInt32(payload, 4, PinCount);
            WriteUInt32(payload, 8, BoardIdSize);
            WriteUInt32(payload, 12, AnalogCount);
            return WritePacket(MessageType.Hello, payload);
        }

        private bool WritePacket(MessageType type, byte[] payload)
        {
            if (_pipeClient == null || !_pipeClient.IsConnected) return false;
            payload = payload ?? Array.Empty<byte>();
            StartWriterThread();
            lock (_writeQueueLock)
            {
                if (_writeQueue.Count >= MaxWriteQueue)
                {
                    if (!TryDropOldestStep())
                    {
                        SetError(FirmwareErrorKind.Io, "write queue full");
                        return false;
                    }
                }

                var header = new byte[20];
                WriteUInt32(header, 0, ProtocolMagic);
                WriteUInt16(header, 4, ProtocolMajor);
                WriteUInt16(header, 6, ProtocolMinor);
                WriteUInt16(header, 8, (ushort)type);
                WriteUInt16(header, 10, 0);
                WriteUInt32(header, 12, (uint)payload.Length);
                WriteUInt32(header, 16, _sequence++);

                _writeQueue.Enqueue(new PendingWrite
                {
                    Type = type,
                    Header = header,
                    Payload = payload
                });
            }
            _writeSignal?.Set();
            return true;
        }

        private bool TryDropOldestStep()
        {
            if (_writeQueue.Count == 0) return false;
            bool dropped = false;
            var temp = new Queue<PendingWrite>(_writeQueue.Count);
            while (_writeQueue.Count > 0)
            {
                var item = _writeQueue.Dequeue();
                if (!dropped && item.Type == MessageType.Step)
                {
                    dropped = true;
                    continue;
                }
                temp.Enqueue(item);
            }
            while (temp.Count > 0)
            {
                _writeQueue.Enqueue(temp.Dequeue());
            }
            return dropped;
        }

        private bool ReadPacket(out MessageType type, out byte[] payload)
        {
            type = MessageType.Log;
            payload = null;
            if (_pipeClient == null) return false;

            var header = new byte[20];
            if (!ReadExact(header, 0, header.Length)) return false;

            uint magic = ReadUInt32(header, 0);
            if (magic != ProtocolMagic) return false;

            ushort major = ReadUInt16(header, 4);
            ushort minor = ReadUInt16(header, 6);
            if (major != ProtocolMajor) return false;
            if (minor > ProtocolMinor && !_versionWarned)
            {
                _versionWarned = true;
                UnityEngine.Debug.LogWarning($"[FirmwareClient] Protocol minor {minor} > {ProtocolMinor}. Proceeding with best-effort parsing.");
            }

            type = (MessageType)ReadUInt16(header, 8);
            uint size = ReadUInt32(header, 12);
            if (size > MaxPayloadBytes) return false;
            if (size > 0)
            {
                payload = new byte[size];
                if (!ReadExact(payload, 0, payload.Length)) return false;
            }
            else
            {
                payload = Array.Empty<byte>();
            }
            return true;
        }

        private bool ReadExact(byte[] buffer, int offset, int count)
        {
            int total = 0;
            try
            {
                while (total < count && _pipeClient != null)
                {
                    int read = _pipeClient.Read(buffer, offset + total, count - total);
                    if (read <= 0) return false;
                    total += read;
                }
            }
            catch (Exception ex)
            {
                var kind = IsPipeBroken(ex) ? FirmwareErrorKind.BrokenPipe : FirmwareErrorKind.Io;
                SetError(kind, ex.Message);
                return false;
            }
            return total == count;
        }

        private static bool IsPipeBroken(Exception ex)
        {
            if (ex == null) return false;
            if (ex is ObjectDisposedException) return true;
            if (ex is IOException io)
            {
                string message = io.Message ?? string.Empty;
                if (message.IndexOf("pipe", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (message.IndexOf("broken", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     message.IndexOf("ended", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }
            }
            return false;
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
            buffer[offset + 4] = (byte)(value >> 32);
            buffer[offset + 5] = (byte)(value >> 40);
            buffer[offset + 6] = (byte)(value >> 48);
            buffer[offset + 7] = (byte)(value >> 56);
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24));
        }

        private static ulong ReadUInt64(byte[] buffer, int offset)
        {
            return (ulong)(
                buffer[offset]
                | ((ulong)buffer[offset + 1] << 8)
                | ((ulong)buffer[offset + 2] << 16)
                | ((ulong)buffer[offset + 3] << 24)
                | ((ulong)buffer[offset + 4] << 32)
                | ((ulong)buffer[offset + 5] << 40)
                | ((ulong)buffer[offset + 6] << 48)
                | ((ulong)buffer[offset + 7] << 56));
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        private static ulong ReadBits(byte[] data, int bitOffset, int bitCount)
        {
            if (data == null || bitCount <= 0) return 0;
            ulong value = 0;
            for (int i = 0; i < bitCount; i++)
            {
                int index = bitOffset + i;
                int byteIndex = index >> 3;
                int bitIndex = index & 7;
                if (byteIndex >= data.Length) break;
                if ((data[byteIndex] & (1 << bitIndex)) != 0)
                {
                    value |= 1UL << i;
                }
            }
            return value;
        }

        private static void WriteFixedString(byte[] buffer, int offset, int size, string value)
        {
            for (int i = 0; i < size; i++) buffer[offset + i] = 0;
            if (string.IsNullOrWhiteSpace(value)) return;
            var bytes = Encoding.UTF8.GetBytes(value);
            int count = Mathf.Min(bytes.Length, size - 1);
            Buffer.BlockCopy(bytes, 0, buffer, offset, count);
        }

        private static long NowMicros()
        {
            return (long)(Stopwatch.GetTimestamp() * 1_000_000.0 / Stopwatch.Frequency);
        }

        private static string ReadFixedString(byte[] buffer, int offset, int size)
        {
            int len = 0;
            for (int i = 0; i < size; i++)
            {
                if (buffer[offset + i] == 0) break;
                len++;
            }
            return len == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, offset, len);
        }

        private BoardState GetBoardState(string boardId)
        {
            if (string.IsNullOrWhiteSpace(boardId)) boardId = "board";
            if (!_boardStates.TryGetValue(boardId, out var state))
            {
                state = new BoardState();
                _boardStates[boardId] = state;
            }
            return state;
        }
    }
}
