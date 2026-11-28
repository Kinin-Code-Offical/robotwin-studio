using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// [couple] A mechanical coupling between two joints: <see cref="B"/>'s speed is <see cref="Ratio"/> times <see cref="A"/>'s
    /// (rad/s or m/s each). A gear pair is a hinge to a hinge with a negative ratio (the teeth turn it the other way), a belt
    /// one with a positive ratio, a lead screw a hinge to a slider with the pitch over 2π. It is a constraint, not a motor:
    /// torque goes through it both ways, so the driven side's load is felt by the driving side. What the mesh loses
    /// (<see cref="Efficiency"/>) is friction at the mesh, proportional to the load it carries: driving through it costs
    /// 1/η of the ideal torque, and the load turning it back gets only (2 − 1/η) of it through, nothing at η ≤ 0.5, which is
    /// why a fine lead screw holds its load with the motor off.
    /// </summary>
    public sealed class SceneCoupling
    {
        internal SceneCoupling(string id, SceneJoint a, SceneJoint b, double ratio)
        {
            Id = id;
            A = a;
            B = b;
            Ratio = ratio;
            Offset = ratio * a.Angle - b.Angle;
        }

        public string Id { get; }

        public SceneJoint A { get; }

        public SceneJoint B { get; }

        public double Ratio { get; set; }

        /// <summary>
        /// [cam] A coupling that is no fixed ratio: B's place as a function of A's (a cam's lift over its shaft's angle) and
        /// its slope there. Each step the constraint is laid along it: the ratio is the slope, the target the lift.
        /// </summary>
        public Func<double, (double Value, double Slope)>? Profile { get; set; }

        /// <summary>Share of the power the mesh passes on (0.05 … 1; 1 = lossless).</summary>
        public double Efficiency
        {
            get => _efficiency;
            set => _efficiency = Math.Max(0.05, Math.Min(1, value));
        }

        private double _efficiency = 1;

        /// <summary>
        /// Free play, all of it, at <see cref="B"/> (rad or m): printed gears have a few tenths of a degree, a lead screw's
        /// nut a tenth of a millimetre. Inside it the two move apart; at either flank the teeth push.
        /// </summary>
        public double Backlash { get; set; }

        /// <summary>
        /// How much the coupling gives under load, at <see cref="B"/> (N·m/rad or N/m): a belt's stretch, a long shaft's
        /// twist. 0 = rigid.
        /// </summary>
        public double Stiffness { get; set; }

        /// <summary>The impulse the upper flank carries (the other side of the play), ≥ 0.</summary>
        internal double ImpulseHi;

        /// <summary>Where the two joints were when coupled: the teeth mesh from there on.</summary>
        internal double Offset;

        internal double Impulse;

        /// <summary>The generalised force the coupling carries on <see cref="B"/> (N·m or N), averaged over the last step.</summary>
        public double Load { get; internal set; }

        internal double StepImpulse;
    }

    public sealed partial class PhysicsScene
    {
        private struct CouplingRow
        {
            public SceneCoupling Coupling;
            public int Count;
            public int B0, B1, B2, B3;
            public Vec3 L0, L1, L2, L3, A0, A1, A2, A3;
            public double Mass, Err;
            public double Half;   // half the play; 0 = none
            public double Gamma, BiasCoef;   // a soft (stretching) coupling; Gamma 0 = rigid
        }

        private CouplingRow[] _couplingRows = new CouplingRow[4];
        private int _couplingCount;

        public List<SceneCoupling> Couplings { get; } = new List<SceneCoupling>();

        /// <summary>Couples joint <paramref name="b"/>'s speed to <paramref name="ratio"/> × joint <paramref name="a"/>'s, from where they are now.</summary>
        public SceneCoupling AddCoupling(SceneJoint a, SceneJoint b, double ratio, string? id = null)
        {
            if (a.Kind is not (JointKind.Hinge or JointKind.Slider) || b.Kind is not (JointKind.Hinge or JointKind.Slider)) throw new ArgumentException("Sabit bir eklem bağlanamaz: dişli, kayış ve vida menteşe ya da kızak ister.");
            var c = new SceneCoupling(id ?? "coupling" + Couplings.Count, a, b, ratio);
            Couplings.Add(c);
            return c;
        }

        /// <summary>
        /// The joint's speed as a Jacobian scaled by <paramref name="scale"/>, added into the row: a hinge's relative angular
        /// velocity about its axis, a slider's relative velocity along it at its anchor.
        /// </summary>
        private void AddJointSpeed(ref CouplingRow row, SceneJoint j, double scale)
        {
            int p = j.ParentSlot >= 0 ? SolverIndex(j.ParentSlot) : -1, c = SolverIndex(j.ChildSlot);
            var qP = j.ParentBody?.Orientation ?? Quat.Identity;
            var axis = qP.Rotate(j.AxisP);
            if (j.Kind == JointKind.Slider)
            {
                var anchor = j.ChildBody.Position + j.ChildBody.Orientation.Rotate(j.AnchorC);
                Add(ref row, c, axis * scale, (anchor - _state[c].X).Cross(axis) * scale);
                if (p >= 0) Add(ref row, p, axis * -scale, (anchor - _state[p].X).Cross(axis) * -scale);
            }
            else
            {
                Add(ref row, c, Vec3.Zero, axis * scale);
                if (p >= 0) Add(ref row, p, Vec3.Zero, axis * -scale);
            }
        }

        /// <summary>Adds a body's terms to the row, merging a body both joints share (a gearbox's housing).</summary>
        private static void Add(ref CouplingRow r, int body, Vec3 l, Vec3 a)
        {
            if (r.Count > 0 && r.B0 == body) { r.L0 += l; r.A0 += a; return; }
            if (r.Count > 1 && r.B1 == body) { r.L1 += l; r.A1 += a; return; }
            if (r.Count > 2 && r.B2 == body) { r.L2 += l; r.A2 += a; return; }
            if (r.Count > 3 && r.B3 == body) { r.L3 += l; r.A3 += a; return; }
            switch (r.Count)
            {
                case 0: r.B0 = body; r.L0 = l; r.A0 = a; break;
                case 1: r.B1 = body; r.L1 = l; r.A1 = a; break;
                case 2: r.B2 = body; r.L2 = l; r.A2 = a; break;
                default: r.B3 = body; r.L3 = l; r.A3 = a; break;
            }

            r.Count++;
        }

        private double Term(int body, Vec3 l, Vec3 a) => l.Dot(l) * _state[body].InvMass + a.Dot(_state[body].InvI * a);

        private double Velocity(int body, Vec3 l, Vec3 a) => l.Dot(_state[body].V) + a.Dot(_state[body].W);

        private void Push(int body, Vec3 l, Vec3 a, double impulse)
        {
            ref var s = ref _state[body];
            s.V += l * (impulse * s.InvMass);
            s.W += s.InvI * (a * impulse);
        }

        /// <summary>The couplings' rows for this substep (after the joints have measured their angles), warm-started.</summary>
        private double _couplingStep = 1e-3;

        private void PrepareCouplings(double h)
        {
            _couplingCount = 0;
            _couplingStep = h;
            if (Couplings.Count == 0) return;
            if (_couplingRows.Length < Couplings.Count) Array.Resize(ref _couplingRows, Couplings.Count * 2);
            foreach (var c in Couplings)
            {
                c.StepImpulse = 0;
                if (!c.A.Enabled || c.A.Broken || !c.B.Enabled || c.B.Broken) continue;
                if (JointAsleep(c.A) && JointAsleep(c.B)) continue;
                if (c.Profile != null)
                {
                    var (lift, slope) = c.Profile(c.A.Angle);   // [cam] linearised where the shaft is now
                    c.Ratio = slope;
                    c.Offset = slope * c.A.Angle - lift;
                }

                var row = new CouplingRow { Coupling = c };
                AddJointSpeed(ref row, c.A, c.Ratio);
                AddJointSpeed(ref row, c.B, -1);
                double k = 0;
                if (row.Count > 0) k += Term(row.B0, row.L0, row.A0);
                if (row.Count > 1) k += Term(row.B1, row.L1, row.A1);
                if (row.Count > 2) k += Term(row.B2, row.L2, row.A2);
                if (row.Count > 3) k += Term(row.B3, row.L3, row.A3);
                if (k <= 1e-15) continue;   // both sides held: nothing to solve
                row.Mass = 1 / k;
                row.Err = c.Ratio * c.A.Angle - c.B.Angle - c.Offset;
                row.Half = Math.Max(0, c.Backlash) / 2;
                if (c.Stiffness > 0)
                {
                    // A belt's stretch: a spring on the error, lightly damped (5 % of critical) as a real belt is.
                    double damping = 2 * 0.05 * Math.Sqrt(c.Stiffness * row.Mass), den = damping + h * c.Stiffness;
                    row.Gamma = 1 / (h * den);
                    row.BiasCoef = c.Stiffness / den;
                }

                if (row.Half <= 0) c.ImpulseHi = 0;
                _couplingRows[_couplingCount++] = row;
                double warm = c.Impulse - c.ImpulseHi;
                if (warm != 0) Apply(ref _couplingRows[_couplingCount - 1], warm);
            }
        }

        private void Apply(ref CouplingRow r, double impulse)
        {
            if (r.Count > 0) Push(r.B0, r.L0, r.A0, impulse);
            if (r.Count > 1) Push(r.B1, r.L1, r.A1, impulse);
            if (r.Count > 2) Push(r.B2, r.L2, r.A2, impulse);
            if (r.Count > 3) Push(r.B3, r.L3, r.A3, impulse);
        }

        private void SolveCouplings(bool useBias, double biasRate, double massScale, double impulseScale, int k = -1)
        {
            var (cf, ct, ci) = IslandRange(k, _couplingCount, _islCouplingStart, _islCouplingIndex);
            for (int t = cf; t < ct; t++)
            {
                ref var r = ref _couplingRows[ci == null ? t : ci[t]];
                double cdot = 0;
                if (r.Count > 0) cdot += Velocity(r.B0, r.L0, r.A0);
                if (r.Count > 1) cdot += Velocity(r.B1, r.L1, r.A1);
                if (r.Count > 2) cdot += Velocity(r.B2, r.L2, r.A2);
                if (r.Count > 3) cdot += Velocity(r.B3, r.L3, r.A3);
                var c = r.Coupling;
                double lambda;
                if (r.Half <= 0 && r.Gamma <= 0)
                {
                    // Rigid, no play: an equality.
                    double acc = c.Impulse;
                    lambda = useBias ? -r.Mass * massScale * (cdot + biasRate * r.Err) - impulseScale * acc : -r.Mass * cdot;
                    c.Impulse = acc + lambda;
                }
                else if (r.Gamma > 0)
                {
                    // Stretching: a spring on the error beyond the play, pushing only from the flank it is past.
                    double excess = r.Err - Math.Max(-r.Half, Math.Min(r.Half, r.Err));
                    double acc = c.Impulse;
                    double next = acc - (cdot + r.BiasCoef * excess + r.Gamma * acc) / (1 / r.Mass + r.Gamma);
                    if (r.Half > 0) next = excess > 0 ? Math.Min(0, next) : excess < 0 ? Math.Max(0, next) : 0;
                    lambda = next - acc;
                    c.Impulse = next;
                }
                else
                {
                    // Play: two flanks, each a one-sided limit; the gap to it closes no faster than one step allows.
                    double gapLo = r.Err + r.Half, gapHi = r.Half - r.Err;
                    double biasLo = gapLo > 0 ? gapLo / _couplingStep : useBias ? biasRate * gapLo : 0;
                    double biasHi = gapHi > 0 ? gapHi / _couplingStep : useBias ? biasRate * gapHi : 0;
                    double lo = Math.Max(0, c.Impulse - r.Mass * (cdot + biasLo));
                    double dLo = lo - c.Impulse;
                    c.Impulse = lo;
                    cdot += dLo / r.Mass;
                    double hi = Math.Max(0, c.ImpulseHi - r.Mass * (-cdot + biasHi));
                    double dHi = hi - c.ImpulseHi;
                    c.ImpulseHi = hi;
                    lambda = dLo - dHi;
                }

                c.StepImpulse += lambda;
                Apply(ref r, lambda);
            }
        }

        private void FinishCouplings(double h)
        {
            foreach (var c in Couplings) c.A.MeshFriction = 0;
            foreach (var c in Couplings)
            {
                c.Load = (c.Impulse - c.ImpulseHi) / h;
                // The mesh's friction for the next substep: Coulomb, proportional to the torque (force) it carries on the
                // driving joint. On either joint it is the same, the two move together.
                if (c.Efficiency < 1) c.A.MeshFriction += (1 / c.Efficiency - 1) * Math.Abs(c.Load * c.Ratio);
            }
        }
    }
}
