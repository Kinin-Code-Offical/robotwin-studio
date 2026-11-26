using System;
using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[diffroute] KiCad's differential pair router: both halves from one centreline, at the pair's gap, mitred corners.</summary>
    public class DiffPairRoutingTests
    {
        private static readonly DiffPair Usb = new DiffPair { Positive = "USB_DP", Negative = "USB_DN", Gap = 0.2 };

        private static double LineDistance(Track a, Track b)
        {
            // distance of b's ends from a's infinite line
            double dx = a.X2 - a.X1, dy = a.Y2 - a.Y1, l = Math.Sqrt(dx * dx + dy * dy);
            double D(double x, double y) => Math.Abs((x - a.X1) * dy - (y - a.Y1) * dx) / l;
            return (D(b.X1, b.Y1) + D(b.X2, b.Y2)) / 2;
        }

        [Fact]
        public void TheHalvesKeepThePairsPitchThroughACorner()
        {
            var board = new BoardDocument();
            var route = DiffPairRouting.Route(board, Usb, CopperLayer.Top, 0.2, (new PcbPoint(10, 10), new PcbPoint(10, 10.5)),
                new[] { new PcbPoint(20, 10.25), new PcbPoint(30, 20.25) });
            Assert.True(route.Clear, route.Problem);
            // fan-out, the straight leg, the 45° leg
            Assert.Equal(3, route.Positive.Count);
            Assert.Equal(3, route.Negative.Count);
            Assert.Equal(0.4, LineDistance(route.Positive[1], route.Negative[1]), 3);   // width 0.2 + gap 0.2, centre to centre
            Assert.Equal(0.4, LineDistance(route.Positive[2], route.Negative[2]), 3);   // ends kept to 0.1 µm
            Assert.True(route.Positive[1].Y1 < route.Negative[1].Y1);   // P stays on its pad's side
            Assert.All(route.Positive, t => Assert.Equal("USB_DP", t.Net));
            // the inner half of a 45° left turn is the shorter: P (outside) is longer by 2·half·tan(22.5°)
            Assert.Equal(2 * 0.2 * Math.Tan(Math.PI / 8) * 2, route.PositiveLength - route.NegativeLength, 2);
        }

        [Fact]
        public void EndsInOppositeOrderOrOtherCopperInTheWayAreFlagged()
        {
            var board = new BoardDocument();
            var start = (new PcbPoint(10, 10), new PcbPoint(10, 10.5));
            var path = new[] { new PcbPoint(20, 10.25) };
            var good = DiffPairRouting.Route(board, Usb, CopperLayer.Top, 0.2, start, path, (new PcbPoint(30, 10), new PcbPoint(30, 10.5)));
            Assert.True(good.Clear, good.Problem);
            Assert.Equal(good.PositiveLength, good.NegativeLength, 6);

            var swapped = DiffPairRouting.Route(board, Usb, CopperLayer.Top, 0.2, start, path, (new PcbPoint(30, 10.5), new PcbPoint(30, 10)));
            Assert.False(swapped.Clear);
            Assert.Contains("ters", swapped.Problem);

            board.Tracks.Add(new Track { Net = "GND", Layer = CopperLayer.Top, X1 = 15, Y1 = 5, X2 = 15, Y2 = 15, Width = 0.3 });
            var blocked = DiffPairRouting.Route(board, Usb, CopperLayer.Top, 0.2, start, path, (new PcbPoint(30, 10), new PcbPoint(30, 10.5)));
            Assert.False(blocked.Clear);
            Assert.True(DiffPairRouting.Route(board, Usb, CopperLayer.Bottom, 0.2, start, path, (new PcbPoint(30, 10), new PcbPoint(30, 10.5))).Clear);
        }

        [Fact]
        public void HalvesLeaveWidelySpacedPadsAt45Degrees()
        {
            // a 2.54 mm header at each end: the halves must not run between the pads
            var board = new BoardDocument();
            var route = DiffPairRouting.Route(board, Usb, CopperLayer.Top, 0.2, (new PcbPoint(0, 0), new PcbPoint(0, 2.54)),
                new[] { new PcbPoint(15, 1.27) }, (new PcbPoint(30, 0), new PcbPoint(30, 2.54)));
            var fan = route.Positive[0];
            Assert.Equal(Math.Abs(fan.X2 - fan.X1), Math.Abs(fan.Y2 - fan.Y1), 3);   // 45°
            static double SegDist(Track t, double x, double y)
            {
                double dx = t.X2 - t.X1, dy = t.Y2 - t.Y1, l2 = dx * dx + dy * dy;
                double k = Math.Max(0, Math.Min(1, ((x - t.X1) * dx + (y - t.Y1) * dy) / l2));
                return Math.Sqrt(Math.Pow(t.X1 + k * dx - x, 2) + Math.Pow(t.Y1 + k * dy - y, 2));
            }

            // D+ keeps away from the D− pads by more than half the pitch
            Assert.All(route.Positive, t => Assert.True(SegDist(t, 0, 2.54) > 1.5 && SegDist(t, 30, 2.54) > 1.5));
            Assert.Equal(route.PositiveLength, route.NegativeLength, 6);
        }

        [Fact]
        public void ANetFindsItsPair()
        {
            var board = new BoardDocument();
            board.DiffPairs.Add(Usb);
            Assert.Same(Usb, DiffPairRouting.PairOf(board, "usb_dn"));
            Assert.Null(DiffPairRouting.PairOf(board, "GND"));
        }
    }
}
