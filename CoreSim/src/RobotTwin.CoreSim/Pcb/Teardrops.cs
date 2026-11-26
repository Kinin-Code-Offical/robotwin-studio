using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>
    /// [teardrop] KiCad's and Altium's teardrops: where a narrow track meets a via or a pad, copper widens from the pad
    /// to the track so a drill that wanders, an etch that eats in or a board that flexes does not cut the joint. They
    /// are not stored: every place that needs copper (drawing, Gerber, zone filling) asks <see cref="Teardrops.Of"/>,
    /// so they always follow the tracks as they are.
    /// </summary>
    public sealed class TeardropSettings
    {
        public bool Enabled { get; set; }

        /// <summary>Length beyond the pad's edge, as a share of the pad's size along the track (KiCad: 50 %).</summary>
        public double LengthRatio { get; set; } = 0.5;

        /// <summary>Longest a teardrop gets (mm).</summary>
        public double MaxLength { get; set; } = 1.0;

        /// <summary>Width at the pad, as a share of the pad's size across the track (KiCad: 100 %).</summary>
        public double WidthRatio { get; set; } = 1.0;

        /// <summary>Widest a teardrop gets at the pad (mm).</summary>
        public double MaxWidth { get; set; } = 2.0;

        /// <summary>Curved (concave) sides, as KiCad's default; straight sides otherwise.</summary>
        public bool Curved { get; set; } = true;

        public bool OnVias { get; set; } = true;
        public bool OnPads { get; set; } = true;
    }

    public sealed class Teardrop
    {
        public string Net { get; set; } = string.Empty;
        public CopperLayer Layer { get; set; }
        public List<PcbPoint> Outline { get; set; } = new List<PcbPoint>();
    }

    public static class Teardrops
    {
        /// <summary>The board's teardrops as copper polygons (none when switched off), each kept clear of other nets.</summary>
        public static List<Teardrop> Of(BoardDocument board)
        {
            var result = new List<Teardrop>();
            var s = board.Teardrops;
            if (s == null || !s.Enabled) return result;
            var pads = board.Pads().ToList();
            int layers = board.LayerCount;
            foreach (var t in board.Tracks)
            {
                if (t.Length < 1e-6) continue;
                foreach (var (ex, ey, ox, oy) in new[] { (t.X1, t.Y1, t.X2, t.Y2), (t.X2, t.Y2, t.X1, t.Y1) })
                {
                    var target = Target(board, pads, s, t, ex, ey, layers);
                    if (target == null) continue;
                    var drop = Build(board, pads, s, t, target.Value, ex, ey, ox, oy, layers);
                    if (drop != null) result.Add(drop);
                }
            }

            return result;
        }

        /// <summary>A via or a pad of the track's net under one end: its centre, sizes (along x / y) and whether it is round.</summary>
        private static (double X, double Y, double W, double H, bool Round, bool Oval)? Target(BoardDocument board, List<PlacedPad> pads, TeardropSettings s, Track t, double x, double y, int layers)
        {
            if (s.OnVias)
                foreach (var v in board.Vias)
                    if (Same(v.Net, t.Net) && v.Spans(t.Layer, layers) && Dist(v.X, v.Y, x, y) <= v.Diameter / 2 + 1e-6)
                        return (v.X, v.Y, v.Diameter, v.Diameter, true, false);
            if (s.OnPads)
                foreach (var p in pads)
                    if (Same(p.Net, t.Net) && p.OnLayer(t.Layer) && Math.Abs(x - p.X) <= p.Width / 2 + 1e-6 && Math.Abs(y - p.Y) <= p.Height / 2 + 1e-6)
                        return (p.X, p.Y, p.Width, p.Height, p.Pad.Shape == PadShape.Circle, p.Pad.Shape == PadShape.Oval);
            return null;
        }

        private static Teardrop? Build(BoardDocument board, List<PlacedPad> pads, TeardropSettings s, Track t,
            (double X, double Y, double W, double H, bool Round, bool Oval) target, double ex, double ey, double ox, double oy, int layers)
        {
            var pad = (target.X, target.Y, target.W, target.H, target.Round);
            if (target.Oval && Math.Abs(target.W - target.H) > 1e-9)
            {
                // an oval is a stadium: where the track leaves through a rounded end, that end's circle is the pad there
                bool tall = target.H > target.W;
                double half = Math.Abs(target.H - target.W) / 2, rad = Math.Min(target.W, target.H) / 2;
                double ux = ox - ex, uy = oy - ey, ul = Math.Sqrt(ux * ux + uy * uy);
                if (ul > 1e-9)
                {
                    ux /= ul;
                    uy /= ul;
                    double tx = Math.Abs(ux) > 1e-9 ? ((ux > 0 ? target.X + target.W / 2 : target.X - target.W / 2) - ex) / ux : double.MaxValue;
                    double ty = Math.Abs(uy) > 1e-9 ? ((uy > 0 ? target.Y + target.H / 2 : target.Y - target.H / 2) - ey) / uy : double.MaxValue;
                    double t0 = Math.Min(tx, ty), px = ex + ux * t0, py = ey + uy * t0;
                    double off = tall ? py - target.Y : px - target.X;
                    if (Math.Abs(off) > half)
                    {
                        double cx = tall ? target.X : target.X + Math.Sign(off) * half, cy = tall ? target.Y + Math.Sign(off) * half : target.Y;
                        pad = (cx, cy, 2 * rad, 2 * rad, true);
                    }
                }
            }

            // the teardrop lies along the track itself (which may enter the pad off its centre), from where it ends
            double dx = ox - ex, dy = oy - ey, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) return null;
            dx /= len;
            dy /= len;
            double nx = -dy, ny = dx;
            double sc = (pad.X - ex) * dx + (pad.Y - ey) * dy, qc = (pad.X - ex) * nx + (pad.Y - ey) * ny;   // the pad's centre in (along, across)
            double r = pad.W / 2, hx = pad.W / 2, hy = pad.H / 2;
            double edge, along, across, acrossHalf;
            if (pad.Round)
            {
                if (Math.Abs(qc) >= r) return null;
                edge = sc + Math.Sqrt(r * r - qc * qc);   // where the track leaves the circle
                along = across = 2 * r;
                acrossHalf = r;
            }
            else
            {
                double tx = Math.Abs(dx) > 1e-9 ? ((dx > 0 ? pad.X + hx : pad.X - hx) - ex) / dx : double.MaxValue;
                double ty = Math.Abs(dy) > 1e-9 ? ((dy > 0 ? pad.Y + hy : pad.Y - hy) - ey) / dy : double.MaxValue;
                edge = Math.Min(tx, ty);
                along = 2 * Math.Min(Math.Abs(dx) > 1e-9 ? hx / Math.Abs(dx) : double.MaxValue, Math.Abs(dy) > 1e-9 ? hy / Math.Abs(dy) : double.MaxValue);
                across = Math.Abs(dx) >= Math.Abs(dy) ? pad.H : pad.W;
                acrossHalf = across / 2;
            }

            if (edge <= 0 || t.Width >= 0.9 * across) return null;   // the track is about as wide as the pad: nothing to flare
            double length = Math.Min(s.MaxLength, s.LengthRatio * along);
            length = Math.Min(length, (len - edge) * 0.9);   // never past the track's far end
            if (length < t.Width * 0.5) return null;
            // as wide as asked, but not out of the pad's sides where it starts
            double width = Math.Min(Math.Max(t.Width, Math.Min(s.MaxWidth, s.WidthRatio * across)), 2 * (acrossHalf - Math.Abs(qc)) * 0.98);
            foreach (double share in new[] { 1.0, 0.75, 0.55 })
            {
                double w = Math.Max(t.Width * 1.1, width * share);
                if (w <= t.Width * 1.05 || w > width + 1e-9) break;
                var outline = Shape(pad, ex, ey, dx, dy, nx, ny, sc, qc, edge, length, w, t.Width, s.Curved);
                if (Clear(board, pads, t, outline, layers)) return new Teardrop { Net = t.Net, Layer = t.Layer, Outline = outline };
            }

            return null;   // no width keeps it clear of the other nets: no teardrop rather than a short
        }

        /// <summary>The outline in (along, across) from the track's end: from inside the pad (width w) out to the track, counter-clockwise.</summary>
        private static List<PcbPoint> Shape((double X, double Y, double W, double H, bool Round) pad, double ex, double ey, double dx, double dy, double nx, double ny,
            double sc, double qc, double edge, double length, double w, double trackWidth, bool curved)
        {
            double tipAt = edge + length;
            // where each side starts: on a round pad on its circle, on a rectangle half-way in from the edge
            double BaseAt(double c) => pad.Round ? sc + Math.Sqrt(Math.Max(0, pad.W * pad.W / 4 - (c - qc) * (c - qc))) : edge * 0.5;
            PcbPoint At(double a, double c) => new PcbPoint(ex + dx * a + nx * c, ey + dy * a + ny * c);
            List<(double A, double C)> Side(double sign)
            {
                double b = BaseAt(sign * w / 2);
                var side = new List<(double A, double C)>();
                if (curved)
                {
                    // a quadratic flare: from the base's corner, pulled in towards the track just beyond the pad's edge
                    double qa = edge + length * 0.3;
                    for (int k = 0; k <= 12; k++)
                    {
                        double u = k / 12.0, m = 1 - u;
                        side.Add((m * m * b + 2 * m * u * qa + u * u * tipAt, sign * (m * m * (w / 2) + 2 * m * u * (trackWidth / 2) + u * u * (trackWidth / 2))));
                    }
                }
                else
                {
                    side.Add((b, sign * w / 2));
                    side.Add((tipAt, sign * trackWidth / 2));
                }

                return side;
            }

            var right = Side(-1);
            var left = Side(1);
            var outline = new List<PcbPoint>();
            foreach (var (a, c) in right) outline.Add(At(a, c));   // one side, base to tip
            for (int i = left.Count - 1; i >= 0; i--) outline.Add(At(left[i].A, left[i].C));   // the other, tip to base
            return Polygon.CounterClockwise(outline);
        }

        /// <summary>Whether the outline keeps the board's clearance to every other net's copper on its layer.</summary>
        private static bool Clear(BoardDocument board, List<PlacedPad> pads, Track t, List<PcbPoint> outline, int layers)
        {
            var b = Polygon.Bounds(outline);
            double reach = 3;   // mm around it that can matter
            bool Near(double x, double y, double grow) => x + grow + reach >= b.MinX && x - grow - reach <= b.MaxX && y + grow + reach >= b.MinY && y - grow - reach <= b.MaxY;

            foreach (var o in board.Tracks)
            {
                if (o.Layer != t.Layer || Same(o.Net, t.Net) || !Near((o.X1 + o.X2) / 2, (o.Y1 + o.Y2) / 2, o.Length / 2)) continue;
                if (SegmentToPolygon(o.X1, o.Y1, o.X2, o.Y2, outline) - o.Width / 2 < board.ClearanceBetween(t.Net, o.Net) - 1e-9) return false;
            }

            foreach (var v in board.Vias)
            {
                if (Same(v.Net, t.Net) || !v.Spans(t.Layer, layers) || !Near(v.X, v.Y, v.Diameter)) continue;
                if (SegmentToPolygon(v.X, v.Y, v.X, v.Y, outline) - v.Diameter / 2 < board.ClearanceBetween(t.Net, v.Net) - 1e-9) return false;
            }

            foreach (var p in pads)
            {
                if (Same(p.Net, t.Net) || !p.OnLayer(t.Layer) || !Near(p.X, p.Y, Math.Max(p.Width, p.Height))) continue;
                var rect = Shapes.Rect(p.X - p.Width / 2, p.Y - p.Height / 2, p.X + p.Width / 2, p.Y + p.Height / 2);
                if (PolygonToPolygon(rect, outline) < board.ClearanceBetween(t.Net, p.Net) - 1e-9) return false;
            }

            return true;
        }

        private static bool Same(string? a, string? b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.Ordinal);

        private static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

        internal static double SegmentToPolygon(double x1, double y1, double x2, double y2, IReadOnlyList<PcbPoint> poly) => CopperDistance.SegmentToPolygon(x1, y1, x2, y2, poly);

        internal static double PolygonToPolygon(IReadOnlyList<PcbPoint> a, IReadOnlyList<PcbPoint> b) => CopperDistance.PolygonToPolygon(a, b);
    }
}
