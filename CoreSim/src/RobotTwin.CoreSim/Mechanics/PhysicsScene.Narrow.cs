using System;
using System.Diagnostics;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// [islands] The narrow phase on the cores. The sweep finds the work in order (each body against the ground, then every
    /// pair whose boxes meet); that list is cut into runs, each run is tested on a core with its own working state and
    /// contact list, and the lists are put together in the runs' order: the same contacts in the same order as one test
    /// after the other. A test reads the bodies, the materials and the warm starts only; what it writes is its own.
    /// </summary>
    public sealed partial class PhysicsScene
    {
        /// <summary>What a contact test works with: one for the scene's own thread, one for each run of the narrow phase.</summary>
        private sealed class NarrowWork
        {
            internal NarrowWork(int contacts) => Contacts = new Contact[contacts];

            internal Contact[] Contacts;
            internal int Count;
            internal Vec3[] Va = new Vec3[32], Vb = new Vec3[32];
            internal Vec3[] Na = new Vec3[32], Nb = new Vec3[32];
            internal double[] Oa = new double[32], Ob = new double[32];
            internal readonly Vec3[] Candidates = new Vec3[16];
            internal Vec3[] PieceCentres = Array.Empty<Vec3>();

            /// <summary>Added to contact feature ids while a pair of convex pieces of concave bodies is tested (keeps warm starts apart).</summary>
            internal int FeatureBase;

            internal SoilState? GroundSoil;   // [terrain] the soil of the body tested against the ground
        }

        [ThreadStatic] private static NarrowWork? t_narrow;
        private readonly NarrowWork _narrowMain = new NarrowWork(64);
        private NarrowWork[] _narrowRuns = Array.Empty<NarrowWork>();

        /// <summary>The working state of the test running on this thread (the scene's own unless the work is shared out).</summary>
        private NarrowWork Narrow => _narrowShared ? t_narrow ?? _narrowMain : _narrowMain;

        private bool _narrowShared;   // the narrow phase is running side by side now

        /// <summary>A piece of narrow-phase work: body A against the ground (Kind 0), the pair (A, B) (1), or (B, A) (2).</summary>
        private struct NarrowOp
        {
            public int A, B;
            public byte Kind;
        }

        private NarrowOp[] _narrowOps = new NarrowOp[64];
        private int _narrowOpCount;

        // side by side or one after the other, whichever the last measurement found cheaper (the result is the same)
        private const int NarrowFromBodies = 24, NarrowMeasure = 64, NarrowRemeasureEvery = 2048, NarrowSettle = 256;
        private long _narrowSubsteps;
        private double _narrowCostInTurn = double.NaN, _narrowCostSideBySide = double.NaN;
        private double _narrowMeasured;
        private int _narrowMeasuredCount;
        private bool _narrowMeasuringSideBySide;

        /// <summary>[islands] Tests: always (true) or never (false) side by side, instead of by measurement.</summary>
        internal bool? ForceNarrowSideBySide { get; set; }

        /// <summary>[islands] Whether the last substep's narrow phase ran side by side.</summary>
        public bool NarrowSideBySide { get; private set; }

        private void AddNarrowOp(int a, int b, byte kind)
        {
            if (_narrowOpCount == _narrowOps.Length) Array.Resize(ref _narrowOps, _narrowOps.Length * 2);
            _narrowOps[_narrowOpCount++] = new NarrowOp { A = a, B = b, Kind = kind };
        }

        /// <summary>The collected work, cut into runs on the cores; the runs' contacts are then joined into the scene's in order.</summary>
        private void RunNarrowOps(double h)
        {
            int ops = _narrowOpCount;
            // one run per thread (kept small: each holds its own contact list, which grows only as far as its work needs);
            // all made at once, so the scene does not keep adding them as its work grows
            if (_narrowRuns.Length == 0)
            {
                _narrowRuns = new NarrowWork[WorkTeam.Shared.Workers + 1];
                for (int i = 0; i < _narrowRuns.Length; i++) _narrowRuns[i] = new NarrowWork(16);
            }

            int runs = Math.Max(1, Math.Min(ops, _narrowRuns.Length));
            _narrowShared = true;
            try
            {
                RunNarrowRuns(h, ops, runs);
            }
            finally
            {
                _narrowShared = false;
            }

            var main = _narrowMain;
            int total = main.Count;
            for (int r = 0; r < runs; r++) total += _narrowRuns[r].Count;
            if (main.Contacts.Length < total) Array.Resize(ref main.Contacts, Math.Max(total, main.Contacts.Length * 2));
            for (int r = 0; r < runs; r++)
            {
                var w = _narrowRuns[r];
                int at = main.Count;
                Array.Copy(w.Contacts, 0, main.Contacts, at, w.Count);
                // a tread's contacts name their patch by index: from the run's list to the scene's
                for (int t = 0; t < w.Count; t++)
                    if (main.Contacts[at + t].TreadCount != 0) main.Contacts[at + t].TreadFirst += at;
                main.Count += w.Count;
            }
        }

        private void RunNarrowRuns(double h, int ops, int runs)
        {
            WorkTeam.Each(runs, r =>
            {
                var w = _narrowRuns[r];
                w.Count = 0;
                int from = (int)((long)ops * r / runs), to = (int)((long)ops * (r + 1) / runs);
                var before = t_narrow;
                t_narrow = w;
                try
                {
                    for (int i = from; i < to; i++)
                    {
                        var op = _narrowOps[i];
                        if (op.Kind == 0) CollideGroundSoil(op.A, h);
                        else if (op.Kind == 1) CollidePair(op.A, op.B, h);
                        else CollidePair(op.B, op.A, h);
                    }
                }
                finally
                {
                    t_narrow = before;
                }
            });
        }

        private bool NarrowBesideEachOther(int bodies)
        {
            if (bodies < NarrowFromBodies || WorkTeam.Shared.Workers == 0) return false;
            if (ForceNarrowSideBySide is { } forced) return forced;
            if (_narrowSubsteps < NarrowSettle || double.IsNaN(_narrowCostInTurn)) return false;
            if (double.IsNaN(_narrowCostSideBySide)) return true;
            bool better = _narrowCostSideBySide < 0.9 * _narrowCostInTurn;
            return (_narrowSubsteps - NarrowSettle) % NarrowRemeasureEvery < NarrowMeasure ? !better : better;
        }

        private void NoteNarrowCost(int bodies, bool sideBySide, long ticks)
        {
            if (bodies < NarrowFromBodies) return;
            _narrowSubsteps++;
            if (_narrowSubsteps <= NarrowSettle) return;
            if (sideBySide != _narrowMeasuringSideBySide)
            {
                _narrowMeasured = 0;
                _narrowMeasuredCount = 0;
                _narrowMeasuringSideBySide = sideBySide;
            }

            _narrowMeasured += ticks;
            if (++_narrowMeasuredCount < NarrowMeasure) return;
            if (sideBySide) _narrowCostSideBySide = _narrowMeasured / _narrowMeasuredCount;
            else _narrowCostInTurn = _narrowMeasured / _narrowMeasuredCount;
            _narrowMeasured = 0;
            _narrowMeasuredCount = 0;
        }
    }
}
