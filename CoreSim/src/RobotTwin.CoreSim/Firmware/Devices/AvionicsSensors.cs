using System;

namespace RobotTwin.CoreSim.Firmware.Devices
{
    /// <summary>
    /// [aero] Base of the clocked avionics sensors: conversions take real (simulated) time read from
    /// <see cref="Clock"/>, physical inputs are set by the host every step, noise is deterministic
    /// (seeded Gaussian). Every device speaks I²C (<see cref="II2cDevice"/>) and has an SPI slave
    /// side (<see cref="SpiSelect"/> / <see cref="SpiTransfer"/> / <see cref="SpiDeselect"/>) for a bus
    /// master to drive byte by byte.
    /// </summary>
    public abstract class ClockedSensor
    {
        private Random _rng;

        protected ClockedSensor(int seed)
        {
            Seed = seed;
            _rng = new Random(seed);
        }

        /// <summary>Simulated time source (s). Without it the sensor converts instantly.</summary>
        public Func<double>? Clock { get; set; }

        public int Seed { get; }

        /// <summary>False when the supply is below its minimum: the device does not answer.</summary>
        public bool Powered { get; set; } = true;

        /// <summary>Noise on/off (tests switch it off to check the register maths exactly).</summary>
        public bool Noise { get; set; } = true;

        protected double Now => Clock?.Invoke() ?? 0;

        protected double Gauss()
        {
            if (!Noise) return 0;
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        protected void ReseedNoise() => _rng = new Random(Seed);
    }

    /// <summary>
    /// [aero] Bosch BMP280 barometer (datasheet BST-BMP280-DS001 rev. 1.26). Register map: calibration
    /// 0x88–0x9F, id 0xD0 = 0x58, reset 0xE0 (0xB6), status 0xF3 (measuring bit 3, im_update bit 0),
    /// ctrl_meas 0xF4 (osrs_t[7:5], osrs_p[4:2], mode[1:0]), config 0xF5 (t_sb[7:5], filter[4:2]),
    /// press 0xF7–0xF9, temp 0xFA–0xFC (20-bit, left-aligned). Raw ADC values are produced by inverting
    /// the datasheet's own integer compensation (§3.11.3) with the datasheet example trimming
    /// coefficients, so firmware using the official formulas reads back the true pressure/temperature
    /// to within the resolution. Timing: t_meas,typ = 1 + 2·T_os + 2·P_os + 0.5 ms (§9.1), standby
    /// from t_sb in normal mode; forced mode measures once then sleeps. Resolution 16 + (osrs − 1) bits,
    /// 20 bits with the IIR filter, which follows x[n] = (x[n−1]·(c − 1) + x_new)/c (§3.3.3). Pressure
    /// noise ≈ 1.3 Pa RMS at ×1 falling ∝ 1/√os (approximates the datasheet noise table).
    /// Burst reads are shadowed: data registers update only between transactions.
    /// </summary>
    public sealed class Bmp280Timed : ClockedSensor, II2cDevice
    {
        public const byte ChipIdValue = 0x58;
        public const byte RegCalib = 0x88, RegId = 0xD0, RegReset = 0xE0, RegStatus = 0xF3, RegCtrlMeas = 0xF4, RegConfig = 0xF5, RegPress = 0xF7, RegTemp = 0xFA;

        // Datasheet example trimming parameters (§3.12).
        public const ushort T1 = 27504, P1 = 36477;
        public const short T2 = 26435, T3 = -1000, P2 = -10685, P3 = 3024, P4 = 2855, P5 = 140, P6 = -7, P7 = 15500, P8 = -14600, P9 = 6000;

        private static readonly double[] Standby = { 0.5, 62.5, 125, 250, 500, 1000, 2000, 4000 };
        private readonly byte[] _regs = new byte[256];
        private int _pointer;
        private bool _expectPointer;
        private bool _inTransaction;
        private double _convStart = double.NegativeInfinity, _convEnd = double.NegativeInfinity;
        private bool _converting;
        private double _filtP = double.NaN, _filtT = double.NaN;
        private int _pendP = 0x80000, _pendT = 0x80000;
        private bool _pendingLatch;
        private int _spiState;
        private int _spiAddr;
        private bool _spiRead;

        public Bmp280Timed(byte address = 0x76, int seed = 280) : base(seed)
        {
            if (address != 0x76 && address != 0x77) throw new ArgumentOutOfRangeException(nameof(address), "BMP280 adresi 0x76 (SDO = GND) veya 0x77 (SDO = VDDIO).");
            Address = address;
            Reset();
        }

        public byte Address { get; }

        /// <summary>True static pressure at the sensor port (Pa).</summary>
        public double Pressure { get; set; } = 101325;

        /// <summary>Die temperature (°C).</summary>
        public double TemperatureC { get; set; } = 25;

        public int Mode => _regs[RegCtrlMeas] & 3;
        public int PressureOversampling => Os((_regs[RegCtrlMeas] >> 2) & 7);
        public int TemperatureOversampling => Os((_regs[RegCtrlMeas] >> 5) & 7);
        public int FilterCoefficient => ((_regs[RegConfig] >> 2) & 7) switch { 0 => 1, 1 => 2, 2 => 4, 3 => 8, _ => 16 };

        /// <summary>Typical measurement time (s) for the current settings.</summary>
        public double MeasurementTime
        {
            get
            {
                int t = TemperatureOversampling, p = PressureOversampling;
                return (1 + 2 * t + (p > 0 ? 2 * p + 0.5 : 0)) * 1e-3;
            }
        }

        public double StandbyTime => Standby[(_regs[RegConfig] >> 5) & 7] * 1e-3;

        /// <summary>RMS pressure noise (Pa) for the current oversampling (before the IIR filter).</summary>
        public double PressureNoise => 1.3 / Math.Sqrt(Math.Max(1, PressureOversampling));

        public int Conversions { get; private set; }

        private static int Os(int code) => code == 0 ? 0 : 1 << Math.Min(4, code - 1);

        public byte PeekRegister(int r) => _regs[r & 0xFF];

        public void Reset()
        {
            Array.Clear(_regs, 0, _regs.Length);
            _regs[RegId] = ChipIdValue;
            W16(0x88, T1); W16(0x8A, T2); W16(0x8C, T3);
            W16(0x8E, P1); W16(0x90, P2); W16(0x92, P3); W16(0x94, P4); W16(0x96, P5);
            W16(0x98, P6); W16(0x9A, P7); W16(0x9C, P8); W16(0x9E, P9);
            SetData(RegPress, 0x80000);
            SetData(RegTemp, 0x80000);
            _converting = false;
            _filtP = _filtT = double.NaN;
            _pendingLatch = false;
            ReseedNoise();
        }

        private void W16(int r, int v)
        {
            _regs[r] = (byte)v;
            _regs[r + 1] = (byte)(v >> 8);
        }

        private void SetData(int r, int raw20)
        {
            _regs[r] = (byte)(raw20 >> 12);
            _regs[r + 1] = (byte)(raw20 >> 4);
            _regs[r + 2] = (byte)((raw20 & 0xF) << 4);
        }

        // ================================================================ time

        /// <summary>Brings the conversion state up to the current time (called on every bus access; hosts may call it too).</summary>
        public void Update()
        {
            double now = Now;
            int guard = 0;
            while (_converting && now >= _convEnd && guard++ < 64)
            {
                Convert();
                if (Mode == 3)
                {
                    // Normal mode: standby, then the next conversion.
                    _convStart = _convEnd + StandbyTime;
                    _convEnd = _convStart + MeasurementTime;
                }
                else
                {
                    _converting = false;
                    _regs[RegCtrlMeas] = (byte)(_regs[RegCtrlMeas] & ~3);   // forced → sleep
                }
            }

            if (guard >= 64 && Mode == 3)
            {
                _convStart = now;
                _convEnd = now + MeasurementTime;
            }

            if (_pendingLatch && !_inTransaction) Latch();
            _regs[RegStatus] = (byte)(_converting && now >= _convStart && now < _convEnd ? 0x08 : 0x00);
        }

        private void Convert()
        {
            Conversions++;
            int osT = TemperatureOversampling, osP = PressureOversampling;
            int c = FilterCoefficient;
            double t = TemperatureC + 0.003 * Gauss();
            double p = Pressure + PressureNoise * Gauss();
            if (c > 1)
            {
                _filtT = double.IsNaN(_filtT) ? t : (_filtT * (c - 1) + t) / c;
                _filtP = double.IsNaN(_filtP) ? p : (_filtP * (c - 1) + p) / c;
                t = _filtT;
                p = _filtP;
            }

            int adcT = osT == 0 ? 0x80000 : Quantise(RawTemperature(t), osT, c > 1);
            int tFine = TFine(osT == 0 ? RawTemperature(t) : adcT);
            int adcP = osP == 0 ? 0x80000 : Quantise(RawPressure(p, tFine), osP, c > 1);
            _pendT = adcT;
            _pendP = adcP;
            _pendingLatch = true;
            if (!_inTransaction) Latch();
        }

        private void Latch()
        {
            SetData(RegTemp, _pendT);
            SetData(RegPress, _pendP);
            _pendingLatch = false;
        }

        private static int Quantise(int raw, int os, bool filtered)
        {
            if (filtered) return raw;
            int bits = 16 + (int)Math.Round(Math.Log(os, 2));
            int drop = 20 - Math.Min(20, bits);
            return (raw >> drop) << drop;
        }

        // ================================================================ compensation (datasheet §3.11.3) and its inverse

        public static int TFine(int adcT)
        {
            int var1 = (((adcT >> 3) - (T1 << 1)) * T2) >> 11;
            int var2 = (((((adcT >> 4) - T1) * ((adcT >> 4) - T1)) >> 12) * T3) >> 14;
            return var1 + var2;
        }

        /// <summary>Temperature in 0.01 °C from a raw reading.</summary>
        public static int CompensateTemperature(int adcT) => (TFine(adcT) * 5 + 128) >> 8;

        /// <summary>Pressure in Pa·256 (Q24.8) from a raw reading and t_fine (64-bit integer formula).</summary>
        public static uint CompensatePressure(int adcP, int tFine)
        {
            long var1 = (long)tFine - 128000;
            long var2 = var1 * var1 * P6;
            var2 += (var1 * P5) << 17;
            var2 += (long)P4 << 35;
            var1 = ((var1 * var1 * P3) >> 8) + ((var1 * P2) << 12);
            var1 = ((1L << 47) + var1) * P1 >> 33;
            if (var1 == 0) return 0;
            long p = 1048576 - adcP;
            p = (((p << 31) - var2) * 3125) / var1;
            var1 = ((long)P9 * (p >> 13) * (p >> 13)) >> 25;
            var2 = ((long)P8 * p) >> 19;
            p = ((p + var1 + var2) >> 8) + ((long)P7 << 4);
            return (uint)p;
        }

        /// <summary>Raw 20-bit temperature whose compensated value is closest to <paramref name="tempC"/> (monotonic bisection).</summary>
        public static int RawTemperature(double tempC)
        {
            int target = (int)Math.Round(tempC * 100);
            int lo = 0, hi = (1 << 20) - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (CompensateTemperature(mid) < target) lo = mid;
                else hi = mid;
            }

            return Math.Abs(CompensateTemperature(lo) - target) <= Math.Abs(CompensateTemperature(hi) - target) ? lo : hi;
        }

        /// <summary>Raw 20-bit pressure whose compensated value is closest to <paramref name="pa"/> (pressure falls as the raw value rises).</summary>
        public static int RawPressure(double pa, int tFine)
        {
            double target = pa * 256;
            int lo = 0, hi = (1 << 20) - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (CompensatePressure(mid, tFine) > target) lo = mid;
                else hi = mid;
            }

            return Math.Abs(CompensatePressure(lo, tFine) - target) <= Math.Abs(CompensatePressure(hi, tFine) - target) ? lo : hi;
        }

        // ================================================================ registers

        private void WriteRegister(int r, byte v)
        {
            switch (r)
            {
                case RegReset:
                    if (v == 0xB6) Reset();
                    return;
                case RegCtrlMeas:
                {
                    _regs[r] = v;
                    int mode = v & 3;
                    if (mode == 1 || mode == 2)
                    {
                        _convStart = Now;
                        _convEnd = _convStart + MeasurementTime;
                        _converting = true;
                    }
                    else if (mode == 3)
                    {
                        _convStart = Now;
                        _convEnd = _convStart + MeasurementTime;
                        _converting = true;
                    }
                    else _converting = false;

                    return;
                }

                case RegConfig:
                    _regs[r] = (byte)(v & 0xFD);
                    return;
            }
        }

        // ================================================================ I²C

        public bool Start(bool read)
        {
            if (!Powered) return false;
            Update();
            _inTransaction = true;
            _expectPointer = !read;
            return true;
        }

        public bool Write(byte value)
        {
            if (_expectPointer)
            {
                _pointer = value;
                _expectPointer = false;
                return true;
            }

            // Write mode is (register, data) pairs.
            WriteRegister(_pointer, value);
            _expectPointer = true;
            return true;
        }

        public byte Read(bool ack)
        {
            byte v = _regs[_pointer & 0xFF];
            _pointer = (_pointer + 1) & 0xFF;
            return v;
        }

        public void Stop()
        {
            _inTransaction = false;
            _expectPointer = false;
            if (_pendingLatch) Latch();
        }

        // ================================================================ SPI (mode 0/3; bit 7 of the control byte = read)

        public void SpiSelect()
        {
            Update();
            _inTransaction = true;
            _spiState = 0;
        }

        public byte SpiTransfer(byte mosi)
        {
            if (!Powered) return 0xFF;
            if (_spiState == 0)
            {
                _spiRead = (mosi & 0x80) != 0;
                _spiAddr = mosi | 0x80;   // the register address is the 7 LSBs with bit 7 set
                _spiState = 1;
                return 0xFF;
            }

            if (_spiRead)
            {
                byte v = _regs[_spiAddr & 0xFF];
                _spiAddr = (_spiAddr + 1) & 0xFF;
                return v;
            }

            WriteRegister(_spiAddr, mosi);
            _spiState = 0;   // next byte is again a control byte
            return 0xFF;
        }

        public void SpiDeselect()
        {
            _inTransaction = false;
            if (_pendingLatch) Latch();
        }
    }

    /// <summary>
    /// [aero] TE Connectivity MS5611-01BA03 barometer and the MS5837-30BA depth sensor (same command
    /// protocol). Commands: 0x1E reset, 0x40+osr D1 (pressure), 0x50+osr D2 (temperature), 0x00 ADC read
    /// (24 bit; 0 if no conversion has finished), 0xA0 + 2k PROM read (C0…C7, CRC-4 per AN520).
    /// Conversion times (typ.): OSR 256…4096 = 0.54/1.06/2.08/4.13/8.22 ms (+16.44 ms for the MS5837's
    /// OSR 8192). Raw D1/D2 come from inverting the datasheet compensation including the second-order
    /// low-temperature terms. RMS noise per datasheet: MS5611 6.5/4.2/2.7/1.8/1.2 Pa;
    /// MS5837-30BA ≈ 20 Pa at OSR 8192 (×√2 per halving).
    /// </summary>
    public sealed class Ms56xx : ClockedSensor, II2cDevice
    {
        private static readonly double[] ConvMs = { 0.54, 1.06, 2.08, 4.13, 8.22, 16.44 };
        private readonly ushort[] _prom = new ushort[8];
        private int _cmd = -1;
        private int _readIndex;
        private uint _adc;
        private bool _adcValid;
        private bool _expectCommand;
        private double _convEnd = double.NegativeInfinity;
        private int _convKind;   // 1 = D1, 2 = D2
        private int _convOsr;
        private byte[] _out = new byte[3];
        private int _outLen;

        public Ms56xx(bool depthSensor = false, byte address = 0, int seed = 5611) : base(seed)
        {
            IsMs5837 = depthSensor;
            Address = address != 0 ? address : depthSensor ? (byte)0x76 : (byte)0x77;
            if (depthSensor)
            {
                // Representative factory calibration of an MS5837-30BA (words C1…C6).
                _prom[1] = 34982; _prom[2] = 36352; _prom[3] = 20328; _prom[4] = 22354; _prom[5] = 26646; _prom[6] = 26146;
                _prom[0] = 0x001A;   // sensor version bits
            }
            else
            {
                // MS5611 datasheet example coefficients.
                _prom[1] = 40127; _prom[2] = 36924; _prom[3] = 23317; _prom[4] = 23282; _prom[5] = 33464; _prom[6] = 28312;
                _prom[0] = 0;
            }

            if (depthSensor) _prom[0] = (ushort)((_prom[0] & 0x0FFF) | (Crc4(_prom, true) << 12));
            else _prom[7] = (ushort)((_prom[7] & 0xFFF0) | Crc4(_prom, false));
        }

        public byte Address { get; }
        public bool IsMs5837 { get; }

        public double Pressure { get; set; } = 101325;
        public double TemperatureC { get; set; } = 20;

        public ushort Prom(int i) => _prom[i & 7];
        public int Conversions { get; private set; }

        public static double ConversionTime(int osrIndex) => ConvMs[Math.Max(0, Math.Min(5, osrIndex))] * 1e-3;

        /// <summary>RMS pressure noise (Pa) at an OSR index (0 = 256 … 5 = 8192).</summary>
        public double PressureNoise(int osrIndex) => IsMs5837
            ? 20 * Math.Pow(Math.Sqrt(2), 5 - Math.Min(5, osrIndex))
            : new[] { 6.5, 4.2, 2.7, 1.8, 1.2, 1.2 }[Math.Min(5, osrIndex)];

        // ================================================================ compensation (datasheets) and inverse

        /// <summary>Temperature (0.01 °C) and pressure (MS5611: 0.01 mbar = Pa; MS5837-30BA: 0.1 mbar = 10 Pa) from D1, D2.</summary>
        public (int Temp, int Pressure) Compensate(uint d1, uint d2)
        {
            long c1 = _prom[1], c2 = _prom[2], c3 = _prom[3], c4 = _prom[4], c5 = _prom[5], c6 = _prom[6];
            long dT = d2 - c5 * 256;
            long temp = 2000 + dT * c6 / 8388608;
            long off = c2 * 65536 + c4 * dT / 128;
            long sens = c1 * 32768 + c3 * dT / 256;
            long t2, off2, sens2;
            if (IsMs5837)
            {
                if (temp < 2000)
                {
                    t2 = 3 * dT * dT / 8589934592L;
                    off2 = 3 * (temp - 2000) * (temp - 2000) / 2;
                    sens2 = 5 * (temp - 2000) * (temp - 2000) / 8;
                    if (temp < -1500)
                    {
                        off2 += 7 * (temp + 1500) * (temp + 1500);
                        sens2 += 4 * (temp + 1500) * (temp + 1500);
                    }
                }
                else
                {
                    t2 = 2 * dT * dT / 137438953472L;
                    off2 = (temp - 2000) * (temp - 2000) / 16;
                    sens2 = 0;
                }
            }
            else
            {
                if (temp < 2000)
                {
                    t2 = dT * dT / 2147483648L;
                    off2 = 5 * (temp - 2000) * (temp - 2000) / 2;
                    sens2 = 5 * (temp - 2000) * (temp - 2000) / 4;
                    if (temp < -1500)
                    {
                        off2 += 7 * (temp + 1500) * (temp + 1500);
                        sens2 += 11 * (temp + 1500) * (temp + 1500) / 2;
                    }
                }
                else
                {
                    t2 = off2 = sens2 = 0;
                }
            }

            temp -= t2;
            off -= off2;
            sens -= sens2;
            long p = IsMs5837 ? ((d1 * sens / 2097152) - off) / 8192 : ((d1 * sens / 2097152) - off) / 32768;
            return ((int)temp, (int)p);
        }

        /// <summary>Pressure unit of <see cref="Compensate"/> in Pa.</summary>
        public double PressureUnit => IsMs5837 ? 10 : 1;

        public uint RawTemperature(double tempC)
        {
            int target = (int)Math.Round(tempC * 100);
            uint lo = 0, hi = (1u << 24) - 1;
            while (hi - lo > 1)
            {
                uint mid = (lo + hi) / 2;
                if (Compensate(8000000, mid).Temp < target) lo = mid;
                else hi = mid;
            }

            return hi;
        }

        public uint RawPressure(double pa, uint d2)
        {
            double target = pa / PressureUnit;
            uint lo = 0, hi = (1u << 24) - 1;
            while (hi - lo > 1)
            {
                uint mid = (lo + hi) / 2;
                if (Compensate(mid, d2).Pressure < target) lo = mid;
                else hi = mid;
            }

            return Math.Abs(Compensate(lo, d2).Pressure - target) <= Math.Abs(Compensate(hi, d2).Pressure - target) ? lo : hi;
        }

        /// <summary>CRC-4 of the PROM (AN520; MS5837 keeps it in C0[15:12]).</summary>
        public static int Crc4(ushort[] prom, bool ms5837)
        {
            var p = (ushort[])prom.Clone();
            uint rem = 0;
            if (ms5837)
            {
                p[0] &= 0x0FFF;
                p[7] = 0;
            }
            else
            {
                p[7] &= 0xFF00;
            }

            int words = 8;
            for (int cnt = 0; cnt < words * 2; cnt++)
            {
                if ((cnt & 1) == 1) rem ^= (uint)(p[cnt >> 1] & 0x00FF);
                else rem ^= (uint)(p[cnt >> 1] >> 8);
                for (int bit = 8; bit > 0; bit--)
                {
                    if ((rem & 0x8000) != 0) rem = (rem << 1) ^ 0x3000;
                    else rem <<= 1;
                    rem &= 0xFFFF;
                }
            }

            return (int)((rem >> 12) & 0xF);
        }

        // ================================================================ commands

        private void Update()
        {
            if (_convKind != 0 && Now >= _convEnd)
            {
                Conversions++;
                uint d2 = RawTemperature(TemperatureC + 0.005 * Gauss());
                _adc = _convKind == 2 ? d2 : RawPressure(Pressure + PressureNoise(_convOsr) * Gauss(), RawTemperature(TemperatureC));
                _adcValid = true;
                _convKind = 0;
            }
        }

        private void Command(byte c)
        {
            Update();
            if (c == 0x1E)
            {
                _convKind = 0;
                _adcValid = false;
                return;
            }

            if ((c & 0xF0) == 0x40 || (c & 0xF0) == 0x50)
            {
                int osr = (c & 0x0F) / 2;
                if (osr > (IsMs5837 ? 5 : 4)) osr = IsMs5837 ? 5 : 4;
                _convKind = (c & 0xF0) == 0x40 ? 1 : 2;
                _convOsr = osr;
                _convEnd = Now + ConversionTime(osr);
                _adcValid = false;
                return;
            }

            if (c == 0x00)
            {
                uint v = _adcValid ? _adc : 0;
                _out = new[] { (byte)(v >> 16), (byte)(v >> 8), (byte)v };
                _outLen = 3;
                _readIndex = 0;
                _adcValid = false;
                return;
            }

            if ((c & 0xF0) == 0xA0)
            {
                int k = (c >> 1) & 7;
                _out = new[] { (byte)(_prom[k] >> 8), (byte)_prom[k] };
                _outLen = 2;
                _readIndex = 0;
            }
        }

        public bool Start(bool read)
        {
            if (!Powered) return false;
            _expectCommand = !read;
            if (read) Update();
            return true;
        }

        public bool Write(byte value)
        {
            if (!_expectCommand) return true;
            Command(value);
            _expectCommand = false;
            return true;
        }

        public byte Read(bool ack) => _readIndex < _outLen ? _out[_readIndex++] : (byte)0;

        public void Stop() => _expectCommand = false;

        public void SpiSelect() => _cmd = -1;

        public byte SpiTransfer(byte mosi)
        {
            if (!Powered) return 0xFF;
            if (_cmd < 0)
            {
                _cmd = mosi;
                Command(mosi);
                return 0xFE;
            }

            return Read(true);
        }

        public void SpiDeselect() => _cmd = -1;
    }

    /// <summary>
    /// [aero] Analog Devices ADXL375 ±200 g accelerometer (datasheet Rev. B). DEVID 0x00 = 0xE5,
    /// BW_RATE 0x2C, POWER_CTL 0x2D (measure bit 3), INT_SOURCE 0x30 (DATA_READY bit 7), DATA_FORMAT 0x31,
    /// DATAX0…DATAZ1 0x32–0x37 (13-bit two's complement, 49 mg/LSB), offsets OFSX/Y/Z 0x1E–0x20 (0.196 g/LSB).
    /// Output data rate from BW_RATE (3200 Hz × 2^(rate−15)); samples latch at that rate; noise
    /// 5 mg/√Hz × √(ODR/2) (datasheet typical noise density, approximated equal on all axes). I²C
    /// (0x53 / 0x1D, auto-increment) and SPI (bit 7 = read, bit 6 = multi-byte).
    /// </summary>
    public sealed class Adxl375 : ClockedSensor, II2cDevice
    {
        public const double ScaleG = 0.049;
        private readonly byte[] _regs = new byte[64];
        private int _pointer;
        private bool _expectPointer;
        private double _nextSample;
        private int _spiState;
        private int _spiAddr;
        private bool _spiRead, _spiMulti;

        public Adxl375(byte address = 0x53, int seed = 375) : base(seed)
        {
            Address = address;
            Reset();
        }

        public byte Address { get; }

        /// <summary>Specific force on each axis (m/s²); a level part at rest reads +1 g on Z.</summary>
        public double AccelX { get; set; }
        public double AccelY { get; set; }
        public double AccelZ { get; set; } = 9.80665;

        public bool Measuring => (_regs[0x2D] & 0x08) != 0;
        public double OutputRate => 3200.0 * Math.Pow(2, (_regs[0x2C] & 0x0F) - 15);
        public int Samples { get; private set; }

        public byte PeekRegister(int r) => _regs[r & 63];

        public void Reset()
        {
            Array.Clear(_regs, 0, _regs.Length);
            _regs[0x00] = 0xE5;
            _regs[0x2C] = 0x0A;
            _regs[0x31] = 0x0B;
            _regs[0x30] = 0x02;
        }

        public void Update()
        {
            double now = Now;
            if (!Measuring)
            {
                _nextSample = now;
                return;
            }

            if (now < _nextSample) return;
            double odr = OutputRate;
            _nextSample = now + 1.0 / odr;
            Samples++;
            double sigma = 0.005 * Math.Sqrt(odr / 2) * 9.80665;
            Put(0x32, AccelX + sigma * Gauss(), (sbyte)_regs[0x1E]);
            Put(0x34, AccelY + sigma * Gauss(), (sbyte)_regs[0x1F]);
            Put(0x36, AccelZ + sigma * Gauss(), (sbyte)_regs[0x20]);
            _regs[0x30] |= 0x80;
        }

        private void Put(int r, double a, sbyte ofs)
        {
            double g = a / 9.80665 + ofs * 0.196;
            int lsb = (int)Math.Round(g / ScaleG);
            lsb = Math.Max(-4096, Math.Min(4095, lsb));
            _regs[r] = (byte)lsb;
            _regs[r + 1] = (byte)(lsb >> 8);
        }

        private byte ReadReg(int r)
        {
            byte v = _regs[r & 63];
            if (r == 0x37 || r == 0x30) _regs[0x30] &= 0x7F;   // reading the data clears DATA_READY
            return v;
        }

        private void WriteReg(int r, byte v)
        {
            r &= 63;
            if (r == 0x00 || (r >= 0x30 && r <= 0x37 && r != 0x31) || r == 0x39) return;
            _regs[r] = v;
            if (r == 0x2D) _nextSample = Now;
        }

        public bool Start(bool read)
        {
            if (!Powered) return false;
            Update();
            _expectPointer = !read;
            return true;
        }

        public bool Write(byte value)
        {
            if (_expectPointer)
            {
                _pointer = value & 63;
                _expectPointer = false;
                return true;
            }

            WriteReg(_pointer, value);
            _pointer = (_pointer + 1) & 63;
            return true;
        }

        public byte Read(bool ack)
        {
            byte v = ReadReg(_pointer);
            _pointer = (_pointer + 1) & 63;
            return v;
        }

        public void Stop() => _expectPointer = false;

        public void SpiSelect()
        {
            Update();
            _spiState = 0;
        }

        public byte SpiTransfer(byte mosi)
        {
            if (!Powered) return 0xFF;
            if (_spiState == 0)
            {
                _spiRead = (mosi & 0x80) != 0;
                _spiMulti = (mosi & 0x40) != 0;
                _spiAddr = mosi & 0x3F;
                _spiState = 1;
                return 0xFF;
            }

            int a = _spiAddr;
            if (_spiMulti) _spiAddr = (_spiAddr + 1) & 63;
            if (_spiRead) return ReadReg(a);
            WriteReg(a, mosi);
            return 0xFF;
        }

        public void SpiDeselect() => _spiState = 0;
    }
}
