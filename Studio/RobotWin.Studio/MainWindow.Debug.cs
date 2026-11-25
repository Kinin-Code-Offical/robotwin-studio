using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using RobotTwin.CoreSim.Firmware.Debug;
using RobotWin.Studio.Firmware;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio
{
    /// <summary>[debug] The debugger at window level: a stop brings the code page forward; the end-to-end check.</summary>
    public partial class MainWindow
    {
        private void SetupDebugger()
        {
            // A board stopped at a breakpoint or after a step: show its code (unless the code page is already in view).
            FirmwareDebugger.Stopped += _ => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_current != "code" && _second != "code") Navigate("code");
            }));
        }

        /// <summary>
        /// --snapshot --fault debug-e2e-circuit: the same in the circuit simulation (Devre). The Blink example's code is
        /// built and loaded, a breakpoint goes on "digitalWrite(13, LOW);": the circuit stops with the program a second in
        /// (after delay(1000)), and goes round once more in two seconds.
        /// </summary>
        internal async void RunCircuitDebugCheck(string logPath)
        {
            var log = new StringBuilder();
            int failures = 0;

            void Check(bool ok, string what)
            {
                if (!ok) failures++;
                log.AppendLine((ok ? "OK   " : "FAIL ") + what);
            }

            async Task<DebugProgram?> WaitStop(double seconds)
            {
                var start = DateTime.UtcNow;
                while ((DateTime.UtcNow - start).TotalSeconds < seconds)
                {
                    if (FirmwareDebugger.Halted is { } h) return h;
                    await Task.Delay(100);
                }

                return null;
            }

            try
            {
                var example = Examples.ExampleProjects.All.First(x => x.Id == "arduino-blink");
                W.Load(example.Build(), null);
                const string key = "p:U1";
                Navigate("code");
                Check(_code.ShowBoard(key), "Kod sayfası Blink'in Uno'sunu (U1) açtı");
                var lines = _code.EditorText.Replace("\r", string.Empty).Split('\n');
                int lowLine = Array.FindIndex(lines, l => l.Contains("digitalWrite(13, LOW);", StringComparison.Ordinal)) + 1;
                Check(lowLine > 0, $"satır bulundu: digitalWrite(13, LOW) → {lowLine}");
                FirmwareDebugger.Toggle(key, FirmwareDebugger.MainFile, lowLine);

                bool uploaded = false;
                void OnUp(Pages.CodeTarget t) => uploaded |= t.Key == key;
                _code.Uploaded += OnUp;
                _code.Upload();
                var t0 = DateTime.UtcNow;
                while (!uploaded && (DateTime.UtcNow - t0).TotalSeconds < 90) await Task.Delay(100);
                _code.Uploaded -= OnUp;
                Check(uploaded, "kod derlendi, karta yüklendi, devre çalışıyor");
                Check(FirmwareDebugger.ProgramFor(key)?.Info != null, "yazılımın satır bilgisi var");

                var p = await WaitStop(15);
                Check(p != null, "Uno devrede kesme noktasında durdu");
                if (p != null)
                {
                    Check(p.Owner is SimulationSession, "duran oturum devre simülasyonu");
                    Check(p.Debugger.Place is { } pl && pl.Line == lowLine, $"durduğu yer: {p.Debugger.Place}");
                    Check(W.Simulation.State == SimState.Paused, "devre simülasyonu da duraklatıldı");
                    double dt = Math.Abs(p.Time() - p.Cosim.ProgramTime);
                    Check(dt < 1e-9, $"tek saat: devre t = {p.Time():F9} s, program t = {p.Cosim.ProgramTime:F9} s");
                    Check(p.Time() > 0.99 && p.Time() < 1.05, $"ilk durak delay(1000)'den sonra: t = {p.Time():F4} s");
                    double first = p.Time();
                    FirmwareDebugger.Continue(p);
                    var q = await WaitStop(15);
                    Check(q != null && q.Debugger.Place is { } ql && ql.Line == lowLine, "Devam: bir tur sonra yine durdu");
                    double period = (q?.Time() ?? first) - first;
                    Check(Math.Abs(period - 2.0) < 0.01, $"bir tur {period:F4} s (iki delay(1000))");
                }
            }
            catch (Exception ex)
            {
                failures++;
                log.AppendLine("FAIL beklenmeyen hata: " + ex);
            }

            log.AppendLine(failures == 0 ? "TÜMÜ GEÇTİ" : failures + " HATA");
            File.WriteAllText(logPath, log.ToString());
        }

        /// <summary>
        /// --snapshot --fault debug-e2e: the servo arm's code is built and loaded through the real app, a breakpoint goes on
        /// "angle += step;", the world runs until the Uno reaches it, then a step and a continue. Checks that the world stopped
        /// at the program's instant, the line, the variables, the step's line and the next stop's values; leaves it stopped.
        /// </summary>
        internal async void RunDebugCheck(string logPath)
        {
            var log = new StringBuilder();
            int failures = 0;

            void Check(bool ok, string what)
            {
                if (!ok) failures++;
                log.AppendLine((ok ? "OK   " : "FAIL ") + what);
            }

            async Task<DebugProgram?> WaitStop(double seconds)
            {
                var start = DateTime.UtcNow;
                while ((DateTime.UtcNow - start).TotalSeconds < seconds)
                {
                    if (FirmwareDebugger.Halted is { } p) return p;
                    await Task.Delay(100);
                }

                return null;
            }

            try
            {
                var template = Examples.WorldTemplates.Find("servo-arm") ?? throw new InvalidOperationException("servo-arm şablonu yok");
                var world = template.Build();
                NewWorld(world);
                var uno = world.Objects.First(o => o.PartId == "arduino-uno");
                string key = "w:" + uno.Id;
                Navigate("code");
                Check(_code.ShowBoard(key), "Kod sayfası servo kolun Uno'sunu açtı");

                // The breakpoint goes on before the upload, so the first pass through loop() already stops.
                var lines = _code.EditorText.Replace("\r", string.Empty).Split('\n');
                int addLine = Array.FindIndex(lines, l => l.Contains("angle += step;", StringComparison.Ordinal)) + 1;
                int testLine = Array.FindIndex(lines, l => l.Contains("if (angle >= 180", StringComparison.Ordinal)) + 1;
                Check(addLine > 0 && testLine > addLine, $"satırlar bulundu: angle += step → {addLine}, if → {testLine}");
                FirmwareDebugger.Toggle(key, FirmwareDebugger.MainFile, addLine);

                bool uploaded = false;
                void OnUp(Pages.CodeTarget t) => uploaded |= t.Key == key;
                _code.Uploaded += OnUp;
                _code.Upload();
                var t0 = DateTime.UtcNow;
                while (!uploaded && (DateTime.UtcNow - t0).TotalSeconds < 90) await Task.Delay(100);
                _code.Uploaded -= OnUp;
                Check(uploaded, "kod derlendi ve karta yüklendi (dünya çalışıyor)");
                bool hasInfo = FirmwareDebugger.ProgramFor(key)?.Info != null;
                Check(hasInfo, "çalışan yazılımın satır ve değişken bilgisi var (ELF)");
                if (!hasInfo)
                {
                    // What ran instead: the status line, the programs the debugger knows and the world's first log lines.
                    log.AppendLine("     durum: " + W.Status + " · etkin sekme: " + W.ActiveDocument.Kind + " " + W.ActiveDocument.Title + " · dünya " + _world.SimulationState.State + " · aranan " + key + " · çalışanlar: "
                        + string.Join(", ", FirmwareDebugger.Running.Select(r => r.Key + (r.Info != null ? " (ELF)" : " (ELF yok)"))));
                    foreach (var e in _world.SessionLog.Take(8)) log.AppendLine("     günlük: " + e.Device + ": " + e.Message);
                }
                Check(FirmwareDebugger.Resolve(key, FirmwareDebugger.MainFile, addLine) == addLine, "kesme noktası satırın kendi kodunda");

                var p = await WaitStop(10);
                Check(p != null, "Uno kesme noktasında durdu");
                if (p != null)
                {
                    Check(p.Debugger.Place is { InSketch: true } pl && pl.Line == addLine, $"durduğu yer: {p.Debugger.Place}");
                    Check(_world.SimulationState.State == SimState.Paused, "dünya da duraklatıldı");
                    double dt = Math.Abs(p.Time() - p.Cosim.ProgramTime);
                    Check(dt < 1e-9, $"tek saat: dünya t = {p.Time():F9} s, program t = {p.Cosim.ProgramTime:F9} s (fark {dt * 1e9:F3} ns)");
                    var angle = p.Info!.Variables.First(v => v.Name == "angle");
                    var step = p.Info.Variables.First(v => v.Name == "step");
                    string a1 = FirmwareDebugInfo.Format(angle, p.Cosim.Mcu.ReadData), s1 = FirmwareDebugInfo.Format(step, p.Cosim.Mcu.ReadData);
                    Check(a1 == "0" && s1 == "2", $"değişkenler ilk durakta: angle = {a1}, step = {s1}");
                    Check(_current == "code" || _second == "code", "Kod sayfası önde");
                    Check(_code.ExecutionLineShown == addLine, "düzenleyici durduğu satırı gösteriyor");

                    double before = p.Time();
                    Check(FirmwareDebugger.Step(p, DebugStepKind.Over), "Adım (F10)");
                    var q = await WaitStop(5);
                    Check(q != null && q.Debugger.Place is { } ql && ql.Line == testLine, $"adım bir sonraki satırda durdu: {q?.Debugger.Place}");
                    Check(q != null && FirmwareDebugInfo.Format(angle, q.Cosim.Mcu.ReadData) == "2", "angle += step çalıştı: angle = 2");
                    Check(q != null && q.Time() > before && q.Time() - before < 1e-3, $"adım {((q?.Time() ?? before) - before) * 1e6:F1} µs sürdü");

                    if (q != null)
                    {
                        double at = q.Time();
                        FirmwareDebugger.Continue(q);
                        var r = await WaitStop(5);
                        Check(r != null && r.Debugger.Place is { } rl && rl.Line == addLine, "Devam: döngüyü dolaşıp kesme noktasında yine durdu");
                        Check(r != null && FirmwareDebugInfo.Format(angle, r.Cosim.Mcu.ReadData) == "2", "ikinci durakta angle = 2 (henüz eklenmedi)");
                        Check(r != null && r.Time() - at > 0.015 && r.Time() - at < 0.03, $"bir döngü {((r?.Time() ?? at) - at) * 1000:F2} ms (delay(16) + iki darbe)");
                    }
                }
            }
            catch (Exception ex)
            {
                failures++;
                log.AppendLine("FAIL beklenmeyen hata: " + ex);
            }

            log.AppendLine(failures == 0 ? "TÜMÜ GEÇTİ" : failures + " HATA");
            File.WriteAllText(logPath, log.ToString());
        }
    }
}
