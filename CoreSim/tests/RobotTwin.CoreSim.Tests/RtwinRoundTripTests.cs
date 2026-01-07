using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RobotTwin.CoreSim.Serialization;
using RobotTwin.CoreSim.Specs;
using Xunit;

namespace RobotTwin.CoreSim.Tests;

public class RtwinRoundTripTests
{
    [Fact]
    public void FixedRtwinFixtureLoadsProjectManifest()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "RobotTwin.CoreSim.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var filePath = Path.Combine(tempRoot, "fixture_v1.rtwin");
            File.WriteAllBytes(filePath, LoadFixtureBytes("fixture_v1.rtwin.base64"));

            var loaded = SimulationSerializer.LoadProject(filePath, extractWorkspace: false);
            Assert.NotNull(loaded);
            Assert.Equal("FixtureProject", loaded!.ProjectName);
            Assert.Equal("1.0.0", loaded.Version);
            Assert.Equal("Fixture manifest for tests", loaded.Description);
            Assert.Equal("circuit-fixture", loaded.Circuit.Id);
            Assert.Equal("FixtureBot", loaded.Robot.Name);
            Assert.Equal("FixtureWorld", loaded.World.Name);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RtwinSaveLoadExtractRoundTripPreservesManifestAndCodeFiles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "RobotTwin.CoreSim.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var filePath = Path.Combine(tempRoot, "sample.rtwin");
            var workspaceRoot = Path.Combine(tempRoot, "sample");
            var codeDir = Path.Combine(workspaceRoot, "Code");
            Directory.CreateDirectory(codeDir);
            var codePath = Path.Combine(codeDir, "main.ino");
            File.WriteAllText(codePath, "// fixture code\nvoid setup(){}\nvoid loop(){}\n");

            var manifest = new ProjectManifest
            {
                ProjectName = "SampleProject",
                Version = "1.0.0",
                Description = "Round-trip serializer test",
                Circuit = new CircuitSpec { Id = "circuit-1", Mode = SimulationMode.Fast },
                Robot = new RobotSpec { Name = "Robot-1" },
                World = new WorldSpec { Name = "World-1", Width = 1, Depth = 1 },
                Metadata = new Dictionary<string, object>()
            };

            SimulationSerializer.SaveProject(manifest, filePath);
            Assert.True(File.Exists(filePath), $"Expected .rtwin to exist: {filePath}");

            var loadedNoExtract = SimulationSerializer.LoadProject(filePath, extractWorkspace: false);
            Assert.NotNull(loadedNoExtract);
            Assert.Equal(manifest.ProjectName, loadedNoExtract!.ProjectName);
            Assert.Equal(manifest.Version, loadedNoExtract.Version);
            Assert.Equal(manifest.Description, loadedNoExtract.Description);
            Assert.Equal(manifest.Circuit.Id, loadedNoExtract.Circuit.Id);
            Assert.Equal(manifest.Robot.Name, loadedNoExtract.Robot.Name);
            Assert.Equal(manifest.World.Name, loadedNoExtract.World.Name);

            var loadedWithExtract = SimulationSerializer.LoadProject(filePath, extractWorkspace: true);
            Assert.NotNull(loadedWithExtract);

            var extractedCodePath = Path.Combine(workspaceRoot, "Code", "main.ino");
            Assert.True(File.Exists(extractedCodePath), $"Expected code file to be extracted: {extractedCodePath}");
            Assert.Contains("void setup", File.ReadAllText(extractedCodePath));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RtwinExtractWritesWorkspaceSnapshotFiles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "RobotTwin.CoreSim.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var filePath = Path.Combine(tempRoot, "snapshot.rtwin");
            var manifest = new ProjectManifest
            {
                ProjectName = "SnapshotProject",
                Version = "1.0.1",
                Description = "Workspace snapshot test",
                Circuit = new CircuitSpec { Id = "circuit-snapshot", Mode = SimulationMode.Fast },
                Robot = new RobotSpec { Name = "SnapshotBot" },
                World = new WorldSpec { Name = "SnapshotWorld", Width = 2, Depth = 2 },
                Metadata = new Dictionary<string, object> { { "author", "fixture" } }
            };

            SimulationSerializer.SaveProject(manifest, filePath);
            var loaded = SimulationSerializer.LoadProject(filePath, extractWorkspace: true);
            Assert.NotNull(loaded);

            var workspaceRoot = Path.Combine(tempRoot, "snapshot");
            Assert.True(File.Exists(Path.Combine(workspaceRoot, "project.json")));
            Assert.True(File.Exists(Path.Combine(workspaceRoot, "circuit.json")));
            Assert.True(File.Exists(Path.Combine(workspaceRoot, "robot.json")));
            Assert.True(File.Exists(Path.Combine(workspaceRoot, "world.json")));
            Assert.True(File.Exists(Path.Combine(workspaceRoot, "metadata.json")));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RtrobotSaveLoadExtractRoundTripPreservesAssemblyAndEnvironment()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "RobotTwin.CoreSim.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var filePath = Path.Combine(tempRoot, "robotstudio_fixture.rtrobot");
            var workspaceRoot = Path.Combine(tempRoot, "robotstudio_fixture");

            var package = new RobotStudioPackage
            {
                Robot = new RobotSpec { Name = "Robot-1" },
                Circuit = new CircuitSpec { Id = "circuit-rs-1", Mode = SimulationMode.Fast },
                Assembly = new AssemblySpec
                {
                    Parts = new List<AssemblyPartSpec>
                    {
                        new AssemblyPartSpec
                        {
                            InstanceId = "part-1",
                            ComponentType = "servo",
                            CatalogId = "catalog/servo/sg90",
                            Position = new Vec3 { X = 1, Y = 2, Z = 3 },
                            Rotation = new Vec3 { X = 0, Y = 90, Z = 0 },
                            Scale = new Vec3 { X = 1, Y = 1, Z = 1 },
                            Pinned = true,
                            DensityKgPerM3 = 1200,
                            MassKg = 0.055,
                            Friction = 0.8
                        }
                    },
                    Wires = new List<AssemblyWireSpec>
                    {
                        new AssemblyWireSpec
                        {
                            Id = "wire-1",
                            From = "part-1:signal",
                            To = "mcu:pwm0",
                            LengthMeters = 0.15,
                            YoungModulus = 1e9,
                            MaxStress = 4e7,
                            Damping = 0.05
                        }
                    }
                },
                Environment = new EnvironmentSpec
                {
                    Name = "Lab",
                    TemperatureC = 22,
                    VibrationAmplitude = 0.01,
                    VibrationFrequencyHz = 60,
                    Gravity = 9.81,
                    AirDensity = 1.225,
                    SurfaceFriction = 0.6
                },
                Metadata = new Dictionary<string, object> { { "author", "fixture" } }
            };

            SimulationSerializer.SaveRobotPackage(package, filePath);
            Assert.True(File.Exists(filePath), $"Expected .rtrobot to exist: {filePath}");

            var loadedNoExtract = SimulationSerializer.LoadRobotPackage(filePath, extractWorkspace: false);
            Assert.NotNull(loadedNoExtract);
            Assert.NotNull(loadedNoExtract!.Assembly);
            Assert.NotNull(loadedNoExtract.Environment);
            Assert.Single(loadedNoExtract.Assembly.Parts);
            Assert.Single(loadedNoExtract.Assembly.Wires);
            Assert.Equal("Lab", loadedNoExtract.Environment.Name);
            Assert.Equal(9.81, loadedNoExtract.Environment.Gravity, 3);

            var loadedWithExtract = SimulationSerializer.LoadRobotPackage(filePath, extractWorkspace: true);
            Assert.NotNull(loadedWithExtract);

            Assert.True(File.Exists(Path.Combine(workspaceRoot, "assembly.json")));
            Assert.True(File.Exists(Path.Combine(workspaceRoot, "environment.json")));
            Assert.True(File.Exists(Path.Combine(workspaceRoot, "robot.json")));
            Assert.True(File.Exists(Path.Combine(workspaceRoot, "circuit.json")));
            Assert.True(File.Exists(Path.Combine(workspaceRoot, "metadata.json")));
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static byte[] LoadFixtureBytes(string fileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        if (!File.Exists(fixturePath))
        {
            // Fallback for local runs where AppContext.BaseDirectory differs.
            fixturePath = Path.Combine(Directory.GetCurrentDirectory(), "CoreSim", "tests", "RobotTwin.CoreSim.Tests", "Fixtures", fileName);
        }
        var b64 = File.ReadAllText(fixturePath);
        var normalized = new StringBuilder(b64.Length);
        foreach (var ch in b64)
        {
            if (!char.IsWhiteSpace(ch)) normalized.Append(ch);
        }
        return Convert.FromBase64String(normalized.ToString());
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp dirs.
        }
    }
}
