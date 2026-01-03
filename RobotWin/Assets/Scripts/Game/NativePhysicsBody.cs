using UnityEngine;

namespace RobotTwin.Game
{
    public class NativePhysicsBody : MonoBehaviour
    {
        public enum ShapeType
        {
            Sphere = 0,
            Box = 1
        }

        [SerializeField] private float _mass = 1.0f;
        [SerializeField] private bool _isStatic;
        [SerializeField] private Vector3 _initialVelocity;
        [SerializeField] private float _linearDamping = 0.01f;
        [SerializeField] private float _angularDamping = 0.02f;
        [Header("Collision Shape")]
        [SerializeField] private ShapeType _shape = ShapeType.Box;
        [SerializeField] private bool _autoFromCollider = true;
        [SerializeField] private float _radius = 0.5f;
        [SerializeField] private Vector3 _halfExtents = Vector3.one * 0.5f;
        [SerializeField] private float _friction = 0.8f;
        [SerializeField] private float _restitution = 0.2f;
        [Header("Aero + Materials")]
        [SerializeField] private float _dragCoefficient = 0.9f;
        [SerializeField] private float _crossSectionArea = 0.02f;
        [SerializeField] private float _surfaceArea = 0.2f;
        [SerializeField] private float _materialStrength = 25000f;
        [SerializeField] private float _fractureToughness = 0.6f;

        public uint BodyId { get; private set; }
        public float Mass => _mass;
        public bool IsStatic => _isStatic;
        public Vector3 InitialVelocity => _initialVelocity;
        public float LinearDamping => _linearDamping;
        public float AngularDamping => _angularDamping;
        public ShapeType Shape => _shape;
        public float Radius => _radius;
        public Vector3 HalfExtents => _halfExtents;
        public float Friction => _friction;
        public float Restitution => _restitution;
        public float DragCoefficient => _dragCoefficient;
        public float CrossSectionArea => _crossSectionArea;
        public float SurfaceArea => _surfaceArea;
        public float MaterialStrength => _materialStrength;
        public float FractureToughness => _fractureToughness;

        public float TemperatureC { get; internal set; }
        public float Damage { get; internal set; }
        public bool IsBroken { get; internal set; }
        public Vector3 Velocity { get; internal set; }
        public Vector3 AngularVelocity { get; internal set; }

        private void OnEnable()
        {
            if (_autoFromCollider)
            {
                SyncFromUnityCollider();
            }
            if (NativePhysicsWorld.Instance != null)
            {
                NativePhysicsWorld.Instance.RegisterBody(this);
            }
        }

        private void OnDisable()
        {
            if (NativePhysicsWorld.Instance != null)
            {
                NativePhysicsWorld.Instance.UnregisterBody(this);
            }
        }

        internal void SetBodyId(uint id)
        {
            BodyId = id;
        }

        private void SyncFromUnityCollider()
        {
            if (TryGetComponent<SphereCollider>(out var sphere))
            {
                _shape = ShapeType.Sphere;
                float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
                _radius = Mathf.Max(0.001f, sphere.radius * scale);
                float area = Mathf.PI * _radius * _radius;
                _crossSectionArea = area;
                _surfaceArea = 4f * Mathf.PI * _radius * _radius;
                return;
            }

            if (TryGetComponent<BoxCollider>(out var box))
            {
                _shape = ShapeType.Box;
                Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
                _halfExtents = new Vector3(
                    Mathf.Max(0.001f, size.x * 0.5f),
                    Mathf.Max(0.001f, size.y * 0.5f),
                    Mathf.Max(0.001f, size.z * 0.5f));
                float x = _halfExtents.x * 2f;
                float y = _halfExtents.y * 2f;
                float z = _halfExtents.z * 2f;
                float xy = x * y;
                float xz = x * z;
                float yz = y * z;
                _crossSectionArea = Mathf.Max(xy, Mathf.Max(xz, yz));
                _surfaceArea = 2f * (xy + xz + yz);
            }
        }
    }
}
