using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RobotWin.Studio.Branding
{
    /// <summary>
    /// The RobotWin mark, drawn in a single colour: a ring holding a two-joint
    /// linkage (base joint, arm, elbow) whose elbow continues as a PCB trace
    /// that bends 45° into a via.
    /// </summary>
    public static class Logo
    {
        public static readonly Color Ink = Color.FromRgb(0xED, 0xED, 0xEF);
        public static readonly Color Hole = Color.FromRgb(0x0E, 0x0E, 0x10);

        private static DrawingImage? _image;

        /// <summary>The mark on a transparent background (100×100 design units).</summary>
        public static DrawingImage Image => _image ??= CreateImage();

        private static DrawingImage CreateImage()
        {
            var group = new DrawingGroup();
            using (var dc = group.Open()) Draw(dc, Ink, Hole);
            group.Freeze();
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        /// <summary>The golden ratio φ; every proportion of the mark is a power of it.</summary>
        public const double Phi = 1.6180339887498949;

        private static double P(int n) => Math.Pow(Phi, n);

        /// <summary>
        /// Draws the mark on a 100×100 field. Golden-ratio construction:
        /// ring radius R = 42, ring stroke R/φ⁵; arm width R/φ⁴, trace width R/φ⁵;
        /// base joint radius R/φ³ → elbow R/φ⁴ → via R/φ⁵ (each 1/φ of the previous);
        /// base joint centre at (−R/φ², +R/φ²) and elbow at (+R/φ³, −R/φ³) from the ring centre;
        /// trace legs R/φ³ then R/φ⁴; holes are 1/φ² of their pads.
        /// </summary>
        public static void Draw(DrawingContext dc, Color ink, Color hole)
        {
            var inkBrush = new SolidColorBrush(ink);
            var holeBrush = new SolidColorBrush(hole);
            inkBrush.Freeze();
            holeBrush.Freeze();
            Pen Stroke(double w)
            {
                var p = new Pen(inkBrush, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                p.Freeze();
                return p;
            }

            const double c = 50, R = 42;
            double ring = R / P(5), arm = R / P(4), trace = R / P(5);
            double rBase = R / P(3), rElbow = R / P(4), rVia = R / P(5);
            var center = new Point(c, c);
            var basePoint = new Point(c - R / P(2), c + R / P(2));
            var elbow = new Point(c + R / P(3), c - R / P(3));
            double d = Math.Sqrt(0.5);
            var bend = new Point(elbow.X + R / P(3), elbow.Y);
            var end = new Point(bend.X + d * R / P(4), bend.Y + d * R / P(4));
            var via = new Point(end.X + d * rVia, end.Y + d * rVia);

            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 100, 100));
            dc.DrawEllipse(null, Stroke(ring), center, R, R);

            var t = new StreamGeometry();
            using (var g = t.Open())
            {
                g.BeginFigure(elbow, false, false);
                g.LineTo(bend, true, true);
                g.LineTo(end, true, true);
            }

            t.Freeze();
            dc.DrawGeometry(null, Stroke(trace), t);
            dc.DrawEllipse(holeBrush, Stroke(trace / Phi), via, rVia, rVia);
            dc.DrawEllipse(inkBrush, null, via, rVia / P(2), rVia / P(2));

            dc.DrawLine(Stroke(arm), basePoint, elbow);
            dc.DrawEllipse(holeBrush, Stroke(arm * 0.9), basePoint, rBase, rBase);
            dc.DrawEllipse(inkBrush, null, basePoint, rBase / P(2), rBase / P(2));
            dc.DrawEllipse(inkBrush, null, elbow, rElbow * Phi, rElbow * Phi);
            dc.DrawEllipse(holeBrush, null, elbow, rElbow * Phi / P(2), rElbow * Phi / P(2));
        }

        /// <summary>Tile colour of the app icon: flat near-black with a hint of grey.</summary>
        public static readonly Color TileColor = Color.FromRgb(0x1C, 0x1C, 0x1F);

        public static Brush TileBrush()
        {
            var b = new SolidColorBrush(TileColor);
            b.Freeze();
            return b;
        }

        /// <summary>App-icon rendering: the light mark on a flat near-black rounded tile.</summary>
        public static BitmapSource Render(int size)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                double s = size / 100.0;
                dc.PushTransform(new ScaleTransform(s, s));
                // Golden tile: corner radius = side/φ³, ring outer diameter = side/φ, centred.
                var tile = new Rect(2, 2, 96, 96);
                double radius = 96 / P(3);
                dc.DrawRoundedRectangle(TileBrush(), null, tile, radius, radius);
                dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38)), 1.2), tile, radius, radius);
                double markScale = 96 / Phi / (2 * 42 + 42 / P(5));
                dc.PushTransform(new TranslateTransform(50 - 50 * markScale, 50 - 50 * markScale));
                dc.PushTransform(new ScaleTransform(markScale, markScale));
                Draw(dc, Color.FromRgb(0xF2, 0xF2, 0xF4), TileColor);
                dc.Pop();
                dc.Pop();
                dc.Pop();
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }

        public static byte[] EncodePng(BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        /// <summary>Writes a multi-resolution .ico (PNG-compressed frames).</summary>
        public static void WriteIco(string path, params int[] sizes)
        {
            if (sizes.Length == 0) sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
            var frames = new List<byte[]>();
            foreach (int size in sizes) frames.Add(EncodePng(Render(size)));

            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write((ushort)0);
            w.Write((ushort)1);
            w.Write((ushort)frames.Count);
            int offset = 6 + 16 * frames.Count;
            for (int i = 0; i < frames.Count; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write(frames[i].Length);
                w.Write(offset);
                offset += frames[i].Length;
            }

            foreach (var frame in frames) w.Write(frame);
        }

        public static void Export(string directory)
        {
            Directory.CreateDirectory(directory);
            foreach (int size in new[] { 32, 64, 128, 256, 512, 1024 })
            {
                File.WriteAllBytes(Path.Combine(directory, $"robotwin-logo-{size}.png"), EncodePng(Render(size)));
            }

            WriteIco(Path.Combine(directory, "robotwin.ico"));

            // File-type icons, plus a preview sheet (256 / 48 / 32 / 16 px per type).
            var types = Shell.FileIcons.Extensions;
            foreach (var ext in types)
            {
                File.WriteAllBytes(Path.Combine(directory, $"filetype{ext}-256.png"), EncodePng(Shell.FileIcons.Render(ext, 256)));
            }

            const int cell = 300;
            var sheet = new DrawingVisual();
            using (var dc = sheet.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E)), null, new Rect(0, 0, cell * types.Length, 420));
                for (int i = 0; i < types.Length; i++)
                {
                    double x = i * cell;
                    dc.DrawImage(Shell.FileIcons.Render(types[i], 256), new Rect(x + 22, 10, 256, 256));
                    dc.DrawImage(Shell.FileIcons.Render(types[i], 48), new Rect(x + 40, 300, 48, 48));
                    dc.DrawImage(Shell.FileIcons.Render(types[i], 32), new Rect(x + 120, 308, 32, 32));
                    dc.DrawImage(Shell.FileIcons.Render(types[i], 16), new Rect(x + 190, 316, 16, 16));
                    var ft = new FormattedText(types[i], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Consolas"), 18, Brushes.White, 1.0);
                    dc.DrawText(ft, new Point(x + cell / 2 - ft.Width / 2, 372));
                }
            }

            var bmp = new RenderTargetBitmap(cell * types.Length, 420, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(sheet);
            File.WriteAllBytes(Path.Combine(directory, "filetypes-sheet.png"), EncodePng(bmp));
        }
    }
}
