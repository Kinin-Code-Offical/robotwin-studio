using System;

namespace RobotTwin.CoreSim.Mechanics
{
    // [terrain] The physics view of a TerrainMap: ground height/normal, per-point surface contact, soft soil.

    /// <summary>
    /// Ground of a <see cref="PhysicsScene"/> made from an editable <see cref="TerrainMap"/>. The map is
    /// shared, not copied: sculpting while a simulation runs changes the ground at once. Contacts
    /// use the painted layers at the contact point (blended where layers meet), against the body's
    /// material class (rubber / plastic / metal); ray-cast wheels also sink into loose layers.
    /// </summary>
    public sealed class TerrainSurface : Terrain
    {
        [ThreadStatic] private static double[]? _weights;

        public TerrainSurface(TerrainMap map)
        {
            Map = map;
        }

        public TerrainMap Map { get; }

        public override double Height(double x, double y) => Map.Height(x, y);
        public override (double Dx, double Dy) Gradient(double x, double y) => Map.Gradient(x, y);
        public override Vec3 Normal(double x, double y) => Map.Normal(x, y);
        public override (double Min, double Max) HeightRange => Map.HeightRange;

        public override double Raycast(Vec3 origin, Vec3 dir, double maxDistance) =>
            Map.Raycast(origin, dir, maxDistance, out double t) ? t : -1;

        private static double[] Buffer => _weights ??= new double[TerrainLayers.Count];

        /// <summary>Id of the dominant layer at a point ("asphalt", "mud" …).</summary>
        public string MaterialAt(double x, double y) => Map.LayerAt(x, y).Id;

        /// <summary>Contact properties of a body material on the painted surface at (x, y).</summary>
        public ContactProperties Contact(int bodyMaterial, double x, double y, bool caster = false)
        {
            var w = Buffer;
            Map.SampleLayers(x, y, w);
            var cls = TerrainLayers.ClassOf(bodyMaterial);
            double us = 0, uk = 0, crr = 0, e = 0, k = 0, steelCrr = 0;
            foreach (int l in Map.ActiveLayers)
            {
                double f = w[l];
                if (f <= 1e-4) continue;
                var layer = TerrainLayers.Get(l);
                var pair = layer.For(cls);
                us += f * pair.Static;
                uk += f * pair.Kinetic;
                crr += f * pair.Rolling;
                e += f * layer.Restitution;
                k += f * layer.Stiffness;
                steelCrr += f * layer.Steel.Rolling;
            }

            if (caster)
            {
                // A ball caster rolls in every direction; its resistance grows on soft floors.
                double mu = 0.03 + 2 * steelCrr;
                return new ContactProperties(mu, mu, 0.05, 0, Math.Max(1, k));
            }

            return new ContactProperties(us, uk, e, crr, Math.Max(1, k));
        }

        /// <summary>True when any painted layer is deformable soil.</summary>
        public bool HasLooseGround
        {
            get
            {
                foreach (int l in Map.ActiveLayers)
                    if (TerrainLayers.Get(l).IsLoose)
                        return true;
                return false;
            }
        }

        /// <summary>Share (0…1) of loose layers at a point; fills the layer weights buffer.</summary>
        public double LooseFraction(double x, double y, out double[] weights)
        {
            weights = Buffer;
            Map.SampleLayers(x, y, weights);
            double loose = 0;
            foreach (int l in Map.ActiveLayers)
                if (weights[l] > 1e-4 && TerrainLayers.Get(l).IsLoose)
                    loose += weights[l];
            return loose;
        }

        /// <summary>
        /// Terramechanics under a ray-cast tyre on loose layers (see <see cref="SoilModel"/>):
        /// <list type="number">
        /// <item>sinkage z(N, slip) solved together with the suspension spring, N = N₀ − k_s·z(N), so the hub sits lower;</item>
        /// <item>compaction rolling resistance √(z/D);</item>
        /// <item>traction: the friction coefficient handed to the tyre becomes the soil's shear strength
        /// (A·c + N·tan φ)/N developed with the wheel's combined slip (Janosi–Hanamoto) — a tyre on sand
        /// has to spin before it pushes.</item>
        /// </list>
        /// Mixed points blend soil and rigid-layer values by their weights. False on rigid ground.
        /// </summary>
        public bool WheelSoil(Wheel wheel, Vec3 point, ref double normalForce, ref double muStatic, ref double muKinetic, out double rolling)
        {
            double slip = Math.Sqrt(wheel.SlipRatio * wheel.SlipRatio + Math.Pow(Math.Tan(Math.Max(-1.4, Math.Min(1.4, wheel.SlipAngle))), 2));
            bool loose = WheelSoil(point.X, point.Y, wheel.Radius, wheel.Width, wheel.SuspensionStiffness, slip, ref normalForce, out double sink, out rolling, out double mu, out double share);
            if (!loose) return false;
            muStatic = (1 - share) * muStatic + share * mu;
            muKinetic = Math.Min(muStatic * 0.95, (1 - share) * muKinetic + share * mu * 0.95);
            _ = sink;
            return true;
        }

        /// <summary>
        /// Soil under a wheel (radius, width) at (x, y) with combined slip <paramref name="slip"/>: reduces the
        /// normal force by the spring share of the sinkage and returns sinkage, rolling resistance, the
        /// developed traction coefficient and the loose share of the surface.
        /// </summary>
        public bool WheelSoil(double x, double y, double radius, double width, double springStiffness, double slip, ref double normalForce,
            out double sink, out double rolling, out double traction, out double looseShare)
        {
            sink = 0;
            rolling = 0;
            traction = 0;
            double n0 = normalForce;
            double share = LooseFraction(x, y, out var w);
            looseShare = share;
            if (n0 <= 0 || share <= 1e-4) return false;

            double Z(double load)
            {
                double z = 0;
                foreach (int l in Map.ActiveLayers)
                {
                    double f = w[l];
                    if (f <= 1e-4 || TerrainLayers.Get(l).Soil is not { } soil) continue;
                    z += f * soil.Sinkage(load, radius, width, slip);
                }

                return z / share;
            }

            // g(N) = N − (N₀ − k·share·z(N)) rises with N: bisection on [0, N₀].
            double a = 0, b = n0;
            if (springStiffness > 0)
            {
                for (int i = 0; i < 24; i++)
                {
                    double m = 0.5 * (a + b);
                    if (m - (n0 - springStiffness * looseShare * Z(m)) > 0) b = m;
                    else a = m;
                }
            }

            double n = springStiffness > 0 ? 0.5 * (a + b) : n0;
            sink = Z(n);
            normalForce = n;
            rolling = SoilModel.RollingFromSinkage(sink, radius) * looseShare;
            double length = SoilModel.ContactLength(radius, sink), area = Math.Max(1e-3, width) * length;
            foreach (int l in Map.ActiveLayers)
            {
                double f = w[l];
                if (f <= 1e-4 || TerrainLayers.Get(l).Soil is not { } soil) continue;
                traction += f / looseShare * soil.PeakTraction(n, area) * soil.ShearDevelopment(slip, length);
            }

            sink *= looseShare;
            return true;
        }

        /// <summary>
        /// Soil state of a rigid body resting on loose ground (used by <see cref="PhysicsScene"/>): the
        /// sinkage of its footprint and, for round bodies, the compaction rolling resistance and the shear
        /// traction coefficient at its slip.
        /// </summary>
        public bool BodySoil(double x, double y, double load, double radius, double width, double plateArea, double slip,
            out double sink, out double rolling, out double traction)
        {
            sink = rolling = traction = 0;
            double share = LooseFraction(x, y, out var w);
            if (load <= 0 || share <= 1e-4) return false;
            bool round = radius > 0;
            foreach (int l in Map.ActiveLayers)
            {
                double f = w[l];
                if (f <= 1e-4 || TerrainLayers.Get(l).Soil is not { } soil) continue;
                f /= share;
                double z, length, area;
                if (round)
                {
                    z = soil.Sinkage(load, radius, width, slip);
                    length = SoilModel.ContactLength(radius, z);
                    area = Math.Max(1e-3, width) * length;
                }
                else
                {
                    area = Math.Max(1e-5, plateArea);
                    z = soil.PlateSinkage(load / area, Math.Max(1e-3, width), Math.Max(0.002, 0.25 * width));
                    length = Math.Sqrt(area);
                }

                sink += f * z;
                traction += f * soil.PeakTraction(load, area) * (round ? soil.ShearDevelopment(slip, length) : 1);
                if (round) rolling += f * SoilModel.RollingFromSinkage(z, radius);
            }

            sink *= share;
            rolling *= share;
            return true;
        }
    }
}
