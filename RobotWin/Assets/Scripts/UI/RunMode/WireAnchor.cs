using UnityEngine;

namespace RobotTwin.UI
{
    public class WireAnchor : MonoBehaviour
    {
        [SerializeField] private string _nodeId;
        [SerializeField] private float _radius = 0.006f;
        [SerializeField] private bool _showGizmo = true;

        private float _lastRadius;
        private Vector3 _lastScale = Vector3.one;

        public string NodeId => _nodeId;
        public float Radius
        {
            get => _radius;
            set
            {
                _radius = Mathf.Max(0.0001f, value);
                SyncScaleAndCollider();
            }
        }

        public void Initialize(string nodeId, float radius)
        {
            _nodeId = nodeId;
            _radius = Mathf.Max(0.0001f, radius);
            SyncScaleAndCollider();
        }

        private void Awake()
        {
            SyncScaleAndCollider();
        }

        private void OnValidate()
        {
            _radius = Mathf.Max(0.0001f, _radius);
            SyncScaleAndCollider();
        }

        private void LateUpdate()
        {
            SyncScaleAndCollider();
        }

        private void SyncScaleAndCollider()
        {
            var scale = transform.localScale;
            float scaleRadius = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)) * 0.5f;
            bool scaleChanged = (scale - _lastScale).sqrMagnitude > 0.0000001f;
            bool radiusChanged = Mathf.Abs(_radius - _lastRadius) > 0.0000001f;

            if (scaleChanged && !radiusChanged)
            {
                _radius = Mathf.Max(0.0001f, scaleRadius);
            }
            else if (radiusChanged && !scaleChanged)
            {
                float diameter = Mathf.Max(0.0002f, _radius * 2f);
                transform.localScale = new Vector3(diameter, diameter, diameter);
                scale = transform.localScale;
            }
            else if (scaleChanged && radiusChanged)
            {
                _radius = Mathf.Max(0.0001f, scaleRadius);
            }

            _lastScale = scale;
            _lastRadius = _radius;

            var collider = GetComponent<SphereCollider>();
            if (collider == null) collider = gameObject.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = _radius;
        }

        private void OnDrawGizmos()
        {
            if (!_showGizmo) return;
            Gizmos.color = new Color(0f, 0.9f, 0.9f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
