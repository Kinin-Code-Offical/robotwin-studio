using System;
using RobotTwin.CoreSim.Mechanics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[couple] Gears, belts and lead screws as constraints: the speed ratio, and the load felt back through them.</summary>
    public class CouplingTests
    {
        private readonly ITestOutputHelper _out;

        public CouplingTests(ITestOutputHelper output) => _out = output;

        private static (PhysicsScene Scene, SceneJoint Drive, SceneJoint Driven) TwoShafts(JointKind second = JointKind.Hinge)
        {
            var scene = new PhysicsScene();
            var a = SceneBody.Cylinder("a", 0.03, 0.01, 0.1, new Vec3(0, 0, 1));
            var b = second == JointKind.Slider ? SceneBody.Box("b", new Vec3(0.05, 0.05, 0.05), 0.5, new Vec3(0.2, 0, 1)) : SceneBody.Cylinder("b", 0.06, 0.01, 0.4, new Vec3(0.2, 0, 1));
            scene.AddBody(a);
            scene.AddBody(b);
            var ja = scene.AddJoint(JointKind.Hinge, a, null, new Vec3(0, 0, 1), Vec3.UnitZ, "ja");
            var jb = scene.AddJoint(second, b, null, new Vec3(0.2, 0, 1), second == JointKind.Slider ? Vec3.UnitX : Vec3.UnitZ, "jb");
            ja.Drive = JointDrive.Velocity;
            ja.TargetSpeed = 10;
            ja.MaxTorque = 5;
            return (scene, ja, jb);
        }

        [Fact]
        public void AGearPairTurnsTheOtherWayAtItsRatioAndFeelsItsLoad()
        {
            var (scene, drive, driven) = TwoShafts();
            var gear = scene.AddCoupling(drive, driven, -0.5);   // 20 teeth driving 40: half the speed, the other way
            driven.MotorTorque = 0.2;   // a load resisting the driven shaft's (negative) turning
            for (int i = 0; i < 1000; i++) scene.Step(1e-3);
            _out.WriteLine($"sürücü {drive.Speed:0.000} rad/s, sürülen {driven.Speed:0.000} rad/s, sürücünün torku {drive.DriveTorque:0.0000} N·m, dişlinin yükü {gear.Load:0.0000}");
            Assert.InRange(drive.Speed, 9.99, 10.01);
            Assert.InRange(driven.Speed, -5.01, -4.99);
            // Power through the mesh: 0.2 N·m at 5 rad/s on the driven side is 0.1 N·m at 10 rad/s on the driving one.
            Assert.InRange(drive.DriveTorque, 0.095, 0.105);
            Assert.InRange(Math.Abs(0.5 * drive.Angle + driven.Angle), 0, 1e-3);   // the teeth do not slip
        }

        [Fact]
        public void ALeadScrewMovesItsNutByThePitchPerTurn()
        {
            var (scene, screw, nut) = TwoShafts(JointKind.Slider);
            double pitch = 0.004;   // 4 mm a turn
            scene.AddCoupling(screw, nut, pitch / (2 * Math.PI));
            screw.TargetSpeed = 2 * Math.PI;   // one turn a second
            for (int i = 0; i < 1000; i++) scene.Step(1e-3);
            _out.WriteLine($"vida {screw.Angle / (2 * Math.PI):0.000} tur, somun {nut.Angle * 1000:0.000} mm, {nut.Speed * 1000:0.000} mm/s");
            Assert.InRange(nut.Speed, 0.00399, 0.00401);
            Assert.InRange(nut.Angle, pitch * screw.Angle / (2 * Math.PI) - 1e-5, pitch * screw.Angle / (2 * Math.PI) + 1e-5);
        }

        /// <summary>A vertical axis on a lead screw with the motor off: the carriage's weight tries to turn the screw back.</summary>
        private static (double Drop, double Spin) HangOnAScrew(double efficiency)
        {
            var scene = new PhysicsScene();
            var rod = SceneBody.Cylinder("rod", 0.004, 0.2, 0.05, new Vec3(0, 0, 0.3));
            var carriage = SceneBody.Box("carriage", new Vec3(0.04, 0.04, 0.02), 2.0, new Vec3(0.05, 0, 0.3));
            scene.AddBody(rod);
            scene.AddBody(carriage);
            var screw = scene.AddJoint(JointKind.Hinge, rod, null, new Vec3(0, 0, 0.3), Vec3.UnitZ, "screw");
            var nut = scene.AddJoint(JointKind.Slider, carriage, null, new Vec3(0.05, 0, 0.3), Vec3.UnitZ, "nut");
            var c = scene.AddCoupling(screw, nut, 0.008 / (2 * Math.PI));   // T8×8: 8 mm a turn
            c.Efficiency = efficiency;
            for (int i = 0; i < 200; i++) scene.Step(1e-3);
            return (-nut.Angle, screw.Angle / (2 * Math.PI));
        }

        [Fact]
        public void AFineLeadScrewHoldsItsLoadAndASteepOneLetsItFall()
        {
            var steep = HangOnAScrew(0.66);   // T8×8 with a plastic nut: back-drivable
            var fine = HangOnAScrew(0.36);   // T8×2's thread angle: self-locking
            _out.WriteLine($"verim %66: taşıyıcı 0,2 s'de {steep.Drop * 1000:0.0} mm düştü, mil {steep.Spin:0.00} tur döndü; verim %36: {fine.Drop * 1000:0.000} mm, {fine.Spin:0.000} tur");
            // Back-driven, the thread passes on (2 − 1/η) of the force: the screw's inertia seen from the carriage grows by its
            // inverse, a = g·m / (m + I / (r²·(2 − 1/η))), slower than free fall (0.196 m in 0.2 s).
            double r = 0.008 / (2 * Math.PI), inertia = 0.5 * 0.05 * 0.004 * 0.004, back = 2 - 1 / 0.66;
            double expected = 0.5 * 9.81 * 2.0 / (2.0 + inertia / (r * r * back)) * 0.2 * 0.2;
            Assert.InRange(steep.Drop, expected * 0.95, expected * 1.05);
            Assert.InRange(fine.Drop, -1e-3, 1e-3);   // a self-locking screw holds
        }

        [Fact]
        public void DrivingThroughALossyGearCostsMoreTorque()
        {
            var (scene, drive, driven) = TwoShafts();
            var gear = scene.AddCoupling(drive, driven, -0.5);
            gear.Efficiency = 0.8;
            driven.MotorTorque = 0.2;
            for (int i = 0; i < 1000; i++) scene.Step(1e-3);
            _out.WriteLine($"verim %80: sürücünün torku {drive.DriveTorque:0.0000} N·m (kayıpsız 0,1000)");
            Assert.InRange(driven.Speed, -5.01, -4.99);
            Assert.InRange(drive.DriveTorque, 0.1 / 0.8 - 0.006, 0.1 / 0.8 + 0.006);   // 1/η of the ideal torque
        }
    }
}
