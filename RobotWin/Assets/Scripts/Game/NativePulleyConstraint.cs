using UnityEngine;
using RobotTwin.Core;

namespace RobotTwin.Game
{
    public class NativePulleyConstraint : MonoBehaviour
    {
        [SerializeField] private NativePhysicsBody _bodyA;
        [SerializeField] private NativePhysicsBody _bodyB;
        [SerializeField] private Transform _pulley;
        [SerializeField] private Vector3 _pulleyOffset;
        [SerializeField] private Vector3 _localAnchorA;
        [SerializeField] private Vector3 _localAnchorB;

        [Header("Pulley")]
        [SerializeField] private bool _autoRestLength = true;
        [SerializeField] private float _restLength = 1.0f;
        [SerializeField] private float _mechanicalRatio = 1.0f;
        [SerializeField] private float _spring = 8000f;
        [SerializeField] private float _damping = 160f;
        [SerializeField] private float _maxForce = 30000f;

        private void Start()
        {
            if (_autoRestLength)
            {
                _restLength = ComputeEffectiveLength();
            }
        }

        private void FixedUpdate()
        {
            if (_bodyA == null || _bodyA.BodyId == 0) return;
            if (_bodyB == null || _bodyB.BodyId == 0) return;

            Vector3 pulleyPos = GetPulleyWorld();
            Vector3 anchorA = _bodyA.transform.TransformPoint(_localAnchorA);
            Vector3 anchorB = _bodyB.transform.TransformPoint(_localAnchorB);

            Vector3 deltaA = pulleyPos - anchorA;
            Vector3 deltaB = pulleyPos - anchorB;
            float lenA = deltaA.magnitude;
            float lenB = deltaB.magnitude;
            if (lenA <= 1e-5f || lenB <= 1e-5f) return;

            float effectiveLength = lenA + lenB * Mathf.Max(0.01f, _mechanicalRatio);
            float stretch = effectiveLength - _restLength;
            if (stretch <= 0f) return;

            Vector3 dirA = deltaA / lenA;
            Vector3 dirB = deltaB / lenB;

            float relVel = 0f;
            relVel += Vector3.Dot(GetPointVelocity(_bodyA, anchorA), dirA);
            relVel += Vector3.Dot(GetPointVelocity(_bodyB, anchorB), dirB) * _mechanicalRatio;

            float tension = stretch * _spring + relVel * _damping;
            if (tension <= 0f) return;
            if (tension > _maxForce) tension = _maxForce;

            Vector3 forceA = dirA * tension;
            Vector3 forceB = dirB * tension * _mechanicalRatio;

            NativeBridge.Physics_ApplyForceAtPoint(_bodyA.BodyId, forceA.x, forceA.y, forceA.z, anchorA.x, anchorA.y, anchorA.z);
            NativeBridge.Physics_ApplyForceAtPoint(_bodyB.BodyId, forceB.x, forceB.y, forceB.z, anchorB.x, anchorB.y, anchorB.z);
        }

        private float ComputeEffectiveLength()
        {
            Vector3 pulleyPos = GetPulleyWorld();
            Vector3 anchorA = _bodyA != null ? _bodyA.transform.TransformPoint(_localAnchorA) : transform.position;
            Vector3 anchorB = _bodyB != null ? _bodyB.transform.TransformPoint(_localAnchorB) : transform.position;
            float lenA = Vector3.Distance(anchorA, pulleyPos);
            float lenB = Vector3.Distance(anchorB, pulleyPos);
            return lenA + lenB * Mathf.Max(0.01f, _mechanicalRatio);
        }

        private Vector3 GetPulleyWorld()
        {
            if (_pulley != null) return _pulley.position;
            return transform.TransformPoint(_pulleyOffset);
        }

        private static Vector3 GetPointVelocity(NativePhysicsBody body, Vector3 worldPoint)
        {
            if (body == null) return Vector3.zero;
            Vector3 r = worldPoint - body.transform.position;
            return body.Velocity + Vector3.Cross(body.AngularVelocity, r);
        }
    }
}
