using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Sandbox
{
    public static partial class WorldExamples
    {
        /// <summary>
        /// [surface] Materials and finishes side by side: a row of parts in their finishes (wood grain, carbon twill,
        /// brushed and polished aluminium, black anodising, rusty steel, kraft tube, rubber, printed PLA), and two
        /// identical aluminium blocks that start at 150 °C — one polished (ε ≈ 0.05), one anodised black
        /// (ε ≈ 0.85). The anodised block loses its heat by radiation as well as by convection and cools clearly
        /// faster; the thermal camera shows it.
        /// </summary>
        public static WorldDocument Surfaces(PartLibrary lib)
        {
            _ = lib;
            var w = new WorldDocument { Title = "Malzeme ve yüzey vitrini" };
            var table = w.AddBody("box", 0.62, 0.30, 0.018, new WorldPose(), "MDF");
            table.Name = "Masa (MDF)";
            table.IsStatic = true;
            table.Parameters["surface"] = "mat-boya";
            table.Color = "#4A4D55";

            (string Name, string Shape, string Material, string Surface, string? Colour, double Sx, double Sy, double Sz)[] row =
            {
                ("Ahşap blok", "box", "AHŞAP", "ahsap", null, 0.06, 0.04, 0.03),
                ("Karbon plaka", "box", "KARBON", "karbon", null, 0.06, 0.04, 0.004),
                ("Fırçalanmış alüminyum", "box", "ALÜMİNYUM", "fircalanmis", null, 0.06, 0.04, 0.02),
                ("Paslı çelik", "box", "ÇELİK", "oksitli", "#7A7F88", 0.05, 0.05, 0.02),
                ("Karton boru", "cylinder", "KARTON", "kraft", "#C9A56E", 0.041, 0.041, 0.09),
                ("Kauçuk takoz", "box", "KAUÇUK", "kaucuk", null, 0.04, 0.04, 0.02),
                ("PLA baskı", "box", "PLA", "baski", "#E5484D", 0.05, 0.04, 0.03)
            };
            for (int i = 0; i < row.Length; i++)
            {
                var r = row[i];
                var b = w.AddBody(r.Shape, r.Sx, r.Sy, r.Sz, new WorldPose { X = -0.26 + i * 0.075, Y = 0.07, Z = 0.018 }, r.Material);
                b.Name = r.Name;
                b.Parameters["surface"] = r.Surface;
                if (r.Colour != null) b.Color = r.Colour;
            }

            // Same aluminium, same size, same start temperature: only the finish differs.
            var polished = w.AddBody("box", 0.05, 0.05, 0.05, new WorldPose { X = -0.1, Y = -0.07, Z = 0.018 }, "ALÜMİNYUM");
            polished.Name = "Parlatılmış blok (150 °C)";
            polished.Parameters["surface"] = "parlatilmis";
            polished.Parameters["temperature"] = "150";
            var anodised = w.AddBody("box", 0.05, 0.05, 0.05, new WorldPose { X = 0.1, Y = -0.07, Z = 0.018 }, "ALÜMİNYUM");
            anodised.Name = "Siyah eloksal blok (150 °C)";
            anodised.Parameters["surface"] = "eloksal";
            anodised.Parameters["temperature"] = "150";
            anodised.Color = "#2B2D33";
            return w;
        }
    }
}
