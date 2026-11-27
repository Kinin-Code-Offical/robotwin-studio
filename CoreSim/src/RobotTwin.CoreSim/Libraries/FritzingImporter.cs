using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Pcb;

namespace RobotTwin.CoreSim.Libraries
{
    // [libs] Fritzing parts (.fzpz = zip of a .fzp module description and SVG views):
    // metadata, connectors → pins, breadboard SVG kept as a flat picture, PCB SVG pads
    // turned into a footprint when they can be located, otherwise a 1×N header.

    public sealed class FritzingPart
    {
        public string ModuleId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Label { get; set; } = "U";
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public List<string> Tags { get; } = new List<string>();
        public Dictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<(string Id, string Name, string Type, string Description)> Connectors { get; } = new List<(string, string, string, string)>();
        public string? BreadboardSvg { get; set; }
        public string? PcbSvg { get; set; }
        public Footprint? Footprint { get; set; }
        public ImportLog Log { get; } = new ImportLog();
        public string File { get; set; } = string.Empty;
    }

    public static class FritzingImporter
    {
        private const long MaxSvg = 4 * 1024 * 1024;

        public static FritzingPart Read(string path)
        {
            var part = new FritzingPart { File = path };
            try
            {
                if (path.EndsWith(".fzpz", StringComparison.OrdinalIgnoreCase)) ReadArchive(path, part);
                else if (path.EndsWith(".fzp", StringComparison.OrdinalIgnoreCase)) ReadFzp(System.IO.File.ReadAllText(path), part, name => FindLoose(path, name));
                else part.Log.Error("Fritzing parçası değil (.fzpz veya .fzp bekleniyordu).", path);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is XmlException || ex is UnauthorizedAccessException)
            {
                part.Log.Error("okunamadı: " + ex.Message, path);
            }

            return part;
        }

        private static string? FindLoose(string fzpPath, string image)
        {
            // Loose .fzp: SVGs live in ../svg/core/<view>/<file>.
            string? dir = Path.GetDirectoryName(Path.GetFullPath(fzpPath));
            for (int up = 0; up < 3 && dir != null; up++, dir = Path.GetDirectoryName(dir))
            {
                foreach (var candidate in new[] { Path.Combine(dir, "svg", "core", image), Path.Combine(dir, "svg", "contrib", image), Path.Combine(dir, "svg", "user", image), Path.Combine(dir, image) })
                {
                    if (System.IO.File.Exists(candidate) && new FileInfo(candidate).Length < MaxSvg) return System.IO.File.ReadAllText(candidate);
                }
            }

            return null;
        }

        private static void ReadArchive(string path, FritzingPart part)
        {
            using var zip = ZipFile.OpenRead(path);
            var fzp = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".fzp", StringComparison.OrdinalIgnoreCase));
            if (fzp == null)
            {
                part.Log.Error(".fzpz içinde .fzp parça tanımı yok.", path);
                return;
            }

            string Text(ZipArchiveEntry e)
            {
                using var r = new StreamReader(e.Open(), Encoding.UTF8);
                return r.ReadToEnd();
            }

            ReadFzp(Text(fzp), part, image =>
            {
                // In an .fzpz "breadboard/foo.svg" is stored flat as "svg.breadboard.foo.svg".
                string flat = "svg." + image.Replace('/', '.').Replace('\\', '.');
                var e = zip.Entries.FirstOrDefault(x => x.Name.Equals(flat, StringComparison.OrdinalIgnoreCase))
                        ?? zip.Entries.FirstOrDefault(x => x.Name.EndsWith(Path.GetFileName(image), StringComparison.OrdinalIgnoreCase) && x.Name.StartsWith("svg.", StringComparison.OrdinalIgnoreCase)
                                                           && x.Name.IndexOf(image.Split('/')[0], StringComparison.OrdinalIgnoreCase) >= 0);
                return e != null && e.Length < MaxSvg ? Text(e) : null;
            });
        }

        private static void ReadFzp(string xml, FritzingPart part, Func<string, string?> openImage)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 };
            XDocument doc;
            using (var reader = XmlReader.Create(new StringReader(xml), settings)) doc = XDocument.Load(reader);
            var m = doc.Root;
            if (m == null || m.Name.LocalName != "module")
            {
                part.Log.Error("Fritzing .fzp değil: kök öğe <module> olmalı.", part.File);
                return;
            }

            string El(string name) => m.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? string.Empty;
            part.ModuleId = (string?)m.Attribute("moduleId") ?? string.Empty;
            part.Title = El("title");
            part.Label = El("label").Length > 0 ? El("label") : "U";
            part.Author = El("author");
            part.Version = El("version");
            part.Description = Regex.Replace(Regex.Replace(El("description"), "<[^>]+>", " "), @"\s+", " ").Trim();
            foreach (var t in m.Descendants().Where(e => e.Name.LocalName == "tag")) part.Tags.Add(t.Value.Trim());
            foreach (var p in m.Descendants().Where(e => e.Name.LocalName == "property"))
            {
                string key = (string?)p.Attribute("name") ?? string.Empty;
                if (key.Length > 0) part.Properties[key] = p.Value.Trim();
            }

            part.Family = part.Properties.TryGetValue("family", out var fam) ? fam : string.Empty;
            if (part.Title.Length == 0) part.Title = part.ModuleId.Length > 0 ? part.ModuleId : "Fritzing parçası";

            foreach (var c in m.Descendants().Where(e => e.Name.LocalName == "connector"))
            {
                string id = (string?)c.Attribute("id") ?? string.Empty;
                string name = (string?)c.Attribute("name") ?? id;
                string type = (string?)c.Attribute("type") ?? "male";
                string desc = c.Elements().FirstOrDefault(e => e.Name.LocalName == "description")?.Value.Trim() ?? string.Empty;
                if (id.Length == 0) continue;
                part.Connectors.Add((id, name, type, desc));
            }

            if (part.Connectors.Count == 0) part.Log.Error("parçada bağlantı noktası (connector) yok.", part.File);
            // connector0, connector1 … in numeric order.
            part.Connectors.Sort((a, b) => ConnectorIndex(a.Id).CompareTo(ConnectorIndex(b.Id)));

            string? Image(string view)
            {
                var v = m.Descendants().FirstOrDefault(e => e.Name.LocalName == view);
                var layers = v?.Descendants().FirstOrDefault(e => e.Name.LocalName == "layers");
                string img = (string?)layers?.Attribute("image") ?? string.Empty;
                return img.Length > 0 ? openImage(img) : null;
            }

            part.BreadboardSvg = Image("breadboardView");
            if (part.BreadboardSvg == null) part.Log.Warn("breadboard görünümü (SVG) bulunamadı; parça resimsiz alınacak.", part.File);
            part.PcbSvg = Image("pcbView");
            part.Footprint = part.PcbSvg != null ? PcbFootprint(part) : null;
            if (part.Footprint == null)
            {
                part.Log.Warn($"PCB görünümünden pad konumları çıkarılamadı; 1×{part.Connectors.Count} header (2.54 mm) kılıfı üretildi — Parça oluşturucuda değiştirebilirsiniz.", part.File);
            }

            part.Log.Info("Fritzing parçalarının elektriksel modeli yoktur (yalnız bağlantı noktaları); bir model Parça oluşturucuda eklenebilir.", part.File);
        }

        private static int ConnectorIndex(string id)
        {
            var m = Regex.Match(id, @"(\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : int.MaxValue;
        }

        // ------------------------------------------------------------------ PCB SVG → footprint

        private static double Length(string? s, double fallbackPx)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallbackPx;
            var m = Regex.Match(s!.Trim(), @"^([-+]?[\d.]+(?:[eE][-+]?\d+)?)\s*(in|mm|cm|px|pt)?$");
            if (!m.Success) return fallbackPx;
            double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return m.Groups[2].Value switch
            {
                "in" => v * 25.4,
                "mm" => v,
                "cm" => v * 10,
                "pt" => v * 25.4 / 72,
                _ => v * 25.4 / 90 // Fritzing's legacy 90 dpi pixels
            };
        }

        private static double[] Transform(XElement e)
        {
            // Accumulated affine [a b c d e f] of the element's ancestors and itself (translate / matrix / scale only).
            var chain = e.AncestorsAndSelf().Reverse().ToList();
            double[] t = { 1, 0, 0, 1, 0, 0 };
            foreach (var x in chain)
            {
                string? tr = (string?)x.Attribute("transform");
                if (string.IsNullOrWhiteSpace(tr)) continue;
                foreach (Match m in Regex.Matches(tr!, @"(matrix|translate|scale|rotate)\s*\(([^)]*)\)"))
                {
                    var a = Regex.Split(m.Groups[2].Value.Trim(), @"[\s,]+").Where(s => s.Length > 0).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                    double[] n;
                    switch (m.Groups[1].Value)
                    {
                        case "matrix" when a.Length == 6: n = a; break;
                        case "translate": n = new[] { 1, 0, 0, 1, a.Length > 0 ? a[0] : 0, a.Length > 1 ? a[1] : 0 }; break;
                        case "scale": n = new[] { a.Length > 0 ? a[0] : 1, 0, 0, a.Length > 1 ? a[1] : (a.Length > 0 ? a[0] : 1), 0, 0 }; break;
                        case "rotate" when a.Length >= 1:
                        {
                            double r = a[0] * Math.PI / 180, cx = a.Length >= 3 ? a[1] : 0, cy = a.Length >= 3 ? a[2] : 0;
                            double c = Math.Cos(r), s = Math.Sin(r);
                            n = new[] { c, s, -s, c, cx - c * cx + s * cy, cy - s * cx - c * cy };
                            break;
                        }

                        default: continue;
                    }

                    t = new[]
                    {
                        t[0] * n[0] + t[2] * n[1], t[1] * n[0] + t[3] * n[1],
                        t[0] * n[2] + t[2] * n[3], t[1] * n[2] + t[3] * n[3],
                        t[0] * n[4] + t[2] * n[5] + t[4], t[1] * n[4] + t[3] * n[5] + t[5]
                    };
                }
            }

            return t;
        }

        private static Footprint? PcbFootprint(FritzingPart part)
        {
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                XDocument doc;
                using (var reader = XmlReader.Create(new StringReader(part.PcbSvg!), settings)) doc = XDocument.Load(reader);
                var svg = doc.Root!;
                var vb = ((string?)svg.Attribute("viewBox") ?? string.Empty).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                double widthMm = Length((string?)svg.Attribute("width"), vb.Length == 4 ? vb[2] : 100);
                double heightMm = Length((string?)svg.Attribute("height"), vb.Length == 4 ? vb[3] : 100);
                double sx = vb.Length == 4 && vb[2] > 0 ? widthMm / vb[2] : 25.4 / 90, sy = vb.Length == 4 && vb[3] > 0 ? heightMm / vb[3] : 25.4 / 90;
                double ox = vb.Length == 4 ? vb[0] : 0, oy = vb.Length == 4 ? vb[1] : 0;

                var pads = new List<Pad>();
                for (int i = 0; i < part.Connectors.Count; i++)
                {
                    string cid = part.Connectors[i].Id;
                    var el = svg.Descendants().FirstOrDefault(e => ((string?)e.Attribute("id")) is string id && (id == cid + "pin" || id == cid + "pad") && (e.Name.LocalName == "circle" || e.Name.LocalName == "rect" || e.Name.LocalName == "ellipse"));
                    if (el == null) return null;
                    var t = Transform(el);
                    double A(string n) => double.TryParse((string?)el.Attribute(n), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
                    double stroke = A("stroke-width");
                    double scale = Math.Sqrt(Math.Abs(t[0] * t[3] - t[1] * t[2]));
                    double cx, cy, w, h, drill = 0;
                    if (el.Name.LocalName == "rect")
                    {
                        cx = A("x") + A("width") / 2;
                        cy = A("y") + A("height") / 2;
                        w = A("width") + stroke;
                        h = A("height") + stroke;
                    }
                    else
                    {
                        double r = el.Name.LocalName == "circle" ? A("r") : Math.Max(A("rx"), A("ry"));
                        cx = A("cx");
                        cy = A("cy");
                        w = h = 2 * r + stroke;
                        if (stroke > 0 && ((string?)el.Attribute("fill") ?? "none") == "none") drill = Math.Max(0, 2 * r - stroke);
                    }

                    double X = t[0] * cx + t[2] * cy + t[4], Y = t[1] * cx + t[3] * cy + t[5];
                    var pad = new Pad
                    {
                        Name = (i + 1).ToString(CultureInfo.InvariantCulture),
                        X = (X - ox) * sx,
                        Y = (Y - oy) * sy,
                        Width = Math.Max(0.2, w * scale * sx),
                        Height = Math.Max(0.2, h * scale * sy),
                        Shape = el.Name.LocalName == "rect" ? PadShape.Rect : PadShape.Circle,
                        Drill = drill * scale * sx
                    };
                    if (pad.Drill <= 0) pad.Layer = CopperLayer.Top;
                    pads.Add(pad);
                }

                if (pads.Count == 0) return null;
                double mx = (pads.Min(p => p.X) + pads.Max(p => p.X)) / 2, my = (pads.Min(p => p.Y) + pads.Max(p => p.Y)) / 2;
                var fp = new Footprint { Id = "FZ_" + Regex.Replace(part.Title, "[^A-Za-z0-9_.-]+", "_"), Description = "Fritzing PCB görünümünden: " + part.Title, Height = 5 };
                foreach (var p in pads)
                {
                    p.X = Math.Round(p.X - mx, 4);
                    p.Y = Math.Round(p.Y - my, 4);
                    fp.Pads.Add(p);
                }

                double bw = widthMm / 2, bh = heightMm / 2;
                fp.Silkscreen.Add(new Segment2(-bw, -bh, bw, -bh));
                fp.Silkscreen.Add(new Segment2(bw, -bh, bw, bh));
                fp.Silkscreen.Add(new Segment2(bw, bh, -bw, bh));
                fp.Silkscreen.Add(new Segment2(-bw, bh, -bw, -bh));
                fp.FitCourtyard();
                part.Log.Info($"PCB görünümünden {pads.Count} pad konumu okundu ({Inv.Mm(widthMm)}×{Inv.Mm(heightMm)} mm).", part.File);
                return fp;
            }
            catch (Exception ex) when (ex is XmlException || ex is FormatException || ex is InvalidOperationException || ex is NullReferenceException)
            {
                part.Log.Warn("PCB SVG okunamadı: " + ex.Message, part.File);
                return null;
            }
        }

        /// <summary>The Fritzing part as a RobotWin user part (no electrical model).</summary>
        public static CustomPart? ToCustomPart(FritzingPart fz, ImportLog log)
        {
            log.AddRange(fz.Log.Issues);
            if (fz.Connectors.Count == 0) return null;
            bool connector = Regex.IsMatch(fz.Family + " " + string.Join(" ", fz.Tags), "header|connector|terminal|konnekt", RegexOptions.IgnoreCase);
            var spec = new UserPartSpec
            {
                RefPrefix = Regex.Match(fz.Label, "^[A-Za-z]+").Value is { Length: > 0 } pre ? pre : "U",
                Symbol = connector ? UserSymbolStyle.Connector : UserSymbolStyle.Box,
                BodyLabel = fz.Title,
                Source = "Fritzing " + Path.GetFileName(fz.File),
                BreadboardSvg = fz.BreadboardSvg,
                Keywords = string.Join(" ", fz.Tags)
            };
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = fz.Connectors.Count;
            for (int i = 0; i < n; i++)
            {
                var c = fz.Connectors[i];
                string name = string.IsNullOrWhiteSpace(c.Name) ? (i + 1).ToString(CultureInfo.InvariantCulture) : c.Name.Trim();
                if (!names.Add(name))
                {
                    name = name + "_" + (i + 1).ToString(CultureInfo.InvariantCulture);
                    names.Add(name);
                }

                var type = Regex.IsMatch(name, "^(GND|VCC|VIN|5V|3V3|3\\.3V|VDD|V\\+|\\+5V)$", RegexOptions.IgnoreCase) ? PinElectricalType.PowerIn : PinElectricalType.Passive;
                spec.Pins.Add(new UserPin { Name = name, Pad = (i + 1).ToString(CultureInfo.InvariantCulture), Type = type, Side = i < (n + 1) / 2 ? PinSide.Left : PinSide.Right, Description = c.Description });
            }

            var fp = fz.Footprint ?? PackageGenerator.Generate(new PackageSpec { Family = PackageFamily.Header, Pins = n, Rows = 1, Pitch = 2.54, Height = 8.5 });
            string id = "fz-" + Regex.Replace((fz.ModuleId.Length > 0 ? fz.ModuleId : fz.Title).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            if (id.Length > 60) id = id.Substring(0, 60).TrimEnd('-');
            var part = new CustomPart
            {
                Id = id,
                Name = fz.Title,
                BasePartId = "udef-" + id,
                Category = "Fritzing" + (fz.Family.Length > 0 ? " · " + fz.Family : string.Empty),
                Description = fz.Description.Length > 300 ? fz.Description.Substring(0, 300) + "…" : fz.Description,
                Author = fz.Author.Length > 0 ? fz.Author : "Fritzing",
                Manufacturer = fz.Properties.TryGetValue("manufacturer", out var mf) ? mf : string.Empty,
                PartNumber = fz.Properties.TryGetValue("mpn", out var mpn) ? mpn : fz.Properties.TryGetValue("part number", out var pn) ? pn : string.Empty,
                Source = "Fritzing",
                Definition = spec,
                Footprint = CustomFootprint.From(fp),
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["footprint"] = fp.Id }
            };
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "model", "footprint" };
            foreach (var kv in fz.Properties.Where(kv => kv.Key != "family").Take(12))
            {
                string key = Regex.Replace(kv.Key.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
                if (key.Length == 0 || !keys.Add(key)) continue;
                spec.Parameters.Add(new UserParameter { Key = key, Label = kv.Key, Value = kv.Value });
            }

            return part;
        }
    }
}
