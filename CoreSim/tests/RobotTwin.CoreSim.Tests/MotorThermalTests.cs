using System;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[motor-thermal] A DC motor's two-node thermal model (winding, housing) and its magnets' temperature coefficient.</summary>
    public class MotorThermalTests
    {
        private readonly ITestOutputHelper _out;

        public MotorThermalTests(ITestOutputHelper output) => _out = output;

        private static (AnalogSimulator Sim, DcMotor Motor, VoltageSource Supply) Rig(double volts, bool jammed)
        {
            var c = new AnalogCircuit();
            int n = c.Node("m");
            var v = c.Add(new VoltageSource("V1", n, AnalogCircuit.Ground, volts));
            var m = c.Add(new DcMotor("M1", n, AnalogCircuit.Ground) { Resistance = 2.0, MotorConstant = 0.01, Jammed = jammed });
            m.Thermal.ThermalResistance = 15;
            m.Thermal.ThermalCapacitance = 20;
            var sim = new AnalogSimulator(c, new AnalogOptions { AmbientTempC = 25, EnableDamage = false });
            sim.Initialize(InitialState.OperatingPoint);
            return (sim, m, v);
        }

        [Fact]
        public void TheWindingHeatsFirstAndSettlesWhereTheNetworkPutsIt()
        {
            var (sim, m, _) = Rig(2.0, true);   // stalled: ~1 A, ~2 W of copper loss
            for (int i = 0; i < 100; i++) sim.Step(0.1);   // 10 s
            double early = m.Thermal.TemperatureC - 25, earlyCase = m.CaseTemperatureC - 25;
            _out.WriteLine($"after 10 s: winding +{early:F2} K, housing +{earlyCase:F2} K");
            Assert.True(early > 3 * earlyCase, "the light winding runs ahead of the heavy housing");

            for (int i = 0; i < 3000; i++) sim.Step(1.0);   // 50 min: steady
            double p = m.CopperLoss;
            double rwc = 0.25 * 15, rca = 15 - rwc;
            _out.WriteLine($"steady: winding {m.Thermal.TemperatureC:F2} °C, housing {m.CaseTemperatureC:F2} °C, {p:F3} W");
            Assert.Equal(25 + p * rca, m.CaseTemperatureC, 1);
            Assert.Equal(m.CaseTemperatureC + p * rwc, m.Thermal.TemperatureC, 1);
            Assert.Equal(25 + p * 15, m.Thermal.TemperatureC, 1);   // as hot as the one-node model in steady state
        }

        [Fact]
        public void AHotMotorSpinsFasterUnloadedAsItsMagnetsWeaken()
        {
            var (cold, coldMotor, _) = Rig(6.0, false);
            for (int i = 0; i < 2000; i++) cold.Step(1e-3);
            double coldSpeed = coldMotor.AngularVelocity;

            var (hot, hotMotor, supply) = Rig(4.0, true);   // stalled for 20 min first: a warm housing
            for (int i = 0; i < 1200; i++) hot.Step(1.0);
            double tc = hotMotor.CaseTemperatureC;
            hotMotor.Jammed = false;
            supply.Waveform = 6.0;
            for (int i = 0; i < 2000; i++) hot.Step(1e-3);   // two seconds: the housing hardly cools
            double hotSpeed = hotMotor.AngularVelocity;
            double k = 1 + hotMotor.MagnetTempCo * (hotMotor.CaseTemperatureC - 25);
            _out.WriteLine($"housing {tc:F1} °C: no-load {coldSpeed:F1} → {hotSpeed:F1} rad/s, Kt ×{k:F3}");
            Assert.True(tc > 60, $"housing {tc:F1} °C");
            Assert.InRange(hotSpeed / coldSpeed, 0.98 / k, 1.02 / k);   // speed ∝ V / Kt with the losses small
        }
    }
}
