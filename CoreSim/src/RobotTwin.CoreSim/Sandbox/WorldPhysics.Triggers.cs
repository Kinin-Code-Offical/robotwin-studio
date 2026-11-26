using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [trigger] Unity's trigger collider: a box ("trigger" = "1") that nothing bumps into but that knows what is inside
    /// it: a part enters when its bounding sphere reaches the box, leaves when it no longer does. A zone fixed on a part
    /// (a gripper's reach, a conveyor's end) rides with it. Entering and leaving go to the run's log; what is inside can
    /// be read at any moment (<see cref="TriggerOccupants"/>).
    /// </summary>
    public sealed partial class WorldPhysics
    {
        public const string TriggerKey = "trigger";

        private sealed class TriggerZone
        {
            public WorldObject Object = null!;
            public Vec3 Centre;           // world, when standing alone
            public Quat Orientation;
            public Vec3 Half;
            public Item? Carrier;         // what it is fixed on
            public Vec3 LocalCentre;      // in the carrier's frame
            public Quat LocalTurn;
            public readonly HashSet<string> Inside = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private List<TriggerZone>? _triggers;

        public static bool IsTrigger(WorldObject o) =>
            o.Kind == WorldObjectKind.Body && o.Parameters.TryGetValue(TriggerKey, out var s) && (s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase));

        private void BuildTriggers(WorldDocument world, Func<WorldObject, WorldBodyGeometry> geometry)
        {
            _triggers = null;
            foreach (var o in world.Objects.Where(IsTrigger))
            {
                var g = geometry(o);
                var q = o.Pose.Orientation;
                var zone = new TriggerZone
                {
                    Object = o,
                    Centre = o.Pose.Position + q.Rotate((g.Min + g.Max) * 0.5),
                    Orientation = q,
                    Half = new Vec3(Math.Max(1e-3, (g.Max.X - g.Min.X) / 2), Math.Max(1e-3, (g.Max.Y - g.Min.Y) / 2), Math.Max(1e-3, (g.Max.Z - g.Min.Z) / 2))
                };
                var mount = world.Joints.FirstOrDefault(j => string.Equals(j.ObjectA, o.Id, StringComparison.OrdinalIgnoreCase) && j.ObjectB != null);
                if (mount != null && _items.TryGetValue(mount.ObjectB!, out var carrier) && carrier.Body != null)
                {
                    var inv = carrier.Body.Orientation.Conjugate;
                    zone.Carrier = carrier;
                    zone.LocalCentre = inv.Rotate(zone.Centre - carrier.Body.Position);
                    zone.LocalTurn = (inv * q).Normalized();
                }

                (_triggers ??= new List<TriggerZone>()).Add(zone);
            }
        }

        private void StepTriggers()
        {
            if (_triggers == null) return;
            foreach (var z in _triggers)
            {
                Vec3 centre = z.Centre;
                Quat turn = z.Orientation;
                if (z.Carrier != null)
                {
                    var cb = z.Carrier.Body;
                    centre = cb.Position + cb.Orientation.Rotate(z.LocalCentre);
                    turn = cb.Orientation * z.LocalTurn;
                }

                var back = turn.Conjugate;
                foreach (var it in _items.Values)
                {
                    if (it.Body == null || ReferenceEquals(it, z.Carrier)) continue;
                    var body = it.Body;
                    double r = body.EffectiveShape?.BoundingRadius ?? 0.01;
                    var local = back.Rotate(body.Position - centre);
                    var nearest = new Vec3(Math.Max(-z.Half.X, Math.Min(z.Half.X, local.X)), Math.Max(-z.Half.Y, Math.Min(z.Half.Y, local.Y)), Math.Max(-z.Half.Z, Math.Min(z.Half.Z, local.Z)));
                    bool inside = (local - nearest).Length <= r;
                    string id = it.Object.Id;
                    if (inside && z.Inside.Add(id))
                        Scene.Report(new PhysicsEvent(Scene.Time, z.Object.Id, PhysicsEventKind.TriggerEnter, $"{it.Object.Name} girdi", 0, false));
                    else if (!inside && z.Inside.Remove(id))
                        Scene.Report(new PhysicsEvent(Scene.Time, z.Object.Id, PhysicsEventKind.TriggerExit, $"{it.Object.Name} çıktı", 0, false));
                }
            }
        }

        /// <summary>The ids of the parts inside a trigger zone now (empty for an unknown one).</summary>
        public IReadOnlyCollection<string> TriggerOccupants(string triggerId) =>
            (IReadOnlyCollection<string>?)_triggers?.FirstOrDefault(z => string.Equals(z.Object.Id, triggerId, StringComparison.OrdinalIgnoreCase))?.Inside ?? Array.Empty<string>();
    }
}
