using UnityEngine;
using System;

namespace RobotTwin.Timing
{
    /// <summary>
    /// Physics Lockstep Controller
    /// Ensures physics simulation runs in perfect lockstep with circuit timing
    /// NO DRIFT - physics uses exact same time delta as circuit/firmware
    /// </summary>
    public class PhysicsLockstepController : MonoBehaviour, ILockstepSubsystem
    {
        [Header("Physics Configuration")]
        [SerializeField] private bool _useDeterministicPhysics = true;
        [SerializeField] private float _fixedTimestep = 0.016f; // 60 Hz default
        [SerializeField] private int _maxSubsteps = 4;

        [Header("Synchronization")]
        [SerializeField] private bool _syncWithCircuitTiming = true;
        [SerializeField] private bool _adjustTimestepDynamically = false;

        [Header("Drift Correction")]
        [SerializeField] private bool _enableDriftCorrection = true;
        [SerializeField] private float _maxAllowedDrift = 0.001f; // 1ms

        // Timing state
        private double _physicsTimeMicros = 0;
        private double _targetTimeMicros = 0;
        private long _stepCount = 0;

        // Drift tracking
        private double _accumulatedDriftMicros = 0;
        private int _driftCorrectionsApplied = 0;

        // Native physics engine interface (if using NativeEngine)
        private bool _useNativeEngine = false;
        private IntPtr _nativeEngineHandle = IntPtr.Zero;

        public PhysicsMetrics Metrics { get; private set; } = new PhysicsMetrics();

        private void Start()
        {
            // Register with GlobalLatencyManager
            GlobalLatencyManager.Instance.RegisterLockstepSubsystem(this);
            GlobalLatencyManager.Instance.RegisterSubsystem("Physics", 0.0);

            // Configure Unity physics
            ConfigureUnityPhysics();

            Debug.Log($"[PhysicsLockstepController] Initialized - Fixed timestep: {_fixedTimestep}s");
        }

        private void ConfigureUnityPhysics()
        {
            if (_useDeterministicPhysics)
            {
                // Set deterministic physics parameters
                Time.fixedDeltaTime = _fixedTimestep;
                Physics.autoSimulation = false; // Manual simulation for lockstep
                Physics.autoSyncTransforms = true;

                // Configure solver for determinism
                Physics.defaultSolverIterations = 6;
                Physics.defaultSolverVelocityIterations = 1;

                Debug.Log("[PhysicsLockstepController] Deterministic physics configured");
            }
        }

        /// <summary>
        /// Lockstep update - called by GlobalLatencyManager
        /// Uses EXACT same delta as circuit/firmware
        /// </summary>
        public void LockstepUpdate(double deltaSeconds, long masterClockMicros)
        {
            if (!_syncWithCircuitTiming)
                return;

            // Get circuit timing from GlobalLatencyManager
            double circuitLatency = GlobalLatencyManager.Instance.GetSubsystemLatencySeconds("Circuit");
            long circuitTime = GlobalLatencyManager.Instance.GetSubsystemTimeMicros("Circuit");

            // Calculate target time (circuit time + latency)
            _targetTimeMicros = circuitTime + (long)(circuitLatency * 1e6);

            // Calculate drift
            double drift = _targetTimeMicros - _physicsTimeMicros;
            _accumulatedDriftMicros += drift;

            // Apply drift correction if needed
            if (_enableDriftCorrection && Math.Abs(drift) > _maxAllowedDrift * 1e6)
            {
                Debug.LogWarning($"[PhysicsLockstepController] DRIFT DETECTED: {drift / 1000.0:F3}ms - CORRECTING");
                _physicsTimeMicros = _targetTimeMicros;
                _driftCorrectionsApplied++;
                Metrics.DriftCorrections++;
            }

            // Step physics
            StepPhysics(deltaSeconds);

            Metrics.LastDriftMicros = (long)drift;
        }

        /// <summary>
        /// Step physics simulation
        /// </summary>
        private void StepPhysics(double deltaSeconds)
        {
            if (_useNativeEngine && _nativeEngineHandle != IntPtr.Zero)
            {
                // Use native physics engine
                StepNativeEngine(deltaSeconds);
            }
            else
            {
                // Use Unity physics
                StepUnityPhysics((float)deltaSeconds);
            }

            _physicsTimeMicros += (long)(deltaSeconds * 1e6);
            _stepCount++;
            Metrics.StepCount = _stepCount;
            Metrics.PhysicsTimeMicros = (long)_physicsTimeMicros;
        }

        /// <summary>
        /// Step Unity physics (manual simulation)
        /// </summary>
        private void StepUnityPhysics(float deltaTime)
        {
            // Manual physics simulation for lockstep
            Physics.Simulate(deltaTime);

            Metrics.UnityPhysicsSteps++;
        }

        /// <summary>
        /// Step native physics engine (if available)
        /// </summary>
        private void StepNativeEngine(double deltaSeconds)
        {
            // TODO: Implement native engine interop
            // This would call NativeEngine::Step(deltaSeconds)

            Metrics.NativeEngineSteps++;
        }

        /// <summary>
        /// Set native engine handle
        /// </summary>
        public void SetNativeEngineHandle(IntPtr handle)
        {
            _nativeEngineHandle = handle;
            _useNativeEngine = handle != IntPtr.Zero;
            Debug.Log($"[PhysicsLockstepController] Native engine handle set: 0x{handle:X}");
        }

        /// <summary>
        /// Get current physics time
        /// </summary>
        public double GetPhysicsTimeSeconds()
        {
            return _physicsTimeMicros / 1e6;
        }

        /// <summary>
        /// Get drift from circuit timing
        /// </summary>
        public double GetDriftMicroseconds()
        {
            return _targetTimeMicros - _physicsTimeMicros;
        }

        /// <summary>
        /// Check if synchronized with circuit timing
        /// </summary>
        public bool IsSynchronized()
        {
            double drift = Math.Abs(GetDriftMicroseconds());
            return drift <= _maxAllowedDrift * 1e6;
        }

        /// <summary>
        /// Force immediate synchronization
        /// </summary>
        public void ForceSynchronization()
        {
            _physicsTimeMicros = _targetTimeMicros;
            _accumulatedDriftMicros = 0;
            Debug.Log("[PhysicsLockstepController] Forced synchronization");
        }

        /// <summary>
        /// Reset physics timing
        /// </summary>
        public void Reset()
        {
            _physicsTimeMicros = 0;
            _targetTimeMicros = 0;
            _stepCount = 0;
            _accumulatedDriftMicros = 0;
            _driftCorrectionsApplied = 0;
            Metrics = new PhysicsMetrics();

            Debug.Log("[PhysicsLockstepController] Reset complete");
        }

        // ILockstepSubsystem implementation
        public bool IsEnabled()
        {
            return enabled && _syncWithCircuitTiming;
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUILayout.BeginArea(new Rect(420, 10, 400, 120));
                GUILayout.BeginVertical("box");
                GUILayout.Label("Physics Lockstep Controller");
                GUILayout.Label($"Physics Time: {_physicsTimeMicros / 1000.0:F3}ms");
                GUILayout.Label($"Target Time: {_targetTimeMicros / 1000.0:F3}ms");
                GUILayout.Label($"Drift: {(_targetTimeMicros - _physicsTimeMicros) / 1000.0:F3}ms");
                GUILayout.Label($"Steps: {_stepCount}");
                GUILayout.Label($"Status: {(IsSynchronized() ? "✓ SYNCED" : "⚠ DRIFT")}");
                GUILayout.EndVertical();
                GUILayout.EndArea();
            }
        }
    }

    /// <summary>
    /// Physics metrics
    /// </summary>
    [Serializable]
    public class PhysicsMetrics
    {
        public long StepCount;
        public long PhysicsTimeMicros;
        public long LastDriftMicros;
        public int DriftCorrections;
        public long UnityPhysicsSteps;
        public long NativeEngineSteps;

        public double GetPhysicsTimeSeconds()
        {
            return PhysicsTimeMicros / 1e6;
        }

        public double GetLastDriftMilliseconds()
        {
            return LastDriftMicros / 1000.0;
        }
    }
}
