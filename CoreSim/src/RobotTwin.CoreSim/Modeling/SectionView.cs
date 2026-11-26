using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>[section] The plane a section view cuts along: across one of the world axes.</summary>
    public enum SectionAxis
    {
        X,
        Y,
        Z
    }

    /// <summary>[section] Where the section view cuts: an axis, how far along it (mm, world) and which side stays.</summary>
    public sealed class SectionPlane
    {
        public SectionAxis Axis { get; set; } = SectionAxis.X;

        /// <summary>The plane's place along the axis (mm, world).</summary>
        public double Offset { get; set; }

        /// <summary>Keep the other side (the side the axis points to).</summary>
        public bool Flip { get; set; }

        public Vec3 AxisVector => Axis switch { SectionAxis.X => Vec3.UnitX, SectionAxis.Y => Vec3.UnitY, _ => Vec3.UnitZ };

        /// <summary>The removed side's direction: what is in front of the plane along it is cut away.</summary>
        public Vec3 Normal => Flip ? -AxisVector : AxisVector;

        public Vec3 Point => AxisVector * Offset;
    }

    /// <summary>
    /// [section] SolidWorks' Section View and Fusion's Section Analysis for the part editor: every object cut by a world
    /// plane, the side in front of it taken away and the cut filled, so the inside (walls, holes, ribs) shows; the cut
    /// faces are returned apart so they can be drawn in their own colour. Display only: the objects are not changed.
    /// </summary>
    public static class SectionView
    {
        /// <summary>
        /// An object's mesh (its own coordinates) cut by the plane given in world coordinates (its placement
        /// <paramref name="world"/>): what stays, and which of its faces are the cut. An object wholly in front is empty.
        /// </summary>
        public static (PolyMesh Mesh, HashSet<int> Caps) Cut(PolyMesh local, Affine world, SectionPlane plane)
        {
            // the plane in the object's own coordinates: a point and two directions along it, carried back
            var n = plane.Normal;
            var p = plane.Point;
            var t1 = n.AnyPerpendicular();
            var t2 = n.Cross(t1);
            var inv = world.Inverse();
            var lp = inv.Apply(p);
            var ln = inv.ApplyVector(t1).Cross(inv.ApplyVector(t2));
            if (ln.Dot(inv.ApplyVector(n)) < 0) ln = -ln;
            ln = ln.Normalized();
            var caps = new HashSet<int>();
            if (local.IsEmpty || ln.LengthSquared < 1e-12) return (local.Clone(), caps);

            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var v in local.Vertices)
            {
                double d = (v - lp).Dot(ln);
                lo = Math.Min(lo, d);
                hi = Math.Max(hi, d);
            }

            if (hi <= 0) return (local.Clone(), caps);   // wholly behind: untouched
            if (lo >= 0) return (new PolyMesh(), caps);    // wholly in front: gone

            var cut = local.Clone();
            MeshCut.Bisect(cut, lp, ln, null, BisectMode.ClearPositive, fill: true);
            double size = Math.Max(1e-9, hi - lo);
            double tol = size * 1e-7 + 1e-9;
            for (int f = 0; f < cut.Faces.Count; f++)
            {
                if (cut.Faces[f].All(i => Math.Abs((cut.Vertices[i] - lp).Dot(ln)) <= tol)) caps.Add(f);
            }

            return (cut, caps);
        }

        /// <summary>The span (mm, world) the document's visible objects cover along an axis: the slider's range.</summary>
        public static (double Min, double Max) Range(ModelDocument doc, SectionAxis axis)
        {
            var a = new SectionPlane { Axis = axis }.AxisVector;
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var o in doc.Objects.Where(o => o.Visible))
            {
                var w = doc.WorldMatrix(o);
                foreach (var v in o.LocalMesh().Vertices)
                {
                    double d = w.Apply(v).Dot(a);
                    lo = Math.Min(lo, d);
                    hi = Math.Max(hi, d);
                }
            }

            return lo > hi ? (-50, 50) : (lo, hi);
        }
    }
}
