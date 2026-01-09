using UnityEngine;
using System.Collections.Generic;
using System;

namespace RobotTwin.UI
{
    /// <summary>
    /// Thermal Heatmap Renderer - GPU-accelerated temperature visualization
    /// Real-time color-coded temperature overlays for circuits and robots
    /// </summary>
    public class HeatmapRenderer : MonoBehaviour
    {
        [Header("Heatmap Settings")]
        [SerializeField] private Material _heatmapMaterial;
        [SerializeField] private Gradient _temperatureGradient;
        [SerializeField] private float _minTemperature = 20f; // °C
        [SerializeField] private float _maxTemperature = 100f; // °C
        [SerializeField] private bool _showLegend = true;
        [SerializeField] private int _resolution = 256; // Texture resolution

        [Header("Display Options")]
        [SerializeField] private float _blendFactor = 0.7f; // 0 = no overlay, 1 = full overlay
        [SerializeField] private bool _showHotspots = true;
        [SerializeField] private float _hotspotThreshold = 70f; // °C
        [SerializeField] private bool _smoothing = true;

        // Render textures
        private RenderTexture _heatmapTexture;
        private Texture2D _legendTexture;

        // Heatmap data
        private Dictionary<string, float> _componentTemperatures = new Dictionary<string, float>();
        private Dictionary<Vector3, float> _spatialTemperatures = new Dictionary<Vector3, float>();

        // Hotspot markers
        private List<GameObject> _hotspotMarkers = new List<GameObject>();

        // Material property IDs
        private int _heatmapTexId;
        private int _minTempId;
        private int _maxTempId;
        private int _blendId;

        public void Initialize()
        {
            // Create default gradient if not set
            if (_temperatureGradient == null)
            {
                _temperatureGradient = CreateDefaultTemperatureGradient();
            }

            // Create render texture
            _heatmapTexture = new RenderTexture(_resolution, _resolution, 0, RenderTextureFormat.ARGB32);
            _heatmapTexture.filterMode = FilterMode.Bilinear;
            _heatmapTexture.wrapMode = TextureWrapMode.Clamp;

            // Create legend texture
            _legendTexture = GenerateLegendTexture(256, 32);

            // Cache material property IDs
            _heatmapTexId = Shader.PropertyToID("_HeatmapTex");
            _minTempId = Shader.PropertyToID("_MinTemp");
            _maxTempId = Shader.PropertyToID("_MaxTemp");
            _blendId = Shader.PropertyToID("_BlendFactor");

            // Setup material
            if (_heatmapMaterial != null)
            {
                _heatmapMaterial.SetTexture(_heatmapTexId, _heatmapTexture);
                _heatmapMaterial.SetFloat(_minTempId, _minTemperature);
                _heatmapMaterial.SetFloat(_maxTempId, _maxTemperature);
                _heatmapMaterial.SetFloat(_blendId, _blendFactor);
            }
        }

        /// <summary>
        /// Update heatmap with new temperature data
        /// </summary>
        public void UpdateHeatmap(Dictionary<string, float> componentTemperatures, Dictionary<Vector3, float> spatialTemperatures)
        {
            _componentTemperatures = componentTemperatures;
            _spatialTemperatures = spatialTemperatures;

            RenderHeatmapToTexture();

            if (_showHotspots)
            {
                UpdateHotspotMarkers();
            }
        }

        /// <summary>
        /// Update circuit heatmap from circuit analysis
        /// </summary>
        public void UpdateCircuitHeatmap(Dictionary<string, ComponentThermal> thermalMap)
        {
            _componentTemperatures.Clear();

            foreach (var kvp in thermalMap)
            {
                _componentTemperatures[kvp.Key] = kvp.Value.Temperature;
            }

            RenderHeatmapToTexture();

            if (_showHotspots)
            {
                UpdateHotspotMarkers();
            }
        }

        /// <summary>
        /// Update robot heatmap from robot analysis
        /// </summary>
        public void UpdateRobotHeatmap(Dictionary<string, float> componentTemperatures)
        {
            _componentTemperatures = componentTemperatures;

            RenderHeatmapToTexture();

            if (_showHotspots)
            {
                UpdateHotspotMarkers();
            }
        }

        private void RenderHeatmapToTexture()
        {
            if (_heatmapTexture == null)
                return;

            // Create temporary texture for CPU-side manipulation
            Texture2D tempTexture = new Texture2D(_resolution, _resolution, TextureFormat.ARGB32, false);
            Color[] pixels = new Color[_resolution * _resolution];

            // Initialize to ambient temperature
            Color ambientColor = GetTemperatureColor(_minTemperature);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = ambientColor;
            }

            // Render component temperatures
            foreach (var kvp in _componentTemperatures)
            {
                string componentId = kvp.Key;
                float temperature = kvp.Value;

                // Get component position (simplified - would need actual 3D position)
                Vector2 pos = GetComponentScreenPosition(componentId);

                // Draw temperature blob
                DrawTemperatureBlob(pixels, _resolution, pos, temperature, 20); // 20 pixel radius
            }

            // Render spatial temperatures
            foreach (var kvp in _spatialTemperatures)
            {
                Vector3 worldPos = kvp.Key;
                float temperature = kvp.Value;

                Vector2 screenPos = WorldToScreenPosition(worldPos);
                DrawTemperatureBlob(pixels, _resolution, screenPos, temperature, 15);
            }

            // Apply smoothing filter
            if (_smoothing)
            {
                pixels = ApplyGaussianBlur(pixels, _resolution, _resolution, 2);
            }

            // Upload to GPU
            tempTexture.SetPixels(pixels);
            tempTexture.Apply();

            Graphics.Blit(tempTexture, _heatmapTexture);

            Destroy(tempTexture);
        }

        private void DrawTemperatureBlob(Color[] pixels, int width, Vector2 center, float temperature, int radius)
        {
            int centerX = (int)center.x;
            int centerY = (int)center.y;

            Color tempColor = GetTemperatureColor(temperature);

            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (x < 0 || x >= width || y < 0 || y >= width)
                        continue;

                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    if (distance > radius)
                        continue;

                    // Falloff based on distance
                    float falloff = 1f - (distance / radius);
                    falloff = Mathf.Pow(falloff, 2f); // Squared falloff

                    int index = y * width + x;
                    pixels[index] = Color.Lerp(pixels[index], tempColor, falloff);
                }
            }
        }

        private Color GetTemperatureColor(float temperature)
        {
            // Normalize temperature to 0-1 range
            float normalized = Mathf.InverseLerp(_minTemperature, _maxTemperature, temperature);
            normalized = Mathf.Clamp01(normalized);

            return _temperatureGradient.Evaluate(normalized);
        }

        private Vector2 GetComponentScreenPosition(string componentId)
        {
            // Simplified - would need to query actual component position
            // For now, use hash-based positioning
            int hash = componentId.GetHashCode();
            float x = ((hash & 0xFF) / 255f) * _resolution;
            float y = (((hash >> 8) & 0xFF) / 255f) * _resolution;

            return new Vector2(x, y);
        }

        private Vector2 WorldToScreenPosition(Vector3 worldPos)
        {
            // Convert world position to screen space (0-resolution)
            Camera mainCam = Camera.main;
            if (mainCam == null)
                return Vector2.zero;

            Vector3 screenPos = mainCam.WorldToScreenPoint(worldPos);
            float x = (screenPos.x / Screen.width) * _resolution;
            float y = (screenPos.y / Screen.height) * _resolution;

            return new Vector2(x, y);
        }

        private Color[] ApplyGaussianBlur(Color[] source, int width, int height, int radius)
        {
            Color[] result = new Color[width * height];

            // Simplified box blur (Gaussian approximation)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color sum = Color.black;
                    int count = 0;

                    for (int ky = -radius; ky <= radius; ky++)
                    {
                        for (int kx = -radius; kx <= radius; kx++)
                        {
                            int sx = Mathf.Clamp(x + kx, 0, width - 1);
                            int sy = Mathf.Clamp(y + ky, 0, height - 1);

                            sum += source[sy * width + sx];
                            count++;
                        }
                    }

                    result[y * width + x] = sum / count;
                }
            }

            return result;
        }

        private void UpdateHotspotMarkers()
        {
            // Clear existing markers
            foreach (var marker in _hotspotMarkers)
            {
                if (marker != null)
                    Destroy(marker);
            }
            _hotspotMarkers.Clear();

            // Create markers for hotspots
            foreach (var kvp in _componentTemperatures)
            {
                if (kvp.Value >= _hotspotThreshold)
                {
                    GameObject marker = CreateHotspotMarker(kvp.Key, kvp.Value);
                    _hotspotMarkers.Add(marker);
                }
            }
        }

        private GameObject CreateHotspotMarker(string componentId, float temperature)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"Hotspot_{componentId}";
            marker.transform.localScale = Vector3.one * 0.05f;

            // Position at component location (simplified)
            // Would need to query actual component transform

            // Color based on severity
            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = GetTemperatureColor(temperature);
                mat.SetFloat("_Metallic", 0.5f);
                mat.SetFloat("_Glossiness", 0.8f);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", GetTemperatureColor(temperature) * 2f);
                renderer.material = mat;
            }

            // Add pulsing animation
            var pulser = marker.AddComponent<HotspotPulser>();
            pulser.Initialize(temperature);

            return marker;
        }

        private Gradient CreateDefaultTemperatureGradient()
        {
            Gradient gradient = new Gradient();

            GradientColorKey[] colorKeys = new GradientColorKey[5];
            colorKeys[0] = new GradientColorKey(new Color(0f, 0f, 1f), 0.0f);    // Blue - cold
            colorKeys[1] = new GradientColorKey(new Color(0f, 1f, 1f), 0.25f);   // Cyan - cool
            colorKeys[2] = new GradientColorKey(new Color(0f, 1f, 0f), 0.5f);    // Green - normal
            colorKeys[3] = new GradientColorKey(new Color(1f, 1f, 0f), 0.75f);   // Yellow - warm
            colorKeys[4] = new GradientColorKey(new Color(1f, 0f, 0f), 1.0f);    // Red - hot

            GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
            alphaKeys[0] = new GradientAlphaKey(0.7f, 0.0f);
            alphaKeys[1] = new GradientAlphaKey(0.9f, 1.0f);

            gradient.SetKeys(colorKeys, alphaKeys);

            return gradient;
        }

        private Texture2D GenerateLegendTexture(int width, int height)
        {
            Texture2D legend = new Texture2D(width, height, TextureFormat.ARGB32, false);
            Color[] pixels = new Color[width * height];

            for (int x = 0; x < width; x++)
            {
                float t = (float)x / width;
                Color color = _temperatureGradient.Evaluate(t);

                for (int y = 0; y < height; y++)
                {
                    pixels[y * width + x] = color;
                }
            }

            legend.SetPixels(pixels);
            legend.Apply();

            return legend;
        }

        public void SetBlendFactor(float blend)
        {
            _blendFactor = Mathf.Clamp01(blend);
            if (_heatmapMaterial != null)
            {
                _heatmapMaterial.SetFloat(_blendId, _blendFactor);
            }
        }

        public void SetTemperatureRange(float min, float max)
        {
            _minTemperature = min;
            _maxTemperature = max;

            if (_heatmapMaterial != null)
            {
                _heatmapMaterial.SetFloat(_minTempId, _minTemperature);
                _heatmapMaterial.SetFloat(_maxTempId, _maxTemperature);
            }

            // Regenerate legend
            if (_legendTexture != null)
            {
                Destroy(_legendTexture);
            }
            _legendTexture = GenerateLegendTexture(256, 32);
        }

        public Texture2D GetLegendTexture()
        {
            return _legendTexture;
        }

        public RenderTexture GetHeatmapTexture()
        {
            return _heatmapTexture;
        }

        private void OnDestroy()
        {
            if (_heatmapTexture != null)
            {
                _heatmapTexture.Release();
                Destroy(_heatmapTexture);
            }

            if (_legendTexture != null)
            {
                Destroy(_legendTexture);
            }

            foreach (var marker in _hotspotMarkers)
            {
                if (marker != null)
                    Destroy(marker);
            }
        }
    }

    /// <summary>
    /// Hotspot marker pulsing animation
    /// </summary>
    public class HotspotPulser : MonoBehaviour
    {
        private float _temperature;
        private float _pulseSpeed = 2f;
        private float _pulseAmount = 0.2f;
        private Vector3 _originalScale;

        public void Initialize(float temperature)
        {
            _temperature = temperature;
            _originalScale = transform.localScale;

            // Faster pulse for hotter components
            _pulseSpeed = 2f + (temperature - 70f) * 0.1f;
        }

        private void Update()
        {
            float pulse = 1f + Mathf.Sin(Time.time * _pulseSpeed) * _pulseAmount;
            transform.localScale = _originalScale * pulse;
        }
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
}
