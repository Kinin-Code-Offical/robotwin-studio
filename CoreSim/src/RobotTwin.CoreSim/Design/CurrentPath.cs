using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [path] The way a part's current goes round (D3: a fault's current path on the sheet): from the part out along each of
    /// its nets to the parts in series with it (their current within a factor of <c>share</c> of its own either way: a
    /// branch carrying far more is another loop, one carrying far less a side path) and to the supplies, and on from them,
    /// until the loop closes. The ground net is part of the loop but is not followed (everything returns through it).
    /// </summary>
    public static class CurrentPath
    {
        /// <param name="netlist">The sheet's connections.</param>
        /// <param name="start">The part the path is for (a burnt LED, an overloaded resistor).</param>
        /// <param name="current">A part's current now (A); NaN when it has none to tell.</param>
        /// <param name="isSource">Whether a part is a supply (a battery, a source, a bench supply): on every loop it touches.</param>
        /// <param name="share">How far a series part's current may differ from the start part's (0.1: a tenth to ten times).</param>
        public static (HashSet<string> Parts, HashSet<string> Nets) Trace(Netlist netlist, string start, Func<string, double> current, Func<string, bool>? isSource = null, double share = 0.1)
        {
            var parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };
            var nets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            double i0 = Math.Abs(current(start));
            double least = double.IsNaN(i0) ? 1e-3 : Math.Max(i0 * share, 1e-4);
            double most = double.IsNaN(i0) || i0 <= 0 ? double.PositiveInfinity : Math.Max(i0 / share, least);
            var byPart = new Dictionary<string, List<Net>>(StringComparer.OrdinalIgnoreCase);
            foreach (var net in netlist.Nets)
            {
                foreach (var c in net.Connections)
                {
                    if (!byPart.TryGetValue(c.RefDes, out var list)) byPart[c.RefDes] = list = new List<Net>();
                    if (!list.Contains(net)) list.Add(net);
                }
            }

            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                string part = queue.Dequeue();
                if (!byPart.TryGetValue(part, out var onNets)) continue;
                foreach (var net in onNets)
                {
                    nets.Add(net.Name);
                    if (net.IsGround) continue;   // the common return: on the path, not followed
                    foreach (var other in net.Connections.Select(c => c.RefDes).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (parts.Contains(other)) continue;
                        double i = Math.Abs(current(other));
                        bool supply = isSource?.Invoke(other) == true;
                        if (!supply && (double.IsNaN(i) || i < least || i > most)) continue;
                        parts.Add(other);
                        queue.Enqueue(other);
                    }
                }
            }

            return (parts, nets);
        }
    }
}
