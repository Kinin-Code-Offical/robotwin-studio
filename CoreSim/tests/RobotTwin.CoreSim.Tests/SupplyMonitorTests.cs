using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[supply] The power-up and brown-out report, from the world's own circuit.</summary>
    public class SupplyMonitorTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public SupplyMonitorTests(ITestOutputHelper output) => _out = output;

        /// <summary>A battery straight onto each part's supply and ground, the world compiled and run for a while.</summary>
        private SupplyMonitor Run(string battery, double seconds, params (string Preset, string Supply)[] parts)
        {
            var w = new WorldDocument();
            var bat = w.AddComponent(Lib, battery, new WorldPose { X = -0.5 });
            double x = 0;
            foreach (var (preset, supply) in parts)
            {
                var o = w.AddComponent(Lib, preset, new WorldPose { X = x += 0.1, Z = 0.01 });
                w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = o.Id, Pin = supply });
                w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = o.Id, Pin = "GND" });
            }

            var wc = WorldCompiler.Compile(w, Lib);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var m = SupplyMonitor.Build(w, wc);
            while (sim.Time < seconds - 1e-9)
            {
                sim.Step(1e-3);
                m.Sample(sim.Time, sim);
            }

            m.Finish(sim.Time);
            foreach (var line in m.Report()) _out.WriteLine(line);
            return m;
        }

        [Fact]
        public void ASonarOnTwoAaCellsNeverComesUp()
        {
            var m = Run("bat-2aa", 0.05, ("hc-sr04", "VCC"), ("gy-521", "VCC"));
            var sonar = m.Watches.Single(x => x.Rating.Min == 4.5);
            var imu = m.Watches.Single(x => x.Rating.Min == 3.0);
            Assert.False(sonar.Ready);   // 3 V for a 4.5–5.5 V module
            Assert.True(imu.Ready);      // the GY-521's regulator takes 3 V
            Assert.Contains(m.Report(), l => l.Contains("hiç 4.50 V'a çıkmadı"));
        }

        [Fact]
        public void NineVoltsOnAnSg90IsReportedAsOverVoltage()
        {
            var m = Run("bat-9v", 0.02, ("servo-sg90", "V+"));
            var servo = m.Watches.Single();
            Assert.True(servo.MaxSeen > 6.0);
            Assert.Contains(m.Events, e => e.Kind == SupplyMonitor.EventKind.OverVoltage);
            Assert.Contains(m.Report(), l => l.Contains("en çok 6.00 V olmalı"));
        }

        [Fact]
        public void ABrownOutIsTimedFromItsDipToItsRecovery()
        {
            var m = Run("bat-4nimh", 0.005, ("hc-sr04", "VCC"));
            var w = m.Watches.Single();
            Assert.True(w.Ready);
            Assert.DoesNotContain(m.Events, e => e.Kind == SupplyMonitor.EventKind.OverVoltage);   // 5.6 V from fresh NiMH cells is within 6 V
            // The rail sags under a motor's starting current: 4.2 V for 12 ms, down to 3.9 V, then back.
            for (int k = 0; k <= 30; k++)
            {
                double t = 0.010 + k * 1e-3;
                double v = k < 2 ? 5.0 : k < 14 ? 4.2 - (k == 8 ? 0.3 : 0) : 5.0;
                m.Record(w, t, v);
            }

            m.Finish(0.04);
            foreach (var e in m.Events) _out.WriteLine($"{e.Time * 1000:0} ms {e.Kind}: {SupplyMonitor.Describe(e)}");
            Assert.Equal(1, w.BrownOuts);
            Assert.Equal(0.012, w.LongestBrownOut, 6);
            Assert.Equal(3.9, w.DeepestDip, 6);
            var recovered = m.Events.Single(e => e.Kind == SupplyMonitor.EventKind.Recovered);
            Assert.Contains("12 ms", SupplyMonitor.Describe(recovered));
        }
    }
}
