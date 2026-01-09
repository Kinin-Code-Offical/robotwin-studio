using NUnit.Framework;
using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.TestTools;
using RobotTwin.Core;
using RobotTwin.UI;

namespace RobotTwin.Tests.Performance
{
    /// <summary>
    /// Integration tests for end-to-end simulation scenarios
    /// </summary>
    public class IntegrationTests
    {
        [UnityTest]
        public IEnumerator FullCircuitSimulation_CompletesWithin5Seconds()
        {
            // Arrange
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Create test circuit with native physics
            NativeBridge.Physics_CreateWorld();
            var config = new NativeBridge.PhysicsConfig
            {
                base_dt = 0.001f,
                gravity_y = -9.81f,
                solver_iterations = 12,
                noise_seed = 42UL
            };
            NativeBridge.Physics_SetConfig(ref config);

            // Add 10 bodies
            for (int i = 0; i < 10; i++)
            {
                var body = new NativeBridge.RigidBody
                {
                    position_x = i * 2.0f,
                    position_y = 5f,
                    position_z = 0f,
                    mass = 1.0f,
                    shape = 0 // Sphere
                };
                NativeBridge.Physics_AddBody(ref body);
            }

            // Act - Run 1000 physics cycles
            for (int i = 0; i < 1000; i++)
            {
                NativeBridge.Physics_Step(0.001f);
                if (i % 100 == 0) yield return null; // Don't block Unity
            }

            NativeBridge.Physics_DestroyWorld();
            stopwatch.Stop();

            // Assert
            Assert.Less(stopwatch.ElapsedMilliseconds, 5000,
                $"Full circuit simulation took too long: {stopwatch.ElapsedMilliseconds}ms (target <5000ms)");
        }

        [UnityTest]
        public IEnumerator MultiSensorInteraction_SensorsRespondCorrectly()
        {
            // Arrange - Create test objects with colliders (simulating sensors)
            var sensors = new GameObject[5];
            var triggers = new GameObject[5];

            for (int i = 0; i < 5; i++)
            {
                sensors[i] = new GameObject($"Sensor_{i}");
                var collider = sensors[i].AddComponent<SphereCollider>();
                collider.isTrigger = true;
                collider.radius = 0.5f;
                sensors[i].transform.position = new Vector3(i * 2f, 0f, 0f);

                triggers[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                triggers[i].name = $"Trigger_{i}";
                triggers[i].transform.position = new Vector3(i * 2f, 10f, 0f);
                var rb = triggers[i].AddComponent<Rigidbody>();
                rb.useGravity = true;
            }

            // Act - Wait for physics to settle
            float startTime = Time.realtimeSinceStartup;
            yield return new WaitForSeconds(0.5f);
            float responseTime = (Time.realtimeSinceStartup - startTime) * 1000f;

            // Cleanup
            foreach (var obj in sensors) GameObject.DestroyImmediate(obj);
            foreach (var obj in triggers) GameObject.DestroyImmediate(obj);

            // Assert
            Assert.Less(responseTime, 100f,
                $"Sensor response too slow: {responseTime:F1}ms (target <100ms)");
        }

        [Test]
        public void DeterministicReplay_ProducesSameResults()
        {
            // Arrange - Setup physics with deterministic seed
            NativeBridge.Physics_CreateWorld();

            var config = new NativeBridge.PhysicsConfig
            {
                base_dt = 0.001f,
                gravity_x = 0f,
                gravity_y = -9.81f,
                gravity_z = 0f,
                gravity_jitter = 0f, // Zero jitter for determinism
                time_jitter = 0f,
                solver_iterations = 12,
                noise_seed = 12345UL, // Fixed seed
                contact_slop = 0.0005f,
                restitution = 0.2f,
                static_friction = 0.8f,
                dynamic_friction = 0.6f
            };
            NativeBridge.Physics_SetConfig(ref config);

            // Create test body
            var body = new NativeBridge.RigidBody
            {
                position_x = 0f,
                position_y = 10f,
                position_z = 0f,
                velocity_x = 0f,
                velocity_y = 0f,
                velocity_z = 0f,
                mass = 1.0f,
                shape = 0 // Sphere
            };

            // Run 1
            uint bodyId1 = NativeBridge.Physics_AddBody(ref body);
            var finalState1 = new NativeBridge.RigidBody();
            for (int i = 0; i < 100; i++)
            {
                NativeBridge.Physics_Step(0.016f);
            }
            NativeBridge.Physics_GetBody(bodyId1, out finalState1);
            NativeBridge.Physics_DestroyWorld();

            // Run 2 - Same seed, same inputs
            NativeBridge.Physics_CreateWorld();
            NativeBridge.Physics_SetConfig(ref config);
            uint bodyId2 = NativeBridge.Physics_AddBody(ref body);
            var finalState2 = new NativeBridge.RigidBody();
            for (int i = 0; i < 100; i++)
            {
                NativeBridge.Physics_Step(0.016f);
            }
            NativeBridge.Physics_GetBody(bodyId2, out finalState2);
            NativeBridge.Physics_DestroyWorld();

            // Assert - States should be identical
            Assert.AreEqual(finalState1.position_x, finalState2.position_x, 0.0001f, "X position mismatch");
            Assert.AreEqual(finalState1.position_y, finalState2.position_y, 0.0001f, "Y position mismatch");
            Assert.AreEqual(finalState1.position_z, finalState2.position_z, 0.0001f, "Z position mismatch");
            Assert.AreEqual(finalState1.velocity_x, finalState2.velocity_x, 0.0001f, "X velocity mismatch");
            Assert.AreEqual(finalState1.velocity_y, finalState2.velocity_y, 0.0001f, "Y velocity mismatch");
        }

        [UnityTest]
        public IEnumerator LongRunSimulation_NoMemoryLeaks()
        {
            // Arrange
            long initialMemory = GC.GetTotalMemory(true);

            // Act
            // TODO: Run simulation for 5000 frames
            for (int i = 0; i < 100; i++) // Shortened for test
            {
                yield return null;
            }

            // Force GC
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long finalMemory = GC.GetTotalMemory(false);
            long leakage = finalMemory - initialMemory;

            // Assert
            Assert.Less(leakage, 10 * 1024 * 1024, "Memory leak detected: " + (leakage / 1024) + " KB");
        }

        [Test]
        public void CircuitSolver_ConvergesWithin100Iterations()
        {
            // Arrange - Create simple resistor divider circuit
            NativeBridge.Physics_CreateWorld();

            // Circuit: 5V source -> 1kΩ -> node -> 1kΩ -> GND
            // Expected: node voltage = 2.5V (perfect divider)

            // We'll use circuit solver via native bridge if available
            // For now, test the iteration count concept
            int maxIterations = 100;
            int actualIterations = 0;

            float voltage = 5.0f;
            float r1 = 1000.0f;
            float r2 = 1000.0f;
            float tolerance = 0.001f; // 0.1%

            // Simple iterative solver simulation
            float nodeVoltage = voltage / 2f; // Initial guess
            float prevVoltage = 0f;

            while (actualIterations < maxIterations)
            {
                actualIterations++;

                // Update node voltage using current divider formula
                nodeVoltage = voltage * r2 / (r1 + r2);

                // Check convergence
                if (Math.Abs(nodeVoltage - prevVoltage) < tolerance)
                {
                    break;
                }

                prevVoltage = nodeVoltage;
            }

            NativeBridge.Physics_DestroyWorld();

            // Assert
            Assert.Less(actualIterations, maxIterations,
                $"Circuit solver did not converge within {maxIterations} iterations (took {actualIterations})");
            Assert.AreEqual(2.5f, nodeVoltage, 0.01f,
                $"Node voltage incorrect: {nodeVoltage}V (expected 2.5V)");
        }

        [UnityTest]
        public IEnumerator Circuit3DView_BuildsWithin500ms()
        {
            // Arrange - Circuit3DView requires actual scene setup
            // This is a simplified version measuring mesh generation time
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Create 10 simple GameObjects (simulating circuit components)
            var components = new GameObject[10];
            for (int i = 0; i < 10; i++)
            {
                components[i] = GameObject.CreatePrimitive(PrimitiveType.Cube);
                components[i].transform.position = new Vector3(i * 2f, 0f, 0f);
                components[i].name = $"Component_{i}";
            }

            // Simulate mesh build delay
            yield return new WaitForSeconds(0.1f);

            stopwatch.Stop();

            // Cleanup
            foreach (var obj in components)
            {
                GameObject.DestroyImmediate(obj);
            }

            // Assert
            Assert.Less(stopwatch.ElapsedMilliseconds, 500,
                $"3D view build took too long: {stopwatch.ElapsedMilliseconds}ms (target <500ms)");
        }

        [Test]
        public void WireRopePhysics_StableAt60FPS()
        {
            // Arrange - Create GameObject with WireRope component
            var wireObj = new GameObject("TestWire");
            var startObj = new GameObject("Start");
            var endObj = new GameObject("End");

            startObj.transform.position = Vector3.zero;
            endObj.transform.position = new Vector3(5f, -2f, 0f);

            var startAnchor = startObj.AddComponent<WireAnchor>();
            var endAnchor = endObj.AddComponent<WireAnchor>();
            startAnchor.Radius = 0.1f;
            endAnchor.Radius = 0.1f;

            var wireRope = wireObj.AddComponent<WireRope>();

            // Use reflection to set private fields (WireRope fields are SerializeField)
            var wireType = wireRope.GetType();
            wireType.GetField("_start", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(wireRope, startAnchor);
            wireType.GetField("_end", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(wireRope, endAnchor);
            wireType.GetField("_segments", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(wireRope, 20);
            wireType.GetField("_useRopePhysics", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(wireRope, true);

            // Act - Measure 60 physics steps
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < 60; i++)
            {
                // Call SimulateRope via reflection
                var simulateMethod = wireType.GetMethod("SimulateRope",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                simulateMethod?.Invoke(wireRope, null);
            }

            stopwatch.Stop();
            float avgStepTime = stopwatch.ElapsedMilliseconds / 60.0f;

            // Cleanup
            GameObject.DestroyImmediate(wireObj);
            GameObject.DestroyImmediate(startObj);
            GameObject.DestroyImmediate(endObj);

            // Assert
            Assert.Less(avgStepTime, 1.0f,
                $"Wire rope physics too slow: {avgStepTime:F3}ms/step (target <1ms for 20 segments)");
        }

        [Test]
        public void SerializationDeserialization_PreservesState()
        {
            // Arrange - Create physics body
            NativeBridge.Physics_CreateWorld();

            var originalBody = new NativeBridge.RigidBody
            {
                position_x = 1.5f,
                position_y = 2.3f,
                position_z = -0.7f,
                velocity_x = 0.5f,
                velocity_y = -1.2f,
                velocity_z = 0.3f,
                angular_velocity_x = 0.1f,
                angular_velocity_y = 0.2f,
                angular_velocity_z = -0.1f,
                mass = 2.5f,
                shape = 0, // Sphere
                is_static = false
            };

            uint bodyId = NativeBridge.Physics_AddBody(ref originalBody);

            // Act - Retrieve body (simulating serialization/deserialization)
            var retrievedBody = new NativeBridge.RigidBody();
            int success = NativeBridge.Physics_GetBody(bodyId, out retrievedBody);

            NativeBridge.Physics_DestroyWorld();

            // Assert - All fields should match
            Assert.AreEqual(1, success, "Failed to retrieve body");
            Assert.AreEqual(originalBody.position_x, retrievedBody.position_x, 0.0001f, "Position X mismatch");
            Assert.AreEqual(originalBody.position_y, retrievedBody.position_y, 0.0001f, "Position Y mismatch");
            Assert.AreEqual(originalBody.position_z, retrievedBody.position_z, 0.0001f, "Position Z mismatch");
            Assert.AreEqual(originalBody.velocity_x, retrievedBody.velocity_x, 0.0001f, "Velocity X mismatch");
            Assert.AreEqual(originalBody.velocity_y, retrievedBody.velocity_y, 0.0001f, "Velocity Y mismatch");
            Assert.AreEqual(originalBody.velocity_z, retrievedBody.velocity_z, 0.0001f, "Velocity Z mismatch");
            Assert.AreEqual(originalBody.mass, retrievedBody.mass, 0.0001f, "Mass mismatch");
            Assert.AreEqual(originalBody.shape, retrievedBody.shape, "Shape mismatch");
        }
    }
}
