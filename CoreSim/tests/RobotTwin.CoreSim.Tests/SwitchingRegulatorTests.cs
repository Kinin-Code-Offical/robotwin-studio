using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[smps] Buck and boost modules on the bench: regulation, efficiency, soft-start, dropout, limit, lockout.</summary>
    public class SwitchingRegulatorTests
    {
        private readonly ITestOutputHelper _out;

        public SwitchingRegulatorTests(ITestOutputHelper output) => _out = output;

        private sealed class Bench
        {
            public AnalogSimulator Sim = null!;
            public SwitchingRegulator Reg = null!;
            public Resistor Load = null!;
            public VoltageSource Supply = null!;
            public int Out;
        }

        /// <summary>A supply (with its lead resistance), the module with its input and output capacitors, a load.</summary>
        private static Bench Build(ConverterTopology topology, double vin, double vout, double load, Action<SwitchingRegulator>? setup = null, double cout = 220e-6)
        {
            var c = new AnalogCircuit();
            int src = c.Node("src"), inp = c.Node("in"), outp = c.Node("out");
            var supply = c.Add(new VoltageSource("V1", src, 0, vin) { SeriesResistance = 0.02 });
            c.Add(new Resistor("RLEAD", src, inp, 0.03));
            c.Add(new Capacitor("CIN", inp, 0, 100e-6) { SeriesResistance = 0.05 });
            var reg = c.Add(new SwitchingRegulator("U1", topology, inp, 0, outp) { OutputVoltage = vout });
            setup?.Invoke(reg);
            c.Add(new Capacitor("COUT", outp, 0, cout) { SeriesResistance = 0.05 });
            var r = c.Add(new Resistor("RL", outp, 0, load));
            var sim = new AnalogSimulator(c, new AnalogOptions { MaxStep = 2e-5 });
            sim.Initialize();
            return new Bench { Sim = sim, Reg = reg, Load = r, Supply = supply, Out = outp };
        }

        private static void Run(Bench b, double seconds)
        {
            double until = b.Sim.Time + seconds;
            while (b.Sim.Time < until - 1e-9) b.Sim.Step(Math.Min(1e-3, until - b.Sim.Time));
        }

        private static readonly Action<SwitchingRegulator> Lm2596 = r =>
        {
            r.CurrentLimit = 3.5;
            r.SwitchDrop = 1.1;
            r.SwitchResistance = 0.05;
            r.Frequency = 150e3;
            r.Inductance = 33e-6;
            r.SoftStart = 2e-3;
        };

        [Fact]
        public void ABuckHoldsFiveVoltsFromTwelveAndDrawsWhatItDelivers()
        {
            var b = Build(ConverterTopology.Buck, 12, 5, 5, Lm2596);
            Run(b, 0.02);
            double vout = b.Sim.NodeVoltage(b.Out);
            _out.WriteLine($"12 → {vout:0.000} V, {b.Reg.OutputCurrent:0.000} A çıkış, {b.Reg.InputCurrent:0.000} A giriş, verim %{b.Reg.Efficiency * 100:0.0}, kayıp {b.Reg.Losses:0.00} W");
            Assert.InRange(vout, 4.97, 5.03);
            Assert.InRange(b.Reg.Efficiency, 0.75, 0.93);   // the LM2596's datasheet: ~80–88 % at 12 → 5 V, 1 A
            Assert.InRange(b.Reg.InputCurrent, 5.0 / 12 / 0.93, 5.0 / 12 / 0.75);
        }

        [Fact]
        public void ItComesUpOverItsSoftStart()
        {
            var b = Build(ConverterTopology.Buck, 12, 5, 10, Lm2596);
            Run(b, 1.0e-3);
            double half = b.Sim.NodeVoltage(b.Out);
            Run(b, 3e-3);
            double up = b.Sim.NodeVoltage(b.Out);
            _out.WriteLine($"1 ms: {half:0.00} V, 4 ms: {up:0.00} V");
            Assert.InRange(half, 1.8, 3.2);   // half way up a 2 ms ramp
            Assert.InRange(up, 4.95, 5.05);
        }

        [Fact]
        public void FromFiveAndAHalfVoltsALm2596CannotMakeFive()
        {
            var b = Build(ConverterTopology.Buck, 5.5, 5, 5, Lm2596);
            Run(b, 0.02);
            double vout = b.Sim.NodeVoltage(b.Out);
            _out.WriteLine($"5,5 → {vout:0.000} V (düşüm), kipte: {b.Reg.InDropout}");
            Assert.True(b.Reg.InDropout);
            Assert.InRange(vout, 3.6, 4.6);   // its bipolar switch drops about 1.1 V
        }

        [Fact]
        public void IntoTooHeavyALoadItHoldsItsCurrentLimit()
        {
            var b = Build(ConverterTopology.Buck, 12, 5, 0.5, Lm2596);
            Run(b, 0.02);
            double vout = b.Sim.NodeVoltage(b.Out);
            _out.WriteLine($"0,5 Ω: {vout:0.00} V, {b.Reg.OutputCurrent:0.00} A, sınırda {b.Reg.InCurrentLimit}");
            Assert.True(b.Reg.InCurrentLimit);
            Assert.InRange(b.Reg.OutputCurrent, 3.4, 3.6);
            Assert.InRange(vout, 1.6, 1.9);
        }

        [Fact]
        public void TenTimesTheOutputCapacitanceStillSettlesWithoutRinging()
        {
            var b = Build(ConverterTopology.Buck, 12, 5, 25, Lm2596, cout: 2.2e-3);
            Run(b, 0.03);
            b.Load.Resistance = 2.5;
            int crossings = 0;
            double last = b.Sim.NodeVoltage(b.Out) - 5;
            for (int i = 0; i < 600; i++)
            {
                b.Sim.Step(5e-5);
                double e = b.Sim.NodeVoltage(b.Out) - 5;
                if (Math.Sign(e) != Math.Sign(last) && Math.Abs(e - last) > 1e-4) crossings++;
                last = e;
            }

            _out.WriteLine($"2,2 mF: 30 ms sonra {b.Sim.NodeVoltage(b.Out):0.000} V, 5 V'u {crossings} kez geçti");
            Assert.InRange(b.Sim.NodeVoltage(b.Out), 4.98, 5.02);
            Assert.InRange(crossings, 0, 3);
        }

        [Theory]
        [InlineData("lm2596-module", 12.0, 5.0, 5.0)]
        [InlineData("mp1584-module", 12.0, 5.0, 5.0)]
        [InlineData("xl4015-module", 24.0, 12.0, 6.0)]
        [InlineData("mt3608-module", 5.0, 12.0, 24.0)]
        [InlineData("mt3608-module", 5.0, 5.2, 10.0)]   // just above its input: a few per cent of duty
        public void TheLibrarysModulesRegulateInACircuit(string preset, double vin, double vout, double load)
        {
            var lib = RobotTwin.CoreSim.Design.PartLibrary.Default;
            var sch = new RobotTwin.CoreSim.Design.Schematic();
            var psu = sch.AddPart(lib, "psu-12v", new RobotTwin.CoreSim.Design.GridPoint(0, 0));
            psu.Parameters["voltage"] = vin.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var mod = sch.AddPart(lib, preset, new RobotTwin.CoreSim.Design.GridPoint(20, 0));
            mod.Parameters["vout"] = vout.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var r = sch.AddPart(lib, "res-1R-1W", new RobotTwin.CoreSim.Design.GridPoint(40, 0));
            r.Parameters["resistance"] = load.ToString(System.Globalization.CultureInfo.InvariantCulture);
            r.Parameters["power"] = "50";
            ToleranceAndSourceTests.Tie(sch, psu, "+", "VIN");
            ToleranceAndSourceTests.Tie(sch, psu, "-", "GND");
            ToleranceAndSourceTests.Tie(sch, mod, "IN+", "VIN");
            ToleranceAndSourceTests.Tie(sch, mod, "IN-", "GND");
            ToleranceAndSourceTests.Tie(sch, mod, "OUT+", "OUT");
            ToleranceAndSourceTests.Tie(sch, mod, "OUT-", "GND");
            ToleranceAndSourceTests.Tie(sch, r, "A", "OUT");
            ToleranceAndSourceTests.Tie(sch, r, "B", "GND");
            var compiled = RobotTwin.CoreSim.Design.CircuitCompiler.Compile(sch, lib);
            var sim = new AnalogSimulator(compiled.Circuit, new AnalogOptions { MaxStep = 5e-5 });
            sim.Initialize();
            while (sim.Time < 0.03 - 1e-9) sim.Step(1e-3);
            double v = compiled.PinVoltage(sim, mod.RefDes, "OUT+") - compiled.PinVoltage(sim, mod.RefDes, "OUT-");
            var reg = (SwitchingRegulator)compiled.DevicesOf(mod.RefDes).Single(d => d is SwitchingRegulator);
            _out.WriteLine($"{preset}: {vin} → {v:0.000} V, {reg.OutputCurrent:0.00} A, giriş {reg.InputCurrent:0.00} A, verim %{reg.Efficiency * 100:0.0}");
            Assert.InRange(v, vout * 0.99, vout * 1.01);
            Assert.InRange(reg.Efficiency, 0.7, 0.96);
        }

        [Fact]
        public void BelowItsLockoutItStaysOff()
        {
            var b = Build(ConverterTopology.Buck, 3.5, 5, 10, Lm2596);
            Run(b, 0.01);
            Assert.False(b.Reg.Running);
            Assert.InRange(b.Sim.NodeVoltage(b.Out), -0.01, 0.01);
        }

        [Fact]
        public void ABoostLiftsALithiumCellToFiveVolts()
        {
            var b = Build(ConverterTopology.Boost, 3.7, 5, 10, r =>
            {
                r.CurrentLimit = 4;
                r.SwitchResistance = 0.08;
                r.MaxDuty = 0.9;
                r.Frequency = 1.2e6;
                r.Inductance = 22e-6;
                r.MinInput = 2.0;
                r.QuiescentCurrent = 0.3e-3;
            });
            Run(b, 0.02);
            double vout = b.Sim.NodeVoltage(b.Out);
            _out.WriteLine($"3,7 → {vout:0.000} V, {b.Reg.OutputCurrent:0.000} A çıkış, {b.Reg.InputCurrent:0.000} A giriş, verim %{b.Reg.Efficiency * 100:0.0}");
            Assert.InRange(vout, 4.97, 5.03);
            Assert.InRange(b.Reg.InputCurrent, 0.5 * 5 / 3.7 / 0.95, 0.5 * 5 / 3.7 / 0.75);
        }

        [Fact]
        public void ALoadStepDipsTheOutputAndItRecovers()
        {
            var b = Build(ConverterTopology.Buck, 12, 5, 25, Lm2596);
            Run(b, 0.01);
            b.Load.Resistance = 2.5;   // 0.2 A → 2 A
            double low = 99;
            for (int i = 0; i < 100; i++)
            {
                b.Sim.Step(5e-5);
                low = Math.Min(low, b.Sim.NodeVoltage(b.Out));
            }

            Run(b, 0.01);
            double settled = b.Sim.NodeVoltage(b.Out);
            _out.WriteLine($"0,2 → 2 A: en düşük {low:0.000} V, 10 ms sonra {settled:0.000} V");
            Assert.InRange(low, 4.2, 4.98);   // it dips while the loop catches up
            Assert.InRange(settled, 4.97, 5.03);
        }
    }
}
