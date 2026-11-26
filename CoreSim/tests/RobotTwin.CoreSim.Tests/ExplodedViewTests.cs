using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[explode] SolidWorks' exploded view: steps in order, parts joined on ride along, automatic spreads.</summary>
    public class ExplodedViewTests
    {
        private static WorldObject Box(WorldDocument w, string name, double x, double z, double size = 0.02)
        {
            var o = new WorldObject { Id = w.NextId("O"), Name = name, Kind = WorldObjectKind.Body, Shape = "box", SizeX = size, SizeY = size, SizeZ = size, MassKg = 0.1, Pose = new WorldPose { X = x, Z = z } };
            w.Objects.Add(o);
            return o;
        }

        [Fact]
        public void StepsPlayInOrderAndJoinedPartsRideAlong()
        {
            var w = new WorldDocument();
            var chassis = Box(w, "Şasi", 0, 0.01, 0.1);
            var lid = Box(w, "Kapak", 0, 0.07);
            var sensor = Box(w, "Sensör", 0, 0.09, 0.01);
            w.Joints.Add(new WorldJoint { ObjectA = sensor.Id, ObjectB = lid.Id });   // the sensor sits on the lid
            var steps = new[]
            {
                new ExplodeStep { Objects = { lid.Id }, DirZ = 1, Distance = 0.05 },
                new ExplodeStep { Objects = { sensor.Id }, DirX = 1, DirZ = 0, Distance = 0.02 }
            };

            var full = ExplodedView.Offsets(w, steps, 1);
            Assert.Equal(0.05, full[lid.Id].Z, 9);
            Assert.Equal(0.05, full[sensor.Id].Z, 9);   // carried by the lid
            Assert.Equal(0.02, full[sensor.Id].X, 9);   // and its own step on top
            Assert.False(full.ContainsKey(chassis.Id));

            var half = ExplodedView.Offsets(w, steps, 0.5);   // the first step done, the second not begun
            Assert.Equal(0.05, half[lid.Id].Z, 9);
            Assert.Equal(0, half[sensor.Id].X, 9);
            var quarter = ExplodedView.Offsets(w, steps, 0.25);
            Assert.Equal(0.025, quarter[lid.Id].Z, 9);
            Assert.Empty(ExplodedView.Offsets(w, steps, 0));

            w.Explode = steps.ToList();
            w.Remove(lid);
            Assert.Single(w.Explode!);
        }

        [Fact]
        public void AutoSpreadsAlongAnAxisInOrderAndRadiallyOutward()
        {
            var w = new WorldDocument();
            var plate = Box(w, "Taban", 0, 0.005, 0.1);
            plate.SizeZ = 0.01;   // a flat base plate
            var a = Box(w, "A", 0, 0.03);
            var b = Box(w, "B", 0, 0.06);
            var under = Box(w, "Alt", 0, -0.02);
            var geo = new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default);
            var steps = ExplodedView.Auto(w, geo, ExplodeLayout.Axis, Vec3.UnitZ, 0.03);
            Assert.Equal(3, steps.Count);   // the biggest stays
            var off = ExplodedView.Offsets(w, steps, 1, geo);
            Assert.Equal(0.03, off[a.Id].Z, 9);
            Assert.Equal(0.06, off[b.Id].Z, 9);
            Assert.Equal(-0.03, off[under.Id].Z, 9);
            Assert.False(off.ContainsKey(plate.Id));

            var radial = ExplodedView.Offsets(w, ExplodedView.Auto(w, geo, ExplodeLayout.Radial, Vec3.UnitZ, 0.02), 1, geo);
            // each moves away from the middle: the spread between the parts grows
            var before = new[] { a, b, under }.Select(o => new Vec3(o.Pose.X, o.Pose.Y, o.Pose.Z)).ToList();
            var after = new[] { a, b, under }.Select((o, i) => before[i] + radial[o.Id]).ToList();
            Assert.True((after[1] - after[2]).Length > (before[1] - before[2]).Length + 0.03);
        }

        [Fact]
        public void APartBesideTheBaseComesAwayAcrossNotIntoTheFloor()
        {
            // a plywood base and a board on the floor next to it (the servo-arm example's Arduino)
            var w = new WorldDocument();
            var plate = Box(w, "Taban", 0, 0.009, 0.1);
            plate.SizeZ = 0.018;
            var board = Box(w, "Arduino", -0.09, 0.005);
            board.SizeZ = 0.01;
            var geo = new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default);
            var off = ExplodedView.Offsets(w, ExplodedView.Auto(w, geo, ExplodeLayout.Axis, Vec3.UnitZ, 0.03), 1, geo);
            Assert.Equal(0, off[board.Id].Z, 9);          // not pushed under the floor
            Assert.Equal(-0.03, off[board.Id].X, 9);      // away from the base, across
        }

        [Fact]
        public void AJoinedChainOpensUpLevelByLevelDeepestFirst()
        {
            var w = new WorldDocument();
            var plate = Box(w, "Taban", 0, 0.005, 0.1);
            plate.SizeZ = 0.01;
            var servo = Box(w, "Servo", 0, 0.03);
            var arm = Box(w, "Kol", 0, 0.05);
            var hand = Box(w, "Uç", 0, 0.07);
            w.Joints.Add(new WorldJoint { ObjectA = servo.Id, ObjectB = plate.Id });
            w.Joints.Add(new WorldJoint { ObjectA = arm.Id, ObjectB = servo.Id });
            w.Joints.Add(new WorldJoint { ObjectA = hand.Id, ObjectB = arm.Id });
            var geo = new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default);
            var steps = ExplodedView.Auto(w, geo, ExplodeLayout.Axis, Vec3.UnitZ, 0.02);
            Assert.Equal(new[] { hand.Id, arm.Id, servo.Id }, steps.Select(s => s.Objects.Single()));   // the deepest comes off first
            var off = ExplodedView.Offsets(w, steps, 1, geo);
            Assert.Equal(0.02, off[servo.Id].Z, 9);
            Assert.Equal(0.04, off[arm.Id].Z, 9);    // its own gap plus the servo's
            Assert.Equal(0.06, off[hand.Id].Z, 9);
            Assert.False(off.ContainsKey(plate.Id));
        }
    }
}
