using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Pcb;

namespace RobotTwin.CoreSim.Libraries
{
    // [libs] SPICE library → parts: .model cards become presets of the built-in diode /
    // BJT / MOSFET models, .subckt definitions become composite parts (own pins, the
    // subcircuit flattened into supported primitives at simulation time).

    public enum SpiceItemKind
    {
        Model,
        Subcircuit
    }

    public sealed class SpiceImportItem
    {
        public SpiceItemKind Kind { get; set; }
        public string Name { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public SpiceModelCard? Card { get; set; }
        public SpiceSubcircuit? Subcircuit { get; set; }
        public SpiceLibrary Library { get; set; } = new SpiceLibrary();
        public bool Selected { get; set; } = true;

        /// <summary>Subcircuit port → displayed pin name (renames).</summary>
        public Dictionary<string, string> PinNames { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Footprint id for the part (empty = default).</summary>
        public string FootprintId { get; set; } = string.Empty;

        /// <summary>Line-by-line findings for this item (filled by <see cref="SpiceImportSession.Analyze"/>).</summary>
        public ImportLog Log { get; } = new ImportLog();

        public int Devices { get; set; }
        public int Skipped { get; set; }

        public string KindText => Kind == SpiceItemKind.Model ? ".model " + (Card?.Type ?? string.Empty) : $".subckt ({Subcircuit?.Pins.Count ?? 0} bacak)";

        public string StatusText => Kind == SpiceItemKind.Model && Log.HasErrors ? "desteklenmiyor"
            : Log.HasErrors && Devices == 0 && Kind == SpiceItemKind.Subcircuit ? "kullanılamaz"
            : Log.HasErrors ? "kısmen (atlanan eleman var)"
            : Log.WarningCount > 0 ? "uyarılarla"
            : "hazır";
    }

    public sealed class SpiceImportSession
    {
        public List<SpiceImportItem> Items { get; } = new List<SpiceImportItem>();
        public List<SpiceLibrary> Libraries { get; } = new List<SpiceLibrary>();
        public ImportLog Log { get; } = new ImportLog();
        public List<string> Paths { get; } = new List<string>();

        public static SpiceImportSession Open(IEnumerable<string> paths)
        {
            var s = new SpiceImportSession();
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)
                                 .Where(f => SpiceParser.Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()) && !f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                                 .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(2000))
                    {
                        s.AddFile(f);
                    }
                }
                else
                {
                    s.AddFile(p);
                }
            }

            return s;
        }

        public static SpiceImportSession FromText(string text, string fileName)
        {
            var s = new SpiceImportSession();
            s.AddLibrary(SpiceParser.Parse(text, fileName), fileName);
            return s;
        }

        private void AddFile(string path)
        {
            Paths.Add(path);
            if (!System.IO.File.Exists(path))
            {
                Log.Error("dosya bulunamadı.", path);
                return;
            }

            AddLibrary(SpiceParser.ParseFile(path), path);
        }

        private void AddLibrary(SpiceLibrary lib, string path)
        {
            Libraries.Add(lib);
            Log.AddRange(lib.Log.Issues);
            foreach (var name in lib.Order)
            {
                if (lib.Subcircuits.TryGetValue(name, out var sub) && sub.Name == name && !Items.Any(i => i.Kind == SpiceItemKind.Subcircuit && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    var item = new SpiceImportItem { Kind = SpiceItemKind.Subcircuit, Name = sub.Name, File = path, Subcircuit = sub, Library = lib };
                    foreach (var p in sub.Pins) item.PinNames[p] = p;
                    item.FootprintId = DefaultFootprint(sub.Pins.Count);
                    Items.Add(item);
                }
                else if (lib.Models.TryGetValue(name, out var card) && !Items.Any(i => i.Kind == SpiceItemKind.Model && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    var item = new SpiceImportItem { Kind = SpiceItemKind.Model, Name = card.Name, File = path, Card = card, Library = lib, Selected = SpiceModelMap.IsSupported(card) };
                    Items.Add(item);
                }
            }

            foreach (var item in Items.Where(i => ReferenceEquals(i.Library, lib))) Analyze(item);
        }

        /// <summary>Default footprint for a subcircuit with <paramref name="pins"/> ports.</summary>
        public static string DefaultFootprint(int pins)
        {
            if (pins == 2) return "AXIAL-400";
            if (pins == 3) return "TO92";
            if (pins == 8 || pins == 14 || pins == 16) return "DIP-" + pins.ToString(CultureInfo.InvariantCulture);
            if (pins >= 4 && pins % 2 == 0 && pins <= 64) return PackageGenerator.IdFor(new PackageSpec { Family = PackageFamily.Dip, Pins = pins, Pitch = 2.54, Span = pins > 28 ? 15.24 : 7.62 });
            return PackageGenerator.IdFor(new PackageSpec { Family = PackageFamily.Header, Pins = Math.Max(1, pins), Rows = 1, Pitch = 2.54 });
        }

        public static void Analyze(SpiceImportItem item)
        {
            item.Log.Issues.Clear();
            if (item.Kind == SpiceItemKind.Model && item.Card != null)
            {
                SpiceModelMap.ToPartParameters(item.Card, out _, item.Log);
                return;
            }

            if (item.Subcircuit == null) return;
            var check = SpiceCircuitBuilder.Check(item.Library, item.Subcircuit.Name, out int devices, out int skipped);
            item.Devices = devices;
            item.Skipped = skipped;
            item.Log.AddRange(check.Issues);
            item.Log.Info($"{devices} eleman simüle edilecek" + (skipped > 0 ? $", {skipped} eleman atlanacak" : string.Empty) + ".");
        }

        private static string Slug(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

        /// <summary>Resolves (and if needed generates) a footprint id.</summary>
        public static Footprint? ResolveFootprint(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            try
            {
                var fp = FootprintLibrary.Get(id);
                if (fp != null) return fp;
            }
            catch (Exception ex) when (ex is FormatException || ex is IndexOutOfRangeException || ex is ArgumentException || ex is OverflowException)
            {
            }

            var m = Regex.Match(id, @"^RW_DIP-(\d+)_W([\d.]+)mm$");
            if (m.Success) return PackageGenerator.Generate(new PackageSpec { Family = PackageFamily.Dip, Pins = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Pitch = 2.54, Span = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) });
            m = Regex.Match(id, @"^RW_Header-(\d+)x(\d+)_P([\d.]+)mm$");
            if (m.Success)
            {
                int rows = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), cols = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                return PackageGenerator.Generate(new PackageSpec { Family = PackageFamily.Header, Rows = rows, Pins = rows * cols, Pitch = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), Height = 8.5 });
            }

            return null;
        }

        public CustomPart? BuildPart(SpiceImportItem item, PartLibrary library, ImportLog log)
        {
            string source = Path.GetFileName(item.File);
            string category = "SPICE · " + (string.IsNullOrEmpty(source) ? "model" : source);
            if (item.Kind == SpiceItemKind.Model)
            {
                var card = item.Card!;
                var p = SpiceModelMap.ToPartParameters(card, out string basePart, log);
                if (p == null || library.Find(basePart) == null) return null;
                string kind = card.Kind switch { "D" => "diyot", "NPN" => "NPN transistör", "PNP" => "PNP transistör", "NMOS" => "N-kanal MOSFET", _ => "P-kanal MOSFET" };
                var part = new CustomPart
                {
                    Id = "spice-" + Slug(card.Name),
                    Name = card.Name,
                    BasePartId = basePart,
                    Category = category,
                    Description = $"SPICE {kind} modeli ({card.Type})" + (card.Text.TryGetValue("MFG", out var mfg) ? ", " + mfg : string.Empty),
                    Manufacturer = card.Text.TryGetValue("MFG", out var m2) ? m2 : string.Empty,
                    Author = "SPICE içe aktarımı",
                    Parameters = p
                };
                if (!string.IsNullOrWhiteSpace(item.FootprintId)) part.Parameters["footprint"] = item.FootprintId;
                return part;
            }

            var sub = item.Subcircuit!;
            if (item.Devices == 0)
            {
                log.Error("alt devreden simüle edilebilir eleman çıkmadı; parça oluşturulmadı.", item.File, sub.Line, sub.Name);
                return null;
            }

            var spec = new UserPartSpec
            {
                RefPrefix = "U",
                Symbol = UserSymbolStyle.Box,
                BodyLabel = sub.Name,
                Source = "SPICE " + source,
                Model = new UserModel { Kind = UserModelKind.Subcircuit, SubcircuitName = sub.Name, SpiceText = item.Library.Extract(sub.Name) }
            };
            int n = sub.Pins.Count;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < n; i++)
            {
                string port = sub.Pins[i];
                string name = item.PinNames.TryGetValue(port, out var nn) && !string.IsNullOrWhiteSpace(nn) ? nn.Trim() : port;
                if (!used.Add(name))
                {
                    // Repeated port names (same node twice) still need distinct pins.
                    name = name + "_" + (i + 1).ToString(CultureInfo.InvariantCulture);
                    used.Add(name);
                }

                bool output = Regex.IsMatch(name, "OUT|^Q$|^Y$|^O$|VO", RegexOptions.IgnoreCase);
                spec.Pins.Add(new UserPin
                {
                    Name = name,
                    Pad = (i + 1).ToString(CultureInfo.InvariantCulture),
                    Type = output ? PinElectricalType.Output : Regex.IsMatch(name, "^(V[CDE]{2}|VCC|VDD|VEE|VSS|GND|V\\+|V-)$", RegexOptions.IgnoreCase) ? PinElectricalType.PowerIn : PinElectricalType.Passive,
                    Side = output ? PinSide.Right : (i < (n + 1) / 2 ? PinSide.Left : PinSide.Right)
                });
                spec.Model.PinMap[port] = name;
            }

            foreach (var kv in sub.Parameters)
            {
                spec.Parameters.Add(new UserParameter { Key = kv.Key, Label = kv.Key, Value = SpiceSyntax.Unquote(kv.Value).Trim('{', '}'), Description = "SPICE alt devre parametresi" });
            }

            string id = "spice-" + Slug(sub.Name);
            var part2 = new CustomPart
            {
                Id = id,
                Name = sub.Name,
                BasePartId = "udef-" + id,
                Category = category,
                Description = string.IsNullOrWhiteSpace(sub.Description) ? $"SPICE alt devresi, {n} bacak, {item.Devices} eleman" : sub.Description,
                Author = "SPICE içe aktarımı",
                Definition = spec,
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            var fp = ResolveFootprint(item.FootprintId);
            if (fp != null)
            {
                part2.Parameters["footprint"] = fp.Id;
                if (!FootprintLibrary.IsBuiltIn(fp.Id)) part2.Footprint = CustomFootprint.From(fp);
            }
            else if (!string.IsNullOrWhiteSpace(item.FootprintId))
            {
                log.Warn($"kılıf '{item.FootprintId}' bulunamadı; kılıfsız oluşturuldu.", item.File, sub.Line, sub.Name);
            }

            var validation = spec.Validate(fp, library, sub.Name);
            log.AddRange(validation.Issues.Where(i => i.Severity != IssueSeverity.Info));
            return part2;
        }

        public PartLibraryFile Commit(PartLibrary library, ImportLog log)
        {
            var names = Paths.Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n)).ToList();
            var file = new PartLibraryFile
            {
                Name = "SPICE: " + (names.Count > 0 ? string.Join(", ", names.Take(3)) + (names.Count > 3 ? " …" : string.Empty) : "modeller"),
                Author = "SPICE içe aktarımı",
                Version = Paths.Where(System.IO.File.Exists).Select(p => System.IO.File.GetLastWriteTimeUtc(p)).DefaultIfEmpty(DateTime.UtcNow).Max().ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
                Description = $"{Items.Count(i => i.Kind == SpiceItemKind.Model)} model, {Items.Count(i => i.Kind == SpiceItemKind.Subcircuit)} alt devre"
            };
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Items.Where(i => i.Selected))
            {
                var part = BuildPart(item, library, log);
                if (part == null) continue;
                string id = part.Id;
                for (int k = 2; !ids.Add(id); k++) id = part.Id + "-" + k.ToString(CultureInfo.InvariantCulture);
                if (id != part.Id)
                {
                    if (part.Definition != null) part.BasePartId = "udef-" + id;
                    part.Id = id;
                }

                file.Parts.Add(part);
            }

            return file;
        }
    }
}
