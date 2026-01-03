using UnityEngine;

namespace RobotTwin.Game
{
    public class NativePhysicsBody : MonoBehaviour
    {
        [SerializeField] private float _mass = 1.0f;
        [SerializeField] private bool _isStatic;
        [SerializeField] private Vector3 _initialVelocity;
        [SerializeField] private float _linearDamping = 0.01f;
        [SerializeField] private float _angularDamping = 0.02f;
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
    }
}
