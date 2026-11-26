using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Firmware
{
    /// <summary>[timeline] What a trace entry records.</summary>
    public enum TraceKind : byte
    {
        /// <summary>An Arduino pin (D0–D13 = 0–13, A0–A5 = 14–19) changed level, whoever drove it.</summary>
        Pin,

        /// <summary>The core entered an interrupt vector (1 = INT0 … 25 = SPM_READY).</summary>
        Enter,

        /// <summary>RETI: the interrupt it was in returned.</summary>
        Return,
    }

    /// <summary>[timeline] One recorded event, stamped with the core's cycle count.</summary>
    public readonly struct TraceEvent
    {
        public TraceEvent(ulong cycle, TraceKind kind, byte index, bool level)
        {
            Cycle = cycle;
            Kind = kind;
            Index = index;
            Level = level;
        }

        public ulong Cycle { get; }
        public TraceKind Kind { get; }

        /// <summary>The pin for <see cref="TraceKind.Pin"/>, the vector for <see cref="TraceKind.Enter"/>.</summary>
        public byte Index { get; }

        /// <summary>The pin's new level (pin events only).</summary>
        public bool Level { get; }
    }

    /// <summary>
    /// [timeline] A logic analyzer inside the chip: every level change of its twenty pins (outputs as the program
    /// sets them, inputs as the circuit drives them) and every interrupt's entry and RETI, by cycle, in a ring of the
    /// most recent events. The levels at the oldest kept event are kept up to date as old events fall out, so the
    /// level of every pin at any kept instant is exact. Timer-driven PWM pins are not edges here (the hardware toggles
    /// them without a port write): <see cref="Atmega328P.GetPin"/> gives their duty.
    /// </summary>
    public sealed class McuTrace
    {
        public const int Pins = 20;
        private readonly TraceEvent[] _ring;
        private readonly bool[] _baseline = new bool[Pins];
        private int _start, _count;

        public McuTrace(ulong startCycle, IReadOnlyList<bool> levels, int capacity = 32768)
        {
            _ring = new TraceEvent[Math.Max(16, capacity)];
            StartCycle = startCycle;
            for (int i = 0; i < Pins && i < levels.Count; i++) _baseline[i] = levels[i];
        }

        /// <summary>The cycle the oldest kept event (or the trace's start) is at: nothing earlier is known.</summary>
        public ulong StartCycle { get; private set; }

        public int Count => _count;

        public int Capacity => _ring.Length;

        public TraceEvent this[int i] => _ring[(_start + i) % _ring.Length];

        /// <summary>Pin levels at <see cref="StartCycle"/>.</summary>
        public bool LevelAtStart(int pin) => _baseline[pin];

        internal void Add(TraceEvent e)
        {
            if (_count == _ring.Length)
            {
                var old = _ring[_start];
                if (old.Kind == TraceKind.Pin) _baseline[old.Index] = old.Level;
                StartCycle = old.Cycle;
                _start = (_start + 1) % _ring.Length;
                _count--;
            }

            _ring[(_start + _count) % _ring.Length] = e;
            _count++;
        }

        /// <summary>The level of <paramref name="pin"/> at <paramref name="cycle"/> (its level at the start for an earlier cycle).</summary>
        public bool LevelAt(int pin, ulong cycle)
        {
            bool level = _baseline[pin];
            for (int i = 0; i < _count; i++)
            {
                var e = this[i];
                if (e.Cycle > cycle) break;
                if (e.Kind == TraceKind.Pin && e.Index == pin) level = e.Level;
            }

            return level;
        }

        /// <summary>
        /// The interrupts serviced between two cycles as (vector, entry cycle, return cycle; ulong.MaxValue while still
        /// inside), RETIs paired with the latest open entry (a handler that re-enables interrupts can nest).
        /// </summary>
        public List<(int Vector, ulong Enter, ulong Return)> Interrupts(ulong from, ulong to)
        {
            var done = new List<(int, ulong, ulong)>();
            var open = new Stack<(int Vector, ulong Enter)>();
            for (int i = 0; i < _count; i++)
            {
                var e = this[i];
                if (e.Cycle > to) break;
                if (e.Kind == TraceKind.Enter) open.Push((e.Index, e.Cycle));
                else if (e.Kind == TraceKind.Return && open.Count > 0)
                {
                    var (v, at) = open.Pop();
                    if (e.Cycle >= from) done.Add((v, at, e.Cycle));
                }
            }

            foreach (var (v, at) in open) done.Add((v, at, ulong.MaxValue));
            done.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            return done;
        }

        /// <summary>The pin's level changes between two cycles, oldest first.</summary>
        public List<(ulong Cycle, bool Level)> Edges(int pin, ulong from, ulong to)
        {
            var edges = new List<(ulong, bool)>();
            for (int i = 0; i < _count; i++)
            {
                var e = this[i];
                if (e.Cycle > to) break;
                if (e.Kind == TraceKind.Pin && e.Index == pin && e.Cycle >= from) edges.Add((e.Cycle, e.Level));
            }

            return edges;
        }
    }
}
