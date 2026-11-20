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
    // [libs] Preview/mapping layer over KiCadLibraryImport: one item per symbol with the
    // automatic model mapping, an alternative "own pins" part (any pin count, KiCad pin
    // names and types), editable pin→pad mapping, pin/pad validation and 3D models.

    public enum KiCadPartMode
    {
        /// <summary>The automatic mapping onto a built-in model (the model's symbol is used).</summary>
        Automatic,

        /// <summary>A part with the KiCad symbol's own pins (rectangle symbol), optionally driven by the mapped model.</summary>
        OwnPins
    }

    public sealed class KiCadImportItem
    {
        public KiCadSymbol Symbol { get; set; } = new KiCadSymbol();
        public Footprint? Footprint { get; set; }
        public bool FootprintEmbedded { get; set; } = true;

        /// <summary>Result of the automatic mapping (null when it failed).</summary>
        public CustomPart? AutoPart { get; set; }

        public KiCadImportStatus AutoStatus { get; set; }
        public string AutoDetails { get; set; } = string.Empty;
        public KiCadPartMode Mode { get; set; }
        public bool Selected { get; set; } = true;

        /// <summary>KiCad pin number → pad (only entries the user changed).</summary>
        public Dictionary<string, string> PadOverrides { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Name => Symbol.FullName;

        public string StatusText => AutoStatus switch
        {
            KiCadImportStatus.Mapped => "modele eşlendi",
            KiCadImportStatus.Generic => "genel (modelsiz)",
            KiCadImportStatus.Skipped => "atlandı",
            _ => "otomatik eşleme yok"
        };
    }

    public sealed class KiCadImportSession
    {
        public List<KiCadImportItem> Items { get; } = new List<KiCadImportItem>();
        public List<Footprint> Footprints { get; } = new List<Footprint>();
        public ImportReport Report { get; private set; } = new ImportReport();
        public ImportLog Log { get; } = new ImportLog();
        public List<string> Paths { get; } = new List<string>();

        /// <summary>Folders searched for 3D models besides the KiCad installation (user-chosen).</summary>
        public List<string> ModelFolders { get; } = new List<string>();

        public bool Load3DModels { get; set; } = true;

        /// <summary>Library version (the newest symbol file version seen).</summary>
        public string Version { get; private set; } = "1.0";

        public string LibraryName { get; private set; } = "KiCad";

        public static KiCadImportSession Open(IEnumerable<string> paths, PartLibrary library)
        {
            var s = new KiCadImportSession();
            s.Paths.AddRange(paths);
            PartLibraryFile file;
            ImportReport report;
            try
            {
                file = KiCadLibraryImport.ImportLibraries(s.Paths, library, out report);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is InvalidDataException || ex is ArgumentException)
            {
                s.Log.Error("KiCad kütüphanesi okunamadı: " + ex.Message);
                return s;
            }

            s.Report = report;
            s.LibraryName = file.Name;
            s.Footprints.AddRange(report.Footprints);
            foreach (var e in report.Errors) s.Log.Error(TranslateKiCad(e));
            foreach (var w in report.Warnings) s.Log.Warn(TranslateKiCad(w));
            foreach (var dir in s.Paths.Where(Directory.Exists)) s.ModelFolders.Add(dir);
            foreach (var p in s.Paths)
            {
                string? parent = File.Exists(p) || Directory.Exists(p) ? Path.GetDirectoryName(Path.GetFullPath(p)) : null;
                if (parent != null && !s.ModelFolders.Contains(parent, StringComparer.OrdinalIgnoreCase)) s.ModelFolders.Add(parent);
            }

            foreach (var f in s.Paths.Where(p => p.EndsWith(".kicad_sym", StringComparison.OrdinalIgnoreCase) && File.Exists(p)))
            {
                try
                {
                    var m = Regex.Match(File.ReadAllText(f), @"\(version\s+(\d+)\)");
                    if (m.Success && string.CompareOrdinal(m.Groups[1].Value, s.Version) > 0) s.Version = m.Groups[1].Value;
                }
                catch (IOException)
                {
                }
            }

            foreach (var entry in report.Entries)
            {
                var sym = report.Symbols.FirstOrDefault(x => x.FullName == entry.Symbol);
                if (sym == null) continue;
                var part = entry.PartId != null ? file.Parts.FirstOrDefault(p => p.Id == entry.PartId) : null;
                Footprint? fp = null;
                bool embedded = true;
                if (entry.FootprintId != null)
                {
                    fp = report.Footprints.FirstOrDefault(f => string.Equals(f.Id, entry.FootprintId, StringComparison.OrdinalIgnoreCase));
                    if (fp == null)
                    {
                        fp = SafeFootprint(entry.FootprintId);
                        embedded = false;
                    }
                }

                if (fp == null && sym.FootprintRef.Trim().Length > 0)
                {
                    // The automatic mapping only records a footprint when it produced a part; look it up ourselves.
                    string fpRef = sym.FootprintRef.Trim();
                    int colon = fpRef.IndexOf(':');
                    string fpLib = colon > 0 ? fpRef.Substring(0, colon) : string.Empty, fpName = colon > 0 ? fpRef.Substring(colon + 1) : fpRef;
                    fp = report.Footprints.FirstOrDefault(f => KiCadLibraryImport.GetFootprintInfo(f) is { } info && info.Name == fpName && (fpLib.Length == 0 || info.Library == fpLib))
                         ?? report.Footprints.FirstOrDefault(f => KiCadLibraryImport.GetFootprintInfo(f)?.Name == fpName || f.Id == fpName);
                    if (fp == null && SafeFootprint(fpName) is { } builtIn && FootprintLibrary.IsBuiltIn(builtIn.Id))
                    {
                        fp = builtIn;
                        embedded = false;
                    }
                }

                var item = new KiCadImportItem
                {
                    Symbol = sym,
                    Footprint = fp,
                    FootprintEmbedded = embedded,
                    AutoPart = part,
                    AutoStatus = entry.Status,
                    AutoDetails = entry.Details,
                    Selected = entry.Status != KiCadImportStatus.Skipped && sym.Pins.Count > 0
                };

                // Generic connector-like parts and failures are better served by the symbol's own pins.
                item.Mode = entry.Status == KiCadImportStatus.Mapped && part != null ? KiCadPartMode.Automatic : KiCadPartMode.OwnPins;
                if (entry.Status == KiCadImportStatus.Mapped && part != null && part.BasePartId.StartsWith("header-", StringComparison.Ordinal) && sym.Pins.Count > 0)
                {
                    item.Mode = KiCadPartMode.OwnPins;
                }

                s.Items.Add(item);
            }

            if (s.Items.Count == 0 && s.Footprints.Count > 0) s.Log.Info($"{s.Footprints.Count} footprint okundu (sembol yok); footprint'ler kullanıcı kılıf kütüphanesine eklenecek.");
            return s;
        }

        private static Footprint? SafeFootprint(string id)
        {
            try
            {
                return FootprintLibrary.Get(id);
            }
            catch (Exception ex) when (ex is FormatException || ex is IndexOutOfRangeException || ex is ArgumentException || ex is OverflowException)
            {
                return null;
            }
        }

        /// <summary>The KiCad pins used for the part: unit 1 + common pins (+ power-only units), body style 1, stacked duplicates removed.</summary>
        public static List<KiCadPin> PartPins(KiCadSymbol s)
        {
            var styled = s.Pins.Where(p => p.BodyStyle <= 1).ToList();
            var powerUnits = new HashSet<int>();
            for (int u = 2; u <= s.UnitCount; u++)
            {
                var unitPins = styled.Where(p => p.Unit == u).ToList();
                if (unitPins.Count > 0 && unitPins.All(p => p.ElectricalType == "power_in" || p.ElectricalType == "power_out")) powerUnits.Add(u);
            }

            bool multiUnitAllSame = s.UnitCount > 1;
            var result = new List<KiCadPin>();
            var numbers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in styled.OrderBy(p => p.Unit))
            {
                // Multi-unit parts (4× NAND): every unit's pins belong to the one package.
                if (!multiUnitAllSame && p.Unit != 0 && p.Unit != 1 && !powerUnits.Contains(p.Unit)) continue;
                if (!numbers.Add(p.Number)) continue;
                result.Add(p);
            }

            return result;
        }

        /// <summary>Pad for a KiCad pin (the user's override, else the pin number).</summary>
        public static string PadOf(KiCadImportItem item, KiCadPin pin) =>
            item.PadOverrides.TryGetValue(pin.Number, out var pad) ? pad : pin.Number;

        /// <summary>Builds the part for an item according to its mode; null with errors in <paramref name="log"/> when impossible.</summary>
        public CustomPart? BuildPart(KiCadImportItem item, PartLibrary library, ImportLog log)
        {
            CustomPart? part;
            if (item.Mode == KiCadPartMode.Automatic && item.AutoPart != null)
            {
                part = Clone(item.AutoPart);
                if (item.PadOverrides.Count > 0 && part.Parameters.TryGetValue("pinmap", out var pinmap))
                {
                    // pinmap is "MODELPIN:kicadnumber"; overrides move the pad.
                    part.Parameters["pinmap"] = string.Join(",", pinmap.Split(',').Select(e =>
                    {
                        int c = e.LastIndexOf(':');
                        if (c <= 0) return e;
                        string num = e.Substring(c + 1).Trim();
                        return item.PadOverrides.TryGetValue(num, out var pad) ? e.Substring(0, c) + ":" + pad : e;
                    }));
                }
            }
            else
            {
                part = OwnPinsPart(item, library, log);
                if (part == null) return null;
            }

            // Pin ↔ pad check against the footprint.
            if (item.Footprint != null)
            {
                var pads = new HashSet<string>(item.Footprint.Pads.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
                var pins = PartPins(item.Symbol);
                var missing = pins.Where(p => p.ElectricalType != "no_connect" && !pads.Contains(PadOf(item, p))).Select(p => p.Number).ToList();
                if (missing.Count > 0) log.Error($"'{item.Footprint.Id}' kılıfında bu bacakların pad'i yok: {string.Join(", ", missing)} (pad eşlemesini düzeltin).", item: item.Name);
                var used = new HashSet<string>(pins.Select(p => PadOf(item, p)), StringComparer.OrdinalIgnoreCase);
                var spare = item.Footprint.Pads.Select(p => p.Name).Where(n => n.Length > 0 && !used.Contains(n)).Distinct().ToList();
                if (spare.Count > 0) log.Info($"kılıfta bacağa bağlanmayan pad(ler): {string.Join(", ", spare.Take(12))}{(spare.Count > 12 ? " …" : string.Empty)} (montaj/ısı pedi olabilir).", item: item.Name);
            }
            else
            {
                log.Warn("kılıf bulunamadı: " + (string.IsNullOrEmpty(item.Symbol.FootprintRef) ? "sembolde Footprint özelliği yok" : $"'{item.Symbol.FootprintRef}' içe aktarılan dosyalarda yok") + "; .pretty klasörünü de seçin.", item: item.Name);
            }

            if (item.Footprint != null && item.FootprintEmbedded)
            {
                part.Footprint = CustomFootprint.From(item.Footprint);
                part.Parameters["footprint"] = item.Footprint.Id;
            }

            if (Load3DModels && item.Footprint != null) Attach3D(part, item.Footprint, log, item.Name);
            return part;
        }

        private static CustomPart Clone(CustomPart p) => new CustomPart
        {
            Id = p.Id, Name = p.Name, BasePartId = p.BasePartId, Category = p.Category, Description = p.Description, Author = p.Author,
            Manufacturer = p.Manufacturer, Datasheet = p.Datasheet, Parameters = new Dictionary<string, string>(p.Parameters, StringComparer.OrdinalIgnoreCase),
            Footprint = p.Footprint, Model3D = p.Model3D, Definition = p.Definition?.Clone()
        };

        private CustomPart? OwnPinsPart(KiCadImportItem item, PartLibrary library, ImportLog log)
        {
            var sym = item.Symbol;
            var pins = PartPins(sym);
            if (pins.Count == 0)
            {
                log.Error("sembolün bacağı yok.", item: item.Name);
                return null;
            }

            double cx = sym.HasBounds ? (sym.MinX + sym.MaxX) / 2 : 0;
            var spec = new UserPartSpec
            {
                RefPrefix = Regex.Match(sym.Reference ?? "U", "^[A-Za-z]+").Value is { Length: > 0 } pre ? pre : "U",
                Symbol = UserSymbolStyle.Box,
                BodyLabel = sym.Value.Length > 0 && sym.Value != "~" ? sym.Value : sym.Name,
                Source = "KiCad " + sym.FullName,
                Keywords = sym.Keywords
            };
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var byNumber = new Dictionary<string, string>(StringComparer.Ordinal);
            // Top to bottom on each side (KiCad Y points up).
            foreach (var p in pins.OrderByDescending(p => p.Y).ThenBy(p => p.X))
            {
                string name = p.CleanName.Length > 0 ? p.CleanName : p.Number;
                if (!names.Add(name))
                {
                    name = name + "_" + p.Number;
                    names.Add(name);
                }

                var type = UserPartSpec.ParseKiCadType(p.ElectricalType);
                PinSide side = Math.Abs(p.Angle % 360) < 1 ? PinSide.Left
                    : Math.Abs(p.Angle % 360 - 180) < 1 || Math.Abs(p.Angle % 360 + 180) < 1 ? PinSide.Right
                    : (p.X < cx ? PinSide.Left : PinSide.Right);
                if (!sym.HasBounds && (Math.Abs(p.Angle - 90) < 1 || Math.Abs(p.Angle - 270) < 1)) side = UserPartSpec.DefaultSide(type);
                spec.Pins.Add(new UserPin { Name = name, Pad = PadOf(item, p), Type = type, Side = side, Description = p.Hidden ? "gizli bacak" : string.Empty });
                byNumber[p.Number] = name;
            }

            // Keep the automatic model when there is one: map its pins through the KiCad numbers.
            var auto = item.AutoPart;
            if (auto != null && item.AutoStatus == KiCadImportStatus.Mapped && library.Find(auto.BasePartId) is { Build: not null } baseDef && auto.Parameters.TryGetValue("pinmap", out var pinmap))
            {
                spec.Model.Kind = UserModelKind.BuiltIn;
                spec.Model.BuiltInPartId = baseDef.Id;
                foreach (var e in pinmap.Split(','))
                {
                    int c = e.LastIndexOf(':');
                    if (c <= 0) continue;
                    string modelPin = e.Substring(0, c).Trim(), num = e.Substring(c + 1).Trim();
                    if (byNumber.TryGetValue(num, out var userPin)) spec.Model.PinMap[modelPin] = userPin;
                }

                foreach (var kv in auto.Parameters.Where(kv => kv.Key != "pinmap" && kv.Key != "footprint" && kv.Key != "pinout"))
                {
                    var pd = baseDef.Parameters.FirstOrDefault(x => x.Key == kv.Key);
                    spec.Parameters.Add(new UserParameter { Key = kv.Key, Label = pd?.Label ?? kv.Key, Value = kv.Value, Unit = pd?.Unit ?? string.Empty });
                }

                log.Info($"KiCad bacaklarıyla parça; simülasyon modeli '{baseDef.Name}' ({spec.Model.PinMap.Count} bacak eşlendi).", item: item.Name);
            }
            else
            {
                spec.Model.Kind = UserModelKind.None;
                log.Info($"KiCad bacaklarıyla parça ({spec.Pins.Count} bacak); uygun simülasyon modeli yok, bacaklar yalnız net taşır. Model Parça oluşturucuda eklenebilir.", item: item.Name);
            }

            string value = sym.Value;
            bool valueDiffers = !string.IsNullOrWhiteSpace(value) && value != "~" && !string.Equals(value, sym.Name, StringComparison.OrdinalIgnoreCase);
            string id = "kicad-" + Regex.Replace((sym.Library + "-" + sym.Name).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return new CustomPart
            {
                Id = id,
                Name = valueDiffers ? sym.Name + " (" + value + ")" : sym.Name,
                BasePartId = "udef-" + id,
                Category = string.IsNullOrEmpty(sym.Library) ? "KiCad" : "KiCad · " + sym.Library,
                Description = string.IsNullOrEmpty(sym.Description) ? "KiCad " + sym.FullName : sym.Description,
                Datasheet = sym.Datasheet == "~" ? string.Empty : sym.Datasheet,
                Manufacturer = sym.Property("Manufacturer") ?? sym.Property("MF") ?? string.Empty,
                Author = "KiCad içe aktarımı",
                Definition = spec,
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }

        private readonly Dictionary<string, Design.PartModel3D?> _modelCache = new Dictionary<string, Design.PartModel3D?>(StringComparer.OrdinalIgnoreCase);

        private void Attach3D(CustomPart part, Footprint fp, ImportLog log, string item)
        {
            var info = KiCadLibraryImport.GetFootprintInfo(fp);
            var r = info?.Models.FirstOrDefault(m => !m.Hidden) ?? info?.Models.FirstOrDefault();
            if (r == null) return;
            string? path = PartModelFiles.ResolveKiCadModel(r.Path, ModelFolders, out string note);
            if (path == null)
            {
                log.Warn("3B model: " + note, item: item);
                return;
            }

            if (PartModelFiles.IsCad(path))
            {
                log.Warn("3B model: " + note, item: item);
                return;
            }

            if (!_modelCache.TryGetValue(path, out var model))
            {
                try
                {
                    var mesh = PartModelFiles.LoadMesh(path, VrmlUnits.KiCad);
                    model = PartModelFiles.ToPartModel(mesh, Path.GetFileName(path), recenter: false);
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is FormatException || ex is NotSupportedException || ex is UnauthorizedAccessException)
                {
                    log.Warn($"3B model '{Path.GetFileName(path)}' okunamadı: {ex.Message}", item: item);
                    model = null;
                }

                _modelCache[path] = model;
            }

            if (model == null) return;
            var copy = new Design.PartModel3D { Mesh = model.Mesh, Name = model.Name, Color = model.Color };
            PartModelFiles.ApplyKiCadPlacement(copy, r);
            part.Model3D = copy;
            if (note.Length > 0) log.Info("3B model: " + note, item: item);
        }

        /// <summary>Builds every selected item into a library file; problems per item go to <paramref name="log"/>.</summary>
        public PartLibraryFile Commit(PartLibrary library, ImportLog log)
        {
            var file = new PartLibraryFile
            {
                Name = LibraryName,
                Author = "KiCad içe aktarımı",
                Version = Version,
                Description = $"KiCad: {Items.Count} sembol, {Footprints.Count} footprint ({string.Join(", ", Paths.Select(Path.GetFileName))})"
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

        /// <summary>English KiCad importer messages, the frequent ones in Turkish.</summary>
        public static string TranslateKiCad(string message)
        {
            (string Pattern, string Replacement)[] table =
            {
                (@"^(.*): not found\.$", "$1: bulunamadı."),
                (@"^(.*): not a KiCad library file \(.*\)\.$", "$1: KiCad kütüphane dosyası değil (.kicad_sym, .kicad_mod veya .pretty klasörü bekleniyor)."),
                (@"^(.*): KiCad 5 legacy symbol libraries \(\.lib\) are not supported.*$", "$1: KiCad 5 eski sembol kütüphanesi (.lib) desteklenmiyor; KiCad 6+ ile açıp kaydederek .kicad_sym üretin."),
                (@"^footprint '(.*)' has the id of a built-in footprint; imported as '(.*)'\.$", "'$1' footprint'i yerleşik bir kılıfla aynı adda; '$2' olarak alındı."),
                (@"^footprint '(.*)' imported twice; the later one \((.*)\) is used\.$", "'$1' footprint'i iki kez geldi; sonraki ($2) kullanıldı."),
                (@"no courtyard graphics; courtyard derived from the pad outline \+ 0\.25 mm\.", "courtyard çizimi yok; pad sınırı + 0.25 mm kullanıldı."),
                (@"custom shape imported as its bounding rectangle\.", "özel şekilli pad sınırlayan dikdörtgen olarak alındı."),
                (@"trapezoid imported as its bounding rectangle\.", "yamuk pad sınırlayan dikdörtgen olarak alındı."),
                (@"drill offset ignored \(copper and hole share one centre here\)\.", "delik kaçıklığı yok sayıldı (bakır ve delik aynı merkezde)."),
                (@"^(.*): Not a KiCad footprint: .*$", "$1: KiCad footprint dosyası değil."),
            };
            foreach (var (p, r) in table)
            {
                if (Regex.IsMatch(message, p)) return Regex.Replace(message, p, r);
            }

            return message;
        }
    }
}
