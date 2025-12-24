using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using System.Collections.Generic;
using RobotTwin.CoreSim.Runtime;
using RobotTwin.CoreSim.Specs;
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

        // State
        private RunEngine _engine;
        private SimulationRecorder _recorder;
        private bool _isRunning = false;
        private string _runOutputPath;
        
        // Active Waveforms
        private Dictionary<string, IWaveform> _activeWaveforms = new Dictionary<string, IWaveform>();

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            var root = _doc.rootVisualElement;

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

            root.Q<Button>("StopButton")?.RegisterCallback<ClickEvent>(OnStopClicked);
            _addSignalBtn?.RegisterCallback<ClickEvent>(OnAddSignal);

            StartSimulation();
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

            _engine.Bus.OnEvent += OnSimulationEvent;
            _engine.Bus.OnFrame += _recorder.RecordFrame;

            _isRunning = true;
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
        }

        private void OnAddSignal(ClickEvent evt)
        {
            string name = _newSignalName.value;
            string type = _waveformType.value;
            double p1 = _param1.value;
            double p2 = _param2.value;
            double p3 = _param3.value;

            IWaveform wf = null;
            switch(type)
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

        private void SaveInjectionConfig()
        {
            // MVP: minimal JSON dump of active keys
            var config = new Dictionary<string, string>();
            foreach(var kvp in _activeWaveforms) config[kvp.Key] = kvp.Value.GetType().Name;
            
            string json = JsonUtility.ToJson(new SerializationWrapper { keys = config.Keys.ToList(), types = config.Values.ToList() }, true);
            File.WriteAllText(Path.Combine(_runOutputPath, "injection_config.json"), json);
        }
        
        [System.Serializable]
        private class SerializationWrapper { public List<string> keys; public List<string> types; }
    }
}
