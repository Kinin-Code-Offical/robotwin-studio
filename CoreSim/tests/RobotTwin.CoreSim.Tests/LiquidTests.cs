using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[liquid] Particle liquid against hydrostatics, a dynamic container, the dam-break experiment and a real pour.</summary>
    public class LiquidTests
    {
        private readonly ITestOutputHelper _out;

        public LiquidTests(ITestOutputHelper output) => _out = output;

        private static SceneBody Wall(PhysicsScene scene, Vec3 centre, Vec3 size)
        {
            var b = new SceneBody { Shape = CollisionShape.Box(size), Position = centre, IsStatic = true };
            scene.AddBody(b);
            return b;
        }

        [Fact]
        public void WaterAtRestPressesOnTheWallWithItsHydrostaticForce()
        {
            // 6 cm of water in a 6 × 6 cm tank: on one side wall F = ρ g D² L / 2.
            var scene = new PhysicsScene();
            double L = 0.06, t = 0.01, H = 0.10, D = 0.06, s = 0.004;
            var side = Wall(scene, new Vec3(-L / 2 - t / 2, 0, H / 2), new Vec3(t, L + 2 * t, H));
            Wall(scene, new Vec3(L / 2 + t / 2, 0, H / 2), new Vec3(t, L + 2 * t, H));
            Wall(scene, new Vec3(0, -L / 2 - t / 2, H / 2), new Vec3(L, t, H));
            Wall(scene, new Vec3(0, L / 2 + t / 2, H / 2), new Vec3(L, t, H));
            var water = new LiquidBody("w", LiquidKind.Water, s);
            water.Add(LiquidBody.Lattice(new Vec3(-L / 2, -L / 2, 0), new Vec3(L / 2, L / 2, D), s, _ => true));
            double expectedMass = 998.2 * L * L * D;
            var watch = Stopwatch.StartNew();
            var force = new List<double>();
            for (int i = 0; i < 1500; i++)
            {
                scene.Step(1e-3);
                water.Step(scene, 1e-3);
                water.ApplyForces();
                if (i >= 1000 && water.LastForces.TryGetValue(side, out var f)) force.Add(f.X);
            }

            double fx = force.Average(), exact = -998.2 * 9.81 * D * D * L / 2;
            _out.WriteLine($"{water.Count} particles ({water.TotalMass * 1000:F1} g, exact {expectedMass * 1000:F1} g) in {watch.Elapsed.TotalSeconds:F1} s: side force {fx:F4} N, hydrostatic {exact:F4} N ({(fx / exact - 1) * 100:+0.0;-0.0} %); interior ρ {water.MeanInteriorDensity:F1} kg/m³, max compression {water.MaxCompression * 100:F2} %");
            Assert.InRange(water.TotalMass / expectedMass, 0.999, 1.001);
            Assert.InRange(fx / exact, 0.95, 1.05);
            Assert.InRange(water.MeanInteriorDensity / 998.2, 0.99, 1.01);
            Assert.True(water.MaxCompression < 0.03, "the liquid stays incompressible within 3 %");
        }

        [Fact]
        public void AFreeTankFeelsTheWeightOfItsWaterAndStaysPut()
        {
            var scene = new PhysicsScene();
            double L = 0.06, t = 0.005, H = 0.08, s = 0.004;
            CollisionShape Piece(Vec3 c, Vec3 size)
            {
                var h = size * 0.5;
                var pts = new List<Vec3>();
                foreach (double x in new[] { -h.X, h.X })
                foreach (double y in new[] { -h.Y, h.Y })
                foreach (double z in new[] { -h.Z, h.Z })
                    pts.Add(c + new Vec3(x, y, z));
                return CollisionShape.ConvexHull(pts, 8);
            }

            var tank = new SceneBody { Shape = CollisionShape.Box(new Vec3(L + 2 * t, L + 2 * t, H)), Position = new Vec3(0, 0, H / 2), Mass = 0.15 };
            tank.Pieces = new[]
            {
                Piece(new Vec3(0, 0, -H / 2 + t / 2), new Vec3(L + 2 * t, L + 2 * t, t)),
                Piece(new Vec3(-L / 2 - t / 2, 0, t / 2), new Vec3(t, L + 2 * t, H - t)),
                Piece(new Vec3(L / 2 + t / 2, 0, t / 2), new Vec3(t, L + 2 * t, H - t)),
                Piece(new Vec3(0, -L / 2 - t / 2, t / 2), new Vec3(L, t, H - t)),
                Piece(new Vec3(0, L / 2 + t / 2, t / 2), new Vec3(L, t, H - t))
            };
            scene.AddBody(tank);
            var water = new LiquidBody("w", LiquidKind.Water, s);
            water.Add(LiquidBody.Lattice(new Vec3(-L / 2, -L / 2, t), new Vec3(L / 2, L / 2, t + 0.05), s, _ => true));
            var fz = new List<double>();
            for (int i = 0; i < 1500; i++)
            {
                scene.Step(1e-3);
                water.Step(scene, 1e-3);
                water.ApplyForces();
                if (i >= 1000 && water.LastForces.TryGetValue(tank, out var f)) fz.Add(f.Z);
            }

            double weight = water.TotalMass * 9.81, mean = -fz.Average();
            _out.WriteLine($"force of the water on the tank {mean:F4} N, its weight {weight:F4} N ({(mean / weight - 1) * 100:+0.0;-0.0} %); tank moved {Math.Abs(tank.Position.Z - H / 2) * 1000:F3} mm");
            Assert.InRange(mean / weight, 0.97, 1.03);
            Assert.True(Math.Abs(tank.Position.Z - H / 2) < 0.001, "the tank stays on the floor");
            Assert.True(tank.Velocity.Length < 0.01);
        }

        [Fact]
        public void ADamBreakFrontRunsLikeMartinAndMoyce()
        {
            // Martin & Moyce (1952), column a wide and 2a high: front Z = x/a against T = t·√(2g/a).
            var scene = new PhysicsScene();
            double a = 0.05715, W = 0.03, t = 0.01, Lc = 0.4, H = 0.2, s = 0.004;
            Wall(scene, new Vec3(-t / 2, W / 2, H / 2), new Vec3(t, W + 2 * t, H));
            Wall(scene, new Vec3(Lc + t / 2, W / 2, H / 2), new Vec3(t, W + 2 * t, H));
            Wall(scene, new Vec3(Lc / 2, -t / 2, H / 2), new Vec3(Lc, t, H));
            Wall(scene, new Vec3(Lc / 2, W + t / 2, H / 2), new Vec3(Lc, t, H));
            var water = new LiquidBody("w", LiquidKind.Water, s);
            water.Add(LiquidBody.Lattice(Vec3.Zero, new Vec3(a, W, 2 * a), s, _ => true));
            double[] refT = { 0.84, 1.43, 1.98, 2.51, 2.98 };
            double[] refZ = { 1.22, 1.67, 2.33, 3.00, 3.67 };
            double scale = Math.Sqrt(2 * 9.81 / a);
            int next = 0;
            double worst = 0;
            for (int i = 0; i < 400 && next < refT.Length; i++)
            {
                scene.Step(1e-3);
                water.Step(scene, 1e-3);
                water.ApplyForces();
                if ((i + 1) * 1e-3 * scale < refT[next]) continue;
                var xs = Enumerable.Range(0, water.Count).Select(water.Position).Where(p => p.Z < 0.02).Select(p => p.X).OrderBy(x => x).ToList();
                double front = (xs[xs.Count - 3] + s / 2) / a;
                double dev = front / refZ[next] - 1;
                worst = Math.Max(worst, Math.Abs(dev));
                _out.WriteLine($"T {refT[next]:F2}: Z {front:F2}, Martin & Moyce {refZ[next]:F2} ({dev * 100:+0.0;-0.0} %)");
                next++;
            }

            Assert.Equal(refT.Length, next);
            Assert.True(worst < 0.12, "the surge front within 12 % of the experiment (its gate was pulled, not removed at once)");
        }

        [Esp32Fact]
        public void TheGlassOfWaterIsPouredIntoTheBowl()
        {
            var w = WorldExamples.GlassPour(PartLibrary.Default);
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-glass-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var options = new RunnerOptions
            {
                Esp32Builder = src =>
                {
                    var build = new Esp32SketchCompiler().Compile(src, dir);
                    Assert.True(build.Success, string.Join("\n", build.Diagnostics) + "\n" + build.Log);
                    return build.DllPath;
                }
            };
            using var r = new WorldRunner(w, options);
            var liquid = r.Physics!.Liquids.Single().Body;
            var glass = w.Objects.First(o => o.Name == "Bardak");
            var bowl = w.Objects.First(o => o.Name == "Kâse");
            int start = liquid.Count;
            double mass = liquid.TotalMass;
            var watch = Stopwatch.StartNew();
            double maxTilt = 0;
            for (int i = 0; i < 110; i++)
            {
                r.Step(0.1);
                r.TryGetBody(glass.Id, out var g);
                double tilt = Math.Acos(Math.Max(-1, Math.Min(1, g.Orientation.Rotate(Vec3.UnitZ).Z))) * 180 / Math.PI;
                maxTilt = Math.Max(maxTilt, tilt);
                if (i % 10 == 9)
                {
                    var com = liquid.CentreOfMass();
                    _out.WriteLine($"t {(i + 1) * 0.1:F1} s: glass tilted {tilt:F0}°, water centre ({com.X * 1000:F0}, {com.Y * 1000:F0}, {com.Z * 1000:F0}) mm, {liquid.LastSubsteps} substeps, {watch.Elapsed.TotalSeconds:F0} s");
                }
            }

            r.TryGetBody(bowl.Id, out var b);
            double rb = 0.075 - 0.003;
            int inBowl = Enumerable.Range(0, liquid.Count).Select(liquid.Position).Count(p =>
                Math.Sqrt((p.X - b.Position.X) * (p.X - b.Position.X) + (p.Y - b.Position.Y) * (p.Y - b.Position.Y)) < rb && p.Z < b.Position.Z + 0.02 + 0.01);
            string serial = string.Concat(r.ReadSerial(null, false).Values);
            _out.WriteLine($"{inBowl} of {start} particles in the bowl ({inBowl * 100.0 / start:F0} %), mass kept {liquid.TotalMass / mass * 100:F1} %, largest tilt {maxTilt:F0}°; serial: {serial.Replace("\n", " | ")}");
            Assert.Contains("Bitti", serial);
            Assert.True(maxTilt > 75, "the servo tips the glass");
            Assert.Equal(start, liquid.Count);
            Assert.True(inBowl > 0.6 * start, "most of the water ends up in the bowl");
        }
    }
}
