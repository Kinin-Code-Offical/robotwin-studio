using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using RobotTwin.CoreSim.Specs;
using RobotTwin.Game;
// using RobotTwin.CoreSim.Host;
using System.Linq;
using RobotTwin.CoreSim.Runtime;

namespace RobotTwin.UI
{
    public class RunModeController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _root;
        private Label _timeLabel;
        private Label _tickLabel;
        private Label _logContentLabel;
        private Label _serialContentLabel;
        private ScrollView _logScroll;
        private ScrollView _serialScroll;
        private bool _logAutoFollow = true;
        private bool _serialAutoFollow = true;
        private Label _pathLabel;
        private Label _projectLabel;
        private Label _usbStatusLabel;
        private ScrollView _usbBoardList;
        private TextField _componentSearchField;
        private VisualElement _circuit3DView;
        private Breadboard3DView _breadboardView;
        private string _layoutClass = string.Empty;
        
        [Header("Configuration")]
        [SerializeField] private string _firmwarePath;

        // Creation UI
        private TextField _newSignalName;
        private DropdownField _waveformType;
        private FloatField _param1; // Value/Amp
        private FloatField _param2; // Freq/EndVal
        private FloatField _param3; // Offset
        private Button _addSignalBtn;
        private ScrollView _signalList;
        private Toggle _injectionActiveToggle;

        // Telemetry UI
        private ScrollView _telemetryScroll;
        private VisualElement _telemetryList;
        private bool _telemetryBuilt;
        private readonly Dictionary<string, TelemetryEntry> _boardTelemetry =
            new Dictionary<string, TelemetryEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TelemetryEntry> _batteryTelemetry =
            new Dictionary<string, TelemetryEntry>(StringComparer.OrdinalIgnoreCase);
        private TelemetryEntry _summaryTelemetry;
        private TelemetryEntry _validationTelemetry;
        private TelemetryEntry _thermalTelemetry;

        // State
        private RobotTwin.Game.SimHost _host;
        private CircuitSpec _activeCircuit;
        
        private bool _isRunning = false;
        private string _runOutputPath;
        private readonly Dictionary<string, bool> _usbConnectedByBoard = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Toggle> _usbToggles = new Dictionary<string, Toggle>(StringComparer.OrdinalIgnoreCase);
        private string _lastValidationSummary = string.Empty;
        private string _lastPowerSummary = string.Empty;
        private readonly Dictionary<string, SwitchStateSnapshot> _switchStateSnapshots =
            new Dictionary<string, SwitchStateSnapshot>(StringComparer.OrdinalIgnoreCase);

        private sealed class SwitchStateSnapshot
        {
            public bool HasState;
            public string State;
            public bool HasClosed;
            public string Closed;
            public bool HasPressed;
            public string Pressed;
        }

        private sealed class TelemetryEntry
        {
            public VisualElement Row;
            public Label Title;
            public Label Value;
        }

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) { enabled = false; return; }
            var root = _doc.rootVisualElement;
            if (root == null) { enabled = false; return; }
            _root = root;
            InitializeResponsiveLayout(root);

            // Bind UI
            _timeLabel = root.Q<Label>("TimeLabel");
            _tickLabel = root.Q<Label>("TickLabel");
            _logContentLabel = root.Q<Label>("LogContentLabel");
            _serialContentLabel = root.Q<Label>("SerialContentLabel");
            _logScroll = root.Q<ScrollView>("LogScroll");
            _serialScroll = root.Q<ScrollView>("SerialScroll");
            InitializeAutoScroll();
            _pathLabel = root.Q<Label>("TelemetryPathLabel");
            _projectLabel = root.Q<Label>("ProjectLabel");
            _usbStatusLabel = root.Q<Label>("UsbStatusLabel");
            _usbBoardList = root.Q<ScrollView>("UsbBoardList");
            _telemetryScroll = root.Q<ScrollView>("TelemetryScroll");
            _telemetryList = root.Q<VisualElement>("TelemetryList");
            _componentSearchField = root.Q<TextField>("ComponentSearchField");
            _circuit3DView = root.Q<VisualElement>("Circuit3DView");
            
            // Buttons
            root.Q<Button>("StopButton")?.RegisterCallback<ClickEvent>(OnStopClicked);
            InitVisualization(root);
            StartSimulation(); // Start Loop
        }

        private void OnDisable() => StopSimulation();

        private void InitializeResponsiveLayout(VisualElement root)
        {
            ApplyResponsiveLayout();
            root.RegisterCallback<GeometryChangedEvent>(_ => ApplyResponsiveLayout());
        }

        private void ApplyResponsiveLayout()
        {
            if (_root == null) return;
            float width = _root.resolvedStyle.width;
            if (width <= 0)
            {
                width = Screen.width;
            }

            string nextClass;
            if (width < 1200f)
            {
                nextClass = "run-compact";
            }
            else if (width < 1600f)
            {
                nextClass = "run-medium";
            }
            else
            {
                nextClass = "run-wide";
            }

            if (string.Equals(_layoutClass, nextClass, StringComparison.Ordinal)) return;
            _root.RemoveFromClassList("run-compact");
            _root.RemoveFromClassList("run-medium");
            _root.RemoveFromClassList("run-wide");
            _root.AddToClassList(nextClass);
            _layoutClass = nextClass;
        }

        private void StartSimulation()
        {
             if (SessionManager.Instance == null || SessionManager.Instance.CurrentCircuit == null)
            {
                Debug.LogError("[RunMode] No Session/Circuit found!");
                return;
            }
            
            _activeCircuit = SessionManager.Instance.CurrentCircuit;
            CaptureSwitchDefaults();
            if (_projectLabel != null)
            {
                string name = SessionManager.Instance.CurrentProject?.ProjectName;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = _activeCircuit?.Id;
                }
                _projectLabel.text = string.IsNullOrWhiteSpace(name) ? "Project: -" : $"Project: {name}";
            }
            InitVisualization(_doc.rootVisualElement);
            InitBreadboardView(_doc.rootVisualElement);

            // Ensure SimHost exists
            if (RobotTwin.Game.SimHost.Instance == null)
            {
                var go = new GameObject("SimHost");
                _host = go.AddComponent<RobotTwin.Game.SimHost>();
            }
            else
            {
                _host = RobotTwin.Game.SimHost.Instance;
            }

            _telemetryBuilt = false;
            BuildUsbList();
            BuildTelemetryEntries();

            // Start
            _host.BeginSimulation();
            _isRunning = true;
        }

        private void StopSimulation()
        {
            if (_host != null)
            {
                _host.StopSimulation();
            }
            RestoreSwitchDefaults();
            _isRunning = false;
        }

        private void OnStopClicked(ClickEvent evt)
        {
            StopSimulation();
            UnityEngine.SceneManagement.SceneManager.LoadScene(1); // Back to CircuitStudio
        }

        private void FixedUpdate()
        {
            // Polyglot Architecture: Delegate heavy physics/solving to C++ NativeEngine
            if (_isRunning && _host == null)
            {
                RobotTwin.Core.NativeBridge.StepSimulation(Time.fixedDeltaTime);
            }

            // Host runs in own thread, nothing to do here besides maybe input polling
        }

        private void HandleHostTick(double simTime)
        {
            // Marshal Key Data to UI Thread if needed
        }

        /*
        private void OnSimulationEvent(EventLogEntry entry)
        {
            _recorder?.RecordEvent(entry);
            AppendLog($"[{entry.TimeSeconds:F2}] {entry.Code}: {entry.Message}");
        }
        */

        private void AppendLog(string text)
        {
            if (_logContentLabel != null)
            {
                _logContentLabel.text = (text + "\n" + _logContentLabel.text)
                    .Substring(0, Mathf.Min(_logContentLabel.text.Length + text.Length + 1, 2000));
                AutoScroll(_logScroll, _logAutoFollow);
            }
        }

        private void Update()
        {
            if (!_isRunning || _host == null) return;

            if (_timeLabel != null) _timeLabel.text = $"Time: {Time.time:F2}s"; // _host.SimTime in simple mode
            if (_tickLabel != null) _tickLabel.text = "Running";
            if (_serialContentLabel != null)
            {
                if (HasAnyUsbConnected())
                {
                    _serialContentLabel.text = string.IsNullOrEmpty(_host.SerialOutput) ? "Serial ready." : _host.SerialOutput;
                }
                else
                {
                    _serialContentLabel.text = string.IsNullOrEmpty(_host.SerialOutput)
                        ? "USB disconnected. Serial paused."
                        : _host.SerialOutput;
                }
                AutoScroll(_serialScroll, _serialAutoFollow);
            }

            UpdateVisualization();
            UpdateTelemetry();
        }

        private void CaptureSwitchDefaults()
        {
            _switchStateSnapshots.Clear();
            if (_activeCircuit?.Components == null) return;
            foreach (var comp in _activeCircuit.Components)
            {
                if (comp == null || comp.Properties == null) continue;
                if (!IsSwitchComponent(comp.Type) && !IsButtonType(comp.Type)) continue;

                var snapshot = new SwitchStateSnapshot();
                if (comp.Properties.TryGetValue("state", out var state))
                {
                    snapshot.HasState = true;
                    snapshot.State = state;
                }
                if (comp.Properties.TryGetValue("closed", out var closed))
                {
                    snapshot.HasClosed = true;
                    snapshot.Closed = closed;
                }
                if (comp.Properties.TryGetValue("pressed", out var pressed))
                {
                    snapshot.HasPressed = true;
                    snapshot.Pressed = pressed;
                }
                _switchStateSnapshots[comp.Id] = snapshot;
            }
        }

        private void RestoreSwitchDefaults()
        {
            if (_activeCircuit?.Components == null) return;
            foreach (var comp in _activeCircuit.Components)
            {
                if (comp == null || comp.Properties == null) continue;
                if (!IsSwitchComponent(comp.Type) && !IsButtonType(comp.Type)) continue;
                if (!_switchStateSnapshots.TryGetValue(comp.Id, out var snapshot)) continue;

                if (snapshot.HasState)
                {
                    comp.Properties["state"] = snapshot.State;
                }
                else
                {
                    comp.Properties.Remove("state");
                }

                if (snapshot.HasClosed)
                {
                    comp.Properties["closed"] = snapshot.Closed;
                }
                else
                {
                    comp.Properties.Remove("closed");
                }

                if (snapshot.HasPressed)
                {
                    comp.Properties["pressed"] = snapshot.Pressed;
                }
                else
                {
                    comp.Properties.Remove("pressed");
                }
            }
        }

        private void BuildUsbList()
        {
            _usbConnectedByBoard.Clear();
            _usbToggles.Clear();

            if (_usbBoardList == null || _activeCircuit?.Components == null) return;

            _usbBoardList.Clear();
            var boards = _activeCircuit.Components.Where(c => IsArduinoType(c.Type)).ToList();
            foreach (var board in boards)
            {
                var row = new VisualElement();
                row.AddToClassList("usb-row");

                var name = new Label(board.Id);
                name.AddToClassList("usb-name");

                var toggle = new Toggle();
                toggle.AddToClassList("usb-toggle");
                toggle.value = true;

                row.Add(name);
                row.Add(toggle);
                _usbBoardList.Add(row);

                _usbConnectedByBoard[board.Id] = true;
                _usbToggles[board.Id] = toggle;
                if (_host != null)
                {
                    _host.SetUsbConnected(board.Id, true);
                }

                string captured = board.Id;
                toggle.RegisterValueChangedCallback(evt => SetUsbConnected(captured, evt.newValue));
            }

            UpdateUsbStatus();
        }

        private void SetUsbConnected(string boardId, bool isConnected)
        {
            if (string.IsNullOrWhiteSpace(boardId)) return;
            _usbConnectedByBoard[boardId] = isConnected;
            if (_host != null)
            {
                _host.SetUsbConnected(boardId, isConnected);
            }
            UpdateUsbStatus();
        }

        private bool HasAnyUsbConnected()
        {
            if (_usbConnectedByBoard.Count == 0) return false;
            return _usbConnectedByBoard.Values.Any(value => value);
        }

        private void UpdateUsbStatus()
        {
            if (_usbStatusLabel == null) return;
            if (_usbConnectedByBoard.Count == 0)
            {
                _usbStatusLabel.text = "USB: N/A";
                return;
            }
            int connected = _usbConnectedByBoard.Values.Count(v => v);
            _usbStatusLabel.text = $"USB: {connected}/{_usbConnectedByBoard.Count} connected";
        }

        private void UpdateTelemetry()
        {
            var telemetry = _host?.LastTelemetry;
            if (_telemetryScroll == null || _telemetryList == null) return;
            if (!_telemetryBuilt)
            {
                BuildTelemetryEntries();
            }

            UpdateBoardTelemetry(telemetry);
            UpdateBatteryTelemetry(telemetry);
            UpdateDiagnosticsTelemetry(telemetry);

            LogValidationSummary(telemetry);
            LogPowerSummary();
        }

        private void BuildTelemetryEntries()
        {
            if (_telemetryList == null) return;
            _telemetryList.Clear();
            _boardTelemetry.Clear();
            _batteryTelemetry.Clear();
            _summaryTelemetry = null;
            _validationTelemetry = null;
            _thermalTelemetry = null;

            AddTelemetrySection("Boards");
            if (_activeCircuit?.Components != null)
            {
                foreach (var board in _activeCircuit.Components.Where(c => IsArduinoType(c.Type)))
                {
                    var entry = CreateTelemetryEntry($"{board.Id} ({board.Type})");
                    _boardTelemetry[board.Id] = entry;
                }
            }
            if (_boardTelemetry.Count == 0)
            {
                AddTelemetryPlaceholder("No Arduino boards.");
            }

            AddTelemetrySection("Sources");
            if (_activeCircuit?.Components != null)
            {
                foreach (var battery in _activeCircuit.Components.Where(c =>
                             string.Equals(c.Type, "Battery", System.StringComparison.OrdinalIgnoreCase)))
                {
                    var entry = CreateTelemetryEntry($"{battery.Id} (Battery)");
                    _batteryTelemetry[battery.Id] = entry;
                }
            }
            if (_batteryTelemetry.Count == 0)
            {
                AddTelemetryPlaceholder("No batteries.");
            }

            AddTelemetrySection("Diagnostics");
            _summaryTelemetry = CreateTelemetryEntry("Signals");
            _validationTelemetry = CreateTelemetryEntry("Validation");
            _thermalTelemetry = CreateTelemetryEntry("Thermal");

            _telemetryBuilt = true;
        }

        private void AddTelemetrySection(string title)
        {
            if (_telemetryList == null) return;
            var label = new Label(title);
            label.AddToClassList("telemetry-section");
            _telemetryList.Add(label);
        }

        private void AddTelemetryPlaceholder(string message)
        {
            if (_telemetryList == null) return;
            var label = new Label(message);
            label.AddToClassList("telemetry-empty");
            _telemetryList.Add(label);
        }

        private TelemetryEntry CreateTelemetryEntry(string title)
        {
            var row = new VisualElement();
            row.AddToClassList("telemetry-item");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("telemetry-title");

            var valueLabel = new Label("N/A");
            valueLabel.AddToClassList("telemetry-value");

            row.Add(titleLabel);
            row.Add(valueLabel);
            _telemetryList.Add(row);

            return new TelemetryEntry
            {
                Row = row,
                Title = titleLabel,
                Value = valueLabel
            };
        }

        private void UpdateBoardTelemetry(TelemetryFrame telemetry)
        {
            if (_activeCircuit?.Components == null) return;
            foreach (var kvp in _boardTelemetry)
            {
                var entry = kvp.Value;
                var board = _activeCircuit.Components.FirstOrDefault(c =>
                    string.Equals(c.Id, kvp.Key, StringComparison.OrdinalIgnoreCase));
                if (board == null)
                {
                    entry.Value.text = "Board not found.";
                    continue;
                }

                string usbText = "USB:unknown";
                if (_usbConnectedByBoard.TryGetValue(board.Id, out var usb))
                {
                    usbText = usb ? "USB:on" : "USB:off";
                }

                string powerText = "Power:unknown";
                if (_host?.BoardPowerById != null && _host.BoardPowerById.TryGetValue(board.Id, out var powered))
                {
                    powerText = powered ? "Power:on" : "Power:off";
                }

                if (telemetry == null)
                {
                    entry.Value.text = $"{usbText} {powerText}\nTelemetry: N/A";
                    continue;
                }

                var lines = new List<string> { $"{usbText} {powerText}" };
                if (TryGetFirmwareLabel(board, out var fwLabel))
                {
                    lines.Add(fwLabel);
                }
                else
                {
                    lines.Add("FW: none");
                }
                lines.Add(BuildArduinoRailSummary(board, telemetry));
                lines.Add(BuildArduinoPinSummary(board, telemetry));
                entry.Value.text = string.Join("\n", lines);
            }
        }

        private void UpdateBatteryTelemetry(TelemetryFrame telemetry)
        {
            if (_activeCircuit?.Components == null) return;
            foreach (var kvp in _batteryTelemetry)
            {
                var entry = kvp.Value;
                var battery = _activeCircuit.Components.FirstOrDefault(c =>
                    string.Equals(c.Id, kvp.Key, StringComparison.OrdinalIgnoreCase));
                if (battery == null)
                {
                    entry.Value.text = "Battery not found.";
                    continue;
                }

                double voltage = TryGetBatteryVoltage(_activeCircuit, battery.Id, 9.0);
                if (telemetry == null)
                {
                    entry.Value.text = $"{voltage:F2}V\nTelemetry: N/A";
                    continue;
                }

                if (telemetry.Signals.TryGetValue($"SRC:{battery.Id}:V", out var srcV))
                {
                    voltage = srcV;
                }
                if (TryGetPinVoltageDelta(_activeCircuit, telemetry, battery.Id, "+", "-", out var vDiff))
                {
                    voltage = vDiff;
                }

                string line1 = $"{voltage:F2}V";
                string line2 = string.Empty;
                string line3 = string.Empty;

                if (telemetry.Signals.TryGetValue($"SRC:{battery.Id}:I", out var current))
                {
                    double power = voltage * current;
                    line1 = $"{voltage:F2}V {(current * 1000.0):F1}mA";
                    line2 = $"P:{Math.Abs(power) * 1000.0:F1}mW";
                }

                if (telemetry.Signals.TryGetValue($"SRC:{battery.Id}:SOC", out var soc))
                {
                    line3 = $"SOC:{soc * 100.0:F0}%";
                }
                if (telemetry.Signals.TryGetValue($"SRC:{battery.Id}:RINT", out var rint))
                {
                    string rintText = $"Rint:{rint:F2}";
                    line3 = string.IsNullOrWhiteSpace(line3) ? rintText : $"{line3} {rintText}";
                }

                var lines = new List<string> { line1 };
                if (!string.IsNullOrWhiteSpace(line2)) lines.Add(line2);
                if (!string.IsNullOrWhiteSpace(line3)) lines.Add(line3);
                entry.Value.text = string.Join("\n", lines);
            }
        }

        private void UpdateDiagnosticsTelemetry(TelemetryFrame telemetry)
        {
            if (_summaryTelemetry != null)
            {
                if (telemetry?.Signals == null)
                {
                    _summaryTelemetry.Value.text = "Signals: N/A";
                }
                else
                {
                    int total = telemetry.Signals.Count;
                    int netCount = CountTelemetryByPrefix(telemetry, "NET:");
                    int compCount = CountTelemetryByPrefix(telemetry, "COMP:");
                    int srcCount = CountTelemetryByPrefix(telemetry, "SRC:");
                    int usbConnected = _usbConnectedByBoard.Values.Count(value => value);
                    int usbTotal = _usbConnectedByBoard.Count;
                    _summaryTelemetry.Value.text =
                        $"Signals: {total} (NET:{netCount} COMP:{compCount} SRC:{srcCount})\nUSB: {usbConnected}/{usbTotal}";
                }
            }

            if (_validationTelemetry != null)
            {
                if (telemetry?.ValidationMessages == null)
                {
                    _validationTelemetry.Value.text = "Validation: N/A";
                }
                else if (telemetry.ValidationMessages.Count == 0)
                {
                    _validationTelemetry.Value.text = "Validation: none";
                }
                else
                {
                    string sample = string.Join(" | ", telemetry.ValidationMessages.Take(2));
                    _validationTelemetry.Value.text = $"Validation: {telemetry.ValidationMessages.Count} - {sample}";
                }
            }

            if (_thermalTelemetry != null)
            {
                double maxTemp = double.NaN;
                if (telemetry?.Signals != null)
                {
                    foreach (var kvp in telemetry.Signals)
                    {
                        if (!kvp.Key.EndsWith(":T", System.StringComparison.OrdinalIgnoreCase)) continue;
                        if (double.IsNaN(maxTemp) || kvp.Value > maxTemp) maxTemp = kvp.Value;
                    }
                }
                _thermalTelemetry.Value.text = double.IsNaN(maxTemp) ? "Max temp: N/A" : $"Max temp: {maxTemp:F1}C";
            }
        }

        private string BuildArduinoRailSummary(ComponentSpec comp, TelemetryFrame telemetry)
        {
            if (comp == null || telemetry == null) return "Rails: N/A";
            var rails = new List<string>();
            AppendPinVoltage(comp, telemetry, "5V", "5V", rails);
            AppendPinVoltage(comp, telemetry, "3V3", "3V3", rails);
            AppendPinVoltage(comp, telemetry, "IOREF", "IOREF", rails);
            AppendPinVoltage(comp, telemetry, "VIN", "VIN", rails);
            AppendPinVoltage(comp, telemetry, "VCC", "VCC", rails);
            if (rails.Count == 0) return "Rails: N/A";
            return "Rails: " + string.Join(" ", rails);
        }

        private string BuildArduinoPinSummary(ComponentSpec comp, TelemetryFrame telemetry)
        {
            if (comp == null || telemetry == null) return "Pin: N/A";
            if (!TryGetPrimaryArduinoPin(comp, out var pin) || string.IsNullOrWhiteSpace(pin)) return "Pin: N/A";
            if (TryGetPinVoltage(comp, pin, telemetry, out var voltage))
            {
                return $"Pin {pin}: {voltage:F2}V";
            }
            return $"Pin {pin}: N/A";
        }

        private static void AppendPinVoltage(ComponentSpec comp, TelemetryFrame telemetry, string pin, string label, List<string> parts)
        {
            if (comp == null || telemetry == null || parts == null) return;
            if (TryGetPinVoltage(comp, pin, telemetry, out var voltage))
            {
                parts.Add($"{label}={voltage:F2}V");
            }
        }

        private static int CountTelemetryByPrefix(TelemetryFrame telemetry, string prefix)
        {
            if (telemetry?.Signals == null || string.IsNullOrWhiteSpace(prefix)) return 0;
            int count = 0;
            foreach (var key in telemetry.Signals.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }
            return count;
        }

        private void LogValidationSummary(TelemetryFrame telemetry)
        {
            if (telemetry == null || telemetry.ValidationMessages == null) return;
            string summary = telemetry.ValidationMessages.Count == 0
                ? string.Empty
                : string.Join("; ", telemetry.ValidationMessages);
            if (string.Equals(summary, _lastValidationSummary, StringComparison.Ordinal)) return;
            _lastValidationSummary = summary;
            if (string.IsNullOrWhiteSpace(summary)) return;
            AppendLog($"[Circuit] {summary}");
        }

        private void LogPowerSummary()
        {
            if (_host == null || _host.BoardPowerById == null) return;
            var unpowered = _host.BoardPowerById.Where(kvp => !kvp.Value).Select(kvp => kvp.Key).ToList();
            string summary = unpowered.Count == 0
                ? "all boards powered"
                : $"unpowered boards: {string.Join(", ", unpowered)}";
            if (string.Equals(summary, _lastPowerSummary, StringComparison.Ordinal)) return;
            _lastPowerSummary = summary;
            if (unpowered.Count == 0) return;
            AppendLog($"[Power] {summary}");
        }

        /*
        private void OnAddSignal(ClickEvent evt)
        {
            // Stubbed
        }

        private void AddWaveform(string name, object wf)
        {
            // _activeWaveforms[name] = wf;
            // RefreshSignalList();
        }
        */



        /*
        private void RefreshSignalList()
        {
            if (_signalList == null) return;
            _signalList.Clear();
            // foreach (var kvp in _activeWaveforms)
            // {
            //    var row = new Label($"{kvp.Key} ({kvp.Value.GetType().Name})");
            //    row.style.color = Color.cyan;
            //    _signalList.Add(row);
            // }
        }
        */

        // --- Visualization Logic ---
        private ScrollView _componentList;
        private Dictionary<string, Label> _componentLabels = new Dictionary<string, Label>();
        private Dictionary<string, string> _componentTypes = new Dictionary<string, string>();
        private Dictionary<string, VisualElement> _componentRows = new Dictionary<string, VisualElement>();
        private Dictionary<string, string> _componentSearchKeys = new Dictionary<string, string>();

        private void InitVisualization(VisualElement root)
        {
            _componentList = root.Q<ScrollView>("ComponentList");
            if (_componentList == null || _activeCircuit == null) return;

            _componentList.Clear();
            _componentLabels.Clear();
            _componentTypes.Clear();
            _componentRows.Clear();
            _componentSearchKeys.Clear();

            foreach (var comp in _activeCircuit.Components)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.FlexStart;
                row.style.alignItems = Align.Center;
                row.AddToClassList("component-row");

                var nameLbl = new Label($"{comp.Id} ({comp.Type})");
                nameLbl.style.color = Color.white;
                nameLbl.AddToClassList("component-name");

                VisualElement controls = null;
                if (IsSwitchComponent(comp.Type))
                {
                    controls = BuildSwitchControls(comp);
                }
                else if (IsButtonType(comp.Type))
                {
                    controls = BuildButtonControls(comp);
                }

                var statusLbl = new Label("OFF");
                statusLbl.style.color = Color.gray;
                statusLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                statusLbl.AddToClassList("component-status");

                row.Add(nameLbl);
                if (controls != null)
                {
                    row.Add(controls);
                }
                row.Add(statusLbl);
                _componentList.Add(row);

                _componentLabels[comp.Id] = statusLbl;
                _componentTypes[comp.Id] = comp.Type;
                _componentRows[comp.Id] = row;
                _componentSearchKeys[comp.Id] = $"{comp.Id} {comp.Type}".ToLowerInvariant();
            }

            if (_componentSearchField != null)
            {
                _componentSearchField.isDelayed = false;
                _componentSearchField.RegisterValueChangedCallback(evt =>
                {
                    ApplyComponentFilter(evt.newValue);
                });
                ApplyComponentFilter(_componentSearchField.value);
            }
        }

        private VisualElement BuildSwitchControls(ComponentSpec comp)
        {
            var controls = new VisualElement();
            controls.AddToClassList("component-controls");

            var toggle = new Toggle(string.Empty);
            toggle.AddToClassList("component-toggle");
            toggle.AddToClassList("component-toggle-switch");
            bool isOpen = !IsSwitchClosed(comp);
            toggle.SetValueWithoutNotify(isOpen);
            toggle.EnableInClassList("toggle-open", isOpen);
            toggle.EnableInClassList("toggle-closed", !isOpen);
            toggle.RegisterValueChangedCallback(evt =>
            {
                bool open = evt.newValue;
                SetSwitchState(comp, !open);
                toggle.EnableInClassList("toggle-open", open);
                toggle.EnableInClassList("toggle-closed", !open);
            });

            controls.Add(toggle);
            return controls;
        }

        private VisualElement BuildButtonControls(ComponentSpec comp)
        {
            var controls = new VisualElement();
            controls.AddToClassList("component-controls");

            var stateToggle = new Toggle(string.Empty);
            stateToggle.AddToClassList("component-toggle");
            stateToggle.AddToClassList("component-toggle-switch");

            void UpdateButtonToggle(bool closed)
            {
                bool isOpen = !closed;
                stateToggle.SetValueWithoutNotify(isOpen);
                stateToggle.EnableInClassList("toggle-open", isOpen);
                stateToggle.EnableInClassList("toggle-closed", !isOpen);
            }

            UpdateButtonToggle(IsSwitchClosed(comp));

            var toggleMode = new Toggle(string.Empty);
            toggleMode.AddToClassList("component-toggle");
            toggleMode.AddToClassList("component-toggle-mode");
            toggleMode.AddToClassList("component-toggle-switch");
            toggleMode.tooltip = "Toggle mode";

            stateToggle.SetEnabled(toggleMode.value);
            toggleMode.RegisterValueChangedCallback(evt =>
            {
                stateToggle.SetEnabled(evt.newValue);
            });

            stateToggle.RegisterValueChangedCallback(evt =>
            {
                if (!toggleMode.value)
                {
                    UpdateButtonToggle(IsSwitchClosed(comp));
                    return;
                }
                bool closed = !evt.newValue;
                SetSwitchState(comp, closed);
                UpdateButtonToggle(closed);
            });

            var pressBtn = new Button { text = "Press" };
            pressBtn.AddToClassList("component-press");

            pressBtn.RegisterCallback<PointerDownEvent>(_ =>
            {
                if (toggleMode.value) return;
                SetSwitchState(comp, true);
                UpdateButtonToggle(true);
            });
            pressBtn.RegisterCallback<PointerUpEvent>(_ =>
            {
                if (toggleMode.value) return;
                SetSwitchState(comp, false);
                UpdateButtonToggle(false);
            });
            pressBtn.RegisterCallback<ClickEvent>(_ =>
            {
                if (!toggleMode.value) return;
                bool nextClosed = !IsSwitchClosed(comp);
                SetSwitchState(comp, nextClosed);
                UpdateButtonToggle(nextClosed);
            });

            controls.Add(stateToggle);
            controls.Add(toggleMode);
            controls.Add(pressBtn);
            return controls;
        }

        private static bool IsSwitchComponent(string type)
        {
            return string.Equals(type, "Switch", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsButtonType(string type)
        {
            return string.Equals(type, "Button", System.StringComparison.OrdinalIgnoreCase);
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

        private static void SetSwitchState(ComponentSpec comp, bool closed)
        {
            if (comp == null) return;
            if (comp.Properties == null)
            {
                comp.Properties = new Dictionary<string, string>();
            }
            comp.Properties["state"] = closed ? "closed" : "open";
            comp.Properties["pressed"] = closed ? "true" : "false";
            comp.Properties["closed"] = closed ? "true" : "false";
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

        private void ApplyComponentFilter(string query)
        {
            string needle = string.IsNullOrWhiteSpace(query) ? string.Empty : query.Trim().ToLowerInvariant();
            foreach (var kvp in _componentRows)
            {
                string id = kvp.Key;
                var row = kvp.Value;
                if (row == null) continue;
                if (needle.Length == 0)
                {
                    row.style.display = DisplayStyle.Flex;
                    continue;
                }
                if (_componentSearchKeys.TryGetValue(id, out var key) && key.Contains(needle))
                {
                    row.style.display = DisplayStyle.Flex;
                }
                else
                {
                    row.style.display = DisplayStyle.None;
                }
            }
        }

        private void InitializeAutoScroll()
        {
            SetupAutoFollow(_logScroll, value => _logAutoFollow = value);
            SetupAutoFollow(_serialScroll, value => _serialAutoFollow = value);
        }

        private static void SetupAutoFollow(ScrollView scrollView, Action<bool> setFlag)
        {
            if (scrollView == null || setFlag == null) return;
            void UpdateFollow()
            {
                setFlag(IsNearBottom(scrollView));
            }

            scrollView.RegisterCallback<WheelEvent>(_ => UpdateFollow());
            scrollView.RegisterCallback<PointerDownEvent>(_ => UpdateFollow());
            if (scrollView.verticalScroller != null)
            {
                scrollView.verticalScroller.valueChanged += _ => UpdateFollow();
            }
        }

        private static void AutoScroll(ScrollView scrollView, bool autoFollow)
        {
            if (scrollView == null || !autoFollow) return;
            scrollView.schedule.Execute(() =>
            {
                float max = GetMaxScroll(scrollView);
                scrollView.scrollOffset = new Vector2(0, max);
            });
        }

        private static bool IsNearBottom(ScrollView scrollView)
        {
            if (scrollView == null) return true;
            float max = GetMaxScroll(scrollView);
            return scrollView.scrollOffset.y >= max - 2f;
        }

        private static float GetMaxScroll(ScrollView scrollView)
        {
            if (scrollView == null) return 0f;
            float contentHeight = scrollView.contentContainer.layout.height;
            float viewportHeight = scrollView.contentViewport != null
                ? scrollView.contentViewport.layout.height
                : scrollView.layout.height;
            return Mathf.Max(0f, contentHeight - viewportHeight);
        }

        private void InitBreadboardView(VisualElement root)
        {
            if (_circuit3DView == null || _activeCircuit == null) return;
            if (_breadboardView == null)
            {
                var go = new GameObject("Breadboard3DView");
                go.transform.SetParent(transform, false);
                _breadboardView = go.AddComponent<Breadboard3DView>();
            }

            _circuit3DView.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                int width = Mathf.RoundToInt(evt.newRect.width);
                int height = Mathf.RoundToInt(evt.newRect.height - 18f);
                _breadboardView.Initialize(width, height);
                _breadboardView.Build(_activeCircuit);
                if (_breadboardView.TargetTexture != null)
                {
                    _circuit3DView.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_breadboardView.TargetTexture));
                }
            });
        }

        private void UpdateVisualization()
        {
            if (_host == null) return;

            var telemetry = _host.LastTelemetry;
            if (telemetry == null)
            {
                foreach (var kvp in _componentLabels)
                {
                    string id = kvp.Key;
                    Label lbl = kvp.Value;

                    lbl.text = "NO DATA";
                    lbl.style.color = Color.gray;
                }
                return;
            }

            foreach (var kvp in _componentLabels)
            {
                string id = kvp.Key;
                Label lbl = kvp.Value;
                _componentTypes.TryGetValue(id, out var type);

                if (IsArduinoType(type))
                {
                    var comp = _activeCircuit?.Components?.FirstOrDefault(c => c.Id == id);
                    bool hasFirmware = TryGetFirmwareLabel(comp, out var fwLabel);
                    string pinInfo = string.Empty;
                    if (comp != null && TryGetPrimaryArduinoPin(comp, out var pin) &&
                        TryGetPinVoltage(comp, pin, telemetry, out var pinVoltage))
                    {
                        pinInfo = $" {pin}:{pinVoltage:F2}V";
                    }
                    lbl.text = fwLabel + pinInfo;
                    lbl.style.color = hasFirmware ? new Color(0.3f, 0.9f, 0.6f) : Color.gray;
                    continue;
                }

                if (IsSwitchComponent(type) || IsButtonType(type))
                {
                    var comp = _activeCircuit?.Components?.FirstOrDefault(c => c.Id == id);
                    bool closed = IsSwitchClosed(comp);
                    bool open = !closed;
                    lbl.text = closed ? "CLOSED" : "OPEN";
                    lbl.style.color = open ? new Color(0.3f, 0.9f, 0.6f) : Color.gray;
                    continue;
                }

                if (string.Equals(type, "LED", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (telemetry.Signals.TryGetValue($"COMP:{id}:I", out var current))
                    {
                        bool isOn = Math.Abs(current) > 0.002;
                        double vDiff = telemetry.Signals.TryGetValue($"COMP:{id}:V", out var v) ? v : 0.0;
                        double power = telemetry.Signals.TryGetValue($"COMP:{id}:P", out var pwr) ? pwr : Math.Abs(current * vDiff);
                        double intensity = telemetry.Signals.TryGetValue($"COMP:{id}:L", out var lum) ? lum : 0.0;
                        double tempC = telemetry.Signals.TryGetValue($"COMP:{id}:T", out var t) ? t : double.NaN;
                        string tempText = double.IsNaN(tempC) ? "T:N/A" : $"T:{tempC:F1}C";
                        lbl.text = $"{(isOn ? "ON" : "OFF")} I:{(current * 1000.0):F1}mA V:{Math.Abs(vDiff):F2}V\nP:{power * 1000.0:F1}mW {tempText} L:{intensity * 100.0:F0}%";
                        lbl.style.color = isOn ? Color.green : Color.gray;
                    }
                    else
                    {
                        lbl.text = "OFF";
                        lbl.style.color = Color.gray;
                    }
                }
                else if (string.Equals(type, "Resistor", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (telemetry.Signals.TryGetValue($"COMP:{id}:I", out var current))
                    {
                        double vDiff = telemetry.Signals.TryGetValue($"COMP:{id}:V", out var v) ? v : 0.0;
                        double power = telemetry.Signals.TryGetValue($"COMP:{id}:P", out var pwr) ? pwr : Math.Abs(current * vDiff);
                        double resistance = telemetry.Signals.TryGetValue($"COMP:{id}:R", out var r) ? r : double.NaN;
                        double tempC = telemetry.Signals.TryGetValue($"COMP:{id}:T", out var t) ? t : double.NaN;
                        string rText = double.IsNaN(resistance) ? "R:N/A" : $"R:{resistance:F0}Ω";
                        string tempText = double.IsNaN(tempC) ? "T:N/A" : $"T:{tempC:F1}C";
                        lbl.text = $"I:{(current * 1000.0):F1}mA V:{Math.Abs(vDiff):F2}V\nP:{power * 1000.0:F1}mW {rText} {tempText}";
                        lbl.style.color = Color.white;
                    }
                    else
                    {
                        lbl.text = "OK";
                        lbl.style.color = Color.white;
                    }
                }
                else if (string.Equals(type, "Battery", System.StringComparison.OrdinalIgnoreCase))
                {
                    double voltage = TryGetBatteryVoltage(_activeCircuit, id, 9.0);
                    if (telemetry.Signals.TryGetValue($"SRC:{id}:V", out var srcV))
                    {
                        voltage = srcV;
                    }
                    if (TryGetPinVoltageDelta(_activeCircuit, telemetry, id, "+", "-", out var vDiff))
                    {
                        voltage = vDiff;
                    }
                    if (telemetry.Signals.TryGetValue($"SRC:{id}:I", out var current))
                    {
                        double power = voltage * current;
                        double soc = telemetry.Signals.TryGetValue($"SRC:{id}:SOC", out var socVal) ? socVal : double.NaN;
                        double rint = telemetry.Signals.TryGetValue($"SRC:{id}:RINT", out var rintVal) ? rintVal : double.NaN;
                        string socText = double.IsNaN(soc) ? "SOC:N/A" : $"SOC:{soc * 100.0:F0}%";
                        string rintText = double.IsNaN(rint) ? "Rint:N/A" : $"Rint:{rint:F2}Ω";
                        lbl.text = $"{voltage:F2}V {(current * 1000.0):F1}mA\nP:{Math.Abs(power) * 1000.0:F1}mW {socText} {rintText}";
                    }
                    else
                    {
                        lbl.text = $"{voltage:F2}V";
                    }
                    lbl.style.color = Color.white;
                }
                else
                {
                    if (telemetry.Signals.TryGetValue($"COMP:{id}:I", out var current))
                    {
                        double vDiff = telemetry.Signals.TryGetValue($"COMP:{id}:V", out var v) ? v : 0.0;
                        double power = telemetry.Signals.TryGetValue($"COMP:{id}:P", out var pwr) ? pwr : Math.Abs(current * vDiff);
                        double tempC = telemetry.Signals.TryGetValue($"COMP:{id}:T", out var t) ? t : double.NaN;
                        string tempText = double.IsNaN(tempC) ? "T:N/A" : $"T:{tempC:F1}C";
                        lbl.text = $"I:{current * 1000.0f:F1}mA V:{Math.Abs(vDiff):F2}V\nP:{power * 1000.0:F1}mW {tempText}";
                        lbl.style.color = Color.white;
                    }
                    else
                    {
                        lbl.text = "OK";
                        lbl.style.color = Color.white;
                    }
                }
            }
        }

        private static bool IsArduinoType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return false;
            return string.Equals(type, "ArduinoUno", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "ArduinoNano", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "ArduinoProMini", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetFirmwareLabel(ComponentSpec comp, out string label)
        {
            label = "FW: none";
            if (comp == null || comp.Properties == null) return false;

            if (comp.Properties.TryGetValue("bvmPath", out var bvmPath) && !string.IsNullOrWhiteSpace(bvmPath))
            {
                label = $"FW: {Path.GetFileName(bvmPath)}";
                return true;
            }

            if (comp.Properties.TryGetValue("firmwarePath", out var hexPath) && !string.IsNullOrWhiteSpace(hexPath))
            {
                label = $"FW: {Path.GetFileName(hexPath)}";
                return true;
            }

            if (comp.Properties.TryGetValue("firmware", out var firmware) &&
                !string.IsNullOrWhiteSpace(firmware) &&
                firmware.EndsWith(".hex", System.StringComparison.OrdinalIgnoreCase))
            {
                label = $"FW: {Path.GetFileName(firmware)}";
                return true;
            }

            return false;
        }

        private static bool TryGetPrimaryArduinoPin(ComponentSpec comp, out string pin)
        {
            pin = "D13";
            if (comp?.Properties == null) return true;
            if (comp.Properties.TryGetValue("virtualFirmware", out var fw) &&
                !string.IsNullOrWhiteSpace(fw) &&
                fw.StartsWith("blink:", System.StringComparison.OrdinalIgnoreCase))
            {
                var parts = fw.Split(':');
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    pin = parts[1];
                }
            }
            return true;
        }

        private static bool TryGetPinVoltage(ComponentSpec comp, string pin, TelemetryFrame telemetry, out double voltage)
        {
            voltage = 0;
            if (comp == null || telemetry == null) return false;
            if (!TryGetNetId(comp, pin, telemetry, out var netId)) return false;
            return telemetry.Signals.TryGetValue($"NET:{netId}", out voltage);
        }

        private static bool TryGetPinVoltageDelta(CircuitSpec circuit, TelemetryFrame telemetry, string compId, string pinA, string pinB, out double vDiff)
        {
            vDiff = 0;
            if (circuit == null || telemetry == null) return false;
            if (!TryGetNetId(circuit, compId, pinA, out var netA)) return false;
            if (!TryGetNetId(circuit, compId, pinB, out var netB)) return false;
            if (!telemetry.Signals.TryGetValue($"NET:{netA}", out var vA)) return false;
            if (!telemetry.Signals.TryGetValue($"NET:{netB}", out var vB)) return false;
            vDiff = vA - vB;
            return true;
        }

        private static bool TryGetNetId(ComponentSpec comp, string pin, TelemetryFrame telemetry, out string netId)
        {
            netId = null;
            if (comp == null || pin == null || telemetry == null) return false;
            return TryGetNetId(SessionManager.Instance?.CurrentCircuit, comp.Id, pin, out netId);
        }

        private static bool TryGetNetId(CircuitSpec circuit, string compId, string pin, out string netId)
        {
            netId = null;
            if (circuit?.Nets == null || string.IsNullOrWhiteSpace(compId) || string.IsNullOrWhiteSpace(pin)) return false;
            string nodeKey = $"{compId}.{pin}";
            foreach (var net in circuit.Nets)
            {
                if (net?.Nodes == null) continue;
                if (net.Nodes.Any(node => string.Equals(node, nodeKey, System.StringComparison.OrdinalIgnoreCase)))
                {
                    netId = net.Id;
                    return !string.IsNullOrWhiteSpace(netId);
                }
            }
            return false;
        }

        private static double TryGetLedMaxCurrent(CircuitSpec circuit, string compId, double fallback)
        {
            if (circuit?.Components == null) return fallback;
            var comp = circuit.Components.FirstOrDefault(c => c.Id == compId);
            if (comp?.Properties == null) return fallback;
            if (TryParseDouble(comp.Properties, "If_max", out var maxCurrent)) return maxCurrent;
            if (TryParseDouble(comp.Properties, "current", out maxCurrent)) return maxCurrent;
            return fallback;
        }

        private static double TryGetBatteryVoltage(CircuitSpec circuit, string compId, double fallback)
        {
            if (circuit?.Components == null) return fallback;
            var comp = circuit.Components.FirstOrDefault(c => c.Id == compId);
            if (comp?.Properties == null) return fallback;
            if (TryParseDouble(comp.Properties, "voltage", out var voltage)) return voltage;
            return fallback;
        }

        private static bool TryParseDouble(Dictionary<string, string> props, string key, out double value)
        {
            value = 0;
            if (props == null || string.IsNullOrWhiteSpace(key)) return false;
            if (!props.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return false;
            string cleaned = raw.Trim().ToLowerInvariant();
            double scale = 1.0;
            if (cleaned.EndsWith("ma"))
            {
                scale = 0.001;
                cleaned = cleaned.Substring(0, cleaned.Length - 2);
            }
            else if (cleaned.EndsWith("ua"))
            {
                scale = 0.000001;
                cleaned = cleaned.Substring(0, cleaned.Length - 2);
            }
            else if (cleaned.EndsWith("a"))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 1);
            }
            else if (cleaned.EndsWith("v"))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 1);
            }
            if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return false;
            value = parsed * scale;
            return true;
        }

        private void SaveInjectionConfig()
        {
            // MVP: minimal JSON dump of active keys
            var config = new Dictionary<string, string>();
            // foreach (var kvp in _activeWaveforms) config[kvp.Key] = kvp.Value.GetType().Name;

            string json = JsonUtility.ToJson(new SerializationWrapper { keys = config.Keys.ToList(), types = config.Values.ToList() }, true);
            File.WriteAllText(Path.Combine(_runOutputPath, "injection_config.json"), json);
        }

        [System.Serializable]
        private class SerializationWrapper { public List<string> keys; public List<string> types; }
    }
}
