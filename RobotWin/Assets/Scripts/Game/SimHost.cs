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
        [SerializeField] private RealtimeScheduleConfig _realtimeConfig = new RealtimeScheduleConfig();
        private RealtimeSchedulerState _realtimeState;
        private readonly Dictionary<string, float> _pinVoltages = new Dictionary<string, float>();
        private readonly Dictionary<string, VirtualMcu> _virtualMcus = new Dictionary<string, VirtualMcu>();
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
        private FirmwareClient _externalFirmwareClient;
        private string _serialBuffer = string.Empty;
        private readonly Dictionary<string, ExternalFirmwareSession> _externalFirmwareSessions =
            new Dictionary<string, ExternalFirmwareSession>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _virtualSerialNextTime = new Dictionary<string, double>();
        private readonly Dictionary<string, bool> _usbConnectedByBoard = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _boardPowerById = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FirmwarePerfCounters> _firmwarePerfByBoard =
            new Dictionary<string, FirmwarePerfCounters>(System.StringComparer.OrdinalIgnoreCase);
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

        private const int ExternalFirmwarePinLimit = 20;
        private const int SerialBufferLimit = 4000;
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
        public int ExternalFirmwareSessionCount => _externalFirmwareSessions.Count;
        public int VirtualBoardCount => _virtualMcus.Count;
        public int PoweredBoardCount => _boardPowerById.Count;

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
            _externalFirmwareSessions.Clear();
            _externalFirmwarePinsByBoard.Clear();
            _externalFirmwarePinWarnings.Clear();
            _externalFirmwareExePath = null;
            _firmwarePerfByBoard.Clear();
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

            Debug.Log("[SimHost] Starting Simulation Loop...");

            _circuit = SessionManager.Instance != null ? SessionManager.Instance.CurrentCircuit : null;
            BuildPinNetMap();

            _useExternalFirmware = TrySetupExternalFirmwares();
            if (_useExternalFirmware)
            {
                _externalFirmwareClient?.LaunchFirmware(_externalFirmwareExePath);
                foreach (var session in _externalFirmwareSessions.Values)
                {
                    TryLoadExternalFirmware(session);
                }
            }
            BuildVirtualMcus();

            _useNativeEngine = !_useExternalFirmware
                && SessionManager.Instance != null
                && SessionManager.Instance.UseNativeEnginePins
                && !SessionManager.Instance.UseVirtualMcu;

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
                // No per-board process; shared firmware client handles all boards.
            }
            _externalFirmwareSessions.Clear();
            _externalFirmwarePinsByBoard.Clear();
            _externalFirmwarePinWarnings.Clear();
            _externalFirmwareClient?.StopFirmware();
            _externalFirmwareClient = null;
            _externalFirmwareExePath = null;
            _useExternalFirmware = false;
            _useNativeEngine = false;
            _nativePinsReady = false;
            _nativeAvrIndex.Clear();
            _virtualMcus.Clear();
            _pinVoltages.Clear();
            _pinStatesByComponent.Clear();
            _pullupResistances.Clear();
            _virtualSerialNextTime.Clear();
            _boardPowerById.Clear();
            _firmwarePerfByBoard.Clear();
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

            if (_realtimeConfig != null && _realtimeConfig.Enabled)
            {
                RunRealtimeScheduler(Time.unscaledDeltaTime);
                return;
            }

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

            float firmwareDt = Mathf.Max(0.0001f, _realtimeConfig.FirmwareDtSeconds);
            float circuitDt = Mathf.Max(0.0001f, _realtimeConfig.CircuitDtSeconds);
            float masterDt = Mathf.Max(0.0001f, _realtimeConfig.MasterDtSeconds);

            _realtimeState.AccumulatorSeconds += deltaSeconds;
            float frameStart = Time.realtimeSinceStartup;

            int masterSteps = Math.Min(_realtimeConfig.MaxStepsPerFrame,
                (int)Math.Floor(_realtimeState.AccumulatorSeconds / masterDt));
            if (masterSteps <= 0)
            {
                return;
            }

            _realtimeState.AccumulatorSeconds -= masterSteps * masterDt;

            for (int step = 0; step < masterSteps; step++)
            {
                _realtimeState.MasterTimeSeconds += masterDt;
                int firmwareSteps = 0;
                while (_realtimeState.MasterTimeSeconds >= _realtimeState.NextFirmwareAt &&
                       firmwareSteps < _realtimeConfig.MaxStepsPerFrame)
                {
                    float elapsedMs = (Time.realtimeSinceStartup - frameStart) * 1000f;
                    if (elapsedMs >= _realtimeConfig.FrameBudgetMs)
                    {
                        _budgetOverruns++;
                        return;
                    }

                    bool solveCircuit = _realtimeState.MasterTimeSeconds >= _realtimeState.NextCircuitAt;
                    bool allowFastPath = _realtimeConfig.AllowFastPath ||
                                         (_realtimeConfig.FrameBudgetMs - elapsedMs) < _realtimeConfig.CircuitBudgetMs;

                    float tickStart = Time.realtimeSinceStartup;
                    TickCount++;
                    RunTick(firmwareDt, solveCircuit, circuitDt, allowFastPath, _realtimeConfig.CircuitBudgetMs);
                    float tickMs = (Time.realtimeSinceStartup - tickStart) * 1000f;

                    if (!solveCircuit && tickMs > _realtimeConfig.FirmwareBudgetMs)
                    {
                        _budgetOverruns++;
                    }
                    if (solveCircuit && tickMs > (_realtimeConfig.FirmwareBudgetMs + _realtimeConfig.CircuitBudgetMs))
                    {
                        _budgetOverruns++;
                    }

                    _realtimeState.NextFirmwareAt += firmwareDt;
                    if (solveCircuit)
                    {
                        _realtimeState.NextCircuitAt += circuitDt;
                    }
                    firmwareSteps++;
                }
            }
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

        private bool TrySetupExternalFirmwares()
        {
            if (SessionManager.Instance == null || _circuit == null || _circuit.Components == null) return false;

            SessionManager.Instance.FindFirmware();
            var firmwarePath = SessionManager.Instance.FirmwarePath;
            if (string.IsNullOrWhiteSpace(firmwarePath) || !File.Exists(firmwarePath)) return false;

            if (SessionManager.Instance.UseVirtualMcu)
            {
                Debug.Log("[SimHost] VirtualMcu enabled; external firmware disabled.");
                return false;
            }

            var boards = _circuit.Components.Where(IsMcuBoard).ToList();
            if (boards.Count == 0) return false;
            var supportedBoards = new List<ComponentSpec>();

            if (_externalFirmwareClient == null)
            {
                _externalFirmwareClient = CreateSharedFirmwareClient();
            }

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
                    BoardProfile = ResolveMcuProfileId(board)
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
            if (session == null || _externalFirmwareClient == null) return false;
            if (session.Loaded) return true;
            if (string.IsNullOrWhiteSpace(session.BvmPath) || !File.Exists(session.BvmPath)) return false;
            if (! _externalFirmwareClient.LoadBvmFile(session.BoardId, session.BoardProfile, session.BvmPath)) return false;
            session.Loaded = true;
            return true;
        }

        private bool TryStepExternalFirmware(ExternalFirmwareSession session, float dtSeconds)
        {
            if (session == null || _externalFirmwareClient == null) return false;

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
                if (!session.LoggedFallback)
                {
                    session.LoggedFallback = true;
                    Debug.LogWarning($"[SimHost] External firmware not responding for {session.BoardId}. Falling back to VirtualMcu.");
                }
                return false;
            }

            ApplyFirmwareResult(session.BoardId, result);
            return true;
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
            return cache.Analog;
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

            for (int i = 0; i < pins.Length; i++)
            {
                string pin = pins[i];
                int state = i < outputs.Length ? outputs[i] : -1;
                bool isOutput = state >= 0;
                if (isOutput)
                {
                    _pinVoltages[$"{boardId}.{pin}"] = state > 0 ? VirtualMcu.DefaultHighVoltage : 0f;
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
