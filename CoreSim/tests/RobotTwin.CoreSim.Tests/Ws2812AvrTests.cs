using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Devices;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[neopixel-avr] An Uno bit-banging a WS2812B strip (Adafruit_NeoPixel's way): the strip reads the pin's own edges.</summary>
    public class Ws2812AvrTests
    {
        private readonly ITestOutputHelper _out;

        public Ws2812AvrTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void TheDecoderReadsHighPulsesAsBitsAndLatchesOnALongLow()
        {
            var d = new Ws2812Decoder();
            var frames = new List<byte[]>();
            d.Frame += frames.Add;
            double t = 0;
            void Bit(bool one)
            {
                d.Edge(true, t);
                d.Edge(false, t + (one ? 0.8e-6 : 0.4e-6));
                t += 1.25e-6;
            }

            foreach (byte b in new byte[] { 0xA5, 0x0F })
                for (int k = 7; k >= 0; k--) Bit((b >> k & 1) != 0);
            d.Poll(t + 20e-6);
            Assert.Empty(frames);   // 20 µs low is not yet the reset code
            d.Poll(t + 60e-6);
            Assert.Equal(new byte[] { 0xA5, 0x0F }, Assert.Single(frames));

            t += 100e-6;   // a 10 µs high is no bit: the frame so far is dropped
            Bit(true);
            d.Edge(true, t);
            d.Edge(false, t + 10e-6);
            t += 20e-6;
            for (int k = 0; k < 8; k++) Bit(k == 0);
            d.Poll(t + 60e-6);
            Assert.Equal(1, d.BadPulses);
            Assert.Equal(new byte[] { 0x80 }, frames[1]);
        }

        [Fact]
        public void AnUnoLightsAStripFromItsOwnCycleTimedEdges()
        {
            var lib = PartLibrary.Default;
            var w = new WorldDocument();
            var uno = w.AddComponent(lib, "arduino-uno", new WorldPose());
            var strip = w.AddComponent(lib, "ws2812-strip", new WorldPose { X = 0.1 });
            strip.Parameters["count"] = "3";
            void Wire(WorldObject a, string pa, WorldObject z, string pz) => w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = z.Id, Pin = pz });
            Wire(uno, "5V", strip, "VDD");
            Wire(uno, "GND1", strip, "GND");
            Wire(uno, "D6", strip, "DIN");
            uno.Firmware = new FirmwareSource { FileName = "neopixel-avr.hex", Hex = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "neopixel-avr.hex")) };
            using var runner = new WorldRunner(w, new RunnerOptions());
            var load = runner.Compiled!.Circuit.Devices.OfType<Ws2812Load>().Single();
            runner.Step(0.1);
            string text = string.Concat(runner.ReadSerial(null, false).Values);
            foreach (var e in runner.DrainEvents()) _out.WriteLine($"[{e.Time:0.000}] {e.Source}: {e.Message}");
            _out.WriteLine(text);
            Assert.Contains("shown 2", text);
            Assert.Equal(((byte)20, (byte)10, (byte)30), load.Strip.Colors[0]);   // the second, shorter frame changed LED0 only
            Assert.Equal(((byte)0, (byte)128, (byte)0), load.Strip.Colors[1]);
            Assert.Equal(((byte)0, (byte)0, (byte)7), load.Strip.Colors[2]);
            Assert.Equal(2, load.Strip.Frames);
            Assert.False(load.DataOutOfSpec);   // the Uno's 5 V data suits a 5 V strip
        }
    }
}
