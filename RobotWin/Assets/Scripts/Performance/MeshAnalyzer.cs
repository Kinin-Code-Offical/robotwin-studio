// RobotWin Studio - Mesh Analyzer + Weighted LOD Builder
// Preserves material assignments and keeps high-weight geometry in low LODs.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RobotTwin.Performance
{
    public static class MeshAnalyzer
    {
        public struct MeshStats
        {
            public int RendererCount;
            public int MeshCount;
            public int TriangleCount;
            public int VertexCount;
            public float TotalVolume;
        }

        public struct LODSettings
        {
            public float LODHigh;
            public float LODMedium;
            public float LODLow;
            public float MediumWeightFraction;
            public float LowWeightFraction;
        }

        public static LODSettings DefaultSettings => new LODSettings
        {
            LODHigh = 0.35f,
            LODMedium = 0.15f,
            LODLow = 0.05f,
            MediumWeightFraction = 0.6f,
            LowWeightFraction = 0.25f
        };

        public static MeshStats Analyze(GameObject root)
        {
            var stats = new MeshStats();
            if (root == null) return stats;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            stats.RendererCount = renderers.Length;
            var meshes = new HashSet<Mesh>();

            foreach (var renderer in renderers)
            {
                var mesh = GetMesh(renderer);
                if (mesh == null) continue;
                meshes.Add(mesh);
                stats.TriangleCount += mesh.triangles != null ? mesh.triangles.Length / 3 : 0;
                stats.VertexCount += mesh.vertexCount;
                stats.TotalVolume += GetRendererWeight(renderer);
            }

            stats.MeshCount = meshes.Count;
            return stats;
        }

        public static int SetupWeightedLodGroups(GameObject root, LODSettings settings)
        {
            if (root == null) return 0;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var grouped = renderers
                .Where(r => r != null && r.enabled)
                .GroupBy(r => GetGroupRoot(root.transform, r.transform))
                .Where(g => g.Key != null)
                .Where(g => g.Count() >= 2);

            int created = 0;
            foreach (var group in grouped)
            {
                var groupRoot = group.Key;
                if (groupRoot.GetComponent<LODGroup>() != null)
                {
                    continue;
                }

                var rendererList = group.ToArray();
                if (rendererList.Length == 0) continue;

                var lodGroup = groupRoot.gameObject.AddComponent<LODGroup>();
                var ordered = rendererList
                    .Select(r => new WeightedRenderer(r, GetRendererWeight(r)))
                    .OrderByDescending(w => w.Weight)
                    .ToArray();

                float totalWeight = ordered.Sum(w => w.Weight);
                var medium = SelectByWeight(ordered, totalWeight * Mathf.Clamp01(settings.MediumWeightFraction));
                var low = SelectByWeight(ordered, totalWeight * Mathf.Clamp01(settings.LowWeightFraction));

                if (medium.Length == 0) medium = new[] { ordered[0].Renderer };
                if (low.Length == 0) low = new[] { ordered[0].Renderer };

                var lods = new[]
                {
                    new LOD(settings.LODHigh, rendererList),
                    new LOD(settings.LODMedium, medium),
                    new LOD(settings.LODLow, low)
                };

                lodGroup.SetLODs(lods);
                lodGroup.RecalculateBounds();
                created++;
            }

            return created;
        }

        private static Transform GetGroupRoot(Transform root, Transform rendererTransform)
        {
            if (root == null || rendererTransform == null) return null;
            var current = rendererTransform;
            while (current.parent != null && current.parent != root)
            {
                current = current.parent;
            }
            return current;
        }

        private static Renderer[] SelectByWeight(WeightedRenderer[] ordered, float targetWeight)
        {
            if (ordered == null || ordered.Length == 0) return new Renderer[0];
            if (targetWeight <= 0f) return new[] { ordered[0].Renderer };

            var list = new List<Renderer>();
            float sum = 0f;
            foreach (var item in ordered)
            {
                list.Add(item.Renderer);
                sum += item.Weight;
                if (sum >= targetWeight) break;
            }
            return list.ToArray();
        }

        private static float GetRendererWeight(Renderer renderer)
        {
            if (renderer == null) return 0f;
            var size = renderer.bounds.size;
            return Mathf.Max(0.000001f, size.x * size.y * size.z);
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is MeshRenderer meshRenderer)
            {
                var filter = meshRenderer.GetComponent<MeshFilter>();
                return filter != null ? filter.sharedMesh : null;
            }

            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            return null;
        }

        private readonly struct WeightedRenderer
        {
            public WeightedRenderer(Renderer renderer, float weight)
            {
                Renderer = renderer;
                Weight = weight;
            }

            public Renderer Renderer { get; }
            public float Weight { get; }
        }
    }
}
