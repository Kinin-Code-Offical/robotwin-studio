using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>[pairs] Two nets routed as a differential pair (USB D+/D−, CAN H/L, LVDS …): together, at one gap, at one length.</summary>
    public sealed class DiffPair
    {
        public string Positive { get; set; } = string.Empty;

        public string Negative { get; set; } = string.Empty;

        /// <summary>Edge-to-edge gap the two tracks keep where they run together (mm).</summary>
        public double Gap { get; set; } = 0.2;

        /// <summary>Largest length difference the pair tolerates (mm); USB 2.0 full speed allows ~2.5 mm, high speed ~1.25 mm.</summary>
        public double MaxSkew { get; set; } = 1.25;

        /// <summary>[impedance] Differential impedance the pair must have (Ω: USB 90, Ethernet and LVDS 100, CAN 120); 0 for none.</summary>
        public double Impedance { get; set; }

        public override string ToString() => Positive + " / " + Negative;
    }

    /// <summary>[pairs] Nets that must arrive together (a parallel bus, a clock and its data): equal length within a tolerance.</summary>
    public sealed class LengthGroup
    {
        public string Name { get; set; } = string.Empty;

        public List<string> Nets { get; set; } = new List<string>();

        /// <summary>Longest minus shortest allowed (mm).</summary>
        public double Tolerance { get; set; } = 2.0;
    }

    /// <summary>[pairs] A pair as routed.</summary>
    public sealed class PairReport
    {
        public DiffPair Pair { get; internal set; } = null!;
        public double PositiveLength { get; internal set; }
        public double NegativeLength { get; internal set; }

        public double Skew => Math.Abs(PositiveLength - NegativeLength);

        /// <summary>Share of the shorter net's track length that runs beside the other at the pair's gap (±30 %).</summary>
        public double Coupled { get; internal set; }

        /// <summary>Mean edge-to-edge gap where they run side by side (mm); NaN when they never do.</summary>
        public double MeanGap { get; internal set; } = double.NaN;
    }

    /// <summary>
    /// [pairs] Routed lengths and the matching they need. A net's length is the copper a signal travels: its tracks
    /// plus the barrel of every via it changes layer through (the board's thickness between the two layers, taken as the
    /// share of the stack). Differential pairs are measured for skew (their length difference), the share they run coupled
    /// at their gap, and that gap; length groups for their spread. Pairs are found from their names (D+/D−, X_P/X_N,
    /// CAN_H/CAN_L, TX+/TX−), and the DRC flags the ones a board defines when they break their limits.
    /// </summary>
    public static class NetLengths
    {
        /// <summary>Routed length of every net (mm).</summary>
        public static Dictionary<string, double> Measure(BoardDocument board)
        {
            var lengths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in board.Tracks.Where(t => t.Net.Length > 0))
                lengths[t.Net] = (lengths.TryGetValue(t.Net, out var l) ? l : 0) + t.Length;
            int copper = Math.Max(2, board.LayerCount);
            double thickness = board.Stackup?.Thickness ?? 1.6;
            foreach (var v in board.Vias.Where(v => v.Net.Length > 0))
            {
                // Signal goes through the barrel between the outermost layers it is routed on at this via.
                var used = board.Tracks.Where(t => string.Equals(t.Net, v.Net, StringComparison.OrdinalIgnoreCase)
                                                   && (Near(t.X1, t.Y1, v) || Near(t.X2, t.Y2, v))).Select(t => LayerIndex(t.Layer, copper)).ToList();
                if (used.Count < 2) continue;
                double span = (used.Max() - used.Min()) / (double)(copper - 1);
                lengths[v.Net] = (lengths.TryGetValue(v.Net, out var l) ? l : 0) + span * thickness;
            }

            return lengths;
        }

        private static bool Near(double x, double y, Via v) => (x - v.X) * (x - v.X) + (y - v.Y) * (y - v.Y) <= v.Diameter * v.Diameter / 4 + 1e-9;

        /// <summary>Position of a copper layer in the stack, 0 on top to <paramref name="copper"/> − 1 at the bottom.</summary>
        private static int LayerIndex(CopperLayer layer, int copper) =>
            layer == CopperLayer.Top ? 0 : layer == CopperLayer.Bottom ? copper - 1 : Math.Min(copper - 2, Math.Max(1, (int)layer - (int)CopperLayer.In1 + 1));

        /// <summary>Suffixes of a pair's two halves, longest first: D+/D−, USB_P/USB_N, CAN_H/CAN_L, TXP/TXN, CANH/CANL.</summary>
        private static readonly (string Pos, string Neg)[] PairSuffixes = { ("+", "-"), ("_P", "_N"), ("_H", "_L"), ("P", "N"), ("H", "L") };

        /// <summary>Pairs found from the board's net names: D+ with D−, USB_P with USB_N, CANH with CANL …</summary>
        public static List<DiffPair> Detect(BoardDocument board)
        {
            var nets = board.NetNames().ToList();
            var found = new List<DiffPair>();
            foreach (var net in nets.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (found.Any(d => d.Positive.Equals(net, StringComparison.OrdinalIgnoreCase) || d.Negative.Equals(net, StringComparison.OrdinalIgnoreCase))) continue;
                foreach (var (pos, neg) in PairSuffixes)
                {
                    if (net.Length <= pos.Length || !net.EndsWith(pos, StringComparison.OrdinalIgnoreCase)) continue;
                    string stem = net.Substring(0, net.Length - pos.Length);
                    bool lower = char.IsLower(net[net.Length - 1]);
                    string partner = stem + (lower ? neg.ToLowerInvariant() : neg);
                    var match = nets.FirstOrDefault(n => n.Equals(partner, StringComparison.Ordinal)) ?? nets.FirstOrDefault(n => n.Equals(partner, StringComparison.OrdinalIgnoreCase));
                    if (match == null) continue;
                    found.Add(new DiffPair { Positive = net, Negative = match });
                    break;
                }
            }

            return found;
        }

        /// <summary>A pair as routed: both lengths, how much runs coupled at its gap, and the mean gap there.</summary>
        public static PairReport Analyze(BoardDocument board, DiffPair pair, Dictionary<string, double>? lengths = null)
        {
            lengths ??= Measure(board);
            var r = new PairReport
            {
                Pair = pair,
                PositiveLength = lengths.TryGetValue(pair.Positive, out var lp) ? lp : 0,
                NegativeLength = lengths.TryGetValue(pair.Negative, out var ln) ? ln : 0
            };
            var pos = board.Tracks.Where(t => string.Equals(t.Net, pair.Positive, StringComparison.OrdinalIgnoreCase)).ToList();
            var neg = board.Tracks.Where(t => string.Equals(t.Net, pair.Negative, StringComparison.OrdinalIgnoreCase)).ToList();
            double coupled = 0, gapSum = 0;
            foreach (var a in pos)
            {
                // Sample the track every 0.25 mm: a point is coupled when the nearest partner track on its layer runs
                // parallel to it (within 10°) at the pair's gap ±30 %.
                int steps = Math.Max(1, (int)Math.Ceiling(a.Length / 0.25));
                double ux = (a.X2 - a.X1) / Math.Max(1e-9, a.Length), uy = (a.Y2 - a.Y1) / Math.Max(1e-9, a.Length);
                for (int k = 0; k < steps; k++)
                {
                    double f = (k + 0.5) / steps, x = a.X1 + (a.X2 - a.X1) * f, y = a.Y1 + (a.Y2 - a.Y1) * f;
                    double best = double.PositiveInfinity;
                    Track? partner = null;
                    foreach (var b in neg.Where(b => b.Layer == a.Layer))
                    {
                        double d = Geometry.PointSegmentDistance(x, y, b.X1, b.Y1, b.X2, b.Y2);
                        if (d < best)
                        {
                            best = d;
                            partner = b;
                        }
                    }

                    if (partner == null) continue;
                    double vx = (partner.X2 - partner.X1) / Math.Max(1e-9, partner.Length), vy = (partner.Y2 - partner.Y1) / Math.Max(1e-9, partner.Length);
                    double edge = best - a.Width / 2 - partner.Width / 2;
                    if (Math.Abs(ux * vx + uy * vy) < Math.Cos(10 * Math.PI / 180) || Math.Abs(edge - pair.Gap) > 0.3 * pair.Gap + 0.02) continue;
                    double piece = a.Length / steps;
                    coupled += piece;
                    gapSum += edge * piece;
                }
            }

            double shorter = Math.Min(r.PositiveLength, r.NegativeLength);
            r.Coupled = shorter > 0 ? Math.Min(1, coupled / pos.Sum(t => t.Length) ) : 0;
            if (coupled > 0) r.MeanGap = gapSum / coupled;
            return r;
        }

        private static string Mm(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>The DRC's warnings for the board's own pairs and groups: skew over its limit, little coupling, a spread group.</summary>
        public static List<DrcViolation> Check(BoardDocument board)
        {
            var issues = new List<DrcViolation>();
            if (board.DiffPairs.Count == 0 && board.LengthGroups.Count == 0) return issues;
            var lengths = Measure(board);
            (double X, double Y) Where(string net)
            {
                var t = board.Tracks.FirstOrDefault(t => string.Equals(t.Net, net, StringComparison.OrdinalIgnoreCase));
                return t != null ? (t.X1, t.Y1) : (0, 0);
            }

            foreach (var pair in board.DiffPairs)
            {
                var r = Analyze(board, pair, lengths);
                if (r.PositiveLength <= 0 || r.NegativeLength <= 0) continue;   // not routed yet: the connectivity check says so
                var (x, y) = Where(pair.Positive);
                if (r.Skew > pair.MaxSkew + 1e-6)
                    issues.Add(new DrcViolation(DrcSeverity.Warning, "Fark çifti uzunluğu",
                        $"{pair}: {Mm(r.PositiveLength)} / {Mm(r.NegativeLength)} mm, fark {Mm(r.Skew)} mm (en çok {Mm(pair.MaxSkew)} mm); kısa olanı menderesle uzatın", x, y));
                if (r.Coupled < 0.6)
                    issues.Add(new DrcViolation(DrcSeverity.Warning, "Fark çifti eşleşmesi",
                        $"{pair}: yolun yalnız %{r.Coupled * 100:0}'i {Mm(pair.Gap)} mm aralıkla yan yana gidiyor; ayrık kısımlarda empedans tutmaz", x, y));
            }

            foreach (var group in board.LengthGroups)
            {
                var measured = group.Nets.Select(n => (Net: n, L: lengths.TryGetValue(n, out var l) ? l : 0)).Where(n => n.L > 0).ToList();
                if (measured.Count < 2) continue;
                var longest = measured.OrderByDescending(n => n.L).First();
                var shortest = measured.OrderBy(n => n.L).First();
                double spread = longest.L - shortest.L;
                if (spread > group.Tolerance + 1e-6)
                {
                    var (x, y) = Where(shortest.Net);
                    issues.Add(new DrcViolation(DrcSeverity.Warning, "Uzunluk eşleme",
                        $"{group.Name}: {shortest.Net} {Mm(shortest.L)} mm, {longest.Net} {Mm(longest.L)} mm, fark {Mm(spread)} mm (tolerans {Mm(group.Tolerance)} mm)", x, y));
                }
            }

            return issues;
        }
    }
}
