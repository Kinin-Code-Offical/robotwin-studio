using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Shell;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [plot] KiCad's Plot window for SVG and PDF: the format (PDF with a page per layer, an SVG per layer, or one SVG),
    /// the layers, colour or black, mirrored, holes open, the edge on every layer, the label. What is drawn is what the
    /// Gerbers carry (CoreSim Pcb/BoardPlot.cs). The choices are remembered.
    /// </summary>
    public partial class PcbPage
    {
        public void ShowPlotDialog()
        {
            var board = Board;
            var settings = AppSettings.Current;
            var layers = BoardPlot.Layers(board);
            var chosen = new HashSet<string>((settings.PcbPlotLayers ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            if (chosen.Count == 0 || !layers.Any(l => chosen.Contains(l.Id)))
                chosen = new HashSet<string>(layers.Where(l => l.Kind is PlotLayerKind.Copper or PlotLayerKind.Silkscreen or PlotLayerKind.Edge).Select(l => l.Id), StringComparer.OrdinalIgnoreCase);

            var panel = new StackPanel();
            panel.Children.Add(Caption("Her katman Gerber'deki çiziminin aynısıyla, 1:1 ölçekte çizilir: yazıcıdan basmak, toner transferi, belgeye eklemek ya da incelemek için.", 0));

            panel.Children.Add(Overline("Biçim", 12));
            var formats = new[] { ("pdf", "PDF: her katman bir sayfa"), ("svg", "SVG: her katman ayrı dosya"), ("svg1", "SVG: hepsi tek dosyada") };
            var formatBoxes = new List<RadioButton>();
            foreach (var (key, text) in formats)
            {
                var r = new RadioButton { Content = Lang.T(text), Tag = key, GroupName = "plotformat", IsChecked = settings.PcbPlotFormat == key, Margin = new Thickness(0, 2, 0, 2) };
                formatBoxes.Add(r);
                panel.Children.Add(r);
            }

            if (formatBoxes.All(r => r.IsChecked != true)) formatBoxes[0].IsChecked = true;

            panel.Children.Add(Overline("Katmanlar", 12));
            var layerBoxes = new List<(PlotLayer Layer, CheckBox Box)>();
            var grid = new UniformGrid { Columns = 2 };
            foreach (var l in layers)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var swatch = new Border { Width = 10, Height = 10, BorderThickness = new Thickness(1), Background = (Brush)new BrushConverter().ConvertFromString(l.Color)!, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                swatch.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");   // a black ink shows on a dark theme too
                row.Children.Add(swatch);
                row.Children.Add(new TextBlock { Text = l.Id, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                if (l.Title != l.Id) row.Children.Add(new TextBlock { Text = "  " + Lang.T(l.Title), Style = S("Text.Caption"), VerticalAlignment = VerticalAlignment.Center });
                var box = new CheckBox { Content = row, IsChecked = chosen.Contains(l.Id), Margin = new Thickness(0, 2, 8, 2), VerticalContentAlignment = VerticalAlignment.Center };
                layerBoxes.Add((l, box));
                grid.Children.Add(box);
            }

            panel.Children.Add(grid);
            var quick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            void Pick(Func<PlotLayer, bool> which)
            {
                foreach (var (l, b) in layerBoxes) b.IsChecked = which(l);
            }

            foreach (var (text, which) in new (string, Func<PlotLayer, bool>)[]
                     {
                         ("Hepsi", _ => true), ("Bakır", l => l.Kind == PlotLayerKind.Copper), ("Üst taraf", l => l.Side == CopperLayer.Top && l.Kind != PlotLayerKind.Graphics),
                         ("Alt taraf", l => l.Side == CopperLayer.Bottom || l.Kind == PlotLayerKind.Edge), ("Hiçbiri", _ => false)
                     })
            {
                var b = Button(Lang.T(text), (_, _) => Pick(which));
                b.Margin = new Thickness(0, 0, 6, 0);
                quick.Children.Add(b);
            }

            panel.Children.Add(quick);

            panel.Children.Add(Overline("Seçenekler", 12));
            var color = new CheckBox { Content = Lang.T("Renkli (kapalıyken her şey siyah: toner transferi, film)"), IsChecked = settings.PcbPlotColor, Margin = new Thickness(0, 2, 0, 2) };
            var mirror = new CheckBox { Content = Lang.T("Aynala"), IsChecked = settings.PcbPlotMirror, Margin = new Thickness(0, 2, 0, 2), ToolTip = Lang.T("Alt katmanı alttan bakılmış gibi ya da üst katmanı toner transferi için ters çizer.") };
            var holes = new CheckBox { Content = Lang.T("Delikleri açık çiz (gerçek çapında)"), IsChecked = settings.PcbPlotHoles, Margin = new Thickness(0, 2, 0, 2) };
            var edge = new CheckBox { Content = Lang.T("Kart kenarını her katmanda çiz"), IsChecked = settings.PcbPlotEdge, Margin = new Thickness(0, 2, 0, 2) };
            var label = new CheckBox { Content = Lang.T("Kart ve katman adını altına yaz"), IsChecked = settings.PcbPlotLabel, Margin = new Thickness(0, 2, 0, 2) };
            foreach (var c in new[] { color, mirror, holes, edge, label }) panel.Children.Add(c);

            var dlg = MakeDialog(Window.GetWindow(this), Lang.T("Çizdir (SVG / PDF)"), 560, new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 200) },
                out var ok, Lang.T("Çizdir…"));
            ok.Click += (_, _) =>
            {
                var picked = layerBoxes.Where(x => x.Box.IsChecked == true).Select(x => x.Layer).ToList();
                if (picked.Count == 0)
                {
                    W.Status = Lang.T("Çizilecek katman seçilmedi");
                    return;
                }

                string format = (string)formatBoxes.First(r => r.IsChecked == true).Tag;
                settings.PcbPlotLayers = string.Join(",", picked.Select(l => l.Id));
                settings.PcbPlotFormat = format;
                settings.PcbPlotColor = color.IsChecked == true;
                settings.PcbPlotMirror = mirror.IsChecked == true;
                settings.PcbPlotHoles = holes.IsChecked == true;
                settings.PcbPlotEdge = edge.IsChecked == true;
                settings.PcbPlotLabel = label.IsChecked == true;
                settings.Save();
                var options = new PlotOptions
                {
                    Color = settings.PcbPlotColor, Mirror = settings.PcbPlotMirror, DrillHoles = settings.PcbPlotHoles, EdgeOnEveryLayer = settings.PcbPlotEdge,
                    Label = settings.PcbPlotLabel, Title = string.IsNullOrWhiteSpace(W.Schematic.Title) ? "RobotWin" : W.Schematic.Title
                };
                if (Plot(board, picked, format, options)) dlg.DialogResult = true;
            };
            if (Unattended.Active)
            {
                dlg.ShowActivated = false;
                dlg.Show();
                dlg.UpdateLayout();
                App.SnapshotTarget = VisualTreeHelper.GetChildrenCount(dlg) > 0 ? VisualTreeHelper.GetChild(dlg, 0) as FrameworkElement : dlg.Content as FrameworkElement;
                return;
            }

            dlg.ShowDialog();
        }

        /// <summary>Asks where and writes the files; false when the user stepped back or it could not be written.</summary>
        private bool Plot(BoardDocument board, IReadOnlyList<PlotLayer> layers, string format, PlotOptions options)
        {
            string name = ProjectFileName();
            var save = new SaveFileDialog
            {
                Title = Lang.T("Çizimi kaydet"),
                Filter = Lang.Filter(format == "pdf" ? "PDF belgesi (*.pdf)|*.pdf" : "SVG vektör (*.svg)|*.svg"),
                FileName = name + (format == "pdf" ? "-plot.pdf" : format == "svg1" ? "-plot.svg" : ".svg")
            };
            if (save.ShowDialog(Window.GetWindow(this)) != true) return false;
            try
            {
                var written = new List<string>();
                if (format == "pdf")
                {
                    File.WriteAllBytes(save.FileName, BoardPlot.Pdf(board, layers, options));
                    written.Add(save.FileName);
                }
                else if (format == "svg1")
                {
                    File.WriteAllText(save.FileName, BoardPlot.Svg(board, layers, options), new UTF8Encoding(false));
                    written.Add(save.FileName);
                }
                else
                {
                    // KiCad's names: the chosen name with each layer's ("kart-F_Cu.svg")
                    string dir = Path.GetDirectoryName(save.FileName) ?? ".", stem = Path.GetFileNameWithoutExtension(save.FileName);
                    foreach (var l in layers)
                    {
                        string path = Path.Combine(dir, stem + "-" + l.FileName + ".svg");
                        File.WriteAllText(path, BoardPlot.Svg(board, l, options), new UTF8Encoding(false));
                        written.Add(path);
                    }
                }

                Canvas.Refresh();   // zones refilled for the plot are the board's now too
                W.Status = written.Count == 1
                    ? $"{layers.Count} katman çizildi → {Path.GetFileName(written[0])}"
                    : $"{written.Count} SVG dosyası yazıldı → {Path.GetDirectoryName(written[0])}";
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(Window.GetWindow(this)!, "Çizim kaydedilemedi", ex.Message, "Tamam");
                return false;
            }
        }
    }
}
