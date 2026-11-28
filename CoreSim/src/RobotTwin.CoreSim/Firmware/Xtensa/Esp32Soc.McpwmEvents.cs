using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] [motor] The MCPWM's timer and operator interrupts, which IDF's driver turns into a timer's on_empty /
    /// on_full / on_stop and a comparator's on_reach: from when a timer started (its count loaded, the first zero a period
    /// on), each period has its zero (TEZ, bits 3–5) and peak (TEP, 6–8), and each operator's comparisons A and B (TEA
    /// 15–17, TEB 18–20) where the count passes the compare value, twice a period counting up and down; a stop command
    /// raises TIMERn_STOP (0–2) at the zero or peak it waits for. The raw bits are set as the turns pass those ticks, as the
    /// fast mode's callbacks run at them (rw_mcpwm.cpp).
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private readonly double[,] _mcpwmOrigin = new double[2, 3];   // when each timer started
        private readonly double[,] _mcpwmServiced = new double[2, 3]; // ticks from the origin its events are raised up to
        private readonly int[,] _mcpwmStopping = new int[2, 3];       // 1 at the next zero, 2 at the next peak
        private bool _mcpwmEventsActive;

        /// <summary>A TIMERn_CFG1 command: a start loads the count now; a stop raises TIMERn_STOP at the zero or peak it names.</summary>
        private void McpwmTimerCommand(int unit, int timer, uint start, bool wasRunning)
        {
            bool running = start >= 2;
            if (running && !wasRunning)
            {
                _mcpwmOrigin[unit, timer] = Seconds;
                _mcpwmServiced[unit, timer] = 0;
                _mcpwmStopping[unit, timer] = 0;
                _mcpwmEventsActive = true;
            }
            else if (!running && wasRunning)
            {
                _mcpwmServiced[unit, timer] = McpwmTicks(unit, timer, Seconds);
                _mcpwmStopping[unit, timer] = start == 1 ? 2 : 1;
                _mcpwmEventsActive = true;
            }
        }

        private double McpwmTickHz(int unit, int timer)
        {
            uint b = McpwmBase[unit];
            double groupHz = McpwmSourceHz / ((Reg(b) & 0xFF) + 1);
            return groupHz / ((Reg(b + 0x04 + 0x10 * (uint)timer) & 0xFF) + 1);
        }

        private double McpwmTicks(int unit, int timer, double seconds) => (seconds - _mcpwmOrigin[unit, timer]) * McpwmTickHz(unit, timer);

        /// <summary>A period's length in ticks and where its zero and peak are (up: 0 and P; down: P and 0; up-down: 0 and P of 2P).</summary>
        private void McpwmPeriod(int unit, int timer, out int mode, out double length, out double zero, out double peak, out int period)
        {
            uint b = McpwmBase[unit];
            period = (int)((Reg(b + 0x04 + 0x10 * (uint)timer) >> 8) & 0xFFFF);
            mode = (int)((Reg(b + 0x08 + 0x10 * (uint)timer) >> 3) & 3);
            switch (mode)
            {
                case 2: length = period + 1; zero = period; peak = 0; break;
                case 3: length = 2.0 * period; zero = 0; peak = period; break;
                default: length = period + 1; zero = 0; peak = period; break;
            }
        }

        /// <summary>How many ticks <c>n·length + offset</c> lie in (a, b].</summary>
        private static long McpwmCrossed(double a, double b, double offset, double length) =>
            (long)Math.Floor((b - offset) / length) - (long)Math.Floor((a - offset) / length);

        /// <summary>Each turn: the timer and comparison events the running timers passed, as raw interrupt bits.</summary>
        private void ServiceMcpwmEvents()
        {
            if (!_mcpwmEventsActive) return;
            for (int unit = 0; unit < 2; unit++)
            {
                uint b = McpwmBase[unit];
                uint raised = 0;
                for (int timer = 0; timer < 3; timer++)
                {
                    bool running = _mcpwmRunning[unit, timer];
                    int stopping = _mcpwmStopping[unit, timer];
                    if (!running && stopping == 0) continue;
                    McpwmPeriod(unit, timer, out int mode, out double length, out double zero, out double peak, out int period);
                    if (mode == 0 || period == 0) continue;
                    double from = _mcpwmServiced[unit, timer], to = McpwmTicks(unit, timer, Seconds);
                    if (to <= from) continue;
                    if (!running)
                    {
                        if (McpwmCrossed(from, to, stopping == 1 ? zero : peak, length) > 0)
                        {
                            raised |= 1u << timer;   // TIMERn_STOP
                            _mcpwmStopping[unit, timer] = 0;
                        }

                        _mcpwmServiced[unit, timer] = to;
                        continue;
                    }

                    if (McpwmCrossed(from, to, zero, length) > 0) raised |= 1u << (3 + timer);
                    if (McpwmCrossed(from, to, peak, length) > 0) raised |= 1u << (6 + timer);
                    for (int op = 0; op < 3; op++)
                    {
                        int opTimer = (int)((Reg(b + 0x38) >> (2 * op)) & 3);
                        if (opTimer != timer) continue;
                        uint opBase = b + 0x3C + 0x38 * (uint)op;
                        for (int which = 0; which < 2; which++)
                        {
                            int cmp = (int)(Reg(opBase + 0x04 + 4 * (uint)which) & 0xFFFF);
                            bool hit = mode switch
                            {
                                2 => cmp <= period && McpwmCrossed(from, to, period - cmp, length) > 0,
                                3 => (cmp < period && McpwmCrossed(from, to, cmp, length) > 0) ||
                                     (cmp > 0 && cmp < period && McpwmCrossed(from, to, length - cmp, length) > 0),
                                _ => cmp <= period && McpwmCrossed(from, to, cmp, length) > 0,
                            };
                            if (hit) raised |= 1u << (which == 0 ? 15 + op : 18 + op);
                        }
                    }

                    _mcpwmServiced[unit, timer] = to;
                }

                if (raised == 0) continue;
                _mcpwmIntRaw[unit] |= raised;
                UpdateMcpwmInterrupt(unit);
            }
        }
    }
}
