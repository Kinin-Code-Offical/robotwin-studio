using UnityEngine;
using System.Collections.Generic;

namespace RobotWin.Hardware
{
    // --- Data Types for valid ".rtcomp" communication ---

    public enum SignalType
    {
        AnalogVoltage, // 0-24V
        DigitalLogic,  // 0/1 (Threshold based)
        PowerRail,     // VCC/GND (high current)
        HighFreqData   // SPI/I2C (Simplified)
    }

    [System.Serializable]
    public struct CircuitPin
    {
        public string PinName;
        public Vector3 LocalPosition; // For 3D Probe snapping
        public SignalType Type;
        public float CurrentRate; // Amps
        public float Voltage;     // Volts
    }

    [System.Serializable]
    public class HardwareFault
    {
        public string FaultID; // e.g., "C4_EXPLOSION"
        public string TriggerCondition; // e.g., "Voltage > 16V"
        public float CurrentProbability; // 0.0 to 1.0
        public bool IsActive;
    }

    /// <summary>
    /// The contract that ALL Hardware Modules (.rtcomp) must obey.
    /// This allows the Gameplay Engine to talk to ANY custom circuit.
    /// </summary>
    public interface IHardwareModule
    {
        // 1. Initialization
        void Initialize(string configJson);

        // 2. Real-Time Physics Loop (runs on Thread)
        void SimulateStep(float variableDeltaTime, float ambientTemp);

        // 3. Interaction
        float ProbePinVoltage(string pinName);
        void SetInputVoltage(string pinName, float voltage);

        // 4. State
        float GetSurfaceTemperature();
        float GetTotalCurrentDraw(); // For Battery Simulation
    }

    /// <summary>
    /// The contract for Test Instruments (Oscilloscope, Multimeter).
    /// </summary>
    public interface IVirtualInstrument
    {
        void ConnectProbe(int channel, IHardwareModule targetModule, string pinName);
        void DisconnectProbe(int channel);
        void SetActive(bool state);
    }
}
