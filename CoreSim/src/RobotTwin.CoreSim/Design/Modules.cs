using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Pcb;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>One external connection of a module (a net of the inner design brought out to a pin).</summary>
    public sealed class ModulePort
    {
        /// <summary>Pin name on the module symbol (usually the inner net name: VIN, GND, SDA …).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Net inside the module this pin connects to.</summary>
        public string Net { get; set; } = string.Empty;

        /// <summary>Pad position on the module footprint (mm from the board centre), when known from the layout.</summary>
        public double? PadX { get; set; }

        public double? PadY { get; set; }
    }

    /// <summary>
    /// A finished design (schematic, optionally with its PCB) packaged as a part that
    /// other projects can place: the inner circuit is simulated in full, its pins
    /// are the nets brought out on the board's connectors, its footprint is the board
    /// outline with those connector pads, and the 3D view shows the board itself.
    /// </summary>
    public sealed class ModuleSpec
    {
        /// <summary>Inner design as schematic JSON (the PCB, if any, is inside it).</summary>
        public string SchematicJson { get; set; } = string.Empty;

        public List<ModulePort> Ports { get; set; } = new List<ModulePort>();

        /// <summary>Board size in mm (0 when the module has no layout).</summary>
        public double BoardWidth { get; set; }

        public double BoardHeight { get; set; }

        private Schematic? _inner;

        public Schematic Inner() => _inner ??= Schematic.FromJson(SchematicJson);
    }

    public static class ModuleBuilder
    {
        private static readonly Dictionary<string, ModuleSpec> ByFootprint = new Dictionary<string, ModuleSpec>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The module whose board a footprint represents (for 3D views), if any.</summary>
        public static ModuleSpec? SpecForFootprint(string footprintId)
        {
            lock (ByFootprint) return ByFootprint.TryGetValue(footprintId, out var s) ? s : null;
        }

        /// <summary>Footprint id used for a module's board.</summary>
        public static string FootprintId(string moduleId) => "MOD-" + moduleId.ToUpperInvariant();

        /// <summary>
        /// Packages a design. Ports default to every net that reaches a connector
        /// (pin header, terminal block) — the way the real board is wired to the
        /// outside — plus ground; without connectors, every named net becomes a port.
        /// </summary>
        public static ModuleSpec Create(Schematic design, PartLibrary library, IEnumerable<string>? portNets = null)
        {
            var netlist = design.BuildNetlist(library);
            var ports = new List<ModulePort>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var board = design.Board;

            void AddPort(string net, string? viaRefDes = null, string? viaPin = null)
            {
                if (!seen.Add(net)) return;
                var port = new ModulePort { Name = PortName(net, ports), Net = net };
                if (board != null && viaRefDes != null && viaPin != null)
                {
                    var comp = board.Find(viaRefDes);
                    var part = design.FindPart(viaRefDes);
                    var def = part != null ? library.Find(part.PartId) : null;
                    var fp = comp != null ? FootprintLibrary.Get(comp.FootprintId) : null;
                    var pinDef = def?.FindPin(viaPin);
                    if (comp != null && fp != null && pinDef != null)
                    {
                        string pad = def!.ResolvePad(pinDef, design.ResolveParameters(library, part!));
                        var p = fp.FindPad(pad);
                        if (p != null)
                        {
                            var (x, y) = comp.ToBoard(p.X, p.Y);
                            port.PadX = x - board.Width / 2;
                            port.PadY = y - board.Height / 2;
                        }
                    }
                }

                ports.Add(port);
            }

            if (portNets != null)
            {
                foreach (var n in portNets)
                {
                    var net = netlist.Nets.FirstOrDefault(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
                    if (net == null) continue;
                    var conn = ConnectorPin(design, library, net);
                    AddPort(net.Name, conn?.RefDes, conn?.Pin);
                }
            }
            else
            {
                foreach (var net in netlist.Nets)
                {
                    var conn = ConnectorPin(design, library, net);
                    if (conn != null) AddPort(net.Name, conn.RefDes, conn.Pin);
                }

                if (ports.Count == 0)
                {
                    foreach (var net in netlist.Nets.Where(n => !n.IsAutoNamed)) AddPort(net.Name);
                }
            }

            // Modules always share ground with whatever they are plugged into.
            foreach (var net in netlist.Nets.Where(n => n.IsGround)) AddPort(net.Name, ConnectorPin(design, library, net)?.RefDes, ConnectorPin(design, library, net)?.Pin);

            return new ModuleSpec
            {
                SchematicJson = design.ToJson(),
                Ports = ports,
                BoardWidth = board?.Width ?? 0,
                BoardHeight = board?.Height ?? 0
            };
        }

        private static NetConnection? ConnectorPin(Schematic design, PartLibrary library, Net net) =>
            net.Connections.FirstOrDefault(c =>
            {
                var p = design.FindPart(c.RefDes);
                return p != null && library.Find(p.PartId)?.Symbol == SymbolKind.Connector;
            });

        private static string PortName(string net, List<ModulePort> existing)
        {
            string name = net.Replace(' ', '_');
            if (name.Length > 10) name = name.Substring(0, 10);
            string candidate = name;
            for (int i = 2; existing.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)); i++) candidate = name + i;
            return candidate;
        }

        /// <summary>
        /// Builds the part definition: symbol pins (ports split left/right), the
        /// footprint (board outline + connector pads, or a header), and the electrical
        /// model that instantiates every inner part with its nets mapped.
        /// </summary>
        public static PartDefinition ToDefinition(string id, string name, string category, string description, ModuleSpec spec)
        {
            var def = new PartDefinition
            {
                Id = id,
                Name = name,
                Category = category,
                Description = description,
                RefPrefix = "M",
                Symbol = SymbolKind.Module,
                SymbolVariant = "MODULE",
                ValueKey = string.Empty,
                Footprint = FootprintId(id)
            };

            int n = spec.Ports.Count;
            int left = (n + 1) / 2, right = n - left;
            double rows = Math.Max(left, right);
            for (int i = 0; i < n; i++)
            {
                bool onLeft = i < left;
                int row = onLeft ? i : i - left;
                double count = onLeft ? left : right;
                double y = (row - (count - 1) / 2.0) * 2;
                def.Pins.Add(new PinDefinition(spec.Ports[i].Name, (i + 1).ToString(CultureInfo.InvariantCulture), onLeft ? -7 : 7, y));
            }

            def.HalfWidth = 6;
            def.HalfHeight = Math.Max(2, rows + 0.5);
            def.Parameters.Add(new ParameterDefinition("model", "Model", name));
            RegisterFootprint(def.Footprint, spec);
            def.Build = ctx => BuildInner(ctx, spec);
            return def;
        }

        /// <summary>Footprint: the module board outline as courtyard with a pad per port.</summary>
        public static Footprint RegisterFootprint(string footprintId, ModuleSpec spec)
        {
            var fp = new Footprint { Id = footprintId, Description = "Modül kartı" };
            int n = spec.Ports.Count;
            for (int i = 0; i < n; i++)
            {
                var port = spec.Ports[i];
                double x = port.PadX ?? (i - (n - 1) / 2.0) * 2.54;
                double y = port.PadY ?? (spec.BoardHeight > 0 ? spec.BoardHeight / 2 - 2.54 : 0);
                fp.Pads.Add(new Pad { Name = (i + 1).ToString(CultureInfo.InvariantCulture), X = x, Y = y, Width = 1.7, Height = 1.7, Drill = 1.0, Shape = i == 0 ? PadShape.Rect : PadShape.Circle });
            }

            double w = spec.BoardWidth > 0 ? spec.BoardWidth : Math.Max(5, n * 2.54 + 2), h = spec.BoardHeight > 0 ? spec.BoardHeight : 5;
            fp.CourtyardMinX = -w / 2;
            fp.CourtyardMaxX = w / 2;
            fp.CourtyardMinY = -h / 2;
            fp.CourtyardMaxY = h / 2;
            foreach (var p in fp.Pads)
            {
                fp.CourtyardMinX = Math.Min(fp.CourtyardMinX, p.X - 1.2);
                fp.CourtyardMaxX = Math.Max(fp.CourtyardMaxX, p.X + 1.2);
                fp.CourtyardMinY = Math.Min(fp.CourtyardMinY, p.Y - 1.2);
                fp.CourtyardMaxY = Math.Max(fp.CourtyardMaxY, p.Y + 1.2);
            }

            fp.Silkscreen.Add(new Segment2(fp.CourtyardMinX, fp.CourtyardMinY, fp.CourtyardMaxX, fp.CourtyardMinY));
            fp.Silkscreen.Add(new Segment2(fp.CourtyardMaxX, fp.CourtyardMinY, fp.CourtyardMaxX, fp.CourtyardMaxY));
            fp.Silkscreen.Add(new Segment2(fp.CourtyardMaxX, fp.CourtyardMaxY, fp.CourtyardMinX, fp.CourtyardMaxY));
            fp.Silkscreen.Add(new Segment2(fp.CourtyardMinX, fp.CourtyardMaxY, fp.CourtyardMinX, fp.CourtyardMinY));
            fp.Height = 12;
            FootprintLibrary.Register(fp);
            lock (ByFootprint) ByFootprint[footprintId] = spec;
            return fp;
        }

        /// <summary>Instantiates the inner design inside the outer circuit ("M1/R3", net "M1/VOUT" …).</summary>
        private static void BuildInner(PartBuildContext ctx, ModuleSpec spec)
        {
            var inner = spec.Inner();
            var library = PartLibrary.Default;
            var netlist = inner.BuildNetlist(library);
            var portByNet = spec.Ports.ToDictionary(p => p.Net, p => p.Name, StringComparer.OrdinalIgnoreCase);
            string prefix = ctx.RefDes + "/";

            foreach (var part in inner.Parts)
            {
                var def = library.Find(part.PartId);
                if (def?.Build == null) continue;
                var parameters = inner.ResolveParameters(library, part);
                string innerRef = part.RefDes;
                int PinNode(string pin)
                {
                    var net = netlist.NetOf(innerRef, pin);
                    if (net == null) return ctx.Circuit.Node(prefix + innerRef + "." + pin + "#open");
                    if (portByNet.TryGetValue(net.Name, out var port)) return ctx.Pin(port);
                    return ctx.Circuit.Node(prefix + net.Name);
                }

                var devices = new List<Analog.AnalogDevice>();
                def.Build(new PartBuildContext(prefix + innerRef, ctx.Circuit, PinNode, parameters, devices));
                ctx.Devices.AddRange(devices);
            }
        }
    }
}
