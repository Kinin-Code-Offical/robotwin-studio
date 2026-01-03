using UnityEngine;
using RobotTwin.Core;

namespace RobotTwin.Game
{
    public class NativeRaycastSensor : MonoBehaviour
    {
        [SerializeField] private float _maxDistance = 25f;
        [SerializeField] private bool _worldSpace = true;
        [SerializeField] private Vector3 _direction = Vector3.forward;

        public bool HasHit { get; private set; }
        public uint HitBodyId { get; private set; }
        public Vector3 HitPoint { get; private set; }
        public Vector3 HitNormal { get; private set; }
        public float HitDistance { get; private set; }

        private void FixedUpdate()
        {
            Vector3 origin = transform.position;
            Vector3 dir = _worldSpace ? _direction.normalized : transform.TransformDirection(_direction).normalized;

            if (NativeBridge.Physics_Raycast(origin.x, origin.y, origin.z,
                dir.x, dir.y, dir.z, _maxDistance, out var hit) == 0)
            {
                HasHit = false;
                return;
            }

            HasHit = true;
            HitBodyId = hit.body_id;
            HitPoint = new Vector3(hit.hit_x, hit.hit_y, hit.hit_z);
            HitNormal = new Vector3(hit.normal_x, hit.normal_y, hit.normal_z);
            HitDistance = hit.distance;
        }
    }
}
