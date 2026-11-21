using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>How a <see cref="MassProperties"/> value was obtained.</summary>
    public enum MassPropertiesMethod
    {
        /// <summary>Exact polyhedral integration of a closed mesh.</summary>
        ExactSolid,

        /// <summary>Open mesh: solid convex hull of its vertices (estimate).</summary>
        ConvexHullSolid,

        /// <summary>Thin shell over the surface (estimate, or deliberate for sheet parts).</summary>
        Shell,

        /// <summary>Solid bounding box (coarse estimate).</summary>
        BoundingBox,

        /// <summary>Open mesh integrated as if it were closed (caller's choice).</summary>
        OpenIntegrated,

        /// <summary>Closed-form primitive (point, box, cylinder, sphere) or explicit tensor.</summary>
        Analytic,

        /// <summary>Result of <see cref="MassProperties.Combine(IEnumerable{MassPart})"/>.</summary>
        Combined
    }

    /// <summary>What to do when a mesh is not watertight.</summary>
    public enum OpenMeshFallback
    {
        /// <summary>Convex hull solid if the hull has volume, otherwise a thin shell.</summary>
        Auto,
        ConvexHull,
        Shell,
        BoundingBox,

        /// <summary>Integrate the open mesh anyway (only sensible for tiny holes).</summary>
        IntegrateAnyway
    }

    public sealed class MassPropertiesOptions
    {
        public OpenMeshFallback OpenMeshFallback { get; set; } = OpenMeshFallback.Auto;

        /// <summary>Wall thickness used to turn a density into a shell mass (m).</summary>
        public double ShellThickness { get; set; } = 0.002;
    }

    /// <summary>A body placed in an assembly: its own frame rotated by <see cref="Rotation"/>, then moved to <see cref="Position"/>.</summary>
    public readonly struct MassPart
    {
        public MassPart(MassProperties properties, Vec3 position, Quat rotation)
        {
            Properties = properties;
            Position = position;
            Rotation = rotation;
        }

        public MassPart(MassProperties properties, Vec3 position) : this(properties, position, Quat.Identity)
        {
        }

        public MassProperties Properties { get; }
        public Vec3 Position { get; }
        public Quat Rotation { get; }
    }

    /// <summary>
    /// Mass, centre of mass and full inertia tensor (about the centre of mass, in the
    /// model's axes) plus its principal decomposition.
    /// </summary>
    public sealed class MassProperties
    {
        private MassProperties()
        {
        }

        public double Mass { get; private set; }

        /// <summary>Solid volume (m³); for a shell the enclosed volume is not known and this is 0.</summary>
        public double Volume { get; private set; }

        public double SurfaceArea { get; private set; }

        /// <summary>Centre of mass in the model frame (m).</summary>
        public Vec3 CenterOfMass { get; private set; }

        /// <summary>Inertia tensor about the centre of mass, model axes (kg·m²). Off-diagonals are −∫xy dm etc.</summary>
        public Mat3 Inertia { get; private set; }

        /// <summary>Principal moments (kg·m²) about <see cref="PrincipalAxes"/>' x, y, z.</summary>
        public Vec3 PrincipalMoments { get; private set; }

        /// <summary>Rotation from the principal frame to the model frame (identity for an axis-aligned symmetric part).</summary>
        public Quat PrincipalAxes { get; private set; } = Quat.Identity;

        public MassPropertiesMethod Method { get; private set; }

        /// <summary>The source mesh was watertight and consistently oriented.</summary>
        public bool IsClosed { get; private set; }

        /// <summary>True when the numbers are an estimate (open mesh fallback).</summary>
        public bool IsEstimate =>
            Method == MassPropertiesMethod.ConvexHullSolid || Method == MassPropertiesMethod.BoundingBox ||
            Method == MassPropertiesMethod.OpenIntegrated || (Method == MassPropertiesMethod.Shell && !_shellRequested) || _estimateParts;

        /// <summary>The mesh was wound inside-out (signed volume negative); results were corrected.</summary>
        public bool WindingInverted { get; private set; }

        public IReadOnlyList<string> Warnings => _warnings;

        public double Density => Volume > 0 ? Mass / Volume : 0;

        /// <summary>Radii of gyration along the principal axes (m).</summary>
        public Vec3 RadiiOfGyration => Mass > 0
            ? new Vec3(Math.Sqrt(Math.Max(0, PrincipalMoments.X) / Mass), Math.Sqrt(Math.Max(0, PrincipalMoments.Y) / Mass),
                Math.Sqrt(Math.Max(0, PrincipalMoments.Z) / Mass))
            : Vec3.Zero;

        private readonly List<string> _warnings = new List<string>();
        private bool _shellRequested, _estimateParts;

        // ================================================================== from meshes

        /// <summary>Mass properties of a solid of uniform density (kg/m³).</summary>
        public static MassProperties FromMesh(MeshModel mesh, double densityKgPerM3, MassPropertiesOptions? options = null) =>
            Compute(mesh, densityKgPerM3, null, options ?? new MassPropertiesOptions());

        /// <summary>Mass properties of a uniform solid scaled to a known total mass (e.g. a weighed 3D print).</summary>
        public static MassProperties FromMeshWithMass(MeshModel mesh, double massKg, MassPropertiesOptions? options = null) =>
            Compute(mesh, null, massKg, options ?? new MassPropertiesOptions());

        /// <summary>Thin shell of uniform areal density (kg/m²), e.g. sheet metal or a surface-only scan.</summary>
        public static MassProperties ShellFromMesh(MeshModel mesh, double kgPerM2)
        {
            var mp = FromShell(mesh, Integrals.Shell(mesh, RefPoint(mesh)), kgPerM2, null);
            mp._shellRequested = true;
            mp.IsClosed = mesh.AnalyzeTopology().IsClosed;
            return mp;
        }

        private static MassProperties Compute(MeshModel mesh, double? density, double? mass, MassPropertiesOptions o)
        {
            if (mesh.TriangleCount == 0 || mesh.Vertices.Count == 0) throw new ArgumentException("Mesh has no triangles.", nameof(mesh));
            var topo = mesh.AnalyzeTopology();
            var origin = RefPoint(mesh);
            var notes = new List<string>();
            bool inverted = false;

            if (topo.IsClosed)
            {
                var s = Integrals.Solid(mesh.Vertices, mesh.Indices, origin);
                if (s.V < 0)
                {
                    s = s.Negated();
                    inverted = true;
                    notes.Add("ağ ters yüzlü (içi dışına dönük); hacmin işareti düzeltildi");
                }

                double ext = mesh.Bounds.Size.Length;
                if (s.V > 1e-12 * ext * ext * ext)
                {
                    var r = FromSolid(s, density, mass, MassPropertiesMethod.ExactSolid);
                    r.IsClosed = true;
                    r.WindingInverted = inverted;
                    r.SurfaceArea = mesh.SurfaceArea();
                    if (topo.DegenerateTriangles > 0) notes.Add($"{topo.DegenerateTriangles} bozuk (alansız) üçgen yok sayıldı");
                    r._warnings.AddRange(notes);
                    return r;
                }

                notes.Add("kapalı ağın içinde hacim yok");
            }
            else
            {
                notes.Add($"ağ kapalı değil ({topo}); yaklaşık değer kullanıldı");
            }

            var mode = o.OpenMeshFallback;
            MassProperties result;
            if (mode == OpenMeshFallback.IntegrateAnyway)
            {
                var s = Integrals.Solid(mesh.Vertices, mesh.Indices, origin);
                if (s.V < 0) s = s.Negated();
                result = s.V > 0 ? FromSolid(s, density, mass, MassPropertiesMethod.OpenIntegrated) : BoxFallback(mesh, density, mass, notes);
            }
            else if (mode == OpenMeshFallback.Shell)
            {
                result = ShellFallback(mesh, origin, density, mass, o);
            }
            else if (mode == OpenMeshFallback.BoundingBox)
            {
                result = BoxFallback(mesh, density, mass, notes);
            }
            else
            {
                var hull = ConvexHull.FromMesh(mesh);
                Integrals hs = default;
                bool ok = !hull.IsDegenerate;
                if (ok)
                {
                    hs = Integrals.Solid(hull.Vertices, hull.Indices, origin);
                    double ext = mesh.Bounds.Size.Length;
                    ok = hs.V > 1e-12 * ext * ext * ext;
                }

                if (ok) result = FromSolid(hs, density, mass, MassPropertiesMethod.ConvexHullSolid);
                else if (mode == OpenMeshFallback.Auto) result = ShellFallback(mesh, origin, density, mass, o);
                else result = BoxFallback(mesh, density, mass, notes);
            }

            result.SurfaceArea = mesh.SurfaceArea();
            result._warnings.InsertRange(0, notes);
            result._warnings.Add($"kütle özellikleri yaklaşık hesaplandı ({result.Method})");
            return result;
        }

        private static MassProperties ShellFallback(MeshModel mesh, Vec3 origin, double? density, double? mass, MassPropertiesOptions o)
        {
            var s = Integrals.Shell(mesh, origin);
            return FromShell(mesh, s, density.HasValue ? density.Value * o.ShellThickness : null, mass);
        }

        private static MassProperties BoxFallback(MeshModel mesh, double? density, double? mass, List<string> notes)
        {
            var b = mesh.Bounds;
            var size = b.Size;
            double vol = size.X * size.Y * size.Z;
            if (vol <= 0) notes.Add("sınırlayan kutu düz; yoğunluktan bulunan kütle sıfır");
            double m = mass ?? (density ?? 0) * vol;
            var r = Box(m, size, b.Center);
            r.Method = MassPropertiesMethod.BoundingBox;
            r.Volume = vol;
            return r;
        }

        private static MassProperties FromSolid(Integrals s, double? density, double? mass, MassPropertiesMethod method)
        {
            double rho = mass.HasValue ? mass.Value / s.V : density ?? 0;
            var r = Build(s, rho, method);
            r.Volume = s.V;
            return r;
        }

        private static MassProperties FromShell(MeshModel mesh, Integrals s, double? arealDensity, double? mass)
        {
            if (s.V <= 0) throw new ArgumentException("Mesh has no area.", nameof(mesh));
            double rho = mass.HasValue ? mass.Value / s.V : arealDensity ?? 0;
            var r = Build(s, rho, MassPropertiesMethod.Shell);
            r.Volume = 0;
            r.SurfaceArea = s.V;
            return r;
        }

        private static MassProperties Build(Integrals s, double rho, MassPropertiesMethod method)
        {
            double v = s.V;
            var c = new Vec3(s.X / v, s.Y / v, s.Z / v);
            // Second moments about the centre of mass (unit density), then scaled.
            double xx = s.XX - v * c.X * c.X, yy = s.YY - v * c.Y * c.Y, zz = s.ZZ - v * c.Z * c.Z;
            double xy = s.XY - v * c.X * c.Y, yz = s.YZ - v * c.Y * c.Z, zx = s.ZX - v * c.Z * c.X;
            var tensor = Mat3.Symmetric(yy + zz, xx + zz, xx + yy, -xy, -zx, -yz) * rho;
            return FromTensor(rho * v, s.Origin + c, tensor, method);
        }

        private static Vec3 RefPoint(MeshModel mesh) => mesh.Bounds.Center;

        // ================================================================== primitives

        /// <summary>Mass properties from an explicit tensor about the centre of mass.</summary>
        public static MassProperties FromTensor(double mass, Vec3 centerOfMass, Mat3 inertiaAboutCom) =>
            FromTensor(mass, centerOfMass, inertiaAboutCom, MassPropertiesMethod.Analytic);

        private static MassProperties FromTensor(double mass, Vec3 com, Mat3 tensor, MassPropertiesMethod method)
        {
            // Enforce exact symmetry.
            tensor = Mat3.Symmetric(tensor.M00, tensor.M11, tensor.M22, 0.5 * (tensor.M01 + tensor.M10), 0.5 * (tensor.M02 + tensor.M20),
                0.5 * (tensor.M12 + tensor.M21));
            var (values, vectors) = tensor.SymmetricEigen();
            return new MassProperties
            {
                Mass = mass,
                CenterOfMass = com,
                Inertia = tensor,
                PrincipalMoments = values,
                PrincipalAxes = vectors.ToQuat(),
                Method = method,
                IsClosed = method == MassPropertiesMethod.Analytic
            };
        }

        public static MassProperties Point(double mass, Vec3 position = default) =>
            FromTensor(mass, position, Mat3.Zero, MassPropertiesMethod.Analytic);

        /// <summary>Solid box, axis aligned.</summary>
        public static MassProperties Box(double mass, Vec3 size, Vec3 center = default)
        {
            double k = mass / 12;
            var r = FromTensor(mass, center,
                Mat3.Diagonal(new Vec3(k * (size.Y * size.Y + size.Z * size.Z), k * (size.X * size.X + size.Z * size.Z), k * (size.X * size.X + size.Y * size.Y))),
                MassPropertiesMethod.Analytic);
            r.Volume = size.X * size.Y * size.Z;
            r.SurfaceArea = 2 * (size.X * size.Y + size.Y * size.Z + size.Z * size.X);
            return r;
        }

        /// <summary>Solid cylinder along Z.</summary>
        public static MassProperties Cylinder(double mass, double radius, double height, Vec3 center = default)
        {
            double ixy = mass * (3 * radius * radius + height * height) / 12, iz = mass * radius * radius / 2;
            var r = FromTensor(mass, center, Mat3.Diagonal(new Vec3(ixy, ixy, iz)), MassPropertiesMethod.Analytic);
            r.Volume = Math.PI * radius * radius * height;
            r.SurfaceArea = 2 * Math.PI * radius * (radius + height);
            return r;
        }

        public static MassProperties Sphere(double mass, double radius, Vec3 center = default)
        {
            double i = 0.4 * mass * radius * radius;
            var r = FromTensor(mass, center, Mat3.Diagonal(new Vec3(i, i, i)), MassPropertiesMethod.Analytic);
            r.Volume = 4.0 / 3 * Math.PI * radius * radius * radius;
            r.SurfaceArea = 4 * Math.PI * radius * radius;
            return r;
        }

        // ================================================================== algebra

        /// <summary>Inertia tensor about an arbitrary point (parallel axis theorem), model axes.</summary>
        public Mat3 InertiaAbout(Vec3 point) => Inertia + ParallelAxis(Mass, CenterOfMass - point);

        private static Mat3 ParallelAxis(double m, Vec3 d) => (Mat3.Identity * d.Dot(d) - Mat3.Outer(d, d)) * m;

        /// <summary>The same body expressed in a parent frame: p_parent = rotation·p + translation.</summary>
        public MassProperties Transformed(Quat rotation, Vec3 translation)
        {
            var r = FromTensor(Mass, rotation.Rotate(CenterOfMass) + translation, Inertia.Rotated(rotation), Method);
            r.CopyMeta(this);
            return r;
        }

        /// <summary>The same shape and mass distribution scaled to another total mass.</summary>
        public MassProperties WithMass(double massKg)
        {
            double k = Mass > 0 ? massKg / Mass : 0;
            var r = FromTensor(massKg, CenterOfMass, Inertia * k, Method);
            r.CopyMeta(this);
            return r;
        }

        private void CopyMeta(MassProperties src)
        {
            Volume = src.Volume;
            SurfaceArea = src.SurfaceArea;
            IsClosed = src.IsClosed;
            WindingInverted = src.WindingInverted;
            _shellRequested = src._shellRequested;
            _estimateParts = src._estimateParts;
            _warnings.AddRange(src._warnings);
        }

        public static MassProperties Combine(params MassPart[] parts) => Combine((IEnumerable<MassPart>)parts);

        /// <summary>
        /// Total mass, centre of mass and inertia tensor of rigidly joined bodies (parallel axis
        /// theorem). Each part's frame is rotated by its Rotation and moved to its Position.
        /// </summary>
        public static MassProperties Combine(IEnumerable<MassPart> parts)
        {
            var list = parts.ToList();
            double m = 0, vol = 0, area = 0;
            var weighted = Vec3.Zero;
            var placed = new List<(double M, Vec3 C, Mat3 I)>(list.Count);
            foreach (var p in list)
            {
                var mp = p.Properties;
                var c = p.Rotation.Rotate(mp.CenterOfMass) + p.Position;
                placed.Add((mp.Mass, c, mp.Inertia.Rotated(p.Rotation)));
                m += mp.Mass;
                weighted += c * mp.Mass;
                vol += mp.Volume;
                area += mp.SurfaceArea;
            }

            var com = m > 0 ? weighted / m : Vec3.Zero;
            var tensor = Mat3.Zero;
            foreach (var (pm, c, i) in placed) tensor += i + ParallelAxis(pm, c - com);
            var r = FromTensor(m, com, tensor, MassPropertiesMethod.Combined);
            r.Volume = vol;
            r.SurfaceArea = area;
            r.IsClosed = list.All(p => p.Properties.IsClosed);
            r._estimateParts = list.Any(p => p.Properties.IsEstimate);
            foreach (var p in list) r._warnings.AddRange(p.Properties._warnings);
            return r;
        }

        public override string ToString() =>
            $"m={Mass:G5} kg, COM={CenterOfMass}, I_principal=({PrincipalMoments.X:G4}, {PrincipalMoments.Y:G4}, {PrincipalMoments.Z:G4}) kg·m², {Method}";

        // ================================================================== integration

        /// <summary>∫1, ∫x, ∫y, ∫z, ∫x², ∫y², ∫z², ∫xy, ∫yz, ∫zx relative to <see cref="Origin"/>.</summary>
        private struct Integrals
        {
            public Vec3 Origin;
            public double V, X, Y, Z, XX, YY, ZZ, XY, YZ, ZX;

            public Integrals Negated() => new Integrals
            {
                Origin = Origin, V = -V, X = -X, Y = -Y, Z = -Z, XX = -XX, YY = -YY, ZZ = -ZZ, XY = -XY, YZ = -YZ, ZX = -ZX
            };

            /// <summary>
            /// Volume integrals of a closed, outward-wound triangle mesh via the divergence
            /// theorem (D. Eberly, "Polyhedral Mass Properties (Revisited)"). Exact for polyhedra.
            /// </summary>
            public static Integrals Solid(IReadOnlyList<Vec3> verts, IReadOnlyList<int> idx, Vec3 origin)
            {
                double i0 = 0, i1 = 0, i2 = 0, i3 = 0, i4 = 0, i5 = 0, i6 = 0, i7 = 0, i8 = 0, i9 = 0;
                int triangles = idx.Count / 3;
                for (int t = 0; t < triangles; t++)
                {
                    var p0 = verts[idx[3 * t]] - origin;
                    var p1 = verts[idx[3 * t + 1]] - origin;
                    var p2 = verts[idx[3 * t + 2]] - origin;
                    double x0 = p0.X, y0 = p0.Y, z0 = p0.Z, x1 = p1.X, y1 = p1.Y, z1 = p1.Z, x2 = p2.X, y2 = p2.Y, z2 = p2.Z;
                    double a1 = x1 - x0, b1 = y1 - y0, c1 = z1 - z0, a2 = x2 - x0, b2 = y2 - y0, c2 = z2 - z0;
                    double d0 = b1 * c2 - b2 * c1, d1 = a2 * c1 - a1 * c2, d2 = a1 * b2 - a2 * b1;

                    Sub(x0, x1, x2, out double f1x, out double f2x, out double f3x, out double g0x, out double g1x, out double g2x);
                    Sub(y0, y1, y2, out double f1y, out double f2y, out double f3y, out double g0y, out double g1y, out double g2y);
                    Sub(z0, z1, z2, out double f1z, out double f2z, out double f3z, out double g0z, out double g1z, out double g2z);

                    i0 += d0 * f1x;
                    i1 += d0 * f2x;
                    i2 += d1 * f2y;
                    i3 += d2 * f2z;
                    i4 += d0 * f3x;
                    i5 += d1 * f3y;
                    i6 += d2 * f3z;
                    i7 += d0 * (y0 * g0x + y1 * g1x + y2 * g2x);
                    i8 += d1 * (z0 * g0y + z1 * g1y + z2 * g2y);
                    i9 += d2 * (x0 * g0z + x1 * g1z + x2 * g2z);
                }

                return new Integrals
                {
                    Origin = origin,
                    V = i0 / 6,
                    X = i1 / 24,
                    Y = i2 / 24,
                    Z = i3 / 24,
                    XX = i4 / 60,
                    YY = i5 / 60,
                    ZZ = i6 / 60,
                    XY = i7 / 120,
                    YZ = i8 / 120,
                    ZX = i9 / 120
                };
            }

            private static void Sub(double w0, double w1, double w2, out double f1, out double f2, out double f3, out double g0, out double g1,
                out double g2)
            {
                double temp0 = w0 + w1;
                f1 = temp0 + w2;
                double temp1 = w0 * w0;
                double temp2 = temp1 + w1 * temp0;
                f2 = temp2 + w2 * f1;
                f3 = w0 * temp1 + w1 * temp2 + w2 * f2;
                g0 = f2 + w0 * (f1 + w0);
                g1 = f2 + w1 * (f1 + w1);
                g2 = f2 + w2 * (f1 + w2);
            }

            /// <summary>Area integrals over the surface (thin shell): ∫r dA and ∫r rᵀ dA = A/12·(Σ vᵢvᵢᵀ + s sᵀ).</summary>
            public static Integrals Shell(MeshModel mesh, Vec3 origin)
            {
                var r = new Integrals { Origin = origin };
                for (int t = 0; t < mesh.TriangleCount; t++)
                {
                    var (a0, b0, c0) = mesh.Triangle(t);
                    Vec3 a = a0 - origin, b = b0 - origin, c = c0 - origin;
                    double area = 0.5 * (b - a).Cross(c - a).Length;
                    if (area <= 0) continue;
                    var s = a + b + c;
                    double k = area / 12;
                    r.V += area;
                    r.X += area * s.X / 3;
                    r.Y += area * s.Y / 3;
                    r.Z += area * s.Z / 3;
                    r.XX += k * (a.X * a.X + b.X * b.X + c.X * c.X + s.X * s.X);
                    r.YY += k * (a.Y * a.Y + b.Y * b.Y + c.Y * c.Y + s.Y * s.Y);
                    r.ZZ += k * (a.Z * a.Z + b.Z * b.Z + c.Z * c.Z + s.Z * s.Z);
                    r.XY += k * (a.X * a.Y + b.X * b.Y + c.X * c.Y + s.X * s.Y);
                    r.YZ += k * (a.Y * a.Z + b.Y * b.Z + c.Y * c.Z + s.Y * s.Z);
                    r.ZX += k * (a.Z * a.X + b.Z * b.X + c.Z * c.X + s.Z * s.X);
                }

                return r;
            }
        }
    }

    /// <summary>Densities of common robot materials (kg/m³).</summary>
    public static class MaterialDensity
    {
        public const double Pla = 1240;
        public const double Petg = 1270;
        public const double Abs = 1040;
        public const double Nylon = 1140;
        public const double Acrylic = 1180;
        public const double Plywood = 600;
        public const double Mdf = 750;
        public const double Fr4 = 1850;
        public const double Aluminium = 2700;
        public const double Steel = 7850;
        public const double Brass = 8500;
        public const double CarbonFibre = 1600;
        public const double Wood = 500;        // pine / spruce
        public const double Balsa = 160;       // model-aircraft grade (100–200)
        public const double Lead = 11340;
        public const double Rubber = 1100;
        public const double Copper = 8960;

        /// <summary>Rough effective density of an FDM print: solid walls/skins plus infill (fractions 0..1).</summary>
        public static double Printed(double material, double infill, double shellFraction = 0.35) =>
            material * (shellFraction + (1 - shellFraction) * Math.Max(0, Math.Min(1, infill)));
    }

    public static class RigidBodyMassExtensions
    {
        /// <summary>
        /// Sets mass and inertia from mass properties. The body origin must be the centre of mass,
        /// so the body frame is the model frame shifted by <see cref="MassProperties.CenterOfMass"/>
        /// (convert model points with <c>p − CenterOfMass</c>); its axes stay the model axes and
        /// the principal axes are stored in <see cref="RigidBody.PrincipalAxes"/>.
        /// </summary>
        public static void SetMassProperties(this RigidBody body, MassProperties mp)
        {
            body.Mass = Math.Max(1e-9, mp.Mass);
            var m = mp.PrincipalMoments;
            double floor = Math.Max(1e-12, 1e-6 * Math.Max(m.X, Math.Max(m.Y, m.Z)));
            body.Inertia = new Vec3(Math.Max(floor, m.X), Math.Max(floor, m.Y), Math.Max(floor, m.Z));
            body.PrincipalAxes = mp.PrincipalAxes;
        }

        /// <summary>Full inertia tensor of the body about its origin, body axes.</summary>
        public static Mat3 InertiaTensor(this RigidBody body) => Mat3.Diagonal(body.Inertia).Rotated(body.PrincipalAxes);
    }
}
