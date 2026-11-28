using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Analysis.Recording;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [unified-timeline] A live run recorded as it goes (no render): the same run stepped as the app steps it, frame by
    /// frame, gives the timeline's serial lines, the board's pin edges and interrupts, and the poses for the replay.
    /// </summary>
    public class LiveRecorderTests
    {
        private readonly ITestOutputHelper _out;

        public LiveRecorderTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void ALiveRunIsRecordedAsItGoes()
        {
            var lib = PartLibrary.Default;
            var w = new WorldDocument { Title = "Canlı kayıt" };
            var uno = w.AddComponent(lib, "arduino-uno", new WorldPose());
            var strip = w.AddComponent(lib, "ws2812-strip", new WorldPose { X = 0.1 });
            strip.Parameters["count"] = "3";
            void Wire(WorldObject a, string pa, WorldObject z, string pz) => w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = z.Id, Pin = pz });
            Wire(uno, "5V", strip, "VDD");
            Wire(uno, "GND1", strip, "GND");
            Wire(uno, "D6", strip, "DIN");
            uno.Firmware = new FirmwareSource { FileName = "neopixel-avr.hex", Hex = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "neopixel-avr.hex")) };
            using var runner = new WorldRunner(w, new RunnerOptions());
            var recorder = new LiveRecorder(runner, outputRate: 200);
            for (int frame = 0; frame < 6; frame++) runner.Step(1.0 / 60);   // the app's frames
            var r = recorder.Finish();
            foreach (var line in r.Log) _out.WriteLine(line);

            Assert.Equal("Canlı kayıt", r.WorldTitle);
            Assert.InRange(r.SimulatedSeconds, 0.099, 0.101);
            Assert.InRange(r.Channels.Count, 7, 25);   // a sample at most every 5 ms of the run's steps, the first and the last
            Assert.Contains(r.Serial, s => s.Text.Contains("shown"));
            var d6 = r.Logic.SingleOrDefault(t => t.Pin == "D6");
            Assert.NotNull(d6);
            Assert.True(d6!.Edges.Count >= 2 * 24 * 3, "every bit of three LEDs' frame is two edges");
            Assert.Contains(r.Interrupts, i => i.Vector.Contains("TIMER0"));
            Assert.Contains(r.BoardNames.Values, n => n.Length > 0);
            Assert.NotNull(r.Channels.Find("pose/" + uno.Id + "/px"));
            Assert.Same(r, recorder.Finish());   // finished once
        }
    }
}
