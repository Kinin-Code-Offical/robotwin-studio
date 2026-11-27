using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[refgeom] Reference planes, axes and points: made as SolidWorks makes them, no solid, and taken by mirror and pattern.</summary>
    public class ModelReferenceTests
    {
        private static void Near(Vec3 expected, Vec3 actual, int digits = 6)
        {
            Assert.Equal(expected.X, actual.X, digits);
            Assert.Equal(expected.Y, actual.Y, digits);
            Assert.Equal(expected.Z, actual.Z, digits);
        }

        [Fact]
        public void APlaneThroughThreePointsHasNoSolidAndIsSaved()
        {
            var doc = new ModelDocument();
            var plane = ModelReferences.PlaneThrough(doc, new Vec3(0, 0, 10), new Vec3(30, 0, 10), new Vec3(0, 30, 10))!;
            var (p, n) = ModelReferences.Frame(doc, plane);
            Near(new Vec3(10, 10, 10), p);
            Near(Vec3.UnitZ, n);
            Assert.Equal("Düzlem1", plane.Name);
            Assert.True(doc.WorldMesh(plane).IsEmpty);
            Assert.Null(ModelReferences.PlaneThrough(doc, Vec3.Zero, new Vec3(1, 0, 0), new Vec3(2, 0, 0)));   // on a line

            var back = ModelDocument.FromJson(doc.ToJson());
            Assert.Equal(ReferenceKind.Plane, back.Objects.Single().Reference!.Kind);
            Assert.DoesNotContain("\"Reference\"", new ModelDocument { Objects = { new ModelObject { Id = "a" } } }.ToJson());
        }

        [Fact]
        public void OffsetMidAndAnglePlanes()
        {
            var doc = new ModelDocument();
            var floor = ModelReferences.Add(doc, ReferenceKind.Plane, Vec3.Zero, Vec3.UnitZ);
            var up = ModelReferences.PlaneOffset(doc, floor, 25);
            Near(new Vec3(0, 0, 25), ModelReferences.Frame(doc, up).Origin);
            var mid = ModelReferences.MidPlane(doc, floor, up)!;
            Near(new Vec3(0, 0, 12.5), ModelReferences.Frame(doc, mid).Origin);
            var xAxis = ModelReferences.Add(doc, ReferenceKind.Axis, Vec3.Zero, Vec3.UnitX);
            var tilted = ModelReferences.PlaneAtAngle(doc, floor, xAxis, 90);
            Near(new Vec3(0, -1, 0), ModelReferences.Frame(doc, tilted).Direction);   // the floor's normal turned 90° about X
            var square = ModelReferences.PlaneNormalTo(doc, xAxis, new Vec3(40, 7, 3));
            Near(new Vec3(40, 0, 0), ModelReferences.Frame(doc, square).Origin);
            Near(Vec3.UnitX, ModelReferences.Frame(doc, square).Direction);
        }

        [Fact]
        public void AxesWhereTwoPlanesMeetAndAlongARoundPart()
        {
            var doc = new ModelDocument();
            var front = ModelReferences.Add(doc, ReferenceKind.Plane, new Vec3(0, 5, 0), Vec3.UnitY);
            var right = ModelReferences.Add(doc, ReferenceKind.Plane, new Vec3(-3, 0, 0), Vec3.UnitX);
            var where = ModelReferences.AxisWhere(doc, front, right)!;
            var (p, d) = ModelReferences.Frame(doc, where);
            Assert.Equal(1, Math.Abs(d.Z), 6);
            Assert.Equal(-3, p.X, 6);
            Assert.Equal(5, p.Y, 6);
            Assert.Null(ModelReferences.AxisWhere(doc, front, ModelReferences.PlaneOffset(doc, front, 10)));

            var shaft = ModelOps.AddPrimitive(doc, PrimitiveKind.Cylinder, 20, 10);
            shaft.Transform.Rotation = new Vec3(90, 0, 0);
            var axis = ModelReferences.AxisOf(doc, shaft)!;
            var (ap, ad) = ModelReferences.Frame(doc, axis);
            Near(doc.WorldMatrix(shaft).ApplyVector(Vec3.UnitZ).Normalized(), ad);
            Near(doc.WorldMatrix(shaft).Apply(Vec3.Zero), ap);
            Assert.Null(ModelReferences.AxisOf(doc, ModelOps.AddPrimitive(doc, PrimitiveKind.Box)));
        }

        [Fact]
        public void MirrorAcrossATiltedPlaneAndPatternAboutAnAxis()
        {
            var doc = new ModelDocument();
            var box = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
            box.Transform.Position = new Vec3(20, 0, 0);
            var diagonal = ModelReferences.Add(doc, ReferenceKind.Plane, Vec3.Zero, new Vec3(1, -1, 0));
            var copy = ModelReferences.MirrorCopy(doc, new[] { box, diagonal }, diagonal).Single();   // the plane itself is not copied
            Near(new Vec3(0, 20, 0), doc.WorldMatrix(copy).Apply(Vec3.Zero));

            var axis = ModelReferences.Add(doc, ReferenceKind.Axis, new Vec3(10, 0, 0), Vec3.UnitZ);
            var ring = ModelReferences.ArrayCircular(doc, new[] { box }, 4, 360, axis);
            Assert.Equal(3, ring.Count);
            Near(new Vec3(10, 10, 0), doc.WorldMatrix(ring[0]).Apply(Vec3.Zero));
            Near(new Vec3(0, 0, 0), doc.WorldMatrix(ring[1]).Apply(Vec3.Zero));

            var centre = ModelReferences.PointAtCenter(doc, new[] { box, axis })!;
            Assert.Equal(ReferenceKind.Point, centre.Reference!.Kind);
            Near(new Vec3(20, 0, ModelOps.WorldBounds(doc, new[] { box }).Min.Z + (ModelOps.WorldBounds(doc, new[] { box }).Max.Z - ModelOps.WorldBounds(doc, new[] { box }).Min.Z) / 2), ModelReferences.Frame(doc, centre).Origin);
        }
    }
}
