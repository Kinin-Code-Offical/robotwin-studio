using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RobotTwin.CoreSim.Firmware.Esp32
{
    /// <summary>
    /// [libs] GCC's variable-length arrays in the fast mode's MSVC build. Arduino code declares them often (`unsigned int
    /// uS[it];` in NewPing, `byte outBuffer[send->inf.size + 5];` in MFRC522, IRremote's Pronto decoder): the ESP32's GCC
    /// takes them, MSVC stops with C2131. After a build fails so, each reported line that is such a declaration is rewritten
    /// into an <c>rw::vla</c> (native/esp32hal/rw_vla.h) and the sketch built again: the sketch's own text in memory, a
    /// library in a copy of its folder in the build's folder (the library itself is not touched). Line numbers stay as they
    /// were. A file that takes the array's sizeof, a C file and a static or extern array are left as they are.
    /// </summary>
    internal sealed class Esp32VlaRewrite
    {
        private static readonly Regex Reported = new Regex(@"^\s*(?<file>[^\r\n]+?)\((?<line>\d+),(?<col>\d+)\)\s*:\s*error C2131", RegexOptions.Multiline | RegexOptions.Compiled);

        // TYPE NAME[SIZE]; or TYPE NAME[SIZE], MORE;  (a type of words, :: and a template's <...>, then the name)
        private static readonly Regex Declaration = new Regex(
            @"(?<type>\b(?:(?:const|volatile|unsigned|signed|struct|enum)\s+)*[A-Za-z_][\w:]*(?:\s*<[^;{}()]*?>)?)\s+(?<name>[A-Za-z_]\w*)\s*\[(?<size>[^\[\];]+)\](?<end>\s*[;,])",
            RegexOptions.Compiled);

        private static readonly HashSet<string> NotTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "return", "delete", "new", "else", "case", "goto", "throw", "sizeof", "using", "typedef", "static", "extern", "co_return", "do"
        };

        /// <summary>The lines (1-based) to rewrite, by the original file's path.</summary>
        private readonly Dictionary<string, HashSet<int>> _lines = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The copies made of library folders: the copy's root → the original's.</summary>
        private readonly Dictionary<string, string> _copies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool Any => _lines.Count > 0;

        /// <summary>
        /// The declarations a failed build's log reports, read from their files through <paramref name="textOf"/>; false
        /// when none is new or can be rewritten (the build then stands as it failed).
        /// </summary>
        public bool Learn(string log, Func<string, string?> textOf)
        {
            bool added = false;
            foreach (Match m in Reported.Matches(log))
            {
                string file = Original(m.Groups["file"].Value.Trim());
                if (file.EndsWith(".c", StringComparison.OrdinalIgnoreCase)) continue;
                int line = int.Parse(m.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture);
                int col = int.Parse(m.Groups["col"].Value, System.Globalization.CultureInfo.InvariantCulture);
                string? text = textOf(file);
                if (text == null) continue;
                var lines = text.Split('\n');
                if (line < 1 || line > lines.Length) continue;
                if (RewriteLine(lines[line - 1], col, text) == null) continue;
                if (!_lines.TryGetValue(file, out var set)) _lines[file] = set = new HashSet<int>();
                if (set.Add(line)) added = true;
            }

            return added;
        }

        /// <summary>The text of <paramref name="path"/> with its reported lines rewritten (as it is when it has none).</summary>
        public string Rewrite(string path, string text)
        {
            if (!_lines.TryGetValue(path, out var set)) return text;
            var lines = text.Split('\n');
            foreach (int line in set)
                if (line >= 1 && line <= lines.Length && RewriteLine(lines[line - 1], 0, text) is { } done)
                    lines[line - 1] = done;
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Each library with a rewritten file is built from a copy of its folder under <paramref name="root"/>: its include
        /// folders and sources point there (a header included from a header next to it is found in the copy too).
        /// </summary>
        public void Overlay(IList<ResolvedLibrary> libraries, string root, StringBuilder log)
        {
            for (int i = 0; i < libraries.Count; i++)
            {
                var lib = libraries[i];
                string folder = Path.GetFullPath(lib.Folder).TrimEnd('\\', '/');
                var touched = _lines.Keys.Where(f => f.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToList();
                if (touched.Count == 0) continue;
                string copy = Path.Combine(root, i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Path.GetFileName(folder));
                CopyTree(folder, copy);
                _copies[copy] = folder;
                foreach (string file in touched)
                {
                    string target = copy + file.Substring(folder.Length);
                    File.WriteAllText(target, Rewrite(file, File.ReadAllText(file)), new UTF8Encoding(false));
                }

                string Map(string p)
                {
                    string full = Path.GetFullPath(p);
                    return full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ? copy + full.Substring(folder.Length) : p;
                }

                var includes = lib.IncludeDirs.Select(Map).ToList();
                var sources = lib.Sources.Select(Map).ToList();
                lib.IncludeDirs.Clear();
                lib.IncludeDirs.AddRange(includes);
                lib.Sources.Clear();
                lib.Sources.AddRange(sources);
                log.AppendLine("değişken boyutlu dizi: " + lib.Name + " (" + touched.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " dosya) kopyasından derleniyor");
            }
        }

        /// <summary>A reported path in a copy made by an earlier attempt, as the original's.</summary>
        private string Original(string path)
        {
            foreach (var kv in _copies)
                if (path.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value + path.Substring(kv.Key.Length);
            return path;
        }

        /// <summary>
        /// The line with its variable-length array declaration (the one at <paramref name="col"/>, or the only one) as an
        /// rw::vla; null when the line has none it can rewrite.
        /// </summary>
        internal static string? RewriteLine(string line, int col, string fileText)
        {
            var matches = Declaration.Matches(line).Cast<Match>().Where(m =>
            {
                string type = m.Groups["type"].Value.Trim();
                string first = type.Split(' ', '\t')[0];
                if (NotTypes.Contains(first) || type.StartsWith("rw::vla", StringComparison.Ordinal)) return false;
                string before = line.Substring(0, m.Index);
                if (Regex.IsMatch(before, @"\b(static|extern|typedef)\s*$|[(=]\s*$")) return false;   // not a block-scope declaration
                return !Regex.IsMatch(fileText, @"\bsizeof\s*\(?\s*" + Regex.Escape(m.Groups["name"].Value) + @"\b");
            }).ToList();
            if (matches.Count == 0) return null;
            var m = col > 0 ? matches.FirstOrDefault(x => col - 1 >= x.Index && col - 1 <= x.Index + x.Length) ?? (matches.Count == 1 ? matches[0] : null)
                            : matches[0];
            if (m == null) return null;
            string t = m.Groups["type"].Value.Trim(), name = m.Groups["name"].Value, size = m.Groups["size"].Value.Trim();
            string replacement = "rw::vla<" + t + "> " + name + "((size_t)(" + size + "));" + (m.Groups["end"].Value.Trim() == "," ? " " + t : string.Empty);
            return line.Substring(0, m.Index) + replacement + line.Substring(m.Index + m.Length);
        }

        private static void CopyTree(string from, string to)
        {
            foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith(".", StringComparison.Ordinal)) continue;
                Directory.CreateDirectory(to + dir.Substring(from.Length));
            }

            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".h" && ext != ".hpp" && ext != ".hh" && ext != ".c" && ext != ".cpp" && ext != ".cc" && ext != ".inc" && ext != ".ipp" && ext != ".tpp") continue;
                string target = to + file.Substring(from.Length);
                if (!Directory.Exists(Path.GetDirectoryName(target)!)) continue;   // under a skipped (dot) folder
                File.Copy(file, target, true);
            }
        }
    }
}
