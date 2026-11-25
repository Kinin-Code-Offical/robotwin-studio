using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Industrial;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [plc] The PLC's panel: its Structured Text program (checked as it is written), download into a running PLC,
    /// restart after a fault, and the live state: RUN/STOP, scans, the I/O bits and the program's variables.
    /// </summary>
    public partial class WorldPage
    {
        private void PlcPanel(WorldObject o)
        {
            if (o.Parameters.TryGetValue("language", out var language) && language.Trim() == CellParts.PlcLadder)
            {
                LadderPanel(o);
                return;
            }

            Inspector.Children.Add(Caption("PLC programı (Yapısal Metin)"));
            o.Parameters.TryGetValue("program", out var text);
            var editor = Editor(string.IsNullOrWhiteSpace(text) ? CellParts.PlcExample : text!, 260);
            Inspector.Children.Add(editor);
            var check = Note(string.Empty);
            void Validate()
            {
                var program = StProgram.Compile(editor.Text.Replace("\r", string.Empty), out var errors);
                check.Text = program != null
                    ? $"Program geçerli: {program.Watch.Count()} değişken."
                    : string.Join("\n", errors.Take(4).Select(e => e.ToString()));
                check.Foreground = (Brush)FindResource(program != null ? "B.Success" : "B.Danger");
            }

            Validate();
            editor.TextChanged += (_, _) => Validate();
            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(Small("Kaydet ve PLC'ye yükle", () =>
            {
                string source = editor.Text.Replace("\r", string.Empty);
                Edit("PLC programı kaydedildi", () => o.Parameters["program"] = source);
                if (_session.State != SimState.Stopped && _session.Physics is { } ph)
                {
                    var errors = ph.LoadPlcProgram(o.Id, source);
                    W.Status = errors.Count == 0 ? "PLC programı yüklendi: RUN" : "PLC programı derlenmedi: " + errors[0];
                }
            }, "Dünya çalışıyorsa program PLC'ye de yüklenir ve soğuk başlar", true));
            row.Children.Add(Small("Yeniden başlat", () => _session.Physics?.RestartPlc(o.Id), "Hatadan sonra PLC'yi STOP'tan RUN'a alır (değişkenler baştan)"));
            Inspector.Children.Add(row);
            Inspector.Children.Add(check);
            Inspector.Children.Add(Caption("Durum"));
            StatusBlock();
            Inspector.Children.Add(Note("G/Ç: I0–I7 → %IX0.0–%IX0.7, Q0–Q7 ← %QX0.0–%QX0.7, %MW0–%MW255 bellek sözcükleri. Türler BOOL, INT, DINT, UINT, WORD, REAL, TIME; bloklar TON, TOF, TP, R_TRIG, F_TRIG, CTU, CTD, CTUD, SR, RS."));
        }

        /// <summary>[plc] A ladder PLC: its check, the editor window, restart, and the same live state.</summary>
        private void LadderPanel(WorldObject o)
        {
            Inspector.Children.Add(Caption("PLC programı (Merdiven)"));
            var check = Note(string.Empty);
            void Validate()
            {
                var ladder = LdProgram.Load(o.Parameters.TryGetValue("ladder", out var json) ? json : string.Empty);
                var program = ladder.Compile(out var errors);
                check.Text = program != null ? $"Merdiven geçerli: {ladder.Rungs.Count} basamak." : string.Join("\n", errors.Take(4).Select(e => e.ToString()));
                check.Foreground = (Brush)FindResource(program != null ? "B.Success" : "B.Danger");
            }

            Validate();
            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(Small("Merdiven düzenleyiciyi aç", () =>
            {
                var ladder = LdProgram.Load(o.Parameters.TryGetValue("ladder", out var json) ? json : string.Empty);
                var window = new LadderWindow(ladder, saved =>
                {
                    string text = saved.Save();
                    Edit("Merdiven diyagramı kaydedildi", () => o.Parameters["ladder"] = text);
                    if (_session.State != SimState.Stopped && _session.Physics is { } ph)
                    {
                        var errors = ph.LoadPlcLadder(o.Id, saved);
                        W.Status = errors.Count == 0 ? "PLC programı yüklendi: RUN" : "PLC programı derlenmedi: " + errors[0];
                    }

                    Validate();
                }, name => _session.State == SimState.Stopped ? double.NaN : _session.Physics?.PlcOf(o.Id)?.Program?.Read(name) ?? double.NaN, o.Name)
                {
                    Owner = Window.GetWindow(this)
                };
                window.Show();
            }, "Basamakları çizin: kontaklar, bobinler, zamanlayıcılar, sayıcılar ve paralel kollar", true));
            row.Children.Add(Small("Yeniden başlat", () => _session.Physics?.RestartPlc(o.Id), "Hatadan sonra PLC'yi STOP'tan RUN'a alır (değişkenler baştan)"));
            Inspector.Children.Add(row);
            Inspector.Children.Add(check);
            Inspector.Children.Add(Caption("Durum"));
            StatusBlock();
        }

        private static void PlcStatus(WorldObject o, WorldPhysics? physics, StringBuilder sb)
        {
            var plc = physics?.PlcOf(o.Id);
            if (plc == null)
            {
                sb.Append("Dünya çalışırken PLC durumu burada görünür.");
                return;
            }

            var p = plc.Program;
            if (p == null)
            {
                sb.Append("STOP · program derlenmedi: " + (plc.Errors.FirstOrDefault()?.ToString() ?? string.Empty));
                return;
            }

            sb.AppendLine(p.Fault == null ? $"RUN · {p.Scans} tarama · çevrim {plc.ScanMicros:0.0} µs" : "STOP · " + p.Fault);
            if (plc.Modbus is { } mb) sb.AppendLine($"Modbus     : {mb.Bind}:{mb.Port} · {mb.Image.Requests} istek");   // [modbus]
            sb.AppendLine("I0–I7      : " + string.Concat(Enumerable.Range(0, 8).Select(i => p.Inputs[i] ? '1' : '0')));
            sb.AppendLine("Q0–Q7      : " + string.Concat(Enumerable.Range(0, 8).Select(i => p.Outputs[i] ? '1' : '0')));
            foreach (var (name, _, value) in p.Watch.Where(w => !w.Name.StartsWith("%", System.StringComparison.Ordinal)).Take(16))
                sb.AppendLine($"{name,-10} : {value}");
        }
    }
}
