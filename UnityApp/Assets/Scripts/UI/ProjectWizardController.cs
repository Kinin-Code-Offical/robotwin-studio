using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
            public string Path;
            public string Type;
            public string Modified;
        }

        public struct Template
        {
            public string Title;
            public string Description;
            public string Tag;
            public string Difficulty;
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

            // 1. Real Projects
            var projects = new List<UserProject>();
            if (Directory.Exists(_projectsPath))
            {
                var files = Directory.GetFiles(_projectsPath, "*.rtwin");
                foreach (var file in files)
                {
                    projects.Add(new UserProject 
                    { 
                        Name = Path.GetFileNameWithoutExtension(file),
                        Path = $"/projects/user/{Path.GetFileName(file)}",
                        Type = "Custom",
                        Modified = File.GetLastWriteTime(file).ToString("MMM dd")
                    });
                }
            }

            // 2. Mock Projects (Premium Demo Data from Screenshot)
            if (projects.Count == 0)
            {
                projects.Add(new UserProject { Name = "Mars_Rover_PCB_Rev2", Path = "/projects/mars-rover/pcb-main.rw", Type = "PCB Design", Modified = "2 hours ago" });
                projects.Add(new UserProject { Name = "Hydraulic_Arm_Sim_v4", Path = "/projects/industrial/arm-v4.rw", Type = "Robotics Arm", Modified = "Yesterday" });
                projects.Add(new UserProject { Name = "Drone_Flight_Controller", Path = "/projects/aero/f405-controller.rw", Type = "Simulation", Modified = "Oct 24, 2023" });
                projects.Add(new UserProject { Name = "Bipedal_Walker_AI", Path = "/projects/ml/bipedal.rw", Type = "Simulation", Modified = "Oct 20, 2023" });
                projects.Add(new UserProject { Name = "Autonomous_Logistics_Bot", Path = "/projects/logistics/bot-v1.rw", Type = "Robotics Arm", Modified = "Sep 15" });
                projects.Add(new UserProject { Name = "Smart_Home_Hub_PCB", Path = "/projects/iot/hub-main.rw", Type = "PCB Design", Modified = "Aug 30" });
            }

            foreach (var proj in projects)
            {
                var row = CreateProjectRow(proj);
                list.Add(row);
            }
        }

        private VisualElement CreateProjectRow(UserProject proj)
        {
            var row = new VisualElement();
            row.AddToClassList("project-row");
            
            // Name Column (Icon + Text)
            var colName = new VisualElement();
            colName.AddToClassList("col-name");
            colName.style.flexDirection = FlexDirection.Row;
            colName.style.alignItems = Align.Center;

            var icon = new VisualElement();
            // Assign icon based on type
            if (proj.Type.Contains("PCB")) icon.AddToClassList("icon-cpu");
            else if (proj.Type.Contains("Robotics")) icon.AddToClassList("icon-hexagon");
            else icon.AddToClassList("icon-activity");
            
            icon.AddToClassList("row-icon");
            colName.Add(icon);

            var lblName = new Label(proj.Name);
            lblName.AddToClassList("row-label-name");
            colName.Add(lblName);
            row.Add(colName);
            
            // Path Column
            var lblPath = new Label(proj.Path);
            lblPath.AddToClassList("col-path");
            lblPath.AddToClassList("row-text-dim");
            row.Add(lblPath);

            // Type Column
            var lblType = new Label(proj.Type);
            lblType.AddToClassList("col-type");
            // Badge style?
            lblType.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f));
            lblType.style.borderTopLeftRadius = 4; lblType.style.borderTopRightRadius = 4;
            lblType.style.borderBottomLeftRadius = 4; lblType.style.borderBottomRightRadius = 4;
            lblType.style.paddingLeft = 8; lblType.style.paddingRight = 8;
            lblType.style.paddingTop = 2; lblType.style.paddingBottom = 2;
            lblType.style.fontSize = 10;
            lblType.style.alignSelf = Align.FlexStart;
            row.Add(lblType);

            // Modified Column
            var lblDate = new Label(proj.Modified);
            lblDate.AddToClassList("col-date");
            lblDate.AddToClassList("row-text-dim");
            row.Add(lblDate);

            // Actions Column
            var colActions = new VisualElement();
            colActions.AddToClassList("col-actions");
            var btnMenu = new Button(); // More vertical dots usually
            btnMenu.text = ":"; // Placeholder for icon
            btnMenu.AddToClassList("row-action-btn");
            colActions.Add(btnMenu);
            row.Add(colActions);

            return row;
        }

        private void PopulateTemplates()
        {
            var list = _root.Q<ScrollView>("TemplateListContainer");
            if (list == null) return;
            list.Clear();

            list.contentContainer.style.flexDirection = FlexDirection.Row;
            list.contentContainer.style.flexWrap = Wrap.Wrap;

            var templates = new List<Template>
            {
                new Template { Title = "6-DOF Robot Arm", Description = "Standard industrial robot arm configuration with inverse kinematics solver setup.", Tag="ROBOTICS", Difficulty="Intermediate", IconClass = "icon-hexagon" },
                new Template { Title = "STM32 Flight Controller", Description = "PCB layout and schematic template for F405 based flight controllers.", Tag="PCB DESIGN", Difficulty="Advanced", IconClass = "icon-cpu" },
                new Template { Title = "Warehouse Digital Twin", Description = "Simulation environment with AGV paths, shelving units, and physics constraints.", Tag="SIMULATION", Difficulty="Beginner", IconClass = "icon-activity" },
                new Template { Title = "Bipedal Walker RL", Description = "Reinforcement learning environment for bipedal locomotion training with PyTorch integration.", Tag="AI / ML", Difficulty="Advanced", IconClass = "icon-zap" }
            };

            foreach (var tmpl in templates)
            {
                var card = CreateTemplateCard(tmpl);
                list.Add(card);
            }
        }

        private VisualElement CreateTemplateCard(Template tmpl)
        {
            var card = new VisualElement();
            card.AddToClassList("template-card-rich");

            // --- Header: Icon + Tag ---
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 15;

            var iconBox = new VisualElement();
            iconBox.AddToClassList("template-icon-box");
            if (tmpl.Tag == "ROBOTICS") iconBox.style.color = new StyleColor(new Color(1f, 0.6f, 0f)); // Orange
            if (tmpl.Tag == "PCB DESIGN") iconBox.style.color = new StyleColor(new Color(0f, 0.8f, 0.5f)); // Green
            if (tmpl.Tag == "SIMULATION") iconBox.style.color = new StyleColor(new Color(0.2f, 0.6f, 1f)); // Blue
            if (tmpl.Tag == "AI / ML") iconBox.style.color = new StyleColor(new Color(0.7f, 0.3f, 1f)); // Purple

            var icon = new VisualElement();
            icon.AddToClassList(tmpl.IconClass);
            icon.AddToClassList("template-icon");
            iconBox.Add(icon);
            header.Add(iconBox);

            var tag = new Label(tmpl.Tag);
            tag.AddToClassList("template-tag");
            header.Add(tag);
            card.Add(header);

            // --- Body: Title + Desc ---
            var title = new Label(tmpl.Title);
            title.AddToClassList("template-title");
            card.Add(title);

            var desc = new Label(tmpl.Description);
            desc.AddToClassList("template-desc");
            card.Add(desc);

            // --- Footer: Difficulty + Link ---
            var footer = new VisualElement();
            footer.AddToClassList("template-footer");
            
            var diff = new Label(tmpl.Difficulty);
            diff.AddToClassList("template-difficulty");
            footer.Add(diff);

            var createBtn = new Button(() => OnTemplateClicked(tmpl));
            createBtn.text = "CREATE ->";
            createBtn.AddToClassList("template-create-link");
            footer.Add(createBtn);

            card.Add(footer);

            return card;
        }

        private void OnTemplateClicked(Template tmpl)
        {
            Debug.Log($"[ProjectWizard] Creating new project: {tmpl.Title}");
            
            string fileName = $"Project_{System.DateTime.Now.Ticks}.rtwin";
            string fullPath = Path.Combine(_projectsPath, fileName);
            File.WriteAllText(fullPath, $"{{\"template\": \"{tmpl.Title}\"}}");

            int sceneCount = SceneManager.sceneCountInBuildSettings;
            if (sceneCount > 1) SceneManager.LoadScene(1);
            else Debug.LogWarning("[ProjectWizard] Scene 1 not found.");
        }
    }
}
