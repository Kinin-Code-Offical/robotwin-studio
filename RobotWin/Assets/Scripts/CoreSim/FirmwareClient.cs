using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
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

    [Serializable]
    public class FirmwareStepResult
    {
        public const int PinUnknown = -1;
        public int[] PinStates = new int[70];
        public string SerialOutput = string.Empty;
        public FirmwarePerfCounters PerfCounters = new FirmwarePerfCounters();
        public ulong OutputTimestampMicros;

        public FirmwareStepResult()
        {
            Array.Fill(PinStates, PinUnknown);
        }
    }

    public class FirmwareClient : MonoBehaviour
    {
        private const string DefaultPipeName = "RoboTwin.FirmwareEngine";
        private const float ConnectRetrySeconds = 1.0f;
        private const int PipeConnectTimeoutMs = 250;
        private const uint ProtocolMagic = 0x57465452; // "RTFW"
        private const ushort ProtocolMajor = 1;
        private const ushort ProtocolMinor = 1;
        private const int PinCount = 70;
        private const int AnalogCount = 16;
        private const int BoardIdSize = 64;
        private const int BoardProfileSize = 64;
        private const uint ClientFlags = 1u << 8; // Lockstep mode hint
        private const uint FeatureTimestampMicros = 1u << 0;
        private const uint FeaturePerfCounters = 1u << 1;
        private const uint MaxPayloadBytes = 8 * 1024 * 1024;

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

        private Process _firmwareProcess;
        private NamedPipeClientStream _pipeClient;
        private Thread _readerThread;
        private volatile bool _readerRunning;
        private volatile bool _readerHealthy;
        private float _nextConnectAttemptTime;
        private readonly object _writeLock = new object();
        private readonly object _stateLock = new object();
        private readonly int[] _lastInputs = new int[PinCount];
        private uint _sequence = 1;
        private ulong _stepSequence = 1;
        private string _pipeName = DefaultPipeName;
        private readonly Dictionary<string, BoardState> _boardStates = new Dictionary<string, BoardState>(StringComparer.OrdinalIgnoreCase);
        private uint _serverFlags;
        private bool _versionWarned;

        public bool IsConnected => _pipeClient != null && _pipeClient.IsConnected;
        public bool ReaderHealthy => _readerHealthy;
        public DateTime LastPacketUtc { get; private set; } = DateTime.MinValue;
        public DateTime LastWriteUtc { get; private set; } = DateTime.MinValue;
        public string LastError { get; private set; } = string.Empty;

        private sealed class BoardState
        {
            public ulong LastSequence;
            public readonly object SequenceLock = new object();
            public readonly int[] PinOutputs = new int[PinCount];
            public readonly StringBuilder SerialBuffer = new StringBuilder();
            public readonly FirmwarePerfCounters Perf = new FirmwarePerfCounters();
            public ulong LastTimestampMicros;

            public BoardState()
            {
                Array.Fill(PinOutputs, FirmwareStepResult.PinUnknown);
            }
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
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
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
                _pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
                UnityEngine.Debug.Log("[FirmwareClient] Connecting to firmware pipe...");
                _pipeClient.Connect(PipeConnectTimeoutMs);
                try
                {
                    _pipeClient.ReadTimeout = 1000;
                    _pipeClient.WriteTimeout = 200;
                }
                catch { }
                UnityEngine.Debug.Log("[FirmwareClient] Connected to firmware pipe.");
                if (!SendHello())
                {
                    Disconnect();
                    return false;
                }
                StartReaderThread();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                UnityEngine.Debug.LogWarning($"[FirmwareClient] Pipe connection failed: {ex.Message}");
                return false;
            }
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

            // Wait for a fresh OutputState matching this step.
            lock (state.SequenceLock)
            {
                var deadline = Time.realtimeSinceStartup + 0.1f; // 100ms budget to avoid stalling Unity.
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

            result.PinStates = new int[PinCount];
            lock (_stateLock)
            {
                // If we timed out waiting for the expected sequence, treat as failed step.
                if (state.LastSequence < expectedSeq)
                {
                    return false;
                }
                Array.Copy(state.PinOutputs, result.PinStates, result.PinStates.Length);
                if (state.SerialBuffer.Length > 0)
                {
                    result.SerialOutput = state.SerialBuffer.ToString();
                    state.SerialBuffer.Clear();
                }
                result.PerfCounters.CopyFrom(state.Perf);
                result.OutputTimestampMicros = state.LastTimestampMicros;
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
            _pipeClient?.Dispose();
            _pipeClient = null;
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
                // OutputStatePayload: board_id (64) + step_sequence (8) + tick_count (8) + pins (70) + perf...
                if (payload == null || payload.Length < BoardIdSize + 16 + PinCount) return;
                lock (_stateLock)
                {
                    LastPacketUtc = DateTime.UtcNow;
                    string boardId = ReadFixedString(payload, 0, BoardIdSize);
                    var state = GetBoardState(boardId);
                    ulong seq = ReadUInt64(payload, BoardIdSize);
                    ulong previous = state.LastSequence;
                    lock (state.SequenceLock)
                    {
                        if (seq >= state.LastSequence)
                        {
                            state.LastSequence = seq;
                        }
                        Monitor.PulseAll(state.SequenceLock);
                    }
                    int offset = BoardIdSize + 16 + PinCount;
                    int perfSize = 13 * 8;
                    int perfEnd = offset + perfSize;
                    if (payload.Length >= perfEnd)
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

                    if (previous > 0 && seq > previous + 1)
                    {
                        state.Perf.DroppedOutputs += seq - previous - 1;
                    }

                    if (payload.Length >= perfEnd + 8)
                    {
                        state.LastTimestampMicros = ReadUInt64(payload, perfEnd);
                        if (DropStaleOutputs && state.LastTimestampMicros > 0)
                        {
                            long ageMicros = NowMicros() - (long)state.LastTimestampMicros;
                            if (ageMicros > (long)(MaxOutputAgeMs * 1000.0))
                            {
                                state.Perf.DroppedOutputs += 1;
                                return;
                            }
                        }
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
                string text = payload.Length > BoardIdSize + 1
                    ? Encoding.UTF8.GetString(payload, BoardIdSize + 1, payload.Length - (BoardIdSize + 1))
                    : string.Empty;
                UnityEngine.Debug.Log($"[Firmware:{boardId}] {text}");
                return;
            }
            if (type == MessageType.Error)
            {
                if (payload == null || payload.Length < BoardIdSize + 4) return;
                LastPacketUtc = DateTime.UtcNow;
                string boardId = ReadFixedString(payload, 0, BoardIdSize);
                string text = payload.Length > BoardIdSize + 4
                    ? Encoding.UTF8.GetString(payload, BoardIdSize + 4, payload.Length - (BoardIdSize + 4))
                    : "Unknown firmware error";
                LastError = $"Firmware error from {boardId}: {text}";
                UnityEngine.Debug.LogWarning($"[Firmware:{boardId}] {text}");
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
                    LastWriteUtc = DateTime.UtcNow;
                    _pipeClient.Write(header, 0, header.Length);
                    if (payload.Length > 0)
                    {
                        _pipeClient.Write(payload, 0, payload.Length);
                    }
                    _pipeClient.Flush();
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    UnityEngine.Debug.LogWarning($"[FirmwareClient] Pipe write failed: {ex.Message}");
                    Disconnect();
                    return false;
                }
            }
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
                LastError = ex.Message;
                return false;
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
