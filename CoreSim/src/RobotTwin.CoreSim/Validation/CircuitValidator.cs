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

            // Rule 3: Connections integrity & Net Analysis
            var adjacency = new Dictionary<string, List<string>>(); // CompID -> connected CompIDs
            
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

                    // Short Circuit Check (Direct Power to GND)
                    if (fromExists && toExists)
                    {
                        var fromDef = GetDefinition(spec, definitions, conn.FromComponentID);
                        var toDef = GetDefinition(spec, definitions, conn.ToComponentID);

                        if (fromDef != null && toDef != null)
                        {
                            if ((fromDef.ID == "gnd" && toDef.Type == ComponentType.Source) ||
                                (toDef.ID == "gnd" && fromDef.Type == ComponentType.Source))
                            {
                                // Check if it's actually the positive rail
                                var sourcePin = (fromDef.Type == ComponentType.Source) ? conn.FromPin : conn.ToPin;
                                if (IsPositiveRail(sourcePin))
                                {
                                    result.Errors.Add($"Short Circuit detected between {conn.FromComponentID} (Power) and {conn.ToComponentID} (GND).");
                                    result.IsValid = false;
                                }
                            }
                        }
                    }
                }
            }

            // Rule 4: Basic Power Budget
            // Simple logic: If we have active components, ensure we have at least one source.
            // (Already covered by existing logic, but let's make it more explicit about "Budget")
            if (hasActive && !hasPower) 
            {
                 // ensure we didn't double add
                 if (!result.Warnings.Any(w => w.Contains("missing Power Source")))
                    result.Warnings.Add("Power Budget Breach: Active components present but no Power Source.");
            }

            return result;
        }

        private static bool IsPositiveRail(string pinName)
        {
            var p = pinName.ToUpperInvariant();
            return p.Contains("VCC") || p.Contains("5V") || p.Contains("3.3V") || p.Contains("VIN") || p.Contains("VDD");
        }

        private static ComponentDefinition? GetDefinition(CircuitSpec spec, List<ComponentDefinition> defs, string instId)
        {
            var comp = spec.Components.FirstOrDefault(c => c.InstanceID == instId);
            if (comp == null) return null;
            return defs.FirstOrDefault(d => d.ID == comp.CatalogID);
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
