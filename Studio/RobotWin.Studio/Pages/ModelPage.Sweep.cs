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
    /// [sweep] The sweep's properties: its profile (the same shape editor as a profile's) and its path typed as points
    /// "x,y,z; x,y,z; …" (mm), committed on Enter or leaving the box. CoreSim Modeling/Primitives.cs builds it.
    /// </summary>
    public partial class ModelPage
    {
        private void SweepSection(PrimitiveSpec spec)
        {
            spec.Sketch ??= new Sketch2D { Outer = SketchShape.Circle(10) };
            spec.Path ??= new List<double> { 0, 0, 0, 0, 0, 30 };
            Section("Profil (kesit)");
            ShapeEditor(spec.Sketch.Outer, false);
            Section("Yol noktaları", $"{spec.Path.Count / 3} nokta");
            Caption("Kesitin taşındığı yol: \"x,y,z; x,y,z; …\" (mm). Köşelerde kesit ikiye bölen düzlemde durur, duvar kalınlığı korunur. Örnek kulp: -30,0,0; -30,0,30; 30,0,30; 30,0,0");
            var path = spec.Path;
            string text = string.Join("; ", Enumerable.Range(0, path.Count / 3).Select(i => string.Format(CultureInfo.InvariantCulture, "{0:0.###},{1:0.###},{2:0.###}", path[3 * i], path[3 * i + 1], path[3 * i + 2])));
            var box = new TextBox { Text = text, TextWrapping = TextWrapping.Wrap, FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, Padding = new Thickness(6, 4, 6, 4), MinHeight = 48 };
            void Commit()
            {
                var pts = new List<double>();
                foreach (var triple in box.Text.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var xyz = triple.Split(',');
                    if (xyz.Length != 3 || !xyz.All(v => double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
                    {
                        _ed.Status("Yol okunamadı: her nokta \"x,y,z\" biçiminde, noktalar ';' ile ayrılmalı");
                        return;
                    }

                    pts.AddRange(xyz.Select(v => double.Parse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture)));
                }

                if (pts.Count < 6)
                {
                    _ed.Status("Yol en az iki nokta olmalı");
                    return;
                }

                if (path.SequenceEqual(pts)) return;
                _ed.Do("Süpürme yolu", () => spec.Path = pts);
            }

            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) Commit();
            };
            Props.Children.Add(box);
        }
    }
}
