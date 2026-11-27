using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [hmi] One element of the world's operator panel (HMI), bound to a PLC's variable: a lamp, a push button, a switch, a
    /// number, a set point, a bar, a dial, a trend, a motor faceplate, the alarm list or a text. It sits on the panel at
    /// <see cref="X"/>, <see cref="Y"/> with its size (panel pixels at 100 %). Kept with the world, so a saved cell keeps
    /// its panel.
    /// </summary>
    public sealed class HmiWidget
    {
        public const string Lamp = "lamba", Button = "düğme", Switch = "anahtar", Number = "sayı", SetPoint = "ayar", Bar = "çubuk";

        /// <summary>[hmi-designer] A dial with its scale, a trend of up to four variables, a motor's faceplate, the alarm list, a text.</summary>
        public const string Gauge = "kadran", Trend = "trend", Motor = "motor", Alarms = "alarmlar", Text = "yazı";

        /// <summary>[hmi-screens] A button that shows another screen of the panel (<see cref="Target"/>).</summary>
        public const string Navigate = "ekran";

        public static readonly string[] Kinds = { Lamp, Button, Switch, Number, SetPoint, Bar, Gauge, Trend, Motor, Alarms, Text, Navigate };

        /// <summary>The kinds that show a value (no variable needed for the alarm list, a text or a screen button).</summary>
        public static bool Bound(string kind) => kind != Alarms && kind != Text && kind != Navigate;

        /// <summary>[hmi-screens] The screen it is on (empty: the first one).</summary>
        public string Screen { get; set; } = string.Empty;

        /// <summary>[hmi-screens] A screen button's screen.</summary>
        public string Target { get; set; } = string.Empty;

        /// <summary>A motor faceplate's roles: start and stop (pulsed while pressed), running and fault (lamps).</summary>
        public const string RoleStart = "başlat", RoleStop = "durdur", RoleRunning = "çalışıyor", RoleFault = "arıza";

        public string Kind { get; set; } = Lamp;

        public string Label { get; set; } = string.Empty;

        /// <summary>The PLC (its object id) and the variable (as the program names it, or an address such as %MW3).</summary>
        public string Plc { get; set; } = string.Empty;

        public string Variable { get; set; } = string.Empty;

        /// <summary>A bar's, dial's, trend's or set point's range, and the unit shown after a number.</summary>
        public double Min { get; set; }

        public double Max { get; set; } = 100;

        public string Unit { get; set; } = string.Empty;

        /// <summary>[hmi-designer] Where it sits on the panel and how large it is (panel pixels); 0 × 0: not placed yet.</summary>
        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        /// <summary>
        /// [hmi-designer] More variables by role: a trend's further series ("2", "3", "4"), a motor's
        /// <see cref="RoleStart"/>, <see cref="RoleStop"/>, <see cref="RoleRunning"/>, <see cref="RoleFault"/>. Null when none.
        /// </summary>
        public Dictionary<string, string>? Roles { get; set; }

        /// <summary>[hmi-designer] A trend's window: how many seconds it shows.</summary>
        public double Seconds { get; set; } = 30;

        public string Role(string role) => Roles != null && Roles.TryGetValue(role, out var v) ? v : string.Empty;

        public void SetRole(string role, string variable)
        {
            if (string.IsNullOrWhiteSpace(variable))
            {
                Roles?.Remove(role);
                if (Roles is { Count: 0 }) Roles = null;
                return;
            }

            (Roles ??= new Dictionary<string, string>(StringComparer.Ordinal))[role] = variable.Trim();
        }

        /// <summary>A trend's series: its main variable, then roles "2", "3", "4" that are set.</summary>
        public IEnumerable<string> Series() =>
            new[] { Variable, Role("2"), Role("3"), Role("4") }.Where(v => v.Length > 0);

        public HmiWidget Clone()
        {
            var c = (HmiWidget)MemberwiseClone();
            c.Roles = Roles == null ? null : new Dictionary<string, string>(Roles, StringComparer.Ordinal);
            return c;
        }

        /// <summary>The size a new one of <paramref name="kind"/> gets (panel pixels).</summary>
        public static (double W, double H) DefaultSize(string kind) => kind switch
        {
            Lamp => (120, 96),
            Button => (120, 72),
            Switch => (140, 72),
            Number => (160, 80),
            SetPoint => (160, 80),
            Bar => (200, 80),
            Gauge => (180, 180),
            Trend => (360, 200),
            Motor => (220, 150),
            Alarms => (360, 160),
            Text => (200, 40),
            Navigate => (160, 56),
            _ => (160, 96),
        };
    }

    /// <summary>[hmi-designer] The operator panel's own settings: its size (an HMI screen's, in pixels) and grid.</summary>
    public sealed class HmiPanelSettings
    {
        public double Width { get; set; } = 800;

        public double Height { get; set; } = 480;

        /// <summary>The grid elements snap to while placed and sized (pixels); 0: free.</summary>
        public double Grid { get; set; } = 8;

        /// <summary>Common HMI screen sizes: 7" (800 × 480), 10" (1024 × 600), 12" (1280 × 800), 15" (1024 × 768).</summary>
        public static readonly (double W, double H)[] Presets = { (800, 480), (1024, 600), (1280, 800), (1024, 768) };

        /// <summary>[hmi-screens] The panel's screens by name, the first the one it starts on; null: one screen.</summary>
        public List<string>? Screens { get; set; }

        public const string MainScreen = "Ana ekran";

        /// <summary>The screens' names (at least the main one).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public IReadOnlyList<string> ScreenNames => Screens is { Count: > 0 } s ? s : new[] { MainScreen };

        /// <summary>The screen a widget is on, by name: its own, or the first when it names none (or one that is gone).</summary>
        public string ScreenOf(HmiWidget w) => ScreenNames.Contains(w.Screen) ? w.Screen : ScreenNames[0];
    }

    /// <summary>[hmi-designer] Places the elements that have no place yet (a panel made before the designer), row by row.</summary>
    public static class HmiLayout
    {
        public static void Place(IList<HmiWidget> widgets, HmiPanelSettings panel)
        {
            // [hmi-screens] each screen lays out its own
            foreach (var screen in widgets.GroupBy(panel.ScreenOf)) PlaceOn(screen.ToList(), panel);
        }

        private static void PlaceOn(IList<HmiWidget> widgets, HmiPanelSettings panel)
        {
            const double gap = 12;
            double x = gap, y = gap, row = 0;
            foreach (var w in widgets.Where(w => w.Width > 0 && w.Height > 0)) y = Math.Max(y, w.Y + w.Height + gap);
            foreach (var w in widgets.Where(w => w.Width <= 0 || w.Height <= 0))
            {
                var (dw, dh) = HmiWidget.DefaultSize(w.Kind);
                if (x + dw > panel.Width - gap && x > gap)
                {
                    x = gap;
                    y += row + gap;
                    row = 0;
                }

                w.X = x;
                w.Y = y;
                w.Width = dw;
                w.Height = dh;
                x += dw + gap;
                row = Math.Max(row, dh);
            }
        }

        /// <summary>
        /// [hmi-screens] A screen renamed: its widgets and the buttons that show it follow; null <paramref name="to"/> removes
        /// it, its widgets going with it and its buttons pointing nowhere (the first screen stays: a panel has at least one).
        /// </summary>
        public static void RenameScreen(IList<HmiWidget> widgets, HmiPanelSettings panel, string from, string? to)
        {
            var names = panel.ScreenNames.ToList();
            int index = names.IndexOf(from);
            if (index < 0) return;
            bool first = index == 0;
            if (to == null)
            {
                if (names.Count <= 1) return;
                names.RemoveAt(index);
                for (int i = widgets.Count - 1; i >= 0; i--)
                    if (panel.ScreenOf(widgets[i]) == from) widgets.RemoveAt(i);
                foreach (var w in widgets.Where(w => w.Target == from)) w.Target = string.Empty;
            }
            else
            {
                if (to.Length == 0 || names.Contains(to)) return;
                names[index] = to;
                foreach (var w in widgets)
                {
                    if (w.Screen == from || (first && w.Screen.Length == 0)) w.Screen = first ? string.Empty : to;
                    if (w.Target == from) w.Target = to;
                }
            }

            panel.Screens = names;
            if (first && to == null) foreach (var w in widgets.Where(w => w.Screen == names[0])) w.Screen = string.Empty;
        }

        /// <summary>A value snapped to the grid (0: as it is).</summary>
        public static double Snap(double v, double grid) => grid > 0 ? Math.Round(v / grid) * grid : v;
    }
}
