using System;
using RobotTwin.CoreSim.Pcb;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[impedance] Track impedance from the stackup against textbook and fab-calculator values.</summary>
    public class ImpedanceTests
    {
        private readonly ITestOutputHelper _out;

        public ImpedanceTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void MicrostripMatchesTextbookDesigns()
        {
            // Pozar, Microwave Engineering, example 3.7: 50 Ω on εr 2.2, 1.27 mm thick: W = 3.91 mm, εeff 1.87
            var (z, ee) = Impedance.Microstrip(3.91, 1.27, 0, 2.2);
            _out.WriteLine($"Pozar 3.7: {z:0.00} Ω εeff {ee:0.000}");
            Assert.InRange(z, 49, 51);
            Assert.InRange(ee, 1.83, 1.91);

            // the classic FR4 board: εr 4.4, 1.6 mm, 1 oz: 50 Ω is about 3 mm wide
            var (fr4, fr4e) = Impedance.Microstrip(3.0, 1.6, 0.035, 4.4);
            _out.WriteLine($"FR4 3.0 mm: {fr4:0.00} Ω εeff {fr4e:0.000}");
            Assert.InRange(fr4, 48, 52);
            Assert.InRange(fr4e, 3.2, 3.5);

            // narrower is higher, thicker copper is lower
            Assert.True(Impedance.Microstrip(0.25, 1.6, 0.035, 4.4).Z0 > 120);
            Assert.True(Impedance.Microstrip(1, 1.6, 0.07, 4.4).Z0 < Impedance.Microstrip(1, 1.6, 0.0175, 4.4).Z0);
        }

        [Fact]
        public void StriplineMatchesTextbookDesigns()
        {
            // Pozar example 3.5: 50 Ω between planes 3.2 mm apart, εr 2.2: W = 2.66 mm
            double z = Impedance.Stripline(2.66, 3.2, 0, 2.2);
            _out.WriteLine($"Pozar 3.5: {z:0.00} Ω");
            Assert.InRange(z, 48.5, 51.5);

            // Wheeler's thin-strip limit and the copper's thickness lowering it
            Assert.True(Impedance.Stripline(0.2, 0.5, 0.035, 4.2) < Impedance.Stripline(0.2, 0.5, 0, 4.2));

            // an offset stripline with equal sides is the symmetric one; moving toward a plane lowers it
            double sym = Impedance.Stripline(0.2, 2 * 0.2 + 0.0175, 0.0175, 4.2);
            Assert.Equal(sym, Impedance.OffsetStripline(0.2, 0.2, 0.2, 0.0175, 4.2), 9);
            Assert.True(Impedance.OffsetStripline(0.2, 0.1, 0.3, 0.0175, 4.2) < sym);
        }

        [Fact]
        public void BoardTracksUseTheirLayersDielectrics()
        {
            var two = new BoardDocument();
            var top = Impedance.OfTrack(two, CopperLayer.Top, 3.0);
            _out.WriteLine($"2 layers, 3 mm: {Impedance.Describe(top)} h {top.Height:0.###}");
            Assert.False(top.Stripline);
            Assert.InRange(top.Height, 1.5, 1.6);
            Assert.InRange(top.Z0, 46, 52);
            Assert.Equal(top.Z0, Impedance.OfTrack(two, CopperLayer.Bottom, 3.0).Z0, 9);

            var four = new BoardDocument();
            four.Stackup.CopperLayers = 4;
            var outer = Impedance.OfTrack(four, CopperLayer.Top, 0.35);
            var inner = Impedance.OfTrack(four, CopperLayer.In1, 0.2);
            _out.WriteLine($"4 layers: top 0.35 mm {Impedance.Describe(outer)} h {outer.Height:0.###}; In1 0.2 mm {Impedance.Describe(inner)} {inner.Height:0.###}/{inner.HeightOther:0.###}");
            Assert.False(outer.Stripline);
            Assert.Equal(four.Stackup.PrepregThickness, outer.Height, 6);   // over the prepreg to In1, not the whole board
            Assert.InRange(outer.Z0, 45, 60);
            Assert.True(inner.Stripline);
            Assert.InRange(inner.Z0, 30, 70);

            // the width solver gives back its target on any layer
            foreach (var layer in new[] { CopperLayer.Top, CopperLayer.In1, CopperLayer.In2, CopperLayer.Bottom })
            {
                double w = Impedance.WidthFor(four, layer, 50);
                Assert.Equal(50, Impedance.OfTrack(four, layer, w).Z0, 2);
            }
        }

        [Fact]
        public void PairsCoupleCloserForLowerDifferentialImpedance()
        {
            var board = new BoardDocument();
            board.Stackup.CopperLayers = 4;
            var line = Impedance.OfTrack(board, CopperLayer.Top, 0.15);
            double close = Impedance.Differential(line.Z0, 0.1, line), far = Impedance.Differential(line.Z0, 5, line);
            _out.WriteLine($"Z0 {line.Z0:0.0}: gap 0.1 → {close:0.0} Ω, gap 5 → {far:0.0} Ω");
            Assert.True(close < far);
            Assert.Equal(2 * line.Z0, far, 1);   // far apart: two independent lines
            Assert.InRange(close, 1.2 * line.Z0, 2 * line.Z0);

            board.DiffPairs.Add(new DiffPair { Positive = "USB_D+", Negative = "USB_D-", Gap = 0.15 });
            string summary = Impedance.Summary(board);
            _out.WriteLine(summary);
            Assert.Contains("dış", summary);
            Assert.Contains("iç", summary);
            Assert.Contains("fark", summary);
        }

        [Fact]
        public void TargetsAreHeldByDrcAndTheFabReadiness()
        {
            var board = new BoardDocument();
            board.Stackup.CopperLayers = 4;
            Assert.Equal(ReadinessState.Pass, Impedance.Readiness(board).State);   // no target: the computed values, said
            Assert.Contains("istenmedi", Impedance.Readiness(board).Detail);

            var rf = new NetClass { Name = "RF", Impedance = 50, TrackWidth = Impedance.WidthFor(board, CopperLayer.Top, 50) };
            board.NetClasses.Add(rf);
            board.AssignClass("ANT", "RF");
            board.Tracks.Add(new Track { Net = "ANT", Layer = CopperLayer.Top, X1 = 5, Y1 = 5, X2 = 25, Y2 = 5, Width = rf.TrackWidth });
            Assert.Empty(Impedance.Check(board));
            Assert.Equal(ReadinessState.Pass, Impedance.Readiness(board).State);

            // necked down between two pads: 0.15 mm on the outer layer is far above 50 Ω
            board.Tracks.Add(new Track { Net = "ANT", Layer = CopperLayer.Top, X1 = 25, Y1 = 5, X2 = 30, Y2 = 5, Width = 0.15 });
            board.Tracks.Add(new Track { Net = "ANT", Layer = CopperLayer.Top, X1 = 30, Y1 = 5, X2 = 35, Y2 = 5, Width = 0.15 });
            var issues = Impedance.Check(board);
            foreach (var i in issues) _out.WriteLine(i.Message);
            Assert.Single(issues);   // one per net, layer and width
            Assert.Equal("Empedans", issues[0].Rule);
            Assert.Equal("ANT", issues[0].Net);
            Assert.Equal(ReadinessState.Warning, Impedance.Readiness(board).State);
            Assert.Contains(DesignRuleChecker.Check(board), v => v.Rule == "Empedans");

            // a USB pair at 90 Ω: the gap solver hits it, a wide gap misses it
            board.Tracks.RemoveAll(t => t.Net == "ANT");
            var usb = new DiffPair { Positive = "USB_D+", Negative = "USB_D-", Impedance = 90 };
            board.DiffPairs.Add(usb);
            usb.Gap = Impedance.GapFor(board, CopperLayer.Top, 0.2, 90);
            _out.WriteLine($"USB 90 Ω at 0.2 mm: gap {usb.Gap:0.###} mm");
            Assert.False(double.IsNaN(usb.Gap));
            board.Tracks.Add(new Track { Net = "USB_D+", Layer = CopperLayer.Top, X1 = 5, Y1 = 10, X2 = 25, Y2 = 10, Width = 0.2 });
            Assert.Empty(Impedance.Check(board));
            usb.Gap = 3;
            var miss = Assert.Single(Impedance.Check(board));
            _out.WriteLine(miss.Message);
            Assert.Equal("Fark empedansı", miss.Rule);

            // no gap reaches 100 Ω with 1 mm tracks (2·Z0 is the ceiling): the message gives a width instead
            Assert.True(double.IsNaN(Impedance.GapFor(board, CopperLayer.Top, 1.0, 100)));
            double w = Impedance.PairWidthFor(board, CopperLayer.Top, 0.2, 100);
            var l = Impedance.OfTrack(board, CopperLayer.Top, w);
            Assert.Equal(100, Impedance.Differential(l.Z0, 0.2, l), 1);
        }
    }
}
