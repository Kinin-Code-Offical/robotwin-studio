using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Runtime;

namespace RobotTwin.UI
{
    public class Circuit3DView : MonoBehaviour
    {
        private const float DefaultScale = 0.01f;
        private const float WireHeight = 0.02f;
        private const float ComponentHeight = 0.03f;
        private const float BoardWorldWidth = CircuitLayoutSizing.BoardWorldWidth;
        private const float BoardWorldHeight = CircuitLayoutSizing.BoardWorldHeight;
        private const float DefaultAnchorRadius = 0.006f;
        private const string PrefabRoot = "Prefabs/Circuit3D";
        private const string TextureRoot = "Prefabs/Circuit3D/Textures";
        private const string ComponentSettingsResource = "Circuit3D/ComponentSettings";
        private static readonly Color CameraBackgroundColor = new Color(0.34f, 0.36f, 0.46f);
        private static readonly Color WireDefaultColor = new Color(0.2f, 0.8f, 1f, 0.9f);
        private static readonly Color[] WirePalette =
        {
            new Color(0.20f, 0.65f, 0.95f, 0.9f),
            new Color(0.95f, 0.55f, 0.20f, 0.9f),
            new Color(0.35f, 0.85f, 0.45f, 0.9f),
            new Color(0.90f, 0.30f, 0.35f, 0.9f),
            new Color(0.75f, 0.55f, 0.95f, 0.9f),
            new Color(0.95f, 0.85f, 0.25f, 0.9f),
            new Color(0.25f, 0.85f, 0.85f, 0.9f),
            new Color(0.90f, 0.45f, 0.75f, 0.9f),
            new Color(0.55f, 0.90f, 0.25f, 0.9f),
            new Color(0.25f, 0.55f, 0.90f, 0.9f),
            new Color(0.80f, 0.70f, 0.45f, 0.9f),
            new Color(0.55f, 0.55f, 0.60f, 0.9f)
        };

        private Camera _camera;
        private RenderTexture _renderTexture;
        private Transform _root;
        private bool _prefabsLoaded;
        private Transform _lightRoot;
        private Light _keyLight;
        private Light _fillLight;
        private Light _rimLight;
        private Light _headLight;
        private readonly Dictionary<string, AnchorState> _anchorStateCache = new Dictionary<string, AnchorState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ComponentVisual> _componentVisuals = new Dictionary<string, ComponentVisual>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<WireRope>> _wiresByNet = new Dictionary<string, List<WireRope>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Material> TextureMaterials =
            new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<GameObject, Bounds> PrefabBoundsCache = new Dictionary<GameObject, Bounds>();
        private static Dictionary<string, ComponentTuning> ComponentTunings;
        private static bool ComponentTuningsLoaded;
        private static Mesh _usbShellMesh;
        private static Mesh _usbTongueMesh;

        [Header("Prefabs (optional)")]
        [SerializeField] private GameObject _arduinoPrefab;
        [SerializeField] private GameObject _arduinoUnoPrefab;
        [SerializeField] private GameObject _arduinoNanoPrefab;
        [SerializeField] private GameObject _arduinoProMiniPrefab;
        [SerializeField] private GameObject _resistorPrefab;
        [SerializeField] private GameObject _ledPrefab;
        [SerializeField] private GameObject _batteryPrefab;
        [SerializeField] private GameObject _buttonPrefab;
        [SerializeField] private GameObject _switchPrefab;
        [SerializeField] private GameObject _servoPrefab;
        [SerializeField] private GameObject _genericPrefab;

        private Vector2 _scale2D = new Vector2(DefaultScale, DefaultScale);
        private Vector2 _min;
        private Vector2 _max;
        private Vector2 _size;
        private Vector2 _viewportSize;
        private Vector2 _orbitAngles = new Vector2(45f, 0f);
        private float _zoom = 0.5f;
        private float _distance = 1.2f;
        private Vector3 _panOffset = Vector3.zero;
        private bool _hasUserCamera;
        private float _fieldOfView = 55f;
        private bool _usePerspective = true;
        private bool _followTarget;
        private Transform _followTransform;
        private Vector3 _followOffset;
        private string _followComponentId;

        private static readonly Vector3 DefaultPrefabEuler = Vector3.zero;
        private static readonly Dictionary<string, Vector3> PrefabEulerOverrides =
            new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
            {
                { "ArduinoUno", Vector3.zero },
                { "ArduinoNano", Vector3.zero },
                { "ArduinoProMini", Vector3.zero },
                { "Resistor", Vector3.zero },
                { "LED", Vector3.zero },
                { "Battery", Vector3.zero },
                { "Button", Vector3.zero },
                { "Switch", Vector3.zero },
                { "ServoSG90", Vector3.zero }
            };

        public RenderTexture TargetTexture => _renderTexture;

        public void Initialize(int width, int height)
        {
            EnsureCamera();
            EnsureRenderTexture(width, height);
        }

        public void SetPerspective(bool enabled)
        {
            _usePerspective = enabled;
            FrameCamera();
        }

        public void SetFieldOfView(float value)
        {
            _fieldOfView = Mathf.Clamp(value, 30f, 95f);
            if (_camera != null) _camera.fieldOfView = _fieldOfView;
            if (_usePerspective)
            {
                FrameCamera();
            }
            else
            {
                UpdateCameraTransform();
            }
        }

        public void SetFollowComponent(string componentId, bool enabled)
        {
            if (!enabled)
            {
                _followTarget = false;
                _followTransform = null;
                _followComponentId = null;
                return;
            }

            _followComponentId = componentId;
            if (_componentVisuals.TryGetValue(componentId, out var visual) && visual?.Root != null)
            {
                SetFollowTarget(visual.Root, true);
            }
        }

        public void SetFollowTarget(Transform target, bool enabled)
        {
            _followTarget = enabled && target != null;
            _followTransform = _followTarget ? target : null;
            if (_followTransform != null)
            {
                _followOffset = _panOffset - _root.InverseTransformPoint(_followTransform.position);
            }
        }

        public void ResetView()
        {
            FrameCamera();
        }

        public bool FocusOnComponent(string componentId, float padding = 1.4f)
        {
            if (string.IsNullOrWhiteSpace(componentId)) return false;
            if (!_componentVisuals.TryGetValue(componentId, out var visual) || visual?.Root == null) return false;
            if (!TryGetWorldBounds(visual.Root, out var bounds))
            {
                _panOffset = visual.Root.localPosition;
                _hasUserCamera = true;
                UpdateCameraTransform();
                return true;
            }

            var centerLocal = transform.InverseTransformPoint(bounds.center);
            _panOffset = centerLocal;

            if (_usePerspective)
            {
                _distance = ComputePerspectiveDistance(bounds, padding);
            }
            else
            {
                _zoom = ComputeOrthoSize(bounds, padding);
            }
            _hasUserCamera = true;
            UpdateCameraTransform();
            return true;
        }

        public void NudgePanWorld(Vector3 delta)
        {
            _panOffset += delta;
            if (_followTarget) _followOffset += delta;
            _hasUserCamera = true;
            UpdateCameraTransform();
        }

        public float GetKeyboardPanStep()
        {
            if (_usePerspective)
            {
                return Mathf.Max(0.02f, _distance * 0.05f);
            }
            return Mathf.Max(0.02f, _zoom * 0.1f);
        }

        private void LateUpdate()
        {
            if (_followTarget && _followTransform != null && _root != null)
            {
                var target = _root.InverseTransformPoint(_followTransform.position);
                _panOffset = target + _followOffset;
                UpdateCameraTransform();
            }

            if (_headLight != null && _camera != null && _headLight.transform.parent != _camera.transform)
            {
                _headLight.transform.SetParent(_camera.transform, false);
            }

            if (_camera != null)
            {
                foreach (var visual in _componentVisuals.Values)
                {
                    if (visual == null) continue;
                    UpdateBillboardRotation(visual.StatusLabel?.transform);
                    UpdateBillboardRotation(visual.BatteryBar?.Root);
                    UpdateBillboardRotation(visual.TempBar?.Root);
                }
            }
        }

        private void UpdateBillboardRotation(Transform target)
        {
            if (target == null || _camera == null) return;
            var toCamera = _camera.transform.position - target.position;
            if (toCamera.sqrMagnitude < 0.0001f) return;
            target.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
        }

        public void Build(CircuitSpec circuit)
        {
            EnsureRoot();
            EnsurePrefabs();
            EnsureLighting();
            CaptureAnchorState();
            ClearRoot();

            UpdateLayoutScale(circuit);
            ComputeBounds(circuit);
            var positions = BuildComponents(circuit);
            ResolveComponentOverlaps(positions);
            var anchors = BuildAnchors(circuit, positions);
            BuildWires(circuit, anchors);
            if (_followTarget && !string.IsNullOrWhiteSpace(_followComponentId))
            {
                SetFollowComponent(_followComponentId, true);
            }

            if (_hasUserCamera)
            {
                UpdateCameraTransform();
            }
            else
            {
                FrameCamera();
            }
        }

        private void EnsureCamera()
        {
            if (_camera != null) return;
            var go = new GameObject("Circuit3D_Camera");
            go.transform.SetParent(transform, false);
            _camera = go.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = CameraBackgroundColor;
            _camera.orthographic = !_usePerspective;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 5000f;
            _camera.fieldOfView = _fieldOfView;
            if (_headLight != null)
            {
                _headLight.transform.SetParent(_camera.transform, false);
            }
        }

        private void EnsureRenderTexture(int width, int height)
        {
            if (width <= 0 || height <= 0) return;
            _viewportSize = new Vector2(width, height);
            if (_renderTexture != null && _renderTexture.width == width && _renderTexture.height == height)
            {
                return;
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
            }

            _renderTexture = new RenderTexture(width, height, 16)
            {
                name = "Circuit3D_RT",
                antiAliasing = 2
            };
            _renderTexture.Create();
            if (_camera != null) _camera.targetTexture = _renderTexture;
        }

        private void EnsureRoot()
        {
            if (_root != null) return;
            var go = new GameObject("Circuit3D_Root");
            go.transform.SetParent(transform, false);
            _root = go.transform;
        }

        private void EnsureLighting()
        {
            if (_lightRoot != null) return;
            var go = new GameObject("Circuit3D_Lights");
            go.transform.SetParent(transform, false);
            _lightRoot = go.transform;

            _keyLight = CreateDirectionalLight("Circuit3D_KeyLight", new Vector3(50f, -30f, 0f), 0.85f, new Color(0.98f, 0.95f, 0.92f));
            _fillLight = CreateDirectionalLight("Circuit3D_FillLight", new Vector3(20f, 160f, 0f), 0.35f, new Color(0.78f, 0.84f, 0.95f));
            _rimLight = CreateDirectionalLight("Circuit3D_RimLight", new Vector3(75f, 40f, 0f), 0.25f, new Color(0.7f, 0.78f, 0.95f));
            _headLight = CreateHeadLight("Circuit3D_HeadLight");
            if (_camera != null)
            {
                _headLight.transform.SetParent(_camera.transform, false);
            }
        }

        private Light CreateDirectionalLight(string name, Vector3 euler, float intensity, Color color)
        {
            var lightGo = new GameObject(name);
            lightGo.transform.SetParent(_lightRoot, false);
            lightGo.transform.localRotation = Quaternion.Euler(euler);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;
            return light;
        }

        private Light CreateHeadLight(string name)
        {
            var lightGo = new GameObject(name);
            lightGo.transform.SetParent(_lightRoot, false);
            lightGo.transform.localPosition = Vector3.zero;
            lightGo.transform.localRotation = Quaternion.identity;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Spot;
            light.intensity = 0.35f;
            light.range = 2f;
            light.spotAngle = 70f;
            light.color = new Color(0.95f, 0.96f, 1f);
            light.shadows = LightShadows.None;
            return light;
        }

        private void ClearRoot()
        {
            if (_root == null) return;
            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                Destroy(_root.GetChild(i).gameObject);
            }
            _componentVisuals.Clear();
            _wiresByNet.Clear();
        }

        private void UpdateLayoutScale(CircuitSpec circuit)
        {
            _scale2D = new Vector2(DefaultScale, DefaultScale);
            if (circuit?.Components == null || circuit.Components.Count == 0) return;

            var ratiosX = new List<float>();
            var ratiosY = new List<float>();
            foreach (var comp in circuit.Components)
            {
                if (comp == null) continue;
                var prefab = GetPrefabForType(comp.Type);
                if (prefab == null) continue;
                if (!TryGetPrefabBounds(prefab, out var bounds)) continue;
                var tuning = GetComponentTuning(comp.Type ?? string.Empty);
                var scale = tuning.Scale;
                if (scale.x <= 0f) scale.x = 1f;
                if (scale.y <= 0f) scale.y = 1f;
                if (scale.z <= 0f) scale.z = 1f;
                var tunedBounds = TransformBounds(bounds, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(tuning.Euler), scale));
                var size2d = GetComponentSize2D(comp);
                if (size2d.x > 0.01f) ratiosX.Add(tunedBounds.size.x / size2d.x);
                if (size2d.y > 0.01f) ratiosY.Add(tunedBounds.size.z / size2d.y);
            }

            if (ratiosX.Count > 0) _scale2D.x = ClampScale(Median(ratiosX));
            if (ratiosY.Count > 0) _scale2D.y = ClampScale(Median(ratiosY));
        }

        private void ApplyPrefabTuning(GameObject part, ComponentSpec comp)
        {
            if (part == null) return;
            var tuning = GetComponentTuning(comp?.Type ?? string.Empty);
            Vector3 euler = tuning.Euler;

            if (comp?.Properties != null)
            {
                if (TryGetFloat(comp.Properties, "rotX", out var rotX)) euler.x += rotX;
                if (TryGetFloat(comp.Properties, "rotY", out var rotY)) euler.y += rotY;
                if (TryGetFloat(comp.Properties, "rotZ", out var rotZ)) euler.z += rotZ;
                if (TryGetFloat(comp.Properties, "rotationX", out var rotXAlt)) euler.x += rotXAlt;
                if (TryGetFloat(comp.Properties, "rotationY", out var rotYAlt)) euler.y += rotYAlt;
                if (TryGetFloat(comp.Properties, "rotationZ", out var rotZAlt)) euler.z += rotZAlt;
            }

            part.transform.localRotation = Quaternion.Euler(euler);
            var scale = tuning.Scale;
            if (scale.x <= 0f) scale.x = 1f;
            if (scale.y <= 0f) scale.y = 1f;
            if (scale.z <= 0f) scale.z = 1f;
            part.transform.localScale = Vector3.Scale(part.transform.localScale, scale);
        }

        private void CaptureAnchorState()
        {
            _anchorStateCache.Clear();
            if (_root == null) return;
            var anchors = _root.GetComponentsInChildren<WireAnchor>(true);
            foreach (var anchor in anchors)
            {
                if (anchor == null || string.IsNullOrWhiteSpace(anchor.NodeId)) continue;
                _anchorStateCache[anchor.NodeId] = new AnchorState(anchor.transform.localPosition, anchor.Radius);
            }
        }

        private void ComputeBounds(CircuitSpec circuit)
        {
            _min = Vector2.zero;
            _max = new Vector2(BoardWorldWidth, BoardWorldHeight);
            if (circuit?.Components == null || circuit.Components.Count == 0)
            {
                _size = _max - _min;
                return;
            }

            foreach (var comp in circuit.Components)
            {
                if (!TryGetPosition(comp, out var pos)) continue;
                var size = GetComponentSize2D(comp);
                _min = Vector2.Min(_min, pos);
                _max = Vector2.Max(_max, pos + size);
            }
            _size = _max - _min;
        }

        private Dictionary<string, Vector3> BuildComponents(CircuitSpec circuit)
        {
            var positions = new Dictionary<string, Vector3>();
            if (circuit == null || circuit.Components == null) return positions;

            foreach (var comp in circuit.Components)
            {
                if (!TryGetPosition(comp, out var pos)) continue;
                var size2d = GetComponentSize2D(comp);
                var prefab = GetPrefabForType(comp.Type);
                var part = CreateInstance(prefab, PrimitiveType.Cube, $"Part_{comp.Id}");
                bool allowTint = prefab == null;
                if (prefab == null)
                {
                    part.transform.localScale = GetPartScale(comp.Type);
                }
                ApplyPrefabTuning(part, comp);

                Vector3 world = prefab != null
                    ? GetPrefabWorldPosition(pos, size2d, part)
                    : ToWorld(pos + size2d * 0.5f, ComponentHeight);
                part.transform.localPosition = world;
                positions[comp.Id] = GetComponentAnchorPosition(part, world);
                if (prefab == null)
                {
                    var renderer = part.GetComponent<Renderer>();
                    renderer.material.color = GetPartColor(comp.Type);
                }

                if (prefab == null)
                {
                    ApplyTexture(part, GetTextureForType(comp.Type));
                }

                RegisterComponentVisual(comp, part, allowTint);

                var label = new GameObject($"{comp.Id}_Label");
                label.transform.SetParent(part.transform, false);
                label.transform.localPosition = new Vector3(0, 0.03f, 0);
                var text = label.AddComponent<TextMesh>();
                text.text = comp.Id;
                text.fontSize = 32;
                text.characterSize = 0.02f;
                text.color = Color.white;
                text.alignment = TextAlignment.Center;
                text.anchor = TextAnchor.MiddleCenter;
            }

            return positions;
        }

        private void ResolveComponentOverlaps(Dictionary<string, Vector3> positions)
        {
            if (_componentVisuals.Count < 2) return;
            var visuals = _componentVisuals.Values.Where(v => v?.Root != null).ToList();
            if (visuals.Count < 2) return;

            const int maxPasses = 6;
            const float padding = 0.0015f;
            for (int pass = 0; pass < maxPasses; pass++)
            {
                bool moved = false;
                for (int i = 0; i < visuals.Count; i++)
                {
                    var a = visuals[i];
                    if (a?.Root == null) continue;
                    if (!TryGetWorldBounds(a.Root, out var boundsA)) continue;
                    for (int j = i + 1; j < visuals.Count; j++)
                    {
                        var b = visuals[j];
                        if (b?.Root == null) continue;
                        if (!TryGetWorldBounds(b.Root, out var boundsB)) continue;
                        if (!BoundsOverlapXZ(boundsA, boundsB, padding)) continue;

                        float overlapX = Mathf.Min(boundsA.max.x - boundsB.min.x, boundsB.max.x - boundsA.min.x);
                        float overlapZ = Mathf.Min(boundsA.max.z - boundsB.min.z, boundsB.max.z - boundsA.min.z);
                        var dir = boundsA.center - boundsB.center;
                        if (Mathf.Abs(dir.x) < 0.0001f) dir.x = 0.01f;
                        if (Mathf.Abs(dir.z) < 0.0001f) dir.z = 0.01f;

                        Vector3 delta;
                        if (overlapX < overlapZ)
                        {
                            float sign = Mathf.Sign(dir.x);
                            delta = new Vector3(sign * (overlapX + padding) * 0.5f, 0f, 0f);
                        }
                        else
                        {
                            float sign = Mathf.Sign(dir.z);
                            delta = new Vector3(0f, 0f, sign * (overlapZ + padding) * 0.5f);
                        }

                        a.Root.position += delta;
                        b.Root.position -= delta;
                        moved = true;
                    }
                }

                if (!moved) break;
            }

            if (positions == null) return;
            foreach (var visual in visuals)
            {
                if (visual?.Root == null) continue;
                positions[visual.Id] = GetComponentAnchorPosition(visual.Root.gameObject, visual.Root.localPosition);
            }
        }

        private static bool BoundsOverlapXZ(Bounds a, Bounds b, float padding)
        {
            if (a.max.x + padding <= b.min.x) return false;
            if (b.max.x + padding <= a.min.x) return false;
            if (a.max.z + padding <= b.min.z) return false;
            if (b.max.z + padding <= a.min.z) return false;
            return true;
        }

        private void RegisterComponentVisual(ComponentSpec comp, GameObject part, bool allowTint)
        {
            if (comp == null || part == null) return;
            var renderers = part.GetComponentsInChildren<Renderer>(true);
            var visual = new ComponentVisual
            {
                Id = comp.Id,
                Type = comp.Type,
                Root = part.transform,
                Renderers = renderers,
                Block = new MaterialPropertyBlock(),
                BaseColor = GetRendererBaseColor(renderers, GetPartColor(comp.Type)),
                BasePosition = part.transform.localPosition,
                BaseRotation = part.transform.localRotation,
                IsLed = IsLedType(comp.Type),
                IsResistor = IsResistorType(comp.Type),
                IsSwitch = IsSwitchType(comp.Type),
                IsButton = IsButtonType(comp.Type),
                IsArduino = IsArduinoType(comp.Type),
                IsBattery = IsBatteryType(comp.Type),
                IsServo = IsServoType(comp.Type),
                ErrorSeed = UnityEngine.Random.Range(0f, 10f),
                Tuning = GetComponentTuning(comp.Type ?? string.Empty),
                AllowTint = allowTint
            };

            if (visual.IsLed && visual.Tuning.UseLedColor)
            {
                visual.BaseColor = visual.Tuning.LedColor;
            }
            if (visual.IsBattery && visual.BaseColor.grayscale < 0.18f)
            {
                visual.BaseColor = new Color(0.25f, 0.26f, 0.3f);
            }

            EnsureComponentCollider(part, renderers);
            var idTag = part.GetComponent<Circuit3DComponentId>();
            if (idTag == null) idTag = part.AddComponent<Circuit3DComponentId>();
            idTag.Initialize(comp.Id, comp.Type);

            if (visual.IsLed)
            {
                EnsureLedGlow(visual);
            }

            if (visual.IsArduino)
            {
                EnsureUsbIndicator(visual);
            }

            if (visual.IsServo)
            {
                FindServoArm(visual);
            }

            if (visual.IsArduino || visual.IsBattery || visual.IsResistor)
            {
                EnsureStatusLabel(visual);
            }

            if (visual.IsBattery)
            {
                EnsureBatteryBar(visual);
            }

            if (visual.IsArduino)
            {
                EnsureTempBar(visual);
            }

            if (visual.IsResistor)
            {
                visual.LegRenderers = FindLegRenderers(renderers);
            }

            _componentVisuals[comp.Id] = visual;
        }

        private static Renderer[] FindLegRenderers(Renderer[] renderers)
        {
            if (renderers == null) return null;
            var legs = new List<Renderer>();
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var name = renderer.name.ToLowerInvariant();
                if (name.Contains("leg") || name.Contains("pin") || name.Contains("lead"))
                {
                    legs.Add(renderer);
                }
            }
            return legs.Count > 0 ? legs.ToArray() : null;
        }

        private void EnsureComponentCollider(GameObject part, Renderer[] renderers)
        {
            if (part == null) return;
            if (renderers == null || renderers.Length == 0) return;

            RemoveBoxColliders(part);
            if (HasSolidCollider(part)) return;
            EnsureMeshColliders(part);
        }

        private static void EnsureMeshColliders(GameObject root)
        {
            if (root == null) return;
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var filter in filters)
            {
                if (filter == null || filter.sharedMesh == null) continue;
                var collider = filter.GetComponent<Collider>();
                if (collider != null && !collider.isTrigger) continue;
                var meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = filter.sharedMesh;
                meshCollider.convex = CanUseConvexMesh(filter.sharedMesh);
            }
        }

        private static bool CanUseConvexMesh(Mesh mesh)
        {
            if (mesh == null) return false;
            if (!mesh.isReadable) return false;
            int triangleCount = mesh.triangles != null ? mesh.triangles.Length / 3 : 0;
            return triangleCount > 0 && triangleCount <= 255;
        }

        private static void RemoveBoxColliders(GameObject root)
        {
            if (root == null) return;
            var boxes = root.GetComponentsInChildren<BoxCollider>(true);
            foreach (var box in boxes)
            {
                if (box == null) continue;
                box.enabled = false;
                if (Application.isPlaying)
                {
                    Destroy(box);
                }
                else
                {
                    DestroyImmediate(box);
                }
            }
        }

        private static void DisableBoxColliders(GameObject root)
        {
            if (root == null) return;
            var boxes = root.GetComponentsInChildren<BoxCollider>(true);
            foreach (var box in boxes)
            {
                if (box == null) continue;
                box.enabled = false;
            }
        }

        private static bool HasSolidCollider(GameObject root)
        {
            if (root == null) return false;
            var colliders = root.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                if (collider != null && !collider.isTrigger) return true;
            }
            return false;
        }


        private void EnsureLedGlow(ComponentVisual visual)
        {
            if (visual == null || visual.GlowLight != null) return;
            var glow = new GameObject("LED_Glow");
            glow.transform.SetParent(visual.Root, false);
            glow.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            float range = visual.Tuning.LedGlowRange > 0f ? visual.Tuning.LedGlowRange : 0.08f;
            light.range = range;
            light.intensity = 0f;
            light.color = visual.BaseColor;
            light.shadows = LightShadows.None;
            visual.GlowLight = light;
        }

        private void EnsureStatusLabel(ComponentVisual visual)
        {
            if (visual == null || visual.Root == null || visual.StatusLabel != null) return;
            var labelGo = new GameObject("StatusLabel");
            labelGo.transform.SetParent(visual.Root, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            var text = labelGo.AddComponent<TextMesh>();
            text.text = string.Empty;
            text.fontSize = 48;
            text.characterSize = 0.02f;
            text.color = new Color(0.9f, 0.95f, 1f);
            text.alignment = TextAlignment.Center;
            text.anchor = TextAnchor.MiddleCenter;
            visual.StatusLabel = text;
            visual.LabelBaseScale = labelGo.transform.localScale;
        }

        private void EnsureBatteryBar(ComponentVisual visual)
        {
            if (visual == null || visual.Root == null || visual.BatteryBar != null) return;
            visual.BatteryBar = CreateBillboardBar("BatteryBar", visual.Root, new Vector3(0f, 0.035f, 0f),
                new Color(0.12f, 0.14f, 0.2f, 0.8f), new Color(0.25f, 0.85f, 0.4f, 0.9f));
        }

        private void EnsureTempBar(ComponentVisual visual)
        {
            if (visual == null || visual.Root == null || visual.TempBar != null) return;
            visual.TempBar = CreateBillboardBar("TempBar", visual.Root, new Vector3(0f, 0.02f, 0f),
                new Color(0.12f, 0.14f, 0.2f, 0.8f), new Color(0.95f, 0.35f, 0.2f, 0.9f));
        }

        private void EnsureSmokeFx(ComponentVisual visual)
        {
            if (visual == null || visual.Root == null || visual.SmokeFx != null) return;
            var fx = CreateFx("SmokeFx", visual.Root, new Vector3(0f, 0.02f, 0f), new Color(0.35f, 0.35f, 0.35f),
                Color.black, 0f, 0f, 0.02f);
            visual.SmokeFx = fx;
        }

        private void EnsureHeatFx(ComponentVisual visual)
        {
            if (visual == null || visual.Root == null || visual.HeatFx != null) return;
            var fx = CreateFx("HeatFx", visual.Root, new Vector3(0f, 0.015f, 0f), new Color(1f, 0.35f, 0.12f),
                new Color(1f, 0.35f, 0.12f), 1.2f, 0.08f, 0.01f);
            visual.HeatFx = fx;
        }

        private void EnsureSparkFx(ComponentVisual visual)
        {
            if (visual == null || visual.Root == null || visual.SparkFx != null) return;
            var fx = CreateFx("SparkFx", visual.Root, new Vector3(0f, 0.02f, 0f), new Color(1f, 0.75f, 0.25f),
                new Color(1f, 0.75f, 0.25f), 2.4f, 0.12f, 0.008f);
            visual.SparkFx = fx;
        }

        private void EnsureErrorFx(ComponentVisual visual)
        {
            if (visual == null || visual.Root == null || visual.ErrorFx != null) return;
            var fx = CreateFx("ErrorFx", visual.Root, new Vector3(0f, 0.03f, 0f), new Color(1f, 0.2f, 0.2f),
                new Color(1f, 0.2f, 0.2f), 1.6f, 0.1f, 0.012f);
            visual.ErrorFx = fx;
        }

        private void EnsureUsbIndicator(ComponentVisual visual)
        {
            if (visual == null || visual.UsbRenderer != null) return;
            var usb = new GameObject("USB_Indicator");
            usb.transform.SetParent(visual.Root, false);
            usb.transform.localPosition = new Vector3(0f, 0.012f, -0.05f);
            usb.transform.localRotation = Quaternion.identity;
            usb.transform.localScale = Vector3.one;

            var shell = CreateUsbPart("USB_Shell", usb.transform, GetUsbShellMesh());
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localRotation = Quaternion.identity;

            var tongue = CreateUsbPart("USB_Tongue", usb.transform, GetUsbTongueMesh());
            tongue.transform.localPosition = new Vector3(0f, -0.0015f, 0.001f);
            tongue.transform.localRotation = Quaternion.identity;
            var tongueRenderer = tongue.GetComponent<Renderer>();
            if (tongueRenderer != null)
            {
                var block = new MaterialPropertyBlock();
                var tongueColor = new Color(0.2f, 0.2f, 0.22f);
                block.SetColor("_Color", tongueColor);
                block.SetColor("_BaseColor", tongueColor);
                block.SetColor("_EmissionColor", Color.black);
                tongueRenderer.SetPropertyBlock(block);
            }

            visual.UsbIndicator = usb.transform;
            visual.UsbRenderer = shell.GetComponent<Renderer>();
            visual.UsbLight = usb.AddComponent<Light>();
            visual.UsbLight.type = LightType.Point;
            visual.UsbLight.range = 0.09f;
            visual.UsbLight.intensity = 0f;
            visual.UsbLight.color = new Color(0.2f, 0.8f, 1f);
            visual.UsbLight.shadows = LightShadows.None;
        }

        private GameObject CreateUsbPart(string name, Transform parent, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private static Mesh GetUsbShellMesh()
        {
            if (_usbShellMesh != null) return _usbShellMesh;
            _usbShellMesh = BuildBoxMesh(new Vector3(0.022f, 0.008f, 0.018f));
            _usbShellMesh.name = "USB_Shell_Mesh";
            return _usbShellMesh;
        }

        private static Mesh GetUsbTongueMesh()
        {
            if (_usbTongueMesh != null) return _usbTongueMesh;
            _usbTongueMesh = BuildBoxMesh(new Vector3(0.014f, 0.003f, 0.012f));
            _usbTongueMesh.name = "USB_Tongue_Mesh";
            return _usbTongueMesh;
        }

        private void FindServoArm(ComponentVisual visual)
        {
            if (visual == null || visual.Root == null) return;
            Transform arm = null;
            foreach (var t in visual.Root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                var name = t.name.ToLowerInvariant();
                if (name.Contains("arm") || name.Contains("horn"))
                {
                    arm = t;
                    break;
                }
            }

            if (arm == null) arm = visual.Root;
            visual.ServoArm = arm;
            visual.ServoArmBaseRotation = arm.localRotation;
        }

        private static Color GetRendererBaseColor(Renderer[] renderers, Color fallback)
        {
            if (renderers == null) return fallback;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var mat = renderer.sharedMaterial;
                if (mat == null) continue;
                if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
                if (mat.HasProperty("_Color")) return mat.GetColor("_Color");
            }
            return fallback;
        }

        private Dictionary<string, WireAnchor> BuildAnchors(CircuitSpec circuit, Dictionary<string, Vector3> positions)
        {
            var anchors = new Dictionary<string, WireAnchor>(StringComparer.OrdinalIgnoreCase);
            if (circuit?.Nets == null) return anchors;
            foreach (var net in circuit.Nets)
            {
                if (net?.Nodes == null) continue;
                foreach (var node in net.Nodes)
                {
                    if (string.IsNullOrWhiteSpace(node) || anchors.ContainsKey(node)) continue;
                    var compId = GetComponentId(node);
                    var pin = GetPinName(node);
                    float radius = DefaultAnchorRadius;
                    bool hasOverride = TryGetAnchorOverride(circuit, compId, pin, out var position, out radius);
                    if (!hasOverride &&
                        !TryGetPrefabAnchor(compId, pin, out position, out radius) &&
                        !positions.TryGetValue(compId, out position))
                    {
                        position = new Vector3(0f, ComponentHeight, 0f);
                    }

                    var anchor = CreateAnchor(node, position, radius, useCache: !hasOverride);
                    anchors[node] = anchor;
                }
            }

            return anchors;
        }

        private WireAnchor CreateAnchor(string nodeId, Vector3 position, float radius, bool useCache)
        {
            var anchorGo = new GameObject($"Anchor_{nodeId}");
            anchorGo.transform.SetParent(_root, false);
            anchorGo.transform.localPosition = position;
            var anchor = anchorGo.AddComponent<WireAnchor>();

            if (useCache && _anchorStateCache.TryGetValue(nodeId, out var state))
            {
                anchor.Initialize(nodeId, state.Radius);
                anchorGo.transform.localPosition = state.LocalPosition;
            }
            else
            {
                anchor.Initialize(nodeId, radius);
            }

            return anchor;
        }

        public void ApplyAnchorOverrides(CircuitSpec circuit)
        {
            if (_root == null || circuit?.Components == null) return;
            var componentMap = circuit.Components
                .Where(comp => comp != null && !string.IsNullOrWhiteSpace(comp.Id))
                .ToDictionary(comp => comp.Id, StringComparer.OrdinalIgnoreCase);

            var anchors = _root.GetComponentsInChildren<WireAnchor>(true);
            foreach (var anchor in anchors)
            {
                if (anchor == null || string.IsNullOrWhiteSpace(anchor.NodeId)) continue;
                string compId = GetComponentId(anchor.NodeId);
                string pin = GetPinName(anchor.NodeId);
                if (string.IsNullOrWhiteSpace(compId) || string.IsNullOrWhiteSpace(pin)) continue;
                if (!componentMap.TryGetValue(compId, out var comp) || comp == null) continue;
                if (comp.Properties == null) comp.Properties = new Dictionary<string, string>();

                Vector3 localPos = anchor.transform.localPosition;
                if (_componentVisuals.TryGetValue(compId, out var visual) && visual?.Root != null)
                {
                    localPos = visual.Root.InverseTransformPoint(anchor.transform.position);
                }

                SetAnchorProperty(comp.Properties, pin, "x", localPos.x);
                SetAnchorProperty(comp.Properties, pin, "y", localPos.y);
                SetAnchorProperty(comp.Properties, pin, "z", localPos.z);
                SetAnchorProperty(comp.Properties, pin, "r", anchor.Radius);
            }
        }

        private bool TryGetPrefabAnchor(string compId, string pin, out Vector3 rootLocalPosition, out float radius)
        {
            rootLocalPosition = Vector3.zero;
            radius = DefaultAnchorRadius;
            if (string.IsNullOrWhiteSpace(compId) || string.IsNullOrWhiteSpace(pin)) return false;
            if (_root == null) return false;
            if (!_componentVisuals.TryGetValue(compId, out var visual) || visual?.Root == null) return false;

            var pinTransform = FindPinTransform(visual.Root, pin);
            if (pinTransform == null) return false;

            var anchor = pinTransform.GetComponent<WireAnchor>();
            if (anchor != null)
            {
                radius = anchor.Radius;
            }
            else
            {
                var scale = pinTransform.lossyScale;
                float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                if (maxScale > 0.0001f && maxScale < 0.5f)
                {
                    radius = maxScale * 0.5f;
                }
            }

            rootLocalPosition = _root.InverseTransformPoint(pinTransform.position);
            return true;
        }

        private bool TryGetAnchorOverride(CircuitSpec circuit, string compId, string pin, out Vector3 rootLocalPosition, out float radius)
        {
            rootLocalPosition = Vector3.zero;
            radius = DefaultAnchorRadius;
            if (circuit?.Components == null) return false;
            if (string.IsNullOrWhiteSpace(compId) || string.IsNullOrWhiteSpace(pin)) return false;
            var comp = circuit.Components.FirstOrDefault(c => string.Equals(c.Id, compId, StringComparison.OrdinalIgnoreCase));
            if (comp?.Properties == null) return false;

            if (!TryGetAnchorProperty(comp.Properties, pin, "x", out var x) ||
                !TryGetAnchorProperty(comp.Properties, pin, "y", out var y))
            {
                return false;
            }

            TryGetAnchorProperty(comp.Properties, pin, "z", out var z);
            TryGetAnchorProperty(comp.Properties, pin, "r", out radius);

            var local = new Vector3(x, y, z);
            if (_componentVisuals.TryGetValue(compId, out var visual) && visual?.Root != null && _root != null)
            {
                rootLocalPosition = _root.InverseTransformPoint(visual.Root.TransformPoint(local));
            }
            else
            {
                rootLocalPosition = local;
            }
            return true;
        }

        private static void SetAnchorProperty(Dictionary<string, string> props, string pin, string suffix, float value)
        {
            if (props == null || string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(suffix)) return;
            string key = $"anchor.{pin}.{suffix}";
            props[key] = value.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryGetAnchorProperty(Dictionary<string, string> props, string pin, string suffix, out float value)
        {
            value = 0f;
            if (props == null || string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(suffix)) return false;
            string key = $"anchor.{pin}.{suffix}";
            if (TryGetFloat(props, key, out value)) return true;
            string lowerKey = $"anchor.{pin.Trim().ToLowerInvariant()}.{suffix}";
            return TryGetFloat(props, lowerKey, out value);
        }

        private static Transform FindPinTransform(Transform root, string pin)
        {
            if (root == null || string.IsNullOrWhiteSpace(pin)) return null;
            string normalized = pin.Trim().ToLowerInvariant();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                string name = t.name.ToLowerInvariant();
                if (name == normalized ||
                    name == $"pin_{normalized}" ||
                    name == $"pin-{normalized}" ||
                    name == $"anchor_{normalized}" ||
                    name == $"wireanchor_{normalized}")
                {
                    return t;
                }
            }
            return null;
        }

        private void BuildWires(CircuitSpec circuit, Dictionary<string, WireAnchor> anchors)
        {
            if (circuit == null || circuit.Nets == null) return;
            _wiresByNet.Clear();
            foreach (var net in circuit.Nets)
            {
                if (net?.Nodes == null || net.Nodes.Count < 2) continue;
                var nodes = net.Nodes.Where(node => !string.IsNullOrWhiteSpace(node)).Distinct().ToList();
                if (nodes.Count < 2) continue;
                var wireColor = GetNetColor(net.Id);

                for (int i = 0; i < nodes.Count - 1; i++)
                {
                    if (!anchors.TryGetValue(nodes[i], out var a)) continue;
                    if (!anchors.TryGetValue(nodes[i + 1], out var b)) continue;
                    var wire = CreateWire(a, b, wireColor);
                    if (wire != null && !string.IsNullOrWhiteSpace(net.Id))
                    {
                        if (!_wiresByNet.TryGetValue(net.Id, out var list))
                        {
                            list = new List<WireRope>();
                            _wiresByNet[net.Id] = list;
                        }
                        list.Add(wire);
                    }
                }
            }
        }

        private WireRope CreateWire(WireAnchor start, WireAnchor end, Color color)
        {
            var go = new GameObject("Wire");
            go.transform.SetParent(_root, false);
            var rope = go.AddComponent<WireRope>();
            rope.Initialize(start, end);
            rope.SetColor(color);
            return rope;
        }

        public void UpdateTelemetry(CircuitSpec circuit, TelemetryFrame telemetry, IReadOnlyDictionary<string, bool> usbConnectedByBoard)
        {
            if (_componentVisuals.Count == 0) return;
            var components = circuit?.Components;
            var componentMap = components != null
                ? components.Where(comp => comp != null && !string.IsNullOrWhiteSpace(comp.Id))
                    .ToDictionary(comp => comp.Id, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ComponentSpec>(StringComparer.OrdinalIgnoreCase);

            var errorIds = BuildErrorSet(components, telemetry);
            foreach (var visual in _componentVisuals.Values)
            {
                componentMap.TryGetValue(visual.Id, out var spec);
                bool hasError = errorIds.Contains(visual.Id);
                bool usbConnected = usbConnectedByBoard != null &&
                    usbConnectedByBoard.TryGetValue(visual.Id, out var connected) && connected;
                ApplyComponentState(visual, spec, telemetry, usbConnected, hasError);
            }

            UpdateWireErrors(circuit, telemetry);
        }

        public bool TryPickComponent(Vector2 viewportPoint, out string componentId, out string componentType)
        {
            componentId = null;
            componentType = null;
            if (_camera == null) return false;
            if (viewportPoint.x < 0f || viewportPoint.x > 1f || viewportPoint.y < 0f || viewportPoint.y > 1f)
            {
                return false;
            }

            var ray = _camera.ViewportPointToRay(new Vector3(viewportPoint.x, viewportPoint.y, 0f));
            float maxDistance = Mathf.Max(10f, _camera.farClipPlane);
            var hits = Physics.RaycastAll(ray, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return false;
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var tag = hit.collider.GetComponentInParent<Circuit3DComponentId>();
                if (tag == null) continue;
                if (_root != null && !tag.transform.IsChildOf(_root)) continue;
                if (string.IsNullOrWhiteSpace(tag.ComponentId)) continue;
                componentId = tag.ComponentId;
                componentType = tag.ComponentType;
                return true;
            }
            return false;
        }

        private static HashSet<string> BuildErrorSet(List<ComponentSpec> components, TelemetryFrame telemetry)
        {
            var errors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (components == null || telemetry?.ValidationMessages == null) return errors;
            foreach (var msg in telemetry.ValidationMessages)
            {
                if (string.IsNullOrWhiteSpace(msg)) continue;
                foreach (var comp in components)
                {
                    if (comp == null || string.IsNullOrWhiteSpace(comp.Id)) continue;
                    if (msg.IndexOf(comp.Id, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        errors.Add(comp.Id);
                    }
                }
            }
            return errors;
        }

        private void UpdateWireErrors(CircuitSpec circuit, TelemetryFrame telemetry)
        {
            if (_wiresByNet.Count == 0) return;
            var errorNets = BuildErrorNetSet(circuit?.Nets, telemetry);
            foreach (var kvp in _wiresByNet)
            {
                bool hasError = errorNets.Contains(kvp.Key);
                var list = kvp.Value;
                if (list == null) continue;
                foreach (var wire in list)
                {
                    if (wire == null) continue;
                    wire.SetError(hasError);
                }
            }
        }

        private static HashSet<string> BuildErrorNetSet(List<NetSpec> nets, TelemetryFrame telemetry)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (nets == null || telemetry?.ValidationMessages == null) return result;
            foreach (var msg in telemetry.ValidationMessages)
            {
                if (string.IsNullOrWhiteSpace(msg)) continue;
                foreach (var net in nets)
                {
                    if (net == null || string.IsNullOrWhiteSpace(net.Id)) continue;
                    if (msg.IndexOf(net.Id, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Add(net.Id);
                    }
                }
            }
            return result;
        }

        private void ApplyComponentState(ComponentVisual visual, ComponentSpec spec, TelemetryFrame telemetry, bool usbConnected, bool hasError)
        {
            if (visual == null) return;
            Color baseColor = visual.BaseColor;
            Color displayColor = baseColor;
            Color emissionColor = Color.black;
            float tempC = float.NaN;
            float currentA = 0f;
            bool hasTemp = false;
            bool hasCurrent = false;
            string labelText = null;
            Color labelColor = new Color(0.9f, 0.95f, 1f);
            float resistorHeat = 0f;

            if (telemetry != null && telemetry.Signals.TryGetValue($"COMP:{visual.Id}:T", out var tempRaw))
            {
                tempC = (float)tempRaw;
                hasTemp = true;
            }

            if (telemetry != null && telemetry.Signals.TryGetValue($"COMP:{visual.Id}:I", out var currentRaw))
            {
                currentA = Mathf.Abs((float)currentRaw);
                hasCurrent = true;
            }

            if (visual.IsResistor)
            {
                if (hasTemp)
                {
                    float hotStart = visual.Tuning.ResistorHotStartTemp > 0f ? visual.Tuning.ResistorHotStartTemp : 60f;
                    float hotFull = visual.Tuning.ResistorHotFullTemp > 0f ? visual.Tuning.ResistorHotFullTemp : 140f;
                    resistorHeat = Mathf.InverseLerp(hotStart, hotFull, tempC);
                    UpdateHeatFx(visual, resistorHeat);
                }
                else
                {
                    UpdateHeatFx(visual, 0f);
                }
                UpdateResistorSmoke(visual, hasTemp ? tempC : float.NaN);
            }

            if (visual.IsLed)
            {
                var tuning = visual.Tuning;
                baseColor = tuning.UseLedColor ? tuning.LedColor : new Color(1f, 0.2f, 0.2f);
                float intensity = 0f;
                if (telemetry != null && telemetry.Signals.TryGetValue($"COMP:{visual.Id}:L", out var lum))
                {
                    intensity = Mathf.Clamp01((float)lum);
                }
                else if (hasCurrent)
                {
                    intensity = Mathf.Clamp01(currentA * 25f);
                }

                bool ledBlown = visual.LedBlown;
                float blowCurrent = tuning.LedBlowCurrent > 0f ? tuning.LedBlowCurrent : 0.08f;
                float blowTemp = tuning.LedBlowTemp > 0f ? tuning.LedBlowTemp : 140f;
                if (!ledBlown && (currentA > blowCurrent || (hasTemp && tempC > blowTemp)))
                {
                    visual.LedBlown = true;
                    visual.LedBlowTime = Time.time;
                    EnsureSparkFx(visual);
                    TriggerSparkFx(visual);
                    ledBlown = true;
                }

                if (ledBlown)
                {
                    intensity = 0f;
                }
                else
                {
                    float flicker = 0.85f + 0.15f * Mathf.Sin(Time.time * 18f + visual.ErrorSeed);
                    intensity = Mathf.Clamp01(intensity * flicker);
                }

                displayColor = Color.Lerp(baseColor * 0.7f, baseColor, intensity);
                emissionColor += baseColor * (intensity * 3.2f);
                if (ledBlown)
                {
                    displayColor = Color.Lerp(displayColor, new Color(0.12f, 0.12f, 0.12f), 0.6f);
                    emissionColor = Color.black;
                }
                if (visual.GlowLight != null)
                {
                    visual.GlowLight.color = baseColor;
                    float glowRange = tuning.LedGlowRange > 0f ? tuning.LedGlowRange : visual.GlowLight.range;
                    visual.GlowLight.range = glowRange;
                    float glowIntensity = tuning.LedGlowIntensity > 0f ? tuning.LedGlowIntensity : 2.5f;
                    visual.GlowLight.intensity = intensity * glowIntensity;
                    visual.GlowLight.enabled = intensity > 0.01f;
                }
            }

            if (visual.IsArduino)
            {
                if (visual.UsbRenderer != null)
                {
                    var usbColor = usbConnected ? new Color(0.2f, 0.8f, 1f) : new Color(0.15f, 0.15f, 0.18f);
                    var usbBlock = visual.Block ?? new MaterialPropertyBlock();
                    usbBlock.Clear();
                    usbBlock.SetColor("_Color", usbColor);
                    usbBlock.SetColor("_BaseColor", usbColor);
                    usbBlock.SetColor("_EmissionColor", usbConnected ? usbColor * 1.5f : Color.black);
                    visual.UsbRenderer.SetPropertyBlock(usbBlock);
                    visual.UsbRenderer.enabled = true;
                }
                if (visual.UsbLight != null)
                {
                    visual.UsbLight.intensity = usbConnected ? 1.2f : 0f;
                    visual.UsbLight.enabled = usbConnected;
                }
            }

            if (visual.IsSwitch || visual.IsButton)
            {
                bool closed = IsSwitchClosed(spec);
                if (visual.IsSwitch)
                {
                    float switchAngle = closed ? -5f : 20f;
                    visual.Root.localRotation = visual.BaseRotation * Quaternion.Euler(switchAngle, 0f, 0f);
                }
                else if (visual.IsButton)
                {
                    visual.Root.localPosition = visual.BasePosition + (closed ? Vector3.down * 0.004f : Vector3.zero);
                }
            }

            if (visual.IsServo && TryGetServoAngle(spec, out var servoAngle))
            {
                var arm = visual.ServoArm ?? visual.Root;
                arm.localRotation = visual.ServoArmBaseRotation * Quaternion.Euler(0f, servoAngle, 0f);
            }

            if (hasError && !visual.IsArduino && !visual.IsBattery)
            {
                // Error visuals are handled via FX overlay instead of tinting base materials.
            }

            if (visual.StatusLabel != null)
            {
                if (visual.IsArduino)
                {
                    labelText = usbConnected ? "USB ON" : "USB OFF";
                    labelColor = usbConnected ? new Color(0.3f, 0.9f, 0.6f) : Color.gray;
                    if (hasTemp)
                    {
                        labelText = $"{labelText}\nT:{tempC:F0}C";
                    }
                }
                else if (visual.IsBattery)
                {
                    double voltage = double.NaN;
                    double current = double.NaN;
                    double soc = double.NaN;
                    if (TryGetTelemetrySignal(telemetry, $"SRC:{visual.Id}:V", out var v))
                    {
                        voltage = v;
                    }
                    else if (TryGetTelemetrySignal(telemetry, $"COMP:{visual.Id}:V", out var vAlt))
                    {
                        voltage = vAlt;
                    }
                    if (TryGetTelemetrySignal(telemetry, $"SRC:{visual.Id}:I", out var i))
                    {
                        current = i;
                    }
                    if (TryGetTelemetrySignal(telemetry, $"SRC:{visual.Id}:SOC", out var socVal))
                    {
                        soc = socVal;
                    }

                    var lines = new List<string>();
                    if (!double.IsNaN(voltage)) lines.Add($"{voltage:F2}V");
                    if (!double.IsNaN(current)) lines.Add($"{current * 1000.0:F0}mA");
                    if (!double.IsNaN(soc)) lines.Add($"SOC {soc * 100.0:F0}%");
                    labelText = lines.Count > 0 ? string.Join("\n", lines) : "BATTERY";
                    labelColor = Color.white;
                }
                else if (visual.IsResistor)
                {
                    if (hasCurrent || hasTemp)
                    {
                        string currentText = hasCurrent ? $"I:{currentA * 1000f:F1}mA" : "I:N/A";
                        string tempText = hasTemp ? $"T:{tempC:F0}C" : "T:N/A";
                        labelText = $"{currentText}\n{tempText}";
                    }
                    else
                    {
                        labelText = "OK";
                    }
                    labelColor = Color.white;
                }
            }

            if (hasError)
            {
                labelColor = new Color(1f, 0.25f, 0.2f);
                labelText = string.IsNullOrWhiteSpace(labelText) ? "ERROR" : $"{labelText}\nERROR";
            }

            ApplyRendererColors(visual, displayColor, emissionColor);
            if (visual.IsResistor)
            {
                ApplyResistorLegColors(visual, resistorHeat);
            }
            UpdateErrorFx(visual, hasError);
            UpdateSparkFx(visual);
            UpdateStatusLabel(visual, labelText, labelColor);
            UpdateBillboardBars(visual, tempC, hasTemp, telemetry);
        }

        private void ApplyRendererColors(ComponentVisual visual, Color baseColor, Color emissionColor)
        {
            if (visual?.Renderers == null) return;
            var block = visual.Block ?? new MaterialPropertyBlock();
            foreach (var renderer in visual.Renderers)
            {
                if (renderer == null) continue;
                bool hasTexture = RendererHasTexture(renderer);
                block.Clear();
                if (visual.AllowTint && !hasTexture)
                {
                    block.SetColor("_Color", baseColor);
                    block.SetColor("_BaseColor", baseColor);
                }
                block.SetColor("_EmissionColor", emissionColor);
                renderer.SetPropertyBlock(block);
            }
        }

        private void UpdateResistorSmoke(ComponentVisual visual, float tempC)
        {
            if (visual == null) return;
            if (float.IsNaN(tempC))
            {
                SetFxActive(visual.SmokeFx, false);
                return;
            }

            float smokeStart = visual.Tuning.ResistorSmokeStartTemp > 0f ? visual.Tuning.ResistorSmokeStartTemp : 110f;
            float smokeFull = visual.Tuning.ResistorSmokeFullTemp > 0f ? visual.Tuning.ResistorSmokeFullTemp : 170f;
            float smokeAmount = Mathf.InverseLerp(smokeStart, smokeFull, tempC);
            if (smokeAmount <= 0.01f)
            {
                SetFxActive(visual.SmokeFx, false);
                return;
            }

            EnsureSmokeFx(visual);
            if (visual.SmokeFx == null) return;
            SetFxActive(visual.SmokeFx, true);
            float scale = Mathf.Lerp(0.6f, 1.6f, smokeAmount);
            visual.SmokeFx.Root.transform.localScale = visual.SmokeFx.BaseScale * scale;
            var color = Color.Lerp(new Color(0.25f, 0.25f, 0.25f), new Color(0.45f, 0.45f, 0.45f), smokeAmount);
            ApplyFxColor(visual.SmokeFx, color, Color.black, 0f);
        }

        private void ApplyResistorLegColors(ComponentVisual visual, float heat01)
        {
            if (visual?.LegRenderers == null || visual.LegRenderers.Length == 0) return;
            float heat = Mathf.Clamp01(heat01);
            var coolColor = new Color(0.6f, 0.6f, 0.65f);
            var hotColor = new Color(1f, 0.45f, 0.2f);
            var legColor = Color.Lerp(coolColor, hotColor, heat);
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", legColor);
            block.SetColor("_BaseColor", legColor);
            block.SetColor("_EmissionColor", legColor * (0.15f + heat * 0.6f));
            foreach (var renderer in visual.LegRenderers)
            {
                if (renderer == null) continue;
                if (RendererHasTexture(renderer)) continue;
                renderer.SetPropertyBlock(block);
            }
        }

        private void UpdateHeatFx(ComponentVisual visual, float heat01)
        {
            if (visual == null)
            {
                return;
            }

            float heat = Mathf.Clamp01(heat01);
            if (heat <= 0.01f)
            {
                SetFxActive(visual.HeatFx, false);
                return;
            }

            EnsureHeatFx(visual);
            if (visual.HeatFx == null) return;
            SetFxActive(visual.HeatFx, true);
            float scale = Mathf.Lerp(0.7f, 1.6f, heat);
            visual.HeatFx.Root.transform.localScale = visual.HeatFx.BaseScale * scale;
            var heatColor = Color.Lerp(new Color(1f, 0.35f, 0.12f), new Color(1f, 0.1f, 0.05f), heat);
            float intensity = Mathf.Lerp(0.4f, 1.8f, heat);
            ApplyFxColor(visual.HeatFx, heatColor, heatColor, intensity);
        }

        private void UpdateErrorFx(ComponentVisual visual, bool hasError)
        {
            if (visual == null) return;
            if (!hasError)
            {
                SetFxActive(visual.ErrorFx, false);
                return;
            }

            EnsureErrorFx(visual);
            if (visual.ErrorFx == null) return;
            SetFxActive(visual.ErrorFx, true);
            float now = Time.time;
            float interval = visual.Tuning.ErrorFxInterval > 0f ? visual.Tuning.ErrorFxInterval : 0.75f;
            if (now - visual.LastErrorFxTime < interval) return;
            visual.LastErrorFxTime = now;
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 6f + visual.ErrorSeed);
            float scale = Mathf.Lerp(0.8f, 1.3f, pulse);
            visual.ErrorFx.Root.transform.localScale = visual.ErrorFx.BaseScale * scale;
            ApplyFxColor(visual.ErrorFx, new Color(1f, 0.2f, 0.2f), new Color(1f, 0.2f, 0.2f), 1.6f + pulse);
        }

        private void TriggerSparkFx(ComponentVisual visual)
        {
            if (visual == null) return;
            EnsureSparkFx(visual);
            if (visual.SparkFx == null) return;
            visual.LastSparkFxTime = Time.time;
            SetFxActive(visual.SparkFx, true);
        }

        private void UpdateSparkFx(ComponentVisual visual)
        {
            if (visual?.SparkFx == null) return;
            float elapsed = Time.time - visual.LastSparkFxTime;
            if (elapsed > 0.35f)
            {
                SetFxActive(visual.SparkFx, false);
                return;
            }
            float t = Mathf.Clamp01(1f - elapsed / 0.35f);
            float scale = Mathf.Lerp(0.6f, 1.4f, t);
            visual.SparkFx.Root.transform.localScale = visual.SparkFx.BaseScale * scale;
            ApplyFxColor(visual.SparkFx, new Color(1f, 0.75f, 0.25f), new Color(1f, 0.75f, 0.25f), 2.4f * t);
        }

        private void UpdateStatusLabel(ComponentVisual visual, string text, Color color)
        {
            if (visual?.StatusLabel == null) return;
            bool hasText = !string.IsNullOrWhiteSpace(text);
            visual.StatusLabel.text = hasText ? text : string.Empty;
            visual.StatusLabel.color = color;
            visual.StatusLabel.gameObject.SetActive(hasText);
            if (_camera == null) return;
            var labelTransform = visual.StatusLabel.transform;
            UpdateLabelTransform(visual, labelTransform);
            var toCamera = _camera.transform.position - labelTransform.position;
            if (toCamera.sqrMagnitude < 0.0001f) return;
            labelTransform.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
        }

        private void UpdateLabelTransform(ComponentVisual visual, Transform labelTransform)
        {
            if (visual == null || labelTransform == null || visual.Root == null) return;
            if (TryGetWorldBounds(visual.Root, out var bounds))
            {
                float heightOffset = Mathf.Max(0.02f, bounds.extents.y + 0.02f);
                var worldPos = bounds.center + Vector3.up * heightOffset;
                labelTransform.position = worldPos;
                float sizeBase = Mathf.Max(bounds.size.x, bounds.size.z);
                visual.StatusLabel.characterSize = Mathf.Clamp(sizeBase * 0.12f, 0.01f, 0.05f);
            }

            var parentScale = visual.Root.lossyScale;
            var scale = visual.LabelBaseScale;
            scale.x *= parentScale.x < 0f ? -1f : 1f;
            scale.y *= parentScale.y < 0f ? -1f : 1f;
            scale.z *= parentScale.z < 0f ? -1f : 1f;
            labelTransform.localScale = scale;
        }

        private void UpdateBillboardBars(ComponentVisual visual, float tempC, bool hasTemp, TelemetryFrame telemetry)
        {
            if (visual == null) return;
            if (visual.BatteryBar != null)
            {
                bool hasSoc = TryGetTelemetrySignal(telemetry, $"SRC:{visual.Id}:SOC", out var socVal);
                float value = hasSoc ? Mathf.Clamp01((float)socVal) : 0f;
                UpdateBillboardBarTransform(visual, visual.BatteryBar, 0.012f);
                bool visible = telemetry != null || hasSoc;
                SetBillboardBarValue(visual.BatteryBar, value, visible);
            }

            if (visual.TempBar != null)
            {
                float value = hasTemp ? Mathf.Clamp01(Mathf.InverseLerp(25f, 120f, tempC)) : 0f;
                UpdateBillboardBarTransform(visual, visual.TempBar, 0.012f);
                bool visible = telemetry != null || hasTemp;
                SetBillboardBarValue(visual.TempBar, value, visible);
            }
        }

        private void UpdateBillboardBarTransform(ComponentVisual visual, BillboardBar bar, float heightOffset)
        {
            if (visual == null || bar?.Root == null) return;
            if (!TryGetWorldBounds(visual.Root, out var bounds)) return;
            var worldPos = bounds.center + Vector3.up * (bounds.extents.y + heightOffset);
            bar.Root.position = worldPos;
        }

        private BillboardBar CreateBillboardBar(string name, Transform parent, Vector3 localPosition, Color background, Color fillColor)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var backgroundQuad = CreateQuad($"{name}_Bg", root.transform, background);
            var fillQuad = CreateQuad($"{name}_Fill", root.transform, fillColor);
            fillQuad.transform.localPosition = new Vector3(-0.015f, 0f, -0.0005f);
            fillQuad.transform.localScale = new Vector3(0.03f, 0.006f, 1f);

            backgroundQuad.transform.localScale = new Vector3(0.035f, 0.008f, 1f);
            root.SetActive(false);
            return new BillboardBar
            {
                Root = root.transform,
                Fill = fillQuad.transform,
                FillRenderer = fillQuad.GetComponent<Renderer>(),
                BaseFillScale = fillQuad.transform.localScale,
                IsVisible = false
            };
        }

        private GameObject CreateQuad(string name, Transform parent, Color color)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localRotation = Quaternion.identity;
            var collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(collider);
                }
                else
                {
                    DestroyImmediate(collider);
                }
            }

            var renderer = quad.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.material = BuildFxMaterial() ?? renderer.material;
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
                renderer.SetPropertyBlock(block);
            }
            return quad;
        }

        private void SetBillboardBarValue(BillboardBar bar, float value, bool visible)
        {
            if (bar == null || bar.Root == null || bar.Fill == null) return;
            float clamped = Mathf.Clamp01(value);
            var scale = bar.BaseFillScale;
            scale.x = Mathf.Max(0.001f, bar.BaseFillScale.x * clamped);
            bar.Fill.localScale = scale;
            float offset = (bar.BaseFillScale.x - scale.x) * 0.5f;
            bar.Fill.localPosition = new Vector3(-offset, bar.Fill.localPosition.y, bar.Fill.localPosition.z);
            if (bar.IsVisible != visible)
            {
                bar.Root.gameObject.SetActive(visible);
                bar.IsVisible = visible;
            }
        }

        private static bool IsLedType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return false;
            return type.IndexOf("led", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsResistorType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return false;
            return type.IndexOf("resistor", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsServoType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return false;
            return type.IndexOf("servo", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsArduinoType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return false;
            return string.Equals(type, "ArduinoUno", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "ArduinoNano", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "ArduinoProMini", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBatteryType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return false;
            return type.IndexOf("battery", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSwitchType(string type)
        {
            return string.Equals(type, "Switch", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsButtonType(string type)
        {
            return string.Equals(type, "Button", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSwitchClosed(ComponentSpec comp)
        {
            if (comp?.Properties == null) return false;
            if (TryGetBool(comp.Properties, "closed", out var closed)) return closed;
            if (TryGetBool(comp.Properties, "pressed", out var pressed)) return pressed;
            if (comp.Properties.TryGetValue("state", out var state))
            {
                string value = (state ?? string.Empty).Trim().ToLowerInvariant();
                return value == "closed" || value == "on" || value == "pressed" || value == "true";
            }
            return false;
        }

        private static bool TryGetServoAngle(ComponentSpec comp, out float angle)
        {
            angle = 0f;
            if (comp?.Properties == null) return false;
            if (TryGetFloat(comp.Properties, "angle", out angle)) return true;
            if (TryGetFloat(comp.Properties, "position", out angle)) return true;
            if (TryGetFloat(comp.Properties, "servoAngle", out angle)) return true;
            if (TryGetFloat(comp.Properties, "rotation", out angle)) return true;
            return false;
        }

        private static bool TryGetFloat(Dictionary<string, string> props, string key, out float value)
        {
            value = 0f;
            if (props == null || string.IsNullOrWhiteSpace(key)) return false;
            if (!props.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return false;
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryGetBool(Dictionary<string, string> props, string key, out bool value)
        {
            value = false;
            if (props == null || string.IsNullOrWhiteSpace(key)) return false;
            if (!props.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim().ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes" || s == "on" || s == "closed" || s == "pressed")
            {
                value = true;
                return true;
            }
            if (s == "false" || s == "0" || s == "no" || s == "off" || s == "open")
            {
                value = false;
                return true;
            }
            return false;
        }

        private static bool TryGetTelemetrySignal(TelemetryFrame telemetry, string key, out double value)
        {
            value = 0.0;
            if (telemetry?.Signals == null || string.IsNullOrWhiteSpace(key)) return false;
            return telemetry.Signals.TryGetValue(key, out value);
        }

        private static ComponentTuning GetComponentTuning(string type)
        {
            EnsureComponentTunings();
            var tuning = BuildDefaultTuning(type);
            if (!string.IsNullOrWhiteSpace(type) && ComponentTunings != null &&
                ComponentTunings.TryGetValue(type, out var loaded))
            {
                tuning = MergeTuning(tuning, loaded);
            }
            return tuning;
        }

        private static void EnsureComponentTunings()
        {
            if (ComponentTuningsLoaded) return;
            ComponentTuningsLoaded = true;
            ComponentTunings = new Dictionary<string, ComponentTuning>(StringComparer.OrdinalIgnoreCase);

            var asset = Resources.Load<TextAsset>(ComponentSettingsResource);
            if (asset == null) return;
            var file = JsonUtility.FromJson<ComponentTuningFile>(asset.text);
            if (file?.Components == null) return;
            foreach (var entry in file.Components)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Type)) continue;
                ComponentTunings[entry.Type] = entry.ToTuning();
            }
        }

        private static ComponentTuning BuildDefaultTuning(string type)
        {
            var tuning = new ComponentTuning
            {
                Euler = DefaultPrefabEuler,
                Scale = Vector3.one,
                UseLedColor = false,
                LedColor = new Color(1f, 0.2f, 0.2f, 1f),
                LedGlowRange = 0.08f,
                LedGlowIntensity = 2.5f,
                LedBlowCurrent = 0.08f,
                LedBlowTemp = 140f,
                ResistorSmokeStartTemp = 110f,
                ResistorSmokeFullTemp = 170f,
                ResistorHotStartTemp = 60f,
                ResistorHotFullTemp = 140f,
                ErrorFxInterval = 0.75f
            };

            if (!string.IsNullOrWhiteSpace(type) && PrefabEulerOverrides.TryGetValue(type, out var euler))
            {
                tuning.Euler = euler;
            }

            return tuning;
        }

        private static ComponentTuning MergeTuning(ComponentTuning baseTuning, ComponentTuning overrideTuning)
        {
            baseTuning.Euler = overrideTuning.Euler;
            if (overrideTuning.Scale.x > 0f) baseTuning.Scale.x = overrideTuning.Scale.x;
            if (overrideTuning.Scale.y > 0f) baseTuning.Scale.y = overrideTuning.Scale.y;
            if (overrideTuning.Scale.z > 0f) baseTuning.Scale.z = overrideTuning.Scale.z;

            if (overrideTuning.UseLedColor)
            {
                baseTuning.UseLedColor = true;
                baseTuning.LedColor = overrideTuning.LedColor;
            }
            if (overrideTuning.LedGlowRange > 0f) baseTuning.LedGlowRange = overrideTuning.LedGlowRange;
            if (overrideTuning.LedGlowIntensity > 0f) baseTuning.LedGlowIntensity = overrideTuning.LedGlowIntensity;
            if (overrideTuning.LedBlowCurrent > 0f) baseTuning.LedBlowCurrent = overrideTuning.LedBlowCurrent;
            if (overrideTuning.LedBlowTemp > 0f) baseTuning.LedBlowTemp = overrideTuning.LedBlowTemp;
            if (overrideTuning.ResistorSmokeStartTemp > 0f) baseTuning.ResistorSmokeStartTemp = overrideTuning.ResistorSmokeStartTemp;
            if (overrideTuning.ResistorSmokeFullTemp > 0f) baseTuning.ResistorSmokeFullTemp = overrideTuning.ResistorSmokeFullTemp;
            if (overrideTuning.ResistorHotStartTemp > 0f) baseTuning.ResistorHotStartTemp = overrideTuning.ResistorHotStartTemp;
            if (overrideTuning.ResistorHotFullTemp > 0f) baseTuning.ResistorHotFullTemp = overrideTuning.ResistorHotFullTemp;
            if (overrideTuning.ErrorFxInterval > 0f) baseTuning.ErrorFxInterval = overrideTuning.ErrorFxInterval;

            return baseTuning;
        }

        [Serializable]
        private sealed class ComponentTuningFile
        {
            public ComponentTuningEntry[] Components;
        }

        [Serializable]
        private sealed class ComponentTuningEntry
        {
            public string Type;
            public Vector3 Euler;
            public Vector3 Scale;
            public bool UseLedColor;
            public Color LedColor;
            public float LedGlowRange;
            public float LedGlowIntensity;
            public float LedBlowCurrent;
            public float LedBlowTemp;
            public float ResistorSmokeStartTemp;
            public float ResistorSmokeFullTemp;
            public float ResistorHotStartTemp;
            public float ResistorHotFullTemp;
            public float ErrorFxInterval;

            public ComponentTuning ToTuning()
            {
                return new ComponentTuning
                {
                    Euler = Euler,
                    Scale = Scale,
                    UseLedColor = UseLedColor,
                    LedColor = LedColor,
                    LedGlowRange = LedGlowRange,
                    LedGlowIntensity = LedGlowIntensity,
                    LedBlowCurrent = LedBlowCurrent,
                    LedBlowTemp = LedBlowTemp,
                    ResistorSmokeStartTemp = ResistorSmokeStartTemp,
                    ResistorSmokeFullTemp = ResistorSmokeFullTemp,
                    ResistorHotStartTemp = ResistorHotStartTemp,
                    ResistorHotFullTemp = ResistorHotFullTemp,
                    ErrorFxInterval = ErrorFxInterval
                };
            }
        }

        private struct ComponentTuning
        {
            public Vector3 Euler;
            public Vector3 Scale;
            public bool UseLedColor;
            public Color LedColor;
            public float LedGlowRange;
            public float LedGlowIntensity;
            public float LedBlowCurrent;
            public float LedBlowTemp;
            public float ResistorSmokeStartTemp;
            public float ResistorSmokeFullTemp;
            public float ResistorHotStartTemp;
            public float ResistorHotFullTemp;
            public float ErrorFxInterval;
        }

        private void FrameCamera()
        {
            if (_camera == null) return;
            if (TryGetContentBounds(out var bounds))
            {
                var centerLocal = transform.InverseTransformPoint(bounds.center);
                _panOffset = centerLocal;
                if (_usePerspective)
                {
                    _distance = ComputePerspectiveDistance(bounds, 1.4f);
                }
                else
                {
                    _zoom = ComputeOrthoSize(bounds, 1.15f);
                    _distance = Mathf.Max(1.2f, bounds.extents.magnitude * 2.2f + 1.5f);
                }
            }
            else
            {
                float width = _size.x * _scale2D.x;
                float height = _size.y * _scale2D.y;
                float size = Mathf.Max(width, height) * 0.6f;
                if (_usePerspective)
                {
                    float fovRad = Mathf.Deg2Rad * Mathf.Max(1f, _fieldOfView);
                    float distance = size / Mathf.Tan(fovRad * 0.5f);
                    _distance = Mathf.Clamp(distance * 1.4f, 0.5f, 5000f);
                }
                else
                {
                    _zoom = Mathf.Max(0.1f, size);
                    _distance = Mathf.Max(width, height) * 1.4f + 0.6f;
                }
                _panOffset = Vector3.zero;
            }

            _orbitAngles = new Vector2(45f, 0f);
            _hasUserCamera = false;
            UpdateCameraTransform();
        }

        private float ComputePerspectiveDistance(Bounds bounds, float padding)
        {
            float aspect = _viewportSize.y > 0f ? _viewportSize.x / _viewportSize.y : 1f;
            float halfHeight = Mathf.Max(0.01f, bounds.extents.y);
            float halfWidth = Mathf.Max(0.01f, bounds.extents.x);
            float halfDepth = Mathf.Max(0.01f, bounds.extents.z);
            float fovRad = Mathf.Deg2Rad * Mathf.Max(1f, _fieldOfView);
            float tanFov = Mathf.Tan(fovRad * 0.5f);
            float verticalDistance = halfHeight / tanFov;
            float horizontalFov = 2f * Mathf.Atan(tanFov * Mathf.Max(0.2f, aspect));
            float horizontalDistance = halfWidth / Mathf.Tan(horizontalFov * 0.5f);
            float distance = Mathf.Max(verticalDistance, horizontalDistance) + halfDepth;
            return Mathf.Clamp(distance * padding, 0.2f, 5000f);
        }

        private float ComputeOrthoSize(Bounds bounds, float padding)
        {
            float aspect = _viewportSize.y > 0f ? _viewportSize.x / _viewportSize.y : 1f;
            float halfHeight = Mathf.Max(0.01f, bounds.extents.y);
            float halfWidth = Mathf.Max(0.01f, bounds.extents.x);
            float size = Mathf.Max(halfHeight, halfWidth / Mathf.Max(0.2f, aspect));
            return Mathf.Clamp(size * padding, 0.08f, 50f);
        }

        public void Pan(Vector2 deltaPixels)
        {
            if (_camera == null) return;
            float pixels = Mathf.Max(1f, _viewportSize.y);
            float unitsPerPixel;
            if (!_usePerspective)
            {
                unitsPerPixel = (_camera.orthographicSize * 2f) / pixels;
            }
            else
            {
                float fovRad = Mathf.Deg2Rad * Mathf.Max(1f, _camera.fieldOfView);
                float viewHeight = 2f * Mathf.Max(0.01f, _distance) * Mathf.Tan(fovRad * 0.5f);
                unitsPerPixel = viewHeight / pixels;
            }
            Vector3 right = _camera.transform.right;
            Vector3 up = _camera.transform.up;
            right.y = 0f;
            up.y = 0f;
            if (right.sqrMagnitude < 0.001f) right = Vector3.right;
            if (up.sqrMagnitude < 0.001f) up = Vector3.forward;
            right.Normalize();
            up.Normalize();
            Vector3 move = (-right * deltaPixels.x - up * deltaPixels.y) * unitsPerPixel;
            _panOffset += move;
            if (_followTarget) _followOffset += move;
            _hasUserCamera = true;
            UpdateCameraTransform();
        }

        public void Orbit(Vector2 deltaPixels)
        {
            if (_camera == null) return;
            _orbitAngles.x = Mathf.Repeat(_orbitAngles.x - deltaPixels.y * 0.2f, 360f);
            _orbitAngles.y = Mathf.Repeat(_orbitAngles.y + deltaPixels.x * 0.2f, 360f);
            _hasUserCamera = true;
            UpdateCameraTransform();
        }

        public void Zoom(float delta)
        {
            if (_camera == null) return;
            float step = -delta * 0.01f;
            if (!_usePerspective)
            {
                _zoom = Mathf.Clamp(_zoom + step, 0.08f, 10f);
            }
            else
            {
                _distance = Mathf.Clamp(_distance + step * 5f, 0.2f, 5000f);
            }
            _hasUserCamera = true;
            UpdateCameraTransform();
        }

        private void UpdateCameraTransform()
        {
            if (_camera == null) return;
            var rotation = Quaternion.Euler(_orbitAngles.x, _orbitAngles.y, 0f);
            var forward = rotation * Vector3.forward;
            _camera.orthographic = !_usePerspective;
            _camera.backgroundColor = CameraBackgroundColor;
            _camera.fieldOfView = _fieldOfView;
            if (_camera.orthographic)
            {
                _camera.orthographicSize = _zoom;
            }
            _camera.transform.localPosition = _panOffset - forward * _distance;
            _camera.transform.localRotation = rotation;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = Mathf.Max(5000f, _distance + 500f);
        }

        private Vector3 ToWorld(Vector2 pos, float y)
        {
            float x = (pos.x - _min.x - _size.x * 0.5f) * _scale2D.x;
            float z = (pos.y - _min.y - _size.y * 0.5f) * _scale2D.y;
            return new Vector3(x, y, z);
        }

        private static bool TryGetPosition(ComponentSpec comp, out Vector2 pos)
        {
            pos = Vector2.zero;
            if (comp == null || comp.Properties == null) return false;
            if (comp.Properties.TryGetValue("posX", out var xRaw) &&
                comp.Properties.TryGetValue("posY", out var yRaw))
            {
                if (float.TryParse(xRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    float.TryParse(yRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    pos = new Vector2(x, y);
                    return true;
                }
            }
            return false;
        }

        private static string GetComponentId(string node)
        {
            if (string.IsNullOrWhiteSpace(node)) return string.Empty;
            int dot = node.IndexOf('.');
            return dot > 0 ? node.Substring(0, dot) : node;
        }

        private static string GetPinName(string node)
        {
            if (string.IsNullOrWhiteSpace(node)) return string.Empty;
            int dot = node.IndexOf('.');
            if (dot < 0 || dot == node.Length - 1) return string.Empty;
            return node.Substring(dot + 1);
        }

        private static Color GetNetColor(string netId)
        {
            if (string.IsNullOrWhiteSpace(netId)) return WireDefaultColor;
            int index = (int)(StableHash(netId) % (uint)WirePalette.Length);
            return WirePalette[index];
        }

        private static uint StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var ch in text)
                {
                    hash ^= ch;
                    hash *= 16777619;
                }
                return hash;
            }
        }

        private static Vector2 GetComponentSize2D(ComponentSpec comp)
        {
            if (TryGetSizeFromProperties(comp?.Properties, out var size)) return size;
            return CircuitLayoutSizing.GetComponentSize2D(comp?.Type ?? string.Empty);
        }

        private static bool TryGetSizeFromProperties(Dictionary<string, string> props, out Vector2 size)
        {
            size = Vector2.zero;
            if (props == null) return false;
            if (TryGetFloat(props, "sizeX", out var width) && TryGetFloat(props, "sizeY", out var height))
            {
                size = new Vector2(width, height);
                return true;
            }
            if (TryGetFloat(props, "width", out width) && TryGetFloat(props, "height", out height))
            {
                size = new Vector2(width, height);
                return true;
            }
            return false;
        }

        private static Vector3 GetPartScale(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return new Vector3(0.06f, 0.02f, 0.02f);
            string key = type.ToLowerInvariant();
            if (key.Contains("arduino")) return new Vector3(0.18f, 0.02f, 0.08f);
            if (key.Contains("resistor")) return new Vector3(0.08f, 0.015f, 0.02f);
            if (key.Contains("led")) return new Vector3(0.04f, 0.02f, 0.04f);
            if (key.Contains("battery")) return new Vector3(0.1f, 0.03f, 0.06f);
            if (key.Contains("button") || key.Contains("switch")) return new Vector3(0.05f, 0.02f, 0.05f);
            return new Vector3(0.06f, 0.02f, 0.02f);
        }

        private Vector3 GetPrefabWorldPosition(Vector2 pos, Vector2 size2d, GameObject part)
        {
            if (part == null) return ToWorld(pos + size2d * 0.5f, ComponentHeight);
            if (!TryGetWorldBounds(part.transform, out var bounds))
            {
                return ToWorld(pos + size2d * 0.5f, ComponentHeight);
            }

            var targetMinWorld = ToWorld(pos, ComponentHeight);
            var offsetMin = bounds.min - part.transform.position;
            return targetMinWorld - offsetMin;
        }

        private Vector3 GetComponentAnchorPosition(GameObject part, Vector3 fallback)
        {
            if (part == null) return fallback;
            if (_root != null && TryGetWorldBounds(part.transform, out var bounds))
            {
                return _root.InverseTransformPoint(bounds.center);
            }
            return fallback;
        }

        private static bool TryGetWorldBounds(Transform root, out Bounds bounds)
        {
            bounds = new Bounds();
            if (root == null) return false;
            bool hasBounds = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return hasBounds;
        }

        private static Color GetPartColor(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return new Color(0.6f, 0.6f, 0.6f);
            string key = type.ToLowerInvariant();
            if (key.Contains("arduino")) return new Color(0.12f, 0.55f, 0.95f);
            if (key.Contains("resistor")) return new Color(0.85f, 0.65f, 0.35f);
            if (key.Contains("led")) return new Color(0.95f, 0.25f, 0.25f);
            if (key.Contains("battery")) return new Color(0.15f, 0.15f, 0.18f);
            if (key.Contains("button") || key.Contains("switch")) return new Color(0.35f, 0.35f, 0.4f);
            return new Color(0.6f, 0.6f, 0.6f);
        }

        private void EnsurePrefabs()
        {
            if (_prefabsLoaded) return;
            _prefabsLoaded = true;

            _arduinoPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/Arduino");
            _arduinoUnoPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/ArduinoUno");
            _arduinoNanoPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/ArduinoNano");
            _arduinoProMiniPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/ArduinoProMini");
            _resistorPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/Resistor");
            _ledPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/LED");
            _batteryPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/Battery");
            _buttonPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/Button");
            _switchPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/Swirch_ON_OFF");
            _servoPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/ServoSG90");
            _genericPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/Generic");
        }

        private GameObject CreateInstance(GameObject prefab, PrimitiveType fallback, string name)
        {
            GameObject go = prefab != null
                ? Instantiate(prefab, _root, false)
                : GameObject.CreatePrimitive(fallback);
            if (prefab == null) go.transform.SetParent(_root, false);
            go.name = name;
            DisableBoxColliders(go);
            return go;
        }

        private GameObject GetPrefabForType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return _genericPrefab;
            string key = type.ToLowerInvariant();
            if (key.Contains("arduinouno")) return _arduinoUnoPrefab ?? _arduinoPrefab;
            if (key.Contains("arduinonano")) return _arduinoNanoPrefab ?? _arduinoPrefab;
            if (key.Contains("arduinopromini")) return _arduinoProMiniPrefab ?? _arduinoPrefab;
            if (key.Contains("arduino")) return _arduinoPrefab ?? _arduinoUnoPrefab ?? _arduinoNanoPrefab ?? _arduinoProMiniPrefab;
            if (key.Contains("resistor")) return _resistorPrefab;
            if (key.Contains("led")) return _ledPrefab;
            if (key.Contains("battery")) return _batteryPrefab;
            if (key.Contains("button")) return _buttonPrefab;
            if (key.Contains("switch")) return _switchPrefab ?? _buttonPrefab;
            if (key.Contains("servo")) return _servoPrefab;
            return _genericPrefab;
        }

        private static string GetTextureForType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return null;
            string key = type.ToLowerInvariant();
            if (key.Contains("arduinouno")) return $"{TextureRoot}/ArduinoUno";
            if (key.Contains("arduinonano")) return $"{TextureRoot}/ArduinoNano";
            if (key.Contains("arduinopromini")) return $"{TextureRoot}/ArduinoProMini";
            if (key.Contains("arduinopromicro")) return $"{TextureRoot}/ArduinoProMicro";
            if (key.Contains("arduino")) return $"{TextureRoot}/ArduinoUno";
            if (key.Contains("servo")) return $"{TextureRoot}/ServoSG90";
            if (key.Contains("capacitor")) return $"{TextureRoot}/Capacitor";
            return null;
        }

        private static void ApplyTexture(GameObject target, string texturePath)
        {
            if (target == null || string.IsNullOrWhiteSpace(texturePath)) return;
            var material = GetTextureMaterial(texturePath);
            if (material == null) return;
            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (RendererHasAnyMaterial(renderer)) continue;
                renderer.sharedMaterial = material;
            }
        }

        private static bool RendererHasAnyMaterial(Renderer renderer)
        {
            if (renderer == null) return false;
            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0) return false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null) return true;
            }
            return false;
        }

        private static bool RendererHasTexture(Renderer renderer)
        {
            if (renderer == null) return false;
            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0) return false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (HasTexture(materials[i])) return true;
            }
            return false;
        }

        private static bool HasTexture(Material material)
        {
            if (material == null) return false;
            if (material.mainTexture != null) return true;
            if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null) return true;
            if (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null) return true;
            if (material.HasProperty("_BaseColorMap") && material.GetTexture("_BaseColorMap") != null) return true;
            if (material.HasProperty("_AlbedoMap") && material.GetTexture("_AlbedoMap") != null) return true;
            if (material.HasProperty("_Diffuse") && material.GetTexture("_Diffuse") != null) return true;
            if (material.HasProperty("_DiffuseMap") && material.GetTexture("_DiffuseMap") != null) return true;
            var names = material.GetTexturePropertyNames();
            foreach (var name in names)
            {
                if (!string.IsNullOrWhiteSpace(name) && material.GetTexture(name) != null) return true;
            }
            return false;
        }

        private static FxHandle CreateFx(string name, Transform parent, Vector3 localPosition, Color baseColor,
            Color lightColor, float lightIntensity, float lightRange, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;

            var gfx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gfx.name = $"{name}_Mesh";
            gfx.transform.SetParent(go.transform, false);
            gfx.transform.localScale = Vector3.one;
            var collider = gfx.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(collider);
                }
                else
                {
                    DestroyImmediate(collider);
                }
            }

            var renderer = gfx.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.material = BuildFxMaterial();
            }

            Light light = null;
            if (lightIntensity > 0f && lightRange > 0f)
            {
                light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = lightColor;
                light.range = lightRange;
                light.intensity = lightIntensity;
                light.shadows = LightShadows.None;
            }

            var handle = new FxHandle
            {
                Root = go,
                Renderer = renderer,
                Light = light,
                BaseScale = Vector3.one * Mathf.Max(0.001f, scale),
                BaseColor = baseColor
            };

            go.transform.localScale = handle.BaseScale;
            SetFxActive(handle, false);
            ApplyFxColor(handle, baseColor, lightColor, lightIntensity);
            return handle;
        }

        private static Material BuildFxMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Standard");
            if (shader == null) return null;
            var material = new Material(shader)
            {
                name = "Circuit3D_FxMat"
            };
            return material;
        }

        private static void ApplyFxColor(FxHandle handle, Color color, Color lightColor, float lightIntensity)
        {
            if (handle == null) return;
            if (handle.Renderer != null)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
                block.SetColor("_EmissionColor", color * 1.2f);
                handle.Renderer.SetPropertyBlock(block);
            }
            if (handle.Light != null)
            {
                handle.Light.color = lightColor;
                handle.Light.intensity = lightIntensity;
                handle.Light.enabled = lightIntensity > 0.01f;
            }
        }

        private static void SetFxActive(FxHandle handle, bool active)
        {
            if (handle?.Root == null) return;
            handle.Root.SetActive(active);
        }

        private bool TryGetPrefabBounds(GameObject prefab, out Bounds bounds)
        {
            bounds = new Bounds();
            if (prefab == null) return false;
            if (PrefabBoundsCache.TryGetValue(prefab, out bounds)) return true;

            bool hasBounds = false;
            var root = prefab.transform;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter == null || filter.sharedMesh == null) continue;
                var meshBounds = filter.sharedMesh.bounds;
                var localToRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var transformed = TransformBounds(meshBounds, localToRoot);
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

            if (hasBounds)
            {
                PrefabBoundsCache[prefab] = bounds;
            }

            return hasBounds;
        }

        private static float Median(List<float> values)
        {
            if (values == null || values.Count == 0) return DefaultScale;
            values.Sort();
            int mid = values.Count / 2;
            if (values.Count % 2 == 1) return values[mid];
            return (values[mid - 1] + values[mid]) * 0.5f;
        }

        private static float ClampScale(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return DefaultScale;
            return Mathf.Clamp(value, 0.0001f, 1f);
        }

        private bool TryGetContentBounds(out Bounds bounds)
        {
            bounds = new Bounds();
            if (_root == null) return false;
            bool hasBounds = false;
            foreach (var renderer in _root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
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

            var newExtents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));

            return new Bounds(center, newExtents * 2f);
        }

        private sealed class ComponentVisual
        {
            public string Id;
            public string Type;
            public Transform Root;
            public Renderer[] Renderers;
            public MaterialPropertyBlock Block;
            public Color BaseColor;
            public Vector3 BasePosition;
            public Quaternion BaseRotation;
            public ComponentTuning Tuning;
            public bool AllowTint;
            public Light GlowLight;
            public Transform UsbIndicator;
            public Renderer UsbRenderer;
            public Light UsbLight;
            public Transform ServoArm;
            public Quaternion ServoArmBaseRotation;
            public TextMesh StatusLabel;
            public Vector3 LabelBaseScale;
            public BillboardBar BatteryBar;
            public BillboardBar TempBar;
            public FxHandle SmokeFx;
            public FxHandle HeatFx;
            public FxHandle SparkFx;
            public FxHandle ErrorFx;
            public Renderer[] LegRenderers;
            public bool LedBlown;
            public float LedBlowTime;
            public float LastErrorFxTime;
            public float LastSparkFxTime;
            public float ErrorSeed;
            public bool IsLed;
            public bool IsResistor;
            public bool IsSwitch;
            public bool IsButton;
            public bool IsArduino;
            public bool IsBattery;
            public bool IsServo;
        }

        private sealed class FxHandle
        {
            public GameObject Root;
            public Renderer Renderer;
            public Light Light;
            public Vector3 BaseScale;
            public Color BaseColor;
        }

        private sealed class BillboardBar
        {
            public Transform Root;
            public Transform Fill;
            public Renderer FillRenderer;
            public Vector3 BaseFillScale;
            public bool IsVisible;
        }

        private readonly struct AnchorState
        {
            public AnchorState(Vector3 localPosition, float radius)
            {
                LocalPosition = localPosition;
                Radius = radius;
            }

            public Vector3 LocalPosition { get; }
            public float Radius { get; }
        }

        private static Material GetTextureMaterial(string resourcePath)
        {
            if (TextureMaterials.TryGetValue(resourcePath, out var cached)) return cached;
            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null) return null;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;

            var material = new Material(shader)
            {
                name = $"{texture.name}_Mat"
            };
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            TextureMaterials[resourcePath] = material;
            return material;
        }

        private static Mesh BuildBoxMesh(Vector3 size)
        {
            float hx = size.x * 0.5f;
            float hy = size.y * 0.5f;
            float hz = size.z * 0.5f;

            var vertices = new[]
            {
                new Vector3(-hx, -hy, hz), new Vector3(hx, -hy, hz), new Vector3(hx, hy, hz), new Vector3(-hx, hy, hz),
                new Vector3(hx, -hy, -hz), new Vector3(-hx, -hy, -hz), new Vector3(-hx, hy, -hz), new Vector3(hx, hy, -hz),
                new Vector3(-hx, -hy, -hz), new Vector3(-hx, -hy, hz), new Vector3(-hx, hy, hz), new Vector3(-hx, hy, -hz),
                new Vector3(hx, -hy, hz), new Vector3(hx, -hy, -hz), new Vector3(hx, hy, -hz), new Vector3(hx, hy, hz),
                new Vector3(-hx, hy, hz), new Vector3(hx, hy, hz), new Vector3(hx, hy, -hz), new Vector3(-hx, hy, -hz),
                new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz), new Vector3(hx, -hy, hz), new Vector3(-hx, -hy, hz)
            };

            var normals = new[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.back, Vector3.back, Vector3.back, Vector3.back,
                Vector3.left, Vector3.left, Vector3.left, Vector3.left,
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                Vector3.down, Vector3.down, Vector3.down, Vector3.down
            };

            var triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 6, 5, 4, 7, 6,
                8, 10, 9, 8, 11, 10,
                12, 14, 13, 12, 15, 14,
                16, 18, 17, 16, 19, 18,
                20, 22, 21, 20, 23, 22
            };

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
