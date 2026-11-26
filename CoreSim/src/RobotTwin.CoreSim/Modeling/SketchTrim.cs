using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>
    /// [sketch-trim] SolidWorks' Power Trim and Extend for sketch lines. Trim takes away the piece of a line between the
    /// crossings on either side of the click (the whole line when nothing crosses it); a cut end is held on the entity that
    /// cut it. Extend runs a line's free end on to the first entity it meets and holds it there. A line's length
    /// dimension goes when it is trimmed (it measured a line that is no longer there). Refused, untouched, when the sketch
    /// would contradict itself.
    /// </summary>
    public static class SketchTrim
    {
        private const double Eps = 1e-7;

        /// <summary>Where a line (as a + t·(b − a)) meets the other entities: (t, the entity), t on the line's own span or beyond.</summary>
        private static List<(double T, SketchEntity By)> Crossings(ConstrainedSketch s, SketchEntity line)
        {
            var a = s.Point(line.Start)!;
            var b = s.Point(line.End)!;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            var hits = new List<(double, SketchEntity)>();
            foreach (var e in s.Entities)
            {
                if (e.Id == line.Id) continue;
                if (e.Kind == SketchEntityKind.Line)
                {
                    var c = s.Point(e.Start)!;
                    var d = s.Point(e.End)!;
                    double ex = d.X - c.X, ey = d.Y - c.Y, den = dx * ey - dy * ex;
                    if (Math.Abs(den) < 1e-12) continue;   // parallel
                    double t = ((c.X - a.X) * ey - (c.Y - a.Y) * ex) / den;
                    double u = ((c.X - a.X) * dy - (c.Y - a.Y) * dx) / den;
                    if (u < -Eps || u > 1 + Eps) continue;   // past the cutter's own ends
                    hits.Add((t, e));
                }
                else
                {
                    var o = s.Point(e.Center)!;
                    double r = e.Kind == SketchEntityKind.Arc ? Math.Sqrt(Sq(s.Point(e.Start)!.X - o.X) + Sq(s.Point(e.Start)!.Y - o.Y)) : e.Radius;
                    double fx = a.X - o.X, fy = a.Y - o.Y, qa = dx * dx + dy * dy, qb = 2 * (fx * dx + fy * dy), qc = fx * fx + fy * fy - r * r;
                    double disc = qb * qb - 4 * qa * qc;
                    if (disc < 0 || qa < 1e-18) continue;
                    foreach (double t in new[] { (-qb - Math.Sqrt(disc)) / (2 * qa), (-qb + Math.Sqrt(disc)) / (2 * qa) })
                    {
                        if (e.Kind == SketchEntityKind.Arc && !OnArc(s, e, a.X + dx * t, a.Y + dy * t)) continue;
                        hits.Add((t, e));
                    }
                }
            }

            return hits;
        }

        private static double Sq(double v) => v * v;

        /// <summary>A point on an arc's circle lies within the arc (counter-clockwise from its start to its end).</summary>
        private static bool OnArc(ConstrainedSketch s, SketchEntity arc, double x, double y)
        {
            var o = s.Point(arc.Center)!;
            double a0 = Math.Atan2(s.Point(arc.Start)!.Y - o.Y, s.Point(arc.Start)!.X - o.X);
            double a1 = Math.Atan2(s.Point(arc.End)!.Y - o.Y, s.Point(arc.End)!.X - o.X);
            double at = Math.Atan2(y - o.Y, x - o.X);
            double span = (a1 - a0 + 4 * Math.PI) % (2 * Math.PI), from = (at - a0 + 4 * Math.PI) % (2 * Math.PI);
            return from <= span + 1e-9;
        }

        private static void HoldOn(ConstrainedSketch s, SketchPoint p, SketchEntity by) =>
            s.Constrain(by.Kind == SketchEntityKind.Line ? SketchConstraintKind.PointOnLine : SketchConstraintKind.PointOnCircle, p.Id, by.Id);

        private static bool Used(ConstrainedSketch s, string pointId) =>
            s.Entities.Any(e => e.Start == pointId || e.End == pointId || e.Center == pointId);

        public static string? Trim(ConstrainedSketch sketch, string lineId, double clickX, double clickY)
        {
            var line = sketch.Entity(lineId);
            if (line == null || line.Kind != SketchEntityKind.Line) return "Budamak için bir çizgiye tıklayın.";
            var before = sketch.Clone();
            var a = sketch.Point(line.Start)!;
            var b = sketch.Point(line.End)!;
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            if (len2 < 1e-18) return "Çizgi sıfır uzunlukta.";
            double tc = ((clickX - a.X) * dx + (clickY - a.Y) * dy) / len2;
            var cuts = Crossings(sketch, line).Where(h => h.T > Eps && h.T < 1 - Eps).OrderBy(h => h.T).ToList();
            var lower = cuts.LastOrDefault(h => h.T < tc);
            var upper = cuts.FirstOrDefault(h => h.T > tc);
            bool hasLower = lower.By != null, hasUpper = upper.By != null;
            if (!hasLower && !hasUpper)
            {
                sketch.Remove(line.Id);   // nothing crosses it: the whole line goes, and its ends unless another entity uses them
                foreach (var id in new[] { line.Start!, line.End! })
                    if (!Used(sketch, id)) sketch.Remove(id);
                return Check(sketch, before);
            }

            // its length dimensions measured a line that will not be there
            sketch.Constraints.RemoveAll(k => k.Kind == SketchConstraintKind.Length && k.A == line.Id);
            var directional = sketch.Constraints.Where(k => k.A == line.Id && k.B == null && k.Kind is SketchConstraintKind.Horizontal or SketchConstraintKind.Vertical).Select(k => k.Kind).ToList();
            string oldStart = line.Start!, oldEnd = line.End!;
            if (hasLower && hasUpper)
            {
                // the middle goes: the line keeps its start, a new line takes its far end
                var p = sketch.AddPoint(a.X + dx * lower.T, a.Y + dy * lower.T);
                var q = sketch.AddPoint(a.X + dx * upper.T, a.Y + dy * upper.T);
                line.End = p.Id;
                var rest = sketch.AddLine(q, b, line.Construction);
                foreach (var k in directional) sketch.Constrain(k, rest.Id);
                HoldOn(sketch, p, lower.By!);
                HoldOn(sketch, q, upper.By!);
            }
            else if (hasUpper)
            {
                // the start's piece goes
                var q = sketch.AddPoint(a.X + dx * upper.T, a.Y + dy * upper.T);
                line.Start = q.Id;
                HoldOn(sketch, q, upper.By!);
                if (!Used(sketch, oldStart)) sketch.Remove(oldStart);
            }
            else
            {
                var p = sketch.AddPoint(a.X + dx * lower.T, a.Y + dy * lower.T);
                line.End = p.Id;
                HoldOn(sketch, p, lower.By!);
                if (!Used(sketch, oldEnd)) sketch.Remove(oldEnd);
            }

            return Check(sketch, before);
        }

        /// <summary>Runs the line's end nearer the click on to the first entity beyond it.</summary>
        public static string? Extend(ConstrainedSketch sketch, string lineId, double clickX, double clickY)
        {
            var line = sketch.Entity(lineId);
            if (line == null || line.Kind != SketchEntityKind.Line) return "Uzatmak için bir çizgiye tıklayın.";
            var before = sketch.Clone();
            var a = sketch.Point(line.Start)!;
            var b = sketch.Point(line.End)!;
            bool atEnd = Sq(clickX - b.X) + Sq(clickY - b.Y) <= Sq(clickX - a.X) + Sq(clickY - a.Y);
            var end = atEnd ? b : a;
            if (sketch.Entities.Count(e => e.Start == end.Id || e.End == end.Id || e.Center == end.Id) > 1) return "Bu uç başka bir öğeye bağlı; uzatılamaz.";
            var hits = Crossings(sketch, line);
            var next = atEnd
                ? hits.Where(h => h.T > 1 + Eps).OrderBy(h => h.T).FirstOrDefault()
                : hits.Where(h => h.T < -Eps).OrderByDescending(h => h.T).FirstOrDefault();
            if (next.By == null) return "Çizginin önünde uzanacağı bir öğe yok.";
            double dx = b.X - a.X, dy = b.Y - a.Y;
            end.X = a.X + dx * next.T;
            end.Y = a.Y + dy * next.T;
            sketch.Constraints.RemoveAll(k => k.Kind == SketchConstraintKind.Length && k.A == line.Id);
            HoldOn(sketch, end, next.By);
            return Check(sketch, before);
        }

        private static string? Check(ConstrainedSketch sketch, ConstrainedSketch before)
        {
            if (sketch.Solve(explain: false).State != SketchState.Conflicting) return null;
            sketch.Points = before.Points;
            sketch.Entities = before.Entities;
            sketch.Constraints = before.Constraints;
            return "Bu değişiklikle ölçüler çelişiyor.";
        }
    }
}
