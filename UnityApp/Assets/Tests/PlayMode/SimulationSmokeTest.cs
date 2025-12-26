using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.IO;
using RobotTwin.CoreSim.Specs;
using RobotTwin.Game; 
using RobotTwin.UI;

namespace RobotTwin.Tests.PlayMode
{
    public class SimulationSmokeTest
    {
        [UnityTest]
        public IEnumerator RunMode_GeneratesTelemetry_In100Ticks()
        {
            // Setup minimal Session
            var circuit = new CircuitSpec { Id = "SmokeTestCircuit", Mode = RobotTwin.CoreSim.Specs.SimulationMode.Fast };
            SessionManager.Instance.StartSession(circuit);

            // Load RunMode Scene (or just create controller if isolated)
            // Ideally we load the scene, but for smoke test we can emulate 
            UnityEngine.SceneManagement.SceneManager.LoadScene("RunMode");
            
            yield return null; // Wait for load

            var controller = GameObject.FindObjectOfType<RunModeController>();
            Assert.IsNotNull(controller, "RunModeController not found in scene");

            // Wait 100 frames (approx 2 seconds at 50fps fixed)
            // We use unscaled time limit or frame count
            float startTime = Time.time;
            while (Time.time - startTime < 3.0f)
            {
                yield return null;
            }

            // Verify File
            // We need to find the latest run folder
            var runsDir = Path.Combine(Application.persistentDataPath, "Runs");
            Assert.IsTrue(Directory.Exists(runsDir), "Runs directory not created");

            var dirs = Directory.GetDirectories(runsDir);
            Assert.IsNotEmpty(dirs, "No run directory created");
            
            // Get latest
            var latest = dirs[dirs.Length - 1]; // Simply last one (time sorted usually)
            
            var framesFile = Path.Combine(latest, "frames.jsonl");
            Assert.IsTrue(File.Exists(framesFile), $"frames.jsonl NOT found at {framesFile}");

            var lines = File.ReadAllLines(framesFile);
            Assert.Greater(lines.Length, 50, "Not enough telemetry frames recorded");
        }
    }
}
