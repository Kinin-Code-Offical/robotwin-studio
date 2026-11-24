using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Robotics
{
    /// <summary>
    /// [pid] A motor's speed loop tried on a copy of the world, headless, with the whole physics: what the motor turns
    /// (a wheel, the car it drives, a fan) is its load. The motor's terminals get a bench voltage (its torque–speed line at
    /// that voltage), as a step for identification or from a PID every control period. Units are the firmware's: the
    /// command in PWM counts (0–255 of the supply), the speed in rpm of the output shaft.
    /// </summary>
    public sealed class MotorBench
    {
        private readonly WorldDocument _world;
        private readonly IWorldGeometry? _geometry;

        public MotorBench(WorldDocument world, IWorldGeometry? geometry, string motorId, double supplyVolts)
        {
            _world = world;
            _geometry = geometry;
            MotorId = motorId;
            Supply = Math.Max(0.1, supplyVolts);
        }

        public string MotorId { get; }

        /// <summary>The supply a PWM of 255 puts on the motor (V).</summary>
        public double Supply { get; }

        private const double Rpm = 60 / (2 * Math.PI);

        private WorldRunner Start()
        {
            var r = new WorldRunner(_world.Clone(), new RunnerOptions { Firmware = false }, _geometry);
            if (r.Physics == null) throw new InvalidOperationException("Bu dünyada fizik yok.");
            return r;
        }

        private void Command(WorldRunner r, double pwm) =>
            r.SetActuator(MotorId, new JsonObject { ["mode"] = "voltage", ["value"] = Math.Max(-255, Math.Min(255, pwm)) / 255.0 * Supply });

        private double Speed(WorldRunner r) => (r.PortState(MotorId)?.Speed ?? 0) * Rpm;

        /// <summary>
        /// Open-loop step from standstill to <paramref name="pwm"/>, sampled every <paramref name="sample"/> s until the speed
        /// has settled (no more than 0.5 % change over the last fifth of the record) or <paramref name="maxSeconds"/>.
        /// </summary>
        public List<(double T, double Y)> StepTest(double pwm, double maxSeconds = 4, double sample = 0.002)
        {
            using var r = Start();
            var list = new List<(double T, double Y)> { (0, Speed(r)) };
            Command(r, pwm);
            double t = 0;
            while (t < maxSeconds - 1e-9)
            {
                r.Step(sample);
                t += sample;
                list.Add((t, Speed(r)));
                if (t >= 0.3 && Settled(list)) break;
            }

            return list;
        }

        private static bool Settled(List<(double T, double Y)> s)
        {
            double tEnd = s[^1].T;
            var window = s.Where(x => x.T >= tEnd * 0.8).ToList();
            if (window.Count < 10) return false;
            double hi = window.Max(x => x.Y), lo = window.Min(x => x.Y);
            return hi - lo <= 0.005 * Math.Max(1e-9, Math.Abs(s[^1].Y)) && Math.Abs(s[^1].Y) > 1e-6;
        }

        /// <summary>
        /// The closed loop in the physics: a PID (the same discrete form as <see cref="PidTuning.Simulate"/> and the copied
        /// code) reads the shaft speed every <paramref name="period"/> s and sets the PWM, for a step of the setpoint (rpm).
        /// </summary>
        public LoopResponse ClosedLoop(PidGains g, double setpoint, double duration, double period, double uMin, double uMax)
        {
            using var r = Start();
            var res = new LoopResponse();
            double integral = 0, dFilt = 0;
            double y = Speed(r), lastY = y;
            for (int step = 0; step * period <= duration + 1e-12; step++)
            {
                double t = step * period;
                y = Speed(r);
                double e = setpoint - y;
                if (g.Td > 0)
                {
                    double tf = g.Td / 10;
                    dFilt += (-(y - lastY) / period - dFilt) * (period / (tf + period));
                }

                lastY = y;
                double unclamped = g.Kc * e + (double.IsPositiveInfinity(g.Ti) ? 0 : g.Kc / g.Ti * integral) + g.Kc * g.Td * dFilt;
                double u = Math.Max(uMin, Math.Min(uMax, unclamped));
                if (!((unclamped > uMax && e > 0) || (unclamped < uMin && e < 0))) integral += e * period;
                res.Trace.Add((t, y, u));
                Command(r, u);
                r.Step(period);
            }

            res.Measure(res.Trace[0].Y, setpoint, uMin, uMax);
            return res;
        }
    }
}
