using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>[clearance] Two parts nearer each other than the asked clearance: the gap and its two ends.</summary>
    public sealed class ClearancePair
    {
        public string A { get; internal set; } = string.Empty;
        public string B { get; internal set; } = string.Empty;

        /// <summary>The smallest gap between them (mm); 0 when they touch or overlap.</summary>
        public double GapMm { get; internal set; }

        /// <summary>The nearest point of A and of B (world metres): where to look.</summary>
        public Vec3 PointA { get; internal set; }

        public Vec3 PointB { get; internal set; }

        /// <summary>They share space (an interference, not just a small gap).</summary>
        public bool Overlapping { get; internal set; }

        /// <summary>Joined by a joint.</summary>
        public bool Joined { get; internal set; }
    }

    /// <summary>[clearance] What SolidWorks' Clearance Verification asks.</summary>
    public sealed class ClearanceOptions
    {
        /// <summary>The smallest acceptable gap (mm): pairs nearer than this are reported.</summary>
        public double MinimumMm { get; set; } = 1;

        /// <summary>Only these objects (by id) against everything; null: every pair.</summary>
        public ICollection<string>? Only { get; set; }

        /// <summary>Pairs that overlap are reported too (as gap 0); off leaves them to the interference check.</summary>
        public bool IncludeOverlapping { get; set; } = true;

        /// <summary>Pairs joined by a joint are checked too.</summary>
        public bool IncludeJoined { get; set; }
    }

    /// <summary>
    /// [clearance] SolidWorks' Clearance Verification for an assembly or world: every pair of parts closer than a
    /// minimum gap (a wire's bend radius, a fan's breathing room, a tolerance stack), with the gap and its two nearest
    /// points. Each part is its convex solid in its pose (as <see cref="Interference"/>); pairs whose boxes are farther
    /// apart than the minimum are skipped, the rest are measured exactly by <see cref="ConvexDistance"/>.
    /// </summary>
    public static class Clearance
    {
        public static List<ClearancePair> Find(WorldDocument world, IWorldGeometry geometry, ClearanceOptions? options = null)
        {
            options ??= new ClearanceOptions();
            var solids = Interference.SolidsOf(world, geometry);
            var joined = Interference.JoinedPairs(world);
            double min = Math.Max(0, options.MinimumMm) / 1000;
            var result = new List<ClearancePair>();
            for (int i = 0; i < solids.Count; i++)
            {
                for (int k = i + 1; k < solids.Count; k++)
                {
                    var a = solids[i];
                    var b = solids[k];
                    if (options.Only != null && !options.Only.Contains(a.Id) && !options.Only.Contains(b.Id)) continue;
                    bool isJoined = joined.Contains((a.Id, b.Id));
                    if (isJoined && !options.IncludeJoined) continue;
                    if (BoxGap(a, b) >= min) continue;
                    var gap = ConvexDistance.Between(a.Hull.Vertices, b.Hull.Vertices);
                    if (gap.Distance >= min) continue;
                    if (gap.Overlapping && !options.IncludeOverlapping) continue;
                    result.Add(new ClearancePair
                    {
                        A = a.Id, B = b.Id, GapMm = gap.Distance * 1000, PointA = gap.OnA, PointB = gap.OnB, Overlapping = gap.Overlapping, Joined = isJoined
                    });
                }
            }

            return result.OrderBy(r => r.GapMm).ToList();
        }

        /// <summary>The gap between two boxes (0 when they meet): no pair can be nearer than this.</summary>
        private static double BoxGap(Interference.Solid a, Interference.Solid b)
        {
            double dx = Math.Max(0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
            double dy = Math.Max(0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
            double dz = Math.Max(0, Math.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
