using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RobotTwin.CoreSim.IPC
{
    public interface IFirmwareClient
    {
        Task ConnectAsync();
        void Disconnect();
        FirmwareStepResult Step(FirmwareStepRequest request);
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

    public sealed class FirmwareLogEventArgs : EventArgs
    {
        public FirmwareLogEventArgs(string boardId, FirmwareLogLevel level, string message)
        {
            BoardId = boardId;
            Level = level;
            Message = message;
        }

        public string BoardId { get; }
        public FirmwareLogLevel Level { get; }
        public string Message { get; }
    }

    public class FirmwareStepRequest
    {
        public ulong StepSequence { get; set; }
        public float RailVoltage { get; set; } = 5.0f;
        public uint DeltaMicros { get; set; } = 100000;
        public int[] PinStates { get; set; } = Array.Empty<int>();
        public float[] AnalogVoltages { get; set; } = Array.Empty<float>();
    }

    public class FirmwarePerfCounters
    {
        public ulong Cycles { get; set; }
        public ulong AdcSamples { get; set; }
        public ulong[] UartTxBytes { get; set; } = new ulong[4];
        public ulong[] UartRxBytes { get; set; } = new ulong[4];
        public ulong SpiTransfers { get; set; }
        public ulong TwiTransfers { get; set; }
        public ulong WdtResets { get; set; }
        public ulong DroppedOutputs { get; set; }

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
    }

    public class FirmwareDebugCounters
    {
        public uint FlashBytes { get; set; }
        public uint SramBytes { get; set; }
        public uint EepromBytes { get; set; }
        public uint IoBytes { get; set; }
        public uint CpuHz { get; set; }
        public ushort ProgramCounter { get; set; }
        public ushort StackPointer { get; set; }
        public byte StatusRegister { get; set; }
        public ushort StackHighWater { get; set; }
        public ushort HeapTopAddress { get; set; }
        public ushort StackMinAddress { get; set; }
        public ushort DataSegmentEnd { get; set; }
        public ulong StackOverflows { get; set; }
        public ulong InvalidMemoryAccesses { get; set; }
        public ulong InterruptCount { get; set; }
        public ulong InterruptLatencyMax { get; set; }
        public ulong TimingViolations { get; set; }
        public ulong CriticalSectionCycles { get; set; }
        public ulong SleepCycles { get; set; }
        public ulong FlashAccessCycles { get; set; }
        public ulong UartOverflows { get; set; }
        public ulong TimerOverflows { get; set; }
        public ulong BrownOutResets { get; set; }
        public ulong GpioStateChanges { get; set; }
        public ulong PwmCycles { get; set; }
        public ulong I2cTransactions { get; set; }
        public ulong SpiTransactions { get; set; }

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

    public class FirmwareDebugBitset
    {
        public ushort BitCount { get; set; }
        public byte[] Raw { get; set; } = Array.Empty<byte>();
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

    public class FirmwareStepResult
    {
        public ulong StepSequence { get; set; }
        public ulong TickCount { get; set; }
        public int[] PinStates { get; set; } = new int[70];
        public string SerialOutput { get; set; } = string.Empty;
        public FirmwarePerfCounters PerfCounters { get; set; } = new FirmwarePerfCounters();
        public FirmwareDebugCounters DebugCounters { get; set; } = new FirmwareDebugCounters();
        public FirmwareDebugBitset DebugBits { get; set; } = new FirmwareDebugBitset();
        public ulong OutputTimestampMicros { get; set; }
    }

    public sealed class FirmwareClient : IFirmwareClient
    {
        private const string DefaultPipeName = "RoboTwin.FirmwareEngine";
        private const uint ProtocolMagic = FirmwareProtocol.ProtocolMagic;
        private const ushort ProtocolMajor = FirmwareProtocol.ProtocolMajor;
        private const ushort ProtocolMinor = FirmwareProtocol.ProtocolMinor;
        private const int PinCount = 70;
        private const int AnalogCount = 16;
        private const int BoardIdSize = 64;
        private const int BoardProfileSize = 64;
        private const uint ClientFlags = 1u << 8; // Lockstep mode hint
        private const uint FeatureTimestampMicros = 1u << 0;
        private const uint FeaturePerfCounters = 1u << 1;
        private const int PipeNotFoundError = 2;
        private const int PipeBusyError = 231;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool WaitNamedPipe(string name, int timeout);

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

        private sealed class BoardState
        {
            public ulong LastSequence;
            public ulong LastTick;
            public readonly int[] PinOutputs = new int[PinCount];
            public readonly StringBuilder SerialBuffer = new StringBuilder();
            public readonly FirmwarePerfCounters Perf = new FirmwarePerfCounters();
            public readonly FirmwareDebugCounters Debug = new FirmwareDebugCounters();
            public readonly FirmwareDebugBitset DebugBits = new FirmwareDebugBitset();
            public readonly object SequenceLock = new object();
            public ulong LastTimestampMicros;
        }

        private NamedPipeClientStream? _pipeClient;
        private Thread? _readerThread;
        private volatile bool _readerRunning;
        private readonly object _writeLock = new object();
        private readonly object _stateLock = new object();
        private uint _sequence = 1;
        private string _pipeName = DefaultPipeName;
        private readonly Dictionary<string, BoardState> _boardStates = new Dictionary<string, BoardState>(StringComparer.OrdinalIgnoreCase);
        private uint _serverFlags;
        private bool _versionWarned;
        private bool _timestampSyncValid;
        private long _timestampOffsetMicros;
        private const long TimestampResyncThresholdMicros = 5_000_000;

        public string PipeName => _pipeName;
        public string BoardId { get; set; } = "board";
        public string BoardProfile { get; set; } = "ArduinoUno";
        public bool DropStaleOutputs { get; set; } = true;
        public double MaxOutputAgeMs { get; set; } = 250.0;
        public int StepTimeoutMs { get; set; } = 2000;
        public bool StrictStepSequence { get; set; } = true;
        public string ExtraLaunchArguments { get; set; } = string.Empty;

        public event EventHandler<FirmwareLogEventArgs>? LogReceived;

        public void Configure(string pipeName)
        {
            _pipeName = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName;
        }

        public async Task ConnectAsync()
        {
            if (_pipeClient != null && _pipeClient.IsConnected) return;
            if (!IsPipeAvailable(_pipeName))
            {
                throw new IOException($"Firmware pipe unavailable: {_pipeName}");
            }
            _pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            await _pipeClient.ConnectAsync(5000).ConfigureAwait(false);
            try
            {
                _pipeClient.ReadTimeout = 1000;
                _pipeClient.WriteTimeout = 200;
            }
            catch
            {
            }
            if (!SendHello())
            {
                Disconnect();
                throw new IOException("Firmware handshake failed.");
            }
            StartReaderThread();
        }

        private static bool IsPipeAvailable(string pipeName)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return true;
            if (string.IsNullOrWhiteSpace(pipeName)) return false;
            string fullName = pipeName.StartsWith(@"\\.\pipe\", StringComparison.Ordinal)
                ? pipeName
                : $@"\\.\pipe\{pipeName}";
            if (WaitNamedPipe(fullName, 0)) return true;
            int err = Marshal.GetLastWin32Error();
            if (err == PipeNotFoundError || err == PipeBusyError) return false;
            return false;
        }

        public void Disconnect()
        {
            StopReaderThread();
            _pipeClient?.Dispose();
            _pipeClient = null;
            _timestampSyncValid = false;
            _timestampOffsetMicros = 0;
        }

        public FirmwareStepResult Step(FirmwareStepRequest request)
        {
            var result = new FirmwareStepResult();
            if (request == null) return result;
            if (_pipeClient == null || !_pipeClient.IsConnected) return result;
            if (!SendStep(BoardId, request)) return result;

            BoardState state;
            lock (_stateLock)
            {
                state = GetBoardState(BoardId);
            }

            lock (state.SequenceLock)
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(1, StepTimeoutMs));
                while (state.LastSequence < request.StepSequence)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }
                    Monitor.Wait(state.SequenceLock, remaining);
                }

                if (StrictStepSequence && state.LastSequence < request.StepSequence)
                {
                    throw new TimeoutException(
                        $"Firmware lockstep timeout. board={BoardId} expected_seq={request.StepSequence} last_seq={state.LastSequence} timeout_ms={StepTimeoutMs}");
                }
            }

            lock (_stateLock)
            {
                result.StepSequence = state.LastSequence;
                result.TickCount = state.LastTick;
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
            }

            return result;
        }

        public bool LoadBvmFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            var data = File.ReadAllBytes(path);
            return LoadBvmBytes(data);
        }

        public bool LoadBvmBytes(byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            if (_pipeClient == null || !_pipeClient.IsConnected) return false;
            var header = new byte[BoardIdSize + BoardProfileSize];
            WriteFixedString(header, 0, BoardIdSize, BoardId);
            WriteFixedString(header, BoardIdSize, BoardProfileSize, BoardProfile ?? string.Empty);
            var payload = new byte[header.Length + data.Length];
            Buffer.BlockCopy(header, 0, payload, 0, header.Length);
            Buffer.BlockCopy(data, 0, payload, header.Length, data.Length);
            return WritePacket(MessageType.LoadBvm, payload);
        }

        public bool InjectFlashBytes(uint address, byte[] data)
        {
            return InjectMemory(FirmwareMemoryType.Flash, address, data);
        }

        public bool InjectMemory(FirmwareMemoryType memoryType, uint address, byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            if (_pipeClient == null || !_pipeClient.IsConnected) return false;
            var payload = new byte[BoardIdSize + 1 + 3 + 4 + 4 + data.Length];
            WriteFixedString(payload, 0, BoardIdSize, BoardId);
            payload[BoardIdSize] = (byte)memoryType;
            WriteUInt32(payload, BoardIdSize + 4, address);
            WriteUInt32(payload, BoardIdSize + 8, (uint)data.Length);
            Buffer.BlockCopy(data, 0, payload, BoardIdSize + 12, data.Length);
            return WritePacket(MessageType.MemoryPatch, payload);
        }

        public void LaunchFirmware(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return;
            string extra = string.IsNullOrWhiteSpace(ExtraLaunchArguments) ? string.Empty : $" {ExtraLaunchArguments}";
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"--pipe {_pipeName} --lockstep{extra}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(startInfo);
        }

        private void StartReaderThread()
        {
            if (_readerThread != null && _readerThread.IsAlive) return;
            _readerRunning = true;
            _readerThread = new Thread(ReadLoop) { IsBackground = true, Name = "FirmwarePipeReader" };
            _readerThread.Start();
        }

        private void StopReaderThread()
        {
            _readerRunning = false;
            if (_readerThread != null && _readerThread.IsAlive)
            {
                _readerThread.Join(500);
            }
            _readerThread = null;
        }

        private void ReadLoop()
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
                return;
            }
            if (type == MessageType.OutputState)
            {
                if (payload == null || payload.Length < BoardIdSize + 16 + PinCount) return;
                lock (_stateLock)
                {
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

                    // If enabled, drop stale outputs without advancing sequence.
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
                        cursor += 1; // reserved
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
                        cursor += 2; // reserved
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

                    if (seq >= state.LastSequence)
                    {
                        state.LastSequence = seq;
                        state.LastTick = tick;
                    }

                    lock (state.SequenceLock)
                    {
                        Monitor.PulseAll(state.SequenceLock);
                    }

                    for (int i = 0; i < PinCount; i++)
                    {
                        byte raw = payload[BoardIdSize + 16 + i];
                        state.PinOutputs[i] = raw == 0xFF ? -1 : raw;
                    }
                }
                return;
            }
            if (type == MessageType.Serial)
            {
                if (payload == null || payload.Length <= BoardIdSize) return;
                lock (_stateLock)
                {
                    string boardId = ReadFixedString(payload, 0, BoardIdSize);
                    var state = GetBoardState(boardId);
                    state.SerialBuffer.Append(Encoding.UTF8.GetString(payload, BoardIdSize, payload.Length - BoardIdSize));
                }
                return;
            }
            if (type == MessageType.Log)
            {
                if (payload == null || payload.Length < BoardIdSize + 1) return;
                string boardId = ReadFixedString(payload, 0, BoardIdSize);
                var level = (FirmwareLogLevel)payload[BoardIdSize];
                string text = payload.Length > BoardIdSize + 1
                    ? Encoding.UTF8.GetString(payload, BoardIdSize + 1, payload.Length - (BoardIdSize + 1))
                    : string.Empty;
                RaiseLog(boardId, level, text);
                Console.WriteLine($"[Firmware:{boardId}] {text}");
                return;
            }
            if (type == MessageType.Error)
            {
                if (payload == null || payload.Length < BoardIdSize + 4) return;
                string boardId = ReadFixedString(payload, 0, BoardIdSize);
                uint code = ReadUInt32(payload, BoardIdSize);
                string text = payload.Length > BoardIdSize + 4
                    ? Encoding.UTF8.GetString(payload, BoardIdSize + 4, payload.Length - (BoardIdSize + 4))
                    : $"Firmware error code {code}";
                RaiseLog(boardId, FirmwareLogLevel.Error, text);
                Console.WriteLine($"[Firmware:{boardId}] {text}");
                return;
            }
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

        private bool SendStep(string boardId, FirmwareStepRequest request)
        {
            if (request == null) return false;
            if (string.IsNullOrWhiteSpace(boardId)) return false;
            var payload = new byte[BoardIdSize + 8 + 4 + PinCount + (AnalogCount * 2) + 8];
            WriteFixedString(payload, 0, BoardIdSize, boardId);
            WriteUInt64(payload, BoardIdSize, request.StepSequence);
            WriteUInt32(payload, BoardIdSize + 8, request.DeltaMicros);
            for (int i = 0; i < PinCount; i++)
            {
                int value = (request.PinStates != null && i < request.PinStates.Length && request.PinStates[i] > 0) ? 1 : 0;
                payload[BoardIdSize + 8 + 4 + i] = (byte)value;
            }
            int analogOffset = BoardIdSize + 8 + 4 + PinCount;
            for (int i = 0; i < AnalogCount; i++)
            {
                float voltage = (request.AnalogVoltages != null && i < request.AnalogVoltages.Length)
                    ? request.AnalogVoltages[i]
                    : 0f;
                if (voltage < 0f) voltage = 0f;
                if (voltage > 5f) voltage = 5f;
                ushort raw = (ushort)Math.Round((voltage / 5f) * 1023f);
                WriteUInt16(payload, analogOffset + (i * 2), raw);
            }
            WriteUInt64(payload, analogOffset + (AnalogCount * 2), (ulong)NowMicros());
            return WritePacket(MessageType.Step, payload);
        }

        private bool WritePacket(MessageType type, byte[] payload)
        {
            if (_pipeClient == null || !_pipeClient.IsConnected) return false;
            payload = payload ?? Array.Empty<byte>();
            var header = new byte[20];
            WriteUInt32(header, 0, ProtocolMagic);
            WriteUInt16(header, 4, ProtocolMajor);
            WriteUInt16(header, 6, ProtocolMinor);
            WriteUInt16(header, 8, (ushort)type);
            WriteUInt16(header, 10, 0);
            WriteUInt32(header, 12, (uint)payload.Length);
            WriteUInt32(header, 16, _sequence++);

            lock (_writeLock)
            {
                try
                {
                    _pipeClient.Write(header, 0, header.Length);
                    if (payload.Length > 0)
                    {
                        _pipeClient.Write(payload, 0, payload.Length);
                    }
                    _pipeClient.Flush();
                    return true;
                }
                catch
                {
                    Disconnect();
                    return false;
                }
            }
        }

        private bool ReadPacket(out MessageType type, out byte[] payload)
        {
            type = MessageType.Log;
            payload = Array.Empty<byte>();
            if (_pipeClient == null) return false;
            var header = new byte[FirmwareProtocol.HeaderSize];
            if (!ReadExact(header, 0, header.Length)) return false;
            if (!FirmwareProtocol.TryParseHeader(header, out var parsed, out _))
            {
                return false;
            }
            ushort minor = parsed.VersionMinor;
            if (minor > ProtocolMinor && !_versionWarned)
            {
                _versionWarned = true;
                Console.WriteLine($"[FirmwareClient] Protocol minor {minor} > {ProtocolMinor}. Proceeding with best-effort parsing.");
            }
            type = (MessageType)parsed.Type;
            uint size = parsed.PayloadSize;
            if (size > 0)
            {
                payload = new byte[size];
                if (!ReadExact(payload, 0, payload.Length)) return false;
            }
            return true;
        }

        private bool ReadExact(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count && _pipeClient != null)
            {
                int read = _pipeClient.Read(buffer, offset + total, count - total);
                if (read <= 0) return false;
                total += read;
            }
            return total == count;
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

        private static ulong ReadBits(byte[] buffer, int bitOffset, int bitCount)
        {
            if (buffer == null || bitCount <= 0) return 0;
            int maxBits = buffer.Length * 8;
            if (bitOffset < 0 || bitOffset + bitCount > maxBits) return 0;
            ulong value = 0;
            for (int bit = 0; bit < bitCount && bit < 64; bit++)
            {
                int target = bitOffset + bit;
                int byteIndex = target / 8;
                int bitIndex = target % 8;
                if ((buffer[byteIndex] & (1 << bitIndex)) != 0)
                {
                    value |= (1UL << bit);
                }
            }
            return value;
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        private static void WriteFixedString(byte[] buffer, int offset, int size, string value)
        {
            for (int i = 0; i < size; i++) buffer[offset + i] = 0;
            if (string.IsNullOrWhiteSpace(value)) return;
            var bytes = Encoding.UTF8.GetBytes(value);
            int count = Math.Min(bytes.Length, size - 1);
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

        private void RaiseLog(string boardId, FirmwareLogLevel level, string message)
        {
            if (LogReceived == null) return;
            var args = new FirmwareLogEventArgs(boardId, level, message ?? string.Empty);
            LogReceived(this, args);
        }
    }
}
