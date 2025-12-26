using Xunit;
using RobotTwin.CoreSim.Runtime;
using RobotTwin.CoreSim.Specs;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RobotTwin.CoreSim.Tests
{
    public class RunEngineTests
    {
        [Fact]
        public void Step_AdvancesTimeAndTick()
        {
            var engine = new RunEngine(new CircuitSpec { Name = "TestCircuit" });
            Assert.Equal(0, engine.Session.TickIndex);
            Assert.Equal(0f, engine.Session.TimeSeconds);

            engine.Step();

            Assert.Equal(1, engine.Session.TickIndex);
            Assert.Equal(0.02f, engine.Session.TimeSeconds, 5);
        }

        [Fact]
        public void Determinism_SameSeed_ProducesSameFrames()
        {
            var engine1 = new RunEngine(new CircuitSpec { Name = "TestCircuit" }, seed: 12345);
            var engine2 = new RunEngine(new CircuitSpec { Name = "TestCircuit" }, seed: 12345);

            engine1.Step();
            engine2.Step();

            // We need to capture the frames to compare, but engine currently publishes to bus.
            // Let's hook the bus.
            TelemetryFrame? frame1 = null;
            TelemetryFrame? frame2 = null;

            engine1.Bus.OnFrame += f => frame1 = f;
            engine2.Bus.OnFrame += f => frame2 = f;

            // Step again to capture
            engine1.Step();
            engine2.Step();

            Assert.NotNull(frame1);
            Assert.NotNull(frame2);
            Assert.Equal(frame1.TimeSeconds, frame2.TimeSeconds);
            Assert.Equal(frame1.Signals["sim_time"], frame2.Signals["sim_time"]);
            Assert.Equal(frame1.Signals["heartbeat"], frame2.Signals["heartbeat"]);
        }

        [Fact]
        public void Recorder_WritesFiles()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "CoreSimTest_" + System.Guid.NewGuid());
            var spec = new CircuitSpec 
            { 
                Name = "Test Circuit", 
                Components = new List<ComponentInstance>
                {
                    new ComponentInstance { InstanceID = "led1", CatalogID = "led", ParameterOverrides = new Dictionary<string, object>() }
                }
            };
            try
            {
                using (var recorder = new SimulationRecorder(tempDir))
                {
                    recorder.RecordFrame(new TelemetryFrame { TickIndex = 1, TimeSeconds = 0.02f });
                    recorder.RecordEvent(new EventLogEntry { Message = "Test" });
                }

                Assert.True(File.Exists(Path.Combine(tempDir, "frames.jsonl")));
                Assert.True(File.Exists(Path.Combine(tempDir, "events.jsonl")));
                
                var lines = File.ReadAllLines(Path.Combine(tempDir, "frames.jsonl"));
                Assert.Single(lines);
                Assert.Contains("0.02", lines[0]);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
