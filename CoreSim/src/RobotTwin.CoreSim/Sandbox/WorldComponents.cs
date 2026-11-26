using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [components] Unity's component checkbox for the components a part carries as one parameter that turns it on
    /// (a shell, a pressure vessel, a parachute, a fault scenario). Switched off, the key is kept as "key.off" with
    /// its value, so the physics, the circuit and every other reader simply do not see it, while its settings stay
    /// for when it is switched back on; an undo restores either state like any other edit.
    /// </summary>
    public static class WorldComponents
    {
        public const string OffSuffix = ".off";

        /// <summary>The keys of the components that can be switched off.</summary>
        public static readonly string[] Switchable = { "shell", "vessel", "parachute", MachineFaults.Key };

        public static bool IsOff(WorldObject o, string key) => o.Parameters.ContainsKey(key + OffSuffix);

        /// <summary>Whether the part carries the component, on or off.</summary>
        public static bool Has(WorldObject o, string key) => o.Parameters.ContainsKey(key) || IsOff(o, key);

        /// <summary>Switches a component on or off; nothing happens for one the part does not carry.</summary>
        public static void SetOn(WorldObject o, string key, bool on)
        {
            if (!Switchable.Contains(key, StringComparer.Ordinal)) throw new ArgumentException("Not a switchable component: " + key, nameof(key));
            string from = on ? key + OffSuffix : key, to = on ? key : key + OffSuffix;
            if (!o.Parameters.TryGetValue(from, out var value)) return;
            o.Parameters.Remove(from);
            o.Parameters[to] = value;
        }

        /// <summary>Takes a component's switched-off copy away too (when the component is removed).</summary>
        public static void ForgetOff(WorldObject o, string key) => o.Parameters.Remove(key + OffSuffix);

        // ================================================================ copy, paste values, paste as new (Unity's ⋮)

        /// <summary>Every parameter that belongs to a component (its switch first).</summary>
        public static IReadOnlyList<string> KeysOf(string key) => key switch
        {
            "shell" => new[] { "shell", "wall", "holes" },
            "vessel" => new[]
            {
                "vessel", "gas", "pressure", "volume", "water", "burst", "nozzle", "nozzle.axis", "nozzle.exit", "valve.at", "close.at", "open.at",
                "grain", "grain.core", "grain.diameter", "grain.length", "grain.segments", "ignite.at", "igniter", "igniter.charge", "igniter.time"
            },
            "parachute" => new[] { "parachute", "parachute.deploy", "parachute.cd" },
            MachineFaults.Key => new[] { MachineFaults.Key, MachineFaults.AtKey, MachineFaults.ValueKey },
            _ => new[] { key }
        };

        /// <summary>The component's values as they are now (switched off included, as "key.off").</summary>
        public static Dictionary<string, string> Copy(WorldObject o, string key)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var k in KeysOf(key))
                if (o.Parameters.TryGetValue(k, out var v)) values[k] = v;
            if (o.Parameters.TryGetValue(key + OffSuffix, out var off)) values[key + OffSuffix] = off;
            return values;
        }

        /// <summary>Whether a part can carry a component with these values (Unity greys "Paste Component As New" otherwise).</summary>
        public static bool CanCarry(WorldObject o, string key, IReadOnlyDictionary<string, string> values) => key switch
        {
            "shell" => o.Kind == WorldObjectKind.Body && o.Shape == "box" && !Has(o, "vessel") && WorldPhysics.LidOf(o) == null,
            "vessel" => o.Kind == WorldObjectKind.Body && o.Shape is "cylinder" or "box" or "sphere" && !Has(o, "shell"),
            "parachute" => o.Kind == WorldObjectKind.Body && !WorldPhysics.IsWing(o),
            MachineFaults.Key => (values.TryGetValue(MachineFaults.Key, out var id) || values.TryGetValue(MachineFaults.Key + OffSuffix, out id))
                                 && MachineFaults.For(o.PartId).Any(f => f.Id == id),
            _ => false
        };

        /// <summary>
        /// Unity's Paste Component Values / Paste Component As New: the component's parameters become exactly the
        /// copied ones (what the copy did not have is taken away, its on / off state comes along).
        /// </summary>
        public static void Paste(WorldObject o, string key, IReadOnlyDictionary<string, string> values)
        {
            foreach (var k in KeysOf(key)) o.Parameters.Remove(k);
            o.Parameters.Remove(key + OffSuffix);
            foreach (var kv in values) o.Parameters[kv.Key] = kv.Value;
        }
    }
}
