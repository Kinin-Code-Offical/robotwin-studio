using UnityEngine;
using UnityEngine.UIElements;
using CoreSim.Catalogs;
using System.Collections.Generic;
using RobotTwin.Game;

namespace RobotTwin.UI
{
    public class ProjectWizardController : MonoBehaviour
    {
        public VisualTreeAsset TemplateItemAsset;
        private UIDocument _doc;
        private ListView _imgList;
        private Button _createButton;
        private Label _descriptionLabel;

        private List<TemplateDefinition> _templates;
        private TemplateDefinition _selectedTemplate;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            var root = _doc.rootVisualElement;

            // Assuming standard names for now
            _imgList = root.Q<ListView>("TemplateList");
            _createButton = root.Q<Button>("CreateButton");
            _descriptionLabel = root.Q<Label>("DescriptionLabel");

            _createButton.clicked += OnCreateClicked;

            LoadTemplates();
        }

        private void LoadTemplates()
        {
            _templates = TemplateCatalog.GetDefaults();
            
            // For MVP: Simple list populate (or just debug log if UI isn't built yet)
            Debug.Log($"Loaded {_templates.Count} templates.");
            
            // Auto-select first
            if (_templates.Count > 0)
            {
                SelectTemplate(_templates[0]);
            }
        }

        public void SelectTemplate(TemplateDefinition template)
        {
            _selectedTemplate = template;
            if (_descriptionLabel != null)
                _descriptionLabel.text = template.Description;
                
            Debug.Log($"Selected: {template.Name}");
        }

        private void OnCreateClicked()
        {
            if (_selectedTemplate != null)
            {
                SessionManager.Instance.InitializeSession(_selectedTemplate);
                // Load Main Scene (Index 1)
                UnityEngine.SceneManagement.SceneManager.LoadScene(1);
            }
        }
    }
}
