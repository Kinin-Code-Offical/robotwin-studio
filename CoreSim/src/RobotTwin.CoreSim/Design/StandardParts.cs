using System;
using System.Collections.Generic;
using System.Globalization;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>Built-in component catalog.</summary>
    internal static partial class StandardParts   // [objs] lights in Design/StandardParts.Lights.cs
    {
        public const string Passive = "Pasif";
        public const string Semiconductor = "Yarı İletken";
        public const string Analog = "Analog Entegre";
        public const string Power = "Güç";
        public const string Switching = "Anahtar & Röle";
        public const string Electromechanical = "Elektromekanik";
        public const string Sensors = "Sensör";
        public const string Logic = "Lojik";
        public const string Connectors = "Bağlantı";
        public const string Instruments = "Ölçüm & Kaynak";
        public const string Boards = "Geliştirme Kartı";
        public const string Displays = "Ekran & I²C Modül";

        public static void Register(PartLibrary lib)
        {
            RegisterPassives(lib);
            RegisterSemiconductors(lib);
            RegisterIntegrated(lib);
            RegisterPower(lib);
            RegisterSwitching(lib);
            RegisterElectromechanical(lib);
            RegisterLights(lib);   // [objs]
            RegisterSensors(lib);
            RegisterDisplays(lib);
            Esp32Parts.Register(lib);
            WirelessParts.Register(lib);   // [radio] wireless / USB parts (Design/WirelessParts.cs)
            SensingParts.Register(lib);    // [sense] line sensors, cameras, LiDAR, GPS, compass, encoder motors (Design/SensingParts.cs)
            BusParts.Register(lib);        // SPI / I²C parts: MCP3008 ADC, BMP280 barometer (Design/BusParts.cs)
            CellParts.Register(lib);   // [arm] robot cell parts (Design/CellParts.cs)
            AvionicsParts.Register(lib);   // [aero] barometers, high-g IMU, depth sensor, e-match, 2212 motor (Design/AvionicsParts.cs)
            RegisterConnectors(lib);
            RegisterBoards(lib);
        }

        // ------------------------------------------------------------------ helpers

        private static PartDefinition Def(PartLibrary lib, string id, string name, string category, string prefix,
            SymbolKind symbol, string footprint, string valueKey, Action<PartBuildContext>? build,
            params ParameterDefinition[] parameters)
        {
            var part = new PartDefinition
            {
                Id = id,
                Name = name,
                Category = category,
                RefPrefix = prefix,
                Symbol = symbol,
                Footprint = footprint,
                ValueKey = valueKey,
                Build = build
            };
            part.Parameters.AddRange(parameters);
            lib.Add(part);
            return part;
        }

        private static PartDefinition TwoPin(this PartDefinition part, string a = "A", string b = "B", string padA = "1", string padB = "2")
        {
            part.Pins.Add(new PinDefinition(a, padA, -3, 0));
            part.Pins.Add(new PinDefinition(b, padB, 3, 0));
            part.HalfWidth = 2;
            part.HalfHeight = 1;
            return part;
        }

        private static PartDefinition Vertical(this PartDefinition part, string top = "+", string bottom = "-")
        {
            part.Pins.Add(new PinDefinition(top, "1", 0, -3));
            part.Pins.Add(new PinDefinition(bottom, "2", 0, 3));
            part.HalfWidth = 1.5;
            part.HalfHeight = 2;
            return part;
        }

        private static PartDefinition Pin(this PartDefinition part, string name, string pad, double x, double y)
        {
            part.Pins.Add(new PinDefinition(name, pad, x, y));
            return part;
        }

        private static PartDefinition Size(this PartDefinition part, double halfWidth, double halfHeight)
        {
            part.HalfWidth = halfWidth;
            part.HalfHeight = halfHeight;
            return part;
        }

        private static ParameterDefinition P(string key, string label, string value, string unit = "", params string[] choices) =>
            new ParameterDefinition(key, label, value, unit, choices.Length > 0 ? choices : null);

        private static void Preset(PartLibrary lib, string id, string name, string partId, string category,
            string description, string parameters = "")
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in parameters.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                map[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim();
            }

            lib.AddPreset(new PartPreset(id, name, partId, category, description, map));
        }

        private static string Inv(double v) => v.ToString("G6", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ passives

        private static void RegisterPassives(PartLibrary lib)
        {
            Def(lib, "resistor", "Direnç", Passive, "R", SymbolKind.Resistor, "AXIAL-400", "resistance",
                c => c.Add(new Resistor(c.RefDes, c.Pin("A"), c.Pin("B"), c.Num("resistance", 1000))
                {
                    PowerRating = c.Num("power", 0.25),
                    TempCoefficient = c.Num("tempco", 100) * 1e-6
                }),
                P("resistance", "Direnç", "1k", "Ω"), P("power", "Güç", "0.25", "W"),
                P("tolerance", "Tolerans", "5", "%"), P("tempco", "Sıcaklık katsayısı", "100", "ppm/K"))
                .TwoPin();

            Def(lib, "potentiometer", "Potansiyometre", Passive, "RV", SymbolKind.Potentiometer, "POT-RV09", "resistance",
                c => c.Add(new Potentiometer(c.RefDes, c.Pin("A"), c.Pin("W"), c.Pin("B"), c.Num("resistance", 10000))
                {
                    Position = c.Num("position", 0.5),
                    Taper = c.Text("taper") == "log" ? PotTaper.Logarithmic : PotTaper.Linear
                }),
                P("resistance", "Direnç", "10k", "Ω"), P("position", "Konum", "0.5", "0-1"),
                P("taper", "Eğri", "linear", "", "linear", "log"), P("tolerance", "Tolerans", "20", "%"))
                .Pin("A", "1", -3, 0).Pin("W", "2", 0, -2.5).Pin("B", "3", 3, 0).Size(2, 1.5);

            Def(lib, "ntc", "NTC Termistör", Passive, "TH", SymbolKind.Thermistor, "RADIAL-2.5", "resistance",
                c => c.Add(new Thermistor(c.RefDes, c.Pin("A"), c.Pin("B"), c.Num("resistance", 10000), c.Num("beta", 3950))
                {
                    SensedTempC = c.Num("temperature", 25)
                }),
                P("resistance", "R25", "10k", "Ω"), P("beta", "Beta", "3950", "K"), P("temperature", "Ortam sıcaklığı", "25", "°C"),
                P("tolerance", "Tolerans", "5", "%"))
                .TwoPin();

            Def(lib, "ldr", "LDR (Fotodirenç)", Passive, "LDR", SymbolKind.Photoresistor, "RADIAL-2.5", "resistance",
                c => c.Add(new Photoresistor(c.RefDes, c.Pin("A"), c.Pin("B"), c.Num("resistance", 10000), c.Num("gamma", 0.7))
                {
                    Lux = c.Num("lux", 100)
                }),
                P("resistance", "R @10 lux", "10k", "Ω"), P("gamma", "Gamma", "0.7"), P("lux", "Aydınlık", "100", "lux"),
                P("tolerance", "Tolerans", "30", "%"))
                .TwoPin();

            Def(lib, "fuse", "Sigorta", Passive, "F", SymbolKind.Fuse, "AXIAL-600", "rating",
                c => c.Add(new Fuse(c.RefDes, c.Pin("A"), c.Pin("B"), c.Num("rating", 1))
                {
                    TimeConstant = c.Text("speed") == "slow" ? 5.0 : 0.1
                }),
                P("rating", "Akım", "1", "A"), P("speed", "Tip", "fast", "", "fast", "slow"))
                .TwoPin();

            Def(lib, "capacitor", "Kondansatör", Passive, "C", SymbolKind.Capacitor, "RADIAL-2.5", "capacitance",
                c => c.Add(new Capacitor(c.RefDes, c.Pin("A"), c.Pin("B"), c.Num("capacitance", 100e-9))
                {
                    VoltageRating = c.Num("voltage", 50),
                    SeriesResistance = c.Num("esr", 0)
                }),
                P("capacitance", "Kapasite", "100n", "F"), P("voltage", "Gerilim", "50", "V"), P("esr", "ESR", "0", "Ω"),
                P("tolerance", "Tolerans", "10", "%"), P("dielectric", "Dielektrik", "X7R", "", "C0G", "X7R", "X5R", "Y5V", "Z5U"))
                .TwoPin();

            Def(lib, "electrolytic", "Elektrolitik Kondansatör", Passive, "C", SymbolKind.PolarCapacitor, "CP-5-2.0", "capacitance",
                c => c.Add(new Capacitor(c.RefDes, c.Pin("+"), c.Pin("-"), c.Num("capacitance", 100e-6))
                {
                    VoltageRating = c.Num("voltage", 16),
                    SeriesResistance = c.Num("esr", 0.1),
                    LeakageResistance = 1e7,
                    Polarized = true
                }),
                P("capacitance", "Kapasite", "100u", "F"), P("voltage", "Gerilim", "16", "V"), P("esr", "ESR", "0.1", "Ω"),
                P("tolerance", "Tolerans", "-20/+80", "%"))
                .TwoPin("+", "-");

            Def(lib, "inductor", "Bobin", Passive, "L", SymbolKind.Inductor, "AXIAL-500", "inductance",
                c => c.Add(new Inductor(c.RefDes, c.Pin("A"), c.Pin("B"), c.Num("inductance", 100e-6))
                {
                    SeriesResistance = Math.Max(1e-3, c.Num("dcr", 0.1))
                }),
                P("inductance", "Endüktans", "100u", "H"), P("dcr", "DC direnç", "0.1", "Ω"), P("current", "Akım", "1", "A"),
                P("tolerance", "Tolerans", "20", "%"))
                .TwoPin();

            double[] e12 = { 10, 12, 15, 18, 22, 27, 33, 39, 47, 56, 68, 82 };
            for (int decade = 0; decade < 5; decade++)
            {
                foreach (double mantissa in e12)
                {
                    double r = mantissa * Math.Pow(10, decade);
                    string label = SiValue.Format(r, "Ω");
                    Preset(lib, "res-" + Inv(r), "Direnç " + label, "resistor", Passive,
                        "Karbon film, 1/4 W, %5", "resistance=" + Inv(r));
                }
            }

            Preset(lib, "res-1M", "Direnç 1 MΩ", "resistor", Passive, "Karbon film, 1/4 W, %5", "resistance=1M");
            Preset(lib, "res-1R-1W", "Direnç 1 Ω 1 W", "resistor", Passive, "Akım ölçme, metal film, %1", "resistance=1;power=1;tolerance=1;footprint=AXIAL-600");
            Preset(lib, "res-10R-2W", "Direnç 10 Ω 2 W", "resistor", Passive, "Güç direnci", "resistance=10;power=2;footprint=AXIAL-800");
            Preset(lib, "res-220-0805", "Direnç 220 Ω (0805 SMD)", "resistor", Passive, "SMD 0805, 1/8 W, %1", "resistance=220;power=0.125;tolerance=1;footprint=R0805");
            Preset(lib, "res-10k-0805", "Direnç 10 kΩ (0805 SMD)", "resistor", Passive, "SMD 0805, 1/8 W, %1", "resistance=10k;power=0.125;tolerance=1;footprint=R0805");

            foreach (var (value, name) in new[] { ("1k", "1 kΩ"), ("10k", "10 kΩ"), ("100k", "100 kΩ") })
            {
                Preset(lib, "pot-" + value, "Potansiyometre " + name, "potentiometer", Passive, "Döner, doğrusal", "resistance=" + value);
            }

            Preset(lib, "trimpot-10k", "Trimpot 10 kΩ (3296)", "potentiometer", Passive, "Çok turlu ayar trimpotu", "resistance=10k;footprint=TRIM-3296");
            Preset(lib, "ntc-10k", "NTC 10 kΩ (B3950)", "ntc", Passive, "Sıcaklık ölçümü için NTC", "resistance=10k;beta=3950");
            Preset(lib, "ldr-gl5528", "LDR GL5528", "ldr", Passive, "5 mm fotodirenç, 10-20 kΩ @10 lux", "resistance=15k;gamma=0.7");
            Preset(lib, "fuse-500mA", "Sigorta 500 mA", "fuse", Passive, "Hızlı atan cam sigorta", "rating=0.5");
            Preset(lib, "fuse-2A-slow", "Sigorta 2 A (gecikmeli)", "fuse", Passive, "Gecikmeli, motor devreleri için", "rating=2;speed=slow");
            Preset(lib, "polyfuse-500mA", "PTC Sigorta 500 mA", "fuse", Passive, "Kendini sıfırlayan polyfuse", "rating=0.5;speed=slow;footprint=RADIAL-5.0");

            foreach (var (value, label) in new[] { ("10p", "10 pF"), ("22p", "22 pF"), ("100p", "100 pF"), ("1n", "1 nF"), ("10n", "10 nF"), ("100n", "100 nF"), ("1u", "1 µF") })
            {
                bool c0g = SiValue.Parse(value, 1) <= 1e-9;
                Preset(lib, "cap-" + value, "Seramik " + label, "capacitor", Passive,
                    c0g ? "Seramik C0G, 50 V, %5" : "Seramik X7R, 50 V, %10",
                    "capacitance=" + value + (c0g ? ";dielectric=C0G;tolerance=5" : ";dielectric=X7R;tolerance=10"));
            }

            Preset(lib, "cap-1u-y5v", "Seramik 1 µF (Y5V)", "capacitor", Passive, "Ucuz Y5V seramik: −20/+80 %, sıcaklıkla −82 %'ye kadar düşer",
                "capacitance=1u;dielectric=Y5V;tolerance=-20/+80;voltage=16");

            foreach (var (value, label, volts, fp) in new[]
            {
                ("1u", "1 µF", "50", "CP-5-2.0"), ("4.7u", "4.7 µF", "50", "CP-5-2.0"), ("10u", "10 µF", "25", "CP-5-2.0"),
                ("47u", "47 µF", "25", "CP-5-2.0"), ("100u", "100 µF", "16", "CP-6.3-2.5"), ("220u", "220 µF", "16", "CP-6.3-2.5"),
                ("470u", "470 µF", "25", "CP-8-3.5"), ("1000u", "1000 µF", "16", "CP-10-5.0"), ("2200u", "2200 µF", "25", "CP-13-5.0")
            })
            {
                string esr = SiValue.Parse(value, 1e-6) >= 470e-6 ? "0.05" : "0.3";
                Preset(lib, "ecap-" + value, "Elektrolitik " + label + " " + volts + " V", "electrolytic", Passive,
                    "Alüminyum elektrolitik", $"capacitance={value};voltage={volts};esr={esr};footprint={fp}");
            }

            foreach (var (value, label) in new[] { ("10u", "10 µH"), ("100u", "100 µH"), ("1m", "1 mH"), ("10m", "10 mH") })
            {
                Preset(lib, "ind-" + value, "Bobin " + label, "inductor", Passive, "Radyal/eksenel güç bobini", "inductance=" + value);
            }
        }

        // ------------------------------------------------------------------ discrete semiconductors

        private static void RegisterSemiconductors(PartLibrary lib)
        {
            Action<PartBuildContext> diode = c => c.Add(new Diode(c.RefDes, c.Pin("A"), c.Pin("K"))
            {
                SaturationCurrent = c.Num("is", 2.5e-9),
                Ideality = c.Num("n", 1.75),
                SeriesResistance = c.Num("rs", 0.5),
                BreakdownVoltage = c.Num("bv", 0),
                BreakdownCurrent = c.Num("ibv", 5e-3),
                BandGap = c.Num("eg", 1.11),
                JunctionCapacitance = c.Num("cj", 0),
                MaxCurrent = c.Num("imax", 0.3),
                ThermalResistance = c.Num("rth", 300)
            });

            Def(lib, "diode", "Diyot", Semiconductor, "D", SymbolKind.Diode, "DO35", "model", diode,
                P("model", "Model", "1N4148"), P("is", "Is", "2.52n", "A"), P("n", "N", "1.752"), P("rs", "Rs", "0.568", "Ω"),
                P("bv", "Ters kırılma", "100", "V"), P("ibv", "Ibv", "100u", "A"), P("eg", "Eg", "1.11", "eV"),
                P("cj", "Cj", "4p", "F"), P("imax", "Maks. akım", "0.3", "A"), P("rth", "Rth", "350", "K/W"))
                .TwoPin("A", "K", "2", "1");

            Def(lib, "schottky", "Schottky Diyot", Semiconductor, "D", SymbolKind.Schottky, "DO41", "model", diode,
                P("model", "Model", "1N5819"), P("is", "Is", "31.7u", "A"), P("n", "N", "1.373"), P("rs", "Rs", "0.051", "Ω"),
                P("bv", "Ters kırılma", "40", "V"), P("ibv", "Ibv", "1m", "A"), P("eg", "Eg", "0.69", "eV"),
                P("cj", "Cj", "100p", "F"), P("imax", "Maks. akım", "1", "A"), P("rth", "Rth", "80", "K/W"))
                .TwoPin("A", "K", "2", "1");

            Def(lib, "zener", "Zener Diyot", Semiconductor, "D", SymbolKind.Zener, "DO35", "voltage",
                c => c.Add(new Diode(c.RefDes, c.Pin("A"), c.Pin("K"))
                {
                    SaturationCurrent = 1e-14,
                    Ideality = 1.0,
                    SeriesResistance = c.Num("rz", 2),
                    BreakdownVoltage = c.Num("voltage", 5.1),
                    BreakdownCurrent = c.Num("izt", 5e-3),
                    MaxCurrent = c.Num("power", 0.5) / Math.Max(1.0, c.Num("voltage", 5.1)),
                    ThermalResistance = 120.0 / Math.Max(0.1, c.Num("power", 0.5)) * 0.5
                }),
                P("voltage", "Zener gerilimi", "5.1", "V"), P("izt", "Test akımı", "5m", "A"), P("rz", "Dinamik direnç", "2", "Ω"),
                P("power", "Güç", "0.5", "W"), P("tolerance", "Tolerans", "5", "%"))
                .TwoPin("A", "K", "2", "1");

            Def(lib, "led", "LED", Semiconductor, "D", SymbolKind.Led, "LED-5MM", "color",
                c => c.Add(new Led(c.RefDes, c.Pin("A"), c.Pin("K"), c.Num("vf", 2.0), c.Num("if", 0.02))),
                P("color", "Renk", "red", "", "red", "orange", "yellow", "green", "blue", "white", "ir", "uv"),
                P("vf", "İleri gerilim", "2.0", "V"), P("if", "Nominal akım", "20m", "A"), P("vftol", "Vf dağılımı", "5", "%"))
                .TwoPin("A", "K", "2", "1").Size(2, 2.4);

            Preset(lib, "1n4148", "1N4148", "diode", Semiconductor, "Hızlı anahtarlama diyodu, 100 V 300 mA");
            Preset(lib, "1n4007", "1N4007", "diode", Semiconductor, "Doğrultucu diyot, 1000 V 1 A",
                "model=1N4007;is=7.03n;n=1.808;rs=0.034;bv=1000;ibv=5u;cj=10p;imax=1;rth=80;footprint=DO41");
            Preset(lib, "1n5408", "1N5408", "diode", Semiconductor, "Doğrultucu diyot, 1000 V 3 A",
                "model=1N5408;is=14n;n=1.9;rs=0.012;bv=1000;ibv=5u;cj=40p;imax=3;rth=40;footprint=DO201");
            Preset(lib, "1n5819", "1N5819", "schottky", Semiconductor, "Schottky, 40 V 1 A");
            Preset(lib, "1n5822", "1N5822", "schottky", Semiconductor, "Schottky, 40 V 3 A",
                "model=1N5822;is=60u;n=1.3;rs=0.02;imax=3;rth=40;footprint=DO201");
            Preset(lib, "bat54", "BAT54 (SOT-23)", "schottky", Semiconductor, "Küçük sinyal Schottky, 30 V 200 mA",
                "model=BAT54;is=0.3u;n=1.05;rs=2;bv=30;imax=0.2;rth=500;footprint=SOT23;pinmap=A:1,K:3");

            foreach (var (code, volts, izt) in new[]
            {
                ("1N4728A", 3.3, 76e-3), ("1N4732A", 4.7, 53e-3), ("1N4733A", 5.1, 49e-3), ("1N4735A", 6.2, 41e-3),
                ("1N4739A", 9.1, 28e-3), ("1N4742A", 12.0, 21e-3), ("1N4744A", 15.0, 17e-3)
            })
            {
                Preset(lib, code.ToLowerInvariant(), code, "zener", Semiconductor, $"Zener {volts.ToString(CultureInfo.InvariantCulture)} V 1 W",
                    $"voltage={Inv(volts)};izt={Inv(izt)};power=1;footprint=DO41");
            }

            Preset(lib, "bzx55c5v1", "BZX55C5V1", "zener", Semiconductor, "Zener 5.1 V 0.5 W", "voltage=5.1;izt=5m;power=0.5");
            Preset(lib, "bzx55c3v3", "BZX55C3V3", "zener", Semiconductor, "Zener 3.3 V 0.5 W", "voltage=3.3;izt=5m;power=0.5");

            foreach (var (color, name, vf) in new[]
            {
                ("red", "Kırmızı", 2.0), ("orange", "Turuncu", 2.05), ("yellow", "Sarı", 2.1), ("green", "Yeşil", 2.2),
                ("blue", "Mavi", 3.1), ("white", "Beyaz", 3.1), ("uv", "UV", 3.3), ("ir", "Kızılötesi (IR)", 1.25)
            })
            {
                Preset(lib, "led5-" + color, "LED 5 mm " + name, "led", Semiconductor, $"5 mm LED, Vf ≈ {Inv(vf)} V @ 20 mA",
                    $"color={color};vf={Inv(vf)};if=20m");
            }

            Preset(lib, "led3-red", "LED 3 mm Kırmızı", "led", Semiconductor, "3 mm LED", "color=red;vf=2.0;footprint=LED-3MM");
            Preset(lib, "led3-green", "LED 3 mm Yeşil", "led", Semiconductor, "3 mm LED", "color=green;vf=2.2;footprint=LED-3MM");
            Preset(lib, "led-0805-red", "LED 0805 Kırmızı", "led", Semiconductor, "SMD LED", "color=red;vf=2.0;if=10m;footprint=LED0805");

            Action<PartBuildContext> Bjt(Polarity polarity) => c => c.Add(new Bjt(c.RefDes, c.Pin("C"), c.Pin("B"), c.Pin("E"), polarity)
            {
                SaturationCurrent = c.Num("is", 1e-14),
                ForwardBeta = c.Num("bf", 200),
                ReverseBeta = c.Num("br", 2),
                EarlyVoltage = c.Num("va", 100),
                MaxCollectorCurrent = c.Num("icmax", 0.5),
                MaxCollectorEmitterVoltage = c.Num("vceo", 40),
                ThermalResistance = c.Num("rth", 200),
                BaseEmitterCapacitance = c.Num("cje", 0),
                BaseCollectorCapacitance = c.Num("cjc", 0)
            });

            ParameterDefinition[] bjtParams =
            {
                P("model", "Model", "2N2222A"), P("is", "Is", "14.34f", "A"), P("bf", "hFE", "255.9"), P("br", "Ters β", "6.09"),
                P("va", "Early gerilimi", "74", "V"), P("icmax", "Maks. Ic", "0.8", "A"), P("vceo", "Vceo", "40", "V"),
                P("rth", "Rth", "200", "K/W"), P("cje", "Cbe", "22p", "F"), P("cjc", "Cbc", "7p", "F"), P("pinout", "Bacak sırası", "EBC"),
                P("hfespread", "hFE dağılımı", "40", "%")
            };

            Def(lib, "npn", "NPN Transistör", Semiconductor, "Q", SymbolKind.Npn, "TO92", "model", Bjt(Polarity.N), bjtParams)
                .Pin("B", "2", -3, 0).Pin("C", "3", 1, -3).Pin("E", "1", 1, 3).Size(2, 2);
            Def(lib, "pnp", "PNP Transistör", Semiconductor, "Q", SymbolKind.Pnp, "TO92", "model", Bjt(Polarity.P), bjtParams)
                .Pin("B", "2", -3, 0).Pin("E", "1", 1, -3).Pin("C", "3", 1, 3).Size(2, 2);

            Def(lib, "darlington", "Darlington Transistör", Semiconductor, "Q", SymbolKind.Darlington, "TO220", "model",
                c =>
                {
                    int mid = c.Internal("mid");
                    c.Add(new Bjt(c.Sub("Q1"), c.Pin("C"), c.Pin("B"), mid) { ForwardBeta = c.Num("bf1", 150), EarlyVoltage = 100 });
                    c.Add(new Bjt(c.Sub("Q2"), c.Pin("C"), mid, c.Pin("E"))
                    {
                        ForwardBeta = c.Num("bf2", 40),
                        SaturationCurrent = 1e-12,
                        EarlyVoltage = 100,
                        MaxCollectorCurrent = c.Num("icmax", 5),
                        MaxCollectorEmitterVoltage = c.Num("vceo", 60),
                        ThermalResistance = c.Num("rth", 62.5)
                    });
                    c.Add(new Resistor(c.Sub("R1"), mid, c.Pin("E"), 8000));
                },
                P("model", "Model", "TIP120"), P("bf1", "β1", "150"), P("bf2", "β2", "40"), P("icmax", "Maks. Ic", "5", "A"),
                P("vceo", "Vceo", "60", "V"), P("rth", "Rth", "62.5", "K/W"), P("pinout", "Bacak sırası", "BCE"),
                P("hfespread", "hFE dağılımı", "40", "%"))
                .Pin("B", "1", -3, 0).Pin("C", "2", 1, -3).Pin("E", "3", 1, 3).Size(2, 2);

            Preset(lib, "2n2222a", "2N2222A", "npn", Semiconductor, "Genel amaçlı NPN, 40 V 800 mA");
            Preset(lib, "2n3904", "2N3904", "npn", Semiconductor, "Küçük sinyal NPN, 40 V 200 mA",
                "model=2N3904;is=6.734f;bf=416.4;br=0.737;va=74;icmax=0.2;cje=4.5p;cjc=3.6p");
            Preset(lib, "bc547b", "BC547B", "npn", Semiconductor, "Küçük sinyal NPN, 45 V 100 mA",
                "model=BC547B;is=7.05f;bf=375;br=0.7;va=62;icmax=0.1;vceo=45;cje=11p;cjc=4p;pinout=CBE");
            Preset(lib, "s8050", "S8050", "npn", Semiconductor, "NPN, 25 V 500 mA", "model=S8050;is=10f;bf=300;va=60;icmax=0.5;vceo=25");
            Preset(lib, "2n3906", "2N3906", "pnp", Semiconductor, "Küçük sinyal PNP, 40 V 200 mA",
                "model=2N3906;is=1.41f;bf=180.7;br=4.98;va=18.7;icmax=0.2;cje=4.5p;cjc=4.5p");
            Preset(lib, "bc557b", "BC557B", "pnp", Semiconductor, "Küçük sinyal PNP, 45 V 100 mA",
                "model=BC557B;is=10f;bf=300;va=50;icmax=0.1;vceo=45;pinout=CBE");
            Preset(lib, "s8550", "S8550", "pnp", Semiconductor, "PNP, 25 V 500 mA", "model=S8550;is=10f;bf=300;va=60;icmax=0.5;vceo=25");
            Preset(lib, "tip31c", "TIP31C", "npn", Semiconductor, "Güç NPN, 100 V 3 A (TO-220)",
                "model=TIP31C;is=1e-12;bf=50;va=100;icmax=3;vceo=100;rth=62.5;pinout=BCE;footprint=TO220");
            Preset(lib, "tip32c", "TIP32C", "pnp", Semiconductor, "Güç PNP, 100 V 3 A (TO-220)",
                "model=TIP32C;is=1e-12;bf=50;va=100;icmax=3;vceo=100;rth=62.5;pinout=BCE;footprint=TO220");
            Preset(lib, "bd139", "BD139", "npn", Semiconductor, "Orta güç NPN, 80 V 1.5 A (TO-126)",
                "model=BD139;is=1e-13;bf=100;va=100;icmax=1.5;vceo=80;rth=100;pinout=ECB;footprint=TO126");
            Preset(lib, "tip120", "TIP120", "darlington", Semiconductor, "Darlington NPN, 60 V 5 A, hFE ≥ 1000");
            Preset(lib, "tip122", "TIP122", "darlington", Semiconductor, "Darlington NPN, 100 V 5 A", "model=TIP122;vceo=100");

            Action<PartBuildContext> Fet(Polarity polarity) => c =>
            {
                var m = c.Add(new Mosfet(c.RefDes, c.Pin("D"), c.Pin("G"), c.Pin("S"), polarity)
                {
                    Threshold = c.Num("vth", 2),
                    MaxDrainSourceVoltage = c.Num("vds", 60),
                    MaxGateSourceVoltage = c.Num("vgsmax", 20),
                    ThermalResistance = c.Num("rth", 62),
                    GateSourceCapacitance = c.Num("cgs", 0),
                    GateDrainCapacitance = c.Num("cgd", 0)
                });
                m.SetOnResistance(c.Num("rdson", 0.1), c.Num("vgsrdson", 10));
            };

            ParameterDefinition[] fetParams =
            {
                P("model", "Model", "IRLZ44N"), P("vth", "Eşik gerilimi", "1.5", "V"), P("rdson", "Rds(on)", "0.022", "Ω"),
                P("vgsrdson", "Rds(on) Vgs", "5", "V"), P("vds", "Maks. Vds", "55", "V"), P("vgsmax", "Maks. Vgs", "16", "V"),
                P("rth", "Rth", "62", "K/W"), P("cgs", "Cgs", "1.5n", "F"), P("cgd", "Cgd", "0.2n", "F"), P("pinout", "Bacak sırası", "GDS"),
                P("vthspread", "Vth dağılımı", "25", "%")
            };

            Def(lib, "nmos", "N-Kanal MOSFET", Semiconductor, "Q", SymbolKind.Nmos, "TO220", "model", Fet(Polarity.N), fetParams)
                .Pin("G", "1", -3, 0).Pin("D", "2", 1, -3).Pin("S", "3", 1, 3).Size(2, 2);
            Def(lib, "pmos", "P-Kanal MOSFET", Semiconductor, "Q", SymbolKind.Pmos, "TO220", "model", Fet(Polarity.P), fetParams)
                .Pin("G", "1", -3, 0).Pin("S", "3", 1, -3).Pin("D", "2", 1, 3).Size(2, 2);

            Preset(lib, "irlz44n", "IRLZ44N", "nmos", Semiconductor, "Lojik seviye N-MOSFET, 55 V 47 A (Arduino ile sürülebilir)");
            Preset(lib, "irfz44n", "IRFZ44N", "nmos", Semiconductor, "N-MOSFET, 55 V 49 A (10 V kapı gerekir)",
                "model=IRFZ44N;vth=3;rdson=0.0175;vgsrdson=10;vgsmax=20;cgs=1.3n;cgd=0.2n");
            Preset(lib, "irf540n", "IRF540N", "nmos", Semiconductor, "N-MOSFET, 100 V 33 A",
                "model=IRF540N;vth=3;rdson=0.044;vgsrdson=10;vds=100;vgsmax=20;cgs=1.8n;cgd=0.15n");
            Preset(lib, "2n7000", "2N7000", "nmos", Semiconductor, "Küçük sinyal N-MOSFET, 60 V 200 mA (TO-92)",
                "model=2N7000;vth=2.1;rdson=1.8;vgsrdson=10;vds=60;vgsmax=20;rth=312;cgs=20p;cgd=5p;pinout=SGD;footprint=TO92");
            Preset(lib, "bs170", "BS170", "nmos", Semiconductor, "Küçük sinyal N-MOSFET, 60 V 500 mA (TO-92)",
                "model=BS170;vth=2.1;rdson=1.2;vgsrdson=10;vds=60;vgsmax=20;rth=312;cgs=24p;cgd=5p;pinout=DGS;footprint=TO92");
            Preset(lib, "ao3400", "AO3400 (SOT-23)", "nmos", Semiconductor, "Lojik seviye N-MOSFET, 30 V 5.7 A",
                "model=AO3400;vth=1.05;rdson=0.033;vgsrdson=4.5;vds=30;vgsmax=12;rth=125;cgs=0.6n;cgd=0.08n;pinout=GSD;footprint=SOT23");
            Preset(lib, "irf9540n", "IRF9540N", "pmos", Semiconductor, "P-MOSFET, -100 V -23 A",
                "model=IRF9540N;vth=3;rdson=0.117;vgsrdson=10;vds=100;vgsmax=20;cgs=1.2n;cgd=0.15n");
            Preset(lib, "ao3401", "AO3401 (SOT-23)", "pmos", Semiconductor, "Lojik seviye P-MOSFET, -30 V -4 A",
                "model=AO3401;vth=0.9;rdson=0.06;vgsrdson=4.5;vds=30;vgsmax=12;rth=125;cgs=0.6n;cgd=0.08n;pinout=GSD;footprint=SOT23");
        }

        // ------------------------------------------------------------------ integrated circuits

        private static void RegisterIntegrated(PartLibrary lib)
        {
            Def(lib, "opamp", "Op-Amp", Analog, "U", SymbolKind.OpAmp, "DIP-8", "model",
                c => c.Add(new OpAmp(c.RefDes, c.Pin("IN+"), c.Pin("IN-"), c.Pin("OUT"), c.Pin("V+"), c.Pin("V-"))
                {
                    GainBandwidth = c.Num("gbw", 1e6),
                    SlewRate = c.Num("slew", 0.3) * 1e6,
                    SwingLow = c.Num("swinglow", 0.02),
                    SwingHigh = c.Num("swinghigh", 1.5),
                    OutputCurrentLimit = c.Num("ilim", 0.04),
                    QuiescentCurrent = c.Num("iq", 0.35e-3),
                    OffsetVoltage = c.Num("vos", 0)
                }),
                P("model", "Model", "LM358"), P("gbw", "Kazanç-bant genişliği", "1M", "Hz"), P("slew", "Yükselme hızı", "0.3", "V/µs"),
                P("swinglow", "Alt ray payı", "0.02", "V"), P("swinghigh", "Üst ray payı", "1.5", "V"), P("ilim", "Çıkış akım sınırı", "40m", "A"),
                P("iq", "Boşta akım", "0.35m", "A"), P("vos", "Ofset", "0", "V"), P("vosmax", "Ofset dağılımı (en çok)", "3m", "V"),
                P("pinmap", "Pin eşleme", "OUT:1,IN-:2,IN+:3,V-:4,V+:8"))
                .Pin("IN-", "2", -4, -1).Pin("IN+", "3", -4, 1).Pin("OUT", "1", 4, 0).Pin("V+", "8", 0, -3).Pin("V-", "4", 0, 3).Size(3, 2.5);

            Preset(lib, "lm358", "LM358 (1/2)", "opamp", Analog, "Tek beslemeli çift op-amp, 3-32 V");
            Preset(lib, "lm324", "LM324 (1/4)", "opamp", Analog, "Tek beslemeli dörtlü op-amp",
                "model=LM324;pinmap=OUT:1,IN-:2,IN+:3,V+:4,V-:11;footprint=DIP-14");
            Preset(lib, "lm741", "LM741", "opamp", Analog, "Klasik genel amaçlı op-amp, ±15 V",
                "model=LM741;slew=0.5;swinglow=1.5;swinghigh=1.5;ilim=25m;iq=1.7m;pinmap=IN-:2,IN+:3,V-:4,OUT:6,V+:7");
            Preset(lib, "tl072", "TL072 (1/2)", "opamp", Analog, "JFET girişli düşük gürültülü op-amp",
                "model=TL072;gbw=3M;slew=13;swinglow=1.5;swinghigh=1.5;ilim=30m;iq=1.4m");
            Preset(lib, "ne5532", "NE5532 (1/2)", "opamp", Analog, "Ses için düşük gürültülü op-amp",
                "model=NE5532;gbw=10M;slew=9;swinglow=1.8;swinghigh=1.8;ilim=38m;iq=4m");
            Preset(lib, "mcp6002", "MCP6002 (1/2)", "opamp", Analog, "Raydan raya 1.8-6 V op-amp",
                "model=MCP6002;gbw=1M;slew=0.6;swinglow=0.025;swinghigh=0.025;ilim=23m;iq=0.1m");

            Def(lib, "gate2", "Lojik Kapı (2 giriş)", Logic, "U", SymbolKind.Gate, "DIP-14", "model",
                c =>
                {
                    var fn = (LogicFunction)Enum.Parse(typeof(LogicFunction), c.Text("function"), true);
                    c.Add(new LogicGate(c.RefDes, fn, c.Pin("Y"), c.Pin("VCC"), c.Pin("GND"), c.Pin("A"), c.Pin("B"))
                    {
                        PropagationDelay = c.Num("delay", 10e-9)
                    });
                },
                P("model", "Model", "74HC00"), P("function", "Fonksiyon", "Nand", "", "And", "Nand", "Or", "Nor", "Xor", "Xnor"),
                P("delay", "Gecikme", "10n", "s"), P("pinmap", "Pin eşleme", "A:1,B:2,Y:3,GND:7,VCC:14"))
                .Pin("A", "1", -4, -1).Pin("B", "2", -4, 1).Pin("Y", "3", 4, 0).Pin("VCC", "14", 0, -3).Pin("GND", "7", 0, 3).Size(3, 2);

            Def(lib, "gate1", "Lojik Kapı (1 giriş)", Logic, "U", SymbolKind.Gate, "DIP-14", "model",
                c =>
                {
                    var fn = (LogicFunction)Enum.Parse(typeof(LogicFunction), c.Text("function"), true);
                    c.Add(new LogicGate(c.RefDes, fn, c.Pin("Y"), c.Pin("VCC"), c.Pin("GND"), c.Pin("A"))
                    {
                        PropagationDelay = c.Num("delay", 10e-9)
                    });
                },
                P("model", "Model", "74HC04"), P("function", "Fonksiyon", "Not", "", "Not", "Buffer"),
                P("delay", "Gecikme", "8n", "s"), P("pinmap", "Pin eşleme", "A:1,Y:2,GND:7,VCC:14"))
                .Pin("A", "1", -4, 0).Pin("Y", "2", 4, 0).Pin("VCC", "14", 0, -3).Pin("GND", "7", 0, 3).Size(3, 2);

            Preset(lib, "74hc00", "74HC00 NAND", "gate2", Logic, "Dörtlü 2 girişli NAND (1 kapı)");
            Preset(lib, "74hc02", "74HC02 NOR", "gate2", Logic, "Dörtlü 2 girişli NOR (1 kapı)",
                "model=74HC02;function=Nor;pinmap=Y:1,A:2,B:3,GND:7,VCC:14");
            Preset(lib, "74hc08", "74HC08 AND", "gate2", Logic, "Dörtlü 2 girişli AND (1 kapı)", "model=74HC08;function=And");
            Preset(lib, "74hc32", "74HC32 OR", "gate2", Logic, "Dörtlü 2 girişli OR (1 kapı)", "model=74HC32;function=Or");
            Preset(lib, "74hc86", "74HC86 XOR", "gate2", Logic, "Dörtlü 2 girişli XOR (1 kapı)", "model=74HC86;function=Xor");
            Preset(lib, "74hc04", "74HC04 NOT", "gate1", Logic, "Altılı evirici (1 kapı)");
            Preset(lib, "74hc14", "74HC14 NOT (Schmitt)", "gate1", Logic, "Schmitt tetiklemeli evirici (1 kapı)", "model=74HC14;delay=12n");
        }

        // ------------------------------------------------------------------ power

        private static void RegisterPower(PartLibrary lib)
        {
            Def(lib, "regulator", "Lineer Regülatör", Power, "U", SymbolKind.Regulator, "TO220", "model",
                c => c.Add(new LinearRegulator(c.RefDes, c.Pin("IN"), c.Pin("GND"), c.Pin("OUT"), c.Num("vout", 5))
                {
                    Dropout = c.Num("dropout", 2),
                    CurrentLimit = c.Num("ilim", 1.5),
                    QuiescentCurrent = c.Num("iq", 5e-3),
                    MaxInputVoltage = c.Num("vinmax", 35),
                    ThermalResistance = c.Num("rth", 65)
                }),
                P("model", "Model", "LM7805"), P("vout", "Çıkış", "5", "V"), P("dropout", "Düşme gerilimi", "2", "V"),
                P("ilim", "Akım sınırı", "1.5", "A"), P("iq", "Boşta akım", "5m", "A"), P("vinmax", "Maks. giriş", "35", "V"),
                P("rth", "Rth (soğutucusuz)", "65", "K/W"), P("pinout", "Bacak sırası", "IGO"), P("tolerance", "Çıkış toleransı", "2", "%"))
                .Pin("IN", "1", -4, 0).Pin("GND", "2", 0, 3).Pin("OUT", "3", 4, 0).Size(3, 1.5);

            Preset(lib, "lm7805", "LM7805", "regulator", Power, "5 V 1.5 A lineer regülatör (TO-220)");
            Preset(lib, "lm7809", "LM7809", "regulator", Power, "9 V 1.5 A lineer regülatör (TO-220)", "model=LM7809;vout=9");
            Preset(lib, "lm7812", "LM7812", "regulator", Power, "12 V 1.5 A lineer regülatör (TO-220)", "model=LM7812;vout=12");
            Preset(lib, "ams1117-3v3", "AMS1117-3.3", "regulator", Power, "3.3 V 1 A LDO (SOT-223)",
                "model=AMS1117-3.3;vout=3.3;dropout=1.1;ilim=1;vinmax=15;rth=90;pinout=GOI;footprint=SOT223");
            Preset(lib, "ams1117-5v0", "AMS1117-5.0", "regulator", Power, "5 V 1 A LDO (SOT-223)",
                "model=AMS1117-5.0;vout=5;dropout=1.1;ilim=1;vinmax=15;rth=90;pinout=GOI;footprint=SOT223");
            Preset(lib, "ld1117v33", "LD1117V33", "regulator", Power, "3.3 V 800 mA LDO (TO-220)",
                "model=LD1117V33;vout=3.3;dropout=1.1;ilim=0.8;vinmax=15;pinout=GOI");
            Preset(lib, "mcp1700-3v3", "MCP1700-3.3", "regulator", Power, "3.3 V 250 mA düşük akımlı LDO (TO-92)",
                "model=MCP1700-3.3;vout=3.3;dropout=0.18;ilim=0.25;iq=1.6u;vinmax=6;rth=200;pinout=GIO;footprint=TO92");

            Def(lib, "battery", "Batarya", Power, "BT", SymbolKind.Battery, "WIRE-2", "model",
                c =>
                {
                    var chem = (BatteryChemistry)Enum.Parse(typeof(BatteryChemistry), c.Text("chemistry"), true);
                    var b = c.Add(new Battery(c.RefDes, c.Pin("+"), c.Pin("-"), chem, (int)Math.Max(1, c.Num("cells", 1)), c.Num("capacity", 2.5))
                    {
                        InitialStateOfCharge = c.Num("soc", 1)
                    });
                    double rint = c.Num("rint", 0);
                    if (rint > 0) b.InternalResistance = rint;
                },
                P("model", "Model", "AA Alkalin"),
                P("chemistry", "Kimya", "Alkaline", "", Enum.GetNames(typeof(BatteryChemistry))),
                P("cells", "Hücre sayısı", "1"), P("capacity", "Kapasite", "2.5", "Ah"), P("soc", "Doluluk", "1", "0-1"),
                P("rint", "İç direnç (0 = varsayılan)", "0", "Ω"))
                .Vertical();

            Preset(lib, "bat-aa", "AA Pil 1.5 V", "battery", Power, "Alkalin AA, 2500 mAh");
            Preset(lib, "bat-2aa", "2×AA Pil 3 V", "battery", Power, "2 adet alkalin AA", "model=2×AA;cells=2");
            Preset(lib, "bat-4aa", "4×AA Pil 6 V", "battery", Power, "4 adet alkalin AA (robot şasileri)", "model=4×AA;cells=4");
            Preset(lib, "bat-9v", "9 V Pil", "battery", Power, "Alkalin 6LR61, 550 mAh", "model=9V;cells=6;capacity=0.55;rint=1.7");
            Preset(lib, "bat-4nimh", "4×AA NiMH 4.8 V", "battery", Power, "Şarj edilebilir 2000 mAh", "model=4×NiMH;chemistry=NiMH;cells=4;capacity=2");
            Preset(lib, "bat-18650", "18650 Li-ion 3.7 V", "battery", Power, "1S Li-ion 3000 mAh", "model=18650;chemistry=LithiumIon;cells=1;capacity=3");
            Preset(lib, "bat-2s-lipo", "LiPo 2S 7.4 V", "battery", Power, "2S LiPo 2200 mAh", "model=2S LiPo;chemistry=LithiumIon;cells=2;capacity=2.2;rint=0.04");
            Preset(lib, "bat-3s-lipo", "LiPo 3S 11.1 V", "battery", Power, "3S LiPo 2200 mAh", "model=3S LiPo;chemistry=LithiumIon;cells=3;capacity=2.2;rint=0.06");
            Preset(lib, "bat-1s-lipo-300", "LiPo 1S 3.7 V 300 mAh", "battery", Power, "Mikro drone pili", "model=1S LiPo 300;chemistry=LithiumIon;cells=1;capacity=0.3;rint=0.15");
            Preset(lib, "bat-cr2032", "CR2032 3 V", "battery", Power, "Lityum düğme pil 220 mAh", "model=CR2032;chemistry=LithiumCoin;cells=1;capacity=0.22");
            Preset(lib, "bat-12v-sla", "Kurşun Asit 12 V 7 Ah", "battery", Power, "Bakımsız akü", "model=12V SLA;chemistry=LeadAcid;cells=6;capacity=7");
            Preset(lib, "bat-lifepo4-4s", "LiFePO4 4S 12.8 V", "battery", Power, "4S LiFePO4 6 Ah", "model=LiFePO4 4S;chemistry=LiFePO4;cells=4;capacity=6");

            Def(lib, "dc-source", "DC Güç Kaynağı", Instruments, "V", SymbolKind.VoltageSource, "WIRE-2", "voltage",
                c => c.Add(new VoltageSource(c.RefDes, c.Pin("+"), c.Pin("-"), c.Num("voltage", 5))
                {
                    SeriesResistance = c.Num("rs", 0.01)
                }),
                P("voltage", "Gerilim", "5", "V"), P("rs", "İç direnç", "0.01", "Ω"))
                .Vertical();

            Def(lib, "current-source", "Akım Kaynağı", Instruments, "I", SymbolKind.CurrentSource, "", "current",
                c => c.Add(new CurrentSource(c.RefDes, c.Pin("+"), c.Pin("-"), c.Num("current", 0.01))),
                P("current", "Akım", "10m", "A"))
                .Vertical();

            Def(lib, "signal-generator", "Fonksiyon Jeneratörü", Instruments, "V", SymbolKind.SignalSource, "", "frequency",
                c =>
                {
                    double f = Math.Max(1e-3, c.Num("frequency", 1000)), amp = c.Num("amplitude", 5), offset = c.Num("offset", 0);
                    double duty = c.Num("duty", 0.5), edge = Math.Max(1e-12, c.Num("edge", 10e-9));
                    // Amplitude set "into 50 Ω": the open-circuit EMF is twice the shown value, like a real generator.
                    double k = c.Text("load") == "50" ? 2.0 : 1.0;
                    double lo = offset * k, hi = (offset + amp) * k;
                    Waveform w;
                    if (c.Text("output") == "off")
                    {
                        w = new DcWaveform(0.0);
                    }
                    else
                    {
                        switch (c.Text("shape"))
                        {
                            case "sine": w = new SineWaveform(offset * k, amp * k, f, 0.0, c.Num("phase", 0)); break;
                            case "triangle": w = new PulseWaveform(lo, hi, 1.0 / f, 0.0, 0.0, 0.5 / f, 0.5 / f); break;
                            case "ramp": w = new FunctionWaveform(FunctionShape.Ramp, lo, hi, f, 0.5, edge, c.Num("phase", 0)); break;
                            case "pulse": w = new FunctionWaveform(FunctionShape.Pulse, lo, hi, f, duty, edge, c.Num("phase", 0)); break;
                            default: w = PulseWaveform.FromDuty(lo, hi, f, duty, 0.0, edge); break;
                        }
                    }

                    c.Add(new VoltageSource(c.RefDes, c.Pin("+"), c.Pin("-"), w) { SeriesResistance = Math.Max(0.0, c.Num("rs", 50)) });
                },
                P("shape", "Dalga", "square", "", "square", "sine", "triangle", "ramp", "pulse"), P("frequency", "Frekans", "1k", "Hz"),
                P("amplitude", "Genlik (kare/darbe: tepe, sinüs: ±)", "5", "V"), P("offset", "Ofset", "0", "V"), P("duty", "Doluluk / darbe genişliği", "0.5", "0-1"),
                P("edge", "Yükselme/düşme süresi", "10n", "s"), P("phase", "Faz", "0", "°"),
                P("rs", "Çıkış empedansı", "50", "Ω"), P("load", "Genlik ayarı", "hiz", "", "hiz", "50"), P("output", "Çıkış", "on", "", "on", "off"))
                .Vertical();

            Def(lib, "bench-psu", "Laboratuvar Güç Kaynağı (CV/CC)", Instruments, "PS", SymbolKind.BenchSupply, "WIRE-2", "voltage",
                c => c.Add(new BenchSupply(c.RefDes, c.Pin("+"), c.Pin("-"), c.Num("voltage", 12), c.Num("ilimit", 1))
                {
                    OvpLevel = c.Num("ovp", 0),
                    OutputEnabled = c.Text("output") != "off"
                }),
                P("voltage", "Gerilim ayarı (CV)", "12", "V"), P("ilimit", "Akım sınırı (CC)", "1", "A"), P("ovp", "Aşırı gerilim koruması (0 = kapalı)", "0", "V"),
                P("output", "Çıkış", "on", "", "on", "off"))
                .Vertical().Size(3, 2.4);

            Def(lib, "multimeter", "Dijital Multimetre", Instruments, "XMM", SymbolKind.Multimeter, "", "mode",
                c => c.Add(new RobotTwin.CoreSim.Instruments.Multimeter(c.RefDes, c.Pin("VΩ"), c.Pin("COM"), c.Pin("mA"), c.Pin("A"))
                {
                    Mode = RobotTwin.CoreSim.Instruments.MultimeterSpec.ParseMode(c.Text("mode"))
                }),
                P("mode", "Fonksiyon", "dcv", "", "dcv", "acv", "dca", "aca", "ohm", "cont", "diode", "cap", "freq"))
                .Pin("A", "", -3, 6).Pin("mA", "", -1, 6).Pin("COM", "", 1, 6).Pin("VΩ", "", 3, 6).Size(4, 4.5);

            Preset(lib, "psu-5v", "Güç Kaynağı 5 V", "dc-source", Instruments, "Laboratuvar tipi sabit 5 V");
            Preset(lib, "psu-3v3", "Güç Kaynağı 3.3 V", "dc-source", Instruments, "Sabit 3.3 V", "voltage=3.3");
            Preset(lib, "psu-9v", "Güç Kaynağı 9 V", "dc-source", Instruments, "Sabit 9 V", "voltage=9");
            Preset(lib, "psu-12v", "Güç Kaynağı 12 V", "dc-source", Instruments, "Sabit 12 V", "voltage=12");
            Preset(lib, "usb-5v", "USB 5 V", "dc-source", Instruments, "USB portu beslemesi", "voltage=5;rs=0.2");
            Preset(lib, "isrc-10m", "Akım Kaynağı 10 mA", "current-source", Instruments, "Sabit akım");
            Preset(lib, "siggen-square", "Kare Dalga 1 kHz", "signal-generator", Instruments, "0-5 V kare dalga");
            Preset(lib, "siggen-sine", "Sinüs 1 kHz", "signal-generator", Instruments, "±1 V sinüs", "shape=sine;amplitude=1");
            Preset(lib, "siggen-pwm", "PWM 490 Hz %50", "signal-generator", Instruments, "Arduino analogWrite frekansı", "frequency=490");
            Preset(lib, "funcgen-sine-50", "Fonksiyon Jeneratörü (50 Ω, sinüs)", "signal-generator", Instruments,
                "1 kHz, 1 V tepe 50 Ω yüke ayarlı: açık uçta 2 V görülür", "shape=sine;amplitude=1;load=50");
            Preset(lib, "funcgen-pulse", "Darbe Jeneratörü 10 kHz %10", "signal-generator", Instruments, "0-5 V, 10 µs darbe, 20 ns kenar",
                "shape=pulse;frequency=10k;duty=0.1;edge=20n");
            Preset(lib, "funcgen-ramp", "Testere Dişi 100 Hz", "signal-generator", Instruments, "0-5 V rampa", "shape=ramp;frequency=100");
            Preset(lib, "bench-psu", "Lab. Güç Kaynağı 0-30 V 3 A", "bench-psu", Instruments, "CV/CC, akım sınırlı, OVP'li", "voltage=12;ilimit=3;ovp=31");
            Preset(lib, "bench-psu-5v-100ma", "Lab. Güç Kaynağı 5 V / 100 mA", "bench-psu", Instruments, "Akım sınırı ile güvenli deneme (kısa devrede CC'ye geçer)",
                "voltage=5;ilimit=0.1");
            Preset(lib, "dmm", "Dijital Multimetre (6000 sayım)", "multimeter", Instruments, "True-RMS, otomatik kademe; VΩ/COM ile gerilim, mA/A ile seri akım ölçer");
            Preset(lib, "dmm-ampere", "Multimetre (akım, mA girişi)", "multimeter", Instruments, "Devreye SERİ bağlanır: mA → COM, 440 mA sigortalı", "mode=dca");

            Def(lib, "ground", "Toprak (GND)", Power, "#GND", SymbolKind.Ground, "", "", null)
                .Pin("GND", "", 0, -1).Size(1, 1).NetName = "GND";
            Def(lib, "power-rail", "Besleme Etiketi", Power, "#PWR", SymbolKind.PowerRail, "", "net", null,
                P("net", "Hat adı", "5V"))
                .Pin("PWR", "", 0, 1).Size(1, 1);

            Preset(lib, "gnd", "GND", "ground", Power, "Ortak toprak");
            Preset(lib, "rail-5v", "+5V", "power-rail", Power, "5 V hattı", "net=5V");
            Preset(lib, "rail-3v3", "+3V3", "power-rail", Power, "3.3 V hattı", "net=3V3");
            Preset(lib, "rail-12v", "+12V", "power-rail", Power, "12 V hattı", "net=12V");
            Preset(lib, "rail-vin", "VIN", "power-rail", Power, "Ham giriş hattı", "net=VIN");

            Def(lib, "ammeter", "Ampermetre", Instruments, "AM", SymbolKind.Ammeter, "", "",
                c => c.Add(new Ammeter(c.RefDes, c.Pin("+"), c.Pin("-"))))
                .TwoPin("+", "-");
            Def(lib, "voltmeter", "Voltmetre", Instruments, "VM", SymbolKind.Voltmeter, "", "",
                c => c.Add(new Resistor(c.RefDes, c.Pin("+"), c.Pin("-"), 10e6)))
                .TwoPin("+", "-");
            Preset(lib, "ammeter", "Ampermetre", "ammeter", Instruments, "Seri bağlanır, akımı ölçer");
            Preset(lib, "voltmeter", "Voltmetre (10 MΩ)", "voltmeter", Instruments, "Paralel bağlanır, gerilimi ölçer");
        }

        // ------------------------------------------------------------------ switches & relays

        private static void RegisterSwitching(PartLibrary lib)
        {
            Def(lib, "button", "Buton", Switching, "SW", SymbolKind.PushButton, "TACT-6", "",
                c => c.Add(new Switch(c.RefDes, c.Pin("A"), c.Pin("B"), c.Flag("closed"))),
                P("closed", "Basılı", "false", "", "false", "true"))
                .TwoPin("A", "B", "1", "3");

            Def(lib, "switch", "Anahtar", Switching, "SW", SymbolKind.Switch, "SW-2", "",
                c => c.Add(new Switch(c.RefDes, c.Pin("A"), c.Pin("B"), c.Flag("closed"))),
                P("closed", "Kapalı", "false", "", "false", "true"))
                .TwoPin();

            Def(lib, "relay", "Röle", Switching, "K", SymbolKind.Relay, "RELAY-SRD", "model",
                c =>
                {
                    double rc = c.Num("rcoil", 70);
                    c.Add(new Relay(c.RefDes, c.Pin("COIL1"), c.Pin("COIL2"), c.Pin("COM"), c.Pin("NO"), c.Pin("NC"))
                    {
                        CoilResistance = rc,
                        CoilInductance = c.Num("lcoil", 0.15),
                        PullInCurrent = c.Num("vpull", 3.75) / rc,
                        DropOutCurrent = c.Num("vdrop", 0.5) / rc,
                        OperateTime = c.Num("toperate", 8e-3)
                    });
                },
                P("model", "Model", "SRD-05VDC-SL-C"), P("rcoil", "Bobin direnci", "70", "Ω"), P("lcoil", "Bobin endüktansı", "0.15", "H"),
                P("vpull", "Çekme gerilimi", "3.75", "V"), P("vdrop", "Bırakma gerilimi", "0.5", "V"), P("toperate", "Çalışma süresi", "8m", "s"))
                .Pin("COIL1", "1", -4, -2).Pin("COIL2", "2", -4, 2).Pin("NO", "4", 4, -2).Pin("COM", "3", 4, 0).Pin("NC", "5", 4, 2).Size(3, 3);

            Preset(lib, "tact-6mm", "Buton 6×6 mm", "button", Switching, "Tactile push buton");
            Preset(lib, "toggle-spst", "Açma/Kapama Anahtarı", "switch", Switching, "SPST toggle anahtar");
            Preset(lib, "limit-switch", "Sınır Anahtarı", "switch", Switching, "Mikro switch (robot kolu uç durdurucu)");
            Preset(lib, "reed-switch", "Reed Anahtar", "switch", Switching, "Mıknatısla kapanan cam reed", "footprint=AXIAL-600");
            Preset(lib, "relay-srd-05", "Röle SRD-05VDC", "relay", Switching, "5 V bobinli 10 A röle");
            Preset(lib, "relay-srd-12", "Röle SRD-12VDC", "relay", Switching, "12 V bobinli 10 A röle",
                "model=SRD-12VDC-SL-C;rcoil=400;vpull=9;vdrop=1.2");
            Preset(lib, "relay-srd-03", "Röle SRD-03VDC", "relay", Switching, "3 V bobinli röle",
                "model=SRD-03VDC-SL-C;rcoil=25;vpull=2.25;vdrop=0.3");
        }

        // ------------------------------------------------------------------ motors, servos, outputs

        private static void RegisterElectromechanical(PartLibrary lib)
        {
            Def(lib, "dc-motor", "DC Motor", Electromechanical, "M", SymbolKind.Motor, "WIRE-2", "model",
                c =>
                {
                    var m = c.Add(new DcMotor(c.RefDes, c.Pin("A"), c.Pin("B")));
                    m.SetFromDatasheet(c.Num("voltage", 6), c.Num("rpm", 200), c.Num("i0", 0.15), c.Num("istall", 1.5),
                        Math.Max(1, c.Num("gear", 48)), c.Num("inductance", 1e-3));
                    m.LoadTorque = c.Num("load", 0);
                    if (c.Num("inertia", 0) > 0) m.Inertia = c.Num("inertia", 0);
                    if (c.Num("rth", 0) > 0) m.Thermal.ThermalResistance = c.Num("rth", 0);
                    if (c.Num("cth", 0) > 0) m.Thermal.ThermalCapacitance = c.Num("cth", 0);
                },
                P("model", "Model", "TT Motor 1:48"), P("voltage", "Nominal gerilim", "6", "V"), P("rpm", "Yüksüz devir (çıkış)", "200", "rpm"),
                P("i0", "Yüksüz akım", "0.15", "A"), P("istall", "Kilitlenme akımı", "1.5", "A"), P("gear", "Dişli oranı", "48"),
                P("variant", "Üretim", "datasheet", "", "datasheet", "klon"),
                P("inductance", "Endüktans", "1m", "H"), P("load", "Yük torku", "0", "N·m"), P("inertia", "Rotor ataleti (0 = otomatik)", "0", "kg·m²"),
                P("rth", "Isıl direnç (0 = varsayılan)", "0", "K/W"), P("cth", "Isıl kapasite (0 = varsayılan)", "0", "J/K"),
                P("tolerance", "Üretim toleransı", "10", "%"))
                .TwoPin();

            Preset(lib, "motor-tt", "TT Redüktörlü Motor 1:48", "dc-motor", Electromechanical, "Robot araba motoru, 3-6 V");
            Preset(lib, "motor-n20", "N20 Mikro Motor 1:100", "dc-motor", Electromechanical, "6 V 300 rpm metal dişli",
                "model=N20 1:100;voltage=6;rpm=300;i0=0.04;istall=0.36;gear=100;inductance=0.5m");
            Preset(lib, "motor-25ga370", "25GA-370 Redüktörlü Motor", "dc-motor", Electromechanical, "6 V 280 rpm",
                "model=25GA-370;voltage=6;rpm=280;i0=0.12;istall=2;gear=21.3;inductance=1.5m");
            Preset(lib, "motor-jga25-12v", "JGA25-370 12 V 100 rpm", "dc-motor", Electromechanical, "Encoder'lı robot motoru",
                "model=JGA25-370;voltage=12;rpm=100;i0=0.08;istall=1.6;gear=78;inductance=2m");
            Preset(lib, "motor-8520", "8520 Coreless Motor", "dc-motor", Electromechanical, "Mikro drone motoru, 3.7 V 48000 rpm",
                "model=8520 Coreless;voltage=3.7;rpm=48000;i0=0.15;istall=3.5;gear=1;inductance=30u;inertia=1.5e-8;rth=40;cth=1.5");
            Preset(lib, "motor-0720", "0720 Coreless Motor", "dc-motor", Electromechanical, "Küçük drone motoru, 3.7 V 52000 rpm",
                "model=0720 Coreless;voltage=3.7;rpm=52000;i0=0.08;istall=1.6;gear=1;inductance=20u;inertia=6e-9;rth=55;cth=0.8");
            Preset(lib, "motor-130", "130 DC Motor", "dc-motor", Electromechanical, "3 V 12000 rpm dişlisiz",
                "model=130;voltage=3;rpm=12000;i0=0.15;istall=2.2;gear=1;inductance=0.2m");

            Def(lib, "servo", "Servo Motor", Electromechanical, "M", SymbolKind.Servo, "HDR-1x3", "model",
                c => c.Add(new Servo(c.RefDes, c.Pin("V+"), c.Pin("GND"), c.Pin("SIG"))
                {
                    SpeedDegPerSec = 60.0 / Math.Max(0.01, c.Num("speed", 0.1)),
                    StallCurrent = c.Num("istall", 0.65),
                    MovingCurrent = c.Num("imove", 0.25),
                    MaxAngleDeg = c.Num("range", 180)
                }),
                P("model", "Model", "SG90"), P("speed", "Hız (s/60°)", "0.1", "s"), P("istall", "Kilitlenme akımı", "0.65", "A"),
                P("imove", "Hareket akımı", "0.25", "A"), P("range", "Açı aralığı", "180", "°"),
                P("variant", "Üretim", "datasheet", "", "datasheet", "klon"), P("pinmap", "Pin eşleme", "GND:1,V+:2,SIG:3"))
                .Pin("SIG", "3", -4, -2).Pin("V+", "2", -4, 0).Pin("GND", "1", -4, 2).Size(3, 3);

            Preset(lib, "servo-sg90", "SG90 Mikro Servo", "servo", Electromechanical, "9 g, 1.8 kg·cm");
            Preset(lib, "servo-mg90s", "MG90S Metal Dişli Servo", "servo", Electromechanical, "13 g, 2.2 kg·cm", "model=MG90S;istall=0.7;imove=0.3");
            Preset(lib, "servo-mg996r", "MG996R Servo", "servo", Electromechanical, "55 g, 11 kg·cm", "model=MG996R;speed=0.17;istall=2.5;imove=0.5");
            Preset(lib, "servo-ds3218", "DS3218 Servo", "servo", Electromechanical, "20 kg·cm dijital servo", "model=DS3218;speed=0.16;istall=2.8;imove=0.6;range=270");

            Def(lib, "buzzer", "Aktif Buzzer", Electromechanical, "BZ", SymbolKind.Buzzer, "BUZZER-12", "voltage",
                c => c.Add(new Buzzer(c.RefDes, c.Pin("+"), c.Pin("-"), c.Num("voltage", 5), c.Num("current", 0.03))),
                P("voltage", "Nominal gerilim", "5", "V"), P("current", "Nominal akım", "30m", "A"))
                .TwoPin("+", "-");

            Def(lib, "lamp", "Akkor Lamba", Electromechanical, "LA", SymbolKind.Lamp, "WIRE-2", "power",
                c => c.Add(new Lamp(c.RefDes, c.Pin("A"), c.Pin("B"), c.Num("voltage", 12), c.Num("power", 5))),
                P("voltage", "Nominal gerilim", "12", "V"), P("power", "Güç", "5", "W"))
                .TwoPin();

            Preset(lib, "buzzer-5v", "Aktif Buzzer 5 V", "buzzer", Electromechanical, "Kendi osilatörlü 12 mm buzzer");
            Preset(lib, "buzzer-3v", "Aktif Buzzer 3 V", "buzzer", Electromechanical, "3 V buzzer", "voltage=3;current=25m");
            Preset(lib, "lamp-12v-5w", "Lamba 12 V 5 W", "lamp", Electromechanical, "Akkor ampul");
            Preset(lib, "lamp-6v-0w6", "Lamba 6 V 0.6 W", "lamp", Electromechanical, "Küçük gösterge ampulü", "voltage=6;power=0.6");
        }

        // ------------------------------------------------------------------ sensors

        private static void RegisterSensors(PartLibrary lib)
        {
            Def(lib, "sensor-module", "Sensör Modülü", Sensors, "U", SymbolKind.Module, "HDR-1x3", "model",
                c => c.Add(new ControlledSource(c.RefDes, c.Pin("OUT"), c.Pin("VCC"), c.Pin("GND"), c.Text("mode") != "absolute")
                {
                    Level = c.Num("level", 0),
                    SupplyCurrent = c.Num("isupply", 5e-3)
                }),
                P("model", "Model", "Analog Sensör"), P("mode", "Çıkış tipi", "ratio", "", "ratio", "absolute"),
                P("level", "Çıkış seviyesi", "0.5", "oran / V"), P("isupply", "Besleme akımı", "5m", "A"),
                P("quantity", "Ölçülen büyüklük", ""), P("pinmap", "Pin eşleme", "VCC:1,GND:2,OUT:3"))
                .Pin("VCC", "1", -4, -2).Pin("OUT", "3", -4, 0).Pin("GND", "2", -4, 2).Size(3, 3);

            Def(lib, "hc-sr04", "HC-SR04 Ultrasonik Mesafe", Sensors, "U", SymbolKind.Module, "HDR-1x4", "model",
                c => c.Add(new UltrasonicSensor(c.RefDes, c.Pin("VCC"), c.Pin("TRIG"), c.Pin("ECHO"), c.Pin("GND"))),
                P("model", "Model", "HC-SR04"), P("pinmap", "Pin eşleme", "VCC:1,TRIG:2,ECHO:3,GND:4"))
                .Pin("VCC", "1", -4, -3).Pin("TRIG", "2", -4, -1).Pin("ECHO", "3", -4, 1).Pin("GND", "4", -4, 3).Size(3, 4);
            Preset(lib, "hc-sr04", "HC-SR04 Ultrasonik Sensör", "hc-sr04", Sensors, "2 cm – 4 m mesafe, TRIG/ECHO zamanlaması");

            Preset(lib, "lm35", "LM35 Sıcaklık Sensörü", "sensor-module", Sensors, "10 mV/°C analog çıkış (TO-92)",
                "model=LM35;mode=absolute;level=0.25;isupply=60u;quantity=temperature;footprint=TO92;pinmap=VCC:1,OUT:2,GND:3");
            Preset(lib, "tmp36", "TMP36 Sıcaklık Sensörü", "sensor-module", Sensors, "500 mV + 10 mV/°C (TO-92)",
                "model=TMP36;mode=absolute;level=0.75;isupply=50u;quantity=temperature;footprint=TO92;pinmap=VCC:1,OUT:2,GND:3");
            Preset(lib, "ir-obstacle", "IR Engel Sensörü Modülü", "sensor-module", Sensors, "Dijital çıkış, engel varken LOW",
                "model=IR Engel;level=1;isupply=20m;quantity=obstacle");
            Preset(lib, "tcrt5000", "TCRT5000 Çizgi Sensörü Modülü", "sensor-module", Sensors, "Yansıma ile çizgi algılama, analog çıkış",
                "model=TCRT5000;level=0.2;isupply=20m;quantity=reflectance");
            Preset(lib, "soil-moisture", "Toprak Nem Sensörü", "sensor-module", Sensors, "Analog nem çıkışı",
                "model=Toprak Nem;level=0.4;quantity=moisture");
            Preset(lib, "pir-hcsr501", "HC-SR501 PIR Hareket Sensörü", "sensor-module", Sensors, "3.3 V dijital çıkış",
                "model=HC-SR501;mode=absolute;level=0;isupply=65u;quantity=motion;pinmap=VCC:1,OUT:2,GND:3");
            Preset(lib, "joystick-axis", "Joystick Ekseni", "sensor-module", Sensors, "Analog joystick X/Y ekseni",
                "model=Joystick;level=0.5;isupply=0.5m;quantity=position");
            Preset(lib, "sound-sensor", "Ses Sensörü Modülü", "sensor-module", Sensors, "Mikrofonlu dijital/analog modül",
                "model=Ses;level=0.5;quantity=sound");
        }

        // ------------------------------------------------------------------ I²C displays and modules

        /// <summary>
        /// I²C modules: electrically a supply load plus the module's SDA/SCL pull-ups;
        /// their protocol side (SSD1306, HD44780 behind a PCF8574, MPU-6050) attaches to
        /// the microcontroller's bus when SDA/SCL are wired to its I²C pins.
        /// </summary>
        private static void RegisterDisplays(PartLibrary lib)
        {
            Action<PartBuildContext> I2cModule(double defaultCurrent) => c =>
            {
                int vcc = c.Pin("VCC"), gnd = c.Pin("GND");
                c.Add(new Resistor(c.RefDes, vcc, gnd, 5.0 / Math.Max(1e-4, c.Num("isupply", defaultCurrent))));
                double pull = c.Num("pullup", 10e3);
                if (pull > 0)
                {
                    c.Add(new Resistor(c.Sub("RSDA"), c.Pin("SDA"), vcc, pull));
                    c.Add(new Resistor(c.Sub("RSCL"), c.Pin("SCL"), vcc, pull));
                }
            };

            var oled = Def(lib, "oled-ssd1306", "OLED Ekran (SSD1306, I²C)", Displays, "DS", SymbolKind.Module, "HDR-1x4", "model",
                I2cModule(0.02),
                P("model", "Model", "SSD1306 128×64"), P("height", "Yükseklik (piksel)", "64", "", "64", "32"),
                P("address", "I²C adresi", "0x3C", "", "0x3C", "0x3D"), P("color", "Piksel rengi", "white", "", "white", "blue", "yellow-blue"),
                P("isupply", "Besleme akımı", "20m", "A"), P("pullup", "Pull-up direnci", "10k", "Ω"),
                P("pinmap", "Pin eşleme", "GND:1,VCC:2,SCL:3,SDA:4"));
            oled.SymbolVariant = "OLED";
            oled.Pin("GND", "1", -8, -3).Pin("VCC", "2", -8, -1).Pin("SCL", "3", -8, 1).Pin("SDA", "4", -8, 3).Size(7, 4.5);
            Preset(lib, "oled-128x64", "0.96\" OLED 128×64 (SSD1306)", "oled-ssd1306", Displays, "I²C monokrom grafik ekran, 0x3C");
            Preset(lib, "oled-128x32", "0.91\" OLED 128×32 (SSD1306)", "oled-ssd1306", Displays, "I²C ince grafik ekran, 0x3C",
                "model=SSD1306 128×32;height=32;isupply=12m");

            var lcd = Def(lib, "lcd-i2c", "Karakter LCD (I²C, PCF8574)", Displays, "DS", SymbolKind.Module, "HDR-1x4", "model",
                I2cModule(0.035),
                P("model", "Model", "LCD 1602 I²C"), P("columns", "Sütun", "16", "", "16", "20"), P("rows", "Satır", "2", "", "2", "4"),
                P("address", "I²C adresi", "0x27", "", "0x27", "0x3F", "0x20"), P("backlight", "Arka ışık", "blue", "", "blue", "green"),
                P("isupply", "Besleme akımı", "35m", "A"), P("pullup", "Pull-up direnci", "4.7k", "Ω"),
                P("pinmap", "Pin eşleme", "GND:1,VCC:2,SDA:3,SCL:4"));
            lcd.SymbolVariant = "LCD";
            lcd.Pin("GND", "1", -11, -3).Pin("VCC", "2", -11, -1).Pin("SDA", "3", -11, 1).Pin("SCL", "4", -11, 3).Size(10, 4.5);
            Preset(lib, "lcd1602-i2c", "LCD 1602 + I²C modül", "lcd-i2c", Displays, "16×2 karakter, mavi arka ışık, PCF8574 0x27");
            Preset(lib, "lcd2004-i2c", "LCD 2004 + I²C modül", "lcd-i2c", Displays, "20×4 karakter, PCF8574 0x27",
                "model=LCD 2004 I²C;columns=20;rows=4;isupply=60m");

            var mpu = Def(lib, "mpu6050", "MPU-6050 İvme + Jiroskop", Displays, "U", SymbolKind.Module, "HDR-1x8", "model",
                I2cModule(0.004),
                P("model", "Model", "GY-521 MPU-6050"), P("address", "I²C adresi", "0x68", "", "0x68", "0x69"),
                P("isupply", "Besleme akımı", "4m", "A"), P("pullup", "Pull-up direnci", "4.7k", "Ω"),
                P("pinmap", "Pin eşleme", "VCC:1,GND:2,SCL:3,SDA:4"));
            mpu.Pin("VCC", "1", -5, -3).Pin("GND", "2", -5, -1).Pin("SCL", "3", -5, 1).Pin("SDA", "4", -5, 3).Size(4, 4);
            Preset(lib, "gy-521", "GY-521 MPU-6050 Modülü", "mpu6050", Displays, "3 eksen ivmeölçer + 3 eksen jiroskop, I²C 0x68");
        }

        // ------------------------------------------------------------------ connectors

        private static void RegisterConnectors(PartLibrary lib)
        {
            foreach (int n in new[] { 2, 3, 4, 5, 6, 8 })
            {
                var part = Def(lib, "header-" + n, $"Pin Header 1×{n}", Connectors, "J", SymbolKind.Connector, "HDR-1x" + n, "", null);
                for (int i = 0; i < n; i++)
                {
                    string name = (i + 1).ToString(CultureInfo.InvariantCulture);
                    part.Pins.Add(new PinDefinition(name, name, -3, (i - (n - 1) / 2.0) * 2));
                }

                part.Size(1.5, n);
                Preset(lib, "hdr-1x" + n, $"Pin Header 1×{n}", "header-" + n, Connectors, "2.54 mm erkek header");
            }

            foreach (int n in new[] { 2, 3 })
            {
                var part = Def(lib, "terminal-" + n, $"Klemens {n}'li", Connectors, "J", SymbolKind.Connector, "TERM-" + n, "", null);
                for (int i = 0; i < n; i++)
                {
                    string name = (i + 1).ToString(CultureInfo.InvariantCulture);
                    part.Pins.Add(new PinDefinition(name, name, -3, (i - (n - 1) / 2.0) * 2));
                }

                part.Size(1.5, n);
                Preset(lib, "term-" + n, $"Vidalı Klemens {n}'li", "terminal-" + n, Connectors, "5.08 mm vidalı klemens");
            }
        }

        // ------------------------------------------------------------------ development boards

        private static void BuildArduino(PartBuildContext c, IEnumerable<string> ioPins)
        {
            int v5 = c.Pin("5V"), v3 = c.Pin("3V3"), vin = c.Pin("VIN"), gnd = c.Pin("GND1");

            if (c.Flag("usb") || WirelessParts.UsbFromCable(c))
            {
                int usb = WirelessParts.UsbSupply(c, gnd, 0.1);   // [radio] usb=cable: VBUS switched by the world's USB cable
                c.Add(new Fuse(c.Sub("F1"), usb, v5, 0.5) { TimeConstant = 2.0 });
            }

            // NCP1117 5 V regulator fed from the barrel jack / VIN.
            c.Add(new LinearRegulator(c.Sub("REG5V"), vin, gnd, v5, 5.0)
            {
                Dropout = 1.1, CurrentLimit = 0.8, MaxInputVoltage = 20, ThermalResistance = 60, QuiescentCurrent = 5e-3
            });
            c.Add(new LinearRegulator(c.Sub("REG3V3"), v5, gnd, v3, 3.3)
            {
                Dropout = 0.3, CurrentLimit = 0.15, MaxInputVoltage = 16, ThermalResistance = 250, QuiescentCurrent = 0.1e-3
            });

            // Microcontroller core, USB bridge and power LED.
            c.Add(new Resistor(c.Sub("CORE"), v5, gnd, 5.0 / c.Num("idle", 0.045)));

            foreach (string pin in ioPins)
            {
                c.Add(new DigitalPin(c.Sub(pin), c.Pin(pin), v5, gnd));
            }
        }

        private static void RegisterBoards(PartLibrary lib)
        {
            Def(lib, "motor-driver", "PWM Motor Sürücü", Electromechanical, "U", SymbolKind.Module, "HDR-1x4", "model",
                c =>
                {
                    var d = c.Add(new AveragedPwmDriver(c.RefDes, c.Pin("VM"), c.Pin("OUT"), c.Pin("GND"))
                    {
                        Resistance = c.Num("rds", 0.5),
                        CurrentRating = c.Num("imax", 1.2)
                    });
                    d.ControlNode = c.Pin("PWM");
                    c.Add(new Resistor(c.Sub("RIN"), c.Pin("PWM"), c.Pin("GND"), 100e3));
                },
                P("model", "Model", "TB6612 (1 kanal)"), P("rds", "Anahtar direnci", "0.5", "Ω"), P("imax", "Sürekli akım", "1.2", "A"),
                P("pinmap", "Pin eşleme", "VM:1,PWM:2,OUT:3,GND:4"))
                .Pin("VM", "1", -4, -3).Pin("PWM", "2", -4, -1).Pin("OUT", "3", -4, 1).Pin("GND", "4", -4, 3).Size(3, 4);
            Preset(lib, "tb6612-ch", "TB6612FNG Motor Sürücü (1 kanal)", "motor-driver", Electromechanical, "PWM girişli MOSFET köprü, 1.2 A");
            Preset(lib, "l298n-ch", "L298N Motor Sürücü (1 kanal)", "motor-driver", Electromechanical, "Bipolar köprü, ~2 V düşümlü, 2 A",
                "model=L298N (1 kanal);rds=2.0;imax=2");

            var fc = Def(lib, "flight-controller", "Fırçalı Uçuş Kontrolcüsü", Boards, "U", SymbolKind.Board, "HDR-1x6", "model",
                c =>
                {
                    int vbat = c.Pin("VBAT"), gnd = c.Pin("GND");
                    foreach (var m in new[] { "M1", "M2", "M3", "M4" })
                    {
                        c.Add(new AveragedPwmDriver(c.Sub(m), vbat, c.Pin(m), gnd) { Resistance = 0.03, CurrentRating = 4.0, ThermalResistance = 120 });
                    }

                    c.Add(new Resistor(c.Sub("MCU"), vbat, gnd, 3.7 / 0.06));
                },
                P("model", "Model", "F3 Brushed FC"), P("pinmap", "Pin eşleme", "VBAT:1,GND:2,M1:3,M2:4,M3:5,M4:6"));
            fc.SymbolVariant = "FC";
            fc.Pin("VBAT", "1", -7, -2).Pin("GND", "2", -7, 2).Pin("M1", "3", 7, -3).Pin("M2", "4", 7, -1).Pin("M3", "5", 7, 1).Pin("M4", "6", 7, 3).Size(6, 4.5);
            Preset(lib, "fc-brushed", "Fırçalı Uçuş Kontrolcüsü (F3)", "flight-controller", Boards, "IMU + PID dengeleme + 4 MOSFET, 1S mikro drone");

            var uno = Def(lib, "arduino-uno", "Arduino Uno R3", Boards, "U", SymbolKind.Board, "ARDUINO-UNO", "",
                null, P("usb", "USB bağlı", "true", "", "true", "false"), P("idle", "Boşta akım", "45m", "A"));
            uno.SymbolVariant = "UNO";
            string[] unoLeft = { "VIN", "5V", "3V3", "AREF", "GND1", "GND2", "GND3", "A0", "A1", "A2", "A3", "A4", "A5" };
            for (int i = 0; i < unoLeft.Length; i++) uno.Pins.Add(new PinDefinition(unoLeft[i], unoLeft[i], -7, -12 + 2 * i));
            var unoIo = new List<string>();
            for (int i = 0; i <= 13; i++)
            {
                string name = "D" + i.ToString(CultureInfo.InvariantCulture);
                uno.Pins.Add(new PinDefinition(name, name, 7, -13 + 2 * i));
                unoIo.Add(name);
            }

            for (int i = 0; i <= 5; i++) unoIo.Add("A" + i.ToString(CultureInfo.InvariantCulture));
            uno.Size(6, 14);
            uno.InternalConnections.Add(new[] { "GND1", "GND2", "GND3" });
            uno.Build = c => BuildArduino(c, unoIo);
            Preset(lib, "arduino-uno", "Arduino Uno R3", "arduino-uno", Boards, "ATmega328P, 14 dijital / 6 analog pin, 16 MHz");

            var nano = Def(lib, "arduino-nano", "Arduino Nano", Boards, "U", SymbolKind.Board, "ARDUINO-NANO", "",
                null, P("usb", "USB bağlı", "true", "", "true", "false"), P("idle", "Boşta akım", "25m", "A"));
            nano.SymbolVariant = "NANO";
            string[] nanoLeft = { "VIN", "5V", "3V3", "AREF", "GND1", "GND2", "A0", "A1", "A2", "A3", "A4", "A5", "A6", "A7" };
            for (int i = 0; i < nanoLeft.Length; i++) nano.Pins.Add(new PinDefinition(nanoLeft[i], nanoLeft[i], -7, -13 + 2 * i));
            var nanoIo = new List<string>();
            for (int i = 0; i <= 13; i++)
            {
                string name = "D" + i.ToString(CultureInfo.InvariantCulture);
                nano.Pins.Add(new PinDefinition(name, name, 7, -13 + 2 * i));
                nanoIo.Add(name);
            }

            for (int i = 0; i <= 5; i++) nanoIo.Add("A" + i.ToString(CultureInfo.InvariantCulture));
            nano.Size(6, 14);
            nano.InternalConnections.Add(new[] { "GND1", "GND2" });
            nano.Build = c => BuildArduino(c, nanoIo);
            Preset(lib, "arduino-nano", "Arduino Nano", "arduino-nano", Boards, "ATmega328P, breadboard dostu, 8 analog giriş");
        }
    }
}
