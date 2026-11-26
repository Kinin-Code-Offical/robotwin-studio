using System;
using System.Collections.Generic;
using System.Globalization;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [smps] Switching regulator modules (Analog/Devices/SwitchingRegulator.cs) from their chips' datasheets and the
    /// boards they come on: the LM2596 and MP1584 step-down and the XL4015 high-current step-down, the MT3608 and XL6009
    /// step-up. Each board carries its input and output capacitors, and its IN− and OUT− are one ground.
    /// </summary>
    public static class PowerParts
    {
        public const string BuckId = "buck-module";
        public const string BoostId = "boost-module";

        public static bool IsModule(string? partId) => partId is BuckId or BoostId;

        public static void Register(PartLibrary lib)
        {
            if (lib == null) throw new ArgumentNullException(nameof(lib));
            Module(lib, BuckId, "Düşürücü Regülatör Modülü (buck)", ConverterTopology.Buck,
                "Anahtarlamalı düşürücü: girişi daha düşük, ayarlı bir çıkışa indirir; doğrusal regülatörden çok daha az ısınır. Çıkış girişten en az düşüm kadar aşağıda kalır, akım sınırını aşan yükte çıkış düşer.");
            Module(lib, BoostId, "Yükseltici Regülatör Modülü (boost)", ConverterTopology.Boost,
                "Anahtarlamalı yükseltici: girişi daha yüksek, ayarlı bir çıkışa çıkarır (örneğin tek Li-ion hücreden 5 V). Çalışmazken giriş diyottan çıkışa geçer.");

            Preset(lib, "lm2596-module", "LM2596 Düşürücü Modül (3 A)", BuckId, "4,5–40 V giriş, 1,25–37 V ayarlı çıkış, 3 A; 150 kHz, iki 220 µF kondansatör",
                "model=LM2596;vout=5;imax=3.5;vinmax=40;uvlo=4.5;freq=150k;l=33u;rds=0.05;vsat=1.1;vf=0.5;iq=5m;ss=1m;cin=220u;cout=220u");
            Preset(lib, "mp1584-module", "MP1584 Mini Düşürücü Modül (3 A)", BuckId, "4,5–28 V giriş, 0,8–20 V ayarlı çıkış, 3 A; 1 MHz, 22 × 17 mm",
                "model=MP1584EN;vout=5;imax=4;vinmax=28;uvlo=4.1;freq=1M;l=4.7u;rds=0.15;vsat=0;vf=0.45;iq=0.1m;ss=1m;cin=10u;cout=22u");
            Preset(lib, "xl4015-module", "XL4015 Düşürücü Modül (5 A)", BuckId, "8–36 V giriş, 1,25–32 V ayarlı çıkış, 5 A; 180 kHz",
                "model=XL4015;vout=12;imax=6.5;vinmax=36;uvlo=7;freq=180k;l=47u;rds=0.08;vsat=0;vf=0.5;iq=2m;ss=2m;cin=220u;cout=470u");
            Preset(lib, "mt3608-module", "MT3608 Yükseltici Modül (2 A)", BoostId, "2–24 V giriş, 28 V'a kadar ayarlı çıkış; 4 A anahtar sınırı, 1,2 MHz",
                "model=MT3608;vout=5;imax=4;vinmax=24;uvlo=2;freq=1.2M;l=22u;rds=0.08;vsat=0;vf=0.45;iq=0.3m;ss=1m;dmax=0.9;cin=22u;cout=22u");
            Preset(lib, "xl6009-module", "XL6009 Yükseltici Modül (4 A)", BoostId, "5–32 V giriş, 35 V'a kadar ayarlı çıkış; 4 A anahtar sınırı, 400 kHz",
                "model=XL6009;vout=12;imax=4;vinmax=32;uvlo=4.5;freq=400k;l=33u;rds=0.11;vsat=0;vf=0.5;iq=2m;ss=2m;dmax=0.9;cin=220u;cout=220u");
        }

        private static void Module(PartLibrary lib, string id, string name, ConverterTopology topology, string description)
        {
            var part = new PartDefinition
            {
                Id = id,
                Name = name,
                Category = StandardParts.Power,
                RefPrefix = "U",
                Symbol = SymbolKind.Module,
                Footprint = "HDR-1x4",
                ValueKey = "vout",
                Description = description,
                Build = c =>
                {
                    int gnd = c.Pin("IN-");
                    c.Add(new Resistor(c.Sub("GNDTIE"), gnd, c.Pin("OUT-"), 0.002));
                    int vin = c.Pin("IN+"), vout = c.Pin("OUT+");
                    var reg = new SwitchingRegulator(c.RefDes, topology, vin, gnd, vout)
                    {
                        OutputVoltage = c.Num("vout", 5),
                        CurrentLimit = Math.Max(0.05, c.Num("imax", 3)),
                        MaxInput = c.Num("vinmax", 40),
                        MinInput = c.Num("uvlo", 4.5),
                        Frequency = Math.Max(1e3, c.Num("freq", 150e3)),
                        Inductance = Math.Max(1e-7, c.Num("l", 33e-6)),
                        SwitchResistance = Math.Max(0, c.Num("rds", 0.1)),
                        SwitchDrop = Math.Max(0, c.Num("vsat", 0)),
                        DiodeDrop = Math.Max(0, c.Num("vf", 0.45)),
                        QuiescentCurrent = Math.Max(0, c.Num("iq", 1e-3)),
                        SoftStart = Math.Max(0, c.Num("ss", 1e-3)),
                        MaxDuty = Math.Max(0.1, Math.Min(0.99, c.Num("dmax", topology == ConverterTopology.Buck ? 0.98 : 0.9)))
                    };
                    c.Add(reg);
                    // The board's own capacitors (low-ESR electrolytics or ceramics).
                    double cin = c.Num("cin", 100e-6), cout = c.Num("cout", 220e-6);
                    if (cin > 0) c.Add(new Capacitor(c.Sub("CIN"), vin, gnd, cin) { SeriesResistance = cin > 50e-6 ? 0.08 : 0.005 });
                    if (cout > 0) c.Add(new Capacitor(c.Sub("COUT"), vout, gnd, cout) { SeriesResistance = cout > 50e-6 ? 0.08 : 0.005 });
                }
            };
            part.Parameters.Add(new ParameterDefinition("model", "Model", topology == ConverterTopology.Buck ? "LM2596" : "MT3608", ""));
            part.Parameters.Add(new ParameterDefinition("vout", "Çıkış gerilimi (trimpot)", "5", "V"));
            part.Parameters.Add(new ParameterDefinition("imax", "Anahtar akım sınırı", topology == ConverterTopology.Buck ? "3.5" : "4", "A"));
            part.Parameters.Add(new ParameterDefinition("vinmax", "En yüksek giriş", topology == ConverterTopology.Buck ? "40" : "24", "V"));
            part.Parameters.Add(new ParameterDefinition("uvlo", "Açılma gerilimi (UVLO)", topology == ConverterTopology.Buck ? "4.5" : "2", "V"));
            part.Parameters.Add(new ParameterDefinition("freq", "Anahtarlama frekansı", topology == ConverterTopology.Buck ? "150k" : "1.2M", "Hz"));
            part.Parameters.Add(new ParameterDefinition("l", "Bobin", topology == ConverterTopology.Buck ? "33u" : "22u", "H"));
            part.Parameters.Add(new ParameterDefinition("rds", "Anahtar direnci", "0.1", "Ω"));
            part.Parameters.Add(new ParameterDefinition("vsat", "Anahtar doyma düşümü", "0", "V"));
            part.Parameters.Add(new ParameterDefinition("vf", "Diyot düşümü", "0.45", "V"));
            part.Parameters.Add(new ParameterDefinition("iq", "Boşta akım", "1m", "A"));
            part.Parameters.Add(new ParameterDefinition("ss", "Yumuşak başlama", "1m", "s"));
            part.Parameters.Add(new ParameterDefinition("cin", "Giriş kondansatörü", topology == ConverterTopology.Buck ? "220u" : "22u", "F"));
            part.Parameters.Add(new ParameterDefinition("cout", "Çıkış kondansatörü", topology == ConverterTopology.Buck ? "220u" : "22u", "F"));
            part.Pins.Add(new PinDefinition("IN+", "1", -4, -1));
            part.Pins.Add(new PinDefinition("IN-", "2", -4, 1));
            part.Pins.Add(new PinDefinition("OUT+", "4", 4, -1));
            part.Pins.Add(new PinDefinition("OUT-", "3", 4, 1));
            part.HalfWidth = 3;
            part.HalfHeight = 3;
            lib.Add(part);
        }

        private static void Preset(PartLibrary lib, string id, string name, string partId, string description, string parameters)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in parameters.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0) map[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim();
            }

            lib.AddPreset(new PartPreset(id, name, partId, StandardParts.Power, description, map));
        }
    }
}
