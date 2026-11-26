using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[mate] Parts placed by relations between their faces and axes, with the freedom each leaves.</summary>
    public class WorldMateTests
    {
        private readonly ITestOutputHelper _out;

        public WorldMateTests(ITestOutputHelper output) => _out = output;

        private static MateFeature Face(WorldObject o, double x, double y, double z, double nx, double ny, double nz) =>
            new MateFeature { Object = o.Id, Kind = MateFeatureKind.Plane, X = x, Y = y, Z = z, DX = nx, DY = ny, DZ = nz };

        private static MateFeature Axis(WorldObject o, double x, double y, double z, double dx, double dy, double dz) =>
            new MateFeature { Object = o.Id, Kind = MateFeatureKind.Axis, X = x, Y = y, Z = z, DX = dx, DY = dy, DZ = dz };

        /// <summary>A fixed 400 × 300 × 20 mm table and a 50 mm cube floating, tilted, above it.</summary>
        private static (WorldDocument W, WorldObject Table, WorldObject Cube) Table()
        {
            var w = new WorldDocument();
            var table = w.AddBody("box", 0.4, 0.3, 0.02, new WorldPose { Z = 0.01 });
            table.IsStatic = true;
            var cube = w.AddBody("box", 0.05, 0.05, 0.05, new WorldPose { X = 0.1, Y = 0.05, Z = 0.2, Roll = 20, Pitch = 10, Yaw = 30 });
            return (w, table, cube);
        }

        [Fact]
        public void ACubeMatedOnATableSitsOnItAndCanStillSlideAndTurn()
        {
            var (w, table, cube) = Table();
            var (mate, r) = WorldMates.Add(w, MateKind.Mate, Face(table, 0, 0, 0.01, 0, 0, 1), Face(cube, 0, 0, -0.025, 0, 0, -1));
            var down = cube.Pose.Orientation.Rotate(new Vec3(0, 0, -1));
            _out.WriteLine($"{r.State}, serbestlik {r.Freedom}, {r.Iterations} yineleme; küp ({cube.Pose.X:0.####}, {cube.Pose.Y:0.####}, {cube.Pose.Z:0.####}), alt yüz {down}");
            Assert.Equal(MateState.UnderConstrained, r.State);
            Assert.Equal(3, r.Freedom);   // slides two ways and turns about the vertical
            Assert.Equal(0.045, cube.Pose.Z, 6);
            Assert.Equal(-1, down.Z, 6);
            // Moved as little as it could: straight down, not across the table.
            Assert.InRange(Math.Abs(cube.Pose.X - 0.1), 0, 0.001);
            Assert.InRange(Math.Abs(cube.Pose.Y - 0.05), 0, 0.001);
            Assert.True(table.IsStatic && table.Pose.Z == 0.01);
            Assert.Single(w.Mates!);
        }

        [Fact]
        public void AShaftInABearingKeepsTwoFreedomsAndAShoulderLeavesOne()
        {
            var w = new WorldDocument();
            var bearing = w.AddBody("cylinder", 0.022, 0.022, 0.007, new WorldPose { Z = 0.1 });
            bearing.IsStatic = true;
            var shaft = w.AddBody("cylinder", 0.008, 0.008, 0.1, new WorldPose { X = 0.05, Y = 0.02, Z = 0.3, Roll = 15, Yaw = 40 });
            var (_, r) = WorldMates.Add(w, MateKind.Concentric, Axis(bearing, 0, 0, 0, 0, 0, 1), Axis(shaft, 0, 0, 0, 0, 0, 1));
            var axis = shaft.Pose.Orientation.Rotate(new Vec3(0, 0, 1));
            _out.WriteLine($"eşeksen: {r.State}, serbestlik {r.Freedom}; mil ({shaft.Pose.X:0.######}, {shaft.Pose.Y:0.######}, {shaft.Pose.Z:0.####}), eksen {axis}");
            Assert.Equal(2, r.Freedom);   // slides along and turns about its axis
            Assert.Equal(0, shaft.Pose.X, 6);
            Assert.Equal(0, shaft.Pose.Y, 6);
            Assert.Equal(1, Math.Abs(axis.Z), 6);
            // A shoulder 30 mm up the shaft against the bearing's top face: only the turning is left, a hinge.
            var (_, r2) = WorldMates.Add(w, MateKind.Mate, Face(bearing, 0, 0, 0.0035, 0, 0, 1), Face(shaft, 0, 0, -0.02, 0, 0, -1));
            _out.WriteLine($"+ omuz: {r2.State}, serbestlik {r2.Freedom}; mil z {shaft.Pose.Z:0.#####}");
            Assert.Equal(1, r2.Freedom);
            Assert.Equal(0.1 + 0.0035 + 0.02, shaft.Pose.Z, 6);
            Assert.Equal(1, r2.FreedomOf(shaft.Id));
            // A block on its own elsewhere does not count against the shaft's freedom.
            var block = w.AddBody("box", 0.02, 0.02, 0.02, new WorldPose { X = 0.3, Z = 0.05 });
            var floor = w.AddBody("box", 0.1, 0.1, 0.01, new WorldPose { X = 0.3 });
            floor.IsStatic = true;
            var (_, r3) = WorldMates.Add(w, MateKind.Mate, Face(floor, 0, 0, 0.01, 0, 0, 1), Face(block, 0, 0, 0, 0, 0, -1));
            Assert.Equal(4, r3.Freedom);
            Assert.Equal(1, r3.FreedomOf(shaft.Id));
            Assert.Equal(3, r3.FreedomOf(block.Id));
        }

        [Fact]
        public void MatesTurnIntoTheJointTheyLeave()
        {
            // A shaft in a bearing with its shoulder on the bearing: one turning left, a hinge on the bearing's axis.
            var w = new WorldDocument();
            var bearing = w.AddBody("cylinder", 0.022, 0.022, 0.007, new WorldPose { X = 0.1, Y = 0.05, Z = 0.1 });
            bearing.IsStatic = true;
            var shaft = w.AddBody("cylinder", 0.008, 0.008, 0.1, new WorldPose { X = 0.15, Y = 0.02, Z = 0.3, Roll = 15, Yaw = 40 });
            WorldMates.Add(w, MateKind.Concentric, Axis(bearing, 0, 0, 0, 0, 0, 1), Axis(shaft, 0, 0, 0, 0, 0, 1));
            Assert.Equal("2 serbestlik kaldı; eklem için bir ya da hiç kalmamalı", WorldMates.JointFor(w, shaft.Id).Why);
            WorldMates.Add(w, MateKind.Mate, Face(bearing, 0, 0, 0.0035, 0, 0, 1), Face(shaft, 0, 0, -0.02, 0, 0, -1));
            var (hinge, why) = WorldMates.JointFor(w, shaft.Id);
            _out.WriteLine($"menteşe: {hinge?.Kind} eksen ({hinge?.AxisX:0.###}, {hinge?.AxisY:0.###}, {hinge?.AxisZ:0.###}) nokta ({hinge?.AnchorX:0.####}, {hinge?.AnchorY:0.####}) {why}");
            Assert.Equal(WorldJointKind.Hinge, hinge!.Kind);
            Assert.Equal(1, Math.Abs(hinge.AxisZ), 6);
            Assert.Equal(0.1, hinge.AnchorX, 6);   // on the bearing's axis
            Assert.Equal(0.05, hinge.AnchorY, 6);
            Assert.Equal(bearing.Id, hinge.ObjectB);

            // A carriage on a rail: on its top, level with its side: one sliding left, a slider along the rail.
            var w2 = new WorldDocument();
            var rail = w2.AddBody("box", 0.3, 0.02, 0.02, new WorldPose());
            rail.IsStatic = true;
            var carriage = w2.AddBody("box", 0.04, 0.04, 0.02, new WorldPose { X = 0.05, Y = 0.01, Z = 0.05, Yaw = 10 });
            WorldMates.Add(w2, MateKind.Mate, Face(rail, 0, 0, 0.02, 0, 0, 1), Face(carriage, 0, 0, 0, 0, 0, -1));
            WorldMates.Add(w2, MateKind.Flush, Face(rail, 0, 0.01, 0.01, 0, 1, 0), Face(carriage, 0, 0.02, 0.01, 0, 1, 0), 0.01);
            var (slider, _) = WorldMates.JointFor(w2, carriage.Id);
            _out.WriteLine($"kızak: {slider?.Kind} eksen ({slider?.AxisX:0.###}, {slider?.AxisY:0.###}, {slider?.AxisZ:0.###})");
            Assert.Equal(WorldJointKind.Slider, slider!.Kind);
            Assert.Equal(1, Math.Abs(slider.AxisX), 6);
        }

        [Fact]
        public void AMateThatRepeatsAnotherIsNamed()
        {
            var (w, table, cube) = Table();
            WorldMates.Add(w, MateKind.Mate, Face(table, 0, 0, 0.01, 0, 0, 1), Face(cube, 0, 0, -0.025, 0, 0, -1));
            var (extra, r) = WorldMates.Add(w, MateKind.Parallel, Face(table, 0, 0, 0.01, 0, 0, 1), Face(cube, 0, 0, 0.025, 0, 0, 1));
            _out.WriteLine($"{r.State}: {string.Join(", ", r.Culprits)}");
            Assert.Equal(MateState.Redundant, r.State);
            Assert.Contains(extra.Id, r.Culprits);
        }

        [Fact]
        public void ContradictingMatesAreRefusedAndNothingMoves()
        {
            var (w, table, cube) = Table();
            WorldMates.Add(w, MateKind.Mate, Face(table, 0, 0, 0.01, 0, 0, 1), Face(cube, 0, 0, -0.025, 0, 0, -1));
            var before = cube.Pose.Clone();
            var (_, r) = WorldMates.Add(w, MateKind.Distance, Face(table, 0, 0, 0.01, 0, 0, 1), Face(cube, 0, 0, -0.025, 0, 0, -1), 0.03);
            _out.WriteLine($"{r.State}: {string.Join(", ", r.Culprits)}");
            Assert.Equal(MateState.Conflicting, r.State);
            Assert.Equal(2, r.Culprits.Count);
            Assert.Single(w.Mates!);   // the new one was taken back
            Assert.Equal(before.Z, cube.Pose.Z, 12);
        }

        [Fact]
        public void APartScrewedToAnotherMovesWithIt()
        {
            var (w, table, cube) = Table();
            var lid = w.AddBody("box", 0.05, 0.05, 0.005, new WorldPose { X = 0.1, Y = 0.05, Z = 0.2275, Roll = 20, Pitch = 10, Yaw = 30 });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = lid.Id, ObjectB = cube.Id, Fastening = "screws" });
            var gap = cube.Pose.Orientation.InverseRotate(lid.Pose.Position - cube.Pose.Position);
            var turn = cube.Pose.Orientation.Conjugate * lid.Pose.Orientation;
            WorldMates.Add(w, MateKind.Mate, Face(table, 0, 0, 0.01, 0, 0, 1), Face(cube, 0, 0, -0.025, 0, 0, -1));
            var after = cube.Pose.Orientation.InverseRotate(lid.Pose.Position - cube.Pose.Position);
            var turnAfter = cube.Pose.Orientation.Conjugate * lid.Pose.Orientation;
            Assert.Equal(0.045, cube.Pose.Z, 6);
            Assert.InRange((after - gap).Length, 0, 1e-8);   // still where it was on the cube
            Assert.InRange(Math.Abs(Math.Abs(turn.W * turnAfter.W + turn.X * turnAfter.X + turn.Y * turnAfter.Y + turn.Z * turnAfter.Z) - 1), 0, 1e-9);   // and turned with it
        }

        [Fact]
        public void MatesTravelInTheFileAndGoWithTheirParts()
        {
            var (w, table, cube) = Table();
            WorldMates.Add(w, MateKind.Mate, Face(table, 0, 0, 0.01, 0, 0, 1), Face(cube, 0, 0, -0.025, 0, 0, -1), 0.002);
            var back = WorldDocument.FromJson(w.ToJson());
            Assert.Single(back.Mates!);
            Assert.Equal(0.002, back.Mates![0].Value, 12);
            Assert.Equal(MateState.UnderConstrained, WorldMates.Solve(back).State);
            Assert.Equal(0.047, back.Objects.Single(o => o.Id == cube.Id).Pose.Z, 6);
            back.Remove(back.Objects.Single(o => o.Id == cube.Id));
            Assert.Null(back.Mates);
        }
    }
}
