using System;

namespace RobotTwin.CoreSim.Firmware.Devices
{
    /// <summary>
    /// Bosch BMP280 barometric pressure / temperature sensor on I²C (0x76/0x77) or SPI (4-wire, bit 7
    /// of the register byte = read). Full register map: chip id 0x58 at 0xD0, soft reset 0xE0 (0xB6),
    /// status 0xF3, ctrl_meas 0xF4 (oversampling, sleep/forced/normal), config 0xF5, data 0xF7–0xFC
    /// and the factory trimming 0x88–0x9F. Raw ADC values are produced so that the datasheet's own
    /// compensation formulas (applied by the sketch's library) return the true pressure and
    /// temperature of the world at the sensor; noise follows the datasheet's RMS per oversampling.
    /// </summary>
    public sealed class Bmp280 : II2cDevice, ISpiDevice
    {
        // Datasheet example trimming values (section 3.12): a real part has its own; these are valid ones.
        private const ushort T1 = 27504;
        private const short T2 = 26435, T3 = -1000;
        private const ushort P1 = 36477;
        private const short P2 = -10685, P3 = 3024, P4 = 2855, P5 = 140, P6 = -7, P7 = 15500, P8 = -14600, P9 = 6000;

        private readonly byte[] _reg = new byte[256];
        private readonly Func<double> _pressurePa;
        private readonly Func<double> _temperatureC;
        private readonly Random _noise;
        private int _pointer;
        private bool _pointerSet;
        private bool _spiFirst;
        private bool _spiRead;
        private int _spiWritePhase;

        public Bmp280(byte address, Func<double> pressurePa, Func<double> temperatureC, int seed = 1)
        {
            Address = address;
            _pressurePa = pressurePa;
            _temperatureC = temperatureC;
            _noise = new Random(seed);
            Reset();
        }

        public byte Address { get; }

        /// <summary>Number of completed measurements (forced or normal mode).</summary>
        public int Measurements { get; private set; }

        public void Reset()
        {
            Array.Clear(_reg, 0, _reg.Length);
            _reg[0xD0] = 0x58;
            void U16(int a, int v)
            {
                _reg[a] = (byte)(v & 0xFF);
                _reg[a + 1] = (byte)((v >> 8) & 0xFF);
            }

            U16(0x88, T1);
            U16(0x8A, T2);
            U16(0x8C, T3);
            U16(0x8E, P1);
            U16(0x90, P2);
            U16(0x92, P3);
            U16(0x94, P4);
            U16(0x96, P5);
            U16(0x98, P6);
            U16(0x9A, P7);
            U16(0x9C, P8);
            U16(0x9E, P9);
            // Data registers read 0x80000 until the first measurement.
            _reg[0xF7] = 0x80;
            _reg[0xFA] = 0x80;
        }

        // ------------------------------------------------------------------ I²C

        public bool Start(bool read)
        {
            if (!read) _pointerSet = false;
            return true;
        }

        public bool Write(byte value)
        {
            if (!_pointerSet)
            {
                _pointer = value;
                _pointerSet = true;
            }
            else WriteRegister(_pointer++, value);
            return true;
        }

        public byte Read(bool ack) => ReadRegister(_pointer++);

        public void Stop()
        {
        }

        // ------------------------------------------------------------------ SPI

        public void Select(bool selected)
        {
            _spiFirst = selected;
            _spiWritePhase = 0;
        }

        public byte Transfer(byte mosi)
        {
            if (_spiFirst)
            {
                _spiFirst = false;
                _spiRead = (mosi & 0x80) != 0;
                _pointer = mosi | 0x80;   // SPI drops bit 7 of the register address
                return 0xFF;
            }

            if (_spiRead) return ReadRegister(_pointer++);
            // Writes alternate control byte / data byte.
            if (_spiWritePhase == 0) WriteRegister(_pointer, mosi);
            else _pointer = mosi | 0x80;
            _spiWritePhase ^= 1;
            return 0xFF;
        }

        // ------------------------------------------------------------------ registers

        private byte ReadRegister(int a)
        {
            a &= 0xFF;
            if (a == 0xF7 && (_reg[0xF4] & 3) == 3) Measure();   // normal mode: data are fresh at every burst read
            return _reg[a];
        }

        private void WriteRegister(int a, byte v)
        {
            a &= 0xFF;
            switch (a)
            {
                case 0xE0:
                    if (v == 0xB6) Reset();
                    return;
                case 0xF4:
                    _reg[0xF4] = v;
                    int mode = v & 3;
                    if (mode == 1 || mode == 2)
                    {
                        Measure();
                        _reg[0xF4] = (byte)(v & 0xFC);   // forced: back to sleep when done
                    }
                    else if (mode == 3) Measure();
                    return;
                case 0xF5:
                    _reg[0xF5] = (byte)(v & 0xFD);
                    return;
            }
        }

        private static readonly double[] NoisePa = { 0, 2.62, 1.69, 1.13, 0.80, 0.62 };   // RMS by osrs_p (×1, ×2, ×4, ×8, ×16)

        private void Measure()
        {
            Measurements++;
            int osrsT = (_reg[0xF4] >> 5) & 7, osrsP = (_reg[0xF4] >> 2) & 7;
            double t = _temperatureC();
            double p = _pressurePa();
            if (osrsT > 0)
            {
                t += Gauss() * 0.005;
                int adcT = InverseTemperature(t);
                Store(0xFA, adcT, osrsT);
                if (osrsP > 0)
                {
                    int tFine = TFine(adcT);
                    p += Gauss() * NoisePa[Math.Min(5, osrsP)];
                    Store(0xF7, InversePressure(p, tFine), osrsP);
                }
                else Store(0xF7, 0x80000, 1);
            }
            else
            {
                Store(0xFA, 0x80000, 1);
                Store(0xF7, 0x80000, 1);
            }
        }

        /// <summary>20-bit result at oversampling ×1 has 16 bits; each doubling adds one bit (datasheet 3.4).</summary>
        private void Store(int msb, int adc, int osrs)
        {
            int bits = 16 + Math.Min(4, Math.Max(0, osrs - 1));
            adc &= ~((1 << (20 - bits)) - 1);
            _reg[msb] = (byte)(adc >> 12);
            _reg[msb + 1] = (byte)(adc >> 4);
            _reg[msb + 2] = (byte)((adc & 0xF) << 4);
        }

        private double Gauss()
        {
            double u1 = 1.0 - _noise.NextDouble(), u2 = _noise.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        // ---- datasheet compensation (8.2, integer versions) and their inverses

        public static int TFine(int adcT)
        {
            // 64-bit so the inverse search can probe the whole 20-bit range without overflow;
            // identical to the datasheet's 32-bit code for real readings.
            long var1 = ((((long)adcT >> 3) - ((long)T1 << 1)) * T2) >> 11;
            long d = ((long)adcT >> 4) - T1;
            long var2 = (((d * d) >> 12) * T3) >> 14;
            return (int)(var1 + var2);
        }

        /// <summary>Temperature in 0.01 °C.</summary>
        public static int CompensateTemperature(int adcT) => (TFine(adcT) * 5 + 128) >> 8;

        /// <summary>Pressure in Pa as unsigned Q24.8 (divide by 256).</summary>
        public static uint CompensatePressure(int adcP, int tFine)
        {
            long var1 = (long)tFine - 128000;
            long var2 = var1 * var1 * P6;
            var2 += (var1 * P5) << 17;
            var2 += (long)P4 << 35;
            var1 = ((var1 * var1 * P3) >> 8) + ((var1 * P2) << 12);
            var1 = ((((long)1) << 47) + var1) * P1 >> 33;
            if (var1 == 0) return 0;
            long p = 1048576 - adcP;
            p = (((p << 31) - var2) * 3125) / var1;
            var1 = ((long)P9 * (p >> 13) * (p >> 13)) >> 25;
            var2 = ((long)P8 * p) >> 19;
            p = ((p + var1 + var2) >> 8) + ((long)P7 << 4);
            return (uint)p;
        }

        private static int InverseTemperature(double celsius)
        {
            int target = (int)Math.Round(celsius * 100);
            int lo = 0, hi = (1 << 20) - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (CompensateTemperature(mid) < target) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }

        private static int InversePressure(double pascal, int tFine)
        {
            // Compensated pressure falls as the raw value rises.
            double target = pascal * 256;
            int lo = 0, hi = (1 << 20) - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (CompensatePressure(mid, tFine) > target) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }

        /// <summary>ISA pressure at an altitude above sea level (m) for a sea-level pressure (Pa).</summary>
        public static double PressureAt(double altitude, double seaLevel = 101325) =>
            seaLevel * Math.Pow(1 - 2.25577e-5 * altitude, 5.25588);
    }
}
