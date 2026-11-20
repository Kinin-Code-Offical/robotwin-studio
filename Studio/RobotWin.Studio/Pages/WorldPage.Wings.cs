using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [linkage] A wing's control surface on the inspector: which servo drives it, and the pushrod
    /// geometry (servo horn hole, control horn, rod length, servo torque). Shows the throw it gives
    /// before running, and live the horn angle under load, the hinge load on the servo and the rod
    /// force while running.
    /// </summary>
    public partial class WorldPage
    {
        private void WingSection(WorldDocument world, WorldObject o)
        {
            if (!WorldPhysics.IsWing(o)) return;
            if (!o.Parameters.TryGetValue("control", out var kind) || kind == "none") return;
            Inspector.Children.Add(Caption("Kumanda bağlantısı (servo → itme çubuğu → yüzey)"));

            var servos = world.Objects.Where(x => x.Kind == WorldObjectKind.Component && ((x.PresetId ?? x.PartId ?? string.Empty).Contains("servo", StringComparison.OrdinalIgnoreCase))).ToList();
            var pick = new ComboBox();
            pick.Items.Add(new ComboBoxItem { Content = "— sabit açı (servo yok)", Tag = string.Empty });
            foreach (var s in servos) pick.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s.Id });
            string current = o.Parameters.TryGetValue("control.servo", out var cs) ? cs : string.Empty;
            pick.SelectedItem = pick.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current) ?? pick.Items[0];
            pick.SelectionChanged += (_, _) =>
            {
                if (pick.SelectedItem is ComboBoxItem { Tag: string id }) Edit("Kumanda servosu", () => o.Parameters["control.servo"] = id);
            };
            Inspector.Children.Add(Labeled("Servo", pick));

            bool linked = WorldPhysics.HasLinkage(o);
            var rod = new CheckBox { Content = "Gerçek itme çubuğu", ToolTip = "Dört çubuk geometrisi; menteşe yükü çubuktan servoya döner", IsChecked = linked, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 2, 0, 6) };
            rod.Click += (_, _) => Edit("İtme çubuğu", () =>
            {
                if (rod.IsChecked == true)
                {
                    o.Parameters["control.linkage"] = "pushrod";
                    if (!o.Parameters.ContainsKey("control.horn")) o.Parameters["control.horn"] = "0.012";
                    if (!o.Parameters.ContainsKey("control.arm")) o.Parameters["control.arm"] = "0.024";
                    if (!o.Parameters.ContainsKey("control.rod")) o.Parameters["control.rod"] = "0.3";
                }
                else
                {
                    foreach (var k in new[] { "control.linkage", "control.horn", "control.arm", "control.rod", "control.torque" }) o.Parameters.Remove(k);
                }
            });
            Inspector.Children.Add(rod);
            if (!linked)
            {
                Inspector.Children.Add(Note("Kapalıyken yüzey servo açısını sabit oranla (control.gain) izler; hava yükü servoya yansımaz."));
                return;
            }

            Inspector.Children.Add(MmParam(o, "control.horn", "Servo kolu deliği (mm)", 12));
            Inspector.Children.Add(MmParam(o, "control.arm", "Yüzey kolu yüksekliği (mm)", 24));
            Inspector.Children.Add(MmParam(o, "control.rod", "Çubuk boyu (mm)", 300));
            var torque = new TextBox { Text = (Param(o, "control.torque", 0.176) * 100).ToString("0.0#", CultureInfo.CurrentCulture) };
            torque.LostFocus += (_, _) =>
            {
                if (double.TryParse(torque.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double ncm) && ncm > 0)
                    Edit("Servo torku", () => o.Parameters["control.torque"] = (ncm / 100).ToString(CultureInfo.InvariantCulture));
            };
            Inspector.Children.Add(Labeled("Servo kilit torku (N·cm)", torque));

            // What this geometry gives before running: the throw for ±45° of horn, and the ratio.
            var lk = new WorldPhysics.PushrodLinkage
            {
                HornRadius = Param(o, "control.horn", 0.012),
                ControlArm = Param(o, "control.arm", 0.024),
                RodLength = Param(o, "control.rod", 0.3)
            };
            double up = lk.SurfaceFor(45 * Math.PI / 180) * 180 / Math.PI, down = lk.SurfaceFor(-45 * Math.PI / 180) * 180 / Math.PI;
            Inspector.Children.Add(Note(FormattableString.Invariant($"Oran (merkezde) {lk.HornRadius / lk.ControlArm:0.00} · servo ±45° → yüzey {up:+0.0;-0.0}° / {down:+0.0;-0.0}° (dört çubuk geometrisi: oran açıyla değişir). Kolu kısaltmak yüzey açısını azaltır, servonun gördüğü yükü de.")));
        }

        private FrameworkElement MmParam(WorldObject o, string key, string label, double fallbackMm)
        {
            var box = new TextBox { Text = (Param(o, key, fallbackMm / 1000) * 1000).ToString("0.#", CultureInfo.CurrentCulture) };
            box.LostFocus += (_, _) =>
            {
                if (double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double mm) && mm > 0.5)
                    Edit(label, () => o.Parameters[key] = (mm / 1000).ToString(CultureInfo.InvariantCulture));
            };
            return Labeled(label, box);
        }

        private static double Param(WorldObject o, string key, double fallback) =>
            o.Parameters.TryGetValue(key, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        /// <summary>Live lines for a wing: deflection, lift, and the pushrod's load on the servo.</summary>
        private void WingLive(WorldObject o, List<string> lines)
        {
            if (!WorldPhysics.IsWing(o) || _session.Physics is not { } ph) return;
            lines.Add($"Yüzey açısı    : {ph.WingDeflection(o.Id):+0.0;-0.0}°");
            if (ph.WingLoads(o.Id) is { } wl) lines.Add($"Kuvvet         : {wl.Force.Length:0.00} N");
            if (ph.LinkageOf(o.Id) is { } lk)
            {
                lines.Add($"Servo kolu     : {lk.Horn * 180 / Math.PI:+0.0;-0.0}° (komut {lk.Command * 180 / Math.PI:+0.0;-0.0}°)");
                lines.Add($"Servo yükü     : {lk.ServoLoad * 100:0.00} N·cm / kilit {lk.StallTorque * 100:0.0} N·cm (%{Math.Abs(lk.ServoLoad) / lk.StallTorque * 100:0})");
                lines.Add($"Çubuk kuvveti  : {lk.RodForce:+0.00;-0.00} N ({(lk.RodForce >= 0 ? "çekme" : "basma")})");
                if (lk.BlownBack) lines.Add("UYARI          : hava yükü servoyu geri itiyor, yüzey komut açısına ulaşamıyor");
            }
        }
    }
}
