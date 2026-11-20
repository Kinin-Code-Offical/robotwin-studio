using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sdk;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [sdk] Environment and programmability of the world page: the atmosphere / wind /
    /// aerodynamics / force-field part of the "Ortam" palette, the "Betikler" (scripts &amp;
    /// plugins) palette tab, the per-object "Ortam ve davranışlar" inspector section and the
    /// script editor. WorldPage.xaml.cs only calls InitSdk, SdkEnvironmentPalette,
    /// BuildSdkPalette, SdkSection and UpdateSdkLive.
    /// </summary>
    public partial class WorldPage
    {
        private TextBlock? _sdkLive;
        private TextBlock? _sdkStatus;


        private void InitSdk()
        {
            SdkServices.Init();
            SdkServices.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (TabSdk.IsChecked == true) BuildPalette();
            }));
        }

        private void SdkEdit(string status, Action change)
        {
            Edit(status, change);
            Scene.InvalidateWind();
        }

        // ================================================================ small controls

        private CheckBox SdkSwitch(string text, bool value, Action<bool> set, string? tip = null)
        {
            var cb = new CheckBox { Content = text, IsChecked = value, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 2, 0, 8), ToolTip = tip };
            cb.Click += (_, _) => set(cb.IsChecked == true);
            return cb;
        }

        private FrameworkElement SdkCombo(string label, (string Text, string Value)[] items, string current, Action<string> set)
        {
            var box = new ComboBox();
            foreach (var (text, value) in items)
            {
                box.Items.Add(new ComboBoxItem { Content = text, Tag = value, IsSelected = string.Equals(value, current, StringComparison.OrdinalIgnoreCase) });
            }

            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is ComboBoxItem it && !string.Equals((string)it.Tag, current, StringComparison.OrdinalIgnoreCase)) set((string)it.Tag);
            };
            return Labeled(label, box);
        }

        private Brush DangerBrush => (Brush)FindResource("B.Danger");

        /// <summary>Monospace program box; checked when it loses focus, committed only when valid (the error shows below with a caret).</summary>
        private FrameworkElement ExpressionBox(string source, Func<string, string?> check, Action<string> commit, string hint)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            var box = new TextBox
            {
                Text = source, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 54, MaxHeight = 220,
                FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, VerticalContentAlignment = VerticalAlignment.Top,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Tag = hint, ToolTip = hint
            };
            var error = new TextBlock { FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, Foreground = DangerBrush, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            var initial = source.Length > 0 ? check(source) : null;
            if (initial != null)
            {
                error.Text = initial;
                error.Visibility = Visibility.Visible;
            }

            box.LostFocus += (_, _) =>
            {
                if (box.Text == source) return;
                var problem = check(box.Text);
                error.Text = problem ?? string.Empty;
                error.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
                if (problem == null) commit(box.Text);
            };
            sp.Children.Add(box);
            sp.Children.Add(error);
            return sp;
        }

        private static string F(double v, string fmt = "0.###") => v.ToString(fmt, Inv);

        private static string V(Vec3 v, string fmt = "0.000") => $"({v.X.ToString(fmt, Inv)}, {v.Y.ToString(fmt, Inv)}, {v.Z.ToString(fmt, Inv)})";

        // ================================================================ Ortam: air, wind, aerodynamics, fields

        private void SdkEnvironmentPalette(WorldDocument world)
        {
            var s = world.Sdk;
            var env = world.Environment;
            var atm = s.Atmosphere;
            double rho = env.AirDensity is double legacy && atm.DensityOverride == null ? legacy : Atmosphere.DensityFor(atm, env.AmbientC);

            Palette.Children.Add(Caption("Hava"));
            Palette.Children.Add(Note($"Yoğunluk ρ = {F(rho, "0.000")} kg/m³ · {F(env.AmbientC, "0.#")} °C, {F(Atmosphere.PressureAt(atm.PressureHPa * 100, atm.AltitudeM) / 100, "0")} hPa"));
            Palette.Children.Add(NumberField("Deniz seviyesi basıncı (hPa)", atm.PressureHPa, v => SdkEdit("Basınç değişti", () => atm.PressureHPa = v), 300, 1100, "0.##"));
            Palette.Children.Add(NumberField("Rakım (m)", atm.AltitudeM, v => SdkEdit("Rakım değişti", () => atm.AltitudeM = v), -400, 11000, "0"));
            Palette.Children.Add(NumberField("Bağıl nem (%)", atm.Humidity * 100, v => SdkEdit("Nem değişti", () => atm.Humidity = v / 100), 0, 100, "0"));
            Palette.Children.Add(NumberField("Sabit yoğunluk (kg/m³, 0 = hesapla)", atm.DensityOverride ?? 0, v => SdkEdit("Hava yoğunluğu değişti", () => atm.DensityOverride = v <= 0 ? null : v), 0, 2000, "0.####"));

            var wind = s.Wind;
            Palette.Children.Add(Caption("Rüzgâr"));
            Palette.Children.Add(NumberField("Ortalama hız (m/s)", wind.Speed, v => SdkEdit("Rüzgâr hızı değişti", () => wind.Speed = v), 0, 100, "0.##"));
            Palette.Children.Add(NumberField("Estiği yön (°, 0 = +X, 90 = +Y)", wind.DirectionDeg, v => SdkEdit("Rüzgâr yönü değişti", () => wind.DirectionDeg = v), -360, 360, "0.#"));
            Palette.Children.Add(NumberField("Dikey bileşen (m/s, + yukarı)", wind.Vertical, v => SdkEdit("Dikey rüzgâr değişti", () => wind.Vertical = v), -50, 50, "0.##"));
            Palette.Children.Add(SdkCombo("Yükseklik profili", new[] { ("Düzgün", "uniform"), ("Üstel yasa", "power"), ("Logaritmik", "log") }, wind.Profile,
                v => SdkEdit("Rüzgâr profili değişti", () => wind.Profile = v)));
            if (wind.Profile != "uniform")
            {
                Palette.Children.Add(NumberField("Referans yüksekliği (m)", wind.ReferenceHeight, v => SdkEdit("Referans yüksekliği değişti", () => wind.ReferenceHeight = v), 0.01, 500, "0.##"));
                if (wind.Profile == "power") Palette.Children.Add(NumberField("Üs α (0.10 su … 0.30 şehir)", wind.PowerExponent, v => SdkEdit("Profil üssü değişti", () => wind.PowerExponent = v), 0, 1, "0.###"));
                else Palette.Children.Add(NumberField("Pürüzlülük z₀ (m)", wind.Roughness, v => SdkEdit("Pürüzlülük değişti", () => wind.Roughness = v), 1e-5, 5, "0.####"));
            }

            Palette.Children.Add(SdkCombo("Türbülans", new[] { ("Yok", "none"), ("Dryden benzeri (tohumlu)", "dryden"), ("Sinüs toplamı (eski)", "sine") }, wind.Turbulence,
                v => SdkEdit("Türbülans değişti", () => wind.Turbulence = v)));
            if (wind.Turbulence != "none")
            {
                Palette.Children.Add(NumberField("Şiddet σ (m/s)", wind.Intensity, v => SdkEdit("Türbülans şiddeti değişti", () => wind.Intensity = v), 0, 30, "0.##"));
                if (wind.Turbulence == "dryden")
                {
                    Palette.Children.Add(NumberField("Uzunluk ölçeği L (m)", wind.LengthScale, v => SdkEdit("Uzunluk ölçeği değişti", () => wind.LengthScale = v), 0.1, 2000, "0.#"));
                    Palette.Children.Add(NumberField("Dikey σ oranı", wind.VerticalRatio, v => SdkEdit("Dikey oran değişti", () => wind.VerticalRatio = v), 0, 2, "0.##"));
                }
            }

            Palette.Children.Add(SdkSwitch("Ani esintiler (1 − cos)", wind.Gusts, v => SdkEdit("Esintiler değişti", () => wind.Gusts = v)));
            if (wind.Gusts)
            {
                Palette.Children.Add(NumberField("Esinti genliği (m/s)", wind.GustAmplitude, v => SdkEdit("Esinti değişti", () => wind.GustAmplitude = v), 0, 50, "0.##"));
                Palette.Children.Add(NumberField("Esinti süresi (s)", wind.GustDuration, v => SdkEdit("Esinti değişti", () => wind.GustDuration = v), 0.05, 60, "0.##"));
                Palette.Children.Add(NumberField("Ortalama aralık (s)", wind.GustInterval, v => SdkEdit("Esinti değişti", () => wind.GustInterval = v), 0.1, 600, "0.#"));
            }

            Palette.Children.Add(NumberField("Rastgelelik tohumu", s.Seed, v => SdkEdit("Tohum değişti", () => s.Seed = (long)v), 0, 1e9, "0"));
            Palette.Children.Add(SdkSwitch("Rüzgârı göster (oklar, rüzgâr çorabı)", s.ShowWind, v => SdkEdit("Rüzgâr görünümü değişti", () => s.ShowWind = v)));

            Palette.Children.Add(Caption("Aerodinamik"));
            Palette.Children.Add(SdkSwitch("Hava sürüklemesi (her nesneye)", s.Aero.Drag, v => SdkEdit("Sürükleme değişti", () => s.Aero.Drag = v),
                "F = ½·ρ·Cd·A·|v|·v; A nesnenin izdüşüm alanı. Kutu Cd 1.05, küre 0.47, silindir 1.0 (yandan) / 0.9 (uçtan)."));
            Palette.Children.Add(SdkSwitch("Kaldırma kuvveti (ρ·V·g, her nesneye)", s.Aero.Buoyancy, v => SdkEdit("Kaldırma değişti", () => s.Aero.Buoyancy = v)));
            Palette.Children.Add(NumberField("Dönme sönümü katsayısı", s.Aero.AngularDamping, v => SdkEdit("Dönme sönümü değişti", () => s.Aero.AngularDamping = v), 0, 10, "0.###"));
            Palette.Children.Add(Note("Nesne başına Cd, alan ve kaldırma sağ paneldeki 'Ortam ve davranışlar' bölümünden değiştirilir."));

            Palette.Children.Add(Caption("Kuvvet alanları ve hava akımları"));
            foreach (var f in s.Fields.ToList()) Palette.Children.Add(FieldCard(world, f));
            void AddField(string label, Action<ForceFieldSpec> init)
            {
                Palette.Children.Add(Action("+ " + label, () => SdkEdit("Alan eklendi", () =>
                {
                    var p = Scene.DropPoint();
                    var f = new ForceFieldSpec { Id = s.NextId("F"), X = Math.Round(p.X, 3), Y = Math.Round(p.Y, 3), Z = 0.15 };
                    init(f);
                    f.Name = s.Fields.Any(x => x.Name == label) ? label + " " + (s.Fields.Count + 1).ToString(Inv) : label;
                    s.Fields.Add(f);
                })));
            }

            AddField("Düzgün alan", f => { f.Kind = "uniform"; f.DirX = 0; f.DirZ = 1; f.Strength = 0.1; });
            AddField("Çekici / itici", f => { f.Kind = "radial"; f.Strength = 0.5; f.Radius = 0.3; });
            AddField("Girdap", f => { f.Kind = "vortex"; f.DirX = 0; f.DirZ = 1; f.Strength = 3; f.Radius = 0.2; f.Inflow = 0.2; });
            AddField("Fan / hava akımı", f => { f.Kind = "fan"; f.Strength = 5; f.SizeX = 0.8; f.SizeY = 0.25; f.SizeZ = 0.25; });
            AddField("Konveyör bölgesi", f => { f.Kind = "conveyor"; f.Strength = 0.2; f.SizeX = 1.0; f.SizeY = 0.2; f.SizeZ = 0.1; f.Z = 0.05; });
            AddField("İfade alanı", f => { f.Kind = "expression"; f.Expression = "# rüzgâr veya kuvvet yazın\nwind.x = 3*sin(t)"; });
            var pluginFields = PluginHost.Shared.Registry.OfKind(SdkTypeKind.ForceField).Concat(PluginHost.Shared.Registry.OfKind(SdkTypeKind.WindSource)).ToList();
            foreach (var t in pluginFields)
            {
                var info = t;
                AddField(info.Name, f =>
                {
                    f.Kind = "plugin";
                    f.PluginType = info.Key;
                    if (info.PluginId != BehaviorRegistry.BuiltinId && info.PluginId != BehaviorRegistry.ScriptId && !s.EnabledPlugins.Contains(info.PluginId)) s.EnabledPlugins.Add(info.PluginId);
                });
            }
        }

        private FrameworkElement FieldCard(WorldDocument world, ForceFieldSpec f)
        {
            var s = world.Sdk;
            var card = new Border
            {
                BorderBrush = (Brush)FindResource("B.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 4), Margin = new Thickness(0, 0, 0, 8)
            };
            var sp = new StackPanel();
            card.Child = sp;
            string kindText = (f.Kind ?? string.Empty) switch
            {
                "uniform" => "Düzgün alan",
                "radial" => "Merkezcil (çekici/itici)",
                "vortex" => "Girdap",
                "fan" => "Fan / hava akımı",
                "conveyor" => "Konveyör bölgesi",
                "expression" => "İfade alanı",
                "plugin" => "Eklenti: " + (f.PluginType != null ? PluginHost.Shared.Registry.Find(f.PluginType)?.Name ?? f.PluginType : "?"),
                _ => f.Kind ?? "?"
            };
            var name = new TextBox { Text = f.Name, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) };
            name.LostFocus += (_, _) =>
            {
                if (name.Text != f.Name) SdkEdit("Alan adı değişti", () => f.Name = name.Text);
            };
            sp.Children.Add(name);
            sp.Children.Add(Note(kindText + (f.Targets.Count > 0 ? $" · yalnızca {f.Targets.Count} nesne" : " · tüm hareketli nesneler")));
            sp.Children.Add(SdkSwitch("Etkin", f.Enabled, v => SdkEdit("Alan açıldı/kapandı", () => f.Enabled = v)));
            sp.Children.Add(Vector3Row("Merkez (mm)", f.X * 1000, f.Y * 1000, f.Z * 1000, (x, y, z) => SdkEdit("Alan taşındı", () =>
            {
                f.X = x / 1000;
                f.Y = y / 1000;
                f.Z = z / 1000;
            })));
            bool alwaysBoxed = f.Kind == "fan" || f.Kind == "conveyor";
            if (!alwaysBoxed) sp.Children.Add(SdkSwitch("Bir kutu bölgesiyle sınırla", f.Bounded, v => SdkEdit("Alan sınırı değişti", () => f.Bounded = v)));
            if (alwaysBoxed || f.Bounded)
            {
                sp.Children.Add(Vector3Row("Bölge boyutu (mm)", f.SizeX * 1000, f.SizeY * 1000, f.SizeZ * 1000, (x, y, z) => SdkEdit("Alan boyutu değişti", () =>
                {
                    f.SizeX = Math.Max(0.001, x / 1000);
                    f.SizeY = Math.Max(0.001, y / 1000);
                    f.SizeZ = Math.Max(0.001, z / 1000);
                })));
                sp.Children.Add(NumberField("Bölge dönüşü (°)", f.Yaw, v => SdkEdit("Alan döndürüldü", () => f.Yaw = v), -360, 360, "0.#"));
            }

            FrameworkElement Dir(string label) => Vector3Row(label, f.DirX, f.DirY, f.DirZ, (x, y, z) => SdkEdit("Alan yönü değişti", () =>
            {
                f.DirX = x;
                f.DirY = y;
                f.DirZ = z;
            }));

            switch (f.Kind)
            {
                case "uniform":
                    sp.Children.Add(Dir("Yön"));
                    sp.Children.Add(NumberField(f.Acceleration ? "Büyüklük (m/s²)" : "Büyüklük (N)", f.Strength, v => SdkEdit("Alan şiddeti değişti", () => f.Strength = v)));
                    sp.Children.Add(SdkSwitch("İvme olarak (kütleden bağımsız)", f.Acceleration, v => SdkEdit("Alan türü değişti", () => f.Acceleration = v)));
                    break;
                case "radial":
                    sp.Children.Add(NumberField(f.Acceleration ? "R'deki ivme (m/s², + çeker)" : "R'deki kuvvet (N, + çeker)", f.Strength, v => SdkEdit("Alan şiddeti değişti", () => f.Strength = v)));
                    sp.Children.Add(NumberField("Yarıçap R (m)", f.Radius, v => SdkEdit("Yarıçap değişti", () => f.Radius = v), 1e-3, 1000));
                    sp.Children.Add(SdkCombo("Azalma", new[] { ("Ters kare (1/r²)", "inverse-square"), ("Doğrusal", "linear"), ("Gauss", "gaussian"), ("Sabit", "none") }, f.Falloff,
                        v => SdkEdit("Azalma değişti", () => f.Falloff = v)));
                    sp.Children.Add(SdkSwitch("İvme olarak (kütleden bağımsız)", f.Acceleration, v => SdkEdit("Alan türü değişti", () => f.Acceleration = v)));
                    break;
                case "vortex":
                    sp.Children.Add(Dir("Eksen"));
                    sp.Children.Add(NumberField(f.Acceleration ? "Çekirdekteki ivme (m/s²)" : "Çekirdekteki hız (m/s)", f.Strength, v => SdkEdit("Girdap değişti", () => f.Strength = v)));
                    sp.Children.Add(NumberField("Çekirdek yarıçapı (m)", f.Radius, v => SdkEdit("Girdap değişti", () => f.Radius = v), 1e-3, 1000));
                    sp.Children.Add(NumberField("İçe akış oranı", f.Inflow, v => SdkEdit("Girdap değişti", () => f.Inflow = v), -5, 5, "0.##"));
                    sp.Children.Add(NumberField("Yukarı akış oranı", f.Updraft, v => SdkEdit("Girdap değişti", () => f.Updraft = v), -5, 5, "0.##"));
                    sp.Children.Add(SdkSwitch("Kuvvet olarak uygula (rüzgâr yerine)", f.Acceleration, v => SdkEdit("Girdap türü değişti", () => f.Acceleration = v),
                        "Kapalıyken girdap havayı döndürür ve nesneleri sürükleme ile taşır (pervaneler de etkilenir)."));
                    break;
                case "fan":
                    sp.Children.Add(Dir("Üfleme yönü"));
                    sp.Children.Add(NumberField("Çıkış hızı (m/s)", f.Strength, v => SdkEdit("Fan hızı değişti", () => f.Strength = v), -100, 100, "0.##"));
                    sp.Children.Add(Note("Hava ~6 çap sonra serbest jet gibi yavaşlar; nesneleri sürüklemeyle iter, pervanelerin giriş havasını değiştirir."));
                    break;
                case "conveyor":
                    sp.Children.Add(Dir("Bant yönü"));
                    sp.Children.Add(NumberField("Bant hızı (m/s)", f.Strength, v => SdkEdit("Bant hızı değişti", () => f.Strength = v), -20, 20, "0.###"));
                    sp.Children.Add(NumberField("Tutuş (μ)", f.Grip, v => SdkEdit("Tutuş değişti", () => f.Grip = v), 0, 5, "0.##"));
                    break;
                case "expression":
                    sp.Children.Add(ExpressionBox(f.Expression, src => ExpressionCheck.ForField(src, f.Parameters), src => SdkEdit("İfade değişti", () => f.Expression = src),
                        "Her nesne için: F.x, F.y, F.z (N) · T.x… (N·m) · FL.x… (gövde ekseninde). Rüzgâr: wind.x, wind.y, wind.z (m/s). Değişkenler: t, x, y, z, vx, vy, vz, m, g, air.x … "));
                    sp.Children.Add(Note("Örnek: wind.x = 3*sin(t)  ·  F.z = -0.5*m*g*(z > 1)"));
                    break;
                case "plugin":
                    var info = f.PluginType != null ? PluginHost.Shared.Registry.Find(f.PluginType) : null;
                    if (info == null) sp.Children.Add(Note("Bu alanı sağlayan eklenti yüklü değil."));
                    else foreach (var p in info.Parameters) sp.Children.Add(ParameterField(p, f.Parameters, "Alan parametresi değişti"));
                    break;
            }

            var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
            var sel = World?.Find(Scene.Selected);
            if (sel != null && !f.Targets.Contains(sel.Id))
            {
                var b = new Button { Content = "Yalnızca: " + sel.Name, Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(6, 2, 6, 2) };
                b.Click += (_, _) => SdkEdit("Alan hedefi değişti", () => f.Targets.Add(sel.Id));
                row.Children.Add(b);
            }

            if (f.Targets.Count > 0)
            {
                var b = new Button { Content = "Herkese uygula", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(6, 2, 6, 2) };
                b.Click += (_, _) => SdkEdit("Alan hedefi değişti", () => f.Targets.Clear());
                row.Children.Add(b);
            }

            var del = new Button { Content = "Sil", Style = (Style)FindResource("Btn.Ghost"), Foreground = DangerBrush, Padding = new Thickness(6, 2, 6, 2) };
            del.Click += (_, _) => SdkEdit("Alan silindi", () => s.Fields.Remove(f));
            row.Children.Add(del);
            sp.Children.Add(row);
            return card;
        }

        /// <summary>Editor for one [Parameter] of a behaviour or plugin field, stored as text in <paramref name="values"/>.</summary>
        private FrameworkElement ParameterField(SdkParameterInfo p, Dictionary<string, string> values, string status)
        {
            string current = values.TryGetValue(p.Name, out var t) ? t : p.DefaultText;
            string label = p.Label + (p.Unit.Length > 0 ? $" ({p.Unit})" : string.Empty);
            FrameworkElement control;
            switch (p.ValueType)
            {
                case "bool":
                    control = SdkSwitch(label, current == "true", v => SdkEdit(status, () => values[p.Name] = v ? "true" : "false"));
                    break;
                case "double":
                case "int":
                    double.TryParse(current, NumberStyles.Float, Inv, out double d);
                    control = NumberField(label, d, v => SdkEdit(status, () => values[p.Name] = v.ToString("R", Inv)),
                        double.IsInfinity(p.Min) ? double.MinValue : p.Min, double.IsInfinity(p.Max) ? double.MaxValue : p.Max, p.ValueType == "int" ? "0" : "0.#####");
                    break;
                default:
                    var box = new TextBox { Text = current, ToolTip = p.ValueType == "vector" ? "x, y, z" : p.Description };
                    box.LostFocus += (_, _) =>
                    {
                        if (box.Text != current) SdkEdit(status, () => values[p.Name] = box.Text.Trim());
                    };
                    control = Labeled(label, box);
                    break;
            }

            if (p.Description.Length > 0) control.ToolTip = p.Description;
            return control;
        }

        // ================================================================ inspector: per object

        private void SdkSection(WorldDocument world, WorldObject o)
        {
            var s = world.Sdk;
            var aero = s.AeroOf(o.Id);
            Inspector.Children.Add(Caption("Ortam ve davranışlar"));
            Inspector.Children.Add(SdkSwitch("Hava sürüklemesi", aero?.Drag ?? true, v => SdkEdit("Sürükleme değişti", () => s.AeroFor(o.Id).Drag = v)));
            if (aero?.Drag ?? true)
            {
                Inspector.Children.Add(NumberField("Cd (0 = şekilden)", aero?.Cd ?? 0, v => SdkEdit("Cd değişti", () => s.AeroFor(o.Id).Cd = v <= 0 ? null : v), 0, 10, "0.###"));
                Inspector.Children.Add(NumberField("Referans alan (cm², 0 = izdüşüm)", (aero?.AreaM2 ?? 0) * 1e4, v => SdkEdit("Alan değişti", () => s.AeroFor(o.Id).AreaM2 = v <= 0 ? null : v / 1e4), 0, 1e6, "0.##"));
            }

            Inspector.Children.Add(SdkSwitch("Kaldırma kuvveti (ρ·V·g)", aero?.Buoyancy ?? false, v => SdkEdit("Kaldırma değişti", () => s.AeroFor(o.Id).Buoyancy = v),
                "Hafif ve hacimli nesneler (balon, köpük) için. Hacim çarpışma şeklinden alınır."));

            var list = s.BehaviorsOf(o.Id).ToList();
            foreach (var a in list) Inspector.Children.Add(BehaviorCard(world, a));
            var add = Action("Davranış ekle ▾", () => { }, "Bu nesneye ifade, hazır davranış, eklenti veya betik ekleyin.");
            add.Click += (_, _) => BehaviorMenu(world, o.Id, add);
            Inspector.Children.Add(add);

            Inspector.Children.Add(Caption("Hava ve betik kuvvetleri (canlı)"));
            _sdkLive = new TextBlock { FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("B.TextSecondary") };
            Inspector.Children.Add(_sdkLive);
            UpdateSdkLive();
        }

        private void BehaviorMenu(WorldDocument world, string? objectId, FrameworkElement anchor)
        {
            var s = world.Sdk;
            var menu = new ContextMenu { PlacementTarget = anchor };
            void Item(ItemsControl parent, string header, Action add)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += (_, _) => add();
                parent.Items.Add(mi);
            }

            Item(menu, "İfade (F.x = …)", () => SdkEdit("İfade eklendi", () => s.Behaviors.Add(new BehaviorAttachment
            {
                Id = s.NextId("B"), ObjectId = objectId, Type = BehaviorAttachment.ExpressionType,
                Expression = "# yay + sönüm: x0 başlangıç konumu\nF.x = -k*(x - x0) - c*vx",
                Parameters = { ["k"] = "20", ["c"] = "0.5" }
            })));
            var reg = PluginHost.Shared.Registry;
            foreach (var group in reg.OfKind(SdkTypeKind.Behavior).GroupBy(t => t.PluginId))
            {
                string title = group.Key == BehaviorRegistry.BuiltinId ? "Hazır davranışlar" : group.Key == BehaviorRegistry.ScriptId ? "Betikler" : "Eklenti: " + (PluginHost.Shared.Find(group.Key)?.Manifest.Name ?? group.Key);
                var sub = new MenuItem { Header = title };
                foreach (var t in group.OrderBy(t => t.Name, StringComparer.CurrentCulture))
                {
                    var info = t;
                    Item(sub, info.Name + (info.Description.Length > 0 ? " — " + Shorten(info.Description, 60) : string.Empty), () => SdkEdit(info.Name + " eklendi", () =>
                    {
                        if (info.PluginId != BehaviorRegistry.BuiltinId && info.PluginId != BehaviorRegistry.ScriptId && !s.EnabledPlugins.Contains(info.PluginId)) s.EnabledPlugins.Add(info.PluginId);
                        s.Behaviors.Add(new BehaviorAttachment { Id = s.NextId("B"), ObjectId = info.AttachTo == "world" ? null : objectId, Type = info.Key });
                    }));
                }

                menu.Items.Add(sub);
            }

            if (!reg.OfKind(SdkTypeKind.Behavior).Any(t => t.PluginId == BehaviorRegistry.ScriptId))
            {
                menu.Items.Add(new MenuItem { Header = "Betik yok — Betikler sekmesinden C# betiği yazın", IsEnabled = false });
            }

            menu.IsOpen = true;
        }

        private static string Shorten(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        private FrameworkElement BehaviorCard(WorldDocument world, BehaviorAttachment a)
        {
            var s = world.Sdk;
            var card = new Border
            {
                BorderBrush = (Brush)FindResource("B.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 4), Margin = new Thickness(0, 4, 0, 8)
            };
            var sp = new StackPanel();
            card.Child = sp;
            var info = a.IsExpression ? null : PluginHost.Shared.Registry.Find(a.Type);
            string title = a.Name.Length > 0 ? a.Name : a.IsExpression ? "İfade" : info?.Name ?? a.Type;
            sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
            string origin = a.IsExpression ? "ifade dili" : info == null ? "türü yüklü değil: " + a.Type
                : info.PluginId == BehaviorRegistry.BuiltinId ? "hazır" : info.PluginId == BehaviorRegistry.ScriptId ? "betik" : "eklenti " + info.PluginId;
            sp.Children.Add(Note(origin + (info?.Description.Length > 0 ? " · " + info.Description : string.Empty)));
            sp.Children.Add(SdkSwitch("Etkin", a.Enabled, v => SdkEdit("Davranış açıldı/kapandı", () => a.Enabled = v)));
            if (a.IsExpression)
            {
                sp.Children.Add(ExpressionBox(a.Expression, src => ExpressionCheck.ForBehavior(src, a.Parameters), src => SdkEdit("İfade değişti", () => a.Expression = src),
                    "Çıktılar: F.x F.y F.z (N, dünya) · FL.x… (gövde) · T.x… TL.x… (N·m). Girdiler: t dt x y z vx vy vz wx wy wz speed m g x0 y0 z0 roll pitch yaw rho tempC wind.x… airspeed h"));
                var pars = new TextBox { Text = string.Join(", ", a.Parameters.Select(kv => kv.Key + "=" + kv.Value)), ToolTip = "Sabitler: k=20, c=0.5" };
                pars.LostFocus += (_, _) =>
                {
                    var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var part in pars.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int eq = part.IndexOf('=');
                        if (eq > 0) parsed[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
                    }

                    if (parsed.Count == a.Parameters.Count && parsed.All(kv => a.Parameters.TryGetValue(kv.Key, out var v) && v == kv.Value)) return;
                    SdkEdit("Sabitler değişti", () =>
                    {
                        a.Parameters.Clear();
                        foreach (var kv in parsed) a.Parameters[kv.Key] = kv.Value;
                    });
                };
                sp.Children.Add(Labeled("Sabitler", pars));
                sp.Children.Add(Note("Ör.: F.z = m*g*0.5 (ağırlığın yarısını taşı) · FL.x = 0.2*(t > 1) · T.z = -0.01*wz"));
            }
            else if (info != null)
            {
                foreach (var p in info.Parameters) sp.Children.Add(ParameterField(p, a.Parameters, "Parametre değişti"));
            }

            var del = new Button { Content = "Kaldır", Style = (Style)FindResource("Btn.Ghost"), Foreground = DangerBrush, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 2, 6, 2) };
            del.Click += (_, _) => SdkEdit("Davranış kaldırıldı", () => s.Behaviors.Remove(a));
            sp.Children.Add(del);
            return card;
        }

        /// <summary>Live air and SDK readouts of the selected object; status table of the Betikler tab.</summary>
        private void UpdateSdkLive()
        {
            var rt = _session.State != SimState.Stopped ? _session.Sdk : null;
            if (_sdkLive != null)
            {
                var o = World?.Find(Scene.Selected);
                if (o == null || rt == null)
                {
                    _sdkLive.Text = "Çalışırken: rüzgâr, havaya göre hız, sürükleme ve betik kuvvetleri.";
                }
                else if (rt.Readout(o.Id) is { } r)
                {
                    var sb = new StringBuilder();
                    string C(Vec3 v, string fmt) => $"{v.X.ToString(fmt, Inv)}, {v.Y.ToString(fmt, Inv)}, {v.Z.ToString(fmt, Inv)}";
                    sb.AppendLine($"Rüzgâr    {C(r.Wind, "0.00")} m/s");
                    sb.AppendLine($"Hava hızı {F(r.RelativeAir.Length, "0.00")} m/s");
                    sb.AppendLine($"Sürükleme {C(r.Drag, "0.0000")} N");
                    if (r.Buoyancy.LengthSquared > 0) sb.AppendLine($"Kaldırma  {F(r.Buoyancy.Length, "0.00000")} N");
                    if (r.SdkForce.LengthSquared > 0) sb.AppendLine($"Betik F   {C(r.SdkForce, "0.000")} N");
                    sb.AppendLine($"Cd·A      {C(r.DragArea * 1e4, "0.#")} cm²");
                    string prefix = o.Name + ".";
                    foreach (var kv in rt.Published.Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Take(12))
                    {
                        sb.AppendLine($"{Shorten(kv.Key.Substring(prefix.Length), 9),-9} {F(kv.Value, "0.####")}");
                    }

                    _sdkLive.Text = sb.ToString().TrimEnd();
                }
            }

            if (_sdkStatus != null && TabSdk.IsChecked == true) _sdkStatus.Text = StatusText(rt);
        }

        private string StatusText(WorldSdkRuntime? rt)
        {
            if (rt == null) return "Dünya çalışmıyor. Çalıştırınca her davranış ve alanın durumu, hataları ve adım başına süresi burada görünür.";
            var sb = new StringBuilder();
            sb.AppendLine($"ρ = {F(rt.AirDensity, "0.000")} kg/m³ · rüzgâr (0,0,1 m) {V(rt.WindAt(new Vec3(0, 0, 1)), "0.0")} m/s");
            foreach (var st in rt.Status)
            {
                sb.AppendLine($"{(st.Active ? "●" : "○")} {Shorten(st.Name, 22),-22} {F(st.AverageMs, "0.000")} ms" + (st.Errors > 0 ? $"  {st.Errors} hata" : string.Empty));
                if (st.LastError != null) sb.AppendLine("   " + Shorten(st.LastError, 120));
            }

            var published = rt.Published.Take(16).ToList();
            if (published.Count > 0)
            {
                sb.AppendLine("Yayımlanan değerler:");
                foreach (var kv in published) sb.AppendLine($"  {Shorten(kv.Key, 24),-24} {F(kv.Value, "0.####")}");
            }

            return sb.ToString().TrimEnd();
        }

        // ================================================================ "Betikler" tab: scripts & plugins

        private void BuildSdkPalette()
        {
            var world = World;
            Palette.Children.Add(Note("Kendi fizik kodunuz: ifadeler (anında), C# betikleri (.NET SDK ile derlenir) ve eklenti DLL'leri. Hepsi sabit 1 ms fizik adımının içinde, tekrarlanabilir şekilde çalışır."));

            Palette.Children.Add(Caption("Çalışma durumu", 4));
            _sdkStatus = new TextBlock { FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("B.TextSecondary") };
            _sdkStatus.Text = StatusText(_session.State != SimState.Stopped ? _session.Sdk : null);
            Palette.Children.Add(_sdkStatus);

            if (world != null)
            {
                var s = world.Sdk;
                Palette.Children.Add(Caption("Bu dünyadaki davranışlar"));
                if (s.Behaviors.Count == 0) Palette.Children.Add(Note("Henüz yok. Bir nesne seçip sağ panelden 'Davranış ekle' deyin."));
                foreach (var a in s.Behaviors)
                {
                    var o = world.Find(a.ObjectId);
                    var info = a.IsExpression ? null : PluginHost.Shared.Registry.Find(a.Type);
                    string title = (o?.Name ?? "Dünya") + " · " + (a.Name.Length > 0 ? a.Name : a.IsExpression ? "ifade" : info?.Name ?? a.Type);
                    string sub = (a.Enabled ? "etkin" : "kapalı") + (a.IsExpression ? " · " + Shorten(a.Expression.Replace('\n', ' '), 40) : info == null ? " · tür yüklü değil" : string.Empty);
                    var id = o?.Id;
                    Palette.Children.Add(Row(title, sub, () =>
                    {
                        if (id == null) return;
                        Scene.Select(id);
                        Scene.FrameSelected();
                    }));
                }

                Palette.Children.Add(Action("Dünyaya davranış ekle ▾", () => { }));
                var last = (Button)Palette.Children[Palette.Children.Count - 1];
                last.Click += (_, _) => BehaviorMenu(world, Scene.Selected, last);

                // ---------------- scripts
                Palette.Children.Add(Caption("C# betikleri"));
                var info2 = ScriptCompiler.Detect();
                Palette.Children.Add(Note(info2.Explanation));
                foreach (var sc in s.Scripts)
                {
                    var script = sc;
                    int lines = script.Source.Count(c => c == '\n') + 1;
                    Palette.Children.Add(Row(script.Name, $"{lines} satır · {(script.Enabled ? "etkin" : "kapalı")} · düzenlemek için tıklayın", () => OpenScriptEditor(world, script)));
                }

                Palette.Children.Add(Action("+ Yeni C# betiği", () =>
                {
                    WorldScript? added = null;
                    SdkEdit("Betik eklendi", () =>
                    {
                        added = new WorldScript { Id = s.NextId("S"), Name = s.Scripts.Count == 0 ? "Betik.cs" : $"Betik{s.Scripts.Count + 1}.cs", Source = ScriptCompiler.StarterScript };
                        s.Scripts.Add(added);
                    });
                    if (added != null) OpenScriptEditor(world, added);
                }));
                if (s.Scripts.Count > 0)
                {
                    Palette.Children.Add(Action("Derle ve yükle", () => CompileScriptsNow(world), "Betikleri .NET SDK ile derler; türleri 'Davranış ekle' menüsünde görünür.", primary: info2.Available));
                }

                var lastBuild = SdkServices.LastScriptBuild;
                if (lastBuild != null)
                {
                    Palette.Children.Add(Note(lastBuild.Summary));
                    foreach (var d in lastBuild.Diagnostics.Where(d => d.IsError).Take(8))
                    {
                        Palette.Children.Add(new TextBlock { Text = d.ToString(), Foreground = DangerBrush, FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
                    }
                }
            }

            // ---------------- plugins
            Palette.Children.Add(Caption("Eklentiler"));
            Palette.Children.Add(Note("Klasör: " + PluginHost.UserPluginDirectory + " (ve dünya dosyasının yanındaki 'Plugins'). Her eklenti kendi alt klasöründe bir DLL ve plugin.json'dan oluşur."));
            foreach (var p in PluginHost.Shared.Plugins.Where(p => p.Source != "builtin"))
            {
                var plugin = p;
                string state = plugin.Error != null ? "HATA: " + plugin.Error : plugin.IsLoaded ? $"{plugin.Types.Count} tür" + (plugin.PartIds.Count > 0 ? $", {plugin.PartIds.Count} parça" : string.Empty) : "yüklü değil";
                Palette.Children.Add(Row($"{plugin.Manifest.Name} {plugin.Manifest.Version}", $"{plugin.Manifest.Author} · {plugin.Source} · {state}", () => { }, plugin.Manifest.Description));
                if (world != null && plugin.IsLoaded && plugin.Source != "script" && plugin.Source != "code")
                {
                    var s = world.Sdk;
                    bool on = s.EnabledPlugins.Any(x => string.Equals(x, plugin.Id, StringComparison.OrdinalIgnoreCase));
                    Palette.Children.Add(SdkSwitch("Bu dünyada etkin", on, v => SdkEdit("Eklenti " + (v ? "etkinleştirildi" : "kapatıldı"), () =>
                    {
                        s.EnabledPlugins.RemoveAll(x => string.Equals(x, plugin.Id, StringComparison.OrdinalIgnoreCase));
                        if (v) s.EnabledPlugins.Add(plugin.Id);
                    })));
                }
            }

            Palette.Children.Add(Action("Eklentileri yeniden yükle", () =>
            {
                StopSimulation();
                SdkServices.ReloadAll();
                W.Status = "Eklentiler yeniden yüklendi";
            }, "Simülasyonu durdurur, eklentileri bellekten atıp diskten yeniden yükler (DLL'i yeniden derledikten sonra)."));
            Palette.Children.Add(Action("Eklenti klasörünü aç", () =>
            {
                try
                {
                    Directory.CreateDirectory(PluginHost.UserPluginDirectory);
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + PluginHost.UserPluginDirectory + "\"") { UseShellExecute = true });
                }
                catch (Exception ex) when (ex is IOException || ex is System.ComponentModel.Win32Exception || ex is UnauthorizedAccessException)
                {
                    W.Status = "Klasör açılamadı: " + ex.Message;
                }
            }));
            Palette.Children.Add(Action("DLL'den eklenti yükle (bu oturum)…", () =>
            {
                var dlg = new OpenFileDialog { Filter = ".NET eklentisi (*.dll)|*.dll", Title = "Eklenti DLL'i seç" };
                if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
                var loaded = PluginHost.Shared.LoadAssemblyFile(dlg.FileName);
                W.Status = loaded.Error != null ? "Eklenti yüklenemedi: " + loaded.Error : $"{loaded.Manifest.Name}: {loaded.Types.Count} tür yüklendi. Kalıcı olması için klasörünü eklenti klasörüne kopyalayın.";
                BuildPalette();
            }));

            Palette.Children.Add(Caption("Şablon ve belgeler"));
            Palette.Children.Add(Note("Eklenti şablonu: sdk/templates/RobotWinPlugin (rüzgâr türbini, manyetik çekim, özel sürükleme, termal yükselme, özel sensör örnekleri). Belge: docs/SDK.md."));
            Palette.Children.Add(Note("İfade fonksiyonları: " + RobotTwin.CoreSim.Sdk.Expressions.ExpressionHelp.FunctionList()));
        }

        private void CompileScriptsNow(WorldDocument world)
        {
            var info = ScriptCompiler.Detect(refresh: true);
            if (!info.Available)
            {
                W.Status = info.Explanation;
                BuildPalette();
                return;
            }

            StopSimulation();
            W.Status = "Betikler derleniyor…";
            var files = world;
            System.Threading.Tasks.Task.Run(() => SdkServices.CompileScripts(files)).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
            {
                var r = t.Result;
                W.Status = r.Summary;
                BuildPalette();
                if (Scene.Selected != null) BuildInspector();
            })));
        }

        /// <summary>Snapshot of an SDK world: show the Ortam tab (or Betikler with RW_SNAPSHOT_TAB=scripts).</summary>
        private void SdkSnapshot()
        {
            var world = World;
            if (world == null || world.Sdk.IsDefault) return;
            string? tab = Environment.GetEnvironmentVariable("RW_SNAPSHOT_TAB");
            if (tab == "scripts") TabSdk.IsChecked = true;
            else TabEnv.IsChecked = true;
            BuildPalette();
            Scene.InvalidateWind();
            if (tab == "inspector" && world.Sdk.Behaviors.FirstOrDefault(b => b.ObjectId != null) is { } first)
            {
                Scene.Select(first.ObjectId);
                UpdateLayout();
                (Inspector.Parent as ScrollViewer)?.ScrollToBottom();
                (Palette.Parent as ScrollViewer)?.ScrollToBottom();
            }
        }

        // ================================================================ script editor window

        private void OpenScriptEditor(WorldDocument world, WorldScript script)
        {
            var owner = Window.GetWindow(this);
            var win = new Window
            {
                Title = "Betik: " + script.Name, Width = 860, Height = 640, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = (Brush)FindResource("B.Base"), Foreground = (Brush)FindResource("B.Text")
            };
            var root = new DockPanel { Margin = new Thickness(12) };
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var name = new TextBox { Text = script.Name, Width = 220 };
            var enabled = new CheckBox { Content = "Etkin", IsChecked = script.Enabled, Style = (Style)FindResource("Switch"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            top.Children.Add(name);
            top.Children.Add(enabled);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var diag = new TextBox
            {
                IsReadOnly = true, Height = 110, FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
                Text = ScriptCompiler.Detect().Explanation + "\nPublic sınıflar IWorldBehavior / WorldBehavior, IForceField veya IWindSource uygulamalıdır. Derleme önbelleğe alınır."
            };
            bottom.Children.Add(diag);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var save = new Button { Content = "Kaydet", Padding = new Thickness(14, 4, 14, 4) };
            var compile = new Button { Content = "Kaydet ve derle", Style = (Style)FindResource("Btn.Primary"), Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(8, 0, 0, 0) };
            var delete = new Button { Content = "Sil", Style = (Style)FindResource("Btn.Ghost"), Foreground = DangerBrush, Margin = new Thickness(8, 0, 0, 0) };
            var close = new Button { Content = "Kapat", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(save);
            buttons.Children.Add(compile);
            buttons.Children.Add(delete);
            buttons.Children.Add(close);
            bottom.Children.Add(buttons);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            var code = new TextBox
            {
                Text = script.Source, AcceptsReturn = true, AcceptsTab = true, FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 13,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.NoWrap
            };
            root.Children.Add(code);
            win.Content = root;

            void Save()
            {
                if (code.Text == script.Source && name.Text == script.Name && (enabled.IsChecked == true) == script.Enabled) return;
                SdkEdit("Betik değişti", () =>
                {
                    script.Source = code.Text;
                    script.Name = string.IsNullOrWhiteSpace(name.Text) ? "Betik.cs" : name.Text.Trim();
                    script.Enabled = enabled.IsChecked == true;
                });
            }

            save.Click += (_, _) => Save();
            close.Click += (_, _) => win.Close();
            delete.Click += (_, _) =>
            {
                SdkEdit("Betik silindi", () => world.Sdk.Scripts.Remove(script));
                win.Close();
            };
            compile.Click += (_, _) =>
            {
                Save();
                if (!ScriptCompiler.Detect().Available)
                {
                    diag.Text = ScriptCompiler.Detect().Explanation;
                    return;
                }

                StopSimulation();
                diag.Text = "Derleniyor… (ilk derleme birkaç saniye sürebilir)";
                compile.IsEnabled = false;
                System.Threading.Tasks.Task.Run(() => SdkServices.CompileScripts(world)).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
                {
                    compile.IsEnabled = true;
                    var r = t.Result;
                    var sb = new StringBuilder(r.Summary).AppendLine();
                    foreach (var d in r.Diagnostics.Take(30)) sb.AppendLine(d.ToString());
                    if (r.Success)
                    {
                        var types = PluginHost.Shared.Find(BehaviorRegistry.ScriptId)?.Types ?? Array.Empty<SdkTypeInfo>();
                        sb.AppendLine("Türler: " + (types.Count == 0 ? "(yok — public bir WorldBehavior sınıfı yazın)" : string.Join(", ", types.Select(x => x.Name))));
                    }

                    diag.Text = sb.ToString();
                    BuildPalette();
                })));
            };
            win.Show();
        }
    }
}
