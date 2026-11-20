using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Mechanics;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    public sealed class MeshTests : IDisposable
    {
        private readonly string _dir;
        private readonly ITestOutputHelper _out;

        public MeshTests(ITestOutputHelper output)
        {
            _out = output;
            _dir = Path.Combine(Path.GetTempPath(), "rtmesh_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (IOException)
            {
            }
        }

        private string P(string name) => Path.Combine(_dir, name);

        private static void Rel(double expected, double actual, double tol, string what = "")
        {
            double err = Math.Abs(actual - expected) / Math.Max(Math.Abs(expected), 1e-300);
            Assert.True(err <= tol, $"{what}: expected {expected:G9}, got {actual:G9} (rel err {err:E2} > {tol:E2})");
        }

        private static void Near(Vec3 expected, Vec3 actual, double tol, string what = "")
        {
            Assert.True((expected - actual).Length <= tol, $"{what}: expected {expected}, got {actual} (|d| = {(expected - actual).Length:E2})");
        }

        private static void NearM(Mat3 expected, Mat3 actual, double tol, string what = "")
        {
            double scale = Math.Max(expected.MaxAbs(), 1e-300);
            Assert.True((expected - actual).MaxAbs() <= tol * scale, $"{what}: expected {expected}, got {actual}");
        }

        // ================================================================== mass properties

        [Fact]
        public void BoxMassPropertiesAreExact()
        {
            var size = new Vec3(0.2, 0.12, 0.05);
            var center = new Vec3(1.5, -2.0, 0.7);
            var mesh = MeshModel.CreateBox(size).Translate(center);
            var mp = MassProperties.FromMesh(mesh, 1000);
            var exact = MassProperties.Box(1000 * size.X * size.Y * size.Z, size, center);

            Assert.Equal(MassPropertiesMethod.ExactSolid, mp.Method);
            Assert.True(mp.IsClosed);
            Assert.False(mp.IsEstimate);
            Rel(size.X * size.Y * size.Z, mp.Volume, 1e-12, "volume");
            Rel(exact.Mass, mp.Mass, 1e-12, "mass");
            Near(center, mp.CenterOfMass, 1e-12, "com");
            NearM(exact.Inertia, mp.Inertia, 1e-10, "inertia");
            Near(exact.PrincipalMoments, mp.PrincipalMoments, 1e-12, "principal");
            Assert.True(Math.Abs(mp.PrincipalAxes.W) > 1 - 1e-12, "axis-aligned box has identity principal axes");
            Rel(2 * (size.X * size.Y + size.Y * size.Z + size.Z * size.X), mp.SurfaceArea, 1e-12, "area");
        }

        [Fact]
        public void RotatedBoxGivesFullTensorAndRecoversPrincipalAxes()
        {
            var size = new Vec3(0.3, 0.1, 0.04);
            var q = (Quat.FromAxisAngle(new Vec3(1, 2, 3), 0.7)).Normalized();
            var mesh = MeshModel.CreateBox(size).Transform(q, new Vec3(0.1, 0.2, 0.3));
            var mp = MassProperties.FromMeshWithMass(mesh, 2.0);
            var box = MassProperties.Box(2.0, size);

            NearM(box.Inertia.Rotated(q), mp.Inertia, 1e-9, "tensor");
            Assert.True(Math.Abs(mp.Inertia.M01) > 1e-6, "off-diagonal terms present");
            var sortedExact = new[] { box.PrincipalMoments.X, box.PrincipalMoments.Y, box.PrincipalMoments.Z }.OrderBy(x => x).ToArray();
            var sortedGot = new[] { mp.PrincipalMoments.X, mp.PrincipalMoments.Y, mp.PrincipalMoments.Z }.OrderBy(x => x).ToArray();
            for (int i = 0; i < 3; i++) Rel(sortedExact[i], sortedGot[i], 1e-9, "moment " + i);

            // Diagonalising with the returned axes reproduces the tensor.
            var rebuilt = Mat3.Diagonal(mp.PrincipalMoments).Rotated(mp.PrincipalAxes);
            NearM(mp.Inertia, rebuilt, 1e-9, "V·D·Vᵀ");
            Rel(1.0, Mat3.FromQuat(mp.PrincipalAxes).Determinant, 1e-12, "proper rotation");
        }

        [Fact]
        public void CylinderAndSphereFromStlMatchAnalyticWithinHalfPercent()
        {
            double r = 0.04, h = 0.12, rho = 1240;
            var cyl = MeshModel.CreateCylinder(r, h, 128).Translate(new Vec3(0.01, 0.02, 0.03));
            cyl.Save(P("cyl.stl"));
            var loaded = MeshModel.Load(P("cyl.stl"));
            Assert.Equal(cyl.TriangleCount, loaded.TriangleCount);
            Assert.Equal(cyl.Vertices.Count, loaded.Vertices.Count); // welded back to shared vertices
            var mp = MassProperties.FromMesh(loaded, rho);
            var exact = MassProperties.Cylinder(rho * Math.PI * r * r * h, r, h, new Vec3(0.01, 0.02, 0.03));
            Assert.Equal(MassPropertiesMethod.ExactSolid, mp.Method);
            Rel(exact.Volume, mp.Volume, 0.005, "cyl volume");
            Rel(exact.Mass, mp.Mass, 0.005, "cyl mass");
            Near(exact.CenterOfMass, mp.CenterOfMass, 1e-6, "cyl com");
            Rel(exact.PrincipalMoments.X, mp.PrincipalMoments.X, 0.005, "cyl Ixx");
            Rel(exact.PrincipalMoments.Y, mp.PrincipalMoments.Y, 0.005, "cyl Iyy");
            Rel(exact.PrincipalMoments.Z, mp.PrincipalMoments.Z, 0.005, "cyl Izz");

            double rs = 0.05;
            var sph = MeshModel.CreateSphere(rs, 128, 64);
            sph.Save(P("sph.obj"));
            sph.Save(P("sph.ply"));
            foreach (var file in new[] { "sph.obj", "sph.ply" })
            {
                var s = MeshModel.Load(P(file));
                var smp = MassProperties.FromMeshWithMass(s, 0.3);
                var sx = MassProperties.Sphere(0.3, rs);
                Rel(sx.Volume, smp.Volume, 0.005, file + " volume");
                Near(Vec3.Zero, smp.CenterOfMass, 1e-7, file + " com");
                Rel(sx.PrincipalMoments.X, smp.PrincipalMoments.X, 0.005, file + " I");
                Rel(sx.PrincipalMoments.Z, smp.PrincipalMoments.Z, 0.005, file + " I");
            }
        }

        [Fact]
        public void InvertedWindingIsCorrected()
        {
            var mesh = MeshModel.CreateBox(new Vec3(1, 2, 3));
            mesh.FlipWinding();
            var mp = MassProperties.FromMesh(mesh, 1);
            Assert.True(mp.WindingInverted);
            Rel(6, mp.Volume, 1e-12);
            Rel(6, mp.Mass, 1e-12);
        }

        [Fact]
        public void OpenMeshFallsBackToFlaggedEstimate()
        {
            var mesh = MeshModel.CreateBox(new Vec3(0.2, 0.1, 0.05));
            mesh.Indices.RemoveRange(0, 6); // remove the bottom face
            var topo = mesh.AnalyzeTopology();
            Assert.False(topo.IsClosed);
            Assert.Equal(4, topo.BoundaryEdges);

            var hull = MassProperties.FromMesh(mesh, 1000);
            Assert.Equal(MassPropertiesMethod.ConvexHullSolid, hull.Method);
            Assert.True(hull.IsEstimate);
            Assert.NotEmpty(hull.Warnings);
            Rel(0.2 * 0.1 * 0.05 * 1000, hull.Mass, 1e-9, "hull keeps the full box");

            var shell = MassProperties.FromMeshWithMass(mesh, 1.0, new MassPropertiesOptions { OpenMeshFallback = OpenMeshFallback.Shell });
            Assert.Equal(MassPropertiesMethod.Shell, shell.Method);
            Assert.True(shell.IsEstimate);
            Rel(1.0, shell.Mass, 1e-12);
            Assert.True(shell.CenterOfMass.Z > 0.001, "open bottom moves the shell COM up");

            var bbox = MassProperties.FromMeshWithMass(mesh, 1.0, new MassPropertiesOptions { OpenMeshFallback = OpenMeshFallback.BoundingBox });
            Assert.Equal(MassPropertiesMethod.BoundingBox, bbox.Method);
            NearM(MassProperties.Box(1, new Vec3(0.2, 0.1, 0.05)).Inertia, bbox.Inertia, 1e-12);

            // A flat sheet has no hull volume: Auto falls back to a shell.
            var sheet = new MeshModel();
            sheet.AddVertex(new Vec3(0, 0, 0));
            sheet.AddVertex(new Vec3(1, 0, 0));
            sheet.AddVertex(new Vec3(1, 1, 0));
            sheet.AddVertex(new Vec3(0, 1, 0));
            sheet.AddTriangle(0, 1, 2);
            sheet.AddTriangle(0, 2, 3);
            var sm = MassProperties.FromMeshWithMass(sheet, 2.0);
            Assert.Equal(MassPropertiesMethod.Shell, sm.Method);
            Near(new Vec3(0.5, 0.5, 0), sm.CenterOfMass, 1e-12);
            Rel(2.0 * (1 + 1) / 12, sm.Inertia.M22, 1e-12, "plate Izz");
        }

        [Fact]
        public void ShellOfTriangleMatchesAnalyticPlate()
        {
            var plate = MeshModel.CreateBox(new Vec3(0.4, 0.2, 1e-9));
            var mp = MassProperties.ShellFromMesh(plate, 1.0);
            // Two faces of 0.08 m² each (sides are negligible).
            Rel(0.16, mp.Mass, 1e-6);
            Rel(mp.Mass * (0.4 * 0.4 + 0.2 * 0.2) / 12, mp.Inertia.M22, 1e-6, "Izz of a thin plate");
        }

        [Fact]
        public void CombineMatchesAnalyticComposite()
        {
            // Two identical boxes side by side = one box twice as long.
            var half = MassProperties.FromMeshWithMass(MeshModel.CreateBox(new Vec3(0.1, 0.1, 0.05)), 0.5);
            var both = MassProperties.Combine(new MassPart(half, new Vec3(-0.05, 0, 0)), new MassPart(half, new Vec3(0.05, 0, 0)));
            var whole = MassProperties.Box(1.0, new Vec3(0.2, 0.1, 0.05));
            Rel(1.0, both.Mass, 1e-12);
            Near(Vec3.Zero, both.CenterOfMass, 1e-12);
            NearM(whole.Inertia, both.Inertia, 1e-9, "two halves");

            // Dumbbell of point masses.
            var db = MassProperties.Combine(new MassPart(MassProperties.Point(0.2), new Vec3(0.3, 0, 0)),
                new MassPart(MassProperties.Point(0.2), new Vec3(-0.3, 0, 0)));
            Rel(2 * 0.2 * 0.09, db.Inertia.M11, 1e-12, "dumbbell Iyy");
            Assert.True(Math.Abs(db.Inertia.M00) < 1e-15);

            // Rover-like assembly: chassis plate, two motors lying along y, battery, board.
            double mc = 0.08, mm = 0.03, mb = 0.1, mboard = 0.025;
            var chassisMesh = MeshModel.CreateBox(new Vec3(0.2, 0.13, 0.004));
            var motorMesh = MeshModel.CreateCylinder(0.012, 0.065, 96);
            var chassis = MassProperties.FromMeshWithMass(chassisMesh, mc);
            var motor = MassProperties.FromMeshWithMass(motorMesh, mm);
            var lay = Quat.FromAxisAngle(Vec3.UnitX, Math.PI / 2);
            var parts = new[]
            {
                new MassPart(chassis, Vec3.Zero),
                new MassPart(motor, new Vec3(-0.035, 0.04, -0.014), lay),
                new MassPart(motor, new Vec3(-0.035, -0.04, -0.014), lay),
                new MassPart(MassProperties.Box(mb, new Vec3(0.07, 0.035, 0.02)), new Vec3(0.03, 0, 0.012)),
                new MassPart(MassProperties.Point(mboard), new Vec3(0.06, 0.02, 0.01))
            };
            var total = MassProperties.Combine(parts);

            // Analytic: sum of (R·I·Rᵀ + parallel axis) with closed-form parts.
            double M = mc + 2 * mm + mb + mboard;
            var com = (new Vec3(-0.035, 0.04, -0.014) * mm + new Vec3(-0.035, -0.04, -0.014) * mm + new Vec3(0.03, 0, 0.012) * mb +
                       new Vec3(0.06, 0.02, 0.01) * mboard) / M;
            Mat3 Pa(double m, Vec3 c) => (Mat3.Identity * (c - com).Dot(c - com) - Mat3.Outer(c - com, c - com)) * m;
            var cylI = MassProperties.Cylinder(mm, 0.012, 0.065).Inertia.Rotated(lay);
            var expected = MassProperties.Box(mc, new Vec3(0.2, 0.13, 0.004)).Inertia + Pa(mc, Vec3.Zero) +
                           cylI + Pa(mm, new Vec3(-0.035, 0.04, -0.014)) + cylI + Pa(mm, new Vec3(-0.035, -0.04, -0.014)) +
                           MassProperties.Box(mb, new Vec3(0.07, 0.035, 0.02)).Inertia + Pa(mb, new Vec3(0.03, 0, 0.012)) +
                           Pa(mboard, new Vec3(0.06, 0.02, 0.01));
            Rel(M, total.Mass, 1e-12, "total mass");
            Near(com, total.CenterOfMass, 1e-12, "total com");
            NearM(expected, total.Inertia, 0.002, "total tensor (motor tessellation)");
            Assert.True(Math.Abs(total.Inertia.M02) > 0, "asymmetric assembly has products of inertia");
            Assert.Equal(MassPropertiesMethod.Combined, total.Method);
        }

        [Fact]
        public void JacobiHandlesDegenerateAndGeneralTensors()
        {
            var (vals, vecs) = Mat3.Diagonal(new Vec3(2, 2, 5)).SymmetricEigen();
            Near(new Vec3(2, 2, 5), vals, 1e-15);
            NearM(Mat3.Identity, vecs, 1e-15);

            var rnd = new Random(3);
            for (int k = 0; k < 50; k++)
            {
                var q = new Quat(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized();
                var d = new Vec3(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                var t = Mat3.Diagonal(d).Rotated(q);
                var (v2, r2) = t.SymmetricEigen();
                NearM(t, Mat3.Diagonal(v2).Rotated(r2.ToQuat()), 1e-12, "reconstruct " + k);
            }
        }

        // ================================================================== rigid body with full tensor

        [Fact]
        public void PrincipalAxesRotationIsEquivalentToRotatingTheBodyFrame()
        {
            var r = Quat.FromAxisAngle(new Vec3(0.3, -1, 0.5), 0.9);
            var q0 = Quat.FromAxisAngle(new Vec3(1, 1, 0), 0.4);
            var inertia = new Vec3(0.002, 0.005, 0.0065);
            var a = new RigidBody { Inertia = inertia, PrincipalAxes = r, Orientation = q0, AngularDrag = 0, LinearDrag = 0 };
            var b = new RigidBody { Inertia = inertia, Orientation = q0 * r, AngularDrag = 0, LinearDrag = 0 };
            var w0 = new Vec3(3, -1, 2);
            a.AngularVelocity = b.AngularVelocity = w0;

            // L = R·I·Rᵀ·ω (world) must stay roughly constant without torque.
            Vec3 L(RigidBody body)
            {
                var frame = body.Orientation * body.PrincipalAxes;
                return frame.Rotate(body.Inertia.Mul(frame.InverseRotate(body.AngularVelocity)));
            }

            var l0 = L(a);
            for (int i = 0; i < 4000; i++)
            {
                a.AddTorque(new Vec3(1e-4, 0, 0));
                b.AddTorque(new Vec3(1e-4, 0, 0));
                Integrate(a, 2.5e-4);
                Integrate(b, 2.5e-4);
            }

            Near(b.AngularVelocity, a.AngularVelocity, 1e-8, "same motion");
            var expectedL = l0 + new Vec3(1e-4, 0, 0) * 1.0;
            Assert.True((L(a) - expectedL).Length / l0.Length < 0.02, $"angular momentum drift {(L(a) - expectedL).Length / l0.Length:P2}");

            // A default body still behaves exactly as before (identity principal axes).
            Assert.Equal(Quat.Identity.W, new RigidBody().PrincipalAxes.W);
        }

        private static void Integrate(RigidBody b, double dt) =>
            typeof(RigidBody).GetMethod("Integrate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(b, new object[] { dt });

        [Fact]
        public void DroppedMeshBoxComesToRestLevel()
        {
            foreach (double yawInModel in new[] { 0.0, 0.5 })
            {
                var size = new Vec3(0.2, 0.12, 0.06);
                var mesh = MeshModel.CreateBox(size).Transform(Quat.FromYaw(yawInModel), new Vec3(0.02, -0.01, 0.03));
                var mp = MassProperties.FromMeshWithMass(mesh, 0.5);
                var world = new World3D(Arena.Create("empty"));
                var body = world.Body;
                var com = body.ConfigureFromMesh(mesh, mp, 8);
                Assert.Equal(8, body.ContactPoints.Count);
                Rel(0.5, body.Mass, 1e-12);
                if (yawInModel != 0) Assert.True(Math.Abs(mp.Inertia.M01) > 1e-6, "yawed box has a product of inertia");
                NearM(mp.Inertia, body.InertiaTensor(), 1e-9, "body tensor");

                body.Position = new Vec3(0, 0, 0.12);
                body.Orientation = Quat.FromAxisAngle(Vec3.UnitX, 0.15) * Quat.FromAxisAngle(Vec3.UnitY, -0.1);
                for (int i = 0; i < 300; i++) world.Step(0.01);

                var up = body.Orientation.Rotate(Vec3.UnitZ);
                double tilt = Math.Acos(Math.Min(1, up.Z));
                Assert.True(tilt < 0.01, $"tilt {tilt} rad");
                Assert.True(body.Velocity.Length < 2e-3, $"v = {body.Velocity}");
                Assert.True(body.AngularVelocity.Length < 2e-2, $"w = {body.AngularVelocity}");
                // COM height ≈ half the box height (minus ~1 mm contact compression).
                double h = body.Position.Z;
                Assert.InRange(h, size.Z / 2 - 0.003, size.Z / 2 + 0.0005);
                _ = com;
            }
        }

        [Fact]
        public void MeshObstacleBlocksBodyAndRaycasts()
        {
            var arena = Arena.Create("empty");
            var post = MeshObstacle.Place(MeshModel.CreateCylinder(0.1, 0.2, 64), 1.0, 0.0);
            arena.MeshObstacles.Add(post);
            Rel(0.9, arena.Raycast(new Vec3(0, 0, 0.05), Vec3.UnitX, 4), 1e-9, "ray to post");
            Rel(3, arena.Raycast(new Vec3(0, 0, 0.05), Vec3.UnitY, 4), 1e-12, "ray past post hits the wall");
            Assert.True(post.Contains(new Vec3(1, 0, 0.1)));
            Assert.False(post.Contains(new Vec3(1, 0, 0.25)));
            Assert.True(post.TryGetPenetration(new Vec3(0.91, 0, 0.1), out double depth, out var n));
            Rel(0.01, depth, 2e-3); // facet of the 64-gon, cos(π/64) below the true radius
            Near(-Vec3.UnitX, n, 0.05);
            Assert.True(post.TryGetPenetration(new Vec3(1.0, 0, 0.001), out _, out var n2) && n2.Z < 0.5, "floor face does not push down");

            // A box sliding towards the post is stopped by it (its corners meet the post at x ≈ 0.913).
            double Slide(Arena a)
            {
                var world = new World3D(a);
                var mesh = MeshModel.CreateBox(new Vec3(0.1, 0.1, 0.05));
                world.Body.ConfigureFromMesh(mesh, MassProperties.FromMeshWithMass(mesh, 0.3), 8);
                world.Body.Position = new Vec3(0.7, 0, 0.026);
                world.Body.Velocity = new Vec3(0.5, 0, 0);
                a.Friction = 0.05;
                double maxX = 0;
                for (int i = 0; i < 150; i++)
                {
                    world.Step(0.01);
                    maxX = Math.Max(maxX, world.Body.Position.X);
                }

                return maxX + 0.05;
            }

            double free = Slide(Arena.Create("empty"));
            double blocked = Slide(arena);
            Assert.True(free > 0.95, $"without the post the box slides to {free:F3}");
            Assert.True(blocked < 0.923, $"box front reached {blocked:F4}");
        }

        // ================================================================== hull & contacts

        [Fact]
        public void HullOfCubeHasEightVertices()
        {
            var cube = MeshModel.CreateBox(new Vec3(1, 1, 1));
            var hull = ConvexHull.FromMesh(cube);
            Assert.False(hull.IsDegenerate);
            Assert.Equal(8, hull.Vertices.Count);
            Assert.Equal(6, hull.Facets.Count);
            Assert.Equal(12, hull.Indices.Count / 3);
            Assert.True(hull.ToMesh().AnalyzeTopology().IsClosed);
            Rel(1.0, MassProperties.FromMesh(hull.ToMesh(), 1).Volume, 1e-12, "hull volume");

            // Interior points, face centres and edge midpoints do not add hull vertices.
            var pts = new List<Vec3>(cube.Vertices);
            var rnd = new Random(1);
            for (int i = 0; i < 2000; i++) pts.Add(new Vec3(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5));
            foreach (double a in new[] { -0.5, 0.5 })
            {
                pts.Add(new Vec3(a, 0, 0));
                pts.Add(new Vec3(0, a, 0));
                pts.Add(new Vec3(0, 0, a));
                pts.Add(new Vec3(a, a, 0));
                pts.Add(new Vec3(0, a, a));
                pts.Add(new Vec3(a, 0, -a));
                for (int k = 0; k < 20; k++) pts.Add(new Vec3(a, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5)); // on a face
            }

            var h2 = ConvexHull.Compute(pts);
            Assert.Equal(8, h2.Vertices.Count);
            Assert.Equal(6, h2.Planes.Count);
        }

        [Fact]
        public void HullOfRandomCloudContainsAllPointsAndIsClosed()
        {
            var rnd = new Random(42);
            var pts = new List<Vec3>();
            for (int i = 0; i < 5000; i++)
            {
                var g = new Vec3(Gauss(rnd), Gauss(rnd) * 0.5, Gauss(rnd) * 0.2);
                pts.Add(g + new Vec3(10, -3, 2));
            }

            var hull = ConvexHull.Compute(pts);
            Assert.False(hull.IsDegenerate);
            Assert.True(hull.ToMesh().AnalyzeTopology().IsClosed);
            foreach (var p in pts) Assert.True(hull.SignedDistance(p) <= hull.Tolerance * 4, $"{p} outside by {hull.SignedDistance(p)}");
            foreach (var v in hull.Vertices) Assert.True(hull.SignedDistance(v) >= -hull.Tolerance * 4);
            Assert.True(MassProperties.FromMesh(hull.ToMesh(), 1).Volume > 0);

            // Sphere: every vertex is on the hull, hull volume = mesh volume.
            var s = MeshModel.CreateSphere(0.3, 48, 24);
            var sh = ConvexHull.FromMesh(s);
            Assert.Equal(s.Vertices.Count, sh.Vertices.Count);
            Rel(MassProperties.FromMesh(s, 1).Volume, MassProperties.FromMesh(sh.ToMesh(), 1).Volume, 1e-9, "sphere hull volume");

            // Flat input is reported as degenerate.
            var flat = ConvexHull.Compute(new[] { new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0), new Vec3(0.5, 0.5, 0) });
            Assert.True(flat.IsDegenerate);
            Assert.Equal(4, flat.Vertices.Count);
        }

        private static double Gauss(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());

        [Fact]
        public void ContactPointsAreSpreadHullVertices()
        {
            var box = MeshModel.CreateBox(new Vec3(0.2, 0.1, 0.05));
            var all = ContactGeometry.ContactPointsFromMesh(box, 16);
            Assert.Equal(8, all.Count);
            var four = ContactGeometry.ContactPointsFromMesh(box, 4);
            Assert.Equal(4, four.Count);
            Assert.Equal(4, four.Distinct().Count());
            // Farthest-point sampling picks a tetrahedron of corners: spread in every axis.
            Assert.Contains(four, p => p.Z < 0);
            Assert.Contains(four, p => p.Z > 0);
            Assert.Contains(four, p => p.X < 0);
            Assert.Contains(four, p => p.X > 0);

            var cyl = MeshModel.CreateCylinder(0.1, 0.02, 200);
            var pts = ContactGeometry.ContactPointsFromMesh(cyl, 12);
            Assert.Equal(12, pts.Count);
            double minSep = double.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            for (int j = i + 1; j < pts.Count; j++)
                minSep = Math.Min(minSep, (pts[i] - pts[j]).Length);
            Assert.True(minSep > 0.015, $"points are spread (min separation {minSep:F4})");
            Assert.True(pts.Count(p => p.Z < 0) >= 3, "bottom rim has support points");
        }

        // ================================================================== ray casts

        [Fact]
        public void RayHitsMeshLikeBruteForce()
        {
            var box = MeshModel.CreateBox(new Vec3(1, 1, 1));
            var bvh = new MeshBvh(box);
            Assert.True(bvh.Raycast(new Vec3(-5, 0.1, 0.2), Vec3.UnitX, 100, out var hit));
            Rel(4.5, hit.Distance, 1e-12);
            Near(-Vec3.UnitX, hit.Normal, 1e-12);
            Near(new Vec3(-0.5, 0.1, 0.2), hit.Point, 1e-12);
            Assert.False(bvh.Raycast(new Vec3(-5, 2, 0), Vec3.UnitX, 100, out _));
            Assert.False(bvh.Raycast(new Vec3(-5, 0, 0), Vec3.UnitX, 4.0, out _), "beyond max distance");
            Assert.True(bvh.Raycast(Vec3.Zero, Vec3.UnitZ, 10, out var inside));
            Rel(0.5, inside.Distance, 1e-12, "from inside");

            var sphere = MeshModel.CreateSphere(0.5, 96, 48).Translate(new Vec3(1, 2, 3));
            var sb = new MeshBvh(sphere);
            Assert.True(sb.Depth < 40);
            var rnd = new Random(7);
            for (int k = 0; k < 500; k++)
            {
                var dir = new Vec3(Gauss(rnd), Gauss(rnd), Gauss(rnd)).Normalized();
                var origin = new Vec3(1, 2, 3) - dir * 2 + new Vec3(Gauss(rnd), Gauss(rnd), Gauss(rnd)) * 0.15;
                bool got = sb.Raycast(origin, dir, 10, out var h);
                bool want = BruteForce(sphere, origin, dir, 10, out double t);
                Assert.Equal(want, got);
                if (got)
                {
                    Assert.True(Math.Abs(t - h.Distance) < 1e-12, $"ray {k}: {t} vs {h.Distance}");
                    Assert.True(Math.Abs(((h.Point - new Vec3(1, 2, 3)).Length) - 0.5) < 0.002, "on the sphere");
                }
            }
        }

        private static bool BruteForce(MeshModel m, Vec3 o, Vec3 d, double max, out double best)
        {
            best = max;
            bool any = false;
            for (int t = 0; t < m.TriangleCount; t++)
            {
                var (a, b, c) = m.Triangle(t);
                var e1 = b - a;
                var e2 = c - a;
                var p = d.Cross(e2);
                double det = e1.Dot(p);
                if (Math.Abs(det) < 1e-18) continue;
                var s = o - a;
                double u = s.Dot(p) / det;
                if (u < 0 || u > 1) continue;
                var q = s.Cross(e1);
                double v = d.Dot(q) / det;
                if (v < 0 || u + v > 1) continue;
                double dist = e2.Dot(q) / det;
                if (dist >= 0 && dist < best)
                {
                    best = dist;
                    any = true;
                }
            }

            return any;
        }

        // ================================================================== file formats

        [Fact]
        public void StlBinaryAndAsciiRoundTripWithUnits()
        {
            var box = MeshModel.CreateBox(new Vec3(0.2, 0.1, 0.05)).Translate(new Vec3(0.1, 0.05, 0.025));
            box.Save(P("bin.stl"));
            box.Save(P("asc.stl"), new MeshSaveOptions { Binary = false });
            Assert.Equal(84 + 50 * 12, new FileInfo(P("bin.stl")).Length);
            Assert.StartsWith("solid", File.ReadAllText(P("asc.stl")));

            foreach (var f in new[] { "bin.stl", "asc.stl" })
            {
                var m = MeshModel.Load(P(f));
                Assert.Equal(8, m.Vertices.Count);
                Assert.Equal(12, m.TriangleCount);
                Assert.True(m.AnalyzeTopology().IsClosed);
                Near(new Vec3(0.2, 0.1, 0.05), m.Bounds.Max, 1e-9, f);
                Rel(0.2 * 0.1 * 0.05, MassProperties.FromMesh(m, 1).Volume, 1e-6, f + " volume");
            }

            // File numbers are millimetres by default.
            string text = File.ReadAllText(P("asc.stl"));
            Assert.Contains("vertex 200 100 50", text);

            var inches = MeshModel.Load(P("bin.stl"), new MeshLoadOptions { Units = MeshUnits.Inch });
            Rel(200 * 0.0254, inches.Bounds.Max.X, 1e-6, "inch");
            var scaled = MeshModel.Load(P("bin.stl"), new MeshLoadOptions { Scale = 2 });
            Rel(0.4, scaled.Bounds.Max.X, 1e-6, "scale");
            var unwelded = MeshModel.Load(P("bin.stl"), new MeshLoadOptions { Weld = false });
            Assert.Equal(36, unwelded.Vertices.Count);

            // Stream loading with sniffing, and a binary file whose header starts with "solid".
            using (var fs = File.OpenRead(P("bin.stl")))
                Assert.Equal(12, MeshModel.Load(fs, MeshFileFormat.Auto).TriangleCount);
            var bytes = File.ReadAllBytes(P("bin.stl"));
            Encoding.ASCII.GetBytes("solid trap").CopyTo(bytes, 0);
            File.WriteAllBytes(P("trap.stl"), bytes);
            Assert.Equal(12, MeshModel.Load(P("trap.stl")).TriangleCount);
        }

        [Fact]
        public void StlFaceColoursRoundTrip()
        {
            var box = MeshModel.CreateBox(new Vec3(1, 1, 1));
            box.TriangleColors = Enumerable.Range(0, 12).Select(t => t < 6 ? new MeshColor(255, 0, 0) : new MeshColor(0, 0, 255)).ToList();
            box.Save(P("c.stl"));
            var m = MeshModel.Load(P("c.stl"));
            Assert.NotNull(m.TriangleColors);
            Assert.Equal(new MeshColor(255, 0, 0), m.ColorOf(0));
            Assert.Equal(new MeshColor(0, 0, 255), m.ColorOf(11));
        }

        [Fact]
        public void ObjParsesPolygonsNegativeIndicesGroupsAndMaterials()
        {
            File.WriteAllText(P("part.mtl"), "# materials\nnewmtl red\nKd 1 0 0\nnewmtl blue_glass\nKd 0 0 1\nd 0.5\n");
            // Unit cube: bottom as a quad, top as a quad written with negative indices, sides
            // split over two objects, one side as a pentagon (extra point on an edge).
            var obj = new StringBuilder();
            obj.Append("# test\nmtllib part.mtl\n");
            obj.Append("v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nv 0 0 1\nv 1 0 1\nv 1 1 1\nv 0 1 1\nv 0.5 0 0\n");
            obj.Append("vt 0 0\nvt 1 0\nvt 1 1\nvn 0 0 -1\nvn 0 0 1\n");
            obj.Append("o base\nusemtl red\n");
            obj.Append("f 9//1 1/1/1 4/2/1 3/3/1 2//1\n"); // bottom pentagon (with edge point 9)
            obj.Append("f -5/1/2 -4/2/2 -3/3/2 -2/1/2\n"); // top: 5 6 7 8 (count = 9 → -5 = 5)
            obj.Append("g walls\nusemtl blue_glass\n");
            obj.Append("f 9 2 6 5 1\n"); // front (y=0), pentagon
            obj.Append("f 2 3 7 6\nf 3 4 8 7\n");
            obj.Append("usemtl red\nf 4 1 5 8\n");
            File.WriteAllText(P("part.obj"), obj.ToString());

            var m = MeshModel.Load(P("part.obj"));
            Assert.Equal(9, m.Vertices.Count);
            Assert.Equal(3 + 2 + 3 + 2 + 2 + 2, m.TriangleCount);
            Assert.Equal(3, m.Groups.Count); // (base, red), (walls, blue_glass), (walls, red)
            Assert.Equal("base", m.Groups[0].Name);
            Assert.Equal(new MeshColor(255, 0, 0), m.Groups[0].Color);
            Assert.Equal(new MeshColor(0, 0, 255, 128), m.Groups[1].Color);
            Assert.Equal(new MeshColor(255, 0, 0), m.ColorOf(m.TriangleCount - 1));
            Assert.NotNull(m.Normals);
            Near(new Vec3(0, 0, -1), m.Normals![0], 1e-12, "vn");
            Near(Vec3.Zero, m.Normals[m.Normals.Count - 1], 0, "faces without vn");

            var topo = m.AnalyzeTopology();
            Assert.True(topo.IsClosed, topo.ToString());
            var mp = MassProperties.FromMesh(m, 1000);
            Rel(1000, mp.Mass, 1e-12, "unit cube in metres");
            Near(new Vec3(0.5, 0.5, 0.5), mp.CenterOfMass, 1e-12);

            // Split by group, save/reload with materials.
            Assert.Equal(3, m.SplitByGroup().Count);
            m.Save(P("out.obj"));
            Assert.True(File.Exists(P("out.mtl")));
            var back = MeshModel.Load(P("out.obj"));
            Assert.Equal(m.TriangleCount, back.TriangleCount);
            Assert.Equal(new MeshColor(255, 0, 0), back.ColorOf(0));
            Rel(1000, MassProperties.FromMesh(back, 1000).Mass, 1e-12);

            Assert.Throws<FormatException>(() => MeshModel.LoadObj(new MemoryStream(Encoding.ASCII.GetBytes("v 0 0 0\nf 1 2 3\n"))));
        }

        [Fact]
        public void YUpModelsAreRotatedToZUp()
        {
            // A 1 × 2 × 3 box authored Y-up: 3 along Y means 3 tall.
            var yup = MeshModel.CreateBox(new Vec3(1, 3, 2));
            for (int i = 0; i < yup.Vertices.Count; i++) yup.Vertices[i] = new Vec3(yup.Vertices[i].X, yup.Vertices[i].Y + 1.5, yup.Vertices[i].Z);
            yup.Save(P("yup.obj"));
            var m = MeshModel.Load(P("yup.obj"), new MeshLoadOptions { UpAxis = MeshUpAxis.Y, Units = MeshUnits.Centimeter });
            var size = m.Bounds.Size;
            Near(new Vec3(0.01, 0.02, 0.03), size, 1e-12, "size");
            Assert.True(Math.Abs(m.Bounds.Min.Z) < 1e-12, "stands on z = 0");
            Assert.True(MassProperties.FromMesh(m, 1).Volume > 0, "winding preserved");

            // Saving back as Y-up restores the file coordinates.
            m.Save(P("yup2.obj"), new MeshSaveOptions { UpAxis = MeshUpAxis.Y, Units = MeshUnits.Centimeter });
            var again = MeshModel.Load(P("yup2.obj"));
            Near(new Vec3(0.5, 3, 1), again.Bounds.Max, 1e-9);
        }

        [Fact]
        public void PlyAsciiAndBinaryWithColoursRoundTrip()
        {
            var cyl = MeshModel.CreateCylinder(0.03, 0.05, 32);
            cyl.VertexColors = cyl.Vertices.Select((v, i) => new MeshColor((byte)i, 100, 200)).ToList();
            cyl.Save(P("v.ply"));
            cyl.Save(P("va.ply"), new MeshSaveOptions { Binary = false });
            Assert.Contains("binary_little_endian", File.ReadAllText(P("v.ply")).Substring(0, 40));
            foreach (var f in new[] { "v.ply", "va.ply" })
            {
                var m = MeshModel.Load(P(f));
                Assert.Equal(cyl.Vertices.Count, m.Vertices.Count);
                Assert.Equal(cyl.TriangleCount, m.TriangleCount);
                Assert.Equal(cyl.VertexColors, m.VertexColors);
                Assert.True(m.AnalyzeTopology().IsClosed);
                Rel(MassProperties.FromMesh(cyl, 1).Volume, MassProperties.FromMesh(m, 1).Volume, 1e-6, f);
            }

            // Hand-written ASCII PLY: quad faces with per-face float colours, extra element and comments.
            string ply = "ply\nformat ascii 1.0\ncomment made by hand\nelement vertex 8\nproperty float x\nproperty float y\nproperty float z\n" +
                         "element face 6\nproperty list uchar int vertex_indices\nproperty float red\nproperty float green\nproperty float blue\n" +
                         "element edge 1\nproperty int vertex1\nproperty int vertex2\nend_header\n" +
                         "0 0 0\n1 0 0\n1 1 0\n0 1 0\n0 0 1\n1 0 1\n1 1 1\n0 1 1\n" +
                         "4 0 3 2 1 1 0 0\n4 4 5 6 7 0 1 0\n4 0 1 5 4 0 0 1\n4 1 2 6 5 0 0 1\n4 2 3 7 6 0 0 1\n4 3 0 4\n7 0 0 1\n0 1\n";
            File.WriteAllText(P("hand.ply"), ply);
            var h = MeshModel.Load(P("hand.ply"));
            Assert.Equal(12, h.TriangleCount);
            Assert.Equal(new MeshColor(255, 0, 0), h.ColorOf(0));
            Assert.Equal(new MeshColor(0, 255, 0), h.ColorOf(2));
            Rel(1, MassProperties.FromMesh(h, 1).Volume, 1e-12, "hand ply cube");
        }

        [Fact]
        public void EmbeddedFormatRoundTrips()
        {
            var m = MeshModel.CreateSphere(0.05, 40, 20);
            m.Name = "dome";
            m.Groups.Add(new MeshGroup { Name = "shell", Material = "pla", Color = new MeshColor(10, 20, 30, 40) });
            m.TriangleGroups = Enumerable.Repeat(0, m.TriangleCount).ToList();
            m.TriangleColors = Enumerable.Range(0, m.TriangleCount).Select(i => new MeshColor((byte)i, 2, 3)).ToList();
            string json = m.ToEmbeddedJson();
            var back = MeshModel.FromEmbeddedJson(json);
            Assert.Equal("dome", back.Name);
            Assert.Equal(m.Indices, back.Indices);
            Assert.Equal(m.TriangleColors, back.TriangleColors);
            Assert.Equal(m.TriangleGroups, back.TriangleGroups);
            Assert.Equal(new MeshColor(10, 20, 30, 40), back.Groups[0].Color);
            for (int i = 0; i < m.Vertices.Count; i++) Assert.True((m.Vertices[i] - back.Vertices[i]).Length < 1e-8);
            Rel(MassProperties.FromMesh(m, 1).Volume, MassProperties.FromMesh(back, 1).Volume, 1e-6);

            var raw = m.ToEmbedded(compress: false);
            Assert.True(json.Length < raw.Positions.Length + raw.Indices.Length, "deflate makes it smaller");
            Assert.Contains("\"positions\"", json);
            // The POCO can live inside other JSON documents.
            var e = EmbeddedMesh.FromJson(json);
            Assert.Equal(m.TriangleCount, e.TriangleCount);
            Assert.Equal(16, e.IndexBits);
        }

        [Fact]
        public void MillionTriangleBinaryStlParsesQuickly()
        {
            var big = MeshModel.CreateSphere(50, 1000, 501); // radius 50 m → written as mm
            Assert.Equal(1_000_000, big.TriangleCount);
            big.Save(P("big.stl"));
            Assert.Equal(84 + 50L * 1_000_000, new FileInfo(P("big.stl")).Length);
            big = null!;
            GC.Collect();

            MeshModel.CreateSphere(1, 30, 15).Save(P("warm.stl"));
            MeshModel.Load(P("warm.stl")); // JIT warm-up
            var sw = Stopwatch.StartNew();
            var m = MeshModel.Load(P("big.stl"));
            sw.Stop();
            _out.WriteLine($"1M-triangle binary STL (welded): {sw.ElapsedMilliseconds} ms, {m.Vertices.Count} vertices");
            Assert.Equal(1_000_000, m.TriangleCount);
            Assert.Equal(1000 * 500 + 2, m.Vertices.Count);
            Assert.True(sw.Elapsed.TotalSeconds < 3, $"parse took {sw.Elapsed.TotalSeconds:F2} s");

            sw.Restart();
            var mp = MassProperties.FromMesh(m, 1000);
            _out.WriteLine($"mass properties incl. topology check: {sw.ElapsedMilliseconds} ms");
            Assert.True(mp.IsClosed);
            Rel(4.0 / 3 * Math.PI * 50 * 50 * 50, mp.Volume, 0.001, "volume");

            sw.Restart();
            var bvh = new MeshBvh(m);
            _out.WriteLine($"BVH build: {sw.ElapsedMilliseconds} ms, depth {bvh.Depth}");
            Assert.True(bvh.Raycast(new Vec3(-200, 0.3, 0.2), Vec3.UnitX, 1000, out var hit));
            Assert.True(Math.Abs(hit.Distance - 150) < 0.01);
        }
    }
}
