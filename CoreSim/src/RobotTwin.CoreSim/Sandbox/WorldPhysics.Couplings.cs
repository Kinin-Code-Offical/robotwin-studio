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
                _couplings[c.Id] = coupling;
                // The gear blanks overlap where their teeth mesh, a nut surrounds its screw: the coupling is their contact.
                Scene.NoCollide.Add((a.ChildBody, b.ChildBody));
            }
        }

        /// <summary>
        /// A coupling's load on its driven joint (N·m or N) and both joints' speeds (rad/s or m/s) while running; null when it
        /// was not built (a joint is missing, or the coupling does not fit them).
        /// </summary>
        public (double Load, double SpeedA, double SpeedB)? CouplingState(string id) =>
            _couplings.TryGetValue(id, out var c) ? (c.Load, c.A.Speed, c.B.Speed) : ((double, double, double)?)null;
    }
}
