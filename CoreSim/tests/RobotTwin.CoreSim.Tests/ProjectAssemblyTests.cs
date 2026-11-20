using System;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Pcb;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[assembly] A project's circuit becomes a real assembly: real parts, cabled pin to pin, no vehicle template.</summary>
    public class ProjectAssemblyTests
    {
        private readonly ITestOutputHelper _out;

        public ProjectAssemblyTests(ITestOutputHelper output) => _out = output;

        private static PartLibrary Lib => PartLibrary.Default;

        [Fact]
        public void ACircuitWithoutAPcbBecomesLooseRealPartsWiredByItsNets()
        {
            var sch = VehicleTemplates.ObstacleAvoider(Lib);
            var w = ProjectAssembly.Build(new ProjectPackage { Schematic = sch }, Lib);
            int physical = sch.Parts.Count(p => Lib.Find(p.PartId) is { } d && d.Symbol != SymbolKind.Ground && d.Pins.Count > 0);
            foreach (var o in w.Objects) _out.WriteLine($"{o.Name} ({o.PartId}) at {o.Pose.X * 1000:F0},{o.Pose.Y * 1000:F0} mm");
            _out.WriteLine($"{w.Cables.Count} cables");
            Assert.Equal(physical, w.Objects.Count);
            Assert.All(w.Objects, o => Assert.Equal(WorldObjectKind.Component, o.Kind));
            Assert.True(w.Cables.Count > 0);

            // Every cable lands on a real pin and the whole thing compiles as one circuit.
            var wc = WorldCompiler.Compile(w, Lib, c => c.Length);
            foreach (var warn in wc.Compiled.Warnings) _out.WriteLine("warning: " + warn);
            Assert.DoesNotContain(wc.Compiled.Warnings, x => x.Contains("pini bulamadı"));

            // The original nets survive: two pins on one net in the circuit are on one node in the world.
            // Each cable is a real conductor (copper + contacts), so its ends are separate nodes: check the voltages.
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
            // Say the controller and the drivers are soldered on the project's PCB; motors, battery and the sensor are not.
            var pcbParts = sch.Parts.Where(p => Lib.Find(p.PartId) is { } d && (d.Symbol == SymbolKind.Board || d.Id == "motor-driver")).Select(p => p.RefDes).ToList();
            Assert.NotEmpty(pcbParts);
            sch.Board ??= new BoardDocument();
            foreach (var r in pcbParts) sch.Board.Components.Add(new BoardComponent { RefDes = r });
            var w = ProjectAssembly.Build(new ProjectPackage { Schematic = sch }, Lib);
            var board = Assert.Single(w.Objects, o => o.Kind == WorldObjectKind.Board);
            var design = w.FindDesign(board.DesignId)!;
            Assert.All(design.Package.Schematic.Parts.Where(p => Lib.Find(p.PartId)?.Symbol != SymbolKind.Ground), p => Assert.Contains(p.RefDes, pcbParts));
            int loose = w.Objects.Count(o => o.Kind == WorldObjectKind.Component);
            _out.WriteLine($"board with {design.Package.Schematic.Parts.Count} parts, {loose} loose parts, {w.Cables.Count} cables");
            Assert.True(loose > 0);
            Assert.Contains(w.Cables, c => c.A.ObjectId == board.Id || c.B.ObjectId == board.Id);
            var wc = WorldCompiler.Compile(w, Lib, c => c.Length);
            Assert.DoesNotContain(wc.Compiled.Warnings, x => x.Contains("pini bulamadı"));
        }
    }
}
