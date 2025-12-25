using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEditor;

namespace RobotTwin.Tests.EditMode
{
    public class WiringTests
    {
        [Test]
        public void Test_WizardScene_UIDocument_WiredCorrectly()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Wizard.unity");
            var go = GameObject.Find("ProjectWizard");
            Assert.IsNotNull(go, "GameObject 'ProjectWizard' not found in scene.");

            var uiDoc = go.GetComponent<UIDocument>();
            Assert.IsNotNull(uiDoc, "UIDocument component missing on ProjectWizard GameObject.");
            
            Assert.IsNotNull(uiDoc.visualTreeAsset, "UIDocument has no VisualTreeAsset assigned.");
            Assert.IsNotNull(uiDoc.panelSettings, "UIDocument has no PanelSettings assigned.");

            // Verify content (emulating instantiation)
            var root = uiDoc.visualTreeAsset.CloneTree();
            Assert.IsNotNull(root.Q<ListView>("TemplateList"), "ListView 'TemplateList' missing from VisualTree.");
            Assert.IsNotNull(root.Q<Button>("CreateButton"), "Button 'CreateButton' missing from VisualTree.");
            Assert.IsNotNull(root.Q<Label>("DescriptionLabel"), "Label 'DescriptionLabel' missing from VisualTree.");
        }

        [Test]
        public void Test_MainScene_UIDocument_WiredCorrectly()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var go = GameObject.Find("CircuitStudio"); // Based on YAML inspection (FileID 100 name)
            Assert.IsNotNull(go, "GameObject 'CircuitStudio' not found.");

            var uiDoc = go.GetComponent<UIDocument>();
            Assert.IsNotNull(uiDoc, "UIDocument component missing.");
            Assert.IsNotNull(uiDoc.visualTreeAsset, "UIDocument.visualTreeAsset is null.");
            Assert.IsNotNull(uiDoc.panelSettings, "UIDocument.panelSettings is null.");
        }

        [Test]
        public void Test_RunModeScene_UIDocument_WiredCorrectly()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RunMode.unity");
            var go = GameObject.Find("RunMode"); // Based on YAML inspection
            Assert.IsNotNull(go, "GameObject 'RunMode' not found.");

            var uiDoc = go.GetComponent<UIDocument>();
            Assert.IsNotNull(uiDoc, "UIDocument component missing.");
            Assert.IsNotNull(uiDoc.visualTreeAsset, "UIDocument.visualTreeAsset is null.");
            Assert.IsNotNull(uiDoc.panelSettings, "UIDocument.panelSettings is null.");
        }
    }
}
