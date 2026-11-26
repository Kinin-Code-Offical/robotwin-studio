using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Pcb;
using RobotWin.Studio.Theme;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [flyout] The tool strip's groups (KiCad's and Blender's flyouts): drawing (line, rectangle, circle, arc, polygon) and
    /// holes (mounting hole, via); and the options bar of the active tool at the canvas' top left.
    /// </summary>
    public partial class PcbPage
    {
        private void InitToolGroups()
        {
            ToolFlyout.Attach(ToolDraw, new[]
            {
                new ToolChoice(nameof(PcbTool.Line), "Çizgi", "I.DrawLine"),
                new ToolChoice(nameof(PcbTool.Rectangle), "Dikdörtgen", "I.DrawRect"),
                new ToolChoice(nameof(PcbTool.Circle), "Daire", "I.DrawCircle"),
                new ToolChoice(nameof(PcbTool.Arc), "Yay", "I.DrawArc"),
                new ToolChoice(nameof(PcbTool.Polygon), "Çokgen", "I.DrawPolygon")
            });
            ToolFlyout.Attach(ToolRoute, new[]
            {
                new ToolChoice(nameof(PcbTool.Route), "Yol", "I.Wire", "X"),
                new ToolChoice(nameof(PcbTool.Tune), "Uzunluk ayarı (menderes)", "I.Meander")
            });
            ToolFlyout.Attach(ToolHole, new[]
            {
                new ToolChoice(nameof(PcbTool.Hole), "Montaj deliği", "I.Hole"),
                new ToolChoice(nameof(PcbTool.Via), "Via", "I.Via")
            });
        }

        /// <summary>[tuning] The length tuner's options: target (the pair's or group's, or a length), amplitude, spacing, corners.</summary>
        private void TuneOptions()
        {
            var t = Canvas.Tuning;
            var auto = new CheckBox { Content = "Hedef: çift / grup", IsChecked = Canvas.TuneTarget == null, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Hedef uzunluk fark çiftinin öteki yarısından ya da uzunluk grubunun en uzun hattından gelir" };
            var target = Number(Canvas.TuneTarget ?? 50, v => Canvas.TuneTarget = v, 0.1, 10000);
            target.Width = 60;
            target.IsEnabled = Canvas.TuneTarget != null;
            target.ToolTip = "Elle hedef uzunluk (mm)";
            auto.Click += (_, _) =>
            {
                Canvas.TuneTarget = auto.IsChecked == true ? null : 50;
                UpdateToolOptions();
            };
            var amp = Number(t.Amplitude, v => t.Amplitude = v, 0.1, 20);
            amp.Width = 50;
            amp.ToolTip = "Kıvrımların yoldan uzaklığı (mm); parça kısaysa en çoka kadar büyür";
            var max = Number(t.MaxAmplitude, v => t.MaxAmplitude = v, 0.1, 50);
            max.Width = 50;
            max.ToolTip = "En çok genlik (mm)";
            var gap = Number(t.Spacing, v => t.Spacing = v, 0, 20, true);
            gap.Width = 50;
            gap.ToolTip = "Kıvrım bacakları arası (mm); 0: hat sınıfının aralığından";
            var corner = Combo(new[] { MeanderCorner.Chamfered, MeanderCorner.Square }, t.Corner, c => t.Corner = c, c => Shell.Lang.T(c == MeanderCorner.Chamfered ? "45° köşe" : "Dik köşe"));
            corner.Width = 100;
            ToolOptionsRow.Children.Add(auto);
            ToolOptionsRow.Children.Add(Label("Elle"));
            ToolOptionsRow.Children.Add(target);
            ToolOptionsRow.Children.Add(Label("Genlik"));
            ToolOptionsRow.Children.Add(amp);
            ToolOptionsRow.Children.Add(Label("En çok", 6));
            ToolOptionsRow.Children.Add(max);
            ToolOptionsRow.Children.Add(Label("Aralık"));
            ToolOptionsRow.Children.Add(gap);
            ToolOptionsRow.Children.Add(Label("Köşe"));
            ToolOptionsRow.Children.Add(corner);
            ToolOptions.Visibility = Visibility.Visible;
        }

        private static TextBlock Label(string text, double left = 12) =>
            new TextBlock { Text = text, Style = S("Text.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 6, 0) };

        /// <summary>The options of the tool in use, over the board's top left corner; hidden for tools without any.</summary>
        private void UpdateToolOptions()
        {
            ToolOptionsRow.Children.Clear();
            if (Canvas.Tool == PcbTool.Tune)
            {
                TuneOptions();
                return;
            }

            if (!BoardCanvas.IsGraphicTool(Canvas.Tool))
            {
                ToolOptions.Visibility = Visibility.Collapsed;
                return;
            }

            var layers = new[] { GraphicLayer.SilkTop, GraphicLayer.SilkBottom, GraphicLayer.FabTop, GraphicLayer.FabBottom, GraphicLayer.Drawings };
            var layer = Combo(layers, Canvas.GraphicDrawLayer, l => Canvas.GraphicDrawLayer = l, l => Shell.Lang.T(BoardCanvas.LayerName(l)));
            layer.Width = 150;
            layer.ToolTip = "Yeni şekillerin çizildiği katman";
            var width = Number(Canvas.DrawWidth, v => Canvas.DrawWidth = v, 0.01, 10);
            width.Width = 60;
            width.ToolTip = "Yeni şekillerin çizgi kalınlığı (mm)";
            ToolOptionsRow.Children.Add(Label("Katman"));
            ToolOptionsRow.Children.Add(layer);
            ToolOptionsRow.Children.Add(Label("Kalınlık"));
            ToolOptionsRow.Children.Add(width);
            ToolOptionsRow.Children.Add(Label("mm", 4));
            if (Canvas.Tool is PcbTool.Rectangle or PcbTool.Circle or PcbTool.Polygon)
            {
                var filled = new CheckBox { Content = "Dolgulu", IsChecked = Canvas.DrawFilled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Dikdörtgen, daire ve çokgen içi dolu çizilir" };
                filled.Click += (_, _) => Canvas.DrawFilled = filled.IsChecked == true;
                ToolOptionsRow.Children.Add(filled);
            }

            ToolOptions.Visibility = Visibility.Visible;
        }
    }
}
