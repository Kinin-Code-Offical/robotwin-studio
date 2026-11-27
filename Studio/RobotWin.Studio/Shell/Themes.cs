using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace RobotWin.Studio.Shell
{
    /// <summary>[theme] One colour theme: its tokens' colours and whether it is a light one.</summary>
    public sealed class ThemePalette
    {
        public ThemePalette(string id, string name, bool light, Dictionary<string, Color> colors)
        {
            Id = id;
            Name = name;
            Light = light;
            Colors = colors;
        }

        public string Id { get; }
        public string Name { get; }
        public bool Light { get; }

        /// <summary>The design tokens' colours ("Canvas", "Surface", "Text", "Primary" …).</summary>
        public Dictionary<string, Color> Colors { get; }

        /// <summary>The colour a gallery tile shows for this theme's accent when the accent follows the theme.</summary>
        public Color Tint { get; init; }

        public Color this[string token] => Colors[token];
    }

    /// <summary>
    /// [theme] The interface's colour themes and accent colours: new brushes under the design tokens' keys, which the
    /// XAML looks up dynamically, so a change recolours the open interface at once. Dark themes (graphite, OLED black,
    /// night blue, ocean, forest, plum, rose night, espresso, high contrast), light ones (light, paper, pink, peach,
    /// lavender, mint, sky) and a custom one from a hue, a tint and light or dark. The editors' canvases follow
    /// (<see cref="CanvasColors"/>); the 3D views keep their own lighting.
    /// </summary>
    internal static class Themes
    {
        /// <summary>The catalogue: dark ones first, then light ones, then the custom one.</summary>
        public static readonly (string Id, string Name, bool Light)[] All =
        {
            ("koyu", "Grafit (varsayılan)", false),
            ("siyah", "Siyah (OLED)", false),
            ("gece", "Gece mavisi", false),
            ("okyanus", "Derin okyanus", false),
            ("orman", "Orman", false),
            ("murdum", "Mürdüm", false),
            ("gul-gece", "Gül kurusu gece", false),
            ("kahve", "Kahve", false),
            ("karsitlik", "Yüksek karşıtlık", false),
            ("acik", "Açık", true),
            ("kagit", "Kâğıt", true),
            ("pembe", "Pembe", true),
            ("seftali", "Şeftali", true),
            ("lavanta", "Lavanta", true),
            ("nane", "Nane", true),
            ("gok", "Gökyüzü", true),
            ("ozel", "Özel", false),
        };

        public static readonly (string Id, string Name, Color Color)[] Accents =
        {
            ("mavi", "Mavi", Color.FromRgb(0x6E, 0x9B, 0xFF)),
            ("turkuaz", "Turkuaz", Color.FromRgb(0x3F, 0xC1, 0xC9)),
            ("yesil", "Yeşil", Color.FromRgb(0x4C, 0xC2, 0x7A)),
            ("sari", "Sarı", Color.FromRgb(0xE8, 0xB9, 0x3A)),
            ("turuncu", "Turuncu", Color.FromRgb(0xF0, 0x8A, 0x3C)),
            ("kirmizi", "Kırmızı", Color.FromRgb(0xEE, 0x5D, 0x5D)),
            ("pembe", "Pembe", Color.FromRgb(0xF4, 0x72, 0xB6)),
            ("gul", "Gül", Color.FromRgb(0xE0, 0x6C, 0x8E)),
            ("mor", "Mor", Color.FromRgb(0xA7, 0x8B, 0xFA)),
            ("tema", "Temanın rengi", Color.FromRgb(0x6E, 0x9B, 0xFF)),
        };

        /// <summary>The theme in use (after <see cref="Apply()"/>).</summary>
        public static ThemePalette Current { get; private set; } = Build("koyu");

        /// <summary>The theme was applied (canvases redraw with <see cref="CanvasColors"/>).</summary>
        public static event Action? Changed;

        private static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        /// <summary>A colour from hue (°), saturation and lightness (0–1).</summary>
        public static Color Hsl(double h, double s, double l, byte alpha = 255)
        {
            h = ((h % 360) + 360) % 360 / 360;
            s = Math.Max(0, Math.Min(1, s));
            l = Math.Max(0, Math.Min(1, l));
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            double Channel(double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            }

            return Color.FromArgb(alpha, (byte)Math.Round(255 * Channel(h + 1.0 / 3)), (byte)Math.Round(255 * Channel(h)), (byte)Math.Round(255 * Channel(h - 1.0 / 3)));
        }

        private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

        /// <summary>
        /// A whole palette from a hue (°) and a tint (the neutrals' saturation, 0–1): the same lightness steps as the
        /// graphite theme for a dark one, mirrored for a light one, so every surface, border and text keeps its contrast.
        /// </summary>
        public static Dictionary<string, Color> Generate(double hue, double tint, bool light)
        {
            Color N(double l, double satScale = 1) => Hsl(hue, tint * satScale, l);
            var d = light
                ? new Dictionary<string, Color>
                {
                    ["Canvas"] = N(0.925), ["Base"] = N(0.945), ["Surface"] = N(0.965), ["Raised"] = N(0.99, 0.6), ["Overlay"] = N(1.0),
                    ["Hover"] = N(0.905), ["Pressed"] = N(0.875), ["Border"] = N(0.865, 0.8), ["BorderStrong"] = N(0.77, 0.8),
                    ["Text"] = N(0.13, 0.5), ["TextSecondary"] = N(0.31, 0.45), ["TextMuted"] = N(0.45, 0.4), ["TextDisabled"] = N(0.66, 0.4),
                    ["Success"] = C(0x2E8B57), ["Warning"] = C(0xB7791F), ["Danger"] = C(0xD12C32),
                }
                : new Dictionary<string, Color>
                {
                    ["Canvas"] = N(0.057), ["Base"] = N(0.07), ["Surface"] = N(0.089), ["Raised"] = N(0.114), ["Overlay"] = N(0.13),
                    ["Hover"] = N(0.141), ["Pressed"] = N(0.167), ["Border"] = N(0.151), ["BorderStrong"] = N(0.202),
                    ["Text"] = N(0.935, 0.3), ["TextSecondary"] = N(0.72, 0.35), ["TextMuted"] = N(0.57, 0.3), ["TextDisabled"] = N(0.37, 0.3),
                    ["Success"] = C(0x5BB98B), ["Warning"] = C(0xD4A84B), ["Danger"] = C(0xE5484D),
                };
            return d;
        }

        /// <summary>The theme of an id (the custom one from the settings' hue, tint and lightness).</summary>
        public static ThemePalette Build(string id)
        {
            var s = AppSettings.Current;
            var entry = All.FirstOrDefault(t => t.Id == id);
            if (entry.Id == null) entry = All[0];
            Dictionary<string, Color> colors;
            double hue;
            switch (entry.Id)
            {
                case "siyah":
                    hue = 240;
                    colors = new Dictionary<string, Color>
                    {
                        ["Canvas"] = C(0x000000), ["Base"] = C(0x050506), ["Surface"] = C(0x0A0A0B), ["Raised"] = C(0x111113), ["Overlay"] = C(0x141416),
                        ["Hover"] = C(0x19191C), ["Pressed"] = C(0x202024), ["Border"] = C(0x1D1D21), ["BorderStrong"] = C(0x2B2B30),
                        ["Text"] = C(0xEDEDEF), ["TextSecondary"] = C(0xB4B4BC), ["TextMuted"] = C(0x8E8E97), ["TextDisabled"] = C(0x5C5C63),
                        ["Success"] = C(0x5BB98B), ["Warning"] = C(0xD4A84B), ["Danger"] = C(0xE5484D),
                    };
                    break;
                case "karsitlik":
                    hue = 240;
                    colors = new Dictionary<string, Color>
                    {
                        ["Canvas"] = C(0x000000), ["Base"] = C(0x000000), ["Surface"] = C(0x0B0B0B), ["Raised"] = C(0x151515), ["Overlay"] = C(0x1B1B1B),
                        ["Hover"] = C(0x2A2A2A), ["Pressed"] = C(0x3A3A3A), ["Border"] = C(0x6A6A6A), ["BorderStrong"] = C(0x9A9A9A),
                        ["Text"] = C(0xFFFFFF), ["TextSecondary"] = C(0xEDEDED), ["TextMuted"] = C(0xD0D0D0), ["TextDisabled"] = C(0x9A9A9A),
                        ["Success"] = C(0x6BE09B), ["Warning"] = C(0xFFC94D), ["Danger"] = C(0xFF6B6B),
                    };
                    break;
                case "koyu":
                    hue = 240;
                    colors = new Dictionary<string, Color>
                    {
                        ["Canvas"] = C(0x0E0E10), ["Base"] = C(0x111113), ["Surface"] = C(0x161618), ["Raised"] = C(0x1C1C1F), ["Overlay"] = C(0x202023),
                        ["Hover"] = C(0x222226), ["Pressed"] = C(0x29292D), ["Border"] = C(0x242428), ["BorderStrong"] = C(0x323237),
                        ["Text"] = C(0xEDEDEF), ["TextSecondary"] = C(0xB4B4BC), ["TextMuted"] = C(0x8E8E97), ["TextDisabled"] = C(0x5C5C63),
                        ["Success"] = C(0x5BB98B), ["Warning"] = C(0xD4A84B), ["Danger"] = C(0xE5484D),
                    };
                    break;
                default:
                    (double h, double tint, bool light) = entry.Id switch
                    {
                        "gece" => (222.0, 0.32, false),
                        "okyanus" => (192.0, 0.34, false),
                        "orman" => (150.0, 0.2, false),
                        "murdum" => (282.0, 0.24, false),
                        "gul-gece" => (338.0, 0.26, false),
                        "kahve" => (26.0, 0.22, false),
                        "acik" => (240.0, 0.06, true),
                        "kagit" => (40.0, 0.32, true),
                        "pembe" => (340.0, 0.6, true),
                        "seftali" => (20.0, 0.62, true),
                        "lavanta" => (262.0, 0.45, true),
                        "nane" => (158.0, 0.32, true),
                        "gok" => (205.0, 0.48, true),
                        _ => (s.ThemeHue, Math.Max(0, Math.Min(0.8, s.ThemeTint / 100)), s.ThemeLight),   // "ozel"
                    };
                    hue = h;
                    colors = Generate(h, tint, light);
                    entry.Light = light;
                    break;
            }

            // The primary button is the text colour as a fill: white on dark themes, near black on light ones.
            var text = colors["Text"];
            colors["Primary"] = text;
            colors["PrimaryHover"] = entry.Light ? Mix(text, colors["Raised"], 0.18) : Color.FromRgb(0xFF, 0xFF, 0xFF);
            colors["PrimaryPressed"] = Mix(text, colors["Raised"], 0.3);
            colors["OnPrimary"] = entry.Light ? colors["Overlay"] : colors["Base"];
            var tile = entry.Light ? Hsl(hue, 0.62, 0.48) : Hsl(hue, 0.7, 0.68);
            return new ThemePalette(entry.Id, entry.Name, entry.Light, colors) { Tint = entry.Id is "koyu" or "siyah" or "karsitlik" or "acik" ? Accents[0].Color : tile };
        }

        /// <summary>The accent colour for a theme: the chosen one, or the theme's own hue; darker on light themes for contrast.</summary>
        public static Color AccentFor(ThemePalette theme, string accent)
        {
            var a = accent == "tema" ? theme.Tint : Accents.FirstOrDefault(x => x.Id == accent).Color;
            if (a == default) a = Accents[0].Color;
            if (!theme.Light) return a;
            // On a light background a bright accent loses its contrast: the same hue, darker.
            return Mix(a, Color.FromRgb(0, 0, 0), 0.22);
        }

        /// <summary>
        /// A colour (a channel's, a mode's) made readable as text on a surface of the theme in use: the same hue, darkened
        /// on a light theme or lightened on a dark one until it stands 4.5:1 against the surface (WCAG for text). A bright
        /// yellow or teal chosen for the dark themes read 1.1–1.9:1 on the light ones.
        /// </summary>
        public static Color Readable(Color c, string surface = "Raised")
        {
            var under = Current.Colors.TryGetValue(surface, out var s) ? s : Current.Colors["Base"];
            var toward = Current.Light ? Color.FromRgb(0, 0, 0) : Color.FromRgb(0xFF, 0xFF, 0xFF);
            for (int i = 0; i < 20 && ContrastAudit.Ratio(c, under) < 4.5; i++) c = Mix(c, toward, 0.12);
            return c;
        }

        /// <summary>Applies the settings' theme and accent; true when everything changed at once (no restart needed).</summary>
        public static bool Apply() => Apply(AppSettings.Current.Theme, AppSettings.Current.Accent);

        public static bool Apply(string theme, string accent)
        {
            var app = Application.Current;
            if (app == null) return false;
            var palette = Build(theme);
            var colors = new Dictionary<string, Color>(palette.Colors);
            var a = AccentFor(palette, accent);
            colors["Accent"] = a;
            colors["AccentSoft"] = Color.FromArgb(palette.Light ? (byte)0x2A : (byte)0x1F, a.R, a.G, a.B);
            colors["Selection"] = Color.FromArgb(palette.Light ? (byte)0x40 : (byte)0x33, a.R, a.G, a.B);   // [tokens]
            colors["Focus"] = a;
            colors["Info"] = a;
            var canvas = colors["Canvas"];
            colors["Scrim"] = Color.FromArgb(0xF0, canvas.R, canvas.G, canvas.B);
            colors["ScrimSoft"] = Color.FromArgb(0xD8, canvas.R, canvas.G, canvas.B);
            // The brushes now under the tokens' keys: what code took from them once is recoloured below.
            var old = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
            foreach (var name in colors.Keys)
            {
                if (app.Resources.Contains("B." + name) && app.Resources["B." + name] is Brush ob) old[ob] = "B." + name;
            }

            foreach (var (name, color) in colors)
            {
                // The XAML takes the brush tokens as dynamic resources: a new brush under the same key recolours what is
                // on screen. Code that read a brush once keeps it until it builds that part again.
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                app.Resources["C." + name] = color;
                app.Resources["B." + name] = brush;
            }

            Current = palette;
            CanvasColors.Apply(palette, a);
            Rebind(app, old);
            Changed?.Invoke();
            return true;
        }

        private static readonly List<WeakReference<DependencyObject>> Roots = new List<WeakReference<DependencyObject>>();

        /// <summary>A part of the interface kept out of the window (a page not shown now) that a theme change should recolour too.</summary>
        public static void Track(DependencyObject root)
        {
            Roots.RemoveAll(r => !r.TryGetTarget(out _));
            Roots.Add(new WeakReference<DependencyObject>(root));
        }

        /// <summary>
        /// Parts built in code took a token's brush once (a panel's background, a label's colour): every property in the
        /// open windows and the tracked parts still holding one of the old token brushes gets the new brush under the same
        /// key. Properties bound to the tokens (the XAML's dynamic resources) follow by themselves and are left alone.
        /// </summary>
        private static void Rebind(Application app, Dictionary<object, string> old)
        {
            if (old.Count == 0) return;
            var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
            void Walk(DependencyObject d)
            {
                if (!seen.Add(d)) return;
                var change = new List<(DependencyProperty Property, object Value)>();
                var local = d.GetLocalValueEnumerator();
                while (local.MoveNext())
                {
                    var e = local.Current;
                    if (e.Value is Brush b && old.TryGetValue(b, out var key) && !e.Property.ReadOnly) change.Add((e.Property, app.Resources[key]));
                }

                foreach (var (p, v) in change) d.SetValue(p, v);
                if (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
                {
                    int n = VisualTreeHelper.GetChildrenCount(d);
                    for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(d, i));
                }

                // Content not in the visual tree yet (a tab not opened, a collapsed panel never measured).
                foreach (var child in LogicalTreeHelper.GetChildren(d))
                {
                    if (child is DependencyObject c) Walk(c);
                }
            }

            foreach (Window w in app.Windows) Walk(w);
            foreach (var r in Roots.ToList())
            {
                if (r.TryGetTarget(out var root)) Walk(root);
            }
        }
    }

    /// <summary>
    /// [theme] Colours the editors draw with themselves (the schematic's paper, grid, wires and symbols; the code's
    /// background): dark on dark themes, paper-white on light ones. Read at every redraw; <see cref="Themes.Changed"/>
    /// asks the canvases to redraw.
    /// </summary>
    public static class CanvasColors
    {
        // [tokens] the technical colours: what a value means on a drawing (stress, logic levels, a current path), the same in
        // every theme and kept apart from the interface's status colours (B.Success / B.Warning / B.Danger)
        public static readonly Color StressWarm = Color.FromRgb(0xE8, 0xA3, 0x3D);
        public static readonly Color StressHot = Color.FromRgb(0xE5, 0x48, 0x4D);
        public static readonly Color LogicHigh = Color.FromRgb(0x3C, 0xC0, 0x6A);
        public static readonly Color LogicLow = Color.FromRgb(0x8A, 0x8F, 0x9A);
        public static readonly Color LogicUndefined = Color.FromRgb(0xE8, 0xA3, 0x3D);
        public static readonly Color CurrentPath = Color.FromRgb(0xE8, 0xA3, 0x3D);

        public static bool Light { get; private set; }
        public static Color Paper { get; private set; } = Color.FromRgb(0x0E, 0x0E, 0x10);
        public static Color GridDot { get; private set; } = Color.FromRgb(0x24, 0x24, 0x28);
        public static Color Wire { get; private set; } = Color.FromRgb(0x8C, 0x8C, 0x94);
        public static Color Text { get; private set; } = Color.FromRgb(0xED, 0xED, 0xEF);
        public static Color MutedText { get; private set; } = Color.FromRgb(0x6C, 0x6C, 0x74);
        public static Color Label { get; private set; } = Color.FromRgb(0xA0, 0xA0, 0xA8);
        public static Color Panel { get; private set; } = Color.FromRgb(0x1C, 0x1C, 0x1F);
        public static Color PanelBorder { get; private set; } = Color.FromRgb(0x32, 0x32, 0x37);
        public static Color Symbol { get; private set; } = Color.FromRgb(0xC8, 0xC8, 0xCE);
        public static Color Pin { get; private set; } = Color.FromRgb(0x6C, 0x6C, 0x74);
        public static Color Board { get; private set; } = Color.FromRgb(0x17, 0x17, 0x1A);
        public static Color Accent { get; private set; } = Color.FromRgb(0x6E, 0x9B, 0xFF);

        /// <summary>The code editor: the caret's line, the gutter's border, line numbers and the caret line's number.</summary>
        public static Color CodeLine { get; private set; } = Color.FromRgb(0x17, 0x17, 0x1A);
        public static Color GutterBorder { get; private set; } = Color.FromRgb(0x24, 0x24, 0x28);
        public static Color GutterText { get; private set; } = Color.FromRgb(0x55, 0x55, 0x5C);

        /// <summary>The colour half way between two (a level between two of the theme's text tokens).</summary>
        private static Color Between(Color a, Color b) => Color.FromRgb((byte)((a.R + b.R + 1) / 2), (byte)((a.G + b.G + 1) / 2), (byte)((a.B + b.B + 1) / 2));
        public static Color GutterCurrent { get; private set; } = Color.FromRgb(0xC8, 0xC8, 0xCE);

        /// <summary>The PCB editor: around the board, the board, its edge, the silkscreen, ratsnest, drawings and the grid.</summary>
        public static Color PcbBack { get; private set; } = Color.FromRgb(0x0E, 0x0E, 0x10);
        public static Color PcbSubstrate { get; private set; } = Color.FromRgb(0x15, 0x16, 0x18);
        public static Color PcbEdge { get; private set; } = Color.FromRgb(0x6C, 0x6C, 0x74);
        public static Color PcbSilk { get; private set; } = Color.FromRgb(0xED, 0xED, 0xEF);
        public static Color PcbRats { get; private set; } = Color.FromRgb(0x8C, 0x8C, 0x96);
        public static Color PcbDrawing { get; private set; } = Color.FromRgb(0x9A, 0xA4, 0xB8);
        public static Color PcbGrid { get; private set; } = Color.FromArgb(0x0E, 0xFF, 0xFF, 0xFF);

        internal static void Apply(ThemePalette t, Color accent)
        {
            Light = t.Light;
            Accent = accent;
            if (!t.Light)
            {
                // Dark: the theme's own canvas tones (graphite stays as it was drawn).
                Paper = t["Canvas"];
                GridDot = t["Border"];
                Wire = Color.FromRgb(0x8C, 0x8C, 0x94);
                Text = t["Text"];
                MutedText = Color.FromRgb(0x6C, 0x6C, 0x74);
                Label = Color.FromRgb(0xA0, 0xA0, 0xA8);
                Panel = t["Raised"];
                PanelBorder = t["BorderStrong"];
                Symbol = Color.FromRgb(0xC8, 0xC8, 0xCE);
                Pin = Color.FromRgb(0x6C, 0x6C, 0x74);
                Board = t["Surface"];
                CodeLine = t["Surface"];
                GutterBorder = t["Border"];
                GutterText = Between(t["TextDisabled"], t["TextMuted"]);   // [theme] line numbers: quiet, yet over 3:1 on the editor
                GutterCurrent = Color.FromRgb(0xC8, 0xC8, 0xCE);
                PcbBack = t["Canvas"];
                PcbSubstrate = Color.FromRgb(0x15, 0x16, 0x18);
                PcbEdge = Color.FromRgb(0x6C, 0x6C, 0x74);
                PcbSilk = Color.FromRgb(0xED, 0xED, 0xEF);
                PcbRats = Color.FromRgb(0x8C, 0x8C, 0x96);
                PcbDrawing = Color.FromRgb(0x9A, 0xA4, 0xB8);
                PcbGrid = Color.FromArgb(0x0E, 0xFF, 0xFF, 0xFF);
                return;
            }

            // Light: a drawing on paper, as a schematic is printed.
            Paper = t["Raised"];
            GridDot = t["BorderStrong"];
            Wire = Color.FromRgb(0x3C, 0x3C, 0x44);
            Text = t["Text"];
            MutedText = t["TextMuted"];
            Label = t["TextSecondary"];
            Panel = t["Overlay"];
            PanelBorder = t["BorderStrong"];
            Symbol = Color.FromRgb(0x2A, 0x2A, 0x32);
            Pin = Color.FromRgb(0x70, 0x70, 0x7A);
            Board = t["Surface"];
            CodeLine = t["Hover"];
            GutterBorder = t["Border"];
            GutterText = Between(t["TextDisabled"], t["TextMuted"]);   // [theme] line numbers are not disabled text: between it and muted
            GutterCurrent = t["TextSecondary"];
            // A light PCB view: a pale board on the theme's canvas, the silkscreen as dark print; copper keeps its colours.
            PcbBack = t["Canvas"];
            PcbSubstrate = Color.FromRgb(0xE4, 0xE2, 0xDA);
            PcbEdge = Color.FromRgb(0x7A, 0x7A, 0x82);
            PcbSilk = Color.FromRgb(0x2A, 0x2A, 0x30);
            PcbRats = Color.FromRgb(0x5A, 0x5A, 0x64);
            PcbDrawing = Color.FromRgb(0x4A, 0x56, 0x70);
            PcbGrid = Color.FromArgb(0x16, 0x00, 0x00, 0x00);
        }
    }
}
