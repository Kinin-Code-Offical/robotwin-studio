using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RobotTwin.CoreSim.Firmware.Devices;

namespace RobotWin.Studio.Editor
{
    /// <summary>
    /// Pictures of live display modules (SSD1306 OLED, HD44780 character LCD) as
    /// bitmaps with visible dot structure, cached per device until its content changes.
    /// </summary>
    public static class DisplayRenderer
    {
        private sealed class Cache
        {
            public int Version = -1;
            public string Style = string.Empty;
            public WriteableBitmap? Bitmap;
        }

        private static readonly ConditionalWeakTable<object, Cache> Caches = new ConditionalWeakTable<object, Cache>();

        // ------------------------------------------------------------------ OLED

        /// <summary>OLED picture, 3 bitmap pixels per panel pixel with a dark gap.</summary>
        public static BitmapSource Oled(Ssd1306 oled, string color)
        {
            var cache = Caches.GetOrCreateValue(oled);
            if (cache.Bitmap != null && cache.Version == oled.Version && cache.Style == color) return cache.Bitmap;
            const int k = 3;
            int w = oled.Width * k, h = oled.Height * k;
            var bmp = cache.Bitmap != null && cache.Bitmap.PixelWidth == w && cache.Bitmap.PixelHeight == h ? cache.Bitmap : new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
            var pixels = new int[w * h];
            bool on = oled.PanelPowered;
            double contrast = 0.55 + 0.45 * oled.Contrast / 255.0;
            int off = unchecked((int)0xFF050506);
            for (int y = 0; y < oled.Height; y++)
            {
                uint lit = color switch
                {
                    "blue" => 0x5AB8FF,
                    "yellow-blue" => y < 16 ? 0xFFD23Cu : 0x5AB8FFu,
                    _ => 0xEEF4FF
                };
                int litArgb = unchecked((int)(0xFF000000 | Scale(lit, contrast)));
                for (int x = 0; x < oled.Width; x++)
                {
                    bool p = on && oled.GetPixel(x, y);
                    int c = p ? litArgb : off;
                    int bx = x * k, by = y * k;
                    for (int dy = 0; dy < k; dy++)
                    {
                        int row = (by + dy) * w + bx;
                        for (int dx = 0; dx < k; dx++)
                        {
                            pixels[row + dx] = dx == k - 1 || dy == k - 1 ? (p ? unchecked((int)(0xFF000000 | Scale((uint)litArgb & 0xFFFFFF, 0.45))) : off) : c;
                        }
                    }
                }
            }

            bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
            cache.Bitmap = bmp;
            cache.Version = oled.Version;
            cache.Style = color;
            return bmp;
        }

        private static uint Scale(uint rgb, double k)
        {
            uint r = (uint)Math.Min(255, ((rgb >> 16) & 0xFF) * k);
            uint g = (uint)Math.Min(255, ((rgb >> 8) & 0xFF) * k);
            uint b = (uint)Math.Min(255, (rgb & 0xFF) * k);
            return (r << 16) | (g << 8) | b;
        }

        // ------------------------------------------------------------------ LCD

        /// <summary>Character LCD picture: 5×8 dot cells, 1-dot gaps, backlight colour.</summary>
        public static BitmapSource Lcd(Pcf8574Lcd lcd, string backlight)
        {
            var cache = Caches.GetOrCreateValue(lcd);
            string style = backlight + (lcd.Backlight ? "+" : "-");
            if (cache.Bitmap != null && cache.Version == lcd.Version && cache.Style == style) return cache.Bitmap;
            const int k = 3;
            int cols = lcd.Columns, rows = lcd.Rows;
            int dotsX = cols * 6 + 1, dotsY = rows * 9 + 1;
            int w = dotsX * k, h = dotsY * k;
            var bmp = cache.Bitmap != null && cache.Bitmap.PixelWidth == w && cache.Bitmap.PixelHeight == h ? cache.Bitmap : new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
            bool green = backlight == "green";
            bool lightOn = lcd.Backlight;
            uint bg = green ? (lightOn ? 0x9CC43Au : 0x4A5A22u) : (lightOn ? 0x2F63E0u : 0x12204Au);
            uint dotOff = green ? (lightOn ? 0x92B935u : 0x45531Fu) : (lightOn ? 0x3A6EEAu : 0x172756u);
            uint dotOn = green ? 0x1C2710u : (lightOn ? 0xEAF2FFu : 0x6A7BA8u);
            var pixels = new int[w * h];
            int bgArgb = unchecked((int)(0xFF000000 | bg));
            for (int i = 0; i < pixels.Length; i++) pixels[i] = bgArgb;
            bool display = lcd.DisplayOn;
            int cursorRow = -1, cursorCol = -1;
            if (lcd.CursorOn && lcd.TryGetCursorCell(out var cr, out var cc))
            {
                cursorRow = cr;
                cursorCol = cc;
            }

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    byte[] glyph = display ? lcd.Glyph(lcd.CodeAt(r, c)) : new byte[8];
                    for (int gy = 0; gy < 8; gy++)
                    {
                        byte line = gy < glyph.Length ? glyph[gy] : (byte)0;
                        if (r == cursorRow && c == cursorCol && gy == 7) line = 0x1F;
                        for (int gx = 0; gx < 5; gx++)
                        {
                            bool lit = (line & (0x10 >> gx)) != 0;
                            int color = unchecked((int)(0xFF000000 | (lit ? dotOn : dotOff)));
                            int px = (1 + c * 6 + gx) * k, py = (1 + r * 9 + gy) * k;
                            for (int dy = 0; dy < k - 1; dy++)
                            {
                                for (int dx = 0; dx < k - 1; dx++) pixels[(py + dy) * w + px + dx] = color;
                            }
                        }
                    }
                }
            }

            bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
            cache.Bitmap = bmp;
            cache.Version = lcd.Version;
            cache.Style = style;
            return bmp;
        }

        /// <summary>Draws a bitmap into a rect without smoothing (crisp dots at any zoom).</summary>
        public static void DrawCrisp(DrawingContext dc, BitmapSource bitmap, Rect rect)
        {
            var group = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
            group.Children.Add(new ImageDrawing(bitmap, rect));
            dc.DrawDrawing(group);
        }
    }
}
