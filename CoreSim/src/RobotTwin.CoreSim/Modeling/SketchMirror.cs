using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>
    /// [sketch-mirror] SolidWorks' Mirror Entities: copies of the chosen lines, circles and arcs on the other side of a
    /// line, each copied point held symmetric to its original about that line (so the copy follows every later change),
    /// a point on the line shared rather than copied, a copied circle kept equal to its original.
    /// </summary>
    public static class SketchMirror
    {
        /// <summary>Null when it was done, else why not (the sketch is left as it was). Returns the new entities' ids in <paramref name="made"/>.</summary>
        public static string? Mirror(ConstrainedSketch sketch, IReadOnlyCollection<string> entityIds, string axisLine, List<string>? made = null)
        {
            var axis = sketch.Entity(axisLine);
            if (axis == null || axis.Kind != SketchEntityKind.Line) return "Ayna ekseni bir çizgi olmalı.";
            var a = sketch.Point(axis.Start)!;
            var b = sketch.Point(axis.End)!;
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            if (len2 < 1e-18) return "Ayna ekseni sıfır uzunlukta.";
            var entities = entityIds.Where(id => id != axisLine).Select(sketch.Entity).Where(e => e != null).Select(e => e!).ToList();
            if (entities.Count == 0) return "Aynalanacak çizgi, daire ya da yay seçin.";

            var before = sketch.Clone();
            var map = new Dictionary<string, SketchPoint>(StringComparer.Ordinal);
            SketchPoint M(string? id)
            {
                var p = sketch.Point(id)!;
                if (map.TryGetValue(p.Id, out var q)) return q;
                double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
                double fx = a.X + dx * t, fy = a.Y + dy * t;   // the foot on the axis
                double off = Math.Sqrt((p.X - fx) * (p.X - fx) + (p.Y - fy) * (p.Y - fy));
                if (off < 1e-3)
                {
                    // on the axis (as drawn, to a thousandth of a mm): its own mirror image, and held there
                    map[p.Id] = p;
                    if (!sketch.Constraints.Any(k => k.Kind == SketchConstraintKind.PointOnLine && k.A == p.Id && k.B == axis.Id) && p.Id != axis.Start && p.Id != axis.End)
                        sketch.Constrain(SketchConstraintKind.PointOnLine, p.Id, axis.Id);
                    return p;
                }

                q = sketch.AddPoint(2 * fx - p.X, 2 * fy - p.Y);
                map[p.Id] = q;
                sketch.Constrain(SketchConstraintKind.Symmetric, p.Id, q.Id, 0, axis.Id);
                return q;
            }

            foreach (var e in entities)
            {
                SketchEntity copy;
                switch (e.Kind)
                {
                    case SketchEntityKind.Line:
                        copy = sketch.AddLine(M(e.Start), M(e.End), e.Construction);
                        break;
                    case SketchEntityKind.Circle:
                        copy = sketch.AddCircle(M(e.Center), e.Radius, e.Construction);
                        sketch.Constrain(SketchConstraintKind.Equal, e.Id, copy.Id);
                        break;
                    default:
                        // a mirror turns the arc's way round: its counter-clockwise run goes from the image of its end
                        copy = sketch.AddArc(M(e.Center), M(e.End), M(e.Start), e.Construction);
                        break;
                }

                made?.Add(copy.Id);
            }

            if (sketch.Solve(explain: false).State == SketchState.Conflicting)
            {
                sketch.Points = before.Points;
                sketch.Entities = before.Entities;
                sketch.Constraints = before.Constraints;
                made?.Clear();
                return "Aynalanınca ölçüler çelişiyor.";
            }

            return null;
        }
    }
}
