using UnityEngine;
using UnityEngine.UIElements;
using RobotTwin.CoreSim.Catalogs;
using RobotTwin.CoreSim.Specs;
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

        private List<TemplateSpec> _templates;
        private TemplateSpec _selectedTemplate;

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
            
            // Populate ListView
            if (_imgList != null)
            {
                _imgList.makeItem = () => new Label();
                _imgList.bindItem = (element, index) => 
                {
                    (element as Label).text = _templates[index].Name;
                };
                
                _imgList.itemsSource = _templates;
                _imgList.selectionChanged += OnSelectionChanged;

                // Refresh
                _imgList.Rebuild();
            }
            
            Debug.Log($"Loaded {_templates.Count} templates.");
            
            // Auto-select first if available
            if (_templates.Count > 0)
            {
                if (_imgList != null) _imgList.SetSelection(0);
                else SelectTemplate(_templates[0]);
            }
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            if (selection == null) return;
            foreach(var item in selection)
            {
                if (item is TemplateSpec t)
                {
                    SelectTemplate(t);
                    return; // Single select
                }
            }
        }

        public void SelectTemplate(TemplateSpec template)
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
