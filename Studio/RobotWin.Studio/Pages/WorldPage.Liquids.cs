using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    // [liquid] Liquids in the palette and the inspector.
    public partial class WorldPage
    {
        private void LiquidPalette()
        {
            Palette.Children.Add(Caption("Sıvılar (parçacıklarla gerçek akış)"));
            Palette.Children.Add(Note("Su, yağ, bal … gerçek yoğunluk, viskozite ve yüzey gerilimiyle akar; kabın duvarlarına basınç, kaba ağırlık, içindekilere kaldırma kuvveti uygular. Kutu ya da silindir, dünya başlarken sıvının dolduracağı hacimdir."));
            Palette.Children.Add(Row("Cam bardak (su dolu)", "Ø70 × 100 mm, 2,5 mm cam, 193 ml su", () => AddGlass(true)));
            Palette.Children.Add(Row("Cam bardak (boş)", "Ø70 × 100 mm, 2,5 mm cam", () => AddGlass(false)));
            Palette.Children.Add(Row("Su (silindir hacim)", "Ø60 × 60 mm, 170 ml", () => AddLiquid("cylinder", 0.06, 0.06, 0.06)));
            Palette.Children.Add(Row("Su (kutu hacim)", "60 × 60 × 60 mm, 216 ml", () => AddLiquid("box", 0.06, 0.06, 0.06)));
        }

        private void AddLiquid(string shape, double sx, double sy, double sz, string kind = "water")
        {
            AddBody(shape, sx, sy, sz, o =>
            {
                var k = LiquidKind.Find(kind);
                o.Name = World!.UniqueName(k.Name);
                o.Parameters["liquid"] = k.Id;
                o.MassKg = k.Density * WorldPhysics.LiquidVolume(o);
            });
        }

        private void AddGlass(bool full)
        {
            EnsureWorld();
            WorldObject? glass = null;
            Edit(full ? "Su dolu bardak eklendi" : "Bardak eklendi", () =>
            {
                var w = World!;
                glass = w.AddBody("cylinder", 0.07, 0.07, 0.10, NewPose(), "CAM");
                glass.Name = w.UniqueName("Bardak");
                glass.MassKg = 0.21;
                glass.Parameters["tube.wall"] = "0.0025";
                glass.Parameters["tube.bottom"] = "0.006";
                if (!full) return;
                double ri = 0.035 - 0.0025 - 0.0005;
                var pose = glass.Pose.Clone();
                pose.Z += 0.0065;
                var water = w.AddBody("cylinder", 2 * ri, 2 * ri, 0.06, pose, "PLA");
                water.Name = w.UniqueName("Su");
                water.Parameters["liquid"] = "water";
                water.MassKg = LiquidKind.Water.Density * WorldPhysics.LiquidVolume(water);
            });
            if (glass != null) Scene.Select(glass.Id);
        }

        /// <summary>The inspector of a liquid: where its volume is, how big, which liquid, and what that liquid is.</summary>
        private void LiquidSection(WorldDocument world, WorldObject o)
        {
            var kind = LiquidKind.Find(o.Parameters["liquid"]);
            Inspector.Children.Add(Caption("Sıvı"));
            var pick = new ComboBox { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var k in LiquidKind.All) pick.Items.Add(new ComboBoxItem { Content = k.Name, Tag = k.Id, IsSelected = k == kind });
            pick.SelectionChanged += (_, _) =>
            {
                if (pick.SelectedItem is ComboBoxItem it && (string)it.Tag != kind.Id)
                    Edit("Sıvı değişti", () =>
                    {
                        o.Parameters["liquid"] = (string)it.Tag;
                        o.MassKg = LiquidKind.Find((string)it.Tag).Density * WorldPhysics.LiquidVolume(o);
                    });
            };
            Inspector.Children.Add(Labeled("Tür", pick));
            double v = WorldPhysics.LiquidVolume(o);
            Inspector.Children.Add(Note(string.Format(CultureInfo.CurrentCulture,
                "{0:0.#} ml · {1:0.#} g · yoğunluk {2:0} kg/m³ · viskozite {3:0.###} mPa·s · yüzey gerilimi {4:0.#} mN/m (20 °C)",
                v * 1e6, kind.Density * v * 1000, kind.Density, kind.Viscosity * 1000, kind.SurfaceTension * 1000)));
            Inspector.Children.Add(Note("Kutu ya da silindir, dünya başlarken sıvının dolduracağı hacimdir; içindeki katı parçaların yeri boş kalır. Canlı görüntüde yaklaşık 4000 parçacık, render'da doğruluk ayarına göre 6000–30 000 parçacık kullanılır."));

            Inspector.Children.Add(Caption("Hacim"));
            Inspector.Children.Add(Vector3Row("Konum (mm)", o.Pose.X * 1000, o.Pose.Y * 1000, o.Pose.Z * 1000, (x, y, z) =>
                Edit("Konum değişti", () =>
                {
                    o.Pose.X = x / 1000;
                    o.Pose.Y = y / 1000;
                    o.Pose.Z = Math.Max(0, z / 1000);
                })));
            Inspector.Children.Add(Vector3Row(o.Shape == "cylinder" ? "Çap · — · yükseklik (mm)" : "X · Y · Z (mm)", o.SizeX * 1000, o.SizeY * 1000, o.SizeZ * 1000, (x, y, z) =>
                Edit("Boyut değişti", () =>
                {
                    o.SizeX = Math.Max(0.002, x / 1000);
                    o.SizeY = o.Shape == "cylinder" ? o.SizeX : Math.Max(0.002, y / 1000);
                    o.SizeZ = Math.Max(0.002, z / 1000);
                    o.MassKg = LiquidKind.Find(o.Parameters["liquid"]).Density * WorldPhysics.LiquidVolume(o);
                })));
            double spacing = o.Parameters.TryGetValue("liquid.spacing", out var sp) && double.TryParse(sp, NumberStyles.Float, CultureInfo.InvariantCulture, out double sv) ? sv : 0;
            Inspector.Children.Add(NumberField("Parçacık aralığı (mm, 0 = otomatik)", spacing * 1000, val => Edit("Parçacık aralığı değişti", () =>
            {
                if (val <= 0) o.Parameters.Remove("liquid.spacing");
                else o.Parameters["liquid.spacing"] = (val / 1000).ToString(CultureInfo.InvariantCulture);
            }), 0, 30, "0.##"));
            if (Scene.Session?.Physics?.Liquids.FirstOrDefault(l => l.Object.Id == o.Id) is { } live)
            {
                var b = live.Body;
                Inspector.Children.Add(Note(string.Format(CultureInfo.CurrentCulture, "Canlı: {0} parçacık ({1:0.#} mm), en büyük sıkışma %{2:0.##}, {3}",
                    b.Count, b.Spacing * 1000, b.MaxCompression * 100, b.Asleep ? "durgun" : b.LastSubsteps + " alt adım")));
            }

            Inspector.Children.Add(Caption("İşlemler"));
            Inspector.Children.Add(Action("Çoğalt (Ctrl+D)", Duplicate));
            Inspector.Children.Add(Action("Sil (Del)", Delete));
        }
    }
}
