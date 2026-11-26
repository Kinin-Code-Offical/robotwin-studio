using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[components] Unity's component checkbox: a switched-off component is not in the run and keeps its settings.</summary>
    public class ComponentSwitchTests
    {
        [Fact]
        public void ASwitchedOffShellIsASolidBoxAgainAndComesBackAsItWas()
        {
            var w = new WorldDocument();
            var box = w.AddBody("box", 0.1, 0.08, 0.05, new WorldPose());
            box.Parameters["shell"] = "true";
            box.Parameters["wall"] = "0.003";
            Assert.True(WorldPhysics.IsShell(box));

            WorldComponents.SetOn(box, "shell", false);
            Assert.False(WorldPhysics.IsShell(box));   // every reader sees a plain body
            Assert.True(WorldComponents.Has(box, "shell"));
            Assert.True(WorldComponents.IsOff(box, "shell"));
            Assert.Equal("0.003", box.Parameters["wall"]);   // its settings stay

            WorldComponents.SetOn(box, "shell", true);
            Assert.True(WorldPhysics.IsShell(box));
            Assert.False(box.Parameters.ContainsKey("shell" + WorldComponents.OffSuffix));
        }

        [Fact]
        public void ASwitchedOffParachuteKeepsItsCanopyButIsNotInTheRun()
        {
            var w = new WorldDocument();
            var body = w.AddBody("cylinder", 0.04, 0.04, 0.2, new WorldPose { Z = 0.1 });
            body.Parameters["parachute"] = "0.6";
            WorldComponents.SetOn(body, "parachute", false);
            Assert.False(body.Parameters.ContainsKey("parachute"));
            Assert.Equal("0.6", body.Parameters["parachute" + WorldComponents.OffSuffix]);
            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default).GeometryOf, null);
            Assert.NotNull(p);   // builds as a plain body

            WorldComponents.SetOn(body, "parachute", true);
            Assert.Equal("0.6", body.Parameters["parachute"]);
            WorldComponents.SetOn(body, "parachute", true);   // twice on: nothing changes
            Assert.Equal("0.6", body.Parameters["parachute"]);
        }

        [Fact]
        public void AComponentCopiesOntoAnotherPartExactlyAsItWas()
        {
            var w = new WorldDocument();
            var a = w.AddBody("cylinder", 0.04, 0.04, 0.2, new WorldPose());
            var b = w.AddBody("cylinder", 0.05, 0.05, 0.3, new WorldPose { X = 0.2 });
            a.Parameters["parachute"] = "0.6";
            a.Parameters["parachute.cd"] = "0.9";
            a.Parameters["wall"] = "0.002";   // not the parachute's
            var copy = WorldComponents.Copy(a, "parachute");
            Assert.Equal(2, copy.Count);

            Assert.True(WorldComponents.CanCarry(b, "parachute", copy));
            b.Parameters["parachute"] = "0.3";
            b.Parameters["parachute.deploy"] = "4";   // the copy has none: it goes
            WorldComponents.Paste(b, "parachute", copy);
            Assert.Equal("0.6", b.Parameters["parachute"]);
            Assert.Equal("0.9", b.Parameters["parachute.cd"]);
            Assert.False(b.Parameters.ContainsKey("parachute.deploy"));
            Assert.False(b.Parameters.ContainsKey("wall"));

            // a switched-off component comes over switched off
            WorldComponents.SetOn(a, "parachute", false);
            WorldComponents.Paste(b, "parachute", WorldComponents.Copy(a, "parachute"));
            Assert.True(WorldComponents.IsOff(b, "parachute"));
            Assert.False(b.Parameters.ContainsKey("parachute"));
        }

        [Fact]
        public void AShellOnlyGoesOntoABoxAndNeverBesideAVessel()
        {
            var w = new WorldDocument();
            var box = w.AddBody("box", 0.1, 0.08, 0.05, new WorldPose());
            var ball = w.AddBody("sphere", 0.05, 0.05, 0.05, new WorldPose());
            var shell = new System.Collections.Generic.Dictionary<string, string> { ["shell"] = "true", ["wall"] = "0.002" };
            Assert.True(WorldComponents.CanCarry(box, "shell", shell));
            Assert.False(WorldComponents.CanCarry(ball, "shell", shell));
            box.Parameters["vessel"] = "true";
            Assert.False(WorldComponents.CanCarry(box, "shell", shell));
        }

        [Fact]
        public void OnlyTheComponentsThatCanBeSwitchedAreSwitched()
        {
            var o = new WorldObject();
            Assert.Throws<System.ArgumentException>(() => WorldComponents.SetOn(o, "wall", false));
            WorldComponents.SetOn(o, "vessel", false);   // not carried: nothing happens
            Assert.Empty(o.Parameters);
        }
    }
}
