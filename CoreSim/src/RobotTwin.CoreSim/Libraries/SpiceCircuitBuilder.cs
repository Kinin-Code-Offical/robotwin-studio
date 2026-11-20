using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Libraries
{
    // [libs] Turns SPICE model cards into the simulator's device parameters and flattens
    // .subckt definitions into analog devices (R C L V I D Q M E G F H and nested X).

    /// <summary>SPICE .model parameters → CoreSim devices and built-in part parameters.</summary>
    public static class SpiceModelMap
    {
        public static readonly string[] SupportedTypes = { "D", "NPN", "PNP", "LPNP", "NMOS", "PMOS", "VDMOS" };

        public static bool IsSupported(SpiceModelCard card) => SupportedTypes.Contains(card.Type, StringComparer.OrdinalIgnoreCase);

        public static void ApplyDiode(Diode d, SpiceModelCard m, double area = 1.0)
        {
            area = area > 0 ? area : 1.0;
            d.SaturationCurrent = m.Get("IS", 1e-14) * area;
            d.Ideality = m.Get("N", 1.0);
            d.SeriesResistance = m.Get("RS", 0) / area;
            d.BreakdownVoltage = m.Get("BV", 0);
            d.BreakdownCurrent = m.Get("IBV", 1e-3) * area;
            d.BreakdownIdeality = m.Get("NBV", 1.0);
            d.BandGap = m.Get("EG", 1.11);
            d.JunctionCapacitance = m.Get("CJO", m.Get("CJ0", m.Get("CJ", 0))) * area;
        }

        public static void ApplyBjt(Bjt q, SpiceModelCard m, double area = 1.0)
        {
            area = area > 0 ? area : 1.0;
            q.SaturationCurrent = m.Get("IS", 1e-16) * area;
            q.ForwardBeta = m.Get("BF", 100);
            q.ReverseBeta = m.Get("BR", 1);
            q.EarlyVoltage = m.Get("VAF", m.Get("VA", 0));
            q.BaseEmitterCapacitance = m.Get("CJE", 0) * area;
            q.BaseCollectorCapacitance = m.Get("CJC", 0) * area;
        }

        public static void ApplyMosfet(Mosfet q, SpiceModelCard m, double? w, double? l)
        {
            bool vd = m.Type == "VDMOS";
            q.Threshold = Math.Abs(m.Get("VTO", m.Get("VT0", vd ? 0 : 0)));
            double kp = m.Get("KP", vd ? 1.0 : 2e-5);
            double ww = w ?? m.Get("W", vd ? 1.0 : 1e-4), ll = l ?? m.Get("L", vd ? 1.0 : 1e-4);
            q.Transconductance = vd ? kp : kp * (ll > 0 ? ww / ll : 1.0);
            q.ChannelModulation = m.Get("LAMBDA", vd ? 0 : 0);
            q.BodyDiode = vd;
            q.GateSourceCapacitance = m.Get("CGS", m.Get("CGSO", 0));
            q.GateDrainCapacitance = m.Get("CGDMAX", m.Get("CGD", m.Get("CGDO", 0)));
        }

        /// <summary>
        /// Parameters for the built-in part that represents a .model card: "diode", "npn", "pnp",
        /// "nmos" or "pmos". Every SPICE parameter is reported as mapped or ignored.
        /// </summary>
        public static Dictionary<string, string>? ToPartParameters(SpiceModelCard m, out string basePartId, ImportLog log)
        {
            var p = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["model"] = m.Name };
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Map(string spice, string key, Func<double, double>? f = null)
            {
                if (!m.Params.TryGetValue(spice, out double v)) return;
                p[key] = Inv.G(f != null ? f(v) : v, 8);
                used.Add(spice);
                log.Info($"{spice}={Inv.G(v)} → {key}", m.File, m.Line, m.Name);
            }

            switch (m.Kind)
            {
                case "D":
                    basePartId = "diode";
                    Map("IS", "is");
                    Map("N", "n");
                    Map("RS", "rs");
                    Map("BV", "bv");
                    Map("IBV", "ibv");
                    Map("EG", "eg");
                    Map("CJO", "cj");
                    Map("CJ0", "cj");
                    Map("IAVE", "imax");
                    if (!m.Has("N")) p["n"] = "1";
                    if (!m.Has("IS")) p["is"] = "1e-14";
                    if (!m.Has("RS")) p["rs"] = "0";
                    if (!m.Has("BV")) p["bv"] = "0";
                    if (!m.Has("CJO") && !m.Has("CJ0")) p["cj"] = "0";
                    break;

                case "NPN":
                case "PNP":
                    basePartId = m.Kind == "NPN" ? "npn" : "pnp";
                    Map("IS", "is");
                    Map("BF", "bf");
                    Map("BR", "br");
                    Map("VAF", "va");
                    Map("VA", "va");
                    Map("CJE", "cje");
                    Map("CJC", "cjc");
                    Map("ICRATING", "icmax");
                    Map("VCEO", "vceo");
                    if (!m.Has("BF")) p["bf"] = "100";
                    if (!m.Has("IS")) p["is"] = "1e-16";
                    if (!m.Has("VAF") && !m.Has("VA")) p["va"] = "0";
                    break;

                case "NMOS":
                case "PMOS":
                {
                    basePartId = m.Kind == "NMOS" ? "nmos" : "pmos";
                    bool vd = m.Type == "VDMOS";
                    double vth = Math.Abs(m.Get("VTO", m.Get("VT0", vd ? 0 : 0)));
                    double kp = m.Get("KP", vd ? 1.0 : 2e-5);
                    double k = vd ? kp : kp * m.Get("W", 1e-4) / Math.Max(1e-12, m.Get("L", 1e-4));
                    // The built-in model is parametrised by Rds(on) at a gate drive; K = 1/(Rds·(Vgs−Vth)).
                    double vgs = vth + 5.0;
                    double rds = 1.0 / Math.Max(1e-12, k * (vgs - vth));
                    if (m.Has("RD") || m.Has("RS")) rds += m.Get("RD", 0) + m.Get("RS", 0);
                    p["vth"] = Inv.G(Math.Max(0.05, vth), 6);
                    p["rdson"] = Inv.G(rds, 6);
                    p["vgsrdson"] = Inv.G(vgs, 6);
                    used.Add("VTO");
                    used.Add("VT0");
                    used.Add("KP");
                    used.Add("W");
                    used.Add("L");
                    used.Add("RD");
                    used.Add("RS");
                    log.Info($"VTO={Inv.G(vth)} V, K={Inv.G(k)} A/V² → vth={p["vth"]} V, Rds(on)={p["rdson"]} Ω @ Vgs={p["vgsrdson"]} V", m.File, m.Line, m.Name);
                    Map("CGS", "cgs");
                    Map("CGSO", "cgs");
                    Map("CGDMAX", "cgd");
                    Map("CGD", "cgd");
                    Map("VDS", "vds");
                    if (vth <= 0.05) log.Warn("eşik gerilimi (VTO) 0 ya da yok; 0.05 V kullanıldı.", m.File, m.Line, m.Name);
                    break;
                }

                default:
                    basePartId = string.Empty;
                    log.Error($"'{m.Type}' türü modeller için yerleşik simülasyon modeli yok (desteklenenler: D, NPN, PNP, NMOS, PMOS, VDMOS).", m.File, m.Line, m.Name);
                    return null;
            }

            var ignored = m.Params.Keys.Where(k => !used.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (ignored.Count > 0)
            {
                log.Warn("modelde karşılığı olmayan, yok sayılan parametreler: " + string.Join(", ", ignored), m.File, m.Line, m.Name);
            }

            if (m.Text.Count > 0)
            {
                foreach (var kv in m.Text.Where(kv => kv.Value.StartsWith("{", StringComparison.Ordinal) || kv.Value.StartsWith("'", StringComparison.Ordinal)))
                {
                    log.Warn($"{kv.Key}={kv.Value}: ifade içeren model parametresi yerleşik modele aktarılamadı.", m.File, m.Line, m.Name);
                }
            }

            return p;
        }
    }

    /// <summary>Flattens a subcircuit into devices. Unsupported elements are reported line by line and skipped.</summary>
    public static class SpiceCircuitBuilder
    {
        private const int MaxDepth = 16;

        private sealed class Scope
        {
            public Scope(SpiceSubcircuit sub, string prefix, Scope? parent)
            {
                Sub = sub;
                Prefix = prefix;
                Parent = parent;
            }

            public SpiceSubcircuit Sub { get; }
            public string Prefix { get; }
            public Scope? Parent { get; }
            public Dictionary<string, int> Ports { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, double> Params { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, VoltageSource> Sources { get; } = new Dictionary<string, VoltageSource>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class Context
        {
            public Context(SpiceLibrary lib, AnalogCircuit circuit, Action<AnalogDevice> add, ImportLog log)
            {
                Lib = lib;
                Circuit = circuit;
                Add = add;
                Log = log;
            }

            public SpiceLibrary Lib { get; }
            public AnalogCircuit Circuit { get; }
            public Action<AnalogDevice> Add { get; }
            public ImportLog Log { get; }
            public Dictionary<string, double> Globals { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public int Devices { get; set; }
            public int Skipped { get; set; }
        }

        /// <summary>
        /// Instantiates <paramref name="subckt"/>: its pins connect to <paramref name="portNodes"/> (by
        /// position), other nodes become internal nodes named "&lt;prefix&gt;#&lt;node&gt;", "0"/"GND" is ground.
        /// Returns the number of devices created.
        /// </summary>
        public static int Build(SpiceLibrary lib, string subckt, AnalogCircuit circuit, string prefix, IReadOnlyList<int> portNodes,
            IReadOnlyDictionary<string, string>? parameters, Action<AnalogDevice> add, ImportLog log)
        {
            if (!lib.Subcircuits.TryGetValue(subckt, out var sub))
            {
                log.Error($"'{subckt}' alt devresi bulunamadı.");
                return 0;
            }

            var ctx = new Context(lib, circuit, add, log);
            EvaluateGlobals(ctx);
            var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (parameters != null)
            {
                foreach (var kv in parameters)
                {
                    if (sub.Parameters.ContainsKey(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)) overrides[kv.Key] = kv.Value;
                }
            }

            Instantiate(ctx, sub, prefix, portNodes, overrides, null, 0);
            return ctx.Devices;
        }

        /// <summary>Dry run into a scratch circuit: reports every element that cannot be simulated.</summary>
        public static ImportLog Check(SpiceLibrary lib, string subckt, out int devices, out int skipped)
        {
            var log = new ImportLog();
            var circuit = new AnalogCircuit();
            if (!lib.Subcircuits.TryGetValue(subckt, out var sub))
            {
                log.Error($"'{subckt}' alt devresi bulunamadı.");
                devices = skipped = 0;
                return log;
            }

            var ports = sub.Pins.Select(p => circuit.Node("port:" + p)).ToList();
            var ctx = new Context(lib, circuit, d => circuit.Add(d), log);
            EvaluateGlobals(ctx);
            try
            {
                Instantiate(ctx, sub, "X", ports, new Dictionary<string, string>(), null, 0);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                log.Error("alt devre kurulamadı: " + ex.Message, sub.File, sub.Line, sub.Name);
            }

            devices = ctx.Devices;
            skipped = ctx.Skipped;
            if (devices == 0) log.Error("alt devreden hiçbir simüle edilebilir eleman çıkmadı.", sub.File, sub.Line, sub.Name);
            return log;
        }

        private static void EvaluateGlobals(Context ctx)
        {
            foreach (var kv in ctx.Lib.Params)
            {
                if (SpiceExpression.TryEvaluate(kv.Value, n => ctx.Globals.TryGetValue(n, out var v) ? v : (double?)null, out double value, out string error))
                {
                    ctx.Globals[kv.Key] = value;
                }
                else
                {
                    ctx.Log.Warn($".param {kv.Key}: {error}");
                }
            }
        }

        private static double? Lookup(Context ctx, Scope s, string name)
        {
            for (var x = s; x != null; x = x.Parent)
            {
                if (x.Params.TryGetValue(name, out var v)) return v;
            }

            if (ctx.Globals.TryGetValue(name, out var g)) return g;
            switch (name.ToLowerInvariant())
            {
                case "pi": return Math.PI;
                case "e": return Math.E;
                case "temp": return 27.0;
                default: return null;
            }
        }

        private static SpiceModelCard? FindModel(Context ctx, Scope s, string name)
        {
            for (var x = s; x != null; x = x.Parent)
            {
                if (x.Sub.Models.TryGetValue(name, out var m)) return m;
            }

            return ctx.Lib.Models.TryGetValue(name, out var g) ? g : null;
        }

        private static SpiceSubcircuit? FindSub(Context ctx, Scope s, string name)
        {
            for (var x = s; x != null; x = x.Parent)
            {
                if (x.Sub.Subcircuits.TryGetValue(name, out var m)) return m;
            }

            return ctx.Lib.Subcircuits.TryGetValue(name, out var g) ? g : null;
        }

        private static void Instantiate(Context ctx, SpiceSubcircuit sub, string prefix, IReadOnlyList<int> ports,
            Dictionary<string, string> overrides, Scope? parent, int depth)
        {
            if (depth > MaxDepth)
            {
                ctx.Log.Error($"'{sub.Name}': iç içe alt devre derinliği {MaxDepth} sınırını aştı (özyinelemeli tanım?).", sub.File, sub.Line, sub.Name);
                return;
            }

            var s = new Scope(sub, prefix, parent);
            for (int i = 0; i < sub.Pins.Count; i++)
            {
                if (i < ports.Count) s.Ports[sub.Pins[i]] = ports[i];
            }

            // Parameters: defaults (evaluated in the caller's scope), instance overrides, then local .param lines.
            foreach (var kv in sub.Parameters)
            {
                string expr = overrides.TryGetValue(kv.Key, out var o) ? o : kv.Value;
                var lookupScope = overrides.ContainsKey(kv.Key) && parent != null ? parent : s;
                if (SpiceExpression.TryEvaluate(expr, n => Lookup(ctx, lookupScope, n), out double v, out string err)) s.Params[kv.Key] = v;
                else ctx.Log.Warn($"parametre {kv.Key}: {err}", sub.File, sub.Line, sub.Name);
            }

            foreach (var kv in sub.LocalParams)
            {
                if (SpiceExpression.TryEvaluate(kv.Value, n => Lookup(ctx, s, n), out double v, out string err)) s.Params[kv.Key] = v;
                else ctx.Log.Warn($".param {kv.Key}: {err}", sub.File, sub.Line, sub.Name);
            }

            // Voltage sources first (F/H elements refer to them), then everything else.
            foreach (var e in sub.Elements.Where(e => e.Kind == 'V')) Element(ctx, s, e, depth);
            foreach (var e in sub.Elements.Where(e => e.Kind != 'V' && e.Kind != 'F' && e.Kind != 'H')) Element(ctx, s, e, depth);
            foreach (var e in sub.Elements.Where(e => e.Kind == 'F' || e.Kind == 'H')) Element(ctx, s, e, depth);
        }

        private static int Node(Context ctx, Scope s, string name)
        {
            if (name == "0" || name.Equals("GND", StringComparison.OrdinalIgnoreCase)) return AnalogCircuit.Ground;
            if (s.Ports.TryGetValue(name, out int port)) return port;
            return ctx.Circuit.Node(s.Prefix + "#" + name);
        }

        private static void Skip(Context ctx, SpiceElement e, string why)
        {
            ctx.Skipped++;
            ctx.Log.Error($"{e.Name}: {why} — eleman atlandı.", e.File, e.Line, e.Name);
        }

        private static bool Value(Context ctx, Scope s, SpiceElement e, string token, out double value)
        {
            string t = token;
            if (SpiceSyntax.IsAssignment(t, out var k, out var v) && (k.Length == 1 || k.Equals("value", StringComparison.OrdinalIgnoreCase))) t = v;
            if (SpiceExpression.TryEvaluate(t, n => Lookup(ctx, s, n), out value, out string err)) return true;
            Skip(ctx, e, err);
            return false;
        }

        private static Dictionary<string, string> Assignments(IEnumerable<string> tokens)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tokens)
            {
                if (SpiceSyntax.IsAssignment(t, out var k, out var v)) d[k] = v;
            }

            return d;
        }

        private static void Element(Context ctx, Scope s, SpiceElement e, int depth)
        {
            var t = e.Tokens;
            string id = s.Prefix + "." + e.Name;
            try
            {
                switch (e.Kind)
                {
                    case 'R':
                    case 'C':
                    case 'L':
                    {
                        if (t.Count < 3)
                        {
                            Skip(ctx, e, "iki düğüm ve bir değer bekleniyordu");
                            return;
                        }

                        if (!Value(ctx, s, e, t[2], out double v)) return;
                        int a = Node(ctx, s, t[0]), b = Node(ctx, s, t[1]);
                        var extra = Assignments(t.Skip(3));
                        if (e.Kind == 'R')
                        {
                            if (Math.Abs(v) < 1e-9)
                            {
                                v = 1e-6;
                                ctx.Log.Warn($"{e.Name}: 0 Ω direnç 1 µΩ olarak alındı.", e.File, e.Line, e.Name);
                            }

                            ctx.Add(new Resistor(id, a, b, v));
                        }
                        else if (e.Kind == 'C')
                        {
                            var c = new Capacitor(id, a, b, v);
                            if (extra.TryGetValue("IC", out var ic) && SpiceExpression.TryEvaluate(ic, n => Lookup(ctx, s, n), out double icv, out _)) c.InitialVoltage = icv;
                            if (extra.TryGetValue("Rser", out var rser) && SpiceExpression.TryEvaluate(rser, n => Lookup(ctx, s, n), out double rs, out _)) c.SeriesResistance = rs;
                            ctx.Add(c);
                        }
                        else
                        {
                            var l = new Inductor(id, a, b, v);
                            if (extra.TryGetValue("IC", out var ic) && SpiceExpression.TryEvaluate(ic, n => Lookup(ctx, s, n), out double icv, out _)) l.InitialCurrent = icv;
                            if (extra.TryGetValue("Rser", out var rser) && SpiceExpression.TryEvaluate(rser, n => Lookup(ctx, s, n), out double rs, out _) && rs > 0) l.SeriesResistance = rs;
                            ctx.Add(l);
                        }

                        ctx.Devices++;
                        return;
                    }

                    case 'V':
                    case 'I':
                    {
                        if (t.Count < 2)
                        {
                            Skip(ctx, e, "iki düğüm bekleniyordu");
                            return;
                        }

                        var wave = Waveform(ctx, s, e, t.Skip(2).ToList());
                        if (wave == null) return;
                        int p = Node(ctx, s, t[0]), m = Node(ctx, s, t[1]);
                        if (e.Kind == 'V')
                        {
                            var src = new VoltageSource(id, p, m, wave);
                            s.Sources[e.Name] = src;
                            ctx.Add(src);
                        }
                        else
                        {
                            // SPICE: current flows from n+ through the source to n-; CoreSim pushes it out of "plus".
                            ctx.Add(new CurrentSource(id, m, p, wave));
                        }

                        ctx.Devices++;
                        return;
                    }

                    case 'D':
                    {
                        if (t.Count < 3)
                        {
                            Skip(ctx, e, "anot, katot ve model adı bekleniyordu");
                            return;
                        }

                        var model = FindModel(ctx, s, t[2]);
                        if (model == null)
                        {
                            Skip(ctx, e, $"'{t[2]}' modeli bulunamadı");
                            return;
                        }

                        if (model.Kind != "D")
                        {
                            Skip(ctx, e, $"'{model.Name}' bir diyot modeli değil ({model.Type})");
                            return;
                        }

                        double area = t.Count > 3 && SpiceSyntax.TryParseNumber(t[3], out double ar) ? ar : 1.0;
                        var d = new Diode(id, Node(ctx, s, t[0]), Node(ctx, s, t[1]));
                        SpiceModelMap.ApplyDiode(d, model, area);
                        ctx.Add(d);
                        ctx.Devices++;
                        return;
                    }

                    case 'Q':
                    {
                        int modelAt = -1;
                        for (int i = 3; i <= 4 && i < t.Count; i++)
                        {
                            if (FindModel(ctx, s, t[i]) != null)
                            {
                                modelAt = i;
                                break;
                            }
                        }

                        if (modelAt < 0)
                        {
                            Skip(ctx, e, t.Count > 3 ? $"'{t[3]}' / '{(t.Count > 4 ? t[4] : "?")}' adında transistör modeli bulunamadı" : "C B E düğümleri ve model adı bekleniyordu");
                            return;
                        }

                        var model = FindModel(ctx, s, t[modelAt])!;
                        if (model.Kind != "NPN" && model.Kind != "PNP")
                        {
                            Skip(ctx, e, $"'{model.Name}' bir BJT modeli değil ({model.Type})");
                            return;
                        }

                        if (modelAt == 4) ctx.Log.Info($"{e.Name}: alt-tabaka düğümü '{t[3]}' yok sayıldı.", e.File, e.Line, e.Name);
                        double area = t.Count > modelAt + 1 && SpiceSyntax.TryParseNumber(t[modelAt + 1], out double ar) ? ar : 1.0;
                        var q = new Bjt(id, Node(ctx, s, t[0]), Node(ctx, s, t[1]), Node(ctx, s, t[2]), model.Kind == "NPN" ? Polarity.N : Polarity.P);
                        SpiceModelMap.ApplyBjt(q, model, area);
                        ctx.Add(q);
                        ctx.Devices++;
                        return;
                    }

                    case 'M':
                    {
                        int modelAt = -1;
                        for (int i = 3; i <= 4 && i < t.Count; i++)
                        {
                            if (FindModel(ctx, s, t[i]) != null)
                            {
                                modelAt = i;
                                break;
                            }
                        }

                        if (modelAt < 0)
                        {
                            Skip(ctx, e, "MOSFET modeli bulunamadı (D G S B MODEL bekleniyordu)");
                            return;
                        }

                        var model = FindModel(ctx, s, t[modelAt])!;
                        if (model.Kind != "NMOS" && model.Kind != "PMOS")
                        {
                            Skip(ctx, e, $"'{model.Name}' bir MOSFET modeli değil ({model.Type})");
                            return;
                        }

                        var extra = Assignments(t.Skip(modelAt + 1));
                        double? w = null, l = null;
                        if (extra.TryGetValue("W", out var ws) && SpiceExpression.TryEvaluate(ws, n => Lookup(ctx, s, n), out double wv, out _)) w = wv;
                        if (extra.TryGetValue("L", out var ls) && SpiceExpression.TryEvaluate(ls, n => Lookup(ctx, s, n), out double lv, out _)) l = lv;
                        if (modelAt == 4)
                        {
                            string bulk = t[3], source = t[2];
                            if (!bulk.Equals(source, StringComparison.OrdinalIgnoreCase))
                            {
                                ctx.Log.Warn($"{e.Name}: gövde (bulk) '{bulk}' kaynağa bağlı değil; gövde etkisi modellenmiyor.", e.File, e.Line, e.Name);
                            }
                        }

                        var q = new Mosfet(id, Node(ctx, s, t[0]), Node(ctx, s, t[1]), Node(ctx, s, t[2]), model.Kind == "NMOS" ? Polarity.N : Polarity.P);
                        SpiceModelMap.ApplyMosfet(q, model, w, l);
                        ctx.Add(q);
                        ctx.Devices++;
                        return;
                    }

                    case 'E':
                    case 'G':
                    {
                        if (t.Count >= 3 && IsNonlinearSpec(t))
                        {
                            Skip(ctx, e, "POLY / VALUE / TABLE / LAPLACE biçimli bağımlı kaynak desteklenmiyor (yalnız doğrusal: n+ n- nc+ nc- kazanç)");
                            return;
                        }

                        if (t.Count < 5)
                        {
                            Skip(ctx, e, "n+ n- nc+ nc- kazanç bekleniyordu");
                            return;
                        }

                        if (!Value(ctx, s, e, t[4], out double g)) return;
                        int p = Node(ctx, s, t[0]), m = Node(ctx, s, t[1]), cp = Node(ctx, s, t[2]), cm = Node(ctx, s, t[3]);
                        if (e.Kind == 'E') ctx.Add(new SpiceVcvs(id, p, m, cp, cm, g));
                        else ctx.Add(new SpiceVccs(id, p, m, cp, cm, g));
                        ctx.Devices++;
                        return;
                    }

                    case 'F':
                    case 'H':
                    {
                        if (t.Count >= 3 && IsNonlinearSpec(t))
                        {
                            Skip(ctx, e, "POLY biçimli akım denetimli kaynak desteklenmiyor");
                            return;
                        }

                        if (t.Count < 4)
                        {
                            Skip(ctx, e, "n+ n- Vkontrol kazanç bekleniyordu");
                            return;
                        }

                        if (!s.Sources.TryGetValue(t[2], out var control))
                        {
                            Skip(ctx, e, $"denetleyen gerilim kaynağı '{t[2]}' bu alt devrede yok");
                            return;
                        }

                        if (!Value(ctx, s, e, t[3], out double g)) return;
                        int p = Node(ctx, s, t[0]), m = Node(ctx, s, t[1]);
                        if (e.Kind == 'F') ctx.Add(new SpiceCccs(id, p, m, control, g));
                        else ctx.Add(new SpiceCcvs(id, p, m, control, g));
                        ctx.Devices++;
                        return;
                    }

                    case 'X':
                    {
                        string? name = SpiceSyntax.SubcircuitNameOf(e);
                        if (name == null)
                        {
                            Skip(ctx, e, "alt devre adı yok");
                            return;
                        }

                        var child = FindSub(ctx, s, name);
                        if (child == null)
                        {
                            Skip(ctx, e, $"'{name}' alt devresi tanımlı değil");
                            return;
                        }

                        int nameAt = t.IndexOf(name);
                        var nodes = t.Take(nameAt).Select(n => Node(ctx, s, n)).ToList();
                        if (nodes.Count != child.Pins.Count)
                        {
                            ctx.Log.Warn($"{e.Name}: '{name}' {child.Pins.Count} bacaklı ama {nodes.Count} düğüm verilmiş; fazlası yok sayıldı, eksikler açık bırakıldı.", e.File, e.Line, e.Name);
                        }

                        var inst = Assignments(t.Skip(nameAt + 1));
                        Instantiate(ctx, child, s.Prefix + "." + e.Name, nodes, inst, s, depth + 1);
                        return;
                    }

                    case 'K':
                        Skip(ctx, e, "manyetik kuplaj (K, transformatör) desteklenmiyor");
                        return;
                    case 'T':
                    case 'O':
                    case 'U':
                    case 'Y':
                    case 'P':
                        Skip(ctx, e, "iletim hattı elemanları desteklenmiyor");
                        return;
                    case 'S':
                    case 'W':
                        Skip(ctx, e, "gerilim/akım denetimli anahtar (S/W) desteklenmiyor");
                        return;
                    case 'B':
                        Skip(ctx, e, "davranışsal kaynak (B, keyfi ifade) desteklenmiyor");
                        return;
                    case 'J':
                        Skip(ctx, e, "JFET modeli yok");
                        return;
                    case 'Z':
                        Skip(ctx, e, "MESFET modeli yok");
                        return;
                    case 'A':
                        Skip(ctx, e, "XSPICE kod modeli (A) desteklenmiyor");
                        return;
                    default:
                        Skip(ctx, e, $"bilinmeyen eleman türü '{e.Kind}'");
                        return;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
            {
                Skip(ctx, e, ex.Message);
            }
        }

        private static bool IsNonlinearSpec(List<string> t)
        {
            foreach (var tok in t.Skip(2))
            {
                string u = tok.ToUpperInvariant();
                if (u == "POLY" || u.StartsWith("VALUE", StringComparison.Ordinal) || u.StartsWith("TABLE", StringComparison.Ordinal) || u.StartsWith("LAPLACE", StringComparison.Ordinal) ||
                    u.StartsWith("FREQ", StringComparison.Ordinal) || u.StartsWith("CHEBYSHEV", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static Waveform? Waveform(Context ctx, Scope s, SpiceElement e, List<string> spec)
        {
            double dc = 0;
            Waveform? transient = null;
            for (int i = 0; i < spec.Count; i++)
            {
                string u = spec[i].ToUpperInvariant();
                List<double> Args()
                {
                    var list = new List<double>();
                    int j = i + 1;
                    while (j < spec.Count && SpiceExpression.TryEvaluate(spec[j], n => Lookup(ctx, s, n), out double v, out _) && !IsKeyword(spec[j]))
                    {
                        list.Add(v);
                        j++;
                    }

                    i = j - 1;
                    return list;
                }

                if (u == "DC")
                {
                    if (i + 1 < spec.Count && Value(ctx, s, e, spec[i + 1], out double v))
                    {
                        dc = v;
                        i++;
                    }

                    continue;
                }

                if (u == "AC")
                {
                    Args();
                    ctx.Log.Info($"{e.Name}: AC genliği yalnız AC analizinde kullanılır; yok sayıldı.", e.File, e.Line, e.Name);
                    continue;
                }

                if (u == "PULSE")
                {
                    var a = Args();
                    double v1 = A(a, 0, 0), v2 = A(a, 1, 0), td = A(a, 2, 0), tr = A(a, 3, 1e-9), tf = A(a, 4, 1e-9), pw = A(a, 5, 1e-3), per = A(a, 6, 0);
                    if (per > 0) transient = new PulseWaveform(v1, v2, per, pw, td, tr, tf);
                    else transient = new PiecewiseLinearWaveform(new[] { (0.0, v1), (td, v1), (td + tr, v2), (td + tr + pw, v2), (td + tr + pw + tf, v1) });
                    continue;
                }

                if (u == "SIN" || u == "SINE")
                {
                    var a = Args();
                    if (A(a, 4, 0) != 0) ctx.Log.Warn($"{e.Name}: SIN sönüm katsayısı (theta) desteklenmiyor; sönümsüz sinüs kullanıldı.", e.File, e.Line, e.Name);
                    transient = new SineWaveform(A(a, 0, 0), A(a, 1, 0), A(a, 2, 1e3), A(a, 3, 0), A(a, 5, 0));
                    continue;
                }

                if (u == "PWL")
                {
                    var a = Args();
                    var pts = new List<(double, double)>();
                    for (int k = 0; k + 1 < a.Count; k += 2) pts.Add((a[k], a[k + 1]));
                    if (pts.Count == 0)
                    {
                        Skip(ctx, e, "PWL noktası yok (dosyadan PWL desteklenmiyor)");
                        return null;
                    }

                    transient = new PiecewiseLinearWaveform(pts);
                    continue;
                }

                if (u == "EXP" || u == "SFFM" || u == "AM" || u == "TRNOISE" || u == "TRRANDOM")
                {
                    var a = Args();
                    ctx.Log.Warn($"{e.Name}: {u} dalga biçimi desteklenmiyor; başlangıç değeri {Inv.G(A(a, 0, dc))} sabit kullanıldı.", e.File, e.Line, e.Name);
                    dc = A(a, 0, dc);
                    continue;
                }

                if (SpiceSyntax.IsAssignment(spec[i], out var key, out _))
                {
                    ctx.Log.Info($"{e.Name}: '{key}' seçeneği yok sayıldı.", e.File, e.Line, e.Name);
                    continue;
                }

                if (SpiceExpression.TryEvaluate(spec[i], n => Lookup(ctx, s, n), out double plain, out string err))
                {
                    dc = plain;
                    continue;
                }

                Skip(ctx, e, err);
                return null;
            }

            return transient ?? new DcWaveform(dc);
        }

        private static bool IsKeyword(string token)
        {
            string u = token.ToUpperInvariant();
            return u == "DC" || u == "AC" || u == "PULSE" || u == "SIN" || u == "PWL" || u == "EXP" || u == "SFFM";
        }

        private static double A(List<double> a, int i, double fallback) => i < a.Count ? a[i] : fallback;
    }
}
