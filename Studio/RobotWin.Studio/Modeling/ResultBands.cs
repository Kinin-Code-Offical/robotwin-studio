using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RobotWin.Studio.Modeling
{
    /// <summary>[part-study] The result colours: ten bands, low (blue) to high (red), as structural results are coloured.</summary>
    internal static class ResultBands
    {
        public static readonly Color[] Colors =
        {
            Color.FromRgb(0x2B, 0x59, 0xC3), Color.FromRgb(0x2F, 0x8B, 0xD6), Color.FromRgb(0x2F, 0xB4, 0xC8), Color.FromRgb(0x3C, 0xBF, 0x8A), Color.FromRgb(0x7C, 0xC8, 0x4A),
            Color.FromRgb(0xC8, 0xD2, 0x3A), Color.FromRgb(0xF2, 0xC9, 0x4C), Color.FromRgb(0xF2, 0x9E, 0x3D), Color.FromRgb(0xE9, 0x69, 0x3A), Color.FromRgb(0xD6, 0x3A, 0x3A),
        };

        /// <summary>The bands as a texture: each band many pixels wide, so the 3D view's texture filtering blends only at the band edges.</summary>
        public static BitmapSource Texture()
        {
            const int per = 32;
            int w = Colors.Length * per;
            var pixels = new byte[w * 4];
            for (int x = 0; x < w; x++)
            {
                var c = Colors[x / per];
                pixels[4 * x] = c.B;
                pixels[4 * x + 1] = c.G;
                pixels[4 * x + 2] = c.R;
                pixels[4 * x + 3] = 255;
            }

            var bmp = BitmapSource.Create(w, 1, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
            bmp.Freeze();
            return bmp;
        }
    }
}
