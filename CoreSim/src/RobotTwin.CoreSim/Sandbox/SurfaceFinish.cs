using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [surface] How a part's surface is finished ("surface" parameter of a world object). A finish is not only
    /// its look: it sets the surface's thermal emissivity, so a polished aluminium part barely radiates
    /// (ε ≈ 0.05) while the same part anodised black radiates almost like a black body (ε ≈ 0.85) — the
    /// heat network, the cooling of a motor casing and the thermal camera all follow it. Values are typical
    /// total hemispherical emissivities near room temperature (Incropera, Appendix A.11; Modest, Table B.1).
    /// </summary>
    public sealed class SurfaceFinish
    {
        public SurfaceFinish(string id, string name, double? emissivity, string note)
        {
            Id = id;
            Name = name;
            Emissivity = emissivity;
            Note = note;
        }

        public string Id { get; }
        public string Name { get; }

        /// <summary>Emissivity of the finished surface; null keeps the material's own.</summary>
        public double? Emissivity { get; }

        public string Note { get; }

        public static IReadOnlyList<SurfaceFinish> All { get; } = new[]
        {
            new SurfaceFinish(string.Empty, "Malzemeden", null, "Malzemenin kendi yüzeyi."),
            new SurfaceFinish("mat-boya", "Mat boya", 0.92, "Boyalı yüzey: ışığı dağıtır, iyi ışınım yapar."),
            new SurfaceFinish("parlak-boya", "Parlak boya / vernik", 0.88, "Parlak kaplama: yansıma güçlü, ışınım yine yüksek."),
            new SurfaceFinish("parlatilmis", "Parlatılmış metal", 0.05, "Ayna gibi metal: ısı ışınımı çok az, yavaş soğur."),
            new SurfaceFinish("fircalanmis", "Fırçalanmış metal", 0.12, "İnce çizgili metal: ışınımı düşük."),
            new SurfaceFinish("eloksal", "Eloksal (siyah)", 0.85, "Anodize alüminyum: ışınımla iyi soğur."),
            new SurfaceFinish("oksitli", "Oksitli / paslı", 0.75, "Oksit tabakası ışınımı artırır."),
            new SurfaceFinish("ahsap", "Ahşap damarı", 0.90, "Verniksiz ahşap."),
            new SurfaceFinish("karbon", "Karbon örgü", 0.85, "Reçineli karbon kumaş (2×2 dimi)."),
            new SurfaceFinish("kraft", "Kraft kâğıt", 0.90, "Karton boru, kraft kâğıt."),
            new SurfaceFinish("kaucuk", "Kauçuk kaplama", 0.93, "Mat siyah kauçuk."),
            new SurfaceFinish("baski", "3B baskı katmanları", null, "0,2 mm katman çizgileri; ışınım malzemeninki.")
        };

        public static SurfaceFinish? For(string? id) =>
            string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(f => string.Equals(f.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Emissivity of an object's surface: its finish's, else its material's.</summary>
        public static double EmissivityOf(WorldObject o, double materialEmissivity) =>
            For(o.Parameters.TryGetValue("surface", out var s) ? s : null)?.Emissivity ?? materialEmissivity;
    }
}
