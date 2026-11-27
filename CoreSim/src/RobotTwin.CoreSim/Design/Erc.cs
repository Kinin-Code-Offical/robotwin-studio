using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Design
{
    public enum ErcSeverity
    {
        Error,
        Warning,

        /// <summary>Normal but worth knowing (e.g. unused pins of a development board).</summary>
        Info
    }

    /// <summary>Electrical role of a pin, inferred from the part kind and pin name (KiCad's pin types).</summary>
    public enum PinRole
    {
        Passive,
        Input,
        Output,
        Bidirectional,
        PowerIn,
        PowerOut
    }

    public sealed class ErcViolation
    {
        public ErcViolation(ErcSeverity severity, string code, string message, GridPoint location, string? refDes = null, string? net = null)
        {
            Severity = severity;
            Code = code;
            Message = message;
            Location = location;
            RefDes = refDes;
            Net = net;
        }

        public ErcSeverity Severity { get; }

        /// <summary>Stable rule id ("pin-unconnected", "output-conflict", ...).</summary>
        public string Code { get; }

        public string Message { get; }
        public GridPoint Location { get; }
        public string? RefDes { get; }
        public string? Net { get; }

        /// <summary>[sheets] The sheet it is on (null: the root), so the editor can go there.</summary>
        public string? Sheet { get; set; }

        public string SeverityText => Severity switch
        {
            ErcSeverity.Error => "Hata",
            ErcSeverity.Warning => "Uyarı",
            _ => "Bilgi"
        };

        public override string ToString() => $"[{SeverityText}] {Message} @ {Location}";
    }

    /// <summary>
    /// Electrical rules check of a schematic: designators, unconnected and dangling items,
    /// single-pin nets, outputs driving each other or a supply, power inputs without a source.
    /// Pin types are inferred (<see cref="RoleOf"/>) because the part library has none.
    /// </summary>
    public static class ElectricalRulesChecker
    {
        public static PinRole RoleOf(PartDefinition def, string pin)
        {
            string p = pin.ToUpperInvariant();
            bool IsGnd() => p.StartsWith("GND", StringComparison.Ordinal) || p == "COM" || p == "V-" || p == "VSS";
            switch (def.Symbol)
            {
                case SymbolKind.Battery:
                case SymbolKind.VoltageSource:
                    return p == "+" ? PinRole.PowerOut : PinRole.Passive;
                case SymbolKind.SignalSource:
                    return p == "+" ? PinRole.Output : PinRole.Passive;
                case SymbolKind.Regulator:
                    return p == "OUT" ? PinRole.PowerOut : p == "IN" ? PinRole.PowerIn : PinRole.Passive;
                case SymbolKind.OpAmp:
                    return p == "OUT" ? PinRole.Output : p.StartsWith("IN", StringComparison.Ordinal) ? PinRole.Input : p == "V+" ? PinRole.PowerIn : PinRole.Passive;
                case SymbolKind.Gate:
                    return p == "Y" || p == "OUT" ? PinRole.Output : p == "VCC" || p == "VDD" ? PinRole.PowerIn : IsGnd() ? PinRole.Passive : PinRole.Input;
                case SymbolKind.Board:
                    if (p == "5V" || p == "3V3" || p == "3.3V") return PinRole.PowerOut;
                    if (p == "VBAT") return PinRole.PowerIn;
                    if (IsGnd() || p == "VIN" || p == "AREF" || p == "EN" || p == "RST") return PinRole.Passive;
                    if (p.Length >= 2 && p[0] == 'M' && char.IsDigit(p[1])) return PinRole.Output;
                    return PinRole.Bidirectional;
                case SymbolKind.Module:
                    if (p == "VCC" || p == "VDD" || p == "VM" || p == "VIN" || p == "VBAT" || p == "5V" || p == "3V3") return PinRole.PowerIn;
                    if (IsGnd()) return PinRole.Passive;
                    if (p == "OUT" || p == "ECHO" || p == "Y" || p == "DOUT" || p == "TX" || p.StartsWith("OUT", StringComparison.Ordinal)) return PinRole.Output;
                    if (p == "TRIG" || p == "PWM" || p == "IN" || p == "DIN" || p == "RX" || p == "EN" || p.StartsWith("IN", StringComparison.Ordinal)) return PinRole.Input;
                    if (p == "SDA" || p == "SCL") return PinRole.Bidirectional;
                    return PinRole.Passive;
                default:
                    return PinRole.Passive;
            }
        }

        /// <summary>Two-terminal parts through which a supply reaches the other side (for "power input not driven").</summary>
        private static bool Conducts(PartDefinition def) => def.Pins.Count == 2 && def.Symbol is
            SymbolKind.Resistor or SymbolKind.Fuse or SymbolKind.Inductor or SymbolKind.Diode or SymbolKind.Schottky or
            SymbolKind.PushButton or SymbolKind.Switch or SymbolKind.Ammeter or SymbolKind.Thermistor or SymbolKind.Photoresistor;

        /// <summary>
        /// A board pin that carries the USB supply while the board is plugged in, as the simulation powers it: an ESP32
        /// DevKit's VIN (USB 5 V through the board's diode). A part fed from it is powered, yet the pin is an input too
        /// (a battery on VIN is the designed use), so it is no conflicting supply.
        /// </summary>
        private static bool UsbSupply(PartDefinition def, string pin) =>
            def.Symbol == SymbolKind.Board && pin.Equals("VIN", StringComparison.OrdinalIgnoreCase) &&
            def.Id.StartsWith("esp32", StringComparison.OrdinalIgnoreCase);

        public static List<ErcViolation> Check(Schematic doc, PartLibrary library)
        {
            var result = new List<ErcViolation>();
            var netlist = doc.BuildNetlist(library);

            // Designators.
            foreach (var part in doc.Parts)
            {
                var def = library.Find(part.PartId);
                if (def == null)
                {
                    result.Add(new ErcViolation(ErcSeverity.Error, "unknown-part", $"{part.RefDes}: kütüphanede olmayan parça '{part.PartId}'.", part.Position, part.RefDes));
                    continue;
                }

                if (!Annotator.IsAnnotated(part.RefDes))
                {
                    result.Add(new ErcViolation(ErcSeverity.Error, "not-annotated", $"{part.RefDes}: numaralandırılmamış parça (Numaralandır komutunu çalıştırın).", part.Position, part.RefDes));
                }
            }

            foreach (var group in Annotator.Duplicates(doc))
            {
                foreach (var part in group.Skip(1))
                {
                    result.Add(new ErcViolation(ErcSeverity.Error, "duplicate-refdes", $"{part.RefDes} tanımlayıcısı birden fazla parçada kullanılıyor.", part.Position, part.RefDes));
                }
            }

            // Pin geometry: refdes.pin -> (part, def, pin, tip).
            var pinInfo = new Dictionary<string, (SchematicPart Part, PartDefinition Def, PinDefinition Pin, GridPoint Tip)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (part, pin, pos) in doc.PinPositions(library))
            {
                pinInfo[part.RefDes + "." + pin.Name] = (part, library.Get(part.PartId), pin, pos);
            }

            var flags = doc.NoConnects.Select(n => n.Position).ToList();
            bool Flagged(GridPoint p) => flags.Any(f => f.Equals(p));
            var wireNets = new HashSet<string>(netlist.WireNets.Where(n => n != null)!, StringComparer.OrdinalIgnoreCase);
            var labelNets = new HashSet<string>(netlist.LabelNets.Where(n => n != null)!, StringComparer.OrdinalIgnoreCase);

            // Nets.
            var driven = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roles = new Dictionary<Net, List<(NetConnection C, PinRole Role, GridPoint Tip, PartDefinition Def)>>();
            foreach (var net in netlist.Nets)
            {
                var list = new List<(NetConnection, PinRole, GridPoint, PartDefinition)>();
                foreach (var c in net.Connections)
                {
                    if (!pinInfo.TryGetValue(c.RefDes + "." + c.Pin, out var info)) continue;
                    list.Add((c, RoleOf(info.Def, info.Pin.Name), info.Tip, info.Def));
                }

                roles[net] = list;
                if (net.IsGround || list.Any(x => x.Item2 == PinRole.PowerOut || UsbSupply(x.Item4, x.Item1.Pin))) driven.Add(net.Name);
            }

            // Propagate "driven" through switches, fuses, resistors and diodes.
            var netOfPin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var net in netlist.Nets) foreach (var c in net.Connections) netOfPin[c.RefDes + "." + c.Pin] = net.Name;
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var part in doc.Parts)
                {
                    var def = library.Find(part.PartId);
                    if (def == null || !Conducts(def)) continue;
                    netOfPin.TryGetValue(part.RefDes + "." + def.Pins[0].Name, out var a);
                    netOfPin.TryGetValue(part.RefDes + "." + def.Pins[1].Name, out var b);
                    if (a == null || b == null) continue;
                    if (driven.Contains(a) && driven.Add(b)) grew = true;
                    if (driven.Contains(b) && driven.Add(a)) grew = true;
                }
            }

            foreach (var net in netlist.Nets)
            {
                var list = roles[net];
                if (list.Count == 0) continue;
                var at = list[0].Tip;

                if (list.Count == 1 && !net.IsGround)
                {
                    var (c, _, tip, def) = list[0];
                    if (Flagged(tip)) continue;
                    bool hasWiring = wireNets.Contains(net.Name) || labelNets.Contains(net.Name) || !net.IsAutoNamed;
                    if (hasWiring)
                    {
                        result.Add(new ErcViolation(ErcSeverity.Warning, "single-pin-net", $"{net.Name} hattında yalnızca bir pin var ({c}): kablo/etiket başka hiçbir yere gitmiyor.", tip, c.RefDes, net.Name));
                    }
                    else
                    {
                        bool bigPart = def.Symbol is SymbolKind.Board or SymbolKind.Module or SymbolKind.Connector;
                        result.Add(new ErcViolation(bigPart ? ErcSeverity.Info : ErcSeverity.Warning, "pin-unconnected",
                            $"{c} bağlı değil" + (bigPart ? " (kullanılmayan kart/modül pini; istenirse NC işareti koyun)." : "."), tip, c.RefDes, net.Name));
                    }

                    continue;
                }

                foreach (var x in list.Where(x => Flagged(x.Tip)))
                {
                    result.Add(new ErcViolation(ErcSeverity.Warning, "nc-connected", $"{x.C} 'bağlantı yok' (NC) işaretli ama {net.Name} hattına bağlı.", x.Tip, x.C.RefDes, net.Name));
                }

                var outputs = list.Where(x => x.Role == PinRole.Output).ToList();
                var supplies = list.Where(x => x.Role == PinRole.PowerOut).ToList();
                if (outputs.Count >= 2)
                {
                    result.Add(new ErcViolation(ErcSeverity.Error, "output-conflict",
                        $"{net.Name}: çıkışlar birbirine bağlı ({string.Join(", ", outputs.Select(o => o.C.ToString()))}) — kısa devre riski.", outputs[1].Tip, outputs[1].C.RefDes, net.Name));
                }

                if (outputs.Count >= 1 && (supplies.Count >= 1 || net.IsGround))
                {
                    string what = net.IsGround ? "toprağa" : "besleme çıkışına (" + supplies[0].C + ")";
                    result.Add(new ErcViolation(ErcSeverity.Error, "output-to-power",
                        $"{outputs[0].C} çıkışı doğrudan {what} bağlı — çıkış kısa devre.", outputs[0].Tip, outputs[0].C.RefDes, net.Name));
                }

                if (supplies.Count >= 2 && !net.IsGround)
                {
                    result.Add(new ErcViolation(ErcSeverity.Warning, "supply-conflict",
                        $"{net.Name}: birden fazla besleme kaynağı aynı hatta ({string.Join(", ", supplies.Select(o => o.C.ToString()))}).", supplies[1].Tip, supplies[1].C.RefDes, net.Name));
                }

                var powerIns = list.Where(x => x.Role == PinRole.PowerIn).ToList();
                if (powerIns.Count > 0 && !driven.Contains(net.Name))
                {
                    result.Add(new ErcViolation(ErcSeverity.Error, "power-not-driven",
                        $"{net.Name}: besleme girişi ({string.Join(", ", powerIns.Select(o => o.C.ToString()))}) hiçbir kaynaktan beslenmiyor.", powerIns[0].Tip, powerIns[0].C.RefDes, net.Name));
                }

                if (list.All(x => x.Role == PinRole.Input))
                {
                    result.Add(new ErcViolation(ErcSeverity.Warning, "input-not-driven",
                        $"{net.Name}: yalnızca girişler bağlı ({string.Join(", ", list.Select(o => o.C.ToString()))}); hattı süren bir çıkış yok.", at, list[0].C.RefDes, net.Name));
                }
            }

            // Wiring, sheet by sheet ([sheets]: a point on one sheet is nowhere on another).
            var sheetIds = new List<string?> { null };
            sheetIds.AddRange(doc.Sheets.Select(x => (string?)x.Uid));
            foreach (var sheetId in sheetIds)
            {
                foreach (var (wire, end) in SchematicEditing.DanglingEnds(doc, library, sheetId))
                {
                    result.Add(new ErcViolation(ErcSeverity.Warning, "wire-dangling", "Kablo ucu boşta (hiçbir pine veya kabloya bağlı değil).", end) { Sheet = sheetId });
                }

                var tips = SchematicEditing.PinTips(doc, library, null, sheetId);
                var wires = doc.Wires.Where(w => Schematic.SameSheet(w.Sheet, sheetId)).ToList();
                foreach (var label in doc.Labels.Where(l => Schematic.SameSheet(l.Sheet, sheetId)))
                {
                    bool onSomething = tips.Contains(label.Position) || wires.Any(w =>
                        w.Points.Any(p => p.Equals(label.Position)) ||
                        Enumerable.Range(0, Math.Max(0, w.Points.Count - 1)).Any(i => SchematicEditing.OnSegmentInterior(label.Position, w.Points[i], w.Points[i + 1])));
                    if (label.IsDirective)
                    {
                        // [directive] a class flag names nothing: it only has to touch the net it gives the class to
                        if (!onSomething)
                            result.Add(new ErcViolation(ErcSeverity.Warning, "label-unconnected", $"'{{{label.NetClass}}}' net sınıfı yönergesi hiçbir kabloya veya pine değmiyor.", label.Position, null, label.NetClass) { Sheet = sheetId });
                        continue;
                    }

                    if (!onSomething)
                    {
                        result.Add(new ErcViolation(ErcSeverity.Warning, "label-unconnected", $"'{label.Name}' etiketi hiçbir kabloya veya pine değmiyor.", label.Position, null, label.Name) { Sheet = sheetId });
                    }
                    else if (!label.Hierarchical &&
                             doc.Labels.Count(l => !l.Hierarchical && string.Equals(l.Name.Trim(), label.Name.Trim(), StringComparison.OrdinalIgnoreCase)) == 1 &&
                             !doc.Parts.Any(p => library.Find(p.PartId)?.Symbol == SymbolKind.PowerRail &&
                                                 string.Equals(doc.ResolveParameters(library, p).TryGetValue("net", out var n) ? n.Trim() : "", label.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                    {
                        int li = doc.Labels.IndexOf(label);
                        string? labelNet = label.Sheet != null && li >= 0 && li < netlist.LabelNets.Count ? netlist.LabelNets[li] : null;   // a sub-sheet's net is /Sheet/name
                        var net = netlist.Nets.FirstOrDefault(x => string.Equals(x.Name, labelNet ?? label.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (net != null && net.Connections.Count <= 1 && !net.IsGround)
                        {
                            result.Add(new ErcViolation(ErcSeverity.Warning, "label-single", $"'{label.Name}' etiketi tek başına: aynı adda başka etiket yok ve hatta tek pin var.", label.Position, null, label.Name) { Sheet = sheetId });
                        }
                    }
                }

                foreach (var nc in doc.NoConnects.Where(x => Schematic.SameSheet(x.Sheet, sheetId)))
                {
                    if (!tips.Contains(nc.Position))
                    {
                        result.Add(new ErcViolation(ErcSeverity.Warning, "nc-floating", "NC işareti bir pinin üzerinde değil.", nc.Position) { Sheet = sheetId });
                    }
                }
            }

            // [sheets] The hierarchy: every hierarchical label needs its pin on the sheet symbol, every pin its label inside.
            foreach (var label in doc.Labels.Where(l => l.Hierarchical && !string.IsNullOrWhiteSpace(l.Name)))
            {
                var symbol = doc.FindSheet(label.Sheet);
                if (symbol == null || !symbol.Pins.Any(pn => string.Equals(pn.Name.Trim(), label.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(new ErcViolation(ErcSeverity.Warning, "hier-label-no-pin",
                        symbol == null ? $"'{label.Name}' hiyerarşik etiketi kök sayfada: bağlanacağı bir sayfa sembolü yok."
                                       : $"'{label.Name}' hiyerarşik etiketinin '{symbol.Name}' sayfa sembolünde pini yok.", label.Position, null, label.Name) { Sheet = label.Sheet });
                }
            }

            foreach (var symbol in doc.Sheets)
            {
                foreach (var pin in symbol.Pins)
                {
                    if (doc.Labels.Any(l => l.Hierarchical && Schematic.SameSheet(l.Sheet, symbol.Uid) && string.Equals(l.Name.Trim(), pin.Name.Trim(), StringComparison.OrdinalIgnoreCase))) continue;
                    result.Add(new ErcViolation(ErcSeverity.Warning, "sheet-pin-no-label",
                        $"'{symbol.Name}' sayfasının '{pin.Name}' pininin alt sayfada hiyerarşik etiketi yok.", pin.Position, null, pin.Name) { Sheet = symbol.Sheet });
                }
            }

            foreach (var v in result)
                if (v.Sheet == null && v.RefDes != null)
                    v.Sheet = doc.FindPart(v.RefDes)?.Sheet;   // [sheets] a part's violation is on its sheet

            return result
                .OrderBy(v => v.Severity)
                .ThenBy(v => v.RefDes ?? "~", Annotator.NaturalComparer)
                .ToList();
        }
    }
}
