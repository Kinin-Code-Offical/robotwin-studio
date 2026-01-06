using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RobotTwin.Game.RaspberryPi
{
    public sealed class RpiRuntimeManager : IDisposable
    {
        private readonly Dictionary<int, int> _gpioState = new Dictionary<int, int>();
        private readonly RpiSharedMemoryTransport _transport = new RpiSharedMemoryTransport();
        private RpiRuntimeConfig _config;
        private string _status = "offline";
        private string _logPath;
        private StreamWriter _logWriter;

        private float _cameraTimer;
        private float _gpioTimer;
        private float _imuTimer;
        private float _timeSyncTimer;
        private float _reconnectTimer;
        private bool _networkSent;
        private ulong _lastDisplaySequence;
        private ulong _lastStatusSequence;
        private bool _connected;
        private bool _allowMock;
        private bool _hasCameraFrame;
        private bool _hasImuSample;
        private string _lastLoggedStatus;

        private byte[] _cameraBuffer;
        private int _cameraWidth;
        private int _cameraHeight;
        private int _cameraStride;
        private float _imuAx;
        private float _imuAy;
        private float _imuAz;
        private float _imuGx;
        private float _imuGy;
        private float _imuGz;
        private float _imuMx;
        private float _imuMy;
        private float _imuMz;

        public Texture2D DisplayTexture { get; private set; }
        public string Status => _status;
        public bool IsRunning => _connected;

        public void Start(RpiRuntimeConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            _config = config;
            _allowMock = config.AllowMock;
            _cameraWidth = config.CameraWidth;
            _cameraHeight = config.CameraHeight;
            _cameraStride = _cameraWidth * 4;
            _cameraBuffer = new byte[_cameraWidth * _cameraHeight * 4];
            _networkSent = false;
            _connected = _transport.Initialize(config, config.CreateShmIfMissing);
            OpenLog();
            _status = _connected ? "connecting" : "unavailable";
            if (!_connected)
            {
                Log("Shared memory not available yet.");
            }
        }

        public void Stop()
        {
            _status = "offline";
            _transport.Dispose();
            _connected = false;
            CloseLog();
        }

        public void Update(float deltaTime, double simTimeSeconds)
        {
            if (_config == null) return;
            if (!_connected)
            {
                _reconnectTimer += deltaTime;
                if (_reconnectTimer > 1.0f)
                {
                    _reconnectTimer = 0f;
                    _transport.Dispose();
                    _connected = _transport.Initialize(_config, false);
                    if (_connected)
                    {
                        _status = "connecting";
                    }
                }
            }

            UpdateStatus();
            UpdateDisplay();
            if (_connected)
            {
                UpdateCamera(deltaTime);
                UpdateGpio(deltaTime);
                UpdateImu(deltaTime);
                UpdateTimeSync(deltaTime, simTimeSeconds);
                UpdateNetwork();
            }
        }

        private void UpdateDisplay()
        {
            if (_transport == null) return;
            if (!_transport.TryReadDisplay(out var header, out var payload)) return;
            if (header.Magic != RpiSharedMemoryChannel.Magic) return;
            if (header.Sequence <= _lastDisplaySequence) return;
            _lastDisplaySequence = header.Sequence;

            int width = header.Width > 0 ? header.Width : _config.DisplayWidth;
            int height = header.Height > 0 ? header.Height : _config.DisplayHeight;
            if (width <= 0 || height <= 0) return;
            if (header.PayloadSize <= 0) return;

            if (DisplayTexture == null || DisplayTexture.width != width || DisplayTexture.height != height)
            {
                DisplayTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            }
            byte[] source = payload;
            if (payload.Length != header.PayloadSize)
            {
                var trimmed = new byte[header.PayloadSize];
                Buffer.BlockCopy(payload, 0, trimmed, 0, header.PayloadSize);
                source = trimmed;
            }
            DisplayTexture.LoadRawTextureData(source);
            DisplayTexture.Apply(false);
            if (_connected)
            {
                _status = "display active";
            }
        }

        private void UpdateStatus()
        {
            if (!_transport.TryReadStatus(out var header, out var payload)) return;
            if (header.Sequence <= _lastStatusSequence) return;
            _lastStatusSequence = header.Sequence;
            if (payload.Length < 8) return;

            uint status = BitConverter.ToUInt32(payload, 0);
            string message = Encoding.UTF8.GetString(payload, 8, payload.Length - 8).TrimEnd('\0', ' ');
            string next = string.IsNullOrWhiteSpace(message) ? FormatStatus(status) : message;
            if (!string.Equals(_lastLoggedStatus, next, StringComparison.Ordinal))
            {
                _lastLoggedStatus = next;
                Log($"Status: {next}");
            }
            _status = next;
        }

        private static string FormatStatus(uint status)
        {
            return status switch
            {
                0 => "running",
                1 => "unavailable",
                2 => "qemu missing",
                3 => "image missing",
                4 => "shm error",
                5 => "qemu failed",
                _ => "status unknown"
            };
        }

        private void UpdateCamera(float deltaTime)
        {
            _cameraTimer += deltaTime;
            if (_cameraTimer < 0.1f) return;
            _cameraTimer = 0f;
            if (_allowMock)
            {
                BuildCameraPattern();
            }
            else if (!_hasCameraFrame)
            {
                return;
            }
            _transport.WriteCameraFrame(_cameraWidth, _cameraHeight, _cameraStride, _cameraBuffer);
            _hasCameraFrame = false;
        }

        private void BuildCameraPattern()
        {
            int width = _cameraWidth;
            int height = _cameraHeight;
            byte tick = (byte)(Time.time * 20f);
            int idx = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    _cameraBuffer[idx++] = (byte)(x + tick);
                    _cameraBuffer[idx++] = (byte)(y + tick * 2);
                    _cameraBuffer[idx++] = (byte)(x + y + tick * 3);
                    _cameraBuffer[idx++] = 255;
                }
            }
        }

        private void UpdateGpio(float deltaTime)
        {
            _gpioTimer += deltaTime;
            if (_gpioTimer < 1.0f) return;
            _gpioTimer = 0f;
            if (_gpioState.Count == 0)
            {
                if (!_allowMock) return;
                int pin = 17;
                int next = _gpioState.TryGetValue(pin, out var value) && value != 0 ? 0 : 1;
                _gpioState[pin] = next;
            }
            _transport.WriteGpio(_gpioState);
        }

        private void UpdateImu(float deltaTime)
        {
            _imuTimer += deltaTime;
            if (_imuTimer < 0.2f) return;
            _imuTimer = 0f;
            if (_allowMock)
            {
                float t = Time.time;
                _transport.WriteImu(
                    Mathf.Sin(t) * 0.02f,
                    Mathf.Cos(t) * 0.02f,
                    9.81f,
                    Mathf.Sin(t * 0.5f) * 0.01f,
                    Mathf.Cos(t * 0.5f) * 0.01f,
                    0.0f,
                    0.1f,
                    0.0f,
                    0.0f
                );
                return;
            }

            if (!_hasImuSample) return;
            _transport.WriteImu(_imuAx, _imuAy, _imuAz, _imuGx, _imuGy, _imuGz, _imuMx, _imuMy, _imuMz);
            _hasImuSample = false;
        }

        private void UpdateTimeSync(float deltaTime, double simTimeSeconds)
        {
            _timeSyncTimer += deltaTime;
            if (_timeSyncTimer < 1.0f) return;
            _timeSyncTimer = 0f;
            _transport.WriteTime(simTimeSeconds, DateTime.UtcNow.Ticks);
        }

        private void UpdateNetwork()
        {
            if (_networkSent) return;
            int mode = 0;
            if (string.Equals(_config.NetworkMode, "nat", StringComparison.OrdinalIgnoreCase))
            {
                mode = 1;
            }
            else if (string.Equals(_config.NetworkMode, "bridge", StringComparison.OrdinalIgnoreCase))
            {
                mode = 2;
            }
            _transport.WriteNetworkMode(mode);
            _networkSent = true;
        }

        public void SetCameraFrame(byte[] rgba, int width, int height, int stride)
        {
            if (rgba == null || width <= 0 || height <= 0) return;
            int expected = width * height * 4;
            if (stride <= 0) stride = width * 4;
            if (rgba.Length < expected) return;

            _cameraWidth = width;
            _cameraHeight = height;
            _cameraStride = stride;
            if (_cameraBuffer == null || _cameraBuffer.Length != expected)
            {
                _cameraBuffer = new byte[expected];
            }
            Buffer.BlockCopy(rgba, 0, _cameraBuffer, 0, expected);
            _hasCameraFrame = true;
        }

        public void SetImuSample(float ax, float ay, float az, float gx, float gy, float gz, float mx, float my, float mz)
        {
            _imuAx = ax;
            _imuAy = ay;
            _imuAz = az;
            _imuGx = gx;
            _imuGy = gy;
            _imuGz = gz;
            _imuMx = mx;
            _imuMy = my;
            _imuMz = mz;
            _hasImuSample = true;
        }

        public void SetGpioState(int pin, int value)
        {
            _gpioState[pin] = value;
        }

        public void SetUnavailable(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) reason = "unavailable";
            _status = reason;
            Log($"Unavailable: {reason}");
        }

        private void OpenLog()
        {
            string repoRoot = RpiRuntimeConfig.ResolveRepoRoot();
            _logPath = Path.Combine(repoRoot, "logs", "rpi", "rpi_runtime.log");
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath) ?? repoRoot);
            _logWriter = new StreamWriter(_logPath, append: true, Encoding.UTF8);
        }

        private void CloseLog()
        {
            _logWriter?.Dispose();
            _logWriter = null;
        }

        private void Log(string message)
        {
            if (_logWriter == null) return;
            string stamp = DateTime.UtcNow.ToString("u").TrimEnd('Z');
            _logWriter.WriteLine($"[{stamp}] {message}");
            _logWriter.Flush();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
