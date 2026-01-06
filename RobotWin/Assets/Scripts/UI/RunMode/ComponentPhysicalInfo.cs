using System;
using UnityEngine;

namespace RobotTwin.UI
{
    public sealed class ComponentPhysicalInfo : MonoBehaviour
    {
        [Serializable]
        public struct PartInfo
        {
            public string Name;
            public string PhysicalMaterialId;
            public float DensityKgPerM3;
            public float MassKg;
            public float VolumeM3;
            public float Friction;
            public float Elasticity;
            public float Strength;
        }

        [SerializeField] private float _totalMassKg;
        [SerializeField] private float _effectiveFriction;
        [SerializeField] private float _effectiveElasticity;
        [SerializeField] private float _effectiveStrength;
        [SerializeField] private PartInfo[] _parts;

        public float TotalMassKg => _totalMassKg;
        public float EffectiveFriction => _effectiveFriction;
        public float EffectiveElasticity => _effectiveElasticity;
        public float EffectiveStrength => _effectiveStrength;
        public PartInfo[] Parts => _parts;

        public void Set(float totalMassKg, float effectiveFriction, float effectiveElasticity, float effectiveStrength, PartInfo[] parts)
        {
            _totalMassKg = totalMassKg;
            _effectiveFriction = effectiveFriction;
            _effectiveElasticity = effectiveElasticity;
            _effectiveStrength = effectiveStrength;
            _parts = parts ?? Array.Empty<PartInfo>();
        }
    }
}
