using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Industrial;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;
using RobotWin.Studio.World;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [hmi] The world's operator panel, a dock tab like the sensors: the panel the HMI designer laid out (World/HmiDesigner.cs),
    /// scaled to the tab, its elements bound to the PLCs' variables. While the world runs they show the program's values and
    /// write into it (a button holds its variable TRUE while pressed, as an operator's finger on a panel), the alarms come
    /// and go and the bar above says how many are active; when it is stopped the panel shows its layout.
    /// </summary>
    public partial class WorldPage
    {
        private RadioButton? _dockHmi;
        private Border? _hmiPanel;
        private readonly List<Action> _hmiRefresh = new List<Action>();
        private DateTime _lastHmi = DateTime.MinValue;
        private readonly HmiAlarmMonitor _hmiAlarms = new HmiAlarmMonitor();
        private TextBlock? _hmiAlarmText;
        private HmiDesigner? _hmiDesigner;
        private bool _hmiFit;   // the panel scaled to the tab, or at its own size with scroll bars
        private Border? _hmiScreen;   // the panel's screen as built last (the snapshot's picture)
        private bool _hmiQuiet;   // an HMI edit: the world changed, the 3D scene did not
        private bool _hmiScreenShot;   // --snapshot "…:hmi:screen": the screen alone is the picture, however often it is rebuilt

        private void BuildHmiDock()
        {
            if (DockRf.Parent is not Panel tabs || LogList.Parent is not Grid host) return;
            _dockHmi = new RadioButton
            {
                Style = (Style)FindResource("Chip"), Content = "HMI", GroupName = "dock", Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Operatör paneli: PLC değişkenlerine bağlı lambalar, düğmeler, göstergeler, trendler, motorlar ve alarmlar"
            };
            tabs.Children.Add(_dockHmi);
            _hmiPanel = new Border { Visibility = Visibility.Collapsed };
            host.Children.Add(_hmiPanel);
            _dockHmi.Checked += (_, _) =>
            {
                if (_dock == null) BuildHmiPanel();
            };
            _session.Updated += (_, _) =>
            {
                if ((DateTime.Now - _lastHmi).TotalMilliseconds < 100) return;
                _lastHmi = DateTime.Now;
                UpdateHmiAlarms();   // alarms come and go whether the tab is open or not
                if (DockShowing("hmi", _dockHmi)) foreach (var refresh in _hmiRefresh) refresh();
            };
            _session.StateChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_session.State == SimState.Stopped) _hmiAlarms.Reset();
                if (DockShowing("hmi", _dockHmi)) BuildHmiPanel();
            }));
        }

        /// <summary>The PLC program of <paramref name="plc"/> while the world runs.</summary>
        private StProgram? LiveProgram(string plc) => _session.State == SimState.Stopped ? null : _session.Physics?.PlcOf(plc)?.Program;

        private double HmiRead(string plc, string variable) => variable.Length == 0 ? double.NaN : LiveProgram(plc)?.Read(variable) ?? double.NaN;

        private void HmiWrite(string plc, string variable, double value)
        {
            if (LiveProgram(plc) is not { } program) return;
            using (SimThread.Hold())
            {
                if (!program.Write(variable, value)) W.Status = $"{variable} yazılamaz (bir girişe bağlı ya da tanımlı değil).";
            }
        }

        private void UpdateHmiAlarms()
        {
            if (World?.HmiAlarms is not { Count: > 0 } alarms || _session.State == SimState.Stopped) return;
            _hmiAlarms.Update(alarms, HmiRead, _session.Time);
            if (_hmiAlarmText != null) ShowAlarmSummary();
        }

        private void ShowAlarmSummary()
        {
            if (_hmiAlarmText == null) return;
            int active = _hmiAlarms.ActiveCount, open = _hmiAlarms.UnacknowledgedCount;
            _hmiAlarmText.Text = World?.HmiAlarms is not { Count: > 0 } ? string.Empty
                : active == 0 && open == 0 ? Shell.Lang.T("Alarm yok")
                : Shell.Lang.T($"{active} etkin alarm · {open} onaysız");
            _hmiAlarmText.SetResourceReference(TextBlock.ForegroundProperty, active > 0 ? "B.Danger" : open > 0 ? "B.Warning" : "B.TextSecondary");
        }

        /// <summary>[hmi-designer] An undoable change of the panel or its alarms: the scene is not rebuilt for it.</summary>
        private void EditHmi(string status, Action change)
        {
            if (World == null) return;
            W.BeginWorldChange();
            change();
            _hmiQuiet = true;
            try
            {
                W.CommitWorldChange(status);
            }
            finally
            {
                _hmiQuiet = false;
            }
        }

        /// <summary>The HMI designer on this world's panel (one window; brought forward when already open).</summary>
        public void OpenHmiDesigner()
        {
            if (World is not { } world) return;
            if (_hmiDesigner is { IsLoaded: true })
            {
                _hmiDesigner.Activate();
                return;
            }

            _hmiDesigner = new HmiDesigner(Window.GetWindow(this), world, EditHmi);
            _hmiDesigner.Closed += (_, _) =>
            {
                _hmiDesigner = null;
                BuildHmiPanel();
            };
            _hmiDesigner.Show();
        }

        private void BuildHmiPanel()
        {
            if (_hmiPanel == null || World is not { } world) return;
            _hmiRefresh.Clear();
            var root = new DockPanel();
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var design = new Button { Content = Shell.Lang.T("Tasarla…"), Style = (Style)FindResource("Btn.Ghost"), Padding = new Thickness(10, 2, 10, 2), MinHeight = 24, ToolTip = Shell.Lang.T("HMI tasarımcısı: öğeleri panele yerleştirin, PLC değişkenlerine bağlayın, alarmları tanımlayın") };
            design.Click += (_, _) => OpenHmiDesigner();
            DockPanel.SetDock(design, Dock.Right);
            bar.Children.Add(design);
            var fit = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = Shell.Lang.T("Sığdır"), Style = (Style)FindResource("Chip"), IsChecked = _hmiFit, Margin = new Thickness(0, 0, 6, 0),
                ToolTip = Shell.Lang.T("Paneli sekmeye sığdır; kapalıyken kendi boyutunda, kaydırma çubuklarıyla"),
            };
            fit.Click += (_, _) =>
            {
                _hmiFit = fit.IsChecked == true;
                BuildHmiPanel();
            };
            DockPanel.SetDock(fit, Dock.Right);
            bar.Children.Add(fit);
            _hmiAlarmText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
            bar.Children.Add(_hmiAlarmText);
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);
            _hmiPanel.Child = root;

            var plcs = world.Objects.Where(o => o.PartId == CellParts.PlcId).ToList();
            if (plcs.Count == 0)
            {
                root.Children.Add(Note("Operatör paneli PLC değişkenlerine bağlanır: önce Parçalar → Robot Hücresi'nden bir PLC ekleyin."));
                return;
            }

            var widgets = world.Hmi ?? new List<HmiWidget>();
            if (widgets.Count == 0)
            {
                root.Children.Add(Note("Panel boş: Tasarla… ile lambalar, düğmeler, göstergeler, trendler, motorlar ve alarm listesi ekleyin."));
                ShowAlarmSummary();
                return;
            }

            // a panel made before the designer gets its places here too (the same rows the designer would make)
            var panel = world.HmiPanel ?? new HmiPanelSettings();
            if (widgets.Any(w => w.Width <= 0 || w.Height <= 0)) HmiLayout.Place(widgets, panel);
            var canvas = new Canvas { Width = panel.Width, Height = panel.Height, Background = (Brush)FindResource("B.Base") };
            var ctx = new HmiContext
            {
                Live = true, Read = HmiRead, Write = HmiWrite, Time = () => _session.Time, Alarms = _hmiAlarms,
                PlcName = id => world.Find(id)?.Name ?? id,
            };
            foreach (var w in widgets)
            {
                var item = HmiElements.Build(w, ctx, _hmiRefresh);
                Canvas.SetLeft(item, w.X);
                Canvas.SetTop(item, w.Y);
                canvas.Children.Add(item);
            }

            // the screen's edge drawn, as an HMI's bezel would be: the panel is that size and no larger
            _hmiScreen = new Border
            {
                Child = canvas, BorderBrush = (Brush)FindResource("B.BorderStrong"), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            };
            if (_hmiScreenShot)
            {
                _hmiScreen.Background = (Brush)FindResource("B.Base");
                App.SnapshotTarget = _hmiScreen;
            }

            root.Children.Add(_hmiFit
                ? new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = _hmiScreen, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
                : new ScrollViewer { Content = _hmiScreen, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            ShowAlarmSummary();
            foreach (var refresh in _hmiRefresh) refresh();
        }

        /// <summary>
        /// --snapshot "…:hmi" / "…:hmi-designer": a PLC running the example program and a panel on its variables (a title,
        /// the blinker and the lamp, the push button, the counter on a dial, a trend, a motor, the alarm list and two alarms).
        /// </summary>
        private void HmiSnapshotSetup()
        {
            if (World is not { } world || !(_snapshotModes.Contains("hmi") || _snapshotModes.Contains("hmi-designer") || _snapshotModes.Contains("plc"))) return;
            var plc = world.Objects.FirstOrDefault(o => o.PartId == CellParts.PlcId) ?? world.AddComponent(Lib, CellParts.PlcId, new WorldPose { X = 0.35, Y = 0.3, Z = 0 });
            plc.Name = "PLC";
            if (world.Hmi != null) return;
            HmiWidget W(string kind, string variable, string label, double x, double y, double w, double h) =>
                new HmiWidget { Kind = kind, Plc = plc.Id, Variable = variable, Label = label, X = x, Y = y, Width = w, Height = h, Max = kind == HmiWidget.Trend ? 1.2 : 10 };

            var motor = W(HmiWidget.Motor, string.Empty, "İtici", 16, 176, 248, 150);
            motor.SetRole(HmiWidget.RoleStart, "Dugme");
            motor.SetRole(HmiWidget.RoleRunning, "Itici");
            var trend = W(HmiWidget.Trend, "Flas", "Flaş ve lamba", 280, 176, 280, 200);
            trend.SetRole("2", "Lamba");
            trend.Seconds = 5;
            world.Hmi = new List<HmiWidget>
            {
                W(HmiWidget.Text, string.Empty, "Hücre paneli", 16, 12, 300, 40),
                W(HmiWidget.Lamp, "Flas", "Flaş", 16, 64, 120, 96),
                W(HmiWidget.Lamp, "Lamba", "Lamba", 144, 64, 120, 96),
                W(HmiWidget.Button, "Dugme", "Düğme", 272, 64, 120, 96),
                W(HmiWidget.Number, "Adim", "Adım", 400, 64, 160, 96),
                W(HmiWidget.Gauge, "Sayi", "Sayaç", 576, 64, 208, 184),
                motor,
                trend,
                W(HmiWidget.Alarms, string.Empty, "Alarmlar", 576, 264, 208, 200),
            };
            world.HmiAlarms = new List<HmiAlarm>
            {
                new HmiAlarm { Plc = plc.Id, Variable = "Flas", Level = HmiAlarm.Message, Condition = "=", Threshold = 1, Text = "Flaş yanıyor" },
                new HmiAlarm { Plc = plc.Id, Variable = "Adim", Level = HmiAlarm.HighHigh, Condition = ">=", Threshold = 10, Text = "İtici çalışıyor" },
            };
        }

        private void HmiSnapshotFinish()
        {
            if (_snapshotModes.Contains("plc") && World?.Objects.FirstOrDefault(o => o.PartId == CellParts.PlcId) is { } plcObject)
            {
                // [plc-workspace] "…:plc[:vars|:watch]": the PLC's workspace on that tab
                var ws = OpenPlcWorkspace(plcObject);
                if (ws == null) return;
                ws.ShowForSnapshot(_snapshotModes.Contains("vars") ? "vars" : _snapshotModes.Contains("watch") ? "watch" : "program", new[] { "Flas", "Adim" });
                ws.UpdateLayout();
                App.SnapshotTarget = VisualTreeHelper.GetChildrenCount(ws) > 0 ? VisualTreeHelper.GetChild(ws, 0) as FrameworkElement : ws.Content as FrameworkElement;
                return;
            }

            if (_snapshotModes.Contains("hmi-designer"))
            {
                OpenHmiDesignerForSnapshot();
            }
            else if (_snapshotModes.Contains("hmi") && _dockHmi != null)
            {
                _hmiScreenShot = _snapshotModes.Contains("screen");   // the panel alone, live, at its own size (the docs' picture)
                RevealDock("hmi", _dockHmi);
                UpdateHmiAlarms();
                BuildHmiPanel();
            }
        }

        /// <summary>--snapshot "…:hmi-designer": the designer on the world's panel, the first element selected.</summary>
        internal void OpenHmiDesignerForSnapshot()
        {
            OpenHmiDesigner();
            if (_hmiDesigner == null) return;
            _hmiDesigner.SelectFirstForSnapshot();
            _hmiDesigner.UpdateLayout();
            App.SnapshotTarget = VisualTreeHelper.GetChildrenCount(_hmiDesigner) > 0 ? VisualTreeHelper.GetChild(_hmiDesigner, 0) as FrameworkElement : _hmiDesigner.Content as FrameworkElement;
        }
    }
}
