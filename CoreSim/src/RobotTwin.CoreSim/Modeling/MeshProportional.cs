using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>[proportional] Blender's proportional editing falloffs: how a point's share of the move fades with its distance.</summary>
    public enum ProportionalFalloff
    {
        Smooth,
        Sphere,
        Root,
        Sharp,
        Linear,
        Constant
    }

    /// <summary>
    /// [proportional] Blender's proportional editing: points within a radius of the selection move with it, each by a
    /// share that fades with its distance to the nearest selected point (1 at the selection, 0 at the radius).
    /// </summary>
    public static class MeshProportional
    {
        /// <summary>The share of the move each unselected point within <paramref name="radius"/> of the selection takes.</summary>
        public static Dictionary<int, double> Weights(IReadOnlyList<Vec3> points, ICollection<int> selected, double radius, ProportionalFalloff falloff = ProportionalFalloff.Smooth)
        {
            var result = new Dictionary<int, double>();
            if (radius <= 0 || selected.Count == 0) return result;
            var sel = selected.Where(i => i >= 0 && i < points.Count).Select(i => points[i]).ToList();
            var chosen = new HashSet<int>(selected);
            for (int i = 0; i < points.Count; i++)
            {
                if (chosen.Contains(i)) continue;
                double d = double.MaxValue;
                foreach (var s in sel) d = Math.Min(d, (points[i] - s).Length);
                if (d >= radius) continue;
                double w = Falloff(1 - d / radius, falloff);
                if (w > 1e-9) result[i] = w;
            }

            return result;
        }

        /// <summary>The falloff curve at t (1 at the selection, 0 at the radius).</summary>
        public static double Falloff(double t, ProportionalFalloff falloff)
        {
            t = Math.Max(0, Math.Min(1, t));
            return falloff switch
            {
                ProportionalFalloff.Sphere => Math.Sqrt(2 * t - t * t),
                ProportionalFalloff.Root => Math.Sqrt(t),
                ProportionalFalloff.Sharp => t * t,
                ProportionalFalloff.Linear => t,
                ProportionalFalloff.Constant => 1,
                _ => 3 * t * t - 2 * t * t * t
            };
        }

        /// <summary>A point moved by its share of a transform: from where it was towards where the whole transform takes it.</summary>
        public static Vec3 Blend(Vec3 from, Vec3 to, double weight) => from + (to - from) * weight;
    }
}
