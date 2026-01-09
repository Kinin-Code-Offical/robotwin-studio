using System.Collections.Generic;
using UnityEngine;

namespace RobotWin.Hardware.Core
{
    public enum FaultType
    {
        OpenCircuit,        // Infinite Resistance
        ShortProximity,     // Connect to Nearest Neighbor Pin
        ShortToGround,      // Connect to GND
        ParameterDrift      // R/C value changes by +/- 50%
    }

    [System.Serializable]
    public class ActiveFault
    {
        public string ComponentID;
        public FaultType Type;
        public float Severity; // 0.0 to 1.0 (e.g., Short resistance)
    }

    /// <summary>
    /// Database of all possible failures for a specific hardware module.
    /// Used by the 'Compactor' to embed failure modes into the runtime.
    /// </summary>
    public class HardwareFaultDatabase : MonoBehaviour
    {
        // Dictionary<ComponentID, List<PossibleFaults>>
        // Implementation simplified for prototype

        [Header("Active Faults")]
        public List<ActiveFault> injectedFaults = new List<ActiveFault>();

        public void InjectFault(string componentId, FaultType type)
        {
            injectedFaults.Add(new ActiveFault { ComponentID = componentId, Type = type, Severity = 1.0f });
            Debug.LogWarning($"[Hardware] FAULT INJECTED: {componentId} -> {type}");

            // Logic to update the HardwareOptimizedModule state would go here
            // e.g., Finding the resistor R1 inside the black box and setting its value to infinity
        }

        public void ClearFaults()
        {
            injectedFaults.Clear();
        }
    }
}
