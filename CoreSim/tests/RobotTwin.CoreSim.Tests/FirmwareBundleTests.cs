using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Formats;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[code] Sketches of several files: kept in a project file, handed to a builder as one bundle, compiled for ESP32.</summary>
    public class FirmwareBundleTests
    {
        private static FirmwareSource TwoJointSketch() => new FirmwareSource
        {
            BoardRefDes = "U1",
            Target = "esp32-hal",
            FileName = "kol.ino",
            Source = "#include \"src/Eklem.h\"\n\nEklem omuz(18);\n\nvoid setup() {\n  Serial.begin(115200);\n  omuz.git(90);\n  Serial.println(omuz.son());\n}\n\nvoid loop() {\n  delay(100);\n}\n",
            Files =
            {
                new FirmwareFile { Path = "src/Eklem.h", Content = "#pragma once\n#include <Arduino.h>\n\nclass Eklem {\npublic:\n  explicit Eklem(int pin);\n  void git(int aci);\n  int son() const { return _aci; }\nprivate:\n  int _pin;\n  int _aci = 0;\n};\n" },
                new FirmwareFile { Path = "src/Eklem.cpp", Content = "#include \"Eklem.h\"\n\nEklem::Eklem(int pin) : _pin(pin) {}\n\nvoid Eklem::git(int aci) {\n  _aci = aci;\n  pinMode(_pin, OUTPUT);\n  digitalWrite(_pin, aci > 90 ? HIGH : LOW);\n}\n" }
            }
        };

        [Fact]
        public void ABundleCarriesEveryFileAndASingleSourceStaysAsItIs()
        {
            var fw = TwoJointSketch();
            string bundle = fw.Bundle();
            var files = FirmwareSource.Unbundle(bundle);
            Assert.Equal(new[] { "kol.ino", "src/Eklem.cpp", "src/Eklem.h" }, files.Select(f => f.Path));
            Assert.Equal(fw.Source.TrimEnd('\n'), files[0].Content);
            Assert.Equal(fw.Files.Single(f => f.Path == "src/Eklem.h").Content.TrimEnd('\n'), files.Single(f => f.Path == "src/Eklem.h").Content);

            var single = new FirmwareSource { Source = "void setup(){}\nvoid loop(){}\n" };
            Assert.Equal(single.Source, single.Bundle());   // old builds' hashes stay valid
            Assert.Single(FirmwareSource.Unbundle(single.Bundle()));

            // A build is current only for the files it was made from.
            fw.Hex = ":00000001FF";
            fw.BuiltFromHash = FirmwareSource.HashOf(fw.Bundle());
            Assert.True(fw.IsBuildCurrent);
            fw.Files[0].Content += "\n// değişti";
            Assert.False(fw.IsBuildCurrent);
        }

        [Fact]
        public void AProjectKeepsTheSketchFilesAndFolders()
        {
            var pkg = new ProjectPackage();
            pkg.Firmware.Add(TwoJointSketch());
            string path = Path.Combine(Path.GetTempPath(), "rw-bundle-" + Guid.NewGuid().ToString("N") + ".rwproj");
            try
            {
                pkg.Save(path);
                var back = ProjectPackage.Load(path).Firmware.Single();
                Assert.Equal("kol.ino", back.FileName);
                Assert.Equal(TwoJointSketch().Source, back.Source);
                Assert.Equal(new[] { "src/Eklem.h", "src/Eklem.cpp" }, back.Files.Select(f => f.Path));
                Assert.Contains("class Eklem", back.Files[0].Content);
                using var zip = ZipFile.OpenRead(path);
                Assert.Contains(zip.Entries, e => e.FullName == "firmware/U1/files/src/Eklem.cpp");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Esp32Fact]
        public void AnEsp32SketchOfSeveralFilesCompilesFromItsBundle()
        {
            string dir = Path.Combine(Path.GetTempPath(), "rw-bundle-esp32-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                var build = new Esp32SketchCompiler().Compile(TwoJointSketch().Bundle(), dir);
                Assert.True(build.Success, string.Join("\n", build.Diagnostics) + "\n" + build.Log);
                Assert.True(File.Exists(build.DllPath));
            }
            finally
            {
                try
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
