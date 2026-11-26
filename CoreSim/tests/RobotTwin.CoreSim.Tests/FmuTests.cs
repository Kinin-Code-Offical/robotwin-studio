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

        private static readonly Lazy<byte[]> Lowpass = new Lazy<byte[]>(BuildLowpass);

        /// <summary>The test FMU: its C source compiled with MSVC and packed with its description, as an FMU is shipped.</summary>
        private static byte[] BuildLowpass()
        {
            string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fmu");
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-fmu-build-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            File.Copy(Path.Combine(fixtures, "lowpass.c"), Path.Combine(dir, "lowpass.c"));
            var env = Esp32Toolchain.GetEnvironment(Esp32Toolchain.FindVcVars(null)!, new StringBuilder());
            int exit = Esp32Toolchain.Run(env, "cl.exe", "/nologo /O2 /LD lowpass.c /Fe:lowpass.dll", dir, out string output);
            Assert.True(exit == 0, output);
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                zip.CreateEntryFromFile(Path.Combine(fixtures, "lowpass.xml"), "modelDescription.xml");
                zip.CreateEntryFromFile(Path.Combine(dir, "lowpass.dll"), "binaries/win64/lowpass.dll");
            }

            return ms.ToArray();
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
            Assert.Throws<NotSupportedException>(() => FmuDescription.Parse("<fmiModelDescription fmiVersion=\"3.0\" modelName=\"x\"/>"));
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
