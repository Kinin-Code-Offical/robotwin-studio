using UnityEngine;
using UnityEngine.Profiling;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace RobotTwin.Runtime.Profiling
{
    /// <summary>
    /// Performance profiler for Unity systems
    /// Tracks frame time, memory allocation, physics step time
    /// </summary>
    public class PerformanceProfiler : MonoBehaviour
    {
        [Header("Profiling Settings")]
        [SerializeField] private bool enableProfiling = true;
        [SerializeField] private int sampleWindow = 60; // frames
        [SerializeField] private bool logToConsole = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.F9;

        private Queue<float> frameTimes = new Queue<float>();
        private Queue<float> renderTimes = new Queue<float>();
        private Queue<long> memorySnapshots = new Queue<long>();

        private Stopwatch frameTimer = new Stopwatch();
        private Stopwatch renderTimer = new Stopwatch();

        private float avgFrameTime;
        private float avgRenderTime;
        private long avgMemory;
        private int frameCount = 0;

        private StringBuilder reportBuilder = new StringBuilder();

        private void Start()
        {
            frameTimer.Start();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                enableProfiling = !enableProfiling;
                UnityEngine.Debug.Log($"Performance Profiling: {(enableProfiling ? "ENABLED" : "DISABLED")}");
            }

            if (!enableProfiling) return;

            // Frame time tracking
            float frameTime = (float)frameTimer.Elapsed.TotalMilliseconds;
            frameTimer.Restart();

            frameTimes.Enqueue(frameTime);
            if (frameTimes.Count > sampleWindow) frameTimes.Dequeue();

            // Memory tracking every 10 frames
            if (frameCount % 10 == 0)
            {
                long memory = Profiler.GetTotalAllocatedMemoryLong();
                memorySnapshots.Enqueue(memory);
                if (memorySnapshots.Count > sampleWindow / 10) memorySnapshots.Dequeue();
            }

            // Calculate averages
            if (frameTimes.Count > 0)
            {
                float sum = 0;
                foreach (float t in frameTimes) sum += t;
                avgFrameTime = sum / frameTimes.Count;
            }

            if (renderTimes.Count > 0)
            {
                float sum = 0;
                foreach (float t in renderTimes) sum += t;
                avgRenderTime = sum / renderTimes.Count;
            }

            if (memorySnapshots.Count > 0)
            {
                long sum = 0;
                foreach (long m in memorySnapshots) sum += m;
                avgMemory = sum / memorySnapshots.Count;
            }

            // Log periodic report
            if (logToConsole && frameCount % 300 == 0) // Every 5 seconds at 60 FPS
            {
                LogPerformanceReport();
            }

            frameCount++;
        }

        private void OnPreRender()
        {
            if (!enableProfiling) return;
            renderTimer.Restart();
        }

        private void OnPostRender()
        {
            if (!enableProfiling) return;

            float renderTime = (float)renderTimer.Elapsed.TotalMilliseconds;
            renderTimes.Enqueue(renderTime);
            if (renderTimes.Count > sampleWindow) renderTimes.Dequeue();
        }

        private void LogPerformanceReport()
        {
            reportBuilder.Clear();
            reportBuilder.AppendLine("=== Performance Report ===");
            reportBuilder.AppendLine($"Avg Frame Time: {avgFrameTime:F2} ms ({(1000f / avgFrameTime):F1} FPS)");
            reportBuilder.AppendLine($"Avg Render Time: {avgRenderTime:F2} ms");
            reportBuilder.AppendLine($"Memory: {avgMemory / (1024 * 1024)} MB");
            reportBuilder.AppendLine($"Samples: {frameTimes.Count} frames");

            // Performance warnings
            if (avgFrameTime > 16.67f)
            {
                reportBuilder.AppendLine("⚠️ WARNING: Frame time >16.67ms (below 60 FPS)");
            }
            if (avgFrameTime > 33.33f)
            {
                reportBuilder.AppendLine("⚠️ CRITICAL: Frame time >33.33ms (below 30 FPS)");
            }

            UnityEngine.Debug.Log(reportBuilder.ToString());
        }

        public void ProfileAction(string actionName, System.Action action)
        {
            if (!enableProfiling)
            {
                action?.Invoke();
                return;
            }

            Profiler.BeginSample(actionName);
            var sw = Stopwatch.StartNew();

            action?.Invoke();

            sw.Stop();
            Profiler.EndSample();

            UnityEngine.Debug.Log($"[PROFILE] {actionName}: {sw.Elapsed.TotalMilliseconds:F2} ms");
        }

        public float GetAverageFrameTime() => avgFrameTime;
        public float GetAverageRenderTime() => avgRenderTime;
        public long GetAverageMemory() => avgMemory;
        public float GetCurrentFPS() => 1000f / avgFrameTime;
    }
}
