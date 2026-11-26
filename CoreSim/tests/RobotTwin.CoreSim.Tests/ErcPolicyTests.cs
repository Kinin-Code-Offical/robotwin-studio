using System.Linq;
using RobotTwin.CoreSim.Design;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[erc-policy] ERC severities per rule and accepted markers, saved with the schematic, as KiCad's ERC settings.</summary>
    public class ErcPolicyTests
    {
        /// <summary>A resistor with one pin wired to nothing but a dangling wire, and a label alone.</summary>
        private static Schematic Sheet()
        {
            var s = new Schematic();
            s.Parts.Add(new SchematicPart { RefDes = "R1", PartId = "resistor", Position = new GridPoint(10, 10) });
            s.Wires.Add(new SchematicWire { Points = { new GridPoint(40, 40), new GridPoint(50, 40) } });
            s.Labels.Add(new NetLabel { Name = "YALNIZ", Position = new GridPoint(70, 70) });
            return s;
        }

        [Fact]
        public void SeveritiesAndExclusionsChangeTheRunAndAreSaved()
        {
            var s = Sheet();
            var raw = ElectricalRulesChecker.Check(s, PartLibrary.Default);
            Assert.Contains(raw, v => v.Code == "wire-dangling");
            Assert.All(raw.Select(v => v.Code).Distinct(), code => Assert.NotNull(ErcRuleCatalog.Find(code)));

            ErcPolicy.SetSeverity(s, "wire-dangling", ErcRuleSeverity.Error);
            var r = ErcPolicy.Apply(s, raw);
            Assert.All(r.Shown.Where(v => v.Code == "wire-dangling"), v => Assert.Equal(ErcSeverity.Error, v.Severity));

            ErcPolicy.SetSeverity(s, "wire-dangling", ErcRuleSeverity.Ignore);
            r = ErcPolicy.Apply(s, raw);
            Assert.DoesNotContain(r.Shown, v => v.Code == "wire-dangling");
            Assert.Equal(raw.Count(v => v.Code == "wire-dangling"), r.Ignored["wire-dangling"]);

            var label = raw.First(v => v.Code == "label-unconnected");
            ErcPolicy.Exclude(s, label, "bilerek");
            r = ErcPolicy.Apply(s, raw);
            Assert.DoesNotContain(r.Shown, v => v.Code == "label-unconnected");
            Assert.Equal("bilerek", Assert.Single(r.Excluded).Exclusion.Comment);

            // the label moved: an exclusion is about the label, not its place
            s.Labels[0].Position = new GridPoint(90, 20);
            r = ErcPolicy.Apply(s, ElectricalRulesChecker.Check(s, PartLibrary.Default));
            Assert.DoesNotContain(r.Shown, v => v.Code == "label-unconnected");

            // saved and read back
            var back = Schematic.FromJson(s.ToJson());
            Assert.Equal(ErcRuleSeverity.Ignore, ErcPolicy.SeverityOf(back, "wire-dangling"));
            Assert.Equal("YALNIZ", Assert.Single(back.ErcExclusions).Net);
            Assert.Single(ErcPolicy.Apply(back, ElectricalRulesChecker.Check(back, PartLibrary.Default)).Excluded);

            // taken back, and the severity forgotten
            Assert.True(ErcPolicy.Include(back, ElectricalRulesChecker.Check(back, PartLibrary.Default).First(v => v.Code == "label-unconnected")));
            ErcPolicy.SetSeverity(back, "wire-dangling", ErcRuleSeverity.Default);
            Assert.Empty(back.ErcSeverities);
            r = ErcPolicy.Apply(back, ElectricalRulesChecker.Check(back, PartLibrary.Default));
            Assert.Contains(r.Shown, v => v.Code == "label-unconnected");
            Assert.Contains(r.Shown, v => v.Code == "wire-dangling" && v.Severity == ErcSeverity.Warning);
        }
    }
}
