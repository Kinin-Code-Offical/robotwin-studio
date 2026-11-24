using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[libs] ESP32 sketches use installed Arduino libraries: found like the Arduino builder finds them, compiled with the sketch.</summary>
    [Trait("Category", "Firmware")]
    public sealed class Esp32LibraryTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly string _root;
        private readonly string _libs;

        public Esp32LibraryTests(ITestOutputHelper output)
        {
            _out = output;
            _root = Path.Combine(Path.GetTempPath(), "rw-esp32-libs-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            _libs = Path.Combine(_root, "libraries");

            // The old (1.0) layout: sources in the root and utility/.
            Write("Sayac/Sayac.h", "#pragma once\n#include \"utility/yardim.h\"\nclass Sayac {\npublic:\n  int artir(int n);\nprivate:\n  int _n = 0;\n};\n");
            Write("Sayac/Sayac.cpp", "#include \"Sayac.h\"\nint Sayac::artir(int n) { _n += yardim_kare(n); return _n; }\n");
            Write("Sayac/utility/yardim.h", "#pragma once\nint yardim_kare(int n);\n");
            Write("Sayac/utility/yardim.cpp", "#include \"yardim.h\"\nint yardim_kare(int n) { return n * n; }\n");

            // The 1.5 layout with library.properties and src/, depending on Sayac; its example is not compiled.
            Write("Yanip/library.properties", "name=Yanip Soner\nversion=1.2.0\narchitectures=*\nincludes=Yanip.h\n");
            Write("Yanip/src/Yanip.h", "#pragma once\n#include <Arduino.h>\n#include <Sayac.h>\nclass Yanip {\npublic:\n  explicit Yanip(int pin) : _pin(pin) {}\n  int adim(int n);\nprivate:\n  int _pin;\n  Sayac _s;\n};\n");
            Write("Yanip/src/Yanip.cpp", "#include \"Yanip.h\"\nint Yanip::adim(int n) { pinMode(_pin, OUTPUT); digitalWrite(_pin, n & 1); return _s.artir(n); }\n");
            Write("Yanip/examples/Ornek/Ornek.ino", "#include <Yanip.h>\nthis does not compile\n");

            // Another library with the same header for AVR only: the one named after the header wins.
            Write("BaskaYanip/library.properties", "name=BaskaYanip\nversion=0.1.0\narchitectures=avr\n");
            Write("BaskaYanip/src/Yanip.h", "#error wrong library\n");

            // time.h is the C library's, not this Time.h (Windows ignores case in paths).
            Write("Time/library.properties", "name=Time\nversion=1.6.1\narchitectures=*\n");
            Write("Time/src/Time.h", "#error wrong time.h\n");

            // Wire.h comes with the HAL: an installed copy is not used.
            Write("Wire/src/Wire.h", "#error wrong Wire.h\n");
        }

        private void Write(string relative, string content)
        {
            string path = Path.Combine(_libs, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private const string Sketch = "#include <Wire.h>\n#include <time.h>\n#include <Yanip.h>\n// #include <Yok.h> in a comment is not an include\n#include \"src/a/motor.h\"\n\nYanip y(2);\n\nvoid setup() {\n  Serial.begin(115200);\n  int t = 0;\n  for (int i = 1; i <= 3; i++) t = y.adim(i);\n  Serial.printf(\"toplam=%d sol=%d sag=%d\\n\", t, solMotor(), sagMotor());\n}\n\nvoid loop() {\n  delay(100);\n}\n";

        private static FirmwareSource Firmware() => new FirmwareSource
        {
            BoardRefDes = "U1",
            Target = "esp32-hal",
            FileName = "kutuphane.ino",
            Source = Sketch,
            Files =
            {
                new FirmwareFile { Path = "src/a/motor.h", Content = "#pragma once\nint solMotor();\nint sagMotor();\n" },
                // Two files of the same name in different folders: each keeps its own object.
                new FirmwareFile { Path = "src/a/motor.cpp", Content = "int solMotor() { return 11; }\n" },
                new FirmwareFile { Path = "src/b/motor.cpp", Content = "int sagMotor() { return 22; }\n" }
            }
        };

        [Fact]
        public void TheIncludedLibrariesAndTheirDependenciesAreFound()
        {
            var libs = ArduinoLibraryResolver.Resolve(new[] { Sketch }, new[] { _libs, Path.Combine(_root, "missing") }, h => Esp32SketchCompiler.HalProvides(h));
            Assert.Equal(new[] { "Yanip Soner", "Sayac" }, libs.Select(l => l.Name));

            var yanip = libs[0];
            Assert.Equal(new[] { Path.Combine(_libs, "Yanip", "src") }, yanip.IncludeDirs);
            Assert.Equal(new[] { "Yanip.cpp" }, yanip.Sources.Select(Path.GetFileName));   // the example is left out

            var sayac = libs[1];
            Assert.Equal(new[] { Path.Combine(_libs, "Sayac"), Path.Combine(_libs, "Sayac", "utility") }, sayac.IncludeDirs);
            Assert.Equal(new[] { "Sayac.cpp", "yardim.cpp" }, sayac.Sources.Select(Path.GetFileName));

            Assert.Empty(ArduinoLibraryResolver.Resolve(new[] { "#include <Arduino.h>\n#include <Servo.h>\n#include <vector>\n" }, new[] { _libs }, Esp32SketchCompiler.HalProvides));
        }

        [Fact]
        public void ABuildIsStaleOnceAUsedLibraryChanges()
        {
            string bundle = Firmware().Bundle();
            string before = Esp32SketchCompiler.LibraryStamp(bundle, new[] { _libs });
            Assert.NotEmpty(before);
            Assert.Equal(before, Esp32SketchCompiler.LibraryStamp(bundle, new[] { _libs }));
            Assert.Empty(Esp32SketchCompiler.LibraryStamp("void setup(){}\nvoid loop(){}\n", new[] { _libs }));

            Write("Sayac/Sayac.cpp", "#include \"Sayac.h\"\nint Sayac::artir(int n) { _n += 2 * yardim_kare(n); return _n; }\n");
            Assert.NotEqual(before, Esp32SketchCompiler.LibraryStamp(bundle, new[] { _libs }));
        }

        [Esp32Fact]
        public void AnEsp32SketchRunsWithItsLibraries()
        {
            var w = new WorldDocument();
            var esp = w.AddComponent(PartLibrary.Default, Esp32Parts.DevKitCId, new WorldPose());
            esp.IsStatic = true;
            esp.Firmware = Firmware();
            string outDir = Path.Combine(_root, "out");
            var options = new RunnerOptions
            {
                Esp32Builder = src =>
                {
                    var compiler = new Esp32SketchCompiler();
                    compiler.LibraryRoots.Add(_libs);
                    var build = compiler.Compile(src, outDir);
                    Assert.True(build.Success, string.Join("\n", build.Diagnostics) + "\n" + build.Log);
                    Assert.Equal(new[] { "Yanip Soner", "Sayac" }, build.Libraries.Select(l => l.Name));
                    return build.DllPath;
                }
            };

            using var runner = new WorldRunner(w, options);
            runner.Step(1.0);
            string text = string.Concat(runner.ReadSerial(null, false).Values);
            _out.WriteLine(text);
            Assert.Contains("toplam=14 sol=11 sag=22", text);   // 1 + 4 + 9 through Yanip → Sayac → utility/yardim
        }

        [Esp32Fact]
        public void ALibraryThatDoesNotBuildIsNamed()
        {
            Write("Bozuk/src/Bozuk.h", "#pragma once\nint bozuk();\n");
            Write("Bozuk/src/Bozuk.cpp", "#include <avr/io.h>\nint bozuk() { return PORTB; }\n");
            var compiler = new Esp32SketchCompiler();
            compiler.LibraryRoots.Add(_libs);
            var build = compiler.Compile("#include <Bozuk.h>\nvoid setup() { bozuk(); }\nvoid loop() {}\n", Path.Combine(_root, "bozuk"));
            Assert.False(build.Success);
            Assert.Contains(build.Errors, d => d.Code == "RW0006" && d.Message.Contains("'Bozuk'"));
            Assert.Contains(build.Errors, d => d.Message.Contains("avr/io.h"));
            // MSVC's messages are read in its code page: a Turkish "açılamıyor" is not "a��lam�yor".
            Assert.DoesNotContain(build.Diagnostics, d => d.Message.Contains('�'));
            Assert.DoesNotContain('�', build.Log);
        }
    }
}
