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
    /// [kin] Kinematics of an arm the user built (servos carrying links): the chain from its base to the selected part,
    /// the servo angles and the tip live, the load on each servo, inverse kinematics to a target typed in or clicked in
    /// the scene, a preview of the pose before running, driving the servos there while running (as a servo tester on
    /// the signal wire), the angles as code, and the reach drawn in the scene. CoreSim Robotics/WorldArm.cs does the work.
    /// </summary>
    public partial class WorldPage
    {
        private WorldArm? _arm;
        private string? _armTip;
        private Vec3? _armTarget;
        private WorldArmSolution? _armSolution;
        private WorldArmReach? _armReach;
        private bool _armShowReach;
        private bool _armBuiltRunning;
        private TextBlock? _armLive;
        private TextBlock? _armResult;

        /// <summary>The selected part's arm, when servos move it (a link, a gripper, a servo carried by another servo).</summary>
        private void KinematicsSection(WorldDocument world, WorldObject o)
        {
            _armLive = null;
            _armResult = null;
            var arm = WorldArm.Build(world, Scene.GeometryOf, o.Id);
            if (arm == null || arm.ActuatedCount == 0 || (arm.Axes.Count < 2 && o.Kind == WorldObjectKind.Component))
            {
                ForgetArm();
                return;
            }

            if (_armTip != o.Id)
            {
                ForgetArm();
                _armTip = o.Id;
            }

            _arm = arm;
            _armReach = null;
            _armBuiltRunning = _session.State != SimState.Stopped;
            Inspector.Children.Add(Caption("Kinematik (kolun ucu)"));
            Inspector.Children.Add(Note("Zincir: " + string.Join(" → ", arm.Path.Select(id => world.Find(id)?.Name ?? id)) + ". Uç noktası parçanın en uzak yüzünün ortası."));
            _armLive = new TextBlock { FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("B.TextSecondary"), Margin = new Thickness(0, 0, 0, 6) };
            Inspector.Children.Add(_armLive);

            // Ters kinematik: a target typed in or clicked in the scene.
            var q = ArmQ();
            var target = _armTarget ?? arm.Tip(q);
            Inspector.Children.Add(Vector3Row("Hedef nokta (mm) X · Y · Z", target.X * 1000, target.Y * 1000, target.Z * 1000, (x, y, z) =>
            {
                _armTarget = new Vec3(x, y, z) / 1000;
                SolveArm();
            }));
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            row.Children.Add(Small("Çöz", () =>
            {
                _armTarget ??= arm.Tip(ArmQ());
                SolveArm();
            }, "Hedefe ulaşan eksen açılarını bulur (ters kinematik); serbest menteşeler olduğu gibi kalır", primary: true));
            row.Children.Add(Small("Sahnede seç", () =>
            {
                if (Scene.PickingPoint)
                {
                    Scene.CancelPointPick();
                    return;
                }

                var planeZ = (_armTarget ?? arm.Tip(ArmQ())).Z;
                Scene.PickPoint(planeZ, p =>
                {
                    _armTarget = p;
                    BuildInspector();
                    SolveArm();
                });
                Shell.Workspace.Current.Status = $"Sahnede kolun ucunun gideceği yere tıklayın (z = {planeZ * 1000:0.#} mm düzleminde). Vazgeçmek için düğmeye yeniden basın.";
            }, "Sahnede tıkladığınız nokta hedef olur (ucun şu anki yüksekliğinde)"));
            row.Children.Add(Small("Ucu hedef yap", () =>
            {
                _armTarget = arm.Tip(ArmQ());
                BuildInspector();
            }, "Hedefi ucun şu anki yerine getirir"));
            Inspector.Children.Add(row);
            _armResult = Note(string.Empty);
            Inspector.Children.Add(_armResult);

            var act = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            bool running = _session.State != SimState.Stopped && _session.Physics != null;
            bool servos = arm.Axes.Any(a => a.Kind == WorldAxisKind.Servo), motors = arm.Axes.Any(a => a.Motor);   // [drive]
            if (!running)
            {
                act.Children.Add(Small("Önizle", PreviewArm, "Eksenleri çözülen açılara çevirip kolu sahnede gösterir (dünya çalışmadan)"));
                act.Children.Add(Small("Kurulduğu gibi", () =>
                {
                    Scene.Preview(null);
                    UpdateArmLive();
                }, servos ? "Önizlemeyi kapatır: servolar 90°" : "Önizlemeyi kapatır: kol kurulduğu duruşta"));
                if (motors) act.Children.Add(Small("Motor hedefi yap", KeepArmTargets, "Çözümü eklem motorlarının hedefi olarak kaydeder: dünya çalışınca kol oraya gider (geri alınabilir)"));
            }
            else
            {
                act.Children.Add(Small(servos ? "Servoları sür" : "Eksenleri sür", DriveArm, servos
                    ? "Çalışırken servolara çözülen açıları verir: sinyal kablosuna servo test cihazı takmak gibi, yazılımın darbeleri yerine geçer. Servo kendi hızı, torku ve beslemesiyle döner."
                    : "Çalışırken eklem motorlarına çözülen değerleri verir; her eksen kendi tork ve hız sınırıyla gider."));
                if (servos) act.Children.Add(Small("Yazılıma bırak", ReleaseArm, "Servoların komutunu yeniden yazılıma bırakır"));
            }

            if (servos) act.Children.Add(Small("Açıları kopyala", CopyArmAngles, "Çözülen servo açılarını Arduino koduna yapıştırılacak sabitler olarak panoya kopyalar"));
            Inspector.Children.Add(act);

            var reach = new CheckBox { Content = "Erişim alanını sahnede göster", IsChecked = _armShowReach, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 2, 0, 6), ToolTip = "Servoların bütün açılarında ucun gidebildiği noktalar; sarı çizgi kolun eksenlerden uca iskeleti, turuncu hedef, yeşil ulaşılan nokta" };
            reach.Click += (_, _) =>
            {
                _armShowReach = reach.IsChecked == true;
                DrawArm();
                UpdateArmLive();
            };
            Inspector.Children.Add(reach);
            ShowArmResult();
            UpdateArmLive();
            DrawArm();
        }

        /// <summary>The axis values now: the physics' while it runs, the preview's (or as built) while stopped.</summary>
        private double[] ArmQ()
        {
            if (_arm == null) return Array.Empty<double>();
            if (_session.State != SimState.Stopped && _session.Physics != null) return _arm.Measure(_session.Physics);
            var q = new double[_arm.Axes.Count];
            if (Scene.PreviewAngles is { } preview)
            {
                for (int i = 0; i < q.Length; i++)
                {
                    // The preview turns horns by their own angle (and motorised joints by theirs, keyed by the joint): the
                    // axis' value whichever way the chain runs through it.
                    var ax = _arm.Axes[i];
                    if (ax.JointId == null && preview.TryGetValue(ax.ObjectId, out double a)) q[i] = a;
                    else if (ax.JointId != null && preview.TryGetValue(ax.JointId, out double b)) q[i] = b;
                }
            }

            return q;
        }

        /// <summary>A run started or stopped: the preview gives way to the physics, the buttons change (preview ↔ drive).</summary>
        private void ArmRunStateChanged()
        {
            bool running = _session.State != SimState.Stopped;
            if (running && Scene.PreviewAngles != null) Scene.Preview(null);
            if (_arm != null && running != _armBuiltRunning) BuildInspector();
        }

        private void ForgetArm()
        {
            if (_armTip != null) Scene.Preview(null);
            _arm = null;
            _armTip = null;
            _armTarget = null;
            _armSolution = null;
            _armReach = null;
            Scene.CancelPointPick();
            Scene.ShowArm(null, Transform3.Identity, null, null, null);
        }

        private void SolveArm()
        {
            if (_arm == null || _armTarget is not { } target) return;
            _armSolution = _arm.Solve(target, ArmQ());
            ShowArmResult();
            DrawArm();
        }

        private void ShowArmResult()
        {
            if (_armResult == null || _arm == null) return;
            var s = _armSolution;
            if (s == null)
            {
                _armResult.Text = "Hedefi yazın ya da sahnede seçin, sonra Çöz'e basın.";
                return;
            }

            var sb = new StringBuilder();
            sb.Append(s.Success ? "Çözüm: " : "En yakın çözüm: ");
            sb.Append(string.Join(" · ", _arm.Axes.Select((a, i) => a.Actuated ? $"{a.Name} {(Math.Round(a.Display(s.Q[i]), 1) + 0.0).ToString("0.#", CultureInfo.CurrentCulture)}{a.Unit}" : null).Where(x => x != null)));
            if (s.Message.Length > 0) sb.Append(". ").Append(s.Message.TrimEnd('.'));
            if (s.Success && s.Dexterity < 0.1) sb.Append(". Tekil konuma yakın: kol gergin ya da katlanmış, bu yönlerde küçük hareketler büyük açı ister.");
            var load = _arm.GravityLoad(s.Q);
            var heavy = _arm.Axes.Select((a, i) => (a, i)).Where(x => x.a.Actuated && x.a.StallTorque > 0 && Math.Abs(load[x.i]) > 0.5 * x.a.StallTorque).ToList();
            foreach (var (a, i) in heavy) sb.Append($". {a.Name} bu konumda tutma torkunun %{Math.Abs(load[i]) / a.StallTorque * 100:0}'ini harcar");
            _armResult.Text = Shell.Lang.T(sb.ToString());   // [lang]
        }

        private void PreviewArm()
        {
            if (_arm == null) return;
            if (_armSolution == null) SolveArm();
            if (_armSolution == null) return;
            var angles = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _arm.Axes.Count; i++)
            {
                var a = _arm.Axes[i];
                if (a.JointId == null) angles[a.ObjectId] = _armSolution.Q[i];
                else if (a.Motor) angles[a.JointId] = _armSolution.Q[i];   // [drive] a motorised joint, by its id
            }

            Scene.Preview(angles);
            UpdateArmLive();
            DrawArm();
        }

        private void DriveArm()
        {
            if (_arm == null || _session.Physics is not { } physics) return;
            if (_armSolution == null) SolveArm();
            if (_armSolution == null) return;
            int servos = 0, motors = 0;
            for (int i = 0; i < _arm.Axes.Count; i++)
            {
                var a = _arm.Axes[i];
                if (a.Motor && a.JointId != null)
                {
                    physics.SetJointTarget(a.JointId, MotorValue(a, _armSolution.Q[i]));   // [drive] the joint's motor goes there
                    motors++;
                }
                else if (a.Kind == WorldAxisKind.Servo)
                {
                    physics.HoldServo(a.ObjectId, WorldArmAxis.ServoDegrees(_armSolution.Q[i]));
                    servos++;
                }
            }

            Shell.Workspace.Current.Status = servos > 0
                ? $"{servos} servo çözülen açılara sürülüyor; yazılımın komutu yerine geçti. 'Yazılıma bırak' ile geri verin."
                : $"{motors} motorlu eksen çözülen değerlere gidiyor.";
        }

        /// <summary>[drive] An axis value (rad / m) as its joint motor's target: degrees for a hinge, metres for a slider.</summary>
        private static double MotorValue(WorldArmAxis a, double q) => a.Kind == WorldAxisKind.Hinge ? q * 180 / Math.PI : q;

        /// <summary>[drive] The solution as the motors' targets in the document: where the robot goes when the world runs.</summary>
        private void KeepArmTargets()
        {
            var world = World;
            if (_arm == null || world == null) return;
            if (_armSolution == null) SolveArm();
            if (_armSolution is not { } s) return;
            int n = 0;
            Edit("Motor hedefleri çözümden", () =>
            {
                for (int i = 0; i < _arm.Axes.Count; i++)
                {
                    var a = _arm.Axes[i];
                    if (!a.Motor || world.Joints.FirstOrDefault(j => j.Id == a.JointId) is not { } joint) continue;
                    joint.DriveTarget = MotorValue(a, s.Q[i]);
                    n++;
                }
            });
            Shell.Workspace.Current.Status = $"{n} motorun hedefi çözüme ayarlandı: dünya çalışınca kol oraya gider.";
        }

        private void ReleaseArm()
        {
            if (_arm == null || _session.Physics is not { } physics) return;
            foreach (var a in _arm.Axes.Where(a => a.Kind == WorldAxisKind.Servo)) physics.HoldServo(a.ObjectId, null);
            Shell.Workspace.Current.Status = "Servolar yeniden yazılımın komutunda.";
        }

        private void CopyArmAngles()
        {
            var world = World;
            if (_arm == null || world == null) return;
            if (_armSolution == null) SolveArm();
            var s = _armSolution;
            if (s == null) return;
            var target = _armTarget ?? s.Reached;
            var sb = new StringBuilder();
            sb.AppendLine($"// RobotWin Studio · ters kinematik: {world.Find(_arm.TipId)?.Name} ucu → ({MmCode(target.X)}, {MmCode(target.Y)}, {MmCode(target.Z)}) mm" + (s.Success ? string.Empty : " (en yakın nokta, " + (s.Error * 1000).ToString("0.#", CultureInfo.InvariantCulture) + " mm uzakta)"));
            for (int i = 0; i < _arm.Axes.Count; i++)
            {
                var a = _arm.Axes[i];
                if (a.Kind != WorldAxisKind.Servo) continue;   // a motorised joint is not in the board's code
                double deg = WorldArmAxis.ServoDegrees(s.Q[i]);
                string pin = SignalPin(world, a.ObjectId);
                sb.AppendLine($"const int {Identifier(a.Name)} = {Math.Round(deg).ToString(CultureInfo.InvariantCulture)};   // {deg.ToString("0.0", CultureInfo.InvariantCulture)}°{(pin.Length > 0 ? " · " + pin : string.Empty)}");
            }

            try
            {
                Clipboard.SetText(sb.ToString());
                Shell.Workspace.Current.Status = "Servo açıları panoya kopyalandı: kodunuza yapıştırıp servo.write(...) ile kullanın.";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Shell.Workspace.Current.Status = "Pano şu an kullanılamıyor; yeniden deneyin.";
            }
        }

        private static string Mm(double m) => (Math.Round(m * 1000, 1) + 0.0).ToString("0.#", CultureInfo.CurrentCulture);

        /// <summary>Millimetres for code (a dot, whatever the language).</summary>
        private static string MmCode(double m) => (m * 1000).ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>The board pin a servo's signal wire goes to ("D9"), or "".</summary>
        private static string SignalPin(WorldDocument world, string servoId)
        {
            foreach (var c in world.Cables)
            {
                if (c.A.ObjectId == servoId && c.A.Pin == "SIG") return c.B.Pin;
                if (c.B.ObjectId == servoId && c.B.Pin == "SIG") return c.A.Pin;
            }

            return string.Empty;
        }

        /// <summary>A C identifier from a part's name: "Omuz servosu" → "OMUZ_SERVOSU".</summary>
        private static string Identifier(string name)
        {
            var sb = new StringBuilder();
            foreach (char ch in name.ToUpperInvariant())
            {
                char c = ch switch { 'Ç' => 'C', 'Ğ' => 'G', 'İ' => 'I', 'I' => 'I', 'Ö' => 'O', 'Ş' => 'S', 'Ü' => 'U', _ => ch };
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) sb.Append(c);
                else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
            }

            string id = sb.ToString().Trim('_');
            if (id.Length == 0 || char.IsDigit(id[0])) id = "SERVO_" + id;
            return id;
        }

        /// <summary>The reach, the arm's skeleton, the target and where it gets, over the scene.</summary>
        private void DrawArm()
        {
            if (_arm == null)
            {
                Scene.ShowArm(null, Transform3.Identity, null, null, null);
                return;
            }

            var physics = _session.State != SimState.Stopped ? _session.Physics : null;
            var motion = _arm.BaseMotion(physics);
            var q = ArmQ();
            if (_armShowReach && _armReach == null) _armReach = _arm.Reach(q, 1600);
            var moves = ArmKinematics.LinkTransforms(_arm.Model, q);
            var skeleton = new List<Vec3>();
            for (int i = 0; i < _arm.Axes.Count; i++) skeleton.Add(motion.Apply((i == 0 ? Transform3.Identity : moves[i - 1]).Apply(_arm.Axes[i].Spec.Point)));
            skeleton.Add(motion.Apply(_arm.Tip(q)));
            Vec3? target = _armTarget is { } t ? motion.Apply(t) : null;
            Vec3? reached = _armSolution != null && !_armSolution.Success ? motion.Apply(_armSolution.Reached) : null;
            Scene.ShowArm(_armShowReach ? _armReach?.Points : null, motion, skeleton, target, reached);
        }

        /// <summary>The servo angles, the tip (model and physics), the load and how far from singular, live.</summary>
        private void UpdateArmLive()
        {
            if (_armLive == null || _arm == null) return;
            var physics = _session.State != SimState.Stopped ? _session.Physics : null;
            var q = ArmQ();
            var load = _arm.GravityLoad(q);
            var sb = new StringBuilder();
            int width = Math.Min(16, _arm.Axes.Max(a => a.Name.Length));
            for (int i = 0; i < _arm.Axes.Count; i++)
            {
                var a = _arm.Axes[i];
                string name = a.Name.Length > width ? a.Name.Substring(0, width) : a.Name.PadRight(width);
                string value = (Math.Round(a.Display(q[i]), 1) + 0.0).ToString("0.0", CultureInfo.CurrentCulture).PadLeft(6) + a.Unit;
                string extra = a.Actuated && a.StallTorque > 0
                    ? " " + Shell.Lang.T("yük") + " " + Shell.Lang.Percent(Math.Abs(load[i]) / a.StallTorque * 100, "0")   // [lang]
                    : " " + Shell.Lang.T(a.Kind is WorldAxisKind.Hinge or WorldAxisKind.Slider ? "serbest" : "motor mili");   // not "mil": that is also the PCB unit
                // [drive] A motorised joint: where its motor is told to go.
                if (a.Motor && a.JointId != null && physics?.JointDriveState(a.JointId) is { } drive)
                    extra += " → " + (a.Kind == WorldAxisKind.Hinge ? drive.Target : drive.Target * 1000).ToString("0.0", CultureInfo.CurrentCulture) + (a.Kind == WorldAxisKind.Hinge ? "°" : " mm");
                if (physics != null && physics.HeldServos.Contains(a.ObjectId)) extra += " (" + Shell.Lang.T("panel") + ")";
                sb.AppendLine($"{name} {value}{extra}");
            }

            var model = _arm.BaseMotion(physics).Apply(_arm.Tip(q));
            sb.Append($"Uç (model) {Mm(model.X)} · {Mm(model.Y)} · {Mm(model.Z)} mm");
            if (_arm.MeasuredTip(physics) is { } measured)
            {
                sb.AppendLine();
                sb.AppendLine($"Uç (fizik) {Mm(measured.X)} · {Mm(measured.Y)} · {Mm(measured.Z)} mm");
                sb.Append($"Model ile fizik farkı {(measured - model).Length * 1000:0.00} mm");
            }

            double dex = _arm.Dexterity(q);
            sb.AppendLine();
            sb.Append($"Tekilliğe uzaklık %{dex * 100:0}" + (dex < 0.1 ? " (kol gergin ya da katlanmış)" : string.Empty));
            if (_armShowReach)
            {
                _armReach ??= _arm.Reach(q, 1600);
                sb.AppendLine();
                sb.Append($"Erişim {_armReach.Nearest * 1000:0}–{_armReach.Farthest * 1000:0} mm" + (_armReach.Planar ? ", tek düzlemde" : string.Empty));
            }

            _armLive.Text = Shell.Lang.T(sb.ToString());   // [lang] line by line
            if (physics != null) DrawArm();
        }

        /// <summary>
        /// [kin] Snapshot options: --arm "Kol 2" selects an arm's tip, --arm-target x,y,z (mm) solves for it, --arm-preview
        /// turns the horns there, --arm-reach draws the reach, --arm-drive drives the servos there for a second and a half
        /// of a run (with --simulate).
        /// </summary>
        private void KinematicsSnapshot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--arm");
            if (i < 0 || i + 1 >= args.Length || World?.Objects.FirstOrDefault(o => NamedLike(o, args[i + 1])) is not { } tip) return;
            Scene.Select(tip.Id);
            BuildInspector();
            if (_arm == null) return;
            _armShowReach = args.Contains("--arm-reach");
            int t = Array.IndexOf(args, "--arm-target");
            if (t >= 0 && t + 1 < args.Length)
            {
                var v = args[t + 1].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture) / 1000).ToArray();
                _armTarget = new Vec3(v[0], v[1], v[2]);
                SolveArm();
            }

            if (args.Contains("--arm-preview")) PreviewArm();
            if (args.Contains("--arm-drive") && _session.State != SimState.Stopped)
            {
                DriveArm();
                for (int k = 0; k < 150; k++) _session.StepOnce(0.01);
                OnSimUpdated();
            }

            BuildInspector();
        }
    }
}
