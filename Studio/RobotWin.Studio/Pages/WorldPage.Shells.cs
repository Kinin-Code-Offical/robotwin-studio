using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [shell] Hollow bodies on the world page: palette entries (enclosure, fuselage pod, each with
    /// a screw-on lid), the inspector's shell section (wall thickness, cable holes, open/close the
    /// lid, the fit check) and the X-ray / exploded view switches.
    /// </summary>
    public partial class WorldPage
    {
        private void ShellPalette()
        {
            Palette.Children.Add(Caption("Gövde kabukları (içi boş, kapaklı)"));
            Palette.Children.Add(Note("İçine kart, pil, servo koyup kabloları geçirebileceğiniz içi boş gövdeler. Duvarlar içerideki parçalarla çarpışır; kapak vidalanır. Seçince “Sığma denetimi” kapağın kapanıp kapanmadığını ve ezilen kabloları gösterir."));
            Palette.Children.Add(Row("Elektronik kutusu + vidalı kapak", "120 × 80 × 40 mm PLA, 2 mm duvar", () => AddShell(0.12, 0.08, 0.04, "PLA", 0.002, "Kutu")));
            Palette.Children.Add(Row("Uçak gövdesi (pod) + kapak", "260 × 50 × 45 mm balsa, 2 mm duvar", () => AddShell(0.26, 0.05, 0.045, "BALSA", 0.002, "Gövde")));
            Palette.Children.Add(Row("Robot şasesi kabuğu + kapak", "200 × 150 × 60 mm PLA, 2.5 mm duvar", () => AddShell(0.2, 0.15, 0.06, "PLA", 0.0025, "Şase")));
        }

        /// <summary>A hollow shell with its lid seated on the rim and screwed down.</summary>
        private void AddShell(double sx, double sy, double sz, string material, double wall, string name)
        {
            EnsureWorld();
            WorldObject? shell = null;
            Edit("Kabuk eklendi", () =>
            {
                var w = World!;
                shell = w.AddBody("box", sx, sy, sz, NewPose(), material);
                shell.Name = w.UniqueName(name);
                shell.Parameters["shell"] = "true";
                shell.Parameters["wall"] = wall.ToString(CultureInfo.InvariantCulture);
                var pose = shell.Pose.Clone();
                pose.Z += sz;
                var lid = w.AddBody("box", sx, sy, wall, pose, material);
                lid.Name = w.UniqueName(name + " kapağı");
                lid.Parameters["lid.of"] = shell.Id;
                lid.Parameters["screwed"] = "true";
            });
            if (shell != null) Scene.Select(shell.Id);
        }

        private void ShellSection(WorldDocument world, WorldObject o)
        {
            bool isShell = WorldPhysics.IsShell(o);
            string? lidOf = WorldPhysics.LidOf(o);
            if (!isShell && lidOf == null) return;
            var shell = isShell ? o : world.Find(lidOf!);
            if (shell == null) return;
            var lid = world.Objects.FirstOrDefault(x => string.Equals(WorldPhysics.LidOf(x), shell.Id, StringComparison.OrdinalIgnoreCase));

            FitCheck(world, shell);   // first: whether it all fits is what matters most
            Inspector.Children.Add(Caption(isShell ? "Kabuk (içi boş gövde)" : "Kapak"));
            if (isShell)
            {
                var wall = new TextBox { Text = (WorldPhysics.WallOf(o) * 1000).ToString("0.0#", CultureInfo.CurrentCulture) };
                wall.LostFocus += (_, _) =>
                {
                    if (double.TryParse(wall.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double mm) && mm > 0.3)
                        Edit("Duvar kalınlığı", () => o.Parameters["wall"] = (mm / 1000).ToString(CultureInfo.InvariantCulture));
                };
                Inspector.Children.Add(Labeled("Duvar (mm)", wall));

                // Cable holes per side.
                var holes = o.Parameters.TryGetValue("holes", out var hs) ? hs.Split(',', ';', ' ').Where(h => h.Length > 0).ToList() : new System.Collections.Generic.List<string>();
                var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
                row.Children.Add(new TextBlock { Text = "Kablo deliği:", Style = (Style)FindResource("Text.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                foreach (var (side, label) in new[] { ("+x", "ön"), ("-x", "arka"), ("+y", "sol"), ("-y", "sağ") })
                {
                    var cb = new CheckBox { Content = label, IsChecked = holes.Contains(side), Foreground = (Brush)FindResource("B.Text"), Margin = new Thickness(0, 0, 10, 0), ToolTip = side + " duvarında kablo geçiş deliği" };
                    cb.Click += (_, _) => Edit("Kablo deliği", () =>
                    {
                        var set = o.Parameters.TryGetValue("holes", out var cur) ? cur.Split(',', ';', ' ').Where(h => h.Length > 0).ToList() : new System.Collections.Generic.List<string>();
                        set.Remove(side);
                        if (cb.IsChecked == true) set.Add(side);
                        o.Parameters["holes"] = string.Join(",", set);
                    });
                    row.Children.Add(cb);
                }

                Inspector.Children.Add(row);
            }

            if (lid != null)
            {
                bool screwed = lid.Parameters.TryGetValue("screwed", out var sc) && sc == "true";
                var sw = new CheckBox { Content = "Kapak vidalı (kenara oturmuşsa gövdeye kaynaşır)", IsChecked = screwed, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 0, 6) };
                sw.Click += (_, _) => Edit("Kapak vidası", () => lid.Parameters["screwed"] = sw.IsChecked == true ? "true" : "false");
                Inspector.Children.Add(sw);
                var buttons = new WrapPanel();
                bool open = lid.Pose.Z > shell.Pose.Z + shell.SizeZ + 0.005;
                buttons.Children.Add(Small(open ? "Kapağı kapat" : "Kapağı aç", () => Edit(open ? "Kapak kapandı" : "Kapak açıldı", () =>
                {
                    var up = shell.Pose.Orientation.Rotate(RobotTwin.CoreSim.Mechanics.Vec3.UnitZ);
                    double lift = open ? -(lid.Pose.Z - shell.Pose.Z - shell.SizeZ) : shell.SizeZ * 1.5;
                    lid.Pose.X += up.X * lift;
                    lid.Pose.Y += up.Y * lift;
                    lid.Pose.Z += up.Z * lift;
                }), "Kapağı kenardan kaldırır / kenara oturtur"));
                Inspector.Children.Add(buttons);
            }

            ViewSwitches();
        }

        /// <summary>X-ray and exploded view switches (display only).</summary>
        private void ViewSwitches()
        {
            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 2) };
            var xr = new CheckBox { Content = "Röntgen", IsChecked = Scene.XRay, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 14, 0), ToolTip = "Kabuk ve kapaklar yarı saydam: içerideki parçalar ve kablolar görünür" };
            xr.Click += (_, _) => Scene.SetXRay(xr.IsChecked == true);
            var ex = new CheckBox { Content = "Patlatılmış görünüm", IsChecked = Scene.Exploded, Style = (Style)FindResource("Switch"), ToolTip = "Kapaklar ve içerideki parçalar yukarı ayrılır (yalnız görüntü)" };
            ex.Click += (_, _) => Scene.SetExploded(ex.IsChecked == true);
            row.Children.Add(xr);
            row.Children.Add(ex);
            Inspector.Children.Add(row);
        }

        /// <summary>The shell's fit check: lid vs contents, walls, pinched cables, cables through walls without a hole.</summary>
        private void FitCheck(WorldDocument world, WorldObject shell)
        {
            Inspector.Children.Add(Caption("Sığma denetimi (" + shell.Name + ")"));
            WorldPhysics physics;
            try
            {
                physics = _session.State != SimState.Stopped && _session.Physics != null ? _session.Physics
                    : Scene.CableLayout ?? WorldPhysics.Build(world, Scene.GeometryOf, null);
            }
            catch (Exception ex)
            {
                Inspector.Children.Add(Note("Denetlenemedi: " + ex.Message));
                return;
            }

            var issues = physics.ShellFit(world).Where(i => string.Equals(i.ShellId, shell.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (issues.Count == 0)
            {
                Inspector.Children.Add(StatusLine("✓ Her şey sığıyor: kapak kapanıyor, duvara giren parça ve ezilen kablo yok.", Color.FromRgb(0x4C, 0xC3, 0x8A)));
                return;
            }

            foreach (var i in issues)
            {
                var colour = i.Kind is "yükseklik" or "ezilme" or "duvar" ? Color.FromRgb(0xE5, 0x48, 0x4D) : Color.FromRgb(0xF5, 0xA6, 0x23);
                var line = StatusLine("● " + i.Message, colour);
                line.Cursor = System.Windows.Input.Cursors.Hand;
                line.ToolTip = "Seçmek için tıklayın";
                var id = i.ObjectId;
                line.MouseLeftButtonUp += (_, _) =>
                {
                    if (world.Cables.Any(c => c.Id == id)) Scene.SelectCable(id);
                    else Scene.Select(id);
                };
                Inspector.Children.Add(line);
            }
        }

        /// <summary>Snapshot flags: --select-shell selects and frames the first shell, --xray and --exploded switch those views on.</summary>
        private void ShellSnapshot()
        {
            var args = Environment.GetCommandLineArgs();
            if (World is not { } w) return;
            if (args.Contains("--select-shell") && w.Objects.FirstOrDefault(WorldPhysics.IsShell) is { } shell)
            {
                Scene.Select(shell.Id);
                Scene.FrameSelected();
            }

            int sel = Array.IndexOf(args, "--select-name");
            if (sel >= 0 && sel + 1 < args.Length && w.Objects.FirstOrDefault(o => o.Name.StartsWith(args[sel + 1], StringComparison.OrdinalIgnoreCase)) is { } named)
            {
                Scene.Select(named.Id);
                Scene.FrameSelected();
                BuildInspector();
            }

            if (args.Contains("--xray")) Scene.SetXRay(true);
            if (args.Contains("--exploded")) Scene.SetExploded(true);
            if (args.Contains("--xray") || args.Contains("--exploded")) BuildInspector();
        }

        private TextBlock StatusLine(string text, Color colour) =>
            new TextBlock { Text = text, Foreground = new SolidColorBrush(colour), TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 1, 0, 3) };
    }
}
