using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>Axis-aligned rectangle in sheet (grid) coordinates.</summary>
    public readonly struct GridRect
    {
        public GridRect(double minX, double minY, double maxX, double maxY)
        {
            MinX = Math.Min(minX, maxX);
            MinY = Math.Min(minY, maxY);
            MaxX = Math.Max(minX, maxX);
            MaxY = Math.Max(minY, maxY);
        }

        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public GridPoint Center => new GridPoint((MinX + MaxX) / 2, (MinY + MaxY) / 2);

        public static GridRect FromCorners(GridPoint a, GridPoint b) => new GridRect(a.X, a.Y, b.X, b.Y);

        public static GridRect FromPoint(GridPoint p) => new GridRect(p.X, p.Y, p.X, p.Y);

        private const double Eps = GridPoint.Tolerance;

        public bool Contains(GridPoint p) =>
            p.X >= MinX - Eps && p.X <= MaxX + Eps && p.Y >= MinY - Eps && p.Y <= MaxY + Eps;

        public bool Contains(GridRect r) =>
            r.MinX >= MinX - Eps && r.MaxX <= MaxX + Eps && r.MinY >= MinY - Eps && r.MaxY <= MaxY + Eps;

        public bool Intersects(GridRect r) =>
            r.MinX <= MaxX + Eps && r.MaxX >= MinX - Eps && r.MinY <= MaxY + Eps && r.MaxY >= MinY - Eps;

        public GridRect Union(GridRect r) =>
            new GridRect(Math.Min(MinX, r.MinX), Math.Min(MinY, r.MinY), Math.Max(MaxX, r.MaxX), Math.Max(MaxY, r.MaxY));

        public GridRect Union(GridPoint p) =>
            new GridRect(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));

        public GridRect Inflate(double d) => new GridRect(MinX - d, MinY - d, MaxX + d, MaxY + d);

        /// <summary>True when the segment a-b touches the rectangle (Liang–Barsky clipping).</summary>
        public bool IntersectsSegment(GridPoint a, GridPoint b)
        {
            if (Contains(a) || Contains(b)) return true;
            double t0 = 0, t1 = 1, dx = b.X - a.X, dy = b.Y - a.Y;
            bool Clip(double p, double q, ref double lo, ref double hi)
            {
                if (Math.Abs(p) < 1e-12) return q >= -Eps;
                double r = q / p;
                if (p < 0)
                {
                    if (r > hi) return false;
                    if (r > lo) lo = r;
                }
                else
                {
                    if (r < lo) return false;
                    if (r < hi) hi = r;
                }

                return true;
            }

            return Clip(-dx, a.X - MinX, ref t0, ref t1) && Clip(dx, MaxX - a.X, ref t0, ref t1) &&
                   Clip(-dy, a.Y - MinY, ref t0, ref t1) && Clip(dy, MaxY - a.Y, ref t0, ref t1) && t0 <= t1;
        }
    }

    /// <summary>How the wire tool bends a new segment towards the cursor.</summary>
    public enum WireMode
    {
        /// <summary>Horizontal and vertical segments only (KiCad's 90° mode).</summary>
        Orthogonal,

        /// <summary>A 45° diagonal plus a horizontal/vertical run.</summary>
        Diagonal45,

        /// <summary>A straight line to the cursor.</summary>
        Free
    }

    /// <summary>
    /// Editor geometry and the editing operations on a schematic that do not depend on the UI:
    /// bounds, box selection, rotate/mirror about a centre, wire routing, breaking and cleaning
    /// wires. Items are the document objects themselves (<see cref="SchematicPart"/>,
    /// <see cref="SchematicWire"/>, <see cref="NetLabel"/>, <see cref="NoConnectFlag"/>).
    /// </summary>
    public static class SchematicEditing
    {
        public static GridPoint Snap(GridPoint p, double grid = 1.0) =>
            new GridPoint(Math.Round(p.X / grid) * grid, Math.Round(p.Y / grid) * grid);

        /// <summary>Symbol body plus pin tips.</summary>
        public static GridRect PartBounds(SchematicPart part, PartDefinition? def)
        {
            if (def == null) return new GridRect(part.Position.X - 1, part.Position.Y - 1, part.Position.X + 1, part.Position.Y + 1);
            var r = GridRect.FromCorners(part.Transform(-def.HalfWidth, -def.HalfHeight), part.Transform(def.HalfWidth, def.HalfHeight));
            foreach (var pin in def.Pins) r = r.Union(part.Transform(pin.X, pin.Y));
            return r;
        }

        public static GridRect? ItemBounds(object item, PartLibrary library)
        {
            switch (item)
            {
                case SchematicPart p: return PartBounds(p, library.Find(p.PartId));
                case SchematicWire w when w.Points.Count > 0:
                    var r = GridRect.FromPoint(w.Points[0]);
                    foreach (var q in w.Points) r = r.Union(q);
                    return r;
                case NetLabel l: return GridRect.FromPoint(l.Position);
                case NoConnectFlag nc: return GridRect.FromPoint(nc.Position);
                default: return null;
            }
        }

        public static GridRect? Bounds(IEnumerable<object> items, PartLibrary library)
        {
            GridRect? result = null;
            foreach (var item in items)
            {
                var b = ItemBounds(item, library);
                if (b == null) continue;
                result = result == null ? b : result.Value.Union(b.Value);
            }

            return result;
        }

        /// <summary>Everything on the sheet, in drawing order.</summary>
        public static IEnumerable<object> AllItems(Schematic doc) =>
            doc.Parts.Cast<object>().Concat(doc.Wires).Concat(doc.Labels).Concat(doc.NoConnects);

        /// <summary>
        /// Box selection. Window mode (dragged left to right) takes items lying completely inside;
        /// crossing mode (right to left) also takes items the rectangle merely touches.
        /// </summary>
        public static List<object> ItemsInRect(Schematic doc, PartLibrary library, GridRect rect, bool crossing)
        {
            var result = new List<object>();
            foreach (var part in doc.Parts)
            {
                var b = PartBounds(part, library.Find(part.PartId));
                if (crossing ? rect.Intersects(b) : rect.Contains(b)) result.Add(part);
            }

            foreach (var wire in doc.Wires)
            {
                if (wire.Points.Count == 0) continue;
                bool hit = crossing
                    ? wire.Points.Count == 1 ? rect.Contains(wire.Points[0]) : Enumerable.Range(0, wire.Points.Count - 1).Any(i => rect.IntersectsSegment(wire.Points[i], wire.Points[i + 1]))
                    : wire.Points.All(rect.Contains);
                if (hit) result.Add(wire);
            }

            foreach (var label in doc.Labels) if (rect.Contains(label.Position)) result.Add(label);
            foreach (var nc in doc.NoConnects) if (rect.Contains(nc.Position)) result.Add(nc);
            return result;
        }

        /// <summary>
        /// Pivot for rotate/mirror: a single part turns about its anchor, a single label or flag
        /// about its point, anything else about the grid-snapped centre of its bounds.
        /// </summary>
        public static GridPoint PivotOf(IReadOnlyCollection<object> items, PartLibrary library)
        {
            if (items.Count == 1)
            {
                switch (items.First())
                {
                    case SchematicPart p: return p.Position;
                    case NetLabel l: return l.Position;
                    case NoConnectFlag nc: return nc.Position;
                }
            }

            var b = Bounds(items, library);
            return b == null ? new GridPoint(0, 0) : Snap(b.Value.Center);
        }

        /// <summary>Quarter turn counter-clockwise on screen (Y down), the same sense as <see cref="SchematicPart.Rotation"/>.</summary>
        public static GridPoint RotatePoint(GridPoint p, GridPoint center) =>
            new GridPoint(center.X + (p.Y - center.Y) + 0.0, center.Y - (p.X - center.X) + 0.0);

        public static GridPoint MirrorPoint(GridPoint p, GridPoint center, bool vertical) =>
            vertical ? new GridPoint(p.X, 2 * center.Y - p.Y + 0.0) : new GridPoint(2 * center.X - p.X + 0.0, p.Y);

        /// <summary>Rotates the items 90° about <paramref name="center"/> (counter-clockwise, or clockwise).</summary>
        public static void Rotate(IEnumerable<object> items, GridPoint center, bool clockwise = false)
        {
            var list = items.ToList();
            for (int turn = 0; turn < (clockwise ? 3 : 1); turn++)
            {
                foreach (var item in list)
                {
                    switch (item)
                    {
                        case SchematicPart p:
                            p.Position = RotatePoint(p.Position, center);
                            p.Rotation = ((p.Rotation + 90) % 360 + 360) % 360;
                            break;
                        case SchematicWire w:
                            for (int i = 0; i < w.Points.Count; i++) w.Points[i] = RotatePoint(w.Points[i], center);
                            break;
                        case NetLabel l:
                            l.Position = RotatePoint(l.Position, center);
                            break;
                        case NoConnectFlag nc:
                            nc.Position = RotatePoint(nc.Position, center);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Mirrors the items in screen space about a vertical axis through the centre (left ↔ right),
        /// or about a horizontal axis when <paramref name="vertical"/> (top ↔ bottom).
        /// A part's local mirror flips X before rotating, so a screen mirror becomes
        /// Mirror = !Mirror with the rotation negated (left/right) or 180° − rotation (top/bottom).
        /// </summary>
        public static void Mirror(IEnumerable<object> items, GridPoint center, bool vertical)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case SchematicPart p:
                        p.Position = MirrorPoint(p.Position, center, vertical);
                        p.Mirror = !p.Mirror;
                        p.Rotation = (((vertical ? 180 - p.Rotation : -p.Rotation) % 360) + 360) % 360;
                        break;
                    case SchematicWire w:
                        for (int i = 0; i < w.Points.Count; i++) w.Points[i] = MirrorPoint(w.Points[i], center, vertical);
                        break;
                    case NetLabel l:
                        l.Position = MirrorPoint(l.Position, center, vertical);
                        break;
                    case NoConnectFlag nc:
                        nc.Position = MirrorPoint(nc.Position, center, vertical);
                        break;
                }
            }
        }

        public static void Translate(IEnumerable<object> items, GridPoint delta)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case SchematicPart p: p.Position += delta; break;
                    case SchematicWire w:
                        for (int i = 0; i < w.Points.Count; i++) w.Points[i] += delta;
                        break;
                    case NetLabel l: l.Position += delta; break;
                    case NoConnectFlag nc: nc.Position += delta; break;
                }
            }
        }

        /// <summary>Removes items from the document; returns how many were removed.</summary>
        public static int Delete(Schematic doc, IEnumerable<object> items)
        {
            int n = 0;
            foreach (var item in items.ToList())
            {
                bool removed = item switch
                {
                    SchematicPart p => doc.Parts.Remove(p),
                    SchematicWire w => doc.Wires.Remove(w),
                    NetLabel l => doc.Labels.Remove(l),
                    NoConnectFlag nc => doc.NoConnects.Remove(nc),
                    _ => false
                };
                if (removed) n++;
            }

            return n;
        }

        // ------------------------------------------------------------- wires

        /// <summary>
        /// The points a new wire leg adds when going from <paramref name="from"/> to <paramref name="to"/>
        /// (excluding <paramref name="from"/>). <paramref name="alternate"/> is KiCad's "/" posture switch:
        /// vertical first instead of horizontal first (or straight run before the diagonal in 45° mode).
        /// </summary>
        public static List<GridPoint> Route(GridPoint from, GridPoint to, WireMode mode, bool alternate)
        {
            var pts = new List<GridPoint>();
            double dx = to.X - from.X, dy = to.Y - from.Y;
            bool straight = Math.Abs(dx) < GridPoint.Tolerance || Math.Abs(dy) < GridPoint.Tolerance;
            if (mode == WireMode.Free || straight || (mode == WireMode.Diagonal45 && Math.Abs(Math.Abs(dx) - Math.Abs(dy)) < GridPoint.Tolerance))
            {
                pts.Add(to);
                return pts;
            }

            if (mode == WireMode.Orthogonal)
            {
                pts.Add(alternate ? new GridPoint(from.X, to.Y) : new GridPoint(to.X, from.Y));
                pts.Add(to);
                return pts;
            }

            // 45°: one diagonal of length min(|dx|,|dy|) and a straight remainder.
            double d = Math.Min(Math.Abs(dx), Math.Abs(dy));
            double sx = Math.Sign(dx), sy = Math.Sign(dy);
            if (!alternate)
            {
                pts.Add(new GridPoint(from.X + sx * d, from.Y + sy * d));
            }
            else
            {
                pts.Add(new GridPoint(to.X - sx * d, to.Y - sy * d));
            }

            pts.Add(to);
            return pts;
        }

        /// <summary>Drops repeated points and interior points that lie on a straight run.</summary>
        public static List<GridPoint> CleanPolyline(IEnumerable<GridPoint> points)
        {
            var clean = new List<GridPoint>();
            foreach (var p in points)
            {
                if (clean.Count > 0 && clean[^1].Equals(p)) continue;
                if (clean.Count >= 2 && Collinear(clean[^2], clean[^1], p) && Between(clean[^2], clean[^1], p))
                {
                    clean[^1] = p;
                    continue;
                }

                clean.Add(p);
            }

            return clean;
        }

        private static bool Collinear(GridPoint a, GridPoint b, GridPoint c) =>
            Math.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) < 1e-6;

        /// <summary>b lies between a and c (so dropping b does not fold the wire back on itself).</summary>
        private static bool Between(GridPoint a, GridPoint b, GridPoint c) =>
            (b.X - a.X) * (c.X - b.X) + (b.Y - a.Y) * (c.Y - b.Y) >= -1e-9;

        public static double DistanceToSegment(GridPoint p, GridPoint a, GridPoint b, out GridPoint closest)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2)) : 0;
            closest = new GridPoint(a.X + dx * t, a.Y + dy * t);
            return Math.Sqrt((closest.X - p.X) * (closest.X - p.X) + (closest.Y - p.Y) * (closest.Y - p.Y));
        }

        /// <summary>Index of the segment of <paramref name="wire"/> closest to p within tolerance, or -1.</summary>
        public static int HitSegment(SchematicWire wire, GridPoint p, double tolerance)
        {
            int best = -1;
            double bestD = tolerance;
            for (int i = 0; i + 1 < wire.Points.Count; i++)
            {
                double d = DistanceToSegment(p, wire.Points[i], wire.Points[i + 1], out _);
                if (d <= bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>True when p lies strictly inside segment a-b.</summary>
        public static bool OnSegmentInterior(GridPoint p, GridPoint a, GridPoint b) =>
            !p.Equals(a) && !p.Equals(b) && DistanceToSegment(p, a, b, out _) < GridPoint.Tolerance;

        /// <summary>
        /// Splits a wire at a point on it (KiCad "Break Wire"). Returns the new second half, or
        /// null when the point is not inside the wire (an end point or off the wire).
        /// </summary>
        public static SchematicWire? BreakWire(Schematic doc, SchematicWire wire, GridPoint at)
        {
            for (int i = 0; i + 1 < wire.Points.Count; i++)
            {
                var a = wire.Points[i];
                var b = wire.Points[i + 1];
                bool interiorVertex = i > 0 && a.Equals(at);
                if (!interiorVertex && !OnSegmentInterior(at, a, b)) continue;
                var first = wire.Points.Take(i + 1).ToList();
                if (!interiorVertex) first.Add(at);
                var second = new List<GridPoint> { at };
                second.AddRange(wire.Points.Skip(i + 1));
                wire.Points = first;
                var tail = new SchematicWire { Points = second };
                int index = doc.Wires.IndexOf(wire);
                doc.Wires.Insert(index < 0 ? doc.Wires.Count : index + 1, tail);
                return tail;
            }

            return null;
        }

        /// <summary>Removes one segment of a wire; the pieces left on either side stay as separate wires.</summary>
        public static bool DeleteSegment(Schematic doc, SchematicWire wire, int segment)
        {
            if (segment < 0 || segment + 1 >= wire.Points.Count) return false;
            var head = wire.Points.Take(segment + 1).ToList();
            var tail = wire.Points.Skip(segment + 1).ToList();
            int index = doc.Wires.IndexOf(wire);
            if (index < 0) return false;
            doc.Wires.RemoveAt(index);
            if (tail.Count >= 2) doc.Wires.Insert(index, new SchematicWire { Points = tail });
            if (head.Count >= 2)
            {
                wire.Points = head;
                doc.Wires.Insert(index, wire);
            }

            return true;
        }

        /// <summary>Pin tips of every part, keyed by position.</summary>
        public static HashSet<GridPoint> PinTips(Schematic doc, PartLibrary library, IEnumerable<SchematicPart>? only = null)
        {
            var set = new HashSet<GridPoint>();
            foreach (var part in only ?? doc.Parts)
            {
                var def = library.Find(part.PartId);
                if (def == null) continue;
                foreach (var pin in def.Pins) set.Add(part.Transform(pin.X, pin.Y));
            }

            return set;
        }

        /// <summary>
        /// True when something electrical sits at p other than <paramref name="exclude"/>:
        /// a pin tip, a label, a vertex of another wire or the inside of another wire's segment.
        /// </summary>
        public static bool IsConnectionPoint(Schematic doc, PartLibrary library, GridPoint p, SchematicWire? exclude = null)
        {
            if (PinTips(doc, library).Contains(p)) return true;
            if (doc.Labels.Any(l => l.Position.Equals(p))) return true;
            foreach (var w in doc.Wires)
            {
                if (ReferenceEquals(w, exclude)) continue;
                for (int i = 0; i < w.Points.Count; i++)
                {
                    if (w.Points[i].Equals(p)) return true;
                    if (i + 1 < w.Points.Count && OnSegmentInterior(p, w.Points[i], w.Points[i + 1])) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Wire ends that are not attached to anything (no pin, label, no-connect, other wire,
        /// and not the other end of the same wire). Used by ERC and for the dangling-end marks.
        /// </summary>
        public static List<(SchematicWire Wire, GridPoint End)> DanglingEnds(Schematic doc, PartLibrary library)
        {
            var tips = PinTips(doc, library);
            var result = new List<(SchematicWire, GridPoint)>();
            foreach (var w in doc.Wires)
            {
                if (w.Points.Count < 2) continue;
                foreach (var end in new[] { w.Points[0], w.Points[^1] })
                {
                    if (tips.Contains(end) || doc.Labels.Any(l => l.Position.Equals(end)) || doc.NoConnects.Any(n => n.Position.Equals(end))) continue;
                    bool joined = false;
                    foreach (var other in doc.Wires)
                    {
                        if (ReferenceEquals(other, w)) continue;
                        for (int i = 0; i < other.Points.Count && !joined; i++)
                        {
                            if (other.Points[i].Equals(end)) joined = true;
                            else if (i + 1 < other.Points.Count && OnSegmentInterior(end, other.Points[i], other.Points[i + 1])) joined = true;
                        }

                        if (joined) break;
                    }

                    // A T onto its own interior counts too (a loop back onto itself).
                    if (!joined)
                    {
                        for (int i = 0; i + 1 < w.Points.Count && !joined; i++)
                        {
                            if (OnSegmentInterior(end, w.Points[i], w.Points[i + 1])) joined = true;
                        }
                    }

                    if (!joined) result.Add((w, end));
                }
            }

            return result;
        }
    }

    /// <summary>
    /// A move or drag of document items, applied from the original positions on every update
    /// so the result never accumulates error. With rubber-banding (KiCad's "G" drag and mouse
    /// drag) wires attached to moving pins, wire vertices and labels stretch along and stay
    /// horizontal/vertical; without it (KiCad's "M" move) only the items themselves move.
    /// </summary>
    public sealed class SchematicDrag
    {
        private readonly Dictionary<SchematicPart, GridPoint> _parts = new Dictionary<SchematicPart, GridPoint>();
        private readonly Dictionary<NetLabel, GridPoint> _labels = new Dictionary<NetLabel, GridPoint>();
        private readonly Dictionary<NoConnectFlag, GridPoint> _flags = new Dictionary<NoConnectFlag, GridPoint>();
        private readonly Dictionary<SchematicWire, List<GridPoint>> _wires = new Dictionary<SchematicWire, List<GridPoint>>();
        private readonly List<WireStretch> _stretch = new List<WireStretch>();
        private readonly HashSet<GridPoint> _fixedPoints;

        public SchematicDrag(Schematic doc, PartLibrary library, IEnumerable<object> items, bool rubberBand)
        {
            var moving = new HashSet<object>(items);
            foreach (var item in moving) Remember(item);

            if (rubberBand)
            {
                var anchors = SchematicEditing.PinTips(doc, library, moving.OfType<SchematicPart>());
                foreach (var w in moving.OfType<SchematicWire>()) foreach (var p in w.Points) anchors.Add(p);
                foreach (var l in moving.OfType<NetLabel>()) anchors.Add(l.Position);

                // Labels and no-connect flags sitting on a moving point travel with it.
                foreach (var l in doc.Labels) if (!moving.Contains(l) && anchors.Contains(l.Position)) Remember(l);
                foreach (var nc in doc.NoConnects) if (!moving.Contains(nc) && anchors.Contains(nc.Position)) Remember(nc);

                var movingSegments = moving.OfType<SchematicWire>()
                    .SelectMany(w => Enumerable.Range(0, Math.Max(0, w.Points.Count - 1)).Select(i => (w.Points[i], w.Points[i + 1])))
                    .ToList();
                bool Attached(GridPoint p) => anchors.Contains(p) || movingSegments.Any(s => SchematicEditing.OnSegmentInterior(p, s.Item1, s.Item2));

                foreach (var w in doc.Wires)
                {
                    if (moving.Contains(w) || w.Points.Count < 2) continue;
                    bool start = Attached(w.Points[0]), end = Attached(w.Points[^1]);
                    if (start && end) Remember(w);
                    else if (start || end) _stretch.Add(new WireStretch(w, start, end));
                }
            }

            // Points other (non-moving) things are attached to: never slide those.
            _fixedPoints = SchematicEditing.PinTips(doc, library, doc.Parts.Where(p => !_parts.ContainsKey(p)));
            foreach (var l in doc.Labels) if (!_labels.ContainsKey(l)) _fixedPoints.Add(l.Position);
            foreach (var w in doc.Wires)
            {
                if (_wires.ContainsKey(w) || w.Points.Count == 0) continue;
                _fixedPoints.Add(w.Points[0]);
                _fixedPoints.Add(w.Points[^1]);
            }
        }

        /// <summary>Everything that moves rigidly (selection plus attached labels and fully attached wires).</summary>
        public IEnumerable<object> RigidItems => _parts.Keys.Cast<object>().Concat(_wires.Keys).Concat(_labels.Keys).Concat(_flags.Keys);

        public IEnumerable<SchematicWire> StretchedWires => _stretch.Select(s => s.Wire);

        private void Remember(object item)
        {
            switch (item)
            {
                case SchematicPart p: _parts[p] = p.Position; break;
                case NetLabel l: _labels[l] = l.Position; break;
                case NoConnectFlag nc: _flags[nc] = nc.Position; break;
                case SchematicWire w: _wires[w] = new List<GridPoint>(w.Points); break;
            }
        }

        public void Apply(GridPoint delta)
        {
            foreach (var kv in _parts) kv.Key.Position = kv.Value + delta;
            foreach (var kv in _labels) kv.Key.Position = kv.Value + delta;
            foreach (var kv in _flags) kv.Key.Position = kv.Value + delta;
            foreach (var kv in _wires) kv.Key.Points = kv.Value.Select(p => p + delta).ToList();
            foreach (var s in _stretch) s.Apply(delta, _fixedPoints);
        }

        /// <summary>Puts everything back where it was.</summary>
        public void Cancel()
        {
            Apply(new GridPoint(0, 0));
            foreach (var s in _stretch) s.Wire.Points = new List<GridPoint>(s.Original);
        }

        private sealed class WireStretch
        {
            public WireStretch(SchematicWire wire, bool start, bool end)
            {
                Wire = wire;
                Original = new List<GridPoint>(wire.Points);
                MoveStart = start;
                MoveEnd = end;
            }

            public SchematicWire Wire { get; }
            public List<GridPoint> Original { get; }
            public bool MoveStart { get; }
            public bool MoveEnd { get; }

            public void Apply(GridPoint delta, HashSet<GridPoint> fixedPoints)
            {
                if (delta.Equals(new GridPoint(0, 0)))
                {
                    Wire.Points = new List<GridPoint>(Original);
                    return;
                }

                var pts = new List<GridPoint>(Original);
                if (MoveEnd) pts = StretchEnd(pts, delta, fixedPoints);
                if (MoveStart)
                {
                    pts.Reverse();
                    pts = StretchEnd(pts, delta, fixedPoints);
                    pts.Reverse();
                }

                var clean = SchematicEditing.CleanPolyline(pts);
                Wire.Points = clean.Count >= 2 ? clean : pts;
            }

            /// <summary>
            /// Moves the last point by delta. The last segment keeps its direction: if the one before
            /// it is perpendicular and its corner is free, the corner slides along; otherwise an elbow is inserted.
            /// </summary>
            private static List<GridPoint> StretchEnd(List<GridPoint> pts, GridPoint delta, HashSet<GridPoint> fixedPoints)
            {
                int n = pts.Count;
                var oldEnd = pts[n - 1];
                var end = oldEnd + delta;
                var prev = pts[n - 2];
                bool horizontal = Math.Abs(prev.Y - oldEnd.Y) < GridPoint.Tolerance && Math.Abs(prev.X - oldEnd.X) > GridPoint.Tolerance;
                bool vertical = Math.Abs(prev.X - oldEnd.X) < GridPoint.Tolerance && Math.Abs(prev.Y - oldEnd.Y) > GridPoint.Tolerance;
                pts[n - 1] = end;
                if (!horizontal && !vertical) return pts;

                if (n >= 3 && !fixedPoints.Contains(prev))
                {
                    var before = pts[n - 3];
                    bool perpendicular = horizontal ? Math.Abs(before.X - prev.X) < GridPoint.Tolerance : Math.Abs(before.Y - prev.Y) < GridPoint.Tolerance;
                    if (perpendicular)
                    {
                        pts[n - 2] = horizontal ? new GridPoint(prev.X, end.Y) : new GridPoint(end.X, prev.Y);
                        return pts;
                    }
                }

                bool aligned = horizontal ? Math.Abs(prev.Y - end.Y) < GridPoint.Tolerance : Math.Abs(prev.X - end.X) < GridPoint.Tolerance;
                if (aligned) return pts;

                // Bend in the middle of the run (a Z), so both ends keep their direction and the new
                // leg does not run along the fixed end's pin row.
                double mid = horizontal ? Math.Round((prev.X + end.X) / 2) : Math.Round((prev.Y + end.Y) / 2);
                bool roomy = horizontal ? Math.Abs(end.X - prev.X) >= 2 : Math.Abs(end.Y - prev.Y) >= 2;
                if (roomy)
                {
                    pts.Insert(n - 1, horizontal ? new GridPoint(mid, prev.Y) : new GridPoint(prev.X, mid));
                    pts.Insert(n, horizontal ? new GridPoint(mid, end.Y) : new GridPoint(end.X, mid));
                }
                else
                {
                    pts.Insert(n - 1, horizontal ? new GridPoint(prev.X, end.Y) : new GridPoint(end.X, prev.Y));
                }

                return pts;
            }
        }
    }

    /// <summary>
    /// Dragging one segment of a wire (KiCad drag on a wire): horizontal segments move up/down,
    /// vertical ones left/right; ends that are attached to something stay put and get a new
    /// leg, and other wires attached at the moved corners stretch along.
    /// </summary>
    public sealed class SegmentDrag
    {
        private readonly SchematicWire _wire;
        private readonly List<GridPoint> _original;
        private readonly int _segment;
        private readonly bool _startAttached, _endAttached;
        private readonly List<(SchematicWire Wire, List<GridPoint> Original, bool Start, bool End, GridPoint Corner)> _others =
            new List<(SchematicWire, List<GridPoint>, bool, bool, GridPoint)>();

        public SegmentDrag(Schematic doc, PartLibrary library, SchematicWire wire, int segment)
        {
            _wire = wire;
            _segment = segment;
            _original = new List<GridPoint>(wire.Points);
            var a = _original[segment];
            var b = _original[segment + 1];
            _startAttached = segment == 0 && SchematicEditing.IsConnectionPoint(doc, library, a, wire);
            _endAttached = segment + 2 == _original.Count && SchematicEditing.IsConnectionPoint(doc, library, b, wire);
            foreach (var other in doc.Wires)
            {
                if (ReferenceEquals(other, wire) || other.Points.Count < 2) continue;
                foreach (var corner in new[] { a, b })
                {
                    bool movesCorner = (corner.Equals(a) && !(_startAttached && segment == 0)) || (corner.Equals(b) && !(_endAttached && segment + 2 == _original.Count));
                    if (!movesCorner) continue;
                    bool s = other.Points[0].Equals(corner), e = other.Points[^1].Equals(corner);
                    if (s || e) _others.Add((other, new List<GridPoint>(other.Points), s, e, corner));
                }
            }
        }

        /// <summary>The delta actually applied (constrained perpendicular to the segment).</summary>
        public GridPoint Constrain(GridPoint delta)
        {
            var a = _original[_segment];
            var b = _original[_segment + 1];
            if (Math.Abs(a.Y - b.Y) < GridPoint.Tolerance) return new GridPoint(0, delta.Y);
            if (Math.Abs(a.X - b.X) < GridPoint.Tolerance) return new GridPoint(delta.X, 0);
            return delta;
        }

        public void Apply(GridPoint rawDelta)
        {
            var d = Constrain(rawDelta);
            var pts = new List<GridPoint>();
            for (int i = 0; i < _original.Count; i++)
            {
                bool moves = i == _segment || i == _segment + 1;
                if (!moves)
                {
                    pts.Add(_original[i]);
                    continue;
                }

                if (i == 0 && _startAttached)
                {
                    pts.Add(_original[i]);
                    pts.Add(_original[i] + d);
                }
                else if (i == _original.Count - 1 && _endAttached)
                {
                    pts.Add(_original[i] + d);
                    pts.Add(_original[i]);
                }
                else
                {
                    pts.Add(_original[i] + d);
                }
            }

            var clean = SchematicEditing.CleanPolyline(pts);
            _wire.Points = clean.Count >= 2 ? clean : pts;

            foreach (var (other, original, s, e, _) in _others)
            {
                var o = new List<GridPoint>(original);
                if (s) o.Insert(1, o[0]);
                if (e) o.Insert(o.Count - 1, o[^1]);
                if (s) o[0] = o[0] + d;
                if (e) o[^1] = o[^1] + d;
                var c = SchematicEditing.CleanPolyline(o);
                other.Points = c.Count >= 2 ? c : o;
            }
        }

        public void Cancel()
        {
            _wire.Points = new List<GridPoint>(_original);
            foreach (var (other, original, _, _, _) in _others) other.Points = new List<GridPoint>(original);
        }
    }
}
