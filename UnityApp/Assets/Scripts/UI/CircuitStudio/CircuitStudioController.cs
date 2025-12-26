using UnityEngine;
using UnityEngine.UIElements;
using RobotTwin.CoreSim.Specs;
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
        // private ComponentCatalog _catalog; // Removed Legacy Catalog

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null)
            {
                Debug.LogError("[CircuitStudioController] No UIDocument found on GameObject!");
                return;
            }

            // FALLBACK: Load from Resources if visualTreeAsset is lost
            if (_doc.visualTreeAsset == null)
            {
                Debug.LogWarning("[CircuitStudioController] VisualTreeAsset missing! Attempting Resource Fallback...");
                // Note: File is at Resources/UI/CircuitStudio.uxml, so path is "UI/CircuitStudio"
                var uxml = Resources.Load<VisualTreeAsset>("UI/CircuitStudio");
                if (uxml != null)
                {
                    _doc.visualTreeAsset = uxml;
                    // Force a re-init of the UI logic inside UIDocument if needed, 
                    // though setting it might not trigger immediate rebuild in OnEnable if the cycle is already running.
                }
                else
                {
                    Debug.LogError("[CircuitStudioController] Resource Fallback Failed! 'Resources/UI/CircuitStudio' not found.");
                }
            }

            // Ensure the tree is cloned
            if (_doc.visualTreeAsset != null && _doc.rootVisualElement == null)
            {
                // This forces the UIDocument to instantiate the tree immediately
                _doc.enabled = false;
                _doc.enabled = true; 
            }

            var root = _doc.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[CircuitStudioController] RootVisualElement is NULL! Check UXML/USS or Fallback.");
                return;
            }

            // UXML Binding (Updated Step 183 names)
            // LeftPanel -> LibraryList
            _paletteContainer = root.Q("LibraryList"); 
            
            // Center -> Canvas
            _canvasContainer = root.Q("Canvas");
            
            // RightPanel -> InspectorContent (Not used for generic palette yet, but ok)
            // _statusLabel = ... (Not in current UXML 183, skipping/optional)

            // RightPanel -> SimulateBtn
            root.Q<Button>("SimulateBtn")?.RegisterCallback<ClickEvent>(OnRun);

            // Other internal buttons if present (Backwards compat with older UXML versions if user didn't update)
            // But based on last seen UXML (Step 183), only SimulateBtn exists.
            // Let's add safeguards.

            InitializeSession();
            PopulatePalette();
        }

        private void InitializeSession()
        {
            if (SessionManager.Instance != null && SessionManager.Instance.CurrentCircuit != null)
            {
                _currentCircuit = SessionManager.Instance.CurrentCircuit;
                Debug.Log($"[CircuitStudio] Loaded session: {_currentCircuit.Id}");
            }
            else
            {
                _currentCircuit = new CircuitSpec { Id = "New Circuit" };
                Debug.Log("[CircuitStudio] Created new session.");
            }

            // if (_currentCircuit == null) _currentCircuit = new CircuitSpec { Id = "New Circuit" };
            
            RefreshCanvas();
        }

        private void PopulatePalette()
        {
            if (_paletteContainer == null) return;
            _paletteContainer.Clear();

            // STOCK COMPONENTS (Hardcoded for MVP Phase 1)
            var stocks = new List<string> { "ArduinoUno", "LED", "Resistor" };

            foreach (var type in stocks)
            {
                var btn = new Button(() => AddComponent(type));
                btn.text = type;
                btn.style.height = 30;
                btn.style.marginBottom = 5;
                _paletteContainer.Add(btn);
            }
        }

        private void AddComponent(string type)
        {
            var spec = new ComponentSpec
            {
                Type = type,
                Id = $"{type}_{_currentCircuit.Components.Count + 1}"
            };
            _currentCircuit.Components.Add(spec);
            Debug.Log($"[CircuitStudio] Added {spec.Id}");
            RefreshCanvas();
        }

        private void RefreshCanvas()
        {
            if (_canvasContainer != null)
            {
                _canvasContainer.Clear();
                foreach (var c in _currentCircuit.Components)
                {
                    var lbl = new Label($"{c.Id}");
                    lbl.style.color = Color.white;
                    lbl.style.position = Position.Absolute;
                    lbl.style.left = Random.Range(50, 400); // MVP Random Scatter
                    lbl.style.top = Random.Range(50, 400);
                    lbl.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
                    lbl.style.paddingLeft = 10;
                    lbl.style.paddingRight = 10;
                    lbl.style.paddingTop = 10;
                    lbl.style.paddingBottom = 10;
                    _canvasContainer.Add(lbl);
                }
            }
        }

        private void OnRun(ClickEvent evt)
        {
            Debug.Log("[CircuitStudio] Starting Simulation...");
            if (SessionManager.Instance != null)
            {
                SessionManager.Instance.StartSession(_currentCircuit);
            }
            UnityEngine.SceneManagement.SceneManager.LoadScene(2); // RunMode
        }
    }
}
