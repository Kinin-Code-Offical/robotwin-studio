using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Pcb;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [assembly] A project's circuit becomes its Montaj: real parts, cabled pin to pin; a rover on its own chassis in its
    /// arena; several projects, each with or without its PCB; and the Montaj follows the project.
    /// </summary>
    public class ProjectAssemblyTests
    {
        private readonly ITestOutputHelper _out;

        public ProjectAssemblyTests(ITestOutputHelper output) => _out = output;

        private static PartLibrary Lib => PartLibrary.Default;

        private static string? FirmwareDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "Studio", "RobotWin.Studio", "Firmware");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        private static int Physical(Schematic sch) => ProjectAssembly.PhysicalParts(sch, Lib).Count;

        /// <summary>Pairs of solid objects whose bounds overlap by more than a millimetre (they would be thrown apart).</summary>
        private static List<string> Overlaps(WorldDocument w, IEnumerable<WorldObject> objects)
        {
            var list = objects.ToList();
            var joined = new HashSet<(string, string)>(w.Joints.Where(j => j.ObjectB != null).SelectMany(j => new[] { (j.ObjectA, j.ObjectB!), (j.ObjectB!, j.ObjectA) }));
            var found = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                for (int k = i + 1; k < list.Count; k++)
                {
                    var a = ProjectAssembly.Extent(w, Lib, new[] { list[i] })!.Value;
                    var b = ProjectAssembly.Extent(w, Lib, new[] { list[k] })!.Value;
                    double ox = Math.Min(a.Max.X, b.Max.X) - Math.Max(a.Min.X, b.Min.X);
                    double oy = Math.Min(a.Max.Y, b.Max.Y) - Math.Max(a.Min.Y, b.Min.Y);
                    double oz = Math.Min(a.Max.Z, b.Max.Z) - Math.Max(a.Min.Z, b.Min.Z);
                    if (ox > 0.001 && oy > 0.001 && oz > 0.001 && !joined.Contains((list[i].Id, list[k].Id)))
                        found.Add($"{list[i].Name} ∩ {list[k].Name} ({ox * 1000:F0}×{oy * 1000:F0}×{oz * 1000:F0} mm)");
                }
            }

            return found;
        }

        [Fact]
        public void ACircuitWithoutAPcbBecomesLooseRealPartsWiredByItsNets()
        {
            var sch = VehicleTemplates.ObstacleAvoider(Lib);
            sch.Assembly = null;   // a circuit with no mechanics of its own
            var w = ProjectAssembly.Build(new ProjectPackage { Schematic = sch }, Lib);
            foreach (var o in w.Objects) _out.WriteLine($"{o.Name} ({o.PartId}) at {o.Pose.X * 1000:F0},{o.Pose.Y * 1000:F0} mm");
            _out.WriteLine($"{w.Cables.Count} cables");
            Assert.True(w.IsAssembly);
            Assert.Equal(sch.ProjectId, w.ProjectId);
            Assert.Equal(Physical(sch), w.Objects.Count);
            Assert.All(w.Objects, o => Assert.Equal(WorldObjectKind.Component, o.Kind));
            Assert.All(w.Objects, o => Assert.StartsWith(sch.ProjectId + "/", o.ProjectRef));
            Assert.True(w.Cables.Count > 0);
            Assert.All(w.Cables, c => Assert.True(c.Length >= 0.1 && c.ProjectNet != null));

            // Every cable lands on a real pin and the whole thing compiles as one circuit.
            var wc = WorldCompiler.Compile(w, Lib, c => c.Length);
            foreach (var warn in wc.Compiled.Warnings) _out.WriteLine("warning: " + warn);
            Assert.DoesNotContain(wc.Compiled.Warnings, x => x.Contains("pini bulamadı"));

            // The original nets survive: two pins on one net in the circuit are on one node in the world.
            var sim = new RobotTwin.CoreSim.Analog.AnalogSimulator(wc.Compiled.Circuit, new RobotTwin.CoreSim.Analog.AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(RobotTwin.CoreSim.Analog.InitialState.OperatingPoint);
            var pwr = sch.BuildNetlist(Lib).Nets.First(n => n.Name.Contains("BAT", StringComparison.OrdinalIgnoreCase) && n.Connections.Count >= 2);
            var pa = pwr.Connections[0];
            var pb = pwr.Connections[1];
            double va = wc.Compiled.PinVoltage(sim, wc.ComponentRef(w.Objects.First(o => o.Name.StartsWith(pa.RefDes + " ")).Id)!, pa.Pin);
            double vb = wc.Compiled.PinVoltage(sim, wc.ComponentRef(w.Objects.First(o => o.Name.StartsWith(pb.RefDes + " ")).Id)!, pb.Pin);
            _out.WriteLine($"net {pwr.Name}: {pa.RefDes}.{pa.Pin} {va:F3} V, {pb.RefDes}.{pb.Pin} {vb:F3} V");
            Assert.True(va > 3, "the battery powers the net");
            Assert.InRange(vb, va - 0.3, va + 0.3);   // the same net: only the cable's drop between them
        }

        [Fact]
        public void PartsOnThePcbStayOnTheBoardAndTheRestAreCabledToItsPins()
        {
            var sch = VehicleTemplates.ObstacleAvoider(Lib);
            sch.Assembly = null;
            // Say the controller and the drivers are soldered on the project's PCB; motors, battery and the sensor are not.
            var pcbParts = sch.Parts.Where(p => Lib.Find(p.PartId) is { } d && (d.Symbol == SymbolKind.Board || d.Id == "motor-driver")).Select(p => p.RefDes).ToList();
            Assert.NotEmpty(pcbParts);
            sch.Board ??= new BoardDocument();
            foreach (var r in pcbParts) sch.Board.Components.Add(new BoardComponent { RefDes = r });
            var w = ProjectAssembly.Build(new ProjectPackage { Schematic = sch }, Lib);
            var board = Assert.Single(w.Objects, o => o.Kind == WorldObjectKind.Board);
            var design = w.FindDesign(board.DesignId)!;
            Assert.All(design.Package.Schematic.Parts.Where(p => Lib.Find(p.PartId)?.Symbol != SymbolKind.Ground), p => Assert.Contains(p.RefDes, pcbParts));
            Assert.Equal(sch.ProjectId, design.Package.Schematic.ProjectId);
            int loose = w.Objects.Count(o => o.Kind == WorldObjectKind.Component);
            _out.WriteLine($"board with {design.Package.Schematic.Parts.Count} parts, {loose} loose parts, {w.Cables.Count} cables");
            Assert.True(loose > 0);
            Assert.Contains(w.Cables, c => c.A.ObjectId == board.Id || c.B.ObjectId == board.Id);
            var wc = WorldCompiler.Compile(w, Lib, c => c.Length);
            Assert.DoesNotContain(wc.Compiled.Warnings, x => x.Contains("pini bulamadı"));

            // The same project without its PCB: every part on its own, no board.
            var sch2 = VehicleTemplates.ObstacleAvoider(Lib);
            sch2.Assembly = null;
            sch2.Board = sch.Board;
            var w2 = ProjectAssembly.Build(new ProjectPackage { Schematic = sch2 }, Lib, null, AssemblyForm.LooseParts);
            Assert.DoesNotContain(w2.Objects, o => o.Kind == WorldObjectKind.Board);
            Assert.Equal(Physical(sch2), w2.Objects.Count);
            Assert.False(w2.AssemblySources.Single().UsedPcb);
        }

        [Fact]
        public void ARoverProjectIsPutTogetherOnItsChassisInItsArena()
        {
            var sch = VehicleTemplates.ObstacleAvoider(Lib);
            var w = ProjectAssembly.Build(new ProjectPackage { Schematic = sch }, Lib);
            foreach (var o in w.Objects) _out.WriteLine($"{o.Name} [{o.Kind}] at {o.Pose.X * 1000:F0},{o.Pose.Y * 1000:F0},{o.Pose.Z * 1000:F0} mm yaw {o.Pose.Yaw:F0} roll {o.Pose.Roll:F0}");
            Assert.Equal("Engelli alan", w.Environment.Name);
            var plate = Assert.Single(w.Objects, o => o.Kind == WorldObjectKind.Body && o.Shape == "plate");
            Assert.Equal(2, w.Objects.Count(o => o.Shape == "wheel"));
            Assert.Single(w.Objects, o => o.Shape == "sphere");
            Assert.Equal(Physical(sch), w.Objects.Count(o => o.Kind == WorldObjectKind.Component));

            // Every circuit part is fixed to the chassis; the wheels sit on the motor shafts.
            var onPlate = new HashSet<string>(w.Joints.Where(j => j.ObjectB == plate.Id).Select(j => j.ObjectA));
            Assert.All(w.Objects.Where(o => o.Kind == WorldObjectKind.Component), o => Assert.Contains(o.Id, onPlate));
            Assert.Equal(2, w.Joints.Count(j => j.PortB == "shaft"));

            // The right motor is mirrored on the axle: its leads are swapped (forward is forward), the left one's are not.
            var motors = w.Objects.Where(o => o.PresetId == "motor-tt").OrderByDescending(o => o.Pose.Y).ToList();
            Assert.False(motors[0].MirroredLeads);
            Assert.True(motors[1].MirroredLeads);

            // The sonar looks ahead; nothing starts inside anything else.
            var sonar = w.Objects.Single(o => o.PartId == "hc-sr04");
            var g = new ApproximateGeometry(w, Lib).GeometryOf(sonar);
            var look = sonar.Pose.Orientation.Rotate(g.SensorAxis);
            var forward = plate.Pose.Orientation.Rotate(Vec3.UnitX);
            Assert.True(look.Dot(forward) > 0.99, $"the sonar looks along {look.X:F2},{look.Y:F2}");
            var solids = w.Objects.Where(o => o.Shape != "wheel").ToList();
            var overlaps = Overlaps(w, solids.Where(o => o.Pose.Z >= plate.Pose.Z));   // on the deck
            foreach (var x in overlaps) _out.WriteLine("overlap: " + x);
            Assert.Empty(overlaps);
            var wc = WorldCompiler.Compile(w, Lib, c => c.Length);
            Assert.DoesNotContain(wc.Compiled.Warnings, x => x.Contains("pini bulamadı"));
        }

        [Fact]
        public void TheRoverDrivesForwardOnItsOwnCodeAndHoldsTogether()
        {
            var sch = VehicleTemplates.ObstacleAvoider(Lib);
            var w = ProjectAssembly.Build(new ProjectPackage { Schematic = sch }, Lib);
            w.Environment.Obstacles.Clear();   // open floor: it should just drive ahead
            w.Environment.Walls = false;
            var plate = w.Objects.Single(o => o.Shape == "plate");
            var wheel = w.Objects.First(o => o.Shape == "wheel");
            var options = new RunnerOptions();
            if (FirmwareDir() is { } dir) options.FirmwareSearchPath.Add(dir);
            using var runner = new WorldRunner(w, options);
            runner.Step(0.3);
            // The wheel's frame sits on its rim: measure its hub (half a diameter up the wheel's own z).
            Vec3 Hub(BodyState s) => s.Position + s.Orientation.Rotate(new Vec3(0, 0, wheel.SizeX / 2));
            Assert.True(runner.TryGetBody(plate.Id, out var start));
            Assert.True(runner.TryGetBody(wheel.Id, out var wheelStart));
            double gap0 = (Hub(wheelStart) - start.Position).Length;
            runner.Step(2.0);
            Assert.True(runner.TryGetBody(plate.Id, out var end));
            Assert.True(runner.TryGetBody(wheel.Id, out var wheelEnd));
            var heading = start.Orientation.Rotate(Vec3.UnitX);
            double ahead = (end.Position - start.Position).Dot(heading);
            double gap1 = (Hub(wheelEnd) - end.Position).Length;
            _out.WriteLine($"drove {ahead * 1000:F0} mm ahead, chassis z {start.Position.Z * 1000:F1} → {end.Position.Z * 1000:F1} mm, wheel offset {gap0 * 1000:F1} → {gap1 * 1000:F1} mm");
            Assert.True(ahead > 0.08, "the robot drives forward");
            Assert.InRange(end.Position.Z, start.Position.Z - 0.01, start.Position.Z + 0.01);   // on its wheels, not tipped or thrown
            Assert.InRange(gap1, gap0 - 0.003, gap0 + 0.003);   // the wheel stays on its shaft
        }

        [Fact]
        public void TheLineFollowerStartsOnItsTrackAndFollowsIt()
        {
            var sch = VehicleTemplates.LineFollower(Lib);
            var w = ProjectAssembly.Build(new ProjectPackage { Schematic = sch }, Lib);
            Assert.Equal("Oval pist", w.Environment.Name);
            var track = Arena.Create("oval").Lines[0];
            double Off(Vec3 p) => track.Min(t => Math.Sqrt((t.Item1 - p.X) * (t.Item1 - p.X) + (t.Item2 - p.Y) * (t.Item2 - p.Y)));
            var plate = w.Objects.Single(o => o.Shape == "plate");
            var lineSensors = w.Objects.Where(o => SensingParts.IsLineSensor(o.PartId)).ToList();
            Assert.Equal(2, lineSensors.Count);
            foreach (var s in lineSensors) _out.WriteLine($"{s.Name} at {s.Pose.X * 1000:F0},{s.Pose.Y * 1000:F0},{s.Pose.Z * 1000:F1} mm");
            Assert.All(lineSensors, s => Assert.InRange(s.Pose.Z, 0.002, 0.008));   // a few millimetres above the floor, as its mechanics meant
            Assert.True(Off(plate.Pose.Position) < 0.02, $"starts on the track ({Off(plate.Pose.Position) * 1000:F0} mm off)");

            var options = new RunnerOptions();
            if (FirmwareDir() is { } dir) options.FirmwareSearchPath.Add(dir);
            using var runner = new WorldRunner(w, options);
            double worst = 0, travelled = 0;
            Vec3? last = null;
            for (int i = 0; i < 16; i++)
            {
                runner.Step(0.5);
                Assert.True(runner.TryGetBody(plate.Id, out var st));
                double off = Off(st.Position);
                worst = Math.Max(worst, off);
                if (last != null) travelled += (st.Position - last.Value).Length;
                last = st.Position;
                _out.WriteLine($"t={0.5 * (i + 1):F1} s at {st.Position.X * 1000:F0},{st.Position.Y * 1000:F0} mm, {off * 1000:F0} mm off the line");
                if (i < 2)
                {
                    foreach (var o in lineSensors)
                    {
                        var rd = runner.Sensing?.Sensors.Get<RobotTwin.CoreSim.Sandbox.Sensing.LineSensorReading>(o.Id);
                        string cref = runner.Circuit!.ComponentRef(o.Id)!;
                        double vdo = runner.Circuit.Compiled.PinVoltage(runner.Simulator!, cref, "DO") - runner.Circuit.Compiled.PinVoltage(runner.Simulator!, cref, "GND");
                        double vcc = runner.Circuit.Compiled.PinVoltage(runner.Simulator!, cref, "VCC") - runner.Circuit.Compiled.PinVoltage(runner.Simulator!, cref, "GND");
                        var ch = rd != null && rd.Channels.Length > 0 ? rd.Channels[0] : default;
                        _out.WriteLine($"   {o.Name}: powered {rd?.Powered} VCC {vcc:F2} V DO {vdo:F2} V refl {ch.Reflectance:F2} signal {ch.Signal:F3} dist {ch.Distance * 1000:F1} mm photo {ch.Photocurrent * 1e6:F1} µA");
                    }

                    foreach (var o in w.Objects.Where(o => o.PresetId == "motor-tt" || o.PartId == "motor-driver" || Lib.Find(o.PartId ?? "")?.Id == "74hc04"))
                    {
                        string cref = runner.Circuit!.ComponentRef(o.Id)!;
                        var def = Lib.Find(o.PartId!)!;
                        _out.WriteLine("   " + o.Name + ": " + string.Join(", ", def.Pins.Select(pn => pn.Name + "=" + (runner.Circuit.Compiled.PinVoltage(runner.Simulator!, cref, pn.Name) - 0).ToString("F2"))));
                    }
                }
            }

            Assert.True(travelled > 0.8, $"it drives ({travelled:F2} m)");
            Assert.True(worst < 0.12, $"it follows the line (worst {worst * 1000:F0} mm off)");
        }

        [Fact]
        public void TheMontajFollowsItsProjectAndKeepsWhatWasBuilt()
        {
            var sch = VehicleTemplates.ObstacleAvoider(Lib);
            sch.Assembly = null;
            var package = new ProjectPackage { Schematic = sch };
            var uno = sch.Parts.Single(p => p.PartId == "arduino-uno");
            package.Firmware.Add(new FirmwareSource { BoardRefDes = uno.RefDes, FileName = "kod.ino", Source = "void setup(){}\nvoid loop(){}\n" });
            var w = ProjectAssembly.Build(package, Lib);
            var unoObj = w.Objects.Single(o => o.ProjectRef == sch.ProjectId + "/" + uno.RefDes);
            Assert.NotSame(package.Firmware[0], unoObj.Firmware);   // a copy, not the project's own object

            // What the user built in the Montaj: a part moved, a bracket of their own fixed to it, a cable of their own.
            unoObj.Pose.X = 0.5;
            var bracket = w.AddBody("box", 0.03, 0.03, 0.003, new WorldPose { X = 0.5, Z = 0.02 });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = bracket.Id, ObjectB = unoObj.Id });
            var mine = w.Connect(new CableEnd { ObjectId = unoObj.Id, Pin = "D13" }, new CableEnd { ObjectId = unoObj.Id, Pin = "GND2" });
            int cables = w.Cables.Count;

            // Nothing changed: nothing to do.
            var none = ProjectAssembly.Sync(w, package, Lib);
            Assert.False(none.Changed, none.Summary());
            Assert.Equal(cables, w.Cables.Count);

            // The project changes: new code, a new LED on D13, the sensor removed, the title renamed.
            package.Firmware[0].Source = "void setup(){ pinMode(13, OUTPUT); }\nvoid loop(){}\n";
            var led = sch.AddPart(Lib, "led3-red", new GridPoint(60, 0));
            VehicleTemplates.Tie(sch, Lib, led, "A", "TRIG");
            VehicleTemplates.Tie(sch, Lib, led, "K", "GND");
            var sonar = sch.Parts.Single(p => p.PartId == "hc-sr04");
            sch.Parts.Remove(sonar);
            sch.Title = "Yeni ad";
            var report = ProjectAssembly.Sync(w, package, Lib);
            _out.WriteLine(report.Summary());
            Assert.Equal(1, report.Added);
            Assert.Equal(1, report.Removed);
            Assert.True(report.Updated >= 1);
            Assert.Contains("void setup(){ pinMode(13", unoObj.Firmware!.Source);
            Assert.Contains(w.Objects, o => o.ProjectRef == sch.ProjectId + "/" + led.RefDes);
            Assert.DoesNotContain(w.Objects, o => o.ProjectRef == sch.ProjectId + "/" + sonar.RefDes);
            Assert.Equal(0.5, unoObj.Pose.X);                                            // where it was put
            Assert.Contains(w.Joints, j => j.ObjectA == bracket.Id && j.ObjectB == unoObj.Id);   // how it was joined
            Assert.Contains(mine, w.Cables);                                              // the user's own cable
            Assert.Contains(w.Cables, c => c.ProjectNet == sch.ProjectId + "/TRIG" && (c.A.ObjectId == w.Objects.Single(o => o.ProjectRef == sch.ProjectId + "/" + led.RefDes).Id || c.B.ObjectId == w.Objects.Single(o => o.ProjectRef == sch.ProjectId + "/" + led.RefDes).Id));
            Assert.Equal("Yeni ad", w.AssemblySources.Single().Title);
            var wc = WorldCompiler.Compile(w, Lib, c => c.Length);
            Assert.DoesNotContain(wc.Compiled.Warnings, x => x.Contains("pini bulamadı"));

            // Placing parts on the PCB later: they go onto one board, the separate ones leave.
            sch.Board ??= new BoardDocument();
            sch.Board.Components.Add(new BoardComponent { RefDes = uno.RefDes });
            var toPcb = ProjectAssembly.Sync(w, package, Lib);
            _out.WriteLine(toPcb.Summary());
            Assert.Single(w.Objects, o => o.Kind == WorldObjectKind.Board);
            Assert.DoesNotContain(w.Objects, o => o.ProjectRef == sch.ProjectId + "/" + uno.RefDes);
        }

        [Fact]
        public void TwoProjectsShareOneMontajSideBySideAndTheMontajTravelsWithItsProject()
        {
            var a = VehicleTemplates.ObstacleAvoider(Lib);
            a.Assembly = null;
            var b = VehicleTemplates.LineFollower(Lib);
            b.Assembly = null;
            var pa = new ProjectPackage { Schematic = a };
            var w = ProjectAssembly.Build(pa, Lib);
            int first = w.Objects.Count;
            var report = ProjectAssembly.Add(w, new ProjectPackage { Schematic = b }, Lib, AssemblyForm.LooseParts, @"C:\projeler\cizgi.rwproj");
            Assert.Equal(2, w.AssemblySources.Count);
            Assert.Equal(@"C:\projeler\cizgi.rwproj", w.SourceOf(b.ProjectId)!.Path);
            Assert.Null(w.SourceOf(a.ProjectId)!.Path);
            Assert.Equal(Physical(b), report.Added);
            Assert.Equal(first + Physical(b), w.Objects.Count);
            var ea = ProjectAssembly.Extent(w, Lib, w.Objects.Where(o => o.ProjectRef!.StartsWith(a.ProjectId)))!.Value;
            var eb = ProjectAssembly.Extent(w, Lib, w.Objects.Where(o => o.ProjectRef!.StartsWith(b.ProjectId)))!.Value;
            Assert.True(eb.Max.Y < ea.Min.Y, "the second project stands beside the first, not inside it");
            Assert.True(w.Cables.Where(c => c.ProjectNet!.StartsWith(b.ProjectId)).All(c =>
                w.Find(c.A.ObjectId)!.ProjectRef!.StartsWith(b.ProjectId) && w.Find(c.B.ObjectId)!.ProjectRef!.StartsWith(b.ProjectId)), "a project's cables stay within it");

            // Saved inside the project: the Montaj comes back as it was.
            pa.Montaj = w.ToBytes();
            var back = ProjectPackage.FromBytes(pa.ToBytes());
            var w2 = WorldDocument.FromBytes(back.Montaj!);
            Assert.True(w2.IsAssembly);
            Assert.Equal(w.Objects.Count, w2.Objects.Count);
            Assert.Equal(2, w2.AssemblySources.Count);
            Assert.Equal(a.ProjectId, back.Schematic.ProjectId);

            // A board copied into a world never carries its project's Montaj.
            var design = new WorldDocument().AddDesign(back);
            Assert.Null(design.Package.Montaj);

            // Removing a project takes its parts and cables, nothing else.
            int removed = ProjectAssembly.RemoveProject(w, b.ProjectId);
            Assert.Equal(Physical(b), removed);
            Assert.Equal(first, w.Objects.Count);
            Assert.DoesNotContain(w.Cables, c => c.ProjectNet!.StartsWith(b.ProjectId));
        }
    }
}
