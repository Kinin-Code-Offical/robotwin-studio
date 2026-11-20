using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>Pin positions of a cable's two plugs, each in its own object's frame (metres).</summary>
    public readonly struct CablePins
    {
        public CablePins(Vec3 a, Vec3 b)
        {
            A = a;
            B = b;
        }

        public Vec3 A { get; }
        public Vec3 B { get; }
    }

    // [phys] Cables as physical ropes, the floor's material and grip, sleeping.
    public sealed partial class WorldPhysics
    {
        private sealed class CableLink
        {
            public WorldCable Cable = null!;
            public CableRope Rope = null!;
        }

        private readonly List<CableLink> _cables = new List<CableLink>();

        /// <summary>Ropes of the world's cables (after <see cref="AttachCables"/>).</summary>
        public IEnumerable<CableRope> Ropes => _cables.Select(c => c.Rope);

        /// <summary>
        /// Floor: the material pair of every part against the floor material (wood unless the
        /// environment names one) scaled to the environment's tyre grip μ, so a rubber wheel grips
        /// with μ while a PLA chassis or a steel caster slides as those materials do. Sleeping on.
        /// </summary>
        private void ConfigureGround(WorldSpec env)
        {
            Scene.GroundFriction = null;
            Scene.GroundMaterial = string.IsNullOrWhiteSpace(env.FloorMaterial) ? "wood" : env.FloorMaterial!;
            Scene.GroundGrip = env.Friction > 0 ? env.Friction : (double?)null;
            Scene.Regions.Clear();
            foreach (var r in env.Regions) Scene.Regions.Add(new SurfaceRegion(r.X, r.Y, r.SizeX, r.SizeY, r.Material));
            if (env.AirDensity is double rho) Scene.AirDensity = rho;
            Scene.Wind = new Vec3(env.WindX, env.WindY, env.WindZ);
            Scene.GustIntensity = env.Gust;
            Scene.SleepEnabled = true;
        }

        /// <summary>
        /// Gives every cable a rope with its real length (or the distance between its pins plus
        /// slack), pinned to the bodies its plugs sit in, and lets it settle over whatever lies
        /// below. <paramref name="pins"/> returns the plug positions in the objects' frames;
        /// <paramref name="seed"/> may give an earlier shape of a cable (by id) to start from.
        /// </summary>
        public void AttachCables(WorldDocument world, Func<WorldCable, CablePins?> pins, int relaxSteps = 300, Func<string, IReadOnlyList<Vec3>?>? seed = null)
        {
            _cables.Clear();
            foreach (var cable in world.Cables)
            {
                if (!_items.TryGetValue(cable.A.ObjectId, out var ia) || !_items.TryGetValue(cable.B.ObjectId, out var ib)) continue;
                if (pins(cable) is not { } p) continue;
                var endA = MakeEnd(ia, p.A);
                var endB = MakeEnd(ib, p.B);
                double span = (endA.World - endB.World).Length;
                double length = cable.Length > 0 ? cable.Length : CableRope.DefaultLength(span);
                var rope = new CableRope(cable.Id, endA, endB, length, cable.Awg);
                var link = new CableLink { Cable = cable, Rope = rope };
                rope.Unplugged += (end, force) => OnUnplugged(link, end, force);
                if (seed?.Invoke(cable.Id) is { Count: >= 2 } previous) rope.Lay(previous);
                if (relaxSteps > 0) rope.Relax(Scene, relaxSteps);
                _cables.Add(link);
            }
        }

        private RopeEnd MakeEnd(Item it, Vec3 objectLocal)
        {
            var pose = PoseOf(it.Object.Id)!.Value;
            var world = pose.Position + pose.Orientation.Rotate(objectLocal);
            var body = it.Body;
            return new RopeEnd
            {
                Body = body,
                Local = body.Orientation.InverseRotate(world - body.Position),
                // Plugs leave their pins along the object's up axis (pin headers, terminal blocks).
                Exit = body.Orientation.InverseRotate(pose.Orientation.Rotate(Vec3.UnitZ))
            };
        }

        /// <summary>
        /// A sphere fixed under a chassis is a ball caster: the ball sits in a socket and rolls in
        /// every direction (steel ball on the floor: rolling resistance only), it is not welded.
        /// </summary>
        private static JointKind CasterKind(JointKind kind, Item child, SceneBody? parent) =>
            kind == JointKind.Fixed && parent != null && child.Object.Kind == WorldObjectKind.Body && child.Object.Shape == "sphere"
                ? JointKind.Ball
                : kind;

        private void StepCables(double dt)
        {
            foreach (var c in _cables) c.Rope.Step(Scene, dt, true);
        }

        private void OnUnplugged(CableLink link, RopeEnd end, double force)
        {
            var cable = link.Cable;
            bool isA = ReferenceEquals(end, link.Rope.A);
            var at = isA ? cable.A : cable.B;
            // The contact is gone: the conductor no longer joins the two nets.
            if (_circuit != null && _circuit.CableDevices.TryGetValue(cable.Id, out var wire)) wire.Resistance = 1e12;
            string message = force > 0
                ? $"{at.Pin} pinindeki kablo ({cable.Name}) {force:F1} N ile gerilip yuvasından çıktı; devre açıldı"
                : $"{at.Pin} pinindeki kablo ({cable.Name}) yuvasından çıktı; devre açıldı";
            Scene.Report(new PhysicsEvent(Scene.Time, at.ObjectId, PhysicsEventKind.Detached, message, force, true));
        }

        /// <summary>Points of a cable's rope from end A to end B (world), or null when it has none.</summary>
        public IReadOnlyList<Vec3>? CablePoints(string cableId)
        {
            foreach (var c in _cables)
                if (string.Equals(c.Cable.Id, cableId, StringComparison.OrdinalIgnoreCase))
                    return c.Rope.Points;
            return null;
        }

        /// <summary>The rope of a cable, or null.</summary>
        public CableRope? RopeOf(string cableId)
        {
            foreach (var c in _cables)
                if (string.Equals(c.Cable.Id, cableId, StringComparison.OrdinalIgnoreCase))
                    return c.Rope;
            return null;
        }

        /// <summary>Whether each plug of a cable is still in its pin (true, true when the cable has no rope).</summary>
        public (bool A, bool B) PlugState(string cableId) =>
            RopeOf(cableId) is { } r ? (r.A.Attached, r.B.Attached) : (true, true);

        /// <summary>
        /// Puts every body back at its object's document pose (editor: parts dragged around while
        /// not simulating) without rebuilding the scene.
        /// </summary>
        public void SyncPoses()
        {
            foreach (var it in _items.Values)
            {
                var q = it.Object.Pose.Orientation;
                var oldQ = it.Body.Orientation;
                var oldX = it.Body.Position;
                it.Body.Orientation = q * it.ShapeTurn;
                it.Body.Position = it.Object.Pose.Position + q.Rotate(it.Centre);
                it.Body.Velocity = Vec3.Zero;
                it.Body.AngularVelocity = Vec3.Zero;
                if (it.Rotor != null)
                {
                    // The rotor rides along rigidly with its body.
                    var rel = oldQ.Conjugate * it.Rotor.Orientation;
                    var off = oldQ.InverseRotate(it.Rotor.Position - oldX);
                    it.Rotor.Orientation = it.Body.Orientation * rel;
                    it.Rotor.Position = it.Body.Position + it.Body.Orientation.Rotate(off);
                }
            }
        }
    }
}
