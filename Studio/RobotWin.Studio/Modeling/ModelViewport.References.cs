using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;

namespace RobotWin.Studio.Modeling
{
    /// <summary>
    /// [refgeom] The reference planes, axes and points on screen: their outlines as screen segments (a plane's square, an
    /// axis's line, a point's cross), drawn by the overlay and picked within a few pixels of a line, as SolidWorks picks a
    /// plane by its border. A reference has no solid, so the ray pick never finds one.
    /// </summary>
    public sealed partial class ModelViewport
    {
        internal IEnumerable<(ModelObject O, List<(Point A, Point B)> Lines, Point Label)> ReferenceOutlines()
        {
            var doc = _ed.Doc;
            foreach (var o in doc.Objects)
            {
                if (o.Reference is not { } r || !o.Visible) continue;
                var m = doc.WorldMatrix(o);
                var lines = new List<(Point, Point)>();
                Point label;
                if (r.Kind == ReferenceKind.Point)
                {
                    var c = Cam.Project(m.Apply(Vec3.Zero));
                    if (!c.Visible) continue;
                    var s = c.Screen;
                    lines.Add((new Point(s.X - 6, s.Y - 6), new Point(s.X + 6, s.Y + 6)));
                    lines.Add((new Point(s.X - 6, s.Y + 6), new Point(s.X + 6, s.Y - 6)));
                    label = new Point(s.X + 8, s.Y - 18);
                }
                else if (r.Kind == ReferenceKind.Axis)
                {
                    double h = r.Size / 2;
                    var a = Cam.Project(m.Apply(new Vec3(0, 0, -h)));
                    var b = Cam.Project(m.Apply(new Vec3(0, 0, h)));
                    if (!a.Visible || !b.Visible) continue;
                    lines.Add((a.Screen, b.Screen));
                    label = new Point(b.Screen.X + 6, b.Screen.Y - 16);
                }
                else
                {
                    double h = r.Size / 2;
                    var corners = new[] { new Vec3(-h, -h, 0), new Vec3(h, -h, 0), new Vec3(h, h, 0), new Vec3(-h, h, 0) }.Select(v => Cam.Project(m.Apply(v))).ToArray();
                    if (corners.Any(c => !c.Visible)) continue;
                    for (int i = 0; i < 4; i++) lines.Add((corners[i].Screen, corners[(i + 1) % 4].Screen));
                    var top = corners.OrderBy(c => c.Screen.Y).ThenBy(c => c.Screen.X).First().Screen;
                    label = new Point(top.X + 6, top.Y + 4);   // inside its top corner, as SolidWorks names a plane
                }

                yield return (o, lines, label);
            }
        }

        /// <summary>The reference whose outline passes within a few pixels of <paramref name="screen"/>, nearest first.</summary>
        internal string? PickReference(Point screen)
        {
            string? best = null;
            double bestD = 6;
            foreach (var (o, lines, _) in ReferenceOutlines())
            {
                foreach (var (a, b) in lines)
                {
                    double d = DistanceToSegment(screen, a, b);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = o.Id;
                    }
                }
            }

            return best;
        }

        private static double DistanceToSegment(Point p, Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
            double x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y;
            return Math.Sqrt(x * x + y * y);
        }
    }
}
