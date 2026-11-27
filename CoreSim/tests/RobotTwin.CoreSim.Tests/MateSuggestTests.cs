using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[mate-suggest] The relation two picked features most likely mean.</summary>
    public class MateSuggestTests
    {
        private static readonly Vec3 Up = Vec3.UnitZ, Down = -Vec3.UnitZ, Side = Vec3.UnitX;

        [Fact]
        public void FacesTurnedTowardsEachOtherRestOnEachOther() =>
            Assert.Equal(MateKind.Mate, WorldMates.Likely(MateFeatureKind.Plane, Up, MateFeatureKind.Plane, new Vec3(0.1, 0, -1)));

        [Fact]
        public void FacesTheSameWayAreFlushAndCrosswisePerpendicular()
        {
            Assert.Equal(MateKind.Flush, WorldMates.Likely(MateFeatureKind.Plane, Up, MateFeatureKind.Plane, Up));
            Assert.Equal(MateKind.Perpendicular, WorldMates.Likely(MateFeatureKind.Plane, Up, MateFeatureKind.Plane, Side));
            Assert.Equal(MateKind.Angle, WorldMates.Likely(MateFeatureKind.Plane, Up, MateFeatureKind.Plane, new Vec3(1, 0, 1)));
        }

        [Fact]
        public void AShaftInLineWithAHoleSharesItsAxis()
        {
            Assert.Equal(MateKind.Concentric, WorldMates.Likely(MateFeatureKind.Axis, Up, MateFeatureKind.Axis, Down));
            Assert.Equal(MateKind.Perpendicular, WorldMates.Likely(MateFeatureKind.Axis, Up, MateFeatureKind.Axis, Side));
        }

        [Fact]
        public void AShaftOnAFaceStandsOnItOrLiesAlongIt()
        {
            Assert.Equal(MateKind.Perpendicular, WorldMates.Likely(MateFeatureKind.Plane, Up, MateFeatureKind.Axis, Up));
            Assert.Equal(MateKind.Parallel, WorldMates.Likely(MateFeatureKind.Plane, Up, MateFeatureKind.Axis, Side));
            Assert.Null(WorldMates.Likely(MateFeatureKind.Point, Up, MateFeatureKind.Plane, Up));
        }

        [Fact]
        public void EverySuggestionFitsItsFeatures()
        {
            foreach (var ka in new[] { MateFeatureKind.Plane, MateFeatureKind.Axis })
                foreach (var kb in new[] { MateFeatureKind.Plane, MateFeatureKind.Axis })
                    foreach (var d in new[] { Up, Down, Side, new Vec3(1, 0, 1) })
                        if (WorldMates.Likely(ka, Up, kb, d) is { } k) Assert.True(WorldMates.Fits(k, ka, kb), $"{k} for {ka}/{kb}");
        }
    }
}
