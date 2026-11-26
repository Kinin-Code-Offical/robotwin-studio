using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using RobotTwin.CoreSim.Sandbox;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    public sealed class ModelingTests
    {
        private readonly ITestOutputHelper _out;

        public ModelingTests(ITestOutputHelper output) => _out = output;

        private static void Rel(double expected, double actual, double tol, string what = "")
        {
            double err = Math.Abs(actual - expected) / Math.Max(Math.Abs(expected), 1e-300);
            Assert.True(err <= tol, $"{what}: expected {expected:G9}, got {actual:G9} (rel err {err:E2} > {tol:E2})");
        }

        private static void Closed(PolyMesh m, string what)
        {
            var topo = m.Topology();
            Assert.True(topo.IsClosed, $"{what} is not closed: {topo}");
            Assert.True(m.Volume() > 0, $"{what} has inward faces (volume {m.Volume()})");
        }

        private static PolyMesh Cube(double size, double x = 0, double y = 0, double z = 0) => PrimitiveBuilder.Box(size, size, size).Translate(new Vec3(x, y, z));

        /// <summary>Area of a regular n-gon with circumradius r.</summary>
        private static double NgonArea(double r, int n) => n / 2.0 * r * r * Math.Sin(2 * Math.PI / n);

        // ------------------------------------------------------------------ primitives

        [Fact]
        public void PrimitivesAreClosedWithExpectedVolumes()
        {
            var box = PrimitiveBuilder.Box(10, 20, 30);
            Closed(box, "box");
            Rel(6000, box.Volume(), 1e-12, "box");
            Assert.Equal(6, box.Faces.Count);

            var cyl = PrimitiveBuilder.Cylinder(5, 10, 64);
            Closed(cyl, "cylinder");
            Rel(NgonArea(5, 64) * 10, cyl.Volume(), 1e-12, "cylinder");

            var cone = PrimitiveBuilder.Cone(5, 0, 12, 64);
            Closed(cone, "cone");
            Rel(NgonArea(5, 64) * 12 / 3, cone.Volume(), 1e-12, "cone");

            var frustum = PrimitiveBuilder.Cone(6, 3, 10, 128);
            Closed(frustum, "frustum");
            Rel(Math.PI * 10 / 3 * (36 + 18 + 9), frustum.Volume(), 2e-3, "frustum");

            var sphere = PrimitiveBuilder.Sphere(10, 96, 48);
            Closed(sphere, "sphere");
            Rel(4.0 / 3 * Math.PI * 1000, sphere.Volume(), 6e-3, "sphere");

            var torus = PrimitiveBuilder.Torus(20, 5, 96, 48);
            Closed(torus, "torus");
            Rel(2 * Math.PI * Math.PI * 20 * 25, torus.Volume(), 4e-3, "torus");

            var tube = PrimitiveBuilder.Tube(10, 7, 20, 64);
            Closed(tube, "tube");
            Rel((NgonArea(10, 64) - NgonArea(7, 64)) * 20, tube.Volume(), 1e-12, "tube");

            var wedge = PrimitiveBuilder.Wedge(10, 20, 6);
            Closed(wedge, "wedge");
            Rel(10 * 20 * 6 / 2.0, wedge.Volume(), 1e-12, "wedge");
        }

        [Fact]
        public void RoundedAndChamferedBoxes()
        {
            double a = 30, b = 20, c = 10, r = 3;
            var rounded = PrimitiveBuilder.RoundedBox(a, b, c, r, 8, false);
            Closed(rounded, "rounded box");
            double ai = a - 2 * r, bi = b - 2 * r, ci = c - 2 * r;
            double exact = ai * bi * ci + 2 * (ai * bi + bi * ci + ci * ai) * r + Math.PI * r * r * (ai + bi + ci) + 4.0 / 3 * Math.PI * r * r * r;
            Rel(exact, rounded.Volume(), 6e-3, "rounded box");

            var chamfered = PrimitiveBuilder.RoundedBox(a, b, c, r, 1, true);
            Closed(chamfered, "chamfered box");
            // Minkowski sum of the inner box with an octahedron of radius r
            double chamferExact = ai * bi * ci + 2 * r * (ai * bi + bi * ci + ci * ai) + 2 * r * r * (ai + bi + ci) + 4.0 / 3 * r * r * r;
            Rel(chamferExact, chamfered.Volume(), 1e-9, "chamfered box");
            Assert.True(chamfered.Faces.Count <= 26, $"chamfer faces: {chamfered.Faces.Count}");
        }

        [Fact]
        public void ExtrudedSketchWithHolesAndPlate()
        {
            var spec = PrimitiveSchema.Create(PrimitiveKind.Extrude);
            spec.Sketch = new Sketch2D { Outer = SketchShape.Rect(40, 20) };
            spec.Sketch.Holes.Add(SketchShape.Circle(10));
            spec.Set("h", 5);
            spec.Set("seg", 48);
            var m = PrimitiveBuilder.Build(spec);
            Closed(m, "extrude");
            Rel((800 - NgonArea(5, 48)) * 5, m.Volume(), 1e-9, "extrude with hole");

            var plate = PrimitiveSchema.Create(PrimitiveKind.Plate);
            plate.Holes!.Add(new HoleSpec { X = 0, Y = 0, D = 8 });
            var pm = PrimitiveBuilder.Build(plate);
            Closed(pm, "plate");
            Assert.True(pm.Volume() < 80 * 50 * 3);

            var slot = PrimitiveSchema.Create(PrimitiveKind.Extrude);
            slot.Sketch = new Sketch2D { Outer = SketchShape.Slot(30, 10) };
            slot.Sketch.Holes.Add(SketchShape.Ngon(6, 3));
            Closed(PrimitiveBuilder.Build(slot), "slot with hexagon");
        }

        [Fact]
        public void RevolveFullAndPartial()
        {
            // rectangle r 5..10, z 0..4 revolved = tube
            var profile = new List<Vec2> { new Vec2(5, 0), new Vec2(10, 0), new Vec2(10, 4), new Vec2(5, 4) };
            var full = PrimitiveBuilder.Revolve(profile, 360, 64);
            Closed(full, "revolve 360");
            Rel((NgonArea(10, 64) - NgonArea(5, 64)) * 4, full.Volume(), 1e-9, "revolve 360");

            var half = PrimitiveBuilder.Revolve(profile, 180, 64);
            Closed(half, "revolve 180");
            Rel((NgonArea(10, 64) - NgonArea(5, 64)) * 4 / 2, half.Volume(), 1e-9, "revolve 180");

            // profile touching the axis → solid of revolution (cylinder)
            var solid = PrimitiveBuilder.Revolve(new List<Vec2> { new Vec2(0, 0), new Vec2(5, 0), new Vec2(5, 10), new Vec2(0, 10) }, 360, 48);
            Closed(solid, "revolve solid");
            Rel(NgonArea(5, 48) * 10, solid.Volume(), 1e-9, "revolve solid");
        }

        // ------------------------------------------------------------------ booleans

        [Fact]
        public void CubeBooleansMatchAnalyticVolumes()
        {
            var a = Cube(2);
            var b = Cube(2, 1, 1, 1);
            var u = Csg.Union(a, b);
            var d = Csg.Difference(a, b);
            var i = Csg.Intersection(a, b);
            Closed(u, "union");
            Closed(d, "difference");
            Closed(i, "intersection");
            Rel(15, u.Volume(), 1e-9, "union");
            Rel(7, d.Volume(), 1e-9, "difference");
            Rel(1, i.Volume(), 1e-9, "intersection");
            Assert.Equal(6, i.Faces.Count);
        }

        [Fact]
        public void CoplanarBooleans()
        {
            // touching faces
            var u = Csg.Union(Cube(1, 0.5, 0.5, 0.5), Cube(1, 1.5, 0.5, 0.5));
            Closed(u, "face-touching union");
            Rel(2, u.Volume(), 1e-9, "face-touching union");
            Assert.Equal(6, u.Faces.Count);

            // overlapping boxes sharing top/bottom/side planes
            var a = PrimitiveBuilder.Box(2, 1, 1).Translate(new Vec3(1, 0.5, 0.5));
            var b = PrimitiveBuilder.Box(2, 1, 1).Translate(new Vec3(2, 0.5, 0.5));
            var u2 = Csg.Union(a, b);
            Closed(u2, "coplanar union");
            Rel(3, u2.Volume(), 1e-9, "coplanar union");
            Assert.Equal(6, u2.Faces.Count);
            var d2 = Csg.Difference(a, b);
            Closed(d2, "coplanar difference");
            Rel(1, d2.Volume(), 1e-9, "coplanar difference");
            var i2 = Csg.Intersection(a, b);
            Closed(i2, "coplanar intersection");
            Rel(1, i2.Volume(), 1e-9, "coplanar intersection");

            // pocket flush with the top face
            var block = PrimitiveBuilder.Box(10, 10, 10);
            var pocket = PrimitiveBuilder.Box(4, 4, 5).Translate(new Vec3(0, 0, 2.5));
            var p = Csg.Difference(block, pocket);
            Closed(p, "flush pocket");
            Rel(1000 - 80, p.Volume(), 1e-9, "flush pocket");

            // identical solids
            Rel(1, Csg.Union(Cube(1), Cube(1)).Volume(), 1e-9, "self union");
            Assert.Empty(Csg.Difference(Cube(1), Cube(1)).Faces);
        }

        [Fact]
        public void CylinderBooleans()
        {
            const int n = 64;
            var block = PrimitiveBuilder.Box(20, 20, 20);
            var drill = PrimitiveBuilder.Cylinder(5, 30, n);
            var holed = Csg.Difference(block, drill);
            Closed(holed, "drilled block");
            Rel(8000 - NgonArea(5, n) * 20, holed.Volume(), 1e-9, "drilled block");
            Rel(8000 - Math.PI * 25 * 20, holed.Volume(), 5e-3, "drilled block vs π");

            var stub = PrimitiveBuilder.Cylinder(5, 30, n);
            var u = Csg.Union(block, stub);
            Closed(u, "block ∪ cylinder");
            Rel(8000 + NgonArea(5, n) * 10, u.Volume(), 1e-9, "block ∪ cylinder");

            // Steinmetz solid: two perpendicular cylinders, V = 16/3 r³
            var c1 = PrimitiveBuilder.Cylinder(5, 20, 96);
            var c2 = PrimitiveBuilder.Cylinder(5, 20, 96).Transform(Affine.Rotate(Vec3.UnitX, 90));
            var st = Csg.Intersection(c1, c2);
            Closed(st, "Steinmetz");
            Rel(16.0 / 3 * 125, st.Volume(), 1e-2, "Steinmetz");

            // many sequential holes
            var plate = PrimitiveBuilder.Box(60, 40, 3);
            var holes = new List<PolyMesh>();
            for (int i = 0; i < 6; i++) holes.Add(PrimitiveBuilder.Cylinder(1.6, 5, 24).Translate(new Vec3(-25 + i * 10, 0, 0)));
            var sw = Stopwatch.StartNew();
            var perforated = Csg.Apply(plate, holes, BooleanOp.Difference);
            _out.WriteLine($"6 holes: {sw.ElapsedMilliseconds} ms, {perforated.Faces.Count} faces");
            Closed(perforated, "perforated plate");
            Rel(60 * 40 * 3 - 6 * NgonArea(1.6, 24) * 3, perforated.Volume(), 1e-9, "perforated plate");
        }

        [Fact]
        public void RotatedAndSphereBooleansStayWatertight()
        {
            var a = PrimitiveBuilder.Box(10, 10, 10).Transform(Affine.Euler(new Vec3(17, 31, 5)));
            var b = PrimitiveBuilder.Sphere(6, 32, 16).Translate(new Vec3(4, 2, 3));
            foreach (var op in new[] { BooleanOp.Union, BooleanOp.Difference, BooleanOp.Intersection })
            {
                var r = Csg.Apply(a, b, op);
                Closed(r, "box/sphere " + op);
            }

            var u = Csg.Union(a, b).Volume();
            var i = Csg.Intersection(a, b).Volume();
            Rel(a.Volume() + b.Volume(), u + i, 1e-6, "inclusion–exclusion");
            Rel(a.Volume() - i, Csg.Difference(a, b).Volume(), 1e-6, "difference = A − (A∩B)");
        }

        [Fact]
        public void FilletedPlateWithManyCuts()
        {
            var doc = new ModelDocument();
            var plate = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
            plate.Primitive!.Set("x", 90);
            plate.Primitive.Set("y", 60);
            plate.Primitive.Set("z", 6);
            plate.Primitive.Set("fillet", 2);
            var tools = new List<ModelObject>();
            var cut = ModelOps.AddPrimitive(doc, PrimitiveKind.Cylinder);
            cut.Primitive!.Set("d", 22);
            cut.Primitive.Set("h", 20);
            cut.Transform.Position = new Vec3(0, 0, 3);
            tools.Add(cut);
            for (int i = 0; i < 4; i++)
            {
                var h = ModelOps.AddPrimitive(doc, PrimitiveKind.Cylinder);
                h.Primitive!.Set("d", 3.2);
                h.Primitive.Set("h", 20);
                h.Primitive.Set("seg", 24);
                h.Transform.Position = new Vec3((i % 2 == 0 ? -1 : 1) * 36, (i < 2 ? -1 : 1) * 22, 3);
                tools.Add(h);
            }

            var slot = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
            slot.Primitive!.Set("x", 30);
            slot.Primitive.Set("y", 8);
            slot.Primitive.Set("z", 3);
            slot.Transform.Position = new Vec3(0, 22, 6);
            tools.Add(slot);
            var sw = Stopwatch.StartNew();
            var before = doc.WorldMesh(plate).Volume();
            ModelOps.Boolean(doc, plate, tools, BooleanOp.Difference);
            _out.WriteLine($"filleted plate: {sw.ElapsedMilliseconds} ms, {plate.LocalMesh().Faces.Count} faces");
            Closed(doc.WorldMesh(plate), "filleted plate");
            Assert.True(sw.ElapsedMilliseconds < 5000);
            Assert.True(doc.WorldMesh(plate).Volume() < before);
        }

        [Fact]
        public void DisjointBooleans()
        {
            var a = Cube(1);
            var b = Cube(1, 5, 0, 0);
            Rel(2, Csg.Union(a, b).Volume(), 1e-12, "disjoint union");
            Rel(1, Csg.Difference(a, b).Volume(), 1e-12, "disjoint difference");
            Assert.Empty(Csg.Intersection(a, b).Faces);
        }

        // ------------------------------------------------------------------ mesh editing

        [Fact]
        public void ExtrudeInsetSubdivide()
        {
            var m = PrimitiveBuilder.Box(10, 10, 10);
            int top = Enumerable.Range(0, m.Faces.Count).First(f => m.FaceNormal(f).Z > 0.9);
            var r = MeshEdit.ExtrudeFaces(m, new[] { top }, 5);
            Closed(m, "extruded");
            Rel(1500, m.Volume(), 1e-12, "extrude");
            Assert.Equal(10, m.Faces.Count);
            Assert.Equal(4, r.Vertices.Count);

            var ins = MeshEdit.InsetFaces(m, new[] { top }, 2);
            Closed(m, "inset");
            Rel(1500, m.Volume(), 1e-12, "inset keeps volume");
            Rel(36, m.FaceArea(top), 1e-9, "inset face area");
            MeshEdit.ExtrudeFaces(m, ins.Faces, -3);
            Closed(m, "pocket by extrude");
            Rel(1500 - 36 * 3, m.Volume(), 1e-9, "pocket");

            int before = m.Faces.Count;
            MeshEdit.Subdivide(m, new[] { 0 });
            Closed(m, "subdivided");
            Rel(1500 - 108, m.Volume(), 1e-9, "subdivide keeps volume");
            Assert.True(m.Faces.Count > before);

            var smooth = MeshEdit.CatmullClark(PrimitiveBuilder.Box(10, 10, 10));
            Closed(smooth, "catmull-clark");
            Assert.Equal(24, smooth.Faces.Count);
            Assert.True(smooth.Volume() < 1000 && smooth.Volume() > 300);
        }

        [Fact]
        public void RecalculateNormalsMergeDeleteFill()
        {
            var m = PrimitiveBuilder.Sphere(5, 16, 8);
            for (int f = 0; f < m.Faces.Count; f += 3) m.FlipFace(f);
            Assert.False(m.IsClosed);
            MeshEdit.RecalculateNormals(m);
            Closed(m, "recalculated");

            var inside = PrimitiveBuilder.Box(2, 2, 2);
            inside.FlipAll();
            MeshEdit.RecalculateNormals(inside);
            Assert.True(inside.Volume() > 0);

            // split vertices and merge them back
            var box = PrimitiveBuilder.Box(1, 1, 1);
            var split = new PolyMesh();
            foreach (var f in box.Faces) split.AddFace(f.Select(i => split.AddVertex(box.Vertices[i] + new Vec3(1e-5, 0, 0) * (split.Vertices.Count % 2))).ToArray());
            Assert.Equal(24, split.Vertices.Count);
            int removed = MeshEdit.MergeByDistance(split, 1e-3);
            Assert.Equal(16, removed);
            Closed(split, "merged");

            // delete the top face and fill it again
            var b2 = PrimitiveBuilder.Box(2, 2, 2);
            int top = Enumerable.Range(0, b2.Faces.Count).First(f => b2.FaceNormal(f).Z > 0.9);
            var topVerts = b2.Faces[top].ToList();
            MeshEdit.DeleteFaces(b2, new[] { top });
            Assert.False(b2.IsClosed);
            var tv = Enumerable.Range(0, b2.Vertices.Count).Where(v => b2.Vertices[v].Z > 0.9).ToList();
            MeshEdit.Fill(b2, tv);
            Closed(b2, "filled");

            // merge the four top vertices: a pyramid
            var b3 = PrimitiveBuilder.Box(2, 2, 2);
            var tops = Enumerable.Range(0, b3.Vertices.Count).Where(v => b3.Vertices[v].Z > 0.9).ToList();
            MeshEdit.MergeVertices(b3, tops);
            Closed(b3, "pyramid");
            Rel(4.0 * 2 / 3, b3.Volume(), 1e-9, "pyramid volume");
        }

        // ------------------------------------------------------------------ document

        [Fact]
        public void DocumentRoundTripAndOperations()
        {
            var doc = new ModelDocument { Title = "Test şasi" };
            var box = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
            box.Primitive!.Set("x", 40);
            box.Primitive.Set("fillet", 2);
            var cyl = ModelOps.AddPrimitive(doc, PrimitiveKind.Cylinder, 10, 0);
            cyl.Transform.RX = 90;
            cyl.Transform.SZ = 2;
            var part = RobotParts.Insert(doc, "l-bracket", 60, 0)[0];
            var mesh = ModelOps.AddMesh(doc, PrimitiveBuilder.Sphere(5, 12, 6), "Küre");
            mesh.ParentId = box.Id;
            mesh.Color = "#FF8800";
            mesh.Material = "ALÜMİNYUM";

            string dir = Path.Combine(Path.GetTempPath(), "rtmodel_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "test" + ModelDocument.Extension);
                doc.Save(path);
                var back = ModelDocument.Load(path);
                Assert.Equal(doc.ToJson(), back.ToJson());
                Assert.Equal("Test şasi", back.Title);
                foreach (var o in doc.Objects)
                {
                    var b = back.Find(o.Id)!;
                    Assert.Equal(o.Name, b.Name);
                    Rel(doc.WorldMesh(o).Volume(), back.WorldMesh(b).Volume(), 1e-12, o.Name);
                }

                Assert.Equal(box.Id, back.Find(mesh.Id)!.ParentId);
            }
            finally
            {
                Directory.Delete(dir, true);
            }

            // boolean through the document: the box loses the cylinder volume inside it
            var d2 = new ModelDocument();
            var blk = ModelOps.AddPrimitive(d2, PrimitiveKind.Box);
            var drill = ModelOps.AddPrimitive(d2, PrimitiveKind.Cylinder);
            drill.Primitive!.Set("d", 6);
            drill.Primitive.Set("h", 40);
            drill.Transform.Position = blk.Transform.Position;
            ModelOps.Boolean(d2, blk, new[] { drill }, BooleanOp.Difference);
            Assert.Single(d2.Objects);
            Assert.Null(blk.Primitive);
            Closed(blk.LocalMesh(), "document boolean");
            Rel(8000 - NgonArea(3, 48) * 20, blk.LocalMesh().Volume(), 1e-9, "document boolean");

            // join keeps both volumes
            var d3 = new ModelDocument();
            var j1 = ModelOps.AddPrimitive(d3, PrimitiveKind.Box);
            var j2 = ModelOps.AddPrimitive(d3, PrimitiveKind.Box, 50, 0);
            j2.Transform.RZ = 45;
            ModelOps.Join(d3, j1, new[] { j2 });
            Assert.Single(d3.Objects);
            Rel(16000, d3.WorldMesh(j1).Volume(), 1e-9, "join");

            // mirror copy: same volume, still outward, mirrored position
            var d4 = new ModelDocument();
            var w = ModelOps.AddPrimitive(d4, PrimitiveKind.Wedge, 30, 10);
            w.Transform.RZ = 20;
            var mirrored = ModelOps.MirrorCopy(d4, new[] { w }, Axis.X, Vec3.Zero)[0];
            Rel(d4.WorldMesh(w).Volume(), d4.WorldMesh(mirrored).Volume(), 1e-9, "mirror volume");
            Assert.True(d4.WorldMesh(mirrored).Volume() > 0);
            Rel(-30, mirrored.Transform.X, 1e-9, "mirror position");
            var cylM = ModelOps.AddPrimitive(d4, PrimitiveKind.Cylinder, 20, 0);
            var cm = ModelOps.MirrorCopy(d4, new[] { cylM }, Axis.Y, Vec3.Zero)[0];
            Assert.NotNull(cm.Primitive);

            // arrays
            var d5 = new ModelDocument();
            var s = ModelOps.AddPrimitive(d5, PrimitiveKind.Cylinder, 30, 0);
            ModelOps.ArrayCircular(d5, new[] { s }, 6, 360, Axis.Z, Vec3.Zero);
            Assert.Equal(6, d5.Objects.Count);
            Rel(30, d5.Objects[3].Transform.Position.Length > 0 ? new Vec3(d5.Objects[3].Transform.X, d5.Objects[3].Transform.Y, 0).Length : 0, 1e-9, "circular radius");
            Rel(-30, d5.Objects[3].Transform.X, 1e-9, "circular 180°");
            ModelOps.ArrayLinear(d5, new[] { s }, 3, new Vec3(0, 25, 0));
            Assert.Equal(8, d5.Objects.Count);
            Rel(50, d5.Objects[7].Transform.Y, 1e-9, "linear");

            // apply transform keeps world geometry
            var d6 = new ModelDocument();
            var sc = ModelOps.AddPrimitive(d6, PrimitiveKind.Box);
            sc.Transform.Scale = new Vec3(2, 1, 0.5);
            sc.Transform.RZ = 30;
            double v0 = d6.WorldMesh(sc).Volume();
            var b0 = d6.WorldMesh(sc).Bounds;
            ModelOps.ApplyTransform(d6, sc);
            Assert.True(sc.Transform.Scale.X == 1 && sc.Transform.RZ == 0);
            Rel(v0, d6.WorldMesh(sc).Volume(), 1e-9, "apply transform");
            Rel(b0.Size.X, d6.WorldMesh(sc).Bounds.Size.X, 1e-9, "apply transform bounds");
        }

        [Fact]
        public void MeasurementsAndMass()
        {
            var cube = PrimitiveBuilder.Box(10, 10, 10);
            var st = ModelMeasure.Of(cube, "ALÜMİNYUM");
            Rel(1000, st.VolumeMm3, 1e-9, "volume");
            Rel(600, st.AreaMm2, 1e-9, "area");
            Rel(2.7, st.MassG, 1e-9, "aluminium 1 cm³");
            Assert.True(st.Closed);
            var pla = ModelMeasure.Of(cube, "PLA", 0.2);
            Rel(MaterialDensity.Printed(MaterialDensity.Pla, 0.2) * 1e-6 * 1000, pla.MassG, 1e-9, "printed PLA");
        }

        /// <summary>[mass-props] A 20 × 10 × 4 mm aluminium block: the box formulas m(b² + c²)/12, its axes the principal ones.</summary>
        [Fact]
        public void MassPropertiesOfABlockMatchTheFormulas()
        {
            var st = ModelMeasure.Of(PrimitiveBuilder.Box(20, 10, 4), "ALÜMİNYUM");
            double m = st.MassG;
            Rel(2.16, m, 1e-9, "0.8 cm³ of aluminium");
            Rel(m * (10 * 10 + 4 * 4) / 12, st.InertiaGmm2.M00, 1e-6, "Lxx");
            Rel(m * (20 * 20 + 4 * 4) / 12, st.InertiaGmm2.M11, 1e-6, "Lyy");
            Rel(m * (20 * 20 + 10 * 10) / 12, st.InertiaGmm2.M22, 1e-6, "Lzz");
            Assert.True(Math.Abs(st.InertiaGmm2.M01) < 1e-6 && Math.Abs(st.InertiaGmm2.M02) < 1e-6 && Math.Abs(st.InertiaGmm2.M12) < 1e-6, "no products of inertia");
            var moments = new[] { st.PrincipalMomentsGmm2.X, st.PrincipalMomentsGmm2.Y, st.PrincipalMomentsGmm2.Z }.OrderBy(v => v).ToArray();
            Rel(m * 116 / 12, moments[0], 1e-6, "P1");
            Rel(m * 500 / 12, moments[2], 1e-6, "P3");
            foreach (var axis in st.PrincipalAxes)
            {
                Rel(1, axis.Length, 1e-9, "unit axis");
                double big = Math.Max(Math.Abs(axis.X), Math.Max(Math.Abs(axis.Y), Math.Abs(axis.Z)));
                Rel(1, big, 1e-6, "along a box edge");
            }

            string report = st.Report("blok");
            Assert.Contains("Kütle özellikleri: blok", report);
            Assert.Contains("Kütle = 2.16 g", report);
            Assert.Contains("Lzz = " + (m * 500 / 12).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), report);
        }

        [Fact]
        public void RobotPartsAreWatertight()
        {
            foreach (var t in RobotParts.All)
            {
                var sw = Stopwatch.StartNew();
                var m = RobotParts.Build(RobotParts.CreateSpec(t.Id));
                var stats = ModelMeasure.Of(m, t.Material, 0.3);
                _out.WriteLine($"{t.Id}: {m.Faces.Count} faces, {stats}, {sw.ElapsedMilliseconds} ms");
                Closed(m, t.Id);
            }
        }

        [Fact]
        public void TriangulatorHandlesHolesAndCollinearPoints()
        {
            var outer = new List<Vec2> { new Vec2(0, 0), new Vec2(5, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10) };
            var tri = Triangulator.Triangulate(outer);
            Assert.Equal(9, tri.Count); // collinear point kept: 3 triangles
            Assert.Contains(1, tri);
            var hole = new List<Vec2> { new Vec2(3, 3), new Vec2(7, 3), new Vec2(7, 7), new Vec2(3, 7) };
            var t2 = Triangulator.Triangulate(outer, new[] { hole });
            double area = 0;
            var all = outer.Concat(hole).ToList();
            for (int i = 0; i < t2.Count; i += 3) area += (all[t2[i + 1]] - all[t2[i]]).Cross(all[t2[i + 2]] - all[t2[i]]) / 2;
            Rel(100 - 16, area, 1e-12, "area with hole");
        }

        [Fact]
        public void ImportedTriangleMeshBecomesPolygons()
        {
            var mm = MeshModel.CreateBox(new Vec3(0.02, 0.01, 0.005));
            var p = ModelOps.FromImported(mm);
            Assert.Equal(6, p.Faces.Count);
            Rel(1000, p.Volume(), 1e-9, "imported box (mm³)");
            var doc = new ModelDocument();
            var o = ModelOps.AddMesh(doc, p, "kutu");
            var back = ModelOps.ToMeshModel(doc, new[] { o }, "kutu");
            Rel(1e-6, MassProperties.FromMesh(back, 1000).Volume, 1e-9, "export volume (m³)");
            // and the world accepts it as a model object
            var world = new WorldDocument();
            var wo = world.AddModel(back, "kutu", new WorldPose());
            Assert.Equal(WorldObjectKind.Model, wo.Kind);
            Assert.Equal(12, MeshModel.FromEmbedded(wo.Mesh!).TriangleCount);
        }
    }
}
