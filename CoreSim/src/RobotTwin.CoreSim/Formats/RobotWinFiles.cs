using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Pcb;

namespace RobotTwin.CoreSim.Formats
{
    /// <summary>
    /// The RobotWin file family. Every format is a ZIP container with a
    /// <c>manifest.json</c> naming its format and version, so files can grow new
    /// sections without breaking older readers.
    /// </summary>
    public static class RobotWinFormats
    {
        public const string AppName = "RobotWin Studio";
        public const string AppVersion = "1.0";

        public const string ProjectExtension = ".rwproj";
        public const string PartExtension = ".rwpart";
        public const string LibraryExtension = ".rwlib";
        public const string RecordingExtension = ".rwsim";
        public const string WorldExtension = ".rwworld";

        public sealed class FileType
        {
            public FileType(string extension, string progId, string description, string format)
            {
                Extension = extension;
                ProgId = progId;
                Description = description;
                Format = format;
            }

            public string Extension { get; }
            public string ProgId { get; }
            public string Description { get; }

            /// <summary>Value of <c>format</c> in the manifest.</summary>
            public string Format { get; }
        }

        public static IReadOnlyList<FileType> All { get; } = new[]
        {
            new FileType(ProjectExtension, "RobotWin.Project", "RobotWin Projesi", "robotwin.project"),
            new FileType(WorldExtension, "RobotWin.World", "RobotWin Dünyası", "robotwin.world"),
            new FileType(PartExtension, "RobotWin.Part", "RobotWin Parçası", "robotwin.part"),
            new FileType(LibraryExtension, "RobotWin.Library", "RobotWin Parça Kütüphanesi", "robotwin.library"),
            new FileType(RecordingExtension, "RobotWin.Recording", "RobotWin Simülasyon Kaydı", "robotwin.recording")
        };

        public static FileType? ForPath(string path) =>
            All.FirstOrDefault(t => string.Equals(Path.GetExtension(path), t.Extension, StringComparison.OrdinalIgnoreCase));

        internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        internal static bool IsZip(string path)
        {
            using var fs = File.OpenRead(path);
            return fs.Length >= 4 && fs.ReadByte() == 'P' && fs.ReadByte() == 'K';
        }

        internal static void WriteText(ZipArchive zip, string name, string text)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            w.Write(text);
        }

        internal static void WriteBytes(ZipArchive zip, string name, byte[] data)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var s = entry.Open();
            s.Write(data, 0, data.Length);
        }

        internal static string? ReadText(ZipArchive zip, string name)
        {
            var entry = zip.GetEntry(name);
            if (entry == null) return null;
            using var r = new StreamReader(entry.Open(), Encoding.UTF8);
            return r.ReadToEnd();
        }

        internal static byte[]? ReadBytes(ZipArchive zip, string name)
        {
            var entry = zip.GetEntry(name);
            if (entry == null) return null;
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        /// <summary>Writes through a temporary file so a crash never leaves a half-written document.</summary>
        internal static void SaveAtomically(string path, Action<ZipArchive> write)
        {
            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full)!;
            Directory.CreateDirectory(dir);
            string tmp = Path.Combine(dir, "." + Path.GetFileName(full) + ".tmp");
            using (var fs = File.Create(tmp))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                write(zip);
            }

            if (File.Exists(full)) File.Replace(tmp, full, null);
            else File.Move(tmp, full);
        }

        internal static Manifest ReadManifest(ZipArchive zip, string expectedFormat, int maxVersion)
        {
            var text = ReadText(zip, "manifest.json") ?? throw new InvalidDataException("Dosyada manifest.json yok; RobotWin dosyası değil.");
            var m = JsonSerializer.Deserialize<Manifest>(text, Json) ?? throw new InvalidDataException("manifest.json okunamadı.");
            if (!string.Equals(m.Format, expectedFormat, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Beklenen '{expectedFormat}', dosya '{m.Format}' içeriyor.");
            if (m.FormatVersion > maxVersion)
                throw new InvalidDataException($"Bu dosya daha yeni bir RobotWin sürümüyle kaydedilmiş (biçim v{m.FormatVersion}).");
            return m;
        }
    }

    /// <summary>Header of every RobotWin container.</summary>
    public sealed class Manifest
    {
        public string Format { get; set; } = string.Empty;
        public int FormatVersion { get; set; } = 1;
        public string App { get; set; } = RobotWinFormats.AppName;
        public string AppVersion { get; set; } = RobotWinFormats.AppVersion;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTime Created { get; set; } = DateTime.UtcNow;
        public DateTime Modified { get; set; } = DateTime.UtcNow;

        /// <summary>Free-form facts for file browsers (part count, boards, vehicle …).</summary>
        public Dictionary<string, string> Summary { get; set; } = new Dictionary<string, string>();
    }

    // ====================================================================== project

    /// <summary>Source code (and its last build) for one programmable board of the project.</summary>
    public sealed class FirmwareSource
    {
        public string BoardRefDes { get; set; } = string.Empty;

        /// <summary>"arduino:avr:uno", "arduino:avr:nano", "esp32-hal" …</summary>
        public string Target { get; set; } = "arduino:avr:uno";

        public string FileName { get; set; } = "sketch.ino";
        public string Source { get; set; } = string.Empty;

        /// <summary>Intel HEX from the last successful build (AVR targets).</summary>
        public string? Hex { get; set; }

        public DateTime? BuiltAt { get; set; }

        /// <summary>Hash of <see cref="Source"/> at the time of the build (detects stale builds).</summary>
        public string? BuiltFromHash { get; set; }

        public bool IsBuildCurrent => Hex != null && BuiltFromHash == HashOf(Source);

        public static string HashOf(string text)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                foreach (char c in text)
                {
                    h ^= c;
                    h *= 1099511628211UL;
                }

                return h.ToString("x16");
            }
        }
    }

    /// <summary>
    /// A <c>.rwproj</c> project: schematic, PCB, mechanical assembly, firmware
    /// sources and builds, project-local parts and a preview picture.
    /// </summary>
    public sealed class ProjectPackage
    {
        public const string Format = "robotwin.project";
        public const int FormatVersion = 2;

        public Manifest Info { get; set; } = new Manifest { Format = Format, FormatVersion = FormatVersion };
        public Schematic Schematic { get; set; } = new Schematic();
        public List<FirmwareSource> Firmware { get; set; } = new List<FirmwareSource>();
        public List<CustomPart> Parts { get; set; } = new List<CustomPart>();

        /// <summary>PNG preview shown by the start page.</summary>
        public byte[]? Preview { get; set; }

        /// <summary>Simulation and view settings (opaque JSON owned by the app).</summary>
        public string? Settings { get; set; }

        /// <summary>True when the file was an old single-JSON project.</summary>
        public bool WasLegacy { get; private set; }

        public FirmwareSource? FirmwareFor(string refDes) =>
            Firmware.FirstOrDefault(f => string.Equals(f.BoardRefDes, refDes, StringComparison.OrdinalIgnoreCase));

        public const string Extension = RobotWinFormats.ProjectExtension;

        /// <summary>The package as .rwproj bytes (worlds embed their boards this way).</summary>
        public byte[] ToBytes()
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) WriteTo(zip);
            return ms.ToArray();
        }

        public static ProjectPackage FromBytes(byte[] data)
        {
            using var ms = new MemoryStream(data, writable: false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            return ReadFrom(zip);
        }

        public void Save(string path) => RobotWinFormats.SaveAtomically(path, WriteTo);

        private void WriteTo(ZipArchive zip)
        {
            var s = Schematic;
            Info.Format = Format;
            Info.FormatVersion = FormatVersion;
            Info.App = RobotWinFormats.AppName;
            Info.AppVersion = RobotWinFormats.AppVersion;
            Info.Modified = DateTime.UtcNow;
            Info.Title = s.Title;
            Info.Summary = BuildSummary();

            // The board and the assembly go into their own entries.
            var board = s.Board;
            var assembly = s.Assembly;
            string schematicJson;
            try
            {
                s.Board = null;
                s.Assembly = null;
                schematicJson = s.ToJson();
            }
            finally
            {
                s.Board = board;
                s.Assembly = assembly;
            }

            {
                RobotWinFormats.WriteText(zip, "manifest.json", JsonSerializer.Serialize(Info, RobotWinFormats.Json));
                RobotWinFormats.WriteText(zip, "schematic.json", schematicJson);
                if (board != null) RobotWinFormats.WriteText(zip, "pcb/board.json", JsonSerializer.Serialize(board, RobotWinFormats.Json));
                if (assembly != null) RobotWinFormats.WriteText(zip, "mechanics/assembly.json", JsonSerializer.Serialize(assembly, RobotWinFormats.Json));
                if (Firmware.Count > 0)
                {
                    var index = new List<FirmwareSource>();
                    foreach (var f in Firmware)
                    {
                        string folder = "firmware/" + Safe(f.BoardRefDes) + "/";
                        RobotWinFormats.WriteText(zip, folder + Safe(f.FileName), f.Source);
                        if (f.Hex != null) RobotWinFormats.WriteText(zip, folder + Path.GetFileNameWithoutExtension(Safe(f.FileName)) + ".hex", f.Hex);
                        index.Add(new FirmwareSource
                        {
                            BoardRefDes = f.BoardRefDes, Target = f.Target, FileName = f.FileName, BuiltAt = f.BuiltAt, BuiltFromHash = f.BuiltFromHash
                        });
                    }

                    RobotWinFormats.WriteText(zip, "firmware/index.json", JsonSerializer.Serialize(index, RobotWinFormats.Json));
                }

                if (Parts.Count > 0) RobotWinFormats.WriteText(zip, "library/parts.json", JsonSerializer.Serialize(Parts, RobotWinFormats.Json));
                if (Settings != null) RobotWinFormats.WriteText(zip, "settings.json", Settings);
                if (Preview != null) RobotWinFormats.WriteBytes(zip, "preview.png", Preview);
            }
        }

        public static ProjectPackage Load(string path)
        {
            if (!RobotWinFormats.IsZip(path))
            {
                // Version 1: the whole project was one schematic JSON.
                var legacy = Schematic.Load(path);
                var p = new ProjectPackage { Schematic = legacy, WasLegacy = true };
                p.Info.Title = legacy.Title;
                p.Info.Created = File.GetCreationTimeUtc(path);
                return p;
            }

            using var zip = ZipFile.OpenRead(path);
            return ReadFrom(zip);
        }

        private static ProjectPackage ReadFrom(ZipArchive zip)
        {
            var pkg = new ProjectPackage { Info = RobotWinFormats.ReadManifest(zip, Format, FormatVersion) };
            pkg.Schematic = Schematic.FromJson(RobotWinFormats.ReadText(zip, "schematic.json") ?? throw new InvalidDataException("Projede schematic.json yok."));
            var board = RobotWinFormats.ReadText(zip, "pcb/board.json");
            if (board != null) pkg.Schematic.Board = JsonSerializer.Deserialize<BoardDocument>(board, RobotWinFormats.Json);
            var assembly = RobotWinFormats.ReadText(zip, "mechanics/assembly.json");
            if (assembly != null) pkg.Schematic.Assembly = JsonSerializer.Deserialize<AssemblyDocument>(assembly, RobotWinFormats.Json);
            var index = RobotWinFormats.ReadText(zip, "firmware/index.json");
            if (index != null)
            {
                foreach (var f in JsonSerializer.Deserialize<List<FirmwareSource>>(index, RobotWinFormats.Json) ?? new List<FirmwareSource>())
                {
                    string folder = "firmware/" + Safe(f.BoardRefDes) + "/";
                    f.Source = RobotWinFormats.ReadText(zip, folder + Safe(f.FileName)) ?? string.Empty;
                    f.Hex = RobotWinFormats.ReadText(zip, folder + Path.GetFileNameWithoutExtension(Safe(f.FileName)) + ".hex");
                    pkg.Firmware.Add(f);
                }
            }

            var parts = RobotWinFormats.ReadText(zip, "library/parts.json");
            if (parts != null) pkg.Parts = JsonSerializer.Deserialize<List<CustomPart>>(parts, RobotWinFormats.Json) ?? new List<CustomPart>();
            pkg.Settings = RobotWinFormats.ReadText(zip, "settings.json");
            pkg.Preview = RobotWinFormats.ReadBytes(zip, "preview.png");
            return pkg;
        }

        /// <summary>Reads only the manifest and preview (fast, for recent-file lists).</summary>
        public static (Manifest Info, byte[]? Preview) Peek(string path)
        {
            if (!RobotWinFormats.IsZip(path))
            {
                var legacy = Schematic.Load(path);
                return (new Manifest { Format = Format, FormatVersion = 1, Title = legacy.Title, Modified = File.GetLastWriteTimeUtc(path) }, null);
            }

            using var zip = ZipFile.OpenRead(path);
            return (RobotWinFormats.ReadManifest(zip, Format, int.MaxValue), RobotWinFormats.ReadBytes(zip, "preview.png"));
        }

        private Dictionary<string, string> BuildSummary()
        {
            var s = Schematic;
            var d = new Dictionary<string, string>
            {
                ["parts"] = s.Parts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["wires"] = s.Wires.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            if (s.Board != null) d["pcb"] = $"{s.Board.Width:0.#}x{s.Board.Height:0.#} mm, {s.Board.Components.Count} parça";
            if (s.Assembly != null && s.Assembly.Kind != VehicleKind.None) d["vehicle"] = s.Assembly.Kind.ToString();
            if (Firmware.Count > 0) d["firmware"] = string.Join(", ", Firmware.Select(f => f.BoardRefDes + ":" + f.Target));
            return d;
        }

        private static string Safe(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in name) sb.Append(invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c);
            return sb.Length == 0 ? "_" : sb.ToString();
        }

    }

    // ====================================================================== parts and libraries

    /// <summary>Pad of a user footprint (serializable form of <see cref="Pad"/>).</summary>
    public sealed class CustomPad
    {
        public string Name { get; set; } = "1";
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 1.6;
        public double Height { get; set; } = 1.6;
        public PadShape Shape { get; set; } = PadShape.Circle;
        public double Drill { get; set; } = 0.8;
        public CopperLayer Layer { get; set; } = CopperLayer.Top;

        /// <summary>Round-rect corner ratio (KiCad roundrect_rratio).</summary>
        public double CornerRatio { get; set; } = 0.25;

        public bool NonPlated { get; set; }
    }

    /// <summary>A footprint drawing line (silkscreen or fab).</summary>
    public sealed class CustomLine
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
    }

    public sealed class CustomCircle
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double R { get; set; }
    }

    /// <summary>
    /// Serializable footprint (user library, <c>.rwfp</c> files, footprints inside parts).
    /// Older files have no graphics lists; their silkscreen is the courtyard rectangle.
    /// </summary>
    public sealed class CustomFootprint
    {
        public const string Extension = ".rwfp";
        public const string Format = "robotwin.footprint";
        public const int FormatVersion = 1;

        public string Id { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<CustomPad> Pads { get; set; } = new List<CustomPad>();
        public double CourtyardMinX { get; set; }
        public double CourtyardMinY { get; set; }
        public double CourtyardMaxX { get; set; }
        public double CourtyardMaxY { get; set; }
        public double Height { get; set; } = 3.0;

        /// <summary>Silkscreen lines; null in files written before the footprint editor.</summary>
        public List<CustomLine>? Silkscreen { get; set; }
        public List<CustomCircle>? SilkCircles { get; set; }
        public List<CustomLine>? FabLines { get; set; }
        public List<FootprintText>? Texts { get; set; }

        public Footprint ToFootprint()
        {
            var fp = new Footprint
            {
                Id = Id, Description = Description, CourtyardMinX = CourtyardMinX, CourtyardMinY = CourtyardMinY,
                CourtyardMaxX = CourtyardMaxX, CourtyardMaxY = CourtyardMaxY, Height = Height
            };
            foreach (var p in Pads)
            {
                fp.Pads.Add(new Pad
                {
                    Name = p.Name, X = p.X, Y = p.Y, Width = p.Width, Height = p.Height, Shape = p.Shape, Drill = p.Drill, Layer = p.Layer,
                    CornerRatio = p.CornerRatio, NonPlated = p.NonPlated
                });
            }

            if (Silkscreen == null && SilkCircles == null)
            {
                // Silkscreen outline from the courtyard.
                fp.Silkscreen.Add(new Segment2(CourtyardMinX, CourtyardMinY, CourtyardMaxX, CourtyardMinY));
                fp.Silkscreen.Add(new Segment2(CourtyardMaxX, CourtyardMinY, CourtyardMaxX, CourtyardMaxY));
                fp.Silkscreen.Add(new Segment2(CourtyardMaxX, CourtyardMaxY, CourtyardMinX, CourtyardMaxY));
                fp.Silkscreen.Add(new Segment2(CourtyardMinX, CourtyardMaxY, CourtyardMinX, CourtyardMinY));
            }
            else
            {
                foreach (var l in Silkscreen ?? new List<CustomLine>()) fp.Silkscreen.Add(new Segment2(l.X1, l.Y1, l.X2, l.Y2));
                foreach (var c in SilkCircles ?? new List<CustomCircle>()) fp.SilkCircles.Add((c.X, c.Y, c.R));
            }

            foreach (var l in FabLines ?? new List<CustomLine>()) fp.FabLines.Add(new Segment2(l.X1, l.Y1, l.X2, l.Y2));
            foreach (var t in Texts ?? new List<FootprintText>()) fp.Texts.Add(new FootprintText { Text = t.Text, X = t.X, Y = t.Y, Size = t.Size, Layer = t.Layer });
            return fp;
        }

        public static CustomFootprint From(Footprint fp) => new CustomFootprint
        {
            Id = fp.Id, Description = fp.Description, CourtyardMinX = fp.CourtyardMinX, CourtyardMinY = fp.CourtyardMinY,
            CourtyardMaxX = fp.CourtyardMaxX, CourtyardMaxY = fp.CourtyardMaxY, Height = fp.Height,
            Pads = fp.Pads.Select(p => new CustomPad
            {
                Name = p.Name, X = p.X, Y = p.Y, Width = p.Width, Height = p.Height, Shape = p.Shape, Drill = p.Drill, Layer = p.Layer,
                CornerRatio = p.CornerRatio, NonPlated = p.NonPlated
            }).ToList(),
            Silkscreen = fp.Silkscreen.Select(s => new CustomLine { X1 = s.X1, Y1 = s.Y1, X2 = s.X2, Y2 = s.Y2 }).ToList(),
            SilkCircles = fp.SilkCircles.Select(c => new CustomCircle { X = c.X, Y = c.Y, R = c.R }).ToList(),
            FabLines = fp.FabLines.Select(s => new CustomLine { X1 = s.X1, Y1 = s.Y1, X2 = s.X2, Y2 = s.Y2 }).ToList(),
            Texts = fp.Texts.Select(t => new FootprintText { Text = t.Text, X = t.X, Y = t.Y, Size = t.Size, Layer = t.Layer }).ToList()
        };

        /// <summary>Writes a footprint file (<c>.rwfp</c>: a zip with manifest.json and footprint.json).</summary>
        public void Save(string path)
        {
            RobotWinFormats.SaveAtomically(path, zip =>
            {
                var m = new Manifest { Format = Format, FormatVersion = FormatVersion, Title = Id, Description = Description };
                m.Summary["pads"] = Pads.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                RobotWinFormats.WriteText(zip, "manifest.json", JsonSerializer.Serialize(m, RobotWinFormats.Json));
                RobotWinFormats.WriteText(zip, "footprint.json", JsonSerializer.Serialize(this, RobotWinFormats.Json));
            });
        }

        public static CustomFootprint Load(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            RobotWinFormats.ReadManifest(zip, Format, FormatVersion);
            var json = RobotWinFormats.ReadText(zip, "footprint.json") ?? throw new InvalidDataException("Footprint dosyasında footprint.json yok.");
            var fp = JsonSerializer.Deserialize<CustomFootprint>(json, RobotWinFormats.Json) ?? throw new InvalidDataException("Footprint okunamadı.");
            if (string.IsNullOrWhiteSpace(fp.Id)) throw new InvalidDataException("Footprint kimliği boş.");
            return fp;
        }
    }

    /// <summary>
    /// A user component (<c>.rwpart</c>): a simulation model from the built-in
    /// catalog with the user's own parameter values, name and category, and
    /// optionally its own PCB footprint.
    /// </summary>
    public sealed class CustomPart
    {
        public const string Format = "robotwin.part";
        public const int FormatVersion = 1;

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>Built-in part definition that provides the electrical model and symbol.</summary>
        public string BasePartId { get; set; } = string.Empty;

        public string Category { get; set; } = "Kullanıcı";
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Datasheet { get; set; } = string.Empty;
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public CustomFootprint? Footprint { get; set; }

        /// <summary>When set, the part is a whole design (sub-circuit + board) used as one module.</summary>
        public ModuleSpec? Module { get; set; }

        /// <summary>The user's own 3D model (from an STL/OBJ/PLY file) shown instead of the generated one.</summary>
        public PartModel3D? Model3D { get; set; }

        /// <summary>Packages a design as a reusable module part.</summary>
        public static CustomPart FromDesign(Schematic design, PartLibrary library, string id, string name, IEnumerable<string>? portNets = null)
        {
            var spec = ModuleBuilder.Create(design, library, portNets);
            return new CustomPart
            {
                Id = id,
                Name = name,
                BasePartId = id,
                Category = "Modüllerim",
                Description = $"{design.Parts.Count} parçalı alt devre, {spec.Ports.Count} pin" + (spec.BoardWidth > 0 ? $", kart {spec.BoardWidth:0.#}×{spec.BoardHeight:0.#} mm" : string.Empty),
                Module = spec
            };
        }

        /// <summary>Adds this part (and its footprint) to a library; returns false if the id already exists.</summary>
        public bool Register(PartLibrary library)
        {
            if (Module != null && library.Find(BasePartId) == null)
            {
                library.Add(ModuleBuilder.ToDefinition(BasePartId, Name, Category, Description, Module));
            }

            if (library.Find(BasePartId) == null)
                throw new InvalidDataException($"'{Name}' parçası bilinmeyen modele dayanıyor: {BasePartId}");
            if (library.FindPreset(Id) != null)
            {
                // Already known (e.g. a project carrying a copy of a library part): still pick up its 3D model.
                if (Model3D != null) PartModels3D.Attach(library, Id, Model3D);
                return false;
            }

            var parameters = new Dictionary<string, string>(Parameters, StringComparer.OrdinalIgnoreCase);
            if (Model3D != null)
            {
                PartModels3D.Register(Id, Model3D);
                parameters[PartModels3D.ParameterKey] = Id;
            }
            if (Footprint != null)
            {
                FootprintLibrary.Register(Footprint.ToFootprint());
                parameters["footprint"] = Footprint.Id;
            }

            library.AddPreset(new PartPreset(Id, Name, BasePartId, Category, Description, parameters));
            return true;
        }

        public static CustomPart FromPreset(PartPreset preset, string newId, string newName)
        {
            var part = new CustomPart
            {
                Id = newId, Name = newName, BasePartId = preset.PartId, Category = "Kullanıcı", Description = preset.Description,
                Parameters = new Dictionary<string, string>(preset.Parameters.ToDictionary(k => k.Key, k => k.Value), StringComparer.OrdinalIgnoreCase)
            };
            return part;
        }

        public void Save(string path)
        {
            RobotWinFormats.SaveAtomically(path, zip =>
            {
                var m = new Manifest { Format = Format, FormatVersion = FormatVersion, Title = Name, Description = Description, Author = Author };
                m.Summary["base"] = BasePartId;
                RobotWinFormats.WriteText(zip, "manifest.json", JsonSerializer.Serialize(m, RobotWinFormats.Json));
                RobotWinFormats.WriteText(zip, "part.json", JsonSerializer.Serialize(this, RobotWinFormats.Json));
            });
        }

        public static CustomPart Load(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            RobotWinFormats.ReadManifest(zip, Format, FormatVersion);
            return JsonSerializer.Deserialize<CustomPart>(RobotWinFormats.ReadText(zip, "part.json") ?? "{}", RobotWinFormats.Json)
                   ?? throw new InvalidDataException("part.json okunamadı.");
        }
    }

    /// <summary>A <c>.rwlib</c> bundle of user parts to share between computers and teams.</summary>
    public sealed class PartLibraryFile
    {
        public const string Format = "robotwin.library";
        public const int FormatVersion = 1;

        public string Name { get; set; } = "Kütüphane";
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<CustomPart> Parts { get; set; } = new List<CustomPart>();

        public void Save(string path)
        {
            RobotWinFormats.SaveAtomically(path, zip =>
            {
                var m = new Manifest { Format = Format, FormatVersion = FormatVersion, Title = Name, Author = Author, Description = Description };
                m.Summary["parts"] = Parts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                RobotWinFormats.WriteText(zip, "manifest.json", JsonSerializer.Serialize(m, RobotWinFormats.Json));
                foreach (var p in Parts) RobotWinFormats.WriteText(zip, "parts/" + p.Id + ".json", JsonSerializer.Serialize(p, RobotWinFormats.Json));
            });
        }

        public static PartLibraryFile Load(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            var m = RobotWinFormats.ReadManifest(zip, Format, FormatVersion);
            var lib = new PartLibraryFile { Name = m.Title, Author = m.Author, Description = m.Description };
            foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("parts/", StringComparison.Ordinal) && e.FullName.EndsWith(".json", StringComparison.Ordinal)))
            {
                var p = JsonSerializer.Deserialize<CustomPart>(RobotWinFormats.ReadText(zip, e.FullName)!, RobotWinFormats.Json);
                if (p != null) lib.Parts.Add(p);
            }

            return lib;
        }
    }

    // ====================================================================== recordings

    public sealed class RecordedChannel
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>"V" or "A".</summary>
        public string Unit { get; set; } = "V";

        public string Color { get; set; } = "#EDEDEF";
        public List<double> Time { get; set; } = new List<double>();
        public List<double> Value { get; set; } = new List<double>();
    }

    public sealed class RecordedEvent
    {
        public double Time { get; set; }
        public string Device { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
    }

    /// <summary>Vehicle state sample (position, attitude, speed).</summary>
    public sealed class PoseSample
    {
        public double T { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Roll { get; set; }
        public double Pitch { get; set; }
        public double Yaw { get; set; }
        public double Speed { get; set; }
    }

    /// <summary>
    /// A <c>.rwsim</c> recording of a run: the project it came from, every probe
    /// waveform, the event log, the serial output and the vehicle trajectory.
    /// Opens again for analysis and exports to CSV.
    /// </summary>
    public sealed class SimulationRecording
    {
        public const string Format = "robotwin.recording";
        public const int FormatVersion = 1;

        public string Title { get; set; } = string.Empty;
        public double Duration { get; set; }
        public DateTime Recorded { get; set; } = DateTime.UtcNow;
        public ProjectPackage? Project { get; set; }
        public List<RecordedChannel> Channels { get; set; } = new List<RecordedChannel>();
        public List<RecordedEvent> Events { get; set; } = new List<RecordedEvent>();
        public List<PoseSample> Trajectory { get; set; } = new List<PoseSample>();
        public string Serial { get; set; } = string.Empty;

        public void Save(string path)
        {
            string? projectTmp = null;
            try
            {
                if (Project != null)
                {
                    projectTmp = Path.Combine(Path.GetTempPath(), "rw-" + Guid.NewGuid().ToString("N") + ".rwproj");
                    Project.Save(projectTmp);
                }

                RobotWinFormats.SaveAtomically(path, zip =>
                {
                    var m = new Manifest { Format = Format, FormatVersion = FormatVersion, Title = Title, Created = Recorded };
                    m.Summary["duration"] = Duration.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    m.Summary["channels"] = Channels.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    RobotWinFormats.WriteText(zip, "manifest.json", JsonSerializer.Serialize(m, RobotWinFormats.Json));
                    foreach (var c in Channels)
                    {
                        var sb = new StringBuilder();
                        sb.Append("# ").Append(c.Name).Append(';').Append(c.Unit).Append(';').Append(c.Color).Append('\n');
                        sb.Append("time_s,value\n");
                        for (int i = 0; i < c.Time.Count; i++)
                        {
                            sb.Append(c.Time[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                                .Append(c.Value[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                        }

                        RobotWinFormats.WriteText(zip, "channels/" + Sanitize(c.Name) + ".csv", sb.ToString());
                    }

                    RobotWinFormats.WriteText(zip, "events.json", JsonSerializer.Serialize(Events, RobotWinFormats.Json));
                    if (Trajectory.Count > 0) RobotWinFormats.WriteText(zip, "trajectory.json", JsonSerializer.Serialize(Trajectory, RobotWinFormats.Json));
                    if (Serial.Length > 0) RobotWinFormats.WriteText(zip, "serial.txt", Serial);
                    if (projectTmp != null) RobotWinFormats.WriteBytes(zip, "project.rwproj", File.ReadAllBytes(projectTmp));
                });
            }
            finally
            {
                if (projectTmp != null && File.Exists(projectTmp)) File.Delete(projectTmp);
            }
        }

        public static SimulationRecording Load(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            var m = RobotWinFormats.ReadManifest(zip, Format, FormatVersion);
            var rec = new SimulationRecording { Title = m.Title, Recorded = m.Created };
            if (m.Summary.TryGetValue("duration", out var d))
                rec.Duration = double.Parse(d, System.Globalization.CultureInfo.InvariantCulture);
            foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("channels/", StringComparison.Ordinal)).OrderBy(e => e.FullName))
            {
                var lines = RobotWinFormats.ReadText(zip, e.FullName)!.Split('\n');
                var c = new RecordedChannel();
                if (lines.Length > 0 && lines[0].StartsWith("# ", StringComparison.Ordinal))
                {
                    var head = lines[0].Substring(2).Split(';');
                    c.Name = head[0];
                    if (head.Length > 1) c.Unit = head[1];
                    if (head.Length > 2) c.Color = head[2];
                }

                foreach (var line in lines.Skip(2))
                {
                    var parts = line.Split(',');
                    if (parts.Length != 2) continue;
                    c.Time.Add(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture));
                    c.Value.Add(double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                }

                rec.Channels.Add(c);
            }

            rec.Events = JsonSerializer.Deserialize<List<RecordedEvent>>(RobotWinFormats.ReadText(zip, "events.json") ?? "[]", RobotWinFormats.Json) ?? new List<RecordedEvent>();
            rec.Trajectory = JsonSerializer.Deserialize<List<PoseSample>>(RobotWinFormats.ReadText(zip, "trajectory.json") ?? "[]", RobotWinFormats.Json) ?? new List<PoseSample>();
            rec.Serial = RobotWinFormats.ReadText(zip, "serial.txt") ?? string.Empty;
            var project = RobotWinFormats.ReadBytes(zip, "project.rwproj");
            if (project != null)
            {
                string tmp = Path.Combine(Path.GetTempPath(), "rw-" + Guid.NewGuid().ToString("N") + ".rwproj");
                try
                {
                    File.WriteAllBytes(tmp, project);
                    rec.Project = ProjectPackage.Load(tmp);
                }
                finally
                {
                    File.Delete(tmp);
                }
            }

            return rec;
        }

        private static string Sanitize(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
            return sb.ToString();
        }
    }
}
