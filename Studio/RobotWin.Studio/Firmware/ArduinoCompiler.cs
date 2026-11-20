using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RobotWin.Studio.Editor;

namespace RobotWin.Studio.Firmware
{
    public sealed class BuildResult
    {
        public bool Success { get; set; }
        public string? Hex { get; set; }
        public List<CodeDiagnostic> Diagnostics { get; } = new List<CodeDiagnostic>();
        public string Log { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public int FlashBytes { get; set; } = -1;
        public int FlashMax { get; set; } = -1;
        public int RamBytes { get; set; } = -1;
        public int RamMax { get; set; } = -1;
    }

    /// <summary>
    /// Builds Arduino sketches with the official toolchain through arduino-cli
    /// (avr-gcc for Uno/Nano). The emulator then runs the produced Intel HEX.
    /// </summary>
    public static class ArduinoCompiler
    {
        public static string BuildRoot { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobotWinStudio", "Build");

        /// <summary>Locates arduino-cli.exe (installed location or PATH).</summary>
        public static string? FindCli()
        {
            string custom = Shell.AppSettings.Current.ArduinoCliPath;
            if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return custom;
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Arduino CLI", "arduino-cli.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Arduino CLI", "arduino-cli.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "arduino-cli.exe")
            };
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (!string.IsNullOrWhiteSpace(dir)) candidates.Add(Path.Combine(dir.Trim(), "arduino-cli.exe"));
            }

            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>Fully qualified board name for a board part, or null when it is not an AVR Arduino.</summary>
        public static string? FqbnFor(string partId) => partId switch
        {
            "arduino-uno" => "arduino:avr:uno",
            "arduino-nano" => "arduino:avr:nano",
            "esp32-devkitc" or "esp32-devkit-v1" => Esp32Target,
            _ => null
        };

        /// <summary>Target name of ESP32 boards: the sketch runs natively against the Arduino-ESP32 HAL model.</summary>
        public const string Esp32Target = "esp32-hal";

        public static bool IsEsp32(string? target) => target == Esp32Target;

        // [radio] The HAL ABI version is part of the cache key: DLLs built for an older ABI are rebuilt, not loaded.
        private static string Esp32Dir(string source) => Path.Combine(BuildRoot, "esp32", "abi" + RobotTwin.CoreSim.Firmware.Esp32.Esp32CoSimulation.AbiVersion + "-" + FirmwareHash(source));

        private static string FirmwareHash(string source) => RobotTwin.CoreSim.Formats.FirmwareSource.HashOf(source);

        /// <summary>The cached native build of an ESP32 sketch, or null when it has not been built yet.</summary>
        public static string? CachedEsp32Dll(string source)
        {
            string marker = Path.Combine(Esp32Dir(source), "build.path");
            if (!File.Exists(marker)) return null;
            string dll = File.ReadAllText(marker).Trim();
            return File.Exists(dll) ? dll : null;
        }

        /// <summary>Builds an ESP32 sketch with MSVC (Visual Studio C++ tools) into a DLL; reuses an earlier build of the same source.</summary>
        public static BuildResult CompileEsp32(string source)
        {
            var result = new BuildResult();
            var watch = Stopwatch.StartNew();
            string? cached = CachedEsp32Dll(source);
            if (cached != null)
            {
                result.Success = true;
                result.Hex = "esp32:" + cached;
                result.Log = "Önbellekteki derleme kullanıldı: " + cached;
                result.Duration = watch.Elapsed;
                return result;
            }

            string? vcvars = string.IsNullOrWhiteSpace(Shell.AppSettings.Current.VcVarsPath) ? null : Shell.AppSettings.Current.VcVarsPath;
            if (!RobotTwin.CoreSim.Firmware.Esp32.Esp32SketchCompiler.ToolchainAvailable(vcvars))
            {
                result.Log = "ESP32 çizimleri yerel olarak MSVC ile derlenir: Visual Studio Build Tools içindeki 'C++ ile masaüstü geliştirme' iş yükünü kurun.";
                result.Diagnostics.Add(new CodeDiagnostic(1, 0, true, "MSVC (vcvars64.bat) bulunamadı"));
                return result;
            }

            string dir = Esp32Dir(source);
            string sketchDir = Path.Combine(dir, "sketch");
            Directory.CreateDirectory(sketchDir);
            string sketch = Path.Combine(sketchDir, "sketch.ino");
            File.WriteAllText(sketch, source, new UTF8Encoding(false));
            var compiler = new RobotTwin.CoreSim.Firmware.Esp32.Esp32SketchCompiler(null, Path.Combine(BuildRoot, "esp32", "shim")) { VcVarsPath = vcvars };
            var build = compiler.Compile(sketch, Path.Combine(dir, "out"));
            result.Log = build.Log;
            foreach (var d in build.Diagnostics)
            {
                if (d.Severity == RobotTwin.CoreSim.Firmware.Esp32.CompilerSeverity.Note) continue;
                bool ours = string.IsNullOrEmpty(d.File) || Path.GetFileName(d.File).Equals("sketch.ino", StringComparison.OrdinalIgnoreCase);
                if (!ours && d.Severity != RobotTwin.CoreSim.Firmware.Esp32.CompilerSeverity.Error) continue;
                string msg = ours ? Translate(d.Message) : $"{Path.GetFileName(d.File)}:{d.Line}: {d.Message}";
                result.Diagnostics.Add(new CodeDiagnostic(ours ? Math.Max(1, d.Line) : 1, ours ? d.Column : 0, d.Severity == RobotTwin.CoreSim.Firmware.Esp32.CompilerSeverity.Error, msg));
            }

            if (build.Success && build.DllPath != null && File.Exists(build.DllPath))
            {
                File.WriteAllText(Path.Combine(dir, "build.path"), build.DllPath);
                result.Success = true;
                result.Hex = "esp32:" + build.DllPath;
            }
            else if (result.Diagnostics.Count == 0)
            {
                result.Diagnostics.Add(new CodeDiagnostic(1, 0, true, "Derleme başarısız (günlüğe bakın)."));
            }

            result.Duration = watch.Elapsed;
            return result;
        }

        private static readonly Regex DiagnosticLine = new Regex(@"^(?<file>.*?\.(ino|cpp|c|h)):(?<line>\d+):(?:(?<col>\d+):)?\s*(?<kind>fatal error|error|warning|note):\s*(?<msg>.*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex FlashLine = new Regex(@"uses (?<used>\d+) bytes .*?Maximum is (?<max>\d+) bytes", RegexOptions.Compiled);
        private static readonly Regex RamLine = new Regex(@"Global variables use (?<used>\d+) bytes .*?Maximum is (?<max>\d+) bytes", RegexOptions.Compiled);

        public static async Task<BuildResult> CompileAsync(string source, string fqbn, string workKey, CancellationToken cancel = default)
        {
            if (IsEsp32(fqbn)) return await Task.Run(() => CompileEsp32(source), cancel).ConfigureAwait(false);
            var result = new BuildResult();
            var watch = Stopwatch.StartNew();
            string? cli = FindCli();
            if (cli == null)
            {
                result.Log = "arduino-cli bulunamadı. Ayarlar'dan veya winget ile 'ArduinoSA.CLI' kurun, ardından 'arduino-cli core install arduino:avr' çalıştırın.";
                result.Diagnostics.Add(new CodeDiagnostic(1, 0, true, "Derleyici (arduino-cli) bulunamadı"));
                return result;
            }

            string safeKey = new string(workKey.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
            string sketchDir = Path.Combine(BuildRoot, safeKey, "sketch");
            string outDir = Path.Combine(BuildRoot, safeKey, "out");
            Directory.CreateDirectory(sketchDir);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(sketchDir, "sketch.ino"), source, new UTF8Encoding(false));

            var psi = new ProcessStartInfo(cli)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var a in new[] { "compile", "--fqbn", fqbn, "--output-dir", outDir, "--warnings", "default", "--no-color", sketchDir }) psi.ArgumentList.Add(a);

            var log = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null) lock (log) log.AppendLine(e.Data);
                };
                p.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) lock (log) log.AppendLine(e.Data);
                };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                using (cancel.Register(() =>
                       {
                           try
                           {
                               if (!p.HasExited) p.Kill(true);
                           }
                           catch (InvalidOperationException)
                           {
                           }
                       }))
                {
                    await p.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }

                result.Success = p.ExitCode == 0 && !cancel.IsCancellationRequested;
            }

            result.Log = log.ToString();
            foreach (var raw in result.Log.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                var m = DiagnosticLine.Match(line);
                if (m.Success && Path.GetFileName(m.Groups["file"].Value).StartsWith("sketch", StringComparison.OrdinalIgnoreCase))
                {
                    string kind = m.Groups["kind"].Value.ToLowerInvariant();
                    if (kind == "note") continue;
                    int col = m.Groups["col"].Success ? int.Parse(m.Groups["col"].Value) : 0;
                    result.Diagnostics.Add(new CodeDiagnostic(int.Parse(m.Groups["line"].Value), col, kind != "warning", Translate(m.Groups["msg"].Value)));
                    continue;
                }

                var f = FlashLine.Match(line);
                if (f.Success)
                {
                    result.FlashBytes = int.Parse(f.Groups["used"].Value);
                    result.FlashMax = int.Parse(f.Groups["max"].Value);
                }

                var r = RamLine.Match(line);
                if (r.Success)
                {
                    result.RamBytes = int.Parse(r.Groups["used"].Value);
                    result.RamMax = int.Parse(r.Groups["max"].Value);
                }
            }

            string hex = Path.Combine(outDir, "sketch.ino.hex");
            if (result.Success && File.Exists(hex)) result.Hex = File.ReadAllText(hex);
            else result.Success = false;
            if (!result.Success && result.Diagnostics.Count == 0 && !cancel.IsCancellationRequested)
            {
                string first = result.Log.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "bilinmeyen hata";
                result.Diagnostics.Add(new CodeDiagnostic(1, 0, true, first));
            }

            result.Duration = watch.Elapsed;
            return result;
        }

        /// <summary>Common gcc messages in Turkish (the original stays in the log).</summary>
        private static string Translate(string msg)
        {
            var m = Regex.Match(msg, "^'(.+)' was not declared in this scope");
            if (m.Success) return $"'{m.Groups[1].Value}' bu kapsamda tanımlı değil";
            m = Regex.Match(msg, "^expected '(.+)' before (.+)$");
            if (m.Success) return $"'{m.Groups[1].Value}' bekleniyordu ({m.Groups[2].Value} öncesinde)";
            m = Regex.Match(msg, "^expected (.+) at end of input");
            if (m.Success) return $"dosya sonunda {m.Groups[1].Value} bekleniyordu (kapanmamış parantez?)";
            if (msg.StartsWith("unused variable", StringComparison.Ordinal)) return "kullanılmayan değişken: " + msg.Substring(15).Trim();
            m = Regex.Match(msg, "^(.+): No such file or directory");
            if (m.Success) return $"{m.Groups[1].Value} bulunamadı (kütüphane kurulu değil)";
            return msg;
        }

        /// <summary>Installs a library from the Arduino library index (run only when the user asks).</summary>
        public static async Task<(bool Ok, string Log)> InstallLibraryAsync(string name)
        {
            string? cli = FindCli();
            if (cli == null) return (false, "arduino-cli bulunamadı");
            var psi = new ProcessStartInfo(cli)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("lib");
            psi.ArgumentList.Add("install");
            psi.ArgumentList.Add(name);
            using var p = Process.Start(psi)!;
            string output = await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            string error = await p.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await p.WaitForExitAsync().ConfigureAwait(false);
            return (p.ExitCode == 0, output + error);
        }
    }
}
