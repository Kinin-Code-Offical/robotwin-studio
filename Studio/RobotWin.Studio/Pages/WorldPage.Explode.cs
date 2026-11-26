using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [clearance] [explode] SolidWorks' Clearance Verification (pairs nearer than a minimum gap, the gap drawn between
    /// its two ends) and its exploded view (steps that move parts apart along a direction, an automatic spread, a slider
    /// and an animation between assembled and exploded). Both are views: the parts' poses do not change.
    /// </summary>
    public partial class WorldPage
    {
        private static double _clearanceMm = 2;
        private Window? _explodeWindow;
        private double _explodeAmount = 1;

        /// <summary>[palette] Opens the clearance check.</summary>
        public void CheckClearance() => OnClearance(this, new RoutedEventArgs());

        /// <summary>[palette] Opens the exploded view.</summary>
        public void OpenExplode() => OnExplode(this, new RoutedEventArgs());

        private static double Mm(TextBox box, double fallback) =>
            double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 0 ? v : fallback;

        private static string Fmt(double v) => v.ToString(v < 10 ? "0.##" : "0.#", CultureInfo.CurrentCulture);

        private UIElement Field(string label, UIElement input, double labelWidth = 120)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var t = new TextBlock { Text = Lang.T(label), Width = labelWidth, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "B.TextSecondary");
            row.Children.Add(t);
            row.Children.Add(input);
            return row;
        }

        private TextBlock Heading(string text) =>
            new TextBlock { Text = Lang.T(text), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) };

        // ================================================================ clearance

        private void OnClearance(object sender, RoutedEventArgs e)
        {
            var world = World;
            if (world == null) return;
            var panel = new StackPanel { Width = 460 };
            panel.Children.Add(new TextBlock { Text = Lang.T("Boşluk doğrulama"), Style = (Style)FindResource("Text.Title"), FontSize = 18, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(new TextBlock { Text = Lang.T("Birbirine istenen boşluktan yakın duran parça çiftleri, aradaki en kısa mesafe ve yeri (parçaların dış şekilleriyle)."), Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            var minimum = new TextBox { Text = Fmt(_clearanceMm), Width = 80, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = Lang.T("Bundan yakın duran çiftler listelenir (mm)") };
            panel.Children.Add(Field("En az boşluk (mm)", minimum));
            var overlapping = new CheckBox { Content = Lang.T("Çakışan parçaları da listele"), IsChecked = true, Margin = new Thickness(0, 2, 0, 4) };
            var joined = new CheckBox { Content = Lang.T("Eklemle bağlı parçaları da denetle"), Margin = new Thickness(0, 0, 0, 4) };
            string? selected = Scene.Selected;
            var only = new CheckBox { Content = Lang.T("Yalnız seçili parça"), Margin = new Thickness(0, 0, 0, 8), IsEnabled = selected != null };
            panel.Children.Add(overlapping);
            panel.Children.Add(joined);
            panel.Children.Add(only);
            var run = new Button { Content = Lang.T("Hesapla"), Style = (Style)FindResource("Btn.Primary"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(run);
            var summary = new TextBlock { Style = (Style)FindResource("Text.Body"), Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(summary);
            var list = new ListBox { MaxHeight = 320 };
            panel.Children.Add(list);

            string NameOf(string id) => world.Find(id)?.Name ?? id;
            void Run()
            {
                _clearanceMm = Mm(minimum, _clearanceMm);
                minimum.Text = Fmt(_clearanceMm);
                var options = new ClearanceOptions
                {
                    MinimumMm = _clearanceMm,
                    IncludeOverlapping = overlapping.IsChecked == true,
                    IncludeJoined = joined.IsChecked == true,
                    Only = only.IsChecked == true && selected != null ? new List<string> { selected } : null
                };
                var found = Clearance.Find(world, new SceneGeometry(Scene), options);
                list.Items.Clear();
                Scene.ShowGap(null, null);
                summary.Text = found.Count == 0 ? Lang.T($"Her çift en az {Fmt(_clearanceMm)} mm uzakta") : Lang.T($"{found.Count} çift {Fmt(_clearanceMm)} mm'den yakın");
                summary.SetResourceReference(TextBlock.ForegroundProperty, found.Count == 0 ? "B.Success" : "B.Warning");
                foreach (var p in found)
                {
                    var row = new DockPanel();
                    string gap = p.Overlapping ? Lang.T("çakışıyor") : Fmt(p.GapMm) + " mm";
                    var v = new TextBlock { Text = gap + (p.Joined ? " · " + Lang.T("bağlı") : string.Empty), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    v.SetResourceReference(TextBlock.ForegroundProperty, p.Overlapping ? "B.Danger" : "B.TextSecondary");
                    DockPanel.SetDock(v, Dock.Right);
                    row.Children.Add(v);
                    row.Children.Add(new TextBlock { Text = NameOf(p.A) + " ↔ " + NameOf(p.B), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                    list.Items.Add(new ListBoxItem { Content = row, Tag = p });
                }

                if (list.Items.Count > 0) list.SelectedIndex = 0;
            }

            run.Click += (_, _) => Run();
            minimum.KeyDown += (_, k) =>
            {
                if (k.Key != System.Windows.Input.Key.Enter) return;
                Run();
                k.Handled = true;
            };
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is not ListBoxItem { Tag: ClearancePair p }) return;
                Scene.Select(p.A);
                Scene.ShowGap(p.PointA, p.PointB);   // the gap drawn between its two ends
                W.Status = p.Overlapping
                    ? Lang.T($"{NameOf(p.A)} ↔ {NameOf(p.B)}: çakışıyor")
                    : Lang.T($"{NameOf(p.A)} ↔ {NameOf(p.B)}: en kısa boşluk {Fmt(p.GapMm)} mm");
            };
            var dlg = RobotWin.Studio.Pcb.PcbForms.MakeDialog(Window.GetWindow(this), Lang.T("Boşluk doğrulama"), 520, panel, out var ok, "Kapat", withCancel: false);
            ok.Click += (_, _) => dlg.Close();
            ok.IsCancel = true;
            dlg.Closed += (_, _) => Scene.ShowGap(null, null);
            dlg.Loaded += (_, _) => Run();
            ShowTool(dlg);
        }

        /// <summary>A modeless tool window (SolidWorks' PropertyManager: the scene stays usable); a snapshot pictures it.</summary>
        private void ShowTool(Window dlg)
        {
            if (Unattended.Active)
            {
                dlg.ShowActivated = false;
                dlg.Show();
                dlg.UpdateLayout();
                App.SnapshotTarget = VisualTreeHelper.GetChildrenCount(dlg) > 0 ? VisualTreeHelper.GetChild(dlg, 0) as FrameworkElement : dlg.Content as FrameworkElement;
                return;
            }

            dlg.Show();
        }

        // ================================================================ exploded view

        /// <summary>Shows the steps at the slider's amount (cables follow when asked: not every frame of an animation).</summary>
        private void ApplyExplode(bool cables = true)
        {
            var w = World;
            if (w == null) return;
            var steps = w.Explode;
            Scene.SetExplodeOffsets(steps == null || steps.Count == 0 || _explodeAmount <= 0 ? null : ExplodedView.Offsets(w, steps, _explodeAmount, new SceneGeometry(Scene)), cables);
        }

        private void OnExplode(object sender, RoutedEventArgs e)
        {
            if (World == null) return;
            if (_explodeWindow != null)
            {
                _explodeWindow.Activate();
                return;
            }

            var panel = new StackPanel { Width = 440 };
            panel.Children.Add(new TextBlock { Text = Lang.T("Patlatılmış görünüm"), Style = (Style)FindResource("Text.Title"), FontSize = 18, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(new TextBlock { Text = Lang.T("Parçaları adım adım ayırır: montaj sırası, bakım ve belgeler için. Yalnız görüntü: parçaların yeri değişmez."), Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });

            // ---- steps
            panel.Children.Add(Heading("Adımlar"));
            var steps = new ListBox { MinHeight = 64, MaxHeight = 180 };
            panel.Children.Add(steps);
            var stepButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            Button Small(string text)
            {
                var b = new Button { Content = Lang.T(text), Style = (Style)FindResource("Btn.Ghost"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
                stepButtons.Children.Add(b);
                return b;
            }

            var up = Small("Yukarı");
            var down = Small("Aşağı");
            var remove = Small("Sil");
            var clear = Small("Tümünü sil");
            panel.Children.Add(stepButtons);

            // ---- a new step
            panel.Children.Add(Heading("Yeni adım"));
            var pick = new TextBlock { Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(pick);
            var directions = new (string Name, Vec3 Dir, bool Radial)[]
            {
                ("+Z (yukarı)", Vec3.UnitZ, false), ("−Z (aşağı)", -Vec3.UnitZ, false), ("+X", Vec3.UnitX, false), ("−X", -Vec3.UnitX, false),
                ("+Y", Vec3.UnitY, false), ("−Y", -Vec3.UnitY, false), ("Dışa doğru (ortadan)", Vec3.UnitZ, true)
            };
            var direction = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var d in directions) direction.Items.Add(Lang.T(d.Name));
            direction.SelectedIndex = 0;
            panel.Children.Add(Field("Yön", direction));
            var distance = new TextBox { Text = "40", Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
            panel.Children.Add(Field("Uzaklık (mm)", distance));
            var add = new Button { Content = Lang.T("Adım ekle"), Style = (Style)FindResource("Btn.Primary"), HorizontalAlignment = HorizontalAlignment.Left };
            panel.Children.Add(add);

            // ---- automatic
            panel.Children.Add(Heading("Otomatik"));
            var layout = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            layout.Items.Add(Lang.T("Eksen boyunca, sırayla"));
            layout.Items.Add(Lang.T("Dışa doğru (ortadan)"));
            layout.SelectedIndex = 0;
            panel.Children.Add(Field("Düzen", layout));
            var axis = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var a in new[] { "Z (yukarı)", "X", "Y" }) axis.Items.Add(Lang.T(a));
            axis.SelectedIndex = 0;
            panel.Children.Add(Field("Eksen", axis));
            var spacing = new TextBox { Text = "30", Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
            panel.Children.Add(Field("Aralık (mm)", spacing));
            var auto = new Button { Content = Lang.T("Otomatik patlat"), Style = (Style)FindResource("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = Lang.T("En büyük parça yerinde kalır, ötekiler ayrılır; var olan adımların yerini alır") };
            panel.Children.Add(auto);

            // ---- play
            panel.Children.Add(Heading("Görünüm"));
            var amount = new Slider { Minimum = 0, Maximum = 1, Value = _explodeAmount, SmallChange = 0.02, LargeChange = 0.1 };
            var amountText = RobotWin.Studio.Theme.SliderInput.Create(amount, "0", " %", v => v * 100, x => x / 100);   // [slider-input]
            var amountRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(amountText, Dock.Right);
            amountRow.Children.Add(amountText);
            amountRow.Children.Add(amount);
            panel.Children.Add(amountRow);
            var playRow = new WrapPanel();
            var play = new Button { Content = Lang.T("Oynat"), Style = (Style)FindResource("Btn.Ghost"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0), ToolTip = Lang.T("Toplu hâlden patlatılmışa adım adım") };
            var collapse = new Button { Content = Lang.T("Topla"), Style = (Style)FindResource("Btn.Ghost"), Padding = new Thickness(10, 2, 10, 2), ToolTip = Lang.T("Patlatılmıştan toplu hâle") };
            playRow.Children.Add(play);
            playRow.Children.Add(collapse);
            panel.Children.Add(playRow);

            string NameOf(string id) => World?.Find(id)?.Name ?? id;
            void FillSteps(int select = -1)
            {
                steps.Items.Clear();
                var list = World?.Explode ?? new List<ExplodeStep>();
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    string dir = s.Radial ? Lang.T("dışa doğru") : directions.FirstOrDefault(d => !d.Radial && (d.Dir - s.Direction).Length < 1e-6).Name is { } n ? Lang.T(n) : $"({s.DirX:0.##}, {s.DirY:0.##}, {s.DirZ:0.##})";
                    string who = string.IsNullOrEmpty(s.Name) ? string.Join(", ", s.Objects.Select(NameOf)) : s.Name;
                    var row = new DockPanel();
                    var right = new TextBlock { Text = $"{Fmt(s.Distance * 1000)} mm · {dir}", Margin = new Thickness(12, 0, 0, 0) };
                    right.SetResourceReference(TextBlock.ForegroundProperty, "B.TextSecondary");
                    DockPanel.SetDock(right, Dock.Right);
                    row.Children.Add(right);
                    row.Children.Add(new TextBlock { Text = $"{i + 1}. {who}", TextTrimming = TextTrimming.CharacterEllipsis });
                    steps.Items.Add(new ListBoxItem { Content = row, Tag = i });
                }

                if (steps.Items.Count == 0) steps.Items.Add(new ListBoxItem { Content = Lang.T("Henüz adım yok: bir parça seçip adım ekleyin ya da otomatik patlatın."), IsEnabled = false });
                if (select >= 0 && select < list.Count) steps.SelectedIndex = select;
                bool any = list.Count > 0;
                up.IsEnabled = down.IsEnabled = remove.IsEnabled = clear.IsEnabled = any;
                amount.IsEnabled = play.IsEnabled = collapse.IsEnabled = any;
            }

            void ShowPick()
            {
                string? id = Scene.Selected;
                pick.Text = id != null ? Lang.T($"Parça: {NameOf(id)}") : Lang.T("Önce sahnede bir parça seçin");
                pick.SetResourceReference(TextBlock.ForegroundProperty, id != null ? "B.Text" : "B.TextSecondary");
                add.IsEnabled = id != null;
            }

            void SetAmount(double v, bool cables = true)
            {
                _explodeAmount = Math.Max(0, Math.Min(1, v));
                ApplyExplode(cables);
            }

            void Change(string status, Action change, int select)
            {
                Edit(status, () =>
                {
                    change();
                    if (World!.Explode is { Count: 0 }) World.Explode = null;
                });
                FillSteps(select);
                if (_explodeAmount <= 0) amount.Value = 1;
                else ApplyExplode();
            }

            int Selected() => steps.SelectedItem is ListBoxItem { Tag: int i } ? i : -1;

            add.Click += (_, _) =>
            {
                if (Scene.Selected is not { } id || World == null) return;
                var d = directions[Math.Max(0, direction.SelectedIndex)];
                double mm = Mm(distance, 40);
                var step = new ExplodeStep { Name = NameOf(id), Objects = { id }, DirX = d.Dir.X, DirY = d.Dir.Y, DirZ = d.Dir.Z, Distance = mm / 1000, Radial = d.Radial };
                Change(Lang.T($"Patlatma adımı: {NameOf(id)} {Fmt(mm)} mm"), () => (World.Explode ??= new List<ExplodeStep>()).Add(step), (World.Explode?.Count ?? 0));
            };
            auto.Click += (_, _) =>
            {
                if (World == null) return;
                var ax = axis.SelectedIndex switch { 1 => Vec3.UnitX, 2 => Vec3.UnitY, _ => Vec3.UnitZ };
                var made = ExplodedView.Auto(World, new SceneGeometry(Scene), layout.SelectedIndex == 1 ? ExplodeLayout.Radial : ExplodeLayout.Axis, ax, Mm(spacing, 30) / 1000);
                if (made.Count == 0)
                {
                    W.Status = Lang.T("Patlatılacak en az iki parça gerekir");
                    return;
                }

                Change(Lang.T($"Otomatik patlatma: {made.Count} adım"), () => World.Explode = made, -1);
            };
            up.Click += (_, _) =>
            {
                int i = Selected();
                if (i <= 0 || World?.Explode == null) return;
                Change(Lang.T("Patlatma adımı yukarı"), () => (World.Explode[i - 1], World.Explode[i]) = (World.Explode[i], World.Explode[i - 1]), i - 1);
            };
            down.Click += (_, _) =>
            {
                int i = Selected();
                if (i < 0 || World?.Explode == null || i >= World.Explode.Count - 1) return;
                Change(Lang.T("Patlatma adımı aşağı"), () => (World.Explode[i + 1], World.Explode[i]) = (World.Explode[i], World.Explode[i + 1]), i + 1);
            };
            remove.Click += (_, _) =>
            {
                int i = Selected();
                if (i < 0 || World?.Explode == null) return;
                Change(Lang.T("Patlatma adımı silindi"), () => World.Explode.RemoveAt(i), Math.Min(i, World.Explode.Count - 2));
            };
            clear.Click += (_, _) =>
            {
                if (World?.Explode == null) return;
                Change(Lang.T("Patlatma adımları silindi"), () => World.Explode = null, -1);
            };
            steps.SelectionChanged += (_, _) =>
            {
                int i = Selected();
                if (i >= 0 && World?.Explode is { } list && i < list.Count && list[i].Objects.FirstOrDefault() is { } first) Scene.Select(first);
            };
            amount.ValueChanged += (_, _) => SetAmount(amount.Value, cables: !amount.IsMouseCaptureWithin);
            amount.PreviewMouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(new Action(() => ApplyExplode()), DispatcherPriority.Background);

            // the animation: the steps one after another over about a second each
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            double target = 1, speed = 1;
            timer.Tick += (_, _) =>
            {
                double next = amount.Value + Math.Sign(target - amount.Value) * speed * 0.016;
                bool done = target > amount.Value ? next >= target : next <= target;
                amount.Value = done ? target : next;
                if (!done) return;
                timer.Stop();
                ApplyExplode();
            };
            void Animate(double to)
            {
                int n = Math.Max(1, World?.Explode?.Count ?? 1);
                speed = 1.0 / Math.Min(6, 0.8 * n);   // a step in about 0.8 s, the whole at most 6 s
                if (to > 0 && amount.Value >= 0.999) amount.Value = 0;   // play from the assembled state
                target = to;
                timer.Start();
            }

            play.Click += (_, _) => Animate(1);
            collapse.Click += (_, _) => Animate(0);

            Action<string?> selectionChanged = _ => ShowPick();
            Scene.SelectionChanged += selectionChanged;
            EventHandler worldChanged = (_, _) =>
            {
                // an undo or a removed part changes the steps under the window
                FillSteps(Selected());
                ApplyExplode();
            };
            W.WorldChanged += worldChanged;
            var dlg = RobotWin.Studio.Pcb.PcbForms.MakeDialog(Window.GetWindow(this), Lang.T("Patlatılmış görünüm"), 500, panel, out var ok, "Kapat", withCancel: false);
            ok.Click += (_, _) => dlg.Close();
            ok.IsCancel = true;
            dlg.Closed += (_, _) =>
            {
                timer.Stop();
                Scene.SelectionChanged -= selectionChanged;
                W.WorldChanged -= worldChanged;
                _explodeWindow = null;
                Scene.SetExplodeOffsets(null);   // back to the assembled view
            };
            _explodeWindow = dlg;
            FillSteps();
            ShowPick();
            SetAmount(_explodeAmount);
            ShowTool(dlg);
        }

        private static Button? FindButton(DependencyObject root, string text)
        {
            if (root is Button b && (b.Content as string) == text) return b;
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
                if (FindButton(System.Windows.Media.VisualTreeHelper.GetChild(root, i), text) is { } found) return found;
            return null;
        }

        /// <summary>Snapshot flags: --clearance &lt;mm&gt; opens the clearance check; --explode auto|window explodes the world (and opens the window).</summary>
        private void AssemblyToolsSnapshot()
        {
            var args = Environment.GetCommandLineArgs();
            int c = Array.IndexOf(args, "--clearance");
            if (c >= 0)
            {
                if (c + 1 < args.Length && double.TryParse(args[c + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double mm)) _clearanceMm = mm;
                OnClearance(this, new RoutedEventArgs());
            }

            int gz = Array.IndexOf(args, "--gizmo");   // [gizmo] --gizmo move|rotate|scale|all [--gizmo-world] [--gizmo-center]
            if (gz >= 0 && gz + 1 < args.Length)
            {
                Scene.GizmoLocal = !args.Contains("--gizmo-world");
                Scene.GizmoCenter = args.Contains("--gizmo-center");
                PickGizmo(args[gz + 1] switch { "rotate" => RobotWin.Studio.World.GizmoMode.Rotate, "scale" => RobotWin.Studio.World.GizmoMode.Scale, "all" => RobotWin.Studio.World.GizmoMode.All, _ => RobotWin.Studio.World.GizmoMode.Move });
            }

            if (args.Contains("--rigidbody"))
            {
                _rigidbodyOpen = true;   // [rigidbody] the Fizik card's damping and constraints, open, as the picture
                BuildInspector();
                Inspector.UpdateLayout();
                DependencyObject? card = FindButton(Inspector, "▾ Kısıtlar ve sönüm");
                while (card != null && System.Windows.Media.VisualTreeHelper.GetParent(card) != Inspector)
                    card = System.Windows.Media.VisualTreeHelper.GetParent(card);
                if (card is FrameworkElement fe) fe.BringIntoView();
            }

            if (args.Contains("--add-component"))
            {
                // [components] the Add Component list, open, as the picture
                BuildInspector();
                Inspector.UpdateLayout();
                var add = FindButton(Inspector, "Bileşen ekle");
                if (add != null)
                {
                    add.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (System.Windows.Media.VisualTreeHelper.GetParent(add) != null && _lastPicker?.Child is FrameworkElement pick)
                    {
                        pick.UpdateLayout();
                        App.SnapshotTarget = pick;
                    }
                }
            }

            if (args.Contains("--scene-rename")) StartRename(Scene.Selected);   // [hierarchy] the row's name as a box
            if (args.Contains("--scene-menu") && Scene.Selected != null)
            {
                // [hierarchy] the selected row's right-click menu, open, as the picture
                var menu = SceneMenu(Scene.Selected);
                menu.PlacementTarget = Palette;
                menu.IsOpen = true;
                menu.UpdateLayout();
                App.SnapshotTarget = menu;
            }

            int x = Array.IndexOf(args, "--explode");
            if (x < 0 || World is not { } w) return;
            string how = x + 1 < args.Length ? args[x + 1] : "auto";
            w.Explode = ExplodedView.Auto(w, new SceneGeometry(Scene), how.Contains("radial") ? ExplodeLayout.Radial : ExplodeLayout.Axis, Vec3.UnitZ, 0.03);
            _explodeAmount = 1;
            if (how.Contains("window")) OnExplode(this, new RoutedEventArgs());
            else ApplyExplode();
        }
    }
}
