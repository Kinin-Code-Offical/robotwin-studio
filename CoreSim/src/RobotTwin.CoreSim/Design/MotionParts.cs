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

            var drv = new PartDefinition
            {
                Id = A4988Id,
                Name = "A4988 Step Motor Sürücü",
                Category = StandardParts.Electromechanical,
                RefPrefix = "U",
                Symbol = SymbolKind.Module,
                Footprint = "HDR-1x16",
                ValueKey = "model",
                Description = "STEP'in her yükselen kenarında bir (mikro) adım, DIR yönü verir; MS1–MS3 tam adımdan 1/16'ya. EN LOW iken çalışır. VMOT 8–35 V, VDD 3–5,5 V; akım sınırı kartın Vref ayarıdır.",
                Build = c =>
                {
                    int gnd = c.Pin("GND");
                    c.Add(new Resistor(c.Sub("GNDTIE"), gnd, c.Pin("GND2"), 0.001));
                    c.Add(new A4988Driver(c.RefDes, c.Pin("VMOT"), gnd, c.Pin("1A"), c.Pin("1B"), c.Pin("2A"), c.Pin("2B"), c.Pin("VDD"),
                        c.Pin("STEP"), c.Pin("DIR"), c.Pin("EN"), c.Pin("MS1"), c.Pin("MS2"), c.Pin("MS3"), c.Pin("SLP"), c.Pin("RST"))
                    {
                        CurrentLimit = c.Num("imax", 1.0),
                        OnResistance = c.Num("rds", 0.55)
                    });
                }
            };
            drv.Parameters.Add(new ParameterDefinition("model", "Model", "A4988", ""));
            drv.Parameters.Add(new ParameterDefinition("imax", "Akım sınırı (Vref / 8·Rs)", "1.0", "A"));
            drv.Parameters.Add(new ParameterDefinition("rds", "Köprü direnci (üst + alt)", "0.55", "Ω"));
            string[] left = { "EN", "MS1", "MS2", "MS3", "RST", "SLP", "STEP", "DIR" };
            string[] right = { "VMOT", "GND", "2B", "2A", "1A", "1B", "VDD", "GND2" };
            for (int k = 0; k < 8; k++)
            {
                drv.Pins.Add(new PinDefinition(left[k], (k + 1).ToString(CultureInfo.InvariantCulture), -6, -7 + 2 * k));
                drv.Pins.Add(new PinDefinition(right[k], (16 - k).ToString(CultureInfo.InvariantCulture), 6, -7 + 2 * k));
            }

            drv.HalfWidth = 5;
            drv.HalfHeight = 8;
            lib.Add(drv);
            Preset(lib, "a4988", "A4988 Step Motor Sürücü", drv.Id, "1/16 mikro adım, 1 A (soğutucusuz), 8–35 V");
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
