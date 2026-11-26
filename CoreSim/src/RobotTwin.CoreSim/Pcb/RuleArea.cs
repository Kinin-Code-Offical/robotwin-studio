using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>
    /// [keepout] KiCad's rule area (keepout): a polygon where, on its layers, tracks, vias, copper pours and (if asked)
    /// parts are not allowed: under an antenna, around a mounting screw's head, beneath a crystal, along a slot. The pour
    /// leaves it out, both routers keep out of it, the DRC reports what is inside.
    /// </summary>
    public sealed class RuleArea
    {
        public string Name { get; set; } = string.Empty;
        public List<PcbPoint> Outline { get; set; } = new List<PcbPoint>();

        /// <summary>The copper layers it holds on; empty: every layer.</summary>
        public List<CopperLayer> Layers { get; set; } = new List<CopperLayer>();

        public bool NoTracks { get; set; } = true;
        public bool NoVias { get; set; } = true;
        public bool NoPour { get; set; } = true;
        public bool NoParts { get; set; }

        public bool OnLayer(CopperLayer l) => Layers.Count == 0 || Layers.Contains(l);

        public bool Contains(double x, double y)
        {
            bool inside = false;
            var p = Outline;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            {
                if ((p[i].Y > y) != (p[j].Y > y) && x < (p[j].X - p[i].X) * (y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X) inside = !inside;
            }

            return inside;
        }

        public IEnumerable<(PcbPoint A, PcbPoint B)> Edges()
        {
            for (int i = 0; i < Outline.Count; i++) yield return (Outline[i], Outline[(i + 1) % Outline.Count]);
        }

        /// <summary>Whether a segment of half width <paramref name="half"/> reaches into the area.</summary>
        public bool Touches(double x1, double y1, double x2, double y2, double half)
        {
            if (Outline.Count < 3) return false;
            if (Contains(x1, y1) || Contains(x2, y2)) return true;
            return Edges().Any(e => Geometry.SegmentSegmentDistance(x1, y1, x2, y2, e.A.X, e.A.Y, e.B.X, e.B.Y) < half - 1e-9);
        }

        /// <summary>What it keeps out, in words ("yol, via, dolgu").</summary>
        public string Describe()
        {
            var what = new List<string>();
            if (NoTracks) what.Add("yol");
            if (NoVias) what.Add("via");
            if (NoPour) what.Add("dolgu");
            if (NoParts) what.Add("parça");
            return string.Join(", ", what);
        }
    }

    /// <summary>[keepout] The DRC's look at the rule areas: tracks, vias, pours and parts inside them.</summary>
    public static class RuleAreaCheck
    {
        public const string Rule = "Yasak bölge";

        public static List<DrcViolation> Check(BoardDocument board)
        {
            var v = new List<DrcViolation>();
            foreach (var a in board.RuleAreas.Where(a => a.Outline.Count >= 3))
            {
                string name = a.Name.Length > 0 ? $"'{a.Name}'" : "yasak bölge";
                if (a.NoTracks)
                {
                    foreach (var t in board.Tracks.Where(t => a.OnLayer(t.Layer) && a.Touches(t.X1, t.Y1, t.X2, t.Y2, t.Width / 2)))
                        v.Add(new DrcViolation(DrcSeverity.Error, Rule, $"{t.Net} yolu {name} içinde (yol yasak) · {PcbLayers.DisplayName(t.Layer)}", (t.X1 + t.X2) / 2, (t.Y1 + t.Y2) / 2, t.Layer));
                }

                if (a.NoVias)
                {
                    foreach (var via in board.Vias.Where(via => a.Touches(via.X, via.Y, via.X, via.Y, via.Diameter / 2) && PcbLayers.Between(via.StartLayer, via.EndLayer, board.LayerCount).Any(a.OnLayer)))
                        v.Add(new DrcViolation(DrcSeverity.Error, Rule, $"{via.Net} viası {name} içinde (via yasak)", via.X, via.Y));
                }

                if (a.NoPour)
                {
                    foreach (var z in board.Zones.Where(z => a.OnLayer(z.Layer) && z.IsFilled))
                    {
                        var inside = z.FilledRegions.SelectMany(r => r.Outer).FirstOrDefault(p => a.Contains(p.X, p.Y));
                        if (inside.X != 0 || inside.Y != 0)
                            v.Add(new DrcViolation(DrcSeverity.Error, Rule, $"{z.Net} dolgusu {name} içinde (dolgu yasak; yeniden doldurun)", inside.X, inside.Y, z.Layer));
                    }
                }

                if (a.NoParts)
                {
                    foreach (var c in board.Components)
                    {
                        if (FootprintLibrary.Get(c.FootprintId) is not { } fp || !a.OnLayer(c.Side)) continue;
                        var b = c.Courtyard(fp);
                        bool hit = new[] { (b.MinX, b.MinY), (b.MaxX, b.MinY), (b.MaxX, b.MaxY), (b.MinX, b.MaxY) }.Any(q => a.Contains(q.Item1, q.Item2))
                                   || a.Outline.Any(p => p.X >= b.MinX && p.X <= b.MaxX && p.Y >= b.MinY && p.Y <= b.MaxY);
                        if (hit) v.Add(new DrcViolation(DrcSeverity.Error, Rule, $"{c.RefDes} {name} içinde (parça yasak)", c.X, c.Y));
                    }
                }
            }

            return v;
        }
    }
}
