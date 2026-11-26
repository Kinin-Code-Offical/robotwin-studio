using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [rigidbody] Unity's Rigidbody settings on the Fizik card: linear and angular damping, and its Constraints
    /// (frozen position and rotation axes). They live in the part's parameters (rb.damping, rb.angdamping,
    /// rb.lockpos, rb.lockrot) and reach the solver through WorldPhysics.
    /// </summary>
    public partial class WorldPage
    {
        /// <summary>Whether the "Kısıtlar ve sönüm" fold is open (kept while the app runs).</summary>
        private static bool _rigidbodyOpen;

        private void RigidbodyFields(WorldObject o)
        {
            if (o.IsStatic) return;   // a fixed part does not move: nothing to damp or freeze
            bool used = new[] { "rb.damping", "rb.angdamping", "rb.lockpos", "rb.lockrot" }.Any(k => o.Parameters.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v) && v != "0");
            bool open = _rigidbodyOpen || used;

            var head = new Button
            {
                Content = (open ? "▾ " : "▸ ") + "Kısıtlar ve sönüm",
                Style = (Style)FindResource("Btn.Ghost"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 4),
                ToolTip = "Unity'deki Rigidbody ayarları: hızın ve dönüşün saniyede ne kadarının söndüğü (yağ, sıvı, fren) ve parçanın hareket edemeyeceği ya da dönemeyeceği dünya eksenleri"
            };
            head.Click += (_, _) =>
            {
                _rigidbodyOpen = !open;
                BuildInspector();
            };
            Inspector.Children.Add(head);
            if (!open) return;

            Inspector.Children.Add(NumberField("Doğrusal sönüm (1/s)", Param(o, "rb.damping"), v => Edit("Sönüm değişti", () => SetParam(o, "rb.damping", v)), 0, 1000, "0.###"));
            Inspector.Children.Add(NumberField("Açısal sönüm (1/s)", Param(o, "rb.angdamping"), v => Edit("Sönüm değişti", () => SetParam(o, "rb.angdamping", v)), 0, 1000, "0.###"));
            Inspector.Children.Add(Labeled("Konumu dondur", AxisChips(o, "rb.lockpos", "Bu dünya ekseni boyunca hareket etmez")));
            Inspector.Children.Add(Labeled("Dönüşü dondur", AxisChips(o, "rb.lockrot", "Bu dünya ekseni çevresinde dönmez")));
        }

        private static double Param(WorldObject o, string key) =>
            o.Parameters.TryGetValue(key, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

        private static void SetParam(WorldObject o, string key, double v)
        {
            if (v <= 0) o.Parameters.Remove(key);
            else o.Parameters[key] = v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        /// <summary>X · Y · Z chips for one axis mask parameter ("xz").</summary>
        private FrameworkElement AxisChips(WorldObject o, string key, string tip)
        {
            string now = o.Parameters.TryGetValue(key, out var s) ? s.ToLowerInvariant() : "";
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (char axis in "xyz")
            {
                var chip = new CheckBox   // Unity's Constraints: one box per axis
                {
                    Content = char.ToUpperInvariant(axis).ToString(),
                    IsChecked = now.Contains(axis),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 14, 0),
                    ToolTip = tip + " (" + char.ToUpperInvariant(axis) + ")"
                };
                char a = axis;
                chip.Click += (_, _) => Edit("Kısıt değişti", () =>
                {
                    string cur = o.Parameters.TryGetValue(key, out var c) ? c.ToLowerInvariant() : "";
                    cur = chip.IsChecked == true ? (cur.Contains(a) ? cur : cur + a) : cur.Replace(a.ToString(), "");
                    cur = new string("xyz".Where(cur.Contains).ToArray());
                    if (cur.Length == 0) o.Parameters.Remove(key);
                    else o.Parameters[key] = cur;
                });
                row.Children.Add(chip);
            }

            return row;
        }
    }
}
