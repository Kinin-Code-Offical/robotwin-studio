using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [mobile] Drive kinematics of a wheeled robot, on its chassis: the wheels and how they grip, the wheel speeds a
    /// wanted motion needs (inverse kinematics), the speed the wheels give against the speed the chassis really has
    /// (slip), and encoder odometry against the true pose with both paths drawn. CoreSim Robotics/WorldVehicle.cs.
    /// </summary>
    public partial class WorldPage
    {
        private WorldVehicle? _vehicle;
        private WorldPhysics? _odoPhysics;
        private readonly WheelOdometry _odometry = new WheelOdometry();
        private readonly List<Vec3> _odoPath = new List<Vec3>();
        private readonly List<Vec3> _truePath = new List<Vec3>();
        private bool _showPaths = true;
        private int _drawnPoints = -1;
        private TextBlock? _vehicleLive;
        private TextBlock? _vehicleIk;
        private PlanarTwist _vehicleWant = new PlanarTwist(0.2, 0, 0);

        /// <summary>The chassis of a wheeled robot: its drive kinematics.</summary>
        private void VehicleSection(WorldDocument world, WorldObject o)
        {
            _vehicleLive = null;
            _vehicleIk = null;
            var v = WorldVehicle.Build(world, Scene.GeometryOf, o.Id);
            if (v == null || v.BaseId != o.Id) return;
            if (_vehicle?.BaseId != v.BaseId) ForgetVehicle();
            _vehicle = v;
            Inspector.Children.Add(Caption("Sürüş kinematiği"));
            double dia = v.Wheels.Average(x => x.Radius) * 2000;
            string geometry = $"İz genişliği {v.Track * 1000:0} mm" + (v.Wheelbase > 0.005 ? $" · aks aralığı {v.Wheelbase * 1000:0} mm" : string.Empty) + $" · tekerlek Ø{dia:0} mm";
            Inspector.Children.Add(Note(char.ToUpper(v.Layout[0], CultureInfo.CurrentCulture) + v.Layout.Substring(1) + ". " + geometry + "."));
            _vehicleLive = new TextBlock { FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("B.TextSecondary"), Margin = new Thickness(0, 0, 0, 6) };
            Inspector.Children.Add(_vehicleLive);
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            row.Children.Add(Small("Odometriyi sıfırla", () =>
            {
                ResetOdometry();
                UpdateVehicleLive();
            }, "Tahmini konumu gerçek konuma getirir ve iki yolu siler"));
            Inspector.Children.Add(row);
            var paths = new CheckBox { Content = "Yolları sahnede göster", IsChecked = _showPaths, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 2, 0, 6), ToolTip = "Turuncu: tekerlek enkoderlerinden tahmin edilen yol (odometri). Yeşil: şasinin gerçekte gittiği yol." };
            paths.Click += (_, _) =>
            {
                _showPaths = paths.IsChecked == true;
                DrawPaths();
            };
            Inspector.Children.Add(paths);

            // Inverse kinematics: the wheel speeds a motion needs.
            Inspector.Children.Add(Note("İstenen hareket için tekerlek hızları (ters kinematik):"));
            Inspector.Children.Add(NumberField("İleri hız (m/s)", _vehicleWant.Vx, x =>
            {
                _vehicleWant = new PlanarTwist(x, _vehicleWant.Vy, _vehicleWant.W);
                ShowVehicleIk();
            }, -5, 5, "0.###"));
            if (v.Holonomic)
            {
                Inspector.Children.Add(NumberField("Sola hız (m/s)", _vehicleWant.Vy, y =>
                {
                    _vehicleWant = new PlanarTwist(_vehicleWant.Vx, y, _vehicleWant.W);
                    ShowVehicleIk();
                }, -5, 5, "0.###"));
            }

            Inspector.Children.Add(NumberField("Dönüş hızı (°/s, sola +)", _vehicleWant.W * 180 / Math.PI, d =>
            {
                _vehicleWant = new PlanarTwist(_vehicleWant.Vx, _vehicleWant.Vy, d * Math.PI / 180);
                ShowVehicleIk();
            }, -3600, 3600, "0.#"));
            _vehicleIk = new TextBlock { FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("B.TextSecondary"), Margin = new Thickness(0, 0, 0, 6) };
            Inspector.Children.Add(_vehicleIk);
            ShowVehicleIk();
            UpdateVehicleLive();
            DrawPaths();
        }

        private void ForgetVehicle()
        {
            _vehicle = null;
            _odoPhysics = null;
            _odoPath.Clear();
            _truePath.Clear();
            Scene.ShowPaths(null, null);
        }

        /// <summary>"Sol ön", "Sağ" …: where a wheel sits on the chassis.</summary>
        private string WheelLabel(DriveWheel w)
        {
            string side = w.Left ? "Sol" : "Sağ";
            if (_vehicle == null || _vehicle.Wheelbase < 0.005) return side;
            double mid = (_vehicle.Wheels.Max(x => x.Position.X) + _vehicle.Wheels.Min(x => x.Position.X)) / 2;
            return side + (w.Position.X > mid ? " ön" : " arka");
        }

        /// <summary>A wheel's speed as people read it: revolutions per minute, rolling forward or back.</summary>
        private static string Rpm(DriveWheel w, double rate)
        {
            double rpm = rate * 60 / (2 * Math.PI) * Math.Sign(w.Roll.X == 0 ? 1 : w.Roll.X);
            return $"{Math.Abs(rpm).ToString("0", CultureInfo.CurrentCulture),4} dev/dk {(Math.Abs(rpm) < 0.5 ? "durur" : rpm > 0 ? "ileri" : "geri")}";
        }

        private void ShowVehicleIk()
        {
            if (_vehicleIk == null || _vehicle == null) return;
            var v = _vehicle;
            var want = _vehicleWant;
            var ok = v.Achievable(want);
            var rates = v.ShaftRates(ok);
            var sb = new StringBuilder();
            for (int i = 0; i < v.Wheels.Count; i++) sb.AppendLine($"{WheelLabel(v.Wheels[i]),-9} {Rpm(v.Wheels[i], rates[i])}");
            double radius = ok.TurnRadius;
            sb.Append(double.IsPositiveInfinity(radius) ? "Düz gider." : radius < 0.005 ? "Olduğu yerde döner." : $"Dönüş yarıçapı {radius * 1000:0} mm.");
            if (Math.Abs(want.Vy) > 1e-9 && !v.Holonomic) sb.Append(" Bu araç yana gidemez; yana hız yok sayıldı.");
            if (v.Skids && Math.Abs(ok.W) > 1e-6) sb.Append(" Kızaklı dönüşte tekerlekler yana kayar: gerçek dönüş bundan yavaştır.");
            _vehicleIk.Text = Shell.Lang.T(sb.ToString());   // [lang]
        }

        private void ResetOdometry()
        {
            if (_vehicle == null) return;
            var physics = _session.State != SimState.Stopped ? _session.Physics : null;
            var pose = _vehicle.PlanarPose(physics)!.Value;
            _odometry.Reset(pose.X, pose.Y, pose.Heading);
            _odoPhysics = physics;
            _odoPath.Clear();
            _truePath.Clear();
            if (physics != null) _odometry.Update(_vehicle, _vehicle.ShaftAngles(physics));
            DrawPaths();
        }

        /// <summary>Every simulation frame: the encoders' change goes into the odometry (exact for any rate but the heading's mid-step rule).</summary>
        private void UpdateVehicleOdometry()
        {
            if (_vehicle == null) return;
            if (World?.Find(_vehicle.BaseId) == null)
            {
                ForgetVehicle();   // another world, or the chassis was deleted
                return;
            }

            var physics = _session.State != SimState.Stopped ? _session.Physics : null;
            if (physics == null) return;
            if (!ReferenceEquals(physics, _odoPhysics)) ResetOdometry();   // a new run starts from the true pose
            _odometry.Update(_vehicle, _vehicle.ShaftAngles(physics));
            var truth = _vehicle.PlanarPose(physics)!.Value;
            Remember(_odoPath, new Vec3(_odometry.X, _odometry.Y, 0.002));
            Remember(_truePath, new Vec3(truth.X, truth.Y, 0.003));
        }

        private static void Remember(List<Vec3> path, Vec3 p)
        {
            if (path.Count > 0 && (path[^1] - p).Length < 0.005) return;
            path.Add(p);
            if (path.Count > 4000) path.RemoveRange(0, 1000);
        }

        private void DrawPaths() => Scene.ShowPaths(_showPaths && _vehicle != null && _odoPath.Count > 1 ? _odoPath : null, _showPaths && _vehicle != null && _truePath.Count > 1 ? _truePath : null);

        private void UpdateVehicleLive()
        {
            if (_vehicle == null) return;
            // The paths are drawn again only when they grew (a tube of thousands of points is not free).
            if (_odoPath.Count + _truePath.Count != _drawnPoints)
            {
                _drawnPoints = _odoPath.Count + _truePath.Count;
                DrawPaths();
            }

            if (_vehicleLive == null) return;
            var v = _vehicle;
            var physics = _session.State != SimState.Stopped ? _session.Physics : null;
            if (physics == null)
            {
                _vehicleLive.Text = "Çalışırken burada tekerlek hızları, tekerleklerden hesaplanan hız ile gerçek hız (kayma) ve odometri sapması görünür.";
                return;
            }

            var rates = v.MeasuredRates(physics);
            var sb = new StringBuilder();
            for (int i = 0; i < v.Wheels.Count; i++) sb.AppendLine($"{WheelLabel(v.Wheels[i]),-9} {Rpm(v.Wheels[i], rates[i])}");
            var est = v.Twist(rates);
            var real = v.MeasuredTwist(physics);
            sb.AppendLine($"Tekerlekten {Twist(est, v.Holonomic)}");
            sb.Append($"Gerçek      {Twist(real, v.Holonomic)}");
            double slip = est.Speed > 0.02 ? Math.Abs(est.Speed - real.Speed) / est.Speed : 0;
            if (slip > 0.005) sb.AppendLine().Append($"Kayma       %{slip * 100:0}");
            var truth = v.PlanarPose(physics)!.Value;
            double ex = truth.X - _odometry.X, ey = truth.Y - _odometry.Y;
            double drift = Math.Sqrt(ex * ex + ey * ey);
            double dh = Math.Abs(Math.IEEERemainder(truth.Heading - _odometry.Heading, 2 * Math.PI)) * 180 / Math.PI;
            sb.AppendLine();
            sb.AppendLine($"Odometri    {Mm(_odometry.X)} · {Mm(_odometry.Y)} mm · {Deg(_odometry.Heading)}°");
            sb.AppendLine($"Gerçek      {Mm(truth.X)} · {Mm(truth.Y)} mm · {Deg(truth.Heading)}°");
            // The drift as a share of the distance driven, in brackets.
            sb.Append($"Sapma       {drift * 1000:0} mm" + (_odometry.Distance > 0.05 ? $" (%{drift / _odometry.Distance * 100:0.0})" : string.Empty) + $" · {dh:0.0}°");
            _vehicleLive.Text = Shell.Lang.T(sb.ToString());   // [lang] line by line
        }

        /// <summary>A number rounded for display, without a "−0".</summary>
        private static string Shown(double v, int decimals) => (Math.Round(v, decimals) + 0.0).ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), CultureInfo.CurrentCulture);

        private static string Deg(double rad) => Shown(Math.IEEERemainder(rad, 2 * Math.PI) * 180 / Math.PI, 1);

        private static string Twist(PlanarTwist t, bool sideways) =>
            Shown(t.Vx, 2) + (sideways ? " / " + Shown(t.Vy, 2) : string.Empty) + $" m/s · {Shown(t.W * 180 / Math.PI, 0)} °/s";

        /// <summary>[mobile] Snapshot option --vehicle "Plaka": selects that chassis before the run, so its odometry runs from the start.</summary>
        private void VehicleSnapshotBegin()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--vehicle");
            if (i < 0 || i + 1 >= args.Length || World?.Objects.FirstOrDefault(o => o.Name.StartsWith(args[i + 1], StringComparison.OrdinalIgnoreCase)) is not { } chassis) return;
            Scene.Select(chassis.Id);
            BuildInspector();
        }
    }
}
