using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>What an emulated ESP32 pad does to its pin: drives it (a level, or a peripheral's PWM), or leaves it to the circuit.</summary>
    public readonly struct Esp32PadDrive
    {
        public Esp32PadDrive(bool driven, bool level, bool openDrain, bool pullUp, bool pullDown, bool inputEnabled, double pwmFrequency, double pwmDuty,
            bool dac = false, double dacVolts = 0)
        {
            IsDac = dac;
            DacVolts = dacVolts;
            Driven = driven;
            Level = level;
            OpenDrain = openDrain;
            PullUp = pullUp;
            PullDown = pullDown;
            InputEnabled = inputEnabled;
            PwmFrequency = pwmFrequency;
            PwmDuty = pwmDuty;
        }

        /// <summary>The output driver is on (for open drain: only while it pulls low).</summary>
        public bool Driven { get; }

        /// <summary>The level it drives (when <see cref="PwmFrequency"/> is NaN).</summary>
        public bool Level { get; }

        public bool OpenDrain { get; }
        public bool PullUp { get; }
        public bool PullDown { get; }
        public bool InputEnabled { get; }

        /// <summary>A peripheral's PWM on the pad (LEDC): its frequency, NaN for a plain level.</summary>
        public double PwmFrequency { get; }

        /// <summary>The PWM's duty, 0–1.</summary>
        public double PwmDuty { get; }

        public bool IsPwm => PwmFrequency > 0;

        /// <summary>The pad is the DAC's analog output (GPIO25: channel 1, GPIO26: channel 2), at <see cref="DacVolts"/>.</summary>
        public bool IsDac { get; }

        public double DacVolts { get; }
    }

    /// <summary>
    /// [esp32emu] The GPIO controller (0x3FF44000) and the IO_MUX (0x3FF49000): the output and output-enable registers
    /// (with their write-1-to-set/clear twins), the GPIO matrix that routes any of 256 peripheral signals to any pad
    /// (FUNC_OUT_SEL, signal 256 = the GPIO_OUT bit) and any pad to a peripheral's input (FUNC_IN_SEL), each pad's
    /// function select, pulls (an RTC pad's in its RTC_IO register) and input enable, open drain (PAD_DRIVER), the input register read from the circuit through
    /// a Schmitt trigger (VIL 0.25·VDD, VIH 0.75·VDD, as fast mode), and the per-pin interrupts (rising, falling, any,
    /// low, high) on source 22. <see cref="PadDrive"/> is what the board applies to the circuit.
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const uint Gpio = 0x3FF44000, IoMux = 0x3FF49000, RtcIo = 0x3FF48400;

        /// <summary>
        /// [esp32hw] Where an RTC pad's pulls are: on the ESP32 the pads that also have an RTC function (GPIO0, 2, 4, 12–15,
        /// 25–27, 32, 33) take their pull-up and pull-down from their RTC_IO register's RUE / RDE bits, which ESP-IDF's
        /// gpio_pullup_en / gpio_pulldown_en set for them (rtc_gpio_pullup_en), not from IO_MUX's; null for the other pads.
        /// </summary>
        private static (uint Register, uint Up, uint Down)? RtcPull(int gpio) => gpio switch
        {
            4 => (RtcIo + 0x94, 1u << 27, 1u << 28),    // TOUCH_PAD0
            0 => (RtcIo + 0x98, 1u << 27, 1u << 28),    // TOUCH_PAD1
            2 => (RtcIo + 0x9C, 1u << 27, 1u << 28),    // TOUCH_PAD2
            15 => (RtcIo + 0xA0, 1u << 27, 1u << 28),   // TOUCH_PAD3
            13 => (RtcIo + 0xA4, 1u << 27, 1u << 28),   // TOUCH_PAD4
            12 => (RtcIo + 0xA8, 1u << 27, 1u << 28),   // TOUCH_PAD5
            14 => (RtcIo + 0xAC, 1u << 27, 1u << 28),   // TOUCH_PAD6
            27 => (RtcIo + 0xB0, 1u << 27, 1u << 28),   // TOUCH_PAD7
            25 => (RtcIo + 0x84, 1u << 27, 1u << 28),   // PAD_DAC1
            26 => (RtcIo + 0x88, 1u << 27, 1u << 28),   // PAD_DAC2
            32 => (RtcIo + 0x8C, 1u << 22, 1u << 23),   // XTAL_32K_PAD, X32P
            33 => (RtcIo + 0x8C, 1u << 27, 1u << 28),   // XTAL_32K_PAD, X32N
            _ => null
        };
        private const int GpioInterruptSource = 22;
        private const int SignalGpioOut = 256;

        // LEDC's output signals in the matrix: high-speed channels 0–7, then low-speed 0–7.
        private const int LedcHsSignal0 = 71, LedcLsSignal0 = 79;

        /// <summary>Each GPIO's IO_MUX register offset (the pads are not in GPIO order); 0: no pad.</summary>
        private static readonly uint[] IoMuxOffset =
        {
            0x44, 0x88, 0x40, 0x84, 0x48, 0x6C, 0x60, 0x64, 0x68, 0x54, // 0–9
            0x58, 0x5C, 0x34, 0x38, 0x30, 0x3C, 0x4C, 0x50, 0x70, 0x74, // 10–19
            0x78, 0x7C, 0x80, 0x8C, 0x90, 0x24, 0x28, 0x2C, 0, 0,       // 20–29
            0, 0, 0x1C, 0x20, 0x14, 0x18, 0x04, 0x08, 0x0C, 0x10,       // 30–39
        };

        /// <summary>The supply the pads swing to, for the input thresholds (the board's 3.3 V rail).</summary>
        public double PadSupply { get; set; } = 3.3;

        private ulong _gpioOut, _gpioEnable, _gpioInput, _gpioStatus;
        private readonly uint[] _gpioPin = new uint[40];
        private readonly uint[] _funcOut = NewFuncOut();
        private readonly uint[] _funcIn = new uint[256];

        private static uint[] NewFuncOut()
        {
            var cfg = new uint[40];
            for (int i = 0; i < cfg.Length; i++) cfg[i] = SignalGpioOut;   // reset: the GPIO_OUT bit
            return cfg;
        }

        /// <summary>The drive pad <paramref name="gpio"/> puts on its pin now.</summary>
        public Esp32PadDrive PadDrive(int gpio)
        {
            if (gpio < 0 || gpio >= 40 || IoMuxOffset[gpio] == 0) return default;
            if (DacOutput(gpio) is { } volts) return new Esp32PadDrive(false, false, false, false, false, false, double.NaN, 0, true, volts);
            uint mux = Reg(IoMux + IoMuxOffset[gpio]);
            bool pullUp = (mux & (1u << 8)) != 0, pullDown = (mux & (1u << 7)) != 0, inputEnabled = (mux & (1u << 9)) != 0;
            if (RtcPull(gpio) is { } rtc)
            {
                uint pad = Reg(rtc.Register);
                pullUp = (pad & rtc.Up) != 0;
                pullDown = (pad & rtc.Down) != 0;
            }

            bool openDrain = (_gpioPin[gpio] & (1u << 2)) != 0;
            int function = (int)((mux >> 12) & 7);
            double frequency = double.NaN, duty = 0;
            bool enabled, level;
            if (function == 2)
            {
                // FUNC_GPIO: the matrix picks the signal
                uint cfg = _funcOut[gpio];
                int signal = (int)(cfg & 0x1FF);
                // OEN_SEL, or the plain GPIO output: GPIO_ENABLE's bit; otherwise the peripheral's own enable (LEDC: always on)
                bool useEnableRegister = (cfg & (1u << 10)) != 0 || signal == SignalGpioOut;
                enabled = !useEnableRegister || (_gpioEnable >> gpio & 1) != 0;
                if ((cfg & (1u << 11)) != 0) enabled = !enabled;
                if (signal == SignalGpioOut) level = (_gpioOut >> gpio & 1) != 0;
                else level = SignalLevel(signal, ref frequency, ref duty);
                if ((cfg & (1u << 9)) != 0)
                {
                    level = !level;
                    duty = 1 - duty;
                }
            }
            else if (gpio == 1 && function == 0)
            {
                // U0TXD, GPIO1's direct function (its reset one): the UART's line, high while idle. The bytes go out as
                // text (Uart0); their bit timing on the pin is not drawn.
                enabled = true;
                level = true;
            }
            else
            {
                // Another pad's direct peripheral function (SPI flash, JTAG …): not modelled, the pad floats.
                enabled = false;
                level = false;
            }

            if (openDrain && enabled && level) enabled = false;   // open drain only pulls low
            return new Esp32PadDrive(enabled, level, openDrain, pullUp, pullDown, inputEnabled, frequency, duty);
        }

        /// <summary>
        /// The DAC on a pad, when it is powered: RTC_IO_PAD_DAC1/2 (GPIO25/26) with XPD_DAC (bit 18) set put the 8-bit value
        /// in bits 26:19 out as VDD × value / 255 (the cosine generator, SENS_SAR_DAC_CTRL2's CW_EN1/2, is not drawn: it
        /// reads as mid-scale). Null when the DAC is off.
        /// </summary>
        public double? DacOutput(int gpio)
        {
            int channel = gpio == 25 ? 0 : gpio == 26 ? 1 : -1;
            if (channel < 0) return null;
            uint pad = Reg(0x3FF48484 + 4 * (uint)channel);
            if ((pad & (1u << 18)) == 0) return null;
            bool cosine = (Reg(0x3FF4889C) & (1u << (24 + channel))) != 0;
            uint value = cosine ? 128u : (pad >> 19) & 0xFF;
            return PadSupply * value / 255.0;
        }

        /// <summary>A matrix output signal's level, or its PWM.</summary>
        private bool SignalLevel(int signal, ref double frequency, ref double duty)
        {
            int channel = signal >= LedcHsSignal0 && signal < LedcHsSignal0 + 8 ? signal - LedcHsSignal0
                : signal >= LedcLsSignal0 && signal < LedcLsSignal0 + 8 ? 8 + signal - LedcLsSignal0 : -1;
            if (channel >= 0)
            {
                var pwm = LedcOutput(channel);
                if (pwm == null)
                {
                    // the channel's output is off: it holds its idle level (IDLE_LV)
                    return (_ledcChannels[channel].Conf0 & 8) != 0;
                }

                if (pwm.Value.Duty <= 0) return false;
                if (pwm.Value.Duty >= 1) return true;
                frequency = pwm.Value.Frequency;
                duty = pwm.Value.Duty;
                return true;
            }

            // [motor] The MCPWM outputs: their generators' PWM after the dead-time module.
            if (McpwmSignal(signal) is { } wave)
            {
                if (!wave.IsPwm) return wave.Level;
                frequency = wave.Frequency;
                duty = wave.Duty;
                return true;
            }

            // The I²C controllers' SCL and SDA (I2C0 29/30, I2C1 95/96): released between transfers, so on their open-drain
            // pads the pull-ups hold the lines high; the transfers themselves run on the bus, not drawn on the pins.
            if (signal == 29 || signal == 30 || signal == 95 || signal == 96) return true;
            // The SPI controllers' chip selects (HSPI 11/61/62, VSPI 68–70): high, unselected, between transfers.
            if (signal == 11 || signal == 61 || signal == 62 || signal == 68 || signal == 69 || signal == 70) return true;
            return false;   // a signal nothing drives yet
        }

        /// <summary>
        /// Samples every pad's input from the circuit (the board calls it after each circuit step): the Schmitt trigger keeps
        /// the last level between the thresholds, and edges and levels set the pins' interrupt status.
        /// </summary>
        public void SamplePads()
        {
            if (PadVoltage == null) return;
            double vil = 0.25 * PadSupply, vih = 0.75 * PadSupply;
            ulong before = _gpioInput;
            for (int gpio = 0; gpio < 40; gpio++)
            {
                if (IoMuxOffset[gpio] == 0) continue;
                double v = PadVoltage(gpio);
                bool was = (before >> gpio & 1) != 0;
                bool now = was ? v > vil : v >= vih;
                if (now) _gpioInput |= 1UL << gpio;
                else _gpioInput &= ~(1UL << gpio);
            }

            for (int gpio = 0; gpio < 40; gpio++)
            {
                int type = (int)((_gpioPin[gpio] >> 7) & 7);
                if (type == 0) continue;
                bool was = (before >> gpio & 1) != 0, now = (_gpioInput >> gpio & 1) != 0;
                bool fire = type switch
                {
                    1 => !was && now,     // rising
                    2 => was && !now,     // falling
                    3 => was != now,      // any edge
                    4 => !now,            // low level
                    5 => now,             // high level
                    _ => false,
                };
                if (fire) _gpioStatus |= 1UL << gpio;
            }

            SamplePcnt();
            UpdateGpioInterrupt();
        }

        /// <summary>Source 22 holds while a pin's status is set and its INT_ENA routes it to a core (bit 13 APP, bit 15 PRO).</summary>
        private void UpdateGpioInterrupt()
        {
            bool any = false;
            for (int gpio = 0; gpio < 40 && !any; gpio++)
                if ((_gpioStatus >> gpio & 1) != 0 && (_gpioPin[gpio] & (1u << 13 | 1u << 15)) != 0) any = true;
            SetPeripheralLevel(GpioInterruptSource, any);
        }

        private bool ReadGpioRegister(uint register, out uint value)
        {
            value = 0;
            if (register >= Gpio && register < Gpio + 0x630)
            {
                uint offset = register - Gpio;
                switch (offset)
                {
                    case 0x04: case 0x08: case 0x0C: value = (uint)_gpioOut; return true;
                    case 0x10: case 0x14: case 0x18: value = (uint)(_gpioOut >> 32) & 0xFF; return true;
                    case 0x20: case 0x24: case 0x28: value = (uint)_gpioEnable; return true;
                    case 0x2C: case 0x30: case 0x34: value = (uint)(_gpioEnable >> 32) & 0xFF; return true;
                    case 0x38: value = 0x13; return true;   // strapping: SPI boot, normal (GPIO0, GPIO2 … as on a dev board)
                    case 0x3C: value = (uint)_gpioInput; return true;
                    case 0x40: value = (uint)(_gpioInput >> 32) & 0xFF; return true;
                    case 0x44: case 0x48: case 0x4C: value = (uint)_gpioStatus; return true;
                    case 0x50: case 0x54: case 0x58: value = (uint)(_gpioStatus >> 32) & 0xFF; return true;
                    case 0x60: value = Pending(13, 0); return true;   // ACPU_INT
                    case 0x68: value = Pending(15, 0); return true;   // PCPU_INT
                    case 0x74: value = Pending(13, 32); return true;
                    case 0x7C: value = Pending(15, 32); return true;
                }

                if (offset >= 0x88 && offset < 0x88 + 40 * 4)
                {
                    value = _gpioPin[(offset - 0x88) / 4];
                    return true;
                }

                if (offset >= 0x130 && offset < 0x530)
                {
                    value = _funcIn[(offset - 0x130) / 4];
                    return true;
                }

                if (offset >= 0x530 && offset < 0x530 + 40 * 4)
                {
                    value = _funcOut[(offset - 0x530) / 4];
                    return true;
                }

                _registers.TryGetValue(register, out value);
                return true;
            }

            if (register >= IoMux && register < IoMux + 0x94)
            {
                _registers.TryGetValue(register, out value);
                return true;
            }

            return false;

            uint Pending(int enableBit, int first)
            {
                uint v = 0;
                for (int i = 0; i < 32 && first + i < 40; i++)
                    if ((_gpioStatus >> (first + i) & 1) != 0 && (_gpioPin[first + i] & (1u << enableBit)) != 0) v |= 1u << i;
                return v;
            }
        }

        private bool WriteGpioRegister(uint register, uint value)
        {
            if (register >= IoMux && register < IoMux + 0x94)
            {
                _registers[register] = value;   // kept as written (the caller stores it too)
                NotifyPadLevels();
                return true;
            }

            if (register < Gpio || register >= Gpio + 0x630) return false;
            uint offset = register - Gpio;
            ulong low = value, high = (ulong)(value & 0xFF) << 32;
            switch (offset)
            {
                case 0x04: _gpioOut = (_gpioOut & ~0xFFFFFFFFUL) | low; break;
                case 0x08: _gpioOut |= low; break;
                case 0x0C: _gpioOut &= ~low; break;
                case 0x10: _gpioOut = (_gpioOut & 0xFFFFFFFFUL) | high; break;
                case 0x14: _gpioOut |= high; break;
                case 0x18: _gpioOut &= ~high; break;
                case 0x20: _gpioEnable = (_gpioEnable & ~0xFFFFFFFFUL) | low; break;
                case 0x24: _gpioEnable |= low; break;
                case 0x28: _gpioEnable &= ~low; break;
                case 0x2C: _gpioEnable = (_gpioEnable & 0xFFFFFFFFUL) | high; break;
                case 0x30: _gpioEnable |= high; break;
                case 0x34: _gpioEnable &= ~high; break;
                case 0x44: _gpioStatus = (_gpioStatus & ~0xFFFFFFFFUL) | low; break;
                case 0x48: _gpioStatus |= low; break;
                case 0x4C: _gpioStatus &= ~low; break;
                case 0x50: _gpioStatus = (_gpioStatus & 0xFFFFFFFFUL) | high; break;
                case 0x54: _gpioStatus |= high; break;
                case 0x58: _gpioStatus &= ~high; break;
                default:
                    if (offset >= 0x88 && offset < 0x88 + 40 * 4) _gpioPin[(offset - 0x88) / 4] = value;
                    else if (offset >= 0x130 && offset < 0x530) _funcIn[(offset - 0x130) / 4] = value;
                    else if (offset >= 0x530 && offset < 0x530 + 40 * 4) _funcOut[(offset - 0x530) / 4] = value & 0xFFF;
                    break;
            }

            if (offset >= 0x44 && offset <= 0x58 || offset >= 0x88 && offset < 0x88 + 40 * 4) UpdateGpioInterrupt();
            if (offset <= 0x34 || offset >= 0x88 && offset < 0x88 + 40 * 4 || offset >= 0x530) NotifyPadLevels();
            return true;
        }

        /// <summary>The ROM's matrix and pad helpers, done as the ROM does them, on these registers.</summary>
        private void BindGpioRom()
        {
            foreach (var name in new[] { "gpio_matrix_in", "esp_rom_gpio_connect_in_signal" })
            {
                Rom(name, c =>
                {
                    // (gpio, signal, invert): the signal reads the pad through the matrix (0x30: constant high, 0x38: low)
                    uint gpio = c.Argument(0), signal = c.Argument(1);
                    if (signal < 256) _funcIn[signal] = 1u << 7 | (c.Argument(2) != 0 ? 1u << 6 : 0) | (gpio & 0x3F);
                    return Return(c, 0);
                });
            }

            foreach (var name in new[] { "gpio_matrix_out", "esp_rom_gpio_connect_out_signal" })
            {
                Rom(name, c =>
                {
                    // (gpio, signal, invert output, invert enable): the pad outputs the signal, its driver on
                    uint gpio = c.Argument(0);
                    if (gpio < 40)
                    {
                        _funcOut[gpio] = (c.Argument(1) & 0x1FF) | (c.Argument(2) != 0 ? 1u << 9 : 0) | (c.Argument(3) != 0 ? 1u << 11 : 0);
                        _gpioEnable |= 1UL << (int)gpio;
                        NotifyPadLevels();
                    }

                    return Return(c, 0);
                });
            }

            foreach (var name in new[] { "gpio_pad_select_gpio", "esp_rom_gpio_pad_select_gpio" })
                Rom(name, c => Return(c, SetPad(c.Argument(0), m => (m & ~(7u << 12)) | 2u << 12)));
            foreach (var name in new[] { "gpio_pad_pullup", "esp_rom_gpio_pad_pullup_only" })
                Rom(name, c => Return(c, SetPull(c.Argument(0), true)));
            Rom("gpio_pad_pulldown", c => Return(c, SetPull(c.Argument(0), false)));
            Rom("gpio_pad_hold", c => Return(c, 0));
            Rom("gpio_pad_unhold", c => Return(c, 0));
            foreach (var name in new[] { "gpio_pad_set_drv", "esp_rom_gpio_pad_set_drv" })
                Rom(name, c => Return(c, SetPad(c.Argument(0), m => (m & ~(3u << 10)) | (c.Argument(1) & 3) << 10)));
            foreach (var name in new[] { "gpio_output_set", "esp_rom_gpio_output_set" })
            {
                Rom(name, c =>
                {
                    // (set, clear, enable, disable) for GPIO0–31
                    _gpioOut = (_gpioOut | c.Argument(0)) & ~(ulong)c.Argument(1);
                    _gpioEnable = (_gpioEnable | c.Argument(2)) & ~(ulong)c.Argument(3);
                    return Return(c, 0);
                });
            }

            Rom("gpio_input_get", c => Return(c, (uint)_gpioInput));
        }

        /// <summary>The ROM's pad pull: one pull on, the other off, where the pad has them (IO_MUX, and an RTC pad's RTC_IO).</summary>
        private uint SetPull(uint gpio, bool up)
        {
            if (gpio < 40 && RtcPull((int)gpio) is { } rtc)
                _registers[rtc.Register] = (Reg(rtc.Register) & ~(rtc.Up | rtc.Down)) | (up ? rtc.Up : rtc.Down);
            return SetPad(gpio, m => (m & ~(1u << 7 | 1u << 8)) | (up ? 1u << 8 : 1u << 7));
        }

        private uint SetPad(uint gpio, Func<uint, uint> change)
        {
            if (gpio < 40 && IoMuxOffset[gpio] != 0)
            {
                uint at = IoMux + IoMuxOffset[gpio];
                _registers[at] = change(Reg(at));
                NotifyPadLevels();
            }

            return 0;
        }
    }
}
