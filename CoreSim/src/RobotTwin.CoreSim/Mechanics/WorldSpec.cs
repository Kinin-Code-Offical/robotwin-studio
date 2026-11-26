using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>An obstacle of a user world (metres). By default a static box standing on the floor.</summary>
    public sealed class WorldObstacle
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double SizeX { get; set; } = 0.2;
        public double SizeY { get; set; } = 0.2;
        public double Height { get; set; } = 0.15;

        /// <summary>"box" (default), "ramp" (wedge rising along +x), "cylinder" (diameter SizeX), "sphere" (diameter SizeX).</summary>
        public string Shape { get; set; } = "box";

        /// <summary>Degrees about the vertical axis.</summary>
        public double Yaw { get; set; }

        /// <summary>Height of its bottom above the floor (m).</summary>
        public double Elevation { get; set; }

        /// <summary>Movable: can be pushed, knocked over, rolled.</summary>
        public bool Dynamic { get; set; }

        /// <summary>Mass of a movable obstacle (kg); 0 = material density × volume.</summary>
        public double Mass { get; set; }

        /// <summary>wood, cardboard, steel, rubber, foam … (null = wood).</summary>
        public string? Material { get; set; }

        internal bool IsLegacyBox =>
            (string.IsNullOrEmpty(Shape) || Shape == "box") && Yaw == 0 && Elevation == 0 && !Dynamic && string.IsNullOrEmpty(Material);
    }

    /// <summary>An imported model placed in the world as a static obstacle.</summary>
    public sealed class WorldMeshObstacle
    {
        public string Name { get; set; } = "Model";
        public EmbeddedMesh? Mesh { get; set; }
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Degrees about the vertical axis.</summary>
        public double Yaw { get; set; }
    }

    /// <summary>A rectangular patch of floor of another material (ice, carpet, sand …).</summary>
    public sealed class WorldRegion
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double SizeX { get; set; } = 0.5;
        public double SizeY { get; set; } = 0.5;
        public string Material { get; set; } = "ice";
    }

    /// <summary>Uneven ground of a user world.</summary>
    public sealed class WorldTerrain
    {
        /// <summary>"flat", "slope" (uniform incline), "hills" (smooth bumps) or "heightmap" (<see cref="Heights"/>).</summary>
        public string Kind { get; set; } = "slope";

        /// <summary>Incline of a slope (degrees) and the compass direction it rises towards (degrees from +x).</summary>
        public double SlopeDeg { get; set; } = 5;

        public double DirectionDeg { get; set; }

        /// <summary>Peak height (m) and wavelength (m) of hills.</summary>
        public double Amplitude { get; set; } = 0.05;

        public double Wavelength { get; set; } = 1.0;

        /// <summary>Height map: Columns × Rows samples spaced CellSize, row-major, centred on the origin.</summary>
        public int Columns { get; set; }

        public int Rows { get; set; }
        public double CellSize { get; set; } = 0.05;
        public List<double> Heights { get; set; } = new List<double>();

        public Terrain? Build(double width, double depth)
        {
            switch ((Kind ?? "flat").ToLowerInvariant())
            {
                case "slope":
                {
                    double g = Math.Tan(Math.Max(-60, Math.Min(60, SlopeDeg)) * Math.PI / 180);
                    double d = DirectionDeg * Math.PI / 180;
                    return new PlaneTerrain(0, g * Math.Cos(d), g * Math.Sin(d));
                }

                case "hills":
                {
                    double k = 2 * Math.PI / Math.Max(0.05, Wavelength), a = Amplitude;
                    double cell = Math.Max(0.01, Math.Min(0.05, Wavelength / 12));
                    return HeightMapTerrain.FromFunction(width + 0.2, depth + 0.2, cell,
                        (x, y) => a * 0.5 * (1 - Math.Cos(k * x)) * 0.5 * (1 - Math.Cos(k * y * 0.83 + 0.7)));
                }

                case "heightmap":
                    if (Columns >= 2 && Rows >= 2 && Heights.Count >= Columns * Rows)
                        return new HeightMapTerrain(Columns, Rows, CellSize, -(Columns - 1) * CellSize / 2, -(Rows - 1) * CellSize / 2, Heights);
                    return null;
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// A user-made test world saved with the project: floor size, grip and material,
    /// gravity (Earth, Moon, Mars …), air and wind, ambient temperature for the thermal
    /// models, terrain, a line track and obstacles (static or movable).
    /// </summary>
    public sealed class WorldSpec
    {
        public string Name { get; set; } = "Özel dünya";
        public double Width { get; set; } = 3.0;
        public double Depth { get; set; } = 2.0;
        public double Friction { get; set; } = 0.9;
        public bool Walls { get; set; } = true;
        public double WallHeight { get; set; } = 0.08;

        /// <summary>Floor material (wood, concrete, asphalt, carpet, ice …); null = the single <see cref="Friction"/> value.</summary>
        public string? FloorMaterial { get; set; }

        public List<WorldRegion> Regions { get; set; } = new List<WorldRegion>();
        public WorldTerrain? Terrain { get; set; }

        /// <summary>Gravitational acceleration (m/s²): 9.81 Earth, 1.62 Moon, 3.71 Mars.</summary>
        public double Gravity { get; set; } = 9.81;

        /// <summary>
        /// Air density (kg/m³); null = sea-level air at <see cref="AmbientC"/> (1.184 at 25 °C, 1.225 at 15 °C, 1.293 at
        /// 0 °C). Mars ≈ 0.02, Moon 0. Scales drag and propeller thrust.
        /// </summary>
        public double? AirDensity { get; set; }

        /// <summary>Mean wind (m/s) and gust amplitude (m/s).</summary>
        public double WindX { get; set; }

        public double WindY { get; set; }
        public double WindZ { get; set; }
        public double Gust { get; set; }

        /// <summary>Air temperature; parts heat up from here.</summary>
        public double AmbientC { get; set; } = 25.0;

        /// <summary>"none", "oval", "eight", or "custom" (uses <see cref="Lines"/>).</summary>
        public string Track { get; set; } = "none";

        public double TrackScale { get; set; } = 1.0;
        public double LineWidth { get; set; } = 0.019;

        /// <summary>Custom tape polylines as [x, y] pairs.</summary>
        public List<List<double[]>> Lines { get; set; } = new List<List<double[]>>();

        public List<WorldObstacle> Obstacles { get; set; } = new List<WorldObstacle>();

        /// <summary>Obstacles of any shape from imported 3D models (rest on the floor).</summary>
        public List<WorldMeshObstacle> MeshObstacles { get; set; } = new List<WorldMeshObstacle>();
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double StartYaw { get; set; }

        /// <summary>[sense] Light, floor optics, taped tracks, GPS origin and magnetic field for the world's sensors (null = defaults).</summary>
        public RobotTwin.CoreSim.Sandbox.Sensing.SensingEnvironment? Sensing { get; set; }   // [sense]

        public Arena ToArena()
        {
            var a = new Arena
            {
                Name = string.IsNullOrWhiteSpace(Name) ? "Özel dünya" : Name,
                Width = Math.Max(0.5, Width),
                Depth = Math.Max(0.5, Depth),
                Friction = Math.Max(0.01, Friction),
                Walls = Walls,
                WallHeight = Math.Max(0.01, WallHeight),
                LineWidth = Math.Max(0.003, LineWidth),
                StartX = StartX,
                StartY = StartY,
                StartYaw = StartYaw * Math.PI / 180,
                FloorMaterial = string.IsNullOrWhiteSpace(FloorMaterial) ? null : Materials.Canonical(FloorMaterial),
                AirDensity = Math.Max(0, AirDensity ?? Sdk.Atmosphere.Density(AmbientC, Sdk.Atmosphere.SeaLevelPressure)),   // [air] as the world runtime's air
                Wind = new Vec3(WindX, WindY, WindZ),
                Gust = Math.Max(0, Gust)
            };
            a.Terrain = Terrain?.Build(a.Width, a.Depth);
            foreach (var r in Regions) a.Regions.Add(new SurfaceRegion(r.X, r.Y, Math.Max(0.01, r.SizeX), Math.Max(0.01, r.SizeY), r.Material));
            if (Track == "oval" || Track == "eight")
            {
                var preset = Arena.Create(Track);
                foreach (var line in preset.Lines) a.Lines.Add(line.Select(p => (p.X * TrackScale, p.Y * TrackScale)).ToList());
                if (StartX == 0 && StartY == 0)
                {
                    a.StartX = preset.StartX * TrackScale;
                    a.StartY = preset.StartY * TrackScale;
                    a.StartYaw = preset.StartYaw;
                }
            }
            else if (Track == "custom")
            {
                foreach (var line in Lines.Where(l => l.Count >= 2)) a.Lines.Add(line.Select(p => (p[0], p[1])).ToList());
            }

            foreach (var o in Obstacles)
            {
                if (o.IsLegacyBox)
                {
                    a.Obstacles.Add(new Obstacle(o.X, o.Y, Math.Max(0.01, o.SizeX), Math.Max(0.01, o.SizeY), Math.Max(0.005, o.Height)));
                    continue;
                }

                var kind = (o.Shape ?? "box").ToLowerInvariant() switch
                {
                    "ramp" or "rampa" => ArenaShapeKind.Ramp,
                    "cylinder" or "silindir" => ArenaShapeKind.Cylinder,
                    "sphere" or "ball" or "top" or "küre" => ArenaShapeKind.Sphere,
                    _ => ArenaShapeKind.Box
                };
                a.Shapes.Add(new ArenaShape
                {
                    Kind = kind, X = o.X, Y = o.Y, Elevation = o.Elevation, Yaw = o.Yaw * Math.PI / 180, SizeX = Math.Max(0.005, o.SizeX),
                    SizeY = Math.Max(0.005, o.SizeY), SizeZ = Math.Max(0.002, o.Height), Dynamic = o.Dynamic, Mass = Math.Max(0, o.Mass),
                    Material = string.IsNullOrWhiteSpace(o.Material) ? "wood" : o.Material!
                });
            }

            foreach (var m in MeshObstacles)
            {
                if (m.Mesh == null) continue;
                a.MeshObstacles.Add(MeshObstacle.Place(MeshModel.FromEmbedded(m.Mesh), m.X, m.Y, m.Yaw * Math.PI / 180, true, m.Name));
            }
            return a;
        }

        /// <summary>Starts a custom world from one of the built-in arenas.</summary>
        public static WorldSpec FromPreset(string preset)
        {
            var a = Arena.Create(preset);
            var w = new WorldSpec
            {
                Width = a.Width,
                Depth = a.Depth,
                Friction = a.Friction,
                Walls = a.Walls,
                FloorMaterial = a.FloorMaterial,
                Track = preset == "oval" || preset == "eight" ? preset : "none",
                StartX = 0,
                StartY = 0,
                // The arena's floor as optical sensors see it: white (0.85) with the black tape (0.06), as in the arena itself.
                Sensing = new RobotTwin.CoreSim.Sandbox.Sensing.SensingEnvironment { FloorReflectance = 0.85, FloorGloss = 0.02 }
            };
            foreach (var o in a.Obstacles) w.Obstacles.Add(new WorldObstacle { X = o.X, Y = o.Y, SizeX = o.SizeX, SizeY = o.SizeY, Height = o.Height });
            foreach (var s in a.Shapes)
            {
                w.Obstacles.Add(new WorldObstacle
                {
                    X = s.X, Y = s.Y, SizeX = s.SizeX, SizeY = s.SizeY, Height = s.SizeZ, Elevation = s.Elevation, Yaw = s.Yaw * 180 / Math.PI,
                    Dynamic = s.Dynamic, Mass = s.Mass, Material = s.Material,
                    Shape = s.Kind switch { ArenaShapeKind.Ramp => "ramp", ArenaShapeKind.Cylinder => "cylinder", ArenaShapeKind.Sphere => "sphere", _ => "box" }
                });
            }

            foreach (var r in a.Regions) w.Regions.Add(new WorldRegion { X = r.X, Y = r.Y, SizeX = r.SizeX, SizeY = r.SizeY, Material = r.Material });
            if (preset == "obstacles" || preset == "ramps")
            {
                w.StartX = a.StartX;
                w.StartY = a.StartY;
            }

            return w;
        }
    }

    public static class WorldExtensions
    {
        /// <summary>The arena an assembly runs in: its own world when set, otherwise the named preset.</summary>
        public static Arena CreateArena(this AssemblyDocument assembly) =>
            assembly.Arena == "custom" && assembly.World != null ? assembly.World.ToArena() : Arena.Create(assembly.Arena);
    }
}
