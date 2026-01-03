using UnityEngine;
using RobotTwin.Core;

namespace RobotTwin.Game
{
    public class NativeServoMotor : MonoBehaviour
    {
        [SerializeField] private NativePhysicsBody _body;

        [Header("Axis")]
        [SerializeField] private Vector3 _localAxis = Vector3.up;
        [SerializeField] private float _targetAngleDeg;

        [Header("Control")]
        [SerializeField] private float _stiffness = 60f;
        [SerializeField] private float _damping = 6f;
        [SerializeField] private float _maxTorque = 120f;

        private Quaternion _baseRotation;
        private Vector3 _axisWorld;

        private void Start()
        {
            if (_body == null)
            {
                _body = GetComponent<NativePhysicsBody>();
            }
            if (_body != null)
            {
                _baseRotation = _body.transform.rotation;
            }
            else
            {
                _baseRotation = transform.rotation;
            }
        }

        private void FixedUpdate()
        {
            if (_body == null || _body.BodyId == 0) return;

            _axisWorld = _body.transform.TransformDirection(_localAxis.normalized);
            float currentAngle = GetSignedAngleFromBase();
            float errorDeg = Mathf.DeltaAngle(currentAngle, _targetAngleDeg);
            float errorRad = errorDeg * Mathf.Deg2Rad;
            float angularVel = Vector3.Dot(_body.AngularVelocity, _axisWorld);

            float torque = errorRad * _stiffness - angularVel * _damping;
            torque = Mathf.Clamp(torque, -_maxTorque, _maxTorque);
            Vector3 torqueVec = _axisWorld * torque;

            NativeBridge.Physics_ApplyTorque(_body.BodyId, torqueVec.x, torqueVec.y, torqueVec.z);
        }

        private float GetSignedAngleFromBase()
        {
            Quaternion current = _body.transform.rotation;
            Quaternion delta = Quaternion.Inverse(_baseRotation) * current;
            delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (angleDeg > 180f) angleDeg -= 360f;
            float sign = Mathf.Sign(Vector3.Dot(axis, _axisWorld));
            if (Mathf.Approximately(sign, 0f)) sign = 1f;
            return angleDeg * sign;
        }

        public void SetTargetAngle(float angleDeg)
        {
            _targetAngleDeg = angleDeg;
        }
    }
}
