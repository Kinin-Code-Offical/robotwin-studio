using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        private const float LabelScaleBoost = 1.25f;
        private const float LedLightRangeBoost = 4.5f;
        private const float LedLightIntensityBoost = 2.0f;
        private const float LedGlowFxRangeBoost = 0.9f;
        private const float SparkShowerGravity = 0.6f;
        private const int SparkShowerMaxParticles = 28;
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
        private readonly Dictionary<string, GameObject> _runtimeModelCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Task> _runtimeModelLoads = new Dictionary<string, Task>(StringComparer.OrdinalIgnoreCase);
        private Transform _runtimeModelRoot;
        private CircuitSpec _lastCircuit;
        private Transform _lightRoot;
        private Light _keyLight;
        private Light _fillLight;
        private Light _rimLight;
        private Light _headLight;
        private float _lightingBlend;
        private readonly Dictionary<string, AnchorState> _anchorStateCache = new Dictionary<string, AnchorState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ComponentVisual> _componentVisuals = new Dictionary<string, ComponentVisual>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<WireRope>> _wiresByNet = new Dictionary<string, List<WireRope>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Material> TextureMaterials =
            new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> RuntimeTextureCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<GameObject, Bounds> PrefabBoundsCache = new Dictionary<GameObject, Bounds>();
        private static Dictionary<string, ComponentTuning> ComponentTunings;
        private static bool ComponentTuningsLoaded;
        private static Mesh _usbShellMesh;
        private static Mesh _usbTongueMesh;
        private static Material _barMaterial;
        private static Material _smokeMaterial;
        private static Material _ledGlowMaterial;
        private static Texture2D _ledGlowTexture;
        private static Texture2D _smokeTexture;
        private static Material _sparkMaterial;

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
        private bool _labelsVisible = true;
        private bool _errorFxEnabled = true;
        private bool _wireHeatmapEnabled;

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

        public void SetLabelsVisible(bool visible)
        {
            _labelsVisible = visible;
            foreach (var visual in _componentVisuals.Values)
            {
                if (visual?.StatusLabel == null) continue;
                bool hasText = !string.IsNullOrWhiteSpace(visual.StatusLabel.text);
                ApplyLabelVisibility(visual, hasText);
            }
        }

        public void SetErrorFxEnabled(bool enabled)
        {
            _errorFxEnabled = enabled;
            if (!enabled)
            {
                foreach (var visual in _componentVisuals.Values)
                {
                    if (visual?.ErrorFx == null) continue;
                    SetFxActive(visual.ErrorFx, false);
                }
                foreach (var kvp in _wiresByNet)
                {
                    var list = kvp.Value;
                    if (list == null) continue;
                    foreach (var wire in list)
                    {
                        if (wire == null) continue;
                        wire.SetError(false);
                    }
                }
            }
        }

        public void SetWireHeatmapEnabled(bool enabled)
        {
            _wireHeatmapEnabled = enabled;
            if (!enabled)
            {
                ResetWireColors();
            }
        }

        public void ClearAnchorCache()
        {
            _anchorStateCache.Clear();
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

        public void NudgePanCamera(Vector2 axes)
        {
            if (_camera == null) return;
            Vector3 right = _camera.transform.right;
            if (right.sqrMagnitude < 0.001f) right = Vector3.right;
            right.Normalize();

            Vector3 forward = _camera.transform.forward;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            float step = GetKeyboardPanStep();
            Vector3 delta = (right * axes.x + forward * axes.y) * step;
            _panOffset += delta;
            if (_followTarget) _followOffset += delta;
            _hasUserCamera = true;
            UpdateCameraTransform();
        }

        public void NudgePanCameraVertical(float axis)
        {
            if (_camera == null) return;
            Vector3 up = _camera.transform.up;
            if (up.sqrMagnitude < 0.001f) up = Vector3.up;
            up.Normalize();

            float step = GetKeyboardPanStep();
            Vector3 delta = up * (axis * step);
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
                    if (_labelsVisible)
                    {
                        UpdateBillboardRotation(visual.StatusLabel?.transform, true);
                    }
                    UpdateBillboardRotation(visual.BatteryBar?.Root);
                    UpdateBillboardRotation(visual.TempBar?.Root);
                }
            }
        }

        private void UpdateBillboardRotation(Transform target, bool flipForward = false)
        {
            if (target == null || _camera == null) return;
            var toCamera = _camera.transform.position - target.position;
            if (toCamera.sqrMagnitude < 0.0001f) return;
            if (flipForward)
            {
                toCamera = -toCamera;
            }
            target.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
        }

        public void Build(CircuitSpec circuit)
        {
            _lastCircuit = circuit;
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

            _keyLight = CreateDirectionalLight("Circuit3D_KeyLight", new Vector3(50f, -30f, 0f), 0.85f, new Color(0.98f, 0.95f, 0.92f), LightRenderMode.ForcePixel);
            _fillLight = CreateDirectionalLight("Circuit3D_FillLight", new Vector3(20f, 160f, 0f), 0.35f, new Color(0.78f, 0.84f, 0.95f), LightRenderMode.Auto);
            _rimLight = CreateDirectionalLight("Circuit3D_RimLight", new Vector3(75f, 40f, 0f), 0.25f, new Color(0.7f, 0.78f, 0.95f), LightRenderMode.Auto);
            _headLight = CreateHeadLight("Circuit3D_HeadLight", LightRenderMode.ForcePixel);
            if (_camera != null)
            {
                _headLight.transform.SetParent(_camera.transform, false);
            }
            ApplyLightingBlend();
        }

        private Light CreateDirectionalLight(string name, Vector3 euler, float intensity, Color color, LightRenderMode renderMode)
        {
            var lightGo = new GameObject(name);
            lightGo.transform.SetParent(_lightRoot, false);
            lightGo.transform.localRotation = Quaternion.Euler(euler);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.renderMode = renderMode;
            light.shadows = LightShadows.None;
            return light;
        }

        private Light CreateHeadLight(string name, LightRenderMode renderMode)
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
            light.renderMode = renderMode;
            light.shadows = LightShadows.None;
            return light;
        }

        public void AdjustLightingBlend(float delta)
        {
            _lightingBlend = Mathf.Clamp01(_lightingBlend + delta);
            ApplyLightingBlend();
        }

        private void ApplyLightingBlend()
        {
            if (_keyLight == null || _fillLight == null || _rimLight == null || _headLight == null) return;
            var studio = LightingProfile.Studio;
            var realistic = LightingProfile.Realistic;
            float t = _lightingBlend;

            _keyLight.intensity = Mathf.Lerp(studio.KeyIntensity, realistic.KeyIntensity, t);
            _keyLight.color = Color.Lerp(studio.KeyColor, realistic.KeyColor, t);
            _fillLight.intensity = Mathf.Lerp(studio.FillIntensity, realistic.FillIntensity, t);
            _fillLight.color = Color.Lerp(studio.FillColor, realistic.FillColor, t);
            _rimLight.intensity = Mathf.Lerp(studio.RimIntensity, realistic.RimIntensity, t);
            _rimLight.color = Color.Lerp(studio.RimColor, realistic.RimColor, t);
            _headLight.intensity = Mathf.Lerp(studio.HeadIntensity, realistic.HeadIntensity, t);
            _headLight.color = Color.Lerp(studio.HeadColor, realistic.HeadColor, t);
            _headLight.range = Mathf.Lerp(studio.HeadRange, realistic.HeadRange, t);
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

                var catalogItem = ComponentCatalog.GetByType(comp.Type);
                RegisterComponentVisual(comp, part, allowTint, catalogItem);
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

        private void RegisterComponentVisual(ComponentSpec comp, GameObject part, bool allowTint, ComponentCatalog.Item catalogItem)
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
                AllowTint = allowTint,
                CatalogItem = catalogItem,
                PartBases = BuildPartBases(renderers),
                ActiveStateId = string.Empty
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

            EnsureStatusLabel(visual);

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

            if (visual.StatusLabel != null)
            {
                visual.StatusLabel.text = comp.Id;
                ApplyLabelVisibility(visual, true);
            }
            if (visual.BatteryBar != null)
            {
                SetBillboardBarValue(visual.BatteryBar, 0f, true);
            }
            if (visual.TempBar != null)
            {
                SetBillboardBarValue(visual.TempBar, 0f, true);
            }

            ApplyOverrides(visual, ResolveStateId(visual, comp));
            BuildPinGizmos(visual);
            _componentVisuals[comp.Id] = visual;
        }

        private static void BuildPinGizmos(ComponentVisual visual)
        {
            if (visual?.Root == null) return;
            var pins = visual.CatalogItem.PinLayout;
            if (pins == null || pins.Count == 0) return;

            visual.PinGizmos = new List<GameObject>();
            foreach (var pin in pins)
            {
                if (string.IsNullOrWhiteSpace(pin.Name)) continue;
                if (pin.AnchorRadius <= 0f && pin.AnchorLocal.sqrMagnitude <= 0.0001f) continue;

                var root = new GameObject($"Pin_{pin.Name}");
                root.transform.SetParent(visual.Root, false);
                root.transform.localPosition = pin.AnchorLocal;

                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.SetParent(root.transform, false);
                float radius = pin.AnchorRadius > 0f ? pin.AnchorRadius : DefaultAnchorRadius;
                sphere.transform.localScale = Vector3.one * Mathf.Max(radius * 2f, 0.002f);
                var collider = sphere.GetComponent<Collider>();
                if (collider != null) Destroy(collider);

                var renderer = sphere.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_Color", new Color(0.2f, 0.7f, 1f));
                    block.SetColor("_BaseColor", new Color(0.2f, 0.7f, 1f));
                    renderer.SetPropertyBlock(block);
                }

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(root.transform, false);
                labelGo.transform.localPosition = new Vector3(0f, radius * 2.5f, 0f);
                var text = labelGo.AddComponent<TextMesh>();
                text.text = pin.Name;
                text.fontSize = 24;
                text.characterSize = 0.002f;
                text.color = new Color(0.8f, 0.9f, 1f);
                text.anchor = TextAnchor.MiddleCenter;

                visual.PinGizmos.Add(root);
            }
        }

        private static Dictionary<string, PartSnapshot> BuildPartBases(Renderer[] renderers)
        {
            var bases = new Dictionary<string, PartSnapshot>(StringComparer.OrdinalIgnoreCase);
            if (renderers == null) return bases;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var tr = renderer.transform;
                if (tr == null) continue;
                if (bases.ContainsKey(tr.name)) continue;
                Color baseColor = Color.white;
                Texture baseTexture = null;
                if (renderer.sharedMaterial != null)
                {
                    baseColor = renderer.sharedMaterial.color;
                    baseTexture = renderer.sharedMaterial.mainTexture;
                    if (baseTexture == null && renderer.sharedMaterial.HasProperty("_BaseMap"))
                    {
                        baseTexture = renderer.sharedMaterial.GetTexture("_BaseMap");
                    }
                }
                bases[tr.name] = new PartSnapshot
                {
                    Transform = tr,
                    Renderer = renderer,
                    Position = tr.localPosition,
                    Rotation = tr.localRotation,
                    Scale = tr.localScale,
                    BaseColor = baseColor,
                    BaseTexture = baseTexture
                };
            }
            return bases;
        }

        private static bool HasStateOverrides(ComponentVisual visual)
        {
            return visual?.CatalogItem.StateOverrides != null && visual.CatalogItem.StateOverrides.Count > 0;
        }

        private static string ResolveStateId(ComponentVisual visual, ComponentSpec spec)
        {
            if (spec?.Properties != null)
            {
                if (spec.Properties.TryGetValue("state", out var state) && !string.IsNullOrWhiteSpace(state))
                {
                    return state.Trim().ToLowerInvariant();
                }
                if (spec.Properties.TryGetValue("mode", out var mode) && !string.IsNullOrWhiteSpace(mode))
                {
                    return mode.Trim().ToLowerInvariant();
                }
            }

            if (visual != null && (visual.IsSwitch || visual.IsButton))
            {
                return IsSwitchClosed(spec) ? "on" : "off";
            }

            return "default";
        }

        private static void ApplyOverrides(ComponentVisual visual, string stateId)
        {
            if (visual == null || visual.PartBases == null) return;
            foreach (var snapshot in visual.PartBases.Values)
            {
                if (snapshot?.Transform == null) continue;
                snapshot.Transform.localPosition = snapshot.Position;
                snapshot.Transform.localRotation = snapshot.Rotation;
                snapshot.Transform.localScale = snapshot.Scale;
                if (snapshot.Renderer != null)
                {
                    SetRendererColor(snapshot.Renderer, snapshot.BaseColor);
                    SetRendererTexture(snapshot.Renderer, snapshot.BaseTexture);
                }
            }

            if (visual.CatalogItem.PartOverrides != null && visual.CatalogItem.PartOverrides.Count > 0)
            {
                ApplyPartOverrides(visual, visual.CatalogItem.PartOverrides);
            }

            if (!string.IsNullOrWhiteSpace(stateId) &&
                visual.CatalogItem.StateOverrides != null &&
                visual.CatalogItem.StateOverrides.Count > 0)
            {
                foreach (var state in visual.CatalogItem.StateOverrides)
                {
                    if (!string.Equals(state.Id, stateId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (state.Parts != null && state.Parts.Count > 0)
                    {
                        ApplyPartOverrides(visual, state.Parts);
                    }
                    break;
                }
            }
        }

        private static void ApplyPartOverrides(ComponentVisual visual, List<ComponentCatalog.PartOverride> overrides)
        {
            if (visual == null || visual.PartBases == null || overrides == null) return;
            foreach (var part in overrides)
            {
                if (string.IsNullOrWhiteSpace(part.Name)) continue;
                if (!visual.PartBases.TryGetValue(part.Name, out var snapshot) || snapshot?.Transform == null) continue;
                snapshot.Transform.localPosition = part.Position;
                snapshot.Transform.localRotation = Quaternion.Euler(part.Rotation);
                snapshot.Transform.localScale = part.Scale == Vector3.zero ? Vector3.one : part.Scale;
                if (snapshot.Renderer == null) continue;
                Texture2D texture = null;
                if (part.UseTexture && !string.IsNullOrWhiteSpace(part.TextureFile))
                {
                    TryLoadRuntimeTexture(visual.CatalogItem, part.TextureFile, out texture);
                }
                ApplyRendererOverride(snapshot.Renderer, part.UseColor, part.Color, texture);
            }
        }

        private static void SetRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(block);
        }

        private static void SetRendererTexture(Renderer renderer, Texture texture)
        {
            if (renderer == null) return;
            if (texture == null) return;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetTexture("_MainTex", texture);
            block.SetTexture("_BaseMap", texture);
            block.SetTexture("_BaseColorMap", texture);
            renderer.SetPropertyBlock(block);
        }

        private static void ApplyRendererOverride(Renderer renderer, bool useColor, Color color, Texture texture)
        {
            if (renderer == null) return;
            var block = new MaterialPropertyBlock();
            if (useColor)
            {
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
            }
            if (texture != null)
            {
                block.SetTexture("_MainTex", texture);
                block.SetTexture("_BaseMap", texture);
                block.SetTexture("_BaseColorMap", texture);
            }
            renderer.SetPropertyBlock(block);
        }

        private static bool TryLoadRuntimeTexture(ComponentCatalog.Item item, string entryName, out Texture2D texture)
        {
            texture = null;
            if (string.IsNullOrWhiteSpace(entryName)) return false;
            string cacheKey = $"{item.SourcePath}|{entryName}";
            if (RuntimeTextureCache.TryGetValue(cacheKey, out var cached))
            {
                texture = cached;
                return texture != null;
            }

            string path = ResolveComponentAssetPath(item, entryName);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                RuntimeTextureCache[cacheKey] = null;
                return false;
            }

            try
            {
                var data = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(data))
                {
                    UnityEngine.Object.Destroy(tex);
                    RuntimeTextureCache[cacheKey] = null;
                    return false;
                }
                tex.name = Path.GetFileNameWithoutExtension(path);
                RuntimeTextureCache[cacheKey] = tex;
                texture = tex;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Circuit3DView] Failed to load texture {entryName}: {ex.Message}");
                RuntimeTextureCache[cacheKey] = null;
                return false;
            }
        }

        private static string ResolveComponentAssetPath(ComponentCatalog.Item item, string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName)) return string.Empty;
            string normalized = entryName.Replace('\\', '/').TrimStart('/');
            if (!string.IsNullOrWhiteSpace(item.SourcePath))
            {
                if (File.Exists(item.SourcePath) && ComponentPackageUtility.IsPackagePath(item.SourcePath))
                {
                    if (ComponentPackageUtility.TryExtractEntryToCache(item.SourcePath, normalized, out var extracted))
                    {
                        return extracted;
                    }
                }
                if (Directory.Exists(item.SourcePath) &&
                    item.SourcePath.EndsWith(ComponentPackageUtility.PackageExtension, StringComparison.OrdinalIgnoreCase))
                {
                    string candidate = Path.Combine(item.SourcePath, normalized.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(candidate)) return candidate;
                }
                if (File.Exists(item.SourcePath))
                {
                    string baseDir = Path.GetDirectoryName(item.SourcePath) ?? string.Empty;
                    string candidate = Path.Combine(baseDir, normalized.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return string.Empty;
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
                bool hasSolid = false;
                bool hasConvex = false;
                var colliders = filter.GetComponents<Collider>();
                foreach (var collider in colliders)
                {
                    if (collider == null || collider.isTrigger) continue;
                    hasSolid = true;
                    if (IsConvexCollider(collider))
                    {
                        hasConvex = true;
                        break;
                    }
                }

                if (hasConvex) continue;

                bool canConvex = CanUseConvexMesh(filter.sharedMesh);
                if (!hasSolid && canConvex)
                {
                    var meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = filter.sharedMesh;
                    meshCollider.convex = true;
                    continue;
                }

                EnsureBoundsBoxCollider(filter.gameObject, filter.sharedMesh);
            }
        }

        private static bool IsConvexCollider(Collider collider)
        {
            if (collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider) return true;
            if (collider is MeshCollider meshCollider) return meshCollider.convex;
            return false;
        }

        private static void EnsureBoundsBoxCollider(GameObject target, Mesh mesh)
        {
            if (target == null || mesh == null) return;
            var box = target.GetComponent<BoxCollider>();
            if (box == null) box = target.AddComponent<BoxCollider>();
            var bounds = mesh.bounds;
            if (bounds.size.sqrMagnitude < 0.0000001f)
            {
                bounds = new Bounds(Vector3.zero, Vector3.one * 0.001f);
            }
            box.center = bounds.center;
            box.size = bounds.size;
            box.isTrigger = false;
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
            float sizeFactor = GetEffectScaleFactor(visual, 3f, 14f);
            float range = visual.Tuning.LedGlowRange > 0f ? visual.Tuning.LedGlowRange : 0.8f;
            range *= sizeFactor * 1.6f;
            light.range = range;
            light.intensity = 0f;
            light.color = visual.BaseColor;
            light.renderMode = LightRenderMode.ForcePixel;
            light.shadows = LightShadows.None;
            visual.GlowLight = light;
        }

        private void EnsureLedGlowFx(ComponentVisual visual)
        {
            if (visual == null || visual.Root == null || visual.LedGlowFx != null) return;
            var fx = CreateFx("LedGlowFx", visual.Root, visual.Tuning.LedGlowOffset, visual.BaseColor,
                visual.BaseColor, 0f, 0f, 0.01f);
            if (fx?.Renderer != null)
            {
                var material = GetLedGlowMaterial();
                if (material != null)
                {
                    fx.Renderer.sharedMaterial = material;
                }
            }
            visual.LedGlowFx = fx;
        }

        private void EnsureStatusLabel(ComponentVisual visual)
        {
            if (visual == null || visual.Root == null || visual.StatusLabel != null) return;
            var labelGo = new GameObject("StatusLabel");
            labelGo.transform.SetParent(visual.Root, false);
            labelGo.transform.localPosition = visual.Tuning.LabelOffset;
            var text = labelGo.AddComponent<TextMesh>();
            text.text = string.Empty;
            text.fontSize = 64;
            text.characterSize = 0.03f;
            text.color = new Color(0.9f, 0.95f, 1f);
            text.alignment = TextAlignment.Center;
            text.anchor = TextAnchor.MiddleCenter;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            visual.StatusLabel = text;
            visual.LabelBaseScale = labelGo.transform.localScale;

            var renderer = labelGo.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sortingOrder = 20;
                if (text.font != null && renderer.sharedMaterial == null)
                {
                    renderer.sharedMaterial = text.font.material;
                }
                if (renderer.sharedMaterial != null)
                {
                    renderer.sharedMaterial.renderQueue = 3000;
                }
            }
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
            if (visual == null || visual.Root == null || visual.SmokeEmitter != null) return;
            var root = new GameObject("SmokeFx");
            root.transform.SetParent(visual.Root, false);
            root.transform.localPosition = visual.Tuning.SmokeOffset;
            root.transform.localRotation = Quaternion.identity;
            visual.SmokeEmitter = new SmokeEmitter
            {
                Root = root.transform,
                Material = GetSmokeMaterial(),
                SpawnAccumulator = 0f,
                Puffs = new List<SmokePuff>()
            };
        }

        private void EnsureHeatFx(ComponentVisual visual)
        {
            if (visual == null || visual.Root == null || visual.HeatFx != null) return;
            var fx = CreateFx("HeatFx", visual.Root, visual.Tuning.HeatFxOffset, new Color(1f, 0.35f, 0.12f),
                new Color(1f, 0.35f, 0.12f), 1.2f, 0.08f, 0.01f);
            visual.HeatFx = fx;
        }

        private void EnsureSparkFx(ComponentVisual visual)
        {
            if (visual == null || visual.Root == null || visual.SparkFx != null) return;
            var fx = CreateFx("SparkFx", visual.Root, visual.Tuning.SparkFxOffset, new Color(1f, 0.75f, 0.25f),
                new Color(1f, 0.75f, 0.25f), 3.6f, 0.25f, 0.02f);
            visual.SparkFx = fx;
        }

        private void EnsureErrorFx(ComponentVisual visual)
        {
            if (visual == null || visual.Root == null || visual.ErrorFx != null) return;
            var fx = CreateFx("ErrorFx", visual.Root, visual.Tuning.ErrorFxOffset, new Color(1f, 0.2f, 0.2f),
                new Color(1f, 0.2f, 0.2f), 1.6f, 0.1f, 0.012f);
            visual.ErrorFx = fx;
        }

        private void EnsureUsbIndicator(ComponentVisual visual)
        {
            if (visual == null || visual.UsbRenderer != null) return;
            var usb = new GameObject("USB_Indicator");
            usb.transform.SetParent(visual.Root, false);
            usb.transform.localPosition = visual.Tuning.UsbOffset;
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

                    if (_componentVisuals.TryGetValue(compId, out var visual))
                    {
                        radius = Mathf.Max(radius, GetAnchorRadius(visual, DefaultAnchorRadius));
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
            if (pinTransform == null)
            {
                if (ComponentCatalog.TryGetPinAnchor(visual.Type, pin, out var anchorLocal, out var anchorRadius))
                {
                    rootLocalPosition = _root.InverseTransformPoint(visual.Root.TransformPoint(anchorLocal));
                    radius = anchorRadius > 0f ? anchorRadius : DefaultAnchorRadius;
                    radius = Mathf.Max(radius, GetAnchorRadius(visual, DefaultAnchorRadius));
                    return true;
                }
                return false;
            }

            var anchor = pinTransform.GetComponent<WireAnchor>();
            if (anchor != null)
            {
                radius = anchor.Radius;
            }
            else
            {
                var scale = pinTransform.lossyScale;
                float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                if (maxScale > 0.0001f)
                {
                    radius = maxScale * 0.5f;
                }
            }

            rootLocalPosition = _root.InverseTransformPoint(pinTransform.position);
            radius = Mathf.Max(radius, GetAnchorRadius(visual, DefaultAnchorRadius));
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
                radius = Mathf.Max(radius, GetAnchorRadius(visual, DefaultAnchorRadius));
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
                bool showError = _errorFxEnabled && hasError;
                bool usbConnected = usbConnectedByBoard != null &&
                    usbConnectedByBoard.TryGetValue(visual.Id, out var connected) && connected;
                ApplyComponentState(visual, spec, circuit, telemetry, usbConnected, showError);
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

        private static bool IsComponentBlown(TelemetryFrame telemetry, string compId)
        {
            if (string.IsNullOrWhiteSpace(compId) || telemetry?.ValidationMessages == null) return false;
            foreach (var msg in telemetry.ValidationMessages)
            {
                if (string.IsNullOrWhiteSpace(msg)) continue;
                if (msg.IndexOf("Component Blown", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    msg.IndexOf(compId, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private void UpdateWireErrors(CircuitSpec circuit, TelemetryFrame telemetry)
        {
            if (_wiresByNet.Count == 0) return;
            var errorNets = _errorFxEnabled ? BuildErrorNetSet(circuit?.Nets, telemetry) : null;
            foreach (var kvp in _wiresByNet)
            {
                bool hasError = _errorFxEnabled && errorNets != null && errorNets.Contains(kvp.Key);
                var list = kvp.Value;
                if (list == null) continue;
                foreach (var wire in list)
                {
                    if (wire == null) continue;
                    wire.SetError(hasError);
                }
            }
        }

        public void ApplyWireHeatmap(TelemetryFrame telemetry)
        {
            if (!_wireHeatmapEnabled || telemetry?.Signals == null) return;
            var netScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in telemetry.Signals)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                if (!kvp.Key.StartsWith("NET:", StringComparison.OrdinalIgnoreCase)) continue;
                string netId = kvp.Key.Substring(4);
                if (string.IsNullOrWhiteSpace(netId)) continue;
                netScores[netId] = Math.Abs(kvp.Value);
            }
            if (netScores.Count == 0)
            {
                ResetWireColors();
                return;
            }

            double minValue = netScores.Values.Min();
            double maxValue = netScores.Values.Max();
            double range = Math.Max(1e-3, maxValue - minValue);

            foreach (var kvp in _wiresByNet)
            {
                var list = kvp.Value;
                if (list == null || list.Count == 0) continue;
                float intensity = 0f;
                if (netScores.TryGetValue(kvp.Key, out var value))
                {
                    intensity = (float)((value - minValue) / range);
                    intensity = Mathf.Clamp01(intensity);
                }
                Color baseColor = GetNetColor(kvp.Key);
                Color heatColor = Color.Lerp(baseColor, Color.red, intensity);
                foreach (var wire in list)
                {
                    wire?.SetColor(heatColor);
                }
            }
        }

        private void ResetWireColors()
        {
            foreach (var kvp in _wiresByNet)
            {
                var list = kvp.Value;
                if (list == null || list.Count == 0) continue;
                Color baseColor = GetNetColor(kvp.Key);
                foreach (var wire in list)
                {
                    wire?.SetColor(baseColor);
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
                if (msg.IndexOf("Floating", StringComparison.OrdinalIgnoreCase) >= 0) continue;
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

        private void ApplyComponentState(ComponentVisual visual, ComponentSpec spec, CircuitSpec circuit, TelemetryFrame telemetry, bool usbConnected, bool hasError)
        {
            if (visual == null) return;
            string stateId = ResolveStateId(visual, spec);
            if (!string.Equals(stateId, visual.ActiveStateId, StringComparison.OrdinalIgnoreCase))
            {
                ApplyOverrides(visual, stateId);
                visual.ActiveStateId = stateId;
            }

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
            bool isBlown = IsComponentBlown(telemetry, visual.Id);
            float ledIntensity = 0f;
            bool hasLedIntensity = false;

            if (TryGetTelemetrySignalAny(telemetry, out var tempRaw,
                    $"COMP:{visual.Id}:T",
                    $"{visual.Id}.T",
                    $"{visual.Id}:T"))
            {
                tempC = (float)tempRaw;
                hasTemp = true;
            }

            if (TryGetTelemetrySignalAny(telemetry, out var currentRaw,
                    $"COMP:{visual.Id}:I",
                    $"{visual.Id}.I",
                    $"{visual.Id}:I"))
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
                float effectScale = GetEffectScaleFactor(visual, 3f, 14f);
                bool hasLedLum = false;
                if (TryGetTelemetrySignalAny(telemetry, out var lum,
                        $"COMP:{visual.Id}:L",
                        $"{visual.Id}.L",
                        $"{visual.Id}:L"))
                {
                    intensity = Mathf.Clamp01((float)lum);
                    hasLedLum = true;
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
                ledIntensity = intensity;
                hasLedIntensity = hasLedLum || hasCurrent;
                if (ledBlown)
                {
                    displayColor = Color.Lerp(displayColor, new Color(0.12f, 0.12f, 0.12f), 0.6f);
                    emissionColor = Color.black;
                }
                if (visual.GlowLight != null)
                {
                    visual.GlowLight.color = baseColor;
                    float glowRange = tuning.LedGlowRange > 0f ? tuning.LedGlowRange : 0.8f;
                    glowRange *= effectScale * 1.6f * LedLightRangeBoost;
                    visual.GlowLight.range = glowRange;
                    float glowIntensity = tuning.LedGlowIntensity > 0f ? tuning.LedGlowIntensity : 12f;
                    glowIntensity *= LedLightIntensityBoost;
                    float lit = Mathf.Pow(Mathf.Clamp01(intensity), 0.6f);
                    visual.GlowLight.intensity = lit * glowIntensity;
                    visual.GlowLight.enabled = lit > 0.01f;
                }
                UpdateLedGlowFx(visual, baseColor, intensity, effectScale);
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

            if (!HasStateOverrides(visual) && (visual.IsSwitch || visual.IsButton))
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

            if (!HasStateOverrides(visual) && visual.IsServo && TryGetServoAngle(spec, out var servoAngle))
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
                    if (TryGetTelemetrySignalAny(telemetry, out var v,
                            $"SRC:{visual.Id}:V",
                            $"COMP:{visual.Id}:V",
                            $"{visual.Id}.V",
                            $"{visual.Id}:V"))
                    {
                        voltage = v;
                    }
                    if (TryGetTelemetrySignalAny(telemetry, out var i,
                            $"SRC:{visual.Id}:I",
                            $"COMP:{visual.Id}:I",
                            $"{visual.Id}.I",
                            $"{visual.Id}:I"))
                    {
                        current = i;
                    }
                    if (TryGetTelemetrySignalAny(telemetry, out var socVal,
                            $"SRC:{visual.Id}:SOC",
                            $"COMP:{visual.Id}:SOC",
                            $"{visual.Id}.SOC",
                            $"{visual.Id}:SOC"))
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
                else if (visual.IsLed)
                {
                    var lines = new List<string>();
                    if (hasLedIntensity)
                    {
                        lines.Add($"L:{ledIntensity * 100f:F0}%");
                    }
                    if (hasCurrent)
                    {
                        lines.Add($"I:{currentA * 1000f:F1}mA");
                    }
                    if (TryGetTelemetrySignalAny(telemetry, out var v,
                            $"COMP:{visual.Id}:V",
                            $"{visual.Id}.V",
                            $"{visual.Id}:V"))
                    {
                        lines.Add($"V:{Mathf.Abs((float)v):F2}V");
                    }
                    if (hasTemp)
                    {
                        lines.Add($"T:{tempC:F0}C");
                    }
                    labelText = lines.Count > 0 ? string.Join("\n", lines) : "LED";
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
            else if (string.IsNullOrWhiteSpace(labelText))
            {
                labelText = visual.Id;
            }

            ApplyRendererColors(visual, displayColor, emissionColor);
            if (visual.IsResistor)
            {
                ApplyResistorLegColors(visual, resistorHeat);
            }
            UpdateErrorFx(visual, hasError);
            UpdateSparkFx(visual);
            if (hasCurrent && currentA > 0.0005f)
            {
                float sparkIntensity = Mathf.Clamp01(currentA / 0.25f);
                bool sparkActive = hasError || isBlown;
                UpdateSparkShower(visual, sparkIntensity, sparkActive);
            }
            else
            {
                UpdateSparkShower(visual, 0f, false);
            }
            if (isBlown && ShouldSparkBlownComponent(visual, spec, circuit, telemetry))
            {
                float interval = 0.1f + 0.2f * Mathf.Abs(Mathf.Sin(Time.time * 7f + visual.ErrorSeed));
                if (Time.time - visual.LastSparkFxTime > interval)
                {
                    TriggerSparkFx(visual);
                }
            }
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
                bool hasEmission = emissionColor.maxColorComponent > 0.0001f;
                if (!visual.AllowTint)
                {
                    if (hasEmission && renderer.sharedMaterial != null &&
                        (renderer.sharedMaterial.HasProperty("_EmissionColor") || renderer.sharedMaterial.HasProperty("_EmissiveColor")))
                    {
                        EnableEmission(renderer);
                        block.Clear();
                        block.SetColor("_EmissionColor", emissionColor);
                        block.SetColor("_EmissiveColor", emissionColor);
                        renderer.SetPropertyBlock(block);
                    }
                    else
                    {
                        renderer.SetPropertyBlock(null);
                    }
                    continue;
                }

                bool hasTexture = RendererHasTexture(renderer);
                block.Clear();
                if (!hasTexture)
                {
                    block.SetColor("_Color", baseColor);
                    block.SetColor("_BaseColor", baseColor);
                }
                if (hasEmission)
                {
                    EnableEmission(renderer);
                }
                block.SetColor("_EmissionColor", emissionColor);
                block.SetColor("_EmissiveColor", emissionColor);
                renderer.SetPropertyBlock(block);
            }
        }

        private void UpdateResistorSmoke(ComponentVisual visual, float tempC)
        {
            if (visual == null) return;
            if (float.IsNaN(tempC))
            {
                ClearSmokeEmitter(visual);
                return;
            }

            float smokeStart = visual.Tuning.ResistorSmokeStartTemp > 0f ? visual.Tuning.ResistorSmokeStartTemp : 110f;
            float smokeFull = visual.Tuning.ResistorSmokeFullTemp > 0f ? visual.Tuning.ResistorSmokeFullTemp : 170f;
            float smokeAmount = Mathf.InverseLerp(smokeStart, smokeFull, tempC);
            if (smokeAmount <= 0.01f)
            {
                ClearSmokeEmitter(visual);
                return;
            }

            EnsureSmokeFx(visual);
            UpdateSmokeEmitter(visual, smokeAmount, tempC);
        }

        private void ClearSmokeEmitter(ComponentVisual visual)
        {
            if (visual?.SmokeEmitter == null) return;
            foreach (var puff in visual.SmokeEmitter.Puffs)
            {
                if (puff?.Transform == null) continue;
                if (Application.isPlaying)
                {
                    Destroy(puff.Transform.gameObject);
                }
                else
                {
                    DestroyImmediate(puff.Transform.gameObject);
                }
            }
            visual.SmokeEmitter.Puffs.Clear();
            if (visual.SmokeEmitter.Root != null)
            {
                visual.SmokeEmitter.Root.gameObject.SetActive(false);
            }
        }

        private void UpdateSmokeEmitter(ComponentVisual visual, float smokeAmount, float tempC)
        {
            if (visual?.SmokeEmitter == null || visual.SmokeEmitter.Root == null) return;
            var emitter = visual.SmokeEmitter;
            if (!emitter.Root.gameObject.activeSelf)
            {
                emitter.Root.gameObject.SetActive(true);
            }

            float effectScale = GetEffectScaleFactor(visual, 4f, 20f) * 1.35f;
            Vector3 localUp = visual.Root.InverseTransformDirection(Vector3.up);
            if (localUp.sqrMagnitude < 0.001f) localUp = Vector3.up;
            localUp.Normalize();
            emitter.LocalUp = localUp;

            if (TryGetWorldBounds(visual.Root, out var bounds))
            {
                var worldPos = bounds.center + Vector3.up * Mathf.Max(0.015f, bounds.extents.y * 0.8f);
                emitter.Root.position = worldPos;
            }

            float spawnRate = Mathf.Lerp(4f, 14f, smokeAmount);
            emitter.SpawnAccumulator += spawnRate * Time.deltaTime;
            int maxPuffs = Mathf.RoundToInt(Mathf.Lerp(18f, 52f, smokeAmount));

            while (emitter.SpawnAccumulator >= 1f && emitter.Puffs.Count < maxPuffs)
            {
                emitter.SpawnAccumulator -= 1f;
                var puff = CreateSmokePuff(emitter, effectScale, smokeAmount);
                if (puff != null)
                {
                    emitter.Puffs.Add(puff);
                }
            }

            float dt = Time.deltaTime;
            for (int i = emitter.Puffs.Count - 1; i >= 0; i--)
            {
                var puff = emitter.Puffs[i];
                if (puff == null || puff.Transform == null)
                {
                    emitter.Puffs.RemoveAt(i);
                    continue;
                }
                puff.Age += dt;
                if (puff.Age >= puff.Lifetime)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(puff.Transform.gameObject);
                    }
                    else
                    {
                        DestroyImmediate(puff.Transform.gameObject);
                    }
                    emitter.Puffs.RemoveAt(i);
                    continue;
                }

                float buoyancy = Mathf.Lerp(0.03f, 0.08f, smokeAmount);
                puff.Velocity += emitter.LocalUp * buoyancy * dt;
                puff.LocalPos += puff.Velocity * dt;
                puff.Transform.localPosition = puff.LocalPos;
                float t = Mathf.Clamp01(puff.Age / puff.Lifetime);
                float scale = Mathf.Lerp(puff.StartScale, puff.EndScale, t);
                puff.Transform.localScale = Vector3.one * scale;
                Color color = Color.Lerp(puff.StartColor, puff.EndColor, t);
                ApplySmokeColor(puff.Renderer, color);
                if (_camera != null)
                {
                    puff.Rotation += puff.AngularVelocity * dt;
                    puff.Transform.rotation = _camera.transform.rotation * Quaternion.AngleAxis(puff.Rotation, Vector3.forward);
                }
            }
        }

        private SmokePuff CreateSmokePuff(SmokeEmitter emitter, float effectScale, float smokeAmount)
        {
            if (emitter?.Root == null) return null;
            var puffGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            puffGo.name = "SmokePuff";
            puffGo.transform.SetParent(emitter.Root, false);
            puffGo.transform.localRotation = Quaternion.identity;
            var collider = puffGo.GetComponent<Collider>();
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

            var renderer = puffGo.GetComponent<Renderer>();
            if (renderer != null && emitter.Material != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterial = emitter.Material;
            }

            float scaleFactor = Mathf.Sqrt(Mathf.Max(0.8f, effectScale));
            Vector2 spread = UnityEngine.Random.insideUnitCircle * Mathf.Lerp(0.01f, 0.03f, smokeAmount) * scaleFactor;
            Vector3 localPos = new Vector3(spread.x, 0f, spread.y);
            float startScale = UnityEngine.Random.Range(0.03f, 0.07f) * scaleFactor;
            float endScale = startScale * UnityEngine.Random.Range(2.8f, 5.2f);
            float lifetime = Mathf.Lerp(1.8f, 4.6f, smokeAmount);
            float speed = Mathf.Lerp(0.07f, 0.19f, smokeAmount) * scaleFactor;
            Vector3 drift = new Vector3(
                UnityEngine.Random.Range(-0.01f, 0.01f),
                UnityEngine.Random.Range(-0.003f, 0.008f),
                UnityEngine.Random.Range(-0.01f, 0.01f));
            Vector3 velocity = emitter.LocalUp * speed + drift;
            var startColor = new Color(0.22f, 0.22f, 0.22f, Mathf.Lerp(0.28f, 0.6f, smokeAmount));
            var endColor = new Color(0.1f, 0.1f, 0.1f, 0f);
            float rotation = UnityEngine.Random.Range(0f, 360f);
            float angular = UnityEngine.Random.Range(-25f, 25f);

            puffGo.transform.localPosition = localPos;
            puffGo.transform.localScale = Vector3.one * startScale;

            var puff = new SmokePuff
            {
                Transform = puffGo.transform,
                Renderer = renderer,
                LocalPos = localPos,
                Velocity = velocity,
                Age = 0f,
                Lifetime = lifetime,
                StartScale = startScale,
                EndScale = endScale,
                StartColor = startColor,
                EndColor = endColor,
                Rotation = rotation,
                AngularVelocity = angular
            };
            ApplySmokeColor(renderer, startColor);
            return puff;
        }

        private static void ApplySmokeColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            block.SetColor("_EmissionColor", color * 0.05f);
            block.SetColor("_EmissiveColor", color * 0.05f);
            block.SetColor("_TintColor", color);
            EnableEmission(renderer);
            renderer.SetPropertyBlock(block);
        }

        private void ApplyResistorLegColors(ComponentVisual visual, float heat01)
        {
            if (visual?.LegRenderers == null || visual.LegRenderers.Length == 0) return;
            float heat = Mathf.Clamp01(heat01);
            var coolColor = new Color(0.55f, 0.55f, 0.6f);
            var hotColor = new Color(1f, 0.38f, 0.12f);
            var legColor = Color.Lerp(coolColor, hotColor, heat);
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", legColor);
            block.SetColor("_BaseColor", legColor);
            block.SetColor("_EmissionColor", legColor * (0.2f + heat * 0.85f));
            block.SetColor("_EmissiveColor", legColor * (0.2f + heat * 0.85f));
            foreach (var renderer in visual.LegRenderers)
            {
                if (renderer == null) continue;
                if (RendererHasTexture(renderer)) continue;
                EnableEmission(renderer);
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
            float scale = Mathf.Lerp(0.9f, 2.1f, heat);
            float sizeFactor = GetEffectScaleFactor(visual, 3f, 14f);
            visual.HeatFx.Root.transform.localScale = visual.HeatFx.BaseScale * (scale * sizeFactor);
            var heatColor = Color.Lerp(new Color(1f, 0.3f, 0.12f), new Color(1f, 0.08f, 0.02f), heat);
            float intensity = Mathf.Lerp(0.6f, 2.4f, heat);
            ApplyFxColor(visual.HeatFx, heatColor, heatColor, intensity);
            if (visual.HeatFx.Light != null)
            {
                visual.HeatFx.Light.range = Mathf.Lerp(0.18f, 0.6f, heat) * sizeFactor;
            }
        }

        private void UpdateLedGlowFx(ComponentVisual visual, Color color, float intensity, float sizeFactor)
        {
            if (visual == null) return;
            if (intensity <= 0.01f)
            {
                SetFxActive(visual.LedGlowFx, false);
                return;
            }

            EnsureLedGlowFx(visual);
            if (visual.LedGlowFx == null) return;
            SetFxActive(visual.LedGlowFx, true);
            float t = Mathf.Pow(Mathf.Clamp01(intensity), 0.6f);
            float glowRange = visual.GlowLight != null ? visual.GlowLight.range : sizeFactor * 0.2f;
            float parentScale = 1f;
            if (visual.Root != null)
            {
                var lossy = visual.Root.lossyScale;
                parentScale = Mathf.Max(lossy.x, Mathf.Max(lossy.y, lossy.z));
            }
            float localRange = glowRange / Mathf.Max(0.001f, parentScale);
            float radius = Mathf.Clamp(localRange * 0.22f, 0.008f, localRange);
            float scale = Mathf.Lerp(radius * 0.5f, radius, t) * LedGlowFxRangeBoost;
            visual.LedGlowFx.Root.transform.localScale = Vector3.one * scale;
            var glowColor = Color.Lerp(color * 0.35f, color, t);
            glowColor.a = Mathf.Lerp(0.02f, 0.12f, t);
            ApplyFxColor(visual.LedGlowFx, glowColor, glowColor, 0f);
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
            float sizeFactor = GetEffectScaleFactor(visual, 3f, 14f);
            visual.ErrorFx.Root.transform.localScale = visual.ErrorFx.BaseScale * (scale * sizeFactor);
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
            float scale = Mathf.Lerp(0.9f, 2.2f, t);
            float sizeFactor = GetEffectScaleFactor(visual, 4f, 18f);
            visual.SparkFx.Root.transform.localScale = visual.SparkFx.BaseScale * (scale * sizeFactor);
            ApplyFxColor(visual.SparkFx, new Color(1f, 0.8f, 0.3f), new Color(1f, 0.8f, 0.3f), 3.4f * t);
            if (visual.SparkFx.Light != null)
            {
                visual.SparkFx.Light.range = Mathf.Lerp(0.25f, 0.8f, t) * sizeFactor;
            }
        }

        private void UpdateSparkShower(ComponentVisual visual, float intensity, bool active)
        {
            if (visual == null)
            {
                return;
            }

            if (!active || intensity <= 0.01f)
            {
                ClearSparkBurst(visual);
                return;
            }

            EnsureSparkBurst(visual);
            if (visual.SparkBurst == null || visual.SparkBurstRoot == null) return;
            if (!visual.SparkBurstRoot.gameObject.activeSelf)
            {
                visual.SparkBurstRoot.gameObject.SetActive(true);
            }

            if (TryGetWorldBounds(visual.Root, out var bounds))
            {
                visual.SparkBurstRoot.position = bounds.center + Vector3.up * Mathf.Max(0.01f, bounds.extents.y * 0.5f);
            }

            float now = Time.time;
            float interval = Mathf.Lerp(0.18f, 0.06f, Mathf.Clamp01(intensity));
            if (now - visual.LastSparkBurstTime >= interval)
            {
                EmitSparkBurst(visual, intensity);
                visual.LastSparkBurstTime = now;
            }

            float dt = Time.deltaTime;
            Vector3 gravity = Physics.gravity * SparkShowerGravity;
            if (visual.Root != null)
            {
                gravity = visual.Root.InverseTransformDirection(gravity);
            }

            for (int i = visual.SparkBurst.Count - 1; i >= 0; i--)
            {
                var particle = visual.SparkBurst[i];
                if (particle?.Transform == null)
                {
                    visual.SparkBurst.RemoveAt(i);
                    continue;
                }
                particle.Age += dt;
                if (particle.Age >= particle.Lifetime)
                {
                    DestroySparkParticle(particle);
                    visual.SparkBurst.RemoveAt(i);
                    continue;
                }

                particle.Velocity += gravity * dt;
                particle.Velocity *= 1f - Mathf.Clamp01(dt * 0.6f);
                particle.LocalPos += particle.Velocity * dt;
                particle.Transform.localPosition = particle.LocalPos;

                float t = Mathf.Clamp01(particle.Age / particle.Lifetime);
                float scale = Mathf.Lerp(particle.StartScale, particle.EndScale, t);
                particle.Transform.localScale = Vector3.one * scale;
                Color color = Color.Lerp(particle.StartColor, particle.EndColor, t);
                ApplySparkParticleColor(particle, color, t);
            }
        }

        private void EnsureSparkBurst(ComponentVisual visual)
        {
            if (visual == null || visual.Root == null || visual.SparkBurstRoot != null) return;
            var root = new GameObject("SparkBurst");
            root.transform.SetParent(visual.Root, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            visual.SparkBurstRoot = root.transform;
            visual.SparkBurst = new List<SparkBurstParticle>();
        }

        private void ClearSparkBurst(ComponentVisual visual)
        {
            if (visual?.SparkBurst == null) return;
            for (int i = visual.SparkBurst.Count - 1; i >= 0; i--)
            {
                var particle = visual.SparkBurst[i];
                DestroySparkParticle(particle);
            }
            visual.SparkBurst.Clear();
            if (visual.SparkBurstRoot != null)
            {
                visual.SparkBurstRoot.gameObject.SetActive(false);
            }
        }

        private void EmitSparkBurst(ComponentVisual visual, float intensity)
        {
            if (visual?.SparkBurstRoot == null || visual.SparkBurst == null) return;
            float sizeFactor = GetEffectScaleFactor(visual, 4f, 16f);
            int count = Mathf.RoundToInt(Mathf.Lerp(4f, 12f, intensity));
            for (int i = 0; i < count && visual.SparkBurst.Count < SparkShowerMaxParticles; i++)
            {
                var particle = CreateSparkParticle(visual.SparkBurstRoot, intensity, sizeFactor);
                if (particle != null)
                {
                    visual.SparkBurst.Add(particle);
                }
            }
        }

        private SparkBurstParticle CreateSparkParticle(Transform parent, float intensity, float sizeFactor)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Spark";
            go.transform.SetParent(parent, false);
            var collider = go.GetComponent<Collider>();
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

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var material = GetSparkMaterial();
                if (material != null)
                {
                    renderer.sharedMaterial = material;
                }
            }

            Vector3 direction = UnityEngine.Random.onUnitSphere;
            direction.y = Mathf.Abs(direction.y) + 0.4f;
            direction.Normalize();
            float speed = Mathf.Lerp(0.14f, 0.34f, UnityEngine.Random.value) * sizeFactor;
            Vector3 velocity = direction * speed;

            float startScale = UnityEngine.Random.Range(0.008f, 0.02f) * sizeFactor;
            float endScale = startScale * UnityEngine.Random.Range(0.3f, 0.6f);
            float lifetime = Mathf.Lerp(0.35f, 0.85f, UnityEngine.Random.value);
            var startColor = Color.Lerp(new Color(1f, 0.85f, 0.45f), new Color(1f, 0.95f, 0.7f), UnityEngine.Random.value);
            var endColor = new Color(1f, 0.25f, 0.05f, 0f);

            Light light = null;
            if (UnityEngine.Random.value > 0.45f)
            {
                light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = startColor;
                light.intensity = Mathf.Lerp(1.2f, 3.2f, intensity);
                light.range = Mathf.Lerp(0.16f, 0.45f, intensity) * sizeFactor;
                light.shadows = LightShadows.None;
            }

            var particle = new SparkBurstParticle
            {
                Transform = go.transform,
                Renderer = renderer,
                Light = light,
                LocalPos = Vector3.zero,
                Velocity = velocity,
                Age = 0f,
                Lifetime = lifetime,
                StartScale = startScale,
                EndScale = endScale,
                StartColor = startColor,
                EndColor = endColor
            };

            ApplySparkParticleColor(particle, startColor, 0f);
            return particle;
        }

        private static void DestroySparkParticle(SparkBurstParticle particle)
        {
            if (particle?.Transform == null) return;
            if (Application.isPlaying)
            {
                Destroy(particle.Transform.gameObject);
            }
            else
            {
                DestroyImmediate(particle.Transform.gameObject);
            }
        }

        private static void ApplySparkParticleColor(SparkBurstParticle particle, Color color, float t)
        {
            if (particle == null) return;
            if (particle.Renderer != null)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
                block.SetColor("_EmissionColor", color * Mathf.Lerp(2.4f, 0.2f, t));
                block.SetColor("_EmissiveColor", color * Mathf.Lerp(2.4f, 0.2f, t));
                particle.Renderer.SetPropertyBlock(block);
            }
            if (particle.Light != null)
            {
                particle.Light.color = color;
                particle.Light.intensity = Mathf.Lerp(particle.Light.intensity, 0f, t);
                particle.Light.enabled = particle.Light.intensity > 0.01f;
            }
        }

        private void UpdateStatusLabel(ComponentVisual visual, string text, Color color)
        {
            if (visual?.StatusLabel == null) return;
            bool hasText = !string.IsNullOrWhiteSpace(text);
            visual.StatusLabel.text = hasText ? text : string.Empty;
            visual.StatusLabel.color = color;
            ApplyLabelVisibility(visual, hasText);
            if (!_labelsVisible || !hasText || _camera == null) return;
            var labelTransform = visual.StatusLabel.transform;
            UpdateLabelTransform(visual, labelTransform);
            UpdateBillboardRotation(labelTransform, true);
        }

        private void ApplyLabelVisibility(ComponentVisual visual, bool hasText)
        {
            if (visual?.StatusLabel == null) return;
            visual.StatusLabel.gameObject.SetActive(_labelsVisible && hasText);
        }

        private void UpdateLabelTransform(ComponentVisual visual, Transform labelTransform)
        {
            if (visual == null || labelTransform == null || visual.Root == null) return;
            if (TryGetWorldBounds(visual.Root, out var bounds))
            {
                var worldPos = bounds.center;
                if (_camera != null)
                {
                    float push = Mathf.Max(0.02f, bounds.size.magnitude * 0.04f);
                    worldPos += -_camera.transform.forward * push;
                    worldPos += _camera.transform.up * Mathf.Max(0.01f, bounds.size.y * 0.05f);
                }
                else
                {
                    worldPos += Vector3.up * Mathf.Max(0.01f, bounds.size.y * 0.05f);
                }
                labelTransform.position = worldPos;
                float sizeBase = Mathf.Max(bounds.size.x, bounds.size.z);
                visual.StatusLabel.characterSize = Mathf.Clamp(sizeBase * 0.28f, 0.035f, 0.18f);
            }

            var parentScale = visual.Root.lossyScale;
            var scale = visual.LabelBaseScale;
            scale.x *= parentScale.x < 0f ? -1f : 1f;
            scale.y *= parentScale.y < 0f ? -1f : 1f;
            scale.z *= parentScale.z < 0f ? -1f : 1f;
            float sizeFactor = GetVisualScaleFactor(visual, 0.9f, 3f);
            scale *= sizeFactor * LabelScaleBoost;
            labelTransform.localScale = scale;
        }

        private void UpdateBillboardBars(ComponentVisual visual, float tempC, bool hasTemp, TelemetryFrame telemetry)
        {
            if (visual == null) return;
            if (visual.BatteryBar != null)
            {
                bool hasSoc = TryGetTelemetrySignalAny(telemetry, out var socVal,
                    $"SRC:{visual.Id}:SOC",
                    $"COMP:{visual.Id}:SOC",
                    $"{visual.Id}.SOC",
                    $"{visual.Id}:SOC");
                float value = hasSoc ? Mathf.Clamp01((float)socVal) : 0f;
                UpdateBillboardBarTransform(visual, visual.BatteryBar, 0.012f);
                SetBillboardBarValue(visual.BatteryBar, value, true);
            }

            if (visual.TempBar != null)
            {
                float value = hasTemp ? Mathf.Clamp01(Mathf.InverseLerp(25f, 120f, tempC)) : 0f;
                UpdateBillboardBarTransform(visual, visual.TempBar, 0.012f);
                SetBillboardBarValue(visual.TempBar, value, true);
            }
        }

        private void UpdateBillboardBarTransform(ComponentVisual visual, BillboardBar bar, float heightOffset)
        {
            if (visual == null || bar?.Root == null) return;
            if (!TryGetWorldBounds(visual.Root, out var bounds)) return;
            var worldPos = bounds.center;
            if (_camera != null)
            {
                float push = Mathf.Max(0.01f, bounds.size.magnitude * 0.02f);
                worldPos += -_camera.transform.forward * push;
            }
            else
            {
                worldPos += Vector3.up * Mathf.Max(0.01f, bounds.size.y * 0.1f + heightOffset);
            }
            bar.Root.position = worldPos;
            float scaleFactor = GetVisualScaleFactor(visual);
            bar.Root.localScale = Vector3.one * scaleFactor;
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
                renderer.material = GetBarMaterial() ?? renderer.material;
                renderer.sortingOrder = 15;
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

        private float GetAnchorRadius(ComponentVisual visual, float baseRadius)
        {
            float factor = GetVisualScaleFactor(visual, 0.8f, 4f);
            return baseRadius * factor;
        }

        private float GetVisualScaleFactor(ComponentVisual visual, float min = 0.6f, float max = 3f)
        {
            if (visual?.Root == null) return 1f;
            if (!TryGetWorldBounds(visual.Root, out var bounds)) return 1f;
            float maxDim = Mathf.Max(bounds.size.x, bounds.size.z);
            if (maxDim <= 0.0001f) return 1f;
            float factor = maxDim * 12f;
            return Mathf.Clamp(factor, min, max);
        }

        private float GetEffectScaleFactor(ComponentVisual visual, float min = 2f, float max = 8f)
        {
            if (visual?.Root == null) return 1f;
            if (!TryGetWorldBounds(visual.Root, out var bounds)) return 1f;
            float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (maxDim <= 0.0001f) return 1f;
            float factor = maxDim * 50f;
            return Mathf.Clamp(factor, min, max);
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

        private static bool TryGetTelemetrySignalAny(TelemetryFrame telemetry, out double value, params string[] keys)
        {
            value = 0.0;
            if (telemetry?.Signals == null || keys == null || keys.Length == 0) return false;
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (telemetry.Signals.TryGetValue(key, out value)) return true;
            }
            return false;
        }

        private static bool TryGetNetId(CircuitSpec circuit, string compId, string pin, out string netId)
        {
            netId = null;
            if (circuit?.Nets == null || string.IsNullOrWhiteSpace(compId) || string.IsNullOrWhiteSpace(pin)) return false;
            string node = $"{compId}.{pin}";
            foreach (var net in circuit.Nets)
            {
                if (net?.Nodes == null || string.IsNullOrWhiteSpace(net.Id)) continue;
                foreach (var n in net.Nodes)
                {
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    if (string.Equals(n, node, StringComparison.OrdinalIgnoreCase))
                    {
                        netId = net.Id;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool TryGetPinVoltage(CircuitSpec circuit, TelemetryFrame telemetry, string compId, string pin, out double voltage)
        {
            voltage = 0.0;
            if (!TryGetNetId(circuit, compId, pin, out var netId)) return false;
            return TryGetTelemetrySignal(telemetry, $"NET:{netId}", out voltage);
        }

        private static bool ShouldSparkBlownComponent(ComponentVisual visual, ComponentSpec spec, CircuitSpec circuit, TelemetryFrame telemetry)
        {
            if (visual == null || spec == null || telemetry?.Signals == null) return false;
            string pinA = null;
            string pinB = null;
            if (visual.IsResistor)
            {
                pinA = "A";
                pinB = "B";
            }
            else if (visual.IsLed)
            {
                pinA = "Anode";
                pinB = "Cathode";
            }
            if (pinA == null || pinB == null) return false;
            if (!TryGetPinVoltage(circuit, telemetry, spec.Id, pinA, out var vA)) return false;
            if (!TryGetPinVoltage(circuit, telemetry, spec.Id, pinB, out var vB)) return false;
            double vDiff = Math.Abs(vA - vB);
            return vDiff > 0.5;
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
            if (asset != null)
            {
                var file = JsonUtility.FromJson<ComponentTuningFile>(asset.text);
                if (file?.Components != null)
                {
                    foreach (var entry in file.Components)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.Type)) continue;
                        ComponentTunings[entry.Type] = entry.ToTuning();
                    }
                }
            }

            AppendCatalogTunings();
        }

        private static void AppendCatalogTunings()
        {
            ComponentCatalog.EnsureLoaded();
            foreach (var item in ComponentCatalog.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Type) || !item.HasTuning) continue;
                var tuning = ConvertTuning(item.Tuning);
                if (ComponentTunings.TryGetValue(item.Type, out var existing))
                {
                    ComponentTunings[item.Type] = MergeTuning(existing, tuning);
                }
                else
                {
                    ComponentTunings[item.Type] = tuning;
                }
            }
        }

        private static ComponentTuning ConvertTuning(ComponentCatalog.Tuning tuning)
        {
            return new ComponentTuning
            {
                Euler = tuning.Euler,
                Scale = tuning.Scale,
                UseLedColor = tuning.UseLedColor,
                LedColor = tuning.LedColor,
                LedGlowRange = tuning.LedGlowRange,
                LedGlowIntensity = tuning.LedGlowIntensity,
                LedBlowCurrent = tuning.LedBlowCurrent,
                LedBlowTemp = tuning.LedBlowTemp,
                ResistorSmokeStartTemp = tuning.ResistorSmokeStartTemp,
                ResistorSmokeFullTemp = tuning.ResistorSmokeFullTemp,
                ResistorHotStartTemp = tuning.ResistorHotStartTemp,
                ResistorHotFullTemp = tuning.ResistorHotFullTemp,
                ErrorFxInterval = tuning.ErrorFxInterval,
                LabelOffset = tuning.LabelOffset,
                LedGlowOffset = tuning.LedGlowOffset,
                HeatFxOffset = tuning.HeatFxOffset,
                SparkFxOffset = tuning.SparkFxOffset,
                ErrorFxOffset = tuning.ErrorFxOffset,
                SmokeOffset = tuning.SmokeOffset,
                UsbOffset = tuning.UsbOffset
            };
        }

        private static ComponentTuning BuildDefaultTuning(string type)
        {
            var tuning = new ComponentTuning
            {
                Euler = DefaultPrefabEuler,
                Scale = Vector3.one,
                UseLedColor = false,
                LedColor = new Color(1f, 0.2f, 0.2f, 1f),
                LedGlowRange = 0.8f,
                LedGlowIntensity = 12f,
                LedBlowCurrent = 0.08f,
                LedBlowTemp = 140f,
                ResistorSmokeStartTemp = 110f,
                ResistorSmokeFullTemp = 170f,
                ResistorHotStartTemp = 60f,
                ResistorHotFullTemp = 140f,
                ErrorFxInterval = 0.75f,
                LabelOffset = new Vector3(0f, 0.05f, 0f),
                LedGlowOffset = new Vector3(0f, 0.01f, 0f),
                HeatFxOffset = new Vector3(0f, 0.015f, 0f),
                SparkFxOffset = new Vector3(0f, 0.02f, 0f),
                ErrorFxOffset = new Vector3(0f, 0.03f, 0f),
                SmokeOffset = Vector3.zero,
                UsbOffset = new Vector3(0f, 0.012f, -0.05f)
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
            if (overrideTuning.LabelOffset.sqrMagnitude > 0.0001f) baseTuning.LabelOffset = overrideTuning.LabelOffset;
            if (overrideTuning.LedGlowOffset.sqrMagnitude > 0.0001f) baseTuning.LedGlowOffset = overrideTuning.LedGlowOffset;
            if (overrideTuning.HeatFxOffset.sqrMagnitude > 0.0001f) baseTuning.HeatFxOffset = overrideTuning.HeatFxOffset;
            if (overrideTuning.SparkFxOffset.sqrMagnitude > 0.0001f) baseTuning.SparkFxOffset = overrideTuning.SparkFxOffset;
            if (overrideTuning.ErrorFxOffset.sqrMagnitude > 0.0001f) baseTuning.ErrorFxOffset = overrideTuning.ErrorFxOffset;
            if (overrideTuning.SmokeOffset.sqrMagnitude > 0.0001f) baseTuning.SmokeOffset = overrideTuning.SmokeOffset;
            if (overrideTuning.UsbOffset.sqrMagnitude > 0.0001f) baseTuning.UsbOffset = overrideTuning.UsbOffset;

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
            public Vector3 LabelOffset;
            public Vector3 LedGlowOffset;
            public Vector3 HeatFxOffset;
            public Vector3 SparkFxOffset;
            public Vector3 ErrorFxOffset;
            public Vector3 SmokeOffset;
            public Vector3 UsbOffset;

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
                    ErrorFxInterval = ErrorFxInterval,
                    LabelOffset = LabelOffset,
                    LedGlowOffset = LedGlowOffset,
                    HeatFxOffset = HeatFxOffset,
                    SparkFxOffset = SparkFxOffset,
                    ErrorFxOffset = ErrorFxOffset,
                    SmokeOffset = SmokeOffset,
                    UsbOffset = UsbOffset
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
            public Vector3 LabelOffset;
            public Vector3 LedGlowOffset;
            public Vector3 HeatFxOffset;
            public Vector3 SparkFxOffset;
            public Vector3 ErrorFxOffset;
            public Vector3 SmokeOffset;
            public Vector3 UsbOffset;
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
            if (prefab != null && !go.activeSelf) go.SetActive(true);
            DisableBoxColliders(go);
            return go;
        }

        private GameObject GetPrefabForType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return _genericPrefab;
            if (TryGetCustomPrefab(type, out var customPrefab)) return customPrefab;
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

        private bool TryGetCustomPrefab(string type, out GameObject prefab)
        {
            prefab = null;
            var item = ComponentCatalog.GetByType(type);
            if (string.IsNullOrWhiteSpace(item.Type)) return false;
            if (string.IsNullOrWhiteSpace(item.ModelFile)) return false;
            if (string.IsNullOrWhiteSpace(item.SourcePath)) return false;

            string modelPath = string.Empty;
            if (ComponentPackageUtility.IsPackagePath(item.SourcePath))
            {
                if (!ComponentPackageUtility.TryExtractEntryToCache(item.SourcePath, item.ModelFile, out modelPath))
                {
                    return false;
                }
            }
            else if (Directory.Exists(item.SourcePath))
            {
                modelPath = Path.Combine(item.SourcePath, item.ModelFile);
            }

            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath)) return false;
            if (_runtimeModelCache.TryGetValue(modelPath, out prefab) && prefab != null) return true;
            if (_runtimeModelLoads.ContainsKey(modelPath)) return false;

            _runtimeModelLoads[modelPath] = LoadRuntimeModelAsync(modelPath, type);
            return false;
        }

        private async Task LoadRuntimeModelAsync(string modelPath, string type)
        {
            try
            {
                if (_runtimeModelRoot == null)
                {
                    var root = new GameObject("RuntimeModels");
                    root.transform.SetParent(transform, false);
                    _runtimeModelRoot = root.transform;
                }

                var instance = await RuntimeModelLoader.TryLoadModelAsync(modelPath, _runtimeModelRoot.gameObject, default);
                if (instance == null) return;

                instance.name = $"{type}_RuntimeModel";
                instance.SetActive(false);
                _runtimeModelCache[modelPath] = instance;
            }
            finally
            {
                _runtimeModelLoads.Remove(modelPath);
            }

            if (this != null && _lastCircuit != null)
            {
                Build(_lastCircuit);
            }
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

        private static Material GetSmokeMaterial()
        {
            if (_smokeMaterial != null) return _smokeMaterial;
            var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ??
                Shader.Find("Legacy Shaders/Particles/Soft Additive") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Legacy Shaders/Transparent/Diffuse") ??
                Shader.Find("Unlit/Transparent") ??
                Shader.Find("Standard");
            if (shader == null) return null;
            _smokeMaterial = new Material(shader)
            {
                name = "Circuit3D_SmokeMat"
            };
            var texture = GetSmokeTexture();
            if (texture != null)
            {
                if (_smokeMaterial.HasProperty("_MainTex")) _smokeMaterial.SetTexture("_MainTex", texture);
                if (_smokeMaterial.HasProperty("_BaseMap")) _smokeMaterial.SetTexture("_BaseMap", texture);
            }
            ConfigureTransparentMaterial(_smokeMaterial);
            _smokeMaterial.renderQueue = 3000;
            return _smokeMaterial;
        }

        private static Texture2D GetSmokeTexture()
        {
            if (_smokeTexture != null) return _smokeTexture;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "Circuit3D_SmokeTex",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float maxDist = center.magnitude;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center.x;
                    float dy = y - center.y;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Clamp01(dist / maxDist);
                    float falloff = Mathf.Pow(1f - t, 2.6f);
                    float alpha = Mathf.SmoothStep(0f, 1f, falloff);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply(false, true);
            _smokeTexture = tex;
            return _smokeTexture;
        }

        private static Material GetLedGlowMaterial()
        {
            if (_ledGlowMaterial != null) return _ledGlowMaterial;
            var shader = Shader.Find("Legacy Shaders/Particles/Soft Additive") ??
                Shader.Find("Legacy Shaders/Particles/Additive") ??
                Shader.Find("Legacy Shaders/Particles/Alpha Blended") ??
                Shader.Find("Unlit/Transparent") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Standard");
            if (shader == null) return null;
            _ledGlowMaterial = new Material(shader)
            {
                name = "Circuit3D_LedGlowMat"
            };
            var texture = GetLedGlowTexture();
            if (texture != null)
            {
                if (_ledGlowMaterial.HasProperty("_MainTex")) _ledGlowMaterial.SetTexture("_MainTex", texture);
                if (_ledGlowMaterial.HasProperty("_BaseMap")) _ledGlowMaterial.SetTexture("_BaseMap", texture);
            }
            ConfigureTransparentMaterial(_ledGlowMaterial);
            _ledGlowMaterial.renderQueue = 3000;
            return _ledGlowMaterial;
        }

        private static Material GetSparkMaterial()
        {
            if (_sparkMaterial != null) return _sparkMaterial;
            _sparkMaterial = BuildFxMaterial();
            if (_sparkMaterial != null)
            {
                _sparkMaterial.name = "Circuit3D_SparkMat";
            }
            return _sparkMaterial;
        }

        private static Texture2D GetLedGlowTexture()
        {
            if (_ledGlowTexture != null) return _ledGlowTexture;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "Circuit3D_LedGlowTex",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float maxDist = center.magnitude;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center.x;
                    float dy = y - center.y;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Clamp01(dist / maxDist);
                    float falloff = Mathf.Pow(1f - t, 3.2f);
                    float alpha = Mathf.SmoothStep(0f, 1f, falloff);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply(false, true);
            _ledGlowTexture = tex;
            return _ledGlowTexture;
        }

        private static void ConfigureTransparentMaterial(Material material)
        {
            if (material == null) return;
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 3f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        private static Material GetBarMaterial()
        {
            if (_barMaterial != null) return _barMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Sprites/Default");
            if (shader == null) return null;
            _barMaterial = new Material(shader)
            {
                name = "Circuit3D_BarMat"
            };
            _barMaterial.renderQueue = 3000;
            return _barMaterial;
        }

        private struct LightingProfile
        {
            public Color KeyColor;
            public float KeyIntensity;
            public Color FillColor;
            public float FillIntensity;
            public Color RimColor;
            public float RimIntensity;
            public Color HeadColor;
            public float HeadIntensity;
            public float HeadRange;

            public static LightingProfile Studio => new LightingProfile
            {
                KeyColor = new Color(0.98f, 0.95f, 0.92f),
                KeyIntensity = 0.85f,
                FillColor = new Color(0.78f, 0.84f, 0.95f),
                FillIntensity = 0.35f,
                RimColor = new Color(0.7f, 0.78f, 0.95f),
                RimIntensity = 0.25f,
                HeadColor = new Color(0.95f, 0.96f, 1f),
                HeadIntensity = 0.35f,
                HeadRange = 2f
            };

            public static LightingProfile Realistic => new LightingProfile
            {
                KeyColor = new Color(0.85f, 0.80f, 0.75f),
                KeyIntensity = 0.35f,
                FillColor = new Color(0.5f, 0.56f, 0.62f),
                FillIntensity = 0.1f,
                RimColor = new Color(0.45f, 0.52f, 0.68f),
                RimIntensity = 0.06f,
                HeadColor = new Color(0.75f, 0.78f, 0.86f),
                HeadIntensity = 0.1f,
                HeadRange = 1.2f
            };
        }

        private static void ApplyFxColor(FxHandle handle, Color color, Color lightColor, float lightIntensity)
        {
            if (handle == null) return;
            if (handle.Renderer != null)
            {
                var block = new MaterialPropertyBlock();
                var mat = handle.Renderer.sharedMaterial;
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
                block.SetColor("_EmissionColor", color * 1.2f);
                block.SetColor("_EmissiveColor", color * 1.2f);
                if (mat != null)
                {
                    if (mat.HasProperty("_TintColor")) block.SetColor("_TintColor", color);
                    if (mat.HasProperty("_MainColor")) block.SetColor("_MainColor", color);
                }
                EnableEmission(handle.Renderer);
                handle.Renderer.SetPropertyBlock(block);
            }
            if (handle.Light != null)
            {
                handle.Light.color = lightColor;
                handle.Light.intensity = lightIntensity;
                handle.Light.enabled = lightIntensity > 0.01f;
            }
        }

        private static void EnableEmission(Renderer renderer)
        {
            if (renderer?.sharedMaterial == null) return;
            var material = renderer.sharedMaterial;
            if (material.HasProperty("_EmissionColor") || material.HasProperty("_EmissiveColor"))
            {
                material.EnableKeyword("_EMISSION");
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
            public SmokeEmitter SmokeEmitter;
            public FxHandle HeatFx;
            public FxHandle SparkFx;
            public FxHandle ErrorFx;
            public FxHandle LedGlowFx;
            public Transform SparkBurstRoot;
            public List<SparkBurstParticle> SparkBurst;
            public float LastSparkBurstTime;
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
            public ComponentCatalog.Item CatalogItem;
            public Dictionary<string, PartSnapshot> PartBases;
            public string ActiveStateId;
            public List<GameObject> PinGizmos;
        }

        private sealed class PartSnapshot
        {
            public Transform Transform;
            public Renderer Renderer;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public Color BaseColor;
            public Texture BaseTexture;
        }

        private sealed class FxHandle
        {
            public GameObject Root;
            public Renderer Renderer;
            public Light Light;
            public Vector3 BaseScale;
            public Color BaseColor;
        }

        private sealed class SmokeEmitter
        {
            public Transform Root;
            public Material Material;
            public Vector3 LocalUp;
            public float SpawnAccumulator;
            public List<SmokePuff> Puffs;
        }

        private sealed class SmokePuff
        {
            public Transform Transform;
            public Renderer Renderer;
            public Vector3 LocalPos;
            public Vector3 Velocity;
            public float Age;
            public float Lifetime;
            public float StartScale;
            public float EndScale;
            public Color StartColor;
            public Color EndColor;
            public float Rotation;
            public float AngularVelocity;
        }

        private sealed class SparkBurstParticle
        {
            public Transform Transform;
            public Renderer Renderer;
            public Light Light;
            public Vector3 LocalPos;
            public Vector3 Velocity;
            public float Age;
            public float Lifetime;
            public float StartScale;
            public float EndScale;
            public Color StartColor;
            public Color EndColor;
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
