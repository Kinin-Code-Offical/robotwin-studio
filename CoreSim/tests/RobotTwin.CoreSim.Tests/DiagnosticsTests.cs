using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Instruments")]
    public class DiagnosticsTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;

        private static void Tie(Schematic sch, SchematicPart part, string pin, string net) => ToleranceAndSourceTests.Tie(sch, part, pin, net);

        private static SchematicPart Add(Schematic sch, string preset, double x, double y = 0) => sch.AddPart(Lib, preset, new GridPoint(x, y));

        private static List<Diagnosis> Analyze(Schematic sch) => CircuitDiagnostics.Analyze(sch, Lib);

        private static (List<Diagnosis> List, AnalogSimulator Sim, CompiledCircuit Compiled, StressTracker Stress) Simulate(Schematic sch, double seconds, bool damage = false)
        {
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var sim = new AnalogSimulator(compiled.Circuit, new AnalogOptions { MaxStep = 1e-3, EnableDamage = damage });
            sim.Initialize();
            var stress = new StressTracker();
            while (sim.Time < seconds - 1e-12)
            {
                sim.Step(Math.Min(5e-3, seconds - sim.Time));
                stress.Sample(compiled.Circuit, sim.Time);
            }

            return (CircuitDiagnostics.Analyze(sch, Lib, compiled, sim, stress), sim, compiled, stress);
        }

        [Fact]
        public void LedStraightAcrossABatteryNeedsAResistor()
        {
            var sch = new Schematic();
            var bat = Add(sch, "bat-9v", 0);
            var led = Add(sch, "led5-red", 20);
            Tie(sch, bat, "+", "VBAT");
            Tie(sch, bat, "-", "GND");
            Tie(sch, led, "A", "VBAT");
            Tie(sch, led, "K", "GND");

            var d = Analyze(sch).Single(x => x.Code == "LED_NO_RESISTOR");
            Assert.Equal(DiagnosisSeverity.Error, d.Severity);
            Assert.Equal(led.RefDes, d.Parts[0]);
            Assert.Contains("390", d.Suggestion);
        }

        [Fact]
        public void LedWithASeriesResistorIsFine()
        {
            var sch = new Schematic();
            var bat = Add(sch, "bat-9v", 0);
            var r = Add(sch, "res-470", 20);
            var led = Add(sch, "led5-red", 40);
            Tie(sch, bat, "+", "VBAT");
            Tie(sch, bat, "-", "GND");
            Tie(sch, r, "A", "VBAT");
            Tie(sch, r, "B", "LA");
            Tie(sch, led, "A", "LA");
            Tie(sch, led, "K", "GND");

            var list = Simulate(sch, 0.05).List;
            Assert.DoesNotContain(list, x => x.Severity == DiagnosisSeverity.Error);
            Assert.DoesNotContain(list, x => x.Code == "LED_NO_RESISTOR");
        }

        [Fact]
        public void SupplyShortedThroughAFuseIsFound()
        {
            var sch = new Schematic();
            var psu = Add(sch, "psu-5v", 0);
            var f = Add(sch, "fuse-500mA", 20);
            Tie(sch, psu, "+", "VCC");
            Tie(sch, psu, "-", "GND");
            Tie(sch, f, "A", "VCC");
            Tie(sch, f, "B", "GND");

            var d = Analyze(sch).Single(x => x.Code == "SUPPLY_SHORT");
            Assert.Contains(psu.RefDes, d.Parts);
            Assert.Contains(f.RefDes, d.Parts);
        }

        [Fact]
        public void MultimeterAmpJackAcrossASupplyIsAShort()
        {
            var sch = new Schematic();
            var psu = Add(sch, "psu-5v", 0);
            var dmm = Add(sch, "dmm", 20);
            Tie(sch, psu, "+", "VCC");
            Tie(sch, psu, "-", "GND");
            Tie(sch, dmm, "A", "VCC");
            Tie(sch, dmm, "COM", "GND");

            Assert.Contains(Analyze(sch), x => x.Code == "SUPPLY_SHORT" && x.Parts.Contains(dmm.RefDes));
        }

        [Fact]
        public void ReversedElectrolyticAndReversedLed()
        {
            var sch = new Schematic();
            var psu = Add(sch, "psu-12v", 0);
            var c = Add(sch, "ecap-100u", 20);
            var led = Add(sch, "led5-green", 40);
            Tie(sch, psu, "+", "12V");
            Tie(sch, psu, "-", "GND");
            Tie(sch, c, "+", "GND");
            Tie(sch, c, "-", "12V");
            Tie(sch, led, "K", "12V");
            Tie(sch, led, "A", "GND");

            var list = Analyze(sch);
            Assert.Contains(list, x => x.Code == "CAP_REVERSED" && x.Parts[0] == c.RefDes);
            Assert.Contains(list, x => x.Code == "LED_REVERSED" && x.Parts[0] == led.RefDes);
        }

        [Fact]
        public void FloatingCmosInputsAndSwitchWithoutPullDown()
        {
            var sch = new Schematic();
            var psu = Add(sch, "psu-5v", -20);
            var u = Add(sch, "74hc00", 0);
            var sw = Add(sch, "toggle-spst", 20);
            Tie(sch, psu, "+", "5V");
            Tie(sch, psu, "-", "GND");
            Tie(sch, u, "VCC", "5V");
            Tie(sch, u, "GND", "GND");
            Tie(sch, u, "B", "IN_B");
            Tie(sch, sw, "A", "5V");
            Tie(sch, sw, "B", "IN_B");

            var list = Analyze(sch);
            var open = list.Single(x => x.Code == "FLOATING_INPUT" && x.Title.Contains(".A:"));
            Assert.Equal(u.RefDes, open.Parts[0]);
            var viaSwitch = list.Single(x => x.Code == "FLOATING_INPUT" && x.Title.Contains(".B:"));
            Assert.Contains(sw.RefDes, viaSwitch.Parts);
            Assert.Contains("açık", viaSwitch.Title);

            // A pull-down fixes the switched input.
            var r = Add(sch, "res-10000", 40);
            Tie(sch, r, "A", "IN_B");
            Tie(sch, r, "B", "GND");
            Assert.DoesNotContain(Analyze(sch), x => x.Code == "FLOATING_INPUT" && x.Title.Contains(".B:"));
        }

        [Fact]
        public void OverloadedResistorShowsNegativeMargin()
        {
            var sch = new Schematic();
            var psu = Add(sch, "psu-9v", 0);
            var r = Add(sch, "res-100", 20);
            Tie(sch, psu, "+", "9V");
            Tie(sch, psu, "-", "GND");
            Tie(sch, r, "A", "9V");
            Tie(sch, r, "B", "GND");

            var d = Simulate(sch, 0.1).List.Single(x => x.Code == "OVER_RATING" && x.Parts[0] == r.RefDes);
            Assert.Equal(DiagnosisSeverity.Error, d.Severity);
            Assert.InRange(d.MarginPercent!.Value, -240, -210);
            Assert.Contains("2 W", d.Suggestion);
        }

        [Fact]
        public void RegulatorWithoutHeadroomIsInDropout()
        {
            var sch = new Schematic();
            var psu = Add(sch, "psu-5v", 0);
            psu.Parameters["voltage"] = "6";
            var reg = Add(sch, "lm7805", 20);
            var load = Add(sch, "res-100", 40);
            Tie(sch, psu, "+", "VIN");
            Tie(sch, psu, "-", "GND");
            Tie(sch, reg, "IN", "VIN");
            Tie(sch, reg, "GND", "GND");
            Tie(sch, reg, "OUT", "5V");
            Tie(sch, load, "A", "5V");
            Tie(sch, load, "B", "GND");

            Assert.Contains(Analyze(sch), x => x.Code == "REG_DROPOUT" && x.Parts[0] == reg.RefDes);
            var dyn = Simulate(sch, 0.05).List.Single(x => x.Code == "REG_DROPOUT");
            Assert.Contains("6.0", dyn.Detail);
        }

        [Fact]
        public void MotorStallCurrentAboveDriverRatingAndMotorOnPin()
        {
            var sch = new Schematic();
            var drv = Add(sch, "tb6612-ch", 0);
            var m = Add(sch, "motor-25ga370", 20);
            Tie(sch, drv, "OUT", "M+");
            Tie(sch, m, "A", "M+");
            Tie(sch, m, "B", "GND");
            Tie(sch, drv, "GND", "GND");
            var d = Analyze(sch).Single(x => x.Code == "DRIVER_STALL");
            Assert.Equal(m.RefDes, d.Parts[0]);
            Assert.True(d.MarginPercent < 0);

            var sch2 = new Schematic();
            var uno = Add(sch2, "arduino-uno", 0);
            var m2 = Add(sch2, "motor-tt", 30);
            Tie(sch2, uno, "D9", "PWM");
            Tie(sch2, uno, "GND1", "GND");
            Tie(sch2, m2, "A", "PWM");
            Tie(sch2, m2, "B", "GND");
            Assert.Contains(Analyze(sch2), x => x.Code == "MOTOR_ON_PIN" && x.Parts[0] == m2.RefDes);
        }

        [Fact]
        public void TransistorSwitchedMotorWithoutFlybackDiode()
        {
            var sch = new Schematic();
            var bat = Add(sch, "bat-3s-lipo", 0);
            var q = Add(sch, "irlz44n", 20);
            var m = Add(sch, "motor-25ga370", 40);
            Tie(sch, bat, "+", "VBAT");
            Tie(sch, bat, "-", "GND");
            Tie(sch, m, "A", "VBAT");
            Tie(sch, m, "B", "D");
            Tie(sch, q, "D", "D");
            Tie(sch, q, "S", "GND");
            Tie(sch, q, "G", "VBAT");
            Assert.Contains(Analyze(sch), x => x.Code == "NO_FLYBACK" && x.Parts[0] == m.RefDes);

            var d = Add(sch, "1n5819", 60);
            Tie(sch, d, "A", "D");
            Tie(sch, d, "K", "VBAT");
            Assert.DoesNotContain(Analyze(sch), x => x.Code == "NO_FLYBACK");
        }

        [Fact]
        public void UnconnectedPinAndMissingGround()
        {
            var sch = new Schematic();
            var psu = Add(sch, "psu-5v", 0);
            var r = Add(sch, "res-1000", 20);
            Tie(sch, psu, "+", "VCC");
            Tie(sch, psu, "-", "RET");
            Tie(sch, r, "A", "VCC");

            var list = Analyze(sch);
            Assert.Contains(list, x => x.Code == "OPEN_PIN" && x.Title.StartsWith(r.RefDes + ".B"));
            Assert.Contains(list, x => x.Code == "NO_GROUND");
        }

        [Fact]
        public void BurnedResistorIsExplained()
        {
            var sch = new Schematic();
            var psu = Add(sch, "psu-9v", 0);
            var r = Add(sch, "res-10", 20);
            Tie(sch, psu, "+", "9V");
            Tie(sch, psu, "-", "GND");
            Tie(sch, r, "A", "9V");
            Tie(sch, r, "B", "GND");

            var (list, _, compiled, stress) = Simulate(sch, 1.0, damage: true);
            var device = compiled.Device<Resistor>(r.RefDes)!;
            Assert.True(device.IsFailed);
            var d = list.Single(x => x.Code == "FAILED");
            Assert.Equal(r.RefDes, d.Parts[0]);
            Assert.Contains("anma", d.Detail);
            Assert.Contains("W", d.Detail);
            Assert.Contains("W'lık", d.Suggestion);
            string why = CircuitDiagnostics.ExplainFailure(compiled, device, stress);
            Assert.Contains("×", why);
        }

        [Fact]
        public void ExamplesOfTheStudioHaveNoErrors()
        {
            // The LED-and-button example drawn the same way as the Studio example.
            var sch = new Schematic();
            var bat = Add(sch, "bat-9v", 0);
            var sw = Add(sch, "tact-6mm", 20);
            var r = Add(sch, "res-470", 40);
            var led = Add(sch, "led5-red", 60);
            Tie(sch, bat, "+", "VBAT");
            Tie(sch, bat, "-", "GND");
            Tie(sch, sw, "A", "VBAT");
            Tie(sch, sw, "B", "S");
            Tie(sch, r, "A", "S");
            Tie(sch, r, "B", "LA");
            Tie(sch, led, "A", "LA");
            Tie(sch, led, "K", "GND");
            Assert.DoesNotContain(Simulate(sch, 0.05).List, x => x.Severity == DiagnosisSeverity.Error);
        }

        [Fact]
        public void StandardValuesRoundUp()
        {
            Assert.Equal(390, CircuitDiagnostics.StandardResistor(350), 6);
            Assert.Equal(1000, CircuitDiagnostics.StandardResistor(1000), 6);
            Assert.Equal(1200, CircuitDiagnostics.StandardResistor(1050), 6);
            Assert.Equal(0.5, CircuitDiagnostics.StandardPower(0.3), 9);
        }
            [Fact]
        public void RepairedPartComesBackCoolAndWorks()
        {
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc");
            var src = c.Add(new VoltageSource("V1", vcc, 0, 9.0));
            var r = c.Add(new Resistor("R1", vcc, 0, 10.0) { PowerRating = 0.25 });
            var sim = new AnalogSimulator(c);
            sim.Initialize();
            sim.Step(1.0);
            Assert.True(r.IsFailed);

            // The cause is fixed (supply lowered to a safe level) and the part replaced.
            ((DcWaveform)src.Waveform).Level = 1.0;
            r.Repair();
            Assert.False(r.IsFailed);
            Assert.Equal(25.0, r.Thermal!.TemperatureC, 6);
            sim.Step(0.5);
            Assert.False(r.IsFailed);
            Assert.InRange(r.Current, 0.099, 0.101);
        }

        [Fact]
        public void PlacedMultimeterMeasuresSeriesCurrentFromTheSchematic()
        {
            var sch = new Schematic();
            var psu = Add(sch, "psu-5v", 0);
            var dmm = Add(sch, "dmm-ampere", 20);
            var r = Add(sch, "res-100", 40);
            Tie(sch, psu, "+", "VCC");
            Tie(sch, psu, "-", "GND");
            Tie(sch, dmm, "mA", "VCC");
            Tie(sch, dmm, "COM", "M");
            Tie(sch, r, "A", "M");
            Tie(sch, r, "B", "GND");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var sim = new AnalogSimulator(compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize();
            sim.Step(1.6);
            var meter = compiled.Device<RobotTwin.CoreSim.Instruments.Multimeter>(dmm.RefDes)!;
            Assert.Equal(RobotTwin.CoreSim.Instruments.MultimeterMode.DcCurrent, meter.Mode);
            Assert.Equal("mA", meter.Reading.Unit);
            Assert.InRange(meter.Reading.Value, 0.0485, 0.0496);
            Assert.DoesNotContain(Analyze(sch), x => x.Code == "SUPPLY_SHORT");
        }
    }
}
