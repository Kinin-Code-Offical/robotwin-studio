using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Catalogs.Templates;
using RobotTwin.CoreSim.Serialization;
using RobotTwin.Game;
using System.Linq;
using System.IO;

namespace RobotTwin.UI
{
    public class ProjectWizardController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _recentList;
        private string _projectsDir;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) return;

            _projectsDir = Path.Combine(Application.persistentDataPath, "Projects");
            Directory.CreateDirectory(_projectsDir);

            var root = _doc.rootVisualElement;

            // Wire Actions
            root.Q<Button>("NewProjectBtn")?.RegisterCallback<ClickEvent>(OnNewProjectClicked);
            root.Q<Button>("OpenProjectBtn")?.RegisterCallback<ClickEvent>(OnOpenProjectClicked);
            root.Q<Button>("ImportCadBtn")?.RegisterCallback<ClickEvent>(e => Debug.Log("Import CAD Clicked (Stub)"));

            // Wire Nav (Stub)
            root.Q<Button>("NavHome")?.RegisterCallback<ClickEvent>(e => Debug.Log("Nav: Home"));
            root.Q<Button>("NavProjects")?.RegisterCallback<ClickEvent>(e => Debug.Log("Nav: Projects"));
            root.Q<Button>("NavTemplates")?.RegisterCallback<ClickEvent>(e => Debug.Log("Nav: Templates"));

            // Populate Recents
            _recentList = root.Q<ScrollView>("RecentProjectsList");
            PopulateRecentProjects();
        }

        private void OnNewProjectClicked(ClickEvent evt)
        {
            Debug.Log("Creating New Project...");
            
            // 1. Get Template Spec (Blinky for now)
            var template = BlinkyTemplate.GetSpec();
            
            // 2. Create Manifest
            string projectName = $"Project_{System.DateTime.Now:MMdd_HHmm}";
            var manifest = new ProjectManifest
            {
                ProjectName = projectName,
                Description = "Created via Wizard",
                Version = "0.1.0",
                Circuit = template.DefaultCircuit,
                Robot = template.DefaultRobot ?? new RobotSpec { Name = "DefaultRobot" },
                World = template.DefaultWorld ?? new WorldSpec { Name = "DefaultWorld" }
            };

            // 3. Save
            string path = Path.Combine(_projectsDir, $"{projectName}.rtwin");
            SimulationSerializer.SaveProject(manifest, path);
            Debug.Log($"Project saved to: {path}");

            // 4. Start Session
            if (SessionManager.Instance != null)
            {
                SessionManager.Instance.StartSession(manifest);
                UnityEngine.SceneManagement.SceneManager.LoadScene(2); // Jump to RunMode
            }
        }

        private void OnOpenProjectClicked(ClickEvent evt)
        {
            // MVP: Load the most recent project found
            var files = Directory.GetFiles(_projectsDir, "*.rtwin");
            if (files.Length == 0)
            {
                Debug.LogWarning("No projects found to open.");
                return;
            }

            var recent = files.OrderByDescending(f => File.GetLastWriteTime(f)).First();
            Debug.Log($"Opening recent project: {recent}");

            var manifest = SimulationSerializer.LoadProject(recent);
            if (manifest != null && SessionManager.Instance != null)
            {
                SessionManager.Instance.StartSession(manifest);
                UnityEngine.SceneManagement.SceneManager.LoadScene(2);
            }
        }


        private void PopulateRecentProjects()
        {
            if (_recentList == null) return;
            _recentList.Clear();

            // Real Scan
            if (!Directory.Exists(_projectsDir)) Directory.CreateDirectory(_projectsDir);
            var files = Directory.GetFiles(_projectsDir, "*.rtwin")
                                 .OrderByDescending(f => File.GetLastWriteTime(f))
                                 .Take(5)
                                 .ToList();

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var time = File.GetLastWriteTime(file).ToString("g");

                var row = new VisualElement();
                row.AddToClassList("recent-project-item");
                // Allow clicking row to open
                row.RegisterCallback<ClickEvent>(e => 
                {
                    var m = SimulationSerializer.LoadProject(file);
                    if (m != null)
                    {
                        SessionManager.Instance.StartSession(m);
                        UnityEngine.SceneManagement.SceneManager.LoadScene(2);
                    }
                });

                var labelName = new Label(name);
                labelName.style.unityFontStyleAndWeight = FontStyle.Bold;
                
                var labelDate = new Label(time);
                labelDate.style.fontSize = 10;
                labelDate.style.color = new Color(0.6f, 0.6f, 0.6f); 

                row.Add(labelName);
                row.Add(labelDate);
                
                _recentList.Add(row);
            }
        }
    }
}
