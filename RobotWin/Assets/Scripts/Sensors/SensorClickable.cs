using UnityEngine;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;

namespace RobotTwin.Sensors
{
    /// <summary>
    /// Sensor Click Handler - Detects clicks on robot sensors
    /// Shows visualization when sensor is clicked
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SensorClickable : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Sensor Configuration")]
        public SensorProperties SensorProperties;

        [Header("Selection Visual")]
        [SerializeField] private Color _normalColor = Color.white;
        [SerializeField] private Color _hoverColor = Color.yellow;
        [SerializeField] private Color _selectedColor = Color.green;
        [SerializeField] private float _glowIntensity = 2f;

        private Renderer _renderer;
        private Material _originalMaterial;
        private Material _instanceMaterial;
        private bool _isSelected = false;
        private bool _isHovered = false;

        // Outline effect
        private GameObject _outlineObject;

        private void Awake()
        {
            // Get or add collider for clicking
            Collider collider = GetComponent<Collider>();
            if (collider == null)
            {
                collider = gameObject.AddComponent<BoxCollider>();
            }

            // Setup renderer
            _renderer = GetComponent<Renderer>();
            if (_renderer != null)
            {
                _originalMaterial = _renderer.material;
                _instanceMaterial = new Material(_originalMaterial);
                _renderer.material = _instanceMaterial;
            }

            // Initialize sensor properties if not set
            if (SensorProperties == null)
            {
                SensorProperties = CreateDefaultProperties();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Toggle selection
            _isSelected = !_isSelected;

            if (_isSelected)
            {
                // Show visualization
                SensorVisualizationController.Instance.ShowSensorArea(gameObject, SensorProperties);
                UpdateVisualState();
            }
            else
            {
                // Hide visualization
                SensorVisualizationController.Instance.HideSensorArea(gameObject.GetInstanceID().ToString());
                UpdateVisualState();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
            UpdateVisualState();

            // Show tooltip with sensor info
            ShowSensorTooltip();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
            UpdateVisualState();

            // Hide tooltip
            HideSensorTooltip();
        }

        private void UpdateVisualState()
        {
            if (_renderer == null || _instanceMaterial == null)
                return;

            Color targetColor = _normalColor;
            float emission = 0f;

            if (_isSelected)
            {
                targetColor = _selectedColor;
                emission = _glowIntensity;
            }
            else if (_isHovered)
            {
                targetColor = _hoverColor;
                emission = _glowIntensity * 0.5f;
            }

            _instanceMaterial.color = targetColor;

            // Enable emission for glow effect
            if (emission > 0f)
            {
                _instanceMaterial.EnableKeyword("_EMISSION");
                _instanceMaterial.SetColor("_EmissionColor", targetColor * emission);
            }
            else
            {
                _instanceMaterial.DisableKeyword("_EMISSION");
            }

            // Update outline
            UpdateOutline(_isSelected || _isHovered);
        }

        private void UpdateOutline(bool show)
        {
            if (show && _outlineObject == null)
            {
                CreateOutline();
            }
            else if (!show && _outlineObject != null)
            {
                Destroy(_outlineObject);
                _outlineObject = null;
            }
        }

        private void CreateOutline()
        {
            _outlineObject = new GameObject("SensorOutline");
            _outlineObject.transform.SetParent(transform, false);
            _outlineObject.transform.localPosition = Vector3.zero;
            _outlineObject.transform.localRotation = Quaternion.identity;
            _outlineObject.transform.localScale = Vector3.one * 1.1f; // Slightly larger

            // Copy mesh
            MeshFilter sourceMF = GetComponent<MeshFilter>();
            if (sourceMF != null)
            {
                MeshFilter mf = _outlineObject.AddComponent<MeshFilter>();
                mf.mesh = sourceMF.mesh;

                MeshRenderer mr = _outlineObject.AddComponent<MeshRenderer>();
                Material outlineMat = new Material(Shader.Find("Standard"));
                outlineMat.color = _isSelected ? _selectedColor : _hoverColor;
                outlineMat.SetFloat("_Metallic", 0.5f);
                outlineMat.SetFloat("_Glossiness", 0.8f);
                outlineMat.EnableKeyword("_EMISSION");
                outlineMat.SetColor("_EmissionColor", outlineMat.color * 0.5f);
                mr.material = outlineMat;
            }
        }

        private void ShowSensorTooltip()
        {
            // Would integrate with UI system to show tooltip
            string tooltip = $"{SensorProperties.Name}\n";
            tooltip += $"Type: {SensorProperties.Type}\n";
            tooltip += $"Range: {SensorProperties.MinRange:F2}m - {SensorProperties.MaxRange:F2}m\n";
            tooltip += $"FOV: {SensorProperties.FieldOfView:F0}°\n";
            tooltip += $"Rate: {SensorProperties.UpdateRate:F0}Hz";

            Debug.Log($"Sensor Tooltip: {tooltip}");
        }

        private void HideSensorTooltip()
        {
            // Hide tooltip UI
        }

        private SensorProperties CreateDefaultProperties()
        {
            // Try to infer sensor type from object name
            string objName = gameObject.name.ToLower();

            SensorProperties props = new SensorProperties
            {
                Name = gameObject.name
            };

            if (objName.Contains("ultrasonic") || objName.Contains("sonar"))
            {
                props.Type = SensorType.Ultrasonic;
                props.MaxRange = 4.0f;
                props.MinRange = 0.02f;
                props.FieldOfView = 30f;
                props.UpdateRate = 50f;
            }
            else if (objName.Contains("ir") || objName.Contains("infrared"))
            {
                props.Type = SensorType.InfraredProximity;
                props.MaxRange = 0.8f;
                props.MinRange = 0.04f;
                props.FieldOfView = 15f;
                props.UpdateRate = 100f;
            }
            else if (objName.Contains("line"))
            {
                props.Type = SensorType.LineSensor;
                props.MaxRange = 0.05f;
                props.MinRange = 0.002f;
                props.FieldOfView = 0f;
                props.SensorWidth = 0.08f;
                props.ArraySize = 4;
                props.UpdateRate = 1000f;
            }
            else if (objName.Contains("color"))
            {
                props.Type = SensorType.ColorSensor;
                props.MaxRange = 0.03f;
                props.MinRange = 0.005f;
                props.FieldOfView = 0f;
                props.SensorWidth = 0.01f;
                props.UpdateRate = 100f;
            }
            else if (objName.Contains("lidar"))
            {
                props.Type = SensorType.LiDAR;
                props.MaxRange = 12.0f;
                props.MinRange = 0.1f;
                props.FieldOfView = 360f;
                props.UpdateRate = 10f;
            }
            else
            {
                // Generic proximity sensor
                props.Type = SensorType.InfraredProximity;
                props.MaxRange = 1.0f;
                props.MinRange = 0.05f;
                props.FieldOfView = 20f;
                props.UpdateRate = 50f;
            }

            return props;
        }

        private void OnDestroy()
        {
            if (_instanceMaterial != null)
                Destroy(_instanceMaterial);

            if (_outlineObject != null)
                Destroy(_outlineObject);
        }

        // Mouse raycast detection (for non-UI clicks)
        private void OnMouseDown()
        {
            // Alternative click detection if not using EventSystem
            _isSelected = !_isSelected;

            if (_isSelected)
            {
                SensorVisualizationController.Instance.ShowSensorArea(gameObject, SensorProperties);
            }
            else
            {
                SensorVisualizationController.Instance.HideSensorArea(gameObject.GetInstanceID().ToString());
            }

            UpdateVisualState();
        }

        private void OnMouseEnter()
        {
            _isHovered = true;
            UpdateVisualState();
            ShowSensorTooltip();
        }

        private void OnMouseExit()
        {
            _isHovered = false;
            UpdateVisualState();
            HideSensorTooltip();
        }
    }

    /// <summary>
    /// Auto-attach SensorClickable to robot sensors
    /// </summary>
    public class RobotSensorSetup : MonoBehaviour
    {
        [Header("Auto-Setup")]
        [SerializeField] private bool _autoSetupOnStart = true;
        [SerializeField] private string[] _sensorNamePatterns = { "sensor", "ultrasonic", "ir", "line", "color", "lidar" };

        [Header("Predefined Sensors")]
        [SerializeField] private List<SensorDefinition> _sensorDefinitions = new List<SensorDefinition>();

        private void Start()
        {
            if (_autoSetupOnStart)
            {
                SetupAllSensors();
            }
        }

        public void SetupAllSensors()
        {
            // Find all child objects that look like sensors
            Transform[] allChildren = GetComponentsInChildren<Transform>(true);

            foreach (Transform child in allChildren)
            {
                if (child == transform) continue; // Skip self

                string childName = child.gameObject.name.ToLower();
                bool isSensor = false;

                // Check if name matches sensor pattern
                foreach (string pattern in _sensorNamePatterns)
                {
                    if (childName.Contains(pattern))
                    {
                        isSensor = true;
                        break;
                    }
                }

                if (isSensor)
                {
                    SetupSensor(child.gameObject);
                }
            }

            // Apply predefined sensor configurations
            ApplyPredefinedConfigurations();

            Debug.Log($"Robot sensor setup complete. Found {allChildren.Length} potential sensors.");
        }

        private void SetupSensor(GameObject sensorObj)
        {
            // Add SensorClickable if not already present
            SensorClickable clickable = sensorObj.GetComponent<SensorClickable>();
            if (clickable == null)
            {
                clickable = sensorObj.AddComponent<SensorClickable>();
            }

            // Ensure collider exists
            Collider collider = sensorObj.GetComponent<Collider>();
            if (collider == null)
            {
                BoxCollider box = sensorObj.AddComponent<BoxCollider>();
                box.isTrigger = false; // Make it clickable
            }
        }

        private void ApplyPredefinedConfigurations()
        {
            foreach (var definition in _sensorDefinitions)
            {
                Transform sensorTransform = transform.Find(definition.SensorPath);
                if (sensorTransform != null)
                {
                    SensorClickable clickable = sensorTransform.GetComponent<SensorClickable>();
                    if (clickable != null)
                    {
                        clickable.SensorProperties = definition.Properties;
                    }
                }
            }
        }

        [ContextMenu("Setup Sensors Now")]
        public void SetupSensorsManual()
        {
            SetupAllSensors();
        }
    }

    [Serializable]
    public class SensorDefinition
    {
        public string SensorPath;           // Path relative to robot root
        public SensorProperties Properties;
    }
}
