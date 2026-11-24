using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [theme] The interface's colour themes and accent colours: new brushes under the design tokens' keys, which the
    /// XAML looks up dynamically, so a change recolours the open interface at once.
    /// </summary>
    internal static class Themes
    {
        public static readonly (string Id, string Name)[] All =
        {
            ("koyu", "Koyu (varsayılan)"),
            ("siyah", "Siyah (OLED ekranlar için)"),
            ("karsitlik", "Yüksek karşıtlık"),
        };

        public static readonly (string Id, string Name, Color Color)[] Accents =
        {
            ("mavi", "Mavi", Color.FromRgb(0x6E, 0x9B, 0xFF)),
            ("turkuaz", "Turkuaz", Color.FromRgb(0x3F, 0xC1, 0xC9)),
            ("turuncu", "Turuncu", Color.FromRgb(0xF0, 0x8A, 0x3C)),
            ("mor", "Mor", Color.FromRgb(0xA7, 0x8B, 0xFA)),
            ("pembe", "Pembe", Color.FromRgb(0xF4, 0x72, 0xB6)),
        };

        private static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        private static Dictionary<string, Color> Palette(string theme) => theme switch
        {
            "siyah" => new Dictionary<string, Color>
            {
                ["Canvas"] = C(0x000000), ["Base"] = C(0x050506), ["Surface"] = C(0x0A0A0B), ["Raised"] = C(0x111113), ["Overlay"] = C(0x141416),
                ["Hover"] = C(0x19191C), ["Pressed"] = C(0x202024), ["Border"] = C(0x1D1D21), ["BorderStrong"] = C(0x2B2B30),
                ["Text"] = C(0xEDEDEF), ["TextSecondary"] = C(0xB4B4BC), ["TextMuted"] = C(0x8E8E97), ["TextDisabled"] = C(0x5C5C63),
            },
            "karsitlik" => new Dictionary<string, Color>
            {
                ["Canvas"] = C(0x000000), ["Base"] = C(0x000000), ["Surface"] = C(0x0B0B0B), ["Raised"] = C(0x151515), ["Overlay"] = C(0x1B1B1B),
                ["Hover"] = C(0x2A2A2A), ["Pressed"] = C(0x3A3A3A), ["Border"] = C(0x6A6A6A), ["BorderStrong"] = C(0x9A9A9A),
                ["Text"] = C(0xFFFFFF), ["TextSecondary"] = C(0xEDEDED), ["TextMuted"] = C(0xD0D0D0), ["TextDisabled"] = C(0x9A9A9A),
            },
            _ => new Dictionary<string, Color>
            {
                ["Canvas"] = C(0x0E0E10), ["Base"] = C(0x111113), ["Surface"] = C(0x161618), ["Raised"] = C(0x1C1C1F), ["Overlay"] = C(0x202023),
                ["Hover"] = C(0x222226), ["Pressed"] = C(0x29292D), ["Border"] = C(0x242428), ["BorderStrong"] = C(0x323237),
                ["Text"] = C(0xEDEDEF), ["TextSecondary"] = C(0xB4B4BC), ["TextMuted"] = C(0x8E8E97), ["TextDisabled"] = C(0x5C5C63),
            },
        };

        /// <summary>Applies the settings' theme and accent; true when everything changed at once (no restart needed).</summary>
        public static bool Apply() => Apply(AppSettings.Current.Theme, AppSettings.Current.Accent);

        public static bool Apply(string theme, string accent)
        {
            var app = Application.Current;
            if (app == null) return false;
            var colors = Palette(theme);
            Color a = Accents[0].Color;
            foreach (var x in Accents)
            {
                if (x.Id == accent) a = x.Color;
            }

            colors["Accent"] = a;
            colors["AccentSoft"] = Color.FromArgb(0x1F, a.R, a.G, a.B);
            foreach (var (name, color) in colors)
            {
                // The XAML takes the brush tokens as dynamic resources: a new brush under the same key recolours what is
                // on screen. Code that read a brush once keeps it until it builds that part again.
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                app.Resources["C." + name] = color;
                app.Resources["B." + name] = brush;
            }

            return true;
        }
    }
}
