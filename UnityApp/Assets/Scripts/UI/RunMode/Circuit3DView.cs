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
        private const float PaddingMm = 60f;
        private const float DefaultAnchorRadius = 0.006f;
        private const string PrefabRoot = "Prefabs/Circuit3D";
        private const string TextureRoot = "Prefabs/Circuit3D/Textures";

        private Camera _camera;
        private RenderTexture _renderTexture;
        private Transform _root;
        private bool _prefabsLoaded;
        private Transform _lightRoot;
        private Light _keyLight;
        private Light _fillLight;
        private Light _rimLight;
        private readonly Dictionary<string, AnchorState> _anchorStateCache = new Dictionary<string, AnchorState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ComponentVisual> _componentVisuals = new Dictionary<string, ComponentVisual>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Material> TextureMaterials =
            new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

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

        private float _scale = DefaultScale;
        private Vector2 _min;
        private Vector2 _max;
        private Vector2 _size;
        private Vector2 _viewportSize;
        private Vector2 _orbitAngles = new Vector2(45f, 0f);
        private float _zoom = 0.5f;
        private float _distance = 1.2f;
        private Vector3 _panOffset = Vector3.zero;
        private bool _hasUserCamera;

        public RenderTexture TargetTexture => _renderTexture;

        public void Initialize(int width, int height)
        {
            EnsureCamera();
            EnsureRenderTexture(width, height);
        }

        public void Build(CircuitSpec circuit)
        {
            EnsureRoot();
            EnsurePrefabs();
            EnsureLighting();
            CaptureAnchorState();
            ClearRoot();

            ComputeBounds(circuit);
            var positions = BuildComponents(circuit);
            var anchors = BuildAnchors(circuit, positions);
            BuildWires(circuit, anchors);

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
            _camera.backgroundColor = new Color(0.07f, 0.08f, 0.1f);
            _camera.orthographic = true;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 10f;
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

            _keyLight = CreateDirectionalLight("Circuit3D_KeyLight", new Vector3(50f, -30f, 0f), 1.2f, new Color(1f, 0.96f, 0.9f));
            _fillLight = CreateDirectionalLight("Circuit3D_FillLight", new Vector3(20f, 160f, 0f), 0.6f, new Color(0.85f, 0.9f, 1f));
            _rimLight = CreateDirectionalLight("Circuit3D_RimLight", new Vector3(75f, 40f, 0f), 0.4f, new Color(0.8f, 0.85f, 1f));
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

        private void ClearRoot()
        {
            if (_root == null) return;
            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                Destroy(_root.GetChild(i).gameObject);
            }
            _componentVisuals.Clear();
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
            _min = new Vector2(0, 0);
            _max = new Vector2(600, 400);
            if (circuit == null || circuit.Components == null || circuit.Components.Count == 0)
            {
                _size = _max - _min;
                return;
            }

            bool initialized = false;
            foreach (var comp in circuit.Components)
            {
                if (!TryGetPosition(comp, out var pos)) continue;
                if (!initialized)
                {
                    _min = pos;
                    _max = pos;
                    initialized = true;
                }
                else
                {
                    _min = Vector2.Min(_min, pos);
                    _max = Vector2.Max(_max, pos);
                }
            }

            if (!initialized)
            {
                _min = Vector2.zero;
                _max = new Vector2(600, 400);
            }

            _min -= Vector2.one * PaddingMm;
            _max += Vector2.one * PaddingMm;
            _size = _max - _min;
        }

        private Dictionary<string, Vector3> BuildComponents(CircuitSpec circuit)
        {
            var positions = new Dictionary<string, Vector3>();
            if (circuit == null || circuit.Components == null) return positions;

            foreach (var comp in circuit.Components)
            {
                if (!TryGetPosition(comp, out var pos)) continue;
                Vector3 world = ToWorld(pos, ComponentHeight);
                positions[comp.Id] = world;

                var prefab = GetPrefabForType(comp.Type);
                var part = CreateInstance(prefab, PrimitiveType.Cube, $"Part_{comp.Id}");
                part.transform.localPosition = world;
                part.transform.localScale = prefab == null ? GetPartScale(comp.Type) : Vector3.one;
                if (prefab == null)
                {
                    var renderer = part.GetComponent<Renderer>();
                    renderer.material.color = GetPartColor(comp.Type);
                }

                ApplyTexture(part, GetTextureForType(comp.Type));

                RegisterComponentVisual(comp, part);

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

        private void RegisterComponentVisual(ComponentSpec comp, GameObject part)
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
                IsServo = IsServoType(comp.Type),
                ErrorSeed = UnityEngine.Random.Range(0f, 10f)
            };

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

            _componentVisuals[comp.Id] = visual;
        }

        private void EnsureComponentCollider(GameObject part, Renderer[] renderers)
        {
            if (part == null) return;
            if (part.GetComponent<Collider>() != null) return;
            if (renderers == null || renderers.Length == 0) return;

            bool hasBounds = false;
            var bounds = new Bounds();
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var worldBounds = renderer.bounds;
                var center = part.transform.InverseTransformPoint(worldBounds.center);
                var size = part.transform.InverseTransformVector(worldBounds.size);
                if (!hasBounds)
                {
                    bounds = new Bounds(center, size);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(new Bounds(center, size));
                }
            }

            if (!hasBounds) return;
            var box = part.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
        }

        private void EnsureLedGlow(ComponentVisual visual)
        {
            if (visual == null || visual.GlowLight != null) return;
            var glow = new GameObject("LED_Glow");
            glow.transform.SetParent(visual.Root, false);
            glow.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 0.12f;
            light.intensity = 0f;
            light.color = visual.BaseColor;
            light.shadows = LightShadows.None;
            visual.GlowLight = light;
        }

        private void EnsureUsbIndicator(ComponentVisual visual)
        {
            if (visual == null || visual.UsbRenderer != null) return;
            var usb = GameObject.CreatePrimitive(PrimitiveType.Cube);
            usb.name = "USB_Indicator";
            usb.transform.SetParent(visual.Root, false);
            usb.transform.localScale = new Vector3(0.02f, 0.01f, 0.02f);
            usb.transform.localPosition = new Vector3(0f, 0.015f, -0.04f);
            var collider = usb.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            visual.UsbIndicator = usb.transform;
            visual.UsbRenderer = usb.GetComponent<Renderer>();
            visual.UsbLight = usb.AddComponent<Light>();
            visual.UsbLight.type = LightType.Point;
            visual.UsbLight.range = 0.08f;
            visual.UsbLight.intensity = 0f;
            visual.UsbLight.color = new Color(0.2f, 0.8f, 1f);
            visual.UsbLight.shadows = LightShadows.None;
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
                    if (!positions.TryGetValue(compId, out var position))
                    {
                        position = new Vector3(0f, ComponentHeight, 0f);
                    }

                    var anchor = CreateAnchor(node, position);
                    anchors[node] = anchor;
                }
            }

            return anchors;
        }

        private WireAnchor CreateAnchor(string nodeId, Vector3 position)
        {
            var anchorGo = new GameObject($"Anchor_{nodeId}");
            anchorGo.transform.SetParent(_root, false);
            anchorGo.transform.localPosition = position;
            var anchor = anchorGo.AddComponent<WireAnchor>();

            if (_anchorStateCache.TryGetValue(nodeId, out var state))
            {
                anchor.Initialize(nodeId, state.Radius);
                anchorGo.transform.localPosition = state.LocalPosition;
            }
            else
            {
                anchor.Initialize(nodeId, DefaultAnchorRadius);
            }

            return anchor;
        }

        private void BuildWires(CircuitSpec circuit, Dictionary<string, WireAnchor> anchors)
        {
            if (circuit == null || circuit.Nets == null) return;
            foreach (var net in circuit.Nets)
            {
                if (net?.Nodes == null || net.Nodes.Count < 2) continue;
                var nodes = net.Nodes.Where(node => !string.IsNullOrWhiteSpace(node)).Distinct().ToList();
                if (nodes.Count < 2) continue;

                for (int i = 0; i < nodes.Count - 1; i++)
                {
                    if (!anchors.TryGetValue(nodes[i], out var a)) continue;
                    if (!anchors.TryGetValue(nodes[i + 1], out var b)) continue;
                    CreateWire(a, b);
                }
            }
        }

        private void CreateWire(WireAnchor start, WireAnchor end)
        {
            var go = new GameObject("Wire");
            go.transform.SetParent(_root, false);
            var rope = go.AddComponent<WireRope>();
            rope.Initialize(start, end);
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
            var hits = Physics.RaycastAll(ray, 10f, ~0, QueryTriggerInteraction.Ignore);
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

        private void ApplyComponentState(ComponentVisual visual, ComponentSpec spec, TelemetryFrame telemetry, bool usbConnected, bool hasError)
        {
            if (visual == null) return;
            Color baseColor = visual.BaseColor;
            Color displayColor = baseColor;
            Color emissionColor = Color.black;

            if (visual.IsResistor && telemetry != null &&
                telemetry.Signals.TryGetValue($"COMP:{visual.Id}:T", out var tempRaw))
            {
                float temp = (float)tempRaw;
                float heat = Mathf.InverseLerp(60f, 140f, temp);
                var hotColor = new Color(1f, 0.35f, 0.1f);
                displayColor = Color.Lerp(displayColor, hotColor, heat);
                emissionColor += hotColor * (heat * 1.5f);
            }

            if (visual.IsLed)
            {
                float intensity = 0f;
                if (telemetry != null && telemetry.Signals.TryGetValue($"COMP:{visual.Id}:L", out var lum))
                {
                    intensity = Mathf.Clamp01((float)lum);
                }
                else if (telemetry != null && telemetry.Signals.TryGetValue($"COMP:{visual.Id}:I", out var current))
                {
                    intensity = Mathf.Clamp01(Mathf.Abs((float)current) * 25f);
                }

                displayColor = Color.Lerp(baseColor * 0.2f, baseColor, 0.3f + intensity * 0.7f);
                emissionColor += baseColor * (0.3f + intensity * 2.5f);
                if (visual.GlowLight != null)
                {
                    visual.GlowLight.color = baseColor;
                    visual.GlowLight.intensity = intensity * 3f;
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
                    float angle = closed ? -5f : 20f;
                    visual.Root.localRotation = visual.BaseRotation * Quaternion.Euler(angle, 0f, 0f);
                }
                else if (visual.IsButton)
                {
                    visual.Root.localPosition = visual.BasePosition + (closed ? Vector3.down * 0.004f : Vector3.zero);
                }
            }

            if (visual.IsServo && TryGetServoAngle(spec, out var angle))
            {
                var arm = visual.ServoArm ?? visual.Root;
                arm.localRotation = visual.ServoArmBaseRotation * Quaternion.Euler(0f, angle, 0f);
            }

            if (hasError)
            {
                float pulse = 0.4f + 0.6f * Mathf.Sin(Time.time * 6f + visual.ErrorSeed);
                var errorColor = new Color(1f, 0.2f, 0.2f);
                displayColor = Color.Lerp(displayColor, errorColor, 0.4f + pulse * 0.3f);
                emissionColor += errorColor * (1f + pulse * 2f);
            }

            ApplyRendererColors(visual, displayColor, emissionColor);
        }

        private void ApplyRendererColors(ComponentVisual visual, Color baseColor, Color emissionColor)
        {
            if (visual?.Renderers == null) return;
            var block = visual.Block ?? new MaterialPropertyBlock();
            foreach (var renderer in visual.Renderers)
            {
                if (renderer == null) continue;
                block.Clear();
                block.SetColor("_Color", baseColor);
                block.SetColor("_BaseColor", baseColor);
                block.SetColor("_EmissionColor", emissionColor);
                renderer.SetPropertyBlock(block);
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

        private void FrameCamera()
        {
            if (_camera == null) return;
            float width = _size.x * _scale;
            float height = _size.y * _scale;
            float size = Mathf.Max(width, height) * 0.6f;
            _zoom = Mathf.Max(0.1f, size);
            _distance = Mathf.Max(width, height) * 1.4f + 0.6f;
            _panOffset = Vector3.zero;
            _orbitAngles = new Vector2(45f, 0f);
            _hasUserCamera = false;
            UpdateCameraTransform();
        }

        public void Pan(Vector2 deltaPixels)
        {
            if (_camera == null) return;
            float pixels = Mathf.Max(1f, _viewportSize.y);
            float unitsPerPixel = (_camera.orthographicSize * 2f) / pixels;
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
            _hasUserCamera = true;
            UpdateCameraTransform();
        }

        public void Orbit(Vector2 deltaPixels)
        {
            if (_camera == null) return;
            _orbitAngles.x = Mathf.Clamp(_orbitAngles.x - deltaPixels.y * 0.2f, 20f, 80f);
            _orbitAngles.y += deltaPixels.x * 0.2f;
            _hasUserCamera = true;
            UpdateCameraTransform();
        }

        public void Zoom(float delta)
        {
            if (_camera == null) return;
            float step = -delta * 0.01f;
            _zoom = Mathf.Clamp(_zoom + step, 0.08f, 10f);
            _hasUserCamera = true;
            UpdateCameraTransform();
        }

        private void UpdateCameraTransform()
        {
            if (_camera == null) return;
            var rotation = Quaternion.Euler(_orbitAngles.x, _orbitAngles.y, 0f);
            var forward = rotation * Vector3.forward;
            _camera.orthographicSize = _zoom;
            _camera.transform.localPosition = _panOffset - forward * _distance;
            _camera.transform.localRotation = rotation;
        }

        private Vector3 ToWorld(Vector2 pos, float y)
        {
            float x = (pos.x - _min.x - _size.x * 0.5f) * _scale;
            float z = (pos.y - _min.y - _size.y * 0.5f) * _scale;
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
                var existing = renderer.sharedMaterial;
                if (HasTexture(existing)) continue;
                renderer.sharedMaterial = material;
            }
        }

        private static bool HasTexture(Material material)
        {
            if (material == null) return false;
            if (material.mainTexture != null) return true;
            if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null) return true;
            return false;
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
            public Light GlowLight;
            public Transform UsbIndicator;
            public Renderer UsbRenderer;
            public Light UsbLight;
            public Transform ServoArm;
            public Quaternion ServoArmBaseRotation;
            public float ErrorSeed;
            public bool IsLed;
            public bool IsResistor;
            public bool IsSwitch;
            public bool IsButton;
            public bool IsArduino;
            public bool IsServo;
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
    }
}
