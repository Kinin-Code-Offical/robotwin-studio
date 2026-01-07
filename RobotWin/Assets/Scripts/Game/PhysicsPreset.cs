using UnityEngine;

namespace RobotTwin.Game
{
    [CreateAssetMenu(menuName = "RobotWin/Physics/Preset", fileName = "PhysicsPreset")]
    public class PhysicsPreset : ScriptableObject
    {
        [Header("Performance / Determinism")]
        public float BaseDt = 0.001f;
        public Vector3 Gravity = new Vector3(0f, -9.80665f, 0f);
        public float GravityJitter = 0.02f;
        public float TimeJitter = 0.00005f;
        public float SolverIterations = 12f;
        public ulong NoiseSeed = 0xA31F2C9B1E45D7UL;
        public float ContactSlop = 0.0005f;
        public float Restitution = 0.2f;
        public float StaticFriction = 0.8f;
        public float DynamicFriction = 0.6f;

        [Header("Environment")]
        public float AirDensity = 1.225f;
        public Vector3 Wind = Vector3.zero;
        public float AmbientTempC = 20f;
        public float RainIntensity = 0f;
        public float ThermalExchange = 0.08f;
        public float SleepLinearThreshold = 0.05f;
        public float SleepAngularThreshold = 0.05f;
        public float SleepTime = 0.5f;

        [Header("Stability")]
        public bool EnableSubstepping = true;
        public float MaxSubstepDt = 0.01f;
        public int MaxSubsteps = 4;
    }
}
