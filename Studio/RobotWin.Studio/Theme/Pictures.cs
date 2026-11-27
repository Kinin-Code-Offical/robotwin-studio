using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RobotWin.Studio.Theme
{
    /// <summary>
    /// [image] Pictures kept in the project as base64 PNG (a logo on the sheet, a reference photo under the board): picked
    /// from a file and turned into PNG (the largest side held to 2048 pixels, so a camera photo does not swell the file), and
    /// decoded once for drawing.
    /// </summary>
    public static class Pictures
    {
        private const int MaxSide = 2048;
        private static readonly ConditionalWeakTable<string, BitmapSource> Decoded = new ConditionalWeakTable<string, BitmapSource>();

        /// <summary>Asks for a picture file; its PNG as base64, or null when none was picked or it does not read.</summary>
        public static string? Pick(Window? owner)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Shell.Lang.T("Resim seçin"),
                Filter = Shell.Lang.T("Resimler") + " (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
            };
            if (Shell.Unattended.Active || dialog.ShowDialog(owner) != true) return null;
            try
            {
                return FromFile(dialog.FileName);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or FileFormatException or ArgumentException)
            {
                Shell.Dialog.Show(owner!, Shell.Lang.T("Resim"), Shell.Lang.T("Resim okunamadı: ") + ex.Message, Shell.Lang.T("Tamam"));
                return null;
            }
        }

        /// <summary>A picture file as base64 PNG, scaled down to <see cref="MaxSide"/> pixels on its largest side.</summary>
        public static string FromFile(string path)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(Path.GetFullPath(path));
            image.EndInit();
            image.Freeze();
            return ToPng(image);
        }

        public static string ToPng(BitmapSource image)
        {
            BitmapSource source = image;
            double side = Math.Max(image.PixelWidth, image.PixelHeight);
            if (side > MaxSide) source = new TransformedBitmap(image, new ScaleTransform(MaxSide / side, MaxSide / side));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return Convert.ToBase64String(stream.ToArray());
        }

        /// <summary>The picture for drawing (decoded once per stored text); null when it does not decode.</summary>
        public static BitmapSource? Decode(string? data)
        {
            if (string.IsNullOrEmpty(data)) return null;
            if (Decoded.TryGetValue(data!, out var cached)) return cached;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(Convert.FromBase64String(data!));
                image.EndInit();
                image.Freeze();
                Decoded.Add(data!, image);
                return image;
            }
            catch (Exception ex) when (ex is FormatException or NotSupportedException or FileFormatException or IOException)
            {
                return null;
            }
        }
    }
}
