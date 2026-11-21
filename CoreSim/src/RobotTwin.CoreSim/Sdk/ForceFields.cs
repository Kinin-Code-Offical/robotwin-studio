using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sdk.Expressions;

namespace RobotTwin.CoreSim.Sdk
{
    /// <summary>Drag areas and volumes of collision shapes.</summary>
    public static class Aerodynamics
    {
        /// <summary>Flat-faced box (each face, flow normal to it).</summary>
        public const double CdBox = 1.05;

        /// <summary>Sphere, subcritical Reynolds number (10³–2·10⁵).</summary>
        public const double CdSphere = 0.47;

        /// <summary>Circular cylinder in cross-flow.</summary>
        public const double CdCylinderSide = 1.0;

        /// <summary>Short cylinder, flow along its axis (blunt end).</summary>
        public const double CdCylinderEnd = 0.9;

        /// <summary>Hulls and meshes: their bounding box with a rounded-body coefficient.</summary>
        public const double CdHull = 0.8;

        /// <summary>
        /// Drag coefficient × projected area along the body x, y, z axes (m²) — what
        /// <see cref="RigidBody.DragArea"/> holds. The drag of a flow from an oblique direction is
        /// interpolated by the direction cosines (|ux|·CdAx + |uy|·CdAy + |uz|·CdAz).
        /// </summary>
        public static Vec3 DragArea(CollisionShape? shape, double? cdOverride = null, double? areaOverride = null)
        {
            if (areaOverride is double a)
            {
                double c = cdOverride ?? 1.0;
                return new Vec3(1, 1, 1) * (Math.Max(0, a) * c);
            }

            if (shape == null) return Vec3.Zero;
            switch (shape.Kind)
            {
                case ShapeKind.Sphere:
                {
                    double area = Math.PI * shape.Radius * shape.Radius;
                    return new Vec3(1, 1, 1) * (area * (cdOverride ?? CdSphere));
                }

                case ShapeKind.Cylinder:
                {
                    double r = shape.Radius, h = shape.HalfHeight * 2;
                    double side = 2 * r * h * (cdOverride ?? CdCylinderSide), end = Math.PI * r * r * (cdOverride ?? CdCylinderEnd);
                    return new Vec3(side, side, end);
                }

                case ShapeKind.Box:
                {
                    var hx = shape.HalfExtents;
                    double cd = cdOverride ?? CdBox;
                    return new Vec3(4 * hx.Y * hx.Z, 4 * hx.X * hx.Z, 4 * hx.X * hx.Y) * cd;
                }

                default:
                {
                    var v = shape.Vertices;
                    if (v == null || v.Count == 0) return Vec3.Zero;
                    var min = v[0];
                    var max = v[0];
                    foreach (var p in v)
                    {
                        min = Vec3.Min(min, p);
                        max = Vec3.Max(max, p);
                    }

                    var s = max - min;
                    return new Vec3(s.Y * s.Z, s.X * s.Z, s.X * s.Y) * (cdOverride ?? CdHull);
                }
            }
        }

        /// <summary>Quadratic drag on a body for a relative air velocity (N): −½ρ·CdA(dir)·|v|·v.</summary>
        public static Vec3 Drag(RigidBody body, Vec3 relativeAirVelocity, double density)
        {
            double sp = relativeAirVelocity.Length;
            if (sp < 1e-12) return Vec3.Zero;
            var lb = body.Orientation.InverseRotate(relativeAirVelocity) / sp;
            double cda = body.DragAreaAlong(lb);
            return relativeAirVelocity * (-0.5 * density * cda * sp);
        }
    }

    /// <summary>Geometry helpers of region fields.</summary>
    internal static class FieldGeometry
    {
        public static Vec3 Local(ForceFieldSpec f, Vec3 p) => f.Orientation.InverseRotate(p - f.Center);

        public static bool Inside(ForceFieldSpec f, Vec3 p)
        {
            var l = Local(f, p);
            return Math.Abs(l.X) <= f.SizeX / 2 && Math.Abs(l.Y) <= f.SizeY / 2 && Math.Abs(l.Z) <= f.SizeZ / 2;
        }

        private static double Smooth(double e0, double e1, double x)
        {
            double u = Math.Max(0, Math.Min(1, (x - e0) / (e1 - e0)));
            return u * u * (3 - 2 * u);
        }

        /// <summary>Fan / air stream: speed decays like a free jet beyond ~6 outlet diameters; soft lateral edges.</summary>
        public static Vec3 Fan(ForceFieldSpec f, Vec3 p)
        {
            var l = Local(f, p);
            var half = f.Size * 0.5;
            if (Math.Abs(l.X) > half.X || Math.Abs(l.Y) > half.Y || Math.Abs(l.Z) > half.Z) return Vec3.Zero;
            var dir = f.Direction;
            if (dir.LengthSquared < 0.5) return Vec3.Zero;
            var dl = f.Orientation.InverseRotate(dir);
            double halfAlong = Math.Abs(dl.X) * half.X + Math.Abs(dl.Y) * half.Y + Math.Abs(dl.Z) * half.Z;
            double s = Math.Max(0, l.Dot(dl) + halfAlong);
            double crossArea = f.SizeX * f.SizeY * f.SizeZ / Math.Max(1e-6, 2 * halfAlong);
            double d = Math.Sqrt(4 * crossArea / Math.PI);
            double speed = f.Strength / (1 + s / Math.Max(1e-3, 6 * d));
            double soft = 1;
            soft *= 1 - (1 - Math.Abs(dl.X)) * Smooth(0.7, 1.0, Math.Abs(l.X) / Math.Max(1e-9, half.X));
            soft *= 1 - (1 - Math.Abs(dl.Y)) * Smooth(0.7, 1.0, Math.Abs(l.Y) / Math.Max(1e-9, half.Y));
            soft *= 1 - (1 - Math.Abs(dl.Z)) * Smooth(0.7, 1.0, Math.Abs(l.Z) / Math.Max(1e-9, half.Z));
            return dir * (speed * soft);
        }

        /// <summary>Rankine vortex about the field axis: tangential speed S·r/R inside the core, S·R/r outside; optional inflow and updraft.</summary>
        public static Vec3 Vortex(ForceFieldSpec f, Vec3 p)
        {
            if (f.Bounded && !Inside(f, p)) return Vec3.Zero;
            var axis = f.Direction;
            if (axis.LengthSquared < 0.5) axis = Vec3.UnitZ;
            var d = p - f.Center;
            var radial = d - axis * d.Dot(axis);
            double r = radial.Length;
            if (r < 1e-9) return axis * (f.Updraft * f.Strength);
            double rc = Math.Max(1e-3, f.Radius);
            double vt = f.Strength * (r < rc ? r / rc : rc / r);
            var er = radial / r;
            var et = axis.Cross(er);
            return et * vt - er * (f.Inflow * vt) + axis * (f.Updraft * vt);
        }

        public static double Falloff(ForceFieldSpec f, double r)
        {
            double rc = Math.Max(1e-6, f.Radius);
            switch ((f.Falloff ?? "none").ToLowerInvariant())
            {
                case "linear":
                    return Math.Max(0, 1 - r / rc);
                case "inverse-square":
                {
                    double soft = 0.05 * rc;
                    return rc * rc / Math.Max(r * r, soft * soft);
                }

                case "gaussian":
                    return Math.Exp(-(r / rc) * (r / rc));
                default:
                    return r <= rc || !f.Bounded ? 1 : 0;
            }
        }
    }

    /// <summary>Variables and outputs of expression behaviours and expression fields.</summary>
    internal sealed class ExpressionForce
    {
        public static readonly string[] Inputs =
        {
            "t", "dt", "x", "y", "z", "vx", "vy", "vz", "wx", "wy", "wz", "speed", "m", "g", "x0", "y0", "z0",
            "roll", "pitch", "yaw", "rho", "tempC", "air.x", "air.y", "air.z", "airspeed", "h"
        };

        public static readonly string[] ForceOutputs = { "F.x", "F.y", "F.z", "T.x", "T.y", "T.z", "FL.x", "FL.y", "FL.z", "TL.x", "TL.y", "TL.z" };
        public static readonly string[] WindOutputs = { "wind.x", "wind.y", "wind.z" };

        private readonly ExpressionProgram _program;
        private readonly double[] _slots;
        private readonly int[] _in;
        private readonly int[] _force;
        private readonly int[] _wind;
        private readonly (string Name, int Slot)[] _locals;
        private readonly List<(int Slot, double Value)> _params = new List<(int, double)>();
        private readonly bool _field;

        /// <param name="field">Field scope: wind.* are outputs; otherwise wind.* read the local wind.</param>
        public ExpressionForce(string source, bool field, IReadOnlyDictionary<string, string>? parameters)
        {
            _field = field;
            var scope = Scope(field, parameters, out var paramNames);
            _program = ExpressionProgram.Compile(source, scope);
            var s = _program.Scope;
            _slots = s.NewSlots();
            _in = Inputs.Select(n => s[n]).ToArray();
            _force = ForceOutputs.Select(n => s[n]).ToArray();
            _wind = WindOutputs.Select(n => s[n]).ToArray();
            WritesForce = _program.Assigned.Any(a => ForceOutputs.Contains(a));
            WritesWind = field && _program.Assigned.Any(a => WindOutputs.Contains(a));
            var reserved = new HashSet<string>(Inputs.Concat(ForceOutputs).Concat(WindOutputs).Concat(paramNames), StringComparer.Ordinal);
            _locals = _program.Assigned.Where(a => !reserved.Contains(a)).Select(a => (a, s[a])).ToArray();
            if (parameters != null)
            {
                foreach (var kv in parameters)
                {
                    if (s.TryGet(kv.Key, out int slot) && BehaviorRegistry.TryParse(kv.Value, typeof(double), out var v) && v is double d) _params.Add((slot, d));
                }
            }

            if (!WritesForce && !WritesWind)
            {
                throw new ExpressionException(field
                    ? "İfade hiçbir çıktı yazmıyor: F.x/F.y/F.z (kuvvet, N), T.x/T.y/T.z (tork) veya wind.x/wind.y/wind.z (rüzgâr, m/s) atayın."
                    : "İfade hiçbir çıktı yazmıyor: F.x/F.y/F.z (kuvvet, N), FL.x… (gövde ekseninde), T.x/T.y/T.z veya TL.x… atayın.", 0, 1, 1);
            }
        }

        public bool WritesForce { get; }
        public bool WritesWind { get; }

        public static ExpressionScope Scope(bool field, IReadOnlyDictionary<string, string>? parameters, out List<string> paramNames)
        {
            var scope = new ExpressionScope();
            foreach (var n in Inputs) scope.Declare(n);
            foreach (var n in ForceOutputs) scope.Declare(n, writable: true);
            foreach (var n in WindOutputs) scope.Declare(n, writable: field);
            foreach (var c in new[] { 'x', 'y', 'z' })
            {
                scope.Alias("F" + c, "F." + c);
                scope.Alias("T" + c, "T." + c);
            }

            scope.Alias("time", "t");
            scope.Alias("mass", "m");
            paramNames = new List<string>();
            if (parameters != null)
            {
                foreach (var kv in parameters)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || scope.Contains(kv.Key)) continue;
                    if (!BehaviorRegistry.TryParse(kv.Value, typeof(double), out _)) continue;
                    scope.Declare(kv.Key);
                    paramNames.Add(kv.Key);
                }
            }

            return scope;
        }

        public IReadOnlyList<(string Name, int Slot)> Locals => _locals;

        public double LocalValue(int slot) => _slots[slot];

        private void Load(double t, double dt, ISimBody? b, Vec3 p, Vec3 air, Vec3 g, double rho, double tempC, Vec3 start)
        {
            var s = _slots;
            Array.Clear(s, 0, s.Length);
            foreach (var (slot, v) in _params) s[slot] = v;
            var v3 = b?.Velocity ?? Vec3.Zero;
            var w = b?.AngularVelocity ?? Vec3.Zero;
            var (roll, pitch, yaw) = (b?.Orientation ?? Quat.Identity).ToEuler();
            double[] values =
            {
                t, dt, p.X, p.Y, p.Z, v3.X, v3.Y, v3.Z, w.X, w.Y, w.Z, v3.Length, b?.Mass ?? 0, g.Length, start.X, start.Y, start.Z,
                roll, pitch, yaw, rho, tempC, air.X, air.Y, air.Z, (v3 - air).Length, p.Z
            };
            for (int i = 0; i < _in.Length; i++) s[_in[i]] = values[i];
            if (!_field)
            {
                // Behaviour scope: wind.* read the local wind (same as air.*).
                s[_wind[0]] = air.X;
                s[_wind[1]] = air.Y;
                s[_wind[2]] = air.Z;
            }
        }

        /// <summary>Force and torque (world) on a body.</summary>
        public (Vec3 Force, Vec3 Torque) ForceOn(ISimBody b, double t, double dt, Vec3 air, Vec3 g, double rho, double tempC, Vec3 start)
        {
            Load(t, dt, b, b.Position, air, g, rho, tempC, start);
            _program.Run(_slots);
            var s = _slots;
            var f = new Vec3(s[_force[0]], s[_force[1]], s[_force[2]]) + b.Orientation.Rotate(new Vec3(s[_force[6]], s[_force[7]], s[_force[8]]));
            var tq = new Vec3(s[_force[3]], s[_force[4]], s[_force[5]]) + b.Orientation.Rotate(new Vec3(s[_force[9]], s[_force[10]], s[_force[11]]));
            return (f, tq);
        }

        /// <summary>Added wind (m/s) at a point.</summary>
        public Vec3 WindAt(Vec3 p, double t, Vec3 baseAir, Vec3 g, double rho, double tempC)
        {
            Load(t, 0, null, p, baseAir, g, rho, tempC, p);
            _program.Run(_slots);
            return new Vec3(_slots[_wind[0]], _slots[_wind[1]], _slots[_wind[2]]);
        }

        /// <summary>Checks a program without running it; returns null when it compiles, else the Turkish error.</summary>
        public static string? Check(string source, bool field, IReadOnlyDictionary<string, string>? parameters = null)
        {
            try
            {
                _ = new ExpressionForce(source, field, parameters);
                return null;
            }
            catch (ExpressionException ex)
            {
                return ExpressionHelp.Describe(ex, source);
            }
        }
    }

    /// <summary>Public entry for the UI: validate expression programs.</summary>
    public static class ExpressionCheck
    {
        /// <summary>Null when the program is valid for an object behaviour; otherwise the error with a caret.</summary>
        public static string? ForBehavior(string source, IReadOnlyDictionary<string, string>? parameters = null) => ExpressionForce.Check(source, false, parameters);

        /// <summary>Null when the program is valid for an expression field (may write wind.*).</summary>
        public static string? ForField(string source, IReadOnlyDictionary<string, string>? parameters = null) => ExpressionForce.Check(source, true, parameters);

        public static IReadOnlyList<string> Inputs => ExpressionForce.Inputs;
        public static IReadOnlyList<string> ForceOutputs => ExpressionForce.ForceOutputs;
        public static IReadOnlyList<string> WindOutputs => ExpressionForce.WindOutputs;
    }
}
