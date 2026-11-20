using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox.Radio
{
    /// <summary>
    /// Builds the radio environment of a world: an ESP32 node per ESP32 board, controllers,
    /// routers, beacons, IR remotes / IR LEDs / IR receivers, and the world's obstacles and
    /// bodies as attenuators. Positions are read live from the objects' poses.
    /// </summary>
    public static class WorldRadio
    {
        private static readonly byte[] EspressifOui = { 0x24, 0x0A, 0xC4 };
        private static readonly byte[] SonyOui = { 0xA0, 0xAB, 0x51 };
        private static readonly byte[] MicrosoftOui = { 0x98, 0x7A, 0x14 };
        private static readonly byte[] RouterOui = { 0x5C, 0x49, 0x79 };

        /// <summary>Deterministic MAC for an object (vendor prefix + hash of the id); the last byte is a multiple of 4 so ESP32 +1/+2 stay inside.</summary>
        public static byte[] MacFor(string objectId, byte[] oui)
        {
            ulong h = RadioRandom.Hash("mac:" + objectId);
            return new[] { oui[0], oui[1], oui[2], (byte)(h >> 8), (byte)(h >> 16), (byte)((h >> 24) & 0xFC) };
        }

        public static bool IsEsp32(PartDefinition def) => def.Id.StartsWith("esp32", StringComparison.OrdinalIgnoreCase);

        private static double Num(IReadOnlyDictionary<string, string> p, string key, double fallback) =>
            p.TryGetValue(key, out var v) ? SiValue.Parse(v, fallback) : fallback;

        private static string Text(IReadOnlyDictionary<string, string> p, string key, string fallback = "") =>
            p.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

        /// <summary>Antenna/aperture position of an object: its pose plus a small height.</summary>
        public static Func<Vec3> PositionOf(WorldObject o, double up = 0.01) => () => o.Pose.Position + new Vec3(0, 0, up);

        /// <summary>A local direction of an object in world coordinates.</summary>
        public static Func<Vec3> Axis(WorldObject o, Vec3 local) => () => o.Pose.Orientation.Rotate(local).Normalized();

        /// <summary>
        /// Builds the medium. <paramref name="simulator"/> (optional) lets IR LEDs sample their
        /// drive; <paramref name="position"/> overrides object positions (live 3D poses).
        /// </summary>
        public static RadioMedium Build(WorldDocument world, WorldCircuit? circuit, PartLibrary library, Func<WorldObject, Vec3>? position = null)
        {
            var m = new RadioMedium();
            m.Propagation.Seed = RadioRandom.Hash("world:" + world.Title) ^ 0x5EED;
            Func<Vec3> Pos(WorldObject o, double up) => position == null ? PositionOf(o, up) : () => position(o) + new Vec3(0, 0, up);

            // ---- environment: obstacles (wood/plaster ≈ 5 dB) and bodies (metal ≈ 20 dB)
            var env = world.Environment;
            if (env.Walls)
            {
                m.Propagation.ArenaHalfWidth = Math.Max(0.25, env.Width / 2);
                m.Propagation.ArenaHalfDepth = Math.Max(0.25, env.Depth / 2);
            }

            foreach (var ob in env.Obstacles)
            {
                m.Propagation.Obstacles.Add(new RadioObstacle(new Vec3(ob.X, ob.Y, ob.Height / 2), new Vec3(ob.SizeX / 2, ob.SizeY / 2, ob.Height / 2), 0, 5.0, true, "engel"));
            }

            foreach (var o in world.Objects.Where(o => o.Kind == WorldObjectKind.Body))
            {
                string mat = (o.Material ?? string.Empty).ToUpperInvariant();
                double loss = mat.Contains("ÇELİK") || mat.Contains("CELIK") || mat.Contains("ALÜMİNYUM") || mat.Contains("ALUMINYUM") ? 20.0
                    : mat.Contains("KARBON") ? 12.0 : mat.Contains("AHŞAP") || mat.Contains("MDF") ? 4.0 : 2.0;
                double sx = o.SizeX, sy = o.Shape == "wheel" || o.Shape == "cylinder" || o.Shape == "sphere" ? o.SizeX : o.SizeY;
                double sz = o.Shape == "wheel" || o.Shape == "sphere" ? o.SizeX : o.SizeZ;
                if (Math.Max(sx, Math.Max(sy, sz)) < 0.03) continue;   // small parts do not shadow 12 cm waves
                m.Propagation.Obstacles.Add(new RadioObstacle(o.Pose.Position + new Vec3(0, 0, sz / 2), new Vec3(sx / 2, sy / 2, sz / 2), o.Pose.Yaw * Math.PI / 180, loss, true, o.Name));
            }

            // ---- ESP32 boards (free-standing or on designed boards)
            if (circuit != null)
            {
                foreach (var prog in circuit.Programmables.Where(p => IsEsp32(p.Definition)))
                {
                    var o = world.Find(prog.ObjectId);
                    if (o == null) continue;
                    string id = prog.ObjectId + (o.Kind == WorldObjectKind.Board ? "/" + prog.InnerRefDes : string.Empty);
                    var node = m.Add(new Esp32RadioNode(id, o.Name + (o.Kind == WorldObjectKind.Board ? " " + prog.InnerRefDes : string.Empty), MacFor(id, EspressifOui)));
                    node.Position = Pos(o, 0.012);
                    node.IsOn = () => false;   // switched on when its co-simulation attaches
                }
            }

            foreach (var o in world.Objects.Where(o => o.Kind == WorldObjectKind.Component && o.PartId != null))
            {
                var def = library.Find(o.PartId!);
                if (def == null) continue;
                var preset = o.PresetId != null ? library.FindPreset(o.PresetId) : null;
                var p = library.ResolveParameters(def, preset, o.Parameters);
                switch (def.Id)
                {
                    case WirelessParts.GamepadId:
                    {
                        string model = Text(p, "model", "DualSense");
                        var kind = model.Contains("DualShock") ? GamepadModel.DualShock4 : model.Contains("Xbox") ? GamepadModel.Xbox : GamepadModel.DualSense;
                        var pad = m.Add(new GamepadNode(o.Id, o.Name, MacFor(o.Id, kind == GamepadModel.Xbox ? MicrosoftOui : SonyOui), kind));
                        pad.Position = Pos(o, 0.02);
                        pad.TxPowerDbm = Num(p, "txpower", 4.0);
                        pad.PoweredOn = !string.Equals(Text(p, "power", "true"), "false", StringComparison.OrdinalIgnoreCase);
                        string paired = Text(p, "pairedWith");
                        if (paired.Length > 0)
                        {
                            var host = m.Nodes.OfType<Esp32RadioNode>().FirstOrDefault(n => string.Equals(n.Id, paired, StringComparison.OrdinalIgnoreCase) || n.Id.StartsWith(paired + "/", StringComparison.OrdinalIgnoreCase));
                            if (host != null) pad.BondedHost = host.BluetoothMacText;
                        }

                        break;
                    }

                    case WirelessParts.RouterId:
                    {
                        var net = new WifiNetwork
                        {
                            Ssid = Text(p, "ssid", "RoboTwin-Ev"),
                            Password = p.TryGetValue("password", out var pw) ? pw : string.Empty,
                            Channel = (int)Num(p, "channel", 6),
                            Gateway = RadioMedium.Ip(192, 168, 1, 1),
                            NextHost = 100
                        };
                        var ap = m.Add(new AccessPointNode(o.Id, o.Name, MacFor(o.Id, RouterOui), net));
                        ap.Position = Pos(o, 0.12);
                        ap.TxPowerDbm = Num(p, "txpower", 20.0);
                        break;
                    }

                    case WirelessParts.BeaconId:
                    {
                        var b = m.Add(new BeaconNode(o.Id, o.Name, MacFor(o.Id, new byte[] { 0xC0, 0x4B, 0x24 })));
                        b.Position = Pos(o, 0.005);
                        b.AdvName = Text(p, "name", "RW-Beacon");
                        b.AdvIntervalS = Math.Max(0.02, Num(p, "interval", 0.1));
                        b.TxPowerDbm = Num(p, "txpower", 0.0);
                        break;
                    }

                    case WirelessParts.IrRemoteId:
                    {
                        var e = new IrEmitter(o.Id, o.Name) { Position = Pos(o, 0.008), Direction = Axis(o, Vec3.UnitX), HalfAngleDeg = 30, RangeM = Num(p, "range", 8.0) };
                        m.Ir.Emitters.Add(e);
                        break;
                    }
                }
            }

            if (circuit != null) AddIrParts(m, world, circuit, library, Pos);
            m.ImplicitNetworkEnabled = !m.Nodes.OfType<AccessPointNode>().Any();
            return m;
        }

        private static void AddIrParts(RadioMedium m, WorldDocument world, WorldCircuit circuit, PartLibrary library, Func<WorldObject, double, Func<Vec3>> pos)
        {
            var resistors = circuit.Compiled.Circuit.Devices.OfType<Resistor>().ToList();
            var pins = circuit.Compiled.Circuit.Devices.OfType<DigitalPin>().ToList();
            foreach (var o in world.Objects)
            {
                foreach (var (fullRef, def, parameters) in PartsOf(world, o, circuit, library))
                {
                    if (def.Id == WirelessParts.IrReceiverId)
                    {
                        var output = circuit.Compiled.Device<DigitalPin>(fullRef + ".OUT");
                        var vs = circuit.Compiled.PinNode(fullRef, "VS");
                        var gnd = circuit.Compiled.PinNode(fullRef, "GND");
                        var r = new IrReceiver(fullRef, o.Name) { Position = pos(o, 0.006), Facing = Axis(o, Vec3.UnitX), Output = output };
                        r.SupplyNode = vs;
                        r.GroundNode = gnd;
                        m.Ir.Receivers.Add(r);
                    }
                    else if (def.Id == "led" && string.Equals(Text(parameters, "color"), "ir", StringComparison.OrdinalIgnoreCase))
                    {
                        var led = circuit.Compiled.DevicesOf(fullRef).OfType<Led>().FirstOrDefault();
                        if (led == null) continue;
                        var driver = FindDriver(led.Anode, resistors, pins);
                        double peak = 0;   // peak detector: the LED current is pulsed by the carrier
                        var e = new IrEmitter(fullRef, o.Name + " IR LED")
                        {
                            Position = pos(o, 0.008),
                            Direction = Axis(o, Vec3.UnitZ),
                            HalfAngleDeg = 20,
                            RangeM = 6.0,
                            Drive = () => Math.Max(0.05, peak / Math.Max(1e-3, led.RatedCurrent)),
                            LiveCarrier = () =>
                            {
                                peak = Math.Max(Math.Abs(led.Current), peak * 0.97);
                                if (led.IsFailed) return false;
                                if (driver != null)
                                {
                                    // A TSOP only responds to the 30–60 kHz carrier, not to a steady LED.
                                    return driver.PwmActive && driver.PwmDuty > 0.02 && driver.PwmFrequency >= 30e3 && driver.PwmFrequency <= 60e3;
                                }

                                return led.Current > 1e-3;
                            }
                        };
                        m.Ir.Emitters.Add(e);
                    }
                }
            }
        }

        /// <summary>A GPIO driving a node directly or through up to three resistive hops (cables, series resistor).</summary>
        private static DigitalPin? FindDriver(int node, List<Resistor> resistors, List<DigitalPin> pins)
        {
            var seen = new HashSet<int> { node };
            var frontier = new List<int> { node };
            for (int depth = 0; depth < 4 && frontier.Count > 0; depth++)
            {
                foreach (var n in frontier)
                {
                    var pin = pins.FirstOrDefault(p => p.Pin == n);
                    if (pin != null) return pin;
                }

                var next = new List<int>();
                foreach (var n in frontier)
                {
                    foreach (var r in resistors)
                    {
                        if (r.Resistance > 20e3) continue;   // pull-ups/downs are not drive paths
                        if (r.A == n && seen.Add(r.B)) next.Add(r.B);
                        if (r.B == n && seen.Add(r.A)) next.Add(r.A);
                    }
                }

                frontier = next;
            }

            return null;
        }

        private static IEnumerable<(string FullRef, PartDefinition Def, IReadOnlyDictionary<string, string> Parameters)> PartsOf(WorldDocument world, WorldObject o, WorldCircuit circuit, PartLibrary library)
        {
            if (o.Kind == WorldObjectKind.Board)
            {
                var sch = world.FindDesign(o.DesignId)?.Package.Schematic;
                if (sch == null) yield break;
                foreach (var p in sch.Parts)
                {
                    var def = library.Find(p.PartId);
                    if (def != null) yield return (WorldCircuit.Prefix(o.Id) + p.RefDes, def, sch.ResolveParameters(library, p));
                }
            }
            else if (o.Kind == WorldObjectKind.Component && o.PartId != null)
            {
                var def = library.Find(o.PartId);
                var r = circuit.ComponentRef(o.Id);
                if (def == null || r == null) yield break;
                var preset = o.PresetId != null ? library.FindPreset(o.PresetId) : null;
                yield return (r, def, library.ResolveParameters(def, preset, o.Parameters));
            }
        }

        /// <summary>The radio node of an ESP32 programmable (null when not an ESP32).</summary>
        public static Esp32RadioNode? NodeOf(RadioMedium medium, WorldProgrammable prog, WorldDocument world)
        {
            var o = world.Find(prog.ObjectId);
            if (o == null) return null;
            string id = prog.ObjectId + (o.Kind == WorldObjectKind.Board ? "/" + prog.InnerRefDes : string.Empty);
            return medium.Find(id) as Esp32RadioNode;
        }

        /// <summary>The 21-key NEC remote found in Arduino kits: label → command.</summary>
        public static IReadOnlyList<(string Label, byte Command)> RemoteKeys { get; } = new List<(string, byte)>
        {
            ("CH−", 0x45), ("CH", 0x46), ("CH+", 0x47),
            ("⏮", 0x44), ("⏭", 0x40), ("⏯", 0x43),
            ("VOL−", 0x07), ("VOL+", 0x15), ("EQ", 0x09),
            ("0", 0x16), ("100+", 0x19), ("200+", 0x0D),
            ("1", 0x0C), ("2", 0x18), ("3", 0x5E),
            ("4", 0x08), ("5", 0x1C), ("6", 0x5A),
            ("7", 0x42), ("8", 0x52), ("9", 0x4A)
        };

        public static byte ParseByte(string? text, byte fallback = 0)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            string t = text!.Trim();
            try
            {
                return t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToByte(t.Substring(2), 16) : byte.Parse(t, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException)
            {
                return fallback;
            }
        }
    }
}
