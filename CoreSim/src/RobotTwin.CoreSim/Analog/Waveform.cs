using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Analog
{
    /// <summary>Time-dependent value of an independent source.</summary>
    public abstract class Waveform
    {
        public abstract double Value(double time);

        /// <summary>First corner strictly after <paramref name="time"/>, or +infinity.</summary>
        public virtual double NextBreakpoint(double time) => double.PositiveInfinity;

        public static implicit operator Waveform(double value) => new DcWaveform(value);
    }

    public sealed class DcWaveform : Waveform
    {
        public DcWaveform(double value)
        {
            Level = value;
        }

        public double Level { get; set; }

        public override double Value(double time) => Level;
    }

    /// <summary>Trapezoidal pulse train (SPICE PULSE).</summary>
    public sealed class PulseWaveform : Waveform
    {
        public PulseWaveform(double low, double high, double period, double width,
            double delay = 0.0, double rise = 1e-8, double fall = 1e-8)
        {
            if (!(period > 0.0)) throw new ArgumentOutOfRangeException(nameof(period));
            Low = low;
            High = high;
            Period = period;
            Rise = Math.Max(rise, 1e-12);
            Fall = Math.Max(fall, 1e-12);
            Width = AnalogMath.Clamp(width, 0.0, Math.Max(0.0, period - Rise - Fall));
            Delay = Math.Max(0.0, delay);
        }

        public double Low { get; }
        public double High { get; }
        public double Period { get; }
        public double Width { get; }
        public double Delay { get; }
        public double Rise { get; }
        public double Fall { get; }

        /// <summary>Square wave from frequency and duty cycle (0..1).</summary>
        public static PulseWaveform FromDuty(double low, double high, double frequency, double duty,
            double delay = 0.0, double edge = 1e-8)
        {
            double period = 1.0 / frequency;
            double width = AnalogMath.Clamp(duty, 0.0, 1.0) * period - edge;
            return new PulseWaveform(low, high, period, Math.Max(0.0, width), delay, edge, edge);
        }

        public override double Value(double time)
        {
            if (time < Delay) return Low;
            double t = (time - Delay) % Period;
            if (t < Rise) return Low + (High - Low) * (t / Rise);
            t -= Rise;
            if (t < Width) return High;
            t -= Width;
            if (t < Fall) return High + (Low - High) * (t / Fall);
            return Low;
        }

        public override double NextBreakpoint(double time)
        {
            double eps = Math.Max(1e-15, Math.Abs(time) * 1e-13);
            if (time < Delay - eps) return Delay;
            double cycle = Math.Floor((time - Delay) / Period);
            if (cycle < 0) cycle = 0;
            for (int k = 0; k < 2; k++)
            {
                double start = Delay + (cycle + k) * Period;
                double c1 = start, c2 = start + Rise, c3 = c2 + Width, c4 = c3 + Fall;
                if (c1 > time + eps) return c1;
                if (c2 > time + eps) return c2;
                if (c3 > time + eps) return c3;
                if (c4 > time + eps) return c4;
            }

            return Delay + (cycle + 2) * Period;
        }
    }

    public sealed class SineWaveform : Waveform
    {
        public SineWaveform(double offset, double amplitude, double frequency, double delay = 0.0, double phaseDegrees = 0.0)
        {
            Offset = offset;
            Amplitude = amplitude;
            Frequency = frequency;
            Delay = delay;
            PhaseRadians = phaseDegrees * Math.PI / 180.0;
        }

        public double Offset { get; }
        public double Amplitude { get; }
        public double Frequency { get; }
        public double Delay { get; }
        public double PhaseRadians { get; }

        public override double Value(double time)
        {
            if (time < Delay) return Offset + Amplitude * Math.Sin(PhaseRadians);
            return Offset + Amplitude * Math.Sin(2.0 * Math.PI * Frequency * (time - Delay) + PhaseRadians);
        }

        public override double NextBreakpoint(double time) =>
            time < Delay - 1e-15 ? Delay : double.PositiveInfinity;
    }

    /// <summary>Piecewise-linear waveform through (time, value) points.</summary>
    public sealed class PiecewiseLinearWaveform : Waveform
    {
        private readonly double[] _times;
        private readonly double[] _values;

        public PiecewiseLinearWaveform(IReadOnlyList<(double Time, double Value)> points)
        {
            if (points == null || points.Count == 0) throw new ArgumentException("At least one point is required.", nameof(points));
            _times = new double[points.Count];
            _values = new double[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0 && points[i].Time < points[i - 1].Time)
                {
                    throw new ArgumentException("Points must be sorted by time.", nameof(points));
                }

                _times[i] = points[i].Time;
                _values[i] = points[i].Value;
            }
        }

        public override double Value(double time)
        {
            if (time <= _times[0]) return _values[0];
            int last = _times.Length - 1;
            if (time >= _times[last]) return _values[last];
            int hi = Array.BinarySearch(_times, time);
            if (hi >= 0) return _values[hi];
            hi = ~hi;
            int lo = hi - 1;
            double span = _times[hi] - _times[lo];
            if (span <= 0.0) return _values[hi];
            return _values[lo] + (_values[hi] - _values[lo]) * (time - _times[lo]) / span;
        }

        public override double NextBreakpoint(double time)
        {
            double eps = Math.Max(1e-15, Math.Abs(time) * 1e-13);
            foreach (double t in _times)
            {
                if (t > time + eps) return t;
            }

            return double.PositiveInfinity;
        }
    }
    public enum FunctionShape
    {
        Sine,
        Square,
        Triangle,
        Ramp,
        Pulse
    }

    /// <summary>
    /// Function-generator output between <see cref="Low"/> and <see cref="High"/>: sine,
    /// square and pulse (finite rise/fall time), triangle with adjustable symmetry, and
    /// ramp (sawtooth). Every corner is reported as a breakpoint so edges are hit exactly.
    /// </summary>
    public sealed class FunctionWaveform : Waveform
    {
        public FunctionWaveform(FunctionShape shape, double low, double high, double frequency,
            double duty = 0.5, double edge = 10e-9, double phaseDegrees = 0.0, double delay = 0.0)
        {
            if (!(frequency > 0.0)) throw new ArgumentOutOfRangeException(nameof(frequency));
            Shape = shape;
            Low = low;
            High = high;
            Frequency = frequency;
            Period = 1.0 / frequency;
            Edge = AnalogMath.Clamp(edge, 1e-12, 0.25 * Period);
            Duty = AnalogMath.Clamp(duty, 0.0, 1.0);
            PhaseDegrees = phaseDegrees;
            Delay = Math.Max(0.0, delay);
        }

        public FunctionShape Shape { get; }
        public double Low { get; }
        public double High { get; }
        public double Frequency { get; }
        public double Period { get; }

        /// <summary>Square/pulse: high fraction of the period. Triangle: rising fraction (symmetry).</summary>
        public double Duty { get; }

        /// <summary>Rise and fall time of square and pulse edges, fall time of the ramp.</summary>
        public double Edge { get; }

        public double PhaseDegrees { get; }
        public double Delay { get; }

        private double Phase(double time)
        {
            double u = (time - Delay) * Frequency + PhaseDegrees / 360.0;
            u -= Math.Floor(u);
            return u;
        }

        public override double Value(double time)
        {
            if (time < Delay) time = Delay;
            double u = Phase(time);
            double amp = High - Low;
            switch (Shape)
            {
                case FunctionShape.Sine:
                    return Low + 0.5 * amp * (1.0 + Math.Sin(2.0 * Math.PI * u));
                case FunctionShape.Triangle:
                {
                    double s = AnalogMath.Clamp(Duty, 1e-6, 1.0 - 1e-6);
                    return u < s ? Low + amp * u / s : High - amp * (u - s) / (1.0 - s);
                }

                case FunctionShape.Ramp:
                {
                    double e = Edge * Frequency;
                    double rise = 1.0 - e;
                    return u < rise ? Low + amp * u / rise : High - amp * (u - rise) / e;
                }

                default:
                {
                    double e = Edge * Frequency;
                    double width = Math.Max(0.0, Duty - e);
                    if (u < e) return Low + amp * u / e;
                    if (u < e + width) return High;
                    if (u < e + width + e) return High - amp * (u - e - width) / e;
                    return Low;
                }
            }
        }

        public override double NextBreakpoint(double time)
        {
            if (Shape == FunctionShape.Sine) return time < Delay - 1e-15 ? Delay : double.PositiveInfinity;
            double eps = Math.Max(1e-15, Math.Abs(time) * 1e-13);
            if (time < Delay - eps) return Delay;
            double e = Edge * Frequency;
            double[] corners;
            switch (Shape)
            {
                case FunctionShape.Triangle:
                    corners = new[] { 0.0, AnalogMath.Clamp(Duty, 1e-6, 1.0 - 1e-6) };
                    break;
                case FunctionShape.Ramp:
                    corners = new[] { 0.0, 1.0 - e };
                    break;
                default:
                {
                    double width = Math.Max(0.0, Duty - e);
                    corners = new[] { 0.0, e, e + width, e + width + e };
                    break;
                }
            }

            double offset = PhaseDegrees / 360.0;
            double cycle = Math.Floor((time - Delay) * Frequency + offset);
            for (int k = 0; k < 3; k++)
            {
                foreach (double c in corners)
                {
                    double t = Delay + (cycle + k + c - offset) * Period;
                    if (t > time + eps) return t;
                }
            }

            return double.PositiveInfinity;
        }
    }
}
