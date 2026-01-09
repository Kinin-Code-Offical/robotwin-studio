// RobotWin Studio - Render Performance Integration Test
// Validates GPU instancing, static batching, and draw call reduction

using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RobotTwin.Performance;

namespace RobotTwin.Tests
{
    [TestFixture]
    public class RenderPerformanceTests
    {
        /// <summary>
        /// Test GPU instancing reduces draw calls for 100 identical objects.
        /// Target: >90% reduction (100 → <10 draw calls).
        /// </summary>
        [Test]
        public void GPUInstancing_IdenticalObjects_ReducesDrawCalls()
        {
            // Create scene with 100 identical cubes
            var root = new GameObject("RenderTestRoot");
            var material = new Material(Shader.Find("Standard"));

            for (int i = 0; i < 100; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform);
                cube.transform.position = new Vector3(i % 10, 0, i / 10);
                cube.GetComponent<Renderer>().sharedMaterial = material;
            }

            // Count before optimization
            int beforeDrawCalls = CountDrawCalls(root);

            // Apply GPU instancing
            int instancedCount = RenderOptimizer.EnableGPUInstancing(root);

            // Count after optimization
            int afterDrawCalls = CountDrawCalls(root);

            // Validate
            Assert.AreEqual(100, instancedCount, "All 100 objects should be instanced");
            Assert.Less(afterDrawCalls, beforeDrawCalls * 0.1f, "Draw calls should reduce by >90%");

            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Test static batching combines meshes with same material.
        /// Target: <5 draw calls for 50 static objects.
        /// </summary>
        [Test]
        public void StaticBatching_StaticObjects_CombinesMeshes()
        {
            // Create scene with 50 static cubes
            var root = new GameObject("RenderTestRoot");
            var material = new Material(Shader.Find("Standard"));

            for (int i = 0; i < 50; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform);
                cube.transform.position = new Vector3(i % 10, 0, i / 10);
                cube.isStatic = true; // CRITICAL: Mark as static
                cube.GetComponent<Renderer>().sharedMaterial = material;
            }

            // Apply static batching
            int drawCallsReduced = RenderOptimizer.ApplyStaticBatching(root);

            // Count final draw calls
            int finalDrawCalls = CountDrawCalls(root);

            // Validate
            Assert.Greater(drawCallsReduced, 0, "Should reduce at least 1 draw call");
            Assert.Less(finalDrawCalls, 5, "Final draw calls should be <5 for 50 static objects");

            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Test full scene optimization pipeline.
        /// Target: >85% draw call reduction for complex scene.
        /// </summary>
        [Test]
        public void FullOptimization_ComplexScene_ReducesDrawCalls()
        {
            // Create complex scene (100 objects, 5 materials)
            var root = new GameObject("RenderTestRoot");
            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);

            Material[] materials = new Material[5];
            for (int i = 0; i < 5; i++)
                materials[i] = new Material(Shader.Find("Standard"));

            for (int i = 0; i < 100; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform);
                cube.transform.position = new Vector3(i % 10, 0, i / 10);
                cube.isStatic = (i % 2 == 0); // 50% static
                cube.GetComponent<Renderer>().sharedMaterial = materials[i % 5];
            }

            // Apply full optimization
            var result = RenderOptimizer.OptimizeScene(root, camera);

            // Validate
            Assert.Greater(result.InitialDrawCalls, 0, "Should have initial draw calls");
            Assert.Less(result.FinalDrawCalls, result.InitialDrawCalls, "Should reduce draw calls");
            Assert.GreaterOrEqual(result.ReductionPercent, 85f, "Should reduce >85% of draw calls");
            Assert.Greater(result.InstancedRenderers, 0, "Should instance some renderers");

            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Test render performance with stopwatch.
        /// Target: <2ms for 1000 components with optimization.
        /// </summary>
        [Test]
        public void RenderPerformance_1000Components_Under2ms()
        {
            // Create massive scene (1000 objects)
            var root = new GameObject("RenderTestRoot");
            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.orthographic = true;
            camera.orthographicSize = 50;

            var material = new Material(Shader.Find("Standard"));

            for (int i = 0; i < 1000; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform);
                cube.transform.position = new Vector3(i % 32, 0, i / 32);
                cube.isStatic = true;
                cube.GetComponent<Renderer>().sharedMaterial = material;
            }

            // Optimize
            RenderOptimizer.OptimizeScene(root, camera);

            // Benchmark render (10 iterations for average)
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++)
            {
                camera.Render();
            }
            stopwatch.Stop();

            double avgMs = stopwatch.Elapsed.TotalMilliseconds / 10.0;

            // Validate
            Assert.Less(avgMs, 2.0, $"Average render time should be <2ms (actual: {avgMs:F3}ms)");

            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Test material instancing creates unique instances per property.
        /// Target: 3 instanced materials for 3 different colors.
        /// </summary>
        [Test]
        public void MaterialInstancing_DifferentColors_CreatesInstances()
        {
            // Clear cache
            RenderOptimizer.ClearCaches();

            // Create scene with 3 different colored materials
            var root = new GameObject("RenderTestRoot");
            Color[] colors = { Color.red, Color.green, Color.blue };

            for (int i = 0; i < 3; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform);
                cube.transform.position = new Vector3(i, 0, 0);
                var material = new Material(Shader.Find("Standard"));
                material.color = colors[i];
                cube.GetComponent<Renderer>().sharedMaterial = material;
            }

            // Apply instancing
            RenderOptimizer.EnableGPUInstancing(root);

            // Validate
            var stats = RenderOptimizer.GetStatistics();
            Assert.AreEqual(3, stats.TotalInstancedMaterials, "Should create 3 instanced materials (one per color)");

            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Helper: Count draw calls for hierarchy (estimate from materials).
        /// </summary>
        private int CountDrawCalls(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            return renderers.Sum(r => r.sharedMaterials.Length);
        }
    }
}
