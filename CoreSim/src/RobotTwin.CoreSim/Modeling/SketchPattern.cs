using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>
    /// [sketch-pattern] SolidWorks' Linear and Circular Sketch Pattern. Copies of the chosen lines, circles and arcs, each
    /// held to the one before it by dimensions, so the pattern follows every later change and each step's dimension sits
    /// in its own gap: in a row, each copied point the step's X and Y from the same point of the copy before; round a
    /// centre point, a construction spoke to each point, equal to the one before and at the step's angle from it. Copied
    /// circles stay equal to their originals. The count includes the original.
    /// </summary>
    public static class SketchPattern
    {
        public static string? Linear(ConstrainedSketch s, IReadOnlyCollection<string> entityIds, int count, double dx, double dy, List<string>? made = null) =>
            Make(s, entityIds, count, made, (p, i) => (p.X + dx * i, p.Y + dy * i), (prev, copy) =>
            {
                s.Constrain(SketchConstraintKind.HorizontalDistance, prev.Id, copy.Id, dx);
                s.Constrain(SketchConstraintKind.VerticalDistance, prev.Id, copy.Id, dy);
            });

        /// <summary>Round <paramref name="centerPoint"/>: copy i turned by i × (360° / count), or i × angle / (count − 1) for less than a full turn.</summary>
        public static string? Circular(ConstrainedSketch s, IReadOnlyCollection<string> entityIds, string centerPoint, int count, double totalDegrees, List<string>? made = null)
        {
            var c = s.Point(centerPoint);
            if (c == null) return "Dairesel çoğaltma için merkez noktası seçin.";
            bool full = Math.Abs(Math.Abs(totalDegrees) - 360) < 1e-9 || Math.Abs(totalDegrees) > 360;
            double each = count <= 1 ? 0 : full ? 360.0 / count : totalDegrees / (count - 1);
            var spokes = new Dictionary<string, SketchEntity>(StringComparer.Ordinal);
            SketchEntity Spoke(SketchPoint p)
            {
                if (!spokes.TryGetValue(p.Id, out var l))
                {
                    l = s.AddLine(c, p, construction: true);
                    spokes[p.Id] = l;
                }

                return l;
            }

            return Make(s, entityIds.Where(id => id != centerPoint).ToList(), count, made, (p, i) =>
            {
                double a = each * i * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
                return (c.X + (p.X - c.X) * cos - (p.Y - c.Y) * sin, c.Y + (p.X - c.X) * sin + (p.Y - c.Y) * cos);
            }, (prev, copy) =>
            {
                var from = Spoke(prev);
                var to = Spoke(copy);
                s.Constrain(SketchConstraintKind.Equal, from.Id, to.Id);
                s.Constrain(SketchConstraintKind.Angle, from.Id, to.Id, NormalAngle(each));
            }, keep: centerPoint);
        }

        private static double NormalAngle(double deg)
        {
            deg %= 360;
            return deg < 0 ? deg + 360 : deg;
        }

        private static string? Make(ConstrainedSketch s, IReadOnlyCollection<string> entityIds, int count, List<string>? made,
            Func<SketchPoint, int, (double X, double Y)> place, Action<SketchPoint, SketchPoint> hold, string? keep = null)
        {
            if (count < 2) return "Adet en az 2 olmalı (aslı dahil).";
            var entities = entityIds.Select(s.Entity).Where(e => e != null).Select(e => e!).ToList();
            if (entities.Count == 0) return "Çoğaltılacak çizgi, daire ya da yay seçin.";
            var before = s.Clone();
            var last = new Dictionary<string, SketchPoint>(StringComparer.Ordinal);   // each original point's copy in the step before
            for (int i = 1; i < count; i++)
            {
                var map = new Dictionary<string, SketchPoint>(StringComparer.Ordinal);
                SketchPoint M(string? id)
                {
                    var p = s.Point(id)!;
                    if (keep != null && p.Id == keep) return p;   // the centre stays the centre
                    if (map.TryGetValue(p.Id, out var q)) return q;
                    var (x, y) = place(p, i);
                    q = s.AddPoint(x, y);
                    map[p.Id] = q;
                    hold(last.TryGetValue(p.Id, out var prev) ? prev : p, q);
                    return q;
                }

                foreach (var e in entities)
                {
                    SketchEntity copy;
                    switch (e.Kind)
                    {
                        case SketchEntityKind.Line:
                            copy = s.AddLine(M(e.Start), M(e.End), e.Construction);
                            break;
                        case SketchEntityKind.Circle:
                            copy = s.AddCircle(M(e.Center), e.Radius, e.Construction);
                            s.Constrain(SketchConstraintKind.Equal, e.Id, copy.Id);
                            break;
                        default:
                            copy = s.AddArc(M(e.Center), M(e.Start), M(e.End), e.Construction);
                            break;
                    }

                    made?.Add(copy.Id);
                }

                foreach (var kv in map) last[kv.Key] = kv.Value;
            }

            if (s.Solve(explain: false).State == SketchState.Conflicting)
            {
                s.Points = before.Points;
                s.Entities = before.Entities;
                s.Constraints = before.Constraints;
                made?.Clear();
                return "Çoğaltınca ölçüler çelişiyor.";
            }

            return null;
        }
    }
}
