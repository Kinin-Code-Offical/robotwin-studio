using UnityEngine;
using RobotTwin.CoreSim.Specs;
using RobotTwin.Core;
using RobotTwin.CoreSim;
using RobotTwin.CoreSim.Engine;
using RobotTwin.CoreSim.Runtime;
using System.Collections.Generic;

namespace RobotTwin.Game
{
    /// <summary>
    /// The "Brain" of the simulation.
    /// Orchestrates NativeEngine (Physics) and Firmware (Logic).
    /// </summary>
    [RequireComponent(typeof(FirmwareClient))]
    public class SimHost : MonoBehaviour
    {
        public static SimHost Instance { get; private set; }
        public delegate void TickHandler(double time);
        public event TickHandler OnTickComplete;

        private FirmwareClient _firmware;
        private CircuitSpec _circuit;
        private bool _isRunning = false;
        private float _tickTimer = 0f;
        private const float TICK_RATE = 0.1f; // 10Hz Firmware Tick
        private CoreSimRuntime _coreSim;
        private readonly Dictionary<string, float> _pinVoltages = new Dictionary<string, float>();
        private readonly Dictionary<string, VirtualArduino> _virtualArduinos = new Dictionary<string, VirtualArduino>();
        private readonly Dictionary<string, List<PinState>> _pinStatesByComponent = new Dictionary<string, List<PinState>>();
        private readonly Dictionary<string, double> _pullupResistances = new Dictionary<string, double>();
        private bool _loggedTelemetry;
        private bool _loggedNativeFallback;

        public float SimTime => Time.time;
        public int TickCount { get; private set; }
        public CircuitSpec Circuit => _circuit;
        public TelemetryFrame LastTelemetry { get; private set; }

        private void Awake()
        {
             if (Instance != null) Destroy(gameObject);
             Instance = this;
             DontDestroyOnLoad(gameObject);
             _firmware = GetComponent<FirmwareClient>();
        }

        private void Start()
        {
            // If loaded in RunMode directly or Session Started
            if (SessionManager.Instance != null && SessionManager.Instance.CurrentCircuit != null)
            {
                // Auto-hook if needed, but usually waiting for "Run" command or scene load
            }
        }

        public void BeginSimulation()
        {
            if (_isRunning) return;
            _isRunning = true;
            _loggedTelemetry = false;
            _loggedNativeFallback = false;

            Debug.Log("[SimHost] Starting Simulation Loop...");

            _circuit = SessionManager.Instance != null ? SessionManager.Instance.CurrentCircuit : null;
            BuildVirtualArduinos();

            bool useVirtualArduino = _virtualArduinos.Count > 0;
            if (!useVirtualArduino && SessionManager.Instance != null)
            {
                // 1. Resolve Firmware Path
                SessionManager.Instance.FindFirmware();
                var fwPath = SessionManager.Instance.FirmwarePath;

                // 2. Launch Firmware Process
                if (!string.IsNullOrEmpty(fwPath))
                {
                    _firmware.LaunchFirmware(fwPath);
                }
                else
                {
                    Debug.LogWarning("[SimHost] Firmware Path invalid. Logic will be disabled.");
                }
            }

            // 3. Initialize Native Engine
            int engines = NativeBridge.GetVersion();
            Debug.Log($"[SimHost] NativeEngine v{engines} Linked.");

            _coreSim = new CoreSimRuntime();
        }

        public void StopSimulation()
        {
            _isRunning = false;
            _firmware.StopFirmware();
            Debug.Log("[SimHost] Simulation Stopped.");
        }

        private void FixedUpdate()
        {
            if (!_isRunning) return;

            // Step Physics (High Frequency)
            NativeBridge.StepSimulation(Time.fixedDeltaTime);
        }

        private void Update()
        {
            if (!_isRunning) return;

            // Step Logic (Low Frequency / Tick)
            _tickTimer += Time.deltaTime;
            if (_tickTimer >= TICK_RATE)
            {
                _tickTimer -= TICK_RATE;
                TickCount++;
                OnTick();
            }
        }

        private void OnTick()
        {
            // Sync with Firmware
            // In full impl, we'd read pipe messages here
            // _firmware.SendIOState("PIN_1", 1);
            if (_coreSim != null && _circuit != null)
            {
                _pinVoltages.Clear();
                _pinStatesByComponent.Clear();
                _pullupResistances.Clear();
                bool usedNativePins = TryApplyNativePins();
                if (!usedNativePins)
                {
                    foreach (var arduino in _virtualArduinos.Values)
                    {
                        arduino.Step(TICK_RATE);
                        arduino.CopyVoltages(_pinVoltages);
                        _pinStatesByComponent[arduino.Id] = arduino.Hal.GetPinStates();
                        _pullupResistances[arduino.Id] = arduino.Hal.GetPullupResistance();
                    }
                }
                LastTelemetry = _coreSim.Step(_circuit, _pinVoltages, _pinStatesByComponent, _pullupResistances, TICK_RATE);
                if (LastTelemetry != null)
                {
                    LastTelemetry.TimeSeconds = SimTime;
                    LastTelemetry.TickIndex = TickCount;
                    if (!_loggedTelemetry)
                    {
                        _loggedTelemetry = true;
                        Debug.Log($"[SimHost] CoreSim solved {LastTelemetry.Signals.Count} signals.");
                    }
                }
            }

            OnTickComplete?.Invoke(SimTime);
        }

        private bool TryApplyNativePins()
        {
            if (SessionManager.Instance == null || !SessionManager.Instance.UseNativeEnginePins)
            {
                return false;
            }

            if (!BridgeInterface.TryReadState(out var state))
            {
                if (!_loggedNativeFallback)
                {
                    _loggedNativeFallback = true;
                    Debug.LogWarning("[SimHost] NativeEngine bridge not available. Falling back to VirtualArduino.");
                }
                return false;
            }

            if (_circuit == null || _circuit.Components == null) return false;
            foreach (var comp in _circuit.Components)
            {
                if (!IsArduinoType(comp.Type)) continue;
                ApplyPinGroup(comp.Id, ref state);
            }

            return true;
        }

        private void ApplyPinGroup(string componentId, ref BridgeInterface.SharedState state)
        {
            for (int i = 0; i <= 7; i++)
            {
                _pinVoltages[$"{componentId}.D{i}"] = BridgeInterface.GetPinVoltage(ref state, i);
            }
            for (int i = 0; i <= 5; i++)
            {
                _pinVoltages[$"{componentId}.D{8 + i}"] = BridgeInterface.GetPinVoltage(ref state, 8 + i);
            }
            for (int i = 0; i <= 5; i++)
            {
                _pinVoltages[$"{componentId}.A{i}"] = BridgeInterface.GetPinVoltage(ref state, 14 + i);
            }
        }

        public void SetPinVoltage(string componentId, string pinName, float voltage)
        {
            if (string.IsNullOrWhiteSpace(componentId) || string.IsNullOrWhiteSpace(pinName)) return;
            if (_virtualArduinos.TryGetValue(componentId, out var board))
            {
                board.SetVoltage(pinName, voltage);
                return;
            }
            _pinVoltages[$"{componentId}.{pinName}"] = voltage;
        }

        private void BuildVirtualArduinos()
        {
            _virtualArduinos.Clear();
            _pinVoltages.Clear();
            if (_circuit == null || _circuit.Components == null) return;

            foreach (var comp in _circuit.Components)
            {
                if (!IsArduinoType(comp.Type)) continue;
                var board = new VirtualArduino(comp.Id);
                if (comp.Properties != null)
                {
                    board.ConfigureFromProperties(comp.Properties);
                }
                if (comp.Properties == null || !comp.Properties.ContainsKey("firmware"))
                {
                    board.LoadProgram(VirtualArduinoProgramFactory.FromFirmwareString("blink:D13:500", board.Hal));
                }
                Debug.Log($"[SimHost] VirtualArduino Active: {board.Id} ({board.FirmwareSource})");
                _virtualArduinos[comp.Id] = board;
            }
        }

        private bool IsArduinoType(string type)
        {
            return string.Equals(type, "ArduinoUno", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "ArduinoNano", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "ArduinoProMini", System.StringComparison.OrdinalIgnoreCase);
        }

        private void OnDestroy()
        {
            StopSimulation();
        }
    }
}
