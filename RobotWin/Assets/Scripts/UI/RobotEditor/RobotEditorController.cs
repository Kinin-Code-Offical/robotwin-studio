using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;

namespace RobotTwin.UI.RobotEditor
{
    /// <summary>
    /// Robot Editor Controller - Mobile Vehicle & Board Configuration
    /// Design robots with mobile vehicle support, board miniaturization, component optimization
    /// User requirement: "mobile araçlarını boardla miniltem ve minimalize algoritmasını geleştir"
    /// </summary>
    public class RobotEditorController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _root;

        // Vehicle Configuration
        private DropdownField _vehicleTypeDropdown;
        private Slider _vehicleSpeedSlider;
        private Label _vehicleSpeedLabel;
        private Slider _vehicleWeightSlider;
        private Label _vehicleWeightLabel;
        private TextField _vehicleNameField;

        // Board Configuration
        private DropdownField _boardTypeDropdown;
        private Slider _boardSizeSlider;
        private Label _boardSizeLabel;
        private Toggle _enableMiniaturizationToggle;
        private Slider _miniaturizationLevelSlider;
        private Label _miniaturizationLevelLabel;

        // Component List
        private ListView _componentListView;
        private Button _addComponentButton;
        private Button _removeComponentButton;
        private Button _optimizeComponentsButton;
        private List<RobotComponent> _components = new List<RobotComponent>();

        // Power & Optimization
        private Slider _batteryCapacitySlider;
        private Label _batteryCapacityLabel;
        private Label _estimatedRuntimeLabel;
        private Label _totalPowerConsumptionLabel;
        private Slider _optimizationLevelSlider;
        private Label _optimizationLevelLabel;

        // Sensors & Actuators
        private ListView _sensorListView;
        private ListView _actuatorListView;
        private Button _addSensorButton;
        private Button _addActuatorButton;
        private List<Sensor> _sensors = new List<Sensor>();
        private List<Actuator> _actuators = new List<Actuator>();

        // Current Configuration
        private RobotConfiguration _currentConfig;

        // Miniaturization Algorithm
        private MiniaturizationEngine _miniaturizationEngine;

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null)
            {
                Debug.LogError("[RobotEditor] Missing UIDocument");
                return;
            }

            _root = _doc.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError("[RobotEditor] rootVisualElement is null");
                return;
            }

            _miniaturizationEngine = new MiniaturizationEngine();

            InitializeCurrentConfig();
            BindUIElements();
            PopulateUI();
            RegisterCallbacks();
        }

        private void InitializeCurrentConfig()
        {
            _currentConfig = new RobotConfiguration
            {
                VehicleType = VehicleType.GroundRover,
                VehicleName = "Mobile_Robot_01",
                MaxSpeed = 2.0f,
                Weight = 5.0f,
                BoardType = BoardType.Arduino_Mega,
                BoardSize = 1.0f,
                EnableMiniaturization = false,
                MiniaturizationLevel = 0f,
                BatteryCapacity = 5000f,
                OptimizationLevel = 0.5f
            };

            // Default components
            _components.Add(new RobotComponent { Name = "Main Controller", Type = ComponentType.Microcontroller, PowerConsumption = 150f, Size = 1.0f });
            _components.Add(new RobotComponent { Name = "Motor Driver", Type = ComponentType.MotorDriver, PowerConsumption = 500f, Size = 0.8f });

            // Default sensors
            _sensors.Add(new Sensor { Name = "Ultrasonic", Type = SensorType.Ultrasonic, Range = 4.0f, PowerConsumption = 15f });
            _sensors.Add(new Sensor { Name = "Line Follower", Type = SensorType.Infrared, Range = 0.1f, PowerConsumption = 30f });

            // Default actuators
            _actuators.Add(new Actuator { Name = "Left Motor", Type = ActuatorType.DCMotor, MaxForce = 10f, PowerConsumption = 1000f });
            _actuators.Add(new Actuator { Name = "Right Motor", Type = ActuatorType.DCMotor, MaxForce = 10f, PowerConsumption = 1000f });
        }

        private void BindUIElements()
        {
            // Vehicle
            _vehicleTypeDropdown = _root.Q<DropdownField>("VehicleTypeDropdown");
            _vehicleSpeedSlider = _root.Q<Slider>("VehicleSpeedSlider");
            _vehicleSpeedLabel = _root.Q<Label>("VehicleSpeedLabel");
            _vehicleWeightSlider = _root.Q<Slider>("VehicleWeightSlider");
            _vehicleWeightLabel = _root.Q<Label>("VehicleWeightLabel");
            _vehicleNameField = _root.Q<TextField>("VehicleNameField");

            // Board
            _boardTypeDropdown = _root.Q<DropdownField>("BoardTypeDropdown");
            _boardSizeSlider = _root.Q<Slider>("BoardSizeSlider");
            _boardSizeLabel = _root.Q<Label>("BoardSizeLabel");
            _enableMiniaturizationToggle = _root.Q<Toggle>("EnableMiniaturizationToggle");
            _miniaturizationLevelSlider = _root.Q<Slider>("MiniaturizationLevelSlider");
            _miniaturizationLevelLabel = _root.Q<Label>("MiniaturizationLevelLabel");

            // Components
            _componentListView = _root.Q<ListView>("ComponentListView");
            _addComponentButton = _root.Q<Button>("AddComponentButton");
            _removeComponentButton = _root.Q<Button>("RemoveComponentButton");
            _optimizeComponentsButton = _root.Q<Button>("OptimizeComponentsButton");

            // Power
            _batteryCapacitySlider = _root.Q<Slider>("BatteryCapacitySlider");
            _batteryCapacityLabel = _root.Q<Label>("BatteryCapacityLabel");
            _estimatedRuntimeLabel = _root.Q<Label>("EstimatedRuntimeLabel");
            _totalPowerConsumptionLabel = _root.Q<Label>("TotalPowerConsumptionLabel");
            _optimizationLevelSlider = _root.Q<Slider>("OptimizationLevelSlider");
            _optimizationLevelLabel = _root.Q<Label>("OptimizationLevelLabel");

            // Sensors & Actuators
            _sensorListView = _root.Q<ListView>("SensorListView");
            _actuatorListView = _root.Q<ListView>("ActuatorListView");
            _addSensorButton = _root.Q<Button>("AddSensorButton");
            _addActuatorButton = _root.Q<Button>("AddActuatorButton");
        }

        private void PopulateUI()
        {
            // Vehicle Type
            if (_vehicleTypeDropdown != null)
            {
                _vehicleTypeDropdown.choices = new List<string>
                {
                    "Ground Rover",
                    "Tracked Vehicle",
                    "Wheeled Vehicle",
                    "Quadcopter",
                    "Hexacopter",
                    "Fixed Wing",
                    "Boat",
                    "Submarine",
                    "Humanoid",
                    "Quadruped",
                    "Custom"
                };
                _vehicleTypeDropdown.value = "Ground Rover";
            }

            if (_vehicleNameField != null)
                _vehicleNameField.value = _currentConfig.VehicleName;

            if (_vehicleSpeedSlider != null)
            {
                _vehicleSpeedSlider.lowValue = 0.1f;
                _vehicleSpeedSlider.highValue = 20f;
                _vehicleSpeedSlider.value = _currentConfig.MaxSpeed;
                UpdateVehicleSpeedLabel();
            }

            if (_vehicleWeightSlider != null)
            {
                _vehicleWeightSlider.lowValue = 0.1f;
                _vehicleWeightSlider.highValue = 100f;
                _vehicleWeightSlider.value = _currentConfig.Weight;
                UpdateVehicleWeightLabel();
            }

            // Board Type
            if (_boardTypeDropdown != null)
            {
                _boardTypeDropdown.choices = new List<string>
                {
                    "Arduino Uno",
                    "Arduino Mega",
                    "Arduino Nano",
                    "Arduino Micro",
                    "Raspberry Pi 4",
                    "Raspberry Pi Zero",
                    "ESP32",
                    "ESP8266",
                    "STM32",
                    "Teensy 4.0",
                    "Custom"
                };
                _boardTypeDropdown.value = "Arduino Mega";
            }

            if (_boardSizeSlider != null)
            {
                _boardSizeSlider.lowValue = 0.1f;
                _boardSizeSlider.highValue = 2f;
                _boardSizeSlider.value = _currentConfig.BoardSize;
                UpdateBoardSizeLabel();
            }

            if (_miniaturizationLevelSlider != null)
            {
                _miniaturizationLevelSlider.lowValue = 0f;
                _miniaturizationLevelSlider.highValue = 1f;
                _miniaturizationLevelSlider.value = _currentConfig.MiniaturizationLevel;
                UpdateMiniaturizationLabel();
            }

            if (_batteryCapacitySlider != null)
            {
                _batteryCapacitySlider.lowValue = 1000f;
                _batteryCapacitySlider.highValue = 20000f;
                _batteryCapacitySlider.value = _currentConfig.BatteryCapacity;
                UpdateBatteryCapacityLabel();
            }

            if (_optimizationLevelSlider != null)
            {
                _optimizationLevelSlider.lowValue = 0f;
                _optimizationLevelSlider.highValue = 1f;
                _optimizationLevelSlider.value = _currentConfig.OptimizationLevel;
                UpdateOptimizationLabel();
            }

            RefreshComponentList();
            RefreshSensorList();
            RefreshActuatorList();
            UpdatePowerStats();
        }

        private void RegisterCallbacks()
        {
            // Vehicle callbacks
            if (_vehicleSpeedSlider != null)
                _vehicleSpeedSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.MaxSpeed = evt.newValue;
                    UpdateVehicleSpeedLabel();
                });

            if (_vehicleWeightSlider != null)
                _vehicleWeightSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.Weight = evt.newValue;
                    UpdateVehicleWeightLabel();
                    UpdatePowerStats();
                });

            if (_vehicleNameField != null)
                _vehicleNameField.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.VehicleName = evt.newValue;
                });

            // Board callbacks
            if (_boardSizeSlider != null)
                _boardSizeSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.BoardSize = evt.newValue;
                    UpdateBoardSizeLabel();
                });

            if (_enableMiniaturizationToggle != null)
                _enableMiniaturizationToggle.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.EnableMiniaturization = evt.newValue;
                    if (_miniaturizationLevelSlider != null)
                        _miniaturizationLevelSlider.SetEnabled(evt.newValue);
                });

            if (_miniaturizationLevelSlider != null)
                _miniaturizationLevelSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.MiniaturizationLevel = evt.newValue;
                    UpdateMiniaturizationLabel();
                    ApplyMiniaturization();
                });

            // Power callbacks
            if (_batteryCapacitySlider != null)
                _batteryCapacitySlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.BatteryCapacity = evt.newValue;
                    UpdateBatteryCapacityLabel();
                    UpdatePowerStats();
                });

            if (_optimizationLevelSlider != null)
                _optimizationLevelSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.OptimizationLevel = evt.newValue;
                    UpdateOptimizationLabel();
                    ApplyOptimization();
                });

            // Component buttons
            if (_addComponentButton != null)
                _addComponentButton.clicked += AddComponent;

            if (_removeComponentButton != null)
                _removeComponentButton.clicked += RemoveSelectedComponent;

            if (_optimizeComponentsButton != null)
                _optimizeComponentsButton.clicked += OptimizeAllComponents;

            // Sensor/Actuator buttons
            if (_addSensorButton != null)
                _addSensorButton.clicked += AddSensor;

            if (_addActuatorButton != null)
                _addActuatorButton.clicked += AddActuator;

            // Top bar buttons
            var saveButton = _root.Q<Button>("SaveRobotButton");
            if (saveButton != null)
                saveButton.clicked += () => SaveConfiguration("RobotConfig.json");

            var loadButton = _root.Q<Button>("LoadRobotButton");
            if (loadButton != null)
                loadButton.clicked += () => LoadConfiguration("RobotConfig.json");

            var buildButton = _root.Q<Button>("BuildRobotButton");
            if (buildButton != null)
                buildButton.clicked += BuildRobot;

            var backButton = _root.Q<Button>("BackButton");
            if (backButton != null)
                backButton.clicked += () => UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        // Label Update Methods
        private void UpdateVehicleSpeedLabel()
        {
            if (_vehicleSpeedLabel != null)
                _vehicleSpeedLabel.text = $"Max Speed: {_currentConfig.MaxSpeed:F1} m/s";
        }

        private void UpdateVehicleWeightLabel()
        {
            if (_vehicleWeightLabel != null)
                _vehicleWeightLabel.text = $"Weight: {_currentConfig.Weight:F1} kg";
        }

        private void UpdateBoardSizeLabel()
        {
            if (_boardSizeLabel != null)
                _boardSizeLabel.text = $"Board Size: {_currentConfig.BoardSize:F2}x (relative)";
        }

        private void UpdateMiniaturizationLabel()
        {
            if (_miniaturizationLevelLabel != null)
            {
                int percentage = Mathf.RoundToInt(_currentConfig.MiniaturizationLevel * 100f);
                _miniaturizationLevelLabel.text = $"Miniaturization: {percentage}% (Size Reduction)";
            }
        }

        private void UpdateBatteryCapacityLabel()
        {
            if (_batteryCapacityLabel != null)
                _batteryCapacityLabel.text = $"Battery: {_currentConfig.BatteryCapacity:F0} mAh";
        }

        private void UpdateOptimizationLabel()
        {
            if (_optimizationLevelLabel != null)
            {
                int percentage = Mathf.RoundToInt(_currentConfig.OptimizationLevel * 100f);
                _optimizationLevelLabel.text = $"Optimization: {percentage}%";
            }
        }

        private void UpdatePowerStats()
        {
            float totalPower = CalculateTotalPowerConsumption();

            if (_totalPowerConsumptionLabel != null)
                _totalPowerConsumptionLabel.text = $"Total Power: {totalPower:F0} mW";

            if (_estimatedRuntimeLabel != null)
            {
                float runtimeHours = (_currentConfig.BatteryCapacity * 3.7f) / totalPower;
                _estimatedRuntimeLabel.text = $"Est. Runtime: {runtimeHours:F1} hours";
            }
        }

        private float CalculateTotalPowerConsumption()
        {
            float total = 0f;

            foreach (var comp in _components)
                total += comp.PowerConsumption;

            foreach (var sensor in _sensors)
                total += sensor.PowerConsumption;

            foreach (var actuator in _actuators)
                total += actuator.PowerConsumption * 0.3f; // Average 30% duty cycle

            // Apply optimization multiplier
            total *= (1f - _currentConfig.OptimizationLevel * 0.5f);

            return total;
        }

        // Miniaturization Algorithm
        private void ApplyMiniaturization()
        {
            if (!_currentConfig.EnableMiniaturization)
                return;

            float level = _currentConfig.MiniaturizationLevel;

            foreach (var comp in _components)
            {
                comp.Size = _miniaturizationEngine.OptimizeSize(comp.OriginalSize, level);
                comp.PowerConsumption = _miniaturizationEngine.OptimizePower(comp.OriginalPower, level);
            }

            foreach (var sensor in _sensors)
            {
                sensor.Size = _miniaturizationEngine.OptimizeSize(1.0f, level);
            }

            _currentConfig.BoardSize = _miniaturizationEngine.OptimizeSize(1.0f, level);
            UpdateBoardSizeLabel();
            RefreshComponentList();
            UpdatePowerStats();

            Debug.Log($"[RobotEditor] Miniaturization applied: {level * 100f}% reduction");
        }

        private void ApplyOptimization()
        {
            float level = _currentConfig.OptimizationLevel;

            foreach (var comp in _components)
            {
                comp.PowerConsumption = _miniaturizationEngine.OptimizePower(comp.OriginalPower, level);
            }

            UpdatePowerStats();
            RefreshComponentList();

            Debug.Log($"[RobotEditor] Power optimization applied: {level * 100f}%");
        }

        private void OptimizeAllComponents()
        {
            // Run full optimization: size + power + layout
            _currentConfig.EnableMiniaturization = true;
            _currentConfig.MiniaturizationLevel = 0.8f;
            _currentConfig.OptimizationLevel = 0.9f;

            ApplyMiniaturization();
            ApplyOptimization();

            Debug.Log("[RobotEditor] Full optimization complete");
        }

        // Component Management
        private void AddComponent()
        {
            var newComp = new RobotComponent
            {
                Name = "New Component",
                Type = ComponentType.Generic,
                PowerConsumption = 50f,
                Size = 1.0f
            };
            newComp.OriginalPower = newComp.PowerConsumption;
            newComp.OriginalSize = newComp.Size;

            _components.Add(newComp);
            RefreshComponentList();
            UpdatePowerStats();
        }

        private void RemoveSelectedComponent()
        {
            if (_componentListView != null && _componentListView.selectedIndex >= 0)
            {
                _components.RemoveAt(_componentListView.selectedIndex);
                RefreshComponentList();
                UpdatePowerStats();
            }
        }

        private void AddSensor()
        {
            var newSensor = new Sensor
            {
                Name = "New Sensor",
                Type = SensorType.Generic,
                Range = 1.0f,
                PowerConsumption = 10f
            };
            _sensors.Add(newSensor);
            RefreshSensorList();
            UpdatePowerStats();
        }

        private void AddActuator()
        {
            var newActuator = new Actuator
            {
                Name = "New Actuator",
                Type = ActuatorType.Servo,
                MaxForce = 5f,
                PowerConsumption = 200f
            };
            _actuators.Add(newActuator);
            RefreshActuatorList();
            UpdatePowerStats();
        }

        private void RefreshComponentList()
        {
            if (_componentListView != null)
            {
                var items = new List<string>();
                foreach (var comp in _components)
                {
                    items.Add($"{comp.Name} - {comp.PowerConsumption:F0}mW - Size:{comp.Size:F2}x");
                }
                _componentListView.itemsSource = items;
                _componentListView.Rebuild();
            }
        }

        private void RefreshSensorList()
        {
            if (_sensorListView != null)
            {
                var items = new List<string>();
                foreach (var sensor in _sensors)
                {
                    items.Add($"{sensor.Name} ({sensor.Type}) - Range:{sensor.Range:F1}m - {sensor.PowerConsumption:F0}mW");
                }
                _sensorListView.itemsSource = items;
                _sensorListView.Rebuild();
            }
        }

        private void RefreshActuatorList()
        {
            if (_actuatorListView != null)
            {
                var items = new List<string>();
                foreach (var actuator in _actuators)
                {
                    items.Add($"{actuator.Name} ({actuator.Type}) - Force:{actuator.MaxForce:F1}N - {actuator.PowerConsumption:F0}mW");
                }
                _actuatorListView.itemsSource = items;
                _actuatorListView.Rebuild();
            }
        }

        // Save/Load/Build
        public void SaveConfiguration(string path)
        {
            try
            {
                RobotConfigurationData data = new RobotConfigurationData
                {
                    Config = _currentConfig,
                    Components = _components,
                    Sensors = _sensors,
                    Actuators = _actuators
                };

                string json = JsonUtility.ToJson(data, true);
                System.IO.File.WriteAllText(path, json);
                Debug.Log($"[RobotEditor] Configuration saved to {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RobotEditor] Failed to save: {ex.Message}");
            }
        }

        public void LoadConfiguration(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    string json = System.IO.File.ReadAllText(path);
                    RobotConfigurationData data = JsonUtility.FromJson<RobotConfigurationData>(json);
                    _currentConfig = data.Config;
                    _components = data.Components;
                    _sensors = data.Sensors;
                    _actuators = data.Actuators;
                    PopulateUI();
                    Debug.Log($"[RobotEditor] Configuration loaded from {path}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RobotEditor] Failed to load: {ex.Message}");
            }
        }

        private void BuildRobot()
        {
            Debug.Log("[RobotEditor] Building robot...");
            Debug.Log($"  Vehicle: {_currentConfig.VehicleType} - {_currentConfig.VehicleName}");
            Debug.Log($"  Board: {_currentConfig.BoardType} (Size: {_currentConfig.BoardSize:F2}x)");
            Debug.Log($"  Components: {_components.Count}");
            Debug.Log($"  Sensors: {_sensors.Count}");
            Debug.Log($"  Actuators: {_actuators.Count}");
            Debug.Log($"  Power: {CalculateTotalPowerConsumption():F0}mW");
            Debug.Log($"  Runtime: {(_currentConfig.BatteryCapacity * 3.7f / CalculateTotalPowerConsumption()):F1}h");
            Debug.Log($"  Miniaturization: {_currentConfig.MiniaturizationLevel * 100f:F0}%");
            Debug.Log($"  Optimization: {_currentConfig.OptimizationLevel * 100f:F0}%");

            // TODO: Generate robot prefab
            // TODO: Load into simulation scene
        }
    }
}
