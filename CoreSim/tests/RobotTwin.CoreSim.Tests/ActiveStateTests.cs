using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[active] Unity's active switch: an inactive object and what rides on it are not in the run.</summary>
    public class ActiveStateTests
    {
        private static WorldObject Box(WorldDocument w, string name, double x, double z = 0.01)
        {
            var o = new WorldObject { Id = w.NextId("O"), Name = name, Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.02, SizeY = 0.02, SizeZ = 0.02, MassKg = 0.05, Pose = new WorldPose { X = x, Z = z } };
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
            w.Cables.Add(new WorldCable { Id = w.NextId("C"), A = new CableEnd { ObjectId = arm.Id, Pin = "1" }, B = new CableEnd { ObjectId = other.Id, Pin = "2" } });
            w.Explode = new() { new ExplodeStep { Objects = { arm.Id, tip.Id } } };
            return (w, b, arm, tip, other);
        }

        [Fact]
        public void AWorldWithNothingSwitchedOffRunsAsItIs()
        {
            var (w, _, _, _, _) = Arm();
            Assert.Same(w, w.WithoutInactive());
        }

        [Fact]
        public void ASwitchedOffArmLeavesTheRunWithWhatRidesOnItButNotTheEditor()
        {
            var (w, b, arm, tip, other) = Arm();
            WorldBuilding.SetActive(arm, false);
            Assert.False(WorldBuilding.IsActive(arm));
            Assert.True(WorldBuilding.IsActive(tip));
            Assert.False(WorldBuilding.ActiveInHierarchy(w, tip));   // it rides on the switched-off arm
            Assert.True(WorldBuilding.ActiveInHierarchy(w, b));

            var run = w.WithoutInactive();
            Assert.Equal(new[] { "Taban", "Duvar" }, run.Objects.Select(o => o.Name));
            Assert.Empty(run.Joints);
            Assert.Empty(run.Cables);
            Assert.Same(b, run.Find(b.Id));   // the same instances: what the run writes shows in the editor
            Assert.Same(other, run.Find(other.Id));

            // the editor's world is untouched
            Assert.Equal(4, w.Objects.Count);
            Assert.Equal(2, w.Joints.Count);
            Assert.Single(w.Cables);
            Assert.Equal(2, w.Explode![0].Objects.Count);

            WorldBuilding.SetActive(arm, true);
            Assert.False(arm.Parameters.ContainsKey(WorldBuilding.InactiveKey));   // nothing left behind in the file
        }

        [Fact]
        public void ASwitchedOffPartHasNoBodyInThePhysics()
        {
            var (w, _, arm, tip, other) = Arm();
            WorldBuilding.SetActive(arm, false);
            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default).GeometryOf, null);
            Assert.DoesNotContain(p.Scene.Bodies, b => b.Tag == arm || b.Tag == tip);
            Assert.Contains(p.Scene.Bodies, b => b.Tag == other);
        }
    }
}
