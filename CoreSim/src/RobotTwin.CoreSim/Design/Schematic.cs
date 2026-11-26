using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobotTwin.CoreSim.Design
{
    public struct GridPoint : IEquatable<GridPoint>
    {
        public GridPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Points closer than this (grid units) are the same electrical point.</summary>
        public const double Tolerance = 1e-3;

        public bool Equals(GridPoint other) => Math.Abs(X - other.X) < Tolerance && Math.Abs(Y - other.Y) < Tolerance;

        public override bool Equals(object? obj) => obj is GridPoint p && Equals(p);

        public override int GetHashCode() =>
            unchecked((int)Math.Round(X * 100) * 397 ^ (int)Math.Round(Y * 100));

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##})", X, Y);

        public static GridPoint operator +(GridPoint a, GridPoint b) => new GridPoint(a.X + b.X, a.Y + b.Y);
        public static GridPoint operator -(GridPoint a, GridPoint b) => new GridPoint(a.X - b.X, a.Y - b.Y);
    }

    /// <summary>A part placed on the schematic sheet.</summary>
    public sealed class SchematicPart
    {
        /// <summary>
        /// [integrity] The part's identity for its whole life: it stays when the designator changes, so the PCB and the
        /// Montaj follow a renamed part instead of dropping it and adding a new one. Empty in files saved before it existed
        /// (<see cref="Schematic.EnsurePartUids"/> gives one); a copy gets its own.
        /// </summary>
        public string Uid { get; set; } = string.Empty;

        public string RefDes { get; set; } = string.Empty;
        public string PartId { get; set; } = string.Empty;
        public string? PresetId { get; set; }
        public GridPoint Position { get; set; }

        /// <summary>Rotation in degrees, multiple of 90, counter-clockwise on screen.</summary>
        public int Rotation { get; set; }
        public bool Mirror { get; set; }

        /// <summary>Parameter values that differ from the preset/definition.</summary>
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Transforms a point from symbol coordinates to sheet coordinates.</summary>
        public GridPoint Transform(double x, double y)
        {
            if (Mirror) x = -x;
            int r = ((Rotation % 360) + 360) % 360;
            double tx, ty;
            switch (r)
            {
                case 90: tx = y; ty = -x; break;
                case 180: tx = -x; ty = -y; break;
                case 270: tx = -y; ty = x; break;
                default: tx = x; ty = y; break;
            }

            return new GridPoint(Position.X + tx, Position.Y + ty);
        }
    }

    public sealed class SchematicWire
    {
        public List<GridPoint> Points { get; set; } = new List<GridPoint>();
    }

    /// <summary>A named label attached to a point; every label with the same text is one net.</summary>
    public sealed class NetLabel
    {
        public string Name { get; set; } = string.Empty;
        public GridPoint Position { get; set; }

        /// <summary>
        /// Drawn as a global label (KiCad's flag shape). A sheet is a single page, so local and
        /// global labels connect the same way: every label with the same name is one net.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Global { get; set; }
    }

    /// <summary>A "no connection" flag (KiCad's X marker) on a pin that is deliberately left open.</summary>
    public sealed class NoConnectFlag
    {
        public GridPoint Position { get; set; }
    }

    public sealed class NetConnection
    {
        public NetConnection(string refDes, string pin)
        {
            RefDes = refDes;
            Pin = pin;
        }

        public string RefDes { get; }
        public string Pin { get; }

        public override string ToString() => RefDes + "." + Pin;
    }

    public sealed class Net
    {
        public string Name { get; set; } = string.Empty;
        public List<NetConnection> Connections { get; } = new List<NetConnection>();
        public bool IsGround { get; set; }

        /// <summary>True when no label or power symbol named the net (the name is derived from a pin).</summary>
        public bool IsAutoNamed { get; set; }
    }

    public sealed class Netlist
    {
        public List<Net> Nets { get; } = new List<Net>();
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Net name of each wire (index-aligned with <see cref="Schematic.Wires"/>); null if it touches nothing named.</summary>
        public List<string?> WireNets { get; } = new List<string?>();

        /// <summary>Net name of each label (index-aligned with <see cref="Schematic.Labels"/>).</summary>
        public List<string?> LabelNets { get; } = new List<string?>();

        /// <summary>Points where three or more connections meet (drawn as junction dots).</summary>
        public List<GridPoint> Junctions { get; } = new List<GridPoint>();

        public Net? NetOf(string refDes, string pin) =>
            Nets.FirstOrDefault(n => n.Connections.Any(c =>
                string.Equals(c.RefDes, refDes, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Pin, pin, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The schematic document: parts, wires and labels. Also the project file format.</summary>
    public sealed class Schematic
    {
        public const int FormatVersion = 1;

        public int Version { get; set; } = FormatVersion;
        public string Title { get; set; } = "Yeni Devre";

        /// <summary>
        /// [assembly] Stable id of the project (empty in files saved before it existed; see <see cref="EnsureProjectId"/>).
        /// Its Montaj and the parts and cables made from it carry it, so they keep their link when the title changes.
        /// </summary>
        public string ProjectId { get; set; } = string.Empty;

        /// <summary>Gives the project an id when it has none yet; returns it.</summary>
        public string EnsureProjectId()
        {
            if (string.IsNullOrEmpty(ProjectId)) ProjectId = Guid.NewGuid().ToString("N");
            return ProjectId;
        }
        public List<SchematicPart> Parts { get; set; } = new List<SchematicPart>();
        public List<SchematicWire> Wires { get; set; } = new List<SchematicWire>();
        public List<NetLabel> Labels { get; set; } = new List<NetLabel>();

        /// <summary>No-connect flags on pins that are intentionally unconnected (silences ERC).</summary>
        public List<NoConnectFlag> NoConnects { get; set; } = new List<NoConnectFlag>();

        /// <summary>Optional PCB layout saved alongside the schematic.</summary>
        public Pcb.BoardDocument? Board { get; set; }

        /// <summary>Mechanical build (robot / drone) the circuits are mounted on.</summary>
        public Mechanics.AssemblyDocument? Assembly { get; set; }

        /// <summary>Component tolerance setting for simulation (null = nominal values).</summary>
        public ToleranceSettings? Tolerances { get; set; }

        /// <summary>
        /// [variants] Alternatives of this design (<see cref="DesignVariant"/>): parts left off, values changed, parts swapped.
        /// </summary>
        public List<DesignVariant> Variants { get; set; } = new List<DesignVariant>();

        /// <summary>[bom] Where each BOM line is bought (Design/BomSourcing.cs), by the line's identity; and the currency its prices are in.</summary>
        public Dictionary<string, BomSource> Sourcing { get; set; } = new Dictionary<string, BomSource>(StringComparer.Ordinal);

        public string BomCurrency { get; set; } = "USD";

        /// <summary>[bom] How many boards the costing is for.</summary>
        public int BomBoards { get; set; } = 1;

        /// <summary>[variants] The alternative simulated and listed now, by name; null: the design as drawn.</summary>
        public string? ActiveVariant { get; set; }

        public SchematicPart? FindPart(string refDes) =>
            Parts.FirstOrDefault(p => string.Equals(p.RefDes, refDes, StringComparison.OrdinalIgnoreCase));

        /// <summary>Next free designator for a prefix ("R" -> "R3").</summary>
        public string NextRefDes(string prefix)
        {
            int max = 0;
            foreach (var part in Parts)
            {
                if (!part.RefDes.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (int.TryParse(part.RefDes.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                {
                    max = Math.Max(max, n);
                }
            }

            return prefix + (max + 1).ToString(CultureInfo.InvariantCulture);
        }

        public SchematicPart AddPart(PartLibrary library, string presetOrPartId, GridPoint position, int rotation = 0)
        {
            var preset = library.FindPreset(presetOrPartId);
            var def = preset != null ? library.Get(preset.PartId) : library.Get(presetOrPartId);
            var part = new SchematicPart
            {
                Uid = NewPartUid(),
                RefDes = NextRefDes(def.RefPrefix),
                PartId = def.Id,
                PresetId = preset?.Id,
                Position = position,
                Rotation = rotation
            };
            Parts.Add(part);
            return part;
        }

        /// <summary>[integrity] A new part identity (<see cref="SchematicPart.Uid"/>).</summary>
        public static string NewPartUid() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// [integrity] Gives every part an identity: one from a file saved before identities existed gets a new one, and of
        /// parts sharing one (a pasted or duplicated copy carries its original's) the first keeps it, the others get their own.
        /// </summary>
        public void EnsurePartUids()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in Parts)
            {
                if (!string.IsNullOrEmpty(p.Uid) && seen.Add(p.Uid)) continue;
                p.Uid = NewPartUid();
                seen.Add(p.Uid);
            }
        }

        public SchematicWire AddWire(params GridPoint[] points)
        {
            var wire = new SchematicWire { Points = points.ToList() };
            Wires.Add(wire);
            return wire;
        }

        public IEnumerable<(SchematicPart Part, PinDefinition Pin, GridPoint Position)> PinPositions(PartLibrary library)
        {
            foreach (var part in Parts)
            {
                var def = library.Find(part.PartId);
                if (def == null) continue;
                foreach (var pin in def.Pins)
                {
                    yield return (part, pin, part.Transform(pin.X, pin.Y));
                }
            }
        }

        public Dictionary<string, string> ResolveParameters(PartLibrary library, SchematicPart part)
        {
            var def = library.Get(part.PartId);
            var preset = part.PresetId != null ? library.FindPreset(part.PresetId) : null;
            return library.ResolveParameters(def, preset, part.Parameters);
        }

        // ------------------------------------------------------------- connectivity

        /// <summary>
        /// Builds the netlist. Wire vertices, pin tips and labels that coincide are
        /// joined; a wire end lying on another wire segment forms a T-junction.
        /// </summary>
        public Netlist BuildNetlist(PartLibrary library)
        {
            var netlist = new Netlist();
            var uf = new UnionFind();
            var pointIds = new Dictionary<GridPoint, int>();

            int Id(GridPoint p)
            {
                if (!pointIds.TryGetValue(p, out int id))
                {
                    id = uf.Add();
                    pointIds[p] = id;
                }

                return id;
            }

            var segments = new List<(GridPoint A, GridPoint B)>();
            foreach (var wire in Wires)
            {
                for (int i = 0; i + 1 < wire.Points.Count; i++)
                {
                    uf.Union(Id(wire.Points[i]), Id(wire.Points[i + 1]));
                    segments.Add((wire.Points[i], wire.Points[i + 1]));
                }

                if (wire.Points.Count == 1) Id(wire.Points[0]);
            }

            var pins = PinPositions(library).ToList();
            foreach (var (_, _, pos) in pins) Id(pos);
            foreach (var label in Labels) Id(label.Position);

            // T-junctions: any point that lies on the interior of a segment joins it.
            foreach (var point in pointIds.Keys.ToList())
            {
                foreach (var (a, b) in segments)
                {
                    if (OnSegment(point, a, b)) uf.Union(pointIds[point], pointIds[a]);
                }
            }

            // Labels with the same name, ground/power symbols and internal part connections.
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var namedRoots = new List<(int Id, string Name, bool Ground)>();

            void Name(int id, string name, bool ground)
            {
                if (byName.TryGetValue(name, out int other)) uf.Union(id, other);
                else byName[name] = id;
                namedRoots.Add((id, name, ground));
            }

            foreach (var label in Labels)
            {
                if (!string.IsNullOrWhiteSpace(label.Name)) Name(pointIds[label.Position], label.Name.Trim(), IsGroundName(label.Name));
            }

            var pinIds = new Dictionary<(SchematicPart, string), int>();
            foreach (var (part, pin, pos) in pins)
            {
                pinIds[(part, pin.Name)] = pointIds[pos];
                var def = library.Get(part.PartId);
                if (def.Symbol == SymbolKind.Ground)
                {
                    Name(pointIds[pos], "GND", true);
                }
                else if (def.Symbol == SymbolKind.PowerRail)
                {
                    var parameters = ResolveParameters(library, part);
                    string net = parameters.TryGetValue("net", out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : "VCC";
                    Name(pointIds[pos], net, IsGroundName(net));
                }
            }

            foreach (var part in Parts)
            {
                var def = library.Find(part.PartId);
                if (def == null)
                {
                    netlist.Warnings.Add($"{part.RefDes}: bilinmeyen parça '{part.PartId}'.");
                    continue;
                }

                foreach (var group in def.InternalConnections)
                {
                    for (int i = 1; i < group.Length; i++)
                    {
                        if (pinIds.TryGetValue((part, group[0]), out int a) && pinIds.TryGetValue((part, group[i]), out int b))
                        {
                            uf.Union(a, b);
                        }
                    }
                }
            }

            // Collect nets.
            var nets = new Dictionary<int, Net>();
            foreach (var (part, pin, pos) in pins)
            {
                var def = library.Get(part.PartId);
                if (def.Symbol == SymbolKind.Ground || def.Symbol == SymbolKind.PowerRail) continue;
                int root = uf.Find(pointIds[pos]);
                if (!nets.TryGetValue(root, out var net))
                {
                    net = new Net();
                    nets[root] = net;
                }

                net.Connections.Add(new NetConnection(part.RefDes, pin.Name));
            }

            // Names: explicit names win (ground first), otherwise "<first part>.<pin>".
            foreach (var (id, name, ground) in namedRoots.OrderByDescending(r => r.Ground))
            {
                int root = uf.Find(id);
                if (!nets.TryGetValue(root, out var net))
                {
                    net = new Net();
                    nets[root] = net;
                }

                if (string.IsNullOrEmpty(net.Name))
                {
                    net.Name = name;
                    net.IsGround = ground;
                }
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var net in nets.Values.OrderBy(n => n.Connections.Count > 0 ? n.Connections[0].ToString() : n.Name, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(net.Name))
                {
                    net.Name = net.Connections.Count > 0 ? net.Connections[0].RefDes + "." + net.Connections[0].Pin : "?";
                    net.IsAutoNamed = true;
                }

                string unique = net.Name;
                for (int k = 2; !used.Add(unique); k++) unique = net.Name + "_" + k.ToString(CultureInfo.InvariantCulture);
                net.Name = unique;
                netlist.Nets.Add(net);

                if (net.Connections.Count == 1 && !net.IsGround)
                {
                    // Boards, connectors and ICs routinely leave pins unused; only flag small parts.
                    var only = net.Connections[0];
                    var owner = FindPart(only.RefDes);
                    var ownerDef = owner != null ? library.Find(owner.PartId) : null;
                    var pinDef = ownerDef?.FindPin(only.Pin);
                    bool flagged = owner != null && pinDef != null && NoConnects.Any(nc => nc.Position.Equals(owner.Transform(pinDef.X, pinDef.Y)));
                    if (ownerDef != null && ownerDef.Pins.Count <= 3 && !flagged)
                    {
                        netlist.Warnings.Add($"{only.RefDes}.{only.Pin} hiçbir yere bağlı değil.");
                    }
                }
            }

            // Wire / label -> net name, and junction dots.
            var nameByRoot = new Dictionary<int, string>();
            foreach (var kv in nets) nameByRoot[kv.Key] = kv.Value.Name;
            foreach (var wire in Wires)
            {
                string? name = null;
                if (wire.Points.Count > 0 && nameByRoot.TryGetValue(uf.Find(pointIds[wire.Points[0]]), out var n)) name = n;
                netlist.WireNets.Add(name);
            }

            foreach (var label in Labels)
            {
                netlist.LabelNets.Add(nameByRoot.TryGetValue(uf.Find(pointIds[label.Position]), out var n) ? n : null);
            }

            var degree = new Dictionary<GridPoint, int>();
            void Bump(GridPoint p, int by) => degree[p] = (degree.TryGetValue(p, out int d) ? d : 0) + by;
            foreach (var wire in Wires)
            {
                for (int i = 0; i < wire.Points.Count; i++)
                {
                    bool end = i == 0 || i == wire.Points.Count - 1;
                    Bump(wire.Points[i], wire.Points.Count == 1 ? 0 : (end ? 1 : 2));
                }
            }

            foreach (var point in degree.Keys.ToList())
            {
                foreach (var (a, b) in segments)
                {
                    if (OnSegment(point, a, b)) Bump(point, 2);
                }
            }

            foreach (var (_, _, pos) in pins)
            {
                if (degree.ContainsKey(pos)) Bump(pos, 1);
            }

            foreach (var kv in degree)
            {
                if (kv.Value >= 3) netlist.Junctions.Add(kv.Key);
            }

            return netlist;
        }

        private static bool IsGroundName(string name) =>
            string.Equals(name.Trim(), "GND", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name.Trim(), "0", StringComparison.Ordinal);

        private static bool OnSegment(GridPoint p, GridPoint a, GridPoint b)
        {
            if (p.Equals(a) || p.Equals(b)) return false;
            double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (len < GridPoint.Tolerance || Math.Abs(cross) / len > GridPoint.Tolerance) return false;
            double dot = (p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y);
            return dot > 0 && dot < len * len;
        }

        // ------------------------------------------------------------- persistence

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

        public static Schematic FromJson(string json)
        {
            var doc = JsonSerializer.Deserialize<Schematic>(json, JsonOptions) ?? new Schematic();
            if (doc.Version > FormatVersion)
            {
                throw new InvalidDataException($"Bu dosya daha yeni bir sürümle kaydedilmiş (v{doc.Version}).");
            }

            foreach (var part in doc.Parts)
            {
                part.Parameters = new Dictionary<string, string>(part.Parameters ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            }

            doc.NoConnects ??= new List<NoConnectFlag>();
            return doc;
        }

        public void Save(string path) => File.WriteAllText(path, ToJson());

        public static Schematic Load(string path) => FromJson(File.ReadAllText(path));
    }

    internal sealed class UnionFind
    {
        private readonly List<int> _parent = new List<int>();

        public int Add()
        {
            _parent.Add(_parent.Count);
            return _parent.Count - 1;
        }

        public int Find(int x)
        {
            while (_parent[x] != x)
            {
                _parent[x] = _parent[_parent[x]];
                x = _parent[x];
            }

            return x;
        }

        public void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) _parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }
    }
}
