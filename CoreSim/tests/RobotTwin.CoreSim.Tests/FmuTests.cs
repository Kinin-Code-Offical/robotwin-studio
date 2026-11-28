using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Fmi;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>Runs where MSVC is installed: the test FMU's binary is compiled from Fixtures/fmu/lowpass.c.</summary>
    public sealed class MsvcFactAttribute : FactAttribute
    {
        public MsvcFactAttribute()
        {
            if (Esp32Toolchain.FindVcVars(null) == null) Skip = "MSVC kurulu değil (vcvars64.bat bulunamadı).";
        }
    }

    /// <summary>[fmi] FMI 2.0 co-simulation FMUs: read, run on their own, and co-simulated in a circuit.</summary>
    public class FmuTests
    {
        private readonly ITestOutputHelper _out;

        public FmuTests(ITestOutputHelper output) => _out = output;

        private static readonly Lazy<byte[]> Lowpass = new Lazy<byte[]>(() => Build("lowpass"));
        private static readonly Lazy<byte[]> Ball = new Lazy<byte[]>(() => Build("ball"));   // [fmi-me]
        private static readonly Lazy<byte[]> Lag = new Lazy<byte[]>(() => Build("lag"));   // [fmi-me]
        private static readonly Lazy<byte[]> Lowpass3 = new Lazy<byte[]>(() => Build("lowpass3", "x86_64-windows"));   // [fmi3]

        /// <summary>A test FMU: its C source compiled with MSVC and packed with its description, as an FMU is shipped.</summary>
        private static byte[] Build(string name, string platform = "win64")
        {
            string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fmu");
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-fmu-build-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            File.Copy(Path.Combine(fixtures, name + ".c"), Path.Combine(dir, name + ".c"));
            var env = Esp32Toolchain.GetEnvironment(Esp32Toolchain.FindVcVars(null)!, new StringBuilder());
            int exit = Esp32Toolchain.Run(env, "cl.exe", $"/nologo /O2 /LD {name}.c /Fe:{name}.dll", dir, out string output);
            Assert.True(exit == 0, output);
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                zip.CreateEntryFromFile(Path.Combine(fixtures, name + ".xml"), "modelDescription.xml");
                zip.CreateEntryFromFile(Path.Combine(dir, name + ".dll"), $"binaries/{platform}/{name}.dll");
            }

            return ms.ToArray();
        }

        /// <summary>[fmi-me] A bouncing ball solved by our integrator: its bounces where the analysis puts them, ticks on its time events.</summary>
        [MsvcFact]
        public void AModelExchangeFmuIsSolvedByOurIntegratorWithItsEvents()
        {
            var d = FmuDescription.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fmu", "ball.xml")));
            Assert.True(d.UsesModelExchange);
            Assert.Equal((2, 1, "ball"), (d.ContinuousStates, d.EventIndicators, d.BinaryId));

            var fmu = FmuArchive.Open(Ball.Value);
            using var ball = new Fmu2Instance(fmu, "ball");
            Assert.True(ball.ModelExchange);
            ball.Initialise(0);
            var h = fmu.Description.Find("h")!;
            var bounces = fmu.Description.Find("bounces")!;
            var ticks = fmu.Description.Find("ticks")!;
            double g = 9.81, e = 0.7;
            double t1 = Math.Sqrt(2 / g), v1 = Math.Sqrt(2 * g);   // first impact
            double t2 = t1 + 2 * e * v1 / g;   // second
            var trace = new List<(double T, double H, double N)>();
            while (ball.Time < 1.2 - 1e-9)
            {
                ball.Step(0.01);
                trace.Add((ball.Time, ball.Get(h), ball.Get(bounces)));
            }

            // the bounces come at the analysed times, to the step they fall in
            double first = trace.First(p => p.N >= 1).T, second = trace.First(p => p.N >= 2).T;
            _out.WriteLine($"impacts at {first:0.000} s and {second:0.000} s (analysis {t1:0.0000}, {t2:0.0000}); steps {ball.IntegratorSteps}, rejected {ball.RejectedSteps}, state events {ball.StateEvents}, time events {ball.TimeEvents}");
            Assert.InRange(t1, first - 0.01, first);
            Assert.InRange(t2, second - 0.01, second);
            double v2 = e * e * v1, dt = 1.2 - t2;
            Assert.Equal(v2 * dt - g / 2 * dt * dt, ball.Get(h), 6);   // the height after the second bounce
            Assert.Equal(2, ball.Get(bounces));
            Assert.Equal(4, ball.Get(ticks));   // its time events at 0.25, 0.5, 0.75 and 1.0 s
            Assert.Equal(4, ball.TimeEvents);
            Assert.All(trace, p => Assert.True(p.H >= -1e-9, $"below the floor at {p.T}: {p.H}"));
        }

        /// <summary>[fmi-me] A model-exchange lag in the circuit: integrated to the circuit's communication points, as accurate as the co-simulation one.</summary>
        [MsvcFact]
        public void AModelExchangeFmuRunsInTheCircuit()
        {
            var fmu = FmuArchive.Open(Lag.Value);
            using (var alone = new Fmu2Instance(fmu, "alone"))
            {
                alone.Initialise(0, new Dictionary<string, double> { ["k"] = 2, ["tau"] = 0.05 });
                alone.Set(fmu.Description.Find("u")!, 1);
                for (int i = 0; i < 100; i++) alone.Step(0.001);
                Assert.Equal(2 * (1 - Math.Exp(-2)), alone.Get(fmu.Description.Find("y")!), 6);   // the integrator's own accuracy
            }

            var c = new AnalogCircuit();
            int input = c.Node("in"), output = c.Node("out");
            c.Add(new VoltageSource("V1", input, AnalogCircuit.Ground, 1.0));
            c.Add(new Resistor("RL", output, AnalogCircuit.Ground, 1000));
            var d = fmu.Description;
            var block = c.Add(new FmuBlock("U1", new Fmu2Instance(fmu, "U1"), AnalogCircuit.Ground,
                new[] { (d.Find("u")!, input) }, new[] { (d.Find("y")!, output) },
                new Dictionary<string, double> { ["k"] = 2, ["tau"] = 0.05 }));
            using (block)
            {
                var sim = new AnalogSimulator(c);
                sim.Initialize(InitialState.OperatingPoint);
                for (int i = 0; i < 100; i++) sim.Step(0.001);
                double expected = 2 * (1 - Math.Exp(-0.1 / 0.05)) * 1000 / 1001;
                double v = sim.NodeVoltage(output);
                _out.WriteLine($"out {v:0.0000} V at 100 ms (expected {expected:0.0000} V); integrator steps {((Fmu2Instance)block.Instance).IntegratorSteps}");
                Assert.InRange(v, expected * 0.98, expected * 1.001);
            }

            var part = FmuImport.ToCustomPart(Lag.Value);   // a model-exchange FMU becomes a part too
            Assert.Equal(new[] { "u", "y", "REF" }, part.Definition!.Pins.Select(p => p.Name));
        }

        [Fact]
        public void TheDescriptionIsRead()
        {
            var d = FmuDescription.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fmu", "lowpass.xml")));
            Assert.Equal("lowpass", d.CoSimulationId);
            Assert.True(d.CanHandleVariableStep);
            Assert.Equal(0.001, d.DefaultStep);
            Assert.Equal(new[] { "u" }, d.Inputs.Select(v => v.Name));
            Assert.Equal(new[] { "y", "steps", "saturated" }, d.Outputs.Select(v => v.Name));
            Assert.Equal(new[] { "k", "tau", "limit" }, d.Parameters.Select(v => v.Name));
            Assert.Equal(FmuType.Boolean, d.Find("saturated")!.Type);
            Assert.Equal(0.01, d.Find("tau")!.Start);
            Assert.Throws<NotSupportedException>(() => FmuDescription.Parse("<fmiModelDescription fmiVersion=\"4.0\" modelName=\"x\"/>"));
        }

        /// <summary>[fmi3] An FMI 3.0 description: the token, the typed variables (Float64, Float32, Int32, Boolean), the outputs.</summary>
        [Fact]
        public void AnFmi3DescriptionIsRead()
        {
            var d = FmuDescription.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fmu", "lowpass3.xml")));
            Assert.True(d.IsFmi3);
            Assert.Equal("{3c1e9b2a-5d7f-4e21-9a43-lowpass3}", d.Guid);
            Assert.Equal("lowpass3", d.CoSimulationId);
            Assert.Equal(new[] { "u" }, d.Inputs.Select(v => v.Name));
            Assert.Equal(new[] { "y", "steps", "saturated" }, d.Outputs.Select(v => v.Name));
            Assert.Equal(new[] { "k", "tau", "limit", "gain32" }, d.Parameters.Select(v => v.Name));
            Assert.Equal("Float32", d.Find("gain32")!.Fmi3Type);
            Assert.Equal(FmuType.Integer, d.Find("steps")!.Type);
            Assert.Equal(FmuType.Boolean, d.Find("saturated")!.Type);
            Assert.Equal(0.01, d.Find("tau")!.Start);
        }

        /// <summary>[fmi3] An FMI 3.0 co-simulation FMU on its own and in the circuit, as the 2.0 one: the exact lag.</summary>
        [MsvcFact]
        public void AnFmi3FmuRunsAloneAndInTheCircuit()
        {
            var fmu = FmuArchive.Open(Lowpass3.Value);
            var d = fmu.Description;
            using (var a = FmuInstance.Open(fmu, "a"))
            {
                Assert.IsType<Fmu3Instance>(a);
                a.Initialise(0, new Dictionary<string, double> { ["k"] = 2, ["tau"] = 0.05, ["gain32"] = 0.5 });
                a.Set(d.Find("u")!, 3);
                for (int i = 0; i < 100; i++) a.Step(0.001);
                Assert.Equal(3 * (1 - Math.Exp(-2)), a.Get(d.Find("y")!), 9);   // k · gain32 · u = 3
                Assert.Equal(100, a.Get(d.Find("steps")!));
                Assert.Equal(0, a.Get(d.Find("saturated")!));
                Assert.Equal(0.5, a.Get(d.Find("gain32")!), 6);
                Assert.Equal(0.1, a.Time, 9);
                Assert.Contains(a.Messages, m => m.Contains("lowpass3 ready"));
            }

            var c = new AnalogCircuit();
            int input = c.Node("in"), output = c.Node("out");
            c.Add(new VoltageSource("V1", input, AnalogCircuit.Ground, 1.0));
            c.Add(new Resistor("RL", output, AnalogCircuit.Ground, 1000));
            var block = c.Add(new FmuBlock("U1", FmuInstance.Open(fmu, "U1"), AnalogCircuit.Ground,
                new[] { (d.Find("u")!, input) }, new[] { (d.Find("y")!, output) },
                new Dictionary<string, double> { ["k"] = 2, ["tau"] = 0.05 }));
            using (block)
            {
                var sim = new AnalogSimulator(c);
                sim.Initialize(InitialState.OperatingPoint);
                for (int i = 0; i < 100; i++) sim.Step(0.001);
                double expected = 2 * (1 - Math.Exp(-0.1 / 0.05)) * 1000 / 1001;
                double v = sim.NodeVoltage(output);
                _out.WriteLine($"FMI 3.0: out {v:0.0000} V at 100 ms (expected {expected:0.0000} V)");
                Assert.InRange(v, expected * 0.98, expected * 1.001);
            }

            var part = FmuImport.ToCustomPart(Lowpass3.Value);   // a part in the library too
            Assert.Equal(new[] { "u", "y", "steps", "saturated", "REF" }, part.Definition!.Pins.Select(p => p.Name));
        }

        [MsvcFact]
        public void AnFmuRunsOnItsOwnSolver()
        {
            var fmu = FmuArchive.Open(Lowpass.Value);
            using var a = new Fmu2Instance(fmu, "a");
            a.Initialise(0, new Dictionary<string, double> { ["k"] = 2, ["tau"] = 0.05 });
            a.Set(fmu.Description.Find("u")!, 1);
            for (int i = 0; i < 100; i++) a.Step(0.001);
            double y = a.Get(fmu.Description.Find("y")!);
            _out.WriteLine($"y(0.1 s) = {y:0.0000}, steps {a.Get(fmu.Description.Find("steps")!)}; {string.Join(" | ", a.Messages)}");
            Assert.Equal(2 * (1 - Math.Exp(-2)), y, 9);   // exact: its solver steps the lag exactly
            Assert.Equal(100, a.Get(fmu.Description.Find("steps")!));
            Assert.Equal(0, a.Get(fmu.Description.Find("saturated")!));
            Assert.Contains(a.Messages, l => l.Contains("lowpass ready"));   // logged while it was being made

            // a second instance of the same FMU keeps its own state (its own copy of the binary)
            using var b = new Fmu2Instance(fmu, "b");
            b.Initialise(0);
            b.Set(fmu.Description.Find("u")!, 10);
            b.Step(1.0);
            Assert.Equal(1, b.Get(fmu.Description.Find("saturated")!));   // 10 > its limit 4.5
            Assert.Equal(y, a.Get(fmu.Description.Find("y")!), 12);
        }

        [MsvcFact]
        public void AnFmuBecomesAPartThatTravelsWithTheProjectAndSimulates()
        {
            // The FMU as a library part: its pins, its parameters, the file inside the part (saved and read back).
            var part = FmuImport.ToCustomPart(Lowpass.Value);
            Assert.Equal(new[] { "u", "y", "steps", "saturated", "REF" }, part.Definition!.Pins.Select(p => p.Name));
            Assert.Contains(part.Definition.Parameters, p => p.Key == "tau" && p.Value == "0.01");
            string file = Path.Combine(Path.GetTempPath(), "robotwin-fmu-part-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".rwpart");
            part.Save(file);
            var back = RobotTwin.CoreSim.Formats.CustomPart.Load(file);
            File.Delete(file);
            var lib = new RobotTwin.CoreSim.Design.PartLibrary();
            RobotTwin.CoreSim.Design.StandardParts.Register(lib);
            Assert.True(back.Register(lib));

            // 1 V into it, its output into a 1 kΩ load, REF on ground: the circuit and the FMU run together.
            var sch = new RobotTwin.CoreSim.Design.Schematic();
            var u1 = sch.AddPart(lib, back.Id, new RobotTwin.CoreSim.Design.GridPoint(0, 0));
            u1.Parameters["k"] = "2";
            u1.Parameters["tau"] = "0.05";
            var v1 = sch.AddPart(lib, "dc-source", new RobotTwin.CoreSim.Design.GridPoint(-30, 0));
            v1.Parameters["voltage"] = "1";
            var rl = sch.AddPart(lib, "resistor", new RobotTwin.CoreSim.Design.GridPoint(30, 0));
            void Net(RobotTwin.CoreSim.Design.SchematicPart p, string pin, string net)
            {
                var pd = lib.Get(p.PartId).FindPin(pin)!;
                sch.Labels.Add(new RobotTwin.CoreSim.Design.NetLabel { Name = net, Position = p.Transform(pd.X, pd.Y) });
            }

            Net(v1, "+", "IN");
            Net(v1, "-", "GND");
            Net(u1, "u", "IN");
            Net(u1, "REF", "GND");
            Net(u1, "y", "OUT");
            Net(rl, "A", "OUT");
            Net(rl, "B", "GND");
            var compiled = RobotTwin.CoreSim.Design.CircuitCompiler.Compile(sch, lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            for (int i = 0; i < 100; i++) sim.Step(0.001);
            double v = compiled.PinVoltage(sim, rl.RefDes, "A");
            double expected = 2 * (1 - Math.Exp(-2)) * 1000 / 1001;
            _out.WriteLine($"{back.Name}: OUT {v:0.0000} V at 100 ms (expected {expected:0.0000} V)");
            Assert.InRange(v, expected * 0.98, expected * 1.001);
            foreach (var d in compiled.Circuit.Devices.OfType<FmuBlock>()) d.Dispose();
        }

        [MsvcFact]
        public void AnFmuIsCoSimulatedWithTheCircuit()
        {
            // 1 V into the lag, its output (gain 2, 50 ms) through its 1 Ω into a 1 kΩ load.
            var fmu = FmuArchive.Open(Lowpass.Value);
            var c = new AnalogCircuit();
            int input = c.Node("in"), output = c.Node("out");
            c.Add(new VoltageSource("V1", input, AnalogCircuit.Ground, 1.0));
            c.Add(new Resistor("RL", output, AnalogCircuit.Ground, 1000));
            var d = fmu.Description;
            var block = c.Add(new FmuBlock("U1", new Fmu2Instance(fmu, "U1"), AnalogCircuit.Ground,
                new[] { (d.Find("u")!, input) }, new[] { (d.Find("y")!, output) },
                new Dictionary<string, double> { ["k"] = 2, ["tau"] = 0.05 }));
            using (block)
            {
                var sim = new AnalogSimulator(c);
                sim.Initialize(InitialState.OperatingPoint);
                var samples = new List<(double T, double V)>();
                for (int i = 0; i < 100; i++)
                {
                    sim.Step(0.001);
                    samples.Add((sim.Time, sim.NodeVoltage(output)));
                }

                foreach (var (t, v) in samples.Where((_, i) => i % 20 == 19)) _out.WriteLine($"t {t * 1000:0} ms: out {v:0.0000} V");
                double expected = 2 * (1 - Math.Exp(-0.1 / 0.05)) * 1000 / 1001;   // at 100 ms, through the 1 Ω into 1 kΩ
                Assert.Equal(0.1, block.Instance.Time, 9);
                Assert.InRange(samples[^1].V, expected * 0.98, expected * 1.001);   // a communication step behind at most
                Assert.True(samples.Zip(samples.Skip(1), (p, q) => q.V >= p.V - 1e-9).All(x => x), "rises without overshoot");
            }
        }
    }
}
