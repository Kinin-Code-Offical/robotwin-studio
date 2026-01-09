using System.Collections.Generic;
using UnityEngine;

namespace RobotTwin.UI
{
    public class WireRope : MonoBehaviour
    {
        private static readonly HashSet<WireRope> ActiveRopes = new HashSet<WireRope>();
        private static readonly Collider[] CollisionBuffer = new Collider[32];
        private const float SpatialGridSize = 0.5f;
        private static readonly Dictionary<Vector3Int, List<WireRope>> SpatialGrid = new Dictionary<Vector3Int, List<WireRope>>();

        [SerializeField] private WireAnchor _start;
        [SerializeField] private WireAnchor _end;
        [SerializeField] private int _segments = 28;
        [SerializeField] private float _sagStrength = 0.45f;
        [SerializeField] private float _liftStrength = 0f;
        [SerializeField] private float _tension = 0.7f;
        [SerializeField] private bool _useRopePhysics = true;
        [SerializeField] private float _gravityScale = 1f;
        [SerializeField] private float _damping = 0.09f;
        [SerializeField] private int _constraintIterations = 8;
        [SerializeField] private int _collisionIterations = 2;
        [SerializeField] private bool _avoidOtherWires = true;
        [SerializeField] private float _wireRepulsion = 0.7f;
        [SerializeField] private bool _smoothLine = true;
        [SerializeField] private int _smoothIterations = 2;
        [SerializeField] private float _bendSmoothing = 0.18f;
        [Header("End Caps")]
        [SerializeField] private bool _showEndCaps = true;
        [SerializeField] private float _copperLength = 0.03f;
        [SerializeField] private float _copperRadius = 0.0016f;
        [SerializeField] private float _solderRadius = 0.0024f;
        [SerializeField] private float _endOffset = 0.001f;
        [SerializeField] private int _solderBlobCount = 2;
        [SerializeField] private float _minSag = 0.04f;
        [SerializeField] private float _maxSag = 6.75f;
        [SerializeField] private float _widthScale = 10f;
        [SerializeField] private float _minWidth = 0.004f;
        [SerializeField] private Color _color = new Color(0.2f, 0.9f, 0.6f);
        [SerializeField] private Color _errorColor = new Color(1f, 0.25f, 0.2f);
        [SerializeField] private float _errorIntensity = 1f;
        [SerializeField] private float _collisionRadiusScale = 0.6f;
        [SerializeField] private float _collisionPadding = 0.002f;

        private LineRenderer _line;
        private static Material _wireMaterial;
        private bool _errorActive;
        private float _errorSeed;
        private SphereCollider _probeCollider;
        private Transform _probeTransform;
        private Vector3[] _points;
        private Vector3[] _prevPoints;
        private Vector3 _lastStart;
        private Vector3 _lastEnd;
        private bool _hasSim;
        private float _lastCollisionRadius;
        private readonly List<Vector3> _smoothBufferA = new List<Vector3>();
        private readonly List<Vector3> _smoothBufferB = new List<Vector3>();
        private Vector3[] _smoothPositions;
        private EndCap _endCapA;
        private EndCap _endCapB;
        private int _visualSeed;
        private static Material _copperMaterial;
        private static Material _solderMaterial;

        public void Initialize(WireAnchor start, WireAnchor end)
        {
            _start = start;
            _end = end;
            EnsureRenderer();
            EnsureEndCaps();
            ResetSimulation();
            UpdateLine();
        }

        private void OnEnable()
        {
            ActiveRopes.Add(this);
            UpdateSpatialGrid();
        }

        private void OnDisable()
        {
            ActiveRopes.Remove(this);
            RemoveFromSpatialGrid();
        }

        public void SetColor(Color color)
        {
            color.a = 1f;
            _color = color;
            UpdateColor();
        }

        public void SetError(bool active)
        {
            _errorActive = active;
            UpdateColor();
        }

        private void Awake()
        {
            _errorSeed = Random.Range(0f, 10f);
            EnsureRenderer();
            EnsureEndCaps();
        }

        private void LateUpdate()
        {
            if (_showEndCaps)
            {
                EnsureEndCaps();
            }
            else
            {
                SetEndCapActive(_endCapA, false);
                SetEndCapActive(_endCapB, false);
            }

            // Visual updates only in LateUpdate
            if (!_useRopePhysics)
            {
                UpdateLine();
            }
            UpdateColor();
        }

        private void FixedUpdate()
        {
            // Physics simulation runs in FixedUpdate for determinism
            if (_useRopePhysics)
            {
                SimulateRope();
            }
        }

        private void EnsureRenderer()
        {
            if (_line != null) return;
            _line = GetComponent<LineRenderer>();
            if (_line == null) _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = false;
            _line.material = GetWireMaterial();
            _line.startColor = _color;
            _line.endColor = _color;
            _line.numCapVertices = 8;
            _line.numCornerVertices = 8;
            _line.alignment = LineAlignment.View;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.sortingOrder = 5;
        }

        private void ResetSimulation()
        {
            _hasSim = false;
            _points = null;
            _prevPoints = null;
            _lastCollisionRadius = 0f;
        }

        private void SimulateRope()
        {
            if (_line == null || _start == null || _end == null) return;
            int count = Mathf.Max(2, _segments);
            var startPos = GetAnchorPosition(_start);
            var endPos = GetAnchorPosition(_end);
            EnsureSimulationPoints(count, startPos, endPos);

            float length = Vector3.Distance(startPos, endPos);
            float slack = Mathf.Max(0f, _sagStrength) * 0.15f;
            slack /= (1f + Mathf.Max(0f, _tension));
            float extraLength = 0f;
            if (slack > 0.0001f)
            {
                extraLength = Mathf.Clamp(length * slack, _minSag, _maxSag);
            }
            float totalLength = Mathf.Max(0.0001f, length + extraLength);
            float segmentLength = totalLength / (count - 1);

            float dt = Time.fixedDeltaTime; // Use fixed timestep for determinism
            Vector3 gravity = Physics.gravity;
            var parent = transform.parent;
            if (parent != null)
            {
                gravity = parent.InverseTransformDirection(gravity);
            }
            float gravityMag = gravity.magnitude;
            Vector3 gravityDir = gravityMag > 0.0001f ? gravity / gravityMag : Vector3.down;
            Vector3 accel = gravity * _gravityScale;
            if (_liftStrength > 0f)
            {
                accel += -gravityDir * (_liftStrength * gravityMag);
            }
            float damping = Mathf.Clamp01(_damping);

            for (int i = 1; i < count - 1; i++)
            {
                Vector3 current = _points[i];
                Vector3 velocity = (current - _prevPoints[i]) * (1f - damping);
                _prevPoints[i] = current;
                _points[i] = current + velocity + accel * (dt * dt);
            }

            int iterations = Mathf.Max(1, _constraintIterations);
            for (int iter = 0; iter < iterations; iter++)
            {
                _points[0] = startPos;
                _points[count - 1] = endPos;
                for (int i = 0; i < count - 1; i++)
                {
                    Vector3 delta = _points[i + 1] - _points[i];
                    float dist = delta.magnitude;
                    if (dist < 0.000001f) continue;
                    float diff = (dist - segmentLength) / dist;
                    Vector3 move = delta * (0.5f * diff);
                    if (i != 0) _points[i] += move;
                    if (i + 1 != count - 1) _points[i + 1] -= move;
                }
            }

            float anchorRadius = Mathf.Max(_start.Radius, _end.Radius);
            float width = Mathf.Max(_minWidth, anchorRadius * _widthScale, anchorRadius * 1.2f);
            _line.startWidth = width;
            _line.endWidth = width;

            _lastCollisionRadius = width * _collisionRadiusScale;
            if (_lastCollisionRadius > 0.0001f)
            {
                int collisionPasses = Mathf.Max(1, _collisionIterations);
                for (int pass = 0; pass < collisionPasses; pass++)
                {
                    ResolveCollisions(_points, _lastCollisionRadius);
                    if (_avoidOtherWires)
                    {
                        ResolveWireCollisions(_points, _lastCollisionRadius);
                    }
                }
            }

            SmoothBends(_points, _bendSmoothing);
            if (_line.positionCount != count) _line.positionCount = count;
            ApplyLinePositions(_points);
            UpdateEndCaps(_points);
            UpdateSpatialGrid();
        }

        private void EnsureSimulationPoints(int count, Vector3 startPos, Vector3 endPos)
        {
            if (_points == null || _points.Length != count)
            {
                _points = new Vector3[count];
                _prevPoints = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)(count - 1);
                    Vector3 pos = Vector3.Lerp(startPos, endPos, t);
                    _points[i] = pos;
                    _prevPoints[i] = pos;
                }
                _lastStart = startPos;
                _lastEnd = endPos;
                _hasSim = true;
                return;
            }

            if (!_hasSim)
            {
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)(count - 1);
                    Vector3 pos = Vector3.Lerp(startPos, endPos, t);
                    _points[i] = pos;
                    _prevPoints[i] = pos;
                }
                _lastStart = startPos;
                _lastEnd = endPos;
                _hasSim = true;
                return;
            }

            Vector3 deltaStart = startPos - _lastStart;
            Vector3 deltaEnd = endPos - _lastEnd;
            if (deltaStart.sqrMagnitude > 0.0000001f || deltaEnd.sqrMagnitude > 0.0000001f)
            {
                Vector3 shift = (deltaStart + deltaEnd) * 0.5f;
                for (int i = 1; i < count - 1; i++)
                {
                    _points[i] += shift;
                    _prevPoints[i] += shift;
                }
                _points[0] = startPos;
                _points[count - 1] = endPos;
                _prevPoints[0] = startPos;
                _prevPoints[count - 1] = endPos;
                _lastStart = startPos;
                _lastEnd = endPos;
            }
        }

        private void UpdateLine()
        {
            if (_line == null || _start == null || _end == null) return;
            int count = Mathf.Max(2, _segments);
            if (_line.positionCount != count) _line.positionCount = count;

            var startPos = GetAnchorPosition(_start);
            var endPos = GetAnchorPosition(_end);
            var gravity = Physics.gravity;
            var gravityDir = gravity.sqrMagnitude > 0.0001f ? gravity.normalized : Vector3.down;
            var parent = transform.parent;
            if (parent != null)
            {
                gravityDir = parent.InverseTransformDirection(gravityDir).normalized;
            }
            var upDir = -gravityDir;

            float length = Vector3.Distance(startPos, endPos);
            float gravityScale = gravity.sqrMagnitude > 0.0001f ? gravity.magnitude / 9.81f : 0f;
            float sag = length * Mathf.Max(0f, _sagStrength) * gravityScale;
            sag = Mathf.Clamp(sag, _minSag, _maxSag);
            sag /= (1f + Mathf.Max(0f, _tension));
            float lift = length * Mathf.Max(0f, _liftStrength);

            float anchorRadius = Mathf.Max(_start.Radius, _end.Radius);
            float width = Mathf.Max(_minWidth, anchorRadius * _widthScale, anchorRadius * 1.2f);
            _line.startWidth = width;
            _line.endWidth = width;

            var control = (startPos + endPos) * 0.5f + gravityDir * sag - upDir * lift;
            var positions = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                positions[i] = QuadraticBezier(startPos, control, endPos, t);
            }

            ResolveCollisions(positions, width * _collisionRadiusScale);
            ApplyLinePositions(positions);
            UpdateEndCaps(positions);
        }

        private void UpdateColor()
        {
            if (_line == null) return;
            if (!_errorActive)
            {
                _color.a = 1f;
                _line.startColor = _color;
                _line.endColor = _color;
                return;
            }

            float pulse = 0.4f + 0.6f * Mathf.Sin(Time.time * 6f + _errorSeed);
            float strength = Mathf.Clamp01(_errorIntensity * pulse);
            var tinted = Color.Lerp(_color, _errorColor, strength);
            tinted.a = 1f;
            _line.startColor = tinted;
            _line.endColor = tinted;
        }

        private static Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        private static Vector3 GetAnchorPosition(WireAnchor anchor)
        {
            if (anchor == null) return Vector3.zero;
            return anchor.GetAttachPosition();
        }

        private void ApplyLinePositions(Vector3[] positions)
        {
            if (_line == null || positions == null || positions.Length == 0) return;
            if (!_smoothLine || _smoothIterations <= 0 || positions.Length < 3)
            {
                if (_line.positionCount != positions.Length) _line.positionCount = positions.Length;
                _line.SetPositions(positions);
                return;
            }

            var smooth = BuildSmoothPositions(positions, Mathf.Clamp(_smoothIterations, 1, 3));
            if (_line.positionCount != smooth.Length) _line.positionCount = smooth.Length;
            _line.SetPositions(smooth);
        }

        private Vector3[] BuildSmoothPositions(Vector3[] input, int iterations)
        {
            if (input == null || input.Length < 3) return input ?? System.Array.Empty<Vector3>();

            var current = _smoothBufferA;
            var next = _smoothBufferB;
            current.Clear();
            current.AddRange(input);

            for (int iter = 0; iter < iterations; iter++)
            {
                if (current.Count < 3) break;
                int targetCount = (current.Count - 1) * 2 + 2;
                if (next.Capacity < targetCount) next.Capacity = targetCount;
                next.Clear();
                next.Add(current[0]);
                for (int i = 0; i < current.Count - 1; i++)
                {
                    Vector3 p0 = current[i];
                    Vector3 p1 = current[i + 1];
                    Vector3 q = Vector3.Lerp(p0, p1, 0.25f);
                    Vector3 r = Vector3.Lerp(p0, p1, 0.75f);
                    next.Add(q);
                    next.Add(r);
                }
                next.Add(current[current.Count - 1]);

                var swap = current;
                current = next;
                next = swap;
            }

            if (_smoothPositions == null || _smoothPositions.Length != current.Count)
            {
                _smoothPositions = new Vector3[current.Count];
            }
            current.CopyTo(_smoothPositions);
            return _smoothPositions;
        }

        private void SmoothBends(Vector3[] positions, float strength)
        {
            if (positions == null || positions.Length < 3) return;
            float t = Mathf.Clamp01(strength);
            if (t <= 0.0001f) return;
            for (int i = 1; i < positions.Length - 1; i++)
            {
                Vector3 target = (positions[i - 1] + positions[i + 1]) * 0.5f;
                Vector3 newPos = Vector3.Lerp(positions[i], target, t);
                Vector3 delta = newPos - positions[i];
                positions[i] = newPos;
                if (_prevPoints != null && i < _prevPoints.Length)
                {
                    _prevPoints[i] += delta;
                }
            }
        }

        private void EnsureEndCaps()
        {
            if (!_showEndCaps)
            {
                SetEndCapActive(_endCapA, false);
                SetEndCapActive(_endCapB, false);
                return;
            }

            if (_endCapA == null)
            {
                _endCapA = CreateEndCap("WireEndA", 0);
            }
            if (_endCapB == null)
            {
                _endCapB = CreateEndCap("WireEndB", 11);
            }
        }

        private void UpdateEndCaps(Vector3[] positions)
        {
            if (!_showEndCaps || positions == null || positions.Length < 2) return;
            if (_endCapA == null || _endCapB == null) EnsureEndCaps();
            if (_endCapA == null || _endCapB == null) return;

            Vector3 start = positions[0];
            Vector3 startDir = positions[1] - positions[0];
            Vector3 end = positions[positions.Length - 1];
            Vector3 endDir = positions[positions.Length - 2] - positions[positions.Length - 1];

            UpdateEndCap(_endCapA, start, startDir);
            UpdateEndCap(_endCapB, end, endDir);
        }

        private void UpdateEndCap(EndCap cap, Vector3 anchorPos, Vector3 dir)
        {
            if (cap?.Root == null) return;
            SetEndCapActive(cap, true);
            if (dir.sqrMagnitude < 0.000001f) dir = Vector3.up;
            var rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            cap.Root.localPosition = anchorPos;
            cap.Root.localRotation = rotation;

            float width = _line != null ? Mathf.Max(_line.startWidth, _line.endWidth) : _minWidth;
            float thicknessRatio = width / Mathf.Max(0.0001f, _minWidth);
            float radiusScale = Mathf.Clamp(thicknessRatio, 0.9f, 6.0f);
            float lengthScale = Mathf.Clamp(thicknessRatio, 0.9f, 4.5f);

            float baseLength = Mathf.Max(0.0001f, cap.CopperLength);
            float baseRadius = Mathf.Max(0.0001f, cap.CopperRadius);

            // Longer "stripped" copper look.
            float length = baseLength * lengthScale * 1.6f;
            float radius = baseRadius * radiusScale;

            // Ensure the visual tip doesn't become tiny compared to the rendered wire width.
            float minTipRadius = width * 0.45f;
            float minTipLength = width * 1.25f;
            radius = Mathf.Max(radius, minTipRadius);
            length = Mathf.Max(length, minTipLength);

            float effectiveRadiusScale = radius / baseRadius;
            float effectiveLengthScale = length / baseLength;
            cap.Copper.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            cap.Copper.localPosition = Vector3.up * (length * 0.5f + _endOffset * effectiveLengthScale);

            if (cap.Solder != null && cap.SolderBasePositions != null && cap.SolderBaseScales != null)
            {
                int count = Mathf.Min(cap.Solder.Length, Mathf.Min(cap.SolderBasePositions.Length, cap.SolderBaseScales.Length));
                for (int i = 0; i < count; i++)
                {
                    var blob = cap.Solder[i];
                    if (blob == null) continue;
                    var basePos = cap.SolderBasePositions[i];
                    var baseScale = cap.SolderBaseScales[i];
                    // Scale X/Z with radius, Y with length for a more organic joint.
                    blob.localScale = new Vector3(baseScale.x * effectiveRadiusScale, baseScale.y * effectiveLengthScale, baseScale.z * effectiveRadiusScale);
                    blob.localPosition = new Vector3(basePos.x * effectiveRadiusScale, basePos.y * effectiveLengthScale, basePos.z * effectiveRadiusScale);
                }
            }
        }

        private EndCap CreateEndCap(string name, int seedOffset)
        {
            var root = new GameObject(name);
            root.transform.SetParent(transform, false);

            var copper = CreatePrimitive("CopperTip", PrimitiveType.Cylinder, root.transform);
            var copperRenderer = copper.GetComponent<Renderer>();
            if (copperRenderer != null) copperRenderer.sharedMaterial = GetCopperMaterial();

            int seed = BuildVisualSeed(seedOffset);
            var rng = new System.Random(seed);
            float length = _copperLength * Mathf.Lerp(0.9f, 1.35f, (float)rng.NextDouble());
            float radius = _copperRadius * Mathf.Lerp(0.9f, 1.25f, (float)rng.NextDouble());
            float solderBase = _solderRadius * Mathf.Lerp(0.9f, 1.3f, (float)rng.NextDouble());
            int blobCount = Mathf.Clamp(_solderBlobCount + rng.Next(0, 2), 2, 4);

            var solder = new Transform[blobCount];
            var solderBaseScales = new Vector3[blobCount];
            var solderBasePositions = new Vector3[blobCount];
            // More organic solder joint: one main elongated blob + a few droplets.
            // Uses primitives only, but with non-uniform scaling (ellipsoids) for shape variety.
            for (int i = 0; i < blobCount; i++)
            {
                var blob = CreatePrimitive($"Solder_{i}", PrimitiveType.Sphere, root.transform);
                var renderer = blob.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = GetSolderMaterial();

                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;

                if (i == 0)
                {
                    float r = solderBase * Mathf.Lerp(1.25f, 1.75f, (float)rng.NextDouble());
                    float y = length * Mathf.Lerp(0.55f, 0.8f, (float)rng.NextDouble());
                    float radial = radius * Mathf.Lerp(0.05f, 0.18f, (float)rng.NextDouble());
                    blob.transform.localScale = new Vector3(r * 2.2f, r * 3.3f, r * 2.2f);
                    blob.transform.localPosition = new Vector3(Mathf.Cos(angle) * radial, y, Mathf.Sin(angle) * radial);
                }
                else
                {
                    float r = solderBase * Mathf.Lerp(0.75f, 1.35f, (float)rng.NextDouble());
                    float y = length * Mathf.Lerp(0.18f, 0.6f, (float)rng.NextDouble());
                    float radial = Mathf.Max(radius * 0.35f, solderBase * Mathf.Lerp(0.25f, 0.9f, (float)rng.NextDouble()));
                    float squash = Mathf.Lerp(0.75f, 1.25f, (float)rng.NextDouble());
                    blob.transform.localScale = new Vector3(r * 2f, r * 2f * squash, r * 2f);
                    blob.transform.localPosition = new Vector3(Mathf.Cos(angle) * radial, y, Mathf.Sin(angle) * radial);
                }

                solder[i] = blob.transform;
                solderBaseScales[i] = blob.transform.localScale;
                solderBasePositions[i] = blob.transform.localPosition;
            }

            return new EndCap
            {
                Root = root.transform,
                Copper = copper.transform,
                Solder = solder,
                SolderBaseScales = solderBaseScales,
                SolderBasePositions = solderBasePositions,
                CopperLength = length,
                CopperRadius = radius
            };
        }

        private static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                if (Application.isPlaying)
                {
                    Destroy(collider);
                }
                else
                {
                    DestroyImmediate(collider);
                }
            }
            return go;
        }

        private static void SetEndCapActive(EndCap cap, bool active)
        {
            if (cap?.Root == null) return;
            if (cap.Root.gameObject.activeSelf != active)
            {
                cap.Root.gameObject.SetActive(active);
            }
        }

        private int BuildVisualSeed(int offset)
        {
            if (_visualSeed != 0) return _visualSeed + offset;
            string seed = $"{_start?.NodeId ?? string.Empty}|{_end?.NodeId ?? string.Empty}";
            int hash = 17;
            for (int i = 0; i < seed.Length; i++)
            {
                hash = hash * 31 + seed[i];
            }
            if (hash == 0) hash = GetInstanceID();
            _visualSeed = hash;
            return _visualSeed + offset;
        }

        private static Material GetCopperMaterial()
        {
            if (_copperMaterial != null) return _copperMaterial;
            _copperMaterial = CreateMetalMaterial("WireCopper", new Color(0.82f, 0.46f, 0.22f), 0.85f, 0.65f);
            return _copperMaterial;
        }

        private static Material GetSolderMaterial()
        {
            if (_solderMaterial != null) return _solderMaterial;
            _solderMaterial = CreateMetalMaterial("WireSolder", new Color(0.78f, 0.78f, 0.82f), 0.9f, 0.35f);
            return _solderMaterial;
        }

        private static Material CreateMetalMaterial(string name, Color color, float metallic, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            var material = new Material(shader)
            {
                name = name
            };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private sealed class EndCap
        {
            public Transform Root;
            public Transform Copper;
            public Transform[] Solder;
            public Vector3[] SolderBaseScales;
            public Vector3[] SolderBasePositions;
            public float CopperLength;
            public float CopperRadius;
        }

        private void ResolveWireCollisions(Vector3[] positions, float radius)
        {
            if (positions == null || positions.Length < 3) return;
            if (radius <= 0.0001f) return;
            float repulsion = Mathf.Clamp01(_wireRepulsion);
            if (repulsion <= 0.001f) return;

            // Use spatial grid to query only nearby wires
            Vector3 centerPos = positions[positions.Length / 2];
            var cellKey = WorldToGridCell(centerPos);
            var nearbyCells = GetNearbyCells(cellKey);

            foreach (var cell in nearbyCells)
            {
                if (!SpatialGrid.TryGetValue(cell, out var nearbyWires)) continue;

                foreach (var other in nearbyWires)
                {
                    if (other == null || other == this || !other._hasSim) continue;
                    var otherPoints = other._points;
                    if (otherPoints == null || otherPoints.Length < 3) continue;

                    float otherRadius = other._lastCollisionRadius > 0f ? other._lastCollisionRadius : radius;
                    float minDist = radius + otherRadius;
                    float minDistSqr = minDist * minDist;

                    for (int i = 1; i < positions.Length - 1; i++)
                    {
                        Vector3 p = positions[i];
                        for (int j = 1; j < otherPoints.Length - 1; j++)
                        {
                            Vector3 delta = p - otherPoints[j];
                            float distSqr = delta.sqrMagnitude;
                            if (distSqr < 0.0000001f || distSqr > minDistSqr) continue;
                            float dist = Mathf.Sqrt(distSqr);
                            Vector3 push = delta / dist * (minDist - dist);
                            positions[i] += push * 0.5f * repulsion;
                            otherPoints[j] -= push * 0.5f * repulsion;
                            if (other._prevPoints != null && j < other._prevPoints.Length)
                            {
                                other._prevPoints[j] = otherPoints[j];
                            }
                        }
                    }
                }
            }
        }

        private void ResolveCollisions(Vector3[] positions, float radius)
        {
            if (positions == null || positions.Length < 3) return;
            if (radius <= 0.0001f) return;
            EnsureProbeCollider();
            if (_probeCollider == null || _probeTransform == null) return;

            _probeCollider.radius = Mathf.Max(0.0001f, radius);
            var probeScale = _probeTransform.lossyScale;
            float probeScaleMax = Mathf.Max(Mathf.Abs(probeScale.x), Mathf.Max(Mathf.Abs(probeScale.y), Mathf.Abs(probeScale.z)));
            if (probeScaleMax <= 0.0001f) probeScaleMax = 1f;
            float worldRadius = _probeCollider.radius * probeScaleMax;
            var parent = transform.parent;
            for (int i = 1; i < positions.Length - 1; i++)
            {
                Vector3 worldPos = parent != null ? parent.TransformPoint(positions[i]) : positions[i];
                _probeTransform.position = worldPos;
                int hitCount = Physics.OverlapSphereNonAlloc(worldPos, worldRadius, CollisionBuffer, ~0, QueryTriggerInteraction.Ignore);
                if (hitCount == 0) continue;
                var hits = CollisionBuffer;
                for (int h = 0; h < hitCount; h++)
                {
                    var hit = hits[h];
                    if (hit == null) continue;
                    if (hit.transform.IsChildOf(transform)) continue;
                    if (Physics.ComputePenetration(
                            _probeCollider, _probeTransform.position, _probeTransform.rotation,
                            hit, hit.transform.position, hit.transform.rotation,
                            out var direction, out var distance))
                    {
                        worldPos += direction * (distance + _collisionPadding);
                        _probeTransform.position = worldPos;
                    }
                }
                positions[i] = parent != null ? parent.InverseTransformPoint(worldPos) : worldPos;
            }
        }

        private void EnsureProbeCollider()
        {
            if (_probeCollider != null) return;
            var probe = new GameObject("WireProbe");
            probe.hideFlags = HideFlags.HideAndDontSave;
            probe.transform.SetParent(transform, false);
            _probeTransform = probe.transform;
            _probeCollider = probe.AddComponent<SphereCollider>();
            _probeCollider.isTrigger = true;
        }

        private static Material GetWireMaterial()
        {
            if (_wireMaterial != null) return _wireMaterial;
            // IMPORTANT: The wire color is driven via LineRenderer vertex colors.
            // Many lit shaders ignore vertex colors, which makes all wires look the same.
            // Prefer a shader known to respect vertex colors.
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Standard");
            if (shader == null) return null;
            _wireMaterial = new Material(shader) { name = "Circuit3D_WirePlastic" };

            // Keep material tint neutral so vertex colors show as-is.
            if (_wireMaterial.HasProperty("_BaseColor")) _wireMaterial.SetColor("_BaseColor", Color.white);
            if (_wireMaterial.HasProperty("_Color")) _wireMaterial.SetColor("_Color", Color.white);

            if (_wireMaterial.HasProperty("_Metallic")) _wireMaterial.SetFloat("_Metallic", 0f);
            if (_wireMaterial.HasProperty("_Glossiness")) _wireMaterial.SetFloat("_Glossiness", 0.25f);
            if (_wireMaterial.HasProperty("_Smoothness")) _wireMaterial.SetFloat("_Smoothness", 0.25f);

            // Try to force opaque surface if the shader supports it.
            if (_wireMaterial.HasProperty("_Surface")) _wireMaterial.SetFloat("_Surface", 0f);
            if (_wireMaterial.HasProperty("_Mode")) _wireMaterial.SetFloat("_Mode", 0f);
            return _wireMaterial;
        }

        private Vector3Int WorldToGridCell(Vector3 worldPos)
        {
            var parent = transform.parent;
            Vector3 localPos = parent != null ? parent.InverseTransformPoint(worldPos) : worldPos;
            return new Vector3Int(
                Mathf.FloorToInt(localPos.x / SpatialGridSize),
                Mathf.FloorToInt(localPos.y / SpatialGridSize),
                Mathf.FloorToInt(localPos.z / SpatialGridSize)
            );
        }

        private List<Vector3Int> GetNearbyCells(Vector3Int center)
        {
            var cells = new List<Vector3Int>(27);
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        cells.Add(new Vector3Int(center.x + x, center.y + y, center.z + z));
                    }
                }
            }
            return cells;
        }

        private void UpdateSpatialGrid()
        {
            if (_points == null || _points.Length == 0) return;
            RemoveFromSpatialGrid();
            Vector3 centerPos = _points[_points.Length / 2];
            var cellKey = WorldToGridCell(centerPos);
            if (!SpatialGrid.TryGetValue(cellKey, out var list))
            {
                list = new List<WireRope>();
                SpatialGrid[cellKey] = list;
            }
            if (!list.Contains(this))
            {
                list.Add(this);
            }
        }

        private void RemoveFromSpatialGrid()
        {
            foreach (var kvp in SpatialGrid)
            {
                kvp.Value.Remove(this);
            }
        }
    }
}
