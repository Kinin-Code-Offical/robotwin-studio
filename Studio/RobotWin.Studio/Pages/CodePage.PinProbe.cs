using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Firmware;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [crossprobe] The code's pins tied to the circuit: with the caret on a pin of the project's board (a number given to
    /// <c>digitalWrite</c>, a <c>#define</c> or constant used as one), the caret line says the board pin and what it is
    /// wired to ("D9 → R1, Q1"), and that part is selected in the circuit, on the PCB and in the Montaj (the views stay).
    /// </summary>
    public partial class CodePage
    {
        private DispatcherTimer? _pinProbe;
        private string? _pinProbeText;

        private void InitPinProbe()
        {
            _pinProbe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _pinProbe.Tick += (_, _) =>
            {
                _pinProbe.Stop();
                ProbePin();
            };
            Editor.Box.SelectionChanged += (_, _) =>
            {
                _pinProbe.Stop();
                _pinProbe.Start();
            };
        }

        /// <summary>The caret line's text: the line and column, and the pin under the caret with what it is wired to.</summary>
        private void ProbePin()
        {
            _pinProbeText = PinWiring(out var part);
            CaretText.Text = Lang.T($"Satır {Editor.CaretLine}, Sütun {Editor.CaretColumn}") + (_pinProbeText != null ? "  ·  " + _pinProbeText : string.Empty);
            if (part != null) CrossProbe.SelectPart(this, part.Uid, part.RefDes);
        }

        /// <summary>"D9 → R1, Q1" for the pin under the caret on the open board of the project; null when there is none.</summary>
        private string? PinWiring(out SchematicPart? first)
        {
            first = null;
            if (_target == null || !_target.Key.StartsWith("p:", StringComparison.Ordinal)) return null;
            string? pin = PinProbe.PinAt(Editor.Box.Text, Editor.Box.CaretIndex);
            if (pin == null) return null;
            string boardRef = _target.Key.Substring(2);
            var board = W.Schematic.FindPart(boardRef);
            var def = board != null ? W.Library.Find(board.PartId) : null;
            if (def == null) return null;
            string? name = PinProbe.BoardPinNames(pin).FirstOrDefault(n => def.Pins.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)));
            if (name == null) return Lang.T($"pin {pin}: kartta yok");
            Netlist netlist;
            try
            {
                netlist = W.Schematic.BuildNetlist(W.Library);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                return name;
            }

            var net = netlist.NetOf(boardRef, name);
            var others = net?.Connections.Where(c => !string.Equals(c.RefDes, boardRef, StringComparison.OrdinalIgnoreCase))
                .Select(c => W.Schematic.FindPart(c.RefDes)).Where(p => p != null && !IsPowerSymbol(p!)).Select(p => p!).Distinct().ToList() ?? new List<SchematicPart>();
            if (others.Count == 0) return Lang.T($"{name}: bağlı değil");
            first = others[0];
            return name + " → " + string.Join(", ", others.Take(4).Select(p => p.RefDes)) + (others.Count > 4 ? " …" : string.Empty);
        }

        private bool IsPowerSymbol(SchematicPart p) =>
            W.Library.Find(p.PartId)?.Symbol is SymbolKind.Ground or SymbolKind.PowerRail || Annotator.IsPowerSymbol(p.RefDes);
    }
}
