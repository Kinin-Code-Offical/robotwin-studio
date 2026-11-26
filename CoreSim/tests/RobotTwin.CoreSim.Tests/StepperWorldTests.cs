using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [stepper] A whole Z axis in a world (the "z-axis" template): 12 V and 5 V supplies, a square wave or an Uno on STEP,
    /// an A4988, a NEMA 17, a coupler to a T8×8 screw in a 608, a brass nut on a carriage riding two rods on LM8UUs. Nothing
    /// is joined by hand except the carriage's screws: the motor's steps lift it 8 mm a turn.
    /// </summary>
    public class StepperWorldTests
    {
        private readonly ITestOutputHelper _out;

        public StepperWorldTests(ITestOutputHelper output) => _out = output;

        internal static WorldDocument ZAxis(double stepRate = 200, bool arduino = false) =>
            WorldExamples.ZAxis(RobotTwin.CoreSim.Design.PartLibrary.Default, arduino ? 0 : stepRate);

        [Fact]
        public void StepsFromAGeneratorTurnTheMotorAndTheScrewLiftsTheCarriage()
        {
            var w = ZAxis();
            var report = DriveAssembly.Assemble(w);
            foreach (var line in report.Links.Concat(report.Problems)) _out.WriteLine(line);
            Assert.Empty(report.Problems);
            Assert.Contains(report.Links, l => l.Contains("motor miline"));
            Assert.Equal(1, report.Couplings);   // the screw drives the nut
            using var runner = new WorldRunner(WorldDocument.FromJson(w.ToJson()), new RunnerOptions { Firmware = false });
            var plate = w.Objects.Single(o => o.Name == "Taşıyıcı");
            double z0 = runner.Physics!.Scene.Bodies.Single(b => b.Id == plate.Id).Position.Z;
            while (runner.Time < 1.0 - 1e-6) runner.Step(0.01);
            double rise = runner.Physics.Scene.Bodies.Single(b => b.Id == plate.Id).Position.Z - z0;
            var stepper = runner.Circuit!.Compiled.Circuit.Devices.OfType<RobotTwin.CoreSim.Analog.Devices.StepperMotor>().Single();
            var rotor = runner.Physics.Scene.Joints.Single(j => j.Id.EndsWith(":shaft", StringComparison.Ordinal));
            _out.WriteLine($"1 s: motor {stepper.FullSteps:0.0} tam adım ({stepper.Angle / (2 * Math.PI):0.000} tur), rotor {rotor.Angle / (2 * Math.PI):0.000} tur, taşıyıcı {rise * 1000:0.00} mm, motor torku {stepper.Torque * 1000:0.0} mN·m");
            Assert.InRange(Math.Abs(stepper.FullSteps), 190, 202);
            Assert.InRange(Math.Abs(rotor.Angle - stepper.Angle), 0, 0.05);   // the shaft is where the electrical rotor is
            Assert.InRange(Math.Abs(rise), 0.0075, 0.0082);   // 8 mm a turn
        }

        [Fact]
        public void AnArduinoSketchRampsTheAxisUpFortyMillimetres()
        {
            string? dir = FirmwareDir();
            if (dir == null) return;   // repository layout not available
            var w = ZAxis(arduino: true);
            DriveAssembly.Assemble(w);
            var options = new RunnerOptions();
            options.FirmwareSearchPath.Add(dir);
            using var runner = new WorldRunner(WorldDocument.FromJson(w.ToJson()), options);
            Assert.Single(runner.Boards);
            var plate = w.Objects.Single(o => o.Name == "Taşıyıcı");
            double z0 = runner.Physics!.Scene.Bodies.Single(b => b.Id == plate.Id).Position.Z, top = 0;
            var stepper = runner.Circuit!.Compiled.Circuit.Devices.OfType<RobotTwin.CoreSim.Analog.Devices.StepperMotor>().Single();
            var driver = runner.Circuit!.Compiled.Circuit.Devices.OfType<RobotTwin.CoreSim.Analog.Devices.StepperDriver>().Single();
            while (runner.Time < 2.0 - 1e-6)
            {
                runner.Step(0.01);
                top = Math.Max(top, runner.Physics.Scene.Bodies.Single(b => b.Id == plate.Id).Position.Z - z0);
            }

            string serial = string.Concat(runner.ReadSerial(null, true).Values);
            _out.WriteLine($"seri: {serial.Replace("\r", "").Replace("\n", " | ")}");
            _out.WriteLine($"2 s: sürücü {driver.Steps} adım, motor {stepper.FullSteps:0.0} tam adım, taşıyıcı en çok {top * 1000:0.00} mm yükseldi");
            Assert.Contains("Yukari 40 mm", serial);
            Assert.InRange(Math.Abs(driver.Steps), 990, 1000);   // the sketch's 1000 steps up
            Assert.InRange(Math.Abs(Math.Abs(stepper.FullSteps) - Math.Abs(driver.Steps)), 0, 2);   // ramped, it lost none
            Assert.InRange(top, 0.0385, 0.0405);   // 40 mm up
        }

        private static string? FirmwareDir()
        {
            for (var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string candidate = System.IO.Path.Combine(d.FullName, "Studio", "RobotWin.Studio", "Firmware");
                if (System.IO.Directory.Exists(candidate)) return candidate;
            }

            return null;
        }
    }
}