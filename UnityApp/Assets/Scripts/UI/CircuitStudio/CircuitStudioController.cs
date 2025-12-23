using UnityEngine;
using UnityEngine.UIElements;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Validation;
using RobotTwin.CoreSim.Catalogs;
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

            root.Q<Button>("InternalSaveBtn")?.RegisterCallback<ClickEvent>(OnSave);
            root.Q<Button>("InternalValidateBtn")?.RegisterCallback<ClickEvent>(OnValidate);
            root.Q<Button>("BackBtn")?.RegisterCallback<ClickEvent>(OnBack);

            InitializeSession();
            PopulatePalette();
        }

        private void InitializeSession()
        {
            // For MVP, create a new blank circuit or load from session
            _currentCircuit = new CircuitSpec { Name = "New Circuit" };
            _catalog = new ComponentCatalog(); 
            
            // Use defaults from CoreSim
            _catalog.Components = ComponentCatalog.GetDefaults();
            
            UpdateStatus("Session Ready.");
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
            if (_canvasContainer == null) return;
            _canvasContainer.Clear();
            foreach (var c in _currentCircuit.Components)
            {
                var lbl = new Label($"{c.InstanceID} ({c.CatalogID})");
                lbl.style.borderWidth = 1;
                lbl.style.borderColor = Color.gray;
                lbl.style.paddingTop = 5;
                lbl.style.paddingBottom = 5;
                lbl.style.marginBottom = 2;
                _canvasContainer.Add(lbl);
            }
        }

        private void OnSave(ClickEvent evt)
        {
            // Serialize
            // var json = RobotTwin.CoreSim.Serialization.SimulationSerializer.Serialize(_currentCircuit); // If implemented
            // Fallback for MVP
            string json = JsonUtility.ToJson(_currentCircuit, true);
            string path = Path.Combine(Application.persistentDataPath, "circuit_mvp.json");
            File.WriteAllText(path, json);
            UpdateStatus($"Saved to {path}");
            Debug.Log($"Circuit Saved: {json}");
        }

        private void OnValidate(ClickEvent evt)
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

        private void UpdateStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
            Debug.Log($"[CircuitStudio] {msg}");
        }
    }
}
