using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Catalogs;

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
        public static ValidationResult Validate(CircuitSpec spec, ComponentCatalog? catalog = null)
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

            // Load Defaults/Catalog for lookups
            var definitions = catalog?.Components ?? ComponentCatalog.GetDefaults();

            bool hasGnd = false;
            bool hasPower = false;
            bool hasActive = false;

            // Rule 1: Must have at least one component
            if (spec.Components == null || spec.Components.Count == 0)
            {
                result.Warnings.Add("Circuit is empty (no components).");
                return result; // Stop early
            }

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

                // Metadata checks
                var def = definitions.FirstOrDefault(d => d.ID == comp.CatalogID);
                if (def == null)
                {
                    result.Warnings.Add($"Unknown Component CatalogID: {comp.CatalogID}");
                }
                else
                {
                    if (def.ID == "gnd") hasGnd = true;
                    if (def.Type == ComponentType.Source) hasPower = true;
                    if (def.Type == ComponentType.Active || def.Type == ComponentType.IC) hasActive = true;
                }
            }

            // Rule 2: GND and Power Checks
            if (hasActive)
            {
                if (!hasGnd) result.Warnings.Add("Active circuit missing GND node.");
                if (!hasPower) result.Warnings.Add("Active circuit missing Power Source.");
            }

            // Rule 3: Connections integrity
            if (spec.Connections != null)
            {
                foreach (var conn in spec.Connections)
                {
                    bool fromExists = ComponentExists(spec, conn.FromComponentID);
                    bool toExists = ComponentExists(spec, conn.ToComponentID);

                    if (!fromExists)
                        result.Errors.Add($"Connection references missing component: {conn.FromComponentID}");
                    
                    if (!toExists)
                        result.Errors.Add($"Connection references missing component: {conn.ToComponentID}");

                    // Validate Pins if components exist
                    if (fromExists) ValidatePin(conn.FromComponentID, conn.FromPin, spec, definitions, result);
                    if (toExists) ValidatePin(conn.ToComponentID, conn.ToPin, spec, definitions, result);
                }
            }

            return result;
        }

        private static bool ComponentExists(CircuitSpec spec, string id)
        {
            return spec.Components != null && spec.Components.Any(c => c.InstanceID == id);
        }

        private static void ValidatePin(string compID, string pin, CircuitSpec spec, List<ComponentDefinition> defs, ValidationResult result)
        {
            var comp = spec.Components.First(c => c.InstanceID == compID);
            var def = defs.FirstOrDefault(d => d.ID == comp.CatalogID);
            
            // If unknown definition, we skip pin check (already warned)
            if (def != null)
            {
                if (!def.Pins.Contains(pin))
                {
                    result.Errors.Add($"Pin '{pin}' does not exist on component '{compID}' ({def.Name}).");
                    result.IsValid = false;
                }
            }
        }
    }
}
