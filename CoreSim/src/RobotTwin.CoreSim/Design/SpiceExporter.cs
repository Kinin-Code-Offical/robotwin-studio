using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// SPICE3 netlist (.cir) of a schematic, for cross-checking our simulator with ngspice,
    /// LTspice or KiCad's simulator. Every part is mapped with the same parameter keys,
    /// defaults and derived values its <see cref="PartDefinition.Build"/> uses (parsed with
    /// <see cref="SiValue"/>), so the two simulators see the same circuit:
    /// R/C/L with their ESR/DCR/leakage, diodes/zeners/LEDs as D with a .model, BJTs as
    /// Gummel-Poon Q models, MOSFETs as level-1 M models with the body diode, batteries and
    /// supplies as V sources behind their series resistance, switches as resistors.
    /// Ground nets become node 0. Parts without a SPICE equivalent (regulators, op-amps,
    /// motors, boards, ...) are listed as "* &lt;RefDes&gt; &lt;PartId&gt; not exported" comments.
    /// </summary>
    public static class SpiceExporter
    {
        public static string ExportSpice(Schematic schematic, PartLibrary library)
        {
            if (schematic == null) throw new ArgumentNullException(nameof(schematic));
            if (library == null) throw new ArgumentNullException(nameof(library));
            return new Writer(schematic, library).Run();
        }

        private sealed class Writer
        {
            private readonly Schematic _schematic;
            private readonly PartLibrary _library;
            private readonly Netlist _netlist;
            private readonly Dictionary<string, string> _nodeOfNet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> _netOfPin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _nodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "0" };
            private readonly HashSet<string> _elements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> _modelBySignature = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly HashSet<string> _modelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly List<string> _models = new List<string>();
            private readonly StringBuilder _body = new StringBuilder();
            private readonly List<double> _frequencies = new List<double>();

            // Current part.
            private SchematicPart _part = null!;
            private PartDefinition _def = null!;
            private IReadOnlyDictionary<string, string> _parameters = null!;

            public Writer(Schematic schematic, PartLibrary library)
            {
                _schematic = schematic;
                _library = library;
                _netlist = schematic.BuildNetlist(library);
            }

            public string Run()
            {
                var grounds = _netlist.Nets.Where(n => n.IsGround).Select(n => n.Name).ToList();
                foreach (var net in _netlist.Nets)
                {
                    _nodeOfNet[net.Name] = net.IsGround ? "0" : UniqueNode(NodeName(net.Name));
                    foreach (var c in net.Connections) _netOfPin[Key(c.RefDes, c.Pin)] = net.Name;
                }

                foreach (var part in _schematic.Parts)
                {
                    var def = _library.Find(part.PartId);
                    if (def == null)
                    {
                        _body.Append("* ").Append(part.RefDes).Append(' ').Append(part.PartId).Append(" not exported (unknown part)\n");
                        continue;
                    }

                    if (def.Symbol == SymbolKind.Ground || def.Symbol == SymbolKind.PowerRail) continue;   // net names only
                    _part = part;
                    _def = def;
                    _parameters = _schematic.ResolveParameters(_library, part);
                    if (!Emit()) _body.Append("* ").Append(part.RefDes).Append(' ').Append(part.PartId).Append(" not exported\n");
                }

                var sb = new StringBuilder();
                sb.Append("* ").Append(OneLine(_schematic.Title)).Append('\n');
                sb.Append("* SPICE3 netlist exported by RobotWin Studio\n");
                sb.Append(grounds.Count > 0
                    ? "* Ground: " + string.Join(", ", grounds) + " -> node 0\n"
                    : "* WARNING: no ground net (GND); SPICE needs a node 0 reference\n");
                sb.Append(_body);
                foreach (string model in _models) sb.Append(model).Append('\n');

                sb.Append(".op\n");
                if (_frequencies.Count > 0)
                {
                    double fMin = _frequencies.Min(), fMax = _frequencies.Max();
                    sb.Append(".tran ").Append(F(1.0 / (fMax * 100.0))).Append(' ').Append(F(5.0 / fMin)).Append('\n');
                }
                else
                {
                    sb.Append(".tran 1E-05 0.01\n");
                }

                sb.Append(".end\n");
                return sb.ToString();
            }

            // ------------------------------------------------------------------ part mapping

            private bool Emit()
            {
                switch (_def.Symbol)
                {
                    case SymbolKind.Resistor:
                        if (!Has("A", "B")) return false;
                        Line(Element('R'), Pin("A"), Pin("B"), F(Num("resistance", 1000)));
                        return true;

                    case SymbolKind.Potentiometer:
                    {
                        if (!Has("A", "W", "B")) return false;
                        double r = Num("resistance", 10000), pos = Clamp(Num("position", 0.5), 0, 1);
                        double f = Text("taper") == "log" ? (Math.Pow(81.0, pos) - 1.0) / 80.0 : pos;
                        const double Wiper = 1.0;
                        Line(Element('R', "A"), Pin("A"), Pin("W"), F(Math.Max(r * f, 0.0) + Wiper));
                        Line(Element('R', "B"), Pin("W"), Pin("B"), F(Math.Max(r * (1.0 - f), 0.0) + Wiper));
                        return true;
                    }

                    case SymbolKind.Thermistor:
                    {
                        if (!Has("A", "B")) return false;
                        double t = Num("temperature", 25) + 273.15, t0 = 25.0 + 273.15;
                        double r = Num("resistance", 10000) * Math.Exp(Num("beta", 3950) * (1.0 / t - 1.0 / t0));
                        Line(Element('R'), Pin("A"), Pin("B"), F(r));
                        return true;
                    }

                    case SymbolKind.Photoresistor:
                    {
                        if (!Has("A", "B")) return false;
                        const double Dark = 1e6;
                        double lux = Num("lux", 100);
                        double r = lux <= 0.01 ? Dark : Math.Min(Dark, Num("resistance", 10000) * Math.Pow(lux / 10.0, -Num("gamma", 0.7)));
                        Line(Element('R'), Pin("A"), Pin("B"), F(r));
                        return true;
                    }

                    case SymbolKind.Fuse:
                        if (!Has("A", "B")) return false;
                        Line(Element('R'), Pin("A"), Pin("B"), F(0.05));   // Fuse.ColdResistance
                        return true;

                    case SymbolKind.Capacitor:
                    case SymbolKind.PolarCapacitor:
                    {
                        bool polar = _def.Symbol == SymbolKind.PolarCapacitor;
                        string a = polar ? "+" : "A", b = polar ? "-" : "B";
                        if (!Has(a, b)) return false;
                        double esr = Num("esr", polar ? 0.1 : 0.0);
                        string plate = Pin(a);
                        if (esr > 0)
                        {
                            plate = Internal("esr");
                            Line(Element('R', "ESR"), Pin(a), plate, F(esr));
                        }

                        Line(Element('C'), plate, Pin(b), F(Num("capacitance", polar ? 100e-6 : 100e-9)));
                        if (polar) Line(Element('R', "LEAK"), plate, Pin(b), F(1e7));
                        return true;
                    }

                    case SymbolKind.Inductor:
                    {
                        if (!Has("A", "B")) return false;
                        string mid = Internal("dcr");
                        Line(Element('L'), Pin("A"), mid, F(Num("inductance", 100e-6)));
                        Line(Element('R', "DCR"), mid, Pin("B"), F(Math.Max(1e-3, Num("dcr", 0.1))));
                        return true;
                    }

                    case SymbolKind.Diode:
                    case SymbolKind.Schottky:
                    {
                        if (!Has("A", "K")) return false;
                        double bv = Num("bv", 0), cj = Num("cj", 0);
                        string body = "IS=" + F(Num("is", 2.5e-9)) + " N=" + F(Num("n", 1.75)) + " RS=" + F(Num("rs", 0.5)) +
                                      (bv > 0 ? " BV=" + F(bv) + " IBV=" + F(Num("ibv", 5e-3)) : string.Empty) +
                                      (cj > 0 ? " CJO=" + F(cj) : string.Empty) + " EG=" + F(Num("eg", 1.11));
                        Line(Element('D'), Pin("A"), Pin("K"), Model("D", "D_" + ModelBase(), body));
                        return true;
                    }

                    case SymbolKind.Zener:
                    {
                        if (!Has("A", "K")) return false;
                        string body = "IS=1E-14 N=1 RS=" + F(Num("rz", 2)) + " BV=" + F(Num("voltage", 5.1)) + " IBV=" + F(Num("izt", 5e-3));
                        Line(Element('D'), Pin("A"), Pin("K"), Model("D", "DZ_" + Ident(Text("voltage")), body));
                        return true;
                    }

                    case SymbolKind.Led:
                    {
                        if (!Has("A", "K")) return false;
                        Led led;
                        try
                        {
                            led = new Led(_part.RefDes, 0, 1, Num("vf", 2.0), Num("if", 0.02));
                        }
                        catch (ArgumentException)
                        {
                            return false;
                        }

                        string body = "IS=" + F(led.SaturationCurrent) + " N=" + F(led.Ideality) + " RS=" + F(led.SeriesResistance) + " EG=" + F(led.BandGap);
                        Line(Element('D'), Pin("A"), Pin("K"), Model("D", "LED_" + Ident(Text("color").Length > 0 ? Text("color") : "generic"), body));
                        return true;
                    }

                    case SymbolKind.Npn:
                    case SymbolKind.Pnp:
                    {
                        if (!Has("C", "B", "E")) return false;
                        double cje = Num("cje", 0), cjc = Num("cjc", 0);
                        string body = "IS=" + F(Num("is", 1e-14)) + " BF=" + F(Num("bf", 200)) + " BR=" + F(Num("br", 2)) + " VAF=" + F(Num("va", 100)) +
                                      (cje > 0 ? " CJE=" + F(cje) : string.Empty) + (cjc > 0 ? " CJC=" + F(cjc) : string.Empty);
                        string kind = _def.Symbol == SymbolKind.Npn ? "NPN" : "PNP";
                        Line(Element('Q'), Pin("C"), Pin("B"), Pin("E"), Model(kind, "Q_" + ModelBase(), body));
                        return true;
                    }

                    case SymbolKind.Darlington:
                    {
                        if (!Has("C", "B", "E")) return false;
                        string mid = Internal("mid");
                        string input = Model("NPN", "Q_" + ModelBase() + "_IN", "IS=1E-14 BF=" + F(Num("bf1", 150)) + " BR=2 VAF=100");
                        string output = Model("NPN", "Q_" + ModelBase() + "_OUT", "IS=1E-12 BF=" + F(Num("bf2", 40)) + " BR=2 VAF=100");
                        Line(Element('Q', "1"), Pin("C"), Pin("B"), mid, input);
                        Line(Element('Q', "2"), Pin("C"), mid, Pin("E"), output);
                        Line(Element('R', "BE"), mid, Pin("E"), F(8000));
                        return true;
                    }

                    case SymbolKind.Nmos:
                    case SymbolKind.Pmos:
                    {
                        if (!Has("D", "G", "S")) return false;
                        bool p = _def.Symbol == SymbolKind.Pmos;
                        double vth = Num("vth", 2), overdrive = Math.Abs(Num("vgsrdson", 10)) - vth;
                        if (overdrive <= 0) return false;   // Mosfet.SetOnResistance rejects it too
                        double kp = 1.0 / (Num("rdson", 0.1) * overdrive);
                        double cgs = Num("cgs", 0), cgd = Num("cgd", 0);

                        // Level 1 with W = L = 1 m: KP is the device's K (A/V^2) and CGSO/CGDO (F/m) are
                        // the gate capacitances in farads; no TOX, so no extra Meyer capacitance.
                        string body = "LEVEL=1 VTO=" + F(p ? -vth : vth) + " KP=" + F(kp) + " LAMBDA=0.01" +
                                      (cgs > 0 ? " CGSO=" + F(cgs) : string.Empty) + (cgd > 0 ? " CGDO=" + F(cgd) : string.Empty);
                        string model = Model(p ? "PMOS" : "NMOS", "M_" + ModelBase(), body);
                        Line(Element('M'), Pin("D"), Pin("G"), Pin("S"), Pin("S"), model, "W=1 L=1");
                        string bodyDiode = Model("D", "D_BODY", "IS=1E-11 N=1.2 RS=0.02");
                        Line(Element('D', "BODY"), p ? Pin("D") : Pin("S"), p ? Pin("S") : Pin("D"), bodyDiode);
                        return true;
                    }

                    case SymbolKind.Battery:
                    {
                        if (!Has("+", "-")) return false;
                        if (!Enum.TryParse(Text("chemistry"), true, out BatteryChemistry chemistry)) return false;
                        int cells = (int)Math.Max(1, Num("cells", 1));
                        double soc = Clamp(Num("soc", 1), 0, 1);
                        var battery = new Battery(_part.RefDes, 0, 1, chemistry, cells, Num("capacity", 2.5));
                        double rint = Num("rint", 0);
                        if (rint > 0) battery.InternalResistance = rint;
                        double depletion = 1.0 - soc;
                        double rs = battery.InternalResistance * (1.0 + 3.0 * Math.Pow(depletion, 4)) + (soc <= 0 ? 1e6 : 0.0);
                        double ocv = soc <= 0 ? 0.0 : cells * Battery.CellVoltage(chemistry, soc);
                        SeriesSource("+", "-", "DC " + F(ocv), rs);
                        return true;
                    }

                    case SymbolKind.VoltageSource:
                        if (!Has("+", "-")) return false;
                        SeriesSource("+", "-", "DC " + F(Num("voltage", 5)), Num("rs", 0.01));
                        return true;

                    case SymbolKind.SignalSource:
                    {
                        if (!Has("+", "-")) return false;
                        double f = Num("frequency", 1000), amp = Num("amplitude", 5), offset = Num("offset", 0);
                        if (!(f > 0)) return false;
                        double period = 1.0 / f;
                        string spec;
                        switch (Text("shape"))
                        {
                            case "sine":
                                spec = "SIN(" + F(offset) + " " + F(amp) + " " + F(f) + ")";
                                break;
                            case "triangle":
                                spec = "PULSE(" + F(offset) + " " + F(offset + amp) + " 0 " + F(0.5 * period) + " " + F(0.5 * period) + " 0 " + F(period) + ")";
                                break;
                            default:
                            {
                                const double Edge = 1e-8;   // PulseWaveform.FromDuty
                                double width = Clamp(Clamp(Num("duty", 0.5), 0, 1) * period - Edge, 0, Math.Max(0, period - 2 * Edge));
                                spec = "PULSE(" + F(offset) + " " + F(offset + amp) + " 0 " + F(Edge) + " " + F(Edge) + " " + F(width) + " " + F(period) + ")";
                                break;
                            }
                        }

                        _frequencies.Add(f);
                        SeriesSource("+", "-", spec, Num("rs", 50));
                        return true;
                    }

                    case SymbolKind.CurrentSource:
                        if (!Has("+", "-")) return false;
                        // Ours pushes current out of "+"; SPICE's flows from N+ through the source to N-.
                        Line(Element('I'), Pin("-"), Pin("+"), "DC " + F(Num("current", 0.01)));
                        return true;

                    case SymbolKind.Ammeter:
                        if (!Has("+", "-")) return false;
                        Line(Element('V'), Pin("+"), Pin("-"), "DC 0");
                        return true;

                    case SymbolKind.Voltmeter:
                        if (!Has("+", "-")) return false;
                        Line(Element('R'), Pin("+"), Pin("-"), F(10e6));
                        return true;

                    case SymbolKind.PushButton:
                    case SymbolKind.Switch:
                        if (!Has("A", "B")) return false;
                        Line(Element('R'), Pin("A"), Pin("B"), F(Flag("closed") ? 0.05 : 1e10));   // Switch On/OffResistance
                        return true;

                    case SymbolKind.Buzzer:
                        if (!Has("+", "-")) return false;
                        Line(Element('R'), Pin("+"), Pin("-"), F(Num("voltage", 5) / Num("current", 0.03)));
                        return true;

                    default:
                        return false;
                }
            }

            /// <summary>V source behind its series resistance: + → R → V → -.</summary>
            private void SeriesSource(string plus, string minus, string spec, double rs)
            {
                string top = Pin(plus);
                if (rs > 0)
                {
                    string inner = Internal("rs");
                    Line(Element('R', "RS"), top, inner, F(rs));
                    top = inner;
                }

                Line(Element('V'), top, Pin(minus), spec);
            }

            // ------------------------------------------------------------------ helpers

            private bool Has(params string[] pins) => pins.All(p => _def.FindPin(p) != null);

            private double Num(string key, double fallback) =>
                _parameters.TryGetValue(key, out var value) ? SiValue.Parse(value, fallback) : fallback;

            private string Text(string key) => _parameters.TryGetValue(key, out var value) ? value : string.Empty;

            private bool Flag(string key)
            {
                string v = Text(key).Trim().ToLowerInvariant();
                return v == "1" || v == "true" || v == "yes" || v == "on" || v == "closed";
            }

            private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

            private string ModelBase()
            {
                string model = Text("model");
                return Ident(model.Length > 0 ? model : _part.RefDes);
            }

            private static string Key(string refDes, string pin) => refDes + "\u0001" + pin;

            /// <summary>Node of a pin: its net, or a private node for an unconnected pin.</summary>
            private string Pin(string pin)
            {
                var def = _def.FindPin(pin)!;
                if (_netOfPin.TryGetValue(Key(_part.RefDes, def.Name), out var net) && _nodeOfNet.TryGetValue(net, out var node)) return node;
                string key = "NC:" + _part.RefDes + "." + def.Name;
                if (!_nodeOfNet.TryGetValue(key, out node))
                {
                    node = UniqueNode("NC_" + Ident(_part.RefDes) + "_" + Ident(def.Name));
                    _nodeOfNet[key] = node;
                }

                return node;
            }

            private string Internal(string suffix) => UniqueNode("N_" + Ident(_part.RefDes) + "_" + suffix);

            private string UniqueNode(string name)
            {
                string unique = name;
                for (int k = 2; !_nodes.Add(unique); k++) unique = name + "_" + k.ToString(CultureInfo.InvariantCulture);
                return unique;
            }

            /// <summary>SPICE node name: letters, digits and '_'; never all digits (that would be a numbered node).</summary>
            private static string NodeName(string net)
            {
                string id = Ident(net);
                return id.All(char.IsDigit) ? "N" + id : id;
            }

            /// <summary>Element name: the type letter followed by the designator ("BT1" → "VBT1").</summary>
            private string Element(char letter, string suffix = "")
            {
                string name = Ident(_part.RefDes);
                if (char.ToUpperInvariant(name[0]) != letter) name = letter + name;
                if (suffix.Length > 0) name += "_" + suffix;
                string unique = name;
                for (int k = 2; !_elements.Add(unique); k++) unique = name + "_" + k.ToString(CultureInfo.InvariantCulture);
                return unique;
            }

            /// <summary>Registers a .model card, reusing an identical one.</summary>
            private string Model(string kind, string name, string body)
            {
                string signature = kind + "(" + body + ")";
                if (_modelBySignature.TryGetValue(signature, out var existing)) return existing;
                string unique = name;
                for (int k = 2; !_modelNames.Add(unique); k++) unique = name + "_" + k.ToString(CultureInfo.InvariantCulture);
                _modelBySignature[signature] = unique;
                _models.Add(".model " + unique + " " + signature);
                return unique;
            }

            private void Line(params string[] fields) => _body.Append(string.Join(" ", fields)).Append('\n');

            private static string Ident(string text)
            {
                var sb = new StringBuilder(text.Length);
                foreach (char c in text) sb.Append(c < 128 && char.IsLetterOrDigit(c) ? c : '_');
                return sb.Length == 0 ? "X" : sb.ToString();
            }

            private static string OneLine(string? text) => (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');

            /// <summary>Plain number (no SPICE suffixes, so "M" can never be misread as milli).</summary>
            private static string F(double v)
            {
                if (double.IsNaN(v) || double.IsInfinity(v)) v = 0.0;
                if (v == 0.0) return "0";
                return v.ToString("G10", CultureInfo.InvariantCulture);
            }
        }
    }
}
