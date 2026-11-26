using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Industrial;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[fault] The cell's equipment going wrong at a set time, as it does in a plant.</summary>
    public class MachineFaultTests
    {
        private readonly ITestOutputHelper _out;

        public MachineFaultTests(ITestOutputHelper output) => _out = output;

        private static MachineFaults.Fault F(string id) => MachineFaults.All.Single(f => f.Id == id);

        [Fact]
        public void ADirtyLensLosesAWhiteBoxItSawAMomentAgo()
        {
            var w = new WorldDocument();
            var pe = w.AddComponent(PartLibrary.Default, "photo-diffuse-300", new WorldPose { Z = 0.1 });
            pe.IsStatic = true;
            MachineFaults.Set(pe, F("lens_dirty"), at: 0.2);   // 40 % of the range left from 0.2 s on
            var g = CellGeometry.For(pe)!;
            var box = w.AddBody("box", 0.1, 0.1, 0.3, new WorldPose { X = g.Max.X + 0.2 + 0.05 }, "paper");
            box.IsStatic = true;
            using var r = new WorldRunner(w, new RunnerOptions { Firmware = false });
            var ph = r.Physics!.PhotoOf(pe.Id)!;
            r.Step(0.1);
            Assert.True(ph.Present);   // 200 mm, white: seen
            r.Step(0.2);
            Assert.False(ph.Present);   // 0.3 m × 40 % × √(0.85/0.9) ≈ 117 mm now
        }

        [Fact]
        public void AStuckValveIgnoresItsCoilAndALowSupplyPushesLess()
        {
            (WorldRunner Runner, MachineCylinder Cyl, ModbusImage Image) Rig(string fault, double at, double? wall)
            {
                var w = new WorldDocument();
                var cy = w.AddComponent(PartLibrary.Default, "cylinder-20x100", new WorldPose { Z = 0.05 });
                cy.IsStatic = true;
                MachineFaults.Set(cy, F(fault), at);
                var io = w.AddComponent(PartLibrary.Default, "modbus-io-8di8do", new WorldPose { X = -0.4, Y = 0.3 });
                io.Parameters["port"] = "0";
                w.Connect(new CableEnd { ObjectId = io.Id, Pin = "DO1" }, new CableEnd { ObjectId = cy.Id, Pin = "SOL" });
                w.Connect(new CableEnd { ObjectId = io.Id, Pin = "GND" }, new CableEnd { ObjectId = cy.Id, Pin = "GND" });
                if (wall is { } x)
                {
                    var g = CellGeometry.For(cy)!;
                    var b = w.AddBody("box", 0.1, 0.2, 0.2, new WorldPose { X = g.Max.X + 0.02 + x + 0.05 }, "steel");
                    b.IsStatic = true;
                }

                var r = new WorldRunner(w, new RunnerOptions { Firmware = false });
                return (r, r.Physics!.CylinderOf(cy.Id)!, r.Physics.ModbusDevices.Single().Image);
            }

            var (r1, c1, i1) = Rig("valve_stuck", 0, null);
            using (r1)
            {
                r1.Step(0.1);
                lock (i1.Gate) i1.Coils[0] = true;
                r1.Step(0.5);
                Assert.True(c1.Retracted);   // the spool did not move
                Assert.False(c1.Energised);
            }

            var (r2, c2, i2) = Rig("low_supply", 0, 0.05);
            using (r2)
            {
                lock (i2.Gate) i2.Coils[0] = true;
                r2.Step(1.0);
                double ideal = 2.5e5 * Math.PI / 4 * 0.02 * 0.02;   // 78.5 N at 2.5 bar
                _out.WriteLine($"düşük beslemede duvara {c2.Force:0.0} N (ideal {ideal:0.0} N)");
                Assert.InRange(c2.Force, ideal - 2, ideal + 1);
            }

            var (r3, c3, i3) = Rig("leak", 0, null);
            using (r3)
            {
                lock (i3.Gate) i3.Coils[0] = true;
                r3.Step(1.0);
                double gauge = (c3.PressureA - 101325) / 1e5;
                _out.WriteLine($"kaçakta ileride ön hazne {gauge:0.00} bar");
                Assert.True(c3.Extended);
                Assert.InRange(gauge, 2.0, 5.8);   // the leak holds the cap end below the supply
            }
        }

        [Fact]
        public void AWeldedContactStaysClosedAndABrokenLeverIsNeverPressed()
        {
            foreach (var (fault, push, closed) in new[] { ("contact_welded", 0.0, true), ("lever_broken", 0.005, false) })
            {
                var w = new WorldDocument();
                var ls = w.AddComponent(PartLibrary.Default, "limit-switch-roller", new WorldPose());
                ls.IsStatic = true;
                MachineFaults.Set(ls, F(fault));
                if (push > 0)
                {
                    var g = CellGeometry.For(ls)!;
                    var box = w.AddBody("box", 0.1, 0.1, 0.2, new WorldPose { X = g.Max.X + 0.025 - push + 0.05 }, "steel");
                    box.IsStatic = true;
                }

                using var r = new WorldRunner(w, new RunnerOptions { Firmware = false });
                r.Step(0.05);
                var sw = r.Physics!.LimitOf(ls.Id)!;
                Assert.False(sw.Pressed);
                Assert.Equal(closed, sw.No!.Closed);   // C–NO
                Assert.Equal(!closed, sw.Nc!.Closed);   // C–NC
            }
        }

        [Fact]
        public async Task AModuleOffTheNetworkAnswersNothing()
        {
            var w = new WorldDocument();
            var io = w.AddComponent(PartLibrary.Default, "modbus-io-8di8do", new WorldPose());
            io.Parameters["port"] = "0";
            MachineFaults.Set(io, F("network_down"), at: 0.1);
            using var r = new WorldRunner(w, new RunnerOptions { Firmware = false });
            using var host = new ModbusHost(r.Physics!.ModbusDevices);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, host.Servers[0].Port);
            var stream = client.GetStream();
            async Task<bool> Answered(int waitMs)
            {
                var frame = new byte[] { 0, 1, 0, 0, 0, 6, 1, 1, 0, 0, 0, 8 };
                await stream.WriteAsync(frame, 0, frame.Length);
                var buffer = new byte[64];
                var read = stream.ReadAsync(buffer, 0, buffer.Length);
                return await Task.WhenAny(read, Task.Delay(waitMs)) == read && read.Result > 0;
            }

            r.Step(0.05);
            Assert.True(await Answered(3000));   // a loaded machine may take a while to answer, but it does
            r.Step(0.1);
            Assert.False(await Answered(500));   // off the network: the request times out
        }
    }
}
