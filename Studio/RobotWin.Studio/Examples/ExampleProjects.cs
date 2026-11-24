using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;

namespace RobotWin.Studio.Examples
{
    public sealed class ExampleProject
    {
        public ExampleProject(string id, string title, string description, string level, string icon, Func<Schematic> build, string page = "schematic")
        {
            Page = page;
            Id = id;
            Title = title;
            Description = description;
            Level = level;
            Icon = icon;
            // [lang] the circuit made in the language in use: its title is its own data from then on
            Build = () =>
            {
                var sch = build();
                if (Shell.Lang.Current != "tr") sch.Title = Shell.Lang.T(sch.Title);
                return sch;
            };
        }

        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public string Level { get; }
        public string Icon { get; }
        public Func<Schematic> Build { get; }

        /// <summary>Page to open after loading ("schematic" or "assembly").</summary>
        public string Page { get; }
    }

    /// <summary>Ready-made circuits that show what the simulator does, drawn with real wiring.</summary>
    public static class ExampleProjects
    {
        public static IReadOnlyList<ExampleProject> All { get; } = new List<ExampleProject>
        {
            new ExampleProject("led-button", "LED ve Buton", "9 V pil, akım sınırlama direnci ve buton. Butona basın, LED yansın; direnci silip LED'i yakın.", "Başlangıç", "I.Bolt", LedButton),
            new ExampleProject("arduino-blink", "Arduino: Gerçek Firmware", "Uno gerçek derlenmiş Blink + Serial firmware'ini çalıştırır: D13'teki LED 1 sn yanıp söner, Seri Monitör'e mesaj yazar.", "Başlangıç", "I.Chip", ArduinoBlink),
            new ExampleProject("arduino-displays", "Arduino: LCD + OLED Ekran", "Gerçek derlenmiş firmware I²C üzerinden 16×2 LCD'ye metin, SSD1306 OLED'e grafik çizer. Potansiyometreyi çevirin.", "Orta", "I.Chip", ArduinoDisplays),
            new ExampleProject("esp32-dimmer", "ESP32: PWM Işık Ayarı", "ESP32 DevKit V1, IO34'teki potansiyometreyi ADC ile okur ve IO25'teki LED'in parlaklığını LEDC PWM ile ayarlar. Kart üstündeki mavi LED yanıp söner.", "Başlangıç", "I.Chip", Esp32Dimmer),
            new ExampleProject("motor-driver", "MOSFET ile Motor Sürme", "490 Hz PWM, lojik seviye MOSFET, serbest dolaşım diyodu ve 25GA-370 motor. Hız ve akımı izleyin.", "Orta", "I.Rotate", MotorDriver),
            new ExampleProject("opamp-amp", "Op-Amp Yükselteç", "TL072 ile 11× evirmeyen yükselteç. Giriş ve çıkışı osiloskopta karşılaştırın.", "Orta", "I.Scope", OpAmpAmplifier),
            new ExampleProject("power-7805", "7805 Güç Kaynağı", "12 V akü, 7805 regülatör ve yük. Yükü artırın, ısınmayı ve termal kapanmayı görün.", "Orta", "I.Bolt", PowerSupply),
            new ExampleProject("rc-filter", "RC Alçak Geçiren Filtre", "1 kHz kare dalga, 1 kΩ ve 1 µF: zaman sabitini osiloskopta görün.", "Başlangıç", "I.Scope", RcFilter),
            new ExampleProject("relay-lamp", "Röle ile Lamba", "Buton → 2N2222 → röle bobini; röle kontağı 12 V lambayı yakar. Flyback diyodu dahil.", "Orta", "I.Pcb", RelayLamp),
            new ExampleProject("nand-logic", "NAND Kapısı", "İki anahtar, pull-down dirençleri ve 74HC00. Doğruluk tablosunu deneyin.", "Başlangıç", "I.Chip", NandLogic),
            new ExampleProject("line-follower", "Robot: Çizgi İzleyen", "İki TCRT5000, 74HC04 ve TB6612 ile analog çizgi izleyen araba. 3D pistte sürüşü, motor akımını ve ısınmayı izleyin.", "Robot", "I.Cube",
                () => VehicleTemplates.LineFollower(PartLibrary.Default), "assembly"),
            new ExampleProject("obstacle-avoider", "Robot: Engelden Kaçan (Arduino)", "Uno gerçek derlenmiş yazılımla HC-SR04'ü okur, iki TB6612 kanalıyla motorları sürer. Kodu Kod sayfasında değiştirip yeniden yükleyin.", "Robot", "I.Cube",
                () => VehicleTemplates.ObstacleAvoider(PartLibrary.Default), "assembly"),
            new ExampleProject("quadcopter", "Drone: Mikro Quadcopter", "1S LiPo, fırçalı uçuş kontrolcüsü (PID) ve dört 8520 motor. Boşluk ile kurun, W/S/A/D ve oklarla uçurun.", "Robot", "I.Cube",
                () => VehicleTemplates.MicroQuadcopter(PartLibrary.Default), "assembly")
        };

        /// <summary>Uno + pot on A0 + I²C LCD and OLED on A4/A5, connected with net labels.</summary>
        private static Schematic ArduinoDisplays()
        {
            var b = new B("Arduino: LCD + OLED");
            var lib = PartLibrary.Default;
            var uno = b.Add("arduino-uno", 0, 0, 0, ("firmware", "builtin:i2c-displays.hex"));
            var lcd = b.Add("lcd1602-i2c", 34, -9);
            var oled = b.Add("oled-128x64", 31, 9);
            var pot = b.Add("pot-10k", -22, 10);
            void L(SchematicPart part, string pin, string net) => VehicleTemplates.Tie(b.S, lib, part, pin, net);
            L(uno, "5V", "5V");
            L(uno, "GND1", "GND");
            L(uno, "A4", "SDA");
            L(uno, "A5", "SCL");
            L(uno, "A0", "POT");
            foreach (var d in new[] { lcd, oled })
            {
                L(d, "VCC", "5V");
                L(d, "GND", "GND");
                L(d, "SDA", "SDA");
                L(d, "SCL", "SCL");
            }

            L(pot, "A", "5V");
            L(pot, "B", "GND");
            L(pot, "W", "POT");
            pot.Parameters["position"] = "0.7";
            return b.S;
        }

        /// <summary>ESP32 DevKit V1 + pot on IO34 + LED with 220 Ω on IO25, connected with net labels.</summary>
        private static Schematic Esp32Dimmer()
        {
            var b = new B("ESP32: PWM Işık Ayarı");
            var lib = PartLibrary.Default;
            var esp = b.Add("esp32-devkit-v1", 0, 0, 0, ("firmware", "builtin:esp32-dimmer.ino"));
            var pot = b.Add("pot-10k", -24, 6);
            var r = b.Add("res-220", 22, 2);
            var led = b.Add("led5-red", 30, 8);
            void L(SchematicPart part, string pin, string net) => VehicleTemplates.Tie(b.S, lib, part, pin, net);
            L(esp, "3V3", "3V3");
            L(esp, "GND1", "GND");
            L(esp, "IO34", "POT");
            L(esp, "IO25", "PWM");
            L(pot, "A", "3V3");
            L(pot, "B", "GND");
            L(pot, "W", "POT");
            L(r, "A", "PWM");
            L(r, "B", "LEDA");
            L(led, "A", "LEDA");
            L(led, "K", "GND");
            pot.Parameters["position"] = "0.6";
            return b.S;
        }

        // ------------------------------------------------------------------ builder

        private sealed class B
        {
            private readonly PartLibrary _lib = PartLibrary.Default;

            public B(string title)
            {
                S = new Schematic { Title = title };
            }

            public Schematic S { get; }

            public SchematicPart Add(string preset, double x, double y, int rotation = 0, params (string Key, string Value)[] parameters)
            {
                var p = S.AddPart(_lib, preset, new GridPoint(x, y), rotation);
                foreach (var (k, v) in parameters) p.Parameters[k] = v;
                return p;
            }

            public GridPoint Pin(SchematicPart part, string pin)
            {
                var def = _lib.Get(part.PartId);
                var p = def.FindPin(pin) ?? throw new ArgumentException(pin);
                return part.Transform(p.X, p.Y);
            }

            /// <summary>Wire between two pins: horizontal first (or vertical first), with one elbow.</summary>
            public void Wire(SchematicPart a, string pa, SchematicPart b, string pb, bool verticalFirst = false)
            {
                var p = Pin(a, pa);
                var q = Pin(b, pb);
                Path(p, q, verticalFirst);
            }

            public void Path(GridPoint p, GridPoint q, bool verticalFirst = false)
            {
                var pts = new List<GridPoint> { p };
                if (p.X != q.X && p.Y != q.Y) pts.Add(verticalFirst ? new GridPoint(p.X, q.Y) : new GridPoint(q.X, p.Y));
                pts.Add(q);
                S.Wires.Add(new SchematicWire { Points = pts });
            }

            public void Wire(params GridPoint[] points) => S.Wires.Add(new SchematicWire { Points = new List<GridPoint>(points) });

            /// <summary>Ground symbol hanging below a pin.</summary>
            public void Gnd(SchematicPart part, string pin, double drop = 2)
            {
                var tip = Pin(part, pin);
                var g = Add("gnd", tip.X, tip.Y + drop + 1);
                Path(tip, Pin(g, "GND"));
            }

            /// <summary>Power-rail symbol above a pin.</summary>
            public void Rail(SchematicPart part, string pin, string net, double rise = 2)
            {
                var tip = Pin(part, pin);
                string preset = net switch { "5V" => "rail-5v", "3V3" => "rail-3v3", "12V" => "rail-12v", _ => "rail-vin" };
                var r = Add(preset, tip.X, tip.Y - rise - 1, 0, ("net", net));
                Path(tip, Pin(r, "PWR"));
            }

            public void Label(SchematicPart part, string pin, string name) =>
                S.Labels.Add(new NetLabel { Name = name, Position = Pin(part, pin) });
        }

        // ------------------------------------------------------------------ circuits

        private static Schematic LedButton()
        {
            var b = new B("LED ve Buton");
            var bat = b.Add("bat-9v", 0, 0);
            var sw = b.Add("tact-6mm", 7, -6);
            var r = b.Add("res-470", 17, -6);
            var led = b.Add("led5-red", 25, 0, 270);
            b.Wire(bat, "+", sw, "A", verticalFirst: true);
            b.Wire(sw, "B", r, "A");
            b.Wire(r, "B", led, "A");
            b.Gnd(bat, "-");
            b.Gnd(led, "K");
            return b.S;
        }

        private static Schematic ArduinoBlink()
        {
            var b = new B("Arduino: Gerçek Firmware");
            var uno = b.Add("arduino-uno", 0, 0, 0, ("firmware", "builtin:blink-serial.hex"));
            var r = b.Add("res-220", 16, 13);
            var led = b.Add("led5-red", 24, 17, 270);
            b.Wire(uno, "D13", r, "A");
            b.Wire(r, "B", led, "A");
            b.Gnd(led, "K");
            var gndPin = b.Pin(uno, "GND1");
            var g = b.Add("gnd", gndPin.X - 4, gndPin.Y + 3);
            b.Path(gndPin, b.Pin(g, "GND"));

            // A potentiometer on A0 to try analog readings.
            var pot = b.Add("pot-10k", -16, 4, 270);
            b.Rail(pot, "A", "5V");
            b.Gnd(pot, "B");
            var a0 = b.Pin(uno, "A0");
            var w = b.Pin(pot, "W");
            b.Path(w, a0, verticalFirst: true);
            var v5 = b.Pin(uno, "5V");
            b.Label(uno, "5V", "5V");
            _ = v5;
            return b.S;
        }

        private static Schematic MotorDriver()
        {
            var b = new B("MOSFET ile Motor Sürme");
            var bat = b.Add("bat-3s-lipo", 0, 0);
            var pwm = b.Add("siggen-pwm", 10, 12, 0, ("duty", "0.6"), ("frequency", "490"));
            var rg = b.Add("res-100", 17, 9);
            var rpd = b.Add("res-10000", 22, 14, 270);
            var q = b.Add("irlz44n", 27, 9);
            var m = b.Add("motor-25ga370", 28, -2, 270);
            var d = b.Add("1n5819", 34, -2, 90);

            b.Rail(bat, "+", "VBAT");
            b.Gnd(bat, "-");
            b.Wire(pwm, "+", rg, "A", verticalFirst: true);
            b.Gnd(pwm, "-");
            b.Wire(rg, "B", q, "G");
            b.Wire(rpd, "A", rg, "B", verticalFirst: true);
            b.Gnd(rpd, "B");
            b.Wire(q, "D", m, "B", verticalFirst: true);
            b.Gnd(q, "S");
            b.Rail(m, "A", "VBAT");
            b.Wire(d, "A", m, "B");
            b.Wire(d, "K", m, "A");
            return b.S;
        }

        private static Schematic OpAmpAmplifier()
        {
            var b = new B("Op-Amp Yükselteç");
            var vp = b.Add("psu-12v", -14, -6);
            var vn = b.Add("psu-12v", -14, 8);
            var sig = b.Add("siggen-sine", -4, 4, 0, ("amplitude", "0.5"));
            var u = b.Add("tl072", 8, 0);
            var rf = b.Add("res-100000", 10, 7);
            var rg = b.Add("res-10000", 2, 11, 270);
            var rl = b.Add("res-10000", 16, 4, 270);

            b.Label(vp, "+", "V+");
            b.Gnd(vp, "-");
            b.Label(vn, "-", "V-");
            var vnPlus = b.Pin(vn, "+");
            var g = b.Add("gnd", vnPlus.X + 3, vnPlus.Y - 1);
            b.Path(vnPlus, b.Pin(g, "GND"));
            b.Label(u, "V+", "V+");
            b.Label(u, "V-", "V-");
            b.Wire(sig, "+", u, "IN+", verticalFirst: true);
            b.Gnd(sig, "-");
            b.Wire(u, "OUT", rf, "B", verticalFirst: false);
            b.Wire(rf, "A", u, "IN-", verticalFirst: false);
            b.Wire(rg, "A", rf, "A", verticalFirst: true);
            b.Gnd(rg, "B");
            b.Wire(u, "OUT", rl, "A");
            b.Gnd(rl, "B");
            b.Label(u, "OUT", "VOUT");
            return b.S;
        }

        private static Schematic PowerSupply()
        {
            var b = new B("7805 Güç Kaynağı");
            var bat = b.Add("bat-12v-sla", 0, 0);
            var cin = b.Add("ecap-100u", 7, 3, 270);
            var reg = b.Add("lm7805", 14, -3);
            var cout = b.Add("cap-100n", 21, 3, 270);
            var load = b.Add("res-47", 27, 3, 270, ("power", "1"));
            var rled = b.Add("res-1000", 33, 0, 270);
            var led = b.Add("led5-green", 33, 8, 270);

            b.Wire(bat, "+", reg, "IN", verticalFirst: true);
            b.Wire(cin, "+", reg, "IN", verticalFirst: true);
            b.Gnd(bat, "-");
            b.Gnd(cin, "-");
            b.Gnd(reg, "GND");
            b.Wire(reg, "OUT", cout, "A");
            b.Wire(reg, "OUT", load, "A");
            b.Wire(reg, "OUT", rled, "A");
            b.Gnd(cout, "B");
            b.Gnd(load, "B");
            b.Wire(rled, "B", led, "A");
            b.Gnd(led, "K");
            b.Label(reg, "OUT", "5V");
            return b.S;
        }

        private static Schematic RcFilter()
        {
            var b = new B("RC Alçak Geçiren Filtre");
            var gen = b.Add("siggen-square", 0, 0, 0, ("frequency", "200"));
            var r = b.Add("res-1000", 8, -3);
            var c = b.Add("cap-1u", 14, 1, 270);
            b.Wire(gen, "+", r, "A", verticalFirst: true);
            b.Wire(r, "B", c, "A");
            b.Gnd(gen, "-");
            b.Gnd(c, "B");
            b.Label(r, "A", "GİRİŞ");
            b.Label(c, "A", "ÇIKIŞ");
            return b.S;
        }

        private static Schematic RelayLamp()
        {
            var b = new B("Röle ile Lamba");
            var psu = b.Add("psu-5v", -12, 0);
            var btn = b.Add("tact-6mm", -4, -6);
            var rb = b.Add("res-1000", 4, 2);
            var q = b.Add("2n2222a", 11, 2);
            var k = b.Add("relay-srd-05", 18, -10);
            var d = b.Add("1n4007", 10, -10, 90);
            var lamp = b.Add("lamp-12v-5w", 28, -6, 270);
            var bat = b.Add("bat-12v-sla", 34, 0);

            // Control side: button -> base resistor -> NPN low-side switch for the coil.
            b.Wire(psu, "+", btn, "A", verticalFirst: true);
            b.Label(psu, "+", "5V");
            b.Gnd(psu, "-");
            b.Wire(btn, "B", rb, "A");
            b.Wire(rb, "B", q, "B");
            b.Gnd(q, "E");
            b.Wire(q, "C", k, "COIL2", verticalFirst: true);
            b.Rail(k, "COIL1", "5V");
            b.Wire(d, "A", k, "COIL2", verticalFirst: true);
            b.Wire(d, "K", k, "COIL1", verticalFirst: true);

            // Load side: battery -> lamp -> NO contact; COM to ground.
            b.Wire(lamp, "A", k, "NO", verticalFirst: true);
            b.Wire(lamp, "B", bat, "+");
            b.Gnd(bat, "-");
            var com = b.Pin(k, "COM");
            var g = b.Add("gnd", com.X + 3, com.Y + 11);
            b.Path(com, b.Pin(g, "GND"));
            return b.S;
        }

        private static Schematic NandLogic()
        {
            var b = new B("NAND Kapısı");
            var psu = b.Add("psu-5v", -14, 0);
            var s1 = b.Add("toggle-spst", -4, -6);
            var s2 = b.Add("toggle-spst", -4, 4);
            var r1 = b.Add("res-10000", 3, -1, 270);
            var r2 = b.Add("res-10000", 6, 9, 270);
            var u = b.Add("74hc00", 12, 0);
            var rl = b.Add("res-330", 20, 0);
            var led = b.Add("led5-blue", 26, 4, 270);

            b.Rail(psu, "+", "5V");
            b.Gnd(psu, "-");
            b.Rail(s1, "A", "5V");
            b.Rail(s2, "A", "5V");
            b.Wire(s1, "B", u, "A");
            b.Wire(s2, "B", u, "B");
            b.Wire(r1, "A", s1, "B", verticalFirst: true);
            b.Wire(r2, "A", s2, "B", verticalFirst: true);
            b.Gnd(r1, "B");
            b.Gnd(r2, "B");
            b.Rail(u, "VCC", "5V");
            b.Gnd(u, "GND");
            b.Wire(u, "Y", rl, "A");
            b.Wire(rl, "B", led, "A");
            b.Gnd(led, "K");
            return b.S;
        }
    }
}
