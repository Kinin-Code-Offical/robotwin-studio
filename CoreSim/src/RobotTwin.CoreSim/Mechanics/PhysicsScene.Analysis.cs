using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>[analysis] One contact of the last physics substep: where, between whom, and the force on <see cref="A"/>.</summary>
    public readonly struct ContactSample
    {
        public ContactSample(SceneBody a, SceneBody? b, Vec3 point, Vec3 normal, Vec3 forceOnA)
        {
            A = a;
            B = b;
            Point = point;
            Normal = normal;
            ForceOnA = forceOnA;
        }

        public SceneBody A { get; }

        /// <summary>The other body, or null for the ground.</summary>
        public SceneBody? B { get; }

        /// <summary>World contact point (m).</summary>
        public Vec3 Point { get; }

        public Vec3 Normal { get; }

        /// <summary>Contact force on A (normal + friction, N, world frame); B feels the opposite.</summary>
        public Vec3 ForceOnA { get; }
    }

    /// <summary>
    /// [analysis] Read-only access to the contact set for the offline analysis recorder (structural
    /// loads per part). Forces are the last substep's impulses divided by its length.
    /// </summary>
    public sealed partial class PhysicsScene
    {
        /// <summary>Appends the contacts of the last substep to <paramref name="into"/>.</summary>
        public void CopyContacts(List<ContactSample> into)
        {
            if (_h <= 0) return;
            for (int i = 0; i < _contactCount; i++)
            {
                ref var c = ref _contacts[i];
                if (c.A < 0 || c.A >= Bodies.Count) continue;
                var p = c.N * c.Pn + c.T1 * c.Pt1 + c.T2 * c.Pt2;
                if (p.LengthSquared == 0) continue;
                var a = Bodies[c.A];
                var b = c.B >= 0 && c.B < Bodies.Count ? Bodies[c.B] : null;
                into.Add(new ContactSample(a, b, _state[c.SA].X + c.RA, c.N, p / _h));   // [weld] RA is from the solver body's centre
            }
        }
    }
}
