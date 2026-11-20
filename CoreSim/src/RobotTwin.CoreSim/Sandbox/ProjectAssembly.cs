using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [assembly] A project's circuit as a real mechanical assembly — no vehicle templates. Parts
    /// soldered on the project's PCB stay on one board object; every other part (motors, servos,
    /// batteries, sensor and controller modules, loose components) becomes its own real 3D part with
    /// its own mass, geometry and physics, and is cabled to exactly the pins its nets go to. The parts
    /// are laid out on the bench around the board; the user then builds whatever the machine is —
    /// chassis, arms, wings, shells, joints — with the world's tools, and everything is simulated as it is.
    /// </summary>
    public static class ProjectAssembly
    {
        /// <summary>Symbols that are not physical parts.</summary>
        private static bool IsVirtual(PartDefinition def) =>
            def.Symbol is SymbolKind.Ground or SymbolKind.Ammeter or SymbolKind.Voltmeter or SymbolKind.Multimeter || def.Pins.Count == 0;

        public static WorldDocument Build(ProjectPackage package, PartLibrary library, string? sourcePath = null)
        {
            var sch = package.Schematic;
            string title = string.IsNullOrWhiteSpace(sch.Title) ? "Proje" : sch.Title;
            var w = new WorldDocument { Title = title + " — montaj" };
            var netlist = sch.BuildNetlist(library);

            // On the PCB = placed as a footprint on the project's board.
            var onBoard = new HashSet<string>(sch.Board?.Components.Select(c => c.RefDes) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var parts = sch.Parts.Where(p => library.Find(p.PartId) is { } d && !IsVirtual(d)).ToList();
            var offBoard = parts.Where(p => !onBoard.Contains(p.RefDes)).ToList();

            WorldObject? board = null;
            if (onBoard.Count > 0)
            {
                var design = w.AddDesign(package, sourcePath);
                // The board object carries only what is soldered on it; the rest becomes real parts.
                var keep = new HashSet<string>(onBoard, StringComparer.OrdinalIgnoreCase);
                design.Package.Schematic.Parts.RemoveAll(p => !keep.Contains(p.RefDes) && library.Find(p.PartId) is { } d && !IsVirtual(d));
                board = w.AddBoard(design, new WorldPose());
                board.Name = title + " (kart)";
            }

            // Lay the loose parts out around the board, controllers first, then the rest by size.
            var objects = new Dictionary<string, WorldObject>(StringComparer.OrdinalIgnoreCase);
            var ordered = offBoard.OrderBy(p => library.Find(p.PartId)!.Symbol == SymbolKind.Board ? 0 : library.Find(p.PartId)!.Symbol == SymbolKind.Battery ? 1 : 2).ThenBy(p => p.RefDes, StringComparer.OrdinalIgnoreCase).ToList();
            double ring = board != null ? 0.16 : 0.0;
            int n = ordered.Count;
            for (int i = 0; i < n; i++)
            {
                var p = ordered[i];
                var def = library.Find(p.PartId)!;
                WorldPose pose;
                if (board == null && i == 0) pose = new WorldPose();
                else
                {
                    // Concentric rings, ~12 cm apart, every part with room around it.
                    int k = board == null ? i - 1 : i;
                    int perRing = 8;
                    int r = k / perRing;
                    double radius = (board == null ? 0.14 : ring) + r * 0.12;
                    double a = 2 * Math.PI * (k % perRing) / Math.Min(perRing, n - r * perRing) + r * 0.3;
                    pose = new WorldPose { X = radius * Math.Cos(a), Y = radius * Math.Sin(a), Yaw = a * 180 / Math.PI + 90 };
                }

                var o = w.AddComponent(library, p.PresetId ?? def.Id, pose);
                o.Name = $"{p.RefDes} {def.Name}";
                foreach (var kv in p.Parameters) o.Parameters[kv.Key] = kv.Value;
                if (package.FirmwareFor(p.RefDes) is { } fw) o.Firmware = fw;   // the board's code goes with it
                objects[p.RefDes] = o;
            }

            // Cables: every off-board pin to its net — to the board's own pin on that net when the net
            // reaches the PCB (a connector pin first), otherwise to one anchor pin of the net (a controller's).
            int colour = 0;
            string[] signal = { "#F2A33B", "#2F7FD8", "#4CC38A", "#B07CF2", "#F2F2F2", "#E8C547" };
            foreach (var net in netlist.Nets)
            {
                var loose = net.Connections.Where(c => objects.ContainsKey(c.RefDes)).ToList();
                if (loose.Count == 0) continue;
                var onPcb = board == null ? new List<NetConnection>() : net.Connections.Where(c => onBoard.Contains(c.RefDes)).ToList();
                if (onPcb.Count == 0 && loose.Count < 2) continue;
                bool ground = net.IsGround || net.Name.StartsWith("GND", StringComparison.OrdinalIgnoreCase);
                bool power = !ground && (net.Name.Contains("V") && (net.Name.Contains("CC") || net.Name.Contains("IN") || net.Name.Contains("BAT") || char.IsDigit(net.Name.FirstOrDefault())) || net.Name.StartsWith("+"));
                string col = ground ? "#1F1F23" : power ? "#E5484D" : signal[colour++ % signal.Length];
                double awg = ground || power ? 22 : 26;

                CableEnd anchor;
                IEnumerable<NetConnection> rest;
                if (onPcb.Count > 0)
                {
                    var best = onPcb.OrderBy(c => sch.FindPart(c.RefDes) is { } bp && library.Find(bp.PartId)?.Symbol == SymbolKind.Connector ? 0 : 1).First();
                    anchor = new CableEnd { ObjectId = board!.Id, Pin = best.RefDes + "." + best.Pin };
                    rest = loose;
                }
                else
                {
                    var hub = loose.OrderBy(c => library.Find(sch.FindPart(c.RefDes)!.PartId)!.Symbol == SymbolKind.Board ? 0 : 1).First();
                    anchor = new CableEnd { ObjectId = objects[hub.RefDes].Id, Pin = hub.Pin };
                    rest = loose.Where(c => c != hub);
                }

                foreach (var c in rest)
                {
                    var o = objects[c.RefDes];
                    var cable = w.Connect(anchor, new CableEnd { ObjectId = o.Id, Pin = c.Pin }, awg, col);
                    var other = w.Find(anchor.ObjectId)!;
                    double run = Math.Sqrt(Math.Pow(o.Pose.X - other.Pose.X, 2) + Math.Pow(o.Pose.Y - other.Pose.Y, 2) + Math.Pow(o.Pose.Z - other.Pose.Z, 2));
                    cable.Length = Math.Round(Math.Max(0.10, 1.3 * run + 0.05) / 0.05) * 0.05;   // jumper lengths come in 5 cm steps
                }
            }

            return w;
        }
    }
}
