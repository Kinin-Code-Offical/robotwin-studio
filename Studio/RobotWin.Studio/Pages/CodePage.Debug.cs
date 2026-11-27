using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RobotTwin.CoreSim.Firmware.Debug;
using RobotTwin.CoreSim.Firmware;
using RobotWin.Studio.Firmware;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>[debug] One variable row of the Değişkenler tab.</summary>
    public sealed class DebugVariableRow
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Where { get; set; } = string.Empty;
        public Brush ValueBrush { get; set; } = Shell.ThemeBrushes.Text;
    }

    /// <summary>
    /// [debug] The code page as a debugger: a click in the gutter puts a breakpoint on a line; when the board reaches
    /// it, the circuit or the world it runs in stops at that very instant and the page shows the line, the program's
    /// variables and the commands to go on line by line (Devam F5, Adım F10, İçine F11, Dışına Shift+F11).
    /// </summary>
    public partial class CodePage
    {
        private readonly LookBack<string> _variablesSeen = new LookBack<string>();
        private DispatcherTimer? _debugTimer;
        private bool _debugWired;

        /// <summary>The board whose code is open (breakpoints are kept under its key).</summary>
        private string? DebugKey => (_folder?.Board ?? _target)?.Key;

        /// <summary>The name the build gives an editor file (the main file is always "sketch.ino" there).</summary>
        private string? BuildFile(string? file) => file == null ? null : _folder != null && file == _folder.MainFile ? FirmwareDebugger.MainFile : file;

        private void WireDebugger()
        {
            if (_debugWired) return;
            _debugWired = true;
            Editor.GutterClicked += OnGutterClicked;
            FirmwareDebugger.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateDebug));
            FirmwareDebugger.Stopped += OnProgramStopped;
            _debugTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(400) };
            _debugTimer.Tick += (_, _) =>
            {
                // live values while the program runs (only while the tab is in view)
                if (!IsVisible || DebugKey is not { } key || FirmwareDebugger.ProgramFor(key) is not { IsHalted: false }) return;
                if (_dock == null) return;
                if (_dock.IsShowing("vars")) ShowVariables();
                if (_dock.IsShowing("regs")) ShowRegisters();
                if (_dock.IsShowing("timeline")) ShowTimeline();
            };
            _debugTimer.Start();
        }

        private void OnGutterClicked(int line)
        {
            if (DebugKey is not { } key || BuildFile(_file) is not { } file)
            {
                W.Status = "Kesme noktası için önce bir kartın kodunu açın.";
                return;
            }

            if (ArduinoCompiler.IsEsp32(Fqbn))
            {
                W.Status = "ESP32 çizimi bilgisayarın işlemcisinde yerel olarak çalışır; kesme noktaları şimdilik yalnız Arduino Uno / Nano (ATmega328P) için.";
                return;
            }

            FirmwareDebugger.Toggle(key, file, line);
            int stops = FirmwareDebugger.Resolve(key, file, line);
            bool on = FirmwareDebugger.LinesOf(key, file).Contains(line);
            if (!on) W.Status = $"Kesme noktası kaldırıldı: {_file}:{line}";
            else if (stops == -1) W.Status = $"{_file}:{line} satırında ve sonrasında çalışan kod yok: kart burada duramaz.";
            else if (stops > 0 && stops != line) W.Status = $"Kesme noktası: {_file}:{line} — bu satırda kod yok, kart {stops}. satırda durur.";
            else W.Status = $"Kesme noktası: {_file}:{line} — kart bu satıra gelince devre ve dünya aynı anda durur.";
        }

        private void OnProgramStopped(DebugProgram p)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (p.Key != DebugKey) ShowBoard(p.Key);
                if (p.Debugger.Place is { InSketch: true } place && _folder != null)
                {
                    string editorFile = place.File == FirmwareDebugger.MainFile ? _folder.MainFile : place.File;
                    if (editorFile != _file && _folder.Files.Contains(editorFile)) Open(_folder, editorFile, rebuild: true);
                    Editor.RevealLine(place.Line);
                }

                if (_dock != null && !_dock.IsShowing("regs") && !_dock.IsShowing("timeline")) _dock.Reveal("vars");
                UpdateDebug();
                string why = p.Debugger.Reason == DebugStopReason.Step ? "adım" : "kesme noktası";
                W.Status = $"{p.Where} durdu ({why}): {StopText(p)} — devre ve dünya da bu anda duruyor.";
            }));
        }

        private static string StopText(DebugProgram p)
        {
            var place = p.Debugger.Place;
            string at = place is { } pl ? (pl.InSketch ? pl.File : System.IO.Path.GetFileName(pl.File)) + ":" + pl.Line.ToString(CultureInfo.InvariantCulture) : "0x" + p.Cosim.Mcu.ProgramCounter.ToString("X4", CultureInfo.InvariantCulture);
            string fn = p.Debugger.Function is { Length: > 0 } f ? " · " + f + "()" : string.Empty;
            return $"{at}{fn} · t = {p.Time().ToString("0.000000", CultureInfo.CurrentCulture)} s";
        }

        /// <summary>Breakpoint marks, the stopped line and the debug bar for the open file.</summary>
        private void UpdateDebug()
        {
            string? key = DebugKey, file = BuildFile(_file);
            if (key == null || file == null)
            {
                Editor.SetBreakpoints(Array.Empty<(int, bool)>());
                Editor.ExecutionLine = 0;
                DebugBar.Visibility = Visibility.Collapsed;
                return;
            }

            Editor.SetBreakpoints(FirmwareDebugger.LinesOf(key, file).Select(l => (l, FirmwareDebugger.Resolve(key, file, l) != -1)));
            var p = FirmwareDebugger.ProgramFor(key);
            var place = p is { IsHalted: true } ? p.Debugger.Place : null;
            Editor.ExecutionLine = place is { InSketch: true } pl && string.Equals(pl.File, file, StringComparison.OrdinalIgnoreCase) ? pl.Line : 0;

            int count = FirmwareDebugger.CountOf(key);
            bool paused = p != null && (p.IsHalted || p.Owner is World.WorldSession { State: Simulation.SimState.Paused } || p.Owner is Simulation.SimulationSession { State: Simulation.SimState.Paused });
            if (p == null && count == 0)
            {
                DebugBar.Visibility = Visibility.Collapsed;
                ShowVariables();
                ShowRegisters();
                ShowTimeline();
                return;
            }

            DebugBar.Visibility = Visibility.Visible;
            bool lines = p?.Info != null;
            DebugContinue.IsEnabled = paused;
            DebugBreak.IsEnabled = p != null && !paused && lines;
            DebugOver.IsEnabled = DebugInto.IsEnabled = DebugOut.IsEnabled = paused && lines;
            DebugClear.IsEnabled = count > 0;
            DebugDot.Fill = (Brush)FindResource(p == null ? "B.TextSecondary" : p.IsHalted ? "B.Warning" : "B.Success");
            string marks = count == 0 ? "kesme noktası yok (satır numarasına tıklayın)" : count + " kesme noktası";
            if (p == null) DebugState.Text = $"{marks} · kart çalışmıyor: Karta yükle ve çalıştır (F5) ile başlayın";
            else if (p.IsHalted) DebugState.Text = $"Durdu · {StopText(p)}";
            else if (!lines) DebugState.Text = "Çalışıyor · bu yazılım burada derlenmedi; satırda durmak için Karta yükle ve çalıştır (F5) ile yeniden derleyin";
            else if (paused) DebugState.Text = $"Duraklatıldı · {marks} · Adım ile kodun bir sonraki satırına gidin";
            else DebugState.Text = $"Çalışıyor · {marks}";
            if (p != null && lines && FirmwareFor(key) is { IsBuildCurrent: false }) DebugState.Text += " · kod son yüklemeden sonra değişti: satırlar kayabilir";
            ShowVariables();
            ShowRegisters();
            ShowTimeline();
        }

        private RobotTwin.CoreSim.Formats.FirmwareSource? FirmwareFor(string key) => _targets.FirstOrDefault(t => t.Key == key)?.Firmware;

        /// <summary>[stack] The frame picked in the call stack (0: where the program stopped).</summary>
        private int _frameIndex;

        private (string Key, ulong Cycles, IReadOnlyList<RobotTwin.CoreSim.Firmware.Debug.DebugFrame> Frames)? _stack;

        /// <summary>[stack] The stopped program's call stack (read once per stop).</summary>
        private IReadOnlyList<RobotTwin.CoreSim.Firmware.Debug.DebugFrame> StackOf(DebugProgram p)
        {
            var mcu = p.Cosim.Mcu;
            if (_stack is { } s && s.Key == p.Key && s.Cycles == mcu.Cycles) return s.Frames;
            var frames = p.Info!.Stack(mcu.ProgramCounter, mcu.StackPointer, mcu.ReadData);
            _stack = (p.Key, mcu.Cycles, frames);
            _frameIndex = 0;
            return frames;
        }

        /// <summary>
        /// [stack] The call stack above the variables, innermost first: a function inlined into its caller is marked so;
        /// a click picks the frame (its locals below, its line in the editor).
        /// </summary>
        private void ShowCallStack(DebugProgram? p)
        {
            CallStackList.Items.Clear();
            if (p == null || p.Info == null || !p.IsHalted)
            {
                CallStackPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var frames = StackOf(p);
            CallStackPanel.Visibility = frames.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                int index = i;
                string where = f.Place is { } pl ? (pl.InSketch ? pl.File : System.IO.Path.GetFileName(pl.File)) + ":" + pl.Line.ToString(CultureInfo.InvariantCulture) : "?";
                var text = new TextBlock
                {
                    Text = (f.Interrupt.Length > 0 ? Lang.T($"{f.Interrupt} kesmesi") : f.Function + "()") + "  ·  " + where + (f.Inlined ? "  ·  " + Lang.T("satır içi") : string.Empty),
                    FontFamily = (FontFamily)FindResource("F.Mono"),
                    FontSize = Shell.TypeScale.Caption,
                    FontWeight = i == _frameIndex ? FontWeights.SemiBold : FontWeights.Normal,
                };
                text.SetResourceReference(TextBlock.ForegroundProperty, i == _frameIndex ? "B.Text" : f.Place is { InSketch: true } ? "B.TextSecondary" : "B.TextMuted");
                var row = new Button { Content = text, Style = (Style)FindResource("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 1, 6, 1), MinHeight = 20 };
                row.ToolTip = Lang.T(f.Interrupt.Length > 0 ? "Kesme işleyicisi: altındaki satır, kesmenin böldüğü kod (döneceği komutta)"
                    : f.Inlined ? "Bu işlevin kodu çağıranın içine gömülü (satır içi): yongada ayrı bir çağrısı yok" : "Bir çağrı: çağıranın kaydettiği dönüş adresinden ve yazmaçlardan çözüldü");
                row.Click += (_, _) => PickFrame(p, index);
                CallStackList.Items.Add(row);
            }
        }

        private void PickFrame(DebugProgram p, int index)
        {
            _frameIndex = index;
            var frames = StackOf(p);
            if (index < frames.Count && frames[index].Place is { InSketch: true } place && _folder != null)
            {
                string editorFile = place.File == FirmwareDebugger.MainFile ? _folder.MainFile : place.File;
                if (editorFile != _file && _folder.Files.Contains(editorFile)) Open(_folder, editorFile, rebuild: true);
                Editor.RevealLine(place.Line);
            }

            ShowVariables();
        }

        /// <summary>The Değişkenler tab: the program's globals and statics read from its RAM (changes since the last look in colour).</summary>
        private void ShowVariables()
        {
            var p = DebugKey is { } key ? FirmwareDebugger.ProgramFor(key) : null;
            ShowCallStack(p);
            if (p == null)
            {
                VariablesList.ItemsSource = null;
                VariablesNote.Text = "Kart çalışırken programın değişkenleri burada görünür; bir kesme noktasında durduğunda o anki değerleriyle.";
                return;
            }

            if (p.Info == null)
            {
                VariablesList.ItemsSource = null;
                VariablesNote.Text = "Bu yazılım burada derlenmedi (örneğin hazır bir örneğin yazılımı): değişkenleri görmek için Karta yükle ve çalıştır (F5) ile derleyin.";
                return;
            }

            var normal = (Brush)FindResource("B.Text");
            var changed = (Brush)FindResource("B.Warning");
            var rows = new List<DebugVariableRow>();
            _variablesSeen.At(p.Key, p.Cosim.Mcu.Cycles);
            // [stack] the picked frame's parameters and locals first (where the compiler keeps them at this address)
            int locals = 0;
            if (p.IsHalted)
            {
                var frames = StackOf(p);
                if (_frameIndex < frames.Count)
                {
                    var frame = frames[_frameIndex];
                    var muted = (Brush)FindResource("B.TextMuted");
                    foreach (var l in frame.Locals)
                    {
                        locals++;
                        rows.Add(new DebugVariableRow
                        {
                            Name = l.Name,
                            Value = l.Available ? l.Value : Lang.T("— (iyileştirildi)"),
                            Type = l.TypeName,
                            Address = Lang.T(l.Where),   // "değer": made from others
                            Where = Lang.T(l.IsParameter ? "parametre" : "yerel") + " · " + frame.Function + "()",
                            ValueBrush = l.Available ? normal : muted,
                        });
                    }
                }
            }

            foreach (var v in p.Info.Variables)
            {
                string value = FirmwareDebugInfo.Format(v, p.Cosim.Mcu.ReadData);
                bool differs = _variablesSeen.Changed(v.DisplayName, value);
                rows.Add(new DebugVariableRow
                {
                    Name = v.DisplayName,
                    Value = value,
                    Type = v.Type.Name.Length > 0 ? v.Type.Name : "?",
                    Address = "0x" + v.Address.ToString("X3", CultureInfo.InvariantCulture),
                    Where = v.File + ":" + v.Line.ToString(CultureInfo.InvariantCulture),
                    ValueBrush = differs ? changed : normal,
                });
            }

            VariablesList.ItemsSource = rows;
            if (locals > 0)
            {
                VariablesNote.Text = Lang.T("Önce seçili çerçevenin parametreleri ve yerelleri (yazmaçta, yığında ya da derleyicinin başkalarından hesapladığı değer), sonra global ve static değişkenler; \"iyileştirildi\": derleyici bu noktada değerini tutmuyor.");
                return;
            }

            VariablesNote.Text = rows.Count == 0
                ? "Kodunuzda sabit adresli (global ya da static) değişken yok."
                : (p.IsHalted ? "Değerler kartın durduğu andaki RAM'den okunuyor" : "Değerler çalışırken RAM'den okunuyor") + "; turuncu: son bakıştan beri değişen.";
        }

        /// <summary>[debug] The line the editor marks as where the stopped program is (for checks).</summary>
        internal int ExecutionLineShown => Editor.ExecutionLine;

        private DebugProgram? Current() => DebugKey is { } key ? FirmwareDebugger.ProgramFor(key) : null;

        private void OnDebugContinue(object sender, RoutedEventArgs e)
        {
            if (Current() is { } p) FirmwareDebugger.Continue(p);
        }

        private void OnDebugBreak(object sender, RoutedEventArgs e)
        {
            if (Current() is { } p && !FirmwareDebugger.BreakNow(p)) W.Status = "Bu yazılımın satır bilgisi yok: Karta yükle ve çalıştır ile derleyin.";
        }

        private void OnDebugOver(object sender, RoutedEventArgs e) => DebugStep(DebugStepKind.Over);

        private void OnDebugInto(object sender, RoutedEventArgs e) => DebugStep(DebugStepKind.Into);

        private void OnDebugOut(object sender, RoutedEventArgs e) => DebugStep(DebugStepKind.Out);

        private void OnDebugClear(object sender, RoutedEventArgs e)
        {
            if (DebugKey is { } key) FirmwareDebugger.ClearAll(key);
        }

        private void DebugStep(DebugStepKind kind)
        {
            if (Current() is not { } p) return;
            if (!FirmwareDebugger.Step(p, kind)) W.Status = "Bu yazılımın satır bilgisi yok: Karta yükle ve çalıştır ile derleyin.";
        }

        /// <summary>F5 (go on), F10, F11, Shift+F11 while the board is stopped; false when the key is not the debugger's.</summary>
        /// <summary>[debug] The stepping keys while the board is stopped (the key as the page knows it, after the user's own keys).</summary>
        private bool DebugKeys(Key key, ModifierKeys mods)
        {
            var p = Current();
            bool paused = p != null && (p.IsHalted || p.Owner is World.WorldSession { State: Simulation.SimState.Paused } || p.Owner is Simulation.SimulationSession { State: Simulation.SimState.Paused });
            if (!paused) return false;
            switch (key)
            {
                case Key.F5 when mods == ModifierKeys.None:
                    FirmwareDebugger.Continue(p!);
                    return true;
                case Key.F10 when mods == ModifierKeys.None:
                    DebugStep(DebugStepKind.Over);
                    return true;
                case Key.F11 when mods == ModifierKeys.None:
                    DebugStep(DebugStepKind.Into);
                    return true;
                case Key.F11 when mods == ModifierKeys.Shift:
                    DebugStep(DebugStepKind.Out);
                    return true;
            }

            return false;
        }
    }
}
