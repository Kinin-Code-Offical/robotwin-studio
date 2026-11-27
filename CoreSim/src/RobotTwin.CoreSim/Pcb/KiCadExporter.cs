using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>
    /// KiCad 8 export: board (.kicad_pcb), schematic (.kicad_sch), netlist (.net),
    /// footprint library (RobotWin.pretty/*.kicad_mod + fp-lib-table), symbol library
    /// (RobotWin.kicad_sym + sym-lib-table) and project (.kicad_pro), plus a zip that
    /// bundles them with the SPICE netlist and the Gerber set.
    ///
    /// Coordinate conventions. Our board and KiCad both use millimetres with Y pointing
    /// down, so board coordinates are written unchanged (no Y flip as in Gerber).
    /// KiCad stores footprint children (pads, graphics, fields) in footprint-local
    /// coordinates and computes absolute positions as
    /// <c>footprint.at + RotatePoint(local, footprint.angle)</c>, where RotatePoint(p, 90)
    /// maps (x, y) to (y, -x) — counter-clockwise on a Y-down screen. That is exactly
    /// <see cref="BoardComponent.ToBoard"/>'s quarter-turn table, so a top-side part is
    /// written with angle = Rotation and the library pad offsets unchanged. A pad's own
    /// <c>at</c> angle is its absolute orientation, i.e. the footprint angle.
    ///
    /// Bottom-side parts. ToBoard mirrors X before rotating: R(rot)·(-x, y). KiCad's own
    /// flip (FOOTPRINT::Flip, default "top/bottom" axis) mirrors the library geometry in
    /// Y and negates the angle, so the canonical KiCad form of a flipped footprint has
    /// local coordinates (x, -y). Since R(rot)·(-x, y) = R(rot + 180)·(x, -y), a bottom
    /// part is written on B.Cu with local (x, -y) and angle rot + 180. KiCad then computes
    /// exactly the same absolute pad positions as <see cref="BoardDocument.Pads"/>, and a
    /// later "Update footprint from library" (which re-flips the library footprint the
    /// same way and keeps the angle) reproduces the same geometry.
    /// </summary>
    public static class KiCadExporter
    {
        public const string DefaultLibrary = "RobotWin";
        public const string BoardFileVersion = "20240108";
        public const string SchematicFileVersion = "20231120";
        public const string Generator = "robotwin_studio";
        public const string GeneratorVersion = "8.0";

        /// <summary>One schematic grid unit (0.1 in, see <see cref="PinDefinition"/>) in millimetres.</summary>
        public const double SchematicGridMm = 2.54;

        private const double SilkWidth = 0.15;        // same as the Gerber silkscreen
        private const double FabWidth = 0.1;
        private const double CourtyardWidth = 0.05;
        private const double EdgeWidth = 0.05;
        private const double MaskExpansion = 0.05;    // same as GerberExporter.Mask
        private const double RoundRectRatio = 0.25;

        // ================================================================== board

        /// <summary>
        /// Writes a .kicad_pcb. When <paramref name="embedDesignRules"/> is true the design
        /// rules are also written into <c>setup</c> with the board-level tokens KiCad's
        /// parser still accepts (trace_min, via_min_size, user_trace_width, ...), so the
        /// file is self-contained. KiCad 8 treats those as legacy settings: they take
        /// precedence over the project's design_settings and are moved into the project on
        /// the next save. <see cref="WriteProjectZip"/> therefore passes false and lets the
        /// .kicad_pro carry the rules, which is where KiCad 8 keeps them.
        /// </summary>
        public static string ExportPcb(BoardDocument board, string title, bool embedDesignRules = true)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            var nets = BoardNets(board);
            var codes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < nets.Count; i++) codes[nets[i]] = i;

            // Pads must carry the net table's exact spelling: KiCad orphans a pad whose net
            // name differs from the name registered for its code.
            (int Code, string Name) NetOf(string? net) =>
                !string.IsNullOrEmpty(net) && codes.TryGetValue(net!, out int c) ? (c, nets[c]) : (0, string.Empty);

            var root = new SNode("kicad_pcb",
                new SNode("version", BoardFileVersion),
                new SNode("generator", Q(Generator)),
                new SNode("generator_version", Q(GeneratorVersion)),
                new SNode("general", new SNode("thickness", N(board.Stackup.Thickness)), new SNode("legacy_teardrops", "no")),
                new SNode("paper", Q(Paper(board.Width + 20, board.Height + 20))),
                new SNode("title_block", new SNode("title", Q(title ?? string.Empty))),
                PcbLayerTable(board),
                PcbSetup(board, embedDesignRules));

            for (int i = 0; i < nets.Count; i++) root.Add(new SNode("net", I(i), Q(nets[i])));

            foreach (var comp in board.Components)
            {
                var fp = FootprintLibrary.Get(comp.FootprintId);
                if (fp == null) continue;
                root.Add(FootprintNode(fp, comp, DefaultLibrary, NetOf));
            }

            for (int i = 0; i < board.MountingHoles.Count; i++) root.Add(MountingHoleNode(board.MountingHoles[i], i, NetOf));

            root.Add(Outline(board));
            root.Add(Drawings(board));
            root.Add(GraphicNodes(board));   // [graphics]

            for (int i = 0; i < board.Tracks.Count; i++)
            {
                var t = board.Tracks[i];
                root.Add(new SNode("segment",
                    XY("start", t.X1, t.Y1),
                    XY("end", t.X2, t.Y2),
                    new SNode("width", N(t.Width)),
                    new SNode("layer", Q(CopperName(t.Layer))),
                    new SNode("net", I(NetOf(t.Net).Code)),
                    Uid("segment:" + i.ToString(CultureInfo.InvariantCulture))));
            }

            int layerCount = board.LayerCount;

            for (int i = 0; i < board.Vias.Count; i++)
            {
                var v = board.Vias[i];
                var span = v.Span(layerCount);
                root.Add(new SNode("via",
                    v.IsThrough(layerCount) ? null : "blind",
                    XY("at", v.X, v.Y),
                    new SNode("size", N(v.Diameter)),
                    new SNode("drill", N(v.Drill)),
                    new SNode("layers", Q(CopperName(span.From)), Q(CopperName(span.To))),
                    new SNode("net", I(NetOf(v.Net).Code)),
                    Uid("via:" + i.ToString(CultureInfo.InvariantCulture))));
            }

            var boardZoneOutline = ZoneOutline(board);
            for (int i = 0; i < board.Zones.Count; i++)
            {
                var z = board.Zones[i];
                var net = NetOf(z.Net);
                var rules = board.Rules;
                var outline = z.IsWholeBoard ? boardZoneOutline : z.Outline;
                double clearance = z.Clearance > 0 ? z.Clearance : board.ClassFor(z.Net).Clearance;
                string? connect = z.PadConnection == ZonePadConnection.Solid ? "yes" : z.PadConnection == ZonePadConnection.None ? "no" : null;
                int island = z.IslandRemoval == IslandRemoval.Never ? 1 : z.IslandRemoval == IslandRemoval.BelowArea ? 2 : 0;
                var fill = new SNode("fill", "yes",
                    new SNode("thermal_gap", N(z.ThermalGap)),
                    new SNode("thermal_bridge_width", N(z.ThermalSpokeWidth)),
                    new SNode("island_removal_mode", I(island)));
                if (island == 2) fill.Add(new SNode("island_area_min", N(z.MinIslandArea)));
                var node = new SNode("zone",
                    new SNode("net", I(net.Code)),
                    new SNode("net_name", Q(net.Name)),
                    new SNode("layer", Q(CopperName(z.Layer))),
                    Uid("zone:" + i.ToString(CultureInfo.InvariantCulture)),
                    string.IsNullOrEmpty(z.Name) ? null : new SNode("name", Q(z.Name)),
                    new SNode("hatch", "edge", "0.5"),
                    z.Priority > 0 ? new SNode("priority", I(z.Priority)) : null,
                    new SNode("connect_pads", connect, new SNode("clearance", N(clearance))),
                    new SNode("min_thickness", N(Math.Max(z.MinWidth, 0.01))),
                    new SNode("filled_areas_thickness", "no"),
                    fill,
                    new SNode("polygon", new SNode("pts", outline.Select(p => XY("xy", p.X, p.Y)))));
                if (z.IsFilled)
                {
                    foreach (var region in z.FilledRegions)
                    {
                        node.Add(new SNode("filled_polygon", new SNode("layer", Q(CopperName(z.Layer))),
                            new SNode("pts", Polygon.Fracture(region).Select(p => XY("xy", p.X, p.Y)))));
                    }
                }

                root.Add(node);
            }

            // [keepout] rule areas as KiCad keepout zones on their layers
            for (int i = 0; i < board.RuleAreas.Count; i++)
            {
                var a = board.RuleAreas[i];
                if (a.Outline.Count < 3) continue;
                string Allowed(bool no) => no ? "not_allowed" : "allowed";
                var layers = a.Layers.Count == 0 ? new SNode("layers", Q("*.Cu")) : new SNode("layers", a.Layers.Select(l => (object)Q(CopperName(l))).ToArray());
                root.Add(new SNode("zone",
                    new SNode("net", I(0)), new SNode("net_name", Q(string.Empty)), layers,
                    Uid("keepout:" + i.ToString(CultureInfo.InvariantCulture)),
                    string.IsNullOrEmpty(a.Name) ? null : new SNode("name", Q(a.Name)),
                    new SNode("hatch", "full", "0.5"),
                    new SNode("connect_pads", new SNode("clearance", N(0))),
                    new SNode("min_thickness", N(0.25)),
                    new SNode("keepout", new SNode("tracks", Allowed(a.NoTracks)), new SNode("vias", Allowed(a.NoVias)), new SNode("pads", "allowed"),
                        new SNode("copperpour", Allowed(a.NoPour)), new SNode("footprints", Allowed(a.NoParts))),
                    new SNode("fill", new SNode("thermal_gap", N(0.5)), new SNode("thermal_bridge_width", N(0.5))),
                    new SNode("polygon", new SNode("pts", a.Outline.Select(p => XY("xy", p.X, p.Y))))));
            }

            return Render(root);
        }

        /// <summary>Net table: index = KiCad net code, entry 0 is the unconnected net "".</summary>
        private static List<string> BoardNets(BoardDocument board)
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Add(string? net)
            {
                if (!string.IsNullOrEmpty(net) && !seen.ContainsKey(net!)) seen[net!] = net!;
            }

            foreach (var comp in board.Components)
            {
                foreach (var kv in comp.PadNets) Add(kv.Value);
            }

            foreach (var t in board.Tracks) Add(t.Net);
            foreach (var v in board.Vias) Add(v.Net);
            foreach (var z in board.Zones) Add(z.Net);
            foreach (var h in board.MountingHoles) Add(h.Net);
            var list = seen.Values.OrderBy(n => n, StringComparer.Ordinal).ToList();
            list.Insert(0, string.Empty);
            return list;
        }

        private static string CopperName(CopperLayer layer) => PcbLayers.KiCadName(layer);

        private static SNode PcbLayerTable(BoardDocument board)
        {
            var layers = new SNode("layers");
            foreach (var l in board.CopperLayers())
            {
                layers.Add(new SNode(PcbLayers.KiCadOrdinal(l).ToString(CultureInfo.InvariantCulture), Q(CopperName(l)), "signal"));
            }

            layers.Add(
                new SNode("32", Q("B.Adhes"), "user", Q("B.Adhesive")),
                new SNode("33", Q("F.Adhes"), "user", Q("F.Adhesive")),
                new SNode("34", Q("B.Paste"), "user"),
                new SNode("35", Q("F.Paste"), "user"),
                new SNode("36", Q("B.SilkS"), "user", Q("B.Silkscreen")),
                new SNode("37", Q("F.SilkS"), "user", Q("F.Silkscreen")),
                new SNode("38", Q("B.Mask"), "user"),
                new SNode("39", Q("F.Mask"), "user"),
                new SNode("40", Q("Dwgs.User"), "user", Q("User.Drawings")),
                new SNode("41", Q("Cmts.User"), "user", Q("User.Comments")),
                new SNode("42", Q("Eco1.User"), "user", Q("User.Eco1")),
                new SNode("43", Q("Eco2.User"), "user", Q("User.Eco2")),
                new SNode("44", Q("Edge.Cuts"), "user"),
                new SNode("45", Q("Margin"), "user"),
                new SNode("46", Q("B.CrtYd"), "user", Q("B.Courtyard")),
                new SNode("47", Q("F.CrtYd"), "user", Q("F.Courtyard")),
                new SNode("48", Q("B.Fab"), "user"),
                new SNode("49", Q("F.Fab"), "user"));
            for (int i = 1; i <= 9; i++)
            {
                layers.Add(new SNode((49 + i).ToString(CultureInfo.InvariantCulture), Q("User." + i.ToString(CultureInfo.InvariantCulture)), "user"));
            }

            return layers;
        }

        /// <summary>KiCad stackup description (copper, dielectrics, mask, paste, silk).</summary>
        private static SNode Stackup(BoardDocument board)
        {
            var st = board.Stackup;
            var node = new SNode("stackup",
                new SNode("layer", Q("F.SilkS"), new SNode("type", Q("Top Silk Screen"))),
                new SNode("layer", Q("F.Paste"), new SNode("type", Q("Top Solder Paste"))),
                new SNode("layer", Q("F.Mask"), new SNode("type", Q("Top Solder Mask")), new SNode("thickness", N(st.SolderMaskThickness))));
            foreach (var l in st.Build(board.Rules.CopperThicknessUm))
            {
                if (l.Type == "copper")
                {
                    node.Add(new SNode("layer", Q(l.Name), new SNode("type", Q("copper")), new SNode("thickness", N(l.Thickness))));
                }
                else
                {
                    node.Add(new SNode("layer", Q(l.Name), new SNode("type", Q(l.Type)), new SNode("thickness", N(l.Thickness)),
                        new SNode("material", Q(l.Material)), new SNode("epsilon_r", N(l.EpsilonR)), new SNode("loss_tangent", N(l.LossTangent))));
                }
            }

            node.Add(
                new SNode("layer", Q("B.Mask"), new SNode("type", Q("Bottom Solder Mask")), new SNode("thickness", N(st.SolderMaskThickness))),
                new SNode("layer", Q("B.Paste"), new SNode("type", Q("Bottom Solder Paste"))),
                new SNode("layer", Q("B.SilkS"), new SNode("type", Q("Bottom Silk Screen"))),
                new SNode("copper_finish", Q(st.Finish)),
                new SNode("dielectric_constraints", "no"));
            return node;
        }

        private static SNode PcbSetup(BoardDocument board, bool embedRules)
        {
            var rules = board.Rules;
            var setup = new SNode("setup",
                Stackup(board),
                new SNode("pad_to_mask_clearance", N(MaskExpansion)),
                new SNode("allow_soldermask_bridges_in_footprints", "no"));
            // [origins] KiCad's drill/place file origin and grid origin, where this board has them
            if (board.AuxOrigin is { } aux) setup.Add(new SNode("aux_axis_origin", N(aux.X), N(aux.Y)));
            if (board.GridOrigin is { } grid) setup.Add(new SNode("grid_origin", N(grid.X), N(grid.Y)));
            if (embedRules)
            {
                // Board-level rule tokens of the KiCad 5 board format; KiCad 6-8 still parse
                // them (and migrate them into the project). Copper-to-edge clearance has no
                // such token and is only carried by the .kicad_pro.
                setup.Add(
                    new SNode("user_trace_width", N(rules.TrackWidth)),
                    new SNode("user_trace_width", N(rules.PowerTrackWidth)),
                    new SNode("trace_clearance", N(rules.Clearance)),
                    new SNode("zone_clearance", N(rules.Clearance)),
                    new SNode("trace_min", N(rules.MinTrackWidth)),
                    new SNode("via_size", N(rules.ViaDiameter)),
                    new SNode("via_drill", N(rules.ViaDrill)),
                    new SNode("via_min_size", N(MinViaDiameter(rules))),
                    new SNode("via_min_drill", N(rules.MinDrill)));
            }

            return setup;
        }

        private static double MinViaDiameter(DesignRules rules) => Math.Min(rules.ViaDiameter, rules.MinDrill + 0.2);

        /// <summary>
        /// Footprint as a library file (<paramref name="comp"/> null) or embedded in a board.
        /// See the class summary for the rotation and bottom-side mapping.
        /// </summary>
        private static SNode FootprintNode(Footprint fp, BoardComponent? comp, string libName,
            Func<string?, (int Code, string Name)>? netOf)
        {
            bool onBoard = comp != null;
            bool flip = onBoard && comp!.Side == CopperLayer.Bottom;
            double angle = onBoard ? FootprintAngle(comp!) : 0.0;
            string seed = onBoard ? "footprint:" + comp!.RefDes.ToUpperInvariant() : "module:" + fp.Id;

            (double X, double Y) P(double x, double y) => flip ? (x, -y) : (x, y);
            string Layer(string front) => flip ? "B" + front.Substring(1) : front;

            var node = new SNode("footprint", Q(onBoard ? libName + ":" + fp.Id : fp.Id));
            if (!onBoard)
            {
                node.Add(new SNode("version", BoardFileVersion), new SNode("generator", Q(Generator)),
                    new SNode("generator_version", Q(GeneratorVersion)));
            }

            node.Add(new SNode("layer", Q(flip ? "B.Cu" : "F.Cu")));
            if (onBoard) node.Add(Uid(seed), At(comp!.X, comp.Y, angle));
            if (!string.IsNullOrEmpty(fp.Description)) node.Add(new SNode("descr", Q(fp.Description)));

            double minX = fp.CourtyardMinX, minY = fp.CourtyardMinY, maxX = fp.CourtyardMaxX, maxY = fp.CourtyardMaxY;
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            string value = onBoard ? (string.IsNullOrEmpty(comp!.Value) ? fp.Id : comp.Value) : fp.Id;
            node.Add(
                FpProperty("Reference", onBoard ? comp!.RefDes : "REF**", P(cx, minY - 1.0), angle, Layer("F.SilkS"), false, false, flip, 1.0, seed + ":reference"),
                FpProperty("Value", value, P(cx, maxY + 1.0), angle, Layer("F.Fab"), false, false, flip, 1.0, seed + ":value"),
                FpProperty("Footprint", onBoard ? libName + ":" + fp.Id : string.Empty, P(cx, cy), angle, Layer("F.Fab"), true, true, flip, 1.27, seed + ":footprint"),
                FpProperty("Datasheet", string.Empty, P(cx, cy), angle, Layer("F.Fab"), true, true, flip, 1.27, seed + ":datasheet"),
                FpProperty("Description", fp.Description, P(cx, cy), angle, Layer("F.Fab"), true, true, flip, 1.27, seed + ":description"));
            if (onBoard) node.Add(new SNode("path", Q("/" + SymbolUuid(comp!.RefDes))));
            if (fp.Pads.Count > 0) node.Add(new SNode("attr", fp.Pads.Any(p => p.IsThroughHole) ? "through_hole" : "smd"));

            // Silkscreen: the footprint's own strokes (what the Gerber exporter plots); parts
            // that have none get their courtyard outline so they are visible on the legend.
            int g = 0;
            string Next() => seed + ":g" + (g++).ToString(CultureInfo.InvariantCulture);
            foreach (var s in fp.Silkscreen) node.Add(FpLine(P(s.X1, s.Y1), P(s.X2, s.Y2), SilkWidth, Layer("F.SilkS"), Next()));
            foreach (var (x, y, r) in fp.SilkCircles) node.Add(FpCircle(P(x, y), r, SilkWidth, Layer("F.SilkS"), Next()));
            if (fp.Silkscreen.Count == 0 && fp.SilkCircles.Count == 0)
            {
                node.Add(FpRect(P(minX, minY), P(maxX, maxY), SilkWidth, Layer("F.SilkS"), Next()));
            }

            node.Add(FpRect(P(minX, minY), P(maxX, maxY), CourtyardWidth, Layer("F.CrtYd"), Next()));
            if (fp.FabLines.Count > 0)
            {
                foreach (var s in fp.FabLines) node.Add(FpLine(P(s.X1, s.Y1), P(s.X2, s.Y2), FabWidth, Layer("F.Fab"), Next()));
            }
            else
            {
                double inset = Math.Min(0.25, Math.Min(maxX - minX, maxY - minY) / 4);
                node.Add(FpRect(P(minX + inset, minY + inset), P(maxX - inset, maxY - inset), FabWidth, Layer("F.Fab"), Next()));
            }

            foreach (var t in fp.Texts)
            {
                var at = P(t.X, t.Y);
                var effects = new SNode("effects", new SNode("font", new SNode("size", N(t.Size), N(t.Size)), new SNode("thickness", N(0.15))));
                if (flip) effects.Add(new SNode("justify", "mirror"));
                node.Add(new SNode("fp_text", "user", Q(t.Text), new SNode("at", N(at.X), N(at.Y), N(angle)),
                    new SNode("layer", Q(Layer(t.Layer == GraphicLayer.FabTop ? "F.Fab" : "F.SilkS"))), Uid(Next()), effects));
            }

            for (int i = 0; i < fp.Pads.Count; i++)
            {
                var pad = fp.Pads[i];
                var net = (Code: 0, Name: string.Empty);
                if (onBoard && netOf != null && comp!.PadNets.TryGetValue(pad.Name, out var n)) net = netOf(n);
                node.Add(PadNode(pad, P(pad.X, pad.Y), angle, flip, net.Code, net.Name, seed + ":pad" + i.ToString(CultureInfo.InvariantCulture)));
            }

            return node;
        }

        /// <summary>KiCad footprint angle in (-180, 180]; bottom parts add 180 (see class summary).</summary>
        private static double FootprintAngle(BoardComponent comp)
        {
            int r = ((comp.Rotation % 360) + 360) % 360;
            if (r % 90 != 0) r = 0;   // BoardComponent.ToBoard only knows quarter turns
            if (comp.Side == CopperLayer.Bottom) r = (r + 180) % 360;
            return r > 180 ? r - 360 : r;
        }

        private static SNode PadNode(Pad pad, (double X, double Y) local, double angle, bool flip, int netCode, string netName, string seed)
        {
            string shape;
            switch (pad.Shape)
            {
                case PadShape.Circle: shape = "circle"; break;
                case PadShape.Rect: shape = "rect"; break;
                case PadShape.Oval: shape = "oval"; break;
                default: shape = "roundrect"; break;
            }

            double w = pad.Width, h = pad.Height;
            if (pad.Shape == PadShape.Circle) w = h = Math.Max(w, h);

            var node = new SNode("pad", Q(pad.NonPlated ? string.Empty : pad.Name), pad.IsThroughHole ? (pad.NonPlated ? "np_thru_hole" : "thru_hole") : "smd", shape,
                At(local.X, local.Y, angle),
                new SNode("size", N(w), N(h)));
            if (pad.IsThroughHole)
            {
                node.Add(new SNode("drill", N(pad.Drill)), new SNode("layers", Q("*.Cu"), Q("*.Mask")));
            }
            else
            {
                string side = (pad.Layer == CopperLayer.Bottom) != flip ? "B" : "F";
                node.Add(new SNode("layers", Q(side + ".Cu"), Q(side + ".Paste"), Q(side + ".Mask")));
            }

            if (pad.Shape == PadShape.RoundRect) node.Add(new SNode("roundrect_rratio", N(Math.Max(0, Math.Min(0.5, pad.CornerRatio > 0 ? pad.CornerRatio : RoundRectRatio)))));
            if (netCode > 0) node.Add(new SNode("net", I(netCode), Q(netName)));
            node.Add(Uid(seed));
            return node;
        }

        private static SNode FpProperty(string name, string value, (double X, double Y) at, double angle, string layer,
            bool hide, bool unlocked, bool mirror, double size, string seed)
        {
            var effects = new SNode("effects",
                new SNode("font", new SNode("size", N(size), N(size)), new SNode("thickness", N(0.15))));
            if (mirror) effects.Add(new SNode("justify", "mirror"));
            return new SNode("property", Q(name), Q(value),
                new SNode("at", N(at.X), N(at.Y), N(angle)),
                unlocked ? new SNode("unlocked", "yes") : null,
                new SNode("layer", Q(layer)),
                hide ? new SNode("hide", "yes") : null,
                Uid(seed),
                effects);
        }

        private static SNode Stroke(double width, string type = "solid") =>
            new SNode("stroke", new SNode("width", N(width)), new SNode("type", type));

        private static SNode FpLine((double X, double Y) a, (double X, double Y) b, double width, string layer, string seed) =>
            new SNode("fp_line", XY("start", a.X, a.Y), XY("end", b.X, b.Y), Stroke(width), new SNode("layer", Q(layer)), Uid(seed));

        private static SNode FpRect((double X, double Y) a, (double X, double Y) b, double width, string layer, string seed) =>
            new SNode("fp_rect", XY("start", a.X, a.Y), XY("end", b.X, b.Y), Stroke(width), new SNode("fill", "none"),
                new SNode("layer", Q(layer)), Uid(seed));

        private static SNode FpCircle((double X, double Y) c, double r, double width, string layer, string seed) =>
            new SNode("fp_circle", XY("center", c.X, c.Y), XY("end", c.X + r, c.Y), Stroke(width), new SNode("fill", "none"),
                new SNode("layer", Q(layer)), Uid(seed));

        /// <summary>Board outline on Edge.Cuts: a rectangle, or lines and tangent arcs (rounded rectangle or polygon).</summary>
        private static IEnumerable<SNode> Outline(BoardDocument board)
        {
            double w = board.Width, h = board.Height;
            double r = Math.Max(0.0, Math.Min(board.CornerRadius, Math.Min(w, h) / 2));
            SNode Layer() => new SNode("layer", Q("Edge.Cuts"));
            bool polygon = board.Outline != null && board.Outline.Count >= 3;
            if (!polygon && r <= 1e-9)
            {
                yield return new SNode("gr_rect", XY("start", 0, 0), XY("end", w, h), Stroke(EdgeWidth, "default"),
                    new SNode("fill", "none"), Layer(), Uid("edge:rect"));
                yield break;
            }

            int li = 0, ai = 0;
            foreach (var e in board.OutlineElements())
            {
                if (e.IsArc)
                {
                    yield return new SNode("gr_arc", XY("start", e.X1, e.Y1), XY("mid", e.MidX, e.MidY), XY("end", e.X2, e.Y2),
                        Stroke(EdgeWidth, "default"), Layer(), Uid("edge:arc" + (ai++).ToString(CultureInfo.InvariantCulture)));
                }
                else
                {
                    yield return new SNode("gr_line", XY("start", e.X1, e.Y1), XY("end", e.X2, e.Y2), Stroke(EdgeWidth, "default"),
                        Layer(), Uid("edge:line" + (li++).ToString(CultureInfo.InvariantCulture)));
                }
            }
        }

        private static string GraphicLayerName(GraphicLayer l) => l switch
        {
            GraphicLayer.SilkTop => "F.SilkS",
            GraphicLayer.SilkBottom => "B.SilkS",
            GraphicLayer.FabTop => "F.Fab",
            GraphicLayer.FabBottom => "B.Fab",
            _ => "Dwgs.User"
        };

        /// <summary>Board texts (gr_text) and dimensions (lines and a label on their layer).</summary>
        private static IEnumerable<SNode> Drawings(BoardDocument board)
        {
            for (int i = 0; i < board.Texts.Count; i++)
            {
                var t = board.Texts[i];
                var effects = new SNode("effects", new SNode("font", new SNode("size", N(t.Size), N(t.Size)), new SNode("thickness", N(t.Thickness))));
                if (t.Mirrored) effects.Add(new SNode("justify", "mirror"));
                yield return new SNode("gr_text", Q(t.Text), new SNode("at", N(t.X), N(t.Y), N(-t.Rotation)),
                    new SNode("layer", Q(GraphicLayerName(t.Layer))), Uid("text:" + i.ToString(CultureInfo.InvariantCulture)), effects);
            }

            for (int i = 0; i < board.Dimensions.Count; i++)
            {
                var d = board.Dimensions[i];
                var geo = d.Geometry();
                string layer = GraphicLayerName(d.Layer);
                int k = 0;
                foreach (var l in geo.Lines)
                {
                    yield return new SNode("gr_line", XY("start", l.X1, l.Y1), XY("end", l.X2, l.Y2), Stroke(0.1, "default"),
                        new SNode("layer", Q(layer)), Uid("dim:" + i.ToString(CultureInfo.InvariantCulture) + ":" + (k++).ToString(CultureInfo.InvariantCulture)));
                }

                double angle = geo.Angle;
                if (angle > 90.0001 || angle <= -90) angle += 180;
                if (d.Label().Length == 0) continue;   // [dims] a centre mark has no text
                yield return new SNode("gr_text", Q(d.Label()), new SNode("at", N(geo.TextX), N(geo.TextY), N(-angle)),
                    new SNode("layer", Q(layer)), Uid("dim:" + i.ToString(CultureInfo.InvariantCulture) + ":text"),
                    new SNode("effects", new SNode("font", new SNode("size", N(d.TextSize), N(d.TextSize)), new SNode("thickness", N(0.12)))));
            }
        }

        /// <summary>[graphics] Drawn shapes as KiCad's gr_line / gr_rect / gr_circle / gr_arc / gr_poly on their layers.</summary>
        private static IEnumerable<SNode> GraphicNodes(BoardDocument board)
        {
            for (int i = 0; i < board.Graphics.Count; i++)
            {
                var g = board.Graphics[i];
                if (!g.IsComplete) continue;
                var p = g.Points;
                string layer = GraphicLayerName(g.Layer);
                var fill = new SNode("fill", g.Filled ? "solid" : "none");
                var uid = Uid("graphic:" + i.ToString(CultureInfo.InvariantCulture));
                switch (g.Shape)
                {
                    case GraphicShape.Line:
                        yield return new SNode("gr_line", XY("start", p[0].X, p[0].Y), XY("end", p[1].X, p[1].Y), Stroke(g.Width), new SNode("layer", Q(layer)), uid);
                        break;
                    case GraphicShape.Rectangle:
                        yield return new SNode("gr_rect", XY("start", p[0].X, p[0].Y), XY("end", p[1].X, p[1].Y), Stroke(g.Width), fill, new SNode("layer", Q(layer)), uid);
                        break;
                    case GraphicShape.Circle:
                        yield return new SNode("gr_circle", XY("center", p[0].X, p[0].Y), XY("end", p[1].X, p[1].Y), Stroke(g.Width), fill, new SNode("layer", Q(layer)), uid);
                        break;
                    case GraphicShape.Arc:
                    {
                        var (a0, sweep) = g.ArcAngles();
                        double r = g.Radius, am = a0 + sweep / 2, a1 = a0 + sweep;
                        yield return new SNode("gr_arc", XY("start", p[0].X + r * Math.Cos(a0), p[0].Y + r * Math.Sin(a0)), XY("mid", p[0].X + r * Math.Cos(am), p[0].Y + r * Math.Sin(am)),
                            XY("end", p[0].X + r * Math.Cos(a1), p[0].Y + r * Math.Sin(a1)), Stroke(g.Width), new SNode("layer", Q(layer)), uid);
                        break;
                    }
                    case GraphicShape.Polygon:
                    {
                        var pts = new SNode("pts");
                        foreach (var q in p) pts.Add(XY("xy", q.X, q.Y));
                        yield return new SNode("gr_poly", pts, Stroke(g.Width), fill, new SNode("layer", Q(layer)), uid);
                        break;
                    }
                }
            }
        }

        /// <summary>A mounting hole as a footprint with one (non-)plated hole.</summary>
        private static SNode MountingHoleNode(MountingHole h, int index, Func<string?, (int Code, string Name)> netOf)
        {
            string id = "MountingHole_" + h.Drill.ToString("0.0##", CultureInfo.InvariantCulture) + "mm" + (h.Plated ? "_Pad" : string.Empty);
            string seed = "hole:" + index.ToString(CultureInfo.InvariantCulture);
            double r = Math.Max(h.Drill, h.PadDiameter) / 2;
            var fp = new SNode("footprint", Q(DefaultLibrary + ":" + id),
                new SNode("layer", Q("F.Cu")),
                Uid(seed),
                At(h.X, h.Y, 0),
                new SNode("descr", Q(Localization.T("Montaj deliği Ø" + h.Drill.ToString("0.0##", CultureInfo.InvariantCulture) + " mm"))),   // [lang]
                FpProperty("Reference", "H" + (index + 1).ToString(CultureInfo.InvariantCulture), (0, -r - 1.0), 0, "F.SilkS", false, false, false, 1.0, seed + ":reference"),
                FpProperty("Value", "MountingHole", (0, r + 1.0), 0, "F.Fab", false, false, false, 1.0, seed + ":value"),
                FpProperty("Footprint", DefaultLibrary + ":" + id, (0, 0), 0, "F.Fab", true, true, false, 1.27, seed + ":footprint"),
                new SNode("attr", "exclude_from_pos_files", "exclude_from_bom"),
                FpCircle((0, 0), r + 0.25, CourtyardWidth, "F.CrtYd", seed + ":crtyd"),
                FpCircle((0, 0), r, SilkWidth, "Cmts.User", seed + ":outline"));
            var pad = h.Plated
                ? new SNode("pad", Q("1"), "thru_hole", "circle", At(0, 0, 0), new SNode("size", N(h.PadDiameter), N(h.PadDiameter)),
                    new SNode("drill", N(h.Drill)), new SNode("layers", Q("*.Cu"), Q("*.Mask")))
                : new SNode("pad", Q(string.Empty), "np_thru_hole", "circle", At(0, 0, 0), new SNode("size", N(h.Drill), N(h.Drill)),
                    new SNode("drill", N(h.Drill)), new SNode("layers", Q("*.Cu"), Q("*.Mask")));
            var net = netOf(h.Net);
            if (h.Plated && net.Code > 0) pad.Add(new SNode("net", I(net.Code), Q(net.Name)));
            pad.Add(Uid(seed + ":pad"));
            fp.Add(pad);
            return fp;
        }

        /// <summary>Board outline inset by the edge clearance (rounded corners as 8-segment chords).</summary>
        private static List<PcbPoint> ZoneOutline(BoardDocument board)
        {
            if (board.Outline != null && board.Outline.Count >= 3)
            {
                var inset = PolyOffset.Offset(PolyBool.Union(new[] { board.OutlinePolygon(0.05) }), -board.Rules.EdgeClearance, 0.05);
                var largest = inset.OrderByDescending(r => r.Area).FirstOrDefault();
                if (largest != null) return largest.Outer;
            }

            double e = board.Rules.EdgeClearance;
            double w = board.Width, h = board.Height;
            double r = Math.Max(0.0, Math.Min(board.CornerRadius, Math.Min(w, h) / 2));
            double ri = r - e;
            var pts = new List<PcbPoint>();
            if (ri <= 1e-6)
            {
                pts.Add(new PcbPoint(e, e));
                pts.Add(new PcbPoint(w - e, e));
                pts.Add(new PcbPoint(w - e, h - e));
                pts.Add(new PcbPoint(e, h - e));
                return pts;
            }

            const int Steps = 8;
            void Arc(double cx, double cy, double fromDeg)
            {
                for (int i = 0; i <= Steps; i++)
                {
                    double a = (fromDeg + 90.0 * i / Steps) * Math.PI / 180.0;
                    pts.Add(new PcbPoint(cx + ri * Math.Cos(a), cy + ri * Math.Sin(a)));
                }
            }

            Arc(r, r, 180);
            Arc(w - r, r, 270);
            Arc(w - r, h - r, 0);
            Arc(r, h - r, 90);
            return pts;
        }

        private static string Paper(double widthMm, double heightMm)
        {
            var sizes = new[] { ("A4", 297.0, 210.0), ("A3", 420.0, 297.0), ("A2", 594.0, 420.0), ("A1", 841.0, 594.0) };
            foreach (var (name, w, h) in sizes)
            {
                if (widthMm <= w && heightMm <= h) return name;
            }

            return "A0";
        }

        // ================================================================== footprint library

        /// <summary>A footprint as a KiCad 8 library file (RobotWin.pretty/&lt;id&gt;.kicad_mod).</summary>
        public static string ExportFootprint(Footprint fp)
        {
            if (fp == null) throw new ArgumentNullException(nameof(fp));
            return Render(FootprintNode(fp, null, DefaultLibrary, null));
        }

        /// <summary>Project footprint library table pointing at ${KIPRJMOD}/&lt;lib&gt;.pretty.</summary>
        public static string FootprintLibTable(string libName = DefaultLibrary) =>
            Render(new SNode("fp_lib_table", new SNode("version", "7"),
                new SNode("lib", new SNode("name", Q(libName)), new SNode("type", Q("KiCad")),
                    new SNode("uri", Q("${KIPRJMOD}/" + libName + ".pretty")), new SNode("options", Q(string.Empty)),
                    new SNode("descr", Q("RobotWin Studio footprints")))));

        /// <summary>Project symbol library table pointing at ${KIPRJMOD}/&lt;lib&gt;.kicad_sym.</summary>
        public static string SymbolLibTable(string libName = DefaultLibrary) =>
            Render(new SNode("sym_lib_table", new SNode("version", "7"),
                new SNode("lib", new SNode("name", Q(libName)), new SNode("type", Q("KiCad")),
                    new SNode("uri", Q("${KIPRJMOD}/" + libName + ".kicad_sym")), new SNode("options", Q(string.Empty)),
                    new SNode("descr", Q("RobotWin Studio symbols")))));

        // ================================================================== schematic model

        /// <summary>
        /// A generic box symbol for one part definition and one pin-to-pad mapping. Presets
        /// can remap pads (pinout "CBE", pinmap "OUT:6,..."), and KiCad pin numbers are fixed
        /// per library symbol, so each distinct mapping becomes its own symbol.
        /// </summary>
        private sealed class SymbolVariant
        {
            public string Name = string.Empty;
            public PartDefinition Def = null!;
            public List<(PinDefinition Pin, string Number)> Pins = new List<(PinDefinition Pin, string Number)>();
        }

        private sealed class ExportPart
        {
            public SchematicPart Part = null!;
            public PartDefinition Def = null!;
            public Dictionary<string, string> Parameters = null!;
            public SymbolVariant Symbol = null!;
            public Footprint? Footprint;
            public string Value = string.Empty;
        }

        /// <summary>Parts that become KiCad symbols: everything except ground/power-rail markers (they only name nets).</summary>
        private static List<ExportPart> ExportParts(Schematic schematic, PartLibrary library, out List<SymbolVariant> variants)
        {
            var byName = new Dictionary<string, SymbolVariant>(StringComparer.Ordinal);
            variants = new List<SymbolVariant>();
            var result = new List<ExportPart>();
            foreach (var part in schematic.Parts)
            {
                var def = library.Find(part.PartId);
                if (def == null || def.Symbol == SymbolKind.Ground || def.Symbol == SymbolKind.PowerRail) continue;
                var parameters = schematic.ResolveParameters(library, part);
                var pins = def.Pins.Select(p => (Pin: p, Number: PinNumber(def, p, parameters))).ToList();
                bool standard = pins.All(x => x.Number == (string.IsNullOrEmpty(x.Pin.Pad) ? x.Pin.Name : x.Pin.Pad));
                string name = SymbolName(def.Id) + (standard ? string.Empty : "_" + string.Join("-", pins.Select(x => SymbolName(x.Number))));
                string unique = name;
                for (int k = 2; ; k++)
                {
                    if (!byName.TryGetValue(unique, out var existing))
                    {
                        existing = new SymbolVariant { Name = unique, Def = def, Pins = pins };
                        byName[unique] = existing;
                        variants.Add(existing);
                    }

                    if (existing.Def == def && existing.Pins.Select(x => x.Number).SequenceEqual(pins.Select(x => x.Number)))
                    {
                        string fpId = def.ResolveFootprint(parameters);
                        result.Add(new ExportPart
                        {
                            Part = part,
                            Def = def,
                            Parameters = parameters,
                            Symbol = existing,
                            Footprint = string.IsNullOrWhiteSpace(fpId) ? null : FootprintLibrary.Get(fpId),
                            Value = PartValue(def, parameters)
                        });
                        break;
                    }

                    unique = name + "_" + k.ToString(CultureInfo.InvariantCulture);
                }
            }

            return result;
        }

        private static string PinNumber(PartDefinition def, PinDefinition pin, IReadOnlyDictionary<string, string> parameters)
        {
            string pad = def.ResolvePad(pin, parameters);
            return string.IsNullOrEmpty(pad) ? pin.Name : pad;
        }

        /// <summary>Same value text <see cref="BoardDocument.Synchronize"/> puts on the footprint.</summary>
        private static string PartValue(PartDefinition def, IReadOnlyDictionary<string, string> parameters) =>
            !string.IsNullOrEmpty(def.ValueKey) && parameters.TryGetValue(def.ValueKey, out var v) && !string.IsNullOrEmpty(v) ? v : def.Name;

        private static string SymbolName(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                sb.Append(char.IsLetterOrDigit(c) && c < 128 || c == '_' || c == '-' || c == '.' || c == '+' ? c : '_');
            }

            return sb.Length == 0 ? "_" : sb.ToString();
        }

        /// <summary>Symbol UUID shared by the schematic symbol, the netlist tstamp and the PCB footprint path.</summary>
        private static string SymbolUuid(string refDes) => Uuid("symbol:" + refDes.ToUpperInvariant());

        private static string PinKey(string refDes, string pin) => refDes + "\u0001" + pin;

        private static Dictionary<string, Net> PinNets(Netlist netlist)
        {
            var map = new Dictionary<string, Net>(StringComparer.OrdinalIgnoreCase);
            foreach (var net in netlist.Nets)
            {
                foreach (var c in net.Connections) map[PinKey(c.RefDes, c.Pin)] = net;
            }

            return map;
        }

        /// <summary>Outward direction of a pin (away from the box) in symbol grid coordinates, Y down.</summary>
        private static (double X, double Y) Outward(PartDefinition def, PinDefinition pin)
        {
            double ox = Math.Abs(pin.X) - def.HalfWidth, oy = Math.Abs(pin.Y) - def.HalfHeight;
            if (ox >= oy) return (pin.X < 0 ? -1 : 1, 0);
            return (0, pin.Y < 0 ? -1 : 1);
        }

        private static SNode SchFont() => new SNode("font", new SNode("size", "1.27", "1.27"));

        private static SNode SchProperty(string name, string value, double x, double y, double angle, bool hide)
        {
            var effects = new SNode("effects", SchFont());
            if (hide) effects.Add(new SNode("hide", "yes"));
            return new SNode("property", Q(name), Q(value), new SNode("at", N(x), N(y), N(angle)), effects);
        }

        /// <summary>
        /// Library symbol: a rectangle of the part's HalfWidth x HalfHeight with every pin at its
        /// <see cref="PinDefinition"/> tip. Library coordinates have Y up, so a pin at grid
        /// (x, y) sits at (2.54 x, -2.54 y) mm; the pin angle is the direction from the tip
        /// towards the body (0 = body to the right).
        /// </summary>
        private static SNode LibSymbol(SymbolVariant v, string fullName)
        {
            var def = v.Def;
            double hw = def.HalfWidth * SchematicGridMm, hh = def.HalfHeight * SchematicGridMm;
            var units = new SNode("symbol", Q(v.Name + "_1_1"));
            foreach (var (pin, number) in v.Pins)
            {
                var (dx, dy) = Outward(def, pin);
                double length = dx != 0 ? Math.Abs(pin.X) - def.HalfWidth : Math.Abs(pin.Y) - def.HalfHeight;
                int angle = dx < 0 ? 0 : dx > 0 ? 180 : dy < 0 ? 270 : 90;
                units.Add(new SNode("pin", "passive", "line",
                    new SNode("at", N(pin.X * SchematicGridMm), N(-pin.Y * SchematicGridMm), I(angle)),
                    new SNode("length", N(Math.Max(0.0, length) * SchematicGridMm)),
                    new SNode("name", Q(string.IsNullOrEmpty(pin.Name) ? "~" : pin.Name), new SNode("effects", SchFont())),
                    new SNode("number", Q(number), new SNode("effects", SchFont()))));
            }

            return new SNode("symbol", Q(fullName),
                new SNode("pin_names", new SNode("offset", N(1.016))),
                new SNode("exclude_from_sim", "no"),
                new SNode("in_bom", "yes"),
                new SNode("on_board", "yes"),
                SchProperty("Reference", def.RefPrefix, 0, hh + 1.27, 0, false),
                SchProperty("Value", v.Name, 0, -hh - 1.27, 0, false),
                SchProperty("Footprint", string.Empty, 0, 0, 0, true),
                SchProperty("Datasheet", string.Empty, 0, 0, 0, true),
                SchProperty("Description", def.Name, 0, 0, 0, true),
                new SNode("symbol", Q(v.Name + "_0_1"),
                    new SNode("rectangle", XY("start", -hw, hh), XY("end", hw, -hh), Stroke(0.254, "default"),
                        new SNode("fill", new SNode("type", "background")))),
                units);
        }

        private sealed class Stub
        {
            public GridPoint Tip;
            public double Dx, Dy;
            public string? Net;
            public double Length;

            public GridPoint End => new GridPoint(Tip.X + Dx * Length, Tip.Y + Dy * Length);

            public bool SameNet(Stub other) =>
                Net != null && other.Net != null && string.Equals(Net, other.Net, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gives every pin a stub of one grid unit (half a unit, or none, where a full stub
        /// would touch a pin tip or stub of another net), so labels can never short nets.
        /// </summary>
        private static void PlaceStubs(List<Stub> stubs)
        {
            var accepted = new List<Stub>();
            foreach (var s in stubs)
            {
                foreach (double length in new[] { 1.0, 0.5, 0.0 })
                {
                    s.Length = length;
                    if (length == 0.0 || !Conflicts(s, stubs, accepted)) break;
                }

                if (s.Length > 0) accepted.Add(s);
            }
        }

        private static bool Conflicts(Stub s, List<Stub> all, List<Stub> accepted)
        {
            GridPoint a = s.Tip, b = s.End;
            foreach (var o in all)
            {
                if (!ReferenceEquals(o, s) && !s.SameNet(o) && OnSegment(o.Tip, a, b)) return true;
            }

            foreach (var o in accepted)
            {
                if (s.SameNet(o)) continue;
                GridPoint c = o.Tip, d = o.End;
                if (OnSegment(a, c, d) || OnSegment(b, c, d) || OnSegment(c, a, b) || OnSegment(d, a, b)) return true;
            }

            return false;
        }

        private static bool OnSegment(GridPoint p, GridPoint a, GridPoint b)
        {
            const double Eps = 1e-6;
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double len2 = vx * vx + vy * vy;
            if (len2 < Eps * Eps) return Math.Abs(p.X - a.X) < Eps && Math.Abs(p.Y - a.Y) < Eps;
            double cross = vx * (p.Y - a.Y) - vy * (p.X - a.X);
            if (Math.Abs(cross) / Math.Sqrt(len2) > Eps) return false;
            double dot = (p.X - a.X) * vx + (p.Y - a.Y) * vy;
            return dot >= -Eps && dot <= len2 + Eps;
        }

        private static int QuarterTurn(int rotation)
        {
            int r = ((rotation % 360) + 360) % 360;
            return r % 90 == 0 ? r : 0;   // SchematicPart.Transform only knows quarter turns
        }

        // ================================================================== schematic

        /// <summary>
        /// Writes a KiCad 8 schematic (.kicad_sch).
        ///
        /// Connectivity approach: every pin of every part gets a short wire stub and a
        /// <c>global_label</c> carrying the name of its net from <see cref="Schematic.BuildNetlist"/>;
        /// the original wires, junctions and labels are not reproduced. Our netlist joins a
        /// point lying anywhere on a wire, merges a part's internally connected pins (the
        /// Arduino GND pins) and names nets through ground/power-rail symbols; KiCad's rules
        /// for T-connections without junctions and for pins on wire interiors differ, and
        /// generic box symbols cannot express internal connections. Naming every pin makes
        /// KiCad's ERC connectivity equal ours by construction: pins share a net exactly when
        /// their labels share a name. Global (not local) labels keep KiCad's net names identical
        /// to ours ("GND", not "/GND"), so the schematic, the .net file and the .kicad_pcb agree.
        /// Stubs are shortened or dropped where they would touch another net's pin or stub.
        ///
        /// Parts keep their positions and rotations (one grid unit = 2.54 mm, shifted onto the
        /// page by a whole number of grid units); ground and power-rail symbols are omitted
        /// because the labels already carry their net names.
        /// </summary>
        public static string ExportSchematic(Schematic schematic, PartLibrary library, string? projectName = null)
        {
            if (schematic == null) throw new ArgumentNullException(nameof(schematic));
            if (library == null) throw new ArgumentNullException(nameof(library));
            string project = ProjectName(projectName, schematic);
            string rootUuid = Uuid("schematic:" + project);
            var parts = ExportParts(schematic, library, out var variants);
            var dnp = Design.Variants.FittingOf(schematic, library)?.NotFitted ?? new HashSet<string>();   // [variants] the chosen alternative's parts not fitted
            var pinNets = PinNets(schematic.BuildNetlist(library));

            var stubs = new List<Stub>();
            foreach (var p in parts)
            {
                var origin = p.Part.Transform(0, 0);
                foreach (var (pin, _) in p.Symbol.Pins)
                {
                    var (dx, dy) = Outward(p.Def, pin);
                    var dir = p.Part.Transform(dx, dy);
                    pinNets.TryGetValue(PinKey(p.Part.RefDes, pin.Name), out var net);
                    stubs.Add(new Stub { Tip = p.Part.Transform(pin.X, pin.Y), Dx = dir.X - origin.X, Dy = dir.Y - origin.Y, Net = net?.Name });
                }
            }

            PlaceStubs(stubs);

            // Page placement: shift by whole grid units so everything stays on the 50 mil grid.
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            void Grow(double x, double y)
            {
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }

            foreach (var p in parts)
            {
                foreach (var c in BoxCorners(p)) Grow(c.X, c.Y);
            }

            foreach (var s in stubs)
            {
                double labelLength = ((s.Net?.Length ?? 0) * 1.0 + 3.0) / SchematicGridMm;
                Grow(s.Tip.X, s.Tip.Y);
                Grow(s.End.X + s.Dx * labelLength, s.End.Y + s.Dy * labelLength);
            }

            foreach (var sh in schematic.Shapes ?? new List<SchematicShape>())   // [shapes] the sheet's drawings fit on the page too
                foreach (var q in sh.Outline(16)) Grow(q.X, q.Y);

            if (double.IsInfinity(minX))
            {
                minX = minY = 0;
                maxX = maxY = 1;
            }

            const int Margin = 8;
            int ox = (int)Math.Ceiling(Margin - minX), oy = (int)Math.Ceiling(Margin - minY);
            double Mx(double gx) => (gx + ox) * SchematicGridMm;
            double My(double gy) => (gy + oy) * SchematicGridMm;

            var root = new SNode("kicad_sch",
                new SNode("version", SchematicFileVersion),
                new SNode("generator", Q(Generator)),
                new SNode("generator_version", Q(GeneratorVersion)),
                new SNode("uuid", Q(rootUuid)),
                new SNode("paper", Q(Paper((maxX + ox + Margin) * SchematicGridMm, (maxY + oy + Margin) * SchematicGridMm + 40))),
                new SNode("title_block", new SNode("title", Q(schematic.Title ?? string.Empty))),
                new SNode("lib_symbols", variants.Select(v => LibSymbol(v, DefaultLibrary + ":" + v.Name))));

            for (int i = 0; i < stubs.Count; i++)
            {
                var s = stubs[i];
                string id = i.ToString(CultureInfo.InvariantCulture);
                if (s.Net == null)
                {
                    root.Add(new SNode("no_connect", XY("at", Mx(s.Tip.X), My(s.Tip.Y)), Uid("schematic:nc:" + id)));
                    continue;
                }

                var end = s.End;
                if (s.Length > 0)
                {
                    root.Add(new SNode("wire",
                        new SNode("pts", XY("xy", Mx(s.Tip.X), My(s.Tip.Y)), XY("xy", Mx(end.X), My(end.Y))),
                        Stroke(0, "default"),
                        Uid("schematic:wire:" + id)));
                }

                // Label body points away from the pin: 0 = text to the right of the anchor.
                int angle = s.Dx > 0.5 ? 0 : s.Dx < -0.5 ? 180 : s.Dy < 0 ? 90 : 270;
                root.Add(new SNode("global_label", Q(s.Net),
                    new SNode("shape", "passive"),
                    new SNode("at", N(Mx(end.X)), N(My(end.Y)), I(angle)),
                    new SNode("effects", SchFont(), new SNode("justify", angle == 0 || angle == 90 ? "left" : "right")),
                    Uid("schematic:label:" + id)));
            }

            foreach (var p in parts)
            {
                var part = p.Part;
                int rotation = QuarterTurn(part.Rotation);
                var corners = BoxCorners(p);
                double cx = Mx((corners.Min(c => c.X) + corners.Max(c => c.X)) / 2);
                double cy = My((corners.Min(c => c.Y) + corners.Max(c => c.Y)) / 2);
                double top = My(corners.Min(c => c.Y)) - 1.27, bottom = My(corners.Max(c => c.Y)) + 1.27;

                // Field angles are relative to the symbol: KiCad swaps them for 90/270 symbols.
                double fieldAngle = rotation % 180 == 0 ? 0 : 90;
                bool onBoard = p.Footprint != null;
                var preset = part.PresetId != null ? library.FindPreset(part.PresetId) : null;
                var symbol = new SNode("symbol",
                    new SNode("lib_id", Q(DefaultLibrary + ":" + p.Symbol.Name)),
                    new SNode("at", N(Mx(part.Position.X)), N(My(part.Position.Y)), I(rotation)),
                    part.Mirror ? new SNode("mirror", "y") : null,
                    new SNode("unit", "1"),
                    new SNode("exclude_from_sim", "no"),
                    new SNode("in_bom", onBoard && !dnp.Contains(part.RefDes) ? "yes" : "no"),
                    new SNode("on_board", onBoard ? "yes" : "no"),
                    new SNode("dnp", dnp.Contains(part.RefDes) ? "yes" : "no"),
                    new SNode("uuid", Q(SymbolUuid(part.RefDes))),
                    SchProperty("Reference", part.RefDes, cx, top, fieldAngle, false),
                    SchProperty("Value", p.Value, cx, bottom, fieldAngle, false),
                    SchProperty("Footprint", onBoard ? DefaultLibrary + ":" + p.Footprint!.Id : string.Empty, cx, cy, 0, true),
                    SchProperty("Datasheet", string.Empty, cx, cy, 0, true),
                    SchProperty("Description", preset?.Description ?? p.Def.Name, cx, cy, 0, true));
                foreach (string number in p.Symbol.Pins.Select(x => x.Number).Distinct(StringComparer.Ordinal))
                {
                    symbol.Add(new SNode("pin", Q(number), Uid("schematic:pin:" + part.RefDes.ToUpperInvariant() + ":" + number)));
                }

                symbol.Add(new SNode("instances",
                    new SNode("project", Q(project),
                        new SNode("path", Q("/" + rootUuid), new SNode("reference", Q(part.RefDes)), new SNode("unit", "1")))));
                root.Add(symbol);
            }

            // [shapes] lines, rectangles, circles and arcs as KiCad's schematic graphics
            var shapes = schematic.Shapes ?? new List<SchematicShape>();
            for (int i = 0; i < shapes.Count; i++)
            {
                var sh = shapes[i];
                var stroke = new SNode("stroke", new SNode("width", N(sh.Width)), new SNode("type", sh.Dashed ? "dash" : "default"));
                var fill = new SNode("fill", new SNode("type", sh.Filled ? "outline" : "none"));
                var id = Uid("schematic:shape:" + i.ToString(CultureInfo.InvariantCulture));
                SNode At(string head, GridPoint g) => XY(head, Mx(g.X), My(g.Y));
                switch (sh.Kind)
                {
                    case SheetShapeKind.Line when sh.Points.Count >= 2:
                        root.Add(new SNode("polyline", new SNode("pts", sh.Points.Select(q => At("xy", q))), stroke, id));
                        break;
                    case SheetShapeKind.Rectangle when sh.Points.Count >= 2:
                        root.Add(new SNode("rectangle", At("start", sh.Points[0]), At("end", sh.Points[1]), stroke, fill, id));
                        break;
                    case SheetShapeKind.Circle when sh.Points.Count >= 2:
                        root.Add(new SNode("circle", At("center", sh.Points[0]), new SNode("radius", N(sh.Radius * SchematicGridMm)), stroke, fill, id));
                        break;
                    case SheetShapeKind.Arc when sh.Points.Count >= 3:
                        root.Add(new SNode("arc", At("start", sh.Points[0]), At("mid", sh.Points[1]), At("end", sh.Points[2]), stroke, new SNode("fill", new SNode("type", "none")), id));
                        break;
                }
            }

            root.Add(new SNode("sheet_instances", new SNode("path", Q("/"), new SNode("page", Q("1")))));
            return Render(root);
        }

        private static GridPoint[] BoxCorners(ExportPart p)
        {
            double hw = p.Def.HalfWidth, hh = p.Def.HalfHeight;
            return new[] { p.Part.Transform(-hw, -hh), p.Part.Transform(hw, -hh), p.Part.Transform(hw, hh), p.Part.Transform(-hw, hh) };
        }

        /// <summary>The generic box symbols of the schematic as a KiCad symbol library (RobotWin.kicad_sym).</summary>
        public static string ExportSymbolLibrary(Schematic schematic, PartLibrary library)
        {
            ExportParts(schematic, library, out var variants);
            var root = new SNode("kicad_symbol_lib",
                new SNode("version", SchematicFileVersion),
                new SNode("generator", Q(Generator)),
                new SNode("generator_version", Q(GeneratorVersion)));
            foreach (var v in variants) root.Add(LibSymbol(v, v.Name));
            return Render(root);
        }

        // ================================================================== netlist

        /// <summary>
        /// KiCad s-expression netlist (version "E"), as Eeschema's "Export Netlist" writes it.
        /// Node pins are footprint pad names (<see cref="PartDefinition.ResolvePad"/>), so it
        /// matches the PCB; component tstamps equal the schematic symbol UUIDs and the PCB
        /// footprint paths. Nets without any pin are left out, as KiCad does.
        /// </summary>
        public static string ExportNetlist(Schematic schematic, PartLibrary library, string? projectName = null)
        {
            if (schematic == null) throw new ArgumentNullException(nameof(schematic));
            if (library == null) throw new ArgumentNullException(nameof(library));
            string project = ProjectName(projectName, schematic);
            var netlist = schematic.BuildNetlist(library);
            var parts = ExportParts(schematic, library, out var variants);
            var byRef = new Dictionary<string, ExportPart>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in parts) byRef[p.Part.RefDes] = p;

            var components = new SNode("components");
            foreach (var p in parts)
            {
                var comp = new SNode("comp",
                    new SNode("ref", Q(p.Part.RefDes)),
                    new SNode("value", Q(p.Value)),
                    p.Footprint != null ? new SNode("footprint", Q(DefaultLibrary + ":" + p.Footprint.Id)) : null,
                    new SNode("libsource", new SNode("lib", Q(DefaultLibrary)), new SNode("part", Q(p.Symbol.Name)),
                        new SNode("description", Q(p.Def.Name))));
                if (p.Footprint == null)
                {
                    comp.Add(new SNode("property", new SNode("name", Q("exclude_from_bom"))),
                        new SNode("property", new SNode("name", Q("exclude_from_board"))));
                }

                comp.Add(new SNode("sheetpath", new SNode("names", Q("/")), new SNode("tstamps", Q("/"))),
                    new SNode("tstamps", Q(SymbolUuid(p.Part.RefDes))));
                components.Add(comp);
            }

            var libparts = new SNode("libparts");
            foreach (var v in variants)
            {
                var pins = new SNode("pins");
                foreach (var (pin, number) in v.Pins)
                {
                    pins.Add(new SNode("pin", new SNode("num", Q(number)), new SNode("name", Q(pin.Name)), new SNode("type", Q("passive"))));
                }

                libparts.Add(new SNode("libpart",
                    new SNode("lib", Q(DefaultLibrary)),
                    new SNode("part", Q(v.Name)),
                    new SNode("description", Q(v.Def.Name)),
                    new SNode("docs", Q(string.Empty)),
                    new SNode("fields",
                        new SNode("field", new SNode("name", Q("Reference")), Q(v.Def.RefPrefix)),
                        new SNode("field", new SNode("name", Q("Value")), Q(v.Name))),
                    pins));
            }

            var nets = new SNode("nets");
            int code = 1;
            foreach (var net in netlist.Nets)
            {
                var node = new SNode("net", new SNode("code", Q(code.ToString(CultureInfo.InvariantCulture))), new SNode("name", Q(net.Name)));
                foreach (var c in net.Connections)
                {
                    if (!byRef.TryGetValue(c.RefDes, out var p)) continue;
                    var pin = p.Def.FindPin(c.Pin);
                    if (pin == null) continue;
                    node.Add(new SNode("node", new SNode("ref", Q(p.Part.RefDes)), new SNode("pin", Q(PinNumber(p.Def, pin, p.Parameters))),
                        new SNode("pinfunction", Q(pin.Name)), new SNode("pintype", Q("passive"))));
                }

                if (!node.Items.Any(i => i is SNode n && n.Head == "node")) continue;
                nets.Add(node);
                code++;
            }

            var root = new SNode("export",
                new SNode("version", Q("E")),
                new SNode("design",
                    new SNode("source", Q(project + ".kicad_sch")),
                    new SNode("tool", Q(Generator + " " + GeneratorVersion)),
                    new SNode("sheet", new SNode("number", Q("1")), new SNode("name", Q("/")), new SNode("tstamps", Q("/")),
                        new SNode("title_block", new SNode("title", Q(schematic.Title ?? string.Empty)), new SNode("source", Q(project + ".kicad_sch"))))),
                components,
                libparts,
                new SNode("libraries", new SNode("library", new SNode("logical", Q(DefaultLibrary)),
                    new SNode("uri", Q("${KIPRJMOD}/" + DefaultLibrary + ".kicad_sym")))),
                nets);
            return Render(root);
        }

        // ================================================================== project

        /// <summary>
        /// Minimal KiCad 8 project file: design rules (board.design_settings), a Default net
        /// class from the rules and a Power class (power track width) assigned to
        /// <paramref name="powerNets"/> by exact-name patterns.
        /// </summary>
        public static string ExportProject(string name, DesignRules? rules = null, IEnumerable<string>? powerNets = null)
        {
            rules ??= new DesignRules();
            var classes = new List<NetClass>
            {
                new NetClass { Name = NetClass.DefaultName, Clearance = rules.Clearance, TrackWidth = rules.TrackWidth, ViaDiameter = rules.ViaDiameter, ViaDrill = rules.ViaDrill },
                new NetClass { Name = NetClass.PowerName, Clearance = rules.Clearance, TrackWidth = rules.PowerTrackWidth, ViaDiameter = rules.ViaDiameter, ViaDrill = rules.ViaDrill }
            };
            var patterns = (powerNets ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(n => (Net: n, Class: NetClass.PowerName)).ToList();
            return ExportProject(name, rules, classes, patterns);
        }

        /// <summary>Project file for a board: its rules, all its net classes and every net's class assignment.</summary>
        public static string ExportProject(string name, BoardDocument board)
        {
            var nets = board.NetNames().Concat(board.Tracks.Select(t => t.Net)).Concat(board.Zones.Select(z => z.Net))
                .Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.Ordinal);
            var patterns = nets.Select(n => (Net: n, Class: board.ClassFor(n).Name))
                .Where(p => !string.Equals(p.Class, NetClass.DefaultName, StringComparison.OrdinalIgnoreCase)).ToList();
            var classes = board.NetClasses.ToList();
            if (!classes.Any(c => string.Equals(c.Name, NetClass.DefaultName, StringComparison.OrdinalIgnoreCase)))
            {
                classes.Insert(0, board.DefaultClass);
            }
            else
            {
                var def = classes.First(c => string.Equals(c.Name, NetClass.DefaultName, StringComparison.OrdinalIgnoreCase));
                classes.Remove(def);
                classes.Insert(0, def);
            }

            return ExportProject(name, board.Rules, classes, patterns);
        }

        private static string ExportProject(string name, DesignRules rules, List<NetClass> classes, List<(string Net, string Class)> patterns)
        {
            string project = ProjectName(name, null);
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();

                w.WriteStartObject("board");
                w.WriteStartObject("design_settings");
                w.WriteStartObject("defaults");
                w.WriteNumber("board_outline_line_width", EdgeWidth);
                w.WriteNumber("copper_line_width", rules.TrackWidth);
                w.WriteNumber("courtyard_line_width", CourtyardWidth);
                w.WriteNumber("fab_line_width", FabWidth);
                w.WriteNumber("silk_line_width", SilkWidth);
                w.WriteEndObject();
                w.WriteStartArray("diff_pair_dimensions");
                w.WriteEndArray();
                w.WriteStartArray("drc_exclusions");
                w.WriteEndArray();
                w.WriteStartObject("meta");
                w.WriteNumber("version", 2);
                w.WriteEndObject();
                w.WriteStartObject("rules");
                w.WriteNumber("min_clearance", rules.Clearance);
                w.WriteNumber("min_copper_edge_clearance", rules.EdgeClearance);
                w.WriteNumber("min_hole_clearance", rules.HoleClearance);
                w.WriteNumber("min_hole_to_hole", rules.MinHoleToHole);
                w.WriteNumber("min_through_hole_diameter", rules.MinDrill);
                w.WriteNumber("min_track_width", rules.MinTrackWidth);
                w.WriteNumber("min_via_annular_width", rules.MinAnnularRing);
                w.WriteNumber("min_via_diameter", MinViaDiameter(rules));
                w.WriteNumber("solder_mask_to_copper_clearance", 0.0);
                w.WriteEndObject();
                w.WriteStartArray("track_widths");
                w.WriteNumberValue(0.0);
                foreach (double tw in classes.Select(c => c.TrackWidth).Distinct().OrderBy(x => x)) w.WriteNumberValue(tw);
                w.WriteEndArray();
                w.WriteStartArray("via_dimensions");
                w.WriteStartObject();
                w.WriteNumber("diameter", 0.0);
                w.WriteNumber("drill", 0.0);
                w.WriteEndObject();
                foreach (var vd in classes.Select(c => (c.ViaDiameter, c.ViaDrill)).Distinct().OrderBy(x => x.ViaDiameter))
                {
                    w.WriteStartObject();
                    w.WriteNumber("diameter", vd.ViaDiameter);
                    w.WriteNumber("drill", vd.ViaDrill);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
                w.WriteBoolean("zones_allow_external_fillets", false);
                w.WriteEndObject();
                w.WriteStartArray("layer_presets");
                w.WriteEndArray();
                w.WriteStartArray("viewports");
                w.WriteEndArray();
                w.WriteEndObject();

                w.WriteStartArray("boards");
                w.WriteEndArray();

                w.WriteStartObject("libraries");
                w.WriteStartArray("pinned_footprint_libs");
                w.WriteEndArray();
                w.WriteStartArray("pinned_symbol_libs");
                w.WriteEndArray();
                w.WriteEndObject();

                w.WriteStartObject("meta");
                w.WriteString("filename", project + ".kicad_pro");
                w.WriteNumber("version", 1);
                w.WriteEndObject();

                w.WriteStartObject("net_settings");
                w.WriteStartArray("classes");
                foreach (var c in classes) WriteNetClass(w, c);
                w.WriteEndArray();
                w.WriteStartObject("meta");
                w.WriteNumber("version", 3);
                w.WriteEndObject();
                w.WriteNull("net_colors");
                w.WriteNull("netclass_assignments");
                w.WriteStartArray("netclass_patterns");
                foreach (var (net, cls) in patterns)
                {
                    w.WriteStartObject();
                    w.WriteString("netclass", cls);
                    w.WriteString("pattern", net);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
                w.WriteEndObject();

                w.WriteStartArray("sheets");
                w.WriteEndArray();
                w.WriteStartObject("text_variables");
                w.WriteEndObject();

                w.WriteEndObject();
            }

            return new UTF8Encoding(false).GetString(stream.ToArray());
        }

        private static void WriteNetClass(Utf8JsonWriter w, NetClass c)
        {
            w.WriteStartObject();
            w.WriteNumber("bus_width", 12);
            w.WriteNumber("clearance", c.Clearance);
            w.WriteNumber("diff_pair_gap", 0.25);
            w.WriteNumber("diff_pair_via_gap", 0.25);
            w.WriteNumber("diff_pair_width", 0.2);
            w.WriteNumber("line_style", 0);
            w.WriteNumber("microvia_diameter", 0.3);
            w.WriteNumber("microvia_drill", 0.1);
            w.WriteString("name", c.Name);
            w.WriteString("pcb_color", "rgba(0, 0, 0, 0.000)");
            w.WriteString("schematic_color", "rgba(0, 0, 0, 0.000)");
            w.WriteNumber("track_width", c.TrackWidth);
            w.WriteNumber("via_diameter", c.ViaDiameter);
            w.WriteNumber("via_drill", c.ViaDrill);
            w.WriteNumber("wire_width", 6);
            w.WriteEndObject();
        }

        // ================================================================== SPICE and bundle

        /// <summary>SPICE3 netlist of the schematic (see <see cref="SpiceExporter"/>).</summary>
        public static string ExportSpice(Schematic schematic, PartLibrary library) => SpiceExporter.ExportSpice(schematic, library);

        /// <summary>
        /// Every project file keyed by its path inside the zip ("&lt;name&gt;/&lt;name&gt;.kicad_pcb", ...).
        /// The board is written without embedded legacy rules: the .kicad_pro carries them.
        /// </summary>
        public static Dictionary<string, string> GenerateProject(Schematic schematic, PartLibrary library, BoardDocument board, string projectName)
        {
            if (schematic == null) throw new ArgumentNullException(nameof(schematic));
            if (library == null) throw new ArgumentNullException(nameof(library));
            if (board == null) throw new ArgumentNullException(nameof(board));
            string name = ProjectName(projectName, schematic);
            string dir = name + "/";
            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [dir + name + ".kicad_pro"] = ExportProject(name, board),
                [dir + name + ".kicad_sch"] = ExportSchematic(schematic, library, name),
                [dir + name + ".kicad_pcb"] = ExportPcb(board, schematic.Title, embedDesignRules: false),
                [dir + name + ".net"] = ExportNetlist(schematic, library, name),
                [dir + name + ".cir"] = SpiceExporter.ExportSpice(schematic, library),
                [dir + "fp-lib-table"] = FootprintLibTable(),
                [dir + "sym-lib-table"] = SymbolLibTable(),
                [dir + DefaultLibrary + ".kicad_sym"] = ExportSymbolLibrary(schematic, library)
            };

            var footprints = new List<Footprint>();
            void Use(Footprint? fp)
            {
                if (fp != null && !footprints.Contains(fp)) footprints.Add(fp);
            }

            foreach (var comp in board.Components) Use(FootprintLibrary.Get(comp.FootprintId));
            foreach (var p in ExportParts(schematic, library, out _)) Use(p.Footprint);
            foreach (var fp in footprints) files[dir + DefaultLibrary + ".pretty/" + fp.Id + ".kicad_mod"] = ExportFootprint(fp);

            // Library copies of the mounting-hole footprints the board references.
            foreach (var h in board.MountingHoles)
            {
                string id = "MountingHole_" + h.Drill.ToString("0.0##", CultureInfo.InvariantCulture) + "mm" + (h.Plated ? "_Pad" : string.Empty);
                var hole = new Footprint { Id = id, Description = Localization.T("Montaj deliği Ø" + h.Drill.ToString("0.0##", CultureInfo.InvariantCulture) + " mm"), Height = 0 };
                hole.Pads.Add(h.Plated
                    ? new Pad { Name = "1", Width = h.PadDiameter, Height = h.PadDiameter, Drill = h.Drill, Shape = PadShape.Circle }
                    : new Pad { Name = string.Empty, Width = h.Drill, Height = h.Drill, Drill = h.Drill, Shape = PadShape.Circle, NonPlated = true });
                double r = Math.Max(h.Drill, h.PadDiameter) / 2;
                hole.CourtyardMinX = hole.CourtyardMinY = -r - 0.25;
                hole.CourtyardMaxX = hole.CourtyardMaxY = r + 0.25;
                hole.SilkCircles.Add((0, 0, r + 0.1));
                files[dir + DefaultLibrary + ".pretty/" + id + ".kicad_mod"] = ExportFootprint(hole);
            }

            foreach (var kv in GerberExporter.Generate(board, name)) files[dir + "gerber/" + kv.Key] = kv.Value;
            return files;
        }

        /// <summary>Writes the KiCad project (see <see cref="GenerateProject"/>) as a zip.</summary>
        public static void WriteProjectZip(string path, Schematic schematic, PartLibrary library, BoardDocument board, string projectName)
        {
            var files = GenerateProject(schematic, library, board, projectName);
            if (File.Exists(path)) File.Delete(path);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var kv in files)
            {
                var entry = zip.CreateEntry(kv.Key, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(kv.Value);
            }
        }

        private static string ProjectName(string? name, Schematic? schematic)
        {
            string text = string.IsNullOrWhiteSpace(name) ? schematic?.Title ?? string.Empty : name!;
            var sb = new StringBuilder();
            foreach (char c in text.Trim()) sb.Append(c < 32 || "<>:\"/\\|?*".IndexOf(c) >= 0 ? '_' : c);
            string result = sb.ToString().Trim().TrimEnd('.');
            return result.Length == 0 ? "robotwin" : result;
        }

        // ================================================================== s-expressions

        /// <summary>An s-expression list: head token followed by pre-formatted atoms and child lists.</summary>
        private sealed class SNode
        {
            public SNode(string head, params object?[] items)
            {
                Head = head;
                Add(items);
            }

            public string Head { get; }
            public List<object> Items { get; } = new List<object>();

            public SNode Add(params object?[] items)
            {
                foreach (var item in items)
                {
                    switch (item)
                    {
                        case null:
                            break;
                        case string atom:
                            Items.Add(atom);
                            break;
                        case SNode node:
                            Items.Add(node);
                            break;
                        case IEnumerable<SNode> nodes:
                            foreach (var n in nodes) Items.Add(n);
                            break;
                        default:
                            throw new ArgumentException("Unsupported s-expression item " + item.GetType().Name);
                    }
                }

                return this;
            }
        }

        private static string Render(SNode root)
        {
            var sb = new StringBuilder();
            Write(sb, root, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, SNode node, int depth)
        {
            sb.Append('\t', depth).Append('(').Append(node.Head);
            int i = 0;
            while (i < node.Items.Count && node.Items[i] is string atom)
            {
                sb.Append(' ').Append(atom);
                i++;
            }

            if (i == node.Items.Count)
            {
                sb.Append(")\n");
                return;
            }

            sb.Append('\n');
            for (; i < node.Items.Count; i++)
            {
                if (node.Items[i] is SNode child) Write(sb, child, depth + 1);
                else sb.Append('\t', depth + 1).Append((string)node.Items[i]).Append('\n');
            }

            sb.Append('\t', depth).Append(")\n");
        }

        /// <summary>Quoted string with KiCad's escapes (OUTPUTFORMATTER::Quotes).</summary>
        private static string Q(string? text)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in text ?? string.Empty)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.Append('"').ToString();
        }

        /// <summary>Millimetres with at most 6 decimals (KiCad's nanometre resolution), invariant, no exponent.</summary>
        private static string N(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0.0;
            double r = Math.Round(v, 6);
            if (r == 0.0) r = 0.0;   // no "-0"
            return r.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

        private static SNode XY(string head, double x, double y) => new SNode(head, N(x), N(y));

        private static SNode At(double x, double y, double angle) =>
            angle == 0.0 ? new SNode("at", N(x), N(y)) : new SNode("at", N(x), N(y), N(angle));

        private static SNode Uid(string seed) => new SNode("uuid", Q(Uuid(seed)));

        /// <summary>
        /// Deterministic RFC 4122-shaped UUID from a seed (two independent 64-bit hashes), so
        /// repeated exports are identical and the schematic, netlist and board agree on symbol
        /// ids without sharing state.
        /// </summary>
        internal static string Uuid(string seed)
        {
            ulong h1 = 14695981039346656037UL, h2 = 0x6A09E667F3BCC909UL;
            foreach (byte b in Encoding.UTF8.GetBytes(seed))
            {
                h1 = (h1 ^ b) * 1099511628211UL;
                h2 = (h2 ^ b) * 0x9E3779B97F4A7C15UL;
                h2 ^= h2 >> 29;
            }

            h1 = Mix(h1);
            h2 = Mix(h2 ^ h1);
            var bytes = new byte[16];
            for (int i = 0; i < 8; i++)
            {
                bytes[i] = (byte)(h1 >> (8 * i));
                bytes[8 + i] = (byte)(h2 >> (8 * i));
            }

            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            var sb = new StringBuilder(36);
            for (int i = 0; i < 16; i++)
            {
                if (i == 4 || i == 6 || i == 8 || i == 10) sb.Append('-');
                sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
