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
    /// <summary>[gas] Igniter, surface soak, flame spread and erosive burning of a solid grain.</summary>
    public class RocketIgnitionTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public RocketIgnitionTests(ITestOutputHelper output) => _out = output;

        private static WorldPhysics Build(WorldDocument w) => WorldPhysics.Build(w, new ApproximateGeometry(w, Lib).GeometryOf, null);

        /// <summary>erfc(x) for x ≥ 0 (Abramowitz &amp; Stegun 7.1.26, |ε| &lt; 1.5·10⁻⁷).</summary>
        private static double Erfc(double x)
        {
            double t = 1 / (1 + 0.3275911 * x);
            double y = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429))));
            return y * Math.Exp(-x * x);
        }

        [Fact]
        public void TheSurfaceSoakMatchesTheSemiInfiniteSolidUnderConvection()
        {
            // T_s = T₀ + (T_g − T₀)·[1 − exp(β²)·erfc(β)], β = h·√(α·t)/k (Carslaw & Jaeger).
            const double k = 0.4, rc = 1800 * 1300, hc = 2000, tg = 2000, t0 = 300, dt = 1e-4;
            var grid = Enumerable.Repeat(t0, 48).ToArray();
            double time = 0;
            foreach (double check in new[] { 0.002, 0.005, 0.02 })
            {
                while (time < check - 1e-12)
                {
                    SolidGrain.Conduct(grid, dt, hc, tg, 0, k, rc, t0);
                    time += dt;
                }

                double beta = hc * Math.Sqrt(k / rc * time) / k;
                double exact = t0 + (tg - t0) * (1 - Math.Exp(beta * beta) * Erfc(beta));
                _out.WriteLine($"{time * 1000:F0} ms: model {grid[0]:F1} K, exact {exact:F1} K");
                Assert.InRange((grid[0] - t0) / (exact - t0), 0.97, 1.03);
            }
        }

        [Fact]
        public void TheIgniterLightsTheHeadEndFirstAndTheFlameSpreadsInMilliseconds()
        {
            var w = WorldExamples.SugarMotorStand(Lib);
            var motor = w.Objects.Single();
            var p = Build(w);
            double firstLit = double.NaN, allLit = double.NaN;
            string? firstMessage = null;
            for (int i = 0; i < 600; i++)
            {
                p.Advance(1e-3, (AnalogSimulator?)null);
                foreach (var e in p.DrainEvents())
                {
                    if (e.Message.Contains("tutuştu") && double.IsNaN(firstLit))
                    {
                        firstLit = e.Time;
                        firstMessage = e.Message;
                    }

                    if (e.Message.Contains("tüm yüzeye yayıldı")) allLit = e.Time;
                }
            }

            _out.WriteLine($"{firstMessage}; all lit at {(allLit - 0.1) * 1000:F1} ms");
            Assert.InRange(firstLit - 0.1, 0.0005, 0.03);    // a 0.5 g igniter soaks the surface to ~590 K in milliseconds
            Assert.InRange(allLit - firstLit, 0.0, 0.1);
            Assert.Contains("baş tarafında", firstMessage);   // the igniter's gas is hottest at the head end
            Assert.True(p.GasOf(motor.Id)!.GrainLeft < 1);
        }

        [Fact]
        public void ABareEMatchHeadCannotLightASugarMotor()
        {
            var w = WorldExamples.SugarMotorStand(Lib);
            var motor = w.Objects.Single();
            motor.Parameters["igniter.charge"] = "0";   // only the ≈ 0.03 g match head
            var p = Build(w);
            double impulse = 0;
            for (int i = 0; i < 1000; i++)
            {
                p.Advance(1e-3, (AnalogSimulator?)null);
                impulse += p.GasOf(motor.Id)!.Thrust * 1e-3;
            }

            var g = p.GasOf(motor.Id)!;
            var events = p.DrainEvents();
            _out.WriteLine($"{g.Phase}; {string.Join(" | ", events.Select(e => e.Message))}");
            Assert.Contains("ateşlenemedi", g.Phase);
            Assert.Contains(events, e => e.Message.Contains("ateşlenemedi"));
            Assert.True(impulse < 0.5, $"impulse {impulse:F2} N·s");
            Assert.InRange(g.GrainLeft, 0.999, 1.0);
        }

        private static SolidGrain LitGrain(double core)
        {
            var kn = Propellant.For("KNSB");
            var grain = new SolidGrain(kn, 0.025, core, 0.06, 2, 293);
            for (int i = 0; i < 4000 && grain.LitFraction < 0.999; i++)
            {
                grain.Evaluate(1e6, 0.05);
                grain.Advance(1e-4, 0.05, 2000, 2000, kn.Products);
            }

            Assert.True(grain.LitFraction > 0.999);
            return grain;
        }

        [Fact]
        public void ASmallPortBurnsFasterTowardsTheNozzleErosively()
        {
            var kn = Propellant.For("KNSB");
            const double p = 4e6;
            var narrow = LitGrain(0.008);
            narrow.Evaluate(p, 0);
            double head = narrow.ZoneRate(0), aft = narrow.ZoneRate(narrow.Zones - 1), r0 = kn.Rate(p);
            var wide = LitGrain(0.018);
            wide.Evaluate(p, 0);
            double wideAft = wide.ZoneRate(wide.Zones - 1);
            _out.WriteLine($"α {narrow.ErosiveAlpha:E2}; r₀ {r0 * 1000:F2} mm/s; 8 mm core: head {head * 1000:F2}, aft {aft * 1000:F2} mm/s; 18 mm core aft {wideAft * 1000:F2} mm/s");
            Assert.InRange(head / r0, 1.0, 1.08);
            Assert.True(aft > 1.15 * r0, "the port flux at the aft end adds erosive burning");
            Assert.InRange(wideAft / r0, 1.0, 1.08);   // a wide port barely erodes
        }
    }
}
