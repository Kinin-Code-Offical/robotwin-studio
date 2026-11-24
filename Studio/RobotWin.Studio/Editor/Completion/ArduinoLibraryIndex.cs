using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobotWin.Studio.Editor.Completion
{
    /// <summary>A library of the Arduino library index (its newest release).</summary>
    public sealed class IndexedLibrary
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
        [JsonPropertyName("author")] public string Author { get; set; } = string.Empty;
        [JsonPropertyName("sentence")] public string Sentence { get; set; } = string.Empty;
        [JsonPropertyName("paragraph")] public string Paragraph { get; set; } = string.Empty;
        [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
        [JsonPropertyName("website")] public string Website { get; set; } = string.Empty;
        [JsonPropertyName("architectures")] public List<string> Architectures { get; set; } = new List<string>();
        [JsonPropertyName("providesIncludes")] public List<string> ProvidesIncludes { get; set; } = new List<string>();

        /// <summary>Runs on this architecture ("avr", "esp32"): listed for it or for all ("*").</summary>
        public bool Supports(string arch) => Architectures.Count == 0 || Architectures.Any(a => a == "*" || string.Equals(a, arch, StringComparison.OrdinalIgnoreCase));

        /// <summary>The header to include: the one the index names, or the library's name without spaces.</summary>
        public string MainHeader => ProvidesIncludes.FirstOrDefault() ?? new string(Name.Replace(' ', '_').Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray()) + ".h";   // the folder name arduino-cli gives it
    }

    /// <summary>
    /// [code] The Arduino library index arduino-cli keeps (library_index.json, about 10 000 libraries): read once, newest
    /// release of each library kept, searched by name, header, author and description without going online.
    /// </summary>
    public sealed class ArduinoLibraryIndex
    {
        private ArduinoLibraryIndex(List<IndexedLibrary> libraries, DateTime updated)
        {
            Libraries = libraries;
            Updated = updated;
        }

        public IReadOnlyList<IndexedLibrary> Libraries { get; }

        /// <summary>When arduino-cli last downloaded the index.</summary>
        public DateTime Updated { get; }

        public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arduino15", "library_index.json");

        public static ArduinoLibraryIndex? Load(string path)
        {
            if (!File.Exists(path)) return null;
            return Parse(File.ReadAllBytes(path), File.GetLastWriteTime(path));
        }

        public static ArduinoLibraryIndex Parse(byte[] json, DateTime updated)
        {
            var newest = new Dictionary<string, IndexedLibrary>(StringComparer.OrdinalIgnoreCase);
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1 || !reader.ValueTextEquals("libraries")) continue;
                reader.Read();
                if (reader.TokenType != JsonTokenType.StartArray) break;
                while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                {
                    var lib = JsonSerializer.Deserialize<IndexedLibrary>(ref reader);
                    if (lib == null || lib.Name.Length == 0) continue;
                    if (!newest.TryGetValue(lib.Name, out var have) || Compare(lib.Version, have.Version) > 0) newest[lib.Name] = lib;
                }

                break;
            }

            return new ArduinoLibraryIndex(newest.Values.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList(), updated);
        }

        /// <summary>Semantic-version order ("1.10.0" after "1.9.2").</summary>
        public static int Compare(string a, string b)
        {
            var pa = a.Split('.', '-', '+');
            var pb = b.Split('.', '-', '+');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                string x = i < pa.Length ? pa[i] : "0", y = i < pb.Length ? pb[i] : "0";
                bool nx = int.TryParse(x, out int ix), ny = int.TryParse(y, out int iy);
                int c = nx && ny ? ix.CompareTo(iy) : string.CompareOrdinal(x, y);
                if (c != 0) return c;
            }

            return 0;
        }

        /// <summary>Libraries matching every word of the query, best first; only those that run on <paramref name="arch"/>.</summary>
        public List<IndexedLibrary> Search(string query, string arch, int max = 60)
        {
            var words = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLowerInvariant()).ToArray();
            if (words.Length == 0) return new List<IndexedLibrary>();
            string whole = query.Trim();
            var scored = new List<(IndexedLibrary Lib, double Score)>();
            foreach (var lib in Libraries)
            {
                if (!lib.Supports(arch)) continue;
                string name = lib.Name.ToLowerInvariant();
                string headers = string.Join(" ", lib.ProvidesIncludes).ToLowerInvariant();
                string text = (lib.Sentence + " " + lib.Paragraph + " " + lib.Author + " " + lib.Category).ToLowerInvariant();
                double score = 0;
                bool all = true;
                foreach (var w in words)
                {
                    if (name.Contains(w)) score += name.StartsWith(w, StringComparison.Ordinal) ? 6 : 4;
                    else if (headers.Contains(w)) score += 5;
                    else if (text.Contains(w)) score += 1;
                    else
                    {
                        all = false;
                        break;
                    }
                }

                if (!all) continue;
                if (string.Equals(lib.Name, whole, StringComparison.OrdinalIgnoreCase)) score += 20;
                if (lib.ProvidesIncludes.Any(h => string.Equals(h, whole, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(h), whole, StringComparison.OrdinalIgnoreCase))) score += 15;
                if (lib.Author.Contains("Arduino") || lib.Author.Contains("Adafruit")) score += 0.5;   // widely used, well maintained
                score -= name.Length * 0.01;
                scored.Add((lib, score));
            }

            return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Lib.Name, StringComparer.OrdinalIgnoreCase).Take(max).Select(s => s.Lib).ToList();
        }

        /// <summary>Libraries that provide a header (for "Servo.h: No such file or directory").</summary>
        public List<IndexedLibrary> Providing(string header, string arch)
        {
            string stem = Path.GetFileNameWithoutExtension(header);
            return Libraries.Where(l => l.Supports(arch) && (l.ProvidesIncludes.Any(h => string.Equals(h, header, StringComparison.OrdinalIgnoreCase))
                                                            || string.Equals(l.MainHeader, header, StringComparison.OrdinalIgnoreCase)
                                                            || string.Equals(l.Name, stem, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(l => string.Equals(l.Name, stem, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(l => l.Author.Contains("Arduino"))
                .ThenBy(l => l.Name.Length)
                .ToList();
        }
    }
}
