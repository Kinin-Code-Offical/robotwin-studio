using System;
using System.Threading;
using System.Threading.Tasks;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.IPC;

namespace RobotTwin.CoreSim.Host
{
    public class SimHost
    {
        private volatile bool _running = false;
        private Thread? _simThread;
        private readonly double _dt; // Timestep in seconds
        private readonly CircuitSpec _spec;
        public CircuitSpec Circuit => _spec;
        private readonly IFirmwareClient _firmwareClient;

        public event Action<double>? OnTickComplete; // Notify UI of updates

        public double SimTime { get; private set; } = 0.0;
        public long TickCount { get; private set; } = 0;

        public SimHost(CircuitSpec spec, IFirmwareClient firmwareClient, double dt = 0.01) // 100Hz default
        {
            _spec = spec;
            _firmwareClient = firmwareClient;
            _dt = dt;
        }

        public void StartFirmwareProcess(string executablePath)
        {
            try
            {
                 var startInfo = new System.Diagnostics.ProcessStartInfo
                 {
                     FileName = executablePath,
                     UseShellExecute = false,
                     CreateNoWindow = true
                 };
                 System.Diagnostics.Process.Start(startInfo);
                 Console.WriteLine($"Started Firmware Engine: {executablePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start firmware process: {ex.Message}");
            }
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _simThread = new Thread(RunLoop);
            _simThread.IsBackground = true;
            _simThread.Name = "SimHostThread";
            _simThread.Start();
        }

        public void Stop()
        {
            _running = false;
            if (_simThread != null && _simThread.IsAlive)
            {
                _simThread.Join(500);
            }
        }

        private void RunLoop()
        {
            // Connect to firmware engine
            _firmwareClient.ConnectAsync().Wait();

            while (_running)
            {
                var startTime = DateTime.Now;

                Tick();

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                var sleepTime = _dt - elapsed;

                if (sleepTime > 0)
                {
                    Thread.Sleep((int)(sleepTime * 1000));
                }
            }

            _firmwareClient.Disconnect();
        }

        private void Tick()
        {
            // 1. Prepare Inputs for Firmware (e.g., from circuit sensors)
            var inputs = new FirmwareStepRequest(); 
            // TODO: Populate inputs from circuit state

            // 2. Step Firmware
            var result = _firmwareClient.Step(inputs);

            // 3. Update Circuit Logic based on Firmware Outputs (Pins)
            // TODO: Apply result.PinStates to circuit model
            // FastSolver.Solve(_spec, result);

            SimTime += _dt;
            TickCount++;

            OnTickComplete?.Invoke(SimTime);
        }
    }
}
