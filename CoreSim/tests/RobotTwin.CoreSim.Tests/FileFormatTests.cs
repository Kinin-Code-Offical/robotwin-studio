using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    public class FileFormatTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "rw-fmt-" + Guid.NewGuid().ToString("N"));
        private static readonly PartLibrary Lib = PartLibrary.Default;

        public FileFormatTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (IOException)
            {
            }
        }

        private static string Read(ZipArchive zip, string name)
        {
            using var r = new StreamReader(zip.GetEntry(name)!.Open());
            return r.ReadToEnd();
        }

        [Fact]
        public void ProjectPackageRoundTripsEverySection()
        {
            var sch = VehicleTemplates.LineFollower(Lib);
            sch.Board = BoardDocument.Synchronize(sch, Lib);
            AutoPlacer.Place(sch.Board);
            var pkg = new ProjectPackage { Schematic = sch, Preview = new byte[] { 0x89, 0x50, 0x4E, 0x47 }, Settings = "{\"speed\":1}" };
            const string code = "void setup(){}\nvoid loop(){}\n";
            pkg.Firmware.Add(new FirmwareSource { BoardRefDes = "U9", Source = code, Hex = ":00000001FF\n", BuiltFromHash = FirmwareSource.HashOf(code) });
            pkg.Parts.Add(new CustomPart { Id = "my-motor", Name = "Benim motorum", BasePartId = "dc-motor", Parameters = { ["rpm"] = "150" } });
            string path = Path.Combine(_dir, "robot.rwproj");
            pkg.Save(path);

            using (var zip = ZipFile.OpenRead(path))
            {
                var names = zip.Entries.Select(e => e.FullName).ToList();
                foreach (var n in new[] { "manifest.json", "schematic.json", "pcb/board.json", "mechanics/assembly.json", "firmware/index.json", "firmware/U9/sketch.ino", "firmware/U9/sketch.hex", "library/parts.json", "preview.png", "settings.json" })
                {
                    Assert.Contains(n, names);
                }

                Assert.DoesNotContain("\"board\"", Read(zip, "schematic.json"));
                Assert.Contains("robotwin.project", Read(zip, "manifest.json"));
            }

            // The in-memory document keeps its board and assembly after saving.
            Assert.NotNull(sch.Board);
            Assert.NotNull(sch.Assembly);

            var back = ProjectPackage.Load(path);
            Assert.False(back.WasLegacy);
            Assert.Equal(sch.Parts.Count, back.Schematic.Parts.Count);
            Assert.Equal(sch.Labels.Count, back.Schematic.Labels.Count);
            Assert.NotNull(back.Schematic.Board);
            Assert.Equal(sch.Board.Components.Count, back.Schematic.Board!.Components.Count);
            Assert.Equal(VehicleKind.Rover, back.Schematic.Assembly!.Kind);
            Assert.Equal(2, back.Schematic.Assembly.Motors.Count);
            var fw = back.FirmwareFor("u9")!;
            Assert.Contains("void loop", fw.Source);
            Assert.True(fw.IsBuildCurrent);
            Assert.Equal("150", back.Parts.Single().Parameters["rpm"]);
            Assert.Equal(4, back.Preview!.Length);
            Assert.Equal("Rover", back.Info.Summary["vehicle"]);
            var (info, preview) = ProjectPackage.Peek(path);
            Assert.Equal(sch.Title, info.Title);
            Assert.NotNull(preview);
        }

        [Fact]
        public void LegacyJsonProjectsStillOpen()
        {
            var sch = new Schematic { Title = "Eski" };
            sch.AddPart(Lib, "res-220", new GridPoint(0, 0));
            string path = Path.Combine(_dir, "old.rwproj");
            sch.Save(path);
            var pkg = ProjectPackage.Load(path);
            Assert.True(pkg.WasLegacy);
            Assert.Single(pkg.Schematic.Parts);
            pkg.Save(path);
            Assert.False(ProjectPackage.Load(path).WasLegacy);
        }

        [Fact]
        public void CustomPartWithFootprintRegistersAndBundles()
        {
            var part = new CustomPart
            {
                Id = "user-res-4k7-test", Name = "Özel 4k7", BasePartId = "resistor",
                Parameters = { ["resistance"] = "4.7k" },
                Footprint = new CustomFootprint
                {
                    Id = "USER-AXIAL-TEST", CourtyardMinX = -6, CourtyardMinY = -1.5, CourtyardMaxX = 6, CourtyardMaxY = 1.5,
                    Pads = { new CustomPad { Name = "1", X = -5 }, new CustomPad { Name = "2", X = 5 } }
                }
            };
            string path = Path.Combine(_dir, "res.rwpart");
            part.Save(path);
            var loaded = CustomPart.Load(path);
            var lib = new PartLibrary();
            foreach (var p in PartLibrary.Default.Parts) lib.Add(p);
            Assert.True(loaded.Register(lib));
            Assert.False(loaded.Register(lib));
            Assert.Equal(2, FootprintLibrary.Get("USER-AXIAL-TEST")!.Pads.Count);
            var preset = lib.FindPreset("user-res-4k7-test")!;
            Assert.Equal("USER-AXIAL-TEST", preset.Parameters["footprint"]);
            Assert.Equal("4.7k", preset.Parameters["resistance"]);

            var bundle = new PartLibraryFile { Name = "Test", Parts = { loaded, new CustomPart { Id = "x2", Name = "X2", BasePartId = "capacitor" } } };
            string libPath = Path.Combine(_dir, "my.rwlib");
            bundle.Save(libPath);
            var back = PartLibraryFile.Load(libPath);
            Assert.Equal(2, back.Parts.Count);
            Assert.Equal("Test", back.Name);
            Assert.Equal("USER-AXIAL-TEST", back.Parts.Single(p => p.Id == "user-res-4k7-test").Footprint!.Id);
        }

        /// <summary>A divider board with a 3-pin header (VIN, OUT, GND) packaged as a module and used in another design.</summary>
        [Fact]
        public void OwnBoardBecomesAModulePartInAnotherDesign()
        {
            var inner = new Schematic { Title = "Bölücü kartı" };
            var r1 = inner.AddPart(Lib, "res-10000", new GridPoint(0, 0));
            var r2 = inner.AddPart(Lib, "res-10000", new GridPoint(10, 0));
            var j = inner.AddPart(Lib, "hdr-1x3", new GridPoint(-10, 0));
            VehicleTemplates.Tie(inner, Lib, j, "1", "VIN");
            VehicleTemplates.Tie(inner, Lib, j, "2", "OUT");
            VehicleTemplates.Tie(inner, Lib, j, "3", "GND");
            VehicleTemplates.Tie(inner, Lib, r1, "A", "VIN");
            VehicleTemplates.Tie(inner, Lib, r1, "B", "OUT");
            VehicleTemplates.Tie(inner, Lib, r2, "A", "OUT");
            VehicleTemplates.Tie(inner, Lib, r2, "B", "GND");
            inner.Board = BoardDocument.Synchronize(inner, Lib);
            inner.Board.Width = 30;
            inner.Board.Height = 20;
            AutoPlacer.Place(inner.Board);

            var part = CustomPart.FromDesign(inner, Lib, "user-divider-test", "Bölücü modülü");
            Assert.Equal(new[] { "VIN", "OUT", "GND" }, part.Module!.Ports.Select(p => p.Net).ToArray());
            Assert.All(part.Module.Ports, p => Assert.NotNull(p.PadX));
            string path = Path.Combine(_dir, "divider.rwpart");
            part.Save(path);
            var loaded = CustomPart.Load(path);
            loaded.Register(Lib);
            var fp = FootprintLibrary.Get(ModuleBuilder.FootprintId("user-divider-test"))!;
            Assert.Equal(3, fp.Pads.Count);
            Assert.True(fp.CourtyardMaxX - fp.CourtyardMinX >= 30 - 1e-9);

            // Outer design: 10 V source → module VIN, read OUT.
            var outer = new Schematic { Title = "Dış devre" };
            var src = outer.AddPart(Lib, "dc-source", new GridPoint(0, 0));
            src.Parameters["voltage"] = "10";
            var m = outer.AddPart(Lib, "user-divider-test", new GridPoint(20, 0));
            Assert.StartsWith("M", m.RefDes);
            var srcDef = Lib.Get(src.PartId);
            VehicleTemplates.Tie(outer, Lib, src, srcDef.Pins[0].Name, "V10");
            VehicleTemplates.Tie(outer, Lib, src, srcDef.Pins[1].Name, "GND");
            VehicleTemplates.Tie(outer, Lib, m, "VIN", "V10");
            VehicleTemplates.Tie(outer, Lib, m, "GND", "GND");
            VehicleTemplates.Tie(outer, Lib, m, "OUT", "MID");
            var compiled = CircuitCompiler.Compile(outer, Lib);
            Assert.DoesNotContain(compiled.Warnings, w => w.Contains("modellenemedi"));
            var sim = new RobotTwin.CoreSim.Analog.AnalogSimulator(compiled.Circuit, new RobotTwin.CoreSim.Analog.AnalogOptions());
            sim.Initialize(RobotTwin.CoreSim.Analog.InitialState.OperatingPoint);
            Assert.InRange(sim.NodeVoltage(compiled.NodeByNet["MID"]), 4.99, 5.01);
            Assert.Equal(2, compiled.DevicesOf(m.RefDes).Count);
            Assert.Contains(compiled.DevicesOf(m.RefDes), d => d.Id == m.RefDes + "/R1");

            // The outer design gets a PCB with the module's board footprint.
            var board = BoardDocument.Synchronize(outer, Lib);
            Assert.Contains(board.Components, c => c.FootprintId == ModuleBuilder.FootprintId("user-divider-test"));
        }

        [Fact]
        public void RecordingRoundTripsWithEmbeddedProject()
        {
            var rec = new SimulationRecording { Title = "Koşu", Duration = 2.5, Serial = "merhaba\n" };
            rec.Channels.Add(new RecordedChannel { Name = "V(OUT)", Unit = "V", Time = { 0, 0.001, 0.002 }, Value = { 0, 2.5, 5 } });
            rec.Events.Add(new RecordedEvent { Time = 1.2, Device = "R1", Message = "yandı", Kind = "Failure" });
            rec.Trajectory.Add(new PoseSample { T = 0.5, X = 1, Y = 2, Speed = 0.3 });
            rec.Project = new ProjectPackage { Schematic = VehicleTemplates.MicroQuadcopter(Lib) };
            string path = Path.Combine(_dir, "run.rwsim");
            rec.Save(path);
            var back = SimulationRecording.Load(path);
            Assert.Equal(2.5, back.Duration);
            Assert.Equal(new[] { 0, 2.5, 5 }, back.Channels.Single().Value);
            Assert.Equal("V(OUT)", back.Channels[0].Name);
            Assert.Equal("yandı", back.Events.Single().Message);
            Assert.Equal(0.3, back.Trajectory.Single().Speed);
            Assert.Equal("merhaba\n", back.Serial);
            Assert.Equal(VehicleKind.Quadcopter, back.Project!.Schematic.Assembly!.Kind);
        }

        [Fact]
        public void WrongFormatIsRejectedWithAClearMessage()
        {
            var part = new CustomPart { Id = "p", Name = "P", BasePartId = "resistor" };
            string path = Path.Combine(_dir, "p.rwpart");
            part.Save(path);
            var ex = Assert.Throws<InvalidDataException>(() => ProjectPackage.Load(path));
            Assert.Contains("robotwin.project", ex.Message);
            Assert.Equal(".rwpart", RobotWinFormats.ForPath(path)!.Extension);
        }
    }
}
