using UnityEngine;
using System;
using System.Collections.Generic;

namespace RobotTwin.Timing
{
    /// <summary>
    /// Global Latency & Synchronization Manager
    /// Ensures ALL subsystems (Circuit, Firmware, Physics, Rendering) run in perfect lockstep
    /// NO DRIFT - Circuit latency propagates to entire system
    /// </summary>
    public class GlobalLatencyManager : MonoBehaviour
    {
        // Singleton
        private static GlobalLatencyManager _instance;
        public static GlobalLatencyManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("GlobalLatencyManager");
                    _instance = go.AddComponent<GlobalLatencyManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        [Header("Master Timing")]
        [SerializeField] private double _masterTimeScale = 1.0;
        [SerializeField] private bool _enableStrictLockstep = true;
        [SerializeField] private float _maxAllowedDrift = 0.001f; // 1ms max drift

        [Header("Subsystem Timing")]
        [SerializeField] private float _targetFPS = 60f;
        [SerializeField] private bool _useFixedTimestep = true;

        // Master clock (microseconds for precision)
        private long _masterClockMicros = 0;
        private long _lastSyncMicros = 0;

        // Circuit timing (from firmware/circuit simulation)
        private double _circuitLatencySeconds = 0.0;
        private long _circuitCycleCount = 0;
        private const double CircuitClockFrequency = 16000000.0; // 16 MHz default

        // Subsystem clocks (all synchronized to master)
        private Dictionary<string, SubsystemClock> _subsystemClocks = new Dictionary<string, SubsystemClock>();

        // Drift detection
        private Dictionary<string, DriftMonitor> _driftMonitors = new Dictionary<string, DriftMonitor>();

        // Lockstep control
        private bool _lockstepEnabled = true;
        private long _lockstepIntervalMicros = 16666; // ~60 FPS default
        private List<ILockstepSubsystem> _lockstepSubsystems = new List<ILockstepSubsystem>();

        // Performance metrics
        public TimingMetrics Metrics { get; private set; } = new TimingMetrics();

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            Initialize();
        }

        private void Initialize()
        {
            _masterClockMicros = 0;
            _lastSyncMicros = 0;

            // Register default subsystems
            RegisterSubsystem("Circuit", 0.0);
            RegisterSubsystem("Firmware", 0.0);
            RegisterSubsystem("Physics", 0.0);
            RegisterSubsystem("Rendering", 0.0);
            RegisterSubsystem("Sensors", 0.0);

            // Setup drift monitors
            foreach (var key in _subsystemClocks.Keys)
            {
                _driftMonitors[key] = new DriftMonitor
                {
                    MaxAllowedDriftMicros = (long)(_maxAllowedDrift * 1e6),
                    DriftCorrectionsApplied = 0
                };
            }

            _lockstepIntervalMicros = (long)(1e6 / _targetFPS);

            Debug.Log($"[GlobalLatencyManager] Initialized - Target FPS: {_targetFPS}, Lockstep: {_lockstepIntervalMicros}µs");
        }

        /// <summary>
        /// Register a subsystem for synchronization
        /// </summary>
        public void RegisterSubsystem(string name, double initialLatencySeconds)
        {
            if (!_subsystemClocks.ContainsKey(name))
            {
                _subsystemClocks[name] = new SubsystemClock
                {
                    Name = name,
                    CurrentTimeMicros = 0,
                    LatencyMicros = (long)(initialLatencySeconds * 1e6),
                    Enabled = true
                };

                Debug.Log($"[GlobalLatencyManager] Registered subsystem: {name}");
            }
        }

        /// <summary>
        /// Register lockstep subsystem
        /// </summary>
        public void RegisterLockstepSubsystem(ILockstepSubsystem subsystem)
        {
            if (!_lockstepSubsystems.Contains(subsystem))
            {
                _lockstepSubsystems.Add(subsystem);
                Debug.Log($"[GlobalLatencyManager] Registered lockstep subsystem: {subsystem.GetType().Name}");
            }
        }

        /// <summary>
        /// Update circuit latency from firmware/circuit simulation
        /// THIS IS THE KEY - Circuit latency becomes the master reference
        /// </summary>
        public void UpdateCircuitLatency(double latencySeconds, long cycleCount)
        {
            _circuitLatencySeconds = latencySeconds;
            _circuitCycleCount = cycleCount;

            // Update circuit subsystem clock
            if (_subsystemClocks.TryGetValue("Circuit", out SubsystemClock clock))
            {
                clock.LatencyMicros = (long)(latencySeconds * 1e6);
                clock.CurrentTimeMicros = (long)(cycleCount / CircuitClockFrequency * 1e6);
            }

            // CRITICAL: Propagate this latency to ALL subsystems
            PropagateLatencyToAllSubsystems(latencySeconds);

            Metrics.CircuitLatencyMicros = (long)(latencySeconds * 1e6);
            Metrics.CircuitCycleCount = cycleCount;
        }

        /// <summary>
        /// Propagate circuit latency to all subsystems
        /// Ensures perfect synchronization - NO DRIFT
        /// </summary>
        private void PropagateLatencyToAllSubsystems(double latencySeconds)
        {
            long latencyMicros = (long)(latencySeconds * 1e6);

            foreach (var kvp in _subsystemClocks)
            {
                if (kvp.Key != "Circuit") // Don't update Circuit itself
                {
                    SubsystemClock clock = kvp.Value;

                    // Apply latency with drift correction
                    long targetTime = _masterClockMicros + latencyMicros;
                    long drift = targetTime - clock.CurrentTimeMicros;

                    // Check drift threshold
                    if (_driftMonitors.TryGetValue(kvp.Key, out DriftMonitor monitor))
                    {
                        if (Math.Abs(drift) > monitor.MaxAllowedDriftMicros)
                        {
                            // CRITICAL DRIFT - Force correction
                            Debug.LogWarning($"[GlobalLatencyManager] DRIFT DETECTED in {kvp.Key}: {drift}µs - CORRECTING");
                            clock.CurrentTimeMicros = targetTime;
                            monitor.DriftCorrectionsApplied++;
                            monitor.LastDriftMicros = drift;
                            Metrics.TotalDriftCorrections++;
                        }
                        else
                        {
                            // Gradual correction
                            clock.CurrentTimeMicros += (long)(drift * 0.1); // 10% correction per frame
                        }
                    }

                    clock.LatencyMicros = latencyMicros;
                }
            }
        }

        /// <summary>
        /// Advance master clock - typically called from FixedUpdate
        /// </summary>
        public void AdvanceMasterClock(double deltaTimeSeconds)
        {
            long deltaMicros = (long)(deltaTimeSeconds * 1e6 * _masterTimeScale);
            _masterClockMicros += deltaMicros;

            // Advance all subsystem clocks in lockstep
            if (_lockstepEnabled)
            {
                foreach (var kvp in _subsystemClocks)
                {
                    if (kvp.Value.Enabled)
                    {
                        kvp.Value.CurrentTimeMicros += deltaMicros;
                    }
                }
            }

            Metrics.MasterClockMicros = _masterClockMicros;
            Metrics.FrameCount++;
        }

        /// <summary>
        /// Execute lockstep update for all registered subsystems
        /// </summary>
        public void ExecuteLockstepUpdate()
        {
            if (!_lockstepEnabled || !_enableStrictLockstep)
                return;

            long now = _masterClockMicros;
            long elapsed = now - _lastSyncMicros;

            // Only update if lockstep interval has passed
            if (elapsed >= _lockstepIntervalMicros)
            {
                double deltaSeconds = elapsed / 1e6;

                // Update all lockstep subsystems with SAME delta
                foreach (var subsystem in _lockstepSubsystems)
                {
                    if (subsystem.IsEnabled())
                    {
                        try
                        {
                            subsystem.LockstepUpdate(deltaSeconds, _masterClockMicros);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[GlobalLatencyManager] Lockstep update failed for {subsystem.GetType().Name}: {e.Message}");
                        }
                    }
                }

                _lastSyncMicros = now;
                Metrics.LockstepUpdates++;
            }
        }

        /// <summary>
        /// Get current time for a subsystem
        /// </summary>
        public long GetSubsystemTimeMicros(string subsystemName)
        {
            if (_subsystemClocks.TryGetValue(subsystemName, out SubsystemClock clock))
            {
                return clock.CurrentTimeMicros;
            }
            return _masterClockMicros;
        }

        /// <summary>
        /// Get current latency for a subsystem
        /// </summary>
        public double GetSubsystemLatencySeconds(string subsystemName)
        {
            if (_subsystemClocks.TryGetValue(subsystemName, out SubsystemClock clock))
            {
                return clock.LatencyMicros / 1e6;
            }
            return 0.0;
        }

        /// <summary>
        /// Check if subsystems are synchronized within threshold
        /// </summary>
        public bool AreSubsystemsSynchronized(out string driftReport)
        {
            long maxDrift = 0;
            string worstSubsystem = "";

            foreach (var kvp in _subsystemClocks)
            {
                long drift = Math.Abs(kvp.Value.CurrentTimeMicros - _masterClockMicros);
                if (drift > maxDrift)
                {
                    maxDrift = drift;
                    worstSubsystem = kvp.Key;
                }
            }

            driftReport = $"Max drift: {maxDrift}µs in {worstSubsystem}";
            return maxDrift <= (long)(_maxAllowedDrift * 1e6);
        }

        /// <summary>
        /// Force immediate synchronization of all subsystems
        /// </summary>
        public void ForceSynchronization()
        {
            Debug.Log("[GlobalLatencyManager] Forcing immediate synchronization of all subsystems");

            foreach (var kvp in _subsystemClocks)
            {
                kvp.Value.CurrentTimeMicros = _masterClockMicros;
            }

            Metrics.ForcedSyncs++;
        }

        /// <summary>
        /// Reset all clocks
        /// </summary>
        public void ResetAllClocks()
        {
            _masterClockMicros = 0;
            _lastSyncMicros = 0;
            _circuitCycleCount = 0;
            _circuitLatencySeconds = 0.0;

            foreach (var kvp in _subsystemClocks)
            {
                kvp.Value.CurrentTimeMicros = 0;
                kvp.Value.LatencyMicros = 0;
            }

            foreach (var kvp in _driftMonitors)
            {
                kvp.Value.DriftCorrectionsApplied = 0;
                kvp.Value.LastDriftMicros = 0;
            }

            Metrics = new TimingMetrics();

            Debug.Log("[GlobalLatencyManager] All clocks reset");
        }

        private void FixedUpdate()
        {
            if (_useFixedTimestep)
            {
                AdvanceMasterClock(Time.fixedDeltaTime);
                ExecuteLockstepUpdate();
            }
        }

        private void Update()
        {
            if (!_useFixedTimestep)
            {
                AdvanceMasterClock(Time.deltaTime);
                ExecuteLockstepUpdate();
            }

            // Update metrics
            Metrics.RealTimeSeconds = Time.realtimeSinceStartup;
            Metrics.SimulatedTimeSeconds = _masterClockMicros / 1e6;
            Metrics.TimeScale = _masterTimeScale;
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint)
            {
                // Display timing info (debug)
                GUILayout.BeginArea(new Rect(10, Screen.height - 150, 400, 140));
                GUILayout.BeginVertical("box");
                GUILayout.Label($"Master Clock: {_masterClockMicros / 1000.0:F3}ms");
                GUILayout.Label($"Circuit Latency: {_circuitLatencySeconds * 1000:F3}ms");
                GUILayout.Label($"Circuit Cycles: {_circuitCycleCount}");
                GUILayout.Label($"Lockstep Updates: {Metrics.LockstepUpdates}");
                GUILayout.Label($"Drift Corrections: {Metrics.TotalDriftCorrections}");

                string driftReport;
                bool synced = AreSubsystemsSynchronized(out driftReport);
                GUILayout.Label($"Status: {(synced ? "✓ SYNCED" : "⚠ DRIFT")} - {driftReport}");
                GUILayout.EndVertical();
                GUILayout.EndArea();
            }
        }
    }

    // Subsystem clock data
    public class SubsystemClock
    {
        public string Name;
        public long CurrentTimeMicros;
        public long LatencyMicros;
        public bool Enabled;
    }

    // Drift monitoring
    public class DriftMonitor
    {
        public long MaxAllowedDriftMicros;
        public long LastDriftMicros;
        public int DriftCorrectionsApplied;
    }

    // Interface for lockstep subsystems
    public interface ILockstepSubsystem
    {
        void LockstepUpdate(double deltaSeconds, long masterClockMicros);
        bool IsEnabled();
    }

    // Timing metrics for analysis
    [Serializable]
    public class TimingMetrics
    {
        public long MasterClockMicros;
        public long CircuitLatencyMicros;
        public long CircuitCycleCount;
        public long FrameCount;
        public long LockstepUpdates;
        public int TotalDriftCorrections;
        public int ForcedSyncs;
        public double RealTimeSeconds;
        public double SimulatedTimeSeconds;
        public double TimeScale;

        public double GetTimeRatio()
        {
            return RealTimeSeconds > 0 ? SimulatedTimeSeconds / RealTimeSeconds : 0.0;
        }
    }
}
