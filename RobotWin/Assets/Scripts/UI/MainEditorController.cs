using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace RobotTwin.UI
{
    public class MainEditorController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _root;
        private Label _statusLabel;
        private Label _timeLabel;

        private TextField _hierarchySearchField;
        private ScrollView _hierarchyContainer;
        private readonly string[] _mockHierarchyItems = new[]
        {
            "Scene Root",
            "Environment",
            "    Floor",
            "    Walls",
            "    Lights",
            "Robot (UR5)",
            "    Base",
            "    Shoulder",
            "    Elbow",
            "    Wrist 1",
            "    Wrist 2",
            "    Wrist 3",
            "Camera Rig"
        };

        private Button _btnPlay;
        private Button _btnPause;
        private Button _btnStop;
        private Button _btnStep;

        private VisualElement _playIcon;
        private VisualElement _pauseIcon;

        private bool _isPlaying = false;
        private float _simTime = 0f;

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) return;
            var root = _doc.rootVisualElement;
            _root = root;
            UiResponsive.Bind(_root, 1200f, 1600f, "editor-compact", "editor-medium", "editor-wide");

            // Bind Toolbar
            _statusLabel = root.Q<Label>("SimStatusLabel");
            _timeLabel = root.Q<Label>("SimTimeLabel");

            _btnPlay = root.Q<Button>("BtnPlay");
            _btnPause = root.Q<Button>("BtnPause");
            _btnStop = root.Q<Button>("BtnStop");
            _btnStep = root.Q<Button>("BtnStep");

            _btnPlay.clicked += OnPlayClicked;
            _btnPause.clicked += OnPauseClicked;
            _btnStop.clicked += OnStopClicked;
            _btnStep.clicked += () => Debug.Log("Step Simulation");

            // Populate Mock Hierarchy
            _hierarchyContainer = root.Q<ScrollView>("HierarchyTree");
            _hierarchySearchField = root.Q<TextField>("HierarchySearchField");

            SearchFieldHelpers.SetupHint(_hierarchySearchField, "Search hierarchy...");
            if (_hierarchySearchField != null)
            {
                _hierarchySearchField.isDelayed = false;
                _hierarchySearchField.RegisterValueChangedCallback(_ => RefreshMockHierarchy());
            }

            RefreshMockHierarchy();
        }

        void Update()
        {
            if (_isPlaying)
            {
                _simTime += Time.deltaTime;
                if (_timeLabel != null)
                    _timeLabel.text = System.TimeSpan.FromSeconds(_simTime).ToString(@"mm\:ss\.ff");
            }
        }

        private void OnPlayClicked()
        {
            _isPlaying = true;
            UpdateStatus("RUNNING", "running");

            // Toggle Buttons in a real app, here we just swap visibility logic if we had separate buttons
            _btnPlay.style.display = DisplayStyle.None;
            _btnPause.style.display = DisplayStyle.Flex;
        }

        private void OnPauseClicked()
        {
            _isPlaying = false;
            UpdateStatus("PAUSED", "paused");

            _btnPlay.style.display = DisplayStyle.Flex;
            _btnPause.style.display = DisplayStyle.None;
        }

        private void OnStopClicked()
        {
            _isPlaying = false;
            _simTime = 0f;
            if (_timeLabel != null) _timeLabel.text = "00:00.00";
            UpdateStatus("READY", "ready");

            _btnPlay.style.display = DisplayStyle.Flex;
            _btnPause.style.display = DisplayStyle.None;
        }

        private void UpdateStatus(string text, string className)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = text;
            _statusLabel.ClearClassList();
            _statusLabel.AddToClassList("status-badge");
            _statusLabel.AddToClassList(className);
        }

        private void RefreshMockHierarchy()
        {
            if (_hierarchyContainer == null) return;
            _hierarchyContainer.Clear();

            string query = SearchFieldHelpers.GetEffectiveQuery(_hierarchySearchField, "Search hierarchy...");
            string queryLower = string.IsNullOrWhiteSpace(query) ? string.Empty : query.ToLowerInvariant();
            bool filtering = !string.IsNullOrWhiteSpace(queryLower);

            bool any = false;
            foreach (var item in _mockHierarchyItems)
            {
                string trimmed = item.Trim();
                if (filtering && !trimmed.ToLowerInvariant().Contains(queryLower))
                {
                    continue;
                }

                var row = new VisualElement();
                row.AddToClassList("tree-item");

                var icon = new VisualElement();
                icon.AddToClassList("tree-icon");

                var label = new Label(trimmed);
                label.AddToClassList("tree-label");

                // Indentation hack
                if (item.StartsWith("   ")) row.style.paddingLeft = 20;

                row.Add(icon);
                row.Add(label);
                _hierarchyContainer.Add(row);
                any = true;
            }

            if (filtering && !any)
            {
                var empty = new Label("No matches");
                empty.style.opacity = 0.7f;
                _hierarchyContainer.Add(empty);
            }
        }
    }
}
