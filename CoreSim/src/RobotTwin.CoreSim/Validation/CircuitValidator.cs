using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Specs;

namespace RobotTwin.CoreSim.Validation
{
    public class ValidationResult
    {
        public bool IsValid { get; set; } = true;
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public static class CircuitValidator
    {
        public static ValidationResult Validate(CircuitSpec spec)
        {
            var result = new ValidationResult();

            if (spec == null)
            {
                result.Errors.Add("CircuitSpec is null.");
                result.IsValid = false;
                return result;
            }

            if (string.IsNullOrWhiteSpace(spec.Name))
            {
                result.Warnings.Add("Circuit name is empty.");
            }

            // Rule 1: Must have at least one component
            if (spec.Components == null || spec.Components.Count == 0)
            {
                result.Warnings.Add("Circuit is empty (no components).");
            }
            else
            {
                // Rule 2: Component ID uniqueness
                var ids = new HashSet<string>();
                foreach (var comp in spec.Components)
                {
                    if (string.IsNullOrWhiteSpace(comp.InstanceID))
                    {
                        result.Errors.Add($"Component with CatalogID '{comp.CatalogID}' has no InstanceID.");
                        result.IsValid = false;
                    }
                    else if (!ids.Add(comp.InstanceID))
                    {
                        result.Errors.Add($"Duplicate Component InstanceID: {comp.InstanceID}");
                        result.IsValid = false;
                    }
                }
            }

            // Rule 3: Connections integrity (Start/End existence)
            if (spec.Connections != null)
            {
                foreach (var conn in spec.Connections)
                {
                    if (!ComponentExists(spec, conn.FromComponentID))
                        result.Errors.Add($"Connection references missing component: {conn.FromComponentID}");
                    
                    if (!ComponentExists(spec, conn.ToComponentID))
                        result.Errors.Add($"Connection references missing component: {conn.ToComponentID}");
                }
            }

            return result;
        }

        private static bool ComponentExists(CircuitSpec spec, string id)
        {
            return spec.Components != null && spec.Components.Any(c => c.InstanceID == id);
        }
    }
}
