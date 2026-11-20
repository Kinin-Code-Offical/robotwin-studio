using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotWin.Studio.Assembly3D;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// "Montaj" page: the robot or drone built from the circuit, in 3D, driven live
    /// by the simulation, with pilot controls, telemetry and fault effects.
    /// </summary>
    public partial class AssemblyPage : UserControl
    {
        private enum CameraMode
        {
            Follow,
            Free,
            Top,
            Onboard
        }

        private readonly PerspectiveCamera _camera = new PerspectiveCamera { FieldOfView = 55, NearPlaneDistance = 0.02, FarPlaneDistance = 60 };
        private readonly ModelVisual3D _sceneVisual = new ModelVisual3D();
        private readonly HashSet<Key> _keys = new HashSet<Key>();
        private readonly Dictionary<string, TextBlock> _hudValues = new Dictionary<string, TextBlock>();
        private readonly Dictionary<string, double> _failedAt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _onFire = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private AssemblyScene? _scene;
        private MechatronicSystem? _sceneMech;
        private string? _sceneKey;
        private CameraMode _mode = CameraMode.Follow;
        private double _yaw = -2.4, _pitch = 0.42, _distance = 0.75;
        private Point3D _target = new Point3D(0, 0, 0.05);
        private Point? _drag;
        private bool _dragPan;
        private bool _hooked;
        private DateTime _lastFrame = DateTime.Now;
        private bool _rebuildingPanel;

        public AssemblyPage()
        {
            InitializeComponent();
            View.Camera = _camera;
            View.Children.Add(_sceneVisual);
            Effects.Project = Project;
            Loaded += (_, _) => Hook(true);
            Unloaded += (_, _) => Hook(false);
            ViewHost.MouseDown += OnMouseDown;
            ViewHost.MouseMove += OnMouseMove;
            ViewHost.MouseUp += (_, _) =>
            {
                _drag = null;
                ViewHost.ReleaseMouseCapture();
            };
            ViewHost.MouseWheel += (_, e) =>
            {
                _distance = Math.Max(0.12, Math.Min(12, _distance * Math.Pow(0.999, e.Delta)));
                e.Handled = true;
            };
            PreviewKeyDown += OnKeyDown;
            PreviewKeyUp += (_, e) => _keys.Remove(e.Key);

            W.Simulation.Failure += OnFailure;
            W.Simulation.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SimulationSession.State)) UpdateRunButton();
            };
            W.DocumentReplaced += (_, _) => Dispatcher.BeginInvoke(new Action(() => Rebuild(true)));
            W.DocumentChanged += (_, _) =>
            {
                if (IsVisible && !_rebuildingPanel) Rebuild(false);
            };
        }

        private static Workspace W => Workspace.Current;

        public void OnShown()
        {
            Rebuild(true);
            UpdateRunButton();
            Focus();
        }

        /// <summary>Used by --snapshot: runs the simulation for a while (arming a drone first).</summary>
        public void PrepareSnapshot(double seconds, string? fault = null)
        {
            if (fault == "mesh" && W.Schematic.Assembly is { } asm)
            {
                // Round printed chassis + a spherical rock in a custom world.
                var body = MeshModel.CreateCylinder(0.095, 0.03, 72);
                var b = body.Bounds;
                body.Translate(new Vec3(0, 0, asm.DeckZ - b.Max.Z));
                asm.ChassisMesh = body.ToEmbedded();
                asm.ChassisMeshName = "yuvarlak-govde.stl";
                asm.ChassisMaterial = "PETG";
                asm.Width = 0.15;
                asm.Length = 0.19;
                asm.World = WorldSpec.FromPreset(asm.Arena);
                asm.Arena = "custom";
                asm.World.MeshObstacles.Add(new WorldMeshObstacle { Name = "kaya", Mesh = MeshModel.CreateSphere(0.12, 32, 16).ToEmbedded(), X = 0.75, Y = 0.1 });
                Rebuild(true);
            }

            var sim = W.Simulation;
            if (!sim.Start()) return;
            sim.Pause();
            if (fault == "battery" && sim.Compiled?.Circuit.Devices.OfType<Battery>().FirstOrDefault() is { } bat) bat.ShortCircuitResistance = 0.05;
            if (fault == "motor" && sim.Compiled?.Circuit.Devices.OfType<DcMotor>().FirstOrDefault() is { } dm) dm.Jammed = true;
            if (sim.Mechanics?.Flight is { } fc)
            {
                fc.TargetAltitude = 0.5;
                fc.Throttle = 0.5;
                fc.Armed = true;
            }

            for (int i = 0; i < Math.Max(1, (int)(seconds / 2e-3)); i++)
            {
                if (sim.Mechanics?.Flight is { } f && sim.Time > seconds * 0.6) f.PitchInput = 0.3;
                sim.StepOnce(2e-3);
            }

            _mode = CameraMode.Follow;
            if (sim.Mechanics != null) _target = new Point3D(sim.Mechanics.Body.Position.X, sim.Mechanics.Body.Position.Y, sim.Mechanics.Body.Position.Z);
        }

        // ================================================================ scene

        private void Rebuild(bool force)
        {
            var assembly = W.Schematic.Assembly;
            bool has = assembly != null && assembly.Kind != VehicleKind.None;
            EmptyHint.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            Hud.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            string key = has ? W.Schematic.ToJson().GetHashCode().ToString(CultureInfo.InvariantCulture) : "none";
            if (!force && key == _sceneKey) return;
            _sceneKey = key;

            _sceneVisual.Content = null;
            _scene = null;
            _sceneMech = null;
            Effects.Clear();
            _failedAt.Clear();
            _onFire.Clear();
            if (has)
            {
                _scene = new AssemblyScene(assembly!, W.Schematic, W.Library);
                _sceneVisual.Content = _scene.Root;
                if (force)
                {
                    _distance = assembly!.Kind == VehicleKind.Quadcopter ? 0.55 : 0.75;
                    _target = _scene.BodyPosition;
                }
            }

            BuildPanel();
            BuildHud();
            UpdateCamera();
        }

        private void Hook(bool on)
        {
            if (on && !_hooked)
            {
                CompositionTarget.Rendering += OnFrame;
                _hooked = true;
                _lastFrame = DateTime.Now;
            }
            else if (!on && _hooked)
            {
                CompositionTarget.Rendering -= OnFrame;
                _hooked = false;
            }
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            var now = DateTime.Now;
            double dt = Math.Min(0.05, (now - _lastFrame).TotalSeconds);
            _lastFrame = now;
            if (!IsVisible) return;

            var sim = W.Simulation;
            var mech = sim.Mechanics;
            if (_scene != null && mech != null)
            {
                if (_sceneMech != mech)
                {
                    // A new run started: reset effects and the charred look.
                    _sceneMech = mech;
                    Effects.Clear();
                    _failedAt.Clear();
                    _onFire.Clear();
                }

                ApplyPilot(mech, dt);
                _scene.Update(mech, Temperature, Failed);
                _scene.UpdateScreens(r => sim.I2cDeviceOf(r));
                EmitEffects(dt);
            }
            else if (_scene != null && _sceneMech != null)
            {
                _sceneMech = null;
                _scene.PlaceAtStart();
                _scene.UpdateHeat(_ => 25, _ => false);
            }

            Effects.Step(dt);
            UpdateCamera();
            UpdateHud(mech);
            SimStatus.Text = sim.StatusText;
            ExpireToasts();
        }

        private double Temperature(string refDes)
        {
            var sim = W.Simulation;
            if (sim.Compiled == null || sim.Simulator == null) return 25;
            double t = double.NaN;
            foreach (var d in sim.Compiled.DevicesOf(refDes))
            {
                if (sim.Simulator.Signals.TryGetValue(d.Id + ":T", out var v)) t = double.IsNaN(t) ? v : Math.Max(t, v);
            }

            return t;
        }

        private bool Failed(string refDes)
        {
            var sim = W.Simulation;
            return sim.Compiled != null && sim.Compiled.DevicesOf(refDes).Any(d => d.IsFailed);
        }

        private void EmitEffects(double dt)
        {
            if (_scene == null) return;
            double time = W.Simulation.Time;
            foreach (var h in _scene.Heat.Values)
            {
                var at = _scene.ToWorld(h.Local);
                double t = Temperature(h.RefDes);
                if (!double.IsNaN(t) && t > 105) Effects.EmitSmoke(at, Math.Min(30, (t - 105) / 4), dt);
                if (_failedAt.TryGetValue(h.RefDes, out var since))
                {
                    double age = time - since;
                    if (_onFire.Contains(h.RefDes) && age < 12) Effects.EmitFire(at, 70, dt);
                    else if (age < 10) Effects.EmitSmoke(at, 18 * (1 - age / 10), dt, true);
                }
            }
        }

        private void OnFailure(object? sender, SimLogEntry entry)
        {
            string refDes = entry.Device.Split('.')[0];
            var msg = entry.Message.ToLowerInvariant();
            bool explode = msg.Contains("patla") || msg.Contains("termal kaçak");
            bool fire = msg.Contains("alev") || msg.Contains("termal kaçak");
            AddToast(entry.Device, entry.Message, true);
            if (_scene == null || !_scene.Heat.TryGetValue(refDes, out var h)) return;
            _failedAt[refDes] = W.Simulation.Time;
            if (fire) _onFire.Add(refDes);
            Effects.Burst(_scene.ToWorld(h.Local), explode);
        }

        // ================================================================ pilot

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            var mech = W.Simulation.Mechanics;
            if (e.Key == Key.Space && mech?.Flight != null)
            {
                mech.Flight.Armed = !mech.Flight.Armed;
                if (mech.Flight.Armed && mech.Flight.Mode == FlightMode.AltitudeHold) mech.Flight.TargetAltitude = Math.Max(mech.Body.Position.Z, 0.6);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.M && mech?.Flight != null)
            {
                mech.Flight.Mode = mech.Flight.Mode == FlightMode.Angle ? FlightMode.AltitudeHold : FlightMode.Angle;
                if (mech.Flight.Mode == FlightMode.AltitudeHold) mech.Flight.TargetAltitude = mech.Body.Position.Z;
                AddToast("FC", mech.Flight.Mode == FlightMode.Angle ? "Mod: Açı (manuel gaz)" : "Mod: İrtifa tutma", false);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.R && Keyboard.FocusedElement is not TextBox)
            {
                OnResetVehicle(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (Keyboard.FocusedElement is TextBox) return;
            _keys.Add(e.Key);
            if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right) e.Handled = true;
        }

        private double _manualThrottle;

        private void ApplyPilot(MechatronicSystem mech, double dt)
        {
            var fc = mech.Flight;
            if (fc == null) return;
            bool Has(Key k) => _keys.Contains(k) && Keyboard.IsKeyDown(k);
            double Axis(Key pos, Key neg) => (Has(pos) ? 1 : 0) - (Has(neg) ? 1 : 0);
            fc.PitchInput = Smooth(fc.PitchInput, 0.6 * Axis(Key.Up, Key.Down), dt);
            fc.RollInput = Smooth(fc.RollInput, 0.6 * Axis(Key.Right, Key.Left), dt);
            fc.YawInput = Smooth(fc.YawInput, 0.7 * Axis(Key.A, Key.D), dt);
            if (fc.Mode == FlightMode.AltitudeHold)
            {
                fc.Throttle = 0.5 + 0.5 * Axis(Key.W, Key.S);
            }
            else
            {
                _manualThrottle = Math.Max(0, Math.Min(1, _manualThrottle + Axis(Key.W, Key.S) * 0.5 * dt));
                fc.Throttle = _manualThrottle;
            }
        }

        private static double Smooth(double current, double target, double dt) => current + (target - current) * Math.Min(1, dt * 10);

        // ================================================================ camera

        private void OnCamera(object sender, RoutedEventArgs e)
        {
            _mode = CamFree.IsChecked == true ? CameraMode.Free : CamTop.IsChecked == true ? CameraMode.Top : CamFpv.IsChecked == true ? CameraMode.Onboard : CameraMode.Follow;
            if (_mode == CameraMode.Free && _scene != null) _target = _scene.BodyPosition;
            Focus();
        }

        private void UpdateCamera()
        {
            var body = _scene?.BodyPosition ?? new Point3D(0, 0, 0);
            switch (_mode)
            {
                case CameraMode.Onboard when _scene != null:
                {
                    var m = _scene.BodyMatrix;
                    var eye = m.Transform(new Point3D(-0.12, 0, 0.09));
                    var look = m.Transform(new Point3D(0.6, 0, -0.05));
                    _camera.Position = eye;
                    _camera.LookDirection = look - eye;
                    _camera.UpDirection = m.Transform(new Vector3D(0, 0, 1));
                    _camera.FieldOfView = 70;
                    return;
                }

                case CameraMode.Top:
                {
                    var a = _scene?.Arena;
                    double span = a == null ? 3 : Math.Max(a.Width, a.Depth * 1.3);
                    _camera.Position = new Point3D(0, -0.001, span * 0.95);
                    _camera.LookDirection = new Vector3D(0, 0.001, -1);
                    _camera.UpDirection = new Vector3D(0, 1, 0);
                    _camera.FieldOfView = 60;
                    return;
                }

                case CameraMode.Follow:
                    _target = new Point3D(_target.X + (body.X - _target.X) * 0.15, _target.Y + (body.Y - _target.Y) * 0.15, _target.Z + (body.Z - _target.Z) * 0.15);
                    break;
            }

            var dir = new Vector3D(Math.Cos(_pitch) * Math.Cos(_yaw), Math.Cos(_pitch) * Math.Sin(_yaw), Math.Sin(_pitch));
            _camera.Position = _target + dir * _distance;
            _camera.LookDirection = -dir;
            _camera.UpDirection = new Vector3D(0, 0, 1);
            _camera.FieldOfView = 55;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            _drag = e.GetPosition(ViewHost);
            _dragPan = e.ChangedButton != MouseButton.Left;
            ViewHost.CaptureMouse();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_drag == null) return;
            var p = e.GetPosition(ViewHost);
            var d = p - _drag.Value;
            _drag = p;
            if (_dragPan && _mode == CameraMode.Free)
            {
                var right = Vector3D.CrossProduct(_camera.LookDirection, new Vector3D(0, 0, 1));
                right.Normalize();
                var fwd = new Vector3D(_camera.LookDirection.X, _camera.LookDirection.Y, 0);
                if (fwd.Length > 1e-6) fwd.Normalize();
                double k = _distance * 0.0016;
                _target += -right * d.X * k + fwd * d.Y * k;
            }
            else
            {
                _yaw -= d.X * 0.008;
                _pitch = Math.Max(0.03, Math.Min(1.5, _pitch + d.Y * 0.006));
            }
        }

        /// <summary>World → screen projection of the current perspective camera (for the effects layer).</summary>
        private (Point Screen, double Depth, bool Visible) Project(Point3D p)
        {
            double w = ViewHost.ActualWidth, h = ViewHost.ActualHeight;
            var look = _camera.LookDirection;
            look.Normalize();
            var right = Vector3D.CrossProduct(look, _camera.UpDirection);
            if (right.Length < 1e-9) return (default, 0, false);
            right.Normalize();
            var up = Vector3D.CrossProduct(right, look);
            var rel = p - _camera.Position;
            double z = Vector3D.DotProduct(rel, look);
            if (z < _camera.NearPlaneDistance) return (default, z, false);
            double f = w / 2 / Math.Tan(_camera.FieldOfView * Math.PI / 360);
            Effects.Focal = f;
            double sx = w / 2 + Vector3D.DotProduct(rel, right) / z * f;
            double sy = h / 2 - Vector3D.DotProduct(rel, up) / z * f;
            return (new Point(sx, sy), z, true);
        }

        // ================================================================ HUD

        private void BuildHud()
        {
            HudGrid.Children.Clear();
            HudGrid.RowDefinitions.Clear();
            _hudValues.Clear();
            var a = W.Schematic.Assembly;
            if (a == null || a.Kind == VehicleKind.None) return;
            HudTitle.Text = a.Kind == VehicleKind.Quadcopter ? "QUADCOPTER" : "ROBOT ARABA";
            var rows = new List<(string Key, string Label)> { ("t", "Zaman"), ("mass", "Kütle"), ("bat", "Batarya") };
            if (a.Kind == VehicleKind.Quadcopter)
            {
                rows.AddRange(new[] { ("arm", "Durum"), ("alt", "İrtifa"), ("att", "Eğim (R/P)"), ("hdg", "Yön"), ("spd", "Hız"), ("mot", "Motorlar") });
                ControlsHint.Text = "Boşluk: kur/kapat · W/S: yüksel/alçal · A/D: dön · Oklar: ileri-geri-yan · M: mod · R: başa al · Fare: kamera";
            }
            else
            {
                rows.AddRange(new[] { ("spd", "Hız"), ("dist", "Yol"), ("hdg", "Yön"), ("mot", "Motor devri"), ("sen", "Sensörler") });
                ControlsHint.Text = "Araç devreyle sürülür · R: başa al · Sol tuş: kamerayı döndür · Teker: yakınlaştır · Sağ tuş (Serbest): kaydır";
            }

            rows.Add(("hot", "En sıcak"));
            for (int i = 0; i < rows.Count; i++)
            {
                HudGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new TextBlock { Text = rows[i].Label, Foreground = (Brush)FindResource("B.TextMuted"), FontSize = 12, Margin = new Thickness(0, 1, 0, 1) };
                var value = new TextBlock { Text = "—", FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, Margin = new Thickness(0, 1, 0, 1) };
                Grid.SetRow(label, i);
                Grid.SetRow(value, i);
                Grid.SetColumn(value, 2);
                HudGrid.Children.Add(label);
                HudGrid.Children.Add(value);
                _hudValues[rows[i].Key] = value;
            }
        }

        private void SetHud(string key, string text, Brush? brush = null)
        {
            if (!_hudValues.TryGetValue(key, out var tb)) return;
            tb.Text = text;
            tb.Foreground = brush ?? (Brush)FindResource("B.Text");
        }

        private void UpdateHud(MechatronicSystem? mech)
        {
            if (_hudValues.Count == 0) return;
            var sim = W.Simulation;
            SetHud("t", SiValue.Format(sim.Time, "s"));
            if (mech == null)
            {
                foreach (var k in _hudValues.Keys.Where(k => k != "t")) SetHud(k, "—");
                SetHud("arm", "Simülasyon kapalı", (Brush)FindResource("B.TextMuted"));
                return;
            }

            SetHud("mass", $"{mech.TotalMass * 1000:F0} g");
            var bat = sim.Compiled?.Circuit.Devices.OfType<Battery>().FirstOrDefault();
            if (bat != null)
            {
                SetHud("bat", $"{bat.Voltage:F2} V  {bat.Current:F2} A  %{bat.StateOfCharge * 100:F0}",
                    bat.IsFailed ? (Brush)FindResource("B.Danger") : bat.StateOfCharge < 0.15 ? (Brush)FindResource("B.Warning") : null);
            }

            var (roll, pitch, yaw) = mech.Body.Orientation.ToEuler();
            SetHud("hdg", $"{(yaw * 180 / Math.PI + 360) % 360:F0}°");
            SetHud("spd", $"{mech.Speed:F2} m/s");
            SetHud("dist", $"{mech.Distance:F2} m");
            if (mech.Flight != null)
            {
                var fc = mech.Flight;
                SetHud("arm", !fc.Powered ? "Güç yok" : fc.Armed ? (fc.Mode == FlightMode.AltitudeHold ? "UÇUŞ · irtifa tutma" : "UÇUŞ · açı modu") : "Hazır (Boşluk ile kur)",
                    fc.Armed ? (Brush)FindResource("B.Success") : (Brush)FindResource("B.TextSecondary"));
                SetHud("alt", fc.Mode == FlightMode.AltitudeHold ? $"{mech.Altitude:F2} m → {fc.TargetAltitude - mech.RideHeight:F2} m" : $"{mech.Altitude:F2} m");
                static string Deg(double rad)
                {
                    double d = Math.Round(rad * 180 / Math.PI, 1);
                    return (d > 0 ? "+" : d < 0 ? "−" : " ") + Math.Abs(d).ToString("0.0", CultureInfo.CurrentCulture) + "°";
                }

                SetHud("att", $"{Deg(roll)} / {Deg(pitch)}");
            }

            var motors = mech.Kind == VehicleKind.Quadcopter
                ? string.Join(" ", mech.Props.Select(p => p.Broken ? "KIRIK" : $"{(p.Motor?.AngularVelocity ?? 0) * 60 / (2 * Math.PI) / 1000:F0}k"))
                : string.Join(" / ", mech.Wheels.Select(w => $"{w.Motor?.OutputRpm ?? 0:F0}"));
            SetHud("mot", mech.Kind == VehicleKind.Quadcopter ? motors + " rpm" : motors + " rpm");
            if (mech.Sensors.Count > 0)
            {
                SetHud("sen", string.Join("  ", mech.Sensors.Select(s => s.Mount.Kind switch
                {
                    SensorKind.Line => s.Triggered ? "■" : "□",
                    SensorKind.Ultrasonic => $"{s.Value * 100:F0}cm",
                    SensorKind.Obstacle => s.Triggered ? "ENGEL" : "açık",
                    _ => "·"
                })));
            }

            // Hottest part on the vehicle
            if (_scene != null && _scene.Heat.Count > 0)
            {
                var hot = _scene.Heat.Keys.Select(r => (Ref: r, T: Temperature(r))).Where(x => !double.IsNaN(x.T)).OrderByDescending(x => x.T).FirstOrDefault();
                if (hot.Ref != null)
                {
                    SetHud("hot", $"{hot.Ref} {hot.T:F0} °C", hot.T > 100 ? (Brush)FindResource("B.Danger") : hot.T > 70 ? (Brush)FindResource("B.Warning") : null);
                }
            }
        }

        private readonly List<(Border Toast, DateTime Until)> _toasts = new List<(Border, DateTime)>();

        private void AddToast(string source, string message, bool danger)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x16, 0x16, 0x18)),
                BorderBrush = danger ? (Brush)FindResource("B.Danger") : (Brush)FindResource("B.Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = source, FontSize = 11, Foreground = danger ? (Brush)FindResource("B.Danger") : (Brush)FindResource("B.TextMuted") });
            sp.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            border.Child = sp;
            Toasts.Children.Insert(0, border);
            _toasts.Add((border, DateTime.Now.AddSeconds(danger ? 8 : 3)));
            while (Toasts.Children.Count > 5) Toasts.Children.RemoveAt(Toasts.Children.Count - 1);
        }

        private void ExpireToasts()
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                if (DateTime.Now < _toasts[i].Until) continue;
                Toasts.Children.Remove(_toasts[i].Toast);
                _toasts.RemoveAt(i);
            }
        }

        // ================================================================ toolbar

        private void UpdateRunButton()
        {
            bool running = W.Simulation.State == SimState.Running;
            RunText.Text = running ? "Duraklat" : W.Simulation.State == SimState.Paused ? "Devam" : "Başlat";
            RunIcon.Data = (Geometry)FindResource(running ? "I.Pause" : "I.Play");
        }

        private void OnRun(object sender, RoutedEventArgs e)
        {
            var sim = W.Simulation;
            switch (sim.State)
            {
                case SimState.Running:
                    sim.Pause();
                    break;
                case SimState.Paused:
                    sim.Resume();
                    break;
                default:
                    if (!sim.Start()) AddToast("Simülasyon", sim.StatusText, true);
                    break;
            }

            UpdateRunButton();
            Focus();
        }

        private void OnSaveRecording(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.SaveRecording();

        private void OnStop(object sender, RoutedEventArgs e)
        {
            W.Simulation.Stop();
            UpdateRunButton();
            Focus();
        }

        private void OnResetVehicle(object sender, RoutedEventArgs e)
        {
            W.Simulation.ResetVehicle();
            Effects.Clear();
            Focus();
        }

        private void OnAutoConfigure(object sender, RoutedEventArgs e)
        {
            W.BeginChange();
            var current = W.Schematic.Assembly;
            var fresh = AssemblyDocument.AutoConfigure(W.Schematic, W.Library,
                current == null ? null : new AssemblyDocument { Kind = current.Kind, Arena = current.Arena });
            if (fresh.Kind == VehicleKind.None)
            {
                W.CancelChange();
                AddToast("Montaj", "Devrede araç oluşturacak motor ya da uçuş kontrolcüsü yok.", false);
                return;
            }

            W.Schematic.Assembly = fresh;
            W.CommitChange("Montaj devreden yeniden oluşturuldu");
            Rebuild(true);
        }

        // ================================================================ assembly panel

        private void Edit(Action<AssemblyDocument> change, bool rebuildPanel = false)
        {
            var a = W.Schematic.Assembly;
            if (a == null) return;
            _rebuildingPanel = true;
            W.BeginChange();
            change(a);
            W.CommitChange("Montaj düzenlendi");
            _rebuildingPanel = false;
            Rebuild(true);
        }

        private TextBlock Overline(string text, double top = 18) =>
            new TextBlock { Text = text, Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, top, 0, 6) };

        private TextBlock Caption(string text) => new TextBlock { Text = text, Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 0, 0, 4) };

        private void BuildPanel()
        {
            Panel.Children.Clear();
            var a = W.Schematic.Assembly;
            Panel.Children.Add(Overline("Araç", 0));
            var kind = new ComboBox { ItemsSource = new[] { "Yok", "Robot araba", "Quadcopter" }, SelectedIndex = a == null ? 0 : (int)a.Kind };
            kind.SelectionChanged += (_, _) =>
            {
                var k = (VehicleKind)kind.SelectedIndex;
                if (W.Schematic.Assembly == null)
                {
                    W.BeginChange();
                    W.Schematic.Assembly = AssemblyDocument.AutoConfigure(W.Schematic, W.Library, new AssemblyDocument { Kind = k, Arena = k == VehicleKind.Quadcopter ? "empty" : "oval" });
                    W.Schematic.Assembly.Kind = k;
                    W.CommitChange("Montaj oluşturuldu");
                    Rebuild(true);
                }
                else
                {
                    Edit(x => x.Kind = k);
                }
            };
            Panel.Children.Add(kind);
            if (a == null || a.Kind == VehicleKind.None) return;

            Panel.Children.Add(Overline("Ortam"));
            var arenas = Arena.PresetNames.Concat(new[] { "custom" }).ToList();
            var arena = new ComboBox
            {
                ItemsSource = arenas.Select(n => n == "custom" ? "Özel dünya (kendin tasarla)" : Arena.Create(n).Name).ToList(),
                SelectedIndex = Math.Max(0, arenas.IndexOf(a.Arena))
            };
            arena.SelectionChanged += (_, _) => Edit(x =>
            {
                string chosen = arenas[arena.SelectedIndex];
                if (chosen == "custom" && x.World == null) x.World = WorldSpec.FromPreset(x.Arena == "custom" ? "empty" : x.Arena);
                x.Arena = chosen;
                x.StartX = x.StartY = x.StartYaw = null;
            });
            Panel.Children.Add(arena);
            if (a.Arena == "custom" && a.World != null) BuildWorldEditor(a.World);

            // Frame dimensions
            Panel.Children.Add(Overline(a.Kind == VehicleKind.Quadcopter ? "Gövde" : "Şasi"));
            if (a.Kind == VehicleKind.Rover)
            {
                Panel.Children.Add(Pair(("Uzunluk (mm)", a.Length * 1000, v => a.Length = v / 1000), ("Genişlik (mm)", a.Width * 1000, v => a.Width = v / 1000)));
                Panel.Children.Add(Pair(("Teker çapı (mm)", a.WheelRadius * 2000, v => a.WheelRadius = v / 2000), ("Şasi kütlesi (g)", a.ChassisMass * 1000, v => a.ChassisMass = v / 1000)));
            }
            else
            {
                Panel.Children.Add(Pair(("Kol uzunluğu (mm)", a.ArmLength * 1000, v => a.ArmLength = v / 1000), ("Pervane çapı (mm)", a.PropDiameter * 1000, v => a.PropDiameter = v / 1000)));
                Panel.Children.Add(Pair(("Gövde kütlesi (g)", a.FrameMass * 1000, v => a.FrameMass = v / 1000), ("—", double.NaN, _ => { })));
            }

            BuildChassisModelSection(a);

            // Motors
            Panel.Children.Add(Overline("Motorlar"));
            var motorParts = W.Schematic.Parts.Where(p => W.Library.Find(p.PartId)?.Symbol == SymbolKind.Motor).ToList();
            var roles = a.Kind == VehicleKind.Quadcopter ? new[] { "M1", "M2", "M3", "M4" } : new[] { "L", "R" };
            string RoleName(string r) => r switch
            {
                "L" => "Sol teker",
                "R" => "Sağ teker",
                "M1" => "M1 · ön sol (CW)",
                "M2" => "M2 · ön sağ (CCW)",
                "M3" => "M3 · arka sağ (CW)",
                "M4" => "M4 · arka sol (CCW)",
                _ => r
            };
            if (motorParts.Count == 0) Panel.Children.Add(Caption("Devrede motor yok."));
            foreach (var role in roles)
            {
                var mount = a.MotorFor(role);
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.Children.Add(new TextBlock { Text = RoleName(role), VerticalAlignment = VerticalAlignment.Center });
                var choice = new ComboBox { ItemsSource = new[] { "—" }.Concat(motorParts.Select(p => p.RefDes)).ToList(), SelectedItem = mount?.RefDes ?? "—" };
                choice.SelectionChanged += (_, _) => Edit(x =>
                {
                    var sel = (string)choice.SelectedItem;
                    x.Motors.RemoveAll(m => m.Role == role || m.RefDes == sel);
                    if (sel != "—") x.Motors.Add(new MotorMount { RefDes = sel, Role = role });
                });
                Grid.SetColumn(choice, 1);
                grid.Children.Add(choice);
                var reverse = new CheckBox { Content = "Ters", Foreground = (Brush)FindResource("B.Text"), IsChecked = mount?.Reverse == true, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsEnabled = mount != null };
                reverse.Click += (_, _) => Edit(x =>
                {
                    var m = x.MotorFor(role);
                    if (m != null) m.Reverse = reverse.IsChecked == true;
                });
                Grid.SetColumn(reverse, 2);
                grid.Children.Add(reverse);
                Panel.Children.Add(grid);
            }

            // Sensors
            Panel.Children.Add(Overline("Sensörler"));
            if (a.Sensors.Count == 0) Panel.Children.Add(Caption("Montajda sensör yok (TCRT5000, IR engel, HC-SR04 otomatik eklenir)."));
            foreach (var s in a.Sensors)
            {
                string kindName = s.Kind switch
                {
                    SensorKind.Line => "çizgi",
                    SensorKind.Obstacle => "engel",
                    SensorKind.Ultrasonic => "ultrasonik",
                    _ => "IMU"
                };
                Panel.Children.Add(Caption($"{s.RefDes} · {kindName}"));
                Panel.Children.Add(Pair(("X (mm)", s.X * 1000, v => s.X = v / 1000), ("Y (mm)", s.Y * 1000, v => s.Y = v / 1000)));
            }

            // Modules
            Panel.Children.Add(Overline("Kartlar ve modüller"));
            if (a.Modules.Count == 0) Panel.Children.Add(Caption("Montajda kart yok."));
            foreach (var m in a.Modules)
            {
                var part = W.Schematic.FindPart(m.RefDes);
                string name = part != null ? (W.Library.FindPreset(part.PresetId ?? string.Empty)?.Name ?? W.Library.Find(part.PartId)?.Name ?? part.PartId) : m.RefDes;
                Panel.Children.Add(Caption($"{m.RefDes} · {name}"));
                Panel.Children.Add(Pair(("X (mm)", m.X * 1000, v => m.X = v / 1000), ("Y (mm)", m.Y * 1000, v => m.Y = v / 1000)));
                double est = part != null && W.Library.Find(part.PartId) is { } md ? AssemblyDocument.PartMass(md, W.Schematic.ResolveParameters(W.Library, part)) : 0;
                Panel.Children.Add(Pair(($"Ağırlık (g) · tahmini {est * 1000:0.#}", m.MassKg.HasValue ? m.MassKg.Value * 1000 : double.NaN, v => m.MassKg = v > 0 ? v / 1000 : null),
                    ("Döndür (°)", m.Rotation, v => m.Rotation = (int)Math.Round(v))));
            }

            BuildFaultSection(a);

            Panel.Children.Add(Overline("Başlangıç"));
            Panel.Children.Add(Pair(("X (m)", a.StartX ?? double.NaN, v => a.StartX = v), ("Y (m)", a.StartY ?? double.NaN, v => a.StartY = v)));
            Panel.Children.Add(Caption("Boş bırakılırsa ortamın başlangıç noktası kullanılır."));
        }

        /// <summary>Size, grip, gravity, temperature, track and obstacles of the user's world.</summary>
        private void BuildWorldEditor(WorldSpec w)
        {
            Panel.Children.Add(Pair(("Genişlik (m)", w.Width, v => w.Width = v), ("Derinlik (m)", w.Depth, v => w.Depth = v)));
            Panel.Children.Add(Pair(("Zemin sürtünmesi (µ)", w.Friction, v => w.Friction = v), ("Yerçekimi (m/s²)", w.Gravity, v => w.Gravity = v)));
            Panel.Children.Add(Pair(("Ortam sıcaklığı (°C)", w.AmbientC, v => w.AmbientC = v), ("Pist ölçeği", w.TrackScale, v => w.TrackScale = v)));
            var gravity = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (label, g) in new[] { ("Dünya", 9.81), ("Ay", 1.62), ("Mars", 3.71) })
            {
                var b = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 0, 10, 0), MinHeight = 26 };
                b.Click += (_, _) => Edit(_ => w.Gravity = g);
                gravity.Children.Add(b);
            }

            Panel.Children.Add(gravity);
            var tracks = new[] { "none", "oval", "eight" };
            var track = new ComboBox { ItemsSource = new[] { "Pist yok", "Oval pist", "Sekiz pist" }, SelectedIndex = Math.Max(0, Array.IndexOf(tracks, w.Track)), Margin = new Thickness(0, 0, 0, 8) };
            track.SelectionChanged += (_, _) => Edit(_ => w.Track = tracks[track.SelectedIndex]);
            Panel.Children.Add(track);
            var walls = new CheckBox { Content = "Kenar duvarları", Foreground = (Brush)FindResource("B.Text"), IsChecked = w.Walls, Margin = new Thickness(0, 0, 0, 8) };
            walls.Click += (_, _) => Edit(_ => w.Walls = walls.IsChecked == true);
            Panel.Children.Add(walls);
            Panel.Children.Add(Caption($"Engeller ({w.Obstacles.Count})"));
            for (int i = 0; i < w.Obstacles.Count; i++)
            {
                var o = w.Obstacles[i];
                int index = i;
                var head = new DockPanel { Margin = new Thickness(0, 4, 0, 2) };
                var del = new Button { Content = "Sil", Style = (Style)FindResource("Btn.Ghost"), Padding = new Thickness(8, 0, 8, 0), MinHeight = 22 };
                DockPanel.SetDock(del, Dock.Right);
                del.Click += (_, _) => Edit(_ => w.Obstacles.RemoveAt(index));
                head.Children.Add(del);
                head.Children.Add(new TextBlock { Text = $"Engel {i + 1}", VerticalAlignment = VerticalAlignment.Center });
                Panel.Children.Add(head);
                Panel.Children.Add(Pair(("X (m)", o.X, v => o.X = v), ("Y (m)", o.Y, v => o.Y = v)));
                Panel.Children.Add(Pair(("Boy (m)", o.SizeX, v => o.SizeX = v), ("En (m)", o.SizeY, v => o.SizeY = v)));
                Panel.Children.Add(Pair(("Yükseklik (m)", o.Height, v => o.Height = v), ("—", double.NaN, _ => { })));
            }

            var add = new Button { Content = "Engel ekle", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            add.Click += (_, _) => Edit(_ => w.Obstacles.Add(new WorldObstacle { X = 0.5, Y = 0.3 * (w.Obstacles.Count % 3 - 1) }));
            Panel.Children.Add(add);

            Panel.Children.Add(Caption($"Model engeller ({w.MeshObstacles.Count})"));
            for (int i = 0; i < w.MeshObstacles.Count; i++)
            {
                var o = w.MeshObstacles[i];
                int index = i;
                var head = new DockPanel { Margin = new Thickness(0, 4, 0, 2) };
                var del = new Button { Content = "Sil", Style = (Style)FindResource("Btn.Ghost"), Padding = new Thickness(8, 0, 8, 0), MinHeight = 22 };
                DockPanel.SetDock(del, Dock.Right);
                del.Click += (_, _) => Edit(_ => w.MeshObstacles.RemoveAt(index));
                head.Children.Add(del);
                head.Children.Add(new TextBlock { Text = o.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                Panel.Children.Add(head);
                Panel.Children.Add(Pair(("X (m)", o.X, v => o.X = v), ("Y (m)", o.Y, v => o.Y = v)));
                Panel.Children.Add(Pair(("Dönüş (°)", o.Yaw, v => o.Yaw = v), ("—", double.NaN, _ => { })));
            }

            var addModel = new Button { Content = "3B model engel ekle (STL/OBJ/PLY)…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            addModel.Click += (_, _) =>
            {
                var mesh = PickModel(out string name);
                if (mesh == null) return;
                Edit(_ => w.MeshObstacles.Add(new WorldMeshObstacle { Name = name, Mesh = mesh.ToEmbedded(), X = 0.6, Y = 0 }));
            };
            Panel.Children.Add(addModel);
        }

        private string _modelUnits = "mm";

        /// <summary>Asks for a 3D model file and loads it in metres, Z up.</summary>
        private MeshModel? PickModel(out string name)
        {
            name = string.Empty;
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "3B modeller (*.stl, *.obj, *.ply)|*.stl;*.obj;*.ply|Tüm dosyalar (*.*)|*.*", Title = "3B model seç" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return null;
            try
            {
                var units = _modelUnits switch { "cm" => MeshUnits.Centimeter, "m" => MeshUnits.Meter, "inç" => MeshUnits.Inch, _ => MeshUnits.Millimeter };
                var mesh = MeshModel.Load(dlg.FileName, new MeshLoadOptions { Units = units });
                name = System.IO.Path.GetFileName(dlg.FileName);
                if (mesh.TriangleCount == 0) throw new System.IO.InvalidDataException("Modelde üçgen yok.");
                var size = mesh.Bounds.Max - mesh.Bounds.Min;
                if (size.Length > 5) AddToast("Model", $"Model {size.X:0.##}×{size.Y:0.##}×{size.Z:0.##} m — birim yanlış olabilir (şu an {_modelUnits}).", false);
                return mesh;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is System.IO.InvalidDataException || ex is FormatException || ex is UnauthorizedAccessException)
            {
                AddToast("Model yüklenemedi", ex.Message, true);
                return null;
            }
        }

        /// <summary>Imported chassis model: file, units, material, infill and weighed mass.</summary>
        private void BuildChassisModelSection(AssemblyDocument a)
        {
            Panel.Children.Add(Overline("Gövde modeli (3B)"));
            Panel.Children.Add(Caption(a.ChassisMesh == null
                ? "Kendi gövdenizi yükleyin; kütle, ağırlık merkezi ve atalet gerçek hacimden hesaplanır."
                : $"{a.ChassisMeshName} · {a.ChassisMesh.TriangleCount:N0} üçgen"));
            var units = new ComboBox { ItemsSource = new[] { "mm", "cm", "m", "inç" }, SelectedItem = _modelUnits, Width = 70, Margin = new Thickness(0, 0, 8, 0) };
            units.SelectionChanged += (_, _) => _modelUnits = (string)units.SelectedItem;
            var load = new Button { Content = a.ChassisMesh == null ? "Model yükle…" : "Değiştir…" };
            load.Click += (_, _) =>
            {
                var mesh = PickModel(out string name);
                if (mesh == null) return;
                // Centre on the chassis and put the model's top at the deck so boards sit on it.
                var b = mesh.Bounds;
                mesh.Translate(new Vec3(-(b.Min.X + b.Max.X) / 2, -(b.Min.Y + b.Max.Y) / 2, a.DeckZ - b.Max.Z));
                Edit(x =>
                {
                    x.ChassisMesh = mesh.ToEmbedded();
                    x.ChassisMeshName = name;
                    var nb = mesh.Bounds;
                    if (x.Kind == VehicleKind.Rover)
                    {
                        x.Length = Math.Max(0.05, nb.Max.X - nb.Min.X);
                        x.Width = Math.Max(0.05, nb.Max.Y - nb.Min.Y);
                    }
                });
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 8) };
            row.Children.Add(units);
            row.Children.Add(load);
            if (a.ChassisMesh != null)
            {
                var remove = new Button { Content = "Kaldır", Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("Btn.Ghost") };
                remove.Click += (_, _) => Edit(x =>
                {
                    x.ChassisMesh = null;
                    x.ChassisMeshName = string.Empty;
                });
                row.Children.Add(remove);
            }

            Panel.Children.Add(row);
            if (a.ChassisMesh == null) return;
            var materials = new[] { "PLA", "PETG", "ABS", "Naylon", "Akrilik", "MDF", "Kontrplak", "FR4", "Alüminyum", "Çelik", "Karbon" };
            var mat = new ComboBox { ItemsSource = materials, SelectedItem = materials.FirstOrDefault(m => string.Equals(m, a.ChassisMaterial, StringComparison.OrdinalIgnoreCase)) ?? "PLA", Margin = new Thickness(0, 0, 0, 8) };
            mat.SelectionChanged += (_, _) => Edit(x => x.ChassisMaterial = (string)mat.SelectedItem);
            Panel.Children.Add(Caption("Malzeme"));
            Panel.Children.Add(mat);
            Panel.Children.Add(Pair(("Doluluk (%)", a.ChassisInfill * 100, v => a.ChassisInfill = Math.Max(0, Math.Min(100, v)) / 100),
                ("Tartılan kütle (g)", a.ChassisMassOverride.HasValue ? a.ChassisMassOverride.Value * 1000 : double.NaN, v => a.ChassisMassOverride = v > 0 ? v / 1000 : null)));
            var mech = W.Simulation.Mechanics;
            if (mech != null && mech.MassBreakdown.Count > 0)
            {
                Panel.Children.Add(Caption($"Toplam {mech.TotalMass * 1000:0} g · ağırlık merkezi ({mech.CenterOfMass.X * 1000:0}, {mech.CenterOfMass.Y * 1000:0}, {mech.CenterOfMass.Z * 1000:0}) mm"));
                foreach (var (n, m) in mech.MassBreakdown.OrderByDescending(x => x.Mass).Take(8)) Panel.Children.Add(Caption($"  {n}: {m * 1000:0.#} g"));
            }
        }

        /// <summary>Buttons that break things on purpose while the simulation runs.</summary>
        private void BuildFaultSection(AssemblyDocument a)
        {
            Panel.Children.Add(Overline("Arıza deneyleri"));
            Panel.Children.Add(Caption("Simülasyon çalışırken uygulanır; sonuçlar fiziksel modelden gelir."));
            var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };

            Button Fault(string text, string tip, Action<SimulationSession> act)
            {
                var b = new Button { Content = text, ToolTip = tip, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 0, 10, 0), MinHeight = 28 };
                b.Click += (_, _) =>
                {
                    var sim = W.Simulation;
                    if (sim.Compiled == null)
                    {
                        AddToast("Arıza deneyi", "Önce simülasyonu başlatın.", false);
                        return;
                    }

                    act(sim);
                    Focus();
                };
                wrap.Children.Add(b);
                return b;
            }

            foreach (var part in W.Schematic.Parts.Where(p => W.Library.Find(p.PartId)?.Symbol == SymbolKind.Battery))
            {
                string r = part.RefDes;
                Fault($"{r} kısa devre", "Pil uçlarını 50 mΩ ile kısa devre eder (ısınma → termal kaçak)", sim =>
                {
                    if (sim.MainDevice(r) is Battery b)
                    {
                        b.ShortCircuitResistance = b.ShortCircuitResistance > 0 ? 0 : 0.05;
                        AddToast(r, b.ShortCircuitResistance > 0 ? "kısa devre uygulandı" : "kısa devre kaldırıldı", b.ShortCircuitResistance > 0);
                    }
                });
            }

            foreach (var m in a.Motors)
            {
                string r = m.RefDes;
                Fault($"{r} sıkıştır", "Mili kilitler: motor durma akımı çeker ve ısınır", sim =>
                {
                    if (sim.Compiled?.DevicesOf(r).OfType<DcMotor>().FirstOrDefault() is { } motor)
                    {
                        motor.Jammed = !motor.Jammed;
                        AddToast(r, motor.Jammed ? "mil kilitlendi" : "mil serbest", motor.Jammed);
                    }
                });
            }

            if (a.Kind == VehicleKind.Quadcopter)
            {
                for (int i = 0; i < 4; i++)
                {
                    int index = i;
                    Fault($"Pervane {i + 1} kır", "Pervaneyi koparır: itki kaybı ve dengesizlik", sim => sim.Mechanics?.BreakProp(index));
                }
            }

            Fault("Tümünü onar", "Yanan parçaları onarır ve arızaları kaldırır", sim =>
            {
                foreach (var d in sim.Compiled!.Circuit.Devices)
                {
                    if (d is Battery b) b.ShortCircuitResistance = 0;
                    if (d is DcMotor dm) dm.Jammed = false;
                }

                sim.RepairAll();
                _failedAt.Clear();
                _onFire.Clear();
                Effects.Clear();
                AddToast("Arıza deneyi", "Tüm parçalar onarıldı", false);
            });
            Panel.Children.Add(wrap);
        }

        private Grid Pair((string Label, double Value, Action<double> Set) left, (string Label, double Value, Action<double> Set) right)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            void Add((string Label, double Value, Action<double> Set) f, int col)
            {
                if (f.Label == "—") return;
                var sp = new StackPanel();
                sp.Children.Add(Caption(f.Label));
                var box = new TextBox { Text = double.IsNaN(f.Value) ? string.Empty : f.Value.ToString("0.###", CultureInfo.CurrentCulture) };
                bool committed = false;
                void Commit()
                {
                    if (committed) return;
                    string text = box.Text.Trim().Replace(',', '.');
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    {
                        if (Math.Abs(v - f.Value) > 1e-9 || double.IsNaN(f.Value))
                        {
                            committed = true;
                            Edit(_ => f.Set(v));
                        }
                    }
                }

                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter) Commit();
                };
                box.LostKeyboardFocus += (_, _) => Commit();
                sp.Children.Add(box);
                Grid.SetColumn(sp, col);
                grid.Children.Add(sp);
            }

            Add(left, 0);
            Add(right, 2);
            return grid;
        }
    }
}
