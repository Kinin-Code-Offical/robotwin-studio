using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [pattern] SolidWorks' Linear / Circular Component Pattern for the selection: how many (with the original), a step
    /// (mm) for a row, or an axis, a centre (mm) and an angle for a ring; copies fixed where the original is fixed. One undo
    /// step; the copies come out selected. CoreSim Sandbox/WorldPatterns.cs does it.
    /// </summary>
    public partial class WorldPage
    {
        private void ShowPatternDialog()
        {
            var world = World;
            var o = world?.Find(Scene.Selected);
            if (world == null || o == null) return;
            var ids = SelectionIds();

            // a ring goes round what the part is fixed on (holes round a plate), else round the world's origin
            var holder = world.Joints.FirstOrDefault(j => j.ObjectA == o.Id && j.ObjectB != null) is { } hj ? world.Find(hj.ObjectB) : null;
            var centre = holder != null ? holder.Pose.Position : Vec3.Zero;

            int count = 4;
            Vec3 step = new Vec3(0.05, 0, 0);
            double angle = 360;
            int axisIndex = 2;
            bool circular = false, keep = true;

            var panel = new StackPanel();
            var kind = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            kind.Items.Add(Lang.T("Doğrusal (bir sıra)"));
            kind.Items.Add(Lang.T("Dairesel (bir eksen çevresinde)"));
            kind.SelectedIndex = 0;
            var linearBox = new StackPanel();
            var circularBox = new StackPanel { Visibility = Visibility.Collapsed };
            kind.SelectionChanged += (_, _) =>
            {
                circular = kind.SelectedIndex == 1;
                linearBox.Visibility = circular ? Visibility.Collapsed : Visibility.Visible;
                circularBox.Visibility = circular ? Visibility.Visible : Visibility.Collapsed;
            };
            panel.Children.Add(Labeled("Desen", kind));
            panel.Children.Add(NumberField("Adet (aslı dahil)", count, v => count = Math.Max(2, (int)Math.Round(v)), 2, 500, "0"));

            linearBox.Children.Add(Vector3Row("Adım (mm) X · Y · Z", step.X * 1000, step.Y * 1000, step.Z * 1000, (x, y, z) => step = new Vec3(x / 1000, y / 1000, z / 1000)));
            panel.Children.Add(linearBox);

            var axis = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var a in new[] { "X", "Y", "Z" }) axis.Items.Add(a);
            axis.SelectedIndex = axisIndex;
            axis.SelectionChanged += (_, _) => axisIndex = axis.SelectedIndex;
            circularBox.Children.Add(Labeled("Eksen", axis));
            circularBox.Children.Add(Vector3Row("Merkez (mm) X · Y · Z", centre.X * 1000, centre.Y * 1000, centre.Z * 1000, (x, y, z) => centre = new Vec3(x / 1000, y / 1000, z / 1000)));
            circularBox.Children.Add(NumberField("Toplam açı (°, 360 = tam tur)", angle, v => angle = v, -360, 360, "0.##"));
            circularBox.Children.Add(new TextBlock
            {
                Text = Lang.T(holder != null ? $"Merkez, parçanın takılı olduğu {holder.Name} nesnesinin yeridir; değiştirilebilir." : "Merkez dünyanın başlangıç noktasıdır; değiştirilebilir."),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), Foreground = (Brush)FindResource("B.TextSecondary")
            });
            panel.Children.Add(circularBox);

            var hold = new CheckBox { Content = Lang.T("Kopyaları aslının sabit olduğu yere tak"), IsChecked = true, Margin = new Thickness(0, 6, 0, 0), ToolTip = Lang.T("Bir plakaya vidalanmış parçanın kopyaları da plakaya vidalı olur") };
            hold.Click += (_, _) => keep = hold.IsChecked == true;
            panel.Children.Add(hold);

            var dlg = RobotWin.Studio.Pcb.PcbForms.MakeDialog(Window.GetWindow(this), Lang.T("Desenle çoğalt"), 440, panel, out var ok, Lang.T("Çoğalt"));
            ok.Click += (_, _) =>
            {
                Keyboard.Focus(ok);   // a typed box still in focus gives its value first
                var made = new List<WorldObject>();
                var axisVec = axisIndex == 0 ? Vec3.UnitX : axisIndex == 1 ? Vec3.UnitY : Vec3.UnitZ;
                Edit(circular ? $"Dairesel desen: {count} adet" : $"Doğrusal desen: {count} adet", () =>
                {
                    made = circular
                        ? WorldPatterns.Circular(world, ids, count, angle, axisVec, centre, keep)
                        : WorldPatterns.Linear(world, ids, count, step, keep);
                });
                if (made.Count > 0) Scene.SelectMany(made.Select(m => m.Id).ToList());
                dlg.DialogResult = true;
            };
            if (Unattended.Active)
            {
                dlg.ShowActivated = false;
                dlg.Show();
                dlg.UpdateLayout();
                App.SnapshotTarget = VisualTreeHelper.GetChildrenCount(dlg) > 0 ? VisualTreeHelper.GetChild(dlg, 0) as FrameworkElement : dlg.Content as FrameworkElement;
                return;
            }

            dlg.ShowDialog();
        }
    }
}
