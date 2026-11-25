using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] The chip's clock and its two timer groups. Time is one clock both cores share: each turn of
    /// <see cref="Run"/> advances it by the turn's cycles at the CPU frequency the firmware set (the clock registers:
    /// XTAL 40 MHz at reset, the PLL's 80/160/240 MHz once IDF switches), and the APB clock the peripherals count
    /// (80 MHz on the PLL, the CPU clock otherwise). Each group (TIMG0 0x3FF5F000, TIMG1 0x3FF60000) has two
    /// general-purpose 64-bit timers (T0, T1) and the LAC timer esp_timer counts on (millis(), micros(), its
    /// alarms): a prescaled up/down counter with load, latch (UPDATE), and an alarm that raises the group's
    /// interrupt (level sources 14–21, edge sources 58–65 through the matrix).
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const uint Timg0 = 0x3FF5F000, Timg1 = 0x3FF60000;
        private const uint RtcClkConf = 0x3FF48070, CpuPerConf = 0x3FF0003C, SysClkConf = 0x3FF66000;

        /// <summary>Seconds of chip time and APB ticks before the current turn.</summary>
        private double _seconds, _apbTicks;

        /// <summary>Each core's cycle count when the current turn began (time inside a turn runs with the core that reads it).</summary>
        private ulong _proTurnStart, _appTurnStart;

        // Level sources 14–17 / 18–21 (T0, T1, WDT, LACT); edge sources 58–61 / 62–65.
        private readonly TimerGroup[] _timerGroups = { new TimerGroup(14, 58), new TimerGroup(18, 62) };

        /// <summary>The CPU clock the firmware set: SOC_CLK_SEL in RTC_CNTL_CLK_CONF, the PLL's CPUPERIOD_SEL, XTAL's divider.</summary>
        public double CpuHz => _cpuHz;

        /// <summary>The APB clock: 80 MHz while the CPU runs from the PLL, the CPU clock otherwise.</summary>
        public double ApbHz => _apbHz;

        // Kept from the clock registers, worked out again only when one of them is written (time is read very often).
        private double _cpuHz = 40e6, _apbHz = 40e6;

        private void UpdateClocks()
        {
            uint source = (Reg(RtcClkConf) >> 27) & 3;
            switch (source)
            {
                case 1:
                    // PLL: 80, 160 or 240 MHz
                    uint period = Reg(CpuPerConf) & 3;
                    _cpuHz = period == 0 ? 80e6 : period == 1 ? 160e6 : 240e6;
                    break;
                case 2:
                    _cpuHz = 8e6;   // the 8 MHz RC oscillator
                    break;
                case 3:
                    _cpuHz = 80e6;  // the audio PLL (IDF does not run the CPU from it)
                    break;
                default:
                    _cpuHz = 40e6 / ((Reg(SysClkConf) & 0x3FF) + 1);   // XTAL through APB_CTRL_PRE_DIV
                    break;
            }

            _apbHz = source == 1 ? 80e6 : _cpuHz;
        }

        /// <summary>Chip time in seconds, to the instruction of the core running now.</summary>
        public double Seconds => _seconds + CyclesInTurn() / CpuHz;

        private double ApbTicks => _apbTicks + CyclesInTurn() * ApbHz / CpuHz;

        private double CyclesInTurn() =>
            _running == null ? 0 : _running == Pro ? Pro.Cycles - _proTurnStart : App.Cycles - _appTurnStart;

        private void BeginTurn()
        {
            _proTurnStart = Pro.Cycles;
            _appTurnStart = App.Cycles;
        }

        /// <summary>Closes a turn of <paramref name="cycles"/>: time moves on, and the timers' alarms that came due fire.</summary>
        private void EndTurn(int cycles)
        {
            double cpu = CpuHz;
            _seconds += cycles / cpu;
            _apbTicks += cycles * ApbHz / cpu;
            for (int g = 0; g < 2; g++) ServiceTimerGroup(g);
            ServiceLedc();
            ServiceUarts();
            ServiceI2c();
            ServiceSpi();
            ServiceRmt();
            ServicePcnt();
        }

        private bool ReadTimerRegister(uint register, out uint value)
        {
            value = 0;
            int g = register >= Timg0 && register < Timg0 + 0x100 ? 0 : register >= Timg1 && register < Timg1 + 0x100 ? 1 : -1;
            if (g < 0) return false;
            var group = _timerGroups[g];
            uint offset = register - (g == 0 ? Timg0 : Timg1);
            if (offset >= 0x48 && offset <= 0x64)
            {
                value = group.Watchdog.Read(offset);
                return true;
            }

            if (offset == 0x98) value = group.IntEnable;
            else if (offset == 0x9C) value = group.IntRaw;
            else if (offset == 0xA0) value = group.IntRaw & group.IntEnable;
            else if (TimerAt(group, offset, out var timer, out uint field)) value = timer.Read(field);
            else return false;
            return true;
        }

        private bool WriteTimerRegister(uint register, uint value)
        {
            int g = register >= Timg0 && register < Timg0 + 0x100 ? 0 : register >= Timg1 && register < Timg1 + 0x100 ? 1 : -1;
            if (g < 0) return false;
            var group = _timerGroups[g];
            uint offset = register - (g == 0 ? Timg0 : Timg1);
            if (offset >= 0x48 && offset <= 0x64)
            {
                group.Watchdog.Write(offset, value, ApbTicks);
                return true;
            }

            if (offset == 0x98) group.IntEnable = value & 0xF;
            else if (offset == 0xA4) group.IntRaw &= ~value;
            else if (offset == 0x9C || offset == 0xA0) { }   // read-only
            else if (TimerAt(group, offset, out var timer, out uint field)) timer.Write(field, value, ApbTicks);
            else return false;
            UpdateTimerLines(g);
            return true;
        }

        /// <summary>The timer and its register (as T0's layout: 0 CONFIG, 4 LO, 8 HI, C UPDATE, 10/14 ALARM, 18/1C LOAD, 20 LOAD) at an offset.</summary>
        private static bool TimerAt(TimerGroup group, uint offset, out GroupTimer timer, out uint field)
        {
            if (offset < 0x24)
            {
                timer = group.T0;
                field = offset;
                return true;
            }

            if (offset < 0x48)
            {
                timer = group.T1;
                field = offset - 0x24;
                return true;
            }

            if (offset == 0x70)
            {
                // LACT: CONFIG, then the RTC calibration word (kept as written), then T0's layout from LO on.
                timer = group.Lact;
                field = 0;
                return true;
            }

            if (offset > 0x74 && offset <= 0x94)
            {
                timer = group.Lact;
                field = offset - 0x74;
                return true;
            }

            timer = group.T0;
            field = 0;
            return false;
        }

        private void ServiceTimerGroup(int g)
        {
            var group = _timerGroups[g];
            double apb = ApbTicks;
            uint before = group.IntRaw;
            switch (group.Watchdog.Due(apb))
            {
                case WatchdogAction.Interrupt:
                    group.IntRaw |= 4;
                    break;
                case WatchdogAction.ResetCpu:
                    RequestRestart(11);   // TGWDT_CPU_RESET
                    break;
                case WatchdogAction.ResetSystem:
                    RequestRestart(g == 0 ? 7 : 8);   // TG0WDT_SYS_RESET / TG1WDT_SYS_RESET
                    break;
            }

            if (group.T0.AlarmDue(apb)) group.IntRaw |= 1;
            if (group.T1.AlarmDue(apb)) group.IntRaw |= 2;
            if (group.Lact.AlarmDue(apb)) group.IntRaw |= 8;
            uint rising = group.IntRaw & ~before;
            if (rising == 0) return;
            // The edge sources pulse once per alarm when the timer's EDGE_INT_EN is set.
            if ((rising & 1) != 0 && group.T0.EdgeInterrupt) RaisePeripheral(group.EdgeSource + 0);
            if ((rising & 2) != 0 && group.T1.EdgeInterrupt) RaisePeripheral(group.EdgeSource + 1);
            if ((rising & 8) != 0 && group.Lact.EdgeInterrupt) RaisePeripheral(group.EdgeSource + 3);
            if ((rising & 4) != 0 && group.Watchdog.EdgeInterrupt) RaisePeripheral(group.EdgeSource + 2);
            UpdateTimerLines(g);
        }

        /// <summary>Each timer's level source holds while its interrupt is raised, enabled, and the timer drives the level line.</summary>
        private void UpdateTimerLines(int g)
        {
            var group = _timerGroups[g];
            uint status = group.IntRaw & group.IntEnable;
            SetPeripheralLevel(group.LevelSource + 0, (status & 1) != 0 && group.T0.LevelInterrupt);
            SetPeripheralLevel(group.LevelSource + 1, (status & 2) != 0 && group.T1.LevelInterrupt);
            SetPeripheralLevel(group.LevelSource + 3, (status & 8) != 0 && group.Lact.LevelInterrupt);
            SetPeripheralLevel(group.LevelSource + 2, (status & 4) != 0 && group.Watchdog.LevelInterrupt);
        }

        private enum WatchdogAction
        {
            None,
            Interrupt,
            ResetCpu,
            ResetSystem
        }

        /// <summary>
        /// A timer group's main watchdog (MWDT): CONFIG0 (EN 31, the four stages' actions in 30:29, 28:27, 26:25, 24:23 —
        /// off, interrupt, reset the CPU, reset the system —, EDGE/LEVEL_INT_EN 22/21), CONFIG1's prescaler (APB ticks per
        /// count, 31:16), the stages' timeouts (CONFIG2–5), FEED (any write starts again at stage 0) and the write
        /// protection (nothing changes unless WPROTECT holds 0x50D83AA1). A stage that runs out does its action and the next
        /// stage starts counting; after stage 3 it is stage 0 again.
        /// </summary>
        private sealed class MainWatchdog
        {
            private const uint Key = 0x50D83AA1;
            private uint _config0, _config1, _protect;
            private readonly uint[] _timeout = new uint[4];
            private int _stage;
            private double _stageStart;

            private bool Enabled => (_config0 & (1u << 31)) != 0;
            public bool EdgeInterrupt => (_config0 & (1u << 22)) != 0;
            public bool LevelInterrupt => (_config0 & (1u << 21)) != 0;
            private double TicksPerCount => Math.Max(1u, _config1 >> 16);

            public uint Read(uint offset)
            {
                switch (offset)
                {
                    case 0x48: return _config0;
                    case 0x4C: return _config1;
                    case 0x50: case 0x54: case 0x58: case 0x5C: return _timeout[(offset - 0x50) / 4];
                    case 0x64: return _protect;
                    default: return 0;
                }
            }

            public void Write(uint offset, uint value, double apb)
            {
                if (offset == 0x64)
                {
                    _protect = value;
                    return;
                }

                if (_protect != Key) return;   // locked: the write does nothing
                switch (offset)
                {
                    case 0x48:
                        bool was = Enabled;
                        _config0 = value;
                        if (Enabled && !was) Feed(apb);
                        break;
                    case 0x4C: _config1 = value; break;
                    case 0x50: case 0x54: case 0x58: case 0x5C: _timeout[(offset - 0x50) / 4] = value; break;
                    case 0x60: Feed(apb); break;
                }
            }

            private void Feed(double apb)
            {
                _stage = 0;
                _stageStart = apb;
            }

            /// <summary>The action of a stage that ran out by APB time <paramref name="apb"/> (the strongest, if several did).</summary>
            public WatchdogAction Due(double apb)
            {
                if (!Enabled) return WatchdogAction.None;
                var result = WatchdogAction.None;
                for (int pass = 0; pass < 4; pass++)
                {
                    double length = Math.Max(1u, _timeout[_stage]) * TicksPerCount;
                    if (apb - _stageStart < length) break;
                    var action = (WatchdogAction)((_config0 >> (29 - 2 * _stage)) & 3);
                    if (action > result) result = action;
                    _stageStart += length;
                    _stage = (_stage + 1) & 3;
                }

                return result;
            }
        }

        private sealed class TimerGroup
        {
            public TimerGroup(int levelSource, int edgeSource)
            {
                LevelSource = levelSource;
                EdgeSource = edgeSource;
            }

            public readonly GroupTimer T0 = new GroupTimer(), T1 = new GroupTimer(), Lact = new GroupTimer();
            public readonly MainWatchdog Watchdog = new MainWatchdog();

            /// <summary>INT_ENA / INT_RAW: bit 0 T0, 1 T1, 2 the watchdog, 3 LACT.</summary>
            public uint IntEnable, IntRaw;

            /// <summary>The group's first level and edge interrupt sources (T0; T1 +1, WDT +2, LACT +3).</summary>
            public readonly int LevelSource, EdgeSource;
        }

        /// <summary>One prescaled 64-bit counter: CONFIG (EN 31, INCREASE 30, AUTORELOAD 29, DIVIDER 28:13, EDGE_INT_EN 12, LEVEL_INT_EN 11, ALARM_EN 10).</summary>
        private sealed class GroupTimer
        {
            private uint _config, _alarmLo, _alarmHi, _loadLo, _loadHi, _latchLo, _latchHi;
            private ulong _value, _lastChecked;
            private double _since;

            private bool Enabled => (_config & (1u << 31)) != 0;
            private bool Increase => (_config & (1u << 30)) != 0;
            private bool AutoReload => (_config & (1u << 29)) != 0;
            private bool AlarmEnabled => (_config & (1u << 10)) != 0;
            public bool EdgeInterrupt => (_config & (1u << 12)) != 0;
            public bool LevelInterrupt => (_config & (1u << 11)) != 0;

            /// <summary>The prescaler: 2..65536 APB ticks per count (0 means 65536, 1 counts as 2).</summary>
            private uint Divider
            {
                get
                {
                    uint d = (_config >> 13) & 0xFFFF;
                    return d == 0 ? 65536u : d == 1 ? 2u : d;
                }
            }

            private ulong Alarm => (ulong)_alarmHi << 32 | _alarmLo;

            /// <summary>The counter at APB time <paramref name="apb"/>.</summary>
            private ulong Counter(double apb)
            {
                if (!Enabled) return _value;
                ulong ticks = (ulong)Math.Max(0, Math.Floor((apb - _since) / Divider));
                return Increase ? _value + ticks : _value - ticks;
            }

            /// <summary>Folds the counts so far into the value, so a new divider or direction applies from now.</summary>
            private void Rebase(double apb)
            {
                if (!Enabled)
                {
                    _since = apb;
                    return;
                }

                ulong ticks = (ulong)Math.Max(0, Math.Floor((apb - _since) / Divider));
                _value = Increase ? _value + ticks : _value - ticks;
                _since += ticks * (double)Divider;
            }

            public uint Read(uint field)
            {
                switch (field)
                {
                    case 0x00: return _config;
                    case 0x04: return _latchLo;
                    case 0x08: return _latchHi;
                    case 0x10: return _alarmLo;
                    case 0x14: return _alarmHi;
                    case 0x18: return _loadLo;
                    case 0x1C: return _loadHi;
                    default: return 0;
                }
            }

            public void Write(uint field, uint value, double apb)
            {
                switch (field)
                {
                    case 0x00:
                        Rebase(apb);
                        _config = value;
                        if (!Enabled) _since = apb;
                        break;
                    case 0x0C:
                        // UPDATE: latch the counter into LO/HI
                        ulong now = Counter(apb);
                        _latchLo = (uint)now;
                        _latchHi = (uint)(now >> 32);
                        break;
                    case 0x10:
                        _alarmLo = value;
                        _lastChecked = Counter(apb);
                        break;
                    case 0x14:
                        _alarmHi = value;
                        _lastChecked = Counter(apb);
                        break;
                    case 0x18: _loadLo = value; break;
                    case 0x1C: _loadHi = value; break;
                    case 0x20:
                        // LOAD: the counter takes LOADHI:LOADLO
                        _value = (ulong)_loadHi << 32 | _loadLo;
                        _since = apb;
                        _lastChecked = _value;
                        break;
                }
            }

            /// <summary>
            /// Whether the alarm came due since the last check: the counter passed the alarm value in its direction (an
            /// alarm set behind the counter waits for the wrap, as on the chip). The alarm then disarms, and an
            /// auto-reload timer starts again from its load value.
            /// </summary>
            public bool AlarmDue(double apb)
            {
                ulong now = Counter(apb), last = _lastChecked;
                _lastChecked = now;
                if (!AlarmEnabled || !Enabled) return false;
                ulong alarm = Alarm;
                bool due = Increase ? last < alarm && alarm <= now : last > alarm && alarm >= now;
                if (!due) return false;
                _config &= ~(1u << 10);
                if (AutoReload)
                {
                    _value = (ulong)_loadHi << 32 | _loadLo;
                    _since = apb;
                    _lastChecked = _value;
                }

                return true;
            }
        }
    }
}
