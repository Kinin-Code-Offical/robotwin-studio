using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RobotWin.Studio.Editor;

namespace RobotWin.Studio.Firmware
{
    /// <summary>
    /// [esp32hw] The ESP32's hardware-compatible build (docs/gelistirici/ESP32_MODLARI.md, Mode 2): the sketch compiled by
    /// Espressif's Arduino-ESP32 core with the real xtensa-esp32-elf-gcc into firmware a real board runs (bootloader,
    /// partition table, application, a merged image and the ELF), and written to the board over its serial port with the
    /// core's esptool. The core is installed by arduino-cli into RobotWin's own folder, never the user's Arduino IDE's.
    /// </summary>
    public static partial class ArduinoCompiler
    {
        /// <summary>The Arduino-ESP32 core the hardware build uses: the last of the 3.x line, whose API the fast mode's HAL follows.</summary>
        public const string Esp32CoreVersion = "3.3.13";

        /// <summary>The board the hardware build targets (ESP32-WROOM / DevKitC / DevKit V1: the classic dual-core ESP32).</summary>
        public const string Esp32HardwareFqbn = "esp32:esp32:esp32";

        private const string Esp32PackageIndex = "https://espressif.github.io/arduino-esp32/package_esp32_index.json";

        /// <summary>
        /// Libraries the fast mode's HAL has built in but the real core does not, installed beside it (pinned) so a
        /// sketch builds the same in both modes: ESP32Servo (servos on the LEDC timers).
        /// </summary>
        private static readonly (string Name, string Version)[] Esp32Companions = { ("ESP32Servo", "3.2.1") };

        private static bool CompanionsInstalled =>
            Esp32Companions.All(c => Directory.Exists(Path.Combine(Esp32ToolchainRoot, "user", "libraries", c.Name)));

        /// <summary>Installs the companion libraries that are missing (a toolchain installed before they were added gets them on its next build).</summary>
        private static async Task<bool> EnsureCompanionsAsync(string cli, Action<string> progress, CancellationToken cancel)
        {
            if (CompanionsInstalled) return true;
            if (await RunAsync(Esp32Cli(cli, new[] { "lib", "update-index", "--no-color" }), progress, cancel).ConfigureAwait(false) != 0) return false;
            foreach (var (name, version) in Esp32Companions)
            {
                if (Directory.Exists(Path.Combine(Esp32ToolchainRoot, "user", "libraries", name))) continue;
                progress($"{name} {version} kuruluyor …");
                if (await RunAsync(Esp32Cli(cli, new[] { "lib", "install", name + "@" + version, "--no-color" }), progress, cancel).ConfigureAwait(false) != 0) return false;
            }

            return CompanionsInstalled;
        }

        /// <summary>
        /// Why a sketch cannot be built for the real chip by this toolchain yet, or null: Bluepad32 needs its own
        /// Arduino-ESP32 variant (BTstack inside), which RobotWin does not install yet; the fast mode runs it.
        /// </summary>
        public static string? Esp32HardwareBlocker(string source) =>
            source.Contains("<Bluepad32.h>", StringComparison.Ordinal) || source.Contains("\"Bluepad32.h\"", StringComparison.Ordinal)
                ? "Bluepad32 kullanan çizim gerçek kart için Bluepad32'nin kendi ESP32 çekirdeğini gerektirir; RobotWin onu henüz kurmuyor (hızlı modda çalışır)."
                : null;

        /// <summary>
        /// [neopixel] Libraries RobotWin writes itself for the real-chip build (no download), so a sketch builds the same in
        /// both modes: RobotWin_NeoPixel, the Adafruit_NeoPixel interface on the core's RMT API, the very header the fast
        /// mode's HAL compiles. Written from the simulator's embedded copy into the build folder; null when it cannot be.
        /// </summary>
        private static string? BundledEsp32Libraries()
        {
            try
            {
                var asm = typeof(RobotTwin.CoreSim.Firmware.Esp32.Esp32SketchCompiler).Assembly;
                string? resource = asm.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == "esp32hal/Adafruit_NeoPixel.h");
                if (resource == null) return null;
                string text;
                using (var stream = asm.GetManifestResourceStream(resource))
                {
                    if (stream == null) return null;
                    using var reader = new StreamReader(stream);
                    text = reader.ReadToEnd();
                }

                string root = Path.Combine(BuildRoot, "esp32hw", "libraries");
                string src = Path.Combine(root, "RobotWin_NeoPixel", "src");
                Directory.CreateDirectory(src);
                string header = Path.Combine(src, "Adafruit_NeoPixel.h");
                if (!File.Exists(header) || File.ReadAllText(header) != text) File.WriteAllText(header, text);
                string properties = Path.Combine(root, "RobotWin_NeoPixel", "library.properties");
                const string props = "name=RobotWin_NeoPixel\nversion=1.0.0\nauthor=RobotWin\nmaintainer=RobotWin\n" +
                                     "sentence=WS2812 strips with the Adafruit_NeoPixel interface on the Arduino-ESP32 RMT API.\n" +
                                     "paragraph=Built into RobotWin Studio for the hardware-compatible ESP32 mode.\ncategory=Display\nurl=\narchitectures=esp32\n";
                if (!File.Exists(properties) || File.ReadAllText(properties) != props) File.WriteAllText(properties, props);
                return root;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Where the ESP32 toolchain lives: RobotWin's own data, cores and downloads for arduino-cli.</summary>
        public static string Esp32ToolchainRoot => Path.Combine(LocalAppData, "RobotWinStudio", "toolchains", "arduino-esp32");

        /// <summary>The pinned core is installed.</summary>
        public static bool Esp32ToolchainInstalled =>
            Directory.Exists(Path.Combine(Esp32ToolchainRoot, "data", "packages", "esp32", "hardware", "esp32", Esp32CoreVersion));

        /// <summary>Points arduino-cli (whichever binary) at RobotWin's ESP32 folders and Espressif's package index.</summary>
        private static void UseEsp32Toolchain(ProcessStartInfo psi)
        {
            string root = Esp32ToolchainRoot;
            psi.Environment["ARDUINO_DIRECTORIES_DATA"] = Path.Combine(root, "data");
            psi.Environment["ARDUINO_DIRECTORIES_DOWNLOADS"] = Path.Combine(root, "downloads");
            psi.Environment["ARDUINO_DIRECTORIES_USER"] = Path.Combine(root, "user");
            psi.Environment["ARDUINO_BOARD_MANAGER_ADDITIONAL_URLS"] = Esp32PackageIndex;
        }

        private static ProcessStartInfo Esp32Cli(string cli, IEnumerable<string> args)
        {
            var psi = new ProcessStartInfo(cli)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            UseEsp32Toolchain(psi);
            return psi;
        }

        /// <summary>Runs arduino-cli to the end, each line of its output to <paramref name="line"/>; its exit code (-1 when cancelled).</summary>
        private static async Task<int> RunAsync(ProcessStartInfo psi, Action<string> line, CancellationToken cancel)
        {
            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) line(e.Data);
            };
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) line(e.Data);
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

            return cancel.IsCancellationRequested ? -1 : p.ExitCode;
        }

        /// <summary>
        /// Installs the pinned Arduino-ESP32 core (its compilers, libraries and esptool; a few hundred MB, once) into
        /// RobotWin's folder. Each line arduino-cli prints goes to <paramref name="progress"/>.
        /// </summary>
        public static async Task<bool> InstallEsp32ToolchainAsync(Action<string> progress, CancellationToken cancel = default)
        {
            if (Esp32ToolchainInstalled) return true;
            string? cli = FindCli();
            if (cli == null)
            {
                progress("arduino-cli bulunamadı: uygulamayı yeniden kurun ya da Ayarlar → Derleyiciler'den bir arduino-cli seçin.");
                return false;
            }

            foreach (var dir in new[] { "data", "downloads", "user" }) Directory.CreateDirectory(Path.Combine(Esp32ToolchainRoot, dir));
            progress("ESP32 paket listesi indiriliyor …");
            if (await RunAsync(Esp32Cli(cli, new[] { "core", "update-index", "--no-color" }), progress, cancel).ConfigureAwait(false) != 0) return false;
            progress($"Arduino-ESP32 {Esp32CoreVersion} kuruluyor (derleyiciler, kütüphaneler, esptool) …");
            int code = await RunAsync(Esp32Cli(cli, new[] { "core", "install", "esp32:esp32@" + Esp32CoreVersion, "--no-color" }), progress, cancel).ConfigureAwait(false);
            if (code != 0 || !Esp32ToolchainInstalled) return false;
            if (!await EnsureCompanionsAsync(cli, progress, cancel).ConfigureAwait(false)) return false;
            // The downloaded archives (over a gigabyte) are unpacked now; only the unpacked core is needed.
            try
            {
                Directory.Delete(Path.Combine(Esp32ToolchainRoot, "downloads"), true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return true;
        }

        /// <summary>
        /// Builds a sketch for a real ESP32 with the Arduino-ESP32 core: the result's <see cref="BuildResult.OutputFolder"/>
        /// holds sketch.ino.bin, .bootloader.bin, .partitions.bin, .merged.bin and .elf; errors map to the sketch's lines
        /// as in the fast mode; flash and RAM use come from the core's own report.
        /// </summary>
        public static async Task<BuildResult> CompileEsp32HardwareAsync(string source, string workKey, Action<string>? progress = null, CancellationToken cancel = default)
        {
            var result = new BuildResult();
            var watch = Stopwatch.StartNew();
            string? cli = FindCli();
            if (cli == null || !Esp32ToolchainInstalled)
            {
                result.Log = cli == null
                    ? "arduino-cli bulunamadı: uygulamayı yeniden kurun ya da Ayarlar → Derleyiciler'den bir arduino-cli seçin."
                    : "ESP32 araç zinciri kurulu değil: Kod → Gerçek kart → ESP32 araç zincirini kur.";
                result.Diagnostics.Add(new CodeDiagnostic(1, 0, true, cli == null ? "Derleyici (arduino-cli) bulunamadı" : "ESP32 araç zinciri kurulu değil"));
                return result;
            }

            if (Esp32HardwareBlocker(source) is { } blocker)
            {
                result.Log = blocker;
                result.Diagnostics.Add(new CodeDiagnostic(1, 0, true, blocker));
                return result;
            }

            if (!await EnsureCompanionsAsync(cli, l => progress?.Invoke(l), cancel).ConfigureAwait(false))
            {
                result.Log = "ESP32 için gereken kütüphaneler kurulamadı (ESP32Servo).";
                result.Diagnostics.Add(new CodeDiagnostic(1, 0, true, result.Log));
                return result;
            }

            string safeKey = new string(workKey.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
            string sketchDir = Path.Combine(BuildRoot, "esp32hw", safeKey, "sketch");
            string outDir = Path.Combine(BuildRoot, "esp32hw", safeKey, "out");
            foreach (var dir in new[] { sketchDir, outDir })
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
            }

            WriteSketch(source, sketchDir);
            var args = new List<string> { "compile", "--fqbn", Esp32HardwareFqbn, "--output-dir", outDir, "--warnings", "default", "--no-color", sketchDir };
            foreach (var root in LibraryRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                args.Add("--libraries");
                args.Add(root);
            }

            if (BundledEsp32Libraries() is { } bundled)
            {
                // After the user's own: arduino-cli prefers an installed library of the header's name over ours.
                args.Add("--libraries");
                args.Add(bundled);
            }

            var log = new StringBuilder();
            int code = await RunAsync(Esp32Cli(cli, args), l =>
            {
                lock (log) log.AppendLine(l);
                progress?.Invoke(l);
            }, cancel).ConfigureAwait(false);
            result.Success = code == 0;
            result.Log = log.ToString();
            ReadLog(result);
            string bin = Path.Combine(outDir, "sketch.ino.bin");
            if (result.Success && File.Exists(bin))
            {
                result.OutputFolder = outDir;
                string elf = Path.Combine(outDir, "sketch.ino.elf");
                if (File.Exists(elf)) result.Elf = File.ReadAllBytes(elf);
                CacheEsp32Hardware(source, outDir);
            }
            else
            {
                result.Success = false;
                if (result.Diagnostics.Count == 0 && !cancel.IsCancellationRequested)
                {
                    string first = result.Log.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "bilinmeyen hata";
                    result.Diagnostics.Add(new CodeDiagnostic(1, 0, true, first));
                }
            }

            result.Duration = watch.Elapsed;
            return result;
        }

        /// <summary>[esp32hw] Where a sketch's real-chip build is kept for the emulator, by its source's hash.</summary>
        private static string Esp32HardwareCacheDir(string source) => Path.Combine(BuildRoot, "esp32hw", "cache", FirmwareHash(source));

        /// <summary>
        /// [esp32hw] The real-chip build of exactly this source (its ELF and merged flash image), which the hardware-compatible
        /// mode runs on the Xtensa emulator; null when it has not been built yet.
        /// </summary>
        public static (string Elf, string? Merged)? CachedEsp32Hardware(string source)
        {
            string dir = Esp32HardwareCacheDir(source);
            string elf = Path.Combine(dir, "firmware.elf");
            if (!File.Exists(elf)) return null;
            string merged = Path.Combine(dir, "firmware.merged.bin");
            return (elf, File.Exists(merged) ? merged : null);
        }

        private static void CacheEsp32Hardware(string source, string outDir)
        {
            try
            {
                string dir = Esp32HardwareCacheDir(source);
                Directory.CreateDirectory(dir);
                string merged = Path.Combine(outDir, "sketch.ino.merged.bin");
                if (File.Exists(merged)) File.Copy(merged, Path.Combine(dir, "firmware.merged.bin"), true);
                File.Copy(Path.Combine(outDir, "sketch.ino.elf"), Path.Combine(dir, "firmware.elf"), true);   // last: its presence marks the build whole
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // the emulator builds again next time
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<BuildResult>> HardwareBuilds =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Task<BuildResult>>();

        /// <summary>
        /// [esp32hw] Builds the sketch for the real chip in the background, once per source (a run that finds no build starts
        /// it; the next run finds it in <see cref="CachedEsp32Hardware"/>). A build the compiler refused is kept, so runs say
        /// why (<see cref="FailedEsp32HardwareBuild"/>) instead of building it again and again; changing the sketch builds anew.
        /// A build that failed without a compiler error (a tool that did not start) is forgotten, so the next run retries it.
        /// </summary>
        public static Task<BuildResult> EnsureEsp32HardwareBuildAsync(string source)
        {
            string hash = FirmwareHash(source);
            return HardwareBuilds.GetOrAdd(hash, h => Task.Run(async () =>
            {
                var result = await CompileEsp32HardwareAsync(source, "src-" + h).ConfigureAwait(false);
                if (!result.Success && !result.Diagnostics.Any(d => d.IsError)) HardwareBuilds.TryRemove(h, out _);
                return result;
            }));
        }

        /// <summary>[esp32hw] This source's real-chip build, when it failed with compiler errors (null while it builds, after a success, or before one).</summary>
        public static BuildResult? FailedEsp32HardwareBuild(string source) =>
            HardwareBuilds.TryGetValue(FirmwareHash(source), out var task) && task.Status == TaskStatus.RanToCompletion && !task.Result.Success ? task.Result : null;

        /// <summary>[esp32hw] A failed build in one line: its first compiler error with the file and line, or the log's last line.</summary>
        public static string FirstError(BuildResult result)
        {
            var error = result.Diagnostics.FirstOrDefault(d => d.IsError);
            if (error != null) return $"{error.File}:{error.Line}: {error.Message}";
            string last = result.Log.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? string.Empty;
            return last.Length > 160 ? last.Substring(0, 160) + "…" : last;
        }

        /// <summary>
        /// [esp32hw] Starts (or joins) this source's background real-chip build and calls <paramref name="done"/> on the UI thread
        /// when it ends, so a run in the fast mode can say the emulator build is ready, or why it failed.
        /// </summary>
        public static void WhenEsp32HardwareBuilt(string source, Action<BuildResult> done)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            EnsureEsp32HardwareBuildAsync(source).ContinueWith(t =>
            {
                if (t.Status != TaskStatus.RanToCompletion) return;
                if (dispatcher != null) dispatcher.BeginInvoke(new Action(() => done(t.Result)));
                else done(t.Result);
            }, TaskScheduler.Default);
        }

        /// <summary>Writes a hardware build to a real ESP32 on <paramref name="port"/> with the core's esptool (the board resets into its bootloader by itself).</summary>
        public static async Task<bool> UploadEsp32Async(string outputFolder, string port, Action<string> progress, CancellationToken cancel = default)
        {
            string? cli = FindCli();
            if (cli == null || !Esp32ToolchainInstalled) return false;
            var args = new[] { "upload", "-p", port, "--fqbn", Esp32HardwareFqbn, "--input-dir", outputFolder, "--no-color" };
            return await RunAsync(Esp32Cli(cli, args), progress, cancel).ConfigureAwait(false) == 0;
        }

        /// <summary>The single image (bootloader + partitions + application at their offsets) web flashers and esptool write at 0x0.</summary>
        public static string? Esp32MergedImage(string outputFolder)
        {
            string merged = Path.Combine(outputFolder, "sketch.ino.merged.bin");
            return File.Exists(merged) ? merged : null;
        }

        /// <summary>The serial ports present now (COM3, COM7 …), as Windows lists them.</summary>
        public static IReadOnlyList<string> SerialPorts()
        {
            var ports = new List<string>();
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
                if (key != null)
                    foreach (var name in key.GetValueNames())
                        if (key.GetValue(name) is string port && port.Length > 0)
                            ports.Add(port);
            }
            catch (System.Security.SecurityException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return ports.Distinct().OrderBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
