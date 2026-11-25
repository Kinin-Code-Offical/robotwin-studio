using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware;
using RobotTwin.CoreSim.Instruments;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[uart] The Uno's Serial on its TX pin as a waveform: what the sketch prints can be read off D1's voltage.</summary>
    public class AvrUartPinTests
    {
        private readonly ITestOutputHelper _out;

        public AvrUartPinTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void WhatTheSketchPrintsIsOnTheTxPin()
        {
            var lib = PartLibrary.Default;
            var sch = new Schematic();
            var uno = sch.AddPart(lib, "arduino-uno", new GridPoint(0, 0));
            var load = sch.AddPart(lib, "res-10000", new GridPoint(20, 0));
            void Net(SchematicPart part, string pin, string net)
            {
                var pd = lib.Get(part.PartId).FindPin(pin)!;
                sch.Labels.Add(new NetLabel { Name = net, Position = part.Transform(pd.X, pd.Y) });
            }

            Net(uno, "D1", "TX");
            Net(load, "A", "TX");   // a receiver's input: 10 kΩ to ground
            Net(load, "B", "GND");
            sch.Labels.Add(new NetLabel { Name = "GND", Position = uno.Transform(-7, -4) });
            var compiled = CircuitCompiler.Compile(sch, lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            var mcu = new Atmega328P();
            mcu.LoadHex(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "servo-arm-debug.hex")));
            var cosim = new FirmwareCoSimulation(mcu, compiled, "U1", () => compiled.PinVoltage(sim, "U1", "5V"));
            var serial = new StringBuilder();
            cosim.SerialOutput += s => serial.Append(s);
            int tx = compiled.NodeByNet["TX"];
            var samples = new List<double>();
            const double slice = 100e-6, sample = 10e-6;
            for (int i = 0; i < (int)(0.08 / slice); i++)
            {
                cosim.Advance(slice, sim.Time);
                for (int k = 0; k < 10; k++)
                {
                    sim.Step(sample);
                    samples.Add(sim.NodeVoltage(tx));
                }
            }

            var decoded = ProtocolDecoder.Uart(samples.ToArray(), 0, sample, 9600, 2.5);
            string line = new string(decoded.Where(f => !f.Error).Select(f => (char)f.Value).ToArray());
            _out.WriteLine($"seri: \"{serial.ToString().Replace("\r", "\\r").Replace("\n", "\\n")}\"; D1'den çözülen: \"{line.Replace("\r", "\\r").Replace("\n", "\\n")}\"; {decoded.Count(f => f.Error)} hata");
            Assert.StartsWith("Servo tarama basladi", serial.ToString());
            Assert.StartsWith("Servo tarama basladi", line);   // the same text, read electrically off the pin
            Assert.DoesNotContain(decoded, f => f.Error);
            Assert.InRange(samples.Max(), 4.5, 5.1);   // a 5 V line
            Assert.InRange(samples.Min(), -0.1, 0.4);
        }
    }
}
