using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RobotWin.Studio.Editor.Completion
{
    /// <summary>An Arduino library folder found on disk.</summary>
    public sealed class LocalLibrary
    {
        public LocalLibrary(string name, string version, string folder, string origin, IReadOnlyList<string> headers)
        {
            Name = name;
            Version = version;
            Folder = folder;
            Origin = origin;
            Headers = headers;
        }

        /// <summary>From library.properties, or the folder name.</summary>
        public string Name { get; }

        public string Version { get; }
        public string Folder { get; }

        /// <summary>"çekirdek" (comes with the board package), "kurulu" (sketchbook / RobotWin folder), "HAL" (ESP32 simulation).</summary>
        public string Origin { get; }

        /// <summary>Headers a sketch can include from it (file names).</summary>
        public IReadOnlyList<string> Headers { get; }
    }

    /// <summary>
    /// [code] Where a board's headers are: for the AVR boards the arduino-cli core (cores/arduino, the variant's
    /// pins_arduino.h, the core's own libraries) and the installed libraries (sketchbook, RobotWin's library folder); for
    /// ESP32 the simulation's HAL headers, the only ones its native build sees. Scans are cached by file and time stamp.
    /// </summary>
    public sealed class ArduinoHeaders
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<string, string?>? _resource;
        private static readonly Dictionary<string, (DateTime Stamp, HeaderInfo Info)> Cache = new Dictionary<string, (DateTime, HeaderInfo)>(StringComparer.OrdinalIgnoreCase);

        public ArduinoHeaders(bool esp32, IEnumerable<string> libraryRoots, string? arduinoData = null, IReadOnlyDictionary<string, string>? halHeaders = null)
        {
            Esp32 = esp32;
            var libs = new List<LocalLibrary>();
            if (esp32)
            {
                if (halHeaders != null)
                {
                    foreach (var kv in halHeaders) _files[kv.Key.Replace('\\', '/')] = "res:" + kv.Key;
                    _resource = name => halHeaders.TryGetValue(name, out var t) ? t : halHeaders.FirstOrDefault(kv => kv.Key.Replace('\\', '/') == name).Value;
                    libs.Add(new LocalLibrary("ESP32 simülasyon katmanı (HAL)", "", "", "HAL",
                        halHeaders.Keys.Select(k => k.Replace('\\', '/')).Where(k => !k.StartsWith("rw_", StringComparison.Ordinal) && !k.Contains("/") && k.EndsWith(".h", StringComparison.Ordinal)).OrderBy(k => k).ToList()));
                }
            }
            else
            {
                string data = arduinoData ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arduino15");
                string avr = Path.Combine(data, "packages", "arduino", "hardware", "avr");
                string? core = Directory.Exists(avr) ? Directory.GetDirectories(avr).OrderBy(VersionKey).LastOrDefault() : null;
                if (core != null)
                {
                    CoreFound = true;
                    AddFolder(Path.Combine(core, "cores", "arduino"), recursive: false);
                    AddFolder(Path.Combine(core, "variants", "standard"), recursive: false);
                    string coreLibs = Path.Combine(core, "libraries");
                    if (Directory.Exists(coreLibs))
                        foreach (var d in Directory.GetDirectories(coreLibs)) libs.Add(AddLibrary(d, "çekirdek"));
                }
            }

            foreach (var root in libraryRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (var d in Directory.GetDirectories(root))
                    if (!Path.GetFileName(d).StartsWith(".", StringComparison.Ordinal)) libs.Add(AddLibrary(d, "kurulu"));   // [libs] ESP32 builds compile them too
            Libraries = libs;
        }

        public bool Esp32 { get; }

        /// <summary>The AVR core of arduino-cli was found (the API comes from the real headers).</summary>
        public bool CoreFound { get; }

        public IReadOnlyList<LocalLibrary> Libraries { get; }

        /// <summary>Headers a sketch can include.</summary>
        public IEnumerable<string> Includable =>
            Libraries.SelectMany(l => l.Headers)
                .Concat(Esp32 ? Array.Empty<string>() : new[] { "Arduino.h", "avr/pgmspace.h", "avr/io.h", "avr/interrupt.h", "util/delay.h" })
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(h => h, StringComparer.OrdinalIgnoreCase);

        private static Version VersionKey(string dir) => Version.TryParse(Path.GetFileName(dir), out var v) ? v : new Version(0, 0);

        private void AddFolder(string dir, bool recursive)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*.h", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                string rel = Path.GetRelativePath(dir, f).Replace('\\', '/');
                if (!_files.ContainsKey(rel)) _files[rel] = f;
            }
        }

        private LocalLibrary AddLibrary(string folder, string origin)
        {
            var lib = Describe(folder, origin);
            string src = Path.Combine(folder, "src");
            AddFolder(Directory.Exists(src) ? src : folder, recursive: true);
            return lib;
        }

        /// <summary>Name, version and includable headers of a library folder (1.5 layout with src/, or the old flat one).</summary>
        public static LocalLibrary Describe(string folder, string origin)
        {
            string name = Path.GetFileName(folder), version = string.Empty;
            string props = Path.Combine(folder, "library.properties");
            var headers = new List<string>();
            if (File.Exists(props))
            {
                foreach (var line in File.ReadAllLines(props))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    if (k == "name") name = v;
                    else if (k == "version") version = v;
                    else if (k == "includes") headers.AddRange(v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
                }
            }

            if (headers.Count == 0)
            {
                string src = Path.Combine(folder, "src");
                string dir = Directory.Exists(src) ? src : folder;
                if (Directory.Exists(dir)) headers.AddRange(Directory.GetFiles(dir, "*.h").Select(Path.GetFileName).Where(h => h != null).Cast<string>());
            }

            return new LocalLibrary(name, version, folder, origin, headers);
        }

        /// <summary>The scanned declarations of a header (null when it is not on this board's include path).</summary>
        public HeaderInfo? Load(string header)
        {
            header = header.Replace('\\', '/');
            if (!_files.TryGetValue(header, out var where))
            {
                // A relative include resolved against the wrong folder: fall back to the file name alone.
                string file = header.Contains('/') ? header.Substring(header.LastIndexOf('/') + 1) : header;
                where = _files.FirstOrDefault(kv => kv.Key.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase) || kv.Key.Equals(file, StringComparison.OrdinalIgnoreCase)).Value;
                if (where == null) return null;
            }

            try
            {
                if (where.StartsWith("res:", StringComparison.Ordinal))
                {
                    string? text = _resource?.Invoke(where.Substring(4));
                    return text == null ? null : CppHeaderScanner.Scan(text, header);
                }

                var stamp = File.GetLastWriteTimeUtc(where);
                lock (Cache)
                {
                    if (Cache.TryGetValue(where, out var hit) && hit.Stamp == stamp) return hit.Info;
                }

                var info = CppHeaderScanner.Scan(File.ReadAllText(where), header);
                lock (Cache) Cache[where] = (stamp, info);
                return info;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>The API a sketch sees: the core (Arduino.h) and the headers it includes.</summary>
        public ApiIndex IndexFor(IEnumerable<string> includes) => ApiIndex.Build(new[] { "Arduino.h" }.Concat(includes), Load);
    }
}
