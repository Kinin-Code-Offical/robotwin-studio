using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>[sketch] What a constrained sketch is drawn from.</summary>
    public enum SketchEntityKind
    {
        /// <summary>A straight segment between two points.</summary>
        Line,

        /// <summary>A full circle: a centre point and a radius.</summary>
        Circle,

        /// <summary>An arc counter-clockwise from its start to its end point round its centre (both at the same radius).</summary>
        Arc
    }

    /// <summary>[sketch] The rules a sketch keeps: geometric relations and driving dimensions (mm, degrees).</summary>
    public enum SketchConstraintKind
    {
        /// <summary>Two points at the same place.</summary>
        Coincident,

        /// <summary>A line level with the sketch's X axis.</summary>
        Horizontal,

        /// <summary>A line along the sketch's Y axis.</summary>
        Vertical,

        /// <summary>A point held where it is (or at Value, Value2).</summary>
        Fix,

        /// <summary>Two points a distance apart.</summary>
        Distance,

        /// <summary>The X distance from one point to another (signed).</summary>
        HorizontalDistance,

        /// <summary>The Y distance from one point to another (signed).</summary>
        VerticalDistance,

        /// <summary>A line's length.</summary>
        Length,

        /// <summary>A circle's or arc's radius.</summary>
        Radius,

        /// <summary>A circle's or arc's diameter.</summary>
        Diameter,

        /// <summary>The angle from one line to another (degrees, counter-clockwise).</summary>
        Angle,

        Parallel,
        Perpendicular,

        /// <summary>Two lines of the same length, or two circles/arcs of the same radius.</summary>
        Equal,

        /// <summary>A point on a line (extended).</summary>
        PointOnLine,

        /// <summary>A point on a circle or arc (its circle).</summary>
        PointOnCircle,

        /// <summary>A line touching a circle or arc, or two circles touching.</summary>
        Tangent,

        /// <summary>A point at a line's middle.</summary>
        Midpoint,

        /// <summary>Two circles or arcs round the same centre.</summary>
        Concentric,

        /// <summary>Two points mirrored about a line.</summary>
        Symmetric
    }

    public sealed class SketchPoint
    {
        public string Id { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }

        public SketchPoint Clone() => (SketchPoint)MemberwiseClone();
    }

    public sealed class SketchEntity
    {
        public string Id { get; set; } = "";
        public SketchEntityKind Kind { get; set; }

        /// <summary>Line start / arc start.</summary>
        public string? Start { get; set; }

        /// <summary>Line end / arc end.</summary>
        public string? End { get; set; }

        /// <summary>Circle or arc centre.</summary>
        public string? Center { get; set; }

        /// <summary>A circle's radius (an arc's is the distance from its centre to its start).</summary>
        public double Radius { get; set; }

        /// <summary>Construction geometry: it guides other entities and makes no edge of the profile.</summary>
        public bool Construction { get; set; }

        public SketchEntity Clone() => (SketchEntity)MemberwiseClone();
    }

    public sealed class SketchConstraint
    {
        public string Id { get; set; } = "";
        public SketchConstraintKind Kind { get; set; }

        /// <summary>The first point or entity.</summary>
        public string A { get; set; } = "";

        /// <summary>The second point or entity, if the relation takes two.</summary>
        public string? B { get; set; }

        /// <summary>A third (the mirror line of <see cref="SketchConstraintKind.Symmetric"/>).</summary>
        public string? C { get; set; }

        /// <summary>The dimension (mm or degrees); a Fix's X.</summary>
        public double Value { get; set; }

        /// <summary>A Fix's Y.</summary>
        public double Value2 { get; set; }

        public SketchConstraint Clone() => (SketchConstraint)MemberwiseClone();
    }

    /// <summary>How far a sketch's constraints pin it down.</summary>
    public enum SketchState
    {
        /// <summary>Some points or radii can still move (<see cref="SketchSolveResult.Freedom"/> > 0).</summary>
        UnderConstrained,

        /// <summary>Every point and radius is fixed by the constraints.</summary>
        FullyConstrained,

        /// <summary>It solved, but some constraints repeat what others already say.</summary>
        Redundant,

        /// <summary>The constraints contradict each other: no shape satisfies them all.</summary>
        Conflicting
    }

    public sealed class SketchSolveResult
    {
        public SketchState State { get; internal set; }

        /// <summary>Degrees of freedom left: the ways the sketch can still move without breaking a constraint.</summary>
        public int Freedom { get; internal set; }

        /// <summary>The largest constraint error after solving (mm).</summary>
        public double Residual { get; internal set; }

        public int Iterations { get; internal set; }

        /// <summary>Constraints that add nothing (Redundant) or that cannot all hold (Conflicting).</summary>
        public List<string> Culprits { get; } = new List<string>();

        public bool Solved => State != SketchState.Conflicting;
    }

    /// <summary>
    /// [sketch] A dimensioned 2D sketch: points, lines, circles and arcs held by geometric constraints and driving
    /// dimensions, solved together (Levenberg–Marquardt on all the constraint equations at once, as parametric CAD
    /// sketchers do). The solver moves the geometry as little as it can to meet the constraints, counts the freedom left
    /// from the rank of the equations, and names the constraints that repeat or contradict others. Its closed loops of
    /// lines and arcs become an extrusion profile.
    /// </summary>
    public sealed class ConstrainedSketch
    {
        public List<SketchPoint> Points { get; set; } = new List<SketchPoint>();
        public List<SketchEntity> Entities { get; set; } = new List<SketchEntity>();
        public List<SketchConstraint> Constraints { get; set; } = new List<SketchConstraint>();

        /// <summary>Accepted constraint error (mm).</summary>
        public const double Tolerance = 1e-10;

        public ConstrainedSketch Clone() => new ConstrainedSketch
        {
            Points = Points.Select(p => p.Clone()).ToList(),
            Entities = Entities.Select(e => e.Clone()).ToList(),
            Constraints = Constraints.Select(c => c.Clone()).ToList()
        };

        public SketchPoint? Point(string? id) => id == null ? null : Points.FirstOrDefault(p => p.Id == id);

        public SketchEntity? Entity(string? id) => id == null ? null : Entities.FirstOrDefault(e => e.Id == id);

        private string Next(string prefix, IEnumerable<string> used)
        {
            var set = new HashSet<string>(used, StringComparer.Ordinal);
            for (int i = 1; ; i++)
            {
                string id = prefix + i;
                if (!set.Contains(id)) return id;
            }
        }

        public SketchPoint AddPoint(double x, double y)
        {
            var p = new SketchPoint { Id = Next("P", Points.Select(q => q.Id)), X = x, Y = y };
            Points.Add(p);
            return p;
        }

        public SketchEntity AddLine(SketchPoint a, SketchPoint b, bool construction = false)
        {
            var e = new SketchEntity { Id = Next("L", Entities.Select(q => q.Id)), Kind = SketchEntityKind.Line, Start = a.Id, End = b.Id, Construction = construction };
            Entities.Add(e);
            return e;
        }

        public SketchEntity AddCircle(SketchPoint centre, double radius, bool construction = false)
        {
            var e = new SketchEntity { Id = Next("K", Entities.Select(q => q.Id)), Kind = SketchEntityKind.Circle, Center = centre.Id, Radius = radius, Construction = construction };
            Entities.Add(e);
            return e;
        }

        public SketchEntity AddArc(SketchPoint centre, SketchPoint start, SketchPoint end, bool construction = false)
        {
            var e = new SketchEntity { Id = Next("Y", Entities.Select(q => q.Id)), Kind = SketchEntityKind.Arc, Center = centre.Id, Start = start.Id, End = end.Id, Construction = construction };
            e.Radius = Math.Sqrt((start.X - centre.X) * (start.X - centre.X) + (start.Y - centre.Y) * (start.Y - centre.Y));
            Entities.Add(e);
            return e;
        }

        public SketchConstraint Constrain(SketchConstraintKind kind, string a, string? b = null, double value = 0, string? c = null)
        {
            var k = new SketchConstraint { Id = Next("C", Constraints.Select(q => q.Id)), Kind = kind, A = a, B = b, C = c, Value = value };
            if (kind == SketchConstraintKind.Fix && Point(a) is { } p && b == null)
            {
                k.Value = p.X;
                k.Value2 = p.Y;
            }

            Constraints.Add(k);
            return k;
        }

        /// <summary>
        /// A shape sketch made into a dimensioned one: an upright rectangle outline gets its width, height and lower-left
        /// corner; round holes their diameters and centres measured from that corner (all fully constrained); any other
        /// outline becomes its polygon of lines, free to be dimensioned.
        /// </summary>
        public static ConstrainedSketch From(Sketch2D sketch)
        {
            var s = new ConstrainedSketch();
            SketchPoint? anchor = null;
            var o = sketch.Outer;
            if (o.Kind == SketchShapeKind.Rectangle && Math.Abs(o.Rotation) < 1e-9 && o.CornerRadius <= 1e-9)
            {
                var lines = s.AddRectangle(o.X - o.Width / 2, o.Y - o.Height / 2, o.X + o.Width / 2, o.Y + o.Height / 2);
                anchor = s.Point(lines[0].Start);
                s.Constrain(SketchConstraintKind.Fix, anchor!.Id);
                s.Constrain(SketchConstraintKind.Length, lines[0].Id, value: o.Width);
                s.Constrain(SketchConstraintKind.Length, lines[1].Id, value: o.Height);
            }
            else if (o.Kind == SketchShapeKind.Circle)
            {
                anchor = s.AddPoint(o.X, o.Y);
                var c = s.AddCircle(anchor, o.Radius);
                s.Constrain(SketchConstraintKind.Fix, anchor.Id);
                s.Constrain(SketchConstraintKind.Diameter, c.Id, value: 2 * o.Radius);
            }
            else Polygon(s, o.ToPolygon(32));

            foreach (var h in sketch.Holes)
            {
                if (h.Kind == SketchShapeKind.Circle)
                {
                    var c = s.AddPoint(h.X, h.Y);
                    var k = s.AddCircle(c, h.Radius);
                    s.Constrain(SketchConstraintKind.Diameter, k.Id, value: 2 * h.Radius);
                    if (anchor != null)
                    {
                        s.Constrain(SketchConstraintKind.HorizontalDistance, anchor.Id, c.Id, h.X - anchor.X);
                        s.Constrain(SketchConstraintKind.VerticalDistance, anchor.Id, c.Id, h.Y - anchor.Y);
                    }
                }
                else Polygon(s, h.ToPolygon(32));
            }

            return s;
        }

        /// <summary>
        /// A motor bracket, fully dimensioned: a 50 × 40 plate whose right end is a half circle tangent to its top and
        /// bottom, an M3 hole 15 mm in and 20 mm up, and an 8 mm seat round the end's centre. Drawn a little off, so the
        /// solver has something to do.
        /// </summary>
        public static ConstrainedSketch Bracket()
        {
            var s = new ConstrainedSketch();
            var p1 = s.AddPoint(0, 0);
            var p2 = s.AddPoint(48, 1);
            var c = s.AddPoint(49, 19);
            var p3 = s.AddPoint(47, 41);
            var p4 = s.AddPoint(1, 39);
            var bottom = s.AddLine(p1, p2);
            var end = s.AddArc(c, p2, p3);
            var top = s.AddLine(p3, p4);
            var left = s.AddLine(p4, p1);
            s.Constrain(SketchConstraintKind.Fix, p1.Id);
            s.Constrain(SketchConstraintKind.Horizontal, bottom.Id);
            s.Constrain(SketchConstraintKind.Horizontal, top.Id);
            s.Constrain(SketchConstraintKind.Vertical, left.Id);
            s.Constrain(SketchConstraintKind.Length, bottom.Id, value: 50);
            s.Constrain(SketchConstraintKind.Length, left.Id, value: 40);
            s.Constrain(SketchConstraintKind.Tangent, bottom.Id, end.Id, 1);
            s.Constrain(SketchConstraintKind.Tangent, top.Id, end.Id, 1);
            var h = s.AddPoint(14, 21);
            var hole = s.AddCircle(h, 1.5);
            s.Constrain(SketchConstraintKind.HorizontalDistance, p1.Id, h.Id, 15);
            s.Constrain(SketchConstraintKind.VerticalDistance, p1.Id, h.Id, 20);
            s.Constrain(SketchConstraintKind.Diameter, hole.Id, value: 3.2);
            var seat = s.AddCircle(s.AddPoint(50, 21), 5);
            s.Constrain(SketchConstraintKind.Concentric, seat.Id, end.Id);
            s.Constrain(SketchConstraintKind.Diameter, seat.Id, value: 8);
            return s;
        }

        private static void Polygon(ConstrainedSketch s, List<Vec2> outline)
        {
            if (outline.Count < 3) return;
            var pts = outline.Select(v => s.AddPoint(v.X, v.Y)).ToList();
            for (int i = 0; i < pts.Count; i++) s.AddLine(pts[i], pts[(i + 1) % pts.Count]);
        }

        /// <summary>The driving dimensions: the values a user sets (lengths, distances, diameters, radii, angles).</summary>
        [JsonIgnore]
        public IEnumerable<SketchConstraint> Dimensions => Constraints.Where(c => c.Kind is SketchConstraintKind.Distance or SketchConstraintKind.HorizontalDistance or
            SketchConstraintKind.VerticalDistance or SketchConstraintKind.Length or SketchConstraintKind.Radius or SketchConstraintKind.Diameter or SketchConstraintKind.Angle);

        /// <summary>A closed rectangle of four lines, level and upright, its corners shared (a common start).</summary>
        public List<SketchEntity> AddRectangle(double x0, double y0, double x1, double y1)
        {
            var p = new[] { AddPoint(x0, y0), AddPoint(x1, y0), AddPoint(x1, y1), AddPoint(x0, y1) };
            var lines = new List<SketchEntity>();
            for (int i = 0; i < 4; i++) lines.Add(AddLine(p[i], p[(i + 1) % 4]));
            Constrain(SketchConstraintKind.Horizontal, lines[0].Id);
            Constrain(SketchConstraintKind.Vertical, lines[1].Id);
            Constrain(SketchConstraintKind.Horizontal, lines[2].Id);
            Constrain(SketchConstraintKind.Vertical, lines[3].Id);
            return lines;
        }

        /// <summary>Removes a point, entity or constraint and everything that depends on it.</summary>
        public void Remove(string id)
        {
            var gone = new HashSet<string>(StringComparer.Ordinal) { id };
            foreach (var e in Entities)
                if (gone.Contains(e.Start ?? "") || gone.Contains(e.End ?? "") || gone.Contains(e.Center ?? ""))
                    gone.Add(e.Id);
            Entities.RemoveAll(e => gone.Contains(e.Id));
            Points.RemoveAll(p => gone.Contains(p.Id));
            Constraints.RemoveAll(c => gone.Contains(c.Id) || gone.Contains(c.A) || gone.Contains(c.B ?? "") || gone.Contains(c.C ?? ""));
        }

        // ---- the unknowns: every point's X and Y, every circle's radius -------------------------------------------

        private sealed class Layout
        {
            public readonly Dictionary<string, int> PointIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> RadiusIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<string, SketchEntity> Entities = new Dictionary<string, SketchEntity>(StringComparer.Ordinal);
            public int Count;
        }

        private Layout Lay()
        {
            var l = new Layout();
            foreach (var p in Points)
            {
                l.PointIndex[p.Id] = l.Count;
                l.Count += 2;
            }

            foreach (var e in Entities)
            {
                l.Entities[e.Id] = e;
                if (e.Kind == SketchEntityKind.Circle) l.RadiusIndex[e.Id] = l.Count++;
            }

            return l;
        }

        private double[] Read(Layout l)
        {
            var x = new double[l.Count];
            foreach (var p in Points)
            {
                int i = l.PointIndex[p.Id];
                x[i] = p.X;
                x[i + 1] = p.Y;
            }

            foreach (var e in Entities)
                if (l.RadiusIndex.TryGetValue(e.Id, out int r)) x[r] = e.Radius;
            return x;
        }

        private void Write(Layout l, double[] x)
        {
            foreach (var p in Points)
            {
                int i = l.PointIndex[p.Id];
                p.X = x[i];
                p.Y = x[i + 1];
            }

            foreach (var e in Entities)
            {
                if (l.RadiusIndex.TryGetValue(e.Id, out int r)) e.Radius = Math.Abs(x[r]);
                else if (e.Kind == SketchEntityKind.Arc && Point(e.Center) is { } c && Point(e.Start) is { } s)
                    e.Radius = Math.Sqrt((s.X - c.X) * (s.X - c.X) + (s.Y - c.Y) * (s.Y - c.Y));
            }
        }

        /// <summary>The equations of one constraint (or an arc's own), each zero when it holds, in mm.</summary>
        private sealed class Equations
        {
            private readonly Layout _l;
            private double[] _x = Array.Empty<double>();
            public readonly List<double> Rows = new List<double>();

            public Equations(Layout l) => _l = l;

            public void Reset(double[] x)
            {
                _x = x;
                Rows.Clear();
            }

            private (double X, double Y) P(string? id)
            {
                if (id == null || !_l.PointIndex.TryGetValue(id, out int i)) throw new InvalidOperationException("nokta yok: " + id);
                return (_x[i], _x[i + 1]);
            }

            private SketchEntity E(string? id) =>
                id != null && _l.Entities.TryGetValue(id, out var e) ? e : throw new InvalidOperationException("çizgi yok: " + id);

            private (double X, double Y) Dir(SketchEntity line)
            {
                var a = P(line.Start);
                var b = P(line.End);
                return (b.X - a.X, b.Y - a.Y);
            }

            private double R(SketchEntity e)
            {
                if (_l.RadiusIndex.TryGetValue(e.Id, out int r)) return _x[r];
                var c = P(e.Center);
                var s = P(e.Start);
                return Math.Sqrt((s.X - c.X) * (s.X - c.X) + (s.Y - c.Y) * (s.Y - c.Y));
            }

            private static double Len(double x, double y) => Math.Sqrt(x * x + y * y);

            /// <summary>The end point a line or arc shares with an arc, if they meet at one.</summary>
            private static string? Shared(SketchEntity a, SketchEntity b)
            {
                if (b.Kind != SketchEntityKind.Arc && a.Kind != SketchEntityKind.Arc) return null;
                foreach (var e in new[] { a.Start, a.End })
                    if (e != null && (e == b.Start || e == b.End)) return e;
                return null;
            }

            /// <summary>An arc's end lies on its circle.</summary>
            public void Arc(SketchEntity arc)
            {
                var c = P(arc.Center);
                var e = P(arc.End);
                Rows.Add(Len(e.X - c.X, e.Y - c.Y) - R(arc));
            }

            public void Add(SketchConstraint k)
            {
                switch (k.Kind)
                {
                    case SketchConstraintKind.Coincident:
                    {
                        var a = P(k.A);
                        var b = P(k.B);
                        Rows.Add(a.X - b.X);
                        Rows.Add(a.Y - b.Y);
                        break;
                    }

                    case SketchConstraintKind.Horizontal:
                        Rows.Add(Dir(E(k.A)).Y);
                        break;
                    case SketchConstraintKind.Vertical:
                        Rows.Add(Dir(E(k.A)).X);
                        break;
                    case SketchConstraintKind.Fix:
                    {
                        var a = P(k.A);
                        Rows.Add(a.X - k.Value);
                        Rows.Add(a.Y - k.Value2);
                        break;
                    }

                    case SketchConstraintKind.Distance:
                    {
                        var a = P(k.A);
                        var b = P(k.B);
                        Rows.Add(Len(b.X - a.X, b.Y - a.Y) - k.Value);
                        break;
                    }

                    case SketchConstraintKind.HorizontalDistance:
                        Rows.Add(P(k.B).X - P(k.A).X - k.Value);
                        break;
                    case SketchConstraintKind.VerticalDistance:
                        Rows.Add(P(k.B).Y - P(k.A).Y - k.Value);
                        break;
                    case SketchConstraintKind.Length:
                    {
                        var d = Dir(E(k.A));
                        Rows.Add(Len(d.X, d.Y) - k.Value);
                        break;
                    }

                    case SketchConstraintKind.Radius:
                        Rows.Add(R(E(k.A)) - k.Value);
                        break;
                    case SketchConstraintKind.Diameter:
                        Rows.Add(2 * R(E(k.A)) - k.Value);
                        break;
                    case SketchConstraintKind.Angle:
                    {
                        // sin(φ − θ) of the angle φ from A to B: smooth, and zero only at θ (not θ + 180°, which the dot
                        // product's sign tells apart; the solver starts near the drawing, so the root it finds is the one meant).
                        var u = Dir(E(k.A));
                        var v = Dir(E(k.B));
                        double n = Math.Max(1e-12, Len(u.X, u.Y) * Len(v.X, v.Y));
                        double cross = (u.X * v.Y - u.Y * v.X) / n, dot = (u.X * v.X + u.Y * v.Y) / n;
                        double t = k.Value * Math.PI / 180;
                        Rows.Add(10 * (cross * Math.Cos(t) - dot * Math.Sin(t)));
                        break;
                    }

                    case SketchConstraintKind.Parallel:
                    {
                        var u = Dir(E(k.A));
                        var v = Dir(E(k.B));
                        Rows.Add(10 * (u.X * v.Y - u.Y * v.X) / Math.Max(1e-12, Len(u.X, u.Y) * Len(v.X, v.Y)));
                        break;
                    }

                    case SketchConstraintKind.Perpendicular:
                    {
                        var u = Dir(E(k.A));
                        var v = Dir(E(k.B));
                        Rows.Add(10 * (u.X * v.X + u.Y * v.Y) / Math.Max(1e-12, Len(u.X, u.Y) * Len(v.X, v.Y)));
                        break;
                    }

                    case SketchConstraintKind.Equal:
                    {
                        var a = E(k.A);
                        var b = E(k.B);
                        double la = a.Kind == SketchEntityKind.Line ? Len(Dir(a).X, Dir(a).Y) : R(a);
                        double lb = b.Kind == SketchEntityKind.Line ? Len(Dir(b).X, Dir(b).Y) : R(b);
                        Rows.Add(la - lb);
                        break;
                    }

                    case SketchConstraintKind.PointOnLine:
                    {
                        var p = P(k.A);
                        var line = E(k.B);
                        var a = P(line.Start);
                        var d = Dir(line);
                        Rows.Add((d.X * (p.Y - a.Y) - d.Y * (p.X - a.X)) / Math.Max(1e-12, Len(d.X, d.Y)));
                        break;
                    }

                    case SketchConstraintKind.PointOnCircle:
                    {
                        var p = P(k.A);
                        var circle = E(k.B);
                        var c = P(circle.Center);
                        Rows.Add(Len(p.X - c.X, p.Y - c.Y) - R(circle));
                        break;
                    }

                    case SketchConstraintKind.Tangent:
                    {
                        var a = E(k.A);
                        var b = E(k.B);
                        if (a.Kind != SketchEntityKind.Line && b.Kind == SketchEntityKind.Line) (a, b) = (b, a);
                        if (Shared(a, b) is { } joint)
                        {
                            // Joined at an end (a fillet, a round end): the radius there is square to the line, or the two
                            // centres and the joint are in line. First order, where the distance form is second order at
                            // a shared point and converges slowly.
                            var p = P(joint);
                            var c2 = P(b.Center);
                            if (a.Kind == SketchEntityKind.Line)
                            {
                                var d = Dir(a);
                                Rows.Add(((p.X - c2.X) * d.X + (p.Y - c2.Y) * d.Y) / Math.Max(1e-12, Len(d.X, d.Y)));
                            }
                            else
                            {
                                var c1 = P(a.Center);
                                double ux = p.X - c1.X, uy = p.Y - c1.Y, vx = p.X - c2.X, vy = p.Y - c2.Y;
                                Rows.Add((ux * vy - uy * vx) / Math.Max(1e-12, Len(vx, vy)));
                            }

                            break;
                        }

                        if (a.Kind == SketchEntityKind.Line)
                        {
                            // The circle's centre at its radius from the line, on whichever side it was drawn (Value: ±1).
                            var s = P(a.Start);
                            var d = Dir(a);
                            var c = P(b.Center);
                            double side = (d.X * (c.Y - s.Y) - d.Y * (c.X - s.X)) / Math.Max(1e-12, Len(d.X, d.Y));
                            Rows.Add(side - (k.Value < 0 ? -1 : 1) * R(b));
                        }
                        else
                        {
                            // Two circles: touching outside (Value ≥ 0) or one inside the other (Value < 0).
                            var c1 = P(a.Center);
                            var c2 = P(b.Center);
                            double dist = Len(c2.X - c1.X, c2.Y - c1.Y);
                            Rows.Add(k.Value < 0 ? dist - Math.Abs(R(a) - R(b)) : dist - (R(a) + R(b)));
                        }

                        break;
                    }

                    case SketchConstraintKind.Midpoint:
                    {
                        var p = P(k.A);
                        var line = E(k.B);
                        var a = P(line.Start);
                        var b = P(line.End);
                        Rows.Add(p.X - (a.X + b.X) / 2);
                        Rows.Add(p.Y - (a.Y + b.Y) / 2);
                        break;
                    }

                    case SketchConstraintKind.Concentric:
                    {
                        var a = P(E(k.A).Center);
                        var b = P(E(k.B).Center);
                        Rows.Add(a.X - b.X);
                        Rows.Add(a.Y - b.Y);
                        break;
                    }

                    case SketchConstraintKind.Symmetric:
                    {
                        // The midpoint of A–B on the line C, and A–B square to it.
                        var a = P(k.A);
                        var b = P(k.B);
                        var line = E(k.C);
                        var s = P(line.Start);
                        var d = Dir(line);
                        double n = Math.Max(1e-12, Len(d.X, d.Y));
                        double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
                        Rows.Add((d.X * (my - s.Y) - d.Y * (mx - s.X)) / n);
                        Rows.Add(((b.X - a.X) * d.X + (b.Y - a.Y) * d.Y) / n);
                        break;
                    }
                }
            }
        }

        /// <summary>The rows each constraint adds (in order), and the arcs' own rows last.</summary>
        private double[] Residuals(Layout l, Equations eq, double[] x, IReadOnlyList<SketchConstraint> constraints, List<int>? owners)
        {
            eq.Reset(x);
            owners?.Clear();
            for (int i = 0; i < constraints.Count; i++)
            {
                int before = eq.Rows.Count;
                eq.Add(constraints[i]);
                if (owners != null)
                    for (int r = before; r < eq.Rows.Count; r++) owners.Add(i);
            }

            foreach (var e in Entities)
            {
                if (e.Kind != SketchEntityKind.Arc) continue;
                int before = eq.Rows.Count;
                eq.Arc(e);
                if (owners != null)
                    for (int r = before; r < eq.Rows.Count; r++) owners.Add(-1);
            }

            return eq.Rows.ToArray();
        }

        /// <summary>
        /// Moves the points and radii as little as it can until every constraint holds, and says how well that went: the
        /// freedom left, the constraints that repeat others, or the ones that cannot all hold.
        /// </summary>
        /// <param name="explain">Find the constraints a conflict comes from (one more solve per constraint): off while dragging.</param>
        public SketchSolveResult Solve(bool explain = true)
        {
            // A constraint left pointing at something removed, or naming the wrong kind of thing, is dropped.
            Constraints.RemoveAll(c => !Valid(c));
            var result = Solve(Constraints, write: true);
            if (explain && result.State == SketchState.Conflicting && Constraints.Count <= 80)
            {
                // The ones without which the rest can be met: the culprits.
                foreach (var k in Constraints)
                {
                    var rest = Constraints.Where(c => !ReferenceEquals(c, k)).ToList();
                    if (Clone().SolveOn(rest).State != SketchState.Conflicting) result.Culprits.Add(k.Id);
                }
            }

            return result;
        }

        private SketchSolveResult SolveOn(List<SketchConstraint> constraints) => Solve(constraints, write: true);

        private bool Valid(SketchConstraint c)
        {
            bool Pt(string? id) => Point(id) != null;
            bool Ln(string? id) => Entity(id) is { Kind: SketchEntityKind.Line } e && Pt(e.Start) && Pt(e.End);
            bool Rd(string? id) => Entity(id) is { } e && e.Kind != SketchEntityKind.Line && Pt(e.Center) && (e.Kind == SketchEntityKind.Circle || (Pt(e.Start) && Pt(e.End)));
            bool Any(string? id) => Ln(id) || Rd(id);
            return c.Kind switch
            {
                SketchConstraintKind.Coincident or SketchConstraintKind.Distance or SketchConstraintKind.HorizontalDistance or SketchConstraintKind.VerticalDistance => Pt(c.A) && Pt(c.B),
                SketchConstraintKind.Fix => Pt(c.A),
                SketchConstraintKind.Horizontal or SketchConstraintKind.Vertical or SketchConstraintKind.Length => Ln(c.A),
                SketchConstraintKind.Radius or SketchConstraintKind.Diameter => Rd(c.A),
                SketchConstraintKind.Angle or SketchConstraintKind.Parallel or SketchConstraintKind.Perpendicular => Ln(c.A) && Ln(c.B),
                SketchConstraintKind.Equal => (Ln(c.A) && Ln(c.B)) || (Rd(c.A) && Rd(c.B)),
                SketchConstraintKind.PointOnLine or SketchConstraintKind.Midpoint => Pt(c.A) && Ln(c.B),
                SketchConstraintKind.PointOnCircle => Pt(c.A) && Rd(c.B),
                SketchConstraintKind.Tangent => Any(c.A) && Rd(c.B) || Rd(c.A) && Any(c.B),
                SketchConstraintKind.Concentric => Rd(c.A) && Rd(c.B),
                SketchConstraintKind.Symmetric => Pt(c.A) && Pt(c.B) && Ln(c.C),
                _ => false
            };
        }

        private SketchSolveResult Solve(IReadOnlyList<SketchConstraint> constraints, bool write)
        {
            var l = Lay();
            var eq = new Equations(l);
            var start = Read(l);
            var result = new SketchSolveResult();
            var owners = new List<int>();
            double[] F(double[] v) => Residuals(l, eq, v, constraints, null);
            var (x, r, it) = LeastSquares.MinNorm(F, start, Tolerance);
            result.Iterations = it;
            result.Residual = r.Length == 0 ? 0 : LeastSquares.Max(r);
            // Rank of the equations at the answer: the freedom left, and the rows that add nothing new.
            var jf = r.Length == 0 ? new double[0, x.Length] : LeastSquares.Jacobian(F, x, r.Length);
            Residuals(l, eq, x, constraints, owners);
            var dependent = LeastSquares.DependentRows(jf, out int rank);
            result.Freedom = x.Length - rank;
            if (result.Residual > 1e-5)
            {
                result.State = SketchState.Conflicting;
                if (write) Write(l, start);   // leave the drawing as it was
                return result;
            }

            if (write) Write(l, x);
            var redundant = dependent.Select(row => owners[row]).Where(o => o >= 0).Distinct().Select(o => constraints[o].Id).ToList();
            if (redundant.Count > 0)
            {
                result.State = SketchState.Redundant;
                result.Culprits.AddRange(redundant);
            }
            else result.State = result.Freedom == 0 ? SketchState.FullyConstrained : SketchState.UnderConstrained;

            return result;
        }

        // ---- profile ----------------------------------------------------------------------------------------------

        /// <summary>
        /// The closed loops its lines and arcs make (construction geometry left out), as counter-clockwise outlines:
        /// the largest the outer edge, the others holes. Ends meet when they share a point or sit together (a Coincident).
        /// </summary>
        public List<List<Vec2>> Loops(int segmentsPerCircle = 64)
        {
            var loops = new List<List<Vec2>>();
            // Points that are one (shared, or coincident within the tolerance) get one key.
            var key = new Dictionary<string, int>(StringComparer.Ordinal);
            var keyPos = new List<Vec2>();
            int KeyOf(string id)
            {
                if (key.TryGetValue(id, out int k)) return k;
                var p = Point(id)!;
                var v = new Vec2(p.X, p.Y);
                for (int i = 0; i < keyPos.Count; i++)
                {
                    if ((keyPos[i] - v).Length < 1e-6)
                    {
                        key[id] = i;
                        return i;
                    }
                }

                keyPos.Add(v);
                key[id] = keyPos.Count - 1;
                return keyPos.Count - 1;
            }

            var edges = new List<(int A, int B, SketchEntity E)>();
            foreach (var e in Entities)
            {
                if (e.Construction) continue;
                if (e.Kind == SketchEntityKind.Circle)
                {
                    var c = Point(e.Center);
                    if (c == null || e.Radius <= 1e-9) continue;
                    var loop = new List<Vec2>();
                    for (int i = 0; i < segmentsPerCircle; i++)
                    {
                        double t = 2 * Math.PI * i / segmentsPerCircle;
                        loop.Add(new Vec2(c.X + e.Radius * Math.Cos(t), c.Y + e.Radius * Math.Sin(t)));
                    }

                    loops.Add(loop);
                    continue;
                }

                if (e.Start == null || e.End == null || Point(e.Start) == null || Point(e.End) == null) continue;
                int a = KeyOf(e.Start), b = KeyOf(e.End);
                if (a != b) edges.Add((a, b, e));
            }

            var used = new bool[edges.Count];
            for (int first = 0; first < edges.Count; first++)
            {
                if (used[first]) continue;
                // Walk from this edge's start along unused edges until it comes back.
                var chain = new List<(int Edge, bool Forward)>();
                int startKey = edges[first].A, at = edges[first].B;
                used[first] = true;
                chain.Add((first, true));
                bool closed = false;
                while (true)
                {
                    if (at == startKey)
                    {
                        closed = true;
                        break;
                    }

                    int next = -1;
                    bool forward = true;
                    for (int i = 0; i < edges.Count; i++)
                    {
                        if (used[i]) continue;
                        if (edges[i].A == at)
                        {
                            next = i;
                            forward = true;
                            break;
                        }

                        if (edges[i].B == at)
                        {
                            next = i;
                            forward = false;
                            break;
                        }
                    }

                    if (next < 0) break;
                    used[next] = true;
                    chain.Add((next, forward));
                    at = forward ? edges[next].B : edges[next].A;
                }

                if (!closed) continue;
                var outline = new List<Vec2>();
                foreach (var (ei, fwd) in chain)
                {
                    var pts = EdgePoints(edges[ei].E, segmentsPerCircle);
                    if (!fwd) pts.Reverse();
                    for (int i = 0; i < pts.Count - 1; i++) outline.Add(pts[i]);   // the last is the next edge's first
                }

                if (outline.Count >= 3)
                {
                    if (Triangulator.SignedArea(outline) < 0) outline.Reverse();
                    loops.Add(outline);
                }
            }

            loops.Sort((p, q) => Math.Abs(Triangulator.SignedArea(q)).CompareTo(Math.Abs(Triangulator.SignedArea(p))));
            return loops;
        }

        private List<Vec2> EdgePoints(SketchEntity e, int segmentsPerCircle)
        {
            var s = Point(e.Start)!;
            var t = Point(e.End)!;
            if (e.Kind == SketchEntityKind.Line) return new List<Vec2> { new Vec2(s.X, s.Y), new Vec2(t.X, t.Y) };
            var c = Point(e.Center)!;
            double r = e.Radius;
            double a0 = Math.Atan2(s.Y - c.Y, s.X - c.X), a1 = Math.Atan2(t.Y - c.Y, t.X - c.X);
            double sweep = a1 - a0;
            while (sweep <= 1e-12) sweep += 2 * Math.PI;
            int n = Math.Max(2, (int)Math.Ceiling(segmentsPerCircle * sweep / (2 * Math.PI) - 1e-6));   // a half turn is half the segments, not one more on round-off
            var pts = new List<Vec2>();
            for (int i = 0; i <= n; i++)
            {
                double a = a0 + sweep * i / n;
                pts.Add(i == 0 ? new Vec2(s.X, s.Y) : i == n ? new Vec2(t.X, t.Y) : new Vec2(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)));
            }

            return pts;
        }

        /// <summary>The sketch as an extrusion profile: its largest loop the outline, loops inside it holes.</summary>
        public Sketch2D? ToProfile(int segmentsPerCircle = 64)
        {
            var loops = Loops(segmentsPerCircle);
            if (loops.Count == 0) return null;
            var outer = loops[0];
            var sketch = new Sketch2D { Outer = Custom(outer) };
            foreach (var loop in loops.Skip(1))
                if (Inside(loop[0], outer)) sketch.Holes.Add(Custom(loop));
            return sketch;
        }

        /// <summary>Even–odd test of a point against a closed outline.</summary>
        private static bool Inside(Vec2 p, IReadOnlyList<Vec2> ring)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[i];
                var b = ring[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }

            return inside;
        }

        private static SketchShape Custom(List<Vec2> loop)
        {
            var pts = new List<double>(loop.Count * 2);
            foreach (var p in loop)
            {
                pts.Add(p.X);
                pts.Add(p.Y);
            }

            return new SketchShape { Kind = SketchShapeKind.Custom, Points = pts };
        }
    }
}
