using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Instruments;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Instruments")]
    public class MultimeterTests
    {
        private static AnalogSimulator Run(AnalogCircuit c, double seconds, double maxStep = 1e-3, bool damage = true)
        {
            var sim = new AnalogSimulator(c, new AnalogOptions { MaxStep = maxStep, EnableDamage = damage });
            sim.Initialize();
            while (sim.Time < seconds - 1e-12) sim.Step(Math.Min(10e-3, seconds - sim.Time));
            return sim;
        }

        /// <summary>The reading must be within ±(pct % of reading + n counts) of the true input (plus <paramref name="extra"/>).</summary>
        private static void AssertWithinSpec(Multimeter dmm, double expected, double extra = 0.0)
        {
            var r = dmm.Reading;
            Assert.True(r.Valid, "no reading yet");
            Assert.False(r.Overload, $"overload: {r.Text}");
            var (pct, digits) = MultimeterSpec.Accuracy(dmm.Mode);
            double tol = pct / 100.0 * Math.Abs(expected) + digits * dmm.Range.Resolution + extra;
            Assert.True(Math.Abs(r.Value - expected) <= tol,
                $"reading {r.Text} ({r.Value:G6}) vs expected {expected:G6}, allowed ±{tol:G3}");
        }

        [Fact]
        public void DcVoltageReadsWithinSpecAndLoadsTheCircuitWithTenMegohm()
        {
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc"), mid = c.Node("mid");
            c.Add(new VoltageSource("V1", vcc, 0, 10.0));
            c.Add(new Resistor("R1", vcc, mid, 10e3));
            c.Add(new Resistor("R2", mid, 0, 10e3));
            var dmm = c.Add(new Multimeter("XMM1", mid, 0));
            Run(c, 1.1);

            double r2 = 1.0 / (1.0 / 10e3 + 1.0 / 10e6);
            double loaded = 10.0 * r2 / (10e3 + r2);
            AssertWithinSpec(dmm, loaded);
            Assert.Equal("V", dmm.Reading.Unit);
            Assert.Equal(3, dmm.Reading.Digits.Split('.')[1].Length);
            Assert.True(dmm.Reading.AutoRange);
        }

        [Fact]
        public void HighImpedanceNodeReadsLowBecauseOfMeterLoading()
        {
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc"), mid = c.Node("mid");
            c.Add(new VoltageSource("V1", vcc, 0, 10.0));
            c.Add(new Resistor("R1", vcc, mid, 10e6));
            c.Add(new Resistor("R2", mid, 0, 10e6));
            var dmm = c.Add(new Multimeter("XMM1", mid, 0));
            Run(c, 1.1);

            // 10 MΩ ∥ 10 MΩ = 5 MΩ: the meter reads 3.33 V, not the unloaded 5 V.
            AssertWithinSpec(dmm, 10.0 / 3.0);
            Assert.True(dmm.Reading.Value < 4.0);
        }

        [Fact]
        public void AutorangeShowsMillivoltsAndOverloadShowsOL()
        {
            var c = new AnalogCircuit();
            int a = c.Node("a"), b = c.Node("b");
            c.Add(new VoltageSource("V1", a, 0, 0.1234));
            c.Add(new VoltageSource("V2", b, 0, 1500.0));
            var low = c.Add(new Multimeter("XMM1", a, 0));
            var high = c.Add(new Multimeter("XMM2", b, 0));
            Run(c, 1.1);

            Assert.Equal("mV", low.Reading.Unit);
            Assert.Equal("600 mV", low.Reading.RangeLabel);
            AssertWithinSpec(low, 0.1234);
            Assert.True(high.Reading.Overload);
            Assert.Equal("OL", high.Reading.Digits);
        }

        [Fact]
        public void AcVoltageIsTrueRmsOfTheAcComponent()
        {
            var c = new AnalogCircuit();
            int s = c.Node("s"), q = c.Node("q");
            c.Add(new VoltageSource("V1", s, 0, new SineWaveform(2.0, 1.0, 1000.0)));
            c.Add(new VoltageSource("V2", q, 0, new PulseWaveform(0.0, 5.0, 10e-3, 5e-3 - 1e-8, 0, 1e-8, 1e-8)));
            var ac = c.Add(new Multimeter("XMM1", s, 0) { Mode = MultimeterMode.AcVoltage });
            var dc = c.Add(new Multimeter("XMM2", s, 0));
            var sq = c.Add(new Multimeter("XMM3", q, 0) { Mode = MultimeterMode.AcVoltage });
            Run(c, 1.1, 2e-5);

            AssertWithinSpec(ac, 1.0 / Math.Sqrt(2.0), 0.004);
            AssertWithinSpec(dc, 2.0, 0.002);
            AssertWithinSpec(sq, 2.5, 0.01);
        }

        [Fact]
        public void DcCurrentInSeriesShowsBurdenVoltageEffect()
        {
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc"), m = c.Node("m");
            c.Add(new VoltageSource("V1", vcc, 0, 5.0));
            var dmm = c.Add(new Multimeter("XMM1", -1, m, vcc, -1) { Mode = MultimeterMode.DcCurrent });
            c.Add(new Resistor("RL", m, 0, 100.0));
            Run(c, 1.6);

            // 1.8 Ω shunt in series: 5 V / 101.8 Ω, not 50 mA.
            double expected = 5.0 / 101.8;
            Assert.Equal("mA", dmm.Reading.Unit);
            Assert.Equal("60 mA", dmm.Reading.RangeLabel);
            AssertWithinSpec(dmm, expected);
            Assert.True(dmm.Reading.Value < 0.0495);
            Assert.InRange(dmm.BurdenVoltage, 0.085, 0.092);
        }

        [Fact]
        public void LeadsInTheMilliampJackAcrossASupplyBlowTheFuse()
        {
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc");
            c.Add(new VoltageSource("V1", vcc, 0, 5.0) { SeriesResistance = 0.1 });
            var dmm = c.Add(new Multimeter("XMM1", -1, 0, vcc, -1));
            var sim = new AnalogSimulator(c, new AnalogOptions { MaxStep = 1e-4 });
            sim.Initialize();
            sim.Step(0.1);

            Assert.True(dmm.MilliAmpFuseBlown);
            Assert.Contains(sim.Events, e => e.DeviceId == "XMM1" && e.Kind == AnalogEventKind.Failure && e.Message.Contains("sigorta"));
            sim.Step(0.01);
            Assert.True(Math.Abs(dmm.LastCurrent) < 1e-6);

            dmm.Repair();
            Assert.False(dmm.MilliAmpFuseBlown);
        }

        [Fact]
        public void ResistanceOfAnUnpoweredResistor()
        {
            var c = new AnalogCircuit();
            int a = c.Node("a");
            c.Add(new Resistor("R1", a, 0, 4700.0));
            var dmm = c.Add(new Multimeter("XMM1", a, 0) { Mode = MultimeterMode.Resistance });
            Run(c, 1.6);

            Assert.Equal("kΩ", dmm.Reading.Unit);
            Assert.Equal("6 kΩ", dmm.Reading.RangeLabel);
            AssertWithinSpec(dmm, 4700.0);
            Assert.Null(dmm.Reading.Warning);
        }

        [Fact]
        public void ResistanceOnAPoweredCircuitIsFlagged()
        {
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc"), a = c.Node("a");
            c.Add(new VoltageSource("V1", vcc, 0, 5.0));
            c.Add(new Resistor("R1", vcc, a, 1000.0));
            c.Add(new Resistor("R2", a, 0, 1000.0));
            var dmm = c.Add(new Multimeter("XMM1", a, 0) { Mode = MultimeterMode.Resistance });
            Run(c, 1.6);

            Assert.True(dmm.ExternalVoltage);
            Assert.NotNull(dmm.Reading.Warning);
            Assert.True(dmm.Reading.Overload);
            Assert.True(Multimeter.CircuitIsEnergized(c));
        }

        [Fact]
        public void ContinuityBeepsOnlyBelowThreshold()
        {
            var c = new AnalogCircuit();
            int a = c.Node("a"), b = c.Node("b");
            c.Add(new Resistor("R1", a, 0, 10.0));
            c.Add(new Resistor("R2", b, 0, 1000.0));
            var low = c.Add(new Multimeter("XMM1", a, 0) { Mode = MultimeterMode.Continuity });
            var high = c.Add(new Multimeter("XMM2", b, 0) { Mode = MultimeterMode.Continuity });
            Run(c, 1.1);

            Assert.True(low.Reading.Beep);
            AssertWithinSpec(low, 10.0);
            Assert.False(high.Reading.Beep);
            Assert.True(high.Reading.Overload);
        }

        [Fact]
        public void DiodeTestShowsForwardDropAndOlReversed()
        {
            var c = new AnalogCircuit();
            int a = c.Node("a"), k = c.Node("k");
            c.Add(new Diode("D1", a, 0) { SaturationCurrent = 2.52e-9, Ideality = 1.752, SeriesResistance = 0.568 });
            c.Add(new Diode("D2", 0, k) { SaturationCurrent = 2.52e-9, Ideality = 1.752, SeriesResistance = 0.568 });
            var fwd = c.Add(new Multimeter("XMM1", a, 0) { Mode = MultimeterMode.Diode });
            var rev = c.Add(new Multimeter("XMM2", k, 0) { Mode = MultimeterMode.Diode });
            Run(c, 1.1);

            Assert.Equal("V", fwd.Reading.Unit);
            Assert.InRange(fwd.Reading.Value, 0.5, 0.8);
            Assert.True(rev.Reading.Overload);
        }

        [Fact]
        public void CapacitanceIsMeasuredByChargeTimeAndAutoranges()
        {
            var c = new AnalogCircuit();
            int a = c.Node("a"), b = c.Node("b");
            c.Add(new Capacitor("C1", a, 0, 1e-6));
            c.Add(new Capacitor("C2", b, 0, 47e-9));
            var big = c.Add(new Multimeter("XMM1", a, 0) { Mode = MultimeterMode.Capacitance });
            var small = c.Add(new Multimeter("XMM2", b, 0) { Mode = MultimeterMode.Capacitance });
            Run(c, 1.5, 2e-4);

            Assert.Equal("µF", big.Reading.Unit);
            AssertWithinSpec(big, 1e-6);
            Assert.Equal("nF", small.Reading.Unit);
            AssertWithinSpec(small, 47e-9);
        }

        [Fact]
        public void FrequencyAndDutyOfAPulseTrain()
        {
            var c = new AnalogCircuit();
            int a = c.Node("a");
            c.Add(new VoltageSource("V1", a, 0, PulseWaveform.FromDuty(0.0, 5.0, 1000.0, 0.3)));
            var dmm = c.Add(new Multimeter("XMM1", a, 0) { Mode = MultimeterMode.Frequency });
            Run(c, 1.6, 5e-5);

            Assert.Equal("kHz", dmm.Reading.Unit);
            AssertWithinSpec(dmm, 1000.0);
            Assert.InRange(dmm.Reading.Secondary, 29.0, 31.0);
        }

        [Fact]
        public void DisplayUpdatesAboutThreeTimesPerSecondAndHoldFreezes()
        {
            var c = new AnalogCircuit();
            int a = c.Node("a");
            var src = c.Add(new VoltageSource("V1", a, 0, 1.0));
            var dmm = c.Add(new Multimeter("XMM1", a, 0));
            var sim = Run(c, 2.0);
            Assert.InRange(dmm.Reading.Sequence, 5, 7);

            dmm.Hold = true;
            string held = dmm.Reading.Digits;
            ((DcWaveform)src.Waveform).Level = 2.0;
            sim.NotifyDiscontinuity();
            sim.Step(1.0);
            Assert.Equal(held, dmm.Reading.Digits);
            Assert.True(dmm.Reading.Hold);

            dmm.Hold = false;
            sim.Step(1.0);
            AssertWithinSpec(dmm, 2.0);
        }

        [Fact]
        public void MovingTheProbesMeasuresTheOtherNet()
        {
            var c = new AnalogCircuit();
            int a = c.Node("a"), b = c.Node("b");
            c.Add(new VoltageSource("V1", a, 0, 3.0));
            c.Add(new VoltageSource("V2", b, 0, 9.0));
            var dmm = c.Add(new Multimeter("~DMM", -1, -1));
            var sim = Run(c, 0.5);
            Assert.False(dmm.IsConnected);
            Assert.False(dmm.Reading.Valid);

            dmm.Connect(a, 0, MultimeterJack.VoltOhm);
            sim.Step(1.0);
            AssertWithinSpec(dmm, 3.0);
            dmm.Connect(b, a, MultimeterJack.VoltOhm);
            sim.Step(1.0);
            AssertWithinSpec(dmm, 6.0);
        }

        [Fact]
        public void RangeButtonLocksAndSteps()
        {
            var c = new AnalogCircuit();
            int a = c.Node("a");
            c.Add(new VoltageSource("V1", a, 0, 3.0));
            var dmm = c.Add(new Multimeter("XMM1", a, 0));
            var sim = Run(c, 1.0);
            Assert.True(dmm.AutoRange);
            dmm.StepRange();
            Assert.False(dmm.AutoRange);
            dmm.StepRange();
            sim.Step(1.0);
            Assert.Equal("60 V", dmm.Reading.RangeLabel);
            Assert.Equal(2, dmm.Reading.Digits.Split('.')[1].Length);
            AssertWithinSpec(dmm, 3.0);
            dmm.SetAutoRange();
            sim.Step(1.0);
            Assert.Equal("6 V", dmm.Reading.RangeLabel);
        }

        [Fact]
        public void FormatDigitsDropsNegativeZero()
        {
            var range = MultimeterSpec.Ranges(MultimeterMode.DcVoltage)[1];
            Assert.Equal("0.000", MultimeterSpec.FormatDigits(-0.0001, range));
            Assert.Equal("-1.234", MultimeterSpec.FormatDigits(-1.234, range));
            Assert.Equal(6000, (int)Math.Round(range.FullScale / range.Resolution));
            Assert.All(Enum.GetValues(typeof(MultimeterMode)).Cast<MultimeterMode>(),
                m => Assert.Equal(m, MultimeterSpec.ParseMode(MultimeterSpec.ModeKey(m))));
        }
    }
}
