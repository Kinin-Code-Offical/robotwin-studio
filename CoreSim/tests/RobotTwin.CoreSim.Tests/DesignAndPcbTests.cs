using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Design")]
    public class DesignAndPcbTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;

        [Theory]
        [InlineData("4k7", 4700)]
        [InlineData("100nF", 100e-9)]
        [InlineData("2.2M", 2.2e6)]
        [InlineData("3V3", 3.3)]
        [InlineData("20mA", 0.02)]
        [InlineData("10ms", 0.01)]
        [InlineData("220Ω", 220)]
        [InlineData("4R7", 4.7)]
        [InlineData("1u", 1e-6)]
        public void SiValueParsesEngineeringNotation(string text, double expected)
        {
            Assert.True(SiValue.TryParse(text, out double v));
            Assert.Equal(expected, v, 9);
        }

        [Fact]
        public void CatalogIsLargeAndEveryPresetSimulatesStandalone()
        {
            Assert.True(Lib.Presets.Count >= 150, $"only {Lib.Presets.Count} presets");
            var failures = new System.Collections.Generic.List<string>();
            foreach (var preset in Lib.Presets)
            {
                var sch = new Schematic();
                sch.AddPart(Lib, preset.Id, new GridPoint(0, 0));
                sch.AddPart(Lib, "gnd", new GridPoint(20, 20));
                var compiled = CircuitCompiler.Compile(sch, Lib);
                if (compiled.Warnings.Any(w => w.Contains("modellenemedi"))) failures.Add(preset.Id + ": " + string.Join(";", compiled.Warnings));
                try
                {
                    var sim = new AnalogSimulator(compiled.Circuit) { MaxSubstepsPerCall = 20_000 };
                    sim.Initialize();
                    sim.Step(1e-3);
                }
                catch (AnalogSimulationException ex)
                {
                    failures.Add(preset.Id + ": " + ex.Message);
                }
            }

            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void EveryFootprintReferencedByTheCatalogExistsAndPadsMatchPins()
        {
            foreach (var preset in Lib.Presets)
            {
                var def = Lib.Get(preset.PartId);
                var parameters = Lib.ResolveParameters(def, preset, null);
                string fpId = def.ResolveFootprint(parameters);
                if (string.IsNullOrEmpty(fpId)) continue;
                var fp = FootprintLibrary.Get(fpId);
                Assert.True(fp != null, $"{preset.Id}: footprint '{fpId}' missing");
                foreach (var pin in def.Pins)
                {
                    string pad = def.ResolvePad(pin, parameters);
                    Assert.True(fp!.FindPad(pad) != null, $"{preset.Id}: pin {pin.Name} -> pad '{pad}' not in {fpId}");
                }
            }
        }

        /// <summary>Arduino D13 -> 220R -> red LED -> GND, drawn with wires like a user would.</summary>
        private static Schematic BlinkSchematic()
        {
            var sch = new Schematic { Title = "Blink" };
            var uno = sch.AddPart(Lib, "arduino-uno", new GridPoint(0, 0));
            var r = sch.AddPart(Lib, "res-220", new GridPoint(20, 13));
            var led = sch.AddPart(Lib, "led5-red", new GridPoint(30, 13));
            var gnd = sch.AddPart(Lib, "gnd", new GridPoint(40, 18));

            var d13 = uno.Transform(7, 13);          // D13 pin tip
            var rA = r.Transform(-3, 0);
            var rB = r.Transform(3, 0);
            var ledA = led.Transform(-3, 0);
            var ledK = led.Transform(3, 0);
            var g = gnd.Transform(0, -1);
            sch.AddWire(d13, rA);
            sch.AddWire(rB, ledA);
            sch.AddWire(ledK, new GridPoint(g.X, ledK.Y), g);

            // Board ground to the same GND symbol through a T-junction on the LED ground wire.
            var unoGnd = uno.Transform(-7, -4);      // GND1
            sch.AddWire(unoGnd, new GridPoint(-10, -4), new GridPoint(-10, 25), new GridPoint(g.X, 25), new GridPoint(g.X, ledK.Y));
            return sch;
        }

        [Fact]
        public void WiredSchematicBuildsCorrectNetlist()
        {
            var netlist = BlinkSchematic().BuildNetlist(Lib);
            var gnd = netlist.Nets.Single(n => n.Name == "GND");
            Assert.True(gnd.IsGround);
            Assert.Contains(gnd.Connections, c => c.RefDes == "D1" && c.Pin == "K");
            Assert.Contains(gnd.Connections, c => c.RefDes == "U1" && c.Pin == "GND1");
            var d13 = netlist.NetOf("U1", "D13")!;
            Assert.Contains(d13.Connections, c => c.RefDes == "R1" && c.Pin == "A");
            Assert.Same(netlist.NetOf("R1", "B"), netlist.NetOf("D1", "A"));
        }

        [Fact]
        public void ArduinoBlinkCircuitLightsLed()
        {
            var compiled = CircuitCompiler.Compile(BlinkSchematic(), Lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            var pin = compiled.Device<DigitalPin>("U1.D13")!;
            var led = compiled.Device<Led>("D1")!;
            Assert.Equal(0.0, led.Brightness);

            pin.Mode = PinMode.OutputHigh;
            sim.Step(1e-3);
            Assert.InRange(compiled.PinVoltage(sim, "U1", "5V"), 4.9, 5.05);
            Assert.InRange(compiled.PinVoltage(sim, "U1", "D13"), 4.5, 5.05);
            Assert.InRange(led.Current, 0.011, 0.015);

            pin.Mode = PinMode.OutputLow;
            sim.Step(1e-3);
            Assert.True(led.Current < 1e-6);
        }

        [Fact]
        public void SchematicJsonRoundTrips()
        {
            var sch = BlinkSchematic();
            sch.Parts[1].Parameters["resistance"] = "330";
            var copy = Schematic.FromJson(sch.ToJson());
            Assert.Equal(sch.Parts.Count, copy.Parts.Count);
            Assert.Equal("330", copy.FindPart("R1")!.Parameters["RESISTANCE"]);
            Assert.Equal(sch.BuildNetlist(Lib).Nets.Count, copy.BuildNetlist(Lib).Nets.Count);
        }

        private static Schematic MotorDriverSchematic()
        {
            // 12 V battery -> MOSFET low-side switch for a motor with flyback diode, gate from a header, 5 V regulator, LED.
            var sch = new Schematic { Title = "Motor sürücü" };
            var bat = sch.AddPart(Lib, "bat-3s-lipo", new GridPoint(0, 0));
            var reg = sch.AddPart(Lib, "lm7805", new GridPoint(10, -3));
            var c1 = sch.AddPart(Lib, "ecap-100u", new GridPoint(20, 3), 90);
            var m = sch.AddPart(Lib, "motor-25ga370", new GridPoint(30, -10), 90);
            var d = sch.AddPart(Lib, "1n5819", new GridPoint(36, -10), 270);
            var q = sch.AddPart(Lib, "irlz44n", new GridPoint(30, 0));
            var rg = sch.AddPart(Lib, "res-100", new GridPoint(22, 10));
            var hdr = sch.AddPart(Lib, "hdr-1x3", new GridPoint(10, 12));
            var rl = sch.AddPart(Lib, "res-1000", new GridPoint(40, 8));
            var led = sch.AddPart(Lib, "led5-green", new GridPoint(48, 8));
            var gnd = sch.AddPart(Lib, "gnd", new GridPoint(0, 20));

            void L(SchematicPart p, double x, double y, string net) =>
                sch.Labels.Add(new NetLabel { Name = net, Position = p.Transform(x, y) });

            L(bat, 0, -3, "VBAT"); L(bat, 0, 3, "GND");
            L(reg, -4, 0, "VBAT"); L(reg, 0, 3, "GND"); L(reg, 4, 0, "5V");
            L(c1, -3, 0, "5V"); L(c1, 3, 0, "GND");
            L(m, -3, 0, "VBAT"); L(m, 3, 0, "DRAIN");
            L(d, -3, 0, "DRAIN"); L(d, 3, 0, "VBAT");
            L(q, 1, -3, "DRAIN"); L(q, 1, 3, "GND"); L(q, -3, 0, "GATE");
            L(rg, 3, 0, "GATE"); L(rg, -3, 0, "PWM");
            L(hdr, -3, -2, "5V"); L(hdr, -3, 0, "PWM"); L(hdr, -3, 2, "GND");
            L(rl, -3, 0, "5V"); L(rl, 3, 0, "LEDA");
            L(led, -3, 0, "LEDA"); L(led, 3, 0, "GND");
            _ = gnd;
            return sch;
        }

        [Fact]
        public void MotorDriverSimulatesRealistically()
        {
            var sch = MotorDriverSchematic();
            sch.Parts.Add(new SchematicPart { RefDes = "VPWM", PartId = "signal-generator", Position = new GridPoint(60, 60),
                Parameters = { ["frequency"] = "1k", ["duty"] = "1", ["amplitude"] = "5" } });
            sch.Labels.Add(new NetLabel { Name = "PWM", Position = sch.FindPart("VPWM")!.Transform(0, -3) });
            sch.Labels.Add(new NetLabel { Name = "GND", Position = sch.FindPart("VPWM")!.Transform(0, 3) });
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize();
            for (int i = 0; i < 100; i++) sim.Step(5e-3);

            var motor = compiled.Device<DcMotor>("M1")!;
            Assert.InRange(motor.OutputRpm, 400, 650);   // 6 V part on ~11.5 V: ~1.9x rated speed
            Assert.InRange(sim.NodeVoltage("5V"), 4.9, 5.05);
            // (5 V - 2.2 V) / 1 kΩ ≈ 2.8 mA on a 20 mA LED.
            Assert.InRange(compiled.Device<Led>("D2")!.Brightness, 0.11, 0.16);
        }

        [Fact]
        public void PcbPipelinePlacesRoutesAndPassesDrc()
        {
            var sch = MotorDriverSchematic();
            var board = BoardDocument.Synchronize(sch, Lib);
            board.Width = 70;
            board.Height = 50;
            Assert.Equal(10, board.Components.Count);   // ground symbol has no footprint
            Assert.Contains("VBAT", board.PowerNets);

            var placement = AutoPlacer.Place(board);
            Assert.Empty(placement.Unplaced);
            var routing = new AutoRouter(board).Route();
            Assert.True(routing.Complete, string.Join("; ", routing.Failed));

            var violations = DesignRuleChecker.Check(board);
            Assert.True(violations.All(v => v.Severity != DrcSeverity.Error),
                string.Join("\n", violations.Where(v => v.Severity == DrcSeverity.Error).Take(10)));

            Assert.All(board.Tracks.Where(t => t.Net == "VBAT"), t => Assert.Equal(board.Rules.PowerTrackWidth, t.Width));

            var files = GerberExporter.Generate(board, "motor");
            Assert.Contains("%FSLAX46Y46*%", files["motor-F_Cu.gtl"]);
            Assert.Contains("M02*", files["motor-B_Cu.gbl"]);
            Assert.Contains("T1C", files["motor-PTH.drl"]);
            Assert.Contains("IRLZ44N", files["motor-BOM.csv"]);
        }

        [Fact]
        public void DrcFindsShortsAndUnroutedNets()
        {
            var board = BoardDocument.Synchronize(MotorDriverSchematic(), Lib);
            AutoPlacer.Place(board);
            var tracks = board.Tracks;
            var pads = board.Pads().Where(p => p.Net == "VBAT").Take(1).Concat(board.Pads().Where(p => p.Net == "GND").Take(1)).ToList();
            tracks.Add(new Track { Net = "VBAT", X1 = pads[0].X, Y1 = pads[0].Y, X2 = pads[1].X, Y2 = pads[1].Y, Width = 0.3 });
            var violations = DesignRuleChecker.Check(board);
            Assert.Contains(violations, v => v.Rule == "Kısa devre");
            Assert.Contains(violations, v => v.Rule == "Bağlanmamış");
        }

        [Fact]
        public void TrackCurrentCapacityMatchesIpc2221()
        {
            var board = new BoardDocument();
            // 1 oz copper, 10 C rise: a 1 mm external trace carries roughly 2.3-2.6 A.
            Assert.InRange(board.CurrentCapacity(1.0), 2.2, 2.7);
            var t = new Track { X2 = 100, Width = 0.5 };
            // 100 mm x 0.5 mm x 35 um copper is close to 96 mOhm.
            Assert.InRange(board.TrackResistance(t), 0.09, 0.10);
        }
    }
}
