using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [badges] What a part comes with, at a glance (D3 "Kütüphane"): an electrical model that simulates, a PCB footprint,
    /// a 3D model, firmware support for a board, and whether its model leaves things out (complete, or partial with how
    /// many notes). Each badge: green when it is there, amber when partial, grey when not; its tooltip says what it means.
    /// </summary>
    public partial class LibraryPage
    {
        /// <summary>The part's model status as (badge, state, what it means); state: 2 there, 1 partial, 0 not.</summary>
        internal static List<(string Name, int State, string Tip)> ModelStatus(PartDefinition def, IReadOnlyDictionary<string, string> parameters)
        {
            var list = new List<(string, int, string)>();
            bool electrical = def.Build != null;
            list.Add(("Elektriksel model", electrical ? 2 : 0, electrical ? "Simülasyonda devrenin bir parçası olarak çalışır" : "Yalnız şema sembolü: simülasyonda bir şey yapmaz"));
            string footprint = def.ResolveFootprint(parameters);
            list.Add(("PCB kılıfı", footprint.Length > 0 ? 2 : 0, footprint.Length > 0 ? "Kılıf: " + footprint : "Karta yerleştirilecek kılıfı yok"));
            bool model3d;
            try
            {
                model3d = Models3D.ComponentModels.Build(def, parameters) is { Children.Count: > 0 } g && !g.Bounds.IsEmpty;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                model3d = false;
            }

            list.Add(("3B model", model3d ? 2 : 0, model3d ? "PCB'nin 3B görünümünde, montajda ve dünyada gerçek biçimiyle görünür" : "3B modeli yok"));
            if (Firmware.ArduinoCompiler.FqbnFor(def.Id) != null)
                list.Add(("Firmware", 2, "Programlanabilir kart: Kod sayfasında yazılır, derlenir ve simülasyonda çalışır"));
            var limits = ModelLimits.Of(def.Id);
            list.Add(("Model sınırları", limits.Count == 0 ? 2 : 1, limits.Count == 0 ? "Modelin bilinen bir eksiği not edilmedi" : $"Modelin {limits.Count} bilinen eksiği var (aşağıda)"));
            return list;
        }

        /// <summary>The badges in the part's detail, under its title.</summary>
        private void AddModelStatus(PartDefinition def, IReadOnlyDictionary<string, string> parameters)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            foreach (var (name, state, tip) in ModelStatus(def, parameters))
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, state == 2 ? "B.Success" : state == 1 ? "B.Warning" : "B.TextMuted");
                var text = new TextBlock { Text = Lang.T(name), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                text.SetResourceReference(TextBlock.ForegroundProperty, state == 0 ? "B.TextMuted" : "B.Text");
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(dot);
                content.Children.Add(text);
                var badge = new Border { Child = content, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(1), ToolTip = Lang.T(tip) };
                badge.SetResourceReference(Border.BackgroundProperty, "B.Surface");
                badge.SetResourceReference(Border.BorderBrushProperty, "B.Border");
                row.Children.Add(badge);
            }

            Detail.Children.Add(row);
        }
    }
}
