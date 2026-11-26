using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Esp32;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[isolation] The sketches of the isolation tests, compiled once.</summary>
    public sealed class Esp32IsolationFixture : IDisposable
    {
        public const string Probe = @"
const int LED = 2;
void setup() {
  Serial.begin(115200);
  pinMode(LED, OUTPUT);
  Serial.println(""probe start"");
}
void loop() {
  static bool on = false;
  on = !on;
  digitalWrite(LED, on ? HIGH : LOW);
  Serial.printf(""t=%lu a=%d d=%d\n"", millis(), analogRead(34), digitalRead(34));
  delay(50);
}
";

        public const string Abort = @"
#include <stdlib.h>
void setup() {
  Serial.begin(115200);
  Serial.println(""up"");
}
void loop() {
  delay(100);
  abort();   // ends the whole process at once: no panic handler gets to say anything
}
";

        public static string Files(string path) => @"
#include <stdio.h>
void setup() {
  Serial.begin(115200);
  FILE* f = fopen(""" + path.Replace('\\', '/') + @""", ""rb"");
  if (f) { char b[16] = {0}; fread(b, 1, 15, f); fclose(f); Serial.printf(""read:%s\n"", b); }
  else Serial.println(""blocked"");
}
void loop() { delay(100); }
";

        public Esp32IsolationFixture()
        {
            Available = Esp32SketchCompiler.IsToolchainAvailable;
            OutputDir = Path.Combine(Path.GetTempPath(), "robotwin-esp32-iso", Guid.NewGuid().ToString("N").Substring(0, 8));
            if (!Available) return;
            Directory.CreateDirectory(OutputDir);
            SecretPath = Path.Combine(OutputDir, "secret.txt");
            File.WriteAllText(SecretPath, "user-secret");
            var sources = new Dictionary<string, string> { ["probe"] = Probe, ["abort"] = Abort, ["files"] = Files(SecretPath) };
            var compiler = new Esp32SketchCompiler();
            var results = new ConcurrentDictionary<string, Esp32BuildResult>();
            Parallel.ForEach(sources, kv => results[kv.Key] = compiler.Compile(kv.Value, Path.Combine(OutputDir, kv.Key)));
            Builds = new Dictionary<string, Esp32BuildResult>(results);
        }

        public bool Available { get; }
        public string OutputDir { get; }
        public string SecretPath { get; } = string.Empty;
        public Dictionary<string, Esp32BuildResult> Builds { get; } = new Dictionary<string, Esp32BuildResult>();

        public Esp32BuildResult Build(string key)
        {
            var build = Builds[key];
            Assert.True(build.Success, key + " did not compile:\n" + string.Join("\n", build.Diagnostics) + "\n" + build.Log);
            return build;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// [isolation] The fast mode runs each sketch in a process of its own (an AppContainer in a job): the same answers as in
    /// the app's process, a sketch that kills its process only reboots its board, and it cannot open the user's files.
    /// </summary>
    [Trait("Category", "Firmware")]
    public class Esp32IsolationTests : IClassFixture<Esp32IsolationFixture>
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly Esp32IsolationFixture _fx;
        private readonly ITestOutputHelper _out;

        public Esp32IsolationTests(Esp32IsolationFixture fixture, ITestOutputHelper output)
        {
            _fx = fixture;
            _out = output;
        }

        private static GridPoint PinAt(SchematicPart part, string pin)
        {
            var p = Lib.Get(part.PartId).FindPin(pin) ?? throw new ArgumentException(part.PartId + " has no pin " + pin);
            return part.Transform(p.X, p.Y);
        }

        private static void Label(Schematic sch, SchematicPart part, string pin, string net) =>
            sch.Labels.Add(new NetLabel { Name = net, Position = PinAt(part, pin) });

        /// <summary>An LED on IO2 and a divider (pot at 0.3) on IO34.</summary>
        private static CompiledCircuit Circuit()
        {
            var sch = new Schematic { Title = "isolation" };
            var board = sch.AddPart(Lib, Esp32Parts.DevKitCId, new GridPoint(0, 0));
            Label(sch, board, "GND2", "GND");
            Label(sch, board, "3V3", "3V3");
            var r = sch.AddPart(Lib, "res-330", new GridPoint(16, 10));
            var led = sch.AddPart(Lib, "led5-red", new GridPoint(26, 10));
            sch.AddWire(PinAt(board, "IO2"), PinAt(r, "A"));
            sch.AddWire(PinAt(r, "B"), PinAt(led, "A"));
            Label(sch, led, "K", "GND");
            var pot = sch.AddPart(Lib, "pot-10k", new GridPoint(-20, 0));
            pot.Parameters["position"] = "0.3";
            Label(sch, pot, "A", "3V3");
            Label(sch, pot, "B", "GND");
            Label(sch, pot, "W", "POT");
            Label(sch, board, "IO34", "POT");
            var gnd = sch.AddPart(Lib, "gnd", new GridPoint(40, 30));
            Label(sch, gnd, "GND", "GND");
            return CircuitCompiler.Compile(sch, Lib);
        }

        private (string Serial, List<(double Time, bool On)> Edges, Esp32CoSimulation Board, double Wall) Run(string dll, bool isolate, double seconds,
            Action<Esp32CoSimulation>? after = null)
        {
            var compiled = Circuit();
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            var board = new Esp32CoSimulation(dll, compiled, "U1", null, new Esp32Options { BootDelay = 0.05, Isolate = isolate });
            var serial = new StringBuilder();
            board.SerialOutput += s => serial.Append(s);
            board.Log += m => _out.WriteLine("log: " + m);
            var led = compiled.Device<Led>("D1")!;
            var edges = new List<(double, bool)>();
            bool on = false;
            var wall = Stopwatch.StartNew();
            const double slice = 100e-6;
            for (int i = 0; i < (int)Math.Round(seconds / slice); i++)
            {
                board.Advance(slice, sim.Time);
                sim.Step(slice);
                bool now = led.Current > 0.5e-3;
                if (now != on)
                {
                    edges.Add((sim.Time, now));
                    on = now;
                }
            }

            double elapsed = wall.Elapsed.TotalSeconds;
            after?.Invoke(board);
            board.Dispose();
            return (serial.ToString(), edges, board, elapsed);
        }

        [Esp32Fact]
        public void TheSketchRunsInItsOwnSandboxAndAnswersAsItDoesInTheApp()
        {
            var build = _fx.Build("probe");
            Assert.NotNull(build.HostPath);
            Assert.Equal(build.HostPath, Esp32SketchHost.Find(build.DllPath!));
            Esp32Isolation level = Esp32Isolation.InProcess;
            var inApp = Run(build.DllPath!, false, 0.6);
            var apart = Run(build.DllPath!, true, 0.6, b => level = b.Isolation);
            _out.WriteLine($"in the app: {inApp.Wall:F2} s wall, apart ({level}): {apart.Wall:F2} s wall");
            _out.WriteLine(apart.Serial);
            Assert.Equal(Esp32Isolation.Sandbox, level);
            Assert.Contains("probe start", apart.Serial);
            Assert.Equal(inApp.Serial, apart.Serial);   // the same text, the same millis(), the same analogRead
            Assert.True(apart.Edges.Count >= 8, "LED edges: " + apart.Edges.Count);
            Assert.Equal(inApp.Edges, apart.Edges);   // each edge at the same instant
            var reading = apart.Serial.Split('\n').First(l => l.StartsWith("t=", StringComparison.Ordinal));
            int a = int.Parse(reading.Split(' ')[1].Substring(2));
            Assert.InRange(a, 2750, 2990);   // the wiper 30 % from A: 0.7 × 3.3 V on the 12-bit ADC
        }

        [Esp32Fact]
        public void ASketchThatEndsItsProcessOnlyRebootsItsBoard()
        {
            var build = _fx.Build("abort");
            int boots = 0;
            string? crash = null;
            var run = Run(build.DllPath!, true, 0.5, b =>
            {
                boots = b.BootCount;
                crash = b.LastCrashMessage;
            });
            _out.WriteLine(run.Serial);
            Assert.True(boots >= 3, "boots: " + boots);   // up, abort, reboot … and the app is still here
            Assert.Contains("beklenmedik biçimde kapandı", crash);
            Assert.Contains("Rebooting...", run.Serial);
            Assert.True(run.Serial.Split('\n').Count(l => l.TrimEnd('\r') == "up") >= 3);
        }

        [Esp32Fact]
        public void TheSandboxKeepsTheUsersFilesFromTheSketch()
        {
            var build = _fx.Build("files");
            var inApp = Run(build.DllPath!, false, 0.2);
            var apart = Run(build.DllPath!, true, 0.2);
            Assert.Contains("read:user-secret", inApp.Serial);   // in the app's process a sketch reads what the user can
            Assert.Contains("blocked", apart.Serial);
            Assert.DoesNotContain("user-secret", apart.Serial);
        }
    }
}
