using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [break] Unity's joint limits and Break Force / Break Torque for one joint, folded until used: a hinge's or slider's
    /// travel (° or mm) and the force (N) and torque (N·m) above which the joint lets go. The physics already holds the
    /// limits and tests both loads every step; a fastening's own strength stands while no force is given here.
    /// </summary>
    public partial class WorldPage
    {
        private readonly HashSet<string> _limitsOpen = new HashSet<string>();

        private void JointLimitRows(WorldJoint j)
        {
            bool moving = j.Axial, hinge = j.Kind == WorldJointKind.Hinge;   // [d6] a six-axis joint shows its limits in its own card
            bool used = (moving && (j.Lower.HasValue || j.Upper.HasValue)) || j.BreakForce > 0 || j.BreakTorque > 0;
            bool open = used || _limitsOpen.Contains(j.Id);
            var head = new Button
            {
                Content = (open ? "▾ " : "▸ ") + (moving ? "Sınırlar ve kırılma" : "Kırılma"),
                Style = (Style)FindResource("Btn.Ghost"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(8, 0, 0, 4),
                ToolTip = moving ? "Unity'deki eklem sınırları ve Break Force / Break Torque: eklemin gidebileceği aralık ve onu koparan kuvvet ile tork"
                                 : "Unity'deki Break Force / Break Torque: bağlantıyı koparan kuvvet ve tork"
            };
            head.Click += (_, _) =>
            {
                if (!_limitsOpen.Remove(j.Id)) _limitsOpen.Add(j.Id);
                BuildInspector();
            };
            Inspector.Children.Add(head);
            if (!open) return;

            var box = new StackPanel { Margin = new Thickness(8, 0, 0, 4) };
            if (moving)
            {
                double scale = hinge ? 1 : 1000;   // a slider's travel in mm
                string unit = hinge ? "°" : "mm";
                var limited = new CheckBox { Content = "Hareket sınırlı", IsChecked = j.Lower.HasValue || j.Upper.HasValue, Margin = new Thickness(0, 0, 0, 6) };
                limited.Click += (_, _) => Edit(limited.IsChecked == true ? "Eklem sınırlandı" : "Eklem sınırı kaldırıldı", () =>
                {
                    if (limited.IsChecked == true)
                    {
                        j.Lower = hinge ? -90 : 0;
                        j.Upper = hinge ? 90 : 0.05;
                    }
                    else
                    {
                        j.Lower = j.Upper = null;
                    }
                });
                box.Children.Add(limited);
                if (j.Lower.HasValue || j.Upper.HasValue)
                {
                    box.Children.Add(NumberField($"Alt sınır ({unit})", (j.Lower ?? 0) * scale, v => Edit("Eklem sınırı", () => j.Lower = v / scale)));
                    box.Children.Add(NumberField($"Üst sınır ({unit})", (j.Upper ?? 0) * scale, v => Edit("Eklem sınırı", () => j.Upper = v / scale)));
                }
            }

            box.Children.Add(NumberField(j.Kind == WorldJointKind.Fixed && j.Fastening.Length > 0 ? "Kırılma kuvveti (N, 0 = bağlantı türünden)" : "Kırılma kuvveti (N, 0 = kopmaz)",
                j.BreakForce, v => Edit("Kırılma kuvveti", () => j.BreakForce = v), 0, 1e7, "0.##"));
            box.Children.Add(NumberField("Kırılma torku (N·m, 0 = kopmaz)", j.BreakTorque, v => Edit("Kırılma torku", () => j.BreakTorque = v), 0, 1e6, "0.###"));
            Inspector.Children.Add(box);
        }
    }
}
