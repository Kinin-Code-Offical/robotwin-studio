using UnityEngine;
using UnityEditor;

namespace RobotWin.EditorTools
{
    /// <summary>
    /// Authentically generates the materials for the "Professor's Track".
    /// Automatically detects if materials are missing and creates physically accurate ones.
    /// Focus: High Contrast Glossiness (Matte Paper vs Shiny Tape) to create lighting challenges.
    /// </summary>
    public class TrackMaterialSetup : EditorWindow
    {
        [MenuItem("RobotWin/Environment/Generate Track Materials")]
        public static void GenerateMaterials()
        {
            EnsureMaterial("Materials/Track_Paper_A3", Color.white, 0.1f, 0.9f); // High roughness (Matte), High Friction
            EnsureMaterial("Materials/Track_Electrical_Tape", Color.black, 0.85f, 0.6f); // Low roughness (Shiny), Lower Friction (Slippery)

            Debug.Log("Track Materials Generated/Verified. Apply 'Track_Paper_A3' to ground and 'Track_Electrical_Tape' to the line.");
        }

        private static void EnsureMaterial(string path, Color albedo, float smoothness, float frictionCoeff)
        {
            // 1. Check if folder exists
            string folder = "Assets/Materials";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Materials");

            // 2. Check if asset exists
            string fullPath = "Assets/" + path + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(fullPath);

            if (mat == null)
            {
                // Create new Standard Shader material
                mat = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, fullPath);
            }

            // 3. Configure Visuals (The "Glare" Effect)
            mat.color = albedo;
            mat.SetFloat("_Glossiness", smoothness); // Smoothness creates the specular highlight
            mat.SetFloat("_Metallic", 0.0f); // Plastic/Paper is dielectric

            // 4. Configure Physics (The "Patinaj" Effect)
            // Note: In Unity, PhysicMaterial is separate. Let's create that too.
            string physPath = "Assets/" + path + "_Phys.physicMaterial";
            PhysicsMaterial physMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(physPath);
            if (physMat == null)
            {
                physMat = new PhysicsMaterial(path + "_Phys");
                AssetDatabase.CreateAsset(physMat, physPath);
            }

            physMat.dynamicFriction = frictionCoeff;
            physMat.staticFriction = frictionCoeff + 0.1f;

            // Link? We can't link PhysMat to Mat directly without a script or manual assignment.
            // But we ensure they exist for the user.
        }
    }
}
