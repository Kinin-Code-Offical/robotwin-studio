using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.World
{
    // [liquid] Liquids drawn as a surface: while the world runs from the live particles, in a replay from the recorded
    // ones; in the editor the object's own volume (ObjectModels) stands for the liquid it will become.
    public sealed partial class WorldScene
    {
        private readonly ModelVisual3D _liquidVisual = new ModelVisual3D();
        private readonly Dictionary<string, Model3D?> _liquidHidden = new Dictionary<string, Model3D?>(StringComparer.OrdinalIgnoreCase);
        private DateTime _liquidBuilt;
        private double _liquidPlaybackTime = double.NaN;
        private System.Threading.Tasks.Task? _liquidTask;
        private int _liquidGeneration;
        private readonly Dictionary<string, System.Windows.Media.Media3D.Material> _liquidLooks = new Dictionary<string, System.Windows.Media.Media3D.Material>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The surface material of a liquid kind, made once (its gradient brushes cost more than the frame's other work).</summary>
        private System.Windows.Media.Media3D.Material LiquidLookOf(LiquidKind kind)
        {
            if (!_liquidLooks.TryGetValue(kind.Name, out var m)) _liquidLooks[kind.Name] = m = LiquidLook.Surface(kind);
            return m;
        }

        /// <summary>Grid cell of the surface as a fraction of the particle spacing (finer for a render).</summary>
        public double LiquidSurfaceCell { get; set; } = 0.5;

        private void LiquidFrame(bool force = false)
        {
            if (World == null) return;
            var sources = new List<(WorldObject Object, LiquidKind Kind, float[] Xyz, double Spacing, float[]? Vel)>();
            var physics = Session != null && Session.State != Simulation.SimState.Stopped ? Session.Physics : null;
            if (Playback is { } pb)
            {
                if (!force && pb.Time == _liquidPlaybackTime) return;
                _liquidPlaybackTime = pb.Time;
                foreach (var o in World.Objects)
                    if (WorldPhysics.IsLiquid(o) && pb.LiquidAt(o.Id) is { } rec)
                        sources.Add((o, LiquidKind.Find(rec.Kind), rec.Positions, rec.Spacing, rec.Velocities));
            }
            else if (physics != null)
            {
                foreach (var l in physics.Liquids) sources.Add((l.Object, l.Body.Kind, l.Body.Snapshot(), l.Body.Spacing, l.Body.VelocitySnapshot()));
            }

            if (sources.Count == 0)
            {
                if (_liquidVisual.Content != null) _liquidVisual.Content = null;
                RestoreLiquidVolumes();
                return;
            }

            // Live: 30 surfaces a second are plenty (the physics runs at 1 kHz).
            var now = DateTime.Now;
            if (!force && Playback == null && (now - _liquidBuilt).TotalMilliseconds < 33) return;
            var eye = _view.Camera is ProjectionCamera cam ? cam.Position : new Point3D(1, 1, 1);
            if (!force && Playback == null)
            {
                // [perf] Live, the surface is meshed on a worker from the particles copied now (11 ms a frame on the UI
                // thread for the water glass); the frame shows the last finished one, and the next starts when it lands.
                if (_liquidTask is { IsCompleted: false }) return;
                _liquidBuilt = now;
                int generation = ++_liquidGeneration;
                double cell = LiquidSurfaceCell;
                foreach (var (o, _, _, _, _) in sources) HideLiquidVolume(o.Id);
                var jobs = sources.Select(x => (x.Kind, x.Xyz, x.Spacing, x.Vel)).ToList();
                _liquidTask = System.Threading.Tasks.Task.Run(() =>
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var meshes = jobs.Select(j => (j.Kind, Mesh: LiquidLook.Mesh(LiquidSurface.Build(j.Xyz, j.Spacing, cell, 2_000_000, j.Vel), 0.55 * j.Spacing, eye))).ToList();
                    double ms = clock.Elapsed.TotalMilliseconds;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Shell.Perf.Add("sıvı yüzeyi (arka)", ms);
                        // A stopped run or a newer surface: this one is dropped.
                        if (generation != _liquidGeneration || Session == null || Session.State == Simulation.SimState.Stopped || Playback != null) return;
                        var live = new Model3DGroup();
                        foreach (var (kind, mesh) in meshes) live.Children.Add(new GeometryModel3D(mesh, LiquidLookOf(kind)));
                        live.Freeze();
                        _liquidVisual.Content = live;
                        OrderLiquid();
                    }));
                });
                return;
            }

            _liquidBuilt = now;
            _liquidGeneration++;   // a surface built here wins over one still on its way
            var group = new Model3DGroup();
            foreach (var (o, kind, xyz, spacing, vel) in sources)
            {
                HideLiquidVolume(o.Id);
                using (Shell.Perf.Measure("sıvı yüzeyi"))
                {
                    var surface = LiquidSurface.Build(xyz, spacing, Playback != null ? (xyz.Length / 3 > 20000 ? 0.5 : 0.33) : LiquidSurfaceCell, Playback != null ? 6_000_000 : 2_000_000, vel);
                    group.Children.Add(new GeometryModel3D(LiquidLook.Mesh(surface, 0.55 * spacing, eye), LiquidLookOf(kind)));
                }
            }

            group.Freeze();
            _liquidVisual.Content = group;
            OrderLiquid();
        }

        private void HideLiquidVolume(string id)
        {
            if (_liquidHidden.ContainsKey(id) || !_items.TryGetValue(id, out var it)) return;
            _liquidHidden[id] = it.Visual.Content;
            it.Visual.Content = null;
        }

        private void RestoreLiquidVolumes()
        {
            if (_liquidHidden.Count == 0) return;
            foreach (var kv in _liquidHidden)
                if (_items.TryGetValue(kv.Key, out var it) && it.Visual.Content == null)
                    it.Visual.Content = kv.Value;
            _liquidHidden.Clear();
        }

        /// <summary>
        /// WPF blends transparent surfaces in drawing order and does not sort them: the liquid is drawn after every opaque
        /// part and before the see-through ones (a glass), so it shows through the glass and the glass in front of it.
        /// </summary>
        private void OrderLiquid()
        {
            if (World == null) return;
            var clear = World.Objects.Where(o => IsSeeThrough(o.Material) && _items.ContainsKey(o.Id)).Select(o => (Visual3D)_items[o.Id].Visual).ToList();
            int at = _view.Children.IndexOf(_liquidVisual);
            if (at >= 0)
            {
                // Already in order: only see-through parts after the liquid, and all of them after it.
                bool ok = true;
                for (int i = at + 1; i < _view.Children.Count && ok; i++) ok = clear.Contains(_view.Children[i]);
                foreach (var v in clear) ok &= _view.Children.IndexOf(v) is var k && (k < 0 || k > at);
                if (ok) return;
                _view.Children.Remove(_liquidVisual);
            }

            foreach (var v in clear)
                if (_view.Children.Contains(v))
                    _view.Children.Remove(v);
            _view.Children.Add(_liquidVisual);
            foreach (var v in clear) _view.Children.Add(v);
        }

        private static bool IsSeeThrough(string? material) => (material ?? string.Empty).ToUpperInvariant() is "CAM" or "GLASS" or "AKRİLİK" or "AKRILIK";
    }
}
