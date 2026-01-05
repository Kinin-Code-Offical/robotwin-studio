using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Serialization;
using RobotTwin.CoreSim.Specs;
using RobotTwin.Game;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
            public string DisplayPath;
            public string FullPath;
            public string Type;
            public DateTime ModifiedAt;
        }

        public struct Template
        {
            public string Title;
            public string Description;
            public string Tag;
            public string Difficulty;
            public string IconClass;
            public string SourcePath;
            public Color AccentColor;
        }

        [Serializable]
        private class RecentProjectEntry
        {
            public string Path;
            public long OpenedTicks;
        }

        [Serializable]
        private class RecentProjectList
        {
            public List<RecentProjectEntry> Items = new List<RecentProjectEntry>();
        }

        // Logic State
        private string _projectsPath;
        private const string ProjectRootKey = "ProjectWizard.ProjectRootPath";
        private const int RecentProjectLimit = 5;
        private const string RecentProjectsKey = "ProjectWizard.RecentProjects";

        private enum ProjectSortMode
        {
            ModifiedDesc,
            NameAsc
        }

        private ProjectSortMode _projectSortMode = ProjectSortMode.ModifiedDesc;
        private List<UserProject> _allProjects = new List<UserProject>();
        private List<UserProject> _recentProjects = new List<UserProject>();
        private string _projectFilter = string.Empty;
        private string _recentFilter = string.Empty;

        private TextField _projectRootField;
        private TextField _projectsSearchField;
        private TextField _recentSearchField;
        private Button _projectsFilterBtn;
        private Label _projectsFilterLabel;

        private VisualElement _contextMenu;
        private Button _contextDetailsBtn;
        private Button _contextDuplicateBtn;
        private Button _contextRemoveBtn;
        private UserProject _contextProject;
        private bool _hasContextProject;
        private VisualElement _selectedRow;

        private VisualElement _detailsOverlay;
        private Label _detailsName;
        private Label _detailsPath;
        private Label _detailsType;
        private Label _detailsModified;
        private Button _detailsCloseBtn;
        private Button _detailsOpenFolderBtn;
        private Button _detailsOpenProjectBtn;
        private UserProject _detailsProject;
        private bool _hasDetailsProject;

        private VisualElement _createOverlay;
        private Label _createTemplateLabel;
        private TextField _createNameField;
        private TextField _createPathField;
        private Button _createBrowseBtn;
        private Button _createCancelBtn;
        private Button _createConfirmBtn;
        private Button _createCloseBtn;
        private Template _pendingTemplate;
        private bool _hasPendingTemplate;

        private VisualElement _deleteOverlay;
        private Label _deleteMessage;
        private Button _deleteCancelBtn;
        private Button _deleteConfirmBtn;
        private Button _deleteCloseBtn;
        private UserProject _deleteProject;
        private bool _hasDeleteProject;

        private ScrollView _componentList;
        private Button _componentsAddBtn;

        private VisualElement _componentEditorOverlay;
        private Label _componentEditorTitle;
        private Button _componentEditorCloseBtn;
        private Button _componentEditorCancelBtn;
        private Button _componentEditorSaveBtn;
        private Button _componentModelBrowseBtn;
        private Button _componentConfigBrowseBtn;
        private Button _componentConfigLoadBtn;
        private Button _componentAddPinBtn;
        private Button _componentAddLabelBtn;
        private Button _componentAddSpecBtn;
        private Button _componentAddDefaultBtn;
        private VisualElement _componentPinsContainer;
        private VisualElement _componentLabelsContainer;
        private VisualElement _componentSpecsContainer;
        private VisualElement _componentDefaultsContainer;

        private string _componentEditorSourcePath;
        private ComponentCatalog.ComponentSource _componentEditorSource;
        private bool _componentEditorIsNew;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) { Debug.LogError("[ProjectWizard] Missing UIDocument"); return; }
            _root = _doc.rootVisualElement;
            if (_root == null) return;

            UiResponsive.Bind(_root, 1200f, 1600f, "wizard-compact", "wizard-medium", "wizard-wide");

            LoadProjectRoot();
            ComponentPackageUtility.MigrateUserJsonToPackages();

            InitializeUI();
        }

        private void InitializeUI()
        {
            // Navigation
            BindNavButton("BtnHome", "HomeView");
            BindNavButton("BtnProjects", "ProjectsView");
            BindNavButton("BtnTemplates", "TemplatesView");
            BindNavButton("BtnComponents", "ComponentsView");

            // Home View Actions
            var btnViewAll = _root.Q<Button>("BtnViewAll");
            if (btnViewAll != null) btnViewAll.clicked += () => SwitchTab("ProjectsView");

            var btnNew = _root.Q<Button>("NewProjectBtn");
            if (btnNew != null) btnNew.clicked += () => SwitchTab("TemplatesView");

            var btnOpen = _root.Q<Button>("OpenProjectBtn");
            if (btnOpen != null) btnOpen.clicked += OnOpenProjectClicked;

            var btnImport = _root.Q<Button>("ImportCadBtn");
            if (btnImport != null) btnImport.clicked += OnImportCadClicked;

            var btnCreate = _root.Q<Button>("CreateProjectBtn");
            if (btnCreate != null) btnCreate.clicked += () => SwitchTab("TemplatesView");

            _projectsFilterBtn = _root.Q<Button>("ProjectsFilterBtn");
            if (_projectsFilterBtn != null) _projectsFilterBtn.clicked += ToggleProjectSort;
            _projectsFilterLabel = _projectsFilterBtn?.Q<Label>("ProjectsFilterLabel");
            UpdateFilterButtonLabel();

            var btnRootApply = _root.Q<Button>("ProjectRootApplyBtn");
            if (btnRootApply != null) btnRootApply.clicked += ApplyProjectRoot;

            var btnRootOpen = _root.Q<Button>("ProjectRootOpenBtn");
            if (btnRootOpen != null) btnRootOpen.clicked += () => OpenInFileBrowser(_projectsPath);

            _projectsSearchField = _root.Q<TextField>("ProjectsSearchField");
            if (_projectsSearchField != null)
            {
                _projectsSearchField.isDelayed = false;
                _projectsSearchField.RegisterValueChangedCallback(evt =>
                {
                    _projectFilter = evt.newValue ?? string.Empty;
                    ApplyProjectFilter();
                });
                _projectsSearchField.RegisterCallback<KeyUpEvent>(_ =>
                {
                    _projectFilter = _projectsSearchField.value ?? string.Empty;
                    ApplyProjectFilter();
                });
            }

            _recentSearchField = _root.Q<TextField>("RecentSearchField");
            if (_recentSearchField != null)
            {
                _recentSearchField.RegisterValueChangedCallback(evt =>
                {
                    _recentFilter = evt.newValue ?? string.Empty;
                    ApplyRecentFilter();
                });
            }

            _projectRootField = _root.Q<TextField>("ProjectRootField");
            if (_projectRootField != null)
            {
                _projectRootField.SetValueWithoutNotify(_projectsPath);
            }

            _contextMenu = _root.Q<VisualElement>("ContextMenu");
            _contextDetailsBtn = _root.Q<Button>("ContextDetailsBtn");
            _contextDuplicateBtn = _root.Q<Button>("ContextDuplicateBtn");
            _contextRemoveBtn = _root.Q<Button>("ContextRemoveBtn");

            if (_contextDetailsBtn != null) _contextDetailsBtn.clicked += () => HandleContextDetails();
            if (_contextDuplicateBtn != null) _contextDuplicateBtn.clicked += () => HandleContextDuplicate();
            if (_contextRemoveBtn != null) _contextRemoveBtn.clicked += () => HandleContextRemove();

            _detailsOverlay = _root.Q<VisualElement>("DetailsOverlay");
            _detailsName = _root.Q<Label>("DetailsName");
            _detailsPath = _root.Q<Label>("DetailsPath");
            _detailsType = _root.Q<Label>("DetailsType");
            _detailsModified = _root.Q<Label>("DetailsModified");
            _detailsCloseBtn = _root.Q<Button>("DetailsCloseBtn");
            _detailsOpenFolderBtn = _root.Q<Button>("DetailsOpenFolderBtn");
            _detailsOpenProjectBtn = _root.Q<Button>("DetailsOpenProjectBtn");

            if (_detailsCloseBtn != null) _detailsCloseBtn.clicked += HideDetails;
            if (_detailsOpenFolderBtn != null) _detailsOpenFolderBtn.clicked += () =>
            {
                if (_hasDetailsProject) OpenInBrowser(_detailsProject.FullPath);
            };
            if (_detailsOpenProjectBtn != null) _detailsOpenProjectBtn.clicked += () =>
            {
                if (_hasDetailsProject) OpenProjectFile(_detailsProject.FullPath);
            };

            if (_detailsOverlay != null)
            {
                _detailsOverlay.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.target == _detailsOverlay) HideDetails();
                });
            }

            _createOverlay = _root.Q<VisualElement>("TemplateCreateOverlay");
            _createTemplateLabel = _root.Q<Label>("TemplateCreateTemplateLabel");
            _createNameField = _root.Q<TextField>("TemplateProjectNameField");
            _createPathField = _root.Q<TextField>("TemplateProjectPathField");
            _createBrowseBtn = _root.Q<Button>("TemplateProjectBrowseBtn");
            _createCancelBtn = _root.Q<Button>("TemplateCreateCancelBtn");
            _createConfirmBtn = _root.Q<Button>("TemplateCreateConfirmBtn");
            _createCloseBtn = _root.Q<Button>("TemplateCreateCloseBtn");

            if (_createBrowseBtn != null) _createBrowseBtn.clicked += BrowseForTemplateLocation;
            if (_createCancelBtn != null) _createCancelBtn.clicked += HideCreateOverlay;
            if (_createCloseBtn != null) _createCloseBtn.clicked += HideCreateOverlay;
            if (_createConfirmBtn != null) _createConfirmBtn.clicked += CreateProjectFromTemplate;

            if (_createOverlay != null)
            {
                _createOverlay.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.target == _createOverlay) HideCreateOverlay();
                });
            }

            _deleteOverlay = _root.Q<VisualElement>("DeleteConfirmOverlay");
            _deleteMessage = _root.Q<Label>("DeleteConfirmMessage");
            _deleteCancelBtn = _root.Q<Button>("DeleteConfirmCancelBtn");
            _deleteConfirmBtn = _root.Q<Button>("DeleteConfirmBtn");
            _deleteCloseBtn = _root.Q<Button>("DeleteConfirmCloseBtn");

            if (_deleteCancelBtn != null) _deleteCancelBtn.clicked += HideDeleteConfirm;
            if (_deleteCloseBtn != null) _deleteCloseBtn.clicked += HideDeleteConfirm;
            if (_deleteConfirmBtn != null) _deleteConfirmBtn.clicked += ConfirmDeleteProject;

            if (_deleteOverlay != null)
            {
                _deleteOverlay.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.target == _deleteOverlay) HideDeleteConfirm();
                });
            }

            _componentList = _root.Q<ScrollView>("ComponentListContainer");
            _componentsAddBtn = _root.Q<Button>("ComponentsAddBtn");
            if (_componentsAddBtn != null) _componentsAddBtn.clicked += () => OpenComponentStudio(CreateDefaultComponentDefinition(), true, null);

            _componentEditorOverlay = _root.Q<VisualElement>("ComponentEditorOverlay");
            _componentEditorTitle = _root.Q<Label>("ComponentEditorTitle");
            _componentEditorCloseBtn = _root.Q<Button>("ComponentEditorCloseBtn");
            _componentEditorCancelBtn = _root.Q<Button>("ComponentEditorCancelBtn");
            _componentEditorSaveBtn = _root.Q<Button>("ComponentEditorSaveBtn");
            _componentModelBrowseBtn = _root.Q<Button>("ComponentModelBrowseBtn");
            _componentConfigBrowseBtn = _root.Q<Button>("ComponentConfigBrowseBtn");
            _componentConfigLoadBtn = _root.Q<Button>("ComponentConfigLoadBtn");
            _componentAddPinBtn = _root.Q<Button>("ComponentAddPinBtn");
            _componentAddLabelBtn = _root.Q<Button>("ComponentAddLabelBtn");
            _componentAddSpecBtn = _root.Q<Button>("ComponentAddSpecBtn");
            _componentAddDefaultBtn = _root.Q<Button>("ComponentAddDefaultBtn");
            _componentPinsContainer = _root.Q<VisualElement>("ComponentPinsContainer");
            _componentLabelsContainer = _root.Q<VisualElement>("ComponentLabelsContainer");
            _componentSpecsContainer = _root.Q<VisualElement>("ComponentSpecsContainer");
            _componentDefaultsContainer = _root.Q<VisualElement>("ComponentDefaultsContainer");

            if (_componentEditorCloseBtn != null) _componentEditorCloseBtn.clicked += HideComponentEditor;
            if (_componentEditorCancelBtn != null) _componentEditorCancelBtn.clicked += HideComponentEditor;
            if (_componentEditorSaveBtn != null) _componentEditorSaveBtn.clicked += SaveComponentDefinition;
            if (_componentModelBrowseBtn != null) _componentModelBrowseBtn.clicked += BrowseForComponentModel;
            if (_componentConfigBrowseBtn != null) _componentConfigBrowseBtn.clicked += BrowseForComponentConfig;
            if (_componentConfigLoadBtn != null) _componentConfigLoadBtn.clicked += LoadComponentConfigFromField;
            if (_componentAddPinBtn != null) _componentAddPinBtn.clicked += () => AddPinRow(new PinLayoutPayload { name = GetNextPinName() });
            if (_componentAddLabelBtn != null) _componentAddLabelBtn.clicked += () => AddLabelRow(new LabelLayoutPayload { text = "{id}", x = 0.5f, y = 0.5f, size = 10, align = "center" });
            if (_componentAddSpecBtn != null) _componentAddSpecBtn.clicked += () => AddKeyValueRow(_componentSpecsContainer, string.Empty, string.Empty);
            if (_componentAddDefaultBtn != null) _componentAddDefaultBtn.clicked += () => AddKeyValueRow(_componentDefaultsContainer, string.Empty, string.Empty);

            if (_componentEditorOverlay != null)
            {
                _componentEditorOverlay.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.target == _componentEditorOverlay) HideComponentEditor();
                });
            }

            _root.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (_detailsOverlay != null && _detailsOverlay.style.display == DisplayStyle.Flex)
                {
                    if (evt.target == _detailsOverlay) HideDetails();
                }
            });

            _root.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (_contextMenu != null && _contextMenu.style.display == DisplayStyle.Flex)
                {
                    if (evt.target is VisualElement ve && IsElementInContextMenu(ve)) return;
                    HideContextMenu();
                }
            });

            _root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                HideContextMenu();
                HideDetails();
                HideCreateOverlay();
                HideDeleteConfirm();
                HideComponentEditor();
                _root.Focus();
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            _root.focusable = true;
            _root.Focus();

            // Initial Data
            PopulateHome();
            PopulateProjects();
            PopulateTemplates();
            PopulateComponents();

            // Default Tab
            SwitchTab("HomeView");
        }

        private void SwitchTab(string viewName)
        {
            // Toggle Views
            SetDisplay("HomeView", viewName == "HomeView");
            SetDisplay("ProjectsView", viewName == "ProjectsView");
            SetDisplay("TemplatesView", viewName == "TemplatesView");
            SetDisplay("ComponentsView", viewName == "ComponentsView");

            // Update Sidebar State
            SetActiveNav("BtnHome", viewName == "HomeView");
            SetActiveNav("BtnProjects", viewName == "ProjectsView");
            SetActiveNav("BtnTemplates", viewName == "TemplatesView");
            SetActiveNav("BtnComponents", viewName == "ComponentsView");

            HideContextMenu();
            HideDetails();
            HideCreateOverlay();
            HideDeleteConfirm();
            HideComponentEditor();
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

        private void LoadProjectRoot()
        {
            string saved = PlayerPrefs.GetString(ProjectRootKey, string.Empty);
            _projectsPath = string.IsNullOrWhiteSpace(saved)
                ? Path.Combine(Application.persistentDataPath, "Projects")
                : saved;

            try
            {
                _projectsPath = Path.GetFullPath(_projectsPath);
            }
            catch
            {
                _projectsPath = Path.Combine(Application.persistentDataPath, "Projects");
            }

            EnsureProjectRoot();
        }

        private void EnsureProjectRoot()
        {
            if (!Directory.Exists(_projectsPath)) Directory.CreateDirectory(_projectsPath);
        }

        private void ApplyProjectRoot()
        {
            if (_projectRootField == null) return;
            string nextPath = _projectRootField.value?.Trim();
            if (string.IsNullOrWhiteSpace(nextPath)) return;

            try
            {
                nextPath = Path.GetFullPath(nextPath);
                Directory.CreateDirectory(nextPath);
                _projectsPath = nextPath;
                if (_projectRootField != null)
                {
                    _projectRootField.SetValueWithoutNotify(_projectsPath);
                }
                PlayerPrefs.SetString(ProjectRootKey, _projectsPath);
                PlayerPrefs.Save();
                PopulateHome();
                PopulateProjects();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectWizard] Failed to set project root: {ex.Message}");
            }
        }

        private void ToggleProjectSort()
        {
            _projectSortMode = _projectSortMode == ProjectSortMode.ModifiedDesc
                ? ProjectSortMode.NameAsc
                : ProjectSortMode.ModifiedDesc;
            UpdateFilterButtonLabel();
            ApplyProjectFilter();
        }

        private void UpdateFilterButtonLabel()
        {
            string label = _projectSortMode == ProjectSortMode.ModifiedDesc ? "Modified" : "Name";
            if (_projectsFilterLabel != null)
            {
                _projectsFilterLabel.text = label;
                return;
            }

            if (_projectsFilterBtn != null)
            {
                _projectsFilterBtn.text = label;
            }
        }

        private List<UserProject> LoadProjects()
        {
            var projects = new List<UserProject>();
            if (!Directory.Exists(_projectsPath)) return projects;

            foreach (var file in Directory.EnumerateFiles(_projectsPath, "*.rtwin", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                var name = Path.GetFileNameWithoutExtension(file);
                var displayPath = GetDisplayPath(file);

                projects.Add(new UserProject
                {
                    Name = name,
                    DisplayPath = displayPath,
                    FullPath = file,
                    Type = GuessProjectType(name),
                    ModifiedAt = info.LastWriteTime
                });
            }

            return projects;
        }

        private string NormalizeProjectPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return string.Empty;
            try
            {
                return Path.GetFullPath(filePath);
            }
            catch
            {
                return filePath;
            }
        }

        private List<RecentProjectEntry> LoadRecentEntries()
        {
            string raw = PlayerPrefs.GetString(RecentProjectsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw)) return new List<RecentProjectEntry>();

            try
            {
                var list = JsonUtility.FromJson<RecentProjectList>(raw);
                return list?.Items ?? new List<RecentProjectEntry>();
            }
            catch
            {
                return new List<RecentProjectEntry>();
            }
        }

        private void SaveRecentEntries(List<RecentProjectEntry> entries)
        {
            if (entries == null) entries = new List<RecentProjectEntry>();
            var cleaned = new List<RecentProjectEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries.OrderByDescending(e => e?.OpenedTicks ?? 0))
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Path)) continue;
                string path = NormalizeProjectPath(entry.Path);
                if (!seen.Add(path)) continue;
                cleaned.Add(new RecentProjectEntry
                {
                    Path = path,
                    OpenedTicks = entry.OpenedTicks
                });
                if (cleaned.Count >= RecentProjectLimit) break;
            }

            var payload = new RecentProjectList { Items = cleaned };
            string json = JsonUtility.ToJson(payload);
            PlayerPrefs.SetString(RecentProjectsKey, json);
            PlayerPrefs.Save();
        }

        private void RecordRecentProject(string filePath)
        {
            string path = NormalizeProjectPath(filePath);
            if (string.IsNullOrWhiteSpace(path)) return;

            var entries = LoadRecentEntries();
            entries.RemoveAll(e => e == null || string.IsNullOrWhiteSpace(e.Path) ||
                                   string.Equals(NormalizeProjectPath(e.Path), path, StringComparison.OrdinalIgnoreCase));
            entries.Insert(0, new RecentProjectEntry
            {
                Path = path,
                OpenedTicks = DateTime.UtcNow.Ticks
            });
            SaveRecentEntries(entries);
        }

        private void RemoveRecentProject(string filePath)
        {
            string path = NormalizeProjectPath(filePath);
            if (string.IsNullOrWhiteSpace(path)) return;
            var entries = LoadRecentEntries();
            entries.RemoveAll(e => e == null || string.IsNullOrWhiteSpace(e.Path) ||
                                   string.Equals(NormalizeProjectPath(e.Path), path, StringComparison.OrdinalIgnoreCase));
            SaveRecentEntries(entries);
        }

        private void UpdateRecentProjectPath(string oldPath, string newPath)
        {
            string oldNormalized = NormalizeProjectPath(oldPath);
            string newNormalized = NormalizeProjectPath(newPath);
            if (string.IsNullOrWhiteSpace(oldNormalized) || string.IsNullOrWhiteSpace(newNormalized)) return;

            var entries = LoadRecentEntries();
            bool updated = false;
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Path)) continue;
                if (!string.Equals(NormalizeProjectPath(entry.Path), oldNormalized, StringComparison.OrdinalIgnoreCase)) continue;
                entry.Path = newNormalized;
                updated = true;
            }

            if (updated) SaveRecentEntries(entries);
        }

        private List<UserProject> LoadRecentProjects()
        {
            var entries = LoadRecentEntries();
            if (entries.Count == 0) return new List<UserProject>();

            var ordered = entries.OrderByDescending(e => e?.OpenedTicks ?? 0).ToList();
            var cleaned = new List<RecentProjectEntry>();
            var projects = new List<UserProject>();

            foreach (var entry in ordered)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Path)) continue;
                string path = NormalizeProjectPath(entry.Path);
                if (!File.Exists(path)) continue;

                var info = new FileInfo(path);
                string name = Path.GetFileNameWithoutExtension(path);
                projects.Add(new UserProject
                {
                    Name = name,
                    DisplayPath = GetDisplayPath(path),
                    FullPath = path,
                    Type = GuessProjectType(name),
                    ModifiedAt = info.LastWriteTime
                });

                cleaned.Add(new RecentProjectEntry { Path = path, OpenedTicks = entry.OpenedTicks });
                if (projects.Count >= RecentProjectLimit) break;
            }

            SaveRecentEntries(cleaned);
            return projects;
        }

        private string GetDisplayPath(string filePath)
        {
            try
            {
                return Path.GetRelativePath(_projectsPath, filePath);
            }
            catch
            {
                return filePath;
            }
        }

        private string GuessProjectType(string name)
        {
            string lower = name.ToLowerInvariant();
            if (lower.Contains("pcb")) return "PCB Design";
            if (lower.Contains("arm") || lower.Contains("robot") || lower.Contains("bot")) return "Robotics";
            return "Simulation";
        }

        private void ApplyProjectFilter()
        {
            var list = _root.Q<ScrollView>("ProjectListContainer");
            if (list == null) return;
            list.Clear();
            HideContextMenu();

            IEnumerable<UserProject> filtered = _allProjects;
            string query = _projectFilter?.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(p =>
                    ContainsIgnoreCase(p.Name, query) ||
                    ContainsIgnoreCase(p.DisplayPath, query) ||
                    ContainsIgnoreCase(p.Type, query));
            }

            filtered = _projectSortMode == ProjectSortMode.NameAsc
                ? filtered.OrderBy(p => p.Name)
                : filtered.OrderByDescending(p => p.ModifiedAt);

            var results = filtered.ToList();
            if (results.Count == 0)
            {
                list.Add(CreateEmptyState("No projects found."));
                return;
            }

            foreach (var proj in results)
            {
                list.Add(CreateProjectRow(proj));
            }
        }

        private void ApplyRecentFilter()
        {
            var list = _root.Q<ScrollView>("RecentProjectsList");
            if (list == null) return;
            list.Clear();
            HideContextMenu();

            IEnumerable<UserProject> filtered = _recentProjects;
            string query = _recentFilter?.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(p =>
                    ContainsIgnoreCase(p.Name, query) ||
                    ContainsIgnoreCase(p.DisplayPath, query) ||
                    ContainsIgnoreCase(p.Type, query));
            }

            var results = filtered.ToList();
            if (results.Count == 0)
            {
                list.Add(CreateEmptyState("No recent projects found."));
                return;
            }

            foreach (var proj in results)
            {
                list.Add(CreateRecentRow(proj));
            }
        }

        private void HandleContextDetails()
        {
            if (!_hasContextProject) return;
            ShowDetails(_contextProject);
            HideContextMenu();
        }

        private void HandleContextDuplicate()
        {
            if (!_hasContextProject) return;
            DuplicateProject(_contextProject);
            HideContextMenu();
        }

        private void HandleContextRemove()
        {
            if (!_hasContextProject) return;
            ShowDeleteConfirm(_contextProject);
            HideContextMenu();
        }

        private void ShowContextMenu(UserProject proj, Vector2 worldPosition, VisualElement row)
        {
            if (_contextMenu == null || _root == null) return;

            _contextProject = proj;
            _hasContextProject = true;
            SetSelectedRow(row);
            HideDetails();
            HideCreateOverlay();
            HideDeleteConfirm();

            _contextMenu.style.display = DisplayStyle.Flex;
            _contextMenu.BringToFront();
            Vector2 local = _root.WorldToLocal(worldPosition);

            float menuWidth = _contextMenu.resolvedStyle.width;
            float menuHeight = _contextMenu.resolvedStyle.height;
            if (menuWidth <= 0f) menuWidth = 160f;
            if (menuHeight <= 0f) menuHeight = 110f;

            float maxX = Mathf.Max(6f, _root.layout.width - menuWidth - 6f);
            float maxY = Mathf.Max(6f, _root.layout.height - menuHeight - 6f);
            float x = Mathf.Clamp(local.x, 6f, maxX);
            float y = Mathf.Clamp(local.y, 6f, maxY);

            _contextMenu.style.left = x;
            _contextMenu.style.top = y;
        }

        private Vector2 GetMenuAnchor(VisualElement anchor)
        {
            if (anchor == null) return Vector2.zero;
            var bounds = anchor.worldBound;
            return new Vector2(bounds.xMax - 4f, bounds.yMax + 4f);
        }

        private void HideContextMenu()
        {
            if (_contextMenu != null) _contextMenu.style.display = DisplayStyle.None;
            _hasContextProject = false;
            SetSelectedRow(null);
        }

        private void SetSelectedRow(VisualElement row)
        {
            if (_selectedRow != null) _selectedRow.RemoveFromClassList("selected");
            _selectedRow = row;
            if (_selectedRow != null) _selectedRow.AddToClassList("selected");
        }

        private bool IsElementInContextMenu(VisualElement element)
        {
            if (_contextMenu == null || element == null) return false;
            var current = element;
            while (current != null)
            {
                if (current == _contextMenu) return true;
                current = current.parent;
            }
            return false;
        }

        private void ShowDetails(UserProject proj)
        {
            _detailsProject = proj;
            _hasDetailsProject = true;
            HideCreateOverlay();
            HideDeleteConfirm();
            if (_detailsOverlay != null)
            {
                _detailsOverlay.style.display = DisplayStyle.Flex;
                _detailsOverlay.BringToFront();
            }

            if (_detailsName != null) _detailsName.text = $"Name: {proj.Name}";
            if (_detailsPath != null) _detailsPath.text = $"Path: {proj.FullPath}";
            if (_detailsType != null) _detailsType.text = $"Type: {proj.Type}";
            if (_detailsModified != null) _detailsModified.text = $"Modified: {proj.ModifiedAt:MMM dd, HH:mm}";
        }

        private void HideDetails()
        {
            if (_detailsOverlay != null) _detailsOverlay.style.display = DisplayStyle.None;
            _hasDetailsProject = false;
        }

        private void ShowCreateOverlay(Template tmpl)
        {
            _pendingTemplate = tmpl;
            _hasPendingTemplate = true;
            HideContextMenu();
            HideDetails();

            if (_createOverlay != null)
            {
                _createOverlay.style.display = DisplayStyle.Flex;
                _createOverlay.BringToFront();
            }

            if (_createTemplateLabel != null) _createTemplateLabel.text = $"Template: {tmpl.Title}";
            if (_createNameField != null)
            {
                _createNameField.SetValueWithoutNotify(tmpl.Title);
                _createNameField.Focus();
                _createNameField.SelectAll();
            }

            if (_createPathField != null)
            {
                _createPathField.SetValueWithoutNotify(_projectsPath);
            }
        }

        private void HideCreateOverlay()
        {
            if (_createOverlay != null) _createOverlay.style.display = DisplayStyle.None;
            _hasPendingTemplate = false;
        }

        private void ShowDeleteConfirm(UserProject proj)
        {
            _deleteProject = proj;
            _hasDeleteProject = true;
            HideContextMenu();
            HideDetails();
            HideCreateOverlay();

            if (_deleteMessage != null) _deleteMessage.text = $"Delete \"{proj.Name}\"?";
            if (_deleteOverlay != null)
            {
                _deleteOverlay.style.display = DisplayStyle.Flex;
                _deleteOverlay.BringToFront();
            }
        }

        private void HideDeleteConfirm()
        {
            if (_deleteOverlay != null) _deleteOverlay.style.display = DisplayStyle.None;
            _hasDeleteProject = false;
        }

        private void ConfirmDeleteProject()
        {
            if (!_hasDeleteProject) return;
            RemoveProject(_deleteProject);
            HideDeleteConfirm();
        }

        private void DuplicateProject(UserProject proj)
        {
            if (string.IsNullOrWhiteSpace(proj.FullPath) || !File.Exists(proj.FullPath)) return;

            string dir = Path.GetDirectoryName(proj.FullPath) ?? _projectsPath;
            string name = Path.GetFileNameWithoutExtension(proj.FullPath);
            string ext = Path.GetExtension(proj.FullPath);
            string copyPath = Path.Combine(dir, $"{name}_copy{ext}");
            copyPath = EnsureUniqueProjectPath(copyPath);
            string workspaceSource = ResolveProjectWorkspaceRoot(proj.FullPath);
            string workspaceTarget = ResolveProjectWorkspaceRoot(copyPath);

            try
            {
                File.Copy(proj.FullPath, copyPath);
                if (!string.IsNullOrWhiteSpace(workspaceSource) && Directory.Exists(workspaceSource))
                {
                    CopyDirectoryRecursive(workspaceSource, workspaceTarget);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectWizard] Failed to duplicate project: {ex.Message}");
                return;
            }

            PopulateHome();
            PopulateProjects();
        }

        private void RemoveProject(UserProject proj)
        {
            if (string.IsNullOrWhiteSpace(proj.FullPath) || !File.Exists(proj.FullPath)) return;

            try
            {
                File.Delete(proj.FullPath);
                string workspaceRoot = ResolveProjectWorkspaceRoot(proj.FullPath);
                if (!string.IsNullOrWhiteSpace(workspaceRoot) && Directory.Exists(workspaceRoot))
                {
                    Directory.Delete(workspaceRoot, true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectWizard] Failed to remove project: {ex.Message}");
                return;
            }

            RemoveRecentProject(proj.FullPath);
            HideDetails();
            PopulateHome();
            PopulateProjects();
        }

        private bool TryRenameProject(UserProject project, string newName, out UserProject updated)
        {
            updated = project;
            string safeName = SanitizeProjectName(newName);
            if (string.IsNullOrWhiteSpace(safeName)) return false;

            string dir = Path.GetDirectoryName(project.FullPath) ?? _projectsPath;
            string ext = Path.GetExtension(project.FullPath);
            string targetPath = Path.Combine(dir, $"{safeName}{ext}");

            if (string.Equals(project.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                updated.Name = safeName;
                return true;
            }

            targetPath = EnsureUniqueProjectPath(targetPath);
            string sourceWorkspace = ResolveProjectWorkspaceRoot(project.FullPath);
            string targetWorkspace = ResolveProjectWorkspaceRoot(targetPath);

            try
            {
                File.Move(project.FullPath, targetPath);
                if (!string.IsNullOrWhiteSpace(sourceWorkspace) && Directory.Exists(sourceWorkspace))
                {
                    Directory.Move(sourceWorkspace, targetWorkspace);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectWizard] Failed to rename project: {ex.Message}");
                return false;
            }

            updated.Name = safeName;
            updated.FullPath = targetPath;
            updated.DisplayPath = GetDisplayPath(targetPath);
            updated.ModifiedAt = File.GetLastWriteTime(targetPath);

            UpdateRecentProjectPath(project.FullPath, targetPath);
            HideDetails();
            PopulateHome();
            PopulateProjects();
            return true;
        }

        private string SanitizeProjectName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = name.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (invalid.Contains(chars[i])) chars[i] = '_';
            }

            return new string(chars).Trim();
        }

        private string EnsureUniqueProjectPath(string targetPath)
        {
            if (!File.Exists(targetPath) && !Directory.Exists(ResolveProjectWorkspaceRoot(targetPath))) return targetPath;

            string dir = Path.GetDirectoryName(targetPath) ?? _projectsPath;
            string name = Path.GetFileNameWithoutExtension(targetPath);
            string ext = Path.GetExtension(targetPath);
            int index = 1;
            string candidate = targetPath;

            while (File.Exists(candidate) || Directory.Exists(ResolveProjectWorkspaceRoot(candidate)))
            {
                candidate = Path.Combine(dir, $"{name}_{index}{ext}");
                index++;
            }

            return candidate;
        }

        private bool ContainsIgnoreCase(string value, string query)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private VisualElement CreateRecentRow(UserProject proj)
        {
            var row = new VisualElement();
            row.AddToClassList("recent-row-home");
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0 || evt.target is Button) return;
                OpenProjectFile(proj.FullPath);
            });
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1 && evt.button != 2) return;
                ShowContextMenu(proj, row.LocalToWorld(evt.localPosition), row);
                evt.StopPropagation();
            });

            var iconBox = new VisualElement();
            iconBox.AddToClassList("recent-icon-box");
            var iconImg = new VisualElement();
            iconImg.AddToClassList("recent-icon-img");

            if (proj.Type == "PCB Design") iconImg.style.unityBackgroundImageTintColor = new StyleColor(new Color(0f, 0.8f, 0.5f));
            else if (proj.Type == "Robotics") iconImg.style.unityBackgroundImageTintColor = new StyleColor(new Color(1f, 0.6f, 0f));
            else iconImg.style.unityBackgroundImageTintColor = new StyleColor(new Color(0.3f, 0.6f, 0.9f));

            iconImg.AddToClassList("icon-activity");
            if (proj.Type == "PCB Design") iconImg.AddToClassList("icon-cpu");
            else if (proj.Type == "Robotics") iconImg.AddToClassList("icon-hexagon");

            iconBox.Add(iconImg);
            row.Add(iconBox);

            var metaCol = new VisualElement();
            metaCol.AddToClassList("recent-meta-col");
            var lblName = new Label(proj.Name);
            lblName.AddToClassList("recent-title");
            var lblSub = new Label($"{proj.Type} - {proj.ModifiedAt:MMM dd, HH:mm}");
            lblSub.AddToClassList("recent-subtitle");
            metaCol.Add(lblName);
            metaCol.Add(lblSub);
            row.Add(metaCol);

            var pathCol = new VisualElement();
            pathCol.AddToClassList("recent-path-col");
            var lblPath = new Label(proj.DisplayPath);
            lblPath.AddToClassList("recent-path-label");
            pathCol.Add(lblPath);
            row.Add(pathCol);

            return row;
        }

        private VisualElement CreateEmptyState(string message)
        {
            var label = new Label(message);
            label.AddToClassList("empty-state");
            return label;
        }

        private void OnOpenProjectClicked()
        {
#if UNITY_EDITOR
            string filePath = EditorUtility.OpenFilePanel("Open Project", _projectsPath, "rtwin");
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                OpenProjectFile(filePath);
                return;
            }
#endif
            SwitchTab("ProjectsView");
        }

        private void OnImportCadClicked()
        {
#if UNITY_EDITOR
            string filePath = EditorUtility.OpenFilePanel("Import CAD/EDA", _projectsPath, "step,stp,kicad_pcb");
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                OpenInFileBrowser(Path.GetDirectoryName(filePath) ?? _projectsPath);
                return;
            }
#endif
            OpenInFileBrowser(_projectsPath);
        }

        private void OpenProjectFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Debug.LogWarning("[ProjectWizard] Project file not found.");
                return;
            }

            ProjectManifest project = null;
            try
            {
                project = SimulationSerializer.LoadProject(filePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectWizard] Failed to load project: {ex.Message}");
                OpenInBrowser(filePath);
                return;
            }

            if (project == null)
            {
                Debug.LogWarning("[ProjectWizard] Failed to load project.");
                OpenInBrowser(filePath);
                return;
            }

            if (SessionManager.Instance != null)
            {
                SessionManager.Instance.StartSession(project, filePath);
            }

            RecordRecentProject(filePath);

            HideContextMenu();
            HideDetails();

            int sceneCount = SceneManager.sceneCountInBuildSettings;
            if (sceneCount > 1) SceneManager.LoadScene("Main");
            else Debug.LogWarning("[ProjectWizard] Scene 1 not found.");
        }

        private void OpenInBrowser(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            string target = Directory.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(target)) return;
            OpenInFileBrowser(target);
        }

        private void OpenInFileBrowser(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectWizard] Failed to open path: {ex.Message}");
                Application.OpenURL(path);
            }
        }

        private List<Template> LoadTemplates()
        {
            var templates = new List<Template>();
            templates.Add(CreateEmptyTemplate());
            foreach (var root in GetTemplateRoots())
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var specPath = Path.Combine(dir, "template.json");
                    if (File.Exists(specPath))
                    {
                        try
                        {
                            var specJson = File.ReadAllText(specPath);
                            var spec = SimulationSerializer.Deserialize<TemplateSpec>(specJson);
                            if (spec != null && !string.IsNullOrWhiteSpace(spec.DisplayName))
                            {
                                templates.Add(new Template
                                {
                                    Title = spec.DisplayName,
                                    Description = spec.Description ?? string.Empty,
                                    Tag = ResolveTemplateTagFromSpec(spec),
                                    Difficulty = string.Empty,
                                    IconClass = string.Empty,
                                    SourcePath = dir,
                                    AccentColor = Color.gray
                                });
                                continue;
                            }
                        }
                        catch
                        {
                            // Fall back to metadata.json
                        }
                    }

                    var metadataPath = Path.Combine(dir, "metadata.json");
                    if (!File.Exists(metadataPath)) continue;

                    TemplateMetadata meta;
                    try
                    {
                        meta = JsonUtility.FromJson<TemplateMetadata>(File.ReadAllText(metadataPath));
                    }
                    catch
                    {
                        continue;
                    }

                    if (meta == null) continue;

                    string templateName = FirstNonEmpty(meta.name, meta.Name, meta.displayName, meta.DisplayName, meta.title, meta.Title);
                    if (string.IsNullOrWhiteSpace(templateName)) continue;
                    string description = FirstNonEmpty(meta.description, meta.Description);
                    string tag = FirstNonEmpty(meta.tag, meta.Tag);
                    string difficulty = FirstNonEmpty(meta.difficulty, meta.Difficulty);

                    templates.Add(new Template
                    {
                        Title = templateName,
                        Description = description ?? string.Empty,
                        Tag = tag ?? string.Empty,
                        Difficulty = difficulty ?? string.Empty,
                        IconClass = string.Empty,
                        SourcePath = dir,
                        AccentColor = Color.gray
                    });
                }
            }

            for (int i = 0; i < templates.Count; i++)
            {
                templates[i] = ApplyTemplateDefaults(templates[i]);
            }

            return templates;
        }

        private IEnumerable<string> GetTemplateRoots()
        {
            var roots = new List<string>
            {
                Path.Combine(Application.dataPath, "Templates"),
                Path.Combine(Application.persistentDataPath, "Templates"),
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Reference_Design", "React_Source", "templates"))
            };

            return roots.Where(Directory.Exists).Distinct();
        }

        private Template CreateEmptyTemplate()
        {
            return new Template
            {
                Title = "Empty Template",
                Description = "Start from a blank project.",
                Tag = "EMPTY",
                Difficulty = "Beginner",
                IconClass = "icon-plus",
                SourcePath = string.Empty,
                AccentColor = new Color(0.55f, 0.6f, 0.7f)
            };
        }

        private Template ApplyTemplateDefaults(Template template)
        {
            string tag = string.IsNullOrWhiteSpace(template.Tag)
                ? ResolveTemplateTag(template.Title)
                : template.Tag.ToUpperInvariant();

            template.Tag = tag;
            if (string.IsNullOrWhiteSpace(template.Difficulty))
            {
                template.Difficulty = ResolveTemplateDifficulty(template.Title, tag);
            }

            if (string.IsNullOrWhiteSpace(template.IconClass))
            {
                template.IconClass = ResolveTemplateIcon(template.Title, tag);
            }

            if (template.AccentColor == default)
            {
                template.AccentColor = ResolveTemplateAccentColor(tag);
            }
            return template;
        }

        private static string ResolveTemplateTagFromSpec(TemplateSpec spec)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.SystemType)) return string.Empty;
            string systemType = spec.SystemType.Trim().ToUpperInvariant();
            if (systemType == "ROBOT" || systemType == "ROBOTICS")
            {
                return "ROBOTICS";
            }
            if (systemType == "CIRCUITONLY")
            {
                return "PCB DESIGN";
            }
            if (systemType == "MECHATRONIC")
            {
                return "ROBOTICS";
            }
            return systemType;
        }

        private string ResolveTemplateTag(string title)
        {
            string lower = (title ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("pcb") || lower.Contains("eda") || lower.Contains("cad")) return "PCB DESIGN";
            if (lower.Contains("robot") || lower.Contains("arm") || lower.Contains("bot")) return "ROBOTICS";
            if (lower.Contains(" ai ") || lower.Contains(" ai") || lower.Contains("ml") || lower.Contains("learning") || lower.Contains("rl")) return "AI / ML";
            return "SIMULATION";
        }

        private string ResolveTemplateDifficulty(string title, string tag)
        {
            string lower = (title ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("advanced")) return "Advanced";
            if (lower.Contains("basic") || lower.Contains("starter")) return "Beginner";
            if (tag == "AI / ML") return "Advanced";
            return "Intermediate";
        }

        private string ResolveTemplateIcon(string title, string tag)
        {
            switch (tag)
            {
                case "PCB DESIGN":
                    return "icon-cpu";
                case "ROBOTICS":
                    return "icon-hexagon";
                case "AI / ML":
                    return "icon-zap";
                case "EMPTY":
                    return "icon-plus";
                default:
                    return "icon-activity";
            }
        }

        private Color ResolveTemplateAccentColor(string tag)
        {
            switch (tag)
            {
                case "PCB DESIGN":
                    return new Color(0f, 0.8f, 0.5f);
                case "ROBOTICS":
                    return new Color(1f, 0.6f, 0f);
                case "AI / ML":
                    return new Color(0.7f, 0.3f, 1f);
                case "EMPTY":
                    return new Color(0.55f, 0.6f, 0.7f);
                default:
                    return new Color(0.2f, 0.6f, 1f);
            }
        }

        [Serializable]
        private class TemplateMetadata
        {
            public string name;
            public string Name;
            public string displayName;
            public string DisplayName;
            public string title;
            public string Title;
            public string description;
            public string Description;
            public string tag;
            public string Tag;
            public string difficulty;
            public string Difficulty;
        }

        private static string FirstNonEmpty(params string[] candidates)
        {
            if (candidates == null) return string.Empty;
            foreach (var value in candidates)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return string.Empty;
        }

        private void PopulateProjects()
        {
            _allProjects = LoadProjects();
            ApplyProjectFilter();
        }

        private VisualElement CreateProjectRow(UserProject proj)
        {
            var row = new VisualElement();
            row.AddToClassList("project-row");
            UserProject currentProject = proj;
            bool isEditing = false;
            bool cancelCommit = false;
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0 || evt.target is Button || isEditing) return;
                OpenProjectFile(currentProject.FullPath);
            });
            row.RegisterCallback<ContextClickEvent>(evt =>
            {
                ShowContextMenu(currentProject, row.LocalToWorld(evt.mousePosition), row);
                evt.StopPropagation();
            });
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1 && evt.button != 2) return;
                ShowContextMenu(currentProject, row.LocalToWorld(evt.localPosition), row);
                evt.StopPropagation();
            });

            var lblPath = new Label(currentProject.DisplayPath);
            lblPath.AddToClassList("col-path");
            lblPath.AddToClassList("row-text-dim");

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

            TextField nameField = null;
            var nameLabel = new Label(currentProject.Name);
            nameLabel.AddToClassList("row-label-name");
            nameLabel.AddToClassList("project-name-label");
            nameLabel.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 1 || evt.button == 2)
                {
                    ShowContextMenu(currentProject, nameLabel.LocalToWorld(evt.localPosition), row);
                    evt.StopPropagation();
                    return;
                }
            });
            nameLabel.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (evt.clickCount == 2)
                {
                    StartInlineEdit();
                }
                evt.StopPropagation();
            });
            colName.Add(nameLabel);

            nameField = new TextField();
            nameField.value = currentProject.Name;
            nameField.isDelayed = false;
            nameField.isPasswordField = false;
            nameField.AddToClassList("project-name-field");
            nameField.style.flexGrow = 1;
            nameField.style.minWidth = 0;
            nameField.style.display = DisplayStyle.None;
            nameField.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 1 || evt.button == 2)
                {
                    ShowContextMenu(currentProject, nameField.LocalToWorld(evt.localPosition), row);
                    evt.StopPropagation();
                    return;
                }

                evt.StopPropagation();
            });
            nameField.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (!isEditing) return;
                if (cancelCommit)
                {
                    cancelCommit = false;
                    return;
                }
                EndInlineEdit(true);
            });
            nameField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    cancelCommit = true;
                    EndInlineEdit(false);
                    _root.Focus();
                    evt.StopPropagation();
                    return;
                }

                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    EndInlineEdit(true);
                    _root.Focus();
                    evt.StopPropagation();
                }
            });
            nameField.RegisterCallback<ContextClickEvent>(evt =>
            {
                ShowContextMenu(currentProject, nameField.LocalToWorld(evt.mousePosition), row);
                evt.StopPropagation();
            });
            colName.Add(nameField);

            void StartInlineEdit()
            {
                if (isEditing) return;
                isEditing = true;
                cancelCommit = false;
                nameLabel.style.display = DisplayStyle.None;
                nameField.AddToClassList("editing");
                nameField.style.display = DisplayStyle.Flex;
                nameField.SetValueWithoutNotify(currentProject.Name);
                nameField.Focus();
                nameField.SelectAll();
                nameField.schedule.Execute(() => nameField.SelectAll());
            }

            void EndInlineEdit(bool commit)
            {
                if (!isEditing) return;
                isEditing = false;

                if (commit)
                {
                    string nextName = nameField.value?.Trim();
                    if (string.IsNullOrWhiteSpace(nextName))
                    {
                        nameField.SetValueWithoutNotify(currentProject.Name);
                    }
                    else if (TryRenameProject(currentProject, nextName, out var updated))
                    {
                        currentProject = updated;
                        nameField.SetValueWithoutNotify(updated.Name);
                        nameLabel.text = updated.Name;
                        lblPath.text = updated.DisplayPath;
                    }
                    else
                    {
                        nameField.SetValueWithoutNotify(currentProject.Name);
                    }
                }
                else
                {
                    nameField.SetValueWithoutNotify(currentProject.Name);
                }

                nameField.style.display = DisplayStyle.None;
                nameField.RemoveFromClassList("editing");
                nameLabel.style.display = DisplayStyle.Flex;
            }
            row.Add(colName);

            // Path Column
            row.Add(lblPath);

            // Type Column
            var lblType = new Label(proj.Type);
            lblType.AddToClassList("col-type");
            lblType.AddToClassList("row-type");
            row.Add(lblType);

            // Modified Column
            var lblDate = new Label(proj.ModifiedAt.ToString("MMM dd"));
            lblDate.AddToClassList("col-date");
            lblDate.AddToClassList("row-text-dim");
            row.Add(lblDate);

            // Actions Column
            var colActions = new VisualElement();
            colActions.AddToClassList("col-actions");
            colActions.AddToClassList("row-actions");

            var btnMenu = new Button();
            btnMenu.text = "...";
            btnMenu.AddToClassList("row-action-btn");
            btnMenu.clicked += () => ShowContextMenu(currentProject, GetMenuAnchor(btnMenu), row);
            colActions.Add(btnMenu);
            row.Add(colActions);

            return row;
        }

        private void PopulateHome()
        {
            _recentProjects = LoadRecentProjects();
            if (_recentProjects.Count == 0)
            {
                _recentProjects = LoadProjects()
                    .OrderByDescending(p => p.ModifiedAt)
                    .Take(RecentProjectLimit)
                    .ToList();
            }

            ApplyRecentFilter();
        }

        private void PopulateTemplates()
        {
            var list = _root.Q<ScrollView>("TemplateListContainer");
            if (list == null) return;
            list.Clear();

            list.contentContainer.style.flexDirection = FlexDirection.Row;
            list.contentContainer.style.flexWrap = Wrap.Wrap;

            var templates = LoadTemplates();
            if (templates.Count == 0)
            {
                list.Add(CreateEmptyState("No templates found."));
                return;
            }

            foreach (var tmpl in templates)
            {
                var card = CreateTemplateCard(tmpl);
                list.Add(card);
            }
        }

        private void PopulateComponents()
        {
            if (_componentList == null) _componentList = _root.Q<ScrollView>("ComponentListContainer");
            if (_componentList == null) return;
            _componentList.Clear();

            ComponentCatalog.Reload();
            var items = ComponentCatalog.Items;
            if (items == null || items.Count == 0)
            {
                _componentList.Add(CreateEmptyState("No components found."));
                return;
            }

            foreach (var item in items.OrderBy(i => i.Order).ThenBy(i => i.Name))
            {
                _componentList.Add(CreateComponentRow(item));
            }
        }

        private VisualElement CreateComponentRow(ComponentCatalog.Item item)
        {
            var row = new VisualElement();
            row.AddToClassList("component-row");

            var main = new VisualElement();
            main.AddToClassList("component-row-main");

            var title = new Label(item.Name);
            title.AddToClassList("component-row-title");
            main.Add(title);

            int pinCount = item.Pins != null ? item.Pins.Count : 0;
            string origin = item.IsUserItem ? "User" : "Built-in";
            var subtitle = new Label($"{item.Type} · {pinCount} pins · {origin}");
            subtitle.AddToClassList("component-row-subtitle");
            main.Add(subtitle);
            row.Add(main);

            var actions = new VisualElement();
            actions.AddToClassList("component-row-actions");

            var editBtn = new Button(() => OpenComponentStudio(BuildDefinitionFromItem(item), false, item));
            editBtn.text = "Edit";
            editBtn.AddToClassList("ghost-button");
            actions.Add(editBtn);

            var dupBtn = new Button(() => DuplicateComponent(item));
            dupBtn.text = "Duplicate";
            dupBtn.AddToClassList("ghost-button");
            actions.Add(dupBtn);

            if (item.IsPackage && !string.IsNullOrWhiteSpace(item.SourcePath))
            {
                var openBtn = new Button(() => OpenInBrowser(item.SourcePath));
                openBtn.text = "Open";
                openBtn.AddToClassList("ghost-button");
                actions.Add(openBtn);
            }

            row.Add(actions);
            return row;
        }

        private void DuplicateComponent(ComponentCatalog.Item item)
        {
            var payload = BuildDefinitionFromItem(item);
            payload.name = $"{payload.name} Copy";
            payload.id = GetUniqueComponentId(payload.id);
            OpenComponentStudio(payload, true, null);
        }

        private void OpenComponentEditor(ComponentDefinitionPayload payload, bool isNew, ComponentCatalog.Item? sourceItem)
        {
            if (_componentEditorOverlay == null) return;
            _componentEditorIsNew = isNew;
            _componentEditorSourcePath = sourceItem.HasValue ? sourceItem.Value.SourcePath : string.Empty;
            _componentEditorSource = sourceItem.HasValue ? sourceItem.Value.Source : ComponentCatalog.ComponentSource.UserPackage;

            if (_componentEditorTitle != null)
            {
                _componentEditorTitle.text = isNew ? "New Component" : "Edit Component";
            }

            ApplyComponentEditorPayload(payload);
            _componentEditorOverlay.style.display = DisplayStyle.Flex;
        }

        private void OpenComponentStudio(ComponentDefinitionPayload payload, bool isNew, ComponentCatalog.Item? sourceItem)
        {
            if (payload == null) return;
            string packagePath = sourceItem.HasValue ? sourceItem.Value.SourcePath : string.Empty;
            string sourceModelPath = string.Empty;
            string json = JsonUtility.ToJson(payload, true);

            if (isNew)
            {
                ComponentStudioSession.StartNew(json, packagePath, sourceModelPath, "Wizard");
            }
            else
            {
                ComponentStudioSession.StartEdit(json, packagePath, "Wizard");
            }

            SceneManager.LoadScene("ComponentStudio");
        }

        private void HideComponentEditor()
        {
            if (_componentEditorOverlay == null) return;
            _componentEditorOverlay.style.display = DisplayStyle.None;
        }

        private ComponentDefinitionPayload CreateDefaultComponentDefinition()
        {
            return new ComponentDefinitionPayload
            {
                id = "component",
                name = "New Component",
                description = string.Empty,
                physicsScript = string.Empty,
                type = "Generic",
                symbol = "U",
                symbolFile = string.Empty,
                iconChar = "U",
                order = 1000,
                size2D = new Vector2(CircuitLayoutSizing.DefaultComponentWidth, CircuitLayoutSizing.DefaultComponentHeight),
                pinLayout = new[]
                {
                    new PinLayoutPayload
                    {
                        name = "P1",
                        x = 0f,
                        y = 0.5f,
                        label = "P1",
                        labelOffsetX = -26f,
                        labelOffsetY = -6f,
                        labelSize = 9
                    },
                    new PinLayoutPayload
                    {
                        name = "P2",
                        x = 1f,
                        y = 0.5f,
                        label = "P2",
                        labelOffsetX = 6f,
                        labelOffsetY = -6f,
                        labelSize = 9
                    }
                },
                labels = new[]
                {
                    new LabelLayoutPayload
                    {
                        text = "{id}",
                        x = 0.5f,
                        y = 0.5f,
                        size = 10,
                        align = "center"
                    }
                },
                shapes = Array.Empty<ShapeLayoutPayload>(),
                specs = Array.Empty<ComponentKeyValue>(),
                defaults = Array.Empty<ComponentKeyValue>(),
                pins = new[] { "P1", "P2" }
            };
        }

        private ComponentDefinitionPayload BuildDefinitionFromItem(ComponentCatalog.Item item)
        {
            var payload = new ComponentDefinitionPayload
            {
                id = item.Id,
                name = item.Name,
                description = item.Description,
                type = item.Type,
                symbol = item.Symbol,
                symbolFile = item.SymbolFile,
                iconChar = item.IconChar,
                order = item.Order,
                pins = item.Pins != null ? item.Pins.ToArray() : Array.Empty<string>(),
                specs = ToKeyValueArray(item.ElectricalSpecs),
                defaults = ToKeyValueArray(item.DefaultProperties),
                size2D = item.Size2D,
                pinLayout = item.PinLayout != null
                    ? item.PinLayout.Select(pin => new PinLayoutPayload
                    {
                        name = pin.Name,
                        x = pin.Position.x,
                        y = pin.Position.y,
                        label = pin.Label,
                        labelOffsetX = pin.LabelOffset.x,
                        labelOffsetY = pin.LabelOffset.y,
                        labelSize = pin.LabelSize,
                        anchorX = pin.AnchorLocal.x,
                        anchorY = pin.AnchorLocal.y,
                        anchorZ = pin.AnchorLocal.z,
                        anchorRadius = pin.AnchorRadius
                    }).ToArray()
                    : Array.Empty<PinLayoutPayload>(),
                labels = item.Labels != null
                    ? item.Labels.Select(label => new LabelLayoutPayload
                    {
                        text = label.Text,
                        x = label.Position.x,
                        y = label.Position.y,
                        size = label.Size,
                        align = label.Align
                    }).ToArray()
                    : Array.Empty<LabelLayoutPayload>(),
                shapes = item.Shapes != null
                    ? item.Shapes.Select(shape => new ShapeLayoutPayload
                    {
                        id = shape.Id,
                        type = shape.Type,
                        x = shape.Position.x,
                        y = shape.Position.y,
                        width = shape.Width,
                        height = shape.Height,
                        text = shape.Text
                    }).ToArray()
                    : Array.Empty<ShapeLayoutPayload>(),
                modelFile = item.ModelFile,
                physicsScript = item.PhysicsScript
            };

            if (item.HasTuning)
            {
                payload.tuning = new ComponentTuningPayload
                {
                    Euler = item.Tuning.Euler,
                    Scale = item.Tuning.Scale,
                    UseLedColor = item.Tuning.UseLedColor,
                    LedColor = item.Tuning.LedColor,
                    LedGlowRange = item.Tuning.LedGlowRange,
                    LedGlowIntensity = item.Tuning.LedGlowIntensity,
                    LedBlowCurrent = item.Tuning.LedBlowCurrent,
                    LedBlowTemp = item.Tuning.LedBlowTemp,
                    ResistorSmokeStartTemp = item.Tuning.ResistorSmokeStartTemp,
                    ResistorSmokeFullTemp = item.Tuning.ResistorSmokeFullTemp,
                    ResistorHotStartTemp = item.Tuning.ResistorHotStartTemp,
                    ResistorHotFullTemp = item.Tuning.ResistorHotFullTemp,
                    ErrorFxInterval = item.Tuning.ErrorFxInterval,
                    LabelOffset = item.Tuning.LabelOffset,
                    LedGlowOffset = item.Tuning.LedGlowOffset,
                    HeatFxOffset = item.Tuning.HeatFxOffset,
                    SparkFxOffset = item.Tuning.SparkFxOffset,
                    ErrorFxOffset = item.Tuning.ErrorFxOffset,
                    SmokeOffset = item.Tuning.SmokeOffset,
                    UsbOffset = item.Tuning.UsbOffset
                };
            }

            if (payload.size2D.x <= 0f || payload.size2D.y <= 0f)
            {
                payload.size2D = CircuitLayoutSizing.GetComponentSize2D(payload.type ?? string.Empty);
            }

            return payload;
        }

        private void ApplyComponentEditorPayload(ComponentDefinitionPayload payload)
        {
            if (payload == null) return;

            SetEditorField("ComponentNameField", payload.name);
            SetEditorField("ComponentIdField", payload.id);
            SetEditorField("ComponentTypeField", payload.type);
            SetEditorField("ComponentDescriptionField", payload.description);
            SetEditorField("ComponentPhysicsScriptField", payload.physicsScript);
            SetEditorField("ComponentSymbolField", payload.symbol);
            SetEditorField("ComponentIconField", payload.iconChar);
            SetEditorField("ComponentOrderField", payload.order.ToString(CultureInfo.InvariantCulture));

            string modelPath = payload.modelFile ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(_componentEditorSourcePath) && Directory.Exists(_componentEditorSourcePath))
            {
                var candidate = Path.Combine(_componentEditorSourcePath, payload.modelFile ?? string.Empty);
                if (File.Exists(candidate))
                {
                    modelPath = candidate;
                }
            }
            SetEditorField("ComponentModelField", modelPath);
            SetEditorField("ComponentConfigField", string.Empty);

            SetEditorFloatField("ComponentSizeXField", payload.size2D.x, false);
            SetEditorFloatField("ComponentSizeYField", payload.size2D.y, false);

            _componentPinsContainer?.Clear();
            _componentLabelsContainer?.Clear();
            _componentSpecsContainer?.Clear();
            _componentDefaultsContainer?.Clear();

            if (payload.pinLayout != null)
            {
                foreach (var pin in payload.pinLayout)
                {
                    AddPinRow(pin);
                }
            }

            if (payload.labels != null)
            {
                foreach (var label in payload.labels)
                {
                    AddLabelRow(label);
                }
            }

            if (payload.specs != null)
            {
                foreach (var entry in payload.specs)
                {
                    AddKeyValueRow(_componentSpecsContainer, entry.key, entry.value);
                }
            }

            if (payload.defaults != null)
            {
                foreach (var entry in payload.defaults)
                {
                    AddKeyValueRow(_componentDefaultsContainer, entry.key, entry.value);
                }
            }

            ApplyComponentEditorTuning(payload.tuning);
        }

        private void ApplyComponentEditorTuning(ComponentTuningPayload tuning)
        {
            if (tuning == null)
            {
                ClearComponentEditorTuning();
                return;
            }

            SetEditorFloatField("ComponentEulerXField", tuning.Euler.x, true);
            SetEditorFloatField("ComponentEulerYField", tuning.Euler.y, true);
            SetEditorFloatField("ComponentEulerZField", tuning.Euler.z, true);
            SetEditorFloatField("ComponentScaleXField", tuning.Scale.x, true);
            SetEditorFloatField("ComponentScaleYField", tuning.Scale.y, true);
            SetEditorFloatField("ComponentScaleZField", tuning.Scale.z, true);
            SetEditorFloatField("ComponentLedColorRField", tuning.LedColor.r, true);
            SetEditorFloatField("ComponentLedColorGField", tuning.LedColor.g, true);
            SetEditorFloatField("ComponentLedColorBField", tuning.LedColor.b, true);
            SetEditorFloatField("ComponentLedColorAField", tuning.LedColor.a, true);
            SetEditorFloatField("ComponentLedGlowRangeField", tuning.LedGlowRange, true);
            SetEditorFloatField("ComponentLedGlowIntensityField", tuning.LedGlowIntensity, true);
            SetEditorFloatField("ComponentLedBlowCurrentField", tuning.LedBlowCurrent, true);
            SetEditorFloatField("ComponentLedBlowTempField", tuning.LedBlowTemp, true);
            SetEditorFloatField("ComponentResistorSmokeStartField", tuning.ResistorSmokeStartTemp, true);
            SetEditorFloatField("ComponentResistorSmokeFullField", tuning.ResistorSmokeFullTemp, true);
            SetEditorFloatField("ComponentResistorHotStartField", tuning.ResistorHotStartTemp, true);
            SetEditorFloatField("ComponentResistorHotFullField", tuning.ResistorHotFullTemp, true);
            SetEditorFloatField("ComponentErrorFxIntervalField", tuning.ErrorFxInterval, true);
            SetEditorFloatField("ComponentLabelOffsetXField", tuning.LabelOffset.x, true);
            SetEditorFloatField("ComponentLabelOffsetYField", tuning.LabelOffset.y, true);
            SetEditorFloatField("ComponentLabelOffsetZField", tuning.LabelOffset.z, true);
            SetEditorFloatField("ComponentLedGlowOffsetXField", tuning.LedGlowOffset.x, true);
            SetEditorFloatField("ComponentLedGlowOffsetYField", tuning.LedGlowOffset.y, true);
            SetEditorFloatField("ComponentLedGlowOffsetZField", tuning.LedGlowOffset.z, true);
            SetEditorFloatField("ComponentHeatFxOffsetXField", tuning.HeatFxOffset.x, true);
            SetEditorFloatField("ComponentHeatFxOffsetYField", tuning.HeatFxOffset.y, true);
            SetEditorFloatField("ComponentHeatFxOffsetZField", tuning.HeatFxOffset.z, true);
            SetEditorFloatField("ComponentSparkFxOffsetXField", tuning.SparkFxOffset.x, true);
            SetEditorFloatField("ComponentSparkFxOffsetYField", tuning.SparkFxOffset.y, true);
            SetEditorFloatField("ComponentSparkFxOffsetZField", tuning.SparkFxOffset.z, true);
            SetEditorFloatField("ComponentErrorFxOffsetXField", tuning.ErrorFxOffset.x, true);
            SetEditorFloatField("ComponentErrorFxOffsetYField", tuning.ErrorFxOffset.y, true);
            SetEditorFloatField("ComponentErrorFxOffsetZField", tuning.ErrorFxOffset.z, true);
            SetEditorFloatField("ComponentSmokeOffsetXField", tuning.SmokeOffset.x, true);
            SetEditorFloatField("ComponentSmokeOffsetYField", tuning.SmokeOffset.y, true);
            SetEditorFloatField("ComponentSmokeOffsetZField", tuning.SmokeOffset.z, true);
            SetEditorFloatField("ComponentUsbOffsetXField", tuning.UsbOffset.x, true);
            SetEditorFloatField("ComponentUsbOffsetYField", tuning.UsbOffset.y, true);
            SetEditorFloatField("ComponentUsbOffsetZField", tuning.UsbOffset.z, true);
        }

        private void ClearComponentEditorTuning()
        {
            SetEditorField("ComponentEulerXField", string.Empty);
            SetEditorField("ComponentEulerYField", string.Empty);
            SetEditorField("ComponentEulerZField", string.Empty);
            SetEditorField("ComponentScaleXField", string.Empty);
            SetEditorField("ComponentScaleYField", string.Empty);
            SetEditorField("ComponentScaleZField", string.Empty);
            SetEditorField("ComponentLedColorRField", string.Empty);
            SetEditorField("ComponentLedColorGField", string.Empty);
            SetEditorField("ComponentLedColorBField", string.Empty);
            SetEditorField("ComponentLedColorAField", string.Empty);
            SetEditorField("ComponentLedGlowRangeField", string.Empty);
            SetEditorField("ComponentLedGlowIntensityField", string.Empty);
            SetEditorField("ComponentLedBlowCurrentField", string.Empty);
            SetEditorField("ComponentLedBlowTempField", string.Empty);
            SetEditorField("ComponentResistorSmokeStartField", string.Empty);
            SetEditorField("ComponentResistorSmokeFullField", string.Empty);
            SetEditorField("ComponentResistorHotStartField", string.Empty);
            SetEditorField("ComponentResistorHotFullField", string.Empty);
            SetEditorField("ComponentErrorFxIntervalField", string.Empty);
            SetEditorField("ComponentLabelOffsetXField", string.Empty);
            SetEditorField("ComponentLabelOffsetYField", string.Empty);
            SetEditorField("ComponentLabelOffsetZField", string.Empty);
            SetEditorField("ComponentLedGlowOffsetXField", string.Empty);
            SetEditorField("ComponentLedGlowOffsetYField", string.Empty);
            SetEditorField("ComponentLedGlowOffsetZField", string.Empty);
            SetEditorField("ComponentHeatFxOffsetXField", string.Empty);
            SetEditorField("ComponentHeatFxOffsetYField", string.Empty);
            SetEditorField("ComponentHeatFxOffsetZField", string.Empty);
            SetEditorField("ComponentSparkFxOffsetXField", string.Empty);
            SetEditorField("ComponentSparkFxOffsetYField", string.Empty);
            SetEditorField("ComponentSparkFxOffsetZField", string.Empty);
            SetEditorField("ComponentErrorFxOffsetXField", string.Empty);
            SetEditorField("ComponentErrorFxOffsetYField", string.Empty);
            SetEditorField("ComponentErrorFxOffsetZField", string.Empty);
            SetEditorField("ComponentSmokeOffsetXField", string.Empty);
            SetEditorField("ComponentSmokeOffsetYField", string.Empty);
            SetEditorField("ComponentSmokeOffsetZField", string.Empty);
            SetEditorField("ComponentUsbOffsetXField", string.Empty);
            SetEditorField("ComponentUsbOffsetYField", string.Empty);
            SetEditorField("ComponentUsbOffsetZField", string.Empty);
        }

        private void SaveComponentDefinition()
        {
            var payload = BuildDefinitionFromEditor();
            if (payload == null) return;
            if (string.IsNullOrWhiteSpace(payload.name) && string.IsNullOrWhiteSpace(payload.id))
            {
                Debug.LogWarning("[ProjectWizard] Component needs at least a name or id.");
                return;
            }

            if (string.IsNullOrWhiteSpace(payload.id))
            {
                payload.id = SanitizeComponentId(payload.name);
            }
            if (string.IsNullOrWhiteSpace(payload.type))
            {
                payload.type = payload.name ?? payload.id;
            }

            string modelSourcePath = GetEditorField("ComponentModelField")?.value?.Trim();
            string packagePath = ResolveComponentPackagePath(payload);
            SaveComponentPackage(payload, packagePath, modelSourcePath);

            ComponentCatalog.Reload();
            PopulateComponents();
            HideComponentEditor();
        }

        private ComponentDefinitionPayload BuildDefinitionFromEditor()
        {
            var payload = new ComponentDefinitionPayload
            {
                name = GetEditorField("ComponentNameField")?.value?.Trim() ?? string.Empty,
                id = SanitizeComponentId(GetEditorField("ComponentIdField")?.value),
                type = GetEditorField("ComponentTypeField")?.value?.Trim() ?? string.Empty,
                description = GetEditorField("ComponentDescriptionField")?.value ?? string.Empty,
                physicsScript = GetEditorField("ComponentPhysicsScriptField")?.value ?? string.Empty,
                symbol = GetEditorField("ComponentSymbolField")?.value ?? string.Empty,
                symbolFile = string.Empty,
                iconChar = GetEditorField("ComponentIconField")?.value ?? string.Empty,
                order = ReadIntFromField("ComponentOrderField", 1000),
                size2D = new Vector2(
                    ReadFloatFromField("ComponentSizeXField", CircuitLayoutSizing.DefaultComponentWidth),
                    ReadFloatFromField("ComponentSizeYField", CircuitLayoutSizing.DefaultComponentHeight))
            };

            var pinLayout = CollectPinLayout();
            payload.pinLayout = pinLayout.ToArray();
            payload.pins = pinLayout.Select(p => p.name).Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            payload.labels = CollectLabelLayout().ToArray();
            payload.specs = CollectKeyValues(_componentSpecsContainer);
            payload.defaults = CollectKeyValues(_componentDefaultsContainer);
            payload.tuning = BuildTuningFromEditor();
            return payload;
        }

        private ComponentTuningPayload BuildTuningFromEditor()
        {
            var tuning = new ComponentTuningPayload();
            bool hasAny = false;

            float ex = 0f;
            float ey = 0f;
            float ez = 0f;
            if (TryReadFloatFromField("ComponentEulerXField", out ex) ||
                TryReadFloatFromField("ComponentEulerYField", out ey) ||
                TryReadFloatFromField("ComponentEulerZField", out ez))
            {
                tuning.Euler = new Vector3(ex, ey, ez);
                hasAny = true;
            }

            float sx = 0f;
            float sy = 0f;
            float sz = 0f;
            if (TryReadFloatFromField("ComponentScaleXField", out sx) ||
                TryReadFloatFromField("ComponentScaleYField", out sy) ||
                TryReadFloatFromField("ComponentScaleZField", out sz))
            {
                tuning.Scale = new Vector3(sx, sy, sz);
                hasAny = true;
            }

            float r = 0f;
            float g = 0f;
            float b = 0f;
            float a = 1f;
            bool hasColor = false;
            if (TryReadFloatFromField("ComponentLedColorRField", out r)) hasColor = true;
            if (TryReadFloatFromField("ComponentLedColorGField", out g)) hasColor = true;
            if (TryReadFloatFromField("ComponentLedColorBField", out b)) hasColor = true;
            if (TryReadFloatFromField("ComponentLedColorAField", out a)) hasColor = true;
            if (hasColor)
            {
                tuning.UseLedColor = true;
                tuning.LedColor = new Color(r, g, b, a);
                hasAny = true;
            }

            if (TryReadFloatFromField("ComponentLedGlowRangeField", out var ledRange)) { tuning.LedGlowRange = ledRange; hasAny = true; }
            if (TryReadFloatFromField("ComponentLedGlowIntensityField", out var ledIntensity)) { tuning.LedGlowIntensity = ledIntensity; hasAny = true; }
            if (TryReadFloatFromField("ComponentLedBlowCurrentField", out var ledCurrent)) { tuning.LedBlowCurrent = ledCurrent; hasAny = true; }
            if (TryReadFloatFromField("ComponentLedBlowTempField", out var ledTemp)) { tuning.LedBlowTemp = ledTemp; hasAny = true; }
            if (TryReadFloatFromField("ComponentResistorSmokeStartField", out var smokeStart)) { tuning.ResistorSmokeStartTemp = smokeStart; hasAny = true; }
            if (TryReadFloatFromField("ComponentResistorSmokeFullField", out var smokeFull)) { tuning.ResistorSmokeFullTemp = smokeFull; hasAny = true; }
            if (TryReadFloatFromField("ComponentResistorHotStartField", out var hotStart)) { tuning.ResistorHotStartTemp = hotStart; hasAny = true; }
            if (TryReadFloatFromField("ComponentResistorHotFullField", out var hotFull)) { tuning.ResistorHotFullTemp = hotFull; hasAny = true; }
            if (TryReadFloatFromField("ComponentErrorFxIntervalField", out var errorInterval)) { tuning.ErrorFxInterval = errorInterval; hasAny = true; }

            float lx = 0f;
            float ly = 0f;
            float lz = 0f;
            if (TryReadFloatFromField("ComponentLabelOffsetXField", out lx) ||
                TryReadFloatFromField("ComponentLabelOffsetYField", out ly) ||
                TryReadFloatFromField("ComponentLabelOffsetZField", out lz))
            {
                tuning.LabelOffset = new Vector3(lx, ly, lz);
                hasAny = true;
            }

            float lgx = 0f;
            float lgy = 0f;
            float lgz = 0f;
            if (TryReadFloatFromField("ComponentLedGlowOffsetXField", out lgx) ||
                TryReadFloatFromField("ComponentLedGlowOffsetYField", out lgy) ||
                TryReadFloatFromField("ComponentLedGlowOffsetZField", out lgz))
            {
                tuning.LedGlowOffset = new Vector3(lgx, lgy, lgz);
                hasAny = true;
            }

            float hx = 0f;
            float hy = 0f;
            float hz = 0f;
            if (TryReadFloatFromField("ComponentHeatFxOffsetXField", out hx) ||
                TryReadFloatFromField("ComponentHeatFxOffsetYField", out hy) ||
                TryReadFloatFromField("ComponentHeatFxOffsetZField", out hz))
            {
                tuning.HeatFxOffset = new Vector3(hx, hy, hz);
                hasAny = true;
            }

            float sx2 = 0f;
            float sy2 = 0f;
            float sz2 = 0f;
            if (TryReadFloatFromField("ComponentSparkFxOffsetXField", out sx2) ||
                TryReadFloatFromField("ComponentSparkFxOffsetYField", out sy2) ||
                TryReadFloatFromField("ComponentSparkFxOffsetZField", out sz2))
            {
                tuning.SparkFxOffset = new Vector3(sx2, sy2, sz2);
                hasAny = true;
            }

            float ex2 = 0f;
            float ey2 = 0f;
            float ez2 = 0f;
            if (TryReadFloatFromField("ComponentErrorFxOffsetXField", out ex2) ||
                TryReadFloatFromField("ComponentErrorFxOffsetYField", out ey2) ||
                TryReadFloatFromField("ComponentErrorFxOffsetZField", out ez2))
            {
                tuning.ErrorFxOffset = new Vector3(ex2, ey2, ez2);
                hasAny = true;
            }

            float smx = 0f;
            float smy = 0f;
            float smz = 0f;
            if (TryReadFloatFromField("ComponentSmokeOffsetXField", out smx) ||
                TryReadFloatFromField("ComponentSmokeOffsetYField", out smy) ||
                TryReadFloatFromField("ComponentSmokeOffsetZField", out smz))
            {
                tuning.SmokeOffset = new Vector3(smx, smy, smz);
                hasAny = true;
            }

            float ux = 0f;
            float uy = 0f;
            float uz = 0f;
            if (TryReadFloatFromField("ComponentUsbOffsetXField", out ux) ||
                TryReadFloatFromField("ComponentUsbOffsetYField", out uy) ||
                TryReadFloatFromField("ComponentUsbOffsetZField", out uz))
            {
                tuning.UsbOffset = new Vector3(ux, uy, uz);
                hasAny = true;
            }

            return hasAny ? tuning : null;
        }

        private void BrowseForComponentModel()
        {
#if UNITY_EDITOR
            string file = EditorUtility.OpenFilePanel("Select Model", string.Empty, "fbx,obj,glb,gltf");
            if (!string.IsNullOrWhiteSpace(file))
            {
                SetEditorField("ComponentModelField", file);
            }
#else
            OpenInFileBrowser(ComponentCatalog.GetUserComponentRoot());
#endif
        }

        private void BrowseForComponentConfig()
        {
#if UNITY_EDITOR
            string file = EditorUtility.OpenFilePanel("Select Config", string.Empty, "json,rtcomp");
            if (!string.IsNullOrWhiteSpace(file))
            {
                SetEditorField("ComponentConfigField", file);
            }
#else
            OpenInFileBrowser(ComponentCatalog.GetUserComponentRoot());
#endif
        }

        private void LoadComponentConfigFromField()
        {
            string path = GetEditorField("ComponentConfigField")?.value?.Trim();
            if (string.IsNullOrWhiteSpace(path)) return;

            string json = string.Empty;
            bool isPackageFile = File.Exists(path) && ComponentPackageUtility.IsPackagePath(path);
            string configPath = path;

            if (isPackageFile)
            {
                if (!ComponentPackageUtility.TryReadDefinitionJson(path, out json))
                {
                    Debug.LogWarning($"[ProjectWizard] Component package not found: {path}");
                    return;
                }
            }
            else
            {
                if (Directory.Exists(path) && path.EndsWith(".rtcomp", StringComparison.OrdinalIgnoreCase))
                {
                    configPath = Path.Combine(path, "component.json");
                }
                if (!File.Exists(configPath))
                {
                    Debug.LogWarning($"[ProjectWizard] Component config not found: {configPath}");
                    return;
                }
                json = File.ReadAllText(configPath);
            }

            try
            {
                var payload = JsonUtility.FromJson<ComponentDefinitionPayload>(json);
                if (payload == null)
                {
                    Debug.LogWarning("[ProjectWizard] Failed to parse component config.");
                    return;
                }
                if (!string.IsNullOrWhiteSpace(payload.modelFile))
                {
                    if (isPackageFile)
                    {
                        if (ComponentPackageUtility.TryExtractEntryToCache(path, payload.modelFile, out var modelPath))
                        {
                            SetEditorField("ComponentModelField", modelPath);
                        }
                    }
                    else if (Directory.Exists(path))
                    {
                        var modelPath = Path.Combine(path, payload.modelFile);
                        if (File.Exists(modelPath))
                        {
                            SetEditorField("ComponentModelField", modelPath);
                        }
                    }
                }
                ApplyComponentEditorPayload(payload);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectWizard] Failed to load component config: {ex.Message}");
            }
        }

        private void AddPinRow(PinLayoutPayload payload)
        {
            if (_componentPinsContainer == null) return;
            var entry = new VisualElement();
            entry.AddToClassList("editor-entry");

            var mainRow = new VisualElement();
            mainRow.AddToClassList("editor-row");
            mainRow.Add(CreateEditorField("PinNameField", payload.name, "editor-field"));
            mainRow.Add(CreateEditorField("PinXField", FormatFloat(payload.x), "editor-field-small"));
            mainRow.Add(CreateEditorField("PinYField", FormatFloat(payload.y), "editor-field-small"));
            mainRow.Add(CreateEditorField("PinLabelField", payload.label, "editor-field"));
            mainRow.Add(CreateEditorField("PinLabelOffsetXField", FormatFloat(payload.labelOffsetX), "editor-field-small"));
            mainRow.Add(CreateEditorField("PinLabelOffsetYField", FormatFloat(payload.labelOffsetY), "editor-field-small"));
            mainRow.Add(CreateEditorField("PinLabelSizeField", payload.labelSize > 0 ? payload.labelSize.ToString(CultureInfo.InvariantCulture) : string.Empty, "editor-field-small"));

            var removeBtn = new Button(() => _componentPinsContainer.Remove(entry));
            removeBtn.text = "X";
            removeBtn.AddToClassList("editor-remove-btn");
            mainRow.Add(removeBtn);
            entry.Add(mainRow);

            var anchorRow = new VisualElement();
            anchorRow.AddToClassList("editor-row");
            anchorRow.Add(CreateEditorField("PinAnchorXField", FormatFloat(payload.anchorX), "editor-field-small"));
            anchorRow.Add(CreateEditorField("PinAnchorYField", FormatFloat(payload.anchorY), "editor-field-small"));
            anchorRow.Add(CreateEditorField("PinAnchorZField", FormatFloat(payload.anchorZ), "editor-field-small"));
            anchorRow.Add(CreateEditorField("PinAnchorRadiusField", FormatFloat(payload.anchorRadius), "editor-field-small"));
            entry.Add(anchorRow);

            _componentPinsContainer.Add(entry);
        }

        private void AddLabelRow(LabelLayoutPayload payload)
        {
            if (_componentLabelsContainer == null) return;
            var entry = new VisualElement();
            entry.AddToClassList("editor-entry");

            var row = new VisualElement();
            row.AddToClassList("editor-row");
            row.Add(CreateEditorField("LabelTextField", payload.text, "editor-field"));
            row.Add(CreateEditorField("LabelXField", FormatFloat(payload.x), "editor-field-small"));
            row.Add(CreateEditorField("LabelYField", FormatFloat(payload.y), "editor-field-small"));
            row.Add(CreateEditorField("LabelSizeField", payload.size > 0 ? payload.size.ToString(CultureInfo.InvariantCulture) : string.Empty, "editor-field-small"));
            row.Add(CreateEditorField("LabelAlignField", payload.align, "editor-field-small"));

            var removeBtn = new Button(() => _componentLabelsContainer.Remove(entry));
            removeBtn.text = "X";
            removeBtn.AddToClassList("editor-remove-btn");
            row.Add(removeBtn);
            entry.Add(row);

            _componentLabelsContainer.Add(entry);
        }

        private void AddKeyValueRow(VisualElement container, string key, string value)
        {
            if (container == null) return;
            var row = new VisualElement();
            row.AddToClassList("editor-row");
            row.Add(CreateEditorField("KeyField", key, "editor-field"));
            row.Add(CreateEditorField("ValueField", value, "editor-field"));
            var removeBtn = new Button(() => container.Remove(row));
            removeBtn.text = "X";
            removeBtn.AddToClassList("editor-remove-btn");
            row.Add(removeBtn);
            container.Add(row);
        }

        private List<PinLayoutPayload> CollectPinLayout()
        {
            var list = new List<PinLayoutPayload>();
            if (_componentPinsContainer == null) return list;
            foreach (var entry in _componentPinsContainer.Children())
            {
                var nameField = entry.Q<TextField>("PinNameField");
                string name = nameField?.value?.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new PinLayoutPayload
                {
                    name = name,
                    x = ReadFloatFromField(entry.Q<TextField>("PinXField")),
                    y = ReadFloatFromField(entry.Q<TextField>("PinYField")),
                    label = entry.Q<TextField>("PinLabelField")?.value ?? string.Empty,
                    labelOffsetX = ReadFloatFromField(entry.Q<TextField>("PinLabelOffsetXField")),
                    labelOffsetY = ReadFloatFromField(entry.Q<TextField>("PinLabelOffsetYField")),
                    labelSize = ReadIntFromField(entry.Q<TextField>("PinLabelSizeField")),
                    anchorX = ReadFloatFromField(entry.Q<TextField>("PinAnchorXField")),
                    anchorY = ReadFloatFromField(entry.Q<TextField>("PinAnchorYField")),
                    anchorZ = ReadFloatFromField(entry.Q<TextField>("PinAnchorZField")),
                    anchorRadius = ReadFloatFromField(entry.Q<TextField>("PinAnchorRadiusField"))
                });
            }
            return list;
        }

        private List<LabelLayoutPayload> CollectLabelLayout()
        {
            var list = new List<LabelLayoutPayload>();
            if (_componentLabelsContainer == null) return list;
            foreach (var entry in _componentLabelsContainer.Children())
            {
                var textField = entry.Q<TextField>("LabelTextField");
                string text = textField?.value?.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;
                list.Add(new LabelLayoutPayload
                {
                    text = text,
                    x = ReadFloatFromField(entry.Q<TextField>("LabelXField")),
                    y = ReadFloatFromField(entry.Q<TextField>("LabelYField")),
                    size = ReadIntFromField(entry.Q<TextField>("LabelSizeField")),
                    align = entry.Q<TextField>("LabelAlignField")?.value ?? string.Empty
                });
            }
            return list;
        }

        private ComponentKeyValue[] CollectKeyValues(VisualElement container)
        {
            if (container == null) return Array.Empty<ComponentKeyValue>();
            var list = new List<ComponentKeyValue>();
            foreach (var row in container.Children())
            {
                var keyField = row.Q<TextField>("KeyField");
                var valueField = row.Q<TextField>("ValueField");
                string key = keyField?.value?.Trim();
                string value = valueField?.value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key)) continue;
                list.Add(new ComponentKeyValue { key = key, value = value });
            }
            return list.ToArray();
        }

        private void SaveComponentPackage(ComponentDefinitionPayload payload, string packagePath, string modelSourcePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath)) return;

            if (!string.IsNullOrWhiteSpace(modelSourcePath) && File.Exists(modelSourcePath))
            {
                string fileName = Path.GetFileName(modelSourcePath);
                payload.modelFile = ComponentPackageUtility.BuildAssetEntryName(fileName);
            }
            else if (string.IsNullOrWhiteSpace(payload.modelFile))
            {
                payload.modelFile = string.Empty;
            }

            payload.pins = payload.pinLayout != null
                ? payload.pinLayout.Select(p => p.name).Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();

            string json = JsonUtility.ToJson(payload, true);
            ComponentPackageUtility.SavePackage(packagePath, json, modelSourcePath);
        }

        private string ResolveComponentPackagePath(ComponentDefinitionPayload payload)
        {
            if (!_componentEditorIsNew &&
                _componentEditorSource == ComponentCatalog.ComponentSource.UserPackage &&
                !string.IsNullOrWhiteSpace(_componentEditorSourcePath) &&
                File.Exists(_componentEditorSourcePath))
            {
                return _componentEditorSourcePath;
            }

            string root = ComponentCatalog.GetUserComponentRoot();
            string baseName = SanitizeComponentId(payload?.id ?? string.Empty);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "component";
            string candidate = Path.Combine(root, baseName + ComponentPackageUtility.PackageExtension);
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;

            int index = 1;
            while (File.Exists(Path.Combine(root, $"{baseName}_{index}{ComponentPackageUtility.PackageExtension}")) ||
                   Directory.Exists(Path.Combine(root, $"{baseName}_{index}{ComponentPackageUtility.PackageExtension}")))
            {
                index++;
            }
            return Path.Combine(root, $"{baseName}_{index}{ComponentPackageUtility.PackageExtension}");
        }

        private string GetUniqueComponentId(string baseId)
        {
            string safeBase = SanitizeComponentId(baseId);
            if (string.IsNullOrWhiteSpace(safeBase)) safeBase = "component";
            var existing = new HashSet<string>(ComponentCatalog.Items.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
            string candidate = safeBase;
            int index = 1;
            while (existing.Contains(candidate))
            {
                candidate = $"{safeBase}_{index++}";
            }
            return candidate;
        }

        private string GetNextPinName()
        {
            if (_componentPinsContainer == null) return "P1";
            int index = _componentPinsContainer.childCount + 1;
            return $"P{index}";
        }

        private static string SanitizeComponentId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var sb = new StringBuilder();
            foreach (char ch in raw.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                {
                    sb.Append(ch);
                }
                else if (char.IsWhiteSpace(ch))
                {
                    sb.Append('-');
                }
            }
            return sb.ToString().Trim('-');
        }

        private static ComponentKeyValue[] ToKeyValueArray(Dictionary<string, string> dict)
        {
            if (dict == null || dict.Count == 0) return Array.Empty<ComponentKeyValue>();
            return dict.Select(kvp => new ComponentKeyValue { key = kvp.Key, value = kvp.Value }).ToArray();
        }

        private TextField GetEditorField(string name)
        {
            return _componentEditorOverlay?.Q<TextField>(name);
        }

        private void SetEditorField(string name, string value)
        {
            var field = GetEditorField(name);
            if (field == null) return;
            field.SetValueWithoutNotify(value ?? string.Empty);
        }

        private void SetEditorFloatField(string name, float value, bool allowEmpty)
        {
            if (allowEmpty && Mathf.Abs(value) < 0.0001f)
            {
                SetEditorField(name, string.Empty);
                return;
            }
            SetEditorField(name, value.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private bool TryReadFloatFromField(string name, out float value)
        {
            value = 0f;
            var field = GetEditorField(name);
            if (field == null) return false;
            string raw = field.value?.Trim();
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private float ReadFloatFromField(string name, float fallback)
        {
            return TryReadFloatFromField(name, out var value) ? value : fallback;
        }

        private int ReadIntFromField(string name, int fallback = 0)
        {
            var field = GetEditorField(name);
            if (field == null) return fallback;
            string raw = field.value?.Trim();
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
            return fallback;
        }

        private static string FormatFloat(float value)
        {
            if (Mathf.Abs(value) < 0.0001f) return string.Empty;
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static TextField CreateEditorField(string name, string value, string className)
        {
            var field = new TextField { name = name };
            field.AddToClassList(className);
            field.SetValueWithoutNotify(value ?? string.Empty);
            return field;
        }

        private static float ReadFloatFromField(TextField field)
        {
            if (field == null) return 0f;
            string raw = field.value?.Trim();
            if (string.IsNullOrWhiteSpace(raw)) return 0f;
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return value;
            return 0f;
        }

        private static int ReadIntFromField(TextField field)
        {
            if (field == null) return 0;
            string raw = field.value?.Trim();
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
            return 0;
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
            var accentSoft = new Color(tmpl.AccentColor.r, tmpl.AccentColor.g, tmpl.AccentColor.b, 0.2f);
            iconBox.style.backgroundColor = new StyleColor(accentSoft);
            iconBox.style.borderTopLeftRadius = 6;
            iconBox.style.borderTopRightRadius = 6;
            iconBox.style.borderBottomLeftRadius = 6;
            iconBox.style.borderBottomRightRadius = 6;
            iconBox.style.alignItems = Align.Center;
            iconBox.style.justifyContent = Justify.Center;

            var icon = new VisualElement();
            string iconClass = string.IsNullOrWhiteSpace(tmpl.IconClass) ? "icon-hexagon" : tmpl.IconClass;
            icon.AddToClassList(iconClass);
            icon.AddToClassList("template-icon");
            icon.style.unityBackgroundImageTintColor = new StyleColor(tmpl.AccentColor);
            iconBox.Add(icon);
            header.Add(iconBox);

            var tag = new Label(tmpl.Tag);
            tag.AddToClassList("template-tag");
            if (string.IsNullOrWhiteSpace(tmpl.Tag)) tag.style.display = DisplayStyle.None;
            if (!string.IsNullOrWhiteSpace(tmpl.Tag))
            {
                tag.style.backgroundColor = new StyleColor(new Color(tmpl.AccentColor.r, tmpl.AccentColor.g, tmpl.AccentColor.b, 0.2f));
                tag.style.color = Color.white;
            }
            header.Add(tag);
            card.Add(header);

            var art = new VisualElement();
            art.AddToClassList("template-art");
            art.style.backgroundColor = new StyleColor(new Color(tmpl.AccentColor.r, tmpl.AccentColor.g, tmpl.AccentColor.b, 0.12f));
            var artIcon = new VisualElement();
            artIcon.AddToClassList("template-art-icon");
            artIcon.AddToClassList(iconClass);
            artIcon.style.unityBackgroundImageTintColor = new StyleColor(tmpl.AccentColor);
            art.Add(artIcon);
            card.Add(art);

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
            if (string.IsNullOrWhiteSpace(tmpl.Difficulty)) diff.style.display = DisplayStyle.None;
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
            ShowCreateOverlay(tmpl);
        }

        private void BrowseForTemplateLocation()
        {
#if UNITY_EDITOR
            string folder = EditorUtility.OpenFolderPanel("Select Project Location", _projectsPath, string.Empty);
            if (!string.IsNullOrWhiteSpace(folder) && _createPathField != null)
            {
                _createPathField.SetValueWithoutNotify(folder);
            }
#else
            OpenInFileBrowser(_projectsPath);
#endif
        }

        private void CreateProjectFromTemplate()
        {
            if (!_hasPendingTemplate) return;

            string rawName = _createNameField?.value ?? string.Empty;
            string safeName = SanitizeProjectName(rawName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = SanitizeProjectName(_pendingTemplate.Title);
            }

            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "NewProject";
            }

            string targetDir = _createPathField?.value?.Trim();
            if (string.IsNullOrWhiteSpace(targetDir)) targetDir = _projectsPath;

            try
            {
                targetDir = Path.GetFullPath(targetDir);
                Directory.CreateDirectory(targetDir);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectWizard] Failed to use project folder: {ex.Message}");
                targetDir = _projectsPath;
                Directory.CreateDirectory(targetDir);
            }

            string targetPath = Path.Combine(targetDir, $"{safeName}.rtwin");
            targetPath = EnsureUniqueProjectPath(targetPath);

            string description = string.IsNullOrWhiteSpace(_pendingTemplate.Description) ? "New project" : _pendingTemplate.Description;
            var manifest = LoadTemplateManifest(_pendingTemplate.SourcePath) ?? BuildDefaultManifest(safeName, description);
            manifest.ProjectName = safeName;
            manifest.Description = description;
            if (manifest.Circuit == null) manifest.Circuit = new CircuitSpec();
            if (manifest.Robot == null) manifest.Robot = new RobotSpec { Name = "DefaultRobot" };
            if (manifest.World == null) manifest.World = new WorldSpec { Name = "DefaultWorld", Width = 0, Depth = 0 };
            CenterTemplateCircuit(manifest.Circuit);

            try
            {
                ImportTemplateResources(_pendingTemplate.SourcePath, targetPath);
                SimulationSerializer.SaveProject(manifest, targetPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectWizard] Failed to create project: {ex.Message}");
                return;
            }

            if (SessionManager.Instance != null)
            {
                SessionManager.Instance.StartSession(manifest, targetPath);
            }

            RecordRecentProject(targetPath);

            HideCreateOverlay();
            PopulateHome();
            PopulateProjects();

            int sceneCount = SceneManager.sceneCountInBuildSettings;
            if (sceneCount > 1) SceneManager.LoadScene("Main");
            else Debug.LogWarning("[ProjectWizard] Scene 1 not found.");
        }

        private ProjectManifest BuildDefaultManifest(string name, string description)
        {
            return new ProjectManifest
            {
                ProjectName = name,
                Description = description,
                Version = "1.0.0",
                Circuit = new CircuitSpec(),
                Robot = new RobotSpec { Name = "DefaultRobot" },
                World = new WorldSpec { Name = "DefaultWorld", Width = 0, Depth = 0 }
            };
        }

        private static void CenterTemplateCircuit(CircuitSpec circuit)
        {
            if (circuit?.Components == null || circuit.Components.Count == 0) return;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            bool hasPosition = false;
            foreach (var comp in circuit.Components)
            {
                if (comp?.Properties == null) continue;
                if (!comp.Properties.TryGetValue("posX", out var xRaw) ||
                    !comp.Properties.TryGetValue("posY", out var yRaw))
                {
                    continue;
                }
                if (!float.TryParse(xRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                    !float.TryParse(yRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    continue;
                }
                var size = CircuitLayoutSizing.GetComponentSize2D(comp.Type ?? string.Empty);
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x + size.x);
                maxY = Mathf.Max(maxY, y + size.y);
                hasPosition = true;
            }

            if (!hasPosition) return;
            var bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            if (bounds.width <= 0f || bounds.height <= 0f) return;

            var boardCenter = new Vector2(
                CircuitLayoutSizing.BoardWorldWidth * 0.5f,
                CircuitLayoutSizing.BoardWorldHeight * 0.5f);
            var offset = boardCenter - bounds.center;
            if (offset.sqrMagnitude < 1f) return;

            foreach (var comp in circuit.Components)
            {
                if (comp?.Properties == null) continue;
                if (!comp.Properties.TryGetValue("posX", out var xRaw) ||
                    !comp.Properties.TryGetValue("posY", out var yRaw))
                {
                    continue;
                }
                if (!float.TryParse(xRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                    !float.TryParse(yRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    continue;
                }
                var shifted = new Vector2(x, y) + offset;
                comp.Properties["posX"] = shifted.x.ToString("F2", CultureInfo.InvariantCulture);
                comp.Properties["posY"] = shifted.y.ToString("F2", CultureInfo.InvariantCulture);
            }
        }

        private ProjectManifest LoadTemplateManifest(string templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath)) return null;

            string rtwinPath = Path.Combine(templatePath, "project.rtwin");
            if (File.Exists(rtwinPath))
            {
                try
                {
                    return SimulationSerializer.LoadProject(rtwinPath, false);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ProjectWizard] Failed to load template rtwin: {ex.Message}");
                }
            }

            string jsonPath = Path.Combine(templatePath, "project.json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    string json = File.ReadAllText(jsonPath);
                    return SimulationSerializer.Deserialize<ProjectManifest>(json);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ProjectWizard] Failed to load template json: {ex.Message}");
                }
            }

            return null;
        }

        private void ImportTemplateResources(string templatePath, string projectPath)
        {
            if (string.IsNullOrWhiteSpace(templatePath) || string.IsNullOrWhiteSpace(projectPath)) return;

            string workspaceRoot = ResolveProjectWorkspaceRoot(projectPath);
            if (string.IsNullOrWhiteSpace(workspaceRoot)) return;

            string codeSource = Path.Combine(templatePath, "Code");
            string codeTarget = Path.Combine(workspaceRoot, "Code");
            if (Directory.Exists(codeSource))
            {
                CopyDirectoryRecursive(codeSource, codeTarget);
            }
        }

        private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(sourceDir, dir);
                Directory.CreateDirectory(Path.Combine(targetDir, rel));
            }

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(sourceDir, file);
                string dest = Path.Combine(targetDir, rel);
                string destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrWhiteSpace(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }
                File.Copy(file, dest, true);
            }
        }

        [Serializable]
        private class ComponentDefinitionPayload
        {
            public string id;
            public string name;
            public string description;
            public string type;
            public string symbol;
            public string symbolFile;
            public string iconChar;
            public int order;
            public string[] pins;
            public ComponentKeyValue[] specs;
            public ComponentKeyValue[] defaults;
            public Vector2 size2D;
            public PinLayoutPayload[] pinLayout;
            public LabelLayoutPayload[] labels;
            public ShapeLayoutPayload[] shapes;
            public ComponentTuningPayload tuning;
            public string modelFile;
            public string physicsScript;
        }

        [Serializable]
        private class ComponentKeyValue
        {
            public string key;
            public string value;
        }

        [Serializable]
        private class PinLayoutPayload
        {
            public string name;
            public float x;
            public float y;
            public string label;
            public float labelOffsetX;
            public float labelOffsetY;
            public int labelSize;
            public float anchorX;
            public float anchorY;
            public float anchorZ;
            public float anchorRadius;
        }

        [Serializable]
        private class LabelLayoutPayload
        {
            public string text;
            public float x;
            public float y;
            public int size;
            public string align;
        }

        [Serializable]
        private class ShapeLayoutPayload
        {
            public string id;
            public string type;
            public float x;
            public float y;
            public float width;
            public float height;
            public string text;
        }

        [Serializable]
        private class ComponentTuningPayload
        {
            public Vector3 Euler;
            public Vector3 Scale;
            public bool UseLedColor;
            public Color LedColor;
            public float LedGlowRange;
            public float LedGlowIntensity;
            public float LedBlowCurrent;
            public float LedBlowTemp;
            public float ResistorSmokeStartTemp;
            public float ResistorSmokeFullTemp;
            public float ResistorHotStartTemp;
            public float ResistorHotFullTemp;
            public float ErrorFxInterval;
            public Vector3 LabelOffset;
            public Vector3 LedGlowOffset;
            public Vector3 HeatFxOffset;
            public Vector3 SparkFxOffset;
            public Vector3 ErrorFxOffset;
            public Vector3 SmokeOffset;
            public Vector3 UsbOffset;
        }

        private static string ResolveProjectWorkspaceRoot(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath)) return string.Empty;
            if (Directory.Exists(projectPath)) return projectPath;

            string projectDir = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrWhiteSpace(projectDir)) return string.Empty;

            if (projectPath.EndsWith(".rtwin", StringComparison.OrdinalIgnoreCase))
            {
                string baseName = Path.GetFileNameWithoutExtension(projectPath);
                if (!string.IsNullOrWhiteSpace(baseName))
                {
                    return Path.Combine(projectDir, baseName);
                }
            }

            return projectDir;
        }
    }
}
