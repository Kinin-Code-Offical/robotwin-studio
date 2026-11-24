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
        /// <summary>[perf] Relaxes every cable by part of a <paramref name="total"/>-step relax (see <see cref="CableRope.RelaxPart"/>).</summary>
        public void RelaxCables(int from, int count, int total) =>
            Settle(_cables.Select(l => l.Rope).ToList(), rope => rope.RelaxPart(Scene, from, count, total));

        /// <summary>
        /// [perf] Settles ropes side by side on the processor's cores. While settling a rope only reads the scene (it
        /// pushes on no body) and never touches another rope, so each comes out exactly as it would alone: 20 cables in
        /// the PS5 plane's fuselage took 7.8 s one after another, held on the thread that pressed Run.
        /// </summary>
        private void Settle(List<CableRope> ropes, Action<CableRope> relax)
        {
            if (ropes.Count == 0) return;
            if (ropes.Count == 1)
            {
                relax(ropes[0]);
                return;
            }

            foreach (var b in Scene.Bodies) _ = b.EffectiveShape;   // built once here, not raced for by the workers
            System.Threading.Tasks.Parallel.ForEach(ropes, relax);
        }

        /// <summary>[perf] Time the last <see cref="AttachCables"/> spent (ms): exits, routes, laying, relaxing.</summary>
        public (double Exits, double Routes, double Laying, double Relaxing) LastCableTimes { get; private set; }

        public void AttachCables(WorldDocument world, Func<WorldCable, CablePins?> pins, int relaxSteps = 300, Func<string, IReadOnlyList<Vec3>?>? seed = null)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            double exits = 0, routes = 0, laying = 0, relaxing = 0;
            double Lap()
            {
                double ms = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                return ms;
            }

            _cables.Clear();
            var settle = new List<(CableRope Rope, int Steps)>();
            ShortCables.Clear();
            BlockedCables.Clear();
            _routes.Clear();
            // Propeller discs are swept volumes, not solids; wires rest on everything else.
            var ignore = new HashSet<SceneBody>();
            foreach (var pm in _props) ignore.Add(pm.It.Body);
            // The plugs and the way each wire leaves its pin, in turn (each looks at the parts around its own pin).
            var wires = new List<(WorldCable Cable, Item A, Item B, RopeEnd EndA, RopeEnd EndB)>();
            foreach (var cable in world.Cables)
            {
                if (!_items.TryGetValue(cable.A.ObjectId, out var ia) || !_items.TryGetValue(cable.B.ObjectId, out var ib)) continue;
                if (pins(cable) is not { } p) continue;
                var endA = MakeEnd(ia, p.A);
                var endB = MakeEnd(ib, p.B);
                FreeExit(endA, ia, ignore, CableRope.RadiusOf(cable.Awg));
                FreeExit(endB, ib, ignore, CableRope.RadiusOf(cable.Awg));
                wires.Add((cable, ia, ib, endA, endB));
            }

            exits += Lap();
            // [perf] [route] Where each wire can really run, around the parts and through openings, never through a solid:
            // the searches only read the scene, so they run side by side (20 routes took 630 ms one after another).
            var found = new List<Vec3>?[wires.Count];
            if (wires.Count > 1)
            {
                foreach (var b in Scene.Bodies) _ = b.EffectiveShape;   // built once here, not raced for by the workers
                System.Threading.Tasks.Parallel.For(0, wires.Count, i => found[i] = RouteOf(wires[i].EndA, wires[i].EndB, wires[i].A.Body, wires[i].B.Body, wires[i].Cable.Awg, ignore));
            }
            else if (wires.Count == 1)
            {
                found[0] = RouteOf(wires[0].EndA, wires[0].EndB, wires[0].A.Body, wires[0].B.Body, wires[0].Cable.Awg, ignore);
            }

            routes += Lap();
            for (int w = 0; w < wires.Count; w++)
            {
                var (cable, ia, ib, endA, endB) = wires[w];
                double span = (endA.World - endB.World).Length;
                var route = found[w];
                double run = route != null ? CableRouter.Length(route) + 2 * RouteLead : span;
                if (route == null) BlockedCables.Add(cable.Id);
                _routes[cable.Id] = route;
                double length = cable.Length > 0 ? cable.Length : CableRope.DefaultLength(Math.Max(span, run));
                // A wire shorter than its way between the pins could not be plugged in: it starts stretched and pulls the parts together.
                if (cable.Length > 0 && cable.Length < Math.Max(span, run)) ShortCables.Add((cable.Id, Math.Max(span, run), cable.Length));
                var rope = new CableRope(cable.Id, endA, endB, length, cable.Awg);
                rope.Ignore = ignore;
                var rigid = WeldedSet(ia.Body);   // [weld] both plugs on one rigid body: its forces on that body are internal
                if (rigid.Contains(ib.Body)) rope.InternalTo = rigid;
                var link = new CableLink { Cable = cable, Rope = rope };
                rope.Unplugged += (end, force) => OnUnplugged(link, end, force);
                if (seed?.Invoke(cable.Id) is { Count: >= 2 } previous) rope.Lay(previous);
                else rope.LayClear(Scene);   // its slack bowed the way with the fewest parts in it
                // A shape through a solid (a bow through a fuselage wall, an earlier shape a part was moved onto) is not
                // where a wire can be: it is laid along the route instead.
                if (route != null && rope.Crossings(Scene).Count > 0) rope.LayAlong(route.GetRange(1, route.Count - 2));
                laying += Lap();
                // Settled in full even from the editor's settled shape: a short warm-up let a wire kick its parts at the
                // start of a run (the glass-pouring servo's world never drew its next frame).
                if (relaxSteps > 0) settle.Add((rope, relaxSteps));
                _cables.Add(link);
            }

            Lap();
            var bySteps = settle.ToDictionary(x => x.Rope, x => x.Steps);
            Settle(settle.Select(x => x.Rope).ToList(), rope => rope.Relax(Scene, bySteps[rope]));
            relaxing += Lap();
            LastCableTimes = (exits, routes, laying, relaxing);
        }

        private readonly Dictionary<string, List<Vec3>?> _routes = new Dictionary<string, List<Vec3>?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>[route] The way found for a cable between its plugs (world points), null when it has none.</summary>
        public IReadOnlyList<Vec3>? RouteOf(string cableId) => _routes.TryGetValue(cableId, out var r) ? r : null;

        /// <summary>How far the plugged end of a wire leaves its pin straight before it can turn (m).</summary>
        private const double RouteLead = 0.012;

        /// <summary>
        /// [route] A wire leaves its pin straight out along the part's up axis, unless another part is right there (a
        /// motor's terminals pressed up against the plate it hangs under): then the first clear direction of the part's
        /// frame, sideways before downwards, as a wire really bends off the terminal.
        /// </summary>
        private void FreeExit(RopeEnd end, Item it, HashSet<SceneBody> ignore, double radius)
        {
            var pin = end.World;
            var q = it.Body.Orientation;
            var up = end.ExitWorld.Normalized();
            var frame = PoseOf(it.Object.Id)?.Orientation ?? q;
            var candidates = new[]
            {
                up, frame.Rotate(Vec3.UnitX), frame.Rotate(-Vec3.UnitX), frame.Rotate(Vec3.UnitY), frame.Rotate(-Vec3.UnitY), -up
            };
            for (int k = 0; k < candidates.Length; k++)
            {
                if (ExitBlocked(pin, candidates[k], it.Body, ignore, radius)) continue;
                if (k > 0) end.Exit = q.InverseRotate(candidates[k]);
                return;
            }
        }

        private bool ExitBlocked(Vec3 pin, Vec3 dir, SceneBody own, HashSet<SceneBody> ignore, double radius)
        {
            var tip = pin + dir * RouteLead;
            if (Scene.GroundEnabled && tip.Z < Scene.GroundHeight(tip.X, tip.Y) + radius) return true;
            foreach (var b in Scene.Bodies)
            {
                if (ReferenceEquals(b, own) || ignore.Contains(b) || !b.Collides || b.EffectiveShape is not { HasSolid: true } shape) continue;
                if (shape.Kind != ShapeKind.Mesh && (tip - b.Position).Length > shape.BoundingRadius + RouteLead + radius) continue;
                var lo = b.Orientation.InverseRotate(pin + dir * 0.001 - b.Position);
                var ld = b.Orientation.InverseRotate(dir);
                if (b.RaycastSolid(shape, lo, ld, RouteLead, out _, out _)) return true;
                var lt = b.Orientation.InverseRotate(tip - b.Position);
                if (b.SignedDistanceSolid(shape, lt, radius, out double d, out _) && d < radius) return true;
            }

            return false;
        }

        /// <summary>[route] Path a wire between two plugs can take through free space (null when there is none).</summary>
        private List<Vec3>? RouteOf(RopeEnd a, RopeEnd b, SceneBody bodyA, SceneBody bodyB, double awg, HashSet<SceneBody> ignore)
        {
            var pinA = a.World;
            var pinB = b.World;
            var from = pinA + a.ExitWorld.Normalized() * RouteLead;
            var to = pinB + b.ExitWorld.Normalized() * RouteLead;
            const double housing = 0.025;   // the plug and its housing sit in the part around the pin
            bool Passable(SceneBody body, Vec3 x) =>
                ignore.Contains(body)
                || (ReferenceEquals(body, bodyA) && (x - pinA).Length < housing)
                || (ReferenceEquals(body, bodyB) && (x - pinB).Length < housing);
            return CableRouter.Route(Scene, from, to, CableRope.RadiusOf(awg), Passable);
        }

        /// <summary>Cables whose set length is shorter than their way between the pins (id, that way, length; m).</summary>
        public List<(string CableId, double Span, double Length)> ShortCables { get; } = new List<(string, double, double)>();

        /// <summary>
        /// [route] Cables that have no way between their plugs except through a solid: a wire into a closed box with no
        /// cable hole. They are laid through it, and reported, since a real one could not be.
        /// </summary>
        public List<string> BlockedCables { get; } = new List<string>();

        /// <summary>Bodies welded rigidly to <paramref name="body"/> (fixed, unbreakable joints), itself included.</summary>
        private HashSet<SceneBody> WeldedSet(SceneBody body)
        {
            var set = new HashSet<SceneBody> { body };
            var queue = new Queue<SceneBody>();
            queue.Enqueue(body);
            while (queue.Count > 0)
            {
                var b = queue.Dequeue();
                foreach (var j in Scene.Joints)
                {
                    if (!j.Weld || !j.Enabled || j.Broken || j.ParentBody == null) continue;
                    var other = ReferenceEquals(j.ChildBody, b) ? j.ParentBody : ReferenceEquals(j.ParentBody, b) ? j.ChildBody : null;
                    if (other != null && set.Add(other)) queue.Enqueue(other);
                }
            }

            return set;
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
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (var c in _cables)
            {
                c.Rope.Step(Scene, dt, true);
                UpdateContacts(c);
            }

            CableSeconds += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) / (double)System.Diagnostics.Stopwatch.Frequency;
        }

        /// <summary>Wall-clock time spent stepping the cables so far (s), for the performance overlay and checks.</summary>
        public double CableSeconds { get; private set; }

        /// <summary>
        /// A plug under pull sits less firmly in its socket: its contact resistance rises with the
        /// rope tension (relative to the pull-out force) until it comes out and the circuit opens.
        /// </summary>
        private void UpdateContacts(CableLink c)
        {
            if (_circuit == null || !_circuit.CableDevices.TryGetValue(c.Cable.Id, out var wire)) return;
            var rope = c.Rope;
            if (!rope.A.Attached || !rope.B.Attached) return;
            double pullOut = Math.Max(1e-6, rope.PullOutForce);
            double r = WorldCable.Resistance(c.Cable.Awg, rope.Length)
                + c.Cable.EndContact(true, rope.A.Tension / pullOut)
                + c.Cable.EndContact(false, rope.B.Tension / pullOut);
            if (Math.Abs(r - wire.Resistance) > 1e-6) wire.Resistance = r;
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
                if (it.Walls != null)   // [shell] walls ride along with the floor
                {
                    foreach (var wall in it.Walls)
                    {
                        var rw = oldQ.Conjugate * wall.Orientation;
                        var ow = oldQ.InverseRotate(wall.Position - oldX);
                        wall.Orientation = it.Body.Orientation * rw;
                        wall.Position = it.Body.Position + it.Body.Orientation.Rotate(ow);
                    }
                }

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
