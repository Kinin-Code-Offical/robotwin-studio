using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;

namespace RobotTwin.CoreSim.Formats
{
    // ====================================================================== footprint metadata

    /// <summary>A <c>(model ...)</c> reference of a KiCad footprint (kept as metadata; not loaded).</summary>
    public sealed class KiCadModelRef
    {
        /// <summary>Path as written, usually with a variable: "${KICAD8_3DMODEL_DIR}/Package_DIP.3dshapes/DIP-8_W7.62mm.wrl".</summary>
        public string Path { get; set; } = string.Empty;
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double OffsetZ { get; set; }
        public double ScaleX { get; set; } = 1;
        public double ScaleY { get; set; } = 1;
        public double ScaleZ { get; set; } = 1;
        public double RotateX { get; set; }
        public double RotateY { get; set; }
        public double RotateZ { get; set; }
        public bool Hidden { get; set; }
        public double Opacity { get; set; } = 1;
    }

    /// <summary>What a KiCad footprint carries beyond <see cref="Footprint"/> (see <see cref="KiCadLibraryImport.GetFootprintInfo"/>).</summary>
    public sealed class KiCadFootprintInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Library { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;

        /// <summary>Tokens of <c>(attr ...)</c>: "smd", "through_hole", "virtual", "exclude_from_bom", ...</summary>
        public List<string> Attributes { get; } = new List<string>();

        public List<KiCadModelRef> Models { get; } = new List<KiCadModelRef>();

        /// <summary>True when the courtyard came from F.CrtYd (or B.CrtYd) graphics, false when derived from the pads.</summary>
        public bool CourtyardFromFile { get; set; }

        /// <summary>Number of <c>np_thru_hole</c> pads (imported as unnamed drilled pads).</summary>
        public int NonPlatedHoles { get; set; }

        /// <summary>True when the file used the KiCad 4/5 <c>(module ...)</c> keyword.</summary>
        public bool IsLegacyModule { get; set; }

        public string FileVersion { get; set; } = string.Empty;
        public string Generator { get; set; } = string.Empty;
        public List<string> Warnings { get; } = new List<string>();
    }

    // ====================================================================== symbols

    /// <summary>A pin of a KiCad library symbol. Coordinates in mm, library frame (Y up).</summary>
    public sealed class KiCadPin
    {
        public string Number { get; set; } = string.Empty;

        /// <summary>Name as written ("~" = unnamed, "~{RESET}" = overbar markup).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>input, output, bidirectional, tri_state, passive, free, unspecified, power_in, power_out, open_collector, open_emitter, no_connect.</summary>
        public string ElectricalType { get; set; } = "passive";

        public string GraphicStyle { get; set; } = "line";
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Direction from the connection point towards the body (0 = body to the right).</summary>
        public double Angle { get; set; }

        public double Length { get; set; }

        /// <summary>Unit (1-based); 0 = common to all units.</summary>
        public int Unit { get; set; }

        /// <summary>Body style (1 = normal, 2 = De Morgan); 0 = common.</summary>
        public int BodyStyle { get; set; }

        public bool Hidden { get; set; }

        /// <summary>Name without KiCad markup; empty for unnamed pins.</summary>
        public string CleanName => KiCadLibraryImport.CleanPinName(Name);

        internal KiCadPin Clone() => (KiCadPin)MemberwiseClone();
    }

    /// <summary>A symbol of a <c>.kicad_sym</c> library (KiCad 6–8), with <c>extends</c> already resolved.</summary>
    public sealed class KiCadSymbol
    {
        public string Name { get; set; } = string.Empty;
        public string Library { get; set; } = string.Empty;

        /// <summary>Parent symbol name for derived symbols (its pins and graphics are inherited).</summary>
        public string? Extends { get; set; }

        public Dictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<KiCadPin> Pins { get; } = new List<KiCadPin>();
        public int UnitCount { get; set; } = 1;
        public bool IsPower { get; set; }
        public bool InBom { get; set; } = true;
        public bool OnBoard { get; set; } = true;
        public bool ExcludeFromSim { get; set; }
        public bool PinNamesHidden { get; set; }
        public bool PinNumbersHidden { get; set; }

        /// <summary>Bounding box of the graphics and pins of body style 1, mm, library frame (Y up).</summary>
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public bool HasBounds { get; set; }

        public List<string> Warnings { get; } = new List<string>();

        public string? Property(string key) => Properties.TryGetValue(key, out var v) ? v : null;

        public string Reference => Property("Reference") ?? string.Empty;
        public string Value => Property("Value") ?? string.Empty;

        /// <summary>Footprint property, "Lib:Name" (may be empty).</summary>
        public string FootprintRef => Property("Footprint") ?? string.Empty;

        public string Datasheet => Property("Datasheet") ?? string.Empty;

        /// <summary>"Description" (KiCad 8) or "ki_description" (KiCad 6/7).</summary>
        public string Description => Property("Description") ?? Property("ki_description") ?? string.Empty;

        public string Keywords => Property("ki_keywords") ?? string.Empty;

        public IReadOnlyList<string> FootprintFilters =>
            (Property("ki_fp_filters") ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        public string FullName => string.IsNullOrEmpty(Library) ? Name : Library + ":" + Name;

        /// <summary>Pins of one unit (plus pins common to all units) in one body style.</summary>
        public IEnumerable<KiCadPin> PinsOfUnit(int unit, int bodyStyle = 1) =>
            Pins.Where(p => (p.Unit == 0 || p.Unit == unit) && (p.BodyStyle == 0 || p.BodyStyle == bodyStyle));

        public override string ToString() => FullName;
    }

    // ====================================================================== import report

    public enum KiCadImportStatus
    {
        /// <summary>Mapped onto a simulation model (or a connector part for connectors).</summary>
        Mapped,

        /// <summary>No electrical model fits; imported as a generic N-pin connector-like part.</summary>
        Generic,

        /// <summary>Intentionally not imported (power symbols, ...).</summary>
        Skipped,

        /// <summary>Could not be imported.</summary>
        Failed
    }

    public sealed class KiCadImportEntry
    {
        public string Symbol { get; set; } = string.Empty;
        public KiCadImportStatus Status { get; set; }
        public string? PartId { get; set; }
        public string? BasePartId { get; set; }
        public string? FootprintId { get; set; }
        public string Details { get; set; } = string.Empty;

        public override string ToString() => $"{Symbol}: {Status}" + (BasePartId != null ? " -> " + BasePartId : string.Empty);
    }

    /// <summary>Result details of <see cref="KiCadLibraryImport.ImportLibraries"/>.</summary>
    public sealed class ImportReport
    {
        /// <summary>Every imported footprint (ids already de-conflicted); register them with <see cref="FootprintLibrary.Register"/>.</summary>
        public List<Footprint> Footprints { get; } = new List<Footprint>();

        public List<KiCadSymbol> Symbols { get; } = new List<KiCadSymbol>();
        public List<KiCadImportEntry> Entries { get; } = new List<KiCadImportEntry>();
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        public int Count(KiCadImportStatus status) => Entries.Count(e => e.Status == status);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("KiCad içe aktarımı: ").Append(Footprints.Count).Append(" footprint, ").Append(Symbols.Count).Append(" sembol; ")
                .Append(Count(KiCadImportStatus.Mapped)).Append(" eşlendi, ").Append(Count(KiCadImportStatus.Generic)).Append(" genel, ")
                .Append(Count(KiCadImportStatus.Skipped)).Append(" atlandı, ").Append(Count(KiCadImportStatus.Failed)).Append(" başarısız.\n");
            foreach (var e in Errors) sb.Append("HATA: ").Append(e).Append('\n');
            foreach (var w in Warnings) sb.Append("uyarı: ").Append(w).Append('\n');
            foreach (var e in Entries)
            {
                sb.Append("- ").Append(e.Symbol).Append(" [").Append(e.Status).Append(']');
                if (e.BasePartId != null) sb.Append(" -> ").Append(e.BasePartId);
                if (e.FootprintId != null) sb.Append(", footprint ").Append(e.FootprintId);
                sb.Append('\n');
                foreach (var line in e.Details.Split('\n'))
                {
                    if (line.Length > 0) sb.Append("    ").Append(line).Append('\n');
                }
            }

            return sb.ToString();
        }
    }

    // ====================================================================== importer

    /// <summary>
    /// Imports KiCad 6–8 libraries (and KiCad 4/5 <c>(module ...)</c> footprints): footprints
    /// from <c>.kicad_mod</c> files and <c>.pretty</c> folders, symbols from <c>.kicad_sym</c>,
    /// and turns symbols into <see cref="CustomPart"/>s on top of the built-in simulation models.
    ///
    /// Coordinates. KiCad footprints use millimetres with Y pointing down, exactly like
    /// <see cref="Footprint"/>, so pad and graphic coordinates are taken unchanged (this is the
    /// inverse of <see cref="KiCadExporter.ExportFootprint"/>). A pad's <c>(at x y a)</c> angle
    /// rotates its size: 90/270 swap width and height; other angles use the rotated bounding box
    /// (shape becomes Rect). A footprint stored on B.Cu (copied from a board) is un-flipped
    /// (Y mirrored, B/F layers swapped) back to its library form.
    /// </summary>
    public static class KiCadLibraryImport
    {
        private static readonly ConditionalWeakTable<Footprint, KiCadFootprintInfo> InfoTable = new ConditionalWeakTable<Footprint, KiCadFootprintInfo>();

        /// <summary>Metadata (3D models, tags, warnings ...) of a footprint created by this importer.</summary>
        public static KiCadFootprintInfo? GetFootprintInfo(Footprint footprint) =>
            footprint != null && InfoTable.TryGetValue(footprint, out var info) ? info : null;

        // ================================================================== footprints

        /// <summary>Parses a <c>.kicad_mod</c> (KiCad 6–8 <c>footprint</c> or legacy <c>module</c>).</summary>
        public static Footprint ImportFootprint(string kicadModText) => ImportFootprint(kicadModText, null);

        /// <summary>As <see cref="ImportFootprint(string)"/>, recording the library nickname in the metadata.</summary>
        public static Footprint ImportFootprint(string kicadModText, string? libraryName)
        {
            if (kicadModText == null) throw new ArgumentNullException(nameof(kicadModText));
            var fp = ParseFootprint(SExpression.Parse(kicadModText), libraryName, out var info);
            InfoTable.Remove(fp);
            InfoTable.Add(fp, info);
            return fp;
        }

        /// <summary>Every <c>*.kicad_mod</c> of a <c>.pretty</c> folder; unreadable files are skipped.</summary>
        public static IEnumerable<Footprint> ImportFootprintLibrary(string prettyDirectory) => ImportFootprintLibrary(prettyDirectory, null);

        /// <summary>As <see cref="ImportFootprintLibrary(string)"/>, collecting per-file errors.</summary>
        public static List<Footprint> ImportFootprintLibrary(string prettyDirectory, ICollection<string>? errors)
        {
            if (!Directory.Exists(prettyDirectory)) throw new DirectoryNotFoundException(prettyDirectory);
            string lib = LibraryNickname(prettyDirectory);
            var result = new List<Footprint>();
            foreach (var file in Directory.GetFiles(prettyDirectory, "*.kicad_mod").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var fp = ImportFootprint(File.ReadAllText(file), lib);
                    // KiCad names a library footprint after its file; the internal name only matters if it agrees.
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    if (!string.Equals(fp.Id, fileName, StringComparison.Ordinal))
                    {
                        GetFootprintInfo(fp)?.Warnings.Add($"iç ad '{fp.Id}' dosya adından farklı; '{fileName}' kullanıldı.");
                        fp.Id = fileName;
                        var info = GetFootprintInfo(fp);
                        if (info != null) info.Name = fileName;
                    }

                    result.Add(fp);
                }
                catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    errors?.Add($"{file}: {ex.Message}");
                }
            }

            return result;
        }

        private static Footprint ParseFootprint(SExpression root, string? libraryName, out KiCadFootprintInfo info)
        {
            if (root.Head != "footprint" && root.Head != "module")
            {
                throw new FormatException($"KiCad footprint dosyası değil: dosya '({root.Head} ...)' ile başlıyor, '(footprint ...)' ya da '(module ...)' bekleniyor.");
            }

            info = new KiCadFootprintInfo { IsLegacyModule = root.Head == "module" };
            SplitLibId(root.Text(1) ?? string.Empty, out string? idLib, out string name);
            info.Name = name;
            info.Library = libraryName ?? idLib ?? string.Empty;
            info.FileVersion = root.ChildText("version") ?? string.Empty;
            info.Generator = root.ChildText("generator") ?? string.Empty;
            info.Description = root.ChildText("descr") ?? string.Empty;
            info.Tags = root.ChildText("tags") ?? string.Empty;
            var attr = root.Child("attr");
            if (attr != null) info.Attributes.AddRange(attr.Atoms());

            var fp = new Footprint { Id = name, Description = info.Description };
            double fpAngle = root.Child("at")?.Number(3, 0.0) ?? 0.0;
            bool flipped = (root.ChildText("layer") ?? "F.Cu").StartsWith("B.", StringComparison.Ordinal);
            if (flipped) info.Warnings.Add("footprint B.Cu katmanında (bir karttan kopyalanmış); kütüphanedeki üst yüz biçimine çevrildi.");

            // ---- pads
            foreach (var padNode in root.Children("pad"))
            {
                var pad = ReadPad(padNode, fpAngle, flipped, info);
                if (pad != null) fp.Pads.Add(pad);
            }

            // ---- graphics: silkscreen and courtyard
            var court = new Bounds();
            var courtBack = new Bounds();
            foreach (var g in root.Items)
            {
                if (!g.IsList || !g.Head.StartsWith("fp_", StringComparison.Ordinal) || g.Head == "fp_text") continue;
                string layer = g.ChildText("layer") ?? string.Empty;
                if (flipped) layer = SwapSide(layer);
                bool silk = layer == "F.SilkS" || layer == "F.Silkscreen";
                bool crt = layer == "F.CrtYd" || layer == "F.Courtyard";
                bool crtB = layer == "B.CrtYd" || layer == "B.Courtyard";
                if (!silk && !crt && !crtB) continue;

                var geo = ReadGraphic(g, info.Warnings);
                if (geo == null) continue;
                if (flipped) geo.MirrorY();
                if (silk)
                {
                    foreach (var line in geo.Polylines)
                    {
                        for (int i = 1; i < line.Count; i++) fp.Silkscreen.Add(new Segment2(line[i - 1].X, line[i - 1].Y, line[i].X, line[i].Y));
                    }

                    foreach (var c in geo.Circles) fp.SilkCircles.Add(c);
                }

                geo.AddTo(crt ? court : crtB ? courtBack : null);
            }

            var cy = !court.IsEmpty ? court : courtBack;
            if (!cy.IsEmpty)
            {
                info.CourtyardFromFile = true;
                if (court.IsEmpty) info.Warnings.Add("F.CrtYd çizimi yok; courtyard B.CrtYd'den alındı.");
                fp.CourtyardMinX = cy.MinX;
                fp.CourtyardMinY = cy.MinY;
                fp.CourtyardMaxX = cy.MaxX;
                fp.CourtyardMaxY = cy.MaxY;
            }
            else
            {
                var pads = new Bounds();
                foreach (var p in fp.Pads)
                {
                    pads.Add(p.X - p.Width / 2, p.Y - p.Height / 2);
                    pads.Add(p.X + p.Width / 2, p.Y + p.Height / 2);
                }

                if (pads.IsEmpty)
                {
                    foreach (var s in fp.Silkscreen)
                    {
                        pads.Add(s.X1, s.Y1);
                        pads.Add(s.X2, s.Y2);
                    }

                    foreach (var (x, y, r) in fp.SilkCircles) pads.AddCircle(x, y, r);
                }

                if (pads.IsEmpty) pads.AddCircle(0, 0, 0.25);
                const double Margin = 0.25;
                fp.CourtyardMinX = pads.MinX - Margin;
                fp.CourtyardMinY = pads.MinY - Margin;
                fp.CourtyardMaxX = pads.MaxX + Margin;
                fp.CourtyardMaxY = pads.MaxY + Margin;
                info.Warnings.Add("courtyard çizimi yok; ped sınırı + 0.25 mm kullanıldı.");
            }

            // ---- 3D models (metadata only)
            foreach (var m in root.Children("model"))
            {
                var model = new KiCadModelRef { Path = m.Text(1) ?? string.Empty, Hidden = m.Flag("hide") };
                var off = (m.Child("offset") ?? m.Child("at"))?.Child("xyz");
                if (off != null)
                {
                    model.OffsetX = off.Number(1);
                    model.OffsetY = off.Number(2);
                    model.OffsetZ = off.Number(3);
                }

                var scale = m.Child("scale")?.Child("xyz");
                if (scale != null)
                {
                    model.ScaleX = scale.Number(1, 1);
                    model.ScaleY = scale.Number(2, 1);
                    model.ScaleZ = scale.Number(3, 1);
                }

                var rot = m.Child("rotate")?.Child("xyz");
                if (rot != null)
                {
                    model.RotateX = rot.Number(1);
                    model.RotateY = rot.Number(2);
                    model.RotateZ = rot.Number(3);
                }

                model.Opacity = m.Child("opacity")?.Number(1, 1) ?? 1;
                info.Models.Add(model);
            }

            fp.Height = EstimateHeight(fp, info);
            return fp;
        }

        private static Pad? ReadPad(SExpression node, double fpAngle, bool flipped, KiCadFootprintInfo info)
        {
            string name = node.Text(1) ?? string.Empty;
            string type = (node.Text(2) ?? string.Empty).ToLowerInvariant();
            string shape = (node.Text(3) ?? string.Empty).ToLowerInvariant();
            string label = name.Length > 0 ? "pad " + name : "unnamed pad";

            var at = node.Child("at");
            double x = at?.Number(1) ?? 0.0, y = at?.Number(2) ?? 0.0;
            double angle = (at?.Number(3, 0.0) ?? 0.0) - fpAngle;
            var size = node.Child("size");
            double w = size?.Number(1, 0.0) ?? 0.0;
            double h = size?.Number(2, w) ?? w;

            var layers = node.Child("layers")?.Atoms().ToList() ?? new List<string>();
            if (flipped) layers = layers.Select(SwapSide).ToList();
            bool anyCu = layers.Any(l => l == "*.Cu" || l == "F&B.Cu");
            bool front = anyCu || layers.Contains("F.Cu");
            bool back = anyCu || layers.Contains("B.Cu");

            var drillNode = node.Child("drill");
            double drill = 0.0;
            if (drillNode != null)
            {
                var nums = drillNode.Numbers();
                if (nums.Count > 0) drill = nums.Max();   // oval slots: the larger dimension
                var offset = drillNode.Child("offset");
                if (offset != null && (Math.Abs(offset.Number(1)) > 1e-9 || Math.Abs(offset.Number(2)) > 1e-9))
                {
                    info.Warnings.Add($"{label}: delik kaçıklığı yok sayıldı (bakır ve delik aynı merkezde).");
                }
            }

            if (w <= 0 && h <= 0)
            {
                w = h = drill > 0 ? drill * 1.6 : 1.0;
                info.Warnings.Add($"{label}: boyut yok; {w.ToString("0.###", CultureInfo.InvariantCulture)} mm varsayıldı.");
            }

            PadShape ps;
            double ox = 0.0, oy = 0.0;
            switch (shape)
            {
                case "circle":
                    ps = PadShape.Circle;
                    h = w;
                    break;
                case "rect":
                    ps = PadShape.Rect;
                    break;
                case "oval":
                    ps = PadShape.Oval;
                    break;
                case "roundrect":
                    ps = PadShape.RoundRect;
                    break;
                case "trapezoid":
                {
                    // KiCad: corner = ±half-size ± delta/2, delta.x widening along Y, delta.y along X.
                    var delta = node.Child("rect_delta");
                    double dx = delta?.Number(1) ?? 0.0, dy = delta?.Number(2) ?? 0.0;
                    w += Math.Abs(dy);
                    h += Math.Abs(dx);
                    ps = PadShape.Rect;
                    info.Warnings.Add($"{label}: yamuk ped sınırlayan dikdörtgen olarak alındı.");
                    break;
                }

                case "custom":
                {
                    var b = new Bounds();
                    string anchor = node.Child("options")?.ChildText("anchor") ?? "circle";
                    if (anchor == "circle") b.AddCircle(0, 0, w / 2);
                    else
                    {
                        b.Add(-w / 2, -h / 2);
                        b.Add(w / 2, h / 2);
                    }

                    var prims = node.Child("primitives");
                    if (prims != null)
                    {
                        foreach (var g in prims.Items)
                        {
                            if (!g.IsList) continue;
                            var geo = ReadGraphic(g, info.Warnings);
                            geo?.AddTo(b, inflate: true);
                        }
                    }

                    w = b.MaxX - b.MinX;
                    h = b.MaxY - b.MinY;
                    ox = (b.MinX + b.MaxX) / 2;
                    oy = (b.MinY + b.MaxY) / 2;
                    ps = PadShape.Rect;
                    info.Warnings.Add($"{label}: özel şekilli ped sınırlayan dikdörtgen olarak alındı.");
                    break;
                }

                default:
                    ps = PadShape.Rect;
                    info.Warnings.Add($"{label}: bilinmeyen şekil '{shape}', dikdörtgen olarak alındı.");
                    break;
            }

            // Pad rotation. KiCad's RotatePoint(p, a) maps (x, y) to (x cos a + y sin a, -x sin a + y cos a).
            double a = ((angle % 360.0) + 360.0) % 360.0;
            double turns = Math.Round(a / 90.0);
            double cos, sin;
            if (Math.Abs(a - turns * 90.0) < 1e-6)
            {
                int q = (int)turns % 4;
                cos = q == 0 ? 1 : q == 2 ? -1 : 0;
                sin = q == 1 ? 1 : q == 3 ? -1 : 0;
                if (q % 2 == 1)
                {
                    double t = w;
                    w = h;
                    h = t;
                }
            }
            else
            {
                double r = a * Math.PI / 180.0;
                cos = Math.Cos(r);
                sin = Math.Sin(r);
                if (ps != PadShape.Circle)
                {
                    double bw = Math.Abs(w * cos) + Math.Abs(h * sin), bh = Math.Abs(w * sin) + Math.Abs(h * cos);
                    w = bw;
                    h = bh;
                    ps = PadShape.Rect;
                    info.Warnings.Add($"{label}: {a.ToString("0.##", CultureInfo.InvariantCulture)}° döndürülmüş, sınırlayan dikdörtgen olarak alındı.");
                }
            }

            x += ox * cos + oy * sin;
            y += -ox * sin + oy * cos;
            if (flipped) y = -y;

            var pad = new Pad { Name = name, X = Clean(x), Y = Clean(y), Width = w, Height = h, Shape = ps };
            switch (type)
            {
                case "thru_hole":
                    if (drill <= 0)
                    {
                        drill = Math.Min(w, h) * 0.5;
                        info.Warnings.Add($"{label}: deliksiz THT ped; {drill.ToString("0.###", CultureInfo.InvariantCulture)} mm delik varsayıldı.");
                    }

                    pad.Drill = drill;
                    break;

                case "np_thru_hole":
                    if (drill <= 0) drill = Math.Min(w, h);
                    pad.Drill = drill;
                    pad.Width = Math.Max(pad.Width, drill);
                    pad.Height = Math.Max(pad.Height, drill);
                    info.NonPlatedHoles++;
                    break;

                case "smd":
                case "connect":
                    if (!front && !back)
                    {
                        info.Warnings.Add($"{label}: bakır katmanı yok ({string.Join(" ", layers)}); atlandı.");
                        return null;
                    }

                    pad.Layer = front ? CopperLayer.Top : CopperLayer.Bottom;
                    break;

                default:
                    info.Warnings.Add($"{label}: bilinmeyen ped türü '{type}'.");
                    if (drill > 0) pad.Drill = drill;
                    else if (!front && back) pad.Layer = CopperLayer.Bottom;
                    break;
            }

            return pad;
        }

        private static double Clean(double v) => Math.Abs(v) < 1e-12 ? 0.0 : Math.Round(v, 9);

        private static string SwapSide(string layer) =>
            layer.StartsWith("F.", StringComparison.Ordinal) ? "B." + layer.Substring(2)
            : layer.StartsWith("B.", StringComparison.Ordinal) ? "F." + layer.Substring(2)
            : layer;

        /// <summary>Height from a "_H5.0mm"-style dimension in the name/model or from the package family.</summary>
        private static double EstimateHeight(Footprint fp, KiCadFootprintInfo info)
        {
            string modelNames = string.Join(" ", info.Models.Select(m => System.IO.Path.GetFileNameWithoutExtension(m.Path.Replace('\\', '/').Split('/').Last())));
            string text = info.Name + " " + modelNames;
            var mh = Regex.Match(text, @"(?:^|[_\-\s])H(\d+(?:\.\d+)?)mm", RegexOptions.IgnoreCase);
            if (mh.Success) return Num(mh.Groups[1].Value);

            string n = info.Name.ToUpperInvariant();
            bool allSmd = fp.Pads.Count > 0 && fp.Pads.All(p => !p.IsThroughHole);
            if (n.StartsWith("MOUNTINGHOLE", StringComparison.Ordinal) || info.Attributes.Contains("virtual")) return 0.0;

            var chip = Regex.Match(n, @"_(0201|0402|0603|0805|1206|1210|1812|2010|2512)_");
            if (chip.Success)
            {
                switch (chip.Groups[1].Value)
                {
                    case "0201": return 0.3;
                    case "0402": return 0.35;
                    case "0603": return 0.45;
                    default: return 0.6;
                }
            }

            var dia = Regex.Match(n, @"_D(\d+(?:\.\d+)?)MM");
            if (n.StartsWith("LED_D", StringComparison.Ordinal) && dia.Success) return Num(dia.Groups[1].Value) * 1.7;
            if (n.StartsWith("CP_RADIAL", StringComparison.Ordinal) && dia.Success) return Num(dia.Groups[1].Value) * 1.6;

            (string Key, double Height)[] table =
            {
                ("SOT-223", 1.8), ("SOT-23", 1.1), ("SOT-323", 1.0), ("SC-70", 1.0), ("SOT-89", 1.6), ("TO-252", 2.4),
                ("TO-263", 4.6), ("SOIC", 1.75), ("SO-", 1.75), ("TSSOP", 1.2), ("MSOP", 1.1), ("SSOP", 2.0), ("QFN", 0.9),
                ("DFN", 0.9), ("TQFP", 1.2), ("LQFP", 1.6), ("QFP", 2.5), ("BGA", 1.5), ("DIP", 4.5), ("TO-92", 5.0),
                ("TO-220", 15.0), ("TO-126", 10.0), ("TO-247", 20.0), ("PINHEADER", 8.5), ("PINSOCKET", 8.5),
                ("JST_XH", 7.0), ("JST_PH", 6.0), ("JST", 6.0), ("TERMINALBLOCK", 10.0), ("C_DISC", 5.0),
                ("C_RADIAL", 7.0), ("D_DO-35", 1.8), ("D_DO-41", 2.7), ("R_AXIAL", 2.5), ("BUZZER", 9.5), ("RELAY", 15.5)
            };
            foreach (var (key, height) in table)
            {
                if (n.Contains(key)) return height;
            }

            return allSmd ? 1.5 : 3.0;
        }

        private static double Num(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ graphics

        private sealed class Bounds
        {
            public double MinX = double.PositiveInfinity, MinY = double.PositiveInfinity;
            public double MaxX = double.NegativeInfinity, MaxY = double.NegativeInfinity;

            public bool IsEmpty => double.IsInfinity(MinX);

            public void Add(double x, double y)
            {
                MinX = Math.Min(MinX, x);
                MinY = Math.Min(MinY, y);
                MaxX = Math.Max(MaxX, x);
                MaxY = Math.Max(MaxY, y);
            }

            public void AddCircle(double x, double y, double r)
            {
                Add(x - r, y - r);
                Add(x + r, y + r);
            }
        }

        /// <summary>A graphic item flattened to polylines (arcs tessellated) and circles.</summary>
        private sealed class Geometry
        {
            public readonly List<List<(double X, double Y)>> Polylines = new List<List<(double X, double Y)>>();
            public readonly List<(double X, double Y, double R)> Circles = new List<(double X, double Y, double R)>();

            /// <summary>Exact arc extremes for bounding boxes (tessellation can cut the bulge).</summary>
            public readonly List<(double X, double Y)> Extremes = new List<(double X, double Y)>();

            public double Width;

            public void MirrorY()
            {
                foreach (var line in Polylines)
                {
                    for (int i = 0; i < line.Count; i++) line[i] = (line[i].X, -line[i].Y);
                }

                for (int i = 0; i < Circles.Count; i++) Circles[i] = (Circles[i].X, -Circles[i].Y, Circles[i].R);
                for (int i = 0; i < Extremes.Count; i++) Extremes[i] = (Extremes[i].X, -Extremes[i].Y);
            }

            public void AddTo(Bounds? b, bool inflate = false)
            {
                if (b == null) return;
                double e = inflate ? Width / 2 : 0.0;
                foreach (var line in Polylines)
                {
                    foreach (var (x, y) in line) b.AddCircle(x, y, e);
                }

                foreach (var (x, y) in Extremes) b.AddCircle(x, y, e);
                foreach (var (x, y, r) in Circles) b.AddCircle(x, y, r + e);
            }
        }

        private static (double X, double Y)? Pt(SExpression? node) =>
            node == null ? ((double, double)?)null : (node.Number(1), node.Number(2));

        /// <summary>fp_/gr_ line, rect, circle, arc, poly, curve (KiCad 5–8 spellings).</summary>
        private static Geometry? ReadGraphic(SExpression g, List<string> warnings)
        {
            string head = g.Head;
            int us = head.IndexOf('_');
            string kind = us >= 0 ? head.Substring(us + 1) : head;
            var geo = new Geometry
            {
                Width = g.Child("stroke")?.Child("width")?.Number(1) ?? g.Child("width")?.Number(1) ?? 0.0
            };

            switch (kind)
            {
                case "line":
                {
                    var a = Pt(g.Child("start"));
                    var b = Pt(g.Child("end"));
                    if (a == null || b == null) return null;
                    geo.Polylines.Add(new List<(double X, double Y)> { a.Value, b.Value });
                    return geo;
                }

                case "rect":
                {
                    var a = Pt(g.Child("start"));
                    var b = Pt(g.Child("end"));
                    if (a == null || b == null) return null;
                    var (x1, y1) = a.Value;
                    var (x2, y2) = b.Value;
                    geo.Polylines.Add(new List<(double X, double Y)> { (x1, y1), (x2, y1), (x2, y2), (x1, y2), (x1, y1) });
                    return geo;
                }

                case "circle":
                {
                    var c = Pt(g.Child("center"));
                    if (c == null) return null;
                    double r;
                    var radius = g.Child("radius");
                    if (radius != null) r = radius.Number(1);
                    else
                    {
                        var e = Pt(g.Child("end"));
                        if (e == null) return null;
                        r = Math.Sqrt(Sq(e.Value.X - c.Value.X) + Sq(e.Value.Y - c.Value.Y));
                    }

                    geo.Circles.Add((c.Value.X, c.Value.Y, r));
                    return geo;
                }

                case "arc":
                {
                    var arc = ReadArc(g);
                    if (arc == null) return null;
                    geo.Polylines.Add(arc.Value.Points);
                    geo.Extremes.AddRange(arc.Value.Extremes);
                    return geo;
                }

                case "poly":
                {
                    var pts = ReadPts(g.Child("pts"));
                    if (pts.Count < 2) return null;
                    if (pts[0] != pts[pts.Count - 1]) pts.Add(pts[0]);
                    geo.Polylines.Add(pts);
                    return geo;
                }

                case "curve":
                case "bezier":
                {
                    var pts = ReadPts(g.Child("pts"));
                    if (pts.Count != 4)
                    {
                        if (pts.Count >= 2) geo.Polylines.Add(pts);
                        return pts.Count >= 2 ? geo : null;
                    }

                    var curve = new List<(double X, double Y)>();
                    const int Steps = 12;
                    for (int i = 0; i <= Steps; i++)
                    {
                        double t = (double)i / Steps, u = 1 - t;
                        double bx = u * u * u * pts[0].X + 3 * u * u * t * pts[1].X + 3 * u * t * t * pts[2].X + t * t * t * pts[3].X;
                        double by = u * u * u * pts[0].Y + 3 * u * u * t * pts[1].Y + 3 * u * t * t * pts[2].Y + t * t * t * pts[3].Y;
                        curve.Add((bx, by));
                    }

                    geo.Polylines.Add(curve);
                    return geo;
                }

                case "text":
                case "text_box":
                case "textbox":
                case "bbox":
                    return null;

                default:
                    warnings.Add($"'{head}' çizimi anlaşılamadı; yok sayıldı.");
                    return null;
            }
        }

        private static double Sq(double v) => v * v;

        /// <summary>Points of a (pts (xy ..) (arc (start)(mid)(end)) ...) list.</summary>
        private static List<(double X, double Y)> ReadPts(SExpression? pts)
        {
            var list = new List<(double X, double Y)>();
            if (pts == null) return list;
            foreach (var item in pts.Items)
            {
                if (!item.IsList) continue;
                if (item.Head == "xy") list.Add((item.Number(1), item.Number(2)));
                else if (item.Head == "arc")
                {
                    var arc = ReadArc(item);
                    if (arc != null) list.AddRange(arc.Value.Points);
                }
            }

            return list;
        }

        private struct ArcResult
        {
            public List<(double X, double Y)> Points;
            public List<(double X, double Y)> Extremes;
        }

        /// <summary>
        /// KiCad 6+ arcs are (start)(mid)(end) on the circle. KiCad 5 arcs are
        /// (start = centre)(end = first point)(angle degrees), drawn clockwise on screen for a
        /// positive angle, i.e. with increasing atan2 angle in the Y-down frame.
        /// </summary>
        private static ArcResult? ReadArc(SExpression g)
        {
            var s = Pt(g.Child("start"));
            var e = Pt(g.Child("end"));
            var m = Pt(g.Child("mid"));
            double cx, cy, r, t0, sweep;
            if (s != null && m != null && e != null)
            {
                double ax = s.Value.X, ay = s.Value.Y, bx = m.Value.X, by = m.Value.Y, ex = e.Value.X, ey = e.Value.Y;
                double d = 2 * (ax * (by - ey) + bx * (ey - ay) + ex * (ay - by));
                if (Math.Abs(d) < 1e-12)
                {
                    return new ArcResult
                    {
                        Points = new List<(double X, double Y)> { s.Value, e.Value },
                        Extremes = new List<(double X, double Y)>()
                    };
                }

                double a2 = ax * ax + ay * ay, b2 = bx * bx + by * by, e2 = ex * ex + ey * ey;
                cx = (a2 * (by - ey) + b2 * (ey - ay) + e2 * (ay - by)) / d;
                cy = (a2 * (ex - bx) + b2 * (ax - ex) + e2 * (bx - ax)) / d;
                r = Math.Sqrt(Sq(ax - cx) + Sq(ay - cy));
                t0 = Math.Atan2(ay - cy, ax - cx);
                double tm = Math.Atan2(by - cy, bx - cx), t1 = Math.Atan2(ey - cy, ex - cx);
                double s1 = Norm2Pi(t1 - t0), sm = Norm2Pi(tm - t0);
                if (s1 < 1e-12) s1 = 2 * Math.PI;   // start == end: full circle
                sweep = sm < s1 ? s1 : s1 - 2 * Math.PI;
            }
            else if (s != null && e != null && g.Child("angle") != null)
            {
                cx = s.Value.X;
                cy = s.Value.Y;
                r = Math.Sqrt(Sq(e.Value.X - cx) + Sq(e.Value.Y - cy));
                t0 = Math.Atan2(e.Value.Y - cy, e.Value.X - cx);
                sweep = g.Child("angle")!.Number(1) * Math.PI / 180.0;
            }
            else
            {
                return null;
            }

            int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / (15.0 * Math.PI / 180.0)));
            var pts = new List<(double X, double Y)>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                double t = t0 + sweep * i / steps;
                pts.Add((cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
            }

            var extremes = new List<(double X, double Y)>();
            for (int k = -8; k <= 8; k++)
            {
                double t = k * Math.PI / 2;
                double rel = sweep >= 0 ? t - t0 : t0 - t;
                if (rel >= -1e-12 && rel <= Math.Abs(sweep) + 1e-12) extremes.Add((cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
            }

            return new ArcResult { Points = pts, Extremes = extremes };
        }

        private static double Norm2Pi(double a)
        {
            double twoPi = 2 * Math.PI;
            a %= twoPi;
            return a < 0 ? a + twoPi : a;
        }

        // ================================================================== symbols

        /// <summary>Parses a <c>.kicad_sym</c> (KiCad 6–8) and resolves <c>extends</c>.</summary>
        public static List<KiCadSymbol> ImportSymbolLibrary(string kicadSymText) => ImportSymbolLibrary(kicadSymText, null);

        /// <summary>As <see cref="ImportSymbolLibrary(string)"/>, naming the library (used for ids and footprint pairing).</summary>
        public static List<KiCadSymbol> ImportSymbolLibrary(string kicadSymText, string? libraryName)
        {
            if (kicadSymText == null) throw new ArgumentNullException(nameof(kicadSymText));
            var root = SExpression.Parse(kicadSymText);
            IEnumerable<SExpression> nodes;
            switch (root.Head)
            {
                case "kicad_symbol_lib":
                    nodes = root.Children("symbol");
                    break;
                case "kicad_sch":
                    nodes = root.Child("lib_symbols")?.Children("symbol") ?? Enumerable.Empty<SExpression>();
                    break;
                case "symbol":
                    nodes = new[] { root };
                    break;
                default:
                    throw new FormatException($"KiCad sembol kütüphanesi değil: dosya '({root.Head} ...)' ile başlıyor, '(kicad_symbol_lib ...)' bekleniyor.");
            }

            var symbols = nodes.Select(n => ParseSymbol(n, libraryName)).ToList();
            var byName = new Dictionary<string, KiCadSymbol>(StringComparer.Ordinal);
            foreach (var s in symbols)
            {
                if (!byName.ContainsKey(s.Name)) byName[s.Name] = s;
            }

            var done = new HashSet<KiCadSymbol>();
            foreach (var s in symbols) ResolveExtends(s, byName, done, new HashSet<KiCadSymbol>());
            return symbols;
        }

        private static void ResolveExtends(KiCadSymbol s, Dictionary<string, KiCadSymbol> byName, HashSet<KiCadSymbol> done, HashSet<KiCadSymbol> visiting)
        {
            if (done.Contains(s)) return;
            if (string.IsNullOrEmpty(s.Extends))
            {
                done.Add(s);
                return;
            }

            if (!visiting.Add(s))
            {
                s.Warnings.Add($"'{s.Name}' sembolünde döngüsel 'extends' zinciri.");
                return;
            }

            if (!byName.TryGetValue(s.Extends!, out var parent))
            {
                s.Warnings.Add($"üst sembol '{s.Extends}' bu kütüphanede yok; bacaklar ve çizimler eksik.");
                done.Add(s);
                return;
            }

            ResolveExtends(parent, byName, done, visiting);
            if (s.Pins.Count == 0) s.Pins.AddRange(parent.Pins.Select(p => p.Clone()));
            s.UnitCount = Math.Max(s.UnitCount, parent.UnitCount);
            s.IsPower |= parent.IsPower;
            s.PinNamesHidden |= parent.PinNamesHidden;
            s.PinNumbersHidden |= parent.PinNumbersHidden;
            if (!s.HasBounds && parent.HasBounds)
            {
                s.MinX = parent.MinX;
                s.MinY = parent.MinY;
                s.MaxX = parent.MaxX;
                s.MaxY = parent.MaxY;
                s.HasBounds = true;
            }

            foreach (var kv in parent.Properties)
            {
                if (!s.Properties.ContainsKey(kv.Key)) s.Properties[kv.Key] = kv.Value;
            }

            done.Add(s);
        }

        private static readonly Regex UnitSuffix = new Regex(@"_(\d+)_(\d+)$", RegexOptions.CultureInvariant);

        private static KiCadSymbol ParseSymbol(SExpression node, string? libraryName)
        {
            SplitLibId(node.Text(1) ?? string.Empty, out string? lib, out string name);
            var sym = new KiCadSymbol
            {
                Name = name,
                Library = libraryName ?? lib ?? string.Empty,
                Extends = node.ChildText("extends"),
                IsPower = node.Flag("power"),
                InBom = !string.Equals(node.ChildText("in_bom"), "no", StringComparison.OrdinalIgnoreCase),
                OnBoard = !string.Equals(node.ChildText("on_board"), "no", StringComparison.OrdinalIgnoreCase),
                ExcludeFromSim = string.Equals(node.ChildText("exclude_from_sim"), "yes", StringComparison.OrdinalIgnoreCase),
                PinNumbersHidden = node.Child("pin_numbers")?.Flag("hide") ?? false,
                PinNamesHidden = node.Child("pin_names")?.Flag("hide") ?? false
            };

            foreach (var p in node.Children("property"))
            {
                string? key = p.Text(1);
                if (!string.IsNullOrEmpty(key)) sym.Properties[key!] = p.Text(2) ?? string.Empty;
            }

            var bounds = new Bounds();
            ReadSymbolBody(node, 0, 0, sym, bounds);
            int units = 1;
            foreach (var sub in node.Children("symbol"))
            {
                var m = UnitSuffix.Match(sub.Text(1) ?? string.Empty);
                int unit = m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                int style = m.Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
                units = Math.Max(units, unit);
                ReadSymbolBody(sub, unit, style, sym, bounds);
            }

            sym.UnitCount = units;
            if (!bounds.IsEmpty)
            {
                sym.MinX = bounds.MinX;
                sym.MinY = bounds.MinY;
                sym.MaxX = bounds.MaxX;
                sym.MaxY = bounds.MaxY;
                sym.HasBounds = true;
            }

            return sym;
        }

        private static void ReadSymbolBody(SExpression node, int unit, int style, KiCadSymbol sym, Bounds bounds)
        {
            bool measure = style <= 1;
            foreach (var item in node.Items)
            {
                if (!item.IsList) continue;
                switch (item.Head)
                {
                    case "pin":
                    {
                        var at = item.Child("at");
                        var pin = new KiCadPin
                        {
                            ElectricalType = item.Text(1) ?? "passive",
                            GraphicStyle = item.Text(2) != null && item.Items.Count > 2 && !item.Items[2].IsList ? item.Text(2)! : "line",
                            X = at?.Number(1) ?? 0.0,
                            Y = at?.Number(2) ?? 0.0,
                            Angle = at?.Number(3, 0.0) ?? 0.0,
                            Length = item.Child("length")?.Number(1) ?? 0.0,
                            Name = item.ChildText("name") ?? string.Empty,
                            Number = item.ChildText("number") ?? string.Empty,
                            Hidden = item.Flag("hide"),
                            Unit = unit,
                            BodyStyle = style
                        };
                        sym.Pins.Add(pin);
                        if (measure)
                        {
                            double r = pin.Angle * Math.PI / 180.0;
                            bounds.Add(pin.X, pin.Y);
                            bounds.Add(pin.X + pin.Length * Math.Cos(r), pin.Y + pin.Length * Math.Sin(r));
                        }

                        break;
                    }

                    case "rectangle":
                    case "circle":
                    case "arc":
                    case "polyline":
                    case "bezier":
                    {
                        if (!measure) break;
                        var renamed = SExpression.List(new[] { SExpression.Token(item.Head == "rectangle" ? "s_rect" : item.Head == "polyline" ? "s_poly" : "s_" + item.Head) }
                            .Concat(item.Items.Skip(1)).ToArray());
                        ReadGraphic(renamed, sym.Warnings)?.AddTo(bounds);
                        break;
                    }
                }
            }
        }

        /// <summary>Pin name without KiCad markup ("~" → "", "~{RESET}" → "RESET", "{slash}" → "/").</summary>
        public static string CleanPinName(string? name)
        {
            if (string.IsNullOrEmpty(name) || name == "~") return string.Empty;
            string s = name!.Replace("{slash}", "/").Replace("{dblquote}", "\"").Replace("{brl}", "{").Replace("{brr}", "}").Replace("{space}", " ");
            s = Regex.Replace(s, @"[~_^]\{([^}]*)\}", "$1");
            return s.Trim();
        }

        // ================================================================== mapping onto simulation models

        /// <summary>
        /// Builds a user part for <paramref name="symbol"/>: guesses the built-in simulation model,
        /// maps the symbol's pins onto the model's pins (<c>pinmap</c> parameter) and attaches the
        /// footprint (parameter <c>footprint</c> plus an embedded <see cref="CustomFootprint"/>
        /// so the part is self-contained in a .rwlib). Symbols without a fitting model become a
        /// generic N-pin connector part; returns null (with the reason in
        /// <paramref name="report"/>) when even that is impossible.
        /// </summary>
        public static CustomPart? ToCustomPart(KiCadSymbol symbol, Footprint? footprint, PartLibrary library, out string report) =>
            ToCustomPart(symbol, footprint, library, true, out report);

        /// <param name="embedFootprint">False: only reference the footprint id (the caller registers the footprint).</param>
        public static CustomPart? ToCustomPart(KiCadSymbol symbol, Footprint? footprint, PartLibrary library, bool embedFootprint, out string report)
        {
            var part = Map(symbol, footprint, library, embedFootprint, out var status, out var log);
            report = $"{symbol.FullName}: {status}\n" + string.Join("\n", log);
            return part;
        }

        private sealed class Words
        {
            public Words(params string?[] parts)
            {
                Text = string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p))).ToLowerInvariant();
                Tokens = new HashSet<string>(Regex.Split(Text, "[^a-z0-9]+").Where(t => t.Length > 0), StringComparer.Ordinal);
            }

            public string Text { get; }
            public HashSet<string> Tokens { get; }

            /// <summary>Whole-token match for plain words, substring match for phrases containing ' ' or '-'.</summary>
            public bool Any(params string[] words) =>
                words.Any(w => w.IndexOf(' ') >= 0 || w.IndexOf('-') >= 0 ? Text.Contains(w) : Tokens.Contains(w));
        }

        private enum Strategy
        {
            /// <summary>Two interchangeable terminals (A/B).</summary>
            Symmetric,

            /// <summary>Two terminals with polarity (A/K or +/-).</summary>
            Polar,

            /// <summary>Named pins, aliases and pin-type fallbacks.</summary>
            Named,

            /// <summary>Connector: header pin k = k-th symbol pin by number.</summary>
            Connector
        }

        private sealed class Candidate
        {
            public Candidate(string partId, string why)
            {
                PartId = partId;
                Why = why;
            }

            public string PartId { get; }
            public string Why { get; }
            public PartPreset? Preset { get; set; }
        }

        private static readonly int[] HeaderSizes = { 2, 3, 4, 5, 6, 8 };
        private static readonly int[] TerminalSizes = { 2, 3 };

        private static CustomPart? Map(KiCadSymbol symbol, Footprint? footprint, PartLibrary library, bool embed,
            out KiCadImportStatus status, out List<string> log)
        {
            if (symbol == null) throw new ArgumentNullException(nameof(symbol));
            if (library == null) throw new ArgumentNullException(nameof(library));
            log = new List<string>();
            foreach (var w in symbol.Warnings) log.Add("uyarı: " + w);

            if (symbol.IsPower)
            {
                status = KiCadImportStatus.Skipped;
                log.Add("güç sembolü (yalnızca bir neti adlandırır); yerine yerleşik toprak / besleme hattı sembollerini kullanın.");
                return null;
            }

            var pins = EffectivePins(symbol, log);
            if (pins.Count == 0)
            {
                status = KiCadImportStatus.Failed;
                log.Add("sembolün bacağı yok; bağlanacak bir şey yok.");
                return null;
            }

            string prefix = RefPrefix(symbol.Reference);
            var words = new Words(symbol.Name, symbol.Value, symbol.Keywords, symbol.Description);
            var candidates = Classify(symbol, prefix, words, pins);
            PreferPresetCandidate(candidates, library, symbol);

            foreach (var cand in candidates)
            {
                if (cand.PartId == "#connector") continue;
                var def = library.Find(cand.PartId);
                if (def == null) continue;
                var map = MapPins(def, StrategyFor(def.Id), pins, words, log, out var mapNotes);
                if (map == null) continue;

                log.Add($"model '{def.Id}' ({def.Name}): {cand.Why}.");
                log.AddRange(mapNotes);
                var parameters = BuildParameters(def, cand.Preset, symbol, words, log);
                parameters["pinmap"] = string.Join(",", def.Pins.Select(p => p.Name + ":" + map[p.Name].Number));
                log.Add("bacak eşlemesi " + parameters["pinmap"] + ".");
                status = KiCadImportStatus.Mapped;
                return Finish(symbol, def, parameters, footprint, map.Values.Select(p => p.Number), library, embed, log);
            }

            // Connector, or nothing electrical fits: a passive N-pin header-like part.
            bool isConnector = candidates.Any(c => c.PartId == "#connector");
            var numbers = pins.Select(p => p.Number).OrderBy(n => n, NaturalComparer.Instance).ToList();
            int count = numbers.Count;
            bool terminal = words.Any("screw", "terminal", "terminalblock", "klemens");
            string? genericId = terminal && TerminalSizes.Contains(count) ? "terminal-" + count.ToString(CultureInfo.InvariantCulture)
                : HeaderSizes.Contains(count) ? "header-" + count.ToString(CultureInfo.InvariantCulture)
                : null;
            var generic = genericId != null ? library.Find(genericId) : null;
            if (generic == null)
            {
                status = KiCadImportStatus.Failed;
                if (candidates.Count > 0 && !isConnector)
                {
                    log.Add("aday modellerin hiçbiri bacaklara uymadı (" + string.Join(", ", candidates.Select(c => c.PartId)) + ").");
                }

                log.Add($"uyan bir simülasyon modeli ve {count} bacaklı genel parça yok (genel parçalar "
                        + string.Join("/", HeaderSizes) + " bacaklı pin başlıkları ve " + string.Join("/", TerminalSizes) + " bacaklı klemensler için var); alınmadı.");
                return null;
            }

            if (isConnector) log.Add($"konnektör: '{generic.Id}' ({generic.Name}) parçasına eşlendi; konnektörlerin elektriksel modeli yoktur.");
            else
            {
                if (candidates.Count > 0) log.Add("aday modeller bacaklara uymadı: " + string.Join(", ", candidates.Where(c => c.PartId != "#connector").Select(c => c.PartId)) + ".");
                log.Add($"uyan simülasyon modeli yok: konnektör gibi pasif bir parça olarak alındı '{generic.Id}' (elektriksel davranışı yok, bacaklar yalnızca netleri taşır).");
            }

            var byNumber = pins.ToDictionary(p => p.Number, StringComparer.Ordinal);
            var pinmap = new List<string>();
            for (int i = 0; i < count; i++)
            {
                string modelPin = generic.Pins[i].Name;
                var kp = byNumber[numbers[i]];
                pinmap.Add(modelPin + ":" + kp.Number);
                if (!isConnector || kp.CleanName.Length > 0 && !Regex.IsMatch(kp.CleanName, @"^(Pin_?)?\d+$", RegexOptions.IgnoreCase))
                {
                    log.Add($"  bacak {modelPin} = KiCad bacağı {kp.Number}" + (kp.CleanName.Length > 0 ? " (" + kp.CleanName + ")" : string.Empty));
                }
            }

            var genericParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["pinmap"] = string.Join(",", pinmap) };
            log.Add("bacak eşlemesi " + genericParams["pinmap"] + ".");
            status = isConnector ? KiCadImportStatus.Mapped : KiCadImportStatus.Generic;
            return Finish(symbol, generic, genericParams, footprint, numbers, library, embed, log);
        }

        private static CustomPart Finish(KiCadSymbol symbol, PartDefinition def, Dictionary<string, string> parameters, Footprint? footprint,
            IEnumerable<string> padNumbers, PartLibrary library, bool embed, List<string> log)
        {
            var pads = padNumbers.ToList();
            if (footprint != null)
            {
                parameters["footprint"] = footprint.Id;
                var missing = pads.Where(n => footprint.FindPad(n) == null).ToList();
                if (missing.Count > 0) log.Add($"uyarı: '{footprint.Id}' footprint'inde {string.Join(", ", missing)} pedi yok.");
                log.Add($"footprint '{footprint.Id}' ({footprint.Pads.Count} ped).");
            }
            else
            {
                string fallback = def.ResolveFootprint(parameters);
                var fp = SafeGet(fallback);
                if (!string.IsNullOrEmpty(symbol.FootprintRef)) log.Add($"uyarı: '{symbol.FootprintRef}' footprint'i yok; modelin varsayılanı '{fallback}' kullanıldı.");
                else if (!string.IsNullOrEmpty(fallback)) log.Add($"footprint belirtilmemiş; modelin varsayılanı '{fallback}' kullanıldı.");
                if (fp != null)
                {
                    var missing = pads.Where(n => fp.FindPad(n) == null).ToList();
                    if (missing.Count > 0) log.Add($"uyarı: varsayılan '{fallback}' footprint'inde {string.Join(", ", missing)} pedi yok; PCB yerleşiminden önce bir footprint atayın.");
                }
            }

            string value = symbol.Value;
            bool valueDiffers = !string.IsNullOrWhiteSpace(value) && value != "~" && !string.Equals(value, symbol.Name, StringComparison.OrdinalIgnoreCase);
            string description = symbol.Description;
            var part = new CustomPart
            {
                Id = MakePartId(symbol),
                Name = valueDiffers ? symbol.Name + " (" + value + ")" : symbol.Name,
                BasePartId = def.Id,
                Category = string.IsNullOrEmpty(symbol.Library) ? "KiCad" : "KiCad · " + symbol.Library,
                Description = string.IsNullOrEmpty(description) ? "KiCad " + symbol.FullName : description,
                Datasheet = symbol.Datasheet == "~" ? string.Empty : symbol.Datasheet,
                Manufacturer = symbol.Property("Manufacturer") ?? symbol.Property("MF") ?? string.Empty,
                Author = "KiCad içe aktarımı",
                Parameters = parameters,
                Footprint = embed && footprint != null ? CustomFootprint.From(footprint) : null
            };
            return part;
        }

        private static Footprint? SafeGet(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            try
            {
                return FootprintLibrary.Get(id!);
            }
            catch (Exception ex) when (ex is FormatException || ex is IndexOutOfRangeException || ex is ArgumentException || ex is OverflowException)
            {
                return null;   // a name the parametric generator misreads ("DIP-8_W7.62mm")
            }
        }

        /// <summary>Unit 1 + common pins + units made only of power pins; NC pins and stacked duplicates dropped.</summary>
        private static List<KiCadPin> EffectivePins(KiCadSymbol s, List<string> log)
        {
            var styled = s.Pins.Where(p => p.BodyStyle <= 1).ToList();
            var powerUnits = new HashSet<int>();
            for (int u = 2; u <= s.UnitCount; u++)
            {
                var unitPins = styled.Where(p => p.Unit == u).ToList();
                if (unitPins.Count > 0 && unitPins.All(p => p.ElectricalType == "power_in" || p.ElectricalType == "power_out")) powerUnits.Add(u);
            }

            var result = new List<KiCadPin>();
            var numbers = new HashSet<string>(StringComparer.Ordinal);
            var dropped = new List<string>();
            foreach (var p in styled)
            {
                if (p.Unit != 0 && p.Unit != 1 && !powerUnits.Contains(p.Unit)) continue;
                if (p.ElectricalType == "no_connect")
                {
                    dropped.Add(p.Number);
                    continue;
                }

                if (!numbers.Add(p.Number)) continue;   // stacked pins / same pad in several units
                result.Add(p);
            }

            if (dropped.Count > 0) log.Add("bağlantısız (NC) bacaklar yok sayıldı: " + string.Join(", ", dropped) + ".");
            if (s.UnitCount > 1)
            {
                var ignored = Enumerable.Range(2, s.UnitCount - 1).Where(u => !powerUnits.Contains(u)).ToList();
                log.Add($"sembolde {s.UnitCount} birim var: birim 1" + (powerUnits.Count > 0 ? " ve güç birimi " + string.Join(", ", powerUnits) : string.Empty)
                        + " eşlendi" + (ignored.Count > 0 ? "; birim " + string.Join(", ", ignored) + " modellenmedi (onun için ayrı bir parça yerleştirin)" : string.Empty) + ".");
            }

            return result;
        }

        private static string RefPrefix(string reference)
        {
            var m = Regex.Match(reference ?? string.Empty, @"^[A-Za-z]+");
            return m.Success ? m.Value.ToUpperInvariant() : string.Empty;
        }

        private static string Norm(string s) => s.Replace(" ", string.Empty).Replace("_", string.Empty).ToUpperInvariant();

        private static List<Candidate> Classify(KiCadSymbol s, string prefix, Words w, List<KiCadPin> pins)
        {
            var c = new List<Candidate>();
            string name = s.Name.ToUpperInvariant();
            bool HasPins(params string[] names) => names.All(n => pins.Any(p => Norm(p.CleanName) == n));
            void Add(string id, string why) => c.Add(new Candidate(id, why));

            bool polarized = w.Any("polarized", "polarised", "electrolytic", "elko", "tantalum", "elec")
                             || Regex.IsMatch(name, @"^CP(\d|_|$)") || HasPins("+") || HasPins("-");

            switch (prefix)
            {
                case "R":
                case "RV":
                case "VR":
                case "POT":
                case "TH":
                case "RT":
                case "NTC":
                case "LDR":
                    if (prefix == "RV" || prefix == "VR" || prefix == "POT" || w.Any("potentiometer", "pot", "trimmer", "trimpot", "variable") || pins.Count == 3)
                        Add("potentiometer", $"'{prefix}' referansı / potansiyometre sözcükleri");
                    else if (prefix == "TH" || prefix == "RT" || prefix == "NTC" || w.Any("thermistor", "ntc"))
                        Add("ntc", "termistör");
                    else if (prefix == "LDR" || w.Any("ldr", "photoresistor", "photocell", "photo resistor", "light dependent"))
                        Add("ldr", "foto direnç");
                    else Add("resistor", "R referans öneki");
                    break;

                case "F":
                case "FU":
                case "FUSE":
                    Add("fuse", prefix + " referans öneki");
                    break;

                case "C":
                case "CP":
                case "CE":
                    if (prefix == "CP" || polarized) Add("electrolytic", "kutuplu kondansatör");
                    Add("capacitor", "C referans öneki");
                    break;

                case "L":
                    Add("inductor", "L referans öneki");
                    break;

                case "D":
                case "LED":
                case "DS":
                case "LD":
                case "CR":
                case "Z":
                case "ZD":
                    if (prefix == "LED" || w.Any("led") || name.StartsWith("LED", StringComparison.Ordinal)) Add("led", "LED sözcüğü");
                    else if (prefix == "Z" || prefix == "ZD" || w.Any("zener")) Add("zener", "zener sözcüğü");
                    else if (w.Any("schottky")) Add("schottky", "schottky sözcüğü");
                    else Add("diode", prefix + " referans öneki");
                    if (!c.Any(x => x.PartId == "diode")) Add("diode", "diyot (yedek seçenek)");
                    break;

                case "Q":
                case "T":
                case "TR":
                case "VT":
                    AddTransistor(c, w, name, HasPins);
                    break;

                case "J":
                case "P":
                case "CN":
                case "CON":
                case "CONN":
                case "X":
                case "XS":
                case "XP":
                case "JP":
                    Add("#connector", "konnektör");
                    break;

                case "SW":
                case "S":
                case "BTN":
                case "KEY":
                    if (w.Any("push", "button", "pushbutton", "tactile", "momentary", "tact")) Add("button", "buton");
                    Add("switch", prefix + " referans öneki");
                    break;

                case "BT":
                case "BAT":
                    Add("battery", prefix + " referans öneki");
                    break;

                case "M":
                case "MOT":
                    if (w.Any("servo")) Add("servo", "servo sözcüğü");
                    Add("dc-motor", prefix + " referans öneki");
                    break;

                case "K":
                case "RL":
                case "RLY":
                case "REL":
                    Add("relay", prefix + " referans öneki");
                    break;

                case "BZ":
                case "BUZ":
                    Add("buzzer", prefix + " referans öneki");
                    break;

                case "LA":
                case "HL":
                    if (w.Any("lamp", "bulb", "incandescent")) Add("lamp", "lamba sözcüğü");
                    break;

                case "V":
                case "VS":
                    if (w.Any("vdc", "dc", "source", "voltage")) Add("dc-source", "gerilim kaynağı");
                    break;
            }

            // Keyword-based guesses (ICs and anything with an unusual prefix).
            if (c.Count == 0 || prefix == "U" || prefix == "IC" || prefix == "A" || prefix == "AR" || prefix == "N")
            {
                if (w.Any("opamp", "op-amp", "op amp", "operational amplifier") || Regex.IsMatch(name, @"^(LM358|LM324|LM741|LM2904|TL0[678]\d|NE5532|MCP60\d\d|OPA\d|LMV3\d\d)"))
                    Add("opamp", "op-amp sözcükleri");
                if (w.Any("regulator", "ldo") || Regex.IsMatch(name, @"^(L?M?78[ML]?\d\d|AMS1117|LD1117|LM1117|MCP1700)"))
                    Add("regulator", "gerilim regülatörü sözcükleri");
                var gate = GateFunction(w, name);
                if (gate != null) Add(gate == "Not" || gate == "Buffer" ? "gate1" : "gate2", gate + " gate");
                if (c.Count == 0)
                {
                    if (w.Any("buzzer")) Add("buzzer", "buzzer sözcüğü");
                    if (w.Any("servo")) Add("servo", "servo sözcüğü");
                    if (w.Any("motor")) Add("dc-motor", "motor sözcüğü");
                    if (w.Any("relay")) Add("relay", "röle sözcüğü");
                    if (w.Any("battery")) Add("battery", "pil sözcüğü");
                    if (w.Any("connector", "header", "socket", "terminal")) Add("#connector", "konnektör sözcükleri");
                }
            }

            return c;
        }

        private static void AddTransistor(List<Candidate> c, Words w, string name, Func<string[], bool> hasPins)
        {
            void Add(string id, string why) => c.Add(new Candidate(id, why));
            if (w.Any("darlington")) Add("darlington", "darlington sözcüğü");
            bool npn = w.Any("npn"), pnp = w.Any("pnp");
            bool nmos = w.Any("nmos", "n-mos", "n-channel", "n-ch", "nfet", "n-fet", "n-mosfet");
            bool pmos = w.Any("pmos", "p-mos", "p-channel", "p-ch", "pfet", "p-fet", "p-mosfet");
            if (npn) Add("npn", "NPN sözcüğü");
            if (pnp) Add("pnp", "PNP sözcüğü");
            if (nmos) Add("nmos", "N kanal sözcüğü");
            if (pmos) Add("pmos", "P kanal sözcüğü");
            if (npn || pnp || nmos || pmos) return;
            if (w.Any("jfet", "igbt", "triac", "scr", "thyristor")) return;   // no model
            if (hasPins(new[] { "B", "C", "E" }))
            {
                Add("npn", "B/C/E bacakları, kutup belirtilmemiş: NPN varsayıldı");
                Add("pnp", "B/C/E bacakları");
            }
            else if (hasPins(new[] { "G", "D", "S" }))
            {
                Add("nmos", "G/D/S bacakları, kanal belirtilmemiş: N kanal varsayıldı");
                Add("pmos", "G/D/S bacakları");
            }
        }

        private static string? GateFunction(Words w, string name)
        {
            var m = Regex.Match(name, @"^74[A-Z]*(\d+)");
            if (m.Success)
            {
                switch (m.Groups[1].Value)
                {
                    case "00": case "132": return "Nand";
                    case "02": return "Nor";
                    case "08": return "And";
                    case "32": return "Or";
                    case "86": return "Xor";
                    case "266": return "Xnor";
                    case "04": case "14": case "05": case "06": return "Not";
                    case "07": case "125": case "126": case "244": case "17": return "Buffer";
                }
            }

            if (!w.Any("gate", "logic", "inverter", "buffer")) return null;
            if (w.Any("nand")) return "Nand";
            if (w.Any("xnor")) return "Xnor";
            if (w.Any("xor")) return "Xor";
            if (w.Any("nor")) return "Nor";
            if (w.Any("inverter", "not")) return "Not";
            if (w.Any("and")) return "And";
            if (w.Any("or")) return "Or";
            if (w.Any("buffer")) return "Buffer";
            return null;
        }

        private static string Alnum(string s) => Regex.Replace(s ?? string.Empty, "[^A-Za-z0-9]", string.Empty).ToUpperInvariant();

        /// <summary>A preset whose id or part number equals the symbol value/name moves its model to the front.</summary>
        private static void PreferPresetCandidate(List<Candidate> candidates, PartLibrary library, KiCadSymbol symbol)
        {
            var ids = new List<string>();
            foreach (var cand in candidates)
            {
                if (!ids.Contains(cand.PartId)) ids.Add(cand.PartId);
                if (cand.PartId == "diode")
                {
                    foreach (var extra in new[] { "schottky", "zener" })
                    {
                        if (!ids.Contains(extra)) ids.Add(extra);
                    }
                }
            }

            foreach (string text in new[] { symbol.Value, symbol.Name })
            {
                // Part numbers only ("LM358", "2N3904", "AMS1117-3.3"): generic names like "LED" or "R" never match.
                string key = Alnum(text);
                if (key.Length < 4 || !key.Any(char.IsDigit) || !key.Any(char.IsLetter)) continue;
                foreach (var preset in library.Presets)
                {
                    if (!ids.Contains(preset.PartId)) continue;
                    string firstToken = preset.Name.Split(new[] { ' ', '(' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                    if (Alnum(preset.Id) != key && Alnum(firstToken) != key) continue;
                    var existing = candidates.FirstOrDefault(x => x.PartId == preset.PartId);
                    var chosen = new Candidate(preset.PartId, (existing?.Why ?? "aynı aile") + $"; parça numarası '{preset.Id}' hazır ayarıyla eşleşiyor") { Preset = preset };
                    if (existing != null) candidates.Remove(existing);
                    candidates.Insert(0, chosen);
                    return;
                }
            }
        }

        private static Strategy StrategyFor(string partId)
        {
            switch (partId)
            {
                case "resistor":
                case "capacitor":
                case "inductor":
                case "fuse":
                case "ntc":
                case "ldr":
                case "switch":
                case "button":
                case "lamp":
                case "dc-motor":
                    return Strategy.Symmetric;
                case "electrolytic":
                case "battery":
                case "buzzer":
                case "dc-source":
                case "diode":
                case "schottky":
                case "zener":
                case "led":
                    return Strategy.Polar;
                default:
                    return partId.StartsWith("header-", StringComparison.Ordinal) || partId.StartsWith("terminal-", StringComparison.Ordinal)
                        ? Strategy.Connector
                        : Strategy.Named;
            }
        }

        /// <summary>Alternative KiCad pin names for a model pin ("#n" = pin number n).</summary>
        private static string[] Aliases(string partId, string pin)
        {
            switch (partId + "/" + pin)
            {
                case "dc-motor/A": return new[] { "+", "M+", "1" };
                case "dc-motor/B": return new[] { "-", "M-", "2" };
                case "potentiometer/A": return new[] { "1", "CW", "#1" };
                case "potentiometer/W": return new[] { "W", "WIPER", "S", "2", "#2" };
                case "potentiometer/B": return new[] { "3", "CCW", "#3" };
                case "servo/SIG": return new[] { "PWM", "SIGNAL", "S", "CTRL", "CONTROL", "IN", "PULSE" };
                case "servo/V+": return new[] { "+", "VCC", "VDD", "PWR", "V" };
                case "servo/GND": return new[] { "-", "V-", "VSS", "0V" };
                case "relay/COIL1": return new[] { "A1", "COIL+", "COIL_1", "+", "L1" };
                case "relay/COIL2": return new[] { "A2", "COIL-", "COIL_2", "-", "L2" };
                case "relay/COM": return new[] { "C", "11", "COMMON" };
                case "relay/NO": return new[] { "14", "NORMALLYOPEN" };
                case "relay/NC": return new[] { "12", "NORMALLYCLOSED" };
            }

            switch (pin)
            {
                case "A": return new[] { "ANODE", "+" };
                case "K": return new[] { "CATHODE", "KA", "-" };
                case "+": return new[] { "P", "POS", "PLUS", "V+", "VCC", "ANODE" };
                case "-": return new[] { "N", "NEG", "MINUS", "V-", "GND", "CATHODE" };
                case "B": return new[] { "BASE" };
                case "C": return new[] { "COLLECTOR" };
                case "E": return new[] { "EMITTER" };
                case "G": return new[] { "GATE" };
                case "D": return new[] { "DRAIN" };
                case "S": return new[] { "SOURCE" };
                case "IN+": return new[] { "+", "+IN", "INP", "NONINV", "VIN+", "NI", "IN+A", "+INA", "INA+" };
                case "IN-": return new[] { "-", "-IN", "INN", "INV", "VIN-", "IN-A", "-INA", "INA-" };
                case "OUT": return partId == "regulator" ? new[] { "VO", "VOUT", "OUTPUT", "OUT1" } : new[] { "VOUT", "O", "OUTPUT", "OUTA", "OUT1", "AOUT" };
                case "IN": return new[] { "VI", "VIN", "INPUT" };
                case "V+": return new[] { "VCC", "VDD", "VS+", "+VS", "VS", "VPOS", "+V", "V+A" };
                case "V-": return new[] { "VEE", "VSS", "GND", "VS-", "-VS", "VNEG", "-V" };
                case "GND": return new[] { "VSS", "COM", "0V", "VEE", "GND/ADJ", "-" };
                case "VCC": return new[] { "VDD", "V+", "+5V", "VS" };
                case "Y": return new[] { "OUT", "O", "Q" };
            }

            return Array.Empty<string>();
        }

        private static Dictionary<string, KiCadPin>? MapPins(PartDefinition def, Strategy strategy, List<KiCadPin> pins, Words words,
            List<string> rejectLog, out List<string> notes)
        {
            notes = new List<string>();
            if ((strategy == Strategy.Symmetric || strategy == Strategy.Polar) && (pins.Count != 2 || def.Pins.Count != 2))
            {
                return null;
            }

            var map = new Dictionary<string, KiCadPin>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<KiCadPin>();
            void Take(string modelPin, KiCadPin pin)
            {
                map[modelPin] = pin;
                used.Add(pin);
            }

            // 1. exact names
            foreach (var mp in def.Pins)
            {
                var hit = pins.FirstOrDefault(p => !used.Contains(p) && p.CleanName.Length > 0 && Norm(p.CleanName) == Norm(mp.Name));
                if (hit != null) Take(mp.Name, hit);
            }

            // 2. aliases
            foreach (var mp in def.Pins)
            {
                if (map.ContainsKey(mp.Name)) continue;
                foreach (string alias in Aliases(def.Id, mp.Name))
                {
                    KiCadPin? hit = alias.StartsWith("#", StringComparison.Ordinal)
                        ? pins.FirstOrDefault(p => !used.Contains(p) && p.Number == alias.Substring(1))
                        : pins.FirstOrDefault(p => !used.Contains(p) && p.CleanName.Length > 0 && Norm(p.CleanName) == Norm(alias));
                    if (hit == null) continue;
                    Take(mp.Name, hit);
                    break;
                }
            }

            // 3. fallbacks
            var free = pins.Where(p => !used.Contains(p)).OrderBy(p => p.Number, NaturalComparer.Instance).ToList();
            var open = def.Pins.Where(p => !map.ContainsKey(p.Name)).ToList();
            if (open.Count > 0)
            {
                switch (strategy)
                {
                    case Strategy.Symmetric:
                        for (int i = 0; i < open.Count && i < free.Count; i++) Take(open[i].Name, free[i]);
                        if (open.Count == 2) notes.Add("bacaklar numarayla atandı (uçlar yer değiştirebilir).");
                        break;

                    case Strategy.Polar:
                    {
                        // KiCad conventions: diodes/LEDs pin 1 = K, 2 = A; polarized parts pin 1 = +.
                        bool diode = def.FindPin("K") != null;
                        var order = diode ? new[] { "K", "A" } : new[] { "+", "-" };
                        if (open.Count == 2)
                        {
                            Take(order[0], free[0]);
                            Take(order[1], free[1]);
                            notes.Add(diode
                                ? $"kutup KiCad numaralandırmasından: bacak {free[0].Number} = katot, bacak {free[1].Number} = anot."
                                : $"kutup KiCad numaralandırmasından: bacak {free[0].Number} = +, bacak {free[1].Number} = -.");
                        }
                        else if (open.Count == 1 && free.Count == 1)
                        {
                            Take(open[0].Name, free[0]);
                        }

                        break;
                    }

                    case Strategy.Named:
                        NamedFallbacks(def, free, open, Take, notes);
                        break;
                }
            }

            if (def.Pins.Any(p => !map.ContainsKey(p.Name)))
            {
                rejectLog.Add($"'{def.Id}' uymadı: model bacakları {string.Join(", ", def.Pins.Where(p => !map.ContainsKey(p.Name)).Select(p => p.Name))} sembolde karşılık bulamadı.");
                return null;
            }

            var leftovers = pins.Where(p => !used.Contains(p)).ToList();
            if (leftovers.Count > 0)
            {
                if (strategy != Strategy.Named || leftovers.Count > map.Count)
                {
                    rejectLog.Add($"'{def.Id}' uymadı: sembol bacakları {string.Join(", ", leftovers.Select(p => p.Number))} modelde karşılık bulamadı.");
                    return null;
                }

                foreach (var p in leftovers)
                {
                    var same = map.FirstOrDefault(kv => p.CleanName.Length > 0 && Norm(kv.Value.CleanName) == Norm(p.CleanName));
                    notes.Add(same.Value != null
                        ? $"bacak {p.Number} ({p.CleanName}) {same.Key} ile aynı (bacak {same.Value.Number}); her model bacağına tek ped eşlenir."
                        : $"bacak {p.Number}" + (p.CleanName.Length > 0 ? " (" + p.CleanName + ")" : string.Empty) + " modelde karşılık bulamadı; simülasyonda bağlantısız kaldı.");
                }
            }

            return map;
        }

        private static void NamedFallbacks(PartDefinition def, List<KiCadPin> free, List<PinDefinition> open, Action<string, KiCadPin> take, List<string> notes)
        {
            bool IsOut(KiCadPin p) => p.ElectricalType == "output" || p.ElectricalType == "tri_state" || p.ElectricalType == "open_collector" || p.ElectricalType == "power_out" && def.Id != "opamp";
            bool IsIn(KiCadPin p) => p.ElectricalType == "input";

            KiCadPin? Next(Func<KiCadPin, bool> filter)
            {
                var hit = free.FirstOrDefault(filter);
                if (hit != null) free.Remove(hit);
                return hit;
            }

            foreach (var mp in open.ToList())
            {
                KiCadPin? hit = null;
                switch (def.Id + "/" + mp.Name)
                {
                    case "opamp/OUT":
                    case "gate2/Y":
                    case "gate1/Y":
                        hit = Next(IsOut);
                        if (hit != null) notes.Add($"{mp.Name} = çıkış bacağı {hit.Number}.");
                        break;
                    case "gate2/A":
                    case "gate2/B":
                    case "gate1/A":
                        hit = Next(IsIn);
                        if (hit != null) notes.Add($"{mp.Name} = giriş bacağı {hit.Number} (girişler bacak sırasıyla).");
                        break;
                    case "regulator/IN":
                        hit = Next(p => p.ElectricalType == "power_in" && !Regex.IsMatch(p.CleanName, "GND|VSS|COM", RegexOptions.IgnoreCase));
                        break;
                    case "regulator/OUT":
                        hit = Next(p => p.ElectricalType == "power_out" || p.ElectricalType == "output");
                        break;
                    case "potentiometer/A":
                    case "potentiometer/W":
                    case "potentiometer/B":
                        hit = Next(_ => true);
                        if (hit != null) notes.Add($"{mp.Name} = bacak {hit.Number} (numarayla).");
                        break;
                }

                if (hit != null)
                {
                    take(mp.Name, hit);
                    open.Remove(mp);
                }
            }
        }

        private static readonly (string Color, string[] Words, double Vf)[] LedColors =
        {
            ("red", new[] { "red", "rot", "kirmizi" }, 2.0), ("orange", new[] { "orange", "amber" }, 2.05), ("yellow", new[] { "yellow", "gelb" }, 2.1),
            ("green", new[] { "green", "grun", "gruen" }, 2.2), ("blue", new[] { "blue", "blau" }, 3.1), ("white", new[] { "white", "weiss", "warmwhite", "coolwhite" }, 3.1),
            ("uv", new[] { "uv", "ultraviolet" }, 3.3), ("ir", new[] { "ir", "infrared", "infra-red" }, 1.25)
        };

        private static Dictionary<string, string> BuildParameters(PartDefinition def, PartPreset? preset, KiCadSymbol symbol, Words words, List<string> log)
        {
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (preset != null)
            {
                foreach (var kv in preset.Parameters)
                {
                    if (kv.Key == "footprint" || kv.Key == "pinmap" || kv.Key == "pinout") continue;
                    parameters[kv.Key] = kv.Value;
                }

                log.Add($"parametreler '{preset.Id}' ({preset.Name}) hazır ayarından alındı.");
            }

            string value = (symbol.Value ?? string.Empty).Trim();
            bool placeholder = value.Length == 0 || value == "~" || string.Equals(value, symbol.Name, StringComparison.OrdinalIgnoreCase);

            void Numeric(string key)
            {
                if (TryParseValue(value, out string text))
                {
                    parameters[key] = text;
                    log.Add($"{key} = {text} (Value alanından).");
                }
                else
                {
                    log.Add($"Value '{value}' bir sayı değil; {key} modelin varsayılanında kaldı: {def.DefaultParameter(key)}.");
                }
            }

            switch (def.Id)
            {
                case "resistor":
                case "potentiometer":
                case "ntc":
                case "ldr":
                    Numeric("resistance");
                    break;
                case "capacitor":
                case "electrolytic":
                    Numeric("capacitance");
                    break;
                case "inductor":
                    Numeric("inductance");
                    break;
                case "fuse":
                    Numeric("rating");
                    break;
                case "dc-source":
                    Numeric("voltage");
                    break;
                case "battery":
                {
                    // [kicad-sch] "9V", "3.7V", "12 V", "3V7": the cells of a real chemistry that make it, or a cell pack
                    // with a regulated output when none does (a 5 V "battery" is a power bank).
                    var m = Regex.Match(value, @"(\d+)V(\d+)|(\d+(?:[.,]\d+)?)\s*V\b", RegexOptions.IgnoreCase);
                    if (preset != null || !m.Success) break;
                    double volts = m.Groups[1].Success
                        ? double.Parse(m.Groups[1].Value + "." + m.Groups[2].Value, CultureInfo.InvariantCulture)
                        : double.Parse(m.Groups[3].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                    var (chemistry, cells, regulated) = BatteryFor(volts);
                    parameters["chemistry"] = chemistry.ToString();
                    parameters["cells"] = cells.ToString(CultureInfo.InvariantCulture);
                    if (regulated) parameters["regulated"] = volts.ToString("R", CultureInfo.InvariantCulture);
                    log.Add(regulated
                        ? $"{volts:0.##} V hiçbir hücre sayısıyla tutmuyor: {cells} hücreli {chemistry} paket, çıkışı {volts:0.##} V'a düzenlenmiş (Value alanından)."
                        : $"{volts:0.##} V = {cells} × {chemistry} hücre (Value alanından).");
                    break;
                }

                case "led":
                {
                    var color = LedColors.FirstOrDefault(c => c.Words.Any(x => words.Tokens.Contains(x) || x.Contains('-') && words.Text.Contains(x)));
                    if (color.Color != null)
                    {
                        parameters["color"] = color.Color;
                        parameters["vf"] = color.Vf.ToString("R", CultureInfo.InvariantCulture);
                        log.Add($"LED rengi {color.Color}, Vf {parameters["vf"]} V.");
                    }
                    else
                    {
                        log.Add($"LED rengi belirtilmemiş; modelin varsayılanı {def.DefaultParameter("color")} (Vf {def.DefaultParameter("vf")} V).");
                    }

                    break;
                }

                case "zener":
                {
                    var m = Regex.Match(value, @"(\d+)V(\d+)|(\d+(?:\.\d+)?)V", RegexOptions.IgnoreCase);
                    if (preset == null && m.Success)
                    {
                        string v = m.Groups[1].Success ? m.Groups[1].Value + "." + m.Groups[2].Value : m.Groups[3].Value;
                        parameters["voltage"] = v;
                        log.Add($"zener gerilimi {v} V (Value alanından).");
                    }

                    break;
                }

                case "regulator":
                    if (preset == null)
                    {
                        string text = value + " " + symbol.Name;
                        var m78 = Regex.Match(text, @"78[ML]?(\d\d)");
                        var mdash = Regex.Match(text, @"-(\d+(?:\.\d+)?)(?:V|\b)");
                        var mv = Regex.Match(text, @"(\d+)V(\d+)");
                        string? vout = m78.Success ? int.Parse(m78.Groups[1].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
                            : mv.Success ? mv.Groups[1].Value + "." + mv.Groups[2].Value
                            : mdash.Success ? mdash.Groups[1].Value : null;
                        if (vout != null)
                        {
                            parameters["vout"] = vout;
                            log.Add($"çıkış gerilimi {vout} V (parça numarasından).");
                        }
                        else log.Add($"'{value}' içinde çıkış gerilimi bulunamadı; modelin varsayılanı {def.DefaultParameter("vout")} V.");
                    }

                    break;

                case "gate2":
                case "gate1":
                {
                    string? fn = GateFunction(words, symbol.Name.ToUpperInvariant()) ?? GateFunction(words, value.ToUpperInvariant());
                    if (fn != null) parameters["function"] = fn;
                    break;
                }
            }

            if (def.Parameters.Any(p => p.Key == "model") && !parameters.ContainsKey("model"))
            {
                parameters["model"] = placeholder ? symbol.Name : value;
            }

            return parameters;
        }

        /// <summary>The chemistry and cell count whose nominal voltage is <paramref name="volts"/> (within 2 %), most common first; else a Li-ion pack regulated down to it.</summary>
        private static (RobotTwin.CoreSim.Analog.Devices.BatteryChemistry Chemistry, int Cells, bool Regulated) BatteryFor(double volts)
        {
            var order = new (RobotTwin.CoreSim.Analog.Devices.BatteryChemistry Chemistry, int MaxCells)[]
            {
                (RobotTwin.CoreSim.Analog.Devices.BatteryChemistry.Alkaline, 8), (RobotTwin.CoreSim.Analog.Devices.BatteryChemistry.LithiumIon, 6),
                (RobotTwin.CoreSim.Analog.Devices.BatteryChemistry.LithiumCoin, 1), (RobotTwin.CoreSim.Analog.Devices.BatteryChemistry.LeadAcid, 12),
                (RobotTwin.CoreSim.Analog.Devices.BatteryChemistry.NiMH, 10), (RobotTwin.CoreSim.Analog.Devices.BatteryChemistry.LiFePO4, 8),
            };
            foreach (var (chemistry, max) in order)
            {
                double cell = RobotTwin.CoreSim.Analog.Devices.Battery.NominalCellVoltage(chemistry);
                int n = (int)Math.Round(volts / cell);
                if (n >= 1 && n <= max && Math.Abs(n * cell - volts) <= 0.02 * volts) return (chemistry, n, false);
            }

            // The pack's regulator only steps down: enough cells to stay above the output until they are nearly empty (3.4 V each).
            return (RobotTwin.CoreSim.Analog.Devices.BatteryChemistry.LithiumIon, Math.Max(1, (int)Math.Ceiling((volts + 0.3) / 3.4)), true);
        }

        private static bool TryParseValue(string value, out string text)
        {
            text = string.Empty;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string trimmed = value.Trim();
            foreach (string candidate in new[] { trimmed, trimmed.Split(new[] { ' ', '\t', '/', ',' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty })
            {
                if (candidate.Length > 0 && char.IsDigit(candidate[0]) && SiValue.TryParse(candidate, out double v) && v > 0)
                {
                    text = candidate;
                    return true;
                }
            }

            return false;
        }

        private static string MakePartId(KiCadSymbol symbol) =>
            "kicad-" + (string.IsNullOrEmpty(symbol.Library) ? string.Empty : Slug(symbol.Library) + "-") + Slug(symbol.Name);

        private static string Slug(string text)
        {
            var sb = new StringBuilder();
            foreach (char ch in text.ToLowerInvariant())
            {
                if (ch < 128 && (char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '+')) sb.Append(ch);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }

            string s = sb.ToString().Trim('-');
            return s.Length == 0 ? "part" : s;
        }

        private static void SplitLibId(string raw, out string? lib, out string name)
        {
            int colon = raw.IndexOf(':');
            if (colon > 0 && colon < raw.Length - 1)
            {
                lib = raw.Substring(0, colon);
                name = raw.Substring(colon + 1);
            }
            else
            {
                lib = null;
                name = raw;
            }
        }

        private static string LibraryNickname(string path)
        {
            string trimmed = path.TrimEnd('/', '\\');
            string file = Path.GetFileName(trimmed);
            foreach (string ext in new[] { ".pretty", ".kicad_sym", ".kicad_mod" })
            {
                if (file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return file.Substring(0, file.Length - ext.Length);
            }

            return file;
        }

        /// <summary>Orders pin numbers naturally: "2" before "10", "A2" before "A10".</summary>
        private sealed class NaturalComparer : IComparer<string>
        {
            public static readonly NaturalComparer Instance = new NaturalComparer();

            public int Compare(string? x, string? y)
            {
                x ??= string.Empty;
                y ??= string.Empty;
                int i = 0, j = 0;
                while (i < x.Length && j < y.Length)
                {
                    if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                    {
                        int si = i, sj = j;
                        while (i < x.Length && char.IsDigit(x[i])) i++;
                        while (j < y.Length && char.IsDigit(y[j])) j++;
                        string a = x.Substring(si, i - si).TrimStart('0'), b = y.Substring(sj, j - sj).TrimStart('0');
                        int c = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                        if (c != 0) return c;
                    }
                    else
                    {
                        int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                        if (c != 0) return c;
                        i++;
                        j++;
                    }
                }

                return (x.Length - i).CompareTo(y.Length - j);
            }
        }

        // ================================================================== whole libraries

        /// <summary>
        /// Imports <c>.kicad_sym</c> files, <c>.kicad_mod</c> files and <c>.pretty</c> folders (a plain
        /// folder is scanned for those) and builds a part library: every symbol becomes a
        /// <see cref="CustomPart"/>, paired with the footprint its Footprint property names
        /// ("Lib:Name", matched by library and name, then by name; a single match of the
        /// symbol's footprint filters is used when the property is empty). Footprints whose id
        /// would replace a built-in footprint are renamed "&lt;Lib&gt;_&lt;Name&gt;".
        /// </summary>
        public static PartLibraryFile ImportLibraries(IEnumerable<string> paths, PartLibrary library, out ImportReport report)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (library == null) throw new ArgumentNullException(nameof(library));
            report = new ImportReport();
            var rep = report;
            var symbolFiles = new List<string>();
            var libNames = new List<string>();
            var footprints = new List<(Footprint Fp, string Lib)>();

            void AddFootprint(Footprint fp, string lib)
            {
                footprints.Add((fp, lib));
                var info = GetFootprintInfo(fp);
                if (info != null)
                {
                    foreach (var w in info.Warnings) rep.Warnings.Add($"{lib}:{fp.Id}: {w}");
                }
            }

            void AddPretty(string dir)
            {
                var errors = new List<string>();
                string lib = LibraryNickname(dir);
                foreach (var fp in ImportFootprintLibrary(dir, errors)) AddFootprint(fp, lib);
                rep.Errors.AddRange(errors);
                libNames.Add(lib);
            }

            void AddFile(string file)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                try
                {
                    switch (ext)
                    {
                        case ".kicad_sym":
                            symbolFiles.Add(file);
                            break;
                        case ".kicad_mod":
                        {
                            string? parent = Path.GetDirectoryName(Path.GetFullPath(file));
                            string lib = parent != null && parent.EndsWith(".pretty", StringComparison.OrdinalIgnoreCase) ? LibraryNickname(parent) : string.Empty;
                            var fp = ImportFootprint(File.ReadAllText(file), lib.Length > 0 ? lib : null);
                            fp.Id = Path.GetFileNameWithoutExtension(file);
                            AddFootprint(fp, lib);
                            break;
                        }

                        case ".lib":
                            rep.Errors.Add($"{file}: KiCad 5 eski sembol kütüphanesi (.lib) desteklenmiyor; .kicad_sym üretmek için kütüphaneyi KiCad 6 veya daha yenisinde açıp kaydedin.");
                            break;
                        default:
                            rep.Errors.Add($"{file}: KiCad kütüphane dosyası değil (.kicad_sym, .kicad_mod veya .pretty klasörü bekleniyor).");
                            break;
                    }
                }
                catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    rep.Errors.Add($"{file}: {ex.Message}");
                }
            }

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (Directory.Exists(path))
                {
                    if (path.TrimEnd('/', '\\').EndsWith(".pretty", StringComparison.OrdinalIgnoreCase) || Directory.GetFiles(path, "*.kicad_mod").Length > 0)
                    {
                        AddPretty(path);
                    }

                    foreach (var sub in Directory.GetDirectories(path, "*.pretty").OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) AddPretty(sub);
                    foreach (var f in Directory.GetFiles(path, "*.kicad_sym").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) AddFile(f);
                }
                else if (File.Exists(path))
                {
                    AddFile(path);
                }
                else
                {
                    rep.Errors.Add($"{path}: bulunamadı.");
                }
            }

            // Footprint ids: never shadow a built-in footprint; later duplicates replace earlier ones.
            var byKey = new Dictionary<string, Footprint>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, Footprint>(StringComparer.OrdinalIgnoreCase);
            var finalIds = new Dictionary<string, Footprint>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fp, lib) in footprints)
            {
                string original = fp.Id;
                if (IsBuiltInFootprint(fp.Id))
                {
                    string renamed = (lib.Length > 0 ? lib : "KiCad") + "_" + fp.Id;
                    rep.Warnings.Add($"'{original}' footprint'i yerleşik bir footprint ile aynı adda; '{renamed}' olarak alındı.");
                    fp.Id = renamed;
                }

                if (finalIds.TryGetValue(fp.Id, out var previous))
                {
                    rep.Warnings.Add($"'{fp.Id}' footprint'i iki kez geldi; sonraki ({(lib.Length > 0 ? lib : "?")}) kullanıldı.");
                    rep.Footprints.Remove(previous);
                }

                finalIds[fp.Id] = fp;
                rep.Footprints.Add(fp);
                byName[original] = fp;
                if (lib.Length > 0) byKey[lib + ":" + original] = fp;
            }

            // Symbols.
            var file = new PartLibraryFile();
            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string symFile in symbolFiles)
            {
                string lib = LibraryNickname(symFile);
                libNames.Add(lib);
                List<KiCadSymbol> symbols;
                try
                {
                    symbols = ImportSymbolLibrary(File.ReadAllText(symFile), lib);
                }
                catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    rep.Errors.Add($"{symFile}: {ex.Message}");
                    continue;
                }

                foreach (var sym in symbols)
                {
                    rep.Symbols.Add(sym);
                    var notes = new List<string>();
                    Footprint? fp = null;
                    bool embed = true;
                    string fpRef = sym.FootprintRef.Trim();
                    if (fpRef.Length > 0)
                    {
                        SplitLibId(fpRef, out string? fpLib, out string fpName);
                        if (fpLib != null && byKey.TryGetValue(fpLib + ":" + fpName, out var exact)) fp = exact;
                        else if (byName.TryGetValue(fpName, out var named))
                        {
                            fp = named;
                            if (fpLib != null) notes.Add($"'{fpRef}' footprint'i adıyla eşlendi.");
                        }
                        else if (SafeGet(fpName) is Footprint builtIn && !FootprintLibrary.UserIds.Contains(fpName))
                        {
                            fp = builtIn;
                            embed = false;
                            notes.Add($"'{fpRef}' footprint'i yerleşik '{builtIn.Id}' footprint'i.");
                        }
                    }
                    else
                    {
                        var filters = sym.FootprintFilters.Select(GlobRegex).ToList();
                        if (filters.Count > 0)
                        {
                            var hits = finalIds.Values.Where(f => filters.Any(r => r.IsMatch(f.Id) || r.IsMatch(GetFootprintInfo(f)?.Name ?? f.Id))).ToList();
                            if (hits.Count == 1)
                            {
                                fp = hits[0];
                                notes.Add($"Footprint özelliği yok; süzgeçlere ({string.Join(" ", sym.FootprintFilters)}) uyan tek alınan footprint '{fp.Id}'.");
                            }
                            else if (hits.Count > 1)
                            {
                                notes.Add($"Footprint özelliği yok; süzgeçlere {hits.Count} footprint uyuyor, hiçbiri seçilmedi.");
                            }
                        }
                    }

                    var part = Map(sym, fp, library, embed, out var status, out var log);
                    var entry = new KiCadImportEntry
                    {
                        Symbol = sym.FullName,
                        Status = status,
                        BasePartId = part?.BasePartId,
                        FootprintId = part != null && part.Parameters.TryGetValue("footprint", out var f) ? f : null,
                        Details = string.Join("\n", notes.Concat(log))
                    };
                    if (part != null)
                    {
                        string id = part.Id;
                        for (int k = 2; usedIds.Contains(id) || library.FindPreset(id) != null; k++) id = part.Id + "-" + k.ToString(CultureInfo.InvariantCulture);
                        part.Id = id;
                        usedIds.Add(id);
                        file.Parts.Add(part);
                        entry.PartId = id;
                    }

                    rep.Entries.Add(entry);
                }
            }

            var names = libNames.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            file.Name = names.Count > 0 ? "KiCad: " + string.Join(", ", names) : "KiCad";
            file.Author = "KiCad içe aktarımı";
            file.Description = $"KiCad'den alındı: {rep.Symbols.Count} sembol, {rep.Footprints.Count} footprint; "
                               + $"{rep.Count(KiCadImportStatus.Mapped)} simülasyon modeline eşlendi, {rep.Count(KiCadImportStatus.Generic)} genel parça.";
            return file;
        }

        /// <summary>
        /// Registers an import: the parts (<see cref="CustomPart.Register"/>) and then every imported
        /// footprint at full fidelity (an embedded <see cref="CustomFootprint"/> keeps pads and
        /// courtyard but not the silkscreen). Returns the number of parts added.
        /// </summary>
        public static int Register(PartLibraryFile file, ImportReport report, PartLibrary library)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (library == null) throw new ArgumentNullException(nameof(library));
            int added = 0;
            foreach (var part in file.Parts)
            {
                if (part.Register(library)) added++;
            }

            if (report != null)
            {
                foreach (var fp in report.Footprints) FootprintLibrary.Register(fp);
            }

            return added;
        }

        private static bool IsBuiltInFootprint(string id)
        {
            if (FootprintLibrary.KnownIds.Contains(id, StringComparer.OrdinalIgnoreCase)) return true;
            if (FootprintLibrary.UserIds.Contains(id)) return false;
            return SafeGet(id) != null;
        }

        private static Regex GlobRegex(string glob) =>
            new Regex("^" + Regex.Escape(glob).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
