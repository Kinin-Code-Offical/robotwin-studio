using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Pcb;
using Geometry = RobotTwin.CoreSim.Pcb.Geometry;

namespace RobotWin.Studio.Pcb
{
    /// <summary>[magnet] KiCad's Magnetic items: when the pointer is snapped to a pad, a track or a drawing.</summary>
    public enum MagnetMode
    {
        Never,

        /// <summary>Only while routing or drawing (KiCad's "when creating tracks").</summary>
        Creating,

        Always
    }

    /// <summary>
    /// [magnet] KiCad's Magnetic items: within a few pixels the pointer goes to a pad's, via's or hole's centre, a track's end
    /// or its centre line, a drawing's or the edge's corners and centres, before the grid. Each kind can be off; a small
    /// diamond shows where it went. The drawing tools, zones and the edge, the ruler and dimensions use it.
    /// </summary>
    public partial class BoardCanvas
    {
        public MagnetMode MagnetPads { get; set; } = MagnetMode.Always;
        public MagnetMode MagnetTracks { get; set; } = MagnetMode.Creating;
        public bool MagnetGraphics { get; set; } = true;

        private PcbPoint? _magnetMark;

        /// <summary>The pointer snapped: to an item within reach, else to the grid. <paramref name="creating"/>: a tool is drawing.</summary>
        private PcbPoint Magnet(double x, double y, bool creating = true)
        {
            _magnetMark = null;
            var board = Board;
            if (board == null) return new PcbPoint(Snap(x), Snap(y));
            double reach = 8 / _scale;
            bool On(MagnetMode m) => m == MagnetMode.Always || (m == MagnetMode.Creating && creating);
            PcbPoint Mark(double px, double py)
            {
                var p = new PcbPoint(px, py);
                _magnetMark = p;
                return p;
            }

            if (On(MagnetPads))
            {
                var pad = HitPad(x, y);
                if (pad != null) return Mark(pad.Value.X, pad.Value.Y);
                var via = HitVia(x, y);
                if (via != null) return Mark(via.X, via.Y);
                var hole = HitHole(x, y);
                if (hole != null) return Mark(hole.X, hole.Y);
            }

            double best = reach;
            PcbPoint? found = null;
            void Try(double px, double py)
            {
                double d = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d > best) return;
                best = d;
                found = new PcbPoint(px, py);
            }

            if (On(MagnetTracks))
            {
                foreach (var t in board.Tracks.Where(t => Visible(t.Layer)))
                {
                    Try(t.X1, t.Y1);
                    Try(t.X2, t.Y2);
                }

                if (found == null)   // no end near: the nearest point of a track's centre line
                {
                    foreach (var t in board.Tracks.Where(t => Visible(t.Layer)))
                    {
                        double dx = t.X2 - t.X1, dy = t.Y2 - t.Y1, len2 = dx * dx + dy * dy;
                        double k = len2 > 0 ? Math.Max(0, Math.Min(1, ((x - t.X1) * dx + (y - t.Y1) * dy) / len2)) : 0;
                        if (Geometry.PointSegmentDistance(x, y, t.X1, t.Y1, t.X2, t.Y2) <= Math.Max(reach, t.Width / 2)) Try(t.X1 + dx * k, t.Y1 + dy * k);
                    }
                }
            }

            if (MagnetGraphics && found == null)
            {
                foreach (var e in board.OutlineElements())
                {
                    Try(e.X1, e.Y1);
                    Try(e.X2, e.Y2);
                    if (e.IsArc) Try(e.CX, e.CY);
                }

                foreach (var g in board.Graphics)
                    foreach (var p in g.Points)
                        Try(p.X, p.Y);
                foreach (var d in board.Dimensions)
                {
                    Try(d.X1, d.Y1);
                    Try(d.X2, d.Y2);
                }
            }

            return found is { } f ? Mark(f.X, f.Y) : new PcbPoint(Snap(x), Snap(y));
        }

        /// <summary>Snapshot: a line started at one point and the pointer at another, caught by the magnet.</summary>
        public void SimulateDrawForSnapshot(double x0, double y0, double x1, double y1)
        {
            SetTool(PcbTool.Line);
            GraphicClick(x0, y0, false);
            _cursor = (x1, y1);
            Magnet(x1, y1);
            InvalidateVisual();
        }

        /// <summary>The diamond where the pointer was caught (KiCad draws it at the magnet's point).</summary>
        private void DrawMagnetMark(DrawingContext dc)
        {
            if (_magnetMark is not { } m || _tool is PcbTool.Select or PcbTool.Route or PcbTool.DiffPair or PcbTool.Tune) return;
            var c = S(m.X, m.Y);
            const double r = 5;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X, c.Y - r), false, true);
                ctx.LineTo(new Point(c.X + r, c.Y), true, false);
                ctx.LineTo(new Point(c.X, c.Y + r), true, false);
                ctx.LineTo(new Point(c.X - r, c.Y), true, false);
            }

            g.Freeze();
            dc.DrawGeometry(null, P(Accent, 1.4), g);
        }
    }
}
