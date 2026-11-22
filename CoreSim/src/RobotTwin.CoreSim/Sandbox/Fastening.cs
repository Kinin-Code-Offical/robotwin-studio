using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [fasten] How two parts held by a fixed joint are fastened ("Fastening" of a <see cref="WorldJoint"/>): screws,
    /// glues, tape, a zip tie, solder. Each one is real physics in three places:
    /// <list type="bullet">
    /// <item>Strength: screws by the load one screw carries (pull-out / shear of the weaker side) times their count;
    /// adhesives by their lap-shear strength over the bonded area (the smaller part's largest face, half covered).
    /// The joint breaks when the force through it exceeds this.</item>
    /// <item>Heat: the contact passes heat as a layer of the fastener (k / thickness) over the share of the
    /// footprint it really touches — glue conducts far better than two parts touching at screw heads.</item>
    /// <item>Temperature: adhesives and plastics soften. Strength falls linearly from the softening to the failure
    /// temperature of the joint (the mean of the two parts in the heat network); past it the joint lets go.</item>
    /// </list>
    /// Typical values: Loctite and 3M datasheets for lap shear on plastics, Bossard / Fastenal for M3 screws.
    /// </summary>
    public sealed class Fastening
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;

        /// <summary>Load one screw / tie carries (N); 0 for adhesives.</summary>
        public double PerPiece { get; init; }
        public int Pieces { get; init; } = 1;

        /// <summary>Lap-shear strength of an adhesive (Pa); 0 for mechanical fasteners.</summary>
        public double Strength { get; init; }

        /// <summary>Conductance of the fastening layer (W/m²·K) and the share of the footprint it touches.</summary>
        public double Conductance { get; init; } = 400;
        public double ContactShare { get; init; } = 0.15;

        /// <summary>Strength starts to fall here and is gone here (°C); NaN = no softening.</summary>
        public double SoftenC { get; init; } = double.NaN;
        public double FailC { get; init; } = double.NaN;

        public string Note { get; init; } = string.Empty;

        /// <summary>Share of an adhesive's footprint that is really wetted.</summary>
        public const double Coverage = 0.5;

        /// <summary>Break force (N) of this fastening between parts whose bonded area is <paramref name="area"/> (m²); 0 = unbreakable.</summary>
        public double BreakForce(double area) => Id.Length == 0 ? 0 : PerPiece > 0 ? PerPiece * Pieces : Strength * Coverage * Math.Max(1e-6, area);

        /// <summary>Remaining share of the strength at joint temperature <paramref name="c"/> (°C).</summary>
        public double StrengthFactor(double c)
        {
            if (double.IsNaN(SoftenC) || c <= SoftenC) return 1;
            if (c >= FailC) return 0;
            return 1 - (c - SoftenC) / (FailC - SoftenC);
        }

        public static IReadOnlyList<Fastening> All { get; } = new[]
        {
            new Fastening { Id = string.Empty, Name = "Sabit (belirtilmemiş)", Note = "Kırılmaz; iki parça vida başlarından değer gibi ısı geçirir." },
            new Fastening { Id = "vida-somun", Name = "M3 vida + somun (2 adet)", PerPiece = 1000, Pieces = 2, Conductance = 400, ContactShare = 0.15,
                Note = "Çelik M3 (8.8) somunla sıkılı: parça başına ≈ 1 kN (plastik delik yatağı ezilmeden önce)." },
            new Fastening { Id = "vida-plastik", Name = "Plastiğe vida (M3, 2 adet, yivli)", PerPiece = 250, Pieces = 2, Conductance = 400, ContactShare = 0.12,
                SoftenC = 60, FailC = 110, Note = "Kendinden yivli M3 PLA/ABS'ye: sökülme ≈ 250 N; plastik ısınınca diş tutuşu azalır." },
            new Fastening { Id = "siyanoakrilat", Name = "Japon yapıştırıcısı (siyanoakrilat)", Strength = 6e6, Conductance = 4000, ContactShare = 0.5,
                SoftenC = 80, FailC = 120, Note = "Plastikte kayma dayanımı ≈ 6 MPa, ince katman (≈ 50 µm) ısıyı iyi geçirir, 80 °C'den sonra zayıflar." },
            new Fastening { Id = "epoksi", Name = "Epoksi (iki bileşenli)", Strength = 15e6, Conductance = 2000, ContactShare = 0.7,
                SoftenC = 60, FailC = 110, Note = "≈ 15 MPa; camsı geçişi ≈ 60 °C olan 5 dakikalık epoksi." },
            new Fastening { Id = "sicak-tutkal", Name = "Sıcak tutkal (EVA)", Strength = 1.5e6, Conductance = 400, ContactShare = 0.3,
                SoftenC = 55, FailC = 85, Note = "≈ 1,5 MPa; kalın katman, 55 °C'den sonra yumuşar, 85 °C'de akar." },
            new Fastening { Id = "bant", Name = "Çift taraflı köpük bant", Strength = 0.5e6, Conductance = 100, ContactShare = 0.8,
                SoftenC = 90, FailC = 150, Note = "Akrilik köpük bant ≈ 0,5 MPa; köpük ısıyı az geçirir." },
            new Fastening { Id = "kelepce", Name = "Kelepçe (plastik zip bağı)", PerPiece = 220, Pieces = 1, Conductance = 400, ContactShare = 0.05,
                SoftenC = 80, FailC = 160, Note = "4,8 mm naylon bağ ≈ 22 kg; temas çok az." },
            new Fastening { Id = "lehim", Name = "Lehim (Sn63/Pb37)", Strength = 30e6, Conductance = 200000, ContactShare = 0.3,
                SoftenC = 150, FailC = 183, Note = "Metal parçalar için; ≈ 30 MPa, 183 °C'de erir, ısıyı çok iyi geçirir." }
        };

        public static Fastening For(string? id) =>
            All.FirstOrDefault(f => string.Equals(f.Id, (id ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) ?? All[0];

        /// <summary>Largest face area of an object (m²): what it can offer to a glued or screwed face.</summary>
        public static double FaceArea(WorldObject o) => o.Shape switch
        {
            "cylinder" or "wheel" => Math.Max(Math.PI * o.SizeX * o.SizeX / 4, o.SizeX * o.SizeZ),
            "sphere" => Math.PI * o.SizeX * o.SizeX / 16,   // a sphere touches on a small cap
            _ => Math.Max(o.SizeX * o.SizeY, Math.Max(o.SizeX * o.SizeZ, o.SizeY * o.SizeZ))
        };
    }
}
