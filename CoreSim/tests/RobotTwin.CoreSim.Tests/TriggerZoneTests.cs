using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[trigger] Unity's trigger zones: nothing bumps into them, they know what passes through and what rests in them.</summary>
    public class TriggerZoneTests
    {
        [Fact]
        public void APartFallsThroughAZoneAndIsSeenComingAndGoing()
        {
            var w = new WorldDocument();
            var zone = new WorldObject { Id = w.NextId("O"), Name = "Algı", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.3, SizeY = 0.3, SizeZ = 0.2, Pose = new WorldPose { Z = 0.5 } };
            zone.Parameters[WorldPhysics.TriggerKey] = "1";
            w.Objects.Add(zone);
            var box = new WorldObject { Id = w.NextId("O"), Name = "Kutu", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.04, SizeY = 0.04, SizeZ = 0.04, MassKg = 0.1, Pose = new WorldPose { Z = 1.0 } };
            w.Objects.Add(box);
            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default).GeometryOf, null);
            var events = new System.Collections.Generic.List<PhysicsEvent>();
            bool sawInside = false;
            for (int i = 0; i < 1500; i++)
            {
                p.Advance(1e-3, null);
                events.AddRange(p.DrainEvents());
                sawInside |= p.TriggerOccupants(zone.Id).Contains(box.Id);
            }

            Assert.True(sawInside);
            Assert.Contains(events, e => e.Kind == PhysicsEventKind.TriggerEnter && e.Source == zone.Id);
            Assert.Contains(events, e => e.Kind == PhysicsEventKind.TriggerExit && e.Source == zone.Id);
            Assert.Empty(p.TriggerOccupants(zone.Id));
            Assert.InRange(p.PoseOf(box.Id)!.Value.Position.Z, -0.001, 0.01);   // through the zone onto the floor
            Assert.Null(p.PoseOf(zone.Id));   // the zone is no body
        }

        [Fact]
        public void APartRestingInAZoneIsInside()
        {
            var w = new WorldDocument();
            var zone = new WorldObject { Id = w.NextId("O"), Name = "Bölge", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.3, SizeY = 0.3, SizeZ = 0.1 };
            zone.Parameters[WorldPhysics.TriggerKey] = "1";
            w.Objects.Add(zone);
            var box = new WorldObject { Id = w.NextId("O"), Name = "Kutu", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.04, SizeY = 0.04, SizeZ = 0.04, MassKg = 0.1, Pose = new WorldPose { X = 0.05 } };
            w.Objects.Add(box);
            var outside = new WorldObject { Id = w.NextId("O"), Name = "Uzak", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.04, SizeY = 0.04, SizeZ = 0.04, MassKg = 0.1, Pose = new WorldPose { X = 0.6 } };
            w.Objects.Add(outside);
            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default).GeometryOf, null);
            for (int i = 0; i < 50; i++) p.Advance(1e-3, null);
            Assert.Equal(new[] { box.Id }, p.TriggerOccupants(zone.Id).ToArray());
        }
    }
}
