using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] [motor] The MCPWM's sync and carrier, as IDF's mcpwm_sync and mcpwm_operator_apply_carrier program them.
    /// A sync event reloads a timer whose SYNCI_EN is set with its phase (TIMERn_SYNC 19:4) in its direction (bit 20, for
    /// up-down counting): toggling SYNC_SW does it for that timer, and each timer's sync out (SYNCO_SEL 3:2: its input passed
    /// on, its zero TEZ, its peak TEP; a SYNC_SW toggle always) reaches the timers whose TIMER_SYNCI_CFG selects it (1–3), as
    /// do the rising edges of the matrix's SYNC0–2 (4–6; signals 31–33 for PWM0, 103–105 for PWM1; bits 9–11 invert them).
    /// The capture timer takes the same sync outs and inputs (CAP_SYNCI_SEL 4:2 with CAP_SYNCI_EN). As the circuit sees an
    /// output by its frequency and duty, a sync moves when a timer's zero, peak and comparisons come (its events, a capture's
    /// count), the phase between timers of one period; it does not cut a longer period short.
    /// The carrier (CARRIERn_CFG) chops an operator's outputs after dead time: a first pulse of (OSHTWTH + 1) carrier
    /// periods, then the carrier (8 · (PRESCALE + 1) group clocks a period, DUTY eighths high), optionally inverting before
    /// and after; the circuit gets the carrier's frequency with the duty averaged over the PWM period, so an averaging load
    /// (a motor, a filter) sees the same power and a pulse transformer the carrier's edges.
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const uint McpwmSynciCfg = 0x34;
        private readonly bool[,] _syncLevel = new bool[2, 3];   // SYNC0–2 as last seen, after inversion
        private bool _syncActive;

        private static uint TimerSyncRegister(int timer) => 0x0C + 0x10 * (uint)timer;

        /// <summary>A write to a timer's sync register or to TIMER_SYNCI_CFG (true when it was one).</summary>
        private bool WriteMcpwmSync(int unit, uint offset, uint value)
        {
            uint b = McpwmBase[unit];
            if (offset == McpwmSynciCfg)
            {
                _registers[b + offset] = value;
                for (int s = 0; s < 3; s++) _syncLevel[unit, s] = SyncInputLevel(unit, s);
                _syncActive = true;
                return true;
            }

            if (offset < 0x0C || offset > 0x2C || (offset - 0x0C) % 0x10 != 0) return false;
            int timer = (int)((offset - 0x0C) / 0x10);
            uint old = Reg(b + offset);
            _registers[b + offset] = value;
            int synco = (int)((value >> 2) & 3);
            if (synco == 1 || synco == 2) _mcpwmEventsActive = true;   // its zero or peak syncs others: watched as its events
            if (((old ^ value) & 2) != 0)
            {
                // SYNC_SW toggled: a software sync of this timer, and its sync out.
                if ((value & 1) != 0) McpwmReload(unit, timer, Seconds);
                McpwmSyncOut(unit, timer, Seconds, 0);
            }

            return true;
        }

        /// <summary>A timer's sync out at <paramref name="t"/>: the timers and the capture timer that take it are reloaded.</summary>
        private void McpwmSyncOut(int unit, int leader, double t, int depth) => McpwmSyncSource(unit, leader + 1, t, depth);

        /// <summary>A sync from source <paramref name="source"/> (1–3 a timer's sync out, 4–6 SYNC0–2) to whoever selects it.</summary>
        private void McpwmSyncSource(int unit, int source, double t, int depth)
        {
            uint b = McpwmBase[unit];
            uint cap = Reg(b + McpwmCapTimerCfg);
            if ((cap & 2) != 0 && ((cap >> 2) & 7) == source) _capOrigin[unit] = t - Reg(b + McpwmCapTimerPhase) / ApbHz;
            uint sel = Reg(b + McpwmSynciCfg);
            for (int f = 0; f < 3; f++)
            {
                if (((sel >> (3 * f)) & 7) != source) continue;
                uint sync = Reg(b + TimerSyncRegister(f));
                if ((sync & 1) == 0) continue;   // SYNCI_EN
                McpwmReload(unit, f, t);
                if (((sync >> 2) & 3) == 0 && depth < 3) McpwmSyncOut(unit, f, t, depth + 1);   // its input passed on
            }
        }

        /// <summary>A running timer's count loaded with its phase at <paramref name="t"/>: its timeline starts over from there.</summary>
        private void McpwmReload(int unit, int timer, double t)
        {
            if (!_mcpwmRunning[unit, timer]) return;
            uint sync = Reg(McpwmBase[unit] + TimerSyncRegister(timer));
            McpwmPeriod(unit, timer, out int mode, out _, out _, out _, out int period);
            double phase = Math.Min((sync >> 4) & 0xFFFF, (uint)Math.Max(period, 0));
            bool down = (sync & (1u << 20)) != 0;
            // Where that count lies in the timeline's period (up: 0..P, down: P..0, up-down: 0..P..0 over 2P).
            double at = mode switch
            {
                2 => period - phase,
                3 => down && phase > 0 ? 2.0 * period - phase : phase,
                _ => phase,
            };
            _mcpwmOrigin[unit, timer] = t - at / McpwmTickHz(unit, timer);
            _mcpwmServiced[unit, timer] = at;
        }

        /// <summary>A timer whose zero (1) or peak (2) is its sync out, at the last of them it passed: the others reloaded.</summary>
        private void McpwmPeriodicSync(int unit, int timer, double from, double to, double zero, double peak, double length)
        {
            int synco = (int)((Reg(McpwmBase[unit] + TimerSyncRegister(timer)) >> 2) & 3);
            if (synco != 1 && synco != 2) return;
            double offset = synco == 1 ? zero : peak;
            if (McpwmCrossed(from, to, offset, length) <= 0) return;
            double tick = Math.Floor((to - offset) / length) * length + offset;
            McpwmSyncOut(unit, timer, _mcpwmOrigin[unit, timer] + tick / McpwmTickHz(unit, timer), 0);
        }

        private static int SyncSignal(int unit, int s) => (unit == 0 ? 31 : 103) + s;

        private bool SyncInputLevel(int unit, int s) =>
            InputSignalLevel(SyncSignal(unit, s), out _) != ((Reg(McpwmBase[unit] + McpwmSynciCfg) & (1u << (9 + s))) != 0);

        /// <summary>Each sample: a rising edge of SYNC0–2 (after inversion) syncs the timers and capture timer that select it.</summary>
        private void SampleMcpwmSync()
        {
            if (!_syncActive) return;
            for (int unit = 0; unit < 2; unit++)
            {
                for (int s = 0; s < 3; s++)
                {
                    bool now = SyncInputLevel(unit, s);
                    bool was = _syncLevel[unit, s];
                    _syncLevel[unit, s] = now;
                    if (now && !was) McpwmSyncSource(unit, 4 + s, Seconds, 0);
                }
            }
        }

        /// <summary>An operator's output through its carrier (CARRIERn_CFG at the operator's +0x28), when it is enabled.</summary>
        private McpwmWave McpwmCarrier(int unit, int op, McpwmWave w, double groupHz)
        {
            uint cfg = Reg(OperatorBase(unit, op) + 0x28);
            if ((cfg & 1) == 0) return w;
            double period = 8.0 * (((cfg >> 1) & 0xF) + 1) / groupHz;   // seconds
            double duty = ((cfg >> 5) & 7) / 8.0;
            double first = (((cfg >> 8) & 0xF) + 1) * period;
            if ((cfg & (1u << 13)) != 0) w = w.Frequency > 0 ? new McpwmWave(w.Frequency, 1 - w.Duty) : new McpwmWave(!w.Level);
            McpwmWave c;
            if (w.Frequency <= 0)
            {
                // High for good: after the first pulse the carrier alone.
                c = !w.Level || duty <= 0 ? new McpwmWave(false) : new McpwmWave(1 / period, duty);
            }
            else
            {
                double high = w.Duty / w.Frequency;
                if (high <= first) c = w;   // the first pulse spans the whole high time
                else
                {
                    double rest = high - first;
                    double whole = Math.Floor(rest / period);
                    double h = first + whole * duty * period + Math.Min(rest - whole * period, duty * period);
                    double avg = h * w.Frequency;
                    c = avg <= 0 ? new McpwmWave(false) : avg >= 1 ? new McpwmWave(true) : new McpwmWave(1 / period, avg);
                }
            }

            if ((cfg & (1u << 12)) != 0) c = c.Frequency > 0 ? new McpwmWave(c.Frequency, 1 - c.Duty) : new McpwmWave(!c.Level);
            return c;
        }
    }
}
