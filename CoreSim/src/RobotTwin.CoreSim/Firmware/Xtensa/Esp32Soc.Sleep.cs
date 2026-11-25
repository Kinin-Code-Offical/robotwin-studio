using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] Light sleep as IDF enters it (rtc_sleep_start): SLEEP_EN with the digital domain kept powered halts both
    /// cores and the APB clock (the timer groups, the interrupt watchdog and LEDC stop; the RTC time goes on) until an
    /// enabled source wakes the chip: the RTC timer, EXT0 (an RTC pad at its level) or a GPIO's light-sleep wake level
    /// (GPIO_PINn's WAKEUP_ENABLE with a low or high level type). It then sets SLP_WAKEUP in RTC_CNTL_INT_RAW and the cause
    /// in WAKEUP_STATE; the firmware, spinning on INT_RAW, carries on and IDF advances esp_timer by the time it slept.
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const uint RtcIntRaw = 0x3FF48040, RtcIntClr = 0x3FF48048;
        private bool _lightSleep;
        private double _lightSleepWakeAt;
        private uint _lightSleepSources;

        /// <summary>The chip is in light sleep: both cores and the APB clock halted until a wake source fires.</summary>
        public bool LightSleeping => _lightSleep;

        /// <summary>Light sleeps so far (for readouts and tests).</summary>
        public int LightSleeps { get; private set; }

        private void EnterLightSleep()
        {
            // WAKEUP_STATE: the sources enabled in bits 21:11 (0 EXT0, 2 GPIO, 3 the RTC timer).
            _lightSleepSources = (Reg(RtcWakeupState) >> 11) & 0x7FF;
            _lightSleepWakeAt = double.PositiveInfinity;
            if ((_lightSleepSources & 8) != 0)
            {
                ulong alarm = (ulong)(Reg(RtcSlpTimer1) & 0xFFFF) << 32 | Reg(RtcSlpTimer0);
                ulong now = (ulong)(Seconds * 150_000.0);
                _lightSleepWakeAt = Seconds + (alarm > now ? (alarm - now) / 150_000.0 : 0);
            }

            _lightSleep = true;
            LightSleeps++;
            ServiceLightSleep();   // a wake level already there wakes it at once
        }

        /// <summary>Each turn while asleep: the first enabled source that fires wakes the chip.</summary>
        private void ServiceLightSleep()
        {
            if (!_lightSleep) return;
            uint cause = 0;
            if ((_lightSleepSources & 8) != 0 && Seconds >= _lightSleepWakeAt) cause |= 8;
            if ((_lightSleepSources & 1) != 0)
            {
                int pad = (int)(Reg(RtcIoExtWakeup0) >> 27);
                int gpio = pad < RtcPadGpio.Length ? RtcPadGpio[pad] : -1;
                bool level = (Reg(RtcExtWakeupConf) & (1u << 30)) != 0;
                if (gpio >= 0 && ((_gpioInput >> gpio & 1) != 0) == level) cause |= 1;
            }

            if ((_lightSleepSources & 4) != 0)
            {
                for (int gpio = 0; gpio < 40; gpio++)
                {
                    uint pin = _gpioPin[gpio];
                    if ((pin & (1u << 10)) == 0) continue;   // WAKEUP_ENABLE
                    int type = (int)((pin >> 7) & 7);
                    bool high = (_gpioInput >> gpio & 1) != 0;
                    if ((type == 4 && !high) || (type == 5 && high)) cause |= 4;
                }
            }

            if (cause == 0) return;
            _lightSleep = false;
            _registers[RtcIntRaw] = Reg(RtcIntRaw) | 1u;                              // SLP_WAKEUP
            _registers[RtcWakeupState] = (Reg(RtcWakeupState) & ~0x7FFu) | cause;
        }

        /// <summary>CPU cycles until the RTC timer wakes the sleeping chip (the run loop's turn while asleep).</summary>
        private long CyclesToWake() =>
            double.IsPositiveInfinity(_lightSleepWakeAt) ? long.MaxValue : Math.Max(1, (long)Math.Ceiling((_lightSleepWakeAt - Seconds) * CpuHz));
    }

    /// <summary>
    /// [esp32emu] Deep sleep as IDF enters it: RTC_CNTL_STATE0's SLEEP_EN with the digital domain powered down
    /// (RTC_CNTL_DIG_PWC's DG_WRAP_PD_EN) stops the chip (<see cref="DeepSleepRequested"/>). What wakes it is read from the
    /// registers as the RTC controller reads them: the RTC timer (WAKEUP_ENA's timer bit, SLP_TIMER0/1's time in ticks of
    /// the 150 kHz slow clock) and EXT0 (an RTC pad at a level, RTC_IO_EXT_WAKEUP0 and EXT_WAKEUP0_LV). RTC memory (the
    /// sketch's RTC_DATA_ATTR variables) outlives the sleep: the board keeps it (<see cref="SaveRtcMemory"/>) for the next
    /// start, which the ROM reports as DEEPSLEEP_RESET with its cause in WAKEUP_STATE.
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const uint RtcState0 = 0x3FF48018, RtcDigPwc = 0x3FF48084, RtcWakeupState = 0x3FF48038;
        private const uint RtcSlpTimer0 = 0x3FF48004, RtcSlpTimer1 = 0x3FF48008, RtcExtWakeupConf = 0x3FF48060, RtcIoExtWakeup0 = 0x3FF484BC;

        // The GPIO of each RTC pad number (RTC_GPIO0–17).
        private static readonly int[] RtcPadGpio = { 36, 37, 38, 39, 34, 35, 25, 26, 33, 32, 4, 0, 2, 15, 13, 12, 14, 27 };

        /// <summary>The firmware put the chip into deep sleep; it has stopped.</summary>
        public bool DeepSleepRequested { get; private set; }

        /// <summary>When the RTC timer wakes it, in seconds from the moment it slept (null: the timer does not wake it).</summary>
        public double? WakeAfterSeconds { get; private set; }

        /// <summary>The pad EXT0 wakes it on and the level it waits for (gpio -1: EXT0 does not wake it).</summary>
        public (int Gpio, bool Level) Ext0Wake { get; private set; } = (-1, false);

        /// <summary>SLEEP_EN: deep sleep when the digital domain powers down with it, light sleep when it stays on.</summary>
        private bool WriteSleepRegister(uint register, uint value)
        {
            if (register == RtcIntClr)
            {
                _registers[RtcIntRaw] = Reg(RtcIntRaw) & ~value;   // SLP_WAKEUP / SLP_REJECT handled
                return false;
            }

            if (register != RtcState0 || (value & (1u << 31)) == 0) return false;
            if ((Reg(RtcDigPwc) & (1u << 31)) == 0)
            {
                EnterLightSleep();
                return false;
            }

            // WAKEUP_STATE: the sources enabled in bits 21:11 (bit 3 of them the RTC timer, bit 0 EXT0), the cause in 10:0.
            uint enable = (Reg(RtcWakeupState) >> 11) & 0x7FF;
            if ((enable & 8) != 0)
            {
                ulong alarm = (ulong)(Reg(RtcSlpTimer1) & 0xFFFF) << 32 | Reg(RtcSlpTimer0);
                ulong now = (ulong)(Seconds * 150_000.0);
                WakeAfterSeconds = alarm > now ? (alarm - now) / 150_000.0 : 0;
            }

            if ((enable & 1) != 0)
            {
                int pad = (int)(Reg(RtcIoExtWakeup0) >> 27);
                Ext0Wake = (pad < RtcPadGpio.Length ? RtcPadGpio[pad] : -1, (Reg(RtcExtWakeupConf) & (1u << 30)) != 0);
            }

            DeepSleepRequested = true;
            return false;
        }

        /// <summary>What RTC memory holds (slow then fast), to carry over a deep sleep.</summary>
        public byte[] SaveRtcMemory()
        {
            var data = new byte[_rtcSlow.Length + _rtcFast.Length];
            Array.Copy(_rtcSlow, data, _rtcSlow.Length);
            Array.Copy(_rtcFast, 0, data, _rtcSlow.Length, _rtcFast.Length);
            return data;
        }

        /// <summary>
        /// Starts after a deep sleep: RTC memory as it was (after <see cref="Load"/>, which the bootloader skips for it on a
        /// wake), and the wake cause in WAKEUP_STATE (1 EXT0, 8 the RTC timer).
        /// </summary>
        public void WakeFromDeepSleep(byte[] rtcMemory, uint cause)
        {
            if (rtcMemory != null && rtcMemory.Length == _rtcSlow.Length + _rtcFast.Length)
            {
                Array.Copy(rtcMemory, _rtcSlow, _rtcSlow.Length);
                Array.Copy(rtcMemory, _rtcSlow.Length, _rtcFast, 0, _rtcFast.Length);
            }

            _registers[RtcWakeupState] = cause & 0x7FF;
        }
    }
}
