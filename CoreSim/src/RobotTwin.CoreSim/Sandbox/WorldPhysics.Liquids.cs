using System;
using System.Collections.Generic;
using System.Globalization;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    public sealed partial class WorldPhysics
    {
        /// <summary>[liquid] A liquid object and its particles.</summary>
        public sealed class LiquidItem
        {
            public LiquidItem(WorldObject o, LiquidBody body)
            {
                Object = o;
                Body = body;
            }

            public WorldObject Object { get; }
            public LiquidBody Body { get; }

            /// <summary>Physics time not yet given to the liquid (it runs on its own, longer step when slow).</summary>
            internal double Pending;
        }

        private readonly List<LiquidItem> _liquids = new List<LiquidItem>();

        public IReadOnlyList<LiquidItem> Liquids => _liquids;

        /// <summary>[liquid] How finely liquids are solved.</summary>
        public sealed class LiquidQuality
        {
            /// <summary>Particles per liquid whose spacing is not set (more = finer surface and flow).</summary>
            public int Particles { get; set; } = 3000;

            /// <summary>Pressure iterations per substep (more = less compression).</summary>
            public int Iterations { get; set; } = 4;

            /// <summary>Longest liquid (sub)step (s): 1 ms live (a resting column stays within 1 % of its density), the physics step in a render.</summary>
            public double MaxStep { get; set; } = 1e-3;

            /// <summary>Liquid at rest stops being stepped until something moves.</summary>
            public bool Sleep { get; set; } = true;

            /// <summary>Iterate the pressure until no particle is compressed by more than this (0 = a fixed number of iterations).</summary>
            public double CompressionTolerance { get; set; }

            /// <summary>The live view: interactive.</summary>
            public static LiquidQuality Live => new LiquidQuality();
        }

        public LiquidQuality Liquid { get; set; } = new LiquidQuality();

        /// <summary>Particles a liquid is made of when its spacing is not set (more = finer surface, slower).</summary>
        public int LiquidParticles { get => Liquid.Particles; set => Liquid.Particles = value; }

        /// <summary>
        /// [liquid] A body with a "liquid" parameter (water, oil, honey …, see <see cref="LiquidKind"/>) is not a rigid part:
        /// its box or cylinder is the volume the liquid fills when the world starts, less whatever solid stands in it.
        /// </summary>
        public static bool IsLiquid(WorldObject o) =>
            o.Kind == WorldObjectKind.Body && o.Parameters.TryGetValue("liquid", out var v) && !string.IsNullOrWhiteSpace(v);

        /// <summary>[tube] Wall thickness of a hollow cylinder (m), 0 = solid.</summary>
        public static double TubeWall(WorldObject o) => Param(o, "tube.wall", 0);

        /// <summary>[tube] Thickness of a hollow cylinder's closed bottom (m), 0 = open at both ends.</summary>
        public static double TubeBottom(WorldObject o) => Param(o, "tube.bottom", 0);

        /// <summary>Volume (m³) a liquid object starts with: its box or upright cylinder.</summary>
        public static double LiquidVolume(WorldObject o) =>
            o.Shape is "cylinder" or "cylinder-z" ? Math.PI / 4 * o.SizeX * o.SizeY * o.SizeZ : o.SizeX * o.SizeY * o.SizeZ;

        private void BuildLiquids(WorldDocument world, Func<WorldObject, WorldBodyGeometry> geometry)
        {
            if (Liquid.Particles <= 0) return;   // a scene built only to lay cables out
            foreach (var o in world.Objects)
            {
                if (!IsLiquid(o)) continue;
                var kind = LiquidKind.Find(o.Parameters["liquid"]);
                double volume = LiquidVolume(o);
                if (volume <= 0) continue;
                double spacing = o.Parameters.TryGetValue("liquid.spacing", out var sp) && double.TryParse(sp, NumberStyles.Float, CultureInfo.InvariantCulture, out double sv) && sv > 0
                    ? sv
                    : Math.Pow(volume / Math.Max(100, LiquidParticles), 1.0 / 3);
                spacing = Math.Max(0.0012, Math.Min(0.03, spacing));
                // A whole number of layers fills an upright volume to its top: the lattice holds the whole height, so the
                // liquid has its full mass (a floored count lost up to half a layer at each end).
                if (Math.Abs(o.Pose.Orientation.Rotate(Vec3.UnitZ).Z) > 0.999) spacing = o.SizeZ / Math.Max(1, Math.Round(o.SizeZ / spacing));
                var liquid = new LiquidBody(o.Id, kind, spacing);
                // The volume from the object's own size (its origin is the bottom centre, as for every primitive body),
                // never from a drawn model: the liquid must start exactly where it was placed.
                var centre = new Vec3(0, 0, o.SizeZ / 2);
                var half = new Vec3(o.SizeX / 2, o.SizeY / 2, o.SizeZ / 2);
                bool round = o.Shape is "cylinder" or "cylinder-z";
                var q = o.Pose.Orientation;
                var origin = o.Pose.Position + q.Rotate(centre);
                // World box around the (possibly turned) volume.
                double ex = 0, ey = 0, ez = 0;
                foreach (var axis in new[] { new Vec3(half.X, 0, 0), new Vec3(0, half.Y, 0), new Vec3(0, 0, half.Z) })
                {
                    var w = q.Rotate(axis);
                    ex += Math.Abs(w.X);
                    ey += Math.Abs(w.Y);
                    ez += Math.Abs(w.Z);
                }

                var lo = origin - new Vec3(ex, ey, ez);
                var hi = origin + new Vec3(ex, ey, ez);
                // Every lattice cell whose centre lies in the liquid's volume is a particle, so particles × spacing³ is the
                // real volume (the cells against a wall included); only centres inside a solid are left out.
                double clear = 0;
                var points = LiquidBody.Lattice(lo, hi, spacing, p =>
                {
                    var l = q.InverseRotate(p - origin);
                    if (Math.Abs(l.Z) > half.Z) return false;
                    if (round)
                    {
                        double rx = l.X / Math.Max(half.X, 1e-9), ry = l.Y / Math.Max(half.Y, 1e-9);
                        if (rx * rx + ry * ry > 1) return false;
                    }
                    else if (Math.Abs(l.X) > half.X || Math.Abs(l.Y) > half.Y) return false;

                    // Not inside a solid (the glass it was poured into, a stone lying in it).
                    if (Scene.GroundEnabled && p.Z < Scene.GroundHeight(p.X, p.Y)) return false;
                    foreach (var b in Scene.Bodies)
                    {
                        if (!b.Collides) continue;
                        var shape = b.EffectiveShape;
                        if (shape == null || !shape.HasSolid) continue;
                        if (shape.Kind != ShapeKind.Mesh && (p - b.Position).Length > shape.BoundingRadius + clear) continue;
                        var local = b.Orientation.InverseRotate(p - b.Position);
                        if (b.SignedDistanceSolid(shape, local, 1e-9, out double d, out _) && d < clear) return false;
                    }

                    return true;
                });
                liquid.Add(points);
                liquid.Iterations = Liquid.Iterations;
                liquid.MaxStep = Liquid.MaxStep;
                liquid.SleepEnabled = Liquid.Sleep;
                liquid.CompressionTolerance = Liquid.CompressionTolerance;
                if (o.Parameters.TryGetValue("liquid.iterations", out var its) && int.TryParse(its, out int iv)) liquid.Iterations = Math.Max(1, Math.Min(20, iv));
                liquid.SetTotalMass(kind.Density * volume);
                liquid.Settle(Scene);   // at rest, at the real density, before the world starts
                _liquids.Add(new LiquidItem(o, liquid));
            }
        }

        private void StepLiquids(double dt)
        {
            foreach (var l in _liquids)
            {
                l.Pending += dt;
                if (l.Pending >= l.Body.SuggestedStep - 1e-12)
                {
                    l.Body.Step(Scene, l.Pending);
                    l.Pending = 0;
                }

                l.Body.ApplyForces();   // the liquid's push acts on every physics step, between its own steps too
            }
        }
    }
}
