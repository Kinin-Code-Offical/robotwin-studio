using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using System.Collections.Generic;
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
        private RobotTwin.Game.SimHost _host;
        private RobotTwin.CoreSim.FirmwareClient _client;
        private CircuitSpec _activeCircuit;
        
        private bool _isRunning = false;
        private string _runOutputPath;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) { enabled = false; return; }
            var root = _doc.rootVisualElement;
            if (root == null) { enabled = false; return; }

            // Bind UI
            _timeLabel = root.Q<Label>("TimeLabel");
            _tickLabel = root.Q<Label>("TickLabel");
            _logContentLabel = root.Q<Label>("LogContentLabel");
            _serialContentLabel = root.Q<Label>("SerialContentLabel");
            _pathLabel = root.Q<Label>("TelemetryPathLabel");
            
            // Buttons
             root.Q<Button>("StopButton")?.RegisterCallback<ClickEvent>(OnStopClicked);
             root.Q<Button>("OpenLogsBtn")?.RegisterCallback<ClickEvent>(OnOpenLogsClicked);

             InitVisualization(root);
             StartSimulation(); // Start Loop
        }

        private void OnDisable() => StopSimulation();

        private void StartSimulation()
        {
             if (SessionManager.Instance == null || SessionManager.Instance.CurrentCircuit == null)
            {
                Debug.LogError("[RunMode] No Session/Circuit found!");
                return;
            }
            
            _activeCircuit = SessionManager.Instance.CurrentCircuit;

            // Ensure FirmwareClient exists
            if (RobotTwin.CoreSim.FirmwareClient.Instance == null)
            {
                var go = new GameObject("FirmwareHost");
                _client = go.AddComponent<RobotTwin.CoreSim.FirmwareClient>();
            }
            else _client = RobotTwin.CoreSim.FirmwareClient.Instance;

            // Ensure SimHost exists
            if (RobotTwin.Game.SimHost.Instance == null)
            {
                 var go = _client.gameObject; // Click on same obj
                 _host = go.AddComponent<RobotTwin.Game.SimHost>();
            }
            else _host = RobotTwin.Game.SimHost.Instance;

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
            _isRunning = false;
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

            // Native Integration Verification (Ohm's Law)
            if (Time.frameCount % 120 == 0) // Every 2s roughly
            {
                float v = 5.0f;
                float r = 220.0f;
                float i = RobotTwin.Core.NativeBridge.CalculateCurrent(v, r);
                Debug.Log($"[NativeEngine] 5V / 220R = {i * 1000f:F2}mA");
            }

            if (_timeLabel != null) _timeLabel.text = $"Time: {Time.time:F2}s"; // _host.SimTime in simple mode
            if (_tickLabel != null) _tickLabel.text = "Running";

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
        private Dictionary<string, string> _componentTypes = new Dictionary<string, string>();

        private void InitVisualization(VisualElement root)
        {
            _componentList = root.Q<ScrollView>("ComponentList");
            if (_componentList == null || _activeCircuit == null) return;

            _componentList.Clear();
            _componentLabels.Clear();
            _componentTypes.Clear();

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
                _componentTypes[comp.Id] = comp.Type;
            }
        }

        private void UpdateVisualization()
        {
            if (_host == null) return;

            var telemetry = _host.LastTelemetry;
            if (telemetry == null)
            {
                // MVP: Simple net-based LED heuristic using CircuitSpec nets.
                foreach (var kvp in _componentLabels)
                {
                    string id = kvp.Key;
                    Label lbl = kvp.Value;

                    if (id.ToLower().Contains("led"))
                    {
                        bool isOn = IsLedDriven(id);
                        lbl.text = isOn ? "ON" : "OFF";
                        lbl.style.color = isOn ? Color.green : Color.gray;
                    }
                    else
                    {
                        lbl.text = "OK";
                        lbl.style.color = Color.white;
                    }
                }
                return;
            }

            foreach (var kvp in _componentLabels)
            {
                string id = kvp.Key;
                Label lbl = kvp.Value;
                _componentTypes.TryGetValue(id, out var type);

                if (string.Equals(type, "LED", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (telemetry.Signals.TryGetValue($"COMP:{id}:I", out var current))
                    {
                        bool isOn = current > 0.002;
                        lbl.text = isOn ? "ON" : "OFF";
                        lbl.style.color = isOn ? Color.green : Color.gray;
                    }
                    else
                    {
                        lbl.text = "OFF";
                        lbl.style.color = Color.gray;
                    }
                }
                else
                {
                    if (telemetry.Signals.TryGetValue($"COMP:{id}:I", out var current))
                    {
                        lbl.text = $"{current * 1000.0f:F1}mA";
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

        private bool IsLedDriven(string ledId)
        {
            if (_activeCircuit == null) return false;
            var nets = _activeCircuit.Nets ?? new List<NetSpec>();
            if (nets.Count == 0) return false;

            bool hasAnode = nets.Any(n => n.Nodes.Contains($"{ledId}.Anode") && n.Nodes.Any(node => node.EndsWith(".D13") || node.EndsWith(".VCC")));
            bool hasCathode = nets.Any(n => n.Nodes.Contains($"{ledId}.Cathode") && n.Nodes.Any(node => node.EndsWith(".GND")));

            if (!hasAnode || !hasCathode) return false;

            return (_host.SimTime % 1.0) < 0.5;
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
