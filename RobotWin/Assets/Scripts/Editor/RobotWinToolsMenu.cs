using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using RobotTwin.Game;
using RobotTwin.Debugging;
using RobotTwin.Tools;

namespace RobotTwin.EditorTools
{
    [InitializeOnLoad]
    public static class RobotWinToolsMenu
    {
        private const string FirmwareMonitorAutoLaunchMenu = "RobotWin/Tools/Monitor/Auto-Launch Firmware Monitor";

        static RobotWinToolsMenu()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            FirmwareMonitorLauncher.TryAutoLaunch();
        }
        [MenuItem("RobotWin/Tools/Physics/Rebuild Native Colliders (Selected)")]
        private static void RebuildNativeCollidersSelected()
        {
            NativeColliderGenerator.GenerateForSelection(Selection.gameObjects);
        }

        [MenuItem("RobotWin/Tools/Physics/Rebuild Native Colliders (Scene)")]
        private static void RebuildNativeCollidersScene()
        {
            NativeColliderGenerator.GenerateForScene();
        }

        [MenuItem("RobotWin/Tools/Physics/Validate Physics Bodies")]
        private static void ValidatePhysicsBodies()
        {
            var bodies = UnityEngine.Object.FindObjectsByType<NativePhysicsBody>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int missingCollider = 0;
            foreach (var body in bodies)
            {
                if (body == null) continue;
                if (body.GetComponent<Collider>() == null)
                {
                    missingCollider++;
                    UnityEngine.Debug.LogWarning($"[Physics] Missing collider on {body.name}", body);
                }
            }
            UnityEngine.Debug.Log($"[Physics] Validation complete. Bodies={bodies.Length}, missing collider={missingCollider}");
        }

        [MenuItem("RobotWin/Tools/Models/Normalize Selected Model")]
        private static void NormalizeSelectedModel()
        {
            foreach (var go in Selection.gameObjects)
            {
                if (go == null) continue;
                if (!TryGetLocalBounds(go.transform, out var bounds)) continue;
                float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (size <= 0f) continue;
                float scale = 1f / size;
                Undo.RecordObject(go.transform, "Normalize Model");
                go.transform.localScale *= scale;
                go.transform.localPosition -= bounds.center * scale;
            }
        }

        [MenuItem("RobotWin/Tools/Models/Center Pivot (Selected)")]
        private static void CenterPivotSelected()
        {
            foreach (var go in Selection.gameObjects)
            {
                if (go == null) continue;
                if (!TryGetLocalBounds(go.transform, out var bounds)) continue;

                var parent = go.transform.parent;
                var pivot = new GameObject($"{go.name}_Pivot");
                Undo.RegisterCreatedObjectUndo(pivot, "Center Pivot");
                pivot.transform.SetParent(parent, false);
                pivot.transform.position = go.transform.TransformPoint(bounds.center);
                pivot.transform.rotation = go.transform.rotation;
                pivot.transform.localScale = Vector3.one;

                Undo.SetTransformParent(go.transform, pivot.transform, "Center Pivot");
                go.transform.localPosition = -bounds.center;
            }
        }

        [MenuItem("RobotWin/Tools/Models/Generate LODs (Selected)")]
        private static void GenerateLodsSelected()
        {
            foreach (var go in Selection.gameObjects)
            {
                if (go == null) continue;
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                var lodGroup = go.GetComponent<LODGroup>();
                if (lodGroup == null)
                {
                    lodGroup = Undo.AddComponent<LODGroup>(go);
                }
                var lods = new[]
                {
                    new LOD(0.6f, renderers),
                    new LOD(0.2f, renderers)
                };
                lodGroup.SetLODs(lods);
                lodGroup.RecalculateBounds();
            }
        }

        [MenuItem("RobotWin/Tools/Debug/Create RemoteCommandServer")]
        private static void CreateRemoteCommandServer()
        {
            var existing = UnityEngine.Object.FindFirstObjectByType<RemoteCommandServer>();
            if (existing != null)
            {
                UnityEngine.Debug.Log("[RobotWin] RemoteCommandServer already exists.");
                Selection.activeObject = existing.gameObject;
                return;
            }
            var go = new GameObject("RemoteCommandServer");
            Undo.RegisterCreatedObjectUndo(go, "Create RemoteCommandServer");
            go.AddComponent<RemoteCommandServer>();
            Selection.activeObject = go;
        }

        [MenuItem("RobotWin/Tools/Debug/Open Debug Console")]
        private static void OpenDebugConsole()
        {
            RunRtTool("debug-console");
            Process.Start(new ProcessStartInfo("http://localhost:8090") { UseShellExecute = true });
        }

        [MenuItem("RobotWin/Tools/Debug/Close Debug Console")]
        private static void CloseDebugConsole()
        {
            RunRtTool("debug-console-stop");
        }

        [MenuItem("RobotWin/Tools/Debug/Tail Unity Log")]
        private static void TailUnityLog()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.Parent?.FullName
                                 ?? Directory.GetParent(Application.dataPath).FullName;
            string logPath = Path.Combine(projectRoot, "logs", "unity", "unity_live_error.log");
            if (!File.Exists(logPath))
            {
                UnityEngine.Debug.LogWarning($"[RobotWin] Unity log not found: {logPath}");
                return;
            }
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{logPath}\"") { UseShellExecute = true });
        }

        [MenuItem("RobotWin/Tools/Build/Build Native")]
        private static void BuildNative() => RunRtTool("build-native");

        [MenuItem("RobotWin/Tools/Build/Build Firmware")]
        private static void BuildFirmware() => RunRtTool("build-firmware");

        [MenuItem("RobotWin/Tools/Monitor/Build Firmware Monitor")]
        private static void BuildFirmwareMonitor() => RunRtTool("build-firmware-monitor");

        [MenuItem("RobotWin/Tools/Monitor/Launch Firmware Monitor")]
        private static void LaunchFirmwareMonitor()
        {
            if (!TryLaunchFirmwareMonitor())
            {
                UnityEngine.Debug.LogWarning("[RobotWin] Firmware Monitor not found. Run Build Firmware Monitor first.");
            }
        }

        [MenuItem(FirmwareMonitorAutoLaunchMenu)]
        private static void ToggleFirmwareMonitorAutoLaunch()
        {
            bool enabled = !FirmwareMonitorLauncher.AutoLaunchEnabled;
            FirmwareMonitorLauncher.AutoLaunchEnabled = enabled;
            Menu.SetChecked(FirmwareMonitorAutoLaunchMenu, enabled);
        }

        [MenuItem(FirmwareMonitorAutoLaunchMenu, true)]
        private static bool ToggleFirmwareMonitorAutoLaunchValidate()
        {
            Menu.SetChecked(FirmwareMonitorAutoLaunchMenu, FirmwareMonitorLauncher.AutoLaunchEnabled);
            return true;
        }

        [MenuItem("RobotWin/Tools/Build/Update Unity Plugins")]
        private static void UpdateUnityPlugins() => RunRtTool("update-unity-plugins");

        [MenuItem("RobotWin/Tools/QA/Run Unity Smoke Test")]
        private static void RunUnitySmoke() => RunRtTool("run-unity-smoke");

        [MenuItem("RobotWin/Tools/QA/Run QA (Jest)")]
        private static void RunQa() => RunRtTool("run-qa");

        [MenuItem("RobotWin/Tools/QA/Validate UXML")]
        private static void ValidateUxml() => RunRtTool("validate-uxml");

        private static void RunRtTool(string command)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.Parent?.FullName
                                 ?? Directory.GetParent(Application.dataPath).FullName;
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -Command \"python .\\tools\\rt_tool.py {command}\"",
                WorkingDirectory = projectRoot,
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        private static bool TryLaunchFirmwareMonitor()
        {
            return FirmwareMonitorLauncher.LaunchIfNeeded();
        }

        private static bool TryGetLocalBounds(Transform root, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            var referenceMatrix = root.worldToLocalMatrix;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var matrix = referenceMatrix * renderer.transform.localToWorldMatrix;
                var transformed = TransformBounds(renderer.localBounds, matrix);
                if (!hasBounds)
                {
                    bounds = transformed;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(transformed);
                }
            }
            return hasBounds;
        }

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            var center = matrix.MultiplyPoint3x4(bounds.center);
            var extents = bounds.extents;
            var axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            var axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            var axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
            extents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, extents * 2f);
        }
    }
}
