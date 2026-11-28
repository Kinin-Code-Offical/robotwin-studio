using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] [motor] The MCPWM's fault handler as IDF's driver programs it: three fault inputs per group (FAULT_DETECT:
    /// enable and pole of each, from matrix signals PWM0_F0–F2 = 34–36, PWM1 = 106–108), and per operator which faults
    /// brake it cycle by cycle (FH_CFG0 F0–F2_CBC, bits 3–1) or one shot (F0–F2_OST, bits 7–5), with what each output does
    /// meanwhile (A / B, CBC / OST, up / down: low, high, toggle or keep). A one-shot trip holds until FH_CFG1 CLR_OST with
    /// the fault gone (a rising edge of the bit); cycle by cycle releases when the fault does. FORCE_CBC / FORCE_OST trip it
    /// by software (SW_CBC / SW_OST enabled) when their value changes, as IDF toggles them; a forced CBC lasts one period. FH_STATUS reads CBC_ON while braking cycle by cycle and OST_ON while a
    /// fault that trips one shot is present (a software trip is an instant: IDF's recovery reads OST_ON as "the fault is still
    /// there" before it writes CLR_OST, the latched brake itself holds until then); the interrupts FAULTn (9–11), FAULTn_CLR
    /// (12–14), OPn_CBC (21–23) and OPn_OST (24–26) come as the fast mode's callbacks do (rw_mcpwm.cpp).
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const uint McpwmFaultDetect = 0xE4, McpwmFhCfg0 = 0x2C, McpwmFhCfg1 = 0x30, McpwmFhStatus = 0x34;
        private readonly bool[,] _faultOn = new bool[2, 3];      // each fault input present (after its pole)
        private readonly bool[,] _ostOn = new bool[2, 3];
        private readonly bool[,] _cbcOn = new bool[2, 3];
        private readonly double[,] _cbcForcedUntil = new double[2, 3];
        private bool _faultActive;

        private static int FaultSignal(int unit, int fault) => (unit == 0 ? 34 : 106) + fault;

        private uint OperatorBase(int unit, int op) => McpwmBase[unit] + 0x3C + 0x38 * (uint)op;

        /// <summary>A write to FAULT_DETECT or an operator's fault handler (true when it was one).</summary>
        private bool WriteMcpwmFault(int unit, uint offset, uint value)
        {
            uint b = McpwmBase[unit];
            if (offset == McpwmFaultDetect)
            {
                _registers[b + offset] = value & 0x3F;   // EVENT_F0–2 are read-only
                _faultActive = true;
                SampleMcpwmFaults();
                return true;
            }

            if (offset < 0x3C || offset >= 0x3C + 3 * 0x38) return false;
            int op = (int)((offset - 0x3C) / 0x38);
            uint inOp = (offset - 0x3C) % 0x38;
            if (inOp == McpwmFhCfg0)
            {
                _registers[b + offset] = value;
                _faultActive = true;
                EvaluateBrakes(unit);
                return true;
            }

            if (inOp == McpwmFhCfg1)
            {
                uint cfg0 = Reg(OperatorBase(unit, op) + McpwmFhCfg0);
                uint old = Reg(b + offset), changed = old ^ value;   // the bits stay as written (the caller stores them)
                if ((changed & value & 1) != 0 && !OstFaultPresent(unit, cfg0)) _ostOn[unit, op] = false;   // CLR_OST's rising edge, the fault gone
                if ((changed & (1u << 4)) != 0 && (cfg0 & (1u << 4)) != 0) Trip(unit, op, oneShot: true);   // FORCE_OST toggled
                if ((changed & (1u << 3)) != 0 && (cfg0 & 1) != 0)                                          // FORCE_CBC toggled
                {
                    _cbcForcedUntil[unit, op] = Seconds + McpwmPeriodSeconds(unit, op);
                    Trip(unit, op, oneShot: false);
                }

                _faultActive = true;
                EvaluateBrakes(unit);
                return true;
            }

            return false;
        }

        private bool ReadMcpwmFault(int unit, uint offset, out uint value)
        {
            value = 0;
            uint b = McpwmBase[unit];
            if (offset == McpwmFaultDetect)
            {
                value = Reg(b + offset) & 0x3F;
                for (int f = 0; f < 3; f++)
                    if (_faultOn[unit, f]) value |= 1u << (6 + f);
                return true;
            }

            if (offset >= 0x3C && offset < 0x3C + 3 * 0x38 && (offset - 0x3C) % 0x38 == McpwmFhStatus)
            {
                int op = (int)((offset - 0x3C) / 0x38);
                bool ostEvent = _ostOn[unit, op] && OstFaultPresent(unit, Reg(OperatorBase(unit, op) + McpwmFhCfg0));
                value = (_cbcOn[unit, op] ? 1u : 0) | (ostEvent ? 2u : 0);
                return true;
            }

            return false;
        }

        private double McpwmPeriodSeconds(int unit, int op)
        {
            int timer = (int)((Reg(McpwmBase[unit] + 0x38) >> (2 * op)) & 3);
            if (timer > 2) timer = 2;
            McpwmPeriod(unit, timer, out int mode, out double length, out _, out _, out _);
            return mode == 0 ? 0 : length / McpwmTickHz(unit, timer);
        }

        private bool OstFaultPresent(int unit, uint cfg0)
        {
            for (int f = 0; f < 3; f++)
                if (_faultOn[unit, f] && (cfg0 & (1u << (7 - f))) != 0) return true;
            return false;
        }

        private void Trip(int unit, int op, bool oneShot)
        {
            if (oneShot)
            {
                if (_ostOn[unit, op]) return;
                _ostOn[unit, op] = true;
                _mcpwmIntRaw[unit] |= 1u << (24 + op);
            }
            else
            {
                _mcpwmIntRaw[unit] |= 1u << (21 + op);
                _cbcOn[unit, op] = true;
            }

            UpdateMcpwmInterrupt(unit);
            NotifyPadLevels();
        }

        /// <summary>After each sample of the pads: the fault inputs, their FAULTn / FAULTn_CLR events and the brakes they set.</summary>
        private void SampleMcpwmFaults()
        {
            if (!_faultActive) return;
            for (int unit = 0; unit < 2; unit++)
            {
                uint detect = Reg(McpwmBase[unit] + McpwmFaultDetect);
                bool changed = false;
                for (int f = 0; f < 3; f++)
                {
                    bool on = (detect & (1u << f)) != 0 && InputSignalLevel(FaultSignal(unit, f), out _) == ((detect & (1u << (3 + f))) != 0);
                    if (on == _faultOn[unit, f]) continue;
                    _faultOn[unit, f] = on;
                    _mcpwmIntRaw[unit] |= 1u << (on ? 9 + f : 12 + f);
                    changed = true;
                }

                if (!changed) continue;
                UpdateMcpwmInterrupt(unit);
                EvaluateBrakes(unit);
            }
        }

        /// <summary>Each operator's brake from the faults present: a one-shot trip latches, cycle by cycle follows them.</summary>
        private void EvaluateBrakes(int unit)
        {
            bool any = false;
            for (int op = 0; op < 3; op++)
            {
                uint cfg0 = Reg(OperatorBase(unit, op) + McpwmFhCfg0);
                bool cbc = Seconds < _cbcForcedUntil[unit, op], ost = false;
                for (int f = 0; f < 3; f++)
                {
                    if (!_faultOn[unit, f]) continue;
                    cbc |= (cfg0 & (1u << (3 - f))) != 0;
                    ost |= (cfg0 & (1u << (7 - f))) != 0;
                }

                if (ost && !_ostOn[unit, op])
                {
                    _ostOn[unit, op] = true;
                    _mcpwmIntRaw[unit] |= 1u << (24 + op);
                    any = true;
                }

                if (cbc != _cbcOn[unit, op])
                {
                    _cbcOn[unit, op] = cbc;
                    if (cbc) _mcpwmIntRaw[unit] |= 1u << (21 + op);
                    any = true;
                }
            }

            if (!any) return;
            UpdateMcpwmInterrupt(unit);
            NotifyPadLevels();
        }

        /// <summary>Each turn: a cycle-by-cycle brake forced by software ends with its period.</summary>
        private void ServiceMcpwmFaults()
        {
            if (!_faultActive) return;
            for (int unit = 0; unit < 2; unit++)
                for (int op = 0; op < 3; op++)
                    if (_cbcOn[unit, op] && _cbcForcedUntil[unit, op] > 0 && Seconds >= _cbcForcedUntil[unit, op])
                    {
                        _cbcForcedUntil[unit, op] = 0;
                        EvaluateBrakes(unit);
                    }
        }

        /// <summary>What an output does while its operator brakes: its level, or null to keep its wave (no brake, or KEEP).</summary>
        private bool? McpwmBrakeLevel(int unit, int op, int output, int mode, McpwmWave wave)
        {
            bool ost = _ostOn[unit, op], cbc = _cbcOn[unit, op];
            if (!ost && !cbc) return null;
            uint cfg0 = Reg(OperatorBase(unit, op) + McpwmFhCfg0);
            bool down = mode == 2;
            int shift = output == 0
                ? (ost ? (down ? 12 : 14) : (down ? 8 : 10))
                : (ost ? (down ? 20 : 22) : (down ? 16 : 18));
            return ((cfg0 >> shift) & 3) switch
            {
                1 => false,
                2 => true,
                3 => !wave.Level,
                _ => null,
            };
        }
    }
}
