using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Editor;

namespace RobotWin.Studio.Pcb
{
    /// <summary>
    /// [diffroute] KiCad's "Route Differential Pair" on the canvas: a click on a pad of a pair (D+/D−, CAN H/L …) picks
    /// both of its pads; each click lays the next leg of the pair's centreline (straight, then 45°); the two tracks follow
    /// it at the pair's gap with mitred corners, the preview showing both lengths and their difference; a click on the
    /// pair's pads at the other end (or Enter, or a double click) puts them down. Backspace takes the last leg back.
    /// </summary>
    public sealed partial class BoardCanvas
    {
        private DiffPair? _dpPair;
        private (PcbPoint P, PcbPoint N) _dpStart;
        private readonly List<PcbPoint> _dpPath = new List<PcbPoint>();
        private CopperLayer _dpLayer;
        private DiffPairRoute? _dpPreview;
        private bool _dpEnded;

        private bool DiffPairActive => _dpPair != null;

        private double PairWidth(DiffPair pair) => RouteWidth ?? Board?.TrackWidthFor(pair.Positive) ?? 0.2;

        private void DiffPairCancel()
        {
            _dpPair = null;
            _dpPath.Clear();
            _dpPreview = null;
            _dpEnded = false;
        }

        private void DiffPairClick(double x, double y, bool doubleClick)
        {
            var board = Board;
            if (board == null) return;
            if (_dpPair == null)
            {
                var pad = HitPad(x, y);
                var pair = pad is { } p ? DiffPairRouting.PairOf(board, p.Net) : null;
                if (pad is not { } hit || pair == null)
                {
                    Say("Fark çifti: bir çiftin (D+/D−, CAN H/L, _P/_N …) ağındaki bir pede tıklayın");
                    return;
                }

                var pads = DiffPairRouting.PadsNear(board, pair, hit.X, hit.Y);
                if (pads is not { } two)
                {
                    Say($"Fark çifti {pair}: çiftin öbür ağının pedi kartta yok");
                    return;
                }

                _dpPair = pair;
                _dpStart = (new PcbPoint(two.P.X, two.P.Y), new PcbPoint(two.N.X, two.N.Y));
                _dpLayer = hit.OnLayer(ActiveLayer) ? ActiveLayer : hit.SmdLayer;
                _dpPath.Clear();
                UpdateDiffPreview();
                Say($"Fark çifti {pair} · aralık {Length(pair.Gap)} · genişlik {Length(PairWidth(pair))}: tıklayarak ilerleyin · karşı uçtaki çift pedine tıklayınca biter · Enter: burada bitir · Geri: son parça · Esc: iptal");
                InvalidateVisual();
                return;
            }

            UpdateDiffPreview();
            if (_dpEnded || doubleClick)
            {
                DiffPairFinish();
                return;
            }

            // the leg to the cursor becomes part of the centreline
            _dpPath.Clear();
            _dpPath.AddRange(CentreTo(_cursor.X, _cursor.Y).Skip(1));
            UpdateDiffPreview();
            InvalidateVisual();
        }

        /// <summary>The centreline so far plus the leg (straight, then 45°) to a point.</summary>
        private List<PcbPoint> CentreTo(double x, double y)
        {
            var mid = new PcbPoint((_dpStart.P.X + _dpStart.N.X) / 2, (_dpStart.P.Y + _dpStart.N.Y) / 2);
            var pts = new List<PcbPoint> { mid };
            pts.AddRange(_dpPath);
            var last = pts[pts.Count - 1];
            var leg = InteractiveRouter.Elbow(last, new PcbPoint(Snap(x), Snap(y)), diagonalFirst: false);
            pts.AddRange(leg.Skip(1));
            return pts;
        }

        private void UpdateDiffPreview()
        {
            var board = Board;
            if (board == null || _dpPair is not { } pair)
            {
                _dpPreview = null;
                return;
            }

            // over the pair's pads at the other end: the route ends on them
            (PcbPoint P, PcbPoint N)? end = null;
            if (HitPad(_cursor.X, _cursor.Y) is { } pad && (Same(pad.Net, pair.Positive) || Same(pad.Net, pair.Negative))
                && DiffPairRouting.PadsNear(board, pair, pad.X, pad.Y) is { } two
                && !(SamePoint(new PcbPoint(two.P.X, two.P.Y), _dpStart.P) && SamePoint(new PcbPoint(two.N.X, two.N.Y), _dpStart.N)))
            {
                end = (new PcbPoint(two.P.X, two.P.Y), new PcbPoint(two.N.X, two.N.Y));
            }

            List<PcbPoint> centre;
            if (end is { } e)
            {
                var endMid = new PcbPoint((e.P.X + e.N.X) / 2, (e.P.Y + e.N.Y) / 2);
                centre = CentreTo(endMid.X, endMid.Y);
                centre.RemoveAt(centre.Count - 1);   // the route adds the ends' middle itself
            }
            else
            {
                centre = CentreTo(_cursor.X, _cursor.Y);
            }

            _dpEnded = end != null;
            _dpPreview = DiffPairRouting.Route(board, pair, _dpLayer, PairWidth(pair), _dpStart, centre.Skip(1).ToList(), end);
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static bool SamePoint(PcbPoint a, PcbPoint b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;

        private void DiffPairFinish()
        {
            var board = Board;
            if (board == null || _dpPair is not { } pair || _dpPreview is not { } route) return;
            if (!route.All.Any())
            {
                DiffPairCancel();
                InvalidateVisual();
                return;
            }

            if (!route.Clear)
            {
                Say((route.Problem ?? "Fark çifti sığmıyor") + " · başka yoldan ilerleyin ya da Esc");
                return;
            }

            BeforeEdit?.Invoke(this, EventArgs.Empty);
            board.Tracks.AddRange(route.All);
            // a pair found from its names becomes one the board defines: the DRC then holds it to its gap and skew
            if (!board.DiffPairs.Any(p => Same(p.Positive, pair.Positive) && Same(p.Negative, pair.Negative))) board.DiffPairs.Add(pair);
            DiffPairCancel();
            Edited($"Fark çifti {pair}: {Length(route.PositiveLength)} / {Length(route.NegativeLength)} · fark {Length(route.Skew)}" +
                   (route.Skew > pair.MaxSkew ? $" (en çok {Length(pair.MaxSkew)}: uzunluk ayarıyla eşitleyin)" : string.Empty));
        }

        /// <summary>The tool's keys: Enter puts the pair down, Backspace takes the last leg back, Esc drops it.</summary>
        private bool DiffPairKey(Key key)
        {
            if (!DiffPairActive) return false;
            switch (key)
            {
                case Key.Enter:
                    UpdateDiffPreview();
                    DiffPairFinish();
                    return true;
                case Key.Back:
                    if (_dpPath.Count > 0)
                    {
                        // back to the previous corner the user put down
                        _dpPath.RemoveAt(_dpPath.Count - 1);
                        if (_dpPath.Count > 0) _dpPath.RemoveAt(_dpPath.Count - 1);
                    }

                    UpdateDiffPreview();
                    InvalidateVisual();
                    return true;
                case Key.Escape:
                    DiffPairCancel();
                    Say(ToolHint());
                    InvalidateVisual();
                    return true;
            }

            return false;
        }

        /// <summary>Snapshot: the pair from the pads nearest one point to the pads nearest another, as the preview shows it (not placed).</summary>
        public bool SnapshotDiffPair(DiffPair pair, PcbPoint from, PcbPoint to, IReadOnlyList<PcbPoint> via)
        {
            var board = Board;
            if (board == null) return false;
            SetTool(PcbTool.DiffPair);
            var start = DiffPairRouting.PadsNear(board, pair, from.X, from.Y);
            var end = DiffPairRouting.PadsNear(board, pair, to.X, to.Y);
            if (start is not { } s || end is not { } e) return false;
            _dpPair = pair;
            _dpStart = (new PcbPoint(s.P.X, s.P.Y), new PcbPoint(s.N.X, s.N.Y));
            _dpLayer = CopperLayer.Top;
            _dpPath.Clear();
            _dpPath.AddRange(via);
            _cursor = (e.P.X, e.P.Y);
            UpdateDiffPreview();
            InvalidateVisual();
            return _dpPreview != null;
        }

        private void DrawDiffPreview(DrawingContext dc)
        {
            if (_tool != PcbTool.DiffPair || _dpPreview is not { } route) return;
            var colour = route.Clear ? LayerColor(_dpLayer) : Danger;
            var pen = P(Color.FromArgb(0xC0, colour.R, colour.G, colour.B), Math.Max(1, PairWidth(_dpPair!) * _scale));
            foreach (var t in route.All) dc.DrawLine(pen, S(t.X1, t.Y1), S(t.X2, t.Y2));
            // the lengths beside the cursor
            string text = $"P {Length(route.PositiveLength)} · N {Length(route.NegativeLength)} · fark {Length(route.Skew)}";
            if (!route.Clear && route.Problem != null) text += "\n" + route.Problem;
            var ft = Text(text, 11, route.Clear ? (route.Skew > _dpPair!.MaxSkew ? Color.FromRgb(0xF2, 0xB8, 0x3B) : Accent) : Danger);
            var at = S(_cursor.X, _cursor.Y) + new Vector(14, 12);
            if (at.X + ft.Width + 8 > ActualWidth) at.X -= ft.Width + 28;   // near the right edge: on the cursor's left
            dc.DrawRectangle(SymbolRenderer.Brush(Color.FromArgb(0xE0, 0x14, 0x14, 0x17)), null, new Rect(at.X - 4, at.Y - 2, ft.Width + 8, ft.Height + 4));
            dc.DrawText(ft, at);
        }
    }
}
