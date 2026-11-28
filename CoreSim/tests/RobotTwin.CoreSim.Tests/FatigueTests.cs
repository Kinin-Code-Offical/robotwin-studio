using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analysis;
using RobotTwin.CoreSim.Analysis.Fem;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[fatigue] Stress-life fatigue: rainflow counting, the S-N line, Miner's sum.</summary>
    public class FatigueTests
    {
        /// <summary>ASTM E1049-85, figure 6: the example history and its counted ranges.</summary>
        [Fact]
        public void RainflowCountsTheAstmExample()
        {
            var cycles = Fatigue.Rainflow(new double[] { -2, 1, -3, 5, -1, 3, -4, 4, -2 });
            var byRange = cycles.GroupBy(c => c.Range).ToDictionary(g => g.Key, g => g.Sum(c => c.Count));
            Assert.Equal(0.5, byRange[3]);
            Assert.Equal(1.5, byRange[4]);
            Assert.Equal(0.5, byRange[6]);
            Assert.Equal(1.0, byRange[8]);
            Assert.Equal(0.5, byRange[9]);
            Assert.Equal(5, byRange.Count);
        }

        [Fact]
        public void TheSnLineRunsFromAThousandCyclesToTenMillion()
        {
            var steel = StructuralMaterial.For("ÇELİK");
            Assert.Equal(1e3, Fatigue.CyclesToFailure(steel, 0.9 * steel.Ultimate), 0);
            Assert.InRange(Fatigue.CyclesToFailure(steel, steel.FatigueStrength), 0.999e7, 1.001e7);
            Assert.True(double.IsPositiveInfinity(Fatigue.CyclesToFailure(steel, 0.9 * steel.FatigueStrength)));   // its endurance limit
            var aluminium = StructuralMaterial.For("ALÜMİNYUM");
            double below = Fatigue.CyclesToFailure(aluminium, 0.9 * aluminium.FatigueStrength);
            Assert.True(below > 1e7 && !double.IsInfinity(below));   // no endurance limit: the line goes on
        }

        /// <summary>Miner's sum of a part loaded and unloaded n times: n over the cycles to failure at that peak.</summary>
        [Fact]
        public void MinerSumsTheCyclesOfAPartsHistory()
        {
            var steel = StructuralMaterial.For("ÇELİK");
            double peak = 250e6;
            var r = new StressResult();
            for (int k = 0; k < 40; k++)
            {
                double v = k % 2 == 1 ? peak : 0;
                r.Frames.Add(new StressFrame { Time = k * 0.1, VonMises = new[] { (float)v, (float)(0.5 * v) } });
            }

            var f = Fatigue.Evaluate(steel, r);
            Assert.Equal(0, f.WorstNode);
            Assert.Equal(19.5, f.Cycles, 6);   // 20 loadings, the last one not taken off: 19 cycles and a half
            Assert.Equal(19.5 / Fatigue.CyclesToFailure(steel, peak), f.Damage, 9);
            Assert.Equal(3.9 / f.Damage, f.LifeSeconds, 3);
            Assert.Contains("Miner", Fatigue.Describe(steel, f));

            // under the endurance limit: no damage, endless life
            var light = new StressResult();
            for (int k = 0; k < 10; k++) light.Frames.Add(new StressFrame { Time = k, VonMises = new[] { k % 2 == 1 ? 100e6f : 0f } });
            var g = Fatigue.Evaluate(steel, light);
            Assert.Equal(0, g.Damage);
            Assert.Contains("sonsuz ömür", Fatigue.Describe(steel, g));
        }
    }
}
