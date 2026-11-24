using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Robotics
{
    /// <summary>
    /// [pid] A first-order-plus-dead-time model of a process, y(s)/u(s) = K·e^(−θs)/(τs + 1): what a step test on a motor's
    /// speed, a heater or a level gives, and what the classic tuning rules are written for.
    /// </summary>
    public readonly struct Fopdt
    {
        public Fopdt(double gain, double timeConstant, double deadTime)
        {
            Gain = gain;
            TimeConstant = Math.Max(1e-6, timeConstant);
            DeadTime = Math.Max(0, deadTime);
        }

        /// <summary>K: output change per input change at steady state (e.g. rad/s per volt).</summary>
        public double Gain { get; }

        /// <summary>τ (s): how fast it follows once it moves.</summary>
        public double TimeConstant { get; }

        /// <summary>θ (s): how long before it starts to move.</summary>
        public double DeadTime { get; }

        /// <summary>θ / τ: below 0.1 easy to control, above 1 dead time dominates.</summary>
        public double Controllability => DeadTime / TimeConstant;

        /// <summary>The model's response to a step of <paramref name="du"/> at t = 0, from 0.</summary>
        public double Step(double t, double du) => t <= DeadTime ? 0 : Gain * du * (1 - Math.Exp(-(t - DeadTime) / TimeConstant));

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "K={0:G4} τ={1:G4} s θ={2:G4} s", Gain, TimeConstant, DeadTime);
    }

    /// <summary>[pid] PID gains in the ideal (ISA) form u = Kc·(e + 1/Ti·∫e + Td·de/dt), and the same as Kp, Ki, Kd.</summary>
    public readonly struct PidGains
    {
        public PidGains(double kc, double ti, double td)
        {
            Kc = kc;
            Ti = ti <= 0 ? double.PositiveInfinity : ti;
            Td = Math.Max(0, td);
        }

        public double Kc { get; }

        /// <summary>Integral time (s); +∞ = no integral action.</summary>
        public double Ti { get; }

        /// <summary>Derivative time (s); 0 = no derivative action.</summary>
        public double Td { get; }

        public double Kp => Kc;
        public double Ki => double.IsPositiveInfinity(Ti) ? 0 : Kc / Ti;
        public double Kd => Kc * Td;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "Kp={0:G4} Ki={1:G4} Kd={2:G4}", Kp, Ki, Kd);
    }

    /// <summary>[pid] One tuning rule's answer for a model.</summary>
    public sealed class PidRule
    {
        public PidRule(string name, string note, PidGains gains)
        {
            Name = name;
            Note = note;
            Gains = gains;
        }

        public string Name { get; }

        /// <summary>What it is for, in Turkish.</summary>
        public string Note { get; }

        public PidGains Gains { get; }
    }

    /// <summary>[pid] How a closed loop answers a setpoint step: the trace and the figures people tune by.</summary>
    public sealed class LoopResponse
    {
        public List<(double T, double Y, double U)> Trace { get; } = new List<(double, double, double)>();

        /// <summary>10–90 % rise time (s); NaN when it never gets there.</summary>
        public double RiseTime { get; internal set; } = double.NaN;

        /// <summary>Overshoot past the setpoint (% of the step).</summary>
        public double Overshoot { get; internal set; }

        /// <summary>Time after which it stays within 2 % of the step (s); NaN when it does not settle.</summary>
        public double SettlingTime { get; internal set; } = double.NaN;

        /// <summary>Error at the end (% of the step).</summary>
        public double SteadyError { get; internal set; }

        /// <summary>Integral of the absolute error (unit·s).</summary>
        public double Iae { get; internal set; }

        /// <summary>Share of the time the output was pinned at a limit (0–1).</summary>
        public double Saturated { get; internal set; }

        /// <summary>Fills the figures from <see cref="Trace"/> for a step from <paramref name="start"/> to <paramref name="setpoint"/>.</summary>
        public void Measure(double start, double setpoint, double uMin, double uMax)
        {
            double step = setpoint - start;
            if (Trace.Count < 2 || Math.Abs(step) < 1e-12) return;
            double s = Math.Sign(step), a = Math.Abs(step);
            double t10 = double.NaN, t90 = double.NaN, peak = 0, iae = 0, pinned = 0;
            for (int i = 0; i < Trace.Count; i++)
            {
                var (t, y, u) = Trace[i];
                double f = (y - start) * s / a;
                if (double.IsNaN(t10) && f >= 0.1) t10 = t;
                if (double.IsNaN(t90) && f >= 0.9) t90 = t;
                peak = Math.Max(peak, f);
                if (i > 0)
                {
                    double dt = t - Trace[i - 1].T;
                    iae += Math.Abs(setpoint - y) * dt;
                    if (u >= uMax - 1e-9 || u <= uMin + 1e-9) pinned += dt;
                }
            }

            RiseTime = double.IsNaN(t10) || double.IsNaN(t90) ? double.NaN : t90 - t10;
            Overshoot = Math.Max(0, (peak - 1) * 100);
            double settle = double.NaN;
            for (int i = Trace.Count - 1; i >= 0; i--)
            {
                if (Math.Abs(Trace[i].Y - setpoint) > 0.02 * a)
                {
                    settle = i == Trace.Count - 1 ? double.NaN : Trace[i + 1].T;
                    break;
                }

                if (i == 0) settle = Trace[0].T;
            }

            SettlingTime = settle;
            SteadyError = (setpoint - Trace[^1].Y) / a * 100;
            Iae = iae;
            Saturated = pinned / Math.Max(1e-12, Trace[^1].T - Trace[0].T);
        }
    }

    /// <summary>
    /// [pid] PID tuning from a step test: identify a first-order-plus-dead-time model (Smith's two-point method),
    /// compute gains by the classic rules (SIMC, IMC, Ziegler–Nichols reaction curve, Cohen–Coon), and simulate the
    /// closed loop with output limits and anti-windup to compare them before they go into code.
    /// </summary>
    public static class PidTuning
    {
        /// <summary>
        /// Smith's two-point method: from an open-loop step of the input by <paramref name="du"/> at t = 0, the times the
        /// output reaches 28.3 % and 63.2 % of its change give τ = 1.5·(t63 − t28), θ = t63 − τ; K = Δy / Δu. Null when the
        /// output does not settle into a clear change (an integrating or oscillating process).
        /// </summary>
        public static Fopdt? Identify(IReadOnlyList<(double T, double Y)> samples, double du)
        {
            if (samples.Count < 8 || Math.Abs(du) < 1e-12) return null;
            double y0 = samples[0].Y;
            // The final value: the mean of the last tenth of the record.
            int tail = Math.Max(2, samples.Count / 10);
            double yEnd = samples.Skip(samples.Count - tail).Average(s => s.Y);
            double dy = yEnd - y0;
            if (Math.Abs(dy) < 1e-9) return null;
            // Still rising at the end: not settled (or integrating).
            double lastSlope = (samples[^1].Y - samples[samples.Count - tail].Y) / Math.Max(1e-12, samples[^1].T - samples[samples.Count - tail].T);
            double span = samples[^1].T - samples[0].T;
            if (Math.Abs(lastSlope) * span > 0.1 * Math.Abs(dy)) return null;
            double t28 = Crossing(samples, y0 + 0.283 * dy, Math.Sign(dy));
            double t63 = Crossing(samples, y0 + 0.632 * dy, Math.Sign(dy));
            if (double.IsNaN(t28) || double.IsNaN(t63) || t63 <= t28) return null;
            double tau = 1.5 * (t63 - t28);
            double theta = Math.Max(0, t63 - tau - samples[0].T);
            return new Fopdt(dy / du, tau, theta);
        }

        /// <summary>The first time the samples cross <paramref name="level"/> going the <paramref name="sign"/> way (linear between samples).</summary>
        private static double Crossing(IReadOnlyList<(double T, double Y)> s, double level, int sign)
        {
            for (int i = 1; i < s.Count; i++)
            {
                double a = (s[i - 1].Y - level) * sign, b = (s[i].Y - level) * sign;
                if (a < 0 && b >= 0)
                {
                    double f = a / (a - b);
                    return s[i - 1].T + f * (s[i].T - s[i - 1].T);
                }
            }

            return double.NaN;
        }

        /// <summary>
        /// Gains by the classic rules. <paramref name="closedLoopTime"/> is SIMC's and IMC's tuning knob τc (λ): the closed
        /// loop's time constant; smaller is faster and less robust. Default τc = θ (Skogestad's "tight" choice), at least a
        /// tenth of τ when there is hardly any dead time.
        /// </summary>
        public static List<PidRule> Rules(Fopdt m, double? closedLoopTime = null)
        {
            double k = m.Gain, tau = m.TimeConstant, th = m.DeadTime;
            double tc = closedLoopTime ?? Math.Max(th, 0.1 * tau);
            double thz = Math.Max(th, 1e-3 * tau);   // the reaction-curve rules divide by θ
            var list = new List<PidRule>
            {
                // Skogestad (2003), SIMC PI: Kc = τ / (K(τc + θ)), Ti = min(τ, 4(τc + θ)).
                new PidRule("SIMC (PI)", "Sağlam ve hızlı; çoğu motor hız döngüsü için ilk seçim", new PidGains(tau / (k * (tc + th)), Math.Min(tau, 4 * (tc + th)), 0)),
                // Rivera, Morari, Skogestad (1986), IMC PID with a Padé dead time: λ = τc.
                new PidRule("IMC (PID)", "Ölü zamanı belirgin süreçler için; λ ile yumuşatılır", new PidGains((2 * tau + th) / (k * (2 * tc + th)), tau + th / 2, tau * th / (2 * tau + th))),
                // Ziegler & Nichols (1942), reaction curve: Kc = 1.2τ/(Kθ), Ti = 2θ, Td = θ/2.
                new PidRule("Ziegler–Nichols (PID)", "Klasik, agresif: çeyrek genlik sönümü, belirgin aşma", new PidGains(1.2 * tau / (k * thz), 2 * thz, 0.5 * thz)),
                // Cohen & Coon (1953), PID.
                new PidRule("Cohen–Coon (PID)", "Ölü zaman / zaman sabiti büyükken; aşması yüksek", new PidGains(
                    tau / (k * thz) * (4.0 / 3 + thz / (4 * tau)),
                    thz * (32 + 6 * thz / tau) / (13 + 8 * thz / tau),
                    thz * 4 / (11 + 2 * thz / tau))),
            };
            return list;
        }

        /// <summary>
        /// Closed loop of a PID on the model for a setpoint step from 0 to <paramref name="setpoint"/>: a discrete PID every
        /// <paramref name="period"/> (as firmware runs it), derivative on the measurement filtered (N = 10), output limited to
        /// [<paramref name="uMin"/>, <paramref name="uMax"/>] with the integral held while it pushes against a limit.
        /// </summary>
        public static LoopResponse Simulate(Fopdt plant, PidGains g, double setpoint, double duration, double period, double uMin, double uMax)
        {
            var r = new LoopResponse();
            double h = Math.Min(period, Math.Max(1e-4, plant.TimeConstant / 50));
            int sub = Math.Max(1, (int)Math.Round(period / h));
            h = period / sub;
            // The dead time as a delay line on the input, at the integration step.
            int delay = (int)Math.Round(plant.DeadTime / h);
            var line = new Queue<double>(Enumerable.Repeat(0.0, delay));
            double y = 0, integral = 0, dFilt = 0, lastY = 0, u = 0;
            double nFilter = 10;
            for (int step = 0; step * period <= duration + 1e-12; step++)
            {
                double t = step * period;
                // The controller samples and updates its output.
                double e = setpoint - y;
                double kc = g.Kc;
                double p = kc * e;
                if (g.Td > 0)
                {
                    // dFilt follows −dy/dt through a first-order filter of time Td/N.
                    double tf = g.Td / nFilter;
                    double raw = -(y - lastY) / period;
                    dFilt += (raw - dFilt) * (period / (tf + period));
                }

                lastY = y;
                double d = kc * g.Td * dFilt;
                double i = double.IsPositiveInfinity(g.Ti) ? 0 : kc / g.Ti * integral;
                double unclamped = p + i + d;
                u = Math.Max(uMin, Math.Min(uMax, unclamped));
                // Conditional integration (anti-windup): no integrating further into a limit.
                bool pushing = (unclamped > uMax && e > 0) || (unclamped < uMin && e < 0);
                if (!pushing) integral += e * period;
                r.Trace.Add((t, y, u));
                // The plant until the next sample.
                for (int k = 0; k < sub; k++)
                {
                    double uin;
                    if (delay > 0)
                    {
                        line.Enqueue(u);
                        uin = line.Dequeue();
                    }
                    else
                    {
                        uin = u;
                    }

                    y += (plant.Gain * uin - y) * (1 - Math.Exp(-h / plant.TimeConstant));
                }
            }

            r.Measure(0, setpoint, uMin, uMax);
            return r;
        }

        /// <summary>The gains as Arduino code: a PID loop at <paramref name="period"/> with the same limits and anti-windup.</summary>
        public static string ArduinoCode(PidGains g, double period, double uMin, double uMax, string rule)
        {
            string F(double v) => double.IsInfinity(v) || double.IsNaN(v) ? "0" : v.ToString("G5", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.AppendLine($"// RobotWin Studio PID ayarı ({rule}): Kc={F(g.Kc)}, Ti={(double.IsPositiveInfinity(g.Ti) ? "∞" : F(g.Ti))} s, Td={F(g.Td)} s");
            sb.AppendLine($"const float KP = {F(g.Kp)}f, KI = {F(g.Ki)}f, KD = {F(g.Kd)}f;");
            sb.AppendLine($"const float DT = {F(period)}f;              // s: loop() bu aralıkla çalışmalı");
            sb.AppendLine($"const float U_MIN = {F(uMin)}f, U_MAX = {F(uMax)}f;");
            sb.AppendLine("float integral = 0, lastY = 0;");
            sb.AppendLine();
            sb.AppendLine("float pidStep(float setpoint, float y) {");
            sb.AppendLine("  float e = setpoint - y;");
            sb.AppendLine("  float d = -(y - lastY) / DT;          // ölçümün türevi: ayar noktası sıçramasında tekme yok");
            sb.AppendLine("  lastY = y;");
            sb.AppendLine("  float u = KP * e + KI * integral + KD * d;");
            sb.AppendLine("  float out = constrain(u, U_MIN, U_MAX);");
            sb.AppendLine("  if (!((u > U_MAX && e > 0) || (u < U_MIN && e < 0))) integral += e * DT;   // anti-windup");
            sb.AppendLine("  return out;");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
