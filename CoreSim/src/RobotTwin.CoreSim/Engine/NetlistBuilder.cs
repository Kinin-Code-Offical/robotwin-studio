using System.Text;
using RobotTwin.CoreSim.Specs;

namespace RobotTwin.CoreSim.Engine
{
    public static class NetlistBuilder
    {
        public static string Build(CircuitSpec spec)
        {
            var sb = new StringBuilder();
            sb.AppendLine("* RoboTwin Generated Netlist");

            foreach (var comp in spec.Components)
            {
                // Basic SPICE mapping placeholder
                // R1 N001 N002 10k
                // D1 N002 0 LED
                if (comp.Type == "Resistor")
                {
                    sb.AppendLine($"R{comp.Id} 1 0 1k"); // Mock connections
                }
                else if (comp.Type == "LED")
                {
                    sb.AppendLine($"D{comp.Id} 1 0 LED");
                }
            }

            sb.AppendLine(".END");
            return sb.ToString();
        }
    }
}
