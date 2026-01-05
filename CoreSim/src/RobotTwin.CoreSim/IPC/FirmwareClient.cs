using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
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
        }
    }

    public class FirmwareStepResult
    {
        public ulong StepSequence { get; set; }
        public int[] PinStates { get; set; } = new int[70];
        public string SerialOutput { get; set; } = string.Empty;
        public FirmwarePerfCounters PerfCounters { get; set; } = new FirmwarePerfCounters();
    }

    public sealed class FirmwareClient : IFirmwareClient
    {
        private const string DefaultPipeName = "RoboTwin.FirmwareEngine";
        private const uint ProtocolMagic = 0x57465452; // "RTFW"
        private const ushort ProtocolMajor = 1;
        private const ushort ProtocolMinor = 0;
        private const int PinCount = 70;
        private const int AnalogCount = 16;
        private const int BoardIdSize = 64;
        private const int BoardProfileSize = 64;
        private const uint ClientFlags = 1; // Lockstep

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
            Error = 9
        }

        private sealed class BoardState
        {
            public ulong LastSequence;
            public readonly int[] PinOutputs = new int[PinCount];
            public readonly StringBuilder SerialBuffer = new StringBuilder();
            public readonly FirmwarePerfCounters Perf = new FirmwarePerfCounters();
            public readonly object SequenceLock = new object();
        }

        private NamedPipeClientStream? _pipeClient;
        private Thread? _readerThread;
        private volatile bool _readerRunning;
        private readonly object _writeLock = new object();
        private readonly object _stateLock = new object();
        private uint _sequence = 1;
        private string _pipeName = DefaultPipeName;
        private readonly Dictionary<string, BoardState> _boardStates = new Dictionary<string, BoardState>(StringComparer.OrdinalIgnoreCase);

        public string PipeName => _pipeName;
        public string BoardId { get; set; } = "board";
        public string BoardProfile { get; set; } = "ArduinoUno";

        public void Configure(string pipeName)
        {
            _pipeName = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName;
        }

        public async Task ConnectAsync()
        {
            if (_pipeClient != null && _pipeClient.IsConnected) return;
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

        public void Disconnect()
        {
            StopReaderThread();
            _pipeClient?.Dispose();
            _pipeClient = null;
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
                // Wait for the specific sequence
                // We use a loop to handle spurious wakeups
                while (state.LastSequence < request.StepSequence)
                {
                    if (!Monitor.Wait(state.SequenceLock, 2000))
                    {
                        // Timeout
                        break;
                    }
                }
            }

            lock (_stateLock)
            {
                result.StepSequence = state.LastSequence;
                Array.Copy(state.PinOutputs, result.PinStates, result.PinStates.Length);
                if (state.SerialBuffer.Length > 0)
                {
                    result.SerialOutput = state.SerialBuffer.ToString();
                    state.SerialBuffer.Clear();
                }
                result.PerfCounters.CopyFrom(state.Perf);
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

        public void LaunchFirmware(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return;
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"--pipe {_pipeName} --lockstep",
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
                    state.LastSequence = seq;
                    lock (state.SequenceLock)
                    {
                        Monitor.PulseAll(state.SequenceLock);
                    }

                    for (int i = 0; i < PinCount; i++)
                    {
                        byte raw = payload[BoardIdSize + 16 + i];
                        state.PinOutputs[i] = raw == 0xFF ? -1 : raw;
                    }
                    int offset = BoardIdSize + 16 + PinCount;
                    int needed = offset + (13 * 8);
                    if (payload.Length >= needed)
                    {
                        state.Perf.Cycles = ReadUInt64(payload, offset); offset += 8;
                        state.Perf.AdcSamples = ReadUInt64(payload, offset); offset += 8;
                        for (int i = 0; i < state.Perf.UartTxBytes.Length; i++)
                        {
                            state.Perf.UartTxBytes[i] = ReadUInt64(payload, offset); offset += 8;
                        }
                        for (int i = 0; i < state.Perf.UartRxBytes.Length; i++)
                        {
                            state.Perf.UartRxBytes[i] = ReadUInt64(payload, offset); offset += 8;
                        }
                        state.Perf.SpiTransfers = ReadUInt64(payload, offset); offset += 8;
                        state.Perf.TwiTransfers = ReadUInt64(payload, offset); offset += 8;
                        state.Perf.WdtResets = ReadUInt64(payload, offset);
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
                string text = payload.Length > BoardIdSize + 1
                    ? Encoding.UTF8.GetString(payload, BoardIdSize + 1, payload.Length - (BoardIdSize + 1))
                    : string.Empty;
                Console.WriteLine($"[Firmware:{boardId}] {text}");
                return;
            }
            if (type == MessageType.Error)
            {
                if (payload == null || payload.Length < BoardIdSize + 4) return;
                string boardId = ReadFixedString(payload, 0, BoardIdSize);
                string text = payload.Length > BoardIdSize + 4
                    ? Encoding.UTF8.GetString(payload, BoardIdSize + 4, payload.Length - (BoardIdSize + 4))
                    : "Unknown firmware error";
                Console.WriteLine($"[Firmware:{boardId}] {text}");
                return;
            }
        }

        private bool SendHello()
        {
            var payload = new byte[16];
            WriteUInt32(payload, 0, ClientFlags);
            WriteUInt32(payload, 4, PinCount);
            WriteUInt32(payload, 8, BoardIdSize);
            WriteUInt32(payload, 12, AnalogCount);
            return WritePacket(MessageType.Hello, payload);
        }

        private bool SendStep(string boardId, FirmwareStepRequest request)
        {
            if (request == null) return false;
            if (string.IsNullOrWhiteSpace(boardId)) return false;
            var payload = new byte[BoardIdSize + 8 + 4 + PinCount + (AnalogCount * 2)];
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
            var header = new byte[20];
            if (!ReadExact(header, 0, header.Length)) return false;
            uint magic = ReadUInt32(header, 0);
            if (magic != ProtocolMagic) return false;
            type = (MessageType)ReadUInt16(header, 8);
            uint size = ReadUInt32(header, 12);
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
