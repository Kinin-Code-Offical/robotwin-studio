using System;
using UnityEngine;
using UnityEngine.UIElements;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Serialization;
using RobotTwin.Game;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

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
        private ScrollView _connectionsList;
        private Button _tabProjectBtn;
        private Button _tabConnectionsBtn;
        private VisualElement _canvasView;
        private VisualElement _boardView;
        private GridLayer _gridLayer;
        private VisualElement _canvasHud;
        private Label _canvasHudLabel;
        private VisualElement _propertiesPanel;
        private VisualElement _outputConsole;
        private ScrollView _outputPanel;
        private bool _outputAutoFollow = true;
        private VisualElement _codePanel;
        private VisualElement _circuit3DPanel;
        private VisualElement _circuit3DView;
        private ScrollView _codeFileList;
        private TextField _codeEditor;
        private Label _codeFileLabel;
        private DropdownField _codeTargetDropdown;
        private Button _codeOpenBtn;
        private Button _codeNewBtn;
        private Button _codeSaveBtn;
        private Button _codeSaveAsBtn;
        private Button _codeBuildBtn;
        private Button _codeBuildRunBtn;
        private Button _codeBuildAllBtn;
        private VisualElement _codeBuildProgressWrap;
        private ProgressBar _codeBuildProgress;
        private Button _centerTabCircuit;
        private Button _centerTabCode;
        private Button _centerTab3D;
        private Label _centerStatusLabel;
        private Button _outputErrorsBtn;
        private Button _outputAllBtn;
        private Button _outputWarningsBtn;
        private Button _outputClearBtn;
        private Button _bottomToggleBtn;
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
        private VisualElement _preferencesOverlay;
        private TextField _prefProjectNameField;
        private TextField _prefProjectIdField;
        private Button _prefSaveBtn;
        private Button _prefCancelBtn;
        private VisualElement _netEditorOverlay;
        private ScrollView _netEditorList;
        private Button _netEditorAddBtn;
        private Button _netEditorSaveBtn;
        private Button _netEditorCancelBtn;
        private Label _netEditorErrorLabel;
        private readonly List<NetEditorRow> _netEditorRows = new List<NetEditorRow>();

        private class NetEditorRow
        {
            public NetSpec Net;
            public VisualElement Row;
            public TextField NameField;
            public Label CountLabel;
            public Button DeleteButton;
            public bool DeleteRequested;
            public Button ConnsButton;
            public VisualElement ConnsPanel;
            public HashSet<string> Nodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // Toolbar
        private Button _btnSelect;
        private Button _btnMove;
        private Button _btnWire;
        private Button _btnSim;
        private Button _btnText;
        private Button _menuFile;
        private Button _menuEdit;
        private Button _menuView;
        private Button _menuDesign;
        private Button _menuTools;
        private Button _menuRoute;

        // Status
        private Label _statusLabel;
        private Label _versionText;
        private bool _bottomCollapsed;
        private float _bottomExpandedHeight;
        private bool _is3DDragging;
        private int _3dPointerId = -1;
        private Vector2 _3dLastPos;
        private ThreeDDragMode _3dDragMode = ThreeDDragMode.None;
        private float _canvasZoom = 1f;
        private Vector2 _canvasPan = Vector2.zero;
        private bool _isPanningCanvas;
        private int _panPointerId = -1;
        private Vector2 _panStart;
        private Vector2 _panOrigin;
        private bool _buildBusy;
        private bool _buildProgressIndeterminate = true;
        private float _buildProgressValue;
        private bool _lastBuildSucceeded;
        private int _buildProgressToken;
        private bool _isMovingComponent;
        private int _movePointerId = -1;
        private VisualElement _moveTarget;
        private ComponentSpec _moveTargetSpec;
        private Vector2 _moveOffset;
        private bool _wireUpdatePending;
        private float _wireUpdateAt;
        private Vector2 _lastCanvasWorldPos;

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
        private VisualElement _wirePreviewLayer;
        private readonly List<WireSegment> _wirePreviewSegments = new List<WireSegment>();
        private string _activeCodePath;
        private string _codeTargetComponentId;
        private readonly Dictionary<string, string> _codeTargetLabels = new Dictionary<string, string>();
        private Breadboard3DView _breadboardView;

        private enum OutputFilterMode
        {
            All,
            Warnings,
            Errors
        }

        private OutputFilterMode _outputFilterMode = OutputFilterMode.All;

        private struct LogEntry
        {
            public System.DateTime Time;
            public LogType Type;
            public string Message;
            public string Stack;
        }

        private struct CodeEditorSnapshot
        {
            public string Text;
            public int CursorIndex;
            public int SelectionIndex;
        }

        private readonly List<LogEntry> _logEntries = new List<LogEntry>();
        private readonly ConcurrentQueue<LogEntry> _pendingLogs = new ConcurrentQueue<LogEntry>();
        private bool _breadboardDirty;
        private readonly List<CodeEditorSnapshot> _codeHistory = new List<CodeEditorSnapshot>();
        private int _codeHistoryIndex = -1;
        private bool _suppressCodeHistory;
        private bool _suppressCodeEditorChanged;
        private Label _codeHighlightLabel;
        private TextElement _codeHighlightTarget;
        private VisualElement _codeHighlightInput;
        private bool _codeHighlightReady;
        private bool _codeHighlightEventsHooked;
        private string _lastHighlightText = string.Empty;

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
        private const float WireObstaclePadding = 12f;
        private bool _constrainComponentsToBoard = true;
        private const float BoardWorldWidth = 4000f;
        private const float BoardWorldHeight = 2400f;
        private const float AutoLayoutSpacing = 140f;
        private const float WireExitPadding = 6f;
        private const float WireUpdateInterval = 0.06f;
        private static readonly Color WirePreviewColor = new Color(0.45f, 0.82f, 1f, 0.8f);
        private static readonly Color[] WirePalette =
        {
            new Color(0.20f, 0.65f, 0.95f, 0.9f),
            new Color(0.95f, 0.55f, 0.20f, 0.9f),
            new Color(0.35f, 0.85f, 0.45f, 0.9f),
            new Color(0.90f, 0.30f, 0.35f, 0.9f),
            new Color(0.75f, 0.55f, 0.95f, 0.9f),
            new Color(0.95f, 0.85f, 0.25f, 0.9f),
            new Color(0.25f, 0.85f, 0.85f, 0.9f),
            new Color(0.90f, 0.45f, 0.75f, 0.9f),
            new Color(0.55f, 0.90f, 0.25f, 0.9f),
            new Color(0.25f, 0.55f, 0.90f, 0.9f),
            new Color(0.80f, 0.70f, 0.45f, 0.9f),
            new Color(0.55f, 0.55f, 0.60f, 0.9f)
        };
        private const int MaxOutputLines = 200;
        private const int MaxCodeHistory = 200;
        private const int CodeIndentSpaces = 4;
        private const int HighlightMaxChars = 20000;
        private const float MinCanvasZoom = 0.35f;
        private const float MaxCanvasZoom = 3f;
        private static readonly Vector2 ViewportSize = new Vector2(1920f, 1080f);
        private static readonly Regex CompilerMessageWithColumn = new Regex(
            @"^(?<file>.+?):(?<line>\d+):(?<col>\d+):\s*(?<type>error|warning|note):\s*(?<message>.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CompilerMessageNoColumn = new Regex(
            @"^(?<file>.+?):(?<line>\d+):\s*(?<type>error|warning|note):\s*(?<message>.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly HashSet<string> CodeKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "auto", "bool", "break", "case", "catch", "char", "class", "const", "constexpr",
            "continue", "default", "do", "double", "else", "enum", "extern", "false", "float",
            "for", "goto", "if", "inline", "int", "long", "namespace", "new", "nullptr", "operator",
            "private", "protected", "public", "register", "return", "short", "signed", "sizeof",
            "static", "struct", "switch", "template", "this", "throw", "true", "try", "typedef",
            "union", "unsigned", "using", "virtual", "void", "volatile", "while",
            "setup", "loop", "HIGH", "LOW", "INPUT", "OUTPUT", "INPUT_PULLUP"
        };
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
        private enum ToolMode { Select, Move, Wire, Text }
        private ToolMode _currentTool = ToolMode.Select;
        private enum CenterPanelMode { Circuit, Code, Preview3D }
        private CenterPanelMode _centerMode = CenterPanelMode.Circuit;
        private enum ThreeDDragMode { None, Pan, Orbit }

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

            UiResponsive.Bind(_root, 1200f, 1600f, "studio-compact", "studio-medium", "studio-wide");

            InitializeUI();
            InitializeSession();

            // Redirect Console (capture main + worker threads)
            Application.logMessageReceivedThreaded += OnLogMessageThreaded;
        }

        private void OnDisable()
        {
            Application.logMessageReceivedThreaded -= OnLogMessageThreaded;
            CancelLibraryDrag();
            if (_isResizing && _resizeTarget != null && _resizePointerId != -1 && _resizeTarget.HasPointerCapture(_resizePointerId))
            {
                _resizeTarget.ReleasePointer(_resizePointerId);
            }
            _isResizing = false;
            _resizePointerId = -1;
            _resizeMode = ResizeMode.None;
            if (_is3DDragging && _circuit3DView != null && _3dPointerId != -1 && _circuit3DView.HasPointerCapture(_3dPointerId))
            {
                _circuit3DView.ReleasePointer(_3dPointerId);
            }
            _is3DDragging = false;
            _3dPointerId = -1;
            _3dDragMode = ThreeDDragMode.None;
            if (_isPanningCanvas && _canvasView != null && _panPointerId != -1 && _canvasView.HasPointerCapture(_panPointerId))
            {
                _canvasView.ReleasePointer(_panPointerId);
            }
            _isPanningCanvas = false;
            _panPointerId = -1;
        }

        private void Update()
        {
            FlushPendingLogs();
            if (_wireUpdatePending && Time.unscaledTime >= _wireUpdateAt)
            {
                _wireUpdatePending = false;
                UpdateWireLayer();
            }
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
            if (_rightResizer != null) _rightResizer.style.display = DisplayStyle.None;

            _libraryList = _root.Q<ScrollView>("LibraryList");
            _librarySearchField = _root.Q<TextField>("LibrarySearchField");
            if (_librarySearchField != null)
            {
                _librarySearchField.isDelayed = false;
                _librarySearchField.RegisterValueChangedCallback(_ => PopulateLibrary());
            }
            if (_libraryList != null)
            {
                _libraryList.verticalScrollerVisibility = ScrollerVisibility.Auto;
                _libraryList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                PopulateLibrary();
            }

            _projectTreeList = _root.Q<ScrollView>("ProjectTreeList");
            _connectionsList = _root.Q<ScrollView>("ConnectionsList");
            _tabProjectBtn = _root.Q<Button>("TabProject");
            _tabConnectionsBtn = _root.Q<Button>("TabConnections");
            _errorCountLabel = _root.Q<Label>("ErrorCountLabel");
            if (_projectTreeList != null)
            {
                _projectTreeList.verticalScrollerVisibility = ScrollerVisibility.Auto;
                _projectTreeList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                RegisterRightClickMenu(_projectTreeList, menu =>
                {
                    menu.AddItem("Save Project", false, SaveCurrentProject);
                    menu.AddItem("Open Project", false, OpenProjectDialog);
                });
            }
            if (_connectionsList != null)
            {
                _connectionsList.verticalScrollerVisibility = ScrollerVisibility.Auto;
                _connectionsList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            }
            InitializeLeftTabs();

            // --- Phase 3: Canvas Binding ---
            _canvasView = _root.Q(className: "canvas-view");
            if (_canvasView != null)
            {
                _canvasView.RegisterCallback<PointerDownEvent>(OnCanvasClick);
                _canvasView.RegisterCallback<PointerDownEvent>(OnCanvasRightClick);
                _canvasView.RegisterCallback<ContextClickEvent>(OnCanvasContextClick);
                _canvasView.RegisterCallback<PointerDownEvent>(OnCanvasPanStart);
                _canvasView.RegisterCallback<PointerMoveEvent>(OnCanvasPanMove);
                _canvasView.RegisterCallback<PointerUpEvent>(OnCanvasPanEnd);
                _canvasView.RegisterCallback<WheelEvent>(OnCanvasWheel, TrickleDown.TrickleDown);
                _canvasView.RegisterCallback<PointerMoveEvent>(OnCanvasPointerMove);
                _canvasView.RegisterCallback<GeometryChangedEvent>(_ => UpdateBoardLayout());
                EnsureCanvasDecorations();
                // Grid/Background is absolute, components added on top
                EnsureWireLayer();
                _canvasHud = _canvasView.Q<VisualElement>(className: "canvas-hud");
                if (_canvasHud != null)
                {
                    _canvasHud.pickingMode = PickingMode.Ignore;
                    _canvasHud.BringToFront();
                    _canvasHudLabel = _canvasHud.Q<Label>(className: "hud-label");
                    if (_centerStatusLabel != null)
                    {
                        _canvasHud.style.display = DisplayStyle.None;
                    }
                    UpdateCanvasHud(null);
                }
            }

            if (_root != null)
            {
                _root.RegisterCallback<PointerDownEvent>(OnCanvasPanStart, TrickleDown.TrickleDown);
                _root.RegisterCallback<PointerMoveEvent>(OnCanvasPanMove, TrickleDown.TrickleDown);
                _root.RegisterCallback<PointerUpEvent>(OnCanvasPanEnd, TrickleDown.TrickleDown);
                _root.RegisterCallback<KeyDownEvent>(OnCanvasKeyDown);
            }

            // --- Phase 4: Properties Binding ---
            _propertiesPanel = _root.Q(className: "panel-right");

            // --- Phase 5: Toolbar & Bottom ---
            _btnSelect = _root.Q<Button>("tool-select");
            _btnMove = _root.Q<Button>("tool-move");
            _btnWire = _root.Q<Button>("tool-wire");
            _btnText = _root.Q<Button>("tool-text");
            _btnSim = _root.Q<Button>("tool-sim"); // Or SimulateBtn? UXML has logic.
            _menuFile = _root.Q<Button>("MenuFile");
            _menuEdit = _root.Q<Button>("MenuEdit");
            _menuView = _root.Q<Button>("MenuView");
            _menuDesign = _root.Q<Button>("MenuDesign");
            _menuTools = _root.Q<Button>("MenuTools");
            _menuRoute = _root.Q<Button>("MenuRoute");

            _btnSelect?.RegisterCallback<ClickEvent>(_ => SetTool(ToolMode.Select));
            _btnMove?.RegisterCallback<ClickEvent>(_ => SetTool(ToolMode.Move));
            _btnWire?.RegisterCallback<ClickEvent>(_ => SetTool(ToolMode.Wire));
            _btnText?.RegisterCallback<ClickEvent>(_ => SetTool(ToolMode.Text));
            _btnSim?.RegisterCallback<ClickEvent>(_ => StartRunMode());
            InitializeMenuBar();

            var btnSimulate = _root.Q<Button>(className: "btn-primary"); // SIMULATE button
            btnSimulate?.RegisterCallback<ClickEvent>(OnRunSimulation);

            var btnDrc = _root.Q<Button>(className: "btn-secondary");
            if (btnDrc == null)
            {
                btnDrc = _root.Q<Button>(className: "btn-secondary-outline");
            }
            btnDrc?.RegisterCallback<ClickEvent>(_ => RunDrcCheck());

            _versionText = _root.Q<Label>(className: "version-text");
            if (_versionText != null) _versionText.text = $"v{Application.version}-editor";

            _outputConsole = _root.Q(className: "output-console");
            _outputConsole?.Clear(); // Clear placeholder logs
            _outputPanel = _root.Q<ScrollView>("OutputPanel");
            if (_outputPanel != null)
            {
                _outputPanel.verticalScrollerVisibility = ScrollerVisibility.Auto;
                _outputPanel.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            }
            InitializeOutputAutoScroll();
            _codePanel = _root.Q<VisualElement>("CodePanel");
            _circuit3DPanel = _root.Q<VisualElement>("Circuit3DPanel");
            _circuit3DView = _root.Q<VisualElement>("Circuit3DView");
            _codeFileList = _root.Q<ScrollView>("CodeFileList");
            _codeEditor = _root.Q<TextField>("CodeEditor");
            if (_codeEditor == null) Debug.LogError("[CircuitStudio] TextField 'CodeEditor' not found in UI!");
            _codeFileLabel = _root.Q<Label>("CodeFileLabel");
            _codeTargetDropdown = _root.Q<DropdownField>("CodeTargetBoard");
            _codeOpenBtn = _root.Q<Button>("CodeOpenBtn");
            _codeNewBtn = _root.Q<Button>("CodeNewBtn");
            _codeSaveBtn = _root.Q<Button>("CodeSaveBtn");
            _codeSaveAsBtn = _root.Q<Button>("CodeSaveAsBtn");
            _codeBuildBtn = _root.Q<Button>("CodeBuildBtn");
            _codeBuildRunBtn = _root.Q<Button>("CodeBuildRunBtn");
            _codeBuildAllBtn = _root.Q<Button>("CodeBuildAllBtn");
            _codeBuildProgressWrap = _root.Q<VisualElement>("CodeBuildProgressWrap");
            _codeBuildProgress = _root.Q<ProgressBar>("CodeBuildProgress");
            _centerTabCircuit = _root.Q<Button>("CenterTabCircuit");
            _centerTabCode = _root.Q<Button>("CenterTabCode");
            _centerTab3D = _root.Q<Button>("CenterTab3D");
            _centerStatusLabel = _root.Q<Label>("CenterStatusLabel");
            if (_canvasHud != null && _centerStatusLabel != null)
            {
                _canvasHud.style.display = DisplayStyle.None;
                UpdateCanvasHud(null);
            }
            _outputErrorsBtn = _root.Q<Button>("OutputFilterErrorsBtn");
            _outputAllBtn = _root.Q<Button>("OutputFilterAllBtn");
            _outputWarningsBtn = _root.Q<Button>("OutputFilterWarningsBtn");
            _outputClearBtn = _root.Q<Button>("OutputClearBtn");
            _bottomToggleBtn = _root.Q<Button>("BottomToggleBtn");
            _preferencesOverlay = _root.Q<VisualElement>("PreferencesOverlay");
            _prefProjectNameField = _root.Q<TextField>("PrefProjectNameField");
            _prefProjectIdField = _root.Q<TextField>("PrefProjectIdField");
            _prefSaveBtn = _root.Q<Button>("PrefSaveBtn");
            _prefCancelBtn = _root.Q<Button>("PrefCancelBtn");
            if (_preferencesOverlay != null)
            {
                _preferencesOverlay.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.target == _preferencesOverlay)
                    {
                        HidePreferences();
                        evt.StopPropagation();
                    }
                });
            }
            _prefCancelBtn?.RegisterCallback<ClickEvent>(_ => HidePreferences());
            _prefSaveBtn?.RegisterCallback<ClickEvent>(_ => ApplyPreferences());

            _netEditorOverlay = _root.Q<VisualElement>("NetEditorOverlay");
            _netEditorList = _root.Q<ScrollView>("NetEditorList");
            _netEditorAddBtn = _root.Q<Button>("NetEditorAddBtn");
            _netEditorSaveBtn = _root.Q<Button>("NetEditorSaveBtn");
            _netEditorCancelBtn = _root.Q<Button>("NetEditorCancelBtn");
            _netEditorErrorLabel = _root.Q<Label>("NetEditorErrorLabel");
            if (_netEditorOverlay != null)
            {
                _netEditorOverlay.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.target == _netEditorOverlay)
                    {
                        HideNetEditor();
                    }
                });
            }
            _netEditorCancelBtn?.RegisterCallback<ClickEvent>(_ => HideNetEditor());
            _netEditorSaveBtn?.RegisterCallback<ClickEvent>(_ => ApplyNetEdits());
            _netEditorAddBtn?.RegisterCallback<ClickEvent>(_ => AddNetEditorRow(null));
            if (_bottomPanel != null)
            {
                _bottomPanel.RegisterCallback<GeometryChangedEvent>(evt =>
                {
                    if (_bottomCollapsed) return;
                    if (evt.newRect.height > 0f)
                    {
                        _bottomExpandedHeight = evt.newRect.height;
                    }
                });
            }

            InitializeCodePanel();
            InitializeBreadboardPreview();
            Initialize3DInput();

            // Global Drag Events
            _root.RegisterCallback<PointerMoveEvent>(OnGlobalDragMove);
            _root.RegisterCallback<PointerUpEvent>(OnGlobalDragEnd);
            _root.RegisterCallback<PointerMoveEvent>(OnResizeMove);
            _root.RegisterCallback<PointerUpEvent>(OnResizeEnd);
            _root.focusable = true;
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            InitializeResizers();
            _canvasView?.Focus();
        }

        private void InitializeCodePanel()
        {
            if (_codeEditor != null)
            {
                _codeEditor.multiline = true;
                _codeEditor.isDelayed = false;
                _codeEditor.RegisterValueChangedCallback(OnCodeEditorChanged);
                _codeEditor.RegisterCallback<KeyDownEvent>(OnCodeEditorKeyDown, TrickleDown.TrickleDown);
                RegisterRightClickMenu(_codeEditor, BuildCodeEditorMenu);
                ApplyCodeEditorTheme();
                _codeEditor.RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    ApplyCodeEditorTheme();
                    SetupCodeEditorSyntaxHighlighting();
                });
                _codeEditor.RegisterCallback<FocusInEvent>(_ =>
                {
                    ApplyCodeEditorTheme();
                    SyncCodeHighlightStyle();
                });
                _codeEditor.RegisterCallback<GeometryChangedEvent>(_ => SyncCodeHighlightStyle());
                SetupCodeEditorSyntaxHighlighting();
            }
            if (_codeFileList != null)
            {
                _codeFileList.verticalScrollerVisibility = ScrollerVisibility.Auto;
                _codeFileList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                RegisterRightClickMenu(_codeFileList, BuildCodeFileListMenu);
            }

            _centerTabCircuit?.RegisterCallback<ClickEvent>(_ => SetCenterPanelMode(CenterPanelMode.Circuit));
            _centerTabCode?.RegisterCallback<ClickEvent>(_ => SetCenterPanelMode(CenterPanelMode.Code));
            _centerTab3D?.RegisterCallback<ClickEvent>(_ => SetCenterPanelMode(CenterPanelMode.Preview3D));
            _outputAllBtn?.RegisterCallback<ClickEvent>(_ => SetOutputFilter(OutputFilterMode.All));
            _outputWarningsBtn?.RegisterCallback<ClickEvent>(_ => SetOutputFilter(OutputFilterMode.Warnings));
            _outputErrorsBtn?.RegisterCallback<ClickEvent>(_ => SetOutputFilter(OutputFilterMode.Errors));
            _outputClearBtn?.RegisterCallback<ClickEvent>(_ => ClearOutputLogs());
            _bottomToggleBtn?.RegisterCallback<ClickEvent>(_ => ToggleBottomPanel());
            _codeNewBtn?.RegisterCallback<ClickEvent>(_ => CreateNewCodeFile());
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

            SetCenterPanelMode(CenterPanelMode.Circuit);
            UpdateOutputFilterButtons();
        }

        private void InitializeLeftTabs()
        {
            if (_tabProjectBtn != null)
            {
                _tabProjectBtn.RegisterCallback<ClickEvent>(_ => SetLeftPanelMode(false));
            }
            if (_tabConnectionsBtn != null)
            {
                _tabConnectionsBtn.RegisterCallback<ClickEvent>(_ => SetLeftPanelMode(true));
            }
            SetLeftPanelMode(false);
        }

        private void SetLeftPanelMode(bool showConnections)
        {
            _tabProjectBtn?.RemoveFromClassList("active");
            _tabConnectionsBtn?.RemoveFromClassList("active");
            if (showConnections) _tabConnectionsBtn?.AddToClassList("active");
            else _tabProjectBtn?.AddToClassList("active");

            if (_projectTreeList != null)
            {
                _projectTreeList.style.display = showConnections ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (_connectionsList != null)
            {
                _connectionsList.style.display = showConnections ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (showConnections)
            {
                PopulateConnectionsList();
            }
        }

        private void InitializeMenuBar()
        {
            if (_menuFile != null) _menuFile.clicked += ShowFileMenu;
            if (_menuEdit != null) _menuEdit.clicked += ShowEditMenu;
            if (_menuView != null) _menuView.clicked += ShowViewMenu;
            if (_menuDesign != null) _menuDesign.clicked += ShowDesignMenu;
            if (_menuTools != null) _menuTools.clicked += ShowToolsMenu;
            if (_menuRoute != null) _menuRoute.clicked += ShowRouteMenu;
        }

        private void ShowFileMenu()
        {
            if (_root == null || _menuFile == null) return;
            var menu = new GenericDropdownMenu();
            menu.AddItem("New Project", false, CreateNewProject);
            menu.AddItem("Open Project", false, OpenProjectDialog);
            menu.AddSeparator(string.Empty);
            menu.AddItem("Save", false, SaveCurrentProject);
            menu.AddItem("Save As", false, SaveProjectAs);
            menu.AddSeparator(string.Empty);
            menu.AddItem("Preferences", false, ShowPreferences);
            menu.AddSeparator(string.Empty);
            menu.AddItem("Close Project", false, CloseProject);
            menu.AddItem("Quit", false, QuitApp);
            menu.DropDown(_menuFile.worldBound, _root, DropdownMenuSizeMode.Auto);
        }

        private void ShowPreferences()
        {
            if (_preferencesOverlay == null) return;
            string projectName = SessionManager.Instance?.CurrentProject?.ProjectName;
            if (string.IsNullOrWhiteSpace(projectName))
            {
                string projectPath = SessionManager.Instance?.CurrentProjectPath;
                if (!string.IsNullOrWhiteSpace(projectPath))
                {
                    projectName = Path.GetFileNameWithoutExtension(projectPath);
                }
            }
            if (string.IsNullOrWhiteSpace(projectName))
            {
                projectName = _currentCircuit?.Id;
            }
            if (string.IsNullOrWhiteSpace(projectName))
            {
                projectName = "Untitled Project";
            }

            if (_prefProjectNameField != null) _prefProjectNameField.value = projectName;
            if (_prefProjectIdField != null) _prefProjectIdField.value = _currentCircuit?.Id ?? string.Empty;
            _preferencesOverlay.style.display = DisplayStyle.Flex;
            _prefProjectNameField?.Focus();
        }

        private void HidePreferences()
        {
            if (_preferencesOverlay == null) return;
            _preferencesOverlay.style.display = DisplayStyle.None;
        }

        private void ApplyPreferences()
        {
            if (_currentCircuit == null)
            {
                HidePreferences();
                return;
            }

            string name = _prefProjectNameField?.value?.Trim();
            string id = _prefProjectIdField?.value?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = SessionManager.Instance?.CurrentProject?.ProjectName;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Untitled Project";
            }
            if (string.IsNullOrWhiteSpace(id))
            {
                id = _currentCircuit.Id;
            }
            if (string.IsNullOrWhiteSpace(id))
            {
                id = name;
            }

            _currentCircuit.Id = id;
            if (SessionManager.Instance?.CurrentProject != null)
            {
                SessionManager.Instance.CurrentProject.ProjectName = name;
                SessionManager.Instance.CurrentProject.Circuit = _currentCircuit;
            }

            PopulateProjectTree();
            UpdateErrorCount();
            SaveCurrentProject();
            HidePreferences();
        }

        private void ShowNetEditor()
        {
            if (_netEditorOverlay == null || _netEditorList == null) return;
            if (_currentCircuit == null) return;
            if (_currentCircuit.Nets == null) _currentCircuit.Nets = new List<NetSpec>();

            _netEditorList.Clear();
            _netEditorRows.Clear();
            SetNetEditorError(string.Empty);

            foreach (var net in _currentCircuit.Nets)
            {
                AddNetEditorRow(net);
            }

            _netEditorOverlay.style.display = DisplayStyle.Flex;
        }

        private void HideNetEditor()
        {
            if (_netEditorOverlay == null) return;
            _netEditorOverlay.style.display = DisplayStyle.None;
        }

        private void AddNetEditorRow(NetSpec net)
        {
            if (_netEditorList == null) return;

            var row = new VisualElement();
            row.AddToClassList("net-editor-row");

            var nameField = new TextField();
            nameField.AddToClassList("input-dark");
            nameField.AddToClassList("net-editor-name");
            nameField.value = !string.IsNullOrWhiteSpace(net?.Id)
                ? net.Id
                : GetUniqueNetName("NET_NEW");

            var countLabel = new Label(net?.Nodes != null ? $"{net.Nodes.Count}" : "0");
            countLabel.AddToClassList("net-editor-count");

            var connsBtn = new Button { text = "Conns" };
            connsBtn.AddToClassList("net-editor-conns-btn");

            var deleteBtn = new Button { text = "Delete" };
            deleteBtn.AddToClassList("net-editor-delete");

            var state = new NetEditorRow
            {
                Net = net,
                Row = row,
                NameField = nameField,
                CountLabel = countLabel,
                DeleteButton = deleteBtn,
                ConnsButton = connsBtn
            };
            if (net?.Nodes != null)
            {
                foreach (var node in net.Nodes)
                {
                    state.Nodes.Add(node);
                }
                countLabel.text = $"{state.Nodes.Count}";
            }

            deleteBtn.RegisterCallback<ClickEvent>(_ =>
            {
                state.DeleteRequested = !state.DeleteRequested;
                row.EnableInClassList("delete", state.DeleteRequested);
            });

            connsBtn.RegisterCallback<ClickEvent>(_ =>
            {
                if (state.ConnsPanel == null)
                {
                    state.ConnsPanel = BuildNetEditorConnectionsPanel(state);
                    int rowIndex = _netEditorList.IndexOf(state.Row);
                    if (rowIndex >= 0)
                    {
                        _netEditorList.Insert(rowIndex + 1, state.ConnsPanel);
                    }
                    else
                    {
                        _netEditorList.Add(state.ConnsPanel);
                    }
                }
                state.ConnsPanel.style.display = state.ConnsPanel.style.display == DisplayStyle.Flex
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            });

            row.Add(nameField);
            row.Add(countLabel);
            row.Add(connsBtn);
            row.Add(deleteBtn);
            _netEditorList.Add(row);
            _netEditorRows.Add(state);
        }

        private VisualElement BuildNetEditorConnectionsPanel(NetEditorRow row)
        {
            var panel = new VisualElement();
            panel.AddToClassList("net-editor-conns");
            panel.style.display = DisplayStyle.None;

            if (_currentCircuit?.Components == null) return panel;

            foreach (var comp in _currentCircuit.Components)
            {
                if (comp == null) continue;
                var info = ResolveCatalogItem(comp);
                var pins = info.Pins ?? new List<string>();
                if (pins.Count == 0) continue;

                var foldout = new Foldout
                {
                    text = $"{comp.Id} ({comp.Type})",
                    value = false
                };
                foldout.AddToClassList("net-editor-foldout");

                foreach (var pin in pins)
                {
                    string node = $"{comp.Id}.{pin}";
                    var toggle = new Toggle(pin);
                    toggle.AddToClassList("net-editor-toggle");
                    toggle.value = row.Nodes.Contains(node);
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.newValue)
                        {
                            row.Nodes.Add(node);
                        }
                        else
                        {
                            row.Nodes.Remove(node);
                        }
                        row.CountLabel.text = $"{row.Nodes.Count}";
                    });
                    foldout.Add(toggle);
                }

                panel.Add(foldout);
            }

            return panel;
        }

        private void ApplyNetEdits()
        {
            if (_currentCircuit == null || _currentCircuit.Nets == null)
            {
                HideNetEditor();
                return;
            }

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var validNodes = BuildValidNodes();
            foreach (var row in _netEditorRows)
            {
                if (row.DeleteRequested)
                {
                    if (row.Nodes.Count > 0)
                    {
                        SetNetEditorError($"Net '{row.Net.Id}' has nodes. Remove nodes before deleting.");
                        return;
                    }
                    continue;
                }

                string name = row.NameField?.value?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    SetNetEditorError("Net name cannot be empty.");
                    return;
                }
                if (!IsValidNetId(name))
                {
                    SetNetEditorError($"Invalid net name '{name}'. Use letters, numbers, '_' or '-'.");
                    return;
                }
                if (!usedNames.Add(name))
                {
                    SetNetEditorError($"Duplicate net name '{name}'.");
                    return;
                }

                foreach (var node in row.Nodes)
                {
                    if (!validNodes.Contains(node))
                    {
                        SetNetEditorError($"Unknown node '{node}'.");
                        return;
                    }
                    if (!usedNodes.Add(node))
                    {
                        SetNetEditorError($"Node '{node}' assigned to multiple nets.");
                        return;
                    }
                }
            }

            var nextNets = new List<NetSpec>();
            foreach (var row in _netEditorRows)
            {
                if (row.DeleteRequested) continue;
                string name = row.NameField?.value?.Trim() ?? string.Empty;
                if (row.Net != null)
                {
                    row.Net.Id = name;
                    row.Net.Nodes = row.Nodes.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                    nextNets.Add(row.Net);
                }
                else
                {
                    nextNets.Add(new NetSpec
                    {
                        Id = name,
                        Nodes = row.Nodes.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                    });
                }
            }

            _currentCircuit.Nets = nextNets;
            HideNetEditor();
            PopulateProjectTree();
            UpdateWireLayer();
            RequestBreadboardRebuild();
        }

        private HashSet<string> BuildValidNodes()
        {
            var nodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_currentCircuit?.Components == null) return nodes;
            foreach (var comp in _currentCircuit.Components)
            {
                if (comp == null) continue;
                var info = ResolveCatalogItem(comp);
                var pins = info.Pins ?? new List<string>();
                foreach (var pin in pins)
                {
                    nodes.Add($"{comp.Id}.{pin}");
                }
            }
            return nodes;
        }

        private void SetNetEditorError(string message)
        {
            if (_netEditorErrorLabel == null) return;
            _netEditorErrorLabel.text = message ?? string.Empty;
        }

        private static bool IsValidNetId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return Regex.IsMatch(name, "^[A-Za-z0-9_-]+$");
        }

        private string GetUniqueNetName(string baseName)
        {
            if (_currentCircuit?.Nets == null) return baseName;
            int index = 1;
            string candidate = baseName;
            while (_currentCircuit.Nets.Any(n => string.Equals(n.Id, candidate, StringComparison.OrdinalIgnoreCase)) ||
                   _netEditorRows.Any(r => string.Equals(r.NameField?.value, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{baseName}_{index++}";
            }
            return candidate;
        }

        private static List<string> BuildNetChoices(List<NetSpec> nets)
        {
            var choices = new List<string> { "UNCONNECTED" };
            if (nets != null)
            {
                foreach (var net in nets.Where(n => n != null && !string.IsNullOrWhiteSpace(n.Id))
                                        .OrderBy(n => n.Id, StringComparer.OrdinalIgnoreCase))
                {
                    choices.Add(net.Id);
                }
            }
            choices.Add("New Net...");
            return choices;
        }

        private void EnsureNetExists(string netId)
        {
            if (_currentCircuit == null || string.IsNullOrWhiteSpace(netId)) return;
            if (_currentCircuit.Nets == null) _currentCircuit.Nets = new List<NetSpec>();
            if (_currentCircuit.Nets.Any(n => string.Equals(n.Id, netId, StringComparison.OrdinalIgnoreCase))) return;
            _currentCircuit.Nets.Add(new NetSpec { Id = netId, Nodes = new List<string>() });
        }

        private void AssignNodeToNet(string node, string netId)
        {
            if (_currentCircuit == null || string.IsNullOrWhiteSpace(node) || string.IsNullOrWhiteSpace(netId)) return;
            if (_currentCircuit.Nets == null) _currentCircuit.Nets = new List<NetSpec>();
            RemoveNodeFromNet(node);
            var net = _currentCircuit.Nets.FirstOrDefault(n => string.Equals(n.Id, netId, StringComparison.OrdinalIgnoreCase));
            if (net == null)
            {
                net = new NetSpec { Id = netId, Nodes = new List<string>() };
                _currentCircuit.Nets.Add(net);
            }
            if (net.Nodes == null) net.Nodes = new List<string>();
            if (!net.Nodes.Contains(node))
            {
                net.Nodes.Add(node);
            }
        }

        private void RemoveNodeFromNet(string node)
        {
            if (_currentCircuit?.Nets == null || string.IsNullOrWhiteSpace(node)) return;
            foreach (var net in _currentCircuit.Nets)
            {
                if (net?.Nodes == null) continue;
                net.Nodes.Remove(node);
            }
        }

        private void ShowEditMenu()
        {
            if (_root == null || _menuEdit == null) return;
            var menu = new GenericDropdownMenu();
            menu.AddItem("Select Tool", false, () => SetTool(ToolMode.Select));
            menu.AddItem("Move Tool", false, () => SetTool(ToolMode.Move));
            menu.AddItem("Wire Tool", false, () => SetTool(ToolMode.Wire));
            menu.AddItem("Text Tool", false, () => SetTool(ToolMode.Text));
            menu.AddSeparator(string.Empty);
            menu.AddItem("Delete Selected", false, () => DeleteSelectedComponent());
            menu.DropDown(_menuEdit.worldBound, _root, DropdownMenuSizeMode.Auto);
        }

        private void ShowViewMenu()
        {
            if (_root == null || _menuView == null) return;
            var menu = new GenericDropdownMenu();
            menu.AddItem("Toggle Left Panel", false, ToggleLeftPanel);
            menu.AddItem("Toggle Right Panel", false, ToggleRightPanel);
            menu.AddItem("Toggle Bottom Panel", false, ToggleBottomPanelVisibility);
            menu.AddSeparator(string.Empty);
            menu.AddItem("Reset Panel Layout", false, ResetPanelLayout);
            menu.DropDown(_menuView.worldBound, _root, DropdownMenuSizeMode.Auto);
        }

        private void ShowDesignMenu()
        {
            if (_root == null || _menuDesign == null) return;
            var menu = new GenericDropdownMenu();
            menu.AddItem("Run DRC", false, RunDrcCheck);
            menu.DropDown(_menuDesign.worldBound, _root, DropdownMenuSizeMode.Auto);
        }

        private void ShowToolsMenu()
        {
            if (_root == null || _menuTools == null) return;
            var menu = new GenericDropdownMenu();
            menu.AddItem("Simulate", false, StartRunMode);
            menu.DropDown(_menuTools.worldBound, _root, DropdownMenuSizeMode.Auto);
        }

        private void ShowRouteMenu()
        {
            if (_root == null || _menuRoute == null) return;
            var menu = new GenericDropdownMenu();
            menu.AddItem("Wire Tool", false, () => SetTool(ToolMode.Wire));
            menu.DropDown(_menuRoute.worldBound, _root, DropdownMenuSizeMode.Auto);
        }

        private void CreateNewProject()
        {
            _currentCircuit = new CircuitSpec
            {
                Id = GetDefaultCircuitId(),
                Components = new List<ComponentSpec>(),
                Nets = new List<NetSpec>()
            };
            _selectedComponent = null;
            _selectedVisual = null;
            _selectedCatalogItem = default;
            NormalizeCircuit();
            RebuildPinUsage();
            RefreshCanvas();
            ResetCanvasView();
            PopulateProjectTree();
            UpdateErrorCount();
            RequestBreadboardRebuild();
            SessionManager.Instance?.StartSession(_currentCircuit);
            SetCenterPanelMode(CenterPanelMode.Circuit);
        }

        private void CloseProject()
        {
            SaveCurrentProject();
            ReturnToProjectWizard();
        }

        private void ReturnToProjectWizard()
        {
            int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings;
            if (sceneCount > 0)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(0);
            }
            else
            {
                Debug.LogWarning("[CircuitStudio] Scene 0 not found.");
            }
        }

        private void QuitApp()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OpenProjectDialog()
        {
            string root = Path.Combine(Application.persistentDataPath, "Projects");
            string path = string.Empty;
#if UNITY_EDITOR
            path = UnityEditor.EditorUtility.OpenFilePanel("Open Project", root, "rtwin,json");
#else
            if (Directory.Exists(root))
            {
                path = Directory.EnumerateFiles(root, "*.rtwin", SearchOption.TopDirectoryOnly).FirstOrDefault();
            }
#endif
            if (string.IsNullOrWhiteSpace(path)) return;
            LoadProjectFromPath(path);
        }

        private void LoadProjectFromPath(string path)
        {
            var project = SimulationSerializer.LoadProject(path);
            if (project == null)
            {
                Debug.LogWarning("[CircuitStudio] Failed to load project.");
                return;
            }

            _currentCircuit = project.Circuit;
            _selectedComponent = null;
            _selectedVisual = null;
            _selectedCatalogItem = default;
            NormalizeCircuit();
            RebuildPinUsage();
            RefreshCanvas();
            ResetCanvasView();
            PopulateProjectTree();
            UpdateErrorCount();
            RequestBreadboardRebuild();
            SessionManager.Instance?.StartSession(project, path);
            SetCenterPanelMode(CenterPanelMode.Circuit);
        }

        private void SaveProjectAs()
        {
            string root = Path.Combine(Application.persistentDataPath, "Projects");
            string path = string.Empty;
#if UNITY_EDITOR
            string defaultName = string.IsNullOrWhiteSpace(_currentCircuit?.Id) ? "NewProject" : _currentCircuit.Id;
            path = UnityEditor.EditorUtility.SaveFilePanel("Save Project As", root, defaultName, "rtwin");
#else
            Directory.CreateDirectory(root);
            string defaultName = string.IsNullOrWhiteSpace(_currentCircuit?.Id) ? "NewProject" : _currentCircuit.Id;
            path = Path.Combine(root, $"{defaultName}.rtwin");
#endif
            if (string.IsNullOrWhiteSpace(path)) return;
            SaveProjectToPath(path);
        }

        private void SaveProjectToPath(string path)
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
            SyncCodeWorkspace(path);
            SimulationSerializer.SaveProject(project, path);
            session?.StartSession(project, path);
            Debug.Log($"[CircuitStudio] Project saved: {path}");
        }

        private void ToggleLeftPanel()
        {
            if (_leftPanel == null) return;
            bool hidden = _leftPanel.resolvedStyle.display == DisplayStyle.None;
            _leftPanel.style.display = hidden ? DisplayStyle.Flex : DisplayStyle.None;
            if (_leftResizer != null) _leftResizer.style.display = hidden ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ToggleRightPanel()
        {
            if (_rightPanel == null) return;
            bool hidden = _rightPanel.resolvedStyle.display == DisplayStyle.None;
            _rightPanel.style.display = hidden ? DisplayStyle.Flex : DisplayStyle.None;
            if (_rightResizer != null) _rightResizer.style.display = hidden ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ToggleBottomPanelVisibility()
        {
            if (_bottomPanel == null) return;
            bool hidden = _bottomPanel.resolvedStyle.display == DisplayStyle.None;
            _bottomPanel.style.display = hidden ? DisplayStyle.Flex : DisplayStyle.None;
            if (_bottomResizer != null)
            {
                _bottomResizer.style.display = hidden && !_bottomCollapsed ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_outputPanel != null)
            {
                _outputPanel.style.display = hidden && !_bottomCollapsed ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void ResetPanelLayout()
        {
            if (_leftPanel != null)
            {
                _leftPanel.style.display = DisplayStyle.Flex;
                _leftPanel.style.width = 300f;
            }
            if (_rightPanel != null)
            {
                _rightPanel.style.display = DisplayStyle.Flex;
                _rightPanel.style.width = 320f;
            }
            if (_bottomPanel != null)
            {
                _bottomPanel.style.display = DisplayStyle.Flex;
                _bottomPanel.style.height = 220f;
                _bottomPanel.RemoveFromClassList("collapsed");
                _bottomCollapsed = false;
            }
            if (_outputPanel != null) _outputPanel.style.display = DisplayStyle.Flex;
            if (_bottomToggleBtn != null) _bottomToggleBtn.text = "HIDE";
            if (_leftResizer != null) _leftResizer.style.display = DisplayStyle.Flex;
            if (_rightResizer != null) _rightResizer.style.display = DisplayStyle.Flex;
            if (_bottomResizer != null) _bottomResizer.style.display = DisplayStyle.Flex;
        }

        private void InitializeBreadboardPreview()
        {
            if (_circuit3DView == null) return;
            if (_breadboardView == null)
            {
                var go = new GameObject("CircuitStudio3DView");
                go.transform.SetParent(transform, false);
                _breadboardView = go.AddComponent<Breadboard3DView>();
            }

            _circuit3DView.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (_breadboardView == null) return;
                int width = Mathf.RoundToInt(evt.newRect.width);
                int height = Mathf.RoundToInt(evt.newRect.height);
                if (width <= 0 || height <= 0) return;

                _breadboardView.Initialize(width, height);
                if (_breadboardView.TargetTexture != null)
                {
                    _circuit3DView.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_breadboardView.TargetTexture));
                    var label = _circuit3DView.Q<Label>(className: "circuit-3d-label");
                    if (label != null) label.style.display = DisplayStyle.None;
                }
                RequestBreadboardRebuild();
            });
        }

        private void Initialize3DInput()
        {
            if (_circuit3DView == null) return;
            _circuit3DView.RegisterCallback<PointerDownEvent>(On3DPointerDown);
            _circuit3DView.RegisterCallback<PointerMoveEvent>(On3DPointerMove);
            _circuit3DView.RegisterCallback<PointerUpEvent>(On3DPointerUp);
            _circuit3DView.RegisterCallback<WheelEvent>(On3DWheel);
        }

        private void RequestBreadboardRebuild()
        {
            _breadboardDirty = true;
        }

        private void LateUpdate()
        {
            if (!_breadboardDirty) return;
            if (_centerMode != CenterPanelMode.Preview3D) return;
            RebuildBreadboardPreview();
        }

        private void RebuildBreadboardPreview()
        {
            if (_breadboardView == null || _currentCircuit == null || _circuit3DView == null) return;
            if (_breadboardView.TargetTexture == null)
            {
                var rect = _circuit3DView.contentRect;
                int width = Mathf.RoundToInt(rect.width);
                int height = Mathf.RoundToInt(rect.height);
                if (width > 0 && height > 0)
                {
                    _breadboardView.Initialize(width, height);
                }
            }

            if (_breadboardView.TargetTexture == null) return;

            _breadboardView.Build(_currentCircuit);
            _circuit3DView.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_breadboardView.TargetTexture));
            var label = _circuit3DView.Q<Label>(className: "circuit-3d-label");
            if (label != null) label.style.display = DisplayStyle.None;
            _breadboardDirty = false;
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
                    Id = GetDefaultCircuitId(),
                    Components = new List<ComponentSpec>(),
                    Nets = new List<NetSpec>()
                };
            }
            NormalizeCircuit();
            RebuildPinUsage();
            RefreshCanvas();
            ResetCanvasView();
            PopulateProjectTree();
            UpdateErrorCount();
        }

        private string GetDefaultCircuitId()
        {
            var name = SessionManager.Instance?.CurrentProject?.ProjectName;
            if (!string.IsNullOrWhiteSpace(name)) return name;
            return "Untitled Project";
        }

        private void SetCenterPanelMode(CenterPanelMode mode)
        {
            CancelLibraryDrag();
            CancelWireMode();
            if (_isPanningCanvas && _canvasView != null && _panPointerId != -1 && _canvasView.HasPointerCapture(_panPointerId))
            {
                _canvasView.ReleasePointer(_panPointerId);
                _isPanningCanvas = false;
                _panPointerId = -1;
            }
            _centerMode = mode;
            if (_canvasView != null)
            {
                _canvasView.style.display = mode == CenterPanelMode.Circuit ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_codePanel != null)
            {
                _codePanel.style.display = mode == CenterPanelMode.Code ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_circuit3DPanel != null)
            {
                _circuit3DPanel.style.display = mode == CenterPanelMode.Preview3D ? DisplayStyle.Flex : DisplayStyle.None;
            }

            UpdateCenterTabs();

            if (mode == CenterPanelMode.Code)
            {
                EnsureCodeWorkspace();
                RefreshCodeTargets();
                RefreshCodeFileList();
                _codeEditor?.Focus();
            }
            else if (mode == CenterPanelMode.Preview3D)
            {
                RequestBreadboardRebuild();
            }
            else
            {
                UpdateBoardLayout();
                BringCanvasHudToFront();
            }
        }

        private void UpdateCenterTabs()
        {
            _centerTabCircuit?.RemoveFromClassList("active");
            _centerTabCode?.RemoveFromClassList("active");
            _centerTab3D?.RemoveFromClassList("active");
            if (_centerMode == CenterPanelMode.Circuit) _centerTabCircuit?.AddToClassList("active");
            if (_centerMode == CenterPanelMode.Code) _centerTabCode?.AddToClassList("active");
            if (_centerMode == CenterPanelMode.Preview3D) _centerTab3D?.AddToClassList("active");
        }

        private void SetOutputFilter(OutputFilterMode mode)
        {
            if (_outputFilterMode == mode) return;
            _outputFilterMode = mode;
            UpdateOutputFilterButtons();
            RefreshOutputConsole();
        }

        private void UpdateOutputFilterButtons()
        {
            _outputAllBtn?.RemoveFromClassList("active");
            _outputWarningsBtn?.RemoveFromClassList("active");
            _outputErrorsBtn?.RemoveFromClassList("active");

            switch (_outputFilterMode)
            {
                case OutputFilterMode.Warnings:
                    _outputWarningsBtn?.AddToClassList("active");
                    break;
                case OutputFilterMode.Errors:
                    _outputErrorsBtn?.AddToClassList("active");
                    break;
                default:
                    _outputAllBtn?.AddToClassList("active");
                    break;
            }
        }

        private void ToggleBottomPanel()
        {
            if (_bottomPanel == null) return;
            if (_bottomCollapsed)
            {
                _bottomCollapsed = false;
                _bottomPanel.RemoveFromClassList("collapsed");
                float height = _bottomExpandedHeight > 0 ? _bottomExpandedHeight : MinBottomPanelHeight;
                _bottomPanel.style.height = height;
                if (_bottomResizer != null) _bottomResizer.style.display = DisplayStyle.Flex;
                if (_outputPanel != null) _outputPanel.style.display = DisplayStyle.Flex;
                if (_bottomToggleBtn != null) _bottomToggleBtn.text = "HIDE";
            }
            else
            {
                _bottomCollapsed = true;
                _bottomExpandedHeight = Mathf.Max(_bottomPanel.resolvedStyle.height, MinBottomPanelHeight);
                _bottomPanel.AddToClassList("collapsed");
                if (_outputPanel != null) _outputPanel.style.display = DisplayStyle.None;
                _bottomPanel.style.height = 36f;
                if (_bottomResizer != null) _bottomResizer.style.display = DisplayStyle.None;
                if (_bottomToggleBtn != null) _bottomToggleBtn.text = "SHOW";
            }
        }

        private void EnsureCodeWorkspace()
        {
            string root = GetCodeRoot(_codeTargetComponentId);
            if (string.IsNullOrWhiteSpace(root)) return;
            Directory.CreateDirectory(root);

            string primaryName = GetPrimarySketchFileName(_codeTargetComponentId);
            string primaryPath = Path.Combine(root, primaryName);
            string legacyPath = Path.Combine(root, "sketch.ino");

            var inoFiles = Directory.EnumerateFiles(root, "*.ino", SearchOption.TopDirectoryOnly).ToList();
            if (!File.Exists(primaryPath))
            {
                if (File.Exists(legacyPath))
                {
                    File.Move(legacyPath, primaryPath);
                    if (string.Equals(_activeCodePath, legacyPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _activeCodePath = primaryPath;
                    }
                }
                else if (inoFiles.Count == 1 && !string.Equals(inoFiles[0], primaryPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Move(inoFiles[0], primaryPath);
                    if (string.Equals(_activeCodePath, inoFiles[0], StringComparison.OrdinalIgnoreCase))
                    {
                        _activeCodePath = primaryPath;
                    }
                }
            }

            bool hasFiles = Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly)
                .Any(path => IsCodeFile(path));
            if (!hasFiles || !File.Exists(primaryPath))
            {
                File.WriteAllText(primaryPath, GetDefaultSketch(_codeTargetComponentId));
            }

            string headerPath = Path.Combine(root, "app.h");
            if (!File.Exists(headerPath))
            {
                File.WriteAllText(headerPath, GetDefaultHeader());
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
                RegisterRightClickMenu(btn, menu => BuildCodeFileItemMenu(menu, file));
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
            ResetCodeHistory(content);
            UpdateCodeFileLabel();
            RefreshCodeFileList();
            ApplyCodeEditorTheme();
        }

        private void SaveCodeFile(bool saveAs)
        {
            string root = GetCodeRoot(_codeTargetComponentId);
            if (string.IsNullOrWhiteSpace(root)) return;

            string target = _activeCodePath;
            if (saveAs || string.IsNullOrWhiteSpace(target))
            {
#if UNITY_EDITOR
                string defaultName = Path.GetFileNameWithoutExtension(GetPrimarySketchFileName(_codeTargetComponentId));
                target = UnityEditor.EditorUtility.SaveFilePanel("Save Sketch", root, defaultName, "ino");
#else
                string primaryName = GetPrimarySketchFileName(_codeTargetComponentId);
                target = Path.Combine(root, primaryName);
                if (File.Exists(target))
                {
                    string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                    target = Path.Combine(root, $"sketch_{stamp}.ino");
                }
#endif
            }

            if (string.IsNullOrWhiteSpace(target)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string content = _codeEditor?.value ?? string.Empty;
            File.WriteAllText(target, content);
            _activeCodePath = target;
            UpdateCodeFileLabel();
            RefreshCodeFileList();
            ApplyCodeEditorTheme();
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
                string projectDir = ResolveProjectWorkspaceRoot(SessionManager.Instance.CurrentProjectPath);
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
                    string workspace = Path.Combine(projectDir, baseName);
                    return workspace;
                }
            }

            return projectDir;
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
            return $"// Target: {id}\\n#include \"app.h\"\\n\\nvoid setup()\\n{{\\n  // Example: pinMode(LED_PIN, OUTPUT);\\n}}\\n\\nvoid loop()\\n{{\\n}}\\n";
        }

        private static string GetPrimarySketchFileName(string componentId)
        {
            string baseName = string.IsNullOrWhiteSpace(componentId) ? "sketch" : componentId.Trim();
            foreach (char ch in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(ch, '_');
            }
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "sketch";
            }
            return $"{baseName}.ino";
        }

        private static string GetDefaultHeader()
        {
            return "// Shared helpers\\n#pragma once\\n\\n// Example pin mapping\\nstatic const int LED_PIN = 13;\\n";
        }

        private void RunCodeBuild()
        {
            StartBuild(false);
        }

        private void RunCodeBuildAndRun()
        {
            StartBuild(true);
        }

        private void StartBuild(bool runAfter)
        {
            if (_buildBusy)
            {
                Debug.LogWarning("[CodeStudio] Build already running.");
                return;
            }
            StartCoroutine(RunCodeBuildRoutine(runAfter, true, true));
        }

        private void RunBuildAll()
        {
            if (_buildBusy)
            {
                Debug.LogWarning("[CodeStudio] Build already running.");
                return;
            }
            StartCoroutine(RunBuildAllRoutine());
        }

        private IEnumerator RunBuildAllRoutine()
        {
            if (_currentCircuit == null || _currentCircuit.Components == null) yield break;
            var targets = _currentCircuit.Components.Where(c => IsArduinoType(c.Type)).ToList();
            if (targets.Count == 0)
            {
                Debug.LogWarning("[CodeStudio] No Arduino targets to build.");
                yield break;
            }

            _buildBusy = true;
            SetBuildButtonsEnabled(false);
            BeginBuildProgress($"Build All 0/{targets.Count}", false);

            string previousTarget = _codeTargetComponentId;
            int success = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                HandleCodeTargetChanged(target.Id);
                _buildProgressValue = targets.Count > 0 ? (i / (float)targets.Count) * 100f : 0f;
                UpdateBuildProgress(0f);
                if (_codeBuildProgress != null)
                {
                    _codeBuildProgress.title = $"Build {i + 1}/{targets.Count}: {target.Id}";
                }

                yield return RunCodeBuildRoutine(false, false, false);
                if (_lastBuildSucceeded) success++;
            }

            HandleCodeTargetChanged(previousTarget);
            _buildProgressValue = 100f;
            UpdateBuildProgress(0f);

            bool allOk = success == targets.Count;
            EndBuildProgress(allOk, $"Build All {success}/{targets.Count}");
            SetBuildButtonsEnabled(true);
            _buildBusy = false;
            Debug.Log($"[CodeStudio] Build All complete: {success}/{targets.Count} targets.");
        }

        private IEnumerator RunCodeBuildRoutine(bool runAfter, bool showProgress, bool manageSession)
        {
            _lastBuildSucceeded = false;
            EnsureCodeWorkspace();
            if (string.IsNullOrWhiteSpace(_activeCodePath))
            {
                SaveCodeFile(true);
            }

            string inoPath = ResolveSketchPath();
            if (string.IsNullOrWhiteSpace(inoPath))
            {
                Debug.LogError("[CodeStudio] No .ino file found to build.");
                if (manageSession) EndBuildProgress(false, "Build failed");
                yield break;
            }

            var targetBoard = ResolveTargetBoard();
            if (targetBoard == null)
            {
                Debug.LogError("[CodeStudio] No Arduino target found.");
                if (manageSession) EndBuildProgress(false, "Build failed");
                yield break;
            }

            string repoRoot = ResolveRepoRoot();
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                Debug.LogError("[CodeStudio] Repo root not found. Build cancelled.");
                if (manageSession) EndBuildProgress(false, "Build failed");
                yield break;
            }

            string toolPath = Path.Combine(repoRoot, "tools", "scripts", "build_bvm.py");
            if (!File.Exists(toolPath))
            {
                Debug.LogWarning($"[CodeStudio] build_bvm.py not found at {toolPath}");
                if (manageSession) EndBuildProgress(false, "Build failed");
                yield break;
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

            if (manageSession)
            {
                _buildBusy = true;
                SetBuildButtonsEnabled(false);
            }
            if (showProgress)
            {
                BeginBuildProgress($"Building {Path.GetFileName(inoPath)}", true);
            }

            var proc = new System.Diagnostics.Process { StartInfo = startInfo };
            proc.OutputDataReceived += (_, evt) =>
            {
                HandleBuildOutputLine(evt.Data, false);
            };
            proc.ErrorDataReceived += (_, evt) =>
            {
                HandleBuildOutputLine(evt.Data, true);
            };

            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CodeStudio] Build failed to start: {ex.Message}");
                if (showProgress) EndBuildProgress(false, "Build failed");
                if (manageSession)
                {
                    SetBuildButtonsEnabled(true);
                    _buildBusy = false;
                }
                proc.Dispose();
                yield break;
            }

            while (!proc.HasExited)
            {
                if (showProgress)
                {
                    UpdateBuildProgress(Time.unscaledDeltaTime);
                }
                yield return null;
            }

            int exitCode = proc.ExitCode;
            proc.Dispose();

            if (exitCode != 0)
            {
                Debug.LogError($"[CodeStudio] Build failed (exit {exitCode}).");
                if (showProgress) EndBuildProgress(false, "Build failed");
                if (manageSession)
                {
                    SetBuildButtonsEnabled(true);
                    _buildBusy = false;
                }
                yield break;
            }

            string hexPath = FindLatestHex(Path.Combine(outDir, "bvm_build"));
            if (string.IsNullOrWhiteSpace(hexPath))
            {
                Debug.LogError("[CodeStudio] Build completed but no .hex was found.");
                if (showProgress) EndBuildProgress(false, "Build failed");
                if (manageSession)
                {
                    SetBuildButtonsEnabled(true);
                    _buildBusy = false;
                }
                yield break;
            }

            ApplyFirmwarePaths(targetBoard, hexPath, outPath, fqbn, inoPath);
            SaveCurrentProject();
            Debug.Log($"[CodeStudio] Build ok. Hex: {hexPath}");
            _lastBuildSucceeded = true;

            if (showProgress) EndBuildProgress(true, "Build ok");
            if (manageSession)
            {
                SetBuildButtonsEnabled(true);
                _buildBusy = false;
            }
            if (runAfter)
            {
                StartRunMode();
            }
        }

        private void HandleBuildOutputLine(string line, bool isErrorStream)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            if (TryParseCompilerMessage(line, out var logType, out var message))
            {
                if (logType == LogType.Error) Debug.LogError(message);
                else if (logType == LogType.Warning) Debug.LogWarning(message);
                else Debug.Log(message);
                return;
            }

            string lower = line.ToLowerInvariant();
            if (lower.Contains("error:"))
            {
                Debug.LogError($"[CodeStudio] {line}");
                return;
            }
            if (lower.Contains("warning:"))
            {
                Debug.LogWarning($"[CodeStudio] {line}");
                return;
            }

            if (isErrorStream)
            {
                Debug.LogWarning($"[CodeStudio] {line}");
            }
            else
            {
                Debug.Log($"[CodeStudio] {line}");
            }
        }

        private static bool TryParseCompilerMessage(string line, out LogType logType, out string message)
        {
            logType = LogType.Log;
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(line)) return false;

            var match = CompilerMessageWithColumn.Match(line);
            if (!match.Success)
            {
                match = CompilerMessageNoColumn.Match(line);
            }
            if (!match.Success) return false;

            string file = match.Groups["file"].Value.Trim();
            string lineText = match.Groups["line"].Value.Trim();
            string colText = match.Groups["col"].Success ? match.Groups["col"].Value.Trim() : string.Empty;
            string type = match.Groups["type"].Value.Trim().ToLowerInvariant();
            string desc = match.Groups["message"].Value.Trim();

            string fileName = Path.GetFileName(file);
            string location = string.IsNullOrWhiteSpace(colText)
                ? $"{fileName}:{lineText}"
                : $"{fileName}:{lineText}:{colText}";

            if (type == "error") logType = LogType.Error;
            else if (type == "warning") logType = LogType.Warning;
            else logType = LogType.Log;

            message = $"[CodeStudio] Build {type}: {location} {desc}";
            return true;
        }

        private void BeginBuildProgress(string title, bool indeterminate)
        {
            _buildProgressToken++;
            _buildProgressIndeterminate = indeterminate;
            _buildProgressValue = 0f;
            if (_codeBuildProgressWrap != null)
            {
                _codeBuildProgressWrap.AddToClassList("active");
            }
            if (_codeBuildProgress != null)
            {
                _codeBuildProgress.title = string.IsNullOrWhiteSpace(title) ? "Building..." : title;
                _codeBuildProgress.value = 0f;
            }
        }

        private void UpdateBuildProgress(float deltaTime)
        {
            if (_codeBuildProgress == null) return;
            if (_buildProgressIndeterminate)
            {
                _buildProgressValue = Mathf.Repeat(_buildProgressValue + deltaTime * 80f, 100f);
                _codeBuildProgress.value = 10f + Mathf.PingPong(_buildProgressValue, 80f);
            }
            else
            {
                _codeBuildProgress.value = Mathf.Clamp(_buildProgressValue, 0f, 100f);
            }
        }

        private void EndBuildProgress(bool success, string title)
        {
            int token = _buildProgressToken;
            if (_codeBuildProgress != null)
            {
                _codeBuildProgress.title = string.IsNullOrWhiteSpace(title)
                    ? (success ? "Build ok" : "Build failed")
                    : title;
                if (success)
                {
                    _codeBuildProgress.value = 100f;
                }
            }
            StartCoroutine(HideBuildProgressAfterDelay(token, 1.25f));
            _buildProgressIndeterminate = true;
        }

        private void SetBuildButtonsEnabled(bool enabled)
        {
            _codeBuildBtn?.SetEnabled(enabled);
            _codeBuildRunBtn?.SetEnabled(enabled);
            _codeBuildAllBtn?.SetEnabled(enabled);
        }

        private IEnumerator HideBuildProgressAfterDelay(int token, float delaySeconds)
        {
            if (_codeBuildProgressWrap == null) yield break;
            if (delaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(delaySeconds);
            }
            if (token != _buildProgressToken || _buildBusy) yield break;
            _codeBuildProgressWrap.RemoveFromClassList("active");
        }

        private string ResolveSketchPath()
        {
            string root = GetCodeRoot(_codeTargetComponentId);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;

            string primaryPath = Path.Combine(root, GetPrimarySketchFileName(_codeTargetComponentId));
            if (File.Exists(primaryPath))
            {
                return primaryPath;
            }

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

        private void ApplyFirmwarePaths(ComponentSpec board, string hexPath, string bvmPath, string fqbn, string inoPath)
        {
            if (board == null) return;

            EnsureComponentProperties(board);
            board.Properties["firmwarePath"] = hexPath;
            board.Properties["firmware"] = hexPath;
            board.Properties["bvmPath"] = bvmPath;
            board.Properties["fqbn"] = fqbn;
            UpdateVirtualFirmwareHint(board, inoPath);
        }

        private static void UpdateVirtualFirmwareHint(ComponentSpec board, string inoPath)
        {
            if (board == null || board.Properties == null) return;
            if (TryInferVirtualFirmware(inoPath, out var virtualFirmware))
            {
                board.Properties["virtualFirmware"] = virtualFirmware;
            }
            else
            {
                board.Properties.Remove("virtualFirmware");
            }
            if (TryInferVirtualSerial(inoPath, out var serialText, out var intervalMs))
            {
                board.Properties["virtualSerial"] = serialText;
                board.Properties["virtualSerialIntervalMs"] = intervalMs.ToString();
            }
            else
            {
                board.Properties.Remove("virtualSerial");
                board.Properties.Remove("virtualSerialIntervalMs");
            }
        }

        private static bool TryInferVirtualFirmware(string inoPath, out string firmware)
        {
            firmware = null;
            if (string.IsNullOrWhiteSpace(inoPath) || !File.Exists(inoPath)) return false;
            string text;
            try
            {
                text = File.ReadAllText(inoPath);
            }
            catch
            {
                return false;
            }

            string pin = null;
            if (Regex.IsMatch(text, @"\bsetLed\s*\(\s*true\s*\)", RegexOptions.IgnoreCase) &&
                Regex.IsMatch(text, @"\bsetLed\s*\(\s*false\s*\)", RegexOptions.IgnoreCase))
            {
                pin = "D13";
            }
            else if (!TryFindDigitalWriteBlinkPin(text, out pin))
            {
                return false;
            }

            int delayMs = 500;
            var delayMatch = Regex.Match(text, @"\bdelay\s*\(\s*(\d+)\s*\)", RegexOptions.IgnoreCase);
            if (delayMatch.Success && int.TryParse(delayMatch.Groups[1].Value, out var parsed))
            {
                delayMs = Mathf.Clamp(parsed, 1, 10000);
            }

            firmware = $"blink:{pin}:{delayMs}";
            return true;
        }

        private static bool TryInferVirtualSerial(string inoPath, out string serialText, out int intervalMs)
        {
            serialText = null;
            intervalMs = 1000;
            if (string.IsNullOrWhiteSpace(inoPath) || !File.Exists(inoPath)) return false;
            string text;
            try
            {
                text = File.ReadAllText(inoPath);
            }
            catch
            {
                return false;
            }

            var matches = Regex.Matches(text, @"\bSerial\.print(ln)?\s*\(\s*""(.*?)""", RegexOptions.IgnoreCase);
            if (matches.Count == 0) return false;

            var sb = new StringBuilder();
            foreach (Match match in matches)
            {
                string payload = match.Groups[2].Value;
                sb.Append(payload);
                if (match.Groups[1].Success) sb.Append('\n');
            }
            serialText = sb.ToString();
            if (string.IsNullOrEmpty(serialText)) return false;

            var delayMatch = Regex.Match(text, @"\bdelay\s*\(\s*(\d+)\s*\)", RegexOptions.IgnoreCase);
            if (delayMatch.Success && int.TryParse(delayMatch.Groups[1].Value, out var parsed))
            {
                intervalMs = Mathf.Clamp(parsed, 100, 10000);
            }
            return true;
        }

        private static bool TryFindDigitalWriteBlinkPin(string text, out string pin)
        {
            pin = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var matches = Regex.Matches(text, @"\bdigitalWrite\s*\(\s*([A-Za-z_][A-Za-z0-9_]*|\d+)\s*,\s*(HIGH|LOW)\s*\)", RegexOptions.IgnoreCase);
            if (matches.Count == 0) return false;

            var states = new Dictionary<string, (bool high, bool low)>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in matches)
            {
                string rawPin = match.Groups[1].Value;
                string state = match.Groups[2].Value;
                states.TryGetValue(rawPin, out var flags);
                if (state.Equals("HIGH", StringComparison.OrdinalIgnoreCase)) flags.high = true;
                if (state.Equals("LOW", StringComparison.OrdinalIgnoreCase)) flags.low = true;
                states[rawPin] = flags;
            }

            foreach (var entry in states)
            {
                if (!entry.Value.high || !entry.Value.low) continue;
                if (TryNormalizeArduinoPin(entry.Key, text, out pin)) return true;
            }

            return false;
        }

        private static bool TryNormalizeArduinoPin(string rawPin, string text, out string pin)
        {
            pin = null;
            if (string.IsNullOrWhiteSpace(rawPin)) return false;
            string token = rawPin.Trim();

            if (string.Equals(token, "LED_BUILTIN", StringComparison.OrdinalIgnoreCase))
            {
                pin = "D13";
                return true;
            }

            if (string.Equals(token, "LED_PIN", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParsePinDefine(text, token, out pin)) return true;
                pin = "D13";
                return true;
            }

            if (token.StartsWith("D", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(token.Substring(1), out var dNum))
            {
                pin = $"D{dNum}";
                return true;
            }

            if (token.StartsWith("A", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(token.Substring(1), out var aNum))
            {
                pin = $"A{aNum}";
                return true;
            }

            if (int.TryParse(token, out var numericPin))
            {
                return TryMapNumericPin(numericPin, out pin);
            }

            return TryParsePinDefine(text, token, out pin);
        }

        private static bool TryParsePinDefine(string text, string name, out string pin)
        {
            pin = null;
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(name)) return false;

            string escaped = Regex.Escape(name);
            string pattern = $@"\b(?:#\s*define\s+{escaped}\s+(\d+)|(?:const|static\s+const)\s+(?:uint8_t|int|byte)\s+{escaped}\s*=\s*(\d+))";
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (!match.Success) return false;

            string value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (!int.TryParse(value, out var numericPin)) return false;
            return TryMapNumericPin(numericPin, out pin);
        }

        private static bool TryMapNumericPin(int numericPin, out string pin)
        {
            pin = null;
            if (numericPin < 0) return false;
            if (numericPin <= 13)
            {
                pin = $"D{numericPin}";
                return true;
            }
            if (numericPin >= 14 && numericPin <= 19)
            {
                pin = $"A{numericPin - 14}";
                return true;
            }
            return false;
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
            string primaryPath = Path.Combine(root, GetPrimarySketchFileName(_codeTargetComponentId));
            string first = File.Exists(primaryPath)
                ? primaryPath
                : Directory.EnumerateFiles(root, "*.ino", SearchOption.TopDirectoryOnly)
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
            AutoLayoutComponentsIfNeeded();
        }

        private void AutoLayoutComponentsIfNeeded()
        {
            if (_currentCircuit == null || _currentCircuit.Components == null) return;
            var components = _currentCircuit.Components;
            if (components.Count == 0) return;

            var withPosition = new List<ComponentSpec>();
            var missingPosition = new List<ComponentSpec>();
            foreach (var comp in components)
            {
                if (TryGetStoredPosition(comp, out _)) withPosition.Add(comp);
                else missingPosition.Add(comp);
            }

            if (missingPosition.Count == 0 && withPosition.Count == components.Count)
            {
                bool allZero = true;
                var unique = new HashSet<Vector2Int>();
                foreach (var comp in withPosition)
                {
                    if (!TryGetStoredPosition(comp, out var pos) || pos.sqrMagnitude > 0.01f)
                    {
                        allZero = false;
                    }
                    unique.Add(new Vector2Int(Mathf.RoundToInt(pos.x / GridSnap), Mathf.RoundToInt(pos.y / GridSnap)));
                }
                bool mostlyOverlapping = unique.Count <= Mathf.Max(1, components.Count / 3);
                bool hasOverlap = HasOverlappingComponents(withPosition);
                if (allZero || mostlyOverlapping || hasOverlap)
                {
                    withPosition.Clear();
                    missingPosition.Clear();
                    missingPosition.AddRange(components);
                }
            }

            if (missingPosition.Count == 0)
            {
                MaybeRecenterComponents(components);
                return;
            }

            var layoutTargets = withPosition.Count == 0 ? components : missingPosition;
            var layoutCenter = GetAutoLayoutCenter();
            Vector2 origin;

            if (withPosition.Count == 0)
            {
                var gridSize = GetAutoLayoutGridSize(layoutTargets);
                origin = layoutCenter - gridSize * 0.5f;
            }
            else
            {
                var bounds = GetExplicitBounds(withPosition);
                origin = new Vector2(bounds.xMax + AutoLayoutSpacing, bounds.yMin);
            }

            ApplyAutoLayout(layoutTargets, origin);
            if (withPosition.Count > 0)
            {
                MaybeRecenterComponents(withPosition);
            }
        }

        private Vector2 GetAutoLayoutCenter()
        {
            if (_boardView != null)
            {
                var rect = _boardView.layout;
                if (rect.width > 0f && rect.height > 0f)
                {
                    return new Vector2(rect.width * 0.5f, rect.height * 0.5f);
                }
            }
            return new Vector2(BoardWorldWidth * 0.5f, BoardWorldHeight * 0.5f);
        }

        private Vector2 GetAutoLayoutGridSize(List<ComponentSpec> components)
        {
            if (components == null || components.Count == 0) return Vector2.zero;
            float maxW = 0f;
            float maxH = 0f;
            foreach (var comp in components)
            {
                var size = GetComponentSize(string.IsNullOrWhiteSpace(comp.Type) ? string.Empty : comp.Type);
                maxW = Mathf.Max(maxW, size.x);
                maxH = Mathf.Max(maxH, size.y);
            }

            float cellW = maxW + AutoLayoutSpacing;
            float cellH = maxH + AutoLayoutSpacing;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(components.Count));
            int rows = Mathf.CeilToInt(components.Count / (float)cols);
            return new Vector2(cols * cellW - AutoLayoutSpacing, rows * cellH - AutoLayoutSpacing);
        }

        private Rect GetExplicitBounds(List<ComponentSpec> components)
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            foreach (var comp in components)
            {
                if (!TryGetStoredPosition(comp, out var pos)) continue;
                var size = GetComponentSize(string.IsNullOrWhiteSpace(comp.Type) ? string.Empty : comp.Type);
                minX = Mathf.Min(minX, pos.x);
                minY = Mathf.Min(minY, pos.y);
                maxX = Mathf.Max(maxX, pos.x + size.x);
                maxY = Mathf.Max(maxY, pos.y + size.y);
            }
            if (minX == float.MaxValue) return new Rect(0f, 0f, 0f, 0f);
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private bool HasOverlappingComponents(List<ComponentSpec> components)
        {
            if (components == null || components.Count < 2) return false;
            var rects = new List<Rect>(components.Count);
            foreach (var comp in components)
            {
                if (!TryGetStoredPosition(comp, out var pos)) continue;
                var size = GetComponentSize(string.IsNullOrWhiteSpace(comp.Type) ? string.Empty : comp.Type);
                rects.Add(new Rect(pos.x, pos.y, size.x, size.y));
            }

            for (int i = 0; i < rects.Count; i++)
            {
                for (int j = i + 1; j < rects.Count; j++)
                {
                    if (rects[i].Overlaps(rects[j]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void ApplyAutoLayout(List<ComponentSpec> components, Vector2 origin)
        {
            if (components == null || components.Count == 0) return;
            float maxW = 0f;
            float maxH = 0f;
            foreach (var comp in components)
            {
                var size = GetComponentSize(string.IsNullOrWhiteSpace(comp.Type) ? string.Empty : comp.Type);
                maxW = Mathf.Max(maxW, size.x);
                maxH = Mathf.Max(maxH, size.y);
            }

            float cellW = maxW + AutoLayoutSpacing;
            float cellH = maxH + AutoLayoutSpacing;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(components.Count));

            for (int i = 0; i < components.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                var comp = components[i];
                var pos = new Vector2(origin.x + col * cellW, origin.y + row * cellH);
                pos.x = Mathf.Round(pos.x / GridSnap) * GridSnap;
                pos.y = Mathf.Round(pos.y / GridSnap) * GridSnap;
                SetComponentPosition(comp, pos);
            }
        }

        private void MaybeRecenterComponents(List<ComponentSpec> components)
        {
            if (components == null || components.Count == 0) return;
            var bounds = GetExplicitBounds(components);
            if (bounds.width <= 0f || bounds.height <= 0f) return;

            Vector2 boardCenter = GetAutoLayoutCenter();
            float boardWidth = BoardWorldWidth;
            float boardHeight = BoardWorldHeight;
            if (_boardView != null && _boardView.layout.width > 0f && _boardView.layout.height > 0f)
            {
                boardWidth = _boardView.layout.width;
                boardHeight = _boardView.layout.height;
            }

            float leftThreshold = boardWidth * 0.25f;
            float topThreshold = boardHeight * 0.25f;
            if (bounds.center.x > leftThreshold || bounds.center.y > topThreshold) return;

            var delta = boardCenter - bounds.center;
            if (delta.sqrMagnitude < 1f) return;

            foreach (var comp in components)
            {
                if (!TryGetStoredPosition(comp, out var pos)) continue;
                var shifted = pos + delta;
                shifted.x = Mathf.Round(shifted.x / GridSnap) * GridSnap;
                shifted.y = Mathf.Round(shifted.y / GridSnap) * GridSnap;
                SetComponentPosition(comp, shifted);
            }
        }
        private void EnsureCanvasDecorations()
        {
            if (_canvasView == null) return;
            if (_boardView == null)
            {
                var surface = _canvasView.Q<VisualElement>(className: "canvas-surface");
                if (surface != null)
                {
                    _boardView = surface;
                }
                else
                {
                    _boardView = new VisualElement();
                    _boardView.name = "CanvasBoard";
                    _boardView.AddToClassList("canvas-board");
                    _canvasView.Add(_boardView);
                }

                _boardView.style.position = Position.Absolute;
                _boardView.style.transformOrigin = new TransformOrigin(0f, 0f, 0f);

                var staticGrids = _boardView.Query<VisualElement>(className: "canvas-grid").ToList();
                foreach (var grid in staticGrids)
                {
                    if (grid is GridLayer) continue;
                    grid.RemoveFromHierarchy();
                }

                if (_canvasHud != null && _canvasHud.parent != _canvasView)
                {
                    _canvasHud.RemoveFromHierarchy();
                    _canvasView.Add(_canvasHud);
                }
            }

            if (_gridLayer == null)
            {
                _gridLayer = new GridLayer
                {
                    Spacing = GridSnap,
                    MajorLineEvery = 5
                };
                _gridLayer.name = "CanvasGrid";
                _gridLayer.AddToClassList("canvas-grid-layer");
                _gridLayer.style.position = Position.Absolute;
                _gridLayer.style.left = 0;
                _gridLayer.style.top = 0;
                _gridLayer.style.right = 0;
                _gridLayer.style.bottom = 0;
                _canvasView.Insert(0, _gridLayer);
            }

            UpdateBoardLayout();
        }

        private void UpdateBoardLayout()
        {
            if (_canvasView == null || _boardView == null) return;
            var rect = _canvasView.contentRect;
            if (rect.width <= 0f || rect.height <= 0f) return;

            float width = Mathf.Max(BoardWorldWidth, rect.width - (BoardPadding * 2f));
            float height = Mathf.Max(BoardWorldHeight, rect.height - (BoardPadding * 2f));

            float left = (rect.width - width) * 0.5f;
            float top = (rect.height - height) * 0.5f;

            _boardView.style.left = left;
            _boardView.style.top = top;
            _boardView.style.width = width;
            _boardView.style.height = height;

            _gridLayer?.MarkDirtyRepaint();
            ApplyCanvasTransform();
            if (_gridLayer != null)
            {
                _gridLayer.SetTransform(_canvasPan, _canvasZoom, GetBoardOrigin());
            }
            UpdateWireLayer();
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
            if (_boardView != null)
            {
                _boardView.Add(_wireLayer);
                _wireLayer.BringToFront();
            }
            EnsureWirePreviewLayer();
        }

        private void EnsureWirePreviewLayer()
        {
            if (_canvasView == null || _wirePreviewLayer != null) return;
            EnsureCanvasDecorations();
            _wirePreviewLayer = new WireLayer();
            _wirePreviewLayer.style.position = Position.Absolute;
            _wirePreviewLayer.style.left = 0;
            _wirePreviewLayer.style.top = 0;
            _wirePreviewLayer.style.right = 0;
            _wirePreviewLayer.style.bottom = 0;
            if (_boardView != null)
            {
                _boardView.Add(_wirePreviewLayer);
                _wirePreviewLayer.BringToFront();
            }
        }

        private void UpdateWireLayer()
        {
            RequestBreadboardRebuild();
            if (_canvasView == null || _wireLayer == null || _currentCircuit == null) return;
            if (_currentCircuit.Nets == null) return;
            _wireSegments.Clear();

            var boardRect = GetBoardRect();
            if (boardRect.width <= 0f || boardRect.height <= 0f) return;

            var obstacles = GetWireObstacles();
            var router = new WireRouter(boardRect, GridSnap, obstacles);
            var netIds = new List<string>();

            foreach (var net in _currentCircuit.Nets.OrderBy(n => n.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                if (net == null || net.Nodes == null || net.Nodes.Count < 2) continue;
                var points = net.Nodes
                    .Select(node => GetWireAnchor(node, GetNodePosition(node)))
                    .Where(p => p.HasValue)
                    .Select(p => p.Value)
                    .ToList();
                if (points.Count < 2) continue;

                netIds.Add(net.Id ?? string.Empty);
                var anchorPin = points[0];
                for (int i = 1; i < points.Count; i++)
                {
                    var targetPin = points[i];
                    if (router.TryRoute(anchorPin, targetPin, net.Id ?? string.Empty, out var path))
                    {
                        AppendPathSegments(router, path, anchorPin, targetPin, net.Id ?? string.Empty);
                        router.CommitPath(path, net.Id ?? string.Empty);
                    }
                    else
                    {
                        AddOrthogonalSegmentsAvoidingObstacles(anchorPin, targetPin, net.Id ?? string.Empty, obstacles);
                    }
                }
            }

            var conflicts = router.BuildNetConflicts();
            var paletteMap = AssignNetPaletteIndices(netIds, conflicts);
            for (int i = 0; i < _wireSegments.Count; i++)
            {
                var seg = _wireSegments[i];
                if (!string.IsNullOrWhiteSpace(seg.NetId) && paletteMap.TryGetValue(seg.NetId, out var paletteIndex))
                {
                    seg.Color = WirePalette[paletteIndex];
                }
                else
                {
                    seg.Color = GetNetColor(seg.NetId);
                }
                _wireSegments[i] = seg;
            }

            _wireSegments.Sort((a, b) => GetNetDrawOrder(a.NetId).CompareTo(GetNetDrawOrder(b.NetId)));

            ((WireLayer)_wireLayer).SetSegments(_wireSegments);
            _wireLayer.BringToFront();
        }

        private void AddOrthogonalSegments(Vector2 start, Vector2 end, string netId)
        {
            if (Mathf.Approximately(start.x, end.x) || Mathf.Approximately(start.y, end.y))
            {
                AddWireSegment(start, end, netId);
                return;
            }

            bool horizontalFirst = (StableHash(netId) & 1u) == 0u;
            Vector2 mid = horizontalFirst
                ? new Vector2(end.x, start.y)
                : new Vector2(start.x, end.y);

            AddWireSegment(start, mid, netId);
            AddWireSegment(mid, end, netId);
        }

        private void AddOrthogonalSegmentsAvoidingObstacles(Vector2 start, Vector2 end, string netId, List<Rect> obstacles)
        {
            if (obstacles == null || obstacles.Count == 0)
            {
                AddOrthogonalSegments(start, end, netId);
                return;
            }

            if (Mathf.Approximately(start.x, end.x) || Mathf.Approximately(start.y, end.y))
            {
                if (!SegmentIntersectsObstacles(start, end, obstacles))
                {
                    AddWireSegment(start, end, netId);
                    return;
                }
            }

            bool horizontalFirst = (StableHash(netId) & 1u) == 0u;
            Vector2 midA = horizontalFirst ? new Vector2(end.x, start.y) : new Vector2(start.x, end.y);
            Vector2 midB = horizontalFirst ? new Vector2(start.x, end.y) : new Vector2(end.x, start.y);

            if (IsOrthogonalPathClear(start, midA, end, obstacles))
            {
                AddWireSegment(start, midA, netId);
                AddWireSegment(midA, end, netId);
                return;
            }
            if (IsOrthogonalPathClear(start, midB, end, obstacles))
            {
                AddWireSegment(start, midB, netId);
                AddWireSegment(midB, end, netId);
                return;
            }

            float offsetStep = WireObstaclePadding + (GridSnap * 2f);
            for (int i = 1; i <= 6; i++)
            {
                float offset = offsetStep * i;
                if (horizontalFirst)
                {
                    if (TryOffsetMid(start, end, netId, new Vector2(0f, offset), obstacles, out var mid) ||
                        TryOffsetMid(start, end, netId, new Vector2(0f, -offset), obstacles, out mid))
                    {
                        AddWireSegment(start, mid, netId);
                        AddWireSegment(mid, end, netId);
                        return;
                    }
                }
                else
                {
                    if (TryOffsetMid(start, end, netId, new Vector2(offset, 0f), obstacles, out var mid) ||
                        TryOffsetMid(start, end, netId, new Vector2(-offset, 0f), obstacles, out mid))
                    {
                        AddWireSegment(start, mid, netId);
                        AddWireSegment(mid, end, netId);
                        return;
                    }
                }
            }

            AddOrthogonalSegments(start, end, netId);
        }

        private bool TryOffsetMid(Vector2 start, Vector2 end, string netId, Vector2 offset, List<Rect> obstacles, out Vector2 mid)
        {
            bool horizontalFirst = (StableHash(netId) & 1u) == 0u;
            mid = horizontalFirst ? new Vector2(end.x, start.y) : new Vector2(start.x, end.y);
            mid += offset;
            mid.x = Mathf.Round(mid.x / GridSnap) * GridSnap;
            mid.y = Mathf.Round(mid.y / GridSnap) * GridSnap;
            var boardRect = GetBoardRect();
            if (boardRect.width > 0f && boardRect.height > 0f)
            {
                mid.x = Mathf.Clamp(mid.x, boardRect.xMin, boardRect.xMax);
                mid.y = Mathf.Clamp(mid.y, boardRect.yMin, boardRect.yMax);
            }
            return IsOrthogonalPathClear(start, mid, end, obstacles);
        }

        private static bool IsOrthogonalPathClear(Vector2 start, Vector2 mid, Vector2 end, List<Rect> obstacles)
        {
            return !SegmentIntersectsObstacles(start, mid, obstacles) &&
                   !SegmentIntersectsObstacles(mid, end, obstacles);
        }

        private static bool SegmentIntersectsObstacles(Vector2 start, Vector2 end, List<Rect> obstacles)
        {
            foreach (var rect in obstacles)
            {
                if (SegmentIntersectsRect(start, end, rect)) return true;
            }
            return false;
        }

        private static bool SegmentIntersectsRect(Vector2 start, Vector2 end, Rect rect)
        {
            if (Mathf.Approximately(start.x, end.x))
            {
                float x = start.x;
                if (x < rect.xMin || x > rect.xMax) return false;
                float minY = Mathf.Min(start.y, end.y);
                float maxY = Mathf.Max(start.y, end.y);
                return maxY >= rect.yMin && minY <= rect.yMax;
            }
            if (Mathf.Approximately(start.y, end.y))
            {
                float y = start.y;
                if (y < rect.yMin || y > rect.yMax) return false;
                float minX = Mathf.Min(start.x, end.x);
                float maxX = Mathf.Max(start.x, end.x);
                return maxX >= rect.xMin && minX <= rect.xMax;
            }
            return rect.Contains(start) || rect.Contains(end);
        }

        private Vector2? GetWireAnchor(string node, Vector2? rawPoint)
        {
            if (!rawPoint.HasValue) return null;
            var point = rawPoint.Value;
            if (string.IsNullOrWhiteSpace(node) || _currentCircuit == null) return point;

            var parts = node.Split('.');
            if (parts.Length == 0) return point;
            string compId = parts[0];
            var spec = _currentCircuit.Components.FirstOrDefault(c => c.Id == compId);
            if (spec == null) return point;

            var size = GetComponentSize(string.IsNullOrWhiteSpace(spec.Type) ? string.Empty : spec.Type);
            var pos = GetComponentPosition(compId);
            var rect = new Rect(pos.x, pos.y, size.x, size.y);
            if (!rect.Contains(point)) return point;

            float leftDist = point.x - rect.xMin;
            float rightDist = rect.xMax - point.x;
            float topDist = point.y - rect.yMin;
            float bottomDist = rect.yMax - point.y;

            float min = Mathf.Min(leftDist, rightDist, topDist, bottomDist);
            if (min == leftDist)
            {
                point.x = rect.xMin - WireExitPadding;
            }
            else if (min == rightDist)
            {
                point.x = rect.xMax + WireExitPadding;
            }
            else if (min == topDist)
            {
                point.y = rect.yMin - WireExitPadding;
            }
            else
            {
                point.y = rect.yMax + WireExitPadding;
            }

            point.x = Mathf.Round(point.x / GridSnap) * GridSnap;
            point.y = Mathf.Round(point.y / GridSnap) * GridSnap;
            var boardRect = GetBoardRect();
            if (boardRect.width > 0f && boardRect.height > 0f)
            {
                point.x = Mathf.Clamp(point.x, boardRect.xMin, boardRect.xMax);
                point.y = Mathf.Clamp(point.y, boardRect.yMin, boardRect.yMax);
            }
            return point;
        }

        private void AddWireSegment(Vector2 start, Vector2 end, string netId)
        {
            if ((end - start).sqrMagnitude < 0.25f) return;
            if (!Mathf.Approximately(start.x, end.x) && !Mathf.Approximately(start.y, end.y))
            {
                bool horizontalFirst = (StableHash(netId) & 1u) == 0u;
                Vector2 mid = horizontalFirst
                    ? new Vector2(end.x, start.y)
                    : new Vector2(start.x, end.y);
                AddWireSegmentRaw(start, mid, netId);
                AddWireSegmentRaw(mid, end, netId);
                return;
            }
            AddWireSegmentRaw(start, end, netId);
        }

        private void AddWireSegmentRaw(Vector2 start, Vector2 end, string netId)
        {
            if ((end - start).sqrMagnitude < 0.25f) return;
            _wireSegments.Add(new WireSegment
            {
                Start = start,
                End = end,
                NetId = netId
            });
        }

        private Color GetNetColor(string netId)
        {
            if (string.IsNullOrWhiteSpace(netId)) return new Color(0.2f, 0.8f, 1f, 0.9f);
            int index = (int)(StableHash(netId) % (uint)WirePalette.Length);
            return WirePalette[index];
        }

        private Rect GetBoardRect()
        {
            if (_boardView != null)
            {
                var rect = _boardView.layout;
                if (rect.width > 0f && rect.height > 0f)
                {
                    return new Rect(0f, 0f, rect.width, rect.height);
                }
            }
            if (_canvasView != null)
            {
                var rect = _canvasView.contentRect;
                return new Rect(0f, 0f, rect.width, rect.height);
            }
            return new Rect(0f, 0f, 0f, 0f);
        }

        private List<Rect> GetWireObstacles()
        {
            var obstacles = new List<Rect>();
            foreach (var kvp in _componentVisuals)
            {
                var el = kvp.Value;
                if (el == null) continue;
                var rect = el.layout;
                if (rect.width <= 0f || rect.height <= 0f) continue;
                var inflated = new Rect(
                    rect.x - WireObstaclePadding,
                    rect.y - WireObstaclePadding,
                    rect.width + WireObstaclePadding * 2f,
                    rect.height + WireObstaclePadding * 2f
                );
                obstacles.Add(inflated);
            }
            return obstacles;
        }

        private void AppendPathSegments(WireRouter router, List<Vector2Int> path, Vector2 startPin, Vector2 endPin, string netId)
        {
            if (path == null || path.Count == 0) return;
            var points = new List<Vector2>(path.Count);
            foreach (var cell in path)
            {
                points.Add(router.CellToPoint(cell));
            }

            if ((startPin - points[0]).sqrMagnitude > 0.25f)
            {
                AddOrthogonalSegments(startPin, points[0], netId);
            }

            Vector2 segmentStart = points[0];
            Vector2 direction = Vector2.zero;
            for (int i = 1; i < points.Count; i++)
            {
                var delta = points[i] - points[i - 1];
                var newDir = new Vector2(Mathf.Sign(delta.x), Mathf.Sign(delta.y));
                if (direction == Vector2.zero)
                {
                    direction = newDir;
                    continue;
                }

                if (newDir != direction)
                {
                    AddWireSegment(segmentStart, points[i - 1], netId);
                    segmentStart = points[i - 1];
                    direction = newDir;
                }
            }

            AddWireSegment(segmentStart, points[points.Count - 1], netId);

            if ((endPin - points[points.Count - 1]).sqrMagnitude > 0.25f)
            {
                AddOrthogonalSegments(points[points.Count - 1], endPin, netId);
            }
        }

        private Dictionary<string, int> AssignNetPaletteIndices(IEnumerable<string> netIds, Dictionary<string, HashSet<string>> conflicts)
        {
            var ordered = netIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var paletteMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var netId in ordered)
            {
                int start = (int)(StableHash(netId) % (uint)WirePalette.Length);
                int chosen = start;
                for (int i = 0; i < WirePalette.Length; i++)
                {
                    int idx = (start + i) % WirePalette.Length;
                    if (!HasColorConflict(netId, idx, paletteMap, conflicts))
                    {
                        chosen = idx;
                        break;
                    }
                }
                paletteMap[netId] = chosen;
            }
            return paletteMap;
        }

        private bool HasColorConflict(string netId, int paletteIndex, Dictionary<string, int> paletteMap, Dictionary<string, HashSet<string>> conflicts)
        {
            if (!conflicts.TryGetValue(netId, out var neighbors) || neighbors.Count == 0) return false;
            foreach (var neighbor in neighbors)
            {
                if (paletteMap.TryGetValue(neighbor, out var neighborIndex) && neighborIndex == paletteIndex)
                {
                    return true;
                }
            }
            return false;
        }

        private static uint StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var ch in text)
                {
                    hash ^= ch;
                    hash *= 16777619;
                }
                return hash;
            }
        }

        private int GetNetDrawOrder(string netId)
        {
            if (string.IsNullOrWhiteSpace(netId)) return int.MaxValue;
            return (int)(StableHash(netId) % 100000);
        }

        private Vector2? GetNodePosition(string node)
        {
            if (string.IsNullOrWhiteSpace(node)) return null;
            var parts = node.Split('.');
            if (parts.Length < 1) return null;
            string compId = parts[0];
            if (_pinVisuals.TryGetValue(node, out var pinEl) && _boardView != null)
            {
                var worldBound = pinEl.worldBound;
                if (worldBound.width > 0f && worldBound.height > 0f)
                {
                    return _boardView.WorldToLocal(worldBound.center);
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

        private bool TryGetStoredPosition(ComponentSpec comp, out Vector2 pos)
        {
            pos = Vector2.zero;
            if (comp == null || comp.Properties == null) return false;
            if (comp.Properties.TryGetValue("posX", out var xRaw) &&
                comp.Properties.TryGetValue("posY", out var yRaw) &&
                float.TryParse(xRaw, out var x) &&
                float.TryParse(yRaw, out var y))
            {
                pos = new Vector2(x, y);
                return true;
            }
            return false;
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
            if (!_constrainComponentsToBoard) return pos;
            if (_boardView == null) return pos;
            var rect = _boardView.layout;
            if (rect.width <= 0f || rect.height <= 0f) return pos;

            float minX = 0f;
            float minY = 0f;
            float maxX = rect.width - size.x;
            float maxY = rect.height - size.y;
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
            if (!_constrainComponentsToBoard) return pos;
            if (_boardView != null)
            {
                var rect = _boardView.layout;
                if (rect.width > 0f && rect.height > 0f)
                {
                    float maxX = Mathf.Max(0f, rect.width - size.x);
                    float maxY = Mathf.Max(0f, rect.height - size.y);
                    float x = Mathf.Clamp(pos.x, 0f, maxX);
                    float y = Mathf.Clamp(pos.y, 0f, maxY);
                    return new Vector2(x, y);
                }
            }

            float maxXFallback = Mathf.Max(0f, ViewportSize.x - size.x);
            float maxYFallback = Mathf.Max(0f, ViewportSize.y - size.y);
            float clampX = Mathf.Clamp(pos.x, 0f, maxXFallback);
            float clampY = Mathf.Clamp(pos.y, 0f, maxYFallback);
            return new Vector2(clampX, clampY);
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
            rootLabel.pickingMode = PickingMode.Position;
            root.Add(rootLabel);
            AttachRootContextMenu(rootLabel);

            foreach (var comp in _currentCircuit.Components)
            {
                var label = new Label($"   - {comp.Id} ({comp.Type})");
                label.AddToClassList("tree-item");
                label.pickingMode = PickingMode.Position;
                root.Add(label);
                AttachComponentContextMenu(label, comp);
                label.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.button != 0 || evt.clickCount < 2) return;
                    SelectComponentFromTree(comp);
                    evt.StopPropagation();
                });
            }

            if (_currentCircuit.Nets != null && _currentCircuit.Nets.Count > 0)
            {
                var netHeader = new Label($"   Nets ({_currentCircuit.Nets.Count})");
                netHeader.AddToClassList("tree-item");
                netHeader.pickingMode = PickingMode.Position;
                root.Add(netHeader);
                AttachRootContextMenu(netHeader);
            }

            _projectTreeList.Add(root);
            PopulateConnectionsList();
        }

        private void SelectComponentFromTree(ComponentSpec comp)
        {
            if (comp == null) return;
            if (!_componentVisuals.TryGetValue(comp.Id, out var visual)) return;
            var item = ResolveCatalogItem(comp);
            SelectComponent(visual, comp, item);
            CenterAndZoomOnSelection();
        }

        private void PopulateConnectionsList()
        {
            if (_connectionsList == null) return;
            _connectionsList.Clear();

            var header = new VisualElement();
            header.AddToClassList("connection-row");
            header.AddToClassList("header");
            header.pickingMode = PickingMode.Position;
            var netHeader = new Label("Net");
            netHeader.AddToClassList("connection-col-net");
            var nodesHeader = new Label("Nodes");
            nodesHeader.AddToClassList("connection-col-nodes");
            var editBtn = new Button { text = "Edit" };
            editBtn.AddToClassList("connection-edit-btn");
            editBtn.RegisterCallback<ClickEvent>(_ => ShowNetEditor());
            header.Add(netHeader);
            header.Add(nodesHeader);
            header.Add(editBtn);
            _connectionsList.Add(header);

            if (_currentCircuit == null || _currentCircuit.Nets == null || _currentCircuit.Nets.Count == 0)
            {
                var empty = new Label("No connections yet.");
                empty.AddToClassList("console-line");
                _connectionsList.Add(empty);
                return;
            }

            foreach (var net in _currentCircuit.Nets)
            {
                if (net == null) continue;
                var row = new VisualElement();
                row.AddToClassList("connection-row");
                row.pickingMode = PickingMode.Position;

                var netLabel = new Label(string.IsNullOrWhiteSpace(net.Id) ? "NET" : net.Id);
                netLabel.AddToClassList("connection-col-net");

                string nodes = net.Nodes != null ? string.Join(", ", net.Nodes) : "None";
                var nodesLabel = new Label(nodes);
                nodesLabel.AddToClassList("connection-col-nodes");

                row.Add(netLabel);
                row.Add(nodesLabel);
                _connectionsList.Add(row);

                AttachNetContextMenu(row, net);
            }
        }


        private void ApplyCodeEditorTheme()
        {
            if (_codeEditor == null) return;

            // Light blue cursor color
            Color lightBlueCursor = new Color(0.25f, 0.65f, 1f, 1f);
            Color selection = new Color(0.22f, 0.62f, 0.9f, 0.35f);

            void InternalApply()
            {
                // 1. Try on the TextField itself
                TrySetColorProperty(_codeEditor, "cursorColor", lightBlueCursor);
                TrySetColorProperty(_codeEditor, "selectionColor", selection);
                TrySetColorProperty(_codeEditor.style, "cursorColor", lightBlueCursor);

                // 2. Try on the internal TextInput element
                var input = _codeEditor.Q(className: "unity-text-input")
                    ?? _codeEditor.Q(className: "unity-text-field__input")
                    ?? _codeEditor.Children().FirstOrDefault(c => c.GetType().Name.Contains("Input"));

                if (input != null)
                {
                    input.style.color = Color.white; // Ensure text is white

                    TrySetColorProperty(input, "cursorColor", lightBlueCursor);
                    TrySetColorProperty(input, "selectionColor", selection);
                    TrySetColorProperty(input.style, "cursorColor", lightBlueCursor);
                    TrySetColorProperty(input.style, "selectionColor", selection);

                    // 3. Try on the TextElement (the actual renderer in Unity 6)
                    var textElement = input.Q<TextElement>() ?? input.Children().OfType<TextElement>().FirstOrDefault();
                    if (textElement != null)
                    {
                        TrySetColorProperty(textElement, "cursorColor", lightBlueCursor);
                        TrySetColorProperty(textElement.style, "cursorColor", lightBlueCursor);
                    }

                    // 4. Recursive search for any cursor-like elements
                    FindAndColorCursor(input, lightBlueCursor);

                    ApplyCodeEditorHighlightOverlay(textElement);
                    SyncCodeHighlightStyle();
                }
            }

            // Run multiple times to fight against Unity's internal resets
            InternalApply();
            _codeEditor.schedule.Execute(InternalApply).ExecuteLater(50);
            _codeEditor.schedule.Execute(InternalApply).ExecuteLater(250);
            _codeEditor.schedule.Execute(InternalApply).ExecuteLater(1000);
        }

        private void SetupCodeEditorSyntaxHighlighting()
        {
            if (_codeEditor == null || _codeHighlightReady) return;

            var input = _codeEditor.Q(className: "unity-text-input")
                ?? _codeEditor.Q(className: "unity-text-field__input")
                ?? _codeEditor.Children().FirstOrDefault(c => c.GetType().Name.Contains("Input"));

            if (input == null) return;

            var textElement = input.Q<TextElement>() ?? input.Children().OfType<TextElement>().FirstOrDefault();
            if (textElement == null) return;

            _codeHighlightTarget = textElement;
            _codeHighlightInput = input;
            if (_codeHighlightLabel == null)
            {
                _codeHighlightLabel = new Label
                {
                    name = "CodeHighlightOverlay"
                };
                _codeHighlightLabel.enableRichText = true;
                _codeHighlightLabel.pickingMode = PickingMode.Ignore;
                _codeHighlightLabel.AddToClassList("code-highlight-overlay");
                _codeHighlightLabel.style.position = Position.Absolute;
                _codeHighlightLabel.style.left = 0;
                _codeHighlightLabel.style.top = 0;
                _codeHighlightLabel.style.right = 0;
                _codeHighlightLabel.style.bottom = 0;
                _codeHighlightLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            }
            if (!_codeHighlightEventsHooked)
            {
                _codeHighlightLabel.RegisterCallback<ChangeEvent<string>>(evt => evt.StopPropagation());
                _codeHighlightEventsHooked = true;
            }

            VisualElement container = input;
            var scrollView = input.Q<ScrollView>();
            if (scrollView != null)
            {
                container = scrollView.contentContainer;
            }

            if (_codeHighlightLabel.parent != container)
            {
                _codeHighlightLabel.RemoveFromHierarchy();
                container.Add(_codeHighlightLabel);
            }

            _codeHighlightReady = true;
            ApplyCodeEditorHighlightOverlay(textElement);
            SyncCodeHighlightStyle();
            UpdateCodeSyntaxHighlight(_codeEditor.value);
        }

        private void ApplyCodeEditorHighlightOverlay(TextElement textElement)
        {
            if (!_codeHighlightReady || _codeHighlightLabel == null || textElement == null) return;
            textElement.style.color = new Color(0f, 0f, 0f, 0f);
            textElement.style.unityTextAlign = TextAnchor.UpperLeft;
        }

        private void SyncCodeHighlightStyle()
        {
            if (!_codeHighlightReady || _codeHighlightLabel == null || _codeHighlightTarget == null) return;

            var resolved = _codeHighlightTarget.resolvedStyle;
            var inputResolved = _codeHighlightInput != null ? _codeHighlightInput.resolvedStyle : resolved;
            _codeHighlightLabel.style.fontSize = resolved.fontSize;
            _codeHighlightLabel.style.paddingLeft = inputResolved.paddingLeft;
            _codeHighlightLabel.style.paddingRight = inputResolved.paddingRight;
            _codeHighlightLabel.style.paddingTop = inputResolved.paddingTop;
            _codeHighlightLabel.style.paddingBottom = inputResolved.paddingBottom;
            _codeHighlightLabel.style.unityTextAlign = TextAnchor.UpperLeft;
        }

        private void UpdateCodeSyntaxHighlight(string source)
        {
            if (!_codeHighlightReady || _codeHighlightLabel == null) return;
            source ??= string.Empty;
            if (string.Equals(source, _lastHighlightText, StringComparison.Ordinal)) return;
            _lastHighlightText = source;
            bool restoreSuppress = _suppressCodeEditorChanged;
            _suppressCodeEditorChanged = true;
            try
            {
                _codeHighlightLabel.text = source.Length > HighlightMaxChars
                    ? EscapeRichText(source)
                    : BuildSyntaxHighlightedText(source);
            }
            finally
            {
                _suppressCodeEditorChanged = restoreSuppress;
            }
        }

        private static string EscapeRichText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string BuildSyntaxHighlightedText(string source)
        {
            const string keywordColor = "#569CD6";
            const string commentColor = "#6A9955";
            const string stringColor = "#CE9178";
            const string numberColor = "#B5CEA8";
            const string preprocessorColor = "#C586C0";

            var sb = new StringBuilder(source.Length + 128);
            bool inLineComment = false;
            bool inBlockComment = false;
            bool inString = false;
            bool inChar = false;
            bool inPreprocessor = false;
            bool escaped = false;
            bool lineWhitespaceOnly = true;

            void OpenColor(string color) => sb.Append("<color=").Append(color).Append(">");
            void CloseColor() => sb.Append("</color>");

            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                char next = i + 1 < source.Length ? source[i + 1] : '\0';

                if (inLineComment || inPreprocessor)
                {
                    if (c == '\n')
                    {
                        AppendEscapedChar(sb, c);
                        CloseColor();
                        inLineComment = false;
                        inPreprocessor = false;
                        lineWhitespaceOnly = true;
                    }
                    else
                    {
                        AppendEscapedChar(sb, c);
                    }
                    continue;
                }

                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        AppendEscapedChar(sb, c);
                        AppendEscapedChar(sb, next);
                        CloseColor();
                        inBlockComment = false;
                        i++;
                    }
                    else
                    {
                        AppendEscapedChar(sb, c);
                    }
                    continue;
                }

                if (inString || inChar)
                {
                    AppendEscapedChar(sb, c);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if ((inString && c == '"') || (inChar && c == '\''))
                    {
                        CloseColor();
                        inString = false;
                        inChar = false;
                    }
                    if (c == '\n') lineWhitespaceOnly = true;
                    else if (!char.IsWhiteSpace(c)) lineWhitespaceOnly = false;
                    continue;
                }

                if (lineWhitespaceOnly && c == '#')
                {
                    OpenColor(preprocessorColor);
                    inPreprocessor = true;
                    AppendEscapedChar(sb, c);
                    lineWhitespaceOnly = false;
                    continue;
                }

                if (c == '/' && next == '/')
                {
                    OpenColor(commentColor);
                    inLineComment = true;
                    AppendEscapedChar(sb, c);
                    AppendEscapedChar(sb, next);
                    i++;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    OpenColor(commentColor);
                    inBlockComment = true;
                    AppendEscapedChar(sb, c);
                    AppendEscapedChar(sb, next);
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    OpenColor(stringColor);
                    inString = true;
                    AppendEscapedChar(sb, c);
                    lineWhitespaceOnly = false;
                    continue;
                }

                if (c == '\'')
                {
                    OpenColor(stringColor);
                    inChar = true;
                    AppendEscapedChar(sb, c);
                    lineWhitespaceOnly = false;
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int start = i;
                    while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.' || source[i] == 'x' || source[i] == 'X'
                        || (source[i] >= 'a' && source[i] <= 'f') || (source[i] >= 'A' && source[i] <= 'F')))
                    {
                        i++;
                    }
                    string number = source.Substring(start, i - start);
                    OpenColor(numberColor);
                    AppendEscapedRange(sb, number);
                    CloseColor();
                    i--;
                    lineWhitespaceOnly = false;
                    continue;
                }

                if (IsIdentifierStart(c))
                {
                    int start = i;
                    i++;
                    while (i < source.Length && IsIdentifierPart(source[i])) i++;
                    string ident = source.Substring(start, i - start);
                    if (CodeKeywords.Contains(ident))
                    {
                        OpenColor(keywordColor);
                        AppendEscapedRange(sb, ident);
                        CloseColor();
                    }
                    else
                    {
                        AppendEscapedRange(sb, ident);
                    }
                    i--;
                    lineWhitespaceOnly = false;
                    continue;
                }

                AppendEscapedChar(sb, c);
                if (c == '\n') lineWhitespaceOnly = true;
                else if (!char.IsWhiteSpace(c)) lineWhitespaceOnly = false;
            }

            if (inLineComment || inBlockComment || inString || inChar || inPreprocessor)
            {
                CloseColor();
            }

            return sb.ToString();
        }

        private static void AppendEscapedChar(StringBuilder sb, char c)
        {
            if (c == '<') sb.Append("&lt;");
            else if (c == '>') sb.Append("&gt;");
            else if (c == '&') sb.Append("&amp;");
            else sb.Append(c);
        }

        private static void AppendEscapedRange(StringBuilder sb, string text)
        {
            foreach (var c in text)
            {
                AppendEscapedChar(sb, c);
            }
        }

        private static bool IsIdentifierStart(char c)
        {
            return char.IsLetter(c) || c == '_';
        }

        private static bool IsIdentifierPart(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        private void FindAndColorCursor(VisualElement element, Color color)
        {
            if (element == null) return;
            foreach (var child in element.Children())
            {
                if (child.name.ToLower().Contains("cursor") || child.GetClasses().Any(c => c.ToLower().Contains("cursor")))
                {
                    child.style.backgroundColor = color;
                    child.style.visibility = Visibility.Visible;
                    child.style.display = DisplayStyle.Flex;
                    child.style.width = 2f;
                }
                FindAndColorCursor(child, color);
            }
        }

        private static void TrySetColorProperty(object target, string propertyName, Color value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName)) return;
            var type = target.GetType();

            // Try to find property (case-insensitive)
            var prop = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (prop != null && prop.CanWrite)
            {
                try
                {
                    if (prop.PropertyType == typeof(Color)) prop.SetValue(target, value);
                    else if (prop.PropertyType == typeof(StyleColor)) prop.SetValue(target, new StyleColor(value));
                }
                catch { }
            }

            // Also try fields
            var field = type.GetField(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (field != null)
            {
                try
                {
                    if (field.FieldType == typeof(Color)) field.SetValue(target, value);
                    else if (field.FieldType == typeof(StyleColor)) field.SetValue(target, new StyleColor(value));
                }
                catch { }
            }
        }

        private void OnCodeEditorChanged(ChangeEvent<string> evt)
        {
            if (_suppressCodeEditorChanged || _suppressCodeHistory) return;
            if (_codeHistoryIndex >= 0 && _codeHistoryIndex < _codeHistory.Count)
            {
                if (string.Equals(_codeHistory[_codeHistoryIndex].Text, evt.newValue, StringComparison.Ordinal))
                {
                    return;
                }
            }
            if (_codeHistoryIndex < _codeHistory.Count - 1)
            {
                _codeHistory.RemoveRange(_codeHistoryIndex + 1, _codeHistory.Count - _codeHistoryIndex - 1);
            }
            _codeHistory.Add(BuildCodeEditorSnapshot(evt.newValue));
            if (_codeHistory.Count > MaxCodeHistory)
            {
                _codeHistory.RemoveAt(0);
            }
            _codeHistoryIndex = _codeHistory.Count - 1;

            // Re-apply theme if it got reset during typing
            ApplyCodeEditorTheme();
            UpdateCodeSyntaxHighlight(evt.newValue);
        }

        private void OnCodeEditorKeyDown(KeyDownEvent evt)
        {
            if (_codeEditor == null) return;
            if (!IsCodeEditorFocused()) return;
            if (evt.ctrlKey || evt.altKey || evt.commandKey) return;

            if (evt.keyCode == KeyCode.Tab)
            {
                if (HandleCodeEditorTab(evt.shiftKey))
                {
                    IgnoreUiEvent(evt);
                    evt.StopPropagation();
                }
                return;
            }

            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                if (HandleCodeEditorNewLine())
                {
                    IgnoreUiEvent(evt);
                    evt.StopPropagation();
                }
                return;
            }

            char typed = evt.character;
            if (typed == '\0') return;

            if (typed == '{' && HandleCodeEditorPair('{', '}'))
            {
                IgnoreUiEvent(evt);
                evt.StopPropagation();
                return;
            }

            if (typed == '(' && HandleCodeEditorPair('(', ')'))
            {
                IgnoreUiEvent(evt);
                evt.StopPropagation();
                return;
            }

            if (typed == '[' && HandleCodeEditorPair('[', ']'))
            {
                IgnoreUiEvent(evt);
                evt.StopPropagation();
                return;
            }

            if (typed == '"' && HandleCodeEditorPair('"', '"'))
            {
                IgnoreUiEvent(evt);
                evt.StopPropagation();
                return;
            }

            if (typed == '\'' && HandleCodeEditorPair('\'', '\''))
            {
                IgnoreUiEvent(evt);
                evt.StopPropagation();
                return;
            }

            if (typed == '}' && HandleCodeEditorClosingBrace())
            {
                IgnoreUiEvent(evt);
                evt.StopPropagation();
            }
        }

        private void IgnoreUiEvent(EventBase evt)
        {
            _root?.panel?.focusController?.IgnoreEvent(evt);
        }

        private bool HandleCodeEditorTab(bool outdent)
        {
            if (!TryGetSelectionIndices(_codeEditor, out int cursorIndex, out int selectionIndex)) return false;
            string text = _codeEditor.value ?? string.Empty;
            int start = Math.Min(cursorIndex, selectionIndex);
            int end = Math.Max(cursorIndex, selectionIndex);
            string indent = new string(' ', CodeIndentSpaces);

            int firstNewLine = text.IndexOf('\n', start);
            bool spansMultipleLines = firstNewLine != -1 && firstNewLine < end;
            if (!spansMultipleLines && start == end)
            {
                if (outdent)
                {
                    int singleLineStart = FindLineStart(text, start);
                    string prefix = text.Substring(singleLineStart, start - singleLineStart);
                    int remove = CountIndentToRemove(prefix);
                    if (remove > 0)
                    {
                        string updatedLine = text.Remove(singleLineStart, remove);
                        ApplyCodeEditorValue(updatedLine, start - remove, start - remove);
                        return true;
                    }
                    return false;
                }
                return ApplyCodeEditorEdit(text, start, end, indent, start + indent.Length);
            }

            int lineStart = FindLineStart(text, start);
            int blockEnd = text.IndexOf('\n', end);
            if (blockEnd == -1) blockEnd = text.Length;
            string block = text.Substring(lineStart, blockEnd - lineStart);
            string[] lines = block.Split('\n');

            var sb = new StringBuilder(block.Length + lines.Length * indent.Length);
            int currentIndex = lineStart;
            int newStart = start;
            int newEnd = end;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int delta = 0;
                if (outdent)
                {
                    int remove = CountIndentToRemove(line);
                    if (remove > 0)
                    {
                        line = line.Substring(remove);
                        delta = -remove;
                    }
                }
                else
                {
                    line = indent + line;
                    delta = indent.Length;
                }

                if (currentIndex <= start)
                {
                    newStart += delta;
                }
                if (currentIndex < end)
                {
                    newEnd += delta;
                }

                sb.Append(line);
                if (i < lines.Length - 1)
                {
                    sb.Append('\n');
                }

                currentIndex += lines[i].Length + 1;
            }

            string updated = text.Substring(0, lineStart) + sb + text.Substring(blockEnd);
            ApplyCodeEditorValue(updated, newStart, newEnd);
            return true;
        }

        private bool HandleCodeEditorNewLine()
        {
            if (!TryGetSelectionIndices(_codeEditor, out int cursorIndex, out int selectionIndex)) return false;
            string text = _codeEditor.value ?? string.Empty;
            int start = Math.Min(cursorIndex, selectionIndex);
            int end = Math.Max(cursorIndex, selectionIndex);

            int lineStart = FindLineStart(text, start);
            string linePrefix = text.Substring(lineStart, start - lineStart);
            string indent = GetLineIndent(linePrefix);
            string trimmed = linePrefix.TrimEnd();
            bool addIndent = trimmed.EndsWith("{", StringComparison.Ordinal);

            string extraIndent = addIndent ? new string(' ', CodeIndentSpaces) : string.Empty;
            bool nextIsClosingBrace = false;
            int scan = start;
            while (scan < text.Length && (text[scan] == ' ' || text[scan] == '\t')) scan++;
            if (scan < text.Length && text[scan] == '}') nextIsClosingBrace = true;

            string insert;
            int newCursor;
            if (addIndent && nextIsClosingBrace)
            {
                insert = "\n" + indent + extraIndent + "\n" + indent;
                newCursor = start + 1 + indent.Length + extraIndent.Length;
            }
            else
            {
                insert = "\n" + indent + extraIndent;
                newCursor = start + insert.Length;
            }

            ApplyCodeEditorEdit(text, start, end, insert, newCursor);
            return true;
        }

        private bool HandleCodeEditorPair(char openChar, char closeChar)
        {
            if (!TryGetSelectionIndices(_codeEditor, out int cursorIndex, out int selectionIndex)) return false;
            string text = _codeEditor.value ?? string.Empty;
            int start = Math.Min(cursorIndex, selectionIndex);
            int end = Math.Max(cursorIndex, selectionIndex);
            string selected = start != end ? text.Substring(start, end - start) : string.Empty;
            string insert = $"{openChar}{selected}{closeChar}";
            int newCursor = start + 1 + selected.Length;
            ApplyCodeEditorEdit(text, start, end, insert, newCursor);
            return true;
        }

        private bool HandleCodeEditorClosingBrace()
        {
            if (!TryGetSelectionIndices(_codeEditor, out int cursorIndex, out int selectionIndex)) return false;
            string text = _codeEditor.value ?? string.Empty;
            int start = Math.Min(cursorIndex, selectionIndex);
            int end = Math.Max(cursorIndex, selectionIndex);

            int lineStart = FindLineStart(text, start);
            string linePrefix = text.Substring(lineStart, start - lineStart);
            if (linePrefix.Length > 0 && linePrefix.All(char.IsWhiteSpace))
            {
                int remove = CountIndentToRemove(linePrefix);
                if (remove > 0)
                {
                    text = text.Remove(start - remove, remove);
                    start -= remove;
                    end = start;
                }
                ApplyCodeEditorEdit(text, start, end, "}", start + 1);
                return true;
            }

            return false;
        }

        private bool TryGetSelectionIndices(TextField field, out int cursorIndex, out int selectionIndex)
        {
            cursorIndex = 0;
            selectionIndex = 0;
            if (field == null) return false;
            bool gotCursor = TryGetIntMember(field, new[] { "cursorIndex", "cursorPosition", "m_CursorIndex" }, out cursorIndex);
            bool gotSelection = TryGetIntMember(field, new[] { "selectionIndex", "selectIndex", "m_SelectIndex" }, out selectionIndex);
            return gotCursor && gotSelection;
        }

        private void ApplyCodeEditorValue(string newValue, int cursorIndex, int selectionIndex)
        {
            if (_codeEditor == null) return;
            newValue ??= string.Empty;
            _codeEditor.value = newValue;
            int max = string.IsNullOrEmpty(newValue) ? 0 : newValue.Length;
            int clampedCursor = Mathf.Clamp(cursorIndex, 0, max);
            int clampedSelection = Mathf.Clamp(selectionIndex, 0, max);
            SetSelectionIndices(_codeEditor, clampedCursor, clampedSelection);
        }

        private void SetSelectionIndices(TextField field, int cursorIndex, int selectionIndex)
        {
            if (field == null) return;
            var type = field.GetType();
            var method = type.GetMethod("SelectRange", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null)
            {
                method.Invoke(field, new object[] { cursorIndex, selectionIndex });
                return;
            }

            TrySetIntMember(field, new[] { "cursorIndex", "cursorPosition", "m_CursorIndex" }, cursorIndex);
            TrySetIntMember(field, new[] { "selectionIndex", "selectIndex", "m_SelectIndex" }, selectionIndex);
        }

        private static bool TryGetIntMember(object target, string[] names, out int value)
        {
            value = 0;
            if (target == null || names == null) return false;
            var type = target.GetType();
            foreach (var name in names)
            {
                var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (prop != null && prop.PropertyType == typeof(int))
                {
                    value = (int)prop.GetValue(target);
                    return true;
                }

                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (field != null && field.FieldType == typeof(int))
                {
                    value = (int)field.GetValue(target);
                    return true;
                }
            }
            return false;
        }

        private static void TrySetIntMember(object target, string[] names, int value)
        {
            if (target == null || names == null) return;
            var type = target.GetType();
            foreach (var name in names)
            {
                var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (prop != null && prop.PropertyType == typeof(int) && prop.CanWrite)
                {
                    prop.SetValue(target, value);
                    return;
                }

                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (field != null && field.FieldType == typeof(int))
                {
                    field.SetValue(target, value);
                    return;
                }
            }
        }

        private static int FindLineStart(string text, int index)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int start = text.LastIndexOf('\n', Math.Max(0, index - 1));
            return start == -1 ? 0 : start + 1;
        }

        private static string GetLineIndent(string line)
        {
            if (string.IsNullOrEmpty(line)) return string.Empty;
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
            return line.Substring(0, i);
        }

        private static int CountIndentToRemove(string line)
        {
            if (string.IsNullOrEmpty(line)) return 0;
            int remove = 0;
            for (int i = 0; i < line.Length && remove < CodeIndentSpaces; i++)
            {
                if (line[i] == ' ') remove++;
                else if (line[i] == '\t')
                {
                    remove = 1;
                    break;
                }
                else break;
            }
            return remove;
        }

        private bool ApplyCodeEditorEdit(string text, int start, int end, string insert, int newCursor)
        {
            if (_codeEditor == null) return false;
            if (start < 0 || end < start) return false;
            string updated = text.Substring(0, start) + insert + text.Substring(end);
            int clampedCursor = Mathf.Clamp(newCursor, 0, updated.Length);
            ApplyCodeEditorValue(updated, clampedCursor, clampedCursor);
            return true;
        }

        private CodeEditorSnapshot BuildCodeEditorSnapshot(string content)
        {
            int cursorIndex = 0;
            int selectionIndex = 0;
            if (_codeEditor != null && TryGetSelectionIndices(_codeEditor, out int cursor, out int selection))
            {
                cursorIndex = cursor;
                selectionIndex = selection;
            }
            else
            {
                int len = content?.Length ?? 0;
                cursorIndex = len;
                selectionIndex = len;
            }

            int max = content?.Length ?? 0;
            cursorIndex = Mathf.Clamp(cursorIndex, 0, max);
            selectionIndex = Mathf.Clamp(selectionIndex, 0, max);

            return new CodeEditorSnapshot
            {
                Text = content ?? string.Empty,
                CursorIndex = cursorIndex,
                SelectionIndex = selectionIndex
            };
        }

        private void ApplyCodeEditorSnapshot(CodeEditorSnapshot snapshot)
        {
            if (_codeEditor == null) return;
            _codeEditor.SetValueWithoutNotify(snapshot.Text ?? string.Empty);
            SetSelectionIndices(_codeEditor, snapshot.CursorIndex, snapshot.SelectionIndex);
        }

        private void ResetCodeHistory(string content)
        {
            _codeHistory.Clear();
            _codeHistory.Add(BuildCodeEditorSnapshot(content));
            _codeHistoryIndex = _codeHistory.Count - 1;
            UpdateCodeSyntaxHighlight(content);
        }

        private void UndoCodeEdit()
        {
            if (_codeEditor == null || _codeHistoryIndex <= 0) return;
            _suppressCodeHistory = true;
            _codeHistoryIndex--;
            var snapshot = _codeHistory[_codeHistoryIndex];
            ApplyCodeEditorSnapshot(snapshot);
            _suppressCodeHistory = false;
            UpdateCodeSyntaxHighlight(snapshot.Text);
        }

        private void RedoCodeEdit()
        {
            if (_codeEditor == null || _codeHistoryIndex >= _codeHistory.Count - 1) return;
            _suppressCodeHistory = true;
            _codeHistoryIndex++;
            var snapshot = _codeHistory[_codeHistoryIndex];
            ApplyCodeEditorSnapshot(snapshot);
            _suppressCodeHistory = false;
            UpdateCodeSyntaxHighlight(snapshot.Text);
        }

        private void BuildCodeEditorMenu(GenericDropdownMenu menu)
        {
            if (menu == null) return;
            menu.AddItem("Undo", false, UndoCodeEdit);
            menu.AddItem("Redo", false, RedoCodeEdit);
            menu.AddSeparator(string.Empty);
            menu.AddItem("Copy All", false, () =>
            {
                if (_codeEditor == null) return;
                GUIUtility.systemCopyBuffer = _codeEditor.value ?? string.Empty;
            });
            menu.AddItem("Paste Replace", false, () =>
            {
                if (_codeEditor == null) return;
                _codeEditor.value = GUIUtility.systemCopyBuffer ?? string.Empty;
            });
            menu.AddSeparator(string.Empty);
            menu.AddItem("Save", false, () => SaveCodeFile(false));
            menu.AddItem("Build", false, RunCodeBuild);
            menu.AddItem("Build + Run", false, RunCodeBuildAndRun);
        }

        private void BuildCodeFileListMenu(GenericDropdownMenu menu)
        {
            if (menu == null) return;
            menu.AddItem("New File", false, CreateNewCodeFile);
            menu.AddItem("Open File", false, OpenCodeFileDialog);
        }

        private void BuildCodeFileItemMenu(GenericDropdownMenu menu, string path)
        {
            if (menu == null || string.IsNullOrWhiteSpace(path)) return;
            menu.AddItem("Open", false, () => LoadCodeFile(path));
            menu.AddItem("Duplicate", false, () => DuplicateCodeFile(path));
            menu.AddItem("Rename", false, () => RenameCodeFile(path));
            menu.AddItem("Delete", false, () => DeleteCodeFile(path));
        }

        private bool IsCodeEditorFocused()
        {
            if (_root?.panel?.focusController == null || _codeEditor == null) return false;
            var focused = _root.panel.focusController.focusedElement as VisualElement;
            if (focused == null) return false;
            return focused == _codeEditor || _codeEditor.Contains(focused);
        }

        private void CreateNewCodeFile()
        {
            string root = GetCodeRoot(_codeTargetComponentId);
            if (string.IsNullOrWhiteSpace(root)) return;
            Directory.CreateDirectory(root);
            string target;
#if UNITY_EDITOR
            target = UnityEditor.EditorUtility.SaveFilePanel("New Code File", root, "new_file", "ino");
#else
            target = GetUniqueCodeFilePath(root, "new_file", ".ino");
#endif
            if (string.IsNullOrWhiteSpace(target)) return;
            if (!IsCodeFile(target))
            {
                target = target.TrimEnd('.', ' ');
                target += ".ino";
            }
            File.WriteAllText(target, string.Empty);
            LoadCodeFile(target);
        }

        private void RenameCodeFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            string root = Path.GetDirectoryName(path) ?? string.Empty;
            string target;
#if UNITY_EDITOR
            target = UnityEditor.EditorUtility.SaveFilePanel("Rename Code File", root, Path.GetFileNameWithoutExtension(path), Path.GetExtension(path).TrimStart('.'));
#else
            target = GetUniqueCodeFilePath(root, Path.GetFileNameWithoutExtension(path) + "_renamed", Path.GetExtension(path));
#endif
            if (string.IsNullOrWhiteSpace(target) || string.Equals(path, target, StringComparison.OrdinalIgnoreCase)) return;
            File.Move(path, target);
            if (string.Equals(_activeCodePath, path, StringComparison.OrdinalIgnoreCase))
            {
                _activeCodePath = target;
                UpdateCodeFileLabel();
            }
            RefreshCodeFileList();
        }

        private void DuplicateCodeFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            string root = Path.GetDirectoryName(path) ?? string.Empty;
            string target = GetUniqueCodeFilePath(root, Path.GetFileNameWithoutExtension(path) + "_copy", Path.GetExtension(path));
            File.Copy(path, target, true);
            RefreshCodeFileList();
        }

        private void DeleteCodeFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            File.Delete(path);
            if (string.Equals(_activeCodePath, path, StringComparison.OrdinalIgnoreCase))
            {
                _activeCodePath = null;
                if (_codeEditor != null) _codeEditor.SetValueWithoutNotify(string.Empty);
                UpdateCodeFileLabel();
                ResetCodeHistory(string.Empty);
            }
            RefreshCodeFileList();
        }

        private static string GetUniqueCodeFilePath(string root, string baseName, string extension)
        {
            string cleanExt = extension.StartsWith(".") ? extension : "." + extension;
            string candidate = Path.Combine(root, baseName + cleanExt);
            if (!File.Exists(candidate)) return candidate;
            int i = 1;
            while (true)
            {
                candidate = Path.Combine(root, $"{baseName}_{i}{cleanExt}");
                if (!File.Exists(candidate)) return candidate;
                i++;
            }
        }

        private void AttachRootContextMenu(VisualElement element)
        {
            RegisterRightClickMenu(element, menu =>
            {
                menu.AddItem("Save Project", false, SaveCurrentProject);
                menu.AddItem("Open Project", false, OpenProjectDialog);
            });
        }

        private void AttachComponentContextMenu(VisualElement element, ComponentSpec comp)
        {
            RegisterRightClickMenu(element, menu =>
            {
                menu.AddItem("Select", false, () => SelectComponentFromTree(comp));
                menu.AddItem("Delete", false, () => DeleteComponent(comp));
            });
        }

        private void AttachNetContextMenu(VisualElement element, NetSpec net)
        {
            RegisterRightClickMenu(element, menu =>
            {
                menu.AddItem("Delete Net", false, () => DeleteNet(net));
            });
        }

        private void RegisterRightClickMenu(VisualElement element, Action<GenericDropdownMenu> buildMenu)
        {
            if (element == null) return;
            element.RegisterCallback<ContextClickEvent>(evt =>
            {
                if (_root == null) return;
                Vector2 pos = element.LocalToWorld(evt.mousePosition);
                pos = _root.WorldToLocal(pos);
                ShowContextMenu(pos, buildMenu);
                evt.StopPropagation();
            });
            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1 || _root == null) return;
                ShowContextMenu(evt.position, buildMenu);
                evt.StopPropagation();
            });
        }

        private void ShowContextMenu(Vector2 position, Action<GenericDropdownMenu> buildMenu)
        {
            if (_root == null || buildMenu == null) return;
            var menu = new GenericDropdownMenu();
            buildMenu(menu);
            menu.DropDown(new Rect(position, Vector2.zero), _root, DropdownMenuSizeMode.Auto);
        }

        private void DeleteComponent(ComponentSpec comp)
        {
            if (comp == null || _currentCircuit == null) return;
            if (_selectedComponent != null && _selectedComponent.Id == comp.Id)
            {
                DeleteSelectedComponent();
                return;
            }

            _currentCircuit.Components.Remove(comp);
            if (_componentVisuals.TryGetValue(comp.Id, out var visual))
            {
                visual.RemoveFromHierarchy();
                _componentVisuals.Remove(comp.Id);
            }
            RemovePinsForComponent(comp.Id);
            if (_currentCircuit.Nets != null)
            {
                _currentCircuit.Nets.RemoveAll(n => n.Nodes.Any(node => node.StartsWith(comp.Id + ".")));
            }
            _usedPins.Remove(comp.Id);
            UpdateWireLayer();
            PopulateProjectTree();
            UpdateErrorCount();
            RequestBreadboardRebuild();
        }

        private void DeleteNet(NetSpec net)
        {
            if (net == null || _currentCircuit == null || _currentCircuit.Nets == null) return;
            _currentCircuit.Nets.Remove(net);
            UpdateWireLayer();
            PopulateProjectTree();
            UpdateErrorCount();
            RequestBreadboardRebuild();
        }

        private void UpdateErrorCount()
        {
            if (_errorCountLabel == null || _currentCircuit == null) return;
            var issues = CollectDrcIssues();
            int errorCount = issues.Count(i => i.Severity == DrcSeverity.Error);
            int warnCount = issues.Count(i => i.Severity == DrcSeverity.Warning);
            _errorCountLabel.text = warnCount == 0
                ? $"{errorCount} Errors"
                : $"{errorCount} Errors / {warnCount} Warnings";
        }

        private enum DrcSeverity
        {
            Error,
            Warning,
            Info
        }

        private sealed class DrcIssue
        {
            public DrcSeverity Severity;
            public string Code;
            public string Message;
        }

        private List<DrcIssue> CollectDrcIssues()
        {
            var issues = new List<DrcIssue>();
            if (_currentCircuit == null) return issues;
            if (_currentCircuit.Components.Count == 0)
            {
                issues.Add(new DrcIssue { Severity = DrcSeverity.Error, Code = "DRC001", Message = "No components placed." });
                return issues;
            }
            if (_currentCircuit.Components.Count > 1 && (_currentCircuit.Nets == null || _currentCircuit.Nets.Count == 0))
            {
                issues.Add(new DrcIssue { Severity = DrcSeverity.Error, Code = "DRC002", Message = "No nets created." });
                return issues;
            }
            if (_currentCircuit.Nets == null) return issues;

            var componentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var comp in _currentCircuit.Components)
            {
                if (string.IsNullOrWhiteSpace(comp.Id))
                {
                    issues.Add(new DrcIssue { Severity = DrcSeverity.Error, Code = "DRC003", Message = "Component with empty ID." });
                    continue;
                }
                if (!componentIds.Add(comp.Id))
                {
                    issues.Add(new DrcIssue { Severity = DrcSeverity.Error, Code = "DRC004", Message = $"Duplicate component ID '{comp.Id}'." });
                }
                if (string.IsNullOrWhiteSpace(comp.Type))
                {
                    issues.Add(new DrcIssue { Severity = DrcSeverity.Warning, Code = "DRC005", Message = $"{comp.Id} has empty type." });
                }
            }

            var pinLookup = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var componentById = new Dictionary<string, ComponentSpec>(StringComparer.OrdinalIgnoreCase);
            foreach (var comp in _currentCircuit.Components)
            {
                var info = ResolveCatalogItem(comp);
                var pins = info.Pins ?? new List<string>();
                pinLookup[comp.Id] = new HashSet<string>(pins, StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(comp.Id))
                {
                    componentById[comp.Id] = comp;
                }
            }

            var netIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nodeToNet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var net in _currentCircuit.Nets)
            {
                if (net == null) continue;
                string netName = string.IsNullOrWhiteSpace(net.Id) ? "<unnamed>" : net.Id;
                var supplyKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool hasSupplyPin = false;
                bool hasGroundPin = false;
                bool hasArduinoSignal = false;
                if (string.IsNullOrWhiteSpace(net.Id))
                {
                    issues.Add(new DrcIssue { Severity = DrcSeverity.Error, Code = "DRC010", Message = "Net has empty name." });
                }
                else if (!netIds.Add(net.Id))
                {
                    issues.Add(new DrcIssue { Severity = DrcSeverity.Error, Code = "DRC011", Message = $"Duplicate net name '{net.Id}'." });
                }

                if (net.Nodes == null || net.Nodes.Count < 2)
                {
                    issues.Add(new DrcIssue
                    {
                        Severity = DrcSeverity.Warning,
                        Code = "DRC020",
                        Message = $"Net {netName} has insufficient nodes."
                    });
                }
                if (net.Nodes == null) continue;

                var seenNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in net.Nodes)
                {
                    if (string.IsNullOrWhiteSpace(node))
                    {
                        issues.Add(new DrcIssue { Severity = DrcSeverity.Error, Code = "DRC012", Message = $"Net {net.Id} has empty node." });
                        continue;
                    }

                    if (!seenNodes.Add(node))
                    {
                        issues.Add(new DrcIssue { Severity = DrcSeverity.Warning, Code = "DRC013", Message = $"Net {net.Id} has duplicate node {node}." });
                    }

                    if (nodeToNet.TryGetValue(node, out var existingNet) && !string.Equals(existingNet, net.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new DrcIssue
                        {
                            Severity = DrcSeverity.Error,
                            Code = "DRC014",
                            Message = $"Node {node} belongs to multiple nets ({existingNet}, {net.Id})."
                        });
                    }
                    else
                    {
                        nodeToNet[node] = net.Id;
                    }

                    var parts = node.Split('.');
                    if (parts.Length < 2)
                    {
                        issues.Add(new DrcIssue { Severity = DrcSeverity.Error, Code = "DRC015", Message = $"Net {net.Id} has invalid node '{node}'." });
                        continue;
                    }
                    if (_currentCircuit.Components.All(c => c.Id != parts[0]))
                    {
                        issues.Add(new DrcIssue { Severity = DrcSeverity.Error, Code = "DRC016", Message = $"Net {net.Id} references missing component '{parts[0]}'." });
                    }
                    else if (pinLookup.TryGetValue(parts[0], out var pins))
                    {
                        if (pins.Count == 0)
                        {
                            issues.Add(new DrcIssue { Severity = DrcSeverity.Error, Code = "DRC017", Message = $"Net {net.Id} references component {parts[0]} without pins." });
                        }
                        else if (!pins.Contains(parts[1]))
                        {
                            issues.Add(new DrcIssue { Severity = DrcSeverity.Error, Code = "DRC018", Message = $"Net {net.Id} references missing pin {parts[0]}.{parts[1]}." });
                        }
                    }

                    if (componentById.TryGetValue(parts[0], out var comp))
                    {
                        string pinName = parts[1];
                        if (IsGroundPin(pinName) || (IsPowerSourceType(comp.Type) && IsPowerSourceNegativePin(pinName)))
                        {
                            hasGroundPin = true;
                        }

                        if (IsSupplyPin(pinName) || (IsPowerSourceType(comp.Type) && IsPowerSourcePositivePin(pinName)))
                        {
                            hasSupplyPin = true;
                            string kind = GetSupplyKind(pinName, comp.Type);
                            if (!string.IsNullOrWhiteSpace(kind))
                            {
                                supplyKinds.Add(kind);
                            }
                        }

                        if (IsArduinoType(comp.Type) && IsArduinoSignalPin(pinName))
                        {
                            hasArduinoSignal = true;
                        }
                    }
                }

                string supplyFromName = GetSupplyKindFromNetName(net.Id);
                if (!string.IsNullOrWhiteSpace(supplyFromName))
                {
                    if (!hasSupplyPin)
                    {
                        issues.Add(new DrcIssue
                        {
                            Severity = DrcSeverity.Error,
                            Code = "DRC060",
                            Message = $"Power net {netName} has no supply source."
                        });
                    }
                    else if (!supplyKinds.Contains(supplyFromName))
                    {
                        issues.Add(new DrcIssue
                        {
                            Severity = DrcSeverity.Warning,
                            Code = "DRC061",
                            Message = $"Power net {netName} does not match source type {supplyFromName}."
                        });
                    }
                }

                if (IsGroundNetName(net.Id) && !hasGroundPin)
                {
                    issues.Add(new DrcIssue
                    {
                        Severity = DrcSeverity.Warning,
                        Code = "DRC062",
                        Message = $"Ground net {netName} has no ground pin."
                    });
                }

                if (hasSupplyPin && hasGroundPin)
                {
                    issues.Add(new DrcIssue
                    {
                        Severity = DrcSeverity.Error,
                        Code = "DRC063",
                        Message = $"Net {netName} shorts supply to ground."
                    });
                }

                if (supplyKinds.Count > 1)
                {
                    issues.Add(new DrcIssue
                    {
                        Severity = DrcSeverity.Error,
                        Code = "DRC064",
                        Message = $"Net {netName} mixes multiple supplies ({string.Join(", ", supplyKinds)})."
                    });
                }

                if ((net.Nodes?.Count ?? 0) >= 2 && !hasSupplyPin && !hasGroundPin && !hasArduinoSignal)
                {
                    issues.Add(new DrcIssue
                    {
                        Severity = DrcSeverity.Warning,
                        Code = "DRC065",
                        Message = $"Net {netName} may be floating (no source or ground)."
                    });
                }
            }

            foreach (var comp in _currentCircuit.Components)
            {
                var info = ResolveCatalogItem(comp);
                var pins = info.Pins ?? new List<string>();
                if (pins.Count == 0) continue;

                var connectedPins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in nodeToNet.Keys.Where(n => n.StartsWith(comp.Id + ".", StringComparison.OrdinalIgnoreCase)))
                {
                    string pinName = node.Substring(comp.Id.Length + 1);
                    connectedPins.Add(pinName);
                }

                if (connectedPins.Count == 0)
                {
                    issues.Add(new DrcIssue
                    {
                        Severity = DrcSeverity.Warning,
                        Code = "DRC050",
                        Message = $"{comp.Id} has no connected pins."
                    });
                }

                if (IsTwoPinComponent(comp.Type))
                {
                    var pair = GetTwoPinPair(comp.Type);
                    if (!connectedPins.Contains(pair.Item1) || !connectedPins.Contains(pair.Item2))
                    {
                        issues.Add(new DrcIssue
                        {
                            Severity = DrcSeverity.Error,
                            Code = "DRC030",
                            Message = $"{comp.Id} missing connection on {pair.Item1}/{pair.Item2}."
                        });
                    }
                    if (TryGetNetId(nodeToNet, $"{comp.Id}.{pair.Item1}", out var netA) &&
                        TryGetNetId(nodeToNet, $"{comp.Id}.{pair.Item2}", out var netB) &&
                        string.Equals(netA, netB, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new DrcIssue
                        {
                            Severity = IsPowerSourceType(comp.Type) ? DrcSeverity.Error : DrcSeverity.Warning,
                            Code = "DRC031",
                            Message = $"{comp.Id} pins {pair.Item1}/{pair.Item2} are on the same net."
                        });
                    }
                }
                else if (IsArduinoType(comp.Type))
                {
                    bool hasGnd = connectedPins.Any(p => p.StartsWith("GND", StringComparison.OrdinalIgnoreCase));
                    if (!hasGnd)
                    {
                        issues.Add(new DrcIssue { Severity = DrcSeverity.Warning, Code = "DRC040", Message = $"{comp.Id} has no GND pin connected." });
                    }

                    bool hasSupply = connectedPins.Any(p =>
                        string.Equals(p, "5V", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p, "3V3", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p, "IOREF", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p, "VCC", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p, "VIN", StringComparison.OrdinalIgnoreCase));
                    if (!hasSupply)
                    {
                        issues.Add(new DrcIssue { Severity = DrcSeverity.Warning, Code = "DRC041", Message = $"{comp.Id} has no power pin connected." });
                    }
                }
            }

            return issues;
        }

        private void RunDrcCheck()
        {
            var issues = CollectDrcIssues();
            int errorCount = issues.Count(i => i.Severity == DrcSeverity.Error);
            int warnCount = issues.Count(i => i.Severity == DrcSeverity.Warning);
            if (errorCount == 0 && warnCount == 0)
            {
                Debug.Log("[CircuitStudio] DRC PASS");
            }
            else if (errorCount == 0)
            {
                Debug.Log($"[CircuitStudio] DRC PASS ({warnCount} warnings)");
            }
            else
            {
                Debug.Log($"[CircuitStudio] DRC FAIL ({errorCount} errors, {warnCount} warnings)");
            }
            foreach (var issue in issues)
            {
                string prefix = $"[CircuitStudio] {issue.Code} {issue.Message}";
                if (issue.Severity == DrcSeverity.Error)
                {
                    Debug.LogError(prefix);
                }
                else if (issue.Severity == DrcSeverity.Warning)
                {
                    Debug.LogWarning(prefix);
                }
                else
                {
                    Debug.Log(prefix);
                }
            }
            UpdateErrorCount();
        }

        private static bool TryGetNetId(Dictionary<string, string> map, string node, out string netId)
        {
            netId = null;
            if (map == null || string.IsNullOrWhiteSpace(node)) return false;
            return map.TryGetValue(node, out netId);
        }

        private static bool IsTwoPinComponent(string type)
        {
            return string.Equals(type, "Resistor", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "Capacitor", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "LED", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "DCMotor", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "Battery", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "Switch", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "Button", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPowerSourceType(string type)
        {
            return string.Equals(type, "Battery", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPowerSourcePositivePin(string pinName)
        {
            if (string.IsNullOrWhiteSpace(pinName)) return false;
            return pinName == "+" || pinName.Equals("POS", StringComparison.OrdinalIgnoreCase) || pinName.Equals("P", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPowerSourceNegativePin(string pinName)
        {
            if (string.IsNullOrWhiteSpace(pinName)) return false;
            return pinName == "-" || pinName.Equals("NEG", StringComparison.OrdinalIgnoreCase) || pinName.Equals("N", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGroundPin(string pinName)
        {
            if (string.IsNullOrWhiteSpace(pinName)) return false;
            if (pinName.StartsWith("GND", StringComparison.OrdinalIgnoreCase)) return true;
            return pinName.Equals("AGND", StringComparison.OrdinalIgnoreCase) ||
                   pinName.Equals("DGND", StringComparison.OrdinalIgnoreCase) ||
                   pinName.Equals("COM", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSupplyPin(string pinName)
        {
            if (string.IsNullOrWhiteSpace(pinName)) return false;
            if (pinName.Equals("5V", StringComparison.OrdinalIgnoreCase)) return true;
            if (pinName.Equals("3V3", StringComparison.OrdinalIgnoreCase) || pinName.Equals("3.3V", StringComparison.OrdinalIgnoreCase)) return true;
            if (pinName.Equals("VIN", StringComparison.OrdinalIgnoreCase)) return true;
            if (pinName.Equals("VCC", StringComparison.OrdinalIgnoreCase)) return true;
            if (pinName.Equals("IOREF", StringComparison.OrdinalIgnoreCase)) return true;
            if (pinName.Equals("VBUS", StringComparison.OrdinalIgnoreCase) || pinName.Equals("USB", StringComparison.OrdinalIgnoreCase) || pinName.Equals("USB5V", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsArduinoSignalPin(string pinName)
        {
            if (string.IsNullOrWhiteSpace(pinName)) return false;
            if (pinName.Equals("SDA", StringComparison.OrdinalIgnoreCase) || pinName.Equals("SCL", StringComparison.OrdinalIgnoreCase)) return true;
            if (pinName.Equals("RX", StringComparison.OrdinalIgnoreCase) || pinName.Equals("TX", StringComparison.OrdinalIgnoreCase)) return true;
            if ((pinName.StartsWith("D", StringComparison.OrdinalIgnoreCase) || pinName.StartsWith("A", StringComparison.OrdinalIgnoreCase)) && pinName.Length > 1)
            {
                if (int.TryParse(pinName.Substring(1), out _)) return true;
            }
            return false;
        }

        private static string GetSupplyKind(string pinName, string compType)
        {
            if (IsPowerSourceType(compType) && IsPowerSourcePositivePin(pinName)) return "BAT";
            if (pinName == null) return string.Empty;
            if (pinName.Equals("5V", StringComparison.OrdinalIgnoreCase) || pinName.Equals("IOREF", StringComparison.OrdinalIgnoreCase)) return "5V";
            if (pinName.Equals("3V3", StringComparison.OrdinalIgnoreCase) || pinName.Equals("3.3V", StringComparison.OrdinalIgnoreCase)) return "3V3";
            if (pinName.Equals("VIN", StringComparison.OrdinalIgnoreCase)) return "VIN";
            if (pinName.Equals("VCC", StringComparison.OrdinalIgnoreCase)) return "VCC";
            if (pinName.Equals("VBUS", StringComparison.OrdinalIgnoreCase) || pinName.Equals("USB", StringComparison.OrdinalIgnoreCase) || pinName.Equals("USB5V", StringComparison.OrdinalIgnoreCase)) return "VBUS";
            return string.Empty;
        }

        private static string GetSupplyKindFromNetName(string netId)
        {
            if (string.IsNullOrWhiteSpace(netId)) return string.Empty;
            string name = netId.ToUpperInvariant();
            if (name.Contains("USB") || name.Contains("VBUS")) return "VBUS";
            if (name.Contains("5V")) return "5V";
            if (name.Contains("3V3") || name.Contains("3.3")) return "3V3";
            if (name.Contains("VIN")) return "VIN";
            if (name.Contains("VCC")) return "VCC";
            if (name.Contains("BAT")) return "BAT";
            return string.Empty;
        }

        private static bool IsGroundNetName(string netId)
        {
            if (string.IsNullOrWhiteSpace(netId)) return false;
            string name = netId.ToUpperInvariant();
            return name.Contains("GND") || name.Contains("GROUND");
        }

        private static Tuple<string, string> GetTwoPinPair(string type)
        {
            if (string.Equals(type, "LED", StringComparison.OrdinalIgnoreCase))
            {
                return Tuple.Create("Anode", "Cathode");
            }
            if (string.Equals(type, "Battery", StringComparison.OrdinalIgnoreCase))
            {
                return Tuple.Create("+", "-");
            }
            return Tuple.Create("A", "B");
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
            info.AddToClassList("lib-item-info");
            var title = new Label(item.Name);
            title.AddToClassList("lib-item-title");
            var sub = new Label(item.Description);
            sub.AddToClassList("lib-item-sub");
            info.Add(title);
            info.Add(sub);
            row.Add(info);

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1) return;
                ShowContextMenu(evt.position, menu =>
                {
                    menu.AddItem("Add To Canvas", false, () =>
                    {
                        Vector2 pos = GetCanvasCenter();
                        InstantiateComponentOnCanvas(item, pos);
                    });
                });
                evt.StopPropagation();
            });

            return row;
        }

        private Vector2 GetCanvasCenter()
        {
            if (_boardView != null)
            {
                var rect = _boardView.layout;
                if (rect.width > 0f && rect.height > 0f)
                {
                    return new Vector2(rect.width * 0.5f, rect.height * 0.5f);
                }
            }
            if (_canvasView == null) return new Vector2(100f, 100f);
            var canvasRect = _canvasView.contentRect;
            return CanvasToBoard(new Vector2(canvasRect.width * 0.5f, canvasRect.height * 0.5f));
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
                var localPos = _canvasView.WorldToLocal(evt.position);
                InstantiateComponentOnCanvas(_dragItem, CanvasToBoard(localPos));
            }
            _dragItem = default;
        }

        // --- Phase 3: Canvas Logic ---
        private ComponentSpec InstantiateComponentOnCanvas(ComponentCatalog.Item item, Vector2 position)
        {
            if (string.IsNullOrWhiteSpace(item.Type)) return null;
            if (_currentCircuit == null) return null;
            if (_currentCircuit.Components == null) _currentCircuit.Components = new List<ComponentSpec>();
            // Grid Snapping (Task 16: 10mm implicit grid)
            // Assuming 1 unit = 1mm for now, or just pixel snapping
            position.x = Mathf.Round(position.x / GridSnap) * GridSnap;
            position.y = Mathf.Round(position.y / GridSnap) * GridSnap;
            var size = GetComponentSize(item);
            if (_constrainComponentsToBoard)
            {
                position = ClampToViewport(position, size);
                position = ClampToBoard(position, size);
            }
            position = ResolvePlacement(position, size, string.Empty);

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
            RequestBreadboardRebuild();
            return spec;
        }

        private void CreateComponentVisuals(ComponentSpec spec, ComponentCatalog.Item catalogItem, Vector2 pos)
        {
            EnsureCanvasDecorations();
            // Task 13: visual component using .prop-card-header style logic broadly
            var el = new VisualElement();
            el.userData = spec; // Link back to data
            el.AddToClassList("circuit-component");
            el.style.position = Position.Absolute;
            var size = GetComponentSize(catalogItem);
            var clampedPos = pos;
            if (_constrainComponentsToBoard)
            {
                clampedPos = ClampToViewport(clampedPos, size);
                clampedPos = ClampToBoard(clampedPos, size);
            }
            clampedPos = ResolvePlacement(clampedPos, size, spec.Id);
            el.style.left = clampedPos.x;
            el.style.top = clampedPos.y;
            el.style.width = size.x;
            el.style.height = size.y;
            SetComponentPosition(spec, clampedPos);

            BuildComponentVisual(el, spec, catalogItem);

            // Selection Logic
            el.RegisterCallback<PointerDownEvent>(e =>
            {
                bool handled = false;
                if (e.button == 0)
                {
                    if (_currentTool == ToolMode.Move)
                    {
                        BeginComponentMove(el, spec, catalogItem, e);
                        handled = true;
                    }
                    else
                    {
                    if (_currentTool == ToolMode.Wire)
                    {
                        HandleWireClick(spec, catalogItem);
                    }
                    else
                    {
                        SelectComponent(el, spec, catalogItem);
                    }
                        handled = true;
                    }
                }
                else if (e.button == 1)
                {
                    ShowContextMenu(e.position, menu =>
                    {
                        menu.AddItem("Select", false, () => SelectComponent(el, spec, catalogItem));
                        menu.AddItem("Delete", false, () => DeleteComponent(spec));
                    });
                    handled = true;
                }
                if (handled)
                {
                    e.StopPropagation();
                }
            });

            // Move Logic (Task 17)
            el.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_isMovingComponent || e.pointerId != _movePointerId || _moveTarget != el)
                {
                    return;
                }
                if (_canvasView == null) return;

                var canvasLocal = _canvasView.WorldToLocal(e.position);
                var p = CanvasToBoard(canvasLocal) - _moveOffset;
                p.x = Mathf.Round(p.x / GridSnap) * GridSnap;
                p.y = Mathf.Round(p.y / GridSnap) * GridSnap;
                if (_constrainComponentsToBoard)
                {
                    p = ClampToViewport(p, size);
                    p = ClampToBoard(p, size);
                }
                el.style.left = p.x;
                el.style.top = p.y;
                SetComponentPosition(spec, p);
                UpdateTransformFields(p);
                RequestWireUpdateThrottled();
            });

            el.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_isMovingComponent && e.pointerId == _movePointerId && _moveTarget == el)
                {
                    EndComponentMove(el, e);
                }
            });

            if (_boardView != null)
            {
                _boardView.Add(el);
            }
            _componentVisuals[spec.Id] = el;
            BringCanvasHudToFront();
        }

        private void BeginComponentMove(VisualElement element, ComponentSpec spec, ComponentCatalog.Item catalogItem, PointerDownEvent evt)
        {
            if (_canvasView == null || element == null || spec == null) return;
            SelectComponent(element, spec, catalogItem);
            _isMovingComponent = true;
            _movePointerId = evt.pointerId;
            _moveTarget = element;
            _moveTargetSpec = spec;
            var canvasLocal = _canvasView.WorldToLocal(evt.position);
            var boardPos = CanvasToBoard(canvasLocal);
            _moveOffset = boardPos - GetComponentPosition(spec.Id);
            element.CapturePointer(_movePointerId);
            evt.StopPropagation();
        }

        private void EndComponentMove(VisualElement element, PointerUpEvent evt)
        {
            _isMovingComponent = false;
            if (element != null && element.HasPointerCapture(_movePointerId))
            {
                element.ReleasePointer(_movePointerId);
            }
            _movePointerId = -1;
            _moveTarget = null;
            _moveTargetSpec = null;
            if (element != null && _selectedComponent != null)
            {
                var size = GetComponentSize(string.IsNullOrWhiteSpace(_selectedComponent.Type) ? string.Empty : _selectedComponent.Type);
                var current = GetComponentPosition(_selectedComponent.Id);
                var resolved = ResolvePlacement(current, size, _selectedComponent.Id);
                if (resolved != current)
                {
                    element.style.left = resolved.x;
                    element.style.top = resolved.y;
                    SetComponentPosition(_selectedComponent, resolved);
                }
            }
            UpdateWireLayer();
            evt.StopPropagation();
        }

        private void RequestWireUpdateThrottled()
        {
            _wireUpdatePending = true;
            _wireUpdateAt = Time.unscaledTime + WireUpdateInterval;
        }

        private Vector2 ResolvePlacement(Vector2 pos, Vector2 size, string ignoreId)
        {
            pos.x = Mathf.Round(pos.x / GridSnap) * GridSnap;
            pos.y = Mathf.Round(pos.y / GridSnap) * GridSnap;
            if (!IsOverlapping(pos, size, ignoreId))
            {
                return pos;
            }

            int maxRadius = 20;
            for (int radius = 1; radius <= maxRadius; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Mathf.Abs(dx) != radius && Mathf.Abs(dy) != radius) continue;
                        var candidate = new Vector2(pos.x + dx * GridSnap, pos.y + dy * GridSnap);
                        if (_constrainComponentsToBoard)
                        {
                            candidate = ClampToBoard(candidate, size);
                        }
                        if (!IsOverlapping(candidate, size, ignoreId))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return pos;
        }

        private bool IsOverlapping(Vector2 pos, Vector2 size, string ignoreId)
        {
            var rect = new Rect(pos.x, pos.y, size.x, size.y);
            foreach (var kvp in _componentVisuals)
            {
                var compId = kvp.Key;
                if (string.Equals(compId, ignoreId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!_componentPositions.TryGetValue(compId, out var otherPos))
                {
                    otherPos = GetComponentPosition(compId);
                }
                var spec = _currentCircuit.Components.FirstOrDefault(c => c.Id == compId);
                var otherSize = GetComponentSize(string.IsNullOrWhiteSpace(spec?.Type) ? string.Empty : spec.Type);
                var otherRect = new Rect(otherPos.x, otherPos.y, otherSize.x, otherSize.y);
                if (rect.Overlaps(otherRect))
                {
                    return true;
                }
            }
            return false;
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
                case "TextNote":
                    BuildTextNoteComponentVisual(root, spec);
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
            switch (kind)
            {
                case SymbolKind.Resistor:
                    root.AddToClassList("component-resistor");
                    break;
                case SymbolKind.Capacitor:
                    root.AddToClassList("component-capacitor");
                    break;
                case SymbolKind.Led:
                    root.AddToClassList("component-led");
                    break;
                case SymbolKind.Motor:
                    root.AddToClassList("component-motor");
                    break;
                case SymbolKind.Battery:
                    root.AddToClassList("component-battery");
                    break;
            }

            var row = new VisualElement();
            row.AddToClassList("block-row");

            var pinNames = item.Pins != null && item.Pins.Count >= 2
                ? item.Pins
                : new List<string> { "A", "B" };

            var leftGroup = new VisualElement();
            leftGroup.AddToClassList("block-pin");
            leftGroup.AddToClassList("block-pin-left");
            var leftLabel = new Label(pinNames[0]);
            leftLabel.AddToClassList("block-pin-label");
            leftLabel.AddToClassList("left");
            var leftPin = CreatePinDot(spec.Id, pinNames[0]);
            leftPin.AddToClassList("block-pin-dot");
            leftGroup.Add(leftLabel);
            leftGroup.Add(leftPin);

            var body = new VisualElement();
            body.AddToClassList("block-body");
            var symbol = new SchematicSymbolElement(kind);
            symbol.AddToClassList("symbol-element");
            body.Add(symbol);
            var title = new Label(spec.Id);
            title.AddToClassList("block-title");
            var subtitle = new Label(item.Name);
            subtitle.AddToClassList("block-subtitle");
            body.Add(title);
            body.Add(subtitle);

            var rightGroup = new VisualElement();
            rightGroup.AddToClassList("block-pin");
            rightGroup.AddToClassList("block-pin-right");
            var rightPin = CreatePinDot(spec.Id, pinNames[1]);
            rightPin.AddToClassList("block-pin-dot");
            var rightLabel = new Label(pinNames[1]);
            rightLabel.AddToClassList("block-pin-label");
            rightLabel.AddToClassList("right");
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

        private void BuildTextNoteComponentVisual(VisualElement root, ComponentSpec spec)
        {
            root.AddToClassList("text-note");
            var note = new TextField
            {
                multiline = true,
                value = spec.Properties != null && spec.Properties.TryGetValue("text", out var text)
                    ? text
                    : "Text"
            };
            note.AddToClassList("text-note-field");
            note.RegisterValueChangedCallback(evt =>
            {
                if (spec.Properties == null) spec.Properties = new Dictionary<string, string>();
                spec.Properties["text"] = evt.newValue ?? string.Empty;
            });
            root.Add(note);
        }

        private VisualElement BuildArduinoPinColumn(ComponentSpec spec, IReadOnlyList<string> pins, bool isLeft)
        {
            var column = new VisualElement();
            column.AddToClassList("arduino-pin-column");
            column.AddToClassList(isLeft ? "arduino-pin-column-left" : "arduino-pin-column-right");
            column.AddToClassList(isLeft ? "left" : "right");

            foreach (var pin in pins)
            {
                var row = new VisualElement();
                row.AddToClassList("arduino-pin-row");
                row.AddToClassList(isLeft ? "left" : "right");

                var label = new Label(GetPinDisplayName(pin));
                label.AddToClassList("arduino-pin-label");
                label.AddToClassList(isLeft ? "left" : "right");

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
            if (catalogInfo.Pins == null || catalogInfo.Pins.Count == 0)
            {
                return;
            }
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
            else if (item.Type == "TextNote")
            {
                SetDefaultIfMissing(spec, "text", "Text");
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
                case "TextNote": return new Vector2(200f, 80f);
                default: return new Vector2(ComponentWidth, ComponentHeight);
            }
        }

        private void OnCanvasClick(PointerDownEvent evt)
        {
            if (evt.button != 0 || _canvasView == null) return;
            // Click on empty canvas -> deselect
            if (IsCanvasBackgroundHit(evt.position, evt.target as VisualElement))
            {
                if (_currentTool == ToolMode.Text)
                {
                    var local = _canvasView.WorldToLocal(evt.position);
                    CreateTextNote(CanvasToBoard(local));
                    SetTool(ToolMode.Select);
                    evt.StopPropagation();
                    return;
                }
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

        private void OnCanvasRightClick(PointerDownEvent evt)
        {
            if (evt.button != 1 || _canvasView == null || _root == null) return;
            if (!IsCanvasBackgroundHit(evt.position, evt.target as VisualElement)) return;
            ShowContextMenu(evt.position, menu =>
            {
                menu.AddItem("Add Text", false, () =>
                {
                    var local = _canvasView.WorldToLocal(evt.position);
                    CreateTextNote(CanvasToBoard(local));
                });
                menu.AddSeparator(string.Empty);
                menu.AddItem("Center Selection", false, CenterCanvasOnSelection);
                menu.AddItem("Reset View", false, ResetCanvasView);
                menu.AddSeparator(string.Empty);
                menu.AddItem("Zoom In", false, () => ZoomCanvasStep(0.9f, GetCanvasViewCenterWorld()));
                menu.AddItem("Zoom Out", false, () => ZoomCanvasStep(1.1f, GetCanvasViewCenterWorld()));
                menu.AddSeparator(string.Empty);
                menu.AddItem("Run DRC", false, RunDrcCheck);
                menu.AddItem("Save Project", false, SaveCurrentProject);
            });
            evt.StopPropagation();
        }

        private void OnCanvasContextClick(ContextClickEvent evt)
        {
            if (_canvasView == null || _root == null) return;
            Vector2 worldPos = _canvasView.LocalToWorld(evt.mousePosition);
            if (!IsCanvasBackgroundHit(worldPos, evt.target as VisualElement)) return;
            Vector2 rootPos = _root.WorldToLocal(worldPos);
            ShowContextMenu(rootPos, menu =>
            {
                menu.AddItem("Add Text", false, () =>
                {
                    var local = _canvasView.WorldToLocal(worldPos);
                    CreateTextNote(CanvasToBoard(local));
                });
                menu.AddSeparator(string.Empty);
                menu.AddItem("Center Selection", false, CenterCanvasOnSelection);
                menu.AddItem("Reset View", false, ResetCanvasView);
                menu.AddSeparator(string.Empty);
                menu.AddItem("Zoom In", false, () => ZoomCanvasStep(0.9f, GetCanvasViewCenterWorld()));
                menu.AddItem("Zoom Out", false, () => ZoomCanvasStep(1.1f, GetCanvasViewCenterWorld()));
                menu.AddSeparator(string.Empty);
                menu.AddItem("Run DRC", false, RunDrcCheck);
                menu.AddItem("Save Project", false, SaveCurrentProject);
            });
            evt.StopPropagation();
        }

        private bool IsCanvasBackgroundHit(Vector2 position, VisualElement target)
        {
            if (_canvasView == null) return false;
            if (!_canvasView.worldBound.Contains(position)) return false;
            var current = target;
            while (current != null && current != _canvasView)
            {
                if (current.ClassListContains("circuit-component") || current.ClassListContains("canvas-text-note"))
                {
                    return false;
                }
                current = current.parent;
            }
            return true;
        }

        private void CreateTextNote(Vector2 position)
        {
            if (_currentCircuit == null) return;
            var item = CreateTextNoteItem();
            var spec = InstantiateComponentOnCanvas(item, position);
            if (spec != null)
            {
                SetDefaultIfMissing(spec, "text", "Text");
            }
        }

        private static ComponentCatalog.Item CreateTextNoteItem()
        {
            return new ComponentCatalog.Item
            {
                Id = "text-note",
                Name = "Text Note",
                Description = "Annotation",
                Type = "TextNote",
                Symbol = "T",
                IconChar = "T",
                ElectricalSpecs = new Dictionary<string, string>(),
                DefaultProperties = new Dictionary<string, string>(),
                Pins = new List<string>(),
                Order = 999
            };
        }

        private void OnCanvasPanStart(PointerDownEvent evt)
        {
            if (_canvasView == null) return;
            if (evt.button == 2 || (evt.button == 0 && evt.ctrlKey))
            {
                if (!_canvasView.worldBound.Contains(evt.position)) return;
                _isPanningCanvas = true;
                _panPointerId = evt.pointerId;
                _panStart = (Vector2)evt.position;
                _panOrigin = _canvasPan;
                _canvasView.CapturePointer(_panPointerId);
                evt.StopPropagation();
            }
        }

        private void OnCanvasPanMove(PointerMoveEvent evt)
        {
            if (!_isPanningCanvas || _panPointerId != evt.pointerId) return;
            Vector2 delta = (Vector2)evt.position - _panStart;
            _canvasPan = _panOrigin + delta;
            ApplyCanvasTransform();
            evt.StopPropagation();
        }

        private void OnCanvasPanEnd(PointerUpEvent evt)
        {
            if (!_isPanningCanvas || _panPointerId != evt.pointerId) return;
            _isPanningCanvas = false;
            if (_canvasView != null && _canvasView.HasPointerCapture(_panPointerId))
            {
                _canvasView.ReleasePointer(_panPointerId);
            }
            _panPointerId = -1;
            evt.StopPropagation();
        }

        private void OnCanvasPointerMove(PointerMoveEvent evt)
        {
            if (_canvasView == null) return;
            if (!_canvasView.worldBound.Contains(evt.position)) return;
            _lastCanvasWorldPos = evt.position;
            var canvasLocal = _canvasView.WorldToLocal(evt.position);
            var boardPos = CanvasToBoard(canvasLocal);
            UpdateCanvasHud(boardPos);
        }

        private void OnCanvasWheel(WheelEvent evt)
        {
            if (_canvasView == null) return;
            Vector2 worldPos = _canvasView.LocalToWorld(evt.mousePosition);
            if (!_canvasView.worldBound.Contains(worldPos)) return;
            float factor = evt.delta.y > 0 ? 1.08f : 0.92f;
            Vector2 pivot = factor < 1f ? GetCanvasViewCenterWorld() : worldPos;
            ZoomCanvasStep(factor, pivot);
            evt.StopPropagation();
        }

        private void OnCanvasKeyDown(KeyDownEvent evt)
        {
            if (_canvasView == null || !evt.ctrlKey) return;
            if (evt.keyCode == KeyCode.UpArrow)
            {
                ZoomCanvasStep(0.92f, GetCanvasViewCenterWorld());
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.DownArrow)
            {
                ZoomCanvasStep(1.08f, GetZoomPivot());
                evt.StopPropagation();
            }
        }

        private Vector2 GetZoomPivot()
        {
            if (_canvasView == null) return Vector2.zero;
            if (_canvasView.worldBound.Contains(_lastCanvasWorldPos))
            {
                return _lastCanvasWorldPos;
            }
            var center = new Vector2(_canvasView.worldBound.center.x, _canvasView.worldBound.center.y);
            return center;
        }

        private Vector2 GetCanvasViewCenterWorld()
        {
            if (_canvasView == null) return Vector2.zero;
            return _canvasView.worldBound.center;
        }

        private void ZoomCanvasStep(float factor, Vector2 worldPivot)
        {
            if (float.IsNaN(_canvasZoom) || float.IsInfinity(_canvasZoom) || _canvasZoom <= 0f)
            {
                _canvasZoom = 1f;
                _canvasPan = Vector2.zero;
            }
            float newZoom = Mathf.Clamp(_canvasZoom * factor, MinCanvasZoom, MaxCanvasZoom);
            if (Mathf.Approximately(newZoom, _canvasZoom)) return;

            Vector2 canvasLocal = _canvasView != null ? _canvasView.WorldToLocal(worldPivot) : worldPivot;
            Vector2 origin = GetBoardOrigin();
            Vector2 localBefore = (canvasLocal - origin - _canvasPan) / _canvasZoom;
            _canvasZoom = newZoom;
            Vector2 viewCenter = _canvasView != null
                ? new Vector2(_canvasView.contentRect.width * 0.5f, _canvasView.contentRect.height * 0.5f)
                : canvasLocal;
            _canvasPan = viewCenter - origin - localBefore * _canvasZoom;
            ApplyCanvasTransform();
        }

        private void ApplyCanvasTransform()
        {
            if (_boardView == null) return;
            if (float.IsNaN(_canvasZoom) || float.IsInfinity(_canvasZoom) || _canvasZoom <= 0f)
            {
                _canvasZoom = 1f;
                _canvasPan = Vector2.zero;
            }
            if (float.IsNaN(_canvasPan.x) || float.IsInfinity(_canvasPan.x) ||
                float.IsNaN(_canvasPan.y) || float.IsInfinity(_canvasPan.y))
            {
                _canvasPan = Vector2.zero;
            }
            ClampCanvasPan();
            _boardView.style.translate = new Translate(_canvasPan.x, _canvasPan.y, 0f);
            _boardView.style.scale = new Scale(new Vector3(_canvasZoom, _canvasZoom, 1f));
            _gridLayer?.SetTransform(_canvasPan, _canvasZoom, GetBoardOrigin());
            UpdateCanvasHud(null);
        }

        private void UpdateCanvasHud(Vector2? boardPosition)
        {
            float safeZoom = _canvasZoom;
            if (float.IsNaN(safeZoom) || float.IsInfinity(safeZoom) || safeZoom <= 0f)
            {
                safeZoom = 1f;
            }
            string text;
            if (boardPosition.HasValue)
            {
                var pos = BoardToDisplay(boardPosition.Value);
                text = $"X: {pos.x:0}mm  Y: {pos.y:0}mm  Scale: {Mathf.RoundToInt(safeZoom * 100f)}%";
            }
            else
            {
                text = $"X: 0mm  Y: 0mm  Scale: {Mathf.RoundToInt(safeZoom * 100f)}%";
            }

            if (_centerStatusLabel != null)
            {
                _centerStatusLabel.text = text;
                return;
            }

            if (_canvasHudLabel != null)
            {
                _canvasHudLabel.text = text;
            }
        }

        private void ResetCanvasView()
        {
            if (_canvasView == null || _boardView == null || _currentCircuit == null || _currentCircuit.Components == null || _currentCircuit.Components.Count == 0)
            {
                _canvasZoom = 1f;
                _canvasPan = Vector2.zero;
                ApplyCanvasTransform();
                return;
            }

            var bounds = GetComponentsBounds();
            var viewRect = _canvasView.contentRect;
            if (viewRect.width <= 0f || viewRect.height <= 0f)
            {
                _canvasZoom = 1f;
                _canvasPan = Vector2.zero;
                ApplyCanvasTransform();
                return;
            }

            float padding = 120f;
            bounds.xMin -= padding;
            bounds.yMin -= padding;
            bounds.xMax += padding;
            bounds.yMax += padding;

            float zoomX = viewRect.width / Mathf.Max(1f, bounds.width);
            float zoomY = viewRect.height / Mathf.Max(1f, bounds.height);
            _canvasZoom = Mathf.Clamp(Mathf.Min(zoomX, zoomY), MinCanvasZoom, MaxCanvasZoom);

            Vector2 viewCenter = new Vector2(viewRect.width * 0.5f, viewRect.height * 0.5f);
            Vector2 targetCenter = new Vector2(bounds.center.x, bounds.center.y);
            _canvasPan = viewCenter - (targetCenter * _canvasZoom) - GetBoardOrigin();
            ApplyCanvasTransform();
        }

        private void CenterCanvasOnSelection()
        {
            if (_canvasView == null || _selectedComponent == null) return;
            var item = ResolveCatalogItem(_selectedComponent);
            var size = GetComponentSize(item);
            Vector2 pos = GetComponentPosition(_selectedComponent.Id);
            Vector2 target = pos + (size * 0.5f);
            Vector2 viewCenter = new Vector2(_canvasView.contentRect.width * 0.5f, _canvasView.contentRect.height * 0.5f);
            _canvasPan = viewCenter - (target * _canvasZoom) - GetBoardOrigin();
            ApplyCanvasTransform();
        }

        private void CenterAndZoomOnSelection()
        {
            if (_canvasView == null || _selectedComponent == null) return;
            var item = ResolveCatalogItem(_selectedComponent);
            var size = GetComponentSize(item);
            var pos = GetComponentPosition(_selectedComponent.Id);
            var bounds = new Rect(pos, size);
            float padding = 120f;
            bounds.xMin -= padding;
            bounds.yMin -= padding;
            bounds.xMax += padding;
            bounds.yMax += padding;

            var viewRect = _canvasView.contentRect;
            if (viewRect.width <= 0f || viewRect.height <= 0f) return;

            float zoomX = viewRect.width / Mathf.Max(1f, bounds.width);
            float zoomY = viewRect.height / Mathf.Max(1f, bounds.height);
            _canvasZoom = Mathf.Clamp(Mathf.Min(zoomX, zoomY), MinCanvasZoom, MaxCanvasZoom);

            Vector2 viewCenter = new Vector2(viewRect.width * 0.5f, viewRect.height * 0.5f);
            _canvasPan = viewCenter - (bounds.center * _canvasZoom) - GetBoardOrigin();
            ApplyCanvasTransform();
        }

        private Vector2 CanvasToBoard(Vector2 canvasLocal)
        {
            if (_boardView == null) return canvasLocal;
            return (canvasLocal - GetBoardOrigin() - _canvasPan) / _canvasZoom;
        }

        private Vector2 GetBoardOrigin()
        {
            if (_boardView == null) return Vector2.zero;
            return new Vector2(_boardView.layout.x, _boardView.layout.y);
        }

        private Vector2 GetBoardCenter()
        {
            if (_boardView == null) return Vector2.zero;
            var rect = _boardView.layout;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return new Vector2(BoardWorldWidth * 0.5f, BoardWorldHeight * 0.5f);
            }
            return new Vector2(rect.width * 0.5f, rect.height * 0.5f);
        }

        private Vector2 BoardToDisplay(Vector2 boardPos)
        {
            return boardPos - GetBoardCenter();
        }

        private static string FormatDisplayMm(float value)
        {
            return $"{value:0.##} mm";
        }

        private void ClampCanvasPan()
        {
            if (_canvasView == null || _boardView == null) return;
            var view = _canvasView.contentRect;
            if (view.width <= 0f || view.height <= 0f) return;
            var board = _boardView.layout;
            if (board.width <= 0f || board.height <= 0f) return;

            float boardWidth = board.width * _canvasZoom;
            float boardHeight = board.height * _canvasZoom;
            var origin = GetBoardOrigin();

            if (boardWidth <= view.width)
            {
                _canvasPan.x = (view.width - boardWidth) * 0.5f - origin.x;
            }
            else
            {
                float minX = view.width - (origin.x + boardWidth);
                float maxX = -origin.x;
                _canvasPan.x = Mathf.Clamp(_canvasPan.x, minX, maxX);
            }

            if (boardHeight <= view.height)
            {
                _canvasPan.y = (view.height - boardHeight) * 0.5f - origin.y;
            }
            else
            {
                float minY = view.height - (origin.y + boardHeight);
                float maxY = -origin.y;
                _canvasPan.y = Mathf.Clamp(_canvasPan.y, minY, maxY);
            }
        }

        private Rect GetComponentsBounds()
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            foreach (var comp in _currentCircuit.Components)
            {
                var size = GetComponentSize(string.IsNullOrWhiteSpace(comp.Type) ? string.Empty : comp.Type);
                var pos = GetComponentPosition(comp.Id);
                minX = Mathf.Min(minX, pos.x);
                minY = Mathf.Min(minY, pos.y);
                maxX = Mathf.Max(maxX, pos.x + size.x);
                maxY = Mathf.Max(maxY, pos.y + size.y);
            }

            if (minX == float.MaxValue) return new Rect(0f, 0f, 0f, 0f);
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
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
            var inputX = _propertiesPanel.Q<TextField>("TransformPosXField");
            var inputY = _propertiesPanel.Q<TextField>("TransformPosYField");
            if (inputX == null || inputY == null)
            {
                var inputs = _propertiesPanel.Query<TextField>(className: "input-dark").ToList();
                if (inputs.Count >= 2)
                {
                    inputX = inputs[0];
                    inputY = inputs[1];
                }
            }
            if (inputX != null && inputY != null)
            {
                _transformInputX = inputX;
                _transformInputY = inputY;

                var initialDisplay = BoardToDisplay(GetComponentPosition(spec.Id));
                inputX.SetValueWithoutNotify(FormatDisplayMm(initialDisplay.x));
                inputY.SetValueWithoutNotify(FormatDisplayMm(initialDisplay.y));

                // Bi-directional binding
                if (inputX.userData == null)
                {
                    inputX.userData = "bound";
                    inputX.RegisterValueChangedCallback(e =>
                    {
                        if (_selectedComponent != spec) return;
                        if (float.TryParse(e.newValue.Replace("mm", "").Trim(), out float v))
                        {
                            var size = GetComponentSize(info);
                            var boardCenter = GetBoardCenter();
                            var p = ClampToBoard(new Vector2(v + boardCenter.x, visual.style.top.value.value), size);
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
                        if (_selectedComponent != spec) return;
                        if (float.TryParse(e.newValue.Replace("mm", "").Trim(), out float v))
                        {
                            var size = GetComponentSize(info);
                            var boardCenter = GetBoardCenter();
                            var p = ClampToBoard(new Vector2(visual.style.left.value.value, v + boardCenter.y), size);
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

                if (IsSwitchType(spec.Type))
                {
                    AddSwitchStateRow(container, spec);
                }

                // Add dynamic specs
                foreach (var kvp in info.ElectricalSpecs)
                {
                    if (IsSwitchType(spec.Type) && string.Equals(kvp.Key, "State", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
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

                    var netField = new Label(netName);
                    netField.AddToClassList("pin-net-readonly");
                    netField.AddToClassList(string.Equals(netName, "UNCONNECTED", StringComparison.OrdinalIgnoreCase)
                        ? "pin-net-unconnected"
                        : "pin-net-connected");

                    var fnLabel = new Label(displayPin == "GND" ? "GND" : "I/O");
                    row.Add(pinLabel);
                    row.Add(netField);
                    row.Add(fnLabel);
                    pinTable.Add(row);
                }
            }
        }

        private static bool IsSwitchType(string type)
        {
            return string.Equals(type, "Switch", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "Button", StringComparison.OrdinalIgnoreCase);
        }

        private void AddSwitchStateRow(VisualElement container, ComponentSpec spec)
        {
            if (container == null || spec == null) return;
            EnsureComponentProperties(spec);

            var row = new VisualElement();
            row.AddToClassList("prop-row");

            var label = new Label("State");
            label.AddToClassList("prop-label-row");

            var dropdown = new DropdownField();
            dropdown.AddToClassList("input-dark");
            dropdown.AddToClassList("input-right");
            dropdown.AddToClassList("switch-state-dropdown");
            dropdown.choices = new List<string> { "Open", "Closed" };

            string state = "open";
            if (spec.Properties.TryGetValue("state", out var rawState) && !string.IsNullOrWhiteSpace(rawState))
            {
                state = NormalizeSwitchState(rawState);
            }
            dropdown.SetValueWithoutNotify(state == "closed" ? "Closed" : "Open");

            dropdown.RegisterValueChangedCallback(evt =>
            {
                if (_selectedComponent != spec) return;
                string next = evt.newValue;
                spec.Properties["state"] = string.Equals(next, "Closed", StringComparison.OrdinalIgnoreCase)
                    ? "closed"
                    : "open";
            });

            row.Add(label);
            row.Add(dropdown);
            container.Add(row);
        }

        private static string NormalizeSwitchState(string value)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0) return "open";
            string normalized = trimmed.ToLowerInvariant();
            if (normalized == "true" || normalized == "1" || normalized == "on" || normalized == "closed" || normalized == "pressed")
            {
                return "closed";
            }
            if (normalized == "false" || normalized == "0" || normalized == "off" || normalized == "open")
            {
                return "open";
            }
            return trimmed;
        }

        private void UpdateTransformFields(Vector2 pos)
        {
            if (_propertiesPanel == null) return;
            pos = ClampToBoard(pos, _selectedComponentSize);
            var displayPos = BoardToDisplay(pos);
            if (_centerStatusLabel != null)
            {
                _centerStatusLabel.text = $"X:{Mathf.RoundToInt(displayPos.x)} Y:{Mathf.RoundToInt(displayPos.y)}";
            }
            if (_transformInputX != null && _transformInputY != null)
            {
                _transformInputX.SetValueWithoutNotify(FormatDisplayMm(displayPos.x));
                _transformInputY.SetValueWithoutNotify(FormatDisplayMm(displayPos.y));
                return;
            }
            _transformInputX = _propertiesPanel.Q<TextField>("TransformPosXField");
            _transformInputY = _propertiesPanel.Q<TextField>("TransformPosYField");
            if (_transformInputX == null || _transformInputY == null)
            {
                var inputs = _propertiesPanel.Query<TextField>(className: "input-dark").ToList();
                if (inputs.Count >= 2)
                {
                    _transformInputX = inputs[0];
                    _transformInputY = inputs[1];
                }
            }
            if (_transformInputX != null && _transformInputY != null)
            {
                _transformInputX.SetValueWithoutNotify(FormatDisplayMm(displayPos.x));
                _transformInputY.SetValueWithoutNotify(FormatDisplayMm(displayPos.y));
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
            _btnText?.RemoveFromClassList("active");

            switch (tool)
            {
                case ToolMode.Select: _btnSelect?.AddToClassList("active"); break;
                case ToolMode.Move: _btnMove?.AddToClassList("active"); break;
                case ToolMode.Wire: _btnWire?.AddToClassList("active"); break;
                case ToolMode.Text: _btnText?.AddToClassList("active"); break;
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            bool targetIsText = evt.target is TextField || evt.target is TextElement;
            bool codeContextActive = _centerMode == CenterPanelMode.Code;
            if (evt.ctrlKey && evt.shiftKey && evt.keyCode == KeyCode.S)
            {
                if (codeContextActive) SaveCodeFile(true);
                else SaveCurrentProject();
                evt.StopPropagation();
                return;
            }
            if (evt.ctrlKey && evt.keyCode == KeyCode.S)
            {
                if (codeContextActive) SaveCodeFile(false);
                else SaveCurrentProject();
                evt.StopPropagation();
                return;
            }
            if (evt.ctrlKey && evt.keyCode == KeyCode.N)
            {
                CreateNewCodeFile();
                evt.StopPropagation();
                return;
            }
            if (evt.ctrlKey && evt.keyCode == KeyCode.O)
            {
                OpenCodeFileDialog();
                evt.StopPropagation();
                return;
            }
            if (evt.ctrlKey && evt.keyCode == KeyCode.B)
            {
                RunCodeBuild();
                evt.StopPropagation();
                return;
            }
            if (evt.ctrlKey && evt.keyCode == KeyCode.R)
            {
                RunCodeBuildAndRun();
                evt.StopPropagation();
                return;
            }
            if (evt.ctrlKey && (evt.keyCode == KeyCode.Z || (evt.shiftKey && evt.keyCode == KeyCode.Z)))
            {
                if (IsCodeEditorFocused())
                {
                    if (evt.shiftKey) RedoCodeEdit();
                    else UndoCodeEdit();
                    evt.StopPropagation();
                }
                return;
            }
            if (evt.ctrlKey && evt.keyCode == KeyCode.Y)
            {
                if (IsCodeEditorFocused())
                {
                    RedoCodeEdit();
                    evt.StopPropagation();
                }
                return;
            }
            if (evt.keyCode == KeyCode.Escape)
            {
                CancelWireMode();
                CancelLibraryDrag();
                evt.StopPropagation();
                return;
            }
            if (targetIsText) return;
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
            if (evt.keyCode == KeyCode.T)
            {
                SetTool(ToolMode.Text);
                evt.StopPropagation();
                return;
            }
            // Task 18: Deletion
            if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
            {
                if (DeleteSelectedComponent())
                {
                    evt.StopPropagation();
                }
            }
        }

        private bool DeleteSelectedComponent()
        {
            if (_selectedComponent == null || _selectedVisual == null || _currentCircuit == null) return false;
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
            RequestBreadboardRebuild();
            Debug.Log("[CircuitStudio] Component Deleted.");
            return true;
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
                bool hasBvm = false;
                bool hasVirtualFirmware = false;
                if (_currentCircuit?.Components != null)
                {
                    foreach (var comp in _currentCircuit.Components)
                    {
                        if (comp?.Properties == null) continue;
                        if (comp.Properties.TryGetValue("bvmPath", out var bvmPath) && File.Exists(bvmPath))
                        {
                            hasBvm = true;
                        }
                        if (comp.Properties.TryGetValue("virtualFirmware", out var virtualFirmware) &&
                            !string.IsNullOrWhiteSpace(virtualFirmware))
                        {
                            hasVirtualFirmware = true;
                        }
                    }
                }
                if (hasBvm)
                {
                    SessionManager.Instance.FindFirmware();
                    bool hasExternalFirmware = !string.IsNullOrWhiteSpace(SessionManager.Instance.FirmwarePath)
                        && File.Exists(SessionManager.Instance.FirmwarePath);
                    if (hasVirtualFirmware)
                    {
                        SessionManager.Instance.UseVirtualArduino = true;
                        SessionManager.Instance.UseNativeEnginePins = false;
                    }
                    else
                    {
                        SessionManager.Instance.UseVirtualArduino = false;
                        SessionManager.Instance.UseNativeEnginePins = !hasExternalFirmware;
                    }
                }
                SessionManager.Instance.StartSession(_currentCircuit);
            }
            UnityEngine.SceneManagement.SceneManager.LoadScene(2);
        }

        private void SaveCurrentProject()
        {
            if (_currentCircuit == null) return;
            string path = SessionManager.Instance?.CurrentProjectPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                string root = System.IO.Path.Combine(Application.persistentDataPath, "Projects");
                System.IO.Directory.CreateDirectory(root);
                string safeName = string.IsNullOrWhiteSpace(_currentCircuit.Id) ? "Untitled" : _currentCircuit.Id;
                string fileName = $"{safeName}.rtwin";
                path = System.IO.Path.Combine(root, fileName);
            }
            SaveProjectToPath(path);
        }

        private void SyncCodeWorkspace(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath)) return;
            string projectDir = ResolveProjectWorkspaceRoot(projectPath);
            if (string.IsNullOrWhiteSpace(projectDir)) return;

            string targetRoot = Path.Combine(projectDir, "Code");
            if (Directory.Exists(targetRoot) &&
                Directory.EnumerateFileSystemEntries(targetRoot, "*", SearchOption.AllDirectories).Any())
            {
                return;
            }

            string fallbackRoot = Path.Combine(Application.persistentDataPath, "CodeStudio");
            if (!Directory.Exists(fallbackRoot)) return;

            CopyDirectoryRecursive(fallbackRoot, targetRoot);
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

        private void OnLogMessageThreaded(string logString, string stackTrace, LogType type)
        {
            _pendingLogs.Enqueue(new LogEntry
            {
                Time = System.DateTime.Now,
                Type = type,
                Message = logString ?? string.Empty,
                Stack = stackTrace ?? string.Empty
            });
        }

        private void FlushPendingLogs()
        {
            if (_pendingLogs.IsEmpty) return;

            bool changed = false;
            while (_pendingLogs.TryDequeue(out var entry))
            {
                _logEntries.Add(entry);
                if (_logEntries.Count > MaxOutputLines)
                {
                    _logEntries.RemoveAt(0);
                }
                changed = true;
            }

            if (changed)
            {
                RefreshOutputConsole();
            }
        }

        private void RefreshOutputConsole()
        {
            if (_outputConsole == null) return;
            _outputConsole.Clear();

            VisualElement lastLine = null;
            foreach (var entry in _logEntries)
            {
                if (!ShouldShowLog(entry.Type)) continue;
                string stamp = entry.Time.ToString("HH:mm:ss.fff");
                var line = new Label($"[{stamp}] [{GetLogLabel(entry.Type)}] {entry.Message}");
                line.AddToClassList("console-line");
                line.AddToClassList(GetLogClass(entry.Type));
                _outputConsole.Add(line);
                lastLine = line;

                if (!string.IsNullOrWhiteSpace(entry.Stack) && IsErrorType(entry.Type))
                {
                    var stackLine = new Label(entry.Stack.Trim());
                    stackLine.AddToClassList("console-line");
                    stackLine.AddToClassList("stack");
                    _outputConsole.Add(stackLine);
                    lastLine = stackLine;
                }
            }

            if (_outputPanel != null && !_bottomCollapsed && lastLine != null && _outputAutoFollow)
            {
                _outputPanel.schedule.Execute(() =>
                {
                    var contentContainer = _outputPanel.contentContainer;
                    if (contentContainer != null && lastLine.hierarchy.parent == contentContainer)
                    {
                        _outputPanel.ScrollTo(lastLine);
                    }
                    _outputPanel.scrollOffset = new Vector2(0f, float.MaxValue);
                    _outputPanel.verticalScroller.value = _outputPanel.verticalScroller.highValue;
                });
            }
        }

        private void ClearOutputLogs()
        {
            _logEntries.Clear();
            while (_pendingLogs.TryDequeue(out _)) { }
            if (_outputConsole != null)
            {
                _outputConsole.Clear();
            }
            if (_outputPanel != null)
            {
                _outputPanel.scrollOffset = Vector2.zero;
            }
        }

        private void InitializeOutputAutoScroll()
        {
            SetupAutoFollow(_outputPanel, value => _outputAutoFollow = value);
        }

        private static void SetupAutoFollow(ScrollView scrollView, Action<bool> setFlag)
        {
            if (scrollView == null || setFlag == null) return;
            void UpdateFollow()
            {
                setFlag(IsNearBottom(scrollView));
            }

            scrollView.RegisterCallback<WheelEvent>(_ => UpdateFollow());
            scrollView.RegisterCallback<PointerDownEvent>(_ => UpdateFollow());
            if (scrollView.verticalScroller != null)
            {
                scrollView.verticalScroller.valueChanged += _ => UpdateFollow();
            }
        }

        private static bool IsNearBottom(ScrollView scrollView)
        {
            if (scrollView == null) return true;
            float max = GetMaxScroll(scrollView);
            return scrollView.scrollOffset.y >= max - 2f;
        }

        private static float GetMaxScroll(ScrollView scrollView)
        {
            if (scrollView == null) return 0f;
            float contentHeight = scrollView.contentContainer.layout.height;
            float viewportHeight = scrollView.contentViewport != null
                ? scrollView.contentViewport.layout.height
                : scrollView.layout.height;
            return Mathf.Max(0f, contentHeight - viewportHeight);
        }

        private bool ShouldShowLog(LogType type)
        {
            switch (_outputFilterMode)
            {
                case OutputFilterMode.Errors:
                    return IsErrorType(type);
                case OutputFilterMode.Warnings:
                    return type == LogType.Warning || IsErrorType(type);
                default:
                    return true;
            }
        }

        private static bool IsErrorType(LogType type)
        {
            return type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
        }

        private static string GetLogLabel(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    return "WARN";
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    return "ERROR";
                default:
                    return "INFO";
            }
        }

        private static string GetLogClass(LogType type)
        {
            if (type == LogType.Warning) return "warn";
            if (IsErrorType(type)) return "error";
            return "info";
        }

        private void On3DPointerDown(PointerDownEvent evt)
        {
            if (_centerMode != CenterPanelMode.Preview3D) return;
            if (_circuit3DView == null || _breadboardView == null) return;
            if (evt.button == 0)
            {
                _3dDragMode = ThreeDDragMode.Pan;
            }
            else if (evt.button == 1)
            {
                _3dDragMode = ThreeDDragMode.Orbit;
            }
            else
            {
                return;
            }

            _is3DDragging = true;
            _3dPointerId = evt.pointerId;
            _3dLastPos = (Vector2)evt.position;
            _circuit3DView.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void On3DPointerMove(PointerMoveEvent evt)
        {
            if (_centerMode != CenterPanelMode.Preview3D) return;
            if (!_is3DDragging || evt.pointerId != _3dPointerId || _breadboardView == null) return;
            var delta = (Vector2)evt.position - _3dLastPos;
            _3dLastPos = (Vector2)evt.position;
            if (_3dDragMode == ThreeDDragMode.Pan)
            {
                _breadboardView.Pan(delta);
            }
            else if (_3dDragMode == ThreeDDragMode.Orbit)
            {
                _breadboardView.Orbit(delta);
            }
            evt.StopPropagation();
        }

        private void On3DPointerUp(PointerUpEvent evt)
        {
            if (!_is3DDragging || evt.pointerId != _3dPointerId) return;
            _is3DDragging = false;
            _3dDragMode = ThreeDDragMode.None;
            if (_circuit3DView != null && _circuit3DView.HasPointerCapture(evt.pointerId))
            {
                _circuit3DView.ReleasePointer(evt.pointerId);
            }
            _3dPointerId = -1;
            evt.StopPropagation();
        }

        private void On3DWheel(WheelEvent evt)
        {
            if (_centerMode != CenterPanelMode.Preview3D) return;
            _breadboardView?.Zoom(evt.delta.y);
            evt.StopPropagation();
        }

        private void RefreshCanvas()
        {
            if (_canvasView == null || _currentCircuit == null) return;
            AutoLayoutComponentsIfNeeded();
            EnsureWireLayer();
            var visuals = _canvasView.Query<VisualElement>().ToList();
            foreach (var v in visuals)
            {
                if (v.userData is ComponentSpec) v.RemoveFromHierarchy();
            }

            _componentPositions.Clear();
            _componentVisuals.Clear();
            _pinVisuals.Clear();
            var missingPositions = _currentCircuit.Components
                .Where(comp => !TryGetStoredPosition(comp, out _))
                .ToList();
            if (missingPositions.Count > 0)
            {
                var layoutCenter = GetAutoLayoutCenter();
                var gridSize = GetAutoLayoutGridSize(missingPositions);
                var origin = layoutCenter - gridSize * 0.5f;
                ApplyAutoLayout(missingPositions, origin);
            }
            for (int i = 0; i < _currentCircuit.Components.Count; i++)
            {
                var comp = _currentCircuit.Components[i];
                Vector2 pos;
                if (!TryGetStoredPosition(comp, out pos))
                {
                    pos = GetAutoLayoutCenter();
                }

                var item = ResolveCatalogItem(comp);
                pos = ClampToViewport(pos, GetComponentSize(item));
                pos = ClampToBoard(pos, GetComponentSize(item));
                SetComponentPosition(comp, pos);
                CreateComponentVisuals(comp, item, pos);
            }
            if (_canvasView != null)
            {
                _canvasView.schedule.Execute(RequestWireUpdateThrottled);
            }
            else
            {
                UpdateWireLayer();
            }
            RequestBreadboardRebuild();
        }
    }

    internal enum WireRoutePass
    {
        Strict,
        NoKeepout,
        AllowOverlap
    }

    internal sealed class WireRouter
    {
        private const int MaxIterations = 12000;
        private readonly Rect _board;
        private readonly float _step;
        private readonly int _cols;
        private readonly int _rows;
        private readonly HashSet<Vector2Int> _blocked;
        private readonly Dictionary<Vector2Int, string> _occupancy = new Dictionary<Vector2Int, string>();
        private readonly Dictionary<string, HashSet<Vector2Int>> _netCells = new Dictionary<string, HashSet<Vector2Int>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Vector2Int[] CardinalDirs =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };
        private static readonly Vector2Int[] AdjacentDirs =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
            new Vector2Int(1, 1),
            new Vector2Int(-1, 1),
            new Vector2Int(1, -1),
            new Vector2Int(-1, -1)
        };

        public WireRouter(Rect board, float step, IEnumerable<Rect> obstacles)
        {
            _board = board;
            _step = Mathf.Max(4f, step);
            _cols = Mathf.Max(1, Mathf.CeilToInt(board.width / _step));
            _rows = Mathf.Max(1, Mathf.CeilToInt(board.height / _step));
            _blocked = BuildBlocked(obstacles);
        }

        public Vector2Int PointToCell(Vector2 point)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(point.x / _step), 0, _cols);
            int y = Mathf.Clamp(Mathf.RoundToInt(point.y / _step), 0, _rows);
            return new Vector2Int(x, y);
        }

        public Vector2 CellToPoint(Vector2Int cell)
        {
            return new Vector2(cell.x * _step, cell.y * _step);
        }

        public bool TryRoute(Vector2 startPoint, Vector2 endPoint, string netId, out List<Vector2Int> path)
        {
            var rawStart = PointToCell(startPoint);
            var rawGoal = PointToCell(endPoint);
            if (rawStart == rawGoal)
            {
                path = new List<Vector2Int> { rawStart };
                return true;
            }

            if (TryFindPath(rawStart, rawGoal, netId, WireRoutePass.Strict, out path)) return true;
            if (TryFindPath(rawStart, rawGoal, netId, WireRoutePass.NoKeepout, out path)) return true;
            if (TryFindPath(rawStart, rawGoal, netId, WireRoutePass.AllowOverlap, out path)) return true;

            path = null;
            return false;
        }

        public void CommitPath(IEnumerable<Vector2Int> path, string netId)
        {
            if (!_netCells.TryGetValue(netId, out var cells))
            {
                cells = new HashSet<Vector2Int>();
                _netCells[netId] = cells;
            }
            foreach (var cell in path)
            {
                cells.Add(cell);
                _occupancy[cell] = netId;
            }
        }

        public Dictionary<string, HashSet<string>> BuildNetConflicts()
        {
            var conflicts = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _netCells)
            {
                var netId = kvp.Key;
                if (!conflicts.ContainsKey(netId)) conflicts[netId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cell in kvp.Value)
                {
                    foreach (var dir in AdjacentDirs)
                    {
                        var neighbor = cell + dir;
                        if (_occupancy.TryGetValue(neighbor, out var other) && !string.Equals(other, netId, StringComparison.OrdinalIgnoreCase))
                        {
                            conflicts[netId].Add(other);
                            if (!conflicts.TryGetValue(other, out var otherSet))
                            {
                                otherSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                conflicts[other] = otherSet;
                            }
                            otherSet.Add(netId);
                        }
                    }
                }
            }
            return conflicts;
        }

        private HashSet<Vector2Int> BuildBlocked(IEnumerable<Rect> obstacles)
        {
            var blocked = new HashSet<Vector2Int>();
            if (obstacles == null) return blocked;
            foreach (var rect in obstacles)
            {
                int minX = Mathf.Clamp(Mathf.FloorToInt(rect.xMin / _step), 0, _cols);
                int maxX = Mathf.Clamp(Mathf.CeilToInt(rect.xMax / _step), 0, _cols);
                int minY = Mathf.Clamp(Mathf.FloorToInt(rect.yMin / _step), 0, _rows);
                int maxY = Mathf.Clamp(Mathf.CeilToInt(rect.yMax / _step), 0, _rows);
                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        blocked.Add(new Vector2Int(x, y));
                    }
                }
            }
            return blocked;
        }

        private bool TryFindPath(Vector2Int rawStart, Vector2Int rawGoal, string netId, WireRoutePass pass, out List<Vector2Int> path)
        {
            var start = FindNearestOpenCell(rawStart, netId, pass);
            var goal = FindNearestOpenCell(rawGoal, netId, pass);
            return FindPath(start, goal, netId, pass, out path);
        }

        private Vector2Int FindNearestOpenCell(Vector2Int origin, string netId, WireRoutePass pass)
        {
            if (!IsHardBlocked(origin, netId, pass)) return origin;
            const int maxRadius = 6;
            for (int radius = 1; radius <= maxRadius; radius++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        if (Mathf.Abs(x) != radius && Mathf.Abs(y) != radius) continue;
                        var cell = new Vector2Int(origin.x + x, origin.y + y);
                        if (cell.x < 0 || cell.y < 0 || cell.x > _cols || cell.y > _rows) continue;
                        if (!IsHardBlocked(cell, netId, pass)) return cell;
                    }
                }
            }
            return origin;
        }

        private bool FindPath(Vector2Int start, Vector2Int goal, string netId, WireRoutePass pass, out List<Vector2Int> path)
        {
            path = null;
            var open = new List<Vector2Int> { start };
            var closed = new HashSet<Vector2Int>();
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var dirFrom = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, int> { [start] = 0 };
            var fScore = new Dictionary<Vector2Int, int> { [start] = Heuristic(start, goal) };

            int iterations = 0;
            while (open.Count > 0 && iterations < MaxIterations)
            {
                iterations++;
                var current = GetLowest(open, fScore, goal);
                if (current == goal)
                {
                    path = ReconstructPath(cameFrom, current);
                    return true;
                }

                open.Remove(current);
                closed.Add(current);

                foreach (var dir in CardinalDirs)
                {
                    var neighbor = current + dir;
                    if (neighbor.x < 0 || neighbor.y < 0 || neighbor.x > _cols || neighbor.y > _rows) continue;
                    if (closed.Contains(neighbor)) continue;
                    if (IsBlocked(neighbor, start, goal, netId, pass)) continue;

                    int turnPenalty = 0;
                    if (dirFrom.TryGetValue(current, out var prevDir) && prevDir != dir)
                    {
                        turnPenalty = 45;
                    }

                    int tentative = gScore[current] + 10 + turnPenalty;
                    if (!gScore.TryGetValue(neighbor, out var prevScore) || tentative < prevScore)
                    {
                        cameFrom[neighbor] = current;
                        dirFrom[neighbor] = dir;
                        gScore[neighbor] = tentative;
                        fScore[neighbor] = tentative + Heuristic(neighbor, goal);
                        if (!open.Contains(neighbor)) open.Add(neighbor);
                    }
                }
            }
            return false;
        }

        private bool IsBlocked(Vector2Int cell, Vector2Int start, Vector2Int goal, string netId, WireRoutePass pass)
        {
            if (cell == start || cell == goal) return false;
            if (_blocked.Contains(cell)) return true;
            if (pass != WireRoutePass.AllowOverlap &&
                _occupancy.TryGetValue(cell, out var other) &&
                !string.Equals(other, netId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (pass == WireRoutePass.Strict && IsNearOtherNet(cell, netId)) return true;
            return false;
        }

        private bool IsHardBlocked(Vector2Int cell, string netId, WireRoutePass pass)
        {
            if (_blocked.Contains(cell)) return true;
            if (pass != WireRoutePass.AllowOverlap &&
                _occupancy.TryGetValue(cell, out var other) &&
                !string.Equals(other, netId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (pass == WireRoutePass.Strict && IsNearOtherNet(cell, netId)) return true;
            return false;
        }

        private bool IsNearOtherNet(Vector2Int cell, string netId)
        {
            foreach (var dir in AdjacentDirs)
            {
                var neighbor = cell + dir;
                if (_occupancy.TryGetValue(neighbor, out var other) &&
                    !string.Equals(other, netId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static int Heuristic(Vector2Int a, Vector2Int b)
        {
            return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y)) * 10;
        }

        private static Vector2Int GetLowest(List<Vector2Int> open, Dictionary<Vector2Int, int> fScore, Vector2Int goal)
        {
            Vector2Int best = open[0];
            int bestScore = fScore.TryGetValue(best, out var bestF) ? bestF : int.MaxValue;
            int bestH = Heuristic(best, goal);

            for (int i = 1; i < open.Count; i++)
            {
                var candidate = open[i];
                int f = fScore.TryGetValue(candidate, out var candF) ? candF : int.MaxValue;
                int h = Heuristic(candidate, goal);
                if (f < bestScore || (f == bestScore && h < bestH) || (f == bestScore && h == bestH && CompareCell(candidate, best) < 0))
                {
                    best = candidate;
                    bestScore = f;
                    bestH = h;
                }
            }

            return best;
        }

        private static int CompareCell(Vector2Int a, Vector2Int b)
        {
            int cmp = a.y.CompareTo(b.y);
            if (cmp != 0) return cmp;
            return a.x.CompareTo(b.x);
        }

        private static List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
        {
            var path = new List<Vector2Int> { current };
            while (cameFrom.TryGetValue(current, out var prev))
            {
                current = prev;
                path.Add(current);
            }
            path.Reverse();
            return path;
        }
    }

    internal struct WireSegment
    {
        public Vector2 Start;
        public Vector2 End;
        public Color Color;
        public string NetId;
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
        private Vector2 _pan = Vector2.zero;
        private float _zoom = 1f;
        private Vector2 _origin = Vector2.zero;

        public GridLayer()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        public void SetTransform(Vector2 pan, float zoom, Vector2 origin)
        {
            _pan = pan;
            _zoom = Mathf.Max(0.05f, zoom);
            _origin = origin;
            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            var rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f || Spacing <= 2f) return;

            var painter = ctx.painter2D;
            float spacingPx = Spacing * _zoom;
            if (spacingPx < 4f)
            {
                float majorSpacing = spacingPx * Mathf.Max(1, MajorLineEvery);
                if (majorSpacing < 4f) return;
                DrawGridLines(painter, rect, majorSpacing, MajorColor, 1f);
                return;
            }

            float offsetX = _origin.x + _pan.x;
            float offsetY = _origin.y + _pan.y;
            float startX = Mathf.Repeat(offsetX, spacingPx);
            float startY = Mathf.Repeat(offsetY, spacingPx);

            int cols = Mathf.CeilToInt((rect.width - startX) / spacingPx) + 2;
            int rows = Mathf.CeilToInt((rect.height - startY) / spacingPx) + 2;

            for (int i = -1; i <= cols; i++)
            {
                float x = startX + i * spacingPx;
                int gridIndex = Mathf.RoundToInt((x - offsetX) / spacingPx);
                bool major = MajorLineEvery > 0 && (gridIndex % MajorLineEvery == 0);
                painter.lineWidth = major ? 1.5f : 1f;
                painter.strokeColor = major ? MajorColor : MinorColor;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 0));
                painter.LineTo(new Vector2(x, rect.height));
                painter.Stroke();
            }

            for (int j = -1; j <= rows; j++)
            {
                float y = startY + j * spacingPx;
                int gridIndex = Mathf.RoundToInt((y - offsetY) / spacingPx);
                bool major = MajorLineEvery > 0 && (gridIndex % MajorLineEvery == 0);
                painter.lineWidth = major ? 1.5f : 1f;
                painter.strokeColor = major ? MajorColor : MinorColor;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, y));
                painter.LineTo(new Vector2(rect.width, y));
                painter.Stroke();
            }
        }

        private void DrawGridLines(Painter2D painter, Rect rect, float spacing, Color color, float width)
        {
            float offsetX = _origin.x + _pan.x;
            float offsetY = _origin.y + _pan.y;
            float startX = Mathf.Repeat(offsetX, spacing);
            float startY = Mathf.Repeat(offsetY, spacing);

            int cols = Mathf.CeilToInt((rect.width - startX) / spacing) + 2;
            int rows = Mathf.CeilToInt((rect.height - startY) / spacing) + 2;

            painter.lineWidth = width;
            painter.strokeColor = color;

            for (int i = -1; i <= cols; i++)
            {
                float x = startX + i * spacing;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 0));
                painter.LineTo(new Vector2(x, rect.height));
                painter.Stroke();
            }

            for (int j = -1; j <= rows; j++)
            {
                float y = startY + j * spacing;
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
            if (string.Equals(type, "TextNote", StringComparison.OrdinalIgnoreCase))
            {
                return new Item
                {
                    Id = "text-note",
                    Name = "Text Note",
                    Description = "Annotation",
                    Type = "TextNote",
                    Symbol = "T",
                    IconChar = "T",
                    ElectricalSpecs = new Dictionary<string, string>(),
                    DefaultProperties = new Dictionary<string, string>(),
                    Pins = new List<string>(),
                    Order = 999
                };
            }
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

