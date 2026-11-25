using System;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [phys] Electrical, mechanical and thermal energy add up: what a supply gives a gear motor goes into the wiring's and
    /// the winding's heat, friction, the gear train, the work done on the load and the energy stored in the spinning
    /// flywheel and the winding's field, nothing made or lost on the way (the circuit and the motor's mechanics are
    /// solved together); and the winding's heat goes into its temperature rise and out to the air.
    /// </summary>
    public class EnergyBalanceTests
    {
        private readonly ITestOutputHelper _out;

        public EnergyBalanceTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void AGearMotorsEnergyAddsUp()
        {
            var c = new AnalogCircuit();
            int p = c.Node("p"), q = c.Node("q");
            c.Add(new VoltageSource("V1", p, 0, 6.0));
            var wiring = c.Add(new Resistor("R1", p, q, 0.5));
            var m = c.Add(new DcMotor("M1", q, 0));
            m.SetFromDatasheet(ratedVoltage: 6.0, noLoadRpm: 280, noLoadCurrent: 0.1, stallCurrent: 1.5, gearRatio: 30);
            m.GearEfficiency = 0.7;
            m.LoadInertia = 2e-3;   // a flywheel on the output shaft
            var sim = new AnalogSimulator(c);
            sim.Initialize();

            const double dt = 1e-4;
            double supplied = 0, wire = 0, copper = 0, friction = 0, gear = 0, work = 0, heatOut = 0;
            double Stored() => 0.5 * m.Inductance * m.Current * m.Current + 0.5 * m.Inertia * m.AngularVelocity * m.AngularVelocity +
                               0.5 * m.LoadInertia * Math.Pow(m.AngularVelocity / m.GearRatio, 2);
            double stored0 = Stored(), heat0 = m.Thermal.ThermalCapacitance * m.Thermal.TemperatureC;
            for (int i = 0; i < 30000; i++)
            {
                if (i == 15000) m.LoadTorque = 0.3 * m.MotorConstant * 1.5 * m.GearRatio;   // a load at 1.5 s: about 30 % of stall at the output
                double t0 = m.Thermal.TemperatureC, load = m.LoadTorque;
                sim.Step(dt);
                double w = m.AngularVelocity, current = m.Current;
                supplied += 6.0 * current * dt;
                wire += current * current * wiring.Resistance * dt;
                copper += m.CopperLoss * dt;
                friction += m.FrictionLoss * dt;
                double toLoad = load * w / m.GearRatio;
                work += toLoad * dt;
                gear += m.GearLoss * dt;   // what the gear train turns into heat
                Assert.Equal(Math.Abs(toLoad) * (1 / m.GearEfficiency - 1), m.GearLoss, 9);
                // the winding's heat that left for the air over the step (the thermal model's exact first-order solution)
                heatOut += m.CopperLoss * dt - m.Thermal.ThermalCapacitance * (m.Thermal.TemperatureC - t0);
            }

            double stored = Stored() - stored0;
            double spent = wire + copper + friction + gear + work + stored;
            double error = (supplied - spent) / supplied;
            _out.WriteLine($"supplied {supplied:F3} J = wiring {wire:F3} + winding {copper:F3} + friction {friction:F3} + gears {gear:F3} + load {work:F3} + stored {stored:F4} J; unaccounted {error:P3}");
            _out.WriteLine($"winding {m.Thermal.TemperatureC:F2} °C; copper heat {copper:F3} J = stored {m.Thermal.ThermalCapacitance * m.Thermal.TemperatureC - heat0:F3} J + to the air {heatOut:F3} J");
            Assert.InRange(error, -0.005, 0.005);   // within 0.5 % of what the supply gave
            Assert.True(work > 0.2 * supplied, "the load got a fair share");
            Assert.Equal(copper, m.Thermal.ThermalCapacitance * m.Thermal.TemperatureC - heat0 + heatOut, 6);
        }
    }
}
