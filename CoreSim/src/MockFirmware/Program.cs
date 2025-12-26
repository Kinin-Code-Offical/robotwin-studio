using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;
using RobotTwin.CoreSim.IPC;

namespace RobotTwin.MockFirmware
{
    class Program
    {
        private const string PIPE_NAME = "RoboTwin.FirmwareEngine.v1";

        static async Task Main(string[] args)
        {
            Console.WriteLine("Mock Firmware Engine V1.0");
            Console.WriteLine($"Listening on Pending Pipe: {PIPE_NAME}...");

            while (true)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(PIPE_NAME, PipeDirection.InOut, 1))
                    {
                        await server.WaitForConnectionAsync();
                        Console.WriteLine("Client Connected!");

                        using (var reader = new StreamReader(server))
                        using (var writer = new StreamWriter(server) { AutoFlush = true })
                        {
                            double simTime = 0.0;
                            double dt = 0.01; // Mock dt

                            while (server.IsConnected)
                            {
                                string line = await reader.ReadLineAsync();
                                if (line == null) break;

                                var request = JsonSerializer.Deserialize<FirmwareStepRequest>(line);
                                
                                // --- MOCK BLINK LOGIC ---
                                simTime += dt;
                                int pin13State = (simTime % 1.0 < 0.5) ? 1 : 0;
                                // ------------------------

                                var result = new FirmwareStepResult
                                {
                                    PinStates = new int[] { pin13State }, // Assuming Index 0 is Pin 13 for mock
                                    SerialOutput = simTime < 0.02 ? "Booting..." : null
                                };

                                string response = JsonSerializer.Serialize(result);
                                await writer.WriteLineAsync(response);
                            }
                        }
                    }
                    Console.WriteLine("Client Disconnected. Waiting for new connection...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    await Task.Delay(1000);
                }
            }
        }
    }
}
