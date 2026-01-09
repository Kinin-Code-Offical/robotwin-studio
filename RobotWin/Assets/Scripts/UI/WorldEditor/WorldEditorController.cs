using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;

namespace RobotTwin.UI.WorldEditor
{
    /// <summary>
    /// World Editor Controller - Environment & Physics Configuration
    /// Allows users to configure world settings, physics parameters, environmental effects,
    /// WiFi modems, sound effects, and all simulation constraints BEFORE running.
    /// </summary>
    public class WorldEditorController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _root;

        // Main Panels
        private VisualElement _environmentPanel;
        private VisualElement _physicsPanel;
        private VisualElement _networkPanel;
        private VisualElement _audioPanel;
        private VisualElement _constraintsPanel;

        // Environment Controls
        private Slider _gravitySlider;
        private Label _gravityLabel;
        private Slider _windXSlider, _windYSlider, _windZSlider;
        private Label _windLabel;
        private Slider _airDensitySlider;
        private Toggle _enableWeatherToggle;
        private DropdownField _weatherTypeDropdown;
        private Slider _temperatureSlider;
        private Label _temperatureLabel;

        // Room Design
        private DropdownField _roomTypeDropdown;
        private TextField _roomSizeXField, _roomSizeYField, _roomSizeZField;
        private TextField _floorColorField;
        private TextField _wallColorField;
        private Toggle _enableCeilingToggle;
        private Slider _lightIntensitySlider;

        // Physics Parameters
        private Slider _timeScaleSlider;
        private Label _timeScaleLabel;
        private Slider _fixedDeltaTimeSlider;
        private Toggle _enableCollisionToggle;
        private Slider _bounceMultiplierSlider;
        private Slider _frictionMultiplierSlider;
        private Toggle _enableGravityToggle;

        // Network (WiFi Modems)
        private ListView _modemListView;
        private Button _addModemButton;
        private Button _removeModemButton;
        private List<ModemConfig> _modems = new List<ModemConfig>();
        private TextField _modemSSIDField;
        private Slider _modemRangeSlider;
        private Slider _modemBandwidthSlider;

        // Audio Effects
        private Toggle _enableSoundToggle;
        private Slider _masterVolumeSlider;
        private ListView _soundEffectsList;
        private List<SoundEffectConfig> _soundEffects = new List<SoundEffectConfig>();
        private Button _addSoundButton;

        // World Constraints
        private TextField _worldBoundsMinXField, _worldBoundsMinYField, _worldBoundsMinZField;
        private TextField _worldBoundsMaxXField, _worldBoundsMaxYField, _worldBoundsMaxZField;
        private Toggle _enableBoundaryCollisionToggle;
        private Slider _maxRobotsSlider;
        private Label _maxRobotsLabel;
        private Slider _maxObjectsSlider;

        // Key Bindings (for test mode)
        private Dictionary<string, KeyCode> _keyBindings = new Dictionary<string, KeyCode>();
        private ListView _keyBindingsList;
        private Button _addKeyBindingButton;

        // Current Configuration
        private WorldConfiguration _currentConfig;

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null)
            {
                Debug.LogError("[WorldEditor] Missing UIDocument");
                return;
            }

            _root = _doc.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError("[WorldEditor] rootVisualElement is null");
                return;
            }

            InitializeCurrentConfig();
            BindUIElements();
            PopulateUI();
            RegisterCallbacks();
        }

        private void InitializeCurrentConfig()
        {
            _currentConfig = new WorldConfiguration
            {
                Gravity = new Vector3(0, -9.81f, 0),
                Wind = Vector3.zero,
                AirDensity = 1.225f,
                Temperature = 20f,
                EnableWeather = false,
                WeatherType = WeatherType.Clear,
                RoomType = RoomType.Laboratory,
                RoomSize = new Vector3(20f, 5f, 20f),
                FloorColor = new Color(0.8f, 0.8f, 0.8f),
                WallColor = new Color(0.9f, 0.9f, 0.9f),
                EnableCeiling = true,
                LightIntensity = 1.0f,
                TimeScale = 1.0f,
                FixedDeltaTime = 0.02f,
                EnableCollision = true,
                BounceMultiplier = 1.0f,
                FrictionMultiplier = 1.0f,
                EnableGravity = true,
                WorldBoundsMin = new Vector3(-50f, -10f, -50f),
                WorldBoundsMax = new Vector3(50f, 20f, 50f),
                EnableBoundaryCollision = true,
                MaxRobots = 10,
                MaxObjects = 100,
                EnableSound = true,
                MasterVolume = 0.7f,
                Modems = new List<ModemConfig>(),
                SoundEffects = new List<SoundEffectConfig>(),
                EnableTerrain = false,
                TerrainSize = new Vector2(60f, 60f),
                TerrainHeight = 6f,
                TerrainNoiseScale = 18f,
                TerrainSeed = 1234,
                EnableFpsMode = false,
                PlayerSpawn = new Vector3(0f, 1.8f, -4f),
                PlayerPrefab = string.Empty
            };

            // Default key bindings
            _keyBindings = new Dictionary<string, KeyCode>
            {
                { "Move Forward", KeyCode.W },
                { "Move Backward", KeyCode.S },
                { "Move Left", KeyCode.A },
                { "Move Right", KeyCode.D },
                { "Jump", KeyCode.Space },
                { "Reset Camera", KeyCode.R },
                { "Toggle Pause", KeyCode.P },
                { "Speed Up", KeyCode.UpArrow },
                { "Slow Down", KeyCode.DownArrow },
                { "Emergency Stop", KeyCode.Escape }
            };
        }

        private void BindUIElements()
        {
            // Main Panels
            _environmentPanel = _root.Q<VisualElement>("EnvironmentPanel");
            _physicsPanel = _root.Q<VisualElement>("PhysicsPanel");
            _networkPanel = _root.Q<VisualElement>("NetworkPanel");
            _audioPanel = _root.Q<VisualElement>("AudioPanel");
            _constraintsPanel = _root.Q<VisualElement>("ConstraintsPanel");

            // Environment
            _gravitySlider = _root.Q<Slider>("GravitySlider");
            _gravityLabel = _root.Q<Label>("GravityLabel");
            _windXSlider = _root.Q<Slider>("WindXSlider");
            _windYSlider = _root.Q<Slider>("WindYSlider");
            _windZSlider = _root.Q<Slider>("WindZSlider");
            _windLabel = _root.Q<Label>("WindLabel");
            _airDensitySlider = _root.Q<Slider>("AirDensitySlider");
            _enableWeatherToggle = _root.Q<Toggle>("EnableWeatherToggle");
            _weatherTypeDropdown = _root.Q<DropdownField>("WeatherTypeDropdown");
            _temperatureSlider = _root.Q<Slider>("TemperatureSlider");
            _temperatureLabel = _root.Q<Label>("TemperatureLabel");

            // Room Design
            _roomTypeDropdown = _root.Q<DropdownField>("RoomTypeDropdown");
            _roomSizeXField = _root.Q<TextField>("RoomSizeXField");
            _roomSizeYField = _root.Q<TextField>("RoomSizeYField");
            _roomSizeZField = _root.Q<TextField>("RoomSizeZField");
            _enableCeilingToggle = _root.Q<Toggle>("EnableCeilingToggle");
            _lightIntensitySlider = _root.Q<Slider>("LightIntensitySlider");

            // Physics
            _timeScaleSlider = _root.Q<Slider>("TimeScaleSlider");
            _timeScaleLabel = _root.Q<Label>("TimeScaleLabel");
            _fixedDeltaTimeSlider = _root.Q<Slider>("FixedDeltaTimeSlider");
            _enableCollisionToggle = _root.Q<Toggle>("EnableCollisionToggle");
            _bounceMultiplierSlider = _root.Q<Slider>("BounceMultiplierSlider");
            _frictionMultiplierSlider = _root.Q<Slider>("FrictionMultiplierSlider");
            _enableGravityToggle = _root.Q<Toggle>("EnableGravityToggle");

            // Network
            _modemListView = _root.Q<ListView>("ModemListView");
            _addModemButton = _root.Q<Button>("AddModemButton");
            _removeModemButton = _root.Q<Button>("RemoveModemButton");
            _modemSSIDField = _root.Q<TextField>("ModemSSIDField");
            _modemRangeSlider = _root.Q<Slider>("ModemRangeSlider");
            _modemBandwidthSlider = _root.Q<Slider>("ModemBandwidthSlider");

            // Audio
            _enableSoundToggle = _root.Q<Toggle>("EnableSoundToggle");
            _masterVolumeSlider = _root.Q<Slider>("MasterVolumeSlider");
            _soundEffectsList = _root.Q<ListView>("SoundEffectsList");
            _addSoundButton = _root.Q<Button>("AddSoundButton");

            // Constraints
            _worldBoundsMinXField = _root.Q<TextField>("WorldBoundsMinXField");
            _worldBoundsMinYField = _root.Q<TextField>("WorldBoundsMinYField");
            _worldBoundsMinZField = _root.Q<TextField>("WorldBoundsMinZField");
            _worldBoundsMaxXField = _root.Q<TextField>("WorldBoundsMaxXField");
            _worldBoundsMaxYField = _root.Q<TextField>("WorldBoundsMaxYField");
            _worldBoundsMaxZField = _root.Q<TextField>("WorldBoundsMaxZField");
            _enableBoundaryCollisionToggle = _root.Q<Toggle>("EnableBoundaryCollisionToggle");
            _maxRobotsSlider = _root.Q<Slider>("MaxRobotsSlider");
            _maxRobotsLabel = _root.Q<Label>("MaxRobotsLabel");
            _maxObjectsSlider = _root.Q<Slider>("MaxObjectsSlider");

            // Key Bindings
            _keyBindingsList = _root.Q<ListView>("KeyBindingsList");
            _addKeyBindingButton = _root.Q<Button>("AddKeyBindingButton");
        }

        private void PopulateUI()
        {
            // Populate Environment values
            if (_gravitySlider != null)
            {
                _gravitySlider.lowValue = -30f;
                _gravitySlider.highValue = 30f;
                _gravitySlider.value = _currentConfig.Gravity.y;
                UpdateGravityLabel();
            }

            if (_windXSlider != null) _windXSlider.value = _currentConfig.Wind.x;
            if (_windYSlider != null) _windYSlider.value = _currentConfig.Wind.y;
            if (_windZSlider != null) _windZSlider.value = _currentConfig.Wind.z;
            UpdateWindLabel();

            if (_airDensitySlider != null)
            {
                _airDensitySlider.lowValue = 0f;
                _airDensitySlider.highValue = 5f;
                _airDensitySlider.value = _currentConfig.AirDensity;
            }

            if (_temperatureSlider != null)
            {
                _temperatureSlider.lowValue = -50f;
                _temperatureSlider.highValue = 50f;
                _temperatureSlider.value = _currentConfig.Temperature;
                UpdateTemperatureLabel();
            }

            if (_weatherTypeDropdown != null)
            {
                _weatherTypeDropdown.choices = new List<string> { "Clear", "Rain", "Snow", "Wind", "Storm" };
                _weatherTypeDropdown.value = _currentConfig.WeatherType.ToString();
            }

            if (_roomTypeDropdown != null)
            {
                _roomTypeDropdown.choices = new List<string> { "Laboratory", "Warehouse", "Office", "Outdoor", "Custom" };
                _roomTypeDropdown.value = _currentConfig.RoomType.ToString();
            }

            // Room Size
            if (_roomSizeXField != null) _roomSizeXField.value = _currentConfig.RoomSize.x.ToString();
            if (_roomSizeYField != null) _roomSizeYField.value = _currentConfig.RoomSize.y.ToString();
            if (_roomSizeZField != null) _roomSizeZField.value = _currentConfig.RoomSize.z.ToString();

            // Physics
            if (_timeScaleSlider != null)
            {
                _timeScaleSlider.lowValue = 0.1f;
                _timeScaleSlider.highValue = 5f;
                _timeScaleSlider.value = _currentConfig.TimeScale;
                UpdateTimeScaleLabel();
            }

            if (_maxRobotsSlider != null)
            {
                _maxRobotsSlider.lowValue = 1f;
                _maxRobotsSlider.highValue = 50f;
                _maxRobotsSlider.value = _currentConfig.MaxRobots;
                UpdateMaxRobotsLabel();
            }

            // World Bounds
            if (_worldBoundsMinXField != null) _worldBoundsMinXField.value = _currentConfig.WorldBoundsMin.x.ToString();
            if (_worldBoundsMinYField != null) _worldBoundsMinYField.value = _currentConfig.WorldBoundsMin.y.ToString();
            if (_worldBoundsMinZField != null) _worldBoundsMinZField.value = _currentConfig.WorldBoundsMin.z.ToString();
            if (_worldBoundsMaxXField != null) _worldBoundsMaxXField.value = _currentConfig.WorldBoundsMax.x.ToString();
            if (_worldBoundsMaxYField != null) _worldBoundsMaxYField.value = _currentConfig.WorldBoundsMax.y.ToString();
            if (_worldBoundsMaxZField != null) _worldBoundsMaxZField.value = _currentConfig.WorldBoundsMax.z.ToString();

            // Populate key bindings
            PopulateKeyBindings();

            _modems = _currentConfig.Modems ?? new List<ModemConfig>();
            _soundEffects = _currentConfig.SoundEffects ?? new List<SoundEffectConfig>();
            RefreshModemList();
            RefreshSoundEffectsList();
        }

        private void RegisterCallbacks()
        {
            // Environment callbacks
            if (_gravitySlider != null)
                _gravitySlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.Gravity.y = evt.newValue;
                    UpdateGravityLabel();
                    ApplyGravityToPhysics();
                });

            if (_windXSlider != null)
                _windXSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.Wind.x = evt.newValue;
                    UpdateWindLabel();
                });

            if (_windYSlider != null)
                _windYSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.Wind.y = evt.newValue;
                    UpdateWindLabel();
                });

            if (_windZSlider != null)
                _windZSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.Wind.z = evt.newValue;
                    UpdateWindLabel();
                });

            if (_temperatureSlider != null)
                _temperatureSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.Temperature = evt.newValue;
                    UpdateTemperatureLabel();
                });

            if (_timeScaleSlider != null)
                _timeScaleSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.TimeScale = evt.newValue;
                    Time.timeScale = evt.newValue;
                    UpdateTimeScaleLabel();
                });

            if (_fixedDeltaTimeSlider != null)
                _fixedDeltaTimeSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.FixedDeltaTime = evt.newValue;
                    Time.fixedDeltaTime = evt.newValue;
                });

            if (_enableGravityToggle != null)
                _enableGravityToggle.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.EnableGravity = evt.newValue;
                    Physics.gravity = evt.newValue ? _currentConfig.Gravity : Vector3.zero;
                });

            if (_maxRobotsSlider != null)
                _maxRobotsSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.MaxRobots = (int)evt.newValue;
                    UpdateMaxRobotsLabel();
                });

            // Modem management
            if (_addModemButton != null)
                _addModemButton.clicked += AddModem;

            if (_removeModemButton != null)
                _removeModemButton.clicked += RemoveSelectedModem;

            // Sound management
            if (_addSoundButton != null)
                _addSoundButton.clicked += AddSoundEffect;

            if (_masterVolumeSlider != null)
                _masterVolumeSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.MasterVolume = evt.newValue;
                    AudioListener.volume = evt.newValue;
                });

            // Key bindings
            if (_addKeyBindingButton != null)
                _addKeyBindingButton.clicked += AddKeyBinding;

            // Top bar buttons
            var saveButton = _root.Q<Button>("SaveConfigButton");
            if (saveButton != null)
                saveButton.clicked += () => SaveConfiguration("WorldConfig.json");

            var loadButton = _root.Q<Button>("LoadConfigButton");
            if (loadButton != null)
                loadButton.clicked += () => LoadConfiguration("WorldConfig.json");

            var applyButton = _root.Q<Button>("ApplyButton");
            if (applyButton != null)
                applyButton.clicked += ApplyConfigurationToScene;

            var backButton = _root.Q<Button>("BackButton");
            if (backButton != null)
                backButton.clicked += () => UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        private void UpdateGravityLabel()
        {
            if (_gravityLabel != null)
                _gravityLabel.text = $"Gravity: {_currentConfig.Gravity.y:F2} m/s²";
        }

        private void UpdateWindLabel()
        {
            if (_windLabel != null)
                _windLabel.text = $"Wind: ({_currentConfig.Wind.x:F1}, {_currentConfig.Wind.y:F1}, {_currentConfig.Wind.z:F1}) m/s";
        }

        private void UpdateTemperatureLabel()
        {
            if (_temperatureLabel != null)
                _temperatureLabel.text = $"Temperature: {_currentConfig.Temperature:F1}°C";
        }

        private void UpdateTimeScaleLabel()
        {
            if (_timeScaleLabel != null)
                _timeScaleLabel.text = $"Time Scale: {_currentConfig.TimeScale:F2}x";
        }

        private void UpdateMaxRobotsLabel()
        {
            if (_maxRobotsLabel != null)
                _maxRobotsLabel.text = $"Max Robots: {_currentConfig.MaxRobots}";
        }

        private void ApplyGravityToPhysics()
        {
            if (_currentConfig.EnableGravity)
            {
                Physics.gravity = _currentConfig.Gravity;
            }
        }

        private void AddModem()
        {
            var modem = new ModemConfig
            {
                SSID = _modemSSIDField?.value ?? $"WiFi_{_modems.Count + 1}",
                Range = _modemRangeSlider?.value ?? 10f,
                Bandwidth = _modemBandwidthSlider?.value ?? 100f,
                Position = Vector3.zero
            };
            _modems.Add(modem);
            RefreshModemList();
        }

        private void RemoveSelectedModem()
        {
            if (_modemListView != null && _modemListView.selectedIndex >= 0 && _modemListView.selectedIndex < _modems.Count)
            {
                _modems.RemoveAt(_modemListView.selectedIndex);
                RefreshModemList();
            }
        }

        private void RefreshModemList()
        {
            if (_modemListView != null)
            {
                _modemListView.itemsSource = _modems;
                _modemListView.Rebuild();
            }
        }

        private void AddSoundEffect()
        {
            var sound = new SoundEffectConfig
            {
                Name = $"Sound_{_soundEffects.Count + 1}",
                Volume = 0.5f,
                Loop = false,
                Spatial = true
            };
            _soundEffects.Add(sound);
            RefreshSoundEffectsList();
        }

        private void RefreshSoundEffectsList()
        {
            if (_soundEffectsList != null)
            {
                _soundEffectsList.itemsSource = _soundEffects;
                _soundEffectsList.Rebuild();
            }
        }

        private void AddKeyBinding()
        {
            // Show dialog for key binding configuration
            Debug.Log("[WorldEditor] Add Key Binding dialog (to be implemented)");
        }

        private void PopulateKeyBindings()
        {
            if (_keyBindingsList != null)
            {
                var bindings = new List<string>();
                foreach (var kvp in _keyBindings)
                {
                    bindings.Add($"{kvp.Key}: {kvp.Value}");
                }
                _keyBindingsList.itemsSource = bindings;
                _keyBindingsList.Rebuild();
            }
        }

        public WorldConfiguration GetCurrentConfiguration()
        {
            return _currentConfig;
        }

        public void SaveConfiguration(string path)
        {
            try
            {
                // Read values from TextField controls before saving
                ReadTextFieldValues();
                SyncConfigLists();

                string json = JsonUtility.ToJson(_currentConfig, true);
                System.IO.File.WriteAllText(path, json);
                Debug.Log($"[WorldEditor] Configuration saved to {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldEditor] Failed to save configuration: {ex.Message}");
            }
        }

        private void ReadTextFieldValues()
        {
            // Room Size
            if (_roomSizeXField != null && float.TryParse(_roomSizeXField.value, out float rsX))
                _currentConfig.RoomSize.x = rsX;
            if (_roomSizeYField != null && float.TryParse(_roomSizeYField.value, out float rsY))
                _currentConfig.RoomSize.y = rsY;
            if (_roomSizeZField != null && float.TryParse(_roomSizeZField.value, out float rsZ))
                _currentConfig.RoomSize.z = rsZ;

            // World Bounds
            if (_worldBoundsMinXField != null && float.TryParse(_worldBoundsMinXField.value, out float minX))
                _currentConfig.WorldBoundsMin.x = minX;
            if (_worldBoundsMinYField != null && float.TryParse(_worldBoundsMinYField.value, out float minY))
                _currentConfig.WorldBoundsMin.y = minY;
            if (_worldBoundsMinZField != null && float.TryParse(_worldBoundsMinZField.value, out float minZ))
                _currentConfig.WorldBoundsMin.z = minZ;
            if (_worldBoundsMaxXField != null && float.TryParse(_worldBoundsMaxXField.value, out float maxX))
                _currentConfig.WorldBoundsMax.x = maxX;
            if (_worldBoundsMaxYField != null && float.TryParse(_worldBoundsMaxYField.value, out float maxY))
                _currentConfig.WorldBoundsMax.y = maxY;
            if (_worldBoundsMaxZField != null && float.TryParse(_worldBoundsMaxZField.value, out float maxZ))
                _currentConfig.WorldBoundsMax.z = maxZ;
        }

        private void ApplyConfigurationToScene()
        {
            // Read latest TextField values
            ReadTextFieldValues();
            SyncConfigLists();

            // Apply physics settings
            Physics.gravity = _currentConfig.EnableGravity ? _currentConfig.Gravity : Vector3.zero;
            Time.timeScale = _currentConfig.TimeScale;
            Time.fixedDeltaTime = _currentConfig.FixedDeltaTime;
            AudioListener.volume = _currentConfig.MasterVolume;

            // Log applied configuration
            Debug.Log("[WorldEditor] Configuration applied to scene:");
            Debug.Log($"  Gravity: {_currentConfig.Gravity}");
            Debug.Log($"  Wind: {_currentConfig.Wind}");
            Debug.Log($"  Temperature: {_currentConfig.Temperature}°C");
            Debug.Log($"  Time Scale: {_currentConfig.TimeScale}x");
            Debug.Log($"  Room Size: {_currentConfig.RoomSize}");
            Debug.Log($"  World Bounds: {_currentConfig.WorldBoundsMin} to {_currentConfig.WorldBoundsMax}");
            Debug.Log($"  Max Robots: {_currentConfig.MaxRobots}, Max Objects: {_currentConfig.MaxObjects}");

            // Update status label
            var statusLabel = _root.Q<Label>("StatusLabel");
            if (statusLabel != null)
                statusLabel.text = "Configuration Applied!";

            var envManager = FindObjectOfType<WorldEnvironmentManager>();
            if (envManager != null)
            {
                envManager.ApplyConfiguration(_currentConfig);
                return;
            }

            // Apply room design (walls, floor, ceiling)
            CreateRoomGeometry();

            // Spawn WiFi modems at configured positions
            SpawnWiFiModems();

            // Configure sound effects
            ConfigureAmbientSound();

            // Create world boundary colliders
            CreateWorldBoundaryColliders();
        }

        private void SyncConfigLists()
        {
            if (_currentConfig == null) return;
            _currentConfig.Modems = _modems != null ? new List<ModemConfig>(_modems) : new List<ModemConfig>();
            _currentConfig.SoundEffects = _soundEffects != null ? new List<SoundEffectConfig>(_soundEffects) : new List<SoundEffectConfig>();
        }

        private void CreateRoomGeometry()
        {
            // Create or update floor
            GameObject floor = GameObject.Find("WorldFloor");
            if (floor == null)
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "WorldFloor";
            }

            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(_currentConfig.RoomSize.x, 0.1f, _currentConfig.RoomSize.z);

            Renderer floorRenderer = floor.GetComponent<Renderer>();
            if (floorRenderer != null)
            {
                floorRenderer.material.color = _currentConfig.FloorColor;
            }

            // Create walls if room type is not Outdoor
            if (_currentConfig.RoomType != RoomType.Outdoor)
            {
                CreateRoomWalls();
            }

            Debug.Log("[WorldEditor] Room geometry created");
        }

        private void CreateRoomWalls()
        {
            float width = _currentConfig.RoomSize.x;
            float height = _currentConfig.RoomSize.y;
            float depth = _currentConfig.RoomSize.z;
            Color wallColor = _currentConfig.WallColor;

            // North wall
            CreateWall("NorthWall", new Vector3(0, height / 2, depth / 2),
                      new Vector3(width, height, 0.1f), wallColor);

            // South wall
            CreateWall("SouthWall", new Vector3(0, height / 2, -depth / 2),
                      new Vector3(width, height, 0.1f), wallColor);

            // East wall
            CreateWall("EastWall", new Vector3(width / 2, height / 2, 0),
                      new Vector3(0.1f, height, depth), wallColor);

            // West wall
            CreateWall("WestWall", new Vector3(-width / 2, height / 2, 0),
                      new Vector3(0.1f, height, depth), wallColor);

            // Ceiling (if enabled)
            if (_currentConfig.EnableCeiling)
            {
                CreateWall("Ceiling", new Vector3(0, height, 0),
                          new Vector3(width, 0.1f, depth), wallColor);
            }
        }

        private void CreateWall(string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject wall = GameObject.Find(name);
            if (wall == null)
            {
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = name;
            }

            wall.transform.position = position;
            wall.transform.localScale = scale;

            Renderer renderer = wall.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }

        private void SpawnWiFiModems()
        {
            // Clear existing modems
            foreach (var existing in GameObject.FindGameObjectsWithTag("WiFiModem"))
            {
                Destroy(existing);
            }

            // Spawn configured modems
            GameObject modemPrefab = Resources.Load<GameObject>("Prefabs/WiFiModem");
            if (modemPrefab != null)
            {
                foreach (var modem in _modems)
                {
                    GameObject instance = Instantiate(modemPrefab, modem.Position, Quaternion.identity);
                    instance.name = modem.SSID;

                    // Configure modem component if it exists
                    var modemComponent = instance.GetComponent<WiFiModemSimulator>();
                    if (modemComponent != null)
                    {
                        modemComponent.SSID = modem.SSID;
                        modemComponent.Range = modem.Range;
                        modemComponent.Bandwidth = modem.Bandwidth;
                    }
                }

                Debug.Log($"[WorldEditor] Spawned {_modems.Count} WiFi modems");
            }
        }

        private void ConfigureAmbientSound()
        {
            AudioSource ambient = Camera.main?.GetComponent<AudioSource>();
            if (ambient == null && Camera.main != null)
            {
                ambient = Camera.main.gameObject.AddComponent<AudioSource>();
            }

            if (ambient != null && _currentConfig.EnableSound)
            {
                ambient.volume = _currentConfig.MasterVolume * 0.5f; // Ambient at 50% of master
                ambient.loop = true;

                // Load room-appropriate ambient sound
                string soundName = GetAmbientSoundForRoomType(_currentConfig.RoomType);
                AudioClip clip = Resources.Load<AudioClip>($"Audio/{soundName}");
                if (clip != null)
                {
                    ambient.clip = clip;
                    if (!ambient.isPlaying)
                        ambient.Play();
                }
            }
        }

        private string GetAmbientSoundForRoomType(RoomType roomType)
        {
            switch (roomType)
            {
                case RoomType.Laboratory: return "LabAmbient";
                case RoomType.Warehouse: return "WarehouseAmbient";
                case RoomType.Office: return "OfficeAmbient";
                case RoomType.Outdoor: return "OutdoorAmbient";
                default: return "SilentAmbient";
            }
        }

        private void CreateWorldBoundaryColliders()
        {
            if (!_currentConfig.EnableBoundaryCollision)
                return;

            GameObject boundary = GameObject.Find("WorldBoundary");
            if (boundary == null)
            {
                boundary = new GameObject("WorldBoundary");
            }

            // Clear existing colliders
            foreach (Transform child in boundary.transform)
            {
                Destroy(child.gameObject);
            }

            // Create invisible boundary walls using world bounds
            Vector3 min = _currentConfig.WorldBoundsMin;
            Vector3 max = _currentConfig.WorldBoundsMax;
            Vector3 center = (min + max) / 2f;
            Vector3 size = max - min;

            // Create six boundary planes
            CreateBoundaryWall(boundary.transform, "BoundaryNorth",
                             new Vector3(center.x, center.y, max.z),
                             new Vector3(size.x, size.y, 0.1f));

            CreateBoundaryWall(boundary.transform, "BoundarySouth",
                             new Vector3(center.x, center.y, min.z),
                             new Vector3(size.x, size.y, 0.1f));

            CreateBoundaryWall(boundary.transform, "BoundaryEast",
                             new Vector3(max.x, center.y, center.z),
                             new Vector3(0.1f, size.y, size.z));

            CreateBoundaryWall(boundary.transform, "BoundaryWest",
                             new Vector3(min.x, center.y, center.z),
                             new Vector3(0.1f, size.y, size.z));

            CreateBoundaryWall(boundary.transform, "BoundaryTop",
                             new Vector3(center.x, max.y, center.z),
                             new Vector3(size.x, 0.1f, size.z));

            CreateBoundaryWall(boundary.transform, "BoundaryBottom",
                             new Vector3(center.x, min.y, center.z),
                             new Vector3(size.x, 0.1f, size.z));

            Debug.Log("[WorldEditor] World boundary colliders created");
        }

        private void CreateBoundaryWall(Transform parent, string name, Vector3 position, Vector3 scale)
        {
            GameObject wall = new GameObject(name);
            wall.transform.SetParent(parent);
            wall.transform.position = position;

            BoxCollider collider = wall.AddComponent<BoxCollider>();
            collider.size = scale;
            collider.isTrigger = false;

            // Make invisible
            wall.layer = LayerMask.NameToLayer("Default");
        }

        public void LoadConfiguration(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    string json = System.IO.File.ReadAllText(path);
                    _currentConfig = JsonUtility.FromJson<WorldConfiguration>(json);
                    PopulateUI();
                    Debug.Log($"[WorldEditor] Configuration loaded from {path}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldEditor] Failed to load configuration: {ex.Message}");
            }
        }
    }

    [Serializable]
    public class WorldConfiguration
    {
        public Vector3 Gravity;
        public Vector3 Wind;
        public float AirDensity;
        public float Temperature;
        public bool EnableWeather;
        public WeatherType WeatherType;
        public RoomType RoomType;
        public Vector3 RoomSize;
        public Color FloorColor;
        public Color WallColor;
        public bool EnableCeiling;
        public float LightIntensity;
        public float TimeScale;
        public float FixedDeltaTime;
        public bool EnableCollision;
        public float BounceMultiplier;
        public float FrictionMultiplier;
        public bool EnableGravity;
        public Vector3 WorldBoundsMin;
        public Vector3 WorldBoundsMax;
        public bool EnableBoundaryCollision;
        public int MaxRobots;
        public int MaxObjects;
        public bool EnableSound;
        public float MasterVolume;
        public List<ModemConfig> Modems;
        public List<SoundEffectConfig> SoundEffects;
        public bool EnableTerrain;
        public Vector2 TerrainSize;
        public float TerrainHeight;
        public float TerrainNoiseScale;
        public int TerrainSeed;
        public bool EnableFpsMode;
        public Vector3 PlayerSpawn;
        public string PlayerPrefab;
    }

    [Serializable]
    public class ModemConfig
    {
        public string SSID;
        public float Range;
        public float Bandwidth;
        public Vector3 Position;
    }

    [Serializable]
    public class SoundEffectConfig
    {
        public string Name;
        public float Volume;
        public bool Loop;
        public bool Spatial;
    }

    public enum WeatherType
    {
        Clear,
        Rain,
        Snow,
        Wind,
        Storm
    }

    public enum RoomType
    {
        Laboratory,
        Warehouse,
        Office,
        Outdoor,
        Custom
    }
}
