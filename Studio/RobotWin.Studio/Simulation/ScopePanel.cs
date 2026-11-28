using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Instruments;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Simulation
{
    /// <summary>Front panel of the oscilloscope: run control, horizontal, trigger, vertical (per channel), cursors, math, XY.</summary>
    public sealed class ScopePanel : ScrollViewer
    {
        private readonly ScopeView _view;

        /// <summary>--snapshot --fault decode-pick: the n-th decoded byte on screen picked as a click would.</summary>
        public void PickDecodedForSnapshot(int n) => _view.PickDecodedForSnapshot(n);
        private readonly StackPanel _root = new StackPanel { Margin = new Thickness(12, 8, 10, 10) };
        private readonly List<Action> _refresh = new List<Action>();
        private readonly Button _run;

        public ScopePanel(ScopeView view)
        {
            _view = view;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Content = _root;

            // ---- run control
            var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            _run = Btn("RUN / STOP", () =>
            {
                if (Scope.Running) Scope.Stop();
                else Scope.Run();
                _view.ForceAcquire();
            }, primary: true);
            top.Children.Add(_run);
            top.Children.Add(Btn("TEK", () =>
            {
                Scope.Single();
                _view.InvalidateVisual();
            }, tip: "Tek tetikleme: bir kez yakala ve dur"));
            top.Children.Add(Btn("AUTOSET", () =>
            {
                Scope.Autoset(Sim.InstrumentTime);
                _view.ForceAcquire();
            }, tip: "V/div, zaman/div ve tetik seviyesini sinyale göre ayarla"));
            // [ref] a bench scope's REF: keep the screen, lay the next runs over it; again to drop it
            top.Children.Add(Btn("REF", () =>
            {
                if (Scope.Reference != null) Scope.Reference = null;
                else if (!Scope.KeepReference()) Workspace.Current.Status = "Saklanacak bir ekran yok: önce bir dalga yakalayın";
                _view.InvalidateVisual();
            }, tip: "Ekrandaki dalgayı referans olarak sakla: sonraki koşular üstüne çizilir, sapması (RMS) durum satırında. Bir daha basınca kaldırır."));
            _root.Children.Add(top);
            // [mask] a pass / fail band round the reference, in divisions of each channel's own V/div
            var masks = new[] { 0.0, 0.1, 0.25, 0.5 };
            Chips("Maske (div)", new[] { "Yok", "±0,1", "±0,25", "±0,5" }, () => Math.Max(0, Array.IndexOf(masks, Scope.MaskDivisions)), i =>
            {
                Scope.MaskDivisions = masks[i];
                if (Scope.MaskDivisions > 0 && Scope.Reference == null) Scope.KeepReference();   // a mask needs a reference: the screen now
                _view.InvalidateVisual();
            });

            // ---- horizontal
            Section("Yatay");
            Stepper("Zaman/div", () => SiValue.Format(Scope.SecondsPerDiv, "s") + "/div", dir =>
            {
                Scope.SecondsPerDiv = Steps125.Next(Scope.SecondsPerDiv, dir, Oscilloscope.MinSecondsPerDiv, Oscilloscope.MaxSecondsPerDiv);
            });
            Stepper("Konum", () => SiValue.Format(Scope.Delay, "s"), dir => Scope.Delay += dir * Scope.SecondsPerDiv, () => Scope.Delay = 0);

            // ---- trigger
            Section("Tetik");
            Chips("Mod", new[] { "Auto", "Normal", "Tek" }, () => (int)Scope.TriggerMode, i =>
            {
                if (i == 2) Scope.Single();
                else
                {
                    Scope.TriggerMode = (TriggerMode)i;
                    Scope.Run();
                }
            });
            Chips("Kaynak", new[] { "1", "2", "3", "4" }, () => Scope.TriggerSource, i => Scope.TriggerSource = i, colored: true);
            Chips("Eğim", new[] { "↗ yükselen", "↘ düşen" }, () => (int)Scope.TriggerSlope, i => Scope.TriggerSlope = (TriggerSlope)i);
            Stepper("Seviye", () => SiValue.Format(Scope.TriggerLevel, TriggerChannel.Unit, 3), dir => Scope.TriggerLevel += dir * 0.2 * TriggerChannel.Scale, () =>
            {
                var t = Scope.Frame?.TraceOf(TriggerChannel);
                if (t != null && !double.IsNaN(t.Measurements.Max)) Scope.TriggerLevel = 0.5 * (t.Measurements.Max + t.Measurements.Min);
            }, "%50");
            Stepper("Holdoff", () => Scope.Holdoff <= 0 ? "kapalı" : SiValue.Format(Scope.Holdoff, "s"), dir =>
            {
                if (Scope.Holdoff <= 0) Scope.Holdoff = dir > 0 ? 1e-6 : 0;
                else if (dir < 0 && Scope.Holdoff <= 1e-6) Scope.Holdoff = 0;
                else Scope.Holdoff = Steps125.Next(Scope.Holdoff, dir, 1e-6, 10);
            });

            // ---- vertical
            Section("Dikey");
            Chips(null, new[] { "CH1", "CH2", "CH3", "CH4" }, () => Sim.SelectedScopeChannel, i => Sim.SelectedScopeChannel = i, colored: true);
            var onRow = Row();
            var on = new CheckBox { Style = Res("Switch"), Content = "Ekranda", Margin = new Thickness(0, 0, 14, 0) };
            on.Click += (_, _) =>
            {
                Selected.Enabled = on.IsChecked == true;
                Changed();
            };
            onRow.Children.Add(on);
            _refresh.Add(() => on.IsChecked = Selected.Enabled);
            _root.Children.Add(onRow);
            Chips("Prob", new[] { "1×", "10×" }, () => Selected.ProbeAttenuation >= 5 ? 1 : 0, i =>
            {
                double old = Selected.ProbeAttenuation;
                Selected.ProbeAttenuation = i == 1 ? 10 : 1;
                Selected.Scale *= Selected.ProbeAttenuation / old;
                Sim.BindScopeLoads();
            });
            Stepper("V/div", () => SiValue.Format(Selected.Scale, Selected.Unit) + "/div", dir =>
                Selected.Scale = Steps125.Next(Selected.Scale, dir, Selected.MinScale, Selected.MaxScale));
            Stepper("Konum", () => Selected.Position.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " div",
                dir => Selected.Position = Math.Max(-8, Math.Min(8, Selected.Position + 0.5 * dir)), () => Selected.Position = 0);
            Chips("Kuplaj", new[] { "DC", "AC", "GND" }, () => (int)Selected.Coupling, i => Selected.Coupling = (ScopeCoupling)i);
            var opts = Row();
            var bw = new CheckBox { Style = Res("Switch"), Content = "BW 20 MHz", Margin = new Thickness(0, 0, 14, 0), ToolTip = "20 MHz bant genişliği sınırı (gürültüyü azaltır, hızlı kenarları yavaşlatır)" };
            bw.Click += (_, _) =>
            {
                Selected.BandwidthLimit = bw.IsChecked == true;
                Changed();
            };
            var inv = new CheckBox { Style = Res("Switch"), Content = "Ters" };
            inv.Click += (_, _) =>
            {
                Selected.Invert = inv.IsChecked == true;
                Changed();
            };
            opts.Children.Add(bw);
            opts.Children.Add(inv);
            _refresh.Add(() =>
            {
                bw.IsChecked = Selected.BandwidthLimit;
                inv.IsChecked = Selected.Invert;
            });
            _root.Children.Add(opts);
            var probeRow = Row();
            var probeLabel = new TextBlock { Style = Res("Text.Caption"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 180 };
            _refresh.Add(() => probeLabel.Text = Sim.ScopeProbes[Sim.SelectedScopeChannel] is { } p ? "Prob: " + p.Name : "Prob bağlı değil (kabloya tıklayın)");
            var detach = Btn("Çıkar", () => Sim.DetachScopeChannel(Sim.SelectedScopeChannel), ghost: true);
            detach.Margin = new Thickness(0, 0, 8, 0);
            probeRow.Children.Add(detach);
            probeRow.Children.Add(probeLabel);
            _root.Children.Add(probeRow);
            // [scope] the channel's own name (on the scale row and in the CSV header) and colour
            var nameRow = Row();
            nameRow.Children.Add(new TextBlock { Text = "Ad", Width = 72, Style = Res("Text.Caption"), VerticalAlignment = VerticalAlignment.Center });
            var title = new TextBox { Width = 150, ToolTip = "Kanalın adı: ölçek satırında ve CSV başlığında görünür; Enter ile uygula" };
            void ApplyTitle()
            {
                Selected.Title = title.Text.Trim();
                Changed();
            }

            title.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) ApplyTitle();
            };
            title.LostKeyboardFocus += (_, _) => ApplyTitle();
            nameRow.Children.Add(title);
            _refresh.Add(() =>
            {
                if (!title.IsKeyboardFocusWithin) title.Text = Selected.Title;
            });
            _root.Children.Add(nameRow);
            var colorRow = Row();
            colorRow.Children.Add(new TextBlock { Text = "Renk", Width = 72, Style = Res("Text.Caption"), VerticalAlignment = VerticalAlignment.Center });
            var swatches = new List<ToggleButton>();
            foreach (var color in ScopeView.Palette)
            {
                var c = color;
                var sw = new ToggleButton
                {
                    Style = Res("Chip"),
                    Margin = new Thickness(0, 0, 4, 0),
                    Padding = new Thickness(5, 3, 5, 3),
                    ToolTip = "Kanal rengi",
                    Content = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(c) }
                };
                sw.Click += (_, _) =>
                {
                    int rgb = c.R << 16 | c.G << 8 | c.B;
                    var own = ScopeView.ChannelColors[Selected.Index];
                    Selected.ColorRgb = c == own ? -1 : rgb;
                    Changed();
                };
                swatches.Add(sw);
                colorRow.Children.Add(sw);
            }

            _refresh.Add(() =>
            {
                var now = ScopeView.ColorOf(Selected.Index);
                foreach (var (sw, c) in swatches.Zip(ScopeView.Palette)) sw.IsChecked = c == now;
            });
            _root.Children.Add(colorRow);

            // ---- display
            Section("Ekran ve ölçüm");
            Chips("İmleç", new[] { "Kapalı", "Zaman", "Gerilim" }, () => (int)_view.CursorMode, i => _view.CursorMode = (ScopeCursorMode)i);
            // [scope] A − B, A + B, A × B (V × A = power), ∫A (charge, energy), dA/dt (slew rate); the scale fits the result
            Chips("Matematik", new[] { "Kapalı", "A−B", "A+B", "A×B", "∫A", "dA/dt" }, () => (int)Scope.MathFunction, i =>
            {
                Scope.MathFunction = (ScopeMathFunction)i;
                Scope.MathAutoScale = true;
            });
            var sourceA = Chips("A", new[] { "1", "2", "3", "4" }, () => Scope.MathSourceA, i =>
            {
                Scope.MathSourceA = i;
                Scope.MathAutoScale = true;
            }, colored: true);
            var sourceB = Chips("B", new[] { "1", "2", "3", "4" }, () => Scope.MathSourceB, i =>
            {
                Scope.MathSourceB = i;
                Scope.MathAutoScale = true;
            }, colored: true);
            _refresh.Add(() =>
            {
                var f = Scope.MathFunction;
                sourceA.Visibility = f == ScopeMathFunction.Off ? Visibility.Collapsed : Visibility.Visible;
                sourceB.Visibility = f is ScopeMathFunction.Add or ScopeMathFunction.Subtract or ScopeMathFunction.Multiply ? Visibility.Visible : Visibility.Collapsed;
            });
            Chips("Ölçüm", new[] { "1", "2", "3", "4", "M" }, () => _view.MeasureChannel, i => _view.MeasureChannel = i, colored: true);
            var dispRow = Row();
            var xy = new CheckBox { Style = Res("Switch"), Content = "XY", Margin = new Thickness(0, 0, 14, 0), ToolTip = "CH1 yatay, CH2 dikey (Lissajous)" };
            xy.Click += (_, _) =>
            {
                Scope.XY = xy.IsChecked == true;
                Changed();
            };
            var load = new CheckBox { Style = Res("Switch"), Content = "Prob yüklemesi", ToolTip = "Prob, devreye 1 MΩ ∥ 110 pF (1×) veya 10 MΩ ∥ 12 pF (10×) olarak bağlanır" };
            load.Click += (_, _) =>
            {
                Sim.ProbeLoading = load.IsChecked == true;
                Sim.BindScopeLoads();
                Changed();
            };
            dispRow.Children.Add(xy);
            dispRow.Children.Add(load);
            _refresh.Add(() =>
            {
                xy.IsChecked = Scope.XY;
                load.IsChecked = Sim.ProbeLoading;
            });
            _root.Children.Add(dispRow);

            // [decode] UART, I²C and SPI read off the probed wires, as a scope's decode option does.
            Section("Çözücü");
            string[] protocols = { string.Empty, "UART", "I2C", "SPI" };
            Chips("Protokol", new[] { "Kapalı", "UART", "I²C", "SPI" }, () => Math.Max(0, Array.IndexOf(protocols, Scope.DecodeProtocol)), i =>
            {
                Scope.DecodeProtocol = protocols[i];
                Changed();
            });
            var decodeA = Chips("A", new[] { "1", "2", "3", "4" }, () => Scope.DecodeA, i =>
            {
                Scope.DecodeA = i;
                Changed();
            }, colored: true);
            var decodeB = Chips("B", new[] { "1", "2", "3", "4" }, () => Scope.DecodeB, i =>
            {
                Scope.DecodeB = i;
                Changed();
            }, colored: true);
            var decodeC = Chips("CS", new[] { "yok", "1", "2", "3", "4" }, () => Scope.DecodeC + 1, i =>
            {
                Scope.DecodeC = i - 1;
                Changed();
            });
            var mode = Chips("Mod", new[] { "0", "1", "2", "3" }, () => Scope.DecodeSpiMode, i =>
            {
                Scope.DecodeSpiMode = i;
                Changed();
            });
            var parityChips = Chips("Eşlik", new[] { "yok", "çift", "tek" }, () => Scope.DecodeParity == "even" ? 1 : Scope.DecodeParity == "odd" ? 2 : 0, i =>
            {
                Scope.DecodeParity = i == 1 ? "even" : i == 2 ? "odd" : "none";
                Changed();
            });
            var baudRow = Row();
            baudRow.Children.Add(new TextBlock { Text = "Baud", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var baud = new TextBox { Width = 80, Text = "9600", ToolTip = "UART hızı (bit/s); Enter ile uygula" };
            baud.KeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter) return;
                if (double.TryParse(baud.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double b) && b > 0) Scope.DecodeBaud = b;
                Changed();
            };
            baudRow.Children.Add(baud);
            _root.Children.Add(baudRow);
            _refresh.Add(() =>
            {
                string p = Scope.DecodeProtocol;
                decodeA.Visibility = p.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                decodeB.Visibility = p is "I2C" or "SPI" ? Visibility.Visible : Visibility.Collapsed;
                decodeC.Visibility = mode.Visibility = p == "SPI" ? Visibility.Visible : Visibility.Collapsed;
                parityChips.Visibility = baudRow.Visibility = p == "UART" ? Visibility.Visible : Visibility.Collapsed;
                if (!baud.IsKeyboardFocusWithin) baud.Text = Scope.DecodeBaud.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            });

            var bottom = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            bottom.Children.Add(Btn("Tüm probları çıkar", () => Sim.ClearProbes(), ghost: true));
            var save = Btn("Kaydı kaydet (.rwsim)", () => (Window.GetWindow(this) as MainWindow)?.SaveRecording(), ghost: true);
            save.ToolTip = "Kanallar, olaylar, seri çıktı, araç yörüngesi ve proje tek dosyada";
            bottom.Children.Add(save);
            bottom.Children.Add(Btn("CSV", ExportCsv, ghost: true, tip: "Ekrandaki örnekler CSV olarak: zaman ve her kanal, adı ve birimiyle"));
            bottom.Children.Add(Btn("PNG", ExportPng, ghost: true, tip: "Osiloskop ekranının görüntüsü PNG olarak"));
            _root.Children.Add(bottom);

            Sim.InstrumentsChanged += (_, _) => Refresh();
            Sim.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SimulationSession.State)) Refresh();
            };
            Loaded += (_, _) => Refresh();
        }

        /// <summary>[scope] The screen's samples to a CSV file (RobotTwin.CoreSim.Instruments.ScopeExport).</summary>
        private void ExportCsv()
        {
            if (Scope.Frame is not { Length: > 0 } frame)
            {
                Workspace.Current.Status = "Dışa aktarılacak bir ekran yok: önce bir dalga yakalayın";
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "osiloskop.csv" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            System.IO.File.WriteAllText(dlg.FileName, RobotTwin.CoreSim.Instruments.ScopeExport.ToCsv(frame), new System.Text.UTF8Encoding(true));
            Workspace.Current.Status = "Osiloskop verisi kaydedildi: " + System.IO.Path.GetFileName(dlg.FileName);
        }

        /// <summary>[scope] The scope's screen as it is drawn now, to a PNG file at the display's own scale.</summary>
        private void ExportPng()
        {
            if (_view.ActualWidth < 1 || _view.ActualHeight < 1)
            {
                Workspace.Current.Status = "Osiloskop ekranı görünmüyor";
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = "osiloskop.png" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            SaveScreen(_view, dlg.FileName);
            Workspace.Current.Status = "Osiloskop görüntüsü kaydedildi: " + System.IO.Path.GetFileName(dlg.FileName);
        }

        /// <summary>A view drawn into a PNG at its display's DPI.</summary>
        internal static void SaveScreen(FrameworkElement view, string path)
        {
            var dpi = VisualTreeHelper.GetDpi(view);
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(view.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(view.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bmp.Render(view);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var file = System.IO.File.Create(path);
            png.Save(file);
        }

        private static SimulationSession Sim => Workspace.Current.Simulation;
        private static Oscilloscope Scope => Sim.Scope;
        private static ScopeChannel Selected => Scope.Channels[Math.Max(0, Math.Min(3, Sim.SelectedScopeChannel))];
        private static ScopeChannel TriggerChannel => Scope.Channels[Math.Max(0, Math.Min(3, Scope.TriggerSource))];

        public void Refresh()
        {
            foreach (var r in _refresh) r();
            _run.Content = Scope.Running ? "■  STOP" : "▶  RUN";
        }

        private void Changed()
        {
            _view.ForceAcquire();
            Refresh();
        }

        private Style Res(string key) => (Style)FindResource(key);

        private void Section(string text) =>
            _root.Children.Add(new TextBlock { Text = text, Style = Res("Text.Overline"), Margin = new Thickness(0, 12, 0, 5) });

        private WrapPanel Row() => new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };

        private Button Btn(string text, Action click, bool primary = false, bool ghost = false, string? tip = null)
        {
            var b = new Button
            {
                Content = text,
                Margin = new Thickness(0, 0, 6, 4),
                Padding = new Thickness(10, 3, 10, 3),
                FontSize = 12,
                ToolTip = tip
            };
            if (primary) b.Style = Res("Btn.Primary");
            else if (ghost) b.Style = Res("Btn.Ghost");
            b.Click += (_, _) =>
            {
                click();
                Refresh();
            };
            return b;
        }

        private void Stepper(string label, Func<string> value, Action<int> step, Action? reset = null, string resetText = "0")
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5), LastChildFill = false };
            var name = new TextBlock { Text = label, Width = 72, Style = Res("Text.Caption"), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(name);
            Button Small(string t, Action a)
            {
                var b = new Button { Content = t, Width = 26, Height = 24, Padding = new Thickness(0), Margin = new Thickness(0, 0, 3, 0), FontSize = 12 };
                b.Click += (_, _) =>
                {
                    a();
                    _view.ForceAcquire();
                    Refresh();
                };
                return b;
            }

            var text = new TextBlock
            {
                Width = 96,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = (FontFamily)FindResource("F.Mono"),
                FontSize = 12,
                Foreground = (Brush)FindResource("B.Text")
            };
            row.Children.Add(Small("−", () => step(-1)));
            row.Children.Add(text);
            row.Children.Add(Small("+", () => step(1)));
            if (reset != null)
            {
                var r = Small(resetText, reset);
                r.Width = resetText.Length > 1 ? 38 : 26;
                r.Margin = new Thickness(4, 0, 0, 0);
                row.Children.Add(r);
            }

            _refresh.Add(() => text.Text = value());
            _root.Children.Add(row);
        }

        private WrapPanel Chips(string? label, string[] items, Func<int> get, Action<int> set, bool colored = false)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
            if (label != null)
            {
                row.Children.Add(new TextBlock { Text = label, Width = 72, Style = Res("Text.Caption"), VerticalAlignment = VerticalAlignment.Center });
            }

            var buttons = new List<ToggleButton>();
            for (int i = 0; i < items.Length; i++)
            {
                int index = i;
                var b = new ToggleButton
                {
                    Content = items[i],
                    Style = Res("Chip"),
                    Margin = new Thickness(0, 0, 4, 0),
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 11
                };
                if (colored)
                {
                    // [theme] a yellow CH1 read 1.1:1 on the light themes; the colour follows one the user picks
                    void Paint() => b.Foreground = new SolidColorBrush(Shell.Themes.Readable(index < 4 ? ScopeView.ColorOf(index) : ScopeView.MathColor));
                    Paint();
                    Shell.Themes.Changed += Paint;
                    _refresh.Add(Paint);
                }

                b.Click += (_, _) =>
                {
                    set(index);
                    _view.ForceAcquire();
                    Refresh();
                };
                buttons.Add(b);
                row.Children.Add(b);
            }

            _refresh.Add(() =>
            {
                int current = get();
                for (int i = 0; i < buttons.Count; i++) buttons[i].IsChecked = i == current;
            });
            _root.Children.Add(row);
            return row;
        }
    }
}
