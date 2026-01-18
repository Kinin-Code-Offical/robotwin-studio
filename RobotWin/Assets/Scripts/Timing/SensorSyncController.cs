using UnityEngine;
using System;
using System.Collections.Generic;

namespace RobotTwin.Timing
{
    /// <summary>
    /// Sensor Synchronization Controller
    /// Ensures all sensors (Line, Color, Ultrasonic, LiDAR) update in lockstep with circuit timing
    /// NO DRIFT - sensor readings synchronized to firmware/circuit cycle timing
    /// </summary>
    public class SensorSyncController : MonoBehaviour, ILockstepSubsystem
    {
        public static SensorSyncController Instance { get; private set; }

        [Header("Sensor Timing")]
        [SerializeField] private bool _syncToCircuitTiming = true;

        [Header("Sensor-Specific Rates")]
        [SerializeField] private float _lineSensorRate = 1000f; // 1 kHz
        [SerializeField] private float _colorSensorRate = 100f; // 100 Hz
        [SerializeField] private float _ultrasonicRate = 50f; // 50 Hz
        [SerializeField] private float _lidarRate = 10f; // 10 Hz
        [SerializeField] private float _imuRate = 1000f; // 1 kHz

        [Header("Synchronization")]
        [SerializeField] private bool _batchSensorUpdates = true;

        // Registered sensors
        private List<ISynchronizedSensor> _sensors = new List<ISynchronizedSensor>();
        private Dictionary<SensorType, List<ISynchronizedSensor>> _sensorsByType = new Dictionary<SensorType, List<ISynchronizedSensor>>();

        // Timing accumulators (microseconds)
        private Dictionary<SensorType, long> _nextUpdateTimeMicros = new Dictionary<SensorType, long>();
        private long _currentTimeMicros = 0;

        public SensorMetrics Metrics { get; private set; } = new SensorMetrics();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            // Register with GlobalLatencyManager
            GlobalLatencyManager.Instance.RegisterLockstepSubsystem(this);
            GlobalLatencyManager.Instance.RegisterSubsystem("Sensors", 0.0);

            // Initialize sensor type lists
            foreach (SensorType type in Enum.GetValues(typeof(SensorType)))
            {
                _sensorsByType[type] = new List<ISynchronizedSensor>();
                _nextUpdateTimeMicros[type] = 0;
            }

            Debug.Log("[SensorSyncController] Initialized");
        }

        /// <summary>
        /// Register a sensor for synchronization
        /// </summary>
        public void RegisterSensor(ISynchronizedSensor sensor)
        {
            if (!_sensors.Contains(sensor))
            {
                _sensors.Add(sensor);

                SensorType type = sensor.GetSensorType();
                if (!_sensorsByType.ContainsKey(type))
                {
                    _sensorsByType[type] = new List<ISynchronizedSensor>();
                }
                _sensorsByType[type].Add(sensor);

                Debug.Log($"[SensorSyncController] Registered sensor: {sensor.GetSensorName()} ({type})");
            }
        }

        /// <summary>
        /// Unregister a sensor
        /// </summary>
        public void UnregisterSensor(ISynchronizedSensor sensor)
        {
            _sensors.Remove(sensor);

            SensorType type = sensor.GetSensorType();
            if (_sensorsByType.ContainsKey(type))
            {
                _sensorsByType[type].Remove(sensor);
            }

            Debug.Log($"[SensorSyncController] Unregistered sensor: {sensor.GetSensorName()}");
        }

        /// <summary>
        /// Lockstep update - called by GlobalLatencyManager
        /// </summary>
        public void LockstepUpdate(double deltaSeconds, long masterClockMicros)
        {
            if (!_syncToCircuitTiming)
                return;

            // Get circuit timing
            long circuitTime = GlobalLatencyManager.Instance.GetSubsystemTimeMicros("Circuit");
            double circuitLatency = GlobalLatencyManager.Instance.GetSubsystemLatencySeconds("Circuit");

            // Update current time with circuit latency
            _currentTimeMicros = circuitTime + (long)(circuitLatency * 1e6);

            // Update sensors based on their update rates
            UpdateSensorsByType();

            Metrics.CurrentTimeMicros = _currentTimeMicros;
            Metrics.TotalUpdates++;
        }

        /// <summary>
        /// Update sensors by type based on their individual rates
        /// </summary>
        private void UpdateSensorsByType()
        {
            // Line sensors (1 kHz)
            UpdateSensorsOfType(SensorType.LineSensor, _lineSensorRate);

            // Color sensors (100 Hz)
            UpdateSensorsOfType(SensorType.ColorSensor, _colorSensorRate);

            // Ultrasonic sensors (50 Hz)
            UpdateSensorsOfType(SensorType.Ultrasonic, _ultrasonicRate);

            // LiDAR sensors (10 Hz)
            UpdateSensorsOfType(SensorType.LiDAR, _lidarRate);

            // IMU sensors (1 kHz)
            UpdateSensorsOfType(SensorType.IMU, _imuRate);
        }

        /// <summary>
        /// Update all sensors of a specific type
        /// </summary>
        private void UpdateSensorsOfType(SensorType type, float updateRate)
        {
            if (!_sensorsByType.ContainsKey(type) || _sensorsByType[type].Count == 0)
                return;

            long updateIntervalMicros = (long)(1e6 / updateRate);
            long nextUpdateTime = _nextUpdateTimeMicros[type];

            // Check if it's time to update
            if (_currentTimeMicros >= nextUpdateTime)
            {
                List<ISynchronizedSensor> sensors = _sensorsByType[type];

                if (_batchSensorUpdates)
                {
                    // Batch update all sensors of this type
                    foreach (var sensor in sensors)
                    {
                        if (sensor.IsEnabled())
                        {
                            sensor.SynchronizedUpdate(_currentTimeMicros / 1e6);
                        }
                    }
                }
                else
                {
                    // Sequential update
                    foreach (var sensor in sensors)
                    {
                        if (sensor.IsEnabled())
                        {
                            sensor.SynchronizedUpdate(_currentTimeMicros / 1e6);
                        }
                    }
                }

                // Update next update time
                _nextUpdateTimeMicros[type] = _currentTimeMicros + updateIntervalMicros;

                // Update metrics
                UpdateMetricsForSensorType(type);
            }
        }

        /// <summary>
        /// Update metrics for sensor type
        /// </summary>
        private void UpdateMetricsForSensorType(SensorType type)
        {
            switch (type)
            {
                case SensorType.LineSensor:
                    Metrics.LineSensorUpdates++;
                    break;
                case SensorType.ColorSensor:
                    Metrics.ColorSensorUpdates++;
                    break;
                case SensorType.Ultrasonic:
                    Metrics.UltrasonicUpdates++;
                    break;
                case SensorType.LiDAR:
                    Metrics.LiDARUpdates++;
                    break;
                case SensorType.IMU:
                    Metrics.IMUUpdates++;
                    break;
            }
        }

        /// <summary>
        /// Get sensor count by type
        /// </summary>
        public int GetSensorCount(SensorType type)
        {
            return _sensorsByType.ContainsKey(type) ? _sensorsByType[type].Count : 0;
        }

        /// <summary>
        /// Get total sensor count
        /// </summary>
        public int GetTotalSensorCount()
        {
            return _sensors.Count;
        }

        /// <summary>
        /// Reset all timing
        /// </summary>
        public void Reset()
        {
            _currentTimeMicros = 0;
            foreach (var key in _nextUpdateTimeMicros.Keys)
            {
                _nextUpdateTimeMicros[key] = 0;
            }
            Metrics = new SensorMetrics();

            Debug.Log("[SensorSyncController] Reset complete");
        }

        // ILockstepSubsystem implementation
        public bool IsEnabled()
        {
            return enabled && _syncToCircuitTiming;
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUILayout.BeginArea(new Rect(830, 10, 400, 150));
                GUILayout.BeginVertical("box");
                GUILayout.Label("Sensor Synchronization");
                GUILayout.Label($"Current Time: {_currentTimeMicros / 1000.0:F3}ms");
                GUILayout.Label($"Total Sensors: {GetTotalSensorCount()}");
                GUILayout.Label($"Line: {GetSensorCount(SensorType.LineSensor)} ({Metrics.LineSensorUpdates} updates)");
                GUILayout.Label($"Color: {GetSensorCount(SensorType.ColorSensor)} ({Metrics.ColorSensorUpdates} updates)");
                GUILayout.Label($"Ultrasonic: {GetSensorCount(SensorType.Ultrasonic)} ({Metrics.UltrasonicUpdates} updates)");
                GUILayout.Label($"LiDAR: {GetSensorCount(SensorType.LiDAR)} ({Metrics.LiDARUpdates} updates)");
                GUILayout.Label($"IMU: {GetSensorCount(SensorType.IMU)} ({Metrics.IMUUpdates} updates)");
                GUILayout.EndVertical();
                GUILayout.EndArea();
            }
        }
    }

    /// <summary>
    /// Sensor type enumeration
    /// </summary>
    public enum SensorType
    {
        LineSensor,
        ColorSensor,
        Ultrasonic,
        LiDAR,
        IMU,
        Other
    }

    /// <summary>
    /// Interface for synchronized sensors
    /// </summary>
    public interface ISynchronizedSensor
    {
        void SynchronizedUpdate(double timeSeconds);
        SensorType GetSensorType();
        string GetSensorName();
        bool IsEnabled();
    }

    /// <summary>
    /// Sensor metrics
    /// </summary>
    [Serializable]
    public class SensorMetrics
    {
        public long CurrentTimeMicros;
        public long TotalUpdates;
        public long LineSensorUpdates;
        public long ColorSensorUpdates;
        public long UltrasonicUpdates;
        public long LiDARUpdates;
        public long IMUUpdates;

        public double GetCurrentTimeSeconds()
        {
            return CurrentTimeMicros / 1e6;
        }
    }
}
