using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RobotTwin.CoreSim.Formats;
using RobotWin.Studio.Branding;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// Registers the RobotWin file types for the current Windows user (HKCU, no
    /// administrator rights): Explorer then shows their icons and opens them with
    /// this application on double-click. Only runs when the user asks for it.
    /// </summary>
    public static class FileAssociations
    {
        private const string Classes = @"Software\Classes\";

        public static string IconFolder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobotWinStudio", "Icons");

        private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "RobotWinStudio.exe");

        /// <summary>True when every type points at this executable.</summary>
        public static bool IsRegistered()
        {
            foreach (var t in RobotWinFormats.All)
            {
                using var key = Registry.CurrentUser.OpenSubKey(Classes + t.ProgId + @"\shell\open\command");
                var cmd = key?.GetValue(null) as string;
                if (cmd == null || cmd.IndexOf(ExePath, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }

            return true;
        }

        public static void Register()
        {
            Directory.CreateDirectory(IconFolder);
            foreach (var t in RobotWinFormats.All)
            {
                string icon = Path.Combine(IconFolder, t.ProgId + ".ico");
                FileIcons.WriteIco(icon, t.Extension);
                using (var ext = Registry.CurrentUser.CreateSubKey(Classes + t.Extension))
                {
                    ext.SetValue(null, t.ProgId);
                    ext.SetValue("Content Type", "application/x-" + t.Format.Replace('.', '-'));
                    using var open = ext.CreateSubKey("OpenWithProgids");
                    open.SetValue(t.ProgId, Array.Empty<byte>(), RegistryValueKind.None);
                }

                using (var prog = Registry.CurrentUser.CreateSubKey(Classes + t.ProgId))
                {
                    prog.SetValue(null, t.Description);
                    prog.SetValue("FriendlyTypeName", t.Description);
                    using (var di = prog.CreateSubKey("DefaultIcon")) di.SetValue(null, "\"" + icon + "\"");
                    using (var cmd = prog.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, $"\"{ExePath}\" \"%1\"");
                    using (var verb = prog.CreateSubKey(@"shell\open")) verb.SetValue(null, "RobotWin Studio ile aç");
                }
            }

            Notify();
        }

        public static void Unregister()
        {
            foreach (var t in RobotWinFormats.All)
            {
                using (var ext = Registry.CurrentUser.OpenSubKey(Classes + t.Extension, true))
                {
                    if (ext != null && string.Equals(ext.GetValue(null) as string, t.ProgId, StringComparison.Ordinal))
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(Classes + t.Extension, false);
                    }
                }

                Registry.CurrentUser.DeleteSubKeyTree(Classes + t.ProgId, false);
            }

            Notify();
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

        private static void Notify() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
    }

    /// <summary>Document icons for the RobotWin file types: a page with the mark and a type tag.</summary>
    public static class FileIcons
    {
        /// <summary>Label, colour and 24-grid glyph of each RobotWin file type (flat colours, no gradients).</summary>
        private static (string Tag, Color Color, string Glyph) Style(string extension) => extension.ToLowerInvariant() switch
        {
            RobotWinFormats.ProjectExtension => ("PROJE", Color.FromRgb(0x5B, 0x8D, 0xFF),
                "M5,4 H19 V20 H5 Z M8,8 H12 L16,12 V16 M8,16 H11 M16,16 A1.4,1.4 0 1 1 15.99,16"),
            RobotWinFormats.WorldExtension => ("DÜNYA", Color.FromRgb(0x2F, 0xC2, 0xB0),
                "M12,3 A9,9 0 1 1 11.99,3 Z M3,12 H21 M12,3 C8.5,6.5 8.5,17.5 12,21 M12,3 C15.5,6.5 15.5,17.5 12,21"),
            RobotWinFormats.PartExtension => ("PARÇA", Color.FromRgb(0x4C, 0xC0, 0x7E),
                "M7,7 H17 V17 H7 Z M10,3 V7 M14,3 V7 M10,17 V21 M14,17 V21 M3,10 H7 M3,14 H7 M17,10 H21 M17,14 H21"),
            RobotWinFormats.LibraryExtension => ("KÜTÜP", Color.FromRgb(0xE0, 0xB0, 0x4A),
                "M4,5 H8.5 V19 H4 Z M10,5 H14.5 V19 H10 Z M16,5.8 L20.2,6.9 L17.2,19.6 L13.9,18.8"),
            RobotWinFormats.RecordingExtension => ("KAYIT", Color.FromRgb(0xEE, 0x5A, 0x5F),
                "M3,12 C5,4 7,4 9,12 S13,20 15,12 S19,4 21,12"),
            ".rwfp" => ("FOOTP", Color.FromRgb(0xA7, 0x7B, 0xFF),
                "M5,5 H9 V9 H5 Z M15,5 H19 V9 H15 Z M5,15 H9 V19 H5 Z M15,15 H19 V19 H15 Z M9,7 H15 M9,17 H15"),
            ".rwmodel" => ("MODEL", Color.FromRgb(0xF0, 0x8A, 0x3C),
                "M12,3 L20,7.5 V16.5 L12,21 L4,16.5 V7.5 Z M4,7.5 L12,12 L20,7.5 M12,12 V21"),
            _ => ("DOSYA", Color.FromRgb(0x9A, 0x9A, 0xA2), "M6,4 H14 L18,8 V20 H6 Z")
        };

        /// <summary>Every icon this app draws (registered types plus editor-only ones).</summary>
        public static readonly string[] Extensions =
        {
            RobotWinFormats.ProjectExtension, RobotWinFormats.WorldExtension, RobotWinFormats.PartExtension,
            RobotWinFormats.LibraryExtension, RobotWinFormats.RecordingExtension, ".rwfp", ".rwmodel"
        };

        /// <summary>
        /// Document icon: a flat near-black sheet (height = width·φ, folded corner = width/φ³) with the
        /// type's glyph in its colour and, from 32 px up, a flat colour band carrying the type name.
        /// </summary>
        public static BitmapSource Render(string extension, int size)
        {
            var (tag, color, glyph) = Style(extension);
            const double phi = 1.6180339887498949;
            bool small = size <= 32;
            var dv = new DrawingVisual();
            using (var g = dv.RenderOpen())
            {
                double s = size / 64.0;
                g.PushTransform(new ScaleTransform(s, s));
                double h = small ? 62 : 60, w = h / phi, x0 = 32 - w / 2, y0 = 32 - h / 2, fold = w / (phi * phi * phi);
                if (small)
                {
                    w = 52;
                    x0 = 6;
                    fold = 12;
                }

                var sheet = new StreamGeometry();
                using (var c = sheet.Open())
                {
                    c.BeginFigure(new Point(x0, y0), true, true);
                    c.LineTo(new Point(x0 + w - fold, y0), true, true);
                    c.LineTo(new Point(x0 + w, y0 + fold), true, true);
                    c.LineTo(new Point(x0 + w, y0 + h), true, true);
                    c.LineTo(new Point(x0, y0 + h), true, true);
                }

                sheet.Freeze();
                var paper = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1F));
                var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x52)), small ? 2.4 : 1.4) { LineJoin = PenLineJoin.Round };
                g.DrawGeometry(paper, edge, sheet);
                var dog = new StreamGeometry();
                using (var c = dog.Open())
                {
                    c.BeginFigure(new Point(x0 + w - fold, y0), true, true);
                    c.LineTo(new Point(x0 + w - fold, y0 + fold), true, true);
                    c.LineTo(new Point(x0 + w, y0 + fold), true, true);
                }

                dog.Freeze();
                g.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38)), edge, dog);

                // Glyph: the type symbol in its colour, golden-sized to the sheet width.
                double gs = small ? 40 : w / phi * 1.18;
                double gy = small ? 32 : y0 + h / (phi * phi) + 2;
                var geo = Geometry.Parse(glyph).Clone();
                geo.Transform = new MatrixTransform(gs / 24, 0, 0, gs / 24, 32 - gs / 2, gy - gs / 2);
                var ink = new Pen(new SolidColorBrush(color), small ? 4.2 : 2.6)
                {
                    StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
                };
                g.DrawGeometry(null, ink, geo);

                if (!small)
                {
                    // Flat colour band with the type name.
                    double bh = h / (phi * phi * phi * phi) + 3.5, by = y0 + h - bh - 4;
                    g.DrawRectangle(new SolidColorBrush(color), null, new Rect(x0 - 4, by, w + 8, bh));
                    var ft = new FormattedText(tag, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), bh * 0.66,
                        new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16)), 1.0);
                    g.DrawText(ft, new Point(32 - ft.Width / 2, by + (bh - ft.Height) / 2));
                }

                g.Pop();
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>Multi-size .ico (PNG-compressed entries, Vista+ format).</summary>
        public static void WriteIco(string path, string extension)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            var images = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++) images[i] = Logo.EncodePng(Render(extension, sizes[i]));
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write((short)0);
            w.Write((short)1);
            w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(images[i].Length);
                w.Write(offset);
                offset += images[i].Length;
            }

            foreach (var img in images) w.Write(img);
        }
    }
}
