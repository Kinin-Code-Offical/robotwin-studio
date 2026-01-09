using UnityEngine;

namespace RobotTwin.UI
{
    /// <summary>
    /// Represents a 3D electrical connection point for wires.
    /// Optimized to only sync when scale or radius actually changes (event-driven).
    /// </summary>
    public class WireAnchor : MonoBehaviour
    {
        [SerializeField] private string _nodeId;
        [SerializeField] private float _radius = 0.006f;
        [SerializeField] private bool _showGizmo = true;
        [SerializeField] private bool _showArrow = true;
        [SerializeField] private Vector3 _direction = Vector3.up;
        [SerializeField] private float _attachOffset = 0.0f;
        [SerializeField] private float _arrowLengthScale = 2.4f;
        [SerializeField] private float _arrowMinLength = 0.004f;
        [SerializeField] private float _arrowRadiusScale = 0.55f;
        [SerializeField] private float _arrowMinRadius = 0.0012f;

        private float _lastRadius;
        private Vector3 _lastScale = Vector3.one;
        private SphereCollider _cachedCollider;
        private bool _needsSync;
        private Vector3 _lastDirection = Vector3.up;
        private Transform _arrowRoot;
        private Transform _arrowShaft;
        private Transform _arrowHead;
        private static Mesh _arrowHeadMesh;
        private static Material _arrowMaterial;

        public string NodeId => _nodeId;
        public Vector3 Direction => _direction;
        public float AttachOffset => _attachOffset;
        public float Radius
        {
            get => _radius;
            set
            {
                if (Mathf.Abs(_radius - value) > 0.0000001f)
                {
                    _radius = Mathf.Max(0.0001f, value);
                    _needsSync = true;
                }
            }
        }

        public void Initialize(string nodeId, float radius)
        {
            _nodeId = nodeId;
            _radius = Mathf.Max(0.0001f, radius);
            _needsSync = true;
        }

        public void SetDirection(Vector3 direction, float attachOffset)
        {
            if (direction.sqrMagnitude < 0.000001f)
            {
                _direction = Vector3.up;
            }
            else
            {
                _direction = direction.normalized;
            }
            _attachOffset = Mathf.Max(0f, attachOffset);
            _needsSync = true;
        }

        public Vector3 GetAttachPosition()
        {
            var dir = _direction.sqrMagnitude < 0.000001f ? Vector3.up : _direction.normalized;
            return transform.localPosition + dir * _attachOffset;
        }

        private void Awake()
        {
            _cachedCollider = GetComponent<SphereCollider>();
            if (_cachedCollider == null)
            {
                _cachedCollider = gameObject.AddComponent<SphereCollider>();
            }
            _cachedCollider.isTrigger = true;
            _needsSync = true;
        }

        private void OnValidate()
        {
            _radius = Mathf.Max(0.0001f, _radius);
            if (_direction.sqrMagnitude < 0.000001f)
            {
                _direction = Vector3.up;
            }
            else
            {
                _direction = _direction.normalized;
            }
            _needsSync = true;
        }

        private void LateUpdate()
        {
            // Only sync when transform changes or when explicitly flagged
            var scale = transform.localScale;
            bool scaleChanged = (scale - _lastScale).sqrMagnitude > 0.0000001f;

            if (scaleChanged || _needsSync)
            {
                SyncScaleAndCollider();
                SyncArrow();
                _needsSync = false;
            }
        }

        private void SyncScaleAndCollider()
        {
            var scale = transform.localScale;
            float scaleRadius = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)) * 0.5f;
            bool radiusChanged = Mathf.Abs(_radius - _lastRadius) > 0.0000001f;

            if (radiusChanged)
            {
                float diameter = Mathf.Max(0.0002f, _radius * 2f);
                transform.localScale = new Vector3(diameter, diameter, diameter);
                scale = transform.localScale;
            }
            else
            {
                _radius = Mathf.Max(0.0001f, scaleRadius);
            }

            _lastScale = scale;
            _lastRadius = _radius;

            if (_cachedCollider != null)
            {
                _cachedCollider.radius = _radius;
            }
        }

        private void SyncArrow()
        {
            if (!_showArrow)
            {
                SetArrowActive(false);
                return;
            }

            EnsureArrow();
            if (_arrowRoot == null) return;

            var dir = _direction.sqrMagnitude < 0.000001f ? Vector3.up : _direction.normalized;
            _arrowRoot.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            _arrowRoot.localPosition = dir * _radius;

            float length = Mathf.Max(_arrowMinLength, _radius * _arrowLengthScale);
            float radius = Mathf.Max(_arrowMinRadius, _radius * _arrowRadiusScale);
            float shaftLength = length * 0.65f;

            if (_arrowShaft != null)
            {
                _arrowShaft.localScale = new Vector3(radius * 2f, shaftLength * 0.5f, radius * 2f);
                _arrowShaft.localPosition = new Vector3(0f, shaftLength * 0.5f, 0f);
            }

            if (_arrowHead != null)
            {
                _arrowHead.localScale = new Vector3(radius * 2.1f, radius * 3.0f, radius * 2.1f);
                _arrowHead.localPosition = new Vector3(0f, shaftLength, 0f);
            }

            if (_lastDirection != dir)
            {
                _lastDirection = dir;
            }

            // Counter-scale to avoid inheriting anchor scale.
            var scale = transform.localScale;
            var inv = new Vector3(
                scale.x == 0f ? 1f : 1f / scale.x,
                scale.y == 0f ? 1f : 1f / scale.y,
                scale.z == 0f ? 1f : 1f / scale.z);
            _arrowRoot.localScale = inv;
        }

        private void EnsureArrow()
        {
            if (_arrowRoot != null) return;
            _arrowRoot = new GameObject("AnchorArrow").transform;
            _arrowRoot.SetParent(transform, false);

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            shaft.transform.SetParent(_arrowRoot, false);
            RemoveCollider(shaft);
            _arrowShaft = shaft.transform;

            var head = new GameObject("Head");
            head.transform.SetParent(_arrowRoot, false);
            var filter = head.AddComponent<MeshFilter>();
            filter.sharedMesh = GetArrowHeadMesh();
            var renderer = head.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetArrowMaterial();
            var collider = head.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = true;
            _arrowHead = head.transform;

            var shaftRenderer = shaft.GetComponent<Renderer>();
            if (shaftRenderer != null)
            {
                shaftRenderer.sharedMaterial = GetArrowMaterial();
            }
        }

        private static void RemoveCollider(GameObject target)
        {
            if (target == null) return;
            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private void SetArrowActive(bool active)
        {
            if (_arrowRoot == null) return;
            if (_arrowRoot.gameObject.activeSelf != active)
            {
                _arrowRoot.gameObject.SetActive(active);
            }
        }

        private static Mesh GetArrowHeadMesh()
        {
            if (_arrowHeadMesh != null) return _arrowHeadMesh;
            var mesh = new Mesh { name = "AnchorArrowHead" };
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

        private static Material GetArrowMaterial()
        {
            if (_arrowMaterial != null) return _arrowMaterial;
            _arrowMaterial = CreateMetalMaterial("AnchorArrow", new Color(0.82f, 0.58f, 0.30f), 0.85f, 0.55f);
            return _arrowMaterial;
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

        private void OnDrawGizmos()
        {
            if (!_showGizmo) return;
            Gizmos.color = new Color(0f, 0.9f, 0.9f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
