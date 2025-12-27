using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq; // Added for Linq

namespace RobotTwin.UI
{
    public class ProjectWizardController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _root;

        // Data Models
        public struct UserProject
        {
            public string Name;
            public string Date;
            public string Type;
            public string Path;
        }

        public struct Template
        {
            public string Title;
            public string Description;
            public string IconClass;
        }

        // Logic State
        private string _projectsPath;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) { Debug.LogError("[ProjectWizard] Missing UIDocument"); return; }
            _root = _doc.rootVisualElement;
            if (_root == null) return;

            _projectsPath = Path.Combine(Application.persistentDataPath, "Projects");
            if (!Directory.Exists(_projectsPath)) Directory.CreateDirectory(_projectsPath);

            InitializeUI();
        }

        private void InitializeUI()
        {
            // Navigation
            BindNavButton("BtnHome", "HomeView");
            BindNavButton("BtnProjects", "ProjectsView");
            BindNavButton("BtnTemplates", "TemplatesView");

            // Home View Actions
            var btnViewAll = _root.Q<Button>("BtnViewAll");
            if (btnViewAll != null) btnViewAll.clicked += () => SwitchTab("ProjectsView");

            var btnNew = _root.Q<Button>("NewProjectBtn");
            if (btnNew != null) btnNew.clicked += () => SwitchTab("TemplatesView");

            // Initial Data
            PopulateProjects();
            PopulateTemplates();

            // Default Tab
            SwitchTab("HomeView");
        }

        private void SwitchTab(string viewName)
        {
            // Toggle Views
            SetDisplay("HomeView", viewName == "HomeView");
            SetDisplay("ProjectsView", viewName == "ProjectsView");
            SetDisplay("TemplatesView", viewName == "TemplatesView");

            // Update Sidebar State
            SetActiveNav("BtnHome", viewName == "HomeView");
            SetActiveNav("BtnProjects", viewName == "ProjectsView");
            SetActiveNav("BtnTemplates", viewName == "TemplatesView");
        }

        private void SetDisplay(string elemName, bool visible)
        {
            var elem = _root.Q(elemName);
            if (elem != null) elem.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetActiveNav(string btnName, bool isActive)
        {
            var btn = _root.Q<Button>(btnName);
            if (btn != null)
            {
                if (isActive) btn.AddToClassList("active");
                else btn.RemoveFromClassList("active");
            }
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

            // Scan Directory
            var projects = new List<UserProject>();
            if (Directory.Exists(_projectsPath))
            {
                var files = Directory.GetFiles(_projectsPath, "*.rtwin");
                foreach (var file in files)
                {
                    projects.Add(new UserProject 
                    { 
                        Name = Path.GetFileNameWithoutExtension(file),
                        Date = File.GetLastWriteTime(file).ToString("MMM dd"),
                        Type = "Custom",
                        Path = file
                    });
                }
            }
            
            // Add Mock if empty for visuals
            if (projects.Count == 0)
            {
                projects.Add(new UserProject { Name = "Example Project", Date = "Now", Type = "Demo", Path = "" });
            }

            foreach (var proj in projects)
            {
                var card = CreateCard(proj);
                list.Add(card);
            }
        }

        private VisualElement CreateCard(UserProject proj)
        {
            var card = new VisualElement();
            card.AddToClassList("project-card"); // Check USS for this
            
            // Styling fallback if class missing
            card.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f));
            card.style.marginBottom = 5;
            card.style.paddingLeft = 10;
            card.style.paddingTop = 10;
            card.style.paddingBottom = 10;
            
            // API Compliance: Explicit Borders
            card.style.borderTopWidth = 1; card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1; card.style.borderRightWidth = 1;
            card.style.borderTopColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            card.style.borderBottomColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            card.style.borderLeftColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            card.style.borderRightColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            card.style.borderTopLeftRadius = 4; card.style.borderBottomRightRadius = 4;
            card.style.borderTopRightRadius = 4; card.style.borderBottomLeftRadius = 4;

            var label = new Label($"{proj.Name} ({proj.Type})");
            label.style.color = Color.white;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(label);

            return card;
        }

        private void PopulateTemplates()
        {
            var list = _root.Q<ScrollView>("TemplateListContainer");
            if (list == null) return;
            list.Clear();

            // Set grid layout on container if possible via code or ensure it is in UXML/USS
            list.contentContainer.style.flexDirection = FlexDirection.Row;
            list.contentContainer.style.flexWrap = Wrap.Wrap;

            var templates = new List<Template>
            {
                new Template { Title = "Empty", Description = "Clean Slate", IconClass = "icon-plus" },
                new Template { Title = "Robotics", Description = "Arm & Gripper", IconClass = "icon-hexagon" },
                new Template { Title = "IoT", Description = "Sensors Network", IconClass = "icon-activity" },
                new Template { Title = "Simulation", Description = "Physics Sandbox", IconClass = "icon-cpu" }
            };

            foreach (var tmpl in templates)
            {
                var card = new Button(() => OnTemplateClicked(tmpl));
                card.AddToClassList("template-card");
                
                // Styling
                card.style.backgroundColor = new StyleColor(new Color(0.25f, 0.25f, 0.25f));
                card.style.marginRight = 10;
                card.style.marginBottom = 10;
                card.style.width = 140;
                card.style.height = 140;
                card.style.alignItems = Align.Center;
                card.style.justifyContent = Justify.Center;
                
                // API Compliance
                card.style.borderTopWidth = 1; card.style.borderBottomWidth = 1;
                card.style.borderLeftWidth = 1; card.style.borderRightWidth = 1;
                card.style.borderTopColor = new StyleColor(new Color(0.4f, 0.4f, 0.4f)); 
                card.style.borderBottomColor = new StyleColor(new Color(0.4f, 0.4f, 0.4f)); 
                card.style.borderLeftColor = new StyleColor(new Color(0.4f, 0.4f, 0.4f)); 
                card.style.borderRightColor = new StyleColor(new Color(0.4f, 0.4f, 0.4f)); 
                card.style.borderTopLeftRadius = 8; card.style.borderBottomRightRadius = 8;
                card.style.borderTopRightRadius = 8; card.style.borderBottomLeftRadius = 8;

                // Icon
                var icon = new VisualElement();
                icon.AddToClassList(tmpl.IconClass);
                icon.style.width = 32; icon.style.height = 32;
                icon.style.marginBottom = 10;
                card.Add(icon);

                var lbl = new Label(tmpl.Title);
                lbl.style.color = Color.white;
                lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                card.Add(lbl);

                list.Add(card);
            }
        }

        private void OnTemplateClicked(Template tmpl)
        {
            Debug.Log($"[ProjectWizard] Creating new project: {tmpl.Title}");
            
            // Create Manifest
            string fileName = $"Project_{System.DateTime.Now.Ticks}.rtwin";
            string fullPath = Path.Combine(_projectsPath, fileName);
            File.WriteAllText(fullPath, $"{{\"template\": \"{tmpl.Title}\"}}");

            // Transition
            // Assuming Scene 1 is the main scene. Using BuildIndex 1 or Name.
            // Be safe: Load Scene by Name if possible, else Index 1.
            int sceneCount = SceneManager.sceneCountInBuildSettings;
            if (sceneCount > 1) 
            {
                SceneManager.LoadScene(1); 
            }
            else
            {
                Debug.LogWarning("[ProjectWizard] Scene 1 not found in Build Settings. Staying here.");
            }
        }
    }
}
