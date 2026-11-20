using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RobotTwin.CoreSim.Libraries
{
    // [libs] Arduino libraries (.zip or folder, 1.5 "src/" layout or legacy flat layout)
    // installed into a libraries folder that the firmware compiler passes to arduino-cli
    // with --libraries. Zip-slip safe, size-limited, never leaves a half-written library.

    public sealed class ArduinoLibraryInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Maintainer { get; set; } = string.Empty;
        public string Sentence { get; set; } = string.Empty;
        public string Paragraph { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Architectures { get; set; } = "*";
        public string Depends { get; set; } = string.Empty;
        public string Includes { get; set; } = string.Empty;

        /// <summary>"1.5" (src/ folder + library.properties) or "1.0" (flat, legacy).</summary>
        public string Layout { get; set; } = "1.5";

        /// <summary>Header files a sketch can #include.</summary>
        public List<string> Headers { get; } = new List<string>();

        public int Examples { get; set; }

        /// <summary>Installed folder (after install / list).</summary>
        public string Folder { get; set; } = string.Empty;

        public ImportLog Log { get; } = new ImportLog();

        public bool SupportsAvr => Architectures.Split(',').Select(a => a.Trim()).Any(a => a == "*" || a.Equals("avr", StringComparison.OrdinalIgnoreCase));

        public string FolderName => ArduinoLibraries.FolderNameFor(Name);
    }

    public static class ArduinoLibraries
    {
        private const long MaxUncompressed = 256L * 1024 * 1024;
        private const int MaxEntries = 50_000;

        public static Dictionary<string, string> ParseProperties(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            return d;
        }

        /// <summary>arduino-cli's folder naming: spaces become '_', other unsafe characters are dropped.</summary>
        public static string FolderNameFor(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.') sb.Append(c);
                else if (c == ' ') sb.Append('_');
            }

            string s = sb.ToString().Trim('.');
            return s.Length == 0 ? "Library" : s;
        }

        private static void Fill(ArduinoLibraryInfo info, Dictionary<string, string> p)
        {
            string Get(string k) => p.TryGetValue(k, out var v) ? v : string.Empty;
            info.Name = Get("name");
            info.Version = Get("version");
            info.Author = Get("author");
            info.Maintainer = Get("maintainer");
            info.Sentence = Get("sentence");
            info.Paragraph = Get("paragraph");
            info.Category = Get("category");
            info.Url = Get("url");
            info.Architectures = p.ContainsKey("architectures") ? Get("architectures") : "*";
            info.Depends = Get("depends");
            info.Includes = Get("includes");
        }

        private static void Check(ArduinoLibraryInfo info)
        {
            if (string.IsNullOrWhiteSpace(info.Name)) info.Log.Error("library.properties içinde 'name' yok.");
            if (!string.IsNullOrEmpty(info.Version) && !Regex.IsMatch(info.Version, @"^\d+(\.\d+){0,3}([-+].*)?$"))
            {
                info.Log.Warn($"sürüm '{info.Version}' anlamsal sürüm (1.2.3) değil.");
            }

            if (info.Headers.Count == 0) info.Log.Error("kütüphanede başlık dosyası (.h) yok; Arduino kütüphanesi değil gibi görünüyor.");
            if (!info.SupportsAvr) info.Log.Warn($"bu kütüphane yalnız şu mimariler için: {info.Architectures}. Uno/Nano (AVR) ile derlenmeyebilir.");
            if (!string.IsNullOrWhiteSpace(info.Depends)) info.Log.Warn("bağımlılıkları da kurulmalı: " + info.Depends);
            if (info.Layout == "1.0") info.Log.Info("eski (1.0) düzen: kaynaklar kök klasörde, library.properties yok.");
        }

        /// <summary>Reads a library folder or .zip without installing it.</summary>
        public static ArduinoLibraryInfo Inspect(string path)
        {
            var info = new ArduinoLibraryInfo();
            try
            {
                if (Directory.Exists(path)) InspectFolder(path, info, out _);
                else if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) InspectZip(path, info, out _);
                else info.Log.Error("bir klasör ya da .zip seçin.");
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                info.Log.Error("okunamadı: " + ex.Message);
            }

            if (!info.Log.HasErrors) Check(info);
            return info;
        }

        private static void InspectFolder(string dir, ArduinoLibraryInfo info, out string root)
        {
            root = dir;
            string? props = Directory.EnumerateFiles(dir, "library.properties", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "examples" + Path.DirectorySeparatorChar))
                .OrderBy(f => f.Count(c => c == Path.DirectorySeparatorChar)).FirstOrDefault();
            if (props != null)
            {
                root = Path.GetDirectoryName(props)!;
                Fill(info, ParseProperties(File.ReadAllText(props)));
            }
            else
            {
                info.Layout = "1.0";
                var header = Directory.EnumerateFiles(dir, "*.h", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(Path.DirectorySeparatorChar + "examples" + Path.DirectorySeparatorChar))
                    .OrderBy(f => f.Count(c => c == Path.DirectorySeparatorChar)).FirstOrDefault();
                if (header != null) root = Path.GetDirectoryName(header)!;
                info.Name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
            }

            string src = Path.Combine(root, "src");
            bool hasSrc = info.Layout == "1.5" && Directory.Exists(src);
            var headerDir = hasSrc ? src : root;
            info.Headers.AddRange(Directory.EnumerateFiles(headerDir, "*.h", hasSrc ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileName(f)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            if (info.Layout == "1.5" && !hasSrc && info.Headers.Count > 0) info.Log.Info("library.properties var ama src/ yok: kaynaklar kök klasörde (izin verilen düzen).");
            string examples = Path.Combine(root, "examples");
            info.Examples = Directory.Exists(examples) ? Directory.EnumerateFiles(examples, "*.ino", SearchOption.AllDirectories).Count() : 0;
        }

        private static void InspectZip(string zipPath, ArduinoLibraryInfo info, out string rootPrefix)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            if (zip.Entries.Count > MaxEntries) throw new InvalidDataException($"zip çok fazla dosya içeriyor ({zip.Entries.Count}).");
            if (zip.Entries.Sum(e => e.Length) > MaxUncompressed) throw new InvalidDataException("zip açıldığında 256 MB'tan büyük.");
            string Norm(string n) => n.Replace('\\', '/');
            bool NotExample(string n) => !Norm(n).Contains("/examples/");
            var props = zip.Entries.Where(e => Norm(e.FullName).EndsWith("library.properties", StringComparison.OrdinalIgnoreCase) && NotExample(e.FullName))
                .OrderBy(e => Norm(e.FullName).Count(c => c == '/')).FirstOrDefault();
            if (props != null)
            {
                string full = Norm(props.FullName);
                rootPrefix = full.Substring(0, full.Length - "library.properties".Length);
                using var r = new StreamReader(props.Open(), Encoding.UTF8);
                Fill(info, ParseProperties(r.ReadToEnd()));
            }
            else
            {
                info.Layout = "1.0";
                var header = zip.Entries.Where(e => Norm(e.FullName).EndsWith(".h", StringComparison.OrdinalIgnoreCase) && NotExample(e.FullName))
                    .OrderBy(e => Norm(e.FullName).Count(c => c == '/')).FirstOrDefault();
                string full = header != null ? Norm(header.FullName) : string.Empty;
                rootPrefix = full.Length > 0 ? full.Substring(0, full.LastIndexOf('/') + 1) : string.Empty;
                string folder = rootPrefix.TrimEnd('/');
                info.Name = folder.Length > 0 ? folder.Substring(folder.LastIndexOf('/') + 1) : Path.GetFileNameWithoutExtension(zipPath);
                info.Name = Regex.Replace(info.Name, "-(master|main)$", string.Empty);
            }

            string prefix = rootPrefix;
            bool hasSrc = info.Layout == "1.5" && zip.Entries.Any(e => Norm(e.FullName).StartsWith(prefix + "src/", StringComparison.OrdinalIgnoreCase));
            foreach (var e in zip.Entries)
            {
                string n = Norm(e.FullName);
                if (!n.StartsWith(prefix, StringComparison.Ordinal) || !n.EndsWith(".h", StringComparison.OrdinalIgnoreCase)) continue;
                string rel = n.Substring(prefix.Length);
                if (hasSrc ? rel.StartsWith("src/", StringComparison.OrdinalIgnoreCase) : rel.IndexOf('/') < 0) info.Headers.Add(Path.GetFileName(rel));
            }

            info.Examples = zip.Entries.Count(e => Norm(e.FullName).StartsWith(prefix + "examples/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".ino", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Installs a library into <paramref name="librariesFolder"/>/&lt;Name&gt;. An existing copy is
        /// replaced only when <paramref name="overwrite"/> is set (update); the new copy is written to a
        /// temporary folder first, so a failure leaves the old one intact.
        /// </summary>
        public static ArduinoLibraryInfo Install(string source, string librariesFolder, bool overwrite)
        {
            var info = new ArduinoLibraryInfo();
            string root;
            string zipPrefix = string.Empty;
            bool isZip = File.Exists(source) && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            try
            {
                if (Directory.Exists(source)) InspectFolder(source, info, out root);
                else if (isZip)
                {
                    InspectZip(source, info, out zipPrefix);
                    root = string.Empty;
                }
                else
                {
                    info.Log.Error("kaynak bulunamadı: bir .zip ya da kütüphane klasörü seçin.");
                    return info;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                info.Log.Error("okunamadı: " + ex.Message);
                return info;
            }

            Check(info);
            if (info.Log.HasErrors) return info;

            Directory.CreateDirectory(librariesFolder);
            string target = Path.Combine(librariesFolder, info.FolderName);
            if (Directory.Exists(target) && !overwrite)
            {
                string old = TryReadVersion(target);
                info.Log.Error($"'{info.Name}' zaten kurulu" + (old.Length > 0 ? $" (sürüm {old})" : string.Empty) + "; güncellemek için Kütüphane yöneticisinden 'Güncelle' kullanın.");
                return info;
            }

            string temp = Path.Combine(librariesFolder, "." + info.FolderName + ".kuruluyor");
            try
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
                Directory.CreateDirectory(temp);
                string tempFull = Path.GetFullPath(temp) + Path.DirectorySeparatorChar;
                if (isZip)
                {
                    using var zip = ZipFile.OpenRead(source);
                    foreach (var e in zip.Entries)
                    {
                        string n = e.FullName.Replace('\\', '/');
                        if (!n.StartsWith(zipPrefix, StringComparison.Ordinal)) continue;
                        string rel = n.Substring(zipPrefix.Length);
                        if (rel.Length == 0 || rel.StartsWith(".git/", StringComparison.Ordinal) || rel.Contains("/.git/")) continue;
                        string dest = Path.GetFullPath(Path.Combine(temp, rel.Replace('/', Path.DirectorySeparatorChar)));
                        if (!dest.StartsWith(tempFull, StringComparison.OrdinalIgnoreCase))
                        {
                            info.Log.Error($"zip güvenli değil: '{e.FullName}' klasör dışına yazmaya çalışıyor; kurulum durduruldu.");
                            throw new InvalidDataException("zip-slip");
                        }

                        if (n.EndsWith("/", StringComparison.Ordinal))
                        {
                            Directory.CreateDirectory(dest);
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        e.ExtractToFile(dest, true);
                    }
                }
                else
                {
                    CopyFolder(root, temp);
                }

                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(temp, target);
                info.Folder = target;
                info.Log.Info($"'{info.Name}' {info.Version} kuruldu: {target} ({info.Headers.Count} başlık, {info.Examples} örnek).");
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                if (ex.Message != "zip-slip") info.Log.Error("kurulamadı: " + ex.Message);
                try
                {
                    if (Directory.Exists(temp)) Directory.Delete(temp, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return info;
        }

        private static void CopyFolder(string from, string to)
        {
            string fromFull = Path.GetFullPath(from).TrimEnd(Path.DirectorySeparatorChar);
            foreach (var dir in Directory.EnumerateDirectories(fromFull, "*", SearchOption.AllDirectories))
            {
                string rel = dir.Substring(fromFull.Length + 1);
                if (rel.Split(Path.DirectorySeparatorChar).Contains(".git")) continue;
                Directory.CreateDirectory(Path.Combine(to, rel));
            }

            foreach (var file in Directory.EnumerateFiles(fromFull, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(fromFull.Length + 1);
                if (rel.Split(Path.DirectorySeparatorChar).Contains(".git")) continue;
                string dest = Path.Combine(to, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, true);
            }
        }

        private static string TryReadVersion(string folder)
        {
            try
            {
                string p = Path.Combine(folder, "library.properties");
                return File.Exists(p) && ParseProperties(File.ReadAllText(p)).TryGetValue("version", out var v) ? v : string.Empty;
            }
            catch (IOException)
            {
                return string.Empty;
            }
        }

        /// <summary>Libraries installed in a libraries folder.</summary>
        public static List<ArduinoLibraryInfo> List(string librariesFolder)
        {
            var list = new List<ArduinoLibraryInfo>();
            if (!Directory.Exists(librariesFolder)) return list;
            foreach (var dir in Directory.EnumerateDirectories(librariesFolder).Where(d => !Path.GetFileName(d).StartsWith(".", StringComparison.Ordinal)).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var info = new ArduinoLibraryInfo();
                try
                {
                    InspectFolder(dir, info, out _);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    info.Log.Error(ex.Message);
                }

                info.Folder = dir;
                if (string.IsNullOrEmpty(info.Name)) info.Name = Path.GetFileName(dir);
                list.Add(info);
            }

            return list;
        }
    }
}
