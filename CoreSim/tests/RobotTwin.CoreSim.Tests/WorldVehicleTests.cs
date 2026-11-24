using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[mobile] Kinematics of wheeled robots built in a world: wheels, forward / inverse kinematics, odometry against the physics.</summary>
    public class WorldVehicleTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public WorldVehicleTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static WorldObject Plate(WorldDocument w) => w.Objects.First(o => o.Kind == WorldObjectKind.Body && o.Shape == "plate");

        [Fact]
        public void TheCarIsATwoWheelDifferentialDrive()
        {
            var w = WorldExamples.CarDetailed(Lib);
            var g = new ApproximateGeometry(w, Lib).GeometryOf;
            // Any part of the car finds it: the battery, a wheel, the plate.
            var v = WorldVehicle.Build(w, g, w.Objects.First(o => (o.PresetId ?? o.PartId) == "bat-4aa").Id)!;
            Assert.NotNull(v);
            Assert.Equal(Plate(w).Id, v.BaseId);
            Assert.Equal(2, v.Wheels.Count);
            Assert.All(v.Wheels, x => Assert.Equal(DriveWheelKind.Standard, x.Kind));
            Assert.All(v.Wheels, x => Assert.Equal(0.0325, x.Radius, 6));
            Assert.False(v.Holonomic);
            Assert.False(v.Skids);
            Assert.Contains("diferansiyel", v.Layout);
            _out.WriteLine($"track {v.Track * 1000:F1} mm, wheels at x {string.Join(", ", v.Wheels.Select(x => (x.Position.X * 1000).ToString("F1")))} mm");
            Assert.InRange(v.Track, 0.12, 0.135);
        }

        [Fact]
        public void DifferentialKinematicsIsTheTextbookFormula()
        {
            var w = WorldExamples.CarDetailed(Lib);
            var v = WorldVehicle.Build(w, new ApproximateGeometry(w, Lib).GeometryOf, Plate(w).Id)!;
            var left = v.Wheels.First(x => x.Left);
            var right = v.Wheels.First(x => !x.Left);
            // v = 0.2 m/s, ω = 1.5 rad/s: v_L = v − ω·b/2, v_R = v + ω·b/2 (the wheels sit on the axle line).
            var twist = new PlanarTwist(0.2, 0, 1.5);
            var rates = v.ShaftRates(twist);
            double vl = rates[v.Wheels.ToList().IndexOf(left)] * left.Radius * left.Roll.X;
            double vr = rates[v.Wheels.ToList().IndexOf(right)] * right.Radius * right.Roll.X;
            Assert.Equal(0.2 - 1.5 * (left.Position.Y), vl, 9);
            Assert.Equal(0.2 - 1.5 * (right.Position.Y), vr, 9);
            // The right motor is mounted mirrored: going straight its shaft turns the other way round.
            var straight = v.ShaftRates(new PlanarTwist(0.2, 0, 0));
            Assert.True(straight[0] * straight[1] < 0);
            // Back again: the plate's origin sits 41 mm ahead of the axle, so turning it also slides sideways at ω·41 mm.
            var back = v.Twist(rates);
            double ahead = -left.Position.X;
            Assert.Equal(0.2, back.Vx, 6);
            Assert.Equal(1.5 * ahead, back.Vy, 6);
            Assert.Equal(1.5, back.W, 6);
            // Sideways is not possible: what the wheels can give keeps the axle from sliding.
            var can = v.Achievable(new PlanarTwist(0.1, 0.1, 0));
            Assert.Equal(0.1, can.Vx, 6);
            Assert.Equal(0, can.Vy, 6);
        }

        /// <summary>A 160 × 120 mm chassis on four TT motors with mecanum wheels in the X pattern (A front-left and rear-right).</summary>
        private static (WorldDocument World, WorldObject Chassis) MecanumBase()
        {
            var w = new WorldDocument { Title = "Mecanum" };
            const double r = 0.0325, shaftZ = 0.0112, shaftTip = 0.0184, shaftX = -0.0227;
            double motorZ = r - shaftZ;
            var chassis = w.AddBody("plate", 0.16, 0.10, 0.003, new WorldPose { Z = motorZ + 0.0248 }, "PLA");
            chassis.MassKg = 0.35;
            foreach (var (x, left, hand) in new[] { (0.055, true, "a"), (0.055, false, "b"), (-0.055, true, "b"), (-0.055, false, "a") })
            {
                double y = left ? 0.03 : -0.03;
                // Right side motors turned round (yaw 180°): their shafts point outwards too.
                var motor = w.AddComponent(Lib, "motor-tt", left
                    ? new WorldPose { X = x - shaftX, Y = y, Z = motorZ }
                    : new WorldPose { X = x + shaftX, Y = y, Z = motorZ, Yaw = 180 });
                double wy = left ? y + shaftTip + 0.02 : y - shaftTip - 0.02;
                var wheel = ReadyParts.Add(w, "mecanum-65-" + hand, new WorldPose { X = x, Y = wy, Z = 0 });
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = motor.Id, ObjectB = chassis.Id });
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = wheel.Id, ObjectB = motor.Id, PortB = "shaft" });
            }

            return (w, chassis);
        }

        [Fact]
        public void AMecanumBaseGoesEveryWay()
        {
            var (w, chassis) = MecanumBase();
            var v = WorldVehicle.Build(w, new ApproximateGeometry(w, Lib).GeometryOf, chassis.Id)!;
            Assert.Equal(4, v.Wheels.Count);
            Assert.All(v.Wheels, x => Assert.Equal(DriveWheelKind.Mecanum, x.Kind));
            Assert.True(v.Holonomic);
            Assert.Contains("mecanum", v.Layout);
            foreach (var t in new[] { new PlanarTwist(0.1, 0.15, 0.5), new PlanarTwist(0, 0.2, 0), new PlanarTwist(0, 0, 2) })
            {
                var back = v.Twist(v.ShaftRates(t));
                Assert.Equal(t.Vx, back.Vx, 6);
                Assert.Equal(t.Vy, back.Vy, 6);
                Assert.Equal(t.W, back.W, 6);
            }
        }

        /// <summary>Drives the shafts at fixed speeds (velocity control, a strong motor) and returns the physics.</summary>
        private static WorldPhysics Drive(WorldDocument w, Func<WorldObject, WorldBodyGeometry> g, WorldVehicle v, double[] rates, double seconds, Action<WorldPhysics>? each = null)
        {
            var p = WorldPhysics.Build(w, g, null);
            var want = v.Wheels.Select((x, i) => (x.MotorId, rates[i])).ToDictionary(x => x.MotorId, x => x.Item2);
            p.DriveOverride = (id, j) =>
            {
                if (!want.TryGetValue(id, out double rate)) return false;
                j.Drive = JointDrive.Velocity;
                j.TargetSpeed = rate;
                j.Damping = 0;
                j.MaxTorque = 0.6;
                return true;
            };
            int steps = (int)Math.Round(seconds / 1e-3);
            for (int i = 0; i < steps; i++)
            {
                p.Advance(1e-3, null);
                if (i % 10 == 9) each?.Invoke(p);
            }

            return p;
        }

        [Fact]
        public void TheMecanumPhysicsSlidesWhereTheKinematicsSays()
        {
            var (w, chassis) = MecanumBase();
            var g = new ApproximateGeometry(w, Lib).GeometryOf;
            var v = WorldVehicle.Build(w, g, chassis.Id)!;
            var p = Drive(w, g, v, v.ShaftRates(new PlanarTwist(0, 0.15, 0)), 1.5);
            var start = v.PlanarPose(null)!.Value;
            var end = v.PlanarPose(p)!.Value;
            double dx = end.X - start.X, dy = end.Y - start.Y;
            double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
            _out.WriteLine($"asked 0.15 m/s to the left: moved {dx * 1000:F0} mm forward, {dy * 1000:F0} mm left, heading {(end.Heading - start.Heading) * 180 / Math.PI:F1}° (direction {angle:F0}°)");
            Assert.True(dy > 0.05, "it slides left");
            Assert.InRange(angle, 70, 110);
        }

        [Fact]
        public void EncoderOdometryFollowsTheCarOnAFlatFloor()
        {
            var w = WorldExamples.CarDetailed(Lib);
            var g = new ApproximateGeometry(w, Lib).GeometryOf;
            var v = WorldVehicle.Build(w, g, Plate(w).Id)!;
            var odo = new WheelOdometry();
            var start = v.PlanarPose(null)!.Value;
            odo.Reset(start.X, start.Y, start.Heading);
            var twist = new PlanarTwist(0.15, 0, 0.8);
            var measured = new List<PlanarTwist>();
            var p = Drive(w, g, v, v.ShaftRates(twist), 3.0, ph =>
            {
                odo.Update(v, v.ShaftAngles(ph));
                measured.Add(v.MeasuredTwist(ph));
            });
            var truth = v.PlanarPose(p)!.Value;
            double err = Math.Sqrt((truth.X - odo.X) * (truth.X - odo.X) + (truth.Y - odo.Y) * (truth.Y - odo.Y));
            double headingErr = Math.Abs(Math.IEEERemainder(truth.Heading - odo.Heading, 2 * Math.PI)) * 180 / Math.PI;
            var late = measured.Skip(measured.Count / 2).ToList();
            double vx = late.Average(m => m.Vx), wz = late.Average(m => m.W);
            _out.WriteLine($"odometry ({odo.X * 1000:F0}, {odo.Y * 1000:F0}) mm {odo.Heading * 180 / Math.PI:F1}°, truth ({truth.X * 1000:F0}, {truth.Y * 1000:F0}) mm {truth.Heading * 180 / Math.PI:F1}°: {err * 1000:F1} mm, {headingErr:F1}° over {odo.Distance * 1000:F0} mm");
            _out.WriteLine($"steady speed: {vx:F3} m/s, {wz:F3} rad/s (asked {twist.Vx}, {twist.W})");
            Assert.True(odo.Distance > 0.4);
            Assert.True(err < 0.02 * odo.Distance, "position drift under 2 % of the distance");
            Assert.True(headingErr < 2);
            Assert.InRange(vx, 0.145, 0.155);
            Assert.InRange(wz, 0.78, 0.82);
        }

        /// <summary>
        /// [phys] A two-wheel car turns at the rate its wheel spacing gives (ω = (v_R − v_L) / track), on the spot and on
        /// an arc. A tyre's tread used to grip at one edge only, so the car turned about its inner edges, 26 % too fast.
        /// </summary>
        [Theory]
        [InlineData(0.0, 0.8)]
        [InlineData(0.15, 0.8)]
        [InlineData(0.2, -1.5)]
        public void ACarTurnsAboutTheMiddleOfItsTyres(double speed, double turn)
        {
            var w = WorldExamples.CarDetailed(Lib);
            var g = new ApproximateGeometry(w, Lib).GeometryOf;
            var v = WorldVehicle.Build(w, g, Plate(w).Id)!;
            var samples = new List<PlanarTwist>();
            Drive(w, g, v, v.ShaftRates(new PlanarTwist(speed, 0, turn)), 2.0, ph => samples.Add(v.MeasuredTwist(ph)));
            var late = samples.Skip(samples.Count / 2).ToList();
            double vx = late.Average(m => m.Vx), wz = late.Average(m => m.W);
            _out.WriteLine($"asked {speed:F2} m/s, {turn:F2} rad/s: got {vx:F4} m/s, {wz:F4} rad/s");
            Assert.InRange(wz, turn - 0.02 * Math.Abs(turn), turn + 0.02 * Math.Abs(turn));
            Assert.InRange(vx, speed - 0.003, speed + 0.003);
        }
    }
}
