using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Editor
{
    /// <summary>
    /// 2-D particle effects over the schematic: the flash and sparks at the moment a part fails,
    /// smoke rising from burnt or overheating parts, and glowing embers. Positions are in sheet
    /// (grid) units so the effects stay on the part while the user pans and zooms.
    /// </summary>
    public sealed class SchematicEffects
    {
        private enum Kind
        {
            Smoke,
            Spark,
            Flash,
            Ember
        }

        private struct Particle
        {
            public Kind Kind;
            public double X, Y, Vx, Vy, Age, Life, Size, Seed;
            public bool Dark;
        }

        private readonly List<Particle> _particles = new List<Particle>();
        private readonly Random _rng = new Random(20261120);
        private readonly Dictionary<string, double> _emitDebt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        private static readonly Brush SmokeLight = Soft(Color.FromRgb(0x9A, 0x9A, 0x9E));
        private static readonly Brush SmokeDark = Soft(Color.FromRgb(0x3C, 0x3A, 0x38));

        public bool Active => _particles.Count > 0;

        public void Clear()
        {
            _particles.Clear();
            _emitDebt.Clear();
        }

        private static Brush Soft(Color c)
        {
            var b = new RadialGradientBrush(Color.FromArgb(0xC0, c.R, c.G, c.B), Color.FromArgb(0x00, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        private double R(double a, double b) => a + (b - a) * _rng.NextDouble();

        /// <summary>The instant of failure: white-orange flash, a spray of sparks and a dense puff.</summary>
        public void Burst(GridPoint at, double size, bool violent)
        {
            _particles.Add(new Particle { Kind = Kind.Flash, X = at.X, Y = at.Y, Life = 0.35, Size = size * (violent ? 3.2 : 2.2) });
            int sparks = violent ? 34 : 18;
            for (int i = 0; i < sparks; i++)
            {
                double a = R(0, 2 * Math.PI), v = R(4, violent ? 16 : 10);
                _particles.Add(new Particle
                {
                    Kind = Kind.Spark,
                    X = at.X + R(-0.3, 0.3),
                    Y = at.Y + R(-0.3, 0.3),
                    Vx = Math.Cos(a) * v,
                    Vy = Math.Sin(a) * v - R(2, 6),
                    Life = R(0.25, 0.7),
                    Size = R(0.08, 0.16),
                    Seed = _rng.NextDouble()
                });
            }

            for (int i = 0; i < (violent ? 14 : 8); i++) AddSmoke(at.X, at.Y, size, true, 1.6);
        }

        /// <summary>Continuous smoke from a part: <paramref name="rate"/> puffs per second.</summary>
        public void Emit(string key, GridPoint at, double size, double rate, double dt, bool dark)
        {
            double debt = (_emitDebt.TryGetValue(key, out double d) ? d : 0) + rate * dt;
            while (debt >= 1)
            {
                AddSmoke(at.X, at.Y, size, dark, 1.0);
                debt -= 1;
            }

            _emitDebt[key] = debt;
        }

        /// <summary>A few glowing embers on a freshly burnt part.</summary>
        public void Embers(GridPoint at, double size, double rate, double dt)
        {
            if (_rng.NextDouble() > rate * dt) return;
            _particles.Add(new Particle
            {
                Kind = Kind.Ember,
                X = at.X + R(-size, size) * 0.5,
                Y = at.Y + R(-size, size) * 0.3,
                Vx = R(-0.4, 0.4),
                Vy = R(-1.5, -0.5),
                Life = R(0.4, 1.0),
                Size = R(0.06, 0.12)
            });
        }

        private void AddSmoke(double x, double y, double size, bool dark, double boost)
        {
            _particles.Add(new Particle
            {
                Kind = Kind.Smoke,
                X = x + R(-0.4, 0.4) * size,
                Y = y + R(-0.2, 0.2) * size,
                Vx = R(-0.6, 0.6),
                Vy = -R(1.6, 3.2) * boost,
                Life = R(1.6, 3.2),
                Size = R(0.5, 0.9) * Math.Max(0.6, size * 0.6),
                Seed = _rng.NextDouble(),
                Dark = dark
            });
        }

        public void Update(double dt)
        {
            if (dt <= 0) return;
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
                    case Kind.Smoke:
                        // Rises, slows, drifts sideways and spreads.
                        p.Vy *= Math.Exp(-0.5 * dt);
                        p.Vx += Math.Sin((p.Age + p.Seed * 6) * 2.3) * 0.9 * dt;
                        p.Size += 0.9 * dt;
                        break;
                    case Kind.Spark:
                        p.Vy += 22 * dt;
                        p.Vx *= Math.Exp(-1.2 * dt);
                        break;
                    case Kind.Ember:
                        p.Vy += 1.0 * dt;
                        break;
                }

                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
                _particles[i] = p;
            }

            if (_particles.Count > 2500) _particles.RemoveRange(0, _particles.Count - 2500);
        }

        private static Brush Solid(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public void Draw(DrawingContext dc, Func<double, double, Point> toScreen, double scale)
        {
            foreach (var p in _particles)
            {
                double life = p.Age / p.Life;
                var at = toScreen(p.X, p.Y);
                switch (p.Kind)
                {
                    case Kind.Smoke:
                    {
                        double alpha = Math.Min(1.0, life * 6) * (1 - life) * (p.Dark ? 0.75 : 0.45);
                        double r = p.Size * scale;
                        dc.PushOpacity(alpha);
                        dc.DrawEllipse(p.Dark ? SmokeDark : SmokeLight, null, at, r, r);
                        dc.Pop();
                        break;
                    }

                    case Kind.Spark:
                    {
                        var c = life < 0.3 ? Color.FromRgb(0xFF, 0xF4, 0xC8) : Color.FromRgb(0xFF, (byte)(200 - 120 * life), 0x40);
                        var tail = toScreen(p.X - p.Vx * 0.025, p.Y - p.Vy * 0.025);
                        var pen = new Pen(Solid(Color.FromArgb((byte)(255 * (1 - life)), c.R, c.G, c.B)), Math.Max(1.2, p.Size * scale * 0.6))
                        {
                            StartLineCap = PenLineCap.Round,
                            EndLineCap = PenLineCap.Round
                        };
                        pen.Freeze();
                        dc.DrawLine(pen, tail, at);
                        break;
                    }

                    case Kind.Flash:
                    {
                        double r = p.Size * scale * (0.6 + 0.8 * life);
                        var brush = new RadialGradientBrush(
                            Color.FromArgb((byte)(230 * (1 - life)), 0xFF, 0xF6, 0xDC),
                            Color.FromArgb(0, 0xFF, 0x8A, 0x20))
                        {
                            GradientOrigin = new Point(0.5, 0.5)
                        };
                        brush.GradientStops.Insert(1, new GradientStop(Color.FromArgb((byte)(160 * (1 - life)), 0xFF, 0xA0, 0x30), 0.45));
                        brush.Freeze();
                        dc.DrawEllipse(brush, null, at, r, r);
                        break;
                    }

                    case Kind.Ember:
                    {
                        var c = Color.FromArgb((byte)(255 * (1 - life)), 0xFF, (byte)(140 - 80 * life), 0x30);
                        double r = Math.Max(1.0, p.Size * scale);
                        dc.DrawEllipse(Solid(c), null, at, r, r);
                        break;
                    }
                }
            }
        }
    }
}
