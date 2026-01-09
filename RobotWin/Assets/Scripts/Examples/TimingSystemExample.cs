using UnityEngine;
using System.Linq;
using RobotTwin.Timing;

namespace RobotTwin.Examples
{
    /// <summary>
    /// Example: Complete timing system integration
    /// Shows how to setup and use the global latency synchronization system
    /// </summary>
    public class TimingSystemExample : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private CircuitLatencyAdapter circuitAdapter;
        [SerializeField] private PhysicsLockstepController physicsController;
        [SerializeField] private TimingValidator validator;

        [Header("Configuration")]
        [SerializeField] private bool _autoSetup = true;
        [SerializeField] private bool _enableDebugOutput = true;

        // Native interop (if using VirtualMcu)
        private System.IntPtr _virtualMcuHandle = System.IntPtr.Zero;

        private void Start()
        {
            if (_autoSetup)
            {
                SetupTimingSystem();
            }

            Debug.Log("==============================================");
            Debug.Log("GLOBAL LATENCY SYNCHRONIZATION SYSTEM ACTIVE");
            Debug.Log("==============================================");
            Debug.Log("All subsystems synchronized with ZERO drift");
            Debug.Log("Press V: Validate timing");
            Debug.Log("Press R: Generate diagnostic report");
            Debug.Log("Press S: Force synchronization");
            Debug.Log("Press T: Reset timing");
            Debug.Log("==============================================");
        }

        /// <summary>
        /// Setup complete timing system
        /// </summary>
        private void SetupTimingSystem()
        {
            Debug.Log("[TimingSystemExample] Setting up timing system...");

            // 1. GlobalLatencyManager auto-initializes (singleton)
            var globalManager = GlobalLatencyManager.Instance;
            Debug.Log("[TimingSystemExample] GlobalLatencyManager initialized");

            // 2. Setup CircuitLatencyAdapter
            if (circuitAdapter == null)
            {
                circuitAdapter = gameObject.AddComponent<CircuitLatencyAdapter>();
            }
            Debug.Log("[TimingSystemExample] CircuitLatencyAdapter configured");

            // 3. Setup PhysicsLockstepController
            if (physicsController == null)
            {
                physicsController = gameObject.AddComponent<PhysicsLockstepController>();
            }
            Debug.Log("[TimingSystemExample] PhysicsLockstepController configured");

            // 4. Setup SensorSyncController (auto-creates)
            // Sensors will auto-register when they implement ISynchronizedSensor
            Debug.Log("[TimingSystemExample] SensorSyncController ready");

            // 5. Setup TimingValidator
            if (validator == null)
            {
                validator = gameObject.AddComponent<TimingValidator>();
            }
            Debug.Log("[TimingSystemExample] TimingValidator configured");

            // 6. Register all existing sensors
            RegisterAllSensors();

            Debug.Log("[TimingSystemExample] ✓ Timing system setup complete!");
        }

        /// <summary>
        /// Find and register all sensors in scene
        /// </summary>
        private void RegisterAllSensors()
        {
            var sensors = FindObjectsOfType<MonoBehaviour>()
                .OfType<ISynchronizedSensor>()
                .ToList();

            foreach (var sensor in sensors)
            {
                SensorSyncController.Instance.RegisterSensor(sensor);
            }

            Debug.Log($"[TimingSystemExample] Registered {sensors.Count} sensors");
        }

        private void Update()
        {
            // Keyboard shortcuts for debugging
            HandleDebugInput();

            // Optional: Display timing info every second
            if (_enableDebugOutput && Time.frameCount % 60 == 0)
            {
                DisplayTimingInfo();
            }
        }

        /// <summary>
        /// Handle debug keyboard input
        /// </summary>
        private void HandleDebugInput()
        {
            // V - Validate timing
            if (Input.GetKeyDown(KeyCode.V))
            {
                ValidationResult result = validator.ValidateTiming();
                Debug.Log($"[VALIDATION] Status: {result.Status}, Max Drift: {result.MaxDriftMicros / 1000.0:F3}ms");
                Debug.Log($"  Circuit: {result.CircuitDriftMicros / 1000.0:F3}ms");
                Debug.Log($"  Physics: {result.PhysicsDriftMicros / 1000.0:F3}ms");
                Debug.Log($"  Firmware: {result.FirmwareDriftMicros / 1000.0:F3}ms");
                Debug.Log($"  Sensors: {result.SensorDriftMicros / 1000.0:F3}ms");
            }

            // R - Generate diagnostic report
            if (Input.GetKeyDown(KeyCode.R))
            {
                string report = validator.GenerateDiagnosticReport();
                Debug.Log(report);
            }

            // S - Force synchronization
            if (Input.GetKeyDown(KeyCode.S))
            {
                GlobalLatencyManager.Instance.ForceSynchronization();
                Debug.Log("[SYNC] Forced synchronization of all subsystems");
            }

            // T - Reset timing
            if (Input.GetKeyDown(KeyCode.T))
            {
                ResetAllTiming();
                Debug.Log("[RESET] All timing reset to zero");
            }
        }

        /// <summary>
        /// Display current timing information
        /// </summary>
        private void DisplayTimingInfo()
        {
            var metrics = GlobalLatencyManager.Instance.Metrics;

            Debug.Log($"[TIMING] Master: {metrics.MasterClockMicros / 1000.0:F3}ms, " +
                     $"Circuit: {metrics.CircuitLatencyMicros / 1000.0:F3}ms, " +
                     $"Cycles: {metrics.CircuitCycleCount}, " +
                     $"Lockstep: {metrics.LockstepUpdates}, " +
                     $"Corrections: {metrics.TotalDriftCorrections}, " +
                     $"Ratio: {metrics.GetTimeRatio():F2}x");
        }

        /// <summary>
        /// Reset all timing systems
        /// </summary>
        private void ResetAllTiming()
        {
            GlobalLatencyManager.Instance.ResetAllClocks();
            circuitAdapter?.Reset();
            physicsController?.Reset();
            validator?.Reset();
        }

        /// <summary>
        /// Example: Manually update circuit latency
        /// Call this from your circuit simulation
        /// </summary>
        public void UpdateCircuitLatency(double latencySeconds, long cycleCount)
        {
            GlobalLatencyManager.Instance.UpdateCircuitLatency(latencySeconds, cycleCount);
        }

        /// <summary>
        /// Example: Get current synchronized time
        /// </summary>
        public double GetSynchronizedTime()
        {
            return GlobalLatencyManager.Instance.Metrics.MasterClockMicros / 1e6;
        }

        /// <summary>
        /// Example: Check if systems are synchronized
        /// </summary>
        public bool IsSynchronized()
        {
            GlobalLatencyManager.Instance.AreSubsystemsSynchronized(out string report);
            return validator.GetLastResult()?.Status == ValidationStatus.Synchronized;
        }

        private void OnDestroy()
        {
            Debug.Log("[TimingSystemExample] Shutting down timing system");
        }

        private void OnGUI()
        {
            // Display quick stats in top-right corner
            if (Event.current.type == EventType.Repaint)
            {
                GUILayout.BeginArea(new Rect(Screen.width - 310, 10, 300, 80));
                GUILayout.BeginVertical("box");
                GUILayout.Label("Timing System Status");

                var metrics = GlobalLatencyManager.Instance.Metrics;
                GUILayout.Label($"Time: {metrics.SimulatedTimeSeconds:F3}s");
                GUILayout.Label($"Ratio: {metrics.GetTimeRatio():F2}x real-time");

                string syncStatus;
                GlobalLatencyManager.Instance.AreSubsystemsSynchronized(out syncStatus);
                GUILayout.Label(syncStatus);

                GUILayout.EndVertical();
                GUILayout.EndArea();
            }
        }
    }

    // ==========================================
    // EXAMPLE SENSOR IMPLEMENTATIONS
    // ==========================================

    /// <summary>
    /// Example: Synchronized Line Sensor
    /// </summary>
    public class ExampleLineSensor : MonoBehaviour, ISynchronizedSensor
    {
        [SerializeField] private float _sensorValue = 0f;
        private double _lastUpdateTime = 0;

        private void Start()
        {
            // Auto-register with SensorSyncController
            SensorSyncController.Instance.RegisterSensor(this);
            Debug.Log($"[ExampleLineSensor] {gameObject.name} registered");
        }

        // Called by SensorSyncController at synchronized time (1 kHz for line sensors)
        public void SynchronizedUpdate(double timeSeconds)
        {
            _lastUpdateTime = timeSeconds;

            // Perform actual sensor reading
            _sensorValue = PerformReading();
        }

        private float PerformReading()
        {
            // Example: Cast ray downward to detect line
            RaycastHit hit;
            if (Physics.Raycast(transform.position, -transform.up, out hit, 0.1f))
            {
                // Check if hit a dark line (example)
                Renderer renderer = hit.collider.GetComponent<Renderer>();
                if (renderer != null && renderer.material.color.grayscale < 0.5f)
                {
                    return 1.0f; // Line detected
                }
            }
            return 0.0f; // No line
        }

        public SensorType GetSensorType() => SensorType.LineSensor;
        public string GetSensorName() => gameObject.name;
        public bool IsEnabled() => enabled && gameObject.activeInHierarchy;

        private void OnDestroy()
        {
            SensorSyncController.Instance.UnregisterSensor(this);
        }
    }

    /// <summary>
    /// Example: Synchronized Ultrasonic Sensor
    /// </summary>
    public class ExampleUltrasonicSensor : MonoBehaviour, ISynchronizedSensor
    {
        [SerializeField] private float _distance = 0f;
        [SerializeField] private float _maxRange = 4.0f;
        private double _lastUpdateTime = 0;

        private void Start()
        {
            SensorSyncController.Instance.RegisterSensor(this);
            Debug.Log($"[ExampleUltrasonicSensor] {gameObject.name} registered");
        }

        // Called at 50 Hz for ultrasonic sensors
        public void SynchronizedUpdate(double timeSeconds)
        {
            _lastUpdateTime = timeSeconds;
            _distance = MeasureDistance();
        }

        private float MeasureDistance()
        {
            RaycastHit hit;
            if (Physics.Raycast(transform.position, transform.forward, out hit, _maxRange))
            {
                return hit.distance;
            }
            return _maxRange; // No object detected
        }

        public SensorType GetSensorType() => SensorType.Ultrasonic;
        public string GetSensorName() => gameObject.name;
        public bool IsEnabled() => enabled && gameObject.activeInHierarchy;

        private void OnDestroy()
        {
            SensorSyncController.Instance.UnregisterSensor(this);
        }
    }
}
