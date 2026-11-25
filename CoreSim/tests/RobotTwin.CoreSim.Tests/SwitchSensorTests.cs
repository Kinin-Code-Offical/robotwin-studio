using System;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[cell] Photoelectric sensors and limit switches: light and contact from the world as circuit signals.</summary>
    public class SwitchSensorTests
    {
        private readonly ITestOutputHelper _out;

        public SwitchSensorTests(ITestOutputHelper output) => _out = output;

        /// <summary>A diffuse sensor sees a white box farther than a black one, as the returned light says.</summary>
        [Theory]
        [InlineData("paper", 0.15, true)]
        [InlineData("rubber", 0.15, false)]   // 8 % reflectance: seen only within ~9 cm of a 30 cm sensor
        [InlineData("rubber", 0.07, true)]
        [InlineData("paper", 0.35, false)]   // past the rated range
        public void ADiffuseSensorsReachFollowsTheTargetsReflectance(string material, double distance, bool seen)
        {
            var world = new WorldDocument();
            var pe = world.AddComponent(PartLibrary.Default, "photo-diffuse-300", new WorldPose { Z = 0.1 });
            pe.IsStatic = true;
            var g = CellGeometry.For(pe)!;
            var box = world.AddBody("box", 0.1, 0.1, 0.3, new WorldPose { X = g.Max.X + distance + 0.05, Z = 0.0 }, material);
            box.IsStatic = true;
            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            while (runner.Time < 0.05 - 1e-6) runner.Step(0.01);
            var ph = runner.Physics!.PhotoOf(pe.Id)!;
            _out.WriteLine($"{material} {distance * 1000:0} mm: ışık {ph.Distance * 1000:0.0} mm'de, cisim {(ph.Present ? "var" : "yok")}");
            Assert.Equal(seen, ph.Present);
            Assert.Equal(seen, ph.Output);   // "cisim varken": the output is on while it sees one
        }

        /// <summary>A retro-reflective sensor's beam is cut by anything in it, whatever its colour.</summary>
        [Fact]
        public void ABeamIsCutByABlackBoxToo()
        {
            var world = new WorldDocument();
            var pe = world.AddComponent(PartLibrary.Default, "photo-retro-3000", new WorldPose { Z = 0.1 });
            pe.IsStatic = true;
            var box = world.AddBody("box", 0.1, 0.1, 0.3, new WorldPose { X = 2.0 }, "rubber");
            box.IsStatic = true;
            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            while (runner.Time < 0.05 - 1e-6) runner.Step(0.01);
            var ph = runner.Physics!.PhotoOf(pe.Id)!;
            Assert.True(ph.Present);
            Assert.InRange(ph.Distance, 1.9, 1.96);
        }

        /// <summary>A box pressing the roller past the pre-travel closes C–NO and opens C–NC; one short of it does not.</summary>
        [Theory]
        [InlineData(0.005, true)]
        [InlineData(0.0015, false)]
        public void ALimitSwitchChangesOverPastItsPreTravel(double push, bool pressed)
        {
            var world = new WorldDocument();
            var ls = world.AddComponent(PartLibrary.Default, "limit-switch-roller", new WorldPose());
            ls.IsStatic = true;
            var g = CellGeometry.For(ls)!;
            double reach = 0.025;
            var box = world.AddBody("box", 0.1, 0.1, 0.2, new WorldPose { X = g.Max.X + reach - push + 0.05 }, "steel");
            box.IsStatic = true;
            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            while (runner.Time < 0.05 - 1e-6) runner.Step(0.01);
            var sw = runner.Physics!.LimitOf(ls.Id)!;
            _out.WriteLine($"{push * 1000:0.0} mm itildi: makara {sw.Travel * 1000:0.0} mm, {(sw.Pressed ? "basılı" : "serbest")}");
            Assert.Equal(pressed, sw.Pressed);
            Assert.InRange(sw.Travel, push - 0.0005, push + 0.0005);
        }
    }
}
