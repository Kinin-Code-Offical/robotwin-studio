using System.IO;
using UnityEditor;
using UnityEngine;

namespace RobotTwin.Editor
{
    public static class GltfFastShaderKeeper
    {
        [MenuItem("RobotWin/Tools/Ensure glTFast Shaders Included")]
        public static void EnsureShadersIncluded()
        {
            string[] shaderNames =
            {
                "glTF/PbrMetallicRoughness",
                "glTF/PbrSpecularGlossiness",
                "glTF/Unlit"
            };

            string targetDir = "Assets/Resources/glTFast";
            EnsureFolder("Assets/Resources", "glTFast");

            int created = 0;
            foreach (var shaderName in shaderNames)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogWarning($"[glTFast] Shader not found: {shaderName} (is glTFast installed?)");
                    continue;
                }

                string fileName = shaderName.Replace("/", "_") + ".mat";
                string assetPath = Path.Combine(targetDir, fileName).Replace('\\', '/');
                var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (existing != null)
                {
                    if (existing.shader != shader)
                    {
                        existing.shader = shader;
                        EditorUtility.SetDirty(existing);
                    }
                    continue;
                }

                var mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, assetPath);
                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[glTFast] Shader keep-alive materials updated. Created={created} Path={targetDir}");
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent))
            {
                var parts = parent.Split('/');
                string current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(current, parts[i]);
                    }
                    current = next;
                }
            }

            string full = parent.TrimEnd('/') + "/" + child;
            if (!AssetDatabase.IsValidFolder(full))
            {
                AssetDatabase.CreateFolder(parent.TrimEnd('/'), child);
            }
        }
    }
}

