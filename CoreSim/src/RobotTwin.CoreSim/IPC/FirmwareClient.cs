using System;
using System.IO.Pipes;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json; // Using JSON for MVP phase as agreed

namespace RobotTwin.CoreSim.IPC
{
    public interface IFirmwareClient
    {
        Task ConnectAsync();
        void Disconnect();
        FirmwareStepResult Step(FirmwareStepRequest request);
    }

    public class FirmwareStepRequest
    {
        public double RailVoltage { get; set; } = 5.0;
        public int[] PinStates { get; set; } = new int[0]; // Input pins
    }

    public class FirmwareStepResult
    {
        public int[] PinStates { get; set; } = new int[0]; // Output/PWM pins
        public string SerialOutput { get; set; } = string.Empty;
    }

    public class FirmwareClient : IFirmwareClient
    {
        private const string PIPE_NAME = "RoboTwin.FirmwareEngine.v1";
        private NamedPipeClientStream? _client;
        private StreamReader? _reader;
        private StreamWriter? _writer;

        public bool IsConnected => _client != null && _client.IsConnected;

        public async Task ConnectAsync()
        {
            if (IsConnected) return;

            // TODO: Optional - Launch process if not running
            // StartEngine("path/to/firmware_engine.exe");

            try
            {
                _client = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut);
                await _client.ConnectAsync(5000); // 5s timeout
                _reader = new StreamReader(_client);
                _writer = new StreamWriter(_client) { AutoFlush = true };
                // Console.WriteLine("Connected to Firmware Engine Pipe."); 
            }
            catch (Exception)
            {
                throw; // Rethrow to let caller handle
            }
        }

        public void Disconnect()
        {
            _writer?.Dispose();
            _writer = null;
            _reader?.Dispose();
            _reader = null;
            _client?.Dispose();
            _client = null;
        }

        public FirmwareStepResult Step(FirmwareStepRequest request)
        {
            if (!IsConnected || _writer == null || _reader == null) return new FirmwareStepResult();

            try
            {
                // Simple JSON Framed with newline for MVP
                string jsonReq = JsonSerializer.Serialize(request);
                _writer.WriteLine(jsonReq);

                string? jsonRes = _reader.ReadLine();
                if (string.IsNullOrEmpty(jsonRes)) return new FirmwareStepResult();

                return JsonSerializer.Deserialize<FirmwareStepResult>(jsonRes) ?? new FirmwareStepResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"IPC Error: {ex.Message}");
                return new FirmwareStepResult();
            }
        }
    }
}
