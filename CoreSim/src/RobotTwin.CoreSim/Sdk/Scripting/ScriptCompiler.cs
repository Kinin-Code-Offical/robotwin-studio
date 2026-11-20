using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace RobotTwin.CoreSim.Sdk
{
    /// <summary>Whether C# scripts can be compiled on this machine, and why not.</summary>
    public sealed class CompilerInfo
    {
        public bool Available { get; internal set; }
        public string? DotnetPath { get; internal set; }

        /// <summary>The .NET SDK used for scripts (a 9.x SDK, pinned with global.json).</summary>
        public string? SdkVersion { get; internal set; }

        public IReadOnlyList<string> InstalledSdks { get; internal set; } = Array.Empty<string>();

        /// <summary>Turkish explanation for the UI.</summary>
        public string Explanation { get; internal set; } = string.Empty;
    }

    public sealed class ScriptDiagnostic
    {
        public string File { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsError { get; set; }

        public override string ToString() => $"{File}({Line},{Column}): {(IsError ? "hata" : "uyarı")} {Code}: {Message}";
    }

    public sealed class ScriptBuildResult
    {
        public bool Success { get; internal set; }
        public string? AssemblyPath { get; internal set; }
        public bool FromCache { get; internal set; }
        public List<ScriptDiagnostic> Diagnostics { get; } = new List<ScriptDiagnostic>();
        public string Output { get; internal set; } = string.Empty;
        public TimeSpan Duration { get; internal set; }

        /// <summary>One-line Turkish summary.</summary>
        public string Summary { get; internal set; } = string.Empty;
    }

    /// <summary>
    /// Compiles C# behaviour scripts without bundling a compiler: when the user has the .NET 9
    /// SDK installed, the scripts are written into a small project in a temp folder that
    /// references this RobotTwin.CoreSim.dll, built with <c>dotnet build</c> (no NuGet packages,
    /// restore pointed at an empty local source so nothing is downloaded) and the resulting DLL
    /// is loaded like a plugin. Builds are cached by a hash of the sources and the SDK assembly.
    /// </summary>
    public static class ScriptCompiler
    {
        private static CompilerInfo? _info;
        private static readonly object Gate = new object();

        /// <summary>Root of the build/cache folders (%LOCALAPPDATA%\RobotWinStudio\Scripts by default).</summary>
        public static string WorkRoot { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobotWinStudio", "Scripts");

        /// <summary>Finds dotnet and a .NET 9 SDK (cached; pass refresh to look again after installing).</summary>
        public static CompilerInfo Detect(bool refresh = false)
        {
            lock (Gate)
            {
                if (_info != null && !refresh) return _info;
                var info = new CompilerInfo();
                string? dotnet = FindDotnet();
                info.DotnetPath = dotnet;
                if (dotnet == null)
                {
                    info.Explanation = "C# betikleri için .NET SDK bulunamadı. Betik derlemek isterseniz Microsoft'un sitesinden .NET 9 SDK'yı kurun " +
                                       "(uygulama kendisi bir şey indirmez). İfade dili ve hazır eklenti DLL'leri SDK olmadan da çalışır.";
                    return _info = info;
                }

                var (code, output) = Run(dotnet, "--list-sdks", Path.GetTempPath(), TimeSpan.FromSeconds(20));
                var sdks = new List<string>();
                if (code == 0)
                {
                    foreach (var line in output.Split('\n'))
                    {
                        var m = Regex.Match(line.Trim(), @"^(\d+\.\d+\.\d+(?:-[\w\.]+)?)\s");
                        if (m.Success) sdks.Add(m.Groups[1].Value);
                    }
                }

                info.InstalledSdks = sdks;
                // Scripts run on this process' runtime (.NET 9) and reference a net9.0 CoreSim: they must target net9.0.
                // A 9.x SDK carries the matching reference pack; newer SDKs would try to download it.
                string? nine = sdks.Where(s => s.StartsWith("9.", StringComparison.Ordinal) && !s.Contains('-'))
                    .OrderByDescending(s => Version.TryParse(s, out var v) ? v : new Version(0, 0)).FirstOrDefault();
                if (nine == null)
                {
                    info.Explanation = sdks.Count == 0
                        ? "dotnet bulundu ama kurulu bir .NET SDK yok (yalnızca çalışma zamanı var). C# betikleri için .NET 9 SDK gerekir."
                        : $"Kurulu SDK'lar: {string.Join(", ", sdks)}. Betikler bu uygulamanın çalışma zamanına (.NET 9) göre derlenmeli; bunun için bir .NET 9.x SDK gerekir (indirme yapılmaz).";
                    return _info = info;
                }

                info.SdkVersion = nine;
                info.Available = true;
                info.Explanation = $".NET SDK {nine} ile C# betikleri derlenebilir.";
                return _info = info;
            }
        }

        private static string? FindDotnet()
        {
            string exe = Environment.OSVersion.Platform == PlatformID.Win32NT ? "dotnet.exe" : "dotnet";
            var candidates = new List<string>();
            string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrEmpty(root)) candidates.Add(Path.Combine(root!, exe));
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (dir.Length > 0) candidates.Add(Path.Combine(dir.Trim('"'), exe));
            }

            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", exe));
            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>Builds the scripts into one assembly. Blocking (seconds on the first build; instant from cache).</summary>
        public static ScriptBuildResult Build(IReadOnlyList<(string Name, string Source)> files, string assemblyName = "RobotWinScripts", CancellationToken cancel = default)
        {
            var result = new ScriptBuildResult();
            var sw = Stopwatch.StartNew();
            var info = Detect();
            if (!info.Available)
            {
                result.Summary = info.Explanation;
                return result;
            }

            if (files.Count == 0)
            {
                result.Summary = "Derlenecek betik yok.";
                return result;
            }

            string coreSim = typeof(ScriptCompiler).Assembly.Location;
            if (string.IsNullOrEmpty(coreSim) || !File.Exists(coreSim))
            {
                result.Summary = "RobotTwin.CoreSim.dll konumu bulunamadı (tek dosyalık yayımlama?); betikler derlenemiyor.";
                return result;
            }

            string safeName = Regex.Replace(assemblyName, @"[^A-Za-z0-9_\.]", "_");
            string hash = Hash(files, coreSim, info.SdkVersion!, safeName);
            string dir = Path.Combine(WorkRoot, hash);
            string outDir = Path.Combine(dir, "out");
            string dll = Path.Combine(outDir, safeName + ".dll");
            if (File.Exists(dll))
            {
                result.Success = true;
                result.FromCache = true;
                result.AssemblyPath = dll;
                result.Summary = "Betikler önbellekten yüklendi (değişiklik yok).";
                return result;
            }

            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                string empty = Path.Combine(dir, "no-packages");
                Directory.CreateDirectory(empty);
                File.WriteAllText(Path.Combine(dir, "global.json"), "{ \"sdk\": { \"version\": \"" + info.SdkVersion + "\", \"rollForward\": \"latestPatch\" } }");
                File.WriteAllText(Path.Combine(dir, "NuGet.config"),
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<configuration>\n  <packageSources>\n    <clear />\n    <add key=\"local\" value=\"no-packages\" />\n  </packageSources>\n</configuration>\n");
                File.WriteAllText(Path.Combine(dir, safeName + ".csproj"), Project(safeName, coreSim));
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, source) in files)
                {
                    string file = Regex.Replace(Path.GetFileName(string.IsNullOrWhiteSpace(name) ? "Betik.cs" : name), @"[^\w\.\-]", "_");
                    if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) file += ".cs";
                    while (!used.Add(file)) file = Path.GetFileNameWithoutExtension(file) + "_.cs";
                    File.WriteAllText(Path.Combine(dir, file), source, new UTF8Encoding(true));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                result.Summary = "Betik klasörü hazırlanamadı: " + ex.Message;
                return result;
            }

            var (code, output) = Run(info.DotnetPath!, $"build \"{safeName}.csproj\" -c Release -o out -nologo -v q -clp:NoSummary", dir, TimeSpan.FromMinutes(3), cancel);
            result.Output = output;
            result.Duration = sw.Elapsed;
            ParseDiagnostics(output, result.Diagnostics);
            result.Success = code == 0 && File.Exists(dll);
            if (result.Success)
            {
                result.AssemblyPath = dll;
                result.Summary = $"Betikler derlendi ({result.Duration.TotalSeconds:0.0} s, {result.Diagnostics.Count(d => !d.IsError)} uyarı).";
            }
            else
            {
                int errors = result.Diagnostics.Count(d => d.IsError);
                result.Summary = errors > 0
                    ? $"Derleme {errors} hata ile başarısız: {result.Diagnostics.First(d => d.IsError)}"
                    : code == -2 ? "Derleme zaman aşımına uğradı veya iptal edildi." : "Derleme başarısız (dotnet çıktısına bakın).";
                if (output.IndexOf("NU1101", StringComparison.Ordinal) >= 0 || output.IndexOf("NU1100", StringComparison.Ordinal) >= 0)
                    result.Summary += " Bir NuGet paketi gerekiyor; betikler paket kullanamaz (indirme yapılmaz).";
            }

            return result;
        }

        private static string Project(string name, string coreSim) =>
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net9.0</TargetFramework>\n" +
            "    <AssemblyName>" + name + "</AssemblyName>\n" +
            "    <RootNamespace>" + name + "</RootNamespace>\n" +
            "    <Nullable>enable</Nullable>\n" +
            "    <ImplicitUsings>enable</ImplicitUsings>\n" +
            "    <LangVersion>latest</LangVersion>\n" +
            "    <Deterministic>true</Deterministic>\n" +
            "    <GenerateDocumentationFile>false</GenerateDocumentationFile>\n" +
            "    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>\n" +
            "    <EnableDefaultCompileItems>true</EnableDefaultCompileItems>\n" +
            "    <DefaultItemExcludes>$(DefaultItemExcludes);out/**;no-packages/**</DefaultItemExcludes>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <Reference Include=\"RobotTwin.CoreSim\">\n" +
            "      <HintPath>" + System.Security.SecurityElement.Escape(coreSim) + "</HintPath>\n" +
            "      <Private>false</Private>\n" +
            "    </Reference>\n" +
            "    <Using Include=\"RobotTwin.CoreSim.Sdk\" />\n" +
            "    <Using Include=\"RobotTwin.CoreSim.Mechanics\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n";

        private static readonly Regex DiagnosticLine = new Regex(
            @"^(?<file>[^\r\n(]+?)\((?<line>\d+),(?<col>\d+)\):\s*(?<sev>error|warning)\s+(?<code>[A-Z]+\d+):\s*(?<msg>.*?)(?:\s+\[[^\]]+\])?\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        public static void ParseDiagnostics(string output, List<ScriptDiagnostic> into)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in DiagnosticLine.Matches(output))
            {
                var d = new ScriptDiagnostic
                {
                    File = Path.GetFileName(m.Groups["file"].Value.Trim()),
                    Line = int.Parse(m.Groups["line"].Value, CultureInfo.InvariantCulture),
                    Column = int.Parse(m.Groups["col"].Value, CultureInfo.InvariantCulture),
                    IsError = m.Groups["sev"].Value.Equals("error", StringComparison.OrdinalIgnoreCase),
                    Code = m.Groups["code"].Value,
                    Message = m.Groups["msg"].Value.Trim()
                };
                if (seen.Add(d.ToString())) into.Add(d);
            }
        }

        private static string Hash(IReadOnlyList<(string Name, string Source)> files, string coreSim, string sdk, string name)
        {
            using var sha = SHA256.Create();
            var sb = new StringBuilder();
            sb.Append(name).Append('\0').Append(sdk).Append('\0');
            var fi = new FileInfo(coreSim);
            sb.Append(fi.Length).Append('\0').Append(fi.LastWriteTimeUtc.Ticks).Append('\0');
            foreach (var (n, s) in files) sb.Append(n).Append('\0').Append(s).Append('\0');
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return BitConverter.ToString(bytes, 0, 10).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static (int Code, string Output) Run(string exe, string args, string cwd, TimeSpan timeout, CancellationToken cancel = default)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                WorkingDirectory = cwd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            psi.Environment["DOTNET_NOLOGO"] = "1";
            psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
            psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            var sb = new StringBuilder();
            try
            {
                using var p = new Process { StartInfo = psi };
                p.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null) lock (sb) sb.AppendLine(e.Data);
                };
                p.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) lock (sb) sb.AppendLine(e.Data);
                };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                var sw = Stopwatch.StartNew();
                while (!p.WaitForExit(100))
                {
                    if (sw.Elapsed > timeout || cancel.IsCancellationRequested)
                    {
                        try
                        {
                            p.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                        }

                        return (-2, sb.ToString());
                    }
                }

                p.WaitForExit();
                lock (sb) return (p.ExitCode, sb.ToString());
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is IOException)
            {
                return (-1, ex.Message);
            }
        }

        /// <summary>A starter script shown when the user adds a new one.</summary>
        public const string StarterScript =
@"using System;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sdk;

// Bu dosyadaki public sınıflar 'Davranış ekle' menüsünde 'Betik' olarak görünür.
// Every public class here appears under 'Add behaviour' as a script behaviour.
[Behavior(""Yay ile merkeze çek"", Description = ""Nesneyi bir noktaya yay ve sönümle bağlar."")]
public sealed class SpringToPoint : WorldBehavior
{
    [Parameter(""Yay sabiti"", Unit = ""N/m"")] public double K { get; set; } = 20;
    [Parameter(""Sönüm"", Unit = ""N·s/m"")] public double C { get; set; } = 0.5;
    [Parameter(""Hedef nokta"", Unit = ""m"")] public Vec3 Target { get; set; } = new Vec3(0, 0, 0.3);

    public override void OnStep(IBehaviorContext ctx, double dt)
    {
        var b = ctx.Self;
        if (b == null) return;
        var f = (Target - b.Position) * K - b.Velocity * C;
        b.ApplyForce(f);
        ctx.Publish(""uzaklık"", (Target - b.Position).Length);
    }
}
";
    }
}
