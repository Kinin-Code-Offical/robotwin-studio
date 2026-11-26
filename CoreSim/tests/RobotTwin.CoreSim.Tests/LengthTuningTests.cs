using System;
using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[tuning] Meanders that bring a pair to equal length, keeping clearance; propagation delay from the stackup.</summary>
    public class LengthTuningTests
    {
        /// <summary>A USB pair on the top layer: D+ 40 mm straight, D− 46 mm with a jog.</summary>
        private static BoardDocument Pair()
        {
            var b = new BoardDocument { Width = 80, Height = 40 };
            b.Tracks.Add(new Track { Net = "D+", Layer = CopperLayer.Top, X1 = 10, Y1 = 15, X2 = 50, Y2 = 15, Width = 0.2 });
            b.Tracks.Add(new Track { Net = "D-", Layer = CopperLayer.Top, X1 = 10, Y1 = 25, X2 = 50, Y2 = 25, Width = 0.2 });
            b.Tracks.Add(new Track { Net = "D-", Layer = CopperLayer.Top, X1 = 50, Y1 = 25, X2 = 50, Y2 = 31, Width = 0.2 });
            b.DiffPairs.Add(new DiffPair { Positive = "D+", Negative = "D-", MaxSkew = 0.5 });
            return b;
        }

        [Fact]
        public void ACoupledPairsSkewIsTunedWithSmallBumpsAwayFromThePartner()
        {
            // [skew] D+ and D− run 0.4 mm apart (0.2 mm gap); D− is 0.8 mm longer by a short jog at its end
            var b = new BoardDocument { Width = 80, Height = 40 };
            b.Tracks.Add(new Track { Net = "D+", Layer = CopperLayer.Top, X1 = 10, Y1 = 15, X2 = 40, Y2 = 15, Width = 0.2 });
            b.Tracks.Add(new Track { Net = "D-", Layer = CopperLayer.Top, X1 = 10, Y1 = 15.4, X2 = 40, Y2 = 15.4, Width = 0.2 });
            b.Tracks.Add(new Track { Net = "D-", Layer = CopperLayer.Top, X1 = 40, Y1 = 15.4, X2 = 40, Y2 = 16.2, Width = 0.2 });
            b.DiffPairs.Add(new DiffPair { Positive = "D+", Negative = "D-", Gap = 0.2, MaxSkew = 0.1 });
            var seg = b.Tracks[0];
            var r = LengthTuning.Tune(b, seg);
            Assert.True(r.Ok, r.Message);
            Assert.Equal(30.8, r.After, 1);
            Assert.Contains("eğiklik", r.Message);
            Assert.All(r.Replacement!, t =>
            {
                Assert.True(t.Y1 <= 15 + 1e-9 && t.Y2 <= 15 + 1e-9, "a bump went towards the partner");   // the side away from D−
                Assert.True(t.Y1 >= 15 - 1.0 - 1e-9 && t.Y2 >= 15 - 1.0 - 1e-9, "bumps are small for a pair");
            });
        }

        [Fact]
        public void MeandersBringTheShortHalfOfAPairToTheLongOne()
        {
            var b = Pair();
            var seg = b.Tracks[0];
            var r = LengthTuning.Tune(b, seg);
            Assert.True(r.Ok, r.Message);
            Assert.Equal(40, r.Before, 6);
            Assert.Equal(46, r.Target, 6);
            Assert.Equal(46, r.After, 2);
            LengthTuning.Apply(b, seg, r);
            var lengths = NetLengths.Measure(b);
            Assert.Equal(lengths["D-"], lengths["D+"], 2);
            // the meandered net still starts and ends where it did, in one connected chain
            var d = b.Tracks.Where(t => t.Net == "D+").ToList();
            Assert.Equal(10, d.First().X1, 9);
            Assert.Equal(50, d.Last().X2, 9);
            for (int i = 0; i + 1 < d.Count; i++)
            {
                Assert.Equal(d[i].X2, d[i + 1].X1, 9);
                Assert.Equal(d[i].Y2, d[i + 1].Y1, 9);
            }

            // no 90° copper corners: chamfered
            Assert.DoesNotContain(NetLengths.Check(b), v => v.Rule == "Fark çifti uzunluğu");
            Assert.DoesNotContain(DesignRuleChecker.Check(b), v => v.Rule.Contains("aralığı") || v.Rule == "Kısa devre");
        }

        [Fact]
        public void TheMeandersKeepAwayFromOtherCopperAndSayWhenTheyCannot()
        {
            var b = Pair();
            // a neighbour 0.6 mm above D+: the bumps go to the other side
            b.Tracks.Add(new Track { Net = "X", Layer = CopperLayer.Top, X1 = 10, Y1 = 14.4, X2 = 50, Y2 = 14.4, Width = 0.2 });
            var r = LengthTuning.Tune(b, b.Tracks[0]);
            Assert.True(r.Ok, r.Message);
            Assert.All(r.Replacement!, t => Assert.True(Math.Min(t.Y1, t.Y2) >= 15 - 1e-9));

            // fenced on both sides: refused, with the reason
            b.Tracks.Add(new Track { Net = "Y", Layer = CopperLayer.Top, X1 = 10, Y1 = 15.6, X2 = 50, Y2 = 15.6, Width = 0.2 });
            r = LengthTuning.Tune(b, b.Tracks[0]);
            Assert.False(r.Ok);
            Assert.Contains("yoluna çok yakın", r.Message);

            // a net in no pair or group needs a target
            var c = new BoardDocument { Width = 50, Height = 30 };
            c.Tracks.Add(new Track { Net = "CLK", Layer = CopperLayer.Top, X1 = 5, Y1 = 10, X2 = 45, Y2 = 10, Width = 0.2 });
            Assert.False(LengthTuning.Tune(c, c.Tracks[0]).Ok);
            var given = LengthTuning.Tune(c, c.Tracks[0], 44);
            Assert.True(given.Ok, given.Message);
            Assert.Equal(44, given.After, 2);
        }

        [Fact]
        public void DelayFollowsTheDielectric()
        {
            var b = Pair();
            // stripline in FR4 (εr 4.5): √4.5 × 3.336 ps/mm ≈ 7.08 ps/mm; a top-layer microstrip is faster (εeff < εr)
            Assert.Equal(Math.Sqrt(4.5) * LengthTuning.VacuumPsPerMm, LengthTuning.PsPerMm(b, CopperLayer.In1, 0.2), 9);
            double micro = LengthTuning.PsPerMm(b, CopperLayer.Top, 0.2);
            Assert.InRange(micro, LengthTuning.VacuumPsPerMm * Math.Sqrt(2.75), LengthTuning.VacuumPsPerMm * Math.Sqrt(4.5));
            Assert.InRange(LengthTuning.EffectivePermittivity(b, CopperLayer.Top, 0.2), 2.75, 3.6);   // 0.2 mm on 1.5 mm FR4
            Assert.Equal(40 * micro, LengthTuning.Delay(b, "D+"), 6);
            double skew = LengthTuning.Delay(b, "D-") - LengthTuning.Delay(b, "D+");
            Assert.Equal(6 * micro, skew, 6);
        }
    }
}
