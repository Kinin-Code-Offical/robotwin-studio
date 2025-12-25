using UnityEngine;
using UnityEngine.UIElements;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Validation;
using RobotTwin.CoreSim.Catalogs;
using RobotTwin.Game;
using System.Collections.Generic;
using System.IO;

namespace RobotTwin.UI
{
    public class CircuitStudioController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _paletteContainer;
        private VisualElement _canvasContainer;
        private Label _statusLabel;
        
        // Connections UI
        private DropdownField _fromCompDropdown;
        private DropdownField _toCompDropdown;
        private TextField _fromPinField;
        private TextField _toPinField;
        private Label _connectionListLabel;

        private CircuitSpec _currentCircuit;
        private ComponentCatalog _catalog;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) return;
            var root = _doc.rootVisualElement;

            _paletteContainer = root.Q("PaletteContainer");
            _canvasContainer = root.Q("CanvasContainer");
            _statusLabel = root.Q<Label>("StatusLabel");
            // Optional: Bind header status too if needed
            // var headerStatus = root.Q<Label>("StatusBarLabel");
            
            _fromCompDropdown = root.Q<DropdownField>("FromCompDropdown");
            _toCompDropdown = root.Q<DropdownField>("ToCompDropdown");
            _fromPinField = root.Q<TextField>("FromPinField");
            _toPinField = root.Q<TextField>("ToPinField");
            _connectionListLabel = root.Q<Label>("ConnectionListLabel");

            root.Q<Button>("InternalSaveBtn")?.RegisterCallback<ClickEvent>(OnSave);
            root.Q<Button>("InternalLoadBtn")?.RegisterCallback<ClickEvent>(OnLoad);
            root.Q<Button>("InternalValidateBtn")?.RegisterCallback<ClickEvent>(OnValidateClicked);
            root.Q<Button>("BackBtn")?.RegisterCallback<ClickEvent>(OnBack);
            root.Q<Button>("ConnectBtn")?.RegisterCallback<ClickEvent>(OnConnect);
            root.Q<Button>("RunBtn")?.RegisterCallback<ClickEvent>(OnRun); // Added Run Btn

            InitializeSession();
            PopulatePalette();
        }

        private void InitializeSession()
        {
            // Try to get from SessionManager
            if (SessionManager.Instance != null && SessionManager.Instance.CurrentCircuit != null)
            {
                _currentCircuit = SessionManager.Instance.CurrentCircuit;
                UpdateStatus($"Loaded session: {_currentCircuit.Name}");
            }
            else
            {
                _currentCircuit = new CircuitSpec { Name = "New Circuit" };
                UpdateStatus("Session/CurrentCircuit null, created new.");
            }

            _catalog = new ComponentCatalog(); 
            _catalog.Components = ComponentCatalog.GetDefaults();
            
            RefreshCanvas();
        }

        private void PopulatePalette()
        {
            if (_paletteContainer == null) return;
            _paletteContainer.Clear();

            foreach (var stock in _catalog.Components)
            {
                var btn = new Button(() => AddComponent(stock));
                btn.text = stock.Name;
                _paletteContainer.Add(btn);
            }
        }

        private void AddComponent(ComponentDefinition template)
        {
            var inst = new ComponentInstance
            {
                CatalogID = template.ID,
                InstanceID = $"{template.ID}_{_currentCircuit.Components.Count + 1}",
                X = 0, Y = 0
            };
            _currentCircuit.Components.Add(inst);
            UpdateStatus($"Added {inst.InstanceID}");
            RefreshCanvas();
        }

        private void RefreshCanvas()
        {
            // Canvas
            if (_canvasContainer != null)
            {
                _canvasContainer.Clear();
                foreach (var c in _currentCircuit.Components)
                {
                    var lbl = new Label($"{c.InstanceID} ({c.CatalogID})");
                    lbl.style.borderLeftWidth = 1;
                    lbl.style.borderRightWidth = 1;
                    lbl.style.borderTopWidth = 1;
                    lbl.style.borderBottomWidth = 1;
                    lbl.style.borderLeftColor = Color.gray;
                    lbl.style.borderRightColor = Color.gray;
                    lbl.style.borderTopColor = Color.gray;
                    lbl.style.borderBottomColor = Color.gray;
                    lbl.style.paddingTop = 5;
                    lbl.style.paddingBottom = 5;
                    lbl.style.marginBottom = 2;
                    _canvasContainer.Add(lbl);
                }
            }

            // Dropdowns
            var choices = new List<string>();
            foreach (var c in _currentCircuit.Components) choices.Add(c.InstanceID);
            
            if (_fromCompDropdown != null) _fromCompDropdown.choices = choices;
            if (_toCompDropdown != null) _toCompDropdown.choices = choices;

            // Connections List
            if (_connectionListLabel != null)
            {
                if (_currentCircuit.Connections == null || _currentCircuit.Connections.Count == 0)
                {
                    _connectionListLabel.text = "No connections.";
                }
                else
                {
                    var lines = new List<string>();
                    foreach(var conn in _currentCircuit.Connections)
                    {
                        lines.Add($"{conn.FromComponentID}:{conn.FromPin} -> {conn.ToComponentID}:{conn.ToPin}");
                    }
                    _connectionListLabel.text = string.Join("\n", lines);
                }
            }
        }

        private void OnConnect(ClickEvent evt)
        {
            string fromID = _fromCompDropdown.value;
            string toID = _toCompDropdown.value;
            string fromPin = _fromPinField.value;
            string toPin = _toPinField.value;

            if (string.IsNullOrEmpty(fromID) || string.IsNullOrEmpty(toID))
            {
                UpdateStatus("Select components properly.");
                return;
            }

            var conn = new Connection 
            { 
                FromComponentID = fromID, 
                FromPin = fromPin, 
                ToComponentID = toID, 
                ToPin = toPin 
            };
            
            if (_currentCircuit.Connections == null) _currentCircuit.Connections = new List<Connection>();
            _currentCircuit.Connections.Add(conn);
            RefreshCanvas();
            UpdateStatus("Connection added.");
        }

        private void OnSave(ClickEvent evt)
        {
            string json = JsonUtility.ToJson(_currentCircuit, true);
            string path = Path.Combine(Application.persistentDataPath, "circuit_mvp.json");
            File.WriteAllText(path, json);
            UpdateStatus($"Saved to {path}");
            Debug.Log($"Circuit Saved: {json}");
        }

         private void OnLoad(ClickEvent evt)
        {
            string path = Path.Combine(Application.persistentDataPath, "circuit_mvp.json");
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                _currentCircuit = JsonUtility.FromJson<CircuitSpec>(json);
                RefreshCanvas();
                UpdateStatus($"Loaded from {path}");
            }
            else
            {
                UpdateStatus("No save file found.");
            }
        }

        private void OnValidateClicked(ClickEvent evt)
        {
            var result = CircuitValidator.Validate(_currentCircuit);
            string msg = result.IsValid ? "Valid" : "Invalid!";
            if (result.Errors.Count > 0) msg += " Errors: " + string.Join("; ", result.Errors);
            if (result.Warnings.Count > 0) msg += " Warnings: " + string.Join("; ", result.Warnings);
            UpdateStatus(msg);
        }

        private void OnBack(ClickEvent evt)
        {
             UnityEngine.SceneManagement.SceneManager.LoadScene(0); // Wizard
        }

        private void OnRun(ClickEvent evt)
        {
            // Validate first? MVP: Just go.
            UnityEngine.SceneManagement.SceneManager.LoadScene(2); // Index 2 = RunMode
        }

        private void UpdateStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
            Debug.Log($"[CircuitStudio] {msg}");
        }
    }
}
