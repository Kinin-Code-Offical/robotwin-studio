using UnityEngine;
using System;
using System.Collections.Generic;
using System.Text;

namespace RobotTwin.Timing
{
    /// <summary>
    /// Timing Validator & Drift Detector
    /// Continuously monitors all subsystems for timing drift
    /// Alerts when synchronization is lost, provides diagnostic reports
    /// </summary>
    public class TimingValidator : MonoBehaviour
    {
        [Header("Validation Configuration")]
        [SerializeField] private bool _enableContinuousValidation = true;
        [SerializeField] private float _validationIntervalSeconds = 1.0f;
        [SerializeField] private bool _logValidationResults = false;

        [Header("Drift Thresholds")]
        [SerializeField] private float _warningThresholdMicros = 1000f; // 1ms warning
        [SerializeField] private float _errorThresholdMicros = 10000f; // 10ms error
        [SerializeField] private float _criticalThresholdMicros = 100000f; // 100ms critical

        [Header("Automatic Correction")]
        [SerializeField] private bool _autoCorrectDrift = true;
        [SerializeField] private bool _autoResyncOnCritical = true;

        // Validation state
        private float _timeSinceLastValidation = 0f;
        private ValidationResult _lastResult = null;

        // Drift history
        private Queue<DriftSample> _driftHistory = new Queue<DriftSample>();
        private const int MaxDriftHistorySize = 100;

        // Alert tracking
        private int _warningCount = 0;
        private int _errorCount = 0;
        private int _criticalCount = 0;

        public ValidationMetrics Metrics { get; private set; } = new ValidationMetrics();

        private void Start()
        {
            Debug.Log("[TimingValidator] Initialized");
        }

        private void Update()
        {
            if (!_enableContinuousValidation)
                return;

            _timeSinceLastValidation += Time.deltaTime;

            if (_timeSinceLastValidation >= _validationIntervalSeconds)
            {
                ValidateTiming();
                _timeSinceLastValidation = 0f;
            }
        }

        /// <summary>
        /// Validate timing across all subsystems
        /// </summary>
        public ValidationResult ValidateTiming()
        {
            ValidationResult result = new ValidationResult
            {
                Timestamp = Time.realtimeSinceStartup,
                MasterClockMicros = GlobalLatencyManager.Instance.Metrics.MasterClockMicros
            };

            // Get timing from all subsystems
            long masterClock = GlobalLatencyManager.Instance.Metrics.MasterClockMicros;
            long circuitTime = GlobalLatencyManager.Instance.GetSubsystemTimeMicros("Circuit");
            long physicsTime = GlobalLatencyManager.Instance.GetSubsystemTimeMicros("Physics");
            long firmwareTime = GlobalLatencyManager.Instance.GetSubsystemTimeMicros("Firmware");
            long sensorTime = GlobalLatencyManager.Instance.GetSubsystemTimeMicros("Sensors");

            // Calculate drifts
            result.CircuitDriftMicros = circuitTime - masterClock;
            result.PhysicsDriftMicros = physicsTime - masterClock;
            result.FirmwareDriftMicros = firmwareTime - masterClock;
            result.SensorDriftMicros = sensorTime - masterClock;

            // Calculate max drift
            result.MaxDriftMicros = Math.Max(
                Math.Max(Math.Abs(result.CircuitDriftMicros), Math.Abs(result.PhysicsDriftMicros)),
                Math.Max(Math.Abs(result.FirmwareDriftMicros), Math.Abs(result.SensorDriftMicros))
            );

            // Determine status
            result.Status = DetermineStatus(result.MaxDriftMicros);

            // Record drift sample
            RecordDriftSample(result);

            // Handle alerts
            HandleAlerts(result);

            // Apply corrections if needed
            if (_autoCorrectDrift && result.Status != ValidationStatus.Synchronized)
            {
                ApplyDriftCorrection(result);
            }

            _lastResult = result;
            Metrics.TotalValidations++;

            if (_logValidationResults)
            {
                LogValidationResult(result);
            }

            return result;
        }

        /// <summary>
        /// Determine validation status based on drift
        /// </summary>
        private ValidationStatus DetermineStatus(long maxDrift)
        {
            long absDrift = Math.Abs(maxDrift);

            if (absDrift <= _warningThresholdMicros)
                return ValidationStatus.Synchronized;
            else if (absDrift <= _errorThresholdMicros)
                return ValidationStatus.MinorDrift;
            else if (absDrift <= _criticalThresholdMicros)
                return ValidationStatus.MajorDrift;
            else
                return ValidationStatus.Critical;
        }

        /// <summary>
        /// Record drift sample for history
        /// </summary>
        private void RecordDriftSample(ValidationResult result)
        {
            DriftSample sample = new DriftSample
            {
                Timestamp = result.Timestamp,
                MaxDriftMicros = result.MaxDriftMicros,
                Status = result.Status
            };

            _driftHistory.Enqueue(sample);

            // Maintain max history size
            while (_driftHistory.Count > MaxDriftHistorySize)
            {
                _driftHistory.Dequeue();
            }
        }

        /// <summary>
        /// Handle alerts based on validation status
        /// </summary>
        private void HandleAlerts(ValidationResult result)
        {
            switch (result.Status)
            {
                case ValidationStatus.MinorDrift:
                    _warningCount++;
                    Metrics.WarningCount++;
                    Debug.LogWarning($"[TimingValidator] MINOR DRIFT DETECTED: {result.MaxDriftMicros / 1000.0:F3}ms");
                    break;

                case ValidationStatus.MajorDrift:
                    _errorCount++;
                    Metrics.ErrorCount++;
                    Debug.LogError($"[TimingValidator] MAJOR DRIFT DETECTED: {result.MaxDriftMicros / 1000.0:F3}ms");
                    break;

                case ValidationStatus.Critical:
                    _criticalCount++;
                    Metrics.CriticalCount++;
                    Debug.LogError($"[TimingValidator] CRITICAL DRIFT: {result.MaxDriftMicros / 1000.0:F3}ms - FORCING RESYNC");

                    if (_autoResyncOnCritical)
                    {
                        GlobalLatencyManager.Instance.ForceSynchronization();
                    }
                    break;
            }
        }

        /// <summary>
        /// Apply drift correction
        /// </summary>
        private void ApplyDriftCorrection(ValidationResult result)
        {
            // Correction is already handled by GlobalLatencyManager's PropagateLatencyToAllSubsystems
            // This method can trigger additional corrections if needed

            Metrics.CorrectionsApplied++;
        }

        /// <summary>
        /// Log validation result
        /// </summary>
        private void LogValidationResult(ValidationResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"[TimingValidator] Validation Result:");
            sb.AppendLine($"  Status: {result.Status}");
            sb.AppendLine($"  Master Clock: {result.MasterClockMicros / 1000.0:F3}ms");
            sb.AppendLine($"  Circuit Drift: {result.CircuitDriftMicros / 1000.0:F3}ms");
            sb.AppendLine($"  Physics Drift: {result.PhysicsDriftMicros / 1000.0:F3}ms");
            sb.AppendLine($"  Firmware Drift: {result.FirmwareDriftMicros / 1000.0:F3}ms");
            sb.AppendLine($"  Sensor Drift: {result.SensorDriftMicros / 1000.0:F3}ms");
            sb.AppendLine($"  Max Drift: {result.MaxDriftMicros / 1000.0:F3}ms");

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Get last validation result
        /// </summary>
        public ValidationResult GetLastResult()
        {
            return _lastResult;
        }

        /// <summary>
        /// Get drift statistics
        /// </summary>
        public DriftStatistics GetDriftStatistics()
        {
            if (_driftHistory.Count == 0)
                return new DriftStatistics();

            long totalDrift = 0;
            long minDrift = long.MaxValue;
            long maxDrift = long.MinValue;

            foreach (var sample in _driftHistory)
            {
                long drift = Math.Abs(sample.MaxDriftMicros);
                totalDrift += drift;
                minDrift = Math.Min(minDrift, drift);
                maxDrift = Math.Max(maxDrift, drift);
            }

            return new DriftStatistics
            {
                SampleCount = _driftHistory.Count,
                AverageDriftMicros = totalDrift / _driftHistory.Count,
                MinDriftMicros = minDrift,
                MaxDriftMicros = maxDrift
            };
        }

        /// <summary>
        /// Generate diagnostic report
        /// </summary>
        public string GenerateDiagnosticReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine("TIMING VALIDATION DIAGNOSTIC REPORT");
            sb.AppendLine("========================================");
            sb.AppendLine();

            // Current status
            if (_lastResult != null)
            {
                sb.AppendLine("CURRENT STATUS:");
                sb.AppendLine($"  Status: {_lastResult.Status}");
                sb.AppendLine($"  Max Drift: {_lastResult.MaxDriftMicros / 1000.0:F3}ms");
                sb.AppendLine($"  Master Clock: {_lastResult.MasterClockMicros / 1000.0:F3}ms");
                sb.AppendLine();
            }

            // Drift statistics
            DriftStatistics stats = GetDriftStatistics();
            sb.AppendLine("DRIFT STATISTICS:");
            sb.AppendLine($"  Samples: {stats.SampleCount}");
            sb.AppendLine($"  Average: {stats.AverageDriftMicros / 1000.0:F3}ms");
            sb.AppendLine($"  Min: {stats.MinDriftMicros / 1000.0:F3}ms");
            sb.AppendLine($"  Max: {stats.MaxDriftMicros / 1000.0:F3}ms");
            sb.AppendLine();

            // Alert counts
            sb.AppendLine("ALERT COUNTS:");
            sb.AppendLine($"  Warnings: {_warningCount}");
            sb.AppendLine($"  Errors: {_errorCount}");
            sb.AppendLine($"  Critical: {_criticalCount}");
            sb.AppendLine();

            // Metrics
            sb.AppendLine("METRICS:");
            sb.AppendLine($"  Total Validations: {Metrics.TotalValidations}");
            sb.AppendLine($"  Corrections Applied: {Metrics.CorrectionsApplied}");
            sb.AppendLine();

            sb.AppendLine("========================================");

            return sb.ToString();
        }

        /// <summary>
        /// Reset all validation state
        /// </summary>
        public void Reset()
        {
            _timeSinceLastValidation = 0f;
            _lastResult = null;
            _driftHistory.Clear();
            _warningCount = 0;
            _errorCount = 0;
            _criticalCount = 0;
            Metrics = new ValidationMetrics();

            Debug.Log("[TimingValidator] Reset complete");
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint && _lastResult != null)
            {
                GUILayout.BeginArea(new Rect(10, 140, 400, 100));
                GUILayout.BeginVertical("box");
                GUILayout.Label("Timing Validation");
                GUILayout.Label($"Status: {GetStatusIcon(_lastResult.Status)} {_lastResult.Status}");
                GUILayout.Label($"Max Drift: {_lastResult.MaxDriftMicros / 1000.0:F3}ms");
                GUILayout.Label($"Alerts: W:{_warningCount} E:{_errorCount} C:{_criticalCount}");
                GUILayout.Label($"Corrections: {Metrics.CorrectionsApplied}");
                GUILayout.EndVertical();
                GUILayout.EndArea();
            }
        }

        private string GetStatusIcon(ValidationStatus status)
        {
            switch (status)
            {
                case ValidationStatus.Synchronized: return "✓";
                case ValidationStatus.MinorDrift: return "⚠";
                case ValidationStatus.MajorDrift: return "⚠⚠";
                case ValidationStatus.Critical: return "❌";
                default: return "?";
            }
        }
    }

    /// <summary>
    /// Validation result
    /// </summary>
    [Serializable]
    public class ValidationResult
    {
        public float Timestamp;
        public long MasterClockMicros;
        public long CircuitDriftMicros;
        public long PhysicsDriftMicros;
        public long FirmwareDriftMicros;
        public long SensorDriftMicros;
        public long MaxDriftMicros;
        public ValidationStatus Status;
    }

    /// <summary>
    /// Validation status
    /// </summary>
    public enum ValidationStatus
    {
        Synchronized,   // All systems in sync (<1ms drift)
        MinorDrift,     // Minor drift detected (1-10ms)
        MajorDrift,     // Major drift detected (10-100ms)
        Critical        // Critical drift (>100ms) - requires immediate correction
    }

    /// <summary>
    /// Drift sample for history
    /// </summary>
    public class DriftSample
    {
        public float Timestamp;
        public long MaxDriftMicros;
        public ValidationStatus Status;
    }

    /// <summary>
    /// Drift statistics
    /// </summary>
    [Serializable]
    public class DriftStatistics
    {
        public int SampleCount;
        public long AverageDriftMicros;
        public long MinDriftMicros;
        public long MaxDriftMicros;

        public double GetAverageMilliseconds()
        {
            return AverageDriftMicros / 1000.0;
        }
    }

    /// <summary>
    /// Validation metrics
    /// </summary>
    [Serializable]
    public class ValidationMetrics
    {
        public long TotalValidations;
        public int WarningCount;
        public int ErrorCount;
        public int CriticalCount;
        public int CorrectionsApplied;
    }
}
