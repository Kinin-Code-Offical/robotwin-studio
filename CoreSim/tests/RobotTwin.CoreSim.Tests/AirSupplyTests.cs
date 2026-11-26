using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[pneu] A double-solenoid valve's memory, and cylinders drawing from a compressor's tank.</summary>
    public class AirSupplyTests
    {
        private readonly ITestOutputHelper _out;

        public AirSupplyTests(ITestOutputHelper output) => _out = output;

        private static (WorldDocument World, WorldObject Cylinder, WorldObject? Compressor) Rig(string preset, string? compressor = null, string? fad = null, string? tank = null)
        {
            var w = new WorldDocument();
            var cy = w.AddComponent(PartLibrary.Default, preset, new WorldPose { Z = 0.05 });
            cy.IsStatic = true;
            var io = w.AddComponent(PartLibrary.Default, "modbus-io-8di8do", new WorldPose { X = -0.4, Y = 0.3 });
            io.Parameters["port"] = "0";
            void Cable(WorldObject a, string pa, WorldObject b, string pb) => w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = b.Id, Pin = pb });
            Cable(io, "DO1", cy, "SOL");
            Cable(io, "DO2", cy, "SOL2");
            Cable(io, "GND", cy, "GND");
            Cable(cy, "EXT", io, "DI1");
            Cable(cy, "RET", io, "DI2");
            WorldObject? kmp = null;
            if (compressor != null)
            {
                kmp = w.AddComponent(PartLibrary.Default, compressor, new WorldPose { X = 0.6 });
                kmp.Name = "KMP1";
                kmp.IsStatic = true;
                if (fad != null) kmp.Parameters["fad"] = fad;     // otherwise the preset's
                if (tank != null) kmp.Parameters["tank"] = tank;
                cy.Parameters["air"] = "KMP1";
                Cable(kmp, "RUN", io, "DI3");
                Cable(kmp, "GND", io, "GND");
            }

            return (w, cy, kmp);
        }

        private static double Until(WorldRunner r, Func<bool> done, double limit)
        {
            double start = r.Time;
            while (!done() && r.Time - start < limit) r.Step(0.001);
            return r.Time - start;
        }

        [Fact]
        public void ADoubleSolenoidValveStaysWhereItWasSent()
        {
            var (world, part, _) = Rig("cylinder-20x100-bistable");
            using var r = new WorldRunner(world, new RunnerOptions { Firmware = false });
            var cy = r.Physics!.CylinderOf(part.Id)!;
            var io = r.Physics.ModbusDevices.Single().Image;
            Assert.True(cy.Bistable);
            r.Step(0.05);

            void Pulse(int coil)
            {
                lock (io.Gate) io.Coils[coil] = true;
                r.Step(0.05);
                lock (io.Gate) io.Coils[coil] = false;   // released: a double-solenoid valve keeps its place
            }

            Pulse(0);
            Assert.InRange(Until(r, () => cy.Extended, 2), 0, 1.0);
            r.Step(0.5);
            Assert.True(cy.Extended && cy.Energised, "with both coils off it stays out");
            Pulse(1);
            Assert.InRange(Until(r, () => cy.Retracted, 2), 0, 1.0);
            r.Step(0.5);
            Assert.True(cy.Retracted && !cy.Energised, "and stays back");

            lock (io.Gate) io.Coils[0] = io.Coils[1] = true;   // both at once: the spool does not move
            r.Step(0.3);
            Assert.True(cy.Retracted);
        }

        [Fact]
        public void CylindersEmptyTheTankAndThePressureSwitchRefillsIt()
        {
            // a Ø20 × 100 cylinder stroking every half second (about 0.4 NL a cycle, 24 NL/min) from a 6 L, 30 L/min
            // compressor that can keep up; the tank starts just above the switch's lower pressure
            var (world, part, kmp) = Rig("cylinder-20x100", "compressor-6l");
            kmp!.Parameters["start"] = "6.2";
            using var r = new WorldRunner(world, new RunnerOptions { Firmware = false });
            var cy = r.Physics!.CylinderOf(part.Id)!;
            var c = r.Physics.CompressorOf(kmp.Id)!;
            var io = r.Physics.ModbusDevices.Single().Image;
            Assert.Same(c, cy.Source);
            double lowest = c.Pressure;
            bool runSeen = false;
            for (int k = 0; k < 24; k++)
            {
                lock (io.Gate) io.Coils[0] = k % 2 == 0;
                for (int i = 0; i < 50; i++)
                {
                    r.Step(0.01);
                    lowest = Math.Min(lowest, c.Pressure);
                    lock (io.Gate) runSeen |= io.DiscreteInputs[2];
                }
            }

            _out.WriteLine($"tank {(c.Pressure - 101325) / 1e5:0.00} bar (lowest {(lowest - 101325) / 1e5:0.00}), {c.Starts} starts, ran {c.RunSeconds:0.0} of {c.Seconds:0.0} s; " +
                           $"pumped {c.AirPumped:0.0} NL, drawn {c.AirDrawn:0.0} NL, the cylinder used {cy.AirUsed:0.0} NL");
            Assert.Equal(cy.AirUsed, c.AirDrawn, 1);           // what left the tank is what the cylinder took
            Assert.True(lowest < 6.05e5 + 101325, "the tank fell to the switch's lower pressure");
            Assert.True(c.Starts >= 1 && runSeen, "the switch started the motor, and its RUN output said so");
            Assert.InRange(c.Pressure, 6e5 + 101325 - 0.3e5, 8.05e5 + 101325);   // it keeps up: the tank stays within the switch's band
        }

        [Fact]
        public void AStarvedSupplySlowsTheStroke()
        {
            // a compressor far too small for the cylinder: the tank runs down and the strokes slow
            var (world, part, kmp) = Rig("cylinder-32x200", "compressor-6l", fad: "2", tank: "2");
            kmp!.Parameters["start"] = "6";
            using var r = new WorldRunner(world, new RunnerOptions { Firmware = false });
            var cy = r.Physics!.CylinderOf(part.Id)!;
            var c = r.Physics.CompressorOf(kmp.Id)!;
            var io = r.Physics.ModbusDevices.Single().Image;
            double Stroke()
            {
                lock (io.Gate) io.Coils[0] = true;
                double t = Until(r, () => cy.Extended, 5);
                r.Step(0.2);
                lock (io.Gate) io.Coils[0] = false;
                Until(r, () => cy.Retracted, 5);
                r.Step(0.2);
                return t;
            }

            double first = Stroke();
            for (int k = 0; k < 6; k++) Stroke();
            double later = Stroke();
            _out.WriteLine($"first stroke {first * 1000:0} ms at 6 bar, the eighth {later * 1000:0} ms with the tank at {(c.Pressure - 101325) / 1e5:0.00} bar");
            Assert.True(c.Pressure < 4e5 + 101325, "the tank ran down");
            Assert.True(later > first * 1.15, "a starved cylinder is slower");
        }
    }
}
