using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// [weld] Parts joined by rigid, unbreakable welds (<see cref="SceneJoint.Weld"/>: glued, screwed for good,
    /// soldered, cut from one piece) move as one rigid body. A chain of separate bodies tied by soft
    /// constraints bends wherever a light part sits in it (a 6 g wire leg between a 400 g airframe and a
    /// wheel let the wheel toe in by degrees); real welded metal does not.
    /// <para>Every step the welded parts are gathered into a group: total mass, centre of mass and the full
    /// inertia tensor (each part's own inertia plus m·(|d|²·E − d·dᵀ)), and its velocity from the parts'
    /// momentum and angular momentum, so nothing done to a part between steps is lost. Forces and torques
    /// on any part (gravity, drag, wings, propellers, motors) are summed onto the group, which is integrated
    /// as one body (implicit gyroscopic term with the full tensor). Each part still collides with its own
    /// shape; the solver applies its contacts and the joints to other bodies to the group, with arms from
    /// the group's centre of mass. Afterwards every part's pose and velocity follow the group exactly.</para>
    /// <para>The welds inside a group are not solved, so their loads come from inverse dynamics: for the
    /// part(s) on one side of a weld, the change of momentum over the step minus every outside force
    /// (applied, contact, other joints) is what the weld carried. Recordings, strain readings and the FEM
    /// loads of the render see the same reaction forces and torques as before.</para>
    /// Breakable fastenings stay ordinary joints: their break test needs the joint's own constraint force.
    /// </summary>
    public sealed partial class PhysicsScene
    {
        private sealed class WeldGroup
        {
            public int Rep;
            public readonly List<int> Members = new List<int>();
            public readonly List<SceneJoint> Welds = new List<SceneJoint>();
            public double Mass;
            public Vec3 X, V, W;
            public Quat Q;
            public Mat3 InvIWorld;
            public readonly List<Vec3> Offset = new List<Vec3>();
            public readonly List<Quat> Rel = new List<Quat>();
            public readonly List<Vec3> P0 = new List<Vec3>();
            public readonly List<Vec3> L0 = new List<Vec3>();
        }

        private readonly List<WeldGroup> _weldGroups = new List<WeldGroup>();

        /// <summary>Solve welded parts as one rigid body (default). Off: every weld is a soft fixed joint, as before.</summary>
        public bool WeldsEnabled { get; set; } = true;
        private int[] _solverOf = new int[16];
        private WeldGroup?[] _groupOf = new WeldGroup?[16];
        private int[] _weldUf = new int[16];
        private bool[] _welded = new bool[16];

        /// <summary>Solver body a body's contacts and joints act on: the group's representative, or itself.</summary>
        private int SolverIndex(int body) => body < 0 ? body : _solverOf[body];

        private bool InWeldGroup(int body) => body >= 0 && _groupOf[body] != null;

        /// <summary>Both bodies are parts of the same welded group (they cannot move relative to each other).</summary>
        private bool SameWeldGroup(int a, int b) => a >= 0 && b >= 0 && _groupOf[a] != null && ReferenceEquals(_groupOf[a], _groupOf[b]);

        private bool IsActiveWeld(SceneJoint j)
        {
            if (!j.Weld || !j.Enabled || j.Broken || j.Kind != JointKind.Fixed) return false;
            if (!double.IsPositiveInfinity(j.BreakForce) || !double.IsPositiveInfinity(j.BreakTorque)) return false;
            int p = j.ParentSlot, c = j.ChildSlot;
            return p >= 0 && c >= 0 && p != c && !Bodies[p].IsStatic && !Bodies[c].IsStatic;
        }

        private int WeldFind(int i)
        {
            while (_weldUf[i] != i)
            {
                _weldUf[i] = _weldUf[_weldUf[i]];
                i = _weldUf[i];
            }

            return i;
        }

        private static Mat3 InertiaWorld(RigidBody b) => Mat3.RotatedDiagonal(b.Orientation * b.PrincipalAxes, b.Inertia);

        /// <summary>Gathers the welded groups for this step (after the slots and sleep states are known).</summary>
        private void BuildWelds(int count)
        {
            if (_solverOf.Length < count)
            {
                int cap = Math.Max(count, _solverOf.Length * 2);
                Array.Resize(ref _solverOf, cap);
                Array.Resize(ref _groupOf, cap);
                Array.Resize(ref _weldUf, cap);
                Array.Resize(ref _welded, cap);
            }

            for (int i = 0; i < count; i++)
            {
                _solverOf[i] = i;
                _groupOf[i] = null;
                _weldUf[i] = i;
                _welded[i] = false;
            }

            _weldGroups.Clear();
            if (!WeldsEnabled) return;
            bool any = false;
            foreach (var j in Joints)
            {
                if (!IsActiveWeld(j)) continue;
                int a = WeldFind(j.ParentSlot), b = WeldFind(j.ChildSlot);
                if (a != b) _weldUf[a] = b;
                _welded[j.ParentSlot] = _welded[j.ChildSlot] = true;
                any = true;
            }

            if (!any) return;
            var byRoot = new Dictionary<int, WeldGroup>();
            for (int i = 0; i < count; i++)
            {
                if (Bodies[i].IsStatic || !_welded[i]) continue;
                int r = WeldFind(i);
                if (!byRoot.TryGetValue(r, out var g)) byRoot[r] = g = new WeldGroup();
                g.Members.Add(i);
            }

            foreach (var j in Joints)
                if (IsActiveWeld(j) && byRoot.TryGetValue(WeldFind(j.ChildSlot), out var g))
                    g.Welds.Add(j);

            foreach (var g in byRoot.Values)
            {
                if (g.Members.Count < 2) continue;
                // A group sleeps or wakes as a whole.
                bool awake = false;
                foreach (int k in g.Members) awake |= !Asleep(k);
                if (awake)
                    foreach (int k in g.Members)
                        if (k < _asleep.Length) _asleep[k] = false;

                int rep = g.Members[0];
                double mass = 0;
                var x = Vec3.Zero;
                foreach (int k in g.Members)
                {
                    var b = Bodies[k];
                    if (b.Mass > Bodies[rep].Mass) rep = k;
                    mass += b.Mass;
                    x += b.Position * b.Mass;
                }

                g.Rep = rep;
                g.Mass = mass;
                g.X = x / mass;
                g.Q = Bodies[rep].Orientation;
                var momentum = Vec3.Zero;
                var angular = Vec3.Zero;
                foreach (int k in g.Members)
                {
                    var b = Bodies[k];
                    var d = b.Position - g.X;
                    g.Offset.Add(g.Q.InverseRotate(d));
                    g.Rel.Add(g.Q.Conjugate * b.Orientation);
                    var p = b.Velocity * b.Mass;
                    var l = InertiaWorld(b) * b.AngularVelocity;
                    momentum += p;
                    angular += l + d.Cross(p);
                    _solverOf[k] = rep;
                    _groupOf[k] = g;
                }

                g.V = momentum / mass;
                g.InvIWorld = GroupInertia(g).Inverse();
                g.W = g.InvIWorld * angular;
                _weldGroups.Add(g);
                SyncGroupMembers(g, true);
                // The parts' momenta once they move together: the start of the weld-load balance.
                foreach (int k in g.Members)
                {
                    var b = Bodies[k];
                    g.P0.Add(b.Velocity * b.Mass);
                    g.L0.Add(InertiaWorld(b) * b.AngularVelocity);
                }
            }
        }

        /// <summary>Inertia tensor of the group about its centre of mass, world axes.</summary>
        private Mat3 GroupInertia(WeldGroup g)
        {
            var I = Mat3.Zero;
            foreach (int k in g.Members)
            {
                var b = Bodies[k];
                var d = b.Position - g.X;
                I = I + InertiaWorld(b) + (Mat3.Identity * d.Dot(d) - Mat3.Outer(d, d)) * b.Mass;
            }

            return I;
        }

        /// <summary>Puts every part where the group's pose puts it (and, with <paramref name="poses"/> false, only sets velocities).</summary>
        private void SyncGroupMembers(WeldGroup g, bool poses)
        {
            for (int n = 0; n < g.Members.Count; n++)
            {
                var b = Bodies[g.Members[n]];
                if (poses)
                {
                    b.Position = g.X + g.Q.Rotate(g.Offset[n]);
                    b.Orientation = (g.Q * g.Rel[n]).Normalized();
                }

                b.Velocity = g.V + g.W.Cross(b.Position - g.X);
                b.AngularVelocity = g.W;
            }
        }

        /// <summary>
        /// Forces and torques on the parts → the group's velocity (symplectic Euler with the implicit gyroscopic
        /// term on the full tensor); the parts' accumulators are cleared and their impulses kept for the weld loads.
        /// </summary>
        private void IntegrateWeldVelocities(double h)
        {
            foreach (var g in _weldGroups)
            {
                if (Asleep(g.Rep)) continue;
                var force = Vec3.Zero;
                var torque = Vec3.Zero;
                foreach (int k in g.Members)
                {
                    var b = Bodies[k];
                    // A body's lumped linear damping acts on its centre-of-mass velocity only (as for a single body):
                    // here the group's, at the centre of mass, so it does not brake the rotation.
                    var damping = g.V * (-b.Mass * Math.Max(0, b.LinearDrag));
                    var t = b.Torque - b.AngularVelocity * Math.Max(0, b.AngularDrag);
                    force += b.Force + damping;
                    torque += t + (b.Position - g.X).Cross(b.Force);
                    b.StepApplied += (b.Force + damping) * h;
                    b.StepAppliedAngular += (t + (g.X - b.Position).Cross(damping)) * h;
                    b.Force = Vec3.Zero;
                    b.Torque = Vec3.Zero;
                }

                var I = GroupInertia(g);
                g.InvIWorld = I.Inverse();
                g.V += force / g.Mass * h;
                var w = g.W;
                var iw = I * w;
                if (iw.LengthSquared > 0)
                {
                    var jac = I + (Mat3.Skew(w) * I - Mat3.Skew(iw)) * h;
                    w -= jac.Inverse() * (w.Cross(iw) * h);
                }

                g.W = w + g.InvIWorld * torque * h;
                SyncGroupMembers(g, false);
            }
        }

        /// <summary>The group's solver state: one body at the centre of mass with the group's mass and inertia.</summary>
        private void LoadWeldStates()
        {
            foreach (var g in _weldGroups)
            {
                if (Asleep(g.Rep)) continue;
                ref var st = ref _state[g.Rep];
                st.X = g.X;
                st.V = g.V;
                st.W = g.W;
                st.InvMass = 1.0 / g.Mass;
                st.InvI = g.InvIWorld;
            }
        }

        /// <summary>Velocities from the solver → the group's pose; the parts follow.</summary>
        private void IntegrateWeldPositions(double h)
        {
            foreach (var g in _weldGroups)
            {
                if (Asleep(g.Rep)) continue;
                ref var st = ref _state[g.Rep];
                g.V = st.V;
                g.W = st.W;
                var x0 = g.X;
                var w = g.W;
                var dq = new Quat(0, w.X, w.Y, w.Z) * g.Q;
                g.Q = new Quat(g.Q.W + 0.5 * h * dq.W, g.Q.X + 0.5 * h * dq.X, g.Q.Y + 0.5 * h * dq.Y, g.Q.Z + 0.5 * h * dq.Z).Normalized();
                g.X += g.V * h;
                st.DX = g.X - x0;
                st.DTheta = g.W * h;
                SyncGroupMembers(g, true);
            }
        }

        /// <summary>Final velocities of the substep (after relaxation and restitution) → the parts.</summary>
        private void FinishWeldVelocities()
        {
            foreach (var g in _weldGroups)
            {
                if (Asleep(g.Rep)) continue;
                g.V = _state[g.Rep].V;
                g.W = _state[g.Rep].W;
                SyncGroupMembers(g, false);
            }
        }

        /// <summary>A joint's impulse on its bodies, kept for the weld loads of the groups they belong to.</summary>
        private void NoteJointImpulse(SceneJoint j, Vec3 force, Vec3 torque)
        {
            int c = j.ChildSlot, p = j.ParentSlot;
            if (!InWeldGroup(c) && !InWeldGroup(p)) return;
            var anchor = j.WorldAnchor;
            if (InWeldGroup(c))
            {
                var b = Bodies[c];
                b.StepJointImpulse += force;
                b.StepJointAngular += torque + (anchor - b.Position).Cross(force);
            }

            if (InWeldGroup(p))
            {
                var b = Bodies[p];
                b.StepJointImpulse -= force;
                b.StepJointAngular -= torque + (anchor - b.Position).Cross(force);
            }
        }

        /// <summary>
        /// Loads carried by the welds inside each group over the step (inverse dynamics on a spanning tree of
        /// its welds): for the parts beyond a weld, momentum change minus every outside impulse.
        /// </summary>
        private void WeldReactions(double dt)
        {
            foreach (var g in _weldGroups)
            {
                // A sleeping group keeps its last loads (its contacts are not solved while it sleeps).
                if (g.Welds.Count == 0 || Asleep(g.Rep)) continue;
                int m = g.Members.Count;
                var index = new Dictionary<int, int>(m);
                for (int n = 0; n < m; n++) index[g.Members[n]] = n;
                var fint = new Vec3[m];
                var tint0 = new Vec3[m];   // about the world origin
                for (int n = 0; n < m; n++)
                {
                    var b = Bodies[g.Members[n]];
                    var dp = b.Velocity * b.Mass - g.P0[n];
                    var dl = InertiaWorld(b) * b.AngularVelocity - g.L0[n];
                    var f = (dp - b.StepApplied - b.StepContactImpulse - b.StepJointImpulse) / dt;
                    var t = (dl - b.StepAppliedAngular - b.StepContactAngular - b.StepJointAngular) / dt;
                    fint[n] = f;
                    tint0[n] = t + b.Position.Cross(f);
                }

                // Spanning tree from the representative; a weld closing a loop shares no load (statically indeterminate).
                var parentEdge = new SceneJoint?[m];
                var parentOf = new int[m];
                var order = new List<int>(m);
                var seen = new bool[m];
                int root = index[g.Rep];
                seen[root] = true;
                parentOf[root] = -1;
                order.Add(root);
                for (int q = 0; q < order.Count; q++)
                {
                    int u = order[q], bu = g.Members[u];
                    foreach (var j in g.Welds)
                    {
                        int other = j.ParentSlot == bu ? j.ChildSlot : j.ChildSlot == bu ? j.ParentSlot : -1;
                        if (other < 0 || !index.TryGetValue(other, out int v) || seen[v]) continue;
                        seen[v] = true;
                        parentOf[v] = u;
                        parentEdge[v] = j;
                        order.Add(v);
                    }
                }

                var subF = (Vec3[])fint.Clone();
                var subT = (Vec3[])tint0.Clone();
                for (int q = order.Count - 1; q > 0; q--)
                {
                    int v = order[q], u = parentOf[v];
                    subF[u] += subF[v];
                    subT[u] += subT[v];
                }

                foreach (var j in g.Welds)
                {
                    j.ReactionForce = Vec3.Zero;
                    j.ReactionTorque = Vec3.Zero;
                }

                for (int q = 1; q < order.Count; q++)
                {
                    int v = order[q];
                    var j = parentEdge[v]!;
                    var anchor = j.WorldAnchor;
                    var force = subF[v];                           // on the parts beyond the weld (side v)
                    var torque = subT[v] - anchor.Cross(force);    // about the anchor
                    double sign = j.ChildSlot == g.Members[v] ? 1 : -1;
                    j.ReactionForce = force * sign;
                    j.ReactionTorque = torque * sign;
                    j.ForceFilter = force.Length;
                    j.TorqueFilter = torque.Length;
                    j.DriveTorque = 0;
                }
            }
        }

        private void ClearWeldAccumulators(SceneBody b)
        {
            b.StepApplied = Vec3.Zero;
            b.StepAppliedAngular = Vec3.Zero;
            b.StepContactAngular = Vec3.Zero;
            b.StepJointImpulse = Vec3.Zero;
            b.StepJointAngular = Vec3.Zero;
        }
    }
}
