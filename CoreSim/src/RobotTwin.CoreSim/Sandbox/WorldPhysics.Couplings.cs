using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>[couple] The world's gears, belts and lead screws as constraints between its joints (Sandbox/WorldCoupling.cs).</summary>
    public sealed partial class WorldPhysics
    {
        private readonly Dictionary<string, SceneCoupling> _couplings = new Dictionary<string, SceneCoupling>(StringComparer.Ordinal);

        private void CoupleJoints(WorldDocument world)
        {
            if (world.Couplings == null) return;
            foreach (var c in world.Couplings)
            {
                var wa = world.Joints.FirstOrDefault(j => j.Id == c.JointA);
                var wb = world.Joints.FirstOrDefault(j => j.Id == c.JointB);
                if (wa == null || wb == null || ReferenceEquals(wa, wb) || c.Ratio(wa.Kind, wb.Kind) is not { } ratio) continue;
                var a = Scene.Joints.FirstOrDefault(s => s.Id == wa.Id);
                var b = Scene.Joints.FirstOrDefault(s => s.Id == wb.Id);
                if (a == null || b == null) continue;
                var coupling = Scene.AddCoupling(a, b, ratio, c.Id);
                coupling.Efficiency = c.EfficiencyFor(wa.Kind, wb.Kind);
                bool hingeB = wb.Kind == WorldJointKind.Hinge;
                coupling.Backlash = Math.Max(0, hingeB ? c.Backlash * Math.PI / 180 : c.Backlash / 1000);
                if (c.Kind == WorldCoupling.Belt && c.BeltStiffness > 0)
                {
                    double k = c.BeltStiffness * 1000, r = c.SizeB / 2000;   // N/m along the span; the driven pulley's radius
                    coupling.Stiffness = hingeB && r > 0 ? k * r * r : k;   // at a pulley, N·m/rad
                }
                _couplings[c.Id] = coupling;
                // The gear blanks overlap where their teeth mesh, a nut surrounds its screw: the coupling is their contact.
                // Everything fixed to one side (the gear on its shaft, the nut on its carriage) is clear of the other.
                foreach (var x in Rigid(a.ChildBody))
                    foreach (var y in Rigid(b.ChildBody))
                        if (!ReferenceEquals(x, y)) Scene.NoCollide.Add((x, y));
            }
        }

        /// <summary>The bodies fixed to this one, any number of fixed joints away (itself included).</summary>
        private HashSet<SceneBody> Rigid(SceneBody body)
        {
            var group = new HashSet<SceneBody> { body };
            var queue = new Queue<SceneBody>(group);
            while (queue.Count > 0)
            {
                var b = queue.Dequeue();
                foreach (var j in Scene.Joints)
                {
                    if (j.Kind != JointKind.Fixed || j.B == null) continue;
                    var other = ReferenceEquals(j.A, b) ? j.B : ReferenceEquals(j.B, b) ? j.A : null;
                    if (other != null && !other.IsStatic && group.Add(other)) queue.Enqueue(other);
                }
            }

            return group;
        }

        /// <summary>
        /// A coupling's load on its driven joint (N·m or N) and both joints' speeds (rad/s or m/s) while running; null when it
        /// was not built (a joint is missing, or the coupling does not fit them).
        /// </summary>
        public (double Load, double SpeedA, double SpeedB)? CouplingState(string id) =>
            _couplings.TryGetValue(id, out var c) ? (c.Load, c.A.Speed, c.B.Speed) : ((double, double, double)?)null;
    }
}
