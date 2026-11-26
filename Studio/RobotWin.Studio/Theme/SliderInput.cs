using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RobotWin.Studio.Theme
{
    /// <summary>
    /// [slider-input] A slider's typed twin: a box beside it shows the value with its unit and takes a typed one, so a
    /// value can be given to the millimetre instead of dragged (SolidWorks' and Fusion's spin boxes beside their sliders).
    /// Enter or leaving the box applies it (clamped to the slider's range), Esc puts back the shown value, ↑/↓ step by the
    /// slider's small change (Shift: the large change). A slider whose value is not what is typed (a log scale, a fraction
    /// shown as a percent) gives the two conversions.
    /// </summary>
    public static class SliderInput
    {
        private static readonly Regex Number = new Regex(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant);

        /// <summary>A new box for a slider.</summary>
        public static TextBox Create(Slider slider, string format, string unit = "", Func<double, double>? toShown = null, Func<double, double>? fromShown = null,
            Action? committed = null, double width = 72)
        {
            var box = new TextBox { Width = width, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(6, 0, 0, 0) };
            Attach(slider, box, format, unit, toShown, fromShown, committed);
            return box;
        }

        /// <summary>Keeps a box and a slider together.</summary>
        public static void Attach(Slider slider, TextBox box, string format, string unit = "", Func<double, double>? toShown = null, Func<double, double>? fromShown = null,
            Action? committed = null)
        {
            toShown ??= v => v;
            fromShown ??= v => v;
            Attach(slider, box, v => toShown(v).ToString(format, CultureInfo.CurrentCulture) + unit, text =>
            {
                var m = Number.Match(text);
                return m.Success && double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double typed) ? fromShown(typed) : null;
            }, committed);
        }

        /// <summary>
        /// [units] A slider over millimetres, its box in the chosen length unit (Settings → Birimler) and reading any unit
        /// typed after the number ("12.5 mm", "0.5 in", "2 cm"); it follows a change of the unit.
        /// </summary>
        public static TextBox CreateLength(Slider slider, Action? committed = null, double width = 86)
        {
            var box = new TextBox { Width = width, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(6, 0, 0, 0) };
            AttachLength(slider, box, committed);
            return box;
        }

        public static void AttachLength(Slider slider, TextBox box, Action? committed = null)
        {
            var show = Attach(slider, box, v => RobotWin.Studio.Shell.LengthUnits.Format(v), text => RobotWin.Studio.Shell.LengthUnits.TryParse(text, out double mm) ? mm : null, committed);
            EventHandler follow = (_, _) => show();
            box.Loaded += (_, _) => RobotWin.Studio.Shell.LengthUnits.Changed += follow;
            box.Unloaded += (_, _) => RobotWin.Studio.Shell.LengthUnits.Changed -= follow;
        }

        /// <summary>The core: shows the slider's value through <paramref name="show"/>, reads a typed one through <paramref name="parse"/> (null: not a value). Returns its "show again".</summary>
        public static Action Attach(Slider slider, TextBox box, Func<double, string> show, Func<string, double?> parse, Action? committed = null)
        {
            if (box.ToolTip == null) box.ToolTip = RobotWin.Studio.Shell.Lang.T("Değeri yazıp Enter'a basın · ↑/↓ adım (Shift: büyük adım)");
            void Show() => box.Text = show(slider.Value);
            Show();
            slider.ValueChanged += (_, _) =>
            {
                if (!box.IsKeyboardFocusWithin) Show();
            };

            bool Apply()
            {
                if (parse(box.Text) is not double v || double.IsNaN(v) || double.IsInfinity(v))
                {
                    Show();
                    return false;
                }

                v = Math.Max(slider.Minimum, Math.Min(slider.Maximum, v));
                bool changed = Math.Abs(v - slider.Value) > 1e-12;
                slider.Value = v;
                Show();
                if (changed) committed?.Invoke();
                return changed;
            }

            box.KeyDown += (_, e) =>
            {
                switch (e.Key)
                {
                    case Key.Enter:
                        Apply();
                        box.SelectAll();
                        e.Handled = true;
                        break;
                    case Key.Escape:
                        Show();
                        Keyboard.ClearFocus();
                        e.Handled = true;
                        break;
                }
            };
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Up && e.Key != Key.Down) return;
                double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? slider.LargeChange : slider.SmallChange;
                double v = Math.Max(slider.Minimum, Math.Min(slider.Maximum, slider.Value + (e.Key == Key.Up ? step : -step)));
                slider.Value = v;
                Show();
                box.SelectAll();
                committed?.Invoke();
                e.Handled = true;
            };
            box.GotKeyboardFocus += (_, _) => box.SelectAll();
            box.LostKeyboardFocus += (_, _) => Apply();
            return Show;
        }
    }
}
