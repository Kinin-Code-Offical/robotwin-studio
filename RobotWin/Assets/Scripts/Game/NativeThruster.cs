using UnityEngine;
using RobotTwin.Core;

namespace RobotTwin.Game
{
    public class NativeThruster : MonoBehaviour
    {
        [SerializeField] private NativePhysicsBody _body;
        [SerializeField] private Transform _nozzle;

        [Header("Force")]
        [SerializeField] private float _maxForce = 25f;
        [SerializeField] private float _throttle;
        [SerializeField] private bool _worldSpace;

        [Header("Input")]
        [SerializeField] private bool _useInputAxis = true;
        [SerializeField] private string _throttleAxis = "Jump";
        [SerializeField] private KeyCode _throttleKey = KeyCode.Space;

        private void Reset()
        {
            if (_body == null)
            {
                _body = GetComponentInParent<NativePhysicsBody>();
            }
        }

        private void FixedUpdate()
        {
            if (_body == null || _body.BodyId == 0) return;
            float input = GetThrottleInput();
            _throttle = Mathf.Clamp01(Mathf.Max(_throttle, input));

            float forceValue = _maxForce * _throttle;
            if (forceValue <= 0f) return;

            Vector3 dir = _worldSpace ? Vector3.up : transform.TransformDirection(Vector3.up);
            Vector3 force = dir.normalized * forceValue;
            Vector3 point = _nozzle != null ? _nozzle.position : transform.position;

            NativeBridge.Physics_ApplyForceAtPoint(_body.BodyId, force.x, force.y, force.z, point.x, point.y, point.z);
        }

        private float GetThrottleInput()
        {
            if (_useInputAxis)
            {
                return Mathf.Clamp01(Input.GetAxisRaw(_throttleAxis));
            }
            return Input.GetKey(_throttleKey) ? 1f : 0f;
        }

        public void SetThrottle(float throttle)
        {
            _throttle = Mathf.Clamp01(throttle);
        }
    }
}
