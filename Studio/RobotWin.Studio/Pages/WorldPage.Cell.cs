using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [arm] Robot cell on the world page: the "Robot hücresi" palette, the teach pendant of an
    /// arm (run/pause/step/stop, speed override, joint and Cartesian jog, teaching points,
    /// program editor), tool/conveyor/light-curtain panels and their live readouts.
    /// WorldPage.xaml.cs only calls <see cref="CellPalette"/>, <see cref="CellSection"/> and <see cref="UpdateCellLive"/>.
    /// </summary>
    public partial class WorldPage
    {
        private TextBlock? _cellStatus;
        private string? _cellObject;
        private double _jogStep = 5;


        // ================================================================ palette

        private void CellPalette()
        {
            Palette.Children.Add(Caption("Robot hücresi"));
            Palette.Children.Add(Note("Endüstriyel kollar, tutucular, konveyör, ışık perdesi, palet ve çit. Bir kol seçiliyken eklenen tutucu/vantuz otomatik olarak kolun flanşına takılır."));
            foreach (var p in Lib.Presets.Where(x => x.Category == CellParts.Category))
            {
                var id = p.Id;
                Palette.Children.Add(Row(p.Name, p.Description, () => AddCellPart(id)));
            }

            Palette.Children.Add(Action("Hazır şablon: Fabrika hücresi", () => _main.NewWorld(FactoryCell.Build()),
                "Konveyör + 6 eksenli kol + vakum + palet + ışık perdesi + çit, programı yüklü ve kablolu.", primary: false));
        }

        private void AddCellPart(string presetId)
        {
            EnsureWorld();
            var world = World!;
            var preset = Lib.FindPreset(presetId);
            if (preset == null) return;
            var selected = world.Find(Scene.Selected);
            WorldObject? added = null;
            Edit("Eklendi", () =>
            {
                var pose = NewPose();
                if (preset.PartId == CellParts.ArmId && selected?.PartId == CellParts.PedestalId)
                {
                    // On top of the selected pedestal.
                    var h = CellGeometry.For(selected)!;
                    pose = new WorldPose { X = selected.Pose.X, Y = selected.Pose.Y, Z = selected.Pose.Z + h.Max.Z, Yaw = selected.Pose.Yaw };
                }

                added = world.AddComponent(Lib, presetId, pose);
                added.Name = Shell.Lang.T(added.Name);   // [lang]
                added.IsStatic = !CellParts.IsTool(preset.PartId);
                if (CellParts.IsTool(preset.PartId) && selected?.PartId == CellParts.ArmId)
                {
                    world.Joints.RemoveAll(j => j.ObjectB == selected.Id && j.PortB == "flange");
                    world.Joints.Add(new WorldJoint { Id = world.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = added.Id, ObjectB = selected.Id, PortB = "flange" });
                    CellGeometry.SeatTools(world, selected);
                }
            });
            if (added != null)
            {
                Scene.Select(added.Id);
                W.Status = $"{added.Name} eklendi";
            }
        }

        // ================================================================ inspector

        private void CellSection(WorldDocument world, WorldObject o)
        {
            _cellStatus = null;
            _cellObject = null;
            if (o.Kind != WorldObjectKind.Component || !CellParts.IsCellPart(o.PartId)) return;
            _cellObject = o.Id;
            switch (o.PartId)
            {
                case CellParts.ArmId:
                    ArmPendant(world, o);
                    break;
                case CellParts.GripperId:
                case CellParts.VacuumId:
                    ToolPanel(world, o);
                    break;
                case CellParts.ConveyorId:
                    ConveyorPanel(o);
                    break;
                case CellParts.CurtainId:
                    CurtainPanel(o);
                    break;
                default:
                    return;
            }

            UpdateCellLive();
        }

        private TextBlock StatusBlock()
        {
            var t = new TextBlock
            {
                FontFamily = (FontFamily)FindResource("F.Mono"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("B.Text"),
                Margin = new Thickness(0, 2, 0, 8)
            };
            var border = new Border
            {
                Background = (Brush)FindResource("B.Raised"),
                BorderBrush = (Brush)FindResource("B.Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 2),
                Margin = new Thickness(0, 0, 0, 8),
                Child = t
            };
            Inspector.Children.Add(border);
            _cellStatus = t;
            return t;
        }

        private Button Small(string text, Action click, string? tip = null, bool primary = false)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 3, 8, 3), ToolTip = tip, MinWidth = 34 };
            if (primary) b.Style = (Style)FindResource("Btn.Primary");
            b.Click += (_, _) => click();
            return b;
        }

        private MachineArm? LiveArm(string id) => _session.State == SimState.Stopped ? null : _session.Physics?.ArmOf(id);

        private void ArmPendant(WorldDocument world, WorldObject o)
        {
            var model = CellGeometry.ModelOf(o);
            Inspector.Children.Add(Caption("Öğretme paneli"));
            Inspector.Children.Add(Note($"{model.Name}: {model.Description}. Erişim {model.Reach * 1000:0} mm, anma yükü {model.RatedPayload:0.##} kg."));
            StatusBlock();

            // ---- tool on the flange
            var mount = world.Joints.FirstOrDefault(j => j.ObjectB == o.Id && j.PortB == "flange");
            var tool = mount != null ? world.Find(mount.ObjectA) : null;
            if (tool == null)
            {
                Inspector.Children.Add(Note("Flanşta takım yok. Paletten bir tutucu veya vantuz ekleyin (kol seçiliyken eklenen takım flanşa takılır)."));
                foreach (var t in world.Objects.Where(x => CellParts.IsTool(x.PartId) && !world.Joints.Any(j => j.ObjectA == x.Id)))
                {
                    var tt = t;
                    Inspector.Children.Add(Action($"'{t.Name}' takımını flanşa tak", () => Edit($"{tt.Name} flanşa takıldı", () =>
                    {
                        world.Joints.Add(new WorldJoint { Id = world.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = tt.Id, ObjectB = o.Id, PortB = "flange" });
                        CellGeometry.SeatTools(world, o);
                    })));
                }
            }

            // ---- program control
            var run = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            run.Children.Add(Small("▶ Başlat", () =>
            {
                if (_session.State == SimState.Stopped) Run();
                var a = LiveArm(o.Id);
                if (a == null) return;
                if (a.Controller.State == ArmState.Paused || a.Controller.State == ArmState.SafetyStop) a.Controller.Resume();
                else a.Controller.Start();
                UpdateCellLive();
            }, "Programı başlatır / devam ettirir (dünya durmuşsa önce dünyayı çalıştırır)", true));
            run.Children.Add(Small("⏸ Duraklat", () => LiveArm(o.Id)?.Controller.Pause(), "Yolda yavaşlayarak durur; Başlat ile devam eder"));
            run.Children.Add(Small("Adım", () =>
            {
                if (_session.State == SimState.Stopped) Run();
                LiveArm(o.Id)?.Controller.Step();
            }, "Bir program satırını çalıştırıp durur"));
            run.Children.Add(Small("■ Durdur", () => LiveArm(o.Id)?.Controller.Stop(), "Programı durdurur, ilk satıra döner"));
            run.Children.Add(Small("Sıfırla", () => LiveArm(o.Id)?.Controller.Reset(), "Arızayı temizler"));
            Inspector.Children.Add(run);

            var loop = new CheckBox { Content = "Döngü (program bitince baştan)", IsChecked = CellParts.Flag(ParamsOf(o), "loop", true), Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 2, 0, 6) };
            loop.Click += (_, _) =>
            {
                bool v = loop.IsChecked == true;
                var a = LiveArm(o.Id);
                if (a != null) a.Controller.Loop = v;
                else Edit("Döngü değişti", () => o.Parameters["loop"] = v ? "true" : "false");
            };
            Inspector.Children.Add(loop);

            var speed = new Slider { Minimum = 1, Maximum = 100, Value = CellParts.Num(ParamsOf(o), "override", 100), TickFrequency = 5, IsSnapToTickEnabled = true, Margin = new Thickness(0, 0, 0, 2) };
            var speedText = new TextBlock { Text = $"Hız ayarı: %{speed.Value:0}", Foreground = (Brush)FindResource("B.TextSecondary") };
            speed.ValueChanged += (_, _) =>
            {
                speedText.Text = $"Hız ayarı: %{speed.Value:0}";
                var a = LiveArm(o.Id);
                if (a != null) a.Controller.Override = speed.Value / 100;
            };
            speed.PreviewMouseUp += (_, _) =>
            {
                if (LiveArm(o.Id) == null) Edit("Hız ayarı değişti", () => o.Parameters["override"] = speed.Value.ToString("0", Inv));
            };
            Inspector.Children.Add(speedText);
            Inspector.Children.Add(speed);

            // ---- jog
            Inspector.Children.Add(Caption("Elle hareket (jog)"));
            Inspector.Children.Add(Note(_session.State == SimState.Stopped
                ? "Dünya dururken jog başlangıç pozunu değiştirir (q0). Çalışırken kol gerçek sürücülerle hareket eder."
                : "Çalışırken jog kolu gerçek sürücüleriyle hareket ettirir; program çalışıyorsa önce durdurun."));
            var step = new ComboBox { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var (label, value) in new[] { ("İnce · 1° / 1 mm", 1.0), ("Orta · 5° / 10 mm", 5.0), ("Kaba · 15° / 50 mm", 15.0) })
            {
                step.Items.Add(new ComboBoxItem { Content = label, Tag = value, IsSelected = Math.Abs(value - _jogStep) < 1e-9 });
            }

            step.SelectionChanged += (_, _) =>
            {
                if (step.SelectedItem is ComboBoxItem it) _jogStep = (double)it.Tag;
            };
            Inspector.Children.Add(Labeled("Adım", step));
            var joints = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 4) };
            for (int i = 0; i < model.Dof; i++)
            {
                int k = i;
                var spec = model.Joints[i];
                var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 4, 4) };
                cell.Children.Add(new TextBlock { Text = spec.Name, Width = 22, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("B.TextSecondary") });
                cell.Children.Add(Small("−", () => JogJoint(o, k, -1), $"{spec.Name} eksi yönde"));
                cell.Children.Add(Small("+", () => JogJoint(o, k, +1), $"{spec.Name} artı yönde"));
                joints.Children.Add(cell);
            }

            Inspector.Children.Add(joints);
            var cart = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var (label, d, rz) in new[]
            {
                ("X−", new Vec3(-1, 0, 0), 0.0), ("X+", new Vec3(1, 0, 0), 0.0), ("Y−", new Vec3(0, -1, 0), 0.0), ("Y+", new Vec3(0, 1, 0), 0.0),
                ("Z−", new Vec3(0, 0, -1), 0.0), ("Z+", new Vec3(0, 0, 1), 0.0), ("Rz−", Vec3.Zero, -1.0), ("Rz+", Vec3.Zero, 1.0)
            })
            {
                var dd = d;
                double rr = rz;
                cart.Children.Add(Small(label, () => JogCartesian(o, dd, rr), "TCP'yi robot taban ekseninde doğrusal hareket ettirir"));
            }

            Inspector.Children.Add(cart);
            var toolRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            toolRow.Children.Add(Small("Kavra / vakum aç", () => LiveArm(o.Id)?.Controller.SetTool(true)));
            toolRow.Children.Add(Small("Bırak", () => LiveArm(o.Id)?.Controller.SetTool(false)));
            toolRow.Children.Add(Small("Başlangıç pozuna dön", () =>
            {
                var a = LiveArm(o.Id);
                a?.Controller.MoveJ(CellGeometry.StartJoints(o), 0.3);
            }));
            Inspector.Children.Add(toolRow);

            // ---- teach points
            Inspector.Children.Add(Caption("Nokta öğret"));
            o.Parameters.TryGetValue("points", out var pointsText);
            var name = new TextBox { Text = ArmProgram.NextPointName(pointsText), Margin = new Thickness(0, 0, 0, 4) };
            Inspector.Children.Add(Labeled("Nokta adı", name));
            var teach = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            teach.Children.Add(Small("Eksen konumunu kaydet", () => TeachPoint(o, name.Text, false), "J(...) : eksen açıları"));
            teach.Children.Add(Small("TCP'yi kaydet (kartezyen)", () => TeachPoint(o, name.Text, true), "C(x, y, z, rx, ry, rz) : taban ekseninde mm ve derece"));
            teach.Children.Add(Small("Noktaya git", () => GoToPoint(o, name.Text), "Seçili noktaya eksen hareketiyle gider"));
            Inspector.Children.Add(teach);

            // ---- program
            Inspector.Children.Add(Caption("Program"));
            var points = Editor(pointsText ?? string.Empty, 90);
            o.Parameters.TryGetValue("program", out var programText);
            var program = Editor(programText ?? string.Empty, 200);
            Inspector.Children.Add(new TextBlock { Text = "Noktalar", Foreground = (Brush)FindResource("B.TextSecondary"), Margin = new Thickness(0, 0, 0, 2) });
            Inspector.Children.Add(points);
            Inspector.Children.Add(new TextBlock { Text = "Komutlar", Foreground = (Brush)FindResource("B.TextSecondary"), Margin = new Thickness(0, 6, 0, 2) });
            Inspector.Children.Add(program);
            var check = Note(string.Empty);
            void Validate()
            {
                var parsed = ArmProgram.Parse(program.Text, points.Text, model);
                check.Text = parsed.Errors.Count == 0
                    ? $"Program geçerli: {parsed.Instructions.Count} komut, {parsed.Points.Count} nokta."
                    : string.Join("\n", parsed.Errors.Take(6));
                check.Foreground = (Brush)FindResource(parsed.Errors.Count == 0 ? "B.Success" : "B.Danger");
            }

            Validate();
            var progRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            progRow.Children.Add(Small("Programı kaydet", () =>
            {
                Edit("Robot programı kaydedildi", () =>
                {
                    o.Parameters["program"] = program.Text.Replace("\r", string.Empty);
                    o.Parameters["points"] = points.Text.Replace("\r", string.Empty);
                });
                var a = LiveArm(o.Id);
                if (a != null && a.Controller.State != ArmState.Running) a.Controller.Load(ArmProgram.Parse(program.Text, points.Text, model));
            }, "Dünya çalışıyorsa program kontrolöre de yüklenir (çalışmıyorken)", true));
            progRow.Children.Add(Small("Doğrula", Validate));
            Inspector.Children.Add(progRow);
            Inspector.Children.Add(check);
            Inspector.Children.Add(Note("Komutlar: MOVEJ nokta [HIZ=%] [OFS=x,y,z] [PAL] · MOVEL nokta [HIZ=mm/s] · KAVRA · BIRAK · TUTUSBEKLE s · BEKLE s · GIRISBEKLE n 1/0 · CIKIS n 1/0 · PALET nx ny nz dx dy dz · PALETSONRAKI · HIZ % · SON. " +
                "G/Ç: DI1–DI4, START (yükselen kenar başlatır), STOP (yüksek: duraklat), SAFE (bağlıysa güvenlik devresi); DO1–DO4, BUSY, FAULT. Kart yazılımı bu pinlerle kolu yönetebilir."));
        }

        private TextBox Editor(string text, double height) => new TextBox
        {
            Text = text.Replace("\n", Environment.NewLine),
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = (FontFamily)FindResource("F.Mono"),
            FontSize = 12,
            Height = height,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top
        };

        private static Dictionary<string, string> ParamsOf(WorldObject o)
        {
            var def = o.PartId != null ? Lib.Find(o.PartId) : null;
            if (def == null) return new Dictionary<string, string>(o.Parameters, StringComparer.OrdinalIgnoreCase);
            var preset = o.PresetId != null ? Lib.FindPreset(o.PresetId) : null;
            return Lib.ResolveParameters(def, preset, o.Parameters);
        }

        private void JogJoint(WorldObject o, int joint, double sign)
        {
            var model = CellGeometry.ModelOf(o);
            var spec = model.Joints[joint];
            double delta = spec.IsPrismatic ? sign * Math.Max(1, _jogStep * (_jogStep >= 15 ? 50.0 / 15 : _jogStep >= 5 ? 2 : 1)) / 1000 : sign * _jogStep * Math.PI / 180;
            var a = LiveArm(o.Id);
            if (a != null)
            {
                a.Controller.JogJoint(joint, delta);
                return;
            }

            var q = CellGeometry.StartJoints(o);
            q[joint] = Math.Max(spec.Min, Math.Min(spec.Max, q[joint] + delta));
            SetStartPose(o, q, $"{spec.Name} {spec.ToDisplay(q[joint]):0.#}{spec.Unit}");
        }

        private void JogCartesian(WorldObject o, Vec3 dir, double rz)
        {
            double mm = _jogStep >= 15 ? 50 : _jogStep >= 5 ? 10 : 1;
            var delta = dir * (mm / 1000);
            double rot = rz * _jogStep * Math.PI / 180;
            var a = LiveArm(o.Id);
            if (a != null)
            {
                a.Controller.JogCartesian(delta, rot);
                return;
            }

            var model = CellGeometry.ModelOf(o);
            var tool = ToolTcp(o);
            var q = CellGeometry.StartJoints(o);
            var tcp = ArmKinematics.Forward(model, q, tool);
            var target = new Transform3(tcp.P + delta, rot != 0 ? (tcp.Q * Quat.FromAxisAngle(Vec3.UnitZ, rot)).Normalized() : tcp.Q);
            var ik = ArmKinematics.Inverse(model, target, q, tool);
            if (!ik.Success)
            {
                W.Status = "Jog yapılamadı: " + ik.Message;
                return;
            }

            SetStartPose(o, ik.Joints, $"TCP {target.P.X * 1000:0}, {target.P.Y * 1000:0}, {target.P.Z * 1000:0} mm" + (ik.NearSingular ? " · tekilliğe yakın!" : string.Empty));
        }

        private Transform3 ToolTcp(WorldObject arm)
        {
            var world = World!;
            var mount = world.Joints.FirstOrDefault(j => j.ObjectB == arm.Id && j.PortB == "flange");
            return ToolGeometry.Tcp(mount != null ? world.Find(mount.ObjectA)?.PartId : null);
        }

        private void SetStartPose(WorldObject o, IReadOnlyList<double> q, string status)
        {
            var model = CellGeometry.ModelOf(o);
            Edit("Kol başlangıç pozu: " + status, () =>
            {
                o.Parameters["q0"] = ArmProgram.FormatJoints(model, q);
                CellGeometry.SeatTools(World!, o);
            });
        }

        private void TeachPoint(WorldObject o, string name, bool cartesian)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || name.Contains('=') || name.Contains(' '))
            {
                W.Status = "Geçerli bir nokta adı yazın (boşluksuz)";
                return;
            }

            var model = CellGeometry.ModelOf(o);
            var a = LiveArm(o.Id);
            var q = a != null ? a.Controller.Reference.ToArray() : CellGeometry.StartJoints(o);
            var pt = new ArmPoint { Name = name };
            if (cartesian) pt.Pose = ArmKinematics.Forward(model, q, a?.Controller.Tool ?? ToolTcp(o));
            else pt.Joints = q;
            o.Parameters.TryGetValue("points", out var points);
            Edit($"Nokta {name} kaydedildi", () => o.Parameters["points"] = ArmProgram.SetPoint(points, pt, model));
            W.Status = ArmProgram.FormatPoint(pt, model);
        }

        private void GoToPoint(WorldObject o, string name)
        {
            var a = LiveArm(o.Id);
            if (a == null)
            {
                W.Status = "Noktaya gitmek için dünyayı çalıştırın";
                return;
            }

            o.Parameters.TryGetValue("points", out var points);
            var parsed = ArmProgram.Parse(string.Empty, points, a.Model);
            if (!parsed.Points.TryGetValue(name.Trim(), out var pt))
            {
                W.Status = $"'{name}' noktası yok";
                return;
            }

            if (pt.Joints != null) a.Controller.MoveJ(pt.Joints, 0.3);
            else if (pt.Pose is { } pose)
            {
                var ik = ArmKinematics.Inverse(a.Model, pose, a.Controller.Reference, a.Controller.Tool);
                if (ik.Success) a.Controller.MoveJ(ik.Joints, 0.3);
                else W.Status = "Noktaya gidilemiyor: " + ik.Message;
            }
        }

        private void ToolPanel(WorldDocument world, WorldObject o)
        {
            var p = ParamsOf(o);
            Inspector.Children.Add(Caption(o.PartId == CellParts.GripperId ? "Paralel tutucu" : "Vakum vantuz"));
            var mount = world.Joints.FirstOrDefault(j => j.ObjectA == o.Id && j.PortB == "flange");
            var arm = mount != null ? world.Find(mount.ObjectB) : null;
            if (o.PartId == CellParts.GripperId)
            {
                double force = CellParts.Num(p, "force", 80), stroke = CellParts.Num(p, "stroke", 60);
                Inspector.Children.Add(Note($"Strok {stroke:0} mm, parmak başı {force:0} N. Kauçuk pedle (μ≈0.6–0.75) sürtünmeyle en çok ≈ {2 * force * 0.65 / 9.81:0.#} kg tutar (hızlanma payı hariç)."));
            }
            else
            {
                double d = CellParts.Num(p, "diameter", 50) / 1000, kpa = CellParts.Num(p, "vacuum", 65);
                int cups = (int)Math.Max(1, CellParts.Num(p, "cups", 1));
                double hold = 0.8 * kpa * 1000 * Math.PI * d * d / 4 * cups;
                Inspector.Children.Add(Note($"{cups} × Ø{d * 1000:0} mm, −{kpa:0} kPa: tutma kuvveti ≈ {hold:0} N ({hold / 9.81 / 1.5:0.#} kg, 1.5 güvenlik katsayısıyla). Düz yüzeye dik oturmalı."));
            }

            Inspector.Children.Add(Note(arm != null ? $"'{arm.Name}' flanşına takılı. Komut: robot programındaki KAVRA / BIRAK." : "Bir robot flanşına takılı değil: kolu seçip öğretme panelinden takın."));
            StatusBlock();
        }

        private void ConveyorPanel(WorldObject o)
        {
            Inspector.Children.Add(Caption("Konveyör"));
            Inspector.Children.Add(Note("Bant üstündeki parçaları sürtünmeyle taşır; sonda dayama ve fotosel (SENSOR) vardır. RUN pini kabloluysa bant yalnızca RUN yüksekken döner."));
            StatusBlock();
            var row = new WrapPanel();
            row.Children.Add(Small("Bandı başlat", () => _session.SetConveyor(o.Id, true)));
            row.Children.Add(Small("Bandı durdur", () => _session.SetConveyor(o.Id, false)));
            Inspector.Children.Add(row);
        }

        private void CurtainPanel(WorldObject o)
        {
            Inspector.Children.Add(Caption("Güvenlik ışık perdesi"));
            Inspector.Children.Add(Note("Işınlardan biri kesilince OSSD düşer; SAFE girişine bağlı kol (veya SAFE'i bağlı olmayan bütün kollar) kontrollü durur ve yeniden başlatma bekler."));
            StatusBlock();
            Inspector.Children.Add(Small("Test: perdeye el sok (2 s)", () => _session.InterruptCurtain(o.Id, 2.0), "Bir kişinin perdeden geçmesini taklit eder"));
        }

        // ================================================================ live readout

        private void UpdateCellLive()
        {
            if (_cellStatus == null || _cellObject == null) return;
            var world = World;
            var o = world?.Find(_cellObject);
            if (o == null)
            {
                _cellStatus = null;
                return;
            }

            var physics = _session.State == SimState.Stopped ? null : _session.Physics;
            var sb = new StringBuilder();
            switch (o.PartId)
            {
                case CellParts.ArmId:
                    ArmStatus(o, physics, sb);
                    break;
                case CellParts.GripperId:
                case CellParts.VacuumId:
                {
                    var t = physics?.ToolOf(o.Id);
                    if (t == null) sb.Append("Dünya çalışırken takım durumu burada görünür.");
                    else if (t.IsVacuum)
                    {
                        sb.AppendLine($"Komut      : {(t.Command ? "VAKUM AÇIK" : "kapalı")}");
                        sb.AppendLine($"Vakum      : {Kpa(t.Pressure)} · tutma {t.HoldForce:0} N");
                        sb.AppendLine($"Durum      : {(t.Holding ? "parça tutuluyor" : t.HeldBody != null ? "sızdırmazlık var, basınç yükseliyor" : "boşta")}");
                        sb.Append($"Yük        : {t.Load:0} N · körük {t.CupTravel * 1000:0.0} mm");
                    }
                    else
                    {
                        var tr = t.FingerTravel;
                        double gap = (ToolGeometry.FingerMinGap + t.TotalStroke - tr.Sum()) * 1000;
                        sb.AppendLine($"Komut      : {(t.Command ? "KAPAT" : "aç")}");
                        sb.AppendLine($"Açıklık    : {gap:0.0} mm");
                        sb.Append($"Durum      : {(t.Holding ? "parça kavrandı" : t.Command ? "kapanıyor / boş" : "açık")}");
                    }

                    break;
                }

                case CellParts.ConveyorId:
                {
                    var cv = physics?.ConveyorOf(o.Id);
                    if (cv == null) sb.Append("Dünya çalışırken bant durumu burada görünür.");
                    else
                    {
                        var p = ParamsOf(o);
                        sb.AppendLine($"Bant       : {(cv.Running ? "çalışıyor" : "durdu")} · {CellParts.Num(p, "speed", 0.2):0.00} m/s");
                        sb.AppendLine($"Fotosel    : {(cv.SensorOn ? "PARÇA VAR" : "boş")}");
                        sb.Append($"Besleyici  : {physics!.SpawnedParts.Count(s => s.Source == o.Id)} parça sahnede");
                    }

                    break;
                }

                case CellParts.CurtainId:
                {
                    var lc = physics?.CurtainOf(o.Id);
                    sb.Append(lc == null ? "Dünya çalışırken perde durumu burada görünür." : lc.Blocked ? $"KESİLDİ · {lc.Beams.Count(b => b.Cut)} / {lc.Beams.Count} ışın · OSSD düşük" : $"Serbest · {lc.Beams.Count} ışın · OSSD yüksek");
                    break;
                }
            }

            _cellStatus.Text = Shell.Lang.T(sb.ToString().TrimEnd());   // [lang] line by line
        }

        private void ArmStatus(WorldObject o, WorldPhysics? physics, StringBuilder sb)
        {
            var arm = physics?.ArmOf(o.Id);
            var model = arm?.Model ?? CellGeometry.ModelOf(o);
            if (arm == null)
            {
                var q0 = CellGeometry.StartJoints(o);
                var tcp0 = ArmKinematics.Forward(model, q0, ToolTcp(o));
                sb.AppendLine("Durum  dünya durdu · başlangıç pozu");
                AppendTcp(sb, tcp0);
                for (int i = 0; i < model.Dof; i++)
                {
                    var s = model.Joints[i];
                    sb.AppendLine($"{s.Name,-3}{s.ToDisplay(q0[i]),7:0.0}{s.Unit,-2} [{s.ToDisplay(s.Min):0} … {s.ToDisplay(s.Max):0}]");
                }

                return;
            }

            var c = arm.Controller;
            string state = c.State switch
            {
                ArmState.Idle => "hazır",
                ArmState.Moving => "hareket (elle)",
                ArmState.Running => "PROGRAM ÇALIŞIYOR",
                ArmState.Paused => "duraklatıldı",
                ArmState.SafetyStop => "GÜVENLİK DURDURMASI",
                _ => "ARIZA"
            };
            sb.AppendLine($"Durum  {state}");
            sb.AppendLine($"Döngü  {c.CycleCount}" + (c.PalletCount > 1 ? $" · palet {c.PalletIndex + 1}/{c.PalletCount}" : string.Empty) + $" · %{c.Override * 100:0} hız");
            if (c.State == ArmState.Fault) sb.AppendLine("Arıza  " + c.FaultText);
            else if (c.CurrentInstruction is { } ins) sb.AppendLine($"Satır  {ins.Line}: {ins.Text}");
            AppendTcp(sb, c.TcpMeasured);
            var q = arm.Measured;
            for (int i = 0; i < model.Dof; i++)
            {
                var s = model.Joints[i];
                double load = c.JointLoad[i];
                int bars = (int)Math.Round(Math.Min(1, load) * 10);
                sb.AppendLine($"{s.Name,-3}{s.ToDisplay(q[i]),7:0.0}{s.Unit,-2} {new string('█', bars)}{new string('·', 10 - bars)} {Shell.Lang.Percent(load * 100, "0")}");   // [lang] "1%" in English
            }

            var di = string.Concat(c.DigitalIn.Take(4).Select(b => b ? '1' : '0'));
            var dout = string.Concat(c.DigitalOut.Take(4).Select(b => b ? '1' : '0'));
            sb.AppendLine($"G/Ç    DI {di} · DO {dout} · güvenlik {(c.SafetyOk ? "tamam" : "AÇIK")}");
            if (arm.Tool is { } t)
            {
                sb.Append(t.IsVacuum ? $"Takım  vakum {Kpa(t.Pressure)} · {(t.Holding ? "tutuyor" : "boş")}" : $"Takım  tutucu {(t.Command ? "kapalı" : "açık")} · {(t.Holding ? "parça var" : "boş")}");
            }
        }

        private static string Kpa(double p) => p < 0.5 ? "0 kPa" : "−" + p.ToString("0", Inv) + " kPa";

        private static void AppendTcp(StringBuilder sb, Transform3 tcp)
        {
            var (r, p, y) = tcp.ToRpy();
            double d = 180 / Math.PI;
            sb.AppendLine(string.Format(Inv, "TCP    {0:0.0}, {1:0.0}, {2:0.0} mm", tcp.P.X * 1000 + 0.0, tcp.P.Y * 1000 + 0.0, tcp.P.Z * 1000 + 0.0));
            sb.AppendLine(string.Format(Inv, "       rx {0:0} · ry {1:0} · rz {2:0}°", Math.Round(r * d) + 0.0, Math.Round(p * d) + 0.0, Math.Round(y * d) + 0.0));
        }
    }
}
