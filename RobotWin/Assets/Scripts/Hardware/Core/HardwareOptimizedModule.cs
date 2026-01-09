using UnityEngine;
using System.Collections.Generic;

namespace RobotWin.Hardware
{
    /// <summary>
    /// Represents a "Compacted" Hardware Circuit (.rtcomp) in the Unity Scene.
    /// This is the "Black Box" optimization of a complex schematic.
    /// </summary>
    public class HardwareOptimizedModule : MonoBehaviour, IHardwareModule
    {
        [Header("Module Meta")]
        public string ModuleName;
        public string Version;

        [Header("Runtime State")]
        [SerializeField] private float _currentTemp = 25.0f; // Celsius
        [SerializeField] private float _powerDraw = 0.0f; // Watts

        // Lookups for Pins (Fast access)
        private readonly Dictionary<string, CircuitPin> _pinMap = new Dictionary<string, CircuitPin>(System.StringComparer.Ordinal);

        // Internal Simulation Parameters (Loaded from .rtcomp JSON)
        private float _thermalMass = 1.0f; // J/K
        private float _thermalResistance = 0.5f; // K/W (Theta_JA)
        private float _efficiencyLoss = 0.05f; // Power dissipation factor
        private double _accumulatedStress = 0.0; // Integrated Arrhenius stress
        private float _baseMTBF = 500000f; // Hours
        private System.Random _reliabilityRng; // Deterministic RNG for failure simulation

        private bool _isDestroyed = false;

        public void Initialize(string configJson)
        {
            // Initialize deterministic RNG with module hash for reproducibility
            int seed = (ModuleName?.GetHashCode() ?? 0) ^ (Version?.GetHashCode() ?? 0);
            _reliabilityRng = new System.Random(seed);

            _pinMap.Clear();

            // Parse JSON configuration if provided
            if (!string.IsNullOrEmpty(configJson))
            {
                try
                {
                    var config = JsonUtility.FromJson<ModuleConfig>(configJson);
                    if (config != null)
                    {
                        _thermalMass = config.thermalMass > 0 ? config.thermalMass : _thermalMass;
                        _thermalResistance = config.thermalResistance > 0 ? config.thermalResistance : _thermalResistance;
                        _efficiencyLoss = config.efficiencyLoss;
                        _baseMTBF = config.baseMTBF > 0 ? config.baseMTBF : _baseMTBF;

                        // Build pin map from configuration
                        if (config.pins != null)
                        {
                            foreach (var pinCfg in config.pins)
                            {
                                _pinMap[pinCfg.name] = new CircuitPin
                                {
                                    Type = (SignalType)pinCfg.type,
                                    Voltage = pinCfg.initialVoltage
                                };
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"Failed to parse module config JSON: {ex.Message}. Using defaults.");
                }
            }

            // Ensure minimum required pins exist
            if (!_pinMap.ContainsKey("VCC"))
                _pinMap.Add("VCC", new CircuitPin { Type = SignalType.PowerRail, Voltage = 5.0f });
            if (!_pinMap.ContainsKey("GND"))
                _pinMap.Add("GND", new CircuitPin { Type = SignalType.PowerRail, Voltage = 0.0f });
        }

        [System.Serializable]
        private class ModuleConfig
        {
            public float thermalMass = 1.0f;
            public float thermalResistance = 0.5f;
            public float efficiencyLoss = 0.05f;
            public float baseMTBF = 500000f;
            public PinConfig[] pins;
        }

        [System.Serializable]
        private class PinConfig
        {
            public string name;
            public int type; // SignalType as int
            public float initialVoltage;
        }

        public void SimulateStep(float dt, float ambientTemp)
        {
            if (_isDestroyed) return;

            // 1. Advanced Thermal Simulation 
            // P_dissipated = I * V_drop (Simplified as PowerDraw * eff)
            float heatGen = _powerDraw * _efficiencyLoss;

            // Heat Dissipation (Theta_JA model):
            // dTemperature = (Power_In - (T_current - T_ambient)/Theta_JA) / Thermal_Mass * dt
            float coolingRate = (_currentTemp - ambientTemp) / _thermalResistance;
            float netEnergy = (heatGen - coolingRate);

            _currentTemp += (netEnergy / _thermalMass) * dt; // Warming/Cooling

            // 2. Reliability Physics (Arrhenius Equation for Acceleration Factor)
            // AF = exp( (Ea / k) * (1/T_use - 1/T_test) )
            // Simplified Rule of Thumb: Rate doubles every 10C rise above 25C
            float tempRise = _currentTemp - 25.0f;
            double accelerationFactor = System.Math.Pow(2.0, tempRise / 10.0);

            // Integrate Stress (Simulating aging)
            // 1 hour of life at high temp = AF hours of "Reliability Life"
            _accumulatedStress += (dt / 3600.0) * accelerationFactor;

            // 3. Probabilistic Failure Check (Exponential Distribution)
            // R(t) = exp(-lambda * t) -> Probability of failure in this step
            double lambda = 1.0 / _baseMTBF;
            double failureProb = 1.0 - System.Math.Exp(-lambda * _accumulatedStress);

            // Use deterministic RNG (seeded per module) for replay consistency
            if (_reliabilityRng != null && _reliabilityRng.NextDouble() < (failureProb * dt * 0.01)) // Scaled for frame rate checks
            {
                TriggerFailure("MTBF_EXCEEDED_AGING");
            }

            // 4. Logic / Behavior (The "Black Box" function)
            // Example: V_out = V_in * Gain (Simulated)
            // In a real implementation, this would run compiled bytecode or a lookup table.
            if (_pinMap.ContainsKey("VCC") && _pinMap["VCC"].Voltage > 2.7f)
            {
                // Logic runs
            }

            // 3. Fault Check
            if (_currentTemp > 120.0f) // Silicon limit
            {
                TriggerFailure("THERMAL_RUNAWAY");
            }
        }

        private void TriggerFailure(string reason)
        {
            if (_isDestroyed) return;

            _isDestroyed = true;
            string moduleName = string.IsNullOrEmpty(ModuleName) ? "UNKNOWN" : ModuleName;
            Debug.LogError($"[Hardware] Module {moduleName} DESTROYED: {reason}");

            // Visual Effect Hook - Defensive null check
            var renderer = GetComponent<Renderer>();
            if (renderer != null && renderer.material != null)
            {
                renderer.material.color = Color.black; // Charred effect
            }
        }

        // --- Interface Impl ---

        public float ProbePinVoltage(string pinName)
        {
            if (_pinMap.ContainsKey(pinName)) return _pinMap[pinName].Voltage;
            return 0f;
        }

        public void SetInputVoltage(string pinName, float voltage)
        {
            if (string.IsNullOrEmpty(pinName)) return; // Defensive check

            if (_pinMap.TryGetValue(pinName, out var pin))
            {
                pin.Voltage = voltage;
                _pinMap[pinName] = pin; // Struct rewrite
            }
        }

        public float GetSurfaceTemperature() => _currentTemp;
        public float GetTotalCurrentDraw() => (_powerDraw / 5.0f); // I = P/V (Simplified)

        // --- Spatial Helpers for Probes ---

        /// <summary>
        /// Finds the nearest Valid Electrical Pin to the given 3D position.
        /// Used by Virtual Instrument Probes to snap to the circuit.
        /// </summary>
        public bool GetNearestPin(Vector3 worldPos, float maxRadius, out string pinName, out Vector3 snapPosition)
        {
            pinName = string.Empty;
            snapPosition = Vector3.zero;

            if (_pinMap.Count == 0) return false; // Early exit if no pins

            float closestDistSqr = maxRadius * maxRadius;
            bool found = false;

            // Use optimized iteration to avoid LINQ allocations
            foreach (var kvp in _pinMap)
            {
                // Convert stored Local Pos -> World Pos
                Vector3 pinWorldPos = transform.TransformPoint(kvp.Value.LocalPosition);

                float distSqr = (worldPos - pinWorldPos).sqrMagnitude;
                if (distSqr < closestDistSqr)
                {
                    closestDistSqr = distSqr;
                    pinName = kvp.Key;
                    snapPosition = pinWorldPos;
                    found = true;
                }
            }

            return found;
        }
    }
}
