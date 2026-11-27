using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Formats;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [code] A folder of code in the code page's explorer: a board's sketch (kept inside the project or the world) or a
    /// folder on disk. Paths are relative with '/'; the main file is the sketch's .ino.
    /// </summary>
    public abstract class CodeFolder
    {
        public abstract string Title { get; }
        public abstract string Subtitle { get; }
        public abstract string MainFile { get; }

        /// <summary>Every file, the main one first.</summary>
        public abstract IReadOnlyList<string> Files { get; }

        /// <summary>Folders that hold no file yet (made with "Yeni klasör").</summary>
        public virtual IReadOnlyList<string> EmptyFolders => Array.Empty<string>();

        public abstract string Read(string path);
        public abstract void Write(string path, string content);
        public abstract void Delete(string path);
        public abstract void Rename(string from, string to);
        public virtual void AddFolder(string path) { }

        /// <summary>The board this folder belongs to (null for a folder on disk).</summary>
        public virtual CodeTarget? Board => null;

        /// <summary>Text files a sketch may hold.</summary>
        public static bool IsCode(string path) =>
            new[] { ".ino", ".h", ".hpp", ".c", ".cpp", ".cc", ".txt", ".md", ".json", ".csv", ".ini", ".properties", ".S" }
                .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        public static string Normalize(string path) =>
            string.Join("/", path.Replace('\\', '/').Split('/').Where(s => s.Length > 0 && s != "." && s != ".."));

        /// <summary>All files as one bundle (what a build and a board get).</summary>
        public FirmwareSource ToFirmware(string target)
        {
            var fw = new FirmwareSource { Target = target, FileName = Path.GetFileName(MainFile), Source = Read(MainFile) };
            foreach (var f in Files.Where(f => f != MainFile && IsCode(f))) fw.Files.Add(new FirmwareFile { Path = f, Content = Read(f) });
            return fw;
        }

        /// <summary>Writes every file into a folder on disk (the main file named after the folder, as the Arduino IDE wants).</summary>
        public void SaveTo(string folder)
        {
            Directory.CreateDirectory(folder);
            foreach (var f in Files)
            {
                string name = f == MainFile ? Path.GetFileName(folder) + Path.GetExtension(MainFile) : f;
                string full = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, Read(f), new UTF8Encoding(false));
            }
        }

        /// <summary>A zip with the sketch in a folder of its name (opens in the Arduino IDE as is).</summary>
        public void ZipTo(string zipPath, string folderName)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var f in Files)
            {
                string name = f == MainFile ? folderName + Path.GetExtension(MainFile) : f;
                var entry = zip.CreateEntry(folderName + "/" + name, CompressionLevel.Optimal);
                using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                w.Write(Read(f));
            }
        }
    }

    /// <summary>
    /// [code] A programmable board the code page can write to: a board of the project's circuit, or one in the open world
    /// (a plain Arduino / ESP32 object or a board design). Any board with an emulator: Uno, Nano (ATmega328P), ESP32.
    /// </summary>
    public sealed class CodeTarget
    {
        public CodeTarget(string key, string title, string where, string fqbn, Func<FirmwareSource?> get, Func<FirmwareSource> create, Action<string?> changed, string? builtinSource)
        {
            Key = key;
            Title = title;
            Where = where;
            Fqbn = fqbn;
            _get = get;
            _create = create;
            _changed = changed;
            BuiltinSource = builtinSource;
        }

        private readonly Func<FirmwareSource?> _get;
        private readonly Func<FirmwareSource> _create;
        private readonly Action<string?> _changed;

        public string Key { get; }

        /// <summary>[mode-badge] The board's parameters (its run mode among them); null when not known.</summary>
        public Dictionary<string, string>? Parameters { get; init; }
        public string Title { get; }

        /// <summary>"proje" or the world's title.</summary>
        public string Where { get; }

        public string Fqbn { get; }
        public bool InWorld => Where != "proje";

        /// <summary>The program it ships with ("builtin:servo-sweep.hex" → its .ino), shown until it has code of its own.</summary>
        public string? BuiltinSource { get; }

        public FirmwareSource? Firmware => _get();
        public FirmwareSource EnsureFirmware() => _get() ?? _create();

        /// <summary>The code (or its build) changed: the document is dirty (status line text optional).</summary>
        public void Changed(string? status = null) => _changed(status);

        public override string ToString() => $"{Title} · {(InWorld ? "dünya: " + Where : "proje")}";
    }

    /// <summary>[code] A board's own sketch: its main source and its other files, stored in the project or the world.</summary>
    public sealed class BoardCode : CodeFolder
    {
        private readonly List<string> _emptyFolders = new List<string>();

        public BoardCode(CodeTarget board) => Target = board;

        public CodeTarget Target { get; }
        public override CodeTarget? Board => Target;
        public override string Title => Target.Title;
        public override string Subtitle => Target.InWorld ? "dünyada: " + Target.Where : "projede";
        private FirmwareSource? Fw => Target.Firmware;
        public override string MainFile => Fw?.FileName is { Length: > 0 } n ? n : "sketch.ino";
        public override IReadOnlyList<string> Files => new[] { MainFile }.Concat(Fw?.Files.Select(f => f.Path) ?? Enumerable.Empty<string>()).ToList();
        public override IReadOnlyList<string> EmptyFolders => _emptyFolders.Where(d => !Files.Any(f => f.StartsWith(d + "/", StringComparison.Ordinal))).ToList();

        public override string Read(string path)
        {
            var fw = Fw;
            if (path == MainFile) return fw != null && (fw.Source.Length > 0 || fw.Files.Count > 0) ? fw.Source : Target.BuiltinSource ?? fw?.Source ?? string.Empty;
            return fw?.Files.FirstOrDefault(f => f.Path == path)?.Content ?? string.Empty;
        }

        public override void Write(string path, string content)
        {
            var fw = Target.EnsureFirmware();
            if (path == MainFile)
            {
                if (fw.Source == content) return;
                fw.Source = content;
            }
            else
            {
                var f = fw.Files.FirstOrDefault(x => x.Path == path);
                if (f == null) fw.Files.Add(new FirmwareFile { Path = path, Content = content });
                else if (f.Content == content) return;
                else f.Content = content;
            }

            Target.Changed(null);
        }

        public override void Delete(string path)
        {
            var fw = Target.EnsureFirmware();
            if (path == MainFile) return;
            fw.Files.RemoveAll(f => f.Path == path || f.Path.StartsWith(path + "/", StringComparison.Ordinal));
            _emptyFolders.RemoveAll(d => d == path || d.StartsWith(path + "/", StringComparison.Ordinal));
            Target.Changed(path + " silindi");
        }

        public override void Rename(string from, string to)
        {
            var fw = Target.EnsureFirmware();
            if (from == MainFile)
            {
                if (Fw?.Source.Length == 0) fw.Source = Read(from);
                fw.FileName = to;
            }
            else
            {
                foreach (var f in fw.Files.Where(f => f.Path == from || f.Path.StartsWith(from + "/", StringComparison.Ordinal)).ToList())
                    f.Path = to + f.Path.Substring(from.Length);
            }

            Target.Changed(from + " → " + to);
        }

        public override void AddFolder(string path)
        {
            if (!_emptyFolders.Contains(path)) _emptyFolders.Add(path);
        }

        /// <summary>Puts another folder's files in place of this board's (uploading a sketch to the board).</summary>
        public void ReplaceWith(CodeFolder other)
        {
            var fw = Target.EnsureFirmware();
            fw.FileName = Path.GetFileName(other.MainFile);
            fw.Source = other.Read(other.MainFile);
            fw.Files.Clear();
            foreach (var f in other.Files.Where(f => f != other.MainFile && IsCode(f))) fw.Files.Add(new FirmwareFile { Path = f, Content = other.Read(f) });
            fw.Hex = null;
            fw.BuiltFromHash = null;
            Target.Changed(null);
        }
    }

    /// <summary>[code] A folder on disk: everything in it (build output folders left out), edited in place.</summary>
    public sealed class DiskCode : CodeFolder
    {
        public DiskCode(string folder) => Folder = Path.GetFullPath(folder);

        public string Folder { get; }
        public override string Title => Path.GetFileName(Folder);
        public override string Subtitle => Folder;

        public override string MainFile
        {
            get
            {
                var inos = Files0().Where(f => f.EndsWith(".ino", StringComparison.OrdinalIgnoreCase) && !f.Contains('/')).ToList();
                return inos.FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), Title, StringComparison.OrdinalIgnoreCase))
                       ?? inos.FirstOrDefault() ?? Files0().FirstOrDefault(f => f.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase)) ?? Title + ".ino";
            }
        }

        private IEnumerable<string> Files0()
        {
            if (!Directory.Exists(Folder)) yield break;
            foreach (var f in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories))
            {
                string rel = Normalize(Path.GetRelativePath(Folder, f));
                if (rel.StartsWith("build/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith(".", StringComparison.Ordinal) || rel.Contains("/.")) continue;
                if (IsCode(rel)) yield return rel;
            }
        }

        public override IReadOnlyList<string> Files
        {
            get
            {
                string main = MainFile;
                var rest = Files0().Where(f => f != main).OrderBy(f => f.Count(c => c == '/')).ThenBy(f => f, StringComparer.OrdinalIgnoreCase);
                return new[] { main }.Concat(rest).ToList();
            }
        }

        public override IReadOnlyList<string> EmptyFolders =>
            Directory.Exists(Folder)
                ? Directory.EnumerateDirectories(Folder, "*", SearchOption.AllDirectories)
                    .Where(d => !Directory.EnumerateFileSystemEntries(d).Any())
                    .Select(d => Normalize(Path.GetRelativePath(Folder, d))).ToList()
                : new List<string>();

        private string Full(string path)
        {
            string full = Path.GetFullPath(Path.Combine(Folder, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)) throw new IOException("Klasörün dışına çıkılamaz: " + path);
            return full;
        }

        public override string Read(string path) => File.Exists(Full(path)) ? File.ReadAllText(Full(path)) : string.Empty;

        public override void Write(string path, string content)
        {
            string full = Full(path);
            if (File.Exists(full) && File.ReadAllText(full) == content) return;
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content, new UTF8Encoding(false));
        }

        public override void Delete(string path)
        {
            string full = Full(path);
            if (Directory.Exists(full)) Directory.Delete(full, true);
            else if (File.Exists(full)) File.Delete(full);
        }

        public override void Rename(string from, string to)
        {
            string a = Full(from), b = Full(to);
            Directory.CreateDirectory(Path.GetDirectoryName(b)!);
            if (Directory.Exists(a)) Directory.Move(a, b);
            else File.Move(a, b);
        }

        public override void AddFolder(string path) => Directory.CreateDirectory(Full(path));
    }
}
