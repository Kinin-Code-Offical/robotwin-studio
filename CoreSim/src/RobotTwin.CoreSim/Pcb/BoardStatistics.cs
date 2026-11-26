using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>
    /// [board-stats] What a board is made of, as KiCad's Inspect → Board Statistics (studied in place): parts by side and
    /// mounting, pads, vias by kind, the drill table (each size, plated or not, and what it is for), copper by layer, the
    /// board's size and area. What a fab quotes from, and a quick check that the board is what one thinks it is.
    /// </summary>
    public sealed class BoardStatistics
    {
        /// <summary>One line of the drill table: a hole size, plated or not, what drills it (via, pad, mounting hole) and how many.</summary>
        public sealed record Drill(double Diameter, bool Plated, string Kind, int Count);

        public int PartsTop { get; private set; }
        public int PartsBottom { get; private set; }
        public int PartsThroughHole { get; private set; }
        public int PartsSmd { get; private set; }

        public int PadsThroughHole { get; private set; }
        public int PadsSmd { get; private set; }
        public int PadsNonPlated { get; private set; }

        public int ViasThrough { get; private set; }
        public int ViasBlindBuried { get; private set; }

        public int Tracks { get; private set; }
        public double TrackLength { get; private set; }
        public Dictionary<CopperLayer, double> LengthByLayer { get; } = new Dictionary<CopperLayer, double>();

        public int Nets { get; private set; }
        public int Zones { get; private set; }
        public int Texts { get; private set; }
        public int MountingHoles { get; private set; }

        public double Width { get; private set; }
        public double Height { get; private set; }

        /// <summary>The outline's area (mm²), cut-outs not subtracted.</summary>
        public double Area { get; private set; }

        public int CopperLayers { get; private set; }
        public double Thickness { get; private set; }

        public List<Drill> Drills { get; } = new List<Drill>();

        public int Holes => Drills.Sum(d => d.Count);

        public static BoardStatistics Of(BoardDocument board)
        {
            var s = new BoardStatistics
            {
                Width = board.Width,
                Height = board.Height,
                CopperLayers = board.LayerCount,
                Thickness = board.Stackup.Thickness,
                Zones = board.Zones.Count,
                Texts = board.Texts.Count,
                MountingHoles = board.MountingHoles.Count,
                Nets = board.NetNames().Count(),
                Tracks = board.Tracks.Count
            };

            var poly = board.OutlinePolygon();
            double a = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i];
                var q = poly[(i + 1) % poly.Count];
                a += p.X * q.Y - q.X * p.Y;
            }

            s.Area = poly.Count >= 3 ? Math.Abs(a) / 2 : board.Width * board.Height;

            var pads = board.Pads().ToList();
            foreach (var comp in board.Components)
            {
                if (comp.Side == CopperLayer.Bottom) s.PartsBottom++;
                else s.PartsTop++;
                if (pads.Any(p => ReferenceEquals(p.Component, comp) && p.Pad.IsThroughHole)) s.PartsThroughHole++;
                else s.PartsSmd++;
            }

            var drills = new Dictionary<(long, bool, string), int>();
            void Hole(double d, bool plated, string kind)
            {
                var key = ((long)Math.Round(d * 1000), plated, kind);
                drills[key] = drills.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            foreach (var p in pads)
            {
                if (!p.Pad.IsThroughHole)
                {
                    s.PadsSmd++;
                    continue;
                }

                if (p.Pad.NonPlated) s.PadsNonPlated++;
                else s.PadsThroughHole++;
                Hole(p.Pad.Drill, !p.Pad.NonPlated, "ped");
            }

            foreach (var v in board.Vias)
            {
                if (v.IsThrough(board.LayerCount)) s.ViasThrough++;
                else s.ViasBlindBuried++;
                Hole(v.Drill, true, "via");
            }

            foreach (var h in board.MountingHoles) Hole(h.Drill, h.Plated, "montaj deliği");

            foreach (var t in board.Tracks)
            {
                s.TrackLength += t.Length;
                s.LengthByLayer[t.Layer] = (s.LengthByLayer.TryGetValue(t.Layer, out double l) ? l : 0) + t.Length;
            }

            s.Drills.AddRange(drills.Select(kv => new Drill(kv.Key.Item1 / 1000.0, kv.Key.Item2, kv.Key.Item3, kv.Value))
                .OrderBy(d => d.Diameter).ThenBy(d => d.Kind, StringComparer.Ordinal));
            return s;
        }

        /// <summary>The statistics as text (the dialog's "copy"); <paramref name="translate"/> puts each line into the user's language.</summary>
        public string ToText(Func<string, string>? translate = null)
        {
            var t = translate ?? (x => x);
            var c = CultureInfo.InvariantCulture;
            string F(double v, string f = "0.##") => v.ToString(f, c);
            var s = new StringBuilder();
            s.AppendLine(t($"Kart: {F(Width)} × {F(Height)} mm, alan {F(Area / 100, "0.###")} cm², {CopperLayers} katman, {F(Thickness)} mm"));
            s.AppendLine(t($"Parçalar: {PartsTop + PartsBottom} (üst {PartsTop}, alt {PartsBottom}; delikli {PartsThroughHole}, SMD {PartsSmd})"));
            s.AppendLine(t($"Pedler: delikli {PadsThroughHole}, SMD {PadsSmd}, kaplamasız {PadsNonPlated}"));
            s.AppendLine(t($"Vialar: delikten deliğe {ViasThrough}, kör/gömülü {ViasBlindBuried}"));
            s.AppendLine(t($"Yollar: {Tracks} parça, {F(TrackLength, "0.#")} mm"));
            foreach (var kv in LengthByLayer.OrderBy(kv => kv.Key))
                s.AppendLine("    " + t(PcbLayers.DisplayName(kv.Key)) + ": " + F(kv.Value, "0.#") + " mm");
            s.AppendLine(t($"Hatlar: {Nets} · dolgu alanları: {Zones} · yazılar: {Texts} · montaj delikleri: {MountingHoles}"));
            s.AppendLine(t($"Delikler: {Holes}, {Drills.Select(d => d.Diameter).Distinct().Count()} çap"));
            foreach (var d in Drills)
                s.AppendLine("    Ø" + F(d.Diameter, "0.###") + " mm · " + t(d.Plated ? "kaplamalı" : "kaplamasız") + " · " + t(d.Kind) + " × " + d.Count);
            return s.ToString();
        }
    }
}
