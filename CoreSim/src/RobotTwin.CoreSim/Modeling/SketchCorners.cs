using System;
using System.Linq;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>
    /// [sketch-fillet] SolidWorks' Sketch Fillet and Sketch Chamfer on the corner where two lines meet: the lines are cut
    /// back and joined by an arc tangent to both (its radius a dimension) or by a straight edge (its two setbacks
    /// dimensions). The corner stays as a "virtual sharp": a point held on both lines' extensions, so every dimension and
    /// relation that held the corner still holds the shape. Refused, with the sketch untouched, when the corner is not two
    /// lines or the cut would pass a line's other end.
    /// </summary>
    public static class SketchCorners
    {
        public static string? Fillet(ConstrainedSketch sketch, string cornerPoint, double radius) => Cut(sketch, cornerPoint, radius, chamfer: false);

        public static string? Chamfer(ConstrainedSketch sketch, string cornerPoint, double setback) => Cut(sketch, cornerPoint, setback, chamfer: true);

        /// <summary>Null when it was done, else why not (the sketch is left as it was).</summary>
        private static string? Cut(ConstrainedSketch sketch, string cornerPoint, double size, bool chamfer)
        {
            if (!(size > 0)) return chamfer ? "Pah uzunluğu sıfırdan büyük olmalı." : "Yarıçap sıfırdan büyük olmalı.";
            var p = sketch.Point(cornerPoint);
            if (p == null) return "Köşe noktası bulunamadı.";
            var lines = sketch.Entities.Where(e => e.Kind == SketchEntityKind.Line && !e.Construction && (e.Start == p.Id || e.End == p.Id)).ToList();
            if (lines.Count != 2) return "Köşede tam iki çizgi birleşmeli.";

            var before = sketch.Clone();
            SketchPoint Far(SketchEntity l) => sketch.Point(l.Start == p.Id ? l.End : l.Start)!;
            var q1 = Far(lines[0]);
            var q2 = Far(lines[1]);
            double l1 = Math.Sqrt((q1.X - p.X) * (q1.X - p.X) + (q1.Y - p.Y) * (q1.Y - p.Y));
            double l2 = Math.Sqrt((q2.X - p.X) * (q2.X - p.X) + (q2.Y - p.Y) * (q2.Y - p.Y));
            if (l1 < 1e-9 || l2 < 1e-9) return "Köşedeki çizgilerden biri sıfır uzunlukta.";
            double u1x = (q1.X - p.X) / l1, u1y = (q1.Y - p.Y) / l1, u2x = (q2.X - p.X) / l2, u2y = (q2.Y - p.Y) / l2;
            double cos = Math.Max(-1, Math.Min(1, u1x * u2x + u1y * u2y));
            double theta = Math.Acos(cos);
            if (theta < 1e-6 || Math.PI - theta < 1e-6) return "Çizgiler aynı doğrultuda: köşe yok.";

            double t = chamfer ? size : size / Math.Tan(theta / 2);   // how far back each line is cut
            if (t >= l1 - 1e-9 || t >= l2 - 1e-9) return chamfer ? "Pah çizgilerden birinden uzun." : "Bu yarıçap için çizgiler kısa.";

            var t1 = sketch.AddPoint(p.X + u1x * t, p.Y + u1y * t);
            var t2 = sketch.AddPoint(p.X + u2x * t, p.Y + u2y * t);
            void Retarget(SketchEntity l, SketchPoint to)
            {
                if (l.Start == p.Id) l.Start = to.Id;
                else l.End = to.Id;
            }

            Retarget(lines[0], t1);
            Retarget(lines[1], t2);
            // a line's length was measured to the corner: it still is, now to the virtual sharp (as SolidWorks does)
            foreach (var (line, far) in new[] { (lines[0], q1), (lines[1], q2) })
                foreach (var k in sketch.Constraints.Where(k => k.Kind == SketchConstraintKind.Length && k.A == line.Id).ToList())
                {
                    k.Kind = SketchConstraintKind.Distance;
                    k.A = far.Id;
                    k.B = p.Id;
                }
            // the virtual sharp: the old corner stays where the two lines would meet
            sketch.Constrain(SketchConstraintKind.PointOnLine, p.Id, lines[0].Id);
            sketch.Constrain(SketchConstraintKind.PointOnLine, p.Id, lines[1].Id);

            if (chamfer)
            {
                sketch.AddLine(t1, t2);
                sketch.Constrain(SketchConstraintKind.Distance, p.Id, t1.Id, size);
                sketch.Constrain(SketchConstraintKind.Distance, p.Id, t2.Id, size);
            }
            else
            {
                double bx = u1x + u2x, by = u1y + u2y, bl = Math.Sqrt(bx * bx + by * by), d = size / Math.Sin(theta / 2);
                var c = sketch.AddPoint(p.X + bx / bl * d, p.Y + by / bl * d);
                // an arc runs counter-clockwise from its start to its end
                double cross = (t1.X - c.X) * (t2.Y - c.Y) - (t1.Y - c.Y) * (t2.X - c.X);
                var arc = cross > 0 ? sketch.AddArc(c, t1, t2) : sketch.AddArc(c, t2, t1);
                sketch.Constrain(SketchConstraintKind.Tangent, lines[0].Id, arc.Id);
                sketch.Constrain(SketchConstraintKind.Tangent, lines[1].Id, arc.Id);
                sketch.Constrain(SketchConstraintKind.Radius, arc.Id, null, size);
            }

            if (sketch.Solve(explain: false).State == SketchState.Conflicting)
            {
                sketch.Points = before.Points;
                sketch.Entities = before.Entities;
                sketch.Constraints = before.Constraints;
                return "Köşe kesilince ölçüler çelişiyor.";
            }

            return null;
        }
    }
}
