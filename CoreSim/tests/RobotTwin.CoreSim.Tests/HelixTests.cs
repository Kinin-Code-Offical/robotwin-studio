using System;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[helix] The coil spring primitive: its wire's volume, its height, closed ends, and coils that never cut each other.</summary>
    public class HelixTests
    {
        private static double Section(double wire) => 8 * (wire / 2) * (wire / 2) * Math.Sin(2 * Math.PI / 16);   // the 16-sided wire

        [Fact]
        public void AnOpenCoilIsItsWireWoundOnTheHelix()
        {
            var m = PrimitiveBuilder.Helix(20, 2, 5, 6, closedEnds: false, segmentsPerTurn: 64);
            Assert.True(m.IsClosed);
            double length = 6 * Math.Sqrt(Math.Pow(Math.PI * 20, 2) + 25);
            Assert.InRange(m.Volume(), Section(2) * length * 0.98, Section(2) * length * 1.02);
            var b = m.Bounds;
            Assert.InRange(b.Max.Z - b.Min.Z, 30, 30 + 2.1);   // six turns of 5 mm, and the wire
            Assert.InRange(b.Max.X, 10.9, 11.01);   // the mean radius and half the wire
            Assert.Equal(0, (b.Max.Z + b.Min.Z) / 2, 1);   // centred
        }

        [Fact]
        public void ClosedEndsWindTheEndTurnsTight()
        {
            var open = PrimitiveBuilder.Helix(20, 2, 5, 8, closedEnds: false, segmentsPerTurn: 32).Bounds;
            var closed = PrimitiveBuilder.Helix(20, 2, 5, 8, closedEnds: true, segmentsPerTurn: 32).Bounds;
            double openHeight = open.Max.Z - open.Min.Z, closedHeight = closed.Max.Z - closed.Min.Z;
            double expected = openHeight - 2 * (5 - 2 * 1.02);   // each end turn rises by the wire, not the pitch
            Assert.InRange(closedHeight, expected - 0.05, expected + 0.05);
        }

        [Fact]
        public void APitchBelowTheWireIsOpenedUpAndTheSpecBuilds()
        {
            var b = PrimitiveBuilder.Helix(10, 2, 0.5, 4, closedEnds: false, segmentsPerTurn: 32).Bounds;
            Assert.True(b.Max.Z - b.Min.Z >= 4 * 2 * 1.05);   // the coils sit wire to wire at least
            var spec = PrimitiveSchema.Create(PrimitiveKind.Helix);
            var m = PrimitiveBuilder.Build(spec);
            Assert.False(m.IsEmpty);
            Assert.Equal("Helis (yay)", PrimitiveSchema.Name(PrimitiveKind.Helix));
            Assert.True(PrimitiveSchema.BottomOffset(spec) > 0);   // stands on the floor when added
        }
    }
}
