using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using RobotTwin.CoreSim.Specs;

namespace RobotTwin.UI
{
    public class Breadboard3DView : MonoBehaviour
    {
        private const float DefaultScale = 0.01f;
        private const float BoardThickness = 0.02f;
        private const float WireHeight = 0.02f;
        private const float ComponentHeight = 0.03f;
        private const float PaddingMm = 60f;
        private const string PrefabRoot = "Prefabs/Circuit3D";
        private const string TextureRoot = "Prefabs/Circuit3D/Textures";

        private Camera _camera;
        private RenderTexture _renderTexture;
        private Transform _root;
        private bool _prefabsLoaded;
        private static readonly Dictionary<string, Material> TextureMaterials =
            new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        [Header("Prefabs (optional)")]
        [SerializeField] private GameObject _breadboardPrefab;
        [SerializeField] private GameObject _holePrefab;
        [SerializeField] private GameObject _arduinoPrefab;
        [SerializeField] private GameObject _arduinoUnoPrefab;
        [SerializeField] private GameObject _arduinoNanoPrefab;
        [SerializeField] private GameObject _arduinoProMiniPrefab;
        [SerializeField] private GameObject _resistorPrefab;
        [SerializeField] private GameObject _ledPrefab;
        [SerializeField] private GameObject _batteryPrefab;
        [SerializeField] private GameObject _buttonPrefab;
        [SerializeField] private GameObject _switchPrefab;
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
            ClearRoot();

            ComputeBounds(circuit);
            BuildBoard();
            BuildHoles();

            var positions = BuildComponents(circuit);
            BuildWires(circuit, positions);

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
            var go = new GameObject("Breadboard3D_Camera");
            go.transform.SetParent(transform, false);
            _camera = go.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
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
                name = "Breadboard3D_RT",
                antiAliasing = 2
            };
            _renderTexture.Create();
            if (_camera != null) _camera.targetTexture = _renderTexture;
        }

        private void EnsureRoot()
        {
            if (_root != null) return;
            var go = new GameObject("Breadboard3D_Root");
            go.transform.SetParent(transform, false);
            _root = go.transform;
        }

        private void ClearRoot()
        {
            if (_root == null) return;
            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                Destroy(_root.GetChild(i).gameObject);
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

        private void BuildBoard()
        {
            var board = CreateInstance(_breadboardPrefab, PrimitiveType.Cube, "Breadboard");
            board.transform.localScale = new Vector3(_size.x * _scale, BoardThickness, _size.y * _scale);
            board.transform.localPosition = Vector3.zero;

            if (_breadboardPrefab == null)
            {
                var renderer = board.GetComponent<Renderer>();
                renderer.material.color = new Color(0.85f, 0.85f, 0.88f);
            }

            ApplyTexture(board, $"{TextureRoot}/Breadboard");
        }

        private void BuildHoles()
        {
            int cols = Mathf.Clamp(Mathf.RoundToInt(_size.x / 40f), 8, 24);
            int rows = Mathf.Clamp(Mathf.RoundToInt(_size.y / 40f), 4, 12);
            float spacingX = (_size.x * _scale) / (cols + 1);
            float spacingZ = (_size.y * _scale) / (rows + 1);
            float startX = -(_size.x * _scale) * 0.5f + spacingX;
            float startZ = -(_size.y * _scale) * 0.5f + spacingZ;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var hole = CreateInstance(_holePrefab, PrimitiveType.Cylinder, "Hole");
                    hole.transform.localScale = new Vector3(0.01f, 0.001f, 0.01f);
                    hole.transform.localPosition = new Vector3(startX + spacingX * c, BoardThickness * 0.5f + 0.001f, startZ + spacingZ * r);
                    if (_holePrefab == null)
                    {
                        var renderer = hole.GetComponent<Renderer>();
                        renderer.material.color = new Color(0.15f, 0.15f, 0.18f);
                    }
                }
            }
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
                part.transform.localScale = GetPartScale(comp.Type);
                if (prefab == null)
                {
                    var renderer = part.GetComponent<Renderer>();
                    renderer.material.color = GetPartColor(comp.Type);
                }

                ApplyTexture(part, GetTextureForType(comp.Type));

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

        private void BuildWires(CircuitSpec circuit, Dictionary<string, Vector3> positions)
        {
            if (circuit == null || circuit.Nets == null) return;
            foreach (var net in circuit.Nets)
            {
                if (net?.Nodes == null || net.Nodes.Count < 2) continue;
                var compIds = net.Nodes.Select(GetComponentId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
                if (compIds.Count < 2) continue;

                for (int i = 0; i < compIds.Count - 1; i++)
                {
                    if (!positions.TryGetValue(compIds[i], out var a)) continue;
                    if (!positions.TryGetValue(compIds[i + 1], out var b)) continue;
                    CreateWire(a, b);
                }
            }
        }

        private void CreateWire(Vector3 start, Vector3 end)
        {
            var go = new GameObject("Wire");
            go.transform.SetParent(_root, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = false;
            lr.startWidth = 0.004f;
            lr.endWidth = 0.004f;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = new Color(0.2f, 0.9f, 0.6f);
            lr.endColor = lr.startColor;
            lr.SetPosition(0, new Vector3(start.x, WireHeight, start.z));
            lr.SetPosition(1, new Vector3(end.x, WireHeight, end.z));
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

            _breadboardPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/Breadboard");
            _holePrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/Hole");
            _arduinoPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/Arduino");
            _arduinoUnoPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/ArduinoUno");
            _arduinoNanoPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/ArduinoNano");
            _arduinoProMiniPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/ArduinoProMini");
            _resistorPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/Resistor");
            _ledPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/LED");
            _batteryPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/Battery");
            _buttonPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/Button");
            _switchPrefab ??= Resources.Load<GameObject>($"{PrefabRoot}/Swirch_ON_OFF");
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
