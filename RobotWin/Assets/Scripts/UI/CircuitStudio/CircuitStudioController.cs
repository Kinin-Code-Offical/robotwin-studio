using System;
using UnityEngine;
using UnityEngine.UIElements;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Serialization;
using RobotTwin.Game;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
        private VisualElement _circuit3DControls;
        private VisualElement _circuit3DControlsBody;
        private Button _circuit3DControlsToggle;
        private ScrollView _codeFileList;
        private TextField _codeEditor;
        private Label _codeFileLabel;
        private DropdownField _codeTargetDropdown;
        private Button _codeFileMenuBtn;
        private Button _codeBuildMenuBtn;
        private VisualElement _codeBuildProgressWrap;
        private ProgressBar _codeBuildProgress;
        private Button _centerTabCircuit;
        private Button _centerTabCode;
        private Button _centerTab3D;
        private Label _centerStatusLabel;
        private Slider _circuit3DFovSlider;
        private Toggle _circuit3DPerspectiveToggle;
        private Toggle _circuit3DFollowToggle;
        private Button _circuit3DRebuildBtn;
        private VisualElement _circuit3DHelpIcon;
        private VisualElement _circuit3DHelpTooltip;
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
        private VisualElement _wireExportOverlay;
        private ProgressBar _wireExportProgress;
        private Label _wireExportStatus;
        private Coroutine _wireExportRoutine;
        private VisualElement _helpOverlay;
        private Button _helpCloseBtn;
        private CircuitClipboard _circuitClipboard;

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

        private sealed class CircuitClipboard
        {
            public readonly List<ComponentSpec> Components = new List<ComponentSpec>();
            public readonly List<ClipboardNet> Nets = new List<ClipboardNet>();
            public readonly Dictionary<string, Vector2> Positions = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
            public Vector2 Center;
        }

        private sealed class ClipboardNet
        {
            public string Id;
            public List<string> Nodes = new List<string>();
        }

        // Toolbar
        private Button _btnSelect;
        private Button _btnMove;
        private Button _btnWire;
        private Button _btnSim;
        private Button _btnText;
        private Button _btnDrc;
        private Button _btnNet;
        private Button _menuFile;
        private Button _menuEdit;
        private Button _menuView;
        private Button _menuDesign;
        private Button _menuTools;
        private Button _menuRoute;
        private Button _menuHelp;

        // Status
        private Label _statusLabel;
        private Label _versionText;
        private bool _bottomCollapsed;
        private float _bottomExpandedHeight;
        private bool _is3DDragging;
        private int _3dPointerId = -1;
        private Vector2 _3dLastPos;
        private ThreeDDragMode _3dDragMode = ThreeDDragMode.None;
        private bool _is3DViewFocused;
        private string _last3dPickedComponentId;
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
        private Vector2 _moveLastValidPos;
        private Vector2 _moveStartPos;
        private bool _wireUpdatePending;
        private float _wireUpdateAt;
        private Vector2 _lastCanvasWorldPos;

        // Selection & State
        private CircuitSpec _currentCircuit;
        private ComponentSpec _selectedComponent;
        private VisualElement _selectedVisual;
        private ComponentCatalog.Item _selectedCatalogItem;
        private Vector2 _selectedComponentSize = new Vector2(ComponentWidth, ComponentHeight);
        private readonly HashSet<string> _selectedComponentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _isBoxSelecting;
        private int _boxSelectionPointerId = -1;
        private VisualElement _selectionBox;
        private Vector2 _boxSelectionStartLocal;
        private Vector2 _boxSelectionStartBoard;
        private bool _isMovingSelection;
        private int _groupMovePointerId = -1;
        private Vector2 _groupMoveStartBoard;
        private readonly Dictionary<string, Vector2> _groupMoveOriginalPositions = new Dictionary<string, Vector2>();
        private readonly List<CircuitSpec> _undoHistory = new List<CircuitSpec>();
        private readonly List<CircuitSpec> _redoHistory = new List<CircuitSpec>();
        private const int MaxHistoryEntries = 32;
        private bool _isRestoringState;

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
        private PinSide _wireStartSide;
        private VisualElement _wireLayer;
        private readonly List<WireSegment> _wireSegments = new List<WireSegment>();
        private WireRouter _lastRouter;
        private VisualElement _wirePreviewLayer;
        private readonly List<WireSegment> _wirePreviewSegments = new List<WireSegment>();
        private bool _showFloatingPins;
        private string _activeCodePath;
        private string _codeTargetComponentId;
        private readonly Dictionary<string, string> _codeTargetLabels = new Dictionary<string, string>();
        private Circuit3DView _circuit3DRenderer;

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
        private bool _circuit3DDirty;
        private bool _circuit3DControlsCollapsed;
        private bool _pendingResetView;
        private bool _resetViewOnNextLayout;
        private bool _resetViewScheduled;
        private readonly List<CodeEditorSnapshot> _codeHistory = new List<CodeEditorSnapshot>();
        private int _codeHistoryIndex = -1;
        private bool _suppressCodeHistory;
        private bool _gridVisible = true;
        private bool _suppressCodeEditorChanged;
        private Label _codeHighlightLabel;
        private TextElement _codeHighlightTarget;
        private VisualElement _codeHighlightInput;
        private bool _codeHighlightReady;
        private bool _codeHighlightEventsHooked;
        private string _lastHighlightText = string.Empty;

        private const float GridSnap = 10f;
        private const float ComponentWidth = CircuitLayoutSizing.DefaultComponentWidth;
        private const float ComponentHeight = CircuitLayoutSizing.DefaultComponentHeight;
        private const float BoardPadding = 24f;
        private const float MinBoardWidth = 320f;
        private const float MinBoardHeight = 220f;
        private const float MinLeftPanelWidth = 220f;
        private const float MaxLeftPanelWidth = 520f;
        private const float MinRightPanelWidth = 260f;
        private const float MaxRightPanelWidth = 520f;
        private const float MinBottomPanelHeight = 100f;
        private const float MaxBottomPanelHeight = 360f;
        private const float WireObstaclePadding = 16f;
        private const float WireLaneSpacing = 10f;
        private const float AutoLayoutMinGap = 80f;
        private const float ManualOverlapPadding = 4f;
        private const float PinObstaclePadding = 6f;
        private bool _constrainComponentsToBoard = true;
        private const float BoardWorldWidth = CircuitLayoutSizing.BoardWorldWidth;
        private const float BoardWorldHeight = CircuitLayoutSizing.BoardWorldHeight;
        private const float AutoLayoutSpacing = 200f;
        private const float WireExitPadding = 6f;
        private const float WireUpdateInterval = 0.06f;
        private const int WireMatrixLogLimit = 8000;
        private const float CanvasKeyPanPixels = 40f;
        private const float CameraKeyPanPixels = 28f;
        private const float CameraKeyOrbitPixels = 18f;
        private const float CameraKeyZoomDelta = 120f;
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
        private const float MaxCanvasZoom = 8f;
        private static readonly Vector2 ViewportSize = new Vector2(1920f, 1080f);
        private static readonly Regex CompilerMessageWithColumn = new Regex(
            @"^(?<file>.+?):(?<line>\d+):(?<col>\d+):\s*(?<type>error|warning|note):\s*(?<message>.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CompilerMessageNoColumn = new Regex(
            @"^(?<file>.+?):(?<line>\d+):\s*(?<type>error|warning|note):\s*(?<message>.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ComponentIdPattern = new Regex("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
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
            TryApplyPendingResetView();
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
                _canvasView.RegisterCallback<PointerDownEvent>(OnCanvasBoxSelectionStart);
                _canvasView.RegisterCallback<PointerDownEvent>(OnCanvasClick);
                _canvasView.RegisterCallback<PointerDownEvent>(OnCanvasRightClick);
                _canvasView.RegisterCallback<ContextClickEvent>(OnCanvasContextClick);
                _canvasView.RegisterCallback<PointerDownEvent>(OnCanvasPanStart);
                _canvasView.RegisterCallback<PointerMoveEvent>(OnCanvasPanMove);
                _canvasView.RegisterCallback<PointerUpEvent>(OnCanvasPanEnd);
                _canvasView.RegisterCallback<PointerUpEvent>(OnCanvasPointerRelease);
                _canvasView.RegisterCallback<WheelEvent>(OnCanvasWheel, TrickleDown.TrickleDown);
                _canvasView.RegisterCallback<PointerMoveEvent>(OnCanvasPointerMove, TrickleDown.TrickleDown);
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
            _btnDrc = _root.Q<Button>("tool-drc");
            _btnNet = _root.Q<Button>("tool-net");
            _menuFile = _root.Q<Button>("MenuFile");
            _menuEdit = _root.Q<Button>("MenuEdit");
            _menuView = _root.Q<Button>("MenuView");
            _menuDesign = _root.Q<Button>("MenuDesign");
            _menuTools = _root.Q<Button>("MenuTools");
            _menuRoute = _root.Q<Button>("MenuRoute");
            _menuHelp = _root.Q<Button>("MenuHelp");

            _btnSelect?.RegisterCallback<ClickEvent>(_ => SetTool(ToolMode.Select));
            _btnMove?.RegisterCallback<ClickEvent>(_ => SetTool(ToolMode.Move));
            _btnWire?.RegisterCallback<ClickEvent>(_ => SetTool(ToolMode.Wire));
            _btnText?.RegisterCallback<ClickEvent>(_ => SetTool(ToolMode.Text));
            _btnSim?.RegisterCallback<ClickEvent>(_ => StartRunMode());
            _btnDrc?.RegisterCallback<ClickEvent>(_ => RunDrcCheck());
            _btnNet?.RegisterCallback<ClickEvent>(_ => ShowNetEditor());
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
            _circuit3DControls = _root.Q<VisualElement>("Circuit3DControls");
            _circuit3DControlsBody = _root.Q<VisualElement>("Circuit3DControlsBody");
            _circuit3DControlsToggle = _root.Q<Button>("Circuit3DControlsToggle");
            _circuit3DFovSlider = _root.Q<Slider>("Circuit3DFovSlider");
            _circuit3DPerspectiveToggle = _root.Q<Toggle>("Circuit3DPerspectiveToggle");
            _circuit3DFollowToggle = _root.Q<Toggle>("Circuit3DFollowToggle");
            _circuit3DRebuildBtn = _root.Q<Button>("Circuit3DRebuildBtn");
            _circuit3DHelpIcon = _root.Q<VisualElement>("Circuit3DHelpIcon");
            _circuit3DHelpTooltip = _root.Q<VisualElement>("Circuit3DHelpTooltip");
            if (_circuit3DControlsToggle != null)
            {
                _circuit3DControlsToggle.clicked += Toggle3DControlsLayout;
                Set3DControlsLayoutCollapsed(false);
            }
            if (_circuit3DRebuildBtn != null)
            {
                _circuit3DRebuildBtn.clicked += ForceRebuildCircuit3DPreview;
            }
            _codeFileList = _root.Q<ScrollView>("CodeFileList");
            _codeEditor = _root.Q<TextField>("CodeEditor");
            if (_codeEditor == null) Debug.LogError("[CircuitStudio] TextField 'CodeEditor' not found in UI!");
            _codeFileLabel = _root.Q<Label>("CodeFileLabel");
            _codeTargetDropdown = _root.Q<DropdownField>("CodeTargetBoard");
            _codeFileMenuBtn = _root.Q<Button>("CodeFileMenuBtn");
            _codeBuildMenuBtn = _root.Q<Button>("CodeBuildMenuBtn");
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
            _wireExportOverlay = _root.Q<VisualElement>("WireExportOverlay");
            _wireExportProgress = _root.Q<ProgressBar>("WireExportProgress");
            _wireExportStatus = _root.Q<Label>("WireExportStatus");
            _helpOverlay = _root.Q<VisualElement>("HelpOverlay");
            _helpCloseBtn = _root.Q<Button>("HelpCloseBtn");
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
            if (_wireExportOverlay != null)
            {
                _wireExportOverlay.style.display = DisplayStyle.None;
            }
            if (_helpOverlay != null)
            {
                _helpOverlay.style.display = DisplayStyle.None;
                _helpOverlay.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.target == _helpOverlay)
                    {
                        HideHelpOverlay();
                        evt.StopPropagation();
                    }
                });
            }
            _helpCloseBtn?.RegisterCallback<ClickEvent>(_ => HideHelpOverlay());
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
            InitializeCircuit3DPreview();
            Initialize3DInput();
            Initialize3DCameraControls();
            Initialize3DHelpOverlay();

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
            _codeFileMenuBtn?.RegisterCallback<ClickEvent>(_ => ShowCodeFileMenu());
            _codeBuildMenuBtn?.RegisterCallback<ClickEvent>(_ => ShowCodeBuildMenu());
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
            if (_menuHelp != null) _menuHelp.clicked += ShowHelpOverlay;
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

            bool netsChanged = !AreNetCollectionsEqual(_currentCircuit.Nets, nextNets);
            if (netsChanged)
            {
                RecordStateForUndo();
            }

            _currentCircuit.Nets = nextNets;
            HideNetEditor();
            PopulateProjectTree();
            UpdateWireLayer();
            RequestCircuit3DRebuild();
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

        private static bool AreNetCollectionsEqual(List<NetSpec> first, List<NetSpec> second)
        {
            var left = NormalizeNetList(first);
            var right = NormalizeNetList(second);
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i].Id, right[i].Id, StringComparison.OrdinalIgnoreCase)) return false;
                var leftNodes = left[i].Nodes;
                var rightNodes = right[i].Nodes;
                if (leftNodes.Count != rightNodes.Count) return false;
                for (int j = 0; j < leftNodes.Count; j++)
                {
                    if (!string.Equals(leftNodes[j], rightNodes[j], StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static List<(string Id, List<string> Nodes)> NormalizeNetList(List<NetSpec> nets)
        {
            return (nets ?? Enumerable.Empty<NetSpec>())
                .Select(net =>
                {
                    var nodes = net?.Nodes != null
                        ? net.Nodes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList()
                        : new List<string>();
                    return (Id: net?.Id ?? string.Empty, Nodes: nodes);
                })
                .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
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
            menu.AddItem("Undo", false, Undo);
            menu.AddItem("Redo", false, Redo);
            menu.AddSeparator(string.Empty);
            menu.AddItem("Select Tool", false, () => SetTool(ToolMode.Select));
            menu.AddItem("Move Tool", false, () => SetTool(ToolMode.Move));
            menu.AddItem("Wire Tool", false, () => SetTool(ToolMode.Wire));
            menu.AddItem("Text Tool", false, () => SetTool(ToolMode.Text));
            menu.AddSeparator(string.Empty);
            menu.AddItem("Copy", false, CopySelectionToClipboard);
            menu.AddItem("Paste", false, PasteClipboard);
            menu.AddSeparator(string.Empty);
            menu.AddItem("Delete Selected", false, () => DeleteSelectedComponents());
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
            menu.AddItem("Show Grid", _gridVisible, ToggleGridVisibility);
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
            menu.AddItem("Net Editor", false, ShowNetEditor);
            menu.AddSeparator(string.Empty);
            menu.AddItem("Auto Layout Selection", false, AutoLayoutSelection);
            menu.AddItem("Auto Layout All", false, AutoLayoutAll);
            menu.AddItem("Select Connected", false, SelectConnectedComponents);
            menu.DropDown(_menuTools.worldBound, _root, DropdownMenuSizeMode.Auto);
        }

        private void ShowRouteMenu()
        {
            if (_root == null || _menuRoute == null) return;
            var menu = new GenericDropdownMenu();
            menu.AddItem("Wire Tool", false, () => SetTool(ToolMode.Wire));
            menu.AddSeparator(string.Empty);
            menu.AddItem("Rebuild Wires", false, RebuildWires);
            menu.AddItem("Wire Report", false, LogWireReport);
            menu.AddItem("Prune Dangling Nets", false, PruneDanglingNets);
            menu.AddItem("Highlight Floating Pins", _showFloatingPins, ToggleFloatingPinsHighlight);
            menu.AddSeparator(string.Empty);
            menu.AddItem("Export Wire Matrix", false, ExportWireMatrix);
            menu.DropDown(_menuRoute.worldBound, _root, DropdownMenuSizeMode.Auto);
        }

        private void ShowHelpOverlay()
        {
            if (_helpOverlay == null) return;
            _helpOverlay.style.display = DisplayStyle.Flex;
        }

        private void HideHelpOverlay()
        {
            if (_helpOverlay == null) return;
            _helpOverlay.style.display = DisplayStyle.None;
        }

        private void ToggleGridVisibility()
        {
            SetGridVisibility(!_gridVisible);
        }

        private void SetGridVisibility(bool visible)
        {
            _gridVisible = visible;
            if (_gridLayer != null)
            {
                _gridLayer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
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
            RequestResetView();
            PopulateProjectTree();
            UpdateErrorCount();
            RequestCircuit3DRebuild();
            ResetHistory();
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
                UnityEngine.SceneManagement.SceneManager.LoadScene("Wizard");
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
            RequestResetView();
            PopulateProjectTree();
            UpdateErrorCount();
            RequestCircuit3DRebuild();
            ResetHistory();
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
            _circuit3DRenderer?.ApplyAnchorOverrides(_currentCircuit);
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

        private void InitializeCircuit3DPreview()
        {
            if (_circuit3DView == null) return;
            if (_circuit3DRenderer == null)
            {
                var go = new GameObject("CircuitStudio3DView");
                go.transform.SetParent(transform, false);
                _circuit3DRenderer = go.AddComponent<Circuit3DView>();
                Apply3DCameraSettings();
            }

            _circuit3DView.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (_circuit3DRenderer == null) return;
                int width = Mathf.RoundToInt(evt.newRect.width);
                int height = Mathf.RoundToInt(evt.newRect.height);
                if (width <= 0 || height <= 0) return;

                _circuit3DRenderer.Initialize(width, height);
                if (_circuit3DRenderer.TargetTexture != null)
                {
                    _circuit3DView.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_circuit3DRenderer.TargetTexture));
                    var label = _circuit3DView.Q<Label>(className: "circuit-3d-label");
                    if (label != null) label.style.display = DisplayStyle.None;
                }
                RequestCircuit3DRebuild();
            });
        }

        private void Initialize3DInput()
        {
            if (_circuit3DView == null) return;
            _circuit3DView.RegisterCallback<PointerDownEvent>(On3DPointerDown);
            _circuit3DView.RegisterCallback<PointerMoveEvent>(On3DPointerMove);
            _circuit3DView.RegisterCallback<PointerUpEvent>(On3DPointerUp);
            _circuit3DView.RegisterCallback<WheelEvent>(On3DWheel);
            if (_root != null)
            {
                _root.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (_centerMode != CenterPanelMode.Preview3D)
                    {
                        _is3DViewFocused = false;
                        return;
                    }
                    _is3DViewFocused = Is3DViewPointerTarget(evt);
                    if (_is3DViewFocused)
                    {
                        _circuit3DView?.Focus();
                    }
                }, TrickleDown.TrickleDown);
            }
        }

        private void Initialize3DCameraControls()
        {
            if (_circuit3DFovSlider != null)
            {
                _circuit3DFovSlider.RegisterValueChangedCallback(evt =>
                {
                    _circuit3DRenderer?.SetFieldOfView(evt.newValue);
                });
            }

            if (_circuit3DPerspectiveToggle != null)
            {
                _circuit3DPerspectiveToggle.RegisterValueChangedCallback(evt =>
                {
                    _circuit3DRenderer?.SetPerspective(evt.newValue);
                });
            }

            if (_circuit3DFollowToggle != null)
            {
                _circuit3DFollowToggle.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue)
                    {
                        _circuit3DRenderer?.SetFollowComponent(null, false);
                        return;
                    }

                    string focusId = _selectedComponent?.Id ?? _last3dPickedComponentId;
                    if (!string.IsNullOrWhiteSpace(focusId))
                    {
                        _circuit3DRenderer?.SetFollowComponent(focusId, true);
                    }
                });
            }

            Apply3DCameraSettings();
            if (_circuit3DControlsToggle != null)
            {
                Set3DControlsLayoutCollapsed(_circuit3DControlsCollapsed);
            }
        }

        private void Initialize3DHelpOverlay()
        {
            if (_circuit3DHelpTooltip != null)
            {
                _circuit3DHelpTooltip.style.display = DisplayStyle.None;
            }
            if (_circuit3DHelpIcon == null || _circuit3DHelpTooltip == null) return;
            _circuit3DHelpIcon.RegisterCallback<ClickEvent>(evt =>
            {
                bool isVisible = _circuit3DHelpTooltip.style.display == DisplayStyle.Flex;
                _circuit3DHelpTooltip.style.display = isVisible ? DisplayStyle.None : DisplayStyle.Flex;
                evt.StopPropagation();
            });
            if (_root != null)
            {
                _root.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (Is3DHelpPointerTarget(evt)) return;
                    if (_circuit3DHelpTooltip?.style.display == DisplayStyle.Flex)
                    {
                        _circuit3DHelpTooltip.style.display = DisplayStyle.None;
                    }
                }, TrickleDown.TrickleDown);
            }
        }

        private bool Is3DHelpPointerTarget(PointerDownEvent evt)
        {
            if (evt.target is VisualElement target)
            {
                if (_circuit3DHelpIcon != null && (_circuit3DHelpIcon == target || _circuit3DHelpIcon.Contains(target)))
                {
                    return true;
                }
                if (_circuit3DHelpTooltip != null && (_circuit3DHelpTooltip == target || _circuit3DHelpTooltip.Contains(target)))
                {
                    return true;
                }
            }
            return false;
        }

        private bool Is3DViewPointerTarget(PointerDownEvent evt)
        {
            if (_circuit3DView == null) return false;
            if (evt.target is VisualElement target)
            {
                return _circuit3DView == target || _circuit3DView.Contains(target);
            }
            return false;
        }

        private void Apply3DCameraSettings()
        {
            if (_circuit3DRenderer == null) return;
            if (_circuit3DFovSlider != null)
            {
                _circuit3DRenderer.SetFieldOfView(_circuit3DFovSlider.value);
            }
            if (_circuit3DPerspectiveToggle != null)
            {
                _circuit3DRenderer.SetPerspective(_circuit3DPerspectiveToggle.value);
            }
        }

        private void Toggle3DControlsLayout()
        {
            Set3DControlsLayoutCollapsed(!_circuit3DControlsCollapsed);
        }

        private void Set3DControlsLayoutCollapsed(bool collapsed)
        {
            _circuit3DControlsCollapsed = collapsed;
            if (_circuit3DControlsBody != null)
            {
                _circuit3DControlsBody.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (_circuit3DControls != null)
            {
                _circuit3DControls.EnableInClassList("controls-collapsed", collapsed);
            }
            if (_circuit3DControlsToggle != null)
            {
                _circuit3DControlsToggle.text = collapsed ? "Show" : "Hide";
            }
        }

        private void RequestCircuit3DRebuild()
        {
            _circuit3DDirty = true;
        }

        private void LateUpdate()
        {
            if (!_circuit3DDirty) return;
            if (_centerMode != CenterPanelMode.Preview3D) return;
            RebuildCircuit3DPreview();
        }

        private void RebuildCircuit3DPreview()
        {
            if (_circuit3DRenderer == null || _currentCircuit == null || _circuit3DView == null) return;
            if (_circuit3DRenderer.TargetTexture == null)
            {
                var rect = _circuit3DView.contentRect;
                int width = Mathf.RoundToInt(rect.width);
                int height = Mathf.RoundToInt(rect.height);
                if (width > 0 && height > 0)
                {
                    _circuit3DRenderer.Initialize(width, height);
                }
            }

            if (_circuit3DRenderer.TargetTexture == null) return;

            _circuit3DRenderer.ClearAnchorCache();
            _circuit3DRenderer.Build(_currentCircuit);
            _circuit3DView.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_circuit3DRenderer.TargetTexture));
            var label = _circuit3DView.Q<Label>(className: "circuit-3d-label");
            if (label != null) label.style.display = DisplayStyle.None;
            _circuit3DDirty = false;
        }

        private void ForceRebuildCircuit3DPreview()
        {
            _circuit3DDirty = true;
            if (_centerMode != CenterPanelMode.Preview3D) return;
            RebuildCircuit3DPreview();
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
            RequestResetView();
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
            _is3DViewFocused = false;
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
                RequestCircuit3DRebuild();
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

        private void OpenCodeFolder()
        {
            string root = GetCodeRoot(_codeTargetComponentId);
            if (string.IsNullOrWhiteSpace(root)) return;
            Directory.CreateDirectory(root);
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = root,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CodeStudio] Failed to open code folder: {ex.Message}");
            }
        }

        private void ShowCodeFileMenu()
        {
            if (_root == null || _codeFileMenuBtn == null) return;
            var menu = new GenericDropdownMenu();
            menu.AddItem("New File", false, CreateNewCodeFile);
            menu.AddItem("Open File", false, OpenCodeFileDialog);
            menu.AddItem("Open Folder", false, OpenCodeFolder);
            menu.AddSeparator(string.Empty);
            menu.AddItem("Save", false, () => SaveCodeFile(false));
            menu.AddItem("Save As", false, () => SaveCodeFile(true));
            menu.DropDown(_codeFileMenuBtn.worldBound, _root, DropdownMenuSizeMode.Auto);
        }

        private void ShowCodeBuildMenu()
        {
            if (_root == null || _codeBuildMenuBtn == null) return;
            var menu = new GenericDropdownMenu();
            menu.AddItem("Build", false, RunCodeBuild);
            menu.AddItem("Build + Run", false, RunCodeBuildAndRun);
            menu.AddItem("Build All", false, RunBuildAll);
            menu.DropDown(_codeBuildMenuBtn.worldBound, _root, DropdownMenuSizeMode.Auto);
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
            string outDir = Path.Combine(codeRoot, "builds");
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
            _codeBuildMenuBtn?.SetEnabled(enabled);
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
                EnsureComponentSizeProperties(comp);
            }
            AutoLayoutComponentsIfNeeded();
        }

        private void EnsureComponentSizeProperties(ComponentSpec comp)
        {
            if (comp?.Properties == null) return;
            if (comp.Properties.ContainsKey("sizeX") && comp.Properties.ContainsKey("sizeY")) return;
            var size = GetComponentSize(string.IsNullOrWhiteSpace(comp.Type) ? string.Empty : comp.Type);
            comp.Properties["sizeX"] = size.x.ToString("F2", CultureInfo.InvariantCulture);
            comp.Properties["sizeY"] = size.y.ToString("F2", CultureInfo.InvariantCulture);
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

            if (missingPosition.Count == 0)
            {
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
                var padded = new Rect(
                    pos.x - AutoLayoutMinGap,
                    pos.y - AutoLayoutMinGap,
                    size.x + AutoLayoutMinGap * 2f,
                    size.y + AutoLayoutMinGap * 2f
                );
                rects.Add(padded);
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
            SetGridVisibility(_gridVisible);

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
            if (_resetViewOnNextLayout)
            {
                ResetCanvasView();
                _resetViewOnNextLayout = false;
                _pendingResetView = false;
            }
            TryApplyPendingResetView();
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
            RequestCircuit3DRebuild();
            if (_canvasView == null || _wireLayer == null || _currentCircuit == null) return;
            if (_currentCircuit.Nets == null)
            {
                UpdatePinDotColors(null);
                return;
            }
            _wireSegments.Clear();

            var boardRect = GetBoardRect();
            if (boardRect.width <= 0f || boardRect.height <= 0f) return;

            var obstacles = GetWireObstacles();
            // Use GridSnap * 0.5f (5f) to align perfectly with the 10f grid
            var router = new WireRouter(boardRect, GridSnap * 0.5f, obstacles);
            _lastRouter = router;
            var netIds = new List<string>();

            foreach (var net in _currentCircuit.Nets.OrderBy(n => GetNetBoundingBoxSize(n)))
            {
                if (net == null || net.Nodes == null || net.Nodes.Count < 2) continue;
                var points = new List<WirePoint>();
                foreach (var node in net.Nodes)
                {
                    var pinPoint = GetNodePosition(node);
                    var anchorPoint = GetWireAnchor(node, pinPoint);
                    if (!pinPoint.HasValue || !anchorPoint.HasValue) continue;
                    points.Add(new WirePoint(node, pinPoint.Value, anchorPoint.Value));
                }
                if (points.Count < 2) continue;

                netIds.Add(net.Id ?? string.Empty);
                foreach (var pair in BuildWirePairs(points))
                {
                    if (router.TryRoute(pair.AnchorStart, pair.AnchorEnd, net.Id ?? string.Empty, out var path))
                    {
                        AppendPathSegments(router, path, pair.PinStart, pair.PinEnd, net.Id ?? string.Empty);
                        router.CommitPath(path, net.Id ?? string.Empty);
                    }
                    else
                    {
                        if (TryAddOrthogonalSegmentsAvoidingObstacles(pair.AnchorStart, pair.AnchorEnd, net.Id ?? string.Empty, obstacles, router))
                        {
                            AddPinStubs(pair.PinStart, pair.AnchorStart, net.Id ?? string.Empty);
                            AddPinStubs(pair.PinEnd, pair.AnchorEnd, net.Id ?? string.Empty);
                        }
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
            UpdatePinDotColors(paletteMap);
            _wirePreviewLayer?.BringToFront();
        }

        private void AutoLayoutSelection()
        {
            if (_currentCircuit?.Components == null) return;
            var targets = _currentCircuit.Components
                .Where(comp => comp != null && _selectedComponentIds.Contains(comp.Id))
                .ToList();
            if (targets.Count == 0)
            {
                LogNoStack(LogType.Log, "[Layout] No selected components to layout.");
                return;
            }
            AutoLayoutComponents(targets);
        }

        private void AutoLayoutAll()
        {
            if (_currentCircuit?.Components == null) return;
            var targets = _currentCircuit.Components.Where(comp => comp != null).ToList();
            if (targets.Count == 0) return;
            AutoLayoutComponents(targets);
        }

        private void AutoLayoutComponents(List<ComponentSpec> components)
        {
            if (components == null || components.Count == 0) return;
            RecordStateForUndo();
            var layoutCenter = GetAutoLayoutCenter();
            var gridSize = GetAutoLayoutGridSize(components);
            var origin = layoutCenter - gridSize * 0.5f;
            ApplyAutoLayoutWithVisuals(components, origin);
            RebuildWires();
            UpdateErrorCount();
            PopulateProjectTree();
            RequestCircuit3DRebuild();
            if (_selectedComponent != null && _selectedComponentIds.Contains(_selectedComponent.Id))
            {
                UpdateTransformFields(GetComponentPosition(_selectedComponent.Id));
            }
        }

        private void ApplyAutoLayoutWithVisuals(List<ComponentSpec> components, Vector2 origin)
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
                var size = GetComponentSize(string.IsNullOrWhiteSpace(comp.Type) ? string.Empty : comp.Type);
                var pos = new Vector2(origin.x + col * cellW, origin.y + row * cellH);
                pos.x = Mathf.Round(pos.x / GridSnap) * GridSnap;
                pos.y = Mathf.Round(pos.y / GridSnap) * GridSnap;
                pos = ClampToBoard(pos, size);
                SetComponentPosition(comp, pos, true);
                if (_componentVisuals.TryGetValue(comp.Id, out var visual))
                {
                    visual.style.left = pos.x;
                    visual.style.top = pos.y;
                }
            }
        }

        private void SelectConnectedComponents()
        {
            if (_currentCircuit?.Nets == null || _currentCircuit.Components == null) return;
            if (_selectedComponentIds.Count == 0) return;

            var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var net in _currentCircuit.Nets)
            {
                if (net?.Nodes == null || net.Nodes.Count == 0) continue;
                bool touchesSelection = false;
                foreach (var node in net.Nodes)
                {
                    int dotIndex = node.IndexOf('.');
                    if (dotIndex <= 0) continue;
                    string compId = node.Substring(0, dotIndex);
                    if (_selectedComponentIds.Contains(compId))
                    {
                        touchesSelection = true;
                        break;
                    }
                }

                if (!touchesSelection) continue;
                foreach (var node in net.Nodes)
                {
                    int dotIndex = node.IndexOf('.');
                    if (dotIndex <= 0) continue;
                    connected.Add(node.Substring(0, dotIndex));
                }
            }

            if (connected.Count == 0)
            {
                LogNoStack(LogType.Log, "[Selection] No connected components found.");
                return;
            }

            ClearSelection();
            ResetPropertyBindings();
            bool primarySet = false;
            foreach (var compId in connected)
            {
                var spec = _currentCircuit.Components.FirstOrDefault(c => c != null && string.Equals(c.Id, compId, StringComparison.OrdinalIgnoreCase));
                if (spec == null) continue;
                if (!_componentVisuals.TryGetValue(spec.Id, out var visual) || visual == null) continue;
                var item = ResolveCatalogItem(spec);
                AddComponentToSelection(visual, spec, item, !primarySet);
                primarySet = true;
            }
        }

        private void CopySelectionToClipboard()
        {
            if (_currentCircuit?.Components == null) return;
            if (_selectedComponentIds.Count == 0)
            {
                LogNoStack(LogType.Log, "[Clipboard] No selection to copy.");
                return;
            }

            var selected = _currentCircuit.Components
                .Where(comp => comp != null && _selectedComponentIds.Contains(comp.Id))
                .ToList();
            if (selected.Count == 0)
            {
                LogNoStack(LogType.Log, "[Clipboard] No selection to copy.");
                return;
            }

            var clipboard = new CircuitClipboard();
            foreach (var comp in selected)
            {
                var clone = CloneComponent(comp);
                if (clone == null) continue;
                clipboard.Components.Add(clone);
                clipboard.Positions[comp.Id] = GetComponentPosition(comp.Id);
            }
            var bounds = GetSelectionBounds(selected);
            clipboard.Center = bounds.center;

            if (_currentCircuit.Nets != null)
            {
                foreach (var net in _currentCircuit.Nets)
                {
                    if (net?.Nodes == null || net.Nodes.Count == 0) continue;
                    var nodes = net.Nodes.Where(IsNodeInSelection).ToList();
                    if (nodes.Count < 2) continue;
                    clipboard.Nets.Add(new ClipboardNet
                    {
                        Id = net.Id,
                        Nodes = nodes
                    });
                }
            }

            _circuitClipboard = clipboard;
            LogNoStack(LogType.Log,
                $"[Clipboard] Copied {clipboard.Components.Count} components, {clipboard.Nets.Count} nets.");
        }

        private void PasteClipboard()
        {
            if (_currentCircuit == null) return;
            if (_circuitClipboard == null || _circuitClipboard.Components.Count == 0)
            {
                LogNoStack(LogType.Log, "[Clipboard] Clipboard is empty.");
                return;
            }

            RecordStateForUndo();
            if (_currentCircuit.Components == null) _currentCircuit.Components = new List<ComponentSpec>();
            if (_currentCircuit.Nets == null) _currentCircuit.Nets = new List<NetSpec>();

            var existingIds = new HashSet<string>(
                _currentCircuit.Components.Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id))
                    .Select(c => c.Id),
                StringComparer.OrdinalIgnoreCase);

            var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var newComponents = new List<ComponentSpec>();

            Vector2 anchor = GetPasteAnchor();
            Vector2 offset = anchor - _circuitClipboard.Center;

            foreach (var comp in _circuitClipboard.Components)
            {
                if (comp == null) continue;
                var clone = CloneComponent(comp);
                if (clone == null) continue;

                string newId = GetUniqueComponentId(comp.Id, existingIds);
                clone.Id = newId;
                EnsureComponentProperties(clone);

                var item = ResolveCatalogItem(clone);
                var size = GetComponentSize(string.IsNullOrWhiteSpace(clone.Type) ? string.Empty : clone.Type);

                Vector2 pos = Vector2.zero;
                if (_circuitClipboard.Positions.TryGetValue(comp.Id, out var storedPos))
                {
                    pos = storedPos;
                }
                pos += offset;
                pos.x = Mathf.Round(pos.x / GridSnap) * GridSnap;
                pos.y = Mathf.Round(pos.y / GridSnap) * GridSnap;
                if (_constrainComponentsToBoard)
                {
                    pos = ClampToBoard(pos, size);
                }

                _currentCircuit.Components.Add(clone);
                existingIds.Add(newId);
                idMap[comp.Id] = newId;

                CreateComponentVisuals(clone, item, pos);
                newComponents.Add(clone);
            }

            int createdNets = 0;
            foreach (var net in _circuitClipboard.Nets)
            {
                if (net?.Nodes == null || net.Nodes.Count == 0) continue;
                var mappedNodes = new List<string>();
                foreach (var node in net.Nodes)
                {
                    string oldCompId = GetComponentIdFromNode(node);
                    if (string.IsNullOrWhiteSpace(oldCompId)) continue;
                    if (!idMap.TryGetValue(oldCompId, out var newCompId)) continue;
                    string pinName = node.Substring(oldCompId.Length + 1);
                    mappedNodes.Add($"{newCompId}.{pinName}");
                }
                if (mappedNodes.Count < 2) continue;

                string baseName = string.IsNullOrWhiteSpace(net.Id) ? "NET_COPY" : $"{net.Id}_COPY";
                if (!IsValidNetId(baseName))
                {
                    baseName = "NET_COPY";
                }
                string newNetId = GetUniqueNetName(baseName);
                _currentCircuit.Nets.Add(new NetSpec
                {
                    Id = newNetId,
                    Nodes = mappedNodes
                });
                createdNets++;
            }

            RebuildPinUsage();
            RebuildWires();
            PopulateProjectTree();
            UpdateErrorCount();
            RequestCircuit3DRebuild();

            ClearSelection();
            ResetPropertyBindings();
            bool primarySet = false;
            foreach (var comp in newComponents)
            {
                if (!_componentVisuals.TryGetValue(comp.Id, out var visual) || visual == null) continue;
                var item = ResolveCatalogItem(comp);
                AddComponentToSelection(visual, comp, item, !primarySet);
                primarySet = true;
            }
            if (_selectedComponent != null)
            {
                UpdateTransformFields(GetComponentPosition(_selectedComponent.Id));
            }

            LogNoStack(LogType.Log, $"[Clipboard] Pasted {newComponents.Count} components, {createdNets} nets.");
        }

        private Vector2 GetPasteAnchor()
        {
            if (_canvasView != null && _canvasView.worldBound.Contains(_lastCanvasWorldPos))
            {
                return GetBoardPositionAt(_lastCanvasWorldPos);
            }
            return GetCanvasCenter();
        }

        private Rect GetSelectionBounds(List<ComponentSpec> components)
        {
            if (components == null || components.Count == 0)
            {
                return new Rect(Vector2.zero, Vector2.zero);
            }

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            foreach (var comp in components)
            {
                if (comp == null) continue;
                var size = GetComponentSize(string.IsNullOrWhiteSpace(comp.Type) ? string.Empty : comp.Type);
                var pos = GetComponentPosition(comp.Id);
                minX = Mathf.Min(minX, pos.x);
                minY = Mathf.Min(minY, pos.y);
                maxX = Mathf.Max(maxX, pos.x + size.x);
                maxY = Mathf.Max(maxY, pos.y + size.y);
            }

            if (minX == float.MaxValue || minY == float.MaxValue ||
                maxX == float.MinValue || maxY == float.MinValue)
            {
                return new Rect(Vector2.zero, Vector2.zero);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private bool IsNodeInSelection(string node)
        {
            string compId = GetComponentIdFromNode(node);
            return !string.IsNullOrWhiteSpace(compId) && _selectedComponentIds.Contains(compId);
        }

        private static string GetComponentIdFromNode(string node)
        {
            if (string.IsNullOrWhiteSpace(node)) return string.Empty;
            int dotIndex = node.IndexOf('.');
            if (dotIndex <= 0) return string.Empty;
            return node.Substring(0, dotIndex);
        }

        private static string GetUniqueComponentId(string baseId, HashSet<string> existingIds)
        {
            string safeBase = string.IsNullOrWhiteSpace(baseId) ? "COMP" : baseId;
            string candidate = $"{safeBase}_COPY";
            int index = 1;
            while (existingIds.Contains(candidate))
            {
                candidate = $"{safeBase}_COPY_{index++}";
            }
            return candidate;
        }

        private void RebuildWires()
        {
            UpdateWireLayer();
            LogNoStack(LogType.Log, "[Wiring] Wires rebuilt.");
        }

        private void ToggleFloatingPinsHighlight()
        {
            _showFloatingPins = !_showFloatingPins;
            UpdateWireLayer();
            string state = _showFloatingPins ? "enabled" : "disabled";
            LogNoStack(LogType.Log, $"[Wiring] Floating pin highlight {state}.");
        }

        private void LogWireReport()
        {
            int netCount = _currentCircuit?.Nets?.Count ?? 0;
            int nodeCount = _currentCircuit?.Nets?.Where(net => net?.Nodes != null).Sum(net => net.Nodes.Count) ?? 0;
            int segmentCount = _wireSegments.Count;
            float totalLength = 0f;
            foreach (var segment in _wireSegments)
            {
                totalLength += Vector2.Distance(segment.Start, segment.End);
            }
            LogNoStack(LogType.Log,
                $"[Wiring] Nets:{netCount} Nodes:{nodeCount} Segments:{segmentCount} TotalLen:{totalLength:F1}mm");
        }

        private void PruneDanglingNets()
        {
            if (_currentCircuit?.Nets == null) return;
            int before = _currentCircuit.Nets.Count;
            var trimmed = _currentCircuit.Nets.Where(net => net?.Nodes != null && net.Nodes.Count >= 2).ToList();
            int removed = before - trimmed.Count;
            if (removed <= 0)
            {
                LogNoStack(LogType.Log, "[Wiring] No dangling nets to prune.");
                return;
            }

            RecordStateForUndo();
            _currentCircuit.Nets = trimmed;
            RebuildPinUsage();
            UpdateWireLayer();
            UpdateErrorCount();
            PopulateProjectTree();
            RequestCircuit3DRebuild();
            LogNoStack(LogType.Log, $"[Wiring] Pruned {removed} dangling net(s).");
        }

        public void ExportWireMatrix()
        {
            if (_lastRouter == null)
            {
                Debug.LogWarning("No wire router available. Please update wires first.");
                return;
            }

            if (_wireExportRoutine != null)
            {
                StopCoroutine(_wireExportRoutine);
                _wireExportRoutine = null;
            }
            _wireExportRoutine = StartCoroutine(ExportWireMatrixRoutine());
        }

        private IEnumerator ExportWireMatrixRoutine()
        {
            ShowWireExportProgress("Exporting wire matrix...");
            string path = Path.Combine(Application.persistentDataPath, "wire_matrix_debug.txt");
            var router = _lastRouter;
            var task = Task.Run(() =>
            {
                string matrix = router.GetDebugMatrix();
                File.WriteAllText(path, matrix);
                return matrix;
            });

            float timer = 0f;
            while (!task.IsCompleted)
            {
                timer += Time.unscaledDeltaTime;
                if (_wireExportProgress != null)
                {
                    _wireExportProgress.value = Mathf.PingPong(timer * 60f, 100f);
                }
                yield return null;
            }

            _wireExportRoutine = null;
            HideWireExportProgress();

            string matrix = null;
            try
            {
                matrix = task.Result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save wire matrix to file: {ex.GetBaseException().Message}");
                yield break;
            }

            if (string.IsNullOrEmpty(matrix))
            {
                Debug.LogWarning("Wire matrix export returned no data.");
                yield break;
            }

            if (matrix.Length <= WireMatrixLogLimit)
            {
                Debug.Log(matrix);
            }
            Debug.Log($"Wire matrix saved to: {path}");
        }

        private void ShowWireExportProgress(string status)
        {
            if (_wireExportOverlay != null)
            {
                _wireExportOverlay.style.display = DisplayStyle.Flex;
            }
            if (_wireExportStatus != null)
            {
                _wireExportStatus.text = status ?? string.Empty;
            }
            if (_wireExportProgress != null)
            {
                _wireExportProgress.title = "Exporting...";
                _wireExportProgress.value = 0f;
            }
        }

        private void HideWireExportProgress()
        {
            if (_wireExportOverlay != null)
            {
                _wireExportOverlay.style.display = DisplayStyle.None;
            }
        }

        private void UpdatePinDotColors(Dictionary<string, int> paletteMap)
        {
            var defaultColor = new Color(0.22f, 0.75f, 0.95f, 0.95f);
            var floatingColor = new Color(0.98f, 0.72f, 0.18f, 0.95f);
            foreach (var kvp in _pinVisuals)
            {
                var pin = kvp.Value;
                if (pin == null) continue;
                bool floating = _showFloatingPins && !IsPinConnected(kvp.Key);
                pin.style.backgroundColor = floating ? floatingColor : defaultColor;
            }

            if (_currentCircuit?.Nets == null) return;
            foreach (var net in _currentCircuit.Nets)
            {
                if (net?.Nodes == null || net.Nodes.Count == 0) continue;
                int paletteIndex;
                Color color = (!string.IsNullOrWhiteSpace(net.Id) && paletteMap != null && paletteMap.TryGetValue(net.Id, out paletteIndex))
                    ? WirePalette[paletteIndex]
                    : GetNetColor(net.Id);
                foreach (var node in net.Nodes)
                {
                    if (_pinVisuals.TryGetValue(node, out var pinEl) && pinEl != null)
                    {
                        pinEl.style.backgroundColor = color;
                    }
                }
            }
        }

        private float GetNetBoundingBoxSize(NetSpec net)
        {
            if (net == null || net.Nodes == null || net.Nodes.Count == 0) return float.MaxValue;
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var node in net.Nodes)
            {
                var pos = GetNodePosition(node);
                if (pos.HasValue)
                {
                    minX = Mathf.Min(minX, pos.Value.x);
                    minY = Mathf.Min(minY, pos.Value.y);
                    maxX = Mathf.Max(maxX, pos.Value.x);
                    maxY = Mathf.Max(maxY, pos.Value.y);
                }
            }
            return (maxX - minX) + (maxY - minY);
        }


        private readonly struct WirePoint
        {
            public string Node { get; }
            public Vector2 PinPoint { get; }
            public Vector2 AnchorPoint { get; }

            public WirePoint(string node, Vector2 pinPoint, Vector2 anchorPoint)
            {
                Node = node;
                PinPoint = pinPoint;
                AnchorPoint = anchorPoint;
            }
        }

        private readonly struct WirePair
        {
            public Vector2 PinStart { get; }
            public Vector2 PinEnd { get; }
            public Vector2 AnchorStart { get; }
            public Vector2 AnchorEnd { get; }

            public WirePair(Vector2 pinStart, Vector2 pinEnd, Vector2 anchorStart, Vector2 anchorEnd)
            {
                PinStart = pinStart;
                PinEnd = pinEnd;
                AnchorStart = anchorStart;
                AnchorEnd = anchorEnd;
            }
        }

        private static List<WirePair> BuildWirePairs(List<WirePoint> points)
        {
            var pairs = new List<WirePair>();
            if (points == null || points.Count < 2) return pairs;

            var connected = new HashSet<int> { 0 };
            var remaining = new HashSet<int>(Enumerable.Range(1, points.Count - 1));

            while (remaining.Count > 0)
            {
                float bestDist = float.MaxValue;
                int bestFrom = -1;
                int bestTo = -1;
                foreach (var from in connected)
                {
                    var p0 = points[from].AnchorPoint;
                    foreach (var to in remaining)
                    {
                        var p1 = points[to].AnchorPoint;
                        float dx = p0.x - p1.x;
                        float dy = p0.y - p1.y;
                        float dist = (dx * dx) + (dy * dy);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestFrom = from;
                            bestTo = to;
                        }
                    }
                }

                if (bestFrom == -1 || bestTo == -1) break;
                pairs.Add(new WirePair(
                    points[bestFrom].PinPoint,
                    points[bestTo].PinPoint,
                    points[bestFrom].AnchorPoint,
                    points[bestTo].AnchorPoint
                ));
                connected.Add(bestTo);
                remaining.Remove(bestTo);
            }

            return pairs;
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

        private bool TryAddOrthogonalSegmentsAvoidingObstacles(Vector2 start, Vector2 end, string netId, List<Rect> obstacles, WireRouter router)
        {
            bool horizontalFirst = (StableHash(netId) & 1u) == 0u;

            if (obstacles == null || obstacles.Count == 0)
            {
                if (router != null && !router.IsSegmentClear(start, end, netId, WireRoutePass.AllowCrossing))
                {
                    return false;
                }
                AddOrthogonalSegments(start, end, netId);
                if (router != null)
                {
                    Vector2 mid = horizontalFirst ? new Vector2(end.x, start.y) : new Vector2(start.x, end.y);
                    router.MarkOccupancy(start, mid, netId);
                    router.MarkOccupancy(mid, end, netId);
                }
                return true;
            }

            if (Mathf.Approximately(start.x, end.x) || Mathf.Approximately(start.y, end.y))
            {
                if (!SegmentIntersectsObstacles(start, end, obstacles))
                {
                    if (router != null && !router.IsSegmentClear(start, end, netId, WireRoutePass.AllowCrossing))
                    {
                        return false;
                    }
                    AddWireSegment(start, end, netId);
                    router?.MarkOccupancy(start, end, netId);
                    return true;
                }
            }

            // horizontalFirst is already defined at the top of the method
            Vector2 midA = horizontalFirst ? new Vector2(end.x, start.y) : new Vector2(start.x, end.y);
            Vector2 midB = horizontalFirst ? new Vector2(start.x, end.y) : new Vector2(end.x, start.y);

            if (IsOrthogonalPathClear(start, midA, end, obstacles))
            {
                if (router != null &&
                    (!router.IsSegmentClear(start, midA, netId, WireRoutePass.AllowCrossing) ||
                     !router.IsSegmentClear(midA, end, netId, WireRoutePass.AllowCrossing)))
                {
                    return false;
                }
                AddWireSegment(start, midA, netId);
                AddWireSegment(midA, end, netId);
                if (router != null)
                {
                    router.MarkOccupancy(start, midA, netId);
                    router.MarkOccupancy(midA, end, netId);
                }
                return true;
            }
            if (IsOrthogonalPathClear(start, midB, end, obstacles))
            {
                if (router != null &&
                    (!router.IsSegmentClear(start, midB, netId, WireRoutePass.AllowCrossing) ||
                     !router.IsSegmentClear(midB, end, netId, WireRoutePass.AllowCrossing)))
                {
                    return false;
                }
                AddWireSegment(start, midB, netId);
                AddWireSegment(midB, end, netId);
                if (router != null)
                {
                    router.MarkOccupancy(start, midB, netId);
                    router.MarkOccupancy(midB, end, netId);
                }
                return true;
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
                        if (router != null &&
                            (!router.IsSegmentClear(start, mid, netId, WireRoutePass.AllowCrossing) ||
                             !router.IsSegmentClear(mid, end, netId, WireRoutePass.AllowCrossing)))
                        {
                            continue;
                        }
                        AddWireSegment(start, mid, netId);
                        AddWireSegment(mid, end, netId);
                        if (router != null)
                        {
                            router.MarkOccupancy(start, mid, netId);
                            router.MarkOccupancy(mid, end, netId);
                        }
                        return true;
                    }
                }
                else
                {
                    if (TryOffsetMid(start, end, netId, new Vector2(offset, 0f), obstacles, out var mid) ||
                        TryOffsetMid(start, end, netId, new Vector2(-offset, 0f), obstacles, out mid))
                    {
                        if (router != null &&
                            (!router.IsSegmentClear(start, mid, netId, WireRoutePass.AllowCrossing) ||
                             !router.IsSegmentClear(mid, end, netId, WireRoutePass.AllowCrossing)))
                        {
                            continue;
                        }
                        AddWireSegment(start, mid, netId);
                        AddWireSegment(mid, end, netId);
                        if (router != null)
                        {
                            router.MarkOccupancy(start, mid, netId);
                            router.MarkOccupancy(mid, end, netId);
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        private void AddPinStubs(Vector2 pinPoint, Vector2 anchorPoint, string netId)
        {
            if ((pinPoint - anchorPoint).sqrMagnitude < 0.25f) return;
            AddOrthogonalSegments(pinPoint, anchorPoint, netId);
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
            return IsOrthogonalPathClear(start, mid, end, obstacles, netId);
        }

        private static bool IsOrthogonalPathClear(Vector2 start, Vector2 mid, Vector2 end, List<Rect> obstacles, string netId = null)
        {
            return !SegmentIntersectsObstacles(start, mid, obstacles, netId) &&
                   !SegmentIntersectsObstacles(mid, end, obstacles, netId);
        }

        private static bool SegmentIntersectsObstacles(Vector2 start, Vector2 end, List<Rect> obstacles, string netId = null)
        {
            // If diagonal, split and check L-shape
            if (!Mathf.Approximately(start.x, end.x) && !Mathf.Approximately(start.y, end.y))
            {
                bool horizontalFirst = (StableHash(netId ?? string.Empty) & 1u) == 0u;
                Vector2 corner = horizontalFirst ? new Vector2(end.x, start.y) : new Vector2(start.x, end.y);
                return SegmentIntersectsObstacles(start, corner, obstacles, netId) ||
                       SegmentIntersectsObstacles(corner, end, obstacles, netId);
            }

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
            if (_pinVisuals.ContainsKey(node))
            {
                var pinCenter = point;
                var side = GetPinSide(rect, pinCenter);
                int laneIndex = GetPinLaneIndex(compId, side, node, rect);
                float laneOffset = laneIndex * WireLaneSpacing;
                float baseOffset = WireObstaclePadding + WireExitPadding + laneOffset;
                var anchor = pinCenter;
                switch (side)
                {
                    case PinSide.Left:
                        anchor.x = rect.xMin - baseOffset;
                        break;
                    case PinSide.Right:
                        anchor.x = rect.xMax + baseOffset;
                        break;
                    case PinSide.Top:
                        anchor.y = rect.yMin - baseOffset;
                        break;
                    case PinSide.Bottom:
                        anchor.y = rect.yMax + baseOffset;
                        break;
                }

                anchor.x = Mathf.Round(anchor.x / GridSnap) * GridSnap;
                anchor.y = Mathf.Round(anchor.y / GridSnap) * GridSnap;
                var pinBoardRect = GetBoardRect();
                if (pinBoardRect.width > 0f && pinBoardRect.height > 0f)
                {
                    anchor.x = Mathf.Clamp(anchor.x, pinBoardRect.xMin, pinBoardRect.xMax);
                    anchor.y = Mathf.Clamp(anchor.y, pinBoardRect.yMin, pinBoardRect.yMax);
                }
                return anchor;
            }
            if (!rect.Contains(point)) return point;

            float leftDist = point.x - rect.xMin;
            float rightDist = rect.xMax - point.x;
            float topDist = point.y - rect.yMin;
            float bottomDist = rect.yMax - point.y;

            float min = Mathf.Min(leftDist, rightDist, topDist, bottomDist);
            float offset = WireObstaclePadding + WireExitPadding;
            if (min == leftDist)
            {
                point.x = rect.xMin - offset;
            }
            else if (min == rightDist)
            {
                point.x = rect.xMax + offset;
            }
            else if (min == topDist)
            {
                point.y = rect.yMin - offset;
            }
            else
            {
                point.y = rect.yMax + offset;
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

        private enum PinSide
        {
            Left,
            Right,
            Top,
            Bottom
        }

        private static PinSide GetPinSide(Rect rect, Vector2 point)
        {
            float leftDist = Mathf.Abs(point.x - rect.xMin);
            float rightDist = Mathf.Abs(rect.xMax - point.x);
            float topDist = Mathf.Abs(point.y - rect.yMin);
            float bottomDist = Mathf.Abs(rect.yMax - point.y);

            float min = Mathf.Min(leftDist, rightDist, topDist, bottomDist);
            if (min == leftDist) return PinSide.Left;
            if (min == rightDist) return PinSide.Right;
            if (min == topDist) return PinSide.Top;
            return PinSide.Bottom;
        }

        private int GetPinLaneIndex(string componentId, PinSide side, string nodeKey, Rect compRect)
        {
            if (string.IsNullOrWhiteSpace(componentId) || _boardView == null) return 0;
            var candidates = new List<(string node, float coord)>();
            string prefix = componentId + ".";
            foreach (var kvp in _pinVisuals)
            {
                if (!kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var pinEl = kvp.Value;
                if (pinEl == null) continue;
                var center = _boardView.WorldToLocal(pinEl.worldBound.center);
                var pinSide = GetPinSide(compRect, center);
                if (pinSide != side) continue;
                float coord = (side == PinSide.Left || side == PinSide.Right) ? center.y : center.x;
                candidates.Add((kvp.Key, coord));
            }

            if (candidates.Count == 0) return 0;
            candidates.Sort((a, b) => a.coord.CompareTo(b.coord));
            for (int i = 0; i < candidates.Count; i++)
            {
                if (string.Equals(candidates[i].node, nodeKey, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return 0;
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
                    // Return local rect (0,0 based) because all component positions are relative to _boardView
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
            if (_boardView != null)
            {
                foreach (var pinEl in _pinVisuals.Values)
                {
                    if (pinEl == null) continue;
                    var world = pinEl.worldBound;
                    if (world.width <= 0f || world.height <= 0f) continue;
                    var min = _boardView.WorldToLocal(new Vector2(world.xMin, world.yMin));
                    var max = _boardView.WorldToLocal(new Vector2(world.xMax, world.yMax));
                    var rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                    var inflated = new Rect(
                        rect.x - PinObstaclePadding,
                        rect.y - PinObstaclePadding,
                        rect.width + PinObstaclePadding * 2f,
                        rect.height + PinObstaclePadding * 2f
                    );
                    obstacles.Add(inflated);
                }
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

            // Connect pin to first grid point
            if ((startPin - points[0]).sqrMagnitude > 0.25f)
            {
                AddWireSegment(startPin, points[0], netId);
                router?.MarkOccupancy(startPin, points[0], netId);
            }

            // Draw each path segment exactly as the router planned
            for (int i = 1; i < points.Count; i++)
            {
                AddWireSegment(points[i - 1], points[i], netId);
                router?.MarkOccupancy(points[i - 1], points[i], netId);
            }

            // Connect last grid point to pin
            if ((endPin - points[points.Count - 1]).sqrMagnitude > 0.25f)
            {
                AddWireSegment(points[points.Count - 1], endPin, netId);
                router?.MarkOccupancy(points[points.Count - 1], endPin, netId);
            }
        }

        private Dictionary<string, int> AssignNetPaletteIndices(IEnumerable<string> netIds, Dictionary<string, HashSet<string>> conflicts)
        {
            var ordered = netIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(id => conflicts != null && conflicts.TryGetValue(id, out var neighbors) ? neighbors.Count : 0)
                .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var paletteMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var netId in ordered)
            {
                int start = (int)(StableHash(netId) % (uint)WirePalette.Length);
                int chosen = start;
                float bestScore = float.NegativeInfinity;
                bool hasAssignedNeighbor = HasAssignedNeighbor(netId, paletteMap, conflicts);

                for (int i = 0; i < WirePalette.Length; i++)
                {
                    int idx = (start + i) % WirePalette.Length;
                    if (HasColorConflict(netId, idx, paletteMap, conflicts)) continue;
                    float score = hasAssignedNeighbor ? GetPaletteContrastScore(netId, idx, paletteMap, conflicts) : 0f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        chosen = idx;
                    }
                }

                if (bestScore == float.NegativeInfinity)
                {
                    for (int i = 0; i < WirePalette.Length; i++)
                    {
                        int idx = (start + i) % WirePalette.Length;
                        float score = hasAssignedNeighbor ? GetPaletteContrastScore(netId, idx, paletteMap, conflicts) : 0f;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            chosen = idx;
                        }
                    }
                }

                paletteMap[netId] = chosen;
            }
            return paletteMap;
        }

        private static bool HasAssignedNeighbor(string netId, Dictionary<string, int> paletteMap, Dictionary<string, HashSet<string>> conflicts)
        {
            if (paletteMap == null || conflicts == null) return false;
            if (!conflicts.TryGetValue(netId, out var neighbors) || neighbors.Count == 0) return false;
            foreach (var neighbor in neighbors)
            {
                if (paletteMap.ContainsKey(neighbor)) return true;
            }
            return false;
        }

        private float GetPaletteContrastScore(string netId, int paletteIndex, Dictionary<string, int> paletteMap, Dictionary<string, HashSet<string>> conflicts)
        {
            if (paletteMap == null || conflicts == null) return 0f;
            if (!conflicts.TryGetValue(netId, out var neighbors) || neighbors.Count == 0) return 0f;

            float minDistance = float.PositiveInfinity;
            bool hasNeighbor = false;
            foreach (var neighbor in neighbors)
            {
                if (!paletteMap.TryGetValue(neighbor, out var neighborIndex)) continue;
                hasNeighbor = true;
                float distance = GetColorDistance(WirePalette[paletteIndex], WirePalette[neighborIndex]);
                if (distance < minDistance) minDistance = distance;
            }

            return hasNeighbor ? minDistance : 0f;
        }

        private static float GetColorDistance(Color a, Color b)
        {
            Color.RGBToHSV(a, out var h1, out var s1, out var v1);
            Color.RGBToHSV(b, out var h2, out var s2, out var v2);
            float hue = Mathf.Abs(h1 - h2);
            hue = Mathf.Min(hue, 1f - hue);
            float sat = Mathf.Abs(s1 - s2);
            float val = Mathf.Abs(v1 - v2);
            return (hue * 0.7f) + (sat * 0.2f) + (val * 0.1f);
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
                if (float.TryParse(xRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    float.TryParse(yRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    return new Vector2(x, y);
                }
            }
            return Vector2.zero;
        }

        private bool TryGetStoredPosition(ComponentSpec comp, out Vector2 pos)
        {
            pos = Vector2.zero;
            if (comp == null || comp.Properties == null) return false;
            if (comp.Properties.TryGetValue("posX", out var xRaw) &&
                comp.Properties.TryGetValue("posY", out var yRaw) &&
                float.TryParse(xRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(yRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                pos = new Vector2(x, y);
                return true;
            }
            return false;
        }

        private void SetComponentPosition(ComponentSpec spec, Vector2 pos, bool suppressRebuild = false)
        {
            EnsureComponentProperties(spec);
            var size = GetComponentSize(string.IsNullOrWhiteSpace(spec.Type) ? string.Empty : spec.Type);
            pos = ClampToViewport(pos, size);
            pos = ClampToBoard(pos, size);
            spec.Properties["posX"] = pos.x.ToString("F2", CultureInfo.InvariantCulture);
            spec.Properties["posY"] = pos.y.ToString("F2", CultureInfo.InvariantCulture);
            spec.Properties["sizeX"] = size.x.ToString("F2", CultureInfo.InvariantCulture);
            spec.Properties["sizeY"] = size.y.ToString("F2", CultureInfo.InvariantCulture);
            _componentPositions[spec.Id] = pos;
            if (!suppressRebuild)
            {
                RequestCircuit3DRebuild();
            }
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

            float maxXFallback = Mathf.Max(0f, BoardWorldWidth - size.x);
            float maxYFallback = Mathf.Max(0f, BoardWorldHeight - size.y);
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
                    Focus3DOnComponent(comp.Id);
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

        private void Focus3DOnComponent(string componentId)
        {
            if (string.IsNullOrWhiteSpace(componentId) || _circuit3DRenderer == null) return;
            _last3dPickedComponentId = componentId;
            _circuit3DRenderer.FocusOnComponent(componentId);
            if (_circuit3DFollowToggle != null && _circuit3DFollowToggle.value)
            {
                _circuit3DRenderer.SetFollowComponent(componentId, true);
            }
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
                DeleteSelectedComponents();
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
            RequestCircuit3DRebuild();
        }

        private void DeleteNet(NetSpec net)
        {
            if (net == null || _currentCircuit == null || _currentCircuit.Nets == null) return;
            _currentCircuit.Nets.Remove(net);
            UpdateWireLayer();
            PopulateProjectTree();
            UpdateErrorCount();
            RequestCircuit3DRebuild();
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
                LogNoStack(LogType.Log, "[CircuitStudio] DRC PASS");
            }
            else if (errorCount == 0)
            {
                LogNoStack(LogType.Log, $"[CircuitStudio] DRC PASS ({warnCount} warnings)");
            }
            else
            {
                LogNoStack(LogType.Log, $"[CircuitStudio] DRC FAIL ({errorCount} errors, {warnCount} warnings)");
            }
            foreach (var issue in issues)
            {
                string prefix = $"[CircuitStudio] {issue.Code} {issue.Message}";
                if (issue.Severity == DrcSeverity.Error)
                {
                    LogNoStack(LogType.Error, prefix);
                }
                else if (issue.Severity == DrcSeverity.Warning)
                {
                    LogNoStack(LogType.Warning, prefix);
                }
                else
                {
                    LogNoStack(LogType.Log, prefix);
                }
            }
            UpdateErrorCount();
        }

        private static void LogNoStack(LogType type, string message)
        {
            var prev = Application.GetStackTraceLogType(type);
            Application.SetStackTraceLogType(type, StackTraceLogType.None);
            Debug.unityLogger.Log(type, message);
            Application.SetStackTraceLogType(type, prev);
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
            RecordStateForUndo();
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
            RequestCircuit3DRebuild();
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
                        if (_selectedComponentIds.Count > 1 && _selectedComponentIds.Contains(spec.Id))
                        {
                            BeginGroupMove(el, spec, catalogItem, e);
                        }
                        else
                        {
                            BeginComponentMove(el, spec, catalogItem, e);
                        }
                        handled = true;
                    }
                    else
                    {
                        if (_currentTool == ToolMode.Wire)
                        {
                            _lastCanvasWorldPos = e.position;
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
                var resolved = ResolvePlacement(p, size, spec.Id);
                if (IsOverlapping(resolved, size, spec.Id))
                {
                    // If still overlapping after search, snap back to last valid position
                    resolved = _moveLastValidPos;
                }
                else
                {
                    _moveLastValidPos = resolved;
                }

                el.style.left = resolved.x;
                el.style.top = resolved.y;
                SetComponentPosition(spec, resolved);
                UpdateTransformFields(resolved);
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
            RecordStateForUndo();
            SelectComponent(element, spec, catalogItem);
            _isMovingComponent = true;
            _movePointerId = evt.pointerId;
            _moveTarget = element;
            _moveTargetSpec = spec;
            var canvasLocal = _canvasView.WorldToLocal(evt.position);
            var boardPos = CanvasToBoard(canvasLocal);
            _moveStartPos = GetComponentPosition(spec.Id);
            _moveOffset = boardPos - GetComponentPosition(spec.Id);
            _moveLastValidPos = GetComponentPosition(spec.Id);
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
            float pad = ManualOverlapPadding;
            var rect = new Rect(pos.x - pad, pos.y - pad, size.x + pad * 2f, size.y + pad * 2f);
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
                var otherRect = new Rect(otherPos.x - pad, otherPos.y - pad, otherSize.x + pad * 2f, otherSize.y + pad * 2f);
                if (rect.Overlaps(otherRect))
                {
                    return true;
                }
            }
            return false;
        }

        private void BuildComponentVisual(VisualElement root, ComponentSpec spec, ComponentCatalog.Item item)
        {
            if (HasCustomLayout(item))
            {
                BuildCustomComponentVisual(root, spec, item);
                return;
            }
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

        private static bool HasCustomLayout(ComponentCatalog.Item item)
        {
            return (item.PinLayout != null && item.PinLayout.Count > 0) ||
                   (item.Labels != null && item.Labels.Count > 0) ||
                   (item.Shapes != null && item.Shapes.Count > 0);
        }

        private void BuildCustomComponentVisual(VisualElement root, ComponentSpec spec, ComponentCatalog.Item item)
        {
            var size = GetComponentSize(item);
            root.AddToClassList("custom-component");

            var body = new VisualElement();
            body.AddToClassList("custom-component-body");
            body.style.position = Position.Relative;
            body.style.width = size.x;
            body.style.height = size.y;
            root.Add(body);

            var pins = item.PinLayout ?? new List<ComponentCatalog.PinLayout>();
            foreach (var pin in pins)
            {
                if (string.IsNullOrWhiteSpace(pin.Name)) continue;
                var position = ResolveLayoutPosition(pin.Position, size);
                var dot = CreatePinDot(spec.Id, pin.Name);
                dot.AddToClassList("custom-pin-dot");
                dot.style.position = Position.Absolute;
                dot.style.left = position.x - 5f;
                dot.style.top = position.y - 5f;
                body.Add(dot);

                string labelText = string.IsNullOrWhiteSpace(pin.Label) ? pin.Name : pin.Label;
                if (!string.IsNullOrWhiteSpace(labelText))
                {
                    var label = new Label(labelText);
                    label.AddToClassList("custom-pin-label");
                    label.style.position = Position.Absolute;
                    label.style.left = position.x + pin.LabelOffset.x;
                    label.style.top = position.y + pin.LabelOffset.y;
                    if (pin.LabelSize > 0) label.style.fontSize = pin.LabelSize;
                    body.Add(label);
                }
            }

            if (item.Labels != null && item.Labels.Count > 0)
            {
                foreach (var entry in item.Labels)
                {
                    if (string.IsNullOrWhiteSpace(entry.Text)) continue;
                    var labelPos = ResolveLayoutPosition(entry.Position, size);
                    var label = new Label(ResolveLabelText(entry.Text, spec, item));
                    label.AddToClassList("custom-component-label");
                    label.style.position = Position.Absolute;
                    label.style.left = labelPos.x;
                    label.style.top = labelPos.y;
                    if (entry.Size > 0) label.style.fontSize = entry.Size;
                    ApplyLabelAlignment(label, entry.Align);
                    body.Add(label);
                }
            }
            bool hasShapes = item.Shapes != null && item.Shapes.Count > 0;
            if (hasShapes)
            {
                foreach (var shape in item.Shapes)
                {
                    if (string.IsNullOrWhiteSpace(shape.Type)) continue;
                    var shapePos = ResolveLayoutPosition(shape.Position, size);
                    var shapeSize = ResolveLayoutSize(new Vector2(shape.Width, shape.Height), size);
                    var element = new LayoutShapeVisual(shape);
                    element.AddToClassList("custom-shape");
                    element.style.position = Position.Absolute;
                    element.style.left = shapePos.x - shapeSize.x * 0.5f;
                    element.style.top = shapePos.y - shapeSize.y * 0.5f;
                    element.style.width = Mathf.Max(6f, shapeSize.x);
                    element.style.height = Mathf.Max(6f, shapeSize.y);
                    body.Add(element);
                }
            }
            else
            {
                var defaultLabel = new Label(spec.Id);
                defaultLabel.AddToClassList("custom-component-label");
                defaultLabel.style.position = Position.Absolute;
                defaultLabel.style.left = 0f;
                defaultLabel.style.top = 0f;
                defaultLabel.style.width = size.x;
                defaultLabel.style.height = size.y;
                defaultLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                body.Add(defaultLabel);
            }
        }

        private static Vector2 ResolveLayoutSize(Vector2 raw, Vector2 size)
        {
            if (raw.x >= 0f && raw.x <= 1f && raw.y >= 0f && raw.y <= 1f)
            {
                return new Vector2(raw.x * size.x, raw.y * size.y);
            }
            return raw;
        }

        private static Vector2 ResolveLayoutPosition(Vector2 raw, Vector2 size)
        {
            if (raw.x >= 0f && raw.x <= 1f && raw.y >= 0f && raw.y <= 1f)
            {
                return new Vector2(raw.x * size.x, raw.y * size.y);
            }
            return raw;
        }

        private static string ResolveLabelText(string template, ComponentSpec spec, ComponentCatalog.Item item)
        {
            if (string.IsNullOrWhiteSpace(template)) return string.Empty;
            string text = template;
            text = text.Replace("{id}", spec?.Id ?? string.Empty);
            text = text.Replace("{type}", spec?.Type ?? string.Empty);
            text = text.Replace("{name}", item.Name ?? string.Empty);
            return text;
        }

        private static void ApplyLabelAlignment(Label label, string alignment)
        {
            if (label == null || string.IsNullOrWhiteSpace(alignment)) return;
            switch (alignment.Trim().ToLowerInvariant())
            {
                case "left":
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    break;
                case "right":
                    label.style.unityTextAlign = TextAnchor.MiddleRight;
                    break;
                case "center":
                    label.style.unityTextAlign = TextAnchor.MiddleCenter;
                    break;
            }
        }

        private sealed class LayoutShapeVisual : VisualElement
        {
            private readonly ComponentCatalog.ShapeLayout _shape;
            private readonly Label _textLabel;

            public LayoutShapeVisual(ComponentCatalog.ShapeLayout shape)
            {
                _shape = shape;
                pickingMode = PickingMode.Ignore;
                generateVisualContent += OnGenerateVisualContent;

                if (string.Equals(shape.Type, "Text", StringComparison.OrdinalIgnoreCase))
                {
                    _textLabel = new Label(shape.Text ?? string.Empty);
                    _textLabel.AddToClassList("custom-shape-text");
                    _textLabel.style.flexGrow = 1f;
                    _textLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    Add(_textLabel);
                }
            }

            private void OnGenerateVisualContent(MeshGenerationContext ctx)
            {
                var painter = ctx.painter2D;
                var rect = contentRect;
                painter.lineWidth = 1f;
                painter.strokeColor = new Color(0.4f, 0.75f, 1f, 0.85f);
                painter.fillColor = new Color(0.12f, 0.18f, 0.28f, 0.25f);

                string type = _shape.Type ?? string.Empty;
                if (type.Equals("Text", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                if (type.Equals("Line", StringComparison.OrdinalIgnoreCase))
                {
                    float midY = rect.y + rect.height * 0.5f;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(rect.x, midY));
                    painter.LineTo(new Vector2(rect.x + rect.width, midY));
                    painter.Stroke();
                    return;
                }
                if (type.Equals("Triangle", StringComparison.OrdinalIgnoreCase))
                {
                    var p1 = new Vector2(rect.x + rect.width * 0.5f, rect.y);
                    var p2 = new Vector2(rect.x + rect.width, rect.y + rect.height);
                    var p3 = new Vector2(rect.x, rect.y + rect.height);
                    painter.BeginPath();
                    painter.MoveTo(p1);
                    painter.LineTo(p2);
                    painter.LineTo(p3);
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                    return;
                }
                if (type.Equals("Circle", StringComparison.OrdinalIgnoreCase))
                {
                    var center = rect.center;
                    float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
                    int segments = 32;
                    painter.BeginPath();
                    painter.MoveTo(center + new Vector2(radius, 0f));
                    for (int i = 1; i <= segments; i++)
                    {
                        float angle = (Mathf.PI * 2f) * (i / (float)segments);
                        painter.LineTo(center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
                    }
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                    return;
                }

                painter.BeginPath();
                painter.MoveTo(new Vector2(rect.x, rect.y));
                painter.LineTo(new Vector2(rect.x + rect.width, rect.y));
                painter.LineTo(new Vector2(rect.x + rect.width, rect.y + rect.height));
                painter.LineTo(new Vector2(rect.x, rect.y + rect.height));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();
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
            dot.pickingMode = PickingMode.Position;
            string key = $"{componentId}.{pinName}";
            dot.name = key;
            _pinVisuals[key] = dot;
            dot.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (_currentTool != ToolMode.Wire) return;
                _lastCanvasWorldPos = evt.position;
                HandleWirePinClick(componentId, pinName, evt.clickCount);
                evt.StopImmediatePropagation();
                evt.StopPropagation();
            });
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

        private void SelectComponent(VisualElement visual, ComponentSpec spec, ComponentCatalog.Item catalogInfo, bool additive = false)
        {
            if (!additive)
            {
                ClearSelection();
                ResetPropertyBindings();
            }
            AddComponentToSelection(visual, spec, catalogInfo, true);
        }

        private void ClearSelection()
        {
            if (_selectedComponentIds.Count > 0)
            {
                foreach (var id in _selectedComponentIds.ToList())
                {
                    if (_componentVisuals.TryGetValue(id, out var visual))
                    {
                        visual?.RemoveFromClassList("selected");
                    }
                }
                _selectedComponentIds.Clear();
            }
            _selectedComponent = null;
            _selectedVisual = null;
            _selectedCatalogItem = default;
            _selectedComponentSize = new Vector2(ComponentWidth, ComponentHeight);
        }

        private void AddComponentToSelection(VisualElement visual, ComponentSpec spec, ComponentCatalog.Item catalogInfo, bool makePrimary)
        {
            if (visual == null || spec == null) return;
            if (!_selectedComponentIds.Contains(spec.Id))
            {
                _selectedComponentIds.Add(spec.Id);
                visual.AddToClassList("selected");
            }
            if (makePrimary || _selectedComponent == null)
            {
                _selectedComponent = spec;
                _selectedVisual = visual;
                _selectedCatalogItem = catalogInfo;
                _selectedComponentSize = GetComponentSize(catalogInfo);
                UpdatePropertiesPanel(spec, catalogInfo, visual);
            }
            if (_circuit3DFollowToggle != null && _circuit3DFollowToggle.value && spec != null)
            {
                _circuit3DRenderer?.SetFollowComponent(spec.Id, true);
            }
        }

        private void RecordStateForUndo(bool clearRedo = true)
        {
            if (_isRestoringState || _currentCircuit == null) return;
            var clone = CloneCircuitSpec(_currentCircuit);
            _undoHistory.Add(clone);
            if (_undoHistory.Count > MaxHistoryEntries)
            {
                _undoHistory.RemoveAt(0);
            }
            if (clearRedo)
            {
                _redoHistory.Clear();
            }
        }

        private void SaveSnapshotForRedo()
        {
            if (_currentCircuit == null) return;
            _redoHistory.Add(CloneCircuitSpec(_currentCircuit));
            if (_redoHistory.Count > MaxHistoryEntries)
            {
                _redoHistory.RemoveAt(0);
            }
        }

        private void Undo()
        {
            if (_undoHistory.Count == 0 || _currentCircuit == null) return;
            var snapshot = _undoHistory[_undoHistory.Count - 1];
            _undoHistory.RemoveAt(_undoHistory.Count - 1);
            AddToHistory(_redoHistory, CloneCircuitSpec(_currentCircuit));
            RestoreCircuitFromSnapshot(snapshot);
        }

        private void Redo()
        {
            if (_redoHistory.Count == 0 || _currentCircuit == null) return;
            var snapshot = _redoHistory[_redoHistory.Count - 1];
            _redoHistory.RemoveAt(_redoHistory.Count - 1);
            AddToHistory(_undoHistory, CloneCircuitSpec(_currentCircuit));
            RestoreCircuitFromSnapshot(snapshot);
        }

        private void RestoreCircuitFromSnapshot(CircuitSpec snapshot)
        {
            if (snapshot == null) return;
            _isRestoringState = true;
            _currentCircuit = CloneCircuitSpec(snapshot);
            NormalizeCircuit();
            RebuildPinUsage();
            RefreshCanvas();
            PopulateProjectTree();
            UpdateErrorCount();
            UpdateWireLayer();
            RequestCircuit3DRebuild();
            _isRestoringState = false;
        }

        private void ResetHistory()
        {
            _undoHistory.Clear();
            _redoHistory.Clear();
            if (_currentCircuit != null)
            {
                _undoHistory.Add(CloneCircuitSpec(_currentCircuit));
            }
        }

        private void AddToHistory(List<CircuitSpec> history, CircuitSpec snapshot)
        {
            if (snapshot == null) return;
            history.Add(snapshot);
            if (history.Count > MaxHistoryEntries)
            {
                history.RemoveAt(0);
            }
        }

        private static CircuitSpec CloneCircuitSpec(CircuitSpec spec)
        {
            if (spec == null) return null;
            var clone = new CircuitSpec
            {
                Id = spec.Id,
                Components = spec.Components?.Select(CloneComponent).ToList(),
                Nets = spec.Nets?.Select(net => new NetSpec
                {
                    Id = net.Id,
                    Nodes = net.Nodes != null ? new List<string>(net.Nodes) : null
                }).ToList()
            };
            return clone;
        }

        private static ComponentSpec CloneComponent(ComponentSpec comp)
        {
            if (comp == null) return null;
            var clone = new ComponentSpec
            {
                Id = comp.Id,
                Type = comp.Type,
                Properties = comp.Properties != null
                    ? comp.Properties.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
                    : null
            };
            return clone;
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
                if (!TryGetNearestPin(spec.Id, _lastCanvasWorldPos, out _wireStartPin))
                {
                    return;
                }
                if (_pinVisuals.TryGetValue($"{spec.Id}.{_wireStartPin}", out var pinEl))
                {
                    var size = GetComponentSize(string.IsNullOrWhiteSpace(spec.Type) ? string.Empty : spec.Type);
                    var pos = GetComponentPosition(spec.Id);
                    var rect = new Rect(pos.x, pos.y, size.x, size.y);
                    var center = _boardView.WorldToLocal(pinEl.worldBound.center);
                    _wireStartSide = GetPinSide(rect, center);
                }
                _wireStartComponent = spec;
                UpdateWirePreview(_lastCanvasWorldPos);
                return;
            }

            if (_wireStartComponent == spec)
            {
                if (TryGetNearestPin(spec.Id, _lastCanvasWorldPos, out var samePin) &&
                    string.Equals(samePin, _wireStartPin, StringComparison.OrdinalIgnoreCase))
                {
                    _wireStartComponent = null;
                    _wireStartPin = string.Empty;
                    ClearWirePreview();
                    return;
                }
            }

            string endPin;
            if (!TryGetNearestPin(spec.Id, _lastCanvasWorldPos, out endPin))
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(_wireStartPin) || string.IsNullOrWhiteSpace(endPin))
            {
                _wireStartComponent = null;
                _wireStartPin = string.Empty;
                return;
            }

            CreateNet(_wireStartComponent, _wireStartPin, spec, endPin);
            _wireStartComponent = null;
            _wireStartPin = string.Empty;
            ClearWirePreview();
        }

        private bool TryGetNearestPin(string componentId, Vector2 worldPos, out string pinName)
        {
            pinName = null;
            if (string.IsNullOrWhiteSpace(componentId)) return false;
            if (_pinVisuals.Count == 0) return false;
            float best = 24f * 24f;
            foreach (var kvp in _pinVisuals)
            {
                if (!kvp.Key.StartsWith(componentId + ".", StringComparison.OrdinalIgnoreCase)) continue;
                var pinEl = kvp.Value;
                if (pinEl == null) continue;
                var center = pinEl.worldBound.center;
                float dx = center.x - worldPos.x;
                float dy = center.y - worldPos.y;
                float dist = (dx * dx) + (dy * dy);
                if (dist < best)
                {
                    best = dist;
                    pinName = kvp.Key.Substring(componentId.Length + 1);
                }
            }
            return !string.IsNullOrWhiteSpace(pinName);
        }

        private void HandleWirePinClick(string componentId, string pinName, int clickCount)
        {
            if (_currentCircuit == null) return;
            var spec = _currentCircuit.Components.FirstOrDefault(c => c.Id == componentId);
            if (spec == null) return;
            string node = $"{componentId}.{pinName}";

            if (clickCount >= 2)
            {
                if (IsPinConnected(node))
                {
                    RemoveConnectionsForPin(componentId, pinName);
                }
                CancelWireMode();
                return;
            }

            if (_wireStartComponent == null)
            {
                _wireStartComponent = spec;
                _wireStartPin = pinName;
                if (_pinVisuals.TryGetValue($"{spec.Id}.{pinName}", out var pinEl))
                {
                    var size = GetComponentSize(string.IsNullOrWhiteSpace(spec.Type) ? string.Empty : spec.Type);
                    var pos = GetComponentPosition(spec.Id);
                    var rect = new Rect(pos.x, pos.y, size.x, size.y);
                    var center = _boardView.WorldToLocal(pinEl.worldBound.center);
                    _wireStartSide = GetPinSide(rect, center);
                }
                var previewPos = _lastCanvasWorldPos;
                if (previewPos == Vector2.zero && _canvasView != null)
                {
                    previewPos = _canvasView.worldBound.center;
                }
                UpdateWirePreview(previewPos);
                return;
            }

            if (_wireStartComponent == spec && string.Equals(_wireStartPin, pinName, StringComparison.OrdinalIgnoreCase))
            {
                CancelWireMode();
                return;
            }

            CreateNet(_wireStartComponent, _wireStartPin, spec, pinName);
            _wireStartComponent = null;
            _wireStartPin = string.Empty;
            ClearWirePreview();
        }

        private bool IsPinConnected(string node)
        {
            if (_currentCircuit?.Nets == null || string.IsNullOrWhiteSpace(node)) return false;
            foreach (var net in _currentCircuit.Nets)
            {
                if (net?.Nodes == null) continue;
                if (net.Nodes.Contains(node)) return true;
            }
            return false;
        }

        private void RemoveConnectionsForPin(string componentId, string pinName)
        {
            if (_currentCircuit?.Nets == null) return;
            string node = $"{componentId}.{pinName}";
            bool changed = false;
            for (int i = _currentCircuit.Nets.Count - 1; i >= 0; i--)
            {
                var net = _currentCircuit.Nets[i];
                if (net?.Nodes == null) continue;
                if (net.Nodes.Remove(node))
                {
                    changed = true;
                }
                if (net.Nodes.Count < 2)
                {
                    _currentCircuit.Nets.RemoveAt(i);
                }
            }

            if (!changed) return;
            RebuildPinUsage();
            UpdateWireLayer();
            UpdateErrorCount();
            PopulateProjectTree();
            if (_selectedComponent != null && string.Equals(_selectedComponent.Id, componentId, StringComparison.OrdinalIgnoreCase))
            {
                var item = ResolveCatalogItem(_selectedComponent);
                if (_selectedVisual != null && item.Type != null)
                {
                    UpdatePropertiesPanel(_selectedComponent, item, _selectedVisual);
                }
            }
        }

        private void CancelWireMode()
        {
            _wireStartComponent = null;
            _wireStartPin = string.Empty;
            ClearWirePreview();
        }

        private void UpdateWirePreview(Vector2 worldPos)
        {
            if (_canvasView == null) return;
            EnsureWirePreviewLayer();
            if (_wirePreviewLayer == null) return;
            if (_wireStartComponent == null || string.IsNullOrWhiteSpace(_wireStartPin))
            {
                ClearWirePreview();
                return;
            }

            string startNode = $"{_wireStartComponent.Id}.{_wireStartPin}";
            var rawStart = GetNodePosition(startNode);
            if (!rawStart.HasValue)
            {
                ClearWirePreview();
                return;
            }
            var startAnchor = GetWireAnchor(startNode, rawStart);
            if (!startAnchor.HasValue)
            {
                ClearWirePreview();
                return;
            }

            var canvasLocal = _canvasView.WorldToLocal(worldPos);
            var end = CanvasToBoard(canvasLocal);
            var boardRect = GetBoardRect();
            if (boardRect.width > 0f && boardRect.height > 0f)
            {
                end.x = Mathf.Clamp(end.x, boardRect.xMin, boardRect.xMax);
                end.y = Mathf.Clamp(end.y, boardRect.yMin, boardRect.yMax);
            }
            end.x = Mathf.Round(end.x / GridSnap) * GridSnap;
            end.y = Mathf.Round(end.y / GridSnap) * GridSnap;

            _wirePreviewSegments.Clear();
            AddPreviewSegments(startAnchor.Value, end);
            ((WireLayer)_wirePreviewLayer).SetSegments(_wirePreviewSegments);
            _wirePreviewLayer.BringToFront();
        }

        private void ClearWirePreview()
        {
            if (_wirePreviewSegments.Count == 0) return;
            _wirePreviewSegments.Clear();
            if (_wirePreviewLayer != null)
            {
                ((WireLayer)_wirePreviewLayer).SetSegments(_wirePreviewSegments);
            }
        }

        private void AddPreviewSegments(Vector2 start, Vector2 end)
        {
            if ((end - start).sqrMagnitude < 0.25f) return;

            float stubLen = 20f;
            Vector2 stubEnd = start;
            switch (_wireStartSide)
            {
                case PinSide.Left: stubEnd.x -= stubLen; break;
                case PinSide.Right: stubEnd.x += stubLen; break;
                case PinSide.Top: stubEnd.y -= stubLen; break;
                case PinSide.Bottom: stubEnd.y += stubLen; break;
            }
            AddPreviewSegmentRaw(start, stubEnd);
            start = stubEnd;

            if (Mathf.Approximately(start.x, end.x) || Mathf.Approximately(start.y, end.y))
            {
                AddPreviewSegmentRaw(start, end);
                return;
            }

            bool horizontalFirst = false;
            if (_wireStartSide == PinSide.Left || _wireStartSide == PinSide.Right)
            {
                if (_wireStartSide == PinSide.Right)
                    horizontalFirst = end.x > start.x;
                else
                    horizontalFirst = end.x < start.x;
            }
            else
            {
                if (_wireStartSide == PinSide.Bottom)
                    horizontalFirst = !(end.y > start.y);
                else
                    horizontalFirst = !(end.y < start.y);
            }

            Vector2 mid = horizontalFirst
                ? new Vector2(end.x, start.y)
                : new Vector2(start.x, end.y);
            AddPreviewSegmentRaw(start, mid);
            AddPreviewSegmentRaw(mid, end);
        }

        private void AddPreviewSegmentRaw(Vector2 start, Vector2 end)
        {
            if ((end - start).sqrMagnitude < 0.25f) return;
            _wirePreviewSegments.Add(new WireSegment
            {
                Start = start,
                End = end,
                NetId = string.Empty,
                Color = WirePreviewColor
            });
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

        private bool HandleEscapeKey()
        {
            bool handled = false;

            if (_helpOverlay != null && _helpOverlay.resolvedStyle.display != DisplayStyle.None)
            {
                HideHelpOverlay();
                handled = true;
            }
            if (_preferencesOverlay != null && _preferencesOverlay.resolvedStyle.display != DisplayStyle.None)
            {
                HidePreferences();
                handled = true;
            }
            if (_netEditorOverlay != null && _netEditorOverlay.resolvedStyle.display != DisplayStyle.None)
            {
                HideNetEditor();
                handled = true;
            }
            if (_circuit3DHelpTooltip != null && _circuit3DHelpTooltip.style.display == DisplayStyle.Flex)
            {
                _circuit3DHelpTooltip.style.display = DisplayStyle.None;
                handled = true;
            }
            if (_isBoxSelecting)
            {
                CancelBoxSelection();
                handled = true;
            }
            if (_isMovingSelection)
            {
                CancelGroupMove();
                handled = true;
            }
            if (_isMovingComponent)
            {
                CancelComponentMove();
                handled = true;
            }
            if (_isPanningCanvas)
            {
                CancelCanvasPan();
                handled = true;
            }
            if (_currentTool == ToolMode.Wire)
            {
                CancelWireMode();
                handled = true;
            }
            if (_isDragging)
            {
                CancelLibraryDrag();
                handled = true;
            }

            return handled;
        }

        private void CancelBoxSelection()
        {
            _isBoxSelecting = false;
            _boxSelectionPointerId = -1;
            HideSelectionBox();
        }

        private void CancelGroupMove()
        {
            if (!_isMovingSelection || _currentCircuit == null) return;
            foreach (var kvp in _groupMoveOriginalPositions)
            {
                var comp = _currentCircuit.Components.FirstOrDefault(c => c != null && c.Id == kvp.Key);
                if (comp == null) continue;
                var pos = kvp.Value;
                if (_componentVisuals.TryGetValue(comp.Id, out var visual))
                {
                    visual.style.left = pos.x;
                    visual.style.top = pos.y;
                }
                SetComponentPosition(comp, pos, true);
            }
            if (_selectedComponent != null)
            {
                UpdateTransformFields(GetComponentPosition(_selectedComponent.Id));
            }
            _isMovingSelection = false;
            if (_canvasView != null && _groupMovePointerId != -1 && _canvasView.HasPointerCapture(_groupMovePointerId))
            {
                _canvasView.ReleasePointer(_groupMovePointerId);
            }
            _groupMovePointerId = -1;
            _groupMoveOriginalPositions.Clear();
            RequestWireUpdateThrottled();
            RequestCircuit3DRebuild();
        }

        private void CancelComponentMove()
        {
            if (!_isMovingComponent) return;
            _isMovingComponent = false;
            if (_moveTarget != null && _moveTarget.HasPointerCapture(_movePointerId))
            {
                _moveTarget.ReleasePointer(_movePointerId);
            }
            if (_moveTarget != null && _moveTargetSpec != null)
            {
                _moveTarget.style.left = _moveStartPos.x;
                _moveTarget.style.top = _moveStartPos.y;
                SetComponentPosition(_moveTargetSpec, _moveStartPos, true);
            }
            _movePointerId = -1;
            _moveTarget = null;
            _moveTargetSpec = null;
            UpdateTransformFields(_moveStartPos);
            RequestWireUpdateThrottled();
            RequestCircuit3DRebuild();
        }

        private void CancelCanvasPan()
        {
            if (!_isPanningCanvas) return;
            _isPanningCanvas = false;
            if (_canvasView != null && _panPointerId != -1 && _canvasView.HasPointerCapture(_panPointerId))
            {
                _canvasView.ReleasePointer(_panPointerId);
            }
            _panPointerId = -1;
        }

        private void CreateNet(ComponentSpec a, string pinA, ComponentSpec b, string pinB)
        {
            if (_currentCircuit == null) return;
            string nodeA = $"{a.Id}.{pinA}";
            string nodeB = $"{b.Id}.{pinB}";
            if (string.Equals(nodeA, nodeB, StringComparison.OrdinalIgnoreCase)) return;
            ConnectPins(nodeA, nodeB);
            UpdateWireLayer();
            UpdateErrorCount();
            PopulateProjectTree();
            if (_selectedVisual != null && _selectedComponent == a)
            {
                UpdatePropertiesPanel(a, ResolveCatalogItem(a), _selectedVisual);
            }
        }

        private void ConnectPins(string nodeA, string nodeB)
        {
            if (_currentCircuit == null) return;
            if (_currentCircuit.Nets == null) _currentCircuit.Nets = new List<NetSpec>();

            var netA = FindNetForNode(nodeA);
            var netB = FindNetForNode(nodeB);

            if (netA == null && netB == null)
            {
                var net = new NetSpec
                {
                    Id = $"NET_{_currentCircuit.Nets.Count + 1}",
                    Nodes = new List<string> { nodeA, nodeB }
                };
                _currentCircuit.Nets.Add(net);
            }
            else if (netA != null && netB == null)
            {
                AssignNodeToNet(nodeB, netA.Id);
            }
            else if (netA == null && netB != null)
            {
                AssignNodeToNet(nodeA, netB.Id);
            }
            else if (netA != null && netB != null)
            {
                if (string.Equals(netA.Id, netB.Id, StringComparison.OrdinalIgnoreCase))
                {
                    AssignNodeToNet(nodeA, netA.Id);
                    AssignNodeToNet(nodeB, netA.Id);
                }
                else
                {
                    var target = netA;
                    var source = netB;
                    if (string.Compare(target.Id, source.Id, StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        target = netB;
                        source = netA;
                    }
                    if (source.Nodes != null)
                    {
                        foreach (var node in source.Nodes.ToList())
                        {
                            AssignNodeToNet(node, target.Id);
                        }
                    }
                    _currentCircuit.Nets.Remove(source);
                }
            }

            RebuildPinUsage();
        }

        private NetSpec FindNetForNode(string node)
        {
            if (_currentCircuit?.Nets == null || string.IsNullOrWhiteSpace(node)) return null;
            foreach (var net in _currentCircuit.Nets)
            {
                if (net?.Nodes == null) continue;
                if (net.Nodes.Contains(node)) return net;
            }
            return null;
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
            if (item.Size2D.x > 0f && item.Size2D.y > 0f)
            {
                return item.Size2D;
            }
            return GetComponentSize(item.Type);
        }

        private Vector2 GetComponentSize(string type)
        {
            return CircuitLayoutSizing.GetComponentSize2D(type);
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
                ClearSelection();
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
                menu.AddItem("Center Selection", false, CenterCanvasOnSelection);
                menu.AddItem("Reset View", false, ResetCanvasView);
                menu.AddSeparator(string.Empty);
                menu.AddItem("Zoom In", false, () => ZoomCanvasStep(1.1f, GetCanvasViewCenterWorld()));
                menu.AddItem("Zoom Out", false, () => ZoomCanvasStep(0.9f, GetCanvasViewCenterWorld()));
                menu.AddSeparator(string.Empty);
                menu.AddItem("Copy", false, CopySelectionToClipboard);
                menu.AddItem("Paste", false, PasteClipboard);
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
                menu.AddItem("Center Selection", false, CenterCanvasOnSelection);
                menu.AddItem("Reset View", false, ResetCanvasView);
                menu.AddSeparator(string.Empty);
                menu.AddItem("Zoom In", false, () => ZoomCanvasStep(1.1f, GetCanvasViewCenterWorld()));
                menu.AddItem("Zoom Out", false, () => ZoomCanvasStep(0.9f, GetCanvasViewCenterWorld()));
                menu.AddSeparator(string.Empty);
                menu.AddItem("Copy", false, CopySelectionToClipboard);
                menu.AddItem("Paste", false, PasteClipboard);
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
                PinLayout = new List<ComponentCatalog.PinLayout>(),
                Labels = new List<ComponentCatalog.LabelLayout>(),
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
            if (_isMovingSelection && evt.pointerId == _groupMovePointerId)
            {
                HandleGroupMove(evt);
                evt.StopPropagation();
                return;
            }
            if (!_canvasView.worldBound.Contains(evt.position)) return;
            if (_isBoxSelecting && evt.pointerId == _boxSelectionPointerId)
            {
                var currentLocal = _canvasView.WorldToLocal(evt.position);
                UpdateSelectionBox(_boxSelectionStartLocal, currentLocal);
            }
            _lastCanvasWorldPos = evt.position;
            var canvasLocal = _canvasView.WorldToLocal(evt.position);
            var boardPos = CanvasToBoard(canvasLocal);
            UpdateCanvasHud(boardPos);
            if (_currentTool == ToolMode.Wire && _wireStartComponent != null && !string.IsNullOrWhiteSpace(_wireStartPin))
            {
                UpdateWirePreview(evt.position);
            }
            else
            {
                ClearWirePreview();
            }
        }

        private void OnCanvasBoxSelectionStart(PointerDownEvent evt)
        {
            if (evt.button != 0 || _canvasView == null) return;
            if (_currentTool != ToolMode.Select) return;
            if (!IsCanvasBackgroundHit(evt.position, evt.target as VisualElement)) return;
            _isBoxSelecting = true;
            _boxSelectionPointerId = evt.pointerId;
            _boxSelectionStartLocal = _canvasView.WorldToLocal(evt.position);
            _boxSelectionStartBoard = CanvasToBoard(_boxSelectionStartLocal);
            EnsureSelectionBox();
            UpdateSelectionBox(_boxSelectionStartLocal, _boxSelectionStartLocal);
        }

        private void OnCanvasPointerRelease(PointerUpEvent evt)
        {
            if (_isBoxSelecting && evt.pointerId == _boxSelectionPointerId)
            {
                if (_canvasView != null)
                {
                    var endLocal = _canvasView.WorldToLocal(evt.position);
                    var boardRect = BuildBoardSelectionRect(_boxSelectionStartBoard, CanvasToBoard(endLocal));
                    ApplyBoxSelection(boardRect);
                }
                _isBoxSelecting = false;
                _boxSelectionPointerId = -1;
                HideSelectionBox();
                evt.StopPropagation();
            }
            if (_isMovingSelection && evt.pointerId == _groupMovePointerId)
            {
                EndGroupMove();
                evt.StopPropagation();
            }
        }

        private Rect BuildBoardSelectionRect(Vector2 start, Vector2 end)
        {
            float minX = Mathf.Min(start.x, end.x);
            float minY = Mathf.Min(start.y, end.y);
            float width = Mathf.Abs(end.x - start.x);
            float height = Mathf.Abs(end.y - start.y);
            return new Rect(minX, minY, width, height);
        }

        private void ApplyBoxSelection(Rect boardRect)
        {
            ClearSelection();
            ResetPropertyBindings();
            var matches = GetComponentsInRect(boardRect);
            if (matches.Count == 0) return;
            bool primarySet = false;
            foreach (var spec in matches)
            {
                if (spec == null) continue;
                if (!_componentVisuals.TryGetValue(spec.Id, out var visual)) continue;
                var item = ResolveCatalogItem(spec);
                AddComponentToSelection(visual, spec, item, !primarySet);
                primarySet = true;
            }
        }

        private List<ComponentSpec> GetComponentsInRect(Rect boardRect)
        {
            var result = new List<ComponentSpec>();
            if (_currentCircuit?.Components == null) return result;
            if (boardRect.width <= 0f || boardRect.height <= 0f) return result;
            foreach (var spec in _currentCircuit.Components)
            {
                if (spec == null) continue;
                var pos = GetComponentPosition(spec.Id);
                var size = GetComponentSize(spec.Type);
                var compRect = new Rect(pos.x, pos.y, size.x, size.y);
                if (boardRect.Overlaps(compRect))
                {
                    result.Add(spec);
                }
            }
            return result;
        }

        private void EnsureSelectionBox()
        {
            if (_selectionBox != null) return;
            if (_canvasView == null) return;
            _selectionBox = new VisualElement();
            _selectionBox.AddToClassList("canvas-selection-box");
            _selectionBox.pickingMode = PickingMode.Ignore;
            _canvasView.Add(_selectionBox);
            _selectionBox.style.display = DisplayStyle.None;
        }

        private void UpdateSelectionBox(Vector2 startLocal, Vector2 currentLocal)
        {
            if (_selectionBox == null) return;
            float left = Mathf.Min(startLocal.x, currentLocal.x);
            float top = Mathf.Min(startLocal.y, currentLocal.y);
            float width = Mathf.Abs(currentLocal.x - startLocal.x);
            float height = Mathf.Abs(currentLocal.y - startLocal.y);
            _selectionBox.style.left = left;
            _selectionBox.style.top = top;
            _selectionBox.style.width = width;
            _selectionBox.style.height = height;
            _selectionBox.style.display = DisplayStyle.Flex;
        }

        private void HideSelectionBox()
        {
            if (_selectionBox == null) return;
            _selectionBox.style.display = DisplayStyle.None;
        }

        private void BeginGroupMove(VisualElement element, ComponentSpec spec, ComponentCatalog.Item catalogItem, PointerDownEvent evt)
        {
            if (_canvasView == null || spec == null) return;
            RecordStateForUndo();
            AddComponentToSelection(element, spec, catalogItem, true);
            _isMovingSelection = true;
            _groupMovePointerId = evt.pointerId;
            _groupMoveStartBoard = CanvasToBoard(_canvasView.WorldToLocal(evt.position));
            _groupMoveOriginalPositions.Clear();
            foreach (var compId in _selectedComponentIds)
            {
                _groupMoveOriginalPositions[compId] = GetComponentPosition(compId);
            }
            _canvasView.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void HandleGroupMove(PointerMoveEvent evt)
        {
            if (_canvasView == null || _currentCircuit == null || _groupMoveOriginalPositions.Count == 0) return;
            var canvasLocal = _canvasView.WorldToLocal(evt.position);
            var boardPos = CanvasToBoard(canvasLocal);
            var delta = boardPos - _groupMoveStartBoard;
            foreach (var kvp in _groupMoveOriginalPositions)
            {
                var id = kvp.Key;
                var startPos = kvp.Value;
                var comp = _currentCircuit.Components.FirstOrDefault(c => c.Id == id);
                if (comp == null) continue;
                var size = GetComponentSize(comp.Type);
                var target = startPos + delta;
                target.x = Mathf.Round(target.x / GridSnap) * GridSnap;
                target.y = Mathf.Round(target.y / GridSnap) * GridSnap;
                if (_constrainComponentsToBoard)
                {
                    target = ClampToViewport(target, size);
                    target = ClampToBoard(target, size);
                }
                if (_componentVisuals.TryGetValue(id, out var visual))
                {
                    visual.style.left = target.x;
                    visual.style.top = target.y;
                }
                SetComponentPosition(comp, target, true);
            }
            if (_selectedComponent != null)
            {
                UpdateTransformFields(GetComponentPosition(_selectedComponent.Id));
            }
            RequestWireUpdateThrottled();
        }

        private void EndGroupMove()
        {
            _isMovingSelection = false;
            if (_canvasView != null && _groupMovePointerId != -1 && _canvasView.HasPointerCapture(_groupMovePointerId))
            {
                _canvasView.ReleasePointer(_groupMovePointerId);
            }
            _groupMovePointerId = -1;
            _groupMoveOriginalPositions.Clear();
            RequestWireUpdateThrottled();
            RequestCircuit3DRebuild();
        }

        private void OnCanvasWheel(WheelEvent evt)
        {
            if (_canvasView == null) return;
            Vector2 worldPos = _canvasView.LocalToWorld(evt.mousePosition);
            if (!_canvasView.worldBound.Contains(worldPos)) return;
            float factor = evt.delta.y > 0 ? 0.92f : 1.08f;
            Vector2 pivot = factor < 1f ? GetCanvasViewCenterWorld() : worldPos;
            ZoomCanvasStep(factor, pivot);
            evt.StopPropagation();
        }

        private void OnCanvasKeyDown(KeyDownEvent evt)
        {
            if (_canvasView == null || _centerMode != CenterPanelMode.Circuit) return;
            if (evt.target is TextField || evt.target is TextElement) return;

            if (evt.ctrlKey)
            {
                if (evt.keyCode == KeyCode.UpArrow)
                {
                    ZoomCanvasStep(1.08f, GetCanvasViewCenterWorld());
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.DownArrow)
                {
                    ZoomCanvasStep(0.92f, GetCanvasViewCenterWorld());
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Alpha0 || evt.keyCode == KeyCode.Keypad0)
                {
                    ResetCanvasView();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.D)
                {
                    ExportWireMatrix();
                    evt.StopPropagation();
                }
                return;
            }

            var delta = Vector2.zero;
            if (evt.keyCode == KeyCode.LeftArrow) delta = new Vector2(CanvasKeyPanPixels, 0f);
            else if (evt.keyCode == KeyCode.RightArrow) delta = new Vector2(-CanvasKeyPanPixels, 0f);
            else if (evt.keyCode == KeyCode.UpArrow) delta = new Vector2(0f, CanvasKeyPanPixels);
            else if (evt.keyCode == KeyCode.DownArrow) delta = new Vector2(0f, -CanvasKeyPanPixels);

            if (delta != Vector2.zero)
            {
                _canvasPan += delta;
                ApplyCanvasTransform();
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

        private void RequestResetView()
        {
            _pendingResetView = true;
            _resetViewOnNextLayout = true;
            TryApplyPendingResetView();
            ScheduleResetView();
        }

        private void TryApplyPendingResetView()
        {
            if (!_pendingResetView) return;
            if (_canvasView == null || _boardView == null) return;
            var rect = _canvasView.contentRect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            var boardRect = _boardView.layout;
            if (boardRect.width <= 0f || boardRect.height <= 0f) return;
            ResetCanvasView();
            _pendingResetView = false;
            _resetViewOnNextLayout = false;
        }

        private void ScheduleResetView()
        {
            if (_resetViewScheduled || _canvasView == null) return;
            _resetViewScheduled = true;
            _canvasView.schedule.Execute(() =>
            {
                _resetViewScheduled = false;
                if (!_pendingResetView) return;
                TryApplyPendingResetView();
                if (_pendingResetView)
                {
                    ScheduleResetView();
                }
            });
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

        private Vector2 GetBoardPositionAt(Vector2 worldPos)
        {
            if (_boardView != null)
            {
                return _boardView.WorldToLocal(worldPos);
            }
            if (_canvasView != null)
            {
                var canvasLocal = _canvasView.WorldToLocal(worldPos);
                return CanvasToBoard(canvasLocal);
            }
            return Vector2.zero;
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
            var title = _propertiesPanel.Q<Label>(className: "prop-title-lg") ??
                        _propertiesPanel.Q<Label>(className: "prop-title");
            var sub = _propertiesPanel.Q<Label>(className: "prop-sub-lg") ??
                      _propertiesPanel.Q<Label>(className: "prop-sub");
            if (title != null) title.text = spec.Id;
            if (sub != null) sub.text = info.Name;

            var nameField = _propertiesPanel.Q<TextField>("ComponentNameField");
            if (nameField != null)
            {
                nameField.SetValueWithoutNotify(spec.Id);
                if (nameField.userData == null)
                {
                    nameField.userData = "bound";
                    nameField.RegisterValueChangedCallback(e =>
                    {
                        if (_selectedComponent != spec) return;
                        string next = e.newValue?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(next))
                        {
                            nameField.SetValueWithoutNotify(spec.Id);
                            return;
                        }
                        if (!TryRenameComponentId(spec, next))
                        {
                            nameField.SetValueWithoutNotify(spec.Id);
                            return;
                        }
                        nameField.SetValueWithoutNotify(spec.Id);
                    });
                }
            }

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

        private bool TryRenameComponentId(ComponentSpec spec, string newId)
        {
            if (spec == null || _currentCircuit == null) return false;
            string oldId = spec.Id ?? string.Empty;
            string trimmed = newId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed)) return false;
            if (!ComponentIdPattern.IsMatch(trimmed))
            {
                LogNoStack(LogType.Warning, $"[Rename] Invalid component id '{trimmed}'. Use letters, numbers, '_' or '-'.");
                return false;
            }
            if (_currentCircuit.Components.Any(c => c != null && !ReferenceEquals(c, spec) &&
                                                    string.Equals(c.Id, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                LogNoStack(LogType.Warning, $"[Rename] Component id '{trimmed}' already exists.");
                return false;
            }

            if (string.Equals(oldId, trimmed, StringComparison.Ordinal))
            {
                return true;
            }

            RecordStateForUndo();

            var selectedIds = _selectedComponentIds.ToList();
            string primaryId = _selectedComponent?.Id;
            if (selectedIds.Remove(oldId))
            {
                selectedIds.Add(trimmed);
            }
            if (string.Equals(primaryId, oldId, StringComparison.OrdinalIgnoreCase))
            {
                primaryId = trimmed;
            }

            if (_currentCircuit.Nets != null)
            {
                foreach (var net in _currentCircuit.Nets)
                {
                    if (net?.Nodes == null) continue;
                    for (int i = 0; i < net.Nodes.Count; i++)
                    {
                        string node = net.Nodes[i];
                        if (string.IsNullOrWhiteSpace(node)) continue;
                        if (!node.StartsWith(oldId + ".", StringComparison.OrdinalIgnoreCase)) continue;
                        net.Nodes[i] = trimmed + node.Substring(oldId.Length);
                    }
                }
            }

            if (spec.Properties != null)
            {
                spec.Properties["label"] = trimmed;
            }

            if (string.Equals(_last3dPickedComponentId, oldId, StringComparison.OrdinalIgnoreCase))
            {
                _last3dPickedComponentId = trimmed;
            }
            if (string.Equals(_codeTargetComponentId, oldId, StringComparison.OrdinalIgnoreCase))
            {
                _codeTargetComponentId = trimmed;
            }

            if (IsArduinoType(spec.Type))
            {
                TryRenameCodeWorkspace(oldId, trimmed);
            }

            spec.Id = trimmed;
            RefreshCanvas();
            PopulateProjectTree();
            UpdateErrorCount();
            RefreshCodeTargets();

            RestoreSelection(selectedIds, primaryId);
            return true;
        }

        private void RestoreSelection(List<string> selectedIds, string primaryId)
        {
            if (selectedIds == null || selectedIds.Count == 0)
            {
                ClearSelection();
                ResetPropertyBindings();
                return;
            }

            ClearSelection();
            ResetPropertyBindings();
            bool primarySet = false;
            foreach (var id in selectedIds)
            {
                var comp = _currentCircuit.Components.FirstOrDefault(c => c != null &&
                                                                          string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
                if (comp == null) continue;
                if (!_componentVisuals.TryGetValue(comp.Id, out var visual) || visual == null) continue;
                var item = ResolveCatalogItem(comp);
                bool makePrimary = !primarySet && string.Equals(comp.Id, primaryId, StringComparison.OrdinalIgnoreCase);
                AddComponentToSelection(visual, comp, item, makePrimary);
                primarySet = primarySet || makePrimary;
            }
            if (!primarySet && _selectedComponent == null)
            {
                var firstId = selectedIds.FirstOrDefault();
                var comp = _currentCircuit.Components.FirstOrDefault(c => c != null &&
                                                                          string.Equals(c.Id, firstId, StringComparison.OrdinalIgnoreCase));
                if (comp != null && _componentVisuals.TryGetValue(comp.Id, out var visual))
                {
                    AddComponentToSelection(visual, comp, ResolveCatalogItem(comp), true);
                }
            }
            if (_selectedComponent != null)
            {
                UpdateTransformFields(GetComponentPosition(_selectedComponent.Id));
            }
        }

        private void TryRenameCodeWorkspace(string oldId, string newId)
        {
            if (string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newId)) return;
            string projectPath = SessionManager.Instance?.CurrentProjectPath;
            if (!string.IsNullOrWhiteSpace(projectPath))
            {
                string workspaceRoot = ResolveProjectWorkspaceRoot(projectPath);
                if (!string.IsNullOrWhiteSpace(workspaceRoot))
                {
                    TryRenameCodeFolder(Path.Combine(workspaceRoot, "Code", oldId),
                        Path.Combine(workspaceRoot, "Code", newId), oldId, newId);
                }
            }

            string fallbackRoot = Path.Combine(Application.persistentDataPath, "CodeStudio");
            TryRenameCodeFolder(Path.Combine(fallbackRoot, oldId), Path.Combine(fallbackRoot, newId), oldId, newId);
        }

        private void TryRenameCodeFolder(string source, string target, string oldId, string newId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)) return;
                if (!Directory.Exists(source)) return;
                if (!Directory.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    Directory.Move(source, target);
                }
                string oldPrimary = Path.Combine(target, GetPrimarySketchFileName(oldId));
                string newPrimary = Path.Combine(target, GetPrimarySketchFileName(newId));
                if (File.Exists(oldPrimary) && !File.Exists(newPrimary))
                {
                    File.Move(oldPrimary, newPrimary);
                    if (string.Equals(_activeCodePath, oldPrimary, StringComparison.OrdinalIgnoreCase))
                    {
                        _activeCodePath = newPrimary;
                        UpdateCodeFileLabel();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CodeStudio] Failed to rename code folder: {ex.Message}");
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
                ClearWirePreview();
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
            if (_centerMode == CenterPanelMode.Preview3D && _is3DViewFocused && !targetIsText)
            {
                if (Handle3DKeyInput(evt))
                {
                    evt.StopPropagation();
                    return;
                }
                if (evt.keyCode == KeyCode.Escape)
                {
                    _is3DViewFocused = false;
                    evt.StopPropagation();
                    return;
                }
                evt.StopPropagation();
                return;
            }
            if (!targetIsText)
            {
                if (evt.ctrlKey && evt.keyCode == KeyCode.Z)
                {
                    Undo();
                    evt.StopPropagation();
                    return;
                }
                if (evt.ctrlKey && (evt.keyCode == KeyCode.Y || (evt.shiftKey && evt.keyCode == KeyCode.Z)))
                {
                    Redo();
                    evt.StopPropagation();
                    return;
                }
                if (_centerMode == CenterPanelMode.Circuit)
                {
                    if (evt.ctrlKey && evt.keyCode == KeyCode.C)
                    {
                        CopySelectionToClipboard();
                        evt.StopPropagation();
                        return;
                    }
                    if (evt.ctrlKey && evt.keyCode == KeyCode.V)
                    {
                        PasteClipboard();
                        evt.StopPropagation();
                        return;
                    }
                }
            }
            if (_centerMode == CenterPanelMode.Preview3D && _is3DViewFocused && !targetIsText)
            {
                if (Handle3DKeyInput(evt))
                {
                    evt.StopPropagation();
                    return;
                }
            }
            if (evt.keyCode == KeyCode.Escape)
            {
                if (HandleEscapeKey())
                {
                    evt.StopPropagation();
                    return;
                }
            }
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
                if (DeleteSelectedComponents())
                {
                    evt.StopPropagation();
                }
            }
        }

        private bool DeleteSelectedComponents()
        {
            if (_selectedComponentIds.Count == 0 || _currentCircuit == null || _currentCircuit.Components == null) return false;
            RecordStateForUndo();
            var toRemove = _currentCircuit.Components.Where(c => c != null && _selectedComponentIds.Contains(c.Id)).ToList();
            if (toRemove.Count == 0) return false;
            foreach (var comp in toRemove)
            {
                _currentCircuit.Components.Remove(comp);
                if (_componentVisuals.TryGetValue(comp.Id, out var visual))
                {
                    visual.RemoveFromHierarchy();
                    _componentVisuals.Remove(comp.Id);
                }
                RemovePinsForComponent(comp.Id);
                _usedPins.Remove(comp.Id);
            }
            if (_circuit3DFollowToggle != null && _circuit3DFollowToggle.value)
            {
                _circuit3DRenderer?.SetFollowComponent(null, false);
            }
            if (_currentCircuit.Nets != null)
            {
                _currentCircuit.Nets.RemoveAll(net =>
                    net?.Nodes != null && net.Nodes.Any(node =>
                        _selectedComponentIds.Any(id => node.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase))));
            }
            ClearSelection();
            UpdateWireLayer();
            PopulateProjectTree();
            UpdateErrorCount();
            RequestCircuit3DRebuild();
            Debug.Log("[CircuitStudio] Components Deleted.");
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
            UnityEngine.SceneManagement.SceneManager.LoadScene("RunMode");
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

            string sourceRoot = string.Empty;
            string sessionPath = SessionManager.Instance?.CurrentProjectPath;
            if (!string.IsNullOrWhiteSpace(sessionPath))
            {
                string sessionDir = ResolveProjectWorkspaceRoot(sessionPath);
                if (!string.IsNullOrWhiteSpace(sessionDir))
                {
                    string sessionRoot = Path.Combine(sessionDir, "Code");
                    if (!string.Equals(sessionRoot, targetRoot, StringComparison.OrdinalIgnoreCase) &&
                        Directory.Exists(sessionRoot) &&
                        Directory.EnumerateFileSystemEntries(sessionRoot, "*", SearchOption.AllDirectories).Any())
                    {
                        sourceRoot = sessionRoot;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(sourceRoot))
            {
                string fallbackRoot = Path.Combine(Application.persistentDataPath, "CodeStudio");
                if (Directory.Exists(fallbackRoot) &&
                    Directory.EnumerateFileSystemEntries(fallbackRoot, "*", SearchOption.AllDirectories).Any())
                {
                    sourceRoot = fallbackRoot;
                }
            }

            if (string.IsNullOrWhiteSpace(sourceRoot)) return;
            CopyDirectoryRecursive(sourceRoot, targetRoot);
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
            if (_circuit3DView == null || _circuit3DRenderer == null) return;
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
            _is3DViewFocused = true;
            _circuit3DView.Focus();
            evt.StopPropagation();
        }

        private void On3DPointerMove(PointerMoveEvent evt)
        {
            if (_centerMode != CenterPanelMode.Preview3D) return;
            if (!_is3DDragging || evt.pointerId != _3dPointerId || _circuit3DRenderer == null) return;
            var delta = (Vector2)evt.position - _3dLastPos;
            _3dLastPos = (Vector2)evt.position;
            if (_3dDragMode == ThreeDDragMode.Pan)
            {
                _circuit3DRenderer.Pan(delta);
            }
            else if (_3dDragMode == ThreeDDragMode.Orbit)
            {
                _circuit3DRenderer.Orbit(delta);
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
            _circuit3DRenderer?.Zoom(evt.delta.y);
            evt.StopPropagation();
        }

        private bool TryPick3DComponent(Vector2 panelPos, out string componentId, out string componentType)
        {
            componentId = null;
            componentType = null;
            if (_circuit3DRenderer == null) return false;
            if (!TryGetCircuit3DViewport(panelPos, out var viewportPoint)) return false;
            return _circuit3DRenderer.TryPickComponent(viewportPoint, out componentId, out componentType);
        }

        private bool TryGetCircuit3DViewport(Vector2 panelPos, out Vector2 viewportPoint)
        {
            viewportPoint = Vector2.zero;
            if (_circuit3DView == null) return false;
            var rect = _circuit3DView.worldBound;
            if (rect.width <= 0f || rect.height <= 0f) return false;
            float x = (panelPos.x - rect.xMin) / rect.width;
            float y = (panelPos.y - rect.yMin) / rect.height;
            viewportPoint = new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(1f - y));
            return true;
        }

        private bool Handle3DKeyInput(KeyDownEvent evt)
        {
            if (_circuit3DRenderer == null) return false;
            if (evt.keyCode == KeyCode.R || evt.keyCode == KeyCode.Home)
            {
                _circuit3DRenderer.ResetView();
                return true;
            }
            if (evt.keyCode == KeyCode.F)
            {
                string focusId = _selectedComponent?.Id ?? _last3dPickedComponentId;
                if (!string.IsNullOrWhiteSpace(focusId))
                {
                    _circuit3DRenderer.FocusOnComponent(focusId);
                    return true;
                }
            }
            if (evt.keyCode == KeyCode.PageUp || evt.keyCode == KeyCode.E)
            {
                _circuit3DRenderer.NudgePanCameraVertical(1f);
                return true;
            }
            if (evt.keyCode == KeyCode.PageDown || evt.keyCode == KeyCode.Q)
            {
                _circuit3DRenderer.NudgePanCameraVertical(-1f);
                return true;
            }

            bool isArrow = evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow ||
                           evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow;
            if (!isArrow) return false;

            if (evt.ctrlKey)
            {
                if (evt.keyCode == KeyCode.UpArrow)
                {
                    _circuit3DRenderer.Zoom(CameraKeyZoomDelta);
                    return true;
                }
                if (evt.keyCode == KeyCode.DownArrow)
                {
                    _circuit3DRenderer.Zoom(-CameraKeyZoomDelta);
                    return true;
                }
                if (evt.keyCode == KeyCode.LeftArrow)
                {
                    _circuit3DRenderer.AdjustLightingBlend(-0.1f);
                    return true;
                }
                if (evt.keyCode == KeyCode.RightArrow)
                {
                    _circuit3DRenderer.AdjustLightingBlend(0.1f);
                    return true;
                }
                return false;
            }

            if (evt.shiftKey)
            {
                var axes = Vector2.zero;
                if (evt.keyCode == KeyCode.LeftArrow) axes = new Vector2(-1f, 0f);
                if (evt.keyCode == KeyCode.RightArrow) axes = new Vector2(1f, 0f);
                if (evt.keyCode == KeyCode.UpArrow) axes = new Vector2(0f, 1f);
                if (evt.keyCode == KeyCode.DownArrow) axes = new Vector2(0f, -1f);
                _circuit3DRenderer.NudgePanCamera(axes);
                return true;
            }

            var orbit = Vector2.zero;
            if (evt.keyCode == KeyCode.LeftArrow) orbit = new Vector2(CameraKeyOrbitPixels, 0f);
            if (evt.keyCode == KeyCode.RightArrow) orbit = new Vector2(-CameraKeyOrbitPixels, 0f);
            if (evt.keyCode == KeyCode.UpArrow) orbit = new Vector2(0f, -CameraKeyOrbitPixels);
            if (evt.keyCode == KeyCode.DownArrow) orbit = new Vector2(0f, CameraKeyOrbitPixels);
            _circuit3DRenderer.Orbit(orbit);
            return true;
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
            RequestCircuit3DRebuild();
        }
    }

    internal enum WireRoutePass
    {
        Standard,
        AllowCrossing,
        Force
    }

    internal sealed class WireRouter
    {
        private const int MaxIterations = 100000;
        private const int CostStep = 10;
        private const int CostTurn = 100; // Increased from 50 to prefer straight lines
        private const int CostBuffer = 100; // Increased from 20 to avoid hugging obstacles
        private const int CostWireBuffer = 50; // Cost for hugging other wires
        private const int CostCross = 500; // Increased from 100 to strongly avoid crossing
        private const int CostOverlap = 10000;

        private readonly Rect _board;
        private readonly float _step;
        private readonly int _cols;
        private readonly int _rows;

        // Grid state
        private readonly HashSet<Vector2Int> _obstacles = new HashSet<Vector2Int>();
        private readonly Dictionary<Vector2Int, string> _wireOccupancy = new Dictionary<Vector2Int, string>();
        private readonly Dictionary<string, HashSet<Vector2Int>> _netCells = new Dictionary<string, HashSet<Vector2Int>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Vector2Int[] Directions =
        {
            new Vector2Int(1, 0),  // Right
            new Vector2Int(-1, 0), // Left
            new Vector2Int(0, 1),  // Up
            new Vector2Int(0, -1)  // Down
        };

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

        public WireRouter(Rect board, float step, IEnumerable<Rect> obstacles)
        {
            _board = board;
            _step = Mathf.Max(4f, step);
            _cols = Mathf.Max(1, Mathf.CeilToInt(board.width / _step));
            _rows = Mathf.Max(1, Mathf.CeilToInt(board.height / _step));

            if (obstacles != null)
            {
                foreach (var rect in obstacles)
                {
                    MarkObstacle(rect);
                }
            }
        }

        private void MarkObstacle(Rect rect)
        {
            // Shrink by one step to ensure the boundary cells (where anchors are) are free.
            // Rect includes padding (16). Step is 5.
            // Shrinking by 5 leaves 11 padding. Safe.
            float shrink = _step;

            int minX = Mathf.Clamp(Mathf.CeilToInt((rect.xMin + shrink) / _step), 0, _cols);
            int maxX = Mathf.Clamp(Mathf.FloorToInt((rect.xMax - shrink) / _step), 0, _cols);
            int minY = Mathf.Clamp(Mathf.CeilToInt((rect.yMin + shrink) / _step), 0, _rows);
            int maxY = Mathf.Clamp(Mathf.FloorToInt((rect.yMax - shrink) / _step), 0, _rows);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    _obstacles.Add(new Vector2Int(x, y));
                }
            }
        }

        public void MarkOccupancy(Vector2 startPoint, Vector2 endPoint, string netId)
        {
            // If the segment is diagonal, split it into orthogonal segments to match AddWireSegment logic
            if (!Mathf.Approximately(startPoint.x, endPoint.x) && !Mathf.Approximately(startPoint.y, endPoint.y))
            {
                bool horizontalFirst = (StableHash(netId) & 1u) == 0u;
                Vector2 mid = horizontalFirst
                    ? new Vector2(endPoint.x, startPoint.y)
                    : new Vector2(startPoint.x, endPoint.y);

                MarkOccupancyRaw(startPoint, mid, netId);
                MarkOccupancyRaw(mid, endPoint, netId);
                return;
            }
            MarkOccupancyRaw(startPoint, endPoint, netId);
        }

        private void MarkOccupancyRaw(Vector2 startPoint, Vector2 endPoint, string netId)
        {
            var start = PointToCell(startPoint);
            var end = PointToCell(endPoint);

            int dx = Math.Sign(end.x - start.x);
            int dy = Math.Sign(end.y - start.y);
            int steps = Mathf.Max(Mathf.Abs(end.x - start.x), Mathf.Abs(end.y - start.y));

            var current = start;
            for (int i = 0; i <= steps; i++)
            {
                if (!_netCells.TryGetValue(netId, out var cells))
                {
                    cells = new HashSet<Vector2Int>();
                    _netCells[netId] = cells;
                }
                cells.Add(current);
                _wireOccupancy[current] = netId;

                current.x += dx;
                current.y += dy;
            }
        }

        public Vector2Int PointToCell(Vector2 point)
        {
            // Use FloorToInt to ensure consistent mapping with MarkObstacle
            int x = Mathf.Clamp(Mathf.FloorToInt((point.x + _step * 0.5f) / _step), 0, _cols);
            int y = Mathf.Clamp(Mathf.FloorToInt((point.y + _step * 0.5f) / _step), 0, _rows);
            return new Vector2Int(x, y);
        }

        public Vector2 CellToPoint(Vector2Int cell)
        {
            // Use cell center to render wires in the same space the router searches
            return new Vector2((cell.x * _step) + (_step * 0.5f), (cell.y * _step) + (_step * 0.5f));
        }

        public bool TryRoute(Vector2 startPoint, Vector2 endPoint, string netId, out List<Vector2Int> path)
        {
            var start = PointToCell(startPoint);
            var goal = PointToCell(endPoint);

            if (start == goal)
            {
                path = new List<Vector2Int> { start };
                return true;
            }

            // Pass 1: Standard (Avoid obstacles and other wires)
            if (FindPath(start, goal, netId, WireRoutePass.Standard, out path)) return true;

            // Pass 2: Allow Crossing (Cross other wires if necessary)
            if (FindPath(start, goal, netId, WireRoutePass.AllowCrossing, out path)) return true;

            // Pass 3: Force (Ignore obstacles if absolutely necessary)
            if (FindPath(start, goal, netId, WireRoutePass.Force, out path)) return true;

            path = null;
            return false;
        }

        public bool IsSegmentClear(Vector2 startPoint, Vector2 endPoint, string netId, WireRoutePass pass)
        {
            var start = PointToCell(startPoint);
            var goal = PointToCell(endPoint);

            if (start.x != goal.x && start.y != goal.y) return false; // Only orthogonal

            int dx = Math.Sign(goal.x - start.x);
            int dy = Math.Sign(goal.y - start.y);
            var current = start;
            int steps = Mathf.Max(Mathf.Abs(goal.x - start.x), Mathf.Abs(goal.y - start.y));
            var dir = new Vector2Int(dx, dy);

            for (int i = 0; i <= steps; i++)
            {
                int cost = GetMoveCost(current, dir, dir, netId, pass, start, goal);
                if (cost >= int.MaxValue) return false;
                current += dir;
            }
            return true;
        }

        public void CommitPath(IEnumerable<Vector2Int> path, string netId)
        {
            if (path == null) return;

            if (!_netCells.TryGetValue(netId, out var cells))
            {
                cells = new HashSet<Vector2Int>();
                _netCells[netId] = cells;
            }

            foreach (var cell in path)
            {
                cells.Add(cell);
                _wireOccupancy[cell] = netId;
            }
        }

        public Dictionary<string, HashSet<string>> BuildNetConflicts()
        {
            // Simple conflict detection based on adjacency
            var conflicts = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            // Implementation omitted for brevity as it's visual only
            return conflicts;
        }

        public string GetDebugMatrix()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Grid Size: {_cols}x{_rows}, Step: {_step}");

            // Header
            sb.Append("   ");
            for (int x = 0; x < _cols; x++) sb.Append((x % 10).ToString());
            sb.AppendLine();

            for (int y = 0; y < _rows; y++)
            {
                sb.Append($"{y,3} ");
                for (int x = 0; x < _cols; x++)
                {
                    var pos = new Vector2Int(x, y);
                    if (_obstacles.Contains(pos))
                    {
                        sb.Append("X");
                    }
                    else if (_wireOccupancy.TryGetValue(pos, out var netId))
                    {
                        // Use first char of netId or specific symbol
                        sb.Append(string.IsNullOrEmpty(netId) ? "?" : netId.Substring(0, 1));
                    }
                    else
                    {
                        sb.Append(".");
                    }
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private bool FindPath(Vector2Int start, Vector2Int goal, string netId, WireRoutePass pass, out List<Vector2Int> path)
        {
            path = null;
            var openSet = new PriorityQueue<Node>(1024);
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, int>();
            var closedSet = new HashSet<Vector2Int>();

            // Initial node
            gScore[start] = 0;
            openSet.Enqueue(new Node(start, 0, Heuristic(start, goal), Vector2Int.zero));

            int iterations = 0;
            while (openSet.Count > 0 && iterations < MaxIterations)
            {
                iterations++;
                var current = openSet.Dequeue();

                if (current.Position == goal)
                {
                    path = ReconstructPath(cameFrom, current.Position);
                    return true;
                }

                if (closedSet.Contains(current.Position)) continue;
                closedSet.Add(current.Position);

                foreach (var dir in Directions)
                {
                    var neighborPos = current.Position + dir;

                    // Bounds check
                    if (neighborPos.x < 0 || neighborPos.y < 0 || neighborPos.x > _cols || neighborPos.y > _rows) continue;

                    // Check walkability
                    int moveCost = GetMoveCost(neighborPos, dir, current.Direction, netId, pass, start, goal);
                    if (moveCost >= int.MaxValue) continue;

                    int newG = gScore[current.Position] + moveCost;

                    if (!gScore.TryGetValue(neighborPos, out var oldG) || newG < oldG)
                    {
                        gScore[neighborPos] = newG;
                        cameFrom[neighborPos] = current.Position;
                        int h = Heuristic(neighborPos, goal);
                        openSet.Enqueue(new Node(neighborPos, newG, h, dir));
                    }
                }
            }

            return false;
        }

        private int GetMoveCost(Vector2Int cell, Vector2Int moveDir, Vector2Int prevDir, string netId, WireRoutePass pass, Vector2Int start, Vector2Int goal)
        {
            // Always allow start and goal
            if (cell == start || cell == goal) return CostStep;

            // Obstacle check
            if (_obstacles.Contains(cell))
            {
                if (pass == WireRoutePass.Force) return CostOverlap * 2;
                return int.MaxValue;
            }

            // Wire occupancy check
            if (_wireOccupancy.TryGetValue(cell, out var ownerNet))
            {
                if (string.Equals(ownerNet, netId, StringComparison.OrdinalIgnoreCase))
                {
                    return CostStep; // Own net is fine
                }

                // Other net
                if (pass == WireRoutePass.Standard) return int.MaxValue;
                if (pass == WireRoutePass.AllowCrossing) return CostCross; // Crossing penalty
                return CostOverlap; // Overlap penalty
            }

            // Base cost
            int cost = CostStep;

            // Turn penalty
            if (prevDir != Vector2Int.zero && prevDir != moveDir)
            {
                cost += CostTurn;
            }

            // Buffer zone (near obstacles)
            if (IsNearObstacle(cell))
            {
                cost += CostBuffer;
            }

            // Wire spacing (near other nets)
            if (IsNearWire(cell, netId))
            {
                cost += CostWireBuffer;
            }

            // Prefer continuing in same direction as start/goal alignment
            // If we are moving horizontally and goal is horizontal from us, good.

            return cost;
        }

        private bool IsNearObstacle(Vector2Int cell)
        {
            // Check 1-cell radius
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (_obstacles.Contains(new Vector2Int(cell.x + dx, cell.y + dy))) return true;
                }
            }
            return false;
        }

        private bool IsNearWire(Vector2Int cell, string myNetId)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var pos = new Vector2Int(cell.x + dx, cell.y + dy);
                    if (_wireOccupancy.TryGetValue(pos, out var otherNet))
                    {
                        if (!string.Equals(otherNet, myNetId, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static int Heuristic(Vector2Int a, Vector2Int b)
        {
            return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y)) * 10;
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

        // Simple Priority Queue for A*
        private class PriorityQueue<T> where T : IComparable<T>
        {
            private readonly List<T> _data;

            public PriorityQueue(int capacity)
            {
                _data = new List<T>(capacity);
            }

            public int Count => _data.Count;

            public void Enqueue(T item)
            {
                _data.Add(item);
                int ci = _data.Count - 1;
                while (ci > 0)
                {
                    int pi = (ci - 1) / 2;
                    if (_data[ci].CompareTo(_data[pi]) >= 0) break;
                    var tmp = _data[ci]; _data[ci] = _data[pi]; _data[pi] = tmp;
                    ci = pi;
                }
            }

            public T Dequeue()
            {
                int li = _data.Count - 1;
                T frontItem = _data[0];
                _data[0] = _data[li];
                _data.RemoveAt(li);

                --li;
                int pi = 0;
                while (true)
                {
                    int ci = pi * 2 + 1;
                    if (ci > li) break;
                    int rc = ci + 1;
                    if (rc <= li && _data[rc].CompareTo(_data[ci]) < 0) ci = rc;
                    if (_data[pi].CompareTo(_data[ci]) <= 0) break;
                    var tmp = _data[pi]; _data[pi] = _data[ci]; _data[ci] = tmp;
                    pi = ci;
                }
                return frontItem;
            }
        }

        private struct Node : IComparable<Node>
        {
            public Vector2Int Position;
            public int G;
            public int F;
            public Vector2Int Direction;

            public Node(Vector2Int pos, int g, int h, Vector2Int dir)
            {
                Position = pos;
                G = g;
                F = g + h;
                Direction = dir;
            }

            public int CompareTo(Node other)
            {
                return F.CompareTo(other.F);
            }
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
        public enum ComponentSource
        {
            Resource,
            StreamingJson,
            StreamingPackage,
            UserJson,
            UserPackage
        }

        public struct PinLayout
        {
            public string Name;
            public Vector2 Position;
            public Vector2 LabelOffset;
            public int LabelSize;
            public string Label;
            public Vector3 AnchorLocal;
            public float AnchorRadius;
        }

        public struct LabelLayout
        {
            public string Text;
            public Vector2 Position;
            public int Size;
            public string Align;
        }

        public struct ShapeLayout
        {
            public string Id;
            public string Type;
            public Vector2 Position;
            public float Width;
            public float Height;
            public string Text;
        }

        public struct Tuning
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

        public struct PartOverride
        {
            public string Name;
            public Vector3 Position;
            public Vector3 Rotation;
            public Vector3 Scale;
            public bool UseColor;
            public Color Color;
            public bool UseTexture;
            public string TextureFile;
        }

        public struct StateOverride
        {
            public string Id;
            public List<PartOverride> Parts;
        }

        public struct Item
        {
            public string Id;
            public string Name;
            public string Description;
            public string Type;
            public string Symbol;
            public string SymbolFile;
            public string IconChar;
            public Dictionary<string, string> ElectricalSpecs;
            public Dictionary<string, string> DefaultProperties;
            public List<string> Pins;
            public Vector2 Size2D;
            public List<PinLayout> PinLayout;
            public List<LabelLayout> Labels;
            public List<ShapeLayout> Shapes;
            public bool HasTuning;
            public Tuning Tuning;
            public string ModelFile;
            public string PhysicsScript;
            public List<PartOverride> PartOverrides;
            public List<StateOverride> StateOverrides;
            public int Order;
            public string SourcePath;
            public ComponentSource Source;

            public bool IsUserItem => Source == ComponentSource.UserJson || Source == ComponentSource.UserPackage;
            public bool IsPackage => Source == ComponentSource.UserPackage || Source == ComponentSource.StreamingPackage;
        }

        private const string ResourceFolder = "Components";
        private const string PackageExtension = ComponentPackageUtility.PackageExtension;
        private const string PackageDefinitionFile = ComponentPackageUtility.DefinitionFileName;
        private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly StringComparer TypeComparer = StringComparer.OrdinalIgnoreCase;
        private static List<Item> _items = new List<Item>();
        private static Dictionary<string, Item> _byId = new Dictionary<string, Item>(IdComparer);
        private static bool _loaded;

        public static string GetUserComponentRoot()
        {
            string root = Path.Combine(Application.persistentDataPath, ResourceFolder);
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }
            return root;
        }

        public static List<Item> Items
        {
            get
            {
                EnsureLoaded();
                return _items;
            }
        }

        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _items = new List<Item>();
            _byId = new Dictionary<string, Item>(IdComparer);

            LoadFromPersistentData();
            if (_items.Count == 0)
            {
                LoadFromStreamingAssets();
                LoadFromResources();
            }

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

        public static bool TryGetPinAnchor(string type, string pin, out Vector3 localPosition, out float radius)
        {
            localPosition = Vector3.zero;
            radius = 0f;
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(pin)) return false;
            var item = GetByType(type);
            if (string.IsNullOrWhiteSpace(item.Type) || item.PinLayout == null || item.PinLayout.Count == 0)
            {
                return false;
            }
            foreach (var layout in item.PinLayout)
            {
                if (!string.Equals(layout.Name, pin, StringComparison.OrdinalIgnoreCase)) continue;
                if (layout.AnchorRadius <= 0f && layout.AnchorLocal.sqrMagnitude <= 0.0001f)
                {
                    return false;
                }
                localPosition = layout.AnchorLocal;
                radius = layout.AnchorRadius;
                return true;
            }
            return false;
        }

        public static bool TryGetTuning(string type, out Tuning tuning)
        {
            tuning = default;
            if (string.IsNullOrWhiteSpace(type)) return false;
            var item = GetByType(type);
            if (string.IsNullOrWhiteSpace(item.Type) || !item.HasTuning) return false;
            tuning = item.Tuning;
            return true;
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
                    item.Source = ComponentSource.Resource;
                    item.SourcePath = asset.name;
                    RegisterOrUpdate(item);
                }
            }
        }

        private static void LoadFromStreamingAssets()
        {
            string root = Path.Combine(Application.streamingAssetsPath, ResourceFolder);
            if (!Directory.Exists(root)) return;

            LoadJsonFiles(root, ComponentSource.StreamingJson);
            LoadPackages(root, ComponentSource.StreamingPackage);
        }

        private static void LoadFromPersistentData()
        {
            string root = GetUserComponentRoot();
            if (!Directory.Exists(root)) return;

            ComponentPackageUtility.MigrateBundledJsonToPackages();
            ComponentPackageUtility.MigrateUserJsonToPackages();
            LoadJsonFiles(root, ComponentSource.UserJson);
            LoadPackages(root, ComponentSource.UserPackage);
        }

        private static void LoadJsonFiles(string root, ComponentSource source)
        {
            var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (IsWithinPackage(file)) continue;
                string json = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(json)) continue;
                if (TryParseDefinition(json, out var item))
                {
                    item.Source = source;
                    item.SourcePath = file;
                    RegisterOrUpdate(item);
                }
            }
        }

        private static void LoadPackages(string root, ComponentSource source)
        {
            var files = Directory.GetFiles(root, "*" + PackageExtension, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (!ComponentPackageUtility.TryReadDefinitionJson(file, out var json)) continue;
                if (TryParseDefinition(json, out var item))
                {
                    item.Source = source;
                    item.SourcePath = file;
                    RegisterOrUpdate(item);
                }
            }

            var directories = Directory.GetDirectories(root, "*" + PackageExtension, SearchOption.AllDirectories);
            foreach (var dir in directories)
            {
                var defPath = Path.Combine(dir, PackageDefinitionFile);
                if (!File.Exists(defPath)) continue;
                string json = File.ReadAllText(defPath);
                if (string.IsNullOrWhiteSpace(json)) continue;
                if (TryParseDefinition(json, out var item))
                {
                    item.Source = source;
                    item.SourcePath = dir;
                    RegisterOrUpdate(item);
                }
            }
        }

        private static bool IsWithinPackage(string path)
        {
            string dir = Path.GetDirectoryName(path);
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (dir.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase)) return true;
                dir = Path.GetDirectoryName(dir);
            }
            return false;
        }

        private static bool TryParseDefinition(string json, out Item item)
        {
            item = default;
            var def = JsonUtility.FromJson<ComponentDefinition>(json);
            if (def == null || string.IsNullOrWhiteSpace(def.type)) return false;

            var pins = BuildPinList(def);
            var pinLayout = ToPinLayout(def.pinLayout);
            var labels = ToLabelLayout(def.labels);
            var shapes = ToShapeLayout(def.shapes);

            item = new Item
            {
                Id = string.IsNullOrWhiteSpace(def.id) ? def.type : def.id,
                Name = string.IsNullOrWhiteSpace(def.name) ? def.type : def.name,
                Description = def.description ?? string.Empty,
                Type = def.type,
                Symbol = string.IsNullOrWhiteSpace(def.symbol) ? "?" : def.symbol,
                SymbolFile = def.symbolFile ?? string.Empty,
                IconChar = string.IsNullOrWhiteSpace(def.iconChar) ? "?" : def.iconChar,
                ElectricalSpecs = ToDictionary(def.specs),
                DefaultProperties = ToDictionary(def.defaults),
                Pins = pins,
                Size2D = def.size2D.x > 0f && def.size2D.y > 0f ? def.size2D : Vector2.zero,
                PinLayout = pinLayout,
                Labels = labels,
                Shapes = shapes,
                ModelFile = def.modelFile ?? string.Empty,
                PhysicsScript = def.physicsScript ?? string.Empty,
                PartOverrides = ToPartOverrides(def.parts),
                StateOverrides = ToStateOverrides(def.states),
                Order = def.order <= 0 ? 1000 : def.order
            };
            if (def.tuning != null)
            {
                item.HasTuning = true;
                item.Tuning = def.tuning.ToTuning();
            }
            return true;
        }

        private static List<string> BuildPinList(ComponentDefinition def)
        {
            var pins = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (def?.pins != null)
            {
                foreach (var pin in def.pins)
                {
                    if (string.IsNullOrWhiteSpace(pin)) continue;
                    if (seen.Add(pin)) pins.Add(pin);
                }
            }
            if (def?.pinLayout != null)
            {
                foreach (var entry in def.pinLayout)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.name)) continue;
                    if (seen.Add(entry.name)) pins.Add(entry.name);
                }
            }
            return pins;
        }

        private static List<PinLayout> ToPinLayout(PinLayoutEntry[] entries)
        {
            var list = new List<PinLayout>();
            if (entries == null) return list;
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.name)) continue;
                list.Add(new PinLayout
                {
                    Name = entry.name,
                    Position = new Vector2(entry.x, entry.y),
                    LabelOffset = new Vector2(entry.labelOffsetX, entry.labelOffsetY),
                    LabelSize = entry.labelSize,
                    Label = entry.label ?? string.Empty,
                    AnchorLocal = new Vector3(entry.anchorX, entry.anchorY, entry.anchorZ),
                    AnchorRadius = entry.anchorRadius
                });
            }
            return list;
        }

        private static List<LabelLayout> ToLabelLayout(LabelLayoutEntry[] entries)
        {
            var list = new List<LabelLayout>();
            if (entries == null) return list;
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.text)) continue;
                list.Add(new LabelLayout
                {
                    Text = entry.text,
                    Position = new Vector2(entry.x, entry.y),
                    Size = entry.size,
                    Align = entry.align ?? string.Empty
                });
            }
            return list;
        }

        private static List<ShapeLayout> ToShapeLayout(ShapeLayoutEntry[] entries)
        {
            var list = new List<ShapeLayout>();
            if (entries == null) return list;
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.type)) continue;
                list.Add(new ShapeLayout
                {
                    Id = entry.id ?? string.Empty,
                    Type = entry.type,
                    Position = new Vector2(entry.x, entry.y),
                    Width = entry.width,
                    Height = entry.height,
                    Text = entry.text ?? string.Empty
                });
            }
            return list;
        }

        private static List<PartOverride> ToPartOverrides(PartOverrideEntry[] entries)
        {
            var list = new List<PartOverride>();
            if (entries == null) return list;
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.name)) continue;
                list.Add(new PartOverride
                {
                    Name = entry.name,
                    Position = entry.position,
                    Rotation = entry.rotation,
                    Scale = entry.scale == Vector3.zero ? Vector3.one : entry.scale,
                    UseColor = entry.useColor,
                    Color = entry.color,
                    UseTexture = entry.useTexture,
                    TextureFile = entry.textureFile ?? string.Empty
                });
            }
            return list;
        }

        private static List<StateOverride> ToStateOverrides(StateOverrideEntry[] entries)
        {
            var list = new List<StateOverride>();
            if (entries == null) return list;
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id)) continue;
                list.Add(new StateOverride
                {
                    Id = entry.id,
                    Parts = ToPartOverrides(entry.parts)
                });
            }
            return list;
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
            public string symbolFile;
            public string iconChar;
            public int order;
            public string[] pins;
            public KeyValue[] specs;
            public KeyValue[] defaults;
            public Vector2 size2D;
            public PinLayoutEntry[] pinLayout;
            public LabelLayoutEntry[] labels;
            public ShapeLayoutEntry[] shapes;
            public ComponentTuningEntry tuning;
            public string modelFile;
            public string physicsScript;
            public PartOverrideEntry[] parts;
            public StateOverrideEntry[] states;
        }

        [System.Serializable]
        private class PinLayoutEntry
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

        [System.Serializable]
        private class LabelLayoutEntry
        {
            public string text;
            public float x;
            public float y;
            public int size;
            public string align;
        }

        [System.Serializable]
        private class ShapeLayoutEntry
        {
            public string id;
            public string type;
            public float x;
            public float y;
            public float width;
            public float height;
            public string text;
        }

        [System.Serializable]
        private class PartOverrideEntry
        {
            public string name;
            public Vector3 position;
            public Vector3 rotation;
            public Vector3 scale;
            public bool useColor;
            public Color color;
            public bool useTexture;
            public string textureFile;
        }

        [System.Serializable]
        private class StateOverrideEntry
        {
            public string id;
            public PartOverrideEntry[] parts;
        }

        [System.Serializable]
        private class ComponentTuningEntry
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

            public Tuning ToTuning()
            {
                return new Tuning
                {
                    Euler = Euler,
                    Scale = Scale,
                    UseLedColor = UseLedColor,
                    LedColor = LedColor,
                    LedGlowRange = LedGlowRange,
                    LedGlowIntensity = LedGlowIntensity,
                    LedBlowCurrent = LedBlowCurrent,
                    LedBlowTemp = LedBlowTemp,
                    ResistorSmokeStartTemp = ResistorSmokeStartTemp,
                    ResistorSmokeFullTemp = ResistorSmokeFullTemp,
                    ResistorHotStartTemp = ResistorHotStartTemp,
                    ResistorHotFullTemp = ResistorHotFullTemp,
                    ErrorFxInterval = ErrorFxInterval,
                    LabelOffset = LabelOffset,
                    LedGlowOffset = LedGlowOffset,
                    HeatFxOffset = HeatFxOffset,
                    SparkFxOffset = SparkFxOffset,
                    ErrorFxOffset = ErrorFxOffset,
                    SmokeOffset = SmokeOffset,
                    UsbOffset = UsbOffset
                };
            }
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
                    SymbolFile = string.Empty,
                    IconChar = "T",
                    ElectricalSpecs = new Dictionary<string, string>(),
                    DefaultProperties = new Dictionary<string, string>(),
                    Pins = new List<string>(),
                    PinLayout = new List<PinLayout>(),
                    Labels = new List<LabelLayout>(),
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
                SymbolFile = string.Empty,
                IconChar = "U",
                ElectricalSpecs = new Dictionary<string, string>(),
                DefaultProperties = new Dictionary<string, string>(),
                Pins = new List<string> { "P1", "P2" },
                PinLayout = new List<PinLayout>(),
                Labels = new List<LabelLayout>(),
                Order = 1000
            };
        }
    }
}

