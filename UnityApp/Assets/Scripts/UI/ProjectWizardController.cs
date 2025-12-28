using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        // Logic State
        private string _projectsPath;
        private const string ProjectRootKey = "ProjectWizard.ProjectRootPath";
        private const int RecentProjectLimit = 5;

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

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) { Debug.LogError("[ProjectWizard] Missing UIDocument"); return; }
            _root = _doc.rootVisualElement;
            if (_root == null) return;

            LoadProjectRoot();

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
                _root.Focus();
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            _root.focusable = true;
            _root.Focus();

            // Initial Data
            PopulateHome();
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

            HideContextMenu();
            HideDetails();
            HideCreateOverlay();
            HideDeleteConfirm();
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
            int index = 1;
            while (File.Exists(copyPath))
            {
                copyPath = Path.Combine(dir, $"{name}_copy{index}{ext}");
                index++;
            }

            try
            {
                File.Copy(proj.FullPath, copyPath);
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
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProjectWizard] Failed to remove project: {ex.Message}");
                return;
            }

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

            try
            {
                File.Move(project.FullPath, targetPath);
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
            if (!File.Exists(targetPath)) return targetPath;

            string dir = Path.GetDirectoryName(targetPath) ?? _projectsPath;
            string name = Path.GetFileNameWithoutExtension(targetPath);
            string ext = Path.GetExtension(targetPath);
            int index = 1;
            string candidate = targetPath;

            while (File.Exists(candidate))
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

            HideContextMenu();
            HideDetails();

            int sceneCount = SceneManager.sceneCountInBuildSettings;
            if (sceneCount > 1) SceneManager.LoadScene(1);
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

                    if (meta == null || string.IsNullOrWhiteSpace(meta.name)) continue;

                    templates.Add(new Template
                    {
                        Title = meta.name,
                        Description = meta.description ?? string.Empty,
                        Tag = meta.tag ?? string.Empty,
                        Difficulty = meta.difficulty ?? string.Empty,
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
            public string description;
            public string tag;
            public string difficulty;
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
            _recentProjects = LoadProjects()
                .OrderByDescending(p => p.ModifiedAt)
                .Take(RecentProjectLimit)
                .ToList();

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

            try
            {
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

            HideCreateOverlay();
            PopulateHome();
            PopulateProjects();

            int sceneCount = SceneManager.sceneCountInBuildSettings;
            if (sceneCount > 1) SceneManager.LoadScene(1);
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

        private ProjectManifest LoadTemplateManifest(string templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath)) return null;

            string rtwinPath = Path.Combine(templatePath, "project.rtwin");
            if (File.Exists(rtwinPath))
            {
                try
                {
                    return SimulationSerializer.LoadProject(rtwinPath);
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
    }
}
