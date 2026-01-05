using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Specs
{
    public static class TemplateSpecValidator
    {
        public static IReadOnlyList<string> Validate(TemplateSpec spec)
        {
            var errors = new List<string>();
            if (spec == null)
            {
                errors.Add("TemplateSpec is null.");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(spec.TemplateId))
            {
                errors.Add("TemplateId is required.");
            }
            if (string.IsNullOrWhiteSpace(spec.DisplayName))
            {
                errors.Add("DisplayName is required.");
            }
            if (string.IsNullOrWhiteSpace(spec.Description))
            {
                errors.Add("Description is required.");
            }
            if (string.IsNullOrWhiteSpace(spec.SystemType))
            {
                errors.Add("SystemType is required.");
            }

            return errors;
        }

        public static void ValidateOrThrow(TemplateSpec spec)
        {
            var errors = Validate(spec);
            if (errors.Count == 0) return;
            throw new ArgumentException(string.Join(" ", errors));
        }
    }
}
