using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

namespace RobotTwin.Editor
{
    [InitializeOnLoad]
    public class TestBootstrapper
    {
        private static string TriggerPath;

        static TestBootstrapper()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            TriggerPath = Path.Combine(projectRoot, "Temp", "StartTest.trigger");

            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // Poll for trigger file
            if (File.Exists(TriggerPath))
            {
                try
                {
                    File.Delete(TriggerPath);
                    Debug.Log("[TestBootstrapper] Trigger detected! Loading Wizard scene and starting Play Mode...");
                    EditorSceneManager.OpenScene("Assets/Scenes/Wizard.unity");
                    
                    var go = new GameObject("AutoPilot");
                    go.AddComponent<RobotTwin.Debugging.AutoPilot>();
                    
                    EditorApplication.isPlaying = true;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[TestBootstrapper] Failed to process trigger: {e.Message}");
                }
            }
        }
    }
}
