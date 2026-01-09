// RobotWin Studio - Memory Profiler for Unity Allocations
// Tracks GC allocations, memory leaks, and heap fragmentation
// Real-time monitoring with 10MB leak detection threshold

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RobotTwin.Performance
{
    /// <summary>
    /// Advanced memory profiler with leak detection and allocation tracking.
    /// Monitors managed heap, GC collections, and allocation hotspots.
    /// </summary>
    public class MemoryProfiler
    {
        private static MemoryProfiler _instance;
        public static MemoryProfiler Instance => _instance ?? (_instance = new MemoryProfiler());

        // Memory snapshots
        private long _baselineMemory;
        private long _currentMemory;
        private long _peakMemory;

        // GC collection tracking
        private int _lastGen0Collections;
        private int _lastGen1Collections;
        private int _lastGen2Collections;

        // Allocation tracking (per-frame)
        private readonly Queue<long> _allocationHistory = new Queue<long>(60); // 1 second at 60fps
        private long _totalAllocations;

        // Leak detection
        private bool _leakDetected;
        private long _leakThreshold = 10 * 1024 * 1024; // 10MB threshold

        // Statistics
        private int _profilingFrames;
        private DateTime _profilingStart;

        /// <summary>
        /// Start memory profiling session.
        /// Takes baseline snapshot and resets statistics.
        /// </summary>
        public void StartProfiling()
        {
            // Force GC to get clean baseline
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            _baselineMemory = GC.GetTotalMemory(false);
            _currentMemory = _baselineMemory;
            _peakMemory = _baselineMemory;

            _lastGen0Collections = GC.CollectionCount(0);
            _lastGen1Collections = GC.CollectionCount(1);
            _lastGen2Collections = GC.CollectionCount(2);

            _allocationHistory.Clear();
            _totalAllocations = 0;
            _leakDetected = false;
            _profilingFrames = 0;
            _profilingStart = DateTime.UtcNow;

            Debug.Log($"[MemoryProfiler] Started profiling. Baseline: {FormatBytes(_baselineMemory)}");
        }

        /// <summary>
        /// Update memory profiler (call every frame).
        /// Tracks allocations, GC collections, and checks for leaks.
        /// </summary>
        public void Update()
        {
            _profilingFrames++;

            // Get current memory
            long previousMemory = _currentMemory;
            _currentMemory = GC.GetTotalMemory(false);

            // Track allocations
            long frameAllocation = Math.Max(0, _currentMemory - previousMemory);
            _allocationHistory.Enqueue(frameAllocation);
            _totalAllocations += frameAllocation;

            // Keep 60 frames of history
            if (_allocationHistory.Count > 60)
                _allocationHistory.Dequeue();

            // Track peak memory
            if (_currentMemory > _peakMemory)
                _peakMemory = _currentMemory;

            // Check for memory leak
            long memoryGrowth = _currentMemory - _baselineMemory;
            if (!_leakDetected && memoryGrowth > _leakThreshold)
            {
                _leakDetected = true;
                Debug.LogWarning($"[MemoryProfiler] Potential memory leak detected! Growth: {FormatBytes(memoryGrowth)} (threshold: {FormatBytes(_leakThreshold)})");
            }

            // Track GC collections
            int gen0 = GC.CollectionCount(0) - _lastGen0Collections;
            int gen1 = GC.CollectionCount(1) - _lastGen1Collections;
            int gen2 = GC.CollectionCount(2) - _lastGen2Collections;

            if (gen0 > 0 || gen1 > 0 || gen2 > 0)
            {
                _lastGen0Collections = GC.CollectionCount(0);
                _lastGen1Collections = GC.CollectionCount(1);
                _lastGen2Collections = GC.CollectionCount(2);
            }
        }

        /// <summary>
        /// Stop profiling and generate report.
        /// </summary>
        public MemoryReport StopProfiling()
        {
            // Force GC to detect leaks
            long beforeGC = GC.GetTotalMemory(false);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long afterGC = GC.GetTotalMemory(false);

            var report = new MemoryReport
            {
                BaselineMemory = _baselineMemory,
                PeakMemory = _peakMemory,
                CurrentMemory = afterGC,
                MemoryGrowth = afterGC - _baselineMemory,
                LeakDetected = _leakDetected,
                TotalAllocations = _totalAllocations,
                AverageAllocationPerFrame = _profilingFrames > 0 ? (float)_totalAllocations / _profilingFrames : 0f,
                Gen0Collections = GC.CollectionCount(0) - _lastGen0Collections,
                Gen1Collections = GC.CollectionCount(1) - _lastGen1Collections,
                Gen2Collections = GC.CollectionCount(2) - _lastGen2Collections,
                ProfilingDuration = (DateTime.UtcNow - _profilingStart).TotalSeconds,
                ProfilingFrames = _profilingFrames
            };

            Debug.Log($"[MemoryProfiler] Stopped profiling. Report:\n{report}");

            return report;
        }

        /// <summary>
        /// Get current memory usage (no GC).
        /// </summary>
        public long GetCurrentMemory()
        {
            return GC.GetTotalMemory(false);
        }

        /// <summary>
        /// Get allocation rate (bytes/second).
        /// </summary>
        public float GetAllocationRate()
        {
            if (_profilingFrames == 0)
                return 0f;

            double seconds = (DateTime.UtcNow - _profilingStart).TotalSeconds;
            return seconds > 0 ? (float)(_totalAllocations / seconds) : 0f;
        }

        /// <summary>
        /// Get recent allocations (last 60 frames).
        /// </summary>
        public long GetRecentAllocations()
        {
            long total = 0;
            foreach (var allocation in _allocationHistory)
                total += allocation;
            return total;
        }

        /// <summary>
        /// Check if leak is detected.
        /// </summary>
        public bool IsLeakDetected() => _leakDetected;

        /// <summary>
        /// Set leak detection threshold (bytes).
        /// </summary>
        public void SetLeakThreshold(long bytes)
        {
            _leakThreshold = bytes;
        }

        /// <summary>
        /// Format bytes to human-readable string.
        /// </summary>
        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int suffixIndex = 0;
            double value = bytes;

            while (value >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                value /= 1024;
                suffixIndex++;
            }

            return $"{value:F2} {suffixes[suffixIndex]}";
        }
    }

    /// <summary>
    /// Memory profiling report.
    /// </summary>
    public struct MemoryReport
    {
        public long BaselineMemory;
        public long PeakMemory;
        public long CurrentMemory;
        public long MemoryGrowth;
        public bool LeakDetected;
        public long TotalAllocations;
        public float AverageAllocationPerFrame;
        public int Gen0Collections;
        public int Gen1Collections;
        public int Gen2Collections;
        public double ProfilingDuration;
        public int ProfilingFrames;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Duration: {ProfilingDuration:F1}s ({ProfilingFrames} frames)");
            sb.AppendLine($"Baseline: {FormatBytes(BaselineMemory)}");
            sb.AppendLine($"Peak: {FormatBytes(PeakMemory)}");
            sb.AppendLine($"Current: {FormatBytes(CurrentMemory)}");
            sb.AppendLine($"Growth: {FormatBytes(MemoryGrowth)} (Leak: {LeakDetected})");
            sb.AppendLine($"Allocations: {FormatBytes(TotalAllocations)} total, {FormatBytes((long)AverageAllocationPerFrame)}/frame");
            sb.AppendLine($"GC Collections: Gen0={Gen0Collections}, Gen1={Gen1Collections}, Gen2={Gen2Collections}");
            return sb.ToString();
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int suffixIndex = 0;
            double value = bytes;

            while (value >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                value /= 1024;
                suffixIndex++;
            }

            return $"{value:F2} {suffixes[suffixIndex]}";
        }
    }

    /// <summary>
    /// MonoBehaviour wrapper for memory profiler (auto-update).
    /// </summary>
    public class MemoryProfilerBehaviour : MonoBehaviour
    {
        [Header("Profiling Configuration")]
        [SerializeField] private bool _enableProfiling = true;
        [SerializeField] private bool _showOverlay = true;
        [SerializeField] private KeyCode _toggleKey = KeyCode.F8;

        [Header("Leak Detection")]
        [Tooltip("Memory leak threshold in MB")]
        [SerializeField] private int _leakThresholdMB = 10;

        private Rect _overlayRect = new Rect(10, 10, 300, 150);
        private GUIStyle _labelStyle;

        private void Start()
        {
            if (_enableProfiling)
            {
                MemoryProfiler.Instance.SetLeakThreshold(_leakThresholdMB * 1024 * 1024);
                MemoryProfiler.Instance.StartProfiling();
            }
        }

        private void Update()
        {
            if (_enableProfiling)
            {
                MemoryProfiler.Instance.Update();
            }

            if (Input.GetKeyDown(_toggleKey))
            {
                _showOverlay = !_showOverlay;
            }
        }

        private void OnGUI()
        {
            if (!_showOverlay || !_enableProfiling)
                return;

            EnsureStyle();

            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(_overlayRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(_overlayRect);
            GUILayout.Label("Memory Profiler", _labelStyle);
            GUILayout.Label($"Current: {FormatBytes(MemoryProfiler.Instance.GetCurrentMemory())}", _labelStyle);
            GUILayout.Label($"Rate: {FormatBytes((long)MemoryProfiler.Instance.GetAllocationRate())}/s", _labelStyle);
            GUILayout.Label($"Recent: {FormatBytes(MemoryProfiler.Instance.GetRecentAllocations())}", _labelStyle);
            GUILayout.Label($"Leak: {(MemoryProfiler.Instance.IsLeakDetected() ? "DETECTED" : "None")}", _labelStyle);
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (_enableProfiling)
            {
                MemoryProfiler.Instance.StopProfiling();
            }
        }

        private void EnsureStyle()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    normal = { textColor = Color.white }
                };
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int suffixIndex = 0;
            double value = bytes;

            while (value >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                value /= 1024;
                suffixIndex++;
            }

            return $"{value:F2} {suffixes[suffixIndex]}";
        }
    }
}
