using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Editor.Completion;
using RobotWin.Studio.Firmware;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [integrity] <see cref="ProjectCheck"/> with what this computer has: the Arduino compiler (the installer's component
    /// or the user's own arduino-cli, with its AVR core), Visual Studio's C++ tools, the ESP32 toolchain, the headers of the
    /// installed Arduino libraries and the library index (which library provides a missing header).
    /// </summary>
    public static class ProjectChecking
    {
        /// <summary>What this computer has, looked up as the check asks (headers and the index only when needed).</summary>
        public static ProjectEnvironment Environment(PartLibrary library)
        {
            string data = ArduinoCompiler.DataFolder;
            var avr = new Lazy<ArduinoHeaders>(() => new ArduinoHeaders(false, ArduinoCompiler.LibraryRoots, data));
            var esp = new Lazy<ArduinoHeaders>(() => new ArduinoHeaders(true, ArduinoCompiler.LibraryRoots, null, Esp32HalHeaders()));
            var index = new Lazy<ArduinoLibraryIndex?>(() =>
            {
                ArduinoLibraryIndex.DataFolder = data;
                return ArduinoLibraryIndex.Load(ArduinoLibraryIndex.DefaultPath);
            });
            return new ProjectEnvironment
            {
                Library = library,
                ArduinoCompiler = ArduinoCompiler.FindCli() != null && Directory.Exists(Path.Combine(data, "packages", "arduino", "hardware", "avr")),
                Esp32Compiler = RobotTwin.CoreSim.Firmware.Esp32.Esp32SketchCompiler.IsToolchainAvailable,
                Esp32Toolchain = ArduinoCompiler.Esp32ToolchainInstalled,
                HeaderAvailable = (header, esp32) => (esp32 ? esp.Value : avr.Value).Has(header),
                LibraryFor = (header, esp32) => index.Value?.Providing(header, esp32 ? "esp32" : "avr").FirstOrDefault()?.Name
            };
        }

        /// <summary>
        /// Checks a project on another thread (it reads a copy, so editing can go on); whether its PCB is behind the
        /// schematic is worked out here, on a copy of the board.
        /// </summary>
        public static async Task<List<ProjectIssue>> CheckAsync(ProjectPackage project, PartLibrary library)
        {
            var copy = ProjectCheck.Snapshot(project);
            var behind = BoardBehind(project, library, "PCB");
            var issues = await Task.Run(() => ProjectCheck.Check(copy, Environment(library)));
            if (behind != null) issues.Add(behind);
            return issues.OrderBy(i => i.Level).ToList();   // errors first (stable: each level as the check ordered it)
        }

        private static ProjectIssue? BoardBehind(ProjectPackage project, PartLibrary library, string subject)
        {
            var board = project.Schematic.Board;
            if (board == null) return null;
            try
            {
                return ProjectCheck.BoardBehind(RobotTwin.CoreSim.Pcb.BoardSync.Preview(project.Schematic, library, board), subject);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is KeyNotFoundException || ex is System.Text.Json.JsonException)
            {
                return null;
            }
        }

        /// <summary>The check's issues together with ones worked out elsewhere (a Montaj behind its projects), errors first.</summary>
        public static List<ProjectIssue> With(List<ProjectIssue> issues, List<ProjectIssue> more) =>
            more.Count == 0 ? issues : issues.Concat(more).OrderBy(i => i.Level).ToList();

        /// <summary>Checks a world, its boards and their designs on another thread.</summary>
        public static Task<List<ProjectIssue>> CheckAsync(WorldDocument world, PartLibrary library)
        {
            var copy = ProjectCheck.Snapshot(world);
            return Task.Run(() => ProjectCheck.Check(copy, Environment(library)));
        }

        /// <summary>The ESP32 simulation's headers, embedded in the simulator (what an ESP32 sketch is compiled against).</summary>
        public static Dictionary<string, string> Esp32HalHeaders()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var asm = typeof(RobotTwin.CoreSim.Firmware.Esp32.Esp32SketchCompiler).Assembly;
            foreach (string name in asm.GetManifestResourceNames())
            {
                string n = name.Replace('\\', '/');
                if (!n.StartsWith("esp32hal/", StringComparison.Ordinal) || !n.EndsWith(".h", StringComparison.Ordinal)) continue;
                using var s = asm.GetManifestResourceStream(name);
                if (s == null) continue;
                using var r = new StreamReader(s);
                map[n.Substring("esp32hal/".Length)] = r.ReadToEnd();
            }

            return map;
        }

        /// <summary>Issues that ask for something (errors and warnings; notes only say what changing the code would need).</summary>
        public static int Blocking(IEnumerable<ProjectIssue> issues) => issues.Count(i => i.Level != IssueLevel.Note);
    }
}
