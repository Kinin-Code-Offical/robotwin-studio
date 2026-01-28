using UnityEngine;
using RobotTwin.CoreSim.Specs;
using RobotTwin.Core;
using RobotTwin.CoreSim;
using RobotTwin.CoreSim.Engine;
using RobotTwin.CoreSim.Runtime;
using RobotTwin.CoreSim.Models.Physics;
using RobotTwin.Game.RaspberryPi;
using RobotTwin.Tools;
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
        [SerializeField] private RealtimeScheduleConfig _realtimeConfig = new RealtimeScheduleConfig();
        private RealtimeSchedulerState _realtimeState;
        private readonly Dictionary<string, float> _pinVoltages = new Dictionary<string, float>();
        private readonly Dictionary<string, VirtualMcu> _virtualMcus = new Dictionary<string, VirtualMcu>();
        private readonly Dictionary<string, List<PinState>> _pinStatesByComponent = new Dictionary<string, List<PinState>>();
        private readonly Dictionary<string, double> _pullupResistances = new Dictionary<string, double>();
        private bool _loggedTelemetry;
        private bool _loggedTelemetrySample;
        private bool _loggedNativeFallback;
        private bool _loggedUnoVirtualFidelityWarning;
        private bool _nativePinsReady;
        private readonly Dictionary<string, int> _nativeAvrIndex = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _pinNetMap = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        private bool _useExternalFirmware;
        private bool _useFirmwareHost;
        private bool _useNativeEngine;
        private string _externalFirmwareExePath;
        private FirmwareClient _externalFirmwareClient;
        private string _serialBuffer = string.Empty;
        private readonly Dictionary<string, ExternalFirmwareSession> _externalFirmwareSessions =
            new Dictionary<string, ExternalFirmwareSession>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _virtualSerialNextTime = new Dictionary<string, double>();
        private readonly Dictionary<string, bool> _usbConnectedByBoard = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _boardPowerById = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FirmwarePerfCounters> _firmwarePerfByBoard =
            new Dictionary<string, FirmwarePerfCounters>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FirmwareDebugCounters> _firmwareDebugByBoard =
            new Dictionary<string, FirmwareDebugCounters>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FirmwareDebugBitset> _firmwareDebugBitsByBoard =
            new Dictionary<string, FirmwareDebugBitset>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int[]> _firmwarePinOutputsByBoard =
            new Dictionary<string, int[]>(System.StringComparer.OrdinalIgnoreCase);
        private RpiRuntimeManager _rpiRuntime;
        private bool _useRpiRuntime;
        private RpiRuntimeConfig _rpiConfig;
        private const int TickTraceCapacity = 120;
        private readonly TickSample[] _tickTrace = new TickSample[TickTraceCapacity];
        private int _tickTraceIndex;
        private int _tickTraceCount;
        private float _lastTickStartTime;
        private bool _timingReady;
        private TimingStats _timingStats;
        private int _lastInputSignature;
        private bool _hasInputSignature;
        private float _lastSolveTimestamp;
        private int _fastPathTicks;
        private int _correctiveTicks;
        private int _budgetOverruns;
        private readonly Dictionary<string, FirmwareInputCache> _firmwareInputCache =
            new Dictionary<string, FirmwareInputCache>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float[]> _analogOverridesByBoard =
            new Dictionary<string, float[]>(System.StringComparer.OrdinalIgnoreCase);

        private const int ExternalFirmwarePinLimit = 20;
        private const int ExternalFirmwareFailureLimit = 3;
        private const float ExternalFirmwareRetryDelay = 1.0f;
        private const float ExternalFirmwareStartupGraceSeconds = 4.0f;
        private const float ExternalFirmwareStartupTimeoutSeconds = 12.0f;
        private const int SerialBufferLimit = 4000;
        private const int VirtualDebugBitCount = 744;
        private readonly Dictionary<string, string[]> _externalFirmwarePinsByBoard =
            new Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _externalFirmwarePinWarnings =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        private class ExternalFirmwareSession
        {
            public string BoardId;
            public string BvmPath;
            public string BoardProfile;
            public bool Loaded;
            public bool LoggedFallback;
            public bool LoggedPending;
            public int FailureCount;
            public float NextRetryTime;
            public float StartTime;
            public bool Disabled;
            public string DisabledReason;
        }

        private sealed class FirmwareInputCache
        {
            public readonly int[] Digital = new int[ExternalFirmwarePinLimit];
            public readonly float[] Analog = new float[16];
            public readonly Dictionary<string, float> LastVoltages =
                new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);
            public int ChangedPins;

            public FirmwareInputCache()
            {
                Array.Fill(Digital, -1);
            }
        }

        private sealed class RealtimeSchedulerState
        {
            public double AccumulatorSeconds;
            public double MasterTimeSeconds;
            public double NextFirmwareAt;
            public double NextCircuitAt;
            public double NextPhysicsAt;
            public bool LastUsedNativePins;

            public void Reset(RealtimeScheduleConfig config)
            {
                AccumulatorSeconds = 0.0;
                MasterTimeSeconds = 0.0;
                NextFirmwareAt = config.FirmwareDtSeconds;
                NextCircuitAt = config.CircuitDtSeconds;
                NextPhysicsAt = config.PhysicsDtSeconds;
                LastUsedNativePins = false;
            }
        }

        public float SimTime => Time.time;
        public int TickCount { get; private set; }
        public CircuitSpec Circuit => _circuit;
        public TelemetryFrame LastTelemetry { get; private set; }
        public string SerialOutput => _serialBuffer;
        public IReadOnlyDictionary<string, bool> BoardPowerById => _boardPowerById;
        public bool IsRunning => _isRunning;
        public bool UseNativeEngine => _useNativeEngine;
        public bool UseExternalFirmware => _useExternalFirmware;
        public bool NativePinsReady => _nativePinsReady;
        public bool UseRpiRuntime => _useRpiRuntime;
        public int ExternalFirmwareSessionCount => _externalFirmwareSessions.Count;
        public int VirtualBoardCount => _virtualMcus.Count;
        public int PoweredBoardCount => _boardPowerById.Count;
        public Texture2D RpiDisplayTexture => _rpiRuntime != null ? _rpiRuntime.DisplayTexture : null;
        public string RpiStatus => _rpiRuntime != null ? _rpiRuntime.Status : "offline";

        public string VirtualComStatusJson { get; private set; } = string.Empty;

        public bool SendSerialInput(string boardId, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (string.IsNullOrWhiteSpace(boardId))
            {
                boardId = "U1";
            }

            if (_useExternalFirmware && _externalFirmwareClient != null)
            {
                if (_externalFirmwareSessions.TryGetValue(boardId, out var session) && session != null && !session.Disabled)
                {
                    return _externalFirmwareClient.SendSerialInput(boardId, text);
                }
                if (_externalFirmwareSessions.Count > 0)
                {
                    var first = _externalFirmwareSessions.Values.FirstOrDefault(s => s != null && !s.Disabled);
                    if (first != null)
                    {
                        return _externalFirmwareClient.SendSerialInput(first.BoardId, text);
                    }
                }
            }

            // VirtualMcu path does not support inbound serial yet; keep local echo.
            AppendSerialOutput(boardId, $"> {text}\n");
            return false;
        }

        public int GetFirmwareBoardIds(List<string> buffer)
        {
            if (buffer == null) return 0;
            buffer.Clear();
            foreach (var key in _firmwareDebugByBoard.Keys)
            {
                buffer.Add(key);
            }
            if (buffer.Count == 0)
            {
                foreach (var key in _firmwarePerfByBoard.Keys)
                {
                    buffer.Add(key);
                }
            }
            return buffer.Count;
        }

        public int GetBoardIds(List<string> buffer)
        {
            if (buffer == null) return 0;
            buffer.Clear();
            foreach (var key in _externalFirmwareSessions.Keys)
            {
                buffer.Add(key);
            }
            foreach (var key in _virtualMcus.Keys)
            {
                if (!buffer.Contains(key))
                {
                    buffer.Add(key);
                }
            }
            return buffer.Count;
        }

        public int[] GetFirmwarePinOutputsSnapshot(string boardId)
        {
            if (string.IsNullOrWhiteSpace(boardId)) return Array.Empty<int>();
            if (_firmwarePinOutputsByBoard.TryGetValue(boardId, out var outputs) && outputs != null && outputs.Length > 0)
            {
                var copy = new int[outputs.Length];
                Array.Copy(outputs, copy, outputs.Length);
                return copy;
            }
            return BuildPinOutputsFromVoltages(boardId);
        }

        public float[] GetFirmwareAnalogInputsSnapshot(string boardId)
        {
            if (string.IsNullOrWhiteSpace(boardId)) return Array.Empty<float>();
            var inputs = BuildFirmwareAnalogInputs(boardId);
            if (inputs == null || inputs.Length == 0) return Array.Empty<float>();
            var copy = new float[inputs.Length];
            Array.Copy(inputs, copy, inputs.Length);
            return copy;
        }

        public bool TryGetPinVoltage(string boardId, string pinName, out float voltage)
        {
            voltage = 0f;
            if (string.IsNullOrWhiteSpace(boardId) || string.IsNullOrWhiteSpace(pinName))
            {
                return false;
            }

            string key = $"{boardId}.{pinName}";
            if (_pinVoltages.TryGetValue(key, out var direct))
            {
                voltage = direct;
                return true;
            }

            if (_pinNetMap.TryGetValue(key, out var netId) && !string.IsNullOrWhiteSpace(netId))
            {
                voltage = GetLastNetVoltage(boardId, pinName);
                return true;
            }

            return false;
        }

        public bool TryGetFirmwareDebugCounters(string boardId, out FirmwareDebugCounters counters)
        {
            counters = null;
            if (string.IsNullOrWhiteSpace(boardId)) return false;
            return _firmwareDebugByBoard.TryGetValue(boardId, out counters);
        }

        public bool TryGetFirmwareDebugBits(string boardId, out FirmwareDebugBitset bits)
        {
            bits = null;
            if (string.IsNullOrWhiteSpace(boardId)) return false;
            return _firmwareDebugBitsByBoard.TryGetValue(boardId, out bits);
        }

        public void SetVirtualComStatusJson(string json)
        {
            VirtualComStatusJson = json ?? string.Empty;
        }

        public struct TimingStats
        {
            public int TickSamples;
            public int JitterSamples;
            public int Overruns;
            public float AvgTickMs;
            public float MaxTickMs;
            public float LastTickMs;
            public float AvgJitterMs;
            public float MaxJitterMs;
            public float LastJitterMs;
        }

        public struct RealtimeBudgetStats
        {
            public int FastPathTicks;
            public int CorrectiveTicks;
            public int BudgetOverruns;
        }

        public struct FirmwareHostTelemetry
        {
            public string ExecutableName;
            public string ResolvedPath;
            public string OverridePath;
            public string PipeName;
            public string Mode;
            public bool ExternalEnabled;
        }

        public readonly struct TickSample
        {
            public readonly int TickIndex;
            public readonly float DtSeconds;
            public readonly float SolveMs;
            public readonly bool UsedNativePins;
            public readonly bool UsedExternalFirmware;

            public TickSample(int tickIndex, float dtSeconds, float solveMs, bool usedNativePins, bool usedExternalFirmware)
            {
                TickIndex = tickIndex;
                DtSeconds = dtSeconds;
                SolveMs = solveMs;
                UsedNativePins = usedNativePins;
                UsedExternalFirmware = usedExternalFirmware;
            }
        }

        public List<TickSample> GetTickTraceSnapshot()
        {
            var list = new List<TickSample>(_tickTraceCount);
            int count = _tickTraceCount;
            if (count <= 0) return list;
            int start = _tickTraceCount < _tickTrace.Length ? 0 : _tickTraceIndex;
            for (int i = 0; i < count; i++)
            {
                int index = (start + i) % _tickTrace.Length;
                list.Add(_tickTrace[index]);
            }
            return list;
        }

        public TimingStats GetTimingStatsSnapshot()
        {
            return _timingStats;
        }

        public RealtimeBudgetStats GetRealtimeBudgetStatsSnapshot()
        {
            return new RealtimeBudgetStats
            {
                FastPathTicks = _fastPathTicks,
                CorrectiveTicks = _correctiveTicks,
                BudgetOverruns = _budgetOverruns
            };
        }

        public FirmwareHostTelemetry GetFirmwareHostTelemetry()
        {
            var host = new FirmwareHostTelemetry
            {
                ExecutableName = string.Empty,
                ResolvedPath = string.Empty,
                OverridePath = string.Empty,
                PipeName = _externalFirmwareClient != null ? _externalFirmwareClient.PipeName : string.Empty,
                Mode = _externalFirmwareClient != null
                    ? (_externalFirmwareClient.LaunchLockstep ? "lockstep" : "realtime")
                    : "unknown",
                ExternalEnabled = _useExternalFirmware
            };

            var session = SessionManager.Instance;
            if (session != null)
            {
                host.ExecutableName = session.FirmwareHostExecutableName ?? string.Empty;
                host.ResolvedPath = session.FirmwarePath ?? string.Empty;
                host.OverridePath = session.ResolveFirmwareHostOverride() ?? string.Empty;
            }

            return host;
        }

        public void SetFirmwareHostMode(bool lockstep)
        {
            if (_externalFirmwareClient != null)
            {
                _externalFirmwareClient.LaunchLockstep = lockstep;
            }
        }

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
            _loggedUnoVirtualFidelityWarning = false;
            _externalFirmwareSessions.Clear();
            _externalFirmwarePinsByBoard.Clear();
            _externalFirmwarePinWarnings.Clear();
            _externalFirmwareExePath = null;
            _firmwarePerfByBoard.Clear();
            _firmwareDebugByBoard.Clear();
            _firmwareDebugBitsByBoard.Clear();
            _timingStats = new TimingStats();
            _timingReady = false;
            _lastTickStartTime = 0f;
            _lastInputSignature = 0;
            _hasInputSignature = false;
            _lastSolveTimestamp = 0f;
            _fastPathTicks = 0;
            _correctiveTicks = 0;
            _budgetOverruns = 0;
            _firmwareInputCache.Clear();
            if (_realtimeState == null)
            {
                _realtimeState = new RealtimeSchedulerState();
            }
            _realtimeState.Reset(_realtimeConfig);
            NativePhysicsWorld.Instance?.SetExternalStepping(_realtimeConfig != null && _realtimeConfig.Enabled);

            Debug.Log("[SimHost] Starting Simulation Loop...");
            FirmwareMonitorLauncher.TryAutoLaunch();

            _circuit = SessionManager.Instance != null ? SessionManager.Instance.CurrentCircuit : null;
            BuildPinNetMap();
            bool unoOnlyMode = RpiRuntimeConfig.IsUnoOnlyMode();
            bool wantsRpi = TryStartRpiRuntime();
            string firmwarePath = ResolveFirmwareHostPath();

            _useExternalFirmware = TrySetupExternalFirmwares(firmwarePath);
            if (_useExternalFirmware
                && FirmwareMonitorLauncher.ForceVirtualMcuInPipeMode
                && !FirmwareMonitorLauncher.IsUnityTargetPreferred)
            {
                Debug.LogWarning("[SimHost] External firmware disabled (Firmware Monitor pipe mode preference).");
                _useExternalFirmware = false;
            }

            _useFirmwareHost = wantsRpi || _useExternalFirmware;
            if (_useFirmwareHost)
            {
                if (string.IsNullOrWhiteSpace(firmwarePath) || !File.Exists(firmwarePath))
                {
                    Debug.LogError("[SimHost] Firmware host missing; external firmware disabled.");
                    _useFirmwareHost = false;
                    _useExternalFirmware = false;
                    if (wantsRpi)
                    {
                        _rpiRuntime?.SetUnavailable("firmware host missing");
                    }
                }
                else
                {
                    EnsureFirmwareClient();
                    _externalFirmwareExePath = firmwarePath;
                    if (_externalFirmwareClient != null)
                    {
                        _externalFirmwareClient.ExtraLaunchArguments = BuildFirmwareLaunchArguments(wantsRpi);
                        _externalFirmwareClient.LaunchFirmware(_externalFirmwareExePath);
                    }
                    if (_useExternalFirmware)
                    {
                        foreach (var session in _externalFirmwareSessions.Values)
                        {
                            TryLoadExternalFirmware(session);
                        }
                    }
                }
            }
            BuildVirtualMcus();

            _useNativeEngine = !_useExternalFirmware
                && SessionManager.Instance != null
                && (SessionManager.Instance.UseNativeEnginePins || unoOnlyMode)
                && !SessionManager.Instance.UseVirtualMcu;

            // 3. Initialize Native Engine
            if (_useNativeEngine)
            {
                try
                {
                    int engines = NativeBridge.GetVersion();
                    Debug.Log($"[SimHost] NativeEngine v{engines} Linked.");
                }
                catch (DllNotFoundException ex)
                {
                    _useNativeEngine = false;
                    Debug.LogWarning($"[SimHost] NativeEngine DLL not found. Falling back to VirtualMcu. ({ex.Message})");
                }
                catch (EntryPointNotFoundException ex)
                {
                    _useNativeEngine = false;
                    Debug.LogWarning($"[SimHost] NativeEngine entry point missing. Falling back to VirtualMcu. ({ex.Message})");
                }
                catch (Exception ex)
                {
                    _useNativeEngine = false;
                    Debug.LogWarning($"[SimHost] NativeEngine init error. Falling back to VirtualMcu. ({ex.Message})");
                }
            }

            _coreSim = new CoreSimRuntime();
            if (_useNativeEngine)
            {
                try
                {
                    InitializeNativePins();
                }
                catch (DllNotFoundException ex)
                {
                    _useNativeEngine = false;
                    _nativePinsReady = false;
                    Debug.LogWarning($"[SimHost] NativeEngine DLL not found during pin init. Falling back to VirtualMcu. ({ex.Message})");
                }
                catch (EntryPointNotFoundException ex)
                {
                    _useNativeEngine = false;
                    _nativePinsReady = false;
                    Debug.LogWarning($"[SimHost] NativeEngine entry point missing during pin init. Falling back to VirtualMcu. ({ex.Message})");
                }
                catch (Exception ex)
                {
                    _useNativeEngine = false;
                    _nativePinsReady = false;
                    Debug.LogWarning($"[SimHost] NativeEngine pin init error. Falling back to VirtualMcu. ({ex.Message})");
                }
            }
        }

        public void StopSimulation()
        {
            _isRunning = false;
            foreach (var session in _externalFirmwareSessions.Values)
            {
                // No per-board process; shared firmware client handles all boards.
            }
            _externalFirmwareSessions.Clear();
            _externalFirmwarePinsByBoard.Clear();
            _externalFirmwarePinWarnings.Clear();
            _externalFirmwareClient?.StopFirmware();
            _externalFirmwareClient = null;
            _externalFirmwareExePath = null;
            _useExternalFirmware = false;
            _useFirmwareHost = false;
            _useNativeEngine = false;
            _nativePinsReady = false;
            _nativeAvrIndex.Clear();
            _virtualMcus.Clear();
            _pinVoltages.Clear();
            _pinStatesByComponent.Clear();
            _pullupResistances.Clear();
            _virtualSerialNextTime.Clear();
            _boardPowerById.Clear();
            _useRpiRuntime = false;
            _rpiRuntime?.Stop();
            _rpiRuntime = null;
            _rpiConfig = null;
            _firmwarePerfByBoard.Clear();
            _firmwareDebugByBoard.Clear();
            _firmwareDebugBitsByBoard.Clear();
            _firmwarePinOutputsByBoard.Clear();
            _serialBuffer = string.Empty;
            _timingStats = new TimingStats();
            _timingReady = false;
            _lastTickStartTime = 0f;
            _lastInputSignature = 0;
            _hasInputSignature = false;
            _lastSolveTimestamp = 0f;
            _fastPathTicks = 0;
            _correctiveTicks = 0;
            _budgetOverruns = 0;
            _firmwareInputCache.Clear();
            _realtimeState?.Reset(_realtimeConfig);
            NativePhysicsWorld.Instance?.SetExternalStepping(false);
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
                // NativeEngine circuit stepping occurs in RunTick to avoid one-tick lag.
            }
        }

        private void Update()
        {
            if (!_isRunning) return;
            _rpiRuntime?.Update(Time.unscaledDeltaTime, SimTime);

            if (_realtimeConfig != null && _realtimeConfig.Enabled)
            {
                RunRealtimeScheduler(Time.unscaledDeltaTime);
                return;
            }
            NativePhysicsWorld.Instance?.SetExternalStepping(false);

            // Step Logic (Low Frequency / Tick)
            _tickTimer += Time.deltaTime;
            if (_tickTimer >= TICK_RATE)
            {
                _tickTimer -= TICK_RATE;
                TickCount++;
                RunTick(TICK_RATE, true, TICK_RATE, false, 0f);
            }
        }

        public bool StepOnce(float dtSeconds)
        {
            if (dtSeconds <= 0f) return false;
            if (!_isRunning)
            {
                BeginSimulation();
            }
            TickCount++;
            RunTick(dtSeconds, true, dtSeconds, false, 0f);
            return true;
        }

        private void RunTick(float firmwareDtSeconds, bool solveCircuit, float circuitDtSeconds, bool allowFastPath, float circuitBudgetMs)
        {
            float tickStart = Time.realtimeSinceStartup;
            if (_timingReady)
            {
                float intervalSeconds = tickStart - _lastTickStartTime;
                UpdateJitterStats(intervalSeconds, firmwareDtSeconds);
            }
            _lastTickStartTime = tickStart;
            _timingReady = true;

            bool usedNativePins = CollectFirmwareOutputs(firmwareDtSeconds);
            if (_realtimeState != null)
            {
                _realtimeState.LastUsedNativePins = usedNativePins;
            }
            float solveMs = 0f;
            if (solveCircuit)
            {
                solveMs = SolveCircuit(circuitDtSeconds, usedNativePins, allowFastPath, circuitBudgetMs);
            }

            if (_coreSim != null && _circuit != null)
            {
                _tickTrace[_tickTraceIndex] = new TickSample(TickCount, firmwareDtSeconds, solveMs, usedNativePins, _useExternalFirmware);
                _tickTraceIndex = (_tickTraceIndex + 1) % _tickTrace.Length;
                _tickTraceCount = Mathf.Min(_tickTraceCount + 1, _tickTrace.Length);
            }

            float tickMs = (Time.realtimeSinceStartup - tickStart) * 1000f;
            UpdateTickStats(tickMs, firmwareDtSeconds);

            OnTickComplete?.Invoke(SimTime);
        }

        private bool CollectFirmwareOutputs(float dtSeconds)
        {
            if (_coreSim == null || _circuit == null)
            {
                return false;
            }

            _pinVoltages.Clear();
            _pinStatesByComponent.Clear();
            _pullupResistances.Clear();
            RefreshBoardPowerStates();
            RefreshTcs34725Overrides();

            bool usedNativePins = TryApplyNativePins(dtSeconds);
            HashSet<string> handledBoards = null;
            if (!usedNativePins && _useExternalFirmware)
            {
                foreach (var session in _externalFirmwareSessions.Values)
                {
                    if (!IsBoardPowered(session.BoardId))
                    {
                        continue;
                    }
                    if (TryStepExternalFirmware(session, dtSeconds))
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
                if (!_loggedUnoVirtualFidelityWarning && RpiRuntimeConfig.IsUnoOnlyMode())
                {
                    _loggedUnoVirtualFidelityWarning = true;
                    Debug.LogWarning("[SimHost] Uno-only mode is running on VirtualMcu fallback. PWM/timers/interrupt fidelity is limited; enable NativeEngine pins + provide .hex/.bvm for realistic behavior.");
                }
                foreach (var mcu in _virtualMcus.Values)
                {
                    if (!IsBoardPowered(mcu.Id))
                    {
                        continue;
                    }
                    if (handledBoards != null && handledBoards.Contains(mcu.Id))
                    {
                        continue;
                    }
                    mcu.Step(dtSeconds);
                    mcu.CopyVoltages(_pinVoltages);
                    _pinStatesByComponent[mcu.Id] = mcu.Hal.GetPinStates();
                    _pullupResistances[mcu.Id] = mcu.Hal.GetPullupResistance();
                    TryAppendVirtualSerial(mcu.Id);
                    UpdateVirtualFirmwareTelemetry(mcu);
                    _firmwarePinOutputsByBoard[mcu.Id] = BuildPinOutputsFromVoltages(mcu.Id);
                }
            }

            return usedNativePins;
        }

        private float SolveCircuit(float dtSeconds, bool usedNativePins, bool allowFastPath, float circuitBudgetMs)
        {
            if (_coreSim == null || _circuit == null)
            {
                return 0f;
            }

            var envWorld = NativePhysicsWorld.Instance;
            if (envWorld != null)
            {
                _coreSim.AmbientTempC = envWorld.AmbientTempC;
            }

            int signature = ComputeInputSignature();
            bool inputsUnchanged = _hasInputSignature && signature == _lastInputSignature;
            bool canSkipSolve = allowFastPath && inputsUnchanged && LastTelemetry != null;
            if (canSkipSolve && _realtimeConfig != null)
            {
                float sinceSolve = Time.realtimeSinceStartup - _lastSolveTimestamp;
                canSkipSolve = sinceSolve <= _realtimeConfig.MaxSolveSkipSeconds;
            }

            if (canSkipSolve)
            {
                _fastPathTicks++;
                if (LastTelemetry != null)
                {
                    LastTelemetry.TimeSeconds = SimTime;
                    LastTelemetry.TickIndex = TickCount;
                    AppendFirmwarePerfSignals(LastTelemetry);
                    AppendFirmwareDebugSignals(LastTelemetry);
                }
                return 0f;
            }

            float solveStart = Time.realtimeSinceStartup;
            LastTelemetry = _coreSim.Step(_circuit, _pinVoltages, _pinStatesByComponent, _pullupResistances, dtSeconds, _boardPowerById, _usbConnectedByBoard);
            float solveMs = (Time.realtimeSinceStartup - solveStart) * 1000f;

            if (LastTelemetry != null)
            {
                LastTelemetry.TimeSeconds = SimTime;
                LastTelemetry.TickIndex = TickCount;
                AppendFirmwarePerfSignals(LastTelemetry);
                AppendFirmwareDebugSignals(LastTelemetry);
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

            _lastInputSignature = signature;
            _hasInputSignature = true;
            _lastSolveTimestamp = Time.realtimeSinceStartup;
            _correctiveTicks++;
            if (circuitBudgetMs > 0f && solveMs > circuitBudgetMs)
            {
                _budgetOverruns++;
            }

            return solveMs;
        }

        private void RunRealtimeScheduler(float deltaSeconds)
        {
            if (_realtimeConfig == null || !_realtimeConfig.Enabled)
            {
                return;
            }

            if (_realtimeState == null)
            {
                _realtimeState = new RealtimeSchedulerState();
                _realtimeState.Reset(_realtimeConfig);
            }

            NativePhysicsWorld.Instance?.SetExternalStepping(true);

            double firmwareDt = Math.Max(0.0001, _realtimeConfig.FirmwareDtSeconds);
            double circuitDt = Math.Max(0.0001, _realtimeConfig.CircuitDtSeconds);
            double physicsDt = Math.Max(0.0001, _realtimeConfig.PhysicsDtSeconds);
            double epsilon = Math.Max(0.000001, _realtimeConfig.EventEpsilonSeconds);

            if (deltaSeconds < 0f)
            {
                deltaSeconds = 0f;
            }

            _realtimeState.AccumulatorSeconds += deltaSeconds;
            if (_realtimeConfig.MaxAccumulatorSeconds > 0f &&
                _realtimeState.AccumulatorSeconds > _realtimeConfig.MaxAccumulatorSeconds)
            {
                _realtimeState.AccumulatorSeconds = _realtimeConfig.MaxAccumulatorSeconds;
            }

            float frameStart = Time.realtimeSinceStartup;
            int eventSteps = 0;

            while (_realtimeState.AccumulatorSeconds > 0 && eventSteps < _realtimeConfig.MaxStepsPerFrame)
            {
                double nextFirmware = _realtimeState.NextFirmwareAt;
                double nextCircuit = _realtimeState.NextCircuitAt;
                double nextPhysics = _realtimeState.NextPhysicsAt;
                double nextEvent = Math.Min(nextFirmware, Math.Min(nextCircuit, nextPhysics));

                double timeUntilEvent = nextEvent - _realtimeState.MasterTimeSeconds;
                if (timeUntilEvent > _realtimeState.AccumulatorSeconds)
                {
                    _realtimeState.MasterTimeSeconds += _realtimeState.AccumulatorSeconds;
                    _realtimeState.AccumulatorSeconds = 0;
                    break;
                }

                if (timeUntilEvent > 0)
                {
                    _realtimeState.MasterTimeSeconds += timeUntilEvent;
                    _realtimeState.AccumulatorSeconds -= timeUntilEvent;
                }

                float elapsedMs = (Time.realtimeSinceStartup - frameStart) * 1000f;
                if (elapsedMs >= _realtimeConfig.FrameBudgetMs)
                {
                    _budgetOverruns++;
                    break;
                }

                bool firmwareDue = Math.Abs(_realtimeState.MasterTimeSeconds - nextFirmware) <= epsilon;
                bool circuitDue = Math.Abs(_realtimeState.MasterTimeSeconds - nextCircuit) <= epsilon;
                bool physicsDue = Math.Abs(_realtimeState.MasterTimeSeconds - nextPhysics) <= epsilon;

                if (firmwareDue)
                {
                    bool solveCircuit = _realtimeState.MasterTimeSeconds + epsilon >= nextCircuit;
                    bool allowFastPath = _realtimeConfig.AllowFastPath ||
                                         (_realtimeConfig.FrameBudgetMs - elapsedMs) < _realtimeConfig.CircuitBudgetMs;

                    float tickStart = Time.realtimeSinceStartup;
                    TickCount++;
                    RunTick((float)firmwareDt, solveCircuit, (float)circuitDt, allowFastPath, _realtimeConfig.CircuitBudgetMs);
                    float tickMs = (Time.realtimeSinceStartup - tickStart) * 1000f;
                    float budgetMs = _realtimeConfig.FirmwareBudgetMs + (solveCircuit ? _realtimeConfig.CircuitBudgetMs : 0f);
                    if (budgetMs > 0f && tickMs > budgetMs)
                    {
                        _budgetOverruns++;
                    }

                    _realtimeState.NextFirmwareAt += firmwareDt;
                    if (solveCircuit)
                    {
                        _realtimeState.NextCircuitAt += circuitDt;
                    }
                    eventSteps++;
                }

                if (circuitDue && !firmwareDue)
                {
                    float elapsedAfter = (Time.realtimeSinceStartup - frameStart) * 1000f;
                    if (elapsedAfter >= _realtimeConfig.FrameBudgetMs)
                    {
                        _budgetOverruns++;
                        break;
                    }

                    bool allowFastPath = _realtimeConfig.AllowFastPath ||
                                         (_realtimeConfig.FrameBudgetMs - elapsedAfter) < _realtimeConfig.CircuitBudgetMs;
                    RunCircuitOnly((float)circuitDt, allowFastPath);
                    _realtimeState.NextCircuitAt += circuitDt;
                    eventSteps++;
                }

                if (physicsDue)
                {
                    float elapsedAfter = (Time.realtimeSinceStartup - frameStart) * 1000f;
                    if (elapsedAfter >= _realtimeConfig.FrameBudgetMs)
                    {
                        _budgetOverruns++;
                        break;
                    }
                    StepPhysicsRealtime((float)physicsDt);
                    _realtimeState.NextPhysicsAt += physicsDt;
                    eventSteps++;
                }

                if (!firmwareDue && !circuitDue && !physicsDue)
                {
                    double nudge = Math.Min(_realtimeState.AccumulatorSeconds, epsilon);
                    _realtimeState.MasterTimeSeconds += nudge;
                    _realtimeState.AccumulatorSeconds -= nudge;
                }
            }
        }

        private void RunCircuitOnly(float dtSeconds, bool allowFastPath)
        {
            if (_coreSim == null || _circuit == null) return;
            bool usedNativePins = _realtimeState != null && _realtimeState.LastUsedNativePins;
            SolveCircuit(dtSeconds, usedNativePins, allowFastPath, _realtimeConfig?.CircuitBudgetMs ?? 0f);
        }

        private void StepPhysicsRealtime(float dtSeconds)
        {
            var world = NativePhysicsWorld.Instance;
            if (world == null || !world.IsRunning) return;
            dtSeconds = ClampPhysicsDt(dtSeconds);
            float start = Time.realtimeSinceStartup;
            world.StepExternal(dtSeconds);
            float stepMs = (Time.realtimeSinceStartup - start) * 1000f;
            if (_realtimeConfig != null && _realtimeConfig.PhysicsBudgetMs > 0f && stepMs > _realtimeConfig.PhysicsBudgetMs)
            {
                _budgetOverruns++;
            }
        }

        private float ClampPhysicsDt(float dtSeconds)
        {
            if (_realtimeConfig == null || !_realtimeConfig.ClampPhysicsDt) return dtSeconds;
            float min = Mathf.Max(0.0001f, _realtimeConfig.MinPhysicsDtSeconds);
            float max = Mathf.Max(min, _realtimeConfig.MaxPhysicsDtSeconds);
            return Mathf.Clamp(dtSeconds, min, max);
        }

        private int ComputeInputSignature()
        {
            unchecked
            {
                int hash = 17;
                foreach (var kvp in _pinVoltages)
                {
                    hash = (hash * 31) ^ kvp.Key.GetHashCode();
                    hash = (hash * 31) ^ kvp.Value.GetHashCode();
                }
                foreach (var kvp in _pinStatesByComponent)
                {
                    hash = (hash * 31) ^ kvp.Key.GetHashCode();
                    var states = kvp.Value;
                    if (states == null) continue;
                    for (int i = 0; i < states.Count; i++)
                    {
                        var state = states[i];
                        hash = (hash * 31) ^ (state.Pin?.GetHashCode() ?? 0);
                        hash = (hash * 31) ^ (state.IsOutput ? 1 : 0);
                        hash = (hash * 31) ^ (state.PullupEnabled ? 1 : 0);
                    }
                }
                foreach (var kvp in _boardPowerById)
                {
                    hash = (hash * 31) ^ kvp.Key.GetHashCode();
                    hash = (hash * 31) ^ (kvp.Value ? 1 : 0);
                }
                foreach (var kvp in _usbConnectedByBoard)
                {
                    hash = (hash * 31) ^ kvp.Key.GetHashCode();
                    hash = (hash * 31) ^ (kvp.Value ? 1 : 0);
                }
                return hash;
            }
        }

        private void UpdateJitterStats(float intervalSeconds, float expectedSeconds)
        {
            float jitterMs = (intervalSeconds - expectedSeconds) * 1000f;
            float jitterAbs = Mathf.Abs(jitterMs);

            _timingStats.LastJitterMs = jitterMs;
            _timingStats.JitterSamples++;
            _timingStats.AvgJitterMs += (jitterAbs - _timingStats.AvgJitterMs) / _timingStats.JitterSamples;
            _timingStats.MaxJitterMs = Mathf.Max(_timingStats.MaxJitterMs, jitterAbs);
        }

        private void UpdateTickStats(float tickMs, float expectedSeconds)
        {
            _timingStats.LastTickMs = tickMs;
            _timingStats.TickSamples++;
            _timingStats.AvgTickMs += (tickMs - _timingStats.AvgTickMs) / _timingStats.TickSamples;
            _timingStats.MaxTickMs = Mathf.Max(_timingStats.MaxTickMs, tickMs);

            if (tickMs > expectedSeconds * 1000f)
            {
                _timingStats.Overruns++;
            }
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
                if (!IsMcuBoard(comp)) continue;
                var profile = GetMcuProfile(comp);
                if (!profile.CoreSupported)
                {
                    Debug.LogWarning($"[SimHost] Native pins unsupported for {comp.Id} ({profile.Id}).");
                    continue;
                }
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
                Debug.LogWarning("[SimHost] No MCU board components for native pins.");
                return;
            }

            if (TryLoadNativeFirmware())
            {
                _nativePinsReady = true;
                Debug.Log("[SimHost] NativeEngine firmware loaded.");
            }
            else
            {
                Debug.LogWarning("[SimHost] NativeEngine firmware missing; falling back to VirtualMcu.");
            }
        }

        private bool TryLoadNativeFirmware()
        {
            if (_circuit == null || _circuit.Components == null) return false;
            bool loadedAny = false;
            foreach (var comp in _circuit.Components)
            {
                if (!IsMcuBoard(comp)) continue;
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

        private bool TryApplyNativePins(float dtSeconds)
        {
            if (_useExternalFirmware || !_useNativeEngine)
            {
                return false;
            }
            if (SessionManager.Instance == null || !SessionManager.Instance.UseNativeEnginePins || !_nativePinsReady)
            {
                return false;
            }

            NativeBridge.StepSimulation(dtSeconds);

            if (!BridgeInterface.TryReadState(out var state))
            {
                if (!_loggedNativeFallback)
                {
                    _loggedNativeFallback = true;
                    Debug.LogWarning("[SimHost] NativeEngine bridge not available. Falling back to VirtualMcu.");
                }
                return false;
            }

            if (_circuit == null || _circuit.Components == null) return false;
            foreach (var comp in _circuit.Components)
            {
                if (!IsMcuBoard(comp)) continue;
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
            if (_virtualMcus.TryGetValue(componentId, out var board))
            {
                board.SetInputVoltage(pinName, voltage);
                return;
            }
            _pinVoltages[$"{componentId}.{pinName}"] = voltage;
        }

        private void BuildVirtualMcus()
        {
            _virtualMcus.Clear();
            _pinVoltages.Clear();
            if (_circuit == null || _circuit.Components == null) return;

            foreach (var comp in _circuit.Components)
            {
                if (!IsMcuBoard(comp)) continue;
                var profile = GetMcuProfile(comp);
                if (!profile.CoreSupported)
                {
                    Debug.LogWarning($"[SimHost] Virtual MCU core unsupported for {comp.Id} ({profile.Id}).");
                    continue;
                }
                var board = new VirtualMcu(comp.Id, profile);
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
                Debug.Log($"[SimHost] VirtualMcu Active: {board.Id} ({(string.IsNullOrWhiteSpace(board.FirmwareSource) ? "none" : board.FirmwareSource)})");
                _virtualMcus[comp.Id] = board;
                if (!_usbConnectedByBoard.ContainsKey(comp.Id))
                {
                    _usbConnectedByBoard[comp.Id] = true;
                }
            }
        }

        private bool IsMcuBoard(ComponentSpec comp)
        {
            if (comp == null) return false;
            if (comp.Properties != null &&
                comp.Properties.TryGetValue("boardProfile", out var profile) &&
                BoardProfiles.IsKnownProfileId(profile))
            {
                return true;
            }
            return BoardProfiles.IsKnownProfileId(comp.Type);
        }

        private static string ResolveMcuProfileId(ComponentSpec board)
        {
            if (board.Properties != null &&
                board.Properties.TryGetValue("boardProfile", out var explicitProfile) &&
                !string.IsNullOrWhiteSpace(explicitProfile))
            {
                return BoardProfiles.Get(explicitProfile).Id;
            }
            if (board == null || string.IsNullOrWhiteSpace(board.Type))
            {
                return BoardProfiles.GetDefault().Id;
            }
            return BoardProfiles.Get(board.Type).Id;
        }

        private static BoardProfileInfo GetMcuProfile(ComponentSpec board)
        {
            return BoardProfiles.Get(ResolveMcuProfileId(board));
        }

        private string[] GetExternalFirmwarePins(string boardId)
        {
            if (string.IsNullOrWhiteSpace(boardId)) boardId = "board";
            if (_externalFirmwarePinsByBoard.TryGetValue(boardId, out var cached)) return cached;

            string profileId = BoardProfiles.GetDefault().Id;
            if (_externalFirmwareSessions.TryGetValue(boardId, out var session) &&
                !string.IsNullOrWhiteSpace(session.BoardProfile))
            {
                profileId = session.BoardProfile;
            }

            var profile = BoardProfiles.Get(profileId);
            var corePins = BoardProfiles.GetCorePins(profile);
            int count = corePins.Count;
            if (profile.CoreLimited && count > ExternalFirmwarePinLimit)
            {
                if (_externalFirmwarePinWarnings.Add(boardId))
                {
                    Debug.LogWarning($"[SimHost] {boardId} profile {profile.Id} exposes {count} pins; firmware core limited to {ExternalFirmwarePinLimit}.");
                }
                count = ExternalFirmwarePinLimit;
            }

            if (count <= 0)
            {
                count = ExternalFirmwarePinLimit;
            }

            var pins = new string[count];
            for (int i = 0; i < count; i++)
            {
                pins[i] = i < corePins.Count ? corePins[i] : $"D{i}";
            }

            _externalFirmwarePinsByBoard[boardId] = pins;
            return pins;
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

        private bool TryStartRpiRuntime()
        {
            if (_useRpiRuntime) return true;
            if (RpiRuntimeConfig.IsUnoOnlyMode())
            {
                return false;
            }
            if (!HasRaspberryPiBoard())
            {
                return false;
            }

            var config = RpiRuntimeConfig.FromEnvironment();
            _rpiRuntime = new RpiRuntimeManager();
            _rpiRuntime.Start(config);
            _useRpiRuntime = true;
            _rpiConfig = config;
            Debug.Log("[SimHost] Raspberry Pi runtime started.");
            return true;
        }

        private bool HasRaspberryPiBoard()
        {
            if (_circuit?.Components == null) return false;
            foreach (var comp in _circuit.Components)
            {
                if (comp == null) continue;
                if (comp.Properties != null &&
                    comp.Properties.TryGetValue("boardProfile", out var profile) &&
                    string.Equals(BoardProfiles.Get(profile).Id, "RaspberryPi", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (!string.IsNullOrWhiteSpace(comp.Type) &&
                    string.Equals(BoardProfiles.Get(comp.Type).Id, "RaspberryPi", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private void RefreshBoardPowerStates()
        {
            _boardPowerById.Clear();
            if (_circuit?.Components == null) return;
            foreach (var comp in _circuit.Components)
            {
                if (!IsMcuBoard(comp)) continue;
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
            string[] pins = { "VIN", "5V", "3V3", "IOREF", "VCC", "RAW" };
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

        private bool TrySetupExternalFirmwares(string firmwarePath)
        {
            if (SessionManager.Instance == null || _circuit == null || _circuit.Components == null) return false;
            if (string.IsNullOrWhiteSpace(firmwarePath) || !File.Exists(firmwarePath)) return false;

            if (SessionManager.Instance.UseVirtualMcu)
            {
                Debug.Log("[SimHost] VirtualMcu enabled; external firmware disabled.");
                return false;
            }

            var boards = _circuit.Components.Where(IsMcuBoard).ToList();
            if (boards.Count == 0) return false;
            var supportedBoards = new List<ComponentSpec>();

            foreach (var board in boards)
            {
                var profile = GetMcuProfile(board);
                if (!profile.CoreSupported)
                {
                    Debug.LogWarning($"[SimHost] External firmware unsupported for {board.Id} ({profile.Id}).");
                    continue;
                }
                supportedBoards.Add(board);
            }

            if (supportedBoards.Count == 0)
            {
                Debug.LogWarning("[SimHost] External firmware disabled; no supported MCU profiles.");
                return false;
            }

            foreach (var board in supportedBoards)
            {
                var profile = GetMcuProfile(board);
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
                    BoardProfile = ResolveMcuProfileId(board),
                    StartTime = Time.unscaledTime
                };
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

        private string ResolveFirmwareHostPath()
        {
            var session = SessionManager.Instance;
            if (session == null) return null;
            session.FindFirmware();
            return session.FirmwarePath;
        }

        private void EnsureFirmwareClient()
        {
            if (_externalFirmwareClient == null)
            {
                _externalFirmwareClient = CreateSharedFirmwareClient();
            }
        }

        private string BuildFirmwareLaunchArguments(bool enableRpi)
        {
            var args = new List<string>();
            if (enableRpi && _rpiConfig != null)
            {
                args.Add("--rpi-enable");
                args.Add("--rpi-shm-dir");
                args.Add(_rpiConfig.SharedMemoryDir);
                args.Add("--rpi-display");
                args.Add($"{_rpiConfig.DisplayWidth}x{_rpiConfig.DisplayHeight}");
                args.Add("--rpi-camera");
                args.Add($"{_rpiConfig.CameraWidth}x{_rpiConfig.CameraHeight}");
            }

            if (enableRpi && _rpiConfig != null && !string.IsNullOrWhiteSpace(_rpiConfig.QemuPath))
            {
                args.Add("--rpi-qemu");
                args.Add(_rpiConfig.QemuPath);
            }
            if (enableRpi && _rpiConfig != null && !string.IsNullOrWhiteSpace(_rpiConfig.ImagePath))
            {
                args.Add("--rpi-image");
                args.Add(_rpiConfig.ImagePath);
            }
            if (enableRpi && _rpiConfig != null && !string.IsNullOrWhiteSpace(_rpiConfig.NetworkMode))
            {
                args.Add("--rpi-net-mode");
                args.Add(_rpiConfig.NetworkMode);
            }
            if (enableRpi && _rpiConfig != null && _rpiConfig.CpuAffinityMask != 0)
            {
                args.Add("--rpi-cpu-affinity");
                args.Add($"0x{_rpiConfig.CpuAffinityMask:X}");
            }
            if (enableRpi && _rpiConfig != null && _rpiConfig.CpuMaxPercent > 0)
            {
                args.Add("--rpi-cpu-max-percent");
                args.Add(_rpiConfig.CpuMaxPercent.ToString(CultureInfo.InvariantCulture));
            }
            if (enableRpi && _rpiConfig != null && _rpiConfig.ThreadCount > 0)
            {
                args.Add("--rpi-threads");
                args.Add(_rpiConfig.ThreadCount.ToString(CultureInfo.InvariantCulture));
            }
            if (enableRpi && _rpiConfig != null && _rpiConfig.PriorityClass > 0)
            {
                args.Add("--rpi-priority");
                args.Add(_rpiConfig.PriorityClass.ToString(CultureInfo.InvariantCulture));
            }

            string repoRoot = RpiRuntimeConfig.ResolveRepoRoot();
            if (enableRpi && _rpiConfig != null)
            {
                string rpiLog = Path.Combine(repoRoot, "logs", "rpi", "rpi_qemu.log");
                args.Add("--rpi-log");
                args.Add(rpiLog);
            }

            string firmwareLogDir = Path.Combine(repoRoot, "logs", "firmware");
            Directory.CreateDirectory(firmwareLogDir);
            string firmwareLogPath = Path.Combine(firmwareLogDir, "firmware_host.log");
            args.Add("--log");
            args.Add(firmwareLogPath);

            return string.Join(" ", args);
        }

        private FirmwareClient CreateSharedFirmwareClient()
        {
            var go = new GameObject("FirmwareClient_Shared");
            go.transform.SetParent(transform);
            var client = go.AddComponent<FirmwareClient>();
            client.Configure(BuildPipeName());
            var session = SessionManager.Instance;
            if (session != null)
            {
                client.LaunchLockstep = session.FirmwareHostLockstep;
            }
            return client;
        }

        private static string BuildPipeName()
        {
            return "RoboTwin.FirmwareEngine";
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
            if (!_useExternalFirmware || session == null || _externalFirmwareClient == null) return;
            if (session.Disabled) return;
            if (Time.unscaledTime < session.NextRetryTime) return;
            if (session.Loaded) return;
            if (string.IsNullOrWhiteSpace(session.BvmPath) || !File.Exists(session.BvmPath))
            {
                Debug.LogWarning($"[SimHost] External firmware missing .bvm file for {session.BoardId}.");
                return;
            }

            if (TryLoadPendingFirmware(session))
            {
                session.LoggedPending = false;
                Debug.Log($"[SimHost] External firmware loaded: {session.BvmPath}");
            }
            else
            {
                HandlePendingExternalFirmware(session, _externalFirmwareClient.LastErrorKind);
            }
        }

        private bool TryLoadPendingFirmware(ExternalFirmwareSession session)
        {
            if (session == null || _externalFirmwareClient == null) return false;
            if (session.Disabled) return false;
            if (Time.unscaledTime < session.NextRetryTime) return false;
            if (session.Loaded) return true;
            if (string.IsNullOrWhiteSpace(session.BvmPath) || !File.Exists(session.BvmPath)) return false;
            if (!_externalFirmwareClient.LoadBvmFile(session.BoardId, session.BoardProfile, session.BvmPath))
            {
                if (_externalFirmwareClient.LastErrorKind == FirmwareErrorKind.PipeUnavailable)
                {
                    session.NextRetryTime = Time.unscaledTime + ExternalFirmwareRetryDelay;
                }
                return false;
            }
            session.Loaded = true;
            return true;
        }

        private bool TryStepExternalFirmware(ExternalFirmwareSession session, float dtSeconds)
        {
            if (session == null || _externalFirmwareClient == null) return false;
            if (session.Disabled) return false;
            if (Time.unscaledTime < session.NextRetryTime) return false;

            TryLoadPendingFirmware(session);

            var request = new RobotTwin.CoreSim.FirmwareStepRequest
            {
                RailVoltage = 5.0f,
                DeltaMicros = (uint)Mathf.RoundToInt(dtSeconds * 1000000f),
                PinStates = BuildFirmwareInputStates(session.BoardId),
                AnalogVoltages = BuildFirmwareAnalogInputs(session.BoardId)
            };

            if (!_externalFirmwareClient.TryStep(session.BoardId, request, out var result))
            {
                var errorKind = _externalFirmwareClient.LastErrorKind;
                if (errorKind == FirmwareErrorKind.PipeUnavailable)
                {
                    session.NextRetryTime = Time.unscaledTime + ExternalFirmwareRetryDelay;
                    HandlePendingExternalFirmware(session, errorKind);
                    return false;
                }
                session.FailureCount++;
                session.NextRetryTime = Time.unscaledTime + ExternalFirmwareRetryDelay;
                if (errorKind == FirmwareErrorKind.AccessDenied)
                {
                    DisableExternalFirmwareSession(session, "access denied to firmware pipe (check admin/integrity level)");
                }
                else if (errorKind == FirmwareErrorKind.BrokenPipe)
                {
                    DisableExternalFirmwareSession(session, "firmware host disconnected");
                }
                else if (session.FailureCount >= ExternalFirmwareFailureLimit)
                {
                    DisableExternalFirmwareSession(session, "repeated firmware failures");
                }
                if (!session.LoggedFallback)
                {
                    session.LoggedFallback = true;
                    Debug.Log($"[SimHost] External firmware not responding for {session.BoardId}. Falling back to VirtualMcu.");
                }
                return false;
            }

            session.FailureCount = 0;
            session.NextRetryTime = 0f;
            session.LoggedFallback = false;
            ApplyFirmwareResult(session.BoardId, result);
            return true;
        }

        private void DisableExternalFirmwareSession(ExternalFirmwareSession session, string reason)
        {
            if (session == null || session.Disabled) return;
            session.Disabled = true;
            session.DisabledReason = string.IsNullOrWhiteSpace(reason) ? "disabled" : reason;
            Debug.Log($"[SimHost] External firmware disabled for {session.BoardId}: {session.DisabledReason}.");

            bool anyActive = _externalFirmwareSessions.Values.Any(s => s != null && !s.Disabled);
            if (!anyActive)
            {
                _useExternalFirmware = false;
                Debug.Log("[SimHost] All external firmware sessions disabled. Using VirtualMcu only.");
            }
        }

        private void HandlePendingExternalFirmware(ExternalFirmwareSession session, FirmwareErrorKind errorKind)
        {
            if (session == null) return;
            if (errorKind != FirmwareErrorKind.PipeUnavailable) return;
            float elapsed = Time.unscaledTime - session.StartTime;
            if (elapsed >= ExternalFirmwareStartupTimeoutSeconds)
            {
                DisableExternalFirmwareSession(session, "firmware pipe not available");
                return;
            }
            if (elapsed < ExternalFirmwareStartupGraceSeconds)
            {
                return;
            }
            if (!session.LoggedPending)
            {
                session.LoggedPending = true;
                Debug.Log($"[SimHost] External firmware pending (pipe not ready): {session.BvmPath}");
            }
        }

        private int[] BuildFirmwareInputStates(string boardId)
        {
            var cache = GetFirmwareInputCache(boardId);
            var pins = GetExternalFirmwarePins(boardId);
            int count = Mathf.Min(pins.Length, ExternalFirmwarePinLimit);
            for (int i = 0; i < count; i++)
            {
                string pin = pins[i];
                float voltage = GetLastNetVoltage(boardId, pin);
                if (!cache.LastVoltages.TryGetValue(pin, out var last) ||
                    Mathf.Abs(last - voltage) > 0.01f)
                {
                    cache.LastVoltages[pin] = voltage;
                    cache.Digital[i] = voltage >= 2.5f ? 1 : 0;
                    cache.ChangedPins++;
                }
            }
            return cache.Digital;
        }

        private float[] BuildFirmwareAnalogInputs(string boardId)
        {
            var cache = GetFirmwareInputCache(boardId);
            var pins = GetExternalFirmwarePins(boardId);
            int write = 0;
            for (int i = 0; i < pins.Length && write < cache.Analog.Length; i++)
            {
                string pin = pins[i];
                if (!pin.StartsWith("A", StringComparison.OrdinalIgnoreCase)) continue;
                float voltage = GetLastNetVoltage(boardId, pin);
                if (!cache.LastVoltages.TryGetValue(pin, out var last) ||
                    Mathf.Abs(last - voltage) > 0.01f)
                {
                    cache.LastVoltages[pin] = voltage;
                    cache.Analog[write] = voltage;
                    cache.ChangedPins++;
                }
                write++;
            }
            ApplyAnalogOverrides(boardId, cache.Analog);
            return cache.Analog;
        }

        private void RefreshTcs34725Overrides()
        {
            _analogOverridesByBoard.Clear();
            if (_circuit?.Components == null) return;

            string fallbackBoardId = null;
            foreach (var comp in _circuit.Components)
            {
                if (IsMcuBoard(comp))
                {
                    fallbackBoardId = comp.Id;
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(fallbackBoardId)) return;

            foreach (var comp in _circuit.Components)
            {
                if (comp == null || !string.Equals(comp.Type, "TCS34725", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string boardId = ResolveI2cBoardId(comp, fallbackBoardId);
                var overrides = GetAnalogOverrides(boardId);
                ApplyTcsChannelOverride(comp, overrides, 10, "r", "red", "tcsR", "tcsRed");
                ApplyTcsChannelOverride(comp, overrides, 11, "g", "green", "tcsG", "tcsGreen");
                ApplyTcsChannelOverride(comp, overrides, 12, "b", "blue", "tcsB", "tcsBlue");
                ApplyTcsChannelOverride(comp, overrides, 13, "c", "clear", "tcsC", "tcsClear");
            }
        }

        private string ResolveI2cBoardId(ComponentSpec device, string fallbackBoardId)
        {
            string sdaNet = GetPinNet(device?.Id, "SDA");
            string sclNet = GetPinNet(device?.Id, "SCL");
            if (_circuit?.Components == null) return fallbackBoardId;

            foreach (var comp in _circuit.Components)
            {
                if (!IsMcuBoard(comp)) continue;
                if (MatchesNet(comp.Id, "SDA", sdaNet) || MatchesNet(comp.Id, "A4", sdaNet))
                {
                    if (MatchesNet(comp.Id, "SCL", sclNet) || MatchesNet(comp.Id, "A5", sclNet))
                    {
                        return comp.Id;
                    }
                }
            }
            return fallbackBoardId;
        }

        private bool MatchesNet(string componentId, string pin, string netId)
        {
            if (string.IsNullOrWhiteSpace(componentId) || string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(netId)) return false;
            string key = $"{componentId}.{pin}";
            return _pinNetMap.TryGetValue(key, out var boardNet) &&
                   string.Equals(boardNet, netId, StringComparison.OrdinalIgnoreCase);
        }

        private string GetPinNet(string componentId, string pin)
        {
            if (string.IsNullOrWhiteSpace(componentId) || string.IsNullOrWhiteSpace(pin)) return string.Empty;
            string key = $"{componentId}.{pin}";
            return _pinNetMap.TryGetValue(key, out var netId) ? netId : string.Empty;
        }

        private float[] GetAnalogOverrides(string boardId)
        {
            if (string.IsNullOrWhiteSpace(boardId)) boardId = "board";
            if (_analogOverridesByBoard.TryGetValue(boardId, out var overrides)) return overrides;
            overrides = new float[16];
            for (int i = 0; i < overrides.Length; i++)
            {
                overrides[i] = float.NaN;
            }
            _analogOverridesByBoard[boardId] = overrides;
            return overrides;
        }

        private void ApplyAnalogOverrides(string boardId, float[] analogInputs)
        {
            if (analogInputs == null) return;
            if (!_analogOverridesByBoard.TryGetValue(boardId, out var overrides)) return;
            int count = Math.Min(analogInputs.Length, overrides.Length);
            for (int i = 0; i < count; i++)
            {
                if (!float.IsNaN(overrides[i]))
                {
                    analogInputs[i] = overrides[i];
                }
            }
        }

        private void ApplyTcsChannelOverride(ComponentSpec comp, float[] overrides, int channel, params string[] keys)
        {
            if (comp?.Properties == null || overrides == null || channel < 0 || channel >= overrides.Length) return;
            if (!TryGetDoubleAny(comp.Properties, out var rawValue, keys)) return;

            double normalized = NormalizeTcsValue(rawValue);
            if (TryGetDoubleAny(comp.Properties, out var noisePct, "noisePct", "noisePercent", "noise"))
            {
                double pct = Math.Abs(noisePct);
                if (pct > 1.0)
                {
                    pct = pct / 100.0;
                }
                if (pct > 0.0)
                {
                    double sample = DeterministicNoise.SampleSigned($"{comp.Id}:tcs:{channel}", TickCount);
                    normalized = ClampValue(normalized + normalized * pct * sample, 0.0, 1023.0);
                }
            }

            overrides[channel] = (float)(normalized / 1023.0 * 5.0);
        }

        private static double NormalizeTcsValue(double raw)
        {
            if (raw <= 1.0)
            {
                return raw * 1023.0;
            }
            if (raw <= 5.5)
            {
                return ClampValue(raw / 5.0 * 1023.0, 0.0, 1023.0);
            }
            if (raw <= 1023.0)
            {
                return raw;
            }
            if (raw <= 65535.0)
            {
                return ClampValue(raw / 64.0, 0.0, 1023.0);
            }
            return 1023.0;
        }

        private static double ClampValue(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static bool TryGetDoubleAny(Dictionary<string, string> props, out double value, params string[] keys)
        {
            value = 0.0;
            if (props == null || keys == null || keys.Length == 0) return false;
            for (int i = 0; i < keys.Length; i++)
            {
                if (TryGetDouble(props, keys[i], out value))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryGetDouble(Dictionary<string, string> props, string key, out double value)
        {
            value = 0.0;
            if (props == null || string.IsNullOrWhiteSpace(key)) return false;
            if (!props.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return false;
            string cleaned = raw.Trim().ToLowerInvariant();
            cleaned = cleaned.Replace("v", string.Empty).Replace(" ", string.Empty);
            return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private FirmwareInputCache GetFirmwareInputCache(string boardId)
        {
            if (string.IsNullOrWhiteSpace(boardId))
            {
                return new FirmwareInputCache();
            }
            if (!_firmwareInputCache.TryGetValue(boardId, out var cache))
            {
                cache = new FirmwareInputCache();
                _firmwareInputCache[boardId] = cache;
            }
            cache.ChangedPins = 0;
            return cache;
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
            var pins = GetExternalFirmwarePins(boardId);
            var pinStates = new List<PinState>(pins.Length);
            int[] outputs = result?.PinStates ?? System.Array.Empty<int>();

            if (outputs.Length > 0)
            {
                var snapshot = new int[outputs.Length];
                Array.Copy(outputs, snapshot, outputs.Length);
                _firmwarePinOutputsByBoard[boardId] = snapshot;
            }

            for (int i = 0; i < pins.Length; i++)
            {
                string pin = pins[i];
                int state = i < outputs.Length ? outputs[i] : -1;
                bool isOutput = state >= 0;
                if (isOutput)
                {
                    float voltage = 0f;
                    if (state <= 0)
                    {
                        voltage = 0f;
                    }
                    else if (state == 1)
                    {
                        voltage = VirtualMcu.DefaultHighVoltage;
                    }
                    else
                    {
                        voltage = VirtualMcu.DefaultHighVoltage * Mathf.Clamp01(state / 255f);
                    }
                    _pinVoltages[$"{boardId}.{pin}"] = voltage;
                }
                pinStates.Add(new PinState(pin, isOutput, false));
            }

            _pinStatesByComponent[boardId] = pinStates;
            _pullupResistances[boardId] = 20000.0;

            if (result != null && !string.IsNullOrWhiteSpace(result.SerialOutput))
            {
                AppendSerialOutput(boardId, result.SerialOutput);
            }

            if (result?.PerfCounters != null)
            {
                _firmwarePerfByBoard[boardId] = result.PerfCounters.Clone();
            }

            if (result?.DebugCounters != null)
            {
                if (!_firmwareDebugByBoard.TryGetValue(boardId, out var debug))
                {
                    debug = new FirmwareDebugCounters();
                    _firmwareDebugByBoard[boardId] = debug;
                }
                debug.CopyFrom(result.DebugCounters);
            }

            if (result?.DebugBits != null)
            {
                if (!_firmwareDebugBitsByBoard.TryGetValue(boardId, out var bits))
                {
                    bits = new FirmwareDebugBitset();
                    _firmwareDebugBitsByBoard[boardId] = bits;
                }
                bits.CopyFrom(result.DebugBits);
            }
        }

        private void UpdateVirtualFirmwareTelemetry(VirtualMcu mcu)
        {
            if (mcu == null) return;

            if (!_firmwarePerfByBoard.TryGetValue(mcu.Id, out var perf) || perf == null)
            {
                perf = new FirmwarePerfCounters();
                _firmwarePerfByBoard[mcu.Id] = perf;
            }

            perf.Cycles = (ulong)Math.Max(0L, mcu.Clock.TotalCycles);
            perf.AdcSamples = 0;
            perf.SpiTransfers = 0;
            perf.TwiTransfers = 0;
            perf.WdtResets = 0;
            perf.DroppedOutputs = 0;
            if (perf.UartTxBytes != null)
            {
                Array.Clear(perf.UartTxBytes, 0, perf.UartTxBytes.Length);
            }
            if (perf.UartRxBytes != null)
            {
                Array.Clear(perf.UartRxBytes, 0, perf.UartRxBytes.Length);
            }

            if (!_firmwareDebugByBoard.TryGetValue(mcu.Id, out var debug) || debug == null)
            {
                debug = new FirmwareDebugCounters();
                _firmwareDebugByBoard[mcu.Id] = debug;
            }

            debug.FlashBytes = VirtualMemory.FlashSizeBytes;
            debug.SramBytes = VirtualMemory.SramSizeBytes;
            debug.EepromBytes = VirtualMemory.EepromSizeBytes;
            debug.IoBytes = 0x200;
            debug.CpuHz = (uint)Math.Max(0, Math.Round(mcu.Clock.FrequencyHz));
            debug.ProgramCounter = (ushort)Mathf.Clamp(mcu.Cpu.ProgramCounter, 0, ushort.MaxValue);
            debug.StackPointer = 0;
            debug.StatusRegister = 0;
            debug.StackHighWater = 0;
            debug.HeapTopAddress = 0;
            debug.StackMinAddress = 0;
            debug.DataSegmentEnd = 0;
            debug.StackOverflows = 0;
            debug.InvalidMemoryAccesses = 0;
            debug.InterruptCount = 0;
            debug.InterruptLatencyMax = 0;
            debug.TimingViolations = 0;
            debug.CriticalSectionCycles = 0;
            debug.SleepCycles = 0;
            debug.FlashAccessCycles = 0;
            debug.UartOverflows = 0;
            debug.TimerOverflows = 0;
            debug.BrownOutResets = 0;
            debug.GpioStateChanges = 0;
            debug.PwmCycles = 0;
            debug.I2cTransactions = 0;
            debug.SpiTransactions = 0;

            if (!_firmwareDebugBitsByBoard.TryGetValue(mcu.Id, out var bits) || bits == null)
            {
                bits = new FirmwareDebugBitset();
                _firmwareDebugBitsByBoard[mcu.Id] = bits;
            }
            PopulateVirtualDebugBits(bits, debug);
        }

        private static void PopulateVirtualDebugBits(FirmwareDebugBitset bits, FirmwareDebugCounters debug)
        {
            if (bits == null || debug == null) return;
            bits.BitCount = VirtualDebugBitCount;
            bits.Raw = Array.Empty<byte>();
            bits.Fields.Clear();

            AddVirtualDebugField(bits, "pc", 0, 16, debug.ProgramCounter);
            AddVirtualDebugField(bits, "sp", 16, 16, debug.StackPointer);
            AddVirtualDebugField(bits, "sreg", 32, 8, debug.StatusRegister);
            AddVirtualDebugField(bits, "flash_bytes", 40, 32, debug.FlashBytes);
            AddVirtualDebugField(bits, "sram_bytes", 72, 32, debug.SramBytes);
            AddVirtualDebugField(bits, "eeprom_bytes", 104, 32, debug.EepromBytes);
            AddVirtualDebugField(bits, "io_bytes", 136, 32, debug.IoBytes);
            AddVirtualDebugField(bits, "cpu_hz", 168, 32, debug.CpuHz);
            AddVirtualDebugField(bits, "stack_high_water", 200, 16, debug.StackHighWater);
            AddVirtualDebugField(bits, "heap_top", 216, 16, debug.HeapTopAddress);
            AddVirtualDebugField(bits, "stack_min", 232, 16, debug.StackMinAddress);
            AddVirtualDebugField(bits, "data_segment_end", 248, 16, debug.DataSegmentEnd);
            AddVirtualDebugField(bits, "stack_overflows", 264, 32, debug.StackOverflows);
            AddVirtualDebugField(bits, "invalid_mem_accesses", 296, 32, debug.InvalidMemoryAccesses);
            AddVirtualDebugField(bits, "interrupt_count", 328, 32, debug.InterruptCount);
            AddVirtualDebugField(bits, "interrupt_latency_max", 360, 32, debug.InterruptLatencyMax);
            AddVirtualDebugField(bits, "timing_violations", 392, 32, debug.TimingViolations);
            AddVirtualDebugField(bits, "critical_section_cycles", 424, 32, debug.CriticalSectionCycles);
            AddVirtualDebugField(bits, "sleep_cycles", 456, 32, debug.SleepCycles);
            AddVirtualDebugField(bits, "flash_access_cycles", 488, 32, debug.FlashAccessCycles);
            AddVirtualDebugField(bits, "uart_overflows", 520, 32, debug.UartOverflows);
            AddVirtualDebugField(bits, "timer_overflows", 552, 32, debug.TimerOverflows);
            AddVirtualDebugField(bits, "brown_out_resets", 584, 32, debug.BrownOutResets);
            AddVirtualDebugField(bits, "gpio_state_changes", 616, 32, debug.GpioStateChanges);
            AddVirtualDebugField(bits, "pwm_cycles", 648, 32, debug.PwmCycles);
            AddVirtualDebugField(bits, "i2c_transactions", 680, 32, debug.I2cTransactions);
            AddVirtualDebugField(bits, "spi_transactions", 712, 32, debug.SpiTransactions);
        }

        private static void AddVirtualDebugField(FirmwareDebugBitset bits, string name, ushort offset, byte width, ulong value)
        {
            bits.Fields.Add(new FirmwareDebugBitField(name, offset, width, value));
        }

        private int[] BuildPinOutputsFromVoltages(string boardId)
        {
            var pins = GetExternalFirmwarePins(boardId);
            int count = Mathf.Min(pins.Length, ExternalFirmwarePinLimit);
            var snapshot = new int[count];
            Array.Fill(snapshot, -1);

            if (!_pinStatesByComponent.TryGetValue(boardId, out var states) || states == null)
            {
                return snapshot;
            }

            var outputMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var state in states)
            {
                if (string.IsNullOrWhiteSpace(state.Pin)) continue;
                outputMap[state.Pin] = state.IsOutput;
            }

            for (int i = 0; i < count; i++)
            {
                string pin = pins[i];
                if (!outputMap.TryGetValue(pin, out var isOutput) || !isOutput)
                {
                    snapshot[i] = -1;
                    continue;
                }

                if (_pinVoltages.TryGetValue($"{boardId}.{pin}", out var voltage))
                {
                    snapshot[i] = VoltageToOutputValue(voltage);
                }
                else
                {
                    snapshot[i] = 0;
                }
            }

            return snapshot;
        }

        private static int VoltageToOutputValue(float voltage)
        {
            if (voltage <= 0.05f) return 0;
            if (voltage >= VirtualMcu.DefaultHighVoltage - 0.25f) return 1;
            float ratio = Mathf.Clamp01(voltage / VirtualMcu.DefaultHighVoltage);
            int pwm = Mathf.Clamp(Mathf.RoundToInt(ratio * 255f), 0, 255);
            if (pwm <= 1) return 1;
            if (pwm >= 255) return 1;
            return pwm;
        }

        private void AppendFirmwarePerfSignals(TelemetryFrame frame)
        {
            if (frame?.Signals == null || _firmwarePerfByBoard.Count == 0) return;
            foreach (var entry in _firmwarePerfByBoard)
            {
                string boardId = entry.Key;
                var perf = entry.Value;
                if (perf == null) continue;
                frame.Signals[$"FW:{boardId}:cycles"] = perf.Cycles;
                frame.Signals[$"FW:{boardId}:adc_samples"] = perf.AdcSamples;
                for (int i = 0; i < perf.UartTxBytes.Length; i++)
                {
                    frame.Signals[$"FW:{boardId}:uart_tx{i}"] = perf.UartTxBytes[i];
                }
                for (int i = 0; i < perf.UartRxBytes.Length; i++)
                {
                    frame.Signals[$"FW:{boardId}:uart_rx{i}"] = perf.UartRxBytes[i];
                }
                frame.Signals[$"FW:{boardId}:spi_transfers"] = perf.SpiTransfers;
                frame.Signals[$"FW:{boardId}:twi_transfers"] = perf.TwiTransfers;
                frame.Signals[$"FW:{boardId}:wdt_resets"] = perf.WdtResets;
                frame.Signals[$"FW:{boardId}:drops"] = perf.DroppedOutputs;
            }
        }

        private void AppendFirmwareDebugSignals(TelemetryFrame frame)
        {
            if (frame?.Signals == null || _firmwareDebugByBoard.Count == 0) return;
            foreach (var entry in _firmwareDebugByBoard)
            {
                string boardId = entry.Key;
                var debug = entry.Value;
                if (debug == null) continue;
                string prefix = $"FW:{boardId}:dbg:";
                frame.Signals[$"{prefix}flash_bytes"] = debug.FlashBytes;
                frame.Signals[$"{prefix}sram_bytes"] = debug.SramBytes;
                frame.Signals[$"{prefix}eeprom_bytes"] = debug.EepromBytes;
                frame.Signals[$"{prefix}io_bytes"] = debug.IoBytes;
                frame.Signals[$"{prefix}cpu_hz"] = debug.CpuHz;
                frame.Signals[$"{prefix}pc"] = debug.ProgramCounter;
                frame.Signals[$"{prefix}sp"] = debug.StackPointer;
                frame.Signals[$"{prefix}sreg"] = debug.StatusRegister;
                frame.Signals[$"{prefix}stack_high_water"] = debug.StackHighWater;
                frame.Signals[$"{prefix}heap_top"] = debug.HeapTopAddress;
                frame.Signals[$"{prefix}stack_min"] = debug.StackMinAddress;
                frame.Signals[$"{prefix}data_segment_end"] = debug.DataSegmentEnd;
                frame.Signals[$"{prefix}stack_overflows"] = debug.StackOverflows;
                frame.Signals[$"{prefix}invalid_mem_accesses"] = debug.InvalidMemoryAccesses;
                frame.Signals[$"{prefix}interrupt_count"] = debug.InterruptCount;
                frame.Signals[$"{prefix}interrupt_latency_max"] = debug.InterruptLatencyMax;
                frame.Signals[$"{prefix}timing_violations"] = debug.TimingViolations;
                frame.Signals[$"{prefix}critical_section_cycles"] = debug.CriticalSectionCycles;
                frame.Signals[$"{prefix}sleep_cycles"] = debug.SleepCycles;
                frame.Signals[$"{prefix}flash_access_cycles"] = debug.FlashAccessCycles;
                frame.Signals[$"{prefix}uart_overflows"] = debug.UartOverflows;
                frame.Signals[$"{prefix}timer_overflows"] = debug.TimerOverflows;
                frame.Signals[$"{prefix}brown_out_resets"] = debug.BrownOutResets;
                frame.Signals[$"{prefix}gpio_state_changes"] = debug.GpioStateChanges;
                frame.Signals[$"{prefix}pwm_cycles"] = debug.PwmCycles;
                frame.Signals[$"{prefix}i2c_transactions"] = debug.I2cTransactions;
                frame.Signals[$"{prefix}spi_transactions"] = debug.SpiTransactions;
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
            var board = _circuit.Components.FirstOrDefault(IsMcuBoard);
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
