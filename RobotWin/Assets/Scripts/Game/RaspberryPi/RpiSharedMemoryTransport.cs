using System;
using System.Collections.Generic;
using System.IO;

namespace RobotTwin.Game.RaspberryPi
{
    public sealed class RpiSharedMemoryTransport : IDisposable
    {
        private RpiSharedMemoryChannel _display;
        private RpiSharedMemoryChannel _camera;
        private RpiSharedMemoryChannel _gpio;
        private RpiSharedMemoryChannel _imu;
        private RpiSharedMemoryChannel _timeSync;
        private RpiSharedMemoryChannel _network;
        private RpiSharedMemoryChannel _status;

        private readonly byte[] _gpioPayload = new byte[RpiSharedMemoryChannel.GpioPayloadBytes];
        private readonly byte[] _imuPayload = new byte[RpiSharedMemoryChannel.ImuPayloadBytes];
        private readonly byte[] _timePayload = new byte[RpiSharedMemoryChannel.TimePayloadBytes];
        private readonly byte[] _netPayload = new byte[RpiSharedMemoryChannel.NetworkPayloadBytes];
        private readonly byte[] _statusPayload = new byte[RpiSharedMemoryChannel.StatusPayloadBytes];

        public bool IsConnected { get; private set; }

        public bool Initialize(RpiRuntimeConfig config, bool createIfMissing)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            string shmDir = config.SharedMemoryDir;
            Directory.CreateDirectory(shmDir);

            _display = new RpiSharedMemoryChannel(Path.Combine(shmDir, "rpi_display.shm"),
                config.DisplayWidth * config.DisplayHeight * 4);
            if (!_display.TryOpen(createIfMissing))
            {
                Dispose();
                return false;
            }

            _camera = new RpiSharedMemoryChannel(Path.Combine(shmDir, "rpi_camera.shm"),
                config.CameraWidth * config.CameraHeight * 4);
            if (!_camera.TryOpen(createIfMissing))
            {
                Dispose();
                return false;
            }

            _gpio = new RpiSharedMemoryChannel(Path.Combine(shmDir, "rpi_gpio.shm"), RpiSharedMemoryChannel.GpioPayloadBytes);
            if (!_gpio.TryOpen(createIfMissing))
            {
                Dispose();
                return false;
            }

            _imu = new RpiSharedMemoryChannel(Path.Combine(shmDir, "rpi_imu.shm"), RpiSharedMemoryChannel.ImuPayloadBytes);
            if (!_imu.TryOpen(createIfMissing))
            {
                Dispose();
                return false;
            }

            _timeSync = new RpiSharedMemoryChannel(Path.Combine(shmDir, "rpi_time.shm"), RpiSharedMemoryChannel.TimePayloadBytes);
            if (!_timeSync.TryOpen(createIfMissing))
            {
                Dispose();
                return false;
            }

            _network = new RpiSharedMemoryChannel(Path.Combine(shmDir, "rpi_net.shm"), RpiSharedMemoryChannel.NetworkPayloadBytes);
            if (!_network.TryOpen(createIfMissing))
            {
                Dispose();
                return false;
            }

            _status = new RpiSharedMemoryChannel(Path.Combine(shmDir, "rpi_status.shm"), RpiSharedMemoryChannel.StatusPayloadBytes);
            if (!_status.TryOpen(createIfMissing))
            {
                Dispose();
                return false;
            }

            IsConnected = true;
            return true;
        }

        public bool TryReadDisplay(out RpiSharedHeader header, out byte[] payload)
        {
            if (_display == null)
            {
                header = default;
                payload = Array.Empty<byte>();
                return false;
            }
            return _display.TryRead(out header, out payload);
        }

        public void WriteCameraFrame(int width, int height, int stride, byte[] payload)
        {
            _camera?.Write(width, height, stride, payload);
        }

        public void WriteGpio(Dictionary<int, int> pins)
        {
            if (_gpio == null) return;
            Array.Clear(_gpioPayload, 0, _gpioPayload.Length);
            int count = Math.Min(pins?.Count ?? 0, 32);
            BitConverter.GetBytes(count).CopyTo(_gpioPayload, 0);
            if (count == 0)
            {
                _gpio.Write(0, 0, 0, _gpioPayload);
                return;
            }

            int offset = 4;
            int written = 0;
            foreach (var kvp in pins)
            {
                if (written >= count) break;
                BitConverter.GetBytes(kvp.Key).CopyTo(_gpioPayload, offset);
                BitConverter.GetBytes(kvp.Value).CopyTo(_gpioPayload, offset + 4);
                offset += 8;
                written++;
            }
            _gpio.Write(0, 0, 0, _gpioPayload);
        }

        public void WriteImu(float ax, float ay, float az, float gx, float gy, float gz, float mx, float my, float mz)
        {
            if (_imu == null) return;
            Array.Clear(_imuPayload, 0, _imuPayload.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(ax), 0, _imuPayload, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(ay), 0, _imuPayload, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(az), 0, _imuPayload, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(gx), 0, _imuPayload, 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(gy), 0, _imuPayload, 16, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(gz), 0, _imuPayload, 20, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(mx), 0, _imuPayload, 24, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(my), 0, _imuPayload, 28, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(mz), 0, _imuPayload, 32, 4);
            _imu.Write(0, 0, 0, _imuPayload);
        }

        public void WriteTime(double simSeconds, long utcTicks)
        {
            if (_timeSync == null) return;
            Array.Clear(_timePayload, 0, _timePayload.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(simSeconds), 0, _timePayload, 0, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(utcTicks), 0, _timePayload, 8, 8);
            _timeSync.Write(0, 0, 0, _timePayload);
        }

        public void WriteNetworkMode(int mode)
        {
            if (_network == null) return;
            Array.Clear(_netPayload, 0, _netPayload.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(mode), 0, _netPayload, 0, 4);
            _network.Write(0, 0, 0, _netPayload);
        }

        public bool TryReadStatus(out RpiSharedHeader header, out byte[] payload)
        {
            if (_status == null)
            {
                header = default;
                payload = Array.Empty<byte>();
                return false;
            }
            return _status.TryRead(out header, out payload);
        }

        public void Dispose()
        {
            _display?.Dispose();
            _camera?.Dispose();
            _gpio?.Dispose();
            _imu?.Dispose();
            _timeSync?.Dispose();
            _network?.Dispose();
            _status?.Dispose();
            IsConnected = false;
        }
    }
}
