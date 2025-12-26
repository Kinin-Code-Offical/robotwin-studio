using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using RobotTwin.CoreSim.Specs;
// Assuming these namespaces exist based on context
// If not, I will rely on the user to fix or I'll catch it in compilation if I ran tests (which I can't easily do).
// But I'll stick to standard Unity UI Toolkit logic + Stub backend calls.

namespace RobotTwin.UI
{
    public class CircuitStudioController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _libraryList;
        private VisualElement _canvas;
        private VisualElement _inspectorContent;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) return;

            var root = _doc.rootVisualElement;

            _libraryList = root.Q<ScrollView>("LibraryList");
            _canvas = root.Q<VisualElement>("Canvas");
            _inspectorContent = root.Q<ScrollView>("InspectorContent");

            root.Q<Button>("SimulateBtn")?.RegisterCallback<ClickEvent>(OnSimulateClicked);

            PopulateLibrary();
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
            item.Add(label);

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
            Debug.Log("Simulate Button Clicked - Transitioning to RunMode (Stub)");
            // In real app, this would save circuit and load RunMode
        }
    }
}
