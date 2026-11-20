using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Editor;

namespace RobotWin.Studio.Pcb
{
    /// <summary>
    /// Hand-routing assists (as in KiCad's interactive router): start a route by clicking its
    /// ratsnest line, F finishes to the nearest pad of the same net around obstacles, target pads
    /// of the net are ringed while routing, and the route's length and the net's total routed
    /// length are shown live next to the cursor.
    /// </summary>
    public sealed partial class BoardCanvas
    {
        /// <summary>F while routing: completes the route to the nearest reachable pad of the same net.</summary>
        private bool FinishToTarget()
        {
            if (_router == null || Board == null || _routePts.Count == 0) return false;
            if (_routeNet.Length == 0)
            {
                Say("Hattı olmayan bir yolun hedefi yok; bir pede tıklayarak bitirin.");
                return true;
            }

            var head = _routePts[_routePts.Count - 1].P;
            var candidates = Board.Pads()
                .Where(p => string.Equals(p.Net, _routeNet, StringComparison.OrdinalIgnoreCase))
                .Where(p => _routeStartPad == null || p.Component != _routeStartPad.Value.Component || p.Pad != _routeStartPad.Value.Pad)
                .Where(p => p.Pad.IsThroughHole || p.SmdLayer == _routeLayer)
                .OrderBy(p => (p.X - head.X) * (p.X - head.X) + (p.Y - head.Y) * (p.Y - head.Y))
                .Take(8)
                .ToList();
            foreach (var p in candidates)
            {
                var path = _router.Walkaround(_routeLayer, head, new PcbPoint(p.X, p.Y), _diagonalFirst, _only90);
                var end = path[path.Count - 1];
                if (Math.Abs(end.X - p.X) > 1e-6 || Math.Abs(end.Y - p.Y) > 1e-6) continue;
                _preview = path;
                CommitPreview();
                FinishRoute(p);
                Say($"{_routeNet}: {p.Component.RefDes}.{p.Pad.Name} pedine bağlandı.");
                return true;
            }

            Say(candidates.Count == 0
                ? "Bu katmanda aynı hatta ped yok: V ile via koyup katman değiştirin."
                : "Aynı hattın pedlerine bu katmanda engelsiz yol bulunamadı: bir ara noktaya tıklayın veya V ile katman değiştirin.");
            return true;
        }

        /// <summary>Click near a ratsnest line with the route tool: starts routing that connection from its nearer pad.</summary>
        private bool StartFromRatsnest(double x, double y)
        {
            if (Board == null) return false;
            const double pick = 6;   // px
            (string Net, PlacedPad A, PlacedPad B)? best = null;
            double bestD = double.MaxValue;
            foreach (var r in Board.Ratsnest())
            {
                var a = S(r.A.X, r.A.Y);
                var b = S(r.B.X, r.B.Y);
                var c = S(x, y);
                double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
                double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((c.X - a.X) * dx + (c.Y - a.Y) * dy) / len2)) : 0;
                double d = Math.Sqrt(Math.Pow(a.X + dx * t - c.X, 2) + Math.Pow(a.Y + dy * t - c.Y, 2));
                if (d < bestD)
                {
                    bestD = d;
                    best = r;
                }
            }

            if (best == null || bestD > pick) return false;
            var (net, pa, pb) = best.Value;
            double da = (pa.X - x) * (pa.X - x) + (pa.Y - y) * (pa.Y - y), db = (pb.X - x) * (pb.X - x) + (pb.Y - y) * (pb.Y - y);
            var from = da <= db ? pa : pb;
            var to = da <= db ? pb : pa;
            RouteClick(from.X, from.Y, false);
            if (_routePts.Count == 0) return true;
            _cursor = (to.X, to.Y);
            UpdateRoutePreview();
            Say($"{net}: {from.Component.RefDes}.{from.Pad.Name} → {to.Component.RefDes}.{to.Pad.Name} · tıklayarak ilerleyin, F ile hedefe bitirin");
            InvalidateVisual();
            return true;
        }

        /// <summary>Length of the route so far plus the preview (mm), and the net's routed length on the board.</summary>
        private (double Route, double Net) RouteLengths()
        {
            double route = 0;
            for (int i = 1; i < _routePts.Count; i++)
            {
                if (_routePts[i - 1].Layer != _routePts[i].Layer) continue;
                route += Dist(_routePts[i - 1].P, _routePts[i].P);
            }

            for (int i = 1; i < _preview.Count; i++) route += Dist(_preview[i - 1], _preview[i]);
            double net = 0;
            if (Board != null && _routeNet.Length > 0)
                foreach (var t in Board.Tracks)
                    if (string.Equals(t.Net, _routeNet, StringComparison.OrdinalIgnoreCase))
                        net += Math.Sqrt((t.X2 - t.X1) * (t.X2 - t.X1) + (t.Y2 - t.Y1) * (t.Y2 - t.Y1));
            return (route, net);
        }

        private static double Dist(PcbPoint a, PcbPoint b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

        /// <summary>Rings the pads the current route can end on (same net, not the start pad).</summary>
        private void DrawRouteTargets(DrawingContext dc)
        {
            if (_tool != PcbTool.Route || _routePts.Count == 0 || Board == null || _routeNet.Length == 0) return;
            var pen = new Pen(SymbolRenderer.Brush(A(Accent, (byte)0xD0)), 1.5) { DashStyle = DashStyles.Dash };
            pen.Freeze();
            foreach (var p in Board.Pads())
            {
                if (!string.Equals(p.Net, _routeNet, StringComparison.OrdinalIgnoreCase)) continue;
                if (_routeStartPad != null && p.Component == _routeStartPad.Value.Component && p.Pad == _routeStartPad.Value.Pad) continue;
                double r = Math.Max(p.Width, p.Height) / 2 * _scale + 4;
                dc.DrawEllipse(null, pen, S(p.X, p.Y), r, r);
            }
        }
    }
}
