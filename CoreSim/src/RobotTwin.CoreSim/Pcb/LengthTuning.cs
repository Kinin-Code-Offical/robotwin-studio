using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>[tuning] The shape of the meanders KiCad's length tuner draws.</summary>
    public enum MeanderCorner
    {
        /// <summary>Corners cut at 45° (KiCad's "chamfered", the usual choice: no acute copper corners).</summary>
        Chamfered,
        Square
    }

    /// <summary>[tuning] How the meanders are drawn.</summary>
    public sealed class MeanderSettings
    {
        /// <summary>How far a meander stands off the track's line (mm); grown up to <see cref="MaxAmplitude"/> when the segment is short.</summary>
        public double Amplitude { get; set; } = 1.0;

        public double MaxAmplitude { get; set; } = 3.0;

        /// <summary>Distance between a meander's legs (mm); 0: three track widths or twice the clearance, whichever is more.</summary>
        public double Spacing { get; set; }

        public MeanderCorner Corner { get; set; } = MeanderCorner.Chamfered;
    }

    /// <summary>[tuning] What a tuning did: the length before and after, the target, and why it fell short if it did.</summary>
    public sealed class TuneResult
    {
        public bool Ok { get; internal set; }
        public string Net { get; internal set; } = string.Empty;
        public double Before { get; internal set; }
        public double After { get; internal set; }
        public double Target { get; internal set; }
        public string Message { get; internal set; } = string.Empty;

        /// <summary>The tracks that replace the tuned segment (null when nothing changed).</summary>
        public List<Track>? Replacement { get; internal set; }
    }

    /// <summary>
    /// [tuning] Length matching as KiCad's "Tune length" does it, and what the length means for the signal:
    /// <list type="bullet">
    /// <item>a straight segment of a net that is too short is replaced by meanders (bumps off its line, 45° corners) so
    /// the net reaches its target: the other half of its differential pair, the longest net of its length group, or
    /// a length given; the meanders keep the net class's clearance to other copper and the board edge;</item>
    /// <item>propagation delay (ps): a microstrip on an outer layer travels at c/√εeff (Hammerstad–Jensen εeff from the
    /// stackup's dielectric under it and the track width), a stripline inside at c/√εr; a pair's skew and a group's
    /// spread in time, which is what the receiver sees.</item>
    /// </list>
    /// </summary>
    public static class LengthTuning
    {
        /// <summary>ps per mm in vacuum (1/c).</summary>
        public const double VacuumPsPerMm = 3.33564095;

        // ---------------------------------------------------------------- delay

        /// <summary>Effective permittivity of a track (Hammerstad–Jensen for an outer-layer microstrip, εr for a stripline).</summary>
        public static double EffectivePermittivity(BoardDocument board, CopperLayer layer, double width)
        {
            var stack = board.Stackup;
            double er = stack.DielectricConstant;
            if (!PcbLayers.IsOuter(layer)) return er;
            var built = stack.Build(board.Rules.CopperThicknessUm);
            int i = built.FindIndex(l => l.Copper == layer);
            int j = layer == CopperLayer.Top ? i + 1 : i - 1;
            double h = j >= 0 && j < built.Count ? built[j].Thickness : stack.Thickness;
            double u = Math.Max(1e-6, width / Math.Max(1e-6, h));
            double f = 1 / Math.Sqrt(1 + 12 / u) + (u < 1 ? 0.04 * (1 - u) * (1 - u) : 0);
            return (er + 1) / 2 + (er - 1) / 2 * f;
        }

        public static double PsPerMm(BoardDocument board, CopperLayer layer, double width) => VacuumPsPerMm * Math.Sqrt(EffectivePermittivity(board, layer, width));

        /// <summary>A net's propagation delay along its routed copper (ps); vias at the dielectric's own speed.</summary>
        public static double Delay(BoardDocument board, string net)
        {
            double ps = 0;
            foreach (var t in board.Tracks.Where(t => string.Equals(t.Net, net, StringComparison.OrdinalIgnoreCase)))
                ps += t.Length * PsPerMm(board, t.Layer, t.Width);
            double viaMm = NetLengths.Measure(board).TryGetValue(net, out double all) ? all - board.Tracks.Where(t => string.Equals(t.Net, net, StringComparison.OrdinalIgnoreCase)).Sum(t => t.Length) : 0;
            return ps + Math.Max(0, viaMm) * VacuumPsPerMm * Math.Sqrt(board.Stackup.DielectricConstant);
        }

        // ---------------------------------------------------------------- target

        /// <summary>The length a net should have: its pair's other half, its group's longest net, or none (null).</summary>
        public static (double Length, string Why)? TargetFor(BoardDocument board, string net, Dictionary<string, double>? lengths = null)
        {
            lengths ??= NetLengths.Measure(board);
            double L(string n) => lengths.TryGetValue(n, out double v) ? v : 0;
            foreach (var p in board.DiffPairs)
            {
                string? other = string.Equals(p.Positive, net, StringComparison.OrdinalIgnoreCase) ? p.Negative
                    : string.Equals(p.Negative, net, StringComparison.OrdinalIgnoreCase) ? p.Positive : null;
                if (other != null) return (L(other), $"{other} ile eşit (fark çifti)");
            }

            foreach (var g in board.LengthGroups)
            {
                if (!g.Nets.Any(n => string.Equals(n, net, StringComparison.OrdinalIgnoreCase))) continue;
                var longest = g.Nets.OrderByDescending(L).First();
                return (L(longest), $"'{g.Name}' grubunun en uzunu {longest} ile eşit");
            }

            return null;
        }

        // ---------------------------------------------------------------- meanders

        /// <summary>
        /// Lengthens <paramref name="segment"/>'s net to <paramref name="target"/> mm (its pair's or group's target when
        /// null) by meanders on that segment. Nothing is changed on the board: the result carries the replacement.
        /// </summary>
        public static TuneResult Tune(BoardDocument board, Track segment, double? target = null, MeanderSettings? settings = null)
        {
            settings ??= new MeanderSettings();
            var lengths = NetLengths.Measure(board);
            var r = new TuneResult { Net = segment.Net, Before = lengths.TryGetValue(segment.Net, out double b) ? b : segment.Length };
            var goal = target.HasValue ? (target.Value, "elle verilen") : TargetFor(board, segment.Net, lengths);
            if (goal == null)
            {
                r.Message = $"{segment.Net} bir fark çiftinde ya da uzunluk grubunda değil: hedef uzunluğu verin";
                return r;
            }

            r.Target = goal.Value.Item1;
            double extra = r.Target - r.Before;
            if (extra <= 0.01)
            {
                r.Ok = true;
                r.After = r.Before;
                r.Message = extra < -0.01 ? $"{segment.Net} hedeften {-extra:0.##} mm uzun: kısaltmak için öteki yolu uzatın" : $"{segment.Net} zaten hedefte ({r.Target:0.##} mm)";
                return r;
            }

            var cls = board.ClassFor(segment.Net);
            double w = segment.Width;
            double spacing = settings.Spacing > 0 ? settings.Spacing : Math.Max(3 * w, 2 * cls.Clearance + w);
            double len = segment.Length;
            if (len < 3 * spacing)
            {
                r.Message = $"Bu parça menderes için kısa ({len:0.##} mm; en az {3 * spacing:0.##} mm): daha uzun bir düz parçaya tıklayın";
                return r;
            }

            // bumps: each one advances 2 × spacing along the line and adds 2 × amplitude (less what the chamfers cut)
            int room = (int)Math.Floor((len - spacing) / (2 * spacing));
            string? lastWhy = null;
            foreach (double side in new[] { 1.0, -1.0 })
            {
                for (double amp = settings.Amplitude; amp <= settings.MaxAmplitude + 1e-9; amp += 0.25)
                {
                    int n = (int)Math.Ceiling(extra / (2 * amp * Gain(settings.Corner, amp, spacing)));
                    if (n < 1) n = 1;
                    if (n > room) continue;
                    var path = Build(segment, n, amp, spacing, side, settings.Corner, extra);
                    if (path == null) continue;
                    var tracks = Tracks(segment, path);
                    if (Collides(board, segment, tracks, cls.Clearance, out lastWhy)) continue;
                    r.Ok = true;
                    r.Replacement = tracks;
                    r.After = r.Before - len + tracks.Sum(t => t.Length);
                    r.Message = $"{segment.Net}: {r.Before:0.##} → {r.After:0.##} mm ({goal.Value.Item2}); {n} kıvrım, genlik {amp:0.##} mm";
                    return r;
                }
            }

            r.Message = lastWhy != null
                ? $"Kıvrımlar sığmıyor: {lastWhy}. Başka bir parçayı deneyin ya da genliği küçültün"
                : $"Bu parçaya {extra:0.##} mm sığmıyor (en çok genlik {settings.MaxAmplitude:0.##} mm): daha uzun bir parça seçin ya da en çok genliği artırın";
            return r;
        }

        /// <summary>The share of 2 × amplitude a bump really adds once its two top corners are cut at 45°.</summary>
        private static double Gain(MeanderCorner corner, double amp, double spacing)
        {
            if (corner == MeanderCorner.Square) return 1;
            double c = Chamfer(amp, spacing);
            return Math.Max(0.1, (2 * amp - 2 * c * (2 - Math.Sqrt(2))) / (2 * amp));   // only the two top corners are cut
        }

        private static double Chamfer(double amp, double spacing) => Math.Min(amp, spacing) / 3;

        /// <summary>
        /// The centre line from the segment's start to its end with n bumps, centred along it; the last bump's height is
        /// trimmed so the added length is exactly <paramref name="extra"/>.
        /// </summary>
        private static List<(double X, double Y)>? Build(Track seg, int n, double amp, double spacing, double side, MeanderCorner corner, double extra)
        {
            double len = seg.Length;
            double ux = (seg.X2 - seg.X1) / len, uy = (seg.Y2 - seg.Y1) / len;
            double nx = -uy * side, ny = ux * side;
            double used = 2 * spacing * n - spacing;   // the last bump needs no gap after it
            double start = (len - used) / 2;
            // heights: all bumps at amp, the last one trimmed (a bump of height a adds 2a, less its chamfers)
            var heights = Enumerable.Repeat(amp, n).ToArray();
            for (int iter = 0; iter < 30; iter++)
            {
                double added = heights.Sum(a => Added(a, amp, spacing, corner));
                double miss = extra - added;
                if (Math.Abs(miss) < 1e-4) break;
                heights[n - 1] = Math.Max(0, heights[n - 1] + miss / 2);
                if (heights[n - 1] > amp + 1e-9) return null;   // needs a bump more than the room gives
            }

            if (heights[n - 1] < 0.05 && n > 1) heights[n - 1] = 0;
            var pts = new List<(double, double)> { (seg.X1, seg.Y1) };
            (double, double) At(double along, double off) => (seg.X1 + ux * along + nx * off, seg.Y1 + uy * along + ny * off);
            double s = start;
            for (int k = 0; k < n; k++)
            {
                double a = heights[k];
                if (a <= 1e-6)
                {
                    s += 2 * spacing;
                    continue;
                }

                double c = corner == MeanderCorner.Chamfered ? Math.Min(Chamfer(amp, spacing), a / 2) : 0;
                pts.Add(At(s, 0));
                if (c > 0)
                {
                    pts.Add(At(s, a - c));
                    pts.Add(At(s + c, a));
                    pts.Add(At(s + spacing - c, a));
                    pts.Add(At(s + spacing, a - c));
                }
                else
                {
                    pts.Add(At(s, a));
                    pts.Add(At(s + spacing, a));
                }

                pts.Add(At(s + spacing, 0));
                s += 2 * spacing;
            }

            pts.Add((seg.X2, seg.Y2));
            return pts;
        }

        /// <summary>What a bump of height a adds to the length: its legs, less what the two 45° cuts at its top save (c(2 − √2) each).</summary>
        private static double Added(double a, double amp, double spacing, MeanderCorner corner)
        {
            if (a <= 0) return 0;
            if (corner == MeanderCorner.Square) return 2 * a;
            double c = Math.Min(Chamfer(amp, spacing), a / 2);   // as Build cuts it
            return 2 * a - 2 * c * (2 - Math.Sqrt(2));
        }

        private static List<Track> Tracks(Track seg, List<(double X, double Y)> pts)
        {
            var list = new List<Track>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var (x1, y1) = pts[i];
                var (x2, y2) = pts[i + 1];
                if (Math.Abs(x1 - x2) < 1e-9 && Math.Abs(y1 - y2) < 1e-9) continue;
                list.Add(new Track { Net = seg.Net, Layer = seg.Layer, Width = seg.Width, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 });
            }

            return list;
        }

        /// <summary>Whether the meanders come closer than the clearance to another net's copper on their layer, or to the board edge.</summary>
        private static bool Collides(BoardDocument board, Track seg, List<Track> tracks, double clearance, out string? why)
        {
            why = null;
            double half = seg.Width / 2;
            foreach (var t in board.Tracks)
            {
                if (ReferenceEquals(t, seg) || t.Layer != seg.Layer || string.Equals(t.Net, seg.Net, StringComparison.OrdinalIgnoreCase)) continue;
                double need = Math.Max(clearance, board.ClassFor(t.Net).Clearance) + half + t.Width / 2;
                foreach (var m in tracks)
                {
                    if (Geometry.SegmentSegmentDistance(m.X1, m.Y1, m.X2, m.Y2, t.X1, t.Y1, t.X2, t.Y2) < need - 1e-6)
                    {
                        why = $"{t.Net} yoluna çok yakın";
                        return true;
                    }
                }
            }

            foreach (var p in board.Pads())
            {
                if (string.Equals(p.Net, seg.Net, StringComparison.OrdinalIgnoreCase) || !p.OnLayer(seg.Layer)) continue;
                foreach (var m in tracks)
                {
                    if (DesignRuleChecker.PadSegmentDistance(p, m.X1, m.Y1, m.X2, m.Y2) < clearance + half - 1e-6)
                    {
                        why = $"{p.Component.RefDes}.{p.Pad.Name} pedine çok yakın";
                        return true;
                    }
                }
            }

            foreach (var v in board.Vias)
            {
                if (string.Equals(v.Net, seg.Net, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var m in tracks)
                {
                    if (Geometry.PointSegmentDistance(v.X, v.Y, m.X1, m.Y1, m.X2, m.Y2) < clearance + half + v.Diameter / 2 - 1e-6)
                    {
                        why = $"{v.Net} viasına çok yakın";
                        return true;
                    }
                }
            }

            // the board edge: the outline polygon's sides
            var outline = board.OutlinePolygon(0.05);
            double edge = board.Rules.EdgeClearance + half;
            for (int i = 0; i < outline.Count; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Count];
                foreach (var m in tracks)
                {
                    if (Geometry.SegmentSegmentDistance(m.X1, m.Y1, m.X2, m.Y2, a.X, a.Y, b.X, b.Y) < edge - 1e-6)
                    {
                        why = "kart kenarına çok yakın";
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Replaces the tuned segment with its meanders on the board.</summary>
        public static void Apply(BoardDocument board, Track segment, TuneResult result)
        {
            if (result.Replacement == null) return;
            int i = board.Tracks.IndexOf(segment);
            if (i < 0) return;
            board.Tracks.RemoveAt(i);
            board.Tracks.InsertRange(i, result.Replacement);
        }
    }
}
