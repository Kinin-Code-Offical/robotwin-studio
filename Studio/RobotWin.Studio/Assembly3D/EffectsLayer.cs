using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace RobotWin.Studio.Assembly3D
{
    public enum ParticleKind
    {
        Smoke,
        Fire,
        Spark,
        Flash,
        Ring
    }

    /// <summary>
    /// Screen-space particle layer drawn over the 3D viewport: smoke from hot or
    /// burnt parts, flames from a battery in thermal runaway, sparks and the flash
    /// of a part that blows up. Particles live in world coordinates and are
    /// projected with the current camera every frame.
    /// </summary>
    public sealed class EffectsLayer : FrameworkElement
    {
        private sealed class Particle
        {
            public ParticleKind Kind;
            public Vector3D Pos;
            public Vector3D Vel;
            public double Age;
            public double Life;
            public double Size;
            public double Seed;
        }

        private readonly List<Particle> _particles = new List<Particle>();
        private readonly Random _rnd = new Random(3);
        private static readonly Brush SmokeBrush = Soft(Color.FromRgb(0x8E, 0x8E, 0x92));
        private static readonly Brush DarkSmokeBrush = Soft(Color.FromRgb(0x38, 0x36, 0x34));
        private static readonly Brush FireBrush = Soft(Color.FromRgb(0xFF, 0x8A, 0x1F));
        private static readonly Brush FireCoreBrush = Soft(Color.FromRgb(0xFF, 0xE0, 0x7A));
        private static readonly Brush FlashBrush = Soft(Color.FromRgb(0xFF, 0xF4, 0xD6));

        public EffectsLayer()
        {
            IsHitTestVisible = false;
        }

        /// <summary>World point → screen point, depth (m) and visibility.</summary>
        public Func<Point3D, (Point Screen, double Depth, bool Visible)>? Project { get; set; }

        /// <summary>Pixels per metre at one metre of depth.</summary>
        public double Focal { get; set; } = 800;

        public int Count => _particles.Count;

        public void Clear()
        {
            _particles.Clear();
            InvalidateVisual();
        }

        private static Brush Soft(Color c)
        {
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(Color.FromArgb(255, c.R, c.G, c.B), 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(150, c.R, c.G, c.B), 0.45));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            b.Freeze();
            return b;
        }

        private double R(double a, double b) => a + (b - a) * _rnd.NextDouble();

        public void EmitSmoke(Point3D at, double rate, double dt, bool dark = false)
        {
            if (!Shell.AppSettings.Current.Effects) return;
            double n = rate * dt;
            int count = (int)n + (_rnd.NextDouble() < n - (int)n ? 1 : 0);
            for (int i = 0; i < count; i++)
            {
                _particles.Add(new Particle
                {
                    Kind = ParticleKind.Smoke,
                    Pos = new Vector3D(at.X + R(-0.005, 0.005), at.Y + R(-0.005, 0.005), at.Z),
                    Vel = new Vector3D(R(-0.02, 0.02), R(-0.02, 0.02), R(0.06, 0.14)),
                    Life = R(1.6, 3.2),
                    Size = R(0.012, 0.02),
                    Seed = dark ? 1 : 0
                });
            }
        }

        public void EmitFire(Point3D at, double rate, double dt)
        {
            if (!Shell.AppSettings.Current.Effects) return;
            double n = rate * dt;
            int count = (int)n + (_rnd.NextDouble() < n - (int)n ? 1 : 0);
            for (int i = 0; i < count; i++)
            {
                _particles.Add(new Particle
                {
                    Kind = ParticleKind.Fire,
                    Pos = new Vector3D(at.X + R(-0.01, 0.01), at.Y + R(-0.01, 0.01), at.Z),
                    Vel = new Vector3D(R(-0.03, 0.03), R(-0.03, 0.03), R(0.25, 0.5)),
                    Life = R(0.25, 0.55),
                    Size = R(0.012, 0.022),
                    Seed = _rnd.NextDouble()
                });
            }

            EmitSmoke(new Point3D(at.X, at.Y, at.Z + 0.03), rate * 0.25, dt, true);
        }

        public void Burst(Point3D at, bool explosion)
        {
            if (!Shell.AppSettings.Current.Effects) return;
            var c = new Vector3D(at.X, at.Y, at.Z);
            if (explosion)
            {
                _particles.Add(new Particle { Kind = ParticleKind.Flash, Pos = c, Life = 0.22, Size = 0.09 });
                _particles.Add(new Particle { Kind = ParticleKind.Ring, Pos = c, Life = 0.45, Size = 0.18 });
            }

            int sparks = explosion ? 60 : 24;
            for (int i = 0; i < sparks; i++)
            {
                double a = R(0, 2 * Math.PI), e = R(0.1, 1.3), v = R(0.4, explosion ? 2.2 : 1.0);
                _particles.Add(new Particle
                {
                    Kind = ParticleKind.Spark,
                    Pos = c,
                    Vel = new Vector3D(Math.Cos(a) * Math.Cos(e) * v, Math.Sin(a) * Math.Cos(e) * v, Math.Sin(e) * v),
                    Life = R(0.3, 0.9),
                    Size = 0.002
                });
            }

            for (int i = 0; i < (explosion ? 26 : 12); i++)
            {
                _particles.Add(new Particle
                {
                    Kind = ParticleKind.Smoke,
                    Pos = c,
                    Vel = new Vector3D(R(-0.25, 0.25), R(-0.25, 0.25), R(0.05, 0.35)),
                    Life = R(1.5, 3.0),
                    Size = R(0.02, 0.04),
                    Seed = 1
                });
            }
        }

        public void Step(double dt)
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    _particles.RemoveAt(i);
                    continue;
                }

                switch (p.Kind)
                {
                    case ParticleKind.Spark:
                        p.Vel += new Vector3D(0, 0, -9.81 * dt);
                        if (p.Pos.Z < 0 && p.Vel.Z < 0) p.Vel = new Vector3D(p.Vel.X * 0.5, p.Vel.Y * 0.5, -p.Vel.Z * 0.3);
                        break;
                    case ParticleKind.Smoke:
                        p.Vel *= Math.Max(0, 1 - 0.6 * dt);
                        p.Vel += new Vector3D(0.015 * dt, 0, 0.05 * dt);
                        break;
                    case ParticleKind.Fire:
                        p.Vel += new Vector3D(0, 0, 0.6 * dt);
                        break;
                }

                p.Pos += p.Vel * dt;
            }

            if (_particles.Count > 1500) _particles.RemoveRange(0, _particles.Count - 1500);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (Project == null) return;
            // Far to near so close particles cover distant ones.
            var order = new List<(double Depth, Particle P, Point S)>(_particles.Count);
            foreach (var p in _particles)
            {
                var (s, depth, visible) = Project(new Point3D(p.Pos.X, p.Pos.Y, p.Pos.Z));
                if (visible) order.Add((depth, p, s));
            }

            order.Sort((a, b) => b.Depth.CompareTo(a.Depth));
            foreach (var (depth, p, s) in order)
            {
                double k = p.Age / p.Life;
                double px = Focal / Math.Max(0.05, depth);
                switch (p.Kind)
                {
                    case ParticleKind.Smoke:
                    {
                        double r = (p.Size + 0.05 * k) * px;
                        dc.PushOpacity((1 - k) * (k < 0.1 ? k / 0.1 : 1) * 0.55);
                        dc.DrawEllipse(p.Seed > 0.5 ? DarkSmokeBrush : SmokeBrush, null, s, r, r);
                        dc.Pop();
                        break;
                    }

                    case ParticleKind.Fire:
                    {
                        double r = p.Size * (1 - 0.6 * k) * px;
                        dc.PushOpacity(1 - k);
                        dc.DrawEllipse(FireBrush, null, s, r, r * 1.3);
                        dc.DrawEllipse(FireCoreBrush, null, s, r * 0.5, r * 0.65);
                        dc.Pop();
                        break;
                    }

                    case ParticleKind.Spark:
                    {
                        var (s2, _, _) = Project(new Point3D(p.Pos.X - p.Vel.X * 0.02, p.Pos.Y - p.Vel.Y * 0.02, p.Pos.Z - p.Vel.Z * 0.02));
                        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * (1 - k)), 0xFF, 0xD0, 0x60)), Math.Max(1, 0.003 * px));
                        dc.DrawLine(pen, s, s2);
                        break;
                    }

                    case ParticleKind.Flash:
                    {
                        double r = p.Size * (0.6 + 1.2 * k) * px;
                        dc.PushOpacity(1 - k);
                        dc.DrawEllipse(FlashBrush, null, s, r, r);
                        dc.Pop();
                        break;
                    }

                    case ParticleKind.Ring:
                    {
                        double r = p.Size * k * px;
                        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(180 * (1 - k)), 0xFF, 0xE8, 0xC0)), Math.Max(1, 3 * (1 - k)));
                        dc.DrawEllipse(null, pen, s, r, r * 0.45);
                        break;
                    }
                }
            }
        }
    }
}
