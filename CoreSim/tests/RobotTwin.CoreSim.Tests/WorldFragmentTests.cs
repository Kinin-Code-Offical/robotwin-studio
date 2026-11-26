using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[clipboard] Copy, paste, duplicate and cut of a world's objects with what is mounted on them.</summary>
    public class WorldFragmentTests
    {
        private static WorldObject Box(WorldDocument w, string name, double x)
        {
            var o = new WorldObject { Id = w.NextId("O"), Name = name, Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.02, SizeY = 0.02, SizeZ = 0.02, Pose = new WorldPose { X = x, Z = 0.01 } };
            w.Objects.Add(o);
            return o;
        }

        private static (WorldDocument W, WorldObject Base, WorldObject Arm, WorldObject Tip, WorldObject Other) Arm()
        {
            var w = new WorldDocument();
            var b = Box(w, "Taban", 0);
            var arm = Box(w, "Kol", 0.03);
            var tip = Box(w, "Uç", 0.06);
            var other = Box(w, "Duvar", 0.5);
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = arm.Id, ObjectB = b.Id, Kind = WorldJointKind.Hinge, AnchorX = 0.015, AnchorZ = 0.01 });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = tip.Id, ObjectB = arm.Id, Kind = WorldJointKind.Fixed });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = other.Id, ObjectB = b.Id, Kind = WorldJointKind.Fixed });   // the wall hangs on the base too
            w.Cables.Add(new WorldCable { Id = w.NextId("C"), A = new CableEnd { ObjectId = b.Id, Pin = "1" }, B = new CableEnd { ObjectId = tip.Id, Pin = "2" } });
            w.Cables.Add(new WorldCable { Id = w.NextId("C"), A = new CableEnd { ObjectId = arm.Id, Pin = "1" }, B = new CableEnd { ObjectId = other.Id, Pin = "2" } });
            return (w, b, arm, tip, other);
        }

        [Fact]
        public void ACopyOfAnArmTakesWhatIsMountedOnItAndItsInnerJointsAndCables()
        {
            var (w, _, arm, tip, _) = Arm();
            var f = WorldFragment.Take(w, new[] { arm.Id });
            Assert.Equal(new[] { "Kol", "Uç" }, f.Objects.Select(o => o.Name));
            Assert.Single(f.Joints);   // the tip on the arm; the arm's hinge to the base stays behind
            Assert.Empty(f.Cables);    // both cables leave the fragment

            var roots = f.PasteInto(w, new Vec3(0, 0.1, 0));
            var copyArm = Assert.Single(roots);
            Assert.Equal("Kol 2", copyArm.Name);
            Assert.Equal(0.1, copyArm.Pose.Y, 9);
            var copyTip = w.Objects.Single(o => o.Name == "Uç 2");
            var j = w.Joints.Single(x => x.ObjectA == copyTip.Id);
            Assert.Equal(copyArm.Id, j.ObjectB);
            Assert.Equal(6 + 0, w.Objects.Count);
            Assert.Contains(copyTip, w.MountedOn(copyArm.Id));
        }

        [Fact]
        public void DuplicatingTheBaseCopiesItsCablesAndTheJsonRoundTrips()
        {
            var (w, b, _, _, _) = Arm();
            var f = WorldFragment.FromJson(WorldFragment.Take(w, new[] { b.Id }).ToJson())!;
            Assert.Equal(4, f.Objects.Count);   // base, arm, tip, and the wall mounted on the base
            Assert.Equal(2, f.Cables.Count);
            int joints = w.Joints.Count, cables = w.Cables.Count;
            f.PasteInto(w, new Vec3(0.2, 0, 0));
            Assert.Equal(joints + 3, w.Joints.Count);
            Assert.Equal(cables + 2, w.Cables.Count);
            var copyHinge = w.Joints.Last(j => j.Kind == WorldJointKind.Hinge);
            Assert.Equal(0.215, copyHinge.AnchorX, 9);   // the anchor moved with the copy
            Assert.All(w.Cables.Skip(cables), c => Assert.DoesNotContain(c.A.ObjectId, new[] { "O1", "O2", "O3", "O4" }));
        }

        [Fact]
        public void CuttingRemovesTheObjectWithWhatIsMountedOnIt()
        {
            var (w, _, arm, _, other) = Arm();
            WorldFragment.RemoveFrom(w, new[] { arm.Id });
            Assert.Equal(new[] { "Taban", "Duvar" }, w.Objects.Select(o => o.Name));
            Assert.DoesNotContain(w.Cables, c => c.A.ObjectId == arm.Id || c.B.ObjectId == arm.Id);
            Assert.Contains(w.Joints, j => j.ObjectA == other.Id);
        }
    }
}
