using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace RobotTwin.UI
{
    public class ProjectWizardController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _projectListContainer;

        private const string SearchRowFocusedClass = "search-row--focused";

        // MOCK DATA (Guarantees UI is never empty)
        public struct ProjectData { public string Name; public string Type; public string Date; }
        private readonly List<ProjectData> _mockData = new List<ProjectData>
        {
            new ProjectData { Name = "Mars Rover v2", Type = "Robotics", Date = "2h ago" },
            new ProjectData { Name = "Drone Flight Controller", Type = "PCB", Date = "Yesterday" },
            new ProjectData { Name = "Home Automation Hub", Type = "IoT", Date = "Oct 22" },
            new ProjectData { Name = "Bipedal Walker AI", Type = "Sim", Date = "Sep 15" },
            new ProjectData { Name = "Mini Sumo Robot", Type = "Robotics", Date = "Aug 01" }
        };

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) return;

            var root = _doc.rootVisualElement;

            WireSearchRowFocus(root);

            // 1. Navigation Binding
            var btnProjects = root.Q<Button>("BtnProjects");
            var btnHome = root.Q<Button>("BtnHome");
            var homeView = root.Q("HomeView");
            var projectsView = root.Q("ProjectsView");

            if (btnProjects != null) btnProjects.clicked += () =>
            {
                if (homeView != null) homeView.style.display = DisplayStyle.None;
                if (projectsView != null) projectsView.style.display = DisplayStyle.Flex;
                PopulateProjects(root);
            };

            if (btnHome != null) btnHome.clicked += () =>
            {
                if (homeView != null) homeView.style.display = DisplayStyle.Flex;
                if (projectsView != null) projectsView.style.display = DisplayStyle.None;
            };

            // 2. Initial Population
            PopulateProjects(root);
        }

        private void WireSearchRowFocus(VisualElement root)
        {
            WireSearchRowFocus(root, "RecentSearchField");
            WireSearchRowFocus(root, "ProjectsSearchField");
        }

        private void WireSearchRowFocus(VisualElement root, string textFieldName)
        {
            var field = root.Q<TextField>(textFieldName);
            if (field == null) return;

            var searchRow = field.parent;
            if (searchRow == null) return;

            field.RegisterCallback<FocusInEvent>(_ => searchRow.AddToClassList(SearchRowFocusedClass));
            field.RegisterCallback<FocusOutEvent>(_ => searchRow.RemoveFromClassList(SearchRowFocusedClass));
        }

        void PopulateProjects(VisualElement root)
        {
            // Try to find container by ID, fallback to creating it if missing
            _projectListContainer = root.Q<VisualElement>("ProjectListContainer");

            // Fallback for missing container in UXML
            if (_projectListContainer == null)
            {
                var projectsView = root.Q("ProjectsView");
                if (projectsView != null)
                {
                    _projectListContainer = new VisualElement();
                    _projectListContainer.name = "ProjectListContainer";
                    projectsView.Add(_projectListContainer);
                }
                else
                {
                    Debug.LogError("CRITICAL: Neither 'ProjectListContainer' nor 'ProjectsView' found in UXML.");
                    return;
                }
            }

            _projectListContainer.Clear();

            foreach (var data in _mockData)
            {
                // Create Card Container Programmatically (Bypass UXML templates for safety)
                var card = new VisualElement();
                card.AddToClassList("project-card");
                card.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.18f));
                card.style.marginBottom = 10;
                card.style.paddingTop = 10; card.style.paddingBottom = 10; card.style.paddingLeft = 10;
                card.style.height = 60;
                card.style.flexDirection = FlexDirection.Row;
                card.style.justifyContent = Justify.SpaceBetween;
                card.style.alignItems = Align.Center;

                // Info Stack
                var info = new VisualElement();
                var nameLbl = new Label(data.Name);
                nameLbl.style.fontSize = 14;
                nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLbl.style.color = new StyleColor(Color.white);

                var typeLbl = new Label($"{data.Type} • {data.Date}");
                typeLbl.style.fontSize = 11;
                typeLbl.style.color = new StyleColor(Color.gray);

                info.Add(nameLbl);
                info.Add(typeLbl);

                // Menu Button
                var menuBtn = new Button(() => Debug.Log($"Menu: {data.Name}"));
                menuBtn.text = ""; // No text, just icon
                menuBtn.AddToClassList("icon-menu"); // Use USS class for PNG icon
                menuBtn.style.width = 30;
                menuBtn.style.height = 30;
                menuBtn.style.backgroundColor = StyleKeyword.None;

                menuBtn.style.borderTopWidth = 0;
                menuBtn.style.borderBottomWidth = 0;
                menuBtn.style.borderLeftWidth = 0;
                menuBtn.style.borderRightWidth = 0;
                menuBtn.style.alignSelf = Align.Center;

                card.Add(info);
                card.Add(menuBtn);

                _projectListContainer.Add(card);
            }
        }
    }
}
