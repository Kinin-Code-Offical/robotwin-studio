using System.Collections.Generic;
using RobotTwin.CoreSim.Specs;
using System.Linq;

namespace RobotTwin.CoreSim.Validation
{
    public class CircuitValidator
    {
        public struct ValidationResult
        {
            public bool IsValid;
            public List<string> Errors;
            public List<string> Warnings;
        }

        public static ValidationResult ValidateCircuit(CircuitSpec spec)
        {
            var result = new ValidationResult
            {
                IsValid = true,
                Errors = new List<string>(),
                Warnings = new List<string>()
            };

            if (spec == null)
            {
                result.Errors.Add("CircuitSpec is null");
                result.IsValid = false;
                return result;
            }

            // 1. Check for Power (VCC) and Ground (GND)
            // Assuming components with specific types or IDs serve as power sources
            bool hasPower = false;
            bool hasGround = false;

            // Simple MVP check: Look for nets named "VCC" or "GND" or components that provide them.
            // In a real netlist, we'd check connectivity.
            // For MVP, if we have a Battery or VoltageSource, assume OK.
            
            if (spec.Components.Any(c => c.CatalogID.ToLower().Contains("battery") || c.CatalogID.ToLower().Contains("power") || c.CatalogID.ToLower().Contains("source")))
            {
                hasPower = true;
                hasGround = true; // Assume battery has both
            }
            
            // Check Nets for named VCC/GND if no explicit component found (abstract representation)
            if (!hasPower) hasPower = spec.Nets.Any(n => n.Id.ToUpper().Contains("VCC") || n.Id.ToUpper().Contains("5V") || n.Id.ToUpper().Contains("3V3"));
            if (!hasGround) hasGround = spec.Nets.Any(n => n.Id.ToUpper().Contains("GND"));

            if (!hasPower) result.Warnings.Add("No obvious Power Source detected (VCC/Battery).");
            if (!hasGround) result.Warnings.Add("No obvious Ground reference detected (GND).");

            // 2. Connectivity Check (Floating Pins)
            // Just warn if a component exists but isn't connected to any net
            var connectedComponentIds = new HashSet<string>();
            foreach (var net in spec.Nets)
            {
                foreach (var pin in net.Pins)
                {
                    connectedComponentIds.Add(pin.ComponentId);
                }
            }

            foreach (var comp in spec.Components)
            {
                if (!connectedComponentIds.Contains(comp.InstanceID))
                {
                    result.Warnings.Add($"Component '{comp.InstanceID}' ({comp.CatalogID}) is floating (no connections).");
                }
            }

            return result;
        }
    }
}
