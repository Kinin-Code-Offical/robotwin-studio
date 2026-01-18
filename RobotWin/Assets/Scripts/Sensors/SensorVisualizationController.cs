using UnityEngine;
using System.Collections.Generic;
using System;

namespace RobotTwin.Sensors
{
    /// <summary>
    /// Sensor Visualization System
    /// Shows 3D detection area with fade effect when sensor is clicked
    /// Displays FOV cone/area, range, and material sensitivity
    /// </summary>
    public class SensorVisualizationController : MonoBehaviour
    {
        [Header("Visualization Settings")]
        [SerializeField] private Material _detectionAreaMaterial;
        [SerializeField] private Material _detectionConeMaterial;
        [SerializeField] private Color _detectionColor = new Color(0f, 1f, 0f, 0.3f);
        [SerializeField] private Color _limitColor = new Color(1f, 0f, 0f, 0.2f);
        [SerializeField] private int _coneSegments = 32;
        [SerializeField] private int _rangeRings = 5;

        [Header("Animation")]
        [SerializeField] private float _pulseDuration = 1.5f;
        [SerializeField] private float _fadeInSpeed = 3f;
        [SerializeField] private float _fadeOutSpeed = 2f;

        // Active visualizations
        private Dictionary<string, SensorVisualization> _activeVisualizations = new Dictionary<string, SensorVisualization>();

        // Raycast layers
        private LayerMask _environmentLayer;

        // Singleton
        private static SensorVisualizationController _instance;
        public static SensorVisualizationController Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<SensorVisualizationController>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("SensorVisualizationController");
                        _instance = go.AddComponent<SensorVisualizationController>();
                    }
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            _environmentLayer = LayerMask.GetMask("Default", "Environment", "Ground");

            // Create default materials if not assigned
            if (_detectionAreaMaterial == null)
            {
                _detectionAreaMaterial = CreateDefaultMaterial();
            }
            if (_detectionConeMaterial == null)
            {
                _detectionConeMaterial = CreateDefaultMaterial();
            }
        }

        /// <summary>
        /// Show sensor detection area when sensor is clicked
        /// </summary>
        public void ShowSensorArea(GameObject sensorObject, SensorProperties properties)
        {
            string sensorId = sensorObject.GetInstanceID().ToString();

            // Toggle if already showing
            if (_activeVisualizations.ContainsKey(sensorId))
            {
                HideSensorArea(sensorId);
                return;
            }

            // Create new visualization
            SensorVisualization viz = new SensorVisualization
            {
                SensorObject = sensorObject,
                Properties = properties,
                VisualizationObject = new GameObject($"Sensor_Viz_{sensorObject.name}"),
                IsActive = true,
                FadeAlpha = 0f,
                PulseTimer = 0f
            };

            viz.VisualizationObject.transform.SetParent(sensorObject.transform, false);
            viz.VisualizationObject.transform.localPosition = Vector3.zero;
            viz.VisualizationObject.transform.localRotation = Quaternion.identity;

            // Build visualization mesh based on sensor type
            BuildVisualizationMesh(viz);

            _activeVisualizations[sensorId] = viz;

            Debug.Log($"Showing sensor area for {sensorObject.name} ({properties.Type})");
        }

        /// <summary>
        /// Hide sensor visualization
        /// </summary>
        public void HideSensorArea(string sensorId)
        {
            if (_activeVisualizations.TryGetValue(sensorId, out SensorVisualization viz))
            {
                viz.IsActive = false;
                // Will be cleaned up in Update after fade out
            }
        }

        /// <summary>
        /// Hide all sensor visualizations
        /// </summary>
        public void HideAllSensors()
        {
            foreach (var viz in _activeVisualizations.Values)
            {
                viz.IsActive = false;
            }
        }

        private void Update()
        {
            List<string> toRemove = new List<string>();

            foreach (var kvp in _activeVisualizations)
            {
                string sensorId = kvp.Key;
                SensorVisualization viz = kvp.Value;

                // Fade in/out
                if (viz.IsActive)
                {
                    viz.FadeAlpha = Mathf.MoveTowards(viz.FadeAlpha, 1f, _fadeInSpeed * Time.deltaTime);
                }
                else
                {
                    viz.FadeAlpha = Mathf.MoveTowards(viz.FadeAlpha, 0f, _fadeOutSpeed * Time.deltaTime);

                    if (viz.FadeAlpha <= 0f)
                    {
                        // Cleanup
                        if (viz.VisualizationObject != null)
                            Destroy(viz.VisualizationObject);
                        toRemove.Add(sensorId);
                        continue;
                    }
                }

                // Pulse animation
                viz.PulseTimer += Time.deltaTime;
                float pulse = Mathf.Sin(viz.PulseTimer * (2f * Mathf.PI / _pulseDuration)) * 0.5f + 0.5f;

                // Update material alpha
                UpdateVisualizationAlpha(viz, viz.FadeAlpha * (0.5f + pulse * 0.5f));

                // Update based on sensor readings
                UpdateSensorReadings(viz);
            }

            // Remove faded out visualizations
            foreach (string id in toRemove)
            {
                _activeVisualizations.Remove(id);
            }
        }

        private void BuildVisualizationMesh(SensorVisualization viz)
        {
            SensorProperties props = viz.Properties;

            switch (props.Type)
            {
                case SensorType.Ultrasonic:
                    BuildUltrasonicCone(viz);
                    break;

                case SensorType.InfraredProximity:
                    BuildIRCone(viz);
                    break;

                case SensorType.LineSensor:
                    BuildLineSensorArea(viz);
                    break;

                case SensorType.ColorSensor:
                    BuildColorSensorArea(viz);
                    break;

                case SensorType.LiDAR:
                    BuildLiDARScan(viz);
                    break;

                default:
                    BuildGenericCone(viz);
                    break;
            }
        }

        private void BuildUltrasonicCone(SensorVisualization viz)
        {
            SensorProperties props = viz.Properties;

            // Create cone mesh with fade gradient
            GameObject coneObj = new GameObject("UltrasonicCone");
            coneObj.transform.SetParent(viz.VisualizationObject.transform, false);

            MeshFilter meshFilter = coneObj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = coneObj.AddComponent<MeshRenderer>();

            Mesh mesh = CreateConeMesh(props.MaxRange, props.FieldOfView, _coneSegments, true);
            meshFilter.mesh = mesh;

            Material mat = new Material(_detectionConeMaterial);
            mat.color = _detectionColor;
            mat.SetFloat("_Mode", 2); // Fade mode
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;

            meshRenderer.material = mat;
            viz.MainMaterial = mat;

            // Add range rings
            for (int i = 1; i <= _rangeRings; i++)
            {
                float distance = (props.MaxRange / _rangeRings) * i;
                CreateRangeRing(viz.VisualizationObject.transform, distance, props.FieldOfView, i == _rangeRings);
            }

            // Add detection quality labels
            CreateDetectionLabels(viz, props.MaxRange * 0.3f, props.MaxRange * 0.7f, props.MaxRange);
        }

        private void BuildIRCone(SensorVisualization viz)
        {
            SensorProperties props = viz.Properties;

            // Similar to ultrasonic but narrower and shorter
            GameObject coneObj = new GameObject("IRCone");
            coneObj.transform.SetParent(viz.VisualizationObject.transform, false);

            MeshFilter meshFilter = coneObj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = coneObj.AddComponent<MeshRenderer>();

            Mesh mesh = CreateConeMesh(props.MaxRange, props.FieldOfView, _coneSegments, true);
            meshFilter.mesh = mesh;

            Material mat = new Material(_detectionConeMaterial);
            mat.color = new Color(1f, 0.3f, 0f, 0.3f); // Orange for IR
            mat.renderQueue = 3000;

            meshRenderer.material = mat;
            viz.MainMaterial = mat;
        }

        private void BuildLineSensorArea(SensorVisualization viz)
        {
            SensorProperties props = viz.Properties;

            // Create rectangular detection area below sensor
            GameObject areaObj = new GameObject("LineSensorArea");
            areaObj.transform.SetParent(viz.VisualizationObject.transform, false);
            areaObj.transform.localPosition = new Vector3(0f, -0.01f, props.MaxRange * 0.5f); // Slightly below sensor

            MeshFilter meshFilter = areaObj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = areaObj.AddComponent<MeshRenderer>();

            // Create rectangular plane with gradient
            Mesh mesh = CreateRectangleMesh(props.SensorWidth, props.MaxRange);
            meshFilter.mesh = mesh;

            Material mat = new Material(_detectionAreaMaterial);
            mat.color = new Color(0f, 0.7f, 1f, 0.4f); // Cyan for line sensor
            mat.renderQueue = 3000;

            meshRenderer.material = mat;
            viz.MainMaterial = mat;

            // Add sensor position indicators
            CreateSensorArrayIndicators(viz, props);
        }

        private void BuildColorSensorArea(SensorVisualization viz)
        {
            SensorProperties props = viz.Properties;

            // Small circular area directly below
            GameObject areaObj = new GameObject("ColorSensorArea");
            areaObj.transform.SetParent(viz.VisualizationObject.transform, false);
            areaObj.transform.localPosition = new Vector3(0f, -0.005f, props.MaxRange * 0.5f);

            MeshFilter meshFilter = areaObj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = areaObj.AddComponent<MeshRenderer>();

            // Create circular area
            Mesh mesh = CreateCircleMesh(props.SensorWidth, 24);
            meshFilter.mesh = mesh;

            Material mat = new Material(_detectionAreaMaterial);
            mat.color = new Color(1f, 0f, 1f, 0.5f); // Magenta for color sensor
            mat.renderQueue = 3000;

            meshRenderer.material = mat;
            viz.MainMaterial = mat;
        }

        private void BuildLiDARScan(SensorVisualization viz)
        {
            SensorProperties props = viz.Properties;

            // Create 360° scanning plane
            GameObject scanObj = new GameObject("LiDARScan");
            scanObj.transform.SetParent(viz.VisualizationObject.transform, false);

            MeshFilter meshFilter = scanObj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = scanObj.AddComponent<MeshRenderer>();

            Mesh mesh = CreateDiskMesh(props.MaxRange, 64);
            meshFilter.mesh = mesh;

            Material mat = new Material(_detectionAreaMaterial);
            mat.color = new Color(1f, 1f, 0f, 0.2f); // Yellow for LiDAR
            mat.renderQueue = 3000;

            meshRenderer.material = mat;
            viz.MainMaterial = mat;
        }

        private void BuildGenericCone(SensorVisualization viz)
        {
            SensorProperties props = viz.Properties;
            BuildUltrasonicCone(viz); // Default to ultrasonic-style
        }

        private Mesh CreateConeMesh(float range, float fovDegrees, int segments, bool fadeGradient)
        {
            Mesh mesh = new Mesh();
            mesh.name = "SensorConeMesh";

            float fovRadians = fovDegrees * Mathf.Deg2Rad;
            float radius = Mathf.Tan(fovRadians / 2f) * range;

            int vertexCount = segments + 2; // +1 for apex, +1 for center of base
            Vector3[] vertices = new Vector3[vertexCount];
            Color[] colors = new Color[vertexCount];
            int[] triangles = new int[segments * 6]; // 2 triangles per segment

            // Apex
            vertices[0] = Vector3.zero;
            colors[0] = new Color(1f, 1f, 1f, 1f); // Full alpha at source

            // Base circle
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * 2f * Mathf.PI;
                float x = Mathf.Cos(angle) * radius;
                float y = Mathf.Sin(angle) * radius;

                vertices[i + 1] = new Vector3(x, y, range);
                colors[i + 1] = new Color(1f, 1f, 1f, fadeGradient ? 0.1f : 0.5f); // Fade at distance
            }

            // Triangles
            int triIndex = 0;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments + 1;

                // Side triangle
                triangles[triIndex++] = 0;
                triangles[triIndex++] = i + 1;
                triangles[triIndex++] = next;
            }

            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        private Mesh CreateRectangleMesh(float width, float length)
        {
            Mesh mesh = new Mesh();
            mesh.name = "LineSensorRectangle";

            Vector3[] vertices = new Vector3[4]
            {
                new Vector3(-width/2, 0, 0),
                new Vector3(width/2, 0, 0),
                new Vector3(width/2, 0, length),
                new Vector3(-width/2, 0, length)
            };

            Color[] colors = new Color[4]
            {
                new Color(1f, 1f, 1f, 0.8f), // Strong at sensor
                new Color(1f, 1f, 1f, 0.8f),
                new Color(1f, 1f, 1f, 0.2f), // Fade at distance
                new Color(1f, 1f, 1f, 0.2f)
            };

            int[] triangles = new int[6] { 0, 2, 1, 0, 3, 2 };

            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        private Mesh CreateCircleMesh(float radius, int segments)
        {
            Mesh mesh = new Mesh();
            mesh.name = "ColorSensorCircle";

            Vector3[] vertices = new Vector3[segments + 1];
            Color[] colors = new Color[segments + 1];
            int[] triangles = new int[segments * 3];

            vertices[0] = Vector3.zero;
            colors[0] = new Color(1f, 1f, 1f, 1f);

            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * 2f * Mathf.PI;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                colors[i + 1] = new Color(1f, 1f, 1f, 0.3f);
            }

            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }

            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        private Mesh CreateDiskMesh(float radius, int segments)
        {
            return CreateCircleMesh(radius, segments);
        }

        private void CreateRangeRing(Transform parent, float distance, float fovDegrees, bool isLimit)
        {
            GameObject ring = new GameObject($"RangeRing_{distance:F1}m");
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0, 0, distance);

            LineRenderer lineRenderer = ring.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.widthMultiplier = 0.01f;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = isLimit ? _limitColor : new Color(_detectionColor.r, _detectionColor.g, _detectionColor.b, 0.5f);
            lineRenderer.endColor = lineRenderer.startColor;

            float fovRadians = fovDegrees * Mathf.Deg2Rad;
            float radius = Mathf.Tan(fovRadians / 2f) * distance;

            int segments = 32;
            lineRenderer.positionCount = segments;

            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * 2f * Mathf.PI;
                float x = Mathf.Cos(angle) * radius;
                float y = Mathf.Sin(angle) * radius;
                lineRenderer.SetPosition(i, new Vector3(x, y, 0));
            }
        }

        private void CreateDetectionLabels(SensorVisualization viz, float goodRange, float moderateRange, float maxRange)
        {
            // Create text labels (simplified - would use TextMeshPro in production)
            CreateLabel(viz.VisualizationObject.transform, "OPTIMAL", goodRange, Color.green);
            CreateLabel(viz.VisualizationObject.transform, "MODERATE", moderateRange, Color.yellow);
            CreateLabel(viz.VisualizationObject.transform, "LIMIT", maxRange, Color.red);
        }

        private void CreateLabel(Transform parent, string text, float distance, Color color)
        {
            GameObject label = new GameObject($"Label_{text}");
            label.transform.SetParent(parent, false);
            label.transform.localPosition = new Vector3(0, 0, distance);

            // Would use TextMeshPro here for proper 3D text
            // For now, just a marker
        }

        private void CreateSensorArrayIndicators(SensorVisualization viz, SensorProperties props)
        {
            // For line sensor arrays, show individual sensor positions
            if (props.ArraySize > 1)
            {
                float spacing = props.SensorWidth / (props.ArraySize - 1);
                float startX = -props.SensorWidth / 2f;

                for (int i = 0; i < props.ArraySize; i++)
                {
                    float x = startX + i * spacing;

                    GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    indicator.transform.SetParent(viz.VisualizationObject.transform, false);
                    indicator.transform.localPosition = new Vector3(x, 0, 0);
                    indicator.transform.localScale = Vector3.one * 0.01f;

                    Renderer renderer = indicator.GetComponent<Renderer>();
                    Material mat = new Material(Shader.Find("Standard"));
                    mat.color = Color.cyan;
                    mat.SetFloat("_Metallic", 0.8f);
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", Color.cyan);
                    renderer.material = mat;
                }
            }
        }

        private void UpdateVisualizationAlpha(SensorVisualization viz, float alpha)
        {
            if (viz.MainMaterial != null)
            {
                Color color = viz.MainMaterial.color;
                color.a = alpha;
                viz.MainMaterial.color = color;
            }
        }

        private void UpdateSensorReadings(SensorVisualization viz)
        {
            // Raycast to see what sensor is detecting
            if (viz.SensorObject == null)
                return;

            Transform sensorTransform = viz.SensorObject.transform;
            SensorProperties props = viz.Properties;

            RaycastHit hit;
            if (Physics.Raycast(sensorTransform.position, sensorTransform.forward, out hit, props.MaxRange, _environmentLayer))
            {
                // Detected something - could show hit point or distance
                viz.CurrentDetectionDistance = hit.distance;
                viz.DetectedObject = hit.collider.gameObject;

                // Could visualize detected material properties
                // Check material sensor compatibility here
            }
            else
            {
                viz.CurrentDetectionDistance = props.MaxRange;
                viz.DetectedObject = null;
            }
        }

        private Material CreateDefaultMaterial()
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.SetFloat("_Mode", 3); // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            return mat;
        }
    }

    // Data structures
    public class SensorVisualization
    {
        public GameObject SensorObject;
        public SensorProperties Properties;
        public GameObject VisualizationObject;
        public Material MainMaterial;
        public bool IsActive;
        public float FadeAlpha;
        public float PulseTimer;
        public float CurrentDetectionDistance;
        public GameObject DetectedObject;
    }

    [Serializable]
    public class SensorProperties
    {
        public string Name;
        public SensorType Type;
        public float MaxRange = 2.0f;              // meters
        public float MinRange = 0.02f;             // meters
        public float FieldOfView = 30f;            // degrees
        public float SensorWidth = 0.1f;           // meters (for line sensors)
        public int ArraySize = 1;                  // number of sensors in array
        public float UpdateRate = 50f;             // Hz

        // Material sensitivity
        public float OpticalSensitivity = 1.0f;
        public float IRSensitivity = 1.0f;
        public float UltrasonicSensitivity = 1.0f;

        // Detection quality zones
        public float OptimalRangePercent = 0.3f;   // 0-30% of max range
        public float GoodRangePercent = 0.7f;      // 30-70% of max range
        // 70-100% is poor/limit range
    }

    public enum SensorType
    {
        Ultrasonic,
        InfraredProximity,
        LineSensor,
        ColorSensor,
        LiDAR,
        Camera,
        Encoder,
        IMU,
        GPS
    }
}
