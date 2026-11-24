using System;
using RobotTwin.CoreSim.Firmware.Esp32;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] The two SAR ADCs as the RTC controller (SENS, 0x3FF48800) drives them for one-shot reads
    /// (analogRead, adc_oneshot): the firmware picks the pad (SAR_MEAS_STARTn EN_PAD), the attenuation (SAR_ATTENn, two
    /// bits per channel) and the width (SAR_READ_CTRLn SAMPLE_BIT: 9–12 bits), raises START, polls DONE and reads DATA.
    /// The conversion samples the pad's voltage from the circuit (<see cref="PadVoltage"/>) through the chip's transfer
    /// curve as Espressif characterised it (<see cref="CharacterisedMillivolts"/>: a line set by the eFuse reference,
    /// with a dead zone at the bottom and, at 11 dB, the measured bend near the top), so analogRead gives the codes a real
    /// ESP32 gives and analogReadMilliVolts, calibrated by IDF, gives back the pad voltage. (Fast mode's HAL is the ideal
    /// line instead: <see cref="Esp32Gpio.AdcRaw12"/>.) The chip's raw output is inverted; DATA_INV (which IDF sets)
    /// turns it the right way round.
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const uint SarReadCtrl1 = 0x3FF48800, SarReadCtrl2 = 0x3FF48890;
        private const uint SarAtten1 = 0x3FF48834, SarAtten2 = 0x3FF48838;
        private const uint SarMeasStart1 = 0x3FF48854, SarMeasStart2 = 0x3FF48894;

        /// <summary>The voltage at a GPIO's pad (the circuit's node there), for the ADCs; null reads 0 V.</summary>
        public Func<int, double>? PadVoltage { get; set; }

        /// <summary>Each unit's last result (DATA) and whether it is done.</summary>
        private readonly uint[] _adcData = new uint[2];
        private readonly bool[] _adcDone = new bool[2];

        private bool ReadAdcRegister(uint register, out uint value)
        {
            int unit = register == SarMeasStart1 ? 0 : register == SarMeasStart2 ? 1 : -1;
            if (unit < 0)
            {
                value = 0;
                return false;
            }

            value = (Reg(register) & ~0x1FFFFu) | _adcData[unit] | (_adcDone[unit] ? 1u << 16 : 0);
            return true;
        }

        private bool WriteAdcRegister(uint register, uint value)
        {
            int unit = register == SarMeasStart1 ? 0 : register == SarMeasStart2 ? 1 : -1;
            if (unit < 0) return false;
            bool wasStarted = (Reg(register) & (1u << 17)) != 0, start = (value & (1u << 17)) != 0;
            if (!start) _adcDone[unit] = false;
            else if (!wasStarted) Convert(unit, value);
            return true;   // the register file keeps the written word (pads, force bits) for the next read
        }

        /// <summary>One conversion on <paramref name="unit"/> (0: ADC1, 1: ADC2) of the lowest pad its EN_PAD selects.</summary>
        private void Convert(int unit, uint measStart)
        {
            uint pads = (measStart >> 19) & (unit == 0 ? 0xFFFu : 0x3FFu);
            int channel = pads == 0 ? -1 : LowestBit(pads);
            int gpio = Esp32Gpio.AdcGpio(unit + 1, channel);
            double volts = gpio >= 0 && PadVoltage != null ? PadVoltage(gpio) : 0;
            int attenuation = channel < 0 ? 0 : (int)((Reg(unit == 0 ? SarAtten1 : SarAtten2) >> (channel * 2)) & 3);
            int code = CharacterisedCode(unit, attenuation, AdcVrefMillivolts, volts * 1000);
            uint readCtrl = Reg(unit == 0 ? SarReadCtrl1 : SarReadCtrl2);
            int width = 9 + (int)((readCtrl >> 16) & 3);
            uint data = (uint)(code >> (12 - width));
            bool invert = (readCtrl & (1u << (unit == 0 ? 28 : 29))) != 0;
            uint max = (1u << width) - 1;
            _adcData[unit] = invert ? data : max - data;
            _adcDone[unit] = true;
        }

        // Espressif's characterisation of the ESP32 SAR ADCs (ESP-IDF adc_cali_line_fitting, Apache-2.0): per attenuation
        // (0, 2.5, 6, 11 dB) the line's slope scale and offset (mV) for each unit, and for 11 dB the bend above code 2880 as
        // two curves (reference 1000 mV and 1200 mV) of 20 points 64 codes apart.
        private static readonly int[][] VrefScale = { new[] { 57431, 76236, 105481, 196602 }, new[] { 57236, 76175, 105678, 197170 } };
        private static readonly int[][] VrefOffset = { new[] { 75, 78, 107, 142 }, new[] { 63, 66, 89, 128 } };
        private static readonly int[][] LutLow =
        {
            new[] { 2240, 2297, 2352, 2405, 2457, 2512, 2564, 2616, 2664, 2709, 2754, 2795, 2832, 2868, 2903, 2937, 2969, 3000, 3030, 3060 },
            new[] { 2238, 2293, 2347, 2399, 2451, 2507, 2561, 2613, 2662, 2710, 2754, 2792, 2831, 2869, 2904, 2937, 2968, 2999, 3029, 3059 },
        };
        private static readonly int[][] LutHigh =
        {
            new[] { 2667, 2706, 2745, 2780, 2813, 2844, 2873, 2901, 2928, 2956, 2982, 3006, 3032, 3059, 3084, 3110, 3135, 3160, 3184, 3209 },
            new[] { 2657, 2698, 2738, 2774, 2807, 2838, 2867, 2894, 2921, 2946, 2971, 2996, 3020, 3043, 3067, 3092, 3116, 3139, 3162, 3185 },
        };

        /// <summary>
        /// The pad voltage (mV) that gives 12-bit code <paramref name="raw"/> on <paramref name="unit"/> (0: ADC1, 1: ADC2) at
        /// <paramref name="attenuation"/>, for a chip whose reference is <paramref name="vrefMv"/>: Espressif's model, the
        /// same arithmetic IDF calibrates with.
        /// </summary>
        public static int CharacterisedMillivolts(int unit, int attenuation, int vrefMv, int raw)
        {
            unit &= 1;
            attenuation &= 3;
            raw = Math.Clamp(raw, 0, 4095);
            long coeffA = (long)vrefMv * VrefScale[unit][attenuation] >> 12;
            int linear = (int)(((coeffA * raw + 32768) >> 16) + VrefOffset[unit][attenuation]);
            if (attenuation != 3 || raw < 2880) return linear;
            int i = Math.Min((raw - 2880) / 64, 18);
            int x2 = 1200 - vrefMv, x1 = vrefMv - 1000;
            int y2 = (i + 1) * 64 + 2880 - raw, y1 = raw - (i * 64 + 2880);
            long v = (long)LutLow[unit][i] * x2 * y2 + (long)LutHigh[unit][i] * x1 * y2 + (long)LutLow[unit][i + 1] * x2 * y1 + (long)LutHigh[unit][i + 1] * x1 * y1;
            int lut = (int)((v + 200 * 64 / 2) / (200 * 64));
            if (raw > 2944) return lut;
            int x = raw - 2880;
            return (linear * 64 + lut * x - linear * x + 32) / 64;   // the hand-over from the line to the bend
        }

        /// <summary>The 12-bit code the chip converts <paramref name="millivolts"/> to: the model's inverse (0 below its bottom, 4095 above its top).</summary>
        public static int CharacterisedCode(int unit, int attenuation, int vrefMv, double millivolts)
        {
            if (double.IsNaN(millivolts)) millivolts = 0;
            int lo = 0, hi = 4095;
            if (millivolts <= CharacterisedMillivolts(unit, attenuation, vrefMv, 0)) return 0;
            if (millivolts >= CharacterisedMillivolts(unit, attenuation, vrefMv, 4095)) return 4095;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (CharacterisedMillivolts(unit, attenuation, vrefMv, mid) < millivolts) lo = mid + 1;
                else hi = mid;
            }

            // lo: the first code at or above the voltage; the one below may be nearer.
            if (lo > 0 && millivolts - CharacterisedMillivolts(unit, attenuation, vrefMv, lo - 1) < CharacterisedMillivolts(unit, attenuation, vrefMv, lo) - millivolts) lo--;
            return lo;
        }

        private static int LowestBit(uint v)
        {
            int n = 0;
            while ((v & 1) == 0)
            {
                v >>= 1;
                n++;
            }

            return n;
        }
    }
}
