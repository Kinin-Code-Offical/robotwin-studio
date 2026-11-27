using System.Linq;
using RobotTwin.CoreSim.Design;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[problems] The suggestion that finishes a one-pin net: the same name written another way, or the free pin of that name.</summary>
    public class NetCompletionTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;

        private static ErcViolation Lone(Schematic sch, string net) =>
            ElectricalRulesChecker.Check(sch, Lib).First(v => v.Code is "single-pin-net" or "label-single" && v.Net == net);

        private static Net? NetOf(Schematic sch, string name) => sch.BuildNetlist(Lib).Nets.FirstOrDefault(n => n.Name == name);

        [Fact]
        public void ALabelWrittenAnotherWayTakesTheNetsName()
        {
            var sch = new Schematic();
            var r1 = sch.AddPart(Lib, "res-220", new GridPoint(0, 0));
            var r2 = sch.AddPart(Lib, "res-220", new GridPoint(0, 10));
            var r3 = sch.AddPart(Lib, "res-220", new GridPoint(0, 20));
            sch.Labels.Add(new NetLabel { Name = "MOTOR_EN", Position = r1.Transform(-3, 0) });
            sch.Labels.Add(new NetLabel { Name = "MOTOR_EN", Position = r2.Transform(-3, 0) });
            sch.Labels.Add(new NetLabel { Name = "Motor-EN", Position = r3.Transform(-3, 0) });

            var fix = NetCompletions.For(sch, Lib, Lone(sch, "Motor-EN"));
            Assert.NotNull(fix);
            Assert.Contains("MOTOR_EN", fix!.Text);
            fix.ApplyTo(sch);
            Assert.Equal(3, NetOf(sch, "MOTOR_EN")!.Connections.Count);
            Assert.DoesNotContain(ElectricalRulesChecker.Check(sch, Lib), v => v.Code == "single-pin-net" && v.Net!.Contains("EN"));
        }

        [Fact]
        public void ALabelWhoseOtherEndWasNeverPutGoesOnTheFreePinOfItsName()
        {
            var sch = new Schematic();
            var r = sch.AddPart(Lib, "res-220", new GridPoint(0, 0));
            var u = sch.AddPart(Lib, AvionicsParts.Ms5837Id, new GridPoint(30, 0));
            sch.Labels.Add(new NetLabel { Name = "SDA", Position = r.Transform(-3, 0) });

            var fix = NetCompletions.For(sch, Lib, Lone(sch, "SDA"));
            Assert.NotNull(fix);
            Assert.Contains(u.RefDes + ".SDA", fix!.Text);
            fix.ApplyTo(sch);
            var sda = NetOf(sch, "SDA")!;
            Assert.Equal(2, sda.Connections.Count);
            Assert.Contains(sda.Connections, c => c.RefDes == u.RefDes && c.Pin == "SDA");
        }

        [Fact]
        public void TwoFreePinsOfTheNameAreNoSuggestion()
        {
            var sch = new Schematic();
            var r = sch.AddPart(Lib, "res-220", new GridPoint(0, 0));
            sch.AddPart(Lib, AvionicsParts.Ms5837Id, new GridPoint(30, 0));
            sch.AddPart(Lib, AvionicsParts.Ms5837Id, new GridPoint(60, 0));
            sch.Labels.Add(new NetLabel { Name = "SDA", Position = r.Transform(-3, 0) });
            Assert.Null(NetCompletions.For(sch, Lib, Lone(sch, "SDA")));
        }
    }
}
