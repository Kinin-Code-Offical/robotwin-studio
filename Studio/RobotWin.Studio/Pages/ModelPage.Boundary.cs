using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Modeling;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [boundary] The boundary solid's properties: its profiles bottom to top, each with the shape editor; their heights
    /// typed as "0; 25; 50" (mm); a profile added on top or the top one taken away. CoreSim Modeling/Primitives.cs builds it.
    /// </summary>
    public partial class ModelPage
    {
        private void BoundarySection(PrimitiveSpec spec)
        {
            spec.Sections ??= new List<Sketch2D> { new Sketch2D { Outer = SketchShape.Circle(30) }, new Sketch2D { Outer = SketchShape.Circle(20) } };
            spec.Heights ??= new List<double> { 0, 30 };
            while (spec.Heights.Count < spec.Sections.Count) spec.Heights.Add((spec.Heights.Count > 0 ? spec.Heights[^1] : 0) + 20);
            for (int i = 0; i < spec.Sections.Count; i++)
            {
                Section(string.Format(CultureInfo.InvariantCulture, "Kesit {0}", i + 1), string.Format(CultureInfo.InvariantCulture, "{0:0.##} mm", spec.Heights[i]));
                ShapeEditor(spec.Sections[i].Outer, false);
            }

            Section("Kesit yükseklikleri");
            Caption("Her kesitin Z yüksekliği, alttan üste: \"0; 25; 50\" (mm). Gövde kesitlerden eğrilerle geçer; Uçlarda dik açıkken uç kesitlerden dik çıkar.");
            var heights = spec.Heights;
            var box = new TextBox
            {
                Text = string.Join("; ", heights.Take(spec.Sections.Count).Select(h => h.ToString("0.###", CultureInfo.InvariantCulture))),
                FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, Padding = new Thickness(6, 4, 6, 4)
            };
            void Commit()
            {
                var values = new List<double>();
                foreach (var part in box.Text.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!double.TryParse(part.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    {
                        _ed.Status("Yükseklikler okunamadı: sayılar ';' ile ayrılmalı");
                        return;
                    }

                    values.Add(v);
                }

                if (values.Count != spec.Sections!.Count)
                {
                    _ed.Status(string.Format(CultureInfo.InvariantCulture, "{0} kesit için {0} yükseklik gerekli", spec.Sections.Count));
                    return;
                }

                if (heights.SequenceEqual(values)) return;
                _ed.Do("Kesit yükseklikleri", () => spec.Heights = values);
            }

            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) Commit();
            };
            Props.Children.Add(box);

            var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(SmallButton("Üste kesit ekle", () => _ed.Do("Kesit eklendi", () =>
            {
                spec.Sections!.Add(spec.Sections.Count > 0 ? spec.Sections[^1].Clone() : new Sketch2D { Outer = SketchShape.Circle(20) });
                spec.Heights!.Add((spec.Heights.Count > 0 ? spec.Heights.Max() : 0) + 20);
            }), "Üstteki kesitin bir kopyası 20 mm yukarıya"));
            var remove = SmallButton("Üst kesiti kaldır", () => _ed.Do("Kesit kaldırıldı", () =>
            {
                spec.Sections!.RemoveAt(spec.Sections.Count - 1);
                spec.Heights!.RemoveAt(spec.Heights.Count - 1);
            }), "En az iki kesit kalır");
            remove.IsEnabled = spec.Sections.Count > 2;
            row.Children.Add(remove);
            Props.Children.Add(row);
        }
    }
}
