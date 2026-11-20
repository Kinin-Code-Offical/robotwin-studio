using System;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>[objs] Light sources that also work as free-standing world objects.</summary>
    internal static partial class StandardParts
    {
        private static void RegisterLights(PartLibrary lib)
        {
            // 5 mm RGB LED, common cathode (R, K = longest lead, G, B).
            Def(lib, "led-rgb", "RGB LED (ortak katot)", Semiconductor, "D", SymbolKind.Module, "HDR-1x4", "model",
                c =>
                {
                    double i = c.Num("if", 0.02);
                    c.Add(new Led(c.Sub("R"), c.Pin("R"), c.Pin("K"), c.Num("vfr", 2.0), i));
                    c.Add(new Led(c.Sub("G"), c.Pin("G"), c.Pin("K"), c.Num("vfg", 3.0), i));
                    c.Add(new Led(c.Sub("B"), c.Pin("B"), c.Pin("K"), c.Num("vfb", 3.1), i));
                },
                P("model", "Model", "5 mm RGB, ortak katot"), P("vfr", "Kırmızı Vf", "2.0", "V"), P("vfg", "Yeşil Vf", "3.0", "V"),
                P("vfb", "Mavi Vf", "3.1", "V"), P("if", "Nominal akım (renk başına)", "20m", "A"), P("lumens", "Işık akısı (beyaz, tümü)", "4", "lm"),
                P("pinmap", "Pin eşleme", "R:1,K:2,G:3,B:4"))
                .Pin("R", "1", -4, -3).Pin("K", "2", 4, 0).Pin("G", "3", -4, 0).Pin("B", "4", -4, 3).Size(3, 4);
            Preset(lib, "led-rgb-5mm", "RGB LED 5 mm (ortak katot)", "led-rgb", Semiconductor, "Üç renk tek gövdede; R, G, B ayrı sürülür, 20 mA/renk");

            // High-power LED on a 20 mm aluminium star (Luxeon / Cree class).
            Def(lib, "led-power", "Güç LED'i (yıldız PCB)", Semiconductor, "D", SymbolKind.Led, "WIRE-2", "model",
                c =>
                {
                    double i = c.Num("if", 0.35), vf = c.Num("vf", 3.2), rs = c.Num("rs", 0.8);
                    // The indicator-LED defaults (8 Ω bulk resistance) do not fit a 0.7 A die: build, then re-fit
                    // the diode to Vf at the rated current with the power die's own resistance.
                    var led = c.Add(new Led(c.RefDes, c.Pin("A"), c.Pin("K"), vf + 8.0 * i, i));
                    led.SeriesResistance = rs;
                    led.BandGap = Math.Max(1.4, vf * 0.95);
                    led.SetForwardPoint(vf, i);
                    // Junction to air through the star (and a small heat sink for 3 W).
                    led.ThermalResistance = c.Num("rth", 40);
                    led.ThermalCapacitance = c.Num("cth", 0.6);
                    led.MaxCurrent = i * 1.4;
                    led.MaxJunctionTempC = 150;
                },
                P("model", "Model", "1 W beyaz"), P("color", "Renk", "white", "", "white", "warm", "red", "green", "blue"),
                P("vf", "İleri gerilim", "3.2", "V"), P("if", "Nominal akım", "350m", "A"), P("lumens", "Işık akısı", "110", "lm"),
                P("rth", "Isıl direnç (eklem → hava)", "40", "K/W"), P("cth", "Isıl kapasite", "0.6", "J/K"), P("rs", "Seri direnç", "0.8", "Ω"))
                .TwoPin("A", "K");
            Preset(lib, "led-1w-white", "Güç LED'i 1 W Beyaz", "led-power", Semiconductor, "3.2 V 350 mA, ≈110 lm, 20 mm yıldız PCB");
            Preset(lib, "led-3w-white", "Güç LED'i 3 W Beyaz", "led-power", Semiconductor, "3.4 V 700 mA, ≈220 lm, yıldız + soğutucu",
                "model=3 W beyaz;vf=3.4;if=700m;lumens=220;rth=28;cth=2");
            Preset(lib, "led-3w-red", "Güç LED'i 3 W Kırmızı", "led-power", Semiconductor, "2.3 V 700 mA, ≈70 lm",
                "model=3 W kırmızı;color=red;vf=2.3;if=700m;lumens=70;rth=28;cth=2");
            Preset(lib, "led-3w-blue", "Güç LED'i 3 W Mavi", "led-power", Semiconductor, "3.4 V 700 mA, ≈40 lm",
                "model=3 W mavi;color=blue;vf=3.4;if=700m;lumens=40;rth=28;cth=2");

            // 12 V LED strip (5050 SMD): every 50 mm cut section is three LEDs in series with a resistor.
            Def(lib, "led-strip", "12 V LED Şerit (5050)", Electromechanical, "LS", SymbolKind.Module, "WIRE-2", "model",
                c =>
                {
                    int n = Math.Max(1, Math.Min(10, (int)Math.Round(c.Num("sections", 3))));
                    double i = c.Num("if", 0.04), vf = c.Num("vf", 3.1);
                    for (int s = 0; s < n; s++)
                    {
                        string k = "S" + s;
                        int a = c.Internal(k + "a"), b = c.Internal(k + "b"), r = c.Internal(k + "c");
                        c.Add(new Led(c.Sub(k + "L1"), c.Pin("+"), a, vf, i));
                        c.Add(new Led(c.Sub(k + "L2"), a, b, vf, i));
                        c.Add(new Led(c.Sub(k + "L3"), b, r, vf, i));
                        c.Add(new Resistor(c.Sub(k + "R"), r, c.Pin("-"), c.Num("resistor", 82)) { PowerRating = 0.25 });
                    }
                },
                P("model", "Model", "5050 beyaz, 60 LED/m"), P("color", "Renk", "white", "", "white", "warm", "red", "green", "blue"),
                P("sections", "Kesit sayısı (her biri 50 mm, 3 LED)", "3"), P("vf", "LED Vf", "3.1", "V"), P("if", "LED akımı", "40m", "A"),
                P("resistor", "Kesit direnci", "82", "Ω"), P("lumens", "LED başına ışık akısı", "14", "lm"))
                .TwoPin("+", "-");
            Preset(lib, "led-strip-12v", "12 V LED Şerit 150 mm", "led-strip", Electromechanical, "5050 beyaz, 3 kesit × 3 LED, ≈0.12 A @ 12 V");
            Preset(lib, "led-strip-12v-red", "12 V LED Şerit 150 mm Kırmızı", "led-strip", Electromechanical, "5050 kırmızı, 3 kesit",
                "model=5050 kırmızı;color=red;vf=2.1;resistor=150;lumens=5");

            // Flashlight bulb (E10, 3.8 V 0.3 A).
            Preset(lib, "lamp-3v8-0w3", "Fener Ampulü 3.8 V 0.3 A", "lamp", Electromechanical, "E10 duylu akkor ampul, ≈12 lm", "voltage=3.8;power=1.14");
        }
    }
}
