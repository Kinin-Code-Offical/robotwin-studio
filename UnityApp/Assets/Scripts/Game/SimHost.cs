using UnityEngine;
using RobotTwin.CoreSim.Specs;
using RobotTwin.Core;
using RobotTwin.CoreSim;
using RobotTwin.CoreSim.Engine;
using RobotTwin.CoreSim.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RobotTwin.Game
{
    /// <summary>
    /// The "Brain" of the simulation.
    /// Orchestrates NativeEngine (Physics) and Firmware (Logic).
    /// </summary>
    public class SimHost : MonoBehaviour
    {
        public static SimHost Instance { get; private set; }
        public delegate void TickHandler(double time);
        public event TickHandler OnTickComplete;
        public event Action<string, string> OnSerialOutput;

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
        private bool _loggedTelemetrySample;
        private bool _loggedNativeFallback;
        private bool _nativePinsReady;
        private readonly Dictionary<string, int> _nativeAvrIndex = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _pinNetMap = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        private bool _useExternalFirmware;
        private bool _useNativeEngine;
        private string _externalFirmwareExePath;
        private string _serialBuffer = string.Empty;
        private readonly Dictionary<string, ExternalFirmwareSession> _externalFirmwareSessions =
            new Dictionary<string, ExternalFirmwareSession>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _virtualSerialNextTime = new Dictionary<string, double>();
        private readonly Dictionary<string, bool> _usbConnectedByBoard = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _boardPowerById = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);

        private const int ExternalFirmwarePinCount = 20;
        private const int SerialBufferLimit = 4000;
        private static readonly string[] ExternalFirmwarePins =
        {
            "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D10", "D11", "D12", "D13",
            "A0", "A1", "A2", "A3", "A4", "A5"
        };

        private class ExternalFirmwareSession
        {
            public string BoardId;
            public string BvmPath;
            public string PipeName;
            public FirmwareClient Client;
            public bool Loaded;
            public bool LoggedFallback;
        }

        public float SimTime => Time.time;
        public int TickCount { get; private set; }
        public CircuitSpec Circuit => _circuit;
        public TelemetryFrame LastTelemetry { get; private set; }
        public string SerialOutput => _serialBuffer;
        public IReadOnlyDictionary<string, bool> BoardPowerById => _boardPowerById;

        public void SetUsbConnected(string boardId, bool connected)
        {
            if (string.IsNullOrWhiteSpace(boardId)) return;
            _usbConnectedByBoard[boardId] = connected;
        }

        private void Awake()
        {
             if (Instance != null) Destroy(gameObject);
             Instance = this;
             DontDestroyOnLoad(gameObject);
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
            _loggedTelemetrySample = false;
            _loggedNativeFallback = false;
            _externalFirmwareSessions.Clear();
            _externalFirmwareExePath = null;

            Debug.Log("[SimHost] Starting Simulation Loop...");

            _circuit = SessionManager.Instance != null ? SessionManager.Instance.CurrentCircuit : null;
            BuildPinNetMap();

            _useExternalFirmware = TrySetupExternalFirmwares();
            if (_useExternalFirmware)
            {
                foreach (var session in _externalFirmwareSessions.Values)
                {
                    session.Client.LaunchFirmware(_externalFirmwareExePath);
                    TryLoadExternalFirmware(session);
                }
            }
            BuildVirtualArduinos();

            _useNativeEngine = !_useExternalFirmware
                && SessionManager.Instance != null
                && SessionManager.Instance.UseNativeEnginePins
                && !SessionManager.Instance.UseVirtualArduino;

            // 3. Initialize Native Engine
            if (_useNativeEngine)
            {
                int engines = NativeBridge.GetVersion();
                Debug.Log($"[SimHost] NativeEngine v{engines} Linked.");
            }

            _coreSim = new CoreSimRuntime();
            if (_useNativeEngine)
            {
                InitializeNativePins();
            }
        }

        public void StopSimulation()
        {
            _isRunning = false;
            foreach (var session in _externalFirmwareSessions.Values)
            {
                session.Client?.StopFirmware();
            }
            _externalFirmwareSessions.Clear();
            _externalFirmwareExePath = null;
            _useExternalFirmware = false;
            _useNativeEngine = false;
            _nativePinsReady = false;
            _nativeAvrIndex.Clear();
            _virtualArduinos.Clear();
            _pinVoltages.Clear();
            _pinStatesByComponent.Clear();
            _pullupResistances.Clear();
            _virtualSerialNextTime.Clear();
            _boardPowerById.Clear();
            _serialBuffer = string.Empty;
            LastTelemetry = null;
            TickCount = 0;
            _tickTimer = 0f;
            Debug.Log("[SimHost] Simulation Stopped.");
        }

        private void FixedUpdate()
        {
            if (!_isRunning) return;

            // Step Physics (High Frequency)
            if (_useNativeEngine && _nativePinsReady)
            {
                NativeBridge.StepSimulation(Time.fixedDeltaTime);
            }
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
                RefreshBoardPowerStates();
                bool usedNativePins = TryApplyNativePins();
                HashSet<string> handledBoards = null;
                if (!usedNativePins && _useExternalFirmware)
                {
                    foreach (var session in _externalFirmwareSessions.Values)
                    {
                        if (!IsBoardPowered(session.BoardId))
                        {
                            continue;
                        }
                        if (TryStepExternalFirmware(session))
                        {
                            if (handledBoards == null)
                            {
                                handledBoards = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                            }
                            handledBoards.Add(session.BoardId);
                        }
                    }
                }
                if (!usedNativePins)
                {
                    foreach (var arduino in _virtualArduinos.Values)
                    {
                        if (!IsBoardPowered(arduino.Id))
                        {
                            continue;
                        }
                        if (handledBoards != null && handledBoards.Contains(arduino.Id))
                        {
                            continue;
                        }
                        arduino.Step(TICK_RATE);
                        arduino.CopyVoltages(_pinVoltages);
                        _pinStatesByComponent[arduino.Id] = arduino.Hal.GetPinStates();
                        _pullupResistances[arduino.Id] = arduino.Hal.GetPullupResistance();
                        TryAppendVirtualSerial(arduino.Id);
                    }
                }
                LastTelemetry = _coreSim.Step(_circuit, _pinVoltages, _pinStatesByComponent, _pullupResistances, TICK_RATE, _boardPowerById, _usbConnectedByBoard);
                if (LastTelemetry != null)
                {
                    LastTelemetry.TimeSeconds = SimTime;
                    LastTelemetry.TickIndex = TickCount;
                    if (!_loggedTelemetry)
                    {
                        _loggedTelemetry = true;
                        Debug.Log($"[SimHost] CoreSim solved {LastTelemetry.Signals.Count} signals.");
                    }
                    if (!_loggedTelemetrySample)
                    {
                        _loggedTelemetrySample = true;
                        LogTelemetrySample();
                    }
                }
            }

            OnTickComplete?.Invoke(SimTime);
        }

        private void InitializeNativePins()
        {
            _nativePinsReady = false;
            _nativeAvrIndex.Clear();
            if (!_useNativeEngine || SessionManager.Instance == null || !SessionManager.Instance.UseNativeEnginePins)
            {
                return;
            }

            NativeBridge.Native_DestroyContext();
            NativeBridge.Native_CreateContext();

            if (_circuit == null || _circuit.Components == null)
            {
                Debug.LogWarning("[SimHost] No circuit available for native pins.");
                return;
            }

            int index = 0;
            foreach (var comp in _circuit.Components)
            {
                if (!IsArduinoType(comp.Type)) continue;
                int nativeId = NativeBridge.Native_AddComponent((int)NativeBridge.ComponentType.IC_Pin, 0, new float[0]);
                if (nativeId <= 0)
                {
                    Debug.LogWarning($"[SimHost] Failed to create native AVR for {comp.Id}.");
                    continue;
                }

                for (int i = 0; i < 20; i++)
                {
                    int nodeId = NativeBridge.Native_AddNode();
                    NativeBridge.Native_Connect(nativeId, i, nodeId);
                }

                _nativeAvrIndex[comp.Id] = index;
                index++;
            }

            if (_nativeAvrIndex.Count == 0)
            {
                Debug.LogWarning("[SimHost] No Arduino components for native pins.");
                return;
            }

            if (TryLoadNativeFirmware())
            {
                _nativePinsReady = true;
                Debug.Log("[SimHost] NativeEngine firmware loaded.");
            }
            else
            {
                Debug.LogWarning("[SimHost] NativeEngine firmware missing; falling back to VirtualArduino.");
            }
        }

        private bool TryLoadNativeFirmware()
        {
            if (_circuit == null || _circuit.Components == null) return false;
            bool loadedAny = false;
            foreach (var comp in _circuit.Components)
            {
                if (!IsArduinoType(comp.Type)) continue;
                if (!_nativeAvrIndex.TryGetValue(comp.Id, out var index)) continue;
                if (comp.Properties == null) continue;

                if (comp.Properties.TryGetValue("bvmPath", out var bvmPath) && File.Exists(bvmPath))
                {
                    loadedAny |= BridgeInterface.LoadBvmFileForAvr(index, bvmPath);
                    continue;
                }

                if (comp.Properties.TryGetValue("firmwarePath", out var hexPath) && File.Exists(hexPath))
                {
                    loadedAny |= BridgeInterface.LoadHexFileForAvr(index, hexPath);
                    continue;
                }

                if (comp.Properties.TryGetValue("firmware", out var firmwarePath))
                {
                    if (!string.IsNullOrWhiteSpace(firmwarePath) &&
                        firmwarePath.EndsWith(".hex", System.StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(firmwarePath))
                    {
                        loadedAny |= BridgeInterface.LoadHexFileForAvr(index, firmwarePath);
                    }
                }
            }

            return loadedAny;
        }

        private bool TryApplyNativePins()
        {
            if (_useExternalFirmware || !_useNativeEngine)
            {
                return false;
            }
            if (SessionManager.Instance == null || !SessionManager.Instance.UseNativeEnginePins || !_nativePinsReady)
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
                if (!_nativeAvrIndex.TryGetValue(comp.Id, out var index)) continue;
                ApplyPinGroup(comp.Id, index);
            }

            return true;
        }

        private void ApplyPinGroup(string componentId, int boardIndex)
        {
            for (int i = 0; i <= 7; i++)
            {
                _pinVoltages[$"{componentId}.D{i}"] = BridgeInterface.GetPinVoltageForAvr(boardIndex, i);
            }
            for (int i = 0; i <= 5; i++)
            {
                _pinVoltages[$"{componentId}.D{8 + i}"] = BridgeInterface.GetPinVoltageForAvr(boardIndex, 8 + i);
            }
            for (int i = 0; i <= 5; i++)
            {
                _pinVoltages[$"{componentId}.A{i}"] = BridgeInterface.GetPinVoltageForAvr(boardIndex, 14 + i);
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
                string firmwareValue = null;
                bool hasFirmwareKey = comp.Properties != null && comp.Properties.TryGetValue("firmware", out firmwareValue);
                if (!hasFirmwareKey || string.IsNullOrWhiteSpace(firmwareValue))
                {
                    Debug.LogError($"[SimHost] {board.Id} has no firmware loaded.");
                }
                Debug.Log($"[SimHost] VirtualArduino Active: {board.Id} ({(string.IsNullOrWhiteSpace(board.FirmwareSource) ? "none" : board.FirmwareSource)})");
                _virtualArduinos[comp.Id] = board;
                if (!_usbConnectedByBoard.ContainsKey(comp.Id))
                {
                    _usbConnectedByBoard[comp.Id] = true;
                }
            }
        }

        private bool IsArduinoType(string type)
        {
            return string.Equals(type, "ArduinoUno", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "ArduinoNano", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "ArduinoProMini", System.StringComparison.OrdinalIgnoreCase);
        }

        private void BuildPinNetMap()
        {
            _pinNetMap.Clear();
            if (_circuit?.Nets == null) return;
            foreach (var net in _circuit.Nets)
            {
                if (string.IsNullOrWhiteSpace(net?.Id) || net.Nodes == null) continue;
                foreach (var node in net.Nodes)
                {
                    if (string.IsNullOrWhiteSpace(node)) continue;
                    _pinNetMap[node] = net.Id;
                }
            }
        }

        private void RefreshBoardPowerStates()
        {
            _boardPowerById.Clear();
            if (_circuit?.Components == null) return;
            foreach (var comp in _circuit.Components)
            {
                if (!IsArduinoType(comp.Type)) continue;
                _boardPowerById[comp.Id] = IsBoardPowered(comp.Id);
            }
        }

        private bool IsBoardPowered(string boardId)
        {
            if (string.IsNullOrWhiteSpace(boardId)) return false;
            if (IsUsbConnected(boardId)) return true;
            if (_circuit?.Components == null) return false;

            var supplyNets = GetBoardSupplyNets(boardId);
            if (supplyNets.Count == 0) return false;

            var batteryNets = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var comp in _circuit.Components)
            {
                if (!string.Equals(comp.Type, "Battery", System.StringComparison.OrdinalIgnoreCase)) continue;
                string netPlus = GetNetFor(comp.Id, "+");
                if (!string.IsNullOrWhiteSpace(netPlus))
                {
                    batteryNets.Add(netPlus);
                }
            }

            if (batteryNets.Count == 0) return false;
            if (supplyNets.Overlaps(batteryNets)) return true;

            foreach (var comp in _circuit.Components)
            {
                if (!IsSwitchComponent(comp)) continue;
                if (!IsSwitchClosed(comp)) continue;
                string netA = GetNetFor(comp.Id, "A");
                string netB = GetNetFor(comp.Id, "B");
                if (string.IsNullOrWhiteSpace(netA) || string.IsNullOrWhiteSpace(netB)) continue;
                if ((supplyNets.Contains(netA) && batteryNets.Contains(netB)) ||
                    (supplyNets.Contains(netB) && batteryNets.Contains(netA)))
                {
                    return true;
                }
            }

            return false;
        }

        private HashSet<string> GetBoardSupplyNets(string boardId)
        {
            var nets = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            string[] pins = { "VIN", "5V", "3V3", "IOREF", "VCC" };
            foreach (var pin in pins)
            {
                string net = GetNetFor(boardId, pin);
                if (!string.IsNullOrWhiteSpace(net))
                {
                    nets.Add(net);
                }
            }
            return nets;
        }

        private string GetNetFor(string compId, string pin)
        {
            if (string.IsNullOrWhiteSpace(compId) || string.IsNullOrWhiteSpace(pin)) return string.Empty;
            string key = $"{compId}.{pin}";
            return _pinNetMap.TryGetValue(key, out var net) ? net : string.Empty;
        }

        private static bool IsSwitchComponent(ComponentSpec comp)
        {
            if (comp == null) return false;
            return string.Equals(comp.Type, "Switch", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(comp.Type, "Button", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSwitchClosed(ComponentSpec comp)
        {
            if (comp?.Properties == null) return false;
            if (TryGetBool(comp.Properties, "closed", out var closed)) return closed;
            if (TryGetBool(comp.Properties, "pressed", out var pressed)) return pressed;
            if (comp.Properties.TryGetValue("state", out var state))
            {
                string value = (state ?? string.Empty).Trim().ToLowerInvariant();
                return value == "closed" || value == "on" || value == "pressed" || value == "true";
            }
            return false;
        }

        private static bool TryGetBool(Dictionary<string, string> props, string key, out bool value)
        {
            value = false;
            if (props == null || string.IsNullOrWhiteSpace(key)) return false;
            if (!props.TryGetValue(key, out var raw)) return false;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim().ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes" || s == "on" || s == "closed" || s == "pressed")
            {
                value = true;
                return true;
            }
            if (s == "false" || s == "0" || s == "no" || s == "off" || s == "open")
            {
                value = false;
                return true;
            }
            return false;
        }

        private bool TrySetupExternalFirmwares()
        {
            if (SessionManager.Instance == null || _circuit == null || _circuit.Components == null) return false;

            SessionManager.Instance.FindFirmware();
            var firmwarePath = SessionManager.Instance.FirmwarePath;
            if (string.IsNullOrWhiteSpace(firmwarePath) || !File.Exists(firmwarePath)) return false;

            if (SessionManager.Instance.UseVirtualArduino)
            {
                Debug.Log("[SimHost] VirtualArduino enabled; external firmware disabled.");
                return false;
            }

            var boards = _circuit.Components.Where(c => IsArduinoType(c.Type)).ToList();
            if (boards.Count == 0) return false;

            foreach (var board in boards)
            {
                if (!TryGetBvmPath(board, out var bvmPath))
                {
                    continue;
                }
                if (!File.Exists(bvmPath))
                {
                    Debug.LogWarning($"[SimHost] Missing .bvm for {board.Id}: {bvmPath}");
                    continue;
                }

                var session = new ExternalFirmwareSession
                {
                    BoardId = board.Id,
                    BvmPath = bvmPath,
                    PipeName = BuildPipeName(board.Id)
                };
                session.Client = CreateFirmwareClient(session);
                _externalFirmwareSessions[board.Id] = session;
                if (!_usbConnectedByBoard.ContainsKey(board.Id))
                {
                    _usbConnectedByBoard[board.Id] = true;
                }
            }

            if (_externalFirmwareSessions.Count == 0) return false;

            _externalFirmwareExePath = firmwarePath;
            Debug.Log($"[SimHost] External firmware enabled for {_externalFirmwareSessions.Count} board(s).");
            return true;
        }

        private FirmwareClient CreateFirmwareClient(ExternalFirmwareSession session)
        {
            var go = new GameObject($"FirmwareClient_{session.BoardId}");
            go.transform.SetParent(transform);
            var client = go.AddComponent<FirmwareClient>();
            client.Configure(session.PipeName);
            return client;
        }

        private static string BuildPipeName(string boardId)
        {
            string token = SanitizePipeToken(boardId);
            if (string.IsNullOrWhiteSpace(token))
            {
                token = "board";
            }
            return $"RoboTwin.FirmwareEngine.v1.{token}";
        }

        private static string SanitizePipeToken(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var sb = new System.Text.StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }
            return sb.ToString();
        }

        private static bool TryGetBvmPath(ComponentSpec board, out string bvmPath)
        {
            bvmPath = null;
            if (board?.Properties == null) return false;
            if (board.Properties.TryGetValue("bvmPath", out bvmPath) && !string.IsNullOrWhiteSpace(bvmPath))
            {
                return true;
            }
            if (board.Properties.TryGetValue("firmwarePath", out var altPath) &&
                !string.IsNullOrWhiteSpace(altPath) &&
                altPath.EndsWith(".bvm", System.StringComparison.OrdinalIgnoreCase))
            {
                bvmPath = altPath;
                return true;
            }
            return false;
        }

        private void TryLoadExternalFirmware(ExternalFirmwareSession session)
        {
            if (!_useExternalFirmware || session == null || session.Client == null) return;
            if (session.Loaded) return;
            if (string.IsNullOrWhiteSpace(session.BvmPath) || !File.Exists(session.BvmPath))
            {
                Debug.LogWarning($"[SimHost] External firmware missing .bvm file for {session.BoardId}.");
                return;
            }

            if (TryLoadPendingFirmware(session))
            {
                Debug.Log($"[SimHost] External firmware loaded: {session.BvmPath}");
            }
            else
            {
                Debug.LogWarning($"[SimHost] External firmware pending (pipe not ready): {session.BvmPath}");
            }
        }

        private bool TryLoadPendingFirmware(ExternalFirmwareSession session)
        {
            if (session == null || session.Client == null) return false;
            if (session.Loaded) return true;
            if (string.IsNullOrWhiteSpace(session.BvmPath) || !File.Exists(session.BvmPath)) return false;
            if (!session.Client.LoadBvmFile(session.BvmPath)) return false;
            session.Loaded = true;
            return true;
        }

        private bool TryStepExternalFirmware(ExternalFirmwareSession session)
        {
            if (session == null || session.Client == null) return false;

            TryLoadPendingFirmware(session);

            var request = new RobotTwin.CoreSim.FirmwareStepRequest
            {
                RailVoltage = 5.0f,
                DeltaMicros = (uint)Mathf.RoundToInt(TICK_RATE * 1000000f),
                PinStates = BuildFirmwareInputStates(session.BoardId)
            };

            if (!session.Client.TryStep(request, out var result))
            {
                if (!session.LoggedFallback)
                {
                    session.LoggedFallback = true;
                    Debug.LogWarning($"[SimHost] External firmware not responding for {session.BoardId}. Falling back to VirtualArduino.");
                }
                return false;
            }

            ApplyFirmwareResult(session.BoardId, result);
            return true;
        }

        private int[] BuildFirmwareInputStates(string boardId)
        {
            var inputs = new int[ExternalFirmwarePinCount];
            for (int i = 0; i < ExternalFirmwarePinCount; i++)
            {
                string pin = ExternalFirmwarePins[i];
                float voltage = GetLastNetVoltage(boardId, pin);
                inputs[i] = voltage >= 2.5f ? 1 : 0;
            }
            return inputs;
        }

        private float GetLastNetVoltage(string boardId, string pin)
        {
            if (LastTelemetry == null || LastTelemetry.Signals == null) return 0f;
            string key = $"{boardId}.{pin}";
            if (!_pinNetMap.TryGetValue(key, out var netId)) return 0f;
            if (string.IsNullOrWhiteSpace(netId)) return 0f;
            if (LastTelemetry.Signals.TryGetValue($"NET:{netId}", out var netVoltage))
            {
                return (float)netVoltage;
            }
            return 0f;
        }

        private void ApplyFirmwareResult(string boardId, RobotTwin.CoreSim.FirmwareStepResult result)
        {
            var pinStates = new List<PinState>(ExternalFirmwarePinCount);
            int[] outputs = result?.PinStates ?? System.Array.Empty<int>();

            for (int i = 0; i < ExternalFirmwarePinCount; i++)
            {
                string pin = ExternalFirmwarePins[i];
                int state = i < outputs.Length ? outputs[i] : -1;
                bool isOutput = state >= 0;
                if (isOutput)
                {
                    _pinVoltages[$"{boardId}.{pin}"] = state > 0 ? VirtualArduino.DefaultHighVoltage : 0f;
                }
                pinStates.Add(new PinState(pin, isOutput, false));
            }

            _pinStatesByComponent[boardId] = pinStates;
            _pullupResistances[boardId] = 20000.0;

            if (result != null && !string.IsNullOrWhiteSpace(result.SerialOutput))
            {
                AppendSerialOutput(boardId, result.SerialOutput);
            }
        }

        private void TryAppendVirtualSerial(string boardId)
        {
            if (_circuit?.Components == null) return;
            var comp = _circuit.Components.FirstOrDefault(c => c.Id == boardId);
            if (comp?.Properties == null) return;
            if (!comp.Properties.TryGetValue("virtualSerial", out var text) || string.IsNullOrWhiteSpace(text)) return;

            double intervalMs = 1000;
            if (comp.Properties.TryGetValue("virtualSerialIntervalMs", out var raw) &&
                double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                intervalMs = parsed;
            }

            double nowMs = Time.time * 1000.0;
            if (_virtualSerialNextTime.TryGetValue(boardId, out var nextMs) && nowMs < nextMs)
            {
                return;
            }

            if (!text.EndsWith("\n"))
            {
                text += "\n";
            }
            AppendSerialOutput(boardId, text);
            _virtualSerialNextTime[boardId] = nowMs + intervalMs;
        }

        private void AppendSerialOutput(string boardId, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!IsUsbConnected(boardId)) return;

            string prefix = string.IsNullOrWhiteSpace(boardId) ? "[SYS]" : $"[{boardId}]";
            string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            OnSerialOutput?.Invoke(boardId, normalized);
            bool endsWithNewline = text.EndsWith("\n") || text.EndsWith("\r");
            string[] lines = normalized.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                bool isLast = i == lines.Length - 1;
                if (isLast && !endsWithNewline)
                {
                    if (lines[i].Length == 0) continue;
                    _serialBuffer += $"{prefix} {lines[i]}";
                }
                else
                {
                    _serialBuffer += $"{prefix} {lines[i]}\n";
                }
            }
            if (_serialBuffer.Length > SerialBufferLimit)
            {
                _serialBuffer = _serialBuffer.Substring(_serialBuffer.Length - SerialBufferLimit);
            }
        }

        private bool IsUsbConnected(string boardId)
        {
            if (string.IsNullOrWhiteSpace(boardId)) return true;
            return !_usbConnectedByBoard.TryGetValue(boardId, out var connected) || connected;
        }

        private void LogTelemetrySample()
        {
            if (_circuit?.Components == null || LastTelemetry?.Signals == null) return;
            var board = _circuit.Components.FirstOrDefault(c => IsArduinoType(c.Type));
            string boardId = board?.Id ?? "U1";
            float d13 = _pinVoltages.TryGetValue($"{boardId}.D13", out var d13v) ? d13v : float.NaN;
            double r1i = LastTelemetry.Signals.TryGetValue("COMP:R1:I", out var r1Current) ? r1Current : double.NaN;
            double d1i = LastTelemetry.Signals.TryGetValue("COMP:D1:I", out var d1Current) ? d1Current : double.NaN;
            double netD13 = LastTelemetry.Signals.TryGetValue("NET:NET_D13", out var netD13v) ? netD13v : double.NaN;
            double netLed = LastTelemetry.Signals.TryGetValue("NET:NET_LED", out var netLedv) ? netLedv : double.NaN;
            double netGnd = LastTelemetry.Signals.TryGetValue("NET:GND", out var netGndv) ? netGndv : double.NaN;
            string validation = LastTelemetry.ValidationMessages.Count == 0
                ? "none"
                : string.Join("; ", LastTelemetry.ValidationMessages);
            Debug.Log($"[SimHost] Telemetry sample: {boardId}.D13={d13:F2}V NET_D13={netD13:F2}V NET_LED={netLed:F2}V GND={netGnd:F2}V R1.I={r1i:F6}A D1.I={d1i:F6}A Validation={validation}");
        }

        private void OnDestroy()
        {
            StopSimulation();
        }
    }
}
