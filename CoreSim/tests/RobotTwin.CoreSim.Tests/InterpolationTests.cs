using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[interpolate] Unity's Rigidbody interpolation for what is drawn: between the last two steps, on along the velocity, or as stepped.</summary>
    public class InterpolationTests
    {
        private static (WorldPhysics P, WorldObject O) Lift(string? mode)
        {
            var w = new WorldDocument();
            var lift = new WorldObject { Id = w.NextId("O"), Name = "Asansör", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.1, SizeY = 0.1, SizeZ = 0.02, MassKg = 1, Pose = new WorldPose { Z = 0.2 } };
            lift.Parameters[WorldPhysics.KinematicKey] = "1";
            lift.Parameters[WorldPhysics.KinematicVelocityKey] = "0,0,0.1";   // exactly 0.1 mm per millisecond step
            if (mode != null) lift.Parameters[WorldPhysics.InterpolationKey] = mode;
            w.Objects.Add(lift);
            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default).GeometryOf, null);
            p.Advance(0.0105, null);   // ten whole 1 ms steps; half a step is left for the next frame
            return (p, lift);
        }

        [Fact]
        public void DrawingBlendsBetweenStepsOrRunsAhead()
        {
            var (stepped, o) = Lift("none");
            double z = stepped.PoseOf(o.Id)!.Value.Position.Z;
            Assert.InRange(z, 0.2 + 0.001 - 1e-9, 0.2 + 0.001 + 1e-9);   // where the tenth step left it
            Assert.Equal(z, stepped.RenderPoseOf(o.Id)!.Value.Position.Z, 12);

            var (blended, o2) = Lift(null);   // the default: interpolate
            Assert.InRange(blended.RenderPoseOf(o2.Id)!.Value.Position.Z, 0.2 + 0.00095 - 1e-9, 0.2 + 0.00095 + 1e-9);   // half-way between steps nine and ten
            Assert.InRange(blended.PoseOf(o2.Id)!.Value.Position.Z, 0.2 + 0.001 - 1e-9, 0.2 + 0.001 + 1e-9);   // what sensors read is the step's own

            var (ahead, o3) = Lift("extrapolate");
            Assert.InRange(ahead.RenderPoseOf(o3.Id)!.Value.Position.Z, 0.2 + 0.00105 - 1e-9, 0.2 + 0.00105 + 1e-9);   // the frame's own time
        }
    }
}
