using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>[drive] What a power-transmission part is.</summary>
    public enum DrivePartKind
    {
        /// <summary>Involute spur gear (module, teeth, face width, bore).</summary>
        Gear,

        /// <summary>GT2 timing pulley (2 mm pitch).</summary>
        Pulley,

        /// <summary>Radial ball bearing (608ZZ …): the shaft turns in its bore.</summary>
        Bearing,

        /// <summary>A bearing in a cast housing that bolts down (KP08 pillow block, KFL08 flange).</summary>
        BearingHousing,

        /// <summary>Hardened steel rod: a shaft or a linear guide rod.</summary>
        Shaft,

        /// <summary>Trapezoidal lead screw (T8).</summary>
        LeadScrew,

        /// <summary>Flanged brass nut of a lead screw.</summary>
        LeadNut,

        /// <summary>Linear ball bearing (LM8UU) that slides along a rod.</summary>
        LinearBearing,

        /// <summary>Shaft collar: a ring clamped on a shaft with a set screw.</summary>
        Collar,

        /// <summary>Flexible shaft coupler (motor 5 mm to screw 8 mm).</summary>
        Coupler
    }

    /// <summary>
    /// [drive] A gear, pulley, bearing, shaft, lead screw or one of their fittings, with its real dimensions (m), material
    /// and mass. Frame: the axis along local Z, the bottom face on z = 0, centred in X/Y; a housing (KP08) stands on its
    /// base with the bore along local Y at <see cref="AxisHeight"/>.
    /// </summary>
    public sealed class DrivePartSpec
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public DrivePartKind Kind { get; init; }

        /// <summary>World material (density, friction, look).</summary>
        public string Material { get; init; } = "steel";

        public string Color { get; init; } = "#B8BCC2";

        /// <summary>Outer diameter (a gear's tip circle, a pulley's, a bearing's); a housing's base length.</summary>
        public double Diameter { get; init; }

        /// <summary>Along the axis: face width, ring width, rod length; a housing's base width.</summary>
        public double Length { get; init; }

        /// <summary>The bore a shaft fits (m); a shaft's or screw's own diameter.</summary>
        public double Bore { get; init; }

        /// <summary>A coupler's other bore (m).</summary>
        public double Bore2 { get; init; }

        /// <summary>Gear module (m): the pitch diameter is module × teeth.</summary>
        public double Module { get; init; }

        /// <summary>Teeth of a gear or a timing pulley.</summary>
        public int Teeth { get; init; }

        /// <summary>A lead screw's or nut's lead (m a turn) and thread pitch (m).</summary>
        public double Lead { get; init; }

        public double Pitch { get; init; }

        /// <summary>Hub on one side (gears, pulleys): its diameter and length (m); 0 = none.</summary>
        public double HubDiameter { get; init; }

        public double HubLength { get; init; }

        /// <summary>The bearing inside a bearing, a housing or a linear bearing (Sandbox/Bearings.cs).</summary>
        public string? Bearing { get; init; }

        /// <summary>A housing's height and the bore axis's height above its base (m).</summary>
        public double Height { get; init; }

        public double AxisHeight { get; init; }

        /// <summary>Datasheet mass (kg); 0 = from the volume and the material's density.</summary>
        public double DatasheetMass { get; init; }

        public double Radius => Diameter / 2;

        /// <summary>A gear's pitch diameter (module × teeth), a GT2 pulley's (teeth × 2 mm / π); else the outer one.</summary>
        public double PitchDiameter => Kind switch
        {
            DrivePartKind.Gear => Module * Teeth,
            DrivePartKind.Pulley => Teeth * 0.002 / Math.PI,
            _ => Diameter
        };

        /// <summary>Turns about its axis on a shaft (and so can be fixed to one): gears, pulleys, collars, couplers.</summary>
        public bool MountsOnShaft => Kind is DrivePartKind.Gear or DrivePartKind.Pulley or DrivePartKind.Collar or DrivePartKind.Coupler;

        /// <summary>A rod something turns or slides on.</summary>
        public bool IsRod => Kind is DrivePartKind.Shaft or DrivePartKind.LeadScrew;

        /// <summary>Its axis in its own frame: along Z, a housing's along Y.</summary>
        public Vec3 AxisLocal => Kind == DrivePartKind.BearingHousing ? Vec3.UnitY : Vec3.UnitZ;

        /// <summary>Where its axis starts in its own frame and how far along it the part reaches (m).</summary>
        public Vec3 AxisOrigin => Kind == DrivePartKind.BearingHousing ? new Vec3(0, -Length / 2, AxisHeight) : Vec3.Zero;

        public double AxisSpan => Kind == DrivePartKind.Coupler || Kind == DrivePartKind.BearingHousing ? Length : Length + (HubLength > 0 ? HubLength : 0);

        /// <summary>Mass: the datasheet's, or the solid's volume × the material's density.</summary>
        public double MassKg => DatasheetMass > 0 ? DatasheetMass : Volume * MechatronicSystem.DensityOf(Material);

        /// <summary>The solid's volume (m³), bores taken out.</summary>
        public double Volume
        {
            get
            {
                static double Ring(double d, double bore, double len) => Math.PI / 4 * Math.Max(0, d * d - bore * bore) * len;
                double hub = HubLength > 0 ? Ring(HubDiameter, Bore, HubLength) : 0;
                return Kind switch
                {
                    DrivePartKind.Gear => Ring(PitchDiameter, Bore, Length) + hub,
                    DrivePartKind.Pulley => Ring(Diameter, Bore, Length) + 2 * Ring(Diameter + 0.0035, Bore, 0.001) + hub,
                    DrivePartKind.Shaft => Ring(Bore, 0, Length),
                    DrivePartKind.LeadScrew => Ring(Bore - 0.001, 0, Length),
                    DrivePartKind.LeadNut => Ring(Diameter, Bore, 0.0035) + Ring(0.0102, Bore, Length - 0.0035),
                    DrivePartKind.LinearBearing => Ring(Diameter, Bore, Length) * 0.7,
                    DrivePartKind.Coupler => Ring(Diameter, (Bore + Bore2) / 2, Length) * 0.8,
                    DrivePartKind.BearingHousing => Diameter * Length * 0.005 + Ring(0.028, 0.022, Length),
                    _ => Ring(Diameter, Bore, Length)
                };
            }
        }

        /// <summary>One line of its sizes for the inspector.</summary>
        public string Sizes
        {
            get
            {
                string Mm(double m) => (m * 1000).ToString("0.##", CultureInfo.InvariantCulture);
                return Kind switch
                {
                    DrivePartKind.Gear => $"modül {Mm(Module)} · {Teeth} diş · bölüm dairesi Ø{Mm(PitchDiameter)} mm · diş genişliği {Mm(Length)} mm · delik Ø{Mm(Bore)} mm",
                    DrivePartKind.Pulley => $"GT2 · {Teeth} diş · bölüm dairesi Ø{Mm(PitchDiameter)} mm · kayış 6 mm · delik Ø{Mm(Bore)} mm",
                    DrivePartKind.Shaft => $"Ø{Mm(Bore)} × {Mm(Length)} mm",
                    DrivePartKind.LeadScrew => $"Ø{Mm(Bore)} × {Mm(Length)} mm · hatve {Mm(Lead)} mm · adım {Mm(Pitch)} mm",
                    DrivePartKind.LeadNut => $"Ø{Mm(Diameter)} flanş · hatve {Mm(Lead)} mm · delik Ø{Mm(Bore)} mm",
                    DrivePartKind.Coupler => $"Ø{Mm(Diameter)} × {Mm(Length)} mm · delikler Ø{Mm(Bore)} / Ø{Mm(Bore2)} mm",
                    DrivePartKind.BearingHousing => $"taban {Mm(Diameter)} × {Mm(Length)} mm · eksen yüksekliği {Mm(AxisHeight)} mm · delik Ø{Mm(Bore)} mm",
                    _ => $"Ø{Mm(Bore)} × Ø{Mm(Diameter)} × {Mm(Length)} mm"
                } + $" · {MassKg * 1000:0.#} g";
            }
        }
    }

    /// <summary>
    /// [drive] Catalogue of gears, pulleys, bearings, shafts, lead screws and their fittings, placed in a world as
    /// <see cref="WorldObjectKind.Body"/> objects with <see cref="WorldObject.Shape"/> = "drive" and
    /// <see cref="WorldObject.PresetId"/> = the part id. Placed as in a real machine, Sandbox/DriveAssembly.cs finds how they
    /// hold each other: a shaft turns in its bearings, a gear is fixed on its shaft, two gears mesh.
    /// </summary>
    public static class DriveParts
    {
        public const string Shape = "drive";
        public const string Group = "Güç aktarımı";

        public static IReadOnlyList<DrivePartSpec> All { get; } = Catalogue();

        public static DrivePartSpec? Find(string? id) => id == null ? null : All.FirstOrDefault(s => s.Id == id);

        public static DrivePartSpec? Of(WorldObject? o) => o != null && o.Shape == Shape ? Find(o.PresetId) : null;

        private static DrivePartSpec Gear(int teeth, double module, double bore, string material, string color, string note) => new DrivePartSpec
        {
            Id = $"gear-m{module * 1000:0.#}-z{teeth}-b{bore * 1000:0}".Replace(',', '.'),
            Name = $"Düz dişli m{module * 1000:0.#} z{teeth} (Ø{bore * 1000:0} delik)",
            Description = note,
            Kind = DrivePartKind.Gear, Module = module, Teeth = teeth, Diameter = module * (teeth + 2), Length = 0.008, Bore = bore,
            HubDiameter = Math.Min(module * (teeth - 2.5), bore + 0.010), HubLength = teeth >= 20 ? 0.007 : 0, Material = material, Color = color
        };

        private static DrivePartSpec Pulley(int teeth, double bore) => new DrivePartSpec
        {
            Id = $"pulley-gt2-{teeth}-b{bore * 1000:0}",
            Name = $"GT2 kasnak {teeth} diş (Ø{bore * 1000:0} delik)",
            Description = "Alüminyum triger kasnağı, 6 mm GT2 kayış için; iki flanş, iki setuskur.",
            Kind = DrivePartKind.Pulley, Teeth = teeth, Diameter = teeth * 0.002 / Math.PI - 0.000508, Length = 0.0075, Bore = bore,
            HubDiameter = Math.Min(teeth * 0.002 / Math.PI + 0.002, 0.026), HubLength = 0.007, Material = "aluminium", Color = "#C9CDD2"
        };

        private static DrivePartSpec Rod(double d, double length, string id, string name, string note) => new DrivePartSpec
        {
            Id = id, Name = name, Description = note, Kind = DrivePartKind.Shaft, Bore = d, Diameter = d, Length = length, Material = "steel", Color = "#D4D8DD"
        };

        private static List<DrivePartSpec> Catalogue() => new List<DrivePartSpec>
        {
            Gear(20, 0.001, 0.008, "pom", "#ECE8DC", "Asetal (POM) düz dişli, 20° basınç açısı, involüt profil. Ø8 mile setuskurla sabitlenir."),
            Gear(30, 0.001, 0.008, "pom", "#ECE8DC", "Asetal (POM) düz dişli, 20° basınç açısı, involüt profil."),
            Gear(40, 0.001, 0.008, "pom", "#ECE8DC", "Asetal (POM) düz dişli, 20° basınç açısı: z20 ile 1:2."),
            Gear(60, 0.001, 0.008, "pom", "#ECE8DC", "Asetal (POM) düz dişli, 20° basınç açısı: z20 ile 1:3."),
            Gear(12, 0.001, 0.005, "brass", "#C9A54C", "Pirinç pinyon, 5 mm motor miline (NEMA 17, 775): POM dişlileri sürer."),
            Pulley(20, 0.005),
            Pulley(20, 0.008),
            Pulley(40, 0.008),
            Pulley(60, 0.008),
            new DrivePartSpec
            {
                Id = "bearing-608zz", Name = "608ZZ rulman (8 × 22 × 7)", Description = "Sabit bilyalı rulman, iki metal kapaklı: kaykay, makara, mil yatağı.",
                Kind = DrivePartKind.Bearing, Bore = 0.008, Diameter = 0.022, Length = 0.007, Bearing = "ball", DatasheetMass = 0.012, Material = "steel"
            },
            new DrivePartSpec
            {
                Id = "bearing-688zz", Name = "688ZZ rulman (8 × 16 × 5)", Description = "İnce kesitli bilyalı rulman.",
                Kind = DrivePartKind.Bearing, Bore = 0.008, Diameter = 0.016, Length = 0.005, Bearing = "ball", DatasheetMass = 0.0035, Material = "steel"
            },
            new DrivePartSpec
            {
                Id = "bearing-625zz", Name = "625ZZ rulman (5 × 16 × 5)", Description = "Küçük bilyalı rulman: 5 mm miller, avara makaraları.",
                Kind = DrivePartKind.Bearing, Bore = 0.005, Diameter = 0.016, Length = 0.005, Bearing = "ball", DatasheetMass = 0.005, Material = "steel"
            },
            new DrivePartSpec
            {
                Id = "bearing-6000zz", Name = "6000ZZ rulman (10 × 26 × 8)", Description = "Sabit bilyalı rulman, 10 mm mil.",
                Kind = DrivePartKind.Bearing, Bore = 0.010, Diameter = 0.026, Length = 0.008, Bearing = "ball", DatasheetMass = 0.019, Material = "steel"
            },
            new DrivePartSpec
            {
                Id = "kp08", Name = "KP08 yataklı rulman (Ø8)", Description = "Döküm gövdede rulman, iki cıvatayla bağlanır; iç bileziği mile setuskurla sıkılır.",
                Kind = DrivePartKind.BearingHousing, Bore = 0.008, Diameter = 0.055, Length = 0.013, Height = 0.029, AxisHeight = 0.015, Bearing = "ball",
                Material = "zinc", Color = "#9DA3AA"
            },
            Rod(0.008, 0.1, "shaft-8-100", "Çelik mil Ø8 × 100 mm", "Sertleştirilmiş, krom kaplı mil (h6): dişli ve kasnak mili ya da doğrusal kızak çubuğu."),
            Rod(0.008, 0.2, "shaft-8-200", "Çelik mil Ø8 × 200 mm", "Sertleştirilmiş, krom kaplı mil (h6)."),
            Rod(0.008, 0.3, "shaft-8-300", "Çelik mil Ø8 × 300 mm", "Sertleştirilmiş, krom kaplı mil (h6): LM8UU ile doğrusal eksen."),
            Rod(0.005, 0.1, "shaft-5-100", "Çelik mil Ø5 × 100 mm", "5 mm mil: küçük dişliler ve 625 rulmanlar."),
            new DrivePartSpec
            {
                Id = "screw-t8x8-300", Name = "T8 vidalı mil 300 mm (hatve 8)", Description = "Paslanmaz trapez vidalı mil, 4 ağızlı, adım 2 mm: turda 8 mm. 3B yazıcı Z ekseni.",
                Kind = DrivePartKind.LeadScrew, Bore = 0.008, Diameter = 0.008, Length = 0.3, Lead = 0.008, Pitch = 0.002, Material = "steel", Color = "#C8CCD0"
            },
            new DrivePartSpec
            {
                Id = "screw-t8x2-300", Name = "T8 vidalı mil 300 mm (hatve 2)", Description = "Tek ağızlı trapez vidalı mil: turda 2 mm, kendini kilitler.",
                Kind = DrivePartKind.LeadScrew, Bore = 0.008, Diameter = 0.008, Length = 0.3, Lead = 0.002, Pitch = 0.002, Material = "steel", Color = "#C8CCD0"
            },
            new DrivePartSpec
            {
                Id = "nut-t8x8", Name = "T8 pirinç somun (hatve 8)", Description = "Flanşlı pirinç somun, T8×8 mile; flanşından taşıyıcıya dört vidayla bağlanır.",
                Kind = DrivePartKind.LeadNut, Bore = 0.008, Diameter = 0.022, Length = 0.015, Lead = 0.008, Pitch = 0.002, Material = "brass", Color = "#C9A54C"
            },
            new DrivePartSpec
            {
                Id = "nut-t8x2", Name = "T8 pirinç somun (hatve 2)", Description = "Flanşlı pirinç somun, T8×2 mile.",
                Kind = DrivePartKind.LeadNut, Bore = 0.008, Diameter = 0.022, Length = 0.015, Lead = 0.002, Pitch = 0.002, Material = "brass", Color = "#C9A54C"
            },
            new DrivePartSpec
            {
                Id = "lm8uu", Name = "LM8UU doğrusal rulman", Description = "Bilyalı doğrusal yatak (8 × 15 × 24): Ø8 çubuk üzerinde kayar, taşıyıcıya bağlanır.",
                Kind = DrivePartKind.LinearBearing, Bore = 0.008, Diameter = 0.015, Length = 0.024, Bearing = "linear-ball", DatasheetMass = 0.016, Material = "steel"
            },
            new DrivePartSpec
            {
                Id = "collar-8", Name = "Mil bileziği Ø8", Description = "Setuskurlu çelik bilezik: mili rulmanda eksenel tutar.",
                Kind = DrivePartKind.Collar, Bore = 0.008, Diameter = 0.016, Length = 0.009, Material = "steel", Color = "#3A3D42"
            },
            new DrivePartSpec
            {
                Id = "coupler-5-8", Name = "Esnek kaplin 5 × 8", Description = "Helisel yarıklı alüminyum kaplin: motorun 5 mm milini 8 mm vidalı mile bağlar, küçük eksen kaçıklığını alır.",
                Kind = DrivePartKind.Coupler, Bore = 0.005, Bore2 = 0.008, Diameter = 0.019, Length = 0.025, Material = "aluminium", Color = "#B9BEC4"
            }
        };

        /// <summary>Adds a part to the world at a pose; ring-shaped parts are hollow, so a shaft goes through their bore.</summary>
        public static WorldObject Add(WorldDocument world, string id, WorldPose pose)
        {
            var spec = Find(id) ?? throw new ArgumentException("Bilinmeyen güç aktarım parçası: " + id, nameof(id));
            var o = new WorldObject
            {
                Id = world.NextId("O"),
                Name = world.UniqueName(ShortName(spec)),
                Kind = WorldObjectKind.Body,
                Shape = Shape,
                PresetId = spec.Id,
                SizeX = spec.Diameter,
                SizeY = spec.Kind == DrivePartKind.BearingHousing ? spec.Length : spec.Diameter,
                SizeZ = spec.Kind == DrivePartKind.BearingHousing ? spec.Height : spec.AxisSpan,
                MassKg = spec.MassKg,
                Material = spec.Material,
                Color = spec.Color,
                Pose = pose
            };
            if (Hollow(spec) is { } wall) o.Parameters["tube.wall"] = wall.ToString("R", CultureInfo.InvariantCulture);
            world.Objects.Add(o);
            return o;
        }

        /// <summary>The wall of a ring-shaped part (m): its collision is a ring the shaft passes through; null for a solid.</summary>
        public static double? Hollow(DrivePartSpec s) =>
            s.IsRod || s.Kind == DrivePartKind.BearingHousing || s.Bore <= 0 ? null : (s.Diameter - Math.Max(s.Bore, s.Bore2)) / 2;

        private static string ShortName(DrivePartSpec s) => s.Kind switch
        {
            DrivePartKind.Gear => $"Dişli z{s.Teeth}",
            DrivePartKind.Pulley => $"Kasnak {s.Teeth}T",
            DrivePartKind.Bearing => s.Name.Split(' ')[0],
            DrivePartKind.BearingHousing => s.Name.Split(' ')[0],
            DrivePartKind.Shaft => $"Mil Ø{s.Bore * 1000:0}",
            DrivePartKind.LeadScrew => "Vidalı mil",
            DrivePartKind.LeadNut => "Somun",
            DrivePartKind.LinearBearing => "LM8UU",
            DrivePartKind.Collar => "Bilezik",
            _ => "Kaplin"
        };

        /// <summary>The physics shape: a cylinder along Z (a ring for bored parts), a housing a box.</summary>
        public static WorldBodyGeometry? Geometry(WorldObject o)
        {
            var s = Of(o);
            if (s == null) return null;
            if (s.Kind == DrivePartKind.BearingHousing)
            {
                // The base plate and the round boss on its web, as their hull: a box would reach a neighbouring shaft that
                // passes over the base's end, as shafts beside a pillow block do.
                double hx = s.Diameter / 2, hy = s.Length / 2, boss = 0.014;
                var hull = new List<Vec3>();
                foreach (double y in new[] { -hy, hy })
                {
                    foreach (double x in new[] { -hx, hx })
                    {
                        hull.Add(new Vec3(x, y, 0));
                        hull.Add(new Vec3(x, y, 0.005));
                    }

                    for (int k = 0; k < 16; k++)
                    {
                        double a = 2 * Math.PI * k / 16;
                        hull.Add(new Vec3(boss * Math.Cos(a), y, s.AxisHeight + boss * Math.Sin(a)));
                    }
                }

                return new WorldBodyGeometry { Min = new Vec3(-hx, -hy, 0), Max = new Vec3(hx, hy, s.Height), MassKg = s.MassKg, Shape = "hull", Hull = hull };
            }
            double r = s.Radius;
            return new WorldBodyGeometry { Min = new Vec3(-r, -r, 0), Max = new Vec3(r, r, s.AxisSpan), MassKg = s.MassKg, Shape = "cylinder-z" };
        }

        /// <summary>The part's axis in the world: a point on it (where the part begins), its direction, and how far it reaches.</summary>
        public static (Vec3 Origin, Vec3 Axis, double Span) AxisOf(WorldObject o, DrivePartSpec s)
        {
            return (o.Pose.ToWorld(s.AxisOrigin), o.Pose.Orientation.Rotate(s.AxisLocal).Normalized(), s.AxisSpan);
        }
    }
}
