using System;
using System.Diagnostics;
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
        private readonly SimHostOptions _options;

        public event Action<double>? OnTickComplete; // Notify UI of updates

        public double SimTime { get; private set; } = 0.0;
        public long TickCount { get; private set; } = 0;

        public FirmwareStepResult? LastFirmwareResult { get; private set; }

        public SimHostOptions Options => _options;

        public SimHost(CircuitSpec spec, IFirmwareClient firmwareClient, double dt = 0.01) // 100Hz default
        {
            _spec = spec;
            _firmwareClient = firmwareClient;
            _options = new SimHostOptions { DtSeconds = dt };
            _dt = dt;
        }

        public SimHost(CircuitSpec spec, IFirmwareClient firmwareClient, SimHostOptions? options)
        {
            _spec = spec;
            _firmwareClient = firmwareClient;
            _options = options ?? new SimHostOptions();

            if (_options.Deterministic != null && _options.Deterministic.Enabled && _options.Deterministic.DtSeconds > 0)
            {
                _dt = _options.Deterministic.DtSeconds;
            }
            else
            {
                _dt = _options.DtSeconds;
            }
        }

        /// <summary>
        /// Synchronous connect helper for deterministic/headless stepping.
        /// </summary>
        public void ConnectFirmware()
        {
            int timeoutMs = _options.Deterministic?.FirmwareConnectTimeoutMs ?? 5000;
            var task = _firmwareClient.ConnectAsync();
            if (!task.Wait(timeoutMs))
            {
                throw new TimeoutException($"Firmware ConnectAsync timed out after {timeoutMs}ms.");
            }
        }

        /// <summary>
        /// Single synchronous simulation tick with no sleeping or background thread.
        /// </summary>
        public FirmwareStepResult StepOnce(FirmwareStepRequest? inputs = null)
        {
            inputs ??= new FirmwareStepRequest();

            // Ensure a stable step sequence for deterministic replay.
            if (inputs.StepSequence == 0)
            {
                inputs.StepSequence = (ulong)(TickCount + 1);
            }

            if (inputs.DeltaMicros == 0)
            {
                uint delta = (uint)Math.Max(1, (int)Math.Round(_dt * 1_000_000.0));
                if (_options.Deterministic?.DeltaMicrosOverride is uint overrideMicros && overrideMicros > 0)
                {
                    delta = overrideMicros;
                }
                inputs.DeltaMicros = delta;
            }

            var result = _firmwareClient.Step(inputs);
            LastFirmwareResult = result;

            SimTime += _dt;
            TickCount++;
            OnTickComplete?.Invoke(SimTime);

            return result;
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
                var startTime = Stopwatch.GetTimestamp();

                Tick();

                var elapsed = (Stopwatch.GetTimestamp() - startTime) / (double)Stopwatch.Frequency;
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
            // Keep legacy threaded path behavior, but route through StepOnce() so both APIs stay consistent.
            StepOnce(null);
        }
    }
}
