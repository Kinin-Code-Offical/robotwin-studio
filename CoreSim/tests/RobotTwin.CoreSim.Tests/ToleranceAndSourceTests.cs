using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Instruments")]
    public class ToleranceAndSourceTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;

        /// <summary>Connects a pin to a named net with a label at the pin tip.</summary>
        internal static void Tie(Schematic sch, SchematicPart part, string pin, string net)
        {
            var def = Lib.Get(part.PartId);
            var p = def.FindPin(pin)!;
            sch.Labels.Add(new NetLabel { Name = net, Position = part.Transform(p.X, p.Y) });
        }

        private static Schematic Divider(string top = "res-10000", string bottom = "res-10000")
        {
            var sch = new Schematic();
            var v = sch.AddPart(Lib, "psu-12v", new GridPoint(0, 0));
            v.Parameters["voltage"] = "10";
            var r1 = sch.AddPart(Lib, top, new GridPoint(20, 0));
            var r2 = sch.AddPart(Lib, bottom, new GridPoint(40, 0));
            Tie(sch, v, "+", "VIN");
            Tie(sch, v, "-", "GND");
            Tie(sch, r1, "A", "VIN");
            Tie(sch, r1, "B", "OUT");
            Tie(sch, r2, "A", "OUT");
            Tie(sch, r2, "B", "GND");
            return sch;
        }

        [Theory]
        [InlineData("5", 0.05, 0.05)]
        [InlineData("-20/+80", 0.2, 0.8)]
        [InlineData("±10%", 0.1, 0.1)]
        [InlineData("", 0.03, 0.03)]
        public void ToleranceBandsParse(string text, double lower, double upper)
        {
            var band = ToleranceBand.Parse(text, 3);
            Assert.Equal(lower, band.Lower, 9);
            Assert.Equal(upper, band.Upper, 9);
        }

        [Fact]
        public void NominalModeLeavesValuesAlone()
        {
            var sch = Divider();
            var compiled = CircuitCompiler.Compile(sch, Lib, new CompileOptions { Tolerances = new ToleranceSettings() });
            Assert.Empty(compiled.ToleranceDraws);
            Assert.Equal(10e3, compiled.Device<Resistor>("R1")!.Resistance, 9);
        }

        [Fact]
        public void RealisticDrawsAreReproduciblePerSeedAndWithinTheBand()
        {
            var sch = Divider();
            var settings = new ToleranceSettings { Mode = ToleranceMode.Realistic, Seed = 42 };
            var a = CircuitCompiler.Compile(sch, Lib, new CompileOptions { Tolerances = settings });
            var b = CircuitCompiler.Compile(sch, Lib, new CompileOptions { Tolerances = settings });
            var c = CircuitCompiler.Compile(sch, Lib, new CompileOptions { Tolerances = new ToleranceSettings { Mode = ToleranceMode.Realistic, Seed = 43 } });
            double ra = a.Device<Resistor>("R1")!.Resistance;
            Assert.Equal(ra, b.Device<Resistor>("R1")!.Resistance);
            Assert.NotEqual(ra, c.Device<Resistor>("R1")!.Resistance);
            Assert.NotEqual(ra, a.Device<Resistor>("R2")!.Resistance);
            Assert.InRange(ra, 9500, 10500);
            var draw = a.ToleranceDraws["R1"].Single();
            Assert.Equal(ra, draw.Actual, 9);
            Assert.Equal(10e3, draw.Nominal, 9);

            // The property panel preview shows the same value without compiling.
            var preview = ToleranceModel.Preview(sch, Lib, sch.FindPart("R1")!, settings);
            Assert.Equal(ra, preview.Single().Actual, 9);
        }

        [Fact]
        public void DrawsCoverTheBandAndElectrolyticsAreAsymmetric()
        {
            var def = Lib.Get("electrolytic");
            var settings = new ToleranceSettings { Mode = ToleranceMode.MonteCarlo, Seed = 5 };
            var values = Enumerable.Range(1, 400).Select(run =>
            {
                var p = Lib.ResolveParameters(def, Lib.FindPreset("ecap-100u"), null);
                return ToleranceModel.Apply(def, p, "C1", settings, run, 25.0).Single().DeviationPercent;
            }).ToList();
            Assert.All(values, v => Assert.InRange(v, -20.0 - 1e-9, 80.0 + 1e-9));
            Assert.Contains(values, v => v > 30);
            Assert.Contains(values, v => v < -10);

            var rdef = Lib.Get("resistor");
            var r = Enumerable.Range(1, 400).Select(run =>
            {
                var p = Lib.ResolveParameters(rdef, null, null);
                return ToleranceModel.Apply(rdef, p, "R7", settings, run, 25.0).Single().DeviationPercent;
            }).ToList();
            Assert.All(r, v => Assert.InRange(v, -5.0 - 1e-9, 5.0 + 1e-9));
            Assert.InRange(r.Average(), -0.6, 0.6);
        }

        [Fact]
        public void CeramicDielectricsFollowTheirTemperatureCharacteristic()
        {
            Assert.Equal(1.0, ToleranceModel.CeramicTemperatureFactor("C0G", 85), 6);
            double x7r = ToleranceModel.CeramicTemperatureFactor("X7R", 125);
            Assert.InRange(x7r, 0.85, 0.95);
            Assert.InRange(ToleranceModel.CeramicTemperatureFactor("X7R", -55), 0.85, 0.95);
            Assert.InRange(ToleranceModel.CeramicTemperatureFactor("Y5V", 85), 0.15, 0.25);
            Assert.Equal(1.0, ToleranceModel.CeramicTemperatureFactor("Y5V", 25), 6);
            Assert.InRange(ToleranceModel.ElectrolyticTemperatureFactor(-25), 0.75, 0.85);

            // A hot Y5V part in realistic mode loses most of its capacitance.
            var sch = new Schematic();
            var c = sch.AddPart(Lib, "cap-1u-y5v", new GridPoint(0, 0));
            sch.AddPart(Lib, "gnd", new GridPoint(10, 10));
            var compiled = CircuitCompiler.Compile(sch, Lib, new CompileOptions
            {
                Tolerances = new ToleranceSettings { Mode = ToleranceMode.Realistic },
                AmbientC = 85
            });
            Assert.True(compiled.Device<Capacitor>(c.RefDes)!.Capacitance < 0.5e-6);
        }

        [Fact]
        public void TransistorHfeAndLedVfSpread()
        {
            var settings = new ToleranceSettings { Mode = ToleranceMode.Realistic, Seed = 3 };
            var sch = new Schematic();
            var q1 = sch.AddPart(Lib, "bc547b", new GridPoint(0, 0));
            var q2 = sch.AddPart(Lib, "bc547b", new GridPoint(10, 0));
            var d1 = sch.AddPart(Lib, "led5-red", new GridPoint(20, 0));
            sch.AddPart(Lib, "gnd", new GridPoint(30, 10));
            var compiled = CircuitCompiler.Compile(sch, Lib, new CompileOptions { Tolerances = settings });
            double b1 = compiled.Device<Bjt>(q1.RefDes)!.ForwardBeta, b2 = compiled.Device<Bjt>(q2.RefDes)!.ForwardBeta;
            Assert.NotEqual(b1, b2);
            Assert.InRange(b1, 375 * 0.6, 375 * 1.4);
            Assert.Contains(compiled.ToleranceDraws[d1.RefDes], d => d.Key == "vf");
        }

        [Fact]
        public void MonteCarloShowsTheSpreadOfADivider()
        {
            var sch = Divider();
            var options = new MonteCarloOptions { Runs = 40, Seed = 9, Net = "OUT", Duration = 2e-3, Metric = MonteCarloMetric.Final };
            var result = MonteCarlo.Run(sch, Lib, options);
            Assert.Empty(result.Errors);
            Assert.Equal(5.0, result.Nominal, 3);
            Assert.Equal(40, result.Values.Count);
            Assert.Equal(40, result.Histogram.Sum());
            Assert.True(result.Min < 5.0 && result.Max > 5.0);
            Assert.InRange(result.Min, 4.7, 5.0);
            Assert.InRange(result.Max, 5.0, 5.3);
            Assert.True(result.StdDev > 0.01 && result.StdDev < 0.15);

            var again = MonteCarlo.Run(sch, Lib, options);
            Assert.Equal(result.Values, again.Values);
        }

        // ------------------------------------------------------------------ sources

        [Fact]
        public void FunctionWaveformShapes()
        {
            var sine = new FunctionWaveform(FunctionShape.Sine, -1, 1, 1000);
            double sum = 0;
            for (int i = 0; i < 1000; i++) sum += Math.Pow(sine.Value(i * 1e-6), 2);
            Assert.InRange(Math.Sqrt(sum / 1000), 0.705, 0.709);

            var pulse = new FunctionWaveform(FunctionShape.Pulse, 0, 5, 10e3, 0.1, 20e-9);
            Assert.Equal(0.0, pulse.Value(0), 9);
            Assert.Equal(5.0, pulse.Value(5e-6), 9);
            Assert.Equal(0.0, pulse.Value(50e-6), 9);
            Assert.Equal(20e-9, pulse.NextBreakpoint(0), 15);
            Assert.Equal(10e-6, pulse.NextBreakpoint(1e-6), 12);

            var tri = new FunctionWaveform(FunctionShape.Triangle, 0, 1, 1000, 0.25);
            Assert.Equal(1.0, tri.Value(0.25e-3), 9);
            Assert.Equal(0.5, tri.Value(0.625e-3), 9);

            var ramp = new FunctionWaveform(FunctionShape.Ramp, 0, 1, 1000, 0.5, 1e-6);
            Assert.InRange(ramp.Value(0.5e-3), 0.49, 0.51);
            Assert.InRange(ramp.Value(0.99975e-3), 0.2, 0.3);
        }

        [Fact]
        public void FiftyOhmAmplitudeSettingDoublesTheOpenCircuitVoltage()
        {
            double Peak(string load, bool terminated)
            {
                var sch = new Schematic();
                var g = sch.AddPart(Lib, "funcgen-sine-50", new GridPoint(0, 0));
                g.Parameters["load"] = load;
                Tie(sch, g, "+", "OUT");
                Tie(sch, g, "-", "GND");
                if (terminated)
                {
                    var r = sch.AddPart(Lib, "resistor", new GridPoint(20, 0));
                    r.Parameters["resistance"] = "50";
                    Tie(sch, r, "A", "OUT");
                    Tie(sch, r, "B", "GND");
                }

                var compiled = CircuitCompiler.Compile(sch, Lib);
                var sim = new AnalogSimulator(compiled.Circuit, new AnalogOptions { MaxStep = 1e-5 });
                sim.Initialize();
                double max = 0;
                sim.SubstepAccepted = _ => max = Math.Max(max, sim.NodeVoltage("OUT"));
                sim.Step(3e-3);
                return max;
            }

            Assert.InRange(Peak("50", false), 1.98, 2.02);
            Assert.InRange(Peak("50", true), 0.98, 1.02);
            Assert.InRange(Peak("hiz", false), 0.98, 1.02);
        }

        [Fact]
        public void BenchSupplyRegulatesVoltageThenLimitsCurrent()
        {
            var c = new AnalogCircuit();
            int p = c.Node("p");
            var psu = c.Add(new BenchSupply("PS1", p, 0, 12.0, 0.1));
            var load = c.Add(new Resistor("RL", p, 0, 1000.0));
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            sim.Step(1e-3);
            Assert.Equal(SupplyRegulation.ConstantVoltage, psu.Regulation);
            Assert.InRange(psu.OutputVoltage, 11.99, 12.01);
            Assert.InRange(psu.OutputCurrent, 0.0119, 0.0121);

            load.Resistance = 10.0;
            sim.NotifyDiscontinuity();
            sim.Step(5e-3);
            Assert.Equal(SupplyRegulation.ConstantCurrent, psu.Regulation);
            Assert.InRange(psu.OutputCurrent, 0.0999, 0.1001);
            Assert.InRange(psu.OutputVoltage, 0.99, 1.01);

            load.Resistance = 1000.0;
            sim.NotifyDiscontinuity();
            sim.Step(5e-3);
            Assert.Equal(SupplyRegulation.ConstantVoltage, psu.Regulation);

            psu.OutputEnabled = false;
            sim.Step(5e-3);
            Assert.InRange(psu.OutputVoltage, -0.01, 0.01);
        }

        [Fact]
        public void AcSupplyGivesItsRmsIntoAResistorAndAPhaseShiftedCurrentIntoACapacitor()
        {
            // [psu] 12 V RMS, 50 Hz into 100 Ω: 120 mA RMS, peaks at ±16.97 V.
            var c = new AnalogCircuit();
            int p = c.Node("p");
            var psu = c.Add(new BenchSupply("PS1", p, 0, 0.0, 1.0) { Output = SupplyOutput.Ac, AcRms = 12.0, Frequency = 50.0 });
            c.Add(new Resistor("RL", p, 0, 100.0));
            var sim = new AnalogSimulator(c);
            sim.Initialize();
            double vmax = double.MinValue, vmin = double.MaxValue;
            for (int i = 0; i < 200; i++)
            {
                sim.Step(0.2e-3);   // 40 ms: two cycles
                vmax = Math.Max(vmax, psu.OutputVoltage);
                vmin = Math.Min(vmin, psu.OutputVoltage);
            }

            Assert.InRange(vmax, 16.8, 17.0);
            Assert.InRange(vmin, -17.0, -16.8);
            Assert.InRange(psu.RmsVoltage, 11.8, 12.2);
            Assert.InRange(psu.RmsCurrent, 0.118, 0.122);
            Assert.Equal(SupplyRegulation.ConstantVoltage, psu.Regulation);

            // 10 µF behind 10 Ω at 50 Hz: |Z| = 318.5 Ω, 37.7 mA RMS, and the source sinks current half the time (four quadrants).
            var c2 = new AnalogCircuit();
            int q = c2.Node("q"), mid = c2.Node("m");
            var ac = c2.Add(new BenchSupply("PS2", q, 0, 0.0, 1.0) { Output = SupplyOutput.Ac, AcRms = 12.0, Frequency = 50.0 });
            c2.Add(new Resistor("R1", q, mid, 10.0));
            c2.Add(new Capacitor("C1", mid, 0, 10e-6));
            var sim2 = new AnalogSimulator(c2);
            sim2.Initialize();
            double imin = double.MaxValue;
            for (int i = 0; i < 300; i++)
            {
                sim2.Step(0.2e-3);
                if (i > 100) imin = Math.Min(imin, ac.OutputCurrent);
            }

            Assert.InRange(ac.RmsCurrent, 0.0360, 0.0395);
            Assert.True(imin < -0.04, "the capacitor gives charge back: the AC source sinks current");
        }

        [Fact]
        public void AcSupplyLimitsThePeakCurrent()
        {
            // 12 V RMS into 10 Ω would want 1.7 A peaks; limited to 0.5 A both ways the output clips.
            var c = new AnalogCircuit();
            int p = c.Node("p");
            var psu = c.Add(new BenchSupply("PS1", p, 0, 0.0, 0.5) { Output = SupplyOutput.Ac, AcRms = 12.0, Frequency = 50.0 });
            c.Add(new Resistor("RL", p, 0, 10.0));
            var sim = new AnalogSimulator(c);
            sim.Initialize();
            double imax = 0, imin = 0;
            bool cc = false;
            for (int i = 0; i < 200; i++)
            {
                sim.Step(0.2e-3);
                imax = Math.Max(imax, psu.OutputCurrent);
                imin = Math.Min(imin, psu.OutputCurrent);
                cc |= psu.Regulation == SupplyRegulation.ConstantCurrent;
            }

            Assert.True(cc);
            Assert.InRange(imax, 0.49, 0.51);
            Assert.InRange(imin, -0.51, -0.49);
        }

        [Fact]
        public void BenchSupplyOvpTripsWhenBackFed()
        {
            var c = new AnalogCircuit();
            int p = c.Node("p"), e = c.Node("e");
            var psu = c.Add(new BenchSupply("PS1", p, 0, 5.0, 1.0) { OvpLevel = 6.0 });
            c.Add(new Resistor("RL", p, 0, 100.0));
            var ext = c.Add(new VoltageSource("V2", e, 0, PulseWaveform.FromDuty(0, 9, 50, 0.5, 5e-3)));
            c.Add(new Resistor("R2", e, p, 10.0));
            var sim = new AnalogSimulator(c);
            sim.Initialize();
            sim.Step(2e-3);
            Assert.False(psu.OvpTripped);
            sim.Step(10e-3);
            Assert.True(psu.OvpTripped);
            Assert.Contains(sim.Events, ev => ev.DeviceId == "PS1" && ev.Message.Contains("OVP"));
            _ = ext;
        }
    }
}
