using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[hull] A part whose rigid body is the convex hull of its own 3D model, not its bounding box.</summary>
    public class ModelHullTests
    {
        private readonly ITestOutputHelper _out;

        public ModelHullTests(ITestOutputHelper output) => _out = output;

        /// <summary>A 25 × 30 mm motor can lying on its side (axis along X), resting on z = 0, as a model's vertices.</summary>
        private static List<Vec3> Can(double r = 0.0125, double length = 0.03, int segments = 40)
        {
            var pts = new List<Vec3>();
            for (int i = 0; i < segments; i++)
            {
                double a = 2 * Math.PI * i / segments;
                foreach (double x in new[] { -length / 2, length / 2 })
                    pts.Add(new Vec3(x, r * Math.Cos(a), r + r * Math.Sin(a)));
            }

            return pts;
        }

        private static (double Travel, double Height) Roll(WorldBodyGeometry g)
        {
            var w = new WorldDocument { Title = "hull" };
            w.Environment.Walls = false;
            var o = w.AddBody("box", 0.03, 0.025, 0.025, new WorldPose { Z = 0.001 }, "ÇELİK");
            var p = WorldPhysics.Build(w, _ => g, null);
            var body = p.Scene.FindBody(o.Id)!;
            for (int i = 0; i < 200; i++) p.Advance(0.001, (AnalogSimulator?)null);   // settle
            var start = p.PoseOf(o.Id)!.Value.Position;
            body.AngularVelocity = new Vec3(-20, 0, 0);   // spun about its own axis: a round can rolls towards +Y
            for (int i = 0; i < 400; i++) p.Advance(0.001, (AnalogSimulator?)null);
            var end = p.PoseOf(o.Id)!.Value.Position;
            return (end.Y - start.Y, body.Position.Z);
        }

        [Fact]
        public void ARoundPartRollsOnItsHullAndRestsAtItsRadius()
        {
            var hull = new WorldBodyGeometry { Min = new Vec3(-0.015, -0.0125, 0), Max = new Vec3(0.015, 0.0125, 0.025), MassKg = 0.05, Shape = "hull", Hull = Can() };
            var box = new WorldBodyGeometry { Min = hull.Min, Max = hull.Max, MassKg = 0.05, Shape = "box" };
            var exact = new WorldBodyGeometry { Min = hull.Min, Max = hull.Max, MassKg = 0.05, Shape = "cylinder-x" };
            var round = Roll(hull);
            var square = Roll(box);
            var cylinder = Roll(exact);
            _out.WriteLine($"hull: rolled {round.Travel * 1000:F1} mm, centre at {round.Height * 1000:F2} mm; exact cylinder {cylinder.Travel * 1000:F1} mm; box {square.Travel * 1000:F1} mm");
            Assert.InRange(round.Height, 0.0120, 0.0131);   // the axis rests one radius up
            Assert.True(round.Travel > 0.6 * cylinder.Travel, "the model-shaped can rolls nearly as far as an exact cylinder");
            Assert.True(Math.Abs(square.Travel) < 0.2 * round.Travel, "a box of the same size barely moves");
        }

        [Fact]
        public void AFlatPointSetFallsBackToABox()
        {
            var flat = new List<Vec3> { new Vec3(0, 0, 0), new Vec3(0.01, 0, 0), new Vec3(0, 0.01, 0), new Vec3(0.01, 0.01, 0) };
            Assert.Null(WorldPhysics.HullBody("flat", flat, 0.01, "pla"));
            Assert.NotNull(WorldPhysics.HullBody("can", Can(), 0.05, "pla"));
        }

        [Fact]
        public void HullPointsSurviveTheBakedGeometryFile()
        {
            var w = new WorldDocument();
            var o = w.AddBody("box", 0.03, 0.025, 0.025, new WorldPose(), "PLA");
            var g = new WorldBodyGeometry { Min = new Vec3(-0.015, -0.0125, 0), Max = new Vec3(0.015, 0.0125, 0.025), Shape = "hull", Hull = Can(segments: 12) };
            var json = Automation.BakedGeometry.Export(w, new Fixed(g));
            var back = Automation.BakedGeometry.Parse(json, new Fixed(new WorldBodyGeometry())).GeometryOf(o);
            Assert.Equal("hull", back.Shape);
            Assert.Equal(24, back.Hull!.Count);
            Assert.True((back.Hull[5] - g.Hull[5]).Length < 1e-12);
        }

        private sealed class Fixed : Automation.IWorldGeometry
        {
            private readonly WorldBodyGeometry _g;

            public Fixed(WorldBodyGeometry g) => _g = g;

            public WorldBodyGeometry GeometryOf(WorldObject o) => _g;

            public double CableLength(WorldCable cable) => 0.1;
        }
    }
}
