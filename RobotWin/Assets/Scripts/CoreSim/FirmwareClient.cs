using System;
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
    public class FirmwareStepResult
    {
        public int[] PinStates = new int[20];
        public string SerialOutput = string.Empty;
    }

    public class FirmwareClient : MonoBehaviour
    {
        private const string DefaultPipeName = "RoboTwin.FirmwareEngine";
        private const float ConnectRetrySeconds = 1.0f;
        private const int PipeConnectTimeoutMs = 250;
        private const uint ProtocolMagic = 0x57465452; // "RTFW"
        private const ushort ProtocolMajor = 1;
        private const ushort ProtocolMinor = 0;
        private const int PinCount = 20;
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

        private Process _firmwareProcess;
        private NamedPipeClientStream _pipeClient;
        private Thread _readerThread;
        private volatile bool _readerRunning;
        private float _nextConnectAttemptTime;
        private readonly object _writeLock = new object();
        private readonly object _stateLock = new object();
        private readonly int[] _lastInputs = new int[PinCount];
        private uint _sequence = 1;
        private string _pipeName = DefaultPipeName;
        private readonly Dictionary<string, BoardState> _boardStates = new Dictionary<string, BoardState>(StringComparer.OrdinalIgnoreCase);

        private sealed class BoardState
        {
            public readonly int[] PinOutputs = new int[PinCount];
            public readonly StringBuilder SerialBuffer = new StringBuilder();
        }

        public string PipeName => _pipeName;

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
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = $"--pipe {_pipeName} --lockstep",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

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

            if (!SendStep(boardId, request)) return false;

            result.PinStates = new int[PinCount];
            lock (_stateLock)
            {
                var state = GetBoardState(boardId);
                Array.Copy(state.PinOutputs, result.PinStates, result.PinStates.Length);
                if (state.SerialBuffer.Length > 0)
                {
                    result.SerialOutput = state.SerialBuffer.ToString();
                    state.SerialBuffer.Clear();
                }
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
                UnityEngine.Debug.Log("[FirmwareClient] Firmware handshake complete.");
                return;
            }
            if (type == MessageType.OutputState)
            {
                if (payload == null || payload.Length < BoardIdSize + 8 + PinCount) return;
                lock (_stateLock)
                {
                    string boardId = ReadFixedString(payload, 0, BoardIdSize);
                    var state = GetBoardState(boardId);
                    for (int i = 0; i < PinCount; i++)
                    {
                        byte raw = payload[BoardIdSize + 8 + i];
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
                string text = payload.Length > BoardIdSize + 1
                    ? Encoding.UTF8.GetString(payload, BoardIdSize + 1, payload.Length - (BoardIdSize + 1))
                    : string.Empty;
                UnityEngine.Debug.Log($"[Firmware:{boardId}] {text}");
                return;
            }
            if (type == MessageType.Error)
            {
                if (payload == null || payload.Length < BoardIdSize + 4) return;
                string boardId = ReadFixedString(payload, 0, BoardIdSize);
                string text = payload.Length > BoardIdSize + 4
                    ? Encoding.UTF8.GetString(payload, BoardIdSize + 4, payload.Length - (BoardIdSize + 4))
                    : "Unknown firmware error";
                UnityEngine.Debug.LogWarning($"[Firmware:{boardId}] {text}");
                return;
            }
        }

        private bool SendStep(string boardId, FirmwareStepRequest request)
        {
            if (request == null) return false;
            var payload = new byte[BoardIdSize + 4 + PinCount + (AnalogCount * 2)];
            WriteFixedString(payload, 0, BoardIdSize, boardId);
            WriteUInt32(payload, BoardIdSize, request.DeltaMicros);
            for (int i = 0; i < PinCount; i++)
            {
                int value = (request.PinStates != null && i < request.PinStates.Length && request.PinStates[i] > 0) ? 1 : 0;
                payload[BoardIdSize + 4 + i] = (byte)value;
                _lastInputs[i] = value;
            }
            int analogOffset = BoardIdSize + 4 + PinCount;
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
            return WritePacket(MessageType.Step, payload);
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
                catch (Exception ex)
                {
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

            type = (MessageType)ReadUInt16(header, 8);
            uint size = ReadUInt32(header, 12);
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
