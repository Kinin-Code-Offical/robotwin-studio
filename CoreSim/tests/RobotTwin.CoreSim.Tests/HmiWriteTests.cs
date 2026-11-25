using RobotTwin.CoreSim.Industrial;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[hmi] An operator panel writes a PLC's variables as an HMI writes its memory; inputs stay the pins'.</summary>
    public class HmiWriteTests
    {
        [Fact]
        public void APanelWritesMemoryAndInternalVariablesButNotInputs()
        {
            var p = StProgram.Compile("VAR Start AT %IX0.0 : BOOL; Run : BOOL; Speed AT %MW4 : INT; Lamp AT %QX0.0 : BOOL; END_VAR\nLamp := Run AND Speed > 10;", out var errors)!;
            Assert.Empty(errors);
            Assert.True(p.Write("run", 1));          // a switch on the panel
            Assert.True(p.Write("Speed", 25));       // a set point
            Assert.False(p.Write("Start", 1));       // the pin decides an input
            Assert.False(p.Write("Nothing", 1));
            p.Scan(10);
            Assert.True(p.Outputs[0]);
            Assert.Equal(25, p.Memory[4]);
            Assert.True(p.Write("Speed", 5));
            p.Scan(10);
            Assert.False(p.Outputs[0]);
        }

        [Fact]
        public void APanelIsSavedWithTheWorld()
        {
            var w = new WorldDocument { Hmi = new System.Collections.Generic.List<HmiWidget> { new HmiWidget { Kind = HmiWidget.SetPoint, Plc = "O1", Variable = "SPEED", Label = "Hız", Max = 50, Unit = "%" } } };
            var again = WorldDocument.FromJson(w.ToJson());
            var x = Assert.Single(again.Hmi!);
            Assert.Equal((HmiWidget.SetPoint, "SPEED", "Hız", 50.0, "%"), (x.Kind, x.Variable, x.Label, x.Max, x.Unit));
            Assert.Null(WorldDocument.FromJson(new WorldDocument().ToJson()).Hmi);
        }
    }
}
