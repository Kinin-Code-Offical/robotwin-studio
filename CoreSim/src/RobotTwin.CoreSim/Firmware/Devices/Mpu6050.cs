using System;

namespace RobotTwin.CoreSim.Firmware.Devices
{
    /// <summary>
    /// InvenSense MPU-6050 6-axis IMU register model on I²C (0x68, or 0x69 with AD0 high).
    /// The first byte of a write transaction sets the register pointer, further bytes
    /// write registers with auto-increment; reads continue from the pointer. The host
    /// sets the physical quantities (<see cref="AccelX"/>… in m/s², <see cref="GyroX"/>…
    /// in rad/s, <see cref="TemperatureC"/>); they are converted with the configured full
    /// scale, saturated, and latched into ACCEL/TEMP/GYRO_OUT at the start of every read
    /// transaction while the chip is awake (so a burst read is coherent). After power-on
    /// the chip sleeps (PWR_MGMT_1 = 0x40) and the output registers hold their last value.
    /// FIFO, DMP, auxiliary I²C master, self-test and the INT pin are not modelled.
    /// </summary>
    public sealed class Mpu6050 : II2cDevice
    {
        public const double StandardGravity = 9.80665;

        public const byte SmplrtDiv = 0x19, Config = 0x1A, GyroConfig = 0x1B, AccelConfig = 0x1C;
        public const byte IntPinCfg = 0x37, IntEnable = 0x38, IntStatus = 0x3A;
        public const byte AccelXoutH = 0x3B, TempOutH = 0x41, GyroXoutH = 0x43;
        public const byte SignalPathReset = 0x68, UserCtrl = 0x6A, PwrMgmt1 = 0x6B, PwrMgmt2 = 0x6C;
        public const byte FifoCountH = 0x72, FifoRw = 0x74, WhoAmI = 0x75;

        private static readonly double[] GyroLsbPerDps = { 131.0, 65.5, 32.8, 16.4 };

        private readonly byte[] _regs = new byte[128];
        private int _pointer;
        private bool _expectPointer;
        private Random _random;

        public Mpu6050(byte address = 0x68)
        {
            if (address != 0x68 && address != 0x69) throw new ArgumentOutOfRangeException(nameof(address), "MPU-6050 address is 0x68 (AD0 low) or 0x69 (AD0 high).");
            Address = address;
            _random = new Random(NoiseSeed);
            Reset();
        }

        public byte Address { get; }

        /// <summary>Increments whenever a register value changes (UI redraw hint).</summary>
        public int Version { get; private set; }

        /// <summary>Specific force along each axis in m/s² (a level, resting chip reads +1 g on Z).</summary>
        public double AccelX { get; set; }
        public double AccelY { get; set; }
        public double AccelZ { get; set; } = StandardGravity;

        /// <summary>Angular rate about each axis in rad/s.</summary>
        public double GyroX { get; set; }
        public double GyroY { get; set; }
        public double GyroZ { get; set; }

        public double TemperatureC { get; set; } = 25.0;

        /// <summary>Gaussian noise standard deviation added to each acceleration sample, m/s² (0 = none).</summary>
        public double AccelNoise { get; set; }

        /// <summary>Gaussian noise standard deviation added to each angular-rate sample, rad/s (0 = none).</summary>
        public double GyroNoise { get; set; }

        /// <summary>Seed of the deterministic noise generator; applied on <see cref="Reset"/>.</summary>
        public int NoiseSeed { get; set; } = 6050;

        public bool Sleeping => (_regs[PwrMgmt1] & 0x40) != 0;

        /// <summary>Accelerometer full scale in g (2, 4, 8 or 16).</summary>
        public int AccelFullScaleG => 2 << ((_regs[AccelConfig] >> 3) & 3);

        /// <summary>Gyroscope full scale in °/s (250, 500, 1000 or 2000).</summary>
        public int GyroFullScaleDps => 250 << ((_regs[GyroConfig] >> 3) & 3);

        public int RegisterPointer => _pointer;

        /// <summary>Reads a register without side effects.</summary>
        public byte PeekRegister(int register) => _regs[register & 0x7F];

        /// <summary>Power-on reset (also triggered by PWR_MGMT_1.DEVICE_RESET).</summary>
        public void Reset()
        {
            Array.Clear(_regs, 0, _regs.Length);
            _regs[PwrMgmt1] = 0x40;
            _regs[WhoAmI] = 0x68;
            _pointer = 0;
            _expectPointer = false;
            _random = new Random(NoiseSeed);
            Version++;
        }

        // ================================================================ II2cDevice

        public bool Start(bool read)
        {
            _expectPointer = !read;
            if (read) Sample();
            return true;
        }

        public bool Write(byte value)
        {
            if (_expectPointer)
            {
                _pointer = value & 0x7F;
                _expectPointer = false;
                return true;
            }

            WriteRegister(_pointer, value);
            Advance();
            return true;
        }

        public byte Read(bool ack)
        {
            int register = _pointer;
            byte value = _regs[register];
            if (register == IntStatus || (_regs[IntPinCfg] & 0x10) != 0) _regs[IntStatus] = 0; // INT_RD_CLEAR
            Advance();
            return value;
        }

        public void Stop()
        {
            _expectPointer = false;
        }

        private void Advance()
        {
            if (_pointer != FifoRw) _pointer = (_pointer + 1) & 0x7F; // FIFO_R_W does not auto-increment
        }

        // ================================================================ registers

        private void WriteRegister(int register, byte value)
        {
            switch (register)
            {
                case WhoAmI:
                case IntStatus:
                case FifoCountH:
                case FifoCountH + 1:
                    return; // read-only
            }

            if (register >= AccelXoutH && register <= 0x60) return; // sensor/external data: read-only

            if (register == PwrMgmt1 && (value & 0x80) != 0)
            {
                Reset(); // DEVICE_RESET: back to defaults (asleep); the bit clears itself
                return;
            }

            if (register == UserCtrl && (value & 0x01) != 0)
            {
                Array.Clear(_regs, AccelXoutH, GyroXoutH + 6 - AccelXoutH); // SIG_COND_RESET clears the sensor registers
                value &= 0xFE;
            }

            if (register == SignalPathReset) value = 0; // self-clearing strobes

            if (_regs[register] == value) return;
            _regs[register] = value;
            Version++;
        }

        private void Sample()
        {
            if (Sleeping) return;
            byte pm2 = _regs[PwrMgmt2];
            double accelLsbPerMs2 = (16384.0 / (1 << ((_regs[AccelConfig] >> 3) & 3))) / StandardGravity;
            double gyroLsbPerRad = GyroLsbPerDps[(_regs[GyroConfig] >> 3) & 3] * 180.0 / Math.PI;

            bool changed = false;
            if ((pm2 & 0x20) == 0) changed |= Store(AccelXoutH + 0, (AccelX + Noise(AccelNoise)) * accelLsbPerMs2);
            if ((pm2 & 0x10) == 0) changed |= Store(AccelXoutH + 2, (AccelY + Noise(AccelNoise)) * accelLsbPerMs2);
            if ((pm2 & 0x08) == 0) changed |= Store(AccelXoutH + 4, (AccelZ + Noise(AccelNoise)) * accelLsbPerMs2);
            if ((_regs[PwrMgmt1] & 0x08) == 0) changed |= Store(TempOutH, (TemperatureC - 36.53) * 340.0); // Temp = raw/340 + 36.53
            if ((pm2 & 0x04) == 0) changed |= Store(GyroXoutH + 0, (GyroX + Noise(GyroNoise)) * gyroLsbPerRad);
            if ((pm2 & 0x02) == 0) changed |= Store(GyroXoutH + 2, (GyroY + Noise(GyroNoise)) * gyroLsbPerRad);
            if ((pm2 & 0x01) == 0) changed |= Store(GyroXoutH + 4, (GyroZ + Noise(GyroNoise)) * gyroLsbPerRad);
            _regs[IntStatus] |= 0x01; // DATA_RDY_INT: a fresh sample is available
            if (changed) Version++;
        }

        private bool Store(int register, double lsb)
        {
            int raw = double.IsNaN(lsb) ? 0 : (int)Math.Max(-32768.0, Math.Min(32767.0, Math.Round(lsb)));
            byte high = (byte)(raw >> 8), low = (byte)raw;
            bool changed = _regs[register] != high || _regs[register + 1] != low;
            _regs[register] = high;
            _regs[register + 1] = low;
            return changed;
        }

        private double Noise(double sigma)
        {
            if (sigma <= 0) return 0;
            double u1 = 1.0 - _random.NextDouble();
            double u2 = _random.NextDouble();
            return sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
