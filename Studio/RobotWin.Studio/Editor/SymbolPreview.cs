using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Editor
{
    /// <summary>Small fitted rendering of a part's schematic symbol (library lists, cards).</summary>
    public sealed class SymbolPreview : FrameworkElement
    {
        public static readonly DependencyProperty PresetIdProperty = DependencyProperty.Register(
            nameof(PresetId), typeof(string), typeof(SymbolPreview),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public string? PresetId
        {
            get => (string?)GetValue(PresetIdProperty);
            set => SetValue(PresetIdProperty, value);
        }

        public Color Ink { get; set; } = SymbolRenderer.SymbolColor;

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            var lib = PartLibrary.Default;
            var preset = PresetId != null ? lib.FindPreset(PresetId) : null;
            var def = preset != null ? lib.Find(preset.PartId) : (PresetId != null ? lib.Find(PresetId) : null);
            if (def == null || ActualWidth < 4) return;

            var parameters = lib.ResolveParameters(def, preset, null);
            double minX = -def.HalfWidth, maxX = def.HalfWidth, minY = -def.HalfHeight, maxY = def.HalfHeight;
            foreach (var pin in def.Pins)
            {
                minX = Math.Min(minX, pin.X);
                maxX = Math.Max(maxX, pin.X);
                minY = Math.Min(minY, pin.Y);
                maxY = Math.Max(maxY, pin.Y);
            }

            if (def.Symbol == SymbolKind.Led) minY -= 1.4;
            if (def.Symbol == SymbolKind.PowerRail) minY -= 0.8;
            double w = maxX - minX + 0.8, h = maxY - minY + 0.8;
            double scale = Math.Min(ActualWidth / w, ActualHeight / h);
            var part = new SchematicPart { PartId = def.Id };
            var origin = new Point(ActualWidth / 2 - (minX + maxX) / 2 * scale, ActualHeight / 2 - (minY + maxY) / 2 * scale);
            dc.PushTransform(new MatrixTransform(SymbolRenderer.PartMatrix(part, scale, origin)));
            var visual = def.Symbol == SymbolKind.Led ? new PartVisual { HasState = true, Brightness = 0.9 } : default;
            SymbolRenderer.Draw(dc, def, parameters, scale, Ink, visual);
            dc.Pop();
        }
    }
}
