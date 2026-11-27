using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    public sealed partial class XtensaCore
    {
        /// <summary>
        /// The FPU is coprocessor 0: with its CPENABLE bit clear an FP instruction raises Coprocessor0Disabled (FreeRTOS
        /// enables it lazily, on a task's first float instruction, and saves the registers then).
        /// </summary>
        private bool FpuEnabled()
        {
            if ((CpEnable & 1) != 0) return true;
            Exception(XtensaCause.Coprocessor0Disabled);
            return false;
        }

        /// <summary>a·b + c with one rounding at the end (the product of two floats is exact in a double).</summary>
        private static float Fma(float a, float b, float c) => (float)((double)a * b + c);

        private static float FromBits(uint bits) => BitConverter.Int32BitsToSingle((int)bits);

        private static uint Bits(float value) => (uint)BitConverter.SingleToInt32Bits(value);

        /// <summary>CONST.S: +0, +1, +2, +0.5 (the higher immediates are reserved and wrap, as QEMU's model does).</summary>
        private static readonly float[] Constants = { 0f, 1f, 2f, 0.5f };

        /// <summary>A float scaled by 2^scale to a 32-bit integer, saturating as the FPU does (NaN gives the largest).</summary>
        private static int ToInt(double value)
        {
            if (double.IsNaN(value) || value >= 2147483647.0) return int.MaxValue;
            if (value <= -2147483648.0) return int.MinValue;
            return (int)value;
        }

        private static uint ToUInt(double value)
        {
            if (double.IsNaN(value) || value >= 4294967295.0) return uint.MaxValue;
            if (value <= 0) return 0;
            return (uint)value;
        }

        private int LoadStoreFloat(int t, int s, int r, uint imm8)
        {
            if (!FpuEnabled()) return 1;
            if (WindowCheck(Frames(s))) return 1;
            uint address = A(s) + imm8 * 4;
            switch (r)
            {
                case 0: F[t] = FromBits(L32(address)); return 1;                        // LSI
                case 4: S32(address, Bits(F[t])); return 1;                             // SSI
                case 8: F[t] = FromBits(L32(address)); SetA(s, address); return 1;      // LSIU
                case 0xC: S32(address, Bits(F[t])); SetA(s, address); return 1;         // SSIU
            }

            Illegal();
            return 1;
        }

        private int Fp0(int t, int s, int r, int op2)
        {
            if (!FpuEnabled()) return 1;
            switch (op2)
            {
                case 0: F[r] = F[s] + F[t]; return 1;                                      // ADD.S
                case 1: F[r] = F[s] - F[t]; return 1;                                      // SUB.S
                case 2: F[r] = F[s] * F[t]; return 1;                                      // MUL.S
                case 4: F[r] = Fma(F[s], F[t], F[r]); return 1;         // MADD.S
                case 5: F[r] = Fma(-F[s], F[t], F[r]); return 1;        // MSUB.S
                // [fpu-div] The divide / square-root option's steps (MADDN.S, DIVN.S and the helpers below) exist only for
                // libgcc's __divsf3, __ieee754_sqrtf, __recipsf2 and __rsqrtsf2. As Espressif's own QEMU models them, the
                // result is made exactly (MKDADJ.S divides, MKSADJ.S takes the root, ADDEXPM.S hands it over) and the
                // Newton steps leave it as it is: the routines return the correctly rounded IEEE result, as the chip does.
                case 6: return 1;                                        // MADDN.S
                case 7: return 1;                                        // DIVN.S
                case 8:
                case 9:
                case 0xA:
                case 0xB:
                case 0xE:
                {
                    // ROUND.S, TRUNC.S, FLOOR.S, CEIL.S, UTRUNC.S ar, fs, scale
                    if (WindowCheck(Frames(r))) return 1;
                    double v = F[s] * Math.Pow(2, t);
                    SetA(r, op2 switch
                    {
                        8 => (uint)ToInt(Math.Round(v, MidpointRounding.ToEven)),
                        9 => (uint)ToInt(Math.Truncate(v)),
                        0xA => (uint)ToInt(Math.Floor(v)),
                        0xB => (uint)ToInt(Math.Ceiling(v)),
                        _ => ToUInt(Math.Truncate(v)),
                    });
                    return 1;
                }

                case 0xC:
                case 0xD:
                    // FLOAT.S, UFLOAT.S fr, as, scale
                    if (WindowCheck(Frames(s))) return 1;
                    F[r] = (float)((op2 == 0xC ? (double)(int)A(s) : A(s)) / Math.Pow(2, t));
                    return 1;
                case 0xF:
                    switch (t)
                    {
                        case 0: F[r] = F[s]; return 1;                                                    // MOV.S
                        case 1: F[r] = FromBits(Bits(F[s]) & 0x7FFFFFFF); return 1;                     // ABS.S
                        case 4: if (WindowCheck(Frames(r))) return 1; SetA(r, Bits(F[s])); return 1;    // RFR
                        case 5: if (WindowCheck(Frames(s))) return 1; F[r] = FromBits(A(s)); return 1;  // WFR
                        case 6: F[r] = FromBits(Bits(F[s]) ^ 0x80000000); return 1;                     // NEG.S
                        case 3: F[r] = Constants[s & 3]; return 1;                                         // CONST.S fr, imm
                        case 7: return 1;                                                                  // DIV0.S
                        case 8: F[r] = 1f / F[s]; return 1;                                                // RECIP0.S
                        case 9: return 1;                                                                  // SQRT0.S
                        case 0xA: F[r] = 1f / (float)Math.Sqrt(F[s]); return 1;                            // RSQRT0.S
                        case 0xB: return 1;                                                                // NEXP01.S
                        case 0xC: F[r] = (float)Math.Sqrt(F[s]); return 1;                                 // MKSADJ.S
                        case 0xD: F[r] = F[s] / F[r]; return 1;                                            // MKDADJ.S
                        case 0xE: return 1;                                                                // ADDEXP.S
                        case 0xF: F[r] = F[s]; return 1;                                                   // ADDEXPM.S
                    }

                    break;
            }

            Illegal();
            return 1;
        }

        private int Fp1(int t, int s, int r, int op2)
        {
            if (!FpuEnabled()) return 1;
            float a = F[s], b = F[t];
            bool unordered = float.IsNaN(a) || float.IsNaN(b);
            switch (op2)
            {
                case 1: SetBool(r, unordered); return 1;                   // UN.S
                case 2: SetBool(r, !unordered && a == b); return 1;        // OEQ.S
                case 3: SetBool(r, unordered || a == b); return 1;         // UEQ.S
                case 4: SetBool(r, !unordered && a < b); return 1;         // OLT.S
                case 5: SetBool(r, unordered || a < b); return 1;          // ULT.S
                case 6: SetBool(r, !unordered && a <= b); return 1;        // OLE.S
                case 7: SetBool(r, unordered || a <= b); return 1;         // ULE.S
                case 8:
                case 9:
                case 0xA:
                case 0xB:
                {
                    // MOVEQZ.S, MOVNEZ.S, MOVLTZ.S, MOVGEZ.S fr, fs, at
                    if (WindowCheck(Frames(t))) return 1;
                    int v = (int)A(t);
                    bool move = op2 switch { 8 => v == 0, 9 => v != 0, 0xA => v < 0, _ => v >= 0 };
                    if (move) F[r] = F[s];
                    return 1;
                }

                case 0xC:
                case 0xD:
                    if (Bool(t) == (op2 == 0xD)) F[r] = F[s];   // MOVF.S / MOVT.S
                    return 1;
            }

            Illegal();
            return 1;
        }
    }
}
