using System;
using System.IO;
using System.Text.Json;

namespace RobotTwin.CoreSim.Runtime
{
    public class SimulationRecorder : IDisposable
    {
        private readonly string _outputPath;
        private StreamWriter? _frameWriter;
        private StreamWriter? _eventWriter;

        public SimulationRecorder(string outputDir)
        {
            _outputPath = outputDir;
            Directory.CreateDirectory(_outputPath);
            
            _frameWriter = new StreamWriter(Path.Combine(_outputPath, "frames.jsonl"));
            _eventWriter = new StreamWriter(Path.Combine(_outputPath, "events.jsonl"));
        }

        public void Attach(TelemetryBus bus)
        {
            bus.OnFrame += RecordFrame;
            bus.OnEvent += RecordEvent;
        }

        public void Detach(TelemetryBus bus)
        {
            bus.OnFrame -= RecordFrame;
            bus.OnEvent -= RecordEvent;
        }

        public void RecordFrame(TelemetryFrame frame)
        {
            var json = JsonSerializer.Serialize(frame);
            _frameWriter?.WriteLine(json);
        }

        public void RecordEvent(EventLogEntry entry)
        {
            var json = JsonSerializer.Serialize(entry);
            _eventWriter?.WriteLine(json);
        }

        public void Flush()
        {
            _frameWriter?.Flush();
            _eventWriter?.Flush();
        }

        public void Dispose()
        {
            _frameWriter?.Dispose();
            _eventWriter?.Dispose();
            _frameWriter = null;
            _eventWriter = null;
        }
    }
}
