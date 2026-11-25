using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] [encoder] The pulse counter (PCNT, 0x3FF57000) as IDF's pulse_cnt driver and encoder libraries use it:
    /// eight units of two channels. Each channel counts the edges of its signal input (rising: POS_MODE, falling:
    /// NEG_MODE; increase, decrease or hold) as its control input's level says (HCTRL/LCTRL: keep, reverse or hold), which
    /// decodes a quadrature encoder in hardware. The 16-bit count returns to zero at the high and low limits; the limits,
    /// the two thresholds and zero raise their events in the unit's status and the shared interrupt (source 48); CTRL holds
    /// a unit in reset or pauses it. Inputs come through the GPIO matrix (FUNC_IN_SEL: a pad, or the constant 0x30 / 0x38):
    /// a pad's edges are those the board's samples of the circuit see, and a pad the chip itself drives with PWM (LEDC)
    /// gives its edges at the PWM's frequency, so a counter fed from the chip's own PWM counts it exactly. The glitch filter
    /// is not modelled (the circuit's samples are coarser than it).
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const uint Pcnt = 0x3FF57000;
        private const int PcntInterruptSource = 48;

        private sealed class PcntUnit
        {
            public short Count;
            public uint Status;                                // as PCNT_Un_STATUS reads: zero mode (1:0), events (6:2)
            public readonly bool[] Signal = new bool[2];       // each channel's signal level at the last sample
            public readonly bool[] PwmLevel = new bool[2];     // a PWM-driven input's level between edges
            public readonly double[] PwmEdges = new double[2]; // a PWM-driven input's fraction of an edge, carried on
        }

        private readonly PcntUnit[] _pcnt = { new PcntUnit(), new PcntUnit(), new PcntUnit(), new PcntUnit(),
                                              new PcntUnit(), new PcntUnit(), new PcntUnit(), new PcntUnit() };
        private uint _pcntCtrl = 0x5555, _pcntIntRaw, _pcntIntEnable;
        private bool _pcntActive;
        private double _pcntServicedAt;

        /// <summary>A unit's count now (−32768…32767), for tests and readouts.</summary>
        public int PcntCount(int unit) => _pcnt[unit].Count;

        /// <summary>The matrix input signal of a unit's channel: its edges (signal) or its level (control).</summary>
        private static int PcntSignal(int unit, int channel, bool control) =>
            (unit < 5 ? 39 + 4 * unit : 71 + 4 * (unit - 5)) + channel + (control ? 2 : 0);

        /// <summary>The level a peripheral's input signal sees through the matrix (FUNC_IN_SEL), and the pad it comes from (−1: a constant).</summary>
        private bool InputSignalLevel(int signal, out int pad)
        {
            pad = -1;
            uint cfg = _funcIn[signal];
            bool level = false;
            if ((cfg & (1u << 7)) != 0)
            {
                uint select = cfg & 0x3F;
                if (select == 0x38) level = true;              // GPIO_MATRIX_CONST_ONE_INPUT
                else if (select < 40 && IoMuxOffset[select] != 0)
                {
                    // A pad whose input buffer is off (IO_MUX FUN_IE) gives the matrix nothing.
                    pad = (int)select;
                    level = (Reg(IoMux + IoMuxOffset[select]) & (1u << 9)) != 0 && (_gpioInput >> pad & 1) != 0;
                }
            }

            return (cfg & (1u << 6)) != 0 ? !level : level;
        }

        private bool ReadPcntRegister(uint register, out uint value)
        {
            value = 0;
            if (register < Pcnt || register >= Pcnt + 0x100) return false;
            uint offset = register - Pcnt;
            if (offset >= 0x60 && offset < 0x80) value = (ushort)_pcnt[(offset - 0x60) / 4].Count;
            else if (offset >= 0x90 && offset < 0xB0) value = _pcnt[(offset - 0x90) / 4].Status;
            else
                value = offset switch
                {
                    0x80 => _pcntIntRaw,
                    0x84 => _pcntIntRaw & _pcntIntEnable,
                    0x88 => _pcntIntEnable,
                    0xB0 => _pcntCtrl,
                    0xFC => 0x14122600,
                    _ => Reg(register)
                };
            return true;
        }

        private bool WritePcntRegister(uint register, uint value)
        {
            if (register < Pcnt || register >= Pcnt + 0x100) return false;
            uint offset = register - Pcnt;
            switch (offset)
            {
                case 0x88:
                    _pcntIntEnable = value & 0xFF;
                    break;
                case 0x8C:
                    _pcntIntRaw &= ~value;
                    for (int u = 0; u < 8; u++)
                        if ((value >> u & 1) != 0) _pcnt[u].Status &= 3;   // the events were handled
                    break;
                case 0xB0:
                    _pcntCtrl = value;
                    for (int u = 0; u < 8; u++)
                        if ((value >> (2 * u) & 1) != 0) _pcnt[u].Count = 0;   // PLUS_CNT_RST: held at zero while set
                    break;
                default:
                    if (offset < 0x60)
                    {
                        _registers[register] = value;
                        if (offset % 0xC == 0)
                        {
                            // CONF0: the channels start from their inputs' present levels (a level is no edge).
                            var unit = _pcnt[offset / 0xC];
                            for (int ch = 0; ch < 2; ch++) unit.Signal[ch] = InputSignalLevel(PcntSignal((int)(offset / 0xC), ch, false), out _);
                        }

                        _pcntActive = false;
                        for (int u = 0; u < 8 && !_pcntActive; u++) _pcntActive = (Reg(Pcnt + 0xC * (uint)u) & 0xFFFF0000u) != 0;
                    }

                    break;
            }

            SetPeripheralLevel(PcntInterruptSource, (_pcntIntRaw & _pcntIntEnable) != 0);
            return true;
        }

        /// <summary>After each sample of the pads: the edges the counters' signal inputs saw.</summary>
        private void SamplePcnt()
        {
            if (!_pcntActive) return;
            for (int u = 0; u < 8; u++)
            {
                if ((Reg(Pcnt + 0xC * (uint)u) & 0xFFFF0000u) == 0) continue;
                var unit = _pcnt[u];
                for (int ch = 0; ch < 2; ch++)
                {
                    bool level = InputSignalLevel(PcntSignal(u, ch, false), out int pad);
                    if (pad >= 0 && PadDrive(pad).IsPwm) continue;   // counted from the PWM's frequency instead
                    if (level == unit.Signal[ch]) continue;
                    unit.Signal[ch] = level;
                    PcntEdge(u, ch, level);
                }
            }

            SetPeripheralLevel(PcntInterruptSource, (_pcntIntRaw & _pcntIntEnable) != 0);
        }

        /// <summary>Each turn: inputs the chip drives itself with PWM give their edges at its frequency.</summary>
        private void ServicePcnt()
        {
            double now = Seconds, dt = now - _pcntServicedAt;
            _pcntServicedAt = now;
            if (!_pcntActive || dt <= 0) return;
            bool any = false;
            for (int u = 0; u < 8; u++)
            {
                if ((Reg(Pcnt + 0xC * (uint)u) & 0xFFFF0000u) == 0) continue;
                var unit = _pcnt[u];
                for (int ch = 0; ch < 2; ch++)
                {
                    int signal = PcntSignal(u, ch, false);
                    InputSignalLevel(signal, out int pad);
                    if (pad < 0) continue;
                    var drive = PadDrive(pad);
                    if (!drive.IsPwm || !drive.Driven || !drive.InputEnabled) continue;
                    unit.PwmEdges[ch] += 2 * drive.PwmFrequency * dt;   // a rising and a falling edge each period
                    int edges = (int)Math.Min(unit.PwmEdges[ch], 1 << 20);
                    unit.PwmEdges[ch] -= edges;
                    bool invert = (_funcIn[signal] & (1u << 6)) != 0;
                    for (int i = 0; i < edges; i++)
                    {
                        unit.PwmLevel[ch] = !unit.PwmLevel[ch];
                        PcntEdge(u, ch, unit.PwmLevel[ch] ^ invert);
                    }

                    any |= edges > 0;
                }
            }

            if (any) SetPeripheralLevel(PcntInterruptSource, (_pcntIntRaw & _pcntIntEnable) != 0);
        }

        /// <summary>An edge on a channel's signal input: its mode, steered by the control input's level, moves the count.</summary>
        private void PcntEdge(int u, int ch, bool rising)
        {
            uint conf0 = Reg(Pcnt + 0xC * (uint)u);
            int shift = 16 + 8 * ch;   // NEG_MODE, POS_MODE, HCTRL_MODE, LCTRL_MODE: two bits each
            int mode = (int)((conf0 >> (rising ? shift + 2 : shift)) & 3);
            int delta = mode == 1 ? 1 : mode == 2 ? -1 : 0;
            if (delta == 0) return;
            bool control = InputSignalLevel(PcntSignal(u, ch, true), out _);
            int steer = (int)((conf0 >> (control ? shift + 4 : shift + 6)) & 3);
            if (steer == 2) return;          // hold: the control input inhibits counting
            if (steer == 1) delta = -delta;  // reverse
            PcntStep(u, delta);
        }

        /// <summary>One count up or down, with the limit, threshold and zero events it reaches.</summary>
        private void PcntStep(int u, int delta)
        {
            if ((_pcntCtrl >> (2 * u) & 3) != 0) return;   // held in reset, or paused
            var unit = _pcnt[u];
            uint conf0 = Reg(Pcnt + 0xC * (uint)u), conf1 = Reg(Pcnt + 0xC * (uint)u + 4), conf2 = Reg(Pcnt + 0xC * (uint)u + 8);
            short high = (short)(conf2 & 0xFFFF), low = (short)(conf2 >> 16);
            short thres0 = (short)(conf1 & 0xFFFF), thres1 = (short)(conf1 >> 16);
            int was = unit.Count, now = was + delta;
            uint events = 0, zeroMode = unit.Status & 3;
            if (high > 0 && now >= high)
            {
                if ((conf0 & (1u << 12)) != 0) events |= 1u << 5;   // H_LIM
                now = 0;
            }
            else if (low < 0 && now <= low)
            {
                if ((conf0 & (1u << 13)) != 0) events |= 1u << 4;   // L_LIM
                now = 0;
            }
            else
            {
                if (now == thres0 && (conf0 & (1u << 14)) != 0) events |= 1u << 3;
                if (now == thres1 && (conf0 & (1u << 15)) != 0) events |= 1u << 2;
                if (now == 0 && was != 0 && (conf0 & (1u << 11)) != 0)
                {
                    events |= 1u << 6;
                    zeroMode = was > 0 ? 0u : 1u;   // from the positive side, or from the negative
                }
            }

            unit.Count = unchecked((short)now);
            if (events == 0) return;
            unit.Status = (unit.Status & ~3u) | zeroMode | events;
            _pcntIntRaw |= 1u << u;
        }
    }
}
