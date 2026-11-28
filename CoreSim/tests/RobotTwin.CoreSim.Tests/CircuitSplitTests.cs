using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [split] A circuit whose parts share nothing but the reference is solved part by part, each at its own step
    /// (AnalogOptions.SplitIndependentParts); what one device ties together stays one part.
    /// </summary>
    public class CircuitSplitTests
    {
        private const double Slice = 50e-6;

        /// <summary>Two RC filters on pulse sources of their own (one with a diode), sharing only ground.</summary>
        private static AnalogCircuit TwoFilters()
        {
            var c = new AnalogCircuit();
            int a = c.Node("A.IN"), ao = c.Node("A.OUT");
            c.Add(new VoltageSource("VA", a, 0, new PulseWaveform(0, 5, 1e-3, 0.5e-3)));
            c.Add(new Resistor("RA", a, ao, 1e3));
            c.Add(new Capacitor("CA", ao, 0, 1e-6));
            int b = c.Node("B.IN"), bm = c.Node("B.MID"), bo = c.Node("B.OUT");
            c.Add(new VoltageSource("VB", b, 0, new PulseWaveform(0, 3.3, 0.3e-3, 0.1e-3, delay: 0.07e-3)));
            c.Add(new Diode("DB", b, bm));
            c.Add(new Resistor("RB", bm, bo, 470));
            c.Add(new Capacitor("CB", bo, 0, 2.2e-6));
            return c;
        }

        private static AnalogSimulator Run(AnalogCircuit c, bool split, double seconds, List<double[]>? trace = null, params string[] nodes)
        {
            var sim = new AnalogSimulator(c, new AnalogOptions { MaxStep = 2e-4, SplitIndependentParts = split });
            sim.Initialize(InitialState.OperatingPoint);
            int steps = (int)Math.Round(seconds / Slice);
            for (int k = 0; k < steps; k++)
            {
                sim.Step(Slice);
                trace?.Add(nodes.Select(n => sim.NodeVoltage(n)).ToArray());
            }

            return sim;
        }

        [Fact]
        public void TwoFiltersSharingOnlyGroundAreSolvedApartAndAgreeWithTheWholeSolve()
        {
            var nodes = new[] { "A.OUT", "B.OUT", "B.MID" };
            var whole = new List<double[]>();
            var apart = new List<double[]>();
            var w = Run(TwoFilters(), false, 0.02, whole, nodes);
            var s = Run(TwoFilters(), true, 0.02, apart, nodes);
            Assert.Equal(1, w.PartCount);
            Assert.Equal(2, s.PartCount);
            Assert.NotEqual(s.PartOf(s.Circuit.Node("A.OUT")), s.PartOf(s.Circuit.Node("B.OUT")));
            Assert.Equal(s.PartOf(s.Circuit.Node("B.IN")), s.PartOf(s.Circuit.Node("B.OUT")));
            double worst = 0;
            for (int k = 0; k < whole.Count; k++)
                for (int j = 0; j < nodes.Length; j++) worst = Math.Max(worst, Math.Abs(whole[k][j] - apart[k][j]));
            Assert.True(worst < 0.02, $"split and whole differ by {worst * 1000:F1} mV");   // both within the error control's tolerance
            Assert.Equal(0, s.Rejoins);
        }

        [Fact]
        public void EachPartTakesOnlyTheStepsItsOwnEdgesNeed()
        {
            int Steps(bool split)
            {
                var sim = new AnalogSimulator(TwoFilters(), new AnalogOptions { MaxStep = 2e-4, SplitIndependentParts = split });
                sim.Initialize(InitialState.OperatingPoint);
                sim.Step(Slice);   // the first step finds the parts
                var counts = new Dictionary<int, int>();
                int a = sim.Circuit.Node("A.OUT");
                sim.Observe(new[] { a }, _ => counts[0] = counts.TryGetValue(0, out int n) ? n + 1 : 1);
                for (int k = 0; k < 200; k++) sim.Step(Slice);
                return counts.TryGetValue(0, out int c) ? c : 0;
            }

            int whole = Steps(false), apart = Steps(true);
            // the 1 kHz filter alone no longer steps at the 3.3 kHz filter's edges
            Assert.True(apart < whole * 0.8, $"filter A took {apart} steps apart, {whole} in the whole circuit");
        }

        [Fact]
        public void TheSameSplitRunRepeatsBitForBit()
        {
            var first = new List<double[]>();
            var second = new List<double[]>();
            Run(TwoFilters(), true, 0.01, first, "A.OUT", "B.OUT");
            Run(TwoFilters(), true, 0.01, second, "A.OUT", "B.OUT");
            for (int k = 0; k < first.Count; k++) Assert.Equal(first[k], second[k]);
        }

        /// <summary>A relay's coil on one supply, its contact switching a lamp on another: one part, as the coil moves the contact.</summary>
        [Fact]
        public void ARelaysCoilAndContactsAreOnePartAndSwitchOnTime()
        {
            AnalogCircuit Build()
            {
                var c = new AnalogCircuit();
                int coil = c.Node("K.COIL"), com = c.Node("K.COM"), no = c.Node("K.NO"), nc = c.Node("K.NC");
                c.Add(new VoltageSource("VCOIL", coil, 0, new PulseWaveform(0, 5, 1.0, 0.5, delay: 2e-3)));
                c.Add(new VoltageSource("VLAMP", com, 0, 12.0));
                c.Add(new Relay("K1", coil, 0, com, no, nc));
                c.Add(new Resistor("LAMP", no, 0, 24));
                c.Add(new Resistor("RNC", nc, 0, 1e3));
                int f = c.Node("F.IN");   // and a filter of its own beside them
                c.Add(new VoltageSource("VF", f, 0, new PulseWaveform(0, 5, 1e-3, 0.5e-3)));
                c.Add(new Resistor("RF", f, c.Node("F.OUT"), 1e3));
                c.Add(new Capacitor("CF", c.Node("F.OUT"), 0, 1e-6));
                return c;
            }

            double Closes(bool split, out AnalogSimulator sim)
            {
                sim = new AnalogSimulator(Build(), new AnalogOptions { MaxStep = 2e-4, SplitIndependentParts = split });
                sim.Initialize(InitialState.OperatingPoint);
                for (int k = 0; k < 400; k++)
                {
                    sim.Step(Slice);
                    if (sim.NodeVoltage("K.NO") > 6) return sim.Time;
                }

                return double.NaN;
            }

            double whole = Closes(false, out _), apart = Closes(true, out var s);
            Assert.Equal(2, s.PartCount);
            Assert.Equal(s.PartOf(s.Circuit.Node("K.COIL")), s.PartOf(s.Circuit.Node("K.NO")));
            Assert.NotEqual(s.PartOf(s.Circuit.Node("K.COIL")), s.PartOf(s.Circuit.Node("F.OUT")));
            Assert.Equal(whole, apart, 9);

            // initialized a second time (an operating point that would not settle, started again from zero): Setup adds no
            // children then, the relay is still one part with them
            var again = new AnalogSimulator(Build(), new AnalogOptions { MaxStep = 2e-4, SplitIndependentParts = true });
            again.Initialize(InitialState.OperatingPoint);
            again.Initialize(InitialState.Zero);
            again.Step(Slice);
            Assert.Equal(2, again.PartCount);
            Assert.Equal(again.PartOf(again.Circuit.Node("K.COIL")), again.PartOf(again.Circuit.Node("K.NO")));
        }

        [Fact]
        public void TheDevicesOfOneComponentAreOnePartEvenOnCircuitsOfTheirOwn()
        {
            var c = TwoFilters();
            c.Find("RA")!.Group = "M1";   // a motor on one circuit, its encoder on the other: one component
            c.Find("RB")!.Group = "M1";
            var sim = new AnalogSimulator(c, new AnalogOptions { SplitIndependentParts = true });
            sim.Initialize(InitialState.OperatingPoint);
            sim.Step(Slice);
            Assert.Equal(1, sim.PartCount);
        }

        /// <summary>Joins two nets only while it is on; says so with a discontinuity, as a switch or a pin that starts to drive does.</summary>
        private sealed class Link : AnalogDevice
        {
            private bool _on;

            public Link(string id, int a, int b) : base(id)
            {
                A = a;
                B = b;
            }

            public int A { get; }
            public int B { get; }
            public bool Announces { get; set; } = true;
            public double SwitchOnAt { get; set; } = double.PositiveInfinity;

            public bool On
            {
                get => _on;
                set
                {
                    if (_on == value) return;
                    _on = value;
                    if (Announces) PendingDiscontinuity = true;
                }
            }

            public override void Stamp(StampContext ctx)
            {
                if (ctx.Time >= SwitchOnAt) _on = true;   // a change it does not announce
                if (_on) ctx.AddConductance(A, B, 1.0);
            }
        }

        private static (AnalogCircuit C, Link L) Divider()
        {
            var c = new AnalogCircuit();
            int v = c.Node("V"), n1 = c.Node("N1"), n2 = c.Node("N2");
            c.Add(new VoltageSource("V1", v, 0, 5.0));
            c.Add(new Resistor("R1", v, n1, 1e3));
            c.Add(new Resistor("R2", n1, 0, 1e3));
            c.Add(new Resistor("R3", n2, 0, 1e3));
            var link = c.Add(new Link("L1", n1, n2));
            return (c, link);
        }

        [Fact]
        public void ADeviceThatStartsToJoinTwoPartsJoinsThemBeforeTheNextStep()
        {
            var (c, link) = Divider();
            var sim = new AnalogSimulator(c, new AnalogOptions { SplitIndependentParts = true });
            sim.Initialize(InitialState.OperatingPoint);
            for (int k = 0; k < 4; k++) sim.Step(Slice);
            Assert.True(sim.PartCount >= 2);
            Assert.Equal(2.5, sim.NodeVoltage("N1"), 6);
            Assert.Equal(0.0, sim.NodeVoltage("N2"), 6);

            string Parts() => $"{sim.PartCount}: V{sim.PartOf(c.Node("V"))} N1{sim.PartOf(c.Node("N1"))} N2{sim.PartOf(c.Node("N2"))} rejoins {sim.Rejoins}";
            string before = Parts();
            link.On = true;
            sim.Step(Slice);
            Assert.True(sim.PartCount == 1, before + " -> " + Parts());
            Assert.Equal(1, sim.Rejoins);
            // 1 kΩ from 5 V into 1 kΩ ∥ (1 Ω + 1 kΩ): N2 follows at once, not a slice later
            double r = 1.0 / (1.0 / 1e3 + 1.0 / 1001.0);
            double n1 = 5.0 * r / (1e3 + r);
            Assert.Equal(n1, sim.NodeVoltage("N1"), 4);
            Assert.Equal(n1 * 1000 / 1001, sim.NodeVoltage("N2"), 4);
        }

        [Fact]
        public void AJoinNoDiscontinuityAnnouncedIsMadeAtTheNextCall()
        {
            var (c, link) = Divider();
            link.Announces = false;
            link.SwitchOnAt = 3.2 * Slice;
            var sim = new AnalogSimulator(c, new AnalogOptions { SplitIndependentParts = true });
            sim.Initialize(InitialState.OperatingPoint);
            for (int k = 0; k < 4; k++) sim.Step(Slice);   // it reached out during the fourth
            sim.Step(Slice);
            Assert.Equal(1, sim.Rejoins);
            Assert.Equal(1, sim.PartCount);
            sim.Step(Slice);
            Assert.True(sim.NodeVoltage("N2") > 1.6, $"N2 {sim.NodeVoltage("N2"):F3} V");
        }

        [Fact]
        public void AnObserverHearsOnlyItsOwnPartsStepsInRisingTime()
        {
            var sim = new AnalogSimulator(TwoFilters(), new AnalogOptions { MaxStep = 2e-4, SplitIndependentParts = true });
            sim.Initialize(InitialState.OperatingPoint);
            sim.Step(Slice);
            var a = new List<double>();
            var b = new List<double>();
            int all = 0;
            sim.Observe(new[] { sim.Circuit.Node("A.OUT") }, t => a.Add(t));
            sim.Observe(new[] { sim.Circuit.Node("B.OUT") }, t => b.Add(t));
            sim.SubstepAccepted = _ => all++;
            for (int k = 0; k < 100; k++) sim.Step(Slice);
            Assert.Equal(all, a.Count + b.Count);
            Assert.True(a.Zip(a.Skip(1), (x, y) => y > x).All(x => x));
            Assert.True(b.Zip(b.Skip(1), (x, y) => y > x).All(x => x));
            Assert.NotEqual(a.Count, b.Count);
        }

        [Fact]
        public void PartsSolvedSideBySideOnTheCoresGiveTheSameResultBitForBit()
        {
            AnalogCircuit Many()
            {
                var c = new AnalogCircuit();
                for (int k = 0; k < 8; k++)
                {
                    int i = c.Node($"F{k}.IN"), m = c.Node($"F{k}.MID"), o = c.Node($"F{k}.OUT");
                    c.Add(new VoltageSource($"V{k}", i, 0, new PulseWaveform(0, 5, (1 + k) * 0.2e-3, (1 + k) * 0.07e-3, delay: k * 13e-6)));
                    c.Add(new Diode($"D{k}", i, m));
                    c.Add(new Resistor($"R{k}", m, o, 330 + 100 * k));
                    c.Add(new Capacitor($"C{k}", o, 0, 1e-6));
                }

                return c;
            }

            List<double> Run(bool sideBySide, out bool used)
            {
                var sim = new AnalogSimulator(Many(), new AnalogOptions { MaxStep = 2e-4, SplitIndependentParts = true }) { ForceSideBySide = sideBySide };
                sim.Initialize(InitialState.OperatingPoint);
                var trace = new List<double>();
                used = false;
                for (int k = 0; k < 400; k++)
                {
                    sim.Step(Slice);
                    used |= sim.PartsSideBySide;
                    for (int n = 1; n <= sim.Circuit.NodeCount; n++) trace.Add(sim.NodeVoltage(n));
                }

                Assert.Equal(8, sim.PartCount);
                return trace;
            }

            var apart = Run(true, out bool parallel);
            var inTurn = Run(false, out bool serial);
            Assert.True(parallel || RobotTwin.CoreSim.Mechanics.WorkTeam.Shared.Workers == 0);
            Assert.False(serial);
            Assert.Equal(inTurn, apart);
        }

        /// <summary>A device of another assembly (a plugin's): notes the thread it is stamped on.</summary>
        private sealed class PluginLoad : AnalogDevice
        {
            public PluginLoad(string id, int a) : base(id) => A = a;

            public int A { get; }
            public HashSet<int> Threads { get; } = new HashSet<int>();

            public override void Stamp(StampContext ctx)
            {
                lock (Threads) Threads.Add(System.Threading.Thread.CurrentThread.ManagedThreadId);
                ctx.AddConductance(A, 0, 1e-3);
            }
        }

        [Fact]
        public void APluginsDeviceIsSolvedOnTheCallersThreadWhileTheOthersRunBesideIt()
        {
            var c = new AnalogCircuit();
            var plugins = new List<PluginLoad>();
            for (int k = 0; k < 6; k++)
            {
                int i = c.Node($"F{k}.IN"), o = c.Node($"F{k}.OUT");
                c.Add(new VoltageSource($"V{k}", i, 0, new PulseWaveform(0, 5, 0.2e-3, 0.1e-3)));
                c.Add(new Resistor($"R{k}", i, o, 1e3));
                c.Add(new Capacitor($"C{k}", o, 0, 1e-6));
                if (k % 3 == 0) plugins.Add(c.Add(new PluginLoad($"P{k}", o)));
            }

            var sim = new AnalogSimulator(c, new AnalogOptions { SplitIndependentParts = true }) { ForceSideBySide = true };
            sim.Initialize(InitialState.OperatingPoint);
            sim.Step(Slice);
            foreach (var p in plugins) p.Threads.Clear();
            bool sideBySide = false;
            for (int k = 0; k < 50; k++)
            {
                sim.Step(Slice);
                sideBySide |= sim.PartsSideBySide;
            }

            Assert.Equal(6, sim.PartCount);
            Assert.True(sideBySide || RobotTwin.CoreSim.Mechanics.WorkTeam.Shared.Workers == 0);
            int me = System.Threading.Thread.CurrentThread.ManagedThreadId;
            foreach (var p in plugins) Assert.Equal(new[] { me }, p.Threads.ToArray());
        }

        [Fact]
        public void EventsOfDifferentPartsComeInTimeOrder()
        {
            var c = new AnalogCircuit();
            foreach (var (name, delay) in new[] { ("K1", 1e-3), ("K2", 0.5e-3) })
            {
                int coil = c.Node(name + ".COIL"), com = c.Node(name + ".COM"), no = c.Node(name + ".NO"), nc = c.Node(name + ".NC");
                c.Add(new VoltageSource("V" + name, coil, 0, new PulseWaveform(0, 5, 1.0, 0.5, delay: delay)));
                c.Add(new VoltageSource("S" + name, com, 0, 12.0));
                c.Add(new Relay(name, coil, 0, com, no, nc));
                c.Add(new Resistor("L" + name, no, 0, 24));
                c.Add(new Resistor("N" + name, nc, 0, 1e3));
            }

            var sim = new AnalogSimulator(c, new AnalogOptions { MaxStep = 2e-4, SplitIndependentParts = true });
            sim.Initialize(InitialState.OperatingPoint);
            sim.Step(Slice);
            Assert.Equal(2, sim.PartCount);
            sim.DrainEvents();
            sim.Step(10e-3);
            var pulls = sim.DrainEvents().Where(e => e.Kind == AnalogEventKind.StateChange).ToList();
            Assert.Equal(new[] { "K2", "K1" }, pulls.Select(e => e.DeviceId).ToArray());
            Assert.True(pulls[0].Time < pulls[1].Time);
        }
    }
}
