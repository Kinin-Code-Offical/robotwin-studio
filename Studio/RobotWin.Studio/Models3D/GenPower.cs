using System;

namespace RobotWin.Studio.Models3D
{
    internal static partial class Gen
    {
        /// <summary>A regulator module's board and its four pads (mm, the board's centre at the origin, pads from IN+ round).</summary>
        internal static (double Length, double Width, (double X, double Y)[] Pads) RegulatorBoard(string model) => Key(model) switch
        {
            "MP1584" => (22, 17, new[] { (-9.5, 6.0), (-9.5, -6.0), (9.5, 6.0), (9.5, -6.0) }),
            "MT3608" => (37, 17, new[] { (-16.5, 6.0), (-16.5, -6.0), (16.5, 6.0), (16.5, -6.0) }),
            "XL4015" => (51, 26.5, new[] { (-23.5, 10.0), (-23.5, -10.0), (23.5, 10.0), (23.5, -10.0) }),
            _ => (43, 21, new[] { (-19.5, 7.5), (-19.5, -7.5), (19.5, 7.5), (19.5, -7.5) })   // LM2596, XL6009
        };

        private static string Key(string model)
        {
            string m = (model ?? "").ToUpperInvariant();
            if (m.StartsWith("MP1584", StringComparison.Ordinal)) return "MP1584";
            if (m.StartsWith("MT3608", StringComparison.Ordinal)) return "MT3608";
            if (m.StartsWith("XL4015", StringComparison.Ordinal)) return "XL4015";
            if (m.StartsWith("XL6009", StringComparison.Ordinal)) return "XL6009";
            return "LM2596";
        }

        /// <summary>
        /// [smps] A switching regulator module as sold: its board with the four solder pads, the chip, the inductor (a
        /// shielded drum, or the XL4015's toroid), the electrolytic or ceramic capacitors, the Schottky diode, the blue
        /// multi-turn trimmer that sets the output, and the power LED.
        /// </summary>
        internal static void RegulatorModule(PartBuilder b, string model)
        {
            string key = Key(model);
            var (l, w, pads) = RegulatorBoard(model);
            const double t = 1.6;
            uint board = key == "MP1584" ? 0x1E6B3Cu : 0x1F4FA0u;
            b[Mat.Plastic(board, 0.25, 20)].RoundedBox(0, 0, 0, l, w, t, 0.8);
            var silver = b[Mat.Metal(0xD8DCE0u, 0.9, 60)];
            var hole = b[Mat.Plastic(0x101012u, 0.1)];
            foreach (var (x, y) in pads)
            {
                silver.CylinderZ(x, y, t - 0.01, t + 0.06, 1.6, 20);
                hole.CylinderZ(x, y, t + 0.06, t + 0.08, 0.6, 12);
            }

            var black = b[Mat.Plastic(0x151517u, 0.3, 10)];
            var tab = b[Mat.Metal(0xB9BDC2u, 0.8, 40)];
            var dark = b[Mat.Plastic(0x1C1C1Eu, 0.25)];
            switch (key)
            {
                case "MP1584":
                    black.Box(-1.5, -3.0, t + 0.75, 4.9, 3.9, 1.5);                       // MP1584EN, SOIC-8 EP
                    dark.RoundedBox(4.0, 2.5, t, 6.0, 6.0, 3.0, 0.8);                    // 4.7 µH shielded inductor
                    Ceramic(b, -5.5, 2.0, t);
                    Ceramic(b, -5.5, -2.0, t);
                    Ceramic(b, 7.0, -3.5, t);
                    black.Box(0.5, 4.8, t + 1.0, 3.6, 1.6, 2.0);                         // SS34 Schottky
                    Trimmer(b, -2.0, 5.0, t, 3.0, 3.0, 2.2);                             // small SMD trimmer
                    break;
                case "MT3608":
                    black.Box(-4.0, 0, t + 0.5, 3.0, 1.7, 1.0);                          // MT3608, SOT-23-6
                    dark.RoundedBox(4.0, -1.5, t, 5.8, 5.8, 3.0, 0.6);                   // 22 µH drum
                    Ceramic(b, -10.5, 3.0, t);
                    Ceramic(b, 11.0, 3.5, t);
                    black.Box(-3.0, -5.0, t + 1.0, 4.4, 2.6, 2.0);                       // SS34 Schottky
                    Trimmer(b, 9.5, -3.0, t, 9.5, 4.8, 10.0);                            // 3296W multi-turn
                    break;
                case "XL4015":
                    // TO-263 chip, its tab soldered to the board.
                    tab.Box(-5.0, -5.5, t + 0.25, 10.2, 8.5, 0.5);
                    black.Box(-5.0, -6.5, t + 2.2, 10.0, 7.5, 4.4);
                    // 47 µH toroid: a yellow-white ring wound with copper.
                    Toroid(b, 8.0, 1.0, t, 8.5, 3.4);
                    Electrolytic(b, -16.0, 5.5, t, 5.0, 13);
                    Electrolytic(b, 16.5, -6.5, t, 5.0, 13);
                    black.Box(4.5, -8.0, t + 1.1, 7.0, 6.0, 2.2);                        // SK54 Schottky
                    Trimmer(b, -13.0, -7.0, t, 9.5, 4.8, 10.0);
                    break;
                default:
                    // LM2596S / XL6009E1 in TO-263, its tab on the board.
                    tab.Box(-6.0, -4.5, t + 0.25, 10.2, 8.5, 0.5);
                    black.Box(-6.0, -5.5, t + 2.2, 10.0, 7.5, 4.4);
                    dark.RoundedBox(6.0, 3.0, t, 12.0, 12.0, 7.0, 1.2);                   // CD105 33 µH shielded drum
                    b[Mat.Plastic(0xE9E9E4u, 0.3)].TopQuad(6.0, 3.0, t + 7.02, 5.0, 2.0);  // its printed value
                    Electrolytic(b, -14.5, 4.0, t, 4.0, 11);
                    Electrolytic(b, 15.0, -4.5, t, 4.0, 11);
                    black.Box(5.5, -6.5, t + 1.1, 4.4, 2.6, 2.2);                        // SS34 Schottky
                    Trimmer(b, -15.5, -5.0, t, 4.8, 9.5, 10.0);
                    break;
            }

            // The power LED and its resistor.
            b[Mat.Plastic(0xE8E8E8u, 0.4)].Box(l / 2 - 5.5, -w / 2 + 2.0, t + 0.3, 1.6, 0.8, 0.6);
            b[Mat.Plastic(0x111113u, 0.2)].Box(l / 2 - 8.0, -w / 2 + 2.0, t + 0.25, 1.6, 0.8, 0.5);
        }

        /// <summary>An aluminium electrolytic: a dark sleeve, the silver top with its scored cross.</summary>
        private static void Electrolytic(PartBuilder b, double x, double y, double z0, double r, double h)
        {
            b[Mat.Plastic(0x1D1F2Au, 0.4, 30)].CylinderZ(x, y, z0, z0 + h - 0.6, r, 28);
            b[Mat.Metal(0xC6CACFu, 0.8, 40)].CylinderZ(x, y, z0 + h - 0.6, z0 + h, r * 0.96, 28);
            var score = b[Mat.Plastic(0x8E9297u, 0.2)];
            score.Box(x, y, z0 + h + 0.01, r * 1.4, 0.25, 0.04);
            score.Box(x, y, z0 + h + 0.01, 0.25, r * 1.4, 0.04);
            b[Mat.Plastic(0xC9CED6u, 0.3)].Box(x - r * 0.72, y, z0 + (h - 0.6) / 2, 0.25, r * 0.9, h - 1.2);   // the minus stripe
        }

        /// <summary>A small ceramic capacitor (1206).</summary>
        private static void Ceramic(PartBuilder b, double x, double y, double z0)
        {
            b[Mat.Plastic(0xB8935Au, 0.3)].Box(x, y, z0 + 0.4, 3.2, 1.6, 0.8);
            var end = b[Mat.Metal(0xD0D3D6u, 0.8, 40)];
            end.Box(x - 1.45, y, z0 + 0.42, 0.35, 1.62, 0.84);
            end.Box(x + 1.45, y, z0 + 0.42, 0.35, 1.62, 0.84);
        }

        /// <summary>The blue trimmer (a 3296W multi-turn, or a small SMD one), its brass adjusting screw on top.</summary>
        private static void Trimmer(PartBuilder b, double x, double y, double z0, double sx, double sy, double h)
        {
            b[Mat.Plastic(0x2D5FD0u, 0.35, 25)].Box(x, y, z0 + h / 2, sx, sy, h);
            bool tall = h > 5;
            double sxw = tall ? x + sx / 2 - 1.6 : x, syw = tall ? y : y;
            b[Mat.Metal(0xC9A54Cu, 0.8, 50)].CylinderZ(sxw, syw, z0 + h, z0 + h + 0.8, tall ? 1.1 : 0.9, 16);
            b[Mat.Plastic(0x3A2E14u, 0.1)].Box(sxw, syw, z0 + h + 0.82, tall ? 1.8 : 1.4, 0.3, 0.05);
        }

        /// <summary>A toroidal inductor: a yellow-and-white powdered-iron ring under its copper winding.</summary>
        private static void Toroid(PartBuilder b, double x, double y, double z0, double R, double r)
        {
            var copper = b[Mat.Metal(0xB86B3Au, 0.8, 50)];
            using (copper.Frame.At(Mx.T(x, y, z0 + r))) copper.Torus(R, r, 40, 14, 0);
            // The core shows yellow and white between the turns: a narrower ring inside the winding's hole.
            var core = b[Mat.Plastic(0xE4D58Au, 0.3)];
            using (core.Frame.At(Mx.T(x, y, z0 + r))) core.Torus(R - r * 0.55, r * 0.5, 32, 10, 0);
        }
    }
}
