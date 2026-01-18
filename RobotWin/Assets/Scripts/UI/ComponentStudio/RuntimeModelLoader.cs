using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace RobotTwin.UI
{
    public static class RuntimeModelLoader
    {
        private static int _mainThreadId;
        private static bool _loggedDispatcherMissing;
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
            GameObject root = null;
            try
            {
                await RunOnMainThreadAsync(() =>
                {
                    root = new GameObject("RuntimeModel");
                    if (host != null) root.transform.SetParent(host.transform, false);
                }, token);

                bool instantiated = await RunOnMainThreadAsync(async () =>
                {
                    return await TryInstantiateAsync(gltfType, gltf, root.transform, token);
                }, token);

                if (!instantiated)
                {
                    Debug.LogWarning($"[RuntimeModelLoader] GLTFast failed to instantiate: {loadPath}");
                    await RunOnMainThreadAsync(() =>
                    {
                        if (root != null) UnityEngine.Object.Destroy(root);
                    }, token);
                    return null;
                }

                LogInfo($"[RuntimeModelLoader] Instantiated {loadPath}");
                await RunOnMainThreadAsync(() =>
                {
                    NormalizeRuntimeModel(root.transform);
                    EnsureRuntimeColliders(root.transform);
                }, token);

                return root;
            }
            catch (OperationCanceledException)
            {
                if (root != null)
                {
                    await RunOnMainThreadAsync(() =>
                    {
                        if (root != null) UnityEngine.Object.Destroy(root);
                    }, default);
                }
                return null;
            }
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
            // Prefer the 4-arg ctor so we can inject a render-pipeline aware material generator.
            // This is critical in URP/HDRP projects, otherwise materials can appear missing/pink.
            var materialGenerator = TryCreateMaterialGenerator(gltfType);

            try
            {
                var downloadProviderType = gltfType.Assembly.GetType("GLTFast.Loading.IDownloadProvider");
                var deferAgentType = gltfType.Assembly.GetType("GLTFast.IDeferAgent");
                var materialGeneratorType = gltfType.Assembly.GetType("GLTFast.Materials.IMaterialGenerator");
                var codeLoggerType = gltfType.Assembly.GetType("GLTFast.Logging.ICodeLogger");

                if (downloadProviderType != null && deferAgentType != null && materialGeneratorType != null && codeLoggerType != null)
                {
                    var ctor = gltfType.GetConstructor(new[] { downloadProviderType, deferAgentType, materialGeneratorType, codeLoggerType });
                    if (ctor != null)
                    {
                        gltf = ctor.Invoke(new[] { (object)null, null, materialGenerator, null });
                        return gltf != null;
                    }
                }

                // Fall back to any 4-parameter ctor (signature varies by glTFast version).
                var anyCtor = gltfType.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 4);
                if (anyCtor != null)
                {
                    // Best-effort: put generator in the third slot, which matches the common signature.
                    gltf = anyCtor.Invoke(new[] { (object)null, null, materialGenerator, null });
                    return gltf != null;
                }
            }
            catch
            {
                // ignore and try default ctor below
            }

            try
            {
                gltf = Activator.CreateInstance(gltfType);
                if (gltf != null)
                {
                    if (materialGenerator == null)
                    {
                        Debug.Log("[RuntimeModelLoader] GLTFast created with default constructor (no material generator injected).");
                    }
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static object TryCreateMaterialGenerator(Type gltfType)
        {
            try
            {
                var asm = gltfType.Assembly;
                var iMatGen = asm.GetType("GLTFast.Materials.IMaterialGenerator");
                if (iMatGen == null) return null;

                // Detect render pipeline (Built-in / URP / HDRP)
                var rpAsset = GraphicsSettings.currentRenderPipeline != null
                    ? GraphicsSettings.currentRenderPipeline
                    : GraphicsSettings.defaultRenderPipeline;

                string rpName = rpAsset != null ? rpAsset.GetType().FullName : "Built-in";

                // Candidate generator types (names vary slightly by glTFast version).
                var candidates = new List<string>();
                if (rpAsset != null)
                {
                    var upper = rpName != null ? rpName.ToUpperInvariant() : string.Empty;
                    if (upper.Contains("UNIVERSAL"))
                    {
                        candidates.Add("GLTFast.Materials.UniversalRPMaterialGenerator");
                        candidates.Add("GLTFast.Materials.UniversalRPShaderGraphMaterialGenerator");
                    }
                    else if (upper.Contains("HIGHDEFINITION") || upper.Contains("HDRENDERPIPELINE"))
                    {
                        candidates.Add("GLTFast.Materials.HighDefinitionRPMaterialGenerator");
                        candidates.Add("GLTFast.Materials.HighDefinitionRPShaderGraphMaterialGenerator");
                    }
                }
                candidates.Add("GLTFast.Materials.StandardMaterialGenerator");
                candidates.Add("GLTFast.Materials.BuiltInMaterialGenerator");

                foreach (var typeName in candidates.Distinct())
                {
                    var t = asm.GetType(typeName);
                    if (t == null || !iMatGen.IsAssignableFrom(t) || t.IsAbstract) continue;
                    var ctor = t.GetConstructor(Type.EmptyTypes);
                    if (ctor == null) continue;
                    var inst = ctor.Invoke(null);
                    if (inst != null)
                    {
                        Debug.Log($"[RuntimeModelLoader] Using GLTFast material generator: {t.FullName} (RP: {rpName})");
                        return inst;
                    }
                }

                // Last resort: scan for any concrete IMaterialGenerator with a parameterless ctor.
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t == null || t.IsAbstract || !iMatGen.IsAssignableFrom(t)) continue;
                        var ctor = t.GetConstructor(Type.EmptyTypes);
                        if (ctor == null) continue;
                        var inst = ctor.Invoke(null);
                        if (inst != null)
                        {
                            Debug.Log($"[RuntimeModelLoader] Using fallback GLTFast material generator: {t.FullName} (RP: {rpName})");
                            return inst;
                        }
                    }
                }
                catch
                {
                    // ignore
                }

                Debug.LogWarning($"[RuntimeModelLoader] Could not create a GLTFast material generator (RP: {rpName}). Materials may appear missing/pink.");
                return null;
            }
            catch
            {
                return null;
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

            Uri loadUri = null;
            if (Path.IsPathRooted(path))
            {
                // On Windows, passing "C:/..." to Uri can be interpreted as scheme "c:".
                // Always prefer a proper file:// URI.
                try
                {
                    var fullPath = Path.GetFullPath(path);
                    loadUri = new Uri(fullPath);
                    loadPath = loadUri.AbsoluteUri;
                }
                catch
                {
                    // Fall back to the raw path if URI construction fails.
                    loadUri = null;
                    loadPath = path;
                }
            }
            else
            {
                // Relative paths are typically handled by glTFast using its own download provider.
                // Keep as-is.
                try
                {
                    loadUri = new Uri(loadPath, UriKind.RelativeOrAbsolute);
                }
                catch
                {
                    loadUri = null;
                }
            }

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
                        if (loadUri == null) continue;
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
                        if (loadUri == null) continue;
                        task = method.Invoke(gltf, new object[] { loadUri, null, token }) as Task<bool>;
                        break;
                    }
                }
            }
            return task != null;
        }

        private static Task RunOnMainThreadAsync(Action action, CancellationToken token)
        {
            if (action == null) return Task.CompletedTask;
            if (token.IsCancellationRequested) return Task.FromCanceled(token);
            if (IsMainThread())
            {
                action();
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>();
            try
            {
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                {
                    if (token.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(token);
                        return;
                    }
                    try
                    {
                        action();
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });
            }
            catch (Exception ex)
            {
                // If dispatcher is missing, we can't safely touch Unity objects off-thread.
                if (!_loggedDispatcherMissing)
                {
                    _loggedDispatcherMissing = true;
                    Debug.LogWarning("[RuntimeModelLoader] UnityMainThreadDispatcher not available. Runtime model instantiation must run on the main thread.");
                }
                tcs.TrySetException(ex);
            }
            return tcs.Task;
        }

        private static Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> func, CancellationToken token)
        {
            if (func == null) return Task.FromResult(default(T));
            if (token.IsCancellationRequested) return Task.FromCanceled<T>(token);
            if (IsMainThread())
            {
                return func();
            }

            var tcs = new TaskCompletionSource<T>();
            try
            {
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                {
                    _ = InvokeAsync();

                    async Task InvokeAsync()
                    {
                        if (token.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(token);
                            return;
                        }
                        try
                        {
                            var result = await func();
                            tcs.TrySetResult(result);
                        }
                        catch (OperationCanceledException)
                        {
                            tcs.TrySetCanceled(token);
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                if (!_loggedDispatcherMissing)
                {
                    _loggedDispatcherMissing = true;
                    Debug.LogWarning("[RuntimeModelLoader] UnityMainThreadDispatcher not available. Runtime model instantiation must run on the main thread.");
                }
                tcs.TrySetException(ex);
            }
            return tcs.Task;
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

        private static void EnsureRuntimeColliders(Transform root)
        {
            if (root == null) return;
            var content = EnsureContentRoot(root);
            if (content == null) return;

            ClearRuntimeColliders(content, root);
            if (!TryGetLocalBounds(content, out var bounds)) return;

            float minSize = Mathf.Max(0.0001f, Mathf.Min(bounds.size.x, Mathf.Min(bounds.size.y, bounds.size.z)));
            float maxSize = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float aspect = maxSize / minSize;
            bool sphereLike = (maxSize - minSize) / maxSize < 0.15f;

            int parts = 1;
            if (!sphereLike)
            {
                parts = aspect > 2.5f ? 3 : (aspect > 1.4f ? 2 : 1);
            }
            parts = Mathf.Clamp(parts, 1, 3);

            var autoRoot = new GameObject("__AutoColliders");
            autoRoot.transform.SetParent(root, false);

            if (sphereLike)
            {
                var sphere = autoRoot.AddComponent<SphereCollider>();
                sphere.center = bounds.center;
                sphere.radius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
            }
            else
            {
                var renderers = content.GetComponentsInChildren<Renderer>(true);
                var entries = BuildRendererEntries(root, renderers);
                if (entries.Count == 0) return;

                int axis = GetLongestAxis(bounds.size);
                entries.Sort((a, b) => a.center[axis].CompareTo(b.center[axis]));
                int batchSize = Mathf.Max(1, Mathf.CeilToInt(entries.Count / (float)parts));
                for (int i = 0; i < parts; i++)
                {
                    int start = i * batchSize;
                    if (start >= entries.Count) break;
                    int end = Mathf.Min(entries.Count, start + batchSize);
                    var groupBounds = entries[start].bounds;
                    for (int j = start + 1; j < end; j++)
                    {
                        groupBounds.Encapsulate(entries[j].bounds);
                    }
                    var box = autoRoot.AddComponent<BoxCollider>();
                    box.center = groupBounds.center;
                    box.size = groupBounds.size;
                }
            }
        }

        private static void ClearRuntimeColliders(Transform content, Transform root)
        {
            if (content == null || root == null) return;
            var autoRoot = root.Find("__AutoColliders");
            if (autoRoot != null)
            {
                UnityEngine.Object.Destroy(autoRoot.gameObject);
            }
            var colliders = content.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                if (collider == null) continue;
                UnityEngine.Object.Destroy(collider);
            }
        }

        private struct RendererEntry
        {
            public Vector3 center;
            public Bounds bounds;
        }

        private static List<RendererEntry> BuildRendererEntries(Transform reference, Renderer[] renderers)
        {
            var entries = new List<RendererEntry>(renderers.Length);
            var referenceMatrix = reference.worldToLocalMatrix;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var matrix = referenceMatrix * renderer.transform.localToWorldMatrix;
                if (!IsFinite(matrix)) continue;
                var transformed = TransformBounds(renderer.localBounds, matrix);
                if (!IsFinite(transformed.center) || !IsFinite(transformed.size)) continue;
                entries.Add(new RendererEntry
                {
                    center = transformed.center,
                    bounds = transformed
                });
            }
            return entries;
        }

        private static int GetLongestAxis(Vector3 size)
        {
            if (size.x >= size.y && size.x >= size.z) return 0;
            if (size.y >= size.z) return 1;
            return 2;
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
