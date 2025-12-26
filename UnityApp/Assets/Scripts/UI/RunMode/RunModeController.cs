using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using System.Collections.Generic;
using RobotTwin.CoreSim.Runtime;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Models.Physics;
using RobotTwin.CoreSim.Models.Power;
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
        private RunEngine _engine;
        private SimulationRecorder _recorder;
        private bool _isRunning = false;
        private string _runOutputPath;

        // Physics Models
        private BatteryModel _battery;
        private ThermalModel _thermal;

        // Active Waveforms
        private Dictionary<string, IWaveform> _activeWaveforms = new Dictionary<string, IWaveform>();

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

            if (_addSignalBtn == null) Debug.LogWarning("[RunModeController] 'AddSignalBtn' not found.");

            root.Q<Button>("StopButton")?.RegisterCallback<ClickEvent>(OnStopClicked);
            root.Q<Button>("OpenLogsBtn")?.RegisterCallback<ClickEvent>(OnOpenLogsClicked);
            _addSignalBtn?.RegisterCallback<ClickEvent>(OnAddSignal);

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

            _engine = new RunEngine(SessionManager.Instance.CurrentCircuit);
            _recorder = new SimulationRecorder(_runOutputPath);

            _recorder.Attach(_engine.Bus);
            _engine.Bus.OnEvent += OnSimulationEvent; // Keep local subscription for HUD logging

            _isRunning = true;
            // Model Init
            _battery = new BatteryModel(2200.0, 11.1); // 3S LiPo Default
            _thermal = new ThermalModel { AmbientTempC = 25.0 };

            Debug.Log($"RunMode started. Logs: {_runOutputPath}");

            // Default Injection (Example)
            AddWaveform("default_sine", new SineWaveform(1.0, 5.0, 0.0));
        }

        private void StopSimulation()
        {
            if (!_isRunning) return;
            _isRunning = false;

            // Persist Config
            SaveInjectionConfig();

            _recorder?.Flush();
            _recorder?.Dispose();
            _recorder = null;
            if (_engine != null)
            {
                _engine.Bus.OnEvent -= OnSimulationEvent;
                _engine = null;
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
            if (!_isRunning || _engine == null) return;

            Dictionary<string, double> inputs = null;
            if (_injectionActiveToggle != null && _injectionActiveToggle.value && _activeWaveforms.Count > 0)
            {
                inputs = new Dictionary<string, double>();
                double t = _engine.Session.TimeSeconds;
                foreach (var kvp in _activeWaveforms)
                {
                    inputs[kvp.Key] = kvp.Value.Sample(t);
                }
            }

            _engine.Step(inputs);

            // Step Physics
            if (_battery != null)
            {
                // Fake Load current for MVP: 0.5A constant + sine wave noise
                double load = 0.5 + (Mathf.Sin((float)_engine.Session.TimeSeconds) * 0.1f);
                _battery.Drain(load, 0.02); // Fixed timestep assumed 20ms
            }

            if (_thermal != null)
            {
                // Fake Current for Heat: 1.0A
                _thermal.Update(1.0, 0.02);
            }
        }

        private void OnSimulationEvent(EventLogEntry entry)
        {
            _recorder?.RecordEvent(entry);
            AppendLog($"[{entry.TimeSeconds:F2}] {entry.Code}: {entry.Message}");
        }

        private void AppendLog(string text)
        {
            if (_logContentLabel != null) _logContentLabel.text = (text + "\n" + _logContentLabel.text).Substring(0, Mathf.Min(_logContentLabel.text.Length + text.Length + 1, 2000));
        }

        private void Update()
        {
            if (!_isRunning || _engine == null) return;
            if (_timeLabel != null) _timeLabel.text = $"Time: {_engine.Session.TimeSeconds:F2}s";
            if (_tickLabel != null) _tickLabel.text = $"Tick: {_engine.Session.TickIndex}";

            UpdateVisualization();
            UpdateTelemetry();
        }

        private void UpdateTelemetry()
        {
            if (_battery != null && _batteryBar != null)
            {
                // Map range 0-100% capacity
                double soc = (_battery.RemainingmAh / _battery.CapacitymAh) * 100.0;
                _batteryBar.value = (float)soc;
                _batteryBar.title = $"{soc:F1}% ({_battery.GetVoltage(0.5):F2}V)";
            }

            if (_thermal != null && _tempBar != null)
            {
                // Normalize for visual (0 to 100 range, where 100 is overheating)
                _tempBar.value = (float)_thermal.CurrentTempC;
                _tempBar.title = $"{_thermal.CurrentTempC:F1}°C";
                
                if (_thermal.CurrentTempC > 80) _tempBar.style.color = Color.red;
                else _tempBar.style.color = Color.white;
            }
        }

        private void OnAddSignal(ClickEvent evt)
        {
            string name = _newSignalName.value;
            string type = _waveformType.value;
            double p1 = _param1.value;
            double p2 = _param2.value;
            double p3 = _param3.value;

            IWaveform wf = null;
            switch (type)
            {
                case "Constant": wf = new ConstantWaveform(p1); break;
                case "Step": wf = new StepWaveform(p1, p2, p3); break; // Init, Final, Time
                case "Ramp": wf = new RampWaveform(0, p2, p1, p3); break; // St, EndT, StVal, EndVal (Packed weirdly for MVP)
                case "Sine": wf = new SineWaveform(p2, p1, p3); break; // Freq, Amp, Offset
                default: wf = new ConstantWaveform(0); break;
            }

            AddWaveform(name, wf);
        }

        private void AddWaveform(string name, IWaveform wf)
        {
            _activeWaveforms[name] = wf;
            RefreshSignalList();
        }



        private void RefreshSignalList()
        {
            if (_signalList == null) return;
            _signalList.Clear();
            foreach (var kvp in _activeWaveforms)
            {
                var row = new Label($"{kvp.Key} ({kvp.Value.GetType().Name})");
                row.style.color = Color.cyan;
                _signalList.Add(row);
            }
        }

        // --- Visualization Logic ---
        private ScrollView _componentList;
        private Dictionary<string, Label> _componentLabels = new Dictionary<string, Label>();

        private void InitVisualization(VisualElement root)
        {
            _componentList = root.Q<ScrollView>("ComponentList");
            if (_componentList == null) return;

            _componentList.Clear();
            _componentLabels.Clear();

            foreach (var comp in _engine.Circuit.Components)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.SpaceBetween;

                var nameLbl = new Label($"{comp.InstanceID} ({comp.CatalogID})");
                nameLbl.style.color = Color.white;

                var statusLbl = new Label("OFF");
                statusLbl.style.color = Color.gray;
                statusLbl.style.unityFontStyleAndWeight = FontStyle.Bold;

                row.Add(nameLbl);
                row.Add(statusLbl);
                _componentList.Add(row);

                _componentLabels[comp.InstanceID] = statusLbl;
            }
        }

        private void UpdateVisualization()
        {
            if (_engine == null) return;

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
                    bool isOn = (_engine.Session.TimeSeconds % 1.0) < 0.5; // Simulate 1Hz blink
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
            foreach (var kvp in _activeWaveforms) config[kvp.Key] = kvp.Value.GetType().Name;

            string json = JsonUtility.ToJson(new SerializationWrapper { keys = config.Keys.ToList(), types = config.Values.ToList() }, true);
            File.WriteAllText(Path.Combine(_runOutputPath, "injection_config.json"), json);
        }

        [System.Serializable]
        private class SerializationWrapper { public List<string> keys; public List<string> types; }
    }
}
