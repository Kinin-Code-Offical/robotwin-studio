using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace RobotTwin.CoreSim.Firmware.Esp32
{
    public enum CompilerSeverity
    {
        Note,
        Warning,
        Error
    }

    /// <summary>A compiler or linker message. Line/column refer to the user's .ino when the message is about sketch code.</summary>
    public sealed class CompilerDiagnostic
    {
        public string File { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
        public CompilerSeverity Severity { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0}({1},{2}): {3} {4}: {5}", File, Line, Column,
                Severity.ToString().ToLowerInvariant(), Code, Message);
    }

    public sealed class Esp32BuildResult
    {
        public bool Success { get; set; }

        /// <summary>The sketch DLL (unique name per build, so an older one can stay loaded).</summary>
        public string? DllPath { get; set; }

        public List<CompilerDiagnostic> Diagnostics { get; } = new List<CompilerDiagnostic>();
        public string Log { get; set; } = string.Empty;

        /// <summary>Whole build (preprocessing + compile + link), excluding a first-time shim library build.</summary>
        public TimeSpan BuildTime { get; set; }

        /// <summary>Time spent building the cached shim library (zero when it came from the cache).</summary>
        public TimeSpan ShimBuildTime { get; set; }

        /// <summary>Time spent capturing the MSVC environment (vcvars64.bat; once per process).</summary>
        public TimeSpan ToolchainSetupTime { get; set; }

        public bool ShimFromCache { get; set; }

        /// <summary>The preprocessed translation unit that was compiled (for inspection).</summary>
        public string GeneratedSource { get; set; } = string.Empty;

        public IEnumerable<CompilerDiagnostic> Errors => Diagnostics.Where(d => d.Severity == CompilerSeverity.Error);
    }

    /// <summary>
    /// Builds an Arduino-ESP32 sketch into a Windows x64 DLL with MSVC against the RoboTwin
    /// HAL shim (native/esp32hal). The shim is compiled once into a static library cached
    /// under <see cref="CacheDirectory"/> (keyed by a hash of its sources and the compiler).
    /// </summary>
    public sealed class Esp32SketchCompiler
    {
        private const string ResourcePrefix = "esp32hal/";
        private const string CommonFlags = "/nologo /O2 /EHsc /std:c++17 /utf-8 /MT /W3 /wd4996 /wd4244 /wd4267 /wd4305 /wd4018 /wd4838 /wd4309 /wd4068 /diagnostics:column";

        private static readonly object ShimGate = new object();

        public Esp32SketchCompiler(string? shimSourceDirectory = null, string? cacheDirectory = null)
        {
            ShimSourceDirectory = shimSourceDirectory;
            CacheDirectory = cacheDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobotWin", "esp32hal-cache");
        }

        /// <summary>Folder with the shim sources (Arduino.h, rw_runtime.cpp...). Null: embedded copy, else searched next to the binaries.</summary>
        public string? ShimSourceDirectory { get; set; }

        public string CacheDirectory { get; set; }

        /// <summary>Overrides MSVC discovery (full path of vcvars64.bat).</summary>
        public string? VcVarsPath { get; set; }

        /// <summary>Keep the generated .cpp and object files next to the DLL (build-* folder).</summary>
        public bool KeepIntermediateFiles { get; set; }

        /// <summary>Extra preprocessor definitions ("NAME" or "NAME=VALUE").</summary>
        public List<string> Defines { get; } = new List<string>();

        /// <summary>True when MSVC (vcvars64.bat) can be found on this machine.</summary>
        public static bool IsToolchainAvailable => Esp32Toolchain.FindVcVars(null) != null;

        /// <summary>True when MSVC is found (at <paramref name="vcvarsPath"/> if given, otherwise automatically).</summary>
        public static bool ToolchainAvailable(string? vcvarsPath) => Esp32Toolchain.FindVcVars(vcvarsPath) != null;

        /// <summary>
        /// Compiles a sketch. <paramref name="sketchSourceOrPath"/> is either the path of an
        /// .ino/.cpp file (other .ino tabs and .cpp/.c files in its folder are included, like
        /// the Arduino IDE) or the sketch source text itself.
        /// </summary>
        public Esp32BuildResult Compile(string sketchSourceOrPath, string outputDir)
        {
            if (sketchSourceOrPath == null) throw new ArgumentNullException(nameof(sketchSourceOrPath));
            if (string.IsNullOrWhiteSpace(outputDir)) throw new ArgumentException("Output directory required.", nameof(outputDir));
            if (sketchSourceOrPath.StartsWith("//@robotwin-sketch", StringComparison.Ordinal))
            {
                // [code] A sketch of several files (Formats.FirmwareSource.Bundle): laid out in a folder, compiled from its main file.
                string dir = Path.Combine(Path.GetFullPath(outputDir), "sketch");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                string? first = null;
                foreach (var (path, content) in Formats.FirmwareSource.Unbundle(sketchSourceOrPath))
                {
                    string full = Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar));
                    if (!Path.GetFullPath(full).StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    File.WriteAllText(full, content, new UTF8Encoding(false));
                    first ??= full;
                }

                if (first != null) return Compile(first, outputDir);
            }

            var result = new Esp32BuildResult();
            var log = new StringBuilder();
            var total = Stopwatch.StartNew();
            string? buildDir = null;
            try
            {
                Directory.CreateDirectory(outputDir);
                outputDir = Path.GetFullPath(outputDir);

                // ---- sources
                var files = new List<SketchSourceFile>();
                var extraSources = new List<string>();
                string? sketchDir = null;
                string sketchName;
                if (LooksLikePath(sketchSourceOrPath) && File.Exists(sketchSourceOrPath))
                {
                    string main = Path.GetFullPath(sketchSourceOrPath);
                    sketchDir = Path.GetDirectoryName(main);
                    sketchName = Path.GetFileNameWithoutExtension(main);
                    files.Add(new SketchSourceFile(main, File.ReadAllText(main)));
                    if (string.Equals(Path.GetExtension(main), ".ino", StringComparison.OrdinalIgnoreCase) && sketchDir != null)
                    {
                        foreach (var tab in Directory.GetFiles(sketchDir, "*.ino").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                        {
                            if (!string.Equals(Path.GetFullPath(tab), main, StringComparison.OrdinalIgnoreCase))
                                files.Add(new SketchSourceFile(Path.GetFullPath(tab), File.ReadAllText(tab)));
                        }

                        extraSources.AddRange(Directory.GetFiles(sketchDir, "*.cpp").Concat(Directory.GetFiles(sketchDir, "*.c"))
                            .Select(Path.GetFullPath).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                        // [code] The Arduino layout: sources under src/ (any depth) belong to the sketch too.
                        string src = Path.Combine(sketchDir, "src");
                        if (Directory.Exists(src))
                            extraSources.AddRange(Directory.GetFiles(src, "*.cpp", SearchOption.AllDirectories).Concat(Directory.GetFiles(src, "*.c", SearchOption.AllDirectories))
                                .Select(Path.GetFullPath).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                    }
                }
                else
                {
                    sketchName = "sketch";
                    files.Add(new SketchSourceFile("sketch.ino", sketchSourceOrPath));
                }

                // ---- toolchain
                var setup = Stopwatch.StartNew();
                string? vcvars = Esp32Toolchain.FindVcVars(VcVarsPath);
                if (vcvars == null)
                {
                    result.Diagnostics.Add(new CompilerDiagnostic
                    {
                        Severity = CompilerSeverity.Error,
                        Code = "RW0001",
                        Message = "MSVC (Visual Studio Build Tools, C++ x64) bulunamadı. Visual Studio Installer'da 'Desktop development with C++' iş yükünü kurun ya da Ayarlar → Derleyiciler → vcvars64.bat yolunu girin."
                    });
                    return result;
                }

                var env = Esp32Toolchain.GetEnvironment(vcvars, log);
                result.ToolchainSetupTime = setup.Elapsed;
                log.AppendLine("MSVC: " + vcvars);

                // ---- shim library (cached)
                var shim = EnsureShim(env, log, result);
                if (shim == null) return result;

                // ---- preprocess
                buildDir = Path.Combine(outputDir, "build-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(buildDir);
                var pre = ArduinoSketchPreprocessor.Process(files);
                result.GeneratedSource = pre.Code;
                string cppPath = Path.Combine(buildDir, SafeName(sketchName) + ".ino.cpp");
                File.WriteAllText(cppPath, pre.Code, new UTF8Encoding(false));

                string dllName = SafeName(sketchName) + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                                 Guid.NewGuid().ToString("N").Substring(0, 6) + ".dll";
                string dllPath = Path.Combine(outputDir, dllName);

                var args = new StringBuilder();
                args.Append(CommonFlags).Append(" /LD");
                args.Append(" /I\"").Append(shim.Value.IncludeDir).Append('"');
                if (sketchDir != null) args.Append(" /I\"").Append(sketchDir).Append('"');
                foreach (var d in Defines) args.Append(" /D\"").Append(d).Append('"');
                args.Append(" /Fo\"").Append(buildDir).Append("\\\\\"");
                args.Append(" \"").Append(cppPath).Append('"');
                foreach (var extra in extraSources) args.Append(" \"").Append(extra).Append('"');
                args.Append(" /Fe\"").Append(dllPath).Append('"');
                args.Append(" /link /NOLOGO /INCREMENTAL:NO /WHOLEARCHIVE:\"").Append(shim.Value.Library).Append('"');
                args.Append(" /IMPLIB:\"").Append(Path.Combine(buildDir, "sketch.lib")).Append('"');
                args.Append(" /PDB:\"").Append(Path.Combine(buildDir, "sketch.pdb")).Append('"');

                var compile = Stopwatch.StartNew();
                int exit = Esp32Toolchain.Run(env, "cl.exe", args.ToString(), buildDir, out string output);
                log.AppendLine("cl " + args);
                log.Append(output);
                log.AppendLine(string.Format(CultureInfo.InvariantCulture, "derleme + bağlama: {0:F2} s (çıkış kodu {1})", compile.Elapsed.TotalSeconds, exit));

                ParseDiagnostics(output, cppPath, files, result.Diagnostics);
                result.Success = exit == 0 && File.Exists(dllPath);
                if (result.Success)
                {
                    result.DllPath = dllPath;
                }
                else if (!result.Errors.Any())
                {
                    result.Diagnostics.Add(new CompilerDiagnostic
                    {
                        Severity = CompilerSeverity.Error,
                        Code = "RW0002",
                        Message = "Derleme başarısız (çıkış kodu " + exit.ToString(CultureInfo.InvariantCulture) + "). Ayrıntı derleme günlüğünde."
                    });
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException ||
                                       ex is System.ComponentModel.Win32Exception)
            {
                result.Success = false;
                result.Diagnostics.Add(new CompilerDiagnostic { Severity = CompilerSeverity.Error, Code = "RW0003", Message = ex.Message });
                log.AppendLine(ex.ToString());
            }
            finally
            {
                result.BuildTime = total.Elapsed - result.ShimBuildTime;
                log.AppendLine(string.Format(CultureInfo.InvariantCulture, "toplam: {0:F2} s (ESP32 katmanı {1}: {2:F2} s, derleyici hazırlığı {3:F2} s)",
                    total.Elapsed.TotalSeconds, result.ShimFromCache ? "önbellekten" : "derlendi", result.ShimBuildTime.TotalSeconds,
                    result.ToolchainSetupTime.TotalSeconds));
                result.Log = log.ToString();
                if (buildDir != null && !KeepIntermediateFiles)
                {
                    try { Directory.Delete(buildDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }

            return result;
        }

        private static bool LooksLikePath(string s) =>
            s.Length < 1024 && s.IndexOf('\n') < 0 && (s.EndsWith(".ino", StringComparison.OrdinalIgnoreCase) ||
                                                         s.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase) ||
                                                         s.IndexOf(Path.DirectorySeparatorChar) >= 0 || s.IndexOf('/') >= 0);

        private static string SafeName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.Length == 0 ? "sketch" : sb.ToString();
        }

        // ------------------------------------------------------------------ shim library

        private (string IncludeDir, string Library)? EnsureShim(IReadOnlyDictionary<string, string> env, StringBuilder log, Esp32BuildResult result)
        {
            var sources = LoadShimSources(out string origin);
            if (sources.Count == 0)
            {
                result.Diagnostics.Add(new CompilerDiagnostic
                {
                    Severity = CompilerSeverity.Error,
                    Code = "RW0004",
                    Message = "ESP32 donanım katmanının kaynakları (native/esp32hal) bulunamadı; uygulamayı yeniden kurun."
                });
                return null;
            }

            string hash = HashShim(sources, env);
            string dir = Path.Combine(CacheDirectory, hash);
            string include = Path.Combine(dir, "include");
            string lib = Path.Combine(dir, "rw_esp32hal.lib");
            log.AppendLine("ESP32 katmanı: " + origin + " -> " + dir);
            if (File.Exists(lib))
            {
                result.ShimFromCache = true;
                return (include, lib);
            }

            var sw = Stopwatch.StartNew();
            lock (ShimGate)
            {
                using (var mutex = new Mutex(false, "Local\\RobotWin.Esp32Hal." + hash))
                {
                    bool owned = false;
                    try
                    {
                        try { owned = mutex.WaitOne(TimeSpan.FromMinutes(5)); } catch (AbandonedMutexException) { owned = true; }
                        if (File.Exists(lib))
                        {
                            result.ShimFromCache = true;
                            return (include, lib);
                        }

                        string temp = dir + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        string tempInclude = Path.Combine(temp, "include");
                        string objDir = Path.Combine(temp, "obj");
                        Directory.CreateDirectory(tempInclude);
                        Directory.CreateDirectory(objDir);
                        foreach (var kv in sources)
                        {
                            string target = Path.Combine(tempInclude, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                            File.WriteAllBytes(target, kv.Value);
                        }

                        string cppList = string.Join(" ", sources.Keys.Where(k => k.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase) && k.IndexOf('/') < 0)
                            .Select(k => "\"" + Path.Combine(tempInclude, k) + "\""));
                        string args = CommonFlags + " /c /MP /I\"" + tempInclude + "\" /Fo\"" + objDir + "\\\\\" " + cppList;
                        int exit = Esp32Toolchain.Run(env, "cl.exe", args, temp, out string output);
                        log.AppendLine("ESP32 katmanı derlemesi: çıkış kodu " + exit.ToString(CultureInfo.InvariantCulture));
                        if (exit != 0)
                        {
                            log.Append(output);
                            ParseDiagnostics(output, null, Array.Empty<SketchSourceFile>(), result.Diagnostics);
                            result.Diagnostics.Add(new CompilerDiagnostic { Severity = CompilerSeverity.Error, Code = "RW0005", Message = "ESP32 HAL katmanı derlenemedi." });
                            return null;
                        }

                        string objs = string.Join(" ", Directory.GetFiles(objDir, "*.obj").Select(o => "\"" + o + "\""));
                        exit = Esp32Toolchain.Run(env, "lib.exe", "/nologo /OUT:\"" + Path.Combine(temp, "rw_esp32hal.lib") + "\" " + objs, temp, out output);
                        if (exit != 0)
                        {
                            log.Append(output);
                            result.Diagnostics.Add(new CompilerDiagnostic { Severity = CompilerSeverity.Error, Code = "RW0005", Message = "ESP32 HAL katmanı arşivlenemedi." });
                            return null;
                        }

                        try { Directory.Delete(objDir, true); } catch (IOException) { }
                        Directory.CreateDirectory(CacheDirectory);
                        try
                        {
                            Directory.Move(temp, dir);
                        }
                        catch (IOException)
                        {
                            // Someone else finished first: use theirs.
                            try { Directory.Delete(temp, true); } catch (IOException) { }
                        }
                    }
                    finally
                    {
                        if (owned) mutex.ReleaseMutex();
                    }
                }
            }

            result.ShimBuildTime = sw.Elapsed;
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "ESP32 katmanı {0:F2} s'de derlendi", sw.Elapsed.TotalSeconds));
            return File.Exists(lib) ? (include, lib) : ((string, string)?)null;
        }

        /// <summary>Shim files keyed by relative path ("Arduino.h", "freertos/task.h").</summary>
        private SortedDictionary<string, byte[]> LoadShimSources(out string origin)
        {
            var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            string? dir = ShimSourceDirectory;
            if (dir == null)
            {
                var asm = typeof(Esp32SketchCompiler).GetTypeInfo().Assembly;
                foreach (string name in asm.GetManifestResourceNames())
                {
                    string normalized = name.Replace('\\', '/');
                    if (!normalized.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;
                    using (var s = asm.GetManifestResourceStream(name))
                    using (var ms = new MemoryStream())
                    {
                        if (s == null) continue;
                        s.CopyTo(ms);
                        files[normalized.Substring(ResourcePrefix.Length)] = ms.ToArray();
                    }
                }

                if (files.Count > 0)
                {
                    origin = "embedded";
                    return files;
                }

                dir = FindShimDirectory();
            }

            origin = dir ?? "(bulunamadı)";
            if (dir == null || !Directory.Exists(dir)) return files;
            foreach (var path in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".h" && ext != ".cpp" && ext != ".hpp") continue;
                string rel = path.Substring(dir.Length).TrimStart('\\', '/').Replace('\\', '/');
                files[rel] = File.ReadAllBytes(path);
            }

            return files;
        }

        private static string? FindShimDirectory()
        {
            var starts = new List<string> { AppContext.BaseDirectory };
            string? asmDir = Path.GetDirectoryName(typeof(Esp32SketchCompiler).GetTypeInfo().Assembly.Location);
            if (!string.IsNullOrEmpty(asmDir)) starts.Add(asmDir!);
            foreach (var start in starts)
            {
                var d = new DirectoryInfo(start);
                while (d != null)
                {
                    foreach (var candidate in new[] { Path.Combine(d.FullName, "native", "esp32hal"), Path.Combine(d.FullName, "CoreSim", "native", "esp32hal") })
                    {
                        if (File.Exists(Path.Combine(candidate, "rw_runtime.cpp"))) return candidate;
                    }

                    d = d.Parent;
                }
            }

            return null;
        }

        private static string HashShim(SortedDictionary<string, byte[]> sources, IReadOnlyDictionary<string, string> env)
        {
            using (var sha = SHA256.Create())
            {
                var ms = new MemoryStream();
                void Add(string s)
                {
                    var b = Encoding.UTF8.GetBytes(s + "\0");
                    ms.Write(b, 0, b.Length);
                }

                Add(CommonFlags);
                Add(env.TryGetValue("VCToolsVersion", out var tools) ? tools : "?");
                Add(env.TryGetValue("VCINSTALLDIR", out var vc) ? vc : "?");
                foreach (var kv in sources)
                {
                    Add(kv.Key);
                    ms.Write(kv.Value, 0, kv.Value.Length);
                }

                byte[] hash = sha.ComputeHash(ms.ToArray());
                return BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        // ------------------------------------------------------------------ diagnostics

        private static readonly Regex DiagnosticLine = new Regex(
            @"^\s*(?<file>.+?)\((?<line>\d+)(?:,(?<col>\d+))?\)\s*:\s*(?<kind>fatal error|error|warning|note)\s+(?<code>[A-Z]+\d+)\s*:\s*(?<msg>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex ToolLine = new Regex(
            @"^\s*(?<file>[^:]*?|.:[^:]*?)\s*:\s*(?<kind>fatal error|error|warning)\s+(?<code>LNK\d+|C\d+|D\d+)\s*:\s*(?<msg>.*)$",
            RegexOptions.Compiled);

        internal static void ParseDiagnostics(string output, string? generatedCpp, IReadOnlyList<SketchSourceFile> files, List<CompilerDiagnostic> into)
        {
            foreach (var raw in output.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                var m = DiagnosticLine.Match(line);
                if (m.Success)
                {
                    string file = m.Groups["file"].Value.Trim();
                    var d = new CompilerDiagnostic
                    {
                        File = MapFile(file, generatedCpp, files),
                        Line = int.Parse(m.Groups["line"].Value, CultureInfo.InvariantCulture),
                        Column = m.Groups["col"].Success ? int.Parse(m.Groups["col"].Value, CultureInfo.InvariantCulture) : 0,
                        Severity = Severity(m.Groups["kind"].Value),
                        Code = m.Groups["code"].Value,
                        Message = m.Groups["msg"].Value.Trim()
                    };
                    into.Add(d);
                    continue;
                }

                m = ToolLine.Match(line);
                if (m.Success)
                {
                    string message = m.Groups["msg"].Value.Trim();
                    string code = m.Groups["code"].Value;
                    if (code == "LNK2019" || code == "LNK2001")
                    {
                        if (message.Contains("void __cdecl setup(void)")) message = "'setup()' tanımlı değil: kodda void setup() işlevi olmalı (" + message + ")";
                        else if (message.Contains("void __cdecl loop(void)")) message = "'loop()' tanımlı değil: kodda void loop() işlevi olmalı (" + message + ")";
                    }

                    into.Add(new CompilerDiagnostic
                    {
                        File = m.Groups["file"].Value.Trim(),
                        Severity = Severity(m.Groups["kind"].Value),
                        Code = code,
                        Message = message
                    });
                }
            }
        }

        private static CompilerSeverity Severity(string kind) =>
            kind == "warning" ? CompilerSeverity.Warning : kind == "note" ? CompilerSeverity.Note : CompilerSeverity.Error;

        private static string MapFile(string file, string? generatedCpp, IReadOnlyList<SketchSourceFile> files)
        {
            string norm = file.Replace("\\\\", "\\");
            foreach (var f in files)
            {
                if (string.Equals(norm, f.Path, StringComparison.OrdinalIgnoreCase)) return f.Path;
                if (norm.EndsWith(f.Path, StringComparison.OrdinalIgnoreCase)) return f.Path;
            }

            if (generatedCpp != null && string.Equals(Path.GetFileName(norm), Path.GetFileName(generatedCpp), StringComparison.OrdinalIgnoreCase))
            {
                return files.Count > 0 ? files[0].Path : norm;
            }

            return norm;
        }
    }

    /// <summary>MSVC discovery (vswhere, then known install folders) and environment capture.</summary>
    internal static class Esp32Toolchain
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Dictionary<string, string>> Environments =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public static string? FindVcVars(string? explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath)) return File.Exists(explicitPath) ? explicitPath : null;
            string? fromEnv = Environment.GetEnvironmentVariable("ROBOTWIN_VCVARS64");
            if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv)) return fromEnv;
            if (Path.DirectorySeparatorChar != '\\') return null;

            foreach (var install in VsWhereInstallations())
            {
                string candidate = Path.Combine(install, "VC", "Auxiliary", "Build", "vcvars64.bat");
                if (File.Exists(candidate) && HasCompiler(install)) return candidate;
            }

            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            {
                if (string.IsNullOrEmpty(root)) continue;
                foreach (var version in new[] { "18", "2022", "2019" })
                {
                    foreach (var edition in new[] { "BuildTools", "Community", "Professional", "Enterprise" })
                    {
                        string install = Path.Combine(root, "Microsoft Visual Studio", version, edition);
                        string candidate = Path.Combine(install, "VC", "Auxiliary", "Build", "vcvars64.bat");
                        if (File.Exists(candidate) && HasCompiler(install)) return candidate;
                    }
                }
            }

            return null;
        }

        private static bool HasCompiler(string install)
        {
            string tools = Path.Combine(install, "VC", "Tools", "MSVC");
            if (!Directory.Exists(tools)) return false;
            return Directory.GetDirectories(tools).Any(v => File.Exists(Path.Combine(v, "bin", "Hostx64", "x64", "cl.exe")));
        }

        private static IEnumerable<string> VsWhereInstallations()
        {
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string vswhere = Path.Combine(string.IsNullOrEmpty(pf86) ? @"C:\Program Files (x86)" : pf86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (!File.Exists(vswhere)) return Array.Empty<string>();
            try
            {
                var psi = new ProcessStartInfo(vswhere,
                    "-products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -sort -property installationPath -format value -utf8")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return Array.Empty<string>();
                    string text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(Directory.Exists).ToList();
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>Runs vcvars64.bat once per process and captures the resulting environment.</summary>
        public static IReadOnlyDictionary<string, string> GetEnvironment(string vcvars, StringBuilder log)
        {
            lock (Gate)
            {
                if (Environments.TryGetValue(vcvars, out var cached)) return cached;
                var psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"\"" + vcvars + "\" >nul 2>&1 && set\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi) ?? throw new InvalidOperationException("cmd.exe başlatılamadı"))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    string text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        env[line.Substring(0, eq)] = line.Substring(eq + 1);
                    }

                    if (!env.ContainsKey("VCToolsInstallDir"))
                    {
                        throw new InvalidOperationException("vcvars64.bat MSVC ortamını kuramadı: " + err.Result);
                    }

                    log.AppendLine("MSVC araçları " + (env.TryGetValue("VCToolsVersion", out var v) ? v : "?"));
                    Environments[vcvars] = env;
                    return env;
                }
            }
        }

        public static string ResolveTool(IReadOnlyDictionary<string, string> env, string exe)
        {
            if (env.TryGetValue("VCToolsInstallDir", out var tools))
            {
                string direct = Path.Combine(tools, "bin", "Hostx64", "x64", exe);
                if (File.Exists(direct)) return direct;
            }

            if (env.TryGetValue("PATH", out var path))
            {
                foreach (var dir in path.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    try
                    {
                        string candidate = Path.Combine(dir.Trim(), exe);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }

            return exe;
        }

        public static int Run(IReadOnlyDictionary<string, string> env, string exe, string arguments, string workingDirectory, out string output)
        {
            var psi = new ProcessStartInfo(ResolveTool(env, exe), arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.Environment.Clear();
            foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
            using (var p = Process.Start(psi) ?? throw new InvalidOperationException(exe + " başlatılamadı"))
            {
                var err = p.StandardError.ReadToEndAsync();
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                output = stdout + err.Result;
                return p.ExitCode;
            }
        }
    }
}
