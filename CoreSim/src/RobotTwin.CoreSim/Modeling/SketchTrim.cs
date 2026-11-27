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
                if (e.Id == line.Id || e.Kind is SketchEntityKind.Ellipse or SketchEntityKind.Spline) continue;   // [curves] they do not cut
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
            if (line is { Kind: SketchEntityKind.Circle or SketchEntityKind.Arc }) return TrimRound(sketch, line, clickX, clickY);   // [sketch-trim] circles and arcs
            if (line == null || line.Kind != SketchEntityKind.Line) return "Budamak için bir çizgiye, daireye ya da yaya tıklayın.";
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

        /// <summary>
        /// [sketch-trim] Where the other entities cross a circle or an arc: (the angle, counter-clockwise from the arc's start
        /// or, for a circle, from +X, in 0 to 2 pi; the entity). For an arc only the crossings within it.
        /// </summary>
        private static List<(double A, SketchEntity By)> RoundCrossings(ConstrainedSketch s, SketchEntity round)
        {
            var o = s.Point(round.Center)!;
            bool arc = round.Kind == SketchEntityKind.Arc;
            double r = arc ? Math.Sqrt(Sq(s.Point(round.Start)!.X - o.X) + Sq(s.Point(round.Start)!.Y - o.Y)) : round.Radius;
            double a0 = arc ? Math.Atan2(s.Point(round.Start)!.Y - o.Y, s.Point(round.Start)!.X - o.X) : 0;
            double span = arc ? (Math.Atan2(s.Point(round.End)!.Y - o.Y, s.Point(round.End)!.X - o.X) - a0 + 4 * Math.PI) % (2 * Math.PI) : 2 * Math.PI;
            var hits = new List<(double, SketchEntity)>();
            void Hit(double x, double y, SketchEntity by)
            {
                double at = (Math.Atan2(y - o.Y, x - o.X) - a0 + 4 * Math.PI) % (2 * Math.PI);
                if (arc && at > span + 1e-9) return;
                hits.Add((at, by));
            }

            foreach (var e in s.Entities)
            {
                if (e.Id == round.Id || e.Kind is SketchEntityKind.Ellipse or SketchEntityKind.Spline) continue;   // [curves] they do not cut
                if (e.Kind == SketchEntityKind.Line)
                {
                    var a = s.Point(e.Start)!;
                    var b = s.Point(e.End)!;
                    double dx = b.X - a.X, dy = b.Y - a.Y, fx = a.X - o.X, fy = a.Y - o.Y;
                    double qa = dx * dx + dy * dy, qb = 2 * (fx * dx + fy * dy), qc = fx * fx + fy * fy - r * r, disc = qb * qb - 4 * qa * qc;
                    if (disc < 0 || qa < 1e-18) continue;
                    foreach (double t in new[] { (-qb - Math.Sqrt(disc)) / (2 * qa), (-qb + Math.Sqrt(disc)) / (2 * qa) })
                        if (t >= -Eps && t <= 1 + Eps) Hit(a.X + dx * t, a.Y + dy * t, e);
                }
                else
                {
                    var c = s.Point(e.Center)!;
                    double r2 = e.Kind == SketchEntityKind.Arc ? Math.Sqrt(Sq(s.Point(e.Start)!.X - c.X) + Sq(s.Point(e.Start)!.Y - c.Y)) : e.Radius;
                    double dx = c.X - o.X, dy = c.Y - o.Y, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < 1e-12 || d > r + r2 + 1e-12 || d < Math.Abs(r - r2) - 1e-12) continue;   // the same centre, apart, or one inside the other
                    double along = (r * r - r2 * r2 + d * d) / (2 * d), h = Math.Sqrt(Math.Max(0, r * r - along * along));
                    double mx = o.X + dx * along / d, my = o.Y + dy * along / d;
                    foreach (int sgn in h < 1e-12 ? new[] { 1 } : new[] { 1, -1 })
                    {
                        double x = mx + sgn * h * -dy / d, y = my + sgn * h * dx / d;
                        if (e.Kind == SketchEntityKind.Arc && !OnArc(s, e, x, y)) continue;
                        Hit(x, y, e);
                    }
                }
            }

            return hits;
        }

        /// <summary>
        /// [sketch-trim] Power trim on a circle or an arc: the piece between the crossings either side of the click goes. A
        /// circle needs two crossings and becomes the arc that is left; an arc is shortened from an end or split in two
        /// (the halves kept equal); an arc or a circle nothing crosses goes whole. Cut ends are held on what cut them.
        /// </summary>
        private static string? TrimRound(ConstrainedSketch sketch, SketchEntity round, double clickX, double clickY)
        {
            var before = sketch.Clone();
            var o = sketch.Point(round.Center)!;
            bool arc = round.Kind == SketchEntityKind.Arc;
            double r = arc ? Math.Sqrt(Sq(sketch.Point(round.Start)!.X - o.X) + Sq(sketch.Point(round.Start)!.Y - o.Y)) : round.Radius;
            double a0 = arc ? Math.Atan2(sketch.Point(round.Start)!.Y - o.Y, sketch.Point(round.Start)!.X - o.X) : 0;
            double span = arc ? (Math.Atan2(sketch.Point(round.End)!.Y - o.Y, sketch.Point(round.End)!.X - o.X) - a0 + 4 * Math.PI) % (2 * Math.PI) : 2 * Math.PI;
            double click = (Math.Atan2(clickY - o.Y, clickX - o.X) - a0 + 4 * Math.PI) % (2 * Math.PI);
            var cuts = RoundCrossings(sketch, round).Where(h => !arc || (h.A > Eps && h.A < span - Eps)).OrderBy(h => h.A).ToList();
            (double X, double Y) At(double a) => (o.X + r * Math.Cos(a0 + a), o.Y + r * Math.Sin(a0 + a));
            if (cuts.Count == 0)
            {
                sketch.Remove(round.Id);   // nothing crosses it: it goes whole, its points with it unless something else uses them
                foreach (var id in new[] { round.Start, round.End, round.Center })
                    if (id != null && !Used(sketch, id)) sketch.Remove(id);
                return Check(sketch, before);
            }

            if (!arc)
            {
                if (cuts.Count < 2) return "Bir daireyi budamak için onu iki yerden kesen öğeler gerekir.";
                // the crossings either side of the click, going round: what is left runs from the one after to the one before
                var after = cuts.FirstOrDefault(h => h.A > click);
                if (after.By == null) after = cuts[0];
                var beforeCut = cuts.LastOrDefault(h => h.A < click);
                if (beforeCut.By == null) beforeCut = cuts[cuts.Count - 1];
                var (sx, sy) = At(after.A);
                var (ex, ey) = At(beforeCut.A);
                var start = sketch.AddPoint(sx, sy);
                var end = sketch.AddPoint(ex, ey);
                round.Kind = SketchEntityKind.Arc;   // the same entity, so its radius, tangencies and equalities stay
                round.Start = start.Id;
                round.End = end.Id;
                HoldOn(sketch, start, after.By);
                HoldOn(sketch, end, beforeCut.By);
                return Check(sketch, before);
            }

            var lower = cuts.LastOrDefault(h => h.A < click);
            var upper = cuts.FirstOrDefault(h => h.A > click);
            string oldStart = round.Start!, oldEnd = round.End!;
            if (lower.By != null && upper.By != null)
            {
                // the middle goes: this arc keeps its start, a new one on the same centre takes its end
                var (px, py) = At(lower.A);
                var (qx, qy) = At(upper.A);
                var p = sketch.AddPoint(px, py);
                var q = sketch.AddPoint(qx, qy);
                round.End = p.Id;
                var rest = sketch.AddArc(o, q, sketch.Point(oldEnd)!, round.Construction);
                sketch.Constrain(SketchConstraintKind.Equal, round.Id, rest.Id);
                HoldOn(sketch, p, lower.By);
                HoldOn(sketch, q, upper.By);
            }
            else if (upper.By != null)
            {
                var (qx, qy) = At(upper.A);
                var q = sketch.AddPoint(qx, qy);
                round.Start = q.Id;
                HoldOn(sketch, q, upper.By);
                if (!Used(sketch, oldStart)) sketch.Remove(oldStart);
            }
            else
            {
                var (px, py) = At(lower.A);
                var p = sketch.AddPoint(px, py);
                round.End = p.Id;
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
