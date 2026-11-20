using System;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [linkage] Servo horn → pushrod → control horn, as built in a model aircraft. The surface's
    /// deflection follows the real four-bar geometry (not a fixed gain: the ratio changes with
    /// angle), and the aerodynamic hinge moment comes back through the rod to the servo: it sags
    /// under load, draws more current, and is blown back once the load exceeds its stall torque.
    /// Wing parameters: "control.horn" (servo arm hole radius, m, default 10 mm), "control.arm"
    /// (control horn height, m, default 12 mm), "control.rod" (pushrod length, m, default 0.3),
    /// "control.torque" (servo stall torque, N·m, default 0.176 = SG90 at 4.8 V). Present when any of
    /// them is set or "control.linkage" = "pushrod".
    /// </summary>
    public sealed partial class WorldPhysics
    {
        /// <summary>Pushrod linkage state of one wing's control.</summary>
        public sealed class PushrodLinkage
        {
            public double HornRadius = 0.010;
            public double ControlArm = 0.012;
            public double RodLength = 0.3;
            public double StallTorque = 0.176;

            /// <summary>Servo proportional band: full stall torque at this angle error (rad).</summary>
            public double Band = 10 * Math.PI / 180;

            /// <summary>Servo horn angle now (rad, 0 = neutral), after load sag and slew.</summary>
            public double Horn;

            /// <summary>Control horn (surface) angle now (rad).</summary>
            public double Surface;

            /// <summary>dSurface/dHorn at the current position (the linkage's mechanical ratio).</summary>
            public double Ratio = 1;

            /// <summary>Torque the air puts on the servo through the rod (N·m, + = toward +horn).</summary>
            public double ServoLoad;

            /// <summary>Force along the pushrod (N, + = tension).</summary>
            public double RodForce;

            /// <summary>Commanded horn angle (rad).</summary>
            public double Command;

            /// <summary>The servo is saturated: the air holds the horn further from the command than its proportional band (full torque and still losing).</summary>
            public bool BlownBack => Math.Abs(Command - Horn) > Band || Math.Abs(ServoLoad) > StallTorque;

            /// <summary>
            /// Surface angle for a horn angle: servo pivot at the origin, horn hole at r(sin θs, cos θs);
            /// control pivot one rod length aft with its horn the same height up at neutral. Solves
            /// |hole_c − hole_s| = rod for θc (Newton), starting from the small-angle ratio r/a.
            /// </summary>
            public double SurfaceFor(double horn)
            {
                double r = HornRadius, a = ControlArm, d = RodLength;
                double sx = r * Math.Sin(horn), sy = r * Math.Cos(horn);
                double th = horn * r / a;
                for (int i = 0; i < 30; i++)
                {
                    double cx = d + a * Math.Sin(th), cy = r - a + a * Math.Cos(th);
                    double ex = cx - sx, ey = cy - sy;
                    double f = ex * ex + ey * ey - d * d;
                    double df = 2 * ex * a * Math.Cos(th) - 2 * ey * a * Math.Sin(th);
                    if (Math.Abs(df) < 1e-15) break;
                    double step = f / df;
                    th -= Math.Max(-0.3, Math.Min(0.3, step));
                    if (Math.Abs(step) < 1e-12) break;
                }

                return th;
            }

            /// <summary>
            /// One step: the horn slews toward the commanded angle (degrees from neutral) at the
            /// servo's speed, slowed and offset by the load; <paramref name="hinge"/> is the hinge
            /// moment on the surface (N·m, + = toward +surface).
            /// </summary>
            public void Step(double commandDeg, double hinge, double speedDegPerSec, double dt)
            {
                ServoLoad = hinge * Ratio;   // virtual work: τ_servo·dθs = M_hinge·dθc
                RodForce = hinge / Math.Max(1e-6, ControlArm * Math.Cos(Surface));
                double k = StallTorque / Band;
                double sag = Math.Max(-Math.PI / 4, Math.Min(Math.PI / 4, ServoLoad / k));   // held position under load
                Command = commandDeg * Math.PI / 180;
                double target = Command + sag;
                double margin = Math.Max(0, 1 - Math.Abs(ServoLoad) / StallTorque);
                double maxStep = speedDegPerSec * Math.PI / 180 * dt * Math.Max(0.05, margin);
                if (Math.Abs(ServoLoad) > StallTorque) maxStep = Math.Max(maxStep, Math.Abs(target - Horn));   // the air wins: the horn goes where it is pushed
                Horn += Math.Max(-maxStep, Math.Min(maxStep, target - Horn));
                Surface = SurfaceFor(Horn);
                const double h = 1e-4;
                Ratio = (SurfaceFor(Horn + h) - SurfaceFor(Horn - h)) / (2 * h);
            }

            /// <summary>Servo supply current for this load (A): idle plus the share of stall current the torque needs.</summary>
            public double Current(double idle, double stall) => idle + (stall - idle) * Math.Min(1, Math.Abs(ServoLoad) / StallTorque);
        }

        /// <summary>The wing's control is driven through a pushrod linkage.</summary>
        public static bool HasLinkage(WorldObject o) => IsWing(o) && LinkageOf(o) != null;

        private static PushrodLinkage? LinkageOf(WorldObject o)
        {
            bool on = o.Parameters.TryGetValue("control.linkage", out var l) && l.Trim().Equals("pushrod", StringComparison.OrdinalIgnoreCase)
                      || o.Parameters.ContainsKey("control.horn") || o.Parameters.ContainsKey("control.arm") || o.Parameters.ContainsKey("control.rod");
            if (!on) return null;
            return new PushrodLinkage
            {
                HornRadius = Math.Max(0.002, Param(o, "control.horn", 0.010)),
                ControlArm = Math.Max(0.002, Param(o, "control.arm", 0.012)),
                RodLength = Math.Max(0.02, Param(o, "control.rod", 0.3)),
                StallTorque = Math.Max(0.01, Param(o, "control.torque", 0.176))
            };
        }

        /// <summary>The pushrod linkage of a wing object (null when its control is not linked by a rod).</summary>
        public PushrodLinkage? LinkageOf(string wingId)
        {
            foreach (var w in _wings)
                if (string.Equals(w.Item.Object.Id, wingId, StringComparison.OrdinalIgnoreCase)) return w.Linkage;
            return null;
        }
    }
}
