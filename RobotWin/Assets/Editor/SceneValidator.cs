using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.Tests
{
    /// <summary>
    /// Scene Validator - Loads and validates all scenes for errors/warnings
    /// Checks: Missing references, null components, naming conventions, performance issues
    /// </summary>
    public class SceneValidator : EditorWindow
    {
        private Vector2 _scrollPosition;
        private List<ValidationResult> _results = new List<ValidationResult>();
        private bool _isValidating = false;

        [MenuItem("RobotWin/Validate All Scenes")]
        public static void ShowWindow()
        {
            GetWindow<SceneValidator>("Scene Validator");
        }

        private void OnGUI()
        {
            GUILayout.Label("Scene Validator", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "Validates all scenes for:\n" +
                "• Missing component references\n" +
                "• Null references\n" +
                "• Naming convention issues\n" +
                "• Performance warnings\n" +
                "• Lighting issues",
                MessageType.Info);

            EditorGUILayout.Space();

            if (!_isValidating)
            {
                if (GUILayout.Button("Validate All Scenes", GUILayout.Height(40)))
                {
                    ValidateAllScenes();
                }

                if (GUILayout.Button("Validate Current Scene", GUILayout.Height(30)))
                {
                    ValidateCurrentScene();
                }
            }
            else
            {
                GUILayout.Label("Validating...", EditorStyles.boldLabel);
            }

            EditorGUILayout.Space();
            GUILayout.Label($"Validation Results ({_results.Count} issues found):", EditorStyles.boldLabel);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(400));

            foreach (var result in _results)
            {
                Color originalColor = GUI.backgroundColor;

                switch (result.Severity)
                {
                    case Severity.Error:
                        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                        break;
                    case Severity.Warning:
                        GUI.backgroundColor = new Color(1f, 1f, 0.5f);
                        break;
                    case Severity.Info:
                        GUI.backgroundColor = new Color(0.7f, 0.7f, 1f);
                        break;
                }

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"[{result.Severity}] {result.SceneName}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Object: {result.ObjectName}");
                EditorGUILayout.LabelField($"Issue: {result.Message}");

                if (GUILayout.Button("Select in Scene", GUILayout.Width(120)))
                {
                    if (result.GameObject != null)
                    {
                        Selection.activeGameObject = result.GameObject;
                        EditorGUIUtility.PingObject(result.GameObject);
                    }
                }

                EditorGUILayout.EndVertical();
                GUI.backgroundColor = originalColor;
                EditorGUILayout.Space();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();

            if (GUILayout.Button("Clear Results"))
            {
                _results.Clear();
            }

            if (GUILayout.Button("Export Results to Console"))
            {
                ExportResultsToConsole();
            }
        }

        private void ValidateAllScenes()
        {
            _isValidating = true;
            _results.Clear();

            // Get all scenes in build settings
            string[] scenePaths = new string[]
            {
                "Assets/Scenes/Main.unity",
                "Assets/Scenes/RobotStudio.unity",
                "Assets/Scenes/WorldEditor.unity",
                "Assets/Scenes/ComponentStudio.unity",
                "Assets/Scenes/RunMode.unity",
                "Assets/Scenes/Wizard.unity"
            };

            string originalScene = SceneManager.GetActiveScene().path;

            foreach (string scenePath in scenePaths)
            {
                if (!System.IO.File.Exists(scenePath))
                {
                    _results.Add(new ValidationResult
                    {
                        SceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath),
                        ObjectName = "N/A",
                        Message = "Scene file not found",
                        Severity = Severity.Error
                    });
                    continue;
                }

                Debug.Log($"Validating scene: {scenePath}");

                try
                {
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    ValidateCurrentScene();
                }
                catch (System.Exception e)
                {
                    _results.Add(new ValidationResult
                    {
                        SceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath),
                        ObjectName = "N/A",
                        Message = $"Failed to load scene: {e.Message}",
                        Severity = Severity.Error
                    });
                }
            }

            // Restore original scene
            if (!string.IsNullOrEmpty(originalScene))
            {
                EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);
            }

            _isValidating = false;
            Repaint();

            Debug.Log($"Scene validation complete: {_results.Count} issues found");
        }

        private void ValidateCurrentScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            string sceneName = activeScene.name;

            // Get all GameObjects in scene
            GameObject[] allObjects = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

            foreach (GameObject obj in allObjects)
            {
                // Check for missing components
                Component[] components = obj.GetComponents<Component>();
                foreach (Component comp in components)
                {
                    if (comp == null)
                    {
                        _results.Add(new ValidationResult
                        {
                            SceneName = sceneName,
                            ObjectName = obj.name,
                            GameObject = obj,
                            Message = "Missing component reference",
                            Severity = Severity.Error
                        });
                    }
                }

                // Check for empty/unnamed objects
                if (string.IsNullOrEmpty(obj.name) || obj.name == "GameObject")
                {
                    _results.Add(new ValidationResult
                    {
                        SceneName = sceneName,
                        ObjectName = obj.name,
                        GameObject = obj,
                        Message = "Unnamed or default-named GameObject",
                        Severity = Severity.Warning
                    });
                }

                // Check for disabled colliders on sensors
                if (obj.name.Contains("Sensor"))
                {
                    Collider collider = obj.GetComponent<Collider>();
                    if (collider == null)
                    {
                        _results.Add(new ValidationResult
                        {
                            SceneName = sceneName,
                            ObjectName = obj.name,
                            GameObject = obj,
                            Message = "Sensor object missing collider",
                            Severity = Severity.Warning
                        });
                    }
                    else if (!collider.enabled)
                    {
                        _results.Add(new ValidationResult
                        {
                            SceneName = sceneName,
                            ObjectName = obj.name,
                            GameObject = obj,
                            Message = "Sensor collider is disabled",
                            Severity = Severity.Warning
                        });
                    }
                }

                // Check for missing Rigidbody on physics objects
                if (obj.GetComponent<Collider>() != null && obj.GetComponent<Rigidbody>() == null)
                {
                    if (!obj.isStatic)
                    {
                        _results.Add(new ValidationResult
                        {
                            SceneName = sceneName,
                            ObjectName = obj.name,
                            GameObject = obj,
                            Message = "Dynamic collider without Rigidbody (may cause performance issues)",
                            Severity = Severity.Info
                        });
                    }
                }

                // Check for extremely large objects (performance)
                if (obj.transform.localScale.magnitude > 1000f)
                {
                    _results.Add(new ValidationResult
                    {
                        SceneName = sceneName,
                        ObjectName = obj.name,
                        GameObject = obj,
                        Message = $"Extremely large scale: {obj.transform.localScale.magnitude:F1}",
                        Severity = Severity.Warning
                    });
                }

                // Check for far objects (culling issues)
                if (obj.transform.position.magnitude > 10000f)
                {
                    _results.Add(new ValidationResult
                    {
                        SceneName = sceneName,
                        ObjectName = obj.name,
                        GameObject = obj,
                        Message = $"Object very far from origin: {obj.transform.position.magnitude:F1}m",
                        Severity = Severity.Info
                    });
                }
            }

            // Check lighting
            if (RenderSettings.ambientLight == Color.black)
            {
                _results.Add(new ValidationResult
                {
                    SceneName = sceneName,
                    ObjectName = "RenderSettings",
                    Message = "Ambient light is black (scene may be too dark)",
                    Severity = Severity.Warning
                });
            }

            // Check for directional light
            Light[] lights = GameObject.FindObjectsByType<Light>(FindObjectsSortMode.None);
            bool hasDirectionalLight = lights.Any(l => l.type == LightType.Directional);
            if (!hasDirectionalLight)
            {
                _results.Add(new ValidationResult
                {
                    SceneName = sceneName,
                    ObjectName = "Lighting",
                    Message = "No directional light found (scene may be poorly lit)",
                    Severity = Severity.Warning
                });
            }

            Repaint();
        }

        private void ExportResultsToConsole()
        {
            Debug.Log("========================================");
            Debug.Log("SCENE VALIDATION RESULTS");
            Debug.Log("========================================");

            var errorCount = _results.Count(r => r.Severity == Severity.Error);
            var warningCount = _results.Count(r => r.Severity == Severity.Warning);
            var infoCount = _results.Count(r => r.Severity == Severity.Info);

            Debug.Log($"Total Issues: {_results.Count}");
            Debug.Log($"Errors: {errorCount} | Warnings: {warningCount} | Info: {infoCount}");
            Debug.Log("========================================");

            foreach (var result in _results)
            {
                string prefix = result.Severity == Severity.Error ? "[ERROR]" :
                               result.Severity == Severity.Warning ? "[WARNING]" : "[INFO]";

                Debug.Log($"{prefix} [{result.SceneName}] {result.ObjectName}: {result.Message}");
            }

            Debug.Log("========================================");
        }

        private class ValidationResult
        {
            public string SceneName;
            public string ObjectName;
            public GameObject GameObject;
            public string Message;
            public Severity Severity;
        }

        private enum Severity
        {
            Info,
            Warning,
            Error
        }
    }
}
