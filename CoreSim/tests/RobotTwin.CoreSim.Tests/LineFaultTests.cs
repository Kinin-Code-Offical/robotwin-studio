using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [faults] Faults put on a wire by the fault instruments, on a real link: one Uno's TX to another's RX (the servo arm
    /// prints "Aci: 180" ~0.56 s in; the listener, Fixtures/uart-flags.ino, prints what it receives in hex with its error
    /// flags). At the same rate, glitches shorter than the receiver's three-sample vote leave the message whole, longer
    /// ones break its bytes (the vote itself is tested bit by bit in <see cref="AvrUartRxTests"/>).
    /// </summary>
    public class LineFaultTests
    {
        private const double Slice = 50e-6;
        private const string Message = "41 63 69 3A 20 31 38 30 0D 0A ";   // "Aci: 180\r\n"
        private readonly ITestOutputHelper _out;

        public LineFaultTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void RandomStrikesArriveAtTheirRateAndRepeat()
        {
            var a = new FaultSchedule(true, 1000, 5e-6, 0.1, 1.0, 7);
            var b = new FaultSchedule(true, 1000, 5e-6, 0.1, 1.0, 7);
            var w = a.Windows(0, 2);
            _out.WriteLine($"{w.Count} strikes in 1 s at 1 kHz");
            Assert.InRange(w.Count, 900, 1100);
            Assert.All(w, x => Assert.InRange(x.Off - x.On, 4.99e-6, 5.01e-6));
            Assert.All(w.Zip(w.Skip(1), (p, q) => q.On - p.Off), gap => Assert.True(gap >= 0));
            Assert.True(w.First().On >= 0.1 && w.Last().Off <= 1.1);
            Assert.Equal(w, b.Windows(0, 2));
            Assert.True(a.Active(w[3].On + 1e-6) && !a.Active(w[3].Off + 1e-9));
            Assert.Equal(w[5].On, a.Next(w[4].Off + 1e-9), 12);

            var stuck = new FaultSchedule(false, 0, 0, 0.2, 0, 1);
            Assert.False(stuck.Active(0.19));
            Assert.True(stuck.Active(0.2) && stuck.Active(100));
            Assert.Equal(0.2, stuck.Next(0), 12);
            Assert.True(double.IsPositiveInfinity(stuck.Next(0.3)));
        }

        /// <summary>The two-board link, with <paramref name="fault"/> put on it; what the listener printed.</summary>
        private string Link(Action<Schematic, PartLibrary, SchematicPart, SchematicPart>? fault, double seconds, out CompiledCircuit compiled)
        {
            var lib = PartLibrary.Default;
            var sch = new Schematic();
            var u1 = sch.AddPart(lib, "arduino-uno", new GridPoint(0, 0));
            var u2 = sch.AddPart(lib, "arduino-uno", new GridPoint(40, 0));
            sch.Labels.Add(new NetLabel { Name = "GND", Position = u1.Transform(-7, -4) });
            sch.Labels.Add(new NetLabel { Name = "GND", Position = u2.Transform(-7, -4) });
            if (fault != null) fault(sch, lib, u1, u2);
            else
            {
                Label(sch, lib, u1, "D1", "LINK");
                Label(sch, lib, u2, "D0", "LINK");
            }

            compiled = CircuitCompiler.Compile(sch, lib);
            var c = compiled;
            var sim = new AnalogSimulator(c.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            var talker = new Atmega328P();
            talker.LoadHex(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "servo-arm-debug.hex")));
            var listener = new Atmega328P();
            listener.LoadHex(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "uart-flags.hex")));
            listener.Eeprom[0] = 0;
            listener.Eeprom[1] = 0;
            var a = new FirmwareCoSimulation(talker, c, u1.RefDes, () => c.PinVoltage(sim, u1.RefDes, "5V"));
            var b = new FirmwareCoSimulation(listener, c, u2.RefDes, () => c.PinVoltage(sim, u2.RefDes, "5V"));
            var heard = new StringBuilder();
            b.SerialOutput += s => heard.Append(s);
            for (int i = 0; i < (int)(seconds / Slice); i++)
            {
                a.Advance(Slice, sim.Time);
                b.Advance(Slice, sim.Time);
                sim.Step(Slice);
            }

            return heard.ToString();
        }

        private static void Label(Schematic sch, PartLibrary lib, SchematicPart part, string pin, string net)
        {
            var pd = lib.Get(part.PartId).FindPin(pin)!;
            sch.Labels.Add(new NetLabel { Name = net, Position = part.Transform(pd.X, pd.Y) });
        }

        private Action<Schematic, PartLibrary, SchematicPart, SchematicPart> Glitches(string width, string rate) => (sch, lib, u1, u2) =>
        {
            Label(sch, lib, u1, "D1", "LINK");
            Label(sch, lib, u2, "D0", "LINK");
            var fi = sch.AddPart(lib, "line-fault", new GridPoint(20, 20));
            fi.Parameters["fault"] = "glitch-low";
            fi.Parameters["width"] = width;
            fi.Parameters["rate"] = rate;
            fi.Parameters["start"] = "0.5";   // on the turn's message only
            Label(sch, lib, fi, "HAT", "LINK");
            Label(sch, lib, fi, "GND", "GND");
        };

        [Fact]
        public void ShortGlitchesAreFilteredLongOnesBreakBytes()
        {
            // 400 pulls to ground a second from 0.5 s on. 2 µs: the vote's samples are 13 µs apart (9600 baud, U2X), one
            // glitch spoils one sample at most. 60 µs, more than half a bit: bits flip and stop bits fall.
            string clean = Link(Glitches("2u", "400"), 0.66, out var c1);
            string broken = Link(Glitches("60u", "400"), 0.66, out var c2);
            int n1 = c1.Circuit.Devices.OfType<FaultSwitch>().Single().Strikes, n2 = c2.Circuit.Devices.OfType<FaultSwitch>().Single().Strikes;
            _out.WriteLine($"2 µs × {n1}: {clean.Replace("\r\n", " | ")}");
            _out.WriteLine($"60 µs × {n2}: {broken.Replace("\r\n", " | ")}");
            Assert.True(n1 > 40 && n2 > 40);
            Assert.Contains(Message, clean);
            Assert.DoesNotContain(Message, broken);
        }

        [Fact]
        public void ALooseContactLosesTheMessage()
        {
            string got = Link((sch, lib, u1, u2) =>
            {
                var lc = sch.AddPart(lib, "loose-contact", new GridPoint(20, 20));
                lc.Parameters["fault"] = "intermittent";
                lc.Parameters["rate"] = "400";
                lc.Parameters["width"] = "300u";
                lc.Parameters["start"] = "0.5";
                Label(sch, lib, u1, "D1", "TX");
                Label(sch, lib, lc, "A", "TX");
                Label(sch, lib, lc, "B", "RX");
                Label(sch, lib, u2, "D0", "RX");
            }, 0.66, out var c);
            var lcs = c.Circuit.Devices.OfType<FaultSwitch>().Single();
            _out.WriteLine($"{lcs.Strikes} breaks: {got}");
            Assert.True(lcs.Strikes > 20);
            Assert.DoesNotContain(Message, got);
            Assert.Contains("53 65 72 76 6F ", got);   // before it started to break, the boot message got through
        }

        [Fact]
        public void WithoutAFaultTheMessageGetsThrough()
        {
            string got = Link(null, 0.66, out _);
            Assert.Contains(Message, got);
        }
    }
}
