using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;

namespace RobotTwin.CoreSim.Libraries
{
    // [libs] A part made by the user (or by an importer) with its own pins, symbol and
    // simulation model. Stored inside the .rwpart (CustomPart.Definition) so the part is
    // self-contained; CustomPart.Register turns it into a PartDefinition.

    public enum PinElectricalType
    {
        Passive,
        Input,
        Output,
        Bidirectional,
        TriState,
        PowerIn,
        PowerOut,
        OpenCollector,
        OpenEmitter,
        NoConnect,
        Unspecified
    }

    public enum PinSide
    {
        Left,
        Right
    }

    public sealed class UserPin
    {
        /// <summary>Symbol pin name (unique within the part).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Footprint pad number the pin is soldered to.</summary>
        public string Pad { get; set; } = string.Empty;

        public PinElectricalType Type { get; set; } = PinElectricalType.Passive;
        public PinSide Side { get; set; } = PinSide.Left;
        public string Description { get; set; } = string.Empty;

        public UserPin Clone() => (UserPin)MemberwiseClone();
    }

    public sealed class UserParameter
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;

        /// <summary>Manufacturing tolerance in percent ("5", "-20/+80"); empty = exact.</summary>
        public string Tolerance { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public UserParameter Clone() => (UserParameter)MemberwiseClone();
    }

    public enum UserSymbolStyle
    {
        /// <summary>Rectangle with the pins on the left and right.</summary>
        Box,

        /// <summary>Connector strip: one column of numbered pins.</summary>
        Connector,

        /// <summary>The drawing of the built-in model (resistor, transistor, op-amp …).</summary>
        FromModel
    }

    public enum UserModelKind
    {
        /// <summary>No electrical behaviour: the pins only carry nets (connectors, mechanical parts).</summary>
        None,

        /// <summary>One of the simulator's built-in models with a pin mapping.</summary>
        BuiltIn,

        /// <summary>A SPICE .subckt made of supported primitives.</summary>
        Subcircuit,

        /// <summary>An ideal behavioural element (source, load, regulator).</summary>
        Behavioral
    }

    public enum BehavioralKind
    {
        VoltageSource,
        CurrentSource,
        Resistor,
        CurrentSink,
        Regulator
    }

    public sealed class UserModel
    {
        public UserModelKind Kind { get; set; } = UserModelKind.None;

        /// <summary>Built-in part id for <see cref="UserModelKind.BuiltIn"/> ("opamp", "npn" …).</summary>
        public string BuiltInPartId { get; set; } = string.Empty;

        /// <summary>
        /// Model pin (built-in pin, subcircuit port or behavioural role) → user pin name.
        /// Unmapped model pins are left open (connected to a private node).
        /// </summary>
        public Dictionary<string, string> PinMap { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Self-contained SPICE text (.subckt plus the models it uses).</summary>
        public string SpiceText { get; set; } = string.Empty;

        public string SubcircuitName { get; set; } = string.Empty;

        public BehavioralKind Behavior { get; set; } = BehavioralKind.VoltageSource;

        private SpiceLibrary? _spice;

        /// <summary>The parsed <see cref="SpiceText"/> (cached).</summary>
        public SpiceLibrary Spice() => _spice ??= SpiceParser.Parse(SpiceText, SubcircuitName + ".sub");

        /// <summary>Model pins in their natural order.</summary>
        public IReadOnlyList<string> ModelPins(PartLibrary library)
        {
            switch (Kind)
            {
                case UserModelKind.BuiltIn:
                    return library.Find(BuiltInPartId)?.Pins.Select(p => p.Name).ToList() ?? new List<string>();
                case UserModelKind.Subcircuit:
                    return Spice().Subcircuits.TryGetValue(SubcircuitName, out var s) ? s.Pins : new List<string>();
                case UserModelKind.Behavioral:
                    return UserPartSpec.BehavioralRoles(Behavior);
                default:
                    return Array.Empty<string>();
            }
        }

        public UserModel Clone()
        {
            var m = (UserModel)MemberwiseClone();
            m.PinMap = new Dictionary<string, string>(PinMap, StringComparer.OrdinalIgnoreCase);
            m._spice = null;
            return m;
        }
    }

    public sealed class UserPartSpec
    {
        public int Version { get; set; } = 1;
        public string RefPrefix { get; set; } = "U";
        public List<UserPin> Pins { get; set; } = new List<UserPin>();
        public UserSymbolStyle Symbol { get; set; } = UserSymbolStyle.Box;

        /// <summary>Text in the symbol body; empty = the part name.</summary>
        public string BodyLabel { get; set; } = string.Empty;

        public UserModel Model { get; set; } = new UserModel();
        public List<UserParameter> Parameters { get; set; } = new List<UserParameter>();

        /// <summary>Where the part came from ("Parça oluşturucu", "KiCad Device:R", "SPICE lm358.lib", "Fritzing …").</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>Breadboard view (Fritzing SVG), shown as a flat picture.</summary>
        public string? BreadboardSvg { get; set; }

        /// <summary>Free-form tags for search.</summary>
        public string Keywords { get; set; } = string.Empty;

        public UserPartSpec Clone() => new UserPartSpec
        {
            Version = Version,
            RefPrefix = RefPrefix,
            Pins = Pins.Select(p => p.Clone()).ToList(),
            Symbol = Symbol,
            BodyLabel = BodyLabel,
            Model = Model.Clone(),
            Parameters = Parameters.Select(p => p.Clone()).ToList(),
            Source = Source,
            BreadboardSvg = BreadboardSvg,
            Keywords = Keywords
        };

        public static IReadOnlyList<string> BehavioralRoles(BehavioralKind kind) => kind switch
        {
            BehavioralKind.Regulator => new[] { "IN", "GND", "OUT" },
            BehavioralKind.Resistor => new[] { "A", "B" },
            _ => new[] { "+", "-" }
        };

        /// <summary>Parameters a behavioural model reads (key, label, default, unit).</summary>
        public static IReadOnlyList<(string Key, string Label, string Value, string Unit)> BehavioralParameters(BehavioralKind kind) => kind switch
        {
            BehavioralKind.VoltageSource => new[] { ("voltage", "Gerilim", "5", "V"), ("rs", "İç direnç", "0.01", "Ω") },
            BehavioralKind.CurrentSource => new[] { ("current", "Akım", "10m", "A") },
            BehavioralKind.Resistor => new[] { ("resistance", "Direnç", "1k", "Ω") },
            BehavioralKind.CurrentSink => new[] { ("current", "Çekilen akım", "100m", "A") },
            BehavioralKind.Regulator => new[] { ("voltage", "Çıkış gerilimi", "3.3", "V") },
            _ => Array.Empty<(string, string, string, string)>()
        };

        public static string BehavioralName(BehavioralKind kind) => kind switch
        {
            BehavioralKind.VoltageSource => "İdeal gerilim kaynağı (+ / −)",
            BehavioralKind.CurrentSource => "İdeal akım kaynağı (+ çıkışlı)",
            BehavioralKind.Resistor => "Direnç / yük (A–B)",
            BehavioralKind.CurrentSink => "Sabit akım yükü (+ → −)",
            BehavioralKind.Regulator => "Doğrusal regülatör (IN, GND, OUT)",
            _ => kind.ToString()
        };

        public static PinElectricalType ParseKiCadType(string? type) => (type ?? string.Empty).ToLowerInvariant() switch
        {
            "input" => PinElectricalType.Input,
            "output" => PinElectricalType.Output,
            "bidirectional" => PinElectricalType.Bidirectional,
            "tri_state" => PinElectricalType.TriState,
            "power_in" => PinElectricalType.PowerIn,
            "power_out" => PinElectricalType.PowerOut,
            "open_collector" => PinElectricalType.OpenCollector,
            "open_emitter" => PinElectricalType.OpenEmitter,
            "no_connect" => PinElectricalType.NoConnect,
            "passive" => PinElectricalType.Passive,
            _ => PinElectricalType.Unspecified
        };

        public static string TypeName(PinElectricalType t) => t switch
        {
            PinElectricalType.Passive => "Pasif",
            PinElectricalType.Input => "Giriş",
            PinElectricalType.Output => "Çıkış",
            PinElectricalType.Bidirectional => "Çift yönlü",
            PinElectricalType.TriState => "Üç durumlu",
            PinElectricalType.PowerIn => "Besleme girişi",
            PinElectricalType.PowerOut => "Besleme çıkışı",
            PinElectricalType.OpenCollector => "Açık kollektör",
            PinElectricalType.OpenEmitter => "Açık emiter",
            PinElectricalType.NoConnect => "Bağlantısız (NC)",
            _ => "Belirsiz"
        };

        /// <summary>Default side for a pin type: inputs and supplies left, outputs right.</summary>
        public static PinSide DefaultSide(PinElectricalType t) =>
            t == PinElectricalType.Output || t == PinElectricalType.PowerOut || t == PinElectricalType.OpenCollector || t == PinElectricalType.OpenEmitter || t == PinElectricalType.TriState
                ? PinSide.Right
                : PinSide.Left;

        // ================================================================== part definition

        /// <summary>Builds the catalog definition: symbol pins, parameters and the electrical model.</summary>
        public PartDefinition ToDefinition(string id, string name, string category, string description, string footprint, PartLibrary library)
        {
            var def = new PartDefinition
            {
                Id = id,
                Name = name,
                Category = category,
                Description = description,
                RefPrefix = string.IsNullOrWhiteSpace(RefPrefix) ? "U" : RefPrefix.Trim(),
                Symbol = Symbol == UserSymbolStyle.Connector ? SymbolKind.Connector : SymbolKind.Module,
                SymbolVariant = "USER",
                ValueKey = "model",
                Footprint = footprint ?? string.Empty
            };

            var baseDef = Model.Kind == UserModelKind.BuiltIn ? library.Find(Model.BuiltInPartId) : null;
            if (!(Symbol == UserSymbolStyle.FromModel && baseDef != null && TryLayoutFromModel(def, baseDef)))
            {
                LayoutPins(def, string.IsNullOrWhiteSpace(BodyLabel) ? name : BodyLabel);
            }

            // Parameters: the user's, then the built-in model's (so its builder finds every key).
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Param(string key, string label, string value, string unit)
            {
                if (string.IsNullOrWhiteSpace(key) || !keys.Add(key)) return;
                def.Parameters.Add(new ParameterDefinition(key, string.IsNullOrWhiteSpace(label) ? key : label, value ?? string.Empty, unit ?? string.Empty));
            }

            Param("model", "Model", string.IsNullOrWhiteSpace(BodyLabel) ? name : BodyLabel, string.Empty);
            foreach (var p in Parameters) Param(p.Key, p.Label, p.Value, p.Unit);
            if (Model.Kind == UserModelKind.Behavioral)
            {
                foreach (var (k, l, v, u) in BehavioralParameters(Model.Behavior)) Param(k, l, v, u);
            }

            if (baseDef != null)
            {
                foreach (var p in baseDef.Parameters.Where(p => p.Key != "pinmap" && p.Key != "footprint" && p.Key != "pinout")) Param(p.Key, p.Label, p.DefaultValue, p.Unit);
            }

            // Tolerances become "tol.<key>" parameters, read by the tolerance model for user parts.
            foreach (var p in Parameters.Where(p => !string.IsNullOrWhiteSpace(p.Tolerance)))
            {
                Param("tol." + p.Key, p.Label + " toleransı", p.Tolerance.Trim().TrimEnd('%'), "%");
                if (baseDef != null && p.Key.Length > 0 && baseDef.Parameters.Any(x => x.Key == "tolerance") && keys.Contains("tolerance"))
                {
                    // The built-in model's own tolerance key, if it has one, follows the first toleranced parameter.
                    var t = def.Parameters.FindIndex(x => x.Key == "tolerance");
                    if (t >= 0 && def.Parameters[t].DefaultValue != p.Tolerance) def.Parameters[t] = new ParameterDefinition("tolerance", def.Parameters[t].Label, p.Tolerance.Trim().TrimEnd('%'), "%");
                }
            }

            def.Build = MakeBuilder(library);
            return def;
        }

        private void LayoutPins(PartDefinition def, string label)
        {
            if (Symbol == UserSymbolStyle.Connector)
            {
                int n = Pins.Count;
                for (int i = 0; i < n; i++) def.Pins.Add(new PinDefinition(Pins[i].Name, Pins[i].Pad, -3, (i - (n - 1) / 2.0) * 2));
                double longest = Pins.Count == 0 ? 1 : Pins.Max(p => p.Name.Length);
                def.HalfWidth = Math.Max(1.5, Math.Ceiling(longest * 0.48 + 1.2) - 0.5);
                def.HalfHeight = Math.Max(1, n);
                if (def.HalfWidth > 1.5)
                {
                    // Keep the pin tips one grid unit outside the body.
                    def.Pins.Clear();
                    double x = -(Math.Ceiling(def.HalfWidth) + 1);
                    def.HalfWidth = Math.Ceiling(def.HalfWidth);
                    for (int i = 0; i < n; i++) def.Pins.Add(new PinDefinition(Pins[i].Name, Pins[i].Pad, x, (i - (n - 1) / 2.0) * 2));
                }

                return;
            }

            var left = Pins.Where(p => p.Side == PinSide.Left).ToList();
            var right = Pins.Where(p => p.Side == PinSide.Right).ToList();
            int maxL = left.Count == 0 ? 0 : left.Max(p => p.Name.Length);
            int maxR = right.Count == 0 ? 0 : right.Max(p => p.Name.Length);
            double half = Math.Max(3, Math.Ceiling((maxL + maxR) * 0.48 / 2 + 1.4));
            half = Math.Max(half, Math.Min(10, Math.Ceiling(label.Length * 0.42 / 2 + 1.3)));
            int rows = Math.Max(1, Math.Max(left.Count, right.Count));
            for (int i = 0; i < left.Count; i++) def.Pins.Add(new PinDefinition(left[i].Name, left[i].Pad, -(half + 1), (i - (left.Count - 1) / 2.0) * 2));
            for (int i = 0; i < right.Count; i++) def.Pins.Add(new PinDefinition(right[i].Name, right[i].Pad, half + 1, (i - (right.Count - 1) / 2.0) * 2));
            def.HalfWidth = half;
            def.HalfHeight = rows;
        }

        private bool TryLayoutFromModel(PartDefinition def, PartDefinition baseDef)
        {
            // Every user pin must stand for exactly one model pin.
            var byUser = new Dictionary<string, PinDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in Model.PinMap)
            {
                var mp = baseDef.FindPin(kv.Key);
                if (mp == null || string.IsNullOrWhiteSpace(kv.Value) || byUser.ContainsKey(kv.Value)) return false;
                byUser[kv.Value] = mp;
            }

            if (Pins.Any(p => !byUser.ContainsKey(p.Name)) || byUser.Count != Pins.Count) return false;
            def.Symbol = baseDef.Symbol;
            def.SymbolVariant = baseDef.SymbolVariant;
            def.HalfWidth = baseDef.HalfWidth;
            def.HalfHeight = baseDef.HalfHeight;
            def.ValueKey = baseDef.ValueKey.Length > 0 ? baseDef.ValueKey : "model";
            foreach (var p in Pins)
            {
                var mp = byUser[p.Name];
                def.Pins.Add(new PinDefinition(p.Name, p.Pad, mp.X, mp.Y));
            }

            return true;
        }

        private Action<PartBuildContext>? MakeBuilder(PartLibrary library)
        {
            var model = Model;
            var userPins = new HashSet<string>(Pins.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            int PinOrOpen(PartBuildContext ctx, string modelPin)
            {
                if (model.PinMap.TryGetValue(modelPin, out var up) && !string.IsNullOrWhiteSpace(up) && userPins.Contains(up)) return ctx.Pin(up);
                return ctx.Internal("nc-" + modelPin);
            }

            switch (model.Kind)
            {
                case UserModelKind.BuiltIn:
                {
                    var baseDef = library.Find(model.BuiltInPartId);
                    if (baseDef?.Build == null) return null;
                    var keys = baseDef.Parameters.Select(p => p.Key).ToList();
                    return ctx =>
                    {
                        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var k in keys)
                        {
                            string v = ctx.Text(k);
                            parameters[k] = v.Length > 0 ? v : baseDef.DefaultParameter(k);
                        }

                        baseDef.Build(new PartBuildContext(ctx.RefDes, ctx.Circuit, pin => PinOrOpen(ctx, pin), parameters, ctx.Devices));
                    };
                }

                case UserModelKind.Subcircuit:
                    return ctx =>
                    {
                        var spice = model.Spice();
                        if (!spice.Subcircuits.TryGetValue(model.SubcircuitName, out var sub)) return;
                        var ports = sub.Pins.Select(p => PinOrOpen(ctx, p)).ToList();
                        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var k in sub.Parameters.Keys)
                        {
                            string v = ctx.Text(k);
                            if (v.Length > 0)
                            {
                                // Catalog values use SI notation ("4.7k", "2.2M"): hand SPICE a plain number.
                                overrides[k] = SiValue.TryParse(v, out double num) ? num.ToString("R", CultureInfo.InvariantCulture) : v;
                            }
                        }

                        SpiceCircuitBuilder.Build(spice, model.SubcircuitName, ctx.Circuit, ctx.RefDes, ports, overrides, d => ctx.Add(d), new ImportLog());
                    };

                case UserModelKind.Behavioral:
                    return ctx => BuildBehavioral(ctx, model.Behavior, pin => PinOrOpen(ctx, pin));

                default:
                    return null;
            }
        }

        private static void BuildBehavioral(PartBuildContext ctx, BehavioralKind kind, Func<string, int> pin)
        {
            switch (kind)
            {
                case BehavioralKind.VoltageSource:
                    ctx.Add(new VoltageSource(ctx.RefDes, pin("+"), pin("-"), new DcWaveform(ctx.Num("voltage", 5))) { SeriesResistance = Math.Max(0, ctx.Num("rs", 0.01)) });
                    break;
                case BehavioralKind.CurrentSource:
                    ctx.Add(new CurrentSource(ctx.RefDes, pin("+"), pin("-"), new DcWaveform(ctx.Num("current", 0.01))));
                    ctx.Add(new Resistor(ctx.Sub("Rp"), pin("+"), pin("-"), 1e9));
                    break;
                case BehavioralKind.Resistor:
                    ctx.Add(new Resistor(ctx.RefDes, pin("A"), pin("B"), Math.Max(1e-6, ctx.Num("resistance", 1000))));
                    break;
                case BehavioralKind.CurrentSink:
                    // Current flows into "+" and out of "-" (a load); the parallel resistor keeps an unpowered sink defined.
                    ctx.Add(new CurrentSource(ctx.RefDes, pin("-"), pin("+"), new DcWaveform(ctx.Num("current", 0.1))));
                    ctx.Add(new Resistor(ctx.Sub("Rp"), pin("+"), pin("-"), 1e7));
                    break;
                case BehavioralKind.Regulator:
                    ctx.Add(new LinearRegulator(ctx.RefDes, pin("IN"), pin("GND"), pin("OUT"), ctx.Num("voltage", 3.3)));
                    break;
            }
        }

        // ================================================================== validation

        /// <summary>
        /// Checks pins, pin↔pad agreement with the footprint, the model mapping and the parameters,
        /// and test-builds the electrical model. Errors block saving; warnings do not.
        /// </summary>
        public ImportLog Validate(Footprint? footprint, PartLibrary library, string? partName = null)
        {
            var log = new ImportLog();
            string item = partName ?? "parça";
            if (Pins.Count == 0) log.Error("parçanın hiç bacağı yok.", item: item);
            foreach (var p in Pins.Where(p => string.IsNullOrWhiteSpace(p.Name))) log.Error($"pad {p.Pad} bacağının adı boş.", item: item);
            foreach (var g in Pins.Where(p => !string.IsNullOrWhiteSpace(p.Name)).GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                log.Error($"'{g.Key}' bacak adı {g.Count()} kez kullanılmış; bacak adları benzersiz olmalı.", item: item);
            }

            foreach (var p in Pins.Where(p => string.IsNullOrWhiteSpace(p.Pad) && p.Type != PinElectricalType.NoConnect))
            {
                log.Warn($"'{p.Name}' bacağına pad numarası verilmemiş; PCB'de lehim noktası olmaz.", item: item);
            }

            foreach (var g in Pins.Where(p => !string.IsNullOrWhiteSpace(p.Pad)).GroupBy(p => p.Pad.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                log.Warn($"pad {g.Key} birden çok bacağa atanmış ({string.Join(", ", g.Select(p => p.Name))}); bu bacaklar PCB'de aynı nete bağlanır.", item: item);
            }

            if (footprint != null)
            {
                var padNames = new HashSet<string>(footprint.Pads.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var p in Pins.Where(p => !string.IsNullOrWhiteSpace(p.Pad) && !padNames.Contains(p.Pad.Trim())))
                {
                    log.Error($"'{p.Name}' bacağı pad {p.Pad} ile eşlenmiş ama '{footprint.Id}' kılıfında bu pad yok.", item: item);
                }

                var used = new HashSet<string>(Pins.Select(p => p.Pad.Trim()), StringComparer.OrdinalIgnoreCase);
                var spare = footprint.Pads.Where(p => !string.IsNullOrEmpty(p.Name) && !used.Contains(p.Name)).Select(p => p.Name).Distinct().ToList();
                if (spare.Count > 0)
                {
                    log.Warn($"'{footprint.Id}' kılıfındaki {spare.Count} pad hiçbir bacağa bağlı değil: {string.Join(", ", spare.Take(16))}{(spare.Count > 16 ? " …" : string.Empty)} (montaj/ısı pedi değilse eşleyin).", item: item);
                }
            }
            else
            {
                log.Warn("kılıf seçilmedi; parça PCB'ye yerleştirilemez.", item: item);
            }

            // Model mapping.
            var modelPins = Model.ModelPins(library);
            var userNames = new HashSet<string>(Pins.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            switch (Model.Kind)
            {
                case UserModelKind.None:
                    log.Info("elektriksel model yok: bacaklar yalnız net taşır (konnektör / mekanik parça).", item: item);
                    break;
                case UserModelKind.BuiltIn when library.Find(Model.BuiltInPartId) == null:
                    log.Error($"yerleşik model '{Model.BuiltInPartId}' bulunamadı.", item: item);
                    break;
                case UserModelKind.Subcircuit:
                {
                    var spice = Model.Spice();
                    if (!spice.Subcircuits.ContainsKey(Model.SubcircuitName))
                    {
                        log.Error($"SPICE metninde '{Model.SubcircuitName}' alt devresi yok.", item: item);
                        break;
                    }

                    var check = SpiceCircuitBuilder.Check(spice, Model.SubcircuitName, out int devices, out int skipped);
                    log.Info($"SPICE alt devresi: {devices} eleman simüle edilecek, {skipped} eleman atlandı.", item: item);
                    log.AddRange(check.Issues.Where(i => i.Severity != IssueSeverity.Info));
                    break;
                }
            }

            if (Model.Kind != UserModelKind.None)
            {
                foreach (var mp in modelPins)
                {
                    if (!Model.PinMap.TryGetValue(mp, out var up) || string.IsNullOrWhiteSpace(up))
                    {
                        log.Warn($"model bacağı '{mp}' hiçbir parça bacağına bağlı değil; açık bırakılır.", item: item);
                    }
                    else if (!userNames.Contains(up))
                    {
                        log.Error($"model bacağı '{mp}' olmayan '{up}' bacağına eşlenmiş.", item: item);
                    }
                }

                foreach (var kv in Model.PinMap.Where(kv => !modelPins.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)))
                {
                    log.Warn($"eşlemede modelde olmayan '{kv.Key}' bacağı var; yok sayılır.", item: item);
                }

                var mapped = new HashSet<string>(Model.PinMap.Values.Where(v => !string.IsNullOrWhiteSpace(v)), StringComparer.OrdinalIgnoreCase);
                foreach (var p in Pins.Where(p => p.Type != PinElectricalType.NoConnect && !mapped.Contains(p.Name)))
                {
                    log.Warn($"'{p.Name}' bacağı simülasyon modeline bağlı değil (elektriksel olarak açık).", item: item);
                }
            }

            if (Symbol == UserSymbolStyle.FromModel)
            {
                var baseDef = Model.Kind == UserModelKind.BuiltIn ? library.Find(Model.BuiltInPartId) : null;
                var probe = new PartDefinition();
                if (baseDef == null || !TryLayoutFromModel(probe, baseDef))
                {
                    log.Warn("'modelin sembolü' yalnız her bacak bir model bacağına birebir eşlendiğinde kullanılabilir; dikdörtgen sembol kullanılacak.", item: item);
                }
            }

            foreach (var p in Parameters)
            {
                if (string.IsNullOrWhiteSpace(p.Key)) log.Error("anahtarı boş bir parametre var.", item: item);
                else if (!string.IsNullOrEmpty(p.Unit) && !string.IsNullOrWhiteSpace(p.Value) && !SiValue.TryParse(p.Value, out _))
                {
                    log.Warn($"'{p.Key}' = '{p.Value}' {p.Unit} sayı olarak okunamadı.", item: item);
                }

                if (!string.IsNullOrWhiteSpace(p.Tolerance) && !ToleranceText(p.Tolerance))
                {
                    log.Warn($"'{p.Key}' toleransı '{p.Tolerance}' anlaşılamadı (örnek: 5 veya -20/+80).", item: item);
                }
            }

            foreach (var g in Parameters.Where(p => !string.IsNullOrWhiteSpace(p.Key)).GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                log.Error($"'{g.Key}' parametresi birden çok kez tanımlanmış.", item: item);
            }

            // Test build in a scratch circuit.
            if (!log.HasErrors && Model.Kind != UserModelKind.None)
            {
                try
                {
                    var def = ToDefinition("~validate", item, "", "", footprint?.Id ?? string.Empty, library);
                    var circuit = new AnalogCircuit();
                    var devices = new List<AnalogDevice>();
                    var values = library.ResolveParameters(def, null, null);
                    def.Build?.Invoke(new PartBuildContext("X1", circuit, pin => circuit.Node("X1." + pin), values, devices));
                    if (devices.Count == 0) log.Warn("model hiçbir simülasyon elemanı üretmedi.", item: item);
                    else log.Info($"model deneme kurulumu başarılı: {devices.Count} eleman.", item: item);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is KeyNotFoundException || ex is NullReferenceException)
                {
                    log.Error("model kurulamadı: " + ex.Message, item: item);
                }
            }

            return log;
        }

        private static bool ToleranceText(string t)
        {
            foreach (var part in t.Replace("%", string.Empty).Replace("±", string.Empty).Split('/'))
            {
                if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return false;
            }

            return true;
        }

        /// <summary>Pins named after the model's pins (used when the creator starts from a built-in model).</summary>
        public static UserPartSpec FromBuiltIn(PartDefinition def, IReadOnlyDictionary<string, string>? parameters = null)
        {
            var spec = new UserPartSpec { RefPrefix = def.RefPrefix, Symbol = UserSymbolStyle.FromModel };
            spec.Model.Kind = def.Build != null ? UserModelKind.BuiltIn : UserModelKind.None;
            spec.Model.BuiltInPartId = def.Id;
            foreach (var p in def.Pins)
            {
                spec.Pins.Add(new UserPin { Name = p.Name, Pad = def.ResolvePad(p, parameters), Side = p.X < 0 ? PinSide.Left : PinSide.Right });
                spec.Model.PinMap[p.Name] = p.Name;
            }

            foreach (var p in def.Parameters.Where(p => p.Key != "pinmap" && p.Key != "footprint" && p.Key != "pinout" && p.Key != "model"))
            {
                string value = parameters != null && parameters.TryGetValue(p.Key, out var v) ? v : p.DefaultValue;
                spec.Parameters.Add(new UserParameter { Key = p.Key, Label = p.Label, Value = value, Unit = p.Unit });
            }

            return spec;
        }
    }
}
