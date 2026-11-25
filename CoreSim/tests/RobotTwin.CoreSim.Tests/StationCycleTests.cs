using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[cycle] A station's steps timed from its PLC's step variable: the cycles and the step that takes longest.</summary>
    public class StationCycleTests
    {
        private readonly ITestOutputHelper _out;

        public StationCycleTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void APusherStationsCyclesAndItsSlowestStepAreMeasured()
        {
            var w = new WorldDocument();
            var plc = w.AddComponent(PartLibrary.Default, "plc-st-8di8do", new WorldPose { X = -0.3, Y = 0.3 });
            var cyl = w.AddComponent(PartLibrary.Default, "cylinder-20x100", new WorldPose { Z = 0.05 });
            cyl.IsStatic = true;
            cyl.Parameters["speed"] = "20";   // a throttled exhaust: the strokes are the slow part
            var io = w.AddComponent(PartLibrary.Default, "modbus-io-8di8do", new WorldPose { X = 0.3, Y = 0.3 });
            io.Parameters["port"] = "0";
            void Wire(WorldObject a, string pa, WorldObject b, string pb) => w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = b.Id, Pin = pb });
            Wire(io, "DO1", plc, "I1");
            Wire(cyl, "EXT", plc, "I2");
            Wire(cyl, "RET", plc, "I3");
            Wire(plc, "Q2", cyl, "SOL");
            Wire(plc, "GND", cyl, "GND");
            Wire(io, "GND", plc, "GND");
            using var r = new WorldRunner(w, new RunnerOptions { Firmware = false });
            var machine = r.Physics!.PlcOf(plc.Id)!;
            var image = r.Physics.ModbusDevices.Single().Image;
            r.Step(0.3);
            lock (image.Gate) image.Coils[0] = true;   // parts keep coming: the station cycles
            r.Step(3.0);
            var steps = machine.Steps;
            _out.WriteLine($"{machine.Cycles.Count} çevrim, ort. {machine.Cycles.Skip(1).DefaultIfEmpty(0).Average():0.000} s; " +
                           string.Join(", ", steps.Select(kv => $"adım {kv.Key}: {kv.Value.Seconds:0.000} s / {kv.Value.Visits}")));
            Assert.True(machine.Cycles.Count >= 3, $"{machine.Cycles.Count} cycles");
            Assert.Equal(new[] { 0.0, 10, 20 }, steps.Keys.ToArray());
            var slowest = steps.OrderByDescending(kv => kv.Value.Seconds).First().Key;
            // The throttle meters out both ways: coming back empties the larger cap-end chamber through it, so step 20 is the slowest.
            Assert.Equal(20, slowest);
            double cycle = machine.Cycles.Skip(1).Average();
            double sum = steps.Values.Sum(v => v.Seconds) / machine.Cycles.Count;
            Assert.InRange(cycle, sum * 0.8, sum * 1.25);   // a cycle is its steps
        }
    }
}
