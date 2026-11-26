using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>[gerber-check] An aperture as a Gerber file defines it (%ADD…%).</summary>
    public sealed class GerberAperture
    {
        public char Kind { get; set; }          // 'C' circle, 'R' rectangle, 'O' obround, 'P' regular polygon, 'M' macro (not drawn)
        public double W { get; set; }           // diameter for C and P (outer), width for R and O
        public double H { get; set; }           // height for R and O
        public int Vertices { get; set; }       // P
        public double Rotation { get; set; }    // P, degrees
        public double Hole { get; set; }        // round hole in the middle (0 = none)
        public string? Macro { get; set; }
    }

    /// <summary>[gerber-check] One graphical object of a Gerber image, in the file's own frame (mm, Y up).</summary>
    public sealed class GerberObject
    {
        public GerberObjectKind Kind { get; set; }
        public bool Dark { get; set; } = true;
        public GerberAperture? Aperture { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double CX { get; set; }
        public double CY { get; set; }
        public bool Clockwise { get; set; }

        /// <summary>A region's contours (arcs already followed in short chords).</summary>
        public List<List<(double X, double Y)>> Contours { get; } = new List<List<(double, double)>>();
    }

    public enum GerberObjectKind
    {
        Draw,
        Arc,
        Flash,
        Region,
    }

    /// <summary>[gerber-check] What a Gerber file says, read without anything from the writer: objects, the file function, and what was not understood.</summary>
    public sealed class GerberImage
    {
        public List<GerberObject> Objects { get; } = new List<GerberObject>();
        public List<string> Warnings { get; } = new List<string>();
        public string FileFunction { get; set; } = string.Empty;
        public string FilePolarity { get; set; } = "Positive";
        public bool Ended { get; set; }
    }

    /// <summary>
    /// [gerber-check] An independent reader of RS-274X (Gerber X2) and Excellon: a second opinion on the files the board
    /// exports, written from the specifications (Ucamco's Gerber Layer Format, rev. 2023.08; the Excellon format) and
    /// sharing no code with the writer. Coordinate formats, units, apertures C/R/O/P with holes, linear and circular
    /// interpolation (G74 single and G75 multi quadrant), regions, polarity; macros, step-and-repeat and image transforms
    /// are reported, not drawn.
    /// </summary>
    public static class GerberReader
    {
        public static GerberImage Parse(string text)
        {
            var image = new GerberImage();
            var apertures = new Dictionary<int, GerberAperture>();
            int intDigits = 4, decDigits = 6;
            bool trailingZeros = false;
            double unit = 1.0;   // mm per file unit
            bool unitsSet = false, formatSet = false;
            GerberAperture? current = null;
            double x = 0, y = 0;
            int interpolation = 1;          // 1 linear, 2 clockwise, 3 counter-clockwise
            bool multiQuadrant = false, region = false, dark = true;
            GerberObject? openRegion = null;
            List<(double, double)>? contour = null;

            double Coord(string digits)
            {
                if (digits.IndexOf('.') >= 0) return double.Parse(digits, CultureInfo.InvariantCulture) * unit;
                bool neg = digits.StartsWith("-", StringComparison.Ordinal);
                string d = digits.TrimStart('+', '-');
                if (trailingZeros) d = d.PadRight(intDigits + decDigits, '0');
                double v = d.Length == 0 ? 0 : double.Parse(d, CultureInfo.InvariantCulture) / Math.Pow(10, decDigits);
                return (neg ? -v : v) * unit;
            }

            void CloseContour()
            {
                if (contour != null && contour.Count >= 3 && openRegion != null) openRegion.Contours.Add(contour);
                contour = null;
            }

            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];
                if (char.IsWhiteSpace(ch))
                {
                    i++;
                    continue;
                }

                if (ch == '%')
                {
                    int end = text.IndexOf('%', i + 1);
                    if (end < 0)
                    {
                        image.Warnings.Add("kapanmamış genişletilmiş komut");
                        break;
                    }

                    string block = text.Substring(i + 1, end - i - 1);
                    i = end + 1;
                    string body = block.Replace("\r", string.Empty).Replace("\n", string.Empty).TrimEnd('*');
                    if (body.StartsWith("FS", StringComparison.Ordinal))
                    {
                        // FSLAX46Y46: L/T zero omission, A/I notation, X and Y digits
                        trailingZeros = body.Length > 2 && body[2] == 'T';
                        if (body.Length > 3 && body[3] == 'I') image.Warnings.Add("artımlı koordinatlar (FS…I) desteklenmiyor");
                        int xi = body.IndexOf('X');
                        if (xi > 0 && xi + 2 < body.Length)
                        {
                            intDigits = body[xi + 1] - '0';
                            decDigits = body[xi + 2] - '0';
                        }

                        formatSet = true;
                    }
                    else if (body.StartsWith("MO", StringComparison.Ordinal))
                    {
                        unit = body.Substring(2) == "IN" ? 25.4 : 1.0;
                        unitsSet = true;
                    }
                    else if (body.StartsWith("AD", StringComparison.Ordinal))
                    {
                        var ap = ParseAperture(body, out int code, unit, image);
                        if (code >= 10) apertures[code] = ap;
                    }
                    else if (body.StartsWith("AM", StringComparison.Ordinal))
                    {
                        // macro definitions are recognised; flashing one is reported where it happens
                    }
                    else if (body.StartsWith("LP", StringComparison.Ordinal))
                    {
                        dark = body.Substring(2) != "C";
                    }
                    else if (body.StartsWith("TF.FileFunction,", StringComparison.Ordinal))
                    {
                        image.FileFunction = body.Substring("TF.FileFunction,".Length);
                    }
                    else if (body.StartsWith("TF.FilePolarity,", StringComparison.Ordinal))
                    {
                        image.FilePolarity = body.Substring("TF.FilePolarity,".Length);
                    }
                    else if (body.StartsWith("SR", StringComparison.Ordinal) && body != "SR")
                    {
                        image.Warnings.Add("adım ve tekrar (SR) çizilmedi");
                    }
                    else if (body.StartsWith("LM", StringComparison.Ordinal) || body.StartsWith("LR", StringComparison.Ordinal) || body.StartsWith("LS", StringComparison.Ordinal))
                    {
                        if (body != "LMN" && body != "LR0" && body != "LS1") image.Warnings.Add("görüntü dönüşümü (" + body + ") çizilmedi");
                    }

                    continue;
                }

                int star = text.IndexOf('*', i);
                if (star < 0) break;
                string cmd = text.Substring(i, star - i).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
                i = star + 1;
                if (cmd.Length == 0) continue;
                if (cmd.StartsWith("G04", StringComparison.Ordinal)) continue;   // comment
                if (cmd == "M02" || cmd == "M00")
                {
                    image.Ended = true;
                    break;
                }

                // Leading G codes (several may share a block in older files: G01X…)
                int p = 0;
                while (p < cmd.Length && cmd[p] == 'G')
                {
                    int q = p + 1;
                    while (q < cmd.Length && char.IsDigit(cmd[q])) q++;
                    int g = int.Parse(cmd.Substring(p + 1, q - p - 1), CultureInfo.InvariantCulture);
                    switch (g)
                    {
                        case 1: interpolation = 1; break;
                        case 2: interpolation = 2; break;
                        case 3: interpolation = 3; break;
                        case 74: multiQuadrant = false; break;
                        case 75: multiQuadrant = true; break;
                        case 36:
                            region = true;
                            openRegion = new GerberObject { Kind = GerberObjectKind.Region, Dark = dark };
                            contour = null;
                            break;
                        case 37:
                            CloseContour();
                            if (openRegion != null && openRegion.Contours.Count > 0) image.Objects.Add(openRegion);
                            openRegion = null;
                            region = false;
                            break;
                        case 54: case 55: break;   // deprecated "select aperture" / "prepare flash" prefixes
                        case 70: unit = 25.4; unitsSet = true; break;
                        case 71: unit = 1.0; unitsSet = true; break;
                        case 90: break;
                        case 91: image.Warnings.Add("artımlı koordinatlar (G91) desteklenmiyor"); break;
                        default: image.Warnings.Add("bilinmeyen G" + g.ToString(CultureInfo.InvariantCulture)); break;
                    }

                    p = q;
                }

                if (p >= cmd.Length) continue;
                string rest = cmd.Substring(p);
                if (rest[0] == 'D' && rest.Length > 1 && char.IsDigit(rest[1]))
                {
                    int code = int.Parse(rest.Substring(1), CultureInfo.InvariantCulture);
                    if (code >= 10)
                    {
                        if (!apertures.TryGetValue(code, out current)) image.Warnings.Add("tanımsız açıklık D" + code.ToString(CultureInfo.InvariantCulture));
                        continue;
                    }
                }

                if (rest[0] == 'M') continue;

                // X…Y…I…J…Dnn
                double nx = x, ny = y, ii = 0, jj = 0;
                int op = -1;
                int k = 0;
                while (k < rest.Length)
                {
                    char letter = rest[k++];
                    int s = k;
                    while (k < rest.Length && (char.IsDigit(rest[k]) || rest[k] == '-' || rest[k] == '+' || rest[k] == '.')) k++;
                    string num = rest.Substring(s, k - s);
                    switch (letter)
                    {
                        case 'X': nx = Coord(num); break;
                        case 'Y': ny = Coord(num); break;
                        case 'I': ii = Coord(num); break;
                        case 'J': jj = Coord(num); break;
                        case 'D': op = int.Parse(num, CultureInfo.InvariantCulture); break;
                    }
                }

                if (!formatSet || !unitsSet)
                {
                    if (!formatSet) image.Warnings.Add("koordinat biçimi (FS) koordinatlardan önce tanımlanmadı");
                    if (!unitsSet) image.Warnings.Add("birim (MO) koordinatlardan önce tanımlanmadı");
                    formatSet = unitsSet = true;
                }

                if (op == 2)
                {
                    if (region)
                    {
                        CloseContour();
                        contour = new List<(double, double)> { (nx, ny) };
                    }
                }
                else if (op == 1)
                {
                    if (interpolation == 1)
                    {
                        if (region)
                        {
                            contour ??= new List<(double, double)> { (x, y) };
                            contour.Add((nx, ny));
                        }
                        else image.Objects.Add(new GerberObject { Kind = GerberObjectKind.Draw, Dark = dark, Aperture = current, X1 = x, Y1 = y, X2 = nx, Y2 = ny });
                    }
                    else
                    {
                        bool cw = interpolation == 2;
                        var (cx, cy) = multiQuadrant ? (x + ii, y + jj) : SingleQuadrantCentre(x, y, nx, ny, Math.Abs(ii), Math.Abs(jj), cw);
                        if (region)
                        {
                            contour ??= new List<(double, double)> { (x, y) };
                            foreach (var pt in ArcPoints(x, y, nx, ny, cx, cy, cw, multiQuadrant)) contour.Add(pt);
                        }
                        else image.Objects.Add(new GerberObject { Kind = GerberObjectKind.Arc, Dark = dark, Aperture = current, X1 = x, Y1 = y, X2 = nx, Y2 = ny, CX = cx, CY = cy, Clockwise = cw });
                    }
                }
                else if (op == 3)
                {
                    if (current?.Kind == 'M') image.Warnings.Add("makro açıklık (" + current.Macro + ") çizilmedi");
                    image.Objects.Add(new GerberObject { Kind = GerberObjectKind.Flash, Dark = dark, Aperture = current, X1 = nx, Y1 = ny });
                }

                x = nx;
                y = ny;
            }

            if (!image.Ended) image.Warnings.Add("dosya M02 ile bitmiyor");
            return image;
        }

        private static GerberAperture ParseAperture(string body, out int code, double unit, GerberImage image)
        {
            // ADD10C,0.5  ADD11R,1.2X0.8  ADD12O,1X2  ADD13P,1.5X6X30  ADD14RoundRect,…
            int s = 3;
            int e = s;
            while (e < body.Length && char.IsDigit(body[e])) e++;
            code = e > s ? int.Parse(body.Substring(s, e - s), CultureInfo.InvariantCulture) : -1;
            string tpl = body.Substring(e);
            int comma = tpl.IndexOf(',');
            string name = comma < 0 ? tpl : tpl.Substring(0, comma);
            var args = new List<double>();
            if (comma >= 0)
            {
                foreach (var a in tpl.Substring(comma + 1).Split('X'))
                    if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) args.Add(v);
            }

            double A(int n) => n < args.Count ? args[n] : 0;
            switch (name)
            {
                case "C": return new GerberAperture { Kind = 'C', W = A(0) * unit, H = A(0) * unit, Hole = A(1) * unit };
                case "R": return new GerberAperture { Kind = 'R', W = A(0) * unit, H = A(1) * unit, Hole = A(2) * unit };
                case "O": return new GerberAperture { Kind = 'O', W = A(0) * unit, H = A(1) * unit, Hole = A(2) * unit };
                case "P": return new GerberAperture { Kind = 'P', W = A(0) * unit, H = A(0) * unit, Vertices = (int)A(1), Rotation = A(2), Hole = A(3) * unit };
                default: return new GerberAperture { Kind = 'M', Macro = name };
            }
        }

        private static (double, double) SingleQuadrantCentre(double x1, double y1, double x2, double y2, double i, double j, bool cw)
        {
            // G74: the centre's offsets are unsigned; the right signs give an arc of at most 90° in the given direction
            (double, double) best = (x1 + i, y1 + j);
            double bestErr = double.MaxValue;
            foreach (var (si, sj) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
            {
                double cx = x1 + si * i, cy = y1 + sj * j;
                double r1 = Math.Sqrt((x1 - cx) * (x1 - cx) + (y1 - cy) * (y1 - cy)), r2 = Math.Sqrt((x2 - cx) * (x2 - cx) + (y2 - cy) * (y2 - cy));
                double sweep = Sweep(x1, y1, x2, y2, cx, cy, cw, false);
                if (sweep > Math.PI / 2 + 1e-6) continue;
                double err = Math.Abs(r1 - r2);
                if (err < bestErr)
                {
                    bestErr = err;
                    best = (cx, cy);
                }
            }

            return best;
        }

        /// <summary>The angle an arc turns through (radians, positive), a full turn when it ends where it began (G75).</summary>
        public static double Sweep(double x1, double y1, double x2, double y2, double cx, double cy, bool cw, bool multiQuadrant = true)
        {
            double a1 = Math.Atan2(y1 - cy, x1 - cx), a2 = Math.Atan2(y2 - cy, x2 - cx);
            double d = cw ? a1 - a2 : a2 - a1;
            while (d < 0) d += 2 * Math.PI;
            while (d >= 2 * Math.PI) d -= 2 * Math.PI;
            if (d < 1e-9 && multiQuadrant && (Math.Abs(x1 - x2) < 1e-9 && Math.Abs(y1 - y2) < 1e-9)) d = 2 * Math.PI;
            return d;
        }

        /// <summary>Points along an arc every ~2° (the end point included, the start left out).</summary>
        public static IEnumerable<(double X, double Y)> ArcPoints(double x1, double y1, double x2, double y2, double cx, double cy, bool cw, bool multiQuadrant = true)
        {
            double r = Math.Sqrt((x1 - cx) * (x1 - cx) + (y1 - cy) * (y1 - cy));
            double a1 = Math.Atan2(y1 - cy, x1 - cx);
            double sweep = Sweep(x1, y1, x2, y2, cx, cy, cw, multiQuadrant);
            int n = Math.Max(2, (int)Math.Ceiling(sweep / (Math.PI / 90)));
            for (int k = 1; k < n; k++)
            {
                double a = a1 + (cw ? -1 : 1) * sweep * k / n;
                yield return (cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            }

            yield return (x2, y2);
        }

        /// <summary>[gerber-check] Holes of an Excellon file (mm, the file's frame), and what was not understood.</summary>
        public static (List<(double D, double X, double Y)> Holes, List<string> Warnings) ParseExcellon(string text)
        {
            var holes = new List<(double, double, double)>();
            var warnings = new List<string>();
            var tools = new Dictionary<int, double>();
            double unit = 1.0;
            bool leadingZeros = false;   // LZ: leading zeros kept (trailing omitted); TZ: trailing kept
            int intDigits = 3, decDigits = 3;
            double tool = 0;
            bool header = false, slots = false;
            double x = 0, y = 0;
            foreach (var raw in text.Replace("\r", string.Empty).Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal)) continue;
                if (line == "M48")
                {
                    header = true;
                    continue;
                }

                if (line == "%" || line == "M95")
                {
                    header = false;
                    continue;
                }

                if (line.StartsWith("METRIC", StringComparison.Ordinal) || line.StartsWith("INCH", StringComparison.Ordinal))
                {
                    unit = line.StartsWith("INCH", StringComparison.Ordinal) ? 25.4 : 1.0;
                    leadingZeros = line.Contains(",LZ");
                    if (unit > 1) { intDigits = 2; decDigits = 4; }
                    var parts = line.Split(',');
                    foreach (var part in parts)
                    {
                        int dot = part.IndexOf('.');
                        if (dot > 0 && part.TrimStart('0', '.').Length >= 0 && part.Replace("0", string.Empty) == ".")
                        {
                            intDigits = dot;
                            decDigits = part.Length - dot - 1;
                        }
                    }

                    continue;
                }

                if (line.StartsWith("M71", StringComparison.Ordinal)) { unit = 1.0; continue; }
                if (line.StartsWith("M72", StringComparison.Ordinal)) { unit = 25.4; continue; }
                if (line == "M30" || line == "M00") break;
                if (line.StartsWith("G85", StringComparison.Ordinal) || line.Contains("G85"))
                {
                    slots = true;
                    continue;
                }

                if (line[0] == 'T')
                {
                    int c = 1;
                    while (c < line.Length && char.IsDigit(line[c])) c++;
                    int n = c > 1 ? int.Parse(line.Substring(1, c - 1), CultureInfo.InvariantCulture) : 0;
                    int ci = line.IndexOf('C', c);
                    if (ci >= 0)
                    {
                        int e = ci + 1;
                        while (e < line.Length && (char.IsDigit(line[e]) || line[e] == '.')) e++;
                        tools[n] = double.Parse(line.Substring(ci + 1, e - ci - 1), CultureInfo.InvariantCulture) * unit;
                    }

                    if (!header) tool = n == 0 ? 0 : tools.TryGetValue(n, out double d) ? d : 0;
                    continue;
                }

                if (header) continue;
                if (line[0] == 'X' || line[0] == 'Y')
                {
                    double Num(string s)
                    {
                        if (s.IndexOf('.') >= 0) return double.Parse(s, CultureInfo.InvariantCulture) * unit;
                        bool neg = s.StartsWith("-", StringComparison.Ordinal);
                        string d = s.TrimStart('+', '-');
                        d = leadingZeros ? d.PadRight(intDigits + decDigits, '0') : d.PadLeft(intDigits + decDigits, '0');
                        double v = double.Parse(d, CultureInfo.InvariantCulture) / Math.Pow(10, decDigits);
                        return (neg ? -v : v) * unit;
                    }

                    int xi = line.IndexOf('X'), yi = line.IndexOf('Y');
                    if (xi >= 0) x = Num(line.Substring(xi + 1, (yi > xi ? yi : line.Length) - xi - 1));
                    if (yi >= 0) y = Num(line.Substring(yi + 1, (xi > yi ? xi : line.Length) - yi - 1));
                    if (tool <= 0) warnings.Add("araç seçilmeden delik");
                    holes.Add((tool, x, y));
                    continue;
                }

                if (line.StartsWith("G", StringComparison.Ordinal) || line.StartsWith("M", StringComparison.Ordinal) || line.StartsWith("FMAT", StringComparison.Ordinal) || line.StartsWith("ICI", StringComparison.Ordinal)) continue;
                warnings.Add("anlaşılmayan satır: " + line);
            }

            if (slots) warnings.Add("yuvalar (G85) delik olarak sayılmadı");
            return (holes, warnings);
        }
    }

    /// <summary>[gerber-check] A black-and-white picture of an area at a fixed pixel size, painted with exact shape tests at pixel centres.</summary>
    public sealed class GerberRaster
    {
        public GerberRaster(double x0, double y0, double width, double height, double pixel)
        {
            X0 = x0;
            Y0 = y0;
            Pixel = pixel;
            W = Math.Max(1, (int)Math.Ceiling(width / pixel));
            H = Math.Max(1, (int)Math.Ceiling(height / pixel));
            Bits = new bool[W * H];
        }

        public double X0 { get; }
        public double Y0 { get; }
        public double Pixel { get; }
        public int W { get; }
        public int H { get; }
        public bool[] Bits { get; }

        public double Cx(int c) => X0 + (c + 0.5) * Pixel;

        public double Cy(int r) => Y0 + (r + 0.5) * Pixel;

        public int Count()
        {
            int n = 0;
            foreach (bool b in Bits) if (b) n++;
            return n;
        }

        private void Box(double minX, double minY, double maxX, double maxY, out int c0, out int c1, out int r0, out int r1)
        {
            c0 = Math.Max(0, (int)Math.Floor((minX - X0) / Pixel));
            c1 = Math.Min(W - 1, (int)Math.Ceiling((maxX - X0) / Pixel));
            r0 = Math.Max(0, (int)Math.Floor((minY - Y0) / Pixel));
            r1 = Math.Min(H - 1, (int)Math.Ceiling((maxY - Y0) / Pixel));
        }

        /// <summary>Sets (or clears) every pixel whose centre passes <paramref name="inside"/>, within a box.</summary>
        public void Paint(double minX, double minY, double maxX, double maxY, Func<double, double, bool> inside, bool value = true)
        {
            Box(minX, minY, maxX, maxY, out int c0, out int c1, out int r0, out int r1);
            for (int r = r0; r <= r1; r++)
            {
                double py = Cy(r);
                for (int c = c0; c <= c1; c++)
                    if (inside(Cx(c), py)) Bits[r * W + c] = value;
            }
        }

        public void Disc(double cx, double cy, double radius, bool value = true) =>
            Paint(cx - radius, cy - radius, cx + radius, cy + radius, (px, py) => (px - cx) * (px - cx) + (py - cy) * (py - cy) <= radius * radius, value);

        public void Capsule(double x1, double y1, double x2, double y2, double radius, bool value = true)
        {
            double dx = x2 - x1, dy = y2 - y1, len2 = dx * dx + dy * dy;
            Paint(Math.Min(x1, x2) - radius, Math.Min(y1, y2) - radius, Math.Max(x1, x2) + radius, Math.Max(y1, y2) + radius, (px, py) =>
            {
                double t = len2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((px - x1) * dx + (py - y1) * dy) / len2));
                double ex = x1 + t * dx - px, ey = y1 + t * dy - py;
                return ex * ex + ey * ey <= radius * radius;
            }, value);
        }

        public void Rect(double cx, double cy, double w, double h, bool value = true) =>
            Paint(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2, (px, py) => Math.Abs(px - cx) <= w / 2 && Math.Abs(py - cy) <= h / 2, value);

        public void RoundRect(double cx, double cy, double w, double h, double radius, bool value = true)
        {
            double hw = w / 2, hh = h / 2, rr = Math.Max(0, Math.Min(radius, Math.Min(hw, hh)));
            Paint(cx - hw, cy - hh, cx + hw, cy + hh, (px, py) =>
            {
                double ax = Math.Abs(px - cx) - (hw - rr), ay = Math.Abs(py - cy) - (hh - rr);
                if (ax <= 0 || ay <= 0) return Math.Abs(px - cx) <= hw && Math.Abs(py - cy) <= hh;
                return ax * ax + ay * ay <= rr * rr;
            }, value);
        }

        /// <summary>A ring segment drawn by a round pen along an arc (its round ends included).</summary>
        public void ArcStroke(double x1, double y1, double x2, double y2, double cx, double cy, bool cw, double radius, bool value = true)
        {
            double r = Math.Sqrt((x1 - cx) * (x1 - cx) + (y1 - cy) * (y1 - cy));
            double a1 = Math.Atan2(y1 - cy, x1 - cx);
            double sweep = GerberReader.Sweep(x1, y1, x2, y2, cx, cy, cw);
            Paint(cx - r - radius, cy - r - radius, cx + r + radius, cy + r + radius, (px, py) =>
            {
                double d = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                if (Math.Abs(d - r) > radius) return false;
                double a = Math.Atan2(py - cy, px - cx);
                double from = cw ? a1 - a : a - a1;
                while (from < 0) from += 2 * Math.PI;
                while (from >= 2 * Math.PI) from -= 2 * Math.PI;
                return from <= sweep + 1e-12;
            }, value);
            Disc(x1, y1, radius, value);
            Disc(x2, y2, radius, value);
        }

        /// <summary>Fills by the even-odd rule over all contours together (holes and Gerber's cut-in fractures both cancel).</summary>
        public void Polygon(IReadOnlyList<IReadOnlyList<(double X, double Y)>> contours, bool value = true)
        {
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var c in contours)
                foreach (var (_, py) in c)
                {
                    minY = Math.Min(minY, py);
                    maxY = Math.Max(maxY, py);
                }

            if (minY > maxY) return;
            Box(0, minY, 0, maxY, out _, out _, out int r0, out int r1);
            var xs = new List<double>();
            for (int r = r0; r <= r1; r++)
            {
                double py = Cy(r);
                xs.Clear();
                foreach (var c in contours)
                {
                    int n = c.Count;
                    for (int k = 0; k < n; k++)
                    {
                        var (ax, ay) = c[k];
                        var (bx, by) = c[(k + 1) % n];
                        if ((ay <= py && by > py) || (by <= py && ay > py)) xs.Add(ax + (py - ay) / (by - ay) * (bx - ax));
                    }
                }

                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int c0 = Math.Max(0, (int)Math.Ceiling((xs[k] - X0) / Pixel - 0.5));
                    int c1 = Math.Min(W - 1, (int)Math.Floor((xs[k + 1] - X0) / Pixel - 0.5));
                    for (int c = c0; c <= c1; c++) Bits[r * W + c] = value;
                }
            }
        }

        /// <summary>Paints a Gerber image's objects in order (clear polarity erases).</summary>
        public void Draw(GerberImage image)
        {
            foreach (var o in image.Objects)
            {
                var ap = o.Aperture;
                switch (o.Kind)
                {
                    case GerberObjectKind.Region:
                    {
                        var list = new List<IReadOnlyList<(double, double)>>();
                        foreach (var c in o.Contours) list.Add(c);
                        Polygon(list, o.Dark);
                        break;
                    }

                    case GerberObjectKind.Draw when ap != null:
                        if (ap.Kind == 'C') Capsule(o.X1, o.Y1, o.X2, o.Y2, ap.W / 2, o.Dark);
                        else if (ap.Kind == 'R')
                        {
                            // a rectangular pen sweeps the hull of its rectangle at both ends
                            double hw = ap.W / 2, hh = ap.H / 2;
                            var pts = new List<(double X, double Y)>();
                            foreach (var (px, py) in new[] { (o.X1, o.Y1), (o.X2, o.Y2) })
                            {
                                pts.Add((px - hw, py - hh));
                                pts.Add((px + hw, py - hh));
                                pts.Add((px + hw, py + hh));
                                pts.Add((px - hw, py + hh));
                            }

                            Polygon(new List<IReadOnlyList<(double, double)>> { Hull(pts) }, o.Dark);
                        }

                        break;
                    case GerberObjectKind.Arc when ap != null:
                        ArcStroke(o.X1, o.Y1, o.X2, o.Y2, o.CX, o.CY, o.Clockwise, ap.W / 2, o.Dark);
                        break;
                    case GerberObjectKind.Flash when ap != null:
                        switch (ap.Kind)
                        {
                            case 'C': Disc(o.X1, o.Y1, ap.W / 2, o.Dark); break;
                            case 'R': Rect(o.X1, o.Y1, ap.W, ap.H, o.Dark); break;
                            case 'O': RoundRect(o.X1, o.Y1, ap.W, ap.H, Math.Min(ap.W, ap.H) / 2, o.Dark); break;
                            case 'P':
                            {
                                var poly = new List<(double, double)>();
                                for (int k = 0; k < Math.Max(3, ap.Vertices); k++)
                                {
                                    double a = (ap.Rotation + 360.0 * k / Math.Max(3, ap.Vertices)) * Math.PI / 180;
                                    poly.Add((o.X1 + ap.W / 2 * Math.Cos(a), o.Y1 + ap.W / 2 * Math.Sin(a)));
                                }

                                Polygon(new List<IReadOnlyList<(double, double)>> { poly }, o.Dark);
                                break;
                            }
                        }

                        if (ap.Hole > 0 && ap.Kind != 'M') Disc(o.X1, o.Y1, ap.Hole / 2, !o.Dark);
                        break;
                }
            }
        }

        private static List<(double X, double Y)> Hull(List<(double X, double Y)> pts)
        {
            pts.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            var hull = new List<(double X, double Y)>();
            double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            foreach (var p in pts)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            int lower = hull.Count + 1;
            for (int k = pts.Count - 2; k >= 0; k--)
            {
                var p = pts[k];
                while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
            return hull;
        }
    }
}
