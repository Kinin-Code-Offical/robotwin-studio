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

        private readonly Dictionary<uint, NativePhysicsBody> _bodyById = new Dictionary<uint, NativePhysicsBody>();
        private readonly Dictionary<NativePhysicsBody, uint> _idByBody = new Dictionary<NativePhysicsBody, uint>();
        private bool _running;

        public bool IsRunning => _running;
        public int BodyCount => _bodyById.Count;

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

        public void ApplyConfig()
        {
            var cfg = new NativeBridge.PhysicsConfig
            {
                base_dt = _baseDt,
                gravity_x = _gravity.x,
                gravity_y = _gravity.y,
                gravity_z = _gravity.z,
                gravity_jitter = _gravityJitter,
                time_jitter = _timeJitter,
                solver_iterations = _solverIterations,
                noise_seed = _noiseSeed,
                contact_slop = _contactSlop,
                restitution = _restitution,
                static_friction = _staticFriction,
                dynamic_friction = _dynamicFriction,
                air_density = _airDensity,
                wind_x = _wind.x,
                wind_y = _wind.y,
                wind_z = _wind.z,
                ambient_temp_c = _ambientTempC,
                rain_intensity = _rainIntensity,
                thermal_exchange = _thermalExchange
            };
            NativeBridge.Physics_SetConfig(ref cfg);
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
                temperature_c = _ambientTempC,
                material_strength = body.MaterialStrength,
                fracture_toughness = body.FractureToughness,
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

        public void UnregisterBody(NativePhysicsBody body)
        {
            if (body == null) return;
            if (!_idByBody.TryGetValue(body, out var id)) return;
            _idByBody.Remove(body);
            _bodyById.Remove(id);
        }

        private void FixedUpdate()
        {
            if (!_running) return;
            NativeBridge.Physics_Step(Time.fixedDeltaTime);

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
