using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    // [terrain] Rigid bodies on loose terrain layers: load-dependent sinkage, compaction rolling
    // resistance and soil shear traction (Bekker / Mohr–Coulomb / Janosi–Hanamoto, see SoilModel).
    public sealed partial class PhysicsScene
    {
        private sealed class SoilState
        {
            public double Sink, Rolling, Traction, X, Y;
            public bool Active;
        }

        private readonly Dictionary<SceneBody, SoilState> _soil = new Dictionary<SceneBody, SoilState>();
        private SoilState? _groundSoil;
        private readonly Dictionary<int, (double N, Vec3 P)> _soilLoad = new Dictionary<int, (double N, Vec3 P)>();

        /// <summary>Time constant (s) with which a body settles into / rises out of loose ground.</summary>
        public double SoilSettleTime { get; set; } = 0.04;

        /// <summary>Current sinkage (m) of a body into loose terrain (0 on rigid ground).</summary>
        public double SoilSinkage(SceneBody body) => _soil.TryGetValue(body, out var s) && s.Active ? s.Sink : 0;

        /// <summary>Ground collision of one body with its soil state applied to the contacts.</summary>
        private void CollideGroundSoil(int a, double h)
        {
            _groundSoil = _soil.Count > 0 && _soil.TryGetValue(Bodies[a], out var s) && s.Active ? s : null;
            CollideGround(a, h);
            _groundSoil = null;
        }

        /// <summary>Extra separation of a ground contact point: the body has sunk into the loose layer there.</summary>
        private double SoilSink(Vec3 p)
        {
            var s = _groundSoil;
            if (s == null || s.Sink <= 0 || Terrain is not TerrainSurface ts) return 0;
            double share = ts.LooseFraction(p.X, p.Y, out _);
            return share <= 1e-4 ? 0 : s.Sink * share;
        }

        /// <summary>Contact on the painted surface, with the soil's traction and compaction resistance for a sunk body.</summary>
        private ContactProperties SoilContact(TerrainSurface surface, int bodyMaterial, double x, double y, bool caster)
        {
            var props = surface.Contact(bodyMaterial, x, y, caster);
            var s = _groundSoil;
            if (s == null || caster || !s.Active) return props;
            double share = surface.LooseFraction(x, y, out _);
            if (share <= 1e-4) return props;
            double mu = (1 - share) * props.StaticFriction + share * s.Traction;
            double muK = Math.Min(mu, (1 - share) * props.KineticFriction + share * s.Traction * 0.95);
            double crr = Math.Max(props.RollingResistance, share * s.Rolling);
            return new ContactProperties(mu, muK, props.Restitution, crr, props.Stiffness);
        }

        /// <summary>
        /// After a step: the ground load of every body (from the last substep's contact impulses) gives
        /// its sinkage, rolling resistance and traction on the loose layers under it; they settle with
        /// <see cref="SoilSettleTime"/> so contact and soil do not chase each other.
        /// </summary>
        private void UpdateSoil(double dt)
        {
            if (Terrain is not TerrainSurface ts || !ts.HasLooseGround)
            {
                if (_soil.Count > 0) _soil.Clear();
                return;
            }

            foreach (var st in _soil.Values) st.Active = false;
            var load = _soilLoad;
            load.Clear();
            double h = _h > 0 ? _h : dt;
            for (int i = 0; i < _contactCount; i++)
            {
                ref var c = ref _contacts[i];
                if (c.B >= 0 || c.Pn <= 0) continue;
                double n = c.Pn / h;
                var p = Bodies[c.A].Position + c.RA;
                load[c.A] = load.TryGetValue(c.A, out var prev) ? (prev.N + n, prev.P + p * n) : (n, p * n);
            }

            double k = Math.Min(1, dt / Math.Max(1e-4, SoilSettleTime));
            foreach (var kv in load)
            {
                var body = Bodies[kv.Key];
                var shape = body.EffectiveShape;
                if (shape == null) continue;
                var at = kv.Value.P / kv.Value.N;
                double radius = 0, width = 0, area = 0;
                var down = -GroundNormal(at.X, at.Y);
                switch (shape.Kind)
                {
                    case ShapeKind.Cylinder:
                    {
                        var axis = body.Orientation.Rotate(Vec3.UnitZ);
                        if (Math.Abs(axis.Dot(down)) < 0.5)
                        {
                            radius = shape.Radius;
                            width = 2 * shape.HalfHeight;
                        }
                        else
                        {
                            area = Math.PI * shape.Radius * shape.Radius;
                            width = 2 * shape.Radius;
                        }

                        break;
                    }

                    case ShapeKind.Sphere:
                        radius = shape.Radius;
                        width = shape.Radius;
                        break;
                    case ShapeKind.Box:
                    {
                        // Footprint = the face turned to the ground.
                        var e = shape.HalfExtents * 2;
                        var local = body.Orientation.InverseRotate(down);
                        double ax = Math.Abs(local.X), ay = Math.Abs(local.Y), az = Math.Abs(local.Z);
                        (double u, double v) = az >= ax && az >= ay ? (e.X, e.Y) : ay >= ax ? (e.X, e.Z) : (e.Y, e.Z);
                        area = u * v;
                        width = Math.Min(u, v);
                        break;
                    }

                    default:
                    {
                        double r = shape.BoundingRadius;
                        area = Math.Max(1e-4, 0.8 * r * r);
                        width = Math.Sqrt(area);
                        break;
                    }
                }

                // Slip of a round body: speed of its contact point relative to its travel.
                double slip = 0;
                if (radius > 0)
                {
                    var vc = body.PointVelocity(at);
                    double travel = Math.Max(0.05, Math.Max(body.Velocity.Length, body.AngularVelocity.Length * radius));
                    slip = Math.Min(1, vc.Length / travel);
                }

                if (!ts.BodySoil(at.X, at.Y, kv.Value.N, radius, width, area, slip, out double sink, out double rolling, out double traction)) continue;
                if (!_soil.TryGetValue(body, out var st))
                {
                    st = new SoilState { Traction = traction, Rolling = rolling };
                    _soil[body] = st;
                }

                st.Sink += (sink - st.Sink) * k;
                st.Rolling += (rolling - st.Rolling) * k;
                st.Traction += (traction - st.Traction) * k;
                st.X = at.X;
                st.Y = at.Y;
                st.Active = true;
            }

            // Lifted bodies come out of their ruts.
            List<SceneBody>? gone = null;
            foreach (var kv in _soil)
            {
                if (kv.Value.Active) continue;
                kv.Value.Sink -= kv.Value.Sink * k;
                if (kv.Value.Sink < 1e-5) (gone ??= new List<SceneBody>()).Add(kv.Key);
                else kv.Value.Active = true;
            }

            if (gone != null)
                foreach (var b in gone)
                    _soil.Remove(b);
        }
    }
}
