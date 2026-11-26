using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RobotWin.Studio.Modeling
{
    /// <summary>
    /// Label + numeric text box. Dragging the label left/right scrubs the value (Shift = fine,
    /// Ctrl = whole steps), Enter or leaving the box commits the typed value.
    /// </summary>
    public sealed class NumberField : Grid
    {
        private readonly TextBox _box = new TextBox();
        private readonly TextBlock _label = new TextBlock();
        private double _value;
        private string? _formula;
        private readonly string _labelText;
        private Point? _drag;
        private double _dragStart;
        private bool _scrubbing;

        public NumberField(string label, double value, string unit = "", double step = 0.5, int decimals = 2)
        {
            Unit = unit;
            Step = step;
            Decimals = decimals;
            _value = value;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            Margin = new Thickness(0, 0, 0, 4);
            _label.Text = label;
            _labelText = label;
            _label.FontSize = 12;
            _label.Foreground = (Brush)Application.Current.FindResource("B.TextSecondary");
            _label.VerticalAlignment = VerticalAlignment.Center;
            _label.Cursor = Cursors.SizeWE;
            _label.Background = Brushes.Transparent;
            _label.TextTrimming = TextTrimming.CharacterEllipsis;
            _label.ToolTip = "Sürükleyerek değiştirin (Shift ince, Ctrl tam adım) veya değer yazın";
            _box.FontFamily = (FontFamily)Application.Current.FindResource("F.Mono");
            _box.FontSize = 12;
            _box.Padding = new Thickness(6, 2, 6, 2);
            _box.MinHeight = 24;
            SetColumn(_box, 1);
            Children.Add(_label);
            Children.Add(_box);
            Show();
            if (IsLength)
            {
                // [units] shown in the chosen unit, and again when it changes
                EventHandler follow = (_, _) => Show();
                Loaded += (_, _) => RobotWin.Studio.Shell.LengthUnits.Changed += follow;
                Unloaded += (_, _) => RobotWin.Studio.Shell.LengthUnits.Changed -= follow;
            }
            _box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitText();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    Show();
                    Keyboard.ClearFocus();
                    e.Handled = true;
                }
                else if (e.Key == Key.Up || e.Key == Key.Down)
                {
                    SetValue(_value + (e.Key == Key.Up ? Step : -Step), true);
                    e.Handled = true;
                }
            };
            _box.LostKeyboardFocus += (_, _) => CommitText();
            _box.GotKeyboardFocus += (_, _) =>
            {
                // A bound value is edited as its formula.
                if (_formula != null && FormulaCommitted != null) _box.Text = "=" + _formula;
            };
            _label.MouseLeftButtonDown += (_, e) =>
            {
                _drag = e.GetPosition(this);
                _dragStart = _value;
                _scrubbing = false;
                _label.CaptureMouse();
                e.Handled = true;
            };
            _label.MouseMove += (_, e) =>
            {
                if (_drag == null || e.LeftButton != MouseButtonState.Pressed) return;
                double dx = e.GetPosition(this).X - _drag.Value.X;
                if (!_scrubbing && Math.Abs(dx) < 3) return;
                if (!_scrubbing)
                {
                    _scrubbing = true;
                    ScrubStarted?.Invoke();
                }

                double k = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 1;
                double v = _dragStart + dx * Step * 0.2 * k;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) v = Math.Round(v / Step) * Step;
                SetValue(v, false);
            };
            _label.MouseLeftButtonUp += (_, _) =>
            {
                if (_drag == null) return;
                _drag = null;
                _label.ReleaseMouseCapture();
                if (_scrubbing)
                {
                    _scrubbing = false;
                    ScrubEnded?.Invoke(_value);
                }
                else
                {
                    _box.Focus();
                    _box.SelectAll();
                }
            };
        }

        public string Unit { get; }

        /// <summary>[units] A length (its values in millimetres): shown in the chosen unit, typed in any.</summary>
        private bool IsLength => Unit == "mm";
        public double Step { get; set; }
        public int Decimals { get; set; }
        public double Min { get; set; } = double.NegativeInfinity;
        public double Max { get; set; } = double.PositiveInfinity;
        public bool Integer { get; set; }

        public double Value => _value;

        /// <summary>
        /// [param] The formula the value comes from ("genislik / 2"), shown in the box while it is being edited; typing
        /// "=…" binds one and typing a plain number unbinds it (<see cref="FormulaCommitted"/>, set only where a value can
        /// take a formula).
        /// </summary>
        public string? Formula
        {
            get => _formula;
            set
            {
                _formula = string.IsNullOrWhiteSpace(value) ? null : value;
                _label.Text = _formula != null ? "ƒ " + _labelText : _labelText;
                _box.ToolTip = _formula != null ? "= " + _formula : null;
                _box.FontStyle = _formula != null ? FontStyles.Italic : FontStyles.Normal;
            }
        }

        /// <summary>[param] A formula typed ("=…"), or null when a plain number replaced the formula.</summary>
        public event Action<string?>? FormulaCommitted;

        /// <summary>Typed or stepped value (final).</summary>
        public event Action<double>? Committed;

        /// <summary>Value while dragging the label.</summary>
        public event Action<double>? Scrubbed;

        public event Action? ScrubStarted;
        public event Action<double>? ScrubEnded;

        private void SetValue(double v, bool final)
        {
            v = Math.Max(Min, Math.Min(Max, v));
            if (Integer) v = Math.Round(v);
            _value = v;
            Show();
            if (final) Committed?.Invoke(v);
            else Scrubbed?.Invoke(v);
        }

        private void Show() => _box.Text = IsLength && !Integer
            ? RobotWin.Studio.Shell.LengthUnits.Format(_value, Decimals, CultureInfo.InvariantCulture)
            : Format(_value) + (Unit.Length > 0 ? " " + Unit : string.Empty);

        private string Format(double v) => Integer ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString("0." + new string('#', Math.Max(1, Decimals)), CultureInfo.InvariantCulture);

        private void CommitText()
        {
            string raw = _box.Text.Trim();
            if (FormulaCommitted != null && raw.StartsWith("=", StringComparison.Ordinal))
            {
                string formula = raw.Substring(1).Trim();
                if (formula.Length > 0 && formula != _formula) FormulaCommitted(formula);
                else Show();
                return;
            }

            if (IsLength && !Integer)
            {
                // [units] "25.4 mm", "1 in", "2*3 cm" or a bare number in the chosen unit
                if (RobotWin.Studio.Shell.LengthUnits.TryParse(raw, out double mm, s => (TryEval(s, out double x), x)))
                {
                    if (_formula != null && FormulaCommitted != null) FormulaCommitted(null);
                    if (Math.Abs(mm - _value) > 1e-12) SetValue(mm, true);
                    else Show();
                }
                else Show();
                return;
            }

            string t = _box.Text.Replace(Unit, string.Empty).Replace("°", string.Empty).Replace("%", string.Empty).Trim();
            if (TryEval(t, out double v))
            {
                if (_formula != null && FormulaCommitted != null)
                {
                    // A plain number in place of the formula: the formula goes, the number stays.
                    FormulaCommitted(null);
                    SetValue(v, true);
                    return;
                }
            }

            if (TryEval(t, out v))
            {
                if (Math.Abs(v - _value) > 1e-12) SetValue(v, true);
                else Show();
            }
            else Show();
        }

        /// <summary>Numbers and arithmetic ("20*2", "100/3", "2,5", "sqrt(2)*10"): Modeling/Expressions.cs.</summary>
        internal static bool TryEval(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            return RobotTwin.CoreSim.Modeling.Expression.TryEvaluate(text, null, out value, out _);
        }
    }

    /// <summary>Themed dialog with a few numeric fields (array counts, offsets, distances …).</summary>
    public static class ParamDialog
    {
        public static double[]? Show(Window? owner, string title, string message, params (string Label, double Value)[] fields)
        {
            double[]? result = null;
            var window = new Window
            {
                Owner = owner,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false
            };
            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 18), Width = 340 };
            panel.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.FindResource("Text.Title"), FontSize = 17 });
            if (!string.IsNullOrEmpty(message))
                panel.Children.Add(new TextBlock { Text = message, Style = (Style)Application.Current.FindResource("Text.Body"), Margin = new Thickness(0, 6, 0, 12) });
            var boxes = new List<TextBox>();
            foreach (var (label, value) in fields)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                row.Children.Add(new TextBlock { Text = label, Foreground = (Brush)Application.Current.FindResource("B.TextSecondary"), VerticalAlignment = VerticalAlignment.Center });
                var box = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), FontFamily = (FontFamily)Application.Current.FindResource("F.Mono"), Padding = new Thickness(6, 3, 6, 3) };
                Grid.SetColumn(box, 1);
                row.Children.Add(box);
                panel.Children.Add(row);
                boxes.Add(box);
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var ok = new Button { Content = "Uygula", MinWidth = 90, IsDefault = true, Style = (Style)Application.Current.FindResource("Btn.Primary") };
            var cancel = new Button { Content = "İptal", MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            ok.Click += (_, _) =>
            {
                var vals = new double[boxes.Count];
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (!NumberField.TryEval(boxes[i].Text.Replace(',', '.'), out vals[i]))
                    {
                        boxes[i].Focus();
                        boxes[i].SelectAll();
                        return;
                    }
                }

                result = vals;
                window.Close();
            };
            cancel.Click += (_, _) => window.Close();
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            var card = new Border
            {
                Margin = new Thickness(20),
                Background = (Brush)Application.Current.FindResource("B.Overlay"),
                BorderBrush = (Brush)Application.Current.FindResource("B.BorderStrong"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = panel
            };
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) window.DragMove();
            };
            window.Content = card;
            window.Loaded += (_, _) =>
            {
                if (boxes.Count > 0)
                {
                    boxes[0].Focus();
                    boxes[0].SelectAll();
                }
            };
            window.ShowDialog();
            return result;
        }
    }
}
