using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;

namespace RobotTwin.CoreSim.Instruments
{
    public enum ScopeCoupling
    {
        DC,
        AC,
        GND
    }

    public enum TriggerSlope
    {
        Rising,
        Falling
    }

    public enum TriggerMode
    {
        /// <summary>Triggers on the edge; free-runs if no edge comes within the auto timeout.</summary>
        Auto,

        /// <summary>Only triggered acquisitions; the last one stays on screen while waiting.</summary>
        Normal,

        /// <summary>One triggered acquisition, then stop.</summary>
        Single
    }

    public enum TriggerStatus
    {
        Stopped,
        Triggered,
        Auto,
        Waiting,
        Roll
    }

    public enum ScopeMathFunction
    {
        Off,
        Subtract,
        Add,

        /// <summary>A × B: a voltage times a current is the power (W).</summary>
        Multiply,

        /// <summary>∫A dt from the screen's left edge: a current gives the charge (C), a power the energy (J).</summary>
        Integrate,

        /// <summary>dA/dt: a slew rate (V/s), a current's rate of change (A/s).</summary>
        Differentiate
    }

    /// <summary>Vertical settings of one input channel.</summary>
    public sealed class ScopeChannel
    {
        private double _probe = 10.0;

        public ScopeChannel(int index)
        {
            Index = index;
            Name = "CH" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public int Index { get; }
        public string Name { get; }
        public bool Enabled { get; set; } = true;

        /// <summary>The signal at the probe tip (null = probe not connected).</summary>
        public ISampleSource? Source { get; set; }

        /// <summary>What the probe is attached to ("ÇIKIŞ", "R1 akımı").</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>"V" for voltage probes, "A" for a current probe.</summary>
        public string Unit { get; set; } = "V";

        public bool IsCurrent => Unit == "A";

        /// <summary>Volts (or amps) per division as shown on screen, probe factor included.</summary>
        public double Scale { get; set; } = 1.0;

        /// <summary>Vertical position of the trace zero, in divisions (positive = up).</summary>
        public double Position { get; set; }

        public ScopeCoupling Coupling { get; set; } = ScopeCoupling.DC;

        /// <summary>Probe attenuation: 1 (1×) or 10 (10×). Current probes are always 1.</summary>
        public double ProbeAttenuation
        {
            get => IsCurrent ? 1.0 : _probe;
            set => _probe = value >= 5 ? 10.0 : 1.0;
        }

        /// <summary>20 MHz bandwidth limit filter.</summary>
        public bool BandwidthLimit { get; set; }

        public bool Invert { get; set; }

        /// <summary>Smallest and largest V/div the front end allows with the present probe.</summary>
        public double MinScale => IsCurrent ? 1e-3 : 1e-3 * ProbeAttenuation;

        public double MaxScale => IsCurrent ? 10.0 : 10.0 * ProbeAttenuation;
    }

    /// <summary>One acquired trace: uniformly sampled values in signal units (NaN where the input had no data).</summary>
    public sealed class ScopeTrace
    {
        public ScopeTrace(string name, string unit, double scale, double position, double[] values)
        {
            Name = name;
            Unit = unit;
            Scale = scale;
            Position = position;
            Values = values;
        }

        public string Name { get; }
        public string Unit { get; }
        public double Scale { get; }
        public double Position { get; }
        public double[] Values { get; }
        public ScopeChannel? Channel { get; set; }

        /// <summary>The input exceeded the ADC range (the trace is flat-topped and measurements are unreliable).</summary>
        public bool Clipped { get; set; }

        public ScopeMeasurements Measurements { get; set; } = ScopeMeasurements.Empty;

        /// <summary>Vertical screen position (divisions from the centre line, positive up) of a value.</summary>
        public double DivOf(double value) => value / Scale + Position;
    }

    /// <summary>A complete acquisition as it is drawn on the 10 × 8 graticule.</summary>
    public sealed class ScopeFrame
    {
        public double StartTime { get; set; }
        public double SampleInterval { get; set; }
        public int Length { get; set; }
        public double SecondsPerDiv { get; set; }
        public double TriggerTime { get; set; } = double.NaN;
        public double TriggerLevel { get; set; } = double.NaN;
        public TriggerStatus Status { get; set; }
        public List<ScopeTrace> Traces { get; } = new List<ScopeTrace>();
        public ScopeTrace? Math { get; set; }
        public double SampleRate => 1.0 / SampleInterval;
        public double ScreenStart => StartTime;
        public double ScreenEnd => StartTime + 10.0 * SecondsPerDiv;

        /// <summary>Horizontal screen position (0..10 divisions) of sample i.</summary>
        public double DivX(int i) => i * SampleInterval / SecondsPerDiv;

        public double TimeAt(int i) => StartTime + i * SampleInterval;

        public ScopeTrace? TraceOf(ScopeChannel channel)
        {
            foreach (var t in Traces) if (ReferenceEquals(t.Channel, channel)) return t;
            return null;
        }
    }

    /// <summary>
    /// Digital storage oscilloscope: 4 channels with V/div, position, AC/DC/GND coupling,
    /// 1×/10× probes, analog bandwidth and 20 MHz limit, 8-bit ADC with a noise floor,
    /// sample rate limited by the record length, edge trigger with level, slope, holdoff and
    /// auto/normal/single modes, roll mode on slow timebases, math (A−B, A+B) and automatic
    /// measurements. Its "analog input" are the solver samples of the probed nets.
    /// </summary>
    public sealed class Oscilloscope
    {
        public const int HorizontalDivisions = 10;
        public const int VerticalDivisions = 8;

        private readonly List<ScopeChannel> _channels = new List<ScopeChannel>();
        private double _lastNow = double.NaN;
        private double _scanCursor = double.NegativeInfinity;
        private double _lastTrigger = double.NegativeInfinity;
        private double _lastTriggerFoundAt = double.NegativeInfinity;
        private bool _armed;
        private ulong _acquisitions;

        public Oscilloscope(int channels = 4)
        {
            for (int i = 0; i < channels; i++) _channels.Add(new ScopeChannel(i));
        }

        public IReadOnlyList<ScopeChannel> Channels => _channels;

        // ------------------------------------------------------------------ horizontal

        public double SecondsPerDiv { get; set; } = 1e-3;

        /// <summary>Horizontal position: time of the screen centre relative to the trigger point.</summary>
        public double Delay { get; set; }

        public static double MinSecondsPerDiv => 10e-9;
        public static double MaxSecondsPerDiv => 50.0;

        /// <summary>Points per screen the acquisition memory holds.</summary>
        public int RecordLength { get; set; } = 10_000;

        public double MaxSampleRate { get; set; } = 1e9;

        /// <summary>Timebases this slow (or slower) scroll continuously in Auto mode.</summary>
        public double RollThreshold { get; set; } = 0.1;

        // ------------------------------------------------------------------ front end

        public double Bandwidth { get; set; } = 100e6;
        public double BandwidthLimitFrequency { get; set; } = 20e6;
        public double AcCouplingCorner { get; set; } = 10.0;
        public int AdcBits { get; set; } = 8;

        /// <summary>Input-referred noise: a fixed floor plus a fraction of the input V/div.</summary>
        public double NoiseFloor { get; set; } = 150e-6;
        public double NoiseFraction { get; set; } = 0.012;
        public double CurrentProbeNoise { get; set; } = 0.4e-3;

        public bool NoiseEnabled { get; set; } = true;

        // ------------------------------------------------------------------ trigger

        public int TriggerSource { get; set; }
        public TriggerSlope TriggerSlope { get; set; } = TriggerSlope.Rising;
        public double TriggerLevel { get; set; }
        public TriggerMode TriggerMode { get; set; } = TriggerMode.Auto;
        public double Holdoff { get; set; }

        public bool Running { get; private set; } = true;

        public TriggerStatus Status { get; private set; } = TriggerStatus.Auto;

        // ------------------------------------------------------------------ display

        public bool XY { get; set; }
        public ScopeMathFunction MathFunction { get; set; }
        public double MathScale { get; set; } = 1.0;
        public double MathPosition { get; set; }

        /// <summary>[scope] The math's sources (channel indices): A, and B for the two-input functions.</summary>
        public int MathSourceA { get; set; }

        public int MathSourceB { get; set; } = 1;

        /// <summary>[scope] The next frame sets the math's scale so its trace fills about six divisions (after the function or a source changes).</summary>
        public bool MathAutoScale { get; set; }

        /// <summary>
        /// [decode] The serial decoder: "" (off), "UART", "I2C" or "SPI"; its channels (UART: A the line; I²C: A SCL, B SDA;
        /// SPI: A SCK, B data, C chip select or −1), the UART's baud rate and parity, the SPI mode.
        /// </summary>
        public string DecodeProtocol { get; set; } = string.Empty;

        public int DecodeA { get; set; }

        public int DecodeB { get; set; } = 1;

        public int DecodeC { get; set; } = -1;

        public double DecodeBaud { get; set; } = 9600;

        public string DecodeParity { get; set; } = "none";

        public int DecodeSpiMode { get; set; }

        /// <summary>[decode] The frame's serial traffic, decoded with the settings above (empty when off or a channel has no probe).</summary>
        public List<DecodedField> Decode(ScopeFrame frame)
        {
            var none = new List<DecodedField>();
            if (DecodeProtocol.Length == 0) return none;
            double[]? Wire(int channel) => channel >= 0 && channel < Channels.Count ? frame.TraceOf(Channels[channel])?.Values : null;
            // The logic threshold: half way between the wire's lowest and highest level in the frame.
            double Threshold(double[] v)
            {
                double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                foreach (double x in v)
                {
                    if (x < lo) lo = x;
                    if (x > hi) hi = x;
                }

                return hi - lo < 0.2 ? double.PositiveInfinity : (lo + hi) / 2;
            }

            var a = Wire(DecodeA);
            if (a == null) return none;
            switch (DecodeProtocol)
            {
                case "UART":
                    return ProtocolDecoder.Uart(a, frame.StartTime, frame.SampleInterval, DecodeBaud, Threshold(a), parity: DecodeParity);
                case "I2C":
                {
                    var b = Wire(DecodeB);
                    return b == null ? none : ProtocolDecoder.I2c(a, b, frame.StartTime, frame.SampleInterval, Threshold(a));
                }

                case "SPI":
                {
                    var b = Wire(DecodeB);
                    return b == null ? none : ProtocolDecoder.Spi(a, b, Wire(DecodeC), frame.StartTime, frame.SampleInterval, Threshold(a), DecodeSpiMode);
                }

                default:
                    return none;
            }
        }

        public ScopeFrame? Frame { get; private set; }

        /// <summary>
        /// [ref] A screen kept to compare with (a bench scope's REF memory): the frame shown when it was kept, its traces at
        /// their own scales and time base, drawn under the live ones; it outlives runs, so one run can be laid over another.
        /// </summary>
        public ScopeFrame? Reference { get; set; }

        /// <summary>[ref] Keeps the screen now as the reference; false when nothing is shown yet.</summary>
        public bool KeepReference()
        {
            if (Frame == null || (Frame.Traces.Count == 0 && Frame.Math == null)) return false;
            Reference = Frame;
            return true;
        }

        /// <summary>
        /// [ref] How far a live trace strays from the kept one on the same channel: the RMS of their difference over the
        /// samples both screens share (same time base and length), or NaN when they cannot be compared sample by sample.
        /// </summary>
        public static double ReferenceDeviation(ScopeFrame reference, ScopeFrame live, ScopeTrace liveTrace)
        {
            var kept = reference.Traces.FirstOrDefault(t => t.Channel != null && t.Channel == liveTrace.Channel);
            if (kept == null || reference.Length != live.Length || Math.Abs(reference.SampleInterval - live.SampleInterval) > 1e-12 * Math.Max(1, live.SampleInterval)) return double.NaN;
            int n = Math.Min(kept.Values.Length, liveTrace.Values.Length);
            if (n == 0) return double.NaN;
            double sum = 0;
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                double d = liveTrace.Values[i] - kept.Values[i];
                if (double.IsNaN(d)) continue;
                sum += d * d;
                count++;
            }

            return count == 0 ? double.NaN : Math.Sqrt(sum / count);
        }

        public double SampleRate => Math.Min(MaxSampleRate, RecordLength / (HorizontalDivisions * SecondsPerDiv));

        public void Run()
        {
            Running = true;
            _armed = true;
        }

        public void Stop()
        {
            Running = false;
            Status = TriggerStatus.Stopped;
            if (Frame != null) Frame.Status = TriggerStatus.Stopped;
        }

        /// <summary>SINGLE: arm for one triggered acquisition.</summary>
        public void Single()
        {
            TriggerMode = TriggerMode.Single;
            Running = true;
            _armed = true;
            Status = TriggerStatus.Waiting;
        }

        /// <summary>Forgets trigger history (new simulation run).</summary>
        public void ResetAcquisition()
        {
            _lastNow = double.NaN;
            _scanCursor = double.NegativeInfinity;
            _lastTrigger = double.NegativeInfinity;
            _lastTriggerFoundAt = double.NegativeInfinity;
            Frame = null;
        }

        // ------------------------------------------------------------------ acquisition

        /// <summary>Acquires a new frame at simulation time <paramref name="now"/> (returns the frozen frame when stopped or waiting).</summary>
        public ScopeFrame? Acquire(double now)
        {
            if (!double.IsNaN(_lastNow) && now < _lastNow) ResetAcquisition();
            if (!Running) return Frame;
            if (!double.IsNaN(_lastNow) && now == _lastNow && Frame != null) return Frame;
            _lastNow = now;

            double spd = SecondsPerDiv;
            double screen = HorizontalDivisions * spd;
            double pre = 0.5 * screen - Delay;
            double post = 0.5 * screen + Delay;
            double start;
            double trig = double.NaN;
            TriggerStatus status;

            bool roll = TriggerMode == TriggerMode.Auto && spd >= RollThreshold;
            if (roll)
            {
                start = now - screen;
                status = TriggerStatus.Roll;
            }
            else
            {
                double found = FindTrigger(now, post, screen);
                if (!double.IsNaN(found))
                {
                    trig = found;
                    start = found - pre;
                    status = TriggerStatus.Triggered;
                    _lastTriggerFoundAt = now;
                }
                else
                {
                    bool recent = now - _lastTriggerFoundAt <= Math.Max(2.5 * screen + Holdoff, 20e-3);
                    if (TriggerMode == TriggerMode.Auto && (!recent || Frame == null))
                    {
                        start = now - screen;
                        status = TriggerStatus.Auto;
                    }
                    else
                    {
                        // Between triggers the last triggered record stays on screen.
                        Status = recent && TriggerMode != TriggerMode.Single ? TriggerStatus.Triggered : TriggerStatus.Waiting;
                        if (Frame != null) Frame.Status = Status;
                        return Frame;
                    }
                }
            }

            int n = Math.Max(16, Math.Min(RecordLength, (int)Math.Round(screen * SampleRate)));
            double dt = screen / n;
            var frame = new ScopeFrame
            {
                StartTime = start,
                SampleInterval = dt,
                Length = n + 1,
                SecondsPerDiv = spd,
                TriggerTime = trig,
                TriggerLevel = TriggerLevel,
                Status = status
            };

            _acquisitions++;
            foreach (var ch in _channels)
            {
                if (!ch.Enabled || ch.Source == null) continue;
                var trace = BuildTrace(ch, start, dt, n + 1);
                trace.Measurements = ScopeMeasurements.Compute(trace.Values, dt, trace.Scale);
                frame.Traces.Add(trace);
            }

            if (MathFunction != ScopeMathFunction.Off && _channels.Count >= 1)
            {
                var a = frame.TraceOf(_channels[Math.Max(0, Math.Min(_channels.Count - 1, MathSourceA))]);
                var b = frame.TraceOf(_channels[Math.Max(0, Math.Min(_channels.Count - 1, MathSourceB))]);
                var math = ScopeMath.Compute(MathFunction, a, b, dt, MathSourceA, MathSourceB);
                if (math != null)
                {
                    if (MathAutoScale)
                    {
                        MathScale = ScopeMath.NiceScale(math.Values);
                        MathAutoScale = false;
                    }

                    frame.Math = new ScopeTrace(math.Name, math.Unit, MathScale, MathPosition, math.Values) { Clipped = math.Clipped };
                    frame.Math.Measurements = ScopeMeasurements.Compute(math.Values, dt, MathScale);
                }
            }

            Frame = frame;
            Status = status;
            if (TriggerMode == TriggerMode.Single && status == TriggerStatus.Triggered)
            {
                Running = false;
                Status = TriggerStatus.Stopped;
            }

            return frame;
        }

        /// <summary>
        /// Scans the trigger source for edges with hysteresis, honouring holdoff (an accepted
        /// trigger blocks new ones for one record plus the holdoff time). Returns the latest
        /// trigger whose post-trigger part of the record is already available.
        /// </summary>
        private double FindTrigger(double now, double post, double screen)
        {
            if (TriggerSource < 0 || TriggerSource >= _channels.Count) return double.NaN;
            var ch = _channels[TriggerSource];
            var src = ch.Source;
            if (src == null || src.Count < 2 || ch.Coupling == ScopeCoupling.GND) return double.NaN;

            double scanTo = now - Math.Max(0.0, post);
            double scanFrom = Math.Max(_scanCursor, Math.Max(src[0].T, now - Math.Max(6.0 * screen, 0.05)));
            if (scanTo <= scanFrom) return double.NaN;

            double offset = 0.0;
            if (ch.Coupling == ScopeCoupling.AC)
            {
                double tau = 1.0 / (2.0 * Math.PI * AcCouplingCorner);
                offset = SampleMath.Mean(src, scanTo - 3.0 * tau, scanTo);
                if (double.IsNaN(offset)) offset = 0.0;
            }

            double sign = ch.Invert ? -1.0 : 1.0;
            double level = TriggerLevel;
            double hyst = 0.12 * ch.Scale;
            bool rising = TriggerSlope == TriggerSlope.Rising;
            double spacing = screen + Math.Max(0.0, Holdoff);
            double best = double.NaN;

            int i0 = Math.Max(1, src.LowerBound(scanFrom));
            int i1 = src.LowerBound(scanTo);
            for (int i = i0; i < i1 && i < src.Count; i++)
            {
                var (ta, va0) = src[i - 1];
                var (tb, vb0) = src[i];
                double va = sign * (va0 - offset), vb = sign * (vb0 - offset);
                if (!rising)
                {
                    va = -va;
                    vb = -vb;
                }

                double lv = rising ? level : -level;
                if (vb < lv - hyst) _armed = true;
                if (_armed && va < lv && vb >= lv)
                {
                    double tc = vb == va ? tb : ta + (lv - va) / (vb - va) * (tb - ta);
                    if (tc >= _lastTrigger + spacing && tc <= scanTo)
                    {
                        _lastTrigger = tc;
                        best = tc;
                        _armed = false;
                    }
                }
            }

            _scanCursor = scanTo;
            return best;
        }

        private ScopeTrace BuildTrace(ScopeChannel ch, double start, double dt, int n)
        {
            var src = ch.Source!;
            var values = new double[n];
            for (int i = 0; i < n; i++) values[i] = double.NaN;
            var trace = new ScopeTrace(ch.Name, ch.Unit, ch.Scale, ch.Position, values) { Channel = ch };
            if (src.Count == 0) return trace;

            double end = start + (n - 1) * dt;
            if (ch.Coupling == ScopeCoupling.GND)
            {
                for (int i = 0; i < n; i++)
                {
                    double t = start + i * dt;
                    if (t >= src[0].T && t <= src[src.Count - 1].T) values[i] = 0.0;
                }
            }
            else
            {
                SampleConditioned(src, ch.Coupling == ScopeCoupling.AC, start, dt, values);
                ApplyBandwidth(values, dt, ch.BandwidthLimit ? Math.Min(BandwidthLimitFrequency, Bandwidth) : Bandwidth);
            }

            // Front end: inversion, noise, 8-bit ADC over ±5.12 div around the screen centre.
            var rng = new InstrumentRandom(InstrumentRandom.Mix(_acquisitions * 7919UL + (ulong)ch.Index + 1));
            double sigma = 0.0;
            if (NoiseEnabled)
            {
                double inputScale = ch.Scale / ch.ProbeAttenuation;
                sigma = ch.IsCurrent
                    ? Math.Sqrt(CurrentProbeNoise * CurrentProbeNoise + Math.Pow(NoiseFraction * ch.Scale, 2))
                    : ch.ProbeAttenuation * Math.Sqrt(NoiseFloor * NoiseFloor + Math.Pow(NoiseFraction * inputScale, 2));
            }

            double lsb = 10.24 * ch.Scale / Math.Pow(2, AdcBits);
            double top = 5.12 * ch.Scale;
            double offset = ch.Position * ch.Scale;
            bool clipped = false;
            for (int i = 0; i < n; i++)
            {
                double v = values[i];
                if (double.IsNaN(v)) continue;
                if (ch.Invert) v = -v;
                if (sigma > 0) v += sigma * rng.NextGaussian();
                double screenValue = v + offset;
                double q = Math.Round(screenValue / lsb) * lsb;
                if (q > top)
                {
                    q = top;
                    clipped = true;
                }
                else if (q < -top)
                {
                    q = -top;
                    clipped = true;
                }

                values[i] = q - offset;
            }

            trace.Clipped = clipped;
            _ = end;
            return trace;
        }

        /// <summary>
        /// Samples the piecewise-linear input at uniform times; with AC coupling the exact
        /// response of the first-order high-pass to each linear segment is used.
        /// </summary>
        private void SampleConditioned(ISampleSource src, bool ac, double start, double dt, double[] values)
        {
            int n = values.Length;
            int count = src.Count;
            double first = src[0].T, last = src[count - 1].T;
            if (!ac)
            {
                int j = Math.Max(1, src.LowerBound(start));
                for (int i = 0; i < n; i++)
                {
                    double t = start + i * dt;
                    if (t < first || t > last) continue;
                    while (j < count - 1 && src[j].T < t) j++;
                    var (tb, vb) = src[j];
                    var (ta, va) = src[Math.Max(0, j - 1)];
                    values[i] = tb > ta ? va + (vb - va) * (t - ta) / (tb - ta) : vb;
                }

                return;
            }

            double tau = 1.0 / (2.0 * Math.PI * AcCouplingCorner);
            double begin = Math.Max(first, start - 3.0 * tau);
            double w = SampleMath.Mean(src, begin - 2.0 * tau, begin);
            if (double.IsNaN(w)) w = SampleMath.ValueAt(src, begin);
            int k = Math.Max(1, src.LowerBound(begin));
            double tPrev = begin, xPrev = SampleMath.ValueAt(src, begin);
            int idx = 0;
            while (idx < n && start + idx * dt < begin) idx++;
            for (; k < count && idx < n; k++)
            {
                var (tb, xb) = src[k];
                if (tb <= tPrev) continue;
                double h = tb - tPrev;
                double slope = (xb - xPrev) / h;
                // Output samples that fall inside this segment.
                while (idx < n)
                {
                    double ts = start + idx * dt;
                    if (ts > tb) break;
                    double d = ts - tPrev;
                    double x = xPrev + slope * d;
                    double ws = x - slope * tau + (w - xPrev + slope * tau) * Math.Exp(-d / tau);
                    values[idx] = x - ws;
                    idx++;
                }

                w = xb - slope * tau + (w - xPrev + slope * tau) * Math.Exp(-h / tau);
                tPrev = tb;
                xPrev = xb;
            }
        }

        /// <summary>First-order analog bandwidth on the uniformly sampled record (exact for linear interpolation between samples).</summary>
        private static void ApplyBandwidth(double[] values, double dt, double bandwidth)
        {
            double tau = 1.0 / (2.0 * Math.PI * bandwidth);
            if (dt > 20.0 * tau) return;
            double a = Math.Exp(-dt / tau);
            double y = double.NaN, xPrev = double.NaN;
            for (int i = 0; i < values.Length; i++)
            {
                double x = values[i];
                if (double.IsNaN(x)) continue;
                if (double.IsNaN(y))
                {
                    y = x;
                    xPrev = x;
                    continue;
                }

                double slope = (x - xPrev) / dt;
                y = x - slope * tau + (y - xPrev + slope * tau) * a;
                xPrev = x;
                values[i] = y;
            }
        }

        // ------------------------------------------------------------------ knobs

        /// <summary>AUTOSET: picks V/div, time/div and trigger level for the connected channels.</summary>
        public void Autoset(double now)
        {
            ScopeChannel? first = null;
            foreach (var ch in _channels)
            {
                if (ch.Source == null || ch.Source.Count < 2) continue;
                var src = ch.Source;
                double from = Math.Max(src[0].T, now - 0.2);
                int i0 = src.LowerBound(from);
                double min = double.PositiveInfinity, max = double.NegativeInfinity;
                for (int i = i0; i < src.Count; i++)
                {
                    double v = src[i].V;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }

                if (double.IsInfinity(min)) continue;
                ch.Enabled = true;
                ch.Coupling = ScopeCoupling.DC;
                double span = Math.Max(max - min, Math.Max(Math.Abs(max), Math.Abs(min)) * 0.25);
                double mid = 0.5 * (min + max);
                double scale = Steps125.AtLeast(Math.Max(Math.Max(span / 6.0, Math.Abs(mid) / 3.5), ch.MinScale));
                ch.Scale = Math.Min(ch.MaxScale, scale);
                ch.Position = Math.Max(-3.5, Math.Min(3.5, -mid / ch.Scale));
                if (first == null)
                {
                    first = ch;
                    TriggerSource = ch.Index;
                    TriggerLevel = mid;
                    TriggerSlope = TriggerSlope.Rising;
                    double freq = EstimateFrequency(src, from, now, mid, 0.1 * (max - min));
                    SecondsPerDiv = freq > 0
                        ? Math.Max(MinSecondsPerDiv, Steps125.AtLeast(2.5 / freq / HorizontalDivisions))
                        : 1e-3;
                }
            }

            Delay = 0.0;
            TriggerMode = TriggerMode.Auto;
            Run();
            ResetTriggerScan();
        }

        /// <summary>
        /// Vertical auto-scale of one channel from its recent signal (what happens when a probe is
        /// first clipped on): 1-2-5 V/div so the swing fills ~6 divisions, centred; optionally the
        /// trigger level goes to the middle of the swing. Returns false when there is no data yet.
        /// </summary>
        public bool AutoScaleChannel(ScopeChannel ch, double now, bool setTrigger)
        {
            var src = ch.Source;
            if (src == null || src.Count < 8) return false;
            double from = Math.Max(src[0].T, now - Math.Max(0.05, 10 * SecondsPerDiv));
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            for (int i = src.LowerBound(from); i < src.Count; i++)
            {
                double v = src[i].V;
                if (v < min) min = v;
                if (v > max) max = v;
            }

            if (double.IsInfinity(min)) return false;
            double span = Math.Max(max - min, Math.Max(Math.Abs(max), Math.Abs(min)) * 0.25);
            if (span <= 0) span = ch.IsCurrent ? 1e-3 : 0.1;
            double mid = 0.5 * (min + max);
            // The offset range of the front end is limited: the centre must stay within ±3.5 div.
            ch.Scale = Math.Min(ch.MaxScale, Math.Max(ch.MinScale, Steps125.AtLeast(Math.Max(span / 6.0, Math.Abs(mid) / 3.5))));
            ch.Position = Math.Max(-3.5, Math.Min(3.5, -mid / ch.Scale));
            if (setTrigger && TriggerSource == ch.Index)
            {
                TriggerLevel = mid;
                ResetTriggerScan();
            }

            return true;
        }

        /// <summary>Trigger settings changed: rescan from the present time.</summary>
        public void ResetTriggerScan()
        {
            _scanCursor = double.NegativeInfinity;
            _lastTrigger = double.NegativeInfinity;
            _lastNow = double.NaN;
        }

        private static double EstimateFrequency(ISampleSource src, double from, double to, double level, double hyst)
        {
            if (hyst <= 0) return 0.0;
            bool high = false;
            double firstRise = double.NaN, lastRise = double.NaN;
            int rises = 0;
            for (int i = Math.Max(1, src.LowerBound(from)); i < src.Count && src[i].T <= to; i++)
            {
                var (ta, va) = src[i - 1];
                var (tb, vb) = src[i];
                if (!high && vb > level + hyst)
                {
                    high = true;
                    double tc = vb == va ? tb : ta + (level - va) / (vb - va) * (tb - ta);
                    if (rises == 0) firstRise = tc;
                    lastRise = tc;
                    rises++;
                }
                else if (high && vb < level - hyst)
                {
                    high = false;
                }
            }

            return rises >= 2 ? (rises - 1) / (lastRise - firstRise) : 0.0;
        }

        /// <summary>Value at a horizontal cursor (time relative to the screen start) for a trace.</summary>
        public static double ValueAt(ScopeFrame frame, ScopeTrace trace, double time)
        {
            double x = (time - frame.StartTime) / frame.SampleInterval;
            int i = (int)Math.Floor(x);
            if (i < 0 || i >= trace.Values.Length) return double.NaN;
            if (i + 1 >= trace.Values.Length) return trace.Values[i];
            double f = x - i;
            return trace.Values[i] + (trace.Values[i + 1] - trace.Values[i]) * f;
        }
    }

    /// <summary>Automatic measurements over the displayed record.</summary>
    public sealed class ScopeMeasurements
    {
        public static readonly ScopeMeasurements Empty = new ScopeMeasurements();

        public double Max { get; private set; } = double.NaN;
        public double Min { get; private set; } = double.NaN;
        public double PeakToPeak => Max - Min;
        public double Top { get; private set; } = double.NaN;
        public double Base { get; private set; } = double.NaN;
        public double Amplitude => Top - Base;
        public double Mean { get; private set; } = double.NaN;
        public double Rms { get; private set; } = double.NaN;
        public double Period { get; private set; } = double.NaN;
        public double Frequency => Period > 0 ? 1.0 / Period : double.NaN;

        /// <summary>Positive duty cycle, 0..1.</summary>
        public double Duty { get; private set; } = double.NaN;

        /// <summary>10 %–90 % rise time.</summary>
        public double RiseTime { get; private set; } = double.NaN;

        public double FallTime { get; private set; } = double.NaN;

        public static ScopeMeasurements Compute(double[] v, double dt, double scale)
        {
            var m = new ScopeMeasurements();
            int count = 0;
            double sum = 0, sumSq = 0, min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (double x in v)
            {
                if (double.IsNaN(x)) continue;
                count++;
                sum += x;
                sumSq += x * x;
                if (x < min) min = x;
                if (x > max) max = x;
            }

            if (count < 4) return m;
            m.Min = min;
            m.Max = max;
            m.Mean = sum / count;
            m.Rms = Math.Sqrt(Math.Max(0, sumSq / count));

            // Top/base from the 2nd/98th percentile so noise spikes do not move them.
            var sorted = new double[count];
            int k = 0;
            foreach (double x in v) if (!double.IsNaN(x)) sorted[k++] = x;
            Array.Sort(sorted);
            m.Base = sorted[(int)(0.02 * (count - 1))];
            m.Top = sorted[(int)Math.Ceiling(0.98 * (count - 1))];
            double amp = m.Top - m.Base;
            if (amp < Math.Max(0.15 * scale, 1e-12)) return m;

            double mid = 0.5 * (m.Top + m.Base), hyst = 0.1 * amp;
            var rises = new List<double>();
            var falls = new List<double>();
            bool? high = null;
            double lastUp = double.NaN, lastDown = double.NaN;
            for (int i = 1; i < v.Length; i++)
            {
                double a = v[i - 1], b = v[i];
                if (double.IsNaN(a) || double.IsNaN(b)) continue;
                if (a < mid && b >= mid) lastUp = (i - 1 + (mid - a) / (b - a)) * dt;
                if (a > mid && b <= mid) lastDown = (i - 1 + (a - mid) / (a - b)) * dt;
                if (high != true && b > mid + hyst)
                {
                    if (high == false && !double.IsNaN(lastUp)) rises.Add(lastUp);
                    high = true;
                }
                else if (high != false && b < mid - hyst)
                {
                    if (high == true && !double.IsNaN(lastDown)) falls.Add(lastDown);
                    high = false;
                }
            }

            if (rises.Count >= 2)
            {
                m.Period = (rises[rises.Count - 1] - rises[0]) / (rises.Count - 1);
                double highSum = 0, periodSum = 0;
                for (int i = 0; i + 1 < rises.Count; i++)
                {
                    foreach (double f in falls)
                    {
                        if (f > rises[i] && f < rises[i + 1])
                        {
                            highSum += f - rises[i];
                            periodSum += rises[i + 1] - rises[i];
                            break;
                        }
                    }
                }

                if (periodSum > 0) m.Duty = highSum / periodSum;
            }
            else if (falls.Count >= 2)
            {
                m.Period = (falls[falls.Count - 1] - falls[0]) / (falls.Count - 1);
            }

            m.RiseTime = EdgeTime(v, dt, rises, m.Base + 0.1 * amp, m.Base + 0.9 * amp, true);
            m.FallTime = EdgeTime(v, dt, falls, m.Base + 0.9 * amp, m.Base + 0.1 * amp, false);
            return m;
        }

        private static double EdgeTime(double[] v, double dt, List<double> crossings, double from, double to, bool rising)
        {
            double total = 0;
            int edges = 0;
            foreach (double tc in crossings)
            {
                int c = (int)(tc / dt);
                double t10 = double.NaN, t90 = double.NaN;
                for (int i = c; i > 0; i--)
                {
                    double a = v[i - 1], b = v[i];
                    if (double.IsNaN(a) || double.IsNaN(b)) break;
                    if (rising ? a <= from && b > from : a >= from && b < from)
                    {
                        t10 = (i - 1 + (from - a) / (b - a)) * dt;
                        break;
                    }
                }

                for (int i = Math.Max(1, c); i < v.Length; i++)
                {
                    double a = v[i - 1], b = v[i];
                    if (double.IsNaN(a) || double.IsNaN(b)) break;
                    if (rising ? a < to && b >= to : a > to && b <= to)
                    {
                        t90 = (i - 1 + (to - a) / (b - a)) * dt;
                        break;
                    }
                }

                if (double.IsNaN(t10) || double.IsNaN(t90) || t90 < t10) continue;
                total += t90 - t10;
                edges++;
            }

            return edges > 0 ? total / edges : double.NaN;
        }
    }

    /// <summary>
    /// Scope probe at the tip: the input resistance and capacitance that load the probed
    /// net (1× ≈ 1 MΩ ∥ 110 pF with the cable, 10× ≈ 10 MΩ ∥ 12 pF). The node can be moved
    /// while the simulation runs; the capacitance starts charged to the node voltage.
    /// </summary>
    public sealed class ScopeProbeLoad : AnalogDevice
    {
        private int _node = -1;
        private bool _fresh = true;
        private double _vPrev, _iPrev, _geq, _ieq;

        public ScopeProbeLoad(string id) : base(id)
        {
        }

        public int Node
        {
            get => _node;
            set
            {
                if (_node == value) return;
                _node = value;
                _fresh = true;
                PendingDiscontinuity = true;
            }
        }

        public double Resistance { get; set; } = 10e6;
        public double Capacitance { get; set; } = 12e-12;

        public void SetProbe(double attenuation)
        {
            Resistance = attenuation >= 5 ? 10e6 : 1e6;
            Capacitance = attenuation >= 5 ? 12e-12 : 110e-12;
            PendingDiscontinuity = true;
        }

        public override void Reset()
        {
            _fresh = true;
            _vPrev = _iPrev = 0.0;
        }

        public override void BeginStep(StepInfo step)
        {
            if (step.IsDc || _fresh || _node <= 0)
            {
                _geq = _ieq = 0.0;
                return;
            }

            if (step.Trapezoidal)
            {
                _geq = 2.0 * Capacitance / step.Dt;
                _ieq = -_geq * _vPrev - _iPrev;
            }
            else
            {
                _geq = Capacitance / step.Dt;
                _ieq = -_geq * _vPrev;
            }
        }

        public override void Stamp(StampContext ctx)
        {
            if (_node <= 0) return;
            ctx.AddConductance(_node, 0, 1.0 / Resistance);
            if (_geq > 0.0)
            {
                ctx.AddConductance(_node, 0, _geq);
                ctx.AddCurrent(_node, 0, _ieq);
            }
        }

        public override void Accept(AcceptContext ctx)
        {
            if (_node <= 0) return;
            double v = ctx.V(_node);
            if (_fresh || ctx.IsDc)
            {
                _vPrev = v;
                _iPrev = 0.0;
                _fresh = false;
                return;
            }

            _iPrev = _geq * v + _ieq;
            _vPrev = v;
        }
    }
}
