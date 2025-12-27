using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using System.Collections.Generic;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Host;
using RobotTwin.CoreSim.IPC;
// using RobotTwin.CoreSim.Models.Physics; // Physics disabled for MVP rewrite
// using RobotTwin.CoreSim.Models.Power;
using RobotTwin.Game;
using System.Linq;

namespace RobotTwin.UI
{
    public class RunModeController : MonoBehaviour
    {
        private UIDocument _doc;
        private Label _timeLabel;
        private Label _tickLabel;
        private Label _logContentLabel;
        private Label _serialContentLabel;
        private Label _pathLabel;
        
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
        private ProgressBar _batteryBar;
        private ProgressBar _tempBar;

        // State
        private SimHost _host;
        private IFirmwareClient _client;
        private CircuitSpec _activeCircuit;
        
        private bool _isRunning = false;
        private string _runOutputPath;

        // Physics Models (Commented out for MVP)
        // private BatteryModel _battery;
        // private ThermalModel _thermal;

        // Active Waveforms
        // private Dictionary<string, IWaveform> _activeWaveforms = new Dictionary<string, IWaveform>();

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null)
            {
                Debug.LogError("[RunModeController] UIDocument component missing! Disabling.");
                enabled = false;
                return;
            }

            var root = _doc.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[RunModeController] RootVisualElement is null! Disabling.");
                enabled = false;
                return;
            }

            _timeLabel = root.Q<Label>("TimeLabel");
            _tickLabel = root.Q<Label>("TickLabel");
            _logContentLabel = root.Q<Label>("LogContentLabel");
            _serialContentLabel = root.Q<Label>("SerialContentLabel");
            _pathLabel = root.Q<Label>("TelemetryPathLabel");

            _newSignalName = root.Q<TextField>("NewSignalName");
            _waveformType = root.Q<DropdownField>("WaveformTypeDropdown");
            _param1 = root.Q<FloatField>("Param1");
            _param2 = root.Q<FloatField>("Param2");
            _param3 = root.Q<FloatField>("Param3");
            _addSignalBtn = root.Q<Button>("AddSignalBtn");
            _signalList = root.Q<ScrollView>("SignalList");
            _injectionActiveToggle = root.Q<Toggle>("InjectionActiveToggle");
            
            _batteryBar = root.Q<ProgressBar>("BatteryBar");
            _tempBar = root.Q<ProgressBar>("TempBar");

            // Validate critical controls
            if (_addSignalBtn == null) Debug.LogWarning("[RunModeController] 'AddSignalBtn' not found.");

            root.Q<Button>("StopButton")?.RegisterCallback<ClickEvent>(OnStopClicked);
            root.Q<Button>("OpenLogsBtn")?.RegisterCallback<ClickEvent>(OnOpenLogsClicked);
            // _addSignalBtn?.RegisterCallback<ClickEvent>(OnAddSignal); // Legacy Stubbed

            StartSimulation();
            InitVisualization(root);
        }

        private void OnDisable() => StopSimulation();

        private void StartSimulation()
        {
            if (SessionManager.Instance == null || SessionManager.Instance.CurrentCircuit == null)
            {
                Debug.LogError("No Session/Circuit found!");
                return;
            }

            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _runOutputPath = Path.Combine(Application.persistentDataPath, "Runs", timestamp);
            Directory.CreateDirectory(_runOutputPath);
            if (_pathLabel != null) _pathLabel.text = $"Log: {_runOutputPath}";

            // INIT HOST
            _client = new FirmwareClient();
            var circuit = SessionManager.Instance.CurrentCircuit;
            _activeCircuit = circuit;
            // Quick Fix: If circuit has no components, add blinky defaults for test
            if (circuit.Components.Count == 0)
            {
                circuit.Components.Add(new ComponentSpec { Id = "U1", Type = "ArduinoUno" });
                circuit.Components.Add(new ComponentSpec { Id = "D1", Type = "LED" });
            }

            _host = new SimHost(circuit, _client);
            if (!string.IsNullOrEmpty(_firmwarePath))
            {
                 _host.StartFirmwareProcess(_firmwarePath);
            }
            
            _host.OnTickComplete += HandleHostTick;
            _host.Start();

            _isRunning = true;
            Debug.Log($"RunMode started. Logs: {_runOutputPath}");

            // Default Injection (Example)
            // AddWaveform("default_sine", new SineWaveform(1.0, 5.0, 0.0));
        }

        private void StopSimulation()
        {
            if (!_isRunning) return;
            _isRunning = false;

            if (_host != null)
            {
                _host.OnTickComplete -= HandleHostTick;
                _host.Stop();
                _host = null;
            }
            if (_client != null)
            {
                _client.Disconnect();
                _client = null;
            }
        }

        private void OnStopClicked(ClickEvent evt)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(1); // Back to CircuitStudio
        }

        private void OnOpenLogsClicked(ClickEvent evt)
        {
            if (!string.IsNullOrEmpty(_runOutputPath))
            {
                Application.OpenURL(_runOutputPath);
            }
        }

        private void FixedUpdate()
        {
            // Polyglot Architecture: Delegate heavy physics/solving to C++ NativeEngine
            if (_isRunning)
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
            if (_logContentLabel != null) _logContentLabel.text = (text + "\n" + _logContentLabel.text).Substring(0, Mathf.Min(_logContentLabel.text.Length + text.Length + 1, 2000));
        }

        private void Update()
        {
            if (!_isRunning || _host == null) return;
            if (_timeLabel != null) _timeLabel.text = $"Time: {_host.SimTime:F2}s";
            if (_tickLabel != null) _tickLabel.text = $"Tick: {_host.TickCount}";

            UpdateVisualization();
            UpdateTelemetry();
        }

        private void UpdateTelemetry()
        {
            // Stubbed for MVP
            if (_batteryBar != null) _batteryBar.title = "N/A";
            if (_tempBar != null) _tempBar.title = "N/A";
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

        private void InitVisualization(VisualElement root)
        {
            _componentList = root.Q<ScrollView>("ComponentList");
            if (_componentList == null || _activeCircuit == null) return;

            _componentList.Clear();
            _componentLabels.Clear();

            foreach (var comp in _activeCircuit.Components)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.SpaceBetween;

                var nameLbl = new Label($"{comp.Id} ({comp.Type})");
                nameLbl.style.color = Color.white;

                var statusLbl = new Label("OFF");
                statusLbl.style.color = Color.gray;
                statusLbl.style.unityFontStyleAndWeight = FontStyle.Bold;

                row.Add(nameLbl);
                row.Add(statusLbl);
                _componentList.Add(row);

                _componentLabels[comp.Id] = statusLbl;
            }
        }

        private void UpdateVisualization()
        {
            if (_host == null) return;

            // MVP: Simple heuristic for LEDs (hardcoded for demo effect, real net lookup is todo in CoreSim)
            // Ideally: _engine.GetPinVoltage(compId, pinName)

            foreach (var kvp in _componentLabels)
            {
                string id = kvp.Key;
                Label lbl = kvp.Value;

                // Demo Logic: If it's an LED and we have active injection or time > 0, flicker it
                // This is a PLACEHOLDER for real net-list lookup
                if (id.ToLower().Contains("led"))
                {
                    bool isOn = (_host.SimTime % 1.0) < 0.5; // Simulate 1Hz blink
                    lbl.text = isOn ? "ON" : "OFF";
                    lbl.style.color = isOn ? Color.green : Color.gray;
                }
                else
                {
                    lbl.text = "OK";
                    lbl.style.color = Color.white;
                }
            }
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
