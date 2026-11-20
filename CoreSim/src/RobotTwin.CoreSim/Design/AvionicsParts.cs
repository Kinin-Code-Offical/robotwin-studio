using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [aero] Electric bridgewire initiator (e-match) for ejection charges and motor ignition. The
    /// bridgewire is a resistor (≈1.6 Ω) whose temperature follows a first-order thermal model,
    /// C·dT/dt = I²R − ΔT/R_th; it is normalised so the steady temperature reaches ignition at
    /// <see cref="FireCurrent"/>. Typical commercial e-matches: no-fire 0.25 A, all-fire 0.5–1 A,
    /// thermal time constant a few ms (e.g. MJG Technologies J-Tek / Daveyfire 28B data sheets).
    /// After firing the bridge burns open.
    /// </summary>
    public sealed class EMatch : AnalogDevice
    {
        private double _heat;

        public EMatch(string id, int a, int b) : base(id)
        {
            A = a;
            B = b;
        }

        public int A { get; }
        public int B { get; }
        public double Resistance { get; set; } = 1.6;

        /// <summary>Current (A) at which the bridgewire just reaches ignition in steady state.</summary>
        public double FireCurrent { get; set; } = 0.35;

        public double TimeConstant { get; set; } = 3e-3;

        public double Current { get; private set; }
        public bool Fired { get; private set; }
        public double FireTime { get; private set; } = double.NaN;

        /// <summary>Raised once when it fires (time).</summary>
        public event Action<double>? Ignited;

        public override void Reset()
        {
            _heat = 0;
            Current = 0;
            Fired = false;
            FireTime = double.NaN;
        }

        public override void Repair()
        {
            base.Repair();
            Reset();
        }

        public override void Stamp(StampContext ctx) => ctx.AddConductance(A, B, Fired ? 1e-9 : 1.0 / Resistance);

        public override void Accept(AcceptContext ctx)
        {
            Current = Fired ? 0 : ctx.V(A, B) / Resistance;
            if (ctx.IsDc || Fired) return;
            double target = Current * Current;
            _heat = target + (_heat - target) * Math.Exp(-ctx.Dt / TimeConstant);
            if (_heat >= FireCurrent * FireCurrent)
            {
                Fired = true;
                FireTime = ctx.Time;
                ctx.RequestDiscontinuity();
                ctx.Events.Add(new AnalogEvent(ctx.Time, Id, AnalogEventKind.StateChange, $"ateşlendi ({Math.Abs(Current):F2} A), köprü teli açıldı"));
                Ignited?.Invoke(ctx.Time);
            }
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "I", Current);
            sink.Write(Id, "FIRED", Fired ? 1.0 : 0.0);
        }
    }

    /// <summary>
    /// [aero] Avionics and recovery parts: BMP280 / MS5611 barometers, ADXL375 high-g accelerometer,
    /// MS5837-30BA depth sensor (I²C register models in Firmware/Devices/AvionicsSensors.cs, fed by the
    /// world), the e-match, and a DC-equivalent model of a 2212 brushless outrunner with its ESC (an
    /// averaged BLDC + ESC behaves like a DC motor with the same Kv and winding resistance).
    /// </summary>
    public static class AvionicsParts
    {
        public const string Category = "Sensör · Havacılık & Su";
        public const string Bmp280Id = "bmp280";
        public const string Ms5611Id = "ms5611";
        public const string Adxl375Id = "adxl375";
        public const string Ms5837Id = "ms5837";
        public const string EMatchId = "e-match";

        public static bool IsAvionicsSensor(string? id) => id == Bmp280Id || id == Ms5611Id || id == Adxl375Id || id == Ms5837Id;

        public static void Register(PartLibrary lib)
        {
            // BMP280 is registered by Design/BusParts.cs (I²C and SPI, one model for both buses).
            Module(lib, Ms5611Id, "MS5611 Barometre (GY-63)",
                "TE MS5611-01BA03, 10 cm çözünürlüklü altimetre. Komutlar (D1/D2 dönüşümü, ADC okuma, PROM + CRC4), OSR 256…4096 dönüşüm süreleri ve veri sayfası gürültüsü. I²C 0x77/0x76.",
                0.0014, "0x77", new[] { "VCC", "GND", "SCL", "SDA", "CSB", "SDO", "PS" });
            Module(lib, Adxl375Id, "ADXL375 ±200 g İvmeölçer",
                "Yüksek ivme (roket kalkışı, çarpma) için ±200 g, 49 mg/LSB, 13 bit. Gövdenin gerçek ivmesi + motor/itki titreşimi + veri sayfası gürültüsü. I²C 0x53 / SPI.",
                0.00014, "0x53", new[] { "VCC", "GND", "SCL", "SDA", "CS", "INT1" });
            Module(lib, Ms5837Id, "MS5837-30BA Derinlik Sensörü (Bar30)",
                "Su geçirmez 30 bar basınç sensörü: p = p_atm + ρ·g·h. Sıvının yoğunluğu (tatlı/deniz suyu, cıva…) derinlik okumasını belirler. I²C 0x76.",
                0.00125, "0x76", new[] { "VCC", "GND", "SCL", "SDA" });

            var em = new PartDefinition
            {
                Id = EMatchId, Name = "Elektrikli Kapsül (e-match)", Category = Category, RefPrefix = "IG", Symbol = SymbolKind.Module,
                Footprint = "WIRE-2", ValueKey = "model",
                Description = "Paraşüt ateşleme barutu / motor ateşleyicisi. Köprü teli ≈1.6 Ω; içinden ateşleme akımı (≈0.35 A sürekli, kısa darbede daha fazlası) geçince birkaç ms'de ateşlenir ve açık devre olur. Bir MOSFET ile karttan sürülür.",
                Build = c => c.Add(new EMatch(c.RefDes, c.Pin("A"), c.Pin("B"))
                {
                    Resistance = Math.Max(0.1, c.Num("r", 1.6)), FireCurrent = Math.Max(0.05, c.Num("ifire", 0.35)), TimeConstant = Math.Max(1e-4, c.Num("tau", 3e-3))
                })
            };
            em.Parameters.Add(new ParameterDefinition("model", "Model", "e-match 1.6 Ω", "", null));
            em.Parameters.Add(new ParameterDefinition("r", "Köprü teli direnci", "1.6", "Ω", null));
            em.Parameters.Add(new ParameterDefinition("ifire", "Ateşleme akımı (sürekli)", "0.35", "A", null));
            em.Parameters.Add(new ParameterDefinition("tau", "Isıl zaman sabiti", "3m", "s", null));
            em.Pins.Add(new PinDefinition("A", "1", -3, 0));
            em.Pins.Add(new PinDefinition("B", "2", 3, 0));
            em.HalfWidth = 2;
            em.HalfHeight = 1;
            lib.Add(em);

            // 2212 1000 Kv outrunner + 30 A ESC, DC-equivalent: 11.1 V → 11 100 rpm unloaded, R ≈ 0.11 Ω (≈ 100 A stall), I0 ≈ 0.5 A.
            lib.AddPreset(new PartPreset("motor-2212-1000kv", "2212 1000 Kv fırçasız + ESC (DC eşdeğeri)", "dc-motor", StandardParts.Electromechanical,
                "RC eğitim uçağı motoru, 3S 11.1 V, 10×4.7\" pervane ile ≈ 9 N statik itki. Ortalama ESC + BLDC davranışı DC motor eşdeğeriyle.",
                Map("model=2212 1000Kv;voltage=11.1;rpm=11100;i0=0.5;istall=100;gear=1;inductance=20u;inertia=2.4e-5;rth=8;cth=25")));
        }

        private static Dictionary<string, string> Map(string s)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in s.Split(';'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0) map[pair.Substring(0, eq)] = pair.Substring(eq + 1);
            }

            return map;
        }

        private static void Module(PartLibrary lib, string id, string name, string description, double supplyCurrent, string address, string[] pins)
        {
            var part = new PartDefinition
            {
                Id = id, Name = name, Category = Category, RefPrefix = "U", Symbol = SymbolKind.Module, Footprint = "HDR-1x" + pins.Length, ValueKey = "model",
                Description = description,
                Build = c =>
                {
                    int vcc = c.Pin("VCC"), gnd = c.Pin("GND");
                    c.Add(new Resistor(c.RefDes, vcc, gnd, 3.3 / Math.Max(1e-6, supplyCurrent)));
                    // Module pull-ups (10 kΩ on GY boards).
                    c.Add(new Resistor(c.Sub("RSDA"), c.Pin("SDA"), vcc, 10e3));
                    c.Add(new Resistor(c.Sub("RSCL"), c.Pin("SCL"), vcc, 10e3));
                }
            };
            part.Parameters.Add(new ParameterDefinition("model", "Model", name, "", null));
            part.Parameters.Add(new ParameterDefinition("address", "I²C adresi", address, "", null));
            part.Parameters.Add(new ParameterDefinition("noise", "Gürültü", "true", "", new[] { "true", "false" }));
            for (int i = 0; i < pins.Length; i++) part.Pins.Add(new PinDefinition(pins[i], (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), -4, -pins.Length + 1 + 2 * i));
            part.HalfWidth = 3;
            part.HalfHeight = pins.Length;
            lib.Add(part);
        }
    }
}
