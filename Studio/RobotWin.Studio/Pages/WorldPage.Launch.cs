using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [launch] Inspector sections for a launch: a switch or button in the world can be closed by hand while
    /// the world runs, or on a countdown ("close.at" / "open.at"); any body can carry a recovery parachute
    /// thrown at apogee or at a set time.
    /// </summary>
    public partial class WorldPage
    {
        private static bool IsSwitchPart(WorldObject o) =>
            o.Kind == WorldObjectKind.Component && (string.Equals(o.PartId, "switch", StringComparison.OrdinalIgnoreCase) || string.Equals(o.PartId, "button", StringComparison.OrdinalIgnoreCase));

        /// <summary>Snapshot options: --frame &lt;name prefix&gt; centres the camera on that object, --thermal turns the heat camera on.</summary>
        private void LaunchSnapshot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--frame");
            if (i >= 0 && i + 1 < args.Length && World?.Objects.FirstOrDefault(o => o.Name.StartsWith(args[i + 1], StringComparison.OrdinalIgnoreCase)) is { } target)
            {
                Scene.Select(target.Id);
                Scene.FrameSelected();
                // An open parachute: frame the body and its canopy together.
                if (_session.Physics?.ParachuteOf(target.Id) is { Deployed: true } c)
                {
                    var mid = c.Attach + c.Pull * (c.Diameter * 0.9);
                    Scene.LookAt(new System.Windows.Media.Media3D.Point3D(mid.X, mid.Y, mid.Z), c.Diameter * 4.5, 12, -35);
                }
            }

            int d = Array.IndexOf(args, "--distance");
            if (d >= 0 && d + 1 < args.Length && double.TryParse(args[d + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double dist)) Scene.CameraDistance = dist;
            if (args.Contains("--thermal")) SetThermalView(true);
            if (args.Contains("--run"))
            {
                // Keep the world running through the snapshot's wait, the camera following the selection.
                Scene.Follow = true;
                _session.Resume();
                UpdateRunButton();
            }
        }

        private void LaunchSection(WorldDocument world, WorldObject o)
        {
            _ = world;
            if (IsSwitchPart(o))
            {
                Inspector.Children.Add(Caption("Anahtar (dünyada)"));
                var physics = _session.State == SimState.Stopped ? null : _session.Physics;
                bool? closed = physics?.SwitchState(o.Id);
                var toggle = new Button
                {
                    Content = closed == true ? "Aç" : "Kapat",
                    IsEnabled = closed != null,
                    ToolTip = closed == null ? "Simülasyon çalışırken anahtarı buradan açıp kapatabilirsiniz." : "Anahtarı şimdi " + (closed == true ? "aç" : "kapat"),
                    Margin = new Thickness(0, 0, 0, 6),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(14, 3, 14, 3)
                };
                toggle.Click += (_, _) =>
                {
                    if (_session.Physics is not { } ph || ph.SwitchState(o.Id) is not { } now) return;
                    ph.SetSwitch(o.Id, !now);
                    W.Status = $"{o.Name}: {(now ? "açıldı" : "kapandı")}";
                    BuildInspector();
                };
                Inspector.Children.Add(Labeled(closed == null ? "Durum: simülasyon durmuş" : $"Durum: {(closed == true ? "kapalı (akım geçiyor)" : "açık")}", toggle));
                Inspector.Children.Add(Field(o, "close.at", "Kendiliğinden kapanır (s, geri sayım)", ""));
                Inspector.Children.Add(Field(o, "open.at", "Kendiliğinden açılır (s)", ""));
                Inspector.Children.Add(Note("Bir fırlatma kontrolcüsünde olduğu gibi: anahtar kapanınca devre e-kibriti ateşler. Elle değiştirdiğiniz anahtar o çalışma boyunca öyle kalır."));
                return;
            }

            if (o.Kind != WorldObjectKind.Body || WorldPhysics.IsWing(o)) return;
            Inspector.Children.Add(Caption("Kurtarma paraşütü", 8));
            Inspector.Children.Add(Field(o, "parachute", "Kanopi çapı (m, boş = yok)", ""));
            if (o.Parameters.ContainsKey("parachute"))
            {
                Inspector.Children.Add(Field(o, "parachute.deploy", "Açılma zamanı (s, boş = tepe noktasında)", ""));
                Inspector.Children.Add(Field(o, "parachute.cd", "Sürükleme katsayısı (boş = 0.75, düz dairesel)", ""));
                Inspector.Children.Add(Note("Altimetre tırmanmanın bittiğini görünce paraşüt atılır; ipler gerilince kanopi Knacke'nin dolma süresiyle (t = 8·D/v) açılır ve sürüklemesi gövdenin üstüne bağlanır. Açılma şoku ve sallanma fizikten çıkar."));
            }
        }
    }
}
