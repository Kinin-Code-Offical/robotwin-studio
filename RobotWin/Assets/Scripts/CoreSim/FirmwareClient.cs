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
    }

    [Serializable]
    public class FirmwareStepResult
    {
        public int[] PinStates = new int[20];
        public string SerialOutput = string.Empty;
    }

    public class FirmwareClient : MonoBehaviour
    {
        private const string DefaultPipeName = "RoboTwin.FirmwareEngine.v1";
        private const float ConnectRetrySeconds = 1.0f;
        private const int PipeConnectTimeoutMs = 250;
        private const uint ProtocolMagic = 0x57465452; // "RTFW"
        private const ushort ProtocolMajor = 1;
        private const ushort ProtocolMinor = 0;
        private const int PinCount = 20;
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
        private readonly int[] _pinOutputs = new int[20];
        private readonly int[] _lastInputs = new int[20];
        private readonly StringBuilder _serialBuffer = new StringBuilder();
        private uint _sequence = 1;
        private string _pipeName = DefaultPipeName;

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

        public bool TryStep(FirmwareStepRequest request, out FirmwareStepResult result)
        {
            result = new FirmwareStepResult();
            if (request == null) return false;
            if (!EnsureConnected()) return false;

            if (!SendStep(request)) return false;

            result.PinStates = new int[_pinOutputs.Length];
            lock (_stateLock)
            {
                Array.Copy(_pinOutputs, result.PinStates, result.PinStates.Length);
                if (_serialBuffer.Length > 0)
                {
                    result.SerialOutput = _serialBuffer.ToString();
                    _serialBuffer.Clear();
                }
            }

            return true;
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
            if (!EnsureConnected()) return false;
            if (!WritePacket(MessageType.LoadBvm, data))
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
                if (payload == null || payload.Length < 8 + PinCount) return;
                lock (_stateLock)
                {
                    for (int i = 0; i < PinCount; i++)
                    {
                        _pinOutputs[i] = payload[8 + i];
                    }
                }
                return;
            }
            if (type == MessageType.Serial)
            {
                if (payload == null || payload.Length == 0) return;
                lock (_stateLock)
                {
                    _serialBuffer.Append(Encoding.UTF8.GetString(payload));
                }
                return;
            }
            if (type == MessageType.Log)
            {
                if (payload == null || payload.Length < 1) return;
                string text = payload.Length > 1 ? Encoding.UTF8.GetString(payload, 1, payload.Length - 1) : string.Empty;
                UnityEngine.Debug.Log($"[Firmware] {text}");
                return;
            }
            if (type == MessageType.Error)
            {
                string text = payload != null && payload.Length > 4
                    ? Encoding.UTF8.GetString(payload, 4, payload.Length - 4)
                    : "Unknown firmware error";
                UnityEngine.Debug.LogWarning($"[Firmware] {text}");
                return;
            }
        }

        private bool SendStep(FirmwareStepRequest request)
        {
            if (request == null) return false;
            var payload = new byte[4 + PinCount];
            WriteUInt32(payload, 0, request.DeltaMicros);
            for (int i = 0; i < PinCount; i++)
            {
                int value = (request.PinStates != null && i < request.PinStates.Length && request.PinStates[i] > 0) ? 1 : 0;
                payload[4 + i] = (byte)value;
                _lastInputs[i] = value;
            }
            return WritePacket(MessageType.Step, payload);
        }

        private bool SendHello()
        {
            var payload = new byte[8];
            WriteUInt32(payload, 0, ClientFlags);
            WriteUInt32(payload, 4, PinCount);
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
    }
}
