using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace RobotTwin.UI
{
    public class ProjectWizardController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _root;

        public struct UserProject
        {
            public string Name;
            public string Date;
            public string Type;
        }

        public struct Template
        {
            public string Title;
            public string Description;
            public string IconName;
        }

        private List<UserProject> _mockProjects;
        private List<Template> _mockTemplates;

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) { Debug.LogError("Missing UIDocument"); return; }
            _root = _doc.rootVisualElement;
            if (_root == null) return;

            // Mock Data
            _mockProjects = new List<UserProject>
            {
                new UserProject { Name = "Mars Rover", Date = "2h ago", Type = "Robotics" },
                new UserProject { Name = "Home IoT", Date = "Yesterday", Type = "IoT" },
                new UserProject { Name = "Factory Sim", Date = "Mon", Type = "Industrial" },
                new UserProject { Name = "Drone Flight", Date = "Oct 22", Type = "Embedded" },
                new UserProject { Name = "Smart City", Date = "Sep 15", Type = "Sim" }
            };

            _mockTemplates = new List<Template>
            {
                new Template { Title = "Empty", Description = "Clean Slate", IconName = "plus" },
                new Template { Title = "Blinky", Description = "LED Demo", IconName = "zap" },
                new Template { Title = "Sensor", Description = "Data Inputs", IconName = "cpu" }
            };

            InitializeUI();
        }

        private void InitializeUI()
        {
            BindNavButton("BtnHome", "HomeView");
            BindNavButton("BtnProjects", "ProjectsView");
            BindNavButton("BtnTemplates", "TemplatesView");
            
            var btnViewAll = _root.Q<Button>("BtnViewAll");
            if (btnViewAll != null) btnViewAll.clicked += () => SwitchTab("ProjectsView");

            var btnNew = _root.Q<Button>("NewProjectBtn");
            if (btnNew != null) btnNew.clicked += () => SwitchTab("TemplatesView");

            PopulateProjects();
            PopulateTemplates();
        }

        private void SwitchTab(string tabName)
        {
            SetDisplay("HomeView", tabName == "HomeView");
            SetDisplay("ProjectsView", tabName == "ProjectsView");
            SetDisplay("TemplatesView", tabName == "TemplatesView");
        }

        private void SetDisplay(string elemName, bool visible)
        {
            var elem = _root.Q(elemName);
            if (elem != null) elem.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void BindNavButton(string btnName, string targetView)
        {
            var btn = _root.Q<Button>(btnName);
            if (btn != null) btn.clicked += () => SwitchTab(targetView);
        }

        private void PopulateProjects()
        {
            var list = _root.Q<ScrollView>("ProjectListContainer");
            if (list == null) return;
            list.Clear();

            foreach (var proj in _mockProjects)
            {
                var card = new VisualElement();
                card.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f));
                card.style.marginBottom = 5;
                card.style.paddingLeft = 10;
                card.style.paddingTop = 10;
                card.style.paddingBottom = 10;
                
                card.style.borderTopLeftRadius = 4;
                card.style.borderTopRightRadius = 4;
                card.style.borderBottomLeftRadius = 4;
                card.style.borderBottomRightRadius = 4;

                var label = new Label($"{proj.Name} ({proj.Type})");
                label.style.color = Color.white;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                
                card.Add(label);
                list.Add(card);
            }
        }

        private void PopulateTemplates()
        {
            var list = _root.Q<ScrollView>("TemplateListContainer");
            if (list == null) return;
            list.Clear();

            foreach (var tmpl in _mockTemplates)
            {
                var card = new VisualElement();
                card.style.backgroundColor = new StyleColor(new Color(0.25f, 0.25f, 0.25f));
                card.style.marginRight = 10;
                card.style.width = 120;
                card.style.height = 120;
                card.style.alignItems = Align.Center;
                card.style.justifyContent = Justify.Center;

                card.style.borderTopLeftRadius = 8;
                card.style.borderTopRightRadius = 8;
                card.style.borderBottomLeftRadius = 8;
                card.style.borderBottomRightRadius = 8;

                var lbl = new Label(tmpl.Title);
                lbl.style.color = Color.white;
                
                card.Add(lbl);
                list.Add(card);
            }
        }
    }
}
