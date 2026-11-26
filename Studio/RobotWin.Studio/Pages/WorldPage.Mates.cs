using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.World;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [mate] Assembly mates in the world: the Hizala tool (H) picks a face or an axis on two parts and offers the relations
    /// that fit them; the parts are placed at once (Sandbox/WorldMates.cs), and the inspector lists a part's mates with
    /// their offsets and the freedom they leave. A dragged part takes the parts mated to it along.
    /// </summary>
    public partial class WorldPage
    {
        /// <summary>The first face or axis picked, waiting for the second.</summary>
        private MateFeature? _mateFirst;

        /// <summary>What the last mate change came to (shown under the part's mates).</summary>
        private MateResult? _mateResult;

        private void OnMateFace(string id, Point3D point, Vector3D normal)
        {
            var world = World;
            var o = world?.Find(id);
            if (world == null || o == null) return;
            var feature = Feature(o, new Vec3(point.X, point.Y, point.Z), new Vec3(normal.X, normal.Y, normal.Z).Normalized());
            if (_mateFirst == null || _mateFirst.Object == id)
            {
                _mateFirst = feature;
                W.Status = $"Hizala: {o.Name} · {FeatureName(feature)} seçildi; şimdi öteki parçanın yüzünü ya da eksenini tıklayın";
                return;
            }

            var first = _mateFirst;
            _mateFirst = null;
            ShowMateMenu(world, first, feature);
        }

        /// <summary>The picked spot as a face, or as the part's axis when the round side of a shaft, wheel or gear was picked.</summary>
        private MateFeature Feature(WorldObject o, Vec3 p, Vec3 n)
        {
            if (WorldMates.AxisOf(o) is { } axis)
            {
                var along = o.Pose.Orientation.Rotate(axis.Direction).Normalized();
                if (Math.Abs(along.Dot(n)) < 0.5)
                    return new MateFeature { Object = o.Id, Kind = MateFeatureKind.Axis, X = axis.Origin.X, Y = axis.Origin.Y, Z = axis.Origin.Z, DX = axis.Direction.X, DY = axis.Direction.Y, DZ = axis.Direction.Z };
            }

            // A face square to the part's own axes is taken exactly square, and onto the part's own face when the pick is
            // within a millimetre and a half of it (the screen pick is good to a pixel; the part's faces are exact).
            var local = o.Pose.Orientation.InverseRotate(n);
            var point = o.Pose.Orientation.InverseRotate(p - o.Pose.Position);
            RobotTwin.CoreSim.Sandbox.WorldBodyGeometry? g = null;
            try
            {
                g = new RobotTwin.CoreSim.Automation.ApproximateGeometry(World!, RobotTwin.CoreSim.Design.PartLibrary.Default).GeometryOf(o);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is System.IO.IOException)
            {
                g = null;
            }

            double[] pt = { point.X, point.Y, point.Z };
            var axes = new[] { new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1) };
            for (int k = 0; k < 3; k++)
            {
                double d = local.Dot(axes[k]);
                if (Math.Abs(Math.Abs(d) - 1) >= 0.002) continue;
                local = axes[k] * Math.Sign(d);
                if (g == null) continue;
                double face = d > 0 ? (k == 0 ? g.Max.X : k == 1 ? g.Max.Y : g.Max.Z) : (k == 0 ? g.Min.X : k == 1 ? g.Min.Y : g.Min.Z);
                if (Math.Abs(pt[k] - face) < 0.0015) pt[k] = face;
            }

            return new MateFeature
            {
                Object = o.Id, Kind = MateFeatureKind.Plane, X = pt[0], Y = pt[1], Z = pt[2],
                DX = local.Normalized().X, DY = local.Normalized().Y, DZ = local.Normalized().Z
            };
        }

        private static string FeatureName(MateFeature f) => f.Kind switch
        {
            MateFeatureKind.Axis => "ekseni",
            MateFeatureKind.Point => "noktası",
            _ => "yüzü"
        };

        private static string MateName(MateKind k) => k switch
        {
            MateKind.Mate => "Yüz yüze",
            MateKind.Flush => "Aynı hizada",
            MateKind.Concentric => "Eş eksenli",
            MateKind.Parallel => "Paralel",
            MateKind.Perpendicular => "Dik",
            MateKind.Distance => "Mesafe",
            _ => "Açı"
        };

        /// <summary>Where a feature is now in the world: its point and direction.</summary>
        private static (Vec3 P, Vec3 D) Where(WorldDocument w, MateFeature f)
        {
            var o = w.Find(f.Object)!;
            return (o.Pose.ToWorld(f.Origin), o.Pose.Orientation.Rotate(f.Direction).Normalized());
        }

        private void ShowMateMenu(WorldDocument world, MateFeature a, MateFeature b)
        {
            var menu = new ContextMenu { PlacementTarget = Scene, Placement = PlacementMode.MousePoint };
            var (pa, da) = Where(world, a);
            var (pb, db) = Where(world, b);
            double distance = (pb - pa).Dot(da), angle = Math.Acos(Math.Max(-1, Math.Min(1, da.Dot(db)))) * 180 / Math.PI;
            void Item(MateKind kind, string text, double value = 0)
            {
                if (!WorldMates.Fits(kind, a.Kind, b.Kind)) return;
                var mi = new MenuItem { Header = text };
                mi.Click += (_, _) => AddMate(world, kind, a, b, value);
                menu.Items.Add(mi);
            }

            Item(MateKind.Mate, "Yüz yüze (birbirine dayalı)");
            Item(MateKind.Flush, "Aynı hizada (yüzler aynı yöne)");
            Item(MateKind.Concentric, "Eş eksenli (mil deliğinde)");
            Item(MateKind.Parallel, "Paralel");
            Item(MateKind.Perpendicular, "Dik");
            Item(MateKind.Distance, $"Mesafe: şimdiki {distance * 1000:0.##} mm", Math.Round(distance, 6));
            Item(MateKind.Angle, $"Açı: şimdiki {angle:0.#}°", Math.Round(angle, 2));
            if (menu.Items.Count == 0)
            {
                W.Status = "Bu ikisi arasında kurulabilecek ilişki yok (bir yüz ya da eksen seçin)";
                return;
            }

            menu.IsOpen = true;
        }

        private void AddMate(WorldDocument world, MateKind kind, MateFeature a, MateFeature b, double value)
        {
            // Tried on a copy first: a contradicting mate leaves no step in the undo history.
            var trial = WorldDocument.FromJson(world.ToJson());
            var (_, tried) = WorldMates.Add(trial, kind, a.Clone(), b.Clone(), value);
            if (tried.State == MateState.Conflicting)
            {
                _mateResult = tried;
                W.Status = $"{MateName(kind)} eklenmedi: öteki ilişkilerle çelişiyor" + Names(trial, tried.Culprits);
                return;
            }

            MateResult? r = null;
            Edit(MateName(kind), () => r = WorldMates.Add(world, kind, a, b, value).Result);
            _mateResult = r;
            if (r != null) W.Status = MateName(kind) + ": " + Describe(world, r, b.Object);
        }

        /// <summary>
        /// [palette] Checks every mate of the world on a copy (nothing moves): placed, how much freedom is left, or which mates
        /// repeat or contradict each other; said in the status bar.
        /// </summary>
        public void CheckMates()
        {
            if (World == null) return;
            int count = World.Mates?.Count ?? 0;
            if (count == 0)
            {
                W.Status = "Montaj ilişkisi yok (Hizala aracı, H)";
                return;
            }

            var copy = WorldDocument.FromJson(World.ToJson());
            var r = WorldMates.Solve(copy, explain: true);
            W.Status = $"Montaj ilişkileri ({count}): " + Describe(copy, r);
        }

        /// <summary>After a drag: the mated parts follow the dragged one (and a fully placed one goes back). True when anything moved.</summary>
        private bool ResolveMates()
        {
            if (World?.Mates == null) return false;
            var before = World.Objects.ToDictionary(o => o.Id, o => (o.Pose.X, o.Pose.Y, o.Pose.Z, o.Pose.Roll, o.Pose.Pitch, o.Pose.Yaw));
            _mateResult = WorldMates.Solve(World, explain: false);
            return World.Objects.Any(o => before.TryGetValue(o.Id, out var p) && p != (o.Pose.X, o.Pose.Y, o.Pose.Z, o.Pose.Roll, o.Pose.Pitch, o.Pose.Yaw));
        }

        private static string Names(WorldDocument w, IEnumerable<string> ids)
        {
            var names = ids.Select(id => w.Mates?.FirstOrDefault(m => m.Id == id)).Where(m => m != null).Select(m => Label(w, m!)).ToList();
            return names.Count == 0 ? "" : ": " + string.Join(" · ", names);
        }

        private static string Label(WorldDocument w, WorldMate m) =>
            $"{MateName(m.Kind)} ({w.Find(m.A.Object)?.Name ?? m.A.Object} – {w.Find(m.B.Object)?.Name ?? m.B.Object})";

        /// <summary>The result in words; with a part, the freedom of the parts mated with it.</summary>
        private static string Describe(WorldDocument w, MateResult r, string? part = null)
        {
            int freedom = part != null && r.FreedomOf(part) is { } f ? f : r.Freedom;
            return r.State switch
            {
                MateState.Redundant => "başka ilişkileri tekrarlıyor" + Names(w, r.Culprits),
                MateState.Conflicting => "ilişkiler çelişiyor" + Names(w, r.Culprits),
                _ when freedom == 0 => "parçalar tam yerleşti",
                _ => freedom == 1 ? "1 serbestlik kaldı" : $"{freedom} serbestlik kaldı"
            };
        }

        /// <summary>The part's mates: each with its offset or angle to set, and a button to remove it; the freedom left.</summary>
        private void MatesSection(WorldDocument world, WorldObject o)
        {
            var mates = WorldMates.Of(world, o.Id).ToList();
            if (mates.Count == 0) return;
            Inspector.Children.Add(Caption($"Montaj ilişkileri ({mates.Count})"));
            foreach (var m in mates)
            {
                var mate = m;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var del = new Button { Style = (Style)FindResource("Btn.Icon"), Width = 26, Height = 26, ToolTip = "İlişkiyi kaldır", Content = new Theme.Icon { Data = (Geometry)FindResource("I.Delete"), Size = 13 } };
                del.Click += (_, _) => Edit("İlişki kaldırıldı", () =>
                {
                    world.Mates?.Remove(mate);
                    if (world.Mates is { Count: 0 }) world.Mates = null;
                    _mateResult = null;
                });
                DockPanel.SetDock(del, Dock.Right);
                row.Children.Add(del);
                row.Children.Add(new TextBlock { Text = Label(world, mate), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                Inspector.Children.Add(row);
                if (mate.Kind is MateKind.Mate or MateKind.Flush or MateKind.Distance)
                    Inspector.Children.Add(NumberField("Aralık (mm)", mate.Value * 1000, v => SetMateValue(world, mate, v / 1000), -1e5, 1e5, "0.###"));
                else if (mate.Kind == MateKind.Angle)
                    Inspector.Children.Add(NumberField("Açı (°)", mate.Value, v => SetMateValue(world, mate, v), 0, 180, "0.##"));
            }

            if (_mateResult != null) Inspector.Children.Add(Note(Describe(world, _mateResult, o.Id)));
            // [mate] What the mates leave becomes the joint the physics holds the part with.
            var (joint, why) = WorldMates.JointFor(world, o.Id);
            if (joint != null)
            {
                bool joined = world.Joints.Any(x => (x.ObjectA == o.Id && x.ObjectB == joint.ObjectB) || (x.ObjectB == o.Id && x.ObjectA == joint.ObjectB));
                string kind = joint.Kind switch { WorldJointKind.Hinge => "menteşe", WorldJointKind.Slider => "kızak", _ => "sabit" };
                if (!joined)
                    Inspector.Children.Add(Action($"İlişkilerden eklem kur ({kind})", () => Edit($"{o.Name}: {kind} eklem kuruldu", () => world.Joints.Add(joint)),
                        "Çalışırken parça bu eklemle tutulur: ilişkilerin bıraktığı hareket kalır"));
            }
            else if (why != null) Inspector.Children.Add(Note("Eklem: " + why));

            Inspector.Children.Add(Action("Başka bir ilişki ekle (H)", () => SetTool(WorldTool.Mate)));
        }

        /// <summary>
        /// [mate] The snapshot "mate": a block and a shaft placed by picking on screen as a user does (the table's top and
        /// the block's top, level 50 mm apart; the hub's round side and the shaft's, on one axis), the picks and the
        /// result in ROBOTWIN_SNAPSHOT_LOG.
        /// </summary>
        private void MateSnapshot()
        {
            var w = new WorldDocument { Title = "Montaj ilişkileri" };
            var table = w.AddBody("box", 0.3, 0.2, 0.02, new WorldPose());
            table.Name = "Masa";
            table.IsStatic = true;
            var block = w.AddBody("box", 0.05, 0.05, 0.05, new WorldPose { X = 0.06, Y = 0.03, Z = 0.12, Roll = 18, Pitch = 9, Yaw = 25 });
            block.Name = "Blok";
            var hub = w.AddBody("cylinder", 0.03, 0.03, 0.03, new WorldPose { X = -0.08, Y = 0.0, Z = 0.02 });
            hub.Name = "Göbek";
            hub.IsStatic = true;
            var shaft = w.AddBody("cylinder", 0.008, 0.008, 0.12, new WorldPose { X = -0.05, Y = -0.05, Z = 0.09, Roll = 30, Yaw = 20 });
            shaft.Name = "Mil";
            _main.NewWorld(w);
            OnShown();
            Scene.FrameAll();
            Scene.UpdateLayout();
            var world = World!;
            var log = new System.Text.StringBuilder();
            MateFeature? Pick(WorldObject o, Vec3 local, string what)
            {
                var at = o.Pose.ToWorld(local);
                if (Scene.ScreenOf(new Point3D(at.X, at.Y, at.Z)) is not { } screen || Scene.SurfaceAt(screen) is not { } hit)
                {
                    log.AppendLine($"{what}: ekranda bulunamadı");
                    return null;
                }

                log.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}: {1} ({2:0.0000},{3:0.0000},{4:0.0000}) n ({5:0.000},{6:0.000},{7:0.000}) [beklenen nokta ({8:0.0000},{9:0.0000},{10:0.0000})]",
                    what, world.Find(hit.Id)?.Name, hit.Point.X, hit.Point.Y, hit.Point.Z, hit.Normal.X, hit.Normal.Y, hit.Normal.Z, at.X, at.Y, at.Z));
                var f = Feature(world.Find(hit.Id)!, new Vec3(hit.Point.X, hit.Point.Y, hit.Point.Z), new Vec3(hit.Normal.X, hit.Normal.Y, hit.Normal.Z).Normalized());
                log.AppendLine($"  → {world.Find(f.Object)?.Name} {FeatureName(f)}");
                return f;
            }

            var tableTop = Pick(table, new Vec3(0.09, -0.06, 0.02), "masanın üstü");
            var blockTop = Pick(block, new Vec3(0, 0, 0.05), "bloğun üstü");
            if (tableTop != null && blockTop != null) AddMate(world, MateKind.Flush, tableTop, blockTop, 0.05);
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "blok: ({0:0.0000},{1:0.0000},{2:0.0000}) r {3:0.##} p {4:0.##} y {5:0.##} · {6}",
                block.Pose.X, block.Pose.Y, block.Pose.Z, block.Pose.Roll, block.Pose.Pitch, block.Pose.Yaw, _mateResult == null ? "-" : Describe(world, _mateResult, block.Id)));
            var hubSide = Pick(hub, new Vec3(0, -0.015, 0.015), "göbeğin yanı");
            var shaftSide = Pick(shaft, new Vec3(0.004, 0, 0.06), "milin yanı");
            if (hubSide != null && shaftSide != null) AddMate(world, MateKind.Concentric, hubSide, shaftSide, 0);
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "mil: ({0:0.0000},{1:0.0000},{2:0.0000}) r {3:0.##} p {4:0.##} · {5}",
                shaft.Pose.X, shaft.Pose.Y, shaft.Pose.Z, shaft.Pose.Roll, shaft.Pose.Pitch, _mateResult == null ? "-" : Describe(world, _mateResult, shaft.Id)));
            Scene.Select(block.Id);
            BuildInspector();
            if (Environment.GetEnvironmentVariable("ROBOTWIN_SNAPSHOT_LOG") is { Length: > 0 } path) System.IO.File.AppendAllText(path, log.ToString());
        }

        /// <summary>A mate's offset or angle set: the parts follow; a value the other mates contradict is refused.</summary>
        private void SetMateValue(WorldDocument world, WorldMate mate, double value)
        {
            var trial = WorldDocument.FromJson(world.ToJson());
            var copy = trial.Mates!.First(m => m.Id == mate.Id);
            copy.Value = value;
            var tried = WorldMates.Solve(trial);
            if (tried.State == MateState.Conflicting)
            {
                W.Status = $"{MateName(mate.Kind)} değişmedi: öteki ilişkilerle çelişiyor" + Names(trial, tried.Culprits);
                BuildInspector();
                return;
            }

            Edit(MateName(mate.Kind) + " değişti", () =>
            {
                mate.Value = value;
                _mateResult = WorldMates.Solve(world);
            });
        }
    }
}
