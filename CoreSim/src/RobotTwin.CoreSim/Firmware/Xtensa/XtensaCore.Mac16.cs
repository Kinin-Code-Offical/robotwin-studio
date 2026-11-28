namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    public sealed partial class XtensaCore
    {
        /// <summary>
        /// [esp32emu] The MAC16 option (op0 = 4): 16 × 16-bit multiplies into the 40-bit accumulator ACCHI:ACCLO, and the
        /// MAC16 data registers M0–M3 with their auto-incrementing loads. The ESP32's GCC uses it for 16-bit
        /// multiply-accumulate (maddhisi4: MULA.AA.LL, read back with RSR ACCLO), esp-dsp's hand-written routines for the
        /// MULA.DD … LDINC forms. op2 picks the operands: 7 AR × AR, 3 AR × MY, 6 MX × AR, 2 MX × MY; 4 / 5 (MX × AR) and
        /// 0 / 1 (MX × MY) with a load that increments / decrements AR[s] by 4; 8 / 9 the loads alone (LDINC / LDDEC).
        /// op1's top bits the operation (0 UMUL, AA only; 1 MUL; 2 MULA; 3 MULS), its low bits the halves (LL, HL, LH, HH:
        /// the first letter the first operand's). MX is M0 or M1 (bit 14), MY M2 or M3 (bit 6), the loaded MW bits 13–12.
        /// </summary>
        private int Mac16(int t, int s, int r, int op1, int op2)
        {
            bool load = op2 <= 1 || op2 == 4 || op2 == 5 || op2 == 8 || op2 == 9;
            bool decrement = op2 == 1 || op2 == 5 || op2 == 9;
            bool multiply = op2 <= 7;
            int operation = op1 >> 2;
            if (op2 > 9 || (multiply && operation == 0 && op2 != 7) || (load && multiply && operation != 2) || (op2 >= 8 && op1 != 0))
            {
                Illegal();   // reserved: only MULA has the load forms, only AA has UMUL
                return 1;
            }

            bool firstIsAr = op2 == 7 || op2 == 3;
            bool secondIsAr = op2 == 7 || op2 == 6 || op2 == 4 || op2 == 5;
            int mx = (r >> 2) & 1, my = 2 + ((t >> 2) & 1), mw = r & 3;
            if (WindowCheck(Frames(load || firstIsAr ? s : 0, secondIsAr ? t : 0))) return 1;

            // the operands as they were before the load (MW may be MX or MY)
            uint first = firstIsAr ? A(s) : M[mx];
            uint second = secondIsAr ? A(t) : M[my];
            if (load)
            {
                uint address = decrement ? A(s) - 4 : A(s) + 4;
                uint word = L32(address);
                if (_excepted) return 1;
                M[mw] = word;
                SetA(s, address);
            }

            if (!multiply) return 1;
            bool firstHigh = (op1 & 1) != 0, secondHigh = (op1 & 2) != 0;
            uint a = firstHigh ? first >> 16 : first & 0xFFFF;
            uint b = secondHigh ? second >> 16 : second & 0xFFFF;
            long product = operation == 0 ? (long)(a * b) : (long)((short)a * (short)b);
            long acc = ((long)(sbyte)(byte)AccHi << 32) | AccLo;
            acc = operation switch
            {
                0 or 1 => product,
                2 => acc + product,
                _ => acc - product
            };

            AccLo = (uint)acc;
            AccHi = operation == 0 ? 0u : (uint)(sbyte)(byte)(acc >> 32);   // 40 bits: ACCHI is the sign-extended top byte
            return 1;
        }
    }
}
