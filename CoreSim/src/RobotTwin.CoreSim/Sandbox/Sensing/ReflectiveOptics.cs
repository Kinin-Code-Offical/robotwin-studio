using System;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox.Sensing
{
    /// <summary>
    /// Optical data of a reflective IR sensor (emitter LED + phototransistor side by side, both
    /// looking at the surface). Distance response, emitter/detector spacing and beam angles set
    /// how the diffuse, the mirror-like (specular) and the background light reach the detector.
    /// </summary>
    public sealed class ReflectiveSensorSpec
    {
        public string Name { get; set; } = "TCRT5000";

        /// <summary>Distance of the strongest signal (m).</summary>
        public double PeakDistance { get; set; } = 0.0025;

        /// <summary>Log-width of the rise below the peak (closer than the peak the emitter and detector cones do not overlap).</summary>
        public double RiseWidth { get; set; } = 0.55;

        /// <summary>Fall-off beyond the peak: f = 1 / (1 + ((h − peak) / FalloffDistance)^FalloffExponent).</summary>
        public double FalloffDistance { get; set; } = 0.0045;

        public double FalloffExponent { get; set; } = 1.8;

        /// <summary>Centre spacing of emitter and detector (m).</summary>
        public double Baseline { get; set; } = 0.005;

        /// <summary>Half-intensity angle of the IR LED and half-sensitivity angle of the phototransistor (degrees).</summary>
        public double EmitterHalfAngleDeg { get; set; } = 16;

        public double DetectorHalfAngleDeg { get; set; } = 25;

        /// <summary>Collector current at the peak distance over a 90 % white card at <see cref="NominalIf"/> (A).</summary>
        public double PeakCurrent { get; set; } = 1.0e-3;

        /// <summary>LED forward current the peak current is specified at (A).</summary>
        public double NominalIf { get; set; } = 0.02;

        /// <summary>Collector dark current I_CEO (A).</summary>
        public double DarkCurrent { get; set; } = 10e-9;

        /// <summary>Signal from direct sunlight on a white floor, relative to the peak signal (daylight filter included).</summary>
        public double AmbientGain { get; set; } = 3.0;

        /// <summary>Gain of a mirrored lamp / sun seen through a glossy surface, relative to the peak signal at full sun.</summary>
        public double AmbientSpecularGain { get; set; } = 40.0;

        /// <summary>Size of the part around the optics that shades the floor from room light (m).</summary>
        public double ShadeSize { get; set; } = 0.006;

        /// <summary>Recommended working range (m) — beyond it the signal is of no use.</summary>
        public double MaxRange { get; set; } = 0.015;

        /// <summary>Relative rms noise of the photocurrent (shot, LED ripple).</summary>
        public double Noise { get; set; } = 0.006;

        /// <summary>Signal change per °C (LED efficiency falls as it warms).</summary>
        public double TempCoefficient { get; set; } = -0.005;

        /// <summary>Vishay TCRT5000: peak at 2.5 mm, 0.2–15 mm range, 950 nm, daylight-blocking filter, I_C ≈ 1 mA typical.</summary>
        public static ReflectiveSensorSpec Tcrt5000 => new ReflectiveSensorSpec();

        /// <summary>QRE1113 (Pololu QTR boards): tiny package, best around 1–3 mm, useful to ≈6 mm (analog) / 9.5 mm (RC).</summary>
        public static ReflectiveSensorSpec Qre1113 => new ReflectiveSensorSpec
        {
            Name = "QRE1113", PeakDistance = 0.0015, RiseWidth = 0.6, FalloffDistance = 0.003, FalloffExponent = 2.0, Baseline = 0.0018,
            EmitterHalfAngleDeg = 22, DetectorHalfAngleDeg = 30, PeakCurrent = 0.4e-3, NominalIf = 0.02, ShadeSize = 0.004, MaxRange = 0.0095
        };

        /// <summary>Relative response to a white diffuse surface at a distance along the axis (0–1, 1 at the peak).</summary>
        public double DistanceResponse(double h)
        {
            if (h <= 1e-5) return 0;
            if (h < PeakDistance)
            {
                double l = Math.Log(h / PeakDistance);
                return Math.Exp(-l * l / (2 * RiseWidth * RiseWidth));
            }

            return 1.0 / (1.0 + Math.Pow((h - PeakDistance) / FalloffDistance, FalloffExponent));
        }

        /// <summary>Normalised gain of a cone at an off-axis angle (Gaussian, 0.5 at the half angle).</summary>
        public static double Cone(double angleRad, double halfDeg)
        {
            double r = angleRad / (halfDeg * Math.PI / 180);
            return Math.Exp(-0.6931 * r * r);
        }
    }

    /// <summary>One evaluation of a reflective sensor against the world.</summary>
    public struct ReflectiveReading
    {
        /// <summary>Total signal relative to the peak white signal (can exceed 1 under glare / sunlight).</summary>
        public double Signal;

        public double Diffuse;
        public double Specular;
        public double Ambient;

        /// <summary>Distance along the sensor axis to the surface (m), +∞ when nothing is within range.</summary>
        public double Distance;

        /// <summary>Spot-averaged diffuse reflectance of what is under the sensor.</summary>
        public double Reflectance;

        /// <summary>Centre of the spot on the surface (world) and its radius (m).</summary>
        public Vec3 Spot;

        public double SpotRadius;
        public int Tape;
    }

    /// <summary>
    /// Reflective sensor physics: the spot the emitter lights on the surface (it grows with
    /// height), diffuse return following the datasheet distance curve, specular return when
    /// the emitter–surface–detector geometry is a mirror path (glossy tape at some heights and
    /// tilts reads white), and room light / sunlight reaching the detector directly or mirrored
    /// by a glossy surface. The floor is the plane z = 0.
    /// </summary>
    public static class ReflectiveOptics
    {
        private static readonly (double X, double Y, double W)[] SpotPattern = BuildPattern();

        private static (double, double, double)[] BuildPattern()
        {
            var p = new (double, double, double)[13];
            p[0] = (0, 0, 0.22);
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3;
                p[1 + i] = (0.45 * Math.Cos(a), 0.45 * Math.Sin(a), 0.085);
                p[7 + i] = (0.9 * Math.Cos(a + Math.PI / 6), 0.9 * Math.Sin(a + Math.PI / 6), 0.045);
            }

            return p;
        }

        /// <param name="centre">Centre of the sensor face (world).</param>
        /// <param name="axis">Viewing direction (towards the surface), unit.</param>
        /// <param name="baseline">Emitter → detector direction, unit and perpendicular to <paramref name="axis"/>.</param>
        /// <param name="obstacle">Optional surface closer than the floor: distance along the axis and its diffuse reflectance.</param>
        public static ReflectiveReading Evaluate(ReflectiveSensorSpec spec, FloorOptics floor, Vec3 centre, Vec3 axis, Vec3 baseline,
            (double Distance, double Reflectance)? obstacle = null)
        {
            var r = new ReflectiveReading { Distance = double.PositiveInfinity, Tape = -2 };
            axis = axis.Normalized();
            double cosTilt = -axis.Z;
            double t = cosTilt > 0.05 ? centre.Z / cosTilt : double.PositiveInfinity;
            double range = spec.MaxRange * 3;
            var env = floor.Environment;

            if (obstacle is { } ob && ob.Distance < t && ob.Distance < range)
            {
                // Another body (a box, a white sheet on the floor …): diffuse only.
                r.Distance = ob.Distance;
                r.Reflectance = ob.Reflectance;
                r.Spot = centre + axis * ob.Distance;
                r.SpotRadius = 0.0006 + ob.Distance * Math.Tan(spec.EmitterHalfAngleDeg * Math.PI / 180);
                r.Diffuse = ob.Reflectance / 0.9 * spec.DistanceResponse(ob.Distance);
                r.Signal = r.Diffuse;
                return r;
            }

            if (double.IsInfinity(t) || t > range || centre.Z < -1e-4)
            {
                r.Ambient = AmbientDirect(spec, env, range);
                r.Signal = r.Ambient;
                return r;
            }

            r.Distance = Math.Max(0, t);
            r.Spot = centre + axis * r.Distance;
            r.SpotRadius = 0.0006 + r.Distance * Math.Tan(spec.EmitterHalfAngleDeg * Math.PI / 180);

            // Spot average over a disc on the floor (two rings + centre).
            var u = baseline.Normalized();
            var v = axis.Cross(u).Normalized();
            double rd = 0, rs = 0, wsum = 0;
            int tapeHits = 0, centreTape = -1;
            foreach (var (px, py, w) in SpotPattern)
            {
                var p = r.Spot + u * (px * r.SpotRadius) + v * (py * r.SpotRadius);
                var s = floor.At(p.X, p.Y);
                rd += s.Diffuse * w;
                rs += s.Specular * w;
                wsum += w;
                if (s.Tape >= 0) tapeHits++;
                if (px == 0 && py == 0) centreTape = s.Tape;
            }

            rd /= wsum;
            rs /= wsum;
            r.Reflectance = rd;
            r.Tape = centreTape;
            r.Diffuse = rd / 0.9 * spec.DistanceResponse(r.Distance) * Math.Pow(Math.Max(0, cosTilt), 1.5);

            // Mirror path emitter → floor → detector.
            var e = centre - u * (spec.Baseline / 2);
            var d = centre + u * (spec.Baseline / 2);
            if (e.Z > 1e-5 && d.Z > 1e-5)
            {
                double k = e.Z / (e.Z + d.Z);
                var mirrorD = new Vec3(d.X, d.Y, -d.Z);
                var p = e + (mirrorD - e) * k;
                var toP = (p - e).Normalized();
                var toD = (d - p).Normalized();
                double thetaE = Math.Acos(Clamp(toP.Dot(axis), -1, 1));
                double thetaD = Math.Acos(Clamp((-toD).Dot(axis), -1, 1));
                double cosI = Clamp(toD.Z, 0, 1);
                var sp = floor.At(p.X, p.Y);
                double fresnel = sp.Specular + (1 - sp.Specular) * Math.Pow(1 - cosI, 5) * sp.Specular * 4;
                double path = (p - e).Length + (d - p).Length;
                double omega = 2 * Math.PI * (1 - Math.Cos(spec.EmitterHalfAngleDeg * Math.PI / 180));
                double gain = Math.PI / (4 * omega);
                r.Specular = fresnel * ReflectiveSensorSpec.Cone(thetaE, spec.EmitterHalfAngleDeg) * ReflectiveSensorSpec.Cone(thetaD, spec.DetectorHalfAngleDeg)
                    * gain * spec.DistanceResponse(Math.Max(spec.PeakDistance, path / 2));
            }

            // Room light / sun: diffusely from the floor around the spot (shaded by the part itself), and mirrored by gloss.
            if (env.AmbientIr > 0)
            {
                double shade = r.Distance / (r.Distance + spec.ShadeSize);
                double ambientDiffuse = env.AmbientIr * spec.AmbientGain * rd * shade;
                double el = env.LightElevationDeg * Math.PI / 180, az = env.LightAzimuthDeg * Math.PI / 180;
                var toLight = new Vec3(Math.Cos(el) * Math.Cos(az), Math.Cos(el) * Math.Sin(az), Math.Sin(el));
                var reflected = new Vec3(-toLight.X, -toLight.Y, toLight.Z);
                double off = Math.Acos(Clamp(reflected.Dot(-axis), -1, 1));
                double fres = rs + (1 - rs) * Math.Pow(1 - Math.Sin(el), 5) * rs * 4;
                double ambientSpec = env.AmbientIr * spec.AmbientSpecularGain * fres * ReflectiveSensorSpec.Cone(off, spec.DetectorHalfAngleDeg) * shade * shade;
                r.Ambient = ambientDiffuse + ambientSpec;
            }

            r.Signal = r.Diffuse + r.Specular + r.Ambient;
            return r;
        }

        /// <summary>Background light when the sensor looks at nothing (lifted robot): what the room reflects into it.</summary>
        private static double AmbientDirect(ReflectiveSensorSpec spec, SensingEnvironment env, double range) =>
            env.AmbientIr * spec.AmbientGain * 0.15;

        private static double Clamp(double v, double a, double b) => v < a ? a : v > b ? b : v;
    }
}
