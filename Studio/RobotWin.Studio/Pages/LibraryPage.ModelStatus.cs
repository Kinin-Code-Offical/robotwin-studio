using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [badges] What a part comes with, at a glance (D3 "Kütüphane"): an electrical model that simulates, a PCB footprint,
    /// a 3D model, a mechanical model (the body the world gives it, and a moving output such as a servo horn or a motor
    /// shaft), firmware support for a board, and whether its model leaves things out (complete, or partial with how many
    /// notes). Each badge: green when it is there, amber when partial, grey when not; its tooltip says what it means.
    /// Under them where the part comes from: maker, part number, source and licence.
    /// </summary>
    public partial class LibraryPage
    {
        /// <summary>The part's model status as (badge, state, what it means); state: 2 there, 1 partial, 0 not.</summary>
        internal static List<(string Name, int State, string Tip)> ModelStatus(PartDefinition def, IReadOnlyDictionary<string, string> parameters, string? presetId = null)
        {
            var list = new List<(string, int, string)>();
            bool electrical = def.Build != null;
            list.Add(("Elektriksel model", electrical ? 2 : 0, electrical ? "Simülasyonda devrenin bir parçası olarak çalışır" : "Yalnız şema sembolü: simülasyonda bir şey yapmaz"));
            string footprint = def.ResolveFootprint(parameters);
            list.Add(("PCB kılıfı", footprint.Length > 0 ? 2 : 0, footprint.Length > 0 ? "Kılıf: " + footprint : "Karta yerleştirilecek kılıfı yok"));
            bool model3d;
            try
            {
                model3d = Models3D.ComponentModels.Build(def, parameters) is { Children.Count: > 0 } g && !g.Bounds.IsEmpty;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                model3d = false;
            }

            list.Add(("3B model", model3d ? 2 : 0, model3d ? "PCB'nin 3B görünümünde, montajda ve dünyada gerçek biçimiyle görünür" : "3B modeli yok"));
            if (Mechanical(def, parameters, model3d) is { } mech) list.Add(mech);
            if (Firmware.ArduinoCompiler.FqbnFor(def.Id) != null)
                list.Add(("Firmware", 2, "Programlanabilir kart: Kod sayfasında yazılır, derlenir ve simülasyonda çalışır"));
            // [datasheet] the part number's figures from its maker's datasheet, the model simulated against them
            if (presetId != null && DatasheetChecks.Results(presetId) is { Count: > 0 } checks)
            {
                int pass = checks.Count(r => r.Pass);
                list.Add(("Veri sayfası", pass == checks.Count ? 2 : 1, pass == checks.Count
                    ? $"Model veri sayfasının {checks.Count} değerinin hepsini tutturuyor (aşağıda)"
                    : $"Model veri sayfasının {checks.Count} değerinden {checks.Count - pass} tanesinin dışında (aşağıda)"));
            }

            var limits = ModelLimits.Of(def.Id);
            list.Add(("Model sınırları", limits.Count == 0 ? 2 : 1, limits.Count == 0 ? "Modelin bilinen bir eksiği not edilmedi" : $"Modelin {limits.Count} bilinen eksiği var (aşağıda)"));
            return list;
        }

        /// <summary>
        /// [badges] The mechanical model: the solid the world gives the part (its own size and mass, or a stand-in box) and
        /// whether it has a moving output the physics drives (a servo's horn, a motor's shaft) with its torque and speed.
        /// </summary>
        private static (string, int, string)? Mechanical(PartDefinition def, IReadOnlyDictionary<string, string> parameters, bool model3d)
        {
            if (def.Symbol is SymbolKind.Ground or SymbolKind.PowerRail) return null;   // a net, not a thing
            RobotTwin.CoreSim.Sandbox.WorldBodyGeometry g;
            try
            {
                var probe = new RobotTwin.CoreSim.Sandbox.WorldObject
                {
                    Kind = RobotTwin.CoreSim.Sandbox.WorldObjectKind.Component, PartId = def.Id,
                    Parameters = new Dictionary<string, string>(parameters.ToDictionary(k => k.Key, k => k.Value), StringComparer.OrdinalIgnoreCase)
                };
                g = new RobotTwin.CoreSim.Automation.ApproximateGeometry(new RobotTwin.CoreSim.Sandbox.WorldDocument(), Lib).GeometryOf(probe);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                return null;
            }

            var size = g.Max - g.Min;
            string body = string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:0.#} × {1:0.#} × {2:0.#} mm, {3:0.#} g", size.X * 1000, size.Y * 1000, size.Z * 1000, g.MassKg * 1000);
            if (g.Assumed)
                return ("Mekanik model", 1, model3d
                    ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "Dünyada biçimi 3B modelinden; kütlesi tanımlı değil, {0:0.#} g varsayılır (başsız koşucuda {1})", g.MassKg * 1000, body)
                    : $"Kendi ölçüsü ve kütlesi tanımlı değil: dünyada {body} genel bir kutu varsayılır");
            string output = g.PortName switch
            {
                "horn" => "servo kolu kilitlenme torku ve yüksüz hızıyla döner",
                "shaft" => "motor mili tork ve devir modeliyle döner",
                null => "hareketli bir çıkışı yok",
                var other => other + " çıkışı fizikte sürülür"
            };
            return ("Mekanik model", 2, $"Dünyada katı cisim ({body}); {output}");
        }

        /// <summary>
        /// [libs] Where the part comes from (the roadmap's "üretici, parça numarası, kaynak ve lisans"): a user's or an
        /// imported part says what it was given; a built-in one is RobotWin's own generic model under its licence.
        /// </summary>
        private void AddSourceInfo(PartPreset preset, IReadOnlyDictionary<string, string> parameters)
        {
            var cp = UserLibrary.PartIds.Contains(preset.Id) ? LoadUserPart(preset) : null;
            string Dash(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s!.Trim();
            var rows = cp != null
                ? new[]
                {
                    ("Üretici", Dash(cp.Manufacturer)),
                    ("Parça numarası", Dash(cp.PartNumber)),
                    ("Kaynak", Dash(cp.Source.Length > 0 ? cp.Source : cp.Definition?.Source is { Length: > 0 } src ? src : cp.Author.Length > 0 ? Lang.T("Kendi parçanız") + " · " + cp.Author : null)),
                    ("Lisans", cp.License.Length > 0 ? cp.License : Lang.T("belirtilmedi"))
                }
                : new[]
                {
                    ("Üretici", Lang.T("— (genel model)")),
                    ("Parça numarası", Dash(parameters.TryGetValue("model", out var m) ? m : null)),
                    ("Kaynak", Lang.T("RobotWin yerleşik kütüphanesi")),
                    ("Lisans", Lang.T("MIT (RobotWin Studio ile)"))
                };
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < rows.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var key = new TextBlock { Text = Lang.T(rows[i].Item1), Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 0, 14, 3) };
                var value = new TextBlock { Text = rows[i].Item2, Style = (Style)FindResource("Text.Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3) };
                if (rows[i].Item2 == "—") value.SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");
                Grid.SetRow(key, i);
                Grid.SetRow(value, i);
                Grid.SetColumn(value, 1);
                grid.Children.Add(key);
                grid.Children.Add(value);
            }

            Detail.Children.Add(grid);
        }

        /// <summary>
        /// [datasheet] The part number against its datasheet (only when it has checks): each figure with its conditions,
        /// the model's value, the datasheet's typical value and range, and whether it falls in; the source under them.
        /// </summary>
        private void AddDatasheetTable(string presetId)
        {
            var results = DatasheetChecks.Results(presetId);
            if (results.Count == 0) return;
            Detail.Children.Add(new TextBlock { Text = Lang.T("Veri sayfasıyla karşılaştırma"), Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, 0, 0, 6) });
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            foreach (double w in new[] { 0.0, 70, 110, 16 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w) : new GridLength(1, GridUnitType.Star) });
            int row = 0;
            void Cell(UIElement e, int col)
            {
                Grid.SetRow(e, row);
                Grid.SetColumn(e, col);
                grid.Children.Add(e);
            }

            TextBlock Text(string s, bool head = false, bool wrap = false) => new TextBlock
            {
                Text = s, FontSize = 12, Margin = new Thickness(0, 2, 8, 2), TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = head ? FontWeights.SemiBold : FontWeights.Normal,
            };

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Cell(Text(string.Empty, true), 0);
            Cell(Text(Lang.T("Model"), true), 1);
            Cell(Text(Lang.T("Veri sayfası"), true), 2);
            foreach (var r in results)
            {
                row++;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var c = r.Check;
                string F(double v) => (v / c.Scale).ToString(Math.Abs(v / c.Scale) >= 100 ? "0" : "0.###", System.Globalization.CultureInfo.CurrentCulture);
                string range = double.IsNegativeInfinity(c.Low) || c.Low == 0 ? Lang.T("en çok") + " " + F(c.High) : double.IsPositiveInfinity(c.High) ? Lang.T("en az") + " " + F(c.Low) : F(c.Low) + " … " + F(c.High);
                string sheet = double.IsNaN(c.Typical) ? range : Lang.T("tipik") + " " + F(c.Typical) + " (" + range + ")";
                var q = Text(Lang.T(c.Quantity), wrap: true);
                q.SetResourceReference(TextBlock.ForegroundProperty, "B.TextSecondary");
                Cell(q, 0);
                Cell(Text(double.IsFinite(r.Value) ? F(r.Value) + " " + c.Unit : "—"), 1);
                var s = Text(sheet + " " + c.Unit, wrap: true);
                s.SetResourceReference(TextBlock.ForegroundProperty, "B.TextSecondary");
                Cell(s, 2);
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, ToolTip = Lang.T(r.Pass ? "Aralıkta" : "Aralığın dışında") };
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, r.Pass ? "B.Success" : "B.Danger");
                Cell(dot, 3);
            }

            Detail.Children.Add(grid);
            var source = new TextBlock { Text = string.Join(" · ", results.Select(x => x.Check.Source).Distinct()), Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
            Detail.Children.Add(source);
        }

        /// <summary>The badges in the part's detail, under its title.</summary>
        private void AddModelStatus(PartDefinition def, IReadOnlyDictionary<string, string> parameters, string? presetId = null)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            foreach (var (name, state, tip) in ModelStatus(def, parameters, presetId))
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, state == 2 ? "B.Success" : state == 1 ? "B.Warning" : "B.TextMuted");
                var text = new TextBlock { Text = Lang.T(name), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                text.SetResourceReference(TextBlock.ForegroundProperty, state == 0 ? "B.TextMuted" : "B.Text");
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(dot);
                content.Children.Add(text);
                var badge = new Border { Child = content, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(1), ToolTip = Lang.T(tip) };
                badge.SetResourceReference(Border.BackgroundProperty, "B.Surface");
                badge.SetResourceReference(Border.BorderBrushProperty, "B.Border");
                row.Children.Add(badge);
            }

            Detail.Children.Add(row);
        }
    }
}
