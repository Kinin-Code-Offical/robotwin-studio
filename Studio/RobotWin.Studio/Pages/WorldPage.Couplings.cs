using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [couple] Gears, belts and lead screws in the inspector: under the joint that has one, its sizes, which way it turns,
    /// what it loses and "Kaldır"; added from "Özellik ekle ▾" by picking the joint at the other end (Sandbox/WorldCoupling.cs).
    /// </summary>
    public partial class WorldPage
    {
        /// <summary>The joints that move this object: the shaft a gear on it turns on, the rail it slides along.</summary>
        private static List<WorldJoint> MovingJoints(WorldDocument world, WorldObject o) =>
            world.Joints.Where(j => j.ObjectA == o.Id && j.Kind != WorldJointKind.Fixed).ToList();

        private static string JointName(WorldDocument world, WorldJoint j) =>
            (world.Find(j.ObjectA)?.Name ?? j.RosName) + " (" + (j.Kind == WorldJointKind.Hinge ? "menteşe" : "kızak") + ")";

        private static bool Coupled(WorldDocument world, WorldJoint x, WorldJoint y) =>
            world.Couplings?.Any(c => (c.JointA == x.Id && c.JointB == y.Id) || (c.JointA == y.Id && c.JointB == x.Id)) == true;

        /// <summary>The couplings of one of the object's joints, each with its sizes; under the joint in "Mekanik bağlantılar".</summary>
        private void CouplingRows(WorldDocument world, WorldJoint j, HashSet<WorldCoupling> shown)
        {
            if (world.Couplings == null) return;
            foreach (var c in world.Couplings.Where(c => c.JointA == j.Id || c.JointB == j.Id).ToList())
            {
                var a = world.Joints.FirstOrDefault(x => x.Id == c.JointA);
                var b = world.Joints.FirstOrDefault(x => x.Id == c.JointB);
                if (a == null || b == null || !shown.Add(c)) continue;
                bool driving = c.JointA == j.Id;
                var other = driving ? b : a;
                var coupling = c;
                Inspector.Children.Add(FeatureHeader(c.Title(a.Kind, b.Kind) + " → " + (world.Find(other.ObjectA)?.Name ?? other.RosName), "Dişli bağı kaldırıldı", () =>
                {
                    world.Couplings?.Remove(coupling);
                    if (world.Couplings?.Count == 0) world.Couplings = null;
                }));
                void Size(string label, bool first, double min, double max, string format) =>
                    Inspector.Children.Add(NumberField(label, first ? coupling.SizeA : coupling.SizeB, v => Edit("Dişli ölçüsü değişti", () =>
                    {
                        if (first) coupling.SizeA = v;
                        else coupling.SizeB = v;
                    }), min, max, format));
                bool linear = WorldCoupling.Linear(a.Kind, b.Kind);
                if (c.Kind == WorldCoupling.Screw)
                {
                    Size("Hatve (mm/tur)", true, 0.1, 100, "0.##");
                    Size("Mil çapı (mm)", false, 1, 200, "0.#");
                }
                else if (linear) Size(c.Kind == WorldCoupling.Gear ? "Pinyon çapı (mm)" : "Kasnak çapı (mm)", true, 1, 1000, "0.##");
                else if (c.Kind == WorldCoupling.Gear)
                {
                    Size("Diş sayısı · bu dişli", driving, 4, 1000, "0");
                    Size("Diş sayısı · öteki dişli", !driving, 4, 1000, "0");
                }
                else
                {
                    Size("Kasnak çapı · bu (mm)", driving, 1, 2000, "0.#");
                    Size("Kasnak çapı · öteki (mm)", !driving, 1, 2000, "0.#");
                }

                double eta = c.EfficiencyFor(a.Kind, b.Kind);
                Inspector.Children.Add(NumberField("Verim (%)", Math.Round(eta * 100, 1), v => Edit("Dişli verimi değişti", () => coupling.Efficiency = v / 100), 5, 100, "0.#"));
                bool hingeB = b.Kind == WorldJointKind.Hinge;
                Inspector.Children.Add(NumberField(hingeB ? "Boşluk (°)" : "Boşluk (mm)", c.Backlash, v => Edit("Dişli boşluğu değişti", () => coupling.Backlash = v), 0, hingeB ? 30 : 10, "0.###"));
                if (c.Kind == WorldCoupling.Belt)
                    Inspector.Children.Add(NumberField("Kayış rijitliği (N/mm, 0 = rijit)", c.BeltStiffness, v => Edit("Kayış rijitliği değişti", () => coupling.BeltStiffness = v), 0, 1e6, "0.#"));
                var reverse = new CheckBox
                {
                    Content = "Ters yön",
                    IsChecked = c.Reverse,
                    Style = (Style)FindResource("Switch"),
                    Margin = new Thickness(0, 2, 0, 6),
                    ToolTip = "Çapraz kayış, araya giren bir ara dişli ya da sol vida: öteki taraf ters yöne döner ya da kayar."
                };
                reverse.Click += (_, _) =>
                {
                    bool on = reverse.IsChecked == true;
                    Edit("Dişli yönü değişti", () => coupling.Reverse = on);
                    BuildInspector();
                };
                Inspector.Children.Add(reverse);
                Inspector.Children.Add(Note(CouplingSummary(c, a, b, eta)));
            }
        }

        /// <summary>What the coupling does: the speed ratio and its direction, or the travel per turn; what it loses; whether it locks.</summary>
        private static string CouplingSummary(WorldCoupling c, WorldJoint a, WorldJoint b, double eta)
        {
            string loss = "verim %" + (eta * 100).ToString("0", CultureInfo.InvariantCulture);
            if (!WorldCoupling.Linear(a.Kind, b.Kind))
            {
                double r = c.Ratio(a.Kind, b.Kind) ?? 0;
                return "Hız oranı ×" + Math.Abs(r).ToString("0.###", CultureInfo.InvariantCulture) + " · " + (r < 0 ? "ters yönde" : "aynı yönde") + " · " + loss;
            }

            double perTurn = c.Kind == WorldCoupling.Screw ? c.SizeA : Math.PI * c.SizeA;
            string travel = perTurn.ToString("0.##", CultureInfo.InvariantCulture) + " mm/tur · " + loss;
            if (c.Kind != WorldCoupling.Screw) return travel;
            return travel + " · " + (eta <= 0.5 ? "kendini kilitler: yük mili geri çeviremez" : "yük mili geri çevirebilir");
        }

        /// <summary>"Dişli, kayış, vidalı mil" under "Özellik ekle ▾": the other joints of the world, and what can join them.</summary>
        private MenuItem? CouplingMenu(WorldDocument world, WorldObject o)
        {
            var own = MovingJoints(world, o);
            if (own.Count == 0) return null;
            var menu = new MenuItem
            {
                Header = "Dişli, kayış, vidalı mil",
                ToolTip = "Bu parçanın eklemini başka bir eklemle dişli, kayış ya da vidalı milla bağlayın: biri döndükçe öteki oranınca döner ya da kayar, yük iki yana da geçer."
            };
            foreach (var mine in own)
            {
                foreach (var other in world.Joints.Where(x => x.Kind != WorldJointKind.Fixed && x.ObjectA != o.Id && !Coupled(world, mine, x)).Take(24))
                {
                    var partner = new MenuItem { Header = (own.Count > 1 ? JointName(world, mine) + " → " : string.Empty) + JointName(world, other) };
                    foreach (var kind in new[] { WorldCoupling.Gear, WorldCoupling.Belt, WorldCoupling.Screw })
                    {
                        if (!WorldCoupling.Fits(kind, mine.Kind, other.Kind)) continue;
                        var c = NewCoupling(world, kind, mine, other);
                        string title = c.Title(mine.Kind, other.Kind);
                        var item = new MenuItem { Header = title };
                        item.Click += (_, _) =>
                        {
                            Edit(title + " eklendi", () => (world.Couplings ??= new List<WorldCoupling>()).Add(c));
                            BuildInspector();
                        };
                        partner.Items.Add(item);
                    }

                    menu.Items.Add(partner);
                }
            }

            return menu.Items.Count > 0 ? menu : null;
        }

        /// <summary>
        /// A new coupling sized from the parts: a cylinder's diameter is its gear's teeth (module 1) or its pulley, a screw rod's
        /// diameter its thread's; else 20 and 40 teeth, a GT2 pulley, a T8×8 screw. Between a hinge and a slider the hinge drives.
        /// </summary>
        private static WorldCoupling NewCoupling(WorldDocument world, string kind, WorldJoint mine, WorldJoint other)
        {
            bool linear = WorldCoupling.Linear(mine.Kind, other.Kind);
            var a = linear && mine.Kind != WorldJointKind.Hinge ? other : mine;
            var b = ReferenceEquals(a, mine) ? other : mine;
            double? Diameter(WorldJoint j) => world.Find(j.ObjectA) is { Shape: "cylinder" or "wheel" } x ? Math.Round(Math.Max(x.SizeX, x.SizeY) * 1000, 1) : null;
            int n = 1;
            while (world.Couplings?.Any(c => c.Id == "couple" + n) == true) n++;
            var coupling = new WorldCoupling { Id = "couple" + n, Kind = kind, JointA = a.Id, JointB = b.Id };
            if (kind == WorldCoupling.Screw)
            {
                coupling.SizeA = 8;
                coupling.SizeB = Diameter(a) ?? 8;
            }
            else if (linear) coupling.SizeA = Diameter(a) ?? (kind == WorldCoupling.Gear ? 20 : 12.73);
            else
            {
                double da = Diameter(a) ?? 20, db = Diameter(b) ?? 40;
                coupling.SizeA = kind == WorldCoupling.Gear ? Math.Max(8, Math.Round(da)) : da;
                coupling.SizeB = kind == WorldCoupling.Gear ? Math.Max(8, Math.Round(db)) : db;
            }

            return coupling;
        }

        /// <summary>[spring] A new spring: 0.5 N·mm/° on a hinge, 0.5 N/mm along a slider, relaxed where the joint is built.</summary>
        private static void AddSpring(WorldJoint j)
        {
            j.SpringStiffness = j.Kind == WorldJointKind.Hinge ? 0.5e-3 * 180 / Math.PI : 500;
            j.SpringRest = 0;
            j.SpringDamping = 0;
        }

        /// <summary>
        /// [spring] The joint's spring in a catalogue's units: N·mm/° and N·mm·s/° on a hinge, N/mm and N·s/m along a slider;
        /// the document keeps N·m/rad and N/m.
        /// </summary>
        private void SpringRows(WorldJoint j)
        {
            bool hinge = j.Kind == WorldJointKind.Hinge;
            double perDegree = 1000 * Math.PI / 180;   // N·m/rad → N·mm/°
            var joint = j;
            Inspector.Children.Add(FeatureHeader(hinge ? "Burulma yayı" : "Helezon yay", "Yay kaldırıldı", () =>
            {
                joint.SpringStiffness = 0;
                joint.SpringRest = 0;
                joint.SpringDamping = 0;
            }));
            Inspector.Children.Add(NumberField(hinge ? "Yay sabiti (N·mm/°)" : "Yay sabiti (N/mm)", hinge ? j.SpringStiffness * perDegree : j.SpringStiffness / 1000, v =>
                Edit("Yay değişti", () => joint.SpringStiffness = hinge ? v / perDegree : v * 1000), 0, 1e7, "0.###"));
            Inspector.Children.Add(NumberField(hinge ? "Serbest konum (°)" : "Serbest konum (mm)", hinge ? j.SpringRest : j.SpringRest * 1000, v =>
                Edit("Yay değişti", () => joint.SpringRest = hinge ? v : v / 1000), -1e6, 1e6, "0.#"));
            Inspector.Children.Add(NumberField(hinge ? "Sönüm (N·mm·s/°)" : "Sönüm (N·s/m)", hinge ? j.SpringDamping * perDegree : j.SpringDamping, v =>
                Edit("Yay değişti", () => joint.SpringDamping = hinge ? v / perDegree : v), 0, 1e7, "0.###"));
            Inspector.Children.Add(Note("Motor olsa da olmasa da eklemi serbest konumuna çeker; sönüm hızla orantılı bir frendir (amortisör, gaz yay, kapı kapatıcı)."));
        }

        /// <summary>[bearing] The joint's bearing: its kind, a hinge's bore, and what it loses under load (Sandbox/Bearings.cs).</summary>
        private void BearingRows(WorldJoint j)
        {
            if (Bearings.Find(j.Bearing) is not { } spec) return;
            var joint = j;
            Inspector.Children.Add(FeatureHeader("Yatak", "Yatak kaldırıldı", () =>
            {
                joint.Bearing = null;
                joint.BearingBore = 0;
            }));
            var box = new ComboBox { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var b in Bearings.For(j.Kind)) box.Items.Add(new ComboBoxItem { Content = b.Name, Tag = b.Id, IsSelected = b.Id == spec.Id, ToolTip = b.Note });
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is not ComboBoxItem { Tag: string id } || id == joint.Bearing) return;
                Edit("Yatak değişti", () => joint.Bearing = id);
                BuildInspector();
            };
            Inspector.Children.Add(Labeled("Tür", box));
            bool hinge = j.Kind == WorldJointKind.Hinge;
            if (hinge)
                Inspector.Children.Add(NumberField("Yatak delik çapı (mm)", j.BearingBore > 0 ? j.BearingBore : spec.Bore, v => Edit("Yatak değişti", () => joint.BearingBore = v), 1, 500, "0.#"));
            double perNewton = Bearings.LoadFriction(j);
            string drag = hinge ? (perNewton * 100 * 1000).ToString("0.##", CultureInfo.InvariantCulture) + " N·mm" : (perNewton * 100).ToString("0.##", CultureInfo.InvariantCulture) + " N";
            Inspector.Children.Add(Note(spec.Note));
            Inspector.Children.Add(Note("μ = " + spec.Mu.ToString("0.####", CultureInfo.InvariantCulture) + " · 100 N yükte " + drag + " sürtünme"));
        }

        /// <summary>The load and speed of the selected part's couplings while running.</summary>
        private void CouplingLive(WorldObject o, List<string> lines)
        {
            var world = World;
            if (world == null || _session.Physics is not { } ph) return;
            foreach (var j in MovingJoints(world, o))
            {
                foreach (var c in world.Couplings?.Where(c => c.JointA == j.Id || c.JointB == j.Id) ?? Enumerable.Empty<WorldCoupling>())
                {
                    var a = world.Joints.FirstOrDefault(x => x.Id == c.JointA);
                    var b = world.Joints.FirstOrDefault(x => x.Id == c.JointB);
                    if (a == null || b == null || ph.CouplingState(c.Id) is not { } s) continue;
                    double speed = c.JointA == j.Id ? s.SpeedA : s.SpeedB;
                    string pace = j.Kind == WorldJointKind.Hinge ? $"{speed * 30 / Math.PI:0} rpm" : $"{speed * 1000:0.0} mm/s";
                    lines.Add($"{c.Title(a.Kind, b.Kind)} yükü: {SiValue.Format(Math.Abs(s.Load), b.Kind == WorldJointKind.Hinge ? "N·m" : "N")} · {pace}");
                }

                if (j.Bearing != null && ph.BearingState(j.Id) is { } drag)   // [bearing]
                    lines.Add("Yatak sürtünmesi: " + SiValue.Format(drag, j.Kind == WorldJointKind.Hinge ? "N·m" : "N"));
                if (j.Sprung && ph.SpringState(j.Id) is { } sp)   // [spring]
                {
                    lines.Add(j.Kind == WorldJointKind.Hinge
                        ? $"Yay torku: {SiValue.Format(Math.Abs(sp.Load), "N·m")} · {sp.Position:0.0}°"
                        : $"Yay kuvveti: {SiValue.Format(Math.Abs(sp.Load), "N")} · {sp.Position * 1000:0.0} mm");
                }
            }
        }
    }
}
