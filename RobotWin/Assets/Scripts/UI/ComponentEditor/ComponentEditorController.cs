using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System;

namespace RobotTwin.UI.ComponentEditor
{
    /// <summary>
    /// Component Editor - Create custom robot components
    /// Parametric design with electrical, thermal, and mechanical properties
    /// </summary>
    public class ComponentEditorController : MonoBehaviour
    {
        // UI Elements
        private VisualElement _root;
        private TextField _nameField;
        private DropdownField _typeDropdown;
        private DropdownField _materialDropdown;

        // Dimensional parameters
        private Slider _widthSlider;
        private Slider _heightSlider;
        private Slider _depthSlider;
        private Slider _massSlider;

        // Electrical properties
        private TextField _voltageField;
        private TextField _currentField;
        private TextField _resistanceField;
        private TextField _capacitanceField;
        private TextField _inductanceField;

        // Thermal properties
        private Slider _thermalResistanceSlider;
        private Slider _maxTempSlider;
        private TextField _powerDissipationField;

        // Mechanical properties
        private Slider _strengthSlider;
        private TextField _elasticityField;
        private DropdownField _mountingTypeDropdown;

        // Pin configuration
        private ListView _pinListView;
        private Button _addPinButton;
        private Button _removePinButton;

        // Preview
        private VisualElement _previewContainer;
        private Label _componentStatsLabel;

        // Buttons
        private Button _saveButton;
        private Button _loadButton;
        private Button _resetButton;
        private Button _exportButton;

        // Current component being edited
        private CustomComponent _currentComponent;

        // Component library
        private List<CustomComponent> _componentLibrary = new List<CustomComponent>();

        public void Initialize(VisualElement root)
        {
            _root = root;
            InitializeUI();
            InitializeCurrentComponent();
            UpdatePreview();
        }

        private void InitializeUI()
        {
            // Basic info
            _nameField = _root.Q<TextField>("component-name");
            _typeDropdown = _root.Q<DropdownField>("component-type");
            _typeDropdown.choices = new List<string> { "Motor", "Sensor", "Actuator", "Structural", "Electronic", "Battery", "Custom" };

            _materialDropdown = _root.Q<DropdownField>("component-material");
            _materialDropdown.choices = new List<string> { "Aluminum", "Steel", "Plastic_ABS", "Plastic_PLA", "CarbonFiber", "Copper", "Brass" };

            // Dimensions
            _widthSlider = _root.Q<Slider>("width-slider");
            _widthSlider.value = 0.05f; // 5cm default
            _widthSlider.RegisterValueChangedCallback(evt => OnDimensionChanged());

            _heightSlider = _root.Q<Slider>("height-slider");
            _heightSlider.value = 0.05f;
            _heightSlider.RegisterValueChangedCallback(evt => OnDimensionChanged());

            _depthSlider = _root.Q<Slider>("depth-slider");
            _depthSlider.value = 0.03f;
            _depthSlider.RegisterValueChangedCallback(evt => OnDimensionChanged());

            _massSlider = _root.Q<Slider>("mass-slider");
            _massSlider.value = 0.1f; // 100g default
            _massSlider.RegisterValueChangedCallback(evt => UpdatePreview());

            // Electrical
            _voltageField = _root.Q<TextField>("voltage");
            _currentField = _root.Q<TextField>("current");
            _resistanceField = _root.Q<TextField>("resistance");
            _capacitanceField = _root.Q<TextField>("capacitance");
            _inductanceField = _root.Q<TextField>("inductance");

            // Thermal
            _thermalResistanceSlider = _root.Q<Slider>("thermal-resistance");
            _maxTempSlider = _root.Q<Slider>("max-temp");
            _powerDissipationField = _root.Q<TextField>("power-dissipation");

            // Mechanical
            _strengthSlider = _root.Q<Slider>("strength");
            _elasticityField = _root.Q<TextField>("elasticity");
            _mountingTypeDropdown = _root.Q<DropdownField>("mounting-type");
            _mountingTypeDropdown.choices = new List<string> { "Screw", "Snap-fit", "Adhesive", "Welded", "Bolted", "Clip" };

            // Pins
            _pinListView = _root.Q<ListView>("pin-list");
            _pinListView.makeItem = MakePinItem;
            _pinListView.bindItem = BindPinItem;

            _addPinButton = _root.Q<Button>("add-pin");
            _addPinButton.clicked += OnAddPin;

            _removePinButton = _root.Q<Button>("remove-pin");
            _removePinButton.clicked += OnRemovePin;

            // Preview
            _previewContainer = _root.Q<VisualElement>("preview-container");
            _componentStatsLabel = _root.Q<Label>("component-stats");

            // Buttons
            _saveButton = _root.Q<Button>("save-button");
            _saveButton.clicked += OnSaveComponent;

            _loadButton = _root.Q<Button>("load-button");
            _loadButton.clicked += OnLoadComponent;

            _resetButton = _root.Q<Button>("reset-button");
            _resetButton.clicked += OnResetComponent;

            _exportButton = _root.Q<Button>("export-button");
            _exportButton.clicked += OnExportComponent;
        }

        private void InitializeCurrentComponent()
        {
            _currentComponent = new CustomComponent
            {
                Name = "NewComponent",
                Type = "Custom",
                Material = "Aluminum",
                Width = 0.05f,
                Height = 0.05f,
                Depth = 0.03f,
                Mass = 0.1f,
                Voltage = 5.0f,
                Current = 0.1f,
                Resistance = 50f,
                ThermalResistance = 10f,
                MaxTemperature = 85f,
                Strength = 100f,
                Pins = new List<ComponentPin>()
            };

            // Add default pins
            _currentComponent.Pins.Add(new ComponentPin { Number = 1, Name = "VCC", Type = "Power", Direction = "Input" });
            _currentComponent.Pins.Add(new ComponentPin { Number = 2, Name = "GND", Type = "Ground", Direction = "Input" });
            _currentComponent.Pins.Add(new ComponentPin { Number = 3, Name = "Signal", Type = "Digital", Direction = "Output" });

            UpdateUIFromComponent();
        }

        private void UpdateUIFromComponent()
        {
            _nameField.value = _currentComponent.Name;
            _typeDropdown.value = _currentComponent.Type;
            _materialDropdown.value = _currentComponent.Material;

            _widthSlider.value = _currentComponent.Width;
            _heightSlider.value = _currentComponent.Height;
            _depthSlider.value = _currentComponent.Depth;
            _massSlider.value = _currentComponent.Mass;

            _voltageField.value = _currentComponent.Voltage.ToString("F2");
            _currentField.value = _currentComponent.Current.ToString("F3");
            _resistanceField.value = _currentComponent.Resistance.ToString("F1");

            _thermalResistanceSlider.value = _currentComponent.ThermalResistance;
            _maxTempSlider.value = _currentComponent.MaxTemperature;

            _strengthSlider.value = _currentComponent.Strength;

            _pinListView.itemsSource = _currentComponent.Pins;
            _pinListView.Rebuild();
        }

        private void OnDimensionChanged()
        {
            // Auto-calculate volume and mass based on material density
            float volume = _widthSlider.value * _heightSlider.value * _depthSlider.value; // m³

            // Get material density
            float density = GetMaterialDensity(_currentComponent.Material);
            float calculatedMass = volume * density; // kg

            // Update mass slider (but allow manual override)
            _massSlider.SetValueWithoutNotify(calculatedMass);

            UpdatePreview();
        }

        private float GetMaterialDensity(string material)
        {
            switch (material)
            {
                case "Aluminum": return 2700f;
                case "Steel": return 7850f;
                case "Plastic_ABS": return 1040f;
                case "Plastic_PLA": return 1240f;
                case "CarbonFiber": return 1600f;
                case "Copper": return 8960f;
                case "Brass": return 8500f;
                default: return 1000f;
            }
        }

        private void UpdatePreview()
        {
            // Update component from UI
            _currentComponent.Name = _nameField.value;
            _currentComponent.Type = _typeDropdown.value;
            _currentComponent.Material = _materialDropdown.value;
            _currentComponent.Width = _widthSlider.value;
            _currentComponent.Height = _heightSlider.value;
            _currentComponent.Depth = _depthSlider.value;
            _currentComponent.Mass = _massSlider.value;

            if (float.TryParse(_voltageField.value, out float voltage))
                _currentComponent.Voltage = voltage;
            if (float.TryParse(_currentField.value, out float current))
                _currentComponent.Current = current;
            if (float.TryParse(_resistanceField.value, out float resistance))
                _currentComponent.Resistance = resistance;

            _currentComponent.ThermalResistance = _thermalResistanceSlider.value;
            _currentComponent.MaxTemperature = _maxTempSlider.value;
            _currentComponent.Strength = _strengthSlider.value;

            // Calculate derived properties
            float volume = _currentComponent.Width * _currentComponent.Height * _currentComponent.Depth;
            float power = _currentComponent.Voltage * _currentComponent.Current;
            float surfaceArea = 2f * (_currentComponent.Width * _currentComponent.Height +
                                      _currentComponent.Width * _currentComponent.Depth +
                                      _currentComponent.Height * _currentComponent.Depth);

            float tempRise = power * _currentComponent.ThermalResistance;
            float operatingTemp = 25f + tempRise; // Assume 25°C ambient

            // Display stats
            string stats = $"Volume: {volume * 1e6f:F2} cm³\n";
            stats += $"Mass: {_currentComponent.Mass * 1000f:F1} g\n";
            stats += $"Surface Area: {surfaceArea * 1e4f:F2} cm²\n";
            stats += $"Power Consumption: {power:F2} W\n";
            stats += $"Operating Temp: {operatingTemp:F1}°C\n";
            stats += $"Thermal Safe: {(operatingTemp < _currentComponent.MaxTemperature ? "YES" : "NO")}\n";
            stats += $"Pin Count: {_currentComponent.Pins.Count}\n";
            stats += $"Estimated Cost: ${EstimateCost(_currentComponent):F2}";

            _componentStatsLabel.text = stats;

            // Render 3D preview (simplified - just show a colored box)
            RenderPreview();
        }

        private void RenderPreview()
        {
            // Clear previous preview
            _previewContainer.Clear();

            // Create simple 2D representation
            var box = new VisualElement();
            box.style.width = _currentComponent.Width * 500f; // Scale for display
            box.style.height = _currentComponent.Height * 500f;
            box.style.backgroundColor = GetMaterialColor(_currentComponent.Material);
            box.style.borderBottomWidth = 2;
            box.style.borderTopWidth = 2;
            box.style.borderLeftWidth = 2;
            box.style.borderRightWidth = 2;
            box.style.borderBottomColor = Color.black;
            box.style.borderTopColor = Color.black;
            box.style.borderLeftColor = Color.black;
            box.style.borderRightColor = Color.black;

            _previewContainer.Add(box);

            // Add pin indicators
            for (int i = 0; i < _currentComponent.Pins.Count; i++)
            {
                var pin = _currentComponent.Pins[i];
                var pinIndicator = new Label(pin.Number.ToString());
                pinIndicator.style.position = Position.Absolute;
                pinIndicator.style.left = (i * 20f) % (box.style.width.value.value - 20f);
                pinIndicator.style.top = 5f;
                pinIndicator.style.backgroundColor = GetPinColor(pin.Type);
                pinIndicator.style.borderTopLeftRadius = 10;
                pinIndicator.style.borderTopRightRadius = 10;
                pinIndicator.style.borderBottomLeftRadius = 10;
                pinIndicator.style.borderBottomRightRadius = 10;
                pinIndicator.style.width = 20;
                pinIndicator.style.height = 20;
                pinIndicator.style.unityTextAlign = TextAnchor.MiddleCenter;

                box.Add(pinIndicator);
            }
        }

        private Color GetMaterialColor(string material)
        {
            switch (material)
            {
                case "Aluminum": return new Color(0.8f, 0.8f, 0.85f);
                case "Steel": return new Color(0.6f, 0.6f, 0.65f);
                case "Plastic_ABS": return new Color(0.9f, 0.9f, 0.9f);
                case "Plastic_PLA": return new Color(0.95f, 0.95f, 1.0f);
                case "CarbonFiber": return new Color(0.1f, 0.1f, 0.1f);
                case "Copper": return new Color(0.9f, 0.5f, 0.3f);
                case "Brass": return new Color(0.9f, 0.8f, 0.3f);
                default: return Color.white;
            }
        }

        private Color GetPinColor(string type)
        {
            switch (type)
            {
                case "Power": return Color.red;
                case "Ground": return Color.black;
                case "Digital": return Color.blue;
                case "Analog": return Color.green;
                case "PWM": return Color.cyan;
                default: return Color.gray;
            }
        }

        private float EstimateCost(CustomComponent component)
        {
            // Simple cost estimation based on material and volume
            float materialCost = GetMaterialCostPerKg(component.Material);
            float baseCost = component.Mass * materialCost;

            // Add complexity cost
            float complexityCost = component.Pins.Count * 0.50f; // $0.50 per pin

            // Add manufacturing cost
            float volume = component.Width * component.Height * component.Depth;
            float manufacturingCost = volume * 1e6f * 0.01f; // $0.01 per cm³

            return baseCost + complexityCost + manufacturingCost;
        }

        private float GetMaterialCostPerKg(string material)
        {
            switch (material)
            {
                case "Aluminum": return 2.5f;
                case "Steel": return 1.0f;
                case "Plastic_ABS": return 3.0f;
                case "Plastic_PLA": return 2.5f;
                case "CarbonFiber": return 50f;
                case "Copper": return 8.0f;
                case "Brass": return 6.0f;
                default: return 5.0f;
            }
        }

        private VisualElement MakePinItem()
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.justifyContent = Justify.SpaceBetween;

            var numberLabel = new Label();
            numberLabel.name = "pin-number";
            container.Add(numberLabel);

            var nameLabel = new Label();
            nameLabel.name = "pin-name";
            container.Add(nameLabel);

            var typeLabel = new Label();
            typeLabel.name = "pin-type";
            container.Add(typeLabel);

            var directionLabel = new Label();
            directionLabel.name = "pin-direction";
            container.Add(directionLabel);

            return container;
        }

        private void BindPinItem(VisualElement element, int index)
        {
            var pin = _currentComponent.Pins[index];

            element.Q<Label>("pin-number").text = $"Pin {pin.Number}";
            element.Q<Label>("pin-name").text = pin.Name;
            element.Q<Label>("pin-type").text = pin.Type;
            element.Q<Label>("pin-direction").text = pin.Direction;
        }

        private void OnAddPin()
        {
            int nextNumber = _currentComponent.Pins.Count + 1;
            _currentComponent.Pins.Add(new ComponentPin
            {
                Number = nextNumber,
                Name = $"Pin{nextNumber}",
                Type = "Digital",
                Direction = "Input"
            });

            _pinListView.Rebuild();
            UpdatePreview();
        }

        private void OnRemovePin()
        {
            if (_pinListView.selectedIndex >= 0 && _pinListView.selectedIndex < _currentComponent.Pins.Count)
            {
                _currentComponent.Pins.RemoveAt(_pinListView.selectedIndex);
                _pinListView.Rebuild();
                UpdatePreview();
            }
        }

        private void OnSaveComponent()
        {
            // Save to library
            var clone = CloneComponent(_currentComponent);
            _componentLibrary.Add(clone);

            // Save to disk (JSON)
            string json = JsonUtility.ToJson(_currentComponent, true);
            string path = $"Components/{_currentComponent.Name}.json";
            System.IO.File.WriteAllText(path, json);

            Debug.Log($"Component saved: {_currentComponent.Name}");
        }

        private void OnLoadComponent()
        {
            // TODO: Show file picker dialog
            Debug.Log("Load component - TODO: implement file picker");
        }

        private void OnResetComponent()
        {
            InitializeCurrentComponent();
            UpdatePreview();
        }

        private void OnExportComponent()
        {
            // Export as 3D model (.obj or .stl)
            ExportAs3DModel(_currentComponent);
            Debug.Log($"Component exported: {_currentComponent.Name}");
        }

        private CustomComponent CloneComponent(CustomComponent source)
        {
            string json = JsonUtility.ToJson(source);
            return JsonUtility.FromJson<CustomComponent>(json);
        }

        private void ExportAs3DModel(CustomComponent component)
        {
            // Generate simple box mesh
            // TODO: Implement proper 3D export
            Debug.Log($"Exporting {component.Name} as 3D model...");
        }
    }

    [Serializable]
    public class CustomComponent
    {
        public string Name;
        public string Type;
        public string Material;

        // Dimensions (meters)
        public float Width;
        public float Height;
        public float Depth;
        public float Mass; // kg

        // Electrical properties
        public float Voltage;
        public float Current;
        public float Resistance;
        public float Capacitance;
        public float Inductance;

        // Thermal properties
        public float ThermalResistance; // K/W
        public float MaxTemperature; // °C

        // Mechanical properties
        public float Strength; // MPa
        public float Elasticity; // GPa
        public string MountingType;

        // Pin configuration
        public List<ComponentPin> Pins;
    }

    [Serializable]
    public class ComponentPin
    {
        public int Number;
        public string Name;
        public string Type; // "Power", "Ground", "Digital", "Analog", "PWM"
        public string Direction; // "Input", "Output", "Bidirectional"
    }
}
