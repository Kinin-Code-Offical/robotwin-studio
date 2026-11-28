using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Firmware
{
    /// <summary>
    /// [esp32hw] Stage 6 of the two ESP32 modes: every world template with an ESP32 runs the same simulated time in the fast
    /// mode (the native HAL model) and in the hardware-compatible mode (the real-chip build on the Xtensa emulator), and the
    /// report says where they differ: the serial lines (the ROM's and IDF's boot logs aside; also compared with the numbers
    /// masked, since timings move a little), each ESP32 output pin's final level or PWM, and how fast each mode ran. Templates
    /// whose sketch needs what the emulator lacks (Wi-Fi, Bluetooth, the camera) are listed with the reason.
    /// </summary>
    internal static class Esp32ModeComparison
    {
        private sealed class RunResult
        {
            public string Serial = string.Empty;
            public double WallSeconds;
            public readonly Dictionary<string, string> Pins = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, (bool Pwm, double Frequency, double Duty, bool Level)> PinValues =
                new Dictionary<string, (bool, double, double, bool)>(StringComparer.Ordinal);
            public int Warnings;
            public readonly List<string> Notes = new List<string>();
        }

        private static readonly Regex BootLine = new Regex(
            @"^(ets |rst:|configsip:|clk_drv|mode:|load:|entry |ho \d|[EWI] \(\d+\) |\[\s*\d+\]\[[EWID]\]|\s*$)", RegexOptions.Compiled);

        private static readonly Regex Number = new Regex(@"-?\d+(\.\d+)?", RegexOptions.Compiled);

        /// <param name="filter">Only the templates whose id holds one of these (comma separated), or null for all.</param>
        public static int Run(string reportPath, double seconds, string? filter = null)
        {
            var md = new StringBuilder();
            md.AppendLine("# ESP32 mod karşılaştırması: hızlı simülasyon ↔ donanım uyumlu (Xtensa emülatörü)");
            md.AppendLine();
            md.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm} · Arduino-ESP32 {1} · her şablon {2:0.##} s simülasyon",
                DateTime.Now, ArduinoCompiler.Esp32CoreVersion, seconds));
            md.AppendLine();
            if (!ArduinoCompiler.Esp32ToolchainInstalled)
            {
                md.AppendLine("ESP32 araç zinciri kurulu değil (`--esp32-build … --esp32-install`); donanım uyumlu mod derlenemiyor.");
                File.WriteAllText(reportPath, md.ToString());
                return 2;
            }

            var rows = new List<string>();
            var details = new StringBuilder();
            int compared = 0, differing = 0;
            var only = filter?.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            foreach (var template in Examples.WorldTemplates.All)
            {
                if (only is { Length: > 0 } && !only.Any(x => template.Id.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
                WorldDocument probe;
                try
                {
                    probe = template.Build();
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is IOException)
                {
                    continue;
                }

                var sources = Esp32Sources(probe);
                if (sources.Count == 0) continue;
                string? gap = sources.Select(s => Esp32Parts.EmulatorGap(s)).FirstOrDefault(g => g != null);
                if (gap != null)
                {
                    rows.Add($"| {template.Id} | — | — | — | — | atlandı: {gap} emülatörde yok |");
                    continue;
                }

                string? blocker = sources.Select(ArduinoCompiler.Esp32HardwareBlocker).FirstOrDefault(b => b != null);
                if (blocker != null)
                {
                    rows.Add($"| {template.Id} | — | — | — | — | atlandı: {blocker} |");
                    continue;
                }

                bool built = true;
                foreach (var source in sources)
                {
                    if (ArduinoCompiler.CachedEsp32Hardware(source) != null) continue;
                    var r = ArduinoCompiler.EnsureEsp32HardwareBuildAsync(source).GetAwaiter().GetResult();
                    if (!r.Success || ArduinoCompiler.CachedEsp32Hardware(source) == null) built = false;
                }

                if (!built)
                {
                    rows.Add($"| {template.Id} | — | — | — | — | gerçek yonga derlemesi başarısız |");
                    continue;
                }

                var fast = RunOnce(template, "fast", seconds);
                var hardware = RunOnce(template, Esp32Parts.HardwareMode, seconds);
                compared++;
                var a = Lines(fast.Serial);
                var b = Lines(hardware.Serial);
                int n = Math.Min(a.Count, b.Count);
                int same = 0, sameShape = 0;
                for (int i = 0; i < n; i++)
                {
                    if (a[i] == b[i]) same++;
                    if (Shape(a[i]) == Shape(b[i])) sameShape++;
                }

                var pinDiffs = PinDifferences(fast, hardware);
                // A very different number of lines (a reboot loop, a sketch stuck in one mode) is a difference too.
                bool countDiffers = Math.Abs(a.Count - b.Count) > Math.Max(2, 0.2 * Math.Max(a.Count, b.Count));
                bool differs = sameShape < n || pinDiffs.Count > 0 || countDiffers;
                if (differs) differing++;
                string serialCell = n == 0 ? $"{a.Count} / {b.Count} satır" : $"{a.Count} / {b.Count} satır, ilk {n}: %{100.0 * same / n:0} aynı, %{100.0 * sameShape / n:0} biçim";
                rows.Add(string.Format(CultureInfo.InvariantCulture, "| {0} | {1} | {2} | {3:0.0}× | {4:0.0}× | {5} |",
                    template.Id, serialCell, pinDiffs.Count == 0 ? "aynı" : pinDiffs.Count + " fark",
                    seconds / Math.Max(1e-6, fast.WallSeconds), seconds / Math.Max(1e-6, hardware.WallSeconds), differs ? "fark var" : "uyumlu"));

                if (!differs) continue;
                details.AppendLine($"## {template.Id}");
                details.AppendLine();
                foreach (var d in pinDiffs) details.AppendLine("- pin " + d);
                int shown = 0;
                for (int i = 0; i < n && shown < 6; i++)
                {
                    if (a[i] == b[i]) continue;   // the numbers too: where the two modes' readings part
                    details.AppendLine($"- satır {i + 1}: hızlı `{a[i]}` · donanım `{b[i]}`");
                    shown++;
                }

                if (a.Count != b.Count) details.AppendLine($"- satır sayısı: hızlı {a.Count}, donanım {b.Count}");
                for (int i = n; i < Math.Max(a.Count, b.Count) && i < n + 3; i++)
                    details.AppendLine(i < a.Count ? $"- yalnız hızlıda: `{a[i]}`" : $"- yalnız donanımda: `{b[i]}`");
                foreach (var note in fast.Notes.Concat(hardware.Notes).Distinct().Take(6)) details.AppendLine("- " + note);
                details.AppendLine();
            }

            md.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} şablon karşılaştırıldı, {1} tanesinde fark var.", compared, differing));
            md.AppendLine();
            md.AppendLine("| Şablon | Seri çıktı (hızlı / donanım) | Pinler | Hızlı mod hızı | Donanım modu hızı | Sonuç |");
            md.AppendLine("|---|---|---|---|---|---|");
            foreach (var row in rows) md.AppendLine(row);
            md.AppendLine();
            md.AppendLine("Hız: simülasyon saniyesinin duvar saati saniyesine oranı (devre ve fizik dahil). \"Biçim\": sayılar maskelenerek aynı satırlar (zamanlamalar az kayar).");
            md.AppendLine();
            md.Append(details);
            File.WriteAllText(reportPath, md.ToString());
            return 0;
        }

        /// <summary>Every ESP32 sketch of a world, bundled as the runners build them.</summary>
        private static List<string> Esp32Sources(WorldDocument world)
        {
            var list = new List<string>();
            foreach (var o in world.Objects)
            {
                bool esp32 = o.PartId?.StartsWith("esp32", StringComparison.OrdinalIgnoreCase) == true || o.Firmware?.Target?.StartsWith("esp32", StringComparison.OrdinalIgnoreCase) == true;
                if (!esp32) continue;
                if (o.Firmware is { } fw && fw.Source.Trim().Length > 0) list.Add(fw.Bundle());
                else if (o.Parameters.TryGetValue("firmware", out var param) && Simulation.SimulationSession.ResolveFirmwarePath(param) is { } path && File.Exists(path))
                    list.Add(new RobotTwin.CoreSim.Formats.FirmwareSource { Source = File.ReadAllText(path) }.Bundle());
            }

            return list;
        }

        private static RunResult RunOnce(Examples.WorldTemplate template, string mode, double seconds)
        {
            var result = new RunResult();
            var options = new RunnerOptions
            {
                Esp32Builder = Automation.StudioApiHost.BuildEsp32,
                Esp32HardwareBuilder = source => ArduinoCompiler.CachedEsp32Hardware(source) is { } c
                    ? (File.ReadAllBytes(c.Elf), c.Merged != null ? File.ReadAllBytes(c.Merged) : null)
                    : null,
                Esp32Mode = mode
            };
            options.FirmwareSearchPath.Add(Path.Combine(AppContext.BaseDirectory, "Firmware"));
            using var runner = new WorldRunner(template.Build(), options);
            var watch = Stopwatch.StartNew();
            runner.Step(seconds);
            result.WallSeconds = watch.Elapsed.TotalSeconds;
            result.Serial = string.Concat(runner.ReadSerial(null, false).Values);
            foreach (var e in runner.DrainEvents())
            {
                if (e.Kind != RunnerEventKind.Warning) continue;
                result.Warnings++;
                if (result.Notes.Count < 4) result.Notes.Add($"{(mode == "fast" ? "hızlı" : "donanım")} uyarısı: {e.Source}: {e.Message}");
            }

            if (runner.Compiled != null)
            {
                foreach (var pin in runner.Compiled.Circuit.Devices.OfType<DigitalPin>())
                {
                    if (!Regex.IsMatch(pin.Id, @"\.IO\d+$") || (pin.Mode != PinMode.OutputLow && pin.Mode != PinMode.OutputHigh && !pin.PwmActive)) continue;
                    result.PinValues[pin.Id] = (pin.PwmActive, pin.PwmFrequency, pin.PwmDuty, pin.Mode == PinMode.OutputHigh);
                }
            }

            return result;
        }

        private static List<string> PinDifferences(RunResult fast, RunResult hardware)
        {
            var list = new List<string>();
            foreach (var id in fast.PinValues.Keys.Union(hardware.PinValues.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                bool inA = fast.PinValues.TryGetValue(id, out var a), inB = hardware.PinValues.TryGetValue(id, out var b);
                string Show((bool Pwm, double Frequency, double Duty, bool Level) v) =>
                    v.Pwm ? string.Format(CultureInfo.InvariantCulture, "PWM {0:0.###} Hz %{1:0.0}", v.Frequency, v.Duty * 100) : v.Level ? "yüksek" : "düşük";
                if (!inA || !inB)
                {
                    list.Add($"{id}: hızlı {(inA ? Show(a) : "çıkış değil")} · donanım {(inB ? Show(b) : "çıkış değil")}");
                    continue;
                }

                bool same = a.Pwm == b.Pwm && (a.Pwm
                    ? Math.Abs(a.Frequency - b.Frequency) <= 0.01 * Math.Max(a.Frequency, b.Frequency) && Math.Abs(a.Duty - b.Duty) <= 0.01
                    : a.Level == b.Level);
                if (!same) list.Add($"{id}: hızlı {Show(a)} · donanım {Show(b)}");
            }

            return list;
        }

        private static List<string> Lines(string serial) =>
            serial.Replace("\r", string.Empty).Split('\n').Select(l => l.TrimEnd()).Where(l => !BootLine.IsMatch(l)).ToList();

        private static string Shape(string line) => Number.Replace(line, "#");
    }
}
