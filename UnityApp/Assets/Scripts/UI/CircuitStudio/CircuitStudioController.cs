using System;
using UnityEngine;
using UnityEngine.UIElements;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Serialization;
using RobotTwin.Game;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace RobotTwin.UI
{
    public class CircuitStudioController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _root;
        
        // UI References
        private ScrollView _libraryList;
        private TextField _librarySearchField;
        private ScrollView _projectTreeList;
        private VisualElement _canvasView;
        private VisualElement _boardView;
        private GridLayer _gridLayer;
        private VisualElement _canvasHud;
        private VisualElement _propertiesPanel;
        private VisualElement _outputConsole;
        private VisualElement _outputPanel;
        private VisualElement _codePanel;
        private ScrollView _codeFileList;
        private TextField _codeEditor;
        private Label _codeFileLabel;
        private DropdownField _codeTargetDropdown;
        private Button _codeOpenBtn;
        private Button _codeSaveBtn;
        private Button _codeSaveAsBtn;
        private Button _codeBuildBtn;
        private Button _codeBuildRunBtn;
        private Button _codeBuildAllBtn;
        private Button _tabOutput;
        private Button _tabCode;
        private Label _errorCountLabel;
        private TextField _transformInputX;
        private TextField _transformInputY;
        private VisualElement _workspaceArea;
        private VisualElement _leftPanel;
        private VisualElement _rightPanel;
        private VisualElement _bottomPanel;
        private VisualElement _leftResizer;
        private VisualElement _rightResizer;
        private VisualElement _bottomResizer;
        
        // Toolbar
        private Button _btnSelect;
        private Button _btnMove;
        private Button _btnWire;
        private Button _btnSim;
        
        // Status
        private Label _statusLabel;
        private Label _versionText;

        // Selection & State
        private CircuitSpec _currentCircuit;
        private ComponentSpec _selectedComponent;
        private VisualElement _selectedVisual;
        private ComponentCatalog.Item _selectedCatalogItem;
        private Vector2 _selectedComponentSize = new Vector2(ComponentWidth, ComponentHeight);

        // Dragging
        private bool _isDragging;
        private ComponentCatalog.Item _dragItem;
        private VisualElement _ghostIcon;
        private int _dragPointerId = -1;
        private readonly Dictionary<string, Vector2> _componentPositions = new Dictionary<string, Vector2>();
        private readonly Dictionary<string, HashSet<string>> _usedPins = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<string, VisualElement> _componentVisuals = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, VisualElement> _pinVisuals = new Dictionary<string, VisualElement>();

        // Resizing
        private enum ResizeMode { None, Left, Right, Bottom }
        private ResizeMode _resizeMode = ResizeMode.None;
        private bool _isResizing;
        private int _resizePointerId = -1;
        private Vector2 _resizeStart;
        private float _startLeftWidth;
        private float _startRightWidth;
        private float _startBottomHeight;
        private VisualElement _resizeTarget;

        // Wiring
        private ComponentSpec _wireStartComponent;
        private string _wireStartPin;
        private VisualElement _wireLayer;
        private readonly List<WireSegment> _wireSegments = new List<WireSegment>();
        private string _activeCodePath;
        private string _codeTargetComponentId;
        private readonly Dictionary<string, string> _codeTargetLabels = new Dictionary<string, string>();

        private const float GridSnap = 10f;
        private const float ComponentWidth = 120f;
        private const float ComponentHeight = 56f;
        private const float BoardPadding = 24f;
        private const float MinBoardWidth = 320f;
        private const float MinBoardHeight = 220f;
        private const float MinLeftPanelWidth = 220f;
        private const float MaxLeftPanelWidth = 520f;
        private const float MinRightPanelWidth = 260f;
        private const float MaxRightPanelWidth = 520f;
        private const float MinBottomPanelHeight = 100f;
        private const float MaxBottomPanelHeight = 360f;
        private static readonly Vector2 ViewportSize = new Vector2(1920f, 1080f);
        private static readonly string[] ArduinoLeftPins =
        {
            "IOREF", "RESET", "3V3", "5V", "GND1", "GND2", "VIN", "A0", "A1", "A2", "A3", "A4", "A5"
        };
        private static readonly string[] ArduinoRightPins =
        {
            "SCL", "SDA", "AREF", "GND3", "D13", "D12", "D11", "D10", "D9", "D8", "D7", "D6", "D5", "D4", "D3", "D2", "D1", "D0"
        };
        private static readonly string[] ArduinoPreferredPins =
        {
            "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D10", "D11", "D12", "D13", "D0", "D1",
            "A0", "A1", "A2", "A3", "A4", "A5",
            "GND1", "GND2", "GND3", "5V", "3V3", "VIN"
        };
        private static readonly string[] ArduinoNanoLeftPins =
        {
            "VIN", "GND1", "RST", "5V", "3V3", "A7", "A6", "A5", "A4", "A3", "A2", "A1", "A0"
        };
        private static readonly string[] ArduinoNanoRightPins =
        {
            "AREF", "D13", "D12", "D11", "D10", "D9", "D8", "D7", "D6", "D5", "D4", "D3", "D2", "D1", "D0", "GND2"
        };
        private static readonly string[] ArduinoNanoPreferredPins =
        {
            "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D10", "D11", "D12", "D13", "D0", "D1",
            "A0", "A1", "A2", "A3", "A4", "A5", "A6", "A7",
            "GND1", "GND2", "5V", "3V3", "VIN", "RST", "AREF"
        };
        private static readonly string[] ArduinoProMiniLeftPins =
        {
            "RAW", "GND1", "RST", "VCC", "A7", "A6", "A5", "A4", "A3", "A2", "A1", "A0"
        };
        private static readonly string[] ArduinoProMiniRightPins =
        {
            "AREF", "D13", "D12", "D11", "D10", "D9", "D8", "D7", "D6", "D5", "D4", "D3", "D2", "D1", "D0", "GND2"
        };
        private static readonly string[] ArduinoProMiniPreferredPins =
        {
            "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D10", "D11", "D12", "D13", "D0", "D1",
            "A0", "A1", "A2", "A3", "A4", "A5", "A6", "A7",
            "GND1", "GND2", "VCC", "RAW", "RST", "AREF"
        };

        private readonly struct ArduinoProfile
        {
            public string Title { get; }
            public IReadOnlyList<string> LeftPins { get; }
            public IReadOnlyList<string> RightPins { get; }
            public IReadOnlyList<string> PreferredPins { get; }

            public ArduinoProfile(string title, IReadOnlyList<string> leftPins, IReadOnlyList<string> rightPins, IReadOnlyList<string> preferredPins)
            {
                Title = title;
                LeftPins = leftPins;
                RightPins = rightPins;
                PreferredPins = preferredPins;
            }
        }

        // Tools
        private enum ToolMode { Select, Move, Wire }
        private ToolMode _currentTool = ToolMode.Select;
        private enum BottomPanelMode { Output, Code }
        private BottomPanelMode _bottomMode = BottomPanelMode.Output;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) return;

            // Ensure VisualTree is loaded
             if (_doc.visualTreeAsset == null)
            {
                var uxml = Resources.Load<VisualTreeAsset>("UI/CircuitStudio");
                if (uxml != null) _doc.visualTreeAsset = uxml;
            }

            if (_doc.visualTreeAsset != null && _doc.rootVisualElement == null)
            {
                _doc.enabled = false;
                _doc.enabled = true;
            }

            _root = _doc.rootVisualElement;
            if (_root == null) return;

            InitializeUI();
            InitializeSession();
            
            // Redirect Console
            Application.logMessageReceived += OnLogMessage;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLogMessage;
            CancelLibraryDrag();
            if (_isResizing && _resizeTarget != null && _resizePointerId != -1 && _resizeTarget.HasPointerCapture(_resizePointerId))
            {
                _resizeTarget.ReleasePointer(_resizePointerId);
            }
            _isResizing = false;
            _resizePointerId = -1;
            _resizeMode = ResizeMode.None;
        }

        private void InitializeUI()
        {
            // --- Phase 2: Library Binding ---
            _workspaceArea = _root.Q<VisualElement>(className: "workspace-area");
            _leftPanel = _root.Q<VisualElement>(className: "panel-left");
            _rightPanel = _root.Q<VisualElement>(className: "panel-right");
            _bottomPanel = _root.Q<VisualElement>(className: "bottom-panel");
            _leftResizer = _root.Q<VisualElement>(className: "left-resizer");
            _rightResizer = _root.Q<VisualElement>(className: "right-resizer");
            _bottomResizer = _root.Q<VisualElement>(className: "bottom-resizer");

            _libraryList = _root.Q<ScrollView>("LibraryList");
            _librarySearchField = _root.Q<TextField>("LibrarySearchField");
            if (_librarySearchField != null)
            {
                _librarySearchField.isDelayed = false;
                _librarySearchField.RegisterValueChangedCallback(_ => PopulateLibrary());
            }
            if (_libraryList != null) PopulateLibrary();

            _projectTreeList = _root.Q<ScrollView>("ProjectTreeList");
            _errorCountLabel = _root.Q<Label>("ErrorCountLabel");

            // --- Phase 3: Canvas Binding ---
            _canvasView = _root.Q(className: "canvas-view");
            if (_canvasView != null)
            {
                _canvasView.RegisterCallback<PointerDownEvent>(OnCanvasClick);
                _canvasView.RegisterCallback<GeometryChangedEvent>(_ => UpdateBoardLayout());
                EnsureCanvasDecorations();
                // Grid/Background is absolute, components added on top
                EnsureWireLayer();
                _canvasHud = _canvasView.Q<VisualElement>(className: "canvas-hud");
                if (_canvasHud != null)
                {
                    _canvasHud.pickingMode = PickingMode.Ignore;
                    _canvasHud.BringToFront();
                }
            }

            // --- Phase 4: Properties Binding ---
            _propertiesPanel = _root.Q(className: "panel-right");

            // --- Phase 5: Toolbar & Bottom ---
            _btnSelect = _root.Q<Button>("tool-select");
            _btnMove = _root.Q<Button>("tool-move");
            _btnWire = _root.Q<Button>("tool-wire");
            _btnSim = _root.Q<Button>("tool-sim"); // Or SimulateBtn? UXML has logic.
            
            _btnSelect?.RegisterCallback<ClickEvent>(_ => SetTool(ToolMode.Select));
            _btnMove?.RegisterCallback<ClickEvent>(_ => SetTool(ToolMode.Move));
            _btnWire?.RegisterCallback<ClickEvent>(_ => SetTool(ToolMode.Wire));

            var btnSimulate = _root.Q<Button>(className: "btn-primary"); // SIMULATE button
            btnSimulate?.RegisterCallback<ClickEvent>(OnRunSimulation);

            var btnDrc = _root.Q<Button>(className: "btn-secondary-outline");
            btnDrc?.RegisterCallback<ClickEvent>(_ => RunDrcCheck());

            _versionText = _root.Q<Label>(className: "version-text");
            if (_versionText != null) _versionText.text = $"v{Application.version}-editor";

            _outputConsole = _root.Q(className: "output-console");
            _outputConsole?.Clear(); // Clear placeholder logs
            _outputPanel = _root.Q<VisualElement>("OutputPanel");
            _codePanel = _root.Q<VisualElement>("CodePanel");
            _codeFileList = _root.Q<ScrollView>("CodeFileList");
            _codeEditor = _root.Q<TextField>("CodeEditor");
            _codeFileLabel = _root.Q<Label>("CodeFileLabel");
            _codeTargetDropdown = _root.Q<DropdownField>("CodeTargetBoard");
            _codeOpenBtn = _root.Q<Button>("CodeOpenBtn");
            _codeSaveBtn = _root.Q<Button>("CodeSaveBtn");
            _codeSaveAsBtn = _root.Q<Button>("CodeSaveAsBtn");
            _codeBuildBtn = _root.Q<Button>("CodeBuildBtn");
            _codeBuildRunBtn = _root.Q<Button>("CodeBuildRunBtn");
            _codeBuildAllBtn = _root.Q<Button>("CodeBuildAllBtn");
            _tabOutput = _root.Q<Button>("TabOutput");
            _tabCode = _root.Q<Button>("TabCode");

            InitializeCodePanel();

            // Global Drag Events
            _root.RegisterCallback<PointerMoveEvent>(OnGlobalDragMove);
            _root.RegisterCallback<PointerUpEvent>(OnGlobalDragEnd);
            _root.RegisterCallback<PointerMoveEvent>(OnResizeMove);
            _root.RegisterCallback<PointerUpEvent>(OnResizeEnd);
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown);

            InitializeResizers();
        }

        private void InitializeCodePanel()
        {
            if (_codeEditor != null)
            {
                _codeEditor.multiline = true;
                _codeEditor.isDelayed = false;
            }

            _tabOutput?.RegisterCallback<ClickEvent>(_ => SetBottomPanelMode(BottomPanelMode.Output));
            _tabCode?.RegisterCallback<ClickEvent>(_ => SetBottomPanelMode(BottomPanelMode.Code));
            _codeOpenBtn?.RegisterCallback<ClickEvent>(_ => OpenCodeFileDialog());
            _codeSaveBtn?.RegisterCallback<ClickEvent>(_ => SaveCodeFile(false));
            _codeSaveAsBtn?.RegisterCallback<ClickEvent>(_ => SaveCodeFile(true));
            _codeBuildBtn?.RegisterCallback<ClickEvent>(_ => RunCodeBuild());
            _codeBuildRunBtn?.RegisterCallback<ClickEvent>(_ => RunCodeBuildAndRun());
            _codeBuildAllBtn?.RegisterCallback<ClickEvent>(_ => RunBuildAll());
            if (_codeTargetDropdown != null)
            {
                _codeTargetDropdown.RegisterValueChangedCallback(evt =>
                {
                    if (evt == null) return;
                    if (_codeTargetLabels.TryGetValue(evt.newValue, out var id))
                    {
                        HandleCodeTargetChanged(id);
                    }
                });
            }

            SetBottomPanelMode(BottomPanelMode.Output);
        }

        private void InitializeSession()
        {
            if (SessionManager.Instance != null && SessionManager.Instance.CurrentCircuit != null)
            {
                _currentCircuit = SessionManager.Instance.CurrentCircuit;
                if (SessionManager.Instance.CurrentProject != null && string.IsNullOrWhiteSpace(_currentCircuit.Id))
                {
                    _currentCircuit.Id = SessionManager.Instance.CurrentProject.ProjectName;
                }
            }
            else
            {
                _currentCircuit = new CircuitSpec 
                { 
                    Id = "New Circuit",
                    Components = new List<ComponentSpec>(),
                    Nets = new List<NetSpec>()
                };
            }
            NormalizeCircuit();
            EnsureDefaultDemoCircuit();
            RebuildPinUsage();
            RefreshCanvas();
            PopulateProjectTree();
            UpdateErrorCount();
        }

        private void SetBottomPanelMode(BottomPanelMode mode)
        {
            _bottomMode = mode;
            if (_outputPanel != null)
            {
                _outputPanel.style.display = mode == BottomPanelMode.Output ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_codePanel != null)
            {
                _codePanel.style.display = mode == BottomPanelMode.Code ? DisplayStyle.Flex : DisplayStyle.None;
            }

            _tabOutput?.RemoveFromClassList("active-blue");
            _tabCode?.RemoveFromClassList("active-blue");
            if (mode == BottomPanelMode.Output) _tabOutput?.AddToClassList("active-blue");
            if (mode == BottomPanelMode.Code) _tabCode?.AddToClassList("active-blue");

            if (mode == BottomPanelMode.Code)
            {
                EnsureCodeWorkspace();
                RefreshCodeTargets();
                RefreshCodeFileList();
            }
        }

        private void EnsureCodeWorkspace()
        {
            string root = GetCodeRoot(_codeTargetComponentId);
            if (string.IsNullOrWhiteSpace(root)) return;
            Directory.CreateDirectory(root);

            bool hasFiles = Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly)
                .Any(path => IsCodeFile(path));
            if (!hasFiles)
            {
                string path = Path.Combine(root, "sketch.ino");
                File.WriteAllText(path, GetDefaultSketch(_codeTargetComponentId));
            }
        }

        private void RefreshCodeFileList()
        {
            if (_codeFileList == null) return;
            _codeFileList.Clear();

            string root = GetCodeRoot(_codeTargetComponentId);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

            var files = Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly)
                .Where(IsCodeFile)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in files)
            {
                var btn = new Button(() => LoadCodeFile(file));
                btn.text = Path.GetFileName(file);
                btn.AddToClassList("code-file-item");
                if (string.Equals(file, _activeCodePath, StringComparison.OrdinalIgnoreCase))
                {
                    btn.AddToClassList("active");
                }
                _codeFileList.Add(btn);
            }
        }

        private void OpenCodeFileDialog()
        {
            string root = GetCodeRoot(_codeTargetComponentId);
            if (string.IsNullOrWhiteSpace(root)) return;
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanel("Open Sketch", root, "ino,cpp,h,hpp");
#else
            string path = Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(IsCodeFile);
#endif
            if (!string.IsNullOrWhiteSpace(path))
            {
                LoadCodeFile(path);
            }
        }

        private void LoadCodeFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            string content = File.ReadAllText(path);
            _activeCodePath = path;
            if (_codeEditor != null)
            {
                _codeEditor.SetValueWithoutNotify(content);
            }
            UpdateCodeFileLabel();
            RefreshCodeFileList();
        }

        private void SaveCodeFile(bool saveAs)
        {
            string root = GetCodeRoot(_codeTargetComponentId);
            if (string.IsNullOrWhiteSpace(root)) return;

            string target = _activeCodePath;
            if (saveAs || string.IsNullOrWhiteSpace(target))
            {
#if UNITY_EDITOR
                target = UnityEditor.EditorUtility.SaveFilePanel("Save Sketch", root, "sketch", "ino");
#else
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                target = Path.Combine(root, $"sketch_{stamp}.ino");
#endif
            }

            if (string.IsNullOrWhiteSpace(target)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string content = _codeEditor?.value ?? string.Empty;
            File.WriteAllText(target, content);
            _activeCodePath = target;
            UpdateCodeFileLabel();
            RefreshCodeFileList();
            Debug.Log($"[CodeStudio] Saved: {target}");
        }

        private void UpdateCodeFileLabel()
        {
            if (_codeFileLabel == null) return;
            if (string.IsNullOrWhiteSpace(_activeCodePath))
            {
                _codeFileLabel.text = "No file loaded";
                return;
            }
            _codeFileLabel.text = Path.GetFileName(_activeCodePath);
        }

        private string GetCodeRoot(string componentId)
        {
            string root = string.Empty;
            if (SessionManager.Instance != null && !string.IsNullOrWhiteSpace(SessionManager.Instance.CurrentProjectPath))
            {
                string projectDir = Path.GetDirectoryName(SessionManager.Instance.CurrentProjectPath);
                if (!string.IsNullOrWhiteSpace(projectDir))
                {
                    root = string.IsNullOrWhiteSpace(componentId)
                        ? Path.Combine(projectDir, "Code")
                        : Path.Combine(projectDir, "Code", componentId);
                }
            }

            if (string.IsNullOrWhiteSpace(root))
            {
                root = string.IsNullOrWhiteSpace(componentId)
                    ? Path.Combine(Application.persistentDataPath, "CodeStudio")
                    : Path.Combine(Application.persistentDataPath, "CodeStudio", componentId);
            }
            return root;
        }

        private static bool IsCodeFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".ino" || ext == ".cpp" || ext == ".h" || ext == ".hpp";
        }

        private static string GetDefaultSketch(string componentId)
        {
            string id = string.IsNullOrWhiteSpace(componentId) ? "U1" : componentId;
            return $"// Target: {id}\\n\\nvoid setup()\\n{{\\n  pinMode(13, OUTPUT);\\n}}\\n\\nvoid loop()\\n{{\\n  digitalWrite(13, HIGH);\\n  delay(500);\\n  digitalWrite(13, LOW);\\n  delay(500);\\n}}\\n";
        }

        private void RunCodeBuild()
        {
            RunCodeBuild(false);
        }

        private void RunCodeBuildAndRun()
        {
            RunCodeBuild(true);
        }

        private bool RunCodeBuild(bool runAfter)
        {
            EnsureCodeWorkspace();
            if (string.IsNullOrWhiteSpace(_activeCodePath))
            {
                SaveCodeFile(true);
            }

            string inoPath = ResolveSketchPath();
            if (string.IsNullOrWhiteSpace(inoPath))
            {
                Debug.LogWarning("[CodeStudio] No .ino file found to build.");
                return false;
            }

            var targetBoard = ResolveTargetBoard();
            if (targetBoard == null)
            {
                Debug.LogWarning("[CodeStudio] No Arduino target found.");
                return false;
            }

            string repoRoot = ResolveRepoRoot();
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                Debug.LogWarning("[CodeStudio] Repo root not found. Build cancelled.");
                return false;
            }

            string toolPath = Path.Combine(repoRoot, "tools", "scripts", "build_bvm.py");
            if (!File.Exists(toolPath))
            {
                Debug.LogWarning($"[CodeStudio] build_bvm.py not found at {toolPath}");
                return false;
            }

            string fqbn = ResolveFqbn(targetBoard);
            string codeRoot = GetCodeRoot(_codeTargetComponentId);
            string outDir = Path.Combine(codeRoot, "build");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(inoPath)}.bvm");

            string pythonExe = ResolvePythonExecutable(out string prefixArgs);
            string args = $"{prefixArgs} \"{toolPath}\" --ino \"{inoPath}\" --fqbn {fqbn} --out \"{outPath}\"";
            string includeRoot = codeRoot;
            string includeLib = Path.Combine(codeRoot, "lib");
            if (Directory.Exists(includeRoot)) args += $" --include \"{includeRoot}\"";
            if (Directory.Exists(includeLib)) args += $" --include \"{includeLib}\"";

            Debug.Log($"[CodeStudio] Build start: {Path.GetFileName(inoPath)} ({fqbn})");
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = args,
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var proc = new System.Diagnostics.Process { StartInfo = startInfo })
            {
                proc.OutputDataReceived += (_, evt) =>
                {
                    if (!string.IsNullOrWhiteSpace(evt.Data))
                    {
                        Debug.Log($"[CodeStudio] {evt.Data}");
                    }
                };
                proc.ErrorDataReceived += (_, evt) =>
                {
                    if (!string.IsNullOrWhiteSpace(evt.Data))
                    {
                        Debug.LogWarning($"[CodeStudio] {evt.Data}");
                    }
                };

                try
                {
                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    proc.WaitForExit();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CodeStudio] Build failed to start: {ex.Message}");
                    return false;
                }

                if (proc.ExitCode != 0)
                {
                    Debug.LogWarning($"[CodeStudio] Build failed (exit {proc.ExitCode}).");
                    return false;
                }
            }

            string hexPath = FindLatestHex(Path.Combine(outDir, "bvm_build"));
            if (string.IsNullOrWhiteSpace(hexPath))
            {
                Debug.LogWarning("[CodeStudio] Build completed but no .hex was found.");
                return false;
            }

            ApplyFirmwarePaths(targetBoard, hexPath, outPath, fqbn);
            SaveCurrentProject();
            Debug.Log($"[CodeStudio] Build ok. Hex: {hexPath}");
            if (runAfter)
            {
                StartRunMode();
            }
            return true;
        }

        private void RunBuildAll()
        {
            if (_currentCircuit == null || _currentCircuit.Components == null) return;
            var targets = _currentCircuit.Components.Where(c => IsArduinoType(c.Type)).ToList();
            if (targets.Count == 0)
            {
                Debug.LogWarning("[CodeStudio] No Arduino targets to build.");
                return;
            }

            string previousTarget = _codeTargetComponentId;
            int success = 0;
            foreach (var target in targets)
            {
                HandleCodeTargetChanged(target.Id);
                bool built = RunCodeBuild(false);
                if (built) success++;
            }

            HandleCodeTargetChanged(previousTarget);
            Debug.Log($"[CodeStudio] Build All complete: {success}/{targets.Count} targets.");
        }

        private string ResolveSketchPath()
        {
            string root = GetCodeRoot(_codeTargetComponentId);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;

            if (!string.IsNullOrWhiteSpace(_activeCodePath) &&
                _activeCodePath.EndsWith(".ino", StringComparison.OrdinalIgnoreCase))
            {
                return _activeCodePath;
            }

            return Directory.EnumerateFiles(root, "*.ino", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private ComponentSpec ResolveTargetBoard()
        {
            if (_currentCircuit == null || _currentCircuit.Components == null) return null;
            if (!string.IsNullOrWhiteSpace(_codeTargetComponentId))
            {
                var match = _currentCircuit.Components.FirstOrDefault(c => c.Id == _codeTargetComponentId);
                if (match != null && IsArduinoType(match.Type)) return match;
            }
            return _currentCircuit.Components.FirstOrDefault(c => IsArduinoType(c.Type));
        }

        private string ResolveFqbn(ComponentSpec board)
        {
            if (board != null && board.Properties != null && board.Properties.TryGetValue("virtualBoard", out var id))
            {
                string key = (id ?? string.Empty).Trim().ToLowerInvariant();
                if (key.Contains("nano")) return "arduino:avr:nano";
                if (key.Contains("pro-mini") || key.Contains("pro_mini") || key.Contains("promini")) return "arduino:avr:pro";
            }

            if (board != null && board.Type != null)
            {
                string type = board.Type.Trim().ToLowerInvariant();
                if (type.Contains("nano")) return "arduino:avr:nano";
                if (type.Contains("pro")) return "arduino:avr:pro";
            }

            return "arduino:avr:uno";
        }

        private string ResolveRepoRoot()
        {
            try
            {
                var dataDir = new DirectoryInfo(Application.dataPath);
                return dataDir.Parent?.Parent?.FullName;
            }
            catch
            {
                return null;
            }
        }

        private string ResolvePythonExecutable(out string prefixArgs)
        {
            prefixArgs = string.Empty;
            if (Application.platform == RuntimePlatform.WindowsEditor ||
                Application.platform == RuntimePlatform.WindowsPlayer)
            {
                prefixArgs = "-3";
                return "py";
            }
            return "python3";
        }

        private string FindLatestHex(string buildDir)
        {
            if (string.IsNullOrWhiteSpace(buildDir) || !Directory.Exists(buildDir)) return null;
            var hex = Directory.EnumerateFiles(buildDir, "*.hex", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            return hex?.FullName;
        }

        private void ApplyFirmwarePaths(ComponentSpec board, string hexPath, string bvmPath, string fqbn)
        {
            if (board == null) return;

            EnsureComponentProperties(board);
            board.Properties["firmwarePath"] = hexPath;
            board.Properties["firmware"] = hexPath;
            board.Properties["bvmPath"] = bvmPath;
            board.Properties["fqbn"] = fqbn;
        }

        private void RefreshCodeTargets()
        {
            if (_codeTargetDropdown == null) return;
            _codeTargetLabels.Clear();
            var choices = new List<string>();
            if (_currentCircuit != null && _currentCircuit.Components != null)
            {
                foreach (var comp in _currentCircuit.Components)
                {
                    if (!IsArduinoType(comp.Type)) continue;
                    string label = $"{comp.Id} ({comp.Type})";
                    _codeTargetLabels[label] = comp.Id;
                    choices.Add(label);
                }
            }

            if (choices.Count == 0)
            {
                choices.Add("No Arduino found");
                _codeTargetComponentId = null;
                _codeTargetDropdown.SetEnabled(false);
                _codeTargetDropdown.choices = choices;
                _codeTargetDropdown.SetValueWithoutNotify(choices[0]);
                return;
            }

            _codeTargetDropdown.SetEnabled(true);
            _codeTargetDropdown.choices = choices;
            string selected = choices[0];
            if (!string.IsNullOrWhiteSpace(_codeTargetComponentId))
            {
                string match = choices.FirstOrDefault(c => _codeTargetLabels[c] == _codeTargetComponentId);
                if (!string.IsNullOrWhiteSpace(match))
                {
                    selected = match;
                }
            }
            HandleCodeTargetChanged(_codeTargetLabels[selected]);
            _codeTargetDropdown.SetValueWithoutNotify(selected);
        }

        private void HandleCodeTargetChanged(string componentId)
        {
            _codeTargetComponentId = componentId;
            _activeCodePath = null;
            UpdateCodeFileLabel();
            EnsureCodeWorkspace();
            RefreshCodeFileList();
            AutoLoadFirstSketch();
        }

        private void AutoLoadFirstSketch()
        {
            string root = GetCodeRoot(_codeTargetComponentId);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
            string first = Directory.EnumerateFiles(root, "*.ino", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
            {
                LoadCodeFile(first);
            }
        }

        private void EnsureDefaultDemoCircuit()
        {
            if (_currentCircuit == null || _currentCircuit.Components == null) return;
            if (_currentCircuit.Components.Count > 0) return;

            var arduino = new ComponentSpec { Id = "U1", Type = "ArduinoUno" };
            var resistor = new ComponentSpec { Id = "R1", Type = "Resistor" };
            var led = new ComponentSpec { Id = "D1", Type = "LED" };

            _currentCircuit.Components.Add(arduino);
            _currentCircuit.Components.Add(resistor);
            _currentCircuit.Components.Add(led);

            ApplyDefaultProperties(arduino, ResolveCatalogItem(arduino));
            ApplyDefaultProperties(resistor, ResolveCatalogItem(resistor));
            ApplyDefaultProperties(led, ResolveCatalogItem(led));

            SetComponentPosition(arduino, new Vector2(100, 200));
            SetComponentPosition(resistor, new Vector2(300, 200));
            SetComponentPosition(led, new Vector2(400, 200));

            _currentCircuit.Nets = new List<NetSpec>
            {
                new NetSpec
                {
                    Id = "NET_SIG",
                    Nodes = new List<string> { "U1.D13", "R1.A" }
                },
                new NetSpec
                {
                    Id = "NET_LED",
                    Nodes = new List<string> { "R1.B", "D1.Anode" }
                },
                new NetSpec
                {
                    Id = "NET_GND",
                    Nodes = new List<string> { "D1.Cathode", "U1.GND1" }
                }
            };
        }

        private void InitializeResizers()
        {
            if (_leftResizer != null)
            {
                _leftResizer.RegisterCallback<PointerDownEvent>(e => BeginResize(e, ResizeMode.Left));
            }
            if (_rightResizer != null)
            {
                _rightResizer.RegisterCallback<PointerDownEvent>(e => BeginResize(e, ResizeMode.Right));
            }
            if (_bottomResizer != null)
            {
                _bottomResizer.RegisterCallback<PointerDownEvent>(e => BeginResize(e, ResizeMode.Bottom));
            }
        }

        private void BeginResize(PointerDownEvent evt, ResizeMode mode)
        {
            if (evt.button != 0 || _isResizing) return;
            _resizeMode = mode;
            _isResizing = true;
            _resizePointerId = evt.pointerId;
            _resizeStart = new Vector2(evt.position.x, evt.position.y);
            _startLeftWidth = _leftPanel?.resolvedStyle.width ?? 0f;
            _startRightWidth = _rightPanel?.resolvedStyle.width ?? 0f;
            _startBottomHeight = _bottomPanel?.resolvedStyle.height ?? 0f;

            _resizeTarget = evt.currentTarget as VisualElement;
            _resizeTarget?.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnResizeMove(PointerMoveEvent evt)
        {
            if (!_isResizing || evt.pointerId != _resizePointerId) return;
            Vector2 evtPos = new Vector2(evt.position.x, evt.position.y);
            Vector2 delta = evtPos - _resizeStart;

            switch (_resizeMode)
            {
                case ResizeMode.Left:
                    if (_leftPanel != null)
                    {
                        float width = Mathf.Clamp(_startLeftWidth + delta.x, MinLeftPanelWidth, MaxLeftPanelWidth);
                        _leftPanel.style.width = width;
                    }
                    break;
                case ResizeMode.Right:
                    if (_rightPanel != null)
                    {
                        float width = Mathf.Clamp(_startRightWidth - delta.x, MinRightPanelWidth, MaxRightPanelWidth);
                        _rightPanel.style.width = width;
                    }
                    break;
                case ResizeMode.Bottom:
                    if (_bottomPanel != null)
                    {
                        float height = Mathf.Clamp(_startBottomHeight - delta.y, MinBottomPanelHeight, MaxBottomPanelHeight);
                        _bottomPanel.style.height = height;
                    }
                    break;
            }
            UpdateBoardLayout();
        }

        private void OnResizeEnd(PointerUpEvent evt)
        {
            if (!_isResizing || evt.pointerId != _resizePointerId) return;
            _isResizing = false;

            if (_resizeTarget != null && _resizeTarget.HasPointerCapture(evt.pointerId))
            {
                _resizeTarget.ReleasePointer(evt.pointerId);
            }

            _resizePointerId = -1;
            _resizeMode = ResizeMode.None;
            _resizeTarget = null;
        }

        private void NormalizeCircuit()
        {
            if (_currentCircuit == null) return;
            if (_currentCircuit.Components == null) _currentCircuit.Components = new List<ComponentSpec>();
            if (_currentCircuit.Nets == null) _currentCircuit.Nets = new List<NetSpec>();
            foreach (var comp in _currentCircuit.Components)
            {
                EnsureComponentProperties(comp);
            }
        }
        private void EnsureCanvasDecorations()
        {
            if (_canvasView == null) return;
            if (_boardView == null)
            {
                _boardView = new VisualElement();
                _boardView.name = "CanvasBoard";
                _boardView.AddToClassList("canvas-board");
                _boardView.style.position = Position.Absolute;
                _canvasView.Add(_boardView);
            }

            if (_gridLayer == null)
            {
                _gridLayer = new GridLayer
                {
                    Spacing = GridSnap,
                    MajorLineEvery = 5
                };
                _gridLayer.name = "CanvasGrid";
                _gridLayer.AddToClassList("canvas-grid");
                _gridLayer.style.position = Position.Absolute;
                _gridLayer.style.left = 0;
                _gridLayer.style.top = 0;
                _gridLayer.style.right = 0;
                _gridLayer.style.bottom = 0;
                _boardView.Add(_gridLayer);
            }

            UpdateBoardLayout();
        }

        private void UpdateBoardLayout()
        {
            if (_canvasView == null || _boardView == null) return;
            var rect = _canvasView.contentRect;
            if (rect.width <= 0f || rect.height <= 0f) return;

            float width = Mathf.Max(MinBoardWidth, rect.width - (BoardPadding * 2f));
            float height = Mathf.Max(MinBoardHeight, rect.height - (BoardPadding * 2f));
            width = Mathf.Min(width, rect.width);
            height = Mathf.Min(height, rect.height);

            float left = (rect.width - width) * 0.5f;
            float top = (rect.height - height) * 0.5f;

            _boardView.style.left = left;
            _boardView.style.top = top;
            _boardView.style.width = width;
            _boardView.style.height = height;

            _gridLayer?.MarkDirtyRepaint();
        }

        private void EnsureWireLayer()
        {
            if (_canvasView == null || _wireLayer != null) return;
            EnsureCanvasDecorations();
            _wireLayer = new WireLayer();
            _wireLayer.style.position = Position.Absolute;
            _wireLayer.style.left = 0;
            _wireLayer.style.top = 0;
            _wireLayer.style.right = 0;
            _wireLayer.style.bottom = 0;
            _canvasView.Add(_wireLayer);
            _wireLayer.BringToFront();
        }

        private void UpdateWireLayer()
        {
            if (_canvasView == null || _wireLayer == null || _currentCircuit == null) return;
            if (_currentCircuit.Nets == null) return;
            _wireSegments.Clear();

            foreach (var net in _currentCircuit.Nets)
            {
                if (net.Nodes == null || net.Nodes.Count < 2) continue;
                var points = net.Nodes.Select(GetNodePosition).Where(p => p.HasValue).Select(p => p.Value).ToList();
                if (points.Count < 2) continue;

                var anchor = points[0];
                var color = GetNetColor(net.Id);
                for (int i = 1; i < points.Count; i++)
                {
                    AddOrthogonalSegments(anchor, points[i], color);
                }
            }

            ((WireLayer)_wireLayer).SetSegments(_wireSegments);
        }

        private void AddOrthogonalSegments(Vector2 start, Vector2 end, Color color)
        {
            if (Mathf.Approximately(start.x, end.x) || Mathf.Approximately(start.y, end.y))
            {
                AddWireSegment(start, end, color);
                return;
            }

            Vector2 mid = Mathf.Abs(end.x - start.x) >= Mathf.Abs(end.y - start.y)
                ? new Vector2(end.x, start.y)
                : new Vector2(start.x, end.y);

            AddWireSegment(start, mid, color);
            AddWireSegment(mid, end, color);
        }

        private void AddWireSegment(Vector2 start, Vector2 end, Color color)
        {
            if ((end - start).sqrMagnitude < 0.25f) return;
            _wireSegments.Add(new WireSegment
            {
                Start = start,
                End = end,
                Color = color
            });
        }

        private Color GetNetColor(string netId)
        {
            if (string.IsNullOrWhiteSpace(netId)) return new Color(0.2f, 0.8f, 1f, 0.9f);
            int hash = netId.GetHashCode();
            float hue = (Mathf.Abs(hash) % 360) / 360f;
            var baseColor = Color.HSVToRGB(hue, 0.5f, 0.9f);
            return new Color(baseColor.r, baseColor.g, baseColor.b, 0.9f);
        }

        private Vector2? GetNodePosition(string node)
        {
            if (string.IsNullOrWhiteSpace(node)) return null;
            var parts = node.Split('.');
            if (parts.Length < 1) return null;
            string compId = parts[0];
            if (_pinVisuals.TryGetValue(node, out var pinEl) && _canvasView != null)
            {
                var worldBound = pinEl.worldBound;
                if (worldBound.width > 0f && worldBound.height > 0f)
                {
                    return _canvasView.WorldToLocal(worldBound.center);
                }
            }

            if (!_componentPositions.TryGetValue(compId, out var pos))
            {
                pos = GetComponentPosition(compId);
                _componentPositions[compId] = pos;
            }
            if (_componentVisuals.TryGetValue(compId, out var compEl))
            {
                var rect = compEl.layout;
                return new Vector2(rect.x + rect.width * 0.5f, rect.y + rect.height * 0.5f);
            }
            return pos + new Vector2(ComponentWidth * 0.5f, ComponentHeight * 0.5f);
        }

        private Vector2 GetComponentPosition(string componentId)
        {
            var comp = _currentCircuit.Components.FirstOrDefault(c => c.Id == componentId);
            if (comp == null) return Vector2.zero;
            if (comp.Properties != null &&
                comp.Properties.TryGetValue("posX", out var xRaw) &&
                comp.Properties.TryGetValue("posY", out var yRaw))
            {
                if (float.TryParse(xRaw, out var x) && float.TryParse(yRaw, out var y)) return new Vector2(x, y);
            }
            return Vector2.zero;
        }

        private void SetComponentPosition(ComponentSpec spec, Vector2 pos)
        {
            EnsureComponentProperties(spec);
            var size = GetComponentSize(string.IsNullOrWhiteSpace(spec.Type) ? string.Empty : spec.Type);
            pos = ClampToViewport(pos, size);
            pos = ClampToBoard(pos, size);
            spec.Properties["posX"] = pos.x.ToString("F2");
            spec.Properties["posY"] = pos.y.ToString("F2");
            _componentPositions[spec.Id] = pos;
        }

        private void EnsureComponentProperties(ComponentSpec spec)
        {
            if (spec.Properties == null) spec.Properties = new Dictionary<string, string>();
        }

        private void RemovePinsForComponent(string componentId)
        {
            if (string.IsNullOrWhiteSpace(componentId)) return;
            var keys = _pinVisuals.Keys.Where(k => k.StartsWith(componentId + ".")).ToList();
            foreach (var key in keys)
            {
                _pinVisuals.Remove(key);
            }
        }

        private Vector2 ClampToBoard(Vector2 pos, Vector2 size)
        {
            if (_boardView == null) return pos;
            var rect = _boardView.layout;
            if (rect.width <= 0f || rect.height <= 0f) return pos;

            float minX = rect.xMin;
            float minY = rect.yMin;
            float maxX = rect.xMax - size.x;
            float maxY = rect.yMax - size.y;
            if (maxX < minX) maxX = minX;
            if (maxY < minY) maxY = minY;

            float x = Mathf.Clamp(pos.x, minX, maxX);
            float y = Mathf.Clamp(pos.y, minY, maxY);
            return new Vector2(x, y);
        }

        private Vector2 ClampToBoard(Vector2 pos)
        {
            return ClampToBoard(pos, new Vector2(ComponentWidth, ComponentHeight));
        }

        private Vector2 ClampToViewport(Vector2 pos, Vector2 size)
        {
            float maxX = Mathf.Max(0f, ViewportSize.x - size.x);
            float maxY = Mathf.Max(0f, ViewportSize.y - size.y);
            float x = Mathf.Clamp(pos.x, 0f, maxX);
            float y = Mathf.Clamp(pos.y, 0f, maxY);
            return new Vector2(x, y);
        }

        private void RebuildPinUsage()
        {
            _usedPins.Clear();
            if (_currentCircuit == null) return;
            if (_currentCircuit.Nets == null) return;
            foreach (var net in _currentCircuit.Nets)
            {
                foreach (var node in net.Nodes)
                {
                    var parts = node.Split('.');
                    if (parts.Length < 2) continue;
                    var compId = parts[0];
                    var pin = parts[1];
                    if (!_usedPins.TryGetValue(compId, out var pins))
                    {
                        pins = new HashSet<string>();
                        _usedPins[compId] = pins;
                    }
                    pins.Add(pin);
                }
            }
        }

        private void PopulateProjectTree()
        {
            if (_projectTreeList == null || _currentCircuit == null) return;
            _projectTreeList.Clear();

            var root = new VisualElement();
            root.AddToClassList("tree-root");

            var rootLabel = new Label($"v {_currentCircuit.Id}");
            rootLabel.AddToClassList("tree-item");
            rootLabel.AddToClassList("root-item");
            root.Add(rootLabel);

            foreach (var comp in _currentCircuit.Components)
            {
                var label = new Label($"   - {comp.Id} ({comp.Type})");
                label.AddToClassList("tree-item");
                root.Add(label);
            }

            if (_currentCircuit.Nets != null && _currentCircuit.Nets.Count > 0)
            {
                var netHeader = new Label($"   Nets ({_currentCircuit.Nets.Count})");
                netHeader.AddToClassList("tree-item");
                root.Add(netHeader);
            }

            _projectTreeList.Add(root);
        }

        private void UpdateErrorCount()
        {
            if (_errorCountLabel == null || _currentCircuit == null) return;
            var errors = CollectDrcErrors();
            _errorCountLabel.text = errors.Count == 0 ? "0 Errors" : $"{errors.Count} Errors";
        }

        private List<string> CollectDrcErrors()
        {
            var errors = new List<string>();
            if (_currentCircuit == null) return errors;
            if (_currentCircuit.Components.Count == 0) errors.Add("No components placed.");
            if (_currentCircuit.Components.Count > 1 && (_currentCircuit.Nets == null || _currentCircuit.Nets.Count == 0)) errors.Add("No nets created.");
            if (_currentCircuit.Nets == null) return errors;
            foreach (var net in _currentCircuit.Nets)
            {
                if (net.Nodes == null || net.Nodes.Count < 2) errors.Add($"Net {net.Id} has insufficient nodes.");
                foreach (var node in net.Nodes)
                {
                    var parts = node.Split('.');
                    if (parts.Length < 2) errors.Add($"Net {net.Id} has invalid node.");
                    if (_currentCircuit.Components.All(c => c.Id != parts[0])) errors.Add($"Net {net.Id} references missing component.");
                }
            }
            return errors;
        }

        private void RunDrcCheck()
        {
            var errors = CollectDrcErrors();
            if (errors.Count == 0)
            {
                Debug.Log("[CircuitStudio] DRC PASS");
            }
            else
            {
                Debug.Log($"[CircuitStudio] DRC FAIL ({errors.Count})");
                foreach (var err in errors) Debug.LogWarning($"[CircuitStudio] {err}");
            }
            UpdateErrorCount();
        }

        // --- Phase 2: Library Logic ---
        private void PopulateLibrary()
        {
            if (_libraryList == null) return;
            _libraryList.Clear();
            string query = _librarySearchField?.value ?? string.Empty;
            string queryLower = string.IsNullOrWhiteSpace(query) ? string.Empty : query.ToLowerInvariant();
            IEnumerable<ComponentCatalog.Item> items = ComponentCatalog.Items;
            if (!string.IsNullOrWhiteSpace(queryLower))
            {
                items = items.Where(i =>
                    (i.Name ?? string.Empty).ToLowerInvariant().Contains(queryLower) ||
                    (i.Description ?? string.Empty).ToLowerInvariant().Contains(queryLower) ||
                    (i.Type ?? string.Empty).ToLowerInvariant().Contains(queryLower));
            }

            foreach (var item in items)
            {
                var el = CreateLibraryItemElement(item);
                el.RegisterCallback<PointerDownEvent>(e => OnLibraryItemDragStart(e, item));
                _libraryList.Add(el);
            }
        }

        private VisualElement CreateLibraryItemElement(ComponentCatalog.Item item)
        {
            // Structure matched to UXML lines 87-93
            var row = new VisualElement();
            row.AddToClassList("lib-item");

            var iconBox = new VisualElement();
            iconBox.AddToClassList("lib-icon-box");
            var iconLbl = new Label(item.IconChar);
            iconBox.Add(iconLbl);
            row.Add(iconBox);

            var info = new VisualElement();
            var title = new Label(item.Name);
            title.AddToClassList("lib-item-title");
            var sub = new Label(item.Description);
            sub.AddToClassList("lib-item-sub");
            info.Add(title);
            info.Add(sub);
            row.Add(info);

            return row;
        }

        private void OnLibraryItemDragStart(PointerDownEvent evt, ComponentCatalog.Item item)
        {
            if (evt.button != 0) return;
            _isDragging = true;
            _dragItem = item;
            _dragPointerId = evt.pointerId;

            // Ghost Icon
            _ghostIcon = new VisualElement();
            _ghostIcon.pickingMode = PickingMode.Ignore;
            _ghostIcon.style.position = Position.Absolute;
            _ghostIcon.style.left = evt.position.x;
            _ghostIcon.style.top = evt.position.y;
            _ghostIcon.style.width = 30;
            _ghostIcon.style.height = 30;
            _ghostIcon.style.backgroundColor = new Color(0.2f, 0.4f, 0.8f, 0.5f); // Blue semi-transparent
            _ghostIcon.style.borderTopRightRadius = 4;
            _ghostIcon.style.borderTopLeftRadius = 4;
            _ghostIcon.style.borderBottomRightRadius = 4;
            _ghostIcon.style.borderBottomLeftRadius = 4;
            
            _root.Add(_ghostIcon);
            _root?.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnGlobalDragMove(PointerMoveEvent evt)
        {
            if (!_isDragging || _ghostIcon == null || evt.pointerId != _dragPointerId) return;
            _ghostIcon.style.left = evt.position.x;
            _ghostIcon.style.top = evt.position.y;
        }

        private void OnGlobalDragEnd(PointerUpEvent evt)
        {
            if (!_isDragging || evt.pointerId != _dragPointerId) return;
            _isDragging = false;
            if (_root != null && _root.HasPointerCapture(evt.pointerId))
            {
                _root.ReleasePointer(evt.pointerId);
            }
            _dragPointerId = -1;
            
            if (_ghostIcon != null)
            {
                _ghostIcon.RemoveFromHierarchy();
                _ghostIcon = null;
            }

            // Drop on Canvas?
            if (_canvasView != null && _canvasView.worldBound.Contains(evt.position))
            {
                Vector2 localPos = _canvasView.WorldToLocal(evt.position);
                InstantiateComponentOnCanvas(_dragItem, localPos);
            }
            _dragItem = default;
        }

        // --- Phase 3: Canvas Logic ---
        private void InstantiateComponentOnCanvas(ComponentCatalog.Item item, Vector2 position)
        {
            if (string.IsNullOrWhiteSpace(item.Type)) return;
            if (_currentCircuit == null) return;
            if (_currentCircuit.Components == null) _currentCircuit.Components = new List<ComponentSpec>();
            // Grid Snapping (Task 16: 10mm implicit grid)
            // Assuming 1 unit = 1mm for now, or just pixel snapping
            position.x = Mathf.Round(position.x / GridSnap) * GridSnap;
            position.y = Mathf.Round(position.y / GridSnap) * GridSnap;
            var size = GetComponentSize(item);
            position = ClampToViewport(position, size);
            position = ClampToBoard(position, size);

            var spec = new ComponentSpec
            {
                Type = item.Type,
                Id = $"{item.Symbol}{_currentCircuit.Components.Count + 1}",
                // Store visual position in metadata dictionary if Spec doesn't support it directly
                // For now, we rely on the runtime binding
            };
            EnsureComponentProperties(spec);

            // Hack: Persist position in a temp dict or misuse existing fields if needed
            // Since ComponentSpec is standard, we assume we can just manage the VisualElement position for this session.
            _currentCircuit.Components.Add(spec);
            ApplyDefaultProperties(spec, item);
            SetComponentPosition(spec, position);

            CreateComponentVisuals(spec, item, position);
            Debug.Log($"[CircuitStudio] Created {spec.Id} at {position}");
            PopulateProjectTree();
            UpdateErrorCount();
            UpdateWireLayer();
        }

        private void CreateComponentVisuals(ComponentSpec spec, ComponentCatalog.Item catalogItem, Vector2 pos)
        {
            // Task 13: visual component using .prop-card-header style logic broadly
            var el = new VisualElement();
            el.userData = spec; // Link back to data
            el.AddToClassList("circuit-component");
            el.style.position = Position.Absolute;
            var size = GetComponentSize(catalogItem);
            var clampedPos = ClampToViewport(pos, size);
            clampedPos = ClampToBoard(clampedPos, size);
            el.style.left = clampedPos.x;
            el.style.top = clampedPos.y;
            el.style.width = size.x;
            el.style.height = size.y;
            SetComponentPosition(spec, clampedPos);
            
            BuildComponentVisual(el, spec, catalogItem);

            // Selection Logic
            el.RegisterCallback<PointerDownEvent>(e => 
            {
                if (e.button == 0)
                {
                    if (_currentTool == ToolMode.Wire)
                    {
                        HandleWireClick(spec, catalogItem);
                    }
                    else
                    {
                        SelectComponent(el, spec, catalogItem);
                    }
                }
                e.StopPropagation();
            });

            // Move Logic (Task 17)
            el.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_currentTool == ToolMode.Move && e.pressedButtons == 1)
                {
                    // Drag logic for move 
                    // Simplified: just follow pointer center
                    var p = _canvasView.WorldToLocal(e.position);
                    p.x = Mathf.Round(p.x / GridSnap) * GridSnap; // Snap
                    p.y = Mathf.Round(p.y / GridSnap) * GridSnap;
                    p = ClampToViewport(p, size);
                    p = ClampToBoard(p, size);
                    el.style.left = p.x;
                    el.style.top = p.y;
                    SetComponentPosition(spec, p);
                    UpdateTransformFields(p);
                    UpdateWireLayer();
                }
            });

            _canvasView.Add(el);
            _componentVisuals[spec.Id] = el;
            BringCanvasHudToFront();
        }

        private void BuildComponentVisual(VisualElement root, ComponentSpec spec, ComponentCatalog.Item item)
        {
            switch (item.Type)
            {
                case "ArduinoUno":
                case "ArduinoNano":
                case "ArduinoProMini":
                    BuildArduinoComponentVisual(root, spec);
                    break;
                case "Resistor":
                    BuildTwoPinComponentVisual(root, spec, item, SymbolKind.Resistor);
                    break;
                case "Capacitor":
                    BuildTwoPinComponentVisual(root, spec, item, SymbolKind.Capacitor);
                    break;
                case "LED":
                    BuildTwoPinComponentVisual(root, spec, item, SymbolKind.Led);
                    break;
                case "DCMotor":
                    BuildTwoPinComponentVisual(root, spec, item, SymbolKind.Motor);
                    break;
                case "Battery":
                    BuildTwoPinComponentVisual(root, spec, item, SymbolKind.Battery);
                    break;
                case "Button":
                case "Switch":
                    BuildTwoPinComponentVisual(root, spec, item, SymbolKind.Generic);
                    break;
                default:
                    BuildGenericComponentVisual(root, spec, item);
                    break;
            }
        }

        private void BuildGenericComponentVisual(VisualElement root, ComponentSpec spec, ComponentCatalog.Item item)
        {
            var header = new VisualElement();
            header.AddToClassList("component-header");

            var iconBox = new VisualElement();
            iconBox.AddToClassList("component-icon");
            string iconText = string.IsNullOrWhiteSpace(item.IconChar)
                ? (string.IsNullOrWhiteSpace(spec.Type) ? "?" : spec.Type.Substring(0, 1))
                : item.IconChar;
            var iconLabel = new Label(iconText);
            iconBox.Add(iconLabel);
            header.Add(iconBox);

            var titleLabel = new Label(spec.Id);
            titleLabel.AddToClassList("component-title");
            header.Add(titleLabel);

            root.Add(header);

            var typeLbl = new Label(spec.Type);
            typeLbl.AddToClassList("component-subtitle");
            root.Add(typeLbl);

            ApplyComponentAccent(iconBox, iconLabel, item);
        }

        private void BuildTwoPinComponentVisual(VisualElement root, ComponentSpec spec, ComponentCatalog.Item item, SymbolKind kind)
        {
            root.AddToClassList("block-component");

            var row = new VisualElement();
            row.AddToClassList("block-row");

            var pinNames = item.Pins != null && item.Pins.Count >= 2
                ? item.Pins
                : new List<string> { "A", "B" };

            var leftGroup = new VisualElement();
            leftGroup.AddToClassList("block-pin");
            var leftLabel = new Label(pinNames[0]);
            leftLabel.AddToClassList("block-pin-label");
            var leftPin = CreatePinDot(spec.Id, pinNames[0]);
            leftPin.AddToClassList("block-pin-dot");
            leftGroup.Add(leftLabel);
            leftGroup.Add(leftPin);

            var body = new VisualElement();
            body.AddToClassList("block-body");
            var title = new Label(spec.Id);
            title.AddToClassList("block-title");
            var subtitle = new Label(item.Name);
            subtitle.AddToClassList("block-subtitle");
            body.Add(title);
            body.Add(subtitle);

            var rightGroup = new VisualElement();
            rightGroup.AddToClassList("block-pin");
            var rightPin = CreatePinDot(spec.Id, pinNames[1]);
            rightPin.AddToClassList("block-pin-dot");
            var rightLabel = new Label(pinNames[1]);
            rightLabel.AddToClassList("block-pin-label");
            rightGroup.Add(rightPin);
            rightGroup.Add(rightLabel);

            row.Add(leftGroup);
            row.Add(body);
            row.Add(rightGroup);
            root.Add(row);
        }

        private void BuildArduinoComponentVisual(VisualElement root, ComponentSpec spec)
        {
            root.AddToClassList("arduino-component");

            var body = new VisualElement();
            body.AddToClassList("arduino-body");

            var profile = GetArduinoProfile(spec.Type);
            var leftColumn = BuildArduinoPinColumn(spec, profile.LeftPins, true);
            var center = BuildArduinoCenter(spec, profile.Title);
            var rightColumn = BuildArduinoPinColumn(spec, profile.RightPins, false);

            body.Add(leftColumn);
            body.Add(center);
            body.Add(rightColumn);

            root.Add(body);
        }

        private VisualElement BuildArduinoPinColumn(ComponentSpec spec, IReadOnlyList<string> pins, bool isLeft)
        {
            var column = new VisualElement();
            column.AddToClassList("arduino-pin-column");
            column.AddToClassList(isLeft ? "left" : "right");

            foreach (var pin in pins)
            {
                var row = new VisualElement();
                row.AddToClassList("arduino-pin-row");
                row.AddToClassList(isLeft ? "left" : "right");

                var label = new Label(GetPinDisplayName(pin));
                label.AddToClassList("arduino-pin-label");

                var dot = CreatePinDot(spec.Id, pin);
                dot.AddToClassList("arduino-pin-dot");

                if (isLeft)
                {
                    row.Add(dot);
                    row.Add(label);
                }
                else
                {
                    row.Add(label);
                    row.Add(dot);
                }

                column.Add(row);
            }

            return column;
        }

        private VisualElement BuildArduinoCenter(ComponentSpec spec, string boardTitle)
        {
            var center = new VisualElement();
            center.AddToClassList("arduino-center");

            var title = new Label(boardTitle);
            title.AddToClassList("arduino-title");
            center.Add(title);

            var idLabel = new Label(spec.Id);
            idLabel.AddToClassList("arduino-subtitle");
            center.Add(idLabel);

            var ports = new VisualElement();
            ports.AddToClassList("arduino-ports");

            var usb = new VisualElement();
            usb.AddToClassList("arduino-port");
            usb.Add(new Label("USB"));

            var pwr = new VisualElement();
            pwr.AddToClassList("arduino-port");
            pwr.Add(new Label("PWR"));

            ports.Add(usb);
            ports.Add(pwr);
            var shield = new VisualElement();
            shield.AddToClassList("arduino-port");
            shield.Add(new Label("SHIELD"));
            ports.Add(shield);
            center.Add(ports);

            return center;
        }

        private ArduinoProfile GetArduinoProfile(string type)
        {
            if (string.Equals(type, "ArduinoNano", System.StringComparison.OrdinalIgnoreCase))
            {
                return new ArduinoProfile("ARDUINO NANO", ArduinoNanoLeftPins, ArduinoNanoRightPins, ArduinoNanoPreferredPins);
            }
            if (string.Equals(type, "ArduinoProMini", System.StringComparison.OrdinalIgnoreCase))
            {
                return new ArduinoProfile("ARDUINO PRO MINI", ArduinoProMiniLeftPins, ArduinoProMiniRightPins, ArduinoProMiniPreferredPins);
            }
            return new ArduinoProfile("ARDUINO UNO", ArduinoLeftPins, ArduinoRightPins, ArduinoPreferredPins);
        }

        private bool IsArduinoType(string type)
        {
            return string.Equals(type, "ArduinoUno", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "ArduinoNano", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "ArduinoProMini", System.StringComparison.OrdinalIgnoreCase);
        }

        private VisualElement CreatePinDot(string componentId, string pinName)
        {
            var dot = new VisualElement();
            dot.AddToClassList("pin-dot");
            dot.pickingMode = PickingMode.Ignore;
            string key = $"{componentId}.{pinName}";
            dot.name = key;
            _pinVisuals[key] = dot;
            return dot;
        }

        private string GetPinDisplayName(string pinName)
        {
            if (string.IsNullOrWhiteSpace(pinName)) return string.Empty;
            if (pinName.StartsWith("GND")) return "GND";
            return pinName;
        }

        private void BringCanvasHudToFront()
        {
            _canvasHud?.BringToFront();
        }

        private void ApplyComponentAccent(VisualElement iconBox, Label iconLabel, ComponentCatalog.Item item)
        {
            var color = GetAccentColor(item.Type);
            iconBox.style.backgroundColor = new Color(color.r, color.g, color.b, 0.2f);
            iconBox.style.borderTopColor = color;
            iconBox.style.borderBottomColor = color;
            iconBox.style.borderLeftColor = color;
            iconBox.style.borderRightColor = color;
            iconLabel.style.color = color;
        }

        private Color GetAccentColor(string type)
        {
            switch (type)
            {
                case "ArduinoUno": return new Color(0.23f, 0.51f, 0.96f);
                case "ArduinoNano": return new Color(0.16f, 0.61f, 0.55f);
                case "ArduinoProMini": return new Color(0.55f, 0.38f, 0.92f);
                case "Resistor": return new Color(0.94f, 0.76f, 0.28f);
                case "Capacitor": return new Color(0.34f, 0.77f, 0.75f);
                case "DCMotor": return new Color(0.94f, 0.52f, 0.36f);
                case "LED": return new Color(0.93f, 0.35f, 0.35f);
                default: return new Color(0.64f, 0.68f, 0.74f);
            }
        }

        private void SelectComponent(VisualElement visual, ComponentSpec spec, ComponentCatalog.Item catalogInfo)
        {
            // Deselect old
            if (_selectedVisual != null)
            {
                _selectedVisual.RemoveFromClassList("selected");
            }

            _selectedComponent = spec;
            _selectedVisual = visual;
            _selectedCatalogItem = catalogInfo;
            _selectedComponentSize = GetComponentSize(catalogInfo);

            ResetPropertyBindings();

            visual.AddToClassList("selected");

            UpdatePropertiesPanel(spec, catalogInfo, visual);
        }

        private void ResetPropertyBindings()
        {
            if (_propertiesPanel == null) return;
            var inputs = _propertiesPanel.Query<TextField>(className: "input-dark").ToList();
            foreach (var input in inputs) input.userData = null;
            _transformInputX = null;
            _transformInputY = null;
        }

        private void HandleWireClick(ComponentSpec spec, ComponentCatalog.Item catalogInfo)
        {
            if (_wireStartComponent == null)
            {
                _wireStartComponent = spec;
                _wireStartPin = GetNextAvailablePin(spec, catalogInfo);
                return;
            }

            if (_wireStartComponent == spec)
            {
                _wireStartComponent = null;
                _wireStartPin = string.Empty;
                return;
            }

            string endPin = GetNextAvailablePin(spec, catalogInfo);
            if (string.IsNullOrWhiteSpace(_wireStartPin) || string.IsNullOrWhiteSpace(endPin))
            {
                _wireStartComponent = null;
                _wireStartPin = string.Empty;
                return;
            }

            CreateNet(_wireStartComponent, _wireStartPin, spec, endPin);
            _wireStartComponent = null;
            _wireStartPin = string.Empty;
        }

        private void CancelWireMode()
        {
            _wireStartComponent = null;
            _wireStartPin = string.Empty;
        }

        private void CancelLibraryDrag()
        {
            if (!_isDragging) return;
            _isDragging = false;
            if (_root != null && _dragPointerId != -1 && _root.HasPointerCapture(_dragPointerId))
            {
                _root.ReleasePointer(_dragPointerId);
            }
            _dragPointerId = -1;
            if (_ghostIcon != null)
            {
                _ghostIcon.RemoveFromHierarchy();
                _ghostIcon = null;
            }
            _dragItem = default;
        }

        private void CreateNet(ComponentSpec a, string pinA, ComponentSpec b, string pinB)
        {
            if (_currentCircuit == null) return;
            if (_currentCircuit.Nets == null) _currentCircuit.Nets = new List<NetSpec>();
            var net = new NetSpec
            {
                Id = $"NET_{_currentCircuit.Nets.Count + 1}",
                Nodes = new List<string>
                {
                    $"{a.Id}.{pinA}",
                    $"{b.Id}.{pinB}"
                }
            };
            _currentCircuit.Nets.Add(net);

            MarkPinUsed(a.Id, pinA);
            MarkPinUsed(b.Id, pinB);

            UpdateWireLayer();
            UpdateErrorCount();
            PopulateProjectTree();
            if (_selectedVisual != null && _selectedComponent == a)
            {
                UpdatePropertiesPanel(a, ResolveCatalogItem(a), _selectedVisual);
            }
        }

        private void MarkPinUsed(string compId, string pin)
        {
            if (!_usedPins.TryGetValue(compId, out var pins))
            {
                pins = new HashSet<string>();
                _usedPins[compId] = pins;
            }
            pins.Add(pin);
        }

        private string GetNextAvailablePin(ComponentSpec spec, ComponentCatalog.Item info)
        {
            if (info.Pins == null || info.Pins.Count == 0) return "P1";
            if (!_usedPins.TryGetValue(spec.Id, out var pins))
            {
                pins = new HashSet<string>();
                _usedPins[spec.Id] = pins;
            }

            IEnumerable<string> orderedPins = info.Pins;
            if (IsArduinoType(info.Type))
            {
                var profile = GetArduinoProfile(info.Type);
                var preferred = new HashSet<string>(profile.PreferredPins);
                var available = new HashSet<string>(info.Pins);
                orderedPins = profile.PreferredPins.Where(available.Contains).Concat(info.Pins.Where(p => !preferred.Contains(p)));
            }

            foreach (var pin in orderedPins)
            {
                if (!pins.Contains(pin)) return pin;
            }
            return info.Pins.Last();
        }

        private void ApplyDefaultProperties(ComponentSpec spec, ComponentCatalog.Item item)
        {
            EnsureComponentProperties(spec);
            spec.Properties["label"] = item.Name;
            if (!string.IsNullOrWhiteSpace(item.Id))
            {
                spec.Properties["catalogId"] = item.Id;
            }

            if (item.DefaultProperties != null && item.DefaultProperties.Count > 0)
            {
                foreach (var kvp in item.DefaultProperties)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                    spec.Properties[kvp.Key] = kvp.Value;
                }
            }

            if (IsArduinoType(item.Type))
            {
                SetDefaultIfMissing(spec, "virtualBoard", item.Type == "ArduinoNano"
                    ? "arduino-nano"
                    : item.Type == "ArduinoProMini"
                        ? "arduino-pro-mini"
                        : "arduino-uno");
                SetDefaultIfMissing(spec, "clock", "16MHz");
                SetDefaultIfMissing(spec, "vcc", "5V");
                SetDefaultIfMissing(spec, "firmware", "blink:D13:500");
                SetDefaultIfMissing(spec, "nativeConfig", "{\"Core\":\"ATmega328P\",\"Clock\":\"16MHz\",\"Firmware\":\"User_Defined\"}");
            }
            else if (item.Type == "Resistor")
            {
                SetDefaultIfMissing(spec, "resistance", "220");
                SetDefaultIfMissing(spec, "nativeConfig", "{\"Value\":\"220\",\"Tolerance\":\"5%\"}");
            }
            else if (item.Type == "LED")
            {
                SetDefaultIfMissing(spec, "forwardV", "2.0V");
                SetDefaultIfMissing(spec, "current", "20mA");
                SetDefaultIfMissing(spec, "nativeConfig", "{\"Vf\":\"2.0V\",\"If_max\":\"20mA\"}");
            }
            else if (item.Type == "Battery")
            {
                SetDefaultIfMissing(spec, "voltage", "9V");
                SetDefaultIfMissing(spec, "capacity", "500mAh");
            }
        }

        private ComponentCatalog.Item ResolveCatalogItem(string type)
        {
            var item = ComponentCatalog.GetByType(type);
            if (string.IsNullOrWhiteSpace(item.Type)) return ComponentCatalog.CreateFallback(type);
            return item;
        }

        private ComponentCatalog.Item ResolveCatalogItem(ComponentSpec spec)
        {
            if (spec != null && spec.Properties != null && spec.Properties.TryGetValue("catalogId", out var id))
            {
                var byId = ComponentCatalog.GetById(id);
                if (!string.IsNullOrWhiteSpace(byId.Type)) return byId;
            }
            return ResolveCatalogItem(spec?.Type ?? string.Empty);
        }

        private void SetDefaultIfMissing(ComponentSpec spec, string key, string value)
        {
            if (spec == null || string.IsNullOrWhiteSpace(key)) return;
            if (!spec.Properties.ContainsKey(key))
            {
                spec.Properties[key] = value;
            }
        }

        private Vector2 GetComponentSize(ComponentCatalog.Item item)
        {
            return GetComponentSize(item.Type);
        }

        private Vector2 GetComponentSize(string type)
        {
            switch (type)
            {
                case "ArduinoUno": return new Vector2(260f, 240f);
                case "ArduinoNano": return new Vector2(240f, 200f);
                case "ArduinoProMini": return new Vector2(240f, 200f);
                case "Resistor": return new Vector2(140f, 50f);
                case "Capacitor": return new Vector2(120f, 50f);
                case "LED": return new Vector2(120f, 50f);
                case "DCMotor": return new Vector2(140f, 60f);
                case "Battery": return new Vector2(140f, 60f);
                default: return new Vector2(ComponentWidth, ComponentHeight);
            }
        }

        private void OnCanvasClick(PointerDownEvent evt)
        {
            // Click on empty canvas -> deselect
            if (evt.target == _canvasView)
            {
                if (_currentTool == ToolMode.Wire)
                {
                    CancelWireMode();
                }
                if (_selectedVisual != null)
                {
                    _selectedVisual.RemoveFromClassList("selected");
                }
                _selectedComponent = null;
                _selectedVisual = null;
                _selectedComponentSize = new Vector2(ComponentWidth, ComponentHeight);
                // Clear Properties logic? or just leave last
            }
        }

        // --- Phase 4: Properties Logic ---
        private void UpdatePropertiesPanel(ComponentSpec spec, ComponentCatalog.Item info, VisualElement visual)
        {
            if (_propertiesPanel == null) return;
            if (info.ElectricalSpecs == null) info.ElectricalSpecs = new Dictionary<string, string>();
            if (info.Pins == null) info.Pins = new List<string>();

            // Bind Header (Task 20)
            var title = _propertiesPanel.Q<Label>(className: "prop-title-lg");
            var sub = _propertiesPanel.Q<Label>(className: "prop-sub-lg");
            if (title != null) title.text = spec.Id;
            if (sub != null) sub.text = info.Name;

            // Bind Transforms (Task 21)
            // Assuming prop-section structure from UXML
            // We need to find the specific text fields. UXML doesn't give them IDs, just classes. 
            // We'll traverse order: PosX is likely first "input-dark" in prop-row-double
            var inputs = _propertiesPanel.Query<TextField>(className: "input-dark").ToList();
            if (inputs.Count >= 2)
            {
                var inputX = inputs[0];
                var inputY = inputs[1];
                _transformInputX = inputX;
                _transformInputY = inputY;
                
                inputX.SetValueWithoutNotify($"{visual.style.left.value.value} mm");
                inputY.SetValueWithoutNotify($"{visual.style.top.value.value} mm");

                // Bi-directional binding
                if (inputX.userData == null)
                {
                    inputX.userData = "bound";
                    inputX.RegisterValueChangedCallback(e =>
                    {
                        if (float.TryParse(e.newValue.Replace("mm", "").Trim(), out float v))
                        {
                            var size = GetComponentSize(info);
                            var p = ClampToBoard(new Vector2(v, visual.style.top.value.value), size);
                            visual.style.left = p.x;
                            visual.style.top = p.y;
                            SetComponentPosition(spec, p);
                            UpdateWireLayer();
                        }
                    });
                }
                if (inputY.userData == null)
                {
                    inputY.userData = "bound";
                    inputY.RegisterValueChangedCallback(e =>
                    {
                        if (float.TryParse(e.newValue.Replace("mm", "").Trim(), out float v))
                        {
                            var size = GetComponentSize(info);
                            var p = ClampToBoard(new Vector2(visual.style.left.value.value, v), size);
                            visual.style.left = p.x;
                            visual.style.top = p.y;
                            SetComponentPosition(spec, p);
                            UpdateWireLayer();
                        }
                    });
                }
            }

            // Bind Electrical Specs (Task 22)
            // Find "ELECTRICAL SPECS" section and populate
            // Complex to find exact parent without ID, but searching for label "ELECTRICAL SPECS" then parent works
            var headerLbl = _propertiesPanel.Query<Label>(className: "prop-section-header")
                .ToList()
                .FirstOrDefault(l => l.text.Contains("ELECTRICAL"));
            if (headerLbl != null)
            {
                var container = headerLbl.parent;
                // Clear existing rows except header
                var rows = container.Query<VisualElement>(className: "prop-row").ToList();
                rows.ForEach(r => r.RemoveFromHierarchy());

                // Add dynamic specs
                foreach (var kvp in info.ElectricalSpecs)
                {
                   var row = new VisualElement();
                   row.AddToClassList("prop-row");
                   var l = new Label(kvp.Key);
                   l.AddToClassList("prop-label-row");
                   var t = new TextField(kvp.Value);
                   t.AddToClassList("input-dark");
                   t.AddToClassList("input-right");
                   row.Add(l);
                   row.Add(t);
                   container.Add(row);
                }
            }

            var pinTable = _propertiesPanel.Q<VisualElement>(className: "pin-table");
            if (pinTable != null)
            {
                var rows = pinTable.Query<VisualElement>(className: "pin-row").ToList();
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].ClassListContains("header")) continue;
                    rows[i].RemoveFromHierarchy();
                }

                var nets = _currentCircuit?.Nets ?? new List<NetSpec>();
                foreach (var pin in info.Pins)
                {
                    string node = $"{spec.Id}.{pin}";
                    var net = nets.FirstOrDefault(n => n.Nodes != null && n.Nodes.Contains(node));
                    string netName = net != null && !string.IsNullOrWhiteSpace(net.Id) ? net.Id : "UNCONNECTED";

                    var row = new VisualElement();
                    row.AddToClassList("pin-row");

                    string displayPin = GetPinDisplayName(pin);
                    var pinLabel = new Label(displayPin);
                    pinLabel.style.width = 60;

                    var netLabel = new Label(netName);
                    netLabel.style.flexGrow = 1;
                    netLabel.style.color = netName == "UNCONNECTED" ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.3f, 0.8f, 0.3f);

                    var fnLabel = new Label(displayPin == "GND" ? "GND" : "I/O");
                    row.Add(pinLabel);
                    row.Add(netLabel);
                    row.Add(fnLabel);
                    pinTable.Add(row);
                }
            }
        }
        
        private void UpdateTransformFields(Vector2 pos)
        {
             if (_propertiesPanel == null) return;
             pos = ClampToBoard(pos, _selectedComponentSize);
             if (_transformInputX != null && _transformInputY != null)
             {
                 _transformInputX.SetValueWithoutNotify($"{pos.x} mm");
                 _transformInputY.SetValueWithoutNotify($"{pos.y} mm");
                 return;
             }
             var inputs = _propertiesPanel.Query<TextField>(className: "input-dark").ToList();
             if (inputs.Count >= 2)
             {
                 _transformInputX = inputs[0];
                 _transformInputY = inputs[1];
                 _transformInputX.SetValueWithoutNotify($"{pos.x} mm");
                 _transformInputY.SetValueWithoutNotify($"{pos.y} mm");
             }
        }

        // --- Phase 5: Toolbar & Tools ---
        private void SetTool(ToolMode tool)
        {
            _currentTool = tool;
            if (tool != ToolMode.Wire)
            {
                _wireStartComponent = null;
                _wireStartPin = string.Empty;
            }
            // Update UI State (Task 24)
            _btnSelect?.RemoveFromClassList("active");
            _btnMove?.RemoveFromClassList("active");
            _btnWire?.RemoveFromClassList("active");

            switch (tool)
            {
                case ToolMode.Select: _btnSelect?.AddToClassList("active"); break;
                case ToolMode.Move: _btnMove?.AddToClassList("active"); break;
                case ToolMode.Wire: _btnWire?.AddToClassList("active"); break;
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.target is TextField) return;
            if (evt.ctrlKey && evt.keyCode == KeyCode.S)
            {
                SaveCurrentProject();
                evt.StopPropagation();
                return;
            }
            if (evt.keyCode == KeyCode.Escape)
            {
                CancelWireMode();
                CancelLibraryDrag();
                evt.StopPropagation();
                return;
            }
            if (evt.keyCode == KeyCode.V)
            {
                SetTool(ToolMode.Select);
                evt.StopPropagation();
                return;
            }
            if (evt.keyCode == KeyCode.M)
            {
                SetTool(ToolMode.Move);
                evt.StopPropagation();
                return;
            }
            if (evt.keyCode == KeyCode.W)
            {
                SetTool(ToolMode.Wire);
                evt.StopPropagation();
                return;
            }
            // Task 18: Deletion
            if ((evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) && _selectedComponent != null && _selectedVisual != null)
            {
                _currentCircuit.Components.Remove(_selectedComponent);
                _selectedVisual.RemoveFromHierarchy();
                _componentVisuals.Remove(_selectedComponent.Id);
                RemovePinsForComponent(_selectedComponent.Id);
                if (_currentCircuit.Nets != null)
                {
                    _currentCircuit.Nets.RemoveAll(n => n.Nodes.Any(node => node.StartsWith(_selectedComponent.Id + ".")));
                }
                _usedPins.Remove(_selectedComponent.Id);
                _selectedVisual = null;
                _selectedComponent = null;
                UpdateWireLayer();
                PopulateProjectTree();
                UpdateErrorCount();
                Debug.Log("[CircuitStudio] Component Deleted.");
            }
        }

        private void OnRunSimulation(ClickEvent evt)
        {
             StartRunMode();
        }

        private void StartRunMode()
        {
            Debug.Log("[CircuitStudio] Transitioning to RunMode...");
            if (SessionManager.Instance != null)
            {
                _currentCircuit.Mode = RobotTwin.CoreSim.Specs.SimulationMode.Fast;
                SessionManager.Instance.StartSession(_currentCircuit);
            }
            UnityEngine.SceneManagement.SceneManager.LoadScene(2);
        }

        private void SaveCurrentProject()
        {
            if (_currentCircuit == null) return;
            var session = SessionManager.Instance;

            var project = session?.CurrentProject ?? new ProjectManifest
            {
                ProjectName = string.IsNullOrWhiteSpace(_currentCircuit.Id) ? "Untitled" : _currentCircuit.Id,
                Description = "CircuitStudio project",
                Version = "1.0.0",
                Circuit = _currentCircuit,
                Robot = session?.CurrentRobot ?? new RobotSpec { Name = "VirtualRobot" },
                World = session?.CurrentWorld ?? new WorldSpec { Name = "VirtualWorld" }
            };

            project.Circuit = _currentCircuit;

            string path = session?.CurrentProjectPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                string root = System.IO.Path.Combine(Application.persistentDataPath, "Projects");
                System.IO.Directory.CreateDirectory(root);
                string fileName = $"{project.ProjectName}.rtwin";
                path = System.IO.Path.Combine(root, fileName);
            }

            SimulationSerializer.SaveProject(project, path);
            session?.StartSession(project, path);
            Debug.Log($"[CircuitStudio] Project saved: {path}");
        }

        private void OnLogMessage(string logString, string stackTrace, LogType type)
        {
            // Task 26: Console Redirection
            if (_outputConsole == null) return;
            
            // Limit logs
            if (_outputConsole.childCount > 20) _outputConsole.RemoveAt(0);

            var line = new Label($"[{type}] {logString}");
            line.style.fontSize = 10;
            line.style.color = type == LogType.Error ? new Color(0.9f, 0.3f, 0.3f) : new Color(0.7f, 0.7f, 0.7f);
            
            _outputConsole.Add(line);
        }
        
        private void RefreshCanvas()
        {
            if (_canvasView == null || _currentCircuit == null) return;
            EnsureWireLayer();
            var visuals = _canvasView.Query<VisualElement>().ToList();
            foreach (var v in visuals)
            {
                if (v.userData is ComponentSpec) v.RemoveFromHierarchy();
            }

            _componentPositions.Clear();
            _componentVisuals.Clear();
            _pinVisuals.Clear();
            for (int i = 0; i < _currentCircuit.Components.Count; i++)
            {
                var comp = _currentCircuit.Components[i];
                var pos = GetComponentPosition(comp.Id);
                if (pos == Vector2.zero)
                {
                    pos = new Vector2(40 + (i * 30), 40 + (i * 20));
                }

                var item = ResolveCatalogItem(comp);
                pos = ClampToViewport(pos, GetComponentSize(item));
                pos = ClampToBoard(pos, GetComponentSize(item));
                SetComponentPosition(comp, pos);
                CreateComponentVisuals(comp, item, pos);
            }
            UpdateWireLayer();
        }
    }

    internal struct WireSegment
    {
        public Vector2 Start;
        public Vector2 End;
        public Color Color;
    }

    internal enum SymbolKind
    {
        Generic,
        Resistor,
        Capacitor,
        Led,
        Motor,
        Battery
    }

    internal class SchematicSymbolElement : VisualElement
    {
        public SymbolKind Kind { get; }

        public SchematicSymbolElement(SymbolKind kind)
        {
            Kind = kind;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            var rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f) return;

            var painter = ctx.painter2D;
            float left = 2f;
            float right = rect.width - 2f;
            float midY = rect.height * 0.5f;
            float amp = Mathf.Min(8f, rect.height * 0.25f);

            painter.lineWidth = 1.5f;
            painter.strokeColor = new Color(0.85f, 0.88f, 0.92f, 0.9f);

            switch (Kind)
            {
                case SymbolKind.Resistor:
                    DrawResistor(painter, left, right, midY, amp);
                    break;
                case SymbolKind.Capacitor:
                    DrawCapacitor(painter, left, right, midY, amp);
                    break;
                case SymbolKind.Led:
                    DrawLed(painter, left, right, midY, amp);
                    break;
                case SymbolKind.Motor:
                    DrawMotor(painter, left, right, midY, amp);
                    break;
                case SymbolKind.Battery:
                    DrawBattery(painter, left, right, midY, amp);
                    break;
                default:
                    DrawGeneric(painter, left, right, midY, amp);
                    break;
            }
        }

        private void DrawGeneric(Painter2D painter, float left, float right, float midY, float amp)
        {
            float boxWidth = (right - left) * 0.4f;
            float boxHeight = amp * 1.6f;
            float boxLeft = left + (right - left - boxWidth) * 0.5f;
            float boxTop = midY - boxHeight * 0.5f;

            painter.BeginPath();
            painter.MoveTo(new Vector2(left, midY));
            painter.LineTo(new Vector2(boxLeft, midY));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(boxLeft, boxTop));
            painter.LineTo(new Vector2(boxLeft + boxWidth, boxTop));
            painter.LineTo(new Vector2(boxLeft + boxWidth, boxTop + boxHeight));
            painter.LineTo(new Vector2(boxLeft, boxTop + boxHeight));
            painter.LineTo(new Vector2(boxLeft, boxTop));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(boxLeft + boxWidth, midY));
            painter.LineTo(new Vector2(right, midY));
            painter.Stroke();
        }

        private void DrawResistor(Painter2D painter, float left, float right, float midY, float amp)
        {
            int zigZags = 6;
            float span = right - left;
            float step = span / (zigZags + 1);

            painter.BeginPath();
            painter.MoveTo(new Vector2(left, midY));
            for (int i = 1; i <= zigZags; i++)
            {
                float x = left + (step * i);
                float y = (i % 2 == 0) ? midY - amp : midY + amp;
                painter.LineTo(new Vector2(x, y));
            }
            painter.LineTo(new Vector2(right, midY));
            painter.Stroke();
        }

        private void DrawCapacitor(Painter2D painter, float left, float right, float midY, float amp)
        {
            float center = (left + right) * 0.5f;
            float gap = 3f;
            float plateHeight = amp * 1.4f;

            painter.BeginPath();
            painter.MoveTo(new Vector2(left, midY));
            painter.LineTo(new Vector2(center - gap, midY));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(center - gap, midY - plateHeight));
            painter.LineTo(new Vector2(center - gap, midY + plateHeight));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(center + gap, midY - plateHeight));
            painter.LineTo(new Vector2(center + gap, midY + plateHeight));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(center + gap, midY));
            painter.LineTo(new Vector2(right, midY));
            painter.Stroke();
        }

        private void DrawLed(Painter2D painter, float left, float right, float midY, float amp)
        {
            float center = (left + right) * 0.5f;
            float diodeWidth = (right - left) * 0.25f;
            float barX = center + diodeWidth * 0.5f;

            painter.BeginPath();
            painter.MoveTo(new Vector2(left, midY));
            painter.LineTo(new Vector2(center - diodeWidth * 0.5f, midY));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(center - diodeWidth * 0.5f, midY - amp));
            painter.LineTo(new Vector2(center - diodeWidth * 0.5f, midY + amp));
            painter.LineTo(new Vector2(barX, midY));
            painter.LineTo(new Vector2(center - diodeWidth * 0.5f, midY - amp));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(barX, midY - amp));
            painter.LineTo(new Vector2(barX, midY + amp));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(barX, midY));
            painter.LineTo(new Vector2(right, midY));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(center + amp * 0.3f, midY - amp * 1.4f));
            painter.LineTo(new Vector2(center + amp, midY - amp * 2f));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(center + amp * 0.8f, midY - amp * 0.6f));
            painter.LineTo(new Vector2(center + amp * 1.5f, midY - amp * 1.2f));
            painter.Stroke();
        }

        private void DrawMotor(Painter2D painter, float left, float right, float midY, float amp)
        {
            float boxWidth = (right - left) * 0.35f;
            float boxHeight = amp * 1.8f;
            float boxLeft = left + (right - left - boxWidth) * 0.5f;
            float boxTop = midY - boxHeight * 0.5f;

            painter.BeginPath();
            painter.MoveTo(new Vector2(left, midY));
            painter.LineTo(new Vector2(boxLeft, midY));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(boxLeft, boxTop));
            painter.LineTo(new Vector2(boxLeft + boxWidth, boxTop));
            painter.LineTo(new Vector2(boxLeft + boxWidth, boxTop + boxHeight));
            painter.LineTo(new Vector2(boxLeft, boxTop + boxHeight));
            painter.LineTo(new Vector2(boxLeft, boxTop));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(boxLeft, boxTop));
            painter.LineTo(new Vector2(boxLeft + boxWidth, boxTop + boxHeight));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(boxLeft + boxWidth, boxTop));
            painter.LineTo(new Vector2(boxLeft, boxTop + boxHeight));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(boxLeft + boxWidth, midY));
            painter.LineTo(new Vector2(right, midY));
            painter.Stroke();
        }

        private void DrawBattery(Painter2D painter, float left, float right, float midY, float amp)
        {
            float center = (left + right) * 0.5f;
            float gap = 3f;
            float longPlate = amp * 1.6f;
            float shortPlate = amp * 0.9f;

            painter.BeginPath();
            painter.MoveTo(new Vector2(left, midY));
            painter.LineTo(new Vector2(center - gap, midY));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(center - gap, midY - longPlate));
            painter.LineTo(new Vector2(center - gap, midY + longPlate));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(center + gap, midY - shortPlate));
            painter.LineTo(new Vector2(center + gap, midY + shortPlate));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(center + gap, midY));
            painter.LineTo(new Vector2(right, midY));
            painter.Stroke();
        }
    }

    internal class GridLayer : VisualElement
    {
        public float Spacing = 10f;
        public int MajorLineEvery = 5;
        public Color MinorColor = new Color(1f, 1f, 1f, 0.04f);
        public Color MajorColor = new Color(1f, 1f, 1f, 0.08f);

        public GridLayer()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            var rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f || Spacing <= 2f) return;

            var painter = ctx.painter2D;
            int cols = Mathf.CeilToInt(rect.width / Spacing);
            int rows = Mathf.CeilToInt(rect.height / Spacing);

            for (int i = 0; i <= cols; i++)
            {
                float x = i * Spacing;
                bool major = MajorLineEvery > 0 && (i % MajorLineEvery == 0);
                painter.lineWidth = major ? 1.5f : 1f;
                painter.strokeColor = major ? MajorColor : MinorColor;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 0));
                painter.LineTo(new Vector2(x, rect.height));
                painter.Stroke();
            }

            for (int j = 0; j <= rows; j++)
            {
                float y = j * Spacing;
                bool major = MajorLineEvery > 0 && (j % MajorLineEvery == 0);
                painter.lineWidth = major ? 1.5f : 1f;
                painter.strokeColor = major ? MajorColor : MinorColor;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, y));
                painter.LineTo(new Vector2(rect.width, y));
                painter.Stroke();
            }
        }
    }

    internal class WireLayer : VisualElement
    {
        private readonly List<WireSegment> _segments = new List<WireSegment>();

        public WireLayer()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        public void SetSegments(IEnumerable<WireSegment> segments)
        {
            _segments.Clear();
            _segments.AddRange(segments);
            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            var painter = ctx.painter2D;
            foreach (var seg in _segments)
            {
                painter.lineWidth = 2f;
                painter.strokeColor = seg.Color;
                painter.BeginPath();
                painter.MoveTo(seg.Start);
                painter.LineTo(seg.End);
                painter.Stroke();
            }
        }
    }

    // Task 6: Data Structure
    public static class ComponentCatalog
    {
        public struct Item
        {
            public string Id;
            public string Name;
            public string Description;
            public string Type;
            public string Symbol;
            public string IconChar;
            public Dictionary<string, string> ElectricalSpecs;
            public Dictionary<string, string> DefaultProperties;
            public List<string> Pins;
            public int Order;
        }

        private const string ResourceFolder = "Components";
        private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly StringComparer TypeComparer = StringComparer.OrdinalIgnoreCase;
        private static List<Item> _items = new List<Item>();
        private static Dictionary<string, Item> _byId = new Dictionary<string, Item>(IdComparer);
        private static bool _loaded;

        public static List<Item> Items
        {
            get
            {
                EnsureLoaded();
                return _items;
            }
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _items = new List<Item>();
            _byId = new Dictionary<string, Item>(IdComparer);

            LoadFromResources();
            LoadFromStreamingAssets();

            if (_items.Count == 0)
            {
                Debug.LogWarning("[ComponentCatalog] No component JSON files found. Using fallback definitions.");
                _items.Add(CreateFallback("ArduinoUno"));
                _items.Add(CreateFallback("Resistor"));
            }

            _items = _items.OrderBy(i => i.Order).ThenBy(i => i.Name).ToList();
            _loaded = true;
        }

        public static Item GetById(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(id)) return default;
            if (_byId.TryGetValue(id, out var item)) return item;
            return default;
        }

        public static Item GetByType(string type)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(type)) return default;
            return _items.Where(i => TypeComparer.Equals(i.Type, type)).OrderBy(i => i.Order).FirstOrDefault();
        }

        private static void RegisterOrUpdate(Item item)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                item.Id = item.Type;
            }

            if (_byId.TryGetValue(item.Id, out var existing))
            {
                _items.Remove(existing);
                _byId[item.Id] = item;
                _items.Add(item);
                return;
            }

            _byId[item.Id] = item;
            _items.Add(item);
        }

        private static void LoadFromResources()
        {
            var assets = Resources.LoadAll<TextAsset>(ResourceFolder);
            if (assets == null || assets.Length == 0) return;

            foreach (var asset in assets)
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.text)) continue;
                if (TryParseDefinition(asset.text, out var item))
                {
                    RegisterOrUpdate(item);
                }
            }
        }

        private static void LoadFromStreamingAssets()
        {
            string root = Path.Combine(Application.streamingAssetsPath, ResourceFolder);
            if (!Directory.Exists(root)) return;

            var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                string json = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(json)) continue;
                if (TryParseDefinition(json, out var item))
                {
                    RegisterOrUpdate(item);
                }
            }
        }

        private static bool TryParseDefinition(string json, out Item item)
        {
            item = default;
            var def = JsonUtility.FromJson<ComponentDefinition>(json);
            if (def == null || string.IsNullOrWhiteSpace(def.type)) return false;

            item = new Item
            {
                Id = string.IsNullOrWhiteSpace(def.id) ? def.type : def.id,
                Name = string.IsNullOrWhiteSpace(def.name) ? def.type : def.name,
                Description = def.description ?? string.Empty,
                Type = def.type,
                Symbol = string.IsNullOrWhiteSpace(def.symbol) ? "?" : def.symbol,
                IconChar = string.IsNullOrWhiteSpace(def.iconChar) ? "?" : def.iconChar,
                ElectricalSpecs = ToDictionary(def.specs),
                DefaultProperties = ToDictionary(def.defaults),
                Pins = def.pins != null ? def.pins.ToList() : new List<string>(),
                Order = def.order <= 0 ? 1000 : def.order
            };
            return true;
        }

        private static Dictionary<string, string> ToDictionary(KeyValue[] entries)
        {
            var dict = new Dictionary<string, string>();
            if (entries == null) return dict;
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.key)) continue;
                dict[entry.key] = entry.value ?? string.Empty;
            }
            return dict;
        }

        [System.Serializable]
        private class ComponentDefinition
        {
            public string id;
            public string name;
            public string description;
            public string type;
            public string symbol;
            public string iconChar;
            public int order;
            public string[] pins;
            public KeyValue[] specs;
            public KeyValue[] defaults;
        }

        [System.Serializable]
        private class KeyValue
        {
            public string key;
            public string value;
        }

        public static Item CreateFallback(string type)
        {
            return new Item
            {
                Id = type,
                Name = type,
                Description = type,
                Type = type,
                Symbol = "U",
                IconChar = "U",
                ElectricalSpecs = new Dictionary<string, string>(),
                DefaultProperties = new Dictionary<string, string>(),
                Pins = new List<string> { "P1", "P2" },
                Order = 1000
            };
        }
    }
}

