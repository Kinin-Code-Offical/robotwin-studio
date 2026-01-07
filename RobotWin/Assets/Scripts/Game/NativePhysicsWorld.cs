using System.Collections.Generic;
using UnityEngine;
using RobotTwin.Core;

namespace RobotTwin.Game
{
    public class NativePhysicsWorld : MonoBehaviour
    {
        public static NativePhysicsWorld Instance { get; private set; }

        [Header("Performance / Determinism")]
        [SerializeField] private bool _autoStart = true;
        [SerializeField] private float _baseDt = 0.001f;
        [SerializeField] private Vector3 _gravity = new Vector3(0f, -9.80665f, 0f);
        [SerializeField] private float _gravityJitter = 0.02f;
        [SerializeField] private float _timeJitter = 0.00005f;
        [SerializeField] private float _solverIterations = 12f;
        [SerializeField] private ulong _noiseSeed = 0xA31F2C9B1E45D7UL;
        [SerializeField] private float _contactSlop = 0.0005f;
        [SerializeField] private float _restitution = 0.2f;
        [SerializeField] private float _staticFriction = 0.8f;
        [SerializeField] private float _dynamicFriction = 0.6f;
        [Header("Environment")]
        [SerializeField] private float _airDensity = 1.225f;
        [SerializeField] private Vector3 _wind = Vector3.zero;
        [SerializeField] private float _ambientTempC = 20f;
        [SerializeField] private float _rainIntensity = 0f;
        [SerializeField] private float _thermalExchange = 0.08f;
        [SerializeField] private float _sleepLinearThreshold = 0.05f;
        [SerializeField] private float _sleepAngularThreshold = 0.05f;
        [SerializeField] private float _sleepTime = 0.5f;
        [Header("Stability")]
        [SerializeField] private bool _enableSubstepping = true;
        [SerializeField] private float _maxSubstepDt = 0.01f;
        [SerializeField] private int _maxSubsteps = 4;
        [SerializeField] private bool _recordDiagnostics = true;
        [Header("Presets")]
        [SerializeField] private bool _usePreset = true;
        [SerializeField] private PhysicsPreset _preset;

        private readonly Dictionary<uint, NativePhysicsBody> _bodyById = new Dictionary<uint, NativePhysicsBody>();
        private readonly Dictionary<NativePhysicsBody, uint> _idByBody = new Dictionary<NativePhysicsBody, uint>();
        private bool _running;
        private bool _externalStepping;
        private float _lastStepMs;
        private float _lastStepDt;
        private int _lastStepSubsteps;
        private long _stepCount;
        private bool _effectiveSubstepping;
        private float _effectiveMaxSubstepDt;
        private int _effectiveMaxSubsteps;
        private float _effectiveAmbientTempC;

        public bool IsRunning => _running;
        public int BodyCount => _bodyById.Count;
        public bool ExternalStepping => _externalStepping;
        public float AmbientTempC => _effectiveAmbientTempC;
        public Vector3 Wind => _wind;
        public float LastStepMs => _lastStepMs;
        public float LastStepDt => _lastStepDt;
        public int LastStepSubsteps => _lastStepSubsteps;
        public long StepCount => _stepCount;
        public PhysicsPreset ActivePreset => _preset;
        public bool UsingPreset => _usePreset && _preset != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (Instance != null) return;
            var go = new GameObject("NativePhysicsWorld");
            go.AddComponent<NativePhysicsWorld>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _effectiveAmbientTempC = _ambientTempC;
            if (_autoStart)
            {
                Initialize();
            }
        }

        private void OnDestroy()
        {
            Shutdown();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnValidate()
        {
            if (_running)
            {
                ApplyConfig();
            }
        }

        public void Initialize()
        {
            if (_running) return;
            NativeBridge.Physics_CreateWorld();
            ApplyConfig();
            _running = true;
        }

        public void Shutdown()
        {
            if (!_running) return;
            NativeBridge.Physics_DestroyWorld();
            _running = false;
            _bodyById.Clear();
            _idByBody.Clear();
        }

        public void SetExternalStepping(bool enabled)
        {
            _externalStepping = enabled;
        }

        public void StepExternal(float dtSeconds)
        {
            if (!_running) return;
            if (dtSeconds <= 0f) return;
            StepInternal(dtSeconds);
        }

        public void ApplyConfig()
        {
            var preset = _usePreset ? _preset : null;
            float baseDt = preset != null ? preset.BaseDt : _baseDt;
            Vector3 gravity = preset != null ? preset.Gravity : _gravity;
            float gravityJitter = preset != null ? preset.GravityJitter : _gravityJitter;
            float timeJitter = preset != null ? preset.TimeJitter : _timeJitter;
            float solverIterations = preset != null ? preset.SolverIterations : _solverIterations;
            ulong noiseSeed = preset != null ? preset.NoiseSeed : _noiseSeed;
            float contactSlop = preset != null ? preset.ContactSlop : _contactSlop;
            float restitution = preset != null ? preset.Restitution : _restitution;
            float staticFriction = preset != null ? preset.StaticFriction : _staticFriction;
            float dynamicFriction = preset != null ? preset.DynamicFriction : _dynamicFriction;
            float airDensity = preset != null ? preset.AirDensity : _airDensity;
            Vector3 wind = preset != null ? preset.Wind : _wind;
            float ambientTemp = preset != null ? preset.AmbientTempC : _ambientTempC;
            float rainIntensity = preset != null ? preset.RainIntensity : _rainIntensity;
            float thermalExchange = preset != null ? preset.ThermalExchange : _thermalExchange;
            float sleepLinear = preset != null ? preset.SleepLinearThreshold : _sleepLinearThreshold;
            float sleepAngular = preset != null ? preset.SleepAngularThreshold : _sleepAngularThreshold;
            float sleepTime = preset != null ? preset.SleepTime : _sleepTime;

            _effectiveSubstepping = preset != null ? preset.EnableSubstepping : _enableSubstepping;
            _effectiveMaxSubstepDt = preset != null ? preset.MaxSubstepDt : _maxSubstepDt;
            _effectiveMaxSubsteps = preset != null ? preset.MaxSubsteps : _maxSubsteps;
            _effectiveAmbientTempC = ambientTemp;

            var cfg = new NativeBridge.PhysicsConfig
            {
                base_dt = baseDt,
                gravity_x = gravity.x,
                gravity_y = gravity.y,
                gravity_z = gravity.z,
                gravity_jitter = gravityJitter,
                time_jitter = timeJitter,
                solver_iterations = solverIterations,
                noise_seed = noiseSeed,
                contact_slop = contactSlop,
                restitution = restitution,
                static_friction = staticFriction,
                dynamic_friction = dynamicFriction,
                air_density = airDensity,
                wind_x = wind.x,
                wind_y = wind.y,
                wind_z = wind.z,
                ambient_temp_c = ambientTemp,
                rain_intensity = rainIntensity,
                thermal_exchange = thermalExchange,
                sleep_linear_threshold = sleepLinear,
                sleep_angular_threshold = sleepAngular,
                sleep_time = sleepTime
            };
            NativeBridge.Physics_SetConfig(ref cfg);
        }

        public void SetPreset(PhysicsPreset preset, bool apply = true)
        {
            _preset = preset;
            if (apply && _running)
            {
                ApplyConfig();
            }
        }

        public void RegisterBody(NativePhysicsBody body)
        {
            if (body == null) return;
            if (_idByBody.ContainsKey(body)) return;
            if (!_running)
            {
                Initialize();
            }

            var t = body.transform;
            var rb = new NativeBridge.RigidBody
            {
                id = 0,
                mass = body.Mass,
                pos_x = t.position.x,
                pos_y = t.position.y,
                pos_z = t.position.z,
                vel_x = body.InitialVelocity.x,
                vel_y = body.InitialVelocity.y,
                vel_z = body.InitialVelocity.z,
                rot_w = t.rotation.w,
                rot_x = t.rotation.x,
                rot_y = t.rotation.y,
                rot_z = t.rotation.z,
                ang_x = 0f,
                ang_y = 0f,
                ang_z = 0f,
                linear_damping = body.LinearDamping,
                angular_damping = body.AngularDamping,
                drag_coefficient = body.DragCoefficient,
                cross_section_area = body.CrossSectionArea,
                surface_area = body.SurfaceArea,
                temperature_c = _effectiveAmbientTempC,
                material_strength = body.MaterialStrength,
                fracture_toughness = body.FractureToughness,
                shape_type = (int)body.Shape,
                radius = body.Radius,
                half_x = body.HalfExtents.x,
                half_y = body.HalfExtents.y,
                half_z = body.HalfExtents.z,
                friction = body.Friction,
                restitution = body.Restitution,
                damage = 0f,
                is_broken = 0,
                is_static = body.IsStatic ? 1 : 0
            };

            uint id = NativeBridge.Physics_AddBody(ref rb);
            if (id == 0) return;

            body.SetBodyId(id);
            _idByBody[body] = id;
            _bodyById[id] = body;
        }

        public void UpdateBody(NativePhysicsBody body)
        {
            if (body == null) return;
            if (!_running) return;
            if (!_idByBody.TryGetValue(body, out var id) || id == 0) return;

            var t = body.transform;
            var rb = new NativeBridge.RigidBody
            {
                id = id,
                mass = body.Mass,
                pos_x = t.position.x,
                pos_y = t.position.y,
                pos_z = t.position.z,
                vel_x = body.Velocity.x,
                vel_y = body.Velocity.y,
                vel_z = body.Velocity.z,
                rot_w = t.rotation.w,
                rot_x = t.rotation.x,
                rot_y = t.rotation.y,
                rot_z = t.rotation.z,
                ang_x = body.AngularVelocity.x,
                ang_y = body.AngularVelocity.y,
                ang_z = body.AngularVelocity.z,
                linear_damping = body.LinearDamping,
                angular_damping = body.AngularDamping,
                drag_coefficient = body.DragCoefficient,
                cross_section_area = body.CrossSectionArea,
                surface_area = body.SurfaceArea,
                temperature_c = body.TemperatureC > 0f ? body.TemperatureC : _effectiveAmbientTempC,
                material_strength = body.MaterialStrength,
                fracture_toughness = body.FractureToughness,
                shape_type = (int)body.Shape,
                radius = body.Radius,
                half_x = body.HalfExtents.x,
                half_y = body.HalfExtents.y,
                half_z = body.HalfExtents.z,
                friction = body.Friction,
                restitution = body.Restitution,
                damage = body.Damage,
                is_broken = body.IsBroken ? 1 : 0,
                is_static = body.IsStatic ? 1 : 0
            };
            NativeBridge.Physics_SetBody(id, ref rb);
        }

        public void UnregisterBody(NativePhysicsBody body)
        {
            if (body == null) return;
            if (!_idByBody.TryGetValue(body, out var id)) return;
            _idByBody.Remove(body);
            _bodyById.Remove(id);
        }

        private void FixedUpdate()
        {
            if (!_running || _externalStepping) return;
            StepInternal(Time.fixedDeltaTime);
        }

        private void StepInternal(float dtSeconds)
        {
            if (dtSeconds <= 0f) return;
            int maxSubsteps = Mathf.Max(1, _effectiveMaxSubsteps);
            float dt = dtSeconds;
            int substeps = 1;
            if (_effectiveSubstepping && _effectiveMaxSubstepDt > 0f && dt > _effectiveMaxSubstepDt)
            {
                substeps = Mathf.Clamp(Mathf.CeilToInt(dt / _effectiveMaxSubstepDt), 1, maxSubsteps);
            }
            float stepDt = dt / substeps;
            if (stepDt <= 0f) return;

            float start = _recordDiagnostics ? Time.realtimeSinceStartup : 0f;
            for (int i = 0; i < substeps; i++)
            {
                NativeBridge.Physics_Step(stepDt);
            }
            if (_recordDiagnostics)
            {
                _lastStepMs = (Time.realtimeSinceStartup - start) * 1000f;
                _lastStepDt = dt;
                _lastStepSubsteps = substeps;
                _stepCount++;
            }
            foreach (var kvp in _bodyById)
            {
                var id = kvp.Key;
                var body = kvp.Value;
                if (body == null) continue;

                if (NativeBridge.Physics_GetBody(id, out var rb) == 0) continue;
                var t = body.transform;
                t.position = new Vector3(rb.pos_x, rb.pos_y, rb.pos_z);
                t.rotation = new Quaternion(rb.rot_x, rb.rot_y, rb.rot_z, rb.rot_w);
                body.Velocity = new Vector3(rb.vel_x, rb.vel_y, rb.vel_z);
                body.AngularVelocity = new Vector3(rb.ang_x, rb.ang_y, rb.ang_z);
                body.TemperatureC = rb.temperature_c;
                body.Damage = rb.damage;
                body.IsBroken = rb.is_broken != 0;
            }
        }
    }
}
