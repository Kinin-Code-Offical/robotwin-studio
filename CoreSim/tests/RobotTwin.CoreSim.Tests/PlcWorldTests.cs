using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[plc] A PLC in the world, wired as on a panel: its example program runs a pusher station on a real cylinder.</summary>
    public class PlcWorldTests
    {
        private readonly ITestOutputHelper _out;

        public PlcWorldTests(ITestOutputHelper output) => _out = output;

        private static void Wire(WorldDocument w, WorldObject a, string pa, WorldObject b, string pb) =>
            w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = b.Id, Pin = pb });

        [Fact]
        public void TheExampleProgramPushesAPartOutAndCountsIt()
        {
            var w = new WorldDocument();
            var plc = w.AddComponent(PartLibrary.Default, "plc-st-8di8do", new WorldPose { X = -0.3, Y = 0.3 });
            var cyl = w.AddComponent(PartLibrary.Default, "cylinder-20x100", new WorldPose { Z = 0.05 });
            cyl.IsStatic = true;
            var io = w.AddComponent(PartLibrary.Default, "modbus-io-8di8do", new WorldPose { X = 0.3, Y = 0.3 });
            io.Parameters["port"] = "0";
            Wire(w, io, "DO1", plc, "I1");      // "a part is there", from a Modbus output standing in for a sensor
            Wire(w, cyl, "EXT", plc, "I2");
            Wire(w, cyl, "RET", plc, "I3");
            Wire(w, plc, "Q2", cyl, "SOL");
            Wire(w, plc, "GND", cyl, "GND");
            Wire(w, io, "GND", plc, "GND");
            using var r = new WorldRunner(w, new RunnerOptions { Firmware = false });
            var machine = r.Physics!.PlcOf(plc.Id)!;
            var cylinder = r.Physics.CylinderOf(cyl.Id)!;
            var image = r.Physics.ModbusDevices.Single().Image;
            Assert.True(machine.Running, string.Join("; ", machine.Errors));
            r.Step(0.3);
            Assert.True(cylinder.Retracted);

            lock (image.Gate) image.Coils[0] = true;   // a part arrives
            bool wentOut = false;
            for (int i = 0; i < 1000 && machine.Program!.Memory[0] == 0; i++)
            {
                r.Step(0.002);
                wentOut |= cylinder.Extended;
                if (wentOut) lock (image.Gate) image.Coils[0] = false;   // pushed away: the sensor sees nothing
            }

            _out.WriteLine($"{r.Time:0.000} s: itildi {wentOut}, sayı {machine.Program!.Memory[0]}, {machine.Program.Scans} tarama, adım {machine.Program.Read("ADIM")}");
            Assert.True(wentOut);
            Assert.Equal(1, machine.Program.Memory[0]);
            r.Step(0.3);
            Assert.True(cylinder.Retracted);
            Assert.Equal(0, machine.Program.Read("ADIM"));
            Assert.Equal(1, machine.Program.Memory[0]);   // no part, no second push
        }

        [Fact]
        public void AFaultStopsThePlcWithItsOutputsOffUntilANewProgramIsLoaded()
        {
            var w = new WorldDocument();
            var plc = w.AddComponent(PartLibrary.Default, "plc-st-8di8do", new WorldPose());
            plc.Parameters["program"] = "VAR q AT %QX0.0 : BOOL; n, d : INT; END_VAR\nq := TRUE;\nn := n + 1;\nIF n > 5 THEN n := n / d; END_IF";
            using var r = new WorldRunner(w, new RunnerOptions { Firmware = false });
            var machine = r.Physics!.PlcOf(plc.Id)!;
            r.Step(0.03);
            Assert.True(machine.Running);
            Assert.True(machine.Program!.Outputs[0]);
            r.Step(0.05);   // the sixth scan divides by zero
            Assert.False(machine.Running);
            Assert.Contains("sıfıra bölme", machine.Program.Fault);

            var errors = r.Physics.LoadPlcProgram(plc.Id, "VAR q AT %QX0.0 : BOOL; END_VAR\nq := FALSE;");
            Assert.Empty(errors);
            r.Step(0.02);
            Assert.True(machine.Running);
            Assert.NotEmpty(r.Physics.LoadPlcProgram(plc.Id, "q := ;"));   // a bad download keeps the running program
            Assert.True(machine.Running);
        }
    }
}
