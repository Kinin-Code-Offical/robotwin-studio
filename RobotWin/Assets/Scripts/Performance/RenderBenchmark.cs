using UnityEngine;
using RobotTwin.Performance;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;

namespace RobotTwin.Tests
{
    /// <summary>
    /// Real-world render performance benchmark for circuit visualization.
    /// Tests RenderOptimizer + ComponentLOD + MemoryProfiler under production load.
    /// Usage: Attach to GameObject in test scene, press Space to run benchmark.
    /// </summary>
    public class RenderBenchmark : MonoBehaviour
    {
        [Header("Test Configuration")]
        [SerializeField] private int componentCount = 1000;
        [SerializeField] private bool autoRunOnStart = false;

        [Header("Prefabs (Assign in Inspector)")]
        [SerializeField] private GameObject arduinoPrefab;
        [SerializeField] private GameObject resistorPrefab;
        [SerializeField] private GameObject ledPrefab;

        private Camera _mainCamera;
        private GameObject _rootObject;
        private MemoryProfiler _memoryProfiler;
        private bool _benchmarkRunning = false;

        void Start()
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                UnityEngine.Debug.LogError("[RenderBenchmark] No main camera found!");
                return;
            }

            _memoryProfiler = new MemoryProfiler();

            if (autoRunOnStart)
            {
                RunBenchmark();
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space) && !_benchmarkRunning)
            {
                RunBenchmark();
            }
        }

        public void RunBenchmark()
        {
            if (_benchmarkRunning) return;
            _benchmarkRunning = true;

            UnityEngine.Debug.Log("=== RENDER OPTIMIZATION BENCHMARK ===");
            UnityEngine.Debug.Log($"Component Count: {componentCount}");
            UnityEngine.Debug.Log($"Unity Version: {Application.unityVersion}");
            UnityEngine.Debug.Log($"Quality Level: {QualitySettings.names[QualitySettings.GetQualityLevel()]}");

            // Phase 1: Setup test scene
            var setupTimer = Stopwatch.StartNew();
            SetupTestScene();
            setupTimer.Stop();
            UnityEngine.Debug.Log($"\n[Phase 1] Scene Setup: {setupTimer.ElapsedMilliseconds}ms");

            // Phase 2: Measure unoptimized performance
            var unoptimizedMetrics = MeasureRenderPerformance("Unoptimized");

            // Phase 3: Apply optimization
            var optimizeTimer = Stopwatch.StartNew();
            var result = RenderOptimizer.OptimizeScene(_rootObject, _mainCamera);
            optimizeTimer.Stop();
            UnityEngine.Debug.Log($"\n[Phase 3] Optimization Time: {optimizeTimer.ElapsedMilliseconds}ms");
            UnityEngine.Debug.Log($"Optimization Result: {result}");

            // Phase 4: Measure optimized performance
            var optimizedMetrics = MeasureRenderPerformance("Optimized");

            // Phase 5: Calculate improvements
            PrintComparisonReport(unoptimizedMetrics, optimizedMetrics);

            _benchmarkRunning = false;
        }

        private void SetupTestScene()
        {
            // Cleanup old scene
            if (_rootObject != null)
            {
                Destroy(_rootObject);
            }

            _rootObject = new GameObject("BenchmarkRoot");

            // Distribute components in 3D grid
            int gridSize = Mathf.CeilToInt(Mathf.Pow(componentCount, 1f / 3f));
            int componentsPlaced = 0;

            for (int x = 0; x < gridSize && componentsPlaced < componentCount; x++)
            {
                for (int y = 0; y < gridSize && componentsPlaced < componentCount; y++)
                {
                    for (int z = 0; z < gridSize && componentsPlaced < componentCount; z++)
                    {
                        GameObject prefab = GetRandomPrefab();
                        if (prefab == null) continue;

                        var instance = Instantiate(prefab, _rootObject.transform);
                        instance.transform.position = new Vector3(x * 2, y * 2, z * 2);
                        instance.name = $"Component_{componentsPlaced}";

                        // Add colliders for realism
                        if (instance.GetComponent<Collider>() == null)
                        {
                            instance.AddComponent<BoxCollider>();
                        }

                        componentsPlaced++;
                    }
                }
            }

            UnityEngine.Debug.Log($"Created {componentsPlaced} components in {gridSize}x{gridSize}x{gridSize} grid");

            // Position camera to view entire scene
            float cameraDistance = gridSize * 3f;
            _mainCamera.transform.position = new Vector3(gridSize, gridSize, -cameraDistance);
            _mainCamera.transform.LookAt(new Vector3(gridSize / 2f, gridSize / 2f, gridSize / 2f));
        }

        private GameObject GetRandomPrefab()
        {
            var prefabs = new List<GameObject>();
            if (arduinoPrefab != null) prefabs.Add(arduinoPrefab);
            if (resistorPrefab != null) prefabs.Add(resistorPrefab);
            if (ledPrefab != null) prefabs.Add(ledPrefab);

            if (prefabs.Count == 0)
            {
                // Fallback: Create primitive cubes
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.GetComponent<Renderer>().material.color = Random.ColorHSV();
                return cube;
            }

            return prefabs[Random.Range(0, prefabs.Count)];
        }

        private RenderMetrics MeasureRenderPerformance(string label)
        {
            UnityEngine.Debug.Log($"\n[Measuring {label} Performance]");

            // Start memory profiling
            _memoryProfiler.StartProfiling();

            // Warm-up frames
            for (int i = 0; i < 10; i++)
            {
                _mainCamera.Render();
            }

            // Measure frame times
            int sampleCount = 100;
            var frameTimes = new List<float>();
            var timer = Stopwatch.StartNew();

            for (int i = 0; i < sampleCount; i++)
            {
                var frameStart = timer.ElapsedTicks;
                _mainCamera.Render();
                var frameEnd = timer.ElapsedTicks;

                float frameTimeMs = (frameEnd - frameStart) * 1000f / Stopwatch.Frequency;
                frameTimes.Add(frameTimeMs);
            }

            timer.Stop();

            // Stop memory profiling
            var memoryReport = _memoryProfiler.StopProfiling();

            // Calculate statistics
            frameTimes.Sort();
            float avgFrameTime = 0;
            foreach (var ft in frameTimes) avgFrameTime += ft;
            avgFrameTime /= frameTimes.Count;

            float minFrameTime = frameTimes[0];
            float maxFrameTime = frameTimes[frameTimes.Count - 1];
            float p50 = frameTimes[frameTimes.Count / 2];
            float p95 = frameTimes[(int)(frameTimes.Count * 0.95f)];
            float p99 = frameTimes[(int)(frameTimes.Count * 0.99f)];

            int triangleCount = CountTriangles();
            int drawCalls = EstimateDrawCalls();

            var metrics = new RenderMetrics
            {
                Label = label,
                AvgFrameTimeMs = avgFrameTime,
                MinFrameTimeMs = minFrameTime,
                MaxFrameTimeMs = maxFrameTime,
                P50Ms = p50,
                P95Ms = p95,
                P99Ms = p99,
                TriangleCount = triangleCount,
                DrawCalls = drawCalls,
                MemoryReport = memoryReport
            };

            UnityEngine.Debug.Log(metrics.ToString());

            return metrics;
        }

        private int CountTriangles()
        {
            int total = 0;
            var renderers = _rootObject.GetComponentsInChildren<MeshRenderer>();
            foreach (var renderer in renderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    total += filter.sharedMesh.triangles.Length / 3;
                }
            }
            return total;
        }

        private int EstimateDrawCalls()
        {
            // Count unique materials
            var materials = new HashSet<Material>();
            var renderers = _rootObject.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat != null)
                    {
                        materials.Add(mat);
                    }
                }
            }
            return materials.Count;
        }

        private void PrintComparisonReport(RenderMetrics unoptimized, RenderMetrics optimized)
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n=== BENCHMARK COMPARISON REPORT ===");
            sb.AppendLine($"Component Count: {componentCount}");
            sb.AppendLine("");

            // Frame time improvements
            sb.AppendLine("FRAME TIME (lower is better):");
            sb.AppendLine($"  Average:  {unoptimized.AvgFrameTimeMs:F2}ms → {optimized.AvgFrameTimeMs:F2}ms  ({CalculateImprovement(unoptimized.AvgFrameTimeMs, optimized.AvgFrameTimeMs):F1}% faster)");
            sb.AppendLine($"  Min:      {unoptimized.MinFrameTimeMs:F2}ms → {optimized.MinFrameTimeMs:F2}ms  ({CalculateImprovement(unoptimized.MinFrameTimeMs, optimized.MinFrameTimeMs):F1}% faster)");
            sb.AppendLine($"  Max:      {unoptimized.MaxFrameTimeMs:F2}ms → {optimized.MaxFrameTimeMs:F2}ms  ({CalculateImprovement(unoptimized.MaxFrameTimeMs, optimized.MaxFrameTimeMs):F1}% faster)");
            sb.AppendLine($"  P50:      {unoptimized.P50Ms:F2}ms → {optimized.P50Ms:F2}ms  ({CalculateImprovement(unoptimized.P50Ms, optimized.P50Ms):F1}% faster)");
            sb.AppendLine($"  P95:      {unoptimized.P95Ms:F2}ms → {optimized.P95Ms:F2}ms  ({CalculateImprovement(unoptimized.P95Ms, optimized.P95Ms):F1}% faster)");
            sb.AppendLine($"  P99:      {unoptimized.P99Ms:F2}ms → {optimized.P99Ms:F2}ms  ({CalculateImprovement(unoptimized.P99Ms, optimized.P99Ms):F1}% faster)");
            sb.AppendLine("");

            // Draw call improvements
            sb.AppendLine("DRAW CALLS (lower is better):");
            sb.AppendLine($"  {unoptimized.DrawCalls} → {optimized.DrawCalls}  ({CalculateReduction(unoptimized.DrawCalls, optimized.DrawCalls):F1}% reduction)");
            sb.AppendLine("");

            // Triangle count
            sb.AppendLine("TRIANGLE COUNT:");
            sb.AppendLine($"  {unoptimized.TriangleCount} → {optimized.TriangleCount}  ({CalculateReduction(unoptimized.TriangleCount, optimized.TriangleCount):F1}% reduction)");
            sb.AppendLine("");

            // Memory impact
            sb.AppendLine("MEMORY ALLOCATION:");
            sb.AppendLine($"  GC Collections: {unoptimized.MemoryReport.TotalGCCollections} → {optimized.MemoryReport.TotalGCCollections}");
            sb.AppendLine($"  Peak Memory:    {unoptimized.MemoryReport.PeakMemoryMB:F2}MB → {optimized.MemoryReport.PeakMemoryMB:F2}MB");
            sb.AppendLine("");

            // FPS comparison
            float unoptimizedFPS = 1000f / unoptimized.AvgFrameTimeMs;
            float optimizedFPS = 1000f / optimized.AvgFrameTimeMs;
            sb.AppendLine($"FRAMERATE:");
            sb.AppendLine($"  {unoptimizedFPS:F1} FPS → {optimizedFPS:F1} FPS  (+{(optimizedFPS - unoptimizedFPS):F1} FPS)");
            sb.AppendLine("");

            // Overall verdict
            float overallImprovement = CalculateImprovement(unoptimized.AvgFrameTimeMs, optimized.AvgFrameTimeMs);
            if (overallImprovement > 80)
            {
                sb.AppendLine("VERDICT: ✅ EXCELLENT - >80% performance improvement");
            }
            else if (overallImprovement > 50)
            {
                sb.AppendLine("VERDICT: ✅ GOOD - 50-80% performance improvement");
            }
            else if (overallImprovement > 20)
            {
                sb.AppendLine("VERDICT: ⚠️ MODERATE - 20-50% performance improvement");
            }
            else
            {
                sb.AppendLine("VERDICT: ❌ MINIMAL - <20% performance improvement");
            }

            UnityEngine.Debug.Log(sb.ToString());
        }

        private float CalculateImprovement(float before, float after)
        {
            if (before == 0) return 0;
            return ((before - after) / before) * 100f;
        }

        private float CalculateReduction(int before, int after)
        {
            if (before == 0) return 0;
            return ((before - after) / (float)before) * 100f;
        }

        private class RenderMetrics
        {
            public string Label;
            public float AvgFrameTimeMs;
            public float MinFrameTimeMs;
            public float MaxFrameTimeMs;
            public float P50Ms;
            public float P95Ms;
            public float P99Ms;
            public int TriangleCount;
            public int DrawCalls;
            public MemoryReport MemoryReport;

            public override string ToString()
            {
                return $"[{Label}] Avg: {AvgFrameTimeMs:F2}ms, P95: {P95Ms:F2}ms, Triangles: {TriangleCount}, DrawCalls: {DrawCalls}";
            }
        }
    }
}
