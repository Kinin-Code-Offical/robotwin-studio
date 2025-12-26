using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using RobotTwin.CoreSim.Specs;
using RobotTwin.Game;

namespace RobotTwin.UI
{
    public class CircuitStudioController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _libraryList;
        private VisualElement _canvas;
        private VisualElement _inspectorContent;

        private CircuitSpec _currentSpec;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) return;

            var root = _doc.rootVisualElement;

            _libraryList = root.Q<ScrollView>("LibraryList");
            _canvas = root.Q<VisualElement>("Canvas");
            _inspectorContent = root.Q<ScrollView>("InspectorContent");

            root.Q<Button>("SimulateBtn")?.RegisterCallback<ClickEvent>(OnSimulateClicked);

            LoadSession();
            PopulateLibrary();
        }

        private void LoadSession()
        {
            if (SessionManager.Instance != null && SessionManager.Instance.CurrentCircuit != null)
            {
                _currentSpec = SessionManager.Instance.CurrentCircuit;
            }
            else
            {
                // Fallback for isolated testing
                _currentSpec = new CircuitSpec { Name = "IsolatedCircuit" };
            }
        }

        private void PopulateLibrary()
        {
            if (_libraryList == null) return;
            _libraryList.Clear();

            var components = new List<string> { "Resistor", "LED", "Arduino Uno", "Battery (9V)", "Servo Motor" };

            foreach (var comp in components)
            {
                var btn = new Button();
                btn.text = comp;
                btn.style.height = 30;
                btn.style.marginBottom = 5;
                btn.RegisterCallback<ClickEvent>(evt => SpawnComponent(comp));
                _libraryList.Add(btn);
            }
        }

        private void SpawnComponent(string componentName)
        {
            Debug.Log($"Spawning Component: {componentName}");
            // MVP: Just add a VisualElement to the Canvas to represent it
            var item = new VisualElement();
            item.style.width = 100;
            item.style.height = 100;
            item.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
            item.style.borderWidth = 1;
            item.style.borderColor = Color.white;
            item.style.position = Position.Absolute;
            item.style.left = 100 + Random.Range(-20, 20);
            item.style.top = 100 + Random.Range(-20, 20);
            
            var label = new Label(componentName);
            label.style.color = Color.white;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            var label = new Label(componentName);
            label.style.color = Color.white;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            item.Add(label);

            // DATA BINDING: Add to Spec
            string catalogId = MapNameToId(componentName);
            string instanceId = $"{catalogId}_{_currentSpec.Components.Count + 1}";
            
            var instance = new ComponentInstance 
            { 
                InstanceID = instanceId, 
                CatalogID = catalogId,
                X = 100, Y = 100 
            };
            _currentSpec.Components.Add(instance);
            
            // Link UI to Data (rudimentary)
            item.userData = instance; // Override default userData usage or coordinate it

            // Basic Drag Logic (MVP)
            item.RegisterCallback<PointerDownEvent>(e => {
                if (e.button == 0) {
                    item.CapturePointer(e.pointerId);
                    item.userData = e.localPosition; // Store offset
                }
            });
            item.RegisterCallback<PointerMoveEvent>(e => {
                if (item.HasPointerCapture(e.pointerId)) {
                    var offset = (Vector3)item.userData;
                    // Need to convert to local coordinates properly, but for MVP this is rough
                    // Delta is easiest?
                    item.style.left = item.style.left.value.value + e.deltaPosition.x;
                    item.style.top = item.style.top.value.value + e.deltaPosition.y;
                }
            });
            item.RegisterCallback<PointerUpEvent>(e => {
                if (item.HasPointerCapture(e.pointerId)) {
                    item.ReleasePointer(e.pointerId);
                    SelectComponent(componentName);
                }
            });

            _canvas.Add(item);
        }

        private void SelectComponent(string name)
        {
            if (_inspectorContent == null) return;
            _inspectorContent.Clear();
            _inspectorContent.Add(new Label($"Selected: {name}"));
            _inspectorContent.Add(new Label("Properties acting as placeholders..."));
        }

        private void OnSimulateClicked(ClickEvent evt)
        {
            Debug.Log("Simulate Button Clicked - Transitioning to RunMode");
            
            if (SessionManager.Instance != null && _currentSpec != null)
            {
                // Ensure Session has the latest version of our spec
                // (Since we modified the reference directly, it should be fine, but let's be explicit)
                SessionManager.Instance.StartSession(_currentSpec);
                UnityEngine.SceneManagement.SceneManager.LoadScene(2); // Level 2 = RunMode
            }
        }

        private string MapNameToId(string name)
        {
            switch (name)
            {
                case "Resistor": return "resistor";
                case "LED": return "led";
                case "Arduino Uno": return "uno";
                case "Battery (9V)": return "source_5v"; // Mapping 9V to 5V source for MVP or BatteryModel if exists? 
                                                         // Wait, Catalog has "source_5v". Let's stick to safe defaults.
                case "Servo Motor": return "servo"; // Not in default catalog I viewed?
                default: return "unknown";
            }
        }
    }
}
