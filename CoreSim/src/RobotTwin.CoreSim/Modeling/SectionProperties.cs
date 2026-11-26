using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>[section-props] A plane section's properties (mm): what SolidWorks' Section Properties gives for a cut.</summary>
    public sealed class SectionResult
    {
        /// <summary>The section's two in-plane axes' names ("Y", "Z" for a cut across X).</summary>
        public string U { get; set; } = "X";
        public string V { get; set; } = "Y";

        public double Area { get; set; }
        public double Perimeter { get; set; }

        /// <summary>The centroid in the plane's U, V (mm, world).</summary>
        public double CentroidU { get; set; }
        public double CentroidV { get; set; }

        /// <summary>Second moments about axes through the centroid parallel to U and V (mm⁴): I_U = ∫ v² dA, I_V = ∫ u² dA.</summary>
        public double Iu { get; set; }
        public double Iv { get; set; }
        public double Iuv { get; set; }

        /// <summary>The principal moments (largest first) and the first principal axis' angle from U (degrees).</summary>
        public double I1 { get; set; }
        public double I2 { get; set; }
        public double PrincipalAngle { get; set; }

        /// <summary>The polar moment about the centroid (I_U + I_V).</summary>
        public double J => Iu + Iv;

        /// <summary>Radii of gyration about the centroidal axes (mm).</summary>
        public double Ru => Area > 0 ? Math.Sqrt(Iu / Area) : 0;
        public double Rv => Area > 0 ? Math.Sqrt(Iv / Area) : 0;

        /// <summary>Elastic section moduli (mm³): I over the farthest fibre from each centroidal axis.</summary>
        public double Wu { get; set; }
        public double Wv { get; set; }
    }

    /// <summary>
    /// [section-props] Area integrals of plane regions by Green's theorem over their outlines: area, first and second
    /// moments, centroid, principal axes. A region is given as polygons that tile it (holes simply not covered), each
    /// turned counter-clockwise first, so faces from any mesh cut add up exactly.
    /// </summary>
    public static class SectionProperties
    {
        public static SectionResult Of(IEnumerable<IReadOnlyList<Vec2>> polygons)
        {
            double a = 0, qu = 0, qv = 0, iuu = 0, ivv = 0, iuv = 0;   // about the origin: ∫dA, ∫u, ∫v, ∫v², ∫u², ∫uv
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            var list = polygons.Where(p => p.Count >= 3).ToList();
            foreach (var poly in list)
            {
                double signed = 0;
                for (int i = 0; i < poly.Count; i++)
                {
                    var p = poly[i];
                    var q = poly[(i + 1) % poly.Count];
                    signed += p.X * q.Y - q.X * p.Y;
                }

                double s = signed < 0 ? -1 : 1;   // every piece counter-clockwise
                for (int i = 0; i < poly.Count; i++)
                {
                    var p = poly[i];
                    var q = poly[(i + 1) % poly.Count];
                    double c = (p.X * q.Y - q.X * p.Y) * s;
                    a += c / 2;
                    qu += (p.X + q.X) * c / 6;
                    qv += (p.Y + q.Y) * c / 6;
                    ivv += (p.X * p.X + p.X * q.X + q.X * q.X) * c / 12;
                    iuu += (p.Y * p.Y + p.Y * q.Y + q.Y * q.Y) * c / 12;
                    iuv += (p.X * q.Y + 2 * p.X * p.Y + 2 * q.X * q.Y + q.X * p.Y) * c / 24;
                    minU = Math.Min(minU, p.X);
                    maxU = Math.Max(maxU, p.X);
                    minV = Math.Min(minV, p.Y);
                    maxV = Math.Max(maxV, p.Y);
                }
            }

            var r = new SectionResult { Area = a, Perimeter = Perimeter(list) };
            if (a <= 1e-12) return r;
            double cu = qu / a, cv = qv / a;
            r.CentroidU = cu;
            r.CentroidV = cv;
            // to the centroid (parallel axes)
            r.Iu = iuu - a * cv * cv;
            r.Iv = ivv - a * cu * cu;
            r.Iuv = iuv - a * cu * cv;
            double mean = (r.Iu + r.Iv) / 2, half = Math.Sqrt((r.Iu - r.Iv) * (r.Iu - r.Iv) / 4 + r.Iuv * r.Iuv);
            r.I1 = mean + half;
            r.I2 = mean - half;
            r.PrincipalAngle = 0.5 * Math.Atan2(-2 * r.Iuv, r.Iu - r.Iv) * 180 / Math.PI;
            double cvMax = Math.Max(maxV - cv, cv - minV), cuMax = Math.Max(maxU - cu, cu - minU);
            r.Wu = cvMax > 0 ? r.Iu / cvMax : 0;
            r.Wv = cuMax > 0 ? r.Iv / cuMax : 0;
            return r;
        }

        /// <summary>The outline's length: the edges no two pieces share (a seam between two pieces is inside).</summary>
        private static double Perimeter(List<IReadOnlyList<Vec2>> polys)
        {
            static (long, long) Key(Vec2 v) => ((long)Math.Round(v.X * 1e6), (long)Math.Round(v.Y * 1e6));
            var count = new Dictionary<((long, long), (long, long)), (int N, double L)>();
            foreach (var poly in polys)
                for (int i = 0; i < poly.Count; i++)
                {
                    var p = poly[i];
                    var q = poly[(i + 1) % poly.Count];
                    var (kp, kq) = (Key(p), Key(q));
                    var k = kp.CompareTo(kq) < 0 ? (kp, kq) : (kq, kp);
                    double len = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                    count[k] = count.TryGetValue(k, out var e) ? (e.N + 1, e.L) : (1, len);
                }

            return count.Values.Where(e => e.N == 1).Sum(e => e.L);
        }

        /// <summary>A profile's section: its outer loop less its holes (a sketch's loops: the largest outer, the others inside it).</summary>
        public static SectionResult OfProfile(IReadOnlyList<Vec2> outer, IEnumerable<IReadOnlyList<Vec2>> holes)
        {
            var whole = Of(new[] { outer });
            var cut = holes.Where(h => h.Count >= 3).ToList();
            if (cut.Count == 0) return whole;
            // the holes' integrals taken off the outer's, about the origin, then to the new centroid
            var parts = new[] { (Of(new[] { outer }), 1.0) }.Concat(cut.Select(h => (Of(new[] { h }), -1.0))).ToList();
            double a = 0, qu = 0, qv = 0, iuu = 0, ivv = 0, iuv = 0;
            foreach (var (p, s) in parts)
            {
                a += s * p.Area;
                qu += s * p.Area * p.CentroidU;
                qv += s * p.Area * p.CentroidV;
                iuu += s * (p.Iu + p.Area * p.CentroidV * p.CentroidV);
                ivv += s * (p.Iv + p.Area * p.CentroidU * p.CentroidU);
                iuv += s * (p.Iuv + p.Area * p.CentroidU * p.CentroidV);
            }

            var r = new SectionResult { Area = a, Perimeter = whole.Perimeter + parts.Skip(1).Sum(x => x.Item1.Perimeter) };
            if (a <= 1e-12) return r;
            r.CentroidU = qu / a;
            r.CentroidV = qv / a;
            r.Iu = iuu - a * r.CentroidV * r.CentroidV;
            r.Iv = ivv - a * r.CentroidU * r.CentroidU;
            r.Iuv = iuv - a * r.CentroidU * r.CentroidV;
            double mean = (r.Iu + r.Iv) / 2, half = Math.Sqrt((r.Iu - r.Iv) * (r.Iu - r.Iv) / 4 + r.Iuv * r.Iuv);
            r.I1 = mean + half;
            r.I2 = mean - half;
            r.PrincipalAngle = 0.5 * Math.Atan2(-2 * r.Iuv, r.Iu - r.Iv) * 180 / Math.PI;
            double minU = outer.Min(v => v.X), maxU = outer.Max(v => v.X), minV = outer.Min(v => v.Y), maxV = outer.Max(v => v.Y);
            double cvMax = Math.Max(maxV - r.CentroidV, r.CentroidV - minV), cuMax = Math.Max(maxU - r.CentroidU, r.CentroidU - minU);
            r.Wu = cvMax > 0 ? r.Iu / cvMax : 0;
            r.Wv = cuMax > 0 ? r.Iv / cuMax : 0;
            return r;
        }

        /// <summary>
        /// The section of the document's visible objects at a section plane: the cut faces of each, in the plane's two
        /// world axes (a cut across X is measured in Y and Z). Null when the plane meets nothing.
        /// </summary>
        public static SectionResult? AtPlane(ModelDocument doc, SectionPlane plane, IEnumerable<ModelObject>? only = null)
        {
            var (ua, va, un, vn) = plane.Axis switch
            {
                SectionAxis.X => (Vec3.UnitY, Vec3.UnitZ, "Y", "Z"),
                SectionAxis.Y => (Vec3.UnitX, Vec3.UnitZ, "X", "Z"),
                _ => (Vec3.UnitX, Vec3.UnitY, "X", "Y")
            };
            var polys = new List<IReadOnlyList<Vec2>>();
            foreach (var o in (only ?? doc.Objects).Where(o => o.Visible))
            {
                var world = doc.WorldMatrix(o);
                var (cut, caps) = SectionView.Cut(o.LocalMesh(), world, plane);
                foreach (int f in caps)
                    polys.Add(cut.Faces[f].Select(i => world.Apply(cut.Vertices[i])).Select(w => new Vec2(w.Dot(ua), w.Dot(va))).ToList());
            }

            if (polys.Count == 0) return null;
            var r = Of(polys);
            r.U = un;
            r.V = vn;
            return r.Area > 1e-9 ? r : null;
        }
    }
}
