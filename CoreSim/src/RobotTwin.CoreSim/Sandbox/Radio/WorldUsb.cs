using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Sandbox.Radio
{
    /// <summary>
    /// USB cables of a world: a "Bilgisayar (USB)" component object whose "target" parameter
    /// names a board object. While plugged it supplies VBUS (5 V) to the board's USB input
    /// and connects the board's UART0 to the serial monitor; unplugged, both are gone.
    /// Boards with no USB cable aimed at them keep their own "usb" parameter (legacy:
    /// powered as if a USB supply were always connected).
    /// </summary>
    public static class WorldUsb
    {
        /// <summary>Every PC/USB object with its target.</summary>
        public static IEnumerable<(WorldObject Host, string TargetId, string? TargetPart, bool Plugged)> Hosts(WorldDocument world)
        {
            foreach (var o in world.Objects)
            {
                if (o.Kind != WorldObjectKind.Component || !string.Equals(o.PartId, WirelessParts.UsbHostId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!o.Parameters.TryGetValue("target", out var target) || string.IsNullOrWhiteSpace(target)) continue;
                o.Parameters.TryGetValue("part", out var part);
                bool plugged = !o.Parameters.TryGetValue("plugged", out var p) || !string.Equals(p.Trim(), "false", StringComparison.OrdinalIgnoreCase);
                yield return (o, target.Trim(), string.IsNullOrWhiteSpace(part) ? null : part!.Trim(), plugged);
            }
        }

        /// <summary>The USB host object aimed at a board (object id + inner designator), or null.</summary>
        public static WorldObject? HostFor(WorldDocument world, string objectId, string? innerRefDes = null)
        {
            foreach (var h in Hosts(world))
            {
                if (!string.Equals(h.TargetId, objectId, StringComparison.OrdinalIgnoreCase)) continue;
                if (innerRefDes != null && h.TargetPart != null && !string.Equals(h.TargetPart, innerRefDes, StringComparison.OrdinalIgnoreCase)) continue;
                return h.Host;
            }

            return null;
        }

        public static bool IsPlugged(WorldObject host) =>
            !host.Parameters.TryGetValue("plugged", out var p) || !string.Equals(p.Trim(), "false", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// World compiler hook: a programmable board part that a USB cable is aimed at gets
        /// usb = "cable" (switched VBUS) and its initial plug state.
        /// </summary>
        public static Dictionary<string, string> Override(WorldDocument world, WorldObject o, string innerRefDes, PartDefinition def, Dictionary<string, string> parameters, PartLibrary library)
        {
            // Boards take power and serial from the cable; [usbc] a USB-C battery charges from it.
            if ((def.Symbol != SymbolKind.Board && def.Symbol != SymbolKind.Battery) || def.Parameters.All(p => p.Key != "usb")) return parameters;
            var host = HostFor(world, o.Id, innerRefDes);
            if (host == null) return parameters;
            if (o.Kind == WorldObjectKind.Board && !host.Parameters.ContainsKey("part"))
            {
                // On a designed board the cable goes to the first programmable part.
                var first = world.FindDesign(o.DesignId)?.Package.Schematic.Parts
                    .FirstOrDefault(p => library.Find(p.PartId) is { Symbol: SymbolKind.Board } d && d.Parameters.Any(x => x.Key == "usb"));
                if (first != null && !string.Equals(first.RefDes, innerRefDes, StringComparison.OrdinalIgnoreCase)) return parameters;
            }

            var copy = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase)
            {
                ["usb"] = "cable",
                ["usbplugged"] = IsPlugged(host) ? "true" : "false"
            };
            return copy;
        }

        /// <summary>Opens/closes a board's USB VBUS switch while simulating. False when the board has no cable-driven USB input.</summary>
        public static bool SetPlugged(WorldCircuit circuit, string objectId, bool plugged)
        {
            bool any = false;
            foreach (var prog in circuit.Programmables.Where(p => string.Equals(p.ObjectId, objectId, StringComparison.OrdinalIgnoreCase)))
            {
                var sw = circuit.Compiled.Device<Switch>(prog.RefDes + ".USBSW");
                if (sw == null) continue;
                sw.Closed = plugged;
                any = true;
            }

            // [usbc] a USB-C battery's charging input
            if (circuit.ComponentRef(objectId) is { } part && circuit.Compiled.Device<Switch>(part + ".USBSW") is { } charger)
            {
                charger.Closed = plugged;
                any = true;
            }

            return any;
        }

        /// <summary>Whether a board's serial port reaches the PC: plugged cable, or no cable at all (legacy worlds).</summary>
        public static bool SerialConnected(WorldDocument world, string objectId)
        {
            var host = HostFor(world, objectId);
            return host == null || IsPlugged(host);
        }
    }
}
