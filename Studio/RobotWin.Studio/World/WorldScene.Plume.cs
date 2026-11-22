using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [gas] Exhaust plumes in the 3D scene itself, so they sit at their depth: a body in front hides them,
    /// they hide what is behind them. Every nozzle that is blowing hot gas shows a flickering flame cone
    /// whose length follows the exit diameter; gas leaves the nozzle at the exit velocity on top of the
    /// vessel's own velocity and is stopped by the still air within a few flame lengths, so a climbing
    /// rocket leaves its smoke behind in the air. Water jets fall under gravity. Particles live in
    /// simulation time: a paused world freezes its plume, slow motion slows it.
    /// </summary>
    public sealed partial class WorldScene
    {
        private sealed class PlumeParticle
        {
            public Vector3D Pos, Vel, Air;
            public double Age, Life, Size0, Size1, Tau, Opacity;
            public int Kind;   // 0 smoke, 1 fire, 2 water
        }

        private const int AtlasColumns = 8, AtlasRows = 3;
        private readonly List<PlumeParticle> _plume = new List<PlumeParticle>();
        private readonly ModelVisual3D _plumeVisual = new ModelVisual3D();
        private readonly Random _plumeRnd = new Random(11);
        private double _plumeClock = double.NaN;
        private static ImageBrush? _plumeAtlas;
        private static Material? _smokeMat, _fireMat, _waterMat, _flameOuter, _flameMid, _flameCore;

        private double PR(double a, double b) => a + (b - a) * _plumeRnd.NextDouble();

        /// <summary>Soft round sprites, columns by opacity, rows: smoke, fire, water.</summary>
        private static ImageBrush Atlas()
        {
            if (_plumeAtlas != null) return _plumeAtlas;
            const int cell = 32;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var rows = new[] { Color.FromRgb(0xEC, 0xEB, 0xE6), Color.FromRgb(0xFF, 0xB8, 0x6A), Color.FromRgb(0xB4, 0xD6, 0xFF) };
                for (int r = 0; r < AtlasRows; r++)
                    for (int c = 0; c < AtlasColumns; c++)
                    {
                        byte a = (byte)(255 * (1 - c / (double)AtlasColumns));
                        var col = rows[r];
                        var brush = new RadialGradientBrush(Color.FromArgb(a, col.R, col.G, col.B), Color.FromArgb(0, col.R, col.G, col.B));
                        brush.GradientStops.Insert(1, new GradientStop(Color.FromArgb((byte)(a * 0.55), col.R, col.G, col.B), 0.55));
                        brush.Freeze();
                        dc.DrawEllipse(brush, null, new Point(c * cell + cell / 2.0, r * cell + cell / 2.0), cell / 2.0 - 1, cell / 2.0 - 1);
                    }
            }

            var bmp = new RenderTargetBitmap(cell * AtlasColumns, cell * AtlasRows, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            var ib = new ImageBrush(bmp) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
            ib.Freeze();
            return _plumeAtlas = ib;
        }

        private static Material Emissive(Color c)
        {
            var m = new MaterialGroup { Children = { new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))), new EmissiveMaterial(new SolidColorBrush(c)) } };
            m.Freeze();
            return m;
        }

        private void EnsurePlumeMaterials()
        {
            if (_smokeMat != null) return;
            var atlas = Atlas();
            var smoke = new MaterialGroup { Children = { new DiffuseMaterial(atlas), new EmissiveMaterial(new ImageBrush(atlas.ImageSource) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1), Opacity = 0.35 }) } };
            smoke.Freeze();
            _smokeMat = smoke;
            var fire = new MaterialGroup { Children = { new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))), new EmissiveMaterial(atlas) } };
            fire.Freeze();
            _fireMat = fire;
            var water = new MaterialGroup { Children = { new DiffuseMaterial(atlas), new SpecularMaterial(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 30) } };
            water.Freeze();
            _waterMat = water;
            // Potassium-salt flames (KNO₃ propellants) burn a pinkish orange around a pale yellow core.
            _flameOuter = Emissive(Color.FromArgb(0x55, 0xFF, 0x6E, 0x4A));
            _flameMid = Emissive(Color.FromArgb(0x8C, 0xFF, 0x9C, 0x5A));
            _flameCore = Emissive(Color.FromArgb(0xF0, 0xFF, 0xF2, 0xD0));
        }

        /// <summary>A blowing nozzle this frame, from the live physics or from a recorded run.</summary>
        private readonly struct JetSource
        {
            public JetSource(Vector3D at, Vector3D dir, Vector3D velocity, double de, double massFlow, double exitVelocity, double thrust, double smoke, bool hot, bool water)
            {
                At = at;
                Dir = dir;
                Velocity = velocity;
                De = de;
                MassFlow = massFlow;
                ExitVelocity = exitVelocity;
                Thrust = thrust;
                Smoke = smoke;
                Hot = hot;
                Water = water;
            }

            public Vector3D At { get; }
            public Vector3D Dir { get; }
            public Vector3D Velocity { get; }
            public double De { get; }
            public double MassFlow { get; }
            public double ExitVelocity { get; }
            public double Thrust { get; }
            public double Smoke { get; }
            public bool Hot { get; }
            public bool Water { get; }
        }

        private readonly Dictionary<string, (Vector3D At, double Time)> _plumeLast = new Dictionary<string, (Vector3D, double)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The nozzles that are blowing now, and the time they belong to; null when nothing runs or replays.</summary>
        private (List<JetSource> Sources, double Time)? JetSources()
        {
            if (World == null) return null;
            var list = new List<JetSource>();
            if (Playback is { } pb)
            {
                // [render] a replay: the recorded jet at the recorded pose of its vessel
                double now = pb.Time;
                foreach (var o in World.Objects)
                {
                    if (!WorldPhysics.IsVessel(o) || pb.PoseOf(o.Id) is not { } pose) continue;
                    double mdot = pb.Value("gas/" + o.Id + "/mdot");
                    if (double.IsNaN(mdot) || mdot <= 1e-3) continue;
                    var (np, axis) = WorldPhysics.NozzleFrame(o);
                    var w = pose.Position + pose.Orientation.Rotate(np);
                    var d = pose.Orientation.Rotate(axis);
                    var at = new Vector3D(w.X, w.Y, w.Z);
                    var v = _plumeLast.TryGetValue(o.Id, out var last) && now > last.Time ? (at - last.At) / (now - last.Time) : new Vector3D();
                    _plumeLast[o.Id] = (at, now);
                    double Val(string k) => pb.Value("gas/" + o.Id + "/" + k) is double x && !double.IsNaN(x) ? x : 0;
                    list.Add(new JetSource(at, new Vector3D(d.X, d.Y, d.Z), v, Math.Max(0.002, WorldPhysics.NozzleExitDiameterOf(o)), mdot / 1000, Val("ve"), Val("thrust"), Val("smoke") / 1000,
                        Val("hot") > 0.5, Val("water") > 1e-4));
                }

                return (list, now);
            }

            var physics = Session != null && Session.State != Simulation.SimState.Stopped ? Session.Physics : null;
            if (physics == null) return null;
            foreach (var o in World.Objects)
            {
                if (!WorldPhysics.IsVessel(o) || physics.GasOf(o.Id) is not { } g || physics.NozzleOf(o.Id) is not { } n || g.MassFlow <= 1e-6) continue;
                var src = physics.VelocityAt(o.Id, n.Point);
                list.Add(new JetSource(new Vector3D(n.Point.X, n.Point.Y, n.Point.Z), new Vector3D(n.Direction.X, n.Direction.Y, n.Direction.Z), new Vector3D(src.X, src.Y, src.Z),
                    Math.Max(0.002, physics.NozzleExitDiameter(o.Id)), g.MassFlow, g.ExitVelocity, g.Thrust, g.SmokeRate, n.Hot, n.Water));
            }

            return (list, physics.Scene.Time);
        }

        /// <summary>Per frame: emit from every blowing nozzle, move the gas, draw plume and flames (live or replayed).</summary>
        private void PlumeFrame()
        {
            if (JetSources() is not { } jets)
            {
                if (_plume.Count > 0 || _plumeVisual.Content != null) ClearPlume();
                return;
            }

            EnsurePlumeMaterials();
            double now = jets.Time;
            if (!double.IsNaN(_plumeClock) && now < _plumeClock - 1e-9) _plume.Clear();   // the replay went back: start the plume afresh
            double dt = double.IsNaN(_plumeClock) ? 0 : Math.Max(0, Math.Min(0.1, now - _plumeClock));
            _plumeClock = now;
            if (dt > 0)
            {
                for (int i = _plume.Count - 1; i >= 0; i--)
                {
                    var p = _plume[i];
                    Advance(p, dt);
                    if (p.Age >= p.Life || p.Kind == 2 && p.Pos.Z < GroundZAt(p.Pos.X, p.Pos.Y) && p.Vel.Z < 0) _plume.RemoveAt(i);
                }

                if (_plume.Count > 4000) _plume.RemoveRange(0, _plume.Count - 4000);
            }

            var flames = new Model3DGroup();
            bool effects = Shell.AppSettings.Current.Effects;
            foreach (var j in jets.Sources)
            {
                double de = j.De;
                var at = j.At;
                var dir = j.Dir;
                if (dir.LengthSquared < 1e-12) continue;
                dir.Normalize();
                bool hot = j.Hot && !j.Water;
                // Visible flame of a fuel-rich, afterburning exhaust: about fifteen exit diameters.
                double flame = hot ? 15 * de * Math.Sqrt(Math.Max(0.2, Math.Min(2.0, j.Thrust / 100))) : 0;
                if (hot) flames.Children.Add(FlameModel(at, dir, de / 2, flame));
                if (!effects || dt <= 0) continue;
                var srcV = j.Velocity;
                int kind = j.Water ? 2 : 0;
                double rate = Math.Min(900, 180 * Math.Sqrt(j.MassFlow * 1000));
                // Enough puffs that a fast vessel leaves an unbroken trail (spacing under half a puff).
                double puff = hot ? Math.Max(0.04, de * 4) : de * 2;
                double count = Math.Min(260, Math.Max(rate * dt, srcV.Length * dt / (0.45 * puff)));
                int k = (int)count + (_plumeRnd.NextDouble() < count - (int)count ? 1 : 0);
                double ve = Math.Max(1, j.ExitVelocity);
                // Smoke is the condensed share of the products (K2CO3 of sugar motors, Al2O3 of composites): its flow sets how thick it looks.
                double smokeOpacity = hot ? Math.Min(1, 0.12 + 0.75 * Math.Sqrt(Math.Max(0, j.Smoke) * 1000 / 25)) : 0.22;
                for (int i = 0; i < k; i++)
                {
                    double t = PR(0, dt);   // spread the births over the frame
                    var jitter = new Vector3D(PR(-1, 1), PR(-1, 1), PR(-1, 1)) * (0.06 * ve);
                    var p = new PlumeParticle { Kind = kind, Air = new Vector3D(0, 0, hot ? 0.35 : 0) };
                    if (kind == 2)
                    {
                        p.Vel = srcV + dir * (ve * PR(0.85, 1.0)) + jitter * 0.3;
                        p.Life = PR(0.8, 1.5);
                        p.Size0 = de * 0.8;
                        p.Size1 = de * 2.2;
                        p.Tau = 0.6;
                        p.Opacity = 0.9;
                    }
                    else
                    {
                        p.Vel = srcV + dir * (ve * PR(0.6, 1.0)) + jitter;
                        double stop = hot ? flame * PR(1.4, 3.0) : de * PR(8, 20);   // the jet mixes out within a few flame lengths
                        p.Tau = stop / Math.Max(1, (p.Vel - p.Air).Length);
                        p.Life = hot ? PR(1.6, 3.6) : PR(0.4, 0.9);
                        p.Size0 = puff * PR(0.8, 1.2);
                        p.Size1 = puff * PR(4, 7);
                        p.Opacity = smokeOpacity;
                    }

                    // Born where the nozzle was t seconds ago (it has moved on since), then aged by t.
                    p.Pos = at - srcV * t + dir * PR(0, 0.2 * Math.Max(flame, de));
                    Advance(p, t);
                    _plume.Add(p);

                    if (hot && _plumeRnd.NextDouble() < 0.5)
                    {
                        // Glowing gas inside the flame.
                        var f = new PlumeParticle { Kind = 1, Air = srcV, Vel = srcV + dir * (ve * PR(0.5, 1.0)), Life = PR(0.02, 0.06), Size0 = de * PR(1.4, 2.2), Size1 = de * 0.6, Opacity = 0.9 };
                        f.Tau = flame * PR(0.5, 1.0) / Math.Max(1, ve);
                        f.Pos = at;
                        _plume.Add(f);
                    }
                }
            }

            var group = new Model3DGroup();
            group.Children.Add(flames);
            AddSprites(group);
            group.Freeze();
            _plumeVisual.Content = group;
            if (_view.Children.Count == 0 || !ReferenceEquals(_view.Children[_view.Children.Count - 1], _plumeVisual))
            {
                _view.Children.Remove(_plumeVisual);
                _view.Children.Add(_plumeVisual);   // translucent: drawn after the opaque scene
            }
        }

        /// <summary>Exact relaxation of the particle's velocity to the air's (time constant τ), plus gravity for water.</summary>
        private static void Advance(PlumeParticle p, double dt)
        {
            p.Age += dt;
            double e = Math.Exp(-dt / Math.Max(1e-5, p.Tau));
            var rel = p.Vel - p.Air;
            p.Pos += p.Air * dt + rel * (p.Tau * (1 - e));
            p.Vel = p.Air + rel * e;
            if (p.Kind == 2)
            {
                p.Vel += new Vector3D(0, 0, -9.81 * dt);
                p.Pos += new Vector3D(0, 0, -0.5 * 9.81 * dt * dt);
            }
        }

        /// <summary>Camera-facing sprites sorted far to near (so the translucent ones blend correctly), one mesh per kind.</summary>
        private void AddSprites(Model3DGroup group)
        {
            if (_plume.Count == 0) return;
            var look = _camera.LookDirection;
            look.Normalize();
            var right = Vector3D.CrossProduct(look, _camera.UpDirection);
            if (right.LengthSquared < 1e-12) right = new Vector3D(1, 0, 0);
            right.Normalize();
            var up = Vector3D.CrossProduct(right, look);
            var eye = (Vector3D)_camera.Position;
            _plume.Sort((a, b) => Vector3D.DotProduct(b.Pos - eye, look).CompareTo(Vector3D.DotProduct(a.Pos - eye, look)));
            var meshes = new[] { new MeshGeometry3D(), new MeshGeometry3D(), new MeshGeometry3D() };
            var normal = -look;
            foreach (var p in _plume)
            {
                double life = Math.Min(1, p.Age / Math.Max(1e-6, p.Life));
                // Smoke spreads like a jet at first, then slowly as it drifts; it fades in over a few ms and out with age.
                double size = p.Kind == 0 ? p.Size0 + (p.Size1 - p.Size0) * (0.6 * (1 - Math.Exp(-p.Age / 0.2)) + 0.4 * life) : p.Size0 + (p.Size1 - p.Size0) * life;
                double opacity = p.Opacity * (p.Kind == 0 ? (1 - life) * Math.Min(1, 0.45 + p.Age / 0.03) : 1 - life * 0.7);
                int col = Math.Max(0, Math.Min(AtlasColumns - 1, (int)((1 - opacity) * AtlasColumns)));
                if (opacity < 0.02) continue;
                var m = meshes[p.Kind];
                var c = new Point3D(p.Pos.X, p.Pos.Y, p.Pos.Z);
                var r = right * (size / 2);
                var u = up * (size / 2);
                int i0 = m.Positions.Count;
                m.Positions.Add(c - r - u);
                m.Positions.Add(c + r - u);
                m.Positions.Add(c + r + u);
                m.Positions.Add(c - r + u);
                for (int j = 0; j < 4; j++) m.Normals.Add(normal);
                double u0 = (col + 0.04) / AtlasColumns, u1 = (col + 0.96) / AtlasColumns, v0 = (p.Kind + 0.04) / AtlasRows, v1 = (p.Kind + 0.96) / AtlasRows;
                m.TextureCoordinates.Add(new Point(u0, v1));
                m.TextureCoordinates.Add(new Point(u1, v1));
                m.TextureCoordinates.Add(new Point(u1, v0));
                m.TextureCoordinates.Add(new Point(u0, v0));
                m.TriangleIndices.Add(i0);
                m.TriangleIndices.Add(i0 + 1);
                m.TriangleIndices.Add(i0 + 2);
                m.TriangleIndices.Add(i0);
                m.TriangleIndices.Add(i0 + 2);
                m.TriangleIndices.Add(i0 + 3);
            }

            var mats = new[] { _smokeMat!, _fireMat!, _waterMat! };
            for (int k = 0; k < 3; k++)
            {
                if (meshes[k].TriangleIndices.Count == 0) continue;
                meshes[k].Freeze();
                group.Children.Add(new GeometryModel3D(meshes[k], mats[k]) { BackMaterial = mats[k] });
            }
        }

        /// <summary>A flickering flame: a pale core inside a pinkish-orange envelope, both lathed about the jet axis.</summary>
        private Model3DGroup FlameModel(Vector3D at, Vector3D dir, double re, double length)
        {
            var g = new Model3DGroup();
            double flicker = PR(0.85, 1.15);
            // Drawn inside out: the white-hot core near the nozzle, the yellow-orange body, the faint pink afterburning envelope.
            g.Children.Add(Lathe(at, dir, re, length * 0.32 * PR(0.9, 1.1), new[] { (0.0, 0.75), (0.25, 0.8), (0.6, 0.45), (1.0, 0.02) }, _flameCore!));
            g.Children.Add(Lathe(at, dir, re, length * 0.7 * flicker, new[] { (0.0, 0.95), (0.15, 1.25), (0.45, 1.05), (0.8, 0.5), (1.0, 0.04) }, _flameMid!));
            g.Children.Add(Lathe(at, dir, re, length * flicker, new[] { (0.0, 1.05), (0.12, 1.7), (0.4, 1.8), (0.75, 1.1), (1.0, 0.05) }, _flameOuter!));
            return g;
        }

        private static GeometryModel3D Lathe(Vector3D at, Vector3D dir, double re, double length, (double Z, double R)[] profile, Material mat)
        {
            var helper = Math.Abs(dir.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            var u = Vector3D.CrossProduct(dir, helper);
            u.Normalize();
            var v = Vector3D.CrossProduct(dir, u);
            const int seg = 16;
            var m = new MeshGeometry3D();
            foreach (var (z, r) in profile)
                for (int s = 0; s < seg; s++)
                {
                    double a = 2 * Math.PI * s / seg;
                    var radial = u * Math.Cos(a) + v * Math.Sin(a);
                    var p = at + dir * (z * length) + radial * (r * re);
                    m.Positions.Add(new Point3D(p.X, p.Y, p.Z));
                    m.Normals.Add(radial);
                }

            for (int k = 0; k + 1 < profile.Length; k++)
                for (int s = 0; s < seg; s++)
                {
                    int a0 = k * seg + s, a1 = k * seg + (s + 1) % seg, b0 = a0 + seg, b1 = a1 + seg;
                    m.TriangleIndices.Add(a0);
                    m.TriangleIndices.Add(b0);
                    m.TriangleIndices.Add(b1);
                    m.TriangleIndices.Add(a0);
                    m.TriangleIndices.Add(b1);
                    m.TriangleIndices.Add(a1);
                }

            m.Freeze();
            return new GeometryModel3D(m, mat) { BackMaterial = mat };
        }

        /// <summary>Advances the plume to the simulation's current time (stepping a paused world, snapshots).</summary>
        public void PlumeTick() => PlumeFrame();

        /// <summary>Removes every plume particle and flame (the simulation stopped or restarted).</summary>
        public void ClearPlume()
        {
            _plume.Clear();
            _plumeLast.Clear();
            _plumeClock = double.NaN;
            _plumeVisual.Content = null;
        }
    }
}
