using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace RobotTwin.UI
{
    public class ProjectWizardController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _recentList;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) return;

            var root = _doc.rootVisualElement;

            // Wire Actions
            root.Q<Button>("NewProjectBtn")?.RegisterCallback<ClickEvent>(OnNewProjectClicked);
            root.Q<Button>("OpenProjectBtn")?.RegisterCallback<ClickEvent>(e => Debug.Log("Open Project Clicked"));
            root.Q<Button>("ImportCadBtn")?.RegisterCallback<ClickEvent>(e => Debug.Log("Import CAD Clicked"));

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
            Debug.Log("New Project Clicked");
            // Future: Open Template Selector
        }

        private void PopulateRecentProjects()
        {
            if (_recentList == null) return;
            _recentList.Clear();

            var dummies = new List<(string name, string date)>()
            {
                ("Line Follower V2", "2 hours ago"),
                ("Robot Arm Prototype", "Yesterday"),
                ("Blinky Test", "2 days ago")
            };

            foreach (var item in dummies)
            {
                var row = new VisualElement();
                row.AddToClassList("recent-project-item");

                var labelName = new Label(item.name);
                labelName.style.unityFontStyleAndWeight = FontStyle.Bold;
                
                var labelDate = new Label(item.date);
                labelDate.style.fontSize = 10;
                labelDate.style.color = new Color(0.6f, 0.6f, 0.6f); // Greyish

                row.Add(labelName);
                row.Add(labelDate);
                
                _recentList.Add(row);
            }
        }
    }
}
