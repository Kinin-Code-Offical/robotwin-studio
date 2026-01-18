using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using RobotTwin.Timing;
using System;

namespace RobotTwin.Tests.PlayMode
{
    /// <summary>
    /// Play Mode tests for timing system - requires actual Unity runtime
    /// Tests sensor synchronization, physics lockstep in real execution
    /// </summary>
    [TestFixture]
    public class TimingSyncPlayModeTests
    {
        private GameObject _testObject;
        private GlobalLatencyManager _latencyManager;
        private SensorSyncController _sensorController;
        private PhysicsLockstepController _physicsController;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _testObject = new GameObject("TimingPlayModeTest");
            _latencyManager = _testObject.AddComponent<GlobalLatencyManager>();
            _sensorController = _testObject.AddComponent<SensorSyncController>();
            _physicsController = _testObject.AddComponent<PhysicsLockstepController>();

            yield return null; // Wait one frame for initialization
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_testObject != null)
            {
                GameObject.Destroy(_testObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator LockstepExecution_MaintainsSynchronization()
        {
            // Arrange
            _latencyManager.ResetAllClocks();

            // Act - Run for 10 frames
            for (int i = 0; i < 10; i++)
            {
                _latencyManager.AdvanceMasterClock(Time.fixedDeltaTime);
                _latencyManager.ExecuteLockstepUpdate();
                yield return new WaitForFixedUpdate();
            }

            // Assert
            string driftReport;
            bool synced = _latencyManager.AreSubsystemsSynchronized(out driftReport);
            Assert.IsTrue(synced, $"After 10 frames, systems should be synchronized: {driftReport}");
        }

        [UnityTest]
        public IEnumerator PhysicsLockstep_StepsAtFixedRate()
        {
            // Arrange
            long initialSteps = _physicsController.Metrics.StepCount;

            // Act - Run for 5 fixed updates
            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate();
                _latencyManager.AdvanceMasterClock(Time.fixedDeltaTime);
                _latencyManager.ExecuteLockstepUpdate();
            }

            // Assert
            long finalSteps = _physicsController.Metrics.StepCount;
            Assert.Greater(finalSteps, initialSteps, "Physics should have stepped");
        }

        [UnityTest]
        public IEnumerator SensorSync_UpdatesAtCorrectRate()
        {
            // Arrange
            var testSensor = _testObject.AddComponent<TestLineSensor>();
            _sensorController.RegisterSensor(testSensor);

            long initialUpdates = _sensorController.Metrics.LineSensorUpdates;

            // Act - Run for 100ms (should trigger ~100 updates @ 1kHz)
            float elapsed = 0;
            while (elapsed < 0.1f)
            {
                _latencyManager.AdvanceMasterClock(Time.fixedDeltaTime);
                _latencyManager.ExecuteLockstepUpdate();
                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }

            // Assert
            long finalUpdates = _sensorController.Metrics.LineSensorUpdates;
            long updateCount = finalUpdates - initialUpdates;
            Assert.Greater(updateCount, 50, "Line sensor should update at ~1kHz");
        }

        [UnityTest]
        public IEnumerator DriftCorrection_AutomaticallyApplies()
        {
            // Arrange - Create artificial drift
            _latencyManager.UpdateCircuitLatency(0.0, 0);
            _latencyManager.AdvanceMasterClock(0.002); // 2ms drift

            yield return null;

            // Act - Let system run for a few frames (drift correction should apply)
            for (int i = 0; i < 5; i++)
            {
                _latencyManager.ExecuteLockstepUpdate();
                yield return new WaitForFixedUpdate();
            }

            // Assert
            Assert.Greater(_latencyManager.Metrics.TotalDriftCorrections, 0,
                "System should have applied drift corrections");
        }

        [UnityTest]
        public IEnumerator CircuitLatency_IntegratesWithPhysics()
        {
            // Arrange
            var circuitAdapter = _testObject.AddComponent<CircuitLatencyAdapter>();
            circuitAdapter.TriggerLatencyCapture();

            yield return null;

            // Act - Circuit latency should propagate to physics
            double circuitLatency = _latencyManager.GetSubsystemLatencySeconds("Circuit");
            double physicsLatency = _latencyManager.GetSubsystemLatencySeconds("Physics");

            // Assert
            Assert.AreEqual(circuitLatency, physicsLatency, 0.001,
                "Circuit and physics latency should match");
        }

        // Test sensor implementation
        private class TestLineSensor : MonoBehaviour, ISynchronizedSensor
        {
            public int UpdateCount = 0;

            public void SynchronizedUpdate(double timeSeconds)
            {
                UpdateCount++;
            }

            public SensorType GetSensorType() => SensorType.LineSensor;
            public string GetSensorName() => "TestLineSensor";
            public bool IsEnabled() => enabled;
        }
    }
}
