using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[gas] Pressure vessels, nozzles, water and solid propellant through one gas model.</summary>
    public class GasPhysicsTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public GasPhysicsTests(ITestOutputHelper output) => _out = output;

        private static WorldPhysics Build(WorldDocument w) => WorldPhysics.Build(w, new ApproximateGeometry(w, Lib).GeometryOf, null);

        private static void Run(WorldPhysics p, double seconds)
        {
            for (int i = 0; i < (int)Math.Round(seconds / 1e-3); i++) p.Advance(1e-3, (AnalogSimulator?)null);
        }

        [Fact]
        public void AnAirTankBlowsDownAsAdiabaticChokedFlowPredicts()
        {
            var w = new WorldDocument { Title = "tank" };
            var tank = w.AddBody("cylinder", 0.1, 0.1, 0.2, new WorldPose(), "ÇELİK");
            tank.IsStatic = true;
            tank.Parameters["vessel"] = "true";
            tank.Parameters["volume"] = "1.0";
            tank.Parameters["pressure"] = "6";
            tank.Parameters["nozzle"] = "2";
            var p = Build(w);
            double pa = 101325, p0 = p.GasOf(tank.Id)!.GaugePressure + pa;
            double t0 = w.Environment.AmbientC + 273.15, g = 1.4, r = 287.05, cd = 0.95;
            double area = Math.PI / 4 * 0.002 * 0.002, v = 1e-3;
            double gamma = Math.Sqrt(g) * Math.Pow(2 / (g + 1), (g + 1) / (2 * (g - 1)));
            // Choked blowdown has two textbook limits: adiabatic (no heat from the wall),
            // p/p0 = [1 + (γ−1)/2 · Cd·A·Γ·√(R·T0)/V · t]^(−2γ/(γ−1)), and isothermal (the wall keeps the gas at T0),
            // p/p0 = exp(−Cd·A·Γ·√(R·T0)/V · t). A real tank lies between: adiabatic at first, warmed by its wall later.
            double k = cd * area * gamma * Math.Sqrt(r * t0) / v;
            foreach (double t in new[] { 0.5, 1.0, 2.0 })
            {
                Run(p, t - p.Scene.Time);
                double model = p.GasOf(tank.Id)!.GaugePressure + pa;
                double adiabatic = p0 * Math.Pow(1 + (g - 1) / 2 * k * t, -2 * g / (g - 1));
                double isothermal = p0 * Math.Exp(-k * t);
                _out.WriteLine($"t={t:F1} s: {model / 1e5:F3} bar abs, adiabatic {adiabatic / 1e5:F3}, isothermal {isothermal / 1e5:F3}");
                Assert.InRange(model, adiabatic * 0.99, isothermal * 1.01);
                if (t <= 0.5) Assert.InRange(model / adiabatic, 0.98, 1.03);   // a fast blowdown is nearly adiabatic
            }
        }

        [Fact]
        public void AWaterRocketEmptiesItsWaterThenFliesToARealisticApogee()
        {
            var w = WorldExamples.WaterRocket(Lib);
            var bottle = w.Objects.First(o => o.Name.Contains("PET"));
            var p = Build(w);
            double maxSpeed = 0, apogee = 0, waterOutAt = double.NaN, maxThrust = 0;
            double z0 = p.PoseOf(bottle.Id)!.Value.Position.Z;
            for (int i = 0; i < 6000; i++)
            {
                p.Advance(1e-3, (AnalogSimulator?)null);
                var g = p.GasOf(bottle.Id)!;
                var body = p.Scene.FindBody(bottle.Id)!;
                maxSpeed = Math.Max(maxSpeed, body.Velocity.Z);
                maxThrust = Math.Max(maxThrust, g.Thrust);
                apogee = Math.Max(apogee, p.PoseOf(bottle.Id)!.Value.Position.Z - z0);
                if (double.IsNaN(waterOutAt) && g.WaterLitres <= 1e-6 && p.Scene.Time > 0.5) waterOutAt = p.Scene.Time - 0.5;
                if (p.Scene.Time > 1.5 && body.Velocity.Z < 0) break;
            }

            var tilt = p.PoseOf(bottle.Id)!.Value.Orientation.Rotate(Vec3.UnitZ).Z;
            _out.WriteLine($"water out after {waterOutAt * 1000:F0} ms, peak thrust {maxThrust:F0} N, max speed {maxSpeed:F1} m/s, apogee {apogee:F1} m, axis·up {tilt:F3}");
            // 2 L / 0.6 L / 4 bar / 21.5 mm: water gone in about a tenth of a second, ~25 m/s, a few tens of metres.
            Assert.InRange(waterOutAt, 0.04, 0.25);
            Assert.InRange(maxThrust, 80, 400);
            Assert.InRange(maxSpeed, 25, 50);   // ~146 g dry; the air pulse after the water adds ~10 m/s
            Assert.InRange(apogee, 12, 65);
            Assert.True(tilt > 0.9, "the fins keep it pointing up");
        }

        [Fact]
        public void AKnsbMotorReachesItsBurnRateEquilibriumPressureAndRealisticImpulse()
        {
            var w = WorldExamples.SugarMotorStand(Lib);
            var motor = w.Objects.Single();
            var p = Build(w);
            double impulse = 0, peak = 0, burnStart = double.NaN, burnEnd = double.NaN, plateau = 0;
            for (int i = 0; i < 4000; i++)
            {
                p.Advance(1e-3, (AnalogSimulator?)null);
                var g = p.GasOf(motor.Id)!;
                impulse += g.Thrust * 1e-3;
                peak = Math.Max(peak, g.GaugePressure);
                if (double.IsNaN(burnStart) && g.Burning) burnStart = p.Scene.Time;
                if (!double.IsNaN(burnStart) && double.IsNaN(burnEnd) && !g.Burning) burnEnd = p.Scene.Time;
                if (Math.Abs(p.Scene.Time - 0.25) < 5e-4) plateau = g.GaugePressure + 101325;
            }

            // Steady state of a cored grain: ρ·A_b·a·pⁿ = p·A_t / c*  →  p^(1−n) = Kn·ρ·a·c*.
            var kn = Propellant.For("KNSB");
            double gm = kn.Products.Gamma, cStar = Math.Sqrt(kn.Products.R * kn.FlameTemperature) / (Math.Sqrt(gm) * Math.Pow(2 / (gm + 1), (gm + 1) / (2 * (gm - 1))));
            double burnArea = 2 * (Math.PI * 0.008 * 0.06 + 2 * Math.PI / 4 * (0.025 * 0.025 - 0.008 * 0.008)), throat = Math.PI / 4 * 0.006 * 0.006;
            double knRatio = burnArea / throat, equilibrium = 2e6;
            for (int i = 0; i < 100; i++) equilibrium = knRatio * kn.Density * kn.Rate(equilibrium) * cStar / 0.95;   // the nozzle's discharge coefficient
            double propellant = 2 * Math.PI / 4 * (0.025 * 0.025 - 0.008 * 0.008) * 0.06 * kn.Density;
            double isp = impulse / (propellant * 9.80665);
            _out.WriteLine($"Kn {knRatio:F0}: chamber {plateau / 1e6:F2} MPa at 0.25 s (steady-state theory {equilibrium / 1e6:F2} MPa), peak {peak / 1e6:F2} MPa gauge, burn {burnEnd - burnStart:F2} s, impulse {impulse:F0} N·s from {propellant * 1000:F0} g: Isp {isp:F0} s");
            Assert.InRange(plateau / equilibrium, 0.8, 1.25);
            Assert.InRange(burnEnd - burnStart, 0.6, 1.8);
            Assert.InRange(isp, 100, 165);   // KNSB delivers ~110–140 s in amateur motors
            Assert.False(p.GasOf(motor.Id)!.Burst, "6061 casing holds ~30 MPa");
        }

        [Fact]
        public void AChokedDownNozzleBurstsTheCasingAndAnOverPumpedBottleBursts()
        {
            var w = WorldExamples.SugarMotorStand(Lib, throatMm: 1.5);
            var motor = w.Objects.Single();
            var p = Build(w);
            Run(p, 1.0);
            var g = p.GasOf(motor.Id)!;
            _out.WriteLine($"1.5 mm throat: {g.Phase}, {g.GaugePressure / 1e6:F1} MPa");
            Assert.True(g.Burst);
            Assert.Contains(p.DrainEvents(), e => e.Kind == PhysicsEventKind.BodyBroken && e.Message.Contains("patladı"));

            var rocket = WorldExamples.WaterRocket(Lib, 0.6, 12);   // a 2 L bottle holds ~9 bar
            var bottle = rocket.Objects.First(o => o.Name.Contains("PET"));
            var q = Build(rocket);
            Run(q, 0.05);
            Assert.True(q.GasOf(bottle.Id)!.Burst);
        }
    }
}
