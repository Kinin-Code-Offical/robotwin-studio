using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Libraries;

namespace RobotTwin.CoreSim.Fmi
{
    /// <summary>
    /// [fmi] An FMU made into a part of the library: a pin for each input (left) and output (right), a REF pin the
    /// voltages are measured from and driven against, a parameter for each of the FMU's parameters (its start value) and
    /// the communication step. The FMU's file travels inside the part, so a project using it opens anywhere.
    /// </summary>
    public static class FmuImport
    {
        public const string ReferencePin = "REF";
        public const string StepParameter = "fmu.step";

        /// <summary>The pins an FMU's part has: its numeric inputs and outputs, then REF.</summary>
        public static IReadOnlyList<string> Ports(FmuDescription d) =>
            d.Inputs.Concat(d.Outputs).Where(Numeric).Select(v => v.Name).Append(ReferencePin).ToList();

        private static bool Numeric(FmuVariable v) => v.Type != FmuType.String;

        /// <summary>A library part from an FMU file's bytes; refuses an FMU this computer cannot run as co-simulation.</summary>
        public static CustomPart ToCustomPart(byte[] fmuBytes)
        {
            var fmu = FmuArchive.Open(fmuBytes);
            var d = fmu.Description;
            if (d.CoSimulationId.Length == 0)
                throw new NotSupportedException(d.ModelExchangeOnly
                    ? $"{d.ModelName}: yalnız model değişimi (Model Exchange) FMU'su; ortak benzetim (Co-Simulation) arabirimi olan bir FMU gerekir."
                    : $"{d.ModelName}: FMU'nun ortak benzetim arabirimi yok.");
            if (fmu.BinaryPath == null)
                throw new NotSupportedException($"{d.ModelName}: FMU'da 64 bit Windows ikilisi yok (binaries/win64/{d.CoSimulationId}.dll).");
            if (!d.Inputs.Concat(d.Outputs).Any(Numeric))
                throw new NotSupportedException($"{d.ModelName}: FMU'nun sayısal girişi ya da çıkışı yok; devreye bağlanacak bir şey yok.");

            var model = new UserModel { Kind = UserModelKind.Fmu, FmuData = Convert.ToBase64String(fmuBytes) };
            var spec = new UserPartSpec { RefPrefix = "U", Symbol = UserSymbolStyle.Box, BodyLabel = d.ModelName, Model = model, Source = "FMI 2.0 FMU" };
            int pad = 1;
            void Pin(string name, PinElectricalType type, PinSide side, string description)
            {
                spec.Pins.Add(new UserPin { Name = name, Pad = (pad++).ToString(CultureInfo.InvariantCulture), Type = type, Side = side, Description = description });
                model.PinMap[name] = name;
            }

            string Info(FmuVariable v) => string.Join(", ", new[] { v.Description, v.Unit.Length > 0 ? "birim " + v.Unit : string.Empty, v.Type.ToString() }.Where(x => x.Length > 0));
            foreach (var v in d.Inputs.Where(Numeric)) Pin(v.Name, PinElectricalType.Input, PinSide.Left, Info(v));
            foreach (var v in d.Outputs.Where(Numeric)) Pin(v.Name, PinElectricalType.Output, PinSide.Right, Info(v));
            Pin(ReferencePin, PinElectricalType.PowerIn, PinSide.Left, "Gerilimlerin ölçüldüğü ve sürüldüğü referans (çoğunlukla GND)");

            foreach (var v in d.Parameters.Where(Numeric))
            {
                spec.Parameters.Add(new UserParameter
                {
                    Key = v.Name,
                    Label = v.Name,
                    Value = (v.Start ?? 0).ToString("R", CultureInfo.InvariantCulture),
                    Unit = v.Unit,
                    Description = v.Description
                });
            }

            spec.Parameters.Add(new UserParameter
            {
                Key = StepParameter,
                Label = "İletişim adımı",
                Value = (d.DefaultStep is double step && step > 0 ? step : 1e-3).ToString("R", CultureInfo.InvariantCulture),
                Unit = "s",
                Description = "Devre ile FMU'nun buluştuğu aralık: girişler verilir, FMU kendi çözücüsüyle bu kadar ilerler, çıkışlar bir sonrakine kadar tutulur."
            });

            string id = "fmu-" + Slug(d.ModelName) + "-" + Hash(fmuBytes);
            int inputs = d.Inputs.Count(Numeric), outputs = d.Outputs.Count(Numeric);
            return new CustomPart
            {
                Id = id,
                BasePartId = id,
                Name = d.ModelName + " (FMU)",
                Category = "FMU",
                Description = (d.Description.Length > 0 ? d.Description + " · " : string.Empty) + $"FMI 2.0 ortak benzetim, {inputs} giriş, {outputs} çıkış" + (d.GenerationTool.Length > 0 ? ", " + d.GenerationTool : string.Empty),
                Author = d.Author,
                Definition = spec
            };
        }

        /// <summary>The part's electrical model: the FMU instance for this placement, its pins, its parameters.</summary>
        public static void Build(PartBuildContext ctx, FmuArchive fmu, Func<string, int> pin)
        {
            var d = fmu.Description;
            var parameters = new Dictionary<string, double>();
            foreach (var v in d.Parameters.Where(Numeric))
                if (ctx.Text(v.Name).Length > 0) parameters[v.Name] = ctx.Num(v.Name, v.Start ?? 0);
            var block = new FmuBlock(ctx.RefDes, new Fmu2Instance(fmu, ctx.RefDes), pin(ReferencePin),
                d.Inputs.Where(Numeric).Select(v => (v, pin(v.Name))),
                d.Outputs.Where(Numeric).Select(v => (v, pin(v.Name))),
                parameters);
            double step = ctx.Num(StepParameter, 0);
            if (step > 0) block.CommunicationStep = step;
            ctx.Add(block);
        }

        private static string Slug(string name)
        {
            var chars = name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) && ch < 128 ? ch : '-').ToArray();
            string s = new string(chars).Trim('-');
            while (s.Contains("--")) s = s.Replace("--", "-");
            return s.Length == 0 ? "model" : s.Length > 32 ? s.Substring(0, 32) : s;
        }

        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Take(4).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }
}
