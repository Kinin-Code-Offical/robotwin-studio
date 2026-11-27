using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Editor
{
    /// <summary>
    /// [layers] The circuit's simulation layers (D3), each switched on or off from the toolbar: the wires coloured by their
    /// voltage, the current's flow along the parts, how hard each part works (the highest of its ratings in use: power,
    /// current, voltage, temperature; a glow from amber near the limit to red over it, with its percentage) and the faults
    /// (a burnt part ringed in red with what burnt it, findable when zoomed out).
    /// </summary>
    public sealed partial class SchematicCanvas
    {
        /// <summary>Glows round the parts working near or over a limit.</summary>
        private void DrawStressLayer(DrawingContext dc)
        {
            foreach (var part in SheetParts)
            {
                var devices = Sim.Compiled?.DevicesOf(part.RefDes);
                if (devices == null || devices.Count == 0 || devices.Any(d => d.IsFailed)) continue;
                double ratio = 0;
                string what = string.Empty;
                foreach (var d in devices)
                {
                    foreach (var s in CircuitDiagnostics.Stresses(d))
                    {
                        if (double.IsNaN(s.Ratio) || s.Ratio <= ratio) continue;
                        ratio = s.Ratio;
                        what = s.Quantity;
                    }
                }

                if (ratio < 0.6) continue;   // comfortable: nothing drawn, so the sheet stays readable
                var r = ScreenRect(part, 0.5);
                double t = Math.Min(1, (ratio - 0.6) / 0.4);   // 0.6 → 1.0 of the limit: amber → red
                var c = ratio >= 1 ? CanvasColors.StressHot : Blend(CanvasColors.StressWarm, CanvasColors.StressHot, t);
                dc.DrawRoundedRectangle(SymbolRenderer.Brush(Color.FromArgb((byte)(0x22 + 0x30 * t), c.R, c.G, c.B)), MakePen(Color.FromArgb(0xC0, c.R, c.G, c.B), 1.2, false), r, 6, 6);
                var label = Text(Lang.T(what) + $" %{ratio * 100:0}", 10.5, c, false, false);
                dc.DrawText(label, new Point(r.Right - label.Width, r.Top - label.Height - 2));
            }
        }

        /// <summary>A red ring and the reason round each burnt part.</summary>
        private void DrawFaultLayer(DrawingContext dc)
        {
            var red = CanvasColors.StressHot;
            foreach (var part in SheetParts)
            {
                var devices = Sim.Compiled?.DevicesOf(part.RefDes);
                var failed = devices?.FirstOrDefault(d => d.IsFailed);
                if (failed == null) continue;
                var r = ScreenRect(part, 0.7);
                dc.DrawRoundedRectangle(null, MakePen(red, 2, false), r, 8, 8);
                string why = Lang.T(string.IsNullOrWhiteSpace(failed.FailureReason) ? "hasarlı" : failed.FailureReason!);
                var label = Text("⚠ " + part.RefDes + ": " + why, 11, red, true, false);
                var at = new Point(r.Left, r.Bottom + 3);
                dc.DrawRoundedRectangle(SymbolRenderer.Brush(PanelColor), null, new Rect(at.X - 4, at.Y - 2, label.Width + 8, label.Height + 4), 4, 4);
                dc.DrawText(label, at);
            }
        }

        // [layers] the digital nodes of the compiled circuit (an MCU's pins, a gate's inputs and output) with their supply
        private object? _digitalFor;
        private Dictionary<int, (int Supply, int Ground)> _digital = new Dictionary<int, (int, int)>();

        private Dictionary<int, (int Supply, int Ground)> DigitalNodes()
        {
            var compiled = Sim.Compiled;
            if (compiled == null) return _digital;
            if (ReferenceEquals(_digitalFor, compiled)) return _digital;
            var map = new Dictionary<int, (int, int)>();
            foreach (var d in compiled.Circuit.Devices)
            {
                switch (d)
                {
                    case DigitalPin p:
                        map[p.Pin] = (p.Supply, p.Ground);
                        break;
                    case LogicGate g:
                        map[g.Output] = (g.Supply, g.Ground);
                        foreach (int i in g.Inputs) map[i] = (g.Supply, g.Ground);
                        break;
                }
            }

            map.Remove(0);   // the ground is no signal
            _digital = map;
            _digitalFor = compiled;
            return map;
        }

        /// <summary>
        /// The logic level on each digital net, once per net on the shown sheet: 1 at 70% of the supply or more, 0 at 30% or
        /// less, ? between (a floating input, a weak pull, two outputs fighting), drawn at the middle of its longest wire.
        /// </summary>
        private void DrawDigitalLayer(DrawingContext dc, Netlist netlist)
        {
            var sim = Sim;
            if (sim.Compiled == null || sim.Simulator == null) return;
            var nodes = DigitalNodes();
            if (nodes.Count == 0) return;
            var best = new Dictionary<string, (Point A, Point B, double Length)>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Doc.Wires.Count && i < netlist.WireNets.Count; i++)
            {
                var wire = Doc.Wires[i];
                string? net = netlist.WireNets[i];
                if (net == null || wire.Bus || !Here(wire) || !sim.Compiled.NodeByNet.TryGetValue(net, out int node) || !nodes.ContainsKey(node)) continue;
                for (int k = 0; k + 1 < wire.Points.Count; k++)
                {
                    var a = ToScreen(wire.Points[k]);
                    var b = ToScreen(wire.Points[k + 1]);
                    double len = (b - a).Length;
                    if (!best.TryGetValue(net, out var cur) || len > cur.Length) best[net] = (a, b, len);
                }
            }

            foreach (var kv in best)
            {
                int node = sim.Compiled.NodeByNet[kv.Key];
                var (supply, ground) = nodes[node];
                double vdd = sim.Simulator.NodeVoltage(supply) - (ground > 0 ? sim.Simulator.NodeVoltage(ground) : 0);
                if (vdd < 0.5) continue;   // the logic is not powered
                double v = sim.Simulator.NodeVoltage(node) - (ground > 0 ? sim.Simulator.NodeVoltage(ground) : 0);
                string level = v >= 0.7 * vdd ? "1" : v <= 0.3 * vdd ? "0" : "?";
                var color = level == "1" ? CanvasColors.LogicHigh : level == "0" ? CanvasColors.LogicLow : CanvasColors.LogicUndefined;
                var at = new Point((kv.Value.A.X + kv.Value.B.X) / 2, (kv.Value.A.Y + kv.Value.B.Y) / 2);
                var ft = Text(level, 10, Colors.White, true, true);
                var r = new Rect(at.X - 7, at.Y - 7, 14, 14);
                dc.DrawRoundedRectangle(SymbolRenderer.Brush(color), null, r, 3, 3);
                dc.DrawText(ft, new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2));
            }
        }

        private Rect ScreenRect(SchematicPart part, double margin)
        {
            var b = PartBounds(part);
            b.Inflate(margin, margin);
            var a = ToScreen(b.Left, b.Top);
            var c = ToScreen(b.Right, b.Bottom);
            return new Rect(a, c);
        }

        private static Color Blend(Color a, Color b, double t) =>
            Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }
}
