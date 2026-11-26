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
            ToolFlyout.Attach(ToolHole, new[]
            {
                new ToolChoice(nameof(PcbTool.Hole), "Montaj deliği", "I.Hole"),
                new ToolChoice(nameof(PcbTool.Via), "Via", "I.Via")
            });
        }

        /// <summary>The options of the tool in use, over the board's top left corner; hidden for tools without any.</summary>
        private void UpdateToolOptions()
        {
            ToolOptionsRow.Children.Clear();
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

            static TextBlock Label(string text, double left = 12) =>
                new TextBlock { Text = text, Style = S("Text.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 6, 0) };
        }
    }
}
