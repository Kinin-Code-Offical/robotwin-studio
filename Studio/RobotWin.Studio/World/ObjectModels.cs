using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Pcb;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Assembly3D;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    /// <summary>Where a cable plugs into an object (object-local metres, Z up).</summary>
    public readonly struct PinAnchor
    {
        public PinAnchor(string pin, string label, Point3D local)
        {
            Pin = pin;
            Label = label;
            Local = local;
        }

        /// <summary>Value stored in <see cref="CableEnd.Pin"/>.</summary>
        public string Pin { get; }

        /// <summary>Shown to the user ("J1.2 (GND)").</summary>
        public string Label { get; }

        public Point3D Local { get; }
    }

    /// <summary>A moving sub-part other objects can be fixed to (servo horn, motor shaft), object-local.</summary>
    public readonly struct PortFrame
    {
        public PortFrame(string name, string label, Point3D pivot, Vector3D axis)
        {
            Name = name;
            Label = label;
            Pivot = pivot;
            Axis = axis;
        }

        public string Name { get; }
        public string Label { get; }
        public Point3D Pivot { get; }
        public Vector3D Axis { get; }
    }

    /// <summary>A built object: its static body, the moving part (if any) and the attachment data.</summary>
    public sealed class ObjectModel
    {
        public Model3DGroup Body { get; } = new Model3DGroup();

        /// <summary>Servo horn / motor shaft geometry, rotated about <see cref="Port"/>'s axis while simulating.</summary>
        public Model3DGroup? Moving { get; set; }

        public PortFrame? Port { get; set; }
        public List<PinAnchor> Pins { get; } = new List<PinAnchor>();

        /// <summary>Local bounding box in metres.</summary>
        public Rect3D Bounds { get; set; }

        /// <summary>Solid body for physics when it differs from <see cref="Bounds"/> (no cable pigtails or leads).</summary>
        public Rect3D CollisionBounds { get; set; } = Rect3D.Empty;

        /// <summary>Emissive layer of LEDs/lamps on the object (lit by the simulation), by inner refdes.</summary>
        public Dictionary<string, EmissiveMaterial> Lights { get; } = new Dictionary<string, EmissiveMaterial>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Live screen of an OLED/LCD module (texture refreshed from the display model while simulating).</summary>
        public ImageBrush? Screen { get; set; }

        /// <summary>Pixel colour (OLED) or backlight (LCD) parameter of the screen.</summary>
        public string ScreenStyle { get; set; } = string.Empty;

        /// <summary>Emissive glow used for heat / burn tint of the whole object.</summary>
        public EmissiveMaterial Heat { get; set; } = new EmissiveMaterial(new SolidColorBrush(Colors.Black));

        /// <summary>Detailed model of a free-standing part (live hooks: heat, charring, light, screen).</summary>
        public Models3D.ComponentInstance? Instance { get; set; }

        /// <summary>Detailed models of the parts on a board, by inner refdes.</summary>
        public List<(string RefDes, Models3D.ComponentInstance Instance)> Parts { get; } = new List<(string, Models3D.ComponentInstance)>();

        /// <summary>Rotation of the detailed model's own moving part (used instead of <see cref="Moving"/>).</summary>
        public AxisAngleRotation3D? PortSpin { get; set; }

        /// <summary>+1, or -1 when the port axis was flipped to point out of the attachment side.</summary>
        public double PortSign { get; set; } = 1;

        /// <summary>Display surface whose brush becomes <see cref="Screen"/> once a picture is shown.</summary>
        public DiffuseMaterial? ScreenMaterial { get; set; }
    }

    /// <summary>
    /// Procedural 3D models of world objects in real dimensions. Components are modelled
    /// from their datasheet sizes; boards are built from their PCB layout; every pin that a
    /// cable can reach gets an anchor.
    /// </summary>
    public static class ObjectModels
    {
        private const double Mm = 0.001;

        public static ObjectModel Build(WorldObject o, WorldDocument world, PartLibrary library)
        {
            var model = o.Kind switch
            {
                WorldObjectKind.Board => DetailedBoard(o, world, library) ?? BuildBoard(o, world, library),
                WorldObjectKind.Component => DetailedComponent(o, library) ?? BuildComponent(o, library),
                WorldObjectKind.Model => BuildMesh(o),
                _ => BuildBody(o)
            };
            if (model.Bounds.IsEmpty) model.Bounds = model.Body.Bounds;
            return model;
        }

        // ================================================================ materials

        public static Color MaterialColor(string? material) => (material ?? string.Empty).ToUpperInvariant() switch
        {
            "KAUÇUK" or "KAUCUK" or "RUBBER" => Color.FromRgb(0x24, 0x24, 0x27),
            "AHŞAP" or "AHSAP" or "MDF" or "KONTRPLAK" => Color.FromRgb(0xC8, 0xA0, 0x6E),
            "FR4" => Color.FromRgb(0x1E, 0x56, 0x31),
            _ => MeshVisual.MaterialColor(material ?? "PLA")
        };

        private static Color ParseColor(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch (FormatException)
            {
                return fallback;
            }
        }

        private static Material Mat(Color c, double spec = 0.1, EmissiveMaterial? heat = null)
        {
            var g = new MaterialGroup();
            g.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
            if (heat != null) g.Children.Add(heat);
            if (spec > 0) g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb((byte)(spec * 255), 255, 255, 255)), 40));
            return g;
        }

        private static void Box(Model3DGroup g, double cx, double cy, double cz, double sx, double sy, double sz, Color c, double spec = 0.1, EmissiveMaterial? heat = null) =>
            g.Children.Add(M.Model(M.Box(cx * Mm, cy * Mm, cz * Mm, sx * Mm, sy * Mm, sz * Mm), Mat(c, spec, heat)));

        /// <summary>Cylinder along an axis (0 = X, 1 = Y, 2 = Z), centred at (cx, cy, cz), sizes in mm.</summary>
        private static void Cyl(Model3DGroup g, double radius, double length, int axis, double cx, double cy, double cz, Color c, double spec = 0.2, EmissiveMaterial? heat = null, int seg = 28) =>
            g.Children.Add(M.Model(M.Cylinder(radius * Mm, length * Mm, axis, seg, cx * Mm, cy * Mm, cz * Mm), Mat(c, spec, heat)));

        // ================================================================ mechanical bodies

        private static ObjectModel BuildBody(WorldObject o)
        {
            var m = new ObjectModel();
            var c = ParseColor(o.Color, MaterialColor(o.Material));
            double sx = o.SizeX * 1000, sy = o.SizeY * 1000, sz = o.SizeZ * 1000;
            switch (o.Shape)
            {
                case "cylinder":
                    Cyl(m.Body, sx / 2, sz, 2, 0, 0, sz / 2, c, 0.15, m.Heat, 40);
                    break;
                case "wheel":
                    // Tyre (rubber) on a hub; axle along local Y.
                    Cyl(m.Body, sx / 2, sz, 1, 0, 0, sx / 2, c, 0.05, m.Heat, 48);
                    Cyl(m.Body, sx / 2 * 0.62, sz * 1.04, 1, 0, 0, sx / 2, Color.FromRgb(0xE8, 0xC2, 0x2C), 0.25, null, 32);
                    Cyl(m.Body, 2.5, sz * 1.1, 1, 0, 0, sx / 2, Color.FromRgb(0x30, 0x30, 0x34), 0.3, null, 12);
                    break;
                case "sphere":
                    m.Body.Children.Add(M.Model(M.Sphere(sx / 2 * Mm, 0, 0, sx / 2 * Mm, 24), Mat(c, 0.25, m.Heat)));
                    break;
                default:
                    Box(m.Body, 0, 0, sz / 2, sx, sy, sz, c, 0.12, m.Heat);
                    break;
            }

            m.Bounds = new Rect3D(-o.SizeX / 2, -Math.Max(o.SizeY, o.Shape == "wheel" ? o.SizeZ : o.SizeY) / 2, 0,
                o.SizeX, Math.Max(o.SizeY, o.Shape == "wheel" ? o.SizeZ : o.SizeY), o.Shape == "wheel" || o.Shape == "sphere" ? o.SizeX : o.SizeZ);
            return m;
        }

        private static ObjectModel BuildMesh(WorldObject o)
        {
            var m = new ObjectModel();
            if (o.Mesh == null) return m;
            MeshModel mesh;
            try
            {
                mesh = MeshModel.FromEmbedded(o.Mesh);
            }
            catch (Exception ex) when (ex is FormatException || ex is System.IO.InvalidDataException)
            {
                return m;
            }

            var b = mesh.Bounds;
            mesh.Translate(new Vec3(-(b.Min.X + b.Max.X) / 2, -(b.Min.Y + b.Max.Y) / 2, -b.Min.Z));
            m.Body.Children.Add(MeshVisual.Build(mesh, ParseColor(o.Color, MaterialColor(o.Material))));
            var nb = mesh.Bounds;
            m.Bounds = new Rect3D(nb.Min.X, nb.Min.Y, nb.Min.Z, nb.Max.X - nb.Min.X, nb.Max.Y - nb.Min.Y, nb.Max.Z - nb.Min.Z);
            return m;
        }

        // ================================================================ boards

        // ================================================================ detailed models (Models3D library)

        private static readonly Matrix3D MmToM = new Matrix3D(Mm, 0, 0, 0, 0, Mm, 0, 0, 0, 0, Mm, 0, 0, 0, 0, 1);

        private static Rect3D Scale(Rect3D r) => r.IsEmpty ? r : new Rect3D(r.X * Mm, r.Y * Mm, r.Z * Mm, r.SizeX * Mm, r.SizeY * Mm, r.SizeZ * Mm);

        private static bool Recoverable(Exception ex) =>
            ex is InvalidOperationException || ex is ArgumentException || ex is NullReferenceException || ex is IndexOutOfRangeException || ex is KeyNotFoundException;

        /// <summary>
        /// Free-standing part from the detailed model library: real geometry, the servo horn / motor
        /// shaft as its port, cable anchors at the actual pin, lead and connector positions.
        /// </summary>
        private static ObjectModel? DetailedComponent(WorldObject o, PartLibrary library)
        {
            var def = o.PartId != null ? library.Find(o.PartId) : null;
            if (def == null) return null;
            // Radio gear has its own models with live hooks (controller lightbar, USB port).
            var w = RobotTwin.CoreSim.Design.WirelessParts.GamepadId;
            if (def.Id == w || def.Id == RobotTwin.CoreSim.Design.WirelessParts.UsbHostId || def.Id == RobotTwin.CoreSim.Design.WirelessParts.RouterId ||
                def.Id == RobotTwin.CoreSim.Design.WirelessParts.BeaconId || def.Id == RobotTwin.CoreSim.Design.WirelessParts.IrRemoteId)
            {
                return null;
            }

            try
            {
                var preset = o.PresetId != null ? library.FindPreset(o.PresetId) : null;
                var p = library.ResolveParameters(def, preset, o.Parameters);
                var inst = Models3D.ComponentModels.Create(def, p, true);
                if (inst.Bounds.IsEmpty) return null;
                var m = new ObjectModel { Instance = inst };
                var root = new Model3DGroup { Transform = new MatrixTransform3D(MmToM) };
                root.Children.Add(inst.Root);
                m.Body.Children.Add(root);
                m.Bounds = Scale(inst.Bounds);
                if (inst.Glow != null) m.Heat = inst.Glow;

                // Physics body: the main shell only (no cable pigtail, leads, horn or shaft).
                foreach (var child in inst.Root.Children)
                {
                    if (child is GeometryModel3D gm && gm.Material is MaterialGroup mg && inst.Body != null && mg.Children.Contains(inst.Body))
                    {
                        m.CollisionBounds = Scale(gm.Bounds);
                    }
                }

                // The lowest point (pin tails, feet) rests on the floor; a shell much smaller than the part
                // (the chip of a module board) would let the rest sink, so the whole footprint is used then.
                if (!m.CollisionBounds.IsEmpty)
                {
                    var c = m.CollisionBounds;
                    var full = m.Bounds;
                    bool small = c.SizeX * c.SizeY < 0.3 * full.SizeX * full.SizeY;
                    double z0 = Math.Min(c.Z, full.Z);
                    m.CollisionBounds = small
                        ? new Rect3D(full.X, full.Y, z0, full.SizeX, full.SizeY, c.Z + c.SizeZ - z0)
                        : new Rect3D(c.X, c.Y, z0, c.SizeX, c.SizeY, c.Z + c.SizeZ - z0);
                }

                var spin = inst.Moving.FirstOrDefault(x => x.Name == "horn" || x.Name == "shaft") ?? inst.Moving.FirstOrDefault();
                if (spin != null)
                {
                    // The port sits where things attach (horn top, shaft end) and points out of the part.
                    var axis = spin.Axis;
                    var tip = spin.Tip;
                    double sign = 1;
                    if (Math.Abs(axis.Y) > 0.9 && axis.Y < 0)
                    {
                        // Gearbox shafts come out of both sides: use the +Y end like the other parts.
                        axis = -axis;
                        tip = new Point3D(tip.X, spin.Pivot.Y + (spin.Pivot.Y - tip.Y), tip.Z);
                        sign = -1;
                    }

                    string label = spin.Name switch { "horn" => "Servo kolu", "shaft" => "Motor mili", "knob" => "Düğme", _ => spin.Name };
                    m.Port = new PortFrame(spin.Name, label, new Point3D(tip.X * Mm, tip.Y * Mm, tip.Z * Mm), axis);
                    m.PortSpin = spin.Rotation;
                    m.PortSign = sign;
                }

                foreach (var (pin, pos, _) in Models3D.ComponentModels.MapPins(def, p, inst.PadAnchors))
                {
                    m.Pins.Add(new PinAnchor(pin, pin, new Point3D(pos.X * Mm, pos.Y * Mm, pos.Z * Mm)));
                }

                // A model without cable anchors cannot be wired: fall back to the simple one.
                if (m.Pins.Count == 0 && def.Pins.Count > 0) return null;
                if (inst.Screen != null)
                {
                    m.ScreenMaterial = inst.Screen;
                    m.Screen = new ImageBrush { Stretch = Stretch.Fill };
                    m.ScreenStyle = inst.ScreenStyle;
                }

                return m;
            }
            catch (Exception ex) when (Recoverable(ex))
            {
                return null;
            }
        }

        /// <summary>User board from its PCB layout with every part as a detailed model.</summary>
        private static ObjectModel? DetailedBoard(WorldObject o, WorldDocument world, PartLibrary library)
        {
            var sch = world.FindDesign(o.DesignId)?.Package.Schematic;
            if (sch == null) return null;
            try
            {
                var board = sch.Board;
                if (board == null || board.Components.Count == 0)
                {
                    board = BoardDocument.Synchronize(sch, library);
                    AutoPlacer.Place(board);
                }

                var scene = Models3D.BoardModels.BuildScene(board, sch, library);
                var m = new ObjectModel();
                var root = new Model3DGroup { Transform = new MatrixTransform3D(MmToM) };
                root.Children.Add(scene.Root);
                m.Body.Children.Add(root);
                foreach (var part in scene.Parts) m.Parts.Add((part.RefDes, part.Instance));
                foreach (var (refDes, pin, pos, _) in Models3D.BoardModels.ConnectorAnchors(board, sch, library))
                {
                    m.Pins.Add(new PinAnchor(refDes + "." + pin, refDes + "." + pin, new Point3D(pos.X * Mm, pos.Y * Mm, pos.Z * Mm)));
                }

                var b = scene.Root.Bounds;
                double w = board.Width, h = board.Height;
                m.Bounds = b.IsEmpty ? new Rect3D(-w / 2 * Mm, -h / 2 * Mm, 0, w * Mm, h * Mm, 13 * Mm) : Scale(b);
                double top = b.IsEmpty ? 12 : Math.Max(1.6, Math.Min(b.Z + b.SizeZ, 25));
                m.CollisionBounds = new Rect3D(-w / 2 * Mm, -h / 2 * Mm, 0, w * Mm, h * Mm, top * Mm);
                return m;
            }
            catch (Exception ex) when (Recoverable(ex))
            {
                return null;
            }
        }

        /// <summary>
        /// Port of a catalogue part in its own frame (metres): where a wheel or arm attaches, and the
        /// top of the part's body. Templates use this to place what they mount on it.
        /// </summary>
        public static (Point3D Pivot, Vector3D Axis, double Height) Mount(PartLibrary library, string presetId)
        {
            var world = new WorldDocument();
            var o = world.AddComponent(library, presetId, new WorldPose());
            var m = Build(o, world, library);
            var body = m.CollisionBounds.IsEmpty ? m.Bounds : m.CollisionBounds;
            return m.Port is { } pf ? (pf.Pivot, pf.Axis, body.Z + body.SizeZ) : (new Point3D(), new Vector3D(0, 0, 1), body.Z + body.SizeZ);
        }

        private static ObjectModel BuildBoard(WorldObject o, WorldDocument world, PartLibrary library)
        {
            var m = new ObjectModel();
            var design = world.FindDesign(o.DesignId);
            var sch = design?.Package.Schematic;
            var board = sch?.Board;
            if (sch == null) return m;
            if (board == null || board.Components.Count == 0)
            {
                board = BoardDocument.Synchronize(sch, library);
                AutoPlacer.Place(board);
            }

            double w = board.Width, h = board.Height, t = 1.6;
            Box(m.Body, 0, 0, t / 2, w, h, t, Color.FromRgb(0x1E, 0x56, 0x31), 0.08, m.Heat);
            try
            {
                var top = Pcb.Editor3DTextures.BoardTop(board);
                m.Body.Children.Add(new GeometryModel3D(M.TexturedTop(0, 0, (t + 0.01) * Mm, w * Mm, h * Mm), new DiffuseMaterial(new ImageBrush(top))));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
            }

            var parameters = sch.Parts.ToDictionary(p => p.RefDes, p => sch.ResolveParameters(library, p), StringComparer.OrdinalIgnoreCase);
            foreach (var comp in board.Components)
            {
                var fp = FootprintLibrary.Get(comp.FootprintId);
                var part = sch.FindPart(comp.RefDes);
                var def = part != null ? library.Find(part.PartId) : null;
                if (fp == null || def == null) continue;
                parameters.TryGetValue(comp.RefDes, out var p);
                // Component body: footprint courtyard footprint, coloured by kind.
                var c0 = comp.ToBoard(fp.CourtyardMinX, fp.CourtyardMinY);
                var c1 = comp.ToBoard(fp.CourtyardMaxX, fp.CourtyardMaxY);
                double x0 = Math.Min(c0.X, c1.X) - w / 2, x1 = Math.Max(c0.X, c1.X) - w / 2;
                double y0 = h / 2 - Math.Max(c0.Y, c1.Y), y1 = h / 2 - Math.Min(c0.Y, c1.Y);
                double height = Math.Max(1.0, Math.Min(fp.Height, 25));
                var (color, inset) = BodyColor(def, p);
                Box(m.Body, (x0 + x1) / 2, (y0 + y1) / 2, t + height / 2, Math.Max(1, x1 - x0 - inset), Math.Max(1, y1 - y0 - inset), height, color, 0.15);
                if (def.Symbol == SymbolKind.Led)
                {
                    var glow = new EmissiveMaterial(new SolidColorBrush(Colors.Black));
                    var lens = new MaterialGroup();
                    lens.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0xC0, color.R, color.G, color.B))));
                    lens.Children.Add(glow);
                    m.Body.Children.Add(M.Model(M.Sphere(2.5 * Mm, (x0 + x1) / 2 * Mm, (y0 + y1) / 2 * Mm, (t + height + 1) * Mm, 12), lens));
                    m.Lights[comp.RefDes] = glow;
                }

                // Cables plug into connectors and board headers.
                if (def.Symbol == SymbolKind.Connector || def.Symbol == SymbolKind.Board)
                {
                    foreach (var pin in def.Pins)
                    {
                        string padName = p != null ? def.ResolvePad(pin, p) : pin.Pad;
                        var pad = fp.FindPad(padName);
                        if (pad == null) continue;
                        var bp = comp.ToBoard(pad.X, pad.Y);
                        double px = bp.X - w / 2, py = h / 2 - bp.Y;
                        Box(m.Body, px, py, t + height + 3, 0.64, 0.64, 6, Color.FromRgb(0xD9, 0xB0, 0x4C), 0.7);
                        m.Pins.Add(new PinAnchor(comp.RefDes + "." + pin.Name, $"{comp.RefDes}.{pin.Name}", new Point3D(px * Mm, py * Mm, (t + height + 6) * Mm)));
                    }
                }
            }

            m.Bounds = new Rect3D(-w / 2 * Mm, -h / 2 * Mm, 0, w * Mm, h * Mm, (t + 12) * Mm);
            return m;
        }

        private static (Color Color, double Inset) BodyColor(PartDefinition def, IReadOnlyDictionary<string, string>? p)
        {
            string color = p != null && p.TryGetValue("color", out var c) ? c.ToLowerInvariant() : string.Empty;
            return def.Symbol switch
            {
                SymbolKind.Resistor => (Color.FromRgb(0xC9, 0xB0, 0x86), 1.2),
                SymbolKind.Led => (color switch
                {
                    "green" or "yeşil" => Color.FromRgb(0x3C, 0xD0, 0x64),
                    "blue" or "mavi" => Color.FromRgb(0x46, 0x7C, 0xF0),
                    "yellow" or "sarı" => Color.FromRgb(0xF0, 0xD0, 0x40),
                    "white" or "beyaz" => Color.FromRgb(0xF0, 0xF0, 0xF0),
                    _ => Color.FromRgb(0xE5, 0x48, 0x4D)
                }, 1.0),
                SymbolKind.Connector => (Color.FromRgb(0x1C, 0x1C, 0x1F), 0.2),
                SymbolKind.Board => (Color.FromRgb(0x1D, 0x5E, 0x9E), 0),
                _ => def.Category.Contains("Kondansatör", StringComparison.OrdinalIgnoreCase) || def.Id.Contains("capacitor", StringComparison.OrdinalIgnoreCase)
                    ? (Color.FromRgb(0x2E, 0x5D, 0xA8), 0.6)
                    : (Color.FromRgb(0x22, 0x22, 0x26), 0.4)
            };
        }

        // ================================================================ free-standing components

        private static ObjectModel BuildComponent(WorldObject o, PartLibrary library)
        {
            var m = new ObjectModel();
            var def = o.PartId != null ? library.Find(o.PartId) : null;
            if (def == null)
            {
                Box(m.Body, 0, 0, 5, 20, 20, 10, Colors.Magenta);
                return m;
            }

            var preset = o.PresetId != null ? library.FindPreset(o.PresetId) : null;
            var p = library.ResolveParameters(def, preset, o.Parameters);
            string model = (p.TryGetValue("model", out var mdl) ? mdl : string.Empty).ToUpperInvariant();
            switch (def.Id)
            {
                case "servo":
                    Servo(m, model);
                    break;
                case "dc-motor":
                    Motor(m, model, p);
                    break;
                case "battery":
                    Battery(m, o.PresetId ?? string.Empty, p);
                    break;
                default:
                    if (!RadioModels.TryBuild(m, def, p)) Generic(m, def, p);   // [radio] World/RadioModels.cs
                    break;
            }

            return m;
        }

        /// <summary>SG90-class micro servo (22.8 × 12.2 × 22.7 mm), output shaft offset toward one end; MG996R scaled up.</summary>
        private static void Servo(ObjectModel m, string model)
        {
            bool big = model.Contains("996") || model.Contains("DS32");
            double k = big ? 40.7 / 22.8 : 1.0;
            double L = 22.8 * k, W = 12.2 * k, H = 22.7 * k;
            var blue = big ? Color.FromRgb(0x1A, 0x1A, 0x1D) : Color.FromRgb(0x2E, 0x6F, 0xD8);
            Box(m.Body, 0, 0, H / 2, L, W, H, blue, 0.2, m.Heat);
            Box(m.Body, 0, 0, H * 0.72, L + 9.6 * k, W, 2.5 * k, blue, 0.2);            // mounting tabs
            double sx = -L / 2 + 6 * k;                                                     // output shaft position
            Cyl(m.Body, 5.9 * k, 4 * k, 2, sx, 0, H + 2 * k, blue, 0.2);                   // gear tower
            Cyl(m.Body, 2.4 * k, 3 * k, 2, sx, 0, H + 5.5 * k, Color.FromRgb(0xF2, 0xF2, 0xF2), 0.4, null, 16);
            // Horn (moves): white single arm.
            var horn = new Model3DGroup();
            double hz = H + 7.5 * k;
            Cyl(horn, 3.5 * k, 1.6 * k, 2, sx, 0, hz, Colors.White, 0.3, null, 20);
            Box(horn, sx + 8 * k, 0, hz, 16 * k, 5 * k, 1.6 * k, Colors.White, 0.3);
            m.Moving = horn;
            m.Port = new PortFrame("horn", "Servo kolu", new Point3D(sx * Mm, 0, (hz + 0.8 * k) * Mm), new Vector3D(0, 0, 1));
            // Pigtail: brown (GND), red (V+), orange (SIG) ending in a 3-pin female connector.
            double cx = L / 2 + 40, cz = 3;
            Box(m.Body, L / 2 + 20, 0, cz, 40, 3.6, 1.2, Color.FromRgb(0x70, 0x50, 0x30), 0.05);
            Box(m.Body, cx, 0, cz, 7.6, 2.6, 14, Color.FromRgb(0x1A, 0x1A, 0x1D), 0.1);
            m.Pins.Add(new PinAnchor("GND", "GND (kahverengi)", new Point3D((cx + 4) * Mm, -2.54 * Mm, cz * Mm)));
            m.Pins.Add(new PinAnchor("V+", "V+ (kırmızı)", new Point3D((cx + 4) * Mm, 0, cz * Mm)));
            m.Pins.Add(new PinAnchor("SIG", "SIG (turuncu)", new Point3D((cx + 4) * Mm, 2.54 * Mm, cz * Mm)));
            m.Bounds = new Rect3D(-L / 2 * Mm, -W / 2 * Mm, 0, (L + 50) * Mm, W * Mm, (hz + 2) * Mm);
            m.CollisionBounds = new Rect3D(-L / 2 * Mm, -W / 2 * Mm, 0, L * Mm, W * Mm, (H + 4 * k) * Mm);
        }

        /// <summary>Yellow TT gear motor (70 × 22.5 × 18.8 mm, shaft across), or a round can motor.</summary>
        private static void Motor(ObjectModel m, string model, IReadOnlyDictionary<string, string> p)
        {
            bool coreless = model.Contains("8520") || model.Contains("0720");
            bool tt = model.Contains("TT") || (!coreless && !model.Contains("25GA") && !model.Contains("JGA") && !model.Contains("N20") && !model.Contains("130"));
            var shaft = new Model3DGroup();
            if (tt)
            {
                var yellow = Color.FromRgb(0xF2, 0xC2, 0x1B);
                Box(m.Body, 0, 0, 9.4, 37, 22.5, 18.8, yellow, 0.15, m.Heat);
                Cyl(m.Body, 10, 28, 0, 32, 0, 10, Color.FromRgb(0xC8, 0xCA, 0xCF), 0.6, m.Heat);   // motor can
                Cyl(shaft, 2.7, 37, 1, -11, 0, 9.4, Colors.White, 0.3, null, 12);                    // double D-shaft
                m.Port = new PortFrame("shaft", "Motor mili", new Point3D(-11 * Mm, 18.5 * Mm, 9.4 * Mm), new Vector3D(0, 1, 0));
                m.Pins.Add(new PinAnchor("A", "A (kırmızı)", new Point3D(47 * Mm, -4 * Mm, 14 * Mm)));
                m.Pins.Add(new PinAnchor("B", "B (siyah)", new Point3D(47 * Mm, 4 * Mm, 14 * Mm)));
                m.Bounds = new Rect3D(-18.5 * Mm, -18.5 * Mm, 0, 64.5 * Mm, 37 * Mm, 20 * Mm);
            }
            else
            {
                double r = coreless ? 4.25 : model.Contains("N20") ? 6 : model.Contains("130") ? 10 : 12.5;
                double len = coreless ? 20 : model.Contains("N20") ? 24 : model.Contains("130") ? 25 : 50;
                Cyl(m.Body, r, len, 0, 0, 0, r, Color.FromRgb(0xC0, 0xC3, 0xCA), 0.6, m.Heat);
                Cyl(shaft, coreless ? 0.5 : 2, coreless ? 6 : 12, 0, len / 2 + (coreless ? 3 : 6), 0, r, Colors.White, 0.4, null, 10);
                m.Port = new PortFrame("shaft", "Motor mili", new Point3D((len / 2 + (coreless ? 6 : 12)) * Mm, 0, r * Mm), new Vector3D(1, 0, 0));
                m.Pins.Add(new PinAnchor("A", "A", new Point3D((-len / 2 - 2) * Mm, -2 * Mm, r * Mm)));
                m.Pins.Add(new PinAnchor("B", "B", new Point3D((-len / 2 - 2) * Mm, 2 * Mm, r * Mm)));
                m.Bounds = new Rect3D(-len / 2 * Mm, -r * Mm, 0, (len + 12) * Mm, 2 * r * Mm, 2 * r * Mm);
            }

            m.Moving = shaft;
        }

        private static void Battery(ObjectModel m, string preset, IReadOnlyDictionary<string, string> p)
        {
            var red = Color.FromRgb(0xC4, 0x2B, 0x1C);
            if (preset.Contains("9v"))
            {
                Box(m.Body, 0, 0, 8.75, 48.5, 26.5, 17.5, Color.FromRgb(0x1D, 0x1D, 0x20), 0.15, m.Heat);
                Cyl(m.Body, 4, 3, 0, 25.7, -6.3, 8.75, Color.FromRgb(0xC8, 0xCA, 0xCF), 0.8);
                Cyl(m.Body, 4, 3, 0, 25.7, 6.3, 8.75, Color.FromRgb(0xC8, 0xCA, 0xCF), 0.8);
                Leads(m, 27, 8.75);
            }
            else if (preset.Contains("18650"))
            {
                Cyl(m.Body, 9.25, 65, 0, 0, 0, 9.25, Color.FromRgb(0x2E, 0x8B, 0x57), 0.35, m.Heat);
                Leads(m, 33, 9.25);
            }
            else if (preset.Contains("lipo"))
            {
                bool small = preset.Contains("300");
                double l = small ? 45 : 75, w = small ? 25 : 35, h = small ? 7 : preset.Contains("3s") ? 25 : 17;
                Box(m.Body, 0, 0, h / 2, l, w, h, Color.FromRgb(0x30, 0x30, 0x36), 0.25, m.Heat);
                Box(m.Body, 0, 0, h + 0.2, l * 0.8, w * 0.8, 0.4, Color.FromRgb(0x3E, 0x7C, 0xC8), 0.1);
                Leads(m, l / 2 + 2, h / 2);
            }
            else
            {
                int cells = preset.Contains("4") ? 4 : preset.Contains("2") ? 2 : 1;
                double cw = 15 * cells;
                Box(m.Body, 0, 0, 7.5, 58, cw, 15, Color.FromRgb(0x1D, 0x1D, 0x20), 0.1, m.Heat);
                for (int i = 0; i < cells; i++) Cyl(m.Body, 7, 50, 0, 0, (i - (cells - 1) / 2.0) * 15, 9, Color.FromRgb(0x2F, 0x5F, 0xC8), 0.3);
                Leads(m, 31, 7.5);
            }
        }

        private static void Leads(ObjectModel m, double x, double z)
        {
            m.CollisionBounds = m.Body.Bounds;
            Box(m.Body, x + 15, -2, z, 30, 1.4, 1.4, Color.FromRgb(0xC4, 0x2B, 0x1C), 0.05);
            Box(m.Body, x + 15, 2, z, 30, 1.4, 1.4, Color.FromRgb(0x1A, 0x1A, 0x1D), 0.05);
            m.Pins.Add(new PinAnchor("+", "+ (kırmızı)", new Point3D((x + 30) * Mm, -2 * Mm, z * Mm)));
            m.Pins.Add(new PinAnchor("-", "− (siyah)", new Point3D((x + 30) * Mm, 2 * Mm, z * Mm)));
        }

        /// <summary>Any other part: a module/board/body sized from its footprint, with a header pin per electrical pin.</summary>
        private static void Generic(ObjectModel m, PartDefinition def, IReadOnlyDictionary<string, string> p)
        {
            var fp = !string.IsNullOrEmpty(def.Footprint) ? FootprintLibrary.Get(p.TryGetValue("footprint", out var f) && !string.IsNullOrEmpty(f) ? f : def.Footprint) : null;
            double x0 = -10, x1 = 10, y0 = -8, y1 = 8, height = 8;
            if (fp != null)
            {
                x0 = Math.Min(fp.CourtyardMinX, -3);
                x1 = Math.Max(fp.CourtyardMaxX, 3);
                y0 = Math.Min(fp.CourtyardMinY, -3);
                y1 = Math.Max(fp.CourtyardMaxY, 3);
                height = Math.Max(2, Math.Min(fp.Height, 30));
            }

            // Breakout modules: real board sizes (their footprint is only the pin header).
            (double W, double H, bool Row)? module = def.SymbolVariant switch
            {
                "OLED" => (27.3, p.TryGetValue("height", out var oh) && oh == "32" ? 12.0 : 27.8, true),
                "LCD" => (p.TryGetValue("columns", out var cols) && cols == "20" ? 98.0 : 80.0, p.TryGetValue("rows", out var rows) && rows == "4" ? 60.0 : 36.0, false),
                _ => def.Id switch
                {
                    "mpu6050" => (21.0, 16.0, true),
                    "hc-sr04" => (45.0, 20.0, true),
                    _ => ((double, double, bool)?)null
                }
            };
            if (module is { } mod)
            {
                x0 = -mod.W / 2;
                x1 = mod.W / 2;
                y0 = -mod.H / 2;
                y1 = mod.H / 2;
                fp = null;   // pins are laid out on the module's own header below
            }

            bool pcb = def.Symbol == SymbolKind.Board || def.Symbol == SymbolKind.Module || def.Category.Contains("Sensör", StringComparison.OrdinalIgnoreCase) ||
                       def.Category.Contains("Ekran", StringComparison.OrdinalIgnoreCase);
            double cx = (x0 + x1) / 2, cy = -(y0 + y1) / 2, sx = x1 - x0, sy = y1 - y0;
            if (pcb)
            {
                var board = def.Id.StartsWith("esp32", StringComparison.OrdinalIgnoreCase) ? Color.FromRgb(0x1A, 0x1A, 0x1D)
                    : def.Symbol == SymbolKind.Board ? Color.FromRgb(0x1D, 0x5E, 0x9E) : Color.FromRgb(0x1E, 0x56, 0x31);
                Box(m.Body, cx, cy, 0.8, sx, sy, 1.6, board, 0.1, m.Heat);
                if (def.SymbolVariant == "OLED" || def.SymbolVariant == "LCD")
                {
                    bool lcd = def.SymbolVariant == "LCD";
                    // OLED: glass panel with the active area offset from the header; LCD: metal bezel + glass.
                    double gw = lcd ? sx * 0.9 : sx * 0.98, gh = lcd ? sy * 0.68 : sy * 0.72, gy = lcd ? cy : cy - sy * 0.06;
                    Box(m.Body, cx, gy, lcd ? 4.6 : 2.4, gw, gh, lcd ? 6 : 1.6, lcd ? Color.FromRgb(0x14, 0x14, 0x16) : Color.FromRgb(0x08, 0x08, 0x0A), 0.6);
                    var brush = new ImageBrush { Stretch = Stretch.Fill };
                    var screen = new MaterialGroup();
                    screen.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x04, 0x04, 0x06))));
                    screen.Children.Add(new EmissiveMaterial(brush));
                    double aw = lcd ? sx * 0.81 : sx * 0.8, ah = lcd ? sy * 0.45 : sy * 0.4;
                    m.Body.Children.Add(new GeometryModel3D(M.TexturedTop(cx * Mm, gy * Mm, (lcd ? 7.65 : 3.25) * Mm, aw * Mm, ah * Mm), screen));
                    m.Screen = brush;
                    m.ScreenStyle = lcd ? (p.TryGetValue("backlight", out var bl) ? bl : "blue") : (p.TryGetValue("color", out var col) ? col : "white");
                }
                else if (def.Id == "hc-sr04")
                {
                    Cyl(m.Body, 8, 12, 2, cx - 13, cy, 7.6, Color.FromRgb(0xC8, 0xCA, 0xCF), 0.6);
                    Cyl(m.Body, 8, 12, 2, cx + 13, cy, 7.6, Color.FromRgb(0xC8, 0xCA, 0xCF), 0.6);
                }
                else
                {
                    Box(m.Body, cx, cy, 1.6 + 1.2, Math.Min(10, sx * 0.4), Math.Min(10, sy * 0.4), 2.4, Color.FromRgb(0x18, 0x18, 0x1B), 0.3);
                }
            }
            else
            {
                Box(m.Body, cx, cy, height / 2, sx, sy, height, Color.FromRgb(0x2A, 0x2A, 0x2F), 0.15, m.Heat);
            }

            double top = pcb ? 1.6 : height;
            int i = 0;
            foreach (var pin in def.Pins)
            {
                double px, py;
                var pad = fp?.FindPad(def.ResolvePad(pin, p));
                if (pad != null)
                {
                    px = pad.X;
                    py = -pad.Y;
                }
                else if (module is { } mm && !mm.Row)
                {
                    // LCD backpack: header on the left edge, vertical.
                    px = x0 + 2.5;
                    py = (i - (def.Pins.Count - 1) / 2.0) * 2.54;
                }
                else if (module is { } mr)
                {
                    // Header row along the top edge.
                    px = (i - (def.Pins.Count - 1) / 2.0) * 2.54;
                    py = mr.H / 2 - 1.8;
                }
                else
                {
                    px = x0 + 2 + (i * 2.54) % Math.Max(2.54, sx - 4);
                    py = -(y0 + 1.5);
                }

                Box(m.Body, px, py, top + 3, 0.64, 0.64, 6, Color.FromRgb(0xD9, 0xB0, 0x4C), 0.7);
                m.Pins.Add(new PinAnchor(pin.Name, pin.Name, new Point3D(px * Mm, py * Mm, (top + 6) * Mm)));
                if (def.Symbol == SymbolKind.Led || def.Symbol == SymbolKind.Lamp)
                {
                    var glow = new EmissiveMaterial(new SolidColorBrush(Colors.Black));
                    var g = new MaterialGroup();
                    g.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0xD0, 0xE5, 0x48, 0x4D))));
                    g.Children.Add(glow);
                    if (i == 0) m.Body.Children.Add(M.Model(M.Sphere(3 * Mm, cx * Mm, cy * Mm, (height + 3) * Mm, 14), g));
                    if (i == 0) m.Lights[""] = glow;
                }

                i++;
            }

            m.Bounds = new Rect3D(x0 * Mm, -y1 * Mm, 0, sx * Mm, sy * Mm, (top + 8) * Mm);
        }
    }
}
