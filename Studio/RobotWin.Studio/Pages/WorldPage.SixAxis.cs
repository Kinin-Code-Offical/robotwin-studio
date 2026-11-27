using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [d6] Unity's Character Joint and Configurable Joint in the inspector. A character joint shows its twist range and
    /// its two swing limits; a configurable joint a grid of its six axes (slide and turn along the joint frame's X, Y, Z:
    /// locked, limited or free, as Unity's X / Y / Z Motion and Angular X / Y / Z Motion), then only the limits the
    /// limited axes need, and its drives folded until used.
    /// </summary>
    public partial class WorldPage
    {
        private readonly HashSet<string> _sixDrivesOpen = new HashSet<string>();

        private static readonly (AxisMotion Motion, string Name)[] Motions =
        {
            (AxisMotion.Locked, "Kilitli"),
            (AxisMotion.Limited, "Sınırlı"),
            (AxisMotion.Free, "Serbest")
        };

        private void SixAxisRows(WorldJoint j)
        {
            var joint = j;
            if (j.Kind == WorldJointKind.Character)
            {
                TwistFields(joint);
                SwingFields(joint, true, true);
                Inspector.Children.Add(Note("Bağlantı noktası yerinde kalır; parça eksen çevresinde burulma sınırları arasında döner, eksenden de iki salınım açısının çizdiği elips koni içinde sapar (omuz, kalça, bir kuklanın uzuvları)."));
                return;
            }

            // One row per axis of the joint frame, its slide and its turn side by side.
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < 4; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Cell(grid, new TextBlock { Text = "Doğrusal", Foreground = (Brush)FindResource("B.TextSecondary"), Margin = new Thickness(2, 0, 2, 3) }, 0, 1);
            Cell(grid, new TextBlock { Text = "Açısal", Foreground = (Brush)FindResource("B.TextSecondary"), Margin = new Thickness(2, 0, 2, 3) }, 0, 2);
            string[] axes = { "X (eksen)", "Y", "Z" };
            for (int r = 0; r < 3; r++) Cell(grid, new TextBlock { Text = axes[r], Foreground = (Brush)FindResource("B.TextSecondary"), VerticalAlignment = VerticalAlignment.Center }, r + 1, 0);
            Cell(grid, MotionBox(joint.MotionX, m => joint.MotionX = m, "X ekseni boyunca kayma"), 1, 1);
            Cell(grid, MotionBox(joint.MotionY, m => joint.MotionY = m, "Y ekseni boyunca kayma"), 2, 1);
            Cell(grid, MotionBox(joint.MotionZ, m => joint.MotionZ = m, "Z ekseni boyunca kayma"), 3, 1);
            Cell(grid, MotionBox(joint.AngularMotionX, m => joint.AngularMotionX = m, "Eksen çevresinde burulma"), 1, 2);
            Cell(grid, MotionBox(joint.AngularMotionY, m => joint.AngularMotionY = m, "Y çevresinde salınım"), 2, 2);
            Cell(grid, MotionBox(joint.AngularMotionZ, m => joint.AngularMotionZ = m, "Z çevresinde salınım"), 3, 2);
            Inspector.Children.Add(grid);

            if (j.MotionX == AxisMotion.Limited || j.MotionY == AxisMotion.Limited || j.MotionZ == AxisMotion.Limited)
                Inspector.Children.Add(NumberField("Doğrusal sınır (mm)", j.LinearLimit * 1000, v => Edit("Eklem sınırı", () => joint.LinearLimit = v / 1000), 0, 1e5, "0.##"));
            if (j.AngularMotionX == AxisMotion.Limited) TwistFields(joint);
            SwingFields(joint, j.AngularMotionY == AxisMotion.Limited, j.AngularMotionZ == AxisMotion.Limited);
            SixAxisDrives(joint);
        }

        private static void Cell(Grid grid, UIElement e, int row, int column)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, column);
            grid.Children.Add(e);
        }

        private ComboBox MotionBox(AxisMotion now, Action<AxisMotion> set, string tip)
        {
            var box = new ComboBox { Margin = new Thickness(2, 0, 2, 4), ToolTip = tip, MinWidth = 0 };
            foreach (var (motion, name) in Motions) box.Items.Add(new ComboBoxItem { Content = name, Tag = motion, IsSelected = motion == now });
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is not ComboBoxItem { Tag: AxisMotion m } || m == now) return;
                Edit("Eklem ekseni değişti", () => set(m));
                BuildInspector();
            };
            return box;
        }

        private void TwistFields(WorldJoint joint)
        {
            Inspector.Children.Add(NumberField("Burulma alt sınırı (°)", joint.Lower ?? -180, v => Edit("Eklem sınırı", () => joint.Lower = v), -180, 180, "0.#"));
            Inspector.Children.Add(NumberField("Burulma üst sınırı (°)", joint.Upper ?? 180, v => Edit("Eklem sınırı", () => joint.Upper = v), -180, 180, "0.#"));
        }

        private void SwingFields(WorldJoint joint, bool first, bool second)
        {
            if (first) Inspector.Children.Add(NumberField("Salınım 1 sınırı (°, 0 = kilitli)", joint.SwingLimit1, v => Edit("Eklem sınırı", () => joint.SwingLimit1 = v), 0, 177, "0.#"));
            if (second) Inspector.Children.Add(NumberField("Salınım 2 sınırı (°, 0 = kilitli)", joint.SwingLimit2, v => Edit("Eklem sınırı", () => joint.SwingLimit2 = v), 0, 177, "0.#"));
        }

        /// <summary>The configurable joint's drives (Unity's X / Y / Z Drive and Slerp Drive), folded until one is set.</summary>
        private void SixAxisDrives(WorldJoint joint)
        {
            bool used = joint.LinearDriveStiffness > 0 || joint.LinearDriveDamping > 0 || joint.AngularDriveStiffness > 0 || joint.AngularDriveDamping > 0;
            bool open = used || _sixDrivesOpen.Contains(joint.Id);
            var head = new Button
            {
                Content = (open ? "▾ " : "▸ ") + "Sürücüler",
                Style = (Style)FindResource("Btn.Ghost"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = "Parçayı hedef konumuna ve açısına çeken yay ile sönüm"
            };
            head.Click += (_, _) =>
            {
                if (!_sixDrivesOpen.Remove(joint.Id)) _sixDrivesOpen.Add(joint.Id);
                BuildInspector();
            };
            Inspector.Children.Add(head);
            if (!open) return;

            double perDegree = 1000 * Math.PI / 180;   // N·m/rad → N·mm/°
            Inspector.Children.Add(NumberField("Kayma yayı (N/mm)", joint.LinearDriveStiffness / 1000, v => Edit("Sürücü değişti", () => joint.LinearDriveStiffness = v * 1000), 0, 1e7, "0.###"));
            Inspector.Children.Add(NumberField("Kayma sönümü (N·s/m)", joint.LinearDriveDamping, v => Edit("Sürücü değişti", () => joint.LinearDriveDamping = v), 0, 1e7, "0.###"));
            Inspector.Children.Add(Vector3Row("Hedef konum X / Y / Z (mm)", joint.TargetX * 1000, joint.TargetY * 1000, joint.TargetZ * 1000,
                (x, y, z) => Edit("Sürücü hedefi değişti", () => (joint.TargetX, joint.TargetY, joint.TargetZ) = (x / 1000, y / 1000, z / 1000))));
            Inspector.Children.Add(NumberField("Dönme yayı (N·mm/°)", joint.AngularDriveStiffness * perDegree, v => Edit("Sürücü değişti", () => joint.AngularDriveStiffness = v / perDegree), 0, 1e7, "0.###"));
            Inspector.Children.Add(NumberField("Dönme sönümü (N·mm·s/°)", joint.AngularDriveDamping * perDegree, v => Edit("Sürücü değişti", () => joint.AngularDriveDamping = v / perDegree), 0, 1e7, "0.###"));
            Inspector.Children.Add(Vector3Row("Hedef burulma / salınım 1 / 2 (°)", joint.TargetTwist, joint.TargetSwing1, joint.TargetSwing2,
                (x, y, z) => Edit("Sürücü hedefi değişti", () => (joint.TargetTwist, joint.TargetSwing1, joint.TargetSwing2) = (x, y, z))));
            Inspector.Children.Add(Note("Kilitli eksenlerde sürücü çalışmaz; yay parçayı hedefe çeker, sönüm hızla orantılı frenler."));
        }
    }
}
