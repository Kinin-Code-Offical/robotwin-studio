using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using System.Collections.Generic;
using RobotTwin.CoreSim.Runtime;
using RobotTwin.CoreSim.Specs;
using RobotTwin.Game; // for SessionManager

namespace RobotTwin.UI
{
    public class RunModeController : MonoBehaviour
    {
        private UIDocument _doc;
        private Label _timeLabel;
        private Label _tickLabel;
        private Label _logContentLabel;
        private Label _pathLabel;
        
        // Injection UI
        private TextField _signalNameField;
        private FloatField _signalValueField;
        private Toggle _injectionActiveToggle;

        // CoreSim Engine
        private RunEngine _engine;
        private SimulationRecorder _recorder;
        private bool _isRunning = false;
        private string _runOutputPath;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            var root = _doc.rootVisualElement;

            _timeLabel = root.Q<Label>("TimeLabel");
            _tickLabel = root.Q<Label>("TickLabel");
            _logContentLabel = root.Q<Label>("LogContentLabel");
            _pathLabel = root.Q<Label>("TelemetryPathLabel");
            
            _signalNameField = root.Q<TextField>("SignalNameField");
            _signalValueField = root.Q<FloatField>("SignalValueField");
            _injectionActiveToggle = root.Q<Toggle>("InjectionActiveToggle");

            root.Q<Button>("StopButton")?.RegisterCallback<ClickEvent>(OnStopClicked);

            StartSimulation();
        }

        private void OnDisable()
        {
            StopSimulation();
        }

        private void StartSimulation()
        {
            if (SessionManager.Instance == null || SessionManager.Instance.CurrentCircuit == null)
            {
                Debug.LogError("No Session/Circuit found!");
                return;
            }

            // Create Run Folder
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _runOutputPath = Path.Combine(Application.persistentDataPath, "Runs", timestamp);
            Directory.CreateDirectory(_runOutputPath);
            if (_pathLabel != null) _pathLabel.text = $"Log Path: {_runOutputPath}";

            // Init Engine & Recorder
            _engine = new RunEngine(SessionManager.Instance.CurrentCircuit);
            _recorder = new SimulationRecorder(_runOutputPath);

            // Hook Bus to UI and Recorder
            _engine.Bus.OnEvent += OnSimulationEvent;
            _engine.Bus.OnFrame += _recorder.RecordFrame; // Record every frame

            _isRunning = true;
            Debug.Log($"RunMode started. Logs: {_runOutputPath}");
        }

        private void StopSimulation()
        {
            if (!_isRunning) return;

            _isRunning = false;
            if (_recorder != null)
            {
                _recorder.Flush();
                _recorder.Dispose();
                _recorder = null;
            }
            if (_engine != null)
            {
                _engine.Bus.OnEvent -= OnSimulationEvent;
                _engine = null;
            }
        }

        private void FixedUpdate()
        {
            if (!_isRunning || _engine == null) return;

            // Gather Inputs (Stub)
            Dictionary<string, double>? inputs = null;
            if (_injectionActiveToggle != null && _injectionActiveToggle.value)
            {
                inputs = new Dictionary<string, double>();
                string name = _signalNameField.value;
                if (!string.IsNullOrEmpty(name))
                {
                    inputs[name] = _signalValueField.value;
                }
            }

            // Step
            _engine.Step(inputs);

            // Record (Manual hook since we own the recorder)
            // Ideally engine bus events trigger recorder, but here we can just do it or let the event handler do it if we passed the frame
            // But engine only passes frame to bus. 
            // Let's hook Recorder to Bus in StartSimulation.
        }

        private void OnSimulationEvent(EventLogEntry entry)
        {
            if (_recorder != null) _recorder.RecordEvent(entry);
            AppendLogToUI($"[{entry.TimeSeconds:F2}] {entry.Code}: {entry.Message}");
        }

        private void AppendLogToUI(string text)
        {
            if (_logContentLabel == null) return;
            _logContentLabel.text += text + "\n";
        }

        private void Update()
        {
             if (!_isRunning || _engine == null) return;

             // Update HUD (Telemetry Frame is volatile, we just grab current session state for UI)
             if (_timeLabel != null) _timeLabel.text = $"Time: {_engine.Session.TimeSeconds:F2}s";
             if (_tickLabel != null) _tickLabel.text = $"Tick: {_engine.Session.TickIndex}";
        }
    }
}
