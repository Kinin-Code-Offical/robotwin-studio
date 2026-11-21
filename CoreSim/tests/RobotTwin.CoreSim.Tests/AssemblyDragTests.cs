using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[drag] Joined parts shield each other's faces from the air.</summary>
    public class AssemblyDragTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public AssemblyDragTests(ITestOutputHelper output) => _out = output;

        private static WorldPhysics Build(WorldDocument w) => WorldPhysics.Build(w, new ApproximateGeometry(w, Lib).GeometryOf, null);

        [Fact]
        public void ALoneBodyKeepsItsTextbookDragInEveryDirection()
        {
            var w = new WorldDocument();
            var ball = w.AddBody("sphere", 0.1, 0.1, 0.1, new WorldPose { Z = 1 }, "PLA");
            var p = Build(w);
            var b = p.Scene.FindBody(ball.Id)!;
            Assert.True(b.DirectionalDrag);
            double area = Math.PI * 0.05 * 0.05;
            foreach (var dir in new[] { Vec3.UnitX, -Vec3.UnitZ, new Vec3(1, 1, 1).Normalized() })
            {
                double cd = b.DragAreaAlong(dir) / area;
                _out.WriteLine($"sphere along {dir}: Cd {cd:F3}");
                Assert.InRange(cd, 0.469, 0.471);   // the measured 0.47 (its skin friction included)
            }
        }

        [Fact]
        public void ANoseConeShieldsTheTubesFrontAndTheTubeShieldsTheNosesBase()
        {
            var w = new WorldDocument();
            const double d = 0.1, len = 0.3;
            var tube = w.AddBody("cylinder", d, d, len, new WorldPose { Z = 1 }, "PLA");
            var nose = w.AddBody("sphere", d, d, d, new WorldPose { Z = 1 + len - d / 2 }, "PLA");
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = nose.Id, ObjectB = tube.Id });
            var p = Build(w);
            var t = p.Scene.FindBody(tube.Id)!;
            var n = p.Scene.FindBody(nose.Id)!;
            var (_, tubePlus, tubeMinus) = AssemblyDrag.Faces(t, new[] { n });
            var (_, nosePlus, noseMinus) = AssemblyDrag.Faces(n, new[] { t });
            _out.WriteLine($"tube +z face covered {tubePlus.Z:P0}, −z {tubeMinus.Z:P0}; nose +z {nosePlus.Z:P0}, −z {noseMinus.Z:P0}");
            Assert.True(tubePlus.Z > 0.95, "the nose covers the tube's top");
            Assert.True(tubeMinus.Z < 0.05, "the tube's base is open");
            Assert.True(noseMinus.Z > 0.6, "the tube covers the nose's lower half");
            Assert.True(nosePlus.Z < 0.05);

            // Flying nose-first (+z): the assembly's Cd·A falls from "two blunt bodies" to nose + base + friction.
            double area = Math.PI * d * d / 4;
            double alone = (0.9 + 0.47) * area;   // what the two parts drew when each flew alone
            double together = t.DragAreaAlong(Vec3.UnitZ) + n.DragAreaAlong(Vec3.UnitZ);
            _out.WriteLine($"axial Cd: parts alone {alone / area:F2}, as an assembly {together / area:F2}");
            Assert.InRange(together / area, 0.45, 0.75);   // a blunt-based tube with a round nose: ~0.5–0.7
            // Sideways the tube still meets the air with its whole length.
            Assert.InRange(t.DragAreaAlong(Vec3.UnitX) / (d * len), 0.95, 1.1);
        }

        [Fact]
        public void TheWaterRocketNowClimbsLikeARealOne()
        {
            var w = WorldExamples.WaterRocket(Lib);
            var bottle = w.Objects.First(o => o.Name.Contains("PET"));
            var p = Build(w);
            double z0 = p.PoseOf(bottle.Id)!.Value.Position.Z, apogee = 0, vmax = 0;
            for (int i = 0; i < 8000; i++)
            {
                p.Advance(1e-3, (AnalogSimulator?)null);
                var body = p.Scene.FindBody(bottle.Id)!;
                vmax = Math.Max(vmax, body.Velocity.Z);
                apogee = Math.Max(apogee, p.PoseOf(bottle.Id)!.Value.Position.Z - z0);
                if (p.Scene.Time > 1.5 && body.Velocity.Z < 0) break;
            }

            _out.WriteLine($"max speed {vmax:F1} m/s, apogee {apogee:F1} m");
            // 2 L, 0.6 L water, 4 bar, ~150 g: published flights and simulators give ~30–50 m.
            Assert.InRange(apogee, 28, 60);
        }
    }
}
