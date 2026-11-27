using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[default-parent] Unity's default parent: new objects are mounted on it as they are made.</summary>
    public class DefaultParentTests
    {
        [Fact]
        public void NewObjectsAreMountedOnTheDefaultParentAPastedAssemblyByItsBase()
        {
            var w = new WorldDocument();
            var chassis = w.AddBody("box", 0.2, 0.1, 0.02, new WorldPose { Z = 0.05 });
            w.DefaultParent = chassis.Id;
            var before = new HashSet<string>(w.Objects.Select(o => o.Id), StringComparer.OrdinalIgnoreCase);
            var sensor = w.AddBody("box", 0.02, 0.02, 0.02, new WorldPose { X = 0.08, Z = 0.07 });
            var bracket = w.AddBody("box", 0.03, 0.03, 0.01, new WorldPose { X = -0.05, Z = 0.065 });
            var servo = w.AddBody("box", 0.02, 0.04, 0.02, new WorldPose { X = -0.05, Z = 0.08 });
            w.Joints.Add(new WorldJoint { Id = "J9", Kind = WorldJointKind.Fixed, ObjectA = servo.Id, ObjectB = bracket.Id });   // came mounted together
            Assert.Equal(2, WorldBuilding.AdoptByDefaultParent(w, before));
            Assert.Contains(w.Joints, j => j.ObjectA == sensor.Id && j.ObjectB == chassis.Id);
            Assert.Contains(w.Joints, j => j.ObjectA == bracket.Id && j.ObjectB == chassis.Id);
            Assert.Single(w.Joints, j => j.ObjectA == servo.Id);   // still on its bracket
            Assert.Equal(0.08, w.Joints.First(j => j.ObjectA == sensor.Id).AnchorX, 9);   // mounted where it stands
            Assert.Contains(w.MountedOn(chassis.Id), o => o.Id == servo.Id);
        }

        [Fact]
        public void WithoutOneOrOnceItIsGoneNothingIsMountedAndItIsSaved()
        {
            var w = new WorldDocument();
            var before = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            w.AddBody("box", 0.02, 0.02, 0.02, new WorldPose());
            Assert.Equal(0, WorldBuilding.AdoptByDefaultParent(w, before));
            w.DefaultParent = "gone";
            Assert.Equal(0, WorldBuilding.AdoptByDefaultParent(w, before));
            Assert.Empty(w.Joints);
            Assert.Equal("gone", WorldDocument.FromJson(w.ToJson()).DefaultParent);
            Assert.DoesNotContain("DefaultParent", new WorldDocument().ToJson());
        }
    }
}
