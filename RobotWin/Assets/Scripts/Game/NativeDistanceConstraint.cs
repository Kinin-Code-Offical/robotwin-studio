using UnityEngine;
using RobotTwin.Core;

namespace RobotTwin.Game
{
    public class NativeDistanceConstraint : MonoBehaviour
    {
        [SerializeField] private NativePhysicsBody _bodyA;
        [SerializeField] private NativePhysicsBody _bodyB;
        [SerializeField] private Vector3 _localAnchorA;
        [SerializeField] private Vector3 _localAnchorB;

        [Header("Constraint")]
        [SerializeField] private bool _autoRestLength = true;
        [SerializeField] private float _restLength = 1.0f;
        [SerializeField] private float _stiffness = 8000f;
        [SerializeField] private float _damping = 160f;
        [SerializeField] private float _maxForce = 30000f;
        [SerializeField] private bool _tensionOnly = false;

        public uint ConstraintId { get; private set; }

        private void Start()
        {
            if (_bodyA == null || _bodyB == null) return;
            if (_bodyA.BodyId == 0 || _bodyB.BodyId == 0) return;

            if (_autoRestLength)
            {
                Vector3 anchorA = _bodyA.transform.TransformPoint(_localAnchorA);
                Vector3 anchorB = _bodyB.transform.TransformPoint(_localAnchorB);
                _restLength = Vector3.Distance(anchorA, anchorB);
            }

            ConstraintId = NativeBridge.Physics_AddDistanceConstraint(
                _bodyA.BodyId,
                _bodyB.BodyId,
                _localAnchorA.x, _localAnchorA.y, _localAnchorA.z,
                _localAnchorB.x, _localAnchorB.y, _localAnchorB.z,
                _restLength, _stiffness, _damping, _maxForce,
                _tensionOnly ? 1 : 0);
        }
    }
}
