using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace RobotWin.Studio.Libraries
{
    // [libs] Renders the simple SVG that Fritzing breadboard views use (rect, circle, ellipse,
    // line, polyline, polygon, path; groups with transforms; flat fill/stroke colours) as a
    // WPF drawing. Gradients become their first stop colour; text and filters are skipped.

    public static class SvgFlatImage
    {
        public static DrawingImage? Render(string? svg, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(svg))
            {
                error = "SVG yok.";
                return null;
            }

            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                XDocument doc;
                using (var r = XmlReader.Create(new StringReader(svg!), settings)) doc = XDocument.Load(r);
                var root = doc.Root ?? throw new FormatException("boş SVG");
                var gradients = new Dictionary<string, Color>(StringComparer.Ordinal);
                foreach (var g in root.Descendants().Where(e => e.Name.LocalName == "linearGradient" || e.Name.LocalName == "radialGradient"))
                {
                    string id = (string?)g.Attribute("id") ?? string.Empty;
                    var stop = g.Elements().FirstOrDefault(e => e.Name.LocalName == "stop");
                    if (id.Length > 0 && stop != null && TryColor(Style(stop, "stop-color") ?? "#808080", gradients, out var c)) gradients[id] = c;
                }

                var group = new DrawingGroup();
                Walk(root, group, new Paint(), gradients, 0);
                var vb = ((string?)root.Attribute("viewBox") ?? string.Empty).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                if (vb.Length == 4 && vb[2] > 0 && vb[3] > 0)
                {
                    // Clip to the viewBox so stray geometry does not stretch the picture.
                    group.ClipGeometry = new RectangleGeometry(new Rect(vb[0], vb[1], vb[2], vb[3]));
                    var outer = new DrawingGroup();
                    outer.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(vb[0], vb[1], vb[2], vb[3]))));
                    outer.Children.Add(group);
                    group = outer;
                }

                if (group.Children.Count == 0) throw new FormatException("çizilebilir öğe yok");
                var img = new DrawingImage(group);
                img.Freeze();
                return img;
            }
            catch (Exception ex) when (ex is XmlException || ex is FormatException || ex is InvalidOperationException || ex is OverflowException || ex is ArgumentException)
            {
                error = "SVG çizilemedi: " + ex.Message;
                return null;
            }
        }

        private sealed class Paint
        {
            public string Fill = "#000000";
            public string Stroke = "none";
            public double StrokeWidth = 1;
            public double Opacity = 1;

            public Paint Copy() => (Paint)MemberwiseClone();
        }

        private static string? Style(XElement e, string name)
        {
            string? style = (string?)e.Attribute("style");
            if (style != null)
            {
                foreach (var part in style.Split(';'))
                {
                    int c = part.IndexOf(':');
                    if (c > 0 && part.Substring(0, c).Trim() == name) return part.Substring(c + 1).Trim();
                }
            }

            return (string?)e.Attribute(name);
        }

        private static double Num(string? s, double fallback = 0)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            var m = Regex.Match(s!, @"^\s*([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)");
            return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : fallback;
        }

        private static bool TryColor(string text, Dictionary<string, Color> gradients, out Color color)
        {
            color = Colors.Black;
            text = text.Trim();
            if (text.Length == 0 || text == "none" || text == "transparent") return false;
            var url = Regex.Match(text, @"url\(#([^)]+)\)");
            if (url.Success)
            {
                if (gradients.TryGetValue(url.Groups[1].Value, out color)) return true;
                color = Color.FromRgb(0x80, 0x80, 0x80);
                return true;
            }

            var rgb = Regex.Match(text, @"rgb\(\s*(\d+)%?\s*,\s*(\d+)%?\s*,\s*(\d+)%?\s*\)");
            if (rgb.Success)
            {
                color = Color.FromRgb((byte)Math.Min(255, int.Parse(rgb.Groups[1].Value, CultureInfo.InvariantCulture)), (byte)Math.Min(255, int.Parse(rgb.Groups[2].Value, CultureInfo.InvariantCulture)), (byte)Math.Min(255, int.Parse(rgb.Groups[3].Value, CultureInfo.InvariantCulture)));
                return true;
            }

            if (Regex.IsMatch(text, "^#[0-9a-fA-F]{3}$")) text = "#" + text[1] + text[1] + text[2] + text[2] + text[3] + text[3];
            try
            {
                color = (Color)ColorConverter.ConvertFromString(text);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        private static Transform? ParseTransform(string? t)
        {
            if (string.IsNullOrWhiteSpace(t)) return null;
            var m = Matrix.Identity;
            foreach (Match x in Regex.Matches(t!, @"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)"))
            {
                var a = Regex.Split(x.Groups[2].Value.Trim(), @"[\s,]+").Where(s => s.Length > 0).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                var n = Matrix.Identity;
                switch (x.Groups[1].Value)
                {
                    case "matrix" when a.Length == 6: n = new Matrix(a[0], a[1], a[2], a[3], a[4], a[5]); break;
                    case "translate": n.Translate(a.Length > 0 ? a[0] : 0, a.Length > 1 ? a[1] : 0); break;
                    case "scale": n.Scale(a.Length > 0 ? a[0] : 1, a.Length > 1 ? a[1] : (a.Length > 0 ? a[0] : 1)); break;
                    case "rotate" when a.Length >= 1:
                        if (a.Length >= 3) n.RotateAt(a[0], a[1], a[2]);
                        else n.Rotate(a[0]);
                        break;
                    case "skewX" when a.Length >= 1: n.Skew(a[0], 0); break;
                    case "skewY" when a.Length >= 1: n.Skew(0, a[0]); break;
                }

                m = n * m;
            }

            return new MatrixTransform(m);
        }

        private static void Walk(XElement e, DrawingGroup into, Paint inherited, Dictionary<string, Color> gradients, int depth)
        {
            if (depth > 64) return;
            var paint = inherited.Copy();
            if (Style(e, "fill") is string f) paint.Fill = f;
            if (Style(e, "stroke") is string s) paint.Stroke = s;
            if (Style(e, "stroke-width") is string sw) paint.StrokeWidth = Num(sw, paint.StrokeWidth);
            double opacity = Num(Style(e, "opacity"), 1) * Num(Style(e, "fill-opacity"), 1);
            paint.Opacity = inherited.Opacity * opacity;
            if (Style(e, "display") == "none" || Style(e, "visibility") == "hidden") return;

            string name = e.Name.LocalName;
            if (name == "defs" || name == "title" || name == "desc" || name == "metadata" || name == "text" || name == "style" || name == "clipPath" || name == "mask" ||
                name == "linearGradient" || name == "radialGradient" || name == "pattern" || name == "symbol" || name == "filter")
            {
                return;
            }

            var transform = ParseTransform((string?)e.Attribute("transform"));
            if (name == "svg" || name == "g" || name == "a" || name == "switch")
            {
                var g = new DrawingGroup();
                if (transform != null && depth > 0) g.Transform = transform;
                foreach (var c in e.Elements()) Walk(c, g, paint, gradients, depth + 1);
                if (g.Children.Count > 0) into.Children.Add(g);
                return;
            }

            Geometry? geo = null;
            double A(string n) => Num((string?)e.Attribute(n));
            try
            {
                switch (name)
                {
                    case "rect":
                        geo = new RectangleGeometry(new Rect(A("x"), A("y"), Math.Max(0, A("width")), Math.Max(0, A("height"))), A("rx"), A("ry") > 0 ? A("ry") : A("rx"));
                        break;
                    case "circle":
                        geo = new EllipseGeometry(new Point(A("cx"), A("cy")), A("r"), A("r"));
                        break;
                    case "ellipse":
                        geo = new EllipseGeometry(new Point(A("cx"), A("cy")), A("rx"), A("ry"));
                        break;
                    case "line":
                        geo = new LineGeometry(new Point(A("x1"), A("y1")), new Point(A("x2"), A("y2")));
                        break;
                    case "polyline":
                    case "polygon":
                    {
                        var nums = Regex.Matches((string?)e.Attribute("points") ?? string.Empty, @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?").Cast<Match>().Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
                        if (nums.Count < 4) return;
                        var fig = new PathFigure { StartPoint = new Point(nums[0], nums[1]), IsClosed = name == "polygon" };
                        for (int i = 2; i + 1 < nums.Count; i += 2) fig.Segments.Add(new LineSegment(new Point(nums[i], nums[i + 1]), true));
                        geo = new PathGeometry(new[] { fig });
                        break;
                    }

                    case "path":
                    {
                        string d = (string?)e.Attribute("d") ?? string.Empty;
                        if (d.Length == 0) return;
                        // WPF path markup is SVG's, but needs separators between "1-2" / ".5.5" style numbers.
                        d = Regex.Replace(d, @"(\d)-", "$1 -");
                        d = Regex.Replace(d, @"(\.\d+)(?=\.)", "$1 ");
                        geo = Geometry.Parse("F1 " + d);
                        break;
                    }

                    default:
                        return;
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidOperationException || ex is ArgumentException)
            {
                return;
            }

            if (geo == null) return;
            if (transform != null) geo.Transform = transform;
            Brush? fill = null;
            Pen? pen = null;
            if (name != "line" && name != "polyline" && TryColor(paint.Fill, gradients, out var fc)) fill = new SolidColorBrush(fc) { Opacity = paint.Opacity };
            if (TryColor(paint.Stroke, gradients, out var sc) && paint.StrokeWidth > 0) pen = new Pen(new SolidColorBrush(sc) { Opacity = paint.Opacity }, paint.StrokeWidth);
            if (fill == null && pen == null) return;
            into.Children.Add(new GeometryDrawing(fill, pen, geo));
        }
    }
}
