using System;
using System.Collections.Generic;
using UnityEngine;
using RobotTwin.CoreSim.Runtime;

namespace RobotTwin.UI
{
    /// <summary>
    /// Runtime diagnostics visualizer for RobotStudio.
    ///
    /// V1 goals:
    /// - Visualize PART:* stress/temperature as part tinting (heatmap style)
    /// - Visualize hotspots with gizmo spheres
    /// - Scream loudly when MECH:drops increases ("data loss")
    ///
    /// The simulation host currently emits placeholder telemetry so the visualization can be wired now,
    /// and later swapped to real NativeEngine/CoreSim/Firmware coupling without changing this class.
    /// </summary>
    public sealed class RobotStudioDiagnosticsOverlay : MonoBehaviour
    {
        [Header("Controls")]
        [SerializeField] private KeyCode _toggleKey = KeyCode.F4;

        [Header("Visualization")]
        [SerializeField] private bool _showTemperature = true;
        [SerializeField] private bool _showStress = true;
        [SerializeField] private bool _showHotspots = true;

        [SerializeField] private float _tempColdC = 22f;
        [SerializeField] private float _tempHotC = 80f;

        // Stress is in Pascals in our placeholder telemetry.
        [SerializeField] private double _stressWarnPa = 2.5e6;
        [SerializeField] private double _stressDangerPa = 1.0e7;

        private RobotStudioSimulationHost _host;
        private ComponentStudioView _view;

        private bool _active;
        private long _lastTelemetryTick = -1;

        private readonly Dictionary<Renderer, MaterialPropertyBlock> _originalBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();
        private readonly MaterialPropertyBlock _scratchBlock = new MaterialPropertyBlock();

        private readonly List<ComponentStudioView.EffectGizmo> _hotspotGizmos = new List<ComponentStudioView.EffectGizmo>(128);

        private long _lastMechDrops = -1;
        private long _lastMechDelta;
        private float _flashUntilRealtime;

        private float _nextResolveAt;

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
            {
                SetActive(!_active);
            }

            if (!_active) return;

            ResolveReferences();
            if (_host == null || _view == null) return;

            var telemetry = _host.LastTelemetry;
            if (telemetry == null) return;

            if (telemetry.TickIndex != _lastTelemetryTick)
            {
                _lastTelemetryTick = telemetry.TickIndex;
                ApplyTelemetryToView(telemetry);
            }

            // Update flash state even if tick didn't change.
            float now = Time.realtimeSinceStartup;
            if (_lastMechDelta > 0 && now >= _flashUntilRealtime)
            {
                _lastMechDelta = 0;
            }
        }

        private void OnDisable()
        {
            if (_active)
            {
                SetActive(false);
            }
        }

        private void SetActive(bool enabled)
        {
            if (_active == enabled) return;
            _active = enabled;

            if (!_active)
            {
                RestoreOriginalBlocks();
                if (_view != null)
                {
                    _view.ClearEffects();
                }
                _lastTelemetryTick = -1;
                _lastMechDrops = -1;
                _lastMechDelta = 0;
                return;
            }

            ResolveReferences(force: true);
        }

        private void ResolveReferences(bool force = false)
        {
            float now = Time.realtimeSinceStartup;
            if (!force && now < _nextResolveAt) return;
            _nextResolveAt = now + 1.0f;

            if (_host == null)
            {
                _host = GetComponent<RobotStudioSimulationHost>();
                if (_host == null)
                {
                    _host = FindFirstObjectByType<RobotStudioSimulationHost>();
                }
            }

            if (_view == null)
            {
                _view = FindFirstObjectByType<ComponentStudioView>();
            }
        }

        private void ApplyTelemetryToView(TelemetryFrame telemetry)
        {
            if (_view == null) return;
            var root = _view.ModelRoot;
            if (root == null) return;

            var parts = root.GetComponentsInChildren<RobotStudioAssemblyItem>(true);
            if (parts == null || parts.Length == 0) return;

            // Data loss detection (rising edge).
            long drops = GetLongSignal(telemetry, "MECH:drops", -1);
            if (drops >= 0)
            {
                if (_lastMechDrops >= 0)
                {
                    long delta = drops - _lastMechDrops;
                    if (delta < 0) delta = 0;
                    _lastMechDelta = delta;
                    if (delta > 0)
                    {
                        _flashUntilRealtime = Time.realtimeSinceStartup + 0.65f;
                    }
                }
                _lastMechDrops = drops;
            }

            _hotspotGizmos.Clear();

            foreach (var part in parts)
            {
                if (part == null) continue;
                string id = part.InstanceId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id)) continue;

                bool hasTemp = telemetry.Signals.TryGetValue($"PART:{id}:T", out var tempC);
                bool hasStress = telemetry.Signals.TryGetValue($"PART:{id}:S", out var stressPa);

                // Compute a combined tint.
                Color tint = Color.white;
                bool applyTint = false;

                if (_showTemperature && hasTemp)
                {
                    float t = Mathf.InverseLerp(_tempColdC, _tempHotC, (float)tempC);
                    tint = Color.Lerp(new Color(0.25f, 0.75f, 1f), new Color(1f, 0.25f, 0.1f), Mathf.Clamp01(t));
                    applyTint = true;
                }

                if (_showStress && hasStress)
                {
                    double s = Math.Max(0.0, stressPa);
                    float k = 0f;
                    if (s >= _stressWarnPa)
                    {
                        k = (float)Mathf.InverseLerp((float)_stressWarnPa, (float)_stressDangerPa, (float)Math.Min(s, _stressDangerPa));
                    }

                    if (k > 0f)
                    {
                        // Mix in a "stress" hue (yellow -> magenta).
                        var stressColor = Color.Lerp(new Color(1f, 0.85f, 0.1f), new Color(1f, 0.1f, 0.85f), Mathf.Clamp01(k));
                        tint = applyTint ? Color.Lerp(tint, stressColor, 0.55f) : stressColor;
                        applyTint = true;
                    }

                    if (_showHotspots && s >= _stressWarnPa)
                    {
                        var bounds = GetWorldBounds(part.transform);
                        var localPos = root.InverseTransformPoint(bounds.center);
                        float radius = Mathf.Lerp(0.01f, 0.03f, Mathf.Clamp01(k));
                        _hotspotGizmos.Add(new ComponentStudioView.EffectGizmo
                        {
                            Id = $"{id}_stress",
                            LocalPosition = localPos,
                            Radius = radius,
                            Color = stressColorFor(s)
                        });
                    }
                }

                if (applyTint)
                {
                    ApplyTint(part.transform, tint);
                }
            }

            if (_showHotspots)
            {
                _view.SetEffectGizmos(_hotspotGizmos);
            }
            else
            {
                _view.ClearEffects();
            }
        }

        private Color stressColorFor(double stressPa)
        {
            double s = Math.Max(0.0, stressPa);
            if (s >= _stressDangerPa) return new Color(1f, 0.15f, 0.3f);
            if (s >= _stressWarnPa) return new Color(1f, 0.8f, 0.15f);
            return new Color(0.2f, 0.9f, 0.4f);
        }

        private void ApplyTint(Transform part, Color tint)
        {
            if (part == null) return;
            var renderers = part.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;

                if (!_originalBlocks.ContainsKey(r))
                {
                    var original = new MaterialPropertyBlock();
                    r.GetPropertyBlock(original);
                    _originalBlocks[r] = original;
                }

                r.GetPropertyBlock(_scratchBlock);
                _scratchBlock.SetColor("_BaseColor", tint);
                _scratchBlock.SetColor("_Color", tint);
                _scratchBlock.SetColor("_EmissionColor", tint * 0.4f);
                r.SetPropertyBlock(_scratchBlock);
            }
        }

        private void RestoreOriginalBlocks()
        {
            if (_originalBlocks.Count == 0) return;
            foreach (var kvp in _originalBlocks)
            {
                if (kvp.Key == null) continue;
                kvp.Key.SetPropertyBlock(kvp.Value);
            }
            _originalBlocks.Clear();
        }

        private static long GetLongSignal(TelemetryFrame telemetry, string key, long fallback)
        {
            if (telemetry?.Signals == null || string.IsNullOrWhiteSpace(key)) return fallback;
            if (!telemetry.Signals.TryGetValue(key, out var value)) return fallback;
            if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
            return (long)Math.Max(0.0, Math.Floor(value));
        }

        private static Bounds GetWorldBounds(Transform root)
        {
            var renderers = root != null ? root.GetComponentsInChildren<Renderer>(true) : null;
            if (renderers == null || renderers.Length == 0)
            {
                return new Bounds(root != null ? root.position : Vector3.zero, Vector3.zero);
            }

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                b.Encapsulate(renderers[i].bounds);
            }
            return b;
        }

        private void OnGUI()
        {
            if (!_active) return;

            float now = Time.realtimeSinceStartup;
            bool flashing = _lastMechDelta > 0 && now < _flashUntilRealtime;
            if (!flashing) return;

            var rect = new Rect(10, 10, Screen.width - 20, 54);
            var bg = GUI.color;
            GUI.color = new Color(1f, 0.15f, 0.15f, 0.85f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 12, rect.y + 10, rect.width - 24, rect.height - 20),
                $"DATA LOSS DETECTED  (+{_lastMechDelta} drops)");
            GUI.color = bg;
        }
    }
}
