using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RobotWin.Studio.Editor
{
    /// <summary>Writes a rendered sheet (a WPF drawing) as PNG or SVG.</summary>
    public static class SchematicExport
    {
        public static void SavePng(Drawing drawing, Size size, string path, double scale = 2.0)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawDrawing(drawing);
                dc.Pop();
            }

            var bmp = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            enc.Save(fs);
        }

        /// <summary>
        /// Vector SVG: every geometry is flattened with its accumulated transform and written as a
        /// path; text is converted to outlines so the file looks the same without the fonts.
        /// </summary>
        public static string ToSvg(Drawing drawing, Size size, string? title = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0:0}\" height=\"{1:0}\" viewBox=\"0 0 {0:0} {1:0}\">", size.Width, size.Height));
            if (!string.IsNullOrEmpty(title)) sb.AppendLine("<title>" + System.Security.SecurityElement.Escape(title) + "</title>");
            Walk(drawing, Matrix.Identity, 1.0, sb);
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        private static void Walk(Drawing d, Matrix m, double opacity, StringBuilder sb)
        {
            switch (d)
            {
                case DrawingGroup g:
                    {
                        var mm = g.Transform != null ? g.Transform.Value * m : m;
                        double op = opacity * g.Opacity;
                        if (op <= 0.01) return;
                        foreach (var child in g.Children) Walk(child, mm, op, sb);
                        break;
                    }

                case GeometryDrawing gd when gd.Geometry != null:
                    {
                        string data = PathData(gd.Geometry, m);
                        if (data.Length == 0) return;
                        string fill = Paint(gd.Brush, opacity, "fill", out string fillOpacity);
                        string stroke = "none", strokeOpacity = string.Empty, width = string.Empty, caps = string.Empty;
                        if (gd.Pen != null && gd.Pen.Thickness > 0)
                        {
                            stroke = Paint(gd.Pen.Brush, opacity, "stroke", out strokeOpacity);
                            double scale = Math.Sqrt(Math.Abs(m.Determinant));
                            width = string.Format(CultureInfo.InvariantCulture, " stroke-width=\"{0:0.###}\"", gd.Pen.Thickness * scale);
                            caps = gd.Pen.StartLineCap == PenLineCap.Round ? " stroke-linecap=\"round\" stroke-linejoin=\"round\"" : string.Empty;
                            if (gd.Pen.DashStyle != null && gd.Pen.DashStyle.Dashes.Count > 0)
                            {
                                var parts = new List<string>();
                                foreach (double dash in gd.Pen.DashStyle.Dashes) parts.Add((dash * gd.Pen.Thickness * scale).ToString("0.##", CultureInfo.InvariantCulture));
                                caps += " stroke-dasharray=\"" + string.Join(",", parts) + "\"";
                            }
                        }

                        if (fill == "none" && stroke == "none") return;
                        sb.Append("<path d=\"").Append(data).Append("\" fill=\"").Append(fill).Append('"').Append(fillOpacity)
                          .Append(" stroke=\"").Append(stroke).Append('"').Append(strokeOpacity).Append(width).Append(caps).AppendLine("/>");
                        break;
                    }

                case GlyphRunDrawing grd when grd.GlyphRun != null:
                    {
                        var geo = grd.GlyphRun.BuildGeometry();
                        string data = PathData(geo, m);
                        if (data.Length == 0) return;
                        string fill = Paint(grd.ForegroundBrush, opacity, "fill", out string fillOpacity);
                        sb.Append("<path d=\"").Append(data).Append("\" fill=\"").Append(fill).Append('"').Append(fillOpacity).AppendLine("/>");
                        break;
                    }
            }
        }

        private static string Paint(Brush? brush, double opacity, string kind, out string opacityAttr)
        {
            opacityAttr = string.Empty;
            Color c;
            switch (brush)
            {
                case SolidColorBrush s: c = s.Color; break;
                case GradientBrush g when g.GradientStops.Count > 0: c = g.GradientStops[0].Color; break;
                default: return "none";
            }

            double a = c.A / 255.0 * opacity * (brush.Opacity);
            if (a <= 0.01) return "none";
            if (a < 0.999) opacityAttr = string.Format(CultureInfo.InvariantCulture, " {0}-opacity=\"{1:0.###}\"", kind, a);
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        private static string PathData(Geometry geometry, Matrix m)
        {
            var flat = geometry.GetFlattenedPathGeometry(0.1, ToleranceType.Absolute);
            var sb = new StringBuilder();
            foreach (var fig in flat.Figures)
            {
                var start = m.Transform(fig.StartPoint);
                sb.Append(string.Format(CultureInfo.InvariantCulture, "M{0:0.##} {1:0.##}", start.X, start.Y));
                foreach (var seg in fig.Segments)
                {
                    IEnumerable<Point> pts = seg switch
                    {
                        LineSegment ls => new[] { ls.Point },
                        PolyLineSegment pl => pl.Points,
                        _ => Array.Empty<Point>()
                    };
                    foreach (var p in pts)
                    {
                        var q = m.Transform(p);
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "L{0:0.##} {1:0.##}", q.X, q.Y));
                    }
                }

                if (fig.IsClosed) sb.Append('Z');
            }

            return sb.ToString();
        }
    }
}
