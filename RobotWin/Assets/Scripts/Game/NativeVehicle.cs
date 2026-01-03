using System.Collections.Generic;
using UnityEngine;
using RobotTwin.Core;

namespace RobotTwin.Game
{
    public class NativeVehicle : MonoBehaviour
    {
        [SerializeField] private NativePhysicsBody _body;
        [SerializeField] private List<Transform> _wheelPoints = new List<Transform>();
        [SerializeField] private List<float> _wheelRadius = new List<float>();
        [SerializeField] private List<float> _suspensionRest = new List<float>();
        [SerializeField] private List<float> _suspensionSpring = new List<float>();
        [SerializeField] private List<float> _suspensionDamping = new List<float>();
        [SerializeField] private List<bool> _driven = new List<bool>();

        [Header("Tire Model (Pacejka)")]
        [SerializeField] private float _pacejkaB = 10f;
        [SerializeField] private float _pacejkaC = 1.9f;
        [SerializeField] private float _pacejkaD = 1.0f;
        [SerializeField] private float _pacejkaE = 0.97f;

        [Header("Aero")]
        [SerializeField] private float _dragCoefficient = 0.35f;
        [SerializeField] private float _downforce = 0f;

        public uint VehicleId { get; private set; }
        public int WheelCount => _wheelPoints.Count;

        private void Start()
        {
            if (_body == null || _body.BodyId == 0) return;
            if (_wheelPoints.Count == 0) return;

            int count = _wheelPoints.Count;
            var positions = new float[count * 3];
            var radius = new float[count];
            var rest = new float[count];
            var spring = new float[count];
            var damping = new float[count];
            var driven = new int[count];

            for (int i = 0; i < count; i++)
            {
                var local = _wheelPoints[i].localPosition;
                positions[i * 3 + 0] = local.x;
                positions[i * 3 + 1] = local.y;
                positions[i * 3 + 2] = local.z;
                radius[i] = i < _wheelRadius.Count ? _wheelRadius[i] : 0.03f;
                rest[i] = i < _suspensionRest.Count ? _suspensionRest[i] : 0.05f;
                spring[i] = i < _suspensionSpring.Count ? _suspensionSpring[i] : 1400f;
                damping[i] = i < _suspensionDamping.Count ? _suspensionDamping[i] : 120f;
                driven[i] = (i < _driven.Count && _driven[i]) ? 1 : 0;
            }

            VehicleId = NativeBridge.Physics_AddVehicle(
                _body.BodyId,
                count,
                positions,
                radius,
                rest,
                spring,
                damping,
                driven);

            if (VehicleId != 0)
            {
                NativeBridge.Physics_SetVehicleTireModel(VehicleId, _pacejkaB, _pacejkaC, _pacejkaD, _pacejkaE);
                NativeBridge.Physics_SetVehicleAero(VehicleId, _dragCoefficient, _downforce);
            }
        }

        public void SetWheelInput(int index, float steer, float driveTorque, float brakeTorque)
        {
            if (VehicleId == 0) return;
            NativeBridge.Physics_SetWheelInput(VehicleId, index, steer, driveTorque, brakeTorque);
        }
    }
}
