using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[integrity] What a project needs that the computer lacks: parts, compilers, libraries.</summary>
    public class ProjectCheckTests
    {
        private const string Blink = "#include <Servo.h>\n#include <Wire.h>\n#include <stdint.h>\n#include <vector>\n#include <avr/io.h>\n#include \"config.h\"\n// #include <Commented.h>\n/* #include <Blocked.h>\n */\nvoid setup() {}\nvoid loop() {}\n";

        private static ProjectPackage Uno(string source = Blink, bool built = false)
        {
            var p = new ProjectPackage();
            p.Schematic.Parts.Add(new SchematicPart { RefDes = "U1", PartId = "arduino-uno" });
            var fw = new FirmwareSource { BoardRefDes = "U1", Target = "arduino:avr:uno", Source = source };
            fw.Files.Add(new FirmwareFile { Path = "config.h", Content = "#pragma once\n" });
            if (built)
            {
                fw.Hex = ":00000001FF\n";
                fw.BuiltFromHash = FirmwareSource.HashOf(fw.Bundle());
            }

            p.Firmware.Add(fw);
            return p;
        }

        private static ProjectEnvironment Env(bool arduino = true, bool msvc = true, bool esp32Toolchain = true) => new ProjectEnvironment
        {
            ArduinoCompiler = arduino,
            Esp32Compiler = msvc,
            Esp32Toolchain = esp32Toolchain,
            HeaderAvailable = (h, esp32) => h == "Wire.h",
            LibraryFor = (h, esp32) => h == "Servo.h" ? "Servo" : null
        };

        [Fact]
        public void TheIncludesAreTheLibrariesNotTheSketchsOwnFilesOrComments()
        {
            var fw = Uno().Firmware[0];
            Assert.Equal(new[] { "Servo.h", "Wire.h", "stdint.h", "vector", "avr/io.h" }, ProjectCheck.Includes(fw));
        }

        [Fact]
        public void AnUnknownPartIsAnErrorThatAsksForItsDefinition()
        {
            var p = new ProjectPackage();
            p.Schematic.Parts.Add(new SchematicPart { RefDes = "X1", PartId = "no-such-part" });
            p.Schematic.Parts.Add(new SchematicPart { RefDes = "R1", PartId = "resistor" });
            var issue = Assert.Single(ProjectCheck.Check(p, Env()));
            Assert.Equal(IssueLevel.Error, issue.Level);
            Assert.Equal(ProjectCheck.PartsArea, issue.Area);
            Assert.Equal(IssueFix.Part, issue.Fix);
            Assert.Equal("X1", issue.Subject);
            Assert.Equal("no-such-part", issue.Argument);
        }

        [Fact]
        public void AnUnoWithoutACompilerRunsItsLastBuildButCannotBuildChangedCode()
        {
            // never built: it cannot run here
            var needed = ProjectCheck.Check(Uno(), Env(arduino: false));
            var compiler = Assert.Single(needed);
            Assert.Equal(IssueLevel.Error, compiler.Level);
            Assert.Equal(IssueFix.ArduinoCompiler, compiler.Fix);
            Assert.StartsWith("U1 (", compiler.Subject);
            // its build is the code's: it runs, and only changing it needs the compiler (libraries are not looked up
            // without the compiler, whose core has the headers)
            var current = Assert.Single(ProjectCheck.Check(Uno(built: true), Env(arduino: false)));
            Assert.Equal(IssueLevel.Note, current.Level);
            Assert.Equal(IssueFix.ArduinoCompiler, current.Fix);
        }

        [Fact]
        public void AMissingLibraryIsNamedSoItCanBeInstalled()
        {
            var issue = Assert.Single(ProjectCheck.Check(Uno(), Env()));
            Assert.Equal(ProjectCheck.LibrariesArea, issue.Area);
            Assert.Equal(IssueLevel.Error, issue.Level);   // the code needs building
            Assert.Equal(IssueFix.Library, issue.Fix);
            Assert.Equal("Servo", issue.Argument);
            Assert.Contains("<Servo.h>", issue.Text);
            // with a current build it only matters for building again
            Assert.Equal(IssueLevel.Note, Assert.Single(ProjectCheck.Check(Uno(built: true), Env())).Level);
            // a header no library is known for: no fix, a hint
            var odd = ProjectCheck.Check(Uno("#include <Mystery.h>\n"), Env()).Single();
            Assert.Equal(IssueFix.None, odd.Fix);
            Assert.Null(odd.Argument);
        }

        [Fact]
        public void AnEsp32NeedsMsvcAndItsHardwareModeTheToolchain()
        {
            var p = new ProjectPackage();
            var board = new SchematicPart { RefDes = "U2", PartId = Esp32Parts.DevKitCId };
            p.Schematic.Parts.Add(board);
            p.Firmware.Add(new FirmwareSource { BoardRefDes = "U2", Target = ProjectCheck.Esp32Target, Source = "#include <WiFi.h>\n#include <esp_timer.h>\nvoid setup(){}\nvoid loop(){}\n" });
            var env = Env(msvc: false, esp32Toolchain: false);
            env.HeaderAvailable = (h, esp32) => esp32 && h == "WiFi.h";
            var issues = ProjectCheck.Check(p, env);
            var msvc = Assert.Single(issues);
            Assert.Equal(IssueFix.Msvc, msvc.Fix);
            Assert.Equal(IssueLevel.Error, msvc.Level);
            board.Parameters[Esp32Parts.ModeKey] = Esp32Parts.HardwareMode;
            issues = ProjectCheck.Check(p, env);
            Assert.Equal(new[] { IssueFix.Msvc, IssueFix.Esp32Toolchain }, issues.Select(i => i.Fix).ToArray());
            Assert.Equal(IssueLevel.Warning, issues[1].Level);   // it runs in the fast mode meanwhile
            Assert.DoesNotContain(ProjectCheck.Check(p, Env()), i => i.Area == ProjectCheck.CompilerArea);
        }

        [Fact]
        public void CodeOfABoardThatIsGoneIsANote()
        {
            var p = Uno();
            p.Schematic.Parts.Clear();
            var issue = Assert.Single(ProjectCheck.Check(p, Env()));
            Assert.Equal(IssueLevel.Note, issue.Level);
            Assert.Equal(ProjectCheck.CodeArea, issue.Area);
        }

        [Fact]
        public void AWorldChecksItsPartsItsBoardsAndTheirDesigns()
        {
            var world = new WorldDocument();
            world.Designs.Add(new WorldDesign { Id = "d1", Name = "Kontrol kartı", Package = Uno() });
            world.Objects.Add(new WorldObject { Name = "Kart", Kind = WorldObjectKind.Board, DesignId = "d1" });
            world.Objects.Add(new WorldObject { Name = "Kayıp kart", Kind = WorldObjectKind.Board, DesignId = "gone" });
            world.Objects.Add(new WorldObject { Name = "Garip", Kind = WorldObjectKind.Component, PartId = "no-such-part" });
            world.Objects.Add(new WorldObject
            {
                Name = "Uno", Kind = WorldObjectKind.Component, PartId = "arduino-uno",
                Firmware = new FirmwareSource { BoardRefDes = "U1", Target = "arduino:avr:uno", Source = "void setup(){}\nvoid loop(){}\n" }
            });
            var issues = ProjectCheck.Check(world, Env(arduino: false));
            Assert.Equal(4, issues.Count);
            Assert.All(issues.Take(4), i => Assert.Equal(IssueLevel.Error, i.Level));
            Assert.Contains(issues, i => i.Subject == "Kayıp kart" && i.Area == ProjectCheck.PartsArea);
            Assert.Contains(issues, i => i.Subject == "Garip" && i.Fix == IssueFix.Part);
            Assert.Contains(issues, i => i.Subject == "Uno" && i.Fix == IssueFix.ArduinoCompiler);
            Assert.Contains(issues, i => i.Subject.StartsWith("Kontrol kartı · U1 (") && i.Fix == IssueFix.ArduinoCompiler);
        }

        [Fact]
        public void AProjectWithNothingMissingHasNoIssues()
        {
            var p = Uno("#include <Wire.h>\nvoid setup(){}\nvoid loop(){}\n");
            Assert.Empty(ProjectCheck.Check(p, Env()));
        }

        [Fact]
        public void AMontajBehindItsProjectIsWorkedOutOnACopyAndLeftAsItIs()
        {
            var lib = PartLibrary.Default;
            var sch = Mechanics.VehicleTemplates.ObstacleAvoider(lib);
            sch.Assembly = null;
            var package = new ProjectPackage { Schematic = sch };
            var w = ProjectAssembly.Build(package, lib);

            // In step with its project: nothing to say.
            Assert.Null(ProjectCheck.AssemblyBehind(ProjectAssembly.Sync(w.Clone(), package, lib), "Montaj"));

            // The project moves on: a part added, one the Montaj placed removed.
            sch.AddPart(lib, "led3-red", new GridPoint(60, 0));
            sch.Parts.Remove(sch.Parts.Single(p => p.PartId == "hc-sr04"));
            string before = w.ToJson();
            var issue = ProjectCheck.AssemblyBehind(ProjectAssembly.Sync(w.Clone(), package, lib), "Montaj");
            Assert.NotNull(issue);
            Assert.Equal(IssueLevel.Warning, issue!.Level);   // a part placed in it goes
            Assert.Equal(ProjectCheck.ConsistencyArea, issue.Area);
            Assert.Equal(IssueFix.SyncMontaj, issue.Fix);
            Assert.Contains("1 parça eklenecek", issue.Text);
            Assert.Contains("1 parça kaldırılacak", issue.Text);
            Assert.Contains("devrede yok, kaldırılacak", issue.Text);   // which one, said ahead
            Assert.Equal(before, w.ToJson());   // the Montaj itself is as it was
        }

        [Fact]
        public void AMontajWhoseProjectIsGoneSaysWhere()
        {
            var issue = ProjectCheck.AssemblySourceMissing("Montaj · Araç", @"D:\projeler\arac.rwproj");
            Assert.Equal(IssueLevel.Warning, issue.Level);
            Assert.Contains(@"D:\projeler\arac.rwproj", issue.Text);
        }
    }
}
