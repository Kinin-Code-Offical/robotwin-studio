using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] [motor] The motor-control PWM (MCPWM0 at 0x3FF5E000, MCPWM1 at 0x3FF6C000) as IDF's mcpwm driver programs
    /// it: three timers (the 160 MHz clock over the group and timer prescalers; up, down or up-down counting; started,
    /// stopped at zero or at the peak), three operators each comparing its timer against two values, and two generators per
    /// operator setting, clearing or toggling their output on the timer's zero and peak and on the comparisons, each in its
    /// counting direction (the chip's priority when they coincide: B, A, peak, zero). The dead-time module delays rising
    /// (RED) and falling (FED) edges, inverts and bypasses as configured, so complementary outputs for a half bridge come out
    /// as on the chip; a continuous software force holds an output low or high. Each output goes to the GPIO matrix
    /// (PWM0_OUT0A–2B 32–37, PWM1_OUT0A–2B 108–113) as the PWM the circuit drives: its frequency and duty, worked out from
    /// one period of the generator's events (two when a toggle doubles it). The capture unit and its interrupts are in
    /// Esp32Soc.McpwmCapture.cs, the timer and operator interrupts in Esp32Soc.McpwmEvents.cs, the fault handler in
    /// Esp32Soc.McpwmFault.cs, the timers' sync and the carrier in Esp32Soc.McpwmSync.cs.
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private static readonly uint[] McpwmBase = { 0x3FF5E000, 0x3FF6C000 };
        private const double McpwmSourceHz = 160e6;

        /// <summary>Each timer runs (started free) or stopped at zero (false) / at the peak (true).</summary>
        private readonly bool[,] _mcpwmRunning = new bool[2, 3];
        private readonly bool[,] _mcpwmStoppedFull = new bool[2, 3];

        /// <summary>A PWM output: its level when constant, or its frequency and duty.</summary>
        public readonly struct McpwmWave
        {
            public McpwmWave(bool level)
            {
                Level = level;
                Frequency = 0;
                Duty = level ? 1 : 0;
            }

            public McpwmWave(double frequency, double duty)
            {
                Frequency = frequency;
                Duty = duty;
                Level = duty >= 0.5;
            }

            public bool Level { get; }

            /// <summary>Hz; 0 for a constant level.</summary>
            public double Frequency { get; }

            public double Duty { get; }

            public bool IsPwm => Frequency > 0 && Duty > 0 && Duty < 1;
        }

        private bool WriteMcpwmRegister(uint register, uint value)
        {
            int unit = register >= McpwmBase[0] && register < McpwmBase[0] + 0x130 ? 0 : register >= McpwmBase[1] && register < McpwmBase[1] + 0x130 ? 1 : -1;
            if (unit < 0) return false;
            uint offset = register - McpwmBase[unit];
            if (WriteMcpwmCapture(unit, offset, value)) return true;
            if (WriteMcpwmFault(unit, offset, value)) return true;
            if (WriteMcpwmSync(unit, offset, value)) return true;
            if (offset >= 0x08 && offset <= 0x28 && (offset - 0x08) % 0x10 == 0)
            {
                // TIMERn_CFG1: TIMER_START (self-clearing on the chip) is a command; its effect is kept here.
                int timer = (int)((offset - 0x08) / 0x10);
                McpwmTimerCommand(unit, timer, value & 7, _mcpwmRunning[unit, timer]);   // its events' timeline (Esp32Soc.McpwmEvents.cs)
                switch (value & 7)
                {
                    case 0:
                        _mcpwmRunning[unit, timer] = false;
                        _mcpwmStoppedFull[unit, timer] = false;
                        break;
                    case 1:
                        _mcpwmRunning[unit, timer] = false;
                        _mcpwmStoppedFull[unit, timer] = true;
                        break;
                    default:
                        _mcpwmRunning[unit, timer] = true;   // free running, or one more period (counted as running)
                        break;
                }
            }

            _registers[register] = value;
            return true;
        }

        /// <summary>The wave a matrix signal of an MCPWM output carries, or null when the signal is not one.</summary>
        private McpwmWave? McpwmSignal(int signal)
        {
            int unit, index;
            if (signal >= 32 && signal <= 37) (unit, index) = (0, signal - 32);
            else if (signal >= 108 && signal <= 113) (unit, index) = (1, signal - 108);
            else return null;
            return McpwmOutput(unit, index / 2, index % 2);
        }

        /// <summary>Output A (0) or B (1) of an operator, after the dead-time module and any software force.</summary>
        public McpwmWave McpwmOutput(int unit, int op, int output)
        {
            uint b = McpwmBase[unit], opBase = b + 0x3C + 0x38 * (uint)op;
            int timer = (int)((Reg(b + 0x38) >> (2 * op)) & 3);
            if (timer > 2) timer = 2;
            uint cfg0 = Reg(b + 0x04 + 0x10 * (uint)timer), cfg1 = Reg(b + 0x08 + 0x10 * (uint)timer);
            double groupHz = McpwmSourceHz / ((Reg(b) & 0xFF) + 1);
            double tickHz = groupHz / ((cfg0 & 0xFF) + 1);
            int period = (int)((cfg0 >> 8) & 0xFFFF);
            int mode = (int)((cfg1 >> 3) & 3);
            bool running = _mcpwmRunning[unit, timer] && mode != 0 && period > 0;
            int cmpA = (int)(Reg(opBase + 0x04) & 0xFFFF), cmpB = (int)(Reg(opBase + 0x08) & 0xFFFF);
            var genA = Generator(Reg(opBase + 0x14), mode, period, cmpA, cmpB, tickHz, running, _mcpwmStoppedFull[unit, timer]);
            var genB = Generator(Reg(opBase + 0x18), mode, period, cmpA, cmpB, tickHz, running, _mcpwmStoppedFull[unit, timer]);

            // Dead time: RED delays the rising edge of its input (A, or B with RED_INSEL), FED the falling edge (FED_INSEL),
            // each optionally inverted; an output not bypassed takes its path (A the RED one, B the FED one), then swaps.
            uint dt = Reg(opBase + 0x1C);
            double dtHz = (dt & (1u << 17)) != 0 ? tickHz : groupHz;
            // DT_RED / DT_FED hold the delay less one cycle (IDF writes ticks − 1).
            double red = ((Reg(opBase + 0x24) & 0xFFFF) + 1) / dtHz, fed = ((Reg(opBase + 0x20) & 0xFFFF) + 1) / dtHz;
            var redPath = Delay((dt & (1u << 11)) != 0 ? genB : genA, red, rising: true, invert: (dt & (1u << 13)) != 0);
            var fedPath = Delay((dt & (1u << 12)) != 0 ? genB : genA, fed, rising: false, invert: (dt & (1u << 14)) != 0);
            var outA = (dt & (1u << 15)) != 0 ? genA : redPath;
            var outB = (dt & (1u << 16)) != 0 ? genB : fedPath;
            if ((dt & (1u << 9)) != 0) outA = outB;    // A_OUTSWAP
            if ((dt & (1u << 10)) != 0) outB = (dt & (1u << 15)) != 0 ? genA : redPath;
            var wave = McpwmCarrier(unit, op, output == 0 ? outA : outB, groupHz);   // the carrier (Esp32Soc.McpwmSync.cs)
            if (McpwmBrakeLevel(unit, op, output, mode, wave) is bool braked) return new McpwmWave(braked);   // a fault's brake (Esp32Soc.McpwmFault.cs)

            // A continuous software force (GENn_FORCE: A in 7:6, B in 9:8; 1 low, 2 high).
            int force = (int)((Reg(opBase + 0x10) >> (output == 0 ? 6 : 8)) & 3);
            if (force == 1) return new McpwmWave(false);
            if (force == 2) return new McpwmWave(true);
            return wave;
        }

        /// <summary>A wave through a rising- or falling-edge delay (seconds), then optionally inverted.</summary>
        private static McpwmWave Delay(McpwmWave w, double delay, bool rising, bool invert)
        {
            McpwmWave d;
            if (w.Frequency <= 0 || delay <= 0) d = w;
            else
            {
                double shift = delay * w.Frequency;   // as a fraction of the period
                double duty = rising ? Math.Max(0, w.Duty - shift) : Math.Min(1, w.Duty + shift);
                d = duty <= 0 ? new McpwmWave(false) : duty >= 1 ? new McpwmWave(true) : new McpwmWave(w.Frequency, duty);
            }

            if (!invert) return d;
            return d.Frequency > 0 ? new McpwmWave(d.Frequency, 1 - d.Duty) : new McpwmWave(!d.Level);
        }

        /// <summary>
        /// One generator's output from its actions (GENn_A/B: up-counting zero 1:0, peak 3:2, A 5:4, B 7:6; down-counting
        /// the same from bit 12; 1 low, 2 high, 3 toggle) over its timer's period.
        /// </summary>
        private static McpwmWave Generator(uint actions, int mode, int period, int cmpA, int cmpB, double tickHz, bool running, bool stoppedFull)
        {
            int Act(int shift) => (int)((actions >> shift) & 3);
            // The events of one period as (tick, priority, action).
            var events = new List<(double Tick, int Priority, int Action)>();
            double length;
            switch (mode)
            {
                case 2:   // down: from the peak to zero
                    length = period + 1;
                    events.Add((0, 1, Act(14)));                               // DTEP
                    if (cmpA <= period) events.Add((period - cmpA, 2, Act(16))); // DTEA
                    if (cmpB <= period) events.Add((period - cmpB, 3, Act(18))); // DTEB
                    events.Add((period, 0, Act(12)));                          // DTEZ
                    break;
                case 3:   // up-down: zero to the peak and back
                    length = 2.0 * period;
                    events.Add((0, 0, Act(0) != 0 ? Act(0) : Act(12)));         // zero, turning up
                    if (cmpA < period) events.Add((cmpA, 2, Act(4)));          // UTEA
                    if (cmpB < period) events.Add((cmpB, 3, Act(6)));          // UTEB
                    events.Add((period, 1, Act(14) != 0 ? Act(14) : Act(2)));  // the peak, turning down
                    if (cmpA < period && cmpA > 0) events.Add((2.0 * period - cmpA, 2, Act(16)));   // DTEA
                    if (cmpB < period && cmpB > 0) events.Add((2.0 * period - cmpB, 3, Act(18)));   // DTEB
                    break;
                default:  // up: zero to the peak
                    length = period + 1;
                    events.Add((0, 0, Act(0)));                                // UTEZ
                    if (cmpA <= period) events.Add((cmpA, 2, Act(4)));         // UTEA
                    if (cmpB <= period) events.Add((cmpB, 3, Act(6)));         // UTEB
                    events.Add((period, 1, Act(2)));                           // UTEP
                    break;
            }

            // At one tick the higher priority applies last, so it wins: zero 0, peak 1, A 2, B 3.
            events.Sort((x, y) => x.Tick != y.Tick ? x.Tick.CompareTo(y.Tick) : x.Priority.CompareTo(y.Priority));
            static bool Apply(bool level, int action) => action switch { 1 => false, 2 => true, 3 => !level, _ => level };

            if (!running)
            {
                // Stopped at zero or at the peak: the level that event left, after a period from low.
                bool level = false;
                foreach (var e in events) level = Apply(level, e.Action);
                foreach (var e in events)
                {
                    level = Apply(level, e.Action);
                    if ((stoppedFull && e.Tick == (mode == 2 ? 0 : period)) || (!stoppedFull && e.Tick == (mode == 2 ? period : 0))) break;
                }

                return new McpwmWave(level);
            }

            // Two periods from low settle the level; the third and fourth give the high time (a toggle may double the period).
            bool lv = false;
            for (int pass = 0; pass < 2; pass++)
                foreach (var e in events) lv = Apply(lv, e.Action);
            bool start = lv;
            double high = 0;
            int periods = 1;
            for (int pass = 0; pass < 2; pass++)
            {
                double last = 0;
                foreach (var e in events)
                {
                    if (lv) high += e.Tick - last;
                    last = e.Tick;
                    lv = Apply(lv, e.Action);
                }

                if (lv) high += length - last;
                if (pass == 0 && lv == start) break;   // periodic in one period
                periods = 2;
            }

            double duty = high / (length * periods);
            if (duty <= 1e-9) return new McpwmWave(false);
            if (duty >= 1 - 1e-9) return new McpwmWave(true);
            return new McpwmWave(tickHz / (length * periods), duty);
        }
    }
}
