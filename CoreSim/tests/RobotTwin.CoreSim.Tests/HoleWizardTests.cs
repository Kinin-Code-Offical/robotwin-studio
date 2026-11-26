using System;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[holes] SolidWorks' Hole Wizard: holes made to the screw's standard figures, drilled along the surface's normal.</summary>
    public class HoleWizardTests
    {
        private static double Ngon(double r, int n = 40) => n / 2.0 * r * r * Math.Sin(2 * Math.PI / n);

        private static (ModelDocument Doc, ModelObject Block) Block()
        {
            var doc = new ModelDocument();
            var b = ModelOps.AddMesh(doc, PrimitiveBuilder.Box(20, 20, 20), "blok");
            return (doc, b);
        }

        [Fact]
        public void ThroughHolesTakeTheirClassesDiameter()
        {
            var (doc, b) = Block();
            HoleWizard.Drill(doc, b, new Vec3(0, 0, 10), Vec3.UnitZ, new DrillSpec { Size = "M3" });
            var m = b.LocalMesh();
            Assert.True(m.IsClosed);
            Assert.Equal(8000 - Ngon(1.7) * 20, m.Volume(), 0);   // Ø3.4 normal fit, all the way through
            Assert.Single(doc.Objects);   // the tools are gone

            // on a side face the hole runs along its normal
            HoleWizard.Drill(doc, b, new Vec3(10, 5, 5), Vec3.UnitX, new DrillSpec { Size = "M3", Fit = HoleFit.Close });
            Assert.Equal(8000 - Ngon(1.7) * 20 - Ngon(1.6) * 20, b.LocalMesh().Volume(), 0);
        }

        [Fact]
        public void CounterboreCountersinkAndInsertHolesAddTheirShapes()
        {
            var (doc, b) = Block();
            HoleWizard.Drill(doc, b, new Vec3(0, 0, 10), Vec3.UnitZ, new DrillSpec { Size = "M3", Type = HoleType.Counterbore });
            double bore = Ngon(1.7) * 20;
            double counterbore = (Ngon(3.0) - Ngon(1.7)) * 3.4;   // DIN 974-1: Ø6 × 3.4 for an M3 cap screw
            Assert.Equal(8000 - bore - counterbore, b.LocalMesh().Volume(), 0);

            (doc, b) = Block();
            HoleWizard.Drill(doc, b, new Vec3(0, 0, 10), Vec3.UnitZ, new DrillSpec { Size = "M3", Type = HoleType.Countersink });
            // 90°: from Ø6.9 at the surface down to the bore, 1.75 mm deep (a frustum less the bore inside it)
            double h = 1.75, R = 3.45, r = 1.7;
            double frustum = h / 3 * (Ngon(R) + Ngon(r) + Math.Sqrt(Ngon(R) * Ngon(r)));
            Assert.Equal(8000 - bore - (frustum - Ngon(r) * h), b.LocalMesh().Volume(), 0);

            (doc, b) = Block();
            HoleWizard.Drill(doc, b, new Vec3(0, 0, 10), Vec3.UnitZ, new DrillSpec { Size = "M3", Type = HoleType.Insert });
            Assert.Equal(8000 - Ngon(2.0) * 6.2, b.LocalMesh().Volume(), 0);   // Ø4 × 5.7 + 0.5 for the melt
            Assert.Contains("gömme somun", HoleWizard.Describe(new DrillSpec { Type = HoleType.Insert }));
        }
    }
}
