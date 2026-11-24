using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace RobotWin.Studio.Models3D
{
    /// <summary>
    /// Frozen, shared materials for component models: matte and glossy plastics,
    /// specular metals, translucent epoxy and glass, and textured surfaces.
    /// Identical requests return the same instance, so meshes group by material.
    /// </summary>
    internal static class Mat
    {
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static readonly HashSet<Material> TransparentSet = new HashSet<Material>();
        private static readonly object Gate = new object();

        public static Color C(uint rgb, byte a = 255) => Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        public static bool IsTransparent(Material m)
        {
            lock (Gate) return TransparentSet.Contains(m);
        }

        private static Material Get(string key, Func<Material> make, bool transparent = false)
        {
            lock (Gate)
            {
                if (Cache.TryGetValue(key, out var m)) return m;
                m = make();
                m.Freeze();
                Cache[key] = m;
                if (transparent) TransparentSet.Add(m);
                return m;
            }
        }

        private static SolidColorBrush Brush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>Plastic: diffuse colour with a soft highlight.</summary>
        public static Material Plastic(Color c, double spec = 0.12, double power = 18) =>
            Get($"P{c}{spec:0.00}{power:0}", () => Group(c, spec, power));

        public static Material Plastic(uint rgb, double spec = 0.12, double power = 18) => Plastic(C(rgb), spec, power);

        /// <summary>Glossy moulded epoxy / lacquer.</summary>
        public static Material Gloss(uint rgb, double spec = 0.45) => Plastic(C(rgb), spec, 45);

        /// <summary>Metal: darker diffuse plus a strong tight highlight.</summary>
        public static Material Metal(uint rgb, double spec = 0.9, double power = 55) =>
            Get($"M{rgb:X6}{spec:0.00}{power:0}", () =>
            {
                var c = C(rgb);
                var g = new MaterialGroup();
                g.Children.Add(new DiffuseMaterial(Brush(Color.FromRgb((byte)(c.R * 0.78), (byte)(c.G * 0.78), (byte)(c.B * 0.78)))));
                g.Children.Add(new SpecularMaterial(Brush(Color.FromArgb((byte)(spec * 255), 255, 255, 255)), power));
                // A little self-light keeps metal from going black on shaded sides.
                g.Children.Add(new EmissiveMaterial(Brush(Color.FromRgb((byte)(c.R * 0.10), (byte)(c.G * 0.10), (byte)(c.B * 0.10)))));
                return g;
            });

        /// <summary>Translucent material (epoxy lens, glass, clear plastic).</summary>
        public static Material Clear(Color c, double spec = 0.8, double power = 60) =>
            Get($"T{c}{spec:0.00}{power:0}", () => Group(c, spec, power), true);

        public static Material Clear(uint rgb, byte alpha, double spec = 0.8) => Clear(C(rgb, alpha), spec);

        /// <summary>Self-lit colour (indicator LEDs that are on, backlights, glowing filaments).</summary>
        public static Material Lit(uint rgb) =>
            Get($"L{rgb:X6}", () =>
            {
                var g = new MaterialGroup();
                g.Children.Add(new DiffuseMaterial(Brush(C(rgb))));
                g.Children.Add(new EmissiveMaterial(Brush(C(rgb))));
                return g;
            });

        /// <summary>Texture-mapped surface (labels, markings, silkscreen).</summary>
        public static Material Image(string key, Func<BitmapSource> image, double spec = 0.0, bool transparent = false) =>
            Get("I" + key + spec.ToString("0.00", CultureInfo.InvariantCulture), () =>
            {
                var brush = new ImageBrush(image()) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
                brush.Freeze();
                var g = new MaterialGroup();
                g.Children.Add(new DiffuseMaterial(brush));
                if (spec > 0) g.Children.Add(new SpecularMaterial(Brush(Color.FromArgb((byte)(spec * 255), 255, 255, 255)), 40));
                return g;
            }, transparent);

        private static Material Group(Color c, double spec, double power)
        {
            var g = new MaterialGroup();
            g.Children.Add(new DiffuseMaterial(Brush(c)));
            if (spec > 0) g.Children.Add(new SpecularMaterial(Brush(Color.FromArgb((byte)Math.Min(255, spec * 255), 255, 255, 255)), power));
            return g;
        }

        // ------------------------------------------------------------------ palette

        public static Material Gold => Metal(0xE2B54A, 0.9, 60);
        public static Material Tin => Metal(0xD6D8DC, 0.85, 50);
        public static Material Nickel => Metal(0xBFC3CA, 0.9, 55);
        public static Material Steel => Metal(0xA6ABB3, 0.8, 45);
        public static Material Chrome => Metal(0xE4E7EC, 1.0, 80);
        public static Material Aluminium => Metal(0xC8CCD2, 0.7, 30);
        public static Material Brass => Metal(0xCDA650, 0.85, 50);
        public static Material Copper => Metal(0xC97A45, 0.85, 50);
        public static Material Solder => Metal(0xCDD0D4, 1.0, 90);
        public static Material BlackPlastic => Plastic(0x1E1E21, 0.10, 16);
        public static Material Epoxy => Plastic(0x1A1A1D, 0.30, 35);
        public static Material Rubber => Plastic(0x1B1B1C, 0.03, 8);
        public static Material WhitePlastic => Plastic(0xEDEDE8, 0.15, 20);
    }

    /// <summary>Cached bitmaps for markings, labels and printed surfaces.</summary>
    internal static class Tex
    {
        private static readonly Dictionary<string, BitmapSource> Cache = new Dictionary<string, BitmapSource>();
        private static readonly object Gate = new object();

        public static BitmapSource Get(string key, Func<BitmapSource> make)
        {
            lock (Gate)
            {
                if (Cache.TryGetValue(key, out var b)) return b;
                b = make();
                if (!b.IsFrozen) b.Freeze();
                Cache[key] = b;
                return b;
            }
        }

        public static BitmapSource Render(int w, int h, Action<DrawingContext> draw)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) draw(dc);
            var bmp = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        public static FormattedText Text(string text, double size, Color color, bool bold = false, string font = "Segoe UI") =>
            new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily(font), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.SemiBold, FontStretches.Normal),
                size, new SolidColorBrush(color), 1.0);

        /// <summary>
        /// Text label bitmap of the given aspect ratio (width / height); lines split on '\n',
        /// centred, scaled to fit. Background may be transparent.
        /// </summary>
        public static BitmapSource Label(string text, double aspect, Color fg, Color bg, bool bold = false, string font = "Segoe UI", int height = 128)
        {
            string key = $"lbl|{text}|{aspect:0.###}|{fg}|{bg}|{bold}|{font}|{height}";
            return Get(key, () =>
            {
                int h = height, w = Math.Max(4, (int)Math.Round(h * aspect));
                var lines = text.Split('\n');
                return Render(w, h, dc =>
                {
                    dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, w, h));
                    double lineH = h / (double)lines.Length;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Length == 0) continue;
                        var ft = Text(lines[i], lineH * 0.78, fg, bold, font);
                        double scale = Math.Min(1.0, (w * 0.94) / Math.Max(1, ft.WidthIncludingTrailingWhitespace));
                        dc.PushTransform(new TranslateTransform(w / 2.0, lineH * i + lineH / 2));
                        dc.PushTransform(new ScaleTransform(scale, scale));
                        dc.DrawText(ft, new Point(-ft.WidthIncludingTrailingWhitespace / 2, -ft.Height / 2));
                        dc.Pop();
                        dc.Pop();
                    }
                });
            });
        }

        /// <summary>Material showing a text label (laser-etched marking, silkscreen, sticker).</summary>
        public static Material LabelMat(string text, double aspect, Color fg, Color bg, bool bold = false, string font = "Segoe UI", int height = 128)
        {
            bool transparent = bg.A < 255;
            string key = $"{text}|{aspect:0.###}|{fg}|{bg}|{bold}|{font}|{height}";
            return Mat.Image(key, () => Label(text, aspect, fg, bg, bold, font, height), 0, transparent);
        }
    }
}
