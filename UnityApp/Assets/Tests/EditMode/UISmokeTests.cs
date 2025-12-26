using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace RobotTwin.Tests.EditMode
{
    public class UISmokeTests
    {
        [Test]
        public void RunMode_UXML_ContainsCriticalElements()
        {
            var path = "Assets/UI/RunMode/RunMode.uxml";
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.IsNotNull(tree, $"Could not load UXML at {path}");

            var root = tree.CloneTree();
            
            // HUD
            Assert.IsNotNull(root.Q<Label>("TimeLabel"), "TimeLabel missing");
            Assert.IsNotNull(root.Q<Label>("TickLabel"), "TickLabel missing");
            
            // Injection
            Assert.IsNotNull(root.Q<TextField>("NewSignalName"), "NewSignalName missing");
            Assert.IsNotNull(root.Q<Button>("AddSignalBtn"), "AddSignalBtn missing");
            Assert.IsNotNull(root.Q<ScrollView>("SignalList"), "SignalList missing");
            
            // Logs
            Assert.IsNotNull(root.Q<Label>("LogContentLabel"), "LogContentLabel missing");
        }

        [Test]
        public void ProjectWizard_UXML_ContainsCriticalElements()
        {
            var path = "Assets/UI/ProjectWizard/ProjectWizard.uxml";
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.IsNotNull(tree, $"Could not load UXML at {path}");

            var root = tree.CloneTree();
            
            Assert.IsNotNull(root.Q<ListView>("TemplateList"), "TemplateList missing");
            Assert.IsNotNull(root.Q<Button>("CreateButton"), "CreateButton missing");
            Assert.IsNotNull(root.Q<Label>("DescriptionLabel"), "DescriptionLabel missing");
        }
    }
}
