using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>Runs only where KiCad 10 (or 9) is installed: its demos and its own command line give the reference netlists.</summary>
    public sealed class KiCadFactAttribute : FactAttribute
    {
        public KiCadFactAttribute()
        {
            if (KiCadSchematicImportTests.KiCadCli == null) Skip = "KiCad kurulu değil (kicad-cli bulunamadı).";
        }
    }

    /// <summary>[kicad-sch] KiCad schematics: connectivity as KiCad's own netlist, mapped onto the simulation models.</summary>
    public class KiCadSchematicImportTests
    {
        private readonly ITestOutputHelper _out;

        public KiCadSchematicImportTests(ITestOutputHelper output) => _out = output;

        internal static string? KiCadCli =>
            new[] { "10.0", "9.0" }.Select(v => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KiCad", v, "bin", "kicad-cli.exe")).FirstOrDefault(File.Exists);

        private static string Demos => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(KiCadCli!)!)!, "share", "kicad", "demos");

        /// <summary>KiCad's netlist of a schematic: each net's (reference, pin) nodes, by net name.</summary>
        private static Dictionary<string, HashSet<string>> KiCadNetlist(string schematic)
        {
            string output = Path.Combine(Path.GetTempPath(), "robotwin-kicad-net-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".net");
            var psi = new ProcessStartInfo(KiCadCli!, $"sch export netlist --format kicadsexpr -o \"{output}\" \"{schematic}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi)!)
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
            }

            var root = SExpression.Parse(File.ReadAllText(output));
            File.Delete(output);
            var nets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var net in root.Child("nets")!.Children("net"))
                nets[net.ChildText("name") ?? string.Empty] = new HashSet<string>(net.Children("node").Select(n => n.ChildText("ref") + "." + n.ChildText("pin")), StringComparer.Ordinal);
            return nets;
        }

        private static string? Project(string schematic) => File.Exists(Path.ChangeExtension(schematic, ".kicad_pro")) ? File.ReadAllText(Path.ChangeExtension(schematic, ".kicad_pro")) : null;

        private static IEnumerable<string> DemoRoots()
        {
            foreach (var project in Directory.EnumerateFiles(Demos, "*.kicad_pro", SearchOption.AllDirectories))
            {
                string sch = Path.ChangeExtension(project, ".kicad_sch");
                if (File.Exists(sch)) yield return sch;
            }
        }

        private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "kicad", "led-divider.kicad_sch"));

        [Fact]
        public void ASheetConnectsAsKiCadSaysIt()
        {
            // Fixtures/kicad/led-divider.kicad_sch: KiCad's own symbols; the nets below are what kicad-cli exports for it.
            var design = KiCadSchematicImport.Read(Fixture);
            string Nodes(string net) => string.Join(" ", design.Nets.Single(n => n.Name == net).Nodes);
            foreach (var n in design.Nets) _out.WriteLine($"{n.Name}: {string.Join(" ", n.Nodes)}");
            Assert.Equal("BT1.1 R1.1 R2.1", Nodes("+5V"));
            Assert.Equal("BT1.2 D1.1 R3.2", Nodes("GND"));
            Assert.Equal("R2.2 R3.1 R4.1", Nodes("/MID"));             // a label on the wire, a T with its junction
            Assert.Equal("D1.2 R1.2", Nodes("Net-(D1-Pad2)"));        // the LED turned, its anode up
            Assert.Equal("R4.2", Nodes("/PROBE"));                    // the wire crosses the LED's without a junction: not joined
            Assert.True(design.Nets.Where(n => n.Nodes.Any(x => x.Reference == "R5")).All(n => n.NoConnect && n.Nodes.Count == 1));
            Assert.Equal(7, design.Symbols.Count);                    // the power symbols name nets, they are not parts
        }

        [Fact]
        public void ASheetImportsOntoModelsAndSimulates()
        {
            var lib = PartLibrary.Default;
            var result = KiCadSchematicImport.Import(Fixture, lib);
            foreach (var line in result.Report) _out.WriteLine(line);
            Assert.Equal(7, result.Mapped);
            Assert.Empty(result.Missing);
            var sch = result.Schematic;
            Assert.Equal("LED ve bölücü", sch.Title);
            Assert.Equal(2, sch.NoConnects.Count);
            Assert.Contains(sch.Parts, p => lib.Get(p.PartId).Symbol == SymbolKind.Ground);

            // the divider halves the battery, the LED's current is what its resistor lets through
            var compiled = CircuitCompiler.Compile(sch, lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            string Pin(string refDes, string number) => sch.Parts.Single(p => p.RefDes == refDes).Parameters["pinmap"].Split(',').Select(x => x.Split(':')).Single(x => x[1] == number)[0];
            double V(string refDes, string number) => compiled.PinVoltage(sim, refDes, Pin(refDes, number));
            double battery = V("BT1", "1") - V("BT1", "2"), mid = V("R2", "2") - V("BT1", "2");
            double led = (V("R1", "1") - V("R1", "2")) / 330;
            _out.WriteLine($"battery {battery:0.000} V, MID {mid:0.000} V, LED {led * 1000:0.00} mA, PROBE {V("R4", "2") - V("BT1", "2"):0.000} V");
            Assert.InRange(battery, 4.5, 5.05);
            Assert.InRange(mid / battery, 0.49, 0.51);
            Assert.InRange(led, 0.005, 0.012);
        }

        [KiCadFact]
        public void TheDemosConnectAsKiCadsOwnNetlist()
        {
            int checkedNets = 0, failures = 0, busNames = 0;
            foreach (var sch in DemoRoots())
            {
                string dir = Path.GetDirectoryName(sch)!;
                var design = KiCadSchematicImport.Read(File.ReadAllText(sch), f => File.Exists(Path.Combine(dir, f)) ? File.ReadAllText(Path.Combine(dir, f)) : null, Project(sch));
                var expected = KiCadNetlist(sch);
                // KiCad's netlist leaves out the symbols that are not on the board
                var offBoard = new HashSet<string>(design.Symbols.Where(x => !x.Placement.OnBoard).Select(x => x.Reference), StringComparer.Ordinal);
                var ours = design.Nets.ToDictionary(n => n, n => new HashSet<string>(n.Nodes.Where(x => !offBoard.Contains(x.Reference)).Select(x => x.Reference + "." + x.Pin), StringComparer.Ordinal));
                var netOfNode = new Dictionary<string, KiCadNet>(StringComparer.Ordinal);
                foreach (var kv in ours)
                foreach (var node in kv.Value)
                    netOfNode[node] = kv.Key;

                int bad = 0, named = 0, badNames = 0;
                foreach (var (name, nodes) in expected)
                {
                    if (nodes.Count < 2) continue;
                    checkedNets++;
                    string first = nodes.First();
                    if (!netOfNode.TryGetValue(first, out var net) || !ours[net].SetEquals(nodes))
                    {
                        if (bad++ < 4)
                        {
                            var got = net == null ? new HashSet<string>() : ours[net];
                            _out.WriteLine($"  {Path.GetFileName(sch)} {name}: KiCad {string.Join(" ", nodes.OrderBy(x => x))} | ours ({net?.Name}) {string.Join(" ", got.OrderBy(x => x))}");
                        }

                        continue;
                    }

                    if (!name.StartsWith("Net-(", StringComparison.Ordinal) && !name.StartsWith("unconnected-(", StringComparison.Ordinal))
                    {
                        named++;
                        if (net.Name != KiCadSchematicImport.Unescape(name) && badNames++ < 3) _out.WriteLine($"  {Path.GetFileName(sch)}: KiCad names it '{name}', ours '{net.Name}'");
                    }
                }

                bool buses = design.Buses > 0;
                _out.WriteLine($"{(buses ? "(bus) " : string.Empty)}{Path.GetFileName(sch)}: {design.Sheets} sheet(s), {design.Symbols.Count} symbols, {expected.Count(e => e.Value.Count >= 2)} nets, {bad} differ, {badNames} of {named} named differently" + (design.Warnings.Count > 0 ? " · " + design.Warnings[0] : string.Empty));
                // Every net is KiCad's, buses or not. Which label a bus member's net is called by follows KiCad's own
                // choice among its drivers only without buses; with them the name may be another of its labels.
                failures += bad + (buses ? 0 : badNames);
                if (buses) busNames += badNames;
            }

            _out.WriteLine($"{checkedNets} nets checked; {busNames} nets on buses called by another of their labels");
            Assert.True(checkedNets > 100, $"only {checkedNets} nets checked");
            Assert.Equal(0, failures);
        }
    
        [KiCadFact]
        public void TheDemosImportOntoOurModelsWithKiCadsNets()
        {
            foreach (var sch in DemoRoots())
            {
                string dir = Path.GetDirectoryName(sch)!;
                var lib = RobotTwin.CoreSim.Design.PartLibrary.Default;
                var result = KiCadSchematicImport.Import(File.ReadAllText(sch), lib, f => File.Exists(Path.Combine(dir, f)) ? File.ReadAllText(Path.Combine(dir, f)) : null, Project(sch));
                if (result.Design.Warnings.Any(w => w.Contains("(bus)", StringComparison.Ordinal))) continue;
                var netlist = result.Schematic.BuildNetlist(lib);
                int agree = 0, differ = 0;
                foreach (var net in result.Design.Nets.Where(n => n.Nodes.Count > 1))
                {
                    // every part made from the symbol (U1, or U1A and U1B when its units were placed apart) that carries the pin
                    var ours = net.Nodes.SelectMany(n => result.Schematic.Parts
                            .Where(p => p.RefDes == n.Reference || p.RefDes.Length == n.Reference.Length + 1 && p.RefDes.StartsWith(n.Reference, StringComparison.Ordinal) && char.IsUpper(p.RefDes[^1]))
                            .Select(p => OurPin(p, n.Pin) is { } pin ? netlist.NetOf(p.RefDes, pin) : null))
                        .Where(x => x != null).Distinct().ToList();
                    if (ours.Count <= 1) agree++;
                    else if (differ++ < 3) _out.WriteLine($"   {net.Name}: KiCad {string.Join(" ", net.Nodes)} → ours {string.Join(" | ", ours.Select(x => x!.Name + ": " + string.Join(" ", x.Connections)))}");
                }

                _out.WriteLine($"{Path.GetFileName(sch)}: {result.Mapped} mapped, {result.Generic} generic, {result.Skipped} left out; {agree} nets whole, {differ} split; {result.Schematic.Labels.Count} labels, {result.Schematic.Parts.Count} parts");
                foreach (var line in result.Report.Where(l => l.Contains("uyarı") || l.Contains("alınmadı")).Take(3)) _out.WriteLine("   " + line);
                Assert.Equal(0, differ);
            }
        }

        private static string? OurPin(RobotTwin.CoreSim.Design.SchematicPart part, string number)
        {
            if (!part.Parameters.TryGetValue("pinmap", out var map)) return null;
            foreach (var pair in map.Split(','))
            {
                int colon = pair.LastIndexOf(':');
                if (colon > 0 && pair.Substring(colon + 1).Trim() == number) return pair.Substring(0, colon).Trim();
            }

            return null;
        }
    }
}
