using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Automation
{
    /// <summary>
    /// Geometry of world objects without the app's 3D models: bodies from their size, common parts
    /// (TT / can motors, SG90-class servos, HC-SR04, batteries, boards) from datasheet dimensions.
    /// Close to, but not identical with, what the app derives from its procedural models — bake the
    /// app's geometry (<see cref="BakedGeometry"/>, RobotWinStudio --bake-geometry) for identical physics.
    /// </summary>
    public sealed class ApproximateGeometry : IWorldGeometry
    {
        private readonly WorldDocument _world;
        private readonly PartLibrary _library;

        public ApproximateGeometry(WorldDocument world, PartLibrary library)
        {
            _world = world;
            _library = library;
        }

        public WorldBodyGeometry GeometryOf(WorldObject o)
        {
            if (ReadyParts.Geometry(o) is { } ready) return ready;   // ready props, wheels, casters: same shapes as in the app (a prop is its swept disc)
            switch (o.Kind)
            {
                case WorldObjectKind.Body:
                    return BodyGeometry(o);
                case WorldObjectKind.Model when o.Mesh != null:
                    try
                    {
                        var mesh = MeshModel.FromEmbedded(o.Mesh);
                        var mb = mesh.Bounds;
                        mesh.Translate(new Vec3(-(mb.Min.X + mb.Max.X) / 2, -(mb.Min.Y + mb.Max.Y) / 2, -mb.Min.Z));
                        var b = mesh.Bounds;
                        double density = MechatronicSystem.DensityOf(o.Material) * Math.Max(0.05, o.Infill);
                        double mass = Math.Abs(MassProperties.FromMesh(mesh, density).Mass);
                        return new WorldBodyGeometry { Min = b.Min, Max = b.Max, Shape = "mesh", Mesh = mesh, MassKg = Math.Max(1e-4, mass) };
                    }
                    catch (FormatException)
                    {
                        return Box(0.02, 0.02, 0.02, 0.01);
                    }

                case WorldObjectKind.Board:
                    var board = _world.FindDesign(o.DesignId)?.Package.Schematic.Board;
                    double w = board != null && board.Width > 0 ? board.Width * 1e-3 : 0.0686;
                    double h = board != null && board.Height > 0 ? board.Height * 1e-3 : 0.0533;
                    return new WorldBodyGeometry
                    {
                        Min = new Vec3(-w / 2, -h / 2, 0), Max = new Vec3(w / 2, h / 2, 0.012),
                        MassKg = w * h * 0.0016 * 1850 + 0.012
                    };
                case WorldObjectKind.Component:
                    return ComponentGeometry(o);
                default:
                    return Box(0.02, 0.02, 0.01, 0.01);
            }
        }

        private static WorldBodyGeometry Box(double x, double y, double z, double mass) =>
            new WorldBodyGeometry { Min = new Vec3(-x / 2, -y / 2, 0), Max = new Vec3(x / 2, y / 2, z), MassKg = mass };

        private static WorldBodyGeometry BodyGeometry(WorldObject o)
        {
            double density = MechatronicSystem.DensityOf(o.Material == "KAUÇUK" ? "ABS" : o.Material) * Math.Max(0.05, o.Infill);
            switch (o.Shape)
            {
                case "wheel":
                    return new WorldBodyGeometry
                    {
                        Min = new Vec3(-o.SizeX / 2, -o.SizeZ / 2, 0), Max = new Vec3(o.SizeX / 2, o.SizeZ / 2, o.SizeX),
                        Shape = "cylinder-y", MassKg = Math.Max(1e-4, Math.PI * o.SizeX * o.SizeX / 4 * o.SizeZ * 0.55 * density)
                    };
                case "cylinder":
                    return new WorldBodyGeometry
                    {
                        Min = new Vec3(-o.SizeX / 2, -o.SizeX / 2, 0), Max = new Vec3(o.SizeX / 2, o.SizeX / 2, o.SizeZ),
                        Shape = "cylinder-z", MassKg = Math.Max(1e-4, Math.PI * o.SizeX * o.SizeX / 4 * o.SizeZ * density)
                    };
                case "sphere":
                    return new WorldBodyGeometry
                    {
                        Min = new Vec3(-o.SizeX / 2, -o.SizeX / 2, 0), Max = new Vec3(o.SizeX / 2, o.SizeX / 2, o.SizeX),
                        Shape = "sphere", MassKg = Math.Max(1e-4, Math.PI / 6 * o.SizeX * o.SizeX * o.SizeX * density)
                    };
                case "wing" when o.Parameters.TryGetValue("symmetric", out var sym) && string.Equals(sym, "false", StringComparison.OrdinalIgnoreCase):
                    // [gas] a single panel (fin) stands on its root: span along its z, thickness across y.
                    return new WorldBodyGeometry
                    {
                        Min = new Vec3(-o.SizeX / 2, -o.SizeZ / 2, 0), Max = new Vec3(o.SizeX / 2, o.SizeZ / 2, o.SizeY),
                        MassKg = Math.Max(1e-4, o.SizeX * o.SizeY * o.SizeZ * density)
                    };
                default:
                    return new WorldBodyGeometry
                    {
                        Min = new Vec3(-o.SizeX / 2, -o.SizeY / 2, 0), Max = new Vec3(o.SizeX / 2, o.SizeY / 2, o.SizeZ),
                        MassKg = Math.Max(1e-4, WorldPhysics.MaterialVolume(o) * density)   // [shell] hollow bodies weigh their walls
                    };
            }
        }

        private WorldBodyGeometry ComponentGeometry(WorldObject o)
        {
            string part = (o.PartId ?? string.Empty).ToLowerInvariant();
            string preset = (o.PresetId ?? string.Empty).ToUpperInvariant();
            o.Parameters.TryGetValue("model", out var modelParam);
            string model = ((modelParam ?? string.Empty) + " " + preset).ToUpperInvariant();
            var def = o.PartId != null ? _library.Find(o.PartId) : null;
            if (part == "dc-motor" || def?.Id == "dc-motor")
            {
                if (model.Contains("2212"))
                {
                    // [uav] 2212 outrunner (Ø28 × 32 mm, 50 g), prop shaft along +z like the 2204.
                    return new WorldBodyGeometry
                    {
                        Min = new Vec3(-0.014, -0.014, 0), Max = new Vec3(0.014, 0.014, 0.032), MassKg = 0.050,
                        PortName = "shaft", PortPivot = new Vec3(0, 0, 0.040), PortAxis = Vec3.UnitZ
                    };
                }

                if (model.Contains("KV") || model.Contains("BRUSHLESS") || model.Contains("2204"))
                {
                    // [esc] outrunner standing on its base, prop shaft up (same as the 3D model).
                    return new WorldBodyGeometry
                    {
                        Min = new Vec3(-0.014, -0.014, 0), Max = new Vec3(0.014, 0.014, 0.0215), MassKg = 0.028,
                        PortName = "shaft", PortPivot = new Vec3(0, 0, 0.026), PortAxis = Vec3.UnitZ
                    };
                }

                bool coreless = model.Contains("8520") || model.Contains("0720");
                bool tt = model.Contains("TT") || (!coreless && !model.Contains("25GA") && !model.Contains("JGA") && !model.Contains("N20") && !model.Contains("130"));
                if (tt)
                {
                    return new WorldBodyGeometry
                    {
                        // The app's detailed TT model (gearbox + can, shaft across).
                        Min = new Vec3(-0.03401, -0.0184, 0), Max = new Vec3(0.0336, 0.0184, 0.02479), MassKg = 0.03,
                        PortName = "shaft", PortPivot = new Vec3(-0.0227, 0.0184, 0.0112), PortAxis = Vec3.UnitY
                    };
                }

                double r = (coreless ? 4.25 : model.Contains("N20") ? 6 : model.Contains("130") ? 10 : 12.5) * 1e-3;
                double len = (coreless ? 20 : model.Contains("N20") ? 24 : model.Contains("130") ? 25 : 50) * 1e-3;
                return new WorldBodyGeometry
                {
                    Min = new Vec3(-len / 2, -r, 0), Max = new Vec3(len / 2, r, 2 * r), MassKg = coreless ? 0.004 : 0.03,
                    PortName = "shaft", PortPivot = new Vec3(len / 2 + (coreless ? 0.006 : 0.012), 0, r), PortAxis = Vec3.UnitX
                };
            }

            if (part.Contains("servo") || def?.Id == "servo")
            {
                bool big = model.Contains("MG996") || model.Contains("DS3218");
                double k = big ? 1.75 : 1.0;
                return new WorldBodyGeometry
                {
                    Min = new Vec3(-0.0114 * k, -0.0061 * k, 0), Max = new Vec3(0.0114 * k, 0.0061 * k, 0.0227 * k), MassKg = big ? 0.055 : 0.009,
                    PortName = "horn", PortPivot = new Vec3(-0.0054 * k, 0, 0.031 * k), PortAxis = Vec3.UnitZ
                };
            }

            if (part.Contains("sr04") || part.Contains("ultrasonic") || preset.Contains("SR04"))
            {
                return new WorldBodyGeometry
                {
                    Min = new Vec3(-0.0225, -0.008, 0), Max = new Vec3(0.0225, 0.004, 0.023), MassKg = 0.009,
                    SensorOrigin = new Vec3(0, -0.008, 0.014), SensorAxis = new Vec3(0, -1, 0)
                };
            }

            string id = (o.PresetId ?? o.PartId ?? string.Empty).ToLowerInvariant();
            if (id.Contains("bat"))
            {
                if (id.Contains("4aa")) return Box(0.0575, 0.0616, 0.0155, 0.11);
                if (id.Contains("2s-usbc")) return Box(0.072, 0.039, 0.019, 0.1);   // [usbc] 2×18650 pack with its charge board
                if (id.Contains("18650")) return Box(0.0704, 0.0186, 0.0186, 0.047);   // lying cell (+ USB-C cap)
                if (id.Contains("aa-usbc")) return Box(0.0513, 0.0145, 0.0145, 0.022);
                if (id.Contains("2aa")) return Box(0.058, 0.031, 0.016, 0.055);
                if (id.Contains("9v")) return Box(0.0264, 0.0176, 0.0464, 0.045);   // stands on its end, like the 3D model
                if (id.Contains("1s")) return Box(0.045, 0.02, 0.008, 0.009);
                if (id.Contains("lipo"))
                {
                    // Same rule as the 3D model: a 2S 2200 mAh pack is 105 × 34 × 16 mm, smaller packs scale with capacity × cells.
                    double cap = o.Parameters.TryGetValue("capacity", out var cs) && double.TryParse(cs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cv) ? cv : 2.2;
                    int cells = id.Contains("3s") ? 3 : id.Contains("1s") ? 1 : 2;
                    double k = Math.Max(0.45, Math.Min(2.0, Math.Cbrt(cap * cells / 4.4)));
                    return Box(0.105 * k, 0.034 * k, (0.0068 * cells + 0.0026) * k, 0.1 * k * k * k);
                }

                return Box(0.05, 0.03, 0.015, 0.05);
            }

            if (def?.Id == "esc") return Box(0.030, 0.016, 0.006, 0.008);   // [esc] 20 A ESC, shrink-wrapped
            if (def?.Symbol == SymbolKind.Board)
            {
                // Real board outlines, in the 3D model's frame (origin at the pin-1 corner): a Nano is not an Uno.
                if (id.Contains("nano")) return new WorldBodyGeometry { Min = new Vec3(-0.0014, -0.041, -0.0029), Max = new Vec3(0.0166, 0.0043, 0.0051), MassKg = 0.007 };
                if (id.Contains("uno")) return new WorldBodyGeometry { Min = new Vec3(-0.0064, -0.0533, -0.0029), Max = new Vec3(0.0686, 0, 0.0084), MassKg = 0.025 };
                if (id.Contains("mini")) return Box(0.033, 0.018, 0.006, 0.004);
                if (id.Contains("mega")) return Box(0.1016, 0.0533, 0.012, 0.037);
                if (id.Contains("esp32")) return new WorldBodyGeometry { Min = new Vec3(-0.0014, -0.0396, -0.0029), Max = new Vec3(0.0269, 0.0127, 0.0072), MassKg = 0.01 };   // DevKit V1, long side along Y
                if (id.Contains("esp8266") || id.Contains("nodemcu")) return Box(0.049, 0.026, 0.009, 0.009);
                if (id.Contains("pico")) return Box(0.051, 0.021, 0.004, 0.003);
                return Box(0.0686, 0.0533, 0.012, 0.025);
            }
            return Box(0.03, 0.02, 0.012, 0.01);
        }

        public double CableLength(WorldCable cable)
        {
            var a = _world.Find(cable.A.ObjectId);
            var b = _world.Find(cable.B.ObjectId);
            if (a == null || b == null) return 0.2;
            double d = (a.Pose.Position - b.Pose.Position).Length;
            return Math.Max(0.03, d * 1.25 + 0.05);
        }
    }

    /// <summary>
    /// Geometry captured from the app (its 3D models) and stored next to a world as
    /// "name.rwworld.geometry.json", so a headless run on any OS uses exactly the app's bodies.
    /// </summary>
    public sealed class BakedGeometry : IWorldGeometry
    {
        private readonly Dictionary<string, WorldBodyGeometry> _bodies = new Dictionary<string, WorldBodyGeometry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _cables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly IWorldGeometry _fallback;

        private BakedGeometry(IWorldGeometry fallback)
        {
            _fallback = fallback;
        }

        public const string Suffix = ".geometry.json";

        public WorldBodyGeometry GeometryOf(WorldObject o)
        {
            if (!_bodies.TryGetValue(o.Id, out var g)) return _fallback.GeometryOf(o);
            var copy = new WorldBodyGeometry
            {
                Min = g.Min, Max = g.Max, MassKg = g.MassKg, Shape = g.Shape, PortName = g.PortName, PortPivot = g.PortPivot, PortAxis = g.PortAxis,
                SensorOrigin = g.SensorOrigin, SensorAxis = g.SensorAxis, Hull = g.Hull
            };
            if (copy.Shape == "mesh")
            {
                // Meshes are not stored twice: rebuilt from the world's own embedded mesh like the app does.
                var mesh = _fallback.GeometryOf(o);
                if (mesh.Mesh != null) copy.Mesh = mesh.Mesh;
                else copy.Shape = "box";
            }

            return copy;
        }

        public double CableLength(WorldCable cable) => _cables.TryGetValue(cable.Id, out var l) ? l : _fallback.CableLength(cable);

        private readonly Dictionary<string, CablePins> _pins = new Dictionary<string, CablePins>(StringComparer.OrdinalIgnoreCase);

        /// <summary>[route] The cable's plug positions as the app's models place them, when the file has them.</summary>
        public CablePins? PinsOf(WorldCable cable) => _pins.TryGetValue(cable.Id, out var p) ? p : (CablePins?)null;

        /// <summary>Writes every object's geometry and cable length of <paramref name="world"/> as JSON.</summary>
        public static string Export(WorldDocument world, IWorldGeometry geometry)
        {
            var objects = new JsonObject();
            foreach (var o in world.Objects)
            {
                var g = geometry.GeometryOf(o);
                var j = new JsonObject { ["shape"] = g.Shape, ["min"] = J.VExact(g.Min), ["max"] = J.VExact(g.Max), ["mass"] = J.Exact(g.MassKg) };
                if (g.PortName != null)
                {
                    j["port"] = g.PortName;
                    j["port_pivot"] = J.VExact(g.PortPivot);
                    j["port_axis"] = J.VExact(g.PortAxis);
                }

                if (g.SensorOrigin is { } so)
                {
                    j["sensor_origin"] = J.VExact(so);
                    j["sensor_axis"] = J.VExact(g.SensorAxis);
                }

                if (g.Shape == "hull" && g.Hull != null)
                {
                    var hull = new JsonArray();
                    foreach (var p in g.Hull) hull.Add(J.VExact(p));
                    j["hull"] = hull;
                }

                objects[o.Id] = j;
            }

            var cables = new JsonObject();
            var pins = new JsonObject();
            foreach (var c in world.Cables)
            {
                cables[c.Id] = J.Exact(c.Length > 0 ? c.Length : geometry.CableLength(c));
                if (geometry.PinsOf(c) is { } p) pins[c.Id] = new JsonObject { ["a"] = J.VExact(p.A), ["b"] = J.VExact(p.B) };
            }

            var root = new JsonObject { ["format"] = "robotwin.world-geometry", ["version"] = 1, ["title"] = world.Title, ["objects"] = objects, ["cables"] = cables };
            if (pins.Count > 0) root["pins"] = pins;
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        public static BakedGeometry Parse(string json, IWorldGeometry fallback)
        {
            var baked = new BakedGeometry(fallback);
            var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("geometry json");
            if (root["objects"] is JsonObject objects)
            {
                foreach (var kv in objects)
                {
                    if (!(kv.Value is JsonObject o)) continue;
                    var g = new WorldBodyGeometry
                    {
                        Shape = J.Str(o, "shape") ?? "box",
                        Min = J.Vec(o, "min") ?? new Vec3(-0.01, -0.01, 0),
                        Max = J.Vec(o, "max") ?? new Vec3(0.01, 0.01, 0.01),
                        MassKg = J.Num(o, "mass", 0.01),
                        PortName = J.Str(o, "port"),
                        PortPivot = J.Vec(o, "port_pivot") ?? Vec3.Zero,
                        PortAxis = J.Vec(o, "port_axis") ?? Vec3.UnitZ,
                        SensorOrigin = J.Vec(o, "sensor_origin"),
                        SensorAxis = J.Vec(o, "sensor_axis") ?? Vec3.UnitX
                    };
                    if (o["hull"] is JsonArray hull)
                    {
                        g.Hull = new List<Vec3>(hull.Count);
                        foreach (var p in hull)
                        {
                            if (p is JsonArray a && a.Count >= 3 && J.TryNumber(a[0], out double x) && J.TryNumber(a[1], out double y) && J.TryNumber(a[2], out double z))
                                g.Hull.Add(new Vec3(x, y, z));
                        }
                    }

                    baked._bodies[kv.Key] = g;
                }
            }

            if (root["cables"] is JsonObject cables)
            {
                foreach (var kv in cables)
                {
                    if (J.TryNumber(kv.Value, out double len)) baked._cables[kv.Key] = len;
                }
            }

            if (root["pins"] is JsonObject pinMap)
            {
                foreach (var kv in pinMap)
                {
                    if (kv.Value is JsonObject pin && J.Vec(pin, "a") is { } a && J.Vec(pin, "b") is { } b) baked._pins[kv.Key] = new CablePins(a, b);
                }
            }

            return baked;
        }

        /// <summary>The baked geometry stored beside a world file, or null.</summary>
        public static BakedGeometry? TryLoadBeside(string worldPath, IWorldGeometry fallback)
        {
            string path = worldPath + Suffix;
            return File.Exists(path) ? Parse(File.ReadAllText(path), fallback) : null;
        }
    }
}
