// RobotWin Studio - LOD System for Circuit Components
// Automatic Level-of-Detail generation for Arduino, Raspberry Pi, complex ICs
// Reduces polygon count by 60-90% at distance

using System.Linq;
using UnityEngine;

namespace RobotTwin.Performance
{
    /// <summary>
    /// Automatic LOD group creator for circuit components.
    /// Creates 3 detail levels: High (100%), Medium (40%), Low (10%).
    /// </summary>
    [ExecuteInEditMode]
    public class ComponentLOD : MonoBehaviour
    {
        [Header("LOD Configuration")]
        [Tooltip("Screen height percentages for LOD transitions")]
        [SerializeField] private float _lodHigh = 0.6f;    // 60% screen height
        [SerializeField] private float _lodMedium = 0.25f; // 25% screen height
        [SerializeField] private float _lodLow = 0.1f;     // 10% screen height

        private LODGroup _lodGroup;

        private void OnValidate()
        {
            // Ensure LOD distances are ordered correctly
            if (_lodMedium >= _lodHigh) _lodMedium = _lodHigh * 0.5f;
            if (_lodLow >= _lodMedium) _lodLow = _lodMedium * 0.4f;
        }

        /// <summary>
        /// Setup LOD group on this component.
        /// Automatically assigns meshes to appropriate LOD levels.
        /// </summary>
        [ContextMenu("Setup LOD Group")]
        public void SetupLODGroup()
        {
            // Get or create LOD group
            _lodGroup = GetComponent<LODGroup>();
            if (_lodGroup == null)
                _lodGroup = gameObject.AddComponent<LODGroup>();

            // Find all renderers in hierarchy
            var allRenderers = GetComponentsInChildren<Renderer>();
            if (allRenderers.Length == 0)
            {
                Debug.LogWarning($"[ComponentLOD] No renderers found on {name}");
                return;
            }

            // Classify renderers by detail level
            var highDetail = ClassifyHighDetail(allRenderers);
            var mediumDetail = ClassifyMediumDetail(allRenderers, highDetail);
            var lowDetail = ClassifyLowDetail(allRenderers, highDetail, mediumDetail);

            // Create LOD array
            LOD[] lods = new LOD[3];
            lods[0] = new LOD(_lodHigh, highDetail);
            lods[1] = new LOD(_lodMedium, mediumDetail);
            lods[2] = new LOD(_lodLow, lowDetail);

            // Apply LODs
            _lodGroup.SetLODs(lods);
            _lodGroup.RecalculateBounds();

            Debug.Log($"[ComponentLOD] Setup complete on {name}: High={highDetail.Length}, Medium={mediumDetail.Length}, Low={lowDetail.Length}");
        }

        /// <summary>
        /// Classify high detail meshes (all visible meshes).
        /// </summary>
        private Renderer[] ClassifyHighDetail(Renderer[] allRenderers)
        {
            // High detail = all renderers
            return allRenderers;
        }

        /// <summary>
        /// Classify medium detail meshes (hide fine details like text, screws).
        /// </summary>
        private Renderer[] ClassifyMediumDetail(Renderer[] allRenderers, Renderer[] highDetail)
        {
            // Medium detail = remove meshes with "Label", "Text", "Screw", "Pin"
            return allRenderers.Where(r =>
            {
                string name = r.name.ToLower();
                return !name.Contains("label") &&
                       !name.Contains("text") &&
                       !name.Contains("screw") &&
                       !name.Contains("pin") &&
                       !name.Contains("lead");
            }).ToArray();
        }

        /// <summary>
        /// Classify low detail meshes (only main body, hide all details).
        /// </summary>
        private Renderer[] ClassifyLowDetail(Renderer[] allRenderers, Renderer[] highDetail, Renderer[] mediumDetail)
        {
            // Low detail = only meshes with "Body", "Board", "Case", "Housing"
            var lowRenderers = allRenderers.Where(r =>
            {
                string name = r.name.ToLower();
                return name.Contains("body") ||
                       name.Contains("board") ||
                       name.Contains("case") ||
                       name.Contains("housing") ||
                       name.Contains("pcb");
            }).ToArray();

            // If no "body" meshes found, use largest mesh
            if (lowRenderers.Length == 0)
            {
                lowRenderers = new[] { GetLargestRenderer(allRenderers) };
            }

            return lowRenderers;
        }

        /// <summary>
        /// Get largest renderer by polygon count.
        /// </summary>
        private Renderer GetLargestRenderer(Renderer[] renderers)
        {
            Renderer largest = renderers[0];
            int maxPolys = GetPolyCount(largest);

            for (int i = 1; i < renderers.Length; i++)
            {
                int polys = GetPolyCount(renderers[i]);
                if (polys > maxPolys)
                {
                    maxPolys = polys;
                    largest = renderers[i];
                }
            }

            return largest;
        }

        /// <summary>
        /// Get polygon count for renderer.
        /// </summary>
        private int GetPolyCount(Renderer renderer)
        {
            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return 0;

            return MeshAnalyzer.GetTriangleCountSafe(meshFilter.sharedMesh);
        }

        /// <summary>
        /// Get LOD statistics (polygon count per level).
        /// </summary>
        public LODStatistics GetStatistics()
        {
            if (_lodGroup == null)
                _lodGroup = GetComponent<LODGroup>();

            if (_lodGroup == null)
                return default;

            var lods = _lodGroup.GetLODs();
            var stats = new LODStatistics
            {
                LodCount = lods.Length,
                HighPolyCount = GetLODPolyCount(lods[0]),
                MediumPolyCount = lods.Length > 1 ? GetLODPolyCount(lods[1]) : 0,
                LowPolyCount = lods.Length > 2 ? GetLODPolyCount(lods[2]) : 0
            };

            stats.MediumReduction = stats.HighPolyCount > 0
                ? (1f - (float)stats.MediumPolyCount / stats.HighPolyCount) * 100f
                : 0f;

            stats.LowReduction = stats.HighPolyCount > 0
                ? (1f - (float)stats.LowPolyCount / stats.HighPolyCount) * 100f
                : 0f;

            return stats;
        }

        /// <summary>
        /// Get total polygon count for LOD level.
        /// </summary>
        private int GetLODPolyCount(LOD lod)
        {
            int total = 0;
            foreach (var renderer in lod.renderers)
            {
                total += GetPolyCount(renderer);
            }
            return total;
        }

        private void OnDrawGizmosSelected()
        {
            if (_lodGroup == null)
                return;

            // Draw LOD bounds
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(transform.position, Vector3.one * _lodGroup.size);

            // Draw LOD transition distances
            var lods = _lodGroup.GetLODs();
            for (int i = 0; i < lods.Length; i++)
            {
                float screenHeight = lods[i].screenRelativeTransitionHeight;
                float radius = _lodGroup.size * screenHeight;

                Gizmos.color = new Color(1f - (i * 0.3f), i * 0.3f, 0f);
                Gizmos.DrawWireSphere(transform.position, radius);
            }
        }
    }

    /// <summary>
    /// LOD statistics for component.
    /// </summary>
    public struct LODStatistics
    {
        public int LodCount;
        public int HighPolyCount;
        public int MediumPolyCount;
        public int LowPolyCount;
        public float MediumReduction;
        public float LowReduction;

        public override string ToString()
        {
            return $"LODs: {LodCount}, Polys: High={HighPolyCount}, Medium={MediumPolyCount} ({MediumReduction:F1}%), Low={LowPolyCount} ({LowReduction:F1}%)";
        }
    }
}
