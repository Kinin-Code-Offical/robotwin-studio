using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[pneu] A cylinder and its valve, driven from a Modbus output as a PLC would: stroke times, end sensors, force.</summary>
    public class PneumaticTests
    {
        private readonly ITestOutputHelper _out;

        public PneumaticTests(ITestOutputHelper output) => _out = output;

        private static (WorldDocument World, WorldObject Cylinder) Rig(string speed = "100", double? wallAt = null)
        {
            var w = new WorldDocument();
            var cy = w.AddComponent(PartLibrary.Default, "cylinder-20x100", new WorldPose { Z = 0.05 });
            cy.IsStatic = true;
            cy.Parameters["speed"] = speed;
            var io = w.AddComponent(PartLibrary.Default, "modbus-io-8di8do", new WorldPose { X = -0.4, Y = 0.3 });
            io.Parameters["port"] = "0";
            w.Connect(new CableEnd { ObjectId = io.Id, Pin = "DO1" }, new CableEnd { ObjectId = cy.Id, Pin = "SOL" });
            w.Connect(new CableEnd { ObjectId = io.Id, Pin = "GND" }, new CableEnd { ObjectId = cy.Id, Pin = "GND" });
            w.Connect(new CableEnd { ObjectId = cy.Id, Pin = "EXT" }, new CableEnd { ObjectId = io.Id, Pin = "DI1" });
            w.Connect(new CableEnd { ObjectId = cy.Id, Pin = "RET" }, new CableEnd { ObjectId = io.Id, Pin = "DI2" });
            if (wallAt is { } at)
            {
                var g = CellGeometry.For(cy)!;
                var wall = w.AddBody("box", 0.1, 0.2, 0.2, new WorldPose { X = g.Max.X + 0.02 + at + 0.05, Z = 0 }, "steel");
                wall.IsStatic = true;
            }

            return (w, cy);
        }

        private static double Until(WorldRunner r, Func<bool> done, double limit)
        {
            double start = r.Time;
            while (!done() && r.Time - start < limit) r.Step(0.001);
            return r.Time - start;
        }

        [Fact]
        public void TheValveDrivesTheCylinderOutAndBackAndItsSensorsSayWhere()
        {
            var (world, part) = Rig();
            using var r = new WorldRunner(world, new RunnerOptions { Firmware = false });
            var cy = r.Physics!.CylinderOf(part.Id)!;
            var io = r.Physics.ModbusDevices.Single().Image;
            r.Step(0.05);
            Assert.True(cy.Retracted && !cy.Extended);

            double before = cy.AirUsed;
            lock (io.Gate) io.Coils[0] = true;   // the PLC energises the coil
            double peak = 0;
            double outTime = Until(r, () =>
            {
                peak = Math.Max(peak, cy.Speed);
                return cy.Extended;
            }, 2);
            r.Step(0.3);
            double gaugeA = (cy.PressureA - 101325) / 1e5, gaugeB = (cy.PressureB - 101325) / 1e5;
            bool[] seen;
            lock (io.Gate) seen = io.DiscreteInputs.ToArray();
            _out.WriteLine($"ileri {outTime * 1000:0} ms, tepe hız {peak:0.00} m/s; uçta A {gaugeA:0.00} bar, B {gaugeB:0.00} bar (gösterge); hava {cy.AirUsed:0.000} NL");
            Assert.InRange(outTime, 0.05, 0.6);   // a Ø20 × 100 cylinder at 6 bar, unloaded: a few tenths of a second
            Assert.True(seen[0] && !seen[1]);   // EXT on DI1, RET off on DI2
            Assert.InRange(gaugeA, 5.9, 6.01);   // the cap end fills to the supply
            Assert.InRange(gaugeB, -0.01, 0.05);   // the rod end has emptied
            // Filling the cap end from 1 to 7 bar (A × stroke plus its dead volume) takes about 0.2 normal litres.
            Assert.InRange(cy.AirUsed - before, 0.15, 0.3);

            lock (io.Gate) io.Coils[0] = false;
            double backTime = Until(r, () => cy.Retracted, 2);
            _out.WriteLine($"geri {backTime * 1000:0} ms");
            Assert.InRange(backTime, 0.05, 0.6);
        }

        [Fact]
        public void AgainstAWallItPushesWithPressureTimesArea()
        {
            var (world, part) = Rig(wallAt: 0.05);
            using var r = new WorldRunner(world, new RunnerOptions { Firmware = false });
            var cy = r.Physics!.CylinderOf(part.Id)!;
            lock (r.Physics.ModbusDevices.Single().Image.Gate) r.Physics.ModbusDevices.Single().Image.Coils[0] = true;
            r.Step(1.0);
            double ideal = 6e5 * Math.PI / 4 * 0.02 * 0.02;   // 188.5 N
            _out.WriteLine($"duvarda: konum {cy.Position * 1000:0.0} mm, kuvvet {cy.Force:0.0} N (ideal {ideal:0.0} N)");
            Assert.InRange(cy.Position, 0.045, 0.052);
            Assert.False(cy.Extended);
            Assert.InRange(cy.Force, ideal - 3, ideal + 1);
        }

        [Fact]
        public void ThrottlingTheExhaustSlowsTheStroke()
        {
            double Stroke(string speed)
            {
                var (world, part) = Rig(speed);
                using var r = new WorldRunner(world, new RunnerOptions { Firmware = false });
                var cy = r.Physics!.CylinderOf(part.Id)!;
                r.Step(0.5);   // the rod end fills to the supply at rest: the throttle meters it out
                lock (r.Physics.ModbusDevices.Single().Image.Gate) r.Physics.ModbusDevices.Single().Image.Coils[0] = true;
                return Until(r, () => cy.Extended, 5);
            }

            double fast = Stroke("100"), slow = Stroke("15");
            _out.WriteLine($"egzoz %100: {fast * 1000:0} ms, %15: {slow * 1000:0} ms");
            Assert.True(slow > fast * 2.5, $"{slow} vs {fast}");
        }
    }
}
