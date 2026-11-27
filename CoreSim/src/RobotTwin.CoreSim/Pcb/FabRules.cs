using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>
    /// [fab-rules] What one fab makes at its standard price for this board (its layer count and copper weight), as the fab
    /// publishes it. A board inside these limits is made as drawn; a board outside them is refused, re-quoted or "adjusted".
    /// </summary>
    public sealed class FabRuleSet
    {
        public string Id { get; init; } = string.Empty;

        /// <summary>"JLCPCB (2 katman, 1 oz)".</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Where the numbers come from (the fab's capability page) and when they were read.</summary>
        public string Source { get; init; } = string.Empty;

        public double MinTrackWidth { get; init; }

        /// <summary>Copper to copper of different nets (track, pad, via).</summary>
        public double MinClearance { get; init; }

        public double MinDrill { get; init; }

        /// <summary>Ring around a plated pad's hole.</summary>
        public double MinPadRing { get; init; }

        /// <summary>Ring around a via's hole (fabs allow a narrower one than a pad's: nothing is soldered to it).</summary>
        public double MinViaRing { get; init; }

        /// <summary>Hole wall to hole wall: pads, and vias among themselves.</summary>
        public double MinHoleToHole { get; init; }

        public double MinViaHoleToHole { get; init; }

        /// <summary>Copper to the routed board edge.</summary>
        public double EdgeClearance { get; init; }

        /// <summary>Narrowest solder-mask web between two pads (green mask).</summary>
        public double MinMaskWeb { get; init; }

        /// <summary>Legend: thinnest stroke and smallest text height the fab prints legibly; 0 where it publishes none.</summary>
        public double MinSilkStroke { get; init; }

        public double MinSilkHeight { get; init; }

        public int MaxLayers { get; init; } = 32;

        /// <summary>Heaviest outer copper for this layer count (µm); 0 where the fab publishes no limit.</summary>
        public double MaxCopperUm { get; init; }

        public bool BlindBuriedVias { get; init; }

        /// <summary>One line: "yol/aralık 0,1/0,1 · delik 0,15 · halka ped 0,18, via 0,05 · …".</summary>
        public string Limits => string.Format(CultureInfo.CurrentCulture,
            "yol/aralık {0:0.###}/{1:0.###} mm · delik {2:0.###} mm · halka ped {3:0.###}, via {4:0.###} mm · delik-delik {5:0.###} (via {6:0.###}) mm · kenar {7:0.###} mm · maske köprüsü {8:0.###} mm",
            MinTrackWidth, MinClearance, MinDrill, MinPadRing, MinViaRing, MinHoleToHole, MinViaHoleToHole, EdgeClearance, MinMaskWeb);
    }

    /// <summary>
    /// [fab-rules] Fabs' published standard capabilities, and the board checked against one of them with the same DRC and
    /// DFM checkers the board's own rules use: a copy of the board takes the fab's minimums as its rules and every
    /// manufacturing finding on it is one the fab would raise. Numbers read from each fab's capability page (November
    /// 2026); a fab changes them over time, so the fab's own page is the final word before ordering.
    /// </summary>
    public static class FabRules
    {
        public static IReadOnlyList<(string Id, string Name)> Fabs { get; } = new[]
        {
            ("jlcpcb", "JLCPCB"),
            ("pcbway", "PCBWay"),
            ("oshpark", "OSH Park"),
        };

        /// <summary>The fab's limits for <paramref name="board"/>'s layer count and outer copper; null for an unknown id.</summary>
        public static FabRuleSet? For(string id, BoardDocument board) => For(id, board.Stackup.CopperLayers, board.Rules.CopperThicknessUm);

        public static FabRuleSet? For(string id, int layers, double copperUm)
        {
            bool multi = layers > 2, heavy = copperUm > 36;
            string oz = (copperUm / 35.0).ToString("0.#", CultureInfo.CurrentCulture);
            string Name(string fab) => string.Format(CultureInfo.CurrentCulture, "{0} ({1} katman, {2} oz)", fab, layers, oz);
            switch ((id ?? string.Empty).ToLowerInvariant())
            {
                case "jlcpcb":
                    // jlcpcb.com/capabilities/pcb-capabilities: 1 oz 0.10/0.10 (multilayer 0.09/0.09), 2 oz 0.16 (multilayer 0.15);
                    // via 0.15 hole / 0.25 pad; PTH ring min 0.18 (multilayer 0.15), 2 oz 0.254; via holes 0.2 apart, pad holes 0.45;
                    // routed edge 0.2; mask bridge 0.10 (2 oz 0.20); legend 0.15 stroke, 1.0 mm text; 2-layer to 4.5 oz, multilayer to 2 oz
                    return new FabRuleSet
                    {
                        Id = "jlcpcb", Name = Name("JLCPCB"), Source = "jlcpcb.com/capabilities/pcb-capabilities (Kasım 2026)",
                        MinTrackWidth = heavy ? (multi ? 0.15 : 0.16) : (multi ? 0.09 : 0.10),
                        MinClearance = heavy ? (multi ? 0.15 : 0.16) : (multi ? 0.09 : 0.10),
                        MinDrill = 0.15, MinPadRing = heavy ? 0.254 : (multi ? 0.15 : 0.18), MinViaRing = 0.05,
                        MinHoleToHole = 0.45, MinViaHoleToHole = 0.2, EdgeClearance = 0.2,
                        MinMaskWeb = heavy ? 0.20 : 0.10, MinSilkStroke = 0.15, MinSilkHeight = 1.0,
                        MaxLayers = 32, MaxCopperUm = multi ? 70 : 157.5, BlindBuriedVias = true,
                    };
                case "pcbway":
                    // pcbway.com/capabilities.html, standard (no extra charge): 0.1/0.1 (6/8 mil at 2 oz); drill 0.2 (0.15 at extra
                    // cost); ring 0.15, via ring 5 mil (7 mil at 2 oz); component holes 16 mil apart, vias 11 mil; CNC edge 0.25;
                    // green mask bridge 4 mil; legend 0.15 wide, 0.8 mm high
                    return new FabRuleSet
                    {
                        Id = "pcbway", Name = Name("PCBWay"), Source = "pcbway.com/capabilities.html (Kasım 2026)",
                        MinTrackWidth = heavy ? 0.152 : 0.10, MinClearance = heavy ? 0.203 : 0.10,
                        MinDrill = 0.2, MinPadRing = 0.15, MinViaRing = heavy ? 0.178 : 0.127,
                        MinHoleToHole = 0.406, MinViaHoleToHole = 0.279, EdgeClearance = 0.25,
                        MinMaskWeb = 0.1016, MinSilkStroke = 0.15, MinSilkHeight = 0.8,
                        MaxLayers = 32, BlindBuriedVias = true,
                    };
                case "oshpark":
                    // docs.oshpark.com/services/two-layer and four-layer: 6/6 mil, ring 5 mil (4-layer 5/5 mil, ring 4 mil); drill
                    // 10 mil; 5 mil between holes; 15 mil edge keepout; mask web 4 mil; legend 5 mil lines; 1 oz (2 oz on the
                    // 0.8 mm two-layer service); no blind or buried vias
                    return new FabRuleSet
                    {
                        Id = "oshpark", Name = Name("OSH Park"), Source = "docs.oshpark.com/services (Kasım 2026)",
                        MinTrackWidth = multi ? 0.127 : 0.1524, MinClearance = multi ? 0.127 : 0.1524,
                        MinDrill = 0.254, MinPadRing = multi ? 0.1016 : 0.127, MinViaRing = multi ? 0.1016 : 0.127,
                        MinHoleToHole = 0.127, MinViaHoleToHole = 0.127, EdgeClearance = 0.381,
                        MinMaskWeb = 0.1016, MinSilkStroke = 0.127, MinSilkHeight = 0,
                        MaxLayers = 4, MaxCopperUm = multi ? 35 : 70, BlindBuriedVias = false,
                    };
                default:
                    return null;
            }
        }

        /// <summary>The DRC rules a fab's limits stand for: its spacing, widths, holes, rings and edge.</summary>
        private static readonly HashSet<string> FabDrc = new HashSet<string>(StringComparer.Ordinal)
        {
            "Yol-yol aralığı", "Yol-ped aralığı", "Yol-delik aralığı", "Ped-ped aralığı", "Via-yol aralığı", "Via-ped aralığı", "Via-via aralığı",
            "Minimum yol genişliği", "Minimum delik", "Halka genişliği", "Delik aralığı", "Delik-delik aralığı", "Kart kenarı", "Kör/gömülü via",
        };

        /// <summary>Where <paramref name="board"/> goes past <paramref name="set"/>: the board's own checkers run on a copy that has the fab's minimums as its rules.</summary>
        public static List<DrcViolation> Check(BoardDocument board, FabRuleSet set)
        {
            var issues = new List<DrcViolation>();
            if (board.Stackup.CopperLayers > set.MaxLayers)
                issues.Add(new DrcViolation(DrcSeverity.Error, "Katman sayısı", string.Format(CultureInfo.CurrentCulture, "{0} katman; üretici en çok {1} katman yapıyor", board.Stackup.CopperLayers, set.MaxLayers), 0, 0));
            if (set.MaxCopperUm > 0 && board.Rules.CopperThicknessUm > set.MaxCopperUm + 0.5)
                issues.Add(new DrcViolation(DrcSeverity.Error, "Bakır ağırlığı", string.Format(CultureInfo.CurrentCulture, "dış bakır {0:0.#} oz; bu katman sayısında en çok {1:0.#} oz", board.Rules.CopperThicknessUm / 35.0, set.MaxCopperUm / 35.0), 0, 0));

            var copy = board.Clone();
            var r = copy.Rules;
            r.MinTrackWidth = set.MinTrackWidth;
            r.MinDrill = set.MinDrill;
            r.MinAnnularRing = set.MinPadRing;
            r.MinViaAnnularRing = set.MinViaRing;
            r.MinHoleToHole = set.MinHoleToHole;
            r.MinViaHoleToHole = set.MinViaHoleToHole;
            r.EdgeClearance = set.EdgeClearance;
            r.HoleClearance = set.MinClearance;
            r.Clearance = set.MinClearance;
            foreach (var c in copy.NetClasses) c.Clearance = set.MinClearance;
            copy.Stackup.AllowBlindBuriedVias = board.Stackup.AllowBlindBuriedVias && set.BlindBuriedVias;
            issues.AddRange(DesignRuleChecker.Check(copy).Where(v => FabDrc.Contains(v.Rule)));

            var dfm = new DfmRules { MinMaskWeb = set.MinMaskWeb, MinSilkStroke = set.MinSilkStroke, MinSilkHeight = set.MinSilkHeight };
            issues.AddRange(DfmChecker.Check(copy, dfm).Issues.Where(v => v.Rule == "DFM · Maske köprüsü" || (set.MinSilkStroke > 0 && v.Rule == "DFM · Okunmaz serigrafi")));
            return issues;
        }

        /// <summary>[fab-ready] The board against its fab's limits, or "no fab chosen".</summary>
        public static ReadinessItem Readiness(BoardDocument board)
        {
            const string name = "Üreticinin kural seti";
            var set = string.IsNullOrEmpty(board.Fab) ? null : For(board.Fab, board);
            if (set == null)
                return new ReadinessItem(name, ReadinessState.NotChecked, "Üretici seçilmedi: kartın kendi kuralları kullanıldı (Kart ayarları → Üretici)");
            var issues = Check(board, set);
            if (issues.Count == 0)
                return new ReadinessItem(name, ReadinessState.Pass, $"{set.Name} sınırları içinde: {set.Limits}. Kaynak: {set.Source}");
            bool fatal = issues.Any(i => i.Rule == "Katman sayısı" || i.Rule == "Bakır ağırlığı");
            return new ReadinessItem(name, fatal ? ReadinessState.Fail : ReadinessState.Warning,
                $"{set.Name}: {issues.Count} yerde sınır aşılıyor; ilki: {issues[0].Rule}: {issues[0].Message}");
        }
    }
}
