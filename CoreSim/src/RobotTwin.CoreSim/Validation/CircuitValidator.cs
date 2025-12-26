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

            if (spec.Components == null || spec.Components.Count == 0)
            {
                result.Warnings.Add("Circuit is empty (no components).");
                // Start early return or continue? Old one returned.
                // But let's let it run (loops won't execute).
            }

            // Check for duplicates
            var idSet = new HashSet<string>();
            foreach (var c in spec.Components)
            {
                if (!idSet.Add(c.InstanceID))
                {
                    result.Errors.Add($"Duplicate Component InstanceID: {c.InstanceID}");
                    result.IsValid = false;
                }
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
                hasGround = true; 
            }
            
            // Check connections for VCC/GND hints
            if (spec.Connections != null)
            {
                foreach (var conn in spec.Connections)
                {
                    var p1 = conn.FromPin.ToUpper();
                    var p2 = conn.ToPin.ToUpper();
                    if (p1.Contains("VCC") || p2.Contains("VCC") || p1.Contains("5V") || p2.Contains("5V")) hasPower = true;
                    if (p1.Contains("GND") || p2.Contains("GND")) hasGround = true;
                }
            }

            if (!hasPower) result.Warnings.Add("No obvious Power Source detected (VCC/Battery).");
            if (!hasGround) result.Warnings.Add("No obvious Ground reference detected (GND).");

            // 2. Connectivity Check (Floating Pins & Integrity)
            var componentIds = spec.Components.Select(c => c.InstanceID).ToHashSet();
            var connectedComponentIds = new HashSet<string>();
            
            if (spec.Connections != null)
            {
                foreach (var conn in spec.Connections)
                {
                    if (!componentIds.Contains(conn.FromComponentID))
                    {
                        result.Errors.Add($"Connection references missing component: '{conn.FromComponentID}'");
                        result.IsValid = false;
                    }
                    if (!componentIds.Contains(conn.ToComponentID))
                    {
                        result.Errors.Add($"Connection references missing component: '{conn.ToComponentID}'");
                        result.IsValid = false;
                    }

                    connectedComponentIds.Add(conn.FromComponentID);
                    connectedComponentIds.Add(conn.ToComponentID);
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
