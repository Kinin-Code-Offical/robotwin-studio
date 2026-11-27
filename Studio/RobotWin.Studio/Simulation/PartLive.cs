using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Simulation
{
    /// <summary>
    /// [crossprobe] A part's simulation values in a few short lines, for the pages that show a part picked anywhere (the
    /// PCB's selection panel; the circuit's inspector has its own fuller rows): what flows through it, a MOSFET's gate, a
    /// servo's angle, a motor's speed, its hottest junction, the rating it is closest to, and its pads' voltages.
    /// Translated line by line; empty when nothing runs or the part is not in the circuit.
    /// </summary>
    public static class PartLive
    {
        public static List<string> Lines(SimulationSession sim, string refDes, IEnumerable<KeyValuePair<string, string>>? padNets = null, int maxPads = 8)
        {
            var lines = new List<string>();
            if (!sim.IsActive || sim.Compiled == null || sim.Simulator == null || string.IsNullOrEmpty(refDes)) return lines;
            var devices = sim.Compiled.DevicesOf(refDes).ToList();
            if (devices.Count == 0) return lines;
            var c = CultureInfo.CurrentCulture;
            double temp = double.NaN;
            DeviceStress? worst = null;
            bool failed = false;
            foreach (var d in devices)
            {
                string p = devices.Count > 1 ? d.Id + ": " : string.Empty;
                switch (d)
                {
                    case Mosfet m:
                        lines.Add(p + T($"ID {SiValue.Format(m.DrainCurrent, "A")} · VGS {SiValue.Format(Node(m.Gate) - Node(m.Source), "V")}"));
                        break;
                    case Servo s:
                        lines.Add(p + T(string.Format(c, "açı {0:0.0}° (hedef {1}) · {2}", s.AngleDeg,
                            double.IsNaN(s.TargetAngleDeg) ? "—" : s.TargetAngleDeg.ToString("0.0", c) + "°", SiValue.Format(s.SupplyCurrent, "A"))));
                        break;
                    case DcMotor m:
                        lines.Add(p + T(string.Format(c, "{0:0} rpm · {1} · {2}", m.OutputRpm, SiValue.Format(m.Current, "A"), SiValue.Format(m.Voltage, "V"))));
                        break;
                    default:
                        double i = SimulationSession.DeviceCurrent(d);
                        if (!double.IsNaN(i)) lines.Add(p + T($"akım {SiValue.Format(i, "A")}"));
                        break;
                }

                if (sim.Simulator.Signals.TryGetValue(d.Id + ":T", out var tv)) temp = double.IsNaN(temp) ? tv : Math.Max(temp, tv);
                foreach (var st in CircuitDiagnostics.Stresses(d))
                    if (worst == null || st.Ratio > worst.Value.Ratio) worst = st;
                failed |= d.IsFailed;
            }

            if (!double.IsNaN(temp)) lines.Add(T(string.Format(c, "sıcaklık {0:0.0} °C", temp)));
            if (worst is { } w)
                lines.Add(T(string.Format(c, "en yakın sınır: {0} {1} / {2} (%{3:0})", Shell.Lang.T(w.Quantity), SiValue.Format(w.Value, w.Unit), SiValue.Format(w.Rating, w.Unit), w.Ratio * 100)));
            if (failed) lines.Add(T("ARIZALI (yandı / bozuldu)"));
            if (padNets != null)
            {
                foreach (var kv in padNets.OrderBy(k => k.Key, StringComparer.Ordinal).Take(maxPads))
                {
                    double v = sim.NetVoltage(kv.Value);
                    if (!double.IsNaN(v)) lines.Add(T($"ped {kv.Key} ({kv.Value}): {SiValue.Format(v, "V")}"));
                }
            }

            return lines;

            double Node(int node) => node <= 0 ? 0 : sim.Simulator!.NodeVoltage(node);
        }

        private static string T(string s) => Shell.Lang.T(s);
    }
}
