using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using RobotTwin.Game;

namespace RobotTwin.EditorTools
{
    public static class NativeColliderGenerator
    {
        private const float SphereThreshold = 0.12f;

        public static void GenerateForSelection(GameObject[] targets)
        {
            if (targets == null || targets.Length == 0)
            {
                Debug.LogWarning("[NativeColliderGenerator] No selection.");
                return;
            }

            var objects = CollectHierarchy(targets);
            GenerateForObjects(objects);
        }

        public static void GenerateForScene()
        {
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            var objects = CollectHierarchy(roots);
            GenerateForObjects(objects);
        }

        [MenuItem("RobotWin/Physics/Generate Native Colliders (Selected)")]
        private static void GenerateSelected()
        {
            GenerateForSelection(Selection.gameObjects);
        }

        [MenuItem("RobotWin/Physics/Generate Native Colliders (Scene)")]
        private static void GenerateScene()
        {
            GenerateForScene();
        }

        private static HashSet<GameObject> CollectHierarchy(GameObject[] roots)
        {
            var set = new HashSet<GameObject>();
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                {
                    if (tr == null) continue;
                    set.Add(tr.gameObject);
                }
            }
            return set;
        }

        private static void GenerateForObjects(HashSet<GameObject> objects)
        {
            int collidersAdded = 0;
            int bodiesAdded = 0;

            foreach (var go in objects)
            {
                if (go == null) continue;

                if (!TryGetBounds(go, out var bounds, out var isSphere))
                {
                    continue;
                }

                if (go.GetComponent<Collider>() == null)
                {
                    if (isSphere)
                    {
                        var sphere = Undo.AddComponent<SphereCollider>(go);
                        sphere.center = bounds.center;
                        sphere.radius = Mathf.Max(0.001f, bounds.extents.magnitude);
                    }
                    else
                    {
                        var box = Undo.AddComponent<BoxCollider>(go);
                        box.center = bounds.center;
                        box.size = bounds.size;
                    }
                    collidersAdded++;
                }

                if (go.GetComponent<NativePhysicsBody>() == null)
                {
                    Undo.AddComponent<NativePhysicsBody>(go);
                    bodiesAdded++;
                }
            }

            Debug.Log($"[NativeColliderGenerator] Added {collidersAdded} collider(s), {bodiesAdded} NativePhysicsBody component(s).");
        }

        private static bool TryGetBounds(GameObject go, out Bounds bounds, out bool isSphere)
        {
            bounds = default;
            isSphere = false;

            if (go.TryGetComponent<MeshFilter>(out var meshFilter) && meshFilter.sharedMesh != null)
            {
                bounds = meshFilter.sharedMesh.bounds;
                isSphere = IsSphereLike(bounds.size);
                return true;
            }

            if (go.TryGetComponent<SkinnedMeshRenderer>(out var skinned))
            {
                bounds = skinned.localBounds;
                isSphere = IsSphereLike(bounds.size);
                return true;
            }

            return false;
        }

        private static bool IsSphereLike(Vector3 size)
        {
            float max = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (max <= 0f) return false;
            float dx = Mathf.Abs(size.x - size.y) / max;
            float dy = Mathf.Abs(size.y - size.z) / max;
            float dz = Mathf.Abs(size.x - size.z) / max;
            return dx < SphereThreshold && dy < SphereThreshold && dz < SphereThreshold;
        }
    }
}
