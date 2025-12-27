using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.UI
{
    public class ProjectWizardController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _root;
        
        // Views
        private VisualElement _homeView;
        private VisualElement _projectsView;
        private VisualElement _templatesView;
        private VisualElement[] _allViews;

        // Nav
        private Dictionary<string, Button> _navButtons;

        // Data
        private struct MockProject { public string Id; public string Name; public string Date; public string Type; }
        private List<MockProject> _projects = new List<MockProject>();

        // Context Menu
        private VisualElement _contextMenu;
        private string _activeContextItemId;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) return;
            _root = _doc.rootVisualElement;

            GenerateMockData();
            InitializeUI();
        }

        private void InitializeUI()
        {
            // 1. Resolve Views
            _homeView = _root.Q<VisualElement>("HomeView");
            _projectsView = _root.Q<VisualElement>("ProjectsView");
            _templatesView = _root.Q<VisualElement>("TemplatesView");
            _allViews = new[] { _homeView, _projectsView, _templatesView };

            // 2. Resolve Navigation
            _navButtons = new Dictionary<string, Button> {
                { "Home", _root.Q<Button>("NavHome") },
                { "Projects", _root.Q<Button>("NavProjects") },
                { "Templates", _root.Q<Button>("NavTemplates") },
                { "Settings", _root.Q<Button>("NavSettings") }
            };

            // Bind Nav Clicks
            if (_navButtons["Home"] != null) _navButtons["Home"].clicked += () => SwitchView(_homeView, "Home");
            if (_navButtons["Projects"] != null) _navButtons["Projects"].clicked += () => SwitchView(_projectsView, "Projects");
            if (_navButtons["Templates"] != null) _navButtons["Templates"].clicked += () => SwitchView(_templatesView, "Templates");

            // 3. Bind Actions
            _root.Q<Button>("ViewAllBtn")?.RegisterCallback<ClickEvent>(e => SwitchView(_projectsView, "Projects"));

            // 4. Bind Search
            var searchHome = _root.Q<TextField>("RecentSearchField");
            var searchProj = _root.Q<TextField>("ProjectsSearchField");
            
            searchHome?.RegisterValueChangedCallback(e => RenderList(_root.Q<ScrollView>("RecentProjectsList"), e.newValue, 5));
            searchProj?.RegisterValueChangedCallback(e => RenderList(_root.Q<ScrollView>("ProjectsList"), e.newValue, 100));

            // 5. Context Menu
            _contextMenu = _root.Q<VisualElement>("ContextMenu");
            // Click anywhere on root to close menu
            _root.RegisterCallback<MouseDownEvent>(e => 
            {
                // Simple hit test check could go here, but blindly closing is okay if we re-open on specific clicks
                // However, blocking the menu click itself is needed.
                if (_contextMenu.style.display == DisplayStyle.Flex)
                    HideContextMenu();
            }, TrickleDown.NoTrickleDown);

             _root.Q<Button>("ContextRemoveBtn")?.RegisterCallback<ClickEvent>(e => RemoveProject(_activeContextItemId));

            // Initial Render
            SwitchView(_homeView, "Home");
        }

        private void GenerateMockData()
        {
            _projects = new List<MockProject> {
                new MockProject { Id="1", Name = "Mars Rover Rev2", Date = "2h ago", Type = "Robotics" },
                new MockProject { Id="2", Name = "Drone Flight Controller", Date = "Yesterday", Type = "PCB" },
                new MockProject { Id="3", Name = "Bipedal Walker AI", Date = "Oct 20", Type = "Simulation" },
                new MockProject { Id="4", Name = "Home Automation Hub", Date = "Sep 15", Type = "IoT" },
                new MockProject { Id="5", Name = "Mini Sumo Robot", Date = "Aug 01", Type = "Robotics" },
                new MockProject { Id="6", Name = "Warehouse Twin", Date = "Jul 22", Type = "Logistics" }
            };
        }

        private void SwitchView(VisualElement target, string navKey)
        {
            if (target == null) return;

            // Hide all
            foreach(var v in _allViews) if(v!=null) v.style.display = DisplayStyle.None;
            
            // Show target
            target.style.display = DisplayStyle.Flex;

            // Update Nav State
            foreach(var kvp in _navButtons) 
            {
                if(kvp.Value == null) continue;
                if(kvp.Key == navKey) kvp.Value.AddToClassList("active");
                else kvp.Value.RemoveFromClassList("active");
            }

            // Refresh Lists
            if (navKey == "Home") RenderList(_root.Q<ScrollView>("RecentProjectsList"), "", 5);
            if (navKey == "Projects") RenderList(_root.Q<ScrollView>("ProjectsList"), "", 100);
        }

        private void RenderList(ScrollView container, string filter, int limit)
        {
            if (container == null) return;
            container.Clear();

            var filtered = _projects
                .Where(p => string.IsNullOrEmpty(filter) || p.Name.ToLower().Contains(filter.ToLower()))
                .Take(limit);

            foreach (var proj in filtered)
            {
                var card = CreateProjectCard(proj);
                container.Add(card);
            }
        }

        private VisualElement CreateProjectCard(MockProject proj)
        {
            // .project-card-item
            var card = new VisualElement();
            card.AddToClassList("project-card-item");

            // Header
            var header = new VisualElement();
            header.AddToClassList("p-header");
            
            var icon = new VisualElement();
            icon.AddToClassList("p-icon");
            // Vary icon based on type for fun
            if (proj.Type == "PCB") icon.AddToClassList("icon-cpu");
            else icon.AddToClassList("icon-box");

            var menuBtn = new Button();
            menuBtn.AddToClassList("p-menu-btn");
            menuBtn.AddToClassList("icon-more-vertical");
            menuBtn.RegisterCallback<ClickEvent>(e => 
            {
                e.StopPropagation(); // Prevent root closing immediately
                ShowContextMenu(e, proj.Id);
            });

            header.Add(icon);
            header.Add(menuBtn);

            // Info
            var title = new Label(proj.Name);
            title.AddToClassList("p-title");
            
            var date = new Label(proj.Date);
            date.AddToClassList("p-date");

            var typeBadge = new Label(proj.Type);
            typeBadge.AddToClassList("p-type-badge");

            card.Add(header);
            card.Add(title);
            card.Add(date);
            card.Add(typeBadge);

            return card;
        }

        private void ShowContextMenu(ClickEvent evt, string projId)
        {
            _activeContextItemId = projId;
            if (_contextMenu == null) return;

            _contextMenu.style.display = DisplayStyle.Flex;
            _contextMenu.BringToFront();
            
            // Position
            Vector2 localPos = evt.position; 
            if (_root != null) localPos = _root.WorldToLocal(evt.position);
            
            _contextMenu.style.left = localPos.x - 140; // Shift left to align
            _contextMenu.style.top = localPos.y + 10;
        }

        private void HideContextMenu()
        {
            if (_contextMenu != null) _contextMenu.style.display = DisplayStyle.None;
        }

        private void RemoveProject(string id)
        {
            var item = _projects.FirstOrDefault(p => p.Id == id);
            if (!string.IsNullOrEmpty(item.Id))
            {
                _projects.Remove(item);
                HideContextMenu();
                // Refresh active view
                if (_homeView.style.display == DisplayStyle.Flex) RenderList(_root.Q<ScrollView>("RecentProjectsList"), "", 5);
                if (_projectsView.style.display == DisplayStyle.Flex) RenderList(_root.Q<ScrollView>("ProjectsList"), "", 100);
            }
        }
    }
}
