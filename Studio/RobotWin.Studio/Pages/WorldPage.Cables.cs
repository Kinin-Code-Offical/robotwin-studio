using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// Cable length check (is the jumper long enough, or is it pulled tight?) and voltage drop for
    /// the "Kablolar ve ölçümler" table and the cable inspector.
    /// </summary>
    public partial class WorldPage
    {
        /// <summary>Standard jumper lengths offered in the inspector (m); 0 = automatic length.</summary>
        private static readonly double[] JumperLengths = { 0, 0.10, 0.15, 0.20, 0.30, 0.50 };

        /// <summary>
        /// Length state of a cable: straight pin-to-pin distance it has to bridge, the length it
        /// has, and a short verdict. While simulating, the rope's real tension decides "tight".
        /// </summary>
        private (double Span, double Length, string Verdict, bool Bad) CableFit(WorldCable c)
        {
            var world = World;
            double len = c.Length > 0 ? c.Length : Scene.CableLength(c);
            var a = world?.Find(c.A.ObjectId);
            var b = world?.Find(c.B.ObjectId);
            if (a == null || b == null) return (0, len, string.Empty, false);
            double span = (Scene.WorldAnchor(a, c.A.Pin) - Scene.WorldAnchor(b, c.B.Pin)).Length;

            if (_session.State != SimState.Stopped && _session.Physics?.RopeOf(c.Id) is { } rope)
            {
                if (!rope.A.Attached || !rope.B.Attached) return (span, len, "fiş çıktı", true);
                double pull = Math.Max(rope.A.Tension, rope.B.Tension);
                if (pull > 0.5 * rope.PullOutForce) return (span, len, $"gergin, {pull:0.0} N çekiyor", true);
            }

            if (span > len) return (span, len, $"yetmiyor ({(span - len) * 100:0} cm kısa)", true);
            double ratio = span / Math.Max(len, 1e-6);
            if (ratio > 0.95) return (span, len, "gergin", true);
            if (ratio > 0.6) return (span, len, "yeterli", false);
            return (span, len, "bol", false);
        }

        /// <summary>Voltage drop along the cable (V) from the simulated current and its copper resistance.</summary>
        private double CableDrop(WorldCable c, double length)
        {
            if (_session.State == SimState.Stopped) return 0;
            var (i, burned, _) = _session.CableState(c);
            double drop = burned ? 0 : Math.Abs(i) * WorldCable.Resistance(c.Awg, length);
            return drop < 1e-6 ? 0 : drop;   // signal lines: below a microvolt is just solver noise
        }

        private ComboBox JumperLengthBox(WorldCable cable)
        {
            var box = new ComboBox { Width = 72, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Kablo boyu (hazır jumper boyları)" };
            bool matched = false;
            foreach (double l in JumperLengths)
            {
                bool sel = l == 0 ? cable.Length <= 0 : Math.Abs(cable.Length - l) < 0.0005;
                matched |= sel;
                box.Items.Add(new ComboBoxItem { Content = l == 0 ? "oto" : (l * 100).ToString("0", CultureInfo.InvariantCulture) + " cm", Tag = l, IsSelected = sel });
            }

            if (!matched) box.Items.Add(new ComboBoxItem { Content = (cable.Length * 100).ToString("0.#", CultureInfo.InvariantCulture) + " cm", Tag = cable.Length, IsSelected = true });
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is ComboBoxItem it && it.Tag is double l && Math.Abs(l - cable.Length) > 0.0001)
                    Edit("Kablo boyu değişti", () => cable.Length = l);
            };
            return box;
        }
    }
}
