using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Industrial;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [hmi] The world's operator panel, a dock tab like the sensors: lamps, push buttons, switches, numbers, set points and
    /// bars bound to the PLCs' variables. While the world runs they show the program's values and write into it (a button
    /// holds its variable TRUE while pressed, as an operator's finger on a panel); when it is stopped they are laid out.
    /// </summary>
    public partial class WorldPage
    {
        private RadioButton? _dockHmi;
        private ScrollViewer? _hmiPanel;
        private WrapPanel? _hmiWidgets;
        private readonly List<Action> _hmiRefresh = new List<Action>();
        private DateTime _lastHmi = DateTime.MinValue;

        private void BuildHmiDock()
        {
            if (DockRf.Parent is not Panel tabs || LogList.Parent is not Grid host) return;
            _dockHmi = new RadioButton
            {
                Style = (Style)FindResource("Chip"), Content = "HMI", GroupName = "dock", Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Operatör paneli: PLC değişkenlerine bağlı lambalar, düğmeler, anahtarlar, sayılar ve ayar değerleri"
            };
            tabs.Children.Add(_dockHmi);
            _hmiWidgets = new WrapPanel { Orientation = Orientation.Horizontal };
            _hmiPanel = new ScrollViewer { Visibility = Visibility.Collapsed, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _hmiWidgets };
            host.Children.Add(_hmiPanel);
            _dockHmi.Checked += (_, _) =>
            {
                if (_dock == null) BuildHmiPanel();
            };
            _session.Updated += (_, _) =>
            {
                if ((DateTime.Now - _lastHmi).TotalMilliseconds < 100 || !DockShowing("hmi", _dockHmi)) return;
                _lastHmi = DateTime.Now;
                foreach (var refresh in _hmiRefresh) refresh();
            };
            _session.StateChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (DockShowing("hmi", _dockHmi)) BuildHmiPanel();
            }));
        }

        /// <summary>The PLC program of <paramref name="plc"/> while the world runs.</summary>
        private StProgram? LiveProgram(string plc) => _session.State == SimState.Stopped ? null : _session.Physics?.PlcOf(plc)?.Program;

        private double HmiRead(HmiWidget w) => LiveProgram(w.Plc)?.Read(w.Variable) ?? double.NaN;

        private void HmiWrite(HmiWidget w, double value)
        {
            if (LiveProgram(w.Plc) is not { } program) return;
            using (SimThread.Hold())
            {
                if (!program.Write(w.Variable, value)) W.Status = $"{w.Variable} yazılamaz (bir girişe bağlı ya da tanımlı değil).";
            }
        }

        private void BuildHmiPanel()
        {
            if (_hmiWidgets == null || World is not { } world) return;
            _hmiWidgets.Children.Clear();
            _hmiRefresh.Clear();
            var plcs = world.Objects.Where(o => o.PartId == CellParts.PlcId).ToList();
            if (plcs.Count == 0)
            {
                _hmiWidgets.Children.Add(Note("Operatör paneli PLC değişkenlerine bağlanır: önce Parçalar → Robot Hücresi'nden bir PLC ekleyin."));
                return;
            }

            foreach (var w in world.Hmi ?? new List<HmiWidget>()) _hmiWidgets.Children.Add(HmiCard(world, w));
            _hmiWidgets.Children.Add(HmiAdder(world, plcs));
            foreach (var refresh in _hmiRefresh) refresh();
        }

        private Border HmiFrame(UIElement content, double width = 150) => new Border
        {
            Width = width,
            MinHeight = 96,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(10, 8, 10, 8),
            Background = (Brush)FindResource("B.Raised"),
            BorderBrush = (Brush)FindResource("B.Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = content
        };

        private FrameworkElement HmiCard(WorldDocument world, HmiWidget w)
        {
            var stack = new StackPanel();
            var head = new DockPanel { LastChildFill = true };
            var remove = new Button { Content = "×", Style = (Style)FindResource("Btn.Ghost"), Padding = new Thickness(4, 0, 4, 0), MinHeight = 0, Height = 18, FontSize = 12, ToolTip = "Göstergeyi kaldır" };
            remove.Click += (_, _) =>
            {
                Edit("HMI göstergesi kaldırıldı", () => world.Hmi?.Remove(w));
                BuildHmiPanel();
            };
            DockPanel.SetDock(remove, Dock.Right);
            head.Children.Add(remove);
            head.Children.Add(new TextBlock { Text = w.Label.Length > 0 ? w.Label : w.Variable, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            stack.Children.Add(head);
            string plcName = world.Find(w.Plc)?.Name ?? "?";
            stack.Children.Add(new TextBlock { Text = $"{plcName} · {w.Variable}", FontSize = Shell.TypeScale.Caption, Foreground = (Brush)FindResource("B.TextSecondary"), Margin = new Thickness(0, 0, 0, 6) });
            var on = new SolidColorBrush(Color.FromRgb(0x3C, 0xD0, 0x64));
            var off = (Brush)FindResource("B.Border");
            switch (w.Kind)
            {
                case HmiWidget.Lamp:
                {
                    var lamp = new Ellipse { Width = 30, Height = 30, Fill = off, HorizontalAlignment = HorizontalAlignment.Left };
                    stack.Children.Add(lamp);
                    _hmiRefresh.Add(() => lamp.Fill = HmiRead(w) is var v && !double.IsNaN(v) && v != 0 ? on : off);
                    break;
                }

                case HmiWidget.Button:
                {
                    var button = new Button { Content = w.Label.Length > 0 ? w.Label : "Bas", Padding = new Thickness(10, 6, 10, 6), HorizontalAlignment = HorizontalAlignment.Left };
                    button.PreviewMouseLeftButtonDown += (_, _) => HmiWrite(w, 1);
                    button.PreviewMouseLeftButtonUp += (_, _) => HmiWrite(w, 0);
                    button.MouseLeave += (_, e) =>
                    {
                        if (e.LeftButton == MouseButtonState.Pressed) HmiWrite(w, 0);
                    };
                    stack.Children.Add(button);
                    break;
                }

                case HmiWidget.Switch:
                {
                    var toggle = new CheckBox { Content = "Açık", Style = (Style)FindResource("Switch") };
                    bool updating = false;
                    toggle.Click += (_, _) =>
                    {
                        if (!updating) HmiWrite(w, toggle.IsChecked == true ? 1 : 0);
                    };
                    stack.Children.Add(toggle);
                    _hmiRefresh.Add(() =>
                    {
                        updating = true;
                        double v = HmiRead(w);
                        toggle.IsChecked = !double.IsNaN(v) && v != 0;
                        toggle.IsEnabled = !double.IsNaN(v);
                        updating = false;
                    });
                    break;
                }

                case HmiWidget.Number:
                {
                    var number = new TextBlock { FontSize = Shell.TypeScale.Headline, FontFamily = (FontFamily)FindResource("F.Mono") };
                    stack.Children.Add(number);
                    _hmiRefresh.Add(() =>
                    {
                        double v = HmiRead(w);
                        number.Text = double.IsNaN(v) ? "—" : v.ToString(Math.Abs(v - Math.Round(v)) < 1e-9 ? "0" : "0.###", CultureInfo.CurrentCulture) + (w.Unit.Length > 0 ? " " + w.Unit : string.Empty);
                    });
                    break;
                }

                case HmiWidget.SetPoint:
                {
                    var box = new TextBox { Width = 90, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Değeri yazıp Enter'a basın" };
                    box.KeyDown += (_, e) =>
                    {
                        if (e.Key != Key.Enter) return;
                        if (double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) HmiWrite(w, Math.Max(w.Min, Math.Min(w.Max, v)));
                    };
                    stack.Children.Add(box);
                    _hmiRefresh.Add(() =>
                    {
                        if (box.IsKeyboardFocusWithin) return;
                        double v = HmiRead(w);
                        box.Text = double.IsNaN(v) ? string.Empty : v.ToString("0.###", CultureInfo.InvariantCulture);
                    });
                    break;
                }

                default:
                {
                    var bar = new ProgressBar { Minimum = w.Min, Maximum = w.Max > w.Min ? w.Max : w.Min + 1, Height = 14 };
                    var value = new TextBlock { FontSize = Shell.TypeScale.Caption, Margin = new Thickness(0, 4, 0, 0) };
                    stack.Children.Add(bar);
                    stack.Children.Add(value);
                    _hmiRefresh.Add(() =>
                    {
                        double v = HmiRead(w);
                        bar.Value = double.IsNaN(v) ? bar.Minimum : Math.Max(bar.Minimum, Math.Min(bar.Maximum, v));
                        value.Text = double.IsNaN(v) ? string.Empty : v.ToString("0.##", CultureInfo.CurrentCulture) + (w.Unit.Length > 0 ? " " + w.Unit : string.Empty);
                    });
                    break;
                }
            }

            return HmiFrame(stack);
        }

        /// <summary>The "+ Gösterge" card: what kind, which PLC and variable, a label.</summary>
        private FrameworkElement HmiAdder(WorldDocument world, List<WorldObject> plcs)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "+ Gösterge", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            var kind = new ComboBox { ItemsSource = HmiWidget.Kinds, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 4) };
            var plc = new ComboBox { ItemsSource = plcs.Select(p => p.Name).ToList(), SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 4) };
            var variable = new ComboBox { IsEditable = true, Margin = new Thickness(0, 0, 0, 4), ToolTip = "Programdaki değişken ya da %MW3 gibi bir adres" };
            var label = new TextBox { Margin = new Thickness(0, 0, 0, 4), ToolTip = "Göstergenin adı (boşsa değişkenin adı)" };
            void FillVariables()
            {
                var o = plcs.ElementAtOrDefault(Math.Max(0, plc.SelectedIndex));
                variable.ItemsSource = o == null ? new List<string>() : PlcVariables(o);
            }

            plc.SelectionChanged += (_, _) => FillVariables();
            FillVariables();
            stack.Children.Add(kind);
            stack.Children.Add(plc);
            stack.Children.Add(variable);
            stack.Children.Add(label);
            var add = new Button { Content = "Ekle", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left };
            add.Click += (_, _) =>
            {
                var o = plcs.ElementAtOrDefault(Math.Max(0, plc.SelectedIndex));
                string name = (variable.Text ?? string.Empty).Trim();
                if (o == null || name.Length == 0)
                {
                    W.Status = "Bir PLC ve bir değişken seçin.";
                    return;
                }

                var w = new HmiWidget { Kind = (string)kind.SelectedItem, Plc = o.Id, Variable = name, Label = label.Text.Trim() };
                Edit("HMI göstergesi eklendi", () => (world.Hmi ??= new List<HmiWidget>()).Add(w));
                BuildHmiPanel();
            };
            stack.Children.Add(add);
            return HmiFrame(stack, 190);
        }

        /// <summary>A PLC's variables (its program compiled now), for choosing what a widget shows.</summary>
        private static List<string> PlcVariables(WorldObject plc)
        {
            StProgram? program;
            if (plc.Parameters.TryGetValue("language", out var language) && language.Trim() == CellParts.PlcLadder)
                program = LdProgram.Load(plc.Parameters.TryGetValue("ladder", out var json) ? json : string.Empty).Compile(out _);
            else
                program = StProgram.Compile(plc.Parameters.TryGetValue("program", out var text) && !string.IsNullOrWhiteSpace(text) ? text : CellParts.PlcExample, out _);
            return program?.Watch.Select(v => v.Name).Where(n => !n.StartsWith("_", StringComparison.Ordinal)).ToList() ?? new List<string>();
        }
    }
}
