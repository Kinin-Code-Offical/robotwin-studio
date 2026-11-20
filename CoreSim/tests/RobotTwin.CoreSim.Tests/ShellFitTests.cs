using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[shell] Hollow bodies: parts sit inside, walls hold them, the lid closes only over what fits.</summary>
    public class ShellFitTests
    {
        private readonly ITestOutputHelper _out;

        public ShellFitTests(ITestOutputHelper output) => _out = output;

        private static WorldBodyGeometry Geometry(WorldObject o) =>
            new WorldBodyGeometry { Min = new Vec3(-o.SizeX / 2, -o.SizeY / 2, 0), Max = new Vec3(o.SizeX / 2, o.SizeY / 2, o.SizeZ), MassKg = o.MassKg ?? 0.02 };

        /// <summary>A 120×80×40 mm box with 2 mm walls; its floor top is at z = 2 mm, its rim at z = 40 mm.</summary>
        private static WorldObject Shell(WorldDocument w, double roll = 0, double z = 0)
        {
            var s = w.AddBody("box", 0.12, 0.08, 0.04, new WorldPose { Z = z, Roll = roll }, "PLA");
            s.Name = "Gövde";
            s.IsStatic = true;
            s.Parameters["shell"] = "true";
            s.Parameters["wall"] = "0.002";
            return s;
        }

        private static WorldObject Lid(WorldDocument w, WorldObject shell, double z)
        {
            var lid = w.AddBody("box", 0.12, 0.08, 0.002, new WorldPose { Z = z }, "PLA");
            lid.Name = "Kapak";
            lid.Parameters["lid.of"] = shell.Id;
            lid.Parameters["screwed"] = "true";
            return lid;
        }

        [Fact]
        public void FittingContentsLetTheLidCloseAndScrewDown()
        {
            var w = new WorldDocument();
            var shell = Shell(w);
            var bat = w.AddBody("box", 0.05, 0.03, 0.02, new WorldPose { X = -0.02, Z = 0.002 }, "PLA");
            bat.Name = "Pil";
            var lid = Lid(w, shell, 0.04);
            var p = WorldPhysics.Build(w, Geometry, null);
            var issues = p.ShellFit(w);
            foreach (var i in issues) _out.WriteLine(i.Message);
            Assert.Empty(issues);
            Assert.True(p.IsScrewed(lid.Id), "a lid seated on the rim is screwed down");
            Assert.Equal(4, p.WallsOf(shell.Id).Count);
        }

        [Fact]
        public void TooTallPartHoldsTheLidUpAndAPartInTheWallIsReported()
        {
            var w = new WorldDocument();
            var shell = Shell(w);
            var tall = w.AddBody("box", 0.02, 0.02, 0.045, new WorldPose { X = 0.02, Z = 0.002 }, "PLA");
            tall.Name = "Konnektör";
            var wide = w.AddBody("box", 0.01, 0.01, 0.01, new WorldPose { X = 0.055, Y = -0.02, Z = 0.002 }, "PLA");
            wide.Name = "Servo";
            Lid(w, shell, 0.04);
            var p = WorldPhysics.Build(w, Geometry, null);
            var issues = p.ShellFit(w);
            foreach (var i in issues) _out.WriteLine($"{i.Kind}: {i.Message}");
            var h = Assert.Single(issues, i => i.Kind == "yükseklik");
            Assert.Equal(tall.Id, h.ObjectId);
            Assert.InRange(h.Amount, 0.0065, 0.0075);   // 2 + 45 − 40 = 7 mm
            var d = Assert.Single(issues, i => i.Kind == "duvar");
            Assert.Equal(wide.Id, d.ObjectId);
            Assert.InRange(d.Amount, 0.0015, 0.0025);   // reaches x = 60 mm, the inner wall face is at 58 mm
        }

        [Fact]
        public void ElectronicsBoxExampleFlagsTheUprightBattery()
        {
            var lib = RobotTwin.CoreSim.Design.PartLibrary.Default;
            var w = WorldExamples.ShellBox(lib);
            var geo = new RobotTwin.CoreSim.Automation.ApproximateGeometry(w, lib);
            var p = WorldPhysics.Build(w, geo.GeometryOf, null);
            var issues = p.ShellFit(w);
            foreach (var i in issues) _out.WriteLine($"{i.Kind}: {i.Message}");
            var h = Assert.Single(issues);
            Assert.Equal("yükseklik", h.Kind);
            Assert.Equal(w.Objects.First(o => o.Name.StartsWith("9 V")).Id, h.ObjectId);
            Assert.InRange(h.Amount, 0.0079, 0.0089);   // 2 mm floor + 46.4 mm battery − 40 mm rim = 8.4 mm
        }

        [Fact]
        public void WallsHoldWhatSlidesInsideATiltedShell()
        {
            var w = new WorldDocument();
            var shell = Shell(w, roll: 40, z: 0.1);   // tipped about x, steeper than the friction angle: things slide to one side wall
            var block = w.AddBody("box", 0.02, 0.02, 0.015, new WorldPose { Z = 0.116 }, "ÇELİK");
            block.MassKg = 0.05;
            var p = WorldPhysics.Build(w, Geometry, null);
            for (int i = 0; i < 4000; i++) p.Advance(1e-3, null);
            var pose = p.PoseOf(block.Id)!.Value;
            var floor = p.PoseOf(shell.Id)!.Value;
            var local = floor.Orientation.InverseRotate(pose.Position - floor.Position);
            // The block may spin on the way down: its reach toward the wall is its half width projected.
            var rel = floor.Orientation.Conjugate * pose.Orientation;
            double yaw = 2 * Math.Atan2(rel.Z, rel.W), reach = Math.Abs(local.Y) + 0.01 * (Math.Abs(Math.Cos(yaw)) + Math.Abs(Math.Sin(yaw)));
            _out.WriteLine($"block in shell frame: {local.X * 1000:F1}, {local.Y * 1000:F1}, {local.Z * 1000:F1} mm, turned {yaw * 180 / Math.PI:F0}°, reaches {reach * 1000:F1} mm (wall face 38 mm)");
            Assert.True(Math.Abs(local.Y) > 0.015, "it slid down toward the low wall");
            Assert.InRange(reach, 0.036, 0.0385);   // resting on the wall's inner face, not through it
            Assert.True(local.Z > 0, "it stays above the floor");
        }
    }
}
