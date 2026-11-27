using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [image] A picture kept in the file as base64 PNG (KiCad's image on a sheet or a board): its size in pixels from the
    /// PNG header, and its printed size at KiCad's 300 pixels per inch.
    /// </summary>
    public static class EmbeddedImage
    {
        /// <summary>KiCad lays a picture out at 300 pixels per inch unless it says otherwise.</summary>
        public const double PixelsPerInch = 300;

        /// <summary>The PNG's width and height in pixels, read from its header; false when it is not a PNG.</summary>
        public static bool TrySize(string? base64, out int width, out int height)
        {
            width = height = 0;
            if (string.IsNullOrEmpty(base64)) return false;
            byte[] head;
            try
            {
                // the first 24 bytes are enough: 32 base64 characters
                head = Convert.FromBase64String(base64!.Length > 32 ? base64.Substring(0, 32) : base64);
            }
            catch (FormatException)
            {
                return false;
            }

            if (head.Length < 24 || head[0] != 0x89 || head[1] != 'P' || head[2] != 'N' || head[3] != 'G') return false;
            width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            return width > 0 && height > 0;
        }

        /// <summary>The picture's printed size (mm) at the given scale.</summary>
        public static (double Width, double Height) SizeMm(string? base64, double scale = 1)
        {
            if (!TrySize(base64, out int w, out int h)) return (0, 0);
            return (w / PixelsPerInch * 25.4 * scale, h / PixelsPerInch * 25.4 * scale);
        }

        /// <summary>The base64 text cut into KiCad's lines (76 characters), each written as its own string.</summary>
        public static IEnumerable<string> Lines(string base64)
        {
            for (int i = 0; i < base64.Length; i += 76) yield return base64.Substring(i, Math.Min(76, base64.Length - i));
        }
    }
}
