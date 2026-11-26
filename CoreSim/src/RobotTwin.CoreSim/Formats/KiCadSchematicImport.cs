using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;

namespace RobotTwin.CoreSim.Formats
{
    /// <summary>A symbol placed on a KiCad schematic sheet.</summary>
    public sealed class KiCadSchSymbol
    {
        /// <summary>The key of its definition in the sheet's <c>lib_symbols</c> (<c>lib_name</c> when given, else <c>lib_id</c>).</summary>
        public string LibKey { get; set; } = string.Empty;
        public string LibId { get; set; } = string.Empty;
        public string Uuid { get; set; } = string.Empty;

        /// <summary>Position, mm, sheet frame (Y down).</summary>
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Rotation, degrees counter-clockwise on the sheet (0, 90, 180, 270).</summary>
        public double Angle { get; set; }

        /// <summary><c>(mirror x)</c>: flipped top to bottom.</summary>
        public bool MirrorX { get; set; }

        /// <summary><c>(mirror y)</c>: flipped left to right.</summary>
        public bool MirrorY { get; set; }

        public int Unit { get; set; } = 1;
        public int BodyStyle { get; set; } = 1;
        public bool InBom { get; set; } = true;
        public bool OnBoard { get; set; } = true;
        public bool DoNotPopulate { get; set; }
        public Dictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Reference by sheet instance path ("/root-uuid/sheet-uuid"), from <c>(instances …)</c>.</summary>
        public Dictionary<string, string> References { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Reference => Properties.TryGetValue("Reference", out var r) ? r : string.Empty;
        public string Value => Properties.TryGetValue("Value", out var v) ? v : string.Empty;
        public string Footprint => Properties.TryGetValue("Footprint", out var f) ? f : string.Empty;

        /// <summary>The reference this symbol has on the sheet instance at <paramref name="path"/>.</summary>
        public string ReferenceAt(string path) => References.TryGetValue(path, out var r) && r.Length > 0 ? r : Reference;
    }

    public enum KiCadLabelKind
    {
        Local,
        Global,
        Hierarchical
    }

    public sealed class KiCadSchLabel
    {
        public string Name { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public KiCadLabelKind Kind { get; set; }
    }

    /// <summary>A hierarchical sheet block: the file it shows and its pins (each joins the child's hierarchical label of that name).</summary>
    public sealed class KiCadSchSheetRef
    {
        public string Name { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string Uuid { get; set; } = string.Empty;
        public List<KiCadSchLabel> Pins { get; } = new List<KiCadSchLabel>();
    }

    /// <summary>One <c>.kicad_sch</c> file as written: its symbols with their definitions, wires, junctions, labels, sheets.</summary>
    /// <summary>[shapes] A graphic of a KiCad sheet (polyline, rectangle, circle, arc), in the file's millimetres.</summary>
    public sealed class KiCadSheetShape
    {
        public SheetShapeKind Kind { get; set; }

        /// <summary>As <see cref="SchematicShape.Points"/>: a circle's centre then a point on it; an arc's start, middle and end.</summary>
        public List<(double X, double Y)> Points { get; } = new List<(double, double)>();

        public double Width { get; set; }
        public bool Dashed { get; set; }
        public bool Filled { get; set; }
    }

    public sealed class KiCadSchFile
    {
        /// <summary>[shapes] The sheet's graphics.</summary>
        public List<KiCadSheetShape> Shapes { get; } = new List<KiCadSheetShape>();

        public string Uuid { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public Dictionary<string, KiCadSymbol> LibSymbols { get; } = new Dictionary<string, KiCadSymbol>(StringComparer.Ordinal);
        public List<KiCadSchSymbol> Symbols { get; } = new List<KiCadSchSymbol>();
        public List<(double X1, double Y1, double X2, double Y2)> Wires { get; } = new List<(double, double, double, double)>();
        public List<(double X, double Y)> Junctions { get; } = new List<(double, double)>();
        public List<KiCadSchLabel> Labels { get; } = new List<KiCadSchLabel>();
        public List<(double X, double Y)> NoConnects { get; } = new List<(double, double)>();
        public List<KiCadSchSheetRef> Sheets { get; } = new List<KiCadSchSheetRef>();
        /// <summary>Bus segments, mm.</summary>
        public List<(double X1, double Y1, double X2, double Y2)> BusWires { get; } = new List<(double, double, double, double)>();

        /// <summary><c>(bus_alias "USB" (members "D_P" "D_N"))</c>: an alias's members, by its name.</summary>
        public Dictionary<string, List<string>> BusAliases { get; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public int Buses => BusWires.Count;
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>KiCad 6's <c>symbol_instances</c> (root file only): "/sheet-uuid/…/symbol-uuid" → reference.</summary>
        public Dictionary<string, string> LegacyReferences { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>A pin on a net: the symbol's reference and the pin's number (and name).</summary>
    public sealed class KiCadNetNode
    {
        public KiCadNetNode(string reference, string pin, string pinName)
        {
            Reference = reference;
            Pin = pin;
            PinName = pinName;
        }

        public string Reference { get; }
        public string Pin { get; }
        public string PinName { get; }
        public override string ToString() => Reference + "." + Pin;
    }

    public sealed class KiCadNet
    {
        /// <summary>As KiCad names it: a global label or power symbol's text, "/sheet/label" for local labels, "Net-(R1-Pad2)" when nothing names it.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>A label, power symbol or hidden power pin names it.</summary>
        public bool Named { get; set; }

        /// <summary>A power symbol or a hidden power pin names it.</summary>
        public bool Power { get; set; }

        public List<KiCadNetNode> Nodes { get; } = new List<KiCadNetNode>();

        /// <summary>A no-connect flag sits on it.</summary>
        public bool NoConnect { get; set; }
    }

    /// <summary>A symbol (all its units) of the design, as found on its sheet instance.</summary>
    public sealed class KiCadDesignSymbol
    {
        public string Reference { get; set; } = string.Empty;
        public string SheetPath { get; set; } = string.Empty;
        public KiCadSchSymbol Placement { get; set; } = new KiCadSchSymbol();
        public KiCadSymbol? Definition { get; set; }
    }

    /// <summary>The design's connectivity: every pin's net, as KiCad's own netlist has it.</summary>
    public sealed class KiCadDesign
    {
        public KiCadSchFile Root { get; set; } = new KiCadSchFile();
        public List<KiCadNet> Nets { get; } = new List<KiCadNet>();
        public List<KiCadDesignSymbol> Symbols { get; } = new List<KiCadDesignSymbol>();
        public List<string> Warnings { get; } = new List<string>();
        public int Sheets { get; set; }

        /// <summary>[shapes] Every sheet's graphics with the path of the sheet they are on.</summary>
        public List<(string SheetPath, KiCadSheetShape Shape)> SheetShapes { get; } = new List<(string, KiCadSheetShape)>();

        /// <summary>Bus segments over every sheet instance.</summary>
        public int Buses { get; set; }

        /// <summary>The net of a pin (reference and number), or null.</summary>
        public KiCadNet? NetOf(string reference, string pin) =>
            Nets.FirstOrDefault(n => n.Nodes.Any(x => string.Equals(x.Reference, reference, StringComparison.OrdinalIgnoreCase) && x.Pin == pin));
    }

    /// <summary>What an imported schematic became.</summary>
    public sealed class KiCadSchematicImportResult
    {
        public Schematic Schematic { get; set; } = new Schematic();
        public KiCadDesign Design { get; set; } = new KiCadDesign();

        /// <summary>One line per symbol: what it became (its model, a generic part, or why it was left out).</summary>
        public List<string> Report { get; } = new List<string>();
        public int Mapped { get; set; }
        public int Generic { get; set; }
        public int Skipped { get; set; }

        /// <summary>References marked "do not populate" in KiCad: drawn, not fitted, so not simulated.</summary>
        public List<string> NotFitted { get; } = new List<string>();

        /// <summary>References left out (no model, no generic part fits); their nets keep the rest of the circuit.</summary>
        public List<string> Missing { get; } = new List<string>();
    }

    /// <summary>
    /// [kicad-sch] Reads KiCad 6–10 schematics (<c>.kicad_sch</c>), hierarchy included, and works out their connectivity the
    /// way KiCad does: wire ends and anything placed on a wire join it, crossings without a junction do not; labels of a
    /// name join within their sheet, global labels and power symbols across the design, hidden power-input pins by their
    /// name; a sheet pin joins the child's hierarchical label of its name, on each instance of the sheet. The symbols are
    /// mapped onto the simulation models by <see cref="KiCadLibraryImport"/> (the same rules as importing a symbol
    /// library), placed where KiCad has them, and joined by net labels on their pins, ground and supply symbols on the
    /// power nets: the netlist is KiCad's, net for net.
    /// </summary>
    public static class KiCadSchematicImport
    {
        /// <summary>
        /// Our part's turn and mirror that point its pins the way KiCad's point on the sheet (our symbols are not drawn
        /// in KiCad's orientation: our resistor lies, KiCad's stands): the best of the eight, by how well each mapped pin's
        /// direction from the centre agrees.
        /// </summary>
        private static (int Rotation, bool Mirror) Orientation(KiCadDesignSymbol symbol, CustomPart custom, PartDefinition def)
        {
            var p = symbol.Placement;
            var pairs = new List<((double X, double Y) Ours, (double X, double Y) Theirs)>();
            foreach (var (ours, number) in PinMap(custom))
            {
                var mine = def.Pins.FirstOrDefault(x => string.Equals(x.Name, ours, StringComparison.OrdinalIgnoreCase));
                var theirs = symbol.Definition?.Pins.FirstOrDefault(x => x.Number == number && (x.Unit == 0 || x.Unit == p.Unit) && x.BodyStyle <= 1);
                if (mine == null || theirs == null) continue;
                var (tx, ty) = PinPosition(p, theirs);
                pairs.Add(((mine.X, mine.Y), (tx - p.X, ty - p.Y)));
            }

            // KiCad mirrors on the sheet after turning, ours in the symbol before: the fallback when no pin tells.
            int angle = (((int)Math.Round(p.Angle)) % 360 + 360) % 360;
            var best = (Rotation: p.MirrorY ? (360 - angle) % 360 : p.MirrorX ? (540 - angle) % 360 : angle, Mirror: p.MirrorX || p.MirrorY);
            double bestScore = double.NegativeInfinity;
            foreach (bool mirror in new[] { best.Mirror, !best.Mirror })
            foreach (int rotation in new[] { best.Rotation, (best.Rotation + 90) % 360, (best.Rotation + 180) % 360, (best.Rotation + 270) % 360 })
            {
                var probe = new SchematicPart { Rotation = rotation, Mirror = mirror };
                double score = 0;
                foreach (var (ours, theirs) in pairs)
                {
                    var q = probe.Transform(ours.X, ours.Y);
                    double a = Math.Sqrt(q.X * q.X + q.Y * q.Y), b = Math.Sqrt(theirs.X * theirs.X + theirs.Y * theirs.Y);
                    if (a > 1e-9 && b > 1e-9) score += (q.X * theirs.X + q.Y * theirs.Y) / (a * b);
                }

                if (score > bestScore + 1e-9)
                {
                    bestScore = score;
                    best = (rotation, mirror);
                }
            }

            return best;
        }

        /// <summary>The wire from a pin out to its label or power symbol, grid units.</summary>
        private const double StubLength = 1;

        /// <summary>A grid unit of this schematic (0.1 in, KiCad's pin pitch).</summary>
        public const double MmPerGridUnit = 2.54;

        // ====================================================================== reading a sheet file

        public static KiCadSchFile ReadSheet(string text)
        {
            var root = SExpression.Parse(text ?? string.Empty);
            if (root.Head != "kicad_sch") throw new FormatException($"KiCad şeması değil: dosya '({root.Head} ...)' ile başlıyor, '(kicad_sch ...)' bekleniyor.");
            var file = new KiCadSchFile
            {
                Uuid = root.ChildText("uuid") ?? string.Empty,
                Version = root.ChildText("version") ?? string.Empty,
                Title = root.Child("title_block")?.ChildText("title") ?? string.Empty
            };

            if (root.Child("lib_symbols") != null)
            {
                foreach (var sym in KiCadLibraryImport.ImportSymbolLibrary(text!))
                    file.LibSymbols[sym.FullName] = sym;
            }

            // KiCad 6 kept every sheet's references in the root file, by "/sheet-uuid/…/symbol-uuid".
            foreach (var path in root.Child("symbol_instances")?.Children("path") ?? Enumerable.Empty<SExpression>())
            {
                string? key = path.Text(1);
                if (key != null) file.LegacyReferences[key] = path.ChildText("reference") ?? string.Empty;
            }

            foreach (var node in root.Children("symbol"))
            {
                var at = node.Child("at")?.Numbers() ?? new List<double>();
                var s = new KiCadSchSymbol
                {
                    LibId = node.ChildText("lib_id") ?? string.Empty,
                    Uuid = node.ChildText("uuid") ?? string.Empty,
                    X = at.Count > 0 ? at[0] : 0,
                    Y = at.Count > 1 ? at[1] : 0,
                    Angle = at.Count > 2 ? at[2] : 0,
                    Unit = (int)(node.Child("unit")?.Number(1, 1) ?? 1),
                    BodyStyle = (int)(node.Child("body_style")?.Number(1, 1) ?? node.Child("convert")?.Number(1, 1) ?? 1),
                    InBom = !string.Equals(node.ChildText("in_bom"), "no", StringComparison.OrdinalIgnoreCase),
                    OnBoard = !string.Equals(node.ChildText("on_board"), "no", StringComparison.OrdinalIgnoreCase),
                    DoNotPopulate = string.Equals(node.ChildText("dnp"), "yes", StringComparison.OrdinalIgnoreCase)
                };
                s.LibKey = node.ChildText("lib_name") is { Length: > 0 } libName ? libName : s.LibId;
                string mirror = node.ChildText("mirror") ?? string.Empty;
                s.MirrorX = mirror == "x";
                s.MirrorY = mirror == "y";
                foreach (var p in node.Children("property"))
                {
                    string? key = p.Text(1);
                    if (key != null) s.Properties[key] = p.Text(2) ?? string.Empty;
                }

                foreach (var project in node.Child("instances")?.Children("project") ?? Enumerable.Empty<SExpression>())
                foreach (var path in project.Children("path"))
                {
                    string? key = path.Text(1);
                    if (key != null) s.References[key] = path.ChildText("reference") ?? string.Empty;
                }

                file.Symbols.Add(s);
            }

            foreach (var w in root.Children("wire"))
            {
                var pts = w.Child("pts")?.Children("xy").Select(xy => (X: xy.Number(1), Y: xy.Number(2))).ToList();
                if (pts == null) continue;
                for (int i = 0; i + 1 < pts.Count; i++) file.Wires.Add((pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y));
            }

            foreach (var b in root.Children("bus"))
            {
                var pts = b.Child("pts")?.Children("xy").Select(xy => (X: xy.Number(1), Y: xy.Number(2))).ToList();
                if (pts == null) continue;
                for (int i = 0; i + 1 < pts.Count; i++) file.BusWires.Add((pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y));
            }

            foreach (var alias in root.Children("bus_alias"))
            {
                string? name = alias.Text(1);
                if (name == null) continue;
                file.BusAliases[name] = alias.Child("members")?.Atoms().ToList() ?? new List<string>();
            }
            foreach (var j in root.Children("junction")) file.Junctions.Add(At(j));
            foreach (var n in root.Children("no_connect")) file.NoConnects.Add(At(n));
            ReadShapes(root, file);   // [shapes]
            foreach (var (head, kind) in new[] { ("label", KiCadLabelKind.Local), ("global_label", KiCadLabelKind.Global), ("hierarchical_label", KiCadLabelKind.Hierarchical) })
            {
                foreach (var l in root.Children(head))
                {
                    var (x, y) = At(l);
                    file.Labels.Add(new KiCadSchLabel { Name = l.Text(1) ?? string.Empty, X = x, Y = y, Kind = kind });
                }
            }

            foreach (var sh in root.Children("sheet"))
            {
                string Prop(params string[] keys)
                {
                    foreach (var p in sh.Children("property"))
                        if (keys.Contains(p.Text(1) ?? string.Empty, StringComparer.OrdinalIgnoreCase)) return p.Text(2) ?? string.Empty;
                    return string.Empty;
                }

                var sheet = new KiCadSchSheetRef
                {
                    Name = Prop("Sheetname", "Sheet name"),
                    File = Prop("Sheetfile", "Sheet file"),
                    Uuid = sh.ChildText("uuid") ?? string.Empty
                };
                foreach (var pin in sh.Children("pin"))
                {
                    var (x, y) = At(pin);
                    sheet.Pins.Add(new KiCadSchLabel { Name = pin.Text(1) ?? string.Empty, X = x, Y = y, Kind = KiCadLabelKind.Hierarchical });
                }

                file.Sheets.Add(sheet);
            }

            return file;
        }

        /// <summary>[shapes] The sheet's polylines, rectangles, circles and arcs (KiCad 6–9 schematic graphics).</summary>
        private static void ReadShapes(SExpression root, KiCadSchFile file)
        {
            void Style(SExpression node, KiCadSheetShape shape)
            {
                var stroke = node.Child("stroke");
                shape.Width = stroke?.Child("width")?.Number(1) ?? 0;
                shape.Dashed = (stroke?.ChildText("type") ?? "default") is "dash" or "dot" or "dash_dot" or "dash_dot_dot";
                shape.Filled = (node.Child("fill")?.ChildText("type") ?? "none") is "outline" or "background" or "color";
            }

            (double X, double Y) P(SExpression? n) => n == null ? (0, 0) : (n.Number(1), n.Number(2));
            foreach (var n in root.Children("polyline"))
            {
                var shape = new KiCadSheetShape { Kind = SheetShapeKind.Line };
                foreach (var xy in n.Child("pts")?.Children("xy") ?? Enumerable.Empty<SExpression>()) shape.Points.Add(P(xy));
                if (shape.Points.Count < 2) continue;
                Style(n, shape);
                file.Shapes.Add(shape);
            }

            foreach (var n in root.Children("rectangle"))
            {
                var shape = new KiCadSheetShape { Kind = SheetShapeKind.Rectangle };
                shape.Points.Add(P(n.Child("start")));
                shape.Points.Add(P(n.Child("end")));
                Style(n, shape);
                file.Shapes.Add(shape);
            }

            foreach (var n in root.Children("circle"))
            {
                var c = P(n.Child("center"));
                double r = n.Child("radius")?.Number(1) ?? 0;
                if (r <= 0) continue;
                var shape = new KiCadSheetShape { Kind = SheetShapeKind.Circle };
                shape.Points.Add(c);
                shape.Points.Add((c.X + r, c.Y));
                Style(n, shape);
                file.Shapes.Add(shape);
            }

            foreach (var n in root.Children("arc"))
            {
                var shape = new KiCadSheetShape { Kind = SheetShapeKind.Arc };
                shape.Points.Add(P(n.Child("start")));
                shape.Points.Add(P(n.Child("mid")));
                shape.Points.Add(P(n.Child("end")));
                Style(n, shape);
                shape.Filled = false;
                file.Shapes.Add(shape);
            }
        }

        private static (double X, double Y) At(SExpression node)
        {
            var at = node.Child("at");
            return (at?.Number(1) ?? 0, at?.Number(2) ?? 0);
        }

        /// <summary>
        /// Where a library pin's connection point lands on the sheet, mm: the library's Y-up frame flipped onto the sheet,
        /// turned counter-clockwise by the symbol's angle, then mirrored on the sheet (KiCad's order; checked against its
        /// own netlists).
        /// </summary>
        public static (double X, double Y) PinPosition(KiCadSchSymbol s, KiCadPin pin)
        {
            double x = pin.X, y = -pin.Y;
            int a = (((int)Math.Round(s.Angle)) % 360 + 360) % 360;
            double rx, ry;
            switch (a)
            {
                case 90: rx = y; ry = -x; break;
                case 180: rx = -x; ry = -y; break;
                case 270: rx = -y; ry = x; break;
                default: rx = x; ry = y; break;
            }

            if (s.MirrorY) rx = -rx;
            if (s.MirrorX) ry = -ry;
            return (s.X + rx, s.Y + ry);
        }

        // ====================================================================== connectivity

        private sealed class UnionFind
        {
            private readonly List<int> _parent = new List<int>();

            public int Add()
            {
                _parent.Add(_parent.Count);
                return _parent.Count - 1;
            }

            public int Find(int i)
            {
                while (_parent[i] != i)
                {
                    _parent[i] = _parent[_parent[i]];
                    i = _parent[i];
                }

                return i;
            }

            public void Union(int a, int b)
            {
                a = Find(a);
                b = Find(b);
                if (a != b) _parent[Math.Max(a, b)] = Math.Min(a, b);
            }
        }

        private static (long, long) Key(double x, double y) => ((long)Math.Round(x * 10000), (long)Math.Round(y * 10000));

        private static bool OnSegment((double X, double Y) p, (double X1, double Y1, double X2, double Y2) w)
        {
            double dx = w.X2 - w.X1, dy = w.Y2 - w.Y1;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-12) return false;
            double t = ((p.X - w.X1) * dx + (p.Y - w.Y1) * dy) / len2;
            if (t <= 1e-9 || t >= 1 - 1e-9) return false;
            double cx = w.X1 + t * dx - p.X, cy = w.Y1 + t * dy - p.Y;
            return cx * cx + cy * cy < 1e-6;
        }

        /// <summary>
        /// Reads a design from its root sheet; <paramref name="readFile"/> gives the text of a sub-sheet file named as the
        /// sheet block names it (relative to the root's folder), or null when it cannot be found.
        /// </summary>
        public static KiCadDesign Read(string rootText, Func<string, string?>? readFile = null, string? projectJson = null)
        {
            var root = ReadSheet(rootText);
            var design = new KiCadDesign { Root = root };
            var uf = new UnionFind();
            var points = new Dictionary<(string Path, (long, long) P), int>();
            var names = new Dictionary<string, int>(StringComparer.Ordinal);   // name key → item
            var named = new List<(int Item, string Name, int Priority, bool Power, int Depth)>();
            int depthNow = 0;
            var pinItems = new List<(int Item, KiCadNetNode Node)>();
            var noConnects = new List<int>();
            var cache = new Dictionary<string, KiCadSchFile?>(StringComparer.OrdinalIgnoreCase);
            string rootPath = "/" + root.Uuid;

            KiCadSchFile? Load(string file)
            {
                if (cache.TryGetValue(file, out var loaded)) return loaded;
                string? text = readFile?.Invoke(file);
                loaded = text == null ? null : ReadSheet(text);
                cache[file] = loaded;
                return loaded;
            }

            // Bus aliases hold for the whole design, wherever they are defined (a sheet, or the project file).
            var aliases = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (name, members) in ProjectAliases(projectJson)) aliases[name] = members;
            var seen = new HashSet<KiCadSchFile>();
            void Gather(KiCadSchFile f, int depth)
            {
                if (!seen.Add(f) || depth > 32) return;
                foreach (var kv in f.BusAliases)
                    if (!aliases.ContainsKey(kv.Key)) aliases[kv.Key] = kv.Value;
                foreach (var child in f.Sheets)
                    if (Load(child.File) is { } c) Gather(c, depth + 1);
            }

            Gather(root, 0);

            int Item(string key)
            {
                if (!names.TryGetValue(key, out int item)) names[key] = item = uf.Add();
                return item;
            }

            int Point(string path, double x, double y)
            {
                var k = (path, Key(x, y));
                if (!points.TryGetValue(k, out int id)) points[k] = id = uf.Add();
                return id;
            }

            void Name(int item, string key, string name, int priority, bool power)
            {
                if (names.TryGetValue(key, out int other)) uf.Union(item, other);
                else names[key] = item;
                named.Add((item, name, priority, power, depthNow));
            }

            // Priorities as KiCad's drivers: global label > power > local label > hierarchical label / sheet pin.
            void Walk(KiCadSchFile sheet, string path, string sheetNamePath, int depth)
            {
                design.Sheets++;
                design.Buses += sheet.Buses;
                foreach (var shape in sheet.Shapes) design.SheetShapes.Add((path, shape));   // [shapes]
                depthNow = depth;

                var attach = new List<(double X, double Y, int Item)>();
                var sheetPower = new HashSet<string>(StringComparer.Ordinal);
                var sheetLocals = new List<(int Item, string Name)>();
                foreach (var w in sheet.Wires)
                {
                    int a = Point(path, w.X1, w.Y1), b = Point(path, w.X2, w.Y2);
                    uf.Union(a, b);
                    attach.Add((w.X1, w.Y1, a));
                    attach.Add((w.X2, w.Y2, b));
                }

                foreach (var (x, y) in sheet.Junctions) attach.Add((x, y, Point(path, x, y)));
                foreach (var l in sheet.Labels)
                {
                    int item = Point(path, l.X, l.Y);
                    attach.Add((l.X, l.Y, item));
                    string name = Unescape(l.Name);
                    switch (l.Kind)
                    {
                        case KiCadLabelKind.Global: Name(item, "G|" + name, name, 6, false); break;
                        case KiCadLabelKind.Local:
                            Name(item, "L|" + path + "|" + name, sheetNamePath + "/" + name, 4, false);
                            sheetLocals.Add((item, name));
                            break;
                        default:
                            // a hierarchical label is also the sheet's local net of its name (KiCad joins them)
                            Name(item, "H|" + path + "|" + name, sheetNamePath + "/" + name, 2, false);
                            uf.Union(item, Item("L|" + path + "|" + name));
                            break;
                    }
                }

                foreach (var (x, y) in sheet.NoConnects)
                {
                    int item = Point(path, x, y);
                    attach.Add((x, y, item));
                    noConnects.Add(item);
                }

                foreach (var s in sheet.Symbols)
                {
                    if (!sheet.LibSymbols.TryGetValue(s.LibKey, out var def) && !sheet.LibSymbols.TryGetValue(s.LibId, out def))
                    {
                        design.Warnings.Add($"{s.ReferenceAt(path)}: '{s.LibKey}' sembolünün tanımı dosyada yok; bacakları bağlanamadı.");
                        continue;
                    }

                    string legacy = path.Substring(rootPath.Length) + "/" + s.Uuid;
                    string reference = s.References.ContainsKey(path) ? s.ReferenceAt(path)
                        : root.LegacyReferences.TryGetValue(legacy, out var old) && old.Length > 0 ? old : s.Reference;
                    if (!def.IsPower && !reference.StartsWith("#", StringComparison.Ordinal))
                        design.Symbols.Add(new KiCadDesignSymbol { Reference = reference, SheetPath = sheetNamePath, Placement = s, Definition = def });

                    foreach (var pin in def.PinsOfUnit(s.Unit, s.BodyStyle))
                    {
                        var (x, y) = PinPosition(s, pin);
                        int item = Point(path, x, y);   // a pin joins wires at their ends only, not along them
                        if (def.IsPower)
                        {
                            // A power symbol names the net by its value (KiCad 7+; the hidden pin's name before), through its
                            // power-input pin; PWR_FLAG's is an output and names nothing.
                            if (pin.ElectricalType != "power_in") continue;
                            string net = Unescape(s.Value.Length > 0 ? s.Value : pin.CleanName);
                            Name(item, "G|" + net, net, 5, true);
                            sheetPower.Add(net);
                            continue;
                        }

                        if (pin.Hidden && pin.ElectricalType == "power_in" && pin.CleanName.Length > 0)
                        {
                            // a hidden power pin joins the net of its name (also on symbols not marked as power ones)
                            Name(item, "G|" + pin.CleanName, pin.CleanName, 5, true);
                            sheetPower.Add(pin.CleanName);
                        }

                        if (reference.StartsWith("#", StringComparison.Ordinal)) continue;   // PWR_FLAG and the like
                        pinItems.Add((item, new KiCadNetNode(reference, pin.Number, pin.CleanName)));
                    }
                }

                // Anything placed on a wire joins it, wire ends included (KiCad's T); crossings alone do not.
                foreach (var (x, y, item) in attach)
                {
                    foreach (var w in sheet.Wires)
                        if (OnSegment((x, y), w)) uf.Union(item, Point(path, w.X1, w.Y1));
                }

                // A local label joins the power net of its name when that power symbol is on the same sheet (KiCad matches them there).
                foreach (var (item, name) in sheetLocals)
                    if (sheetPower.Contains(name) && names.TryGetValue("G|" + name, out int power)) uf.Union(item, power);

                Buses(sheet, path, aliases, Item, uf);

                foreach (var child in sheet.Sheets)
                {
                    string childPath = path + "/" + child.Uuid;
                    string childNames = sheetNamePath + "/" + child.Name;
                    foreach (var pin in child.Pins)
                    {
                        // A bus pin joins each member to the sub-sheet's member of the same name.
                        if (KiCadBusNames.Members(Unescape(pin.Name), aliases) is { } busMembers)
                        {
                            foreach (var m in busMembers) uf.Union(Item(SheetPinMember(path, child.Uuid, pin.Name, m.Net)), Item("L|" + childPath + "|" + m.Net));
                            continue;
                        }

                        int item = Point(path, pin.X, pin.Y);
                        foreach (var w in sheet.Wires)
                            if (OnSegment((pin.X, pin.Y), w)) uf.Union(item, Point(path, w.X1, w.Y1));
                        string pinName = Unescape(pin.Name);
                        Name(item, "H|" + childPath + "|" + pinName, childNames + "/" + pinName, 1, false);   // the child's hierarchical label
                    }

                    if (depth > 32)
                    {
                        design.Warnings.Add($"{childNames}: sayfa iç içeliği 32'yi aşıyor (kendini içeren bir sayfa?); daha derini okunmadı.");
                        continue;
                    }

                    var childFile = Load(child.File);
                    if (childFile == null)
                    {
                        design.Warnings.Add($"{childNames}: alt sayfa dosyası '{child.File}' bulunamadı; içindekiler alınmadı.");
                        continue;
                    }

                    Walk(childFile, childPath, childNames, depth + 1);
                    depthNow = depth;
                }
            }

            Walk(root, rootPath, string.Empty, 0);

            // Nets: the groups holding pins, named by their strongest driver; of equal ones the one nearest the root
            // sheet, then the first by name (as KiCad).
            var byRoot = new Dictionary<int, KiCadNet>();
            var chosen = new HashSet<int>();
            foreach (var (item, node) in pinItems)
            {
                int r = uf.Find(item);
                if (!byRoot.TryGetValue(r, out var net)) byRoot[r] = net = new KiCadNet();
                if (!net.Nodes.Any(n => n.Reference == node.Reference && n.Pin == node.Pin)) net.Nodes.Add(node);
            }

            foreach (var (item, name, _, power, _) in named.OrderByDescending(n => n.Priority).ThenBy(n => n.Depth).ThenBy(n => n.Name, StringComparer.Ordinal))
            {
                int r = uf.Find(item);
                if (!byRoot.TryGetValue(r, out var net) || !chosen.Add(r)) continue;
                net.Name = name;
                net.Named = true;
                net.Power = power;
            }

            foreach (int item in noConnects)
                if (byRoot.TryGetValue(uf.Find(item), out var net)) net.NoConnect = true;

            foreach (var net in byRoot.Values)
            {
                net.Nodes.Sort((a, b) => string.Compare(a.Reference, b.Reference, StringComparison.Ordinal) != 0
                    ? NaturalCompare(a.Reference, b.Reference) : NaturalCompare(a.Pin, b.Pin));
                if (!net.Named)
                {
                    var first = net.Nodes[0];
                    net.Name = net.Nodes.Count == 1 ? $"unconnected-({first.Reference}-Pad{first.Pin})" : $"Net-({first.Reference}-Pad{first.Pin})";
                }

                design.Nets.Add(net);
            }

            design.Nets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            return design;
        }

        // ====================================================================== onto our schematic

        /// <summary>
        /// Imports a design: each symbol (all its units as one part) mapped onto a simulation model with its own value and
        /// footprint, placed where KiCad has it (sub-sheets side by side to the right of the root), its pins joined to their
        /// KiCad nets by net labels, ground symbols on GND and supply symbols on the other power nets, no-connect flags kept.
        /// The result's netlist is checked against KiCad's; when two of our pins happen to meet where KiCad's did not, the
        /// placement is spread out until none do.
        /// </summary>
        public static KiCadSchematicImportResult Import(string rootText, PartLibrary library, Func<string, string?>? readFile = null, string? projectJson = null)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            var design = Read(rootText, readFile, projectJson);
            var result = new KiCadSchematicImportResult { Design = design };
            var netOf = new Dictionary<(string, string), KiCadNet>();
            foreach (var net in design.Nets)
            foreach (var node in net.Nodes)
                netOf[(node.Reference, node.Pin)] = net;

            // One part per placed unit: a symbol drawn as one unit keeps its reference; one whose units are placed
            // separately (both halves of a dual op-amp) becomes a part per unit, U1A, U1B, each with that unit's pins.
            var symbols = design.Symbols.GroupBy(s => (s.Reference, s.Placement.Unit))
                .Select(g => g.First())
                .OrderBy(s => s.Reference, Comparer<string>.Create(NaturalCompare)).ThenBy(s => s.Placement.Unit).ToList();
            var placedUnits = symbols.GroupBy(s => s.Reference, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            var mapped = new List<(KiCadDesignSymbol Symbol, string RefDes, CustomPart Part, PartDefinition Def)>();
            foreach (var s in symbols)
            {
                if (s.Placement.DoNotPopulate)
                {
                    if (!result.NotFitted.Contains(s.Reference))
                    {
                        result.NotFitted.Add(s.Reference);
                        result.Report.Add($"{s.Reference} ({s.Placement.LibId}): takılmıyor (KiCad'de DNP), benzetime alınmadı.");
                    }

                    continue;
                }

                bool split = placedUnits[s.Reference] > 1 && (s.Definition?.UnitCount ?? 1) > 1;
                string refDes = split ? s.Reference + UnitLetter(s.Placement.Unit) : s.Reference;
                var instance = ForInstance(s);
                var footprint = FindFootprint(s.Placement.Footprint);
                var custom = KiCadLibraryImport.ToCustomPart(instance, footprint, library, false, out string report);
                string status = report.Split('\n')[0];
                status = status.Substring(status.LastIndexOf(':') + 1).Trim();
                string what = $"{refDes} ({s.Placement.LibId}{(s.Placement.Value.Length > 0 ? ", " + s.Placement.Value : string.Empty)})";
                if (custom == null || library.Find(custom.BasePartId) is not { } def)
                {
                    result.Skipped++;
                    result.Missing.Add(refDes);
                    string why = report.Split('\n').Skip(1).LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? status;
                    result.Report.Add($"{what}: alınmadı — {why}");
                    continue;
                }

                if (status == nameof(KiCadImportStatus.Generic)) result.Generic++;
                else result.Mapped++;
                result.Report.Add($"{what} → {def.Name}" + (status == nameof(KiCadImportStatus.Generic) ? " (genel parça: elektriksel modeli yok)" : string.Empty)
                                  + (split ? $" ({s.Reference}'in {s.Placement.Unit}. birimi, ayrı parça)" : string.Empty));
                mapped.Add((s, refDes, custom, def));
            }

            // Sheets side by side: each sheet instance keeps its own drawing, shifted right of the one before.
            var offsets = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
            double right = double.NegativeInfinity, top = 0;
            foreach (var sheet in mapped.GroupBy(m => m.Symbol.SheetPath).OrderBy(g => g.Key.Length).ThenBy(g => g.Key, StringComparer.Ordinal))
            {
                double minX = sheet.Min(m => m.Symbol.Placement.X), maxX = sheet.Max(m => m.Symbol.Placement.X), minY = sheet.Min(m => m.Symbol.Placement.Y);
                if (double.IsNegativeInfinity(right))
                {
                    offsets[sheet.Key] = (0, 0);
                    top = minY;
                    right = maxX;
                    continue;
                }

                double dx = right + 60 - minX;
                offsets[sheet.Key] = (dx, top - minY);
                right = maxX + dx;
            }

            // Our symbols are drawn larger than KiCad's: the drawing is spread by how much (the typical part's ratio), and
            // further while two of our pins still meet where KiCad's did not.
            double scale = Spread(mapped.Select(m => (m.Symbol.Definition, m.Def)));
            double spreadUsed = scale;
            foreach (double step in new[] { 1.0, 1.5, 2.0, 3.0 })
            {
                var sch = Build(mapped, offsets, netOf, library, scale * step);
                var clash = Clashes(sch, mapped, netOf, library);
                result.Schematic = sch;
                spreadUsed = scale * step;
                if (clash == null) break;
                if (step >= 3.0) result.Report.Add("uyarı: " + clash);
            }

            // [shapes] the sheets' graphics, moved and spread as their sheet's parts were
            foreach (var (sheetPath, shape) in design.SheetShapes)
            {
                var (ox, oy) = offsets.TryGetValue(sheetPath, out var o) ? o : (0, 0);
                double G(double mm) => Math.Round(mm * spreadUsed / (2 * MmPerGridUnit)) / 2;
                var drawn = new SchematicShape { Kind = shape.Kind, Width = shape.Width > 0 ? shape.Width : 0.15, Dashed = shape.Dashed, Filled = shape.Filled };
                foreach (var (x, y) in shape.Points) drawn.Points.Add(new GridPoint(G(x + ox), G(y + oy)));
                if (shape.Kind == SheetShapeKind.Arc && drawn.ArcCircle() == null) drawn.Kind = SheetShapeKind.Line;   // flattened by the grid
                result.Schematic.Shapes.Add(drawn);
            }

            if (design.SheetShapes.Count > 0) result.Report.Add($"{design.SheetShapes.Count} çizim (çizgi, dikdörtgen, daire, yay) alındı");

            result.Schematic.Title = design.Root.Title.Length > 0 ? design.Root.Title : "KiCad";
            foreach (var w in design.Warnings) result.Report.Add("uyarı: " + w);
            return result;
        }

        /// <summary>One pin's way out: where it is, the stub's end, and what goes there (a label's text, a power net, or a no-connect flag).</summary>
        private sealed class PinOut
        {
            public GridPoint At;
            public GridPoint Tip;
            public double Ux, Uy;
            public string Name = string.Empty;
            public KiCadNet Net = null!;
        }

        private static Schematic Build(List<(KiCadDesignSymbol Symbol, string RefDes, CustomPart Part, PartDefinition Def)> mapped,
            Dictionary<string, (double X, double Y)> offsets, Dictionary<(string, string), KiCadNet> netOf, PartLibrary library, double spread)
        {
            var sch = new Schematic();
            double Grid(double mm) => Math.Round(mm * spread / (2 * MmPerGridUnit)) / 2;   // half-unit steps
            var placed = new List<(SchematicPart Part, KiCadDesignSymbol Symbol, CustomPart Custom, PartDefinition Def)>();
            foreach (var (symbol, refDes, custom, def) in mapped)
            {
                var p = symbol.Placement;
                var (ox, oy) = offsets[symbol.SheetPath];
                var (rotation, mirror) = Orientation(symbol, custom, def);
                var part = new SchematicPart
                {
                    Uid = Schematic.NewPartUid(),
                    RefDes = refDes,
                    PartId = def.Id,
                    Position = new GridPoint(Grid(p.X + ox), Grid(p.Y + oy)),
                    Rotation = rotation,
                    Mirror = mirror
                };
                foreach (var kv in custom.Parameters) part.Parameters[kv.Key] = kv.Value;
                sch.Parts.Add(part);
                placed.Add((part, symbol, custom, def));
            }

            List<PinOut> Outs(SchematicPart part, KiCadDesignSymbol symbol, CustomPart custom, PartDefinition def)
            {
                var list = new List<PinOut>();
                foreach (var (ours, number) in PinMap(custom))
                {
                    var pin = def.Pins.FirstOrDefault(x => string.Equals(x.Name, ours, StringComparison.OrdinalIgnoreCase));
                    if (pin == null || !netOf.TryGetValue((symbol.Reference, number), out var net)) continue;
                    // A short wire out of the pin, away from the body, carries the label or the power symbol clear of it.
                    double ux = Math.Abs(pin.X) >= Math.Abs(pin.Y) ? Math.Sign(pin.X) : 0, uy = ux == 0 ? Math.Sign(pin.Y) : 0;
                    if (ux == 0 && uy == 0) uy = 1;
                    var at = part.Transform(pin.X, pin.Y);
                    var tip = part.Transform(pin.X + ux * StubLength, pin.Y + uy * StubLength);
                    string name = OurName(net);
                    if (tip.X < at.X - 0.1 && !net.Power && (net.Nodes.Count > 1 || net.Named))
                    {
                        // A label's text runs to the right of its point: on a pin facing left the stub is as long as the
                        // text, so the label sits on its wire and ends at the pin, not on the body.
                        double run = Math.Max(StubLength, Math.Ceiling(LabelWidth(name) * 2) / 2);
                        tip = new GridPoint(at.X - run, at.Y);
                    }

                    list.Add(new PinOut { At = at, Tip = tip, Ux = tip.X - at.X, Uy = tip.Y - at.Y, Name = name, Net = net });
                }

                return list;
            }

            // Where KiCad's parts sit closer than ours fit, push the later one (top to bottom, left to right) right or down,
            // whichever is shorter, until nothing it carries — body, pins, labels, power symbols — touches what came before.
            var boxes = new List<(double X0, double Y0, double X1, double Y1)>();
            foreach (var item in placed.OrderBy(x => x.Part.Position.Y).ThenBy(x => x.Part.Position.X))
            {
                var box = Box(item.Part, item.Def, Outs(item.Part, item.Symbol, item.Custom, item.Def));
                for (int guard = 0; guard < 400; guard++)
                {
                    int hit = boxes.FindIndex(b => b.X0 < box.X1 && box.X0 < b.X1 && b.Y0 < box.Y1 && box.Y0 < b.Y1);
                    if (hit < 0) break;
                    var o = boxes[hit];
                    double dx = Math.Ceiling((o.X1 - box.X0) * 2) / 2, dy = Math.Ceiling((o.Y1 - box.Y0) * 2) / 2;
                    var shift = dx <= dy ? new GridPoint(dx, 0) : new GridPoint(0, dy);
                    item.Part.Position += shift;
                    box = (box.X0 + shift.X, box.Y0 + shift.Y, box.X1 + shift.X, box.Y1 + shift.Y);
                }

                boxes.Add(box);
            }

            foreach (var (part, symbol, custom, def) in placed)
            {
                foreach (var o in Outs(part, symbol, custom, def))
                {
                    if (o.Net.Nodes.Count < 2 && !o.Net.Named)
                    {
                        if (o.Net.NoConnect) sch.NoConnects.Add(new NoConnectFlag { Position = o.At });
                        continue;
                    }

                    sch.Wires.Add(new SchematicWire { Points = { o.At, o.Tip } });
                    if (o.Net.Power && (o.Name == "GND" || o.Name == "0"))
                    {
                        var gnd = sch.AddPart(library, "ground", new GridPoint(o.Tip.X, o.Tip.Y + 1));
                        gnd.RefDes = sch.NextRefDes("#GND");
                    }
                    else if (o.Net.Power)
                    {
                        var rail = sch.AddPart(library, "power-rail", new GridPoint(o.Tip.X, o.Tip.Y - 1));
                        rail.Parameters["net"] = o.Name;
                    }
                    else sch.Labels.Add(new NetLabel { Name = o.Name, Position = o.Tip });
                }
            }

            return sch;
        }

        /// <summary>About how wide a net label is drawn, grid units (its flag and its text).</summary>
        private static double LabelWidth(string name) => 0.75 * name.Length + 2;

        /// <summary>What a placed part covers with what it carries (grid units, a unit of margin): its body, pins, stubs, labels by their text, power symbols.</summary>
        private static (double X0, double Y0, double X1, double Y1) Box(SchematicPart part, PartDefinition def, List<PinOut> outs)
        {
            double hw = Math.Max(def.HalfWidth, 0.5), hh = Math.Max(def.HalfHeight, 0.5);
            if (part.Rotation % 180 != 0) (hw, hh) = (hh, hw);
            double x0 = part.Position.X - hw, x1 = part.Position.X + hw, y0 = part.Position.Y - hh, y1 = part.Position.Y + hh;
            void Grow(double x, double y)
            {
                x0 = Math.Min(x0, x);
                x1 = Math.Max(x1, x);
                y0 = Math.Min(y0, y);
                y1 = Math.Max(y1, y);
            }

            foreach (var pin in def.Pins)
            {
                var at = part.Transform(pin.X, pin.Y);
                Grow(at.X, at.Y);
            }

            foreach (var o in outs)
            {
                Grow(o.Tip.X, o.Tip.Y);
                if (o.Net.Nodes.Count < 2 && !o.Net.Named) continue;
                if (o.Net.Power)
                {
                    Grow(o.Tip.X - 1.5, o.Tip.Y + (o.Name == "GND" ? 2 : -2.5));
                    Grow(o.Tip.X + 1.5, o.Tip.Y);
                    continue;
                }

                // a label's text runs to the right of its point
                Grow(o.Tip.X + LabelWidth(o.Name), o.Tip.Y - 0.6);
                Grow(o.Tip.X, o.Tip.Y + 0.6);
            }

            return (x0 - 0.5, y0 - 0.5, x1 + 0.5, y1 + 0.5);
        }

        /// <summary>How much to spread KiCad's drawing: how much larger our symbols are, the median over the parts, half as much again for the labels (1.5–5, in quarter steps).</summary>
        private static double Spread(IEnumerable<(KiCadSymbol? KiCad, PartDefinition Ours)> parts)
        {
            var ratios = new List<double>();
            foreach (var (kicad, ours) in parts)
            {
                if (kicad == null || !kicad.HasBounds) continue;
                double theirs = Math.Max(kicad.MaxX - kicad.MinX, kicad.MaxY - kicad.MinY) / MmPerGridUnit;
                double mine = 2 * Math.Max(Math.Max(ours.HalfWidth, ours.HalfHeight), ours.Pins.Count == 0 ? 0 : ours.Pins.Max(x => Math.Max(Math.Abs(x.X), Math.Abs(x.Y))));
                if (theirs > 0.5 && mine > 0) ratios.Add(mine / theirs);
            }

            if (ratios.Count == 0) return 1;
            ratios.Sort();
            double median = ratios[ratios.Count / 2];
            // half as much again: room for the labels on their stubs
            return Math.Max(1.5, Math.Min(5, Math.Ceiling(median * 1.5 * 4) / 4));
        }

        /// <summary>A pin pair of ours that lands on one net where KiCad has two, described; null when the netlists agree.</summary>
        private static string? Clashes(Schematic sch, List<(KiCadDesignSymbol Symbol, string RefDes, CustomPart Part, PartDefinition Def)> mapped,
            Dictionary<(string, string), KiCadNet> netOf, PartLibrary library)
        {
            var ours = sch.BuildNetlist(library);
            foreach (var (symbol, refDes, custom, _) in mapped)
            foreach (var (pin, number) in PinMap(custom))
            {
                var net = ours.NetOf(refDes, pin);
                if (net == null || !netOf.TryGetValue((symbol.Reference, number), out var kicad)) continue;
                foreach (var c in net.Connections)
                {
                    var other = mapped.FirstOrDefault(m => m.RefDes == c.RefDes);
                    if (other.Part == null) continue;
                    string? otherNumber = PinMap(other.Part).Where(x => string.Equals(x.Ours, c.Pin, StringComparison.OrdinalIgnoreCase)).Select(x => x.Number).FirstOrDefault();
                    if (otherNumber == null) continue;
                    if (!netOf.TryGetValue((other.Symbol.Reference, otherNumber), out var otherNet) || otherNet != kicad)
                        return $"{symbol.Reference}.{number} ile {other.Symbol.Reference}.{otherNumber} bizim yerleşimde aynı noktaya düşüyor; KiCad'de ayrı netler.";
                }
            }

            return null;
        }

        private static IEnumerable<(string Ours, string Number)> PinMap(CustomPart part)
        {
            if (!part.Parameters.TryGetValue("pinmap", out var map)) yield break;
            foreach (var pair in map.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = pair.LastIndexOf(':');
                if (colon > 0) yield return (pair.Substring(0, colon).Trim(), pair.Substring(colon + 1).Trim());
            }
        }

        /// <summary>The net's name on our single sheet: the root's local labels without KiCad's leading "/".</summary>
        private static string OurName(KiCadNet net) =>
            net.Name.StartsWith("/", StringComparison.Ordinal) ? net.Name.Substring(1) : net.Name;

        /// <summary>The library definition with this placement's own value, footprint and fields.</summary>
        private static KiCadSymbol ForInstance(KiCadDesignSymbol s)
        {
            var d = s.Definition!;
            var copy = new KiCadSymbol
            {
                Name = d.Name, Library = d.Library, Extends = null, UnitCount = d.UnitCount, IsPower = d.IsPower, InBom = s.Placement.InBom,
                OnBoard = s.Placement.OnBoard, ExcludeFromSim = d.ExcludeFromSim, PinNamesHidden = d.PinNamesHidden, PinNumbersHidden = d.PinNumbersHidden,
                MinX = d.MinX, MinY = d.MinY, MaxX = d.MaxX, MaxY = d.MaxY, HasBounds = d.HasBounds
            };
            foreach (var kv in d.Properties) copy.Properties[kv.Key] = kv.Value;
            foreach (var kv in s.Placement.Properties) copy.Properties[kv.Key] = kv.Value;
            copy.Properties["Reference"] = s.Reference;
            int unit = s.Placement.Unit;
            if (d.UnitCount <= 1 || unit <= 1)
            {
                copy.Pins.AddRange(d.Pins);
                return copy;
            }

            // Another unit than the first: it trades places with unit 1, so the mapping takes its pins (and the common ones).
            foreach (var pin in d.Pins)
            {
                var c = pin.Clone();
                if (pin.Unit == unit) c.Unit = 1;
                else if (pin.Unit == 1) c.Unit = unit;
                copy.Pins.Add(c);
            }

            return copy;
        }

        private static string UnitLetter(int unit) =>
            unit >= 1 && unit <= 26 ? ((char)('A' + unit - 1)).ToString() : "_" + unit.ToString(CultureInfo.InvariantCulture);

        /// <summary>The footprint a "Library:Name" field names, when the footprint library knows it (or makes it).</summary>
        private static Footprint? FindFootprint(string field)
        {
            if (string.IsNullOrWhiteSpace(field)) return null;
            string name = field.Substring(field.IndexOf(':') + 1);
            try
            {
                return FootprintLibrary.Get(name);
            }
            catch (Exception ex) when (ex is FormatException || ex is IndexOutOfRangeException || ex is ArgumentException || ex is OverflowException || ex is KeyNotFoundException || ex is InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// A sheet's buses: segments joined end to end (and where one ends on another), and the bus names on them — bus
        /// labels, global ones, hierarchical labels, sheet pins. On one bus, members with the same key are one net (a
        /// vector's index, a group member's own name): "DPHY0_P{DPHY}" and "DPHY0_C{DPHY}" join DPHY0_P.C_N with DPHY0_C.C_N.
        /// </summary>
        private static void Buses(KiCadSchFile sheet, string path, Dictionary<string, List<string>> aliases, Func<string, int> item, UnionFind uf)
        {
            if (sheet.BusWires.Count == 0) return;
            var local = new UnionFind();
            var points = new Dictionary<(long, long), int>();
            int Point(double x, double y)
            {
                var k = Key(x, y);
                if (!points.TryGetValue(k, out int id)) points[k] = id = local.Add();
                return id;
            }

            int? On(double x, double y)
            {
                foreach (var w in sheet.BusWires)
                {
                    if (Key(w.X1, w.Y1) == Key(x, y) || Key(w.X2, w.Y2) == Key(x, y) || OnSegment((x, y), w)) return Point(w.X1, w.Y1);
                }

                return null;
            }

            foreach (var w in sheet.BusWires) local.Union(Point(w.X1, w.Y1), Point(w.X2, w.Y2));
            foreach (var w in sheet.BusWires)
            {
                foreach (var (x, y) in new[] { (w.X1, w.Y1), (w.X2, w.Y2) })
                {
                    foreach (var other in sheet.BusWires)
                        if (OnSegment((x, y), other)) local.Union(Point(x, y), Point(other.X1, other.Y1));
                }
            }

            // What is on a bus: labels (their members are the sheet's nets of those names, or the design's for global
            // ones) and sheet pins (each pin's members its own, joined to the sub-sheet).
            var named = new List<(double X, double Y, string Name, Func<string, string> Scope)>();
            foreach (var l in sheet.Labels)
                named.Add((l.X, l.Y, Unescape(l.Name), l.Kind == KiCadLabelKind.Global ? net => "G|" + net : net => "L|" + path + "|" + net));
            foreach (var sh in sheet.Sheets)
                foreach (var pin in sh.Pins)
                {
                    string uuid = sh.Uuid, pinName = pin.Name;
                    named.Add((pin.X, pin.Y, Unescape(pin.Name), net => SheetPinMember(path, uuid, pinName, net)));
                }

            var byBus = new Dictionary<int, List<(string Key, string Item, string Net)>>();
            foreach (var (x, y, name, scope) in named)
            {
                if (KiCadBusNames.Members(name, aliases) is not { } members || On(x, y) is not int bus) continue;
                int root = local.Find(bus);
                if (!byBus.TryGetValue(root, out var list)) byBus[root] = list = new List<(string, string, string)>();
                foreach (var m in members) list.Add((m.Key, scope(m.Net), m.Net));
            }

            // The wires that leave a bus through its entries carry their member's name as a label: a member joins the
            // sheet's net of its full name when one is labelled so (only then: a name nothing on the sheet carries is not
            // a net here, and two blocks' pins of one name stay apart).
            var labelled = new HashSet<string>(sheet.Labels.Where(l => l.Kind != KiCadLabelKind.Global).Select(l => Unescape(l.Name)), StringComparer.Ordinal);
            foreach (var list in byBus.Values)
            {
                foreach (var group in list.GroupBy(m => m.Key, StringComparer.Ordinal))
                {
                    int first = -1;
                    foreach (var (_, key, net) in group)
                    {
                        int id = item(key);
                        if (first < 0) first = id;
                        else uf.Union(first, id);
                        if (labelled.Contains(net)) uf.Union(first, item("L|" + path + "|" + net));
                    }
                }
            }
        }

        /// <summary>A bus sheet pin's member on the parent sheet (its own: two blocks' pins of one name are not one net).</summary>
        private static string SheetPinMember(string path, string sheetUuid, string pin, string net) => "S|" + path + "|" + sheetUuid + "|" + pin + "|" + net;

        /// <summary>The bus aliases of a KiCad project file (<c>schematic.bus_aliases</c>: name → members).</summary>
        private static IEnumerable<(string Name, List<string> Members)> ProjectAliases(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) yield break;
            System.Text.Json.JsonDocument doc;
            try
            {
                doc = System.Text.Json.JsonDocument.Parse(json!);
            }
            catch (System.Text.Json.JsonException)
            {
                yield break;
            }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("schematic", out var schematic) || !schematic.TryGetProperty("bus_aliases", out var aliases)
                    || aliases.ValueKind != System.Text.Json.JsonValueKind.Object) yield break;
                foreach (var a in aliases.EnumerateObject())
                {
                    if (a.Value.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                    var members = new List<string>();
                    foreach (var m in a.Value.EnumerateArray())
                        if (m.ValueKind == System.Text.Json.JsonValueKind.String) members.Add(m.GetString()!);
                    yield return (a.Name, members);
                }
            }
        }

        private static readonly (string Escape, string Text)[] Escapes =
        {
            ("{slash}", "/"), ("{backslash}", "\\"), ("{lt}", "<"), ("{gt}", ">"), ("{colon}", ":"), ("{dblquote}", "\""),
            ("{quote}", "'"), ("{space}", " "), ("{tab}", "\t"), ("{return}", "\n"), ("{bar}", "|"), ("{comma}", ","),
        };

        /// <summary>A KiCad text with its escapes written out ("VPP{slash}MCLR" → "VPP/MCLR"): both spellings are one name.</summary>
        public static string Unescape(string text)
        {
            if (text.IndexOf('{') < 0) return text;
            foreach (var (escape, plain) in Escapes) text = text.Replace(escape, plain);
            return text;
        }

        private static int NaturalCompare(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    int c = long.Parse(a.Substring(si, Math.Min(18, i - si)), CultureInfo.InvariantCulture).CompareTo(long.Parse(b.Substring(sj, Math.Min(18, j - sj)), CultureInfo.InvariantCulture));
                    if (c != 0) return c;
                }
                else
                {
                    int c = a[i].CompareTo(b[j]);
                    if (c != 0) return c;
                    i++;
                    j++;
                }
            }

            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
