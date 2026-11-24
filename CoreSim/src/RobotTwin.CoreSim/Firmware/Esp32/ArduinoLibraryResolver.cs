using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RobotTwin.CoreSim.Firmware.Esp32
{
    /// <summary>An installed Arduino library a sketch uses: where its headers are and which sources build with it.</summary>
    public sealed class ResolvedLibrary
    {
        public string Name { get; set; } = string.Empty;
        public string Folder { get; set; } = string.Empty;

        /// <summary>Folders added to the include path (src/, or the root and utility/ of the old layout).</summary>
        public List<string> IncludeDirs { get; } = new List<string>();

        /// <summary>The library's .c / .cpp files (examples, extras and tests left out).</summary>
        public List<string> Sources { get; } = new List<string>();

        public override string ToString() => Name + " (" + Folder + ")";
    }

    /// <summary>
    /// [libs] Finds the installed Arduino libraries a sketch includes, the way the Arduino builder does: every
    /// <c>#include</c> the board's core (here the ESP32 HAL) and the sketch itself do not provide is looked up in the
    /// library folders, and the chosen library's own includes are followed in turn. A library whose name matches the
    /// header and whose architectures cover the board wins over others providing the same header.
    /// </summary>
    public static class ArduinoLibraryResolver
    {
        private static readonly Regex Include = new Regex(@"^[ \t]*#[ \t]*include[ \t]*[<""](?<h>[^>""\r\n]+)[>""]", RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly string[] Skipped = { "examples", "extras", "test", "tests", "docs", "doc" };

        /// <summary>The headers a source text includes, in order.</summary>
        public static IEnumerable<string> Includes(string text)
        {
            foreach (Match m in Include.Matches(StripComments(text))) yield return m.Groups["h"].Value.Trim().Replace('\\', '/');
        }

        /// <summary>
        /// The libraries in <paramref name="roots"/> that <paramref name="sketchTexts"/> need, dependencies first
        /// resolved in include order. <paramref name="provided"/> says a header comes from the core or the sketch.
        /// </summary>
        public static List<ResolvedLibrary> Resolve(IEnumerable<string> sketchTexts, IEnumerable<string> roots, Func<string, bool> provided, string architecture = "esp32")
        {
            var candidates = Candidates(roots);
            var chosen = new List<ResolvedLibrary>();
            var seenHeaders = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<string>(sketchTexts.SelectMany(Includes));
            while (pending.Count > 0)
            {
                string header = pending.Dequeue();
                if (!seenHeaders.Add(header) || provided(header)) continue;
                if (chosen.Any(l => l.IncludeDirs.Any(d => FileExistsExact(d, header)))) continue;
                var lib = Pick(candidates, header, architecture);
                if (lib == null) continue;   // a system header (stdint.h, vector …) or one not installed: the compiler says so
                var resolved = Load(lib);
                chosen.Add(resolved);
                foreach (var file in resolved.Sources.Concat(resolved.IncludeDirs.SelectMany(d => SafeFiles(d, "*.h", SearchOption.AllDirectories))))
                {
                    string text;
                    try { text = File.ReadAllText(file); }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }
                    string dir = Path.GetDirectoryName(file)!;
                    foreach (var h in Includes(text))
                        if (!FileExistsExact(dir, h)) pending.Enqueue(h);
                }
            }

            return chosen;
        }

        /// <summary>A short hash of the chosen libraries' files (names, sizes, times): a build is stale once it changes.</summary>
        public static string Stamp(IEnumerable<ResolvedLibrary> libraries)
        {
            var sb = new StringBuilder();
            foreach (var lib in libraries)
            {
                sb.Append(lib.Folder).Append('|');
                foreach (var f in lib.Sources.Concat(lib.IncludeDirs.SelectMany(d => SafeFiles(d, "*.h", SearchOption.AllDirectories))).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.Ordinal))
                {
                    try
                    {
                        var info = new FileInfo(f);
                        sb.Append(f).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks).Append(';');
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            if (sb.Length == 0) return string.Empty;
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())), 0, 6).Replace("-", string.Empty).ToLowerInvariant();
        }

        private sealed class Candidate
        {
            public string Folder = string.Empty;
            public string Name = string.Empty;
            public string Architectures = "*";
            public bool Precompiled;
            public bool Flat;   // the old (1.0) layout or no src/: sources in the root (and utility/)
            public string SourceRoot = string.Empty;
            public int Order;
        }

        private static List<Candidate> Candidates(IEnumerable<string> roots)
        {
            var list = new List<Candidate>();
            int order = 0;
            foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var dir in SafeDirs(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    if (Path.GetFileName(dir).StartsWith(".", StringComparison.Ordinal)) continue;   // an install in progress
                    var c = new Candidate { Folder = dir, Name = Path.GetFileName(dir), Order = order++ };
                    string props = Path.Combine(dir, "library.properties");
                    string src = Path.Combine(dir, "src");
                    if (File.Exists(props))
                    {
                        try
                        {
                            foreach (var line in File.ReadAllLines(props))
                            {
                                int eq = line.IndexOf('=');
                                if (eq <= 0) continue;
                                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                                if (k == "name" && v.Length > 0) c.Name = v;
                                else if (k == "architectures" && v.Length > 0) c.Architectures = v;
                                else if (k == "precompiled") c.Precompiled = v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("full", StringComparison.OrdinalIgnoreCase);
                            }
                        }
                        catch (IOException)
                        {
                        }

                        c.Flat = !Directory.Exists(src);
                    }
                    else
                    {
                        // No library.properties: the old flat layout, unless the headers are only under src/ (a folder copied by hand).
                        c.Flat = !Directory.Exists(src) || SafeFiles(dir, "*.h", SearchOption.TopDirectoryOnly).Any();
                    }

                    c.SourceRoot = c.Flat ? dir : src;
                    list.Add(c);
                }
            }

            return list;
        }

        private static Candidate? Pick(List<Candidate> candidates, string header, string architecture)
        {
            string stem = Path.GetFileNameWithoutExtension(header);
            return candidates.Where(c => FileExistsExact(c.SourceRoot, header))
                .OrderByDescending(c => string.Equals(Path.GetFileName(c.Folder), stem, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Name, stem, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => ArchScore(c.Architectures, architecture))
                .ThenBy(c => c.Order)
                .FirstOrDefault();
        }

        private static int ArchScore(string architectures, string architecture)
        {
            var archs = architectures.Split(',').Select(a => a.Trim()).ToList();
            if (archs.Any(a => a.Equals(architecture, StringComparison.OrdinalIgnoreCase))) return 2;
            return archs.Contains("*") ? 1 : 0;
        }

        private static ResolvedLibrary Load(Candidate c)
        {
            var lib = new ResolvedLibrary { Name = c.Name, Folder = c.Folder };
            lib.IncludeDirs.Add(c.SourceRoot);
            if (c.Precompiled) return lib;   // ships a .a for real targets only; its headers still help
            if (c.Flat)
            {
                lib.Sources.AddRange(Sources(c.SourceRoot, SearchOption.TopDirectoryOnly));
                string utility = Path.Combine(c.SourceRoot, "utility");
                if (Directory.Exists(utility))
                {
                    lib.IncludeDirs.Add(utility);
                    lib.Sources.AddRange(Sources(utility, SearchOption.AllDirectories));
                }
            }
            else
            {
                lib.Sources.AddRange(Sources(c.SourceRoot, SearchOption.AllDirectories));
            }

            return lib;
        }

        private static IEnumerable<string> Sources(string dir, SearchOption option) =>
            SafeFiles(dir, "*.cpp", option).Concat(SafeFiles(dir, "*.c", option))
                .Where(f => !Path.GetRelativePath(dir, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(part => Skipped.Contains(part, StringComparer.OrdinalIgnoreCase)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The file exists with exactly this spelling. Windows paths ignore case, but an include of time.h must not
        /// pick a library's Time.h (the C library's header is meant).
        /// </summary>
        private static bool FileExistsExact(string dir, string relative)
        {
            string path;
            try
            {
                path = Path.GetFullPath(Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }

            if (!File.Exists(path)) return false;
            string? folder = Path.GetDirectoryName(path);
            if (folder == null) return false;
            string name = Path.GetFileName(path);
            return SafeFiles(folder, name, SearchOption.TopDirectoryOnly).Any(f => string.Equals(Path.GetFileName(f), name, StringComparison.Ordinal));
        }

        private static IEnumerable<string> SafeFiles(string dir, string pattern, SearchOption option)
        {
            try
            {
                return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern, option) : Array.Empty<string>();
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private static IEnumerable<string> SafeDirs(string dir)
        {
            try
            {
                return Directory.GetDirectories(dir);
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private static string StripComments(string text)
        {
            if (text.IndexOf("/*", StringComparison.Ordinal) < 0 && text.IndexOf("//", StringComparison.Ordinal) < 0) return text;
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') i++;
                    if (i < text.Length) sb.Append('\n');
                }
                else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int stop = end < 0 ? text.Length : end + 2;
                    for (int k = i; k < stop; k++) if (text[k] == '\n') sb.Append('\n');
                    i = stop - 1;
                }
                else if (c == '"')
                {
                    // A string literal: copied as it is (an include's "file.h" is one too).
                    sb.Append(c);
                    for (i++; i < text.Length && text[i] != '"' && text[i] != '\n'; i++)
                    {
                        sb.Append(text[i]);
                        if (text[i] == '\\' && i + 1 < text.Length) sb.Append(text[++i]);
                    }

                    if (i < text.Length) sb.Append(text[i]);
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
