using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    public class RobotCellPhysicsTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public RobotCellPhysicsTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static WorldBodyGeometry Geometry(WorldObject o)
        {
            var cell = CellGeometry.For(o);
            if (cell != null) return cell;
            return new WorldBodyGeometry { Min = new Vec3(-o.SizeX / 2, -o.SizeY / 2, 0), Max = new Vec3(o.SizeX / 2, o.SizeY / 2, o.SizeZ), MassKg = o.MassKg ?? 0.1 };
        }

        private static void Run(WorldPhysics p, double seconds, Func<bool>? until = null)
        {
            for (int i = 0; i < (int)(seconds / 1e-3); i++)
            {
                p.Advance(1e-3, null);
                if (until != null && until()) return;
            }
        }

        private static WorldObject AddArm(WorldDocument w, string preset = "arm-rw6-850", double z = 0)
        {
            var arm = w.AddComponent(Lib, preset, new WorldPose { Z = z });
            arm.IsStatic = true;
            return arm;
        }

        [Fact]
        public void ArmFollowsJointAndLinearMovesInPhysics()
        {
            var w = new WorldDocument();
            var armObj = AddArm(w);
            var p = WorldPhysics.Build(w, Geometry, null);
            var arm = p.ArmOf(armObj.Id)!;
            var c = arm.Controller;
            var target = new[] { 0.8, 0.4, 0.3, -0.5, 1.0, 0.7 };
            Assert.True(c.MoveJ(target, 1.0));
            Run(p, 4, () => c.State == ArmState.Idle);
            Run(p, 0.2);
            var q = arm.Measured;
            double worst = q.Zip(target, (a, b) => Math.Abs(a - b)).Max();
            _out.WriteLine($"MoveJ settled, worst joint error {worst * 180 / Math.PI:0.000}°");
            Assert.True(worst < 0.2 * Math.PI / 180, $"joint error {worst * 180 / Math.PI:0.###}°");

            // The physical flange (link body) agrees with forward kinematics of the measured joints.
            var frames = arm.LinkFrames();
            var flangeBody = frames[5] * arm.Model.Flange;
            var flangeFk = arm.BasePose * ArmKinematics.Forward(arm.Model, q);
            _out.WriteLine($"flange body vs FK: {(flangeBody.P - flangeFk.P).Length * 1000:0.000} mm");
            Assert.True((flangeBody.P - flangeFk.P).Length < 0.001);

            var tcp = c.TcpReference;
            var goal = new Transform3(tcp.P + new Vec3(-0.1, 0.15, -0.12), tcp.Q);
            Assert.True(c.MoveL(goal, 0.4));
            Run(p, 4, () => c.State == ArmState.Idle);
            Run(p, 0.2);
            var reached = arm.TcpWorld;
            _out.WriteLine($"MoveL error {(reached.P - goal.P).Length * 1000:0.000} mm, state {c.State}");
            Assert.True((reached.P - goal.P).Length < 0.0015);
            Assert.NotEqual(ArmState.Fault, c.State);
        }

        [Fact]
        public void JointLimitsAreEnforced()
        {
            var w = new WorldDocument();
            var armObj = AddArm(w);
            var p = WorldPhysics.Build(w, Geometry, null);
            var arm = p.ArmOf(armObj.Id)!;
            var spec = arm.Model.Joints[1];
            Assert.Equal(spec.Max - arm.Model.Home[1], arm.Joints[1].Upper!.Value, 9);
            Assert.Equal(spec.Min - arm.Model.Home[1], arm.Joints[1].Lower!.Value, 9);
            var beyond = arm.Model.Home.ToArray();
            beyond[1] = spec.Max + 0.2;
            Assert.False(arm.Controller.MoveJ(beyond));
            // Jogging clamps at the limit.
            for (int i = 0; i < 40; i++)
            {
                arm.Controller.JogJoint(2, -0.2);
                Run(p, 2, () => arm.Controller.State == ArmState.Idle);
            }

            Assert.InRange(arm.Controller.Reference[2], arm.Model.Joints[2].Min - 1e-9, arm.Model.Joints[2].Min + 1e-6);
        }

        /// <summary>A payload welded to the flange while the arm is stretched out horizontally.</summary>
        private (WorldPhysics P, MachineArm Arm) Stretched(double payloadKg)
        {
            var w = new WorldDocument();
            var armObj = AddArm(w);
            armObj.Parameters["q0"] = "0, 85, -85, 0, 0, 0";
            var flange = CellGeometry.FlangeWorld(armObj);
            // 10 cm cube centred 6 cm beyond the flange.
            var c = flange.Apply(new Vec3(0, 0, 0.06));
            var load = w.AddBody("box", 0.1, 0.1, 0.1, new WorldPose { X = c.X, Y = c.Y, Z = c.Z - 0.05 }, "ÇELİK");
            load.MassKg = payloadKg;
            w.Joints.Add(new WorldJoint { Id = "J1", ObjectA = load.Id, ObjectB = armObj.Id, PortB = "flange" });
            var p = WorldPhysics.Build(w, Geometry, null);
            return (p, p.ArmOf(armObj.Id)!);
        }

        [Fact]
        public void RatedPayloadIsHeldButOverloadTripsAFollowingError()
        {
            var (p1, a1) = Stretched(5.0);
            Run(p1, 1.5);
            double sag = (a1.Measured[1] - a1.Controller.Reference[1]) * 180 / Math.PI;
            _out.WriteLine($"5 kg: state {a1.Controller.State}, J2 load {a1.Controller.JointLoad[1]:P0}, J5 load {a1.Controller.JointLoad[4]:P0}, J2 error {sag:0.000}°");
            Assert.Equal(ArmState.Idle, a1.Controller.State);
            Assert.InRange(a1.Controller.JointLoad[1], 0.15, 0.95);

            var (p2, a2) = Stretched(40.0);
            Run(p2, 1.5);
            _out.WriteLine($"40 kg: state {a2.Controller.State} — {a2.Controller.FaultText}");
            Assert.Equal(ArmState.Fault, a2.Controller.State);
            Assert.Contains("tork", a2.Controller.FaultText);
        }

        /// <summary>Arm + tool, a static table and a box on it; returns the box after a pick-and-lift program.</summary>
        private (double Lift, MachineArm Arm, string Log) PickAndLift(string toolPreset, double boxMass, Action<WorldObject>? tweakTool = null, double boxX = 0.05, double boxY = 0.04, double boxZ = 0.08)
        {
            var w = new WorldDocument();
            var armObj = AddArm(w);
            var tool = w.AddComponent(Lib, toolPreset, new WorldPose());
            tweakTool?.Invoke(tool);
            w.Joints.Add(new WorldJoint { Id = "J1", ObjectA = tool.Id, ObjectB = armObj.Id, PortB = "flange" });
            CellGeometry.SeatTools(w, armObj);
            var table = w.AddBody("box", 0.4, 0.4, 0.3, new WorldPose { X = 0.6 }, "AHŞAP");
            table.IsStatic = true;
            var box = w.AddBody("box", boxX, boxY, boxZ, new WorldPose { X = 0.6, Z = 0.3 }, "AHŞAP");
            box.MassKg = boxMass;
            bool vacuum = toolPreset.StartsWith("vacuum", StringComparison.Ordinal);
            double top = 0.3 + boxZ;
            double grasp = vacuum ? top - 0.005 : top - 0.025;
            armObj.Parameters["points"] = $"UST = C(600, 0, {(top + 0.15) * 1000:0.#}, 180, 0, 0)\nAL = C(600, 0, {grasp * 1000:0.#}, 180, 0, 0)";
            armObj.Parameters["program"] = "MOVEJ UST HIZ=80\nMOVEL AL HIZ=200\nKAVRA\nTUTUSBEKLE 1.5\nBEKLE 0.2\nMOVEL UST HIZ=400\nBEKLE 0.5";
            armObj.Parameters["loop"] = "false";
            var p = WorldPhysics.Build(w, Geometry, null);
            var arm = p.ArmOf(armObj.Id)!;
            double z0 = p.PoseOf(box.Id)!.Value.Position.Z;
            Assert.True(arm.Controller.Start());
            var log = new System.Text.StringBuilder();
            for (int i = 0; i < 12000 && arm.Controller.State == ArmState.Running; i++)
            {
                p.Advance(1e-3, null);
                foreach (var m in p.DrainMachineMessages()) log.AppendLine($"{m.Time:0.00} {m.Text}");
            }

            foreach (var m in p.DrainMachineMessages()) log.AppendLine($"{m.Time:0.00} {m.Text}");
            double z1 = p.PoseOf(box.Id)!.Value.Position.Z;
            _out.WriteLine($"{toolPreset} {boxMass} kg: lifted {(z1 - z0) * 1000:0.0} mm, state {arm.Controller.State} {arm.Controller.FaultText}");
            _out.WriteLine(log.ToString());
            return (z1 - z0, arm, log.ToString());
        }

        [Fact]
        public void GripperHoldsALightPartByFriction()
        {
            var (lift, arm, _) = PickAndLift("gripper-2f-60", 0.5);
            Assert.True(lift > 0.12, $"lifted only {lift * 1000:0} mm");
            Assert.NotEqual(ArmState.Fault, arm.Controller.State);
        }

        [Fact]
        public void HeavyPartSlipsOutOfAWeakGrip()
        {
            // 10 N per finger, rubber on wood (μ ≈ 0.6–0.75): at most ~15 N of friction for a 3 kg (29 N) part.
            var (lift, _, _) = PickAndLift("gripper-2f-60", 3.0, t => t.Parameters["force"] = "10");
            Assert.True(lift < 0.02, $"should slip, lifted {lift * 1000:0} mm");
        }

        [Fact]
        public void VacuumCupHoldsAndLosesAnOverweightPart()
        {
            var (lift, arm, _) = PickAndLift("vacuum-cup-50", 1.0, null, 0.16, 0.12, 0.1);
            Assert.True(lift > 0.12, $"lifted only {lift * 1000:0} mm");
            Assert.NotEqual(ArmState.Fault, arm.Controller.State);
            Assert.True(arm.Tool!.HoldForce > 80, $"hold force {arm.Tool.HoldForce:0} N");

            // Ø50 at −65 kPa holds ~100 N: a 20 kg part is too heavy.
            var (lift2, _, log) = PickAndLift("vacuum-cup-50", 20.0, null, 0.16, 0.12, 0.1);
            Assert.True(lift2 < 0.02, $"should drop, lifted {lift2 * 1000:0} mm");
            Assert.Contains("Vakum tutuşu kaçtı", log);
        }

        [Fact]
        public void ConveyorCarriesAPartToTheStopAndTheSensorSeesIt()
        {
            var w = new WorldDocument();
            var cvObj = w.AddComponent(Lib, "conveyor-1600", new WorldPose());
            cvObj.IsStatic = true;
            var box = w.AddBody("box", 0.16, 0.12, 0.1, new WorldPose { X = -0.6, Z = 0.6 }, "AHŞAP");
            box.MassKg = 0.8;
            var p = WorldPhysics.Build(w, Geometry, null);
            var cv = p.ConveyorOf(cvObj.Id)!;
            Run(p, 0.5);
            double x0 = p.PoseOf(box.Id)!.Value.Position.X;
            Run(p, 2.0);
            double x1 = p.PoseOf(box.Id)!.Value.Position.X;
            double v = (x1 - x0) / 2.0;
            _out.WriteLine($"box speed {v:0.000} m/s (belt 0.20)");
            Assert.InRange(v, 0.18, 0.205);
            Assert.False(cv.SensorOn);
            Run(p, 6);
            var pose = p.PoseOf(box.Id)!.Value;
            _out.WriteLine($"box rests at x = {pose.Position.X:0.000}, z = {pose.Position.Z:0.000}, sensor {cv.SensorOn}");
            Assert.InRange(pose.Position.X + 0.08, 0.77, 0.785);   // front face against the end stop at 0.78
            Assert.InRange(pose.Position.Z, 0.595, 0.605);
            Assert.True(cv.SensorOn);
        }

        [Fact]
        public void ConveyorFeederSpawnsBoxesThatQueueUp()
        {
            var w = new WorldDocument();
            var cvObj = w.AddComponent(Lib, "conveyor-feed", new WorldPose());
            cvObj.IsStatic = true;
            cvObj.Parameters["feed_period"] = "2";
            cvObj.Parameters["speed"] = "0.4";
            var p = WorldPhysics.Build(w, Geometry, null);
            Run(p, 12);
            var boxes = p.SpawnedParts.Select(s => s.Body.Position).OrderByDescending(b => b.X).ToList();
            _out.WriteLine(string.Join(" ", boxes.Select(b => $"{b.X:0.00}")));
            Assert.InRange(boxes.Count, 4, 7);
            // Queued against the stop, one behind the other, none fell off.
            Assert.InRange(boxes[0].X, 0.69, 0.705);
            Assert.InRange(boxes[1].X, boxes[0].X - 0.17, boxes[0].X - 0.155);
            Assert.All(boxes, b => Assert.InRange(b.Z, 0.64, 0.66));   // centres: belt 0.60 + half a box
        }

        [Fact]
        public void LightCurtainStopsTheArm()
        {
            var w = new WorldDocument();
            var armObj = AddArm(w);
            armObj.Parameters["points"] = "A = J(90, 0, 0, 0, 45, 0)\nB = J(-90, 0, 0, 0, 45, 0)";
            armObj.Parameters["program"] = "MOVEJ A\nMOVEJ B";
            armObj.Parameters["autostart"] = "true";
            var lc = w.AddComponent(Lib, "light-curtain-900", new WorldPose { X = 1.5 });
            lc.IsStatic = true;
            var p = WorldPhysics.Build(w, Geometry, null);
            var arm = p.ArmOf(armObj.Id)!;
            Run(p, 0.5);
            Assert.Equal(ArmState.Running, arm.Controller.State);
            p.InterruptCurtain(lc.Id, 1.0);
            Run(p, 0.6);
            Assert.True(p.CurtainOf(lc.Id)!.Blocked);
            Assert.Equal(ArmState.SafetyStop, arm.Controller.State);
            double held = arm.Measured[0];
            Run(p, 0.3);
            Assert.True(Math.Abs(arm.Measured[0] - held) < 0.002, "the arm must stand still");
            Run(p, 1.0);
            Assert.False(p.CurtainOf(lc.Id)!.Blocked);
            Assert.Equal(ArmState.SafetyStop, arm.Controller.State);
            arm.Controller.Resume();
            Run(p, 0.3);
            Assert.Equal(ArmState.Running, arm.Controller.State);
        }

        [Fact]
        public void StartInputWiredToAPhotoEyeStartsTheProgram()
        {
            // Conveyor photo-eye → arm START (rising edge), common 0 V: the program starts when a part arrives,
            // and BUSY goes high on the arm's output pin.
            var w = new WorldDocument();
            var armObj = AddArm(w);
            armObj.Parameters["points"] = "A = J(30, 0, 0, 0, 45, 0)";
            armObj.Parameters["program"] = "MOVEJ A\nCIKIS 2 1";
            armObj.Parameters["loop"] = "false";
            var cvObj = w.AddComponent(Lib, "conveyor-1600", new WorldPose { Y = 1.0 });
            cvObj.IsStatic = true;
            var box = w.AddBody("box", 0.16, 0.12, 0.1, new WorldPose { X = 0.3, Y = 1.0, Z = 0.6 }, "AHŞAP");
            box.MassKg = 0.5;
            w.Connect(new CableEnd { ObjectId = cvObj.Id, Pin = "SENSOR" }, new CableEnd { ObjectId = armObj.Id, Pin = "START" });
            w.Connect(new CableEnd { ObjectId = cvObj.Id, Pin = "GND" }, new CableEnd { ObjectId = armObj.Id, Pin = "GND" });
            var wc = WorldCompiler.Compile(w, Lib, c => 1.0);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var p = WorldPhysics.Build(w, Geometry, wc);
            var arm = p.ArmOf(armObj.Id)!;
            double busyMax = 0, startedAt = double.NaN;
            void Advance(double s)
            {
                for (int i = 0; i < (int)(s / 1e-3); i++)
                {
                    sim.Step(1e-3);
                    p.Advance(1e-3, sim);
                    busyMax = Math.Max(busyMax, wc.Compiled.PinVoltage(sim, wc.ComponentRef(armObj.Id)!, "BUSY"));
                    if (double.IsNaN(startedAt) && arm.Controller.State == ArmState.Running) startedAt = p.Scene.Time;
                }
            }

            Advance(1.2);
            Assert.Equal(ArmState.Idle, arm.Controller.State);
            Assert.False(arm.Controller.DigitalOut[1]);
            Assert.True(busyMax < 0.5);
            Advance(3.0);   // the box reaches the photo-eye (0.36 m at 0.2 m/s), the program runs and ends
            _out.WriteLine($"started at {startedAt:0.000} s, BUSY peak {busyMax:0.00} V");
            Assert.True(p.ConveyorOf(cvObj.Id)!.SensorOn);
            Assert.InRange(startedAt, 1.6, 2.2);
            Assert.InRange(busyMax, 4.5, 5.1);
            Assert.Equal(ArmState.Idle, arm.Controller.State);
            Assert.True(arm.Controller.DigitalOut[1]);
            Assert.InRange(wc.Compiled.PinVoltage(sim, wc.ComponentRef(armObj.Id)!, "DO2"), 4.5, 5.1);
        }

        [Fact]
        public void ScaraTemplateMovesTheBlocksWithItsGripper()
        {
            var w = FactoryCell.Scara();
            var p = WorldPhysics.Build(w, Geometry, null);
            var arm = p.ArmOf(w.Objects.First(o => o.PartId == CellParts.ArmId).Id)!;
            var blocks = w.Objects.Where(o => o.Name.StartsWith("Blok", StringComparison.Ordinal)).ToList();
            var log = new System.Text.StringBuilder();
            int mostInB = 0;
            for (int i = 0; i < 32000 && arm.Controller.State != ArmState.Fault && arm.Controller.CycleCount == 0; i++)
            {
                p.Advance(1e-3, null);
                foreach (var m in p.DrainMachineMessages()) log.AppendLine($"{m.Time:0.00} {m.Text}");
                if (i % 100 == 0)
                {
                    int inB = blocks.Count(b => p.PoseOf(b.Id)!.Value.Position.Y > 0.15 && p.PoseOf(b.Id)!.Value.Position.Z > 0.74);
                    mostInB = Math.Max(mostInB, inB);
                }
            }

            _out.WriteLine(log.ToString());
            _out.WriteLine($"t = {p.Scene.Time:0.0} s, state {arm.Controller.State} {arm.Controller.FaultText}, cycles {arm.Controller.CycleCount}, most blocks in row B {mostInB}");
            foreach (var b in blocks) _out.WriteLine($"{b.Name}: {p.PoseOf(b.Id)!.Value.Position}");
            Assert.NotEqual(ArmState.Fault, arm.Controller.State);
            Assert.Equal(4, mostInB);
            // After the full cycle the blocks are back in row A, upright, near their slots.
            Assert.All(blocks, b => Assert.InRange(p.PoseOf(b.Id)!.Value.Position.Y, -0.17, -0.13));
        }

        [Fact]
        public void FactoryCellTemplateStacksBoxesOnThePallet()
        {
            var w = FactoryCell.Build();
            var wc = WorldCompiler.Compile(w, Lib, c => 1.0);
            Assert.DoesNotContain(wc.Compiled.Warnings, x => x.Contains("modellenemedi"));
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var p = WorldPhysics.Build(w, Geometry, wc);
            var arm = p.ArmOf(w.Objects.First(o => o.PartId == CellParts.ArmId).Id)!;
            var log = new System.Text.StringBuilder();
            int cycles = 0;
            for (int i = 0; i < 45000; i++)
            {
                sim.Step(1e-3);
                p.Advance(1e-3, sim);
                foreach (var m in p.DrainMachineMessages()) log.AppendLine($"{m.Time:0.00} {m.ObjectId} {m.Text}");
                if (arm.Controller.State == ArmState.Fault) break;
                cycles = arm.Controller.CycleCount;
            }

            _out.WriteLine(log.ToString());
            var onPallet = p.SpawnedParts.Count(s => Math.Abs(s.Body.Position.Y - FactoryCell.PalletY) < 0.35 && Math.Abs(s.Body.Position.X - FactoryCell.PalletX) < 0.45 && s.Body.Position.Z < 0.45);
            _out.WriteLine($"state {arm.Controller.State} {arm.Controller.FaultText}; cycles {cycles}; pallet index {arm.Controller.PalletIndex}; boxes on pallet {onPallet}; spawned {p.SpawnedParts.Count}");
            foreach (var s in p.SpawnedParts) _out.WriteLine($"  {s.Id} at {s.Body.Position}");
            Assert.NotEqual(ArmState.Fault, arm.Controller.State);
            Assert.True(onPallet >= 3, $"only {onPallet} boxes on the pallet");
        }
    }
}
