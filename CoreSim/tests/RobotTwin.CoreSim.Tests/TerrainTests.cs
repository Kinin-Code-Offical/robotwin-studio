using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[terrain] Editable terrain: sampling, sculpt math, surface layers in the physics, persistence.</summary>
    public class TerrainTests
    {
        private const double G = 9.81;
        private readonly ITestOutputHelper _out;

        public TerrainTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static void Run(PhysicsScene s, double seconds, double dt = 1e-3)
        {
            int n = (int)Math.Round(seconds / dt);
            for (int i = 0; i < n; i++) s.Step(dt);
        }

        private static TerrainMap Map(double w = 4, double d = 4, double cell = 0.05, string layer = "concrete") => new TerrainMap(w, d, cell, 0, 0, layer);

        // ================================================================== sampling

        [Fact]
        public void BilinearSamplingReproducesAPlaneExactly()
        {
            var m = Map(3, 2, 0.1);
            m.Fill((x, y) => 0.2 + 0.1 * x - 0.05 * y);
            var rnd = new Random(3);
            for (int i = 0; i < 200; i++)
            {
                double x = rnd.NextDouble() * 2.8 - 1.4, y = rnd.NextDouble() * 1.8 - 0.9;
                Assert.Equal(0.2 + 0.1 * x - 0.05 * y, m.Height(x, y), 5);
                var (dx, dy) = m.Gradient(x, y);
                Assert.Equal(0.1, dx, 5);
                Assert.Equal(-0.05, dy, 5);
            }

            var n = m.Normal(0.3, 0.2);
            var expected = new Vec3(-0.1, 0.05, 1).Normalized();
            Assert.True((n - expected).Length < 1e-6);
            var vn = m.VertexNormal(10, 7);
            Assert.True((vn - expected).Length < 1e-6, "vertex normal by central differences");
        }

        [Fact]
        public void BilinearInterpolationBetweenVerticesAndEdgeClamping()
        {
            var m = Map(1, 1, 0.5);   // 3 × 3 vertices
            Assert.Equal(3, m.Columns);
            m.Heights[m.Index(1, 1)] = 1f;   // centre peak at (0, 0)
            m.NotifyAll();
            Assert.Equal(1.0, m.Height(0, 0), 6);
            Assert.Equal(0.5, m.Height(0.25, 0), 6);          // halfway along an edge
            Assert.Equal(0.25, m.Height(0.25, 0.25), 6);      // bilinear: (1-fx)(1-fy)
            Assert.Equal(m.Height(0.5, 0.2), m.Height(3, 0.2), 9); // outside: edge continues
            Assert.Equal((0.0, 1.0), m.HeightRange);
        }

        [Fact]
        public void RaycastFindsTheSurface()
        {
            var m = Map(4, 4, 0.05);
            m.Fill((x, y) => 0.1 * Math.Sin(2 * x) * Math.Cos(1.5 * y));
            var o = new Vec3(0.37, -0.41, 2);
            Assert.True(m.Raycast(o, new Vec3(0, 0, -1), 10, out double t));
            Assert.Equal(m.Height(0.37, -0.41), 2 - t, 5);
            var d = new Vec3(0.3, 0.2, -1).Normalized();
            Assert.True(m.Raycast(o, d, 10, out t));
            var p = o + d * t;
            Assert.True(Math.Abs(p.Z - m.Height(p.X, p.Y)) < 1e-5);
            Assert.False(m.Raycast(new Vec3(10, 10, 1), new Vec3(0, 0, -1), 5, out _, insideOnly: true));
            Assert.False(m.Raycast(o, new Vec3(0, 0, 1), 10, out _));
        }

        // ================================================================== brush math

        [Fact]
        public void BrushFalloffAndRaiseLowerSmoothFlatten()
        {
            Assert.Equal(1, TerrainSculpt.Falloff(0.1, 0.5, 0.4), 9);
            Assert.Equal(1, TerrainSculpt.Falloff(0.2, 0.5, 0.4), 9);
            Assert.Equal(0.5, TerrainSculpt.Falloff(0.35, 0.5, 0.4), 9);   // midway of the soft ring
            Assert.Equal(0, TerrainSculpt.Falloff(0.5, 0.5, 0.4), 9);

            var m = Map(4, 4, 0.05);
            var b = new TerrainBrush { Radius = 0.5, Hardness = 0.4 };
            TerrainSculpt.Raise(m, b, 0, 0, 0.1);
            Assert.Equal(0.1, m.Height(0, 0), 5);
            Assert.Equal(0.1, m.Height(0.2, 0), 5);
            Assert.Equal(0.05, m.Height(0.35, 0), 5);
            Assert.Equal(0, m.Height(0.6, 0), 9);
            TerrainSculpt.Raise(m, b, 0, 0, -0.04);
            Assert.Equal(0.06, m.Height(0, 0), 5);

            // Smoothing lowers the peak and keeps the volume roughly.
            double before = m.Heights.Sum(h => (double)h);
            for (int i = 0; i < 30; i++) TerrainSculpt.Smooth(m, new TerrainBrush { Radius = 1.5, Hardness = 1 }, 0, 0, 1);
            Assert.True(m.Height(0, 0) < 0.05);
            Assert.Equal(before, m.Heights.Sum(h => (double)h), 1);

            // Flatten with rate 1 levels the hard core exactly.
            TerrainSculpt.Flatten(m, new TerrainBrush { Radius = 0.4, Hardness = 0.5 }, 0, 0, 0.02, 1);
            Assert.Equal(0.02, m.Height(0.1, -0.1), 5);
        }

        [Fact]
        public void StampsRampsAndRoadsHaveTheirShapes()
        {
            var m = Map(6, 4, 0.02);
            // Speed bump 30 mm high, 150 mm long across x, 1 m wide.
            TerrainSculpt.Stamp(m, TerrainStamp.SpeedBump, -1, 0, 0, 0.03, 0.15, 1.0);
            Assert.Equal(0.03, m.Height(-1, 0), 4);
            Assert.Equal(0.0, m.Height(-1 + 0.08, 0), 4);
            Assert.True(m.Height(-1 + 0.0375, 0) > 0.012 && m.Height(-1 + 0.0375, 0) < 0.018, "cos² profile halfway");

            // Pothole 40 mm deep with a flat bottom.
            TerrainSculpt.Stamp(m, TerrainStamp.Pothole, 1, 1, 0, 0.04, 0.4, 0.4);
            Assert.Equal(-0.04, m.Height(1.05, 1), 4);

            // Kerb 40 mm: full height on the strip, nothing beyond.
            TerrainSculpt.Stamp(m, TerrainStamp.Kerb, 1, -1, 90, 0.04, 0.1, 0.8);
            Assert.Equal(0.04, m.Height(1, -1), 4);
            Assert.Equal(0, m.Height(1, -0.8), 4);

            // Ramp from 0 to 0.2 m over 2 m: linear in between, sides untouched far away.
            var r = Map(6, 4, 0.05);
            TerrainSculpt.Ramp(r, -1, 0, 0, 1, 0, 0.2, 0.6, 1);
            Assert.Equal(0.1, r.Height(0, 0), 4);
            Assert.Equal(0.15, r.Height(0.5, 0.1), 4);
            Assert.Equal(0, r.Height(0, 1), 9);

            // Road: painted asphalt inside its width, original layer outside, level across.
            var road = Map(6, 4, 0.05, "grass");
            road.Fill((x, y) => 0.1 * y);
            TerrainSculpt.Road(road, new[] { (-2.0, 0.0), (0.0, 0.3), (2.0, 0.0) }, 0.6, TerrainLayers.IndexOf("asphalt"), true);
            Assert.Equal("asphalt", road.LayerAt(0, 0.3).Id);
            Assert.Equal("grass", road.LayerAt(0, -1.2).Id);
            Assert.True(Math.Abs(road.Height(0, 0.45) - road.Height(0, 0.15)) < 0.004, "road levelled across");
        }

        [Fact]
        public void PaintKeepsWeightsNormalisedAndUndoRestoresTheStroke()
        {
            var m = Map(2, 2, 0.05, "grass");
            var b = new TerrainBrush { Radius = 0.3, Hardness = 0.5, Layer = TerrainLayers.IndexOf("mud") };
            var heights = (float[])m.Heights.Clone();
            m.History.BeginStroke("çamur");
            for (int i = 0; i < 10; i++) TerrainSculpt.Paint(m, b, 0, 0, 0.3);
            TerrainSculpt.Raise(m, b, 0, 0, 0.05);
            Assert.True(m.History.EndStroke(m));
            Assert.Equal("mud", m.LayerAt(0, 0).Id);
            for (int i = 0; i < m.VertexCount; i++)
            {
                int sum = m.ActiveLayers.Sum(l => m.LayerWeights(l)![i]);
                Assert.Equal(255, sum);
            }

            var w = new double[TerrainLayers.Count];
            m.SampleLayers(0.26, 0, w);
            Assert.True(w[TerrainLayers.IndexOf("mud")] > 0.05 && w[TerrainLayers.IndexOf("grass")] > 0.05, "blended at the border");
            Assert.Equal(1.0, w.Sum(), 6);

            Assert.Equal("çamur", m.History.Undo(m));
            Assert.Equal("grass", m.LayerAt(0, 0).Id);
            Assert.Equal(heights, m.Heights);
            Assert.Equal("çamur", m.History.Redo(m));
            Assert.Equal("mud", m.LayerAt(0, 0).Id);
            Assert.Equal(0.05, m.Height(0, 0), 5);
        }

        // ================================================================== physics on the terrain

        private static PhysicsScene Scene(TerrainMap m) => new PhysicsScene { Terrain = new TerrainSurface(m), AirDensity = 0 };

        [Fact]
        public void SphereRestsOnTheTerrainAtTheRightHeight()
        {
            var m = Map(2, 2, 0.05);
            m.Fill((x, y) => 0.2);
            var s = Scene(m);
            var b = s.AddBody(SceneBody.Sphere("s", 0.03, 0.1, new Vec3(0.1, 0.1, 0.3), "steel"));
            Run(s, 1.0);
            _out.WriteLine($"z = {b.Position.Z * 1000:F2} mm");
            Assert.True(Math.Abs(b.Position.Z - 0.23) < 1e-3);
            Assert.True(b.Velocity.Length < 1e-3);

            // On a 15° hill the sphere keeps its radius from the surface along the normal while it rolls.
            var hill = Map(6, 2, 0.05);
            double angle = 15 * Math.PI / 180;
            hill.Fill((x, y) => Math.Tan(angle) * x + 1);
            var s2 = Scene(hill);
            var ball = s2.AddBody(SceneBody.Sphere("s", 0.03, 0.1, new Vec3(1, 0, 1 + Math.Tan(angle) + 0.03 / Math.Cos(angle)), "rubber"));
            Run(s2, 0.6);
            double sep = hill.Separation(ball.Position);
            _out.WriteLine($"gap along normal {sep * 1000:F2} mm, v = {ball.Velocity.Length:F3} m/s");
            Assert.True(Math.Abs(sep - 0.03) < 1e-3);
            // Rolling solid sphere: a = 5/7 g sin θ (minus a little rolling resistance).
            double vExpected = 5.0 / 7 * G * Math.Sin(angle) * 0.6;
            Assert.True(Math.Abs(ball.Velocity.Length - vExpected) < 0.1 * vExpected);
        }

        private static (SceneBody Box, PhysicsScene Scene) BoxOnSlope(string layer, double angleDeg)
        {
            double angle = angleDeg * Math.PI / 180;
            var m = new TerrainMap(4, 2, 0.05, 0, 0, layer);
            m.Fill((x, y) => Math.Tan(angle) * x);
            var s = Scene(m);
            var q = Quat.FromAxisAngle(Vec3.UnitY, -angle);
            var b = s.AddBody(SceneBody.Box("b", new Vec3(0.1, 0.1, 0.05), 0.3, q.Rotate(new Vec3(0, 0, 0.0249)), "wood"));
            b.Orientation = q;
            return (b, s);
        }

        [Fact]
        public void BoxStaysOnGripLayerAndSlidesOnIceWhenMuBelowTanSlope()
        {
            // tan 20° = 0.364. Rubber mat (hard plastic μs 0.70) holds; ice (μs 0.08, μk 0.05) does not.
            var (stay, s1) = BoxOnSlope("rubbermat", 20);
            Run(s1, 0.3);
            var p0 = stay.Position;
            Run(s1, 1.5);
            Assert.True((stay.Position - p0).Length < 1e-3, "box held by the rubber mat");

            var (slide, s2) = BoxOnSlope("ice", 20);
            Run(s2, 0.2);
            double v0 = slide.Velocity.Length;
            Run(s2, 0.5);
            double a = (slide.Velocity.Length - v0) / 0.5;
            var ice = TerrainLayers.Find("ice")!.Plastic;
            double angle = 20 * Math.PI / 180;
            double expected = G * (Math.Sin(angle) - ice.Kinetic * Math.Cos(angle));
            _out.WriteLine($"a = {a:F3} m/s² (expected {expected:F3})");
            Assert.True(Math.Abs(a - expected) < 0.05 * expected);
        }

        [Fact]
        public void RollingResistanceDependsOnThePaintedSurface()
        {
            double Coast(string layer)
            {
                var m = new TerrainMap(30, 2, 0.1, 10, 0, layer);
                var s = Scene(m);
                const double r = 0.033;
                var wheel = SceneBody.Cylinder("w", r, 0.025, 0.05, new Vec3(0, 0, r), "rubber");
                wheel.Orientation = Quat.FromAxisAngle(Vec3.UnitX, Math.PI / 2);
                wheel.Velocity = new Vec3(1.0, 0, 0);
                wheel.AngularVelocity = new Vec3(0, 1.0 / r, 0);
                s.AddBody(wheel);
                Run(s, 2.0);
                return wheel.Position.X;
            }

            double asphalt = Coast("asphalt"), grass = Coast("grass"), sand = Coast("sand");
            _out.WriteLine($"coasting 2 s from 1 m/s: asphalt {asphalt:F3} m, grass {grass:F3} m, sand {sand:F3} m");
            Assert.True(asphalt > grass + 0.1 && grass > sand + 0.05);
            // Asphalt: v decays by g·Crr → x = v0 t − ½ g Crr t² within a few percent.
            double crr = TerrainLayers.Find("asphalt")!.Rubber.Rolling;
            double expected = 2.0 - 0.5 * G * crr * 4;
            Assert.True(Math.Abs(asphalt - expected) < 0.1 * expected, $"asphalt {asphalt:F3} vs {expected:F3}");
        }

        [Fact]
        public void TyreSinksIntoLooseGroundButNotIntoAsphalt()
        {
            var m = new TerrainMap(2, 2, 0.05, 0, 0, "asphalt");
            var b = new TerrainBrush { Radius = 0.3, Hardness = 1, Layer = TerrainLayers.IndexOf("sand") };
            TerrainSculpt.Paint(m, b, 0.5, 0.5, 1);
            var ground = new TerrainSurface(m);
            double n = 5;
            Assert.False(ground.WheelSoil(-0.5, -0.5, 0.033, 0.025, 900, 0, ref n, out _, out _, out _, out _));
            Assert.Equal(5, n);
            Assert.True(ground.WheelSoil(0.5, 0.5, 0.033, 0.025, 900, 0, ref n, out double sink, out double crr, out _, out double share));
            _out.WriteLine($"sand: sinkage {sink * 1000:F1} mm, N {n:F2} N, Crr {crr:F3}");
            Assert.Equal(1, share, 6);
            Assert.True(sink > 0.002 && sink <= 0.35 * 0.033 + 1e-9);
            Assert.True(n < 5 && n > 0);
            Assert.Equal(5 - 900 * sink, n, 2);   // the spring carries what the soil gives way
            Assert.True(crr > TerrainLayers.Find("asphalt")!.Rubber.Rolling * 5);
            var rubber = Materials.IndexOf("rubber");
            Assert.Equal(TerrainLayers.Find("asphalt")!.Rubber.Static, ground.Contact(rubber, -0.5, -0.5).StaticFriction, 6);
        }

        [Fact]
        public void BekkerSinkageMatchesTheClosedFormForDrySand()
        {
            // Wong, dry sand: n = 1.1, k_c = 0.99 kN/m^2.1, k_φ = 1528.43 kN/m^3.1.
            var sand = TerrainLayers.Find("sand")!.Soil!.Value;
            double W = 5, b = 0.025, D = 0.066;
            double expected = Math.Pow(3 * W / ((3 - 1.1) * (990 + b * 1.53e6) * Math.Sqrt(D)), 2 / (2 * 1.1 + 1));
            Assert.Equal(expected, sand.Sinkage(W, D / 2, b), 9);
            // Heavier load sinks deeper, wider tyre less deep.
            Assert.True(sand.Sinkage(10, D / 2, b) > sand.Sinkage(5, D / 2, b));
            Assert.True(sand.Sinkage(3, D / 2, 0.05) < sand.Sinkage(3, D / 2, 0.025));
            // Slip sinkage: a spinning tyre digs in.
            Assert.True(sand.Sinkage(1, D / 2, b, 0.8) > 1.5 * sand.Sinkage(1, D / 2, b, 0));
            // Plate: z = (p / (k_c/b + k_φ))^(1/n).
            double p = 2000;
            Assert.Equal(Math.Pow(p / (990 / 0.1 + 1.53e6), 1 / 1.1), sand.PlateSinkage(p, 0.1), 9);
        }

        [Fact]
        public void SoilTractionNeedsSlipAndIsLimitedByShearStrength()
        {
            var sand = TerrainLayers.Find("sand")!.Soil!.Value;
            double L = 0.02;
            // Janosi–Hanamoto: 1 − K/(sL)(1 − e^(−sL/K)) rises with slip toward 1.
            double j1 = sand.ShearDevelopment(0.05, L, 0), j2 = sand.ShearDevelopment(0.3, L, 0), j3 = sand.ShearDevelopment(5, L, 0);
            Assert.True(j1 < j2 && j2 < j3 && j3 < 1);
            double sL = 0.3 * L, K = sand.ShearModulus;
            Assert.Equal(1 - K / sL * (1 - Math.Exp(-sL / K)), j2, 9);
            // Mohr–Coulomb: (A c + W tan φ)/W.
            Assert.Equal((1e-3 * 1040 + 4 * Math.Tan(28 * Math.PI / 180)) / 4, sand.PeakTraction(4, 1e-3), 9);
            // Mud is far weaker than sand under the same tyre.
            var mud = TerrainLayers.Find("mud")!.Soil!.Value;
            Assert.True(mud.PeakTraction(3, 5e-4) < 0.6 * sand.PeakTraction(3, 5e-4));

            var m = new TerrainMap(2, 2, 0.05, 0, 0, "sand");
            var ground = new TerrainSurface(m);
            double n1 = 3, n2 = 3;
            ground.WheelSoil(0, 0, 0.033, 0.025, 900, 0.02, ref n1, out _, out _, out double t1, out _);
            ground.WheelSoil(0, 0, 0.033, 0.025, 900, 0.5, ref n2, out _, out _, out double t2, out _);
            _out.WriteLine($"sand traction coefficient: slip 2 % → {t1:F3}, slip 50 % → {t2:F3}");
            Assert.True(t2 > 1.3 * t1);   // (2 % slip is lifted by the 30 % creep floor)
        }

        [Fact]
        public void DrawbarPullTestSlipsMoreOnSandAndStallsInMud()
        {
            // A 1 kg four-wheel cart drives 0.06 N·m into each wheel against a 1.5 N drawbar load.
            (double Slip, double X) Pull(string layer)
            {
                var m = new TerrainMap(20, 3, 0.1, 8, 0, layer);
                var s = Scene(m);
                var body = s.AddBody(SceneBody.Box("cart", new Vec3(0.2, 0.15, 0.04), 1.0, new Vec3(0, 0, 0.045), "pla"));
                foreach (var (x, y) in new[] { (0.07, 0.09), (0.07, -0.09), (-0.07, 0.09), (-0.07, -0.09) })
                    s.Wheels.Add(new Wheel { Body = body, Mount = new Vec3(x, y, -0.01), Radius = 0.033, Width = 0.025, Drive = WheelDrive.Torque, DriveTorque = 0.06 });
                double slip = 0;
                int k = 0;
                for (int i = 0; i < 2000; i++)
                {
                    body.AddForce(new Vec3(-1.5, 0, 0));
                    s.Step(1e-3);
                    if (i < 500) continue;
                    double vx = body.Velocity.X;
                    slip += s.Wheels.Average(w => { double vw = w.AngularVelocity * w.Radius; return Math.Abs(vw - vx) / Math.Max(0.02, Math.Max(Math.Abs(vw), Math.Abs(vx))); });
                    k++;
                }

                return (slip / k, body.Position.X);
            }

            var asphalt = Pull("asphalt");
            var sand = Pull("sand");
            var mud = Pull("mud");
            _out.WriteLine($"drawbar 1.5 N: asphalt slip {asphalt.Slip:P1} x {asphalt.X:F2} m · sand slip {sand.Slip:P1} x {sand.X:F2} m · mud slip {mud.Slip:P1} x {mud.X:F2} m");
            Assert.True(sand.Slip > asphalt.Slip * 2, "sand needs slip to build up shear");
            Assert.True(mud.Slip > 0.5, "mud cannot carry the load: the wheels spin");
            Assert.True(asphalt.X > sand.X && sand.X > mud.X);
        }

        [Fact]
        public void RigidWheelAndBoxSinkIntoSandByTheirFootprint()
        {
            var m = new TerrainMap(4, 2, 0.05, 0, 0, "sand");
            var s = Scene(m);
            const double r = 0.033;
            var wheel = SceneBody.Cylinder("w", r, 0.025, 0.25, new Vec3(-1, 0, r), "rubber");
            wheel.Orientation = Quat.FromAxisAngle(Vec3.UnitX, Math.PI / 2);
            s.AddBody(wheel);
            var plate = s.AddBody(SceneBody.Box("p", new Vec3(0.2, 0.2, 0.02), 0.25, new Vec3(1, 0, 0.01), "pla"));
            Run(s, 1.0);
            double zw = s.SoilSinkage(wheel), zp = s.SoilSinkage(plate);
            _out.WriteLine($"same weight: wheel sinks {zw * 1000:F2} mm (centre z {wheel.Position.Z * 1000:F1} mm), 200 mm plate sinks {zp * 1000:F3} mm");
            Assert.True(zw > 0.001);
            Assert.True(zp < zw / 5);
            Assert.True(wheel.Position.Z < r - 0.0008, "the wheel sits in its rut");
        }

        [Fact]
        public void RayCastWheelsCoastFurtherOnAsphaltAndSitLowerInSand()
        {
            (double X, double Z) Cart(string layer)
            {
                var m = new TerrainMap(20, 3, 0.1, 8, 0, layer);
                var s = Scene(m);
                var body = s.AddBody(SceneBody.Box("cart", new Vec3(0.2, 0.15, 0.04), 1.0, new Vec3(0, 0, 0.06), "pla"));
                foreach (var (x, y) in new[] { (0.07, 0.09), (0.07, -0.09), (-0.07, 0.09), (-0.07, -0.09) })
                {
                    s.Wheels.Add(new Wheel
                    {
                        Body = body, Mount = new Vec3(x, y, -0.01), Radius = 0.033, Width = 0.025,
                        Drive = WheelDrive.Torque, AngularVelocity = 1.0 / 0.033
                    });
                }

                body.Velocity = new Vec3(1.0, 0, 0);
                Run(s, 1.5);
                return (body.Position.X, body.Position.Z);
            }

            var asphalt = Cart("asphalt");
            var sand = Cart("sand");
            _out.WriteLine($"asphalt x={asphalt.X:F3} z={asphalt.Z * 1000:F1} mm · sand x={sand.X:F3} z={sand.Z * 1000:F1} mm");
            Assert.True(asphalt.X > sand.X + 0.2, "sand brakes the coasting cart");
            Assert.True(sand.Z < asphalt.Z - 0.002, "tyres sink into sand");
        }

        // ================================================================== world integration

        private static WorldBodyGeometry Geometry(WorldObject o) => new WorldBodyGeometry
        {
            Min = new Vec3(-o.SizeX / 2, -o.SizeY / 2, 0), Max = new Vec3(o.SizeX / 2, o.SizeY / 2, o.SizeZ), MassKg = 0.1,
            Shape = o.Shape == "sphere" ? "sphere" : "box"
        };

        [Fact]
        public void WorldBodiesLandOnTheSculptedTerrainAndStaticPropsCollidePiecewise()
        {
            var w = new WorldDocument { Title = "Arazi" };
            w.Terrain = new TerrainMap(4, 4, 0.05, 0, 0, "dirt");
            TerrainSculpt.Stamp(w.Terrain, TerrainStamp.Bump, 1, 1, 0, 0.3, 1.6, 1.6);
            var box = w.AddBody("box", 0.1, 0.1, 0.05, new WorldPose { X = 1, Y = 1, Z = 0.6 });
            var stairs = WorldBuilding.AddProp(w, "stairs", -1, -1);
            var ball = w.AddBody("sphere", 0.04, 0.04, 0.04, new WorldPose { X = -1 + 0.12, Y = -1, Z = 0.4 });
            Assert.True(stairs.IsStatic);
            var p = WorldPhysics.Build(w, Geometry, null);
            Assert.Equal(3, p.Scene.Bodies.Count(b => b.Id.StartsWith(stairs.Id + "#", StringComparison.Ordinal)));
            p.Advance(1.5, null);
            var bp = p.PoseOf(box.Id)!.Value.Position;
            var sp = p.PoseOf(ball.Id)!.Value.Position;
            _out.WriteLine($"box base z {bp.Z * 1000:F1} mm (bump top 300 mm), ball base z {sp.Z * 1000:F1} mm (top step 120 mm)");
            // The flat box bears on its corners on the dome (≈ 294 mm), not on the 300 mm summit.
            Assert.True(Math.Abs(bp.Z - w.Terrain.Height(1.05, 1.05)) < 1.5e-3);
            Assert.Equal(0.12, sp.Z, 2);
            Assert.Equal("dirt", p.Scene.GroundMaterialAt(1, 1));
        }

        [Fact]
        public void TerrainSurvivesBinaryAndWorldFileRoundTrips()
        {
            var m = new TerrainMap(3, 2, 0.04, 0.5, -0.25, "grass");
            m.Fill((x, y) => 0.05 * Math.Sin(3 * x) + 0.02 * y);
            TerrainSculpt.Paint(m, new TerrainBrush { Radius = 0.4, Layer = TerrainLayers.IndexOf("gravel") }, 0.5, 0, 1);
            var back = TerrainMap.FromBytes(m.ToBytes());
            Assert.Equal(m.Columns, back.Columns);
            Assert.Equal(m.Rows, back.Rows);
            Assert.Equal(m.OriginX, back.OriginX);
            Assert.Equal(m.Heights, back.Heights);
            Assert.Equal(m.ActiveLayers, back.ActiveLayers);
            foreach (int l in m.ActiveLayers) Assert.Equal(m.LayerWeights(l), back.LayerWeights(l));

            var w = new WorldDocument { Title = "Arazi testi", Terrain = m };
            WorldBuilding.AddProp(w, "cone", 0.6, 0.1);
            string path = Path.Combine(Path.GetTempPath(), "rw-terrain-" + Guid.NewGuid().ToString("N") + WorldDocument.Extension);
            try
            {
                w.Save(path);
                var loaded = WorldDocument.Load(path);
                Assert.NotNull(loaded.Terrain);
                Assert.Equal(m.Heights, loaded.Terrain!.Heights);
                Assert.Equal("gravel", loaded.Terrain.LayerAt(0.5, 0).Id);
                var cone = loaded.Objects.Single();
                Assert.Equal("cone", cone.Parameters[TerrainProps.Key]);
                Assert.Equal(m.Height(0.6, 0.1), cone.Pose.Z, 5);
                Assert.NotNull(loaded.Clone().Terrain);
                Assert.Null(WorldDocument.FromJson(loaded.ToJson()).Terrain);   // undo snapshots stay small
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void PropMeshesAreClosedAndStandOnTheGround()
        {
            foreach (var def in TerrainProps.All)
            {
                var mesh = TerrainProps.BuildMesh(def.Id, new Vec3(def.SizeX, def.SizeY, def.SizeZ));
                mesh.Validate();
                var b = mesh.Bounds;
                Assert.Equal(0, b.Min.Z, 6);
                Assert.Equal(0, (b.Min.X + b.Max.X) / 2, 6);
                Assert.True(Math.Abs(b.Max.Z - def.SizeZ) < 0.15 * def.SizeZ, def.Id);
                if (def.Dynamic)
                {
                    var mp = MassProperties.FromMesh(mesh, 1000);
                    Assert.True(mp.Mass > 0, def.Id + " has a positive volume (outward winding)");
                }
            }
        }
    }

    internal static class TerrainTestExtensions
    {
        /// <summary>Distance of a point above the surface along the local normal.</summary>
        public static double Separation(this TerrainMap m, Vec3 p) => new TerrainSurface(m).Separation(p, out _, out _);

    }
}
