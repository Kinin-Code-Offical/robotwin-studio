using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>Bisect, slice, loop cut, solidify and edge extrude of the modeller.</summary>
    public sealed class MeshCutTests
    {
        private static void Closed(PolyMesh m, string what)
        {
            var topo = m.Topology();
            Assert.True(topo.IsClosed, $"{what} is not closed: {topo}");
        }

        private static PolyMesh Box() => PrimitiveBuilder.RoundedBox(60, 40, 20, 0, 1, false);

        [Fact]
        public void BisectCutAddsALoopAndKeepsTheSolid()
        {
            var m = Box();
            double v0 = m.Volume();
            var c = m.Bounds.Center;
            var r = MeshCut.Bisect(m, c, Vec3.UnitZ);
            Closed(m, "cut box");
            Assert.Equal(10, m.Faces.Count);          // four side faces split in two
            Assert.Equal(4, r.Vertices.Count);         // the new edge loop
            Assert.All(r.Vertices, v => Assert.Equal(c.Z, m.Vertices[v].Z, 9));
            Assert.Equal(v0, m.Volume(), 6);
        }

        [Fact]
        public void BisectClearAndFillLeavesAClosedHalf()
        {
            var m = Box();
            double v0 = m.Volume();
            var c = m.Bounds.Center;
            MeshCut.Bisect(m, c, Vec3.UnitZ, null, BisectMode.ClearPositive, fill: true);
            Closed(m, "lower half");
            Assert.Equal(v0 / 2, m.Volume(), 6);
            Assert.All(m.Vertices, p => Assert.True(p.Z <= c.Z + 1e-9));
        }

        [Fact]
        public void ObliqueSliceOfASphereGivesTwoClosedPiecesOfTheWholeVolume()
        {
            var s = PrimitiveBuilder.Sphere(25, 32, 16);
            double v0 = s.Volume();
            var (a, b) = MeshCut.Slice(s, new Vec3(3, -2, 5), new Vec3(1, 2, 3));
            Closed(a, "front piece");
            Closed(b, "back piece");
            Assert.Equal(v0, a.Volume() + b.Volume(), 6);
            Assert.True(a.Volume() > 0 && b.Volume() > 0);
        }

        [Fact]
        public void SlicingATubeKeepsTheBoreOpen()
        {
            var t = PrimitiveBuilder.Tube(20, 12, 50, 32);
            double v0 = t.Volume();
            var c = t.Bounds.Center;
            var (top, bottom) = MeshCut.Slice(t, c, Vec3.UnitZ);
            Closed(top, "top");
            Closed(bottom, "bottom");
            Assert.Equal(v0 / 2, top.Volume(), 4);
            Assert.Equal(v0 / 2, bottom.Volume(), 4);
            // a filled bore would add the inner disc: π·12²·25 ≈ 11 310 mm³
            Assert.True(Math.Abs(bottom.Volume() - v0 / 2) < 1);
        }

        [Fact]
        public void ConcaveFacesCrossedSeveralTimesAreSplitAlongChords()
        {
            // a U-shaped prism: its top and bottom faces are crossed four times by a plane across both arms
            var u = new List<Vec2>
            {
                new Vec2(0, 0), new Vec2(30, 0), new Vec2(30, 40), new Vec2(20, 40),
                new Vec2(20, 10), new Vec2(10, 10), new Vec2(10, 40), new Vec2(0, 40)
            };
            var m = PrimitiveBuilder.Extrude(u, new List<List<Vec2>>(), 8);
            double v0 = m.Volume();
            var (a, b) = MeshCut.Slice(m, new Vec3(0, 25, 0), Vec3.UnitY);
            Closed(a, "two arm tips");
            Closed(b, "base");
            Assert.Equal(v0, a.Volume() + b.Volume(), 6);
            Assert.Equal(2 * 10 * 15 * 8, a.Volume(), 6);
        }

        [Fact]
        public void KnifeOnSelectedFacesKeepsNeighboursWatertight()
        {
            var m = Box();
            int top = Enumerable.Range(0, m.Faces.Count).First(f => m.FaceNormal(f).Z > 0.9);
            var c = m.Bounds.Center;
            MeshCut.Bisect(m, c, Vec3.UnitX, new[] { top });
            Closed(m, "knifed box");
            Assert.Equal(7, m.Faces.Count);           // only the top face split
        }

        [Fact]
        public void LoopCutSplitsTheRingOfQuads()
        {
            var m = Box();
            double v0 = m.Volume();
            int top = Enumerable.Range(0, m.Faces.Count).First(f => m.FaceNormal(f).Z > 0.9);
            var f = m.Faces[top];
            var (edges, faces) = MeshCut.EdgeRing(m, f[0], f[1]);
            Assert.Equal(4, faces.Count);
            Assert.Equal(4, edges.Count);
            var r = MeshCut.LoopCut(m, f[0], f[1], 3);
            Closed(m, "loop cut box");
            Assert.Equal(6 + 4 * 3, m.Faces.Count);
            Assert.Equal(12, r.Vertices.Count);
            Assert.Equal(v0, m.Volume(), 6);
        }

        [Fact]
        public void SolidifyGivesAnOpenBoxEvenWalls()
        {
            var m = Box();
            int top = Enumerable.Range(0, m.Faces.Count).First(f => m.FaceNormal(f).Z > 0.9);
            MeshEdit.DeleteFaces(m, new[] { top });
            MeshCut.Solidify(m, 2);
            Closed(m, "tray");
            Assert.Equal(60 * 40 * 20 - 56 * 36 * 18, m.Volume(), 4);
        }

        [Fact]
        public void SolidifyHollowsAClosedBody()
        {
            var m = Box();
            MeshCut.Solidify(m, 2);
            Closed(m, "hollow box");
            Assert.Equal(60 * 40 * 20 - 56 * 36 * 16, m.Volume(), 4);
        }

        [Fact]
        public void BevelOfOneEdgeCutsAChamferPrism()
        {
            var m = Box();
            // the top edge running along X at the front (y = -20)
            var e = m.Edges().First(x =>
            {
                var a = m.Vertices[x.A];
                var b = m.Vertices[x.B];
                return a.Z > 9 && b.Z > 9 && a.Y < -19 && b.Y < -19;
            });
            var r = MeshCut.Bevel(m, new[] { e }, 3);
            Closed(m, "one chamfer");
            Assert.Equal(7, m.Faces.Count);
            Assert.Single(r.Faces);
            Assert.Equal(60 * 40 * 20 - 3 * 3 / 2.0 * 60, m.Volume(), 6);
        }

        [Fact]
        public void BevelOfAllEdgesAddsCornerPatches()
        {
            var m = Box();
            double v0 = m.Volume();
            MeshCut.Bevel(m, m.Edges(), 2);
            Closed(m, "chamfered box");
            Assert.Equal(6 + 12 + 8, m.Faces.Count);
            // all twelve chamfer prisms, less their overlaps at the eight corners
            double prisms = 2 * 2 / 2.0 * 4 * (60 + 40 + 20);
            Assert.True(m.Volume() < v0 - prisms + 8 * 8 && m.Volume() > v0 - prisms, "volume " + m.Volume());
        }

        [Fact]
        public void BevelOfTwoEdgesMeetingAtACornerStaysClosed()
        {
            var m = Box();
            var top = m.Edges().Where(x => m.Vertices[x.A].Z > 9 && m.Vertices[x.B].Z > 9).ToList();
            var corner = top.First();
            var pair = top.Where(x => x.A == corner.A || x.B == corner.A).Take(2).ToList();
            Assert.Equal(2, pair.Count);
            MeshCut.Bevel(m, pair, 4);
            Closed(m, "two chamfers");
            Assert.Equal(8, m.Faces.Count);
        }

        [Fact]
        public void LoopSelectionFollowsTheLoopCut()
        {
            var m = Box();
            int top = Enumerable.Range(0, m.Faces.Count).First(f => m.FaceNormal(f).Z > 0.9);
            var f = m.Faces[top];
            var cut = MeshCut.LoopCut(m, f[0], f[1], 1);
            // the new loop runs round the box: four vertices with four edges each
            var a = cut.Vertices[0];
            int b = m.Edges().Where(e => e.A == a || e.B == a).Select(e => e.A == a ? e.B : e.A).First(cut.Vertices.Contains);
            var loop = MeshSelect.EdgeLoop(m, a, b);
            Assert.Equal(4, loop.Count);
            Assert.True(loop.All(cut.Vertices.Contains));
            var fresh = Box();
            var ff = fresh.Faces[top];
            Assert.Equal(4, MeshSelect.FaceLoop(fresh, ff[0], ff[1]).Count);
        }

        [Fact]
        public void LinkedGrowAndShrinkWorkOnPieces()
        {
            var m = Box();
            var other = Box().Translate(new Vec3(100, 0, 0));
            m.Append(other);
            var linked = MeshSelect.Linked(m, new[] { 0 });
            Assert.Equal(8, linked.Count);
            var one = new HashSet<int> { 0 };
            var grown = MeshSelect.Grow(m, one);
            Assert.Equal(7, grown.Count);               // a box corner touches three faces: all but the far corner
            Assert.Contains(0, MeshSelect.Shrink(m, grown));    // shrinking the grown set keeps the start corner
            Assert.Empty(MeshSelect.Shrink(m, one));
        }

        [Fact]
        public void EdgeExtrudeGrowsAnOpenBorder()
        {
            var m = new PolyMesh();
            int a = m.AddVertex(new Vec3(0, 0, 0)), b = m.AddVertex(new Vec3(10, 0, 0));
            int c = m.AddVertex(new Vec3(10, 10, 0)), d = m.AddVertex(new Vec3(0, 10, 0));
            m.AddFace(a, b, c, d);
            var r = MeshCut.ExtrudeEdges(m, new[] { (c, d) });
            Assert.Equal(2, r.Vertices.Count);
            foreach (int v in r.Vertices) m.Vertices[v] += new Vec3(0, 10, 0);
            Assert.Equal(2, m.Faces.Count);
            // the new quad faces the same way as the first one
            Assert.True(m.FaceNormal(1).Z > 0.99);
            Assert.Equal(200, m.FaceArea(0) + m.FaceArea(1), 9);
        }
    }
}
