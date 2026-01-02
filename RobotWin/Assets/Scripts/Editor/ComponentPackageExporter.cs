using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RobotTwin.UI;
using UnityEditor;
using UnityEngine;

namespace RobotTwin.Editor
{
    public static class ComponentPackageExporter
    {
        private const string ResourcesFolder = "Resources/Components";
        private const string StreamingFolder = "StreamingAssets/Components";

        [MenuItem("RobotTwin/Components/Export RTComp Packages")]
        public static void ExportPackages()
        {
            string dataPath = Application.dataPath;
            string resourcesRoot = Path.Combine(dataPath, ResourcesFolder.Replace('/', Path.DirectorySeparatorChar));
            string streamingRoot = Path.Combine(dataPath, StreamingFolder.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(streamingRoot);

            int exported = 0;
            exported += ExportFolder(resourcesRoot, streamingRoot);
            exported += ExportFolder(streamingRoot, streamingRoot);

            AssetDatabase.Refresh();
            Debug.Log($"[ComponentPackageExporter] Exported {exported} .rtcomp packages to {streamingRoot}.");
        }

        private static int ExportFolder(string sourceRoot, string outputRoot)
        {
            if (!Directory.Exists(sourceRoot)) return 0;
            int count = 0;
            var files = Directory.GetFiles(sourceRoot, "*.json", SearchOption.AllDirectories);
            foreach (var jsonFile in files)
            {
                if (IsWithinPackageDir(jsonFile)) continue;
                if (string.Equals(Path.GetFileName(jsonFile), ComponentPackageUtility.DefinitionFileName, StringComparison.OrdinalIgnoreCase)) continue;

                string json = File.ReadAllText(jsonFile);
                if (string.IsNullOrWhiteSpace(json)) continue;

                var stub = ParseStub(json);
                string modelSource = ResolveModelSource(jsonFile, stub?.modelFile);
                if (string.IsNullOrWhiteSpace(modelSource))
                {
                    modelSource = ResolveBundledModelByType(stub?.type);
                }

                string packagedModelSource = modelSource;
                if (!string.IsNullOrWhiteSpace(modelSource))
                {
                    TryExtractMaterialsFromFbx(modelSource);
                    if (TryConvertModelToGlb(modelSource, out var glbPath))
                    {
                        packagedModelSource = glbPath;
                    }
                    else
                    {
                        string fallbackGlb = Path.ChangeExtension(modelSource, ".glb");
                        if (File.Exists(fallbackGlb))
                        {
                            packagedModelSource = fallbackGlb;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(packagedModelSource))
                {
                    json = UpdateModelFileInJson(json, ComponentPackageUtility.BuildAssetEntryName(Path.GetFileName(packagedModelSource)));
                }

                string packageName = SanitizeFileName(Path.GetFileNameWithoutExtension(jsonFile));
                string packagePath = Path.Combine(outputRoot, packageName + ComponentPackageUtility.PackageExtension);
                ComponentPackageUtility.SavePackage(packagePath, json, packagedModelSource);
                count++;
            }
            return count;
        }

        private static string ResolveModelSource(string jsonFile, string modelFile)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modelFile)) return string.Empty;
                string path = modelFile.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathRooted(path) && File.Exists(path)) return path;
                string baseDir = Path.GetDirectoryName(jsonFile) ?? string.Empty;
                string candidate = Path.Combine(baseDir, path);
                return File.Exists(candidate) ? candidate : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static ModelStub ParseStub(string json)
        {
            try
            {
                return JsonUtility.FromJson<ModelStub>(json);
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveBundledModelByType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return string.Empty;
            string key = type.ToLowerInvariant();
            string fileName = null;
            if (key.Contains("arduinouno")) fileName = "ArduinoUno.fbx";
            else if (key.Contains("arduinonano")) fileName = "ArduinoNano.fbx";
            else if (key.Contains("arduinopromini")) fileName = "ArduinoProMini.fbx";
            else if (key.Contains("arduinopromicro")) fileName = "ArduinoProMicro.fbx";
            else if (key.Contains("arduino")) fileName = "Arduino.fbx";
            else if (key.Contains("resistor")) fileName = "Resistor.fbx";
            else if (key.Contains("led")) fileName = "LED.fbx";
            else if (key.Contains("battery")) fileName = "Battery.fbx";
            else if (key.Contains("button")) fileName = "Button.fbx";
            else if (key.Contains("switch")) fileName = "Swirch_ON_OFF.fbx";
            else if (key.Contains("servo")) fileName = "ServoSG90.fbx";

            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            string path = Path.Combine(Application.dataPath, "Resources", "Prefabs", "Circuit3D", fileName);
            return File.Exists(path) ? path : string.Empty;
        }

        private static string UpdateModelFileInJson(string json, string entryName)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(entryName)) return json;
            try
            {
                var obj = JObject.Parse(json);
                obj["modelFile"] = entryName;
                return obj.ToString();
            }
            catch
            {
                return json;
            }
        }

        private static bool IsWithinPackageDir(string path)
        {
            string dir = Path.GetDirectoryName(path);
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (dir.EndsWith(ComponentPackageUtility.PackageExtension, StringComparison.OrdinalIgnoreCase)) return true;
                dir = Path.GetDirectoryName(dir);
            }
            return false;
        }

        private static void TryExtractMaterialsFromFbx(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return;
            if (!string.Equals(Path.GetExtension(sourcePath), ".fbx", StringComparison.OrdinalIgnoreCase)) return;
            string outputDir = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrWhiteSpace(outputDir)) return;

            string assetPath;
            bool tempImported = false;
            if (!TryGetAssetPath(sourcePath, out assetPath))
            {
                string tempImportRoot = Path.Combine(Application.dataPath, "Temp", "ComponentPackageImporter");
                Directory.CreateDirectory(tempImportRoot);
                string tempFull = Path.Combine(tempImportRoot, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, tempFull, true);
                if (!TryGetAssetPath(tempFull, out assetPath)) return;
                tempImported = true;
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) return;

            var materials = new HashSet<Material>();
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat != null) materials.Add(mat);
                }
            }
            if (materials.Count == 0) return;

            string tempMatRoot = Path.Combine(Application.dataPath, "Temp", "ComponentPackageMaterials");
            Directory.CreateDirectory(tempMatRoot);

            var tempMatAssets = new List<string>();
            foreach (var mat in materials)
            {
                string safeName = SanitizeFileName(mat.name);
                if (string.IsNullOrWhiteSpace(safeName)) safeName = "mat";
                string matFull = Path.Combine(tempMatRoot, safeName + ".mat");
                string matAsset = ToAssetPath(matFull);
                matAsset = AssetDatabase.GenerateUniqueAssetPath(matAsset);
                var matCopy = new Material(mat);
                AssetDatabase.CreateAsset(matCopy, matAsset);
                tempMatAssets.Add(matAsset);
            }
            AssetDatabase.SaveAssets();

            foreach (var matAsset in tempMatAssets)
            {
                CopyAssetFileToDirectory(matAsset, outputDir);
                foreach (var dep in AssetDatabase.GetDependencies(matAsset, true))
                {
                    if (string.Equals(dep, matAsset, StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsTextureAsset(dep)) CopyAssetFileToDirectory(dep, outputDir);
                }
            }

            foreach (var matAsset in tempMatAssets)
            {
                AssetDatabase.DeleteAsset(matAsset);
            }
            if (tempImported)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
            AssetDatabase.Refresh();
        }

        private static bool TryGetAssetPath(string fullPath, out string assetPath)
        {
            assetPath = string.Empty;
            if (string.IsNullOrWhiteSpace(fullPath)) return false;
            string dataPath = Application.dataPath.Replace('\\', '/');
            string full = Path.GetFullPath(fullPath).Replace('\\', '/');
            if (!full.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase)) return false;
            assetPath = "Assets" + full.Substring(dataPath.Length);
            return true;
        }

        private static string ToAssetPath(string fullPath)
        {
            if (!TryGetAssetPath(fullPath, out var assetPath)) return string.Empty;
            return assetPath;
        }

        private static void CopyAssetFileToDirectory(string assetPath, string outputDir)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(outputDir)) return;
            if (!assetPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase)) return;
            string fullPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets".Length).TrimStart('/', '\\'));
            if (!File.Exists(fullPath)) return;
            string destPath = Path.Combine(outputDir, Path.GetFileName(fullPath));
            if (File.Exists(destPath))
            {
                var srcTime = File.GetLastWriteTimeUtc(fullPath);
                var destTime = File.GetLastWriteTimeUtc(destPath);
                if (destTime >= srcTime) return;
            }
            File.Copy(fullPath, destPath, true);
        }

        private static bool IsTextureAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return false;
            var type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            return type != null && typeof(Texture).IsAssignableFrom(type);
        }

        private static bool TryConvertModelToGlb(string sourcePath, out string glbPath)
        {
            glbPath = string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return false;
            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext == ".glb" || ext == ".gltf") return false;
            if (ext != ".fbx" && ext != ".obj") return false;

            if (!TryFindBlenderPath(out var blenderPath))
            {
                Debug.LogWarning("[ComponentPackageExporter] Blender not found. Set ROBOTWIN_BLENDER or BLENDER_PATH.");
                return false;
            }
            string scriptPath = ResolveBlenderScriptPath();
            if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            {
                Debug.LogWarning("[ComponentPackageExporter] Blender script missing.");
                return false;
            }

            string outputPath = Path.ChangeExtension(sourcePath, ".glb");
            if (File.Exists(outputPath))
            {
                var srcTime = File.GetLastWriteTimeUtc(sourcePath);
                var outTime = File.GetLastWriteTimeUtc(outputPath);
                if (outTime >= srcTime)
                {
                    glbPath = outputPath;
                    return true;
                }
            }

            if (!RunBlenderConvert(blenderPath, scriptPath, sourcePath, outputPath)) return false;
            if (!File.Exists(outputPath))
            {
                Debug.LogWarning("[ComponentPackageExporter] Blender finished but GLB was not created.");
                return false;
            }
            glbPath = outputPath;
            return true;
        }

        private static bool TryFindBlenderPath(out string blenderPath)
        {
            blenderPath = GetEnvVar("ROBOTWIN_BLENDER");
            if (string.IsNullOrWhiteSpace(blenderPath))
            {
                blenderPath = GetEnvVar("BLENDER_PATH");
            }
            if (!string.IsNullOrWhiteSpace(blenderPath))
            {
                blenderPath = blenderPath.Trim().Trim('"');
                if (Directory.Exists(blenderPath))
                {
                    string candidate = Path.Combine(blenderPath, "blender.exe");
                    if (File.Exists(candidate))
                    {
                        blenderPath = candidate;
                        return true;
                    }
                }
                if (File.Exists(blenderPath)) return true;
            }

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                string candidate = Path.Combine(dir.Trim(), "blender.exe");
                if (File.Exists(candidate))
                {
                    blenderPath = candidate;
                    return true;
                }
            }

            blenderPath = string.Empty;
            return false;
        }

        private static string GetEnvVar(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
            value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(value)) return value;
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
        }

        private static string ResolveBlenderScriptPath()
        {
            string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            return Path.Combine(repoRoot, "tools", "scripts", "blender_to_glb.py");
        }

        private static bool RunBlenderConvert(string blenderPath, string scriptPath, string inputPath, string outputPath)
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = blenderPath,
                    Arguments = $"--background --python \"{scriptPath}\" -- \"{inputPath}\" \"{outputPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null) return false;
                process.WaitForExit(120000);
                if (process.ExitCode != 0)
                {
                    string error = process.StandardError.ReadToEnd();
                    Debug.LogWarning($"[ComponentPackageExporter] Blender conversion failed: {error}");
                    return false;
                }
                string output = process.StandardOutput.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Debug.Log($"[ComponentPackageExporter] Blender output: {output}");
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ComponentPackageExporter] Blender conversion error: {ex.Message}");
                return false;
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "package";
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(safe) ? "package" : safe;
        }

        [Serializable]
        private class ModelStub
        {
            public string modelFile;
            public string type;
        }
    }
}
