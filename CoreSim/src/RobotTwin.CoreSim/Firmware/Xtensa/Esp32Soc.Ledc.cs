using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] The LED PWM controller (LEDC, 0x3FF59000): eight high-speed and eight low-speed channels on four timers
    /// each. A timer counts its clock (APB 80 MHz or the 1 MHz REF_TICK; the low-speed ones the slow clock LEDC_CONF
    /// picks) through a fractional divider (DIV_NUM, 10.8 fixed point) up to 2^DUTY_RES, so the PWM frequency is
    /// clock / divider / 2^bits. A channel takes a new duty when the firmware raises DUTY_START: at the next PWM period
    /// the hardware loads DUTY (4 fractional bits), clears DUTY_START, then steps the duty by DUTY_SCALE every
    /// DUTY_CYCLE periods, DUTY_NUM times (a hardware fade), and raises DUTY_CHNG_END (interrupt source 43). DUTY_R
    /// reads the duty the output has now. <see cref="LedcOutput"/> gives each channel's frequency and duty for the pins.
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const uint Ledc = 0x3FF59000, LedcEnd = 0x3FF59194;
        private const int LedcInterruptSource = 43;

        private readonly LedcChannel[] _ledcChannels = NewLedcChannels();
        private readonly uint[] _ledcTimerConf = new uint[8];   // 0–3 high speed, 4–7 low speed
        private uint _ledcIntRaw, _ledcIntEnable, _ledcConf;

        private static LedcChannel[] NewLedcChannels()
        {
            var channels = new LedcChannel[16];
            for (int i = 0; i < channels.Length; i++) channels[i] = new LedcChannel();
            return channels;
        }

        private sealed class LedcChannel
        {
            public uint Conf0, Hpoint, Duty, Conf1;

            /// <summary>The duty the output has (4 fractional bits, as DUTY_R).</summary>
            public uint Current;

            /// <summary>When the pending DUTY_START takes effect (the next period), and when its fade ends; NaN: nothing pending.</summary>
            public double StartAt = double.NaN, EndAt = double.NaN;

            /// <summary>The fade under way: from <see cref="FadeFrom"/>, DUTY_NUM steps of DUTY_SCALE every DUTY_CYCLE periods.</summary>
            public uint FadeFrom, FadeNum, FadeCycle, FadeScale;
            public bool FadeUp;
            public double Period;
        }

        /// <summary>A channel's PWM output now: frequency (Hz) and duty (0–1); null while it is off (no timer clock or output disabled).</summary>
        public (double Frequency, double Duty)? LedcOutput(int channel)
        {
            if (channel < 0 || channel >= 16) return null;
            var ch = _ledcChannels[channel];
            if ((ch.Conf0 & 4) == 0) return null;   // SIG_OUT_EN
            int timer = LedcTimerOf(channel);
            double period = LedcPeriod(timer);
            if (double.IsNaN(period)) return null;
            Advance(ch, Seconds);
            int bits = (int)(_ledcTimerConf[timer] & 0x1F);
            return (1 / period, Math.Clamp((ch.Current >> 4) / (double)(1u << bits), 0, 1));
        }

        private int LedcTimerOf(int channel) => (channel < 8 ? 0 : 4) + (int)(_ledcChannels[channel].Conf0 & 3);

        /// <summary>A timer's PWM period in seconds (NaN when paused, held in reset or without a divider).</summary>
        private double LedcPeriod(int timer)
        {
            uint conf = _ledcTimerConf[timer];
            if ((conf & (3u << 23)) != 0) return double.NaN;   // PAUSE or RST
            double divider = ((conf >> 5) & 0x3FFFF) / 256.0;
            if (divider < 1) return double.NaN;
            bool tickSel = (conf & (1u << 25)) != 0;
            double clock = timer < 4
                ? (tickSel ? ApbHz : 1e6)
                : (tickSel ? ((_ledcConf & 1) != 0 ? ApbHz : 8e6) : 1e6);
            int bits = (int)(conf & 0x1F);
            return divider * (1u << bits) / clock;
        }

        private bool ReadLedcRegister(uint register, out uint value)
        {
            value = 0;
            if (register < Ledc || register >= LedcEnd) return false;
            uint offset = register - Ledc;
            if (offset < 0x140)
            {
                var ch = _ledcChannels[offset / 0x14];
                Advance(ch, Seconds);
                switch (offset % 0x14)
                {
                    case 0x00: value = ch.Conf0; break;
                    case 0x04: value = ch.Hpoint; break;
                    case 0x08: value = ch.Duty; break;
                    case 0x0C: value = ch.Conf1; break;
                    case 0x10: value = ch.Current; break;
                }

                return true;
            }

            if (offset < 0x180)
            {
                int timer = (int)((offset - 0x140) / 0x20) * 4 + (int)((offset - 0x140) % 0x20 / 8);
                bool isValue = (offset & 4) != 0;
                value = isValue ? 0 : _ledcTimerConf[timer];   // the counter itself is not kept
                return true;
            }

            switch (offset)
            {
                case 0x180: value = _ledcIntRaw; break;
                case 0x184: value = _ledcIntRaw & _ledcIntEnable; break;
                case 0x188: value = _ledcIntEnable; break;
                case 0x190: value = _ledcConf; break;
            }

            return true;
        }

        private bool WriteLedcRegister(uint register, uint value)
        {
            if (register < Ledc || register >= LedcEnd) return false;
            uint offset = register - Ledc;
            if (offset < 0x140)
            {
                int channel = (int)(offset / 0x14);
                var ch = _ledcChannels[channel];
                Advance(ch, Seconds);
                switch (offset % 0x14)
                {
                    case 0x00: ch.Conf0 = value; break;
                    case 0x04: ch.Hpoint = value & 0xFFFFF; break;
                    case 0x08: ch.Duty = value & 0x1FFFFFF; break;
                    case 0x0C:
                        ch.Conf1 = value;
                        if ((value & (1u << 31)) != 0) StartDuty(channel);
                        break;
                }

                return true;
            }

            if (offset < 0x180)
            {
                if ((offset & 4) == 0) _ledcTimerConf[(int)((offset - 0x140) / 0x20) * 4 + (int)((offset - 0x140) % 0x20 / 8)] = value;
                return true;
            }

            switch (offset)
            {
                case 0x188: _ledcIntEnable = value & 0xFFFFFF; break;
                case 0x18C: _ledcIntRaw &= ~value; break;
                case 0x190: _ledcConf = value; break;
            }

            SetPeripheralLevel(LedcInterruptSource, (_ledcIntRaw & _ledcIntEnable) != 0);
            return true;
        }

        /// <summary>DUTY_START: the hardware takes DUTY and the fade set-up at the next PWM period.</summary>
        private void StartDuty(int channel)
        {
            var ch = _ledcChannels[channel];
            double period = LedcPeriod(LedcTimerOf(channel));
            if (double.IsNaN(period)) period = 0;   // no clock: nothing counts, the update lands at once
            uint conf1 = ch.Conf1;
            ch.Period = period;
            ch.FadeFrom = ch.Duty;
            ch.FadeScale = conf1 & 0x3FF;
            ch.FadeCycle = Math.Max(1, (conf1 >> 10) & 0x3FF);
            ch.FadeNum = (conf1 >> 20) & 0x3FF;
            ch.FadeUp = (conf1 & (1u << 30)) != 0;
            // The update lands at the timer's next period boundary (the counter's overflow); IDF waits for it before the next.
            ch.StartAt = period > 0 ? (Math.Floor(Seconds / period) + 1) * period : Seconds;
            ch.EndAt = ch.StartAt + ch.FadeNum * ch.FadeCycle * period;
        }

        /// <summary>Brings a channel to time <paramref name="now"/>: the pending start lands (DUTY_START clears), the fade steps.</summary>
        private static void Advance(LedcChannel ch, double now)
        {
            if (double.IsNaN(ch.StartAt) || now < ch.StartAt) return;
            ch.Conf1 &= ~(1u << 31);
            long steps = ch.Period <= 0 ? ch.FadeNum : Math.Min(ch.FadeNum, (long)((now - ch.StartAt) / (ch.FadeCycle * ch.Period)));
            long delta = steps * (long)ch.FadeScale << 4;
            long duty = ch.FadeUp ? ch.FadeFrom + delta : ch.FadeFrom - delta;
            ch.Current = (uint)Math.Clamp(duty, 0, 0x1FFFFFF);
        }

        /// <summary>Each turn: fades that finished raise DUTY_CHNG_END (HS channel n: bit 8+n, LS: bit 16+n).</summary>
        private void ServiceLedc()
        {
            double now = Seconds;
            bool raised = false;
            for (int c = 0; c < 16; c++)
            {
                var ch = _ledcChannels[c];
                if (double.IsNaN(ch.StartAt)) continue;
                Advance(ch, now);
                if (now < ch.EndAt) continue;
                ch.StartAt = ch.EndAt = double.NaN;
                _ledcIntRaw |= 1u << (8 + c);
                raised = true;
            }

            if (raised) SetPeripheralLevel(LedcInterruptSource, (_ledcIntRaw & _ledcIntEnable) != 0);
        }
    }
}
