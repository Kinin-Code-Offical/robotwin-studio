using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sdk;
using RobotTwin.CoreSim.Sdk.Expressions;
using RobotTwin.CoreSim.Tests.SdkSamples;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests.SdkSamples
{
    // Behaviours that live in the test assembly and are registered through PluginHost.RegisterAssembly,
    // the same path a plugin DLL takes (type discovery, parameters, isolation).

    [Behavior("Sayaç")]
    public sealed class CountingBehavior : WorldBehavior
    {
        public static int Steps;

        [Parameter("İtme", Unit = "N")] public double Push { get; set; } = 0.5;

        public override void OnStep(IBehaviorContext ctx, double dt)
        {
            Steps++;
            ctx.Self?.ApplyForce(new Vec3(Push, 0, 0));
            ctx.Publish("adım", Steps);
        }
    }

    [Behavior("Hata atan")]
    public sealed class ThrowingBehavior : WorldBehavior
    {
        public override void OnStep(IBehaviorContext ctx, double dt) => throw new InvalidOperationException("kasıtlı hata");
    }

    [Behavior("Yavaş")]
    public sealed class SlowBehavior : WorldBehavior
    {
        public override void OnStep(IBehaviorContext ctx, double dt)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < 0.3) { }
        }
    }

    public sealed class UpdraftSource : IWindSource
    {
        public Vec3 WindAt(Vec3 point, double time) => new Vec3(0, 0, 2);
    }
}

namespace RobotTwin.CoreSim.Tests
{
    public class SdkTests
    {
        private readonly ITestOutputHelper _out;

        public SdkTests(ITestOutputHelper output) => _out = output;

        private static void Rel(double expected, double actual, double tol, string what) =>
            Assert.True(Math.Abs(actual - expected) <= tol * Math.Abs(expected), $"{what}: expected {expected:G6}, got {actual:G6} (tolerance {tol:P1})");

        private static WorldBodyGeometry Geometry(WorldObject o)
        {
            double mass = o.MassKg ?? 0.05;
            if (o.Shape == "sphere")
            {
                double r = o.SizeX / 2;
                return new WorldBodyGeometry { Min = new Vec3(-r, -r, -r), Max = new Vec3(r, r, r), Shape = "sphere", MassKg = mass };
            }

            return new WorldBodyGeometry { Min = new Vec3(-o.SizeX / 2, -o.SizeY / 2, -o.SizeZ / 2), Max = new Vec3(o.SizeX / 2, o.SizeY / 2, o.SizeZ / 2), MassKg = mass };
        }

        private static WorldDocument ZeroG()
        {
            var w = new WorldDocument();
            w.Environment.Gravity = 0;
            return w;
        }

        // ================================================================ air

        [Fact]
        public void StandardAtmosphereDensityAndPressure()
        {
            Rel(1.2250, Atmosphere.Density(15, 101325), 0.001, "ISA sea-level density");
            Rel(89875, Atmosphere.PressureAt(101325, 1000), 0.002, "pressure at 1000 m");
            Assert.True(Atmosphere.Density(30, 101325, 1.0) < Atmosphere.Density(30, 101325, 0.0), "humid air is lighter");
            Rel(1.789e-5, Atmosphere.Viscosity(15), 0.005, "ISA viscosity at 15 °C");
        }

        [Fact]
        public void FallingSphereReachesAnalyticTerminalVelocity()
        {
            var w = new WorldDocument();
            w.Environment.AmbientC = 15;
            var ball = w.AddBody("sphere", 0.04, 0.04, 0.04, new WorldPose { Z = 80 });
            ball.MassKg = 0.0027;
            var p = WorldPhysics.Build(w, Geometry, null);
            var rt = p.Sdk!;
            Rel(1.2250, rt.AirDensity, 0.001, "density at 15 °C, sea level");
            double r = 0.02, m = 0.0027, g = 9.81;
            double cda = Aerodynamics.CdSphere * Math.PI * r * r;
            double vt = Math.Sqrt(2 * m * g / (rt.AirDensity * cda));
            var body = p.Scene.FindBody(ball.Id)!;
            // v(t) = vt·tanh(g·t/vt) for quadratic drag from rest.
            for (int i = 0; i < 1000; i++) p.Advance(1e-3, null);
            double v1 = -body.Velocity.Z;
            Rel(vt * Math.Tanh(g * 1.0 / vt), v1, 0.01, "speed after 1 s");
            for (int i = 0; i < 4000; i++) p.Advance(1e-3, null);
            _out.WriteLine($"vt analytic {vt:F4} m/s, simulated {-body.Velocity.Z:F4} m/s, v(1 s) {v1:F4}");
            Rel(vt, -body.Velocity.Z, 0.005, "terminal velocity");
        }

        [Fact]
        public void WindPushesALightBoxWithTheDragForce()
        {
            var w = ZeroG();
            w.Environment.AmbientC = 20;
            w.Sdk.Wind.Speed = 5;
            w.Sdk.Wind.DirectionDeg = 0;
            var box = w.AddBody("box", 0.1, 0.2, 0.1, new WorldPose { Z = 1 });
            box.MassKg = 0.02;
            var p = WorldPhysics.Build(w, Geometry, null);
            var rt = p.Sdk!;
            var body = p.Scene.FindBody(box.Id)!;
            p.Advance(1e-3, null);
            double expected = 0.5 * rt.AirDensity * Aerodynamics.CdBox * 0.2 * 0.1 * 25;   // face 0.2 × 0.1 m normal to x
            double a0 = body.Velocity.X / 1e-3;
            _out.WriteLine($"F expected {expected:F5} N, measured {a0 * 0.02:F5} N");
            Rel(expected, a0 * 0.02, 0.02, "wind force");
            Assert.True(Math.Abs(body.Velocity.Y) < 1e-9 && Math.Abs(body.Velocity.Z) < 1e-9);
            var ro = rt.Readout(box.Id)!.Value;
            Rel(5, ro.Wind.X, 1e-9, "wind readout");
            // Direction convention: 90° blows towards +Y.
            w.Sdk.Wind.DirectionDeg = 90;
            var p2 = WorldPhysics.Build(w, Geometry, null);
            p2.Advance(1e-3, null);
            Assert.True(p2.Scene.FindBody(box.Id)!.Velocity.Y > 0);
        }

        [Fact]
        public void PerObjectDragOverrideAndDisable()
        {
            var w = ZeroG();
            w.Sdk.Wind.Speed = 4;
            var a = w.AddBody("box", 0.1, 0.1, 0.1, new WorldPose { Z = 1 });
            var b = w.AddBody("box", 0.1, 0.1, 0.1, new WorldPose { Y = 1, Z = 1 });
            w.Sdk.AeroFor(a.Id).Cd = 2.1;
            w.Sdk.AeroFor(b.Id).Drag = false;
            var p = WorldPhysics.Build(w, Geometry, null);
            p.Advance(1e-3, null);
            double expected = 0.5 * p.Sdk!.AirDensity * 2.1 * 0.01 * 16;
            Rel(expected, p.Scene.FindBody(a.Id)!.Velocity.X / 1e-3 * 0.05, 0.02, "drag with Cd override");
            Assert.Equal(0, p.Scene.FindBody(b.Id)!.Velocity.X);
        }

        [Fact]
        public void PowerLawProfileScalesWindWithHeight()
        {
            var s = new WindSettings { Speed = 10, Profile = "power", ReferenceHeight = 10, PowerExponent = 1.0 / 7 };
            var m = new WindModel(s, Vec3.Zero, 0, 1);
            Rel(10, m.At(new Vec3(0, 0, 10)).X, 1e-9, "at reference height");
            Rel(10 * Math.Pow(0.1, 1.0 / 7), m.At(new Vec3(0, 0, 1)).X, 1e-9, "at 1 m");
            var log = new WindModel(new WindSettings { Speed = 10, Profile = "log", ReferenceHeight = 10, Roughness = 0.03 }, Vec3.Zero, 0, 1);
            Rel(10 * Math.Log(1 / 0.03) / Math.Log(10 / 0.03), log.At(new Vec3(0, 0, 1)).X, 1e-9, "log profile at 1 m");
        }

        [Fact]
        public void DrydenTurbulenceHasTheRequestedStatistics()
        {
            var s = new WindSettings { Speed = 5, Turbulence = "dryden", Intensity = 1.0, VerticalRatio = 0.5, LengthScale = 10 };
            var m = new WindModel(s, Vec3.Zero, 0, seed: 42);
            double tau = m.CorrelationTime;
            Rel(2.0, tau, 1e-9, "correlation time L/V");
            const double dt = 0.01;
            const int n = 1_000_000;
            int lag = (int)Math.Round(tau / dt);
            var u = new double[n];
            double sumW2 = 0;
            for (int i = 0; i < n; i++)
            {
                m.Advance(dt);
                u[i] = m.Turbulence.X;
                sumW2 += m.Turbulence.Z * m.Turbulence.Z;
            }

            double mean = u.Average();
            double var = u.Sum(x => (x - mean) * (x - mean)) / n;
            double cov = 0;
            for (int i = 0; i + lag < n; i++) cov += (u[i] - mean) * (u[i + lag] - mean);
            double rho = cov / (n - lag) / var;
            _out.WriteLine($"mean {mean:F4}, σ {Math.Sqrt(var):F4}, σw {Math.Sqrt(sumW2 / n):F4}, ρ(τ) {rho:F4} (e⁻¹ = {Math.Exp(-1):F4})");
            Assert.InRange(mean, -0.06, 0.06);
            Rel(1.0, Math.Sqrt(var), 0.04, "horizontal σ");
            Rel(0.5, Math.Sqrt(sumW2 / n), 0.04, "vertical σ");
            Assert.InRange(rho, Math.Exp(-1) - 0.05, Math.Exp(-1) + 0.05);

            // Same seed → same sequence; another seed → another one.
            var a = new WindModel(s, Vec3.Zero, 0, 7);
            var b = new WindModel(s, Vec3.Zero, 0, 7);
            var c = new WindModel(s, Vec3.Zero, 0, 8);
            for (int i = 0; i < 1000; i++)
            {
                a.Advance(1e-3);
                b.Advance(1e-3);
                c.Advance(1e-3);
            }

            Assert.Equal(a.Turbulence.X, b.Turbulence.X);
            Assert.NotEqual(a.Turbulence.X, c.Turbulence.X);
        }

        [Fact]
        public void DiscreteGustsAreOneMinusCosineAndBounded()
        {
            var s = new WindSettings { Speed = 3, Gusts = true, GustAmplitude = 4, GustDuration = 1, GustInterval = 3 };
            var m = new WindModel(s, Vec3.Zero, 0, 5);
            double peak = 0;
            int gusts = 0;
            bool inGust = false;
            for (int i = 0; i < 600_000; i++)
            {
                m.Advance(1e-3);
                double g = m.Gust.Length;
                peak = Math.Max(peak, g);
                if (g > 0 && !inGust) gusts++;
                inGust = g > 0;
                Assert.True(Vector(m.Gust).Y == 0 && m.Gust.X >= 0, "gusts blow along the mean wind");
            }

            _out.WriteLine($"{gusts} gusts in 600 s, peak {peak:F3} m/s");
            Assert.InRange(peak, 3.0, 4.0 + 1e-9);
            Assert.InRange(gusts, 100, 200);   // mean period ≈ interval + duration = 4 s → ≈ 150
        }

        private static Vec3 Vector(Vec3 v) => v;

        [Fact]
        public void TurbulentWorldIsDeterministic()
        {
            Vec3 Run()
            {
                var w = ZeroG();
                w.Sdk.Seed = 99;
                w.Sdk.Wind.Speed = 3;
                w.Sdk.Wind.Turbulence = "dryden";
                w.Sdk.Wind.Intensity = 1.5;
                w.Sdk.Wind.Gusts = true;
                var o = w.AddBody("box", 0.1, 0.1, 0.1, new WorldPose { Z = 1 });
                var p = WorldPhysics.Build(w, Geometry, null);
                for (int i = 0; i < 2000; i++) p.Advance(1e-3, null);
                return p.Scene.FindBody(o.Id)!.Position;
            }

            var a = Run();
            var b = Run();
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y, b.Y);
            Assert.Equal(a.Z, b.Z);
            Assert.True(a.X > 0.01, "the wind moved the box");
        }

        // ================================================================ expression language

        private static double Eval(string e, params (string, double)[] vars) =>
            ExpressionProgram.Evaluate(e, vars.ToDictionary(v => v.Item1, v => v.Item2));

        [Fact]
        public void ExpressionPrecedenceAndFunctions()
        {
            Assert.Equal(14, Eval("2+3*4"));
            Assert.Equal(20, Eval("(2+3)*4"));
            Assert.Equal(-4, Eval("-2^2"));
            Assert.Equal(512, Eval("2^3^2"));
            Assert.Equal(0.5, Eval("2^-1"));
            Assert.Equal(8, Eval("2**3"));
            Assert.Equal(1, Eval("10 % 3"));
            Assert.Equal(2, Eval("-7 % 3"));
            Assert.Equal(2.5, Eval("10/4"));
            Assert.Equal(1, Eval("1 < 2 && 3 >= 3"));
            Assert.Equal(0, Eval("1 > 2 || !1"));
            Assert.Equal(1, Eval("1 + 1 == 2"));
            Assert.Equal(-1, Eval("x > 0 ? 1 : -1", ("x", -3)));
            Assert.Equal(3, Eval("x < 0 ? 1 : x < 5 ? 3 : 7", ("x", 2)));
            Assert.Equal(1, Eval("sin(pi/2)"), 12);
            Assert.Equal(5, Eval("max(1, 5, 3)"));
            Assert.Equal(-2, Eval("min(4, -2)"));
            Assert.Equal(2, Eval("clamp(7, 0, 2)"));
            Assert.Equal(5, Eval("hypot(3, 4)"), 12);
            Assert.Equal(Math.PI / 4, Eval("atan2(1, 1)"), 12);
            Assert.Equal(180, Eval("deg(pi)"), 12);
            Assert.Equal(1.5e-3, Eval("1.5e-3"));
            Assert.Equal(0.75, Eval("smoothstep(0, 1, 0.5) + 0.25"), 12);
            Assert.Equal(1, Eval("square(0.1, 1)"));
            Assert.Equal(Eval("noise(3.7, 2)"), Eval("noise(3.7, 2)"));
            Assert.InRange(Eval("noise(3.7)"), -1, 1);
            Assert.Equal(-6, Eval("-k*(x - x0)", ("k", 2), ("x", 4), ("x0", 1)));
        }

        [Fact]
        public void ExpressionProgramsAssignOutputsAndLocals()
        {
            var scope = new ExpressionScope();
            scope.Declare("x");
            scope.Declare("vx");
            scope.Declare("F.x", writable: true);
            scope.Alias("Fx", "F.x");
            var prog = ExpressionProgram.Compile("k = 20  # yay\nc = 0.5; // sönüm\nFx = -k*x - c*vx", scope);
            var slots = prog.Scope.NewSlots();
            slots[prog.Scope["x"]] = 0.1;
            slots[prog.Scope["vx"]] = 2;
            prog.Run(slots);
            Assert.Equal(-3, slots[prog.Scope["F.x"]], 12);
            Assert.Equal(new[] { "k", "c", "F.x" }, prog.Assigned);
            Assert.False(scope.Contains("k"), "locals do not leak into the caller's scope");
        }

        [Theory]
        [InlineData("2 + ", "İfade eksik")]
        [InlineData("(1 + 2", "')' bekleniyordu")]
        [InlineData("foo + 1", "Bilinmeyen değişken 'foo'")]
        [InlineData("sinn(1)", "Bilinmeyen fonksiyon")]
        [InlineData("max()", "bağımsız değişken")]
        [InlineData("atan2(1)", "2 bağımsız değişken")]
        [InlineData("2x", "'*'")]
        [InlineData("1 $ 2", "Beklenmeyen karakter")]
        [InlineData("sin", "parantezle")]
        public void ExpressionErrorsAreClearAndPositioned(string source, string fragment)
        {
            var ex = Assert.Throws<ExpressionException>(() => ExpressionProgram.Evaluate(source));
            Assert.Contains(fragment, ex.Message);
            Assert.True(ex.Line >= 1 && ex.Column >= 1);
        }

        [Fact]
        public void ExpressionErrorReportsLineAndColumnAndSuggests()
        {
            var scope = new ExpressionScope();
            scope.Declare("speed");
            scope.Declare("F.x", writable: true);
            var ex = Assert.Throws<ExpressionException>(() => ExpressionProgram.Compile("a = 1\nF.x = sped * 2", scope));
            Assert.Equal(2, ex.Line);
            Assert.Equal(7, ex.Column);
            Assert.Contains("speed", ex.Message);
            var ro = Assert.Throws<ExpressionException>(() => ExpressionProgram.Compile("speed = 3", scope));
            Assert.Contains("salt okunur", ro.Message);
            var unknownOut = Assert.Throws<ExpressionException>(() => ExpressionProgram.Compile("G.x = 3", scope));
            Assert.Contains("Bilinmeyen çıktı", unknownOut.Message);
            Assert.NotNull(ExpressionCheck.ForBehavior("k = 2"));   // no output
            Assert.Null(ExpressionCheck.ForBehavior("F.x = -k*(x - x0) - c*vx", new Dictionary<string, string> { ["k"] = "3", ["c"] = "0.1" }));
            Assert.NotNull(ExpressionCheck.ForBehavior("wind.x = 3"));   // wind is read-only on objects
            Assert.Null(ExpressionCheck.ForField("wind.x = 3*sin(t)"));
        }

        [Fact]
        public void ExpressionBehaviorAppliesItsForce()
        {
            var w = ZeroG();
            var o = w.AddBody("box", 0.1, 0.1, 0.1, new WorldPose { Z = 1 });
            o.MassKg = 0.5;
            w.Sdk.Aero.Drag = false;
            w.Sdk.Behaviors.Add(new BehaviorAttachment { Id = "B1", ObjectId = o.Id, Expression = "F.x = 2\nFL.y = 1", Parameters = { } });
            var p = WorldPhysics.Build(w, Geometry, null);
            for (int i = 0; i < 1000; i++) p.Advance(1e-3, null);
            var b = p.Scene.FindBody(o.Id)!;
            Rel(0.5 * 4 * 1.0, b.Position.X, 0.01, "x = ½·(F/m)·t²");
            Rel(2.0, b.Velocity.Y, 0.01, "body-frame force (unrotated body)");
        }

        [Fact]
        public void ExpressionSpringOscillatesWithTheRightPeriod()
        {
            var w = ZeroG();
            var o = w.AddBody("box", 0.1, 0.1, 0.1, new WorldPose { X = 0.1, Z = 1 });
            o.MassKg = 0.2;
            w.Sdk.Aero.Drag = false;
            w.Sdk.Behaviors.Add(new BehaviorAttachment { Id = "B1", ObjectId = o.Id, Expression = "F.x = -k*(x - x0 + 0.1)", Parameters = { ["k"] = "20" } });
            var p = WorldPhysics.Build(w, Geometry, null);
            var b = p.Scene.FindBody(o.Id)!;
            double period = 2 * Math.PI * Math.Sqrt(0.2 / 20);
            int steps = (int)Math.Round(period / 1e-3);
            double minX = double.MaxValue;
            for (int i = 0; i < steps; i++)
            {
                p.Advance(1e-3, null);
                minX = Math.Min(minX, b.Position.X);
            }

            Assert.InRange(minX, -0.102, -0.098);   // swings to the other side of x = 0
            Assert.InRange(b.Position.X, 0.098, 0.102);   // back after one period (symplectic Euler, 1 ms)
        }

        // ================================================================ fields

        [Fact]
        public void FieldsActOnBodies()
        {
            var w = ZeroG();
            w.Sdk.Aero.Drag = false;
            var a = w.AddBody("box", 0.05, 0.05, 0.05, new WorldPose { X = 1, Z = 1 });
            a.MassKg = 0.1;
            var c = w.AddBody("box", 0.05, 0.05, 0.05, new WorldPose { X = -3, Z = 1 });
            c.MassKg = 0.1;
            // Attractor at the origin: 2 N at r = 1 m, inverse-square.
            w.Sdk.Fields.Add(new ForceFieldSpec { Id = "F1", Kind = "radial", Strength = 2, Radius = 1, Falloff = "inverse-square", Z = 1, Targets = { a.Id } });
            // Conveyor slab around the second body: belt 0.3 m/s along +y.
            w.Sdk.Fields.Add(new ForceFieldSpec { Id = "F2", Kind = "conveyor", X = -3, Z = 1, SizeX = 1, SizeY = 10, SizeZ = 0.2, DirX = 0, DirY = 1, Strength = 0.3, Grip = 100 });
            var p = WorldPhysics.Build(w, Geometry, null);
            p.Advance(1e-3, null);
            Rel(-2 / 0.1 * 1e-3, p.Scene.FindBody(a.Id)!.Velocity.X, 0.01, "attractor acceleration");
            for (int i = 0; i < 1000; i++) p.Advance(1e-3, null);
            var cb = p.Scene.FindBody(c.Id)!;
            Rel(0.3, cb.Velocity.Y, 0.01, "carried at belt speed");
            Assert.True(Math.Abs(cb.Velocity.X) < 1e-6);
        }

        [Fact]
        public void ConveyorCarriesAParcelOverAFrictionalFloor()
        {
            var w = new WorldDocument();
            var parcel = w.AddBody("box", 0.08, 0.08, 0.06, new WorldPose { X = -0.6, Z = 0.03 }, "AHŞAP");
            parcel.MassKg = 0.2;
            w.Sdk.Fields.Add(new ForceFieldSpec { Id = "F1", Kind = "conveyor", Z = 0.05, SizeX = 1.6, SizeY = 0.2, SizeZ = 0.15, Strength = 0.25 });
            var p = WorldPhysics.Build(w, Geometry, null);
            for (int i = 0; i < 2000; i++) p.Advance(1e-3, null);
            var b = p.Scene.FindBody(parcel.Id)!;
            _out.WriteLine($"parcel v = {b.Velocity}, x = {b.Position.X:F3}");
            Rel(0.25, b.Velocity.X, 0.1, "parcel rides at about the belt speed");
            Assert.True(b.Position.X > -0.3);
        }

        [Fact]
        public void FanAndExpressionWindBlowOnlyWhereTheyShould()
        {
            var w = ZeroG();
            var inStream = w.AddBody("box", 0.05, 0.05, 0.05, new WorldPose { X = 0.2, Z = 0.5 });
            var outside = w.AddBody("box", 0.05, 0.05, 0.05, new WorldPose { X = 0.2, Y = 2, Z = 0.5 });
            w.Sdk.Fields.Add(new ForceFieldSpec { Id = "F1", Kind = "fan", X = 0.5, Z = 0.5, SizeX = 1, SizeY = 0.3, SizeZ = 0.3, DirX = 1, Strength = 6 });
            w.Sdk.Fields.Add(new ForceFieldSpec { Id = "F2", Kind = "expression", Expression = "wind.z = y > 1 ? 3 : 0" });
            var p = WorldPhysics.Build(w, Geometry, null);
            var rt = p.Sdk!;
            Assert.True(rt.WindAt(new Vec3(0.2, 0, 0.5)).X > 3, "inside the fan stream");
            Assert.Equal(0, rt.WindAt(new Vec3(0.2, 2, 0.5)).X);
            Assert.Equal(3, rt.WindAt(new Vec3(0.2, 2, 0.5)).Z);
            p.Advance(1e-3, null);
            Assert.True(p.Scene.FindBody(inStream.Id)!.Velocity.X > 0);
            var ob = p.Scene.FindBody(outside.Id)!;
            Assert.Equal(0, ob.Velocity.X);
            Assert.True(ob.Velocity.Z > 0, "expression updraft");
            var preview = WorldSdkRuntime.Preview(w);
            Assert.True(preview(new Vec3(0.2, 0, 0.5)).X > 3);
        }

        // ================================================================ plugins

        private static PluginManifest TestManifest(string id) => new PluginManifest
        {
            Id = id, Name = "Test", EntryTypes = { typeof(CountingBehavior).FullName!, typeof(ThrowingBehavior).FullName!, typeof(SlowBehavior).FullName!, typeof(UpdraftSource).FullName! }
        };

        [Fact]
        public void InAssemblyBehaviorsRegisterThroughThePluginPath()
        {
            var host = new PluginHost(new BehaviorRegistry(), new PartLibrary());
            var loaded = host.RegisterAssembly(typeof(SdkTests).Assembly, TestManifest("testasm"));
            Assert.Null(loaded.Error);
            var counting = host.Registry.Find("testasm:" + typeof(CountingBehavior).FullName)!;
            Assert.Equal("Sayaç", counting.Name);
            var push = Assert.Single(counting.Parameters);
            Assert.Equal("İtme", push.Label);
            Assert.Equal("0.5", push.DefaultText);
            Assert.Contains(host.Registry.Types, t => t.Kind == SdkTypeKind.WindSource);
            var inst = (CountingBehavior)BehaviorRegistry.Create(counting, new Dictionary<string, string> { ["Push"] = "1.25" });
            Assert.Equal(1.25, inst.Push);
            var warnings = new List<string>();
            BehaviorRegistry.Create(counting, new Dictionary<string, string> { ["Push"] = "abc" }, warnings.Add);
            Assert.Single(warnings);
        }

        [Fact]
        public void ExceptionsAreIsolatedReportedAndTheOffenderIsSwitchedOff()
        {
            var host = new PluginHost(BehaviorRegistry.Shared, new PartLibrary());
            host.RegisterAssembly(typeof(SdkTests).Assembly, TestManifest("isolation.test"));
            var w = ZeroG();
            w.Sdk.Aero.Drag = false;
            w.Sdk.EnabledPlugins.Add("isolation.test");
            var o = w.AddBody("box", 0.1, 0.1, 0.1, new WorldPose { Z = 1 });
            o.MassKg = 1;
            w.Sdk.Behaviors.Add(new BehaviorAttachment { Id = "B1", ObjectId = o.Id, Type = "isolation.test:" + typeof(ThrowingBehavior).FullName, Name = "Bozuk" });
            w.Sdk.Behaviors.Add(new BehaviorAttachment { Id = "B2", ObjectId = o.Id, Type = "isolation.test:" + typeof(CountingBehavior).FullName, Parameters = { ["Push"] = "1" } });
            int before = CountingBehavior.Steps;
            var p = WorldPhysics.Build(w, Geometry, null);
            for (int i = 0; i < 10; i++) p.Advance(1e-3, null);
            var events = p.Sdk!.DrainEvents();
            foreach (var e in events) _out.WriteLine(e.ToString());
            Assert.Equal(3, events.Count(e => e.Level == SdkEventLevel.Error && e.Message.Contains("kasıtlı hata")));
            Assert.Contains(events, e => e.Message.Contains("devre dışı bırakıldı"));
            var status = p.Sdk.Status.First(s => s.Id == "B1");
            Assert.False(status.Active);
            Assert.Equal(3, status.Errors);
            Assert.True(p.Sdk.Status.First(s => s.Id == "B2").Active);
            Rel(0.01, p.Scene.FindBody(o.Id)!.Velocity.X, 0.01, "the healthy behaviour kept pushing (1 N · 10 ms / 1 kg)");
            Assert.Equal(10, CountingBehavior.Steps - before);
            Assert.True(p.Sdk.Published.ContainsKey(o.Name + ".adım"));
        }

        [Fact]
        public void PluginBehaviorsOnlyRunWhereEnabledAndSlowOnesAreWarned()
        {
            var host = new PluginHost(BehaviorRegistry.Shared, new PartLibrary());
            host.RegisterAssembly(typeof(SdkTests).Assembly, TestManifest("budget.test"));
            var w = ZeroG();
            var o = w.AddBody("box", 0.1, 0.1, 0.1, new WorldPose { Z = 1 });
            w.Sdk.Behaviors.Add(new BehaviorAttachment { Id = "B1", ObjectId = o.Id, Type = "budget.test:" + typeof(SlowBehavior).FullName });
            var p = WorldPhysics.Build(w, Geometry, null);
            p.Advance(1e-3, null);
            Assert.Contains(p.Sdk!.DrainEvents(), e => e.Message.Contains("etkin değil"));

            w.Sdk.EnabledPlugins.Add("budget.test");
            w.Sdk.StepBudgetMs = 0.1;
            var p2 = WorldPhysics.Build(w, Geometry, null);
            for (int i = 0; i < 2000; i++) p2.Advance(1e-3, null);
            Assert.Contains(p2.Sdk!.DrainEvents(), e => e.Level == SdkEventLevel.Warning && e.Message.Contains("bütçe"));
        }

        /// <summary>Emits a real plugin DLL (no compiler needed): a WorldBehavior pushing its object along +x with <paramref name="force"/> N.</summary>
        private static void EmitPlugin(string path, double force)
        {
            var ab = new PersistedAssemblyBuilder(new AssemblyName("EmittedPushPlugin"), typeof(object).Assembly);
            var mb = ab.DefineDynamicModule("EmittedPushPlugin");
            var tb = mb.DefineType("Emitted.Pusher", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, typeof(WorldBehavior));
            var baseCtor = typeof(WorldBehavior).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, Type.EmptyTypes)!;
            var ctor = tb.DefineConstructor(MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, CallingConventions.Standard, Type.EmptyTypes);
            var cil = ctor.GetILGenerator();
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Call, baseCtor);
            cil.Emit(OpCodes.Ret);
            var step = tb.DefineMethod(nameof(WorldBehavior.OnStep), MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(void), new[] { typeof(IBehaviorContext), typeof(double) });
            var il = step.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, typeof(IBehaviorContext).GetProperty(nameof(IBehaviorContext.Self))!.GetMethod!);
            il.Emit(OpCodes.Ldc_R8, force);
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Newobj, typeof(Vec3).GetConstructor(new[] { typeof(double), typeof(double), typeof(double) })!);
            il.Emit(OpCodes.Callvirt, typeof(ISimBody).GetMethod(nameof(ISimBody.ApplyForce))!);
            il.Emit(OpCodes.Ret);
            tb.CreateType();
            using var fs = File.Create(path);
            ab.Save(fs);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double RunWithPlugin(string pluginId)
        {
            var w = ZeroG();
            w.Sdk.Aero.Drag = false;
            w.Sdk.EnabledPlugins.Add(pluginId);
            var o = w.AddBody("box", 0.1, 0.1, 0.1, new WorldPose { Z = 1 });
            o.MassKg = 1;
            w.Sdk.Behaviors.Add(new BehaviorAttachment { Id = "B1", ObjectId = o.Id, Type = pluginId + ":Emitted.Pusher" });
            var p = WorldPhysics.Build(w, Geometry, null);
            for (int i = 0; i < 100; i++) p.Advance(1e-3, null);
            var errors = p.Sdk!.DrainEvents().Where(e => e.Level == SdkEventLevel.Error).ToList();
            Assert.Empty(errors);
            p.Sdk.Stop();
            return p.Scene.FindBody(o.Id)!.Velocity.X;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static LoadedPlugin LoadFrom(PluginHost host, string dir) => host.LoadDirectory(dir).Single();

        [Fact]
        public void PluginDllLoadsRunsUnloadsAndReloads()
        {
            string dir = Path.Combine(Path.GetTempPath(), "rw-sdk-test-" + Guid.NewGuid().ToString("N"));
            string pluginDir = Path.Combine(dir, "pusher");
            Directory.CreateDirectory(pluginDir);
            try
            {
                const string id = "emitted.pusher";
                EmitPlugin(Path.Combine(pluginDir, "EmittedPushPlugin.dll"), 1.0);
                File.WriteAllText(Path.Combine(pluginDir, PluginManifest.FileName),
                    "{ \"id\": \"" + id + "\", \"name\": \"İtici\", \"version\": \"1.0.0\", \"author\": \"test\", \"assembly\": \"EmittedPushPlugin.dll\" }");
                var host = new PluginHost(BehaviorRegistry.Shared, new PartLibrary());
                Assert.Single(PluginHost.Discover(dir));
                var loaded = LoadFrom(host, dir);
                Assert.True(loaded.IsLoaded, loaded.Error);
                Assert.True(loaded.CanUnload);
                Assert.Equal("İtici", loaded.Manifest.Name);
                Assert.NotNull(BehaviorRegistry.Shared.Find(id + ":Emitted.Pusher"));
                Rel(0.1, RunWithPlugin(id), 1e-6, "Δv = F·t/m from the plugin's force");

                // Rebuild on disk (the DLL is not locked) and reload: the new code runs.
                EmitPlugin(Path.Combine(pluginDir, "EmittedPushPlugin.dll"), 3.0);
                var reloaded = host.Reload(id)!;
                Assert.True(reloaded.IsLoaded, reloaded.Error);
                Rel(0.3, RunWithPlugin(id), 1e-6, "force of the reloaded plugin");

                var weak = host.Unload(id)!;
                Assert.Null(BehaviorRegistry.Shared.Find(id + ":Emitted.Pusher"));
                for (int i = 0; i < 20 && weak.IsAlive; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

                Assert.False(weak.IsAlive, "the plugin's load context was collected");
            }
            finally
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public void BrokenPluginFoldersReportProblemsInsteadOfThrowing()
        {
            string dir = Path.Combine(Path.GetTempPath(), "rw-sdk-bad-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "bad"));
            try
            {
                File.WriteAllText(Path.Combine(dir, "bad", "NotADll.dll"), "this is not a PE image");
                File.WriteAllText(Path.Combine(dir, "bad", PluginManifest.FileName), "{ \"id\": \"bad.one\", \"assembly\": \"NotADll.dll\" }");
                var host = new PluginHost(new BehaviorRegistry(), new PartLibrary());
                var r = host.LoadDirectory(dir).Single();
                Assert.False(r.IsLoaded);
                Assert.Contains(".NET", r.Error);
                var missing = new PluginManifest { Id = "x", Assembly = "nope.dll", Directory = dir };
                Assert.Contains(missing.Validate(), e => e.Contains("bulunamadı"));
                Assert.Contains(new PluginManifest { Id = "a:b", Assembly = "nope.dll", Directory = dir }.Validate(), e => e.Contains("içeremez"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void BuiltinBehaviorsAreRegisteredAndWork()
        {
            var keys = BehaviorRegistry.Shared.OfKind(SdkTypeKind.Behavior).Where(t => t.PluginId == BehaviorRegistry.BuiltinId).Select(t => t.Name).ToList();
            Assert.Contains("Yay–sönümleyici", keys);
            Assert.Contains("Manyetik çekim", keys);
            Assert.Contains("Rüzgâr türbini torku", keys);

            var w = ZeroG();
            w.Sdk.Aero.Drag = false;
            var a = w.AddBody("box", 0.02, 0.02, 0.02, new WorldPose { Z = 1 });
            var b = w.AddBody("box", 0.02, 0.02, 0.02, new WorldPose { X = 0.03, Z = 1 });
            a.MassKg = b.MassKg = 0.01;
            w.Sdk.Behaviors.Add(new BehaviorAttachment
            {
                Id = "B1", ObjectId = a.Id, Type = "builtin:RobotTwin.CoreSim.Sdk.Builtins.MagneticAttraction",
                Parameters = { ["Target"] = b.Name, ["Strength"] = "1", ["Exponent"] = "4" }
            });
            var p = WorldPhysics.Build(w, Geometry, null);
            p.Advance(1e-3, null);
            // F = 1 N · (1 cm / 3 cm)⁴ on each, towards each other.
            double f = Math.Pow(1.0 / 3, 4);
            Rel(f / 0.01 * 1e-3, p.Scene.FindBody(a.Id)!.Velocity.X, 0.01, "magnet pulls A");
            Rel(-f / 0.01 * 1e-3, p.Scene.FindBody(b.Id)!.Velocity.X, 0.01, "and B equally");
        }

        [Fact]
        public void SensorPartFollowsAPublishedReading()
        {
            var lib = new PartLibrary();
            SdkParts.EnsureRegistered(lib);
            SdkParts.EnsureRegistered(lib);
            var def = lib.Get(SdkParts.SensorId);
            Assert.NotNull(lib.FindPreset(SdkParts.SensorId));
            var d = new SensorDevice("U1", 1, 0) { Scale = 0.5, Offset = 1, Max = 5 };
            d.Feed(3);
            Assert.Equal(2.5, d.Value);
            d.Feed(100);
            Assert.Equal(5, d.Value);
            Assert.Equal(2, def.Pins.Count);
        }

        // ================================================================ scripts (needs a .NET 9 SDK; skipped otherwise)

        [Fact]
        [Trait("Category", "Slow")]
        public void ScriptCompilerBuildsWithTheInstalledSdkOrExplainsWhy()
        {
            var info = ScriptCompiler.Detect();
            _out.WriteLine(info.Explanation);
            Assert.False(string.IsNullOrWhiteSpace(info.Explanation));
            if (!info.Available) return;
            string root = Path.Combine(Path.GetTempPath(), "rw-sdk-scripts-" + Guid.NewGuid().ToString("N"));
            string old = ScriptCompiler.WorkRoot;
            try
            {
                ScriptCompiler.WorkRoot = root;
                var bad = ScriptCompiler.Build(new[] { ("Bad.cs", "public class X { void M() { int a = ; } }") }, "BadScripts");
                Assert.False(bad.Success);
                Assert.Contains(bad.Diagnostics, d => d.IsError && d.File == "Bad.cs" && d.Line == 1);
                var ok = ScriptCompiler.Build(new[] { ("Spring.cs", ScriptCompiler.StarterScript) }, "GoodScripts");
                _out.WriteLine(ok.Summary);
                Assert.True(ok.Success, ok.Summary + "\n" + ok.Output);
                var again = ScriptCompiler.Build(new[] { ("Spring.cs", ScriptCompiler.StarterScript) }, "GoodScripts");
                Assert.True(again.FromCache);
                var host = new PluginHost(new BehaviorRegistry(), new PartLibrary());
                var loaded = host.LoadAssemblyFile(ok.AssemblyPath!, "scripttest");
                Assert.True(loaded.IsLoaded, loaded.Error);
                var t = Assert.Single(loaded.Types);
                Assert.Equal("Yay ile merkeze çek", t.Name);
                Assert.Equal(3, t.Parameters.Count);
                host.Unload("scripttest");
            }
            finally
            {
                ScriptCompiler.WorkRoot = old;
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        [Fact]
        public void SettingsRoundTripThroughTheWorldJson()
        {
            var w = new WorldDocument();
            w.Sdk.Wind.Speed = 4;
            w.Sdk.Wind.Turbulence = "dryden";
            w.Sdk.Fields.Add(new ForceFieldSpec { Id = "F1", Kind = "fan", Strength = 3, Targets = { "O1" } });
            w.Sdk.Behaviors.Add(new BehaviorAttachment { Id = "B1", ObjectId = "O1", Expression = "F.z = 1", Parameters = { ["k"] = "2" } });
            w.Sdk.Scripts.Add(new WorldScript { Id = "S1", Source = "// x" });
            w.Sdk.EnabledPlugins.Add("acme");
            var back = WorldDocument.FromJson(w.ToJson());
            Assert.Equal(4, back.Sdk.Wind.Speed);
            Assert.Equal("dryden", back.Sdk.Wind.Turbulence);
            Assert.Equal("fan", back.Sdk.Fields[0].Kind);
            Assert.Equal("O1", back.Sdk.Fields[0].Targets[0]);
            Assert.Equal("2", back.Sdk.Behaviors[0].Parameters["k"]);
            Assert.Equal("acme", back.Sdk.EnabledPlugins[0]);
            Assert.DoesNotContain("MeanVector", w.ToJson());
            var legacy = WorldDocument.FromJson("{\"Title\":\"eski\"}");
            Assert.NotNull(legacy.Sdk);
            Assert.True(legacy.Sdk.IsDefault);
        }
    }
}
