using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [stepper] An A4988 driving a 17HS4401 from STEP pulses: a step is 1.8°, microsteps divide it, the rotor holds up to
    /// K·I and slips beyond, and started too fast it stalls instead of following.
    /// </summary>
    public class StepperTests
    {
        private readonly ITestOutputHelper _out;

        public StepperTests(ITestOutputHelper output) => _out = output;

        private sealed class Rig
        {
            public AnalogSimulator Sim = null!;
            public StepperMotor Motor = null!;
            public StepperDriver Driver = null!;
        }

        /// <summary>12 V on VMOT, 5 V logic, 2 µs STEP pulses at a rate (none at 0), MS pins high for 1/16.</summary>
        private static Rig Build(double stepRate, bool sixteenth = false, double currentLimit = 1.0, bool forward = true, StepperChip chip = StepperChip.A4988,
            double disableAt = double.NaN)
        {
            var c = new AnalogCircuit();
            int vmot = c.Node("vmot"), vdd = c.Node("vdd"), step = c.Node("step"), dir = c.Node("dir"), en = c.Node("en");
            int ms1 = c.Node("ms1"), ms2 = c.Node("ms2"), ms3 = c.Node("ms3"), slp = c.Node("slp"), rst = c.Node("rst");
            int a1 = c.Node("a1"), a2 = c.Node("a2"), b1 = c.Node("b1"), b2 = c.Node("b2");
            c.Add(new VoltageSource("VM", vmot, 0, 12.0));
            c.Add(new VoltageSource("VD", vdd, 0, 5.0));
            c.Add(new VoltageSource("VS", step, 0, stepRate > 0 ? new PulseWaveform(0, 5, 1 / stepRate, 2e-6, 0.002) : (Waveform)0.0));
            c.Add(new VoltageSource("VDIR", dir, 0, forward ? 5.0 : 0.0));
            if (!double.IsNaN(disableAt))
                c.Add(new VoltageSource("VEN", en, 0, new PiecewiseLinearWaveform(new[] { (0.0, 0.0), (disableAt, 0.0), (disableAt + 1e-6, 5.0) })));
            if (sixteenth)
            {
                c.Add(new VoltageSource("VMS1", ms1, 0, 5.0));
                c.Add(new VoltageSource("VMS2", ms2, 0, 5.0));
                c.Add(new VoltageSource("VMS3", ms3, 0, 5.0));
            }

            var driver = c.Add(new StepperDriver("U1", chip, vmot, 0, a1, a2, b1, b2, chip == StepperChip.Drv8825 ? -1 : vdd, step, dir, en, ms1, ms2,
                chip == StepperChip.Tmc2209 ? -1 : ms3, chip == StepperChip.Tmc2209 ? -1 : slp, chip == StepperChip.Tmc2209 ? -1 : rst) { CurrentLimit = currentLimit });
            var motor = c.Add(new StepperMotor("M1", a1, a2, b1, b2) { Resistance = 1.5, Inductance = 2.8e-3, TorqueConstant = 0.40 / (Math.Sqrt(2) * 1.7), DetentTorque = 0.022, Inertia = 54e-7 });
            var sim = new AnalogSimulator(c, new AnalogOptions { MaxStep = 5e-5 });
            sim.Initialize();
            return new Rig { Sim = sim, Motor = motor, Driver = driver };
        }

        private static void Run(Rig r, double seconds)
        {
            double until = r.Sim.Time + seconds;
            while (r.Sim.Time < until - 1e-9) r.Sim.Step(Math.Min(0.005, until - r.Sim.Time));
        }

        [Fact]
        public void TwoHundredFullStepsTurnItOnce()
        {
            var r = Build(200);
            Run(r, 1.0);
            _out.WriteLine($"1 s, 200 adım/s: sürücü {r.Driver.Steps} adım saydı, mil {r.Motor.FullSteps:0.00} tam adım ({r.Motor.Angle * 180 / Math.PI:0.0}°), faz akımları {r.Motor.CurrentA:0.00} / {r.Motor.CurrentB:0.00} A");
            Assert.InRange(r.Driver.Steps, 199, 200);
            Assert.InRange(r.Motor.FullSteps, 198.5, 201.5);   // a turn, less the lag of the last step
            Assert.InRange(Math.Sqrt(r.Motor.CurrentA * r.Motor.CurrentA + r.Motor.CurrentB * r.Motor.CurrentB), 0.9, 1.1);   // the current limit, shared by the phases
        }

        [Fact]
        public void SixteenthStepsDivideAStepAndReverseWithDir()
        {
            var r = Build(3200, sixteenth: true);
            Run(r, 0.5);
            _out.WriteLine($"1/16 adım, 3200 darbe/s, 0,5 s: {r.Motor.FullSteps:0.00} tam adım");
            Assert.Equal(16, r.Driver.Microsteps);
            Assert.InRange(r.Motor.FullSteps, 98, 101.5);   // half a turn
            var back = Build(3200, sixteenth: true, forward: false);
            Run(back, 0.5);
            Assert.InRange(back.Motor.FullSteps, -101, -98);
        }

        [Fact]
        public void ADrv8825StepsInThirtySecondsWithItsOwnLogic()
        {
            // M0–M2 all high: 1/32; 6400 pulses a second is a turn a second. No VDD: the chip makes its own 3.3 V.
            var r = Build(6400, sixteenth: true, chip: StepperChip.Drv8825);
            Run(r, 0.5);
            _out.WriteLine($"DRV8825 1/{r.Driver.Microsteps}: 0,5 s'de {r.Motor.FullSteps:0.00} tam adım");
            Assert.Equal(32, r.Driver.Microsteps);
            Assert.InRange(r.Motor.FullSteps, 98, 101.5);
        }

        [Fact]
        public void ATmc2209InterpolatesBetweenStepsToA256th()
        {
            // MS1, MS2 high: 1/16, each step spread over the interval between pulses (MicroPlyer).
            var r = Build(1600, sixteenth: true, chip: StepperChip.Tmc2209);
            Run(r, 0.5);
            Assert.Equal(16, r.Driver.Microsteps);
            double now = r.Sim.Time, period = 1.0 / 1600;
            // Between two pulses the translator is part of the way to the next position, not sitting on it.
            double before = r.Driver.PositionAt(now), later = r.Driver.PositionAt(now + period / 2);
            _out.WriteLine($"TMC2209 1/16: 0,5 s'de {r.Motor.FullSteps:0.00} tam adım; konum {before:0.0} → yarım aralık sonra {later:0.0} (1/256 adım)");
            Assert.InRange(r.Motor.FullSteps, 48, 51.5);   // a quarter turn
            Assert.True(r.Driver.PositionAt(now) != r.Driver.Position || later > before, "it moves between pulses");
        }

        [Fact]
        public void SwitchedOffItsWindingCurrentDecaysIntoTheSupply()
        {
            // Holding at the current limit, then EN high: the bridge lets go, the windings push their current on through the
            // body diodes against VMOT. It falls to zero in L·i/(VMOT + 1.4 V) ≈ 0.2 ms, not in one step, and the voltage
            // across a winding stays at the supply's, not the kilovolts an instant cut would make.
            var r = Build(0, disableAt: 0.05);
            Run(r, 0.0499);
            double held = Math.Max(Math.Abs(r.Motor.CurrentA), Math.Abs(r.Motor.CurrentB));
            double peak = 0, zeroAt = double.NaN, supplyIn = 0;
            var vm = r.Sim.Circuit.Devices.OfType<VoltageSource>().Single(v => v.Id == "VM");
            r.Sim.SubstepAccepted = t =>
            {
                peak = Math.Max(peak, Math.Max(Math.Abs(r.Sim.NodeVoltage(r.Motor.A1) - r.Sim.NodeVoltage(r.Motor.A2)), Math.Abs(r.Sim.NodeVoltage(r.Motor.B1) - r.Sim.NodeVoltage(r.Motor.B2))));
                if (double.IsNaN(zeroAt) && t > 0.05 && Math.Abs(r.Motor.CurrentA) < 0.01 && Math.Abs(r.Motor.CurrentB) < 0.01) zeroAt = t;
                supplyIn = Math.Min(supplyIn, vm.Current);
            };
            Run(r, 0.003);
            _out.WriteLine($"tutma {held:0.00} A; EN HIGH sonrası akım {(zeroAt - 0.05) * 1000:0.000} ms'de sıfır, sargıda en çok {peak:0.0} V, kaynağa geri en çok {-supplyIn * 1000:0} mA");
            Assert.InRange(held, 0.65, 1.05);
            Assert.False(r.Driver.Enabled);
            Assert.InRange(zeroAt - 0.05, 0.1e-3, 0.4e-3);   // L·i/(VMOT + 1.4)
            Assert.InRange(peak, 12, 15);   // the supply and two diode drops
            Assert.True(supplyIn < -0.1, "the winding's energy goes back into VMOT");
        }

        [Fact]
        public void ItHoldsUpToItsTorqueAndSlipsBeyond()
        {
            // At 1 A the peak torque is K·I = 0.166 N·m.
            var held = Build(0);
            Run(held, 0.1);
            double rest = held.Motor.Angle;
            held.Motor.LoadTorque = 0.10;
            Run(held, 0.5);
            double deflection = (held.Motor.Angle - rest) * 180 / Math.PI;
            var slips = Build(0);
            Run(slips, 0.1);
            slips.Motor.LoadTorque = 0.25;
            Run(slips, 0.5);
            _out.WriteLine($"0,10 N·m: {deflection:0.00}° sapma; 0,25 N·m: {slips.Motor.Angle * 180 / Math.PI:0} ° döndü");
            Assert.InRange(Math.Abs(deflection), 0.3, 1.0);   // asin(0.10/0.166)/50 ≈ 0.74°
            Assert.True(Math.Abs(slips.Motor.Angle) > 1.0, "beyond its holding torque the load turns it");
        }

        [Fact]
        public void StartedTooFastItStallsInsteadOfFollowing()
        {
            // 2400 full steps a second (12 turns a second) from standstill, no ramp: the rotor cannot keep up.
            var r = Build(2400);
            Run(r, 0.25);
            _out.WriteLine($"2400 adım/s rampasız: sürücü {r.Driver.Steps} adım, mil {r.Motor.FullSteps:0.0} tam adım");
            Assert.True(r.Driver.Steps > 590);
            Assert.True(Math.Abs(r.Motor.FullSteps) < 0.5 * r.Driver.Steps, "it loses steps");
        }
    }
}
