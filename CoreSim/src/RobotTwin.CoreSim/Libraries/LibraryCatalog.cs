using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RobotTwin.CoreSim.Formats;

namespace RobotTwin.CoreSim.Libraries
{
    // [libs] Registry of installed part libraries (libraries.json in the user library folder):
    // source, version, enabled flag, priority, the parts each one provides and how part-id
    // conflicts are resolved. The loader asks it for the load order and which copy of a
    // conflicting part to accept.

    public enum LibrarySourceKind
    {
        /// <summary>A .rwlib from someone else (or exported earlier).</summary>
        RobotWin,

        /// <summary>The user's own .rwpart files (one virtual library).</summary>
        UserParts,
        KiCad,
        Spice,
        Fritzing,
        Arduino
    }

    public sealed class InstalledLibrary
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public LibrarySourceKind Source { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>Files/folders it was imported from (used by "update").</summary>
        public List<string> SourcePaths { get; set; } = new List<string>();

        /// <summary>File name inside the library folder ("*.rwpart" for the user's own parts; a full folder path for Arduino).</summary>
        public string File { get; set; } = string.Empty;

        public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool Enabled { get; set; } = true;

        /// <summary>Lower loads first (wins id conflicts that have no explicit resolution).</summary>
        public int Priority { get; set; } = 100;

        public List<string> PartIds { get; set; } = new List<string>();

        /// <summary>Part id → display name (for the cross-library search without reading the files).</summary>
        public Dictionary<string, string> PartNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int FootprintCount { get; set; }

        /// <summary>Last write time of <see cref="File"/> when <see cref="PartIds"/> was read.</summary>
        public long Stamp { get; set; }

        public string SourceText => Source switch
        {
            LibrarySourceKind.UserParts => "Kendi parçalarım",
            LibrarySourceKind.KiCad => "KiCad",
            LibrarySourceKind.Spice => "SPICE",
            LibrarySourceKind.Fritzing => "Fritzing",
            LibrarySourceKind.Arduino => "Arduino",
            _ => "RobotWin (.rwlib)"
        };
    }

    public sealed class LibraryConflict
    {
        public string PartId { get; set; } = string.Empty;

        /// <summary>Libraries that provide the id, in load order ("builtin" = the built-in catalog).</summary>
        public List<string> LibraryIds { get; } = new List<string>();

        /// <summary>The library whose copy is used.</summary>
        public string Winner { get; set; } = string.Empty;

        public bool Resolved { get; set; }
    }

    public sealed class LibraryCatalog
    {
        public const string FileName = "libraries.json";
        public const string UserPartsId = "user-parts";
        public const string BuiltInId = "builtin";

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        public string Folder { get; private set; } = string.Empty;
        public List<InstalledLibrary> Libraries { get; set; } = new List<InstalledLibrary>();

        /// <summary>Part id → library id chosen by the user for a conflict.</summary>
        public Dictionary<string, string> Resolutions { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [JsonIgnore]
        public ImportLog Log { get; } = new ImportLog();

        private sealed class Stored
        {
            public int Version { get; set; } = 1;
            public List<InstalledLibrary> Libraries { get; set; } = new List<InstalledLibrary>();
            public Dictionary<string, string> Resolutions { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Loads (or creates) the catalog of <paramref name="folder"/> and synchronises it with the files there. Never throws for bad content.</summary>
        public static LibraryCatalog Load(string folder)
        {
            var c = new LibraryCatalog { Folder = folder };
            string path = Path.Combine(folder, FileName);
            if (System.IO.File.Exists(path))
            {
                try
                {
                    var stored = JsonSerializer.Deserialize<Stored>(System.IO.File.ReadAllText(path), Json);
                    if (stored != null)
                    {
                        c.Libraries = stored.Libraries.Where(l => l != null && !string.IsNullOrWhiteSpace(l.Id)).ToList();
                        c.Resolutions = new Dictionary<string, string>(stored.Resolutions ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
                {
                    string backup = path + ".bozuk-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    try
                    {
                        System.IO.File.Copy(path, backup, true);
                    }
                    catch (IOException)
                    {
                    }

                    c.Log.Error($"kütüphane listesi ({FileName}) okunamadı: {ex.Message}. Yedeği '{Path.GetFileName(backup)}' olarak saklandı; liste dosyalardan yeniden kuruldu.");
                    c.Libraries.Clear();
                }
            }

            c.Sync();
            return c;
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, FileName);
            string tmp = path + ".tmp";
            System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(new Stored { Libraries = Libraries, Resolutions = Resolutions }, Json), new UTF8Encoding(false));
            if (System.IO.File.Exists(path)) System.IO.File.Replace(tmp, path, null);
            else System.IO.File.Move(tmp, path);
        }

        public InstalledLibrary? Find(string id) => Libraries.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>The library a file in the folder belongs to.</summary>
        public InstalledLibrary? OfFile(string path)
        {
            string name = Path.GetFileName(path);
            if (name.EndsWith(RobotWinFormats.PartExtension, StringComparison.OrdinalIgnoreCase)) return Find(UserPartsId);
            return Libraries.FirstOrDefault(l => l.Source != LibrarySourceKind.Arduino && string.Equals(l.File, name, StringComparison.OrdinalIgnoreCase));
        }

        private static long StampOf(string path)
        {
            try
            {
                return System.IO.File.Exists(path) ? System.IO.File.GetLastWriteTimeUtc(path).Ticks : 0;
            }
            catch (IOException)
            {
                return 0;
            }
        }

        /// <summary>Adds entries for files nobody registered (copied by hand, older versions), drops entries whose files are gone, re-reads changed files.</summary>
        public void Sync()
        {
            if (!Directory.Exists(Folder)) return;
            var libs = Directory.EnumerateFiles(Folder, "*" + RobotWinFormats.LibraryExtension).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            var parts = Directory.EnumerateFiles(Folder, "*" + RobotWinFormats.PartExtension).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

            Libraries.RemoveAll(l => l.Source != LibrarySourceKind.Arduino && l.Id != UserPartsId && !System.IO.File.Exists(Path.Combine(Folder, l.File)));
            Libraries.RemoveAll(l => l.Source == LibrarySourceKind.Arduino && !Directory.Exists(l.File));

            foreach (var f in libs)
            {
                var entry = OfFile(f);
                long stamp = StampOf(f);
                if (entry == null)
                {
                    entry = new InstalledLibrary { Id = UniqueId(Path.GetFileNameWithoutExtension(f)), File = Path.GetFileName(f), Source = LibrarySourceKind.RobotWin, InstalledAt = System.IO.File.GetCreationTimeUtc(f) };
                    Libraries.Add(entry);
                }

                if (entry.Stamp != stamp) ReadLibraryFile(entry, f, stamp);
            }

            var user = Find(UserPartsId);
            if (parts.Count > 0)
            {
                if (user == null)
                {
                    user = new InstalledLibrary { Id = UserPartsId, Name = "Kendi parçalarım", Source = LibrarySourceKind.UserParts, File = "*" + RobotWinFormats.PartExtension, Priority = 0, Version = "—" };
                    Libraries.Insert(0, user);
                }

                long stamp = parts.Select(StampOf).DefaultIfEmpty(0).Max() ^ parts.Count;
                if (user.Stamp != stamp)
                {
                    user.PartIds.Clear();
                    user.PartNames.Clear();
                    foreach (var p in parts)
                    {
                        try
                        {
                            var cp = CustomPart.Load(p);
                            user.PartIds.Add(cp.Id);
                            user.PartNames[cp.Id] = cp.Name;
                        }
                        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is JsonException || ex is UnauthorizedAccessException)
                        {
                            Log.Warn($"{Path.GetFileName(p)}: {ex.Message}");
                        }
                    }

                    user.Stamp = stamp;
                    user.UpdatedAt = DateTime.UtcNow;
                }
            }
            else if (user != null)
            {
                Libraries.Remove(user);
            }
        }

        private void ReadLibraryFile(InstalledLibrary entry, string path, long stamp)
        {
            try
            {
                var file = PartLibraryFile.Load(path);
                if (string.IsNullOrWhiteSpace(entry.Name)) entry.Name = string.IsNullOrWhiteSpace(file.Name) ? Path.GetFileNameWithoutExtension(path) : file.Name;
                entry.Version = file.Version;
                if (string.IsNullOrWhiteSpace(entry.Author)) entry.Author = file.Author;
                if (string.IsNullOrWhiteSpace(entry.Description)) entry.Description = file.Description;
                entry.PartIds = file.Parts.Select(p => p.Id).ToList();
                entry.PartNames = file.Parts.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
                entry.FootprintCount = file.Parts.Count(p => p.Footprint != null);
                if (entry.Source == LibrarySourceKind.RobotWin)
                {
                    string a = file.Author ?? string.Empty;
                    if (a.IndexOf("KiCad", StringComparison.OrdinalIgnoreCase) >= 0) entry.Source = LibrarySourceKind.KiCad;
                    else if (a.IndexOf("SPICE", StringComparison.OrdinalIgnoreCase) >= 0) entry.Source = LibrarySourceKind.Spice;
                    else if (a.IndexOf("Fritzing", StringComparison.OrdinalIgnoreCase) >= 0) entry.Source = LibrarySourceKind.Fritzing;
                }

                entry.Stamp = stamp;
                entry.UpdatedAt = DateTime.UtcNow;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                Log.Error($"{Path.GetFileName(path)} okunamadı: {ex.Message}");
                if (string.IsNullOrWhiteSpace(entry.Name)) entry.Name = Path.GetFileNameWithoutExtension(path);
                entry.Stamp = stamp;
            }
        }

        public string UniqueId(string stem)
        {
            var chars = (stem ?? "lib").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-').ToArray();
            string s = string.Join("-", new string(chars).Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries));
            if (s.Length == 0) s = "lib";
            string id = s;
            for (int i = 2; Find(id) != null || id == UserPartsId || id == BuiltInId; i++) id = s + "-" + i.ToString(CultureInfo.InvariantCulture);
            return id;
        }

        /// <summary>Saves an imported library file into the folder and registers it. Returns the entry.</summary>
        public InstalledLibrary Install(PartLibraryFile file, LibrarySourceKind source, IEnumerable<string> sourcePaths, string? replaceId = null)
        {
            Directory.CreateDirectory(Folder);
            var existing = replaceId != null ? Find(replaceId) : null;
            string fileName = existing?.File ?? UniqueFileName(file.Name);
            file.Save(Path.Combine(Folder, fileName));
            var entry = existing ?? new InstalledLibrary { Id = UniqueId(file.Name), InstalledAt = DateTime.UtcNow, Priority = 100 + Libraries.Count };
            entry.Name = file.Name;
            entry.Source = source;
            entry.Version = file.Version;
            entry.Author = file.Author;
            entry.Description = file.Description;
            entry.File = fileName;
            entry.SourcePaths = sourcePaths.Select(Path.GetFullPath).ToList();
            entry.PartIds = file.Parts.Select(p => p.Id).ToList();
            entry.PartNames = file.Parts.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
            entry.FootprintCount = file.Parts.Count(p => p.Footprint != null);
            entry.UpdatedAt = DateTime.UtcNow;
            entry.Stamp = StampOf(Path.Combine(Folder, fileName));
            if (existing == null) Libraries.Add(entry);
            Save();
            return entry;
        }

        /// <summary>Registers an installed Arduino library folder.</summary>
        public InstalledLibrary InstallArduino(ArduinoLibraryInfo info, string sourcePath)
        {
            var entry = Libraries.FirstOrDefault(l => l.Source == LibrarySourceKind.Arduino && string.Equals(l.File, info.Folder, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new InstalledLibrary { Id = UniqueId("arduino-" + info.Name), Source = LibrarySourceKind.Arduino, InstalledAt = DateTime.UtcNow, Priority = 1000 };
                Libraries.Add(entry);
            }

            entry.Name = info.Name;
            entry.Version = info.Version;
            entry.Author = info.Author;
            entry.Description = info.Sentence;
            entry.File = info.Folder;
            entry.SourcePaths = new List<string> { Path.GetFullPath(sourcePath) };
            entry.PartIds = info.Headers.ToList();
            entry.UpdatedAt = DateTime.UtcNow;
            Save();
            return entry;
        }

        private string UniqueFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string stem = new string((name ?? "kutuphane").Select(c => invalid.Contains(c) || c == ':' ? '_' : c).ToArray()).Trim();
            if (stem.Length == 0) stem = "kutuphane";
            if (stem.Length > 60) stem = stem.Substring(0, 60);
            string file = stem + RobotWinFormats.LibraryExtension;
            for (int i = 2; System.IO.File.Exists(Path.Combine(Folder, file)) || Libraries.Any(l => string.Equals(l.File, file, StringComparison.OrdinalIgnoreCase)); i++)
            {
                file = stem + " (" + i.ToString(CultureInfo.InvariantCulture) + ")" + RobotWinFormats.LibraryExtension;
            }

            return file;
        }

        /// <summary>Removes a library: its file (or Arduino folder) is moved to the "Kaldırılanlar" folder, never deleted outright.</summary>
        public bool Remove(string id)
        {
            var entry = Find(id);
            if (entry == null) return false;
            string trash = Path.Combine(Folder, "Kaldırılanlar", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(trash);
            if (entry.Source == LibrarySourceKind.Arduino)
            {
                if (Directory.Exists(entry.File)) Directory.Move(entry.File, Path.Combine(trash, Path.GetFileName(entry.File.TrimEnd(Path.DirectorySeparatorChar))));
            }
            else if (entry.Id == UserPartsId)
            {
                foreach (var f in Directory.EnumerateFiles(Folder, "*" + RobotWinFormats.PartExtension).ToList()) System.IO.File.Move(f, Path.Combine(trash, Path.GetFileName(f)));
            }
            else
            {
                string f = Path.Combine(Folder, entry.File);
                if (System.IO.File.Exists(f)) System.IO.File.Move(f, Path.Combine(trash, entry.File));
            }

            Libraries.Remove(entry);
            foreach (var key in Resolutions.Where(kv => string.Equals(kv.Value, id, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList()) Resolutions.Remove(key);
            Save();
            return true;
        }

        public void SetEnabled(string id, bool enabled)
        {
            var e = Find(id);
            if (e == null) return;
            e.Enabled = enabled;
            Save();
        }

        /// <summary>Files to load, enabled libraries only, by priority (lower first).</summary>
        public List<string> LoadOrder()
        {
            var result = new List<string>();
            if (!Directory.Exists(Folder)) return result;
            foreach (var lib in Libraries.Where(l => l.Enabled && l.Source != LibrarySourceKind.Arduino).OrderBy(l => l.Priority).ThenBy(l => l.InstalledAt))
            {
                if (lib.Id == UserPartsId) result.AddRange(Directory.EnumerateFiles(Folder, "*" + RobotWinFormats.PartExtension).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                else
                {
                    string f = Path.Combine(Folder, lib.File);
                    if (System.IO.File.Exists(f)) result.Add(f);
                }
            }

            return result;
        }

        /// <summary>False when a conflict on this part id was resolved in favour of another library.</summary>
        public bool Accepts(string file, string partId)
        {
            if (!Resolutions.TryGetValue(partId, out var winner)) return true;
            var lib = OfFile(file);
            return lib == null || string.Equals(lib.Id, winner, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Part ids provided by more than one enabled library (or shadowing a built-in part).</summary>
        public List<LibraryConflict> Conflicts(ICollection<string>? builtInIds = null)
        {
            var by = new Dictionary<string, LibraryConflict>(StringComparer.OrdinalIgnoreCase);
            foreach (var lib in Libraries.Where(l => l.Enabled && l.Source != LibrarySourceKind.Arduino).OrderBy(l => l.Priority).ThenBy(l => l.InstalledAt))
            {
                foreach (var id in lib.PartIds.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!by.TryGetValue(id, out var c))
                    {
                        c = new LibraryConflict { PartId = id };
                        if (builtInIds != null && builtInIds.Contains(id)) c.LibraryIds.Add(BuiltInId);
                        by[id] = c;
                    }

                    c.LibraryIds.Add(lib.Id);
                }
            }

            var list = by.Values.Where(c => c.LibraryIds.Count > 1).ToList();
            foreach (var c in list)
            {
                if (c.LibraryIds[0] == BuiltInId)
                {
                    c.Winner = BuiltInId;
                    c.Resolved = false;
                }
                else if (Resolutions.TryGetValue(c.PartId, out var w) && c.LibraryIds.Contains(w, StringComparer.OrdinalIgnoreCase))
                {
                    c.Winner = w;
                    c.Resolved = true;
                }
                else
                {
                    c.Winner = c.LibraryIds[0];
                }
            }

            return list;
        }

        public void Resolve(string partId, string libraryId)
        {
            Resolutions[partId] = libraryId;
            Save();
        }

        /// <summary>Search across every library (enabled or not): (library, part id, part name).</summary>
        public IEnumerable<(InstalledLibrary Library, string PartId, string Name)> Search(string text)
        {
            var tokens = (text ?? string.Empty).ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var lib in Libraries)
            {
                foreach (var id in lib.PartIds)
                {
                    string name = lib.PartNames.TryGetValue(id, out var n) ? n : id;
                    string hay = (id + " " + name + " " + lib.Name + " " + lib.SourceText).ToLowerInvariant();
                    if (tokens.All(t => hay.Contains(t))) yield return (lib, id, name);
                }
            }
        }
    }
}
