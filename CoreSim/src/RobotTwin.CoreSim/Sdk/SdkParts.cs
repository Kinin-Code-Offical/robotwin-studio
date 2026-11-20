using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Sdk
{
    /// <summary>
    /// A voltage output for custom sensors: a Thevenin source (value, output resistance) between
    /// OUT and GND whose open-circuit voltage is set by code (a behaviour, an expression or a
    /// published reading). Subclass it in a plugin for sensors with their own electrical model,
    /// or use the ready "sdk-sensor" part.
    /// </summary>
    public class SensorDevice : AnalogDevice
    {
        private double _value;

        public SensorDevice(string id, int output, int ground) : base(id)
        {
            Output = output;
            Ground = ground;
        }

        public int Output { get; }
        public int Ground { get; }

        /// <summary>Open-circuit output voltage (V), clamped to [<see cref="Min"/>, <see cref="Max"/>].</summary>
        public double Value
        {
            get => _value;
            set
            {
                double v = double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Max(Min, Math.Min(Max, value));
                if (Math.Abs(v - _value) > 1e-12) PendingDiscontinuity = true;
                _value = v;
            }
        }

        public double Min { get; set; }
        public double Max { get; set; } = 5.0;

        /// <summary>Output resistance (Ω).</summary>
        public double OutputResistance { get; set; } = 100.0;

        /// <summary>Name of the published reading that drives this output ("" = set <see cref="Value"/> yourself).</summary>
        public string Signal { get; set; } = string.Empty;

        /// <summary>Volts per unit of the published reading, and offset (V).</summary>
        public double Scale { get; set; } = 1.0;

        public double Offset { get; set; }

        /// <summary>Sets <see cref="Value"/> from a reading with <see cref="Scale"/> and <see cref="Offset"/>.</summary>
        public void Feed(double reading) => Value = reading * Scale + Offset;

        public override void Stamp(StampContext ctx)
        {
            double g = 1.0 / Math.Max(1e-3, OutputResistance);
            ctx.AddConductance(Output, Ground, g);
            ctx.AddCurrent(Ground, Output, _value * g);
        }

        public override void Reset()
        {
            _value = 0;
        }
    }

    /// <summary>Circuit parts that come with the SDK.</summary>
    public static class SdkParts
    {
        public const string SensorId = "sdk-sensor";

        /// <summary>Adds the SDK parts to a library once ("Programlanabilir sensör").</summary>
        public static void EnsureRegistered(PartLibrary library)
        {
            lock (library)
            {
                if (library.Find(SensorId) != null) return;
                var def = new PartDefinition
                {
                    Id = SensorId,
                    Name = "Programlanabilir sensör (SDK)",
                    Category = "Sensör",
                    Description = "Çıkış gerilimi bir davranış/ifade tarafından yayımlanan değerden gelir (OUT–GND).",
                    RefPrefix = "U",
                    Symbol = SymbolKind.Module,
                    HalfWidth = 2,
                    HalfHeight = 1,
                    ValueKey = "signal"
                };
                def.Pins.Add(new PinDefinition("OUT", "1", -3, 0));
                def.Pins.Add(new PinDefinition("GND", "2", 3, 0));
                def.Parameters.Add(new ParameterDefinition("signal", "Okuma adı (Publish)", "sensör"));
                def.Parameters.Add(new ParameterDefinition("scale", "Ölçek (V/birim)", "1"));
                def.Parameters.Add(new ParameterDefinition("offset", "Kayma", "0", "V"));
                def.Parameters.Add(new ParameterDefinition("min", "En düşük", "0", "V"));
                def.Parameters.Add(new ParameterDefinition("max", "En yüksek", "5", "V"));
                def.Parameters.Add(new ParameterDefinition("rout", "Çıkış direnci", "100", "Ω"));
                def.Build = ctx =>
                {
                    var d = ctx.Add(new SensorDevice(ctx.RefDes, ctx.Pin("OUT"), ctx.Pin("GND"))
                    {
                        Signal = ctx.Text("signal"),
                        Scale = ctx.Num("scale", 1),
                        Offset = ctx.Num("offset", 0),
                        Min = ctx.Num("min", 0),
                        Max = ctx.Num("max", 5),
                        OutputResistance = ctx.Num("rout", 100)
                    });
                    d.Value = d.Offset;
                };
                library.Add(def);
                library.AddPreset(new PartPreset(SensorId, def.Name, SensorId, "Sensör", "Davranış veya ifadeyle sürülen 0–5 V analog çıkış",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
            }
        }
    }
}
