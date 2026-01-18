
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RobotTwin.CoreSim.IPC;

namespace RobotWinFirmwareMonitor
{
    public sealed class MainForm : Form
    {
        private readonly FirmwareClient _client = new FirmwareClient();
        private readonly HttpClient _unityHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
        private readonly BindingList<PinRow> _pinRows = new BindingList<PinRow>();
        private readonly BindingList<AnalogRow> _analogRows = new BindingList<AnalogRow>();
        private readonly Dictionary<string, ListViewItem> _perfItems = new Dictionary<string, ListViewItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ListViewItem> _cpuItems = new Dictionary<string, ListViewItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ListViewItem> _routingItems = new Dictionary<string, ListViewItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ListViewItem> _bitMapItems = new Dictionary<string, ListViewItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _routingLastValues = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly object _sequenceLock = new object();
        private readonly object _pinHistoryLock = new object();
        private readonly Queue<byte[]> _pinHistory = new Queue<byte[]>();
        private readonly Queue<string> _traceBuffer = new Queue<string>();
        private readonly object _resourceLock = new object();
        private readonly List<ResourceSample> _resourceSamples = new List<ResourceSample>();
        private readonly StartupOptions _startupOptions;
        private readonly string _logDirectory = Program.LogDirectoryPath;
        private readonly string _profilesDirectory = Path.Combine(Program.LogDirectoryPath, "profiles");
        private const int UnityBridgeBasePort = 8085;
        private const int UnityBridgePortAttempts = 10;
        private const int PinHistoryDepth = 120;
        private const int ResourceHistoryDepth = 180;
        private const int TraceMaxItems = 3000;
        private const int TraceMaxLines = 5000;
        private const int ProcessTreeDepthLimit = 64;
        private const string RefreshProcessLabel = "Refresh Processes";
        private const string RefreshHostsLabel = "Refresh Hosts";
        private static readonly Color UiTextColor = Color.FromArgb(35, 35, 35);
        private static readonly Color UiInputTextColor = Color.FromArgb(20, 20, 20);
        private static readonly Color UiInputBackColor = Color.White;
        private static Icon? _appIcon;
        private int _processTreeRefreshActive;
        private int _runningHostRefreshActive;
        private bool _launchOnConnect;
        private readonly Dictionary<TabPage, DetachedTab> _detachedTabs = new Dictionary<TabPage, DetachedTab>();
        private TabPage? _dragTab;
        private TabControl? _dragTabControl;

        private CancellationTokenSource? _stepCts;
        private CancellationTokenSource? _traceCts;
        private CancellationTokenSource? _unityCts;
        private CancellationTokenSource? _unityAutoConnectCts;
        private int _unityAutoConnectActive;
        private ulong _stepSequence;
        private bool _connected;
        private volatile bool _isUnityTarget;
        private string? _unityBaseUrl;
        private bool _unitySerialInitialized;
        private int _unityLastSerialLength;
        private bool _startupOptionsApplied;
        private string _traceFilter = string.Empty;
        private bool _tracePaused;
        private ulong _lastIdleTime;
        private ulong _lastKernelTime;
        private ulong _lastUserTime;
        private ulong _lastNetBytes;
        private long _lastNetTicks;
        private double _netMaxMbps;
        private ResourceSample _latestResourceSample;

        private TextBox _pipeNameTextBox = null!;
        private TextBox _boardIdTextBox = null!;
        private TextBox _boardProfileTextBox = null!;
        private TextBox _firmwarePathTextBox = null!;
        private TextBox _bvmPathTextBox = null!;
        private TextBox _extraArgsTextBox = null!;
        private TextBox _tracePathTextBox = null!;
        private TextBox _injectPathTextBox = null!;
        private TextBox _injectAddressTextBox = null!;
        private ComboBox _injectTypeCombo = null!;
        private TextBox _serviceNameTextBox = null!;
        private TextBox _serviceExeTextBox = null!;
        private TextBox _serviceArgsTextBox = null!;
        private ComboBox _profileCombo = null!;
        private TextBox _profileNameTextBox = null!;
        private Button _saveProfileButton = null!;
        private Button _loadProfileButton = null!;
        private Button _deleteProfileButton = null!;
        private CheckBox _prefAutoConnect = null!;
        private CheckBox _prefAutoTraceTail = null!;
        private ComboBox _prefConnectionMode = null!;
        private ComboBox _targetProgramCombo = null!;
        private ComboBox _prefTargetProgramCombo = null!;
        private ComboBox _runningHostCombo = null!;
        private Button _refreshHostButton = null!;
        private Button _attachHostButton = null!;
        private TextBox _helpTextBox = null!;
        private MenuStrip _menuStrip = null!;
        private NumericUpDown _deltaMicrosInput = null!;
        private NumericUpDown _stepIntervalMsInput = null!;
        private Label _statusLabel = null!;
        private Label _activityLabel = null!;
        private DataGridView _pinsGrid = null!;
        private DataGridView _analogGrid = null!;
        private TextBox _logTextBox = null!;
        private TextBox _serialTextBox = null!;
        private TextBox _bitOpsTextBox = null!;
        private TextBox _traceTextBox = null!;
        private TextBox _traceFilterTextBox = null!;
        private ListView _traceList = null!;
        private ListView _perfList = null!;
        private ListView _cpuList = null!;
        private ListView _routingList = null!;
        private ListView _bitMapList = null!;
        private TreeView _processTree = null!;
        private Button _refreshProcessButton = null!;
        private PinGraphPanel _pinGraphPanel = null!;
        private Button _traceTailButton = null!;
        private CheckBox _autoStepCheckBox = null!;
        private Button _traceClearButton = null!;
        private Button _traceOpenButton = null!;
        private CheckBox _tracePauseCheckBox = null!;
        private Label _routingStatusLabel = null!;
        private Button _routingResetButton = null!;
        private Label _injectStatusLabel = null!;
        private Label _injectSizeLabel = null!;
        private TextBox _injectPreviewTextBox = null!;
        private Button _injectValidateButton = null!;
        private Label _serviceStatusLabel = null!;
        private TextBox _serviceOutputTextBox = null!;
        private Label _resourceCpuLabel = null!;
        private Label _resourceRamLabel = null!;
        private Label _resourceAppMemLabel = null!;
        private Label _resourceNetLabel = null!;
        private ResourceGraphPanel _resourceGraphPanel = null!;
        private System.Windows.Forms.Timer _resourceTimer = null!;

        public MainForm()
        {
            Program.LogMessage("ui", "MainForm ctor start.");
            Text = "RobotWinFirmwareMonitor";
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(245, 247, 250);
            ForeColor = UiTextColor;
            Icon = GetAppIcon();
            Width = 1500;
            Height = 900;
            MinimumSize = new Size(1100, 720);
            Directory.CreateDirectory(_logDirectory);
            Directory.CreateDirectory(_profilesDirectory);
            _startupOptions = StartupOptions.FromArgs(Environment.GetCommandLineArgs());
            Program.LogMessage("ui", "BuildLayout start.");
            BuildLayout();
            Program.LogMessage("ui", "BuildLayout done.");
            ApplyTheme(this);
            Program.LogMessage("ui", "ApplyTheme done.");
            InitializeData();
            Program.LogMessage("ui", "InitializeData done.");
            ApplyDefaults();
            WireEvents();
            UpdateConnectionState(false);
            Program.LogMessage("ui", "MainForm ctor end.");
        }

        private void BuildLayout()
        {
            _menuStrip = new MenuStrip { Dock = DockStyle.Top };
            var connectionMenu = new ToolStripMenuItem("Connection");
            var programMenu = new ToolStripMenuItem("Program");
            var windowMenu = new ToolStripMenuItem("Window");
            var helpMenu = new ToolStripMenuItem("Help");
            _menuStrip.Items.AddRange(new ToolStripItem[] { connectionMenu, programMenu, windowMenu, helpMenu });
            MainMenuStrip = _menuStrip;

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(6)
            };

            var row1 = CreateFlowRow();
            _pipeNameTextBox = CreateInputTextBox(180, "RoboTwin.FirmwareEngine");
            _boardIdTextBox = CreateInputTextBox(120, "board");
            _boardProfileTextBox = CreateInputTextBox(140, "ArduinoUno");
            _statusLabel = new Label { AutoSize = true, ForeColor = Color.DarkRed, Text = "Disconnected" };
            row1.Controls.Add(MakeLabel("Pipe"));
            row1.Controls.Add(_pipeNameTextBox);
            row1.Controls.Add(MakeLabel("Board ID"));
            row1.Controls.Add(_boardIdTextBox);
            row1.Controls.Add(MakeLabel("Profile"));
            row1.Controls.Add(_boardProfileTextBox);
            row1.Controls.Add(MakeLabel("Status"));
            row1.Controls.Add(_statusLabel);
            _targetProgramCombo = CreateComboBox(140);
            _targetProgramCombo.Items.AddRange(new object[] { "Firmware Host", "Unity" });
            _targetProgramCombo.SelectedIndex = 0;
            _targetProgramCombo.SelectedIndexChanged += (_, __) => SyncTargetProgram(_targetProgramCombo.SelectedIndex);
            row1.Controls.Add(MakeLabel("Target"));
            row1.Controls.Add(_targetProgramCombo);

            var row2 = CreateFlowRow();
            _firmwarePathTextBox = CreateInputTextBox(320);
            var firmwareBrowse = new Button { Text = "Browse" };
            _bvmPathTextBox = CreateInputTextBox(320);
            var bvmBrowse = new Button { Text = "Browse" };
            _extraArgsTextBox = CreateInputTextBox(240);
            row2.Controls.Add(MakeLabel("Firmware EXE"));
            row2.Controls.Add(_firmwarePathTextBox);
            row2.Controls.Add(firmwareBrowse);
            row2.Controls.Add(MakeLabel("BVM/HEX"));
            row2.Controls.Add(_bvmPathTextBox);
            row2.Controls.Add(bvmBrowse);
            row2.Controls.Add(MakeLabel("Extra Args"));
            row2.Controls.Add(_extraArgsTextBox);

            var row3 = CreateFlowRow();
            var connectButton = new Button { Text = "Connect" };
            var disconnectButton = new Button { Text = "Disconnect" };
            var launchButton = new Button { Text = "Launch Firmware" };
            var loadButton = new Button { Text = "Load BVM/HEX" };
            var stepButton = new Button { Text = "Step Once" };
            _autoStepCheckBox = new CheckBox { Text = "Auto Step" };
            _deltaMicrosInput = CreateNumericInput(100, 2000000, 100000, 1000, 100);
            _stepIntervalMsInput = CreateNumericInput(1, 1000, 50, 5, 80);
            _tracePathTextBox = CreateInputTextBox(220);
            var traceBrowse = new Button { Text = "Browse" };
            _traceTailButton = new Button { Text = "Tail Trace" };
            _activityLabel = new Label { AutoSize = true, ForeColor = Color.DimGray, Text = "Active outputs: 0 high, 0 pwm" };
            row3.Controls.Add(connectButton);
            row3.Controls.Add(disconnectButton);
            row3.Controls.Add(launchButton);
            row3.Controls.Add(loadButton);
            row3.Controls.Add(MakeLabel("Delta us"));
            row3.Controls.Add(_deltaMicrosInput);
            row3.Controls.Add(MakeLabel("Step ms"));
            row3.Controls.Add(_stepIntervalMsInput);
            row3.Controls.Add(stepButton);
            row3.Controls.Add(_autoStepCheckBox);
            row3.Controls.Add(MakeLabel("Trace file"));
            row3.Controls.Add(_tracePathTextBox);
            row3.Controls.Add(traceBrowse);
            row3.Controls.Add(_traceTailButton);
            row3.Controls.Add(_activityLabel);

            header.Controls.Add(row1);
            header.Controls.Add(row2);
            header.Controls.Add(row3);

            var row4 = CreateFlowRow();
            _runningHostCombo = CreateComboBox(360);
            _refreshHostButton = new Button { Text = RefreshHostsLabel };
            _attachHostButton = new Button { Text = "Attach PID" };
            row4.Controls.Add(MakeLabel("Running Host"));
            row4.Controls.Add(_runningHostCombo);
            row4.Controls.Add(_refreshHostButton);
            row4.Controls.Add(_attachHostButton);
            header.Controls.Add(row4);

            var mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 880
            };

            var dataTabs = new TabControl { Dock = DockStyle.Fill };
            var pinsTab = new TabPage("Pins");
            var analogTab = new TabPage("Analog");
            var graphTab = new TabPage("Pin Graph");
            _pinsGrid = new DataGridView { Dock = DockStyle.Fill };
            _analogGrid = new DataGridView { Dock = DockStyle.Fill };
            pinsTab.Controls.Add(_pinsGrid);
            analogTab.Controls.Add(_analogGrid);
            _pinGraphPanel = new PinGraphPanel(GetPinHistorySnapshot);
            graphTab.Controls.Add(_pinGraphPanel);
            dataTabs.TabPages.Add(pinsTab);
            dataTabs.TabPages.Add(analogTab);
            dataTabs.TabPages.Add(graphTab);

            var logTabs = new TabControl { Dock = DockStyle.Fill };
            _logTextBox = CreateLogTextBox();
            _serialTextBox = CreateLogTextBox();
            _bitOpsTextBox = CreateLogTextBox();
            _traceTextBox = CreateLogTextBox();
            _traceList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            _traceList.Columns.Add("PC", 80, HorizontalAlignment.Left);
            _traceList.Columns.Add("OP", 80, HorizontalAlignment.Left);
            _traceList.Columns.Add("MNEM", 80, HorizontalAlignment.Left);
            _traceList.Columns.Add("SP", 80, HorizontalAlignment.Left);
            _traceList.Columns.Add("SREG", 80, HorizontalAlignment.Left);
            _traceList.Columns.Add("Tick", 140, HorizontalAlignment.Left);
            _perfList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            _perfList.Columns.Add("Counter", 210, HorizontalAlignment.Left);
            _perfList.Columns.Add("Value", 160, HorizontalAlignment.Left);

            _cpuList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            _cpuList.Columns.Add("Metric", 210, HorizontalAlignment.Left);
            _cpuList.Columns.Add("Value", 180, HorizontalAlignment.Left);

            _routingList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            _routingList.Columns.Add("Signal", 220, HorizontalAlignment.Left);
            _routingList.Columns.Add("Value", 160, HorizontalAlignment.Left);
            _routingList.Columns.Add("Delta", 160, HorizontalAlignment.Left);

            var routingHeader = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                Padding = new Padding(4),
                WrapContents = false,
                AutoSize = false
            };
            _routingStatusLabel = new Label { AutoSize = true, Text = "Last update: --" };
            _routingResetButton = new Button { Text = "Reset Deltas" };
            var routingCopyButton = new Button { Text = "Copy" };
            routingCopyButton.Click += (_, __) => CopyRoutingMetrics();
            routingHeader.Controls.Add(_routingStatusLabel);
            routingHeader.Controls.Add(routingCopyButton);
            routingHeader.Controls.Add(_routingResetButton);

            var routingPanel = new Panel { Dock = DockStyle.Fill };
            routingPanel.Controls.Add(_routingList);
            routingPanel.Controls.Add(routingHeader);

            _bitMapList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            _bitMapList.Columns.Add("Field", 180, HorizontalAlignment.Left);
            _bitMapList.Columns.Add("Offset", 70, HorizontalAlignment.Left);
            _bitMapList.Columns.Add("Width", 60, HorizontalAlignment.Left);
            _bitMapList.Columns.Add("Bits", 220, HorizontalAlignment.Left);
            _bitMapList.Columns.Add("Value", 160, HorizontalAlignment.Left);

            _processTree = new TreeView { Dock = DockStyle.Fill };
            _refreshProcessButton = new Button { Dock = DockStyle.Top, Height = 28, Text = RefreshProcessLabel };
            var processPanel = new Panel { Dock = DockStyle.Fill };
            processPanel.Controls.Add(_processTree);
            processPanel.Controls.Add(_refreshProcessButton);

            var resourcePanel = new Panel { Dock = DockStyle.Fill };
            var resourceHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 2,
                AutoSize = true,
                Padding = new Padding(6)
            };
            resourceHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            resourceHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            resourceHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            resourceHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _resourceCpuLabel = new Label { AutoSize = true, Text = "CPU: --" };
            _resourceRamLabel = new Label { AutoSize = true, Text = "RAM: --" };
            _resourceAppMemLabel = new Label { AutoSize = true, Text = "App Memory: --" };
            _resourceNetLabel = new Label { AutoSize = true, Text = "Network: --" };
            resourceHeader.Controls.Add(_resourceCpuLabel, 0, 0);
            resourceHeader.Controls.Add(_resourceRamLabel, 1, 0);
            resourceHeader.Controls.Add(_resourceAppMemLabel, 0, 1);
            resourceHeader.Controls.Add(_resourceNetLabel, 1, 1);

            _resourceGraphPanel = new ResourceGraphPanel(GetResourceSamplesSnapshot);
            resourcePanel.Controls.Add(_resourceGraphPanel);
            resourcePanel.Controls.Add(resourceHeader);

            _resourceTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _resourceTimer.Tick += (_, __) => UpdateResourceMonitor();

            var injectorPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 6,
                Padding = new Padding(8)
            };
            injectorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            injectorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            injectorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            injectorPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            injectorPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            injectorPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            injectorPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            injectorPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            injectorPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            _injectPathTextBox = CreateInputTextBox(0);
            _injectPathTextBox.Dock = DockStyle.Fill;
            _injectAddressTextBox = CreateInputTextBox(0, "0x0000");
            _injectAddressTextBox.Dock = DockStyle.Fill;
            _injectTypeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _injectTypeCombo.Items.AddRange(new object[] { "Flash", "SRAM", "IO", "EEPROM" });
            _injectTypeCombo.SelectedIndex = 0;
            var injectBrowse = new Button { Text = "Browse", Dock = DockStyle.Fill };
            _injectValidateButton = new Button { Text = "Validate", Dock = DockStyle.Fill };
            var injectButton = new Button { Text = "Inject", Dock = DockStyle.Fill };
            _injectSizeLabel = new Label { AutoSize = true, Text = "Size: --" };
            _injectStatusLabel = new Label { AutoSize = true, Text = "Status: idle" };
            _injectPreviewTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };

            injectorPanel.Controls.Add(MakePanelLabel("Patch File"), 0, 0);
            injectorPanel.Controls.Add(_injectPathTextBox, 1, 0);
            injectorPanel.Controls.Add(injectBrowse, 2, 0);
            injectorPanel.Controls.Add(MakePanelLabel("Address"), 0, 1);
            injectorPanel.Controls.Add(_injectAddressTextBox, 1, 1);
            injectorPanel.Controls.Add(MakePanelLabel(" "), 2, 1);
            injectorPanel.Controls.Add(MakePanelLabel("Memory Type"), 0, 2);
            injectorPanel.Controls.Add(_injectTypeCombo, 1, 2);
            injectorPanel.Controls.Add(MakePanelLabel(" "), 2, 2);
            injectorPanel.Controls.Add(MakePanelLabel("Patch Size"), 0, 3);
            injectorPanel.Controls.Add(_injectSizeLabel, 1, 3);
            injectorPanel.Controls.Add(_injectValidateButton, 2, 3);
            injectorPanel.Controls.Add(MakePanelLabel("Preview"), 0, 4);
            injectorPanel.Controls.Add(_injectPreviewTextBox, 1, 4);
            injectorPanel.SetColumnSpan(_injectPreviewTextBox, 2);
            injectorPanel.Controls.Add(MakePanelLabel("Status"), 0, 5);
            injectorPanel.Controls.Add(_injectStatusLabel, 1, 5);
            injectorPanel.Controls.Add(injectButton, 2, 5);

            var servicePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(8)
            };
            servicePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            servicePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            servicePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            servicePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var serviceFields = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3
            };
            serviceFields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            serviceFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            serviceFields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            serviceFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            serviceFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            serviceFields.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            _serviceNameTextBox = CreateInputTextBox(0, "RobotWinFirmwareHost");
            _serviceNameTextBox.Dock = DockStyle.Fill;
            _serviceExeTextBox = CreateInputTextBox(0);
            _serviceExeTextBox.Dock = DockStyle.Fill;
            _serviceArgsTextBox = CreateInputTextBox(0);
            _serviceArgsTextBox.Dock = DockStyle.Fill;
            var serviceBrowse = new Button { Text = "Browse", Dock = DockStyle.Fill };
            var installService = new Button { Text = "Install" };
            var removeService = new Button { Text = "Remove" };
            var startService = new Button { Text = "Start" };
            var stopService = new Button { Text = "Stop" };
            var queryService = new Button { Text = "Query", Dock = DockStyle.Fill };

            serviceFields.Controls.Add(MakePanelLabel("Service Name"), 0, 0);
            serviceFields.Controls.Add(_serviceNameTextBox, 1, 0);
            serviceFields.Controls.Add(queryService, 2, 0);
            serviceFields.Controls.Add(MakePanelLabel("Binary Path"), 0, 1);
            serviceFields.Controls.Add(_serviceExeTextBox, 1, 1);
            serviceFields.Controls.Add(serviceBrowse, 2, 1);
            serviceFields.Controls.Add(MakePanelLabel("Arguments"), 0, 2);
            serviceFields.Controls.Add(_serviceArgsTextBox, 1, 2);
            serviceFields.Controls.Add(MakePanelLabel(" "), 2, 2);

            var serviceButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            serviceButtons.Controls.Add(installService);
            serviceButtons.Controls.Add(removeService);
            serviceButtons.Controls.Add(startService);
            serviceButtons.Controls.Add(stopService);

            _serviceStatusLabel = new Label { AutoSize = true, Text = "Status: --" };
            _serviceOutputTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };

            servicePanel.Controls.Add(serviceFields, 0, 0);
            servicePanel.Controls.Add(serviceButtons, 0, 1);
            servicePanel.Controls.Add(_serviceStatusLabel, 0, 2);
            servicePanel.Controls.Add(_serviceOutputTextBox, 0, 3);

            logTabs.TabPages.Add(MakeTab("Logs", _logTextBox));
            logTabs.TabPages.Add(MakeTab("Serial", _serialTextBox));
            logTabs.TabPages.Add(MakeTab("Bit Ops", _bitOpsTextBox));
            var telemetryTabs = new TabControl { Dock = DockStyle.Fill };
            telemetryTabs.TabPages.Add(MakeTab("Perf", _perfList));
            telemetryTabs.TabPages.Add(MakeTab("CPU/Memory", _cpuList));
            telemetryTabs.TabPages.Add(MakeTab("Routing", routingPanel));
            telemetryTabs.TabPages.Add(MakeTab("Bit Map", _bitMapList));
            telemetryTabs.TabPages.Add(MakeTab("Resources", resourcePanel));
            logTabs.TabPages.Add(MakeTab("Telemetry", telemetryTabs));
            EnableTabReorder(telemetryTabs);
            logTabs.TabPages.Add(MakeTab("Process Tree", processPanel));
            logTabs.TabPages.Add(MakeTab("Injector", injectorPanel));
            logTabs.TabPages.Add(MakeTab("Services", servicePanel));
            logTabs.TabPages.Add(BuildPreferencesTab());
            logTabs.TabPages.Add(BuildHelpTab());
            var traceSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 220
            };
            traceSplit.Panel1.Controls.Add(_traceList);
            traceSplit.Panel2.Controls.Add(_traceTextBox);
            var traceTools = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                Padding = new Padding(4),
                WrapContents = false,
                AutoSize = false
            };
            _traceFilterTextBox = CreateInputTextBox(180);
            _tracePauseCheckBox = new CheckBox { Text = "Pause" };
            _traceClearButton = new Button { Text = "Clear" };
            _traceOpenButton = new Button { Text = "Open Trace" };
            traceTools.Controls.Add(MakeLabel("Filter"));
            traceTools.Controls.Add(_traceFilterTextBox);
            traceTools.Controls.Add(_tracePauseCheckBox);
            traceTools.Controls.Add(_traceClearButton);
            traceTools.Controls.Add(_traceOpenButton);

            var tracePanel = new Panel { Dock = DockStyle.Fill };
            tracePanel.Controls.Add(traceSplit);
            tracePanel.Controls.Add(traceTools);
            logTabs.TabPages.Add(MakeTab("Trace", tracePanel));

            mainSplit.Panel1.Controls.Add(dataTabs);
            mainSplit.Panel2.Controls.Add(logTabs);

            Controls.Add(mainSplit);
            Controls.Add(header);
            Controls.Add(_menuStrip);

            BuildMenuItems(connectionMenu, programMenu, windowMenu, helpMenu, dataTabs, logTabs, pinsTab, analogTab, graphTab);
            AttachTabContextMenu(telemetryTabs);

            firmwareBrowse.Click += (_, __) => BrowseFile(_firmwarePathTextBox, "Executable|*.exe|All files|*.*");
            bvmBrowse.Click += (_, __) => BrowseFile(_bvmPathTextBox, "Firmware|*.bvm;*.hex;*.bin|All files|*.*");
            traceBrowse.Click += (_, __) => BrowseFile(_tracePathTextBox, "Log files|*.log;*.txt|All files|*.*");
            connectButton.Click += async (_, __) => await ConnectWithLaunchAsync(_launchOnConnect);
            disconnectButton.Click += (_, __) => Disconnect();
            launchButton.Click += (_, __) => LaunchFirmware();
            loadButton.Click += (_, __) => LoadBvm();
            stepButton.Click += async (_, __) => await StepOnceAsync();
            _autoStepCheckBox.CheckedChanged += async (_, __) => await ToggleAutoStepAsync();
            _traceTailButton.Click += async (_, __) => await ToggleTraceTailAsync();
            _refreshProcessButton.Click += (_, __) => RefreshProcessTree();
            _refreshHostButton.Click += (_, __) => RefreshRunningHosts();
            _attachHostButton.Click += async (_, __) => await AttachSelectedHostAsync();
            injectBrowse.Click += (_, __) =>
            {
                BrowseFile(_injectPathTextBox, "Binary patch|*.bin;*.patch|All files|*.*");
                UpdateInjectPreview();
            };
            injectButton.Click += (_, __) => InjectPatch();
            _injectValidateButton.Click += (_, __) => ValidateInjectSettings();
            serviceBrowse.Click += (_, __) => BrowseFile(_serviceExeTextBox, "Executable|*.exe|All files|*.*");
            installService.Click += (_, __) => InstallService();
            removeService.Click += (_, __) => RemoveService();
            startService.Click += (_, __) => StartService();
            stopService.Click += (_, __) => StopService();
            queryService.Click += (_, __) => QueryService();
            _injectPathTextBox.Leave += (_, __) => UpdateInjectPreview();
            _routingResetButton.Click += (_, __) => ResetRoutingDeltas();
            _traceClearButton.Click += (_, __) => ClearTrace();
            _traceOpenButton.Click += (_, __) => OpenTraceFile();
            _traceFilterTextBox.TextChanged += (_, __) =>
            {
                _traceFilter = _traceFilterTextBox.Text.Trim();
                ApplyTraceFilter();
            };
            _tracePauseCheckBox.CheckedChanged += (_, __) => ToggleTracePause(_tracePauseCheckBox.Checked);
        }

        private void BuildMenuItems(
            ToolStripMenuItem connectionMenu,
            ToolStripMenuItem programMenu,
            ToolStripMenuItem windowMenu,
            ToolStripMenuItem helpMenu,
            TabControl dataTabs,
            TabControl logTabs,
            TabPage pinsTab,
            TabPage analogTab,
            TabPage graphTab)
        {
            var connectRunning = new ToolStripMenuItem("Connect (Running)") { ShortcutKeys = Keys.Control | Keys.R };
            connectRunning.Click += async (_, __) =>
            {
                _launchOnConnect = false;
                await ConnectWithLaunchAsync(false);
            };
            var connectLaunch = new ToolStripMenuItem("Connect + Launch") { ShortcutKeys = Keys.Control | Keys.L };
            connectLaunch.Click += async (_, __) =>
            {
                _launchOnConnect = true;
                await ConnectWithLaunchAsync(true);
            };
            var disconnect = new ToolStripMenuItem("Disconnect") { ShortcutKeys = Keys.Control | Keys.D };
            disconnect.Click += (_, __) => Disconnect();
            var refreshProcesses = new ToolStripMenuItem("Refresh Process Tree");
            refreshProcesses.Click += (_, __) => RefreshProcessTree();
            var refreshHosts = new ToolStripMenuItem("Refresh Running Hosts");
            refreshHosts.Click += (_, __) => RefreshRunningHosts();
            var attachPid = new ToolStripMenuItem("Attach Selected PID");
            attachPid.Click += async (_, __) => await AttachSelectedHostAsync();
            var saveProfile = new ToolStripMenuItem("Save Profile") { ShortcutKeys = Keys.Control | Keys.S };
            saveProfile.Click += (_, __) => SaveProfile();
            var loadProfile = new ToolStripMenuItem("Load Profile") { ShortcutKeys = Keys.Control | Keys.O };
            loadProfile.Click += (_, __) => LoadSelectedProfile();

            connectionMenu.DropDownItems.Add(connectRunning);
            connectionMenu.DropDownItems.Add(connectLaunch);
            connectionMenu.DropDownItems.Add(new ToolStripSeparator());
            connectionMenu.DropDownItems.Add(disconnect);
            connectionMenu.DropDownItems.Add(refreshProcesses);
            connectionMenu.DropDownItems.Add(refreshHosts);
            connectionMenu.DropDownItems.Add(attachPid);
            connectionMenu.DropDownItems.Add(new ToolStripSeparator());
            connectionMenu.DropDownItems.Add(saveProfile);
            connectionMenu.DropDownItems.Add(loadProfile);

            var launchFirmware = new ToolStripMenuItem("Launch Firmware");
            launchFirmware.Click += (_, __) => LaunchFirmware();
            var loadBvm = new ToolStripMenuItem("Load BVM/HEX");
            loadBvm.Click += (_, __) => LoadBvm();
            var stepOnce = new ToolStripMenuItem("Step Once");
            stepOnce.Click += async (_, __) => await StepOnceAsync();
            var autoStep = new ToolStripMenuItem("Auto Step") { CheckOnClick = true };
            autoStep.CheckedChanged += async (_, __) =>
            {
                _autoStepCheckBox.Checked = autoStep.Checked;
                await ToggleAutoStepAsync();
            };
            _autoStepCheckBox.CheckedChanged += (_, __) => autoStep.Checked = _autoStepCheckBox.Checked;
            var injectPatch = new ToolStripMenuItem("Inject Patch");
            injectPatch.Click += (_, __) => InjectPatch();
            var openLogs = new ToolStripMenuItem("Open Logs Folder");
            openLogs.Click += (_, __) => OpenPath(_logDirectory);

            programMenu.DropDownItems.Add(launchFirmware);
            programMenu.DropDownItems.Add(loadBvm);
            programMenu.DropDownItems.Add(stepOnce);
            programMenu.DropDownItems.Add(autoStep);
            programMenu.DropDownItems.Add(new ToolStripSeparator());
            programMenu.DropDownItems.Add(injectPatch);
            programMenu.DropDownItems.Add(openLogs);

            windowMenu.DropDownItems.Add(MakeDetachMenuItem("Detach Pins", dataTabs, pinsTab));
            windowMenu.DropDownItems.Add(MakeDetachMenuItem("Detach Analog", dataTabs, analogTab));
            windowMenu.DropDownItems.Add(MakeDetachMenuItem("Detach Pin Graph", dataTabs, graphTab));
            windowMenu.DropDownItems.Add(new ToolStripSeparator());
            windowMenu.DropDownItems.Add(MakeDetachMenuItem("Detach Logs", logTabs, logTabs.TabPages[0]));
            windowMenu.DropDownItems.Add(MakeDetachMenuItem("Detach Serial", logTabs, logTabs.TabPages[1]));
            windowMenu.DropDownItems.Add(MakeDetachMenuItem("Detach Telemetry", logTabs, logTabs.TabPages[3]));
            windowMenu.DropDownItems.Add(MakeDetachMenuItem("Detach Trace", logTabs, logTabs.TabPages[^1]));

            var openReadme = new ToolStripMenuItem("Open README");
            openReadme.Click += (_, __) =>
            {
                var readme = Path.Combine("tools", "RobotWinFirmwareMonitor", "README.md");
                OpenPath(readme);
            };
            var about = new ToolStripMenuItem("About");
            about.Click += (_, __) => MessageBox.Show(
                "RobotWinFirmwareMonitor\nFirmware analysis & control console.",
                "About",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            helpMenu.DropDownItems.Add(openReadme);
            helpMenu.DropDownItems.Add(about);

            AttachTabContextMenu(dataTabs);
            AttachTabContextMenu(logTabs);
            EnableTabReorder(dataTabs);
            EnableTabReorder(logTabs);
        }

        private TabPage BuildPreferencesTab()
        {
            var page = new TabPage("Preferences");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var profileGroup = new GroupBox { Text = "Profiles", Dock = DockStyle.Fill };
            var profileLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3,
                Padding = new Padding(6)
            };
            profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            profileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            profileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            profileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

            _profileNameTextBox = CreateInputTextBox(0);
            _profileNameTextBox.Dock = DockStyle.Fill;
            _profileCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _saveProfileButton = new Button { Text = "Save", Dock = DockStyle.Fill };
            _loadProfileButton = new Button { Text = "Load", Dock = DockStyle.Fill };
            _deleteProfileButton = new Button { Text = "Delete", Dock = DockStyle.Fill };

            profileLayout.Controls.Add(MakePanelLabel("Profile Name"), 0, 0);
            profileLayout.Controls.Add(_profileNameTextBox, 1, 0);
            profileLayout.Controls.Add(_saveProfileButton, 2, 0);
            profileLayout.Controls.Add(MakePanelLabel("Saved Profiles"), 0, 1);
            profileLayout.Controls.Add(_profileCombo, 1, 1);
            profileLayout.Controls.Add(_loadProfileButton, 2, 1);
            profileLayout.Controls.Add(MakePanelLabel(" "), 0, 2);
            profileLayout.Controls.Add(_deleteProfileButton, 2, 2);
            profileGroup.Controls.Add(profileLayout);

            var behaviorGroup = new GroupBox { Text = "Behavior", Dock = DockStyle.Fill };
            var behaviorLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(6)
            };
            behaviorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            behaviorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            behaviorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            behaviorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            behaviorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            _prefAutoConnect = new CheckBox { Text = "Auto Connect on Startup", Dock = DockStyle.Fill };
            _prefAutoTraceTail = new CheckBox { Text = "Auto Tail Trace After Connect", Dock = DockStyle.Fill };
            _prefConnectionMode = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _prefConnectionMode.Items.AddRange(new object[] { "Connect to running", "Launch on connect" });
            _prefConnectionMode.SelectedIndex = 0;
            _prefTargetProgramCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _prefTargetProgramCombo.Items.AddRange(new object[] { "Firmware Host", "Unity" });
            _prefTargetProgramCombo.SelectedIndex = 0;

            behaviorLayout.Controls.Add(_prefAutoConnect, 0, 0);
            behaviorLayout.SetColumnSpan(_prefAutoConnect, 2);
            behaviorLayout.Controls.Add(_prefAutoTraceTail, 0, 1);
            behaviorLayout.SetColumnSpan(_prefAutoTraceTail, 2);
            behaviorLayout.Controls.Add(MakePanelLabel("Connection Mode"), 0, 2);
            behaviorLayout.Controls.Add(_prefConnectionMode, 1, 2);
            behaviorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            behaviorLayout.Controls.Add(MakePanelLabel("Target Program"), 0, 3);
            behaviorLayout.Controls.Add(_prefTargetProgramCombo, 1, 3);
            behaviorGroup.Controls.Add(behaviorLayout);

            var infoGroup = new GroupBox { Text = "Info", Dock = DockStyle.Fill };
            var infoLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Profiles are saved under logs/RobotWinFirmwareMonitor/profiles.\n" +
                       "Save is enabled only after a successful connection.",
                ForeColor = UiTextColor
            };
            infoGroup.Controls.Add(infoLabel);

            layout.Controls.Add(profileGroup, 0, 0);
            layout.Controls.Add(behaviorGroup, 0, 1);
            layout.Controls.Add(infoGroup, 0, 2);
            page.Controls.Add(layout);

            _saveProfileButton.Click += (_, __) => SaveProfile();
            _loadProfileButton.Click += (_, __) => LoadSelectedProfile();
            _deleteProfileButton.Click += (_, __) => DeleteSelectedProfile();
            _prefConnectionMode.SelectedIndexChanged += (_, __) =>
            {
                _launchOnConnect = _prefConnectionMode.SelectedIndex == 1;
            };
            _prefTargetProgramCombo.SelectedIndexChanged += (_, __) => SyncTargetProgram(_prefTargetProgramCombo.SelectedIndex);

            LoadProfilesList();
            return page;
        }

        private TabPage BuildHelpTab()
        {
            var page = new TabPage("Help");
            _helpTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                BackColor = UiInputBackColor,
                ForeColor = UiTextColor,
                Text =
                    "RobotWinFirmwareMonitor Help\n\n" +
                    "Connection:\n" +
                    "- Use Connection > Connect (Running) to attach to a running firmware host.\n" +
                    "- Use Connection > Connect + Launch to start the host and attach.\n\n" +
                    "Running Host:\n" +
                    "- Use the Running Host dropdown to attach by PID (FirmwareHost only).\n\n" +
                    "Profiles:\n" +
                    "- Save profiles after connecting. They store pipe, board, paths, and timing settings.\n\n" +
                    "Windows:\n" +
                    "- Right-click any tab to detach it into a new window.\n"
            };
            page.Controls.Add(_helpTextBox);
            return page;
        }

        private void InitializeData()
        {
            _pinsGrid.AutoGenerateColumns = false;
            _pinsGrid.AllowUserToAddRows = false;
            _pinsGrid.AllowUserToDeleteRows = false;
            _pinsGrid.RowHeadersVisible = false;
            _pinsGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            ConfigureGrid(_pinsGrid);

            _pinsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Pin",
                DataPropertyName = nameof(PinRow.Pin),
                Width = 50,
                ReadOnly = true
            });
            _pinsGrid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                HeaderText = "Input",
                DataPropertyName = nameof(PinRow.Input),
                Width = 60
            });
            _pinsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Output",
                DataPropertyName = nameof(PinRow.Output),
                Width = 90,
                ReadOnly = true
            });
            _pinsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Hex",
                DataPropertyName = nameof(PinRow.OutputHex),
                Width = 70,
                ReadOnly = true
            });
            _pinsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Bits",
                DataPropertyName = nameof(PinRow.OutputBits),
                Width = 110,
                ReadOnly = true
            });
            _pinsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Activity",
                DataPropertyName = nameof(PinRow.Activity),
                Width = 120,
                ReadOnly = true
            });

            _analogGrid.AutoGenerateColumns = false;
            _analogGrid.AllowUserToAddRows = false;
            _analogGrid.AllowUserToDeleteRows = false;
            _analogGrid.RowHeadersVisible = false;
            _analogGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            ConfigureGrid(_analogGrid);

            _analogGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Channel",
                DataPropertyName = nameof(AnalogRow.Channel),
                Width = 70,
                ReadOnly = true
            });
            var voltageColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "Voltage",
                DataPropertyName = nameof(AnalogRow.Voltage),
                Width = 90
            };
            voltageColumn.DefaultCellStyle.Format = "0.00";
            _analogGrid.Columns.Add(voltageColumn);
            _analogGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Raw",
                DataPropertyName = nameof(AnalogRow.Raw),
                Width = 70,
                ReadOnly = true
            });
            _analogGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Bits",
                DataPropertyName = nameof(AnalogRow.Bits),
                Width = 120,
                ReadOnly = true
            });

            for (int i = 0; i < 70; i++)
            {
                _pinRows.Add(new PinRow(i));
            }

            for (int i = 0; i < 16; i++)
            {
                var row = new AnalogRow(i) { Voltage = 0f };
                row.UpdateDerived();
                _analogRows.Add(row);
            }

            _pinsGrid.DataSource = _pinRows;
            _analogGrid.DataSource = _analogRows;
            _analogGrid.CellEndEdit += (_, e) => UpdateAnalogRow(e.RowIndex);

            InitializePerfItems();
            InitializeCpuItems();
            InitializeRoutingItems();
            InitializeBitMapItems();
        }

        private void ApplyDefaults()
        {
            if (string.IsNullOrWhiteSpace(_extraArgsTextBox.Text))
            {
                _extraArgsTextBox.Text = $"--log {Path.Combine(_logDirectory, "firmware.log")}";
            }
            if (string.IsNullOrWhiteSpace(_tracePathTextBox.Text))
            {
                _tracePathTextBox.Text = Path.Combine(_logDirectory, "trace.log");
            }
            if (_prefConnectionMode != null && _prefConnectionMode.SelectedIndex < 0)
            {
                _prefConnectionMode.SelectedIndex = 0;
            }
            _launchOnConnect = _prefConnectionMode != null && _prefConnectionMode.SelectedIndex == 1;
            if (_targetProgramCombo != null && _targetProgramCombo.SelectedIndex < 0)
            {
                _targetProgramCombo.SelectedIndex = 0;
            }
            if (_prefTargetProgramCombo != null && _prefTargetProgramCombo.SelectedIndex < 0)
            {
                _prefTargetProgramCombo.SelectedIndex = 0;
            }
            SyncTargetProgram(_targetProgramCombo?.SelectedIndex ?? 0);
            LoadProfilesList();
            RefreshRunningHosts();
            UpdateInjectPreview();
        }

        private void StartResourceMonitor()
        {
            if (_resourceTimer == null) return;
            if (!_resourceTimer.Enabled)
            {
                UpdateResourceMonitor();
                _resourceTimer.Start();
            }
        }

        private void StopResourceMonitor()
        {
            if (_resourceTimer == null) return;
            _resourceTimer.Stop();
        }

        private void UpdateResourceMonitor()
        {
            if (IsDisposed) return;
            var sample = CollectResourceSample();
            lock (_resourceLock)
            {
                _resourceSamples.Add(sample);
                if (_resourceSamples.Count > ResourceHistoryDepth)
                {
                    _resourceSamples.RemoveRange(0, _resourceSamples.Count - ResourceHistoryDepth);
                }
            }
            _latestResourceSample = sample;

            _resourceCpuLabel.Text = $"CPU: {sample.CpuPercent:F1}%";
            _resourceRamLabel.Text = $"RAM: {sample.RamPercent:F1}% ({sample.RamUsedGb:F2}/{sample.RamTotalGb:F2} GB)";
            _resourceAppMemLabel.Text = $"App Memory: {sample.AppMemMb:F1} MB ({sample.AppMemPercent:F1}%)";
            _resourceNetLabel.Text = $"Network: {sample.NetPercent:F1}% ({sample.NetMbps:F2} Mbps)";
            _resourceGraphPanel.Invalidate();
        }

        private ResourceSample CollectResourceSample()
        {
            double cpuPercent = GetCpuUsagePercent();
            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref memStatus))
            {
                memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            }
            double totalRamMb = memStatus.ullTotalPhys / (1024.0 * 1024.0);
            double usedRamMb = (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024.0 * 1024.0);
            double ramPercent = memStatus.dwMemoryLoad;
            double ramUsedGb = usedRamMb / 1024.0;
            double ramTotalGb = totalRamMb / 1024.0;

            double appMemMb;
            using (var proc = Process.GetCurrentProcess())
            {
                appMemMb = proc.WorkingSet64 / (1024.0 * 1024.0);
            }
            double appMemPercent = totalRamMb > 0 ? appMemMb / totalRamMb * 100.0 : 0;

            long totalSpeedBits;
            ulong totalBytes = GetTotalNetworkBytes(out totalSpeedBits);
            long nowTicks = DateTime.UtcNow.Ticks;
            double netMbps = 0;
            double netPercent = 0;
            if (_lastNetTicks != 0 && totalBytes >= _lastNetBytes)
            {
                double seconds = (nowTicks - _lastNetTicks) / (double)TimeSpan.TicksPerSecond;
                if (seconds > 0)
                {
                    ulong deltaBytes = totalBytes - _lastNetBytes;
                    double bitsPerSec = (deltaBytes * 8.0) / seconds;
                    netMbps = bitsPerSec / 1_000_000.0;
                    if (totalSpeedBits > 0)
                    {
                        netPercent = Math.Clamp(bitsPerSec / totalSpeedBits * 100.0, 0, 100);
                    }
                    else
                    {
                        _netMaxMbps = Math.Max(_netMaxMbps, netMbps);
                        netPercent = _netMaxMbps > 0 ? Math.Clamp(netMbps / _netMaxMbps * 100.0, 0, 100) : 0;
                    }
                }
            }
            _lastNetBytes = totalBytes;
            _lastNetTicks = nowTicks;

            return new ResourceSample(DateTime.Now, cpuPercent, ramPercent, appMemPercent, netPercent, appMemMb, ramUsedGb, ramTotalGb, netMbps);
        }

        private double GetCpuUsagePercent()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
            {
                return 0;
            }
            ulong idleTime = idle.ToUInt64();
            ulong kernelTime = kernel.ToUInt64();
            ulong userTime = user.ToUInt64();
            if (_lastKernelTime == 0 && _lastUserTime == 0)
            {
                _lastIdleTime = idleTime;
                _lastKernelTime = kernelTime;
                _lastUserTime = userTime;
                return 0;
            }

            ulong kernelDelta = kernelTime - _lastKernelTime;
            ulong userDelta = userTime - _lastUserTime;
            ulong idleDelta = idleTime - _lastIdleTime;
            ulong total = kernelDelta + userDelta;
            _lastIdleTime = idleTime;
            _lastKernelTime = kernelTime;
            _lastUserTime = userTime;
            if (total == 0) return 0;
            double cpu = (1.0 - (double)idleDelta / total) * 100.0;
            return Math.Clamp(cpu, 0, 100);
        }

        private ulong GetTotalNetworkBytes(out long totalSpeedBits)
        {
            totalSpeedBits = 0;
            ulong totalBytes = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }
                totalSpeedBits += nic.Speed;
                var stats = nic.GetIPStatistics();
                totalBytes += (ulong)stats.BytesReceived + (ulong)stats.BytesSent;
            }
            return totalBytes;
        }

        private IReadOnlyList<ResourceSample> GetResourceSamplesSnapshot()
        {
            lock (_resourceLock)
            {
                return _resourceSamples.ToArray();
            }
        }

        private void WireEvents()
        {
            Shown += async (_, __) =>
            {
                Program.LogMessage("ui", "MainForm shown.");
                ApplyStartupOptions();
                if (IsAutoConnectEnabled())
                {
                    if (IsUnityTarget())
                    {
                        StartUnityAutoConnect();
                    }
                    else
                    {
                        await ConnectWithLaunchAsync(_launchOnConnect);
                    }
                }
                else if (IsUnityTarget())
                {
                    await ConnectUnityAsync().ConfigureAwait(false);
                }
                StartResourceMonitor();
            };
            FormClosing += (_, __) =>
            {
                Program.LogMessage("ui", "MainForm closing.");
                StopResourceMonitor();
                StopAutoStep();
                StopTraceTail();
                StopUnityAutoConnect();
                DisconnectUnity();
                _client.Disconnect();
            };
            FormClosed += (_, __) => Program.LogMessage("ui", "MainForm closed.");
            _client.LogReceived += OnFirmwareLogReceived;
            if (_prefAutoConnect != null)
            {
                _prefAutoConnect.CheckedChanged += (_, __) =>
                {
                    if (IsAutoConnectEnabled())
                    {
                        if (IsUnityTarget() && !_connected)
                        {
                            StartUnityAutoConnect();
                        }
                    }
                    else
                    {
                        StopUnityAutoConnect();
                    }
                };
            }
        }

        private void ApplyStartupOptions()
        {
            if (_startupOptionsApplied) return;
            _startupOptionsApplied = true;

            if (_startupOptions.AutoConnect.HasValue && _prefAutoConnect != null)
            {
                _prefAutoConnect.Checked = _startupOptions.AutoConnect.Value;
            }
            if (_startupOptions.TargetProgramIndex.HasValue)
            {
                SyncTargetProgram(_startupOptions.TargetProgramIndex.Value);
            }
        }

        private async Task ConnectAsync()
        {
            if (_connected)
            {
                AppendLine(_logTextBox, "Already connected.");
                return;
            }
            if (IsUnityTarget())
            {
                await ConnectUnityAsync().ConfigureAwait(false);
                return;
            }

            _client.Configure(_pipeNameTextBox.Text.Trim());
            _client.BoardId = _boardIdTextBox.Text.Trim();
            _client.BoardProfile = _boardProfileTextBox.Text.Trim();
            _client.ExtraLaunchArguments = _extraArgsTextBox.Text.Trim();

            UpdateStatus("Connecting...", Color.DarkGoldenrod);
            try
            {
                await _client.ConnectAsync().ConfigureAwait(false);
                SafeUi(() => UpdateConnectionState(true));
            }
            catch (Exception ex)
            {
                SafeUi(() =>
                {
                    UpdateConnectionState(false);
                    AppendLine(_logTextBox, $"Connect failed: {ex.Message}");
                });
                if (!_launchOnConnect && !IsUnityTarget())
                {
                    string? baseUrl = await FindUnityBridgeAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(baseUrl))
                    {
                        SafeUi(() =>
                        {
                            AppendLine(_logTextBox, "Unity bridge detected. Switching target to Unity.");
                            if (_targetProgramCombo != null)
                            {
                                _targetProgramCombo.SelectedIndex = 1;
                            }
                        });
                        await ConnectUnityAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        private void Disconnect()
        {
            StopAutoStep();
            StopUnityAutoConnect();
            DisconnectUnity();
            _client.Disconnect();
            UpdateConnectionState(false);
        }

        private bool IsUnityTarget()
        {
            return _isUnityTarget;
        }

        private bool IsAutoConnectEnabled()
        {
            if (_prefAutoConnect == null) return false;
            return InvokeIfRequired(() => _prefAutoConnect.Checked);
        }

        private bool EnsurePipeMode(string action)
        {
            if (!IsUnityTarget()) return true;
            AppendLine(_logTextBox, $"{action} unavailable in Unity bridge mode.");
            return false;
        }

        private async Task ConnectUnityAsync()
        {
            bool connected = await TryConnectUnityAsync(true).ConfigureAwait(false);
            if (!connected && IsAutoConnectEnabled())
            {
                StartUnityAutoConnect();
            }
        }

        private async Task<bool> TryConnectUnityAsync(bool logIfMissing)
        {
            if (_connected)
            {
                if (logIfMissing)
                {
                    AppendLine(_logTextBox, "Already connected.");
                }
                return true;
            }

            StopAutoStep();
            DisconnectUnity();
            _unitySerialInitialized = false;
            _unityLastSerialLength = 0;
            if (logIfMissing)
            {
                SafeUi(() => UpdateStatus("Connecting (Unity)...", Color.DarkGoldenrod));
            }

            string? baseUrl = await FindUnityBridgeAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                if (logIfMissing)
                {
                    SafeUi(() =>
                    {
                        UpdateConnectionState(false);
                        UpdateStatus("Unity bridge not found", Color.DarkRed);
                        AppendLine(_logTextBox, "Unity bridge not found. Start Play Mode or enable RemoteCommandServer.");
                    });
                }
                return false;
            }

            _unityBaseUrl = baseUrl;
            _unityCts = new CancellationTokenSource();
            SafeUi(() =>
            {
                UpdateConnectionState(true);
                UpdateStatus("Connected (Unity Bridge)", Color.DarkGreen);
                AppendLine(_logTextBox, $"Unity bridge connected: {_unityBaseUrl}");
            });
            _ = Task.Run(() => PollUnityBridgeAsync(_unityCts.Token));
            return true;
        }

        private void StartUnityAutoConnect()
        {
            if (!IsUnityTarget() || _connected || !IsAutoConnectEnabled())
            {
                return;
            }
            StopUnityAutoConnect();
            if (Interlocked.Exchange(ref _unityAutoConnectActive, 1) == 1)
            {
                return;
            }

            _unityAutoConnectCts = new CancellationTokenSource();
            var token = _unityAutoConnectCts.Token;

            _ = Task.Run(async () =>
            {
                SafeUi(() => UpdateStatus("Waiting for Unity bridge...", Color.DarkGoldenrod));
                try
                {
                    while (!token.IsCancellationRequested && !_connected && IsUnityTarget() && IsAutoConnectEnabled())
                    {
                        if (await TryConnectUnityAsync(false).ConfigureAwait(false))
                        {
                            return;
                        }
                        await Task.Delay(1500, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    Interlocked.Exchange(ref _unityAutoConnectActive, 0);
                }
            }, token);
        }

        private void StopUnityAutoConnect()
        {
            if (_unityAutoConnectCts != null)
            {
                _unityAutoConnectCts.Cancel();
                _unityAutoConnectCts.Dispose();
                _unityAutoConnectCts = null;
            }
            Interlocked.Exchange(ref _unityAutoConnectActive, 0);
        }

        private void DisconnectUnity()
        {
            if (_unityCts == null) return;
            _unityCts.Cancel();
            _unityCts.Dispose();
            _unityCts = null;
            _unityBaseUrl = null;
            _unitySerialInitialized = false;
            _unityLastSerialLength = 0;
        }

        private async Task<string?> FindUnityBridgeAsync()
        {
            for (int i = 0; i < UnityBridgePortAttempts; i++)
            {
                int port = UnityBridgeBasePort + i;
                string baseUrl = $"http://localhost:{port}/";
                try
                {
                    using var response = await _unityHttp.GetAsync($"{baseUrl}bridge").ConfigureAwait(false);
                    if (response.IsSuccessStatusCode) return baseUrl;
                }
                catch
                {
                }
            }
            return null;
        }

        private async Task PollUnityBridgeAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    string? baseUrl = _unityBaseUrl;
                    if (string.IsNullOrWhiteSpace(baseUrl)) break;
                    var bridgeTask = _unityHttp.GetStringAsync($"{baseUrl}bridge");
                    var telemetryTask = _unityHttp.GetStringAsync($"{baseUrl}telemetry");
                    await Task.WhenAll(bridgeTask, telemetryTask).ConfigureAwait(false);
                    ApplyUnityBridgeSnapshot(bridgeTask.Result, telemetryTask.Result);
                    await Task.Delay(500, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SafeUi(() =>
                    {
                        UpdateConnectionState(false);
                        UpdateStatus("Unity bridge lost", Color.DarkRed);
                        AppendLine(_logTextBox, $"Unity bridge error: {ex.Message}");
                    });
                    SafeUi(() =>
                    {
                        if (IsAutoConnectEnabled())
                        {
                            StartUnityAutoConnect();
                        }
                    });
                    break;
                }
            }
        }

        private void ApplyUnityBridgeSnapshot(string bridgeJson, string telemetryJson)
        {
            SafeUi(() =>
            {
                string pipeName = string.Empty;
                string mode = string.Empty;
                bool useFirmware = false;
                bool running = false;
                long tick = 0;
                double time = 0;

                try
                {
                    using var bridgeDoc = JsonDocument.Parse(bridgeJson);
                    if (bridgeDoc.RootElement.TryGetProperty("firmware_pipe", out var pipeProp))
                    {
                        pipeName = pipeProp.GetString() ?? string.Empty;
                    }
                    if (bridgeDoc.RootElement.TryGetProperty("use_firmware", out var fwProp))
                    {
                        useFirmware = fwProp.ValueKind == JsonValueKind.True;
                    }
                    if (bridgeDoc.RootElement.TryGetProperty("firmware_host", out var hostProp))
                    {
                        if (hostProp.TryGetProperty("pipe", out var pipeProp2))
                        {
                            pipeName = pipeProp2.GetString() ?? pipeName;
                        }
                        if (hostProp.TryGetProperty("mode", out var modeProp))
                        {
                            mode = modeProp.GetString() ?? string.Empty;
                        }
                    }
                }
                catch
                {
                }

                try
                {
                    using var telemetryDoc = JsonDocument.Parse(telemetryJson);
                    var telemetryRoot = telemetryDoc.RootElement;
                    if (telemetryRoot.TryGetProperty("running", out var runningProp))
                    {
                        running = runningProp.ValueKind == JsonValueKind.True;
                    }
                    if (telemetryRoot.TryGetProperty("tick", out var tickProp) &&
                        tickProp.TryGetInt64(out var tickValue))
                    {
                        tick = tickValue;
                    }
                    if (telemetryRoot.TryGetProperty("time", out var timeProp) &&
                        timeProp.TryGetDouble(out var timeValue))
                    {
                        time = timeValue;
                    }
                    ApplyUnityBridgeTelemetry(telemetryRoot, tick, time);
                }
                catch
                {
                }

                if (!string.IsNullOrWhiteSpace(pipeName) && _pipeNameTextBox != null)
                {
                    _pipeNameTextBox.Text = pipeName;
                }

                string runLabel = running ? "running" : "idle";
                string fwLabel = useFirmware ? "fw:on" : "fw:off";
                string modeSuffix = string.IsNullOrWhiteSpace(mode) ? string.Empty : $" | {mode}";
                _activityLabel.Text = $"Unity: {runLabel} | tick {tick} | t={time:F2}s | {fwLabel}{modeSuffix}";
            });
        }

        private void ApplyUnityBridgeTelemetry(JsonElement telemetryRoot, long tick, double time)
        {
            string desiredBoardId = _boardIdTextBox?.Text.Trim() ?? string.Empty;
            string selectedBoardId = string.Empty;

            if (telemetryRoot.TryGetProperty("firmware", out var perfArray) &&
                TrySelectBoardElement(perfArray, desiredBoardId, out var perfBoard, out var perfBoardId))
            {
                selectedBoardId = perfBoardId;
                if (perfBoard.TryGetProperty("metrics", out var metrics))
                {
                    UpdatePerfFromUnity(metrics, tick, time);
                    UpdateRoutingFromUnityMetrics(metrics);
                }
            }

            if (telemetryRoot.TryGetProperty("firmware_debug", out var debugArray) &&
                TrySelectBoardElement(debugArray, desiredBoardId, out var debugBoard, out var debugBoardId))
            {
                if (string.IsNullOrWhiteSpace(selectedBoardId))
                {
                    selectedBoardId = debugBoardId;
                }
                if (debugBoard.TryGetProperty("counters", out var counters))
                {
                    UpdateCpuFromUnity(counters);
                    UpdateRoutingFromUnityDebug(counters);
                }
            }

            if (telemetryRoot.TryGetProperty("firmware_bits", out var bitsArray) &&
                TrySelectBoardElement(bitsArray, desiredBoardId, out var bitsBoard, out var bitsBoardId))
            {
                if (string.IsNullOrWhiteSpace(selectedBoardId))
                {
                    selectedBoardId = bitsBoardId;
                }
                if (bitsBoard.TryGetProperty("fields", out var fields) &&
                    fields.ValueKind == JsonValueKind.Array)
                {
                    UpdateBitMapFromUnity(fields);
                }
            }

            if (telemetryRoot.TryGetProperty("firmware_pins", out var pinsArray) &&
                TrySelectBoardElement(pinsArray, desiredBoardId, out var pinsBoard, out var pinsBoardId))
            {
                if (string.IsNullOrWhiteSpace(selectedBoardId))
                {
                    selectedBoardId = pinsBoardId;
                }
                if (pinsBoard.TryGetProperty("outputs", out var outputs) &&
                    outputs.ValueKind == JsonValueKind.Array)
                {
                    UpdatePinsFromUnity(outputs, (ulong)tick);
                }
            }

            if (telemetryRoot.TryGetProperty("firmware_analog", out var analogArray) &&
                TrySelectBoardElement(analogArray, desiredBoardId, out var analogBoard, out var analogBoardId))
            {
                if (string.IsNullOrWhiteSpace(selectedBoardId))
                {
                    selectedBoardId = analogBoardId;
                }
                if (analogBoard.TryGetProperty("values", out var values) &&
                    values.ValueKind == JsonValueKind.Array)
                {
                    UpdateAnalogFromUnity(values);
                }
            }

            if (telemetryRoot.TryGetProperty("serial", out var serialObj) &&
                serialObj.ValueKind == JsonValueKind.Object)
            {
                UpdateSerialFromUnity(serialObj);
            }

            if (string.IsNullOrWhiteSpace(desiredBoardId) &&
                !string.IsNullOrWhiteSpace(selectedBoardId) &&
                _boardIdTextBox != null)
            {
                _boardIdTextBox.Text = selectedBoardId;
            }
        }

        private static bool TrySelectBoardElement(JsonElement array, string desiredBoardId, out JsonElement element, out string selectedId)
        {
            element = default;
            selectedId = string.Empty;
            if (array.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in array.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idProp)) continue;
                string id = idProp.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(desiredBoardId) &&
                    string.Equals(desiredBoardId, id, StringComparison.OrdinalIgnoreCase))
                {
                    element = item;
                    selectedId = id;
                    return true;
                }
            }

            foreach (var item in array.EnumerateArray())
            {
                element = item;
                if (item.TryGetProperty("id", out var idProp))
                {
                    selectedId = idProp.GetString() ?? string.Empty;
                }
                return true;
            }

            return false;
        }

        private void UpdatePerfFromUnity(JsonElement metrics, long tick, double time)
        {
            SetPerfValue("step_sequence", tick.ToString());
            SetPerfValue("tick_count", tick.ToString());
            long outputUs = (long)Math.Round(time * 1_000_000.0);
            SetPerfValue("output_timestamp_us", outputUs.ToString(System.Globalization.CultureInfo.InvariantCulture));

            SetPerfValue("cycles", ReadMetric(metrics, "cycles"));
            SetPerfValue("adc_samples", ReadMetric(metrics, "adc_samples"));
            for (int i = 0; i < 4; i++)
            {
                SetPerfValue($"uart_tx_{i}", ReadMetric(metrics, $"uart_tx{i}"));
            }
            for (int i = 0; i < 4; i++)
            {
                SetPerfValue($"uart_rx_{i}", ReadMetric(metrics, $"uart_rx{i}"));
            }
            SetPerfValue("spi_transfers", ReadMetric(metrics, "spi_transfers"));
            SetPerfValue("twi_transfers", ReadMetric(metrics, "twi_transfers"));
            SetPerfValue("wdt_resets", ReadMetric(metrics, "wdt_resets"));
            SetPerfValue("dropped_outputs", ReadMetric(metrics, "drops"));
        }

        private void UpdateCpuFromUnity(JsonElement counters)
        {
            SetCpuValue("pc", $"0x{ReadUInt(counters, "pc"):X4}");
            SetCpuValue("sp", $"0x{ReadUInt(counters, "sp"):X4}");
            SetCpuValue("sreg", $"0x{ReadUInt(counters, "sreg"):X2}");
            SetCpuValue("flash_bytes", ReadMetric(counters, "flash_bytes"));
            SetCpuValue("sram_bytes", ReadMetric(counters, "sram_bytes"));
            SetCpuValue("eeprom_bytes", ReadMetric(counters, "eeprom_bytes"));
            SetCpuValue("io_bytes", ReadMetric(counters, "io_bytes"));
            SetCpuValue("cpu_hz", ReadMetric(counters, "cpu_hz"));
            SetCpuValue("stack_high_water", ReadMetric(counters, "stack_high_water"));
            SetCpuValue("heap_top", $"0x{ReadUInt(counters, "heap_top"):X4}");
            SetCpuValue("stack_min", $"0x{ReadUInt(counters, "stack_min"):X4}");
            SetCpuValue("data_segment_end", $"0x{ReadUInt(counters, "data_segment_end"):X4}");
            SetCpuValue("stack_overflows", ReadMetric(counters, "stack_overflows"));
            SetCpuValue("invalid_mem_accesses", ReadMetric(counters, "invalid_mem_accesses"));
            SetCpuValue("interrupt_count", ReadMetric(counters, "interrupt_count"));
            SetCpuValue("interrupt_latency_max", ReadMetric(counters, "interrupt_latency_max"));
            SetCpuValue("timing_violations", ReadMetric(counters, "timing_violations"));
            SetCpuValue("critical_section_cycles", ReadMetric(counters, "critical_section_cycles"));
            SetCpuValue("sleep_cycles", ReadMetric(counters, "sleep_cycles"));
            SetCpuValue("flash_access_cycles", ReadMetric(counters, "flash_access_cycles"));
            SetCpuValue("uart_overflows", ReadMetric(counters, "uart_overflows"));
            SetCpuValue("timer_overflows", ReadMetric(counters, "timer_overflows"));
            SetCpuValue("brown_out_resets", ReadMetric(counters, "brown_out_resets"));
        }

        private void UpdateRoutingFromUnityMetrics(JsonElement metrics)
        {
            ulong txSum = 0;
            ulong rxSum = 0;
            for (int i = 0; i < 4; i++)
            {
                txSum += ReadULong(metrics, $"uart_tx{i}");
                rxSum += ReadULong(metrics, $"uart_rx{i}");
            }
            SetRoutingValue("serial_bytes_tx", txSum.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SetRoutingValue("serial_bytes_rx", rxSum.ToString(System.Globalization.CultureInfo.InvariantCulture));
            UpdateRoutingStatus("Unity metrics");
        }

        private void UpdateRoutingFromUnityDebug(JsonElement counters)
        {
            SetRoutingValue("gpio_state_changes", ReadMetric(counters, "gpio_state_changes"));
            SetRoutingValue("pwm_cycles", ReadMetric(counters, "pwm_cycles"));
            SetRoutingValue("i2c_transactions", ReadMetric(counters, "i2c_transactions"));
            SetRoutingValue("spi_transactions", ReadMetric(counters, "spi_transactions"));
            UpdateRoutingStatus("Unity debug");
        }

        private void UpdateBitMapFromUnity(JsonElement fields)
        {
            if (fields.ValueKind != JsonValueKind.Array) return;

            foreach (var field in fields.EnumerateArray())
            {
                if (!field.TryGetProperty("name", out var nameProp)) continue;
                string name = nameProp.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;

                string offset = ReadMetric(field, "offset");
                string width = ReadMetric(field, "width");
                string bits = field.TryGetProperty("bits", out var bitsProp) ? bitsProp.GetString() ?? string.Empty : string.Empty;
                string value = ReadMetric(field, "value");

                if (!_bitMapItems.TryGetValue(name, out var item))
                {
                    item = new ListViewItem(name);
                    item.SubItems.Add(offset);
                    item.SubItems.Add(width);
                    item.SubItems.Add(bits);
                    item.SubItems.Add(value);
                    _bitMapList.Items.Add(item);
                    _bitMapItems[name] = item;
                }
                else
                {
                    item.SubItems[1].Text = offset;
                    item.SubItems[2].Text = width;
                    item.SubItems[3].Text = bits;
                    item.SubItems[4].Text = value;
                }
            }
        }

        private void UpdatePinsFromUnity(JsonElement outputs, ulong sequence)
        {
            if (outputs.ValueKind != JsonValueKind.Array) return;

            int[] snapshot = new int[_pinRows.Count];
            Array.Fill(snapshot, -1);

            int index = 0;

            foreach (var item in outputs.EnumerateArray())
            {
                if (index >= _pinRows.Count) break;
                int newValue = ParsePinValue(item);
                snapshot[index] = newValue;

                var row = _pinRows[index];
                int oldValue = row.OutputValue;
                bool changed = newValue != oldValue;

                row.OutputValue = newValue;
                row.Output = FormatOutput(newValue);
                row.OutputHex = FormatHex(newValue);
                row.OutputBits = FormatBits(newValue);
                row.Activity = BuildActivity(newValue, sequence, changed);

                if (changed)
                {
                    AppendLine(_bitOpsTextBox, BuildBitOpLine(sequence, index, oldValue, newValue), 2000);
                }

                index++;
            }

            UpdatePinHistory(snapshot);
        }

        private void UpdateAnalogFromUnity(JsonElement values)
        {
            if (values.ValueKind != JsonValueKind.Array) return;

            int index = 0;
            foreach (var item in values.EnumerateArray())
            {
                if (index >= _analogRows.Count) break;
                float voltage = 0f;
                if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var doubleValue))
                {
                    voltage = ClampVoltage((float)doubleValue);
                }

                var row = _analogRows[index];
                row.Voltage = voltage;
                row.UpdateDerived();
                index++;
            }
        }

        private void UpdateSerialFromUnity(JsonElement serialObj)
        {
            int length = 0;
            bool hasLength = false;
            if (serialObj.TryGetProperty("length", out var lengthProp) &&
                lengthProp.TryGetInt32(out var lengthValue))
            {
                length = lengthValue;
                hasLength = true;
            }

            bool reset = false;
            if (serialObj.TryGetProperty("reset", out var resetProp))
            {
                reset = resetProp.ValueKind == JsonValueKind.True;
            }

            string delta = string.Empty;
            if (serialObj.TryGetProperty("delta", out var deltaProp))
            {
                delta = deltaProp.GetString() ?? string.Empty;
            }

            string buffer = string.Empty;
            if (serialObj.TryGetProperty("buffer", out var bufferProp))
            {
                buffer = bufferProp.GetString() ?? string.Empty;
            }

            if (hasLength && length < _unityLastSerialLength)
            {
                reset = true;
            }

            if (!_unitySerialInitialized || reset)
            {
                if (!string.IsNullOrEmpty(buffer))
                {
                    ReplaceTextWithLimit(_serialTextBox, buffer, 2000);
                }
                else
                {
                    ReplaceTextWithLimit(_serialTextBox, string.Empty, 2000);
                    if (!string.IsNullOrEmpty(delta))
                    {
                        AppendLine(_serialTextBox, delta, 2000);
                    }
                }
                _unitySerialInitialized = true;
            }
            else if (!string.IsNullOrEmpty(delta))
            {
                AppendLine(_serialTextBox, delta, 2000);
            }

            if (hasLength)
            {
                _unityLastSerialLength = length;
            }
        }

        private static int ParsePinValue(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt32(out var number))
                {
                    return number;
                }
                if (value.TryGetInt64(out var longValue))
                {
                    if (longValue > int.MaxValue) return int.MaxValue;
                    if (longValue < int.MinValue) return int.MinValue;
                    return (int)longValue;
                }
            }
            return -1;
        }

        private static string ReadMetric(JsonElement element, string name)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var prop))
            {
                if (prop.TryGetInt64(out var number))
                {
                    return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                if (prop.TryGetDouble(out var floatValue))
                {
                    return floatValue.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                return "0";
            }

            if (element.ValueKind == JsonValueKind.Number)
            {
                if (element.TryGetInt64(out var number))
                {
                    return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                if (element.TryGetDouble(out var floatValue))
                {
                    return floatValue.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            return "0";
        }

        private static uint ReadUInt(JsonElement element, string name)
        {
            ulong value = ReadULong(element, name);
            return value > uint.MaxValue ? uint.MaxValue : (uint)value;
        }

        private static ulong ReadULong(JsonElement element, string name)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var prop))
            {
                if (prop.TryGetUInt64(out var number))
                {
                    return number;
                }
                if (prop.TryGetInt64(out var signed) && signed >= 0)
                {
                    return (ulong)signed;
                }
                if (prop.TryGetDouble(out var floatValue) && floatValue >= 0)
                {
                    return (ulong)floatValue;
                }
            }
            return 0;
        }

        private void LaunchFirmware()
        {
            if (!EnsurePipeMode("Launch")) return;
            string path = _firmwarePathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                AppendLine(_logTextBox, "Firmware EXE not found.");
                return;
            }
            if (!TryPrepareLaunchArguments(out var launchArgs))
            {
                return;
            }
            _client.ExtraLaunchArguments = launchArgs;
            _extraArgsTextBox.Text = launchArgs;
            _client.LaunchFirmware(path);
            AppendLine(_logTextBox, $"Launched firmware: {path}");
        }

        private void LoadBvm()
        {
            if (!EnsurePipeMode("Load firmware")) return;
            if (!_connected)
            {
                AppendLine(_logTextBox, "Connect before loading firmware.");
                return;
            }
            string path = _bvmPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                AppendLine(_logTextBox, "BVM/HEX not found.");
                return;
            }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".hex" || ext == ".ihx")
            {
                AppendLine(_logTextBox, "Firmware host expects .bvm. Convert HEX to BVM before loading.");
                return;
            }
            bool ok = _client.LoadBvmFile(path);
            AppendLine(_logTextBox, ok ? $"Loaded firmware image: {path}" : "Firmware load failed.");
        }

        private async Task StepOnceAsync()
        {
            FirmwareStepRequest? request = null;
            bool connected = InvokeIfRequired(() =>
            {
                if (!EnsurePipeMode("Step"))
                {
                    return false;
                }
                if (!_connected)
                {
                    AppendLine(_logTextBox, "Connect before stepping.");
                    return false;
                }
                CommitEdits();
                request = BuildStepRequest();
                return true;
            });
            if (!connected || request == null) return;
            try
            {
                var result = await Task.Run(() => _client.Step(request)).ConfigureAwait(false);
                SafeUi(() => ApplyStepResult(result));
            }
            catch (Exception ex)
            {
                SafeUi(() => AppendLine(_logTextBox, $"Step failed: {ex.Message}"));
            }
        }

        private async Task ToggleAutoStepAsync()
        {
            if (_autoStepCheckBox.Checked)
            {
                if (!EnsurePipeMode("Auto step"))
                {
                    _autoStepCheckBox.Checked = false;
                    return;
                }
                if (!_connected)
                {
                    _autoStepCheckBox.Checked = false;
                    AppendLine(_logTextBox, "Connect before auto stepping.");
                    return;
                }
                StartAutoStep();
                await Task.CompletedTask.ConfigureAwait(false);
            }
            else
            {
                StopAutoStep();
                await Task.CompletedTask.ConfigureAwait(false);
            }
        }

        private void StartAutoStep()
        {
            if (_stepCts != null) return;
            _stepCts = new CancellationTokenSource();
            var token = _stepCts.Token;
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    await StepOnceAsync().ConfigureAwait(false);
                    int delay = InvokeIfRequired(() => (int)_stepIntervalMsInput.Value);
                    try
                    {
                        await Task.Delay(delay, token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, token);
        }

        private void StopAutoStep()
        {
            if (_stepCts == null) return;
            _stepCts.Cancel();
            _stepCts.Dispose();
            _stepCts = null;
            _autoStepCheckBox.Checked = false;
        }

        private async Task ToggleTraceTailAsync()
        {
            if (_traceCts != null)
            {
                StopTraceTail();
                await Task.CompletedTask.ConfigureAwait(false);
                return;
            }

            string path = _tracePathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                AppendLine(_traceTextBox, "Trace file not found.", TraceMaxLines);
                return;
            }
            if (!IsUnderLogDirectory(path))
            {
                AppendLine(_traceTextBox, $"Trace file must live under {_logDirectory}.", TraceMaxLines);
                return;
            }

            _traceCts = new CancellationTokenSource();
            _traceTailButton.Text = "Stop Trace";
            _ = Task.Run(() => TailTraceFile(path, _traceCts.Token));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private void StopTraceTail()
        {
            if (_traceCts == null) return;
            _traceCts.Cancel();
            _traceCts.Dispose();
            _traceCts = null;
            _traceTailButton.Text = "Tail Trace";
        }

        private void TailTraceFile(string path, CancellationToken token)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                stream.Seek(0, SeekOrigin.End);
                while (!token.IsCancellationRequested)
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        SafeUi(() => AppendTraceLine(line));
                    }
                    Thread.Sleep(200);
                }
            }
            catch (Exception ex)
            {
                SafeUi(() => AppendLine(_traceTextBox, $"Trace tail stopped: {ex.Message}", TraceMaxLines));
            }
        }

        private FirmwareStepRequest BuildStepRequest()
        {
            var pinStates = new int[_pinRows.Count];
            for (int i = 0; i < _pinRows.Count; i++)
            {
                pinStates[i] = _pinRows[i].Input ? 1 : 0;
            }

            var analog = new float[_analogRows.Count];
            for (int i = 0; i < _analogRows.Count; i++)
            {
                analog[i] = _analogRows[i].Voltage;
            }

            return new FirmwareStepRequest
            {
                StepSequence = NextSequence(),
                DeltaMicros = (uint)_deltaMicrosInput.Value,
                PinStates = pinStates,
                AnalogVoltages = analog
            };
        }

        private void ApplyStepResult(FirmwareStepResult result)
        {
            int highCount = 0;
            int pwmCount = 0;
            for (int i = 0; i < result.PinStates.Length && i < _pinRows.Count; i++)
            {
                var row = _pinRows[i];
                int newValue = result.PinStates[i];
                int oldValue = row.OutputValue;
                bool changed = newValue != oldValue;

                row.OutputValue = newValue;
                row.Output = FormatOutput(newValue);
                row.OutputHex = FormatHex(newValue);
                row.OutputBits = FormatBits(newValue);
                row.Activity = BuildActivity(newValue, result.StepSequence, changed);

                if (changed)
                {
                    AppendLine(_bitOpsTextBox, BuildBitOpLine(result.StepSequence, i, oldValue, newValue), 2000);
                }

                if (newValue == 1) highCount++;
                if (newValue > 1 && newValue <= 255) pwmCount++;
            }

            _activityLabel.Text = $"Active outputs: {highCount} high, {pwmCount} pwm";
            UpdatePerf(result);
            UpdateCpu(result);
            UpdateRouting(result);
            UpdateBitMap(result);
            UpdatePinHistory(result.PinStates);

            if (!string.IsNullOrWhiteSpace(result.SerialOutput))
            {
                AppendLine(_serialTextBox, result.SerialOutput, 2000);
            }
        }

        private void UpdatePerf(FirmwareStepResult result)
        {
            SetPerfValue("step_sequence", result.StepSequence.ToString());
            SetPerfValue("tick_count", result.TickCount.ToString());
            SetPerfValue("cycles", result.PerfCounters.Cycles.ToString());
            SetPerfValue("adc_samples", result.PerfCounters.AdcSamples.ToString());
            for (int i = 0; i < result.PerfCounters.UartTxBytes.Length; i++)
            {
                SetPerfValue($"uart_tx_{i}", result.PerfCounters.UartTxBytes[i].ToString());
            }
            for (int i = 0; i < result.PerfCounters.UartRxBytes.Length; i++)
            {
                SetPerfValue($"uart_rx_{i}", result.PerfCounters.UartRxBytes[i].ToString());
            }
            SetPerfValue("spi_transfers", result.PerfCounters.SpiTransfers.ToString());
            SetPerfValue("twi_transfers", result.PerfCounters.TwiTransfers.ToString());
            SetPerfValue("wdt_resets", result.PerfCounters.WdtResets.ToString());
            SetPerfValue("dropped_outputs", result.PerfCounters.DroppedOutputs.ToString());
            SetPerfValue("output_timestamp_us", result.OutputTimestampMicros.ToString());
        }

        private void UpdateCpu(FirmwareStepResult result)
        {
            var debug = result.DebugCounters;
            SetCpuValue("pc", $"0x{debug.ProgramCounter:X4}");
            SetCpuValue("sp", $"0x{debug.StackPointer:X4}");
            SetCpuValue("sreg", $"0x{debug.StatusRegister:X2}");
            SetCpuValue("flash_bytes", debug.FlashBytes.ToString());
            SetCpuValue("sram_bytes", debug.SramBytes.ToString());
            SetCpuValue("eeprom_bytes", debug.EepromBytes.ToString());
            SetCpuValue("io_bytes", debug.IoBytes.ToString());
            SetCpuValue("cpu_hz", debug.CpuHz.ToString());
            SetCpuValue("stack_high_water", debug.StackHighWater.ToString());
            SetCpuValue("heap_top", $"0x{debug.HeapTopAddress:X4}");
            SetCpuValue("stack_min", $"0x{debug.StackMinAddress:X4}");
            SetCpuValue("data_segment_end", $"0x{debug.DataSegmentEnd:X4}");
            SetCpuValue("stack_overflows", debug.StackOverflows.ToString());
            SetCpuValue("invalid_mem_accesses", debug.InvalidMemoryAccesses.ToString());
            SetCpuValue("interrupt_count", debug.InterruptCount.ToString());
            SetCpuValue("interrupt_latency_max", debug.InterruptLatencyMax.ToString());
            SetCpuValue("timing_violations", debug.TimingViolations.ToString());
            SetCpuValue("critical_section_cycles", debug.CriticalSectionCycles.ToString());
            SetCpuValue("sleep_cycles", debug.SleepCycles.ToString());
            SetCpuValue("flash_access_cycles", debug.FlashAccessCycles.ToString());
            SetCpuValue("uart_overflows", debug.UartOverflows.ToString());
            SetCpuValue("timer_overflows", debug.TimerOverflows.ToString());
            SetCpuValue("brown_out_resets", debug.BrownOutResets.ToString());
        }

        private void UpdateRouting(FirmwareStepResult result)
        {
            var debug = result.DebugCounters;
            SetRoutingValue("gpio_state_changes", debug.GpioStateChanges.ToString());
            SetRoutingValue("pwm_cycles", debug.PwmCycles.ToString());
            SetRoutingValue("i2c_transactions", debug.I2cTransactions.ToString());
            SetRoutingValue("spi_transactions", debug.SpiTransactions.ToString());
            SetRoutingValue("serial_bytes_tx", SumArray(result.PerfCounters.UartTxBytes).ToString());
            SetRoutingValue("serial_bytes_rx", SumArray(result.PerfCounters.UartRxBytes).ToString());
            UpdateRoutingStatus("Firmware");
        }

        private void UpdateBitMap(FirmwareStepResult result)
        {
            if (result.DebugBits.Fields.Count == 0)
            {
                return;
            }

            foreach (var field in result.DebugBits.Fields)
            {
                if (!_bitMapItems.TryGetValue(field.Name, out var item))
                {
                    item = new ListViewItem(field.Name);
                    item.SubItems.Add(field.Offset.ToString());
                    item.SubItems.Add(field.Width.ToString());
                    item.SubItems.Add(field.Bits);
                    item.SubItems.Add(field.Value.ToString());
                    _bitMapList.Items.Add(item);
                    _bitMapItems[field.Name] = item;
                }
                else
                {
                    item.SubItems[1].Text = field.Offset.ToString();
                    item.SubItems[2].Text = field.Width.ToString();
                    item.SubItems[3].Text = field.Bits;
                    item.SubItems[4].Text = field.Value.ToString();
                }
            }
        }

        private void UpdatePinHistory(int[] pinStates)
        {
            var snapshot = new byte[_pinRows.Count];
            for (int i = 0; i < snapshot.Length; i++)
            {
                int value = (pinStates != null && i < pinStates.Length) ? pinStates[i] : -1;
                snapshot[i] = value < 0 ? (byte)255 : (byte)Math.Min(255, value);
            }
            lock (_pinHistoryLock)
            {
                _pinHistory.Enqueue(snapshot);
                while (_pinHistory.Count > PinHistoryDepth)
                {
                    _pinHistory.Dequeue();
                }
            }
            _pinGraphPanel.Invalidate();
        }

        private IReadOnlyList<byte[]> GetPinHistorySnapshot()
        {
            lock (_pinHistoryLock)
            {
                return new List<byte[]>(_pinHistory);
            }
        }

        private static ulong SumArray(ulong[] values)
        {
            if (values == null) return 0;
            ulong sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }
            return sum;
        }

        private void UpdateAnalogRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _analogRows.Count) return;
            var row = _analogRows[rowIndex];
            row.Voltage = ClampVoltage(row.Voltage);
            row.UpdateDerived();
        }

        private void CommitEdits()
        {
            _pinsGrid.EndEdit();
            _analogGrid.EndEdit();
            for (int i = 0; i < _analogRows.Count; i++)
            {
                UpdateAnalogRow(i);
            }
        }

        private void UpdateConnectionState(bool connected)
        {
            _connected = connected;
            UpdateStatus(connected ? "Connected" : "Disconnected", connected ? Color.DarkGreen : Color.DarkRed);
        }

        private void UpdateStatus(string text, Color color)
        {
            _statusLabel.Text = text;
            _statusLabel.ForeColor = color;
        }

        private void InitializePerfItems()
        {
            AddPerfItem("step_sequence");
            AddPerfItem("tick_count");
            AddPerfItem("cycles");
            AddPerfItem("adc_samples");
            for (int i = 0; i < 4; i++)
            {
                AddPerfItem($"uart_tx_{i}");
            }
            for (int i = 0; i < 4; i++)
            {
                AddPerfItem($"uart_rx_{i}");
            }
            AddPerfItem("spi_transfers");
            AddPerfItem("twi_transfers");
            AddPerfItem("wdt_resets");
            AddPerfItem("dropped_outputs");
            AddPerfItem("output_timestamp_us");
        }

        private void InitializeCpuItems()
        {
            AddCpuItem("pc");
            AddCpuItem("sp");
            AddCpuItem("sreg");
            AddCpuItem("flash_bytes");
            AddCpuItem("sram_bytes");
            AddCpuItem("eeprom_bytes");
            AddCpuItem("io_bytes");
            AddCpuItem("cpu_hz");
            AddCpuItem("stack_high_water");
            AddCpuItem("heap_top");
            AddCpuItem("stack_min");
            AddCpuItem("data_segment_end");
            AddCpuItem("stack_overflows");
            AddCpuItem("invalid_mem_accesses");
            AddCpuItem("interrupt_count");
            AddCpuItem("interrupt_latency_max");
            AddCpuItem("timing_violations");
            AddCpuItem("critical_section_cycles");
            AddCpuItem("sleep_cycles");
            AddCpuItem("flash_access_cycles");
            AddCpuItem("uart_overflows");
            AddCpuItem("timer_overflows");
            AddCpuItem("brown_out_resets");
        }

        private void InitializeRoutingItems()
        {
            AddRoutingItem("gpio_state_changes");
            AddRoutingItem("pwm_cycles");
            AddRoutingItem("i2c_transactions");
            AddRoutingItem("spi_transactions");
            AddRoutingItem("serial_bytes_tx");
            AddRoutingItem("serial_bytes_rx");
        }

        private void InitializeBitMapItems()
        {
            _bitMapList.Items.Clear();
            _bitMapItems.Clear();
        }

        private void AddPerfItem(string name)
        {
            var item = new ListViewItem(name);
            item.SubItems.Add("0");
            _perfList.Items.Add(item);
            _perfItems[name] = item;
        }

        private void AddCpuItem(string name)
        {
            var item = new ListViewItem(name);
            item.SubItems.Add("0");
            _cpuList.Items.Add(item);
            _cpuItems[name] = item;
        }

        private void AddRoutingItem(string name)
        {
            var item = new ListViewItem(name);
            item.SubItems.Add("0");
            item.SubItems.Add("0");
            _routingList.Items.Add(item);
            _routingItems[name] = item;
        }

        private void SetPerfValue(string name, string value)
        {
            if (_perfItems.TryGetValue(name, out var item))
            {
                item.SubItems[1].Text = value;
            }
        }

        private void SetCpuValue(string name, string value)
        {
            if (_cpuItems.TryGetValue(name, out var item))
            {
                item.SubItems[1].Text = value;
            }
        }

        private void SetRoutingValue(string name, string value)
        {
            if (_routingItems.TryGetValue(name, out var item))
            {
                item.SubItems[1].Text = value;
                if (long.TryParse(value, out var parsed))
                {
                    if (_routingLastValues.TryGetValue(name, out var last))
                    {
                        item.SubItems[2].Text = (parsed - last).ToString();
                    }
                    else
                    {
                        item.SubItems[2].Text = "0";
                    }
                    _routingLastValues[name] = parsed;
                }
                else
                {
                    item.SubItems[2].Text = "--";
                }
            }
        }

        private void ResetRoutingDeltas()
        {
            _routingLastValues.Clear();
            foreach (var item in _routingItems.Values)
            {
                if (item.SubItems.Count > 2)
                {
                    item.SubItems[2].Text = "0";
                }
            }
            UpdateRoutingStatus("Deltas reset");
        }

        private void UpdateRoutingStatus(string source)
        {
            if (_routingStatusLabel == null) return;
            _routingStatusLabel.Text = $"Last update: {DateTime.Now:HH:mm:ss} ({source})";
        }

        private void CopyRoutingMetrics()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Signal\tValue\tDelta");
                foreach (var item in _routingItems.Values)
                {
                    string delta = item.SubItems.Count > 2 ? item.SubItems[2].Text : "--";
                    sb.AppendLine($"{item.Text}\t{item.SubItems[1].Text}\t{delta}");
                }
                Clipboard.SetText(sb.ToString());
                AppendLine(_logTextBox, "Routing metrics copied to clipboard.");
            }
            catch (Exception ex)
            {
                AppendLine(_logTextBox, $"Routing copy failed: {ex.Message}");
            }
        }

        private void OnFirmwareLogReceived(object? sender, FirmwareLogEventArgs e)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} [{e.Level}] {e.BoardId}: {e.Message}";
            SafeUi(() => AppendLine(_logTextBox, line, 2000));
            if (e.Message.StartsWith("TRACE", StringComparison.OrdinalIgnoreCase))
            {
                SafeUi(() => AppendTraceLine(e.Message));
            }
        }

        private void BrowseFile(TextBox target, string filter)
        {
            using var dialog = new OpenFileDialog
            {
                Filter = filter,
                InitialDirectory = string.IsNullOrWhiteSpace(target.Text) ? Environment.CurrentDirectory : Path.GetDirectoryName(target.Text)
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                target.Text = dialog.FileName;
            }
        }

        private static void ConfigureGrid(DataGridView grid)
        {
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 30, 30);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.WhiteSmoke;
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(20, 20, 20);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(210, 230, 250);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(20, 20, 20);
        }

        private bool TryPrepareLaunchArguments(out string launchArgs)
        {
            Directory.CreateDirectory(_logDirectory);
            var tokens = SplitArgs(_extraArgsTextBox.Text);
            string? logPath = null;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (!string.Equals(tokens[i], "--log", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (i + 1 < tokens.Count)
                {
                    logPath = tokens[i + 1];
                }
                break;
            }

            if (string.IsNullOrWhiteSpace(logPath))
            {
                logPath = Path.Combine(_logDirectory, "firmware.log");
                tokens.Add("--log");
                tokens.Add(logPath);
            }

            if (!IsUnderLogDirectory(logPath))
            {
                AppendLine(_logTextBox, $"Log path must live under {_logDirectory}.");
                launchArgs = string.Empty;
                return false;
            }

            launchArgs = JoinArgs(tokens);
            return true;
        }

        private void UpdateInjectPreview()
        {
            if (_injectSizeLabel == null || _injectPreviewTextBox == null || _injectStatusLabel == null) return;
            string path = _injectPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                _injectSizeLabel.Text = "Size: --";
                _injectPreviewTextBox.Clear();
                SetInjectStatus("Status: idle", UiTextColor);
                return;
            }
            if (!File.Exists(path))
            {
                _injectSizeLabel.Text = "Size: --";
                _injectPreviewTextBox.Clear();
                SetInjectStatus("Status: patch file not found", Color.DarkRed);
                return;
            }

            try
            {
                var info = new FileInfo(path);
                _injectSizeLabel.Text = $"Size: {info.Length:N0} bytes";
                byte[] preview = ReadPreviewBytes(path, 96);
                _injectPreviewTextBox.Text = BuildHexPreview(preview);
                SetInjectStatus("Status: patch ready", Color.DarkGreen);
            }
            catch (Exception ex)
            {
                _injectSizeLabel.Text = "Size: --";
                _injectPreviewTextBox.Clear();
                SetInjectStatus($"Status: preview failed ({ex.Message})", Color.DarkRed);
            }
        }

        private void ValidateInjectSettings()
        {
            string path = _injectPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                SetInjectStatus("Status: patch file not found", Color.DarkRed);
                return;
            }
            if (!TryParseAddress(_injectAddressTextBox.Text.Trim(), out uint address))
            {
                SetInjectStatus("Status: invalid address", Color.DarkRed);
                return;
            }
            var memoryType = ParseMemoryType();
            SetInjectStatus($"Status: ready to inject {memoryType} @ 0x{address:X}", Color.DarkGreen);
        }

        private void SetInjectStatus(string message, Color color)
        {
            if (_injectStatusLabel == null) return;
            _injectStatusLabel.Text = message;
            _injectStatusLabel.ForeColor = color;
        }

        private static byte[] ReadPreviewBytes(string path, int maxBytes)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int count = (int)Math.Min(maxBytes, stream.Length);
            var buffer = new byte[count];
            int read = stream.Read(buffer, 0, count);
            if (read < count)
            {
                Array.Resize(ref buffer, read);
            }
            return buffer;
        }

        private static string BuildHexPreview(byte[] data)
        {
            if (data.Length == 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i += 16)
            {
                sb.Append($"{i:X4}: ");
                int lineLen = Math.Min(16, data.Length - i);
                for (int j = 0; j < lineLen; j++)
                {
                    sb.Append($"{data[i + j]:X2} ");
                }
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private void InjectPatch()
        {
            if (!EnsurePipeMode("Inject")) return;
            if (!_connected)
            {
                AppendLine(_logTextBox, "Connect before injecting.");
                SetInjectStatus("Status: connect before injecting", Color.DarkRed);
                return;
            }
            string path = _injectPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                AppendLine(_logTextBox, "Patch file not found.");
                SetInjectStatus("Status: patch file not found", Color.DarkRed);
                return;
            }
            if (!TryParseAddress(_injectAddressTextBox.Text.Trim(), out uint address))
            {
                AppendLine(_logTextBox, "Invalid address.");
                SetInjectStatus("Status: invalid address", Color.DarkRed);
                return;
            }
            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                AppendLine(_logTextBox, $"Patch read failed: {ex.Message}");
                SetInjectStatus("Status: patch read failed", Color.DarkRed);
                return;
            }
            var memoryType = ParseMemoryType();
            bool ok = _client.InjectMemory(memoryType, address, data);
            AppendLine(_logTextBox, ok
                ? $"Injected {data.Length} bytes at 0x{address:X} ({memoryType})"
                : "Patch injection failed.");
            SetInjectStatus(ok
                ? $"Status: injected {data.Length:N0} bytes"
                : "Status: injection failed", ok ? Color.DarkGreen : Color.DarkRed);
        }

        private static bool TryParseAddress(string text, out uint address)
        {
            address = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return uint.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out address);
            }
            return uint.TryParse(text, out address);
        }

        private FirmwareMemoryType ParseMemoryType()
        {
            return _injectTypeCombo.SelectedIndex switch
            {
                1 => FirmwareMemoryType.Sram,
                2 => FirmwareMemoryType.Io,
                3 => FirmwareMemoryType.Eeprom,
                _ => FirmwareMemoryType.Flash
            };
        }

        private void InstallService()
        {
            string name = _serviceNameTextBox.Text.Trim();
            string path = _serviceExeTextBox.Text.Trim();
            string args = _serviceArgsTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
            {
                AppendLine(_logTextBox, "Service name and binary path are required.");
                SetServiceStatus("Status: name and path required", Color.DarkRed);
                return;
            }
            string binPath = string.IsNullOrWhiteSpace(args) ? $"\"{path}\"" : $"\"{path}\" {args}";
            RunServiceCommand($"create \"{name}\" binPath= \"{binPath}\" start= auto", true, false);
        }

        private void RemoveService()
        {
            string name = _serviceNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                AppendLine(_logTextBox, "Service name is required.");
                SetServiceStatus("Status: service name required", Color.DarkRed);
                return;
            }
            RunServiceCommand($"delete \"{name}\"", true, false);
        }

        private void StartService()
        {
            string name = _serviceNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                AppendLine(_logTextBox, "Service name is required.");
                SetServiceStatus("Status: service name required", Color.DarkRed);
                return;
            }
            RunServiceCommand($"start \"{name}\"", true, false);
        }

        private void StopService()
        {
            string name = _serviceNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                AppendLine(_logTextBox, "Service name is required.");
                SetServiceStatus("Status: service name required", Color.DarkRed);
                return;
            }
            RunServiceCommand($"stop \"{name}\"", true, false);
        }

        private void QueryService()
        {
            string name = _serviceNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                AppendLine(_logTextBox, "Service name is required.");
                SetServiceStatus("Status: service name required", Color.DarkRed);
                return;
            }
            RunServiceCommand($"query \"{name}\"", false, true);
        }

        private void SetServiceStatus(string message, Color color)
        {
            if (_serviceStatusLabel == null) return;
            _serviceStatusLabel.Text = message;
            _serviceStatusLabel.ForeColor = color;
        }

        private void AppendServiceOutput(string line)
        {
            if (_serviceOutputTextBox == null) return;
            AppendLine(_serviceOutputTextBox, line, 4000);
        }

        private void UpdateServiceStatusFromOutput(string output)
        {
            if (_serviceStatusLabel == null) return;
            if (string.IsNullOrWhiteSpace(output))
            {
                SetServiceStatus("Status: query complete", UiTextColor);
                return;
            }

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase)) continue;
                int colon = trimmed.IndexOf(':');
                string rest = colon >= 0 ? trimmed.Substring(colon + 1).Trim() : trimmed.Substring(5).Trim();
                string stateUpper = rest.ToUpperInvariant();
                if (stateUpper.Contains("RUNNING"))
                {
                    SetServiceStatus($"Status: {rest}", Color.DarkGreen);
                }
                else if (stateUpper.Contains("STOPPED"))
                {
                    SetServiceStatus($"Status: {rest}", Color.DarkRed);
                }
                else
                {
                    SetServiceStatus($"Status: {rest}", UiTextColor);
                }
                return;
            }
            SetServiceStatus("Status: query complete", UiTextColor);
        }

        private void RunServiceCommand(string arguments, bool elevate, bool captureOutput)
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = arguments,
                    UseShellExecute = elevate,
                    Verb = elevate ? "runas" : string.Empty,
                    CreateNoWindow = true
                };
                if (!elevate && captureOutput)
                {
                    startInfo.UseShellExecute = false;
                    startInfo.RedirectStandardOutput = true;
                    startInfo.RedirectStandardError = true;
                }
                var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    AppendLine(_logTextBox, "Service command failed to start.");
                    SetServiceStatus("Status: command failed to start", Color.DarkRed);
                    return;
                }
                if (!elevate && captureOutput)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(2000);
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        AppendLine(_logTextBox, output.Trim(), 4000);
                        AppendServiceOutput(output.Trim());
                    }
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        AppendLine(_logTextBox, error.Trim(), 4000);
                        AppendServiceOutput(error.Trim());
                    }
                    UpdateServiceStatusFromOutput(output);
                }
                else
                {
                    AppendLine(_logTextBox, $"Service command started: sc.exe {arguments}");
                    SetServiceStatus("Status: command sent", UiTextColor);
                }
            }
            catch (Exception ex)
            {
                AppendLine(_logTextBox, $"Service command failed: {ex.Message}");
                SetServiceStatus("Status: command failed", Color.DarkRed);
            }
        }

        private bool IsUnderLogDirectory(string path)
        {
            string logRoot = Path.GetFullPath(_logDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(logRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> SplitArgs(string commandLine)
        {
            var args = new List<string>();
            if (string.IsNullOrWhiteSpace(commandLine)) return args;
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();
            foreach (char ch in commandLine)
            {
                if (ch == '\"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (char.IsWhiteSpace(ch) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        args.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }
                current.Append(ch);
            }
            if (current.Length > 0)
            {
                args.Add(current.ToString());
            }
            return args;
        }

        private static string JoinArgs(List<string> tokens)
        {
            if (tokens.Count == 0) return string.Empty;
            var parts = new string[tokens.Count];
            for (int i = 0; i < tokens.Count; i++)
            {
                parts[i] = QuoteIfNeeded(tokens[i]);
            }
            return string.Join(" ", parts);
        }

        private static string QuoteIfNeeded(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }
            return value.IndexOf(' ') >= 0 ? $"\"{value}\"" : value;
        }

        private void RefreshProcessTree()
        {
            if (Interlocked.Exchange(ref _processTreeRefreshActive, 1) == 1)
            {
                return;
            }

            SetProcessRefreshBusy(true);
            _ = Task.Run(() =>
            {
                try
                {
                    var processes = GetProcessList();
                    var nodes = BuildProcessNodes(processes);
                    SafeUi(() =>
                    {
                        if (IsDisposed) return;
                        _processTree.BeginUpdate();
                        _processTree.Nodes.Clear();
                        _processTree.Nodes.AddRange(nodes.ToArray());
                        _processTree.EndUpdate();
                    });
                }
                catch (Exception ex)
                {
                    SafeUi(() => AppendLine(_logTextBox, $"Process tree refresh failed: {ex.Message}"));
                }
                finally
                {
                    Interlocked.Exchange(ref _processTreeRefreshActive, 0);
                    SafeUi(() => SetProcessRefreshBusy(false));
                }
            });
        }

        private void SetProcessRefreshBusy(bool busy)
        {
            if (_refreshProcessButton != null)
            {
                _refreshProcessButton.Enabled = !busy;
                _refreshProcessButton.Text = busy ? "Refreshing..." : RefreshProcessLabel;
            }
            if (_processTree != null)
            {
                _processTree.Enabled = !busy;
            }
        }

        private static List<TreeNode> BuildProcessNodes(Dictionary<uint, ProcessInfo> processes)
        {
            var children = new Dictionary<uint, List<ProcessInfo>>();
            foreach (var pair in processes)
            {
                var info = pair.Value;
                if (!children.TryGetValue(info.ParentPid, out var list))
                {
                    list = new List<ProcessInfo>();
                    children[info.ParentPid] = list;
                }
                list.Add(info);
            }

            var roots = new List<TreeNode>();
            foreach (var pair in processes)
            {
                var info = pair.Value;
                if (!processes.ContainsKey(info.ParentPid) || info.ParentPid == info.Pid)
                {
                    roots.Add(BuildNode(info, children, new HashSet<uint>(), 0));
                }
            }
            roots.Sort((a, b) => string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase));
            return roots;
        }

        private static TreeNode BuildNode(ProcessInfo info, Dictionary<uint, List<ProcessInfo>> children, HashSet<uint> path, int depth)
        {
            var node = new TreeNode($"{info.Name} ({info.Pid})");
            if (depth >= ProcessTreeDepthLimit)
            {
                node.Nodes.Add(new TreeNode("... depth limit"));
                return node;
            }
            if (!path.Add(info.Pid))
            {
                node.Nodes.Add(new TreeNode("... cycle"));
                return node;
            }
            if (children.TryGetValue(info.Pid, out var kids))
            {
                kids.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                foreach (var child in kids)
                {
                    node.Nodes.Add(BuildNode(child, children, path, depth + 1));
                }
            }
            path.Remove(info.Pid);
            return node;
        }

        private static Dictionary<uint, ProcessInfo> GetProcessList()
        {
            var processes = new Dictionary<uint, ProcessInfo>();
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == IntPtr.Zero || snapshot == INVALID_HANDLE_VALUE)
            {
                return processes;
            }

            try
            {
                var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                if (!Process32First(snapshot, ref entry))
                {
                    return processes;
                }
                do
                {
                    var info = new ProcessInfo
                    {
                        Pid = entry.th32ProcessID,
                        ParentPid = entry.th32ParentProcessID,
                        Name = entry.szExeFile
                    };
                    processes[info.Pid] = info;
                } while (Process32Next(snapshot, ref entry));
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return processes;
        }

        private static FlowLayoutPanel CreateFlowRow()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0, 2, 0, 2)
            };
        }

        private static Label MakeLabel(string text)
        {
            return new Label
            {
                AutoSize = true,
                Text = text,
                Margin = new Padding(6, 8, 4, 4),
                ForeColor = UiTextColor,
                BackColor = Color.Transparent
            };
        }

        private static Label MakePanelLabel(string text)
        {
            return new Label
            {
                AutoSize = true,
                Text = text,
                ForeColor = UiTextColor,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static TextBox CreateInputTextBox(int width, string? text = null)
        {
            var box = new TextBox
            {
                Width = width > 0 ? width : 100,
                BackColor = UiInputBackColor,
                ForeColor = UiInputTextColor
            };
            if (!string.IsNullOrEmpty(text))
            {
                box.Text = text;
            }
            return box;
        }

        private static NumericUpDown CreateNumericInput(decimal min, decimal max, decimal value, decimal increment, int width)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                Increment = increment,
                Width = width,
                BackColor = UiInputBackColor,
                ForeColor = UiInputTextColor
            };
        }

        private static ComboBox CreateComboBox(int width)
        {
            return new ComboBox
            {
                Width = width,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = UiInputBackColor,
                ForeColor = UiInputTextColor
            };
        }

        private static TabPage MakeTab(string title, Control content)
        {
            var page = new TabPage(title);
            page.Controls.Add(content);
            return page;
        }

        private static TextBox CreateLogTextBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point),
                BackColor = Color.FromArgb(15, 15, 15),
                ForeColor = Color.FromArgb(210, 230, 210),
                Tag = "log"
            };
        }

        private static Icon GetAppIcon()
        {
            if (_appIcon != null) return _appIcon;
            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "RobotWinFirmwareMonitor.ico");
                if (!File.Exists(iconPath))
                {
                    iconPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools",
                        "RobotWinFirmwareMonitor", "RobotWinFirmwareMonitor.ico");
                }
                if (File.Exists(iconPath))
                {
                    _appIcon = new Icon(iconPath);
                    return _appIcon;
                }
            }
            catch
            {
            }
            return SystemIcons.Application;
        }

        private static void ApplyTheme(Control root)
        {
            foreach (Control control in root.Controls)
            {
                if (control is TextBox box && string.Equals(box.Tag as string, "log", StringComparison.Ordinal))
                {
                    continue;
                }

                switch (control)
                {
                    case TextBox textBox:
                        textBox.BackColor = UiInputBackColor;
                        textBox.ForeColor = UiInputTextColor;
                        break;
                    case ComboBox comboBox:
                        comboBox.BackColor = UiInputBackColor;
                        comboBox.ForeColor = UiInputTextColor;
                        break;
                    case NumericUpDown numeric:
                        numeric.BackColor = UiInputBackColor;
                        numeric.ForeColor = UiInputTextColor;
                        break;
                    case TabControl tabControl:
                        tabControl.BackColor = UiInputBackColor;
                        tabControl.ForeColor = UiTextColor;
                        break;
                    case TabPage tabPage:
                        tabPage.BackColor = UiInputBackColor;
                        tabPage.ForeColor = UiTextColor;
                        break;
                    case ListView listView:
                        listView.BackColor = UiInputBackColor;
                        listView.ForeColor = UiTextColor;
                        break;
                    case TreeView treeView:
                        treeView.BackColor = UiInputBackColor;
                        treeView.ForeColor = UiTextColor;
                        break;
                    case DataGridView:
                        break;
                    default:
                        control.ForeColor = UiTextColor;
                        break;
                }

                if (control.HasChildren)
                {
                    ApplyTheme(control);
                }
            }
        }

        private static void AppendLine(TextBox box, string line, int maxLines = 1500)
        {
            if (string.IsNullOrEmpty(line)) return;
            box.AppendText(line + Environment.NewLine);
            if (box.Lines.Length > maxLines)
            {
                var lines = box.Lines;
                box.Lines = lines[^maxLines..];
            }
        }

        private static void ReplaceTextWithLimit(TextBox box, string text, int maxLines = 1500)
        {
            if (string.IsNullOrEmpty(text))
            {
                box.Clear();
                return;
            }

            string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = normalized.Split('\n');
            if (lines.Length > maxLines)
            {
                lines = lines[^maxLines..];
            }
            box.Lines = lines;
        }

        private void AppendTraceLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            EnqueueTraceLine(line);
            if (_tracePaused) return;
            if (!TraceFilterMatch(line)) return;
            AppendTraceLineFiltered(line, true);
        }

        private void EnqueueTraceLine(string line)
        {
            _traceBuffer.Enqueue(line);
            while (_traceBuffer.Count > TraceMaxLines)
            {
                _traceBuffer.Dequeue();
            }
        }

        private void AppendTraceLineFiltered(string line, bool ensureVisible)
        {
            AppendLine(_traceTextBox, line, TraceMaxLines);
            if (!TryParseTrace(line, out var record))
            {
                return;
            }

            var item = new ListViewItem(record.Pc);
            item.SubItems.Add(record.Op);
            item.SubItems.Add(record.Mnemonic);
            item.SubItems.Add(record.Sp);
            item.SubItems.Add(record.Sreg);
            item.SubItems.Add(record.Tick);
            _traceList.Items.Add(item);

            int excess = _traceList.Items.Count - TraceMaxItems;
            if (excess > 0)
            {
                for (int i = 0; i < excess; i++)
                {
                    _traceList.Items.RemoveAt(0);
                }
            }

            if (ensureVisible && _traceList.Items.Count > 0)
            {
                _traceList.EnsureVisible(_traceList.Items.Count - 1);
            }
        }

        private bool TraceFilterMatch(string line)
        {
            if (string.IsNullOrWhiteSpace(_traceFilter)) return true;
            return line.IndexOf(_traceFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyTraceFilter()
        {
            if (IsDisposed) return;
            _traceTextBox.Clear();
            _traceList.BeginUpdate();
            _traceList.Items.Clear();
            foreach (var line in _traceBuffer)
            {
                if (!TraceFilterMatch(line)) continue;
                AppendTraceLineFiltered(line, false);
            }
            _traceList.EndUpdate();
            if (_traceList.Items.Count > 0)
            {
                _traceList.EnsureVisible(_traceList.Items.Count - 1);
            }
        }

        private void ClearTrace()
        {
            _traceBuffer.Clear();
            _traceTextBox.Clear();
            _traceList.Items.Clear();
        }

        private void ToggleTracePause(bool paused)
        {
            _tracePaused = paused;
            if (!_tracePaused)
            {
                ApplyTraceFilter();
            }
        }

        private void OpenTraceFile()
        {
            string path = _tracePathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                AppendLine(_logTextBox, "Trace path is empty.");
                return;
            }
            if (!File.Exists(path))
            {
                AppendLine(_logTextBox, "Trace file not found.");
                return;
            }
            OpenPath(path);
        }

        private static bool TryParseTrace(string line, out TraceRecord record)
        {
            record = default;
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }
            if (!line.StartsWith("TRACE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string? pc = null;
            string? op = null;
            string? mnemonic = null;
            string? sp = null;
            string? sreg = null;
            string? tick = null;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                int eq = part.IndexOf('=');
                if (eq <= 0 || eq == part.Length - 1)
                {
                    continue;
                }
                string key = part.Substring(0, eq);
                string value = part.Substring(eq + 1);
                switch (key)
                {
                    case "pc":
                        pc = value;
                        break;
                    case "op":
                        op = value;
                        break;
                    case "mnem":
                        mnemonic = value;
                        break;
                    case "sp":
                        sp = value;
                        break;
                    case "sreg":
                        sreg = value;
                        break;
                    case "tick":
                        tick = value;
                        break;
                }
            }

            if (pc == null && op == null && mnemonic == null && sp == null && sreg == null && tick == null)
            {
                return false;
            }

            record = new TraceRecord
            {
                Pc = pc ?? "--",
                Op = op ?? "--",
                Mnemonic = mnemonic ?? "--",
                Sp = sp ?? "--",
                Sreg = sreg ?? "--",
                Tick = tick ?? "--"
            };
            return true;
        }

        private struct TraceRecord
        {
            public string Pc;
            public string Op;
            public string Mnemonic;
            public string Sp;
            public string Sreg;
            public string Tick;
        }

        private static string BuildActivity(int value, ulong sequence, bool changed)
        {
            if (value < 0)
            {
                return "unknown";
            }
            if (value > 1)
            {
                return changed ? $"pwm@{sequence}" : "pwm";
            }
            return changed ? $"chg@{sequence}" : string.Empty;
        }

        private static string FormatOutput(int value)
        {
            if (value < 0) return "UNK";
            if (value > 1) return $"PWM {value}";
            return value.ToString();
        }

        private static string FormatHex(int value)
        {
            if (value < 0) return "--";
            return $"0x{value:X2}";
        }

        private static string FormatBits(int value)
        {
            if (value < 0) return "--------";
            return Convert.ToString(value, 2).PadLeft(8, '0');
        }

        private static string BuildBitOpLine(ulong sequence, int pin, int oldValue, int newValue)
        {
            string oldLabel = FormatOutput(oldValue);
            string newLabel = FormatOutput(newValue);
            string oldBits = FormatBits(oldValue);
            string newBits = FormatBits(newValue);
            return $"seq {sequence} pin {pin}: {oldLabel} -> {newLabel} [{oldBits} -> {newBits}]";
        }

        private static float ClampVoltage(float voltage)
        {
            if (voltage < 0f) return 0f;
            if (voltage > 5f) return 5f;
            return voltage;
        }

        private ulong NextSequence()
        {
            lock (_sequenceLock)
            {
                _stepSequence++;
                return _stepSequence;
            }
        }

        private void SafeUi(Action action)
        {
            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        private T InvokeIfRequired<T>(Func<T> func)
        {
            if (InvokeRequired)
            {
                return (T)Invoke(func);
            }
            return func();
        }

        private async Task ConnectWithLaunchAsync(bool launch)
        {
            if (IsUnityTarget())
            {
                if (launch)
                {
                    AppendLine(_logTextBox, "Unity target ignores launch. Use Unity Bridge connect.");
                }
                await ConnectUnityAsync().ConfigureAwait(false);
            }
            else
            {
                if (launch)
                {
                    LaunchFirmware();
                }
                await ConnectAsync().ConfigureAwait(false);
            }
            if (_prefAutoTraceTail != null && _prefAutoTraceTail.Checked && _traceCts == null)
            {
                await ToggleTraceTailAsync();
            }
        }

        private void SaveProfile()
        {
            if (!_connected)
            {
                AppendLine(_logTextBox, "Connect before saving profile.");
                return;
            }
            string name = SanitizeProfileName(_profileNameTextBox?.Text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(name))
            {
                AppendLine(_logTextBox, "Profile name required.");
                return;
            }

            var profile = CaptureProfile(name);
            string path = GetProfilePath(name);
            Directory.CreateDirectory(_profilesDirectory);
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            LoadProfilesList();
            if (_profileCombo != null)
            {
                _profileCombo.SelectedItem = name;
            }
            AppendLine(_logTextBox, $"Saved profile: {name}");
        }

        private void LoadSelectedProfile()
        {
            string? name = _profileCombo?.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = SanitizeProfileName(_profileNameTextBox?.Text ?? string.Empty);
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                AppendLine(_logTextBox, "Select a profile to load.");
                return;
            }
            string path = GetProfilePath(name);
            if (!File.Exists(path))
            {
                AppendLine(_logTextBox, $"Profile not found: {name}");
                return;
            }
            try
            {
                var json = File.ReadAllText(path);
                var profile = JsonSerializer.Deserialize<MonitorProfile>(json);
                if (profile == null)
                {
                    AppendLine(_logTextBox, "Profile load failed: invalid data.");
                    return;
                }
                ApplyProfile(profile);
                AppendLine(_logTextBox, $"Loaded profile: {name}");
            }
            catch (Exception ex)
            {
                AppendLine(_logTextBox, $"Profile load failed: {ex.Message}");
            }
        }

        private void DeleteSelectedProfile()
        {
            string? name = _profileCombo?.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                AppendLine(_logTextBox, "Select a profile to delete.");
                return;
            }
            string path = GetProfilePath(name);
            if (!File.Exists(path))
            {
                AppendLine(_logTextBox, $"Profile not found: {name}");
                return;
            }
            File.Delete(path);
            LoadProfilesList();
            AppendLine(_logTextBox, $"Deleted profile: {name}");
        }

        private void LoadProfilesList()
        {
            if (_profileCombo == null) return;
            _profileCombo.Items.Clear();
            if (Directory.Exists(_profilesDirectory))
            {
                foreach (var file in Directory.GetFiles(_profilesDirectory, "*.json"))
                {
                    _profileCombo.Items.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            if (_profileCombo.Items.Count > 0 && _profileCombo.SelectedIndex < 0)
            {
                _profileCombo.SelectedIndex = 0;
            }
        }

        private MonitorProfile CaptureProfile(string name)
        {
            return new MonitorProfile
            {
                Name = name,
                PipeName = _pipeNameTextBox.Text.Trim(),
                BoardId = _boardIdTextBox.Text.Trim(),
                BoardProfile = _boardProfileTextBox.Text.Trim(),
                FirmwarePath = _firmwarePathTextBox.Text.Trim(),
                BvmPath = _bvmPathTextBox.Text.Trim(),
                ExtraArgs = _extraArgsTextBox.Text.Trim(),
                TracePath = _tracePathTextBox.Text.Trim(),
                DeltaMicros = (uint)_deltaMicrosInput.Value,
                StepIntervalMs = (int)_stepIntervalMsInput.Value,
                LaunchOnConnect = _launchOnConnect,
                AutoConnect = _prefAutoConnect?.Checked ?? false,
                AutoTraceTail = _prefAutoTraceTail?.Checked ?? false,
                TargetProgramIndex = _targetProgramCombo?.SelectedIndex ?? 0
            };
        }

        private void ApplyProfile(MonitorProfile profile)
        {
            _pipeNameTextBox.Text = profile.PipeName ?? string.Empty;
            _boardIdTextBox.Text = profile.BoardId ?? string.Empty;
            _boardProfileTextBox.Text = profile.BoardProfile ?? string.Empty;
            _firmwarePathTextBox.Text = profile.FirmwarePath ?? string.Empty;
            _bvmPathTextBox.Text = profile.BvmPath ?? string.Empty;
            _extraArgsTextBox.Text = profile.ExtraArgs ?? string.Empty;
            _tracePathTextBox.Text = profile.TracePath ?? string.Empty;
            _deltaMicrosInput.Value = ClampNumeric(_deltaMicrosInput, profile.DeltaMicros);
            _stepIntervalMsInput.Value = ClampNumeric(_stepIntervalMsInput, profile.StepIntervalMs);
            _launchOnConnect = profile.LaunchOnConnect;
            if (_prefAutoConnect != null) _prefAutoConnect.Checked = profile.AutoConnect;
            if (_prefAutoTraceTail != null) _prefAutoTraceTail.Checked = profile.AutoTraceTail;
            if (_prefConnectionMode != null)
            {
                _prefConnectionMode.SelectedIndex = _launchOnConnect ? 1 : 0;
            }
            if (_targetProgramCombo != null)
            {
                _targetProgramCombo.SelectedIndex = profile.TargetProgramIndex;
            }
            if (_prefTargetProgramCombo != null)
            {
                _prefTargetProgramCombo.SelectedIndex = profile.TargetProgramIndex;
            }
        }

        private static decimal ClampNumeric(NumericUpDown control, long value)
        {
            decimal val = value;
            if (val < control.Minimum) return control.Minimum;
            if (val > control.Maximum) return control.Maximum;
            return val;
        }

        private string GetProfilePath(string name)
        {
            return Path.Combine(_profilesDirectory, $"{name}.json");
        }

        private static string SanitizeProfileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var chars = new List<char>(name.Length);
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    chars.Add(c);
                }
            }
            return new string(chars.ToArray());
        }

        private void SyncTargetProgram(int index)
        {
            int target = index < 0 ? 0 : index;
            _isUnityTarget = target == 1;
            if (_targetProgramCombo != null && _targetProgramCombo.SelectedIndex != target)
            {
                _targetProgramCombo.SelectedIndex = target;
            }
            if (_prefTargetProgramCombo != null && _prefTargetProgramCombo.SelectedIndex != target)
            {
                _prefTargetProgramCombo.SelectedIndex = target;
            }

            // Currently both modes use the same firmware pipe; keep the field visible for future expansion.
            if (target == 1 && string.IsNullOrWhiteSpace(_pipeNameTextBox.Text))
            {
                _pipeNameTextBox.Text = "RoboTwin.FirmwareEngine";
            }

            if (target == 1)
            {
                if (_prefAutoConnect != null && !_prefAutoConnect.Checked)
                {
                    _prefAutoConnect.Checked = true;
                }
                if (IsAutoConnectEnabled() && !_connected)
                {
                    StartUnityAutoConnect();
                }
            }
            else
            {
                StopUnityAutoConnect();
            }
        }

        private void RefreshRunningHosts()
        {
            if (_runningHostCombo == null) return;
            if (Interlocked.Exchange(ref _runningHostRefreshActive, 1) == 1) return;

            SetHostRefreshBusy(true);
            _ = Task.Run(() =>
            {
                List<RunningHostInfo> hosts = new List<RunningHostInfo>();
                try
                {
                    hosts.AddRange(EnumerateRunningHosts());
                }
                catch (Exception ex)
                {
                    SafeUi(() => AppendLine(_logTextBox, $"Host refresh failed: {ex.Message}"));
                }
                SafeUi(() =>
                {
                    _runningHostCombo.BeginUpdate();
                    _runningHostCombo.Items.Clear();
                    foreach (var host in hosts)
                    {
                        _runningHostCombo.Items.Add(host);
                    }
                    _runningHostCombo.EndUpdate();
                    if (_runningHostCombo.Items.Count > 0)
                    {
                        _runningHostCombo.SelectedIndex = 0;
                    }
                    else
                    {
                        _runningHostCombo.Text = string.Empty;
                    }
                    SetHostRefreshBusy(false);
                });
                Interlocked.Exchange(ref _runningHostRefreshActive, 0);
            });
        }

        private void SetHostRefreshBusy(bool busy)
        {
            if (_refreshHostButton != null)
            {
                _refreshHostButton.Enabled = !busy;
                _refreshHostButton.Text = busy ? "Refreshing..." : RefreshHostsLabel;
            }
            if (_runningHostCombo != null)
            {
                _runningHostCombo.Enabled = !busy;
            }
        }

        private async Task AttachSelectedHostAsync()
        {
            if (_runningHostCombo?.SelectedItem is not RunningHostInfo host)
            {
                AppendLine(_logTextBox, "Select a running firmware host PID.");
                return;
            }
            _pipeNameTextBox.Text = host.PipeName;
            await ConnectWithLaunchAsync(false);
        }

        private static IEnumerable<RunningHostInfo> EnumerateRunningHosts()
        {
            var hosts = new List<RunningHostInfo>();
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                return hosts;
            }

            foreach (var proc in processes)
            {
                string name = proc.ProcessName ?? string.Empty;
                if (!name.Contains("FirmwareHost", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                int pid = proc.Id;
                string commandLine = TryGetCommandLine(pid) ?? string.Empty;
                string pipeName = ParsePipeName(commandLine) ?? "RoboTwin.FirmwareEngine";
                hosts.Add(new RunningHostInfo(pid, $"{name}.exe", pipeName, commandLine));
            }
            hosts.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return hosts;
        }

        private static string? ParsePipeName(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return null;
            var tokens = SplitArgs(commandLine);
            for (int i = 0; i < tokens.Count - 1; i++)
            {
                if (string.Equals(tokens[i], "--pipe", StringComparison.OrdinalIgnoreCase))
                {
                    return tokens[i + 1];
                }
            }
            return null;
        }

        private static string? TryGetCommandLine(int pid)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId={pid}");
                foreach (ManagementObject obj in searcher.Get())
                {
                    return obj["CommandLine"]?.ToString();
                }
            }
            catch
            {
            }
            return null;
        }

        private void AttachTabContextMenu(TabControl tabControl)
        {
            var menu = new ContextMenuStrip();
            var detachItem = new ToolStripMenuItem("Detach Tab");
            detachItem.Click += (_, __) =>
            {
                if (tabControl.SelectedTab != null)
                {
                    DetachTab(tabControl, tabControl.SelectedTab);
                }
            };
            menu.Items.Add(detachItem);

            tabControl.MouseUp += (_, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                for (int i = 0; i < tabControl.TabPages.Count; i++)
                {
                    if (tabControl.GetTabRect(i).Contains(e.Location))
                    {
                        tabControl.SelectedIndex = i;
                        menu.Show(tabControl, e.Location);
                        break;
                    }
                }
            };
        }

        private ToolStripMenuItem MakeDetachMenuItem(string title, TabControl tabControl, TabPage tabPage)
        {
            var item = new ToolStripMenuItem(title);
            item.Click += (_, __) => DetachTab(tabControl, tabPage);
            return item;
        }

        private void DetachTab(TabControl tabControl, TabPage tabPage)
        {
            if (_detachedTabs.ContainsKey(tabPage)) return;
            int index = tabControl.TabPages.IndexOf(tabPage);
            if (index < 0) return;

            Control? content = tabPage.Controls.Count > 0 ? tabPage.Controls[0] : null;
            if (content != null)
            {
                tabPage.Controls.Remove(content);
            }
            tabControl.TabPages.Remove(tabPage);

            var host = new Form
            {
                Text = $"RobotWinFirmwareMonitor - {tabPage.Text}",
                Width = 900,
                Height = 600,
                StartPosition = FormStartPosition.CenterParent
            };
            if (content != null)
            {
                content.Dock = DockStyle.Fill;
                host.Controls.Add(content);
            }
            host.FormClosed += (_, __) => AttachTab(tabControl, tabPage, content, index);

            _detachedTabs[tabPage] = new DetachedTab(tabControl, tabPage, content, host, index);
            host.Show(this);
        }

        private void AttachTab(TabControl tabControl, TabPage tabPage, Control? content, int index)
        {
            if (_detachedTabs.ContainsKey(tabPage))
            {
                _detachedTabs.Remove(tabPage);
            }
            if (content != null)
            {
                content.Parent?.Controls.Remove(content);
                tabPage.Controls.Add(content);
                content.Dock = DockStyle.Fill;
            }
            if (index < 0 || index > tabControl.TabPages.Count)
            {
                tabControl.TabPages.Add(tabPage);
            }
            else
            {
                tabControl.TabPages.Insert(index, tabPage);
            }
        }

        private void EnableTabReorder(TabControl tabControl)
        {
            tabControl.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _dragTabControl = tabControl;
                _dragTab = GetTabAt(tabControl, e.Location);
            };
            tabControl.MouseMove += (_, e) =>
            {
                if (_dragTabControl != tabControl || _dragTab == null) return;
                if (e.Button != MouseButtons.Left) return;
                var target = GetTabAt(tabControl, e.Location);
                if (target == null || target == _dragTab) return;
                int sourceIndex = tabControl.TabPages.IndexOf(_dragTab);
                int targetIndex = tabControl.TabPages.IndexOf(target);
                if (sourceIndex < 0 || targetIndex < 0) return;
                tabControl.TabPages.RemoveAt(sourceIndex);
                tabControl.TabPages.Insert(targetIndex, _dragTab);
                tabControl.SelectedTab = _dragTab;
            };
            tabControl.MouseUp += (_, __) =>
            {
                _dragTab = null;
                _dragTabControl = null;
            };
        }

        private static TabPage? GetTabAt(TabControl tabControl, Point location)
        {
            for (int i = 0; i < tabControl.TabPages.Count; i++)
            {
                if (tabControl.GetTabRect(i).Contains(location))
                {
                    return tabControl.TabPages[i];
                }
            }
            return null;
        }

        private void OpenPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string resolved = path;
                if (!Path.IsPathRooted(path))
                {
                    resolved = Path.GetFullPath(path, AppContext.BaseDirectory);
                }
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = resolved,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                AppendLine(_logTextBox, $"Open failed: {ex.Message}");
            }
        }

        private readonly struct ResourceSample
        {
            public ResourceSample(
                DateTime timestamp,
                double cpuPercent,
                double ramPercent,
                double appMemPercent,
                double netPercent,
                double appMemMb,
                double ramUsedGb,
                double ramTotalGb,
                double netMbps)
            {
                Timestamp = timestamp;
                CpuPercent = cpuPercent;
                RamPercent = ramPercent;
                AppMemPercent = appMemPercent;
                NetPercent = netPercent;
                AppMemMb = appMemMb;
                RamUsedGb = ramUsedGb;
                RamTotalGb = ramTotalGb;
                NetMbps = netMbps;
            }

            public DateTime Timestamp { get; }
            public double CpuPercent { get; }
            public double RamPercent { get; }
            public double AppMemPercent { get; }
            public double NetPercent { get; }
            public double AppMemMb { get; }
            public double RamUsedGb { get; }
            public double RamTotalGb { get; }
            public double NetMbps { get; }
        }

        private sealed class ResourceGraphPanel : Panel
        {
            private readonly Func<IReadOnlyList<ResourceSample>> _samplesProvider;
            private readonly Pen _cpuPen = new Pen(Color.FromArgb(220, 80, 60), 2f);
            private readonly Pen _ramPen = new Pen(Color.FromArgb(80, 120, 220), 2f);
            private readonly Pen _appPen = new Pen(Color.FromArgb(70, 170, 90), 2f);
            private readonly Pen _netPen = new Pen(Color.FromArgb(200, 160, 60), 2f);
            private readonly Pen _gridPen = new Pen(Color.FromArgb(220, 225, 230), 1f);

            public ResourceGraphPanel(Func<IReadOnlyList<ResourceSample>> samplesProvider)
            {
                _samplesProvider = samplesProvider;
                DoubleBuffered = true;
                BackColor = Color.White;
                Dock = DockStyle.Fill;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var samples = _samplesProvider();
                if (samples.Count < 2)
                {
                    using var brush = new SolidBrush(Color.DimGray);
                    e.Graphics.DrawString("Waiting for resource samples...", Font, brush, new PointF(10, 10));
                    return;
                }

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new RectangleF(40, 10, ClientSize.Width - 55, ClientSize.Height - 30);
                if (rect.Width <= 10 || rect.Height <= 10) return;

                for (int i = 0; i <= 4; i++)
                {
                    float y = rect.Top + rect.Height * i / 4f;
                    e.Graphics.DrawLine(_gridPen, rect.Left, y, rect.Right, y);
                }

                DrawSeries(e.Graphics, samples, rect, s => s.CpuPercent, _cpuPen);
                DrawSeries(e.Graphics, samples, rect, s => s.RamPercent, _ramPen);
                DrawSeries(e.Graphics, samples, rect, s => s.AppMemPercent, _appPen);
                DrawSeries(e.Graphics, samples, rect, s => s.NetPercent, _netPen);

                DrawLegend(e.Graphics, rect, samples[^1]);
            }

            private void DrawSeries(Graphics g, IReadOnlyList<ResourceSample> samples, RectangleF rect, Func<ResourceSample, double> selector, Pen pen)
            {
                int count = samples.Count;
                if (count < 2) return;
                var points = new PointF[count];
                for (int i = 0; i < count; i++)
                {
                    float x = rect.Left + rect.Width * i / (count - 1);
                    double value = Math.Clamp(selector(samples[i]), 0, 100);
                    float y = rect.Bottom - (float)(value / 100.0 * rect.Height);
                    points[i] = new PointF(x, y);
                }
                g.DrawLines(pen, points);
            }

            private void DrawLegend(Graphics g, RectangleF rect, ResourceSample sample)
            {
                float x = rect.Left + 8;
                float y = rect.Top + 6;
                DrawLegendItem(g, x, y, _cpuPen.Color, $"CPU {sample.CpuPercent:F1}%");
                DrawLegendItem(g, x + 90, y, _ramPen.Color, $"RAM {sample.RamPercent:F1}%");
                DrawLegendItem(g, x + 180, y, _appPen.Color, $"App {sample.AppMemPercent:F1}%");
                DrawLegendItem(g, x + 270, y, _netPen.Color, $"Net {sample.NetPercent:F1}%");
            }

            private void DrawLegendItem(Graphics g, float x, float y, Color color, string label)
            {
                using var brush = new SolidBrush(color);
                g.FillRectangle(brush, x, y + 4, 10, 10);
                using var textBrush = new SolidBrush(Color.FromArgb(60, 60, 60));
                g.DrawString(label, Font, textBrush, new PointF(x + 14, y));
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _cpuPen.Dispose();
                    _ramPen.Dispose();
                    _appPen.Dispose();
                    _netPen.Dispose();
                    _gridPen.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        private sealed class PinGraphPanel : Panel
        {
            private readonly Func<IReadOnlyList<byte[]>> _historyProvider;
            private readonly SolidBrush[] _valueBrushes = new SolidBrush[256];

            public PinGraphPanel(Func<IReadOnlyList<byte[]>> historyProvider)
            {
                _historyProvider = historyProvider;
                DoubleBuffered = true;
                BackColor = Color.Black;
                Dock = DockStyle.Fill;
                for (int i = 0; i < _valueBrushes.Length; i++)
                {
                    _valueBrushes[i] = new SolidBrush(BuildColor((byte)i));
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var history = _historyProvider();
                int historyCount = history.Count;
                if (historyCount == 0)
                {
                    using var brush = new SolidBrush(Color.DimGray);
                    e.Graphics.DrawString("No pin history yet.", Font, brush, new PointF(10, 10));
                    return;
                }

                int pinCount = history[historyCount - 1].Length;
                if (pinCount == 0)
                {
                    return;
                }

                e.Graphics.SmoothingMode = SmoothingMode.None;
                float cellWidth = (float)ClientSize.Width / historyCount;
                float cellHeight = (float)ClientSize.Height / pinCount;

                for (int x = 0; x < historyCount; x++)
                {
                    var snapshot = history[x];
                    for (int pin = 0; pin < pinCount; pin++)
                    {
                        byte value = snapshot[pin];
                        var brush = _valueBrushes[value];
                        e.Graphics.FillRectangle(brush, x * cellWidth, pin * cellHeight, cellWidth, cellHeight);
                    }
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    foreach (var brush in _valueBrushes)
                    {
                        brush?.Dispose();
                    }
                }
                base.Dispose(disposing);
            }

            private static Color BuildColor(byte value)
            {
                if (value == 255)
                {
                    return Color.FromArgb(50, 50, 50);
                }
                if (value == 0)
                {
                    return Color.FromArgb(20, 40, 60);
                }
                if (value == 1)
                {
                    return Color.FromArgb(80, 220, 120);
                }
                int intensity = Math.Clamp(255 - value, 40, 255);
                return Color.FromArgb(255, intensity, 40);
            }
        }

        private sealed class ProcessInfo
        {
            public uint Pid { get; set; }
            public uint ParentPid { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        private sealed class DetachedTab
        {
            public DetachedTab(TabControl tabControl, TabPage page, Control? content, Form host, int index)
            {
                TabControl = tabControl;
                Page = page;
                Content = content;
                Host = host;
                Index = index;
            }

            public TabControl TabControl { get; }
            public TabPage Page { get; }
            public Control? Content { get; }
            public Form Host { get; }
            public int Index { get; }
        }

        private sealed class StartupOptions
        {
            public int? TargetProgramIndex { get; private set; }
            public bool? AutoConnect { get; private set; }

            public static StartupOptions FromArgs(string[] args)
            {
                var options = new StartupOptions();
                if (args == null || args.Length == 0) return options;

                for (int i = 1; i < args.Length; i++)
                {
                    string arg = args[i]?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(arg)) continue;

                    if (arg.Equals("--unity", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("--target=unity", StringComparison.OrdinalIgnoreCase))
                    {
                        options.TargetProgramIndex = 1;
                        continue;
                    }

                    if (arg.Equals("--firmware", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("--target=firmware", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("--target=host", StringComparison.OrdinalIgnoreCase))
                    {
                        options.TargetProgramIndex = 0;
                        continue;
                    }

                    if (arg.StartsWith("--target=", StringComparison.OrdinalIgnoreCase))
                    {
                        string target = arg.Substring("--target=".Length);
                        if (target.Equals("unity", StringComparison.OrdinalIgnoreCase))
                        {
                            options.TargetProgramIndex = 1;
                        }
                        else if (target.Equals("firmware", StringComparison.OrdinalIgnoreCase) ||
                                 target.Equals("host", StringComparison.OrdinalIgnoreCase))
                        {
                            options.TargetProgramIndex = 0;
                        }
                        continue;
                    }

                    if (arg.Equals("--auto-connect", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("--autoconnect", StringComparison.OrdinalIgnoreCase))
                    {
                        options.AutoConnect = true;
                        continue;
                    }

                    if (arg.Equals("--no-auto-connect", StringComparison.OrdinalIgnoreCase))
                    {
                        options.AutoConnect = false;
                        continue;
                    }

                    if (arg.Equals("--unity-auto", StringComparison.OrdinalIgnoreCase))
                    {
                        options.TargetProgramIndex = 1;
                        options.AutoConnect = true;
                    }
                }

                return options;
            }
        }

        private sealed class MonitorProfile
        {
            public string? Name { get; set; }
            public string? PipeName { get; set; }
            public string? BoardId { get; set; }
            public string? BoardProfile { get; set; }
            public string? FirmwarePath { get; set; }
            public string? BvmPath { get; set; }
            public string? ExtraArgs { get; set; }
            public string? TracePath { get; set; }
            public uint DeltaMicros { get; set; }
            public int StepIntervalMs { get; set; }
            public bool LaunchOnConnect { get; set; }
            public bool AutoConnect { get; set; }
            public bool AutoTraceTail { get; set; }
            public int TargetProgramIndex { get; set; }
        }

        private sealed class RunningHostInfo
        {
            public RunningHostInfo(int pid, string name, string pipeName, string commandLine)
            {
                Pid = pid;
                Name = name;
                PipeName = pipeName;
                CommandLine = commandLine;
            }

            public int Pid { get; }
            public string Name { get; }
            public string PipeName { get; }
            public string CommandLine { get; }
            public string DisplayName => $"{Name} (PID {Pid})";

            public override string ToString()
            {
                return $"{DisplayName} | pipe={PipeName}";
            }
        }

        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;

            public ulong ToUInt64()
            {
                return ((ulong)dwHighDateTime << 32) | dwLowDateTime;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private sealed class PinRow : INotifyPropertyChanged
        {
            private bool _input;
            private string _output = "UNK";
            private string _outputHex = "--";
            private string _outputBits = "--------";
            private string _activity = string.Empty;

            public PinRow(int pin)
            {
                Pin = pin;
            }

            public int Pin { get; }
            public int OutputValue { get; set; } = -1;

            public bool Input
            {
                get => _input;
                set
                {
                    if (_input == value) return;
                    _input = value;
                    OnPropertyChanged(nameof(Input));
                }
            }

            public string Output
            {
                get => _output;
                set
                {
                    if (_output == value) return;
                    _output = value;
                    OnPropertyChanged(nameof(Output));
                }
            }

            public string OutputHex
            {
                get => _outputHex;
                set
                {
                    if (_outputHex == value) return;
                    _outputHex = value;
                    OnPropertyChanged(nameof(OutputHex));
                }
            }

            public string OutputBits
            {
                get => _outputBits;
                set
                {
                    if (_outputBits == value) return;
                    _outputBits = value;
                    OnPropertyChanged(nameof(OutputBits));
                }
            }

            public string Activity
            {
                get => _activity;
                set
                {
                    if (_activity == value) return;
                    _activity = value;
                    OnPropertyChanged(nameof(Activity));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged(string name)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        private sealed class AnalogRow : INotifyPropertyChanged
        {
            private float _voltage;
            private int _raw;
            private string _bits = "0000000000";

            public AnalogRow(int channel)
            {
                Channel = channel;
            }

            public int Channel { get; }

            public float Voltage
            {
                get => _voltage;
                set
                {
                    if (Math.Abs(_voltage - value) < 0.0001f) return;
                    _voltage = value;
                    OnPropertyChanged(nameof(Voltage));
                }
            }

            public int Raw
            {
                get => _raw;
                set
                {
                    if (_raw == value) return;
                    _raw = value;
                    OnPropertyChanged(nameof(Raw));
                }
            }

            public string Bits
            {
                get => _bits;
                set
                {
                    if (_bits == value) return;
                    _bits = value;
                    OnPropertyChanged(nameof(Bits));
                }
            }

            public void UpdateDerived()
            {
                float clamped = ClampVoltage(_voltage);
                int raw = (int)Math.Round((clamped / 5f) * 1023f);
                Raw = raw;
                Bits = Convert.ToString(raw, 2).PadLeft(10, '0');
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged(string name)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
