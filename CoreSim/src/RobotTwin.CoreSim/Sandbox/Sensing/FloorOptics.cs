using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox.Sensing
{
    /// <summary>What the floor is like at one point, optically (near-IR).</summary>
    public readonly struct FloorSample
    {
        public FloorSample(double diffuse, double specular, int tape)
        {
            Diffuse = diffuse;
            Specular = specular;
            Tape = tape;
        }

        /// <summary>Lambertian reflectance (0–1).</summary>
        public double Diffuse { get; }

        /// <summary>Mirror reflectance at normal incidence (0–1); grows towards grazing angles (Fresnel).</summary>
        public double Specular { get; }

        /// <summary>Index of the tape at this point, -1 = bare floor, -2 = off the floor (edge / void).</summary>
        public int Tape { get; }
    }

    /// <summary>
    /// Floor reflectance map of a world: bare floor, taped track sections with their finish
    /// (matte, faded with patchy wear, glossy, metallic) and the legacy oval/eight tracks.
    /// </summary>
    public sealed class FloorOptics
    {
        private sealed class Strip
        {
            public FloorTape Tape = null!;
            public double[] Xs = Array.Empty<double>();
            public double[] Ys = Array.Empty<double>();
            public double MinX, MinY, MaxX, MaxY, Half;
            public double Diffuse, Specular, Patch;
        }

        private readonly List<Strip> _strips = new List<Strip>();
        private readonly double _halfW, _halfD;

        public FloorOptics(WorldSpec env)
        {
            // A world without sensing settings has the editor's dark workshop floor.
            Environment = env.Sensing ?? new SensingEnvironment { FloorReflectance = 0.3, FloorGloss = 0.02 };
            _halfW = Math.Max(0.25, env.Width) / 2;
            _halfD = Math.Max(0.25, env.Depth) / 2;
            Tapes = AllTapes(env);
            foreach (var t in Tapes)
            {
                if (t.Points.Count < 2) continue;
                var (d, s, p) = t.Optics();
                var st = new Strip
                {
                    Tape = t, Xs = t.Points.Select(q => q[0]).ToArray(), Ys = t.Points.Select(q => q[1]).ToArray(),
                    Half = Math.Max(0.001, t.Width / 2), Diffuse = d, Specular = s, Patch = p
                };
                st.MinX = st.Xs.Min() - st.Half;
                st.MaxX = st.Xs.Max() + st.Half;
                st.MinY = st.Ys.Min() - st.Half;
                st.MaxY = st.Ys.Max() + st.Half;
                _strips.Add(st);
            }
        }

        public SensingEnvironment Environment { get; }

        /// <summary>Every tape of the world: the sensing environment's tapes plus the legacy track as matte tape.</summary>
        public IReadOnlyList<FloorTape> Tapes { get; }

        public static List<FloorTape> AllTapes(WorldSpec env)
        {
            var list = new List<FloorTape>();
            if (env.Sensing != null) list.AddRange(env.Sensing.Tapes);
            IEnumerable<List<(double X, double Y)>> legacy = Enumerable.Empty<List<(double, double)>>();
            if (env.Track == "oval" || env.Track == "eight")
            {
                legacy = Arena.Create(env.Track).Lines.Select(l => l.Select(p => (p.X * env.TrackScale, p.Y * env.TrackScale)).ToList());
            }
            else if (env.Track == "custom")
            {
                legacy = env.Lines.Where(l => l.Count >= 2).Select(l => l.Select(p => (p[0], p[1])).ToList());
            }

            foreach (var line in legacy)
            {
                list.Add(new FloorTape { Name = "Pist", Width = env.LineWidth, Finish = TapeFinish.Matte, Points = line.Select(p => new[] { p.Item1, p.Item2 }).ToList() });
            }

            return list;
        }

        /// <summary>Optical floor properties at a world point (metres).</summary>
        public FloorSample At(double x, double y)
        {
            if (Math.Abs(x) > _halfW || Math.Abs(y) > _halfD) return new FloorSample(0.02, 0, -2);
            for (int i = _strips.Count - 1; i >= 0; i--)
            {
                var s = _strips[i];
                if (x < s.MinX || x > s.MaxX || y < s.MinY || y > s.MaxY) continue;
                for (int k = 0; k + 1 < s.Xs.Length; k++)
                {
                    if (Pcb.Geometry.PointSegmentDistance(x, y, s.Xs[k], s.Ys[k], s.Xs[k + 1], s.Ys[k + 1]) > s.Half) continue;
                    double d = s.Diffuse, spec = s.Specular;
                    if (s.Tape.Finish == TapeFinish.Faded)
                    {
                        // Wear in patches (≈1 cm) with fine scuffs (≈2 mm) where the floor shows through.
                        double coarse = ValueNoise(x / 0.011, y / 0.011, s.Tape.Seed);
                        double fine = ValueNoise(x / 0.0022, y / 0.0022, s.Tape.Seed * 31 + 7);
                        d += s.Patch * (coarse - 0.5) * 2;
                        if (fine > 0.86 - 0.12 * s.Tape.Wear) d = Lerp(d, Environment.FloorReflectance, 0.7);
                        spec = Lerp(spec, 0.01, coarse);
                    }
                    else if (s.Patch > 0)
                    {
                        d += s.Patch * (ValueNoise(x / 0.006, y / 0.006, s.Tape.Seed) - 0.5);
                    }

                    return new FloorSample(Math.Max(0.01, Math.Min(0.98, d)), spec, IndexOf(s.Tape));
                }
            }

            return new FloorSample(Environment.FloorReflectance, Environment.FloorGloss, -1);
        }

        private int IndexOf(FloorTape t)
        {
            for (int i = 0; i < Tapes.Count; i++)
            {
                if (ReferenceEquals(Tapes[i], t)) return i;
            }

            return -1;
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        /// <summary>Smooth deterministic 2D value noise in [0, 1).</summary>
        public static double ValueNoise(double x, double y, int seed)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            double fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            double a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
            return Lerp(Lerp(a, b, fx), Lerp(c, d, fx), fy);
        }

        private static double Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }
    }
}
