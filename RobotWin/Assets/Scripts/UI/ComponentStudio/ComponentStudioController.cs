
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RobotTwin.UI
{
    public class ComponentStudioController : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _root;
        private VisualElement _viewport;
        private Label _statusLabel;
        private Button _saveBtn;
        private Button _saveExitBtn;
        private Button _saveAsBtn;
        private Button _openBtn;
        private Button _backBtn;
        private Button _toolSelectBtn;
        private Button _toolAnchorBtn;
        private Button _toolPinBtn;
        private Button _toolLabelBtn;
        private Button _toolStateBtn;
        private Button _toolMaterialBtn;
        private Button _modelBrowseBtn;
        private Button _configBrowseBtn;
        private Button _configLoadBtn;
        private Button _addPinBtn;
        private Button _addLabelBtn;
        private Button _addSpecBtn;
        private Button _addDefaultBtn;
        private Button _addAnchorBtn;
        private Button _symbolBrowseBtn;
        private Button _frameBtn;
        private Toggle _anchorPickToggle;
        private Toggle _anchorShowToggle;
        private ScrollView _pinsContainer;
        private ScrollView _labelsContainer;
        private ScrollView _specsContainer;
        private ScrollView _defaultsContainer;
        private ScrollView _fxContainer;
        private Button _addFxBtn;
        private ScrollView _catalogList;
        private Button _catalogRefreshBtn;
        private ScrollView _hierarchyTree;
        private ScrollView _anchorsContainer;
        private Button _leftTabHierarchyBtn;
        private Button _leftTabCatalogBtn;
        private Button _leftTabBasicsBtn;
        private Button _leftTabTypeBtn;
        private Button _leftTabObjectsBtn;
        private DropdownField _componentTypeDropdown;
        private TextField _symbolFileField;
        private VisualElement _leftPanelHierarchy;
        private VisualElement _leftPanelCatalog;
        private VisualElement _leftPanelBasics;
        private VisualElement _leftPanelType;
        private VisualElement _leftPanelObjects;
        private VisualElement _objectsLabelsSection;
        private VisualElement _objectsPinsSection;
        private VisualElement _objectsFxSection;
        private VisualElement _objectsAnchorsSection;
        private Label _typePanelTitle;
        private ScrollView _partsContainer;
        private ScrollView _statesContainer;
        private VisualElement _layoutPreview;
        private VisualElement _layoutPreviewLarge;
        private VisualElement _layoutPreviewActive;
        private VisualElement _layoutBodyActive;
        private VisualElement _layoutBody;
        private readonly Dictionary<string, VisualElement> _layoutPins = new Dictionary<string, VisualElement>(StringComparer.OrdinalIgnoreCase);
        private TextField _stateNameField;
        private Button _stateAddBtn;
        private TextField _partPosXField;
        private TextField _partPosYField;
        private TextField _partPosZField;
        private TextField _partRotXField;
        private TextField _partRotYField;
        private TextField _partRotZField;
        private TextField _partScaleXField;
        private TextField _partScaleYField;
        private TextField _partScaleZField;
        private TextField _partColorRField;
        private TextField _partColorGField;
        private TextField _partColorBField;
        private TextField _partColorAField;
        private Toggle _partUseColorToggle;
        private Button _partApplyBtn;
        private VisualElement _helpIcon;
        private VisualElement _helpTooltip;
        private VisualElement _helpOverlay;
        private Label _helpOverlayTitle;
        private Label _helpOverlayVersionLabel;
        private VisualElement _helpOverlayShortcuts;
        private VisualElement _helpOverlayAbout;
        private Button _helpOverlayCloseBtn;
        private VisualElement _confirmOverlay;
        private Label _confirmMessage;
        private Button _confirmCancelBtn;
        private Button _confirmExitBtn;
        private VisualElement _loadingOverlay;
        private VisualElement _loadingSpinner;
        private Label _loadingLabel;
        private Button _loadingCancelBtn;
        private IVisualElementScheduledItem _loadingSpin;
        private RenderTexture _loadingBlurTexture;
        private Button _viewportResetBtn;
        private Button _viewportFrameBtn;
        private Button _viewportAnchorsBtn;
        private Button _viewportPickBtn;
        private Button _viewportZoomInBtn;
        private Button _viewportZoomOutBtn;
        private Button _viewportToolsToggleBtn;
        private VisualElement _viewportTools;
        private VisualElement _viewportToolsBody;
        private Button _viewportPinBtn;
        private Button _viewportLabelBtn;
        private Button _viewportMaterialBtn;
        private Button _viewportStateBtn;
        private VisualElement _viewCube;
        private VisualElement _viewCubeSurface;
        private Button _viewCubeTopBtn;
        private Button _viewCubeBottomBtn;
        private Button _viewCubeLeftBtn;
        private Button _viewCubeRightBtn;
        private Button _viewCubeFrontBtn;
        private Button _viewCubeBackBtn;
        private Button _centerTab3dBtn;
        private Button _centerTab2dBtn;
        private VisualElement _component3DPanel;
        private VisualElement _component2DPanel;
        private Button _layoutResetBtn;
        private Button _layoutToolSelectBtn;
        private Button _layoutToolPanBtn;
        private Button _layoutToolRotateBtn;
        private Button _layoutToolScaleBtn;
        private Button _layoutToolPinBtn;
        private Button _layoutToolLabelBtn;
        private Button _layoutToolBoxBtn;
        private Button _layoutToolTriangleBtn;
        private Button _layoutToolLineBtn;
        private Button _layoutToolTextBtn;
        private Button _layoutToolCircleBtn;
        private Button _layoutGridToggleBtn;
        private VisualElement _layoutGrid;
        private VisualElement _layoutHelpIcon;
        private VisualElement _layoutHelpTooltip;
        private bool _layoutGridVisible = true;
        private bool _is2dView;
        private bool _layoutHover;
        private bool _layoutKeyFocus;
        private bool _layoutPanning;
        private int _layoutPanPointerId = -1;
        private Vector2 _layoutPanStart;
        private Vector2 _layoutPanStartOffset;
        private Vector2 _layoutPanOffset = Vector2.zero;
        private float _layoutZoom = LayoutDefaultZoom;
        private readonly List<ShapeLayoutPayload> _shapeLayout = new List<ShapeLayoutPayload>();
        private string _selectedShapeId;
        private string _draggingShapeId;
        private bool _draggingShape;
        private bool _placingShape;
        private string _placingShapeId;
        private Vector2 _placingShapeStart;
        private string _pendingShapeTextFocusId;
        private float _shapeRotateStartAngle;
        private float _shapeRotateBaseAngle;
        private float _shapeScaleStartDistance;
        private float _shapeScaleBaseWidth;
        private float _shapeScaleBaseHeight;
        private const float LayoutZoomMin = 0.4f;
        private const float LayoutZoomMax = 3.5f;
        private const float LayoutDefaultZoom = 0.85f;
        private const float LayoutZoomSpeed = 0.0015f;
        private const float LayoutPanSpeed = 1f;
        private const float LayoutKeyPanStep = 22f;
        private const float LayoutKeyZoomStep = 0.08f;
        private const float LayoutTextDefaultFontSize = 10f;
        private const float LayoutTextMinFontSize = 8f;
        private const float LayoutTextPadding = 2f;
        private const float LayoutTextWidthFactor = 0.56f;
        private const float LayoutTextHeightFactor = 1.2f;
        private const string FxTypeUnsetLabel = "Unspecified";
        private const string PinTypeLead = "lead";
        private const string PinTypePin = "pin";
        private static readonly List<string> PinTypeChoices = new List<string>
        {
            PinTypeLead,
            PinTypePin
        };
        private static readonly List<string> FxTypeChoices = new List<string>
        {
            FxTypeUnsetLabel,
            "Glow",
            "Heat",
            "Spark",
            "Smoke",
            "Error",
            "Pulse",
            "Vibration"
        };
        private LayoutTool _layoutTool = LayoutTool.Select;
        private VisualElement _circuit3DControls;
        private VisualElement _circuit3DControlsBody;
        private Button _circuit3DControlsToggle;
        private Slider _circuit3DFovSlider;
        private Toggle _circuit3DPerspectiveToggle;
        private Toggle _circuit3DFollowToggle;
        private Toggle _viewportPartColorsToggle;
        private Button _circuit3DRebuildBtn;
        private VisualElement _viewportEditPanel;
        private Toggle _viewportEditPanelToggle;
        private Toggle _viewportEditAnchorsToggle;
        private Toggle _viewportEditEffectsToggle;
        private Toggle _viewportEditPartsToggle;
        private Toggle _viewportEditStatesToggle;
        private Toggle _viewportEditSnapToggle;
        private TextField _viewportEditSnapField;
        private Button _viewportEditMoveBtn;
        private Button _viewportEditRotateBtn;
        private Button _viewportEditScaleBtn;
        private Button _viewportEditResetBtn;
        private VisualElement _menuLayer;
        private Button _menuFileBtn;
        private Button _menuEditBtn;
        private Button _menuViewBtn;
        private Button _menuToolsBtn;
        private Button _menuHelpBtn;
        private VisualElement _menuFileDropdown;
        private VisualElement _menuEditDropdown;
        private VisualElement _menuViewDropdown;
        private VisualElement _menuToolsDropdown;
        private VisualElement _menuHelpDropdown;
        private Button _menuFileOpenBtn;
        private Button _menuFileSaveBtn;
        private Button _menuFileSaveAsBtn;
        private Button _menuFileImportModelBtn;
        private Button _menuFileExitBtn;
        private Button _menuEditAddPinBtn;
        private Button _menuEditAddLabelBtn;
        private Button _menuEditAddStateBtn;
        private Button _menuEditValidateBtn;
        private Button _menuViewToggleOutputBtn;
        private Button _menuViewToggleAnchorsBtn;
        private Button _menuViewFrameBtn;
        private Button _menuViewResetBtn;
        private Button _menuToolsMaterialBtn;
        private Button _menuToolsValidateBtn;
        private Button _menuToolsRunBtn;
        private Button _menuToolsReloadModelBtn;
        private Button _menuToolsExtractPinsBtn;
        private Button _menuHelpShortcutsBtn;
        private Button _menuHelpAboutBtn;
        private VisualElement _outputPanel;
        private ScrollView _outputScroll;
        private VisualElement _outputConsole;
        private Button _outputFilterAllBtn;
        private Button _outputFilterWarningsBtn;
        private Button _outputFilterErrorsBtn;
        private Button _outputClearBtn;
        private Button _outputToggleBtn;
        private Label _outputCountLabel;
        private VisualElement _materialOverlay;
        private Label _materialTargetLabel;
        private TextField _materialTextureField;
        private Button _materialTextureBrowseBtn;
        private Toggle _materialUseTextureToggle;
        private TextField _materialColorRField;
        private TextField _materialColorGField;
        private TextField _materialColorBField;
        private TextField _materialColorAField;
        private Toggle _materialUseColorToggle;
        private TextField _materialPhysicalMaterialField;
        private TextField _materialDensityField;
        private TextField _materialVolumeField;
        private TextField _materialMassField;
        private TextField _materialFrictionField;
        private TextField _materialElasticityField;
        private TextField _materialStrengthField;
        private Label _materialComputedMassLabel;
        private Button _materialApplyBtn;
        private Button _materialClearBtn;
        private Button _materialCloseBtn;
        private Label _inspectorTargetLabel;
        private VisualElement _inspectorDetailsContainer;
        private VisualElement _notificationStack;
        private VisualElement _ledSettingsSection;
        private VisualElement _resistorSettingsSection;
        private VisualElement _layoutContextMenu;
        private Button _layoutContextDeletePinBtn;
        private Button _layoutContextDeleteLabelBtn;
        private Button _layoutContextResetViewBtn;
        private Button _layoutContextDeleteShapeBtn;
        private string _layoutContextShapeId;
        private bool _catalogDragging;
        private string _catalogDragName;
        private string _catalogDragKind;
        private int _catalogDragPointerId = -1;
        private VisualElement _catalogDragGhost;
        private string _layoutContextPinName;
        private VisualElement _layoutContextLabelEntry;

        private ComponentStudioView _studioView;
        private VisualElement _selectedPinEntry;
        private VisualElement _selectedLabelEntry;
        private VisualElement _selectedPartEntry;
        private VisualElement _selectedStateEntry;
        private string _selectedPartName;
        private string _selectedStateId;
        private bool _draggingPin;
        private string _draggingPinName;
        private bool _draggingLabel;
        private VisualElement _draggingLabelEntry;
        private int _dragPointerId = -1;
        private string _packagePath;
        private string _modelSourcePath;
        private ComponentDefinitionPayload _payload;
        private Vector3 _modelOffset = Vector3.zero;

        private bool _orbiting;
        private bool _panning;
        private bool _anchorPickMode;
        private bool _anchorVisible = true;
        private bool _partColorsEnabled;
        private Vector2 _lastPointer;
        private bool _viewportHover;
        private int _viewportPointerId = -1;
        private bool _circuit3DControlsCollapsed = true;
        private bool _viewportToolsCollapsed = true;
        private bool _outputCollapsed;
        private bool _validationQueued;
        private ViewportEditMode _viewportEditMode = ViewportEditMode.Move;
        private EditTarget _editTarget;
        private bool _isViewportEditDragging;
        private Plane _editPlane;
        private Vector3 _editStartLocal;
        private Vector3 _editStartRotation;
        private Vector3 _editStartScale;
        private Vector2 _editStartPointer;

        private readonly Dictionary<string, PartOverride> _partOverrides = new Dictionary<string, PartOverride>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StateOverride> _stateOverrides = new Dictionary<string, StateOverride>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PartTransformSnapshot> _partBaseTransforms = new Dictionary<string, PartTransformSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly List<StudioLogEntry> _logEntries = new List<StudioLogEntry>();
        private readonly Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private OutputFilter _outputFilter = OutputFilter.All;
        private ToolAction _activeTool = ToolAction.Select;
        private const float CameraKeyOrbitPixels = 6f;
        private const float CameraKeyZoomDelta = 120f;
        private const float CameraKeyZoomScale = 0.0005f;
        private const float CameraKeyPanBase = 0.6f;
        private const float CameraKeyPanShift = 1.2f;
        private CancellationTokenSource _modelLoadCts;
        private bool _viewportKeyFocus = true;
        private readonly List<EditorSnapshot> _undoStack = new List<EditorSnapshot>();
        private readonly List<EditorSnapshot> _redoStack = new List<EditorSnapshot>();
        private EditorSnapshot _lastSnapshot;
        private EditorSnapshot _lastSavedSnapshot;
        private bool _isRestoringHistory;
        private bool _hasSavedSnapshot;
        private bool _hasUnsavedChanges;
        private ConfirmOverlayAction _confirmAction = ConfirmOverlayAction.None;
        private const int MaxHistorySnapshots = 100;
        private ComponentStudioView.GizmoHandle _activeGizmoHandle;
        private Vector3 _gizmoDragOriginWorld;
        private Vector3 _rotateStartDirWorld;
        private Vector3 _rotateAxisWorld;
        private float _rotatePrevAngle;
        private float _rotateAccumAngle;
        private float _rotateStartAngle;
        private bool _rotateDragActive;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null)
            {
                Debug.LogError("[ComponentStudio] Missing UIDocument");
                enabled = false;
                return;
            }
            _root = _doc.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError("[ComponentStudio] UIDocument rootVisualElement is null.");
                enabled = false;
                return;
            }

            UiResponsive.Bind(_root, 1200f, 1600f, "studio-compact", "studio-medium", "studio-wide");

            BindUi();

            // Critical UI sanity: viewport + status are required for event hooks and feedback.
            if (_viewport == null || _statusLabel == null)
            {
                Debug.LogError("[ComponentStudio] UI binding failed: missing required elements (StudioViewport / StudioStatusLabel).");
                enabled = false;
                return;
            }

            Initialize3DView();
            InitializeHelp();
            RegisterKeyboardShortcuts();
            LoadSession();
        }

        private void OnDisable()
        {
            CancelModelLoad(true);
        }

        private void BindUi()
        {
            _viewport = _root.Q<VisualElement>("StudioViewport");
            _statusLabel = _root.Q<Label>("StudioStatusLabel");
            _saveBtn = _root.Q<Button>("StudioSaveBtn");
            _saveExitBtn = _root.Q<Button>("StudioSaveExitBtn");
            _saveAsBtn = _root.Q<Button>("StudioSaveAsBtn");
            _openBtn = _root.Q<Button>("StudioOpenBtn");
            _backBtn = _root.Q<Button>("StudioBackBtn");
            _toolSelectBtn = _root.Q<Button>("ToolSelectBtn");
            _toolAnchorBtn = _root.Q<Button>("ToolAnchorBtn");
            _toolPinBtn = _root.Q<Button>("ToolPinBtn");
            _toolLabelBtn = _root.Q<Button>("ToolLabelBtn");
            _toolStateBtn = _root.Q<Button>("ToolStateBtn");
            _toolMaterialBtn = _root.Q<Button>("ToolMaterialBtn");
            _modelBrowseBtn = _root.Q<Button>("ComponentModelBrowseBtn");
            _configBrowseBtn = _root.Q<Button>("ComponentConfigBrowseBtn");
            _configLoadBtn = _root.Q<Button>("ComponentConfigLoadBtn");
            _addPinBtn = _root.Q<Button>("ComponentAddPinBtn");
            _addLabelBtn = _root.Q<Button>("ComponentAddLabelBtn");
            _addSpecBtn = _root.Q<Button>("ComponentAddSpecBtn");
            _addDefaultBtn = _root.Q<Button>("ComponentAddDefaultBtn");
            _addAnchorBtn = _root.Q<Button>("ComponentAddAnchorBtn");
            _symbolBrowseBtn = _root.Q<Button>("ComponentSymbolBrowseBtn");
            _frameBtn = _root.Q<Button>("AnchorFrameBtn");
            _anchorPickToggle = _root.Q<Toggle>("AnchorPickToggle");
            _anchorShowToggle = _root.Q<Toggle>("AnchorShowToggle");

            _pinsContainer = _root.Q<ScrollView>("ComponentPinsContainer");
            _labelsContainer = _root.Q<ScrollView>("ComponentLabelsContainer");
            _specsContainer = _root.Q<ScrollView>("ComponentSpecsContainer");
            _defaultsContainer = _root.Q<ScrollView>("ComponentDefaultsContainer");
            _fxContainer = _root.Q<ScrollView>("ComponentFxContainer");
            _addFxBtn = _root.Q<Button>("ComponentAddFxBtn");
            _catalogList = _root.Q<ScrollView>("ComponentCatalogList");
            _catalogRefreshBtn = _root.Q<Button>("CatalogRefreshBtn");
            _hierarchyTree = _root.Q<ScrollView>("HierarchyTreeContainer");
            _anchorsContainer = _root.Q<ScrollView>("ComponentAnchorsContainer");
            _leftTabHierarchyBtn = _root.Q<Button>("LeftTabHierarchyBtn");
            _leftTabCatalogBtn = _root.Q<Button>("LeftTabCatalogBtn");
            _leftTabBasicsBtn = _root.Q<Button>("LeftTabBasicsBtn");
            _leftTabTypeBtn = _root.Q<Button>("LeftTabTypeBtn");
            _leftTabObjectsBtn = _root.Q<Button>("LeftTabObjectsBtn");
            _componentTypeDropdown = _root.Q<DropdownField>("ComponentTypeField");
            _symbolFileField = _root.Q<TextField>("ComponentSymbolFileField");
            _leftPanelHierarchy = _root.Q<VisualElement>("LeftPanelHierarchy");
            _leftPanelCatalog = _root.Q<VisualElement>("LeftPanelCatalog");
            _leftPanelBasics = _root.Q<VisualElement>("LeftPanelBasics");
            _leftPanelType = _root.Q<VisualElement>("LeftPanelType");
            _leftPanelObjects = _root.Q<VisualElement>("LeftPanelObjects");
            _objectsLabelsSection = _root.Q<VisualElement>("ObjectsLabelsSection");
            _objectsPinsSection = _root.Q<VisualElement>("ObjectsPinsSection");
            _objectsFxSection = _root.Q<VisualElement>("ObjectsFxSection");
            _objectsAnchorsSection = _root.Q<VisualElement>("ObjectsAnchorsSection");
            _typePanelTitle = _root.Q<Label>("TypePanelTitle");
            _partsContainer = _root.Q<ScrollView>("PartListContainer");
            _statesContainer = _root.Q<ScrollView>("StateListContainer");
            _layoutPreview = _root.Q<VisualElement>("Layout2DPreview");
            _layoutPreviewLarge = _root.Q<VisualElement>("Layout2DPreviewLarge");
            _layoutPreviewActive = _layoutPreviewLarge ?? _layoutPreview;
            _stateNameField = _root.Q<TextField>("StateNameField");
            _stateAddBtn = _root.Q<Button>("StateAddBtn");
            _partPosXField = _root.Q<TextField>("PartPosXField");
            _partPosYField = _root.Q<TextField>("PartPosYField");
            _partPosZField = _root.Q<TextField>("PartPosZField");
            _partRotXField = _root.Q<TextField>("PartRotXField");
            _partRotYField = _root.Q<TextField>("PartRotYField");
            _partRotZField = _root.Q<TextField>("PartRotZField");
            _partScaleXField = _root.Q<TextField>("PartScaleXField");
            _partScaleYField = _root.Q<TextField>("PartScaleYField");
            _partScaleZField = _root.Q<TextField>("PartScaleZField");
            _partColorRField = _root.Q<TextField>("PartColorRField");
            _partColorGField = _root.Q<TextField>("PartColorGField");
            _partColorBField = _root.Q<TextField>("PartColorBField");
            _partColorAField = _root.Q<TextField>("PartColorAField");
            _partUseColorToggle = _root.Q<Toggle>("PartUseColorToggle");
            _partApplyBtn = _root.Q<Button>("PartApplyBtn");
            _helpIcon = _root.Q<VisualElement>("StudioHelpIcon");
            _helpTooltip = _root.Q<VisualElement>("StudioHelpTooltip");
            _helpOverlay = _root.Q<VisualElement>("HelpOverlay");
            _helpOverlayTitle = _root.Q<Label>("HelpOverlayTitle");
            _helpOverlayVersionLabel = _root.Q<Label>("HelpOverlayVersionLabel");
            _helpOverlayShortcuts = _root.Q<VisualElement>("HelpOverlayShortcuts");
            _helpOverlayAbout = _root.Q<VisualElement>("HelpOverlayAbout");
            _helpOverlayCloseBtn = _root.Q<Button>("HelpOverlayCloseBtn");
            _confirmOverlay = _root.Q<VisualElement>("ConfirmOverlay");
            _confirmMessage = _root.Q<Label>("ConfirmMessage");
            _confirmCancelBtn = _root.Q<Button>("ConfirmCancelBtn");
            _confirmExitBtn = _root.Q<Button>("ConfirmExitBtn");
            _loadingOverlay = _root.Q<VisualElement>("StudioLoadingOverlay");
            _loadingSpinner = _root.Q<VisualElement>("StudioLoadingSpinner");
            _loadingLabel = _root.Q<Label>("StudioLoadingLabel");
            _loadingCancelBtn = _root.Q<Button>("StudioLoadingCancelBtn");
            _viewportResetBtn = _root.Q<Button>("ViewportResetBtn");
            _viewportFrameBtn = _root.Q<Button>("ViewportFrameBtn");
            _viewportAnchorsBtn = _root.Q<Button>("ViewportAnchorsBtn");
            _viewportPickBtn = _root.Q<Button>("ViewportPickBtn");
            _viewportZoomInBtn = _root.Q<Button>("ViewportZoomInBtn");
            _viewportZoomOutBtn = _root.Q<Button>("ViewportZoomOutBtn");
            _viewportToolsToggleBtn = _root.Q<Button>("ViewportToolsToggleBtn");
            _viewportTools = _root.Q<VisualElement>("ViewportTools");
            _viewportToolsBody = _root.Q<VisualElement>("ViewportToolsBody");
            _viewportPinBtn = _root.Q<Button>("ViewportPinBtn");
            _viewportLabelBtn = _root.Q<Button>("ViewportLabelBtn");
            _viewportMaterialBtn = _root.Q<Button>("ViewportMaterialBtn");
            _viewportStateBtn = _root.Q<Button>("ViewportStateBtn");
            _viewCube = _root.Q<VisualElement>("ViewportViewCube");
            _viewCubeSurface = _root.Q<VisualElement>("ViewCubeSurface");
            _viewCubeTopBtn = _root.Q<Button>("ViewCubeTopBtn");
            _viewCubeBottomBtn = _root.Q<Button>("ViewCubeBottomBtn");
            _viewCubeLeftBtn = _root.Q<Button>("ViewCubeLeftBtn");
            _viewCubeRightBtn = _root.Q<Button>("ViewCubeRightBtn");
            _viewCubeFrontBtn = _root.Q<Button>("ViewCubeFrontBtn");
            _viewCubeBackBtn = _root.Q<Button>("ViewCubeBackBtn");
            _centerTab3dBtn = _root.Q<Button>("CenterTab3D");
            _centerTab2dBtn = _root.Q<Button>("CenterTab2D");
            _component3DPanel = _root.Q<VisualElement>("Component3DPanel");
            _component2DPanel = _root.Q<VisualElement>("Component2DPanel");
            _layoutResetBtn = _root.Q<Button>("Layout2DResetBtn");
            _layoutToolSelectBtn = _root.Q<Button>("LayoutToolSelectBtn");
            _layoutToolPanBtn = _root.Q<Button>("LayoutToolPanBtn");
            _layoutToolRotateBtn = _root.Q<Button>("LayoutToolRotateBtn");
            _layoutToolScaleBtn = _root.Q<Button>("LayoutToolScaleBtn");
            _layoutToolPinBtn = _root.Q<Button>("LayoutToolPinBtn");
            _layoutToolLabelBtn = _root.Q<Button>("LayoutToolLabelBtn");
            _layoutToolBoxBtn = _root.Q<Button>("LayoutToolBoxBtn");
            _layoutToolTriangleBtn = _root.Q<Button>("LayoutToolTriangleBtn");
            _layoutToolLineBtn = _root.Q<Button>("LayoutToolLineBtn");
            _layoutToolTextBtn = _root.Q<Button>("LayoutToolTextBtn");
            _layoutToolCircleBtn = _root.Q<Button>("LayoutToolCircleBtn");
            _layoutGridToggleBtn = _root.Q<Button>("LayoutGridToggleBtn");
            _layoutGrid = _root.Q<VisualElement>("Layout2DGrid");
            _layoutHelpIcon = _root.Q<VisualElement>("Layout2DHelpIcon");
            _layoutHelpTooltip = _root.Q<VisualElement>("Layout2DHelpTooltip");
            _circuit3DControls = _root.Q<VisualElement>("Circuit3DControls");
            _circuit3DControlsBody = _root.Q<VisualElement>("Circuit3DControlsBody");
            _circuit3DControlsToggle = _root.Q<Button>("Circuit3DControlsToggle");
            _circuit3DFovSlider = _root.Q<Slider>("Circuit3DFovSlider");
            _circuit3DPerspectiveToggle = _root.Q<Toggle>("Circuit3DPerspectiveToggle");
            _circuit3DFollowToggle = _root.Q<Toggle>("Circuit3DFollowToggle");
            _viewportPartColorsToggle = _root.Q<Toggle>("ViewportPartColorsToggle");
            _circuit3DRebuildBtn = _root.Q<Button>("Circuit3DRebuildBtn");
            _viewportEditPanel = _root.Q<VisualElement>("ViewportEditPanel");
            _viewportEditPanelToggle = _root.Q<Toggle>("ViewportEditPanelToggle");
            _viewportEditAnchorsToggle = _root.Q<Toggle>("ViewportEditAnchorsToggle");
            _viewportEditEffectsToggle = _root.Q<Toggle>("ViewportEditEffectsToggle");
            _viewportEditPartsToggle = _root.Q<Toggle>("ViewportEditPartsToggle");
            _viewportEditStatesToggle = _root.Q<Toggle>("ViewportEditStatesToggle");
            _viewportEditSnapToggle = _root.Q<Toggle>("ViewportEditSnapToggle");
            _viewportEditSnapField = _root.Q<TextField>("ViewportEditSnapField");
            _viewportEditMoveBtn = _root.Q<Button>("ViewportEditMoveBtn");
            _viewportEditRotateBtn = _root.Q<Button>("ViewportEditRotateBtn");
            _viewportEditScaleBtn = _root.Q<Button>("ViewportEditScaleBtn");
            _viewportEditResetBtn = _root.Q<Button>("ViewportEditResetBtn");
            _menuLayer = _root.Q<VisualElement>("MenuDropdownLayer");
            _menuFileBtn = _root.Q<Button>("MenuFileBtn");
            _menuEditBtn = _root.Q<Button>("MenuEditBtn");
            _menuViewBtn = _root.Q<Button>("MenuViewBtn");
            _menuToolsBtn = _root.Q<Button>("MenuToolsBtn");
            _menuHelpBtn = _root.Q<Button>("MenuHelpBtn");
            _menuFileDropdown = _root.Q<VisualElement>("MenuFileDropdown");
            _menuEditDropdown = _root.Q<VisualElement>("MenuEditDropdown");
            _menuViewDropdown = _root.Q<VisualElement>("MenuViewDropdown");
            _menuToolsDropdown = _root.Q<VisualElement>("MenuToolsDropdown");
            _menuHelpDropdown = _root.Q<VisualElement>("MenuHelpDropdown");
            _menuFileOpenBtn = _root.Q<Button>("MenuFileOpen");
            _menuFileSaveBtn = _root.Q<Button>("MenuFileSave");
            _menuFileSaveAsBtn = _root.Q<Button>("MenuFileSaveAs");
            _menuFileImportModelBtn = _root.Q<Button>("MenuFileImportModel");
            _menuFileExitBtn = _root.Q<Button>("MenuFileExit");
            _menuEditAddPinBtn = _root.Q<Button>("MenuEditAddPin");
            _menuEditAddLabelBtn = _root.Q<Button>("MenuEditAddLabel");
            _menuEditAddStateBtn = _root.Q<Button>("MenuEditAddState");
            _menuEditValidateBtn = _root.Q<Button>("MenuEditValidate");
            _menuViewToggleOutputBtn = _root.Q<Button>("MenuViewToggleOutput");
            _menuViewToggleAnchorsBtn = _root.Q<Button>("MenuViewToggleAnchors");
            _menuViewFrameBtn = _root.Q<Button>("MenuViewFrame");
            _menuViewResetBtn = _root.Q<Button>("MenuViewReset");
            _menuToolsMaterialBtn = _root.Q<Button>("MenuToolsMaterial");
            _menuToolsValidateBtn = _root.Q<Button>("MenuToolsValidate");
            _menuToolsRunBtn = _root.Q<Button>("MenuToolsRun");
            _menuToolsReloadModelBtn = _root.Q<Button>("MenuToolsReloadModel");
            _menuToolsExtractPinsBtn = _root.Q<Button>("MenuToolsExtractPins");
            _menuHelpShortcutsBtn = _root.Q<Button>("MenuHelpShortcuts");
            _menuHelpAboutBtn = _root.Q<Button>("MenuHelpAbout");
            _outputPanel = _root.Q<VisualElement>("StudioOutputPanel");
            _outputScroll = _root.Q<ScrollView>("StudioOutputScroll");
            _outputConsole = _root.Q<VisualElement>("StudioOutputConsole");
            _outputFilterAllBtn = _root.Q<Button>("StudioOutputFilterAllBtn");
            _outputFilterWarningsBtn = _root.Q<Button>("StudioOutputFilterWarningsBtn");
            _outputFilterErrorsBtn = _root.Q<Button>("StudioOutputFilterErrorsBtn");
            _outputClearBtn = _root.Q<Button>("StudioOutputClearBtn");
            _outputToggleBtn = _root.Q<Button>("StudioOutputToggleBtn");
            _outputCountLabel = _root.Q<Label>("StudioOutputCountLabel");
            _materialOverlay = _root.Q<VisualElement>("MaterialOverlay");
            _materialTargetLabel = _root.Q<Label>("MaterialTargetLabel");
            _materialTextureField = _root.Q<TextField>("MaterialTexturePathField");
            _materialTextureBrowseBtn = _root.Q<Button>("MaterialTextureBrowseBtn");
            _materialUseTextureToggle = _root.Q<Toggle>("MaterialUseTextureToggle");
            _materialColorRField = _root.Q<TextField>("MaterialColorRField");
            _materialColorGField = _root.Q<TextField>("MaterialColorGField");
            _materialColorBField = _root.Q<TextField>("MaterialColorBField");
            _materialColorAField = _root.Q<TextField>("MaterialColorAField");
            _materialUseColorToggle = _root.Q<Toggle>("MaterialUseColorToggle");
            _materialPhysicalMaterialField = _root.Q<TextField>("MaterialPhysicalMaterialField");
            _materialDensityField = _root.Q<TextField>("MaterialDensityField");
            _materialVolumeField = _root.Q<TextField>("MaterialVolumeField");
            _materialMassField = _root.Q<TextField>("MaterialMassField");
            _materialFrictionField = _root.Q<TextField>("MaterialFrictionField");
            _materialElasticityField = _root.Q<TextField>("MaterialElasticityField");
            _materialStrengthField = _root.Q<TextField>("MaterialStrengthField");
            _materialComputedMassLabel = _root.Q<Label>("MaterialComputedMassLabel");
            _materialApplyBtn = _root.Q<Button>("MaterialApplyBtn");
            _materialClearBtn = _root.Q<Button>("MaterialClearBtn");
            _materialCloseBtn = _root.Q<Button>("MaterialCloseBtn");
            _inspectorTargetLabel = _root.Q<Label>("InspectorTargetLabel");
            _inspectorDetailsContainer = _root.Q<VisualElement>("InspectorDetailsContainer");
            _notificationStack = _root.Q<VisualElement>("StudioNotificationStack");
            _ledSettingsSection = _root.Q<VisualElement>("LedSettingsSection");
            _resistorSettingsSection = _root.Q<VisualElement>("ResistorSettingsSection");
            _layoutContextMenu = _root.Q<VisualElement>("LayoutContextMenu");
            _layoutContextDeletePinBtn = _root.Q<Button>("LayoutContextDeletePinBtn");
            _layoutContextDeleteLabelBtn = _root.Q<Button>("LayoutContextDeleteLabelBtn");
            _layoutContextDeleteShapeBtn = _root.Q<Button>("LayoutContextDeleteShapeBtn");
            _layoutContextResetViewBtn = _root.Q<Button>("LayoutContextResetViewBtn");

            if (_saveBtn != null) _saveBtn.clicked += SavePackageOnly;
            if (_saveExitBtn != null) _saveExitBtn.clicked += SavePackageAndReturn;
            if (_saveAsBtn != null) _saveAsBtn.clicked += SavePackageAs;
            if (_openBtn != null) _openBtn.clicked += OpenPackageFromDialog;
            if (_backBtn != null) _backBtn.clicked += ReturnToWizard;
            if (_modelBrowseBtn != null) _modelBrowseBtn.clicked += BrowseForModel;
            if (_configBrowseBtn != null) _configBrowseBtn.clicked += BrowseForConfig;
            if (_configLoadBtn != null) _configLoadBtn.clicked += LoadConfigFromField;
            if (_addPinBtn != null) _addPinBtn.clicked += () => AddPinRow(new PinLayoutPayload { name = GetNextPinName() });
            if (_addLabelBtn != null) _addLabelBtn.clicked += () => AddLabelRow(new LabelLayoutPayload { text = "{id}", x = 0.5f, y = 0.5f, size = 10, align = "center" });
            if (_addSpecBtn != null) _addSpecBtn.clicked += () => AddKeyValueRow(_specsContainer, string.Empty, string.Empty);
            if (_addDefaultBtn != null) _addDefaultBtn.clicked += () => AddKeyValueRow(_defaultsContainer, string.Empty, string.Empty);
            if (_addAnchorBtn != null) _addAnchorBtn.clicked += () => AddPinRow(new PinLayoutPayload { name = GetNextPinName(), anchorRadius = 0.01f });
            if (_symbolBrowseBtn != null) _symbolBrowseBtn.clicked += BrowseForSymbol;
            if (_addFxBtn != null) _addFxBtn.clicked += () => AddFxRow(new FxPayload { id = $"fx_{Guid.NewGuid():N}".Substring(0, 6), type = "Glow" });
            if (_frameBtn != null) _frameBtn.clicked += () => _studioView?.FrameModel();
            if (_anchorShowToggle != null)
            {
                _anchorVisible = _anchorShowToggle.value;
                _anchorShowToggle.RegisterValueChangedCallback(evt =>
                {
                    _anchorVisible = evt.newValue;
                    if (_viewportEditAnchorsToggle != null) _viewportEditAnchorsToggle.SetValueWithoutNotify(evt.newValue);
                    RefreshAnchorGizmos();
                });
            }
            if (_anchorPickToggle != null)
            {
                _anchorPickMode = _anchorPickToggle.value;
                _anchorPickToggle.RegisterValueChangedCallback(evt => _anchorPickMode = evt.newValue);
            }
            if (_partApplyBtn != null) _partApplyBtn.clicked += ApplySelectedPartOverride;
            if (_stateAddBtn != null) _stateAddBtn.clicked += AddStateOverride;
            if (_loadingCancelBtn != null) _loadingCancelBtn.clicked += () => CancelModelLoad(true);
            if (_viewportResetBtn != null) _viewportResetBtn.clicked += () => _studioView?.ResetView();
            if (_viewportFrameBtn != null) _viewportFrameBtn.clicked += () => _studioView?.FrameModel();
            if (_viewportAnchorsBtn != null) _viewportAnchorsBtn.clicked += ToggleAnchors;
            if (_viewportPickBtn != null) _viewportPickBtn.clicked += ToggleAnchorPick;
            if (_viewportZoomInBtn != null) _viewportZoomInBtn.clicked += () => _studioView?.Zoom(-0.08f);
            if (_viewportZoomOutBtn != null) _viewportZoomOutBtn.clicked += () => _studioView?.Zoom(0.08f);
            if (_viewportToolsToggleBtn != null) _viewportToolsToggleBtn.clicked += ToggleViewportTools;
            SetViewportToolsCollapsed(_viewportToolsCollapsed);
            if (_viewportPinBtn != null) _viewportPinBtn.clicked += () => ActivateTool(ToolAction.Pin);
            if (_viewportLabelBtn != null) _viewportLabelBtn.clicked += () => ActivateTool(ToolAction.Label);
            if (_viewportMaterialBtn != null) _viewportMaterialBtn.clicked += () => ToggleMaterialPanel();
            if (_viewportStateBtn != null) _viewportStateBtn.clicked += () => ActivateTool(ToolAction.State);
            if (_viewCubeTopBtn != null) _viewCubeTopBtn.clicked += () => { _studioView?.SnapView(ComponentStudioView.ViewPreset.Top); _studioView?.FrameModel(); };
            if (_viewCubeBottomBtn != null) _viewCubeBottomBtn.clicked += () => { _studioView?.SnapView(ComponentStudioView.ViewPreset.Bottom); _studioView?.FrameModel(); };
            if (_viewCubeLeftBtn != null) _viewCubeLeftBtn.clicked += () => { _studioView?.SnapView(ComponentStudioView.ViewPreset.Left); _studioView?.FrameModel(); };
            if (_viewCubeRightBtn != null) _viewCubeRightBtn.clicked += () => { _studioView?.SnapView(ComponentStudioView.ViewPreset.Right); _studioView?.FrameModel(); };
            if (_viewCubeFrontBtn != null) _viewCubeFrontBtn.clicked += () => { _studioView?.SnapView(ComponentStudioView.ViewPreset.Front); _studioView?.FrameModel(); };
            if (_viewCubeBackBtn != null) _viewCubeBackBtn.clicked += () => { _studioView?.SnapView(ComponentStudioView.ViewPreset.Back); _studioView?.FrameModel(); };
            if (_centerTab3dBtn != null) _centerTab3dBtn.clicked += () => SetCenterView(true);
            if (_centerTab2dBtn != null) _centerTab2dBtn.clicked += () => SetCenterView(false);
            if (_layoutResetBtn != null) _layoutResetBtn.clicked += ResetLayoutView;
            if (_layoutToolSelectBtn != null) _layoutToolSelectBtn.clicked += () => SetLayoutTool(LayoutTool.Select);
            if (_layoutToolPanBtn != null)
            {
                _layoutToolPanBtn.style.display = DisplayStyle.None;
                _layoutToolPanBtn.clicked += () => SetLayoutTool(LayoutTool.Select);
            }
            if (_layoutToolRotateBtn != null) _layoutToolRotateBtn.clicked += () => SetLayoutTool(LayoutTool.Rotate);
            if (_layoutToolScaleBtn != null) _layoutToolScaleBtn.clicked += () => SetLayoutTool(LayoutTool.Scale);
            if (_layoutToolPinBtn != null) _layoutToolPinBtn.clicked += () => SetLayoutTool(LayoutTool.Pin);
            if (_layoutToolLabelBtn != null) _layoutToolLabelBtn.clicked += () => SetLayoutTool(LayoutTool.Label);
            if (_layoutToolBoxBtn != null) _layoutToolBoxBtn.clicked += () => SetLayoutTool(LayoutTool.Box);
            if (_layoutToolTriangleBtn != null) _layoutToolTriangleBtn.clicked += () => SetLayoutTool(LayoutTool.Triangle);
            if (_layoutToolLineBtn != null) _layoutToolLineBtn.clicked += () => SetLayoutTool(LayoutTool.Line);
            if (_layoutToolTextBtn != null)
            {
                _layoutToolTextBtn.style.display = DisplayStyle.None;
                _layoutToolTextBtn.clicked += () => SetLayoutTool(LayoutTool.Label);
            }
            if (_layoutToolCircleBtn != null) _layoutToolCircleBtn.clicked += () => SetLayoutTool(LayoutTool.Circle);
            if (_layoutGridToggleBtn != null) _layoutGridToggleBtn.clicked += ToggleLayoutGrid;
            if (_layoutHelpIcon != null)
            {
                _layoutHelpIcon.RegisterCallback<ClickEvent>(evt =>
                {
                    ToggleLayoutHelpTooltip();
                    evt.StopPropagation();
                });
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
            if (_helpOverlayCloseBtn != null)
            {
                _helpOverlayCloseBtn.clicked += HideHelpOverlay;
            }
            if (_confirmOverlay != null)
            {
                _confirmOverlay.style.display = DisplayStyle.None;
                _confirmOverlay.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.target == _confirmOverlay)
                    {
                        HideConfirmOverlay();
                        evt.StopPropagation();
                    }
                });
            }
            if (_confirmCancelBtn != null) _confirmCancelBtn.clicked += HideConfirmOverlay;
            if (_confirmExitBtn != null) _confirmExitBtn.clicked += ConfirmOverlayAccept;
            SetCenterView(true);
            if (_circuit3DControlsToggle != null)
            {
                _circuit3DControlsToggle.clicked += Toggle3DControlsLayout;
                Set3DControlsLayoutCollapsed(_circuit3DControlsCollapsed);
            }
            if (_circuit3DFovSlider != null)
            {
                _circuit3DFovSlider.RegisterValueChangedCallback(evt =>
                {
                    _studioView?.SetFieldOfView(evt.newValue);
                });
            }
            if (_circuit3DPerspectiveToggle != null)
            {
                _circuit3DPerspectiveToggle.RegisterValueChangedCallback(evt =>
                {
                    _studioView?.SetPerspective(evt.newValue);
                });
            }
            if (_circuit3DFollowToggle != null)
            {
                _circuit3DFollowToggle.RegisterValueChangedCallback(_ => ApplyFollowSelection());
            }
            if (_viewportPartColorsToggle != null)
            {
                _partColorsEnabled = _viewportPartColorsToggle.value;
                _viewportPartColorsToggle.RegisterValueChangedCallback(evt => TogglePartColors(evt.newValue));
            }
            if (_circuit3DRebuildBtn != null) _circuit3DRebuildBtn.clicked += () => LoadModelForPayload(_payload);
            if (_viewportEditPanelToggle != null)
            {
                _viewportEditPanelToggle.RegisterValueChangedCallback(evt =>
                {
                    SetViewportEditPanelVisible(evt.newValue);
                    RefreshTransformGizmo();
                });
                SetViewportEditPanelVisible(_viewportEditPanelToggle.value);
            }
            if (_viewportEditAnchorsToggle != null)
            {
                _viewportEditAnchorsToggle.RegisterValueChangedCallback(evt =>
                {
                    SetAnchorVisible(evt.newValue);
                    RefreshAnchorGizmos();
                    RefreshTransformGizmo();
                });
            }
            if (_viewportEditEffectsToggle != null)
            {
                _viewportEditEffectsToggle.RegisterValueChangedCallback(_ =>
                {
                    RefreshEffectGizmos();
                    RefreshTransformGizmo();
                });
            }
            if (_viewportEditPartsToggle != null)
            {
                _viewportEditPartsToggle.RegisterValueChangedCallback(_ =>
                {
                    RefreshEffectGizmos();
                    RefreshTransformGizmo();
                });
            }
            if (_viewportEditStatesToggle != null)
            {
                _viewportEditStatesToggle.RegisterValueChangedCallback(_ =>
                {
                    RefreshEffectGizmos();
                    RefreshTransformGizmo();
                });
            }
            if (_viewportEditMoveBtn != null) _viewportEditMoveBtn.clicked += () => SetViewportEditMode(ViewportEditMode.Move);
            if (_viewportEditRotateBtn != null) _viewportEditRotateBtn.clicked += () => SetViewportEditMode(ViewportEditMode.Rotate);
            if (_viewportEditScaleBtn != null) _viewportEditScaleBtn.clicked += () => SetViewportEditMode(ViewportEditMode.Scale);
            if (_viewportEditResetBtn != null) _viewportEditResetBtn.clicked += ResetTransformSelection;
            if (_viewportEditSnapField != null && string.IsNullOrWhiteSpace(_viewportEditSnapField.value))
            {
                _viewportEditSnapField.value = "0.01";
            }
            UpdateViewportEditButtons();
            if (_toolSelectBtn != null) _toolSelectBtn.clicked += () => ActivateTool(ToolAction.Select);
            if (_toolAnchorBtn != null) _toolAnchorBtn.clicked += () => ActivateTool(ToolAction.Anchor);
            if (_toolPinBtn != null) _toolPinBtn.clicked += () => ActivateTool(ToolAction.Pin);
            if (_toolLabelBtn != null) _toolLabelBtn.clicked += () => ActivateTool(ToolAction.Label);
            if (_toolStateBtn != null) _toolStateBtn.clicked += () => ActivateTool(ToolAction.State);
            if (_toolMaterialBtn != null) _toolMaterialBtn.clicked += () => ToggleMaterialPanel();
            if (_menuFileBtn != null) _menuFileBtn.clicked += () => ToggleMenu(_menuFileDropdown, _menuFileBtn);
            if (_menuEditBtn != null) _menuEditBtn.clicked += () => ToggleMenu(_menuEditDropdown, _menuEditBtn);
            if (_menuViewBtn != null) _menuViewBtn.clicked += () => ToggleMenu(_menuViewDropdown, _menuViewBtn);
            if (_menuToolsBtn != null) _menuToolsBtn.clicked += () => ToggleMenu(_menuToolsDropdown, _menuToolsBtn);
            if (_menuHelpBtn != null) _menuHelpBtn.clicked += () => ToggleMenu(_menuHelpDropdown, _menuHelpBtn);
            if (_menuFileOpenBtn != null) _menuFileOpenBtn.clicked += () => { OpenPackageFromDialog(); HideMenus(); };
            if (_menuFileSaveBtn != null) _menuFileSaveBtn.clicked += () => { SavePackageOnly(); HideMenus(); };
            if (_menuFileSaveAsBtn != null) _menuFileSaveAsBtn.clicked += () => { SavePackageAs(); HideMenus(); };
            if (_menuFileImportModelBtn != null) _menuFileImportModelBtn.clicked += () => { BrowseForModel(); HideMenus(); };
            if (_menuFileExitBtn != null) _menuFileExitBtn.clicked += () => { ReturnToWizard(); HideMenus(); };
            if (_menuEditAddPinBtn != null) _menuEditAddPinBtn.clicked += () => { AddPinRow(new PinLayoutPayload { name = GetNextPinName() }); HideMenus(); };
            if (_menuEditAddLabelBtn != null) _menuEditAddLabelBtn.clicked += () => { AddLabelRow(new LabelLayoutPayload { text = "{id}", x = 0.5f, y = 0.5f, size = 10, align = "center" }); HideMenus(); };
            if (_menuEditAddStateBtn != null) _menuEditAddStateBtn.clicked += () => { TryAddStateFromMenu(); HideMenus(); };
            if (_menuEditValidateBtn != null) _menuEditValidateBtn.clicked += () => { RunValidation(); HideMenus(); };
            if (_menuViewToggleOutputBtn != null) _menuViewToggleOutputBtn.clicked += () => { ToggleOutputPanel(); HideMenus(); };
            if (_menuViewToggleAnchorsBtn != null) _menuViewToggleAnchorsBtn.clicked += () => { ToggleAnchors(); HideMenus(); };
            if (_menuViewFrameBtn != null) _menuViewFrameBtn.clicked += () => { _studioView?.FrameModel(); HideMenus(); };
            if (_menuViewResetBtn != null) _menuViewResetBtn.clicked += () => { _studioView?.ResetView(); HideMenus(); };
            if (_menuToolsMaterialBtn != null) _menuToolsMaterialBtn.clicked += () => { ToggleMaterialPanel(); HideMenus(); };
            if (_menuToolsValidateBtn != null) _menuToolsValidateBtn.clicked += () => { RunValidationAndNotify(); HideMenus(); };
            if (_menuToolsRunBtn != null) _menuToolsRunBtn.clicked += () => { RunModelCheck(); HideMenus(); };
            if (_menuToolsReloadModelBtn != null) _menuToolsReloadModelBtn.clicked += () => { LoadModelForPayload(_payload); HideMenus(); };
            if (_menuToolsExtractPinsBtn != null) _menuToolsExtractPinsBtn.clicked += () => { AutoAddPinsFromCurrent(); HideMenus(); };
            if (_menuHelpShortcutsBtn != null) _menuHelpShortcutsBtn.clicked += () => { ShowHelpOverlay(HelpOverlaySection.Shortcuts); HideMenus(); };
            if (_menuHelpAboutBtn != null) _menuHelpAboutBtn.clicked += () => { ShowHelpOverlay(HelpOverlaySection.About); HideMenus(); };
            if (_outputFilterAllBtn != null) _outputFilterAllBtn.clicked += () => SetOutputFilter(OutputFilter.All);
            if (_outputFilterWarningsBtn != null) _outputFilterWarningsBtn.clicked += () => SetOutputFilter(OutputFilter.Warnings);
            if (_outputFilterErrorsBtn != null) _outputFilterErrorsBtn.clicked += () => SetOutputFilter(OutputFilter.Errors);
            if (_outputClearBtn != null) _outputClearBtn.clicked += ClearOutput;
            if (_outputToggleBtn != null) _outputToggleBtn.clicked += ToggleOutputPanel;
            if (_materialTextureBrowseBtn != null) _materialTextureBrowseBtn.clicked += BrowseForTexture;
            if (_materialApplyBtn != null) _materialApplyBtn.clicked += ApplyMaterialFromModal;
            if (_materialClearBtn != null) _materialClearBtn.clicked += ClearMaterialOverrides;
            if (_materialCloseBtn != null) _materialCloseBtn.clicked += CloseMaterialModal;
            if (_catalogRefreshBtn != null) _catalogRefreshBtn.clicked += RefreshCatalogAndHierarchy;
            if (_leftTabHierarchyBtn != null) _leftTabHierarchyBtn.clicked += () => SetLeftPanelTab(LeftPanelTab.Hierarchy);
            if (_leftTabCatalogBtn != null) _leftTabCatalogBtn.clicked += () => SetLeftPanelTab(LeftPanelTab.Catalog);
            if (_leftTabBasicsBtn != null) _leftTabBasicsBtn.clicked += () => SetLeftPanelTab(LeftPanelTab.Basics);
            if (_leftTabTypeBtn != null) _leftTabTypeBtn.clicked += () => SetLeftPanelTab(LeftPanelTab.Type);
            if (_leftTabObjectsBtn != null) _leftTabObjectsBtn.clicked += () => SetLeftPanelTab(LeftPanelTab.Objects);
            if (_layoutContextDeletePinBtn != null) _layoutContextDeletePinBtn.clicked += DeleteContextPin;
            if (_layoutContextDeleteLabelBtn != null) _layoutContextDeleteLabelBtn.clicked += DeleteContextLabel;
            if (_layoutContextDeleteShapeBtn != null) _layoutContextDeleteShapeBtn.clicked += DeleteContextShape;
            if (_layoutContextResetViewBtn != null) _layoutContextResetViewBtn.clicked += ResetLayoutView;

            if (_layoutPreview != null)
            {
                _layoutPreview.focusable = true;
                _layoutPreview.RegisterCallback<GeometryChangedEvent>(_ => RefreshLayoutPreview());
                _layoutPreview.RegisterCallback<PointerMoveEvent>(OnLayoutPointerMove);
                _layoutPreview.RegisterCallback<PointerUpEvent>(OnLayoutPointerUp);
                _layoutPreview.RegisterCallback<PointerLeaveEvent>(OnLayoutPointerLeave);
                _layoutPreview.RegisterCallback<PointerCancelEvent>(OnLayoutPointerCancel);
                _layoutPreview.RegisterCallback<PointerCaptureOutEvent>(OnLayoutPointerCaptureOut);
            }
            if (_layoutPreviewLarge != null)
            {
                _layoutPreviewLarge.focusable = true;
                _layoutPreviewLarge.RegisterCallback<GeometryChangedEvent>(_ => RefreshLayoutPreview());
                _layoutPreviewLarge.RegisterCallback<PointerDownEvent>(OnLayoutPreviewPointerDown);
                _layoutPreviewLarge.RegisterCallback<PointerMoveEvent>(OnLayoutPointerMove);
                _layoutPreviewLarge.RegisterCallback<PointerUpEvent>(OnLayoutPointerUp);
                _layoutPreviewLarge.RegisterCallback<PointerLeaveEvent>(OnLayoutPointerLeave);
                _layoutPreviewLarge.RegisterCallback<WheelEvent>(OnLayoutPreviewWheel);
                _layoutPreviewLarge.RegisterCallback<PointerCancelEvent>(OnLayoutPointerCancel);
                _layoutPreviewLarge.RegisterCallback<PointerCaptureOutEvent>(OnLayoutPointerCaptureOut);
                _layoutPreviewLarge.RegisterCallback<PointerEnterEvent>(_ => _layoutHover = true);
                _layoutPreviewLarge.RegisterCallback<PointerLeaveEvent>(_ => _layoutHover = false);
            }

            var sizeXField = GetField("ComponentSizeXField");
            var sizeYField = GetField("ComponentSizeYField");
            if (sizeXField != null) sizeXField.RegisterValueChangedCallback(_ => RefreshLayoutPreview());
            if (sizeYField != null) sizeYField.RegisterValueChangedCallback(_ => RefreshLayoutPreview());
            if (_componentTypeDropdown != null)
            {
                _componentTypeDropdown.RegisterValueChangedCallback(_ =>
                {
                    EnsureRequiredStates();
                    UpdateTypePanels();
                });
            }
            else
            {
                var typeField = GetField("ComponentTypeField");
                if (typeField != null) typeField.RegisterValueChangedCallback(_ =>
                {
                    EnsureRequiredStates();
                    UpdateTypePanels();
                });
            }

            if (_viewport != null)
            {
                _viewport.focusable = true;
                _viewport.RegisterCallback<PointerDownEvent>(OnViewportPointerDown);
                _viewport.RegisterCallback<PointerMoveEvent>(OnViewportPointerMove);
                _viewport.RegisterCallback<PointerUpEvent>(OnViewportPointerUp);
                _viewport.RegisterCallback<WheelEvent>(OnViewportWheel);
                _viewport.RegisterCallback<PointerEnterEvent>(_ => _viewportHover = true);
                _viewport.RegisterCallback<PointerLeaveEvent>(_ => _viewportHover = false);
            }
            if (_viewCubeSurface != null)
            {
                _viewCubeSurface.RegisterCallback<GeometryChangedEvent>(OnViewCubeGeometryChanged);
                _viewCubeSurface.RegisterCallback<PointerDownEvent>(OnViewCubePointerDown);
            }
            if (_root != null)
            {
                _root.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
                _root.RegisterCallback<PointerMoveEvent>(OnCatalogDragMove);
                _root.RegisterCallback<PointerUpEvent>(OnCatalogDragEnd);
                _root.RegisterCallback<PointerCancelEvent>(OnCatalogDragCancel);
            }

            UpdateToolButtons();
            RegisterValidationBindings();
            InitializeTypeDropdown();
            UpdateTypePanels();
            InitializeOutputPanel();
            RegisterMenuDismissHandler();
            RefreshCatalogAndHierarchy();
            SetLeftPanelTab(LeftPanelTab.Hierarchy);
        }

        private void RegisterMenuDismissHandler()
        {
            if (_root == null) return;
            _root.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (IsMenuPointerTarget(evt)) return;
                HideMenus();
            }, TrickleDown.TrickleDown);
        }

        private void OnRootPointerDown(PointerDownEvent evt)
        {
            if (evt.target is not VisualElement target) return;
            if (!IsLayoutContextTarget(target))
            {
                HideLayoutContextMenu();
            }
            if (_is2dView)
            {
                _layoutKeyFocus = true;
                if (!IsLayoutHelpPointerTarget(target))
                {
                    HideLayoutHelpTooltip();
                }
                return;
            }
            _viewportKeyFocus = true;
        }

        private bool IsLayoutHelpPointerTarget(VisualElement target)
        {
            if (target == null) return false;
            if (_layoutHelpIcon != null && (_layoutHelpIcon == target || _layoutHelpIcon.Contains(target)))
            {
                return true;
            }
            if (_layoutHelpTooltip != null && (_layoutHelpTooltip == target || _layoutHelpTooltip.Contains(target)))
            {
                return true;
            }
            return false;
        }

        private void ToggleLayoutHelpTooltip()
        {
            if (_layoutHelpTooltip == null) return;
            bool isVisible = _layoutHelpTooltip.style.display == DisplayStyle.Flex;
            _layoutHelpTooltip.style.display = isVisible ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void HideLayoutHelpTooltip()
        {
            if (_layoutHelpTooltip == null) return;
            _layoutHelpTooltip.style.display = DisplayStyle.None;
        }

        private void ToggleMenu(VisualElement dropdown, VisualElement anchor)
        {
            if (dropdown == null || anchor == null || _root == null) return;
            bool wasVisible = dropdown.style.display == DisplayStyle.Flex;
            HideMenus();
            if (wasVisible) return;
            ShowMenu(dropdown, anchor);
        }

        private void ShowMenu(VisualElement dropdown, VisualElement anchor)
        {
            if (_menuLayer != null) _menuLayer.style.display = DisplayStyle.Flex;
            dropdown.style.display = DisplayStyle.Flex;
            PositionMenu(dropdown, anchor);
        }

        private void HideMenus()
        {
            if (_menuFileDropdown != null) _menuFileDropdown.style.display = DisplayStyle.None;
            if (_menuEditDropdown != null) _menuEditDropdown.style.display = DisplayStyle.None;
            if (_menuViewDropdown != null) _menuViewDropdown.style.display = DisplayStyle.None;
            if (_menuToolsDropdown != null) _menuToolsDropdown.style.display = DisplayStyle.None;
            if (_menuHelpDropdown != null) _menuHelpDropdown.style.display = DisplayStyle.None;
            if (_menuLayer != null) _menuLayer.style.display = DisplayStyle.None;
        }

        private void PositionMenu(VisualElement dropdown, VisualElement anchor)
        {
            if (_root == null || dropdown == null || anchor == null) return;
            var rootRect = _root.worldBound;
            var anchorRect = anchor.worldBound;
            float left = anchorRect.xMin - rootRect.xMin;
            float top = anchorRect.yMax - rootRect.yMin + 4f;
            dropdown.style.left = left;
            dropdown.style.top = top;

            dropdown.schedule.Execute(() =>
            {
                var dropdownRect = dropdown.worldBound;
                float overflowX = dropdownRect.xMax - rootRect.xMax;
                if (overflowX > 0f)
                {
                    dropdown.style.left = left - overflowX - 6f;
                }
                float overflowY = dropdownRect.yMax - rootRect.yMax;
                if (overflowY > 0f)
                {
                    dropdown.style.top = top - dropdownRect.height - anchorRect.height;
                }
            });
        }

        private bool IsMenuPointerTarget(PointerDownEvent evt)
        {
            if (evt == null || evt.target is not VisualElement target) return false;
            return IsMenuElement(target, _menuFileBtn, _menuFileDropdown) ||
                   IsMenuElement(target, _menuEditBtn, _menuEditDropdown) ||
                   IsMenuElement(target, _menuViewBtn, _menuViewDropdown) ||
                   IsMenuElement(target, _menuToolsBtn, _menuToolsDropdown) ||
                   IsMenuElement(target, _menuHelpBtn, _menuHelpDropdown);
        }

        private static bool IsMenuElement(VisualElement target, VisualElement button, VisualElement dropdown)
        {
            if (button != null && (target == button || button.Contains(target))) return true;
            if (dropdown != null && (target == dropdown || dropdown.Contains(target))) return true;
            return false;
        }

        private void InitializeOutputPanel()
        {
            SetOutputFilter(_outputFilter);
            RenderOutput();
        }

        private void ToggleOutputPanel()
        {
            if (_outputPanel == null) return;
            _outputCollapsed = !_outputCollapsed;
            if (_outputCollapsed)
            {
                _outputPanel.AddToClassList("collapsed");
                if (_outputToggleBtn != null) _outputToggleBtn.text = "SHOW";
            }
            else
            {
                _outputPanel.RemoveFromClassList("collapsed");
                if (_outputToggleBtn != null) _outputToggleBtn.text = "HIDE";
            }
        }

        private void SetLeftPanelTab(LeftPanelTab tab)
        {
            SetPanelVisible(_leftPanelHierarchy, tab == LeftPanelTab.Hierarchy);
            SetPanelVisible(_leftPanelCatalog, tab == LeftPanelTab.Catalog);
            SetPanelVisible(_leftPanelBasics, tab == LeftPanelTab.Basics);
            SetPanelVisible(_leftPanelType, tab == LeftPanelTab.Type);
            SetPanelVisible(_leftPanelObjects, tab == LeftPanelTab.Objects);

            ToggleActiveClass(_leftTabHierarchyBtn, tab == LeftPanelTab.Hierarchy);
            ToggleActiveClass(_leftTabCatalogBtn, tab == LeftPanelTab.Catalog);
            ToggleActiveClass(_leftTabBasicsBtn, tab == LeftPanelTab.Basics);
            ToggleActiveClass(_leftTabTypeBtn, tab == LeftPanelTab.Type);
            ToggleActiveClass(_leftTabObjectsBtn, tab == LeftPanelTab.Objects);
        }

        private static void SetPanelVisible(VisualElement panel, bool isVisible)
        {
            if (panel == null) return;
            panel.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetOutputFilter(OutputFilter filter)
        {
            _outputFilter = filter;
            if (_outputFilterAllBtn != null) ToggleActiveClass(_outputFilterAllBtn, filter == OutputFilter.All);
            if (_outputFilterWarningsBtn != null) ToggleActiveClass(_outputFilterWarningsBtn, filter == OutputFilter.Warnings);
            if (_outputFilterErrorsBtn != null) ToggleActiveClass(_outputFilterErrorsBtn, filter == OutputFilter.Errors);
            RenderOutput();
        }

        private void ToggleActiveClass(VisualElement element, bool isActive)
        {
            if (element == null) return;
            if (isActive) element.AddToClassList("active");
            else element.RemoveFromClassList("active");
        }

        private void ClearOutput()
        {
            _logEntries.Clear();
            RunValidation();
            RenderOutput();
        }

        private void ResetHistory()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            var snapshot = BuildSnapshot();
            _undoStack.Add(snapshot);
            _lastSnapshot = snapshot;
            MarkSavedSnapshot(snapshot);
        }

        private void CaptureUndoSnapshot()
        {
            if (_isRestoringHistory) return;
            var snapshot = BuildSnapshot();
            if (SnapshotEquals(snapshot, _lastSnapshot)) return;
            _undoStack.Add(snapshot);
            if (_undoStack.Count > MaxHistorySnapshots)
            {
                _undoStack.RemoveAt(0);
            }
            _redoStack.Clear();
            _lastSnapshot = snapshot;
            _hasUnsavedChanges = _hasSavedSnapshot ? !SnapshotEquals(snapshot, _lastSavedSnapshot) : true;
        }

        private void Undo()
        {
            if (_undoStack.Count < 2) return;
            var current = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _redoStack.Add(current);
            ApplySnapshot(_undoStack[_undoStack.Count - 1]);
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) return;
            var next = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _undoStack.Add(next);
            ApplySnapshot(next);
        }

        private EditorSnapshot BuildSnapshot()
        {
            var payload = BuildPayloadFromEditor() ?? CreateDefaultPayload();
            string json = JsonUtility.ToJson(payload);
            string modelPath = GetField("ComponentModelField")?.value?.Trim() ?? string.Empty;
            return new EditorSnapshot
            {
                PayloadJson = json,
                ModelSourcePath = modelPath
            };
        }

        private void ApplySnapshot(EditorSnapshot snapshot)
        {
            ResetTransientStates();
            _isRestoringHistory = true;
            _modelSourcePath = snapshot.ModelSourcePath ?? string.Empty;
            _payload = JsonUtility.FromJson<ComponentDefinitionPayload>(snapshot.PayloadJson) ?? CreateDefaultPayload();
            ApplyPayload(_payload);
            LoadModelForPayload(_payload);
            RunValidation();
            _lastSnapshot = snapshot;
            _isRestoringHistory = false;
            _hasUnsavedChanges = _hasSavedSnapshot && !SnapshotEquals(snapshot, _lastSavedSnapshot);
        }

        private void ResetTransientStates()
        {
            if (_menuLayer != null && _menuLayer.style.display == DisplayStyle.Flex)
            {
                HideMenus();
            }
            if (_layoutContextMenu != null && _layoutContextMenu.style.display == DisplayStyle.Flex)
            {
                HideLayoutContextMenu();
            }
            if (_materialOverlay != null && _materialOverlay.style.display == DisplayStyle.Flex)
            {
                CloseMaterialModal();
            }
            if (_helpTooltip != null && _helpTooltip.style.display == DisplayStyle.Flex)
            {
                _helpTooltip.style.display = DisplayStyle.None;
            }
            if (_helpOverlay != null && _helpOverlay.style.display == DisplayStyle.Flex)
            {
                _helpOverlay.style.display = DisplayStyle.None;
            }
            if (_confirmOverlay != null && _confirmOverlay.style.display == DisplayStyle.Flex)
            {
                _confirmOverlay.style.display = DisplayStyle.None;
            }
            if (_layoutHelpTooltip != null && _layoutHelpTooltip.style.display == DisplayStyle.Flex)
            {
                _layoutHelpTooltip.style.display = DisplayStyle.None;
            }
            if (_catalogDragging)
            {
                CancelCatalogDrag();
            }
            if (_isViewportEditDragging || _activeGizmoHandle != null || _editTarget.Kind != EditTargetKind.None)
            {
                EndViewportEdit();
            }
            if (_orbiting || _panning)
            {
                _orbiting = false;
                _panning = false;
                ReleaseViewportPointerCapture();
            }
            if (_placingShape || _draggingPin || _draggingLabel || _draggingShape || _layoutPanning)
            {
                StopLayoutInteractions();
            }
            _pendingShapeTextFocusId = null;
            _layoutContextShapeId = null;
            _layoutContextPinName = null;
            _layoutContextLabelEntry = null;
        }

        private static bool SnapshotEquals(EditorSnapshot left, EditorSnapshot right)
        {
            return string.Equals(left.PayloadJson, right.PayloadJson, StringComparison.Ordinal) &&
                   string.Equals(left.ModelSourcePath, right.ModelSourcePath, StringComparison.Ordinal);
        }

        private void MarkSavedSnapshot(EditorSnapshot snapshot)
        {
            _lastSavedSnapshot = snapshot;
            _hasSavedSnapshot = true;
            _hasUnsavedChanges = false;
        }

        private void AddLog(LogLevel level, string message, string source = null, string detail = null)
        {
            _logEntries.Add(new StudioLogEntry
            {
                Level = level,
                Message = message,
                Source = source ?? string.Empty,
                Detail = detail ?? string.Empty
            });
            RenderOutput();
        }

        private void RenderOutput()
        {
            if (_outputConsole != null)
            {
                _outputConsole.Clear();

                IEnumerable<StudioLogEntry> entries = _logEntries;
                if (_outputFilter == OutputFilter.Warnings)
                {
                    entries = entries.Where(e => e.Level == LogLevel.Warning);
                }
                else if (_outputFilter == OutputFilter.Errors)
                {
                    entries = entries.Where(e => e.Level == LogLevel.Error);
                }

                int added = 0;
                foreach (var entry in entries)
                {
                    var line = new Label(entry.Message);
                    line.AddToClassList("console-line");
                    if (entry.Level == LogLevel.Warning) line.AddToClassList("warn");
                    if (entry.Level == LogLevel.Error) line.AddToClassList("error");
                    if (entry.Level == LogLevel.Info) line.AddToClassList("info");
                    _outputConsole.Add(line);
                    added++;
                    if (!string.IsNullOrWhiteSpace(entry.Detail))
                    {
                        var detail = new Label(entry.Detail);
                        detail.AddToClassList("console-line");
                        detail.AddToClassList("stack");
                        _outputConsole.Add(detail);
                    }
                }

                if (added == 0)
                {
                    var empty = new Label("No output to display.");
                    empty.AddToClassList("empty-state");
                    _outputConsole.Add(empty);
                }

                UpdateOutputCounts();
            }

            RenderNotifications();
        }

        private void UpdateOutputCounts()
        {
            if (_outputCountLabel == null) return;
            int warnings = _logEntries.Count(e => e.Level == LogLevel.Warning);
            int errors = _logEntries.Count(e => e.Level == LogLevel.Error);
            if (errors > 0)
            {
                _outputCountLabel.text = $"{errors} Errors";
                _outputCountLabel.AddToClassList("error");
            }
            else if (warnings > 0)
            {
                _outputCountLabel.text = $"{warnings} Warnings";
                _outputCountLabel.RemoveFromClassList("error");
            }
            else
            {
                _outputCountLabel.text = "0 Issues";
                _outputCountLabel.RemoveFromClassList("error");
            }
        }

        private void RenderNotifications()
        {
            if (_notificationStack == null) return;
            _notificationStack.Clear();

            var entries = _logEntries
                .Where(entry => entry.Level == LogLevel.Error || entry.Level == LogLevel.Warning)
                .Reverse()
                .Take(3)
                .Reverse()
                .ToList();

            foreach (var entry in entries)
            {
                var note = new VisualElement();
                note.AddToClassList("studio-note");
                if (entry.Level == LogLevel.Warning) note.AddToClassList("warning");

                var header = new VisualElement();
                header.AddToClassList("studio-note-header");
                var title = new Label(entry.Level == LogLevel.Error ? "Error" : "Warning");
                title.AddToClassList("studio-note-title");
                var entryRef = entry;
                var closeBtn = new Button(() =>
                {
                    _logEntries.Remove(entryRef);
                    UpdateOutputCounts();
                    RenderNotifications();
                })
                {
                    text = "X"
                };
                closeBtn.AddToClassList("studio-note-close");
                header.Add(title);
                header.Add(closeBtn);

                var body = new Label(entry.Message ?? string.Empty);
                body.AddToClassList("studio-note-body");

                note.Add(header);
                note.Add(body);
                if (!string.IsNullOrWhiteSpace(entry.Detail))
                {
                    var detail = new Label(entry.Detail);
                    detail.AddToClassList("studio-note-body");
                    note.Add(detail);
                }
                _notificationStack.Add(note);
            }
        }

        private void RegisterValidationBindings()
        {
            RegisterValidationField(GetField("ComponentNameField"));
            RegisterValidationField(GetField("ComponentIdField"));
            RegisterValidationField(_componentTypeDropdown);
            RegisterValidationField(GetField("ComponentTypeField"));
            RegisterValidationField(GetField("ComponentModelField"));
            RegisterValidationField(_symbolFileField);
            RegisterValidationField(GetField("ComponentSizeXField"));
            RegisterValidationField(GetField("ComponentSizeYField"));
            RegisterValidationField(GetField("ComponentPhysicsScriptField"));
            RegisterEffectField("ComponentEulerXField");
            RegisterEffectField("ComponentEulerYField");
            RegisterEffectField("ComponentEulerZField");
            RegisterEffectField("ComponentScaleXField");
            RegisterEffectField("ComponentScaleYField");
            RegisterEffectField("ComponentScaleZField");
            RegisterEffectField("ComponentLabelOffsetXField");
            RegisterEffectField("ComponentLabelOffsetYField");
            RegisterEffectField("ComponentLabelOffsetZField");
            RegisterEffectField("ComponentLedGlowOffsetXField");
            RegisterEffectField("ComponentLedGlowOffsetYField");
            RegisterEffectField("ComponentLedGlowOffsetZField");
            RegisterEffectField("ComponentHeatFxOffsetXField");
            RegisterEffectField("ComponentHeatFxOffsetYField");
            RegisterEffectField("ComponentHeatFxOffsetZField");
            RegisterEffectField("ComponentSparkFxOffsetXField");
            RegisterEffectField("ComponentSparkFxOffsetYField");
            RegisterEffectField("ComponentSparkFxOffsetZField");
            RegisterEffectField("ComponentErrorFxOffsetXField");
            RegisterEffectField("ComponentErrorFxOffsetYField");
            RegisterEffectField("ComponentErrorFxOffsetZField");
            RegisterEffectField("ComponentSmokeOffsetXField");
            RegisterEffectField("ComponentSmokeOffsetYField");
            RegisterEffectField("ComponentSmokeOffsetZField");
            RegisterEffectField("ComponentUsbOffsetXField");
            RegisterEffectField("ComponentUsbOffsetYField");
            RegisterEffectField("ComponentUsbOffsetZField");
            if (_anchorPickToggle != null) _anchorPickToggle.RegisterValueChangedCallback(_ => QueueValidation());
            if (_anchorShowToggle != null) _anchorShowToggle.RegisterValueChangedCallback(_ => QueueValidation());
            if (_partUseColorToggle != null) _partUseColorToggle.RegisterValueChangedCallback(_ => QueueValidation());
            if (_materialUseTextureToggle != null) _materialUseTextureToggle.RegisterValueChangedCallback(_ => QueueValidation());
            if (_materialUseColorToggle != null) _materialUseColorToggle.RegisterValueChangedCallback(_ => QueueValidation());
        }

        private void InitializeTypeDropdown()
        {
            if (_componentTypeDropdown == null) return;
            var choices = ComponentCatalog.Items
                .Select(item => item.Type)
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!choices.Any(choice => string.Equals(choice, "Generic", StringComparison.OrdinalIgnoreCase)))
            {
                choices.Insert(0, "Generic");
            }
            if (choices.Count == 0)
            {
                choices.Add("Generic");
            }
            _componentTypeDropdown.choices = choices;
        }

        private void RegisterValidationField(TextField field)
        {
            if (field == null) return;
            field.RegisterValueChangedCallback(_ => QueueValidation());
        }

        private void RegisterValidationField(DropdownField field)
        {
            if (field == null) return;
            field.RegisterValueChangedCallback(_ => QueueValidation());
        }

        private void RegisterAnchorField(TextField field)
        {
            if (field == null) return;
            field.RegisterValueChangedCallback(_ => RefreshAnchorGizmos());
        }

        private void RegisterEffectField(string fieldName)
        {
            var field = GetField(fieldName);
            if (field == null) return;
            field.RegisterValueChangedCallback(_ =>
            {
                RefreshEffectGizmos();
                var euler = ReadVector3Field("ComponentEulerXField", "ComponentEulerYField", "ComponentEulerZField");
                var scale = new Vector3(
                    ReadFloat(GetField("ComponentScaleXField"), 1f),
                    ReadFloat(GetField("ComponentScaleYField"), 1f),
                    ReadFloat(GetField("ComponentScaleZField"), 1f));
                _studioView?.ApplyModelTuning(euler, scale, _modelOffset);
                SyncLayoutSizeFromModelBounds();
                QueueValidation();
            });
        }

        private void QueueValidation()
        {
            if (_validationQueued || _root == null) return;
            _validationQueued = true;
            _root.schedule.Execute(() =>
            {
                _validationQueued = false;
                CaptureUndoSnapshot();
                RunValidation();
            }).ExecuteLater(150);
        }

        private void RunValidation()
        {
            var issues = new List<StudioLogEntry>();

            string name = GetField("ComponentNameField")?.value?.Trim();
            string id = SanitizeComponentId(GetField("ComponentIdField")?.value);
            string type = GetTypeValue();
            string modelPath = GetField("ComponentModelField")?.value?.Trim();
            float sizeX = ReadFloat(GetField("ComponentSizeXField"), 0f);
            float sizeY = ReadFloat(GetField("ComponentSizeYField"), 0f);

            if (string.IsNullOrWhiteSpace(name))
            {
                issues.Add(new StudioLogEntry { Level = LogLevel.Error, Message = "Name is required.", Source = "validation" });
            }
            if (string.IsNullOrWhiteSpace(type))
            {
                issues.Add(new StudioLogEntry { Level = LogLevel.Error, Message = "Type is required for catalog binding.", Source = "validation" });
            }
            if (string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
            {
                issues.Add(new StudioLogEntry { Level = LogLevel.Warning, Message = "Id will be auto-generated from name.", Source = "validation" });
            }
            if (sizeX <= 0f || sizeY <= 0f)
            {
                issues.Add(new StudioLogEntry { Level = LogLevel.Error, Message = "2D size must be greater than zero.", Source = "validation" });
            }
            bool hasModel = !string.IsNullOrWhiteSpace(modelPath);
            if (!hasModel && _payload != null && !string.IsNullOrWhiteSpace(_payload.modelFile))
            {
                hasModel = true;
            }

            if (!hasModel)
            {
                issues.Add(new StudioLogEntry { Level = LogLevel.Warning, Message = "No model assigned. 3D preview will be placeholder.", Source = "validation" });
            }
            else if (!string.IsNullOrWhiteSpace(modelPath) && !File.Exists(modelPath))
            {
                issues.Add(new StudioLogEntry { Level = LogLevel.Warning, Message = "Model path does not exist on disk.", Source = "validation" });
            }

            var pins = CollectPinLayout();
            if (pins.Count == 0)
            {
                issues.Add(new StudioLogEntry { Level = LogLevel.Warning, Message = "No pins defined. Wiring will be impossible.", Source = "validation" });
            }
            else
            {
                var dupes = pins.GroupBy(p => p.name, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                if (dupes.Count > 0)
                {
                    issues.Add(new StudioLogEntry
                    {
                        Level = LogLevel.Warning,
                        Message = $"Duplicate pins: {string.Join(", ", dupes)}",
                        Source = "validation"
                    });
                }
                foreach (var pin in pins)
                {
                    if (pin.x < 0f || pin.x > 1.2f || pin.y < 0f || pin.y > 1.2f)
                    {
                        issues.Add(new StudioLogEntry
                        {
                            Level = LogLevel.Warning,
                            Message = $"Pin {pin.name} is outside 2D bounds.",
                            Source = "validation"
                        });
                        break;
                    }
                }
            }

            var labels = CollectLabelLayout();
            foreach (var label in labels)
            {
                if (label.size < 0)
                {
                    issues.Add(new StudioLogEntry
                    {
                        Level = LogLevel.Warning,
                        Message = $"Label {label.text} has invalid size.",
                        Source = "validation"
                    });
                    break;
                }
            }

            string physicsScript = GetField("ComponentPhysicsScriptField")?.value?.Trim();
            if (!string.IsNullOrWhiteSpace(physicsScript) && !File.Exists(physicsScript))
            {
                issues.Add(new StudioLogEntry { Level = LogLevel.Warning, Message = "Physics script path is invalid.", Source = "validation" });
            }

            foreach (var part in _partOverrides.Values)
            {
                if (!part.UseTexture || string.IsNullOrWhiteSpace(part.TextureEntry)) continue;
                if (string.IsNullOrWhiteSpace(ResolveTexturePath(part.TextureEntry)))
                {
                    issues.Add(new StudioLogEntry
                    {
                        Level = LogLevel.Warning,
                        Message = $"Texture missing for part {part.Name}.",
                        Source = "validation"
                    });
                }
            }

            foreach (var state in _stateOverrides.Values)
            {
                foreach (var part in state.Parts.Values)
                {
                    if (!part.UseTexture || string.IsNullOrWhiteSpace(part.TextureEntry)) continue;
                    if (string.IsNullOrWhiteSpace(ResolveTexturePath(part.TextureEntry)))
                    {
                        issues.Add(new StudioLogEntry
                        {
                            Level = LogLevel.Warning,
                            Message = $"Texture missing for part {part.Name} in state {state.Id}.",
                            Source = "validation"
                        });
                    }
                }
            }

            _logEntries.RemoveAll(entry => string.Equals(entry.Source, "validation", StringComparison.OrdinalIgnoreCase));
            _logEntries.AddRange(issues);
            RenderOutput();
        }

        private void RunValidationAndNotify()
        {
            RunValidation();
            bool hasErrors = _logEntries.Any(entry =>
                entry.Level == LogLevel.Error &&
                string.Equals(entry.Source, "validation", StringComparison.OrdinalIgnoreCase));
            if (hasErrors)
            {
                SetStatus("Validation failed. Fix errors.", true);
            }
            else
            {
                SetStatus("Validation OK.", false);
                AddLog(LogLevel.Info, "Validation OK.", "validation");
            }
        }

        private void RunModelCheck()
        {
            RunValidation();
            bool hasErrors = _logEntries.Any(entry =>
                entry.Level == LogLevel.Error &&
                string.Equals(entry.Source, "validation", StringComparison.OrdinalIgnoreCase));
            if (hasErrors)
            {
                SetStatus("Run blocked by validation errors.", true);
                AddLog(LogLevel.Error, "Run blocked. Fix validation errors first.", "run");
                return;
            }
            SetStatus("Run OK. Model ready.", false);
            AddLog(LogLevel.Info, "Run OK. Model ready.", "run");
        }

        private bool EnsureValidationOkForSave()
        {
            RunValidation();
            bool hasErrors = _logEntries.Any(entry =>
                entry.Level == LogLevel.Error &&
                string.Equals(entry.Source, "validation", StringComparison.OrdinalIgnoreCase));
            if (hasErrors)
            {
                SetStatus("Fix validation errors before saving.", true);
                return false;
            }
            return true;
        }

        private void OpenMaterialModal()
        {
            if (_materialOverlay == null) return;
            if (!IsMaterialEditingAllowed(out var reason))
            {
                AddLog(LogLevel.Warning, reason, "material");
                SetMaterialPanelVisible(false);
                return;
            }
            UpdateMaterialModalFromSelection();
            SetMaterialPanelVisible(true);
        }

        private void SetMaterialPanelVisible(bool visible)
        {
            if (_materialOverlay == null) return;
            _materialOverlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible)
            {
                _activeTool = ToolAction.Material;
            }
            else if (_activeTool == ToolAction.Material)
            {
                _activeTool = ToolAction.Select;
            }
            UpdateToolButtons();
        }

        private void ToggleMaterialPanel()
        {
            if (_materialOverlay == null) return;
            bool visible = _materialOverlay.style.display == DisplayStyle.Flex;
            if (!visible)
            {
                OpenMaterialModal();
                return;
            }
            SetMaterialPanelVisible(false);
        }

        private void CloseMaterialModal()
        {
            SetMaterialPanelVisible(false);
        }

        private void UpdateMaterialModalFromSelection()
        {
            if (_materialTargetLabel != null)
            {
                string target = string.IsNullOrWhiteSpace(_selectedPartName) ? "No part selected" : _selectedPartName;
                if (!string.IsNullOrWhiteSpace(_selectedStateId))
                {
                    target = $"{target} [{_selectedStateId}]";
                }
                _materialTargetLabel.text = target;
            }

            var overrideData = GetActivePartOverride();
            if (overrideData != null)
            {
                if (_materialTextureField != null) _materialTextureField.SetValueWithoutNotify(ResolveTexturePath(overrideData.TextureEntry));
                if (_materialUseTextureToggle != null) _materialUseTextureToggle.SetValueWithoutNotify(overrideData.UseTexture);
                if (_materialColorRField != null) _materialColorRField.SetValueWithoutNotify(FormatFloat(overrideData.Color.r));
                if (_materialColorGField != null) _materialColorGField.SetValueWithoutNotify(FormatFloat(overrideData.Color.g));
                if (_materialColorBField != null) _materialColorBField.SetValueWithoutNotify(FormatFloat(overrideData.Color.b));
                if (_materialColorAField != null) _materialColorAField.SetValueWithoutNotify(FormatFloat(overrideData.Color.a));
                if (_materialUseColorToggle != null) _materialUseColorToggle.SetValueWithoutNotify(overrideData.UseColor);

                if (_materialPhysicalMaterialField != null) _materialPhysicalMaterialField.SetValueWithoutNotify(overrideData.PhysicalMaterialId ?? string.Empty);
                if (_materialDensityField != null) _materialDensityField.SetValueWithoutNotify(FormatFloat(overrideData.DensityKgPerM3));
                if (_materialVolumeField != null) _materialVolumeField.SetValueWithoutNotify(FormatFloat(overrideData.VolumeM3));
                if (_materialMassField != null) _materialMassField.SetValueWithoutNotify(FormatFloat(overrideData.MassKg));
                if (_materialFrictionField != null) _materialFrictionField.SetValueWithoutNotify(FormatFloat(overrideData.Friction));
                if (_materialElasticityField != null) _materialElasticityField.SetValueWithoutNotify(FormatFloat(overrideData.Elasticity));
                if (_materialStrengthField != null) _materialStrengthField.SetValueWithoutNotify(FormatFloat(overrideData.Strength));
            }
            else
            {
                if (_materialTextureField != null) _materialTextureField.SetValueWithoutNotify(string.Empty);
                if (_materialUseTextureToggle != null) _materialUseTextureToggle.SetValueWithoutNotify(false);
                if (_materialColorRField != null) _materialColorRField.SetValueWithoutNotify(string.Empty);
                if (_materialColorGField != null) _materialColorGField.SetValueWithoutNotify(string.Empty);
                if (_materialColorBField != null) _materialColorBField.SetValueWithoutNotify(string.Empty);
                if (_materialColorAField != null) _materialColorAField.SetValueWithoutNotify(string.Empty);
                if (_materialUseColorToggle != null) _materialUseColorToggle.SetValueWithoutNotify(false);

                if (_materialPhysicalMaterialField != null) _materialPhysicalMaterialField.SetValueWithoutNotify(string.Empty);
                if (_materialDensityField != null) _materialDensityField.SetValueWithoutNotify(string.Empty);
                if (_materialVolumeField != null) _materialVolumeField.SetValueWithoutNotify(string.Empty);
                if (_materialMassField != null) _materialMassField.SetValueWithoutNotify(string.Empty);
                if (_materialFrictionField != null) _materialFrictionField.SetValueWithoutNotify(string.Empty);
                if (_materialElasticityField != null) _materialElasticityField.SetValueWithoutNotify(string.Empty);
                if (_materialStrengthField != null) _materialStrengthField.SetValueWithoutNotify(string.Empty);
            }

            UpdateMaterialComputedMassLabel(overrideData);
        }

        private void UpdateMaterialComputedMassLabel(PartOverride overrideData)
        {
            if (_materialComputedMassLabel == null) return;
            if (overrideData == null || string.IsNullOrWhiteSpace(_selectedPartName))
            {
                _materialComputedMassLabel.text = "Estimated Mass: (unset)";
                return;
            }

            float mass = 0f;
            bool hasMass = false;

            if (overrideData.MassKg > 0f)
            {
                mass = overrideData.MassKg;
                hasMass = true;
            }
            else if (overrideData.VolumeM3 > 0f && overrideData.DensityKgPerM3 > 0f)
            {
                mass = overrideData.DensityKgPerM3 * overrideData.VolumeM3;
                hasMass = true;
            }
            else if (overrideData.DensityKgPerM3 > 0f && TryGetPartTransform(_selectedPartName, out var part) && part != null)
            {
                var renderer = part.GetComponentInChildren<Renderer>(true);
                if (renderer != null)
                {
                    var size = renderer.bounds.size;
                    float volume = Mathf.Abs(size.x * size.y * size.z);
                    if (volume > 0f)
                    {
                        mass = overrideData.DensityKgPerM3 * volume;
                        hasMass = true;
                    }
                }
            }

            _materialComputedMassLabel.text = hasMass ? $"Estimated Mass: {mass:0.###} kg" : "Estimated Mass: (unset)";
        }

        private void BrowseForTexture()
        {
#if UNITY_EDITOR
            string file = EditorUtility.OpenFilePanel("Select Texture", string.Empty, "png,jpg,jpeg,tga,tif,tiff,bmp,dds");
            if (!string.IsNullOrWhiteSpace(file))
            {
                if (_materialTextureField != null) _materialTextureField.SetValueWithoutNotify(file);
            }
#else
            if (TryPickFileRuntime("Select Texture", "Texture|*.png;*.jpg;*.jpeg;*.tga;*.tif;*.tiff;*.bmp;*.dds|All Files|*.*", out var file))
            {
                if (_materialTextureField != null) _materialTextureField.SetValueWithoutNotify(file);
            }
#endif
        }

        private void BrowseForSymbol()
        {
#if UNITY_EDITOR
            string file = EditorUtility.OpenFilePanel("Select Symbol", string.Empty, "png,svg");
            if (!string.IsNullOrWhiteSpace(file))
            {
                SetTextValue("ComponentSymbolFileField", file);
            }
#else
            if (TryPickFileRuntime("Select Symbol", "Symbol|*.png;*.svg|All Files|*.*", out var file))
            {
                SetTextValue("ComponentSymbolFileField", file);
            }
#endif
        }

        private void ApplyMaterialFromModal()
        {
            if (string.IsNullOrWhiteSpace(_selectedPartName))
            {
                AddLog(LogLevel.Warning, "Select a part before applying material.", "material");
                return;
            }
            if (!TryGetPartTransform(_selectedPartName, out var part)) return;

            var overrideData = GetOrCreateActivePartOverride(part);
            bool useTexture = _materialUseTextureToggle != null && _materialUseTextureToggle.value;
            bool useColor = _materialUseColorToggle != null && _materialUseColorToggle.value;
            overrideData.UseColor = useColor;
            overrideData.Color = new Color(
                ReadFloat(_materialColorRField, 1f),
                ReadFloat(_materialColorGField, 1f),
                ReadFloat(_materialColorBField, 1f),
                ReadFloat(_materialColorAField, 1f));
            if (_partUseColorToggle != null) _partUseColorToggle.SetValueWithoutNotify(useColor);
            SetField(_partColorRField, FormatFloat(overrideData.Color.r));
            SetField(_partColorGField, FormatFloat(overrideData.Color.g));
            SetField(_partColorBField, FormatFloat(overrideData.Color.b));
            SetField(_partColorAField, FormatFloat(overrideData.Color.a));

            overrideData.UseTexture = useTexture;
            overrideData.TextureEntry = string.Empty;
            string texturePath = _materialTextureField?.value?.Trim();
            if (useTexture && !string.IsNullOrWhiteSpace(texturePath))
            {
                if (TryPrepareTexture(texturePath, out var entryName, out _))
                {
                    overrideData.TextureEntry = entryName;
                }
                else
                {
                    AddLog(LogLevel.Warning, "Texture could not be prepared for packaging.", "material");
                    overrideData.UseTexture = false;
                }
            }
            else if (useTexture)
            {
                overrideData.UseTexture = false;
                AddLog(LogLevel.Warning, "Texture path is empty.", "material");
            }

            overrideData.PhysicalMaterialId = _materialPhysicalMaterialField?.value?.Trim() ?? string.Empty;
            overrideData.DensityKgPerM3 = ReadFloat(_materialDensityField, 0f);
            overrideData.VolumeM3 = ReadFloat(_materialVolumeField, 0f);
            overrideData.MassKg = ReadFloat(_materialMassField, 0f);
            overrideData.Friction = ReadFloat(_materialFrictionField, 0f);
            overrideData.Elasticity = ReadFloat(_materialElasticityField, 0f);
            overrideData.Strength = ReadFloat(_materialStrengthField, 0f);

            ApplySelectedPartOverride();
            UpdateMaterialComputedMassLabel(overrideData);
            QueueValidation();
        }

        private void ClearMaterialOverrides()
        {
            if (string.IsNullOrWhiteSpace(_selectedPartName)) return;
            if (!TryGetPartTransform(_selectedPartName, out var part)) return;
            var overrideData = GetOrCreateActivePartOverride(part);
            overrideData.TextureEntry = string.Empty;
            overrideData.UseTexture = false;

            overrideData.PhysicalMaterialId = string.Empty;
            overrideData.DensityKgPerM3 = 0f;
            overrideData.MassKg = 0f;
            overrideData.VolumeM3 = 0f;
            overrideData.Friction = 0f;
            overrideData.Elasticity = 0f;
            overrideData.Strength = 0f;

            ApplyPartMaterial(part, overrideData, null);
            if (_materialTextureField != null) _materialTextureField.SetValueWithoutNotify(string.Empty);
            if (_materialUseTextureToggle != null) _materialUseTextureToggle.SetValueWithoutNotify(false);

            if (_materialPhysicalMaterialField != null) _materialPhysicalMaterialField.SetValueWithoutNotify(string.Empty);
            if (_materialDensityField != null) _materialDensityField.SetValueWithoutNotify(string.Empty);
            if (_materialVolumeField != null) _materialVolumeField.SetValueWithoutNotify(string.Empty);
            if (_materialMassField != null) _materialMassField.SetValueWithoutNotify(string.Empty);
            if (_materialFrictionField != null) _materialFrictionField.SetValueWithoutNotify(string.Empty);
            if (_materialElasticityField != null) _materialElasticityField.SetValueWithoutNotify(string.Empty);
            if (_materialStrengthField != null) _materialStrengthField.SetValueWithoutNotify(string.Empty);
            UpdateMaterialComputedMassLabel(overrideData);
            QueueValidation();
        }

        private void ApplyPartMaterial(Transform part, PartOverride overrideData, string texturePath)
        {
            if (_studioView == null || part == null) return;
            Texture2D texture = null;
            if (!string.IsNullOrWhiteSpace(texturePath))
            {
                if (!_textureCache.TryGetValue(texturePath, out texture))
                {
                    texture = LoadTexture(texturePath);
                    if (texture != null) _textureCache[texturePath] = texture;
                }
            }
            _studioView.SetPartMaterial(part, overrideData.UseColor, overrideData.Color, texture);
        }

        private Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                var data = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(data))
                {
                    tex.name = Path.GetFileNameWithoutExtension(path);
                    return tex;
                }
                Destroy(tex);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ComponentStudio] Failed to load texture: {ex.Message}");
            }
            return null;
        }

        private bool TryPrepareTexture(string sourcePath, out string entryName, out string localPath)
        {
            entryName = string.Empty;
            localPath = string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return false;
            string modelPath = GetField("ComponentModelField")?.value?.Trim();
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                AddLog(LogLevel.Warning, "Select a model before assigning textures.", "material");
                return false;
            }
            string modelDir = Path.GetDirectoryName(modelPath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(modelDir)) return false;
            string texturesDir = Path.Combine(modelDir, "textures");
            Directory.CreateDirectory(texturesDir);
            string fileName = Path.GetFileName(sourcePath);
            string destPath = Path.Combine(texturesDir, fileName);
            if (!string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, destPath, true);
            }
            localPath = destPath;
            string relative = Path.GetRelativePath(modelDir, destPath);
            entryName = ComponentPackageUtility.BuildAssetEntryName(relative);
            return true;
        }

        private string ResolveTexturePath(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName)) return string.Empty;
            if (!string.IsNullOrWhiteSpace(_packagePath) &&
                File.Exists(_packagePath) &&
                ComponentPackageUtility.IsPackagePath(_packagePath) &&
                ComponentPackageUtility.TryExtractEntryToCache(_packagePath, entryName, out var extracted))
            {
                return extracted;
            }

            string modelPath = GetField("ComponentModelField")?.value?.Trim();
            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                string modelDir = Path.GetDirectoryName(modelPath) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(modelDir))
                {
                    string relative = entryName.Replace('\\', '/');
                    if (relative.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        relative = relative.Substring("assets/".Length);
                    }
                    relative = relative.Replace('/', Path.DirectorySeparatorChar);
                    string candidate = Path.Combine(modelDir, relative);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return entryName;
        }

        private string ResolveSymbolPath(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName)) return string.Empty;
            if (File.Exists(entryName)) return entryName;
            if (!string.IsNullOrWhiteSpace(_packagePath) &&
                File.Exists(_packagePath) &&
                ComponentPackageUtility.IsPackagePath(_packagePath) &&
                ComponentPackageUtility.TryExtractEntryToCache(_packagePath, entryName, out var extracted))
            {
                return extracted;
            }

            string modelPath = GetField("ComponentModelField")?.value?.Trim();
            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                string modelDir = Path.GetDirectoryName(modelPath) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(modelDir))
                {
                    string relative = entryName.Replace('\\', '/');
                    if (relative.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        relative = relative.Substring("assets/".Length);
                    }
                    relative = relative.Replace('/', Path.DirectorySeparatorChar);
                    string candidate = Path.Combine(modelDir, relative);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return string.Empty;
        }

        private PartOverride GetActivePartOverride()
        {
            if (string.IsNullOrWhiteSpace(_selectedPartName)) return null;
            if (!string.IsNullOrWhiteSpace(_selectedStateId) &&
                _stateOverrides.TryGetValue(_selectedStateId, out var state) &&
                state.Parts.TryGetValue(_selectedPartName, out var stateOverride))
            {
                return stateOverride;
            }

            if (_partOverrides.TryGetValue(_selectedPartName, out var partOverride))
            {
                return partOverride;
            }
            return null;
        }

        private PartOverride GetOrCreateActivePartOverride(Transform part)
        {
            if (part == null) return null;
            if (!string.IsNullOrWhiteSpace(_selectedStateId))
            {
                if (!_stateOverrides.TryGetValue(_selectedStateId, out var state))
                {
                    state = new StateOverride(_selectedStateId);
                    _stateOverrides[_selectedStateId] = state;
                }
                if (!state.Parts.TryGetValue(part.name, out var stateOverride))
                {
                    stateOverride = new PartOverride(part);
                    state.Parts[part.name] = stateOverride;
                }
                return stateOverride;
            }

            if (!_partOverrides.TryGetValue(part.name, out var partOverride))
            {
                partOverride = new PartOverride(part);
                _partOverrides[part.name] = partOverride;
            }
            return partOverride;
        }

        private void TryAddStateFromMenu()
        {
            if (_stateNameField == null)
            {
                AddLog(LogLevel.Warning, "State field not found in UI.", "state");
                return;
            }
            if (string.IsNullOrWhiteSpace(_stateNameField.value))
            {
                _stateNameField.SetValueWithoutNotify("state");
                _stateNameField.Focus();
                AddLog(LogLevel.Info, "Enter a state name and press Add.", "state");
                return;
            }
            AddStateOverride();
        }

        private void ToggleHelpTooltip()
        {
            if (_helpTooltip == null) return;
            _helpTooltip.style.display = _helpTooltip.style.display == DisplayStyle.Flex ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void ShowHelpOverlay(HelpOverlaySection section)
        {
            if (_helpOverlay == null) return;
            bool showShortcuts = section == HelpOverlaySection.Shortcuts;
            SetPanelVisible(_helpOverlayShortcuts, showShortcuts);
            SetPanelVisible(_helpOverlayAbout, !showShortcuts);
            if (_helpOverlayTitle != null)
            {
                _helpOverlayTitle.text = showShortcuts ? "Keyboard Shortcuts" : "About Component Studio";
            }
            if (_helpOverlayVersionLabel != null)
            {
                _helpOverlayVersionLabel.text = $"Version: v{Application.version}";
            }
            _helpOverlay.style.display = DisplayStyle.Flex;
        }

        private void HideHelpOverlay()
        {
            if (_helpOverlay == null) return;
            _helpOverlay.style.display = DisplayStyle.None;
        }

        private void ShowConfirmOverlay(string message, ConfirmOverlayAction action)
        {
            if (_confirmOverlay == null) return;
            _confirmAction = action;
            if (_confirmMessage != null) _confirmMessage.text = message;
            _confirmOverlay.style.display = DisplayStyle.Flex;
        }

        private void HideConfirmOverlay()
        {
            if (_confirmOverlay == null) return;
            _confirmAction = ConfirmOverlayAction.None;
            _confirmOverlay.style.display = DisplayStyle.None;
        }

        private void ConfirmOverlayAccept()
        {
            var action = _confirmAction;
            HideConfirmOverlay();
            if (action == ConfirmOverlayAction.ReturnToWizard)
            {
                ExecuteReturnToWizard();
            }
        }

        private void AutoAddPinsFromCurrent()
        {
            if (_pinsContainer == null) return;
            string type = GetTypeValue();
            if (string.IsNullOrWhiteSpace(type))
            {
                AddLog(LogLevel.Warning, "Set component type before auto-adding pins.", "pins");
                return;
            }

            var item = ComponentCatalog.GetByType(type);
            if (string.IsNullOrWhiteSpace(item.Type))
            {
                AddLog(LogLevel.Warning, $"No catalog definition found for type '{type}'.", "pins");
                return;
            }

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _pinsContainer.Children())
            {
                string pinName = entry.Q<TextField>("PinNameField")?.value?.Trim();
                if (!string.IsNullOrWhiteSpace(pinName)) existing.Add(pinName);
            }

            int added = 0;
            if (item.PinLayout != null && item.PinLayout.Count > 0)
            {
                foreach (var layout in item.PinLayout)
                {
                    if (string.IsNullOrWhiteSpace(layout.Name) || existing.Contains(layout.Name)) continue;
                    AddPinRow(new PinLayoutPayload
                    {
                        name = layout.Name,
                        x = layout.Position.x,
                        y = layout.Position.y,
                        label = layout.Label,
                        labelOffsetX = layout.LabelOffset.x,
                        labelOffsetY = layout.LabelOffset.y,
                        labelSize = layout.LabelSize,
                        anchorX = layout.AnchorLocal.x,
                        anchorY = layout.AnchorLocal.y,
                        anchorZ = layout.AnchorLocal.z,
                        anchorRadius = layout.AnchorRadius,
                        pinType = layout.PinType
                    });
                    added++;
                }
            }
            else if (item.Pins != null && item.Pins.Count > 0)
            {
                foreach (var pin in item.Pins)
                {
                    if (string.IsNullOrWhiteSpace(pin) || existing.Contains(pin)) continue;
                    AddPinRow(new PinLayoutPayload { name = pin });
                    added++;
                }
            }

            AddLog(LogLevel.Info, added > 0 ? $"Auto-added {added} pins from catalog." : "No new pins were added.", "pins");
            QueueValidation();
        }

        private void RegisterKeyboardShortcuts()
        {
            if (_root == null) return;
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _root.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
        }

        private void InitializeHelp()
        {
            if (_helpTooltip != null)
            {
                _helpTooltip.style.display = DisplayStyle.None;
            }
            if (_helpIcon == null || _helpTooltip == null) return;
            _helpIcon.RegisterCallback<ClickEvent>(evt =>
            {
                bool isVisible = _helpTooltip.style.display == DisplayStyle.Flex;
                _helpTooltip.style.display = isVisible ? DisplayStyle.None : DisplayStyle.Flex;
                evt.StopPropagation();
            });
            if (_root != null)
            {
                _root.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (IsHelpPointerTarget(evt)) return;
                    if (_helpTooltip?.style.display == DisplayStyle.Flex)
                    {
                        _helpTooltip.style.display = DisplayStyle.None;
                    }
                }, TrickleDown.TrickleDown);
            }
        }

        private bool IsMaterialEditingAllowed(out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(_selectedPartName))
            {
                reason = "Select a part to edit materials.";
                return false;
            }
            if (!TryGetSelectedPart(out var part) || part == null)
            {
                reason = "Selected part is not available.";
                return false;
            }
            if (!PartSupportsTexture(part))
            {
                reason = "Selected part does not support texture overrides.";
                return false;
            }
            return true;
        }

        private static bool PartSupportsTexture(Transform part)
        {
            if (part == null) return false;
            var renderers = part.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var mats = renderer.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;
                foreach (var mat in mats)
                {
                    if (mat == null) continue;
                    if (mat.HasProperty("_BaseMap") || mat.HasProperty("_MainTex"))
                    {
                        return true;
                    }
                    if (mat.mainTexture != null)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsHelpPointerTarget(PointerDownEvent evt)
        {
            if (evt.target is VisualElement target)
            {
                if (_helpIcon != null && (_helpIcon == target || _helpIcon.Contains(target)))
                {
                    return true;
                }
                if (_helpTooltip != null && (_helpTooltip == target || _helpTooltip.Contains(target)))
                {
                    return true;
                }
            }
            return false;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (TryHandleUndoRedo(evt))
            {
                evt.StopPropagation();
                return;
            }
            if (evt.target is VisualElement target && IsTextInputTarget(target))
            {
                return;
            }
            if (_is2dView && IsLayoutKeyTarget())
            {
                if (Handle2DKeyInput(evt))
                {
                    IgnoreEvent(evt);
                    evt.StopPropagation();
                    return;
                }
                if (evt.keyCode == KeyCode.Escape && HandleEscapeKey())
                {
                    IgnoreEvent(evt);
                    evt.StopPropagation();
                    return;
                }
            }
            if (!_is2dView && IsViewportKeyTarget())
            {
                if (Handle3DKeyInput(evt))
                {
                    IgnoreEvent(evt);
                    evt.StopPropagation();
                    return;
                }
                if (evt.keyCode == KeyCode.Escape && HandleEscapeKey())
                {
                    IgnoreEvent(evt);
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
            if (evt.ctrlKey && evt.shiftKey && evt.keyCode == KeyCode.M)
            {
                ToggleMaterialPanel();
                evt.StopPropagation();
                return;
            }
            if (evt.ctrlKey && evt.shiftKey && evt.keyCode == KeyCode.S)
            {
                SavePackageAs();
                evt.StopPropagation();
                return;
            }
            if (evt.ctrlKey && evt.keyCode == KeyCode.S)
            {
                SavePackageOnly();
                evt.StopPropagation();
                return;
            }
            if (evt.ctrlKey && evt.keyCode == KeyCode.O)
            {
                OpenPackageFromDialog();
                evt.StopPropagation();
                return;
            }
            if (evt.ctrlKey && evt.keyCode == KeyCode.M)
            {
                BrowseForModel();
                evt.StopPropagation();
                return;
            }
            if (evt.ctrlKey && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
            {
                RunModelCheck();
                evt.StopPropagation();
                return;
            }
            if (_is2dView) return;
            if (!evt.ctrlKey && !evt.shiftKey && !evt.altKey && evt.keyCode == KeyCode.P)
            {
                ActivateTool(ToolAction.Pin);
                evt.StopPropagation();
                return;
            }
            if (!evt.ctrlKey && !evt.shiftKey && !evt.altKey && evt.keyCode == KeyCode.L)
            {
                ActivateTool(ToolAction.Label);
                evt.StopPropagation();
                return;
            }
            if (evt.keyCode == KeyCode.F1)
            {
                ToggleHelpTooltip();
                evt.StopPropagation();
                return;
            }
        }

        private bool TryHandleUndoRedo(KeyDownEvent evt)
        {
            if (evt == null || !evt.ctrlKey) return false;
            if (evt.shiftKey && evt.keyCode == KeyCode.Z)
            {
                Redo();
                return true;
            }
            if (evt.keyCode == KeyCode.Z)
            {
                Undo();
                return true;
            }
            if (evt.keyCode == KeyCode.Y)
            {
                Redo();
                return true;
            }
            return false;
        }

        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (_is2dView)
            {
                if (!IsLayoutKeyTarget()) return;
                IgnoreEvent(evt);
                evt.StopPropagation();
                return;
            }
            if (!IsViewportKeyTarget()) return;
            IgnoreEvent(evt);
            evt.StopPropagation();
        }

        private void IgnoreEvent(EventBase evt)
        {
            _root?.panel?.focusController?.IgnoreEvent(evt);
        }

        private static bool IsTextInputTarget(VisualElement target)
        {
            for (var element = target; element != null; element = element.parent)
            {
                if (element is TextField) return true;
                if (element is DropdownField) return true;
                if (element.ClassListContains("unity-text-input")) return true;
                if (element.ClassListContains("unity-base-dropdown")) return true;
            }
            return false;
        }

        private bool HandleEscapeKey()
        {
            bool handled = false;
            if (_menuLayer != null && _menuLayer.style.display == DisplayStyle.Flex)
            {
                HideMenus();
                handled = true;
            }
            if (_layoutContextMenu != null && _layoutContextMenu.style.display == DisplayStyle.Flex)
            {
                HideLayoutContextMenu();
                handled = true;
            }
            if (_materialOverlay != null && _materialOverlay.style.display == DisplayStyle.Flex)
            {
                CloseMaterialModal();
                handled = true;
            }
            if (_helpTooltip != null && _helpTooltip.style.display == DisplayStyle.Flex)
            {
                _helpTooltip.style.display = DisplayStyle.None;
                handled = true;
            }
            if (_helpOverlay != null && _helpOverlay.style.display == DisplayStyle.Flex)
            {
                HideHelpOverlay();
                handled = true;
            }
            if (_confirmOverlay != null && _confirmOverlay.style.display == DisplayStyle.Flex)
            {
                HideConfirmOverlay();
                handled = true;
            }
            if (_layoutHelpTooltip != null && _layoutHelpTooltip.style.display == DisplayStyle.Flex)
            {
                _layoutHelpTooltip.style.display = DisplayStyle.None;
                handled = true;
            }
            if (_catalogDragging)
            {
                CancelCatalogDrag();
                handled = true;
            }
            if (IsAnchorPickEnabled())
            {
                SetAnchorPick(false);
                handled = true;
            }
            if (_isViewportEditDragging)
            {
                EndViewportEdit();
                handled = true;
            }
            if (_orbiting || _panning)
            {
                _orbiting = false;
                _panning = false;
                ReleaseViewportPointerCapture();
                handled = true;
            }
            if (_placingShape)
            {
                CancelPlacingShape();
                StopLayoutInteractions();
                handled = true;
            }
            if (_draggingPin || _draggingLabel || _draggingShape || _layoutPanning)
            {
                StopLayoutInteractions();
                handled = true;
            }
            return handled;
        }

        private bool Handle3DKeyInput(KeyDownEvent evt)
        {
            if (_studioView == null) return false;

            if (evt.altKey)
            {
                if (evt.keyCode == KeyCode.UpArrow)
                {
                    _studioView.SnapView(ComponentStudioView.ViewPreset.Top);
                    _studioView.FrameModel();
                    return true;
                }
                if (evt.keyCode == KeyCode.DownArrow)
                {
                    _studioView.SnapView(ComponentStudioView.ViewPreset.Bottom);
                    _studioView.FrameModel();
                    return true;
                }
                if (evt.keyCode == KeyCode.LeftArrow)
                {
                    _studioView.SnapView(ComponentStudioView.ViewPreset.Left);
                    _studioView.FrameModel();
                    return true;
                }
                if (evt.keyCode == KeyCode.RightArrow)
                {
                    _studioView.SnapView(ComponentStudioView.ViewPreset.Right);
                    _studioView.FrameModel();
                    return true;
                }
            }
            if (evt.keyCode == KeyCode.Alpha1)
            {
                SetViewportEditMode(ViewportEditMode.Move);
                return true;
            }
            if (evt.keyCode == KeyCode.Alpha2)
            {
                SetViewportEditMode(ViewportEditMode.Rotate);
                return true;
            }
            if (evt.keyCode == KeyCode.Alpha3)
            {
                SetViewportEditMode(ViewportEditMode.Scale);
                return true;
            }
            if (evt.keyCode == KeyCode.G)
            {
                if (_viewportEditSnapToggle != null)
                {
                    _viewportEditSnapToggle.SetValueWithoutNotify(!_viewportEditSnapToggle.value);
                }
                return true;
            }
            if (evt.keyCode == KeyCode.R || evt.keyCode == KeyCode.Home)
            {
                _studioView.ResetView();
                return true;
            }
            if (evt.keyCode == KeyCode.F)
            {
                _studioView.FrameModel();
                return true;
            }
            if (evt.keyCode == KeyCode.O)
            {
                SetAnchorVisible(!IsAnchorVisible());
                RefreshAnchorGizmos();
                return true;
            }
            if (!evt.ctrlKey && !evt.shiftKey && !evt.altKey && evt.keyCode == KeyCode.C)
            {
                SetPartColorsEnabled(!_partColorsEnabled, true);
                return true;
            }

            float nudge = evt.shiftKey ? CameraKeyPanShift : CameraKeyPanBase;
            if (evt.keyCode == KeyCode.W)
            {
                if (evt.ctrlKey) return false;
                _studioView.NudgePanCamera(new Vector2(0f, nudge));
                return true;
            }
            if (evt.keyCode == KeyCode.S)
            {
                if (evt.ctrlKey) return false;
                _studioView.NudgePanCamera(new Vector2(0f, -nudge));
                return true;
            }
            if (evt.keyCode == KeyCode.A)
            {
                if (evt.ctrlKey) return false;
                _studioView.NudgePanCamera(new Vector2(-nudge, 0f));
                return true;
            }
            if (evt.keyCode == KeyCode.D)
            {
                if (evt.ctrlKey) return false;
                _studioView.NudgePanCamera(new Vector2(nudge, 0f));
                return true;
            }
            if (evt.keyCode == KeyCode.PageUp || (evt.shiftKey && evt.keyCode == KeyCode.E))
            {
                _studioView.NudgePanCameraVertical(nudge);
                return true;
            }
            if (evt.keyCode == KeyCode.PageDown || (evt.shiftKey && evt.keyCode == KeyCode.Q))
            {
                _studioView.NudgePanCameraVertical(-nudge);
                return true;
            }

            bool isArrow = evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow ||
                           evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow;
            if (!isArrow) return false;

            if (evt.ctrlKey)
            {
                if (evt.keyCode == KeyCode.LeftArrow)
                {
                    _studioView.AdjustLightingBlend(-0.1f);
                    return true;
                }
                if (evt.keyCode == KeyCode.RightArrow)
                {
                    _studioView.AdjustLightingBlend(0.1f);
                    return true;
                }
                if (evt.keyCode == KeyCode.UpArrow)
                {
                    _studioView.Zoom(CameraKeyZoomDelta * CameraKeyZoomScale);
                    return true;
                }
                if (evt.keyCode == KeyCode.DownArrow)
                {
                    _studioView.Zoom(-CameraKeyZoomDelta * CameraKeyZoomScale);
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
                _studioView.NudgePanCamera(axes * nudge);
                return true;
            }

            var orbit = Vector2.zero;
            if (evt.keyCode == KeyCode.LeftArrow) orbit = new Vector2(CameraKeyOrbitPixels, 0f);
            if (evt.keyCode == KeyCode.RightArrow) orbit = new Vector2(-CameraKeyOrbitPixels, 0f);
            if (evt.keyCode == KeyCode.UpArrow) orbit = new Vector2(0f, -CameraKeyOrbitPixels);
            if (evt.keyCode == KeyCode.DownArrow) orbit = new Vector2(0f, CameraKeyOrbitPixels);
            _studioView.Orbit(orbit);
            return true;
        }

        private bool Handle2DKeyInput(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.R || evt.keyCode == KeyCode.F)
            {
                ResetLayoutView();
                return true;
            }
            if (evt.keyCode == KeyCode.G)
            {
                ToggleLayoutGrid();
                return true;
            }
            if (!evt.ctrlKey && !evt.shiftKey && !evt.altKey && evt.keyCode == KeyCode.P)
            {
                SetLayoutTool(LayoutTool.Pin);
                return true;
            }
            if (!evt.ctrlKey && !evt.shiftKey && !evt.altKey && evt.keyCode == KeyCode.L)
            {
                SetLayoutTool(LayoutTool.Label);
                return true;
            }
            if (!evt.ctrlKey && !evt.shiftKey && !evt.altKey && evt.keyCode == KeyCode.E)
            {
                SetLayoutTool(LayoutTool.Rotate);
                return true;
            }
            if (!evt.ctrlKey && !evt.shiftKey && !evt.altKey && evt.keyCode == KeyCode.S)
            {
                SetLayoutTool(LayoutTool.Scale);
                return true;
            }
            if (!evt.ctrlKey && !evt.shiftKey && !evt.altKey && evt.keyCode == KeyCode.B)
            {
                SetLayoutTool(LayoutTool.Box);
                return true;
            }
            if (!evt.ctrlKey && !evt.shiftKey && !evt.altKey && evt.keyCode == KeyCode.T)
            {
                SetLayoutTool(LayoutTool.Triangle);
                return true;
            }
            if (!evt.ctrlKey && !evt.shiftKey && !evt.altKey && evt.keyCode == KeyCode.N)
            {
                SetLayoutTool(LayoutTool.Line);
                return true;
            }
            if (!evt.ctrlKey && !evt.shiftKey && !evt.altKey && evt.keyCode == KeyCode.X)
            {
                SetLayoutTool(LayoutTool.Label);
                return true;
            }
            if (!evt.ctrlKey && !evt.shiftKey && !evt.altKey && evt.keyCode == KeyCode.C)
            {
                SetLayoutTool(LayoutTool.Circle);
                return true;
            }
            if (!evt.ctrlKey && !evt.shiftKey && !evt.altKey && evt.keyCode == KeyCode.V)
            {
                SetLayoutTool(LayoutTool.Select);
                return true;
            }
            if (evt.keyCode == KeyCode.Equals || evt.keyCode == KeyCode.KeypadPlus)
            {
                ApplyLayoutZoom(LayoutKeyZoomStep, GetLayoutZoomCenter());
                return true;
            }
            if (evt.keyCode == KeyCode.Minus || evt.keyCode == KeyCode.KeypadMinus)
            {
                ApplyLayoutZoom(-LayoutKeyZoomStep, GetLayoutZoomCenter());
                return true;
            }
            if (evt.ctrlKey && (evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow))
            {
                float zoomDelta = evt.keyCode == KeyCode.UpArrow ? LayoutKeyZoomStep : -LayoutKeyZoomStep;
                ApplyLayoutZoom(zoomDelta, GetLayoutZoomCenter());
                return true;
            }

            Vector2 pan = Vector2.zero;
            if (!evt.ctrlKey && (evt.keyCode == KeyCode.W || evt.keyCode == KeyCode.UpArrow)) pan = new Vector2(0f, LayoutKeyPanStep);
            if (!evt.ctrlKey && (evt.keyCode == KeyCode.S || evt.keyCode == KeyCode.DownArrow)) pan = new Vector2(0f, -LayoutKeyPanStep);
            if (!evt.ctrlKey && (evt.keyCode == KeyCode.A || evt.keyCode == KeyCode.LeftArrow)) pan = new Vector2(LayoutKeyPanStep, 0f);
            if (!evt.ctrlKey && (evt.keyCode == KeyCode.D || evt.keyCode == KeyCode.RightArrow)) pan = new Vector2(-LayoutKeyPanStep, 0f);
            if (pan != Vector2.zero)
            {
                _layoutPanOffset += pan * LayoutPanSpeed;
                RefreshLayoutPreview();
                return true;
            }

            return false;
        }

        private void Initialize3DView()
        {
            if (_viewport == null) return;
            if (_studioView == null)
            {
                var go = new GameObject("ComponentStudioView");
                go.transform.SetParent(transform, false);
                _studioView = go.AddComponent<ComponentStudioView>();
            }

            _viewport.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (_studioView == null) return;
                int width = Mathf.RoundToInt(evt.newRect.width);
                int height = Mathf.RoundToInt(evt.newRect.height);
                if (width <= 0 || height <= 0) return;
                _studioView.Initialize(width, height);
                Apply3DCameraSettings();
                if (_studioView.TargetTexture != null)
                {
                    _viewport.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_studioView.TargetTexture));
                }
                UpdateViewCubeSurface();
            });
        }
        private void LoadSession()
        {
            if (!ComponentStudioSession.HasSession)
            {
                _payload = CreateDefaultPayload();
                ApplyPayload(_payload);
                _studioView?.SetPlaceholderModel();
                RunValidation();
                ResetHistory();
                return;
            }

            _packagePath = ComponentStudioSession.PackagePath ?? string.Empty;
            _modelSourcePath = ComponentStudioSession.SourceModelPath ?? string.Empty;
            _payload = JsonUtility.FromJson<ComponentDefinitionPayload>(ComponentStudioSession.PayloadJson) ?? CreateDefaultPayload();
            ApplyPayload(_payload);

            LoadModelForPayload(_payload);
            RunValidation();
            ResetHistory();
        }

        private void LoadModelForPayload(ComponentDefinitionPayload payload)
        {
            if (_studioView == null || payload == null)
            {
                return;
            }

            string modelPath = _modelSourcePath;
            if (string.IsNullOrWhiteSpace(modelPath) && !string.IsNullOrWhiteSpace(payload.modelFile))
            {
                if (!string.IsNullOrWhiteSpace(_packagePath) &&
                    ComponentPackageUtility.IsPackagePath(_packagePath) &&
                    ComponentPackageUtility.TryExtractEntryToCache(_packagePath, payload.modelFile, out var extracted))
                {
                    modelPath = extracted;
                }
            }

            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                _studioView.SetPlaceholderModel();
                return;
            }

            string ext = Path.GetExtension(modelPath).ToLowerInvariant();
#if UNITY_EDITOR
            if (ext == ".fbx" || ext == ".obj")
            {
                string fallbackGlb = Path.ChangeExtension(modelPath, ".glb");
                if (File.Exists(fallbackGlb))
                {
                    modelPath = fallbackGlb;
                    ext = ".glb";
                }
            }
#endif
#if !UNITY_EDITOR
            if (ext == ".fbx" || ext == ".obj")
            {
                SetStatus("FBX/OBJ not supported at runtime. Convert to GLB first.", true);
                _studioView.SetPlaceholderModel();
                return;
            }
#endif

#if UNITY_EDITOR
            if (TryConvertModelToGlb(modelPath, out var convertedPath))
            {
                modelPath = convertedPath;
                ext = Path.GetExtension(modelPath).ToLowerInvariant();
            }
#endif

            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                _modelSourcePath = modelPath;
                SetField("ComponentModelField", modelPath);
            }

#if UNITY_EDITOR
            var assetPath = ToUnityAssetPath(modelPath);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab != null)
                {
                    _studioView.SetModel(prefab);
                    ApplyCurrentModelTuning();
                    RefreshPartsList();
                    RefreshAnchorGizmos();
                    RefreshEffectGizmos();
                    return;
                }
            }
#endif

            if (ext == ".fbx" || ext == ".obj")
            {
                SetStatus("FBX/OBJ needs Blender conversion. Export GLB to preview.", true);
                _studioView.SetPlaceholderModel();
                return;
            }

            BeginRuntimeModelLoad(modelPath);
        }

        private void BeginRuntimeModelLoad(string modelPath)
        {
            if (_studioView == null) return;
            CancelModelLoad(true);
            _modelLoadCts = new CancellationTokenSource();
            _modelLoadCts.CancelAfter(TimeSpan.FromSeconds(20));
            BeginModelLoad("Loading model...");
            LoadModelAsync(modelPath, _modelLoadCts.Token);
        }

        private async void LoadModelAsync(string modelPath, CancellationToken token)
        {
            await System.Threading.Tasks.Task.Yield();
            if (token.IsCancellationRequested)
            {
                EndModelLoad();
                return;
            }
            var instance = await RuntimeModelLoader.TryLoadModelAsync(modelPath, null, token);
            if (token.IsCancellationRequested)
            {
                if (instance != null) Destroy(instance);
                EndModelLoad();
                return;
            }
            if (instance == null)
            {
                _studioView.SetPlaceholderModel();
                EndModelLoad();
                return;
            }

            _studioView.SetModelInstance(instance);
            ApplyCurrentModelTuning();
            RefreshPartsList();
            RefreshAnchorGizmos();
            RefreshEffectGizmos();
            EndModelLoad();
        }

        private void ApplyPayload(ComponentDefinitionPayload payload)
        {
            if (payload == null) return;
            SetField("ComponentNameField", payload.name);
            SetField("ComponentIdField", payload.id);
            SetTypeValue(payload.type);
            SetField("ComponentDescriptionField", payload.description);
            SetField("ComponentPhysicsScriptField", payload.physicsScript);
            SetField("ComponentSymbolField", payload.symbol);
            SetTextValue("ComponentSymbolFileField", ResolveSymbolPath(payload.symbolFile));
            SetField("ComponentIconField", payload.iconChar);
            SetField("ComponentOrderField", payload.order.ToString(CultureInfo.InvariantCulture));
            SetField("ComponentModelField", _modelSourcePath);
            SetField("ComponentConfigField", string.Empty);

            SetField("ComponentSizeXField", payload.size2D.x.ToString("0.###", CultureInfo.InvariantCulture));
            SetField("ComponentSizeYField", payload.size2D.y.ToString("0.###", CultureInfo.InvariantCulture));

            _pinsContainer?.Clear();
            _labelsContainer?.Clear();
            _specsContainer?.Clear();
            _defaultsContainer?.Clear();
            _fxContainer?.Clear();
            _shapeLayout.Clear();
            _selectedShapeId = null;

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

            if (payload.shapes != null)
            {
                foreach (var shape in payload.shapes)
                {
                    if (shape == null) continue;
                    if (string.IsNullOrWhiteSpace(shape.id))
                    {
                        shape.id = $"shape_{Guid.NewGuid():N}".Substring(0, 8);
                    }
                    if (IsTextShapeType(shape.type))
                    {
                        if (shape.fontSize <= 0f)
                        {
                            shape.fontSize = LayoutTextDefaultFontSize;
                        }
                        shape.fontSize = Mathf.Max(LayoutTextMinFontSize, shape.fontSize);
                    }
                    _shapeLayout.Add(shape);
                }
            }

            RefreshLayoutPreview();

            if (payload.specs != null)
            {
                foreach (var entry in payload.specs)
                {
                    AddKeyValueRow(_specsContainer, entry.key, entry.value);
                }
            }

            if (payload.defaults != null)
            {
                foreach (var entry in payload.defaults)
                {
                    AddKeyValueRow(_defaultsContainer, entry.key, entry.value);
                }
            }

            if (payload.fx != null)
            {
                foreach (var fx in payload.fx)
                {
                    AddFxRow(fx);
                }
            }

            ApplyTuning(payload.tuning);
            BuildOverrides(payload);
            EnsureRequiredStates();
            RefreshPartsList();
            RefreshStatesList();
            RefreshEffectGizmos();
            QueueValidation();
            RefreshCatalogAndHierarchy();
        }

        private void EnsureRequiredStates()
        {
            string type = GetTypeValue();
            var required = GetRequiredStatesForType(type);
            if (required.Count == 0) return;
            bool changed = false;
            foreach (var stateId in required)
            {
                if (_stateOverrides.ContainsKey(stateId)) continue;
                _stateOverrides[stateId] = new StateOverride(stateId);
                changed = true;
            }
            if (changed)
            {
                RefreshStatesList();
                RefreshCatalogAndHierarchy();
            }
        }

        private static List<string> GetRequiredStatesForType(string type)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(type)) return list;
            string key = type.Trim().ToLowerInvariant();
            if (key.Contains("button") || key.Contains("momentary"))
            {
                list.Add("pressed");
                list.Add("released");
                return list;
            }
            if (key.Contains("switch") || key.Contains("toggle"))
            {
                list.Add("on");
                list.Add("off");
                return list;
            }
            if (key.Contains("led"))
            {
                list.Add("on");
                list.Add("off");
            }
            if (key.Contains("servo") || key.Contains("motor"))
            {
                list.Add("idle");
                list.Add("active");
            }
            return list;
        }

        private void BuildOverrides(ComponentDefinitionPayload payload)
        {
            _partOverrides.Clear();
            _stateOverrides.Clear();

            if (payload.parts != null)
            {
                foreach (var part in payload.parts)
                {
                    if (part == null || string.IsNullOrWhiteSpace(part.name)) continue;
                    _partOverrides[part.name] = new PartOverride(part);
                }
            }

            if (payload.states != null)
            {
                foreach (var state in payload.states)
                {
                    if (state == null || string.IsNullOrWhiteSpace(state.id)) continue;
                    _stateOverrides[state.id] = new StateOverride(state);
                }
            }
        }

        private void ApplyTuning(ComponentTuningPayload tuning)
        {
            if (tuning == null)
            {
                ClearTuning();
                return;
            }

            SetField("ComponentEulerXField", FormatFloat(tuning.Euler.x));
            SetField("ComponentEulerYField", FormatFloat(tuning.Euler.y));
            SetField("ComponentEulerZField", FormatFloat(tuning.Euler.z));
            SetField("ComponentScaleXField", FormatFloat(tuning.Scale.x));
            SetField("ComponentScaleYField", FormatFloat(tuning.Scale.y));
            SetField("ComponentScaleZField", FormatFloat(tuning.Scale.z));
            SetField("ComponentLedColorRField", FormatFloat(tuning.LedColor.r));
            SetField("ComponentLedColorGField", FormatFloat(tuning.LedColor.g));
            SetField("ComponentLedColorBField", FormatFloat(tuning.LedColor.b));
            SetField("ComponentLedColorAField", FormatFloat(tuning.LedColor.a));
            SetField("ComponentLedGlowRangeField", FormatFloat(tuning.LedGlowRange));
            SetField("ComponentLedGlowIntensityField", FormatFloat(tuning.LedGlowIntensity));
            SetField("ComponentLedBlowCurrentField", FormatFloat(tuning.LedBlowCurrent));
            SetField("ComponentLedBlowTempField", FormatFloat(tuning.LedBlowTemp));
            SetField("ComponentResistorSmokeStartField", FormatFloat(tuning.ResistorSmokeStartTemp));
            SetField("ComponentResistorSmokeFullField", FormatFloat(tuning.ResistorSmokeFullTemp));
            SetField("ComponentResistorHotStartField", FormatFloat(tuning.ResistorHotStartTemp));
            SetField("ComponentResistorHotFullField", FormatFloat(tuning.ResistorHotFullTemp));
            SetField("ComponentErrorFxIntervalField", FormatFloat(tuning.ErrorFxInterval));
            SetField("ComponentLabelOffsetXField", FormatFloat(tuning.LabelOffset.x));
            SetField("ComponentLabelOffsetYField", FormatFloat(tuning.LabelOffset.y));
            SetField("ComponentLabelOffsetZField", FormatFloat(tuning.LabelOffset.z));
            SetField("ComponentLedGlowOffsetXField", FormatFloat(tuning.LedGlowOffset.x));
            SetField("ComponentLedGlowOffsetYField", FormatFloat(tuning.LedGlowOffset.y));
            SetField("ComponentLedGlowOffsetZField", FormatFloat(tuning.LedGlowOffset.z));
            SetField("ComponentHeatFxOffsetXField", FormatFloat(tuning.HeatFxOffset.x));
            SetField("ComponentHeatFxOffsetYField", FormatFloat(tuning.HeatFxOffset.y));
            SetField("ComponentHeatFxOffsetZField", FormatFloat(tuning.HeatFxOffset.z));
            SetField("ComponentSparkFxOffsetXField", FormatFloat(tuning.SparkFxOffset.x));
            SetField("ComponentSparkFxOffsetYField", FormatFloat(tuning.SparkFxOffset.y));
            SetField("ComponentSparkFxOffsetZField", FormatFloat(tuning.SparkFxOffset.z));
            SetField("ComponentErrorFxOffsetXField", FormatFloat(tuning.ErrorFxOffset.x));
            SetField("ComponentErrorFxOffsetYField", FormatFloat(tuning.ErrorFxOffset.y));
            SetField("ComponentErrorFxOffsetZField", FormatFloat(tuning.ErrorFxOffset.z));
            SetField("ComponentSmokeOffsetXField", FormatFloat(tuning.SmokeOffset.x));
            SetField("ComponentSmokeOffsetYField", FormatFloat(tuning.SmokeOffset.y));
            SetField("ComponentSmokeOffsetZField", FormatFloat(tuning.SmokeOffset.z));
            SetField("ComponentUsbOffsetXField", FormatFloat(tuning.UsbOffset.x));
            SetField("ComponentUsbOffsetYField", FormatFloat(tuning.UsbOffset.y));
            SetField("ComponentUsbOffsetZField", FormatFloat(tuning.UsbOffset.z));
            _modelOffset = tuning.ModelOffset;
            _studioView?.ApplyModelTuning(tuning.Euler, tuning.Scale, _modelOffset);
            RefreshEffectGizmos();
            SyncLayoutSizeFromModelBounds();
        }

        private void ApplyCurrentModelTuning()
        {
            if (_studioView == null) return;
            var euler = ReadVector3Field("ComponentEulerXField", "ComponentEulerYField", "ComponentEulerZField");
            var scale = ReadScaleVector3Field("ComponentScaleXField", "ComponentScaleYField", "ComponentScaleZField");
            _studioView.ApplyModelTuning(euler, scale, _modelOffset);
            SyncLayoutSizeFromModelBounds();
        }

        private void SyncLayoutSizeFromModelBounds()
        {
            if (_studioView == null) return;
            if (!_studioView.TryGetModelBoundsLocal(out var bounds)) return;
            float sizeX = Mathf.Max(bounds.size.x, 0.001f);
            float sizeY = Mathf.Max(bounds.size.z, 0.001f);
            var sizeXField = GetField("ComponentSizeXField");
            var sizeYField = GetField("ComponentSizeYField");
            bool updated = false;
            if (sizeXField != null)
            {
                float current = ReadFloat(sizeXField, -1f);
                if (Mathf.Abs(current - sizeX) > 0.0005f)
                {
                    SetField("ComponentSizeXField", sizeX.ToString("0.###", CultureInfo.InvariantCulture));
                    updated = true;
                }
            }
            if (sizeYField != null)
            {
                float current = ReadFloat(sizeYField, -1f);
                if (Mathf.Abs(current - sizeY) > 0.0005f)
                {
                    SetField("ComponentSizeYField", sizeY.ToString("0.###", CultureInfo.InvariantCulture));
                    updated = true;
                }
            }
            if (updated)
            {
                RefreshLayoutPreview();
            }
        }

        private void ClearTuning()
        {
            SetField("ComponentEulerXField", string.Empty);
            SetField("ComponentEulerYField", string.Empty);
            SetField("ComponentEulerZField", string.Empty);
            SetField("ComponentScaleXField", string.Empty);
            SetField("ComponentScaleYField", string.Empty);
            SetField("ComponentScaleZField", string.Empty);
            SetField("ComponentLedColorRField", string.Empty);
            SetField("ComponentLedColorGField", string.Empty);
            SetField("ComponentLedColorBField", string.Empty);
            SetField("ComponentLedColorAField", string.Empty);
            SetField("ComponentLedGlowRangeField", string.Empty);
            SetField("ComponentLedGlowIntensityField", string.Empty);
            SetField("ComponentLedBlowCurrentField", string.Empty);
            SetField("ComponentLedBlowTempField", string.Empty);
            SetField("ComponentResistorSmokeStartField", string.Empty);
            SetField("ComponentResistorSmokeFullField", string.Empty);
            SetField("ComponentResistorHotStartField", string.Empty);
            SetField("ComponentResistorHotFullField", string.Empty);
            SetField("ComponentErrorFxIntervalField", string.Empty);
            SetField("ComponentLabelOffsetXField", string.Empty);
            SetField("ComponentLabelOffsetYField", string.Empty);
            SetField("ComponentLabelOffsetZField", string.Empty);
            SetField("ComponentLedGlowOffsetXField", string.Empty);
            SetField("ComponentLedGlowOffsetYField", string.Empty);
            SetField("ComponentLedGlowOffsetZField", string.Empty);
            SetField("ComponentHeatFxOffsetXField", string.Empty);
            SetField("ComponentHeatFxOffsetYField", string.Empty);
            SetField("ComponentHeatFxOffsetZField", string.Empty);
            SetField("ComponentSparkFxOffsetXField", string.Empty);
            SetField("ComponentSparkFxOffsetYField", string.Empty);
            SetField("ComponentSparkFxOffsetZField", string.Empty);
            SetField("ComponentErrorFxOffsetXField", string.Empty);
            SetField("ComponentErrorFxOffsetYField", string.Empty);
            SetField("ComponentErrorFxOffsetZField", string.Empty);
            SetField("ComponentSmokeOffsetXField", string.Empty);
            SetField("ComponentSmokeOffsetYField", string.Empty);
            SetField("ComponentSmokeOffsetZField", string.Empty);
            SetField("ComponentUsbOffsetXField", string.Empty);
            SetField("ComponentUsbOffsetYField", string.Empty);
            SetField("ComponentUsbOffsetZField", string.Empty);
            _modelOffset = Vector3.zero;
            _studioView?.ApplyModelTuning(Vector3.zero, Vector3.one, _modelOffset);
            RefreshEffectGizmos();
        }

        private void BrowseForModel()
        {
#if UNITY_EDITOR
            string file = EditorUtility.OpenFilePanel("Select Model", string.Empty, "fbx,obj,glb,gltf");
            if (!string.IsNullOrWhiteSpace(file))
            {
                SetField("ComponentModelField", file);
                _modelSourcePath = file;
                LoadModelForPayload(_payload);
                QueueValidation();
            }
#else
            if (TryPickFileRuntime("Select Model", "3D Models|*.glb;*.gltf;*.fbx;*.obj|All Files|*.*", out var file))
            {
                SetField("ComponentModelField", file);
                _modelSourcePath = file;
                LoadModelForPayload(_payload);
                QueueValidation();
            }
            else
            {
                SetStatus("Model selection not available. Paste the path manually.", true);
            }
#endif
        }

        private void BrowseForConfig()
        {
#if UNITY_EDITOR
            string file = EditorUtility.OpenFilePanel("Select Config", string.Empty, "json,rtcomp");
            if (!string.IsNullOrWhiteSpace(file))
            {
                SetField("ComponentConfigField", file);
            }
#else
            if (TryPickFileRuntime("Select Config", "Component Config|*.json;*.rtcomp|All Files|*.*", out var file))
            {
                SetField("ComponentConfigField", file);
            }
#endif
        }

        private void LoadConfigFromField()
        {
            string path = GetField("ComponentConfigField")?.value?.Trim();
            if (string.IsNullOrWhiteSpace(path)) return;

            string json = string.Empty;
            bool isPackageFile = File.Exists(path) && ComponentPackageUtility.IsPackagePath(path);
            string configPath = path;

            if (isPackageFile)
            {
                if (!ComponentPackageUtility.TryReadDefinitionJson(path, out json))
                {
                    SetStatus("Component package not found.", true);
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
                    SetStatus("Component config not found.", true);
                    return;
                }
                json = File.ReadAllText(configPath);
            }

            try
            {
                var payload = JsonUtility.FromJson<ComponentDefinitionPayload>(json);
                if (payload == null)
                {
                    SetStatus("Failed to parse component config.", true);
                    return;
                }
                _payload = payload;
                ApplyPayload(_payload);
                LoadModelForPayload(_payload);
                RunValidation();
                ResetHistory();
                SetStatus("Config loaded.", false);
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to load config: {ex.Message}", true);
            }
        }
        private void SavePackageAndReturn()
        {
            if (!EnsureValidationOkForSave()) return;
            var payload = BuildPayloadFromEditor();
            if (payload == null)
            {
                SetStatus("Invalid component data.", true);
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

            string modelSourcePath = GetField("ComponentModelField")?.value?.Trim();
            string sourceOriginal = modelSourcePath;
            if (TryConvertModelToGlb(modelSourcePath, out var glbPath))
            {
                modelSourcePath = glbPath;
                SetField("ComponentModelField", modelSourcePath);
            }
            else if (!string.IsNullOrWhiteSpace(modelSourcePath))
            {
                string ext = Path.GetExtension(modelSourcePath).ToLowerInvariant();
                if (ext == ".fbx" || ext == ".obj")
                {
                    string fallbackGlb = Path.ChangeExtension(modelSourcePath, ".glb");
                    if (File.Exists(fallbackGlb))
                    {
                        modelSourcePath = fallbackGlb;
                        SetField("ComponentModelField", modelSourcePath);
                    }
                    else
                    {
                        SetStatus("FBX/OBJ detected. Install Blender or export GLB for runtime builds.", true);
                    }
                }
            }
#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(sourceOriginal))
            {
                string outputDir = Path.GetDirectoryName(modelSourcePath) ?? Path.GetDirectoryName(sourceOriginal);
                TryExtractMaterialsFromFbx(sourceOriginal, outputDir);
            }
#endif

            string packagePath = ResolvePackagePath(payload);
            SaveComponentPackage(payload, packagePath, modelSourcePath);
            SetStatus("Saved component package.", false);
            MarkSavedSnapshot(BuildSnapshot());
            ReturnToWizard();
        }

        private void SavePackageOnly()
        {
            if (!EnsureValidationOkForSave()) return;
            var payload = BuildPayloadFromEditor();
            if (payload == null)
            {
                SetStatus("Invalid component data.", true);
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

            string modelSourcePath = GetField("ComponentModelField")?.value?.Trim();
            string sourceOriginal = modelSourcePath;
            if (TryConvertModelToGlb(modelSourcePath, out var glbPath))
            {
                modelSourcePath = glbPath;
                SetField("ComponentModelField", modelSourcePath);
            }
            else if (!string.IsNullOrWhiteSpace(modelSourcePath))
            {
                string ext = Path.GetExtension(modelSourcePath).ToLowerInvariant();
                if (ext == ".fbx" || ext == ".obj")
                {
                    string fallbackGlb = Path.ChangeExtension(modelSourcePath, ".glb");
                    if (File.Exists(fallbackGlb))
                    {
                        modelSourcePath = fallbackGlb;
                        SetField("ComponentModelField", modelSourcePath);
                    }
                    else
                    {
                        SetStatus("FBX/OBJ detected. Install Blender or export GLB for runtime builds.", true);
                    }
                }
            }
#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(sourceOriginal))
            {
                string outputDir = Path.GetDirectoryName(modelSourcePath) ?? Path.GetDirectoryName(sourceOriginal);
                TryExtractMaterialsFromFbx(sourceOriginal, outputDir);
            }
#endif

            string packagePath = ResolvePackagePath(payload);
            SaveComponentPackage(payload, packagePath, modelSourcePath);
            SetStatus("Saved component package.", false);
            MarkSavedSnapshot(BuildSnapshot());
        }

        private void SavePackageAs()
        {
            if (!EnsureValidationOkForSave()) return;
            var payload = BuildPayloadFromEditor();
            if (payload == null)
            {
                SetStatus("Invalid component data.", true);
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

            string modelSourcePath = GetField("ComponentModelField")?.value?.Trim();
            if (TryConvertModelToGlb(modelSourcePath, out var glbPath))
            {
                modelSourcePath = glbPath;
                SetField("ComponentModelField", modelSourcePath);
            }

            string packagePath = ResolvePackagePath(payload);
#if UNITY_EDITOR
            string baseName = SanitizeComponentId(payload.id);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "component";
            string initialDir = Path.GetDirectoryName(packagePath) ?? ComponentCatalog.GetUserComponentRoot();
            string chosen = EditorUtility.SaveFilePanel("Save Component Package", initialDir, baseName, "rtcomp");
            if (string.IsNullOrWhiteSpace(chosen)) return;
            if (!chosen.EndsWith(ComponentPackageUtility.PackageExtension, StringComparison.OrdinalIgnoreCase))
            {
                chosen += ComponentPackageUtility.PackageExtension;
            }
            packagePath = chosen;
#endif
            _packagePath = packagePath;
            SaveComponentPackage(payload, packagePath, modelSourcePath);
            SetStatus("Saved component package (Save As).", false);
            MarkSavedSnapshot(BuildSnapshot());
        }

        private void OpenPackageFromDialog()
        {
#if UNITY_EDITOR
            string root = ComponentCatalog.GetUserComponentRoot();
            string file = EditorUtility.OpenFilePanel("Open Component Config", root, "rtcomp,json");
            if (string.IsNullOrWhiteSpace(file)) return;
            SetField("ComponentConfigField", file);
            _packagePath = ComponentPackageUtility.IsPackagePath(file) ? file : string.Empty;
            _modelSourcePath = string.Empty;
            LoadConfigFromField();
#else
            SetStatus("Open not available at runtime. Paste the path manually.", true);
#endif
        }

        private void ReturnToWizard()
        {
            if (_hasUnsavedChanges)
            {
                ShowConfirmOverlay("You have unsaved changes. Return to wizard anyway?", ConfirmOverlayAction.ReturnToWizard);
                return;
            }
            ExecuteReturnToWizard();
        }

        private void ExecuteReturnToWizard()
        {
            string scene = string.IsNullOrWhiteSpace(ComponentStudioSession.ReturnScene) ? "Wizard" : ComponentStudioSession.ReturnScene;
            ComponentStudioSession.Clear();
            SceneManager.LoadScene(scene);
        }

        private VisualElement AddPinRow(PinLayoutPayload payload)
        {
            if (_pinsContainer == null) return null;
            var entry = new VisualElement();
            entry.AddToClassList("editor-entry");
            entry.RegisterCallback<PointerDownEvent>(_ => SelectPinEntry(entry));

            var mainRow = new VisualElement();
            mainRow.AddToClassList("editor-row");
            var nameField = CreateField("PinNameField", payload.name, "editor-field");
            var xField = CreateField("PinXField", FormatFloat(payload.x), "editor-field-small");
            var yField = CreateField("PinYField", FormatFloat(payload.y), "editor-field-small");
            var labelField = CreateField("PinLabelField", payload.label, "editor-field");
            var labelOffsetXField = CreateField("PinLabelOffsetXField", FormatFloat(payload.labelOffsetX), "editor-field-small");
            var labelOffsetYField = CreateField("PinLabelOffsetYField", FormatFloat(payload.labelOffsetY), "editor-field-small");
            var labelSizeField = CreateField("PinLabelSizeField", payload.labelSize > 0 ? payload.labelSize.ToString(CultureInfo.InvariantCulture) : string.Empty, "editor-field-small");
            mainRow.Add(nameField);
            mainRow.Add(xField);
            mainRow.Add(yField);
            mainRow.Add(labelField);
            mainRow.Add(labelOffsetXField);
            mainRow.Add(labelOffsetYField);
            mainRow.Add(labelSizeField);

            var removeBtn = new Button(() =>
            {
                var name = nameField?.value?.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    RemovePinEntryByName(name);
                }
                else
                {
                    if (_selectedPinEntry == entry) _selectedPinEntry = null;
                    _pinsContainer.Remove(entry);
                    RefreshAnchorGizmos();
                    RefreshLayoutPreview();
                    QueueValidation();
                    RefreshCatalogAndHierarchy();
                }
            });
            removeBtn.text = "X";
            removeBtn.AddToClassList("editor-remove-btn");
            mainRow.Add(removeBtn);
            entry.Add(mainRow);

            var anchorRow = new VisualElement();
            anchorRow.AddToClassList("editor-row");
            var anchorXField = CreateField("PinAnchorXField", FormatFloat(payload.anchorX), "editor-field-small");
            var anchorYField = CreateField("PinAnchorYField", FormatFloat(payload.anchorY), "editor-field-small");
            var anchorZField = CreateField("PinAnchorZField", FormatFloat(payload.anchorZ), "editor-field-small");
            var anchorRadiusField = CreateField("PinAnchorRadiusField", FormatFloat(payload.anchorRadius), "editor-field-small");
            var pinTypeField = CreateDropdownField("PinTypeField", NormalizePinType(payload.pinType), "editor-field-small", PinTypeChoices);
            anchorRow.Add(anchorXField);
            anchorRow.Add(anchorYField);
            anchorRow.Add(anchorZField);
            anchorRow.Add(anchorRadiusField);
            anchorRow.Add(pinTypeField);
            entry.Add(anchorRow);

            _pinsContainer.Add(entry);
            RefreshAnchorGizmos();
            if (nameField != null) nameField.RegisterValueChangedCallback(_ => RefreshLayoutPreview());
            if (xField != null) xField.RegisterValueChangedCallback(_ => RefreshLayoutPreview());
            if (yField != null) yField.RegisterValueChangedCallback(_ => RefreshLayoutPreview());
            RegisterValidationField(nameField);
            RegisterValidationField(xField);
            RegisterValidationField(yField);
            RegisterValidationField(labelField);
            RegisterValidationField(labelOffsetXField);
            RegisterValidationField(labelOffsetYField);
            RegisterValidationField(labelSizeField);
            RegisterValidationField(anchorXField);
            RegisterValidationField(anchorYField);
            RegisterValidationField(anchorZField);
            RegisterValidationField(anchorRadiusField);
            RegisterValidationField(pinTypeField);
            RegisterAnchorField(anchorXField);
            RegisterAnchorField(anchorYField);
            RegisterAnchorField(anchorZField);
            RegisterAnchorField(anchorRadiusField);
            if (pinTypeField != null) pinTypeField.RegisterValueChangedCallback(_ => RefreshAnchorGizmos());
            RefreshLayoutPreview();
            QueueValidation();
            RefreshCatalogAndHierarchy();
            return entry;
        }

        private void RemovePinEntryByName(string pinName)
        {
            if (_pinsContainer == null || string.IsNullOrWhiteSpace(pinName)) return;
            foreach (var entry in _pinsContainer.Children())
            {
                var nameField = entry.Q<TextField>("PinNameField");
                if (!string.Equals(nameField?.value?.Trim(), pinName, StringComparison.OrdinalIgnoreCase)) continue;
                if (_selectedPinEntry == entry) _selectedPinEntry = null;
                _pinsContainer.Remove(entry);
                RefreshAnchorGizmos();
                RefreshLayoutPreview();
                QueueValidation();
                RefreshCatalogAndHierarchy();
                return;
            }
        }

        private void AddLabelRow(LabelLayoutPayload payload)
        {
            if (_labelsContainer == null) return;
            var entry = new VisualElement();
            entry.AddToClassList("editor-entry");
            entry.RegisterCallback<PointerDownEvent>(_ => SelectLabelEntry(entry));

            var row = new VisualElement();
            row.AddToClassList("editor-row");
            var labelTextField = CreateField("LabelTextField", payload.text, "editor-field");
            var labelXField = CreateField("LabelXField", FormatFloat(payload.x), "editor-field-small");
            var labelYField = CreateField("LabelYField", FormatFloat(payload.y), "editor-field-small");
            var labelSizeField = CreateField("LabelSizeField", payload.size > 0 ? payload.size.ToString(CultureInfo.InvariantCulture) : string.Empty, "editor-field-small");
            var labelAlignField = new DropdownField { name = "LabelAlignField" };
            labelAlignField.AddToClassList("editor-field-small");
            labelAlignField.AddToClassList("input-dark");
            labelAlignField.choices = new List<string> { "left", "center", "right" };
            string alignValue = string.IsNullOrWhiteSpace(payload.align) ? "center" : payload.align;
            if (!labelAlignField.choices.Contains(alignValue, StringComparer.OrdinalIgnoreCase))
            {
                labelAlignField.choices.Add(alignValue);
            }
            labelAlignField.SetValueWithoutNotify(alignValue);
            row.Add(labelTextField);
            row.Add(labelXField);
            row.Add(labelYField);
            row.Add(labelSizeField);
            row.Add(labelAlignField);

            var removeBtn = new Button(() =>
            {
                RemoveLabelEntry(entry);
            });
            removeBtn.text = "X";
            removeBtn.AddToClassList("editor-remove-btn");
            row.Add(removeBtn);
            entry.Add(row);

            _labelsContainer.Add(entry);
            if (labelTextField != null) labelTextField.RegisterValueChangedCallback(_ => RefreshLayoutPreview());
            if (labelXField != null) labelXField.RegisterValueChangedCallback(_ => RefreshLayoutPreview());
            if (labelYField != null) labelYField.RegisterValueChangedCallback(_ => RefreshLayoutPreview());
            if (labelSizeField != null) labelSizeField.RegisterValueChangedCallback(_ => RefreshLayoutPreview());
            if (labelAlignField != null) labelAlignField.RegisterValueChangedCallback(_ => RefreshLayoutPreview());
            RegisterValidationField(labelTextField);
            RegisterValidationField(labelXField);
            RegisterValidationField(labelYField);
            RegisterValidationField(labelSizeField);
            RegisterValidationField(labelAlignField);
            RefreshLayoutPreview();
            QueueValidation();
            RefreshCatalogAndHierarchy();
        }

        private void AddShapeAt(float nx, float ny, LayoutTool tool)
        {
            string type = tool switch
            {
                LayoutTool.Box => "Box",
                LayoutTool.Triangle => "Triangle",
                LayoutTool.Line => "Line",
                LayoutTool.Text => "Text",
                LayoutTool.Circle => "Circle",
                _ => "Box"
            };
            var shape = new ShapeLayoutPayload
            {
                id = $"shape_{Guid.NewGuid():N}".Substring(0, 8),
                type = type,
                x = Mathf.Clamp01(nx),
                y = Mathf.Clamp01(ny),
                width = type == "Line" ? 0.35f : 0.22f,
                height = type == "Line" ? 0.04f : (type == "Text" ? 0.08f : 0.18f),
                text = type == "Text" ? "TEXT" : string.Empty,
                lineFlip = false,
                rotation = 0f,
                fontSize = type == "Text" ? LayoutTextDefaultFontSize : 0f
            };
            _shapeLayout.Add(shape);
            if (type == "Text")
            {
                _pendingShapeTextFocusId = shape.id;
            }
            SelectShape(shape.id);
        }

        private void BeginShapePlacement(float nx, float ny, LayoutTool tool, int pointerId)
        {
            if (_layoutPreviewActive == null) return;
            string type = tool switch
            {
                LayoutTool.Box => "Box",
                LayoutTool.Triangle => "Triangle",
                LayoutTool.Line => "Line",
                LayoutTool.Text => "Text",
                LayoutTool.Circle => "Circle",
                _ => "Box"
            };
            var shape = new ShapeLayoutPayload
            {
                id = $"shape_{Guid.NewGuid():N}".Substring(0, 8),
                type = type,
                x = Mathf.Clamp01(nx),
                y = Mathf.Clamp01(ny),
                width = 0.02f,
                height = 0.02f,
                text = type == "Text" ? "TEXT" : string.Empty,
                lineFlip = false,
                rotation = 0f,
                fontSize = type == "Text" ? LayoutTextDefaultFontSize : 0f
            };
            _shapeLayout.Add(shape);
            _placingShape = true;
            _placingShapeId = shape.id;
            _placingShapeStart = new Vector2(nx, ny);
            if (type == "Text")
            {
                _pendingShapeTextFocusId = shape.id;
            }
            _dragPointerId = pointerId;
            if (_layoutPreviewActive.HasPointerCapture(_dragPointerId) == false)
            {
                _layoutPreviewActive.CapturePointer(_dragPointerId);
            }
            SelectShape(shape.id);
        }

        private bool UpdatePlacingShape(Vector2 local)
        {
            if (!_placingShape || string.IsNullOrWhiteSpace(_placingShapeId)) return false;
            if (!TryGetLayoutNormalizedPosition(local, out var nx, out var ny)) return false;
            var shape = _shapeLayout.FirstOrDefault(s => s != null && s.id == _placingShapeId);
            if (shape == null) return false;
            float startX = Mathf.Clamp01(_placingShapeStart.x);
            float startY = Mathf.Clamp01(_placingShapeStart.y);
            float endX = Mathf.Clamp01(nx);
            float endY = Mathf.Clamp01(ny);
            float width = Mathf.Max(0.02f, Mathf.Abs(endX - startX));
            float height = Mathf.Max(0.02f, Mathf.Abs(endY - startY));
            shape.x = (startX + endX) * 0.5f;
            shape.y = (startY + endY) * 0.5f;
            shape.width = width;
            shape.height = height;
            if (string.Equals(shape.type, "Line", StringComparison.OrdinalIgnoreCase))
            {
                shape.height = Mathf.Max(0.02f, height);
                float dx = endX - startX;
                float dy = endY - startY;
                shape.lineFlip = dx * dy < 0f;
            }
            return true;
        }

        private void FinishPlacingShape()
        {
            if (!_placingShape) return;
            _placingShape = false;
            _placingShapeId = null;
        }

        private void CancelPlacingShape()
        {
            if (!_placingShape) return;
            if (!string.IsNullOrWhiteSpace(_placingShapeId))
            {
                RemoveShapeById(_placingShapeId);
            }
            _placingShape = false;
            _placingShapeId = null;
        }

        private void RemoveShapeById(string shapeId)
        {
            if (string.IsNullOrWhiteSpace(shapeId)) return;
            _shapeLayout.RemoveAll(shape => shape != null && shape.id == shapeId);
            if (_selectedShapeId == shapeId) _selectedShapeId = null;
            RefreshLayoutPreview();
            QueueValidation();
        }

        private void AddFxRow(FxPayload payload)
        {
            if (_fxContainer == null) return;
            if (payload == null) return;
            if (string.IsNullOrWhiteSpace(payload.id))
            {
                payload.id = $"fx_{Guid.NewGuid():N}".Substring(0, 9);
            }
            var entry = new VisualElement();
            entry.AddToClassList("editor-entry");
            entry.userData = payload.id ?? string.Empty;

            var row = new VisualElement();
            row.AddToClassList("editor-row");
            var idField = CreateField("FxIdField", payload.id, "editor-field-small");
            string normalizedType = NormalizeFxTypeValue(payload.type);
            var typeField = CreateDropdownField("FxTypeField", normalizedType, "editor-field", FxTypeChoices);
            var anchorField = CreateField("FxAnchorField", payload.anchorId, "editor-field-small");
            row.Add(idField);
            row.Add(typeField);
            row.Add(anchorField);

            var removeBtn = new Button(() =>
            {
                _fxContainer.Remove(entry);
                RefreshEffectGizmos();
                QueueValidation();
                RefreshCatalogAndHierarchy();
            });
            removeBtn.text = "X";
            removeBtn.AddToClassList("editor-remove-btn");
            row.Add(removeBtn);
            entry.Add(row);

            var row2 = new VisualElement();
            row2.AddToClassList("editor-row");
            var posXField = CreateField("FxPosXField", FormatFloat(payload.localPosition.x), "editor-field-small");
            var posYField = CreateField("FxPosYField", FormatFloat(payload.localPosition.y), "editor-field-small");
            var posZField = CreateField("FxPosZField", FormatFloat(payload.localPosition.z), "editor-field-small");
            var triggerField = CreateField("FxTriggerField", payload.trigger, "editor-field");
            row2.Add(posXField);
            row2.Add(posYField);
            row2.Add(posZField);
            row2.Add(triggerField);
            entry.Add(row2);

            var row3 = new VisualElement();
            row3.AddToClassList("editor-row");
            var exprField = CreateField("FxExpressionField", payload.expression, "editor-field");
            row3.Add(exprField);
            entry.Add(row3);

            _fxContainer.Add(entry);
            RegisterValidationField(idField);
            RegisterValidationField(typeField);
            RegisterValidationField(anchorField);
            RegisterValidationField(posXField);
            RegisterValidationField(posYField);
            RegisterValidationField(posZField);
            RegisterValidationField(triggerField);
            RegisterValidationField(exprField);
            RefreshEffectGizmos();
            QueueValidation();
            RefreshCatalogAndHierarchy();
        }

        private void RemoveLabelEntry(VisualElement entry)
        {
            if (_labelsContainer == null || entry == null) return;
            if (_selectedLabelEntry == entry) _selectedLabelEntry = null;
            _labelsContainer.Remove(entry);
            RefreshLayoutPreview();
            QueueValidation();
            RefreshCatalogAndHierarchy();
        }

        private void AddKeyValueRow(VisualElement container, string key, string value)
        {
            if (container == null) return;
            var row = new VisualElement();
            row.AddToClassList("editor-row");
            var keyField = CreateField("KeyField", key, "editor-field");
            var valueField = CreateField("ValueField", value, "editor-field");
            row.Add(keyField);
            row.Add(valueField);
            var removeBtn = new Button(() =>
            {
                container.Remove(row);
                QueueValidation();
            });
            removeBtn.text = "X";
            removeBtn.AddToClassList("editor-remove-btn");
            row.Add(removeBtn);
            container.Add(row);
            RegisterValidationField(keyField);
            RegisterValidationField(valueField);
            QueueValidation();
        }

        private void SelectPinEntry(VisualElement entry)
        {
            if (_selectedPinEntry == entry) return;
            if (_selectedPinEntry != null) _selectedPinEntry.RemoveFromClassList("selected");
            _selectedPinEntry = entry;
            if (_selectedPinEntry != null) _selectedPinEntry.AddToClassList("selected");
            _selectedShapeId = null;
            if (_is2dView && _selectedPinEntry != null)
            {
                FocusLayoutOnEntry(_selectedPinEntry, true);
            }
            RefreshLayoutPreview();
            RefreshTransformGizmo();
            UpdateInspectorForSelection("Pin", entry);
        }

        private void SelectLabelEntry(VisualElement entry)
        {
            if (_selectedLabelEntry == entry) return;
            if (_selectedLabelEntry != null) _selectedLabelEntry.RemoveFromClassList("selected");
            _selectedLabelEntry = entry;
            if (_selectedLabelEntry != null) _selectedLabelEntry.AddToClassList("selected");
            _selectedShapeId = null;
            if (_is2dView && _selectedLabelEntry != null)
            {
                FocusLayoutOnEntry(_selectedLabelEntry, false);
            }
            RefreshLayoutPreview();
            UpdateInspectorForSelection("Label", entry);
        }

        private void SelectShape(string shapeId)
        {
            if (string.IsNullOrWhiteSpace(shapeId)) return;
            _selectedShapeId = shapeId;
            if (_selectedPinEntry != null) _selectedPinEntry.RemoveFromClassList("selected");
            if (_selectedLabelEntry != null) _selectedLabelEntry.RemoveFromClassList("selected");
            _selectedPinEntry = null;
            _selectedLabelEntry = null;
            UpdateInspectorForShape(shapeId);
            RefreshLayoutPreview();
        }

        private void UpdateInspectorForShape(string shapeId)
        {
            var shape = _shapeLayout.FirstOrDefault(s => s != null && s.id == shapeId);
            if (_inspectorTargetLabel != null)
            {
                if (shape == null)
                {
                    _inspectorTargetLabel.text = "Shape";
                }
                else
                {
                    string label = string.IsNullOrWhiteSpace(shape.type) ? "Shape" : $"Shape: {shape.type}";
                    _inspectorTargetLabel.text = label;
                }
            }
            if (_inspectorDetailsContainer == null) return;
            _inspectorDetailsContainer.Clear();
            if (shape == null) return;
            var detail = new Label($"Type: {shape.type}");
            detail.AddToClassList("form-label");
            _inspectorDetailsContainer.Add(detail);
            bool isTextShape = IsTextShapeType(shape.type);

            var rotationRow = new VisualElement();
            rotationRow.AddToClassList("form-row");
            var rotationLabel = new Label("Rotation");
            rotationLabel.AddToClassList("form-label");
            var rotationField = new TextField { value = shape.rotation.ToString("0.#", CultureInfo.InvariantCulture) };
            rotationField.AddToClassList("form-field");
            rotationField.AddToClassList("input-dark");
            rotationRow.Add(rotationLabel);
            rotationRow.Add(rotationField);
            _inspectorDetailsContainer.Add(rotationRow);
            rotationField.RegisterValueChangedCallback(evt =>
            {
                if (float.TryParse(evt.newValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    shape.rotation = Mathf.Repeat(value, 360f);
                    RefreshLayoutPreview();
                    QueueValidation();
                }
            });

            var sizeRow = new VisualElement();
            sizeRow.AddToClassList("form-row-inline");
            var sizeLabel = new Label("Size");
            sizeLabel.AddToClassList("form-label");
            var widthField = new TextField { value = shape.width.ToString("0.###", CultureInfo.InvariantCulture) };
            widthField.AddToClassList("form-field");
            widthField.AddToClassList("editor-field-small");
            widthField.AddToClassList("input-dark");
            var heightField = new TextField { value = shape.height.ToString("0.###", CultureInfo.InvariantCulture) };
            heightField.AddToClassList("form-field");
            heightField.AddToClassList("editor-field-small");
            heightField.AddToClassList("input-dark");
            sizeRow.Add(sizeLabel);
            sizeRow.Add(widthField);
            sizeRow.Add(heightField);
            _inspectorDetailsContainer.Add(sizeRow);
            widthField.RegisterValueChangedCallback(evt =>
            {
                if (float.TryParse(evt.newValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    shape.width = Mathf.Max(0.02f, value);
                    RefreshLayoutPreview();
                    QueueValidation();
                }
            });
            heightField.RegisterValueChangedCallback(evt =>
            {
                if (float.TryParse(evt.newValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    shape.height = Mathf.Max(0.02f, value);
                    RefreshLayoutPreview();
                    QueueValidation();
                }
            });

            if (isTextShape)
            {
                widthField.SetEnabled(false);
                heightField.SetEnabled(false);

                var fontRow = new VisualElement();
                fontRow.AddToClassList("form-row");
                var fontLabel = new Label("Font");
                fontLabel.AddToClassList("form-label");
                float fontValue = shape.fontSize > 0f ? shape.fontSize : LayoutTextDefaultFontSize;
                var fontField = new TextField { value = fontValue.ToString("0.#", CultureInfo.InvariantCulture) };
                fontField.AddToClassList("form-field");
                fontField.AddToClassList("input-dark");
                fontRow.Add(fontLabel);
                fontRow.Add(fontField);
                _inspectorDetailsContainer.Add(fontRow);
                fontField.RegisterValueChangedCallback(evt =>
                {
                    if (float.TryParse(evt.newValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        shape.fontSize = Mathf.Max(LayoutTextMinFontSize, value);
                        RefreshLayoutPreview();
                        QueueValidation();
                    }
                });

                var textRow = new VisualElement();
                textRow.AddToClassList("form-row");
                var textLabel = new Label("Text");
                textLabel.AddToClassList("form-label");
                var textField = new TextField { value = shape.text ?? string.Empty };
                textField.AddToClassList("form-field");
                textField.AddToClassList("input-dark");
                textRow.Add(textLabel);
                textRow.Add(textField);
                _inspectorDetailsContainer.Add(textRow);
                textField.RegisterValueChangedCallback(evt =>
                {
                    shape.text = evt.newValue ?? string.Empty;
                    RefreshLayoutPreview();
                    QueueValidation();
                });
                if (!string.IsNullOrWhiteSpace(_pendingShapeTextFocusId) && _pendingShapeTextFocusId == shape.id)
                {
                    _pendingShapeTextFocusId = null;
                    textField.schedule.Execute(() =>
                    {
                        textField.Focus();
                        textField.SelectAll();
                    }).ExecuteLater(1);
                }
            }
        }

        private void UpdateInspectorForSelection(string kind, VisualElement entry)
        {
            if (_inspectorTargetLabel != null)
            {
                string label = "No selection";
                if (!string.IsNullOrWhiteSpace(kind))
                {
                    string name = entry?.userData as string;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        var nameField = entry?.Q<TextField>("PinNameField") ?? entry?.Q<TextField>("LabelTextField");
                        name = nameField?.value ?? string.Empty;
                    }
                    label = string.IsNullOrWhiteSpace(name) ? kind : $"{kind}: {name}";
                }
                _inspectorTargetLabel.text = label;
            }

            if (_inspectorDetailsContainer == null) return;
            _inspectorDetailsContainer.Clear();
            if (entry == null) return;
            var detail = new Label($"Type: {kind}");
            detail.AddToClassList("form-label");
            _inspectorDetailsContainer.Add(detail);
        }

        private void RefreshCatalogAndHierarchy()
        {
            RefreshCatalog();
            RefreshHierarchy();
            RefreshAnchorsList();
        }

        private void RefreshAnchorsList()
        {
            if (_anchorsContainer == null) return;
            _anchorsContainer.Clear();
            if (_pinsContainer == null) return;
            foreach (var entry in _pinsContainer.Children())
            {
                var nameField = entry.Q<TextField>("PinNameField");
                string name = nameField?.value?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;
                float radius = ReadFloat(entry.Q<TextField>("PinAnchorRadiusField"));
                var row = new VisualElement();
                row.AddToClassList("hierarchy-item");
                var label = new Label(name);
                label.AddToClassList("hierarchy-item-label");
                var kindLabel = new Label(radius > 0f ? $"r={FormatFloat(radius)}" : "anchor");
                kindLabel.AddToClassList("hierarchy-item-kind");
                row.Add(label);
                row.Add(kindLabel);
                row.RegisterCallback<PointerDownEvent>(_ => SelectPinEntry(entry));
                _anchorsContainer.Add(row);
            }
        }

        private string GetModelDisplayName()
        {
            string modelPath = _modelSourcePath;
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                modelPath = GetField("ComponentModelField")?.value;
            }
            if (string.IsNullOrWhiteSpace(modelPath) && _payload != null)
            {
                modelPath = _payload.modelFile;
            }
            if (string.IsNullOrWhiteSpace(modelPath)) return "Model";
            return Path.GetFileNameWithoutExtension(modelPath);
        }

        private void RefreshCatalog()
        {
            if (_catalogList == null) return;
            _catalogList.Clear();

            string modelName = GetModelDisplayName();
            AddCatalogHeader(modelName, 0);
            int indent = 1;

            AddCatalogHeader("Pins", indent);
            if (_pinsContainer != null)
            {
                foreach (var entry in _pinsContainer.Children())
                {
                    var nameField = entry.Q<TextField>("PinNameField");
                    string name = nameField?.value?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    AddCatalogItem(name, "Pin", () => SelectPinEntry(entry), true, indent);
                }
            }

            AddCatalogHeader("Labels", indent);
            if (_labelsContainer != null)
            {
                foreach (var entry in _labelsContainer.Children())
                {
                    var textField = entry.Q<TextField>("LabelTextField");
                    string text = textField?.value?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    AddCatalogItem(text, "Label", () => SelectLabelEntry(entry), true, indent);
                }
            }

            AddCatalogHeader("States", indent);
            foreach (var state in _stateOverrides.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                string stateId = state;
                AddCatalogItem(stateId, "State", () =>
                {
                    var entry = FindStateEntryById(stateId);
                    if (entry != null) SelectStateEntry(entry);
                }, false, indent);
            }

            AddCatalogHeader("Parts", indent);
            if (_partsContainer != null)
            {
                foreach (var entry in _partsContainer.Children())
                {
                    string name = entry.userData as string ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    AddCatalogItem(name, "Part", () => SelectPartEntry(entry), false, indent);
                }
            }

            AddCatalogHeader("Effects", indent);
            var effectNames = new[]
            {
                "LabelOffset", "LedGlowOffset", "HeatFxOffset", "SparkFxOffset", "ErrorFxOffset", "SmokeOffset", "UsbOffset"
            };
            foreach (var effect in effectNames)
            {
                AddCatalogItem(effect, "FX", null, true, indent);
            }
            if (_fxContainer != null)
            {
                foreach (var entry in _fxContainer.Children())
                {
                    var idField = entry.Q<TextField>("FxIdField");
                    string id = idField?.value?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    AddCatalogItem(id, "FX", null, true, indent);
                }
            }

            AddCatalogHeader("Scripts", indent);
            var physicsField = GetField("ComponentPhysicsScriptField");
            string scriptPath = physicsField?.value?.Trim() ?? string.Empty;
            AddCatalogItem(string.IsNullOrWhiteSpace(scriptPath) ? "Physics Script (none)" : scriptPath, "Script", null, true, indent);
        }

        private void RefreshHierarchy()
        {
            if (_hierarchyTree == null) return;
            _hierarchyTree.Clear();

            string modelName = GetModelDisplayName();
            AddHierarchyItem(modelName, "Model", 0, () =>
            {
                SetCenterView(true);
                SelectModelRoot();
            }, () =>
            {
                SetCenterView(true);
                SelectModelRoot();
                _studioView?.FrameModel();
            });

            AddHierarchyItem("2D View", "Root", 1, () => SetCenterView(false), () => SetCenterView(false));
            if (_pinsContainer != null)
            {
                foreach (var entry in _pinsContainer.Children())
                {
                    var nameField = entry.Q<TextField>("PinNameField");
                    string name = nameField?.value?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    AddHierarchyItem(name, "Pin", 2, () => SelectPinEntry(entry), () =>
                    {
                        SetCenterView(false);
                        FocusLayoutOnEntry(entry, true);
                    });
                }
            }
            if (_labelsContainer != null)
            {
                foreach (var entry in _labelsContainer.Children())
                {
                    var textField = entry.Q<TextField>("LabelTextField");
                    string text = textField?.value?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    AddHierarchyItem(text, "Label", 2, () => SelectLabelEntry(entry), () =>
                    {
                        SetCenterView(false);
                        FocusLayoutOnEntry(entry, false);
                    });
                }
            }
            if (_shapeLayout != null && _shapeLayout.Count > 0)
            {
                foreach (var shape in _shapeLayout)
                {
                    if (shape == null || string.IsNullOrWhiteSpace(shape.id)) continue;
                    string kind = string.IsNullOrWhiteSpace(shape.type) ? "Shape" : shape.type;
                    string label = kind;
                    if (IsTextShapeType(shape.type) && !string.IsNullOrWhiteSpace(shape.text))
                    {
                        label = shape.text.Trim();
                    }
                    else
                    {
                        label = string.IsNullOrWhiteSpace(shape.id) ? kind : $"{kind} {shape.id}";
                    }
                    AddHierarchyItem(label, kind, 2, () => SelectShape(shape.id), () =>
                    {
                        SetCenterView(false);
                        FocusLayoutOnNormalized(shape.x, shape.y);
                    });
                }
            }

            AddHierarchyItem("3D View", "Root", 1, () => SetCenterView(true), () => SetCenterView(true));
            if (_partsContainer != null)
            {
                foreach (var entry in _partsContainer.Children())
                {
                    string name = entry.userData as string ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    AddHierarchyItem(name, "Part", 2, () => SelectPartEntry(entry), () =>
                    {
                        SetCenterView(true);
                        FrameSelectedPart();
                    });
                }
            }
            if (_fxContainer != null)
            {
                foreach (var entry in _fxContainer.Children())
                {
                    var idField = entry.Q<TextField>("FxIdField");
                    string id = idField?.value?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    AddHierarchyItem(id, "FX", 2, null, () => SetCenterView(true));
                }
            }
        }

        private void AddCatalogHeader(string title, int indent = 0)
        {
            var label = new Label(title);
            label.AddToClassList("panel-section-title");
            if (indent > 0)
            {
                label.style.marginLeft = indent * 12;
            }
            _catalogList.Add(label);
        }

        private void AddCatalogItem(string name, string kind, Action onClick, bool allowDrag = false, int indent = 0)
        {
            var row = new VisualElement();
            row.AddToClassList("catalog-item");
            if (indent > 0)
            {
                row.style.marginLeft = indent * 12;
            }
            var label = new Label(name);
            label.AddToClassList("catalog-item-label");
            var kindLabel = new Label(kind);
            kindLabel.AddToClassList("hierarchy-item-kind");
            row.Add(label);
            row.Add(kindLabel);
            if (allowDrag)
            {
                row.RegisterCallback<PointerDownEvent>(evt => StartCatalogDrag(evt, name, kind));
            }
            if (onClick != null)
            {
                row.RegisterCallback<PointerDownEvent>(_ => onClick());
            }
            _catalogList.Add(row);
        }

        private void AddHierarchyItem(string name, string kind, int indent, Action onClick, Action onDoubleClick)
        {
            if (_hierarchyTree == null) return;
            var row = new VisualElement();
            row.AddToClassList("hierarchy-item");
            row.style.marginLeft = indent * 12;
            var label = new Label(name);
            label.AddToClassList("hierarchy-item-label");
            var kindLabel = new Label(kind);
            kindLabel.AddToClassList("hierarchy-item-kind");
            row.Add(label);
            row.Add(kindLabel);
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount > 1)
                {
                    onDoubleClick?.Invoke();
                }
                else
                {
                    onClick?.Invoke();
                }
            });
            _hierarchyTree.Add(row);
        }

        private VisualElement FindStateEntryById(string id)
        {
            if (_statesContainer == null || string.IsNullOrWhiteSpace(id)) return null;
            foreach (var entry in _statesContainer.Children())
            {
                if (entry.userData is string stateId && string.Equals(stateId, id, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
            return null;
        }

        private void DeleteContextPin()
        {
            if (string.IsNullOrWhiteSpace(_layoutContextPinName)) return;
            RemovePinEntryByName(_layoutContextPinName);
            HideLayoutContextMenu();
        }

        private void DeleteContextLabel()
        {
            if (_layoutContextLabelEntry == null) return;
            RemoveLabelEntry(_layoutContextLabelEntry);
            HideLayoutContextMenu();
        }

        private void DeleteContextShape()
        {
            if (string.IsNullOrWhiteSpace(_layoutContextShapeId)) return;
            RemoveShapeById(_layoutContextShapeId);
            HideLayoutContextMenu();
        }

        private void ShowLayoutContextMenu(Vector2 panelPosition)
        {
            if (_layoutContextMenu == null || _root == null) return;
            var parentRect = _component2DPanel != null ? _component2DPanel.worldBound : _root.worldBound;
            float left = panelPosition.x - parentRect.xMin;
            float top = panelPosition.y - parentRect.yMin;
            _layoutContextMenu.style.left = left;
            _layoutContextMenu.style.top = top;
            _layoutContextMenu.style.display = DisplayStyle.Flex;
        }

        private void StartCatalogDrag(PointerDownEvent evt, string name, string kind)
        {
            if (evt.button != 0) return;
            if (_root == null) return;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(kind)) return;
            _catalogDragging = true;
            _catalogDragName = name;
            _catalogDragKind = kind;
            _catalogDragPointerId = evt.pointerId;

            _catalogDragGhost = new VisualElement();
            _catalogDragGhost.pickingMode = PickingMode.Ignore;
            _catalogDragGhost.style.position = Position.Absolute;
            _catalogDragGhost.style.left = evt.position.x;
            _catalogDragGhost.style.top = evt.position.y;
            _catalogDragGhost.style.width = 24;
            _catalogDragGhost.style.height = 24;
            _catalogDragGhost.style.backgroundColor = new Color(0.2f, 0.8f, 0.9f, 0.45f);
            _catalogDragGhost.style.borderTopLeftRadius = 4;
            _catalogDragGhost.style.borderTopRightRadius = 4;
            _catalogDragGhost.style.borderBottomLeftRadius = 4;
            _catalogDragGhost.style.borderBottomRightRadius = 4;
            _root.Add(_catalogDragGhost);

            _root.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnCatalogDragMove(PointerMoveEvent evt)
        {
            if (!_catalogDragging || _catalogDragGhost == null || evt.pointerId != _catalogDragPointerId) return;
            _catalogDragGhost.style.left = evt.position.x;
            _catalogDragGhost.style.top = evt.position.y;
        }

        private void OnCatalogDragEnd(PointerUpEvent evt)
        {
            if (!_catalogDragging || evt.pointerId != _catalogDragPointerId) return;
            if (_root != null && _root.HasPointerCapture(evt.pointerId))
            {
                _root.ReleasePointer(evt.pointerId);
            }
            if (_catalogDragGhost != null)
            {
                _catalogDragGhost.RemoveFromHierarchy();
                _catalogDragGhost = null;
            }

            bool droppedOnViewport = _viewport != null && _viewport.worldBound.Contains(evt.position);
            bool droppedOnLayout = _layoutPreviewLarge != null && _layoutPreviewLarge.worldBound.Contains(evt.position);
            if (droppedOnViewport || droppedOnLayout)
            {
                TryApplyCatalogDrop(evt.position, _catalogDragName, _catalogDragKind, droppedOnViewport);
            }
            _catalogDragging = false;
            _catalogDragName = string.Empty;
            _catalogDragKind = string.Empty;
            _catalogDragPointerId = -1;
        }

        private void OnCatalogDragCancel(PointerCancelEvent evt)
        {
            if (!_catalogDragging || evt.pointerId != _catalogDragPointerId) return;
            CancelCatalogDrag();
        }

        private void CancelCatalogDrag()
        {
            if (_root != null && _catalogDragPointerId != -1 && _root.HasPointerCapture(_catalogDragPointerId))
            {
                _root.ReleasePointer(_catalogDragPointerId);
            }
            if (_catalogDragGhost != null)
            {
                _catalogDragGhost.RemoveFromHierarchy();
                _catalogDragGhost = null;
            }
            _catalogDragging = false;
            _catalogDragName = string.Empty;
            _catalogDragKind = string.Empty;
            _catalogDragPointerId = -1;
        }

        private void TryApplyCatalogDrop(Vector2 worldPosition, string name, string kind, bool droppedOnViewport)
        {
            if (string.IsNullOrWhiteSpace(kind)) return;
            if (kind.Equals("FX", StringComparison.OrdinalIgnoreCase))
            {
                if (!droppedOnViewport) return;
                Vector3 localPos = Vector3.zero;
                if (_studioView != null && _viewport != null && droppedOnViewport)
                {
                    var rect = _viewport.worldBound;
                    if (rect.width > 0f && rect.height > 0f)
                    {
                        var local = new Vector2(worldPosition.x - rect.xMin, worldPosition.y - rect.yMin);
                        var viewportPoint = new Vector2(local.x / rect.width, 1f - (local.y / rect.height));
                        _studioView.TryPickLocal(viewportPoint, out localPos);
                    }
                }

                var payload = new FxPayload
                {
                    id = $"fx_{Guid.NewGuid():N}".Substring(0, 6),
                    type = ResolveFxTypeFromCatalogName(name),
                    localPosition = localPos,
                    trigger = string.Empty,
                    expression = string.Empty
                };
                AddFxRow(payload);
                return;
            }
            if (kind.Equals("Script", StringComparison.OrdinalIgnoreCase))
            {
                var field = GetField("ComponentPhysicsScriptField");
                if (field == null) return;
                string value = string.Equals(name, "Physics Script (none)", StringComparison.OrdinalIgnoreCase) ? string.Empty : name;
                field.SetValueWithoutNotify(value);
                QueueValidation();
                return;
            }
            if (kind.Equals("Pin", StringComparison.OrdinalIgnoreCase) && !droppedOnViewport)
            {
                if (!TryGetLayoutNormalizedFromWorld(worldPosition, out var nx, out var ny)) return;
                var entry = FindPinEntry(name);
                if (entry == null)
                {
                    entry = AddPinRow(new PinLayoutPayload { name = string.IsNullOrWhiteSpace(name) ? GetNextPinName() : name, x = nx, y = ny });
                    if (entry != null) SelectPinEntry(entry);
                }
                else
                {
                    SetEntryValue(entry, "PinXField", nx.ToString("0.###", CultureInfo.InvariantCulture));
                    SetEntryValue(entry, "PinYField", ny.ToString("0.###", CultureInfo.InvariantCulture));
                }
                RefreshLayoutPreview();
                QueueValidation();
                return;
            }
            if (kind.Equals("Label", StringComparison.OrdinalIgnoreCase) && !droppedOnViewport)
            {
                if (!TryGetLayoutNormalizedFromWorld(worldPosition, out var nx, out var ny)) return;
                var entry = FindLabelEntry(name);
                if (entry == null)
                {
                    AddLabelRow(new LabelLayoutPayload { text = string.IsNullOrWhiteSpace(name) ? "{id}" : name, x = nx, y = ny, size = 10, align = "center" });
                }
                else
                {
                    SetEntryValue(entry, "LabelXField", nx.ToString("0.###", CultureInfo.InvariantCulture));
                    SetEntryValue(entry, "LabelYField", ny.ToString("0.###", CultureInfo.InvariantCulture));
                }
                RefreshLayoutPreview();
                QueueValidation();
            }
        }

        private bool TryGetLayoutNormalizedFromWorld(Vector2 worldPosition, out float nx, out float ny)
        {
            nx = 0f;
            ny = 0f;
            if (_layoutPreviewActive == null) return false;
            var local = _layoutPreviewActive.WorldToLocal(worldPosition);
            return TryGetLayoutNormalizedPosition(new Vector2(local.x, local.y), out nx, out ny);
        }

        private static string ResolveFxTypeFromCatalogName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Glow";
            string key = name.Trim().ToLowerInvariant();
            if (key.Contains("heat")) return "Heat";
            if (key.Contains("spark") || key.Contains("arc")) return "Spark";
            if (key.Contains("error") || key.Contains("alarm")) return "Error";
            if (key.Contains("smoke") || key.Contains("steam")) return "Smoke";
            if (key.Contains("usb")) return "Pulse";
            if (key.Contains("label")) return "Glow";
            if (key.Contains("led") || key.Contains("glow")) return "Glow";
            if (key.Contains("vibration")) return "Vibration";
            return "Glow";
        }

        private void PrepareLayoutContextMenu(bool showPinDelete, bool showLabelDelete, bool showShapeDelete)
        {
            if (_layoutContextDeletePinBtn != null)
            {
                _layoutContextDeletePinBtn.style.display = showPinDelete ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_layoutContextDeleteLabelBtn != null)
            {
                _layoutContextDeleteLabelBtn.style.display = showLabelDelete ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_layoutContextDeleteShapeBtn != null)
            {
                _layoutContextDeleteShapeBtn.style.display = showShapeDelete ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void HideLayoutContextMenu()
        {
            if (_layoutContextMenu == null) return;
            _layoutContextMenu.style.display = DisplayStyle.None;
            _layoutContextPinName = null;
            _layoutContextLabelEntry = null;
            _layoutContextShapeId = null;
        }

        private bool IsLayoutContextTarget(VisualElement target)
        {
            if (_layoutContextMenu == null || target == null) return false;
            return _layoutContextMenu == target || _layoutContextMenu.Contains(target);
        }

        private void FocusLayoutOnEntry(VisualElement entry, bool isPin)
        {
            if (entry == null) return;
            float nx;
            float ny;
            if (isPin)
            {
                nx = ReadFloat(entry.Q<TextField>("PinXField"));
                ny = ReadFloat(entry.Q<TextField>("PinYField"));
            }
            else
            {
                nx = ReadFloat(entry.Q<TextField>("LabelXField"));
                ny = ReadFloat(entry.Q<TextField>("LabelYField"));
            }
            FocusLayoutOnNormalized(nx, ny);
        }

        private void FocusLayoutOnNormalized(float nx, float ny)
        {
            if (_layoutPreviewLarge == null) return;
            float previewWidth = _layoutPreviewLarge.resolvedStyle.width;
            float previewHeight = _layoutPreviewLarge.resolvedStyle.height;
            if (previewWidth <= 1f || previewHeight <= 1f) return;

            if (_layoutZoom < 1.1f)
            {
                _layoutZoom = 1.1f;
            }

            float sizeX = ReadFloat(GetField("ComponentSizeXField"), CircuitLayoutSizing.DefaultComponentWidth);
            float sizeY = ReadFloat(GetField("ComponentSizeYField"), CircuitLayoutSizing.DefaultComponentHeight);
            if (sizeX <= 0f) sizeX = CircuitLayoutSizing.DefaultComponentWidth;
            if (sizeY <= 0f) sizeY = CircuitLayoutSizing.DefaultComponentHeight;

            float padding = 12f;
            Vector2 averageSize = GetAverageComponentSize();
            float maxX = Mathf.Max(sizeX, averageSize.x);
            float maxY = Mathf.Max(sizeY, averageSize.y);
            float scale = Mathf.Min((previewWidth - padding * 2f) / maxX, (previewHeight - padding * 2f) / maxY);
            scale = Mathf.Clamp(scale, 0.1f, 100f);

            float bodyWidth = sizeX * scale * _layoutZoom;
            float bodyHeight = sizeY * scale * _layoutZoom;
            _layoutPanOffset = new Vector2(
                (previewWidth * 0.5f) - (bodyWidth * 0.5f) - (nx * bodyWidth),
                (previewHeight * 0.5f) - (bodyHeight * 0.5f) - (ny * bodyHeight));
            ClampLayoutPanOffset();
            RefreshLayoutPreview();
        }

        private void FrameSelectedPart()
        {
            if (string.IsNullOrWhiteSpace(_selectedPartName)) return;
            if (_studioView == null) return;
            _studioView.FrameModel();
        }

        private void RefreshAnchorGizmos()
        {
            if (_studioView == null) return;
            if (!IsAnchorVisible())
            {
                _studioView.ClearAnchors();
                return;
            }

            var anchors = new List<ComponentStudioView.AnchorGizmo>();
            if (_pinsContainer != null)
            {
                foreach (var entry in _pinsContainer.Children())
                {
                    var nameField = entry.Q<TextField>("PinNameField");
                    string name = nameField?.value?.Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    float x = ReadFloat(entry.Q<TextField>("PinAnchorXField"));
                    float y = ReadFloat(entry.Q<TextField>("PinAnchorYField"));
                    float z = ReadFloat(entry.Q<TextField>("PinAnchorZField"));
                    float r = ReadFloat(entry.Q<TextField>("PinAnchorRadiusField"));
                    string pinType = NormalizePinType(GetEntryValue(entry, "PinTypeField"));
                    Color color = string.Equals(pinType, PinTypePin, StringComparison.OrdinalIgnoreCase)
                        ? new Color(0.85f, 0.68f, 0.4f)
                        : new Color(0.2f, 0.7f, 1f);
                    anchors.Add(new ComponentStudioView.AnchorGizmo
                    {
                        Name = name,
                        LocalPosition = new Vector3(x, y, z),
                        Radius = r <= 0f ? 0.006f : r,
                        Color = color
                    });
                }
            }
            _studioView.SetAnchorGizmos(anchors);
            RefreshTransformGizmo();
        }

        private void RefreshEffectGizmos()
        {
            if (_studioView == null) return;
            if (_is2dView)
            {
                _studioView.ClearEffects();
                return;
            }
            if (_viewportEditEffectsToggle != null && !_viewportEditEffectsToggle.value)
            {
                _studioView.ClearEffects();
                return;
            }

            var gizmos = new List<ComponentStudioView.EffectGizmo>
            {
                BuildEffectGizmo("Component", Vector3.zero, new Color(0.9f, 0.9f, 0.9f)),
                BuildEffectGizmo("LabelOffset", ReadVector3Field("ComponentLabelOffsetXField", "ComponentLabelOffsetYField", "ComponentLabelOffsetZField"), new Color(0.55f, 0.9f, 0.95f)),
                BuildEffectGizmo("LedGlowOffset", ReadVector3Field("ComponentLedGlowOffsetXField", "ComponentLedGlowOffsetYField", "ComponentLedGlowOffsetZField"), new Color(0.95f, 0.85f, 0.25f)),
                BuildEffectGizmo("HeatFxOffset", ReadVector3Field("ComponentHeatFxOffsetXField", "ComponentHeatFxOffsetYField", "ComponentHeatFxOffsetZField"), new Color(0.95f, 0.45f, 0.25f)),
                BuildEffectGizmo("SparkFxOffset", ReadVector3Field("ComponentSparkFxOffsetXField", "ComponentSparkFxOffsetYField", "ComponentSparkFxOffsetZField"), new Color(0.95f, 0.55f, 0.85f)),
                BuildEffectGizmo("ErrorFxOffset", ReadVector3Field("ComponentErrorFxOffsetXField", "ComponentErrorFxOffsetYField", "ComponentErrorFxOffsetZField"), new Color(0.95f, 0.35f, 0.35f)),
                BuildEffectGizmo("SmokeOffset", ReadVector3Field("ComponentSmokeOffsetXField", "ComponentSmokeOffsetYField", "ComponentSmokeOffsetZField"), new Color(0.7f, 0.75f, 0.8f)),
                BuildEffectGizmo("UsbOffset", ReadVector3Field("ComponentUsbOffsetXField", "ComponentUsbOffsetYField", "ComponentUsbOffsetZField"), new Color(0.35f, 0.85f, 0.45f))
            };

            if (_fxContainer != null)
            {
                foreach (var entry in _fxContainer.Children())
                {
                    var idField = entry.Q<TextField>("FxIdField");
                    string id = idField?.value?.Trim();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    string type = DenormalizeFxTypeValue(GetEntryValue(entry, "FxTypeField"));
                    string anchorId = entry.Q<TextField>("FxAnchorField")?.value?.Trim();
                    Vector3 localPos = new Vector3(
                        ReadFloat(entry.Q<TextField>("FxPosXField")),
                        ReadFloat(entry.Q<TextField>("FxPosYField")),
                        ReadFloat(entry.Q<TextField>("FxPosZField")));
                    if (!string.IsNullOrWhiteSpace(anchorId) && TryGetAnchorLocalPosition(anchorId, out var anchorPos))
                    {
                        localPos = anchorPos;
                    }
                    gizmos.Add(BuildEffectGizmo(id, localPos, GetFxColor(type)));
                }
            }

            _studioView.SetEffectGizmos(gizmos);
            RefreshTransformGizmo();
        }

        private bool TryGetAnchorLocalPosition(string id, out Vector3 local)
        {
            local = Vector3.zero;
            var entry = FindPinEntryByName(id);
            if (entry == null) return false;
            local = new Vector3(
                ReadFloat(entry.Q<TextField>("PinAnchorXField")),
                ReadFloat(entry.Q<TextField>("PinAnchorYField")),
                ReadFloat(entry.Q<TextField>("PinAnchorZField")));
            return true;
        }

        private static Color GetFxColor(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return new Color(0.8f, 0.8f, 0.8f);
            string key = type.Trim().ToLowerInvariant();
            if (key.Contains("glow") || key.Contains("led")) return new Color(0.95f, 0.85f, 0.25f);
            if (key.Contains("heat")) return new Color(0.95f, 0.45f, 0.25f);
            if (key.Contains("spark") || key.Contains("arc")) return new Color(0.95f, 0.55f, 0.85f);
            if (key.Contains("smoke") || key.Contains("steam")) return new Color(0.7f, 0.75f, 0.8f);
            if (key.Contains("error") || key.Contains("alarm")) return new Color(0.95f, 0.35f, 0.35f);
            if (key.Contains("pulse")) return new Color(0.45f, 0.85f, 0.95f);
            if (key.Contains("vibration")) return new Color(0.75f, 0.85f, 0.45f);
            return new Color(0.7f, 0.85f, 0.6f);
        }

        private ComponentStudioView.EffectGizmo BuildEffectGizmo(string id, Vector3 local, Color color)
        {
            return new ComponentStudioView.EffectGizmo
            {
                Id = id,
                LocalPosition = local,
                Radius = 0.012f,
                Color = color
            };
        }

        private Vector3 ReadVector3Field(string xField, string yField, string zField)
        {
            float x = ReadFloat(GetField(xField), 0f);
            float y = ReadFloat(GetField(yField), 0f);
            float z = ReadFloat(GetField(zField), 0f);
            return new Vector3(x, y, z);
        }

        private Vector3 ReadScaleVector3Field(string xField, string yField, string zField)
        {
            float x = ReadFloat(GetField(xField), 1f);
            float y = ReadFloat(GetField(yField), 1f);
            float z = ReadFloat(GetField(zField), 1f);
            return new Vector3(x, y, z);
        }

        private void RefreshLayoutPreview()
        {
            if (_layoutPreview == null && _layoutPreviewLarge == null) return;

            _layoutPins.Clear();
            _layoutBodyActive = null;

            var primaryPreview = _is2dView ? (_layoutPreviewLarge ?? _layoutPreview) : (_layoutPreview ?? _layoutPreviewLarge);
            if (primaryPreview == null) return;
            _layoutPreviewActive = primaryPreview;
            ClampLayoutPanOffset();

            RenderLayoutPreview(primaryPreview, true);

            var secondaryPreview = primaryPreview == _layoutPreview ? _layoutPreviewLarge : _layoutPreview;
            if (secondaryPreview != null)
            {
                RenderLayoutPreview(secondaryPreview, false);
            }
        }

        private void ClampLayoutPanOffset()
        {
            if (_layoutPreviewLarge == null) return;
            float previewWidth = _layoutPreviewLarge.resolvedStyle.width;
            float previewHeight = _layoutPreviewLarge.resolvedStyle.height;
            if (previewWidth <= 1f || previewHeight <= 1f) return;

            float sizeX = ReadFloat(GetField("ComponentSizeXField"), CircuitLayoutSizing.DefaultComponentWidth);
            float sizeY = ReadFloat(GetField("ComponentSizeYField"), CircuitLayoutSizing.DefaultComponentHeight);
            if (sizeX <= 0f) sizeX = CircuitLayoutSizing.DefaultComponentWidth;
            if (sizeY <= 0f) sizeY = CircuitLayoutSizing.DefaultComponentHeight;

            float padding = 12f;
            Vector2 averageSize = GetAverageComponentSize();
            float maxX = Mathf.Max(sizeX, averageSize.x);
            float maxY = Mathf.Max(sizeY, averageSize.y);
            float scale = Mathf.Min((previewWidth - padding * 2f) / maxX, (previewHeight - padding * 2f) / maxY);
            scale = Mathf.Clamp(scale, 0.1f, 100f);

            float bodyWidth = sizeX * scale * _layoutZoom;
            float bodyHeight = sizeY * scale * _layoutZoom;
            float baseLeft = (previewWidth - bodyWidth) * 0.5f;
            float baseTop = (previewHeight - bodyHeight) * 0.5f;

            float margin = 8f;
            float extra = Mathf.Min(previewWidth, previewHeight) * 0.2f;
            float left = baseLeft + _layoutPanOffset.x;
            float top = baseTop + _layoutPanOffset.y;

            float minLeft = margin - extra;
            float maxLeft = previewWidth - bodyWidth - margin + extra;
            if (maxLeft >= minLeft)
            {
                left = Mathf.Clamp(left, minLeft, maxLeft);
            }
            else
            {
                float overpan = (bodyWidth - previewWidth) * 0.5f + margin + extra;
                left = Mathf.Clamp(left, baseLeft - overpan, baseLeft + overpan);
            }

            float minTop = margin - extra;
            float maxTop = previewHeight - bodyHeight - margin + extra;
            if (maxTop >= minTop)
            {
                top = Mathf.Clamp(top, minTop, maxTop);
            }
            else
            {
                float overpan = (bodyHeight - previewHeight) * 0.5f + margin + extra;
                top = Mathf.Clamp(top, baseTop - overpan, baseTop + overpan);
            }

            _layoutPanOffset = new Vector2(left - baseLeft, top - baseTop);
        }

        private void RenderLayoutPreview(VisualElement target, bool interactive)
        {
            if (target == null) return;
            target.Clear();

            float previewWidth = target.resolvedStyle.width;
            float previewHeight = target.resolvedStyle.height;
            if (previewWidth <= 1f || previewHeight <= 1f) return;

            float sizeX = ReadFloat(GetField("ComponentSizeXField"), CircuitLayoutSizing.DefaultComponentWidth);
            float sizeY = ReadFloat(GetField("ComponentSizeYField"), CircuitLayoutSizing.DefaultComponentHeight);
            if (sizeX <= 0f) sizeX = CircuitLayoutSizing.DefaultComponentWidth;
            if (sizeY <= 0f) sizeY = CircuitLayoutSizing.DefaultComponentHeight;

            float padding = 12f;
            Vector2 averageSize = GetAverageComponentSize();
            float maxX = Mathf.Max(sizeX, averageSize.x);
            float maxY = Mathf.Max(sizeY, averageSize.y);
            float scale = Mathf.Min((previewWidth - padding * 2f) / maxX, (previewHeight - padding * 2f) / maxY);
            scale = Mathf.Clamp(scale, 0.1f, 100f);
            float zoom = interactive && target == _layoutPreviewLarge ? _layoutZoom : 1f;
            Vector2 pan = interactive && target == _layoutPreviewLarge ? _layoutPanOffset : Vector2.zero;
            float bodyWidth = sizeX * scale * zoom;
            float bodyHeight = sizeY * scale * zoom;
            float bodyLeft = (previewWidth - bodyWidth) * 0.5f + pan.x;
            float bodyTop = (previewHeight - bodyHeight) * 0.5f + pan.y;

            if (averageSize.x > 0f && averageSize.y > 0f)
            {
                float avgWidth = averageSize.x * scale * zoom;
                float avgHeight = averageSize.y * scale * zoom;
                float avgLeft = (previewWidth - avgWidth) * 0.5f + pan.x;
                float avgTop = (previewHeight - avgHeight) * 0.5f + pan.y;
                var avgRect = new VisualElement();
                avgRect.AddToClassList("layout-average");
                avgRect.style.left = avgLeft;
                avgRect.style.top = avgTop;
                avgRect.style.width = avgWidth;
                avgRect.style.height = avgHeight;
                target.Add(avgRect);

                var avgLabel = new Label("avg");
                avgLabel.AddToClassList("layout-average-label");
                avgLabel.style.left = avgLeft + 6f;
                avgLabel.style.top = avgTop + 4f;
                target.Add(avgLabel);
            }

            var body = new VisualElement();
            body.AddToClassList("layout-body");
            body.style.left = bodyLeft;
            body.style.top = bodyTop;
            body.style.width = bodyWidth;
            body.style.height = bodyHeight;
            target.Add(body);
            if (interactive)
            {
                _layoutBodyActive = body;
            }

            if (_pinsContainer != null)
            {
                foreach (var entry in _pinsContainer.Children())
                {
                    var nameField = entry.Q<TextField>("PinNameField");
                    string pinName = nameField?.value?.Trim();
                    if (string.IsNullOrWhiteSpace(pinName)) continue;
                    float pinX = ReadFloat(entry.Q<TextField>("PinXField"));
                    float pinY = ReadFloat(entry.Q<TextField>("PinYField"));

                    float px = pinX >= 0f && pinX <= 1f ? pinX * bodyWidth : pinX * scale * zoom;
                    float py = pinY >= 0f && pinY <= 1f ? pinY * bodyHeight : pinY * scale * zoom;

                    var pin = new VisualElement();
                    pin.AddToClassList("layout-pin");
                    pin.style.left = bodyLeft + px - 5f;
                    pin.style.top = bodyTop + py - 5f;
                    pin.userData = pinName;
                    if (interactive)
                    {
                        pin.RegisterCallback<PointerDownEvent>(OnLayoutPinPointerDown);
                        _layoutPins[pinName] = pin;
                    }
                    target.Add(pin);

                    var label = new Label(pinName);
                    label.AddToClassList("layout-pin-label");
                    label.style.left = bodyLeft + px + 6f;
                    label.style.top = bodyTop + py - 6f;
                    target.Add(label);
                }
            }

            if (_labelsContainer != null)
            {
                foreach (var entry in _labelsContainer.Children())
                {
                    var textField = entry.Q<TextField>("LabelTextField");
                    string text = textField?.value?.Trim();
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    float lx = ReadFloat(entry.Q<TextField>("LabelXField"));
                    float ly = ReadFloat(entry.Q<TextField>("LabelYField"));
                    float size = ReadFloat(entry.Q<TextField>("LabelSizeField"), 10f);
                    string align = GetEntryValue(entry, "LabelAlignField");

                    float px = lx >= 0f && lx <= 1f ? lx * bodyWidth : lx * scale * zoom;
                    float py = ly >= 0f && ly <= 1f ? ly * bodyHeight : ly * scale * zoom;

                    var layoutLabel = new Label(text);
                    layoutLabel.AddToClassList("layout-text-label");
                    layoutLabel.style.left = bodyLeft + px;
                    layoutLabel.style.top = bodyTop + py;
                    layoutLabel.style.fontSize = Mathf.Max(8f, size);
                    layoutLabel.style.unityTextAlign = ResolveTextAlign(align);
                    layoutLabel.userData = entry;
                    if (interactive)
                    {
                        layoutLabel.RegisterCallback<PointerDownEvent>(OnLayoutLabelPointerDown);
                    }
                    target.Add(layoutLabel);
                }
            }

            if (_shapeLayout.Count == 0) return;
            foreach (var shape in _shapeLayout)
            {
                if (shape == null) continue;
                float px = shape.x >= 0f && shape.x <= 1f ? shape.x * bodyWidth : shape.x * scale * zoom;
                float py = shape.y >= 0f && shape.y <= 1f ? shape.y * bodyHeight : shape.y * scale * zoom;
                float w = shape.width >= 0f && shape.width <= 1f ? shape.width * bodyWidth : shape.width * scale * zoom;
                float h = shape.height >= 0f && shape.height <= 1f ? shape.height * bodyHeight : shape.height * scale * zoom;

                float fontSize = 0f;
                if (IsTextShapeType(shape.type))
                {
                    fontSize = ResolveTextFontSize(shape);
                    var textSize = EstimateTextPixelSize(shape.text, fontSize);
                    float pad = Mathf.Max(LayoutTextPadding, fontSize * 0.2f);
                    w = textSize.x + pad * 2f;
                    h = textSize.y + pad;
                    if (interactive)
                    {
                        UpdateShapeSizeFromPixels(shape, w, h, bodyWidth, bodyHeight, scale, zoom);
                    }
                }

                var element = new LayoutShapeElement(shape, shape.id == _selectedShapeId, fontSize);
                element.AddToClassList("layout-shape");
                element.style.left = bodyLeft + px - w * 0.5f;
                element.style.top = bodyTop + py - h * 0.5f;
                element.style.width = Mathf.Max(6f, w);
                element.style.height = Mathf.Max(6f, h);
                element.userData = shape;
                if (interactive)
                {
                    element.RegisterCallback<PointerDownEvent>(OnLayoutShapePointerDown);
                }
                target.Add(element);
            }
        }

        private static TextAnchor ResolveTextAlign(string align)
        {
            if (string.IsNullOrWhiteSpace(align)) return TextAnchor.MiddleCenter;
            string value = align.Trim().ToLowerInvariant();
            if (value.Contains("left")) return TextAnchor.MiddleLeft;
            if (value.Contains("right")) return TextAnchor.MiddleRight;
            return TextAnchor.MiddleCenter;
        }

        private static bool IsTextShapeType(string type)
        {
            return string.Equals(type, "Text", StringComparison.OrdinalIgnoreCase);
        }

        private static Vector2 RotatePoint(Vector2 point, Vector2 center, float angleDeg)
        {
            if (Mathf.Abs(angleDeg) < 0.01f) return point;
            float rad = angleDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            var offset = point - center;
            return new Vector2(
                offset.x * cos - offset.y * sin + center.x,
                offset.x * sin + offset.y * cos + center.y);
        }

        private static float ResolveTextFontSize(ShapeLayoutPayload shape)
        {
            if (shape == null) return LayoutTextDefaultFontSize;
            float size = shape.fontSize > 0f ? shape.fontSize : LayoutTextDefaultFontSize;
            return Mathf.Max(LayoutTextMinFontSize, size);
        }

        private static Vector2 EstimateTextPixelSize(string text, float fontSize)
        {
            if (string.IsNullOrWhiteSpace(text)) text = " ";
            float width = text.Length * fontSize * LayoutTextWidthFactor;
            float height = fontSize * LayoutTextHeightFactor;
            return new Vector2(Mathf.Max(6f, width), Mathf.Max(6f, height));
        }

        private static void UpdateShapeSizeFromPixels(ShapeLayoutPayload shape, float widthPx, float heightPx,
            float bodyWidth, float bodyHeight, float scale, float zoom)
        {
            if (shape == null) return;
            if (shape.width >= 0f && shape.width <= 1f)
            {
                shape.width = bodyWidth > 0f ? widthPx / bodyWidth : shape.width;
            }
            else
            {
                shape.width = (scale * zoom) > 0f ? widthPx / (scale * zoom) : shape.width;
            }

            if (shape.height >= 0f && shape.height <= 1f)
            {
                shape.height = bodyHeight > 0f ? heightPx / bodyHeight : shape.height;
            }
            else
            {
                shape.height = (scale * zoom) > 0f ? heightPx / (scale * zoom) : shape.height;
            }
        }

        private sealed class LayoutShapeElement : VisualElement
        {
            private readonly ShapeLayoutPayload _payload;
            private readonly bool _selected;
            private readonly Label _textLabel;

            public LayoutShapeElement(ShapeLayoutPayload payload, bool selected, float textFontSize)
            {
                _payload = payload;
                _selected = selected;
                pickingMode = PickingMode.Position;
                generateVisualContent += OnGenerateVisualContent;

                if (IsTextShape(_payload?.type))
                {
                    _textLabel = new Label(_payload?.text ?? "TEXT");
                    _textLabel.AddToClassList("layout-shape-text");
                    _textLabel.style.flexGrow = 1f;
                    _textLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    if (textFontSize > 0f)
                    {
                        _textLabel.style.fontSize = Mathf.Max(LayoutTextMinFontSize, textFontSize);
                    }
                    Add(_textLabel);
                }

                if (IsNoOutlineShape(_payload?.type))
                {
                    AddToClassList("shape-no-outline");
                }

                if (_selected)
                {
                    AddToClassList("selected");
                }
            }

            private static bool IsTextShape(string type)
            {
                return string.Equals(type, "Text", StringComparison.OrdinalIgnoreCase);
            }

            private static bool IsNoOutlineShape(string type)
            {
                if (string.IsNullOrWhiteSpace(type)) return false;
                return type.Equals("Text", StringComparison.OrdinalIgnoreCase) ||
                       type.Equals("Triangle", StringComparison.OrdinalIgnoreCase) ||
                       type.Equals("Line", StringComparison.OrdinalIgnoreCase) ||
                       type.Equals("Circle", StringComparison.OrdinalIgnoreCase);
            }

            private void OnGenerateVisualContent(MeshGenerationContext ctx)
            {
                if (_payload == null) return;
                var painter = ctx.painter2D;
                var rect = contentRect;
                painter.lineWidth = _selected ? 2f : 1f;
                painter.strokeColor = _selected ? Color.white : new Color(0.75f, 0.85f, 1f, 0.8f);
                painter.fillColor = new Color(0.15f, 0.2f, 0.28f, 0.25f);

                string type = _payload.type ?? string.Empty;
                if (type.Equals("Text", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            if (type.Equals("Line", StringComparison.OrdinalIgnoreCase))
            {
                Vector2 start;
                Vector2 end;
                if (_payload != null && _payload.lineFlip)
                {
                    start = new Vector2(rect.x + rect.width, rect.y);
                    end = new Vector2(rect.x, rect.y + rect.height);
                }
                else
                {
                    start = new Vector2(rect.x, rect.y);
                    end = new Vector2(rect.x + rect.width, rect.y + rect.height);
                }
                var center = rect.center;
                float rotation = _payload?.rotation ?? 0f;
                start = RotatePoint(start, center, rotation);
                end = RotatePoint(end, center, rotation);
                painter.BeginPath();
                painter.MoveTo(start);
                painter.LineTo(end);
                painter.Stroke();
                return;
            }

            if (type.Equals("Triangle", StringComparison.OrdinalIgnoreCase))
            {
                var center = rect.center;
                float rotation = _payload?.rotation ?? 0f;
                var p1 = RotatePoint(new Vector2(rect.x + rect.width * 0.5f, rect.y), center, rotation);
                var p2 = RotatePoint(new Vector2(rect.x + rect.width, rect.y + rect.height), center, rotation);
                var p3 = RotatePoint(new Vector2(rect.x, rect.y + rect.height), center, rotation);
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

            var boxCenter = rect.center;
            float boxRotation = _payload?.rotation ?? 0f;
            var b1 = RotatePoint(new Vector2(rect.x, rect.y), boxCenter, boxRotation);
            var b2 = RotatePoint(new Vector2(rect.x + rect.width, rect.y), boxCenter, boxRotation);
            var b3 = RotatePoint(new Vector2(rect.x + rect.width, rect.y + rect.height), boxCenter, boxRotation);
            var b4 = RotatePoint(new Vector2(rect.x, rect.y + rect.height), boxCenter, boxRotation);
            painter.BeginPath();
            painter.MoveTo(b1);
            painter.LineTo(b2);
            painter.LineTo(b3);
            painter.LineTo(b4);
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
            }
        }

        private Vector2 GetAverageComponentSize()
        {
            var items = ComponentCatalog.Items;
            if (items == null || items.Count == 0) return Vector2.zero;
            float totalX = 0f;
            float totalY = 0f;
            int count = 0;
            foreach (var item in items)
            {
                if (item.Size2D.x <= 0f || item.Size2D.y <= 0f) continue;
                totalX += item.Size2D.x;
                totalY += item.Size2D.y;
                count++;
            }
            if (count == 0) return Vector2.zero;
            return new Vector2(totalX / count, totalY / count);
        }

        private void OnLayoutPinPointerDown(PointerDownEvent evt)
        {
            if (_layoutPreviewActive == null) return;
            if (_layoutTool == LayoutTool.Pan || _layoutTool == LayoutTool.Label) return;
            if (evt.target is not VisualElement pin) return;
            if (evt.button == 1)
            {
                var entry = FindPinEntry(pin.userData as string);
                if (entry != null) SelectPinEntry(entry);
                _layoutContextPinName = pin.userData as string;
                _layoutContextLabelEntry = null;
                _layoutContextShapeId = null;
                PrepareLayoutContextMenu(true, false, false);
                ShowLayoutContextMenu(evt.position);
                evt.StopPropagation();
                return;
            }
            _draggingPinName = pin.userData as string;
            if (string.IsNullOrWhiteSpace(_draggingPinName)) return;
            _draggingPin = true;
            _layoutKeyFocus = true;
            _dragPointerId = evt.pointerId;
            if (_layoutPreviewActive.HasPointerCapture(_dragPointerId) == false)
            {
                _layoutPreviewActive.CapturePointer(_dragPointerId);
            }
            evt.StopPropagation();
        }

        private void OnLayoutLabelPointerDown(PointerDownEvent evt)
        {
            if (_layoutPreviewActive == null) return;
            if (_layoutTool == LayoutTool.Pan || _layoutTool == LayoutTool.Pin) return;
            if (evt.target is not VisualElement label) return;
            if (label.userData is not VisualElement entry) return;
            if (evt.button == 1)
            {
                SelectLabelEntry(entry);
                _layoutContextPinName = null;
                _layoutContextLabelEntry = entry;
                _layoutContextShapeId = null;
                PrepareLayoutContextMenu(false, true, false);
                ShowLayoutContextMenu(evt.position);
                evt.StopPropagation();
                return;
            }
            SelectLabelEntry(entry);
            _draggingLabel = true;
            _draggingLabelEntry = entry;
            _layoutKeyFocus = true;
            _dragPointerId = evt.pointerId;
            if (_layoutPreviewActive.HasPointerCapture(_dragPointerId) == false)
            {
                _layoutPreviewActive.CapturePointer(_dragPointerId);
            }
            evt.StopPropagation();
        }

        private void OnLayoutShapePointerDown(PointerDownEvent evt)
        {
            if (_layoutPreviewActive == null) return;
            if (evt.currentTarget is not VisualElement element) return;
            if (element.userData is not ShapeLayoutPayload shape) return;
            if (evt.button == 1)
            {
                SelectShape(shape.id);
                _layoutContextPinName = null;
                _layoutContextLabelEntry = null;
                _layoutContextShapeId = shape.id;
                PrepareLayoutContextMenu(false, false, true);
                ShowLayoutContextMenu(evt.position);
                evt.StopPropagation();
                return;
            }
            SelectShape(shape.id);
            _draggingShape = true;
            _draggingShapeId = shape.id;
            _layoutKeyFocus = true;
            _dragPointerId = evt.pointerId;
            if (TryGetLayoutShapeCenterPixel(shape, out var center))
            {
                var local = new Vector2(evt.localPosition.x, evt.localPosition.y);
                _shapeRotateStartAngle = Mathf.Atan2(local.y - center.y, local.x - center.x);
                _shapeRotateBaseAngle = shape.rotation;
                _shapeScaleStartDistance = Mathf.Max(1f, Vector2.Distance(center, local));
                _shapeScaleBaseWidth = Mathf.Max(0.02f, shape.width);
                _shapeScaleBaseHeight = Mathf.Max(0.02f, shape.height);
            }
            if (_layoutPreviewActive.HasPointerCapture(_dragPointerId) == false)
            {
                _layoutPreviewActive.CapturePointer(_dragPointerId);
            }
            evt.StopPropagation();
        }

        private void OnLayoutPreviewPointerDown(PointerDownEvent evt)
        {
            if (_layoutPreviewLarge == null || _layoutPreviewActive != _layoutPreviewLarge) return;
            _layoutKeyFocus = true;
            if (evt.button == 1)
            {
                _layoutContextPinName = null;
                _layoutContextLabelEntry = null;
                _layoutContextShapeId = null;
                PrepareLayoutContextMenu(false, false, false);
                ShowLayoutContextMenu(evt.position);
                evt.StopPropagation();
                return;
            }
            if (evt.button == 0 && evt.ctrlKey)
            {
                if (_selectedPinEntry != null)
                {
                    _draggingPinName = _selectedPinEntry.Q<TextField>("PinNameField")?.value?.Trim();
                    if (!string.IsNullOrWhiteSpace(_draggingPinName))
                    {
                        _draggingPin = true;
                        _dragPointerId = evt.pointerId;
                        if (_layoutPreviewActive.HasPointerCapture(_dragPointerId) == false)
                        {
                            _layoutPreviewActive.CapturePointer(_dragPointerId);
                        }
                        evt.StopPropagation();
                        return;
                    }
                }
                if (_selectedLabelEntry != null)
                {
                    _draggingLabel = true;
                    _draggingLabelEntry = _selectedLabelEntry;
                    _dragPointerId = evt.pointerId;
                    if (_layoutPreviewActive.HasPointerCapture(_dragPointerId) == false)
                    {
                        _layoutPreviewActive.CapturePointer(_dragPointerId);
                    }
                    evt.StopPropagation();
                    return;
                }
                if (!string.IsNullOrWhiteSpace(_selectedShapeId))
                {
                    _draggingShape = true;
                    _draggingShapeId = _selectedShapeId;
                    _dragPointerId = evt.pointerId;
                    if (_layoutPreviewActive.HasPointerCapture(_dragPointerId) == false)
                    {
                        _layoutPreviewActive.CapturePointer(_dragPointerId);
                    }
                    evt.StopPropagation();
                    return;
                }
            }
            if (evt.button == 0 && (_layoutTool == LayoutTool.Pin || _layoutTool == LayoutTool.Label ||
                                    _layoutTool == LayoutTool.Box || _layoutTool == LayoutTool.Triangle ||
                                    _layoutTool == LayoutTool.Line || _layoutTool == LayoutTool.Text ||
                                    _layoutTool == LayoutTool.Circle))
            {
                if (TryGetLayoutNormalizedPosition(new Vector2(evt.localPosition.x, evt.localPosition.y), out var nx, out var ny))
                {
                    if (_layoutTool == LayoutTool.Pin)
                    {
                        var entry = AddPinRow(new PinLayoutPayload { name = GetNextPinName(), x = nx, y = ny });
                        if (entry != null) SelectPinEntry(entry);
                        RefreshLayoutPreview();
                        QueueValidation();
                    }
                    else if (_layoutTool == LayoutTool.Label)
                    {
                        AddLabelRow(new LabelLayoutPayload { text = "{id}", x = nx, y = ny, size = 10, align = "center" });
                        RefreshLayoutPreview();
                        QueueValidation();
                    }
                    else
                    {
                        BeginShapePlacement(nx, ny, _layoutTool, evt.pointerId);
                        RefreshLayoutPreview();
                    }
                    evt.StopPropagation();
                    return;
                }
            }
            bool allowPan = (_layoutTool == LayoutTool.Pan && evt.button == 0) || evt.button == 1 || evt.button == 2;
            if (!allowPan) return;
            _layoutPanning = true;
            _layoutPanPointerId = evt.pointerId;
            _layoutPanStart = new Vector2(evt.localPosition.x, evt.localPosition.y);
            _layoutPanStartOffset = _layoutPanOffset;
            if (_layoutPreviewActive.HasPointerCapture(_layoutPanPointerId) == false)
            {
                _layoutPreviewActive.CapturePointer(_layoutPanPointerId);
            }
            evt.StopPropagation();
        }

        private bool TryGetLayoutNormalizedPosition(Vector2 local, out float nx, out float ny)
        {
            nx = 0f;
            ny = 0f;
            if (_layoutBodyActive == null) return false;
            float bodyLeft = _layoutBodyActive.resolvedStyle.left;
            float bodyTop = _layoutBodyActive.resolvedStyle.top;
            float bodyWidth = _layoutBodyActive.resolvedStyle.width;
            float bodyHeight = _layoutBodyActive.resolvedStyle.height;
            if (bodyWidth <= 1f || bodyHeight <= 1f) return false;
            nx = Mathf.Clamp01((local.x - bodyLeft) / bodyWidth);
            ny = Mathf.Clamp01((local.y - bodyTop) / bodyHeight);
            return true;
        }

        private bool TryGetLayoutShapeCenterPixel(ShapeLayoutPayload shape, out Vector2 center)
        {
            center = Vector2.zero;
            if (_layoutBodyActive == null || shape == null) return false;
            float bodyLeft = _layoutBodyActive.resolvedStyle.left;
            float bodyTop = _layoutBodyActive.resolvedStyle.top;
            float bodyWidth = _layoutBodyActive.resolvedStyle.width;
            float bodyHeight = _layoutBodyActive.resolvedStyle.height;
            if (bodyWidth <= 1f || bodyHeight <= 1f) return false;
            float sizeX = ReadFloat(GetField("ComponentSizeXField"), CircuitLayoutSizing.DefaultComponentWidth);
            float sizeY = ReadFloat(GetField("ComponentSizeYField"), CircuitLayoutSizing.DefaultComponentHeight);
            if (sizeX <= 0f || sizeY <= 0f) return false;
            float scaleX = bodyWidth / sizeX;
            float scaleY = bodyHeight / sizeY;

            float cx = shape.x >= 0f && shape.x <= 1f ? shape.x * bodyWidth : shape.x * scaleX;
            float cy = shape.y >= 0f && shape.y <= 1f ? shape.y * bodyHeight : shape.y * scaleY;
            center = new Vector2(bodyLeft + cx, bodyTop + cy);
            return true;
        }

        private void OnLayoutPointerMove(PointerMoveEvent evt)
        {
            if (_layoutPanning)
            {
                if (_layoutPreviewActive == null) return;
                if (_layoutPanPointerId == -1)
                {
                    StopLayoutInteractions();
                    return;
                }
                if (_layoutPanPointerId != evt.pointerId) return;
                Vector2 delta = new Vector2(evt.localPosition.x, evt.localPosition.y) - _layoutPanStart;
                _layoutPanOffset = _layoutPanStartOffset + delta * LayoutPanSpeed;
                RefreshLayoutPreview();
                evt.StopPropagation();
                return;
            }
            if (_placingShape)
            {
                if (_layoutPreviewActive == null) return;
                if (_dragPointerId != -1 && evt.pointerId != _dragPointerId) return;
                if (UpdatePlacingShape(new Vector2(evt.localPosition.x, evt.localPosition.y)))
                {
                    RefreshLayoutPreview();
                    evt.StopPropagation();
                }
                return;
            }
            if (_draggingPin && !string.IsNullOrWhiteSpace(_draggingPinName))
            {
                if (_layoutPreviewActive == null || _layoutBodyActive == null) return;

                Vector2 local = new Vector2(evt.localPosition.x, evt.localPosition.y);
                float bodyLeft = _layoutBodyActive.resolvedStyle.left;
                float bodyTop = _layoutBodyActive.resolvedStyle.top;
                float bodyWidth = _layoutBodyActive.resolvedStyle.width;
                float bodyHeight = _layoutBodyActive.resolvedStyle.height;
                if (bodyWidth <= 1f || bodyHeight <= 1f) return;

                float nx = Mathf.Clamp01((local.x - bodyLeft) / bodyWidth);
                float ny = Mathf.Clamp01((local.y - bodyTop) / bodyHeight);

                var entry = FindPinEntry(_draggingPinName);
                if (entry != null)
                {
                    SetEntryValue(entry, "PinXField", nx.ToString("0.###", CultureInfo.InvariantCulture));
                    SetEntryValue(entry, "PinYField", ny.ToString("0.###", CultureInfo.InvariantCulture));
                }

                RefreshLayoutPreview();
                evt.StopPropagation();
                return;
            }

            if (_draggingShape && !string.IsNullOrWhiteSpace(_draggingShapeId))
            {
                if (_layoutPreviewActive == null || _layoutBodyActive == null) return;
                Vector2 local = new Vector2(evt.localPosition.x, evt.localPosition.y);
                float bodyLeft = _layoutBodyActive.resolvedStyle.left;
                float bodyTop = _layoutBodyActive.resolvedStyle.top;
                float bodyWidth = _layoutBodyActive.resolvedStyle.width;
                float bodyHeight = _layoutBodyActive.resolvedStyle.height;
                if (bodyWidth <= 1f || bodyHeight <= 1f) return;

                float nx = Mathf.Clamp01((local.x - bodyLeft) / bodyWidth);
                float ny = Mathf.Clamp01((local.y - bodyTop) / bodyHeight);
                var shape = _shapeLayout.FirstOrDefault(s => s != null && s.id == _draggingShapeId);
                if (shape != null)
                {
                    if (_layoutTool == LayoutTool.Rotate)
                    {
                        if (TryGetLayoutShapeCenterPixel(shape, out var center))
                        {
                            float angle = Mathf.Atan2(local.y - center.y, local.x - center.x);
                            float delta = angle - _shapeRotateStartAngle;
                            shape.rotation = Mathf.Repeat(_shapeRotateBaseAngle + delta * Mathf.Rad2Deg, 360f);
                            RefreshLayoutPreview();
                            evt.StopPropagation();
                            return;
                        }
                    }
                    if (_layoutTool == LayoutTool.Scale)
                    {
                        if (TryGetLayoutShapeCenterPixel(shape, out var center))
                        {
                            float dist = Mathf.Max(1f, Vector2.Distance(center, local));
                            float scale = dist / Mathf.Max(1f, _shapeScaleStartDistance);
                            shape.width = Mathf.Max(0.02f, _shapeScaleBaseWidth * scale);
                            shape.height = Mathf.Max(0.02f, _shapeScaleBaseHeight * scale);
                            RefreshLayoutPreview();
                            evt.StopPropagation();
                            return;
                        }
                    }
                    if (evt.altKey)
                    {
                        shape.width = Mathf.Clamp(Mathf.Abs(nx - shape.x) * 2f, 0.02f, 1f);
                        shape.height = Mathf.Clamp(Mathf.Abs(ny - shape.y) * 2f, 0.02f, 1f);
                    }
                    else
                    {
                        shape.x = nx;
                        shape.y = ny;
                    }
                }

                RefreshLayoutPreview();
                evt.StopPropagation();
                return;
            }

            if (!_draggingLabel || _draggingLabelEntry == null) return;
            if (_layoutPreviewActive == null || _layoutBodyActive == null) return;

            Vector2 labelLocal = new Vector2(evt.localPosition.x, evt.localPosition.y);
            float labelLeft = _layoutBodyActive.resolvedStyle.left;
            float labelTop = _layoutBodyActive.resolvedStyle.top;
            float labelWidth = _layoutBodyActive.resolvedStyle.width;
            float labelHeight = _layoutBodyActive.resolvedStyle.height;
            if (labelWidth <= 1f || labelHeight <= 1f) return;

            float lnx = Mathf.Clamp01((labelLocal.x - labelLeft) / labelWidth);
            float lny = Mathf.Clamp01((labelLocal.y - labelTop) / labelHeight);

            SetEntryValue(_draggingLabelEntry, "LabelXField", lnx.ToString("0.###", CultureInfo.InvariantCulture));
            SetEntryValue(_draggingLabelEntry, "LabelYField", lny.ToString("0.###", CultureInfo.InvariantCulture));

            RefreshLayoutPreview();
            evt.StopPropagation();
        }

        private void OnLayoutPointerUp(PointerUpEvent evt)
        {
            if (_layoutPanning && evt.pointerId == _layoutPanPointerId)
            {
                StopLayoutInteractions();
                evt.StopPropagation();
                return;
            }
            if (_placingShape && evt.pointerId == _dragPointerId)
            {
                FinishPlacingShape();
                ReleaseLayoutPointerCapture();
                RefreshLayoutPreview();
                QueueValidation();
                evt.StopPropagation();
                return;
            }
            if (!_draggingPin && !_draggingLabel && !_draggingShape) return;
            StopLayoutInteractions();
            QueueValidation();
        }

        private void OnLayoutPointerLeave(PointerLeaveEvent evt)
        {
            if (_layoutPanning && evt.pointerId == _layoutPanPointerId)
            {
                StopLayoutInteractions();
                evt.StopPropagation();
                return;
            }
            if (_placingShape && evt.pointerId == _dragPointerId)
            {
                FinishPlacingShape();
                ReleaseLayoutPointerCapture();
                RefreshLayoutPreview();
                QueueValidation();
                evt.StopPropagation();
                return;
            }
            if (!_draggingPin && !_draggingLabel && !_draggingShape) return;
            StopLayoutInteractions();
            QueueValidation();
        }

        private void OnLayoutPreviewWheel(WheelEvent evt)
        {
            if (_layoutPreviewLarge == null || _layoutPreviewActive != _layoutPreviewLarge) return;
            var local = _layoutPreviewLarge.WorldToLocal(evt.mousePosition);
            ApplyLayoutZoom(-evt.delta.y * LayoutZoomSpeed, new Vector2(local.x, local.y));
            evt.StopPropagation();
        }

        private Vector2 GetLayoutZoomCenter()
        {
            if (_layoutPreviewLarge == null) return Vector2.zero;
            float width = _layoutPreviewLarge.resolvedStyle.width;
            float height = _layoutPreviewLarge.resolvedStyle.height;
            return new Vector2(width * 0.5f, height * 0.5f);
        }

        private void ApplyLayoutZoom(float zoomDelta, Vector2 localPoint)
        {
            if (_layoutPreviewLarge == null || _layoutPreviewActive != _layoutPreviewLarge) return;
            if (Mathf.Abs(zoomDelta) < 0.0001f) return;

            float oldZoom = _layoutZoom;
            float newZoom = Mathf.Clamp(oldZoom * (1f + zoomDelta), LayoutZoomMin, LayoutZoomMax);
            if (Mathf.Abs(newZoom - oldZoom) < 0.0001f) return;

            float width = _layoutPreviewLarge.resolvedStyle.width;
            float height = _layoutPreviewLarge.resolvedStyle.height;
            var center = new Vector2(width * 0.5f, height * 0.5f);
            Vector2 content = (localPoint - center - _layoutPanOffset) / oldZoom;
            _layoutZoom = newZoom;
            _layoutPanOffset = localPoint - center - content * newZoom;
            ClampLayoutPanOffset();

            RefreshLayoutPreview();
        }

        private void OnLayoutPointerCancel(PointerCancelEvent evt)
        {
            if (_placingShape)
            {
                CancelPlacingShape();
            }
            if (_draggingPin || _draggingShape || _placingShape || _layoutPanning)
            {
                StopLayoutInteractions();
            }
            evt.StopPropagation();
        }

        private void OnLayoutPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_placingShape)
            {
                CancelPlacingShape();
            }
            if (_draggingPin || _draggingShape || _placingShape || _layoutPanning)
            {
                StopLayoutInteractions();
            }
            evt.StopPropagation();
        }

        private void ReleaseLayoutPointerCapture()
        {
            if (_layoutPreviewActive == null) return;
            if (_dragPointerId >= 0 && _layoutPreviewActive.HasPointerCapture(_dragPointerId))
            {
                _layoutPreviewActive.ReleasePointer(_dragPointerId);
                _dragPointerId = -1;
            }
            if (_layoutPanPointerId >= 0 && _layoutPreviewActive.HasPointerCapture(_layoutPanPointerId))
            {
                _layoutPreviewActive.ReleasePointer(_layoutPanPointerId);
                _layoutPanPointerId = -1;
            }
        }

        private VisualElement FindPinEntry(string pinName)
        {
            if (_pinsContainer == null || string.IsNullOrWhiteSpace(pinName)) return null;
            foreach (var entry in _pinsContainer.Children())
            {
                var nameField = entry.Q<TextField>("PinNameField");
                if (string.Equals(nameField?.value?.Trim(), pinName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
            return null;
        }

        private VisualElement FindLabelEntry(string text)
        {
            if (_labelsContainer == null || string.IsNullOrWhiteSpace(text)) return null;
            foreach (var entry in _labelsContainer.Children())
            {
                var textField = entry.Q<TextField>("LabelTextField");
                if (string.Equals(textField?.value?.Trim(), text, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
            return null;
        }

        private void RefreshPartsList()
        {
            if (_partsContainer == null) return;
            _partsContainer.Clear();
            _partBaseTransforms.Clear();
            if (_studioView == null || _studioView.Parts == null || _studioView.Parts.Count == 0)
            {
                var empty = new Label("No model parts loaded.");
                empty.AddToClassList("empty-state");
                _partsContainer.Add(empty);
                RefreshCatalogAndHierarchy();
                return;
            }

            foreach (var part in _studioView.Parts)
            {
                if (part == null) continue;
                _partBaseTransforms[part.name] = new PartTransformSnapshot
                {
                    Position = part.localPosition,
                    Rotation = part.localEulerAngles,
                    Scale = part.localScale
                };
                var row = new VisualElement();
                row.AddToClassList("part-row");
                row.userData = part.name;
                row.RegisterCallback<PointerDownEvent>(_ => SelectPartEntry(row));

                var name = new Label(part.name);
                name.AddToClassList("part-name");
                row.Add(name);
                _partsContainer.Add(row);

                if (!_partOverrides.ContainsKey(part.name))
                {
                    _partOverrides[part.name] = new PartOverride(part);
                }
            }
            ApplyStateOverridesToModel();
            RefreshCatalogAndHierarchy();
        }

        private void ApplyStateOverridesToModel()
        {
            if (_studioView == null) return;
            foreach (var overrideData in _partOverrides.Values)
            {
                ApplyPartOverrideToModel(overrideData);
            }

            if (!string.IsNullOrWhiteSpace(_selectedStateId) &&
                _stateOverrides.TryGetValue(_selectedStateId, out var state))
            {
                foreach (var overrideData in state.Parts.Values)
                {
                    ApplyPartOverrideToModel(overrideData);
                }
            }
            if (_partColorsEnabled)
            {
                ApplyPartColorOverlay();
            }
        }

        private void ApplyPartOverrideToModel(PartOverride overrideData)
        {
            if (overrideData == null || string.IsNullOrWhiteSpace(overrideData.Name)) return;
            if (!TryGetPartTransform(overrideData.Name, out var part)) return;
            _studioView.SetPartTransform(part, overrideData.Position, overrideData.Rotation, overrideData.Scale);
            ApplyPartMaterial(part, overrideData, overrideData.UseTexture ? ResolveTexturePath(overrideData.TextureEntry) : null);
        }

        private void RefreshStatesList()
        {
            if (_statesContainer == null) return;
            _statesContainer.Clear();
            if (_stateOverrides.Count == 0)
            {
                _stateOverrides["default"] = new StateOverride("default");
            }

            foreach (var state in _stateOverrides.Values.OrderBy(s => s.Id))
            {
                var row = new VisualElement();
                row.AddToClassList("state-row");
                row.userData = state.Id;
                row.RegisterCallback<PointerDownEvent>(_ => SelectStateEntry(row));
                var label = new Label(state.Id);
                label.AddToClassList("state-name");
                row.Add(label);
                if (!string.IsNullOrWhiteSpace(_selectedStateId) &&
                    string.Equals(_selectedStateId, state.Id, StringComparison.OrdinalIgnoreCase))
                {
                    row.AddToClassList("selected");
                    _selectedStateEntry = row;
                }
                _statesContainer.Add(row);
            }
            RefreshCatalogAndHierarchy();
        }

        private void AddStateOverride()
        {
            string stateId = _stateNameField?.value?.Trim();
            if (string.IsNullOrWhiteSpace(stateId)) return;
            if (_stateOverrides.ContainsKey(stateId)) return;
            _stateOverrides[stateId] = new StateOverride(stateId);
            RefreshStatesList();
            _stateNameField?.SetValueWithoutNotify(string.Empty);
            _selectedStateId = stateId;
            RefreshStatesList();
            ApplyStateOverridesToModel();
            QueueValidation();
        }

        private void SelectStateEntry(VisualElement entry)
        {
            if (_selectedStateEntry == entry) return;
            if (_selectedStateEntry != null) _selectedStateEntry.RemoveFromClassList("selected");
            _selectedStateEntry = entry;
            if (_selectedStateEntry != null) _selectedStateEntry.AddToClassList("selected");
            _selectedStateId = entry?.userData as string;
            ApplyStateOverridesToModel();
            LoadSelectedPart();
            UpdateInspectorForSelection("State", entry);
            _studioView?.ClearSelectionOutline();
        }

        private void SelectPartEntry(VisualElement entry)
        {
            if (_selectedPartEntry == entry) return;
            if (_selectedPartEntry != null) _selectedPartEntry.RemoveFromClassList("selected");
            _selectedPartEntry = entry;
            if (_selectedPartEntry != null) _selectedPartEntry.AddToClassList("selected");
            _selectedPartName = entry?.userData as string;
            LoadSelectedPart();
            RefreshTransformGizmo();
            if (_materialOverlay != null && _materialOverlay.style.display == DisplayStyle.Flex)
            {
                UpdateMaterialModalFromSelection();
            }
            UpdateInspectorForSelection("Part", entry);
            if (_studioView != null && TryGetPartTransform(_selectedPartName, out var part))
            {
                _studioView.SetSelectionOutline(part);
            }
            else
            {
                _studioView?.ClearSelectionOutline();
            }
        }

        private void SelectModelRoot()
        {
            if (_selectedPartEntry != null) _selectedPartEntry.RemoveFromClassList("selected");
            _selectedPartEntry = null;
            _selectedPartName = null;
            if (_selectedPinEntry != null) _selectedPinEntry.RemoveFromClassList("selected");
            if (_selectedLabelEntry != null) _selectedLabelEntry.RemoveFromClassList("selected");
            if (_selectedStateEntry != null) _selectedStateEntry.RemoveFromClassList("selected");
            _selectedPinEntry = null;
            _selectedLabelEntry = null;
            _selectedStateEntry = null;
            _selectedStateId = null;
            UpdateInspectorForModel();
            if (_materialOverlay != null && _materialOverlay.style.display == DisplayStyle.Flex)
            {
                UpdateMaterialModalFromSelection();
            }
            var modelRoot = _studioView?.ModelRoot;
            if (_studioView != null && modelRoot != null)
            {
                _studioView.SetSelectionOutline(modelRoot);
            }
            else
            {
                _studioView?.ClearSelectionOutline();
            }
        }

        private void UpdateInspectorForModel()
        {
            if (_inspectorTargetLabel != null)
            {
                _inspectorTargetLabel.text = $"Model: {GetModelDisplayName()}";
            }
            if (_inspectorDetailsContainer == null) return;
            _inspectorDetailsContainer.Clear();
            var detail = new Label("Root selection");
            detail.AddToClassList("form-label");
            _inspectorDetailsContainer.Add(detail);
        }

        private void LoadSelectedPart()
        {
            if (string.IsNullOrWhiteSpace(_selectedPartName)) return;
            if (!TryGetPartTransform(_selectedPartName, out var part)) return;

            var overrideData = GetOrCreateActivePartOverride(part);

            SetField(_partPosXField, FormatFloat(overrideData.Position.x));
            SetField(_partPosYField, FormatFloat(overrideData.Position.y));
            SetField(_partPosZField, FormatFloat(overrideData.Position.z));
            SetField(_partRotXField, FormatFloat(overrideData.Rotation.x));
            SetField(_partRotYField, FormatFloat(overrideData.Rotation.y));
            SetField(_partRotZField, FormatFloat(overrideData.Rotation.z));
            SetField(_partScaleXField, FormatFloat(overrideData.Scale.x));
            SetField(_partScaleYField, FormatFloat(overrideData.Scale.y));
            SetField(_partScaleZField, FormatFloat(overrideData.Scale.z));
            SetField(_partColorRField, FormatFloat(overrideData.Color.r));
            SetField(_partColorGField, FormatFloat(overrideData.Color.g));
            SetField(_partColorBField, FormatFloat(overrideData.Color.b));
            SetField(_partColorAField, FormatFloat(overrideData.Color.a));
            if (_partUseColorToggle != null) _partUseColorToggle.SetValueWithoutNotify(overrideData.UseColor);
            ApplyFollowSelection();
        }

        private void ApplySelectedPartOverride()
        {
            if (string.IsNullOrWhiteSpace(_selectedPartName)) return;
            if (!TryGetPartTransform(_selectedPartName, out var part)) return;

            var overrideData = GetOrCreateActivePartOverride(part);
            overrideData.Position = new Vector3(
                ReadFloat(_partPosXField, part.localPosition.x),
                ReadFloat(_partPosYField, part.localPosition.y),
                ReadFloat(_partPosZField, part.localPosition.z));
            overrideData.Rotation = new Vector3(
                ReadFloat(_partRotXField, part.localEulerAngles.x),
                ReadFloat(_partRotYField, part.localEulerAngles.y),
                ReadFloat(_partRotZField, part.localEulerAngles.z));
            overrideData.Scale = new Vector3(
                ReadFloat(_partScaleXField, part.localScale.x),
                ReadFloat(_partScaleYField, part.localScale.y),
                ReadFloat(_partScaleZField, part.localScale.z));
            overrideData.Color = new Color(
                ReadFloat(_partColorRField, 1f),
                ReadFloat(_partColorGField, 1f),
                ReadFloat(_partColorBField, 1f),
                ReadFloat(_partColorAField, 1f));
            overrideData.UseColor = _partUseColorToggle != null && _partUseColorToggle.value;

            _studioView.SetPartTransform(part, overrideData.Position, overrideData.Rotation, overrideData.Scale);
            ApplyPartMaterial(part, overrideData, overrideData.UseTexture ? ResolveTexturePath(overrideData.TextureEntry) : null);
            QueueValidation();
        }

        private bool TryGetPartTransform(string name, out Transform part)
        {
            part = null;
            if (_studioView == null || _studioView.Parts == null) return false;
            foreach (var candidate in _studioView.Parts)
            {
                if (candidate == null) continue;
                if (string.Equals(candidate.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    part = candidate;
                    return true;
                }
            }
            return false;
        }

        private void OnViewportPointerDown(PointerDownEvent evt)
        {
            if (_studioView == null) return;
            _viewport?.Focus();
            _viewportKeyFocus = true;
            var pointerPos = new Vector2(evt.position.x, evt.position.y);
            _lastPointer = pointerPos;

            if (IsAnchorPickEnabled() && _selectedPinEntry != null)
            {
                TryPickAnchor(pointerPos);
                evt.StopPropagation();
                return;
            }

            if (evt.button == 0 && IsViewportEditActive())
            {
                if (TryGetViewportPoint(pointerPos, out var viewportPoint))
                {
                    if (_studioView.TryPickGizmoHandle(viewportPoint, out var handle, out _))
                    {
                        if (BeginGizmoDrag(handle, pointerPos, viewportPoint))
                        {
                            evt.StopPropagation();
                            return;
                        }
                    }
                    if (_studioView.TryPickEditMarker(viewportPoint, out var marker))
                    {
                        BeginViewportEdit(marker, pointerPos);
                        evt.StopPropagation();
                        return;
                    }
                    if (IsPartEditEnabled() && _studioView.TryPickPart(viewportPoint, out var part))
                    {
                        if (evt.clickCount >= 2)
                        {
                            SelectPartByName(part.name);
                            BeginViewportEdit(part, pointerPos);
                            if (_activeTool == ToolAction.Material)
                            {
                                OpenMaterialModal();
                            }
                            evt.StopPropagation();
                            return;
                        }
                    }
                }
            }

            if (evt.button == 0)
            {
                _orbiting = true;
            }
            else if (evt.button == 1 || evt.button == 2)
            {
                _panning = true;
            }
            if (_orbiting || _panning)
            {
                DisableFollowDuringCameraMove();
            }
            if (_orbiting || _panning)
            {
                _viewportPointerId = evt.pointerId;
                if (_viewport != null && !_viewport.HasPointerCapture(evt.pointerId))
                {
                    _viewport.CapturePointer(evt.pointerId);
                }
            }
            evt.StopPropagation();
        }

        private void DisableFollowDuringCameraMove()
        {
            if (_circuit3DFollowToggle == null || !_circuit3DFollowToggle.value) return;
            _circuit3DFollowToggle.SetValueWithoutNotify(false);
            ApplyFollowSelection();
        }

        private void OnViewportPointerMove(PointerMoveEvent evt)
        {
            if (_studioView == null) return;
            if (_viewportPointerId != -1 && evt.pointerId != _viewportPointerId) return;
            var pointerPos = new Vector2(evt.position.x, evt.position.y);
            var delta = pointerPos - _lastPointer;
            _lastPointer = pointerPos;

            if (_isViewportEditDragging)
            {
                if (TryGetViewportPoint(pointerPos, out var viewportPoint))
                {
                    UpdateViewportEdit(viewportPoint, pointerPos - _editStartPointer);
                    evt.StopPropagation();
                    return;
                }
            }

            if (_orbiting)
            {
                _studioView.Orbit(delta * 0.2f);
            }
            else if (_panning)
            {
                _studioView.Pan(new Vector2(delta.x, -delta.y) * 0.8f);
            }
            if (_orbiting || _panning)
            {
                evt.StopPropagation();
            }
        }

        private void OnViewportPointerUp(PointerUpEvent evt)
        {
            if (_viewportPointerId != -1 && evt.pointerId != _viewportPointerId) return;
            if (_isViewportEditDragging)
            {
                EndViewportEdit();
                evt.StopPropagation();
                return;
            }
            _orbiting = false;
            _panning = false;
            ReleaseViewportPointerCapture();
            evt.StopPropagation();
        }

        private void ReleaseViewportPointerCapture()
        {
            if (_viewport != null && _viewportPointerId != -1 && _viewport.HasPointerCapture(_viewportPointerId))
            {
                _viewport.ReleasePointer(_viewportPointerId);
            }
            _viewportPointerId = -1;
        }

        private void OnViewportWheel(WheelEvent evt)
        {
            if (_studioView == null) return;
            if (_editTarget.Kind == EditTargetKind.Anchor && _viewportEditMode == ViewportEditMode.Scale && evt.shiftKey)
            {
                float radius = _editStartScale.x * 0.5f + evt.delta.y * 0.0005f;
                ApplyAnchorRadius(_editTarget.Id, radius);
                evt.StopPropagation();
                return;
            }

            float wheel = -evt.delta.y;
            float zoomDelta;
            if (Mathf.Abs(wheel) < 1.5f)
            {
                zoomDelta = Mathf.Sign(wheel) * CameraKeyZoomDelta;
            }
            else if (Mathf.Abs(wheel) < 15f)
            {
                zoomDelta = wheel * 40f;
            }
            else
            {
                zoomDelta = wheel;
            }
            _studioView.Zoom(zoomDelta * CameraKeyZoomScale);
            evt.StopPropagation();
        }

        private void OnViewCubeGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateViewCubeSurface();
        }

        private void OnViewCubePointerDown(PointerDownEvent evt)
        {
            if (_studioView == null || _viewCubeSurface == null) return;
            var rect = _viewCubeSurface.worldBound;
            if (rect.width <= 0f || rect.height <= 0f) return;
            var local = new Vector2(evt.position.x - rect.xMin, evt.position.y - rect.yMin);
            var viewportPoint = new Vector2(local.x / rect.width, 1f - (local.y / rect.height));
            if (_studioView.TryPickViewCubeFace(viewportPoint, out var preset))
            {
                _studioView.SnapView(preset);
                _studioView.FrameModel();
                _viewport?.Focus();
                _viewportKeyFocus = true;
                evt.StopPropagation();
            }
        }

        private void UpdateViewCubeSurface()
        {
            if (_studioView == null || _viewCubeSurface == null) return;
            float width = _viewCubeSurface.resolvedStyle.width;
            float height = _viewCubeSurface.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            {
                var rect = _viewCubeSurface.worldBound;
                width = rect.width;
                height = rect.height;
            }
            int size = Mathf.RoundToInt(Mathf.Max(0f, Mathf.Min(width, height)));
            if (size <= 0) return;
            _studioView.SetViewCubeSize(size);
            var texture = _studioView.ViewCubeTexture;
            if (texture == null) return;
            _viewCubeSurface.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(texture));
            _viewCubeSurface.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        }

        private bool IsViewportKeyTarget()
        {
            if (_viewport == null || _root == null) return false;
            if (_menuLayer != null && _menuLayer.style.display == DisplayStyle.Flex) return false;
            if (_materialOverlay != null && _materialOverlay.style.display == DisplayStyle.Flex) return false;
            if (_is2dView) return false;
            if (_viewportKeyFocus) return true;
            if (_viewportHover) return true;
            var focused = _root.panel?.focusController?.focusedElement as VisualElement;
            if (focused == null) return false;
            if (_viewport != null && (focused == _viewport || _viewport.Contains(focused))) return true;
            return _component3DPanel != null && (focused == _component3DPanel || _component3DPanel.Contains(focused));
        }

        private bool IsLayoutKeyTarget()
        {
            if (_layoutPreviewLarge == null || _root == null) return false;
            if (_menuLayer != null && _menuLayer.style.display == DisplayStyle.Flex) return false;
            if (_materialOverlay != null && _materialOverlay.style.display == DisplayStyle.Flex) return false;
            if (_layoutKeyFocus) return true;
            if (_layoutHover) return true;
            var focused = _root.panel?.focusController?.focusedElement as VisualElement;
            if (focused == null) return false;
            if (focused == _layoutPreviewLarge || _layoutPreviewLarge.Contains(focused)) return true;
            return _component2DPanel != null && (_component2DPanel == focused || _component2DPanel.Contains(focused));
        }

        private void Apply3DCameraSettings()
        {
            if (_studioView == null) return;
            if (_circuit3DFovSlider != null)
            {
                _studioView.SetFieldOfView(_circuit3DFovSlider.value);
            }
            if (_circuit3DPerspectiveToggle != null)
            {
                _studioView.SetPerspective(_circuit3DPerspectiveToggle.value);
            }
        }

        private void RefreshTransformGizmo()
        {
            if (_studioView == null || _is2dView || !IsViewportEditActive())
            {
                _studioView?.HideTransformGizmo();
                return;
            }
            var mode = GetGizmoMode();
            if (mode == ComponentStudioView.GizmoMode.None)
            {
                _studioView.HideTransformGizmo();
                return;
            }
            if (!TryGetGizmoTarget(mode, out var target, out var worldPos, out var worldRot, out var bounds))
            {
                if (TryGetDraggedGizmoTarget(mode, out worldPos, out worldRot, out bounds))
                {
                    _studioView.SetTransformGizmo(worldPos, worldRot, bounds, mode, true);
                    return;
                }
                _studioView.HideTransformGizmo();
                return;
            }
            _studioView.SetTransformGizmo(worldPos, worldRot, bounds, mode, true);
        }

        private ComponentStudioView.GizmoMode GetGizmoMode()
        {
            return _viewportEditMode switch
            {
                ViewportEditMode.Move => ComponentStudioView.GizmoMode.Move,
                ViewportEditMode.Rotate => ComponentStudioView.GizmoMode.Rotate,
                ViewportEditMode.Scale => ComponentStudioView.GizmoMode.Scale,
                _ => ComponentStudioView.GizmoMode.None
            };
        }

        private bool TryGetGizmoTarget(ComponentStudioView.GizmoMode mode, out EditTarget target, out Vector3 worldPos, out Quaternion worldRot, out Bounds bounds)
        {
            target = new EditTarget();
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;
            bounds = new Bounds(Vector3.zero, Vector3.one * 0.1f);

            if (TryGetSelectedPart(out var part))
            {
                target = new EditTarget { Kind = EditTargetKind.Part, Id = part.name, Part = part };
                worldRot = part.rotation;
                if (_studioView.TryGetPartBoundsLocal(part, out bounds))
                {
                    worldPos = part.TransformPoint(bounds.center);
                    bounds.center = Vector3.zero;
                }
                else
                {
                    worldPos = part.position;
                    bounds = new Bounds(Vector3.zero, Vector3.one * 0.2f);
                }
                return true;
            }

            if (TryGetSelectedAnchor(out var anchorId, out var anchorLocal) && (mode == ComponentStudioView.GizmoMode.Move || mode == ComponentStudioView.GizmoMode.Scale))
            {
                target = new EditTarget { Kind = EditTargetKind.Anchor, Id = anchorId };
                if (_studioView.Root != null)
                {
                    worldPos = _studioView.Root.TransformPoint(anchorLocal);
                    worldRot = _studioView.Root.rotation;
                }
                bounds = new Bounds(Vector3.zero, Vector3.one * 0.1f);
                return true;
            }

            if (_studioView.TryGetModelBoundsLocal(out bounds))
            {
                target = new EditTarget { Kind = EditTargetKind.Component, Id = "Component" };
                if (_studioView.Root != null) worldRot = _studioView.Root.rotation;
                worldPos = _studioView.Root != null ? _studioView.Root.TransformPoint(bounds.center) : bounds.center;
                bounds.center = Vector3.zero;
                return true;
            }

            return false;
        }

        private bool TryGetDraggedGizmoTarget(ComponentStudioView.GizmoMode mode, out Vector3 worldPos, out Quaternion worldRot, out Bounds bounds)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;
            bounds = new Bounds(Vector3.zero, Vector3.one * 0.1f);
            if (!_isViewportEditDragging || _activeGizmoHandle == null) return false;

            if (_editTarget.Kind == EditTargetKind.Part && _editTarget.Part != null)
            {
                worldRot = _editTarget.Part.rotation;
                if (_studioView.TryGetPartBoundsLocal(_editTarget.Part, out bounds))
                {
                    worldPos = _editTarget.Part.TransformPoint(bounds.center);
                    bounds.center = Vector3.zero;
                }
                else
                {
                    worldPos = _editTarget.Part.position;
                    bounds = new Bounds(Vector3.zero, Vector3.one * 0.2f);
                }
                return true;
            }

            if (_editTarget.Kind == EditTargetKind.Anchor)
            {
                var local = _editStartLocal;
                if (TryGetSelectedAnchor(out _, out var anchorLocal))
                {
                    local = anchorLocal;
                }
                if (_studioView.Root != null)
                {
                    worldPos = _studioView.Root.TransformPoint(local);
                    worldRot = _studioView.Root.rotation;
                }
                bounds = new Bounds(Vector3.zero, Vector3.one * 0.1f);
                return true;
            }

            if (_editTarget.Kind == EditTargetKind.Component)
            {
                if (_studioView.Root != null) worldRot = _studioView.Root.rotation;
                if (_studioView.TryGetModelBoundsLocal(out var modelBounds))
                {
                    bounds = modelBounds;
                    worldPos = _studioView.Root != null ? _studioView.Root.TransformPoint(modelBounds.center) : modelBounds.center;
                    bounds.center = Vector3.zero;
                }
                else
                {
                    worldPos = _studioView.Root != null ? _studioView.Root.position : Vector3.zero;
                }
                return true;
            }

            return false;
        }

        private bool TryGetSelectedPart(out Transform part)
        {
            part = null;
            if (!IsPartEditEnabled()) return false;
            if (string.IsNullOrWhiteSpace(_selectedPartName)) return false;
            return TryGetPartTransform(_selectedPartName, out part);
        }

        private bool TryGetSelectedAnchor(out string id, out Vector3 localPos)
        {
            id = string.Empty;
            localPos = Vector3.zero;
            if (_selectedPinEntry == null) return false;
            id = _selectedPinEntry.userData as string ?? string.Empty;
            var xField = _selectedPinEntry.Q<TextField>("PinAnchorXField");
            var yField = _selectedPinEntry.Q<TextField>("PinAnchorYField");
            var zField = _selectedPinEntry.Q<TextField>("PinAnchorZField");
            if (xField == null || yField == null || zField == null) return false;
            localPos = new Vector3(ReadFloat(xField), ReadFloat(yField), ReadFloat(zField));
            return true;
        }

        private float ReadSelectedAnchorRadius()
        {
            if (_selectedPinEntry == null) return 0.006f;
            var radiusField = _selectedPinEntry.Q<TextField>("PinAnchorRadiusField");
            return ReadFloat(radiusField, 0.006f);
        }

        private void SetCenterView(bool show3d)
        {
            _is2dView = !show3d;
            _viewportKeyFocus = show3d;
            _layoutKeyFocus = !show3d;
            if (_component3DPanel != null)
            {
                _component3DPanel.style.display = show3d ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_component2DPanel != null)
            {
                _component2DPanel.style.display = show3d ? DisplayStyle.None : DisplayStyle.Flex;
            }
            SetTabActive(_centerTab3dBtn, show3d);
            SetTabActive(_centerTab2dBtn, !show3d);
            UpdateObjectsPanelForView();
            if (!show3d)
            {
                RefreshLayoutPreview();
                _studioView?.ClearAnchors();
                _studioView?.ClearEffects();
                if (_layoutPreviewLarge != null) _layoutPreviewLarge.Focus();
                else _layoutPreview?.Focus();
                return;
            }
            RefreshAnchorGizmos();
            RefreshEffectGizmos();
            UpdateViewCubeSurface();
            RefreshTransformGizmo();
            _viewport?.Focus();
        }

        private void UpdateObjectsPanelForView()
        {
            bool show2d = _is2dView;
            SetPanelVisible(_objectsLabelsSection, show2d);
            SetPanelVisible(_objectsPinsSection, show2d);
            SetPanelVisible(_objectsFxSection, !show2d);
            SetPanelVisible(_objectsAnchorsSection, !show2d);
        }

        private static void SetTabActive(Button tab, bool active)
        {
            if (tab == null) return;
            tab.EnableInClassList("active", active);
        }

        private void ResetLayoutView()
        {
            _layoutZoom = LayoutDefaultZoom;
            _layoutPanOffset = Vector2.zero;
            ClampLayoutPanOffset();
            RefreshLayoutPreview();
        }

        private void SetLayoutTool(LayoutTool tool)
        {
            if (tool == LayoutTool.Text)
            {
                tool = LayoutTool.Label;
            }
            _layoutTool = tool;
            switch (tool)
            {
                case LayoutTool.Pin:
                    _activeTool = ToolAction.Pin;
                    break;
                case LayoutTool.Label:
                    _activeTool = ToolAction.Label;
                    break;
                default:
                    _activeTool = ToolAction.Select;
                    break;
            }
            UpdateToolButtons();
        }

        private void ToggleLayoutGrid()
        {
            _layoutGridVisible = !_layoutGridVisible;
            if (_layoutGrid != null)
            {
                _layoutGrid.style.display = _layoutGridVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_layoutGridToggleBtn != null)
            {
                ToggleActiveClass(_layoutGridToggleBtn, _layoutGridVisible);
            }
        }

        private void StopLayoutInteractions()
        {
            ReleaseLayoutPointerCapture();
            _draggingPin = false;
            _draggingPinName = null;
            _draggingLabel = false;
            _draggingLabelEntry = null;
            _draggingShape = false;
            _draggingShapeId = null;
            _placingShape = false;
            _placingShapeId = null;
            _layoutPanning = false;
            _layoutPanPointerId = -1;
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

        private void ToggleAnchors()
        {
            SetAnchorVisible(!IsAnchorVisible());
            RefreshAnchorGizmos();
            QueueValidation();
        }

        private void ToggleAnchorPick()
        {
            SetAnchorPick(!IsAnchorPickEnabled());
            QueueValidation();
        }

        private bool IsAnchorPickEnabled()
        {
            return _anchorPickToggle != null ? _anchorPickToggle.value : _anchorPickMode;
        }

        private void SetAnchorPick(bool enabled)
        {
            _anchorPickMode = enabled;
            if (_anchorPickToggle != null) _anchorPickToggle.SetValueWithoutNotify(enabled);
        }

        private bool IsAnchorVisible()
        {
            return _anchorShowToggle != null ? _anchorShowToggle.value : _anchorVisible;
        }

        private void SetAnchorVisible(bool visible)
        {
            _anchorVisible = visible;
            if (_anchorShowToggle != null) _anchorShowToggle.SetValueWithoutNotify(visible);
            if (_viewportEditAnchorsToggle != null && _viewportEditAnchorsToggle.value != visible)
            {
                _viewportEditAnchorsToggle.SetValueWithoutNotify(visible);
            }
        }

        private void ApplyFollowSelection()
        {
            if (_studioView == null) return;
            if (_circuit3DFollowToggle == null || !_circuit3DFollowToggle.value)
            {
                _studioView.SetFollowTarget(null, false);
                return;
            }
            if (string.IsNullOrWhiteSpace(_selectedPartName) || !TryGetPartTransform(_selectedPartName, out var part))
            {
                _studioView.SetFollowTarget(null, false);
                return;
            }
            _studioView.SetFollowTarget(part, true);
        }

        private void TogglePartColors(bool enabled)
        {
            SetPartColorsEnabled(enabled, false);
        }

        private void SetPartColorsEnabled(bool enabled, bool updateToggle)
        {
            _partColorsEnabled = enabled;
            if (updateToggle && _viewportPartColorsToggle != null)
            {
                _viewportPartColorsToggle.SetValueWithoutNotify(enabled);
            }
            if (_studioView == null) return;
            if (!_partColorsEnabled)
            {
                ApplyStateOverridesToModel();
                return;
            }
            ApplyPartColorOverlay();
        }

        private void ApplyPartColorOverlay()
        {
            if (_studioView == null || _studioView.Parts == null) return;
            int index = 0;
            foreach (var part in _studioView.Parts)
            {
                if (part == null) continue;
                float hue = (index * 0.17f) % 1f;
                var color = Color.HSVToRGB(hue, 0.55f, 0.95f);
                _studioView.SetPartColor(part, color);
                index++;
            }
        }

        private void ToggleViewportTools()
        {
            SetViewportToolsCollapsed(!_viewportToolsCollapsed);
        }

        private void SetViewportToolsCollapsed(bool collapsed)
        {
            _viewportToolsCollapsed = collapsed;
            if (_viewportToolsBody != null)
            {
                _viewportToolsBody.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (_viewportTools != null)
            {
                _viewportTools.EnableInClassList("tools-collapsed", collapsed);
            }
            if (_viewportToolsToggleBtn != null)
            {
                _viewportToolsToggleBtn.text = collapsed ? "Viewport" : "Hide";
            }
        }

        private enum ViewportEditMode
        {
            None,
            Move,
            Rotate,
            Scale
        }

        private enum EditTargetKind
        {
            None,
            Anchor,
            Effect,
            Part,
            Component
        }

        private struct EditTarget
        {
            public EditTargetKind Kind;
            public string Id;
            public Transform Part;
        }

        private enum OutputFilter
        {
            All,
            Warnings,
            Errors
        }

        private enum LogLevel
        {
            Info,
            Warning,
            Error
        }

        private class StudioLogEntry
        {
            public LogLevel Level;
            public string Message;
            public string Source;
            public string Detail;
        }

        private enum ToolAction
        {
            Select,
            Anchor,
            Pin,
            Label,
            State,
            Material
        }

        private enum LayoutTool
        {
            Select,
            Pan,
            Rotate,
            Scale,
            Pin,
            Label,
            Box,
            Triangle,
            Line,
            Text,
            Circle
        }

        private enum LeftPanelTab
        {
            Hierarchy,
            Catalog,
            Basics,
            Type,
            Objects
        }

        private enum HelpOverlaySection
        {
            Shortcuts,
            About
        }

        private enum ConfirmOverlayAction
        {
            None,
            ReturnToWizard
        }

        private void ActivateTool(ToolAction action)
        {
            _activeTool = action;
            UpdateToolButtons();
            switch (action)
            {
                case ToolAction.Select:
                    SetAnchorPick(false);
                    break;
                case ToolAction.Anchor:
                    SetAnchorVisible(true);
                    SetAnchorPick(true);
                    RefreshAnchorGizmos();
                    break;
                case ToolAction.Pin:
                    {
                        var entry = AddPinRow(new PinLayoutPayload { name = GetNextPinName() });
                        if (entry != null)
                        {
                            SelectPinEntry(entry);
                        }
                        SetAnchorVisible(true);
                        SetAnchorPick(true);
                        RefreshAnchorGizmos();
                    }
                    break;
                case ToolAction.Label:
                    AddLabelRow(new LabelLayoutPayload { text = "{id}", x = 0.5f, y = 0.5f, size = 10, align = "center" });
                    break;
                case ToolAction.State:
                    _stateNameField?.Focus();
                    break;
                case ToolAction.Material:
                    OpenMaterialModal();
                    break;
            }
        }

        private void UpdateToolButtons()
        {
            SetToolButtonActive(_toolSelectBtn, _activeTool == ToolAction.Select);
            SetToolButtonActive(_toolAnchorBtn, _activeTool == ToolAction.Anchor);
            SetToolButtonActive(_toolPinBtn, _activeTool == ToolAction.Pin);
            SetToolButtonActive(_toolLabelBtn, _activeTool == ToolAction.Label);
            SetToolButtonActive(_toolStateBtn, _activeTool == ToolAction.State);
            SetToolButtonActive(_toolMaterialBtn, _activeTool == ToolAction.Material);
            SetToolButtonActive(_viewportPinBtn, _activeTool == ToolAction.Pin);
            SetToolButtonActive(_viewportLabelBtn, _activeTool == ToolAction.Label);
            SetToolButtonActive(_viewportMaterialBtn, _activeTool == ToolAction.Material);
            SetToolButtonActive(_viewportStateBtn, _activeTool == ToolAction.State);
            SetToolButtonActive(_layoutToolSelectBtn, _layoutTool == LayoutTool.Select);
            SetToolButtonActive(_layoutToolPanBtn, _layoutTool == LayoutTool.Pan);
            SetToolButtonActive(_layoutToolRotateBtn, _layoutTool == LayoutTool.Rotate);
            SetToolButtonActive(_layoutToolScaleBtn, _layoutTool == LayoutTool.Scale);
            SetToolButtonActive(_layoutToolPinBtn, _layoutTool == LayoutTool.Pin);
            SetToolButtonActive(_layoutToolLabelBtn, _layoutTool == LayoutTool.Label);
            SetToolButtonActive(_layoutToolBoxBtn, _layoutTool == LayoutTool.Box);
            SetToolButtonActive(_layoutToolTriangleBtn, _layoutTool == LayoutTool.Triangle);
            SetToolButtonActive(_layoutToolLineBtn, _layoutTool == LayoutTool.Line);
            SetToolButtonActive(_layoutToolTextBtn, _layoutTool == LayoutTool.Text);
            SetToolButtonActive(_layoutToolCircleBtn, _layoutTool == LayoutTool.Circle);
            ToggleActiveClass(_layoutGridToggleBtn, _layoutGridVisible);
        }

        private void SetViewportEditMode(ViewportEditMode mode)
        {
            if (_viewportEditMode == mode)
            {
                _viewportEditMode = ViewportEditMode.None;
            }
            else
            {
                _viewportEditMode = mode;
            }
            if (_isViewportEditDragging)
            {
                EndViewportEdit();
            }
            UpdateViewportEditButtons();
            RefreshTransformGizmo();
        }

        private void UpdateViewportEditButtons()
        {
            SetToolButtonActive(_viewportEditMoveBtn, _viewportEditMode == ViewportEditMode.Move);
            SetToolButtonActive(_viewportEditRotateBtn, _viewportEditMode == ViewportEditMode.Rotate);
            SetToolButtonActive(_viewportEditScaleBtn, _viewportEditMode == ViewportEditMode.Scale);
        }

        private void SetViewportEditPanelVisible(bool visible)
        {
            if (_viewportEditPanel == null) return;
            _viewportEditPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void SetToolButtonActive(Button button, bool active)
        {
            if (button == null) return;
            if (active)
            {
                button.AddToClassList("active");
            }
            else
            {
                button.RemoveFromClassList("active");
            }
        }

        private void TryPickAnchor(Vector2 pointer)
        {
            if (_viewport == null || _studioView == null || _selectedPinEntry == null) return;
            var rect = _viewport.worldBound;
            if (rect.width <= 0f || rect.height <= 0f) return;
            var local = pointer - rect.position;
            var viewportPoint = new Vector2(local.x / rect.width, 1f - (local.y / rect.height));

            if (_studioView.TryPickLocal(viewportPoint, out var localPos))
            {
                SetEntryValue(_selectedPinEntry, "PinAnchorXField", FormatFloat(localPos.x));
                SetEntryValue(_selectedPinEntry, "PinAnchorYField", FormatFloat(localPos.y));
                SetEntryValue(_selectedPinEntry, "PinAnchorZField", FormatFloat(localPos.z));
                if (string.IsNullOrWhiteSpace(GetEntryValue(_selectedPinEntry, "PinAnchorRadiusField")))
                {
                    SetEntryValue(_selectedPinEntry, "PinAnchorRadiusField", "0.006");
                }
                RefreshAnchorGizmos();
            }
        }

        private bool TryGetViewportPoint(Vector2 pointer, out Vector2 viewportPoint)
        {
            viewportPoint = Vector2.zero;
            if (_viewport == null) return false;
            var rect = _viewport.worldBound;
            if (rect.width <= 0f || rect.height <= 0f) return false;
            var local = pointer - rect.position;
            viewportPoint = new Vector2(local.x / rect.width, 1f - (local.y / rect.height));
            return true;
        }

        private bool IsViewportEditActive()
        {
            if (_viewportEditMode == ViewportEditMode.None) return false;
            return (_viewportEditAnchorsToggle != null && _viewportEditAnchorsToggle.value) ||
                   (_viewportEditEffectsToggle != null && _viewportEditEffectsToggle.value) ||
                   (_viewportEditPartsToggle != null && _viewportEditPartsToggle.value) ||
                   (_viewportEditStatesToggle != null && _viewportEditStatesToggle.value);
        }

        private bool IsPartEditEnabled()
        {
            if (_viewportEditPartsToggle != null && _viewportEditPartsToggle.value) return true;
            return _viewportEditStatesToggle != null && _viewportEditStatesToggle.value && !string.IsNullOrWhiteSpace(_selectedStateId);
        }

        private bool BeginGizmoDrag(ComponentStudioView.GizmoHandle handle, Vector2 pointer, Vector2 viewportPoint)
        {
            if (handle == null || _studioView == null) return false;
            if (!TryGetGizmoTarget(GetGizmoMode(), out var target, out var worldPos, out _, out _)) return false;

            _activeGizmoHandle = handle;
            _editTarget = target;
            _editStartPointer = pointer;
            _isViewportEditDragging = true;
            _gizmoDragOriginWorld = worldPos;
            _rotateDragActive = false;
            _rotatePrevAngle = 0f;
            _rotateAccumAngle = 0f;
            _rotateStartAngle = 0f;
            _studioView.SetRotateRebuildLock(false);

            if (_editTarget.Kind == EditTargetKind.Part && _editTarget.Part != null)
            {
                _editStartLocal = _editTarget.Part.localPosition;
                _editStartRotation = _editTarget.Part.localEulerAngles;
                _editStartScale = _editTarget.Part.localScale;
                SetEditPlaneForWorld(_editTarget.Part.position);
                return InitializeRotateDrag(handle, viewportPoint);
            }

            if (_editTarget.Kind == EditTargetKind.Anchor && TryGetSelectedAnchor(out _, out var anchorLocal))
            {
                _editStartLocal = anchorLocal;
                float radius = ReadSelectedAnchorRadius();
                _editStartScale = Vector3.one * Mathf.Max(radius, 0.006f) * 2f;
                SetEditPlaneForLocal(_editStartLocal);
                return InitializeRotateDrag(handle, viewportPoint);
            }

            if (_editTarget.Kind == EditTargetKind.Component)
            {
                _editStartLocal = _modelOffset;
                _editStartRotation = ReadVector3Field("ComponentEulerXField", "ComponentEulerYField", "ComponentEulerZField");
                _editStartScale = ReadScaleVector3Field("ComponentScaleXField", "ComponentScaleYField", "ComponentScaleZField");
                SetEditPlaneForWorld(worldPos);
                return InitializeRotateDrag(handle, viewportPoint);
            }

            _activeGizmoHandle = null;
            _isViewportEditDragging = false;
            return false;
        }

        private bool InitializeRotateDrag(ComponentStudioView.GizmoHandle handle, Vector2 viewportPoint)
        {
            if (handle == null || handle.Kind != ComponentStudioView.GizmoHandleKind.RotateAxis) return true;
            if (_studioView == null) return true;

            _rotateAxisWorld = handle.transform.forward;
            var plane = new Plane(_rotateAxisWorld, _gizmoDragOriginWorld);
            if (_studioView.TryGetViewportRay(viewportPoint, out var ray) && plane.Raycast(ray, out var enter))
            {
                var point = ray.GetPoint(enter);
                var dir = point - _gizmoDragOriginWorld;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    _rotateStartDirWorld = dir.normalized;
                    var localDir = handle.transform.InverseTransformDirection(_rotateStartDirWorld);
                    _rotateStartAngle = Mathf.Atan2(localDir.y, localDir.x) * Mathf.Rad2Deg;
                    _rotateDragActive = true;
                    _studioView.SetRotateRebuildLock(true);
                    _studioView.SetRotateArc(handle.Axis, 0f, _rotateStartAngle);
                }
            }
            return true;
        }

        private void BeginViewportEdit(ComponentStudioView.EditMarker marker, Vector2 pointer)
        {
            if (marker == null || _studioView == null) return;
            var kind = marker.Kind == ComponentStudioView.EditMarkerKind.Anchor ? EditTargetKind.Anchor : EditTargetKind.Effect;
            if (kind == EditTargetKind.Effect && string.Equals(marker.Id, "Component", StringComparison.OrdinalIgnoreCase))
            {
                kind = EditTargetKind.Component;
            }
            _editTarget = new EditTarget { Kind = kind, Id = marker.Id };
            _editStartLocal = marker.transform.localPosition;
            _editStartScale = marker.transform.localScale;
            _editStartPointer = pointer;
            _isViewportEditDragging = true;
            SetEditPlaneForLocal(_editStartLocal);
            if (_editTarget.Kind == EditTargetKind.Anchor)
            {
                var entry = FindPinEntryByName(_editTarget.Id);
                if (entry != null) SelectPinEntry(entry);
            }
        }

        private void BeginViewportEdit(Transform part, Vector2 pointer)
        {
            if (part == null || _studioView == null) return;
            _editTarget = new EditTarget { Kind = EditTargetKind.Part, Id = part.name, Part = part };
            _editStartLocal = part.localPosition;
            _editStartRotation = part.localEulerAngles;
            _editStartScale = part.localScale;
            _editStartPointer = pointer;
            _isViewportEditDragging = true;
            SetEditPlaneForWorld(part.position);
        }

        private void SetEditPlaneForLocal(Vector3 localPos)
        {
            if (_studioView == null || _studioView.Root == null || _studioView.ViewCamera == null) return;
            Vector3 worldPos = _studioView.Root.TransformPoint(localPos);
            _editPlane = new Plane(_studioView.ViewCamera.transform.forward, worldPos);
        }

        private void SetEditPlaneForWorld(Vector3 worldPos)
        {
            if (_studioView == null || _studioView.ViewCamera == null) return;
            _editPlane = new Plane(_studioView.ViewCamera.transform.forward, worldPos);
        }

        private const float RotationDragSpeed = 0.08f;

        private void UpdateViewportEdit(Vector2 viewportPoint, Vector2 pointerDelta)
        {
            if (!_isViewportEditDragging || _studioView == null) return;
            if (_editTarget.Kind == EditTargetKind.None) return;

            if (_activeGizmoHandle != null)
            {
                UpdateViewportEditWithGizmo(viewportPoint, pointerDelta);
                return;
            }

            if (_viewportEditMode == ViewportEditMode.Move)
            {
                if (_studioView.TryGetViewportRay(viewportPoint, out var ray) && _editPlane.Raycast(ray, out var enter))
                {
                    var worldPos = ray.GetPoint(enter);
                    Vector3 localPos = _editStartLocal;
                    if (_editTarget.Kind == EditTargetKind.Part && _editTarget.Part != null)
                    {
                        var parent = _editTarget.Part.parent;
                        localPos = parent != null ? parent.InverseTransformPoint(worldPos) : worldPos;
                        localPos = ApplySnap(localPos);
                        ApplyPartPosition(_editTarget.Part, localPos);
                        return;
                    }
                    if (_studioView.Root != null)
                    {
                        localPos = _studioView.Root.InverseTransformPoint(worldPos);
                    }
                    localPos = ApplySnap(localPos);
                    if (_editTarget.Kind == EditTargetKind.Anchor)
                    {
                        ApplyAnchorPosition(_editTarget.Id, localPos);
                    }
                    else if (_editTarget.Kind == EditTargetKind.Effect)
                    {
                        ApplyEffectOffset(_editTarget.Id, localPos);
                    }
                }
                return;
            }

            if (_editTarget.Kind == EditTargetKind.Part && _editTarget.Part != null)
            {
                if (_viewportEditMode == ViewportEditMode.Rotate)
                {
                    var rotation = _editStartRotation;
                    rotation.y += pointerDelta.x * RotationDragSpeed;
                    rotation.x -= pointerDelta.y * RotationDragSpeed;
                    ApplyPartRotation(_editTarget.Part, rotation);
                    return;
                }
                if (_viewportEditMode == ViewportEditMode.Scale)
                {
                    float scaleDelta = 1f + pointerDelta.x * 0.003f;
                    var scale = _editStartScale * Mathf.Clamp(scaleDelta, 0.1f, 10f);
                    ApplyPartScale(_editTarget.Part, ApplySnap(scale));
                    return;
                }
            }

            if (_editTarget.Kind == EditTargetKind.Component)
            {
                if (_viewportEditMode == ViewportEditMode.Rotate)
                {
                    var euler = ReadVector3Field("ComponentEulerXField", "ComponentEulerYField", "ComponentEulerZField");
                    euler.y += pointerDelta.x * RotationDragSpeed;
                    euler.x -= pointerDelta.y * RotationDragSpeed;
                    ApplyComponentEuler(euler);
                    return;
                }
                if (_viewportEditMode == ViewportEditMode.Scale)
                {
                    var scale = ReadScaleVector3Field("ComponentScaleXField", "ComponentScaleYField", "ComponentScaleZField");
                    float scaleDelta = 1f + pointerDelta.x * 0.003f;
                    scale *= Mathf.Clamp(scaleDelta, 0.1f, 10f);
                    ApplyComponentScale(ApplySnap(scale));
                    return;
                }
            }

            if (_editTarget.Kind == EditTargetKind.Anchor && _viewportEditMode == ViewportEditMode.Scale)
            {
                float radius = _editStartScale.x * 0.5f + pointerDelta.x * 0.0005f;
                ApplyAnchorRadius(_editTarget.Id, radius);
            }
        }

        private void UpdateViewportEditWithGizmo(Vector2 viewportPoint, Vector2 pointerDelta)
        {
            if (_activeGizmoHandle == null || _studioView == null) return;
            var handle = _activeGizmoHandle;

            Quaternion basisRotation = Quaternion.identity;
            if (_editTarget.Kind == EditTargetKind.Part && _editTarget.Part != null)
            {
                basisRotation = _editTarget.Part.rotation;
            }
            else if (_studioView.Root != null)
            {
                basisRotation = _studioView.Root.rotation;
            }

            if (handle.Kind == ComponentStudioView.GizmoHandleKind.MoveAxis)
            {
                var axisDir = GetAxisDirection(handle.Axis, basisRotation);
                if (_studioView.TryGetViewportRay(viewportPoint, out var ray) && _editPlane.Raycast(ray, out var enter))
                {
                    var worldPos = ray.GetPoint(enter);
                    float distance = Vector3.Dot(worldPos - _gizmoDragOriginWorld, axisDir);
                    var constrainedWorld = _gizmoDragOriginWorld + axisDir * distance;
                    Vector3 localPos = _editStartLocal;

                    if (_editTarget.Kind == EditTargetKind.Part && _editTarget.Part != null)
                    {
                        var parent = _editTarget.Part.parent;
                        localPos = parent != null ? parent.InverseTransformPoint(constrainedWorld) : constrainedWorld;
                        localPos = ApplySnap(localPos);
                        ApplyPartPosition(_editTarget.Part, localPos);
                    }
                    else if (_studioView.Root != null)
                    {
                        localPos = _studioView.Root.InverseTransformPoint(constrainedWorld);
                        localPos = ApplySnap(localPos);
                        if (_editTarget.Kind == EditTargetKind.Anchor)
                        {
                            ApplyAnchorPosition(_editTarget.Id, localPos);
                        }
                        else if (_editTarget.Kind == EditTargetKind.Effect)
                        {
                            ApplyEffectOffset(_editTarget.Id, localPos);
                        }
                        else if (_editTarget.Kind == EditTargetKind.Component)
                        {
                            ApplyComponentOffset(localPos);
                        }
                    }
                    RefreshTransformGizmo();
                }
                return;
            }

            if (handle.Kind == ComponentStudioView.GizmoHandleKind.RotateAxis)
            {
                if (_editTarget.Kind != EditTargetKind.Part && _editTarget.Kind != EditTargetKind.Component) return;
                float angle = GetRotateDragAngle(viewportPoint, pointerDelta, handle.Axis);
                if (_editTarget.Kind == EditTargetKind.Part && _editTarget.Part != null)
                {
                    var startRotation = Quaternion.Euler(_editStartRotation);
                    var parent = _editTarget.Part.parent;
                    var localAxis = parent != null
                        ? parent.InverseTransformDirection(_rotateAxisWorld).normalized
                        : _rotateAxisWorld.normalized;
                    var rotated = Quaternion.AngleAxis(angle, localAxis) * startRotation;
                    ApplyPartRotation(_editTarget.Part, rotated.eulerAngles);
                }
                else if (_editTarget.Kind == EditTargetKind.Component)
                {
                    var startRotation = Quaternion.Euler(_editStartRotation);
                    var localAxis = _studioView.Root != null
                        ? _studioView.Root.InverseTransformDirection(_rotateAxisWorld).normalized
                        : _rotateAxisWorld.normalized;
                    var rotated = Quaternion.AngleAxis(angle, localAxis) * startRotation;
                    ApplyComponentEuler(rotated.eulerAngles);
                }
                RefreshTransformGizmo();
                _studioView.SetRotateArc(handle.Axis, _rotateAccumAngle, _rotateStartAngle);
                return;
            }

            if (handle.Kind == ComponentStudioView.GizmoHandleKind.ScaleAxis)
            {
                if (_editTarget.Kind != EditTargetKind.Part && _editTarget.Kind != EditTargetKind.Component) return;
                float scaleDelta = 1f + pointerDelta.x * 0.0012f;
                scaleDelta = Mathf.Clamp(scaleDelta, 0.25f, 4f);
                var scale = _editStartScale;
                if (handle.Axis == ComponentStudioView.GizmoAxis.X) scale.x = _editStartScale.x * scaleDelta;
                if (handle.Axis == ComponentStudioView.GizmoAxis.Y) scale.y = _editStartScale.y * scaleDelta;
                if (handle.Axis == ComponentStudioView.GizmoAxis.Z) scale.z = _editStartScale.z * scaleDelta;
                scale = ApplySnap(scale);
                if (_editTarget.Kind == EditTargetKind.Part && _editTarget.Part != null)
                {
                    ApplyPartScale(_editTarget.Part, scale);
                }
                else if (_editTarget.Kind == EditTargetKind.Component)
                {
                    ApplyComponentScale(scale);
                }
                RefreshTransformGizmo();
                return;
            }

            if (handle.Kind == ComponentStudioView.GizmoHandleKind.ScaleUniform)
            {
                float scaleDelta = 1f + pointerDelta.x * 0.0012f;
                scaleDelta = Mathf.Clamp(scaleDelta, 0.25f, 4f);
                if (_editTarget.Kind == EditTargetKind.Anchor)
                {
                    float radius = _editStartScale.x * 0.5f * scaleDelta;
                    ApplyAnchorRadius(_editTarget.Id, radius);
                }
                else if (_editTarget.Kind == EditTargetKind.Part && _editTarget.Part != null)
                {
                    var scale = _editStartScale * scaleDelta;
                    ApplyPartScale(_editTarget.Part, ApplySnap(scale));
                }
                else if (_editTarget.Kind == EditTargetKind.Component)
                {
                    var scale = _editStartScale * scaleDelta;
                    ApplyComponentScale(ApplySnap(scale));
                }
                RefreshTransformGizmo();
            }
        }

        private float GetRotateDragAngle(Vector2 viewportPoint, Vector2 pointerDelta, ComponentStudioView.GizmoAxis axis)
        {
            if (!_rotateDragActive || _studioView == null)
            {
                float fallback = (axis == ComponentStudioView.GizmoAxis.X ? -pointerDelta.y : pointerDelta.x) * RotationDragSpeed;
                _rotateAccumAngle += fallback;
                return _rotateAccumAngle;
            }

            var plane = new Plane(_rotateAxisWorld, _gizmoDragOriginWorld);
            if (_studioView.TryGetViewportRay(viewportPoint, out var ray) && plane.Raycast(ray, out var enter))
            {
                var point = ray.GetPoint(enter);
                var dir = point - _gizmoDragOriginWorld;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    var currentDir = dir.normalized;
                    float currentAngle = Vector3.SignedAngle(_rotateStartDirWorld, currentDir, _rotateAxisWorld);
                    float delta = currentAngle - _rotatePrevAngle;
                    if (delta > 180f) delta -= 360f;
                    if (delta < -180f) delta += 360f;
                    _rotateAccumAngle += delta;
                    _rotatePrevAngle = currentAngle;
                }
            }
            return _rotateAccumAngle;
        }

        private static Vector3 GetAxisDirection(ComponentStudioView.GizmoAxis axis, Quaternion rotation)
        {
            Vector3 baseAxis = Vector3.right;
            if (axis == ComponentStudioView.GizmoAxis.Y) baseAxis = Vector3.up;
            if (axis == ComponentStudioView.GizmoAxis.Z) baseAxis = Vector3.forward;
            var dir = rotation * baseAxis;
            return dir.normalized;
        }

        private void EndViewportEdit()
        {
            _isViewportEditDragging = false;
            _editTarget = new EditTarget();
            _activeGizmoHandle = null;
            _rotateDragActive = false;
            _rotatePrevAngle = 0f;
            _rotateAccumAngle = 0f;
            _rotateStartAngle = 0f;
            if (_studioView != null)
            {
                _studioView.SetRotateRebuildLock(false);
                _studioView.ClearRotateArcs();
            }
            RefreshTransformGizmo();
        }

        private void SelectPartByName(string partName)
        {
            if (_partsContainer == null || string.IsNullOrWhiteSpace(partName)) return;
            foreach (var child in _partsContainer.Children())
            {
                if (!(child is VisualElement entry)) continue;
                if (!string.Equals(entry.userData as string, partName, StringComparison.OrdinalIgnoreCase)) continue;
                SelectPartEntry(entry);
                return;
            }
        }

        private Vector3 ApplySnap(Vector3 value)
        {
            if (_viewportEditSnapToggle == null || !_viewportEditSnapToggle.value) return value;
            float step = GetSnapStep();
            if (step <= 0f) return value;
            value.x = Mathf.Round(value.x / step) * step;
            value.y = Mathf.Round(value.y / step) * step;
            value.z = Mathf.Round(value.z / step) * step;
            return value;
        }

        private float GetSnapStep()
        {
            if (_viewportEditSnapField == null) return 0f;
            if (float.TryParse(_viewportEditSnapField.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var step))
            {
                return Mathf.Max(0f, step);
            }
            return 0f;
        }

        private void ApplyAnchorPosition(string id, Vector3 local)
        {
            var entry = FindPinEntryByName(id);
            if (entry == null) return;
            SetEntryValue(entry, "PinAnchorXField", FormatFloat(local.x));
            SetEntryValue(entry, "PinAnchorYField", FormatFloat(local.y));
            SetEntryValue(entry, "PinAnchorZField", FormatFloat(local.z));
            RefreshAnchorGizmos();
            QueueValidation();
        }

        private void ApplyAnchorRadius(string id, float radius)
        {
            var entry = FindPinEntryByName(id);
            if (entry == null) return;
            float clamped = Mathf.Max(0.001f, radius);
            SetEntryValue(entry, "PinAnchorRadiusField", FormatFloat(clamped));
            RefreshAnchorGizmos();
            QueueValidation();
        }

        private void ApplyEffectOffset(string id, Vector3 local)
        {
            if (string.Equals(id, "Component", StringComparison.OrdinalIgnoreCase)) return;
            switch (id)
            {
                case "LabelOffset":
                    SetVector3Fields("ComponentLabelOffsetXField", "ComponentLabelOffsetYField", "ComponentLabelOffsetZField", local);
                    break;
                case "LedGlowOffset":
                    SetVector3Fields("ComponentLedGlowOffsetXField", "ComponentLedGlowOffsetYField", "ComponentLedGlowOffsetZField", local);
                    break;
                case "HeatFxOffset":
                    SetVector3Fields("ComponentHeatFxOffsetXField", "ComponentHeatFxOffsetYField", "ComponentHeatFxOffsetZField", local);
                    break;
                case "SparkFxOffset":
                    SetVector3Fields("ComponentSparkFxOffsetXField", "ComponentSparkFxOffsetYField", "ComponentSparkFxOffsetZField", local);
                    break;
                case "ErrorFxOffset":
                    SetVector3Fields("ComponentErrorFxOffsetXField", "ComponentErrorFxOffsetYField", "ComponentErrorFxOffsetZField", local);
                    break;
                case "SmokeOffset":
                    SetVector3Fields("ComponentSmokeOffsetXField", "ComponentSmokeOffsetYField", "ComponentSmokeOffsetZField", local);
                    break;
                case "UsbOffset":
                    SetVector3Fields("ComponentUsbOffsetXField", "ComponentUsbOffsetYField", "ComponentUsbOffsetZField", local);
                    break;
            }
            RefreshEffectGizmos();
            QueueValidation();
        }

        private void ApplyComponentEuler(Vector3 euler)
        {
            SetVector3Fields("ComponentEulerXField", "ComponentEulerYField", "ComponentEulerZField", euler);
            _studioView?.ApplyModelTuning(euler, ReadScaleVector3Field("ComponentScaleXField", "ComponentScaleYField", "ComponentScaleZField"), _modelOffset);
            SyncLayoutSizeFromModelBounds();
            QueueValidation();
        }

        private void ApplyComponentScale(Vector3 scale)
        {
            var clamped = new Vector3(
                Mathf.Max(0.001f, scale.x),
                Mathf.Max(0.001f, scale.y),
                Mathf.Max(0.001f, scale.z));
            SetVector3Fields("ComponentScaleXField", "ComponentScaleYField", "ComponentScaleZField", clamped);
            _studioView?.ApplyModelTuning(ReadVector3Field("ComponentEulerXField", "ComponentEulerYField", "ComponentEulerZField"), clamped, _modelOffset);
            SyncLayoutSizeFromModelBounds();
            QueueValidation();
        }

        private void ApplyComponentOffset(Vector3 localOffset)
        {
            _modelOffset = localOffset;
            _studioView?.ApplyModelTuning(
                ReadVector3Field("ComponentEulerXField", "ComponentEulerYField", "ComponentEulerZField"),
                ReadScaleVector3Field("ComponentScaleXField", "ComponentScaleYField", "ComponentScaleZField"),
                _modelOffset);
            QueueValidation();
        }

        private void ResetTransformSelection()
        {
            bool reset = false;
            if (!string.IsNullOrWhiteSpace(_selectedPartName) && TryGetPartTransform(_selectedPartName, out var part) && part != null)
            {
                if (_partBaseTransforms.TryGetValue(part.name, out var snapshot))
                {
                    var overrideData = GetOrCreateActivePartOverride(part);
                    overrideData.Position = snapshot.Position;
                    overrideData.Rotation = snapshot.Rotation;
                    overrideData.Scale = snapshot.Scale;
                    ApplyPartOverrideToModel(overrideData);
                    UpdatePartFields(overrideData);
                    reset = true;
                }
            }

            if (!reset)
            {
                ApplyComponentEuler(Vector3.zero);
                ApplyComponentScale(Vector3.one);
                ApplyComponentOffset(Vector3.zero);
                reset = true;
            }

            RefreshTransformGizmo();
            SetStatus(reset ? "Transform reset." : "Nothing to reset.", false);
        }

        private void ApplyPartPosition(Transform part, Vector3 local)
        {
            if (part == null) return;
            var overrideData = GetOrCreateActivePartOverride(part);
            overrideData.Position = local;
            _studioView.SetPartTransform(part, overrideData.Position, overrideData.Rotation, overrideData.Scale);
            UpdatePartFields(overrideData);
            QueueValidation();
        }

        private void ApplyPartRotation(Transform part, Vector3 rotation)
        {
            if (part == null) return;
            var overrideData = GetOrCreateActivePartOverride(part);
            overrideData.Rotation = rotation;
            _studioView.SetPartTransform(part, overrideData.Position, overrideData.Rotation, overrideData.Scale);
            UpdatePartFields(overrideData);
            QueueValidation();
        }

        private void ApplyPartScale(Transform part, Vector3 scale)
        {
            if (part == null) return;
            var overrideData = GetOrCreateActivePartOverride(part);
            overrideData.Scale = new Vector3(
                Mathf.Max(0.001f, scale.x),
                Mathf.Max(0.001f, scale.y),
                Mathf.Max(0.001f, scale.z));
            _studioView.SetPartTransform(part, overrideData.Position, overrideData.Rotation, overrideData.Scale);
            UpdatePartFields(overrideData);
            QueueValidation();
        }

        private void UpdatePartFields(PartOverride overrideData)
        {
            if (overrideData == null) return;
            SetField(_partPosXField, FormatFloat(overrideData.Position.x));
            SetField(_partPosYField, FormatFloat(overrideData.Position.y));
            SetField(_partPosZField, FormatFloat(overrideData.Position.z));
            SetField(_partRotXField, FormatFloat(overrideData.Rotation.x));
            SetField(_partRotYField, FormatFloat(overrideData.Rotation.y));
            SetField(_partRotZField, FormatFloat(overrideData.Rotation.z));
            SetField(_partScaleXField, FormatFloat(overrideData.Scale.x));
            SetField(_partScaleYField, FormatFloat(overrideData.Scale.y));
            SetField(_partScaleZField, FormatFloat(overrideData.Scale.z));
        }

        private VisualElement FindPinEntryByName(string name)
        {
            if (_pinsContainer == null || string.IsNullOrWhiteSpace(name)) return null;
            foreach (var entry in _pinsContainer.Children())
            {
                var nameField = entry.Q<TextField>("PinNameField");
                if (nameField == null) continue;
                if (string.Equals(nameField.value?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
            return null;
        }

        private void SetVector3Fields(string xField, string yField, string zField, Vector3 value)
        {
            SetField(xField, FormatFloat(value.x));
            SetField(yField, FormatFloat(value.y));
            SetField(zField, FormatFloat(value.z));
        }

        private ComponentDefinitionPayload BuildPayloadFromEditor()
        {
            var payload = new ComponentDefinitionPayload
            {
                name = GetField("ComponentNameField")?.value?.Trim() ?? string.Empty,
                id = SanitizeComponentId(GetField("ComponentIdField")?.value),
                type = GetTypeValue(),
                description = GetField("ComponentDescriptionField")?.value ?? string.Empty,
                physicsScript = GetField("ComponentPhysicsScriptField")?.value ?? string.Empty,
                symbol = GetField("ComponentSymbolField")?.value ?? string.Empty,
                symbolFile = GetTextValue("ComponentSymbolFileField").Trim(),
                iconChar = GetField("ComponentIconField")?.value ?? string.Empty,
                order = ReadInt(GetField("ComponentOrderField"), 1000),
                size2D = new Vector2(
                    ReadFloat(GetField("ComponentSizeXField"), CircuitLayoutSizing.DefaultComponentWidth),
                    ReadFloat(GetField("ComponentSizeYField"), CircuitLayoutSizing.DefaultComponentHeight))
            };

            var pinLayout = CollectPinLayout();
            payload.pinLayout = pinLayout.ToArray();
            payload.pins = pinLayout.Select(p => p.name).Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            payload.labels = CollectLabelLayout().ToArray();
            payload.shapes = _shapeLayout.Select(shape => shape == null ? null : new ShapeLayoutPayload
            {
                id = shape.id,
                type = shape.type,
                x = shape.x,
                y = shape.y,
                width = shape.width,
                height = shape.height,
                text = shape.text,
                lineFlip = shape.lineFlip,
                rotation = shape.rotation,
                fontSize = shape.fontSize
            }).Where(shape => shape != null).ToArray();
            payload.specs = CollectKeyValues(_specsContainer);
            payload.defaults = CollectKeyValues(_defaultsContainer);
            payload.fx = CollectFxPayloads();
            payload.tuning = BuildTuningFromEditor();
            payload.parts = _partOverrides.Values.Select(p => p.ToPayload()).ToArray();
            payload.states = _stateOverrides.Values.Select(s => s.ToPayload()).ToArray();
            return payload;
        }

        private struct EditorSnapshot
        {
            public string PayloadJson;
            public string ModelSourcePath;
        }

        private struct PartTransformSnapshot
        {
            public Vector3 Position;
            public Vector3 Rotation;
            public Vector3 Scale;
        }

        private ComponentTuningPayload BuildTuningFromEditor()
        {
            var tuning = new ComponentTuningPayload();
            bool hasAny = false;

            float ex = 0f;
            float ey = 0f;
            float ez = 0f;
            if (TryReadFloat("ComponentEulerXField", out ex) ||
                TryReadFloat("ComponentEulerYField", out ey) ||
                TryReadFloat("ComponentEulerZField", out ez))
            {
                tuning.Euler = new Vector3(ex, ey, ez);
                hasAny = true;
            }

            float sx = 0f;
            float sy = 0f;
            float sz = 0f;
            if (TryReadFloat("ComponentScaleXField", out sx) ||
                TryReadFloat("ComponentScaleYField", out sy) ||
                TryReadFloat("ComponentScaleZField", out sz))
            {
                tuning.Scale = new Vector3(sx, sy, sz);
                hasAny = true;
            }

            float r = 0f;
            float g = 0f;
            float b = 0f;
            float a = 1f;
            bool hasColor = false;
            if (TryReadFloat("ComponentLedColorRField", out r)) hasColor = true;
            if (TryReadFloat("ComponentLedColorGField", out g)) hasColor = true;
            if (TryReadFloat("ComponentLedColorBField", out b)) hasColor = true;
            if (TryReadFloat("ComponentLedColorAField", out a)) hasColor = true;
            if (hasColor)
            {
                tuning.UseLedColor = true;
                tuning.LedColor = new Color(r, g, b, a);
                hasAny = true;
            }

            if (TryReadFloat("ComponentLedGlowRangeField", out var ledRange)) { tuning.LedGlowRange = ledRange; hasAny = true; }
            if (TryReadFloat("ComponentLedGlowIntensityField", out var ledIntensity)) { tuning.LedGlowIntensity = ledIntensity; hasAny = true; }
            if (TryReadFloat("ComponentLedBlowCurrentField", out var ledCurrent)) { tuning.LedBlowCurrent = ledCurrent; hasAny = true; }
            if (TryReadFloat("ComponentLedBlowTempField", out var ledTemp)) { tuning.LedBlowTemp = ledTemp; hasAny = true; }
            if (TryReadFloat("ComponentResistorSmokeStartField", out var smokeStart)) { tuning.ResistorSmokeStartTemp = smokeStart; hasAny = true; }
            if (TryReadFloat("ComponentResistorSmokeFullField", out var smokeFull)) { tuning.ResistorSmokeFullTemp = smokeFull; hasAny = true; }
            if (TryReadFloat("ComponentResistorHotStartField", out var hotStart)) { tuning.ResistorHotStartTemp = hotStart; hasAny = true; }
            if (TryReadFloat("ComponentResistorHotFullField", out var hotFull)) { tuning.ResistorHotFullTemp = hotFull; hasAny = true; }
            if (TryReadFloat("ComponentErrorFxIntervalField", out var errorInterval)) { tuning.ErrorFxInterval = errorInterval; hasAny = true; }

            float lx = 0f;
            float ly = 0f;
            float lz = 0f;
            if (TryReadFloat("ComponentLabelOffsetXField", out lx) ||
                TryReadFloat("ComponentLabelOffsetYField", out ly) ||
                TryReadFloat("ComponentLabelOffsetZField", out lz))
            {
                tuning.LabelOffset = new Vector3(lx, ly, lz);
                hasAny = true;
            }

            float lgx = 0f;
            float lgy = 0f;
            float lgz = 0f;
            if (TryReadFloat("ComponentLedGlowOffsetXField", out lgx) ||
                TryReadFloat("ComponentLedGlowOffsetYField", out lgy) ||
                TryReadFloat("ComponentLedGlowOffsetZField", out lgz))
            {
                tuning.LedGlowOffset = new Vector3(lgx, lgy, lgz);
                hasAny = true;
            }

            float hx = 0f;
            float hy = 0f;
            float hz = 0f;
            if (TryReadFloat("ComponentHeatFxOffsetXField", out hx) ||
                TryReadFloat("ComponentHeatFxOffsetYField", out hy) ||
                TryReadFloat("ComponentHeatFxOffsetZField", out hz))
            {
                tuning.HeatFxOffset = new Vector3(hx, hy, hz);
                hasAny = true;
            }

            float sx2 = 0f;
            float sy2 = 0f;
            float sz2 = 0f;
            if (TryReadFloat("ComponentSparkFxOffsetXField", out sx2) ||
                TryReadFloat("ComponentSparkFxOffsetYField", out sy2) ||
                TryReadFloat("ComponentSparkFxOffsetZField", out sz2))
            {
                tuning.SparkFxOffset = new Vector3(sx2, sy2, sz2);
                hasAny = true;
            }

            float ex2 = 0f;
            float ey2 = 0f;
            float ez2 = 0f;
            if (TryReadFloat("ComponentErrorFxOffsetXField", out ex2) ||
                TryReadFloat("ComponentErrorFxOffsetYField", out ey2) ||
                TryReadFloat("ComponentErrorFxOffsetZField", out ez2))
            {
                tuning.ErrorFxOffset = new Vector3(ex2, ey2, ez2);
                hasAny = true;
            }

            float smx = 0f;
            float smy = 0f;
            float smz = 0f;
            if (TryReadFloat("ComponentSmokeOffsetXField", out smx) ||
                TryReadFloat("ComponentSmokeOffsetYField", out smy) ||
                TryReadFloat("ComponentSmokeOffsetZField", out smz))
            {
                tuning.SmokeOffset = new Vector3(smx, smy, smz);
                hasAny = true;
            }

            float ux = 0f;
            float uy = 0f;
            float uz = 0f;
            if (TryReadFloat("ComponentUsbOffsetXField", out ux) ||
                TryReadFloat("ComponentUsbOffsetYField", out uy) ||
                TryReadFloat("ComponentUsbOffsetZField", out uz))
            {
                tuning.UsbOffset = new Vector3(ux, uy, uz);
                hasAny = true;
            }

            if (_modelOffset.sqrMagnitude > 0.000001f)
            {
                tuning.ModelOffset = _modelOffset;
                hasAny = true;
            }

            return hasAny ? tuning : null;
        }
        private List<PinLayoutPayload> CollectPinLayout()
        {
            var list = new List<PinLayoutPayload>();
            if (_pinsContainer == null) return list;
            foreach (var entry in _pinsContainer.Children())
            {
                var nameField = entry.Q<TextField>("PinNameField");
                string name = nameField?.value?.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new PinLayoutPayload
                {
                    name = name,
                    x = ReadFloat(entry.Q<TextField>("PinXField")),
                    y = ReadFloat(entry.Q<TextField>("PinYField")),
                    label = entry.Q<TextField>("PinLabelField")?.value ?? string.Empty,
                    labelOffsetX = ReadFloat(entry.Q<TextField>("PinLabelOffsetXField")),
                    labelOffsetY = ReadFloat(entry.Q<TextField>("PinLabelOffsetYField")),
                    labelSize = ReadInt(entry.Q<TextField>("PinLabelSizeField")),
                    anchorX = ReadFloat(entry.Q<TextField>("PinAnchorXField")),
                    anchorY = ReadFloat(entry.Q<TextField>("PinAnchorYField")),
                    anchorZ = ReadFloat(entry.Q<TextField>("PinAnchorZField")),
                    anchorRadius = ReadFloat(entry.Q<TextField>("PinAnchorRadiusField")),
                    pinType = NormalizePinType(GetEntryValue(entry, "PinTypeField"))
                });
            }
            return list;
        }

        private List<LabelLayoutPayload> CollectLabelLayout()
        {
            var list = new List<LabelLayoutPayload>();
            if (_labelsContainer == null) return list;
            foreach (var entry in _labelsContainer.Children())
            {
                var textField = entry.Q<TextField>("LabelTextField");
                string text = textField?.value?.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;
                list.Add(new LabelLayoutPayload
                {
                    text = text,
                    x = ReadFloat(entry.Q<TextField>("LabelXField")),
                    y = ReadFloat(entry.Q<TextField>("LabelYField")),
                    size = ReadInt(entry.Q<TextField>("LabelSizeField")),
                    align = GetEntryValue(entry, "LabelAlignField")
                });
            }
            return list;
        }

        private FxPayload[] CollectFxPayloads()
        {
            if (_fxContainer == null) return Array.Empty<FxPayload>();
            var list = new List<FxPayload>();
            foreach (var entry in _fxContainer.Children())
            {
                var idField = entry.Q<TextField>("FxIdField");
                string id = idField?.value?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id)) continue;
                var fx = new FxPayload
                {
                    id = id,
                    type = DenormalizeFxTypeValue(GetEntryValue(entry, "FxTypeField")),
                    anchorId = entry.Q<TextField>("FxAnchorField")?.value?.Trim() ?? string.Empty,
                    localPosition = new Vector3(
                        ReadFloat(entry.Q<TextField>("FxPosXField")),
                        ReadFloat(entry.Q<TextField>("FxPosYField")),
                        ReadFloat(entry.Q<TextField>("FxPosZField"))),
                    trigger = entry.Q<TextField>("FxTriggerField")?.value ?? string.Empty,
                    expression = entry.Q<TextField>("FxExpressionField")?.value ?? string.Empty
                };
                list.Add(fx);
            }
            return list.ToArray();
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

            string symbolSourcePath = GetTextValue("ComponentSymbolFileField").Trim();
            if (!string.IsNullOrWhiteSpace(symbolSourcePath) && File.Exists(symbolSourcePath))
            {
                payload.symbolFile = ComponentPackageUtility.BuildAssetEntryName(Path.GetFileName(symbolSourcePath));
            }
            else if (string.IsNullOrWhiteSpace(payload.symbolFile))
            {
                payload.symbolFile = string.Empty;
            }

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
            var extraAssets = new List<string>();
            if (!string.IsNullOrWhiteSpace(symbolSourcePath) && File.Exists(symbolSourcePath))
            {
                extraAssets.Add(symbolSourcePath);
            }
            ComponentPackageUtility.SavePackage(packagePath, json, modelSourcePath, extraAssets);
        }

        private string ResolvePackagePath(ComponentDefinitionPayload payload)
        {
            if (!string.IsNullOrWhiteSpace(_packagePath) && File.Exists(_packagePath))
            {
                return _packagePath;
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

        private ComponentDefinitionPayload CreateDefaultPayload()
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

        private void BeginModelLoad(string message)
        {
            if (_loadingLabel != null)
            {
                _loadingLabel.text = string.IsNullOrWhiteSpace(message) ? "Loading model..." : message;
            }
            if (_loadingOverlay != null)
            {
                UpdateLoadingBlurBackdrop();
                _loadingOverlay.style.display = DisplayStyle.Flex;
            }

            if (_loadingSpinner != null && _loadingSpin == null)
            {
                _loadingSpin = _loadingSpinner.schedule.Execute(() =>
                {
                    if (_loadingSpinner == null) return;
                    var angle = (Time.realtimeSinceStartup * 360f) % 360f;
                    _loadingSpinner.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
                }).Every(16);
            }
        }

        private void EndModelLoad()
        {
            if (_loadingOverlay != null)
            {
                _loadingOverlay.style.display = DisplayStyle.None;
            }
        }

        private void UpdateLoadingBlurBackdrop()
        {
            if (_loadingOverlay == null || _studioView == null) return;
            var src = _studioView.TargetTexture;
            if (src == null) return;

            int w = Mathf.Max(16, src.width / 6);
            int h = Mathf.Max(16, src.height / 6);
            if (_loadingBlurTexture == null || _loadingBlurTexture.width != w || _loadingBlurTexture.height != h)
            {
                if (_loadingBlurTexture != null)
                {
                    _loadingBlurTexture.Release();
                }
                _loadingBlurTexture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
                {
                    name = "ComponentStudio_LoadingBlur",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _loadingBlurTexture.Create();
            }

            Graphics.Blit(src, _loadingBlurTexture);
            _loadingOverlay.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_loadingBlurTexture));
        }

        private void CancelModelLoad(bool hideOverlay)
        {
            if (_modelLoadCts != null)
            {
                _modelLoadCts.Cancel();
                _modelLoadCts.Dispose();
                _modelLoadCts = null;
            }
            if (hideOverlay)
            {
                EndModelLoad();
            }
        }

        private void SetStatus(string message, bool isError)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = message;
            _statusLabel.RemoveFromClassList("status-error");
            _statusLabel.RemoveFromClassList("status-ok");
            _statusLabel.AddToClassList(isError ? "status-error" : "status-ok");
            if (!string.IsNullOrWhiteSpace(message))
            {
                AddLog(isError ? LogLevel.Warning : LogLevel.Info, message, "status");
            }
        }

        private TextField GetField(string name)
        {
            return _root?.Q<TextField>(name);
        }

        private DropdownField GetDropdownField(string name)
        {
            return _root?.Q<DropdownField>(name);
        }

        private string GetTextValue(string name)
        {
            var field = GetField(name);
            if (field != null) return field.value ?? string.Empty;
            var dropdown = GetDropdownField(name);
            return dropdown?.value ?? string.Empty;
        }

        private void SetTextValue(string name, string value)
        {
            var field = GetField(name);
            if (field != null)
            {
                field.SetValueWithoutNotify(value ?? string.Empty);
                return;
            }
            var dropdown = GetDropdownField(name);
            dropdown?.SetValueWithoutNotify(value ?? string.Empty);
        }

        private string GetTypeValue()
        {
            return GetTextValue("ComponentTypeField").Trim();
        }

        private void SetTypeValue(string value)
        {
            value = value ?? string.Empty;
            if (_componentTypeDropdown != null && !string.IsNullOrWhiteSpace(value))
            {
                var choices = _componentTypeDropdown.choices ?? new List<string>();
                if (!choices.Any(choice => string.Equals(choice, value, StringComparison.OrdinalIgnoreCase)))
                {
                    var updated = new List<string>(choices) { value };
                    _componentTypeDropdown.choices = updated;
                }
            }
            SetTextValue("ComponentTypeField", value);
            UpdateTypePanels();
        }

        private void UpdateTypePanels()
        {
            string type = GetTypeValue();
            string label = string.IsNullOrWhiteSpace(type) ? "TYPE" : type.Trim().ToUpperInvariant();
            if (_leftTabTypeBtn != null) _leftTabTypeBtn.text = label;
            if (_typePanelTitle != null) _typePanelTitle.text = $"{label} Specs";

            bool showLed = type.Contains("led", StringComparison.OrdinalIgnoreCase);
            bool showResistor = type.Contains("resistor", StringComparison.OrdinalIgnoreCase);
            if (_ledSettingsSection != null)
            {
                _ledSettingsSection.style.display = showLed ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_resistorSettingsSection != null)
            {
                _resistorSettingsSection.style.display = showResistor ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void SetField(string name, string value)
        {
            var field = GetField(name);
            if (field == null) return;
            field.SetValueWithoutNotify(value ?? string.Empty);
        }

        private static void SetField(TextField field, string value)
        {
            if (field == null) return;
            field.SetValueWithoutNotify(value ?? string.Empty);
        }

        private static TextField CreateField(string name, string value, string className)
        {
            var field = new TextField { name = name };
            field.AddToClassList(className);
            field.SetValueWithoutNotify(value ?? string.Empty);
            return field;
        }

        private static DropdownField CreateDropdownField(string name, string value, string className, IEnumerable<string> choices)
        {
            var field = new DropdownField { name = name };
            if (!string.IsNullOrWhiteSpace(className)) field.AddToClassList(className);
            field.AddToClassList("input-dark");
            var list = choices?.ToList() ?? new List<string>();
            if (!string.IsNullOrWhiteSpace(value) &&
                !list.Any(choice => string.Equals(choice, value, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(value);
            }
            field.choices = list;
            string resolved = string.IsNullOrWhiteSpace(value)
                ? (list.Count > 0 ? list[0] : string.Empty)
                : value;
            field.SetValueWithoutNotify(resolved);
            return field;
        }

        private static string NormalizeFxTypeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? FxTypeUnsetLabel : value.Trim();
        }

        private static string DenormalizeFxTypeValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return string.Equals(value, FxTypeUnsetLabel, StringComparison.OrdinalIgnoreCase) ? string.Empty : value.Trim();
        }

        private static string NormalizePinType(string value)
        {
            if (string.Equals(value, PinTypePin, StringComparison.OrdinalIgnoreCase))
            {
                return PinTypePin;
            }
            return PinTypeLead;
        }

        private static string FormatFloat(float value)
        {
            if (Mathf.Abs(value) < 0.0001f) return string.Empty;
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static float ReadFloat(TextField field, float fallback = 0f)
        {
            if (field == null) return fallback;
            string raw = field.value?.Trim();
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return value;
            return fallback;
        }

        private static int ReadInt(TextField field, int fallback = 0)
        {
            if (field == null) return fallback;
            string raw = field.value?.Trim();
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
            return fallback;
        }

        private bool TryReadFloat(string fieldName, out float value)
        {
            value = 0f;
            var field = GetField(fieldName);
            if (field == null) return false;
            string raw = field.value?.Trim();
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string GetEntryValue(VisualElement entry, string fieldName)
        {
            if (entry == null) return string.Empty;
            var textField = entry.Q<TextField>(fieldName);
            if (textField != null) return textField.value ?? string.Empty;
            var dropdown = entry.Q<DropdownField>(fieldName);
            return dropdown?.value ?? string.Empty;
        }

        private static void SetEntryValue(VisualElement entry, string fieldName, string value)
        {
            if (entry == null) return;
            var field = entry.Q<TextField>(fieldName);
            if (field != null)
            {
                field.SetValueWithoutNotify(value ?? string.Empty);
                return;
            }
            var dropdown = entry.Q<DropdownField>(fieldName);
            dropdown?.SetValueWithoutNotify(value ?? string.Empty);
        }

        private static string SanitizeComponentId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var safe = new string(raw.Trim().Select(ch =>
                    char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : (char.IsWhiteSpace(ch) ? '-' : '\0'))
                .Where(ch => ch != '\0').ToArray());
            return safe.Trim('-');
        }

        private string GetNextPinName()
        {
            if (_pinsContainer == null) return "P1";
            int index = _pinsContainer.childCount + 1;
            return $"P{index}";
        }

        private static string ToUnityAssetPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return string.Empty;
            string dataPath = Application.dataPath.Replace('\\', '/');
            string full = Path.GetFullPath(filePath).Replace('\\', '/');
            if (!full.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return "Assets" + full.Substring(dataPath.Length);
        }

        private bool TryPickFileRuntime(string title, string filter, out string path)
        {
            path = string.Empty;
#if UNITY_EDITOR
            string extension = "*";
            if (!string.IsNullOrWhiteSpace(filter))
            {
                var parts = filter.Split('|');
                if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    extension = parts[1].Replace("*.", string.Empty).Replace("*", string.Empty).Split(';')[0];
                }
            }
            path = UnityEditor.EditorUtility.OpenFilePanel(title ?? "Select file", Application.dataPath, extension);
            return !string.IsNullOrWhiteSpace(path);
#else
            return false;
#endif
        }

        private bool TryConvertModelToGlb(string sourcePath, out string glbPath)
        {
            glbPath = string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return false;
            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext == ".glb" || ext == ".gltf") return false;
            if (ext != ".fbx" && ext != ".obj") return false;

            if (!TryFindBlenderPath(out var blenderPath))
            {
                Debug.LogWarning("[ComponentStudio] Blender not found. Set ROBOTWIN_BLENDER or BLENDER_PATH.");
                return false;
            }
            string scriptPath = ResolveBlenderScriptPath();
            if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            {
                Debug.LogWarning("[ComponentStudio] Blender script missing.");
                return false;
            }

            string outputPath = Path.ChangeExtension(sourcePath, ".glb");
            if (File.Exists(outputPath))
            {
                var srcTime = File.GetLastWriteTimeUtc(sourcePath);
                var outTime = File.GetLastWriteTimeUtc(outputPath);
                if (outTime >= srcTime)
                {
                    glbPath = outputPath;
                    return true;
                }
            }

            if (!RunBlenderConvert(blenderPath, scriptPath, sourcePath, outputPath)) return false;
            if (!File.Exists(outputPath))
            {
                Debug.LogWarning("[ComponentStudio] Blender finished but GLB was not created.");
                return false;
            }
            glbPath = outputPath;
            return true;
        }

        private static bool TryFindBlenderPath(out string blenderPath)
        {
            blenderPath = GetEnvVar("ROBOTWIN_BLENDER");
            if (string.IsNullOrWhiteSpace(blenderPath))
            {
                blenderPath = GetEnvVar("BLENDER_PATH");
            }
            if (!string.IsNullOrWhiteSpace(blenderPath))
            {
                blenderPath = blenderPath.Trim().Trim('"');
                if (Directory.Exists(blenderPath))
                {
                    string candidate = Path.Combine(blenderPath, "blender.exe");
                    if (File.Exists(candidate))
                    {
                        blenderPath = candidate;
                        return true;
                    }
                }
                if (File.Exists(blenderPath)) return true;
            }

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                string candidate = Path.Combine(dir.Trim(), "blender.exe");
                if (File.Exists(candidate))
                {
                    blenderPath = candidate;
                    return true;
                }
            }

            blenderPath = string.Empty;
            return false;
        }

        private static string GetEnvVar(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
            value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(value)) return value;
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
        }

        private static string ResolveBlenderScriptPath()
        {
            string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            return Path.Combine(repoRoot, "tools", "scripts", "blender_to_glb.py");
        }

        private static bool RunBlenderConvert(string blenderPath, string scriptPath, string inputPath, string outputPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = blenderPath,
                    Arguments = $"--background --python \"{scriptPath}\" -- \"{inputPath}\" \"{outputPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(startInfo);
                if (process == null) return false;
                process.WaitForExit(120000);
                if (process.ExitCode != 0)
                {
                    string error = process.StandardError.ReadToEnd();
                    Debug.LogWarning($"[ComponentStudio] Blender conversion failed: {error}");
                    return false;
                }
                string output = process.StandardOutput.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Debug.Log($"[ComponentStudio] Blender output: {output}");
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ComponentStudio] Blender conversion error: {ex.Message}");
                return false;
            }
        }

#if UNITY_EDITOR
        private static void TryExtractMaterialsFromFbx(string sourcePath, string outputDir)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return;
            if (string.IsNullOrWhiteSpace(outputDir)) return;
            if (!string.Equals(Path.GetExtension(sourcePath), ".fbx", StringComparison.OrdinalIgnoreCase)) return;

            string tempImportRoot = Path.Combine(Application.dataPath, "Temp", "ComponentStudioImports");
            string tempMatRoot = Path.Combine(Application.dataPath, "Temp", "ComponentStudioMaterials");
            Directory.CreateDirectory(tempImportRoot);
            Directory.CreateDirectory(tempMatRoot);

            string fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(fileName)) return;

            string tempFbxFull = Path.Combine(tempImportRoot, fileName);
            File.Copy(sourcePath, tempFbxFull, true);

            string tempFbxAsset = ToUnityAssetPath(tempFbxFull);
            if (string.IsNullOrWhiteSpace(tempFbxAsset)) return;
            AssetDatabase.ImportAsset(tempFbxAsset, ImportAssetOptions.ForceUpdate);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(tempFbxAsset);
            if (prefab == null) return;

            var materials = new HashSet<Material>();
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat != null) materials.Add(mat);
                }
            }
            if (materials.Count == 0) return;

            var tempMatAssets = new List<string>();
            foreach (var mat in materials)
            {
                string safeName = SanitizeComponentId(mat.name);
                if (string.IsNullOrWhiteSpace(safeName)) safeName = "mat";
                string matAsset = Path.Combine(tempMatRoot, safeName + ".mat");
                matAsset = AssetDatabase.GenerateUniqueAssetPath(ToUnityAssetPath(matAsset));
                var matCopy = new Material(mat);
                AssetDatabase.CreateAsset(matCopy, matAsset);
                tempMatAssets.Add(matAsset);
            }
            AssetDatabase.SaveAssets();

            Directory.CreateDirectory(outputDir);
            foreach (var matAsset in tempMatAssets)
            {
                CopyAssetFileToDirectory(matAsset, outputDir);
                foreach (var dep in AssetDatabase.GetDependencies(matAsset, true))
                {
                    if (string.Equals(dep, matAsset, StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsTextureAsset(dep)) CopyAssetFileToDirectory(dep, outputDir);
                }
            }

            foreach (var matAsset in tempMatAssets)
            {
                AssetDatabase.DeleteAsset(matAsset);
            }
            AssetDatabase.DeleteAsset(tempFbxAsset);
            AssetDatabase.Refresh();
        }

        private static void CopyAssetFileToDirectory(string assetPath, string outputDir)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(outputDir)) return;
            if (!assetPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase)) return;
            string fullPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets".Length).TrimStart('/', '\\'));
            if (!File.Exists(fullPath)) return;
            string destPath = Path.Combine(outputDir, Path.GetFileName(fullPath));
            if (File.Exists(destPath))
            {
                var srcTime = File.GetLastWriteTimeUtc(fullPath);
                var destTime = File.GetLastWriteTimeUtc(destPath);
                if (destTime >= srcTime) return;
            }
            File.Copy(fullPath, destPath, true);
        }

        private static bool IsTextureAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return false;
            var type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            return type != null && typeof(Texture).IsAssignableFrom(type);
        }
#endif

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
            public PartOverridePayload[] parts;
            public StateOverridePayload[] states;
            public FxPayload[] fx;
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
            public string pinType;
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
            public bool lineFlip;
            public float rotation;
            public float fontSize;
        }

        [Serializable]
        private class PartOverridePayload
        {
            public string name;
            public Vector3 position;
            public Vector3 rotation;
            public Vector3 scale;
            public Color color;
            public bool useColor;
            public string textureFile;
            public bool useTexture;

            // Physical (optional)
            public string physicalMaterial;
            public float densityKgPerM3;
            public float massKg;
            public float volumeM3;
            public float friction;
            public float elasticity;
            public float strength;
        }

        [Serializable]
        private class StateOverridePayload
        {
            public string id;
            public PartOverridePayload[] parts;
        }

        [Serializable]
        private class ComponentTuningPayload
        {
            public Vector3 Euler;
            public Vector3 Scale;
            public Vector3 ModelOffset;
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

        [Serializable]
        private class FxPayload
        {
            public string id;
            public string type;
            public string anchorId;
            public Vector3 localPosition;
            public string trigger;
            public string expression;
        }

        private class PartOverride
        {
            public string Name;
            public Vector3 Position;
            public Vector3 Rotation;
            public Vector3 Scale;
            public Color Color;
            public bool UseColor;
            public string TextureEntry;
            public bool UseTexture;

            public string PhysicalMaterialId;
            public float DensityKgPerM3;
            public float MassKg;
            public float VolumeM3;
            public float Friction;
            public float Elasticity;
            public float Strength;

            public PartOverride(Transform part)
            {
                Name = part.name;
                Position = part.localPosition;
                Rotation = part.localEulerAngles;
                Scale = part.localScale;
                TextureEntry = string.Empty;
                UseTexture = false;

                PhysicalMaterialId = string.Empty;
                DensityKgPerM3 = 0f;
                MassKg = 0f;
                VolumeM3 = 0f;
                Friction = 0f;
                Elasticity = 0f;
                Strength = 0f;
            }

            public PartOverride(PartOverridePayload payload)
            {
                Name = payload.name;
                Position = payload.position;
                Rotation = payload.rotation;
                Scale = payload.scale;
                Color = payload.color;
                UseColor = payload.useColor;
                TextureEntry = payload.textureFile ?? string.Empty;
                UseTexture = payload.useTexture;

                PhysicalMaterialId = payload.physicalMaterial ?? string.Empty;
                DensityKgPerM3 = payload.densityKgPerM3;
                MassKg = payload.massKg;
                VolumeM3 = payload.volumeM3;
                Friction = payload.friction;
                Elasticity = payload.elasticity;
                Strength = payload.strength;
            }

            public PartOverridePayload ToPayload()
            {
                return new PartOverridePayload
                {
                    name = Name,
                    position = Position,
                    rotation = Rotation,
                    scale = Scale,
                    color = Color,
                    useColor = UseColor,
                    textureFile = TextureEntry,
                    useTexture = UseTexture,

                    physicalMaterial = PhysicalMaterialId,
                    densityKgPerM3 = DensityKgPerM3,
                    massKg = MassKg,
                    volumeM3 = VolumeM3,
                    friction = Friction,
                    elasticity = Elasticity,
                    strength = Strength
                };
            }
        }

        private class StateOverride
        {
            public string Id;
            public Dictionary<string, PartOverride> Parts = new Dictionary<string, PartOverride>(StringComparer.OrdinalIgnoreCase);

            public StateOverride(string id)
            {
                Id = id;
            }

            public StateOverride(StateOverridePayload payload)
            {
                Id = payload.id;
                if (payload.parts != null)
                {
                    foreach (var part in payload.parts)
                    {
                        if (part == null || string.IsNullOrWhiteSpace(part.name)) continue;
                        Parts[part.name] = new PartOverride(part);
                    }
                }
            }

            public StateOverridePayload ToPayload()
            {
                return new StateOverridePayload
                {
                    id = Id,
                    parts = Parts.Values.Select(p => p.ToPayload()).ToArray()
                };
            }
        }
    }
}
