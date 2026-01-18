using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.UI.CircuitEditor
{
    /// <summary>
    /// Circuit Editor Controller - Advanced Circuit Design & Analysis
    /// Design circuits with optimization, power analysis, thermal simulation
    /// Integrates with World Editor temperature effects
    /// </summary>
    public class CircuitEditorController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _root;

        // Component Library
        private ListView _componentLibraryView;
        private TextField _componentSearchField;
        private DropdownField _componentCategoryDropdown;
        private List<CircuitComponent> _componentLibrary = new List<CircuitComponent>();

        // Circuit Canvas
        private VisualElement _circuitCanvas;
        private List<CircuitComponent> _placedComponents = new List<CircuitComponent>();
        private List<CircuitConnection> _connections = new List<CircuitConnection>();
        private Button _addWireButton;
        private Button _deleteSelectedButton;
        private Button _clearCircuitButton;

        // Circuit Analysis
        private Label _totalResistanceLabel;
        private Label _totalCapacitanceLabel;
        private Label _totalPowerLabel;
        private Label _maxVoltageLabel;
        private Label _maxCurrentLabel;
        private Button _analyzeCircuitButton;
        private Button _optimizeCircuitButton;

        // Power Management
        private Slider _supplyVoltageSlider;
        private Label _supplyVoltageLabel;
        private Slider _maxCurrentLimitSlider;
        private Label _maxCurrentLimitLabel;
        private Label _powerConsumptionLabel;
        private Label _efficiencyLabel;

        // Thermal Simulation
        private Slider _ambientTemperatureSlider;
        private Label _ambientTemperatureLabel;
        private Label _maxComponentTempLabel;
        private Label _thermalStatusLabel;
        private Toggle _enableThermalSimToggle;

        // Optimization
        private Slider _optimizationTargetSlider;
        private Label _optimizationTargetLabel;
        private DropdownField _optimizationGoalDropdown;
        private Button _runOptimizationButton;
        private Label _optimizationResultLabel;

        // Circuit Solver Integration
        private CircuitSolver _solver;
        private CircuitAnalyzer _analyzer;
        private CircuitOptimizer _optimizer;

        // Current Configuration
        private CircuitConfiguration _currentConfig;

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null)
            {
                Debug.LogError("[CircuitEditor] Missing UIDocument");
                return;
            }

            _root = _doc.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError("[CircuitEditor] rootVisualElement is null");
                return;
            }

            _solver = new CircuitSolver();
            _analyzer = new CircuitAnalyzer();
            _optimizer = new CircuitOptimizer();

            InitializeCurrentConfig();
            InitializeComponentLibrary();
            BindUIElements();
            PopulateUI();
            RegisterCallbacks();
        }

        private void InitializeCurrentConfig()
        {
            _currentConfig = new CircuitConfiguration
            {
                SupplyVoltage = 5.0f,
                MaxCurrentLimit = 1.0f,
                AmbientTemperature = 25f,
                EnableThermalSimulation = false,
                OptimizationGoal = OptimizationGoal.MinimizePower,
                OptimizationTarget = 0.8f
            };
        }

        private void InitializeComponentLibrary()
        {
            // Resistors
            _componentLibrary.Add(new CircuitComponent { Name = "10Ω Resistor", Type = ComponentType.Resistor, Value = 10f, PowerRating = 0.25f, Category = "Resistors" });
            _componentLibrary.Add(new CircuitComponent { Name = "100Ω Resistor", Type = ComponentType.Resistor, Value = 100f, PowerRating = 0.25f, Category = "Resistors" });
            _componentLibrary.Add(new CircuitComponent { Name = "1kΩ Resistor", Type = ComponentType.Resistor, Value = 1000f, PowerRating = 0.25f, Category = "Resistors" });
            _componentLibrary.Add(new CircuitComponent { Name = "10kΩ Resistor", Type = ComponentType.Resistor, Value = 10000f, PowerRating = 0.25f, Category = "Resistors" });

            // Capacitors
            _componentLibrary.Add(new CircuitComponent { Name = "10µF Capacitor", Type = ComponentType.Capacitor, Value = 0.00001f, VoltageRating = 25f, Category = "Capacitors" });
            _componentLibrary.Add(new CircuitComponent { Name = "100µF Capacitor", Type = ComponentType.Capacitor, Value = 0.0001f, VoltageRating = 25f, Category = "Capacitors" });
            _componentLibrary.Add(new CircuitComponent { Name = "1000µF Capacitor", Type = ComponentType.Capacitor, Value = 0.001f, VoltageRating = 50f, Category = "Capacitors" });

            // Inductors
            _componentLibrary.Add(new CircuitComponent { Name = "10µH Inductor", Type = ComponentType.Inductor, Value = 0.00001f, CurrentRating = 1f, Category = "Inductors" });
            _componentLibrary.Add(new CircuitComponent { Name = "100µH Inductor", Type = ComponentType.Inductor, Value = 0.0001f, CurrentRating = 1f, Category = "Inductors" });

            // Transistors
            _componentLibrary.Add(new CircuitComponent { Name = "NPN Transistor", Type = ComponentType.Transistor, Value = 100f, PowerRating = 0.5f, Category = "Transistors" });
            _componentLibrary.Add(new CircuitComponent { Name = "PNP Transistor", Type = ComponentType.Transistor, Value = 100f, PowerRating = 0.5f, Category = "Transistors" });
            _componentLibrary.Add(new CircuitComponent { Name = "MOSFET N-Channel", Type = ComponentType.MOSFET, Value = 0.1f, PowerRating = 2f, Category = "Transistors" });

            // Diodes
            _componentLibrary.Add(new CircuitComponent { Name = "1N4148 Diode", Type = ComponentType.Diode, Value = 0.7f, CurrentRating = 0.2f, Category = "Diodes" });
            _componentLibrary.Add(new CircuitComponent { Name = "1N4007 Power Diode", Type = ComponentType.Diode, Value = 0.7f, CurrentRating = 1f, Category = "Diodes" });
            _componentLibrary.Add(new CircuitComponent { Name = "LED Red", Type = ComponentType.LED, Value = 1.8f, CurrentRating = 0.02f, Category = "Diodes" });

            // ICs
            _componentLibrary.Add(new CircuitComponent { Name = "LM358 Op-Amp", Type = ComponentType.OpAmp, Value = 1000000f, PowerRating = 0.5f, Category = "ICs" });
            _componentLibrary.Add(new CircuitComponent { Name = "LM7805 Regulator", Type = ComponentType.VoltageRegulator, Value = 5f, CurrentRating = 1f, Category = "ICs" });
            _componentLibrary.Add(new CircuitComponent { Name = "555 Timer", Type = ComponentType.Timer, Value = 0f, PowerRating = 0.6f, Category = "ICs" });

            // Power
            _componentLibrary.Add(new CircuitComponent { Name = "Battery 9V", Type = ComponentType.Battery, Value = 9f, PowerRating = 5f, Category = "Power" });
            _componentLibrary.Add(new CircuitComponent { Name = "Power Supply 5V", Type = ComponentType.PowerSupply, Value = 5f, PowerRating = 10f, Category = "Power" });
        }

        private void BindUIElements()
        {
            // Component Library
            _componentLibraryView = _root.Q<ListView>("ComponentLibraryView");
            _componentSearchField = _root.Q<TextField>("ComponentSearchField");
            _componentCategoryDropdown = _root.Q<DropdownField>("ComponentCategoryDropdown");

            // Circuit Canvas
            _circuitCanvas = _root.Q<VisualElement>("CircuitCanvas");
            _addWireButton = _root.Q<Button>("AddWireButton");
            _deleteSelectedButton = _root.Q<Button>("DeleteSelectedButton");
            _clearCircuitButton = _root.Q<Button>("ClearCircuitButton");

            // Analysis
            _totalResistanceLabel = _root.Q<Label>("TotalResistanceLabel");
            _totalCapacitanceLabel = _root.Q<Label>("TotalCapacitanceLabel");
            _totalPowerLabel = _root.Q<Label>("TotalPowerLabel");
            _maxVoltageLabel = _root.Q<Label>("MaxVoltageLabel");
            _maxCurrentLabel = _root.Q<Label>("MaxCurrentLabel");
            _analyzeCircuitButton = _root.Q<Button>("AnalyzeCircuitButton");
            _optimizeCircuitButton = _root.Q<Button>("OptimizeCircuitButton");

            // Power Management
            _supplyVoltageSlider = _root.Q<Slider>("SupplyVoltageSlider");
            _supplyVoltageLabel = _root.Q<Label>("SupplyVoltageLabel");
            _maxCurrentLimitSlider = _root.Q<Slider>("MaxCurrentLimitSlider");
            _maxCurrentLimitLabel = _root.Q<Label>("MaxCurrentLimitLabel");
            _powerConsumptionLabel = _root.Q<Label>("PowerConsumptionLabel");
            _efficiencyLabel = _root.Q<Label>("EfficiencyLabel");

            // Thermal
            _ambientTemperatureSlider = _root.Q<Slider>("AmbientTemperatureSlider");
            _ambientTemperatureLabel = _root.Q<Label>("AmbientTemperatureLabel");
            _maxComponentTempLabel = _root.Q<Label>("MaxComponentTempLabel");
            _thermalStatusLabel = _root.Q<Label>("ThermalStatusLabel");
            _enableThermalSimToggle = _root.Q<Toggle>("EnableThermalSimToggle");

            // Optimization
            _optimizationTargetSlider = _root.Q<Slider>("OptimizationTargetSlider");
            _optimizationTargetLabel = _root.Q<Label>("OptimizationTargetLabel");
            _optimizationGoalDropdown = _root.Q<DropdownField>("OptimizationGoalDropdown");
            _runOptimizationButton = _root.Q<Button>("RunOptimizationButton");
            _optimizationResultLabel = _root.Q<Label>("OptimizationResultLabel");
        }

        private void PopulateUI()
        {
            // Component categories
            if (_componentCategoryDropdown != null)
            {
                _componentCategoryDropdown.choices = new List<string> { "All", "Resistors", "Capacitors", "Inductors", "Transistors", "Diodes", "ICs", "Power" };
                _componentCategoryDropdown.value = "All";
            }

            // Supply voltage
            if (_supplyVoltageSlider != null)
            {
                _supplyVoltageSlider.lowValue = 1.5f;
                _supplyVoltageSlider.highValue = 24f;
                _supplyVoltageSlider.value = _currentConfig.SupplyVoltage;
                UpdateSupplyVoltageLabel();
            }

            // Max current
            if (_maxCurrentLimitSlider != null)
            {
                _maxCurrentLimitSlider.lowValue = 0.1f;
                _maxCurrentLimitSlider.highValue = 10f;
                _maxCurrentLimitSlider.value = _currentConfig.MaxCurrentLimit;
                UpdateMaxCurrentLimitLabel();
            }

            // Ambient temperature
            if (_ambientTemperatureSlider != null)
            {
                _ambientTemperatureSlider.lowValue = -20f;
                _ambientTemperatureSlider.highValue = 80f;
                _ambientTemperatureSlider.value = _currentConfig.AmbientTemperature;
                UpdateAmbientTemperatureLabel();
            }

            // Optimization goal
            if (_optimizationGoalDropdown != null)
            {
                _optimizationGoalDropdown.choices = new List<string> { "Minimize Power", "Minimize Cost", "Maximize Efficiency", "Minimize Size" };
                _optimizationGoalDropdown.value = "Minimize Power";
            }

            // Optimization target
            if (_optimizationTargetSlider != null)
            {
                _optimizationTargetSlider.lowValue = 0f;
                _optimizationTargetSlider.highValue = 1f;
                _optimizationTargetSlider.value = _currentConfig.OptimizationTarget;
                UpdateOptimizationTargetLabel();
            }

            RefreshComponentLibrary();
        }

        private void RegisterCallbacks()
        {
            // Power callbacks
            if (_supplyVoltageSlider != null)
                _supplyVoltageSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.SupplyVoltage = evt.newValue;
                    UpdateSupplyVoltageLabel();
                    UpdatePowerAnalysis();
                });

            if (_maxCurrentLimitSlider != null)
                _maxCurrentLimitSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.MaxCurrentLimit = evt.newValue;
                    UpdateMaxCurrentLimitLabel();
                    UpdatePowerAnalysis();
                });

            // Thermal callbacks
            if (_ambientTemperatureSlider != null)
                _ambientTemperatureSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.AmbientTemperature = evt.newValue;
                    UpdateAmbientTemperatureLabel();
                    if (_currentConfig.EnableThermalSimulation)
                        RunThermalSimulation();
                });

            if (_enableThermalSimToggle != null)
                _enableThermalSimToggle.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.EnableThermalSimulation = evt.newValue;
                    if (evt.newValue)
                        RunThermalSimulation();
                });

            // Optimization callbacks
            if (_optimizationTargetSlider != null)
                _optimizationTargetSlider.RegisterValueChangedCallback(evt =>
                {
                    _currentConfig.OptimizationTarget = evt.newValue;
                    UpdateOptimizationTargetLabel();
                });

            // Button callbacks
            if (_analyzeCircuitButton != null)
                _analyzeCircuitButton.clicked += AnalyzeCircuit;

            if (_optimizeCircuitButton != null)
                _optimizeCircuitButton.clicked += OptimizeCircuit;

            if (_runOptimizationButton != null)
                _runOptimizationButton.clicked += RunFullOptimization;

            if (_clearCircuitButton != null)
                _clearCircuitButton.clicked += ClearCircuit;

            // Component search
            if (_componentSearchField != null)
                _componentSearchField.RegisterValueChangedCallback(evt => RefreshComponentLibrary());

            if (_componentCategoryDropdown != null)
                _componentCategoryDropdown.RegisterValueChangedCallback(evt => RefreshComponentLibrary());

            // Top bar buttons
            var saveButton = _root.Q<Button>("SaveCircuitButton");
            if (saveButton != null)
                saveButton.clicked += () => SaveConfiguration("CircuitConfig.json");

            var loadButton = _root.Q<Button>("LoadCircuitButton");
            if (loadButton != null)
                loadButton.clicked += () => LoadConfiguration("CircuitConfig.json");

            var backButton = _root.Q<Button>("BackButton");
            if (backButton != null)
                backButton.clicked += () => UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        // Label Updates
        private void UpdateSupplyVoltageLabel()
        {
            if (_supplyVoltageLabel != null)
                _supplyVoltageLabel.text = $"Supply: {_currentConfig.SupplyVoltage:F1}V";
        }

        private void UpdateMaxCurrentLimitLabel()
        {
            if (_maxCurrentLimitLabel != null)
                _maxCurrentLimitLabel.text = $"Max Current: {_currentConfig.MaxCurrentLimit:F2}A";
        }

        private void UpdateAmbientTemperatureLabel()
        {
            if (_ambientTemperatureLabel != null)
                _ambientTemperatureLabel.text = $"Ambient: {_currentConfig.AmbientTemperature:F1}°C";
        }

        private void UpdateOptimizationTargetLabel()
        {
            if (_optimizationTargetLabel != null)
            {
                int percentage = Mathf.RoundToInt(_currentConfig.OptimizationTarget * 100f);
                _optimizationTargetLabel.text = $"Target: {percentage}%";
            }
        }

        // Component Library Management
        private void RefreshComponentLibrary()
        {
            if (_componentLibraryView == null) return;

            string searchTerm = _componentSearchField?.value ?? "";
            string category = _componentCategoryDropdown?.value ?? "All";

            var filtered = _componentLibrary.FindAll(comp =>
            {
                bool matchesSearch = string.IsNullOrEmpty(searchTerm) || comp.Name.ToLower().Contains(searchTerm.ToLower());
                bool matchesCategory = category == "All" || comp.Category == category;
                return matchesSearch && matchesCategory;
            });

            var items = new List<string>();
            foreach (var comp in filtered)
            {
                items.Add($"{comp.Name} - {FormatComponentValue(comp)}");
            }

            _componentLibraryView.itemsSource = items;
            _componentLibraryView.Rebuild();
        }

        private string FormatComponentValue(CircuitComponent comp)
        {
            switch (comp.Type)
            {
                case ComponentType.Resistor:
                    return $"{FormatResistance(comp.Value)} {comp.PowerRating}W";
                case ComponentType.Capacitor:
                    return $"{FormatCapacitance(comp.Value)} {comp.VoltageRating}V";
                case ComponentType.Inductor:
                    return $"{FormatInductance(comp.Value)} {comp.CurrentRating}A";
                case ComponentType.Battery:
                case ComponentType.PowerSupply:
                    return $"{comp.Value}V {comp.PowerRating}W";
                default:
                    return $"{comp.PowerRating}W";
            }
        }

        private string FormatResistance(float ohms)
        {
            if (ohms >= 1000000) return $"{ohms / 1000000:F1}MΩ";
            if (ohms >= 1000) return $"{ohms / 1000:F1}kΩ";
            return $"{ohms:F0}Ω";
        }

        private string FormatCapacitance(float farads)
        {
            if (farads >= 0.001) return $"{farads * 1000:F1}mF";
            if (farads >= 0.000001) return $"{farads * 1000000:F1}µF";
            if (farads >= 0.000000001) return $"{farads * 1000000000:F1}nF";
            return $"{farads * 1000000000000:F1}pF";
        }

        private string FormatInductance(float henries)
        {
            if (henries >= 0.001) return $"{henries * 1000:F1}mH";
            if (henries >= 0.000001) return $"{henries * 1000000:F1}µH";
            return $"{henries * 1000000000:F1}nH";
        }

        // Circuit Analysis
        private void AnalyzeCircuit()
        {
            if (_placedComponents.Count == 0)
            {
                Debug.LogWarning("[CircuitEditor] No components placed");
                return;
            }

            CircuitAnalysisResult result = _analyzer.Analyze(_placedComponents, _connections, _currentConfig);

            // Update UI labels
            if (_totalResistanceLabel != null)
                _totalResistanceLabel.text = $"Total R: {FormatResistance(result.TotalResistance)}";

            if (_totalCapacitanceLabel != null)
                _totalCapacitanceLabel.text = $"Total C: {FormatCapacitance(result.TotalCapacitance)}";

            if (_totalPowerLabel != null)
                _totalPowerLabel.text = $"Total Power: {result.TotalPower:F3}W";

            if (_maxVoltageLabel != null)
                _maxVoltageLabel.text = $"Max Voltage: {result.MaxVoltage:F2}V";

            if (_maxCurrentLabel != null)
                _maxCurrentLabel.text = $"Max Current: {result.MaxCurrent:F3}A";

            if (_powerConsumptionLabel != null)
                _powerConsumptionLabel.text = $"Consumption: {result.PowerConsumption:F3}W";

            if (_efficiencyLabel != null)
                _efficiencyLabel.text = $"Efficiency: {result.Efficiency * 100f:F1}%";

            Debug.Log($"[CircuitEditor] Circuit analyzed: {result.TotalPower:F3}W, {result.Efficiency * 100f:F1}% efficient");
        }

        private void UpdatePowerAnalysis()
        {
            // Automatically reanalyze on power setting changes
            if (_placedComponents.Count > 0)
                AnalyzeCircuit();
        }

        // Thermal Simulation
        private void RunThermalSimulation()
        {
            if (_placedComponents.Count == 0) return;

            float maxTemp = _currentConfig.AmbientTemperature;

            foreach (var comp in _placedComponents)
            {
                float power = comp.PowerConsumption;
                float thermalResistance = 50f; // °C/W (simplified)
                float componentTemp = _currentConfig.AmbientTemperature + (power * thermalResistance);

                comp.CurrentTemperature = componentTemp;
                if (componentTemp > maxTemp)
                    maxTemp = componentTemp;
            }

            if (_maxComponentTempLabel != null)
                _maxComponentTempLabel.text = $"Max Temp: {maxTemp:F1}°C";

            if (_thermalStatusLabel != null)
            {
                if (maxTemp > 85f)
                    _thermalStatusLabel.text = "Status: OVERHEATING";
                else if (maxTemp > 70f)
                    _thermalStatusLabel.text = "Status: High Temperature";
                else
                    _thermalStatusLabel.text = "Status: Normal";
            }

            Debug.Log($"[CircuitEditor] Thermal simulation: Max temp {maxTemp:F1}°C");
        }

        // Circuit Optimization
        private void OptimizeCircuit()
        {
            if (_placedComponents.Count == 0)
            {
                Debug.LogWarning("[CircuitEditor] No components to optimize");
                return;
            }

            // Run basic optimization: reduce power consumption
            foreach (var comp in _placedComponents)
            {
                if (comp.Type == ComponentType.Resistor)
                {
                    // Increase resistance to reduce current
                    comp.Value *= 1.2f;
                }
            }

            AnalyzeCircuit();
            Debug.Log("[CircuitEditor] Circuit optimized");
        }

        private void RunFullOptimization()
        {
            if (_placedComponents.Count == 0)
            {
                Debug.LogWarning("[CircuitEditor] No components to optimize");
                return;
            }

            OptimizationResult result = _optimizer.Optimize(_placedComponents, _connections, _currentConfig);

            if (_optimizationResultLabel != null)
                _optimizationResultLabel.text = $"Optimization: {result.PowerReduction * 100f:F1}% power reduction, {result.ComponentsReplaced} components replaced";

            AnalyzeCircuit();
            Debug.Log($"[CircuitEditor] Full optimization: {result.PowerReduction * 100f:F1}% power reduction");
        }

        private void ClearCircuit()
        {
            _placedComponents.Clear();
            _connections.Clear();

            // Reset UI
            if (_totalResistanceLabel != null) _totalResistanceLabel.text = "Total R: 0Ω";
            if (_totalCapacitanceLabel != null) _totalCapacitanceLabel.text = "Total C: 0F";
            if (_totalPowerLabel != null) _totalPowerLabel.text = "Total Power: 0W";

            Debug.Log("[CircuitEditor] Circuit cleared");
        }

        // Save/Load
        public void SaveConfiguration(string path)
        {
            try
            {
                CircuitConfigurationData data = new CircuitConfigurationData
                {
                    Config = _currentConfig,
                    Components = _placedComponents,
                    Connections = _connections
                };

                string json = JsonUtility.ToJson(data, true);
                System.IO.File.WriteAllText(path, json);
                Debug.Log($"[CircuitEditor] Configuration saved to {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CircuitEditor] Failed to save: {ex.Message}");
            }
        }

        public void LoadConfiguration(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    string json = System.IO.File.ReadAllText(path);
                    CircuitConfigurationData data = JsonUtility.FromJson<CircuitConfigurationData>(json);
                    _currentConfig = data.Config;
                    _placedComponents = data.Components;
                    _connections = data.Connections;
                    PopulateUI();
                    AnalyzeCircuit();
                    Debug.Log($"[CircuitEditor] Configuration loaded from {path}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CircuitEditor] Failed to load: {ex.Message}");
            }
        }
    }

    // Data Structures
    [Serializable]
    public class CircuitConfiguration
    {
        public float SupplyVoltage;
        public float MaxCurrentLimit;
        public float AmbientTemperature;
        public bool EnableThermalSimulation;
        public OptimizationGoal OptimizationGoal;
        public float OptimizationTarget;
        public float SupplyCurrent;
    }

    [Serializable]
    public class CircuitConfigurationData
    {
        public CircuitConfiguration Config;
        public List<CircuitComponent> Components;
        public List<CircuitConnection> Connections;
    }

    [Serializable]
    public class CircuitComponent
    {
        public string Id;
        public string Name;
        public ComponentType Type;
        public float Value;
        public float PowerRating;
        public float VoltageRating;
        public float CurrentRating;
        public string Category;
        public float PowerConsumption;
        public float CurrentTemperature;
        public Vector2 Position;

        // Extended properties
        public float Resistance;
        public float RatedCurrent;
        public string PositiveNode;
        public string NegativeNode;
        public float ThermalResistance;
        public float MaxTemperature;
    }

    [Serializable]
    public class CircuitConnection
    {
        public int FromComponentIndex;
        public int ToComponentIndex;
        public ConnectionType Type;

        // Extended properties
        public string FromNode;
        public string ToNode;
        public float Length;
    }

    [Serializable]
    public struct CircuitAnalysisResult
    {
        public float TotalResistance;
        public float TotalCapacitance;
        public float TotalPower;
        public float MaxVoltage;
        public float MaxCurrent;
        public float PowerConsumption;
        public float Efficiency;

        // Extended analysis properties
        public float Timestamp;
        public Dictionary<string, float> NodeVoltages;
        public Dictionary<string, float> ComponentCurrents;
        public float TotalPowerDissipation;
        public Dictionary<string, float> ComponentPowers;
        public Dictionary<string, ComponentThermal> ThermalMap;
        public float MaxTemperature;
        public string HottestComponent;
        public bool IsThermalSafe;
        public float SignalIntegrityScore;
        public Dictionary<string, float> ComponentStress;
        public List<string> OverstressedComponents;
        public float CircuitHealthScore;
        public bool IsCircuitSafe;
        public List<string> Recommendations;
    }

    [Serializable]
    public struct OptimizationResult
    {
        public float PowerReduction;
        public int ComponentsReplaced;
        public float CostReduction;
    }

    // Enums
    public enum ComponentType
    {
        Resistor,
        Capacitor,
        Inductor,
        Transistor,
        MOSFET,
        Diode,
        LED,
        OpAmp,
        VoltageRegulator,
        Timer,
        Battery,
        PowerSupply,
        Motor,
        Servo
    }

    public enum ConnectionType
    {
        Wire,
        Series,
        Parallel,
        Signal
    }

    public enum OptimizationGoal
    {
        MinimizePower,
        MinimizeCost,
        MaximizeEfficiency,
        MinimizeSize
    }

    /// <summary>
    /// Circuit Solver - Integrate with NativeEngine circuit solver
    /// </summary>
    public class CircuitSolver
    {
        public void SolveCircuit(List<CircuitComponent> components, List<CircuitConnection> connections)
        {
            // TODO: Integrate with NativeEngine CircuitSolver (0.0085µs/solve)
            Debug.Log("[CircuitSolver] Solving circuit...");
        }
    }

    /// <summary>
    /// Circuit Analyzer - Advanced Power/Thermal/Signal Analysis
    /// - Nodal voltage analysis (Kirchhoff's Current Law)
    /// - Power dissipation and thermal modeling
    /// - Signal integrity checks
    /// - Component stress analysis
    /// </summary>
    public class CircuitAnalyzer
    {
        private Dictionary<string, NodeVoltage> _nodeVoltages = new Dictionary<string, NodeVoltage>();
        private Dictionary<string, ComponentThermal> _thermalMap = new Dictionary<string, ComponentThermal>();

        public CircuitAnalysisResult Analyze(List<CircuitComponent> components, List<CircuitConnection> connections, CircuitConfiguration config)
        {
            CircuitAnalysisResult result = new CircuitAnalysisResult();
            result.Timestamp = (float)DateTime.Now.TimeOfDay.TotalSeconds;

            // Build circuit graph
            var circuitGraph = BuildCircuitGraph(components, connections);

            // Nodal analysis - solve for voltages at each node
            result.NodeVoltages = SolveNodalAnalysis(circuitGraph, config.SupplyVoltage);

            // Calculate currents through each component
            result.ComponentCurrents = CalculateComponentCurrents(components, connections, result.NodeVoltages);

            // Power analysis
            result.TotalPowerDissipation = 0f;
            result.ComponentPowers = new Dictionary<string, float>();

            foreach (var comp in components)
            {
                float voltage = GetComponentVoltage(comp, result.NodeVoltages);
                float current = result.ComponentCurrents.ContainsKey(comp.Id) ? result.ComponentCurrents[comp.Id] : 0f;
                float power = voltage * current;

                result.ComponentPowers[comp.Id] = power;
                result.TotalPowerDissipation += power;
            }

            // Thermal analysis - calculate component temperatures
            result.ThermalMap = AnalyzeThermal(components, result.ComponentPowers, config.AmbientTemperature);
            result.MaxTemperature = 0f;
            result.HottestComponent = "";

            foreach (var thermal in result.ThermalMap.Values)
            {
                if (thermal.Temperature > result.MaxTemperature)
                {
                    result.MaxTemperature = thermal.Temperature;
                    result.HottestComponent = thermal.ComponentId;
                }
            }

            result.IsThermalSafe = result.MaxTemperature < 85f; // 85°C typical limit

            // Efficiency calculation
            float inputPower = config.SupplyVoltage * config.SupplyCurrent;
            float usefulPower = CalculateUsefulPower(components, result.ComponentPowers);
            result.Efficiency = inputPower > 0 ? (usefulPower / inputPower) : 0f;

            // Signal integrity analysis
            result.SignalIntegrityScore = AnalyzeSignalIntegrity(connections, result.NodeVoltages);

            // Stress analysis - check component ratings
            result.ComponentStress = AnalyzeComponentStress(components, result.ComponentCurrents, result.ThermalMap);
            result.OverstressedComponents = result.ComponentStress.Where(kvp => kvp.Value > 0.8f).Select(kvp => kvp.Key).ToList();

            // Overall health score
            result.CircuitHealthScore = CalculateHealthScore(result);
            result.IsCircuitSafe = result.CircuitHealthScore > 0.7f && result.OverstressedComponents.Count == 0;

            // Recommendations
            result.Recommendations = GenerateRecommendations(result);

            return result;
        }

        private Dictionary<string, List<string>> BuildCircuitGraph(List<CircuitComponent> components, List<CircuitConnection> connections)
        {
            var graph = new Dictionary<string, List<string>>();

            foreach (var connection in connections)
            {
                if (!graph.ContainsKey(connection.FromNode))
                    graph[connection.FromNode] = new List<string>();
                if (!graph.ContainsKey(connection.ToNode))
                    graph[connection.ToNode] = new List<string>();

                graph[connection.FromNode].Add(connection.ToNode);
                graph[connection.ToNode].Add(connection.FromNode);
            }

            return graph;
        }

        private Dictionary<string, float> SolveNodalAnalysis(Dictionary<string, List<string>> graph, float supplyVoltage)
        {
            // Simplified nodal analysis (real implementation would use matrix solver)
            var voltages = new Dictionary<string, float>();

            // Ground node
            voltages["GND"] = 0f;

            // Supply node
            voltages["VCC"] = supplyVoltage;

            // Iteratively solve for intermediate nodes (simplified)
            int maxIterations = 100;
            for (int iter = 0; iter < maxIterations; iter++)
            {
                bool converged = true;

                foreach (var node in graph.Keys)
                {
                    if (node == "GND" || node == "VCC")
                        continue;

                    // Average voltage of neighbors (simple approximation)
                    float sum = 0f;
                    int count = 0;

                    foreach (var neighbor in graph[node])
                    {
                        if (voltages.ContainsKey(neighbor))
                        {
                            sum += voltages[neighbor];
                            count++;
                        }
                    }

                    float newVoltage = count > 0 ? sum / count : 0f;

                    if (!voltages.ContainsKey(node))
                    {
                        voltages[node] = newVoltage;
                        converged = false;
                    }
                    else if (Math.Abs(voltages[node] - newVoltage) > 0.01f)
                    {
                        voltages[node] = newVoltage;
                        converged = false;
                    }
                }

                if (converged)
                    break;
            }

            return voltages;
        }

        private Dictionary<string, float> CalculateComponentCurrents(List<CircuitComponent> components, List<CircuitConnection> connections, Dictionary<string, float> nodeVoltages)
        {
            var currents = new Dictionary<string, float>();

            foreach (var comp in components)
            {
                float voltage = GetComponentVoltage(comp, nodeVoltages);

                // Ohm's law: I = V / R
                if (comp.Resistance > 0)
                {
                    currents[comp.Id] = voltage / comp.Resistance;
                }
                else
                {
                    currents[comp.Id] = comp.RatedCurrent;
                }
            }

            return currents;
        }

        private float GetComponentVoltage(CircuitComponent comp, Dictionary<string, float> nodeVoltages)
        {
            float vPositive = nodeVoltages.ContainsKey(comp.PositiveNode) ? nodeVoltages[comp.PositiveNode] : 0f;
            float vNegative = nodeVoltages.ContainsKey(comp.NegativeNode) ? nodeVoltages[comp.NegativeNode] : 0f;
            return Math.Abs(vPositive - vNegative);
        }

        private Dictionary<string, ComponentThermal> AnalyzeThermal(List<CircuitComponent> components, Dictionary<string, float> componentPowers, float ambientTemp)
        {
            var thermalMap = new Dictionary<string, ComponentThermal>();

            foreach (var comp in components)
            {
                float power = componentPowers.ContainsKey(comp.Id) ? componentPowers[comp.Id] : 0f;

                // Temperature rise: ΔT = P * θ_JA (thermal resistance)
                float thermalResistance = comp.ThermalResistance; // K/W
                float tempRise = power * thermalResistance;
                float temperature = ambientTemp + tempRise;

                thermalMap[comp.Id] = new ComponentThermal
                {
                    ComponentId = comp.Id,
                    PowerDissipation = power,
                    Temperature = temperature,
                    ThermalResistance = thermalResistance,
                    IsOverheating = temperature > comp.MaxTemperature
                };
            }

            return thermalMap;
        }

        private float CalculateUsefulPower(List<CircuitComponent> components, Dictionary<string, float> componentPowers)
        {
            float usefulPower = 0f;

            foreach (var comp in components)
            {
                // Consider motors, LEDs, etc. as useful load
                if (comp.Type == ComponentType.Motor || comp.Type == ComponentType.LED || comp.Type == ComponentType.Servo)
                {
                    if (componentPowers.ContainsKey(comp.Id))
                        usefulPower += componentPowers[comp.Id];
                }
            }

            return usefulPower;
        }

        private float AnalyzeSignalIntegrity(List<CircuitConnection> connections, Dictionary<string, float> nodeVoltages)
        {
            float score = 1.0f;

            foreach (var connection in connections)
            {
                // Check for excessive voltage drop
                float vFrom = nodeVoltages.ContainsKey(connection.FromNode) ? nodeVoltages[connection.FromNode] : 0f;
                float vTo = nodeVoltages.ContainsKey(connection.ToNode) ? nodeVoltages[connection.ToNode] : 0f;
                float drop = Math.Abs(vFrom - vTo);

                // Penalize large voltage drops on signal lines
                if (connection.Type == ConnectionType.Signal && drop > 0.5f)
                {
                    score -= 0.1f;
                }

                // Check wire length (signal degradation)
                if (connection.Length > 0.5f) // >50cm
                {
                    score -= 0.05f;
                }
            }

            return Math.Max(0f, Math.Min(1f, score));
        }

        private Dictionary<string, float> AnalyzeComponentStress(List<CircuitComponent> components, Dictionary<string, float> componentCurrents, Dictionary<string, ComponentThermal> thermalMap)
        {
            var stress = new Dictionary<string, float>();

            foreach (var comp in components)
            {
                float stressLevel = 0f;

                // Current stress
                float current = componentCurrents.ContainsKey(comp.Id) ? componentCurrents[comp.Id] : 0f;
                float currentStress = comp.RatedCurrent > 0 ? (current / comp.RatedCurrent) : 0f;

                // Thermal stress
                float thermalStress = 0f;
                if (thermalMap.ContainsKey(comp.Id))
                {
                    float temp = thermalMap[comp.Id].Temperature;
                    thermalStress = comp.MaxTemperature > 0 ? (temp / comp.MaxTemperature) : 0f;
                }

                // Combined stress (take maximum)
                stressLevel = Math.Max(currentStress, thermalStress);

                stress[comp.Id] = stressLevel;
            }

            return stress;
        }

        private float CalculateHealthScore(CircuitAnalysisResult result)
        {
            float score = 1.0f;

            // Thermal penalty
            if (!result.IsThermalSafe)
                score -= 0.3f;

            // Overstressed components penalty
            score -= result.OverstressedComponents.Count * 0.1f;

            // Signal integrity bonus
            score += (result.SignalIntegrityScore - 0.8f) * 0.5f;

            // Efficiency bonus
            score += (result.Efficiency - 0.7f) * 0.3f;

            return Math.Max(0f, Math.Min(1f, score));
        }

        private List<string> GenerateRecommendations(CircuitAnalysisResult result)
        {
            var recommendations = new List<string>();

            if (!result.IsThermalSafe)
            {
                recommendations.Add($"CRITICAL: {result.HottestComponent} overheating at {result.MaxTemperature:F1}°C - add heatsink or cooling");
            }

            if (result.OverstressedComponents.Count > 0)
            {
                recommendations.Add($"WARNING: {result.OverstressedComponents.Count} components overstressed - consider higher-rated parts");
            }

            if (result.Efficiency < 0.7f)
            {
                recommendations.Add($"Efficiency is low ({result.Efficiency:P1}) - optimize power distribution or use switching regulators");
            }

            if (result.SignalIntegrityScore < 0.8f)
            {
                recommendations.Add("Signal integrity issues detected - shorten wires or add termination resistors");
            }

            if (result.TotalPowerDissipation > 10f)
            {
                recommendations.Add($"High power dissipation ({result.TotalPowerDissipation:F1}W) - verify power supply capacity");
            }

            return recommendations;
        }
    }

    [Serializable]
    public class NodeVoltage
    {
        public string NodeId;
        public float Voltage;
    }

    [Serializable]
    public class ComponentThermal
    {
        public string ComponentId;
        public float PowerDissipation;
        public float Temperature;
        public float ThermalResistance;
        public bool IsOverheating;
    }

    /// <summary>
    /// Circuit Optimizer - Component replacement & layout optimization
    /// </summary>
    public class CircuitOptimizer
    {
        public OptimizationResult Optimize(List<CircuitComponent> components, List<CircuitConnection> connections, CircuitConfiguration config)
        {
            OptimizationResult result = new OptimizationResult();

            int replaced = 0;
            float originalPower = 0f, optimizedPower = 0f;

            foreach (var comp in components)
            {
                originalPower += comp.PowerConsumption;

                // Optimize based on goal
                switch (config.OptimizationGoal)
                {
                    case OptimizationGoal.MinimizePower:
                        if (comp.Type == ComponentType.Resistor && comp.PowerConsumption > 0.1f)
                        {
                            comp.Value *= 1.5f; // Increase resistance
                            comp.PowerConsumption *= 0.7f;
                            replaced++;
                        }
                        break;

                    case OptimizationGoal.MaximizeEfficiency:
                        comp.PowerConsumption *= (1f - config.OptimizationTarget * 0.3f);
                        replaced++;
                        break;
                }

                optimizedPower += comp.PowerConsumption;
            }

            result.PowerReduction = (originalPower - optimizedPower) / originalPower;
            result.ComponentsReplaced = replaced;
            result.CostReduction = result.PowerReduction * 0.5f;

            return result;
        }
    }

}

