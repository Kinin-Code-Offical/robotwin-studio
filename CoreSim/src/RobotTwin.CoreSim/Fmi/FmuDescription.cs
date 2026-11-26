using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace RobotTwin.CoreSim.Fmi
{
    public enum FmuCausality
    {
        Parameter,
        CalculatedParameter,
        Input,
        Output,
        Local,
        Independent
    }

    public enum FmuType
    {
        Real,
        Integer,
        Boolean,
        String,
        Enumeration
    }

    /// <summary>[fmi] One of an FMU's scalar variables: what it is called, its value reference, its role and its type.</summary>
    public sealed class FmuVariable
    {
        public string Name { get; set; } = string.Empty;
        public uint ValueReference { get; set; }
        public FmuCausality Causality { get; set; } = FmuCausality.Local;
        public string Variability { get; set; } = "continuous";
        public FmuType Type { get; set; } = FmuType.Real;

        /// <summary>The start value, when it has one (booleans 0/1).</summary>
        public double? Start { get; set; }

        public string Unit { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public override string ToString() => $"{Name} ({Causality}, {Type}, vr {ValueReference})";
    }

    /// <summary>
    /// [fmi] An FMU's <c>modelDescription.xml</c> (FMI 2.0): the model's name and GUID, its co-simulation interface (the
    /// binary's identifier, whether it takes a varying step), the default experiment and its variables.
    /// </summary>
    public sealed class FmuDescription
    {
        public string FmiVersion { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string Guid { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string GenerationTool { get; set; } = string.Empty;

        /// <summary>The co-simulation binary's name (binaries/win64/&lt;id&gt;.dll), or empty when the FMU has no co-simulation interface.</summary>
        public string CoSimulationId { get; set; } = string.Empty;

        /// <summary>True when the FMU is only for model exchange (it needs the importer's integrator).</summary>
        public bool ModelExchangeOnly { get; set; }

        /// <summary>[fmi-me] The model-exchange binary's name, or empty when the FMU has no model-exchange interface.</summary>
        public string ModelExchangeId { get; set; } = string.Empty;

        /// <summary>[fmi-me] How many continuous states and event indicators the model has.</summary>
        public int ContinuousStates { get; set; }

        public int EventIndicators { get; set; }

        /// <summary>[fmi-me] The model needs no fmi2CompletedIntegratorStep after each step.</summary>
        public bool CompletedIntegratorStepNotNeeded { get; set; }

        /// <summary>The binary this importer runs: the co-simulation one when there is one, else the model-exchange one.</summary>
        public string BinaryId => CoSimulationId.Length > 0 ? CoSimulationId : ModelExchangeId;

        /// <summary>[fmi-me] Run through model exchange: the FMU gives derivatives, our integrator solves them.</summary>
        public bool UsesModelExchange => CoSimulationId.Length == 0 && ModelExchangeId.Length > 0;

        public bool CanHandleVariableStep { get; set; }
        public double? DefaultStep { get; set; }
        public double? DefaultStop { get; set; }
        public List<FmuVariable> Variables { get; } = new List<FmuVariable>();

        public IEnumerable<FmuVariable> Inputs => Variables.Where(v => v.Causality == FmuCausality.Input);
        public IEnumerable<FmuVariable> Outputs => Variables.Where(v => v.Causality == FmuCausality.Output);
        public IEnumerable<FmuVariable> Parameters => Variables.Where(v => v.Causality == FmuCausality.Parameter);

        public FmuVariable? Find(string name) => Variables.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal));

        public static FmuDescription Parse(string xml)
        {
            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml);
            }
            catch (System.Xml.XmlException ex)
            {
                throw new FormatException("modelDescription.xml okunamadı: " + ex.Message, ex);
            }

            var root = doc.Root ?? throw new FormatException("modelDescription.xml boş.");
            if (root.Name.LocalName != "fmiModelDescription") throw new FormatException($"modelDescription.xml değil: kök öğe '{root.Name.LocalName}'.");
            string A(XElement e, string name) => (string?)e.Attribute(name) ?? string.Empty;
            double? N(XElement? e, string name) =>
                e != null && double.TryParse((string?)e.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : (double?)null;

            var d = new FmuDescription
            {
                FmiVersion = A(root, "fmiVersion"),
                ModelName = A(root, "modelName"),
                Guid = A(root, "guid"),
                Description = A(root, "description"),
                Author = A(root, "author"),
                GenerationTool = A(root, "generationTool")
            };
            if (!d.FmiVersion.StartsWith("2.", StringComparison.Ordinal))
                throw new NotSupportedException($"FMI {d.FmiVersion}: yalnız FMI 2.0 destekleniyor.");

            var cs = root.Element("CoSimulation");
            if (cs != null)
            {
                d.CoSimulationId = A(cs, "modelIdentifier");
                d.CanHandleVariableStep = string.Equals(A(cs, "canHandleVariableCommunicationStepSize"), "true", StringComparison.OrdinalIgnoreCase);
            }
            else d.ModelExchangeOnly = root.Element("ModelExchange") != null;

            var me = root.Element("ModelExchange");
            if (me != null)
            {
                d.ModelExchangeId = A(me, "modelIdentifier");
                d.CompletedIntegratorStepNotNeeded = string.Equals(A(me, "completedIntegratorStepNotNeeded"), "true", StringComparison.OrdinalIgnoreCase);
            }

            // FMI 2.0 counts the states by their derivatives in ModelStructure (1.0's numberOfContinuousStates when there is none)
            int derivatives = root.Element("ModelStructure")?.Element("Derivatives")?.Elements("Unknown").Count() ?? 0;
            d.ContinuousStates = derivatives > 0 ? derivatives
                : int.TryParse(A(root, "numberOfContinuousStates"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nx) ? Math.Max(0, nx) : 0;
            d.EventIndicators = int.TryParse(A(root, "numberOfEventIndicators"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nz) ? Math.Max(0, nz) : 0;

            var experiment = root.Element("DefaultExperiment");
            d.DefaultStep = N(experiment, "stepSize");
            d.DefaultStop = N(experiment, "stopTime");

            foreach (var sv in root.Element("ModelVariables")?.Elements("ScalarVariable") ?? Enumerable.Empty<XElement>())
            {
                var typed = sv.Elements().FirstOrDefault(e => e.Name.LocalName is "Real" or "Integer" or "Boolean" or "String" or "Enumeration");
                var v = new FmuVariable
                {
                    Name = A(sv, "name"),
                    ValueReference = uint.TryParse(A(sv, "valueReference"), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint vr) ? vr : 0,
                    Variability = A(sv, "variability") is { Length: > 0 } va ? va : "continuous",
                    Description = A(sv, "description"),
                    Causality = A(sv, "causality") switch
                    {
                        "parameter" => FmuCausality.Parameter,
                        "calculatedParameter" => FmuCausality.CalculatedParameter,
                        "input" => FmuCausality.Input,
                        "output" => FmuCausality.Output,
                        "independent" => FmuCausality.Independent,
                        _ => FmuCausality.Local
                    },
                    Type = typed?.Name.LocalName switch
                    {
                        "Integer" => FmuType.Integer,
                        "Boolean" => FmuType.Boolean,
                        "String" => FmuType.String,
                        "Enumeration" => FmuType.Enumeration,
                        _ => FmuType.Real
                    }
                };
                if (typed != null)
                {
                    v.Unit = A(typed, "unit");
                    string start = A(typed, "start");
                    if (v.Type == FmuType.Boolean && start.Length > 0) v.Start = start is "true" or "1" ? 1 : 0;
                    else if (double.TryParse(start, NumberStyles.Float, CultureInfo.InvariantCulture, out double s)) v.Start = s;
                }

                d.Variables.Add(v);
            }

            return d;
        }
    }
}
