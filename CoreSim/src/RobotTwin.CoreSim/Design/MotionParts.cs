using System;
using System.Collections.Generic;
using System.Globalization;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [stepper] Stepper motors and their drivers: bipolar NEMA 17s from their datasheets (resistance, inductance, holding
    /// torque at rated current, detent torque, rotor inertia) and the A4988 carrier (Analog/Devices/Stepper.cs).
    /// </summary>
    public static class MotionParts
    {
        public const string StepperId = "stepper";
        public const string A4988Id = "a4988";
        public const string Drv8825Id = "drv8825-stepper";
        public const string Tmc2209Id = "tmc2209";

        /// <summary>The driver carriers (same footprint and pin rows).</summary>
        public static bool IsDriver(string? partId) => partId is A4988Id or Drv8825Id or Tmc2209Id;

        public static void Register(PartLibrary lib)
        {
            if (lib == null) throw new ArgumentNullException(nameof(lib));

            var stepper = new PartDefinition
            {
                Id = StepperId,
                Name = "Step Motor (iki fazlı, bipolar)",
                Category = StandardParts.Electromechanical,
                RefPrefix = "M",
                Symbol = SymbolKind.Module,
                Footprint = "HDR-1x4",
                ValueKey = "model",
                Description = "İki fazlı hibrit step motor: A ve B sargıları, 200 adım/tur (1,8°). Bir sürücüyle (A4988) adım adım döner; yükü ya da adım hızı akımın verebileceğini aşarsa adım kaçırır.",
                Build = c =>
                {
                    double rated = Math.Max(0.05, c.Num("irated", 1.7));
                    var m = c.Add(new StepperMotor(c.RefDes, c.Pin("A+"), c.Pin("A-"), c.Pin("B+"), c.Pin("B-"))
                    {
                        Resistance = c.Num("resistance", 1.5),
                        Inductance = c.Num("inductance", 2.8e-3),
                        // Holding torque is quoted with both phases at rated current (full step): √2 of one phase's.
                        TorqueConstant = c.Num("holding", 0.40) / (Math.Sqrt(2) * rated),
                        RotorTeeth = Math.Max(1, (int)Math.Round(c.Num("steps", 200) / 4)),
                        DetentTorque = c.Num("detent", 0.02),
                        Inertia = c.Num("inertia", 54e-7),
                        LoadTorque = c.Num("load", 0)
                    });
                }
            };
            stepper.Parameters.Add(new ParameterDefinition("model", "Model", "17HS4401", ""));
            stepper.Parameters.Add(new ParameterDefinition("irated", "Anma akımı / faz", "1.7", "A"));
            stepper.Parameters.Add(new ParameterDefinition("resistance", "Sargı direnci / faz", "1.5", "Ω"));
            stepper.Parameters.Add(new ParameterDefinition("inductance", "Sargı endüktansı / faz", "2.8m", "H"));
            stepper.Parameters.Add(new ParameterDefinition("holding", "Tutma torku (iki faz, anma akımı)", "0.40", "N·m"));
            stepper.Parameters.Add(new ParameterDefinition("detent", "Takılma torku (akımsız)", "0.02", "N·m"));
            stepper.Parameters.Add(new ParameterDefinition("inertia", "Rotor ataleti", "54e-7", "kg·m²"));
            stepper.Parameters.Add(new ParameterDefinition("steps", "Adım / tur", "200", ""));
            stepper.Parameters.Add(new ParameterDefinition("load", "Yük torku", "0", "N·m"));
            stepper.Parameters.Add(new ParameterDefinition("length", "Gövde boyu", "40", "mm"));
            string[] coil = { "A+", "A-", "B+", "B-" };
            for (int k = 0; k < coil.Length; k++) stepper.Pins.Add(new PinDefinition(coil[k], (k + 1).ToString(CultureInfo.InvariantCulture), -5, -3 + 2 * k));
            stepper.HalfWidth = 4;
            stepper.HalfHeight = 4;
            lib.Add(stepper);
            Preset(lib, "nema17-17hs4401", "NEMA 17 Step Motor 17HS4401", stepper.Id, "42 × 42 × 40 mm, 1,7 A, 0,40 N·m, 1,8°: 3B yazıcı ve CNC ekseni",
                "model=17HS4401;irated=1.7;resistance=1.5;inductance=2.8m;holding=0.40;detent=0.022;inertia=54e-7");
            Preset(lib, "nema17-17hs08", "NEMA 17 İnce Step Motor 17HS08-1004S", stepper.Id, "42 × 42 × 20 mm, 1,0 A, 0,13 N·m: ekstruder ve hafif eksenler",
                "model=17HS08-1004S;irated=1.0;resistance=3.5;inductance=4.6m;holding=0.13;detent=0.012;inertia=20e-7;length=20");

            Driver(lib, A4988Id, "A4988 Step Motor Sürücü", StepperChip.A4988,
                "STEP'in her yükselen kenarında bir (mikro) adım, DIR yönü verir; MS1–MS3 tam adımdan 1/16'ya. EN LOW iken çalışır. VMOT 8–35 V, VDD 3–5,5 V; akım sınırı kartın Vref ayarıdır.",
                new[] { "EN", "MS1", "MS2", "MS3", "RST", "SLP", "STEP", "DIR" }, new[] { "VMOT", "GND", "2B", "2A", "1A", "1B", "VDD", "GND2" }, 1.0);
            Preset(lib, "a4988", "A4988 Step Motor Sürücü", A4988Id, "1/16 mikro adım, 1 A (soğutucusuz), 8–35 V");
            Driver(lib, Drv8825Id, "DRV8825 Step Motor Sürücü", StepperChip.Drv8825,
                "A4988 ile aynı ayaklar, 1/32 mikro adım (M0–M2), kendi 3,3 V mantığı (VDD yok: o ayak FAULT), VMOT 8,2–45 V. Akım sınırı Vref × 2.",
                new[] { "EN", "M0", "M1", "M2", "RST", "SLP", "STEP", "DIR" }, new[] { "VMOT", "GND", "B2", "B1", "A1", "A2", "FLT", "GND2" }, 1.5);
            Preset(lib, "drv8825", "DRV8825 Step Motor Sürücü", Drv8825Id, "1/32 mikro adım, 1,5 A (soğutucusuz), 8,2–45 V");
            Driver(lib, Tmc2209Id, "TMC2209 Step Motor Sürücü", StepperChip.Tmc2209,
                "Sessiz sürücü (bağımsız kip): MS1/MS2 ile 1/8, 1/16, 1/32, 1/64; her adım 1/256'ya ara değerlenir (MicroPlyer). VM 4,75–29 V, VIO 3,3–5 V. EN LOW iken çalışır.",
                new[] { "EN", "MS1", "MS2", "PDN", "NC1", "NC2", "STEP", "DIR" }, new[] { "VM", "GND", "B2", "B1", "A1", "A2", "VIO", "GND2" }, 1.2);
            Preset(lib, "tmc2209", "TMC2209 Step Motor Sürücü", Tmc2209Id, "1/256 ara değerlemeli, 1,2 A tepe (1,7 A en çok), 4,75–29 V");
        }

        /// <summary>A driver carrier: eight logic pins down the left, power and the two phases down the right.</summary>
        private static void Driver(PartLibrary lib, string id, string name, StepperChip chip, string description, string[] left, string[] right, double current)
        {
            var drv = new PartDefinition
            {
                Id = id,
                Name = name,
                Category = StandardParts.Electromechanical,
                RefPrefix = "U",
                Symbol = SymbolKind.Module,
                Footprint = "HDR-1x16",
                ValueKey = "model",
                Description = description,
                Build = c =>
                {
                    int gnd = c.Pin("GND");
                    c.Add(new Resistor(c.Sub("GNDTIE"), gnd, c.Pin("GND2"), 0.001));
                    int Pin(string name) => Array.IndexOf(left, name) >= 0 || Array.IndexOf(right, name) >= 0 ? c.Pin(name) : -1;
                    var d = new StepperDriver(c.RefDes, chip, Pin(right[0]), gnd, Pin(right[4]), Pin(right[5]), Pin(right[3]), Pin(right[2]),
                        chip == StepperChip.Drv8825 ? -1 : Pin(right[6]), Pin("STEP"), Pin("DIR"), Pin("EN"), Pin(left[1]), Pin(left[2]),
                        chip == StepperChip.Tmc2209 ? -1 : Pin(left[3]), chip == StepperChip.Tmc2209 ? -1 : Pin("SLP"), chip == StepperChip.Tmc2209 ? -1 : Pin("RST"))
                    {
                        CurrentLimit = c.Num("imax", current)
                    };
                    if (c.Num("rds", 0) > 0) d.OnResistance = c.Num("rds", 0);
                    c.Add(d);
                }
            };
            drv.Parameters.Add(new ParameterDefinition("model", "Model", name.Split(' ')[0], ""));
            drv.Parameters.Add(new ParameterDefinition("imax", "Akım sınırı (tepe, Vref ayarı)", current.ToString(CultureInfo.InvariantCulture), "A"));
            drv.Parameters.Add(new ParameterDefinition("rds", "Köprü direnci (0 = çipin)", "0", "Ω"));
            for (int k = 0; k < 8; k++)
            {
                drv.Pins.Add(new PinDefinition(left[k], (k + 1).ToString(CultureInfo.InvariantCulture), -6, -7 + 2 * k));
                drv.Pins.Add(new PinDefinition(right[k], (16 - k).ToString(CultureInfo.InvariantCulture), 6, -7 + 2 * k));
            }

            drv.HalfWidth = 5;
            drv.HalfHeight = 8;
            lib.Add(drv);
        }

        private static void Preset(PartLibrary lib, string id, string name, string partId, string description, string parameters = "")
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in parameters.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0) map[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim();
            }

            lib.AddPreset(new PartPreset(id, name, partId, StandardParts.Electromechanical, description, map));
        }
    }
}
