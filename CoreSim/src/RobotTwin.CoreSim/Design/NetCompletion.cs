using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>[problems] A suggested way to finish a net that has one pin only: what it does (Turkish) and the edit.</summary>
    public sealed class NetCompletion
    {
        internal NetCompletion(string text, Action<Schematic> apply)
        {
            Text = text;
            _apply = apply;
        }

        private readonly Action<Schematic> _apply;

        /// <summary>What the fix does, for its tooltip.</summary>
        public string Text { get; }

        /// <summary>Makes the edit (one undo step in the editor).</summary>
        public void ApplyTo(Schematic doc) => _apply(doc);
    }

    /// <summary>
    /// [problems] "Tek pinli hattı tamamlama önerisi": a labelled net with one pin is mostly a label written another way than
    /// the net it was meant for, or a label whose other end was never put on the pin of the same name. Two suggestions,
    /// each only when there is exactly one answer: (1) another net whose name is the same but for case and separators
    /// ("MOTOR_EN" / "MOTOR-EN", "Sda " / "SDA") — the labels take its name; (2) a free pin on the same sheet named as the
    /// label ("SDA", or one of a pin's names: "A4/SDA") — a label of that name goes on its tip. Null when neither holds.
    /// </summary>
    public static class NetCompletions
    {
        public static NetCompletion? For(Schematic doc, PartLibrary library, ErcViolation v)
        {
            if (v.Code is not ("single-pin-net" or "label-single") || string.IsNullOrWhiteSpace(v.Net)) return null;
            var netlist = doc.BuildNetlist(library);
            string net = v.Net!;

            // the net's own labels (a net without one was drawn by wire alone: nothing to name)
            var mine = new List<int>();
            for (int i = 0; i < doc.Labels.Count && i < netlist.LabelNets.Count; i++)
                if (!doc.Labels[i].IsDirective && string.Equals(netlist.LabelNets[i], net, StringComparison.OrdinalIgnoreCase)) mine.Add(i);
            if (mine.Count == 0) return null;
            string name = doc.Labels[mine[0]].Name.Trim();
            string key = Key(name);
            if (key.Length == 0) return null;
            string? sheet = doc.Labels[mine[0]].Sheet;
            var lone = netlist.Nets.FirstOrDefault(n => string.Equals(n.Name, net, StringComparison.OrdinalIgnoreCase));
            var lonePins = new HashSet<string>(lone?.Connections.Select(c => c.RefDes + "." + c.Pin) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            // (1) the same name written another way
            var twins = netlist.Nets.Where(n => !string.Equals(n.Name, net, StringComparison.OrdinalIgnoreCase) && !n.IsAutoNamed && n.Connections.Count > 0 && Key(Last(n.Name)) == key).ToList();
            if (twins.Count == 1)
            {
                string to = Last(twins[0].Name);
                int pins = twins[0].Connections.Count;
                var labels = mine.Select(i => doc.Labels[i]).ToList();
                return new NetCompletion($"Etiketi '{to}' yap: aynı ad başka yazılmış, {pins} pinli '{to}' hattına bağlanır",
                    d =>
                    {
                        foreach (var l in labels) l.Name = to;
                    });
            }

            // (2) a free pin of the label's name on its sheet
            var byPin = new Dictionary<string, Net>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in netlist.Nets)
                foreach (var c in n.Connections)
                    byPin[c.RefDes + "." + c.Pin] = n;
            var free = doc.PinPositions(library)
                .Where(x => Schematic.SameSheet(x.Part.Sheet, sheet) && !lonePins.Contains(x.Part.RefDes + "." + x.Pin.Name))
                .Where(x => x.Pin.Name.Split('/').Any(part => Key(part) == key))
                .Where(x => !byPin.TryGetValue(x.Part.RefDes + "." + x.Pin.Name, out var n) || (n.Connections.Count <= 1 && !n.IsGround))
                .Where(x => !doc.NoConnects.Any(nc => nc.Position.Equals(x.Position) && Schematic.SameSheet(nc.Sheet, sheet)))
                .ToList();
            if (free.Count == 1)
            {
                var (part, pin, at) = free[0];
                return new NetCompletion($"{part.RefDes}.{pin.Name} pinine '{name}' etiketi koy: aynı adlı boştaki pin",
                    d => d.Labels.Add(new NetLabel { Name = name, Position = at, Sheet = part.Sheet }));
            }

            return null;
        }

        /// <summary>A net's own name without its sheet path ("/Motor/EN" → "EN").</summary>
        private static string Last(string net)
        {
            int i = net.LastIndexOf('/');
            return i >= 0 && i < net.Length - 1 ? net.Substring(i + 1) : net;
        }

        /// <summary>A name as it is meant: letters and digits only, upper case.</summary>
        private static string Key(string name) => new string(name.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }
}
