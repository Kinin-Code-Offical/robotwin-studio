using System.Collections.Generic;
using RobotTwin.CoreSim.Design;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[path] A part's current path: the series loop it is in, not the parts beside it.</summary>
    public class CurrentPathTests
    {
        private static Net N(string name, bool ground, params (string Part, string Pin)[] pins)
        {
            var n = new Net { Name = name, IsGround = ground };
            foreach (var (part, pin) in pins) n.Connections.Add(new NetConnection(part, pin));
            return n;
        }

        // BT1+ → R1 → D1 → GND ← BT1-; beside it R2 from BT1+ to GND with a tiny current, and an idle switch on the LED's anode
        private static Netlist Circuit()
        {
            var nl = new Netlist();
            nl.Nets.Add(N("VBAT", false, ("BT1", "+"), ("R1", "1"), ("R2", "1")));
            nl.Nets.Add(N("A", false, ("R1", "2"), ("D1", "A"), ("SW1", "1")));
            nl.Nets.Add(N("GND", true, ("D1", "K"), ("BT1", "-"), ("R2", "2")));
            nl.Nets.Add(N("SWOUT", false, ("SW1", "2")));
            return nl;
        }

        private static readonly Dictionary<string, double> Currents = new Dictionary<string, double>
        {
            ["BT1"] = -0.0151, ["R1"] = 0.015, ["D1"] = 0.015, ["R2"] = 0.0001, ["SW1"] = 0
        };

        [Fact]
        public void TheLedsLoopIsItsResistorAndTheBattery()
        {
            var (parts, nets) = CurrentPath.Trace(Circuit(), "D1", p => Currents[p], p => p == "BT1");
            Assert.Equal(new[] { "BT1", "D1", "R1" }, Sorted(parts));
            Assert.Contains("A", nets);
            Assert.Contains("VBAT", nets);
            Assert.Contains("GND", nets);
            Assert.DoesNotContain("SWOUT", nets);
        }

        [Fact]
        public void AWeakSideBranchIsLeftOut()
        {
            var (parts, _) = CurrentPath.Trace(Circuit(), "R1", p => Currents[p], p => p == "BT1");
            Assert.DoesNotContain("R2", parts);   // 0.1 mA beside 15 mA
            Assert.DoesNotContain("SW1", parts);
        }

        [Fact]
        public void GroundIsNotFollowed()
        {
            // R2 alone: its loop goes through the battery; the LED's loop beside it (150 times its current) is not on it
            var (parts, _) = CurrentPath.Trace(Circuit(), "R2", p => Currents[p], p => p == "BT1");
            Assert.Contains("BT1", parts);
            Assert.DoesNotContain("D1", parts);
        }

        private static string[] Sorted(HashSet<string> s)
        {
            var a = new List<string>(s);
            a.Sort(System.StringComparer.Ordinal);
            return a.ToArray();
        }
    }
}
