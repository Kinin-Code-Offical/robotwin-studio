using UnityEngine;
using RobotTwin.Core;

namespace RobotTwin.Game
{
    public class NativeCableConstraint : MonoBehaviour
    {
        [SerializeField] private NativePhysicsBody _bodyA;
        [SerializeField] private NativePhysicsBody _bodyB;
        [SerializeField] private Transform _anchorA;
        [SerializeField] private Transform _anchorB;
        [SerializeField] private Vector3 _localAnchorA;
        [SerializeField] private Vector3 _localAnchorB;

        [Header("Cable")]
        [SerializeField] private bool _autoRestLength = true;
        [SerializeField] private float _restLength = 0.5f;
        [SerializeField] private float _spring = 6000f;
        [SerializeField] private float _damping = 120f;
        [SerializeField] private float _maxForce = 20000f;

        private void Start()
        {
            if (_autoRestLength)
            {
                _restLength = Vector3.Distance(GetAnchorWorldA(), GetAnchorWorldB());
            }
        }

        private void FixedUpdate()
        {
            if (_bodyA == null || _bodyA.BodyId == 0) return;
            bool hasBodyB = _bodyB != null && _bodyB.BodyId != 0;
            if (!hasBodyB && _anchorB == null) return;

            Vector3 anchorA = GetAnchorWorldA();
            Vector3 anchorB = GetAnchorWorldB();
            Vector3 delta = anchorB - anchorA;
            float length = delta.magnitude;
            if (length <= _restLength || length <= 1e-5f) return;

            Vector3 dir = delta / length;
            float stretch = length - _restLength;

            float relVel = 0f;
            Vector3 velA = GetPointVelocity(_bodyA, anchorA);
            relVel -= Vector3.Dot(velA, dir);
            if (hasBodyB)
            {
                Vector3 velB = GetPointVelocity(_bodyB, anchorB);
                relVel += Vector3.Dot(velB, dir);
            }

            float tension = stretch * _spring + relVel * _damping;
            if (tension <= 0f) return;
            if (tension > _maxForce) tension = _maxForce;

            Vector3 force = dir * tension;
            NativeBridge.Physics_ApplyForceAtPoint(_bodyA.BodyId, force.x, force.y, force.z, anchorA.x, anchorA.y, anchorA.z);

            if (hasBodyB)
            {
                NativeBridge.Physics_ApplyForceAtPoint(_bodyB.BodyId, -force.x, -force.y, -force.z, anchorB.x, anchorB.y, anchorB.z);
            }
        }

        private Vector3 GetAnchorWorldA()
        {
            if (_anchorA != null) return _anchorA.position;
            if (_bodyA != null) return _bodyA.transform.TransformPoint(_localAnchorA);
            return transform.position;
        }

        private Vector3 GetAnchorWorldB()
        {
            if (_anchorB != null) return _anchorB.position;
            if (_bodyB != null) return _bodyB.transform.TransformPoint(_localAnchorB);
            return transform.position;
        }

        private static Vector3 GetPointVelocity(NativePhysicsBody body, Vector3 worldPoint)
        {
            if (body == null) return Vector3.zero;
            Vector3 r = worldPoint - body.transform.position;
            return body.Velocity + Vector3.Cross(body.AngularVelocity, r);
        }
    }
}
