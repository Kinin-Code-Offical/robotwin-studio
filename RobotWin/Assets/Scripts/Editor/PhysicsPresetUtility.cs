using UnityEditor;
using UnityEngine;
using RobotTwin.Game;
using RobotTwin.Debugging;

namespace RobotTwin.EditorTools
{
    public static class PhysicsPresetUtility
    {
        [MenuItem("RobotWin/Tools/Physics/Apply Selected Preset", true)]
        private static bool ValidateApplySelectedPreset()
        {
            return Selection.activeObject is PhysicsPreset;
        }

        [MenuItem("RobotWin/Tools/Physics/Apply Selected Preset")]
        private static void ApplySelectedPreset()
        {
            var preset = Selection.activeObject as PhysicsPreset;
            if (preset == null)
            {
                Debug.LogWarning("[Physics] Select a PhysicsPreset asset first.");
                return;
            }

            var world = NativePhysicsWorld.Instance;
            if (world == null)
            {
                Debug.LogWarning("[Physics] NativePhysicsWorld not running. Enter Play Mode.");
                return;
            }

            world.SetPreset(preset);
            Debug.Log($"[Physics] Applied preset '{preset.name}'.");
        }

        [MenuItem("RobotWin/Tools/Physics/Add Diagnostics Overlay")]
        private static void AddDiagnosticsOverlay()
        {
            var existing = Object.FindFirstObjectByType<PhysicsDiagnosticsOverlay>();
            if (existing != null)
            {
                Selection.activeObject = existing.gameObject;
                Debug.Log("[Physics] Diagnostics overlay already present.");
                return;
            }

            var go = new GameObject("PhysicsDiagnosticsOverlay");
            go.AddComponent<PhysicsDiagnosticsOverlay>();
            Undo.RegisterCreatedObjectUndo(go, "Add Physics Diagnostics Overlay");
            Selection.activeObject = go;
        }
    }
}
