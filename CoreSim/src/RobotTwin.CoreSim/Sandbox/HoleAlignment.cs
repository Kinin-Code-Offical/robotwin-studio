using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>[holes] Two holes of two parts that are meant to line up (one runs on into the other) but are off by a little.</summary>
    public sealed class HoleMisalignment
    {
        public string A { get; internal set; } = string.Empty;
        public string B { get; internal set; } = string.Empty;

        public double DiameterAMm { get; internal set; }
        public double DiameterBMm { get; internal set; }

        /// <summary>How far the two axes stand apart (mm).</summary>
        public double DeviationMm { get; internal set; }

        /// <summary>Where the holes meet, on each one's axis (world metres): where to look.</summary>
        public Vec3 PointA { get; internal set; }

        public Vec3 PointB { get; internal set; }

        /// <summary>The two holes in the world.</summary>
        public MeshHole HoleA { get; internal set; } = null!;

        public MeshHole HoleB { get; internal set; } = null!;
    }

    /// <summary>[holes] What SolidWorks' Hole Alignment asks.</summary>
    public sealed class HoleAlignmentOptions
    {
        /// <summary>Axes further apart than this (mm) are two separate holes, not one hole out of line.</summary>
        public double MaxDeviationMm { get; set; } = 1;

        /// <summary>Holes this far apart along their axis (mm) still count as running on into each other (a washer's gap).</summary>
        public double AxialGapMm { get; set; } = 1;

        /// <summary>Only holes of these objects (by id) against everything; null: every pair.</summary>
        public ICollection<string>? Only { get; set; }
    }

    /// <summary>
    /// [holes] SolidWorks' Hole Alignment for an assembly or world: the holes of every part (round walls found in their
    /// meshes by <see cref="HoleFinder"/>, put in their poses), and every pair of holes on two parts that run on into each
    /// other along one axis but whose axes stand apart by more than nothing and at most the asked deviation: the bolt that
    /// would not go through. Lined-up holes and holes far apart are left out.
    /// </summary>
    public static class HoleAlignment
    {
        private static readonly double ParallelCos = Math.Cos(2 * Math.PI / 180);
        private const double LinedUpM = 1e-6;   // a micrometre: lined up

        /// <summary>An object's holes, in the world.</summary>
        public static List<MeshHole> HolesOf(WorldObject o, IWorldGeometry geometry)
        {
            if (o.Kind == WorldObjectKind.Empty || geometry.MeshOf(o) is not { } mesh) return new List<MeshHole>();
            return HoleFinder.Find(mesh).Select(h => h.Transformed(o.Pose.ToWorld)).ToList();
        }

        public static List<HoleMisalignment> Find(WorldDocument world, IWorldGeometry geometry, HoleAlignmentOptions? options = null)
        {
            options ??= new HoleAlignmentOptions();
            double max = Math.Max(0, options.MaxDeviationMm) / 1000, axialGap = Math.Max(0, options.AxialGapMm) / 1000;
            var holes = world.Objects.Select(o => (Id: o.Id, Holes: HolesOf(o, geometry))).Where(x => x.Holes.Count > 0).ToList();
            var result = new List<HoleMisalignment>();
            for (int i = 0; i < holes.Count; i++)
            for (int j = i + 1; j < holes.Count; j++)
            {
                var (ida, ha) = holes[i];
                var (idb, hb) = holes[j];
                if (options.Only != null && !options.Only.Contains(ida) && !options.Only.Contains(idb)) continue;
                foreach (var a in ha)
                foreach (var b in hb)
                {
                    if (Math.Abs(a.Axis.Dot(b.Axis)) < ParallelCos) continue;
                    var w = b.Center - a.Center;
                    double along = w.Dot(a.Axis);
                    double lateral = (w - a.Axis * along).Length;
                    if (lateral <= LinedUpM || lateral > max) continue;

                    // along A's axis: A spans ±depth/2, B spans along ± its depth/2; they must meet or nearly
                    double a0 = -a.Depth / 2, a1 = a.Depth / 2, b0 = along - b.Depth / 2, b1 = along + b.Depth / 2;
                    double gap = Math.Max(b0 - a1, a0 - b1);
                    if (gap > axialGap) continue;
                    double s = gap > 0 ? (b0 > a1 ? (a1 + b0) / 2 : (a0 + b1) / 2) : (Math.Max(a0, b0) + Math.Min(a1, b1)) / 2;
                    result.Add(new HoleMisalignment
                    {
                        A = ida, B = idb, HoleA = a, HoleB = b,
                        DiameterAMm = a.Diameter * 1000, DiameterBMm = b.Diameter * 1000, DeviationMm = lateral * 1000,
                        PointA = a.Center + a.Axis * s,
                        PointB = b.Center + a.Axis * (s - along)
                    });
                }
            }

            return result.OrderByDescending(r => r.DeviationMm).ToList();
        }
    }
}
