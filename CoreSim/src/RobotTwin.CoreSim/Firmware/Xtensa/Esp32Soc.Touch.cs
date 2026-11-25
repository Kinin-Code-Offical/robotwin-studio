using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] The capacitive touch sensor as IDF's touch driver reads it: each pad's count of charge cycles in the
    /// measurement window in SENS_SAR_TOUCH_OUT1–5 (pads 2n and 2n+1 in one word: the even one in bits 31:16, the odd one in
    /// 15:0), which falls when a finger adds capacitance (as fast mode's touchRead: about 75 untouched, under 20 touched).
    /// The values come from the board (<see cref="TouchValue"/>); measurements are always done (CTRL2.MEAS_DONE).
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const uint TouchOut1 = 0x3FF48870, TouchCtrl2 = 0x3FF48884;

        // The GPIO of each touch pad T0–T9.
        private static readonly int[] TouchPadGpio = { 4, 0, 2, 15, 13, 12, 14, 27, 33, 32 };

        /// <summary>A pad's count by its GPIO (null: 75, an untouched pad).</summary>
        public Func<int, int>? TouchValue { get; set; }

        private bool ReadTouchRegister(uint register, out uint value)
        {
            value = 0;
            if (register >= TouchOut1 && register < TouchOut1 + 5 * 4)
            {
                int pair = (int)((register - TouchOut1) / 4);
                value = (uint)Count(2 * pair) << 16 | (uint)Count(2 * pair + 1);
                return true;
            }

            if (register == TouchCtrl2)
            {
                value = Reg(register) | 1u << 10;   // TOUCH_MEAS_DONE
                return true;
            }

            return false;

            int Count(int pad) => Math.Clamp(TouchValue?.Invoke(TouchPadGpio[pad]) ?? 75, 0, 0xFFFF);
        }
    }
}
