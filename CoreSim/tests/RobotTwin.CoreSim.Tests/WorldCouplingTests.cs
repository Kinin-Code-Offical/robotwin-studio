using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[couple] Gears and lead screws in a world: saved with it, built into its physics, gone with their joints.</summary>
    public class WorldCouplingTests
    {
        private readonly ITestOutputHelper _out;

        public WorldCouplingTests(ITestOutputHelper output) => _out = output;

        private static WorldObject Body(WorldDocument w, string id, string shape, double x, double z, double sx, double sz)
        {
            var o = new WorldObject { Id = id, Name = id, Kind = WorldObjectKind.Body, Shape = shape, SizeX = sx, SizeY = sx, SizeZ = sz, Pose = new WorldPose { X = x, Z = z } };
            w.Objects.Add(o);
            return o;
        }

        private static WorldJoint Joint(WorldDocument w, WorldObject o, WorldJointKind kind, double speed = 0)
        {
            var j = new WorldJoint { Id = "j-" + o.Id, Kind = kind, ObjectA = o.Id, AnchorX = o.Pose.X, AnchorZ = o.Pose.Z, AxisZ = 1 };
            if (speed != 0)
            {
                j.Drive = WorldJoint.DriveSpeedMode;
                j.DriveTarget = speed;
                j.DriveForce = 2;
            }

            w.Joints.Add(j);
            return j;
        }

        [Fact]
        public void AGearPairAndALeadScrewRunInTheWorld()
        {
            var world = new WorldDocument();
            var pinion = Joint(world, Body(world, "pinyon", "cylinder", 0, 0.2, 0.02, 0.01), WorldJointKind.Hinge, 360);   // one turn a second
            var wheel = Joint(world, Body(world, "cark", "cylinder", 0.03, 0.2, 0.04, 0.01), WorldJointKind.Hinge);
            var screw = Joint(world, Body(world, "mil", "cylinder", 0.2, 0.15, 0.008, 0.2), WorldJointKind.Hinge, 360);
            var carriage = Body(world, "tasiyici", "box", 0.23, 0.15, 0.03, 0.02);
            carriage.MassKg = 0.5;
            var nut = Joint(world, carriage, WorldJointKind.Slider);
            world.Couplings = new()
            {
                new WorldCoupling { Id = "dişli", Kind = WorldCoupling.Gear, JointA = pinion.Id, JointB = wheel.Id, SizeA = 20, SizeB = 40 },
                new WorldCoupling { Id = "vida", Kind = WorldCoupling.Screw, JointA = screw.Id, JointB = nut.Id, SizeA = 8, SizeB = 8 }
            };

            world = WorldDocument.FromJson(world.ToJson());   // saved and opened again
            Assert.Equal(2, world.Couplings!.Count);
            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            while (runner.Time < 1 - 1e-6) runner.Step(0.01);
            var gear = runner.Physics!.CouplingState("dişli")!.Value;
            var lead = runner.Physics.CouplingState("vida")!.Value;
            double thread = runner.Physics.Scene.Couplings.Single(c => c.Id == "vida").Efficiency;
            _out.WriteLine($"pinyon {gear.SpeedA * 30 / Math.PI:0.0} dev/dk, çark {gear.SpeedB * 30 / Math.PI:0.0} dev/dk; mil {lead.SpeedA * 30 / Math.PI:0.0} dev/dk, taşıyıcı {lead.SpeedB * 1000:0.00} mm/s, vida yükü {lead.Load:0.00} N, verim %{thread * 100:0}");
            Assert.InRange(gear.SpeedA, 2 * Math.PI - 0.01, 2 * Math.PI + 0.01);
            Assert.InRange(gear.SpeedB, -Math.PI - 0.01, -Math.PI + 0.01);   // 20 teeth turn 40 at half the speed, the other way
            Assert.InRange(lead.SpeedB, 0.0079, 0.0081);   // 8 mm a turn
            Assert.InRange(thread, 0.6, 0.72);   // a T8×8 with a plastic nut
            Assert.InRange(Math.Abs(lead.Load), 4.0, 6.0);   // it lifts the carriage: its 4.9 N on the thread

            // Deleting a gear takes its coupling with it.
            world.Remove(world.Find("cark")!);
            Assert.Single(world.Couplings!);
        }

        [Theory]
        [InlineData(8, 8, 0.66, false)]   // T8×8: the printer's fast Z screw falls with the motor off
        [InlineData(2, 8, 0.36, true)]   // T8×2: holds
        [InlineData(5, 16, 0.41, true)]   // Tr16×5: a machine's slow axis
        public void AThreadsEfficiencyFollowsItsAngle(double lead, double diameter, double expected, bool selfLocking)
        {
            double eta = WorldCoupling.ScrewEfficiency(lead, diameter);
            _out.WriteLine($"Tr{diameter}×{lead}: verim %{eta * 100:0.0}");
            Assert.InRange(eta, expected - 0.03, expected + 0.03);
            Assert.Equal(selfLocking, eta <= 0.5);
        }

        [Fact]
        public void TheRatioFollowsTheKindAndTheJoints()
        {
            var gear = new WorldCoupling { Kind = WorldCoupling.Gear, SizeA = 20, SizeB = 40 };
            Assert.Equal(-0.5, gear.Ratio(WorldJointKind.Hinge, WorldJointKind.Hinge));
            Assert.Equal(0.01, gear.Ratio(WorldJointKind.Hinge, WorldJointKind.Slider)!.Value, 12);   // a 20 mm pinion: 10 mm per radian
            var belt = new WorldCoupling { Kind = WorldCoupling.Belt, SizeA = 20, SizeB = 40, Reverse = true };   // a crossed belt
            Assert.Equal(-0.5, belt.Ratio(WorldJointKind.Hinge, WorldJointKind.Hinge));
            var screw = new WorldCoupling { Kind = WorldCoupling.Screw, SizeA = 8, SizeB = 8 };
            Assert.Null(screw.Ratio(WorldJointKind.Hinge, WorldJointKind.Hinge));   // a screw needs a slider
            Assert.Null(gear.Ratio(WorldJointKind.Slider, WorldJointKind.Slider));
            Assert.Equal(2 * Math.PI / 0.008, screw.Ratio(WorldJointKind.Slider, WorldJointKind.Hinge)!.Value, 9);   // the nut driving the screw
        }
    }
}
