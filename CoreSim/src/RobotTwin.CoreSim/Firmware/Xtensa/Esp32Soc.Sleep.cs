using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
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

        /// <summary>SLEEP_EN: deep sleep when the digital domain powers down with it (light sleep is not modelled: it returns at once).</summary>
        private bool WriteSleepRegister(uint register, uint value)
        {
            if (register != RtcState0 || (value & (1u << 31)) == 0) return false;
            if ((Reg(RtcDigPwc) & (1u << 31)) == 0)
            {
                // Light sleep: the chip wakes at once (SLP_WAKEUP in RTC_CNTL_INT_RAW), its time is not skipped.
                _registers[0x3FF48040] = Reg(0x3FF48040) | 1u;
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
