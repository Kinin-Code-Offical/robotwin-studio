using UnityEngine;
using System.Collections.Generic;

namespace RobotTwin.UI.WorldEditor
{
    /// <summary>
    /// World Environment Manager - Runtime environment control
    /// Applies world configuration to the scene: room design, WiFi modems, boundaries, weather effects
    /// </summary>
    public class WorldEnvironmentManager : MonoBehaviour
    {
        [Header("World Configuration")]
        [SerializeField] private WorldConfiguration _activeConfig;

        [Header("Room Design")]
        [SerializeField] private GameObject _floorPrefab;
        [SerializeField] private GameObject _wallPrefab;
        [SerializeField] private GameObject _ceilingPrefab;
        [SerializeField] private Material _floorMaterial;
        [SerializeField] private Material _wallMaterial;

        [Header("WiFi Modems")]
        [SerializeField] private GameObject _wifiModemPrefab;
        [SerializeField] private List<GameObject> _activeModems = new List<GameObject>();

        [Header("World Boundaries")]
        [SerializeField] private GameObject _boundaryWallPrefab;
        [SerializeField] private List<GameObject> _boundaryWalls = new List<GameObject>();

        [Header("Terrain")]
        [SerializeField] private bool _terrainEnabled;
#if UNITY_TERRAIN
        [SerializeField] private Terrain _terrain;
        [SerializeField] private TerrainCollider _terrainCollider;
#endif

        [Header("Player")]
        [SerializeField] private GameObject _playerPrefab;
        [SerializeField] private GameObject _playerInstance;

        [Header("Weather & Environment")]
#if UNITY_PARTICLE_SYSTEM
        [SerializeField] private ParticleSystem _rainParticles;
        [SerializeField] private ParticleSystem _snowParticles;
#endif
#if UNITY_WIND
        [SerializeField] private WindZone _windZone;
#endif
        [SerializeField] private Light _directionalLight;

        private GameObject _roomContainer;
        private GameObject _modemContainer;
        private GameObject _boundaryContainer;

        void Start()
        {
            // Create containers for organization
            _roomContainer = new GameObject("Room_Container");
            _roomContainer.transform.SetParent(transform);

            _modemContainer = new GameObject("Modem_Container");
            _modemContainer.transform.SetParent(transform);

            _boundaryContainer = new GameObject("Boundary_Container");
            _boundaryContainer.transform.SetParent(transform);

            // Apply default configuration if available
            if (_activeConfig != null)
            {
                ApplyConfiguration(_activeConfig);
            }
        }

        public void ApplyConfiguration(WorldConfiguration config)
        {
            _activeConfig = config;

            ApplyPhysicsSettings(config);
            ApplyEnvironmentSettings(config);
            BuildRoomDesign(config);
            BuildTerrain(config);
            SpawnWiFiModems(config);
            CreateWorldBoundaries(config);
            ApplyAudioSettings(config);
            EnsurePlayer(config);

            Debug.Log($"[WorldEnvironmentManager] Configuration applied: {config.RoomType} room, {config.Wind} wind");
        }

        private void ApplyPhysicsSettings(WorldConfiguration config)
        {
            Physics.gravity = config.EnableGravity ? config.Gravity : Vector3.zero;
            Time.timeScale = config.TimeScale;
            Time.fixedDeltaTime = config.FixedDeltaTime;

            // Apply physics multipliers (requires custom materials or global settings)
            // This is a placeholder - actual implementation would need material modification
            Debug.Log($"[WorldEnvironmentManager] Physics: Gravity={config.Gravity.y}, TimeScale={config.TimeScale}");
        }

        private void ApplyEnvironmentSettings(WorldConfiguration config)
        {
#if UNITY_WIND
            // Apply wind
            if (_windZone != null)
            {
                _windZone.windMain = config.Wind.magnitude;
                _windZone.transform.forward = config.Wind.normalized;
            }
#endif

#if UNITY_PARTICLE_SYSTEM
            // Apply weather
            if (config.EnableWeather)
            {
                switch (config.WeatherType)
                {
                    case WeatherType.Rain:
                        if (_rainParticles != null) _rainParticles.Play();
                        if (_snowParticles != null) _snowParticles.Stop();
                        break;
                    case WeatherType.Snow:
                        if (_snowParticles != null) _snowParticles.Play();
                        if (_rainParticles != null) _rainParticles.Stop();
                        break;
                    default:
                        if (_rainParticles != null) _rainParticles.Stop();
                        if (_snowParticles != null) _snowParticles.Stop();
                        break;
                }
            }
            else
            {
                if (_rainParticles != null) _rainParticles.Stop();
                if (_snowParticles != null) _snowParticles.Stop();
            }
#endif

            // Apply light intensity
            if (_directionalLight != null)
            {
                _directionalLight.intensity = config.LightIntensity;
            }

            Debug.Log($"[WorldEnvironmentManager] Environment: Wind={config.Wind}, Weather={config.WeatherType}");
        }

        private void BuildRoomDesign(WorldConfiguration config)
        {
            // Clear existing room
            if (_roomContainer != null)
            {
                foreach (Transform child in _roomContainer.transform)
                {
                    Destroy(child.gameObject);
                }
            }

            Vector3 roomSize = config.RoomSize;
            Vector3 center = Vector3.zero;

            // Create floor
            if (!config.EnableTerrain)
            {
                GameObject floor = CreatePlane(center - Vector3.up * 0.01f, roomSize.x, roomSize.z, config.FloorColor);
                floor.name = "Floor";
                floor.transform.SetParent(_roomContainer.transform);
            }

            // Create walls
            CreateWall(center + Vector3.forward * roomSize.z / 2, roomSize.x, roomSize.y, config.WallColor, "Wall_North");
            CreateWall(center - Vector3.forward * roomSize.z / 2, roomSize.x, roomSize.y, config.WallColor, "Wall_South");
            CreateWall(center + Vector3.right * roomSize.x / 2, roomSize.z, roomSize.y, config.WallColor, "Wall_East");
            CreateWall(center - Vector3.right * roomSize.x / 2, roomSize.z, roomSize.y, config.WallColor, "Wall_West");

            // Create ceiling if enabled
            if (config.EnableCeiling)
            {
                GameObject ceiling = CreatePlane(center + Vector3.up * roomSize.y, roomSize.x, roomSize.z, config.WallColor);
                ceiling.name = "Ceiling";
                ceiling.transform.SetParent(_roomContainer.transform);
                ceiling.transform.Rotate(180f, 0f, 0f);
            }

            Debug.Log($"[WorldEnvironmentManager] Room built: {roomSize}");
        }

        private GameObject CreatePlane(Vector3 position, float width, float depth, Color color)
        {
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.position = position;
            plane.transform.localScale = new Vector3(width / 10f, 1f, depth / 10f);

            Renderer renderer = plane.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (_floorMaterial != null)
                {
                    renderer.sharedMaterial = _floorMaterial;
                }
                else
                {
                    Material mat = new Material(Shader.Find("Standard"));
                    mat.color = color;
                    renderer.sharedMaterial = mat;
                }
            }

            return plane;
        }

        private void CreateWall(Vector3 position, float width, float height, Color color, string name)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = position;
            wall.transform.localScale = new Vector3(width, height, 0.2f);

            Renderer renderer = wall.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (_wallMaterial != null)
                {
                    renderer.sharedMaterial = _wallMaterial;
                }
                else
                {
                    Material mat = new Material(Shader.Find("Standard"));
                    mat.color = color;
                    renderer.sharedMaterial = mat;
                }
            }

            wall.transform.SetParent(_roomContainer.transform);
        }

        private void SpawnWiFiModems(WorldConfiguration config)
        {
            // Clear existing modems
            foreach (var modem in _activeModems)
            {
                if (modem != null) Destroy(modem);
            }
            _activeModems.Clear();

            if (config?.Modems == null || config.Modems.Count == 0)
            {
                Debug.Log("[WorldEnvironmentManager] WiFi modems: none configured");
                return;
            }

            if (_wifiModemPrefab == null)
            {
                _wifiModemPrefab = Resources.Load<GameObject>("Prefabs/WiFiModem");
            }

            if (_wifiModemPrefab == null)
            {
                Debug.LogWarning("[WorldEnvironmentManager] WiFi modem prefab missing (Prefabs/WiFiModem)");
                return;
            }

            foreach (var modem in config.Modems)
            {
                var instance = Instantiate(_wifiModemPrefab, modem.Position, Quaternion.identity, _modemContainer.transform);
                instance.name = modem.SSID;
                _activeModems.Add(instance);

                var modemComponent = instance.GetComponent<WiFiModemSimulator>();
                if (modemComponent != null)
                {
                    modemComponent.SSID = modem.SSID;
                    modemComponent.Range = modem.Range;
                    modemComponent.Bandwidth = modem.Bandwidth;
                }
            }

            Debug.Log($"[WorldEnvironmentManager] WiFi modems spawned: {config.Modems.Count}");
        }

        private void BuildTerrain(WorldConfiguration config)
        {
            if (!config.EnableTerrain)
            {
                if (_terrain != null)
                {
                    _terrain.gameObject.SetActive(false);
                }
                _terrainEnabled = false;
                return;
            }

            _terrainEnabled = true;
            if (_terrain == null)
            {
                var terrainGo = new GameObject("WorldTerrain");
                terrainGo.transform.SetParent(_roomContainer != null ? _roomContainer.transform : transform);
                _terrain = terrainGo.AddComponent<Terrain>();
                _terrainCollider = terrainGo.AddComponent<TerrainCollider>();
            }

            var data = new TerrainData();
            int resolution = 129;
            data.heightmapResolution = resolution;
            data.size = new Vector3(config.TerrainSize.x, config.TerrainHeight, config.TerrainSize.y);

            float scale = Mathf.Max(0.001f, config.TerrainNoiseScale);
            float[,] heights = new float[resolution, resolution];
            float seedX = (config.TerrainSeed % 1000) * 0.17f;
            float seedZ = (config.TerrainSeed % 1000) * 0.23f;
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float nx = (x + seedX) / (resolution - 1) * scale;
                    float nz = (z + seedZ) / (resolution - 1) * scale;
                    float h = Mathf.PerlinNoise(nx, nz);
                    heights[z, x] = h * 0.35f;
                }
            }
            data.SetHeights(0, 0, heights);

            _terrain.terrainData = data;
            if (_terrainCollider != null)
            {
                _terrainCollider.terrainData = data;
            }

            _terrain.gameObject.SetActive(true);
        }

        private void EnsurePlayer(WorldConfiguration config)
        {
            if (!config.EnableFpsMode)
            {
                if (_playerInstance != null) _playerInstance.SetActive(false);
                return;
            }

            if (_playerInstance == null)
            {
                GameObject prefab = _playerPrefab;
                if (prefab == null && !string.IsNullOrWhiteSpace(config.PlayerPrefab))
                {
                    prefab = Resources.Load<GameObject>(config.PlayerPrefab);
                }
                _playerInstance = prefab != null ? Instantiate(prefab) : CreateFallbackPlayer();
            }

            _playerInstance.transform.position = config.PlayerSpawn;
            _playerInstance.transform.rotation = Quaternion.identity;
            _playerInstance.SetActive(true);
        }

        private static GameObject CreateFallbackPlayer()
        {
            var root = new GameObject("FPS_Player");
            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            var cameraGo = new GameObject("Camera");
            cameraGo.transform.SetParent(root.transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            cameraGo.AddComponent<Camera>();

            var fps = root.AddComponent<RobotTwin.Gameplay.FpsPlayerController>();
            fps.CameraRoot = cameraGo.transform;
            return root;
        }

        private void CreateWorldBoundaries(WorldConfiguration config)
        {
            // Clear existing boundaries
            foreach (var boundary in _boundaryWalls)
            {
                if (boundary != null) Destroy(boundary);
            }
            _boundaryWalls.Clear();

            if (!config.EnableBoundaryCollision)
                return;

            Vector3 min = config.WorldBoundsMin;
            Vector3 max = config.WorldBoundsMax;
            Vector3 center = (min + max) / 2f;
            Vector3 size = max - min;

            // Create invisible boundary walls
            CreateBoundaryWall(center + Vector3.forward * size.z / 2, size.x, size.y, 0.1f, "Boundary_North");
            CreateBoundaryWall(center - Vector3.forward * size.z / 2, size.x, size.y, 0.1f, "Boundary_South");
            CreateBoundaryWall(center + Vector3.right * size.x / 2, size.z, size.y, 0.1f, "Boundary_East");
            CreateBoundaryWall(center - Vector3.right * size.x / 2, size.z, size.y, 0.1f, "Boundary_West");
            CreateBoundaryWall(center + Vector3.up * size.y / 2, size.x, 0.1f, size.z, "Boundary_Top");
            CreateBoundaryWall(center - Vector3.up * size.y / 2, size.x, 0.1f, size.z, "Boundary_Bottom");

            Debug.Log($"[WorldEnvironmentManager] Boundaries created: {min} to {max}");
        }

        private void CreateBoundaryWall(Vector3 position, float width, float height, float depth, string name)
        {
            GameObject boundary = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boundary.name = name;
            boundary.transform.position = position;
            boundary.transform.localScale = new Vector3(width, height, depth);

            // Make invisible but keep collider
            Renderer renderer = boundary.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }

            boundary.transform.SetParent(_boundaryContainer.transform);
            _boundaryWalls.Add(boundary);
        }

        private void ApplyAudioSettings(WorldConfiguration config)
        {
            AudioListener.volume = config.EnableSound ? config.MasterVolume : 0f;
            Debug.Log($"[WorldEnvironmentManager] Audio: Volume={config.MasterVolume}");
        }

        void Update()
        {
            // Apply wind force to rigidbodies (simplified)
            if (_activeConfig != null && _activeConfig.Wind.magnitude > 0.01f)
            {
                ApplyWindForce();
            }
        }

        private void ApplyWindForce()
        {
            Rigidbody[] rigidbodies = FindObjectsOfType<Rigidbody>();
            foreach (var rb in rigidbodies)
            {
                if (!rb.isKinematic)
                {
                    // Apply wind force based on air density and object velocity
                    Vector3 relativeWind = _activeConfig.Wind - rb.velocity;
                    Vector3 dragForce = 0.5f * _activeConfig.AirDensity * relativeWind.sqrMagnitude * 0.1f * relativeWind.normalized;
                    rb.AddForce(dragForce, ForceMode.Force);
                }
            }
        }

        public WorldConfiguration GetActiveConfiguration()
        {
            return _activeConfig;
        }

        public void LoadConfigurationFromFile(string path)
        {
            if (System.IO.File.Exists(path))
            {
                string json = System.IO.File.ReadAllText(path);
                WorldConfiguration config = JsonUtility.FromJson<WorldConfiguration>(json);
                ApplyConfiguration(config);
                Debug.Log($"[WorldEnvironmentManager] Configuration loaded from {path}");
            }
            else
            {
                Debug.LogWarning($"[WorldEnvironmentManager] Configuration file not found: {path}");
            }
        }
    }
}
