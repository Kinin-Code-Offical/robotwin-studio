using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RobotTwin.UI
{
    public static class RuntimeModelLoader
    {
        private static int _mainThreadId;
        private const float MaxModelBoundsSize = 1000f;
        private const float MaxModelBoundsCenter = 1000f;
        private const string ContentRootName = "__RuntimeModelContent";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CaptureMainThreadId()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static async Task<GameObject> TryLoadModelAsync(string path, GameObject host, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".glb" && ext != ".gltf")
            {
                Debug.LogWarning($"[RuntimeModelLoader] Unsupported model format: {ext}");
                return null;
            }

            var gltfType = ResolveGltfType();
            if (gltfType == null)
            {
                Debug.LogWarning("[RuntimeModelLoader] GLTFast not available. Install the GLTFast package to load models at runtime.");
                return null;
            }

            EnsureDeferAgent(gltfType);
            if (!TryCreateGltf(gltfType, out var gltf)) return null;
            if (!TryGetLoadTask(gltf, gltfType, path, token, out var task, out var loadPath)) return null;

            LogInfo($"[RuntimeModelLoader] Loading {loadPath}");
            bool loaded;
            try
            {
                loaded = await task;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RuntimeModelLoader] GLTFast load exception: {ex.Message}");
                return null;
            }
            if (!loaded)
            {
                Debug.LogWarning($"[RuntimeModelLoader] GLTFast failed to load: {loadPath}");
                return null;
            }

            LogInfo($"[RuntimeModelLoader] Loaded {loadPath}");
            var root = new GameObject("RuntimeModel");
            if (host != null) root.transform.SetParent(host.transform, false);
            bool instantiated = await TryInstantiateAsync(gltfType, gltf, root.transform, token);
            if (!instantiated)
            {
                Debug.LogWarning($"[RuntimeModelLoader] GLTFast failed to instantiate: {loadPath}");
                UnityEngine.Object.Destroy(root);
                return null;
            }

            LogInfo($"[RuntimeModelLoader] Instantiated {loadPath}");
            NormalizeRuntimeModel(root.transform);
            return root;
        }

        public static bool TryLoadModel(string path, GameObject host, out GameObject instance)
        {
            instance = null;
            if (IsMainThread())
            {
                Debug.LogWarning("[RuntimeModelLoader] TryLoadModel called on main thread. Use TryLoadModelAsync.");
                return false;
            }
            try
            {
                var task = TryLoadModelAsync(path, host, default);
                task.Wait();
                instance = task.Result;
                return instance != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RuntimeModelLoader] Load error: {ex.Message}");
                instance = null;
                return false;
            }
        }

        private static Type ResolveGltfType()
        {
            return Type.GetType("GLTFast.GltfImport, com.unity.cloud.gltfast")
                ?? Type.GetType("GLTFast.GltfImport, glTFast")
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(asm => asm.GetType("GLTFast.GltfImport"))
                    .FirstOrDefault(t => t != null);
        }

        private static bool TryCreateGltf(Type gltfType, out object gltf)
        {
            gltf = null;
            try
            {
                gltf = Activator.CreateInstance(gltfType);
                return gltf != null;
            }
            catch (MissingMethodException)
            {
                var types = new[]
                {
                    gltfType.Assembly.GetType("GLTFast.Loading.IDownloadProvider"),
                    gltfType.Assembly.GetType("GLTFast.IDeferAgent"),
                    gltfType.Assembly.GetType("GLTFast.Materials.IMaterialGenerator"),
                    gltfType.Assembly.GetType("GLTFast.Logging.ICodeLogger")
                };
                var ctor = types.All(t => t != null) ? gltfType.GetConstructor(types) : null;
                if (ctor == null)
                {
                    ctor = gltfType.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 4);
                }
                if (ctor == null) return false;
                gltf = ctor.Invoke(new object[] { null, null, null, null });
                return gltf != null;
            }
        }

        private static bool TryGetLoadTask(
            object gltf,
            Type gltfType,
            string path,
            CancellationToken token,
            out Task<bool> task,
            out string loadPath)
        {
            task = null;
            loadPath = path;

            if (Path.IsPathRooted(path) && TryGetLoadFileTask(gltf, gltfType, path, token, out task))
            {
                return true;
            }

            var loadMethods = gltfType.GetMethods().Where(m => m.Name == "Load").ToArray();
            if (loadMethods.Length == 0) return false;

            if (Path.IsPathRooted(path))
            {
                try
                {
                    loadPath = new Uri(path).AbsoluteUri;
                }
                catch
                {
                    loadPath = path.Replace('\\', '/');
                }
            }

            var loadUri = new Uri(loadPath);
            foreach (var method in loadMethods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 1)
                {
                    if (parameters[0].ParameterType == typeof(string))
                    {
                        task = method.Invoke(gltf, new object[] { loadPath }) as Task<bool>;
                        break;
                    }
                    if (parameters[0].ParameterType == typeof(Uri))
                    {
                        task = method.Invoke(gltf, new object[] { loadUri }) as Task<bool>;
                        break;
                    }
                }
                if (parameters.Length == 3)
                {
                    if (parameters[0].ParameterType == typeof(string))
                    {
                        task = method.Invoke(gltf, new object[] { loadPath, null, token }) as Task<bool>;
                        break;
                    }
                    if (parameters[0].ParameterType == typeof(Uri))
                    {
                        task = method.Invoke(gltf, new object[] { loadUri, null, token }) as Task<bool>;
                        break;
                    }
                }
            }
            return task != null;
        }

        private static bool TryGetLoadFileTask(object gltf, Type gltfType, string path, CancellationToken token, out Task<bool> task)
        {
            task = null;
            var loadFile = gltfType.GetMethods().FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "LoadFile", StringComparison.Ordinal)) return false;
                var parameters = method.GetParameters();
                return parameters.Length == 4
                    && parameters[0].ParameterType == typeof(string)
                    && parameters[3].ParameterType == typeof(CancellationToken);
            });
            if (loadFile == null) return false;
            task = loadFile.Invoke(gltf, new object[] { path, null, null, token }) as Task<bool>;
            return task != null;
        }

        private static async Task<bool> TryInstantiateAsync(Type gltfType, object gltf, Transform parent, CancellationToken token)
        {
            var asyncMethod = gltfType.GetMethods().FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "InstantiateMainSceneAsync", StringComparison.Ordinal)) return false;
                var parameters = method.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType == typeof(Transform)
                    && parameters[1].ParameterType == typeof(CancellationToken);
            });
            if (asyncMethod != null)
            {
                LogInfo("[RuntimeModelLoader] Instantiating scene async");
                var task = asyncMethod.Invoke(gltf, new object[] { parent, token }) as Task<bool>;
                return task != null && await task;
            }

            var syncMethod = gltfType.GetMethod("InstantiateMainScene", new[] { typeof(Transform) });
            if (syncMethod == null)
            {
                Debug.LogWarning("[RuntimeModelLoader] GLTFast InstantiateMainScene not found.");
                return false;
            }
            LogInfo("[RuntimeModelLoader] Instantiating scene sync");
            return (bool)syncMethod.Invoke(gltf, new object[] { parent });
        }

        private static void EnsureDeferAgent(Type gltfType)
        {
            try
            {
                var agentType = gltfType.Assembly.GetType("GLTFast.TimeBudgetPerFrameDeferAgent");
                var setDefault = gltfType.GetMethod("SetDefaultDeferAgent", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (agentType == null || setDefault == null) return;

                var existing = UnityEngine.Object.FindFirstObjectByType(agentType);
                if (existing == null)
                {
                    var go = new GameObject("GLTFast_DeferAgent");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    existing = go.AddComponent(agentType);
                }

                var setBudget = agentType.GetMethod("SetFrameBudget", new[] { typeof(float) });
                if (setBudget != null)
                {
                    setBudget.Invoke(existing, new object[] { 0.01f });
                }

                setDefault.Invoke(null, new[] { existing });
            }
            catch
            {
                // ignore; fallback to GLTFast defaults
            }
        }

        private static bool IsMainThread()
        {
            if (_mainThreadId == 0) return true;
            return Thread.CurrentThread.ManagedThreadId == _mainThreadId;
        }

        private static void LogInfo(string message)
        {
            if (Debug.isDebugBuild || Application.isEditor)
            {
                Debug.Log(message);
            }
        }

        private static void NormalizeRuntimeModel(Transform root)
        {
            if (root == null) return;
            var contentRoot = EnsureContentRoot(root);
            if (contentRoot == null) return;

            bool sanitized = SanitizeTransforms(contentRoot);
            bool normalized = NormalizeBounds(contentRoot, MaxModelBoundsSize, MaxModelBoundsCenter);
            if (sanitized && !normalized)
            {
                Debug.LogWarning("[RuntimeModelLoader] Normalized model transforms for stability.");
            }
        }

        private static Transform EnsureContentRoot(Transform root)
        {
            if (root == null) return null;
            Transform existing = null;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child != null && child.name == ContentRootName)
                {
                    existing = child;
                    break;
                }
            }
            if (existing != null && root.childCount == 1) return existing;

            if (root.childCount == 0) return null;
            var children = new List<Transform>(root.childCount);
            for (int i = 0; i < root.childCount; i++)
            {
                children.Add(root.GetChild(i));
            }

            var content = existing != null ? existing.gameObject : new GameObject(ContentRootName);
            content.transform.SetParent(root, false);
            foreach (var child in children)
            {
                if (child == null || child == content.transform) continue;
                child.SetParent(content.transform, true);
            }
            return content.transform;
        }

        private static bool SanitizeTransforms(Transform root)
        {
            bool changed = false;
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null) continue;
                changed |= SanitizeTransform(current);
                for (int i = 0; i < current.childCount; i++)
                {
                    stack.Push(current.GetChild(i));
                }
            }
            return changed;
        }

        private static bool SanitizeTransform(Transform target)
        {
            bool changed = false;
            var position = target.localPosition;
            if (!IsFinite(position))
            {
                position = Vector3.zero;
                changed = true;
            }

            var rotation = target.localRotation;
            if (!IsValidQuaternion(rotation))
            {
                rotation = Quaternion.identity;
                changed = true;
            }

            var scale = target.localScale;
            if (!IsFinite(scale))
            {
                scale = Vector3.one;
                changed = true;
            }

            if (changed)
            {
                target.localPosition = position;
                target.localRotation = rotation;
                target.localScale = scale;
            }
            return changed;
        }

        private static bool NormalizeBounds(Transform root, float maxSize, float maxCenter)
        {
            if (!TryGetLocalBounds(root, out var bounds)) return false;
            if (!IsFinite(bounds.center) || !IsFinite(bounds.size)) return false;

            float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float center = MaxAbs(bounds.center);
            bool needsScale = size > maxSize;
            bool needsCenter = center > maxCenter;
            if (!needsScale && !needsCenter) return false;

            float scaleFactor = needsScale && size > 0f ? maxSize / size : 1f;
            root.localScale = root.localScale * scaleFactor;
            root.localPosition += -bounds.center * scaleFactor;
            Debug.LogWarning($"[RuntimeModelLoader] Normalized model bounds (size {size:0.###}, center {center:0.###}, scale {scaleFactor:0.####}).");
            return true;
        }

        private static bool TryGetLocalBounds(Transform root, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var localBounds = renderer.localBounds;
                var matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                if (!IsFinite(matrix)) continue;
                var transformed = TransformBounds(localBounds, matrix);
                if (!IsFinite(transformed.center) || !IsFinite(transformed.size)) continue;
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

        private static float MaxAbs(Vector3 value)
        {
            return Mathf.Max(Mathf.Abs(value.x), Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z)));
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(Matrix4x4 value)
        {
            for (int i = 0; i < 16; i++)
            {
                if (!IsFinite(value[i])) return false;
            }
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsValidQuaternion(Quaternion value)
        {
            if (!IsFinite(value)) return false;
            float magnitude = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return magnitude > 0.0001f;
        }
    }
}
