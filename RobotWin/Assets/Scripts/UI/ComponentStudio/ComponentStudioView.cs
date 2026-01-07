using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RobotTwin.UI
{
    public class ComponentStudioView : MonoBehaviour
    {
        private const int ViewCubeLayer = 30;

        public enum ViewPreset
        {
            Top,
            Bottom,
            Left,
            Right,
            Front,
            Back
        }

        public enum GizmoMode
        {
            None,
            Move,
            Rotate,
            Scale
        }

        public enum GizmoAxis
        {
            X,
            Y,
            Z,
            Uniform
        }

        public enum GizmoHandleKind
        {
            MoveAxis,
            RotateAxis,
            ScaleAxis,
            ScaleUniform
        }

        public sealed class GizmoHandle : MonoBehaviour
        {
            public GizmoHandleKind Kind;
            public GizmoAxis Axis;
            public float Radius;
            public float Thickness;
        }

        private Camera _camera;
        private RenderTexture _renderTexture;
        private Camera _viewCubeCamera;
        private RenderTexture _viewCubeTexture;
        private Transform _viewCubeRoot;
        private GameObject _viewCubeModel;
        private Transform _viewCubeFaceRoot;
        private Light _viewCubeLight;
        private int _viewCubeSize = 128;
        private readonly List<ViewCubeFaceInfo> _viewCubeFaces = new List<ViewCubeFaceInfo>();
        private Transform _root;
        private Transform _modelRoot;
        private Transform _modelContent;

        // Runtime material overrides (e.g., per-part material/texture edits).
        // IMPORTANT: Materials created at runtime must be explicitly destroyed, otherwise they leak.
        private readonly Dictionary<Renderer, Material> _originalSharedMaterials = new Dictionary<Renderer, Material>();
        private readonly Dictionary<Renderer, Material> _overrideSharedMaterials = new Dictionary<Renderer, Material>();
        private GameObject _modelInstance;
        private readonly List<Transform> _parts = new List<Transform>();
        private readonly List<GameObject> _anchorGizmos = new List<GameObject>();
        private readonly List<GameObject> _effectGizmos = new List<GameObject>();
        private Transform _gizmoRoot;
        private readonly List<GameObject> _gizmoObjects = new List<GameObject>();
        private readonly List<LineRenderer> _gizmoLines = new List<LineRenderer>();
        private readonly Dictionary<GizmoAxis, GizmoHandle> _rotateHandles = new Dictionary<GizmoAxis, GizmoHandle>();
        private readonly Dictionary<GizmoAxis, LineRenderer> _rotateArcLines = new Dictionary<GizmoAxis, LineRenderer>();
        private static Mesh _arrowHeadMesh;
        private const int RotateArcSegments = 64;
        private bool _lockRotateRebuild;
        private GizmoMode _gizmoMode = GizmoMode.None;
        private bool _gizmoVisible;
        private Vector3 _gizmoWorldPos;
        private Quaternion _gizmoWorldRot = Quaternion.identity;
        private float _gizmoScale = 1f;
        private Bounds _gizmoBounds;
        private LineRenderer _selectionOutline;
        private Transform _selectionTarget;
        private readonly Dictionary<Renderer, MaterialPropertyBlock> _partFocusOriginalBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();
        private Transform _focusedPart;
        private Vector2 _orbitAngles = new Vector2(28f, 30f);
        private float _distance = 1.4f;
        private Vector3 _panOffset = Vector3.zero;
        private Vector2 _viewportSize;
        private static readonly Color CameraBackgroundColor = new Color(0.34f, 0.36f, 0.46f);
        private Color _background = CameraBackgroundColor;
        private float _fieldOfView = 55f;
        private bool _usePerspective = true;
        private bool _followTarget;
        private Transform _followTransform;
        private Light _keyLight;
        private Light _fillLight;
        private Light _rimLight;
        private Light _headLight;
        private Transform _markerRoot;
        private Transform _referenceRoot;
        private readonly List<LineRenderer> _referenceLines = new List<LineRenderer>();
        [SerializeField] private bool _showReferenceFrame = true;

        private Material _referenceLineMaterial;
        private float _lightingBlend;

        private const int ReferenceGridHalfLines = 8;

        private bool ShouldShowReferenceFrame()
        {
            if (!_showReferenceFrame) return false;
            var scene = SceneManager.GetActiveScene();
            return !string.Equals(scene.name, "WorldScene", StringComparison.OrdinalIgnoreCase);
        }

        public struct AnchorGizmo
        {
            public string Name;
            public Vector3 LocalPosition;
            public float Radius;
            public Color Color;
        }

        private struct ViewCubeFaceInfo
        {
            public Transform Face;
            public Renderer LabelRenderer;
        }

        public RenderTexture TargetTexture => _renderTexture;
        public RenderTexture ViewCubeTexture => _viewCubeTexture;
        public IReadOnlyList<Transform> Parts => _parts;
        public Transform Root => _root;
        public Transform ModelRoot => _modelRoot;
        public Camera ViewCamera => _camera;

        public enum EditMarkerKind
        {
            Anchor,
            Effect
        }

        public sealed class EditMarker : MonoBehaviour
        {
            public EditMarkerKind Kind;
            public string Id;
        }

        public void Initialize(int width, int height)
        {
            EnsureCamera();
            EnsureRenderTexture(width, height);
            EnsureViewCube(_viewCubeSize);
            if (_modelRoot != null)
            {
                FrameModel();
            }
        }

        public void SetBackground(Color color)
        {
            _background = color;
            if (_camera != null) _camera.backgroundColor = color;
        }

        public void SetPerspective(bool enabled)
        {
            _usePerspective = enabled;
            if (_camera != null) _camera.orthographic = !_usePerspective;
            FrameModel();
        }

        public void SetFieldOfView(float value)
        {
            _fieldOfView = Mathf.Clamp(value, 30f, 95f);
            if (_camera != null) _camera.fieldOfView = _fieldOfView;
            if (_usePerspective)
            {
                FrameModel();
            }
            else
            {
                UpdateCameraTransform();
            }
        }

        public void SetFollowTarget(Transform target, bool enabled)
        {
            _followTarget = enabled && target != null;
            _followTransform = _followTarget ? target : null;
            if (_followTransform != null && _root != null)
            {
                _panOffset = _root.InverseTransformPoint(_followTransform.position);
                UpdateCameraTransform();
            }
        }

        public void ApplyModelTuning(Vector3 euler, Vector3 scale)
        {
            ApplyModelTuning(euler, scale, Vector3.zero);
        }

        public void ApplyModelTuning(Vector3 euler, Vector3 scale, Vector3 offset)
        {
            if (_modelRoot == null) return;
            _modelRoot.localRotation = Quaternion.Euler(euler);
            _modelRoot.localScale = scale;
            _modelRoot.localPosition = offset;
            UpdateViewCubeOrientation();
        }

        public void SetModel(GameObject prefab)
        {
            ClearModel();
            if (prefab == null) return;
            if (_root == null) EnsureCamera();
            var pivot = new GameObject("ComponentModel");
            pivot.transform.SetParent(_root, false);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.identity;
            pivot.transform.localScale = Vector3.one;

            var content = Instantiate(prefab);
            content.name = "ComponentContent";
            content.transform.SetParent(pivot.transform, false);

            _modelInstance = pivot;
            _modelRoot = pivot.transform;
            _modelContent = content.transform;

            RecenterModelToBounds();
            CacheParts();
            EnsureModelColliders();
            FrameModel();
        }

        public void SetModelInstance(GameObject instance)
        {
            SetModelInstance(instance, true);
        }

        public void SetModelInstance(GameObject instance, bool recenter)
        {
            ClearModel();
            if (instance == null) return;
            if (_root == null) EnsureCamera();
            var pivot = new GameObject("ComponentModel");
            pivot.transform.SetParent(_root, false);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.identity;
            pivot.transform.localScale = Vector3.one;

            instance.name = "ComponentContent";
            instance.SetActive(true);
            instance.transform.SetParent(pivot.transform, false);

            _modelInstance = pivot;
            _modelRoot = pivot.transform;
            _modelContent = instance.transform;

            if (recenter)
            {
                RecenterModelToBounds();
            }
            CacheParts();
            EnsureModelColliders();
            FrameModel();
        }

        public void SetPlaceholderModel()
        {
            ClearModel();
            if (_root == null) EnsureCamera();
            var pivot = new GameObject("ComponentModel");
            pivot.transform.SetParent(_root, false);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.identity;
            pivot.transform.localScale = Vector3.one;

            var content = GameObject.CreatePrimitive(PrimitiveType.Cube);
            content.name = "ComponentPlaceholder";
            content.transform.SetParent(pivot.transform, false);
            content.transform.localScale = new Vector3(0.2f, 0.05f, 0.12f);

            _modelInstance = pivot;
            _modelRoot = pivot.transform;
            _modelContent = content.transform;
            RecenterModelToBounds();
            CacheParts();
            EnsureModelColliders();
            FrameModel();
        }

        public void ClearModel()
        {
            if (_modelInstance != null)
            {
                Destroy(_modelInstance);
            }
            _modelInstance = null;
            _modelRoot = null;
            _modelContent = null;
            _parts.Clear();
            ClearAnchors();
            ClearEffects();
            ClearSelectionOutline();
            if (_markerRoot != null)
            {
                Destroy(_markerRoot.gameObject);
                _markerRoot = null;
            }
            _followTarget = false;
            _followTransform = null;

            ClearPartFocus();

            // Clean up any runtime-created materials we own.
            if (_overrideSharedMaterials.Count > 0)
            {
                foreach (var kvp in _overrideSharedMaterials)
                {
                    var mat = kvp.Value;
                    if (mat != null)
                    {
                        Destroy(mat);
                    }
                }
                _overrideSharedMaterials.Clear();
            }
            _originalSharedMaterials.Clear();
        }

        private void RecenterModelToBounds()
        {
            if (_modelRoot == null || _modelContent == null) return;
            if (!TryGetBoundsRelativeTo(_modelRoot, _modelRoot, out var bounds)) return;
            if (bounds.center.sqrMagnitude <= 0.000001f) return;
            _modelContent.localPosition -= bounds.center;
        }

        public void FrameModel()
        {
            if (_camera == null) return;
            if (!TryGetModelBounds(out var bounds))
            {
                _distance = 1.2f;
                _panOffset = Vector3.zero;
                UpdateCameraTransform();
                UpdateReferenceFrame(new Bounds(Vector3.zero, Vector3.one * 0.1f));
                return;
            }

            _panOffset = bounds.center;
            _distance = _usePerspective
                ? ComputePerspectiveDistance(bounds, 1.4f)
                : ComputeOrthoSize(bounds, 1.15f);
            UpdateCameraTransform();
            UpdateReferenceFrame(bounds);
        }

        public void FramePart(Transform part)
        {
            if (_camera == null) return;
            if (part == null)
            {
                FrameModel();
                return;
            }
            if (_modelRoot == null)
            {
                FrameModel();
                return;
            }

            // Frame in the same reference-space as FrameModel (model-root local), so camera pan stays consistent.
            if (!TryGetBoundsRelativeTo(_modelRoot, part, out var bounds))
            {
                FrameModel();
                return;
            }

            _panOffset = bounds.center;
            _distance = _usePerspective
                ? ComputePerspectiveDistance(bounds, 1.35f)
                : ComputeOrthoSize(bounds, 1.10f);
            UpdateCameraTransform();
            UpdateReferenceFrame(bounds);
        }

        public void ResetView()
        {
            _orbitAngles = new Vector2(28f, 30f);
            FrameModel();
        }

        public void SnapView(ViewPreset preset)
        {
            if (_modelRoot != null)
            {
                var modelRotation = GetModelRotation();
                var modelUp = modelRotation * Vector3.up;
                var modelRight = modelRotation * Vector3.right;
                var modelForward = modelRotation * Vector3.forward;
                Vector3 desiredForward = modelForward;
                Vector3 desiredUp = modelUp;

                switch (preset)
                {
                    case ViewPreset.Top:
                        desiredForward = -modelUp;
                        desiredUp = modelForward;
                        break;
                    case ViewPreset.Bottom:
                        desiredForward = modelUp;
                        desiredUp = modelForward;
                        break;
                    case ViewPreset.Left:
                        desiredForward = -modelRight;
                        desiredUp = modelUp;
                        break;
                    case ViewPreset.Right:
                        desiredForward = modelRight;
                        desiredUp = modelUp;
                        break;
                    case ViewPreset.Front:
                        desiredForward = modelForward;
                        desiredUp = modelUp;
                        break;
                    case ViewPreset.Back:
                        desiredForward = -modelForward;
                        desiredUp = modelUp;
                        break;
                }

                desiredForward.Normalize();
                desiredUp.Normalize();
                if (Mathf.Abs(Vector3.Dot(desiredForward, desiredUp)) > 0.95f)
                {
                    desiredUp = modelRight;
                }

                var rotation = Quaternion.LookRotation(desiredForward, desiredUp);
                var euler = rotation.eulerAngles;
                _orbitAngles = new Vector2(NormalizeAngle(euler.x), NormalizeAngle(euler.y));
                UpdateCameraTransform();
                return;
            }

            switch (preset)
            {
                case ViewPreset.Top:
                    _orbitAngles = new Vector2(90f, 0f);
                    break;
                case ViewPreset.Bottom:
                    _orbitAngles = new Vector2(-90f, 0f);
                    break;
                case ViewPreset.Left:
                    _orbitAngles = new Vector2(0f, -90f);
                    break;
                case ViewPreset.Right:
                    _orbitAngles = new Vector2(0f, 90f);
                    break;
                case ViewPreset.Front:
                    _orbitAngles = new Vector2(0f, 0f);
                    break;
                case ViewPreset.Back:
                    _orbitAngles = new Vector2(0f, 180f);
                    break;
            }
            UpdateCameraTransform();
        }

        public void Orbit(Vector2 delta)
        {
            _orbitAngles.x -= delta.y;
            _orbitAngles.y += delta.x;
            UpdateCameraTransform();
        }

        public void Pan(Vector2 delta)
        {
            if (_camera == null) return;
            float scale = _distance * 0.0025f;
            var right = _camera.transform.right;
            var up = _camera.transform.up;
            _panOffset -= right * delta.x * scale;
            _panOffset -= up * delta.y * scale;
            UpdateCameraTransform();
        }

        public void NudgePanCamera(Vector2 axes)
        {
            if (_camera == null) return;
            var right = _camera.transform.right;
            if (right.sqrMagnitude < 0.001f) right = Vector3.right;
            right.Normalize();

            var forward = _camera.transform.forward;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            float step = GetKeyboardPanStep();
            _panOffset += (right * axes.x + forward * axes.y) * step;
            UpdateCameraTransform();
        }

        public void NudgePanCameraVertical(float axis)
        {
            if (_camera == null) return;
            var up = _camera.transform.up;
            if (up.sqrMagnitude < 0.001f) up = Vector3.up;
            up.Normalize();

            float step = GetKeyboardPanStep();
            _panOffset += up * (axis * step);
            UpdateCameraTransform();
        }

        public float GetKeyboardPanStep()
        {
            return Mathf.Max(0.02f, _distance * 0.05f);
        }

        public void Zoom(float amount)
        {
            _distance = Mathf.Max(0.001f, _distance * (1f - amount));
            UpdateCameraTransform();
        }

        public void SetViewport(int width, int height)
        {
            EnsureRenderTexture(width, height);
        }

        public void SetAnchorGizmos(IReadOnlyList<AnchorGizmo> anchors)
        {
            ClearAnchors();
            if (_modelRoot == null || anchors == null) return;

            foreach (var anchor in anchors)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"Anchor_{anchor.Name}";
                sphere.transform.SetParent(GetMarkerRoot(), false);
                sphere.transform.localPosition = anchor.LocalPosition;
                float radius = Mathf.Max(anchor.Radius, 0.005f);
                sphere.transform.localScale = Vector3.one * radius * 2f;
                var marker = sphere.AddComponent<EditMarker>();
                marker.Kind = EditMarkerKind.Anchor;
                marker.Id = anchor.Name ?? string.Empty;
                var renderer = sphere.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_Color", anchor.Color);
                    block.SetColor("_BaseColor", anchor.Color);
                    renderer.SetPropertyBlock(block);
                }
                _anchorGizmos.Add(sphere);
            }
        }

        public void ClearAnchors()
        {
            foreach (var gizmo in _anchorGizmos)
            {
                if (gizmo != null)
                {
                    Destroy(gizmo);
                }
            }
            _anchorGizmos.Clear();
        }

        public struct EffectGizmo
        {
            public string Id;
            public Vector3 LocalPosition;
            public float Radius;
            public Color Color;
        }

        public void SetEffectGizmos(IReadOnlyList<EffectGizmo> gizmos)
        {
            ClearEffects();
            if (_modelRoot == null || gizmos == null) return;

            foreach (var gizmo in gizmos)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"Effect_{gizmo.Id}";
                sphere.transform.SetParent(GetMarkerRoot(), false);
                sphere.transform.localPosition = gizmo.LocalPosition;
                float radius = Mathf.Max(gizmo.Radius, 0.008f);
                sphere.transform.localScale = Vector3.one * radius * 2f;
                var marker = sphere.AddComponent<EditMarker>();
                marker.Kind = EditMarkerKind.Effect;
                marker.Id = gizmo.Id ?? string.Empty;
                var renderer = sphere.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_Color", gizmo.Color);
                    block.SetColor("_BaseColor", gizmo.Color);
                    renderer.SetPropertyBlock(block);
                }
                _effectGizmos.Add(sphere);
            }
        }

        public void ClearEffects()
        {
            foreach (var gizmo in _effectGizmos)
            {
                if (gizmo != null)
                {
                    Destroy(gizmo);
                }
            }
            _effectGizmos.Clear();
        }

        public void SetPartTransform(Transform part, Vector3 position, Vector3 rotation, Vector3 scale)
        {
            if (part == null) return;
            part.localPosition = position;
            part.localRotation = Quaternion.Euler(rotation);
            part.localScale = scale;
        }

        public void SetPartColor(Transform part, Color color)
        {
            if (part == null) return;
            var renderers = part.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
                renderer.SetPropertyBlock(block);
            }
        }

        public void SetPartMaterial(Transform part, bool useColor, Color color, Texture2D texture)
        {
            if (part == null) return;
            var renderers = part.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                // If there's no active override, revert back to the original shared material.
                bool hasAnyOverride = texture != null || useColor;
                if (!hasAnyOverride)
                {
                    if (_overrideSharedMaterials.TryGetValue(renderer, out var oldOverride) && oldOverride != null)
                    {
                        Destroy(oldOverride);
                    }
                    _overrideSharedMaterials.Remove(renderer);

                    if (_originalSharedMaterials.TryGetValue(renderer, out var original) && original != null)
                    {
                        renderer.sharedMaterial = original;
                    }
                    _originalSharedMaterials.Remove(renderer);
                    continue;
                }

                if (!_originalSharedMaterials.ContainsKey(renderer) && renderer.sharedMaterial != null)
                {
                    _originalSharedMaterials[renderer] = renderer.sharedMaterial;
                }

                if (!_overrideSharedMaterials.TryGetValue(renderer, out var overrideMat) || overrideMat == null)
                {
                    var baseMat = renderer.sharedMaterial;
                    overrideMat = baseMat != null
                        ? new Material(baseMat)
                        : new Material(Shader.Find("Standard"));
                    overrideMat.name = $"{renderer.gameObject.name}_RuntimeOverride";
                    _overrideSharedMaterials[renderer] = overrideMat;
                }

                // Apply requested overrides.
                if (texture != null)
                {
                    overrideMat.mainTexture = texture;
                    if (overrideMat.HasProperty("_BaseMap")) overrideMat.SetTexture("_BaseMap", texture);
                }
                else
                {
                    overrideMat.mainTexture = null;
                    if (overrideMat.HasProperty("_BaseMap")) overrideMat.SetTexture("_BaseMap", null);
                }

                if (useColor)
                {
                    overrideMat.color = color;
                    if (overrideMat.HasProperty("_BaseColor")) overrideMat.SetColor("_BaseColor", color);
                }

                // Use sharedMaterial to avoid Unity silently instancing via renderer.material.
                renderer.sharedMaterial = overrideMat;
            }
        }

        /// <summary>
        /// Dims all non-selected parts for clearer selection, regardless of hierarchy/grouping.
        /// This only affects a per-renderer MaterialPropertyBlock and is fully reversible.
        /// </summary>
        public void SetPartFocus(Transform focusedPart, float dimFactor = 0.25f)
        {
            if (_modelRoot == null)
            {
                ClearPartFocus();
                return;
            }

            // Restore any prior focus state first.
            ClearPartFocus();
            _focusedPart = focusedPart;
            if (_focusedPart == null) return;

            dimFactor = Mathf.Clamp01(dimFactor);
            if (dimFactor >= 0.999f) return;

            var focusedRenderers = new HashSet<Renderer>(_focusedPart.GetComponentsInChildren<Renderer>(true));
            var allRenderers = _modelRoot.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in allRenderers)
            {
                if (renderer == null) continue;
                if (focusedRenderers.Contains(renderer)) continue;

                // Cache the existing block so we can restore exactly.
                var existing = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(existing);
                var cached = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(cached);
                _partFocusOriginalBlocks[renderer] = cached;

                Color baseColor = Color.white;
                var mat = renderer.sharedMaterial;
                if (mat != null)
                {
                    if (mat.HasProperty("_BaseColor")) baseColor = mat.GetColor("_BaseColor");
                    else if (mat.HasProperty("_Color")) baseColor = mat.GetColor("_Color");
                    else baseColor = mat.color;
                }

                var dimmed = new Color(baseColor.r * dimFactor, baseColor.g * dimFactor, baseColor.b * dimFactor, baseColor.a);
                existing.SetColor("_BaseColor", dimmed);
                existing.SetColor("_Color", dimmed);
                renderer.SetPropertyBlock(existing);
            }
        }

        public void ClearPartFocus()
        {
            if (_partFocusOriginalBlocks.Count > 0)
            {
                foreach (var kvp in _partFocusOriginalBlocks)
                {
                    var renderer = kvp.Key;
                    if (renderer == null) continue;
                    renderer.SetPropertyBlock(kvp.Value);
                }
                _partFocusOriginalBlocks.Clear();
            }
            _focusedPart = null;
        }

        public void SetSelectionOutline(Transform target)
        {
            _selectionTarget = target;
            if (_selectionTarget == null)
            {
                ClearSelectionOutline();
                return;
            }
            EnsureSelectionOutline();
            UpdateSelectionOutline();
        }

        public void ClearSelectionOutline()
        {
            _selectionTarget = null;
            if (_selectionOutline != null)
            {
                _selectionOutline.gameObject.SetActive(false);
            }
        }

        public bool TryPickLocal(Vector2 viewportPoint, out Vector3 localPosition)
        {
            localPosition = Vector3.zero;
            if (_camera == null || _modelRoot == null) return false;
            if (!IsViewportPointValid(viewportPoint)) return false;
            if (!TryGetViewportRay(viewportPoint, out var ray)) return false;
            var hits = Physics.RaycastAll(ray, 100f);
            if (hits == null || hits.Length == 0) return false;
            float best = float.MaxValue;
            Vector3 bestPoint = Vector3.zero;
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.collider.GetComponentInParent<GizmoHandle>() != null) continue;
                if (hit.collider.GetComponent<EditMarker>() != null) continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    bestPoint = hit.point;
                }
            }
            if (best == float.MaxValue) return false;
            localPosition = _modelRoot.InverseTransformPoint(bestPoint);
            return true;
        }

        public bool TryPickEditMarker(Vector2 viewportPoint, out EditMarker marker)
        {
            marker = null;
            if (_camera == null) return false;
            if (!IsViewportPointValid(viewportPoint)) return false;
            if (!TryGetViewportRay(viewportPoint, out var ray)) return false;
            var hits = Physics.RaycastAll(ray, 100f);
            if (hits == null || hits.Length == 0) return false;
            float best = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.collider.GetComponentInParent<GizmoHandle>() != null) continue;
                var candidate = hit.collider.GetComponent<EditMarker>();
                if (candidate == null) continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    marker = candidate;
                }
            }
            return marker != null;
        }

        public bool TryPickPart(Vector2 viewportPoint, out Transform part)
        {
            part = null;
            if (_camera == null || _modelRoot == null) return false;
            if (!IsViewportPointValid(viewportPoint)) return false;
            if (!TryGetViewportRay(viewportPoint, out var ray)) return false;
            var hits = Physics.RaycastAll(ray, 100f);
            if (hits == null || hits.Length == 0) return false;
            float best = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.collider.GetComponentInParent<GizmoHandle>() != null) continue;
                if (hit.collider.GetComponent<EditMarker>() != null) continue;
                var hitTransform = hit.collider.transform;
                if (hitTransform == null) continue;
                if (!hitTransform.IsChildOf(_modelRoot)) continue;
                var candidate = hitTransform;
                while (candidate != null && candidate.parent != _modelRoot)
                {
                    candidate = candidate.parent;
                }
                if (candidate == null) continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    part = candidate;
                }
            }
            return part != null;
        }

        public bool TryPickGizmoHandle(Vector2 viewportPoint, out GizmoHandle handle, out Vector3 worldPoint)
        {
            handle = null;
            worldPoint = Vector3.zero;
            if (!_gizmoVisible || _camera == null || _gizmoRoot == null) return false;
            if (!IsViewportPointValid(viewportPoint)) return false;
            if (!TryGetViewportRay(viewportPoint, out var ray)) return false;
            var hits = Physics.RaycastAll(ray, 200f);
            if (hits == null || hits.Length == 0) return false;
            float best = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                var candidate = hit.collider.GetComponentInParent<GizmoHandle>();
                if (candidate == null) continue;
                if (candidate.Kind == GizmoHandleKind.RotateAxis)
                {
                    var axis = candidate.transform.forward;
                    var plane = new Plane(axis, candidate.transform.position);
                    if (!plane.Raycast(ray, out var enter)) continue;
                    var point = ray.GetPoint(enter);
                    float handleScale = _gizmoRoot != null ? _gizmoRoot.localScale.x : _gizmoScale;
                    float radius = candidate.Radius * handleScale;
                    float thickness = Mathf.Max(candidate.Thickness * handleScale * 1.6f, 0.02f);
                    float dist = Vector3.Distance(candidate.transform.position, point);
                    if (Mathf.Abs(dist - radius) > thickness) continue;
                    if (hit.distance < best)
                    {
                        best = hit.distance;
                        handle = candidate;
                        worldPoint = point;
                    }
                    continue;
                }
                if (hit.distance < best)
                {
                    best = hit.distance;
                    handle = candidate;
                    worldPoint = hit.point;
                }
            }
            return handle != null;
        }

        public void SetTransformGizmo(Vector3 worldPosition, Quaternion worldRotation, Bounds bounds, GizmoMode mode, bool visible)
        {
            _gizmoVisible = visible;
            if (!visible)
            {
                SetGizmoActive(false);
                return;
            }
            EnsureGizmoRoot();
            _gizmoWorldPos = worldPosition;
            _gizmoWorldRot = worldRotation;
            bool rebuild = _gizmoMode != mode || _gizmoObjects.Count == 0;
            if (mode == GizmoMode.Scale || (mode == GizmoMode.Rotate && !_lockRotateRebuild))
            {
                if (_gizmoBounds.size != bounds.size || _gizmoBounds.center != bounds.center)
                {
                    rebuild = true;
                }
                _gizmoBounds = bounds;
            }
            if (rebuild)
            {
                BuildGizmo(mode, bounds);
            }
            UpdateGizmoTransform();
            SetGizmoActive(true);
        }

        public void HideTransformGizmo()
        {
            _gizmoVisible = false;
            SetGizmoActive(false);
        }

        public bool TryGetModelBoundsLocal(out Bounds bounds)
        {
            return TryGetModelBounds(out bounds);
        }

        public bool TryGetPartBoundsLocal(Transform part, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            if (part == null) return false;
            return TryGetBoundsRelativeTo(part, part, out bounds);
        }

        public bool TryGetViewportRay(Vector2 viewportPoint, out Ray ray)
        {
            ray = new Ray();
            if (_camera == null) return false;
            viewportPoint = new Vector2(Mathf.Clamp01(viewportPoint.x), Mathf.Clamp01(viewportPoint.y));
            ray = _camera.ViewportPointToRay(new Vector3(viewportPoint.x, viewportPoint.y, 0f));
            return true;
        }

        private static bool IsViewportPointValid(Vector2 viewportPoint)
        {
            return viewportPoint.x >= 0f && viewportPoint.x <= 1f &&
                   viewportPoint.y >= 0f && viewportPoint.y <= 1f;
        }

        private void EnsureGizmoRoot()
        {
            if (_gizmoRoot != null) return;
            var rootGo = new GameObject("ComponentStudioGizmo");
            rootGo.transform.SetParent(transform, false);
            _gizmoRoot = rootGo.transform;
            _gizmoRoot.localPosition = Vector3.zero;
            _gizmoRoot.localRotation = Quaternion.identity;
        }

        private void SetGizmoActive(bool active)
        {
            if (_gizmoRoot == null) return;
            _gizmoRoot.gameObject.SetActive(active);
        }

        private void BuildGizmo(GizmoMode mode, Bounds bounds)
        {
            ClearGizmoObjects();
            _gizmoMode = mode;
            if (mode == GizmoMode.Move)
            {
                BuildMoveGizmo(bounds);
            }
            else if (mode == GizmoMode.Rotate)
            {
                BuildRotateGizmo(bounds);
            }
            else if (mode == GizmoMode.Scale)
            {
                BuildScaleGizmo(bounds);
            }
        }

        private void ClearGizmoObjects()
        {
            foreach (var obj in _gizmoObjects)
            {
                if (obj != null) Destroy(obj);
            }
            _gizmoObjects.Clear();
            foreach (var line in _gizmoLines)
            {
                if (line != null) Destroy(line.gameObject);
            }
            _gizmoLines.Clear();
            _rotateHandles.Clear();
            _rotateArcLines.Clear();
        }

        private void BuildMoveGizmo(Bounds bounds)
        {
            float extent = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
            float shaftLength = Mathf.Clamp(Mathf.Max(extent * 1.6f, 0.65f), 0.6f, 4f);
            float shaftRadius = Mathf.Clamp(shaftLength * 0.06f, 0.03f, 0.16f);
            float headSize = Mathf.Clamp(shaftLength * 0.24f, 0.14f, 0.5f);

            CreateAxisHandle("MoveAxisX", GizmoHandleKind.MoveAxis, GizmoAxis.X, Color.red, Vector3.right, shaftLength, shaftRadius, headSize);
            CreateAxisHandle("MoveAxisY", GizmoHandleKind.MoveAxis, GizmoAxis.Y, Color.green, Vector3.up, shaftLength, shaftRadius, headSize);
            CreateAxisHandle("MoveAxisZ", GizmoHandleKind.MoveAxis, GizmoAxis.Z, new Color(0.35f, 0.7f, 1f), Vector3.forward, shaftLength, shaftRadius, headSize);
        }

        private void BuildRotateGizmo(Bounds bounds)
        {
            float extent = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
            float radius = Mathf.Max(extent * 1.0f, 0.25f);
            float thickness = Mathf.Max(radius * 0.08f, 0.035f);
            float width = Mathf.Max(radius * 0.028f, 0.012f);
            CreateRotateHandle("RotateAxisX", GizmoAxis.X, Color.red, Vector3.right, radius, thickness, width);
            CreateRotateHandle("RotateAxisY", GizmoAxis.Y, Color.green, Vector3.up, radius, thickness, width);
            CreateRotateHandle("RotateAxisZ", GizmoAxis.Z, new Color(0.35f, 0.7f, 1f), Vector3.forward, radius, thickness, width);
        }

        private void BuildScaleGizmo(Bounds bounds)
        {
            var ext = bounds.extents;
            float minExtent = 0.05f;
            ext = new Vector3(
                Mathf.Max(ext.x, minExtent),
                Mathf.Max(ext.y, minExtent),
                Mathf.Max(ext.z, minExtent));
            bounds = new Bounds(bounds.center, ext * 2f);

            float handleSize = Mathf.Clamp(bounds.size.magnitude * 0.045f, 0.08f, 0.22f);
            float cornerSize = Mathf.Clamp(bounds.size.magnitude * 0.055f, 0.09f, 0.26f);
            float handleOffset = Mathf.Max(handleSize * 0.35f, 0.02f);
            float cornerOffset = Mathf.Max(cornerSize * 0.3f, 0.02f);

            float x = Mathf.Max(ext.x + handleOffset, minExtent * 0.5f);
            float y = Mathf.Max(ext.y + handleOffset, minExtent * 0.5f);
            float z = Mathf.Max(ext.z + handleOffset, minExtent * 0.5f);

            CreateScaleAxisHandle("ScaleAxisX", GizmoAxis.X, Color.red, new Vector3(x, 0f, 0f), handleSize);
            CreateScaleAxisHandle("ScaleAxisY", GizmoAxis.Y, Color.green, new Vector3(0f, y, 0f), handleSize);
            CreateScaleAxisHandle("ScaleAxisZ", GizmoAxis.Z, new Color(0.35f, 0.7f, 1f), new Vector3(0f, 0f, z), handleSize);

            var center = bounds.center;
            float cornerX = Mathf.Max(ext.x + cornerOffset, minExtent * 0.5f);
            float cornerY = Mathf.Max(ext.y + cornerOffset, minExtent * 0.5f);
            float cornerZ = Mathf.Max(ext.z + cornerOffset, minExtent * 0.5f);
            var cornerOffsets = new[]
            {
                new Vector3(cornerX, cornerY, cornerZ),
                new Vector3(cornerX, cornerY, -cornerZ),
                new Vector3(cornerX, -cornerY, cornerZ),
                new Vector3(cornerX, -cornerY, -cornerZ),
                new Vector3(-cornerX, cornerY, cornerZ),
                new Vector3(-cornerX, cornerY, -cornerZ),
                new Vector3(-cornerX, -cornerY, cornerZ),
                new Vector3(-cornerX, -cornerY, -cornerZ)
            };
            foreach (var offset in cornerOffsets)
            {
                CreateCornerHandle(center + offset, new Color(0.82f, 0.88f, 0.98f), cornerSize);
            }
        }

        private void CreateAxisHandle(string name, GizmoHandleKind kind, GizmoAxis axis, Color color, Vector3 direction, float shaftLength, float shaftRadius, float headSize)
        {
            if (_gizmoRoot == null) return;
            var handle = new GameObject(name);
            handle.transform.SetParent(_gizmoRoot, false);
            handle.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
            var handleComp = handle.AddComponent<GizmoHandle>();
            handleComp.Kind = kind;
            handleComp.Axis = axis;

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.transform.SetParent(handle.transform, false);
            shaft.transform.localScale = new Vector3(shaftRadius, shaftLength * 0.5f, shaftRadius);
            shaft.transform.localPosition = new Vector3(0f, shaftLength * 0.5f, 0f);
            ApplyGizmoColor(shaft, color);

            var head = new GameObject($"{name}_Head");
            head.transform.SetParent(handle.transform, false);
            head.transform.localPosition = new Vector3(0f, shaftLength, 0f);
            head.transform.localScale = new Vector3(headSize, headSize * 1.4f, headSize);
            var meshFilter = head.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = GetArrowHeadMesh();
            var meshRenderer = head.AddComponent<MeshRenderer>();
            ApplyGizmoColor(head, color);
            var meshCollider = head.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = true;

            if (kind == GizmoHandleKind.MoveAxis)
            {
                var hitbox = handle.AddComponent<BoxCollider>();
                float totalLength = shaftLength + headSize * 1.1f;
                float hitRadius = Mathf.Max(shaftRadius * 2.6f, headSize * 0.7f);
                hitbox.size = new Vector3(hitRadius, totalLength, hitRadius);
                hitbox.center = new Vector3(0f, totalLength * 0.5f, 0f);
            }

            _gizmoObjects.Add(handle);
        }

        private static Mesh GetArrowHeadMesh()
        {
            if (_arrowHeadMesh != null) return _arrowHeadMesh;
            var mesh = new Mesh { name = "GizmoArrowHead" };
            var verts = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0f, 1f, 0f)
            };
            var tris = new[]
            {
                0, 1, 4,
                1, 2, 4,
                2, 3, 4,
                3, 0, 4,
                0, 3, 2,
                0, 2, 1
            };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _arrowHeadMesh = mesh;
            return _arrowHeadMesh;
        }

        private void CreateRotateHandle(string name, GizmoAxis axis, Color color, Vector3 direction, float radius, float thickness, float width)
        {
            if (_gizmoRoot == null) return;
            var handle = new GameObject(name);
            handle.transform.SetParent(_gizmoRoot, false);
            handle.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, direction);
            var handleComp = handle.AddComponent<GizmoHandle>();
            handleComp.Kind = GizmoHandleKind.RotateAxis;
            handleComp.Axis = axis;
            handleComp.Radius = radius;
            handleComp.Thickness = thickness;

            var line = handle.AddComponent<LineRenderer>();
            ConfigureLineRenderer(line, color, 80, handleComp.Radius, width);
            _gizmoLines.Add(line);

            var arcGo = new GameObject($"{name}_Arc");
            arcGo.transform.SetParent(handle.transform, false);
            var arc = arcGo.AddComponent<LineRenderer>();
            ConfigureArcRenderer(arc, Color.Lerp(color, Color.white, 0.5f), width * 1.4f);
            arc.enabled = false;
            _rotateArcLines[axis] = arc;
            _rotateHandles[axis] = handleComp;

            var collider = handle.AddComponent<SphereCollider>();
            collider.radius = handleComp.Radius + handleComp.Thickness * 2f;
            collider.isTrigger = false;

            _gizmoObjects.Add(handle);
        }

        private void CreateScaleAxisHandle(string name, GizmoAxis axis, Color color, Vector3 localOffset, float size)
        {
            if (_gizmoRoot == null) return;
            var handle = new GameObject(name);
            handle.transform.SetParent(_gizmoRoot, false);
            handle.transform.localPosition = Vector3.zero;
            var handleComp = handle.AddComponent<GizmoHandle>();
            handleComp.Kind = GizmoHandleKind.ScaleAxis;
            handleComp.Axis = axis;

            float length = localOffset.magnitude;
            if (length > 0.0001f)
            {
                var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                shaft.transform.SetParent(handle.transform, false);
                shaft.transform.localRotation = Quaternion.FromToRotation(Vector3.up, localOffset.normalized);
                float shaftRadius = Mathf.Max(size * 0.15f, 0.01f);
                shaft.transform.localScale = new Vector3(shaftRadius, length * 0.5f, shaftRadius);
                shaft.transform.localPosition = localOffset * 0.5f;
                ApplyGizmoColor(shaft, color);
                var shaftCollider = shaft.GetComponent<Collider>();
                if (shaftCollider != null) Destroy(shaftCollider);
            }

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(handle.transform, false);
            cube.transform.localScale = Vector3.one * size;
            cube.transform.localPosition = localOffset;
            ApplyGizmoColor(cube, color);
            _gizmoObjects.Add(handle);
        }

        private void CreateCornerHandle(Vector3 localPosition, Color color, float size)
        {
            if (_gizmoRoot == null) return;
            var handle = new GameObject("ScaleCorner");
            handle.transform.SetParent(_gizmoRoot, false);
            handle.transform.localPosition = localPosition;
            var handleComp = handle.AddComponent<GizmoHandle>();
            handleComp.Kind = GizmoHandleKind.ScaleUniform;
            handleComp.Axis = GizmoAxis.Uniform;

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(handle.transform, false);
            cube.transform.localScale = Vector3.one * size;
            ApplyGizmoColor(cube, color);
            _gizmoObjects.Add(handle);
        }

        private void CreateScaleBox(Bounds bounds, Color color)
        {
            var min = bounds.min;
            var max = bounds.max;
            var corners = new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, max.z),
                new Vector3(min.x, max.y, max.z)
            };
            int[,] edges =
            {
                {0,1},{1,2},{2,3},{3,0},
                {4,5},{5,6},{6,7},{7,4},
                {0,4},{1,5},{2,6},{3,7}
            };
            float lineWidth = Mathf.Clamp(bounds.size.magnitude * 0.01f, 0.03f, 0.06f);
            for (int i = 0; i < edges.GetLength(0); i++)
            {
                var lineObj = new GameObject($"ScaleBoxEdge{i}");
                lineObj.transform.SetParent(_gizmoRoot, false);
                var line = lineObj.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.useWorldSpace = false;
                line.startWidth = lineWidth;
                line.endWidth = lineWidth;
                line.material = new Material(Shader.Find("Sprites/Default"));
                ConfigureGizmoMaterial(line.material);
                line.startColor = color;
                line.endColor = color;
                line.SetPosition(0, corners[edges[i, 0]]);
                line.SetPosition(1, corners[edges[i, 1]]);
                _gizmoLines.Add(line);
            }
        }

        private void ConfigureLineRenderer(LineRenderer line, Color color, int segments, float radius, float width)
        {
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = segments;
            line.startWidth = width;
            line.endWidth = width;
            line.material = new Material(Shader.Find("Sprites/Default"));
            ConfigureGizmoMaterial(line.material);
            line.startColor = color;
            line.endColor = color;
            float step = Mathf.PI * 2f / (segments - 1);
            for (int i = 0; i < segments; i++)
            {
                float angle = step * i;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
        }

        private void ConfigureArcRenderer(LineRenderer line, Color color, float width)
        {
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = 0;
            line.startWidth = width;
            line.endWidth = width;
            line.material = new Material(Shader.Find("Sprites/Default"));
            ConfigureGizmoMaterial(line.material);
            line.startColor = color;
            line.endColor = color;
        }

        public void SetRotateArc(GizmoAxis axis, float degrees, float startAngle)
        {
            if (_gizmoMode != GizmoMode.Rotate) return;
            if (!_rotateArcLines.TryGetValue(axis, out var arc) || arc == null) return;
            if (!_rotateHandles.TryGetValue(axis, out var handle) || handle == null) return;

            float abs = Mathf.Abs(degrees);
            if (abs < 0.5f)
            {
                arc.enabled = false;
                return;
            }

            float displayAngle = Mathf.Clamp(degrees, -360f, 360f);
            int segments = Mathf.Clamp(Mathf.CeilToInt(RotateArcSegments * Mathf.Clamp01(Mathf.Abs(displayAngle) / 360f)), 3, RotateArcSegments);
            arc.positionCount = segments;
            arc.enabled = true;

            float step = displayAngle / (segments - 1);
            for (int i = 0; i < segments; i++)
            {
                float angle = (startAngle + step * i) * Mathf.Deg2Rad;
                arc.SetPosition(i, new Vector3(Mathf.Cos(angle) * handle.Radius, Mathf.Sin(angle) * handle.Radius, 0f));
            }
        }

        public void ClearRotateArcs()
        {
            foreach (var arc in _rotateArcLines.Values)
            {
                if (arc != null) arc.enabled = false;
            }
        }

        public void SetRotateRebuildLock(bool locked)
        {
            _lockRotateRebuild = locked;
        }

        private void ApplyGizmoColor(GameObject target, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Standard");
            if (shader == null) return;
            var mat = new Material(shader) { name = "ComponentStudioGizmoMat" };
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            ConfigureGizmoMaterial(mat);
            renderer.sharedMaterial = mat;
        }

        private static void ConfigureGizmoMaterial(Material material)
        {
            if (material == null) return;
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
            if (material.HasProperty("_ZTest")) material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        private void UpdateGizmoTransform()
        {
            if (!_gizmoVisible || _gizmoRoot == null) return;
            _gizmoRoot.position = _gizmoWorldPos;
            _gizmoRoot.rotation = _gizmoWorldRot;
            _gizmoScale = ComputeGizmoScale(_gizmoWorldPos);
            _gizmoRoot.localScale = Vector3.one * _gizmoScale;
        }

        private float ComputeGizmoScale(Vector3 worldPos)
        {
            if (_camera == null) return 1f;
            float dist = Vector3.Distance(_camera.transform.position, worldPos);
            return Mathf.Clamp(dist * 0.1f, 0.08f, 2f);
        }

        private void EnsureSelectionOutline()
        {
            if (_selectionOutline != null) return;
            var lineGo = new GameObject("ComponentStudioSelectionOutline");
            lineGo.transform.SetParent(transform, false);
            _selectionOutline = lineGo.AddComponent<LineRenderer>();
            _selectionOutline.useWorldSpace = true;
            _selectionOutline.loop = false;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                var mat = new Material(shader);
                ConfigureGizmoMaterial(mat);
                _selectionOutline.material = mat;
            }
            _selectionOutline.startColor = Color.white;
            _selectionOutline.endColor = Color.white;
            _selectionOutline.positionCount = 17;
            _selectionOutline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _selectionOutline.receiveShadows = false;
        }

        private void UpdateSelectionOutline()
        {
            if (_selectionOutline == null || _selectionTarget == null)
            {
                return;
            }
            if (!TryGetBoundsRelativeTo(_selectionTarget, _selectionTarget, out var bounds))
            {
                _selectionOutline.gameObject.SetActive(false);
                return;
            }
            var ext = bounds.extents;
            if (ext == Vector3.zero)
            {
                ext = Vector3.one * 0.01f;
            }
            var center = bounds.center;
            var localCorners = new[]
            {
                center + new Vector3(-ext.x, -ext.y, -ext.z),
                center + new Vector3(ext.x, -ext.y, -ext.z),
                center + new Vector3(ext.x, -ext.y, ext.z),
                center + new Vector3(-ext.x, -ext.y, ext.z),
                center + new Vector3(-ext.x, ext.y, -ext.z),
                center + new Vector3(ext.x, ext.y, -ext.z),
                center + new Vector3(ext.x, ext.y, ext.z),
                center + new Vector3(-ext.x, ext.y, ext.z)
            };
            var order = new[] { 0, 1, 2, 3, 0, 4, 5, 1, 5, 6, 2, 6, 7, 3, 7, 4, 0 };
            var points = new Vector3[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                points[i] = _selectionTarget.TransformPoint(localCorners[order[i]]);
            }
            _selectionOutline.positionCount = points.Length;
            _selectionOutline.SetPositions(points);

            float width = 0.003f;
            if (_camera != null)
            {
                float dist = Vector3.Distance(_camera.transform.position, _selectionTarget.position);
                width = Mathf.Clamp(dist * 0.0025f, 0.002f, 0.02f);
            }
            _selectionOutline.startWidth = width;
            _selectionOutline.endWidth = width;
            _selectionOutline.gameObject.SetActive(true);
        }

        private Transform GetMarkerRoot()
        {
            if (_markerRoot != null) return _markerRoot;
            var markerGo = new GameObject("ComponentStudioMarkers");
            markerGo.transform.SetParent(_modelRoot != null ? _modelRoot : (_root != null ? _root : transform), false);
            _markerRoot = markerGo.transform;
            return _markerRoot;
        }

        private Transform GetReferenceRoot()
        {
            if (_referenceRoot != null) return _referenceRoot;
            var rootGo = new GameObject("ComponentStudioReference");
            rootGo.transform.SetParent(_root != null ? _root : transform, false);
            rootGo.transform.localPosition = Vector3.zero;
            rootGo.transform.localRotation = Quaternion.identity;
            rootGo.transform.localScale = Vector3.one;
            _referenceRoot = rootGo.transform;
            return _referenceRoot;
        }

        private Material GetReferenceLineMaterial()
        {
            if (_referenceLineMaterial != null) return _referenceLineMaterial;
            // Prefer shaders that respect LineRenderer vertex colors (start/end colors).
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Standard");
            if (shader == null) return null;
            _referenceLineMaterial = new Material(shader) { name = "ComponentStudioReferenceLineMat" };
            if (_referenceLineMaterial.HasProperty("_BaseColor")) _referenceLineMaterial.SetColor("_BaseColor", Color.white);
            if (_referenceLineMaterial.HasProperty("_Color")) _referenceLineMaterial.SetColor("_Color", Color.white);
            if (_referenceLineMaterial.HasProperty("_Cull")) _referenceLineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            if (_referenceLineMaterial.HasProperty("_ZWrite")) _referenceLineMaterial.SetInt("_ZWrite", 0);
            if (_referenceLineMaterial.HasProperty("_ZTest")) _referenceLineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            _referenceLineMaterial.renderQueue = 3000;
            return _referenceLineMaterial;
        }

        private LineRenderer CreateReferenceLine(string name)
        {
            var root = GetReferenceRoot();
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.material = GetReferenceLineMaterial();
            return lr;
        }

        private void EnsureReferenceLines(int desired)
        {
            while (_referenceLines.Count < desired)
            {
                _referenceLines.Add(CreateReferenceLine($"RefLine_{_referenceLines.Count}"));
            }
        }

        private void SetReferenceLine(int index, Vector3 a, Vector3 b, Color color, float width)
        {
            if (index < 0 || index >= _referenceLines.Count) return;
            var lr = _referenceLines[index];
            if (lr == null) return;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = color;
            lr.endColor = color;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.enabled = true;
        }

        private void UpdateReferenceFrame(Bounds modelBounds)
        {
            if (_root == null) return;

            if (!ShouldShowReferenceFrame())
            {
                if (_referenceRoot != null) _referenceRoot.gameObject.SetActive(false);
                for (int i = 0; i < _referenceLines.Count; i++)
                {
                    if (_referenceLines[i] != null) _referenceLines[i].enabled = false;
                }
                return;
            }

            if (_referenceRoot != null && !_referenceRoot.gameObject.activeSelf)
            {
                _referenceRoot.gameObject.SetActive(true);
            }

            float extent = Mathf.Max(modelBounds.extents.x, Mathf.Max(modelBounds.extents.y, modelBounds.extents.z));
            if (extent <= 0.00001f) extent = 0.05f;
            // Make grid/axes more visible: larger extents and thicker lines.
            float axisLen = extent * 2.4f;
            float gridHalf = extent * 3.6f;
            float y = modelBounds.min.y;
            // Push the grid slightly below the model to reduce z-fighting shimmer.
            y -= Mathf.Max(extent * 0.01f, 0.002f);
            float widthAxis = Mathf.Clamp(extent * 0.03f, 0.0025f, 0.04f);
            float widthGrid = Mathf.Clamp(extent * 0.02f, 0.0018f, 0.02f);

            int gridLines = (ReferenceGridHalfLines * 2 + 1) * 2;
            EnsureReferenceLines(3 + gridLines);

            SetReferenceLine(0, Vector3.zero, new Vector3(axisLen, 0f, 0f), new Color(0.95f, 0.25f, 0.25f, 1f), widthAxis);
            SetReferenceLine(1, Vector3.zero, new Vector3(0f, axisLen, 0f), new Color(0.25f, 0.95f, 0.35f, 1f), widthAxis);
            SetReferenceLine(2, Vector3.zero, new Vector3(0f, 0f, axisLen), new Color(0.35f, 0.7f, 1f, 1f), widthAxis);

            float spacing = gridHalf / ReferenceGridHalfLines;
            int idx = 3;
            for (int i = -ReferenceGridHalfLines; i <= ReferenceGridHalfLines; i++)
            {
                float p = i * spacing;
                float alpha = i == 0 ? 0.32f : 0.16f;
                float w = i == 0 ? widthGrid * 1.35f : widthGrid;
                var c = new Color(0.8f, 0.85f, 0.95f, alpha);
                // Lines parallel to X (vary Z)
                SetReferenceLine(idx++, new Vector3(-gridHalf, y, p), new Vector3(gridHalf, y, p), c, w);
                // Lines parallel to Z (vary X)
                SetReferenceLine(idx++, new Vector3(p, y, -gridHalf), new Vector3(p, y, gridHalf), c, w);
            }

            // Disable any extras if we ever reduce desired.
            for (int i = idx; i < _referenceLines.Count; i++)
            {
                if (_referenceLines[i] != null) _referenceLines[i].enabled = false;
            }
        }

        private void EnsureCamera()
        {
            if (_camera != null) return;
            var rootGo = new GameObject("ComponentStudioRoot");
            rootGo.transform.SetParent(transform, false);
            _root = rootGo.transform;

            var camGo = new GameObject("ComponentStudioCamera");
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = _background;
            _camera.orthographic = !_usePerspective;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 5000f;
            _camera.fieldOfView = _fieldOfView;
            _camera.cullingMask &= ~(1 << ViewCubeLayer);

            _keyLight = CreateLight("KeyLight", new Vector3(0.4f, 0.8f, 0.2f), 1.1f);
            _fillLight = CreateLight("FillLight", new Vector3(-0.6f, 0.4f, -0.2f), 0.7f);
            _rimLight = CreateLight("RimLight", new Vector3(0.2f, 0.2f, -0.8f), 0.35f);
            _headLight = CreateHeadLight();
            ApplyLightingBlend();
            EnsureViewCube(_viewCubeSize);
            // Provide immediate orientation cues even before a model is loaded.
            UpdateReferenceFrame(new Bounds(Vector3.zero, Vector3.one * 0.1f));
        }

        private Light CreateHeadLight()
        {
            if (_camera == null || _headLight != null) return _headLight;
            var lightGo = new GameObject("HeadLight");
            lightGo.transform.SetParent(_camera.transform, false);
            lightGo.transform.localRotation = Quaternion.identity;
            _headLight = lightGo.AddComponent<Light>();
            _headLight.type = LightType.Directional;
            _headLight.intensity = 0.45f;
            _headLight.color = Color.white;
            _headLight.shadows = LightShadows.None;
            _headLight.cullingMask &= ~(1 << ViewCubeLayer);
            return _headLight;
        }

        private Light CreateLight(string name, Vector3 dir, float intensity)
        {
            var lightGo = new GameObject(name);
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.rotation = Quaternion.LookRotation(dir.normalized);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = Color.white;
            light.shadows = LightShadows.None;
            light.cullingMask &= ~(1 << ViewCubeLayer);
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
        }

        private void EnsureRenderTexture(int width, int height)
        {
            width = Mathf.Max(16, width);
            height = Mathf.Max(16, height);
            if (_renderTexture != null && _renderTexture.width == width && _renderTexture.height == height) return;
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }
            _renderTexture = new RenderTexture(width, height, 16)
            {
                name = "ComponentStudioRT",
                antiAliasing = 4
            };
            _renderTexture.Create();
            if (_camera != null) _camera.targetTexture = _renderTexture;
            _viewportSize = new Vector2(width, height);
            UpdateCameraTransform();
        }

        private void EnsureViewCube(int size)
        {
            if (_viewCubeRoot == null)
            {
                var rootGo = new GameObject("ComponentStudioViewCubeRoot");
                rootGo.transform.SetParent(transform, false);
                _viewCubeRoot = rootGo.transform;
                _viewCubeRoot.localPosition = Vector3.zero;
                _viewCubeRoot.localRotation = Quaternion.identity;

                _viewCubeModel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _viewCubeModel.name = "ViewCube";
                _viewCubeModel.transform.SetParent(_viewCubeRoot, false);
                _viewCubeModel.transform.localScale = Vector3.one;
                SetLayerRecursively(_viewCubeModel.transform, ViewCubeLayer);
                var renderer = _viewCubeModel.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Standard") ??
                        Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color");
                    if (shader != null)
                    {
                        var mat = new Material(shader) { name = "ViewCubeMat" };
                        mat.color = new Color(0.22f, 0.26f, 0.32f);
                        renderer.sharedMaterial = mat;
                    }
                }
                EnsureViewCubeDecorations();
            }

            if (_viewCubeCamera == null)
            {
                var camGo = new GameObject("ComponentStudioViewCubeCamera");
                camGo.transform.SetParent(transform, false);
                _viewCubeCamera = camGo.AddComponent<Camera>();
                _viewCubeCamera.clearFlags = CameraClearFlags.SolidColor;
                _viewCubeCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _viewCubeCamera.orthographic = false;
                _viewCubeCamera.fieldOfView = 26f;
                _viewCubeCamera.nearClipPlane = 0.05f;
                _viewCubeCamera.farClipPlane = 10f;
                _viewCubeCamera.cullingMask = 1 << ViewCubeLayer;
                camGo.transform.localPosition = new Vector3(0f, 0f, -3.6f);
                camGo.transform.localRotation = Quaternion.identity;
                camGo.transform.LookAt(Vector3.zero);
            }

            EnsureViewCubeLight();

            size = Mathf.Clamp(size, 64, 256);
            if (_viewCubeTexture == null || _viewCubeTexture.width != size || _viewCubeTexture.height != size)
            {
                if (_viewCubeTexture != null)
                {
                    _viewCubeTexture.Release();
                    Destroy(_viewCubeTexture);
                }
                _viewCubeTexture = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
                {
                    name = "ComponentStudioViewCubeRT",
                    antiAliasing = 4
                };
                _viewCubeTexture.Create();
            }
            if (_viewCubeCamera != null) _viewCubeCamera.targetTexture = _viewCubeTexture;
            UpdateViewCubeOrientation();
        }

        private void OnDestroy()
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
            if (_viewCubeTexture != null)
            {
                _viewCubeTexture.Release();
                Destroy(_viewCubeTexture);
                _viewCubeTexture = null;
            }
            if (_referenceLineMaterial != null)
            {
                Destroy(_referenceLineMaterial);
                _referenceLineMaterial = null;
            }
            ClearModel();
        }

        private void EnsureViewCubeLight()
        {
            if (_viewCubeLight != null || _viewCubeRoot == null) return;
            var lightGo = new GameObject("ViewCubeLight");
            lightGo.transform.SetParent(_viewCubeRoot, false);
            lightGo.transform.localRotation = Quaternion.Euler(35f, 45f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = Color.white;
            light.cullingMask = 1 << ViewCubeLayer;
            _viewCubeLight = light;
        }

        private void EnsureViewCubeDecorations()
        {
            if (_viewCubeModel == null || _viewCubeFaceRoot != null) return;
            var faceRoot = new GameObject("ViewCubeFaces");
            faceRoot.transform.SetParent(_viewCubeModel.transform, false);
            _viewCubeFaceRoot = faceRoot.transform;
            _viewCubeFaces.Clear();

            CreateViewCubeFace("Top", new Vector3(0f, 0.51f, 0f), new Vector3(-90f, 0f, 0f), new Color(0.92f, 0.32f, 0.32f, 1f), "TOP");
            CreateViewCubeFace("Bottom", new Vector3(0f, -0.51f, 0f), new Vector3(90f, 0f, 0f), new Color(0.28f, 0.45f, 0.9f, 1f), "BOTTOM");
            CreateViewCubeFace("Left", new Vector3(-0.51f, 0f, 0f), new Vector3(0f, -90f, 0f), new Color(0.92f, 0.45f, 0.75f, 1f), "LEFT");
            CreateViewCubeFace("Right", new Vector3(0.51f, 0f, 0f), new Vector3(0f, 90f, 0f), new Color(0.34f, 0.78f, 0.42f, 1f), "RIGHT");
            CreateViewCubeFace("Front", new Vector3(0f, 0f, 0.51f), Vector3.zero, new Color(0.32f, 0.7f, 0.86f, 1f), "FRONT");
            CreateViewCubeFace("Back", new Vector3(0f, 0f, -0.51f), new Vector3(0f, 180f, 0f), new Color(0.95f, 0.72f, 0.32f, 1f), "BACK");
        }

        private void CreateViewCubeFace(string name, Vector3 localPos, Vector3 localEuler, Color color, string label)
        {
            if (_viewCubeFaceRoot == null) return;
            var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = $"ViewCubeFace_{name}";
            face.transform.SetParent(_viewCubeFaceRoot, false);
            face.transform.localPosition = localPos;
            face.transform.localRotation = Quaternion.Euler(localEuler);
            face.transform.localScale = Vector3.one * 0.86f;

            var collider = face.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = face.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                    Shader.Find("Unlit/Color") ??
                    Shader.Find("Standard");
                if (shader != null)
                {
                    var mat = new Material(shader) { name = $"ViewCubeFace_{name}_Mat" };
                    mat.color = color;
                    renderer.sharedMaterial = mat;
                }
            }

            var textGo = new GameObject($"ViewCubeFace_{name}_Label");
            textGo.transform.SetParent(face.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, 0.012f);
            textGo.transform.localRotation = Quaternion.identity;
            var text = textGo.AddComponent<TextMesh>();
            text.text = label;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 32;
            text.characterSize = 0.045f;
            text.color = Color.white;
            var textRenderer = text.GetComponent<MeshRenderer>();
            if (textRenderer != null && textRenderer.sharedMaterial != null)
            {
                var textMat = new Material(textRenderer.sharedMaterial) { name = $"ViewCubeFace_{name}_TextMat" };
                if (textMat.HasProperty("_Cull")) textMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                // Render after the face quad to reduce z-fighting with the label's slight offset.
                textMat.renderQueue = 3000;
                if (textMat.HasProperty("_ZWrite")) textMat.SetInt("_ZWrite", 0);
                if (textMat.HasProperty("_ZTest")) textMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                textRenderer.sharedMaterial = textMat;
                textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                textRenderer.receiveShadows = false;
            }

            SetLayerRecursively(face.transform, ViewCubeLayer);
            if (textRenderer != null)
            {
                _viewCubeFaces.Add(new ViewCubeFaceInfo
                {
                    Face = face.transform,
                    LabelRenderer = textRenderer
                });
            }
        }

        private void UpdateViewCubeOrientation()
        {
            if (_viewCubeRoot == null) return;
            if (_camera == null) return;
            var modelRotation = GetModelRotation();
            _viewCubeRoot.rotation = _camera.transform.rotation * modelRotation;
            UpdateViewCubeFaceVisibility();
        }

        private void UpdateViewCubeFaceVisibility()
        {
            if (_viewCubeCamera == null || _viewCubeFaces.Count == 0) return;
            Vector3 camForward = _viewCubeCamera.transform.forward;
            foreach (var face in _viewCubeFaces)
            {
                if (face.Face == null || face.LabelRenderer == null) continue;
                // A face is visible if its normal points toward the camera (i.e., opposite the camera's forward).
                bool frontFacing = Vector3.Dot(face.Face.forward, camForward) < 0f;
                face.LabelRenderer.enabled = frontFacing;
            }
        }

        private Quaternion GetModelRotation()
        {
            if (_modelContent != null) return _modelContent.rotation;
            if (_modelRoot != null) return _modelRoot.rotation;
            return Quaternion.identity;
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            return angle;
        }

        private static void SetLayerRecursively(Transform target, int layer)
        {
            if (target == null) return;
            target.gameObject.layer = layer;
            for (int i = 0; i < target.childCount; i++)
            {
                SetLayerRecursively(target.GetChild(i), layer);
            }
        }

        private void LateUpdate()
        {
            if (_followTarget && _followTransform != null && _root != null)
            {
                _panOffset = _root.InverseTransformPoint(_followTransform.position);
                UpdateCameraTransform();
            }
            if (_selectionTarget != null)
            {
                UpdateSelectionOutline();
            }
        }

        private void UpdateCameraTransform()
        {
            if (_camera == null) return;
            var rotation = Quaternion.Euler(_orbitAngles.x, _orbitAngles.y, 0f);
            float cameraDistance = _usePerspective ? _distance : Mathf.Max(0.1f, _distance * 2f);
            var offset = rotation * new Vector3(0f, 0f, -cameraDistance);
            _camera.transform.position = _panOffset + offset;
            _camera.transform.rotation = rotation;
            _camera.orthographic = !_usePerspective;
            if (!_usePerspective)
            {
                _camera.orthographicSize = Mathf.Max(0.01f, _distance);
            }
            UpdateViewCubeOrientation();
            UpdateGizmoTransform();
        }

        public void SetViewCubeSize(int size)
        {
            _viewCubeSize = Mathf.Clamp(size, 64, 256);
            EnsureViewCube(_viewCubeSize);
        }

        public bool TryPickViewCubeFace(Vector2 viewportPoint, out ViewPreset preset)
        {
            preset = ViewPreset.Front;
            if (_viewCubeCamera == null || _viewCubeRoot == null) return false;
            var ray = _viewCubeCamera.ViewportPointToRay(new Vector3(viewportPoint.x, viewportPoint.y, 0f));
            int mask = 1 << ViewCubeLayer;
            if (!Physics.Raycast(ray, out var hit, 10f, mask)) return false;
            var normal = _viewCubeRoot.InverseTransformDirection(hit.normal);
            var abs = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            if (abs.x > abs.y && abs.x > abs.z)
            {
                preset = normal.x > 0f ? ViewPreset.Right : ViewPreset.Left;
            }
            else if (abs.y > abs.x && abs.y > abs.z)
            {
                preset = normal.y > 0f ? ViewPreset.Top : ViewPreset.Bottom;
            }
            else
            {
                preset = normal.z > 0f ? ViewPreset.Front : ViewPreset.Back;
            }
            return true;
        }

        private void CacheParts()
        {
            _parts.Clear();
            if (_modelRoot == null) return;
            var renderers = _modelRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                _parts.Add(renderer.transform);
            }
            _parts.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        }

        private void EnsureModelColliders()
        {
            if (_modelRoot == null || _modelContent == null) return;
            RemoveModelColliders();

            if (!TryGetBoundsRelativeTo(_modelRoot, _modelContent, out var bounds)) return;
            var renderers = _modelContent.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            var entries = BuildRendererEntries(_modelRoot, renderers);
            if (entries.Count == 0) return;

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
            autoRoot.transform.SetParent(_modelRoot, false);

            if (sphereLike)
            {
                var sphere = autoRoot.AddComponent<SphereCollider>();
                sphere.center = bounds.center;
                sphere.radius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
            }
            else
            {
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

            EnsureNativeBoundsCollider(bounds, sphereLike);
        }

        private void RemoveModelColliders()
        {
            if (_modelRoot == null || _modelContent == null) return;
            var auto = _modelRoot.Find("__AutoColliders");
            if (auto != null)
            {
                Destroy(auto.gameObject);
            }
            var colliders = _modelContent.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                if (collider == null) continue;
                Destroy(collider);
            }
            foreach (var collider in _modelRoot.GetComponents<Collider>())
            {
                if (collider == null) continue;
                Destroy(collider);
            }
        }

        private void EnsureNativeBoundsCollider(Bounds bounds, bool sphereLike)
        {
            if (_modelRoot == null) return;
            if (sphereLike)
            {
                var sphere = _modelRoot.gameObject.AddComponent<SphereCollider>();
                sphere.center = bounds.center;
                sphere.radius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
            }
            else
            {
                var box = _modelRoot.gameObject.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
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

        private bool TryGetBoundsRelativeTo(Transform reference, Transform scope, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            if (reference == null || scope == null) return false;
            var renderers = scope.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            var referenceMatrix = reference.worldToLocalMatrix;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var matrix = referenceMatrix * renderer.transform.localToWorldMatrix;
                if (!IsFinite(matrix)) continue;
                var transformed = TransformBounds(renderer.localBounds, matrix);
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

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
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

        private bool TryGetModelBounds(out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            if (_modelRoot == null) return false;
            var reference = _root != null ? _root : _modelRoot;
            return TryGetBoundsRelativeTo(reference, _modelRoot, out bounds);
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
            return Mathf.Max(0.02f, distance * padding);
        }

        private float ComputeOrthoSize(Bounds bounds, float padding)
        {
            float aspect = _viewportSize.y > 0f ? _viewportSize.x / _viewportSize.y : 1f;
            float halfHeight = Mathf.Max(0.01f, bounds.extents.y);
            float halfWidth = Mathf.Max(0.01f, bounds.extents.x);
            float size = Mathf.Max(halfHeight, halfWidth / Mathf.Max(0.2f, aspect));
            return Mathf.Max(0.02f, size * padding);
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

            public static LightingProfile Studio => new LightingProfile
            {
                KeyColor = new Color(0.98f, 0.95f, 0.92f),
                KeyIntensity = 1.1f,
                FillColor = new Color(0.78f, 0.84f, 0.95f),
                FillIntensity = 0.7f,
                RimColor = new Color(0.7f, 0.78f, 0.95f),
                RimIntensity = 0.35f,
                HeadColor = new Color(0.95f, 0.96f, 1f),
                HeadIntensity = 0.45f
            };

            public static LightingProfile Realistic => new LightingProfile
            {
                KeyColor = new Color(0.85f, 0.80f, 0.75f),
                KeyIntensity = 0.45f,
                FillColor = new Color(0.5f, 0.56f, 0.62f),
                FillIntensity = 0.18f,
                RimColor = new Color(0.45f, 0.52f, 0.68f),
                RimIntensity = 0.12f,
                HeadColor = new Color(0.75f, 0.78f, 0.86f),
                HeadIntensity = 0.18f
            };
        }
    }
}
